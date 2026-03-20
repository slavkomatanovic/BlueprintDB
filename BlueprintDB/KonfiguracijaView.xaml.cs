using System.IO;
using System.Windows;
using System.Windows.Controls;
using Blueprint.App.Models;
using Microsoft.Win32;

namespace Blueprint.App;

public partial class KonfiguracijaView : UserControl
{
    /// <summary>Fired when the user clicks OK with valid selections.</summary>
    public event EventHandler? ConfigurationComplete;

    /// <summary>Fired when the user clicks Cancel.</summary>
    public event EventHandler? ConfigurationCancelled;

    private static readonly object _dummy = new();
    private static readonly string[] _dbExtensions = { ".sqlite", ".db", ".accdb", ".mdb" };

    public KonfiguracijaView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadProgrami();
        BuildDriveTree();
        if (!string.IsNullOrEmpty(AppState.BackendDatabasePath))
            txtPutanja.Text = AppState.BackendDatabasePath;
        LanguageService.TranslateLogicalChildren(this);
    }

    // ── Program ComboBox ────────────────────────────────────────────────────

    private void LoadProgrami()
    {
        using var db = new BlueprintDbContext();
        cbProgrami.ItemsSource = db.Programis
            .Where(p => p.Skriven != true)
            .OrderBy(p => p.Nazivprograma)
            .ToList();

        if (AppState.SelectedProgramId > 0)
            cbProgrami.SelectedValue = AppState.SelectedProgramId;
    }

    // ── File-system TreeView ────────────────────────────────────────────────

    private void BuildDriveTree()
    {
        tvDirs.Items.Clear();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            tvDirs.Items.Add(MakeDirItem(drive.RootDirectory, drive.Name));
    }

    private TreeViewItem MakeDirItem(DirectoryInfo dir, string? label = null)
    {
        var item = new TreeViewItem { Header = label ?? dir.Name, Tag = dir };
        try
        {
            if (dir.GetDirectories().Length > 0 ||
                dir.GetFiles().Any(f => _dbExtensions.Contains(
                    f.Extension.ToLower(System.Globalization.CultureInfo.InvariantCulture))))
                item.Items.Add(_dummy);
        }
        catch { }
        return item;
    }

    private void tvDirs_ItemExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem item ||
            item.Tag is not DirectoryInfo dir) return;
        if (item.Items.Count != 1 || item.Items[0] != _dummy) return;

        item.Items.Clear();
        try
        {
            foreach (var sub in dir.GetDirectories().OrderBy(d => d.Name))
                item.Items.Add(MakeDirItem(sub));
        }
        catch { }
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
            lvFiles.ItemsSource = dir.GetFiles()
                .Where(f => _dbExtensions.Contains(
                    f.Extension.ToLower(System.Globalization.CultureInfo.InvariantCulture)))
                .OrderBy(f => f.Name)
                .ToList();
        }
        catch { lvFiles.ItemsSource = null; }
    }

    private void lvFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (lvFiles.SelectedItem is FileInfo fi)
            txtPutanja.Text = fi.FullName;
    }

    // ── Browse ──────────────────────────────────────────────────────────────

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

        AppState.SelectedProgramId   = p.Idprograma;
        AppState.SelectedProgramName = p.Nazivprograma ?? "";
        AppState.BackendDatabasePath = txtPutanja.Text.Trim();

        ConfigurationComplete?.Invoke(this, EventArgs.Empty);
    }

    private void BtnOtkazi_Click(object sender, RoutedEventArgs e)
        => ConfigurationCancelled?.Invoke(this, EventArgs.Empty);
}
