using System.IO;
using System.Windows;
using System.Windows.Controls;
using Blueprint.App.Backend;
using Blueprint.App.Models;
using Microsoft.Win32;

namespace Blueprint.App;

public partial class KonfiguracijaWindow : Window
{
    public event EventHandler? ConfigurationComplete;

    // Sentinel object — used as a placeholder child so TreeViewItems show expand arrows
    private static readonly object _dummy = new();

    private static readonly string[] _dbExtensions = { ".sqlite", ".db", ".accdb", ".mdb" };

    public KonfiguracijaWindow()
    {
        InitializeComponent();

        cbBackendType.ItemsSource  = Enum.GetNames<BackendType>();
        cbBackendType.SelectedItem = AppState.BackendType.ToString();
        if (cbBackendType.SelectedIndex < 0) cbBackendType.SelectedIndex = 0;

        LoadProgrami();
        BuildDriveTree();
        RestoreBackendValues();
        chkBrisiNepotrebno.IsChecked = AppState.BrisiNepotrebno;
        LanguageService.TranslateWindow(this);
        WindowSettings.Restore("KonfiguracijaWindow", this);
        Closing += (_, _) => WindowSettings.Save("KonfiguracijaWindow", this);
    }

    private void RestoreBackendValues()
    {
        if (string.IsNullOrEmpty(AppState.BackendDatabasePath)) return;
        var bt = AppState.BackendType;
        bool usesCs  = bt is BackendType.MySQL or BackendType.MariaDB or
                       BackendType.SqlServer or BackendType.PostgreSQL or
                       BackendType.Firebird  or BackendType.DB2 or BackendType.Oracle;
        if (usesCs)
            txtCs.Text = AppState.BackendDatabasePath;
        else if (bt == BackendType.DBase)
            txtFolder.Text = AppState.BackendDatabasePath;
        else
            txtPutanja.Text = AppState.BackendDatabasePath;
    }

    // ── Program ComboBox ────────────────────────────────────────────────────

    private void LoadProgrami()
    {
        try
        {
            using var db = new BlueprintDbContext();
            cbProgrami.ItemsSource = db.Programis
                .Where(p => p.Skriven != true)
                .OrderBy(p => p.Nazivprograma)
                .ToList();

            if (AppState.SelectedProgramId > 0)
                cbProgrami.SelectedValue = AppState.SelectedProgramId;
        }
        catch (Exception ex)
        {
            LogService.Error("Konfiguracija", "Error loading programs", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    // ── File-system TreeView ────────────────────────────────────────────────

    private void BuildDriveTree()
    {
        tvDirs.Items.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var item = MakeDirItem(drive.RootDirectory, drive.Name);
            tvDirs.Items.Add(item);
        }
    }

    private TreeViewItem MakeDirItem(DirectoryInfo dir, string? label = null)
    {
        var item = new TreeViewItem
        {
            Header = label ?? dir.Name,
            Tag    = dir
        };

        // Add dummy so the expand arrow is shown; replaced on first expand
        try
        {
            if (dir.GetDirectories().Length > 0 ||
                dir.GetFiles().Any(f => _dbExtensions.Contains(
                    f.Extension.ToLower(System.Globalization.CultureInfo.InvariantCulture))))
            {
                item.Items.Add(_dummy);
            }
        }
        catch (UnauthorizedAccessException) { /* access denied — no arrow */ }
        catch (Exception ex) { LogService.Warning("Konfiguracija", $"Error reading directory: {dir.FullName} — {ex.Message}"); }

        return item;
    }

    private void tvDirs_ItemExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem item ||
            item.Tag is not DirectoryInfo dir) return;

        // Only expand once (replace dummy with real children)
        if (item.Items.Count != 1 || item.Items[0] != _dummy) return;

        item.Items.Clear();
        try
        {
            foreach (var sub in dir.GetDirectories().OrderBy(d => d.Name))
                item.Items.Add(MakeDirItem(sub));
        }
        catch (UnauthorizedAccessException) { /* access denied */ }
        catch (Exception ex) { LogService.Warning("Konfiguracija", $"Error expanding directory: {dir.FullName} — {ex.Message}"); }
    }

