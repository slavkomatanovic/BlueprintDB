using System.IO;
using Microsoft.Data.SqlClient;

namespace Blueprint.App.Backend;

/// <summary>
/// Microsoft SQL Server (including Express) backend.
/// Connection string format:
/// Server=.\SQLEXPRESS;Database=dbname;Integrated Security=True;TrustServerCertificate=True;
/// or:
/// Server=host;Database=dbname;User Id=user;Password=password;TrustServerCertificate=True;
/// </summary>
public sealed class SqlServerBackendConnector : IBackendConnector
{
    private readonly SqlConnection _conn;
    private SqlTransaction? _tx;
    private string _schema = "dbo";

    public SqlServerBackendConnector(string connectionString)
        => _conn = new SqlConnection(connectionString);

    public void Open()
    {
        _conn.Open();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT SCHEMA_NAME()";
        _schema = cmd.ExecuteScalar()?.ToString() ?? "dbo";
    }

    public IReadOnlyList<string> GetTableNames()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES " +
            "WHERE TABLE_TYPE = 'BASE TABLE' AND TABLE_SCHEMA = @s ORDER BY TABLE_NAME";
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
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_SCHEMA = @s AND TABLE_NAME = @t ORDER BY ORDINAL_POSITION";
        cmd.Parameters.AddWithValue("@s", _schema);
        cmd.Parameters.AddWithValue("@t", tableName);
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    public IReadOnlyDictionary<string, CanonicalType> GetColumnTypes(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_SCHEMA = @s AND TABLE_NAME = @t";
        cmd.Parameters.AddWithValue("@s", _schema);
        cmd.Parameters.AddWithValue("@t", tableName);
        using var r = cmd.ExecuteReader();
        var dict = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase);
        while (r.Read())
            dict[r.GetString(0)] = TypeMappings.Resolve(BackendType.SqlServer, r.GetString(1));
        return dict;
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ReadAll(
        string tableName, IReadOnlyList<string> columns)
    {
        var cols = string.Join(", ", columns.Select(c => $"[{Q(c)}]"));
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {cols} FROM [{Q(_schema)}].[{Q(tableName)}]";
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
        cmd.CommandText = $"DELETE FROM [{Q(_schema)}].[{Q(tableName)}]";
        cmd.ExecuteNonQuery();
    }

    public void InsertRows(string tableName, IReadOnlyList<string> columns,
                           IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        var colList   = string.Join(", ", columns.Select(c => $"[{Q(c)}]"));
        var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var sql = $"INSERT INTO [{Q(_schema)}].[{Q(tableName)}] ({colList}) VALUES ({paramList})";

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
        var pkCols  = columns.Where(c => c.PrimaryKey).Select(c => $"[{Q(c.Name)}]").ToList();
        var colDefs = columns.Select(c =>
        {
            var type = TypeMappings.ResolveToDdl(BackendType.SqlServer, c.SqlType, c.MaxLength);
            var nn   = (c.NotNull || c.PrimaryKey) ? " NOT NULL" : " NULL";
            return $"  [{Q(c.Name)}] {type}{nn}";
        }).ToList();
        if (pkCols.Count > 0)
            colDefs.Add($"  PRIMARY KEY ({string.Join(", ", pkCols)})");

        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            $"IF NOT EXISTS (SELECT 1 FROM sys.tables t " +
            $"JOIN sys.schemas s ON t.schema_id = s.schema_id " +
            $"WHERE s.name = @s AND t.name = @tn)\n" +
            $"  CREATE TABLE [{Q(_schema)}].[{Q(tableName)}] (\n{string.Join(",\n", colDefs)}\n)";
        cmd.Parameters.AddWithValue("@s", _schema);
        cmd.Parameters.AddWithValue("@tn", tableName);
        cmd.ExecuteNonQuery();
    }

    public void AddColumn(string tableName, ColumnSchema column)
    {
        var type = TypeMappings.ResolveToDdl(BackendType.SqlServer, column.SqlType, column.MaxLength);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            $"IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS " +
            $"WHERE TABLE_SCHEMA=@s AND TABLE_NAME=@tn AND COLUMN_NAME=@cn)\n" +
            $"  ALTER TABLE [{Q(_schema)}].[{Q(tableName)}] ADD [{Q(column.Name)}] {type} NULL";
        cmd.Parameters.AddWithValue("@s", _schema);
        cmd.Parameters.AddWithValue("@tn", tableName);
        cmd.Parameters.AddWithValue("@cn", column.Name);
        cmd.ExecuteNonQuery();
    }

    public void DropTable(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText =
            $"IF OBJECT_ID(N'[{Q(_schema)}].[{Q(tableName)}]','U') IS NOT NULL " +
            $"DROP TABLE [{Q(_schema)}].[{Q(tableName)}]";
        cmd.ExecuteNonQuery();
    }

    public void DropColumn(string tableName, string columnName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = $"ALTER TABLE [{Q(_schema)}].[{Q(tableName)}] DROP COLUMN [{Q(columnName)}]";
        cmd.ExecuteNonQuery();
    }

    public bool SupportsForeignKeys => true;

    public IReadOnlyList<ForeignKeyInfo> GetForeignKeys()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT fk.name, " +
            "       OBJECT_NAME(fkc.parent_object_id), " +
            "       COL_NAME(fkc.parent_object_id, fkc.parent_column_id), " +
            "       OBJECT_NAME(fkc.referenced_object_id), " +
            "       COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) " +
            "FROM sys.foreign_keys fk " +
            "JOIN sys.foreign_key_columns fkc ON fk.object_id = fkc.constraint_object_id " +
            "JOIN sys.schemas s ON fk.schema_id = s.schema_id " +
            "WHERE s.name = @s";
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
            $"IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = @cn)\n" +
            $"  ALTER TABLE [{Q(_schema)}].[{Q(childTable)}] ADD CONSTRAINT [{Q(constraintName)}] " +
            $"  FOREIGN KEY ([{Q(childColumn)}]) REFERENCES [{Q(_schema)}].[{Q(parentTable)}]([{Q(parentColumn)}])" +
            cascadeSql;
        cmd.Parameters.AddWithValue("@cn", constraintName);
        cmd.ExecuteNonQuery();
    }

    public void BeginTransaction() => _tx = _conn.BeginTransaction();
    public void Commit()   { _tx?.Commit();   _tx = null; }
    public void Rollback() { _tx?.Rollback(); _tx = null; }
    public void Dispose()  { _tx?.Dispose();  _conn.Dispose(); }

    private static string Q(string s) => s.Replace("]", "]]");
}
