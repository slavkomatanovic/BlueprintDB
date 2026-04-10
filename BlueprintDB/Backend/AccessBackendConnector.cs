using System.Data;
using System.Data.OleDb;
using System.IO;

namespace Blueprint.App.Backend;

/// <summary>
/// MS Access backend. Requires the Microsoft ACE OleDb provider to be installed on the host machine.
/// .accdb files use ACE 16.0; .mdb files use Jet 4.0.
/// </summary>
public sealed class AccessBackendConnector : IBackendConnector
{
    private readonly OleDbConnection _conn;
    private OleDbTransaction? _tx;

    public AccessBackendConnector(string dbPath)
    {
        var ext      = Path.GetExtension(dbPath).ToLowerInvariant();
        var provider = ext == ".accdb" ? "Microsoft.ACE.OLEDB.16.0" : "Microsoft.Jet.OLEDB.4.0";
        _conn = new OleDbConnection($"Provider={provider};Data Source={dbPath};");
    }

    public void Open() => _conn.Open();

    public IReadOnlyList<string> GetTableNames()
    {
        var schema = _conn.GetOleDbSchemaTable(
            OleDbSchemaGuid.Tables, new object?[] { null, null, null, "TABLE" });
        var list = new List<string>();
        if (schema == null) return list;
        foreach (DataRow row in schema.Rows)
        {
            var name = row["TABLE_NAME"]?.ToString() ?? "";
            if (!name.StartsWith("MSys", StringComparison.OrdinalIgnoreCase))
                list.Add(name);
        }
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

    public IReadOnlyList<ColumnSchema> GetColumnSchema(string tableName)
    {
        // Prikupi PK kolone
        var pkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var pkSchema = _conn.GetOleDbSchemaTable(
                OleDbSchemaGuid.Primary_Keys, new object?[] { null, null, tableName });
            if (pkSchema != null)
                foreach (DataRow row in pkSchema.Rows)
                    pkNames.Add(row["COLUMN_NAME"]?.ToString() ?? "");
        }
        catch { /* ignorisati ako PK schema nije dostupna */ }

        var schema = _conn.GetOleDbSchemaTable(
            OleDbSchemaGuid.Columns, new object?[] { null, null, tableName, null });
        var list = new List<ColumnSchema>();
        if (schema == null) return list;

        foreach (DataRow row in schema.Rows)
        {
            var name     = row["COLUMN_NAME"]?.ToString() ?? "";
            var typeCode = row["DATA_TYPE"] is short s ? (int)s : row["DATA_TYPE"] is int n ? n : 0;
            var nullable = row["IS_NULLABLE"] is bool b ? b : true;
            var isPk     = pkNames.Contains(name);
            var maxLen   = 0;
            if (row["CHARACTER_MAXIMUM_LENGTH"] != DBNull.Value)
            {
                var ml = Convert.ToInt32(row["CHARACTER_MAXIMUM_LENGTH"]);
                if (ml > 0 && ml <= 8000) maxLen = ml;
            }

            // PK + Integer type = AutoNumber in Access (almost without exception)
            string sqlType;
            if (isPk && typeCode == 3)
            {
                sqlType = "AutoNumber";
            }
            else
            {
                var canonical = MapOleDbType(typeCode);
                sqlType = TypeMappings.CanonicalToAdo(canonical, maxLen);
            }
            list.Add(new ColumnSchema(name, sqlType, !nullable, isPk, maxLen));
        }
        return list;
    }

