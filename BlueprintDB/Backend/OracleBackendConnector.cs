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
        // Oracle stores unquoted names in uppercase — convert to match USER_TAB_COLUMNS
        cmd.CommandText =
            "SELECT COLUMN_NAME FROM USER_TAB_COLUMNS " +
            "WHERE TABLE_NAME = :t ORDER BY COLUMN_ID";
        cmd.Parameters.Add(new OracleParameter("t", tableName.ToUpperInvariant()));
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public IReadOnlyList<ColumnSchema> GetColumnSchema(string tableName)
    {
        // Primary key columns
        var pkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pkCmd = _conn.CreateCommand())
        {
            pkCmd.CommandText =
                "SELECT acc.COLUMN_NAME FROM USER_CONSTRAINTS ac " +
                "JOIN USER_CONS_COLUMNS acc ON ac.CONSTRAINT_NAME = acc.CONSTRAINT_NAME " +
                "WHERE ac.TABLE_NAME = :t AND ac.CONSTRAINT_TYPE = 'P'";
            pkCmd.Parameters.Add(new OracleParameter("t", tableName.ToUpperInvariant()));
            using var pkr = pkCmd.ExecuteReader();
            while (pkr.Read()) pkNames.Add(pkr.GetString(0));
        }

        // Identity columns (Oracle 12c+)
        var identityCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var idCmd = _conn.CreateCommand())
        {
            idCmd.CommandText =
                "SELECT COLUMN_NAME FROM USER_TAB_IDENTITY_COLS WHERE TABLE_NAME = :t";
            idCmd.Parameters.Add(new OracleParameter("t", tableName.ToUpperInvariant()));
            try
            {
                using var idr = idCmd.ExecuteReader();
                while (idr.Read()) identityCols.Add(idr.GetString(0));
            }
            catch { /* USER_TAB_IDENTITY_COLS not available on older Oracle versions */ }
        }

        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT COLUMN_NAME, DATA_TYPE, CHAR_COL_DECL_LENGTH, NULLABLE " +
            "FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :t ORDER BY COLUMN_ID";
        cmd.Parameters.Add(new OracleParameter("t", tableName.ToUpperInvariant()));
        using var r = cmd.ExecuteReader();
        var list = new List<ColumnSchema>();
        while (r.Read())
        {
            var name     = r.GetString(0);
            var dataType = r.GetString(1);
            var rawMax   = r.IsDBNull(2) ? 0 : r.GetInt32(2);
            var maxLen   = rawMax > 0 && rawMax <= 8000 ? rawMax : 0;
            var notNull  = r.GetString(3) == "N";
            var isPk     = pkNames.Contains(name);
            var isIdent  = identityCols.Contains(name);

            string sqlType;
            if (isIdent)
                sqlType = "AutoNumber";
            else
            {
                var canonical = TypeMappings.Resolve(BackendType.Oracle, dataType);
                sqlType = TypeMappings.CanonicalToAdo(canonical, maxLen);
            }
            list.Add(new ColumnSchema(name, sqlType, notNull, isPk, maxLen));
        }
        return list;
    }

    public IReadOnlyDictionary<string, CanonicalType> GetColumnTypes(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        // USER_TAB_COLUMNS is scoped to the current user — much faster than ALL_TAB_COLUMNS
        cmd.CommandText =
            "SELECT COLUMN_NAME, DATA_TYPE FROM USER_TAB_COLUMNS " +
            "WHERE TABLE_NAME = :t ORDER BY COLUMN_ID";
        cmd.Parameters.Add(new OracleParameter("t", tableName.ToUpperInvariant()));
        using var r = cmd.ExecuteReader();
        var dict = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase);
        while (r.Read())
            dict[r.GetString(0)] = TypeMappings.Resolve(BackendType.Oracle, r.GetString(1));
        return dict;
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ReadAll(
        string tableName, IReadOnlyList<string> columns)
    {
        var cols = string.Join(", ", columns.Select(c => $"\"{Q(c)}\""));
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {cols} FROM \"{Q(tableName)}\"";
        // Fetch all LOB data inline — prevents per-row network roundtrips for CLOB/BLOB columns
        cmd.InitialLOBFetchSize = -1;
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
        foreach (var row in rows)
        {
            cmd.Parameters.Clear();
            for (int i = 0; i < columns.Count; i++)
                cmd.Parameters.Add(new OracleParameter($"p{i}", ToOracleValue(row[columns[i]])));
            cmd.ExecuteNonQuery();
        }
    }

    private static object ToOracleValue(object? val) => val switch
    {
        null       => DBNull.Value,
        bool b     => b ? 1 : 0,
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        Guid g     => g.ToString(),
        _          => val
    };

    public void CreateTable(string tableName, IReadOnlyList<ColumnSchema> columns)
    {
        // Oracle has no IF NOT EXISTS — guard with USER_TABLES check
        using var existsCmd = _conn.CreateCommand();
        existsCmd.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :t";
        existsCmd.Parameters.Add(new OracleParameter("t", tableName.ToUpperInvariant()));
        var count = Convert.ToInt32(existsCmd.ExecuteScalar());
        if (count > 0) return;

        var pkCols  = columns.Where(c => c.PrimaryKey).Select(c => $"\"{Q(c.Name)}\"").ToList();
        var colDefs = columns.Select(c =>
        {
            var type = TypeMappings.ResolveToDdl(BackendType.Oracle, c.SqlType, c.MaxLength);
            var nn   = (c.NotNull || c.PrimaryKey) ? " NOT NULL" : "";
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
        // Oracle has no ADD COLUMN IF NOT EXISTS — guard with USER_TAB_COLUMNS check
        using var existsCmd = _conn.CreateCommand();
        existsCmd.CommandText =
            "SELECT COUNT(*) FROM USER_TAB_COLUMNS WHERE TABLE_NAME = :t AND COLUMN_NAME = :c";
        existsCmd.Parameters.Add(new OracleParameter("t", tableName.ToUpperInvariant()));
        existsCmd.Parameters.Add(new OracleParameter("c", column.Name.ToUpperInvariant()));
        var count = Convert.ToInt32(existsCmd.ExecuteScalar());
        if (count > 0) return;

        var type = TypeMappings.ResolveToDdl(BackendType.Oracle, column.SqlType, column.MaxLength);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE \"{Q(tableName)}\" ADD (\"{Q(column.Name)}\" {type})";
        cmd.ExecuteNonQuery();
    }

    public void DropTable(string tableName)
    {
        // Oracle has no DROP TABLE IF EXISTS — guard with USER_TABLES check
        using var existsCmd = _conn.CreateCommand();
        existsCmd.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :t";
        existsCmd.Parameters.Add(new OracleParameter("t", tableName.ToUpperInvariant()));
        var count = Convert.ToInt32(existsCmd.ExecuteScalar());
        if (count == 0) return;

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

    // Oracle stores unquoted identifiers in UPPERCASE in the data dictionary.
    // We always uppercase before quoting so DDL/DML identifiers match dictionary lookups.
    private static string Q(string s) => s.ToUpperInvariant().Replace("\"", "\"\"");
}
