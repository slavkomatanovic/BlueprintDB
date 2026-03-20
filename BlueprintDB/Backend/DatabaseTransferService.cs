using Blueprint.App;

namespace Blueprint.App.Backend;

/// <summary>
/// Transfers data table-by-table from source to target backend.
/// Algorithm: for each table — find common columns, DELETE target rows, INSERT source rows, all in one transaction.
/// </summary>
public sealed class DatabaseTransferService
{
    public TransferResult Transfer(
        IBackendConnector source,
        IBackendConnector target,
        IReadOnlyList<string> tableNames,
        IProgress<(int Current, int Total, string Table)>? progress = null)
    {
        var errors  = new List<(string, string)>();
        int skipped = 0;
        int ok      = 0;

        for (int i = 0; i < tableNames.Count; i++)
        {
            var table = tableNames[i];
            progress?.Report((i + 1, tableNames.Count, table));

            try
            {
                var srcCols = source.GetColumnNames(table)
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var tgtCols = target.GetColumnNames(table)
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var common  = srcCols
                    .Where(c => tgtCols.Contains(c))
                    .ToList();

                if (common.Count == 0)
                {
                    skipped++;
                    continue;
                }

                var rows = source.ReadAll(table, common);

                target.BeginTransaction();
                try
                {
                    target.DeleteAll(table);
                    target.InsertRows(table, common, rows);
                    target.Commit();
                    ok++;
                }
                catch
                {
                    target.Rollback();
                    throw;
                }
            }
            catch (Exception ex)
            {
                LogService.Error("Transfer", "Error in DatabaseTransferService", ex);
                errors.Add((table, ex.Message));
            }
        }

        return new TransferResult(ok, skipped, errors);
    }
}
