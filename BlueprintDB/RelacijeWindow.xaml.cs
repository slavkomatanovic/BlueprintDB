using System.Windows;
using System.Windows.Controls;
using Blueprint.App.Models;

namespace Blueprint.App;

public partial class RelacijeWindow : Window
{
    private int _programId;
    private Relacije? _current;

    public RelacijeWindow()
    {
        InitializeComponent();
        _programId = AppState.SelectedProgramId;

        try
        {
            using var db = new BlueprintDbContext();
            cbProgrami.ItemsSource = db.Programis
                .Where(p => p.Skriven != true)
                .OrderBy(p => p.Nazivprograma)
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error loading programs", ex);
        }

        if (_programId > 0)
            cbProgrami.SelectedValue = _programId;

        LanguageService.TranslateWindow(this);
        WindowSettings.Restore("RelacijeWindow", this);
        Closing += (_, _) => WindowSettings.Save("RelacijeWindow", this);
    }

    private void LoadTabele()
    {
        try
        {
            using var db = new BlueprintDbContext();
            var tabele = db.Tabeles
                .Where(t => t.Idprograma == _programId && t.Skriven != true)
                .OrderBy(t => t.Nazivtabele)
                .Select(t => t.Nazivtabele)
                .ToList();

            cbTabelaL.ItemsSource = tabele;
            cbTabelaD.ItemsSource = tabele;
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error loading tables", ex);
        }
    }

    private void LoadGrid()
    {
        try
        {
            using var db = new BlueprintDbContext();
            dgRelacije.ItemsSource = db.Relacijes
                .Where(r => r.Idprograma == _programId && r.Skriven != true)
                .OrderBy(r => r.Tabelal).ThenBy(r => r.Tabelad)
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error loading relations", ex);
        }
    }

    private void LoadPolja(string? tabelaNaziv)
    {
        if (string.IsNullOrEmpty(tabelaNaziv)) { cbPolje.ItemsSource = null; return; }
        try
        {
            using var db = new BlueprintDbContext();
            var tabela = db.Tabeles.FirstOrDefault(t => t.Idprograma == _programId && t.Nazivtabele == tabelaNaziv);
            if (tabela == null) { cbPolje.ItemsSource = null; return; }
            cbPolje.ItemsSource = db.Kolones
                .Where(k => k.Idtabele == tabela.Idtabele && k.Skriven != true)
                .OrderBy(k => k.Nazivkolone)
                .Select(k => k.Nazivkolone)
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error loading fields", ex);
        }
    }

    private void CbProgrami_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cbProgrami.SelectedValue is int id)
        {
            _programId = id;
            AppState.SaveSelectedProgram(id);
            LoadTabele();
            LoadGrid();
            ClearForm();
        }
    }

    private void CbTabelaL_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadPolja(cbTabelaL.SelectedItem as string);
        AutoFillNaziv();
    }

    private void AutoFillNaziv()
    {
        var l = cbTabelaL.SelectedItem as string ?? "";
        var d = cbTabelaD.SelectedItem as string ?? "";
        if (!string.IsNullOrEmpty(l) && !string.IsNullOrEmpty(d))
            txtNazivRelacije.Text = l + d;
    }

    private void DgRelacije_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dgRelacije.SelectedItem is not Relacije r) { ClearForm(); return; }
        _current = r;
        cbTabelaL.SelectedItem        = r.Tabelal;
        cbTabelaD.SelectedItem        = r.Tabelad;
        LoadPolja(r.Tabelal);
        cbPolje.SelectedItem          = r.Polje;
        txtNazivRelacije.Text         = r.Nazivrelacije;
        txtVerzija.Text               = r.Verzija;
        chkCascade.IsChecked          = r.Updatedeletecascade;
    }

    private void ClearForm()
    {
        _current = null;
        cbTabelaL.SelectedIndex = -1;
        cbTabelaD.SelectedIndex = -1;
        cbPolje.ItemsSource     = null;
        txtNazivRelacije.Text   = "";
        txtVerzija.Text         = "";
        chkCascade.IsChecked    = false;
        dgRelacije.SelectedItem = null;
    }

    private void BtnNovi_Click(object sender, RoutedEventArgs e) => ClearForm();

    private void BtnSacuvaj_Click(object sender, RoutedEventArgs e)
    {
        if (cbTabelaL.SelectedItem is not string tL || cbTabelaD.SelectedItem is not string tD)
        {
            MyMsgBox.Show("MSG_ODABERI_TABELU", icon: MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var db = new BlueprintDbContext();
            if (_current == null)
            {
                db.Relacijes.Add(new Relacije
                {
                    Idprograma          = _programId,
                    Tabelal             = tL,
                    Tabelad             = tD,
                    Polje               = cbPolje.SelectedItem as string,
                    Nazivrelacije       = txtNazivRelacije.Text.Trim(),
                    Updatedeletecascade = chkCascade.IsChecked == true,
                    Verzija             = txtVerzija.Text.Trim(),
                    Korisnik            = Environment.UserName,
                    Datumupisa          = DateTime.Now,
                    Skriven             = false
                });
            }
            else
            {
                var rec = db.Relacijes.Find(_current.Idrelacije);
                if (rec != null)
                {
                    rec.Tabelal             = tL;
                    rec.Tabelad             = tD;
                    rec.Polje               = cbPolje.SelectedItem as string;
                    rec.Nazivrelacije       = txtNazivRelacije.Text.Trim();
                    rec.Updatedeletecascade = chkCascade.IsChecked == true;
                    rec.Verzija             = txtVerzija.Text.Trim();
                    rec.Korisnik            = Environment.UserName;
                    rec.Datumupisa          = DateTime.Now;
                }
            }
            db.SaveChanges();
            LogService.Info("CRUD", $"Relation saved: {tL} → {tD}");
            LoadGrid();
            ClearForm();
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error saving relation", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void BtnObrisi_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        if (MyMsgBox.Show("MSG_POTVRDA_BRISANJA", icon: MessageBoxImage.Warning,
                buttons: MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try
        {
            using var db = new BlueprintDbContext();
            var rec = db.Relacijes.Find(_current.Idrelacije);
            if (rec != null) db.Relacijes.Remove(rec);
            db.SaveChanges();
            LogService.Info("CRUD", $"Relation deleted: {_current.Tabelal} → {_current.Tabelad}");
            LoadGrid();
            ClearForm();
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error deleting relation", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void BtnSredi_Click(object sender, RoutedEventArgs e)
    {
        // Auto-popuni NazivRelacije = TabelaL + TabelaD za sve relacije ovog programa
        try
        {
            using var db = new BlueprintDbContext();
            var sve = db.Relacijes.Where(r => r.Idprograma == _programId).ToList();
            foreach (var r in sve.Where(r => !string.IsNullOrEmpty(r.Tabelal) && !string.IsNullOrEmpty(r.Tabelad)))
                r.Nazivrelacije = r.Tabelal + r.Tabelad;
            db.SaveChanges();
            LoadGrid();
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error auto-naming relations", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void BtnZatvori_Click(object sender, RoutedEventArgs e) => Close();
}
