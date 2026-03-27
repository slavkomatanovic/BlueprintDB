using System.IO;
using System.Windows;
using Blueprint.App.Backend;
using Blueprint.App.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace Blueprint.App;

public partial class MatrixImportWindow : Window
{
    private string _folder = "";
    private bool _running;

    // Parsed data (populated by Preview, consumed by Import)
    private List<MatrixProgram> _programs = [];
    private List<MatrixTabela>  _tabele   = [];
    private List<MatrixKolona>  _kolone   = [];

    public MatrixImportWindow()
    {
        InitializeComponent();
        LanguageService.TranslateWindow(this);
        WindowSettings.Restore("MatrixImportWindow", this);
        Closing += (_, _) => WindowSettings.Save("MatrixImportWindow", this);
    }

    // ── Browse ────────────────────────────────────────────────────────────────

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        // Open Programi.txt directly — we use its directory as the import folder
        var initDir = _folder.Length > 0 ? _folder : @"C:\mysoftware\matrix\src\tables";
        var dlg = new OpenFileDialog
        {
            Title            = "Select Programi.txt in the Matrix tables folder",
            Filter           = "Programi.txt|Programi.txt|Text files|*.txt",
            InitialDirectory = Directory.Exists(initDir) ? initDir : "",
            FileName         = "Programi.txt"
        };

        if (dlg.ShowDialog() != true) return;

        _folder = Path.GetDirectoryName(dlg.FileName) ?? "";
        txtFolder.Text = _folder;
        btnPreview.IsEnabled = File.Exists(Path.Combine(_folder, "Programi.txt")) &&
                               File.Exists(Path.Combine(_folder, "Tabele.txt"))   &&
                               File.Exists(Path.Combine(_folder, "Kolone.txt"));
        btnImport.IsEnabled = false;
        ResetPreview();

        if (!btnPreview.IsEnabled)
            Log("⚠  Folder does not contain Programi.txt / Tabele.txt / Kolone.txt");
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    private void BtnPreview_Click(object sender, RoutedEventArgs e)
    {
        ClearLog();
        try
        {
            ParseFiles();
            lblProgCount.Text = _programs.Count.ToString();
            lblTblCount.Text  = _tabele.Count.ToString();
            lblColCount.Text  = _kolone.Count.ToString();
            Log($"Found {_programs.Count} program(s), {_tabele.Count} table(s), {_kolone.Count} column(s).");
            Log("");
            foreach (var p in _programs)
                Log($"  {p.Naziv}  ({_tabele.Count(t => t.IdPrograma == p.Id)} tables)");
            btnImport.IsEnabled = _programs.Count > 0;
        }
        catch (Exception ex)
        {
            Log($"Error reading files: {ex.Message}");
            LogService.Error("MatrixImport", "Preview failed", ex);
        }
    }

    // ── Import ────────────────────────────────────────────────────────────────

    private async void BtnImport_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        _running = true;
        btnImport.IsEnabled  = false;
        btnPreview.IsEnabled = false;
        btnBrowse.IsEnabled  = false;
        btnClose.IsEnabled   = false;
        ClearLog();

        bool overwrite = rbOverwrite.IsChecked == true;

        int progAdded = 0, tblAdded = 0, colAdded = 0;
        int progSkipped = 0, tblSkipped = 0, colSkipped = 0;

