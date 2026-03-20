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

    public void CreateTable(string tableName, IReadOnlyList<ColumnSchema> columns)
    {
        // Provjeri sve objekte (TABLE, VIEW, LINK...) — Access ne dozvoljava CREATE TABLE
        // ako je ime zauzeto bilo kojim tipom objekta, a GetTableNames() vraca samo TABLE.
        if (AnyObjectExists(tableName)) return;

        // Access DDL: [bracket] quoting, no IF NOT EXISTS, single-column PK inline
        var colDefs = columns.Select(c =>
        {
            var type = NormalizeAccessType(c.SqlType);
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
        var type = NormalizeAccessType(column.SqlType);
        using var cmd = new OleDbCommand(
            $"ALTER TABLE [{tableName}] ADD COLUMN [{column.Name}] {type}", _conn);
        cmd.ExecuteNonQuery();
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

            "DECIMAL"   or "NUMERIC"   or "NUMBER"                  => "DECIMAL",

            "BIT"       or "BOOLEAN"   or "BOOL"       or "YESNO"  => "YESNO",

            "DATE"      or "DATETIME"  or "TIMESTAMP"
                or "DATETIME2"         or "SMALLDATETIME"           => "DATETIME",

            "BINARY"    or "VARBINARY" or "BLOB"       or "IMAGE"
                or "LONGBINARY"                                      => "LONGBINARY",

            "CURRENCY"  or "MONEY"     or "SMALLMONEY"              => "CURRENCY",

            "AUTOINCREMENT" or "COUNTER"                            => "AUTOINCREMENT",

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
        // Access Jet SQL doesn't support ON DELETE/UPDATE CASCADE via DDL
        using var cmd = new OleDbCommand(
            $"ALTER TABLE [{childTable}] ADD CONSTRAINT [{constraintName}] " +
            $"FOREIGN KEY ([{childColumn}]) REFERENCES [{parentTable}]([{parentColumn}])", _conn);
        cmd.ExecuteNonQuery();
    }

    public void BeginTransaction() => _tx = _conn.BeginTransaction();
    public void Commit()   { _tx?.Commit();   _tx = null; }
    public void Rollback() { _tx?.Rollback(); _tx = null; }
    public void Dispose()  { _tx?.Dispose();  _conn.Dispose(); }
}
