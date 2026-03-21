using System.Data.Odbc;

namespace Blueprint.App.Backend;

/// <summary>
/// IBM DB2 backend via ODBC.
/// Requires IBM Data Server Driver (ODBC/CLI) to be installed on the host machine.
/// Connection string (ODBC DSN):     DSN=mydsn;UID=user;PWD=pass;
/// Connection string (driver-based): Driver={IBM DB2 ODBC DRIVER};Database=MYDB;Hostname=host;Port=50000;Protocol=TCPIP;Uid=user;Pwd=pass;
/// </summary>
public sealed class Db2BackendConnector : IBackendConnector
{
    private readonly OdbcConnection _conn;
    private OdbcTransaction? _tx;

    public Db2BackendConnector(string connectionString)
        => _conn = new OdbcConnection(connectionString);

    public void Open() => _conn.Open();

    public IReadOnlyList<string> GetTableNames()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT TABNAME FROM SYSCAT.TABLES " +
            "WHERE TABSCHEMA = CURRENT SCHEMA AND TYPE = 'T' " +
            "ORDER BY TABNAME";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0).TrimEnd());
        return list;
    }

    public IReadOnlyList<string> GetColumnNames(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        // ODBC uses positional '?' parameters — name is ignored, only order matters
        cmd.CommandText =
            "SELECT COLNAME FROM SYSCAT.COLUMNS " +
            "WHERE TABSCHEMA = CURRENT SCHEMA AND TABNAME = ? " +
            "ORDER BY COLNO";
        cmd.Parameters.AddWithValue("tabname", tableName.ToUpperInvariant());
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0).TrimEnd());
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
        var paramList = string.Join(", ", columns.Select(_ => "?"));
        var sql = $"INSERT INTO \"{Q(tableName)}\" ({colList}) VALUES ({paramList})";

        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = sql;
        for (int i = 0; i < columns.Count; i++)
            cmd.Parameters.AddWithValue($"p{i}", DBNull.Value);

        foreach (var row in rows)
        {
            for (int i = 0; i < columns.Count; i++)
                cmd.Parameters[i].Value = row[columns[i]] ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }
    }

    public void CreateTable(string tableName, IReadOnlyList<ColumnSchema> columns)
    {
        // DB2 has no IF NOT EXISTS — guard with SYSCAT.TABLES check
        using var existsCmd = _conn.CreateCommand();
        existsCmd.CommandText =
            "SELECT COUNT(*) FROM SYSCAT.TABLES " +
            "WHERE TABSCHEMA = CURRENT SCHEMA AND TABNAME = ?";
        existsCmd.Parameters.AddWithValue("tabname", tableName.ToUpperInvariant());
        var count = Convert.ToInt32(existsCmd.ExecuteScalar());
        if (count > 0) return;

        var pkCols  = columns.Where(c => c.PrimaryKey).Select(c => $"\"{Q(c.Name)}\"").ToList();
        var colDefs = columns.Select(c =>
        {
            var type = string.IsNullOrEmpty(c.SqlType) ? "VARCHAR(255)" : c.SqlType;
            var nn   = c.NotNull && !c.PrimaryKey ? " NOT NULL" : "";
            return $"  \"{Q(c.Name)}\" {type}{nn}";
        }).ToList();
        if (pkCols.Count > 0)
            colDefs.Add($"  PRIMARY KEY ({string.Join(", ", pkCols)})");

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"CREATE TABLE \"{Q(tableName)}\" (\n{string.Join(",\n", colDefs)}\n)";
        cmd.ExecuteNonQuery();
    }

    public void AddColumn(string tableName, ColumnSchema column)
    {
        var type = string.IsNullOrEmpty(column.SqlType) ? "VARCHAR(255)" : column.SqlType;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            $"ALTER TABLE \"{Q(tableName)}\" ADD COLUMN \"{Q(column.Name)}\" {type}";
        cmd.ExecuteNonQuery();
    }

    public void DropTable(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"DROP TABLE \"{Q(tableName)}\"";
        cmd.ExecuteNonQuery();
    }

    public void DropColumn(string tableName, string columnName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"ALTER TABLE \"{Q(tableName)}\" DROP COLUMN \"{Q(columnName)}\"";
        cmd.ExecuteNonQuery();
    }

    public bool SupportsForeignKeys => true;

    public IReadOnlyList<ForeignKeyInfo> GetForeignKeys()
    {
        using var cmd = _conn.CreateCommand();
        // SYSCAT.KEYCOLUSE holds columns for both FK and referenced PK/UK constraints
        cmd.CommandText =
            "SELECT r.CONSTNAME, r.TABNAME, fk.COLNAME, r.REFTABNAME, pk.COLNAME " +
            "FROM SYSCAT.REFERENCES r " +
            "JOIN SYSCAT.KEYCOLUSE fk " +
            "  ON fk.CONSTNAME = r.CONSTNAME AND fk.TABSCHEMA = r.TABSCHEMA AND fk.TABNAME = r.TABNAME " +
            "JOIN SYSCAT.KEYCOLUSE pk " +
            "  ON pk.CONSTNAME = r.REFKEYNAME AND pk.COLSEQ = fk.COLSEQ " +
            "WHERE r.TABSCHEMA = CURRENT SCHEMA " +
            "ORDER BY r.CONSTNAME, fk.COLSEQ";
        using var r = cmd.ExecuteReader();
        var list = new List<ForeignKeyInfo>();
        while (r.Read())
            list.Add(new ForeignKeyInfo(
                r.GetString(0).TrimEnd(), r.GetString(1).TrimEnd(), r.GetString(2).TrimEnd(),
                r.GetString(3).TrimEnd(), r.GetString(4).TrimEnd()));
        return list;
    }

    public void AddForeignKey(string constraintName, string childTable, string childColumn,
                              string parentTable, string parentColumn, bool cascade)
    {
        // DB2 supports ON DELETE CASCADE; ON UPDATE CASCADE is not supported
        var cascadeSql = cascade ? " ON DELETE CASCADE" : "";
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            $"ALTER TABLE \"{Q(childTable)}\" ADD CONSTRAINT \"{Q(constraintName)}\" " +
            $"FOREIGN KEY (\"{Q(childColumn)}\") " +
            $"REFERENCES \"{Q(parentTable)}\"(\"{Q(parentColumn)}\")" +
            cascadeSql;
        cmd.ExecuteNonQuery();
    }

    public void BeginTransaction() => _tx = _conn.BeginTransaction();
    public void Commit()   { _tx?.Commit();   _tx = null; }
    public void Rollback() { _tx?.Rollback(); _tx = null; }
    public void Dispose()  { _tx?.Dispose();  _conn.Dispose(); }

    private static string Q(string s) => s.Replace("\"", "\"\"");
}