    private void tvDirs_SelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item && item.Tag is DirectoryInfo dir)
            ShowFilesIn(dir);
    }

    private void ShowFilesIn(DirectoryInfo dir)
    {
        try
        {
            lvFiles.ItemsSource = dir
                .GetFiles()
                .Where(f => _dbExtensions.Contains(
                    f.Extension.ToLower(System.Globalization.CultureInfo.InvariantCulture)))
                .OrderBy(f => f.Name)
                .ToList();
        }
        catch (UnauthorizedAccessException)
        {
            lvFiles.ItemsSource = null;
        }
        catch (Exception ex)
        {
            LogService.Warning("Konfiguracija", $"Error listing files: {dir.FullName} — {ex.Message}");
            lvFiles.ItemsSource = null;
        }
    }

    private async void lvFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lvFiles.SelectedItem is FileInfo fi)
        {
            txtPutanja.Text = fi.FullName;
            await CheckForFirmeAdreseAsync();
        }
    }

    // ── Backend type toggle ─────────────────────────────────────────────────

    private void CbBackendType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cbBackendType?.SelectedItem is null) return;
        var sel = cbBackendType.SelectedItem.ToString();
        bool usesCs  = sel is nameof(BackendType.MySQL) or nameof(BackendType.MariaDB) or
                       nameof(BackendType.SqlServer) or nameof(BackendType.PostgreSQL) or
                       nameof(BackendType.Firebird)  or nameof(BackendType.DB2) or nameof(BackendType.Oracle);
        bool isFolder = sel == nameof(BackendType.DBase);

        fileBrowserPanel.Visibility = !usesCs && !isFolder ? Visibility.Visible : Visibility.Collapsed;
        csPanel.Visibility          = usesCs    ? Visibility.Visible : Visibility.Collapsed;
        folderPanel.Visibility      = isFolder  ? Visibility.Visible : Visibility.Collapsed;
        pathRow.Visibility          = !usesCs && !isFolder ? Visibility.Visible : Visibility.Collapsed;

        if (usesCs)
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
        "DB2"                => "Driver={IBM DB2 ODBC DRIVER};Database=MYDB;Hostname=host;Port=50000;Protocol=TCPIP;Uid=user;Pwd=pass;",
        "Firebird"           => "DataSource=host;Database=C:\\path\\to\\db.fdb;User=SYSDBA;Password=masterkey;",
        _                    => ""
    };

    private string GetBackendCs()
    {
        var type = Enum.Parse<BackendType>(cbBackendType.SelectedItem?.ToString() ?? "SQLite");
        bool usesCs = type is BackendType.MySQL or BackendType.MariaDB or
            BackendType.SqlServer or BackendType.PostgreSQL or
            BackendType.Firebird  or BackendType.DB2 or BackendType.Oracle;
        if (usesCs)                    return txtCs.Text.Trim();
        if (type == BackendType.DBase) return txtFolder.Text.Trim();
        return txtPutanja.Text.Trim();
    }

    // ── Browse buttons ──────────────────────────────────────────────────────

    private async void BtnPregledaj_Click(object sender, RoutedEventArgs e)
    {
        var type = Enum.Parse<BackendType>(cbBackendType.SelectedItem?.ToString() ?? "SQLite");
        var filter = type == BackendType.Access
            ? "Access files|*.accdb;*.mdb|All files|*.*"
            : "SQLite files|*.sqlite;*.db|All files|*.*";

        var dlg = new OpenFileDialog
        {
            Title  = "Select backend database file",
            Filter = filter,
            InitialDirectory = string.IsNullOrEmpty(AppState.BackendDatabasePath)
                ? @"C:\"
                : Path.GetDirectoryName(AppState.BackendDatabasePath) ?? @"C:\"
        };
        if (dlg.ShowDialog() == true)
        {
            txtPutanja.Text = dlg.FileName;
            await CheckForFirmeAdreseAsync();
        }
    }

    private async void BtnBrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select dBase folder" };
        if (dlg.ShowDialog() == true)
        {
            txtFolder.Text = dlg.FolderName;
            await CheckForFirmeAdreseAsync();
        }
    }

    private async void TxtCs_LostFocus(object sender, RoutedEventArgs e)
        => await CheckForFirmeAdreseAsync();

    // ── firmeadrese detection ────────────────────────────────────────────────

    private static readonly string[] _firmeAdreseNames =
        ["firmeadrese", "firmaadresa", "FirmeAdrese"];

    /// <summary>
    /// Ako adresa sadrži ODBC connection string (npr. "ODBC;DSN=SQLite3 Datasource;DATABASE=C:\...\db.sqlite"),
    /// izvlači stvarnu putanju iz DATABASE= parametra i vraća je kao čisti file path.
    /// Za sve ostale formate vraća adresu nepromijenjenu.
    /// </summary>
    private static string ResolveAdresa(string adresa)
    {
        if (!adresa.StartsWith("ODBC", StringComparison.OrdinalIgnoreCase)) return adresa;

        // Traži DATABASE=<path> — završava na ';' ili kraju stringa
        var match = System.Text.RegularExpressions.Regex.Match(
            adresa, @"DATABASE=([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : adresa;
    }

    // Pravi naziv tabele pronađen u backendu (čuva se radi prikaza u UI porukama)
    private string? _foundFirmeAdreseTable;

    private async Task CheckForFirmeAdreseAsync()
    {
        var cs = GetBackendCs();
        if (string.IsNullOrWhiteSpace(cs))
        {
            pnlAutoRepair.Visibility = Visibility.Collapsed;
            return;
        }

        BackendType type;
        try { type = Enum.Parse<BackendType>(cbBackendType.SelectedItem?.ToString() ?? "SQLite"); }
        catch { return; }

        lblFirmeAdreseStatus.Text = "Checking…";
        pnlAutoRepair.Visibility  = Visibility.Visible;
        chkAutoRepairAllDatabases.IsEnabled = false;
        _foundFirmeAdreseTable = null;

        string? found = await Task.Run(() =>
        {
            try
            {
                using var c = BackendConnectorFactory.Create(cs, type);
                c.Open();
                var tables = c.GetTableNames();
                return tables.FirstOrDefault(t => _firmeAdreseNames.Any(n =>
                    string.Equals(t, n, StringComparison.OrdinalIgnoreCase)));
            }
            catch { return null; }
        });

        if (found != null)
        {
            _foundFirmeAdreseTable          = found;
            lblFirmeAdreseStatus.Text       = $"✓ {found} table found";
            chkAutoRepairAllDatabases.IsEnabled = true;
        }
        else
        {
            pnlAutoRepair.Visibility = Visibility.Collapsed;
            chkAutoRepairAllDatabases.IsChecked = false;
        }
    }

    private async Task<List<BackendEntry>> ReadFirmeAdreseAsync(string cs, BackendType primaryType)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var connector = BackendConnectorFactory.Create(cs, primaryType);
                connector.Open();

                var tables = connector.GetTableNames();
                var tableName = tables.FirstOrDefault(t => _firmeAdreseNames.Any(n =>
                    string.Equals(t, n, StringComparison.OrdinalIgnoreCase)));
                if (tableName == null) return [];

                // "adresa" je jedino obavezno polje
                var columns   = connector.GetColumnNames(tableName);
                var adresaCol = columns.FirstOrDefault(c =>
                                    c.Equals("Adresa", StringComparison.OrdinalIgnoreCase));
                if (adresaCol == null) return [];

                // firmaCol je opcionalan
                var firmaCol   = columns.FirstOrDefault(c =>
                                     c.Equals("Firma",      StringComparison.OrdinalIgnoreCase) ||
                                     c.Equals("nazivfirme", StringComparison.OrdinalIgnoreCase));
                var skrivenCol = columns.FirstOrDefault(c =>
                                     c.Equals("skriven", StringComparison.OrdinalIgnoreCase));

                var colsToRead = new List<string> { adresaCol };
                if (firmaCol   != null) colsToRead.Add(firmaCol);
                if (skrivenCol != null) colsToRead.Add(skrivenCol);

                var rows    = connector.ReadAll(tableName, colsToRead);
                var entries = new List<BackendEntry>();

                foreach (var row in rows)
                {
                    var adresaRaw = row[adresaCol]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(adresaRaw)) continue;

                    // skip hidden records (NULL treated as not hidden)
                    if (skrivenCol != null)
                    {
                        var sk = row[skrivenCol];
                        if (sk != null && sk != DBNull.Value &&
                            sk.ToString() != "0" && sk is not false and not 0L) continue;
                    }

                    // Izvuci stvarnu putanju ako je ODBC connection string
                    var resolvedPath = ResolveAdresa(adresaRaw);

                    BackendType entryType;
                    try { entryType = BackendConnectorFactory.DetectFromPath(resolvedPath); }
                    catch (Exception ex)
                    {
                        LogService.Warning("Konfiguracija",
                            $"Cannot detect backend type for '{resolvedPath}': {ex.Message}");
                        continue;
                    }

                    var firma = firmaCol != null ? row[firmaCol]?.ToString() : null;
                    entries.Add(new BackendEntry(
                        string.IsNullOrWhiteSpace(firma) ? resolvedPath : firma,
                        entryType, resolvedPath));
                }

                return entries;
            }
            catch (Exception ex)
            {
                LogService.Warning("Konfiguracija", $"Error reading firmeadrese: {ex.Message}");
                return [];
            }
        });
    }

    // ── OK / Cancel ─────────────────────────────────────────────────────────

    private async void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (cbProgrami.SelectedItem is not Programi p)
        {
            MyMsgBox.Show("MSG_ODABERI_PROGRAM_KONFIG", icon: MessageBoxImage.Warning);
            return;
        }

        var cs = GetBackendCs();
        if (string.IsNullOrWhiteSpace(cs))
        {
            MyMsgBox.Show("MSG_ODABERI_BAZU", icon: MessageBoxImage.Warning);
            return;
        }

        var backendType = Enum.Parse<BackendType>(cbBackendType.SelectedItem?.ToString() ?? "SQLite");

        AppState.SelectedProgramId   = p.Idprograma;
        AppState.SelectedProgramName = p.Nazivprograma ?? "";
        AppState.BackendDatabasePath = cs;
        AppState.BackendType         = backendType;
        AppState.BrisiNepotrebno     = chkBrisiNepotrebno.IsChecked == true;

        ConfigurationComplete?.Invoke(this, EventArgs.Empty);

        // Otvori Schema Sync wizard sa popunjenim vrijednostima — isti UI i ista logika
        try
        {
            var syncWin = new SchemaSyncWizardWindow(p.Idprograma, backendType, cs)
            {
                Owner = Owner
            };
            syncWin.Show();
        }
        catch (Exception ex)
        {
            LogService.Error("Konfiguracija", "Error starting schema sync from configuration", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }

        // Ako je označen auto-repair, pročitaj firmeadrese i otvori batch prozor
        if (chkAutoRepairAllDatabases.IsChecked == true)
        {
            var entries = await ReadFirmeAdreseAsync(cs, backendType);
            if (entries.Count > 0)
            {
                try
                {
                    var batchWin = new BatchSchemaSyncWindow(p.Idprograma, entries)
                    {
                        Owner = Owner
                    };
                    batchWin.Show();
                }
                catch (Exception ex)
                {
                    LogService.Error("Konfiguracija", "Error starting batch sync from firmeadrese", ex);
                    MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
                }
            }
            else
            {
                var tbl = _foundFirmeAdreseTable ?? "firmeadrese";
                MyMsgBox.Show($"No usable entries found in the '{tbl}' table.",
                    icon: MessageBoxImage.Warning);
            }
        }

        Close();
    }

    private void BtnOtkazi_Click(object sender, RoutedEventArgs e)
        => Close();
}
