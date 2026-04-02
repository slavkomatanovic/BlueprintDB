using System.Windows;
using System.Windows.Controls;
using Blueprint.App.Models;

namespace Blueprint.App;

public partial class KoloneWindow : Window
{
    private Kolone? _selected;
    private readonly int _tabeleId;

    // AllowNull values stored in the database
    private static readonly string[] _nullValues = { "", "YES", "No" };

    public KoloneWindow(int tabeleId, string? tabelaNaziv)
    {
        InitializeComponent();
        _tabeleId = tabeleId;
        lblTabela.Text = tabelaNaziv ?? string.Empty;
        LoadTipPodatka();
        LoadAllowNull();
        LoadData();
        LanguageService.TranslateWindow(this);
        WindowSettings.Restore("KoloneWindow", this);
        Closing += (_, _) => WindowSettings.Save("KoloneWindow", this);
    }

    // ── Lookup loaders ──────────────────────────────────────────────────────

    private void LoadTipPodatka()
    {
        try
        {
            using var db = new BlueprintDbContext();
            var types = db.Tippodatkas
                .Where(t => t.Skriven != true)
                .Select(t => t.Tippodatka1)
                .OrderBy(t => t)
                .ToList();

            // Allow blank selection at the top
            types.Insert(0, "");
            cbTipPodatka.ItemsSource = types;
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error loading data types", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void LoadAllowNull()
    {
        cbAllowNull.ItemsSource = _nullValues;
        cbAllowNull.SelectedIndex = 0;
    }

    // ── Data ────────────────────────────────────────────────────────────────

    private void LoadData()
    {
        try
        {
            using var db = new BlueprintDbContext();
            dgKolone.ItemsSource = db.Kolones
                .Where(k => k.Idtabele == _tabeleId && k.Skriven != true)
                .OrderBy(k => k.Nazivkolone)
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error loading columns", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void dgKolone_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dgKolone.SelectedItem is not Kolone k) return;

        _selected = k;
        txtNazivKolone.Text  = k.Nazivkolone;
        cbTipPodatka.Text    = k.Tippodatka  ?? "";
        txtFieldsize.Text    = k.Fieldsize    ?? "";
        txtDefault.Text      = k.Default      ?? "";
        cbAllowNull.Text     = k.Allownull    ?? "";
        chkKey.IsChecked     = k.Key;
    }

    // ── Actions ─────────────────────────────────────────────────────────────

    private void BtnNovi_Click(object sender, RoutedEventArgs e)
    {
        _selected = null;
        dgKolone.SelectedItem = null;
        txtNazivKolone.Clear();
        cbTipPodatka.SelectedIndex  = 0;
        txtFieldsize.Clear();
        txtDefault.Clear();
        cbAllowNull.SelectedIndex   = 0;
        chkKey.IsChecked = false;
        txtNazivKolone.Focus();
    }

    private void BtnSacuvaj_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtNazivKolone.Text))
        {
            MyMsgBox.Show("MSG_NAZIV_KOLONE_PRAZAN", icon: MessageBoxImage.Warning);
            return;
        }

        var tipPodatka = cbTipPodatka.SelectedItem as string ?? cbTipPodatka.Text;
        var allowNull  = cbAllowNull.SelectedItem  as string ?? cbAllowNull.Text;

        try
        {
            using var db = new BlueprintDbContext();

            if (_selected == null)
            {
                db.Kolones.Add(new Kolone
                {
                    Idtabele    = _tabeleId,
                    Nazivkolone = txtNazivKolone.Text.Trim(),
                    Tippodatka  = tipPodatka.Trim(),
                    Fieldsize   = txtFieldsize.Text.Trim(),
                    Default     = txtDefault.Text.Trim(),
                    Allownull   = allowNull,
                    Key         = chkKey.IsChecked == true,
                    Korisnik    = Environment.UserName,
                    Datumupisa  = DateTime.Now,
                    Skriven     = false,
                    Vremenskipecat = (decimal)DateTime.Now.TimeOfDay.TotalSeconds
                });
            }
            else
            {
                var existing = db.Kolones.Find(_selected.Idkolone);
                if (existing != null)
                {
                    var oldName = existing.Nazivkolone;
                    existing.Nazivkolone = txtNazivKolone.Text.Trim();
                    existing.Tippodatka  = tipPodatka.Trim();
                    existing.Fieldsize   = txtFieldsize.Text.Trim();
                    existing.Default     = txtDefault.Text.Trim();
                    existing.Allownull   = allowNull;
                    existing.Key         = chkKey.IsChecked == true;

                    // If column was renamed, update all Relacije records that reference the old name
                    if (!string.Equals(oldName, existing.Nazivkolone, StringComparison.OrdinalIgnoreCase))
                    {
                        var table = db.Tabeles.Find(_tabeleId);
                        if (table != null)
                        {
                            var affected = db.Relacijes
                                .Where(r => r.Idprograma == table.Idprograma &&
                                            r.Polje      == oldName           &&
                                            r.Skriven    != true)
                                .ToList();
                            foreach (var rel in affected)
                                rel.Polje = existing.Nazivkolone;
                        }
                    }
                }
            }

            db.SaveChanges();
            LoadData();
            BtnNovi_Click(sender, e);
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error saving column", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void BtnObrisi_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            MyMsgBox.Show("MSG_ODABERI_KOLONU_BRISANJE", icon: MessageBoxImage.Warning);
            return;
        }

        // Block deletion if column is referenced in any relation
        try
        {
            using var dbCheck = new BlueprintDbContext();
            var table = dbCheck.Tabeles.Find(_tabeleId);
            if (table != null)
            {
                int relCount = dbCheck.Relacijes.Count(r =>
                    r.Idprograma == table.Idprograma &&
                    r.Polje      == _selected.Nazivkolone &&
                    r.Skriven    != true);
                if (relCount > 0)
                {
                    MyMsgBox.Show(
                        string.Format(LanguageService.T("MSG_KOLONA_POD_RELACIJOM"), _selected.Nazivkolone, relCount),
                        icon: MessageBoxImage.Warning);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error checking relations for column", ex);
        }

        var result = MessageBox.Show(
            string.Format(LanguageService.T("MSG_BRISANJE_KOLONE"), _selected.Nazivkolone),
            LanguageService.T("MSG_POTVRDA_BRISANJA"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                using var db = new BlueprintDbContext();
                var existing = db.Kolones.Find(_selected.Idkolone);
                if (existing != null) { existing.Skriven = true; db.SaveChanges(); }
                LoadData();
                BtnNovi_Click(sender, e);
            }
            catch (Exception ex)
            {
                LogService.Error("CRUD", "Error deleting column", ex);
                MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
            }
        }
    }

    private void BtnZatvori_Click(object sender, RoutedEventArgs e) => Close();
}
