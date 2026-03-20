using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Blueprint.App.Models;
using Microsoft.Data.Sqlite;

namespace Blueprint.App;

public partial class TabeleWindow : Window
{
    private Tabele? _selected;
    private int _currentProgramId;

    public TabeleWindow()
    {
        InitializeComponent();
        LoadProgrami();
        LanguageService.TranslateWindow(this);
        btnImport.ToolTip = LanguageService.T("TIP_CMD_IMPORT");
        WindowSettings.Restore("TabeleWindow", this);
        Closing += (_, _) => WindowSettings.Save("TabeleWindow", this);
    }

    public TabeleWindow(int programId, string? programNaziv) : this()
    {
        cbProgrami.SelectedValue = programId;
    }

    private void LoadProgrami()
    {
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
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void LoadData()
    {
        if (_currentProgramId <= 0) { dgTabele.ItemsSource = null; return; }

        try
        {
            using var db = new BlueprintDbContext();
            dgTabele.ItemsSource = db.Tabeles
                .Where(t => t.Idprograma == _currentProgramId && t.Skriven != true)
                .OrderBy(t => t.Nazivtabele)
                .ToList();
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error loading tables", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void cbProgrami_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cbProgrami.SelectedValue is int id)
        {
            _currentProgramId = id;
            LoadData();
        }
    }

    private void dgTabele_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dgTabele.SelectedItem is Tabele t)
        {
            _selected = t;
            txtNazivTabele.Text = t.Nazivtabele;
            txtVerzija.Text = t.Verzija;
            txtSid.Text = t.Sid.ToString();
        }
    }

