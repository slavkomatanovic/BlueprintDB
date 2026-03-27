using Blueprint.App.Backend;
using Blueprint.App.Models;

namespace Blueprint.App;

/// <summary>
/// Application-level state shared across all windows.
/// SelectedProgramId is persisted to the parametri table so it survives restarts.
/// </summary>
public static class AppState
{
    private const int    AppChapter       = 0;
    private const string LastProgramParam = "LastProgramId";

    /// <summary>Primary key of the currently selected program from the programi table.</summary>
    public static int SelectedProgramId { get; set; } = 0;

    /// <summary>Display name of the currently selected program.</summary>
    public static string SelectedProgramName { get; set; } = "";

    /// <summary>
    /// File path of the backend database being managed by Blueprint
    /// (target Access / MySQL / SQLite database, NOT the Blueprint metadata DB).
    /// </summary>
    public static string BackendDatabasePath { get; set; } = "";

    /// <summary>Backend type matching BackendDatabasePath (file path or connection string).</summary>
    public static BackendType BackendType { get; set; } = BackendType.SQLite;

    // ── Options (set by KonfiguracijaWindow) ─────────────────────────────────
    public static bool BrisiNepotrebno { get; set; } = false;

    // ── Last selected program persistence ────────────────────────────────────

    /// <summary>
    /// Call once on startup (after DbSeeder) to restore the last selected program.
    /// </summary>
    public static void LoadSelectedProgram()
    {
        try
        {
            using var db = new BlueprintDbContext();
            var param = db.Parametris.FirstOrDefault(p =>
                p.Idpoglavlja == AppChapter && p.Nazivparametra == LastProgramParam);
            if (param?.Ocitano != null && int.TryParse(param.Ocitano, out var id) && id > 0)
                SelectedProgramId = id;
        }
        catch (Exception ex)
        {
            LogService.Error("AppState", "Failed to load last selected program", ex);
        }
    }

    /// <summary>
    /// Saves the currently selected program ID to the parametri table.
    /// Call from any window's cbProgrami_SelectionChanged.
    /// </summary>
    public static void SaveSelectedProgram(int programId)
    {
        SelectedProgramId = programId;
        try
        {
            using var db = new BlueprintDbContext();
            var param = db.Parametris.FirstOrDefault(p =>
                p.Idpoglavlja == AppChapter && p.Nazivparametra == LastProgramParam);
            if (param == null)
                db.Parametris.Add(new Parametri
                {
                    Idpoglavlja    = AppChapter,
                    Nazivparametra = LastProgramParam,
                    Ocitano        = programId.ToString()
                });
            else
                param.Ocitano = programId.ToString();
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            LogService.Error("AppState", "Failed to save last selected program", ex);
        }
    }
}
