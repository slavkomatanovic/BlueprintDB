using System.Data;
using System.Data.OleDb;
using System.IO;
using System.Text;

namespace Blueprint.App.Backend;

/// <summary>
/// dBase / FoxPro backend via OleDb (Jet/ACE) za čitanje i DDL,
/// i direktnim binarnim I/O na .dbf fajlovima za pisanje.
///
/// ACE OleDb ima poznati bug: ne upisuje string vrijednosti u CHARACTER
/// polja putem SQL INSERT-a (ni parametarski ni literalni pristup ne rade).
/// Zaobilazimo to direktnim pisanjem u .dbf binarni format.
/// </summary>
public sealed class DBaseBackendConnector : IBackendConnector
{
    private readonly OleDbConnection _conn;
    private OleDbTransaction? _tx;
    private readonly string _folderPath;

    public DBaseBackendConnector(string folderPath)
    {
        _folderPath = folderPath;
        string cs;
        try
        {
            cs = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={folderPath};Extended Properties=\"dBASE IV;\";";
            _ = new OleDbConnection(cs);
        }
        catch
        {
            cs = $"Provider=Microsoft.Jet.OLEDB.4.0;Data Source={folderPath};Extended Properties=\"dBASE IV;\";";
        }
        _conn = new OleDbConnection(cs);
    }

    public void Open() => _conn.Open();

    // ── Read / DDL (ACE OleDb) ────────────────────────────────────────────────

    public IReadOnlyList<string> GetTableNames()
    {
        var schema = _conn.GetOleDbSchemaTable(
            OleDbSchemaGuid.Tables, new object?[] { null, null, null, "TABLE" });
        var list = new List<string>();
        if (schema == null) return list;
        foreach (DataRow row in schema.Rows)
        {
            var name = row["TABLE_NAME"]?.ToString() ?? "";
            if (name.EndsWith(".dbf", StringComparison.OrdinalIgnoreCase))
                name = name[..^4];
            list.Add(name);
        }
        return list;
    }

    public IReadOnlyList<string> GetColumnNames(string tableName)
    {
        // If we have a sidecar with full names (>10 chars), return those
        var sidecar = SidecarPath(tableName);
        if (File.Exists(sidecar))
        {
            var lines = File.ReadAllLines(sidecar)
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .ToList();
            if (lines.Count > 0) return lines;
        }

        using var cmd = new OleDbCommand($"SELECT * FROM [{tableName}] WHERE 1=0", _conn);
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        for (int i = 0; i < r.FieldCount; i++)
            list.Add(r.GetName(i));
        return list;
    }

