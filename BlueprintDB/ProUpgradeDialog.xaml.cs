using System.Diagnostics;
using System.Windows;

namespace Blueprint.App;

public partial class ProUpgradeDialog : Window
{
    public ProUpgradeDialog()
    {
        InitializeComponent();
    }

    private void BtnBuy_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://blueprintdb.io/#pricing") { UseShellExecute = true });
        // Keep dialog open so user can enter key after purchase
    }

    private void BtnEnterKey_Click(object sender, RoutedEventArgs e)
    {
        new LicenseActivationWindow { Owner = this }.ShowDialog();
        if (LicenseService.IsPro)
            Close();
    }
}
