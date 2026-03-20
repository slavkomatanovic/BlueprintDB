using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Blueprint.App.Models;

namespace Blueprint.App;

public static class LanguageService
{
    private static Dictionary<string, string> _translations = new();
    public static int CurrentLanguageId { get; set; } = 0;

    public static void Initialize()
    {
        try
        {
            using var db = new BlueprintDbContext();

            if (CurrentLanguageId == 0)
            {
                var defaultLang = db.Jeziks
                    .FirstOrDefault(j => j.Podrazumijevani == true && j.Skriven != true);
                CurrentLanguageId = defaultLang?.Idjezik ?? 1;
            }

            _translations = db.Rjecniks
                .Where(r => r.Idjezik == CurrentLanguageId && !string.IsNullOrEmpty(r.Original) && r.Skriven != true)
                .ToDictionary(
                    r => r.Original!.Trim(),
                    r => r.Prijevod ?? r.Original!,
                    StringComparer.OrdinalIgnoreCase
                );
        }
        catch
        {
            _translations = new Dictionary<string, string>();
        }
    }

    public static string T(string key, string fallback = "")
    {
        if (string.IsNullOrWhiteSpace(key)) return fallback;

        if (_translations.TryGetValue(key.Trim(), out var translation))
            return translation;

        return !string.IsNullOrEmpty(fallback) ? fallback : key;
    }

    /// <summary>
    /// Translates all controls in a Window using their Tag as the dictionary key.
    /// Uses LogicalTreeHelper so it works immediately after InitializeComponent().
    /// </summary>
    public static void TranslateWindow(Window window)
    {
        if (window == null) return;

        if (window.Tag is string windowKey && !string.IsNullOrEmpty(windowKey))
            window.Title = T(windowKey, window.Title);

        TranslateLogicalTree(window);
    }

    /// <summary>
    /// Translates all controls inside a UserControl (no Title handling).
    /// </summary>
    public static void TranslateLogicalChildren(DependencyObject root)
        => TranslateLogicalTree(root);

    private static void TranslateLogicalTree(DependencyObject parent)
    {
        foreach (var childObj in LogicalTreeHelper.GetChildren(parent))
        {
            if (childObj is not DependencyObject child) continue;

            if (child is FrameworkElement fe && fe.Tag is string key && !string.IsNullOrEmpty(key))
                ApplyTranslation(fe, key);

            // Translate DataGrid column headers via [KEY] notation in Header text
            if (child is DataGrid dg)
            {
                foreach (var col in dg.Columns)
                {
                    if (col.Header is string hdr && hdr.StartsWith("[") && hdr.EndsWith("]"))
                    {
                        var colKey = hdr.Substring(1, hdr.Length - 2);
                        col.Header = T(colKey, colKey);
                    }
                }
            }

            TranslateLogicalTree(child);
        }
    }

    private static void ApplyTranslation(FrameworkElement fe, string key)
    {
        switch (fe)
        {
            // GroupBox and other HeaderedContentControls (Expander, etc.)
            case HeaderedContentControl hcc when hcc.Header is string:
                hcc.Header = T(key, hcc.Header.ToString()!);
                break;

            // MenuItem, TabItem, TreeViewItem
            case HeaderedItemsControl hic when hic.Header is string:
                hic.Header = T(key, hic.Header.ToString()!);
                break;

            // Buttons, Labels (Content is string)
            case ContentControl cc when cc.Content is string:
                cc.Content = T(key, cc.Content.ToString()!);
                break;

            // TextBlock
            case TextBlock tb:
                tb.Text = T(key, tb.Text);
                break;
        }
    }
}
