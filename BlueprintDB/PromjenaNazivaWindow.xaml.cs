using System.Windows;
using System.Windows.Controls;
using Blueprint.App.Models;

namespace Blueprint.App;

public partial class PromjenaNazivaWindow : Window
{
    private int _programId;
    private Promjenanazivatabela? _current;

    public PromjenaNazivaWindow()
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
        WindowSettings.Restore("PromjenaNazivaWindow", this);
        Closing += (_, _) => WindowSettings.Save("PromjenaNazivaWindow", this);
    }

    private void LoadTabele()
    {
        try
        {
            using var db = new BlueprintDbContext();
            cbStariNaziv.ItemsSource = db.Tabeles
                .Where(t => t.Idprograma == _programId && t.Skriven != true)
                .OrderBy(t => t.Nazivtabele)
                .Select(t => t.Nazivtabele)
                .ToList();
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
            dgPromjena.ItemsSource = db.Promjenanazivatabelas
                .Where(p => p.Idprograma == _programId && p.Skriven != true)
                .OrderBy(p => p.Starinazivtabele)
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error loading table renames", ex);
        }
    }

    private void CbProgrami_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cbProgrami.SelectedValue is int id)
        {
            _programId = id;
            LoadTabele();
            LoadGrid();
            ClearForm();
        }
    }

    private void DgPromjena_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dgPromjena.SelectedItem is not Promjenanazivatabela p) { ClearForm(); return; }
        _current = p;
        cbStariNaziv.Text = p.Starinazivtabele ?? "";
        txtNoviNaziv.Text = p.Novinazivtabele  ?? "";
        txtVerzija.Text   = p.Verzija           ?? "";
    }

    private void ClearForm()
    {
        _current            = null;
        cbStariNaziv.Text   = "";
        txtNoviNaziv.Text   = "";
        txtVerzija.Text     = "";
        dgPromjena.SelectedItem = null;
    }

    private void BtnNovi_Click(object sender, RoutedEventArgs e) => ClearForm();

    private void BtnSacuvaj_Click(object sender, RoutedEventArgs e)
    {
        var stari = cbStariNaziv.Text.Trim();
        var novi  = txtNoviNaziv.Text.Trim();

        if (string.IsNullOrEmpty(stari) || string.IsNullOrEmpty(novi))
        {
            MyMsgBox.Show("MSG_POPUNI_POLJA", icon: MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var db = new BlueprintDbContext();
            if (_current == null)
            {
                db.Promjenanazivatabelas.Add(new Promjenanazivatabela
                {
                    Idprograma       = _programId,
                    Starinazivtabele = stari,
                    Novinazivtabele  = novi,
                    Verzija          = txtVerzija.Text.Trim(),
                    Korisnik         = Environment.UserName,
                    Datumupisa       = DateTime.Now,
                    Skriven          = false
                });
            }
            else
            {
                var rec = db.Promjenanazivatabelas.Find(_current.Id);
                if (rec != null)
                {
                    rec.Starinazivtabele = stari;
                    rec.Novinazivtabele  = novi;
                    rec.Verzija          = txtVerzija.Text.Trim();
                    rec.Korisnik         = Environment.UserName;
                    rec.Datumupisa       = DateTime.Now;
                }
            }
            db.SaveChanges();
            LogService.Info("CRUD", $"Table rename saved: {stari} → {novi}");
            LoadGrid();
            ClearForm();
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error saving table rename", ex);
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
            var rec = db.Promjenanazivatabelas.Find(_current.Id);
            if (rec != null) db.Promjenanazivatabelas.Remove(rec);
            db.SaveChanges();
            LogService.Info("CRUD", $"Table rename deleted: {_current.Starinazivtabele}");
            LoadGrid();
            ClearForm();
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error deleting table rename", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void BtnZatvori_Click(object sender, RoutedEventArgs e) => Close();
}
