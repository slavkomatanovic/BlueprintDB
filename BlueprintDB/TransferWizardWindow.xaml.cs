using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Blueprint.App.Backend;
using Blueprint.App.Models;
using Microsoft.Win32;

namespace Blueprint.App;

public partial class TransferWizardWindow : Window
{
    private int _currentStep = 1;
    private bool _transferStarted;
    private bool _transferDone;

    public TransferWizardWindow()
    {
        InitializeComponent();

        try
        {
            using var db = new BlueprintDbContext();
            cbProgram.ItemsSource = db.Programis
                .Where(p => p.Skriven != true)
                .OrderBy(p => p.Nazivprograma)
                .ToList();
            if (AppState.SelectedProgramId > 0)
                cbProgram.SelectedValue = AppState.SelectedProgramId;
        }
        catch (Exception ex)
        {
            LogService.Error("TransferWizard", "Failed to load programs", ex);
            MessageBox.Show(ex.Message, "Blueprint", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        var types = Enum.GetNames<BackendType>();
        cbSrcType.ItemsSource = types;
        cbTgtType.ItemsSource = types;
        cbSrcType.SelectedIndex = 0;
        cbTgtType.SelectedIndex = 0;
        WindowSettings.Restore("TransferWizardWindow", this);
        Closing += (_, _) => WindowSettings.Save("TransferWizardWindow", this);
    }

    // ── Sidebar ──────────────────────────────────────────────────────────────

    private void UpdateSidebar()
    {
        dot1.Fill = B(_currentStep > 1 ? "#10B981" : "#0EA5E9");
        dot2.Fill = B(_currentStep > 2 ? "#10B981" : _currentStep == 2 ? "#0EA5E9" : "#4B5563");
        dot3.Fill = B(_currentStep > 3 ? "#10B981" : _currentStep == 3 ? "#0EA5E9" : "#4B5563");
        dot4.Fill = B(_currentStep == 4 ? "#0EA5E9" : "#4B5563");

        lbl1.Foreground = B("White");
        lbl2.Foreground = B(_currentStep >= 2 ? "White" : "#7DD3FC");
        lbl3.Foreground = B(_currentStep >= 3 ? "White" : "#7DD3FC");
        lbl4.Foreground = B(_currentStep >= 4 ? "White" : "#7DD3FC");
    }

    private static SolidColorBrush B(string color)
        => new((Color)ColorConverter.ConvertFromString(color));

    // ── Navigation ────────────────────────────────────────────────────────────

    private void ShowStep(int step)
    {
        page1.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        page2.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        page3.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        page4.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;
        _currentStep = step;
        UpdateSidebar();
        btnBack.IsEnabled = step > 1 && !_transferStarted;
        btnNext.Content   = step == 4 && _transferDone ? "Finish" : "Next →";
        btnNext.IsEnabled = !(step == 4 && _transferStarted && !_transferDone);
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1) ShowStep(_currentStep - 1);
    }

    private async void BtnNext_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            switch (_currentStep)
            {
                case 1:
                    if (cbProgram.SelectedValue is not int)
                    {
                        MessageBox.Show("Please select a program.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    ShowStep(2);
                    break;

                case 2:
                    if (string.IsNullOrWhiteSpace(GetCs(cbSrcType, txtSrcPath, txtSrcFolder, txtSrcCs)))
                    {
                        MessageBox.Show("Please specify the source database.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    ShowStep(3);
                    break;

                case 3:
                    if (string.IsNullOrWhiteSpace(GetCs(cbTgtType, txtTgtPath, txtTgtFolder, txtTgtCs)))
                    {
                        MessageBox.Show("Please specify the target database.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    ShowStep(4);
                    await RunTransferAsync();
                    break;

                case 4:
                    Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("TransferWizard", "Unexpected error in wizard navigation", ex);
            MessageBox.Show(ex.Message, "Blueprint", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

    // ── Backend type toggles ─────────────────────────────────────────────────

    private void CbSrcType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Toggle(cbSrcType, lblSrcPath, rowSrcPath, lblSrcFolder, rowSrcFolder, lblSrcCs, srcCsGrid);
        UpdateHint(cbSrcType, txtSrcCs, hintSrcCs);
    }

    private void CbTgtType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        Toggle(cbTgtType, lblTgtPath, rowTgtPath, lblTgtFolder, rowTgtFolder, lblTgtCs, tgtCsGrid);
        UpdateHint(cbTgtType, txtTgtCs, hintTgtCs);
    }

    private static void Toggle(ComboBox cb,
        TextBlock lblPath, Grid rowPath,
        TextBlock lblFolder, Grid rowFolder,
        TextBlock lblCs, UIElement csGrid)
    {
        if (cb?.SelectedItem is null) return;
        var sel = cb.SelectedItem.ToString();
        bool usesCs  = sel is nameof(BackendType.MySQL) or nameof(BackendType.MariaDB) or
                       nameof(BackendType.SqlServer) or nameof(BackendType.PostgreSQL) or
                       nameof(BackendType.Firebird)  or nameof(BackendType.DB2) or nameof(BackendType.Oracle);
        bool isFolder = sel == nameof(BackendType.DBase);

        var fileV   = !usesCs && !isFolder ? Visibility.Visible : Visibility.Collapsed;
        var folderV = isFolder  ? Visibility.Visible : Visibility.Collapsed;
        var csV     = usesCs    ? Visibility.Visible : Visibility.Collapsed;

        lblPath.Visibility   = fileV;   rowPath.Visibility   = fileV;
        lblFolder.Visibility = folderV; rowFolder.Visibility = folderV;
        lblCs.Visibility     = csV;     csGrid.Visibility    = csV;
    }

    private static void UpdateHint(ComboBox cb, TextBox txtCs, TextBlock hint)
    {
        if (cb?.SelectedItem is null) return;
        var h = GetCsHint(cb.SelectedItem.ToString()!);
        hint.Text       = h;
        hint.Visibility = txtCs.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        txtCs.ToolTip   = h;
    }

    private void TxtSrcCs_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => hintSrcCs.Visibility = txtSrcCs.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void TxtTgtCs_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => hintTgtCs.Visibility = txtTgtCs.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private static string GetCsHint(string backend) => backend switch
    {
        "MySQL" or "MariaDB" => "Server=host;Port=3306;Database=db;Uid=user;Pwd=password;",
        "SqlServer"          => "Server=.\\SQLEXPRESS;Database=db;Integrated Security=True;TrustServerCertificate=True;",
        "PostgreSQL"         => "Host=host;Database=db;Username=user;Password=password;",
        "Oracle"             => "Data Source=host:1521/service;User Id=user;Password=password;",
        "DB2"                => "Server=host:50000;Database=MYDB;UID=user;PWD=pass;Security=none;Authentication=SERVER;",
        "Firebird"           => "DataSource=host;Database=C:\\path\\to\\db.fdb;User=SYSDBA;Password=masterkey;",
        _                    => ""
    };

    private static string GetCs(ComboBox cb, TextBox pathBox, TextBox folderBox, TextBox csBox)
    {
        var type = Enum.Parse<BackendType>(cb.SelectedItem?.ToString() ?? "SQLite");
        bool usesCs = type is BackendType.MySQL or BackendType.MariaDB or
            BackendType.SqlServer or BackendType.PostgreSQL or
            BackendType.Firebird  or BackendType.DB2 or BackendType.Oracle;
        if (usesCs)                     return csBox.Text.Trim();
        if (type == BackendType.DBase)  return folderBox.Text.Trim();
        return pathBox.Text.Trim();
    }

    private static BackendType GetType(ComboBox cb)
        => Enum.Parse<BackendType>(cb.SelectedItem?.ToString() ?? "SQLite");

    // ── Browse buttons ────────────────────────────────────────────────────────

    private void BtnSrcBrowse_Click(object sender, RoutedEventArgs e)         => BrowseFile(txtSrcPath, cbSrcType);
    private void BtnSrcBrowseFolder_Click(object sender, RoutedEventArgs e)   => BrowseFolder(txtSrcFolder);
    private void BtnTgtBrowse_Click(object sender, RoutedEventArgs e)         => BrowseFile(txtTgtPath, cbTgtType);
    private void BtnTgtBrowseFolder_Click(object sender, RoutedEventArgs e)   => BrowseFolder(txtTgtFolder);

    private static void BrowseFile(TextBox t, ComboBox cb)
        => WizardFileHelper.BrowseAndDetect(t, cb);

    private static void BrowseFolder(TextBox t)
    {
        var dlg = new OpenFolderDialog { Title = "Select dBase folder" };
        if (dlg.ShowDialog() == true) t.Text = dlg.FolderName;
    }

    // ── Transfer ─────────────────────────────────────────────────────────────

    private async Task RunTransferAsync()
    {
        _transferStarted  = true;
        btnBack.IsEnabled = false;
        btnNext.IsEnabled = false;
        LicenseService.IncrementTransferWizardUses();

        int programId  = (int)cbProgram.SelectedValue;
        var srcCs      = GetCs(cbSrcType, txtSrcPath, txtSrcFolder, txtSrcCs);
        var tgtCs      = GetCs(cbTgtType, txtTgtPath, txtTgtFolder, txtTgtCs);
        var srcType    = GetType(cbSrcType);
        var tgtType    = GetType(cbTgtType);

        List<string> tableNames;
        using (var db = new BlueprintDbContext())
        {
            tableNames = db.Tabeles
                .Where(t => t.Idprograma == programId && t.Skriven != true)
                .OrderBy(t => t.Nazivtabele)
                .Select(t => t.Nazivtabele!)
                .ToList();
        }

        if (tableNames.Count == 0)
        {
            lblTitle.Text = "Nothing to transfer";
            lblSub.Text   = "No tables found for the selected program. Run Schema Import first.";
            _transferDone = true;
            btnNext.Content   = "Finish";
            btnNext.IsEnabled = true;
            return;
        }

        try
        {
            var progress = new Progress<(int TableCurrent, int TableTotal, string Table, int RowCurrent, int RowTotal)>(p =>
            {
                progressBar.Value = (double)p.TableCurrent / p.TableTotal * 100;

                if (p.RowTotal > 0)
                {
                    pnlRowProgress.Visibility = Visibility.Visible;
                    progressBarRow.Value      = (double)p.RowCurrent / p.RowTotal * 100;
                    lblRowStatus.Text         = p.RowCurrent == 0
                        ? $"{p.Table}  —  {p.RowTotal} rows"
                        : $"{p.Table}  —  {p.RowCurrent} / {p.RowTotal} rows";
                }
                else
                {
                    pnlRowProgress.Visibility = Visibility.Collapsed;
                }

                if (p.RowCurrent == 0)
                    Log($"  [{p.TableCurrent}/{p.TableTotal}]  {p.Table}");
            });

            var result = await Task.Run(() =>
            {
                using var src = BackendConnectorFactory.Create(srcCs, srcType);
                using var tgt = BackendConnectorFactory.Create(tgtCs, tgtType);
                src.Open(); tgt.Open();
                return new DatabaseTransferService().Transfer(src, tgt, tableNames, progress);
            });

            progressBar.Value = 100;
            _transferDone = true;

            if (result.Success)
            {
                lblTitle.Text     = "Transfer complete!";
                lblSummary.Text   = $"✓  {result.TablesOk} table(s) transferred." +
                                    (result.TablesSkipped > 0 ? $"  {result.TablesSkipped} skipped (no common columns)." : "");
                pnlSummary.Visibility = Visibility.Visible;
            }
            else
            {
                lblTitle.Text     = "Transfer completed with errors";
                pnlSummary.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xF2));
                pnlSummary.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xCA, 0xCA));
                lblSummary.Foreground  = new SolidColorBrush(Color.FromRgb(0x7F, 0x1D, 0x1D));
                lblSummary.Text   = string.Join("\n", result.Errors.Select(e => $"• {e.Table}: {e.Error}"));
                pnlSummary.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("TransferWizard", "Transfer failed", ex);
            _transferDone = true;
            lblTitle.Text = "Transfer failed";
            pnlSummary.Background  = new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xF2));
            pnlSummary.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xCA, 0xCA));
            lblSummary.Foreground  = new SolidColorBrush(Color.FromRgb(0x7F, 0x1D, 0x1D));
            lblSummary.Text        = ex.Message;
            pnlSummary.Visibility  = Visibility.Visible;
        }
        finally
        {
            btnNext.Content   = "Finish";
            btnNext.IsEnabled = true;
        }
    }

    private void Log(string msg)
    {
        lstLog.Items.Add(msg);
        if (lstLog.Items.Count > 0)
            lstLog.ScrollIntoView(lstLog.Items[^1]);
    }
}
