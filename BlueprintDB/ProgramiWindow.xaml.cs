using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Blueprint.App.Models;

namespace Blueprint.App;

public partial class ProgramiWindow : Window
{
    private Programi? _selected;

    public ProgramiWindow()
    {
        InitializeComponent();
        LoadData();
        LanguageService.TranslateWindow(this);
        WindowSettings.Restore("ProgramiWindow", this);
        Closing += (_, _) => WindowSettings.Save("ProgramiWindow", this);
    }

    private void LoadData()
    {
        try
        {
            using var db = new BlueprintDbContext();
            dgProgrami.ItemsSource = db.Programis
                .Where(p => p.Skriven != true)
                .OrderBy(p => p.Nazivprograma)
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error loading programs", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void dgProgrami_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dgProgrami.SelectedItem is Programi p)
        {
            _selected = p;
            txtNaziv.Text = p.Nazivprograma;
            txtVerzija.Text = p.Verzija;
        }
    }

    private void dgProgrami_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (dgProgrami.SelectedItem is Programi p)
            new TabeleWindow(p.Idprograma, p.Nazivprograma).Show();
    }

    private void BtnNovi_Click(object sender, RoutedEventArgs e)
    {
        _selected = null;
        dgProgrami.SelectedItem = null;
        txtNaziv.Clear();
        txtVerzija.Clear();
        txtNaziv.Focus();
    }

    private void BtnSacuvaj_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtNaziv.Text))
        {
            MyMsgBox.Show("MSG_NAZIV_PROGRAMA_PRAZAN", icon: MessageBoxImage.Warning);
            return;
        }

        try
        {
            using var db = new BlueprintDbContext();

            if (_selected == null)
            {
                db.Programis.Add(new Programi
                {
                    Nazivprograma = txtNaziv.Text.Trim(),
                    Verzija = txtVerzija.Text.Trim(),
                    Korisnik = Environment.UserName,
                    Datumupisa = DateTime.Now,
                    Skriven = false,
                    Vremenskipecat = (decimal)DateTime.Now.TimeOfDay.TotalSeconds
                });
            }
            else
            {
                var existing = db.Programis.Find(_selected.Idprograma);
                if (existing != null)
                {
                    existing.Nazivprograma = txtNaziv.Text.Trim();
                    existing.Verzija = txtVerzija.Text.Trim();
                }
            }

            db.SaveChanges();
            LoadData();
            BtnNovi_Click(sender, e);
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error saving program", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void BtnObrisi_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            MyMsgBox.Show("MSG_ODABERI_PROGRAM_BRISANJE", icon: MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            string.Format(LanguageService.T("MSG_BRISANJE_PROGRAMA"), _selected.Nazivprograma),
            LanguageService.T("MSG_POTVRDA_BRISANJA"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                using var db = new BlueprintDbContext();
                var existing = db.Programis.Find(_selected.Idprograma);
                if (existing != null)
                {
                    existing.Skriven = true;
                    db.SaveChanges();
                }
                LoadData();
                BtnNovi_Click(sender, e);
            }
            catch (Exception ex)
            {
                LogService.Error("CRUD", "Error deleting program", ex);
                MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
            }
        }
    }

    private void BtnTabele_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            MyMsgBox.Show("MSG_ODABERI_PROGRAM_TABELE", icon: MessageBoxImage.Warning);
            return;
        }
        new TabeleWindow(_selected.Idprograma, _selected.Nazivprograma).Show();
    }

    private void BtnZatvori_Click(object sender, RoutedEventArgs e) => Close();
}