    public IReadOnlyDictionary<string, CanonicalType> GetColumnTypes(string tableName)
    {
        var schema = _conn.GetOleDbSchemaTable(
            OleDbSchemaGuid.Columns, new object?[] { null, null, tableName, null });
        var dict = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase);
        if (schema == null) return dict;
        foreach (DataRow row in schema.Rows)
        {
            var colName  = row["COLUMN_NAME"]?.ToString() ?? "";
            var typeCode = row["DATA_TYPE"] is short s ? (int)s : (row["DATA_TYPE"] is int n ? n : 0);
            dict[colName] = MapOleDbType(typeCode);
        }
        return dict;
    }

    private static CanonicalType MapOleDbType(int code) => code switch
    {
        2 or 3 or 16 or 17 or 18 or 19 => CanonicalType.Int32,     // SmallInt, Integer, TinyInt variants
        20 or 21                        => CanonicalType.Int64,     // BigInt, UnsignedBigInt
        4 or 5                          => CanonicalType.Double,    // Single, Double
        6 or 14 or 131                  => CanonicalType.Decimal,   // Currency, Decimal, Numeric
        7 or 133 or 134 or 135          => CanonicalType.DateTime,  // Date, DBDate, DBTime, DBTimeStamp
        11                              => CanonicalType.Boolean,   // Boolean (Yes/No)
        128 or 204 or 205               => CanonicalType.Bytes,     // Binary, VarBinary, LongVarBinary
        201 or 203                      => CanonicalType.Text,      // LongVarChar / LongVarWChar (Memo)
        130 or 200 or 202               => CanonicalType.Text,      // WChar, VarChar, VarWChar
        72                              => CanonicalType.Guid,      // adGUID
        _                               => CanonicalType.Text,      // default / unknown
    };

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
        foreach (var row in rows)
        {
            cmd.Parameters.Clear();
            for (int i = 0; i < columns.Count; i++)
                cmd.Parameters.Add(MakeParam(row[columns[i]]));
            cmd.ExecuteNonQuery();
        }
    }

    public void CreateTable(string tableName, IReadOnlyList<ColumnSchema> columns)
    {
        // Provjeri sve objekte (TABLE, VIEW, LINK...) — Access ne dozvoljava CREATE TABLE
        // ako je ime zauzeto bilo kojim tipom objekta, a GetTableNames() vraca samo TABLE.
        if (AnyObjectExists(tableName)) return;

        // Access DDL: [bracket] quoting, no IF NOT EXISTS, single-column PK inline
        var colDefs = columns.Select(c =>
        {
            var type = ResolveAccessDdl(c.SqlType, c.MaxLength);
            var pk   = c.PrimaryKey ? " PRIMARY KEY" : "";
            var nn   = c.NotNull && !c.PrimaryKey ? " NOT NULL" : "";
            return $"[{c.Name}] {type}{pk}{nn}";
        });
        using var cmd = new OleDbCommand(
            $"CREATE TABLE [{tableName}] ({string.Join(", ", colDefs)})", _conn);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Vraca true ako ime vec postoji kao bilo koji Access objekat (tabela, query, linked tabela).
    /// Koristimo null filter za TABLE_TYPE da uhvatimo sve tipove.
    /// </summary>
    private bool AnyObjectExists(string name)
    {
        var schema = _conn.GetOleDbSchemaTable(
            OleDbSchemaGuid.Tables, new object?[] { null, null, null, null });
        if (schema == null) return false;
        foreach (DataRow row in schema.Rows)
        {
            var objName = row["TABLE_NAME"]?.ToString() ?? "";
            if (string.Equals(objName, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public void AddColumn(string tableName, ColumnSchema column)
    {
        var type = ResolveAccessDdl(column.SqlType, column.MaxLength);
        using var cmd = new OleDbCommand(
            $"ALTER TABLE [{tableName}] ADD COLUMN [{column.Name}] {type}", _conn);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Razrješava tip kolone u Access DDL string.
    /// Proba sve backend canonical mape (da podrži ADO imena, SQLite tipove, ANSI tipove itd.),
    /// a za Access-specifične tipove (AUTOINCREMENT, COUNTER, AUTONUMBER) koristi NormalizeAccessType.
    /// </summary>
    private static string ResolveAccessDdl(string? sqlType, int maxLength)
    {
        // AutoNumber / identity → AUTOINCREMENT (must be checked before canonical lookup)
        if (TypeMappings.IsAutoNumberType(sqlType?.Trim()))
            return "AUTOINCREMENT";

        var canonical = TypeMappings.ResolveFromAny(sqlType);
        if (canonical != CanonicalType.Unknown)
            return TypeMappings.GetDdlType(BackendType.Access, canonical, maxLength);
        return NormalizeAccessType(sqlType);
    }

    // Access Jet SQL ne prepoznaje ANSI tipove — mapiramo na Access-kompatibilne DDL tipove.
    private static string NormalizeAccessType(string? sqlType)
    {
        if (string.IsNullOrWhiteSpace(sqlType)) return "TEXT";

        // Izvuci bazni tip bez dimenzija npr. "VARCHAR(255)" -> "VARCHAR"
        var baseName = sqlType.Trim().Split('(')[0].Trim().ToUpperInvariant();

        return baseName switch
        {
            "VARCHAR"   or "NVARCHAR"  or "CHAR"     or "NCHAR"
                or "CHARACTER" or "CHARACTER VARYING" or "TEXT"
                or "TINYTEXT"  or "MEDIUMTEXT"        or "CLOB"   => "TEXT",

            "LONGTEXT"  or "MEMO"      or "LONGVARCHAR" or "NTEXT" => "LONGTEXT",

            "INT"       or "INTEGER"   or "SMALLINT"   or "TINYINT"
                or "INT2" or "INT4"                                 => "INTEGER",

            "BIGINT"    or "INT8"      or "LONG"                    => "LONG",

            "FLOAT"     or "REAL"      or "DOUBLE"
                or "DOUBLE PRECISION"  or "FLOAT8"                  => "DOUBLE",

            "DECIMAL"   or "NUMERIC"   or "NUMBER"                  => "DECIMAL(18,4)",

            "BIT"       or "BOOLEAN"   or "BOOL"       or "YESNO"  => "YESNO",

            "DATE"      or "DATETIME"  or "TIMESTAMP"
                or "DATETIME2"         or "SMALLDATETIME"           => "DATETIME",

            "BINARY"    or "VARBINARY" or "BLOB"       or "IMAGE"
                or "LONGBINARY"                                      => "LONGBINARY",

            "CURRENCY"  or "MONEY"     or "SMALLMONEY"              => "CURRENCY",

            "AUTOINCREMENT" or "COUNTER" or "AUTONUMBER"             => "AUTOINCREMENT",

            _ => "TEXT"   // sigurni fallback za nepoznate tipove
        };
    }

    public void DropTable(string tableName)
    {
        using var cmd = new OleDbCommand($"DROP TABLE [{tableName}]", _conn);
        cmd.ExecuteNonQuery();
    }

    public void DropColumn(string tableName, string columnName)
    {
        using var cmd = new OleDbCommand($"ALTER TABLE [{tableName}] DROP COLUMN [{columnName}]", _conn);
        cmd.ExecuteNonQuery();
    }

    public bool SupportsForeignKeys => true;

    public IReadOnlyList<ForeignKeyInfo> GetForeignKeys()
    {
        // Restrictions: PK_TABLE_CATALOG, PK_TABLE_SCHEMA, PK_TABLE_NAME,
        //               FK_TABLE_CATALOG, FK_TABLE_SCHEMA, FK_TABLE_NAME
        var schema = _conn.GetOleDbSchemaTable(
            OleDbSchemaGuid.Foreign_Keys, new object?[] { null, null, null, null, null, null });
        var list = new List<ForeignKeyInfo>();
        if (schema == null) return list;
        foreach (DataRow row in schema.Rows)
        {
            list.Add(new ForeignKeyInfo(
                row["FK_NAME"]?.ToString()      ?? "",
                row["FK_TABLE_NAME"]?.ToString() ?? "",
                row["FK_COLUMN_NAME"]?.ToString() ?? "",
                row["PK_TABLE_NAME"]?.ToString() ?? "",
                row["PK_COLUMN_NAME"]?.ToString() ?? ""));
        }
        return list;
    }

    public void AddForeignKey(string constraintName, string childTable, string childColumn,
                              string parentTable, string parentColumn, bool cascade)
    {
        // Access requires the child column to be indexed before a FK constraint can be defined.
        // If the column was just added via ADD COLUMN it won't have an index yet.
        EnsureColumnIndexed(childTable, childColumn);

        // Access Jet SQL doesn't support ON DELETE/UPDATE CASCADE via DDL
        using var cmd = new OleDbCommand(
            $"ALTER TABLE [{childTable}] ADD CONSTRAINT [{constraintName}] " +
            $"FOREIGN KEY ([{childColumn}]) REFERENCES [{parentTable}]([{parentColumn}])", _conn);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Creates a non-unique index on the column if it doesn't already have one.
    /// Access throws "Invalid field definition" when adding a FK on an unindexed column.
    /// </summary>
    private void EnsureColumnIndexed(string tableName, string columnName)
    {
        try
        {
            var indexSchema = _conn.GetOleDbSchemaTable(
                OleDbSchemaGuid.Indexes, new object?[] { null, null, null, null, tableName });
            if (indexSchema != null)
            {
                foreach (DataRow row in indexSchema.Rows)
                {
                    if (string.Equals(row["COLUMN_NAME"]?.ToString(), columnName,
                            StringComparison.OrdinalIgnoreCase))
                        return; // already indexed
                }
            }

            var idxName = $"IX_{tableName}_{columnName}";
            using var cmd = new OleDbCommand(
                $"CREATE INDEX [{idxName}] ON [{tableName}] ([{columnName}])", _conn);
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Ignore — if index creation fails the FK DDL will throw with the original error
        }
    }

    // OleDb ne podržava automatsku konverziju tipova via AddWithValue —
    // mora se eksplicitno navesti OleDbType koji odgovara Access tipu kolone.
    // long → Integer (32-bit); Access nema native Int64.
    private static OleDbParameter MakeParam(object? value)
    {
        if (value == null || value is DBNull)
            return new OleDbParameter("p", OleDbType.VarWChar) { Value = DBNull.Value };

        return value switch
        {
            bool b      => new OleDbParameter("p", OleDbType.Boolean)      { Value = b },
            int n       => new OleDbParameter("p", OleDbType.Integer)      { Value = n },
            long n      => new OleDbParameter("p", OleDbType.Integer)      { Value = (int)Math.Clamp(n, int.MinValue, int.MaxValue) },
            double d    => new OleDbParameter("p", OleDbType.Double)       { Value = d },
            decimal d   => new OleDbParameter("p", OleDbType.Double)        { Value = (double)d },
            DateOnly d  => new OleDbParameter("p", OleDbType.Date)         { Value = d.ToDateTime(TimeOnly.MinValue) },
            DateTime dt => new OleDbParameter("p", OleDbType.Date)         { Value = dt },
            byte[] b    => new OleDbParameter("p", OleDbType.LongVarBinary){ Value = b },
            Guid g      => new OleDbParameter("p", OleDbType.Guid)         { Value = g },
            string s    => new OleDbParameter("p", OleDbType.LongVarWChar) { Value = s },
            _           => new OleDbParameter("p", OleDbType.LongVarWChar) { Value = value.ToString() }
        };
    }

    public void BeginTransaction() => _tx = _conn.BeginTransaction();
    public void Commit()   { _tx?.Commit();   _tx = null; }
    public void Rollback() { _tx?.Rollback(); _tx = null; }
    public void Dispose()  { _tx?.Dispose();  _conn.Dispose(); }
}
