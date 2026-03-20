using System.Collections.ObjectModel;
using System.Windows;
using Blueprint.App.Backend;
using Blueprint.App.Models;
using Microsoft.Win32;

namespace Blueprint.App;

public partial class BatchSchemaSyncWindow : Window
{
    private readonly ObservableCollection<string> _backends = [];
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

    // ── Backend list ─────────────────────────────────────────────────────────

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title      = "Select backend database files",
            Filter     = "Database files|*.sqlite;*.db;*.accdb;*.mdb;*.fdb;*.gdb|All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var path in dlg.FileNames)
            if (!_backends.Contains(path, StringComparer.OrdinalIgnoreCase))
                _backends.Add(path);
    }

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        var toRemove = lstBackends.SelectedItems.Cast<string>().ToList();
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
            MyMsgBox.Show("Dodaj barem jedan backend fajl.", icon: MessageBoxImage.Warning);
            return;
        }

        _running = true;
        btnSyncAll.IsEnabled = false;
        progressBar.Visibility = Visibility.Visible;
        progressBar.Maximum = _backends.Count;
        progressBar.Value   = 0;

        var deleteRedundant = chkBrisiNepotrebno.IsChecked == true;
        var results = new ObservableCollection<BatchSyncResult>();
        dgResults.ItemsSource = results;

        int done = 0;
        foreach (var path in _backends.ToList())
        {
            lblProgress.Text = $"Processing {done + 1} / {_backends.Count}:  {System.IO.Path.GetFileName(path)}";

            var backendType = BackendConnectorFactory.DetectFromPath(path);

            if (!LicenseService.CanUseSchemaSyncWith(backendType))
            {
                results.Add(new BatchSyncResult(path, backendType, 0, 0, 0, 0, 0,
                    new InvalidOperationException($"Pro licence required for {backendType}")));
            }
            else
            {
                var result = await SchemaSyncService.RunAsync(
                    programId, backendType, path, deleteRedundant);
                results.Add(result);
            }

            done++;
            progressBar.Value = done;
        }

        var ok    = results.Count(r => r.Success);
        var error = results.Count(r => !r.Success);
        lblProgress.Text = $"Done — {ok} succeeded, {error} failed.";

        _running = false;
        btnSyncAll.IsEnabled = true;
    }

    private void BtnZatvori_Click(object sender, RoutedEventArgs e) => Close();
}
