using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Blueprint.App.Models;

namespace Blueprint.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "—";

        lblVersion.Text       = $"Database Metadata Manager  ·  v{version}";
        lblVersionValue.Text  = version;
        lblLicense.Text       = LicenseService.IsPro ? "Blueprint Pro (activated)" : "Blueprint Free";
        lblMetadataPath.Text  = BlueprintDbContext.GetDatabasePath();
        lblMetadataPath.ToolTip = $"Click to copy:  {BlueprintDbContext.GetDatabasePath()}";
        lblRuntime.Text       = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    }

    private void LblMetadataPath_Click(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Clipboard.SetText(BlueprintDbContext.GetDatabasePath());
            lblMetadataPath.ToolTip = "Copied!";
        }
        catch { /* clipboard not available */ }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}
