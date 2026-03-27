using MySqlConnector;

namespace Blueprint.App.Backend;

/// <summary>
/// MySQL backend. Connection string format:
/// Server=host;Port=3306;Database=dbname;Uid=user;Pwd=password;
/// </summary>
public sealed class MySqlBackendConnector : IBackendConnector
{
    private readonly MySqlConnection _conn;
    private MySqlTransaction? _tx;

    public MySqlBackendConnector(string connectionString)
        => _conn = new MySqlConnection(connectionString);

    public void Open() => _conn.Open();

    public IReadOnlyList<string> GetTableNames()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SHOW TABLES";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public IReadOnlyList<string> GetColumnNames(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SHOW COLUMNS FROM `{Q(tableName)}`";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0)); // Field
        return list;
    }

    public IReadOnlyList<ColumnSchema> GetColumnSchema(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE, " +
            "       EXTRA, COLUMN_KEY " +
            "FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t ORDER BY ORDINAL_POSITION";
        cmd.Parameters.AddWithValue("@t", tableName);
        using var r = cmd.ExecuteReader();
        var list = new List<ColumnSchema>();
        while (r.Read())
        {
            var name       = r.GetString(0);
            var dataType   = r.GetString(1);
            var rawMax     = r.IsDBNull(2) ? 0L : r.GetInt64(2);
            var maxLen     = rawMax > 0 && rawMax <= 8000 ? (int)rawMax : 0;
            var notNull    = r.GetString(3) == "NO";
            var extra      = r.IsDBNull(4) ? "" : r.GetString(4);
            var colKey     = r.IsDBNull(5) ? "" : r.GetString(5);
            var isAutoInc  = extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase);
            var isPk       = colKey.Equals("PRI", StringComparison.OrdinalIgnoreCase);

            string sqlType;
            if (isAutoInc)
                sqlType = "AutoNumber";
            else
            {
                var canonical = TypeMappings.Resolve(BackendType.MySQL, dataType);
                sqlType = TypeMappings.CanonicalToAdo(canonical, maxLen);
            }
            list.Add(new ColumnSchema(name, sqlType, notNull, isPk, maxLen));
        }
        return list;
    }

    public IReadOnlyDictionary<string, CanonicalType> GetColumnTypes(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT COLUMN_NAME, DATA_TYPE FROM information_schema.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t";
        cmd.Parameters.AddWithValue("@t", tableName);
        using var r = cmd.ExecuteReader();
        var dict = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase);
        while (r.Read())
            dict[r.GetString(0)] = TypeMappings.Resolve(BackendType.MySQL, r.GetString(1));
        return dict;
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ReadAll(
        string tableName, IReadOnlyList<string> columns)
    {
        var cols = string.Join(", ", columns.Select(c => $"`{Q(c)}`"));
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {cols} FROM `{Q(tableName)}`";
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
        cmd.CommandText = $"DELETE FROM `{Q(tableName)}`";
        cmd.ExecuteNonQuery();
    }

    public void InsertRows(string tableName, IReadOnlyList<string> columns,
                           IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        var colList   = string.Join(", ", columns.Select(c => $"`{Q(c)}`"));
        var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var sql = $"INSERT INTO `{Q(tableName)}` ({colList}) VALUES ({paramList})";

        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = sql;
        foreach (var row in rows)
        {
            cmd.Parameters.Clear();
            for (int i = 0; i < columns.Count; i++)
                cmd.Parameters.AddWithValue($"@p{i}", row[columns[i]] ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public void CreateTable(string tableName, IReadOnlyList<ColumnSchema> columns)
    {
        // MySQL: AUTO_INCREMENT columns must be part of a key.
        // Force AutoNumber columns into pkCols even if PrimaryKey flag is not set in metadata.
        var pkCols = columns
            .Where(c => c.PrimaryKey || TypeMappings.IsAutoNumberType(c.SqlType))
            .Select(c => $"`{Q(c.Name)}`")
            .ToList();
        var colDefs = columns.Select(c =>
        {
            var type = TypeMappings.ResolveToDdl(BackendType.MySQL, c.SqlType, c.MaxLength);
            var nn   = (c.NotNull || c.PrimaryKey || TypeMappings.IsAutoNumberType(c.SqlType)) ? " NOT NULL" : "";
            return $"  `{Q(c.Name)}` {type}{nn}";
        }).ToList();
        if (pkCols.Count > 0)
            colDefs.Add($"  PRIMARY KEY ({string.Join(", ", pkCols)})");

        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            $"CREATE TABLE IF NOT EXISTS `{Q(tableName)}` (\n{string.Join(",\n", colDefs)}\n)" +
            " ENGINE=InnoDB DEFAULT CHARSET=utf8mb4";
        cmd.ExecuteNonQuery();
    }

    public void AddColumn(string tableName, ColumnSchema column)
    {
        var type = TypeMappings.ResolveToDdl(BackendType.MySQL, column.SqlType, column.MaxLength);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE `{Q(tableName)}` ADD COLUMN `{Q(column.Name)}` {type} NULL";
        cmd.ExecuteNonQuery();
    }

    public void DropTable(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"DROP TABLE `{Q(tableName)}`";
        cmd.ExecuteNonQuery();
    }

    public void DropColumn(string tableName, string columnName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"ALTER TABLE `{Q(tableName)}` DROP COLUMN `{Q(columnName)}`";
        cmd.ExecuteNonQuery();
    }

    public bool SupportsForeignKeys => true;

    public IReadOnlyList<ForeignKeyInfo> GetForeignKeys()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT kcu.CONSTRAINT_NAME, kcu.TABLE_NAME, kcu.COLUMN_NAME, " +
            "       kcu.REFERENCED_TABLE_NAME, kcu.REFERENCED_COLUMN_NAME " +
            "FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu " +
            "WHERE kcu.TABLE_SCHEMA = DATABASE() " +
            "  AND kcu.REFERENCED_TABLE_NAME IS NOT NULL";
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
        var cascadeSql = cascade ? " ON DELETE CASCADE ON UPDATE CASCADE" : "";
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            $"ALTER TABLE `{Q(childTable)}` ADD CONSTRAINT `{Q(constraintName)}` " +
            $"FOREIGN KEY (`{Q(childColumn)}`) REFERENCES `{Q(parentTable)}`(`{Q(parentColumn)}`)" +
            cascadeSql;
        cmd.ExecuteNonQuery();
    }

    public void BeginTransaction() => _tx = _conn.BeginTransaction();
    public void Commit()   { _tx?.Commit();   _tx = null; }
    public void Rollback() { _tx?.Rollback(); _tx = null; }
    public void Dispose()  { _tx?.Dispose();  _conn.Dispose(); }

    private static string Q(string s) => s.Replace("`", "``");
}
