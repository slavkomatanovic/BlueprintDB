using Blueprint.App.Models;

namespace Blueprint.App;

/// <summary>
/// Seeds the SQLite database with initial data (English language + translations).
/// SyncTranslations runs on every startup to add any keys that are new since last run.
/// </summary>
public static class DbSeeder
{
    public static void Seed()
    {
        using var db = new BlueprintDbContext();
        db.Database.EnsureCreated();

        // First-time seed: insert the default language
        if (!db.Jeziks.Any())
        {
            db.Jeziks.Add(new Jezik
            {
                Idjezik         = 1,
                Nazivjezika     = "English",
                Podrazumijevani = true,
                Skriven         = false,
                Korisnik        = "system",
                Datumupisa      = DateTime.Now,
                Vremenskipecat  = 0
            });
            db.SaveChanges();
        }

        // Always sync translations — adds any missing keys without touching existing ones
        SyncTranslations(db, languageId: 1);
    }

    // ── Translation master list ─────────────────────────────────────────────
    // Add new keys here; SyncTranslations will insert them on next startup.

    private static readonly (string Key, string Value)[] _entries =
    [
        // ── Window titles ───────────────────────────────────────────────────
        ("FORM_MAIN",              "Blueprint"),
        ("FORM_KONFIGURACIJA",     "Configuration"),
        ("FORM_PROGRAMI",          "Programs"),
        ("FORM_TABELE",            "Tables"),
        ("FORM_KOLONE",            "Columns"),

        // ── Main menu ────────────────────────────────────────────────────────
        ("MENU_METADATA",          "Metadata"),
        ("MENU_TOOLS",             "Tools"),
        ("MENU_PROGRAMI",          "Programs"),
        ("MENU_TABELE",            "Tables"),
        ("MENU_SURPLUS",           "Surplus"),
        ("MENU_RELACIJE",          "Relations"),
        ("MENU_PARAMETRI",         "INI Parameters"),
        ("MENU_PROMJENA_NAZIVA",   "Rename tables"),
        ("MENU_TRANSFER",          "Data Migration"),
        ("MENU_PUTANJA",           "Path / Start"),
        ("MENU_KONFIGURACIJA",     "Configuration"),
        ("MENU_KRAJ",              "Exit"),

        // ── Toolbar buttons (MainWindow) ─────────────────────────────────────
        ("BTN_PROGRAMI",           "Programs"),
        ("BTN_TABELE",             "Tables"),
        ("BTN_SURPLUS",            "Surplus"),
        ("BTN_RELACIJE",           "Relations"),
        ("BTN_PARAMETRI",          "INI Params"),
        ("BTN_KRAJ",               "Exit"),

        // ── Shared action buttons ────────────────────────────────────────────
        ("CMD_NOVI",               "New"),
        ("CMD_SACUVAJ",            "Save"),
        ("CMD_OBRISI",             "Delete"),
        ("CMD_ZATVORI",            "Close"),
        ("CMD_TABELE_NAV",         "Tables \u2192"),
        ("CMD_KOLONE_NAV",         "Columns \u2192"),
        ("CMD_IMPORT",             "Import"),
        ("CMD_RESET",              "Reset"),
        ("TIP_CMD_IMPORT",         "Import tables into the selected program from the backend on demand."),

        // ── KonfiguracijaWindow ───────────────────────────────────────────────
        ("GRP_PROGRAM_IZBOR",      "Program selection"),
        ("LBL_ODABRANI_PROGRAM",   "Program:"),
        ("GRP_BACKEND",            "Backend database"),
        ("LBL_BACKEND_BAZA",       "Path:"),
        ("CMD_PREGLEDAJ",          "Browse..."),
        ("CMD_OK",                 "OK"),
        ("CMD_OTKAZI",             "Cancel"),
        ("GRP_OPCIJE",             "Options"),
        ("CHK_BRISI_NEPOTREBNO",   "Auto-delete redundant tables and columns"),
        ("CHK_AUTO_REPAIR",        "Auto-process all selected databases"),
        ("CHK_IGNORE_VERSION",     "Ignore version"),

        // ── GroupBox headers ─────────────────────────────────────────────────
        ("GRP_UNOS_IZMJENA",       "Add / Edit"),

        // ── Labels ───────────────────────────────────────────────────────────
        ("LBL_NAZIV_PROGRAMA",     "Program name:"),
        ("LBL_VERZIJA",            "Version:"),
        ("LBL_PROGRAM",            "Program:"),
        ("LBL_NAZIV_TABELE",       "Table name:"),
        ("LBL_SID",                "SID:"),
        ("LBL_TABELA",             "Table:"),
        ("LBL_NAZIV_KOLONE",       "Column name:"),
        ("LBL_TIP_PODATKA",        "Data type:"),
        ("LBL_DEFAULT",            "Default:"),
        ("LBL_VELICINA",            "Size:"),
        ("LBL_NULL",               "Null:"),
        ("LBL_KEY",                "Key:"),

        // ── DataGrid column headers ──────────────────────────────────────────
        ("HDR_ID",                 "ID"),
        ("HDR_NAZIV_PROGRAMA",     "Program name"),
        ("HDR_VERZIJA",            "Version"),
        ("HDR_NAZIV_TABELE",       "Table name"),
        ("HDR_SID",                "SID"),
        ("HDR_NAZIV_KOLONE",       "Column name"),
        ("HDR_TIP_PODATKA",        "Data type"),
        ("HDR_DEFAULT",            "Default"),
        ("HDR_VELICINA",           "Size"),
        ("HDR_NULL",               "Null"),
        ("HDR_INDEXED",            "Indexed"),
        ("HDR_KEY",                "Key"),

        // ── Validation messages ──────────────────────────────────────────────
        ("MSG_NAZIV_PROGRAMA_PRAZAN",     "Program name cannot be empty!"),
        ("MSG_ODABERI_PROGRAM_BRISANJE",  "Please select a program to delete."),
        ("MSG_ODABERI_PROGRAM_TABELE",    "Please select a program to view tables."),
        ("MSG_NAZIV_TABELE_PRAZAN",       "Table name cannot be empty!"),
        ("MSG_ODABERI_PROGRAM",           "Please select a program first!"),
        ("MSG_ODABERI_TABELU_BRISANJE",   "Please select a table to delete."),
        ("MSG_ODABERI_TABELU_KOLONE",     "Please select a table to view columns."),
        ("MSG_NAZIV_KOLONE_PRAZAN",       "Column name cannot be empty!"),
        ("MSG_ODABERI_KOLONU_BRISANJE",   "Please select a column to delete."),
        ("MSG_ODABERI_PROGRAM_KONFIG",    "Please select a program."),
        ("MSG_ODABERI_BAZU",              "Please select a database file."),

        // ── Delete confirmations ({0} = item name) ───────────────────────────
        ("MSG_BRISANJE_PROGRAMA",  "Delete program '{0}'?"),
        ("MSG_BRISANJE_TABELE",    "Delete table '{0}'?"),
        ("MSG_BRISANJE_KOLONE",    "Delete column '{0}'?"),
        ("MSG_POTVRDA_BRISANJA",   "Confirm deletion"),

        // ── Misc ─────────────────────────────────────────────────────────────
        ("MSG_NOT_IMPLEMENTED",    "Not yet implemented."),
        ("MSG_IMPORT_DONE",        "Import completed successfully."),
        ("MSG_RESET_CONFIRM",      "Are you sure you want to delete all records for this program? (Tables, Columns, Relations)"),
        ("MSG_ODABERI_BAZU_IMPORT","Please select a backend database in Setup first."),
        ("Blueprint",              "Blueprint"),

        // ── License ───────────────────────────────────────────────────────────
        ("MENU_LICENSE",           "License"),

        // ── Help ─────────────────────────────────────────────────────────────
        ("FORM_HELP",              "Help"),
        ("MENU_HELP",              "Help"),

        // ── Options menu ─────────────────────────────────────────────────────
        ("MENU_OPTIONS",           "Options"),
        ("MENU_LOG",               "Log"),

        // ── LogWindow ─────────────────────────────────────────────────────────
        ("FORM_LOG",               "Log"),
        ("LBL_LOG_NIVO",           "Level:"),
        ("LBL_LOG_KATEGORIJA",     "Category:"),
        ("LBL_LOG_PRETRAGA",       "Search:"),
        ("CMD_LOG_OSVJEZI",        "Refresh"),
        ("CMD_LOG_OCISTI",         "Clear log"),
        ("HDR_LOG_DATUM",          "Date/Time"),
        ("HDR_LOG_NIVO",           "Level"),
        ("HDR_LOG_KATEGORIJA",     "Category"),
        ("HDR_LOG_PORUKA",         "Message"),
        ("HDR_LOG_BACKEND",        "Backend"),
        ("HDR_LOG_PROGRAM",        "Program"),
        ("HDR_LOG_KORISNIK",       "User"),
        ("LBL_LOG_DETALJI",        "Details / Stack trace"),
        ("LBL_LOG_SQLKOD",         "SQL code"),
        ("ALL",                    "All"),
        ("LOG_ENTRIES",            "entries"),
        ("LOG_TOTAL",              "total"),
        ("LOG_LAST_ENTRY",         "Last entry:"),
        ("LOG_NO_ENTRIES",         "No entries."),
        ("MSG_LOG_OCISTI_POTVRDA", "Delete all log entries?"),

        // ── WizardWindow ──────────────────────────────────────────────────────
        ("FORM_WIZARD",            "Schema Import Wizard"),
        ("MENU_WIZARD",            "Schema Import Wizard\u2026"),
        ("BTN_WIZARD",             "Import Wizard"),

        // ── TransferWizardWindow ──────────────────────────────────────────────
        ("FORM_TRANSFER_WIZARD",   "Transfer Data Wizard"),
        ("MENU_TRANSFER_WIZARD",   "Transfer Data Wizard\u2026"),
        ("BTN_TRANSFER_WIZARD",    "Transfer Wizard"),

        // ── SchemaSyncWizardWindow ────────────────────────────────────────────
        ("FORM_SCHEMA_SYNC",       "Schema Sync Wizard"),
        ("MENU_SCHEMA_SYNC",       "Schema Sync Wizard\u2026"),
        ("BTN_SCHEMA_SYNC",        "Schema Sync"),

        // ── Tables submenu ────────────────────────────────────────────────────
        ("MENU_TABELE_KOLONE",     "Tables / Columns"),

        // ── Wizards submenu & Language ────────────────────────────────────────
        ("MENU_WIZARDS",           "Wizards"),
        ("MENU_LANGUAGE",          "Language"),

        // ── TransferWindow ────────────────────────────────────────────────────
        ("FORM_TRANSFER",          "Transfer Database"),
        ("GRP_IZVOR",              "Source"),
        ("GRP_CILJ",               "Target"),
        ("LBL_TIP",                "Type:"),
        ("LBL_PUTANJA",            "Path:"),
        ("CMD_TRANSFER",           "Transfer"),
        ("MSG_TRANSFER_SRC_EMPTY", "Please enter a source database path or connection string."),
        ("MSG_TRANSFER_TGT_EMPTY", "Please enter a target database path or connection string."),
        ("MSG_NEMA_TABELA_ZA_TRANSFER", "No tables found for the selected program. Run Import first."),
        ("MSG_TRANSFER_RUNNING",   "Transfer in progress\u2026"),
        ("MSG_TRANSFER_DONE",      "Transfer completed successfully."),
        ("MSG_TRANSFER_ERRORS",    "Transfer completed with errors:"),
    ];

