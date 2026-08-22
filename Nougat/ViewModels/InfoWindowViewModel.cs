using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nougat.Infrastructure;
using Nougat.Localization;
using Nougat.Services;

namespace Nougat.ViewModels;

public partial class InfoWindowViewModel : ViewModelBase
{
    private readonly UpdateService _updates;
    private readonly ILogger<InfoWindowViewModel> _logger;

    public string Version { get; }
    public string GithubUrl => "https://github.com/LHP542/Nougat";
    public string CoffeeUrl => "https://www.buymeacoffee.com/kroste";
    /// <summary>Arbeitsgruppe, unter der das Werkzeug entsteht.</summary>
    public string Organisation => "Arbeitsgruppe 5424 IT-Basis-Dienste";

    [ObservableProperty] public partial string UpdateStatus { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    public partial bool UpdateAvailable { get; set; }

    [ObservableProperty] public partial bool IsDownloading { get; set; }
    [ObservableProperty] public partial double DownloadProgress { get; set; }

    public InfoWindowViewModel(UpdateService updates, ILogger<InfoWindowViewModel> logger)
    {
        _updates = updates;
        _logger = logger;
        Version = "Version " + AppVersion.Current;
    }

    private bool CanCheckUpdate() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanCheckUpdate))]
    public async Task CheckUpdateAsync()
    {
        IsBusy = true;
        UpdateStatus = Loc.T("Update_Searching");
        try
        {
            var result = await _updates.CheckAsync(forceRefresh: true);
            UpdateAvailable = result.UpdateAvailable && result.AssetAvailable;

            UpdateStatus = result switch
            {
                { Error: { } e } => Loc.T("Update_CheckFailed", e),
                { UpdateAvailable: true, AssetAvailable: true } r => Loc.T("Update_Available", r.LatestVersion),
                { UpdateAvailable: true, AssetAvailable: false } r => Loc.T("Update_NoAsset", r.LatestVersion),
                _ => Loc.T("Update_UpToDate"),
            };
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanInstallUpdate() => UpdateAvailable && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    public async Task InstallUpdateAsync()
    {
        IsBusy = true;
        IsDownloading = true;
        DownloadProgress = 0;
        UpdateStatus = Loc.T("Update_Downloading");
        try
        {
            var progress = new Progress<double>(p => DownloadProgress = p);
            var ok = await _updates.DownloadAndApplyAsync(progress);

            if (!ok)
            {
                UpdateStatus = Loc.T("Update_FailedSeeLog");
                return;
            }

            UpdateStatus = Loc.T("Update_Installing");
            // PFLICHT: Der Installer wartet auf das Prozessende. Ohne diesen Aufruf
            // haengt die UI bei 100 % und das Update wird nie angewendet.
            _updates.TerminateForUpdate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Update-Installation fehlgeschlagen.");
            UpdateStatus = Loc.T("Update_Failed", ex.Message);
        }
        finally
        {
            IsDownloading = false;
            IsBusy = false;
        }
    }
}
