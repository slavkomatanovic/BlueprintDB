using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Blueprint.App;

public partial class HelpWindow : Window
{
    // ── Topic / section data model ───────────────────────────────────────────

    private record HelpSection(string Heading, string Body);

    private record HelpTopic(string Title, string Icon, HelpSection[] Sections);

    // ── Topic content ────────────────────────────────────────────────────────

    private static readonly HelpTopic[] Topics =
    [
        new HelpTopic("Overview", "\uE897",
        [
            new HelpSection("What is Blueprint?",
                "Blueprint is a database metadata management tool. It stores and manages " +
                "structural definitions — Programs, Tables, Columns, and Relations — for " +
                "target databases that your applications use.\n\n" +
                "Think of Blueprint as a schema registry: it keeps track of what tables and " +
                "columns should exist in each of your application databases, and provides " +
                "tools to compare, synchronise, and migrate that schema to live backends."),

            new HelpSection("Key concepts",
                "Program — the top-level unit. Each program represents one application " +
                "whose database schema Blueprint manages.\n\n" +
                "Table — a database table belonging to a program. Blueprint stores its name, " +
                "SID (legacy identifier), and which program it belongs to.\n\n" +
                "Column — a column belonging to a table. Blueprint records name, data type, " +
                "size, default value, nullability, key flag, and indexed flag.\n\n" +
                "Relation — a foreign-key relationship between two tables in the same program. " +
                "Used by the Schema Sync Wizard to create FK constraints on the live backend.\n\n" +
                "Backend — the live database that Blueprint connects to for import or sync " +
                "operations. Supported engines: SQLite, MySQL, SQL Server, PostgreSQL, " +
                "MS Access, Firebird, Oracle, DB2, dBASE."),

            new HelpSection("Typical workflow",
                "1. Add a Program in the Programs window.\n" +
                "2. Run the Schema Import Wizard to pull the current table/column definitions " +
                "   straight from a live backend database.\n" +
                "3. Review and adjust the imported metadata in the Tables and Columns windows.\n" +
                "4. Add Relations between tables in the Relations window.\n" +
                "5. When the schema evolves, run Schema Sync Wizard to detect differences " +
                "   between Blueprint's records and the live backend, then apply changes.\n" +
                "6. Use the Transfer Data Wizard to copy data from one backend to another."),
        ]),

        new HelpTopic("Programs", "\uE716",
        [
            new HelpSection("Opening the Programs window",
                "Open the Programs window from:\n" +
                "  • Home screen — click the Programs tile\n" +
                "  • Menu: Metadata → Programs\n\n" +
                "The window shows all registered programs in a list on the left and an " +
                "edit form on the right."),

            new HelpSection("Adding a program",
                "1. Click New (or press Alt+N).\n" +
                "2. Enter the Program name and Version.\n" +
                "3. Click Save.\n\n" +
                "Program name must not be empty. Version is optional but recommended " +
                "so Blueprint can track schema changes across releases."),

            new HelpSection("Editing a program",
                "Select a program in the list. The form fields populate automatically. " +
                "Change the name or version, then click Save."),

            new HelpSection("Deleting a program",
                "Select a program, then click Delete. Blueprint will ask for confirmation " +
                "before removing the program and all its associated tables, columns, and " +
                "relations."),

            new HelpSection("Navigating to Tables",
                "Click the Tables → button to jump directly to the Tables window " +
                "pre-filtered to the selected program."),
        ]),

        new HelpTopic("Tables", "\uE9D2",
        [
            new HelpSection("Opening the Tables window",
                "Open the Tables window from:\n" +
                "  • Home screen — click the Tables tile\n" +
                "  • Menu: Metadata → Tables / Columns\n" +
                "  • Programs window — click the Tables → button\n\n" +
                "The list shows all tables for the currently selected program."),

            new HelpSection("Selecting a program",
                "Use the Program drop-down at the top of the window to switch between " +
                "programs. The table list updates automatically."),

            new HelpSection("Adding a table",
                "1. Select the target program.\n" +
                "2. Click New.\n" +
                "3. Enter the Table name and optionally the SID.\n" +
                "4. Click Save.\n\n" +
                "SID is a legacy numeric identifier carried over from the original " +
                "VBA/Access implementation. It is optional but preserved for compatibility."),

            new HelpSection("Deleting a table",
                "Select a table, then click Delete. All columns belonging to that table " +
                "are also deleted."),

            new HelpSection("Navigating to Columns",
                "Click the Columns → button to jump directly to the Columns window " +
                "pre-filtered to the selected table."),
        ]),

        new HelpTopic("Columns", "\uE9D9",
        [
            new HelpSection("Opening the Columns window",
                "Open the Columns window from:\n" +
                "  • Menu: Metadata → Tables / Columns (then navigate)\n" +
                "  • Tables window — click the Columns → button"),

            new HelpSection("Column fields",
                "Name — column name as it appears in the database.\n" +
                "Data type — the SQL data type (e.g. VARCHAR, INT, DATE).\n" +
                "Size — maximum length or precision, where applicable.\n" +
                "Default — default value expression stored in the schema.\n" +
                "Null — whether NULL values are allowed (Yes/No).\n" +
                "Key — marks the column as a primary key.\n" +
                "Indexed — marks the column as indexed."),

            new HelpSection("Adding a column",
                "1. Select the Program and Table at the top of the window.\n" +
                "2. Click New.\n" +
                "3. Fill in the column fields.\n" +
                "4. Click Save."),

            new HelpSection("Editing and deleting",
                "Select a column in the list, change the fields in the form, then Save. " +
                "To remove a column, select it and click Delete."),
        ]),

        new HelpTopic("Relations", "\uE71B",
        [
            new HelpSection("What are relations?",
                "A relation describes a foreign-key link between two tables within the " +
                "same program. Blueprint records:\n\n" +
                "  • Parent table (Tabelal) — the table that owns the primary key\n" +
                "  • Child table (Tabelad) — the table that holds the foreign key\n" +
                "  • Field (Polje) — the column name used in both tables\n" +
                "  • Constraint name — optional; generated automatically if blank\n" +
                "  • Cascade — whether DELETE / UPDATE should cascade\n\n" +
                "Relations are used by the Schema Sync Wizard to create FK constraints " +
                "on live backend databases that support them."),

            new HelpSection("Adding a relation",
                "1. Select the Program.\n" +
                "2. Click New.\n" +
                "3. Choose the parent table, child table, and shared field.\n" +
                "4. Optionally set a constraint name and cascade flag.\n" +
                "5. Click Save."),

            new HelpSection("Backend support",
                "The following backends support FK constraints:\n" +
                "MySQL, SQL Server, PostgreSQL, MS Access, Firebird, Oracle.\n\n" +
                "SQLite, DB2, and dBASE do not have FK constraint support in Blueprint " +
                "(SQLite supports them in principle but Blueprint does not apply them)."),
        ]),

        new HelpTopic("Schema Import Wizard", "\uE82D",
        [
            new HelpSection("Purpose",
                "The Schema Import Wizard reads the table and column definitions from a " +
                "live backend database and stores them in Blueprint. Use this to bootstrap " +
                "Blueprint's metadata from an existing database rather than entering " +
                "everything by hand."),

            new HelpSection("Before you start",
                "1. Make sure a Program exists in Blueprint (create one if needed).\n" +
                "2. Open Configuration (Tools → Configuration) and set:\n" +
                "   • The target Program\n" +
                "   • The backend database path / connection string\n" +
                "3. Return to the Home screen and click Schema Import."),

            new HelpSection("Running the wizard",
                "Step 1 — Select Program and Backend type.\n" +
                "Step 2 — Enter the database path or connection string.\n" +
                "Step 3 — Click Import. Blueprint reads the schema and inserts or " +
                "updates all tables and columns for the selected program.\n\n" +
                "If 'Auto-delete redundant tables and columns' is enabled in " +
                "Configuration, any Blueprint entries that are no longer present in " +
                "the backend will be removed."),

            new HelpSection("After import",
                "Review the imported metadata in the Tables and Columns windows. " +
                "Add Relations manually if needed, since foreign-key metadata varies " +
                "between backends."),
        ]),

        new HelpTopic("Schema Sync Wizard", "\uE895",
        [
            new HelpSection("Purpose",
                "The Schema Sync Wizard compares Blueprint's stored schema against a " +
                "live backend database and lists all differences. You can then choose " +
                "which changes to apply to the backend.\n\n" +
                "Supported operations:\n" +
                "  • Create missing tables\n" +
                "  • Add missing columns to existing tables\n" +
                "  • Add missing FK constraints (on supported backends)"),

            new HelpSection("Running the wizard",
                "1. Open the Schema Sync Wizard (Tools → Schema Sync Wizard…).\n" +
                "2. Select the Program and Backend type.\n" +
                "3. Enter the database path or connection string.\n" +
                "4. Click Analyse. Blueprint connects to the backend, fetches the live " +
                "   schema, and displays:\n" +
                "   • Tables to create (in Blueprint but not in backend)\n" +
                "   • Columns to add (in Blueprint but not in backend table)\n" +
                "   • FK constraints to add (Relations defined in Blueprint but not " +
                "     present in backend)\n" +
                "5. Review the plan, then click Apply to execute the DDL statements."),

            new HelpSection("SQL Preview",
                "Before clicking Apply, you can preview the exact DDL that will be " +
                "executed by clicking Show SQL. This lets you review and optionally " +
                "copy the script before committing changes."),

            new HelpSection("Notes",
                "  • The wizard only adds, never drops. Columns or tables that exist " +
                "    in the backend but not in Blueprint are left untouched.\n" +
                "  • If the backend does not support FK constraints, the FK section " +
                "    is skipped silently.\n" +
                "  • Always back up the backend database before applying sync changes."),
        ]),

        new HelpTopic("Transfer Data Wizard", "\uE8AB",
        [
            new HelpSection("Purpose",
                "The Transfer Data Wizard copies data from a source database to a " +
                "target database for all tables belonging to the selected program. " +
                "Use this to migrate data between environments (e.g. development → " +
                "production) or between backend types (e.g. Access → MySQL)."),

            new HelpSection("Running the wizard",
                "1. Open the Transfer Data Wizard (Tools → Transfer Data Wizard…).\n" +
                "2. Select the Program.\n" +
                "3. Choose the Source backend type and enter its path / connection string.\n" +
                "4. Choose the Target backend type and enter its path / connection string.\n" +
                "5. Click Transfer.\n\n" +
                "The wizard iterates over all tables for the program, reads rows from " +
                "the source, and inserts them into the target. Progress and any errors " +
                "are shown in the log panel."),

            new HelpSection("Before transferring",
                "The target database must already have the correct schema (tables and " +
                "columns must exist). Run the Schema Sync Wizard against the target first " +
                "if needed.\n\n" +
                "Import the program's schema into Blueprint first if you haven't already " +
                "(run Schema Import Wizard)."),

            new HelpSection("Notes",
                "  • Existing data in the target is not cleared before transfer — " +
                "    duplicate-key errors may occur if rows already exist.\n" +
                "  • Large tables may take time; do not close the window during transfer.\n" +
                "  • Check the Log window (Options → Log) for detailed error information."),
        ]),

        new HelpTopic("Data Migration", "\uECC4",
        [
            new HelpSection("Purpose",
                "Data Migration (Tools → Data Migration) is a direct database-to-database " +
                "transfer tool for a single selected backend path. Unlike the Transfer Data " +
                "Wizard it operates on arbitrary SQL without the program/table metadata layer."),

            new HelpSection("Usage",
                "1. Select Source type and enter the path or connection string.\n" +
                "2. Select Target type and enter the path or connection string.\n" +
                "3. Click Transfer.\n\n" +
                "This tool is intended for experienced users who need low-level control " +
                "over migration tasks not covered by the wizard."),
        ]),

        new HelpTopic("Configuration", "\uE8B8",
        [
            new HelpSection("Opening Configuration",
                "Open Configuration from:\n" +
                "  • Home screen — click the Setup tile\n" +
                "  • Menu: Tools → Configuration"),

            new HelpSection("Settings",
                "Program — select the active program. This sets the program used by the " +
                "import and sync wizards as the default.\n\n" +
                "Backend database path — path to the SQLite / Access file or connection " +
                "string for the target backend.\n\n" +
                "Auto-delete redundant tables and columns — when enabled, the Schema " +
                "Import Wizard removes Blueprint entries that no longer exist in the " +
                "backend.\n\n" +
                "Auto-process all selected databases — when enabled, the import wizard " +
                "processes all databases for the program without prompting.\n\n" +
                "Ignore version — skips version comparison during import."),
        ]),

        new HelpTopic("Log", "\uE9D9",
        [
            new HelpSection("Opening the Log window",
                "Open the Log from Options → Log in the main menu."),

            new HelpSection("Filtering",
                "Level — filter by severity: All, ERROR, WARNING, INFO, SQL.\n" +
                "Category — filter by operation category.\n" +
                "Search — full-text search across the message column.\n\n" +
                "Click Refresh to reload entries after applying filters."),

            new HelpSection("Entry details",
                "Select any log row to see the full details and stack trace (for errors) " +
                "or the SQL statement (for SQL entries) in the panel below the grid."),

            new HelpSection("Clearing the log",
                "Click Clear log to delete all log entries. You will be asked to confirm " +
                "before the entries are permanently removed."),
        ]),

        new HelpTopic("Language", "\uE775",
        [
            new HelpSection("Switching language",
                "Open Options → Language from the main menu. A submenu lists all available " +
                "languages. Click a language to switch immediately.\n\n" +
                "The selection is saved and restored the next time Blueprint starts."),

            new HelpSection("Adding a new language",
                "Languages and their translations are stored in Blueprint's SQLite metadata " +
                "database. To add a new language:\n\n" +
                "1. Insert a row into the Jezik table (name, Podrazumijevani=false, Skriven=false).\n" +
                "2. For each UI key in the Rjecnik table, add a corresponding row for the " +
                "   new language ID with the translated text.\n\n" +
                "The UI keys are all uppercase identifiers such as FORM_MAIN, BTN_PROGRAMI, " +
                "MSG_NAZIV_PROGRAMA_PRAZAN, etc. Refer to the existing English rows " +
                "(Idjezik=1) for the full list."),
        ]),

        new HelpTopic("Keyboard shortcuts", "\uE92E",
        [
            new HelpSection("CRUD windows (Programs, Tables, Columns, Relations)",
                "Alt+N — New\n" +
                "Alt+S — Save\n" +
                "Alt+D — Delete\n" +
                "Alt+C or Esc — Close window"),

            new HelpSection("General",
                "F1 — Open Help (from any window that has a Help button)\n" +
                "Esc — Close / Cancel the active dialog"),
        ]),

        new HelpTopic("Troubleshooting", "\uE9CE",
        [
            new HelpSection("Cannot connect to backend",
                "  • Verify the path / connection string in Configuration.\n" +
                "  • Ensure the backend server is running (MySQL, SQL Server, PostgreSQL).\n" +
                "  • For file-based backends (SQLite, Access, dBASE), check that the file " +
                "    exists and that Blueprint has read/write permissions.\n" +
                "  • Check the Log window for the exact error message."),

            new HelpSection("Schema Sync shows no differences",
                "If the wizard reports 'Up to date' but you expect changes, verify that:\n" +
                "  • The correct Program is selected.\n" +
                "  • The backend connection string points to the right database.\n" +
                "  • Tables in Blueprint use exactly the same names as in the backend " +
                "    (names are compared case-insensitively, but check for typos)."),

            new HelpSection("Import creates duplicate entries",
                "Each import run is designed to be idempotent — it inserts missing rows " +
                "and updates existing ones. If you see duplicates, it may be because " +
                "the same table name exists with different casing. Blueprint compares " +
                "names case-insensitively during import."),

            new HelpSection("Log window shows no entries",
                "Blueprint logs operations to the same SQLite metadata database. If the " +
                "log is empty, the operations simply succeeded without error. Try running " +
                "an import or sync and then refresh the log."),

            new HelpSection("Window position is wrong after moving to another monitor",
                "If Blueprint's windows appear off-screen, delete the saved positions " +
                "by removing the rows in the Parametri table where Idpoglavlja = 0, " +
                "or simply drag the window back onto a visible monitor — the new " +
                "position will be saved automatically on close."),
        ]),
    ];

