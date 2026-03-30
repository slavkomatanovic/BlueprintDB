using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Blueprint.App.Backend;
using Blueprint.App.Models;
using Microsoft.Win32;

namespace Blueprint.App;

public partial class SchemaSyncWizardWindow : Window
{
    private int  _currentStep = 1;
    private int  _programId;
    private bool _analysing;
    private bool _applied;

    // DDL operations collected during analysis (Blueprint is master)
    private readonly List<(string TableName, IReadOnlyList<ColumnSchema> Columns)> _tablesToCreate  = [];
    private readonly List<(string TableName, ColumnSchema Col)>                    _columnsToAdd    = [];
    private readonly List<Models.Relacije>                                          _fksToAdd        = [];
    private readonly List<string>                                                   _surplusTables   = [];
    private readonly List<(string TableName, string ColumnName)>                   _surplusColumns  = [];

    public SchemaSyncWizardWindow()
    {
        InitializeComponent();

        try
        {
            using var db = new BlueprintDbContext();
            cbProgram.ItemsSource = db.Programis
                .Where(p => p.Skriven != true)
                .OrderBy(p => p.Nazivprograma)
                .ToList();
            if (AppState.SelectedProgramId > 0)
                cbProgram.SelectedValue = AppState.SelectedProgramId;
        }
        catch (Exception ex)
        {
            LogService.Error("SchemaSyncWizard", "Failed to load programs", ex);
            MessageBox.Show(ex.Message, "Blueprint", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        cbBackendType.ItemsSource  = Enum.GetNames<BackendType>();
        cbBackendType.SelectedIndex = 0;
        WindowSettings.Restore("SchemaSyncWizardWindow", this);
        Closing += (_, _) => WindowSettings.Save("SchemaSyncWizardWindow", this);
    }

    /// <summary>
    /// Otvara wizard sa već popunjenim vrijednostima i odmah pokreće analizu (preskače korake 1 i 2).
    /// Koristi se iz KonfiguracijaWindow nakon OK.
    /// </summary>
    public SchemaSyncWizardWindow(int programId, BackendType backendType, string connectionString)
    {
        InitializeComponent();

        // Popuni kontrole pa idi direktno na korak 3
        try
        {
            using var db = new BlueprintDbContext();
            cbProgram.ItemsSource = db.Programis
                .Where(p => p.Skriven != true)
                .OrderBy(p => p.Nazivprograma)
                .ToList();
            cbProgram.SelectedValue = programId;
        }
        catch (Exception ex)
        {
            LogService.Error("SchemaSyncWizard", "Failed to load programs", ex);
            MessageBox.Show(ex.Message, "Blueprint", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        cbBackendType.ItemsSource = Enum.GetNames<BackendType>();
        cbBackendType.SelectedItem = backendType.ToString();
        txtPath.Text = connectionString;

        _programId = programId;

        Loaded += async (_, _) =>
        {
            try
            {
                ShowStep(3);
                await RunAnalysisAsync();
            }
            catch (Exception ex)
            {
                LogService.Error("SchemaSync", "Unexpected startup error", ex);
                MessageBox.Show(ex.Message, "Blueprint", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
    }

    // ── Sidebar ──────────────────────────────────────────────────────────────

    private void UpdateSidebar()
    {
        dot1.Fill = B(_currentStep > 1 ? "#10B981" : "#6366F1");
        dot2.Fill = B(_currentStep > 2 ? "#10B981" : _currentStep == 2 ? "#6366F1" : "#4B5563");
        dot3.Fill = B(_currentStep == 3 ? "#6366F1" : "#4B5563");

        lbl1.Foreground = B("White");
        lbl2.Foreground = B(_currentStep >= 2 ? "White" : "#A5B4FC");
        lbl3.Foreground = B(_currentStep >= 3 ? "White" : "#A5B4FC");
    }

    private static SolidColorBrush B(string color)
        => new((Color)ColorConverter.ConvertFromString(color));

    // ── Navigation ────────────────────────────────────────────────────────────

    private void ShowStep(int step)
    {
        page1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        page2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        page3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        _currentStep = step;
        UpdateSidebar();
        btnBack.IsEnabled = step > 1 && !_analysing;
        btnNext.Content   = step == 3 && _applied ? "Finish" : "Next →";
        btnNext.IsEnabled = !(step == 3 && !_applied);
    }

    private async void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            switch (_currentStep)
            {
                case 1:
                    if (cbProgram.SelectedValue is not int id)
                    {
                        MessageBox.Show("Please select a program.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    _programId = id;
                    ShowStep(2);
                    break;

                case 2:
                    if (string.IsNullOrWhiteSpace(GetConnectionString()))
                    {
                        MessageBox.Show("Please specify a database path or connection string.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    ShowStep(3);
                    await RunAnalysisAsync();
                    break;

                case 3:
                    Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("SchemaSync", "Unexpected error in wizard navigation", ex);
            MessageBox.Show(ex.Message, "Blueprint", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1) ShowStep(_currentStep - 1);
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    // ── Backend selection ─────────────────────────────────────────────────────

    private void CbBackendType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cbBackendType?.SelectedItem is null) return;
        var sel = cbBackendType.SelectedItem.ToString();
        bool usesCs  = sel is nameof(BackendType.MySQL) or nameof(BackendType.MariaDB) or
                       nameof(BackendType.SqlServer) or nameof(BackendType.PostgreSQL) or
                       nameof(BackendType.Firebird)  or nameof(BackendType.DB2) or nameof(BackendType.Oracle);
        bool isFolder = sel == nameof(BackendType.DBase);

        var fileV   = !usesCs && !isFolder ? Visibility.Visible : Visibility.Collapsed;
        var folderV = isFolder  ? Visibility.Visible : Visibility.Collapsed;
        var csV     = usesCs    ? Visibility.Visible : Visibility.Collapsed;

        lblPath.Visibility   = fileV;   rowPath.Visibility   = fileV;
        lblFolder.Visibility = folderV; rowFolder.Visibility = folderV;
        lblCs.Visibility    = csV;
        csGrid.Visibility   = csV;
        if (csV == Visibility.Visible)
        {
            var hint      = GetCsHint(sel!);
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
        if (usesCs)                     return txtCs.Text.Trim();
        if (type == BackendType.DBase)  return txtFolder.Text.Trim();
        return txtPath.Text.Trim();
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select database file",
            Filter = "Database files|*.sqlite;*.db;*.accdb;*.mdb;*.fdb;*.gdb|All files|*.*"
        };
        if (dlg.ShowDialog() == true) txtPath.Text = dlg.FileName;
    }

    private void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select dBase folder" };
        if (dlg.ShowDialog() == true) txtFolder.Text = dlg.FolderName;
    }

    // ── Analysis (Blueprint → backend) ───────────────────────────────────────

    private async Task RunAnalysisAsync()
    {
        var backendType = Enum.Parse<BackendType>(cbBackendType.SelectedItem?.ToString() ?? "SQLite");

        if (!LicenseService.CanUseSchemaSyncWith(backendType))
        {
            MessageBox.Show(
                $"Schema Sync with {backendType} requires Blueprint Pro.\n\nUse Tools → License to activate.",
                "Blueprint Pro Required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            btnBack.IsEnabled = true;
            return;
        }

        _analysing        = true;
        btnBack.IsEnabled = false;
        btnNext.IsEnabled = false;
        _tablesToCreate.Clear();
        _columnsToAdd.Clear();
        _fksToAdd.Clear();
        _surplusTables.Clear();
        _surplusColumns.Clear();

        var cs = GetConnectionString();

        try
        {
            await Task.Run(() =>
            {
                LogService.Info("SchemaSync", $"Analysis started for program ID={_programId}");
                using var connector = BackendConnectorFactory.Create(cs, backendType);
                connector.Open();

                using var db = new BlueprintDbContext();

                // Blueprint metadata is the master/source-of-truth
                var bpTables = db.Tabeles
                    .Where(t => t.Idprograma == _programId && t.Skriven != true)
                    .ToList();

                var liveTables = connector.GetTableNames();
                var liveNames  = liveTables.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var bpNames    = bpTables.Select(t => t.Nazivtabele!).ToHashSet(StringComparer.OrdinalIgnoreCase);

                // Očisti stare surplus zapise za ovaj program
                var oldTabele = db.Tabelenoves.Where(t => t.Idprograma == _programId).ToList();
                var oldIds = oldTabele.Select(t => t.Idtabele).ToList();
                db.Kolonenoves.RemoveRange(db.Kolonenoves.Where(k => oldIds.Contains(k.Idtabele)));
                db.Tabelenoves.RemoveRange(oldTabele);
                db.SaveChanges();

                // Tables that exist in Blueprint but are missing from the live backend → CREATE TABLE
                foreach (var bpTable in bpTables.Where(t => !liveNames.Contains(t.Nazivtabele!)))
                {
                    var bpCols = db.Kolones
                        .Where(k => k.Idtabele == bpTable.Idtabele && k.Skriven != true)
                        .OrderBy(k => k.Idkolone)
                        .ToList();

                    var cols = bpCols.Select(k => new ColumnSchema(
                        Name:       k.Nazivkolone!,
                        SqlType:    k.Tippodatka ?? "",
                        NotNull:    k.Allownull == "No",
                        PrimaryKey: k.Key,
                        MaxLength:  int.TryParse(k.Fieldsize, out var fs) ? fs : 0
                    )).ToList();

                    _tablesToCreate.Add((bpTable.Nazivtabele!, cols));
                    LogService.Info("SchemaSync", $"Table to create detected: {bpTable.Nazivtabele}");

                    Dispatcher.Invoke(() =>
                        AddDiffItem($"[+] CREATE TABLE: {bpTable.Nazivtabele}  ({cols.Count} column(s))",
                                    Brushes.SeaGreen));
                }

                // For tables that exist in both — find columns missing from live → ADD COLUMN
                foreach (var bpTable in bpTables.Where(t => liveNames.Contains(t.Nazivtabele!)))
                {
                    var liveColNames = connector.GetColumnNames(bpTable.Nazivtabele!)
                        .ToList();
                    var liveColNamesSet = liveColNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

                    var bpCols = db.Kolones
                        .Where(k => k.Idtabele == bpTable.Idtabele && k.Skriven != true)
                        .OrderBy(k => k.Idkolone)
                        .ToList();

                    foreach (var bpCol in bpCols.Where(k => !liveColNamesSet.Contains(k.Nazivkolone!)))
                    {
                        var col = new ColumnSchema(
                            Name:       bpCol.Nazivkolone!,
                            SqlType:    bpCol.Tippodatka ?? "",
                            NotNull:    bpCol.Allownull == "No",
                            PrimaryKey: bpCol.Key,
                            MaxLength:  int.TryParse(bpCol.Fieldsize, out var fs) ? fs : 0
                        );
                        _columnsToAdd.Add((bpTable.Nazivtabele!, col));
                        LogService.Info("SchemaSync", $"Column to add detected: {bpTable.Nazivtabele}.{bpCol.Nazivkolone}");

                        Dispatcher.Invoke(() =>
                        {
                            var typeStr = string.IsNullOrEmpty(col.SqlType) ? "" : $"  ({col.SqlType})";
                            AddDiffItem($"[+] ADD COLUMN: {bpTable.Nazivtabele}.{bpCol.Nazivkolone}{typeStr}",
                                        Brushes.SeaGreen);
                        });
                    }

                    // Kolone koje su EXTRA u live (surplus — ne u Blueprint)
                    foreach (var liveCol in liveColNames.Where(c => !bpCols.Any(k => string.Equals(k.Nazivkolone, c, StringComparison.OrdinalIgnoreCase))))
                    {
                        // Provjeri postoji li već TabeleNove unos za ovu tabelu (CijelaTabela=False)
                        var tNova = db.Tabelenoves.FirstOrDefault(t =>
                            t.Idprograma == _programId &&
                            t.Nazivtabele == bpTable.Nazivtabele &&
                            t.Cijelatabela == false);
                        if (tNova == null)
                        {
                            tNova = new Tabelenove
                            {
                                Idprograma   = _programId,
                                Nazivtabele  = bpTable.Nazivtabele,
                                Cijelatabela = false,
                                Datumupisa   = DateTime.Now,
                                Skriven      = false
                            };
                            db.Tabelenoves.Add(tNova);
                            db.SaveChanges();
                        }
                        db.Kolonenoves.Add(new Kolonenove
                        {
                            Idtabele    = tNova.Idtabele,
                            Nazivkolone = liveCol,
                            Datumupisa  = DateTime.Now,
                            Skriven     = false
                        });
                        db.SaveChanges();

                        _surplusColumns.Add((bpTable.Nazivtabele!, liveCol));

                        var dropHint = AppState.BrisiNepotrebno ? "  → will DROP" : "";
                        Dispatcher.Invoke(() =>
                            AddDiffItem($"[i] Surplus kolona: {bpTable.Nazivtabele}.{liveCol}{dropHint}", Brushes.DarkOrange));
                    }
                }

                // Tabele koje postoje u live bazi ali NE u Blueprint → surplus cijela tabela
                foreach (var extra in liveTables.Where(t => !bpNames.Contains(t)))
                {
                    // Upiši u TabeleNove
                    var tNova = new Tabelenove
                    {
                        Idprograma   = _programId,
                        Nazivtabele  = extra,
                        Cijelatabela = true,
                        Datumupisa   = DateTime.Now,
                        Skriven      = false
                    };
                    db.Tabelenoves.Add(tNova);
                    db.SaveChanges(); // da dobijemo Idtabele

                    // Upiši sve kolone te tabele u KoloneNove
                    var extraCols = connector.GetColumnNames(extra);
                    foreach (var col in extraCols)
                    {
                        db.Kolonenoves.Add(new Kolonenove
                        {
                            Idtabele    = tNova.Idtabele,
                            Nazivkolone = col,
                            Datumupisa  = DateTime.Now,
                            Skriven     = false
                        });
                    }
                    db.SaveChanges();

                    _surplusTables.Add(extra);

                    var dropHint = AppState.BrisiNepotrebno ? "  → will DROP" : "";
                    Dispatcher.Invoke(() =>
                        AddDiffItem($"[i] Surplus tabela (nije u Blueprint): {extra}  ({extraCols.Count} kolona){dropHint}",
                                    Brushes.DarkOrange));
                }

                // FK constraints — only for backends that support them
                if (connector.SupportsForeignKeys)
                {
                    var liveFks = connector.GetForeignKeys();
                    var bpRelacije = db.Relacijes
                        .Where(r => r.Idprograma == _programId && r.Skriven != true)
                        .ToList();

                    foreach (var rel in bpRelacije)
                    {
                        if (string.IsNullOrEmpty(rel.Tabelad) || string.IsNullOrEmpty(rel.Polje) ||
                            string.IsNullOrEmpty(rel.Tabelal)) continue;

                        bool exists = liveFks.Any(fk =>
                            string.Equals(fk.ChildTable,  rel.Tabelad, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(fk.ChildColumn, rel.Polje,   StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(fk.ParentTable, rel.Tabelal, StringComparison.OrdinalIgnoreCase));

                        if (!exists)
                        {
                            _fksToAdd.Add(rel);
                            LogService.Info("SchemaSync", $"FK to add detected: {rel.Tabelad}.{rel.Polje} → {rel.Tabelal}");
                            Dispatcher.Invoke(() =>
                                AddDiffItem(
                                    $"[+] ADD FK: {rel.Tabelad}.{rel.Polje} → {rel.Tabelal}  [{rel.Nazivrelacije}]",
                                    Brushes.SeaGreen));
                        }
                    }
                }
            });

            bool hasDiffs = _tablesToCreate.Count > 0 || _columnsToAdd.Count > 0 || _fksToAdd.Count > 0;
            bool hasSurplus = AppState.BrisiNepotrebno && (_surplusTables.Count > 0 || _surplusColumns.Count > 0);

            if (!hasDiffs && !hasSurplus)
            {
                lblSyncTitle.Text = "Schema is up to date";
                lblSyncSub.Text   = "No differences found. Live database matches Blueprint schema.";
                AddDiffItem("✓  Live database is already in sync with Blueprint.", Brushes.SeaGreen);
                btnNext.Content   = "Finish";
                btnNext.IsEnabled = true;
            }
            else
            {
                lblSyncTitle.Text = "Differences found";
                var parts = new List<string>();
                if (_tablesToCreate.Count > 0)  parts.Add($"{_tablesToCreate.Count} table(s) to create");
                if (_columnsToAdd.Count > 0)    parts.Add($"{_columnsToAdd.Count} column(s) to add");
                if (_fksToAdd.Count > 0)        parts.Add($"{_fksToAdd.Count} FK constraint(s) to add");
                if (hasSurplus)
                {
                    if (_surplusTables.Count > 0)  parts.Add($"{_surplusTables.Count} surplus table(s) to drop");
                    if (_surplusColumns.Count > 0) parts.Add($"{_surplusColumns.Count} surplus column(s) to drop");
                }
                lblSyncSub.Text     = string.Join(", ", parts) + ".  Click Apply to execute DDL on the backend.";
                btnApply.Visibility = Visibility.Visible;
                btnNext.IsEnabled   = false;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("SchemaSync", "Analysis error", ex);
            lblSyncTitle.Text = "Analysis failed";
            lblSyncSub.Text   = ex.Message;
            btnNext.Content   = "Finish";
            btnNext.IsEnabled = true;
        }
        finally
        {
            _analysing = false;
        }
    }

    // ── Apply DDL ─────────────────────────────────────────────────────────────

    private async void BtnApply_Click(object sender, RoutedEventArgs e)
    {
        btnApply.IsEnabled = false;
        btnNext.IsEnabled  = false;

        var cs          = GetConnectionString();
        var backendType = Enum.Parse<BackendType>(cbBackendType.SelectedItem?.ToString() ?? "SQLite");

        int tablesCreated = 0, columnsAdded = 0, fksAdded = 0, tablesDropped = 0, columnsDropped = 0;

        try
        {
            await Task.Run(() =>
            {
                using var connector = BackendConnectorFactory.Create(cs, backendType);
                connector.Open();

                // Re-check live state at apply time — user may retry after partial failure
                var liveNow = connector.GetTableNames()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var (tableName, cols) in _tablesToCreate)
                {
                    if (liveNow.Contains(tableName)) continue;
                    connector.CreateTable(tableName, cols);
                    liveNow.Add(tableName);   // track for column checks below
                    LogService.Info("SchemaSync", $"Table created: {tableName}");
                    tablesCreated++;
                }

                foreach (var (tableName, col) in _columnsToAdd)
                {
                    var liveColsNow = connector.GetColumnNames(tableName)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (liveColsNow.Contains(col.Name)) continue;
                    connector.AddColumn(tableName, col);
                    LogService.Info("SchemaSync", $"Column added: {tableName}.{col.Name}");
                    columnsAdded++;
                }

                // Drop surplus columns and tables (only when BrisiNepotrebno is enabled)
                if (AppState.BrisiNepotrebno)
                {
                    foreach (var (tableName, columnName) in _surplusColumns)
                    {
                        connector.DropColumn(tableName, columnName);
                        LogService.Info("SchemaSync", $"Surplus column dropped: {tableName}.{columnName}");
                        columnsDropped++;
                    }

                    // Drop surplus tables (whole tables not in Blueprint)
                    var liveNowForDrop = connector.GetTableNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var tableName in _surplusTables)
                    {
                        if (!liveNowForDrop.Contains(tableName)) continue;
                        connector.DropTable(tableName);
                        LogService.Info("SchemaSync", $"Surplus table dropped: {tableName}");
                        tablesDropped++;
                    }
                }

                // FKs must be applied after all tables exist
                if (_fksToAdd.Count > 0 && connector.SupportsForeignKeys)
                {
                    var liveFksNow = connector.GetForeignKeys();
                    foreach (var rel in _fksToAdd)
                    {
                        if (string.IsNullOrEmpty(rel.Tabelad) || string.IsNullOrEmpty(rel.Polje) ||
                            string.IsNullOrEmpty(rel.Tabelal)) continue;

                        bool alreadyExists = liveFksNow.Any(fk =>
                            string.Equals(fk.ChildTable,  rel.Tabelad, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(fk.ChildColumn, rel.Polje,   StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(fk.ParentTable, rel.Tabelal, StringComparison.OrdinalIgnoreCase));
                        if (alreadyExists) continue;

                        var constraintName = string.IsNullOrWhiteSpace(rel.Nazivrelacije)
                            ? $"FK_{rel.Tabelad}_{rel.Tabelal}"
                            : rel.Nazivrelacije;

                        connector.AddForeignKey(constraintName, rel.Tabelad, rel.Polje,
                                                rel.Tabelal, rel.Polje, rel.Updatedeletecascade);
                        LogService.Info("SchemaSync", $"FK created: {rel.Tabelad}.{rel.Polje} → {rel.Tabelal}");
                        fksAdded++;
                    }
                }
            });

            _applied = true;
            var summaryParts = new List<string>
            {
                $"{tablesCreated} table(s) created",
                $"{columnsAdded} column(s) added",
                $"{fksAdded} FK constraint(s) added"
            };
            if (AppState.BrisiNepotrebno)
            {
                summaryParts.Add($"{tablesDropped} surplus table(s) dropped");
                summaryParts.Add($"{columnsDropped} surplus column(s) dropped");
            }
            lblSummary.Text = "✓  Applied: " + string.Join(", ", summaryParts) + ".";
            pnlSummary.Visibility = Visibility.Visible;
            btnNext.Content   = "Finish";
            btnNext.IsEnabled = true;
        }
        catch (Exception ex)
        {
            LogService.Error("SchemaSync", "Error applying DDL", ex);
            pnlSummary.Background  = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xF2));
            pnlSummary.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xCA, 0xCA));
            lblSummary.Foreground  = new SolidColorBrush(Color.FromRgb(0x7F, 0x1D, 0x1D));
            lblSummary.Text        = ex.Message;
            pnlSummary.Visibility  = Visibility.Visible;
            btnApply.IsEnabled     = true;
        }
    }

    private void AddDiffItem(string text, Brush color)
    {
        lstDiff.Items.Add(new ListBoxItem
        {
            Content    = text,
            Foreground = color,
            FontFamily = new FontFamily("Consolas"),
            FontSize   = 11,
            Padding    = new Thickness(4, 1, 4, 1)
        });
    }
}
