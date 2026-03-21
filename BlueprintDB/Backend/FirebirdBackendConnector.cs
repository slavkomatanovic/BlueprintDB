using FirebirdSql.Data.FirebirdClient;

namespace Blueprint.App.Backend;

/// <summary>
/// Firebird backend.
/// Connection string: DataSource=localhost;Database=C:\path\db.fdb;User=SYSDBA;Password=masterkey;
/// For embedded (no server): DataSource=localhost;Database=C:\path\db.fdb;User=SYSDBA;Password=masterkey;ServerType=1;
/// </summary>
public sealed class FirebirdBackendConnector : IBackendConnector
{
    private readonly FbConnection _conn;
    private FbTransaction? _tx;

    public FirebirdBackendConnector(string connectionString)
        => _conn = new FbConnection(connectionString);

    public void Open() => _conn.Open();

    public IReadOnlyList<string> GetTableNames()
    {
        using var cmd = _conn.CreateCommand();
        // RDB$SYSTEM_FLAG = 0 excludes system tables; RDB$VIEW_SOURCE IS NULL excludes views
        cmd.CommandText =
            "SELECT TRIM(RDB$RELATION_NAME) FROM RDB$RELATIONS " +
            "WHERE RDB$SYSTEM_FLAG = 0 AND RDB$VIEW_SOURCE IS NULL " +
            "ORDER BY RDB$RELATION_NAME";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0).TrimEnd());
        return list;
    }

    public IReadOnlyList<string> GetColumnNames(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT TRIM(RDB$FIELD_NAME) FROM RDB$RELATION_FIELDS " +
            "WHERE RDB$RELATION_NAME = @t " +
            "ORDER BY RDB$FIELD_POSITION";
        // Firebird stores relation names as CHAR(31) — pad for exact match in older versions
        cmd.Parameters.AddWithValue("@t", tableName.PadRight(31));
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
        cmd.CommandText = $"SELECT {cols} FROM \"{Q(FbId(tableName))}\"";
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
        cmd.CommandText = $"DELETE FROM \"{Q(FbId(tableName))}\"";
        cmd.ExecuteNonQuery();
    }

    public void InsertRows(string tableName, IReadOnlyList<string> columns,
                           IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        var colList   = string.Join(", ", columns.Select(c => $"\"{Q(FbId(c))}\""));
        var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var sql = $"INSERT INTO \"{Q(FbId(tableName))}\" ({colList}) VALUES ({paramList})";

        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = sql;
        for (int i = 0; i < columns.Count; i++)
            cmd.Parameters.AddWithValue($"@p{i}", DBNull.Value);

        foreach (var row in rows)
        {
            for (int i = 0; i < columns.Count; i++)
                cmd.Parameters[$"@p{i}"].Value = row[columns[i]] ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }
    }

    public void CreateTable(string tableName, IReadOnlyList<ColumnSchema> columns)
    {
        // Firebird has no IF NOT EXISTS before v3; guard with RDB$RELATIONS check
        using var existsCmd = _conn.CreateCommand();
        existsCmd.CommandText =
            "SELECT COUNT(*) FROM RDB$RELATIONS WHERE TRIM(RDB$RELATION_NAME) = @t";
        existsCmd.Parameters.AddWithValue("@t", tableName.TrimEnd());
        var count = Convert.ToInt32(existsCmd.ExecuteScalar());
        if (count > 0) return;

        var pkCols  = columns.Where(c => c.PrimaryKey).Select(c => $"\"{Q(FbId(c.Name))}\"").ToList();
        var colDefs = columns.Select(c =>
        {
            var type = string.IsNullOrEmpty(c.SqlType) ? "VARCHAR(255)" : c.SqlType;
            var nn   = c.NotNull && !c.PrimaryKey ? " NOT NULL" : "";
            return $"  \"{Q(FbId(c.Name))}\" {type}{nn}";
        }).ToList();
        if (pkCols.Count > 0)
            colDefs.Add($"  PRIMARY KEY ({string.Join(", ", pkCols)})");

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"CREATE TABLE \"{Q(FbId(tableName))}\" (\n{string.Join(",\n", colDefs)}\n)";
        cmd.ExecuteNonQuery();
    }

    public void AddColumn(string tableName, ColumnSchema column)
    {
        var type = string.IsNullOrEmpty(column.SqlType) ? "VARCHAR(255)" : column.SqlType;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            $"ALTER TABLE \"{Q(FbId(tableName))}\" ADD \"{Q(FbId(column.Name))}\" {type}";
        cmd.ExecuteNonQuery();
    }

    public void DropTable(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"DROP TABLE \"{Q(FbId(tableName))}\"";
        cmd.ExecuteNonQuery();
    }

    public void DropColumn(string tableName, string columnName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"ALTER TABLE \"{Q(FbId(tableName))}\" DROP \"{Q(FbId(columnName))}\"";
        cmd.ExecuteNonQuery();
    }

    public bool SupportsForeignKeys => true;

    public IReadOnlyList<ForeignKeyInfo> GetForeignKeys()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT TRIM(rc.RDB$CONSTRAINT_NAME), " +
            "       TRIM(rc.RDB$RELATION_NAME), " +
            "       TRIM(iseg.RDB$FIELD_NAME), " +
            "       TRIM(rc2.RDB$RELATION_NAME), " +
            "       TRIM(iseg2.RDB$FIELD_NAME) " +
            "FROM RDB$RELATION_CONSTRAINTS rc " +
            "JOIN RDB$REF_CONSTRAINTS refc ON rc.RDB$CONSTRAINT_NAME = refc.RDB$CONSTRAINT_NAME " +
            "JOIN RDB$INDEX_SEGMENTS iseg ON rc.RDB$INDEX_NAME = iseg.RDB$INDEX_NAME " +
            "JOIN RDB$RELATION_CONSTRAINTS rc2 ON refc.RDB$CONST_NAME_UQ = rc2.RDB$CONSTRAINT_NAME " +
            "JOIN RDB$INDEX_SEGMENTS iseg2 ON rc2.RDB$INDEX_NAME = iseg2.RDB$INDEX_NAME " +
            "WHERE rc.RDB$CONSTRAINT_TYPE = 'FOREIGN KEY'";
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
        var cascadeSql = cascade ? " ON DELETE CASCADE ON UPDATE CASCADE" : "";
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            $"ALTER TABLE \"{Q(FbId(childTable))}\" ADD CONSTRAINT \"{Q(FbId(constraintName))}\" " +
            $"FOREIGN KEY (\"{Q(FbId(childColumn))}\") " +
            $"REFERENCES \"{Q(FbId(parentTable))}\"(\"{Q(FbId(parentColumn))}\")" +
            cascadeSql;
        cmd.ExecuteNonQuery();
    }

    public void BeginTransaction() => _tx = _conn.BeginTransaction();
    public void Commit()   { _tx?.Commit();   _tx = null; }
    public void Rollback() { _tx?.Rollback(); _tx = null; }
    public void Dispose()  { _tx?.Dispose();  _conn.Dispose(); }

    /// <summary>Escapes double quotes by doubling them.</summary>
    private static string Q(string s) => s.Replace("\"", "\"\"");

    /// <summary>
    /// Truncates identifier to 31 characters — Firebird's limit for older versions (pre-4.0).
    /// Firebird 4.0+ supports up to 63 characters.
    /// </summary>
    private static string FbId(string name) =>
        name.Length > 31 ? name[..31].TrimEnd() : name.TrimEnd();
}
