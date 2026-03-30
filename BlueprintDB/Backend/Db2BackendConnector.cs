using System.IO;
using System.Runtime.InteropServices;
using IBM.Data.Db2;

namespace Blueprint.App.Backend;

/// <summary>
/// IBM DB2 backend via Net.IBM.Data.Db2 managed driver.
///
/// PREREQUISITE (Windows): IBM Data Server Driver Package must be installed.
/// Download free: https://www.ibm.com/support/fixcentral/ → "IBM Data Server Driver Package"
///
/// The connector auto-detects GSKit from (in order):
///   1. IBM_DB_HOME environment variable
///   2. IBM Data Server Driver Package default install path
///   3. Python ibm_db package (pip install ibm_db) — useful for development
///   4. NuGet-bundled clidriver (no GSKit — fails with AESEncryptADONET)
///
/// Connection string: Server=host:50000;Database=MYDB;UID=user;PWD=pass;
/// </summary>
public sealed class Db2BackendConnector : IBackendConnector
{
    private readonly DB2Connection _conn;
    private DB2Transaction? _tx;

    private const string MissingDriverMessage =
        "DB2 backend requires IBM Data Server Driver Package installed on this machine.\n\n" +
        "Download (free): https://www.ibm.com/support/fixcentral/\n" +
        "Search for: \"IBM Data Server Driver Package\" → Windows 64-bit\n\n" +
        "After installation, restart Blueprint and try again.";

    // True when a GSKit-capable clidriver was found at startup.
    private static readonly bool _gskitAvailable;

    // Runs once before any DB2Connection is created.
    // Resolves the IBM clidriver: prefers sources that include GSKit.
    static Db2BackendConnector()
    {
        var binDir = ResolveCliBinDir();
        if (binDir == null) return;

        AddToPath(binDir);
        SetDllDirectory(binDir);

        // GSKit is present when icc64/ exists alongside the main bin/ dir.
        _gskitAvailable = Directory.Exists(Path.Combine(binDir, "icc64"))
                       || HasGskitOnPath();
    }