    public IReadOnlyDictionary<string, CanonicalType> GetColumnTypes(string tableName)
    {
        try
        {
            using var cmd = new OleDbCommand($"SELECT * FROM [{tableName}] WHERE 1=0", _conn);
            using var r   = cmd.ExecuteReader();

            // Collect ACE types indexed by position
            var types = new List<CanonicalType>();
            for (int i = 0; i < r.FieldCount; i++)
                types.Add(DotNetTypeToCanonical(r.GetFieldType(i)));

            // Use full column names from sidecar if available (ACE truncates to 10 chars)
            var names = GetColumnNames(tableName);
            var dict  = new Dictionary<string, CanonicalType>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Math.Min(names.Count, types.Count); i++)
                dict[names[i]] = types[i];
            return dict;
        }
        catch
        {
            return GetColumnNames(tableName)
                   .ToDictionary(c => c, _ => CanonicalType.Unknown, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static CanonicalType DotNetTypeToCanonical(Type? t)
    {
        if (t == typeof(string))   return CanonicalType.Text;
        if (t == typeof(bool))     return CanonicalType.Boolean;
        if (t == typeof(int))      return CanonicalType.Int32;
        if (t == typeof(short))    return CanonicalType.Int32;
        if (t == typeof(long))     return CanonicalType.Int64;
        if (t == typeof(float))    return CanonicalType.Double;
        if (t == typeof(double))   return CanonicalType.Double;
        if (t == typeof(decimal))  return CanonicalType.Decimal;
        if (t == typeof(DateTime)) return CanonicalType.DateTime;
        if (t == typeof(byte[]))   return CanonicalType.Bytes;
        if (t == typeof(Guid))     return CanonicalType.Guid;
        return CanonicalType.Unknown;
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> ReadAll(
        string tableName, IReadOnlyList<string> columns)
    {
        // dBase .dbf field names are max 10 chars — ACE only knows the truncated names.
        // Build SELECT with truncated names but key result rows by full names.
        var truncated = columns.Select(c => c.Length > 10 ? c[..10] : c).ToList();
        var cols = string.Join(", ", truncated.Select(c => $"[{c}]"));
        using var cmd = new OleDbCommand($"SELECT {cols} FROM [{tableName}]", _conn, _tx);
        using var r = cmd.ExecuteReader();
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        while (r != null && r.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < columns.Count; i++)
            {
                object? val = r.IsDBNull(i) ? null : r.GetValue(i);
                if (val is string s) val = s.TrimEnd();
                row[columns[i]] = val; // store under full name
            }
            rows.Add(row);
        }
        return rows;
    }

    // ── Write (direktan binarni .dbf I/O) ────────────────────────────────────

    /// <summary>
    /// Briše sve redove iz .dbf fajla direktnim binarnim pristupom:
    /// postavljamo broj zapisa na 0 i truncatujemo fajl na header + EOF marker.
    /// </summary>
    public void DeleteAll(string tableName)
    {
        var path = DbfPath(tableName);
        if (!File.Exists(path)) return;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        if (fs.Length < 32) return;

        // Čitamo header da nađemo veličinu header-a (offset prvog zapisa)
        var hdr = new byte[32];
        fs.Read(hdr, 0, 32);
        int headerSize = BitConverter.ToUInt16(hdr, 8);

        // Postavljamo broj zapisa na 0
        fs.Seek(4, SeekOrigin.Begin);
        fs.Write(new byte[4], 0, 4);

        // Truncatujemo fajl: header + 0x1A EOF marker
        fs.SetLength(headerSize + 1);
        fs.Seek(headerSize, SeekOrigin.Begin);
        fs.WriteByte(0x1A);
    }

    /// <summary>
    /// Upisuje redove direktno u .dbf fajl zaobilazeći ACE OleDb.
    /// Čita definicije polja iz .dbf headera i piše binarne zapise.
    /// </summary>
    public void InsertRows(string tableName, IReadOnlyList<string> columns,
                           IEnumerable<IReadOnlyDictionary<string, object?>> rows)
    {
        var path = DbfPath(tableName);
        if (!File.Exists(path))
        {
            LogService.Warning("DBase", $"InsertRows: file not found: {path}");
            return;
        }

        var fields = DbfReadFields(path);
        LogService.Info("DBase", $"InsertRows '{tableName}' dbf fields: " +
            string.Join(", ", fields.Select(f => $"{f.Name}({f.Type},{f.Length})")));

        // Windows-1250 — Central European, pokriva Bosnian/Croatian/Serbian: š,č,ć,ž,đ
        var enc = Encoding.GetEncoding(1250);

        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        // Čitamo tekući broj zapisa i header size
        var hdr = new byte[32];
        fs.Read(hdr, 0, 32);
        uint recCount  = BitConverter.ToUInt32(hdr, 4);
        int  headerSz  = BitConverter.ToUInt16(hdr, 8);
        int  recSize   = BitConverter.ToUInt16(hdr, 10);

        // Pozicioniramo se na kraj zadnjeg zapisa (prepisujemo stari EOF marker)
        long writePos = headerSz + (long)recCount * recSize;
        fs.Seek(writePos, SeekOrigin.Begin);

        uint written = 0;
        foreach (var row in rows)
        {
            fs.WriteByte(0x20); // 0x20 = aktivan zapis (nije obrisan)
            foreach (var f in fields)
            {
                // Exact match first; fall back to prefix match for truncated names (dBase 10-char limit)
                if (!row.TryGetValue(f.Name, out var val))
                {
                    var key = row.Keys.FirstOrDefault(k =>
                        k.StartsWith(f.Name, StringComparison.OrdinalIgnoreCase));
                    val = key != null ? row[key] : null;
                }
                var bytes = DbfFieldBytes(f, val, enc);
                fs.Write(bytes, 0, bytes.Length);
            }
            written++;
            if (written % 50 == 0)
                LogService.Info("DBase", $"InsertRows '{tableName}': {written} rows written...");
        }

        // Pišemo EOF marker
        fs.WriteByte(0x1A);

        // Ažuriramo broj zapisa u headeru
        fs.Seek(4, SeekOrigin.Begin);
        fs.Write(BitConverter.GetBytes(recCount + written), 0, 4);

        LogService.Info("DBase", $"InsertRows '{tableName}': done ({written} rows total).");
    }

    private string DbfPath(string tableName)
        => Path.Combine(_folderPath, tableName + ".dbf");

    private string SidecarPath(string tableName)
        => Path.Combine(_folderPath, tableName + ".bp_names");

    // ── .dbf binary helpers ───────────────────────────────────────────────────

    private sealed record DbfFieldDef(string Name, char Type, int Length, int DecCount);

    /// <summary>Čita definicije polja iz .dbf headera.</summary>
    private static List<DbfFieldDef> DbfReadFields(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var hdr = new byte[32];
        fs.Read(hdr, 0, 32);
        int headerSize = BitConverter.ToUInt16(hdr, 8);

        var fields = new List<DbfFieldDef>();
        while (fs.Position < headerSize - 1)
        {
            var fd = new byte[32];
            int n = fs.Read(fd, 0, 32);
            if (n < 1 || fd[0] == 0x0D) break; // 0x0D = terminator
            if (n < 32) break;

            // Ime polja: prvih 11 bajtova, null-terminated
            var name = Encoding.ASCII.GetString(fd, 0, 11)
                                     .TrimEnd('\0')
                                     .Trim();
            char type   = (char)fd[11];
            int  length = fd[16];
            int  decCnt = fd[17];
            fields.Add(new DbfFieldDef(name, type, length, decCnt));
        }
        return fields;
    }

    /// <summary>
    /// Formatira vrijednost u bajte za jedno .dbf polje.
    /// CHARACTER: lijevo poravnanje, space-fill.
    /// NUMERIC/FLOAT: desno poravnanje, space-fill.
    /// LOGICAL: 'T' / 'F'.
    /// DATE: YYYYMMDD.
    /// </summary>
    private static byte[] DbfFieldBytes(DbfFieldDef field, object? val, Encoding enc)
    {
        var buf = new byte[field.Length];
        Array.Fill(buf, (byte)' '); // space-fill

        switch (field.Type)
        {
            case 'C': // Character — string, lijevo poravnan, space-padded
            {
                var s = val switch
                {
                    null or DBNull => "",
                    bool b         => b ? "T" : "F",
                    _              => Convert.ToString(val) ?? ""
                };
                var bytes = enc.GetBytes(s);
                int copy = Math.Min(bytes.Length, field.Length);
                Array.Copy(bytes, 0, buf, 0, copy);
                break;
            }
            case 'N': // Numeric — desno poravnan, decimal separator '.'
            case 'F': // Float
            {
                if (val is null or DBNull) break;
                string numStr;
                try
                {
                    numStr = field.DecCount > 0
                        ? Convert.ToDouble(val).ToString("F" + field.DecCount,
                              System.Globalization.CultureInfo.InvariantCulture)
                        : Convert.ToInt64(val).ToString();
                }
                catch { numStr = "0"; }
                // Desno poravnanje u polju dužine field.Length
                var trimmed = numStr.Length > field.Length
                    ? numStr[..field.Length]
                    : numStr.PadLeft(field.Length);
                var bytes = Encoding.ASCII.GetBytes(trimmed);
                Array.Copy(bytes, 0, buf, 0, Math.Min(bytes.Length, field.Length));
                break;
            }
            case 'L': // Logical — 'T' ili 'F'
            {
                buf[0] = val switch
                {
                    bool b   => b ? (byte)'T' : (byte)'F',
                    int  i   => i != 0 ? (byte)'T' : (byte)'F',
                    long l   => l != 0 ? (byte)'T' : (byte)'F',
                    string s => (s == "T" || string.Equals(s, "true",
                                    StringComparison.OrdinalIgnoreCase)) ? (byte)'T' : (byte)'F',
                    _        => (byte)'F'
                };
                break;
            }
            case 'D': // Date — YYYYMMDD
            {
                string dateStr = val switch
                {
                    DateTime dt => dt.ToString("yyyyMMdd"),
                    DateOnly d  => d.ToString("yyyyMMdd"),
                    string s    => s.Length >= 8 ? s[..8] : s.PadRight(8),
                    _           => "        "
                };
                var bytes = Encoding.ASCII.GetBytes(dateStr.PadRight(8)[..8]);
                Array.Copy(bytes, 0, buf, 0, 8);
                break;
            }
            // Ostale tipove (MEMO 'M', GENERAL 'G', BLOB 'B') ostavljamo space-filled
        }
        return buf;
    }

    // ── DDL (ACE OleDb) ───────────────────────────────────────────────────────

    public void CreateTable(string tableName, IReadOnlyList<ColumnSchema> columns)
    {
        var dbfPath = DbfPath(tableName);
        if (File.Exists(dbfPath)) return;

        var colDefs = columns.Select(c =>
        {
            var ddlType = TypeMappings.ResolveToDdl(BackendType.DBase, c.SqlType, c.MaxLength);
            if (ddlType == "CHARACTER")
            {
                var len = c.MaxLength > 0 && c.MaxLength <= 254 ? c.MaxLength : 254;
                ddlType = $"CHARACTER({len})";
            }
            return $"[{c.Name}] {ddlType}";
        }).ToList();
        using var cmd = new OleDbCommand(
            $"CREATE TABLE [{tableName}] ({string.Join(", ", colDefs)})", _conn, _tx);
        cmd.ExecuteNonQuery();

        // Write sidecar with full column names (dBase truncates field names to 10 chars in .dbf)
        var fullNames = columns.Select(c => c.Name).ToList();
        if (fullNames.Any(n => n.Length > 10))
            File.WriteAllLines(SidecarPath(tableName), fullNames);
    }

    public void AddColumn(string tableName, ColumnSchema column)
    {
        var existing = GetColumnNames(tableName);
        if (existing.Any(c => c.Equals(column.Name, StringComparison.OrdinalIgnoreCase)))
            return;

        var ddlType = TypeMappings.ResolveToDdl(BackendType.DBase, column.SqlType, column.MaxLength);
        if (ddlType == "CHARACTER")
        {
            var len = column.MaxLength > 0 && column.MaxLength <= 254 ? column.MaxLength : 254;
            ddlType = $"CHARACTER({len})";
        }
        try
        {
            using var cmd = new OleDbCommand(
                $"ALTER TABLE [{tableName}] ADD [{column.Name}] {ddlType}", _conn, _tx);
            cmd.ExecuteNonQuery();
        }
        catch (OleDbException ex) when (ex.Message.Contains("already exists",
                                            StringComparison.OrdinalIgnoreCase)) { }
        catch (OleDbException ex) when (ex.Message.Contains("not supported on a table that contains data",
                                            StringComparison.OrdinalIgnoreCase))
        {
            LogService.Warning("DBase",
                $"Cannot add column '{column.Name}' to '{tableName}': table has data.");
        }
    }

    public void DropTable(string tableName)
    {
        var dbfPath = DbfPath(tableName);
        if (File.Exists(dbfPath)) File.Delete(dbfPath);
        var sc = SidecarPath(tableName);
        if (File.Exists(sc)) File.Delete(sc);
    }

    public void BeginTransaction() { }
    public void Commit()   { }
    public void Rollback() { }
    public void Dispose()  { _tx?.Dispose(); _conn.Dispose(); }
}
