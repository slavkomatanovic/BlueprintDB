using System.Windows;
using System.Windows.Controls;
using Blueprint.App.Models;
using Microsoft.EntityFrameworkCore;

namespace Blueprint.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        LanguageService.TranslateWindow(this);
        ShowHomeView();
        UpdateStatusBar();
        WindowSettings.Restore("MainWindow", this);
        Loaded   += async (_, _) =>
        {
            (mainContent.Content as HomeView)?.RefreshGetStarted();
            var update = await UpdateService.CheckForUpdateAsync();
            if (update is not null)
                (mainContent.Content as HomeView)?.ShowUpdateBanner(update);
        };
        Closing  += (_, _) => WindowSettings.Save("MainWindow", this);
    }

    // ── Navigation ──────────────────────────────────────────────────────────

    private void ShowHomeView()
    {
        var home = new HomeView();
        home.ProgramiRequested       += (_, _) => OpenProgrami();
        home.TabeleRequested         += (_, _) => OpenTabele();
        home.WizardRequested         += (_, _) => OpenWizard();
        home.TransferWizardRequested += (_, _) => OpenTransferWizard();
        home.SchemaSyncRequested     += (_, _) => OpenSchemaSyncWizard();
        home.KonfiguracijaRequested  += (_, _) => OpenSetup();
        home.KrajRequested           += (_, _) => Application.Current.Shutdown();

        mainContent.Content = home;
        Width      = 900;
        Height     = 620;
        ResizeMode = ResizeMode.CanResize;
        CenterOnScreen();
    }

    private void OpenSetup()
    {
        var win = new KonfiguracijaWindow { Owner = this };
        win.ConfigurationComplete += (_, _) =>
        {
            UpdateStatusBar();
            Title = $"Blueprint \u2014 {AppState.SelectedProgramName}";
        };
        win.Closed += (_, _) => Activate();
        win.Show();
    }

    private void OpenWizard()
    {
        // Pro gate is enforced inside WizardWindow when backend type is selected
        var win = new WizardWindow { Owner = this };
        win.ImportComplete += (_, _) =>
        {
            UpdateStatusBar();
            Title = $"Blueprint \u2014 {AppState.SelectedProgramName}";
        };
        win.Closed += (_, _) =>
        {
            Activate();
            (mainContent.Content as HomeView)?.RefreshGetStarted();
        };
        win.Show();
    }

    private void OpenTransferWizard()
    {
        if (!LicenseService.CanUseTransferWizard)
        {
            if (!PromptActivation(
                $"Transfer Data Wizard\n\nYou have used all {LicenseService.FreeTransferLimit} free runs."))
                return;
            if (!LicenseService.CanUseTransferWizard) return;
        }

        // Warn free users how many runs they have left (before the last one)
        if (!LicenseService.IsPro)
        {
            int remaining = LicenseService.FreeTransferRunsRemaining;
            if (remaining <= 2)
            {
                var msg = remaining == 1
                    ? "This is your last free Transfer Data Wizard run.\nUpgrade to Pro for unlimited transfers."
                    : $"You have {remaining} free Transfer Data Wizard runs remaining.\nUpgrade to Pro for unlimited transfers.";
                MessageBox.Show(msg, "Blueprint Free", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        var win = new TransferWizardWindow { Owner = this };
        win.Closed += (_, _) => Activate();
        win.Show();
    }

    private void OpenSchemaSyncWizard()
    {
        var win = new SchemaSyncWizardWindow { Owner = this };
        win.Closed += (_, _) => Activate();
        win.Show();
    }

    /// <summary>
    /// Shows the activation window for a Pro feature.
    /// Returns true if the user activated Pro, false if they cancelled.
    /// </summary>
    private bool PromptActivation(string featureName)
    {
        MessageBox.Show(
            $"{featureName} requires Blueprint Pro.\n\nEnter your license key to activate.",
            "Blueprint Pro Required",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        new LicenseActivationWindow { Owner = this }.ShowDialog();
        UpdateStatusBar();
        return LicenseService.IsPro;
    }

    private static bool HasAnyPrograms()
    {
        try
        {
            using var db = new BlueprintDbContext();
            return db.Programis.Any(p => p.Skriven != true);
        }
        catch (Exception ex)
        {
            LogService.Error("MainWindow", "Error checking programs", ex);
            return false;
        }
    }

    // ── Status bar & helpers ─────────────────────────────────────────────────

    private void UpdateStatusBar()
    {
        lblProgram.Text = AppState.SelectedProgramId > 0
            ? $"\uE716  {AppState.SelectedProgramName}"
            : "";
        lblBackend.Text = !string.IsNullOrEmpty(AppState.BackendDatabasePath)
            ? $"\uE8DA  {AppState.BackendDatabasePath}"
            : "";
        badgePro.Visibility = LicenseService.IsPro ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CenterOnScreen()
    {
        Left = (SystemParameters.PrimaryScreenWidth  - Width)  / 2;
        Top  = (SystemParameters.PrimaryScreenHeight - Height) / 2;
    }

    // ── Window actions ───────────────────────────────────────────────────────

    private static void OpenProgrami()
        => new ProgramiWindow().Show();

    private static void OpenTabele()
        => new TabeleWindow(AppState.SelectedProgramId, AppState.SelectedProgramName).Show();

    // ── Menu handlers ────────────────────────────────────────────────────────

    private void MenuLanguage_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        try
        {
            menuLanguage.Items.Clear();
            using var db = new BlueprintDbContext();
            var langs = db.Jeziks
                .Where(j => j.Skriven != true)
                .OrderBy(j => j.Nazivjezika)
                .ToList();

            foreach (var lang in langs)
            {
                var item = new MenuItem
                {
                    Header    = lang.Nazivjezika,
                    IsChecked = lang.Idjezik == LanguageService.CurrentLanguageId
                };
                var langId = lang.Idjezik;
                item.Click += (_, _) => SwitchLanguage(langId);
                menuLanguage.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            LogService.Error("MainWindow", "Error loading languages", ex);
        }
    }

    private void SwitchLanguage(int languageId)
    {
        try
        {
            // Persist choice — startup reads Podrazumijevani to select the initial language
            using (var db = new BlueprintDbContext())
            {
                foreach (var lang in db.Jeziks)
                    lang.Podrazumijevani = lang.Idjezik == languageId;
                db.SaveChanges();
            }

            LanguageService.CurrentLanguageId = languageId;
            LanguageService.Initialize();
            LanguageService.TranslateWindow(this);
            if (mainContent.Content is HomeView home)
                LanguageService.TranslateLogicalChildren(home);
        }
        catch (Exception ex)
        {
            LogService.Error("MainWindow", "Error switching language", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
        }
    }

    private void MenuLicense_Click(object sender, RoutedEventArgs e)
    {
        new LicenseActivationWindow { Owner = this }.ShowDialog();
        UpdateStatusBar();  // refresh PRO badge after possible activation/deactivation
    }

    private void MenuHelpTopics_Click(object sender, RoutedEventArgs e)        => new HelpWindow { Owner = this }.Show();
    private void MenuAbout_Click(object sender, RoutedEventArgs e)             => new AboutWindow { Owner = this }.ShowDialog();
    private void MenuLog_Click(object sender, RoutedEventArgs e)               => new LogWindow { Owner = this }.Show();

    private void MenuProgrami_Click(object sender, RoutedEventArgs e)          => OpenProgrami();
    private void MenuTabele_Click(object sender, RoutedEventArgs e)            => OpenTabele();
    private void MenuSurplus_Click(object sender, RoutedEventArgs e)           => new TabeleNoveWindow { Owner = this }.Show();
    private void MenuRelacije_Click(object sender, RoutedEventArgs e)          => new RelacijeWindow().Show();
    private void MenuPromjenaNaziva_Click(object sender, RoutedEventArgs e)    => new PromjenaNazivaWindow().Show();
    private void MenuWizard_Click(object sender, RoutedEventArgs e)            => OpenWizard();
    private void MenuTransferWizard_Click(object sender, RoutedEventArgs e)    => OpenTransferWizard();
    private void MenuSchemaSyncWizard_Click(object sender, RoutedEventArgs e)  => OpenSchemaSyncWizard();
    private void MenuBatchSync_Click(object sender, RoutedEventArgs e)
    {
        var win = new BatchSchemaSyncWindow { Owner = this };
        win.Closed += (_, _) => Activate();
        win.Show();
    }
    private void MenuMatrixImport_Click(object sender, RoutedEventArgs e)
    {
        var win = new MatrixImportWindow { Owner = this };
        win.Closed += (_, _) => Activate();
        win.Show();
    }
    private void MenuTransfer_Click(object sender, RoutedEventArgs e)          => new TransferWindow { Owner = this }.Show();
    private void MenuKonfiguracija_Click(object sender, RoutedEventArgs e)     => OpenSetup();
    private void MenuKraj_Click(object sender, RoutedEventArgs e)              => Application.Current.Shutdown();

    private void MenuResetApp_Click(object sender, RoutedEventArgs e)
    {
        var r1 = MyMsgBox.Show("MSG_RESET_CONFIRM1", buttons: MessageBoxButton.YesNo, icon: MessageBoxImage.Warning);
        if (r1 != MessageBoxResult.Yes) return;

        var r2 = MyMsgBox.Show("MSG_RESET_CONFIRM2", buttons: MessageBoxButton.YesNo, icon: MessageBoxImage.Warning);
        if (r2 != MessageBoxResult.Yes) return;

        try
        {
            using var db = new BlueprintDbContext();
            db.Tempkolonet2s.RemoveRange(db.Tempkolonet2s);
            db.Tempkolonet1s.RemoveRange(db.Tempkolonet1s);
            db.Kolonenoves.RemoveRange(db.Kolonenoves);
            db.Tabelenoves.RemoveRange(db.Tabelenoves);
            db.Kolones.RemoveRange(db.Kolones);
            db.Relacijes.RemoveRange(db.Relacijes);
            db.Tabeles.RemoveRange(db.Tabeles);
            db.Promjenanazivatabelas.RemoveRange(db.Promjenanazivatabelas);
            db.Dokumentis.RemoveRange(db.Dokumentis);
            db.Poglavljas.RemoveRange(db.Poglavljas);
            db.Putanjes.RemoveRange(db.Putanjes);
            db.Parametris.RemoveRange(db.Parametris);
            db.Logs.RemoveRange(db.Logs);
            db.Programis.RemoveRange(db.Programis);
            db.SaveChanges();
            db.Database.ExecuteSqlRaw("VACUUM;");
        }
        catch (Exception ex)
        {
            LogService.Error("MainWindow", "Reset failed", ex);
            MyMsgBox.Show(ex.Message, icon: MessageBoxImage.Error);
            return;
        }

        AppState.SelectedProgramId   = 0;
        AppState.SelectedProgramName = "";
        AppState.BackendDatabasePath = "";

        // Close all child windows before refreshing
        foreach (var w in Application.Current.Windows.OfType<Window>().Where(w => w != this).ToList())
            w.Close();

        UpdateStatusBar();
        Title = "Blueprint";
        ShowHomeView();
    }
}
