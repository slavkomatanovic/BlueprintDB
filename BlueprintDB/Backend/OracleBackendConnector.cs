using Oracle.ManagedDataAccess.Client;

namespace Blueprint.App.Backend;

/// <summary>
/// Oracle backend (pure managed driver, no Oracle client install required).
/// Connection string: Data Source=host:1521/service;User Id=user;Password=pass;
/// Or with TNS alias:  Data Source=MYALIAS;User Id=user;Password=pass;
/// </summary>
public sealed class OracleBackendConnector : IBackendConnector
{
    private readonly OracleConnection _conn;
    private OracleTransaction? _tx;

    public OracleBackendConnector(string connectionString)
        => _conn = new OracleConnection(connectionString);

    public void Open() => _conn.Open();

    public IReadOnlyList<string> GetTableNames()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT TABLE_NAME FROM USER_TABLES ORDER BY TABLE_NAME";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public IReadOnlyList<string> GetColumnNames(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT COLUMN_NAME FROM USER_TAB_COLUMNS " +
            "WHERE TABLE_NAME = :t ORDER BY COLUMN_ID";
        cmd.Parameters.Add(new OracleParameter("t", tableName));
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
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
        var paramList = string.Join(", ", columns.Select((_, i) => $":p{i}"));
        var sql = $"INSERT INTO \"{Q(tableName)}\" ({colList}) VALUES ({paramList})";

        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = sql;
        for (int i = 0; i < columns.Count; i++)
            cmd.Parameters.Add(new OracleParameter($"p{i}", DBNull.Value));

        foreach (var row in rows)
        {
            for (int i = 0; i < columns.Count; i++)
                cmd.Parameters[i].Value = row[columns[i]] ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }
    }

    public void CreateTable(string tableName, IReadOnlyList<ColumnSchema> columns)
    {
        // Oracle has no IF NOT EXISTS; names unquoted are auto-uppercased
        var pkCols  = columns.Where(c => c.PrimaryKey).Select(c => $"\"{Q(c.Name)}\"").ToList();
        var colDefs = columns.Select(c =>
        {
            var type = string.IsNullOrEmpty(c.SqlType) ? "VARCHAR2(255)" : c.SqlType;
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
        var type = string.IsNullOrEmpty(column.SqlType) ? "VARCHAR2(255)" : column.SqlType;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE \"{Q(tableName)}\" ADD (\"{Q(column.Name)}\" {type})";
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
        cmd.CommandText =
            "SELECT ac.constraint_name, ac.table_name, acc.column_name, " +
            "       ac2.table_name, acc2.column_name " +
            "FROM user_constraints ac " +
            "JOIN user_cons_columns acc ON ac.constraint_name = acc.constraint_name " +
            "JOIN user_constraints ac2 ON ac.r_constraint_name = ac2.constraint_name " +
            "JOIN user_cons_columns acc2 ON ac2.constraint_name = acc2.constraint_name " +
            "WHERE ac.constraint_type = 'R'";
        using var r = cmd.ExecuteReader();
        var list = new List<ForeignKeyInfo>();
        while (r.Read())
            list.Add(new ForeignKeyInfo(r.GetString(0), r.GetString(1), r.GetString(2),
                                        r.GetString(3), r.GetString(4)));
        return list;
    }

    public void AddForeignKey(string constraintName, string childTable, string childColumn,
                              string parentTable, string parentColumn, bool cascade)
    {
        // Oracle doesn't support ON UPDATE CASCADE
        var cascadeSql = cascade ? " ON DELETE CASCADE" : "";
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            $"ALTER TABLE \"{Q(childTable)}\" ADD CONSTRAINT \"{Q(constraintName)}\" " +
            $"FOREIGN KEY (\"{Q(childColumn)}\") REFERENCES \"{Q(parentTable)}\"(\"{Q(parentColumn)}\")" +
            cascadeSql;
        cmd.ExecuteNonQuery();
    }

    public void BeginTransaction() => _tx = _conn.BeginTransaction();
    public void Commit()   { _tx?.Commit();   _tx = null; }
    public void Rollback() { _tx?.Rollback(); _tx = null; }
    public void Dispose()  { _tx?.Dispose();  _conn.Dispose(); }

    private static string Q(string s) => s.Replace("\"", "\"\"");
}
