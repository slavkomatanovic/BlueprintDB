using System.Collections.ObjectModel;
using System.Windows;
using Blueprint.App.Backend;
using Blueprint.App.Models;
using Microsoft.Win32;

namespace Blueprint.App;

public partial class BatchSchemaSyncWindow : Window
{
    private readonly ObservableCollection<BackendEntry> _backends = [];
    private bool _running;

    public BatchSchemaSyncWindow()
    {
        InitializeComponent();
        lstBackends.ItemsSource = _backends;

        chkBrisiNepotrebno.IsChecked = AppState.BrisiNepotrebno;

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
            LogService.Error("BatchSync", "Error loading programs", ex);
        }

        LanguageService.TranslateWindow(this);
        WindowSettings.Restore("BatchSchemaSyncWindow", this);
        Closing += (_, _) => WindowSettings.Save("BatchSchemaSyncWindow", this);
    }

    /// <summary>Pre-populated from firmeadrese. User reviews the list then clicks Sync All.</summary>
    public BatchSchemaSyncWindow(int programId, IEnumerable<BackendEntry> entries) : this()
    {
        cbProgrami.SelectedValue = programId;
        foreach (var e in entries)
            _backends.Add(e);
    }

    // ── Backend list ─────────────────────────────────────────────────────────

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title       = "Select backend database files",
            Filter      = "Database files|*.sqlite;*.db;*.accdb;*.mdb;*.fdb;*.gdb|All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var path in dlg.FileNames)
        {
            if (_backends.Any(b => b.Cs.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            try
            {
                var type = BackendConnectorFactory.DetectFromPath(path);
                _backends.Add(new BackendEntry(System.IO.Path.GetFileName(path), type, path));
            }
            catch (Exception ex)
            {
                LogService.Warning("BatchSync", $"Cannot detect type for {path}: {ex.Message}");
            }
        }
    }

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        var toRemove = lstBackends.SelectedItems.Cast<BackendEntry>().ToList();
        foreach (var item in toRemove)
            _backends.Remove(item);
    }

    // ── Sync All ─────────────────────────────────────────────────────────────

    private async void BtnSyncAll_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;

        if (cbProgrami.SelectedValue is not int programId)
        {
            MyMsgBox.Show("MSG_ODABERI_PROGRAM_KONFIG", icon: MessageBoxImage.Warning);
            return;
        }
        if (_backends.Count == 0)
        {
            MyMsgBox.Show("Dodaj barem jedan backend.", icon: MessageBoxImage.Warning);
            return;
        }

        await RunSyncAsync(programId);
    }

    private async Task RunSyncAsync(int programId)
    {
        _running = true;
        btnSyncAll.IsEnabled   = false;
        progressBar.Visibility = Visibility.Visible;
        progressBar.Maximum    = _backends.Count;
        progressBar.Value      = 0;

        var deleteRedundant = chkBrisiNepotrebno.IsChecked == true;
        var results = new ObservableCollection<BatchSyncResult>();
        dgResults.ItemsSource = results;

        int done = 0;
        foreach (var entry in _backends.ToList())
        {
            lblProgress.Text = $"Processing {done + 1} / {_backends.Count}:  {entry.Label}";

            if (!LicenseService.CanUseSchemaSyncWith(entry.Type))
            {
                results.Add(new BatchSyncResult(entry.Label, entry.Type, 0, 0, 0, 0, 0,
                    new InvalidOperationException($"Pro licence required for {entry.Type}")));
            }
            else
            {
                var result = await SchemaSyncService.RunAsync(
                    programId, entry.Type, entry.Cs, deleteRedundant);
                results.Add(result with { Path = entry.Label });
            }

            done++;
            progressBar.Value = done;
        }

        var ok    = results.Count(r => r.Success);
        var error = results.Count(r => !r.Success);
        lblProgress.Text = $"Done — {ok} succeeded, {error} failed.";

        _running          = false;
        btnSyncAll.IsEnabled = true;
    }

    private void BtnZatvori_Click(object sender, RoutedEventArgs e) => Close();
}
