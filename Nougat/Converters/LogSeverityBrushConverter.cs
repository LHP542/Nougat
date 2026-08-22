using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;
using Nougat.Models;

namespace Nougat.Converters;

/// <summary>
/// Faerbt die Severity-Spalte im Log-Panel.
///
/// WARUM: Vorher hing dort ein fester Gold-Brush — ein "Error" sah damit exakt
/// aus wie ein "Info". Die Brushes KrosteErrorBrush/WarnBrush/InfoBrush waren
/// in App.axaml definiert, aber an nichts gebunden.
/// </summary>
public sealed class LogSeverityBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as LogSeverity?) switch
        {
            LogSeverity.Error => "KrosteErrorBrush",
            LogSeverity.Warn => "KrosteWarnBrush",
            LogSeverity.Info => "KrosteInfoBrush",
            _ => "KrosteMutedTextBrush",
        };

        if (Avalonia.Application.Current?.TryGetResource(key, ThemeVariant.Dark, out var brush) == true
            && brush is IBrush found)
        {
            return found;
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Nur OneWay-Binding.");
}
