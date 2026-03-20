using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Blueprint.App.Models;

namespace Blueprint.App;

public partial class LogWindow : Window
{
    private List<Log> _allRows = [];

    public LogWindow()
    {
        InitializeComponent();
        LanguageService.TranslateWindow(this);
        WindowSettings.Restore("LogWindow", this);
        Closing += (_, _) => WindowSettings.Save("LogWindow", this);
        cbNivo.ItemsSource      = new[] { LanguageService.T("ALL", "All"), "ERROR", "WARNING", "INFO", "SQL" };
        cbNivo.SelectedIndex    = 0;
        cbKategorija.SelectedIndex = 0;
        LoadLog();
    }

    // ── Load & filter ────────────────────────────────────────────────────────

    private void LoadLog()
    {
        try
        {
            using var db = new BlueprintDbContext();
            _allRows = db.Logs
                .OrderByDescending(l => l.Datumvrijeme)
                .ThenByDescending(l => l.Idlog)
                .ToList();

            // Popuni kategorija combobox iz podataka
            var kategorije = _allRows
                .Select(l => l.Kategorija ?? "")
                .Distinct()
                .OrderBy(k => k)
                .ToList();
            var allLabel = LanguageService.T("ALL", "All");
            kategorije.Insert(0, allLabel);

            var prethodnaKat = cbKategorija.SelectedItem?.ToString() ?? allLabel;
            cbKategorija.ItemsSource = kategorije;
            cbKategorija.SelectedItem = kategorije.Contains(prethodnaKat) ? prethodnaKat : allLabel;

            ApplyFilter();
        }
        catch (Exception ex)
        {
            // Ne logujemo u LogService jer može uzrokovati rekurziju
            MessageBox.Show($"Greška pri učitavanju log-a:\n{ex.Message}",
                "Log", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyFilter()
    {
        var allLabel = LanguageService.T("ALL", "All");
        var nivo  = cbNivo.SelectedItem?.ToString() ?? allLabel;
        var kat   = cbKategorija.SelectedItem?.ToString() ?? allLabel;
        var query = txtSearch.Text.Trim();

        var filtered = _allRows.AsEnumerable();

        if (nivo != allLabel && nivo != "All")
            filtered = filtered.Where(l => l.Nivo == nivo);

        if (kat != allLabel && kat != "All")
            filtered = filtered.Where(l => l.Kategorija == kat);

        if (!string.IsNullOrEmpty(query))
            filtered = filtered.Where(l =>
                (l.Poruka   ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (l.Detalji  ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (l.Sqlkod   ?? "").Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (l.Backend  ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));

        var result = filtered.ToList();
        dgLog.ItemsSource = result;

        lblCount.Text  = $"{result.Count} {LanguageService.T("LOG_ENTRIES", "entries")}  /  {LanguageService.T("LOG_TOTAL", "total")} {_allRows.Count}";
        lblStatus.Text = result.Count > 0
            ? $"{LanguageService.T("LOG_LAST_ENTRY", "Last entry:")} {result[0].Datumvrijeme:yyyy-MM-dd HH:mm:ss}"
            : LanguageService.T("LOG_NO_ENTRIES", "No entries.");

        txtDetalji.Text = "";
        txtSqlKod.Text  = "";
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void DgLog_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dgLog.SelectedItem is not Log row)
        {
            txtDetalji.Text = "";
            txtSqlKod.Text  = "";
            return;
        }
        txtDetalji.Text = row.Detalji ?? "";
        txtSqlKod.Text  = row.Sqlkod  ?? "";
    }

    private void Filter_Changed(object sender, EventArgs e)
        => ApplyFilter();

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        => LoadLog();

    private void BtnClear_Click(object sender, RoutedEventArgs e)
    {
        var result = MyMsgBox.Show(
            "MSG_LOG_OCISTI_POTVRDA",
            icon: MessageBoxImage.Warning,
            buttons: MessageBoxButton.YesNo);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            using var db = new BlueprintDbContext();
            db.Logs.RemoveRange(db.Logs);
            db.SaveChanges();
            LoadLog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Greška pri čišćenju log-a:\n{ex.Message}",
                "Log", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnZatvori_Click(object sender, RoutedEventArgs e)
        => Close();
}
