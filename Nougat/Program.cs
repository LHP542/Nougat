using Avalonia;
using NLog;

namespace Nougat;

internal static class Program
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            _logger.Fatal(ex, "Unbehandelte Exception im Programmstart.");
            return 1;
        }
        finally
        {
            LogManager.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
