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

    public HomeView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LanguageService.TranslateLogicalChildren(this);
            RefreshGetStarted();
        };
    }

    // Tracks whether the user dismissed the banner this session.
    private bool _bannerDismissed;

    /// <summary>
    /// Shows the Getting Started banner unless the user has already dismissed it this session.
    /// Called on load and after a wizard completes (to re-hide if wizard was just finished).
    /// </summary>
    public void RefreshGetStarted()
    {
        if (_bannerDismissed)
        {
            pnlGetStarted.Visibility = Visibility.Collapsed;
            return;
        }
        pnlGetStarted.Visibility = Visibility.Visible;
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