    // ── Constructor ──────────────────────────────────────────────────────────

    public HelpWindow()
    {
        InitializeComponent();
        LanguageService.TranslateWindow(this);
        WindowSettings.Restore("HelpWindow", this);
        Closing += (_, _) => WindowSettings.Save("HelpWindow", this);

        // Populate the sidebar
        foreach (var topic in Topics)
            lstTopics.Items.Add(topic);

        // Render topic titles in the ListBox using a DataTemplate defined in code
        lstTopics.DisplayMemberPath = "Title";

        if (Topics.Length > 0)
            lstTopics.SelectedIndex = 0;
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void LstTopics_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lstTopics.SelectedItem is not HelpTopic topic) return;
        RenderTopic(topic);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // ── Content rendering ────────────────────────────────────────────────────

    private void RenderTopic(HelpTopic topic)
    {
        pnlContent.Children.Clear();

        // Topic title
        pnlContent.Children.Add(new TextBlock
        {
            Text       = topic.Title,
            FontSize   = 22,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            Margin     = new Thickness(0, 0, 0, 20)
        });

        foreach (var section in topic.Sections)
        {
            // Section heading
            pnlContent.Children.Add(new TextBlock
            {
                Text       = section.Heading,
                FontSize   = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x47, 0x55, 0x69)),
                Margin     = new Thickness(0, 0, 0, 6)
            });

