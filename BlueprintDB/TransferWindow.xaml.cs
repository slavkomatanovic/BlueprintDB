using System.IO;
using System.Windows;
using System.Windows.Controls;
using Blueprint.App.Backend;
using Blueprint.App.Models;
using Microsoft.Win32;

namespace Blueprint.App;

public partial class TransferWindow : Window
{
    public TransferWindow()
    {
        InitializeComponent();
        LoadProgrami();
        LoadTypeComboBoxes();
        LanguageService.TranslateWindow(this);
        WindowSettings.Restore("TransferWindow", this);
        Closing += (_, _) => WindowSettings.Save("TransferWindow", this);
    }

    // ── Init ────────────────────────────────────────────────────────────────

    private void LoadProgrami()
    {
        try
        {
            using var db = new BlueprintDbContext();
            cbProgrami.ItemsSource = db.Programis
                .Where(p => p.Skriven != true)
                .OrderBy(p => p.Nazivprograma)
                .ToList();

            if (AppState.SelectedProgramId > 0)
                cbProgrami.SelectedValue = AppState.SelectedProgramId;
        }
        catch (Exception ex)
        {
            LogService.Error("TransferWindow", "Failed to load programs", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void LoadTypeComboBoxes()
    {
        var types = Enum.GetNames<BackendType>();
        cbSrcType.ItemsSource = types;
        cbTgtType.ItemsSource = types;
        cbSrcType.SelectedIndex = 0;
        cbTgtType.SelectedIndex = 0;
    }

    // ── Type selection → show/hide field rows ────────────────────────────────
    //
    //  File mode   (SQLite, Access):              path textbox + Browse (file)
    //  Folder mode (DBase):                       path textbox + Browse (folder)
    //  CS mode     (everything else):             connection string textbox, no Browse

    private void cbSrcType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ToggleConnectionFields(cbSrcType, lblSrcPath, txtSrcPath, btnSrcBrowse, lblSrcCS, txtSrcCS);

    private void cbTgtType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ToggleConnectionFields(cbTgtType, lblTgtPath, txtTgtPath, btnTgtBrowse, lblTgtCS, txtTgtCS);

    private static void ToggleConnectionFields(
        ComboBox typeBox,
        Label lblPath, TextBox txtPath, Button btnBrowse,
        Label lblCs,   TextBox txtCs)
    {
        var selected = typeBox.SelectedItem?.ToString();
        bool usesCs = selected is
            nameof(BackendType.MySQL)      or nameof(BackendType.MariaDB) or
            nameof(BackendType.SqlServer)  or nameof(BackendType.PostgreSQL) or
            nameof(BackendType.Firebird)   or nameof(BackendType.DB2) or
            nameof(BackendType.Oracle);

        var fileVis = usesCs ? Visibility.Collapsed : Visibility.Visible;
        var csVis   = usesCs ? Visibility.Visible   : Visibility.Collapsed;

        lblPath.Visibility   = fileVis;
        txtPath.Visibility   = fileVis;
        btnBrowse.Visibility = fileVis;
        lblCs.Visibility     = csVis;
        txtCs.Visibility     = csVis;
    }

    // ── Browse buttons ───────────────────────────────────────────────────────

    private void BtnSrcBrowse_Click(object sender, RoutedEventArgs e)
        => Browse(txtSrcPath, cbSrcType.SelectedItem?.ToString() == nameof(BackendType.DBase));

    private void BtnTgtBrowse_Click(object sender, RoutedEventArgs e)
        => Browse(txtTgtPath, cbTgtType.SelectedItem?.ToString() == nameof(BackendType.DBase));

    private static void Browse(TextBox target, bool folderMode)
    {
        if (folderMode)
        {
            var dlg = new OpenFolderDialog { Title = "Select dBase folder" };
            if (!string.IsNullOrEmpty(target.Text))
                dlg.InitialDirectory = target.Text;
            if (dlg.ShowDialog() == true)
                target.Text = dlg.FolderName;
        }
        else
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select database file",
                Filter = "Database files|*.sqlite;*.db;*.accdb;*.mdb;*.fdb;*.gdb|All files|*.*"
            };
            if (!string.IsNullOrEmpty(target.Text))
                dlg.InitialDirectory = Path.GetDirectoryName(target.Text) ?? "";
            if (dlg.ShowDialog() == true)
                target.Text = dlg.FileName;
        }
    }

    // ── Transfer ─────────────────────────────────────────────────────────────

    private async void BtnTransfer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
        if (cbProgrami.SelectedValue is not int programId)
        {
            MyMsgBox.Show("MSG_ODABERI_PROGRAM", icon: MessageBoxImage.Warning);
            return;
        }

        var (srcCs, srcType) = GetConnectionInfo(cbSrcType, txtSrcPath, txtSrcCS);
        var (tgtCs, tgtType) = GetConnectionInfo(cbTgtType, txtTgtPath, txtTgtCS);

        if (string.IsNullOrWhiteSpace(srcCs))
        {
            MyMsgBox.Show("MSG_TRANSFER_SRC_EMPTY", icon: MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(tgtCs))
        {
            MyMsgBox.Show("MSG_TRANSFER_TGT_EMPTY", icon: MessageBoxImage.Warning);
            return;
        }

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
            MyMsgBox.Show("MSG_NEMA_TABELA_ZA_TRANSFER", icon: MessageBoxImage.Warning);
            return;
        }

        btnTransfer.IsEnabled = false;
        progressBar.Value     = 0;
        lblStatus.Text        = LanguageService.T("MSG_TRANSFER_RUNNING");

        progressBarRow.Visibility = Visibility.Collapsed;
        lblRowStatus.Visibility   = Visibility.Collapsed;

        var progress = new Progress<(int TableCurrent, int TableTotal, string Table, int RowCurrent, int RowTotal)>(p =>
        {
            progressBar.Value = (double)p.TableCurrent / p.TableTotal * 100;
            lblStatus.Text    = $"{p.TableCurrent}/{p.TableTotal}  —  {p.Table}";

            if (p.RowTotal > 0)
            {
                progressBarRow.Visibility = Visibility.Visible;
                lblRowStatus.Visibility   = Visibility.Visible;
                progressBarRow.Value      = (double)p.RowCurrent / p.RowTotal * 100;
                lblRowStatus.Text         = p.RowCurrent == 0
                    ? $"{p.RowTotal} rows"
                    : $"{p.RowCurrent} / {p.RowTotal} rows";
            }
            else
            {
                progressBarRow.Visibility = Visibility.Collapsed;
                lblRowStatus.Visibility   = Visibility.Collapsed;
            }
        });

        try
        {
            var result = await Task.Run(() =>
            {
                using var src = BackendConnectorFactory.Create(srcCs, srcType);
                using var tgt = BackendConnectorFactory.Create(tgtCs, tgtType);
                src.Open();
                tgt.Open();
                return new DatabaseTransferService().Transfer(src, tgt, tableNames, progress);
            });

            progressBar.Value = 100;

            if (result.Success)
            {
                lblStatus.Text = LanguageService.T("MSG_TRANSFER_DONE");
                MessageBox.Show(
                    $"{LanguageService.T("MSG_TRANSFER_DONE")}\n\n" +
                    $"Tables transferred: {result.TablesOk}\n" +
                    $"Tables skipped (no common columns): {result.TablesSkipped}",
                    LanguageService.T("FORM_TRANSFER"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                lblStatus.Text = LanguageService.T("MSG_TRANSFER_ERRORS");
                var detail = string.Join("\n", result.Errors.Select(err => $"  • {err.Table}: {err.Error}"));
                MessageBox.Show(
                    $"{LanguageService.T("MSG_TRANSFER_ERRORS")}\n\n{detail}",
                    LanguageService.T("FORM_TRANSFER"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            LogService.Error("Transfer", "Transfer error", ex);
            lblStatus.Text = ex.Message;
            MessageBox.Show(ex.Message, LanguageService.T("FORM_TRANSFER"),
                            MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            btnTransfer.IsEnabled = true;
        }
        } // outer try
        catch (Exception ex)
        {
            LogService.Error("Transfer", "Unexpected error", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private static (string ConnectionString, BackendType Type) GetConnectionInfo(
        ComboBox typeBox, TextBox pathBox, TextBox csBox)
    {
        var type = Enum.Parse<BackendType>(typeBox.SelectedItem?.ToString() ?? "SQLite");
        bool usesCs = type is
            BackendType.MySQL      or BackendType.MariaDB   or
            BackendType.SqlServer  or BackendType.PostgreSQL or
            BackendType.Firebird   or BackendType.DB2       or
            BackendType.Oracle;
        var cs = usesCs ? csBox.Text.Trim() : pathBox.Text.Trim();
        return (cs, type);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
