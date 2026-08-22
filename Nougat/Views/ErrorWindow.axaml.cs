using System.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Nougat.Chrome;
using NLog;

namespace Nougat.Views;

public partial class ErrorWindow : ChromeWindow
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private readonly string _details;

    // Parameterloser ctor für den XAML-Designer.
    public ErrorWindow() : this(new InvalidOperationException("Beispielfehler")) { }

    public ErrorWindow(Exception ex)
    {
        InitializeComponent();
        _details = ex.ToString();
        MessageText.Text = ex.Message;
        DetailText.Text = _details;
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard is { } clipboard)
                await clipboard.SetTextAsync(_details);
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Details konnten nicht in die Zwischenablage kopiert werden.");
        }
    }

    private void OnOpenLogClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
            if (Directory.Exists(logDir))
                Process.Start(new ProcessStartInfo(logDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Log-Ordner konnte nicht geöffnet werden.");
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
