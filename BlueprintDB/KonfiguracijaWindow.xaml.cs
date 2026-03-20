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
        LoadProgrami();
        BuildDriveTree();
        if (!string.IsNullOrEmpty(AppState.BackendDatabasePath))
            txtPutanja.Text = AppState.BackendDatabasePath;
        chkBrisiNepotrebno.IsChecked = AppState.BrisiNepotrebno;
        LanguageService.TranslateWindow(this);
        WindowSettings.Restore("KonfiguracijaWindow", this);
        Closing += (_, _) => WindowSettings.Save("KonfiguracijaWindow", this);
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

    private void lvFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lvFiles.SelectedItem is FileInfo fi)
            txtPutanja.Text = fi.FullName;
    }

    // ── Browse button ───────────────────────────────────────────────────────

    private void BtnPregledaj_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Select backend database file",
            Filter = "Database files|*.sqlite;*.db;*.accdb;*.mdb|All files|*.*",
            InitialDirectory = string.IsNullOrEmpty(AppState.BackendDatabasePath)
                ? @"C:\"
                : Path.GetDirectoryName(AppState.BackendDatabasePath) ?? @"C:\"
        };

        if (dlg.ShowDialog() == true)
            txtPutanja.Text = dlg.FileName;
    }

    // ── OK / Cancel ─────────────────────────────────────────────────────────

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (cbProgrami.SelectedItem is not Programi p)
        {
            MyMsgBox.Show("MSG_ODABERI_PROGRAM_KONFIG", icon: MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(txtPutanja.Text))
        {
            MyMsgBox.Show("MSG_ODABERI_BAZU", icon: MessageBoxImage.Warning);
            return;
        }

        AppState.SelectedProgramId        = p.Idprograma;
        AppState.SelectedProgramName      = p.Nazivprograma ?? "";
        AppState.BackendDatabasePath      = txtPutanja.Text.Trim();
        AppState.BrisiNepotrebno = chkBrisiNepotrebno.IsChecked == true;

        ConfigurationComplete?.Invoke(this, EventArgs.Empty);

        // Otvori Schema Sync wizard sa popunjenim vrijednostima — isti UI i ista logika
        try
        {
            var backendType = BackendConnectorFactory.DetectFromPath(AppState.BackendDatabasePath);
            var syncWin     = new SchemaSyncWizardWindow(p.Idprograma, backendType, AppState.BackendDatabasePath)
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

        Close();
    }

    private void BtnOtkazi_Click(object sender, RoutedEventArgs e)
        => Close();
}
