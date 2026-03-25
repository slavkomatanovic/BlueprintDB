using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;

namespace Blueprint.App;

public partial class LicenseActivationWindow : Window
{
    public LicenseActivationWindow()
    {
        InitializeComponent();
        RefreshState();
    }

    // ── State display ────────────────────────────────────────────────────────

    private void RefreshState()
    {
        if (LicenseService.IsPro && LicenseService.ActiveKey != null)
        {
            pnlDeactivate.Visibility = Visibility.Visible;
            lblActiveKey.Text        = MaskKey(LicenseService.ActiveKey);
            btnActivate.IsEnabled    = false;
            ShowStatus(true, "\uE73E  Blueprint Pro is active. Thank you for your support!");
        }
        else
        {
            pnlDeactivate.Visibility = Visibility.Collapsed;
            btnActivate.IsEnabled    = true;
            pnlStatus.Visibility     = Visibility.Collapsed;
        }
    }

    private static string MaskKey(string key)
    {
        if (key.Length < 10) return key;
        var parts = key.Split('-');
        if (parts.Length == 5)
            return $"{parts[0]}-{parts[1]}-XXXX-XXXX-{parts[4][^4..]}";
        return key[..9] + "****";
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private async void BtnActivate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var key = txtKey.Text.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                ShowStatus(false, "Please enter a license key.");
                return;
            }

            btnActivate.IsEnabled = false;
            ShowStatus(true, "\uE895  Contacting license server…");

            var result = await LicenseService.ActivateAsync(key);

            switch (result)
            {
                case LicenseActivationResult.Success:
                    ShowStatus(true, "\uE73E  License activated successfully! Blueprint Pro is now active.");
                    RefreshState();
                    txtKey.Text = string.Empty;
                    break;

                case LicenseActivationResult.AlreadyActive:
                    ShowStatus(true, "\uE73E  This license is already active on this machine.");
                    btnActivate.IsEnabled = true;
                    break;

                case LicenseActivationResult.KeyExhausted:
                    ShowStatus(false, "\uE711  This license key has reached its activation limit.\n" +
                                      "Deactivate it on another machine first, or purchase a new license.");
                    btnActivate.IsEnabled = true;
                    break;

                case LicenseActivationResult.NetworkError:
                    ShowStatus(false, "\uE704  Could not reach the license server. Please check your internet connection and try again.");
                    btnActivate.IsEnabled = true;
                    break;

                case LicenseActivationResult.InvalidKey:
                default:
                    ShowStatus(false, "\uE711  Invalid license key. Please check for typos or purchase a new license.");
                    btnActivate.IsEnabled = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            LogService.Error("License", "Activation error", ex);
            ShowStatus(false, $"Unexpected error: {ex.Message}");
            btnActivate.IsEnabled = true;
        }
    }

    private void TxtKey_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return) BtnActivate_Click(sender, e);
    }

    private async void LblDeactivate_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var confirm = MessageBox.Show(
                "Deactivate Blueprint Pro on this machine?\n\nThis will free up one activation slot.",
                "Deactivate License",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            await LicenseService.DeactivateAsync();
            RefreshState();
            ShowStatus(false, "License deactivated. The app is now running in Free mode.");
        }
        catch (Exception ex)
        {
            LogService.Error("License", "Deactivation error", ex);
            ShowStatus(false, $"Unexpected error: {ex.Message}");
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void LnkBuy_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { }
        e.Handled = true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ShowStatus(bool success, string message)
    {
        lblStatus.Text            = message;
        pnlStatus.Background      = success
            ? new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7))
            : new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
        pnlStatus.BorderBrush     = success
            ? new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC))
            : new SolidColorBrush(Color.FromRgb(0xFC, 0xA5, 0xA5));
        pnlStatus.BorderThickness = new Thickness(1);
        lblStatus.Foreground      = success
            ? new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34))
            : new SolidColorBrush(Color.FromRgb(0x99, 0x17, 0x17));
        pnlStatus.Visibility      = Visibility.Visible;
    }
}
