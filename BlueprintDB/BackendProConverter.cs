using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Blueprint.App;

/// <summary>
/// Returns Visibility.Visible if the backend string requires Pro; Collapsed if it is free (SQLite / Access).
/// Used in ComboBox ItemTemplates to show a "PRO" badge next to non-free backends.
/// </summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class BackendProConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = value?.ToString();
        return name is "SQLite" or "Access" ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
