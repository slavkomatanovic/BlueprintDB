using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace Blueprint.App.Backend;

public sealed class SqliteBackendConnector : IBackendConnector
{
    private readonly SqliteConnection _conn;
    private SqliteTransaction? _tx;

    public SqliteBackendConnector(string dbPath)
        => _conn = new SqliteConnection($"Data Source={dbPath}");

    public void Open() => _conn.Open();

    public IReadOnlyList<string> GetTableNames()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public IReadOnlyList<ColumnSchema> GetColumnSchema(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{Q(tableName)}\")";
        using var r = cmd.ExecuteReader();
        var list = new List<ColumnSchema>();
        while (r.Read())
        {
            var rawType = r.GetString(2);
            var m       = Regex.Match(rawType, @"\((\d+)\)");
            int maxLen  = m.Success ? int.Parse(m.Groups[1].Value) : 0;
            var baseType = m.Success ? rawType[..rawType.IndexOf('(')] : rawType;
            list.Add(new ColumnSchema(
                Name:       r.GetString(1),
                SqlType:    baseType.Trim().ToUpperInvariant(),
                NotNull:    r.GetInt32(3) == 1,
                PrimaryKey: r.GetInt32(5) > 0,
                MaxLength:  maxLen));
        }
        return list;
    }

    public IReadOnlyList<string> GetColumnNames(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{Q(tableName)}\")";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(1));
        return list;
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ReadAll(
        string tableName, IReadOnlyList<string> columns)
    {
        var cols = string.Join(", ", columns.Select(c => $"\"{Q(c)}\""));
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {cols} FROM \"{Q(tableName)}\"";
        using var r = cmd.ExecuteReader();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (r.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < columns.Count; i++)
                row[columns[i]] = r.IsDBNull(i) ? null : r.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    public void DeleteAll(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"DELETE FROM \"{Q(tableName)}\"";
        cmd.ExecuteNonQuery();
    }

    public void InsertRows(string tableName, IReadOnlyList<string> columns,
                           IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        var colList   = string.Join(", ", columns.Select(c => $"\"{Q(c)}\""));
        var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var sql = $"INSERT INTO \"{Q(tableName)}\" ({colList}) VALUES ({paramList})";

        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = sql;
        for (int i = 0; i < columns.Count; i++)
            cmd.Parameters.Add(new SqliteParameter($"@p{i}", SqliteType.Text));

        foreach (var row in rows)
        {
            for (int i = 0; i < columns.Count; i++)
                cmd.Parameters[$"@p{i}"].Value = row[columns[i]] ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }
    }

    public void CreateTable(string tableName, IReadOnlyList<ColumnSchema> columns)
    {
        var pkCols  = columns.Where(c => c.PrimaryKey).Select(c => $"\"{Q(c.Name)}\"").ToList();
        var colDefs = columns.Select(c =>
        {
            var type = string.IsNullOrEmpty(c.SqlType) ? "TEXT" : c.SqlType;
            var nn   = c.NotNull && !c.PrimaryKey ? " NOT NULL" : "";
            return $"  \"{Q(c.Name)}\" {type}{nn}";
        }).ToList();
        if (pkCols.Count > 0)
            colDefs.Add($"  PRIMARY KEY ({string.Join(", ", pkCols)})");

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"CREATE TABLE IF NOT EXISTS \"{Q(tableName)}\" (\n{string.Join(",\n", colDefs)}\n)";
        cmd.ExecuteNonQuery();
    }

    public void AddColumn(string tableName, ColumnSchema column)
    {
        var type = string.IsNullOrEmpty(column.SqlType) ? "TEXT" : column.SqlType;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE \"{Q(tableName)}\" ADD COLUMN \"{Q(column.Name)}\" {type}";
        cmd.ExecuteNonQuery();
    }

    public void DropTable(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"DROP TABLE IF EXISTS \"{Q(tableName)}\"";
        cmd.ExecuteNonQuery();
    }

    public void DropColumn(string tableName, string columnName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"ALTER TABLE \"{Q(tableName)}\" DROP COLUMN \"{Q(columnName)}\"";
        cmd.ExecuteNonQuery();
    }

    public void BeginTransaction() => _tx = _conn.BeginTransaction();
    public void Commit()   { _tx?.Commit();   _tx = null; }
    public void Rollback() { _tx?.Rollback(); _tx = null; }
    public void Dispose()  { _tx?.Dispose();  _conn.Dispose(); }

    private static string Q(string s) => s.Replace("\"", "\"\"");
}