            // Body — render paragraph with left-indent for lines starting with bullet
            var bodyBlock = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize     = 13,
                Foreground   = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
                LineHeight   = 20,
                Margin       = new Thickness(0, 0, 0, 20)
            };

            // Split body into lines, preserve formatting cues
            var lines = section.Body.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) bodyBlock.Inlines.Add(new LineBreak());

                var line = lines[i];
                if (line.StartsWith("  • ") || line.StartsWith("   • "))
                {
                    // Bullet line — indent
                    bodyBlock.Inlines.Add(new Run("    " + line.TrimStart()));
                }
                else if (line.Length > 2 && char.IsDigit(line[0]) && line[1] == '.')
                {
                    // Numbered step
                    bodyBlock.Inlines.Add(new Run(line));
                }
                else
                {
                    bodyBlock.Inlines.Add(new Run(line));
                }
            }

            pnlContent.Children.Add(bodyBlock);
        }
    }

    // ── Public helper: open to a specific topic ──────────────────────────────

    public void NavigateTo(string topicTitle)
    {
        for (int i = 0; i < lstTopics.Items.Count; i++)
        {
            if (lstTopics.Items[i] is HelpTopic t &&
                string.Equals(t.Title, topicTitle, StringComparison.OrdinalIgnoreCase))
            {
                lstTopics.SelectedIndex = i;
                return;
            }
        }
    }
}
