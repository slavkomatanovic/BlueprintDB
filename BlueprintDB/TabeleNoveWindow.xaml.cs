using System.Text;
using System.Windows;
using System.Windows.Input;
using Blueprint.App.Backend;
using Blueprint.App.Models;

namespace Blueprint.App;

public partial class TabeleNoveWindow : Window
{
    // View model za prikaz u dgTabele
    private record TabelaNovRow(int Idtabele, int Idprograma, string? Nazivtabele, bool Cijelatabela,
        string? Verzija)
    {
        public string TipOpis => Cijelatabela ? "Cijela tabela" : "Surplus kolone";
    }

    public TabeleNoveWindow()
    {
        InitializeComponent();
        lblSubtitle.Text = AppState.SelectedProgramName;
        LanguageService.TranslateWindow(this);
        WindowSettings.Restore("TabeleNoveWindow", this);
        Closing += (_, _) => WindowSettings.Save("TabeleNoveWindow", this);
        Refresh();
    }

    private void Refresh()
    {
        try
        {
            using var db = new BlueprintDbContext();
            var rows = db.Tabelenoves
                .Where(t => t.Idprograma == AppState.SelectedProgramId && t.Skriven != true)
                .OrderBy(t => t.Nazivtabele)
                .Select(t => new TabelaNovRow(t.Idtabele, t.Idprograma, t.Nazivtabele, t.Cijelatabela, t.Verzija))
                .ToList();
            dgTabele.ItemsSource = rows;
            dgKolone.ItemsSource = null;
            UpdateStatus(rows.Count);
        }
        catch (Exception ex)
        {
            LogService.Error("Surplus", "Error loading surplus tables", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void UpdateStatus(int count)
    {
        lblStatus.Text = count == 0
            ? "Nema surplus tabela."
            : $"{count} surplus unos(a).";
    }

    private void DgTabele_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (dgTabele.SelectedItem is not TabelaNovRow row) { dgKolone.ItemsSource = null; return; }

        try
        {
            using var db = new BlueprintDbContext();
            dgKolone.ItemsSource = db.Kolonenoves
                .Where(k => k.Idtabele == row.Idtabele && k.Skriven != true)
                .OrderBy(k => k.Nazivkolone)
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Error("Surplus", "Error loading surplus columns", ex);
        }

        lblKoloneHeader.Text = row.Cijelatabela
            ? $"Kolone tabele  '{row.Nazivtabele}'  (cijela tabela je surplus)"
            : $"Surplus kolone u tabeli  '{row.Nazivtabele}'";
    }

    // ── Izbriši (single) ────────────────────────────────────────────────────

    private void BtnIzbrisi_Click(object sender, RoutedEventArgs e)
    {
        if (dgTabele.SelectedItem is not TabelaNovRow row) return;
        var backendType = BackendConnectorFactory.DetectFromPath(AppState.BackendDatabasePath);

        if (row.Cijelatabela)
        {
            var sql = BackendConnectorFactory.GetDropTableSql(backendType, row.Nazivtabele!);
            if (!SqlPreviewWindow.Show(sql, owner: this)) return;

            Cursor = Cursors.Wait;
            try
            {
                ExecuteOnBackend(c => c.DropTable(row.Nazivtabele!));
                LogService.Sql(BackendConnectorFactory.GetDropTableSql(backendType, row.Nazivtabele!), backendType.ToString(), "Surplus");
            }
            catch (Exception ex)
            {
                LogService.Error("Surplus", "Error dropping", ex, null, backendType.ToString());
                MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
                return;
            }
            finally { Cursor = Cursors.Arrow; }

            RemoveTabela(row.Idtabele);
        }
        else
        {
            if (dgKolone.SelectedItem is not Kolonenove kol)
            {
                MyMsgBox.Show("Odaberi kolonu u donjoj listi.", icon: MessageBoxImage.Warning);
                return;
            }

            var sql = BackendConnectorFactory.GetDropColumnSql(backendType, row.Nazivtabele!, kol.Nazivkolone!);
            if (!SqlPreviewWindow.Show(sql, owner: this)) return;

            Cursor = Cursors.Wait;
            try
            {
                ExecuteOnBackend(c => c.DropColumn(row.Nazivtabele!, kol.Nazivkolone!));
                LogService.Sql(BackendConnectorFactory.GetDropColumnSql(backendType, row.Nazivtabele!, kol.Nazivkolone!), backendType.ToString(), "Surplus");
            }
            catch (Exception ex)
            {
                LogService.Error("Surplus", "Error dropping", ex, null, backendType.ToString());
                MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
                return;
            }
            finally { Cursor = Cursors.Arrow; }

            using var db = new BlueprintDbContext();
            var k = db.Kolonenoves.Find(kol.Idkolone);
            if (k != null) db.Kolonenoves.Remove(k);
            db.SaveChanges();

            if (!db.Kolonenoves.Any(x => x.Idtabele == row.Idtabele && x.Skriven != true))
                RemoveTabela(row.Idtabele);
        }

        Refresh();
    }

    // ── Izbriši sve ─────────────────────────────────────────────────────────

    private void BtnIzbrisiSve_Click(object sender, RoutedEventArgs e)
    {
        if (dgTabele.Items.Count == 0) return;
        var backendType = BackendConnectorFactory.DetectFromPath(AppState.BackendDatabasePath);

        // Generiši sve SQL naredbe i pokaži preview
        using var db0 = new BlueprintDbContext();
        var tabele0 = db0.Tabelenoves
            .Where(t => t.Idprograma == AppState.SelectedProgramId && t.Skriven != true)
            .ToList();

        var sb = new StringBuilder();
        foreach (var t in tabele0)
        {
            var kolone0 = db0.Kolonenoves.Where(k => k.Idtabele == t.Idtabele).ToList();
            foreach (var k in kolone0)
                sb.AppendLine(BackendConnectorFactory.GetDropColumnSql(backendType, t.Nazivtabele!, k.Nazivkolone!) + ";");
            if (t.Cijelatabela)
                sb.AppendLine(BackendConnectorFactory.GetDropTableSql(backendType, t.Nazivtabele!) + ";");
            sb.AppendLine();
        }

        if (!SqlPreviewWindow.Show(sb.ToString().TrimEnd(), owner: this)) return;

        Cursor = Cursors.Wait;
        var errors = new List<string>();

        try
        {
            using var connector = OpenConnector();
            using var db = new BlueprintDbContext();

            var tabele = db.Tabelenoves
                .Where(t => t.Idprograma == AppState.SelectedProgramId && t.Skriven != true)
                .ToList();

            foreach (var t in tabele)
            {
                // Prvo DROP kolone (CijelaTabela=False)
                var kolone = db.Kolonenoves.Where(k => k.Idtabele == t.Idtabele).ToList();
                foreach (var k in kolone)
                {
                    try
                    {
                        connector.DropColumn(t.Nazivtabele!, k.Nazivkolone!);
                        LogService.Sql(BackendConnectorFactory.GetDropColumnSql(backendType, t.Nazivtabele!, k.Nazivkolone!), backendType.ToString(), "Surplus");
                    }
                    catch (Exception ex) { errors.Add($"{t.Nazivtabele}.{k.Nazivkolone}: {ex.Message}"); }
                    db.Kolonenoves.Remove(k);
                }

                // Ako cijela tabela → DROP TABLE
                if (t.Cijelatabela)
                {
                    try
                    {
                        connector.DropTable(t.Nazivtabele!);
                        LogService.Sql(BackendConnectorFactory.GetDropTableSql(backendType, t.Nazivtabele!), backendType.ToString(), "Surplus");
                    }
                    catch (Exception ex) { errors.Add($"{t.Nazivtabele}: {ex.Message}"); }
                }

                db.Tabelenoves.Remove(t);
            }
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            LogService.Error("Surplus", "Error dropping all surplus entries", ex);
            errors.Add(ex.Message);
        }
        finally { Cursor = Cursors.Arrow; }

        if (errors.Count > 0)
            MyMsgBox.Show(string.Join("\n", errors), icon: MessageBoxImage.Warning);

        Refresh();
    }

    // ── Očisti listu ────────────────────────────────────────────────────────

    private void BtnOcistiListu_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MyMsgBox.Show(
            "Obrisati listu surplus unosa bez brisanja tabela i polja u backend bazi?",
            icon: MessageBoxImage.Question, buttons: MessageBoxButton.YesNo);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            using var db = new BlueprintDbContext();
            var ids = db.Tabelenoves
                .Where(t => t.Idprograma == AppState.SelectedProgramId)
                .Select(t => t.Idtabele)
                .ToList();
            db.Kolonenoves.RemoveRange(db.Kolonenoves.Where(k => ids.Contains(k.Idtabele)));
            db.Tabelenoves.RemoveRange(db.Tabelenoves.Where(t => t.Idprograma == AppState.SelectedProgramId));
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            LogService.Error("Surplus", "Error clearing surplus list", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }

        Refresh();
    }

    private void BtnZatvori_Click(object sender, RoutedEventArgs e) => Close();

    // ── Helpers ─────────────────────────────────────────────────────────────

    private void RemoveTabela(int idTabele)
    {
        try
        {
            using var db = new BlueprintDbContext();
            db.Kolonenoves.RemoveRange(db.Kolonenoves.Where(k => k.Idtabele == idTabele));
            var t = db.Tabelenoves.Find(idTabele);
            if (t != null) db.Tabelenoves.Remove(t);
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            LogService.Error("Surplus", "Error removing surplus entry", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void ExecuteOnBackend(Action<IBackendConnector> action)
    {
        using var connector = OpenConnector();
        action(connector);
    }

    private IBackendConnector OpenConnector()
    {
        var cs = AppState.BackendDatabasePath;
        var type = BackendConnectorFactory.DetectFromPath(cs);
        var connector = BackendConnectorFactory.Create(cs, type);
        connector.Open();
        return connector;
    }
}
