using Blueprint.App;

namespace Blueprint.App.Backend;

/// <summary>
/// Transfers data table-by-table from source to target backend.
/// Uses canonical types as intermediate representation to ensure
/// correct type mapping between any source/target backend combination.
/// </summary>
public sealed class DatabaseTransferService
{
    public TransferResult Transfer(
        IBackendConnector source,
        IBackendConnector target,
        IReadOnlyList<string> tableNames,
        IProgress<(int TableCurrent, int TableTotal, string Table, int RowCurrent, int RowTotal)>? progress = null)
    {
        var errors  = new List<(string, string)>();
        int skipped = 0;
        int ok      = 0;

        for (int i = 0; i < tableNames.Count; i++)
        {
            var table = tableNames[i];
            progress?.Report((i + 1, tableNames.Count, table, 0, 0));

            try
            {
                LogService.Info("Transfer", $"Starting table '{table}' ({i + 1}/{tableNames.Count})");
                var srcCols    = source.GetColumnNames(table)
                                       .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var tgtColsList = target.GetColumnNames(table);
                var tgtCols    = tgtColsList.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var common     = srcCols.Where(c => tgtCols.Contains(c)).ToList();

                // Use target-side column name casing for INSERT statements.
                // Backends like PostgreSQL store column names in lowercase and are
                // case-sensitive when identifiers are quoted — so we must pass the
                // exact casing the target knows about, not the source casing.
                var tgtCasingMap    = tgtColsList.ToDictionary(c => c, c => c, StringComparer.OrdinalIgnoreCase);
                var commonForTarget = common.Select(c => tgtCasingMap.TryGetValue(c, out var tc) ? tc : c).ToList();

                if (common.Count == 0)
                {
                    skipped++;
                    continue;
                }

                // Get canonical types for source and target columns
                var sourceTypes = source.GetColumnTypes(table);
                var targetTypes = target.GetColumnTypes(table);

                // Read rows and normalize each value to canonical C# type
                var rows = source.ReadAll(table, common)
                                 .Select(r => CanonicalizeRow(r, common, sourceTypes, targetTypes))
                                 .ToList();

                int rowTotal = rows.Count;
                progress?.Report((i + 1, tableNames.Count, table, 0, rowTotal));

                // Wrap rows in a counting enumerable to report per-row progress
                int rowsDone  = 0;
                int reportEvery = Math.Max(1, rowTotal / 20); // report ~20 times per table
                IEnumerable<IReadOnlyDictionary<string, object?>> reportingRows = rows.Select(r =>
                {
                    rowsDone++;
                    if (rowsDone % reportEvery == 0 || rowsDone == rowTotal)
                        progress?.Report((i + 1, tableNames.Count, table, rowsDone, rowTotal));
                    return r;
                });

                target.BeginTransaction();
                try
                {
                    target.DeleteAll(table);
                    target.InsertRows(table, commonForTarget, reportingRows);
                    target.Commit();
                    ok++;
                }
                catch (Exception bulkEx)
                {
                    target.Rollback();
                    LogService.Warning("Transfer",
                        $"Table '{table}': bulk insert failed ({bulkEx.Message}), retrying row-by-row.");

                    // Bulk insert failed — retry row-by-row to isolate bad rows
                    // (e.g. NOT NULL constraint on source NULLs, type mismatches, etc.)
                    int rowsInserted = 0, rowsSkipped = 0;
                    string? firstRowError = null;
                    target.BeginTransaction();
                    try
                    {
                        target.DeleteAll(table);
                        foreach (var singleRow in rows)
                        {
                            try
                            {
                                target.InsertRows(table, commonForTarget, new[] { singleRow });
                                rowsInserted++;
                            }
                            catch (Exception rowEx)
                            {
                                firstRowError ??= rowEx.Message;
                                // Last resort: replace NULLs with type-appropriate defaults
                                // so NOT NULL constraints don't block migration
                                try
                                {
                                    var coerced = CoerceNulls(singleRow, common, sourceTypes);
                                    target.InsertRows(table, commonForTarget, new[] { coerced });
                                    rowsInserted++;
                                    LogService.Warning("Transfer",
                                        $"Table '{table}': row inserted with NULL→default substitution.");
                                }
                                catch
                                {
                                    rowsSkipped++;
                                }
                            }
                        }
                        target.Commit();
                        ok++;
                        if (rowsSkipped > 0)
                        {
                            LogService.Warning("Transfer",
                                $"Table '{table}': {rowsInserted} rows OK, {rowsSkipped} skipped. First error: {firstRowError}");
                            errors.Add((table,
                                $"{rowsSkipped} row(s) skipped — {firstRowError}"));
                        }
                    }
                    catch (Exception retryEx)
                    {
                        target.Rollback();
                        throw retryEx;
                    }
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Transfer", $"Error transferring table '{table}'", ex);
                errors.Add((table, ex.Message));
            }
        }

        return new TransferResult(ok, skipped, errors);
    }

    /// <summary>
    /// Replaces NULL values with type-appropriate defaults so NOT NULL constraints
    /// in the target don't block migration of rows that have NULLs in the source.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> CoerceNulls(
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<string, CanonicalType> types)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
        {
            var val = row.TryGetValue(col, out var v) ? v : null;
            if (val is null)
            {
                var ct = types.TryGetValue(col, out var t) ? t : CanonicalType.Unknown;
                val = ct switch
                {
                    CanonicalType.Int32    => (object)0,
                    CanonicalType.Int64    => 0L,
                    CanonicalType.Double   => 0.0,
                    CanonicalType.Decimal  => 0m,
                    CanonicalType.Boolean  => false,
                    CanonicalType.DateTime => DateTime.MinValue,
                    CanonicalType.Date     => DateOnly.MinValue,
                    CanonicalType.Bytes    => Array.Empty<byte>(),
                    CanonicalType.Guid     => Guid.Empty,
                    _                     => ""   // Text, Unknown
                };
            }
            result[col] = val;
        }
        return result;
    }

    /// <summary>
    /// Converts each value in a row to its canonical C# type based on the source column schema.
    /// When source and target column types differ (e.g. source INTEGER, target BOOLEAN),
    /// applies a second-pass coercion so the value fits the target's expected representation.
    /// This handles legacy data where -1 means True (Access/VBA convention) but the target
    /// column is BOOLEAN — SQL_BIT only accepts 0/1, so -1 must be normalised to true (1).
    /// </summary>
    private static IReadOnlyDictionary<string, object?> CanonicalizeRow(
        IReadOnlyDictionary<string, object?> row,
        IReadOnlyList<string> columns,
        IReadOnlyDictionary<string, CanonicalType> sourceTypes,
        IReadOnlyDictionary<string, CanonicalType> targetTypes)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
        {
            var val        = row.TryGetValue(col, out var v) ? v : null;
            var srcType    = sourceTypes.TryGetValue(col, out var s) ? s : CanonicalType.Unknown;
            var canonical  = TypeMappings.ToCanonicalValue(val, srcType);

            // Cross-type coercion: if target expects Boolean but source delivered a non-bool
            // (e.g. Int64 -1 from an INTEGER column), normalise to bool so the target
            // receives 0/1 instead of a raw integer that may be misread by ODBC drivers.
            var tgtType = targetTypes.TryGetValue(col, out var t) ? t : CanonicalType.Unknown;
            if (tgtType == CanonicalType.Boolean && canonical is not bool)
                canonical = TypeMappings.ToCanonicalValue(canonical, CanonicalType.Boolean);

            result[col] = canonical;
        }
        return result;
    }
}