        try
        {
            await Task.Run(() =>
            {
                using var db = new BlueprintDbContext();

                // Pre-load known ADO types so we can register missing ones
                var knownTypes = db.Tippodatkas
                    .Where(t => t.Skriven != true)
                    .Select(t => t.Tippodatka1!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // ── Step 1: Programs ──────────────────────────────────────────
                // Map old Matrix IDPrograma → new Blueprint programId
                var progIdMap = new Dictionary<int, int>();

                foreach (var mp in _programs)
                {
                    Dispatcher.Invoke(() => lblStatus.Text = $"Program: {mp.Naziv}…");

                    var existing = db.Programis.FirstOrDefault(p =>
                        p.Nazivprograma == mp.Naziv && p.Skriven != true);

                    int blueprintProgramId;

                    if (existing != null)
                    {
                        if (overwrite)
                        {
                            existing.Verzija    = mp.Verzija;
                            existing.Korisnik   = Environment.UserName;
                            existing.Datumupisa = DateTime.Now;
                            db.SaveChanges();
                        }
                        blueprintProgramId = existing.Idprograma;
                        progSkipped++;
                        Dispatcher.Invoke(() => Log($"  [SKIP]  Program: {mp.Naziv} (already exists)"));
                    }
                    else
                    {
                        var newProg = new Programi
                        {
                            Nazivprograma  = mp.Naziv,
                            Verzija        = mp.Verzija,
                            Korisnik       = Environment.UserName,
                            Datumupisa     = DateTime.Now,
                            Skriven        = false,
                            Vremenskipecat = (decimal)DateTime.Now.TimeOfDay.TotalSeconds
                        };
                        db.Programis.Add(newProg);
                        db.SaveChanges();
                        blueprintProgramId = newProg.Idprograma;
                        progAdded++;
                        Dispatcher.Invoke(() => Log($"  [ADD]   Program: {mp.Naziv}"));
                    }

                    progIdMap[mp.Id] = blueprintProgramId;
                }

                // ── Step 2: Tables ────────────────────────────────────────────
                var tblIdMap = new Dictionary<int, int>();

                foreach (var mt in _tabele)
                {
                    if (!progIdMap.TryGetValue(mt.IdPrograma, out int blueprintProgramId))
                        continue; // orphan table — skip

                    var existing = db.Tabeles.FirstOrDefault(t =>
                        t.Idprograma  == blueprintProgramId &&
                        t.Nazivtabele == mt.Naziv &&
                        t.Skriven     != true);

                    int blueprintTableId;

                    if (existing != null)
                    {
                        blueprintTableId = existing.Idtabele;
                        tblSkipped++;
                    }
                    else
                    {
                        var newTbl = new Tabele
                        {
                            Idprograma     = blueprintProgramId,
                            Nazivtabele    = mt.Naziv,
                            Verzija        = mt.Verzija,
                            Korisnik       = Environment.UserName,
                            Datumupisa     = DateTime.Now,
                            Skriven        = false,
                            Vremenskipecat = (decimal)DateTime.Now.TimeOfDay.TotalSeconds
                        };
                        db.Tabeles.Add(newTbl);
                        db.SaveChanges();
                        blueprintTableId = newTbl.Idtabele;
                        tblAdded++;
                    }

                    tblIdMap[mt.Id] = blueprintTableId;
                }

                // ── Step 3: Columns ───────────────────────────────────────────
                foreach (var mk in _kolone)
                {
                    if (!tblIdMap.TryGetValue(mk.IdTabele, out int blueprintTableId))
                        continue; // orphan column — skip

                    var existing = db.Kolones.FirstOrDefault(k =>
                        k.Idtabele    == blueprintTableId &&
                        k.Nazivkolone == mk.Naziv &&
                        k.Skriven     != true);

                    if (existing != null)
                    {
                        if (overwrite)
                        {
                            existing.Tippodatka     = mk.TipPodatka;
                            existing.Fieldsize      = mk.FieldSize;
                            existing.Allownull      = mk.AllowNull;
                            existing.Key            = mk.Key;
                            existing.Default        = mk.Default;
                            existing.Indexed        = mk.Indexed;
                            existing.Korisnik       = Environment.UserName;
                            existing.Datumupisa     = DateTime.Now;
                        }
                        colSkipped++;
                    }
                    else
                    {
                        db.Kolones.Add(new Kolone
                        {
                            Idtabele       = blueprintTableId,
                            Nazivkolone    = mk.Naziv,
                            Tippodatka     = mk.TipPodatka,
                            Fieldsize      = mk.FieldSize,
                            Allownull      = mk.AllowNull,
                            Key            = mk.Key,
                            Default        = mk.Default,
                            Indexed        = mk.Indexed,
                            Korisnik       = Environment.UserName,
                            Datumupisa     = DateTime.Now,
                            Skriven        = false,
                            Vremenskipecat = (decimal)DateTime.Now.TimeOfDay.TotalSeconds
                        });
                        colAdded++;

                        // Register the ADO type in the tippodatka lookup table if not already there
                        if (!string.IsNullOrEmpty(mk.TipPodatka) && knownTypes.Add(mk.TipPodatka))
                        {
                            db.Database.ExecuteSqlRaw(
                                "INSERT INTO tippodatka (tippodatka, korisnik, datumupisa, skriven, vremenskipecat) VALUES ({0},{1},{2},{3},{4})",
                                mk.TipPodatka,
                                Environment.UserName,
                                DateTime.Now,
                                false,
                                (decimal)DateTime.Now.TimeOfDay.TotalSeconds);
                        }
                    }
                }
                db.SaveChanges();
            });

            Log("");
            Log($"✓  Import complete.");
            Log($"   Programs: {progAdded} added, {progSkipped} skipped");
            Log($"   Tables:   {tblAdded} added, {tblSkipped} skipped");
            Log($"   Columns:  {colAdded} added, {colSkipped} skipped");
            lblStatus.Text = $"Done — {progAdded} programs, {tblAdded} tables, {colAdded} columns added.";
        }
        catch (Exception ex)
        {
            Log($"✗  Import failed: {ex.Message}");
            LogService.Error("MatrixImport", "Import failed", ex);
            lblStatus.Text = "Import failed — see log.";
        }
        finally
        {
            _running             = false;
            btnImport.IsEnabled  = true;
            btnPreview.IsEnabled = true;
            btnBrowse.IsEnabled  = true;
            btnClose.IsEnabled   = true;
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // ── File parsing ──────────────────────────────────────────────────────────

    private void ParseFiles()
    {
        _programs = ParseProgrami(Path.Combine(_folder, "Programi.txt"));
        _tabele   = ParseTabele(Path.Combine(_folder, "Tabele.txt"));
        _kolone   = ParseKolone(Path.Combine(_folder, "Kolone.txt"));
    }

    private static List<MatrixProgram> ParseProgrami(string path)
    {
        var list = new List<MatrixProgram>();
        bool header = true;
        foreach (var line in File.ReadLines(path))
        {
            if (header) { header = false; continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;
            var f = line.Split('\t');
            if (f.Length < 1) continue;
            if (!int.TryParse(f[0].Trim(), out int id)) continue;
            list.Add(new MatrixProgram(
                id,
                f.Length > 1 ? f[1].Trim() : "",
                f.Length > 2 ? f[2].Trim() : null));
        }
        return list;
    }

    private static List<MatrixTabela> ParseTabele(string path)
    {
        var list = new List<MatrixTabela>();
        bool header = true;
        foreach (var line in File.ReadLines(path))
        {
            if (header) { header = false; continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;
            var f = line.Split('\t');
            if (f.Length < 2) continue;
            if (!int.TryParse(f[0].Trim(), out int id))   continue;
            if (!int.TryParse(f[1].Trim(), out int progId)) continue;
            list.Add(new MatrixTabela(
                id,
                progId,
                f.Length > 2 ? f[2].Trim() : "",
                f.Length > 3 ? f[3].Trim() : null));
        }
        return list;
    }

    private static List<MatrixKolona> ParseKolone(string path)
    {
        var list = new List<MatrixKolona>();
        bool header = true;
        foreach (var line in File.ReadLines(path))
        {
            if (header) { header = false; continue; }
            if (string.IsNullOrWhiteSpace(line)) continue;
            var f = line.Split('\t');
            if (f.Length < 2) continue;
            if (!int.TryParse(f[0].Trim(), out int id))    continue;
            if (!int.TryParse(f[1].Trim(), out int tblId)) continue;

            var naziv     = f.Length > 2 ? f[2].Trim() : "";
            var tip       = f.Length > 3 ? f[3].Trim() : "";
            var defVal    = f.Length > 4 ? NullIfEmpty(f[4].Trim()) : null;
            var fieldSize = f.Length > 5 ? NullIfEmpty(f[5].Trim()) : null;
            var allowNull = f.Length > 6 ? NormalizeAllowNull(f[6].Trim()) : "YES";
            var indexed   = f.Length > 7 ? NullIfEmpty(f[7].Trim()) : null;
            var key       = f.Length > 8 && f[8].Trim().Equals("True", StringComparison.OrdinalIgnoreCase);

            // Ensure tip is always a valid ADO type
            var adoTip = string.IsNullOrEmpty(tip) ? "adVariant" : tip;

            list.Add(new MatrixKolona(id, tblId, naziv, adoTip, defVal, fieldSize, allowNull, indexed, key));
        }
        return list;
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

    private static string NormalizeAllowNull(string s)
    {
        if (string.IsNullOrEmpty(s)) return "YES";
        return s.Equals("yes", StringComparison.OrdinalIgnoreCase) ? "YES" : s;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ResetPreview()
    {
        lblProgCount.Text = "—";
        lblTblCount.Text  = "—";
        lblColCount.Text  = "—";
        _programs.Clear();
        _tabele.Clear();
        _kolone.Clear();
    }

    private void ClearLog() => txtLog.Text = "";

    private void Log(string msg)
    {
        Dispatcher.Invoke(() =>
        {
            txtLog.Text += msg + "\n";
            logScroll.ScrollToEnd();
        });
    }
}

// ── Data transfer records (internal to this assembly) ────────────────────────

internal record MatrixProgram(int Id, string Naziv, string? Verzija);
internal record MatrixTabela(int Id, int IdPrograma, string Naziv, string? Verzija);
internal record MatrixKolona(int Id, int IdTabele, string Naziv, string TipPodatka,
    string? Default, string? FieldSize, string AllowNull, string? Indexed, bool Key);
