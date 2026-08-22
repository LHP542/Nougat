using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nougat.Services;

namespace Nougat.ViewModels;

public partial class SdkInstallViewModel : ViewModelBase
{
    private readonly DotnetSdkService _sdk;
    private CancellationTokenSource? _cts;

    public event EventHandler<string>? Completed;  // string = installierter Pfad

    [ObservableProperty] public partial string StatusText { get; set; } = "Bereit zur Installation";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial bool IsInstalling { get; set; }
    public ObservableCollection<string> LogLines { get; } = [];

    public SdkInstallViewModel(DotnetSdkService sdk)
    {
        _sdk = sdk;
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsInstalling) return;
        IsInstalling = true;
        _cts = new CancellationTokenSource();
        try
        {
            StatusText = "Installiere .NET 10 SDK ...";
            var path = await _sdk.InstallAsync(
                channel: "10.0",
                progress: new Progress<double>(p => Progress = p),
                onLog: line => LogLines.Add(line),
                ct: _cts.Token
            );
            StatusText = "Installation abgeschlossen: " + path;
            Completed?.Invoke(this, path);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Abgebrochen.";
        }
        catch (Exception ex)
        {
            StatusText = "Fehler: " + ex.Message;
        }
        finally
        {
            IsInstalling = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