    // ── Keys that are always updated (deliberate renames) ───────────────────
    // Add an entry here when you intentionally change a label across versions.
    // Unlike _entries, these overwrite whatever is currently in the DB.

    private static readonly (string Key, string Value)[] _updates =
    [
        ("MENU_KONFIGURACIJA",   "Configuration"),
        ("FORM_KONFIGURACIJA",   "Configuration"),
        ("MENU_TRANSFER",        "Data Migration"),
        ("BTN_WIZARD",           "Schema Import"),
        ("CHK_BRISI_NEPOTREBNO", "Auto-delete redundant tables and columns"),
        ("CHK_AUTO_REPAIR",      "Auto-process all selected databases"),
        ("CHK_IGNORE_VERSION",   "Ignore version"),
        ("GRP_OPCIJE",           "Options"),
        ("TIP_CMD_IMPORT",       "Import tables into the selected program from the backend on demand."),
    ];

    // ── Private sync helper ─────────────────────────────────────────────────

    private static void SyncTranslations(BlueprintDbContext db, int languageId)
    {
        var existing = db.Rjecniks
            .Where(r => r.Idjezik == languageId)
            .Select(r => r.Original!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int nextId = db.Rjecniks.Any()
            ? db.Rjecniks.Max(r => r.Idrjecnik) + 1
            : 1;

        bool any = false;

        // Insert missing keys
        foreach (var (key, value) in _entries)
        {
            if (existing.Contains(key)) continue;

            db.Rjecniks.Add(new Rjecnik
            {
                Idrjecnik      = nextId++,
                Idjezik        = languageId,
                Original       = key,
                Prijevod       = value,
                Korisnik       = "system",
                Datumupisa     = DateTime.Now,
                Skriven        = false,
                Vremenskipecat = 0
            });
            any = true;
        }

        // Force-update deliberately renamed keys
        foreach (var (key, value) in _updates)
        {
            var row = db.Rjecniks.FirstOrDefault(r =>
                r.Idjezik == languageId &&
                r.Original == key);
            if (row != null && row.Prijevod != value)
            {
                row.Prijevod = value;
                any = true;
            }
        }

        if (any) db.SaveChanges();
    }
}
