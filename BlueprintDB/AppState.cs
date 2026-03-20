namespace Blueprint.App;

/// <summary>
/// Application-level state shared across all windows.
/// Set by KonfiguracijaWindow on startup.
/// </summary>
public static class AppState
{
    /// <summary>Primary key of the currently selected program from the programi table.</summary>
    public static int SelectedProgramId { get; set; } = 0;

    /// <summary>Display name of the currently selected program.</summary>
    public static string SelectedProgramName { get; set; } = "";

    /// <summary>
    /// File path of the backend database being managed by Blueprint
    /// (target Access / MySQL / SQLite database, NOT the Blueprint metadata DB).
    /// </summary>
    public static string BackendDatabasePath { get; set; } = "";

    // ── Options (set by KonfiguracijaWindow) ────────────────────────────────
    public static bool BrisiNepotrebno { get; set; } = false;
}
