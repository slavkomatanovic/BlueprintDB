using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Blueprint.App.Models;

namespace Blueprint.App;

public partial class HomeView : UserControl
{
    // Events that the ShellWindow (MainWindow) handles
    public event EventHandler? ProgramiRequested;
    public event EventHandler? TabeleRequested;
    public event EventHandler? WizardRequested;
    public event EventHandler? TransferWizardRequested;
    public event EventHandler? SchemaSyncRequested;
    public event EventHandler? KrajRequested;
    public event EventHandler? KonfiguracijaRequested;
    public event EventHandler? UpgradeRequested;

    private UpdateCheckResult? _pendingUpdate;

    public HomeView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LanguageService.TranslateLogicalChildren(this);
            RefreshGetStarted();
        };
    }

    public void ShowUpdateBanner(UpdateCheckResult result)
    {
        _pendingUpdate = result;
        lblUpdateTitle.Text = $"Update Available — Blueprint {result.TagName}";
        lblUpdateBody.Text  = $"You are running v{result.CurrentVersion.Major}.{result.CurrentVersion.Minor}.{result.CurrentVersion.Build}. " +
                              $"Version {result.TagName} is now available on GitHub.";
        pnlUpdate.Visibility = Visibility.Visible;
    }

    private void BtnDownloadUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null) return;
        Process.Start(new ProcessStartInfo(_pendingUpdate.ReleaseUrl) { UseShellExecute = true });
    }

    private void BtnSkipUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null) return;
        UpdateService.SkipVersion(_pendingUpdate.TagName);
        pnlUpdate.Visibility = Visibility.Collapsed;
    }

    // Tracks whether the user dismissed banners this session.
    private bool _bannerDismissed;
    private bool _proBannerDismissed;

    /// <summary>
    /// Shows or hides the Getting Started and Unlock Pro banners.
    /// Called on load and after a wizard completes.
    /// </summary>
    public void RefreshGetStarted()
    {
        bool hasProgrami = false;
        try
        {
            using var db = new BlueprintDbContext();
            hasProgrami = db.Programis.Any(p => p.Skriven != true);
        }
        catch { /* leave hasProgrami = false */ }

        pnlGetStarted.Visibility = (!_bannerDismissed && !hasProgrami)
            ? Visibility.Visible : Visibility.Collapsed;

        RefreshProBanner(hasProgrami);
    }

    /// <summary>
    /// Refreshes only the Pro upgrade banner (e.g. after a license activation).
    /// </summary>
    public void RefreshProBanner(bool? hasProgrami = null)
    {
        if (_proBannerDismissed || LicenseService.IsPro)
        {
            pnlUnlockPro.Visibility = Visibility.Collapsed;
            return;
        }

        bool hasP = hasProgrami ?? false;
        if (hasProgrami is null)
        {
            try
            {
                using var db = new BlueprintDbContext();
                hasP = db.Programis.Any(p => p.Skriven != true);
            }
            catch { }
        }

        pnlUnlockPro.Visibility = hasP ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnProgrami_Click(object sender, RoutedEventArgs e)
        => ProgramiRequested?.Invoke(this, EventArgs.Empty);

    private void BtnTabele_Click(object sender, RoutedEventArgs e)
        => TabeleRequested?.Invoke(this, EventArgs.Empty);

    private void BtnGetStarted_Click(object sender, RoutedEventArgs e)
        => WizardRequested?.Invoke(this, EventArgs.Empty);

    private void BtnDismissGetStarted_Click(object sender, RoutedEventArgs e)
    {
        _bannerDismissed = true;
        pnlGetStarted.Visibility = Visibility.Collapsed;
    }

    private void BtnUpgradePro_Click(object sender, RoutedEventArgs e)
        => UpgradeRequested?.Invoke(this, EventArgs.Empty);

    private void BtnDismissUpgradePro_Click(object sender, RoutedEventArgs e)
    {
        _proBannerDismissed = true;
        pnlUnlockPro.Visibility = Visibility.Collapsed;
    }

    private void BtnWizard_Click(object sender, RoutedEventArgs e)
        => WizardRequested?.Invoke(this, EventArgs.Empty);

    private void BtnTransferWizard_Click(object sender, RoutedEventArgs e)
        => TransferWizardRequested?.Invoke(this, EventArgs.Empty);

    private void BtnSchemaSyncWizard_Click(object sender, RoutedEventArgs e)
        => SchemaSyncRequested?.Invoke(this, EventArgs.Empty);

    private void BtnKraj_Click(object sender, RoutedEventArgs e)
        => KrajRequested?.Invoke(this, EventArgs.Empty);

    private void BtnKonfiguracija_Click(object sender, RoutedEventArgs e)
        => KonfiguracijaRequested?.Invoke(this, EventArgs.Empty);
}