    private static bool HasGskitOnPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        return path.Split(Path.PathSeparator)
                   .Any(d => File.Exists(Path.Combine(d, "gsk8ssl_64.dll"))
                          || File.Exists(Path.Combine(d, "gsk8ssl64.dll")));
    }

    private static string? ResolveCliBinDir()
    {
        // 1. Honour an existing IBM_DB_HOME (system install or user-set env var).
        var ibmHome = Environment.GetEnvironmentVariable("IBM_DB_HOME");
        if (!string.IsNullOrEmpty(ibmHome))
        {
            var d = Path.Combine(ibmHome, "bin");
            if (Directory.Exists(d)) return d;
        }

        // 2. Common default path from IBM Data Server Driver Package installer.
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var systemInstall = Path.Combine(programFiles, "IBM", "IBM DATA SERVER DRIVER", "clidriver", "bin");
        if (Directory.Exists(systemInstall)) return systemInstall;

        // 3. Python ibm_db package — bundles clidriver with GSKit (icc64/).
        //    Installed via: pip install ibm_db
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var pythonRoot = Path.Combine(appData, "Python");
        if (Directory.Exists(pythonRoot))
        {
            foreach (var pyDir in Directory.GetDirectories(pythonRoot, "Python3*"))
            {
                var candidate = Path.Combine(pyDir, "site-packages", "clidriver");
                var candidateBin = Path.Combine(candidate, "bin");
                if (Directory.Exists(Path.Combine(candidateBin, "icc64")))
                {
                    Environment.SetEnvironmentVariable("IBM_DB_HOME", candidate);
                    // GSKit DLLs live in icc64/ — add to PATH so the driver finds them.
                    AddToPath(Path.Combine(candidateBin, "icc64"));
                    return candidateBin;
                }
            }
        }

        // 4. Fall back to the NuGet-bundled clidriver (no GSKit — connection will fail
        //    with AESEncryptADONET unless IBM Data Server Driver Package is installed).
        var bundled = Path.Combine(AppContext.BaseDirectory, "clidriver", "bin");
        if (Directory.Exists(bundled))
        {
            Environment.SetEnvironmentVariable("IBM_DB_HOME",
                Path.GetDirectoryName(bundled)!);
            return bundled;
        }

        return null;
    }

    private static void AddToPath(string dir)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!path.Contains(dir, StringComparison.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("PATH", dir + Path.PathSeparator + path);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);

    public Db2BackendConnector(string connectionString)
    {
        if (!_gskitAvailable)
            throw new InvalidOperationException(MissingDriverMessage);

        if (!connectionString.Contains("Connect Timeout", StringComparison.OrdinalIgnoreCase))
            connectionString += ";Connect Timeout=15";
        if (!connectionString.Contains("Security", StringComparison.OrdinalIgnoreCase))
            connectionString += ";Security=none";
        if (!connectionString.Contains("Authentication", StringComparison.OrdinalIgnoreCase))
            connectionString += ";Authentication=SERVER";

        _conn = new DB2Connection(connectionString);
    }

    public void Open()
    {
        try
        {
            _conn.Open();
        }
        catch (Exception ex) when (IsGskitError(ex))
        {
            throw new InvalidOperationException(MissingDriverMessage, ex);
        }
    }

    // Catches AESEncryptADONET / SQL0902 in case GSKit detection was a false positive.
    private static bool IsGskitError(Exception ex)
    {
        var msg = ex.Message + (ex.InnerException?.Message ?? "");
        return msg.Contains("AESEncryptADONET", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("SQL0902", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("SQL1042", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("no context policies", StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> GetTableNames()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT TABNAME FROM SYSCAT.TABLES " +
            "WHERE TABSCHEMA = CURRENT SCHEMA AND TYPE = 'T' " +
            "ORDER BY TABNAME";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0).TrimEnd());
        return list;
    }

    public IReadOnlyList<string> GetColumnNames(string tableName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT COLNAME FROM SYSCAT.COLUMNS " +
            "WHERE TABSCHEMA = CURRENT SCHEMA AND TABNAME = @tabname " +
            "ORDER BY COLNO";
        cmd.Parameters.Add(new DB2Parameter("@tabname", tableName));
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0).TrimEnd());
        return list;
    }

    public IReadOnlyList<ColumnSchema> GetColumnSchema(string tableName)
    {
        var pkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var pkCmd = _conn.CreateCommand())
        {
            pkCmd.CommandText =
                "SELECT kcu.COLNAME FROM SYSCAT.KEYCOLUSE kcu " +
                "JOIN SYSCAT.TABCONST tc ON kcu.CONSTNAME = tc.CONSTNAME " +
                "  AND kcu.TABSCHEMA = tc.TABSCHEMA AND kcu.TABNAME = tc.TABNAME " +
                "WHERE tc.TABSCHEMA = CURRENT SCHEMA AND tc.TABNAME = @tabname AND tc.TYPE = 'P'";
            pkCmd.Parameters.Add(new DB2Parameter("@tabname", tableName));
            try
            {
                using var pkr = pkCmd.ExecuteReader();
                while (pkr.Read()) pkNames.Add(pkr.GetString(0).TrimEnd());
            }
            catch { /* ignore if catalog views unavailable */ }
        }

        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT COLNAME, TYPENAME, LENGTH, NULLS, IDENTITY " +
            "FROM SYSCAT.COLUMNS " +
            "WHERE TABNAME = @tabname AND TABSCHEMA = CURRENT SCHEMA " +
            "ORDER BY COLNO";
        cmd.Parameters.Add(new DB2Parameter("@tabname", tableName));
        using var r = cmd.ExecuteReader();
        var list = new List<ColumnSchema>();
        while (r.Read())
        {
            var name       = r.GetString(0).TrimEnd();
            var typeName   = r.GetString(1).TrimEnd();
            var rawLen     = r.IsDBNull(2) ? 0 : r.GetInt32(2);
            var maxLen     = rawLen > 0 && rawLen <= 8000 ? rawLen : 0;
            var notNull    = !r.IsDBNull(3) && r.GetString(3).Trim() == "N";
            var identChar  = r.IsDBNull(4) ? ' ' : r.GetString(4).Trim().FirstOrDefault();
            var isIdentity = identChar == 'Y' || identChar == 'D';
            var isPk       = pkNames.Contains(name);

            string sqlType;
            if (isIdentity)
                sqlType = "AutoNumber";
            else
            {
                var canonical = TypeMappings.Resolve(BackendType.DB2, typeName);
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
            "SELECT COLNAME, TYPENAME FROM SYSCAT.COLUMNS " +
            "WHERE TABNAME = @tabname AND TABSCHEMA = CURRENT SCHEMA " +
            "ORDER BY COLNO";
        cmd.Parameters.Add(new DB2Parameter("@tabname", tableName));
        try
        {
            using var r = cmd.ExecuteReader();
            var dict = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase);
            while (r.Read())
                dict[r.GetString(0)] = TypeMappings.Resolve(BackendType.DB2, r.GetString(1));
            return dict;
        }
        catch
        {
            return GetColumnNames(tableName)
                   .ToDictionary(c => c, _ => CanonicalType.Unknown, StringComparer.OrdinalIgnoreCase);
        }
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ReadAll(
        string tableName, IReadOnlyList<string> columns)
    {
        var cols = string.Join(", ", columns.Select(c => $"\"{Q(c)}\""));
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"SELECT {cols} FROM \"{Q(tableName)}\"";
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
        var paramList = string.Join(", ", columns.Select((_, i) => $"@p{i}"));
        var sql = $"INSERT INTO \"{Q(tableName)}\" ({colList}) VALUES ({paramList})";

        using var cmd = _conn.CreateCommand();
        cmd.Transaction = _tx;
        cmd.CommandText = sql;
        foreach (var row in rows)
        {
            cmd.Parameters.Clear();
            for (int i = 0; i < columns.Count; i++)
                cmd.Parameters.Add(new DB2Parameter($"@p{i}", ToDb2Value(row[columns[i]])));
            cmd.ExecuteNonQuery();
        }
    }

    private static object ToDb2Value(object? val) => val switch
    {
        null       => DBNull.Value,
        bool b     => b ? 1 : 0,
        DateOnly d => d.ToDateTime(TimeOnly.MinValue),
        Guid g     => g.ToString(),
        _          => val
    };

    public void CreateTable(string tableName, IReadOnlyList<ColumnSchema> columns)
    {
        using var existsCmd = _conn.CreateCommand();
        existsCmd.CommandText =
            "SELECT COUNT(*) FROM SYSCAT.TABLES " +
            "WHERE TABSCHEMA = CURRENT SCHEMA AND TABNAME = @tabname";
        existsCmd.Parameters.Add(new DB2Parameter("@tabname", tableName));
        var count = Convert.ToInt32(existsCmd.ExecuteScalar());
        if (count > 0) return;

        var pkCols  = columns.Where(c => c.PrimaryKey).Select(c => $"\"{Q(c.Name)}\"").ToList();
        var colDefs = columns.Select(c =>
        {
            var type = TypeMappings.ResolveToDdl(BackendType.DB2, c.SqlType, c.MaxLength);
            var nn   = (c.NotNull || c.PrimaryKey || TypeMappings.IsAutoNumberType(c.SqlType)) ? " NOT NULL" : "";
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
        var type = TypeMappings.ResolveToDdl(BackendType.DB2, column.SqlType, column.MaxLength);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            $"ALTER TABLE \"{Q(tableName)}\" ADD COLUMN \"{Q(column.Name)}\" {type}";
        cmd.ExecuteNonQuery();
    }

    public void DropTable(string tableName)
    {
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
            "SELECT r.CONSTNAME, r.TABNAME, fk.COLNAME, r.REFTABNAME, pk.COLNAME " +
            "FROM SYSCAT.REFERENCES r " +
            "JOIN SYSCAT.KEYCOLUSE fk " +
            "  ON fk.CONSTNAME = r.CONSTNAME AND fk.TABSCHEMA = r.TABSCHEMA AND fk.TABNAME = r.TABNAME " +
            "JOIN SYSCAT.KEYCOLUSE pk " +
            "  ON pk.CONSTNAME = r.REFKEYNAME AND pk.COLSEQ = fk.COLSEQ " +
            "WHERE r.TABSCHEMA = CURRENT SCHEMA " +
            "ORDER BY r.CONSTNAME, fk.COLSEQ";
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
        var cascadeSql = cascade ? " ON DELETE CASCADE" : "";
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            $"ALTER TABLE \"{Q(childTable)}\" ADD CONSTRAINT \"{Q(constraintName)}\" " +
            $"FOREIGN KEY (\"{Q(childColumn)}\") " +
            $"REFERENCES \"{Q(parentTable)}\"(\"{Q(parentColumn)}\")" +
            cascadeSql;
        cmd.ExecuteNonQuery();
    }

    public void BeginTransaction() => _tx = (DB2Transaction)_conn.BeginTransaction();
    public void Commit()   { _tx?.Commit();   _tx = null; }
    public void Rollback() { _tx?.Rollback(); _tx = null; }
    public void Dispose()  { _tx?.Dispose();  _conn.Dispose(); }

    private static string Q(string s) => s.Replace("\"", "\"\"");
}
