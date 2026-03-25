using Npgsql;

namespace Blueprint.App.Backend;

/// <summary>
/// PostgreSQL backend.
/// Connection string: Host=host;Database=db;Username=user;Password=pass;
/// </summary>
public sealed class PostgreSqlBackendConnector : IBackendConnector
{
    private readonly NpgsqlConnection _conn;
    private NpgsqlTransaction? _tx;
    private string _schema = "public";

    public PostgreSqlBackendConnector(string connectionString)
        => _conn = new NpgsqlConnection(connectionString);

    public void Open()
    {
        _conn.Open();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT CURRENT_SCHEMA";
        _schema = cmd.ExecuteScalar()?.ToString() ?? "public";
    }

    public IReadOnlyList<string> GetTableNames()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT table_name FROM information_schema.tables " +
            "WHERE table_schema = @s AND table_type = 'BASE TABLE' " +
            "ORDER BY table_name";
        cmd.Parameters.AddWithValue("@s", _schema);
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public IReadOnlyList<string> GetColumnNames(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT column_name FROM information_schema.columns " +
            "WHERE table_schema = @s AND table_name = @t " +
            "ORDER BY ordinal_position";
        cmd.Parameters.AddWithValue("@s", _schema);
        cmd.Parameters.AddWithValue("@t", tableName);
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
        cmd.CommandText = $"SELECT {cols} FROM \"{Q(_schema)}\".\"{Q(tableName)}\"";
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
        cmd.CommandText = $"DELETE FROM \"{Q(_schema)}\".\"{Q(tableName)}\"";
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyDictionary<string, CanonicalType> GetColumnTypes(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT column_name, data_type FROM information_schema.columns " +
            "WHERE table_schema = @s AND table_name = @t";
        cmd.Parameters.AddWithValue("@s", _schema);
        cmd.Parameters.AddWithValue("@t", tableName);
        using var r = cmd.ExecuteReader();
        var dict = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase);
        while (r.Read())
            dict[r.GetString(0)] = TypeMappings.Resolve(BackendType.PostgreSQL, r.GetString(1));
        return dict;
    }

