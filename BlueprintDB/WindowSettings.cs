using System.Windows;
using Blueprint.App.Models;

namespace Blueprint.App;

/// <summary>
/// Persists and restores window position/size using the parametri table.
/// Format stored in Ocitano: "Left,Top,Width,Height" (integer pixels).
/// Idpoglavlja = 0 is reserved for app-level (non-document) settings.
/// </summary>
public static class WindowSettings
{
    private const int AppChapterId = 0;

    /// <summary>
    /// Saves current window position and size to the parametri table.
    /// Skips if the window is minimized (Left would be -32000).
    /// </summary>
    public static void Save(string windowName, Window window)
    {
        if (window.WindowState == WindowState.Minimized) return;

        var value = $"{(int)window.Left},{(int)window.Top},{(int)window.Width},{(int)window.Height}";

        try
        {
            using var db = new BlueprintDbContext();
            var param = db.Parametris.FirstOrDefault(p =>
                p.Idpoglavlja == AppChapterId && p.Nazivparametra == windowName);

            if (param == null)
            {
                db.Parametris.Add(new Parametri
                {
                    Idpoglavlja    = AppChapterId,
                    Nazivparametra = windowName,
                    Ocitano        = value
                });
            }
            else
            {
                param.Ocitano = value;
            }
            db.SaveChanges();
        }
        catch { }
    }

    /// <summary>
    /// Restores window position and size from the parametri table.
    /// Sets WindowStartupLocation = Manual so WPF uses the restored values.
    /// Clamps to screen bounds so the window cannot appear off-screen.
    /// </summary>
    public static void Restore(string windowName, Window window)
    {
        try
        {
            using var db = new BlueprintDbContext();
            var param = db.Parametris.FirstOrDefault(p =>
                p.Idpoglavlja == AppChapterId && p.Nazivparametra == windowName);

            if (param?.Ocitano == null) return;

            var parts = param.Ocitano.Split(',');
            if (parts.Length != 4) return;

            if (!int.TryParse(parts[0], out var l) || !int.TryParse(parts[1], out var t) ||
                !int.TryParse(parts[2], out var w) || !int.TryParse(parts[3], out var h)) return;

            // Guard: skip if window was minimized when last closed
            if (l < -30000 || t < -30000) return;

            // Enforce minimum size
            if (w < 200) w = 200;
            if (h < 150) h = 150;

            // Clamp so window doesn't land off-screen
            var sw = (int)SystemParameters.PrimaryScreenWidth;
            var sh = (int)SystemParameters.PrimaryScreenHeight;
            if (l + w > sw) l = sw - w;
            if (t + h > sh) t = sh - h;
            if (l < 0) l = 0;
            if (t < 0) t = 0;

            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left   = l;
            window.Top    = t;
            window.Width  = w;
            window.Height = h;
        }
        catch { }
    }
}
