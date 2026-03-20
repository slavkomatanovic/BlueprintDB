using System.Data;
using System.Data.OleDb;

namespace Blueprint.App.Backend;

/// <summary>
/// dBase / FoxPro backend via OleDb (Jet 4.0).
/// The "connection string" is the folder path that contains the .dbf files.
/// Each .dbf file is one table. Requires Microsoft Jet or ACE OleDb provider.
/// </summary>
public sealed class DBaseBackendConnector : IBackendConnector
{
    private readonly OleDbConnection _conn;
    private OleDbTransaction? _tx;

    public DBaseBackendConnector(string folderPath)
    {
        // Try ACE 12.0 first (comes with Office 2007+); fall back to Jet 4.0
        string cs;
        try
        {
            cs = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={folderPath};Extended Properties=\"dBASE IV;\";";
            _ = new OleDbConnection(cs); // just construct to check provider availability
        }
        catch
        {
            cs = $"Provider=Microsoft.Jet.OLEDB.4.0;Data Source={folderPath};Extended Properties=\"dBASE IV;\";";
        }
        _conn = new OleDbConnection(cs);
    }

    public void Open() => _conn.Open();

    public IReadOnlyList<string> GetTableNames()
    {
        var schema = _conn.GetOleDbSchemaTable(
            OleDbSchemaGuid.Tables, new object?[] { null, null, null, "TABLE" });
        var list = new List<string>();
        if (schema == null) return list;
        foreach (DataRow row in schema.Rows)
            list.Add(row["TABLE_NAME"]?.ToString() ?? "");
        return list;
    }

    public IReadOnlyList<string> GetColumnNames(string tableName)
    {
        var schema = _conn.GetOleDbSchemaTable(
            OleDbSchemaGuid.Columns, new object?[] { null, null, tableName, null });
        var list = new List<string>();
        if (schema == null) return list;
        foreach (DataRow row in schema.Rows)
            list.Add(row["COLUMN_NAME"]?.ToString() ?? "");
        return list;
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ReadAll(
        string tableName, IReadOnlyList<string> columns)
    {
        var cols = string.Join(", ", columns.Select(c => $"[{c}]"));
        using var cmd = new OleDbCommand($"SELECT {cols} FROM [{tableName}]", _conn, _tx);
        using var r = cmd.ExecuteReader();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (r != null && r.Read())
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
        using var cmd = new OleDbCommand($"DELETE FROM [{tableName}]", _conn, _tx);
        cmd.ExecuteNonQuery();
    }

    public void InsertRows(string tableName, IReadOnlyList<string> columns,
                           IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        var colList   = string.Join(", ", columns.Select(c => $"[{c}]"));
        var paramList = string.Join(", ", columns.Select(_ => "?"));
        var sql = $"INSERT INTO [{tableName}] ({colList}) VALUES ({paramList})";

        using var cmd = new OleDbCommand(sql, _conn, _tx);
        for (int i = 0; i < columns.Count; i++)
            cmd.Parameters.Add($"p{i}", OleDbType.VarWChar);

        foreach (var row in rows)
        {
            for (int i = 0; i < columns.Count; i++)
                cmd.Parameters[i].Value = row[columns[i]] ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }
    }

    public void BeginTransaction() => _tx = _conn.BeginTransaction();
    public void Commit()   { _tx?.Commit();   _tx = null; }
    public void Rollback() { _tx?.Rollback(); _tx = null; }
    public void Dispose()  { _tx?.Dispose();  _conn.Dispose(); }
}
