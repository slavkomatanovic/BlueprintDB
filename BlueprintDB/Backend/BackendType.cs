using System.IO;

namespace Blueprint.App.Backend;

public enum BackendType
{
    SQLite,
    Access,
    MySQL,
    MariaDB,
    SqlServer,
    PostgreSQL,
    DBase,
    Firebird,
    DB2,
    Oracle
}

/// <summary>
/// Creates the right IBackendConnector for a given connection string / file path.
/// To add a new backend: add an enum value above and one case in Create().
/// </summary>
public static class BackendConnectorFactory
{
    public static BackendType DetectFromPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".sqlite" or ".db"  => BackendType.SQLite,
            ".accdb"  or ".mdb" => BackendType.Access,
            ".fdb"    or ".gdb" => BackendType.Firebird,
            ".dbf"              => BackendType.DBase,
            _ when path.Contains("Host=",        StringComparison.OrdinalIgnoreCase) &&
                   path.Contains("Port=3306",    StringComparison.OrdinalIgnoreCase)
                => BackendType.MySQL,
            _ when path.Contains("Host=",        StringComparison.OrdinalIgnoreCase)
                => BackendType.PostgreSQL,
            _ when path.Contains("DataSource=",  StringComparison.OrdinalIgnoreCase) &&
                   path.Contains("Database=",    StringComparison.OrdinalIgnoreCase) &&
                   path.Contains("User=",        StringComparison.OrdinalIgnoreCase)
                => BackendType.Firebird,
            _ when path.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) &&
                   path.Contains("User Id=",     StringComparison.OrdinalIgnoreCase)
                => BackendType.Oracle,
            _ when path.Contains("Server=",      StringComparison.OrdinalIgnoreCase) &&
                   path.Contains("UID=",         StringComparison.OrdinalIgnoreCase)
                => BackendType.DB2,
            _ when path.Contains("Server=",      StringComparison.OrdinalIgnoreCase) &&
                  (path.Contains("Database=",    StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("Initial Catalog=", StringComparison.OrdinalIgnoreCase))
                => BackendType.SqlServer,
            _ => throw new NotSupportedException($"Cannot detect backend type from: {path}")
        };
    }

    public static IBackendConnector Create(string connectionString, BackendType type)
        => type switch
        {
            BackendType.SQLite     => new SqliteBackendConnector(connectionString),
            BackendType.Access     => new AccessBackendConnector(connectionString),
            BackendType.MySQL      => new MySqlBackendConnector(connectionString),
            BackendType.MariaDB    => new MySqlBackendConnector(connectionString),
            BackendType.SqlServer  => new SqlServerBackendConnector(connectionString),
            BackendType.PostgreSQL => new PostgreSqlBackendConnector(connectionString),
            BackendType.DBase      => new DBaseBackendConnector(connectionString),
            BackendType.Firebird   => new FirebirdBackendConnector(connectionString),
            BackendType.DB2        => new Db2BackendConnector(connectionString),
            BackendType.Oracle     => new OracleBackendConnector(connectionString),
            _ => throw new NotSupportedException($"Backend not supported: {type}")
        };

    /// <summary>Generiše DROP TABLE SQL za zadani backend tip.</summary>
    public static string GetDropTableSql(BackendType type, string tableName) => type switch
    {
        BackendType.Access     => $"DROP TABLE [{tableName}]",
        BackendType.SQLite     => $"DROP TABLE IF EXISTS \"{tableName}\"",
        BackendType.MySQL
        or BackendType.MariaDB => $"DROP TABLE `{tableName}`",
        BackendType.SqlServer  => $"IF OBJECT_ID(N'[dbo].[{tableName}]', 'U') IS NOT NULL\n    DROP TABLE [dbo].[{tableName}]",
        BackendType.PostgreSQL => $"DROP TABLE IF EXISTS \"{tableName}\"",
        BackendType.Firebird   => $"DROP TABLE \"{tableName}\"",
        BackendType.DB2        => $"DROP TABLE \"{tableName}\"",
        BackendType.Oracle     => $"DROP TABLE \"{tableName}\"",
        _                      => $"DROP TABLE {tableName}"
    };

    /// <summary>Generiše ALTER TABLE DROP COLUMN SQL za zadani backend tip.</summary>
    public static string GetDropColumnSql(BackendType type, string tableName, string columnName) => type switch
    {
        BackendType.Access     => $"ALTER TABLE [{tableName}] DROP COLUMN [{columnName}]",
        BackendType.SQLite     => $"ALTER TABLE \"{tableName}\" DROP COLUMN \"{columnName}\"",
        BackendType.MySQL
        or BackendType.MariaDB => $"ALTER TABLE `{tableName}` DROP COLUMN `{columnName}`",
        BackendType.SqlServer  => $"ALTER TABLE [dbo].[{tableName}] DROP COLUMN [{columnName}]",
        BackendType.PostgreSQL => $"ALTER TABLE \"{tableName}\" DROP COLUMN \"{columnName}\"",
        BackendType.Firebird   => $"ALTER TABLE \"{tableName}\" DROP \"{columnName}\"",
        BackendType.DB2        => $"ALTER TABLE \"{tableName}\" DROP COLUMN \"{columnName}\"",
        BackendType.Oracle     => $"ALTER TABLE \"{tableName}\" DROP COLUMN \"{columnName}\"",
        _                      => $"ALTER TABLE {tableName} DROP COLUMN {columnName}"
    };
}
