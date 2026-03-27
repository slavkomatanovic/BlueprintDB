using System.Windows;

namespace Blueprint.App;

public partial class App : Application
{
    private void App_Startup(object sender, StartupEventArgs e)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        // Global UI thread exception handler — log and show friendly message
        DispatcherUnhandledException += (_, ev) =>
        {
            LogService.Error("App", "Unhandled UI exception", ev.Exception);
            MessageBox.Show($"Neočekivana greška:\n{ev.Exception.Message}",
                "Blueprint", MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true;
        };

        // Background thread / non-UI exceptions (e.g. Oracle internal pool threads)
        AppDomain.CurrentDomain.UnhandledException += (_, ev) =>
        {
            if (ev.ExceptionObject is Exception ex)
            {
                LogService.Error("App", "Unhandled background thread exception", ex);
                // Show dialog on UI thread — process may still terminate after this
                try
                {
                    Dispatcher.Invoke(() =>
                        MessageBox.Show($"Neočekivana pozadinska greška:\n{ex.Message}",
                            "Blueprint", MessageBoxButton.OK, MessageBoxImage.Error));
                }
                catch { /* dispatcher may be shut down */ }
            }
        };

        // Unobserved task exceptions (e.g. Oracle fire-and-forget internal tasks)
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            LogService.Error("App", "Unobserved task exception", ev.Exception);
            ev.SetObserved(); // prevent process termination
        };

        try
        {
            DbSeeder.Seed();
            LanguageService.Initialize();
            LicenseService.Initialize();
            AppState.LoadSelectedProgram();
        }
        catch (Exception ex)
        {
            LogService.Error("Startup", "Startup initialization error", ex);
            MessageBox.Show($"Greška pri pokretanju aplikacije:\n{ex.Message}",
                "Blueprint", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        LogService.Info("Startup", $"Blueprint started. User: {Environment.UserName}, Machine: {Environment.MachineName}");

        // MainWindow is the persistent shell container.
        // It shows KonfiguracijaView internally on startup.
        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogService.Info("Startup", "Blueprint closed.");
        base.OnExit(e);
    }
}
