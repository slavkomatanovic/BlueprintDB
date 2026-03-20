namespace Blueprint.App.Backend;

/// <summary>FK constraint row returned by GetForeignKeys.</summary>
public record ForeignKeyInfo(
    string ConstraintName,
    string ChildTable,
    string ChildColumn,
    string ParentTable,
    string ParentColumn);

/// <summary>
/// Column metadata returned by GetColumnSchema.
/// Connectors that cannot determine type/size return only the Name; the rest default to empty/false/0.
/// </summary>
public record ColumnSchema(
    string Name,
    string SqlType    = "",
    bool   NotNull    = false,
    bool   PrimaryKey = false,
    int    MaxLength  = 0);

/// <summary>
/// Abstraction over any relational backend (SQLite, MS Access, MySQL, …).
/// Add a new backend by: (1) implementing this interface, (2) adding one case in BackendConnectorFactory.
/// </summary>
public interface IBackendConnector : IDisposable
{
    void Open();

    /// <summary>Returns all user-visible table names in the database.</summary>
    IReadOnlyList<string> GetTableNames();

    /// <summary>Returns column names for the given table.</summary>
    IReadOnlyList<string> GetColumnNames(string tableName);

    /// <summary>
    /// Returns full column schema for the given table.
    /// Default implementation returns name-only ColumnSchema records.
    /// Override in connectors that can provide richer info (type, size, nullability, PK).
    /// </summary>
    IReadOnlyList<ColumnSchema> GetColumnSchema(string tableName)
        => GetColumnNames(tableName)
           .Select(n => new ColumnSchema(n))
           .ToList();

    /// <summary>Reads all rows, returning only the requested columns.</summary>
    IReadOnlyList<IReadOnlyDictionary<string, object?>> ReadAll(string tableName, IReadOnlyList<string> columns);

    void DeleteAll(string tableName);

    void InsertRows(string tableName, IReadOnlyList<string> columns,
                    IEnumerable<IReadOnlyDictionary<string, object?>> rows);

    void BeginTransaction();
    void Commit();
    void Rollback();

    /// <summary>
    /// Creates a new table in the backend with the given column definitions.
    /// Columns from Blueprint metadata are passed in; use the SqlType stored there.
    /// </summary>
    void CreateTable(string tableName, IReadOnlyList<ColumnSchema> columns)
        => throw new NotSupportedException("DDL (CreateTable) is not supported for this backend.");

    /// <summary>
    /// Adds a single new column to an existing table.
    /// Note: most backends cannot add NOT NULL columns without a DEFAULT at ALTER time.
    /// </summary>
    void AddColumn(string tableName, ColumnSchema column)
        => throw new NotSupportedException("DDL (AddColumn) is not supported for this backend.");

    /// <summary>Drops (deletes) an entire table from the backend database.</summary>
    void DropTable(string tableName)
        => throw new NotSupportedException("DropTable is not supported for this backend.");

    /// <summary>Drops (removes) a single column from an existing table in the backend database.</summary>
    void DropColumn(string tableName, string columnName)
        => throw new NotSupportedException("DropColumn is not supported for this backend.");

    /// <summary>Whether this backend supports FK constraints via DDL.</summary>
    bool SupportsForeignKeys => false;

    /// <summary>Returns all FK constraints currently defined in the database.</summary>
    IReadOnlyList<ForeignKeyInfo> GetForeignKeys() => [];

    /// <summary>
    /// Adds a FK constraint on childTable.childColumn referencing parentTable.parentColumn.
    /// </summary>
    void AddForeignKey(string constraintName, string childTable, string childColumn,
                       string parentTable, string parentColumn, bool cascade)
        => throw new NotSupportedException("FK constraints are not supported for this backend.");
}
