using Blueprint.App.Backend;
using Blueprint.App.Models;

namespace Blueprint.App;

/// <summary>
/// Result of a single one-shot schema sync run (used by batch processing).
/// </summary>
public record BatchSyncResult(
    string Path,
    BackendType BackendType,
    int TablesCreated,
    int ColumnsAdded,
    int FksAdded,
    int TablesDropped,
    int ColumnsDropped,
    Exception? Error = null)
{
    public bool   Success    => Error == null;
    public string StatusText => Error != null
        ? $"Error: {Error.Message}"
        : $"+{TablesCreated}t  +{ColumnsAdded}c  +{FksAdded}fk" +
          (TablesDropped + ColumnsDropped > 0
              ? $"  -{TablesDropped}t  -{ColumnsDropped}c"
              : "");
}

/// <summary>
/// Runs a complete analyse + apply schema sync in one step.
/// Used by BatchSchemaSyncWindow; the interactive SchemaSyncWizardWindow
/// keeps its own two-phase approach (analyse → show diff → apply).
/// </summary>
public static class SchemaSyncService
{
    public static async Task<BatchSyncResult> RunAsync(
        int programId, BackendType backendType, string connectionString, bool deleteRedundant)
    {
        int tablesCreated = 0, columnsAdded = 0, fksAdded = 0,
            tablesDropped = 0, columnsDropped = 0;

        try
        {
            await Task.Run(() =>
            {
                using var connector = BackendConnectorFactory.Create(connectionString, backendType);
                connector.Open();

                using var db = new BlueprintDbContext();

                var bpTables  = db.Tabeles.Where(t => t.Idprograma == programId && t.Skriven != true).ToList();
                var liveTables = connector.GetTableNames();
                var liveNames  = liveTables.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var bpNames    = bpTables.Select(t => t.Nazivtabele!).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Očisti stare surplus zapise za ovaj program
                var oldTabele = db.Tabelenoves.Where(t => t.Idprograma == programId).ToList();
                var oldIds    = oldTabele.Select(t => t.Idtabele).ToList();
                db.Kolonenoves.RemoveRange(db.Kolonenoves.Where(k => oldIds.Contains(k.Idtabele)));
                db.Tabelenoves.RemoveRange(oldTabele);
                db.SaveChanges();

                // ── Collect diff ─────────────────────────────────────────────

                var tablesToCreate  = new List<(string Name, List<ColumnSchema> Cols)>();
                var columnsToAdd    = new List<(string Table, ColumnSchema Col)>();
                var surplusColumns  = new List<(string Table, string Column)>();
                var surplusTables   = new List<string>();

                // Tables missing from live → CREATE TABLE
                foreach (var bpTable in bpTables.Where(t => !liveNames.Contains(t.Nazivtabele!)))
                {
                    var rawCols = db.Kolones
                        .Where(k => k.Idtabele == bpTable.Idtabele && k.Skriven != true)
                        .OrderBy(k => k.Idkolone)
                        .ToList();
                    var cols = rawCols.Select(k => new ColumnSchema(
                        k.Nazivkolone!, k.Tippodatka ?? "", k.Allownull == "No", k.Key,
                        int.TryParse(k.Fieldsize, out var fs) ? fs : 0)).ToList();
                    tablesToCreate.Add((bpTable.Nazivtabele!, cols));
                }

                // For tables in both: missing columns → ADD COLUMN; extra columns → surplus
                foreach (var bpTable in bpTables.Where(t => liveNames.Contains(t.Nazivtabele!)))
                {
                    var liveColNames    = connector.GetColumnNames(bpTable.Nazivtabele!).ToList();
                    var liveColNamesSet = liveColNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var bpCols = db.Kolones
                        .Where(k => k.Idtabele == bpTable.Idtabele && k.Skriven != true)
                        .OrderBy(k => k.Idkolone).ToList();

                    foreach (var bpCol in bpCols.Where(k => !liveColNamesSet.Contains(k.Nazivkolone!)))
                    {
                        _ = int.TryParse(bpCol.Fieldsize, out var fs);
                        columnsToAdd.Add((bpTable.Nazivtabele!, new ColumnSchema(
                            bpCol.Nazivkolone!, bpCol.Tippodatka ?? "", bpCol.Allownull == "No", bpCol.Key, fs)));
                    }

                    foreach (var liveCol in liveColNames.Where(c =>
                        !bpCols.Any(k => string.Equals(k.Nazivkolone, c, StringComparison.OrdinalIgnoreCase))))
                    {
                        surplusColumns.Add((bpTable.Nazivtabele!, liveCol));

                        // Record surplus in Blueprint metadata
                        var tNova = db.Tabelenoves.FirstOrDefault(t =>
                            t.Idprograma == programId &&
                            t.Nazivtabele == bpTable.Nazivtabele &&
                            t.Cijelatabela == false);
                        if (tNova == null)
                        {
                            tNova = new Tabelenove
                            {
                                Idprograma   = programId,
                                Nazivtabele  = bpTable.Nazivtabele,
                                Cijelatabela = false,
                                Datumupisa   = DateTime.Now,
                                Skriven      = false
                            };
                            db.Tabelenoves.Add(tNova);
                            db.SaveChanges();
                        }
                        db.Kolonenoves.Add(new Kolonenove
                        {
                            Idtabele    = tNova.Idtabele,
                            Nazivkolone = liveCol,
                            Datumupisa  = DateTime.Now,
                            Skriven     = false
                        });
                        db.SaveChanges();
                    }
                }

                // Tables in live but not in Blueprint → surplus whole table
                foreach (var extra in liveTables.Where(t => !bpNames.Contains(t)))
                {
                    surplusTables.Add(extra);
                    var tNova = new Tabelenove
                    {
                        Idprograma   = programId,
                        Nazivtabele  = extra,
                        Cijelatabela = true,
                        Datumupisa   = DateTime.Now,
                        Skriven      = false
                    };
                    db.Tabelenoves.Add(tNova);
                    db.SaveChanges();
                    foreach (var col in connector.GetColumnNames(extra))
                        db.Kolonenoves.Add(new Kolonenove
                        {
                            Idtabele    = tNova.Idtabele,
                            Nazivkolone = col,
                            Datumupisa  = DateTime.Now,
                            Skriven     = false
                        });
                    db.SaveChanges();
                }

                // FK constraints
                var fksToAdd = new List<Relacije>();
                if (connector.SupportsForeignKeys)
                {
                    var liveFks    = connector.GetForeignKeys();
                    var bpRelacije = db.Relacijes
                        .Where(r => r.Idprograma == programId && r.Skriven != true).ToList();

                    foreach (var rel in bpRelacije.Where(r =>
                        !string.IsNullOrEmpty(r.Tabelad) &&
                        !string.IsNullOrEmpty(r.Polje)   &&
                        !string.IsNullOrEmpty(r.Tabelal)))
                    {
                        if (!liveFks.Any(fk =>
                            string.Equals(fk.ChildTable,  rel.Tabelad, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(fk.ChildColumn, rel.Polje,   StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(fk.ParentTable, rel.Tabelal, StringComparison.OrdinalIgnoreCase)))
                            fksToAdd.Add(rel);
                    }
                }

                // ── Apply ────────────────────────────────────────────────────

                var liveNow = connector.GetTableNames().ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var (name, cols) in tablesToCreate)
                {
                    if (liveNow.Contains(name)) continue;
                    connector.CreateTable(name, cols);
                    liveNow.Add(name);
                    tablesCreated++;
                }

                foreach (var (table, col) in columnsToAdd)
                {
                    var liveNowCols = connector.GetColumnNames(table)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (!liveNowCols.Contains(col.Name))
                    {
                        connector.AddColumn(table, col);
                        columnsAdded++;
                    }
                }

                if (deleteRedundant)
                {
                    foreach (var (table, col) in surplusColumns)
                    {
                        connector.DropColumn(table, col);
                        columnsDropped++;
                    }

                    var liveForDrop = connector.GetTableNames()
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var tbl in surplusTables.Where(t => liveForDrop.Contains(t)))
                    {
                        connector.DropTable(tbl);
                        tablesDropped++;
                    }
                }

                if (fksToAdd.Count > 0 && connector.SupportsForeignKeys)
                {
                    var liveFksNow = connector.GetForeignKeys();
                    foreach (var rel in fksToAdd)
                    {
                        // Verify both tables exist on the backend before attempting to add FK
                        if (!liveNow.Contains(rel.Tabelal) || !liveNow.Contains(rel.Tabelad))
                        {
                            LogService.Warning("SchemaSync",
                                $"Skipping FK '{rel.Nazivrelacije}': table '{rel.Tabelal}' or '{rel.Tabelad}' not found on backend.");
                            continue;
                        }

                        // Verify the FK column exists in the child table
                        var liveChildCols = connector.GetColumnNames(rel.Tabelad)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (!liveChildCols.Contains(rel.Polje ?? ""))
                        {
                            LogService.Warning("SchemaSync",
                                $"Skipping FK '{rel.Nazivrelacije}': column '{rel.Polje}' not found in table '{rel.Tabelad}'.");
                            continue;
                        }

                        if (liveFksNow.Any(fk =>
                            string.Equals(fk.ChildTable,  rel.Tabelad, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(fk.ChildColumn, rel.Polje,   StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(fk.ParentTable, rel.Tabelal, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        var fkName = string.IsNullOrWhiteSpace(rel.Nazivrelacije)
                            ? $"FK_{rel.Tabelad}_{rel.Tabelal}"
                            : rel.Nazivrelacije;
                        connector.AddForeignKey(fkName, rel.Tabelad, rel.Polje,
                            rel.Tabelal, rel.Polje, rel.Updatedeletecascade);
                        fksAdded++;
                    }
                }

                LogService.Info("BatchSync",
                    $"Sync completed for {connectionString}: " +
                    $"+{tablesCreated}t +{columnsAdded}c +{fksAdded}fk " +
                    $"-{tablesDropped}t -{columnsDropped}c");
            });

            return new BatchSyncResult(connectionString, backendType,
                tablesCreated, columnsAdded, fksAdded,
                tablesDropped, columnsDropped);
        }
        catch (Exception ex)
        {
            LogService.Error("BatchSync", $"Sync error: {connectionString}", ex);
            return new BatchSyncResult(connectionString, backendType, 0, 0, 0, 0, 0, ex);
        }
    }
}
