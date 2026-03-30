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

    public IReadOnlyList<ColumnSchema> GetColumnSchema(string tableName)
    {
        // Primary key columns
        var pkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pkCmd = _conn.CreateCommand())
        {
            pkCmd.CommandText =
                "SELECT TRIM(iseg.RDB$FIELD_NAME) " +
                "FROM RDB$RELATION_CONSTRAINTS rc " +
                "JOIN RDB$INDEX_SEGMENTS iseg ON rc.RDB$INDEX_NAME = iseg.RDB$INDEX_NAME " +
                "WHERE rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY' " +
                "  AND TRIM(rc.RDB$RELATION_NAME) = @t";
            pkCmd.Parameters.AddWithValue("@t", FbId(tableName));
            using var pkr = pkCmd.ExecuteReader();
            while (pkr.Read()) pkNames.Add(pkr.GetString(0).TrimEnd());
        }

        using var cmd = _conn.CreateCommand();
        // RDB$IDENTITY_TYPE: 0=ALWAYS, 1=BY DEFAULT — both are auto-increment (Firebird 3+)
        // RDB$FIELD_LENGTH is in bytes; for VARCHAR it's char length if RDB$CHARACTER_LENGTH is set
        cmd.CommandText =
            "SELECT TRIM(rf.RDB$FIELD_NAME), f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE, " +
            "       COALESCE(f.RDB$CHARACTER_LENGTH, f.RDB$FIELD_LENGTH), " +
            "       rf.RDB$NULL_FLAG, rf.RDB$IDENTITY_TYPE " +
            "FROM RDB$RELATION_FIELDS rf " +
            "JOIN RDB$FIELDS f ON rf.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME " +
            "WHERE UPPER(rf.RDB$RELATION_NAME) = UPPER(@t) " +
            "ORDER BY rf.RDB$FIELD_POSITION";
        cmd.Parameters.AddWithValue("@t", FbId(tableName));
        using var r = cmd.ExecuteReader();
        var list = new List<ColumnSchema>();
        while (r.Read())
        {
            var name       = r.GetString(0).TrimEnd();
            var typeCode   = r.GetInt32(1);
            var subType    = r.IsDBNull(2) ? 0 : r.GetInt32(2);
            var rawLen     = r.IsDBNull(3) ? 0 : r.GetInt32(3);
            var maxLen     = rawLen > 0 && rawLen <= 8000 ? rawLen : 0;
            var notNull    = !r.IsDBNull(4) && r.GetInt32(4) == 1;
            var isIdentity = !r.IsDBNull(5); // RDB$IDENTITY_TYPE IS NOT NULL
            var isPk       = pkNames.Contains(name);

            string sqlType;
            if (isIdentity)
            {
                sqlType = "AutoNumber";
            }
            else
            {
                // Firebird field type codes: 7=SMALLINT,8=INTEGER,16=BIGINT,10=FLOAT,
                //   27=DOUBLE,37=VARCHAR,14=CHAR,261=BLOB,12=DATE,13=TIME,35=TIMESTAMP,23=BOOLEAN
                var canonical = typeCode switch
                {
                    7   => CanonicalType.Int32,
                    8   => CanonicalType.Int32,
                    16  => CanonicalType.Int64,
                    10  => CanonicalType.Double,
                    27  => CanonicalType.Double,
                    37  => CanonicalType.Text,
                    14  => CanonicalType.Text,
                    261 => subType == 1 ? CanonicalType.Text : CanonicalType.Bytes,
                    12  => CanonicalType.Date,
                    13  => CanonicalType.Text,
                    35  => CanonicalType.DateTime,
                    23  => CanonicalType.Boolean,
                    _   => CanonicalType.Unknown
                };
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
            "SELECT rf.RDB$FIELD_NAME, f.RDB$FIELD_TYPE, f.RDB$FIELD_SUB_TYPE " +
            "FROM RDB$RELATION_FIELDS rf " +
            "JOIN RDB$FIELDS f ON rf.RDB$FIELD_SOURCE = f.RDB$FIELD_NAME " +
            "WHERE UPPER(rf.RDB$RELATION_NAME) = UPPER(@t)";
        cmd.Parameters.AddWithValue("@t", FbId(tableName));
        using var r = cmd.ExecuteReader();
        var dict = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase);
        while (r.Read())
        {
            var colName = r.GetString(0).Trim();
            // Firebird field type codes: 7=SMALLINT,8=INTEGER,16=BIGINT,10=FLOAT,
            //   27=DOUBLE,37=VARCHAR,14=CHAR,261=BLOB,12=DATE,13=TIME,35=TIMESTAMP,23=BOOLEAN
            var typeCode = r.GetInt32(1);
            var subType  = r.IsDBNull(2) ? 0 : r.GetInt32(2);
            var canonical = typeCode switch
            {
                7   => CanonicalType.Int32,
                8   => CanonicalType.Int32,
                16  => CanonicalType.Int64,
                10  => CanonicalType.Double,
                27  => CanonicalType.Double,
                37  => CanonicalType.Text,
                14  => CanonicalType.Text,
                261 => subType == 1 ? CanonicalType.Text : CanonicalType.Bytes,
                12  => CanonicalType.Date,
                13  => CanonicalType.Text,   // TIME → text
                35  => CanonicalType.DateTime,
                23  => CanonicalType.Boolean,
                _   => CanonicalType.Unknown
            };
            dict[colName] = canonical;
        }
        return dict;
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
        foreach (var row in rows)
        {
            cmd.Parameters.Clear();
            for (int i = 0; i < columns.Count; i++)
                cmd.Parameters.AddWithValue($"@p{i}", ToFbValue(row[columns[i]]));
            cmd.ExecuteNonQuery();
        }
    }

    private static object ToFbValue(object? val) => val switch
    {
        null          => DBNull.Value,
        bool b        => b ? 1 : 0,              // Firebird 3+ supports BOOLEAN but SMALLINT is universal
        DateOnly d    => d.ToDateTime(TimeOnly.MinValue),
        Guid g        => g.ToString(),
        _             => val
    };

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
            var type = TypeMappings.ResolveToDdl(BackendType.Firebird, c.SqlType, c.MaxLength);
            var nn   = (c.NotNull || c.PrimaryKey || TypeMappings.IsAutoNumberType(c.SqlType)) ? " NOT NULL" : "";
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
        var type = TypeMappings.ResolveToDdl(BackendType.Firebird, column.SqlType, column.MaxLength);
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
