using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Nougat.Infrastructure;
using Nougat.Localization;
using Nougat.Models;
using Nougat.Services;
using Nougat.ViewModels;
using Nougat.Views;

namespace Nougat;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private TrayController? _tray;

    public override void Initialize()
    {
        MaskingLayoutRenderer.RegisterRenderer();
        GlobalExceptionHandler.Install();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = new ServiceCollection().AddNougat().BuildServiceProvider();
        ApplyUiCulture();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var vm = Services.GetRequiredService<MainWindowViewModel>();
            var window = new MainWindow { DataContext = vm };
            desktop.MainWindow = window;

            _tray = new TrayController(this, window);
            _tray.Install();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Setzt die gespeicherte Sprache, BEVOR das erste Fenster gebaut wird —
    /// sonst rendert die UI kurz in der Systemsprache und springt dann um.
    /// Ohne gespeicherte Einstellung bleibt es bei der Systemsprache.
    /// </summary>
    private static void ApplyUiCulture()
    {
        var iso = Services.GetService<AppSettings>()?.UiCulture;
        if (!string.IsNullOrWhiteSpace(iso))
            LocalizationService.Instance.SetCulture(iso);
    }
}
