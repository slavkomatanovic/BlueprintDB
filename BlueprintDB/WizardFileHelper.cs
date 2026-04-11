using Blueprint.App.Backend;
using Microsoft.Win32;

namespace Blueprint.App;

/// <summary>
/// Shared file-browse helpers for wizard windows.
/// Provides per-backend Open dialog filters and extension → backend detection.
/// </summary>
internal static class WizardFileHelper
{
    /// <summary>
    /// Returns an OpenFileDialog filter string scoped to the given backend.
    /// For backends that use a connection string (no file), returns the full combined filter.
    /// </summary>
    public static string GetFileFilter(BackendType type) => type switch
    {
        BackendType.SQLite   => "SQLite files|*.sqlite;*.db|All files|*.*",
        BackendType.Access   => "Access files|*.accdb;*.mdb|All files|*.*",
        BackendType.Firebird => "Firebird files|*.fdb;*.gdb|All files|*.*",
        _                    => "Database files|*.sqlite;*.db;*.accdb;*.mdb;*.fdb;*.gdb|All files|*.*",
    };

    /// <summary>
    /// Infers a backend type from a file extension.
    /// Returns null for unrecognised extensions or connection-string backends.
    /// </summary>
    public static BackendType? DetectFromExtension(string filePath) =>
        System.IO.Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".sqlite" or ".db"  => BackendType.SQLite,
            ".accdb"  or ".mdb" => BackendType.Access,
            ".fdb"    or ".gdb" => BackendType.Firebird,
            _                   => null,
        };

    /// <summary>
    /// Opens a file-browse dialog filtered for the currently selected backend,
    /// sets the path in <paramref name="pathBox"/>, and auto-switches
    /// <paramref name="typeCombo"/> if the chosen file's extension implies a
    /// different backend than the one currently selected.
    /// </summary>
    public static void BrowseAndDetect(System.Windows.Controls.TextBox pathBox,
                                       System.Windows.Controls.ComboBox typeCombo)
    {
        var current = Enum.TryParse<BackendType>(typeCombo.SelectedItem?.ToString(), out var t)
                      ? t : BackendType.SQLite;

        var dlg = new OpenFileDialog
        {
            Title  = "Select database file",
            Filter = GetFileFilter(current),
        };
        if (dlg.ShowDialog() != true) return;

        pathBox.Text = dlg.FileName;

        var detected = DetectFromExtension(dlg.FileName);
        if (detected is not null && detected != current)
            typeCombo.SelectedItem = detected.ToString();
    }
}
