using System.Text;
using Blueprint.App.Backend;
using Blueprint.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Blueprint.App;

/// <summary>
/// Generates SQL DDL (CREATE TABLE statements) for a program's schema
/// targeting a specific backend dialect. No live database connection needed.
/// </summary>
public static class SchemaExportService
{
    /// <summary>
    /// Generates the full DDL script for all tables in the given program.
    /// </summary>
    public static string GenerateDdl(int programId, BackendType target)
    {
        using var db = new BlueprintDbContext();

        var program = db.Programis
            .FirstOrDefault(p => p.Idprograma == programId && p.Skriven != true)
            ?? throw new InvalidOperationException("Program not found.");

        var tables = db.Tabeles
            .Where(t => t.Idprograma == programId && t.Skriven != true)
            .OrderBy(t => t.Nazivtabele)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"-- Blueprint DDL Export");
        sb.AppendLine($"-- Program : {program.Nazivprograma}");
        sb.AppendLine($"-- Backend : {target}");
        sb.AppendLine($"-- Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        foreach (var table in tables)
        {
            var cols = db.Kolones
                .Where(k => k.Idtabele == table.Idtabele && k.Skriven != true)
                .OrderBy(k => k.Idkolone)
                .ToList();

            AppendCreateTable(sb, target, table.Nazivtabele!, cols);
            sb.AppendLine();
        }

        // FK constraints (for backends that support them)
        if (SupportsFk(target))
        {
            var relacije = db.Relacijes
                .Where(r => r.Idprograma == programId && r.Skriven != true)
                .ToList();

            if (relacije.Count > 0)
            {
                sb.AppendLine($"-- Foreign key constraints");
                sb.AppendLine();
                int fkIndex = 1;
                foreach (var rel in relacije)
                {
                    if (string.IsNullOrEmpty(rel.Tabelad) || string.IsNullOrEmpty(rel.Polje) ||
                        string.IsNullOrEmpty(rel.Tabelal)) continue;

                    var constraintName = $"fk_{rel.Tabelad}_{rel.Polje}_{fkIndex++}";
                    var childTable  = Quote(target, rel.Tabelad);
                    var childCol    = Quote(target, rel.Polje);
                    var parentTable = Quote(target, rel.Tabelal);
                    var parentCol   = Quote(target, rel.Polje!);

                    sb.AppendLine($"ALTER TABLE {childTable}");
                    sb.AppendLine($"    ADD CONSTRAINT {constraintName}");
                    sb.AppendLine($"    FOREIGN KEY ({childCol}) REFERENCES {parentTable} ({parentCol});");
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void AppendCreateTable(
        StringBuilder sb, BackendType target, string tableName, List<Kolone> cols)
    {
        var pkCols = cols
            .Where(c => c.Key == true || TypeMappings.IsAutoNumberType(c.Tippodatka))
            .ToList();

        sb.AppendLine($"CREATE TABLE {Quote(target, tableName)} (");

        for (int i = 0; i < cols.Count; i++)
        {
            var c    = cols[i];
            _ = int.TryParse(c.Fieldsize, out var maxLen);
            var type = TypeMappings.ResolveToDdl(target, c.Tippodatka, maxLen);
            var nn   = (c.Allownull == "No" || c.Key == true || TypeMappings.IsAutoNumberType(c.Tippodatka))
                       ? " NOT NULL" : "";

            bool isLast = i == cols.Count - 1 && (target == BackendType.SQLite || pkCols.Count == 0);
            sb.AppendLine($"    {Quote(target, c.Nazivkolone!)} {type}{nn}{(isLast ? "" : ",")}");
        }

        // Explicit PRIMARY KEY constraint (not SQLite — SQLite uses column-level PK or rowid)
        if (pkCols.Count > 0 && target != BackendType.SQLite)
        {
            var pkList = string.Join(", ", pkCols.Select(c => Quote(target, c.Nazivkolone!)));
            sb.AppendLine($"    PRIMARY KEY ({pkList})");
        }

        sb.Append(")");
        sb.AppendLine(TableSuffix(target));
    }

    private static string Quote(BackendType target, string name) => target switch
    {
        BackendType.MySQL or BackendType.MariaDB => $"`{name.Replace("`", "``")}`",
        BackendType.SqlServer                    => $"[{name.Replace("]", "]]")}]",
        _                                        => $"\"{name.Replace("\"", "\"\"")}\"",
    };

    private static string TableSuffix(BackendType target) => target switch
    {
        BackendType.MySQL or BackendType.MariaDB => " ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
        _                                        => ";",
    };

    private static bool SupportsFk(BackendType target) => target is
        BackendType.MySQL or BackendType.MariaDB or BackendType.PostgreSQL or
        BackendType.SqlServer or BackendType.Oracle or BackendType.Firebird or BackendType.DB2;
}
