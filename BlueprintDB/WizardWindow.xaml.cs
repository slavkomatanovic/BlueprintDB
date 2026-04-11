using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Blueprint.App.Backend;
using Blueprint.App.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace Blueprint.App;

public partial class WizardWindow : Window
{
    // Fired when import completes so MainWindow can refresh the status bar.
    public event EventHandler? ImportComplete;

    private int _currentStep = 1;
    private int _programId;
    private int _existingProgramId;   // > 0 when user selected an existing program to reimport
    private bool _importStarted;
    private bool _importDone;

    public WizardWindow()
    {
        InitializeComponent();
        cbBackendType.ItemsSource   = Enum.GetNames<BackendType>();
        cbBackendType.SelectedIndex = 0;
        LoadExistingPrograms();
        // Focus the dropdown if programs exist, otherwise go straight to the name field
        if (cbExistingProgram.Items.Count > 0)
            cbExistingProgram.Focus();
        else
            txtProgramName.Focus();
        WindowSettings.Restore("WizardWindow", this);
        Closing += (_, _) => WindowSettings.Save("WizardWindow", this);
    }

    private void LoadExistingPrograms()
    {
        try
        {
            using var db = new BlueprintDbContext();
            cbExistingProgram.ItemsSource = db.Programis
                .Where(p => p.Skriven != true)
                .OrderBy(p => p.Nazivprograma)
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Error("WizardWindow", "Error loading programs", ex);
        }
    }