    public void InsertRows(string tableName, IReadOnlyList<string> columns,
                           IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        var colList   = string.Join(", ", columns.Select(c => $"\"{Q(c)}\""));
        var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var sql = $"INSERT INTO \"{Q(_schema)}\".\"{Q(tableName)}\" ({colList}) VALUES ({paramList})";

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

    private static string MapToPgType(ColumnSchema c)
    {
        var t   = (c.SqlType ?? "").Trim().ToUpperInvariant();
        var len = c.MaxLength > 0 ? c.MaxLength : 0;

        return t switch
        {
            "TEXT" or "CLOB" or "NTEXT" or "TINYTEXT"
                or "MEDIUMTEXT" or "LONGTEXT"          => "TEXT",

            "VARCHAR" or "NVARCHAR" or "CHARACTER VARYING"
                or "VARYING CHARACTER"                 => len > 0 ? $"VARCHAR({len})" : "TEXT",

            // CHAR without length → PostgreSQL CHAR(1) is a trap — use TEXT
            "CHAR" or "CHARACTER" or "NCHAR"           => len > 0 ? $"CHAR({len})" : "TEXT",

            "INTEGER" or "INT" or "INT4"
                or "MEDIUMINT" or "INT32"              => "INTEGER",
            "BIGINT" or "INT8" or "INT64"              => "BIGINT",
            "SMALLINT" or "INT2" or "TINYINT" or "INT16" => "SMALLINT",

            "BOOLEAN" or "BOOL"                        => "BOOLEAN",
            "BIT"                                      => len > 1 ? $"BIT({len})" : "BOOLEAN",

            "REAL" or "FLOAT4" or "FLOAT"              => "REAL",
            "DOUBLE" or "DOUBLE PRECISION" or "FLOAT8" => "DOUBLE PRECISION",

            "NUMERIC" or "DECIMAL" or "NUMBER"         => len > 0 ? $"NUMERIC({len})" : "NUMERIC",

            "DATE"                                     => "DATE",
            "TIME"                                     => "TIME",
            "DATETIME" or "TIMESTAMP"
                or "TIMESTAMP WITHOUT TIME ZONE"       => "TIMESTAMP",
            "TIMESTAMP WITH TIME ZONE" or "TIMESTAMPTZ" => "TIMESTAMPTZ",

            "BYTEA" or "BLOB" or "BINARY"
                or "VARBINARY" or "IMAGE"              => "BYTEA",

            "UUID"                                     => "UUID",
            "JSON"                                     => "JSON",
            "JSONB"                                     => "JSONB",

            _ => string.IsNullOrEmpty(c.SqlType) ? "TEXT" : c.SqlType
        };
    }

    public void CreateTable(string tableName, IReadOnlyList<ColumnSchema> columns)
    {
        var pkCols  = columns.Where(c => c.PrimaryKey).Select(c => $"\"{Q(c.Name)}\"").ToList();
        var colDefs = columns.Select(c =>
        {
            var canonical = TypeMappings.Resolve(BackendType.PostgreSQL, c.SqlType);
            var type = canonical != CanonicalType.Unknown
                ? TypeMappings.GetDdlType(BackendType.PostgreSQL, canonical, c.MaxLength)
                : MapToPgType(c);  // keep MapToPgType as fallback for unknown types
            var nn   = (c.NotNull || c.PrimaryKey) ? " NOT NULL" : "";
            return $"  \"{Q(c.Name)}\" {type}{nn}";
        }).ToList();
        if (pkCols.Count > 0)
            colDefs.Add($"  PRIMARY KEY ({string.Join(", ", pkCols)})");

        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            $"CREATE TABLE IF NOT EXISTS \"{Q(_schema)}\".\"{Q(tableName)}\" " +
            $"(\n{string.Join(",\n", colDefs)}\n)";
        cmd.ExecuteNonQuery();
    }

    public void AddColumn(string tableName, ColumnSchema column)
    {
        var canonical = TypeMappings.Resolve(BackendType.PostgreSQL, column.SqlType);
        var type = canonical != CanonicalType.Unknown
            ? TypeMappings.GetDdlType(BackendType.PostgreSQL, canonical, column.MaxLength)
            : MapToPgType(column);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            $"ALTER TABLE \"{Q(_schema)}\".\"{Q(tableName)}\" " +
            $"ADD COLUMN IF NOT EXISTS \"{Q(column.Name)}\" {type}";
        cmd.ExecuteNonQuery();
    }

    public void DropTable(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"DROP TABLE IF EXISTS \"{Q(_schema)}\".\"{Q(tableName)}\"";
        cmd.ExecuteNonQuery();
    }

    public void DropColumn(string tableName, string columnName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText =
            $"ALTER TABLE \"{Q(_schema)}\".\"{Q(tableName)}\" " +
            $"DROP COLUMN IF EXISTS \"{Q(columnName)}\"";
        cmd.ExecuteNonQuery();
    }

    public bool SupportsForeignKeys => true;

    public IReadOnlyList<ForeignKeyInfo> GetForeignKeys()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT tc.constraint_name, tc.table_name, kcu.column_name, " +
            "       ccu.table_name, ccu.column_name " +
            "FROM information_schema.table_constraints tc " +
            "JOIN information_schema.key_column_usage kcu " +
            "  ON tc.constraint_name = kcu.constraint_name AND tc.table_schema = kcu.table_schema " +
            "JOIN information_schema.constraint_column_usage ccu " +
            "  ON tc.constraint_name = ccu.constraint_name AND tc.table_schema = ccu.table_schema " +
            "WHERE tc.constraint_type = 'FOREIGN KEY' AND tc.table_schema = @s";
        cmd.Parameters.AddWithValue("@s", _schema);
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
            $"ALTER TABLE \"{Q(_schema)}\".\"{Q(childTable)}\" " +
            $"ADD CONSTRAINT \"{Q(constraintName)}\" " +
            $"FOREIGN KEY (\"{Q(childColumn)}\") " +
            $"REFERENCES \"{Q(_schema)}\".\"{Q(parentTable)}\"(\"{Q(parentColumn)}\")" +
            cascadeSql;
        cmd.ExecuteNonQuery();
    }

    public void BeginTransaction() => _tx = _conn.BeginTransaction();
    public void Commit()   { _tx?.Commit();   _tx = null; }
    public void Rollback() { _tx?.Rollback(); _tx = null; }
    public void Dispose()  { _tx?.Dispose();  _conn.Dispose(); }

    private static string Q(string s) => s.Replace("\"", "\"\"");
}
