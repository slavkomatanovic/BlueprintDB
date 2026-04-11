using System.IO;
using System.Windows;
using Blueprint.App.Backend;
using Blueprint.App.Models;
using Microsoft.Win32;

namespace Blueprint.App;

public partial class ExportSchemaSqlDialog : Window
{
    public ExportSchemaSqlDialog()
    {
        InitializeComponent();

        // Load all programs
        try
        {
            using var db = new BlueprintDbContext();
            var programs = db.Programis
                .Where(p => p.Skriven != true)
                .OrderBy(p => p.Nazivprograma)
                .ToList();
            cbProgram.ItemsSource = programs;

            // Pre-select the currently active program if one is set
            if (AppState.SelectedProgramId > 0)
                cbProgram.SelectedValue = AppState.SelectedProgramId;
            else if (programs.Count > 0)
                cbProgram.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            LogService.Error("ExportSchema", "Error loading programs", ex);
        }

        cbTarget.ItemsSource   = Enum.GetNames<BackendType>();
        cbTarget.SelectedIndex = 0;
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (cbProgram.SelectedValue is not int programId)
        {
            MyMsgBox.Show("Please select a program to export.", icon: MessageBoxImage.Warning);
            return;
        }

        var programName = (cbProgram.SelectedItem as Programi)?.Nazivprograma ?? "export";
        var backendName = cbTarget.SelectedItem?.ToString() ?? "SQLite";
        var target      = Enum.Parse<BackendType>(backendName);

        // Pro gate for non-free backends
        if (!LicenseService.CanUseBackend(target))
        {
            new ProUpgradeDialog { Owner = this }.ShowDialog();
            if (!LicenseService.IsPro) return;
        }

        var dlg = new SaveFileDialog
        {
            Title        = "Export Schema as SQL",
            Filter       = "SQL files (*.sql)|*.sql|All files (*.*)|*.*",
            FileName     = $"{programName}_{backendName}.sql",
            DefaultExt   = ".sql",
            AddExtension = true
        };

        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var sql = SchemaExportService.GenerateDdl(programId, target);
            File.WriteAllText(dlg.FileName, sql, System.Text.Encoding.UTF8);
            LogService.Info("ExportSchema", $"Exported {programName} → {backendName} to {dlg.FileName}");
            MyMsgBox.Show($"Schema exported successfully.\n\n{dlg.FileName}", icon: MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            LogService.Error("ExportSchema", "Export failed", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }
}
