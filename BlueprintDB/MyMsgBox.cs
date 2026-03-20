using System;
using System.Windows;

namespace Blueprint.App;

/// <summary>
/// C# ekvivalent MyMsgBox procedure iz projekta Pragma.
/// Automatski prevodi poruku i naslov prije prikazivanja MessageBox-a.
/// </summary>
public static class MyMsgBox
{
    public static MessageBoxResult Show(
        string messageKey, 
        string titleKey = "Blueprint", 
        MessageBoxButton buttons = MessageBoxButton.OK, 
        MessageBoxImage icon = MessageBoxImage.Information)
    {
        // Prevedi tekst poruke (ako ne postoji u rječniku, T vraća sam ključ)
        string translatedMessage = LanguageService.T(messageKey);
        
        // Prevedi naslov
        string translatedTitle = LanguageService.T(titleKey);

        // Prikaži standardni WPF MessageBox sa prevedenim tekstovima
        return MessageBox.Show(translatedMessage, translatedTitle, buttons, icon);
    }
}
