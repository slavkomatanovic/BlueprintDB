using Blueprint.App.Models;

namespace Blueprint.App;

/// <summary>
/// Centralni servis za logiranje u Blueprint log tabelu.
/// Sve metode su fire-and-forget — nikad ne bacaju iznimku prema pozivaocu.
/// </summary>
public static class LogService
{
    public static void Error(string kategorija, string poruka, Exception? ex = null,
        string? sql = null, string? backend = null)
    {
        var detalji = ex == null ? null
            : $"{ex.GetType().Name}: {ex.Message}" +
              (ex.InnerException != null ? $"\nInner: {ex.InnerException.Message}" : "") +
              $"\n{ex.StackTrace}";
        Write("ERROR", kategorija, poruka, detalji, sql, backend);
    }

    public static void Warning(string kategorija, string poruka, string? detalji = null,
        string? backend = null)
        => Write("WARNING", kategorija, poruka, detalji, null, backend);

    public static void Info(string kategorija, string poruka)
        => Write("INFO", kategorija, poruka, null, null, null);

    public static void Sql(string sql, string backend, string kategorija = "Backend")
        => Write("SQL", kategorija, $"Executed on {backend}", null, sql, backend);

    private static void Write(string nivo, string kategorija, string poruka,
        string? detalji, string? sqlkod, string? backend)
    {
        try
        {
            using var db = new BlueprintDbContext();
            db.Logs.Add(new Log
            {
                Datumvrijeme = DateTime.Now,
                Nivo         = nivo,
                Kategorija   = kategorija,
                Poruka       = Truncate(poruka, 255),
                Detalji      = Truncate(detalji, 500),
                Sqlkod       = Truncate(sqlkod, 500),
                Backend      = Truncate(backend, 255),
                Idprogram    = AppState.SelectedProgramId > 0 ? AppState.SelectedProgramId : null,
                Korisnik     = Truncate(Environment.UserName, 50),
                Masina       = Truncate(Environment.MachineName, 255),
            });
            db.SaveChanges();
        }
        catch
        {
            // Logiranje ne smije rušiti aplikaciju — tiho ignoriramo greške u logu
        }
    }

    private static string Truncate(string? s, int max)
        => s == null ? null! : s.Length <= max ? s : s[..max];
}