    private void TxtProgramName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(txtProgramName.Text))
        {
            cbExistingProgram.SelectedItem = null;
            _existingProgramId = 0;
            pnlReimportWarning.Visibility = Visibility.Collapsed;
        }
    }

    private void CbExistingProgram_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cbExistingProgram.SelectedValue is int id)
        {
            _existingProgramId = id;
            txtProgramName.Text = "";
            txtVersion.Text = "";
            pnlReimportWarning.Visibility = Visibility.Visible;
        }
        else
        {
            _existingProgramId = 0;
            pnlReimportWarning.Visibility = Visibility.Collapsed;
        }
    }

    // ── Sidebar ──────────────────────────────────────────────────────────────

    private void UpdateSidebar()
    {
        dot1.Fill = B(_currentStep > 1 ? "#10B981" : "#3B82F6");
        dot2.Fill = B(_currentStep > 2 ? "#10B981" : _currentStep == 2 ? "#3B82F6" : "#4B5563");
        dot3.Fill = B(_currentStep == 3 ? "#3B82F6" : "#4B5563");

        lbl1.Foreground = B("White");
        lbl2.Foreground = B(_currentStep >= 2 ? "White" : "#93C5FD");
        lbl3.Foreground = B(_currentStep >= 3 ? "White" : "#93C5FD");
    }

    private static SolidColorBrush B(string color)
        => new((Color)ColorConverter.ConvertFromString(color));

    // ── Step navigation ───────────────────────────────────────────────────────

    private void ShowStep(int step)
    {
        page1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        page2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        page3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        _currentStep = step;
        UpdateSidebar();
        btnBack.IsEnabled = step > 1 && !_importStarted;
        btnNext.Content   = step == 3 && _importDone ? "Finish" : "Next →";
        btnNext.IsEnabled = !(step == 3 && _importStarted && !_importDone);
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1) ShowStep(_currentStep - 1);
    }

    private async void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            switch (_currentStep)
            {
                case 1:
                    if (_existingProgramId <= 0 && string.IsNullOrWhiteSpace(txtProgramName.Text))
                    {
                        MessageBox.Show("Enter a new program name or select an existing program.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    ShowStep(2);
                    break;

                case 2:
                    if (string.IsNullOrWhiteSpace(GetConnectionString()))
                    {
                        MessageBox.Show("Please specify a database path or connection string.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    var selectedType = Enum.Parse<BackendType>(cbBackendType.SelectedItem?.ToString() ?? "SQLite");
                    if (!LicenseService.CanUseSchemaImport(selectedType))
                    {
                        MessageBox.Show(
                            $"Schema Import with {selectedType} requires Blueprint Pro.\n\nSQLite and Access import are free.\nUse Tools → License to activate Pro.",
                            "Blueprint Pro Required",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    ShowStep(3);
                    await RunImportAsync();
                    break;

                case 3:
                    Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("WizardWindow", "Unexpected error in wizard navigation", ex);
            MessageBox.Show(ex.Message, "Blueprint", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    // ── Backend type toggle (same logic as TransferWindow) ────────────────────

    private void CbBackendType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cbBackendType?.SelectedItem is null) return;

        var selected = cbBackendType.SelectedItem.ToString();
        bool usesCs = selected is
            nameof(BackendType.MySQL)     or nameof(BackendType.MariaDB)   or
            nameof(BackendType.SqlServer) or nameof(BackendType.PostgreSQL) or
            nameof(BackendType.Firebird)  or nameof(BackendType.DB2)       or
            nameof(BackendType.Oracle);
        bool isFolder = selected == nameof(BackendType.DBase);

        var fileVis   = !usesCs && !isFolder ? Visibility.Visible : Visibility.Collapsed;
        var folderVis = isFolder  ? Visibility.Visible : Visibility.Collapsed;
        var csVis     = usesCs    ? Visibility.Visible : Visibility.Collapsed;

        lblPath.Visibility   = fileVis;
        rowPath.Visibility   = fileVis;
        lblFolder.Visibility = folderVis;
        rowFolder.Visibility = folderVis;
        lblCs.Visibility    = csVis;
        csGrid.Visibility   = csVis;
        if (csVis == Visibility.Visible)
        {
            var hint      = GetCsHint(selected!);
            hintCs.Text   = hint;
            hintCs.Visibility = txtCs.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            txtCs.ToolTip = hint;
        }
    }

    private void TxtCs_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => hintCs.Visibility = txtCs.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private static string GetCsHint(string backend) => backend switch
    {
        "MySQL" or "MariaDB" => "Server=host;Port=3306;Database=db;Uid=user;Pwd=password;",
        "SqlServer"          => "Server=.\\SQLEXPRESS;Database=db;Integrated Security=True;TrustServerCertificate=True;",
        "PostgreSQL"         => "Host=host;Database=db;Username=user;Password=password;",
        "Oracle"             => "Data Source=host:1521/service;User Id=user;Password=password;",
        "DB2"                => "Server=host:50000;Database=MYDB;UID=user;PWD=pass;Security=none;Authentication=SERVER;",
        "Firebird"           => "DataSource=host;Database=C:\\path\\to\\db.fdb;User=SYSDBA;Password=masterkey;",
        _                    => ""
    };

    private string GetConnectionString()
    {
        var type = Enum.Parse<BackendType>(cbBackendType.SelectedItem?.ToString() ?? "SQLite");
        bool usesCs = type is BackendType.MySQL or BackendType.MariaDB or
            BackendType.SqlServer or BackendType.PostgreSQL or
            BackendType.Firebird  or BackendType.DB2 or BackendType.Oracle;
        if (usesCs)          return txtCs.Text.Trim();
        if (type == BackendType.DBase) return txtFolder.Text.Trim();
        return txtPath.Text.Trim();
    }

    // ── Browse buttons ────────────────────────────────────────────────────────

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select database file",
            Filter = "Database files|*.sqlite;*.db;*.accdb;*.mdb;*.fdb;*.gdb|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        txtPath.Text = dlg.FileName;

        var detected = DetectBackendFromExtension(dlg.FileName);
        if (detected is not null)
        {
            cbBackendType.SelectedItem = detected.ToString();
        }
        else
        {
            var ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
            if (!string.IsNullOrEmpty(ext))
                MessageBox.Show($"File extension '{ext}' does not match any supported database type.",
                    "Unknown file type", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static BackendType? DetectBackendFromExtension(string filePath) =>
        System.IO.Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".sqlite" or ".db"   => BackendType.SQLite,
            ".accdb"  or ".mdb"  => BackendType.Access,
            ".fdb"    or ".gdb"  => BackendType.Firebird,
            _                    => null
        };

    private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select dBase folder" };
        if (dlg.ShowDialog() == true) txtFolder.Text = dlg.FolderName;
    }

    // ── Import ────────────────────────────────────────────────────────────────

    private async Task RunImportAsync()
    {
        _importStarted    = true;
        btnBack.IsEnabled = false;
        btnNext.IsEnabled = false;

        var cs          = GetConnectionString();
        var backendType = Enum.Parse<BackendType>(cbBackendType.SelectedItem?.ToString() ?? "SQLite");

        // Resolve program name — from ComboBox selection or TextBox
        string programName;
        if (_existingProgramId > 0)
        {
            using var dbName = new BlueprintDbContext();
            programName = dbName.Programis.Where(p => p.Idprograma == _existingProgramId)
                              .Select(p => p.Nazivprograma!).FirstOrDefault() ?? "";
        }
        else
        {
            programName = txtProgramName.Text.Trim();
        }
        var version = txtVersion.Text.Trim();

        int tablesAdded    = 0;
        int columnsAdded   = 0;
        int relationsAdded = 0;
        bool importFks          = chkImportFks.IsChecked == true;
        bool importSystemTables = chkImportSystemTables.IsChecked == true;

        try
        {
            if (_existingProgramId > 0)
            {
                // Reimport mode: delete existing schema first
                _programId = _existingProgramId;
                await Task.Run(() => ClearProgramSchema(_programId));
                Log($"Existing schema for '{programName}' cleared. Reimporting…");
            }
            else
            {
                _programId = await Task.Run(() => CreateOrFindProgram(programName, version));
                Log($"Program '{programName}' ready (ID {_programId}).");
            }

            await Task.Run(() =>
            {
                using var connector = BackendConnectorFactory.Create(cs, backendType);
                connector.Open();

                var allTableNames = connector.GetTableNames();
                var tableNames = importSystemTables
                    ? allTableNames
                    : allTableNames.Where(t => !t.StartsWith("MSys", StringComparison.OrdinalIgnoreCase)).ToList();
                int total = tableNames.Count;
                int done  = 0;

                Dispatcher.Invoke(() => Log($"Found {total} table(s) in the source database."));

                using var db = new BlueprintDbContext();

                // Pre-load known data types to avoid repeated DB hits
                var knownTypes = db.Tippodatkas
                    .Where(t => t.Skriven != true)
                    .Select(t => t.Tippodatka1!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var tableName in tableNames)
                {
                    done++;
                    Dispatcher.Invoke(() =>
                    {
                        progressBar.Value = (double)done / total * 100;
                        Log($"  [{done}/{total}]  {tableName}");
                    });

                    // Find or create Tabele
                    var existing = db.Tabeles.FirstOrDefault(t =>
                        t.Idprograma   == _programId &&
                        t.Nazivtabele  == tableName  &&
                        t.Skriven      != true);

                    int tableId;
                    if (existing == null)
                    {
                        var newTable = new Tabele
                        {
                            Idprograma     = _programId,
                            Nazivtabele    = tableName,
                            Korisnik       = Environment.UserName,
                            Datumupisa     = DateTime.Now,
                            Skriven        = false,
                            Vremenskipecat = (decimal)DateTime.Now.TimeOfDay.TotalSeconds
                        };
                        db.Tabeles.Add(newTable);
                        db.SaveChanges();
                        tableId = newTable.Idtabele;
                        tablesAdded++;
                    }
                    else
                    {
                        tableId = existing.Idtabele;
                    }

                    // Import columns; collect new SQL types along the way
                    var columns = connector.GetColumnSchema(tableName);
                    foreach (var col in columns)
                    {
                        bool colExists = db.Kolones.Any(k =>
                            k.Idtabele    == tableId  &&
                            k.Nazivkolone == col.Name &&
                            k.Skriven     != true);

                        // Normalize backend-specific type to ADO vocabulary
                        var adoType = TypeMappings.NormalizeToAdo(col.SqlType, col.MaxLength);

                        if (!colExists)
                        {
                            db.Kolones.Add(new Kolone
                            {
                                Idtabele       = tableId,
                                Nazivkolone    = col.Name,
                                Tippodatka     = adoType,
                                Fieldsize      = col.MaxLength > 0 ? col.MaxLength.ToString() : null,
                                Allownull      = col.NotNull ? "No" : "YES",
                                Key            = col.PrimaryKey,
                                Korisnik       = Environment.UserName,
                                Datumupisa     = DateTime.Now,
                                Skriven        = false,
                                Vremenskipecat = (decimal)DateTime.Now.TimeOfDay.TotalSeconds
                            });
                            columnsAdded++;
                        }

                        // Register any new ADO type in the tippodatka lookup table.
                        // The table has no PK (HasNoKey), so EF tracking is not possible — use raw SQL.
                        if (knownTypes.Add(adoType))
                        {
                            db.Database.ExecuteSqlRaw(
                                "INSERT INTO tippodatka (tippodatka, korisnik, datumupisa, skriven, vremenskipecat) VALUES ({0},{1},{2},{3},{4})",
                                adoType,
                                Environment.UserName,
                                DateTime.Now,
                                false,
                                (decimal)DateTime.Now.TimeOfDay.TotalSeconds);
                        }
                    }
                    db.SaveChanges();
                }

                // ── Foreign key import ────────────────────────────────────────
                if (importFks && connector.SupportsForeignKeys)
                {
                    Dispatcher.Invoke(() => Log("Importing foreign keys…"));
                    var fks = connector.GetForeignKeys();
                    foreach (var fk in fks)
                    {
                        // Skip relations involving system tables unless explicitly requested
                        if (!importSystemTables &&
                            (fk.ParentTable.StartsWith("MSys", StringComparison.OrdinalIgnoreCase) ||
                             fk.ChildTable.StartsWith("MSys",  StringComparison.OrdinalIgnoreCase)))
                            continue;
                        bool exists = db.Relacijes.Any(r =>
                            r.Idprograma == _programId &&
                            r.Tabelal    == fk.ChildTable  &&
                            r.Polje      == fk.ChildColumn &&
                            r.Skriven    != true);
                        if (exists) continue;

                        db.Relacijes.Add(new Relacije
                        {
                            Idprograma          = _programId,
                            Tabelal             = fk.ParentTable,
                            Tabelad             = fk.ChildTable,
                            Polje               = fk.ChildColumn,
                            Nazivrelacije       = fk.ConstraintName,
                            Updatedeletecascade = false,
                            Korisnik            = Environment.UserName,
                            Datumupisa          = DateTime.Now,
                            Skriven             = false,
                            Vremenskipecat      = (decimal)DateTime.Now.TimeOfDay.TotalSeconds
                        });
                        relationsAdded++;
                    }
                    db.SaveChanges();
                    Dispatcher.Invoke(() => Log($"  {relationsAdded} relation(s) imported."));
                }
            });

            // Success
            _importDone = true;
            progressBar.Value  = 100;
            lblImportTitle.Text = "Import complete!";
            lblImportSub.Text   = $"The schema for '{programName}' has been imported successfully.";
            var relPart = relationsAdded > 0 ? $", {relationsAdded} relation(s)" : "";
            lblSummary.Text = $"✓  {tablesAdded} table(s), {columnsAdded} column(s){relPart} added. " +
                              "You can now browse the schema in the Tables and Relations windows.";
            pnlSummary.Visibility = Visibility.Visible;

            AppState.SelectedProgramId   = _programId;
            AppState.SelectedProgramName = programName;
            ImportComplete?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LogService.Error("Import", "Schema import failed", ex);
            _importDone = true;
            progressBar.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            lblImportTitle.Text = "Import failed";
            lblImportSub.Text   = "An error occurred. The program record was created but schema may be incomplete.";
            pnlSummary.Background   = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xF2));
            pnlSummary.BorderBrush  = new SolidColorBrush(Color.FromRgb(0xFB, 0xCA, 0xCA));
            lblSummary.Foreground   = new SolidColorBrush(Color.FromRgb(0x7F, 0x1D, 0x1D));
            lblSummary.Text         = ex.Message;
            pnlSummary.Visibility   = Visibility.Visible;
        }
        finally
        {
            btnNext.Content   = "Finish";
            btnNext.IsEnabled = true;
        }
    }

    private static void ClearProgramSchema(int programId)
    {
        using var db = new BlueprintDbContext();
        var tableIds = db.Tabeles
            .Where(t => t.Idprograma == programId)
            .Select(t => t.Idtabele)
            .ToList();
        if (tableIds.Count > 0)
            db.Kolones.RemoveRange(db.Kolones.Where(k => tableIds.Contains(k.Idtabele)));
        db.Tabeles.RemoveRange(db.Tabeles.Where(t => t.Idprograma == programId));
        db.Relacijes.RemoveRange(db.Relacijes.Where(r => r.Idprograma == programId));
        db.SaveChanges();
    }

    private static int CreateOrFindProgram(string name, string version)
    {
        using var db = new BlueprintDbContext();
        var existing = db.Programis.FirstOrDefault(p =>
            p.Nazivprograma == name && p.Skriven != true);
        if (existing != null) return existing.Idprograma;

        var prog = new Programi
        {
            Nazivprograma  = name,
            Verzija        = version,
            Korisnik       = Environment.UserName,
            Datumupisa     = DateTime.Now,
            Skriven        = false,
            Vremenskipecat = (decimal)DateTime.Now.TimeOfDay.TotalSeconds
        };
        db.Programis.Add(prog);
        db.SaveChanges();
        return prog.Idprograma;
    }

    private void Log(string msg)
    {
        lstLog.Items.Add(msg);
        lstLog.ScrollIntoView(lstLog.Items[^1]);
    }
}