    private void dgTabele_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (dgTabele.SelectedItem is Tabele t)
            new KoloneWindow(t.Idtabele, t.Nazivtabele).Show();
    }

    private void BtnNovi_Click(object sender, RoutedEventArgs e)
    {
        _selected = null;
        dgTabele.SelectedItem = null;
        txtNazivTabele.Clear();
        txtVerzija.Clear();
        txtSid.Clear();
        txtNazivTabele.Focus();
    }

    private void BtnSacuvaj_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProgramId <= 0)
        {
            MyMsgBox.Show("MSG_ODABERI_PROGRAM", icon: MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(txtNazivTabele.Text))
        {
            MyMsgBox.Show("MSG_NAZIV_TABELE_PRAZAN", icon: MessageBoxImage.Warning);
            return;
        }

        _ = int.TryParse(txtSid.Text, out int sid);

        try
        {
            using var db = new BlueprintDbContext();

            if (_selected == null)
            {
                db.Tabeles.Add(new Tabele
                {
                    Idprograma = _currentProgramId,
                    Nazivtabele = txtNazivTabele.Text.Trim(),
                    Verzija = txtVerzija.Text.Trim(),
                    Sid = sid,
                    Korisnik = Environment.UserName,
                    Datumupisa = DateTime.Now,
                    Skriven = false,
                    Vremenskipecat = (decimal)DateTime.Now.TimeOfDay.TotalSeconds
                });
            }
            else
            {
                var existing = db.Tabeles.Find(_selected.Idtabele);
                if (existing != null)
                {
                    existing.Nazivtabele = txtNazivTabele.Text.Trim();
                    existing.Verzija = txtVerzija.Text.Trim();
                    existing.Sid = sid;
                }
            }

            db.SaveChanges();
            LoadData();
            BtnNovi_Click(sender, e);
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error saving table", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void BtnObrisi_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            MyMsgBox.Show("MSG_ODABERI_TABELU_BRISANJE", icon: MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            string.Format(LanguageService.T("MSG_BRISANJE_TABELE"), _selected.Nazivtabele),
            LanguageService.T("MSG_POTVRDA_BRISANJA"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                using var db = new BlueprintDbContext();
                var existing = db.Tabeles.Find(_selected.Idtabele);
                if (existing != null) { existing.Skriven = true; db.SaveChanges(); }
                LoadData();
                BtnNovi_Click(sender, e);
            }
            catch (Exception ex)
            {
                LogService.Error("CRUD", "Error deleting table", ex);
                MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
            }
        }
    }

    private void BtnKolone_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null)
        {
            MyMsgBox.Show("MSG_ODABERI_TABELU_KOLONE", icon: MessageBoxImage.Warning);
            return;
        }
        new KoloneWindow(_selected.Idtabele, _selected.Nazivtabele).Show();
    }

    private void BtnZatvori_Click(object sender, RoutedEventArgs e) => Close();

    // ── Import ───────────────────────────────────────────────────────────────

    private void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProgramId <= 0)
        {
            MyMsgBox.Show("MSG_ODABERI_PROGRAM", icon: MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrEmpty(AppState.BackendDatabasePath))
        {
            MyMsgBox.Show("MSG_ODABERI_BAZU", icon: MessageBoxImage.Warning);
            return;
        }

        try
        {
            var schemaTables = ReadSqliteSchema(AppState.BackendDatabasePath);
            using var db = new BlueprintDbContext();

            foreach (var (tableName, columns) in schemaTables)
            {
                var existing = db.Tabeles.FirstOrDefault(t =>
                    t.Idprograma == _currentProgramId &&
                    t.Nazivtabele == tableName &&
                    t.Skriven != true);

                int tableId;
                if (existing == null)
                {
                    var newTable = new Tabele
                    {
                        Idprograma  = _currentProgramId,
                        Nazivtabele = tableName,
                        Korisnik    = Environment.UserName,
                        Datumupisa  = DateTime.Now,
                        Skriven     = false,
                        Vremenskipecat = (decimal)DateTime.Now.TimeOfDay.TotalSeconds
                    };
                    db.Tabeles.Add(newTable);
                    db.SaveChanges();
                    tableId = newTable.Idtabele;
                }
                else
                {
                    tableId = existing.Idtabele;
                }

                foreach (var col in columns)
                {
                    var existingCol = db.Kolones.FirstOrDefault(k =>
                        k.Idtabele == tableId &&
                        k.Nazivkolone == col.Name &&
                        k.Skriven != true);

                    if (existingCol == null)
                    {
                        db.Kolones.Add(new Kolone
                        {
                            Idtabele   = tableId,
                            Nazivkolone = col.Name,
                            Tippodatka  = col.SqlType,
                            Fieldsize   = col.MaxLength > 0 ? col.MaxLength.ToString() : null,
                            Allownull   = col.NotNull ? "No" : "YES",
                            Key         = col.PrimaryKey,
                            Korisnik    = Environment.UserName,
                            Datumupisa  = DateTime.Now,
                            Skriven     = false,
                            Vremenskipecat = (decimal)DateTime.Now.TimeOfDay.TotalSeconds
                        });
                    }
                }
            }

            db.SaveChanges();
            LoadData();
            MessageBox.Show(LanguageService.T("MSG_IMPORT_DONE"),
                            LanguageService.T("FORM_TABELE"),
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", $"Error in {this.GetType().Name}", ex);
            MessageBox.Show(ex.Message, "Import error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private record ColInfo(string Name, string SqlType, bool NotNull, bool PrimaryKey, int MaxLength);

    private static List<(string Table, List<ColInfo> Columns)> ReadSqliteSchema(string dbPath)
    {
        var result = new List<(string, List<ColInfo>)>();
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly }.ToString();

        using var conn = new SqliteConnection(cs);
        conn.Open();

        var tables = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tables.Add(r.GetString(0));
        }

        foreach (var tbl in tables)
        {
            var cols = new List<ColInfo>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{tbl.Replace("\"", "\"\"")}\")";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                // cid | name | type | notnull | dflt_value | pk
                var rawType = r.GetString(2);
                var m       = Regex.Match(rawType, @"\((\d+)\)");
                int maxLen  = m.Success ? int.Parse(m.Groups[1].Value) : 0;
                var baseType = m.Success ? rawType[..rawType.IndexOf('(')] : rawType;

                cols.Add(new ColInfo(
                    Name:       r.GetString(1),
                    SqlType:    baseType.Trim().ToUpperInvariant(),
                    NotNull:    r.GetInt32(3) == 1,
                    PrimaryKey: r.GetInt32(5) > 0,
                    MaxLength:  maxLen));
            }
            result.Add((tbl, cols));
        }

        return result;
    }

    // ── Reset ────────────────────────────────────────────────────────────────

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        if (_currentProgramId <= 0)
        {
            MyMsgBox.Show("MSG_ODABERI_PROGRAM", icon: MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            LanguageService.T("MSG_RESET_CONFIRM"),
            LanguageService.T("MSG_POTVRDA_BRISANJA"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            using var db = new BlueprintDbContext();

            var tableIds = db.Tabeles
                .Where(t => t.Idprograma == _currentProgramId)
                .Select(t => t.Idtabele).ToList();

            db.Kolones.RemoveRange(db.Kolones.Where(k => tableIds.Contains(k.Idtabele)));
            db.Tabeles.RemoveRange(db.Tabeles.Where(t => t.Idprograma == _currentProgramId));

            var noveIds = db.Tabelenoves
                .Where(t => t.Idprograma == _currentProgramId)
                .Select(t => t.Idtabele).ToList();

            db.Kolonenoves.RemoveRange(db.Kolonenoves.Where(k => noveIds.Contains(k.Idtabele)));
            db.Tabelenoves.RemoveRange(db.Tabelenoves.Where(t => t.Idprograma == _currentProgramId));
            db.Relacijes.RemoveRange(db.Relacijes.Where(r => r.Idprograma == _currentProgramId));

            db.SaveChanges();
            LoadData();
            BtnNovi_Click(sender, e);
        }
        catch (Exception ex)
        {
            LogService.Error("CRUD", "Error resetting program data", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }
}
