using System.Diagnostics;
using Avalonia.Interactivity;
using Nougat.Chrome;
using Nougat.ViewModels;

namespace Nougat.Views;

public partial class InfoWindow : ChromeWindow
{
    public InfoWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    /// <summary>
    /// Beim Öffnen einmal nach Updates sehen. Bewusst nicht awaited: Der Dialog
    /// soll sofort stehen, auch wenn GitHub gerade langsam oder offline ist.
    /// </summary>
    private void OnOpened(object? sender, System.EventArgs e)
    {
        if (DataContext is InfoWindowViewModel vm && vm.CheckUpdateCommand.CanExecute(null))
            vm.CheckUpdateCommand.Execute(null);
    }

    private void OnGithubClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InfoWindowViewModel vm) OpenUrl(vm.GithubUrl);
    }

    private void OnCoffeeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is InfoWindowViewModel vm) OpenUrl(vm.CoffeeUrl);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Kein passender Handler - Fallback still.
        }
    }
}
