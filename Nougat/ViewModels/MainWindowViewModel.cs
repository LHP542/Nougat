using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nougat.Localization;
using Nougat.Models;
using Nougat.Services;

namespace Nougat.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// GitHub-Konto, dessen Repos zur Auswahl stehen. Seit dem Umzug der
    /// dienstlichen Projekte die Organisation <c>LHP542</c> statt des
    /// persönlichen Kontos — sonst listet Nougat die privaten Repos und
    /// findet genau die nicht, für die das Offline-Bundle gebaut wird.
    /// </summary>
    private const string GithubAccount = "LHP542";

    private readonly GithubRepoService _github;
    private readonly RepoCacheService _cache;
    private readonly BundleOrchestrator _orchestrator;
    private readonly DotnetSdkService _sdk;
    private readonly SettingsService _settingsService;
    private readonly UpdateService _updates;
    private readonly AppSettings _settings;
    private readonly ILogger<MainWindowViewModel> _logger;

    private CancellationTokenSource? _bundleCts;

    public ObservableCollection<RepoItemViewModel> Repos { get; } = [];
    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    [ObservableProperty] public partial string OutputDirectory { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = Loc.T("Status_Ready");
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildBundleCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelBuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshReposCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BuildBundleCommand))]
    [NotifyPropertyChangedFor(nameof(SelectedCountText))]
    public partial int SelectedCount { get; set; }

    /// <summary>
    /// "{0} ausgewählt" als fertige Property statt StringFormat im XAML — ein
    /// StringFormat-Literal wäre nicht übersetzbar.
    /// </summary>
    public string SelectedCountText => Loc.T("Main_SelectedCount", SelectedCount);
    [ObservableProperty] public partial string RepoListStatus { get; set; } = "";

    public string Title => Loc.T("Main_Title");

    public MainWindowViewModel(
        GithubRepoService github,
        RepoCacheService cache,
        BundleOrchestrator orchestrator,
        DotnetSdkService sdk,
        SettingsService settingsService,
        AppSettings settings,
        UpdateService updates,
        ILogger<MainWindowViewModel> logger)
    {
        _github = github;
        _cache = cache;
        _orchestrator = orchestrator;
        _sdk = sdk;
        _settingsService = settingsService;
        _settings = settings;
        _updates = updates;
        _logger = logger;

        OutputDirectory = _settings.OutputDirectory;

        // Beim Start: Cache lesen, dann im Hintergrund refresh.
        _ = LoadInitialAsync();
        _ = CheckForUpdateAsync();
    }

    private async Task LoadInitialAsync()
    {
        HashSet<string> selectedFromCache = new(StringComparer.OrdinalIgnoreCase);

        if (_cache.TryLoad(out var cached))
        {
            selectedFromCache = new HashSet<string>(cached.SelectedNames, StringComparer.OrdinalIgnoreCase);
            PopulateFromCache(cached.Repos, selectedFromCache);
            RepoListStatus = Loc.T("Repos_FromCache", cached.Repos.Count, cached.FetchedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
            if (_cache.IsFresh(cached, TimeSpan.FromHours(_settings.RepoCacheTtlHours)))
                return;
        }

        // Hintergrund-Refresh
        await RefreshReposAsync();
    }

    /// <summary>
    /// Update-Check beim Start — nicht blockierend und ohne Fehlerdialog. Ist der
    /// Rechner offline oder der Proxy zickig, steht davon nur eine Zeile im Log.
    /// Installiert wird hier nichts: Das passiert erst auf Klick im Über-Fenster.
    /// </summary>
    private async Task CheckForUpdateAsync()
    {
        var result = await _updates.CheckAsync();
        if (result.Error is not null)
        {
            _logger.LogDebug("Update-Check beim Start ohne Ergebnis: {Error}", result.Error);
            return;
        }

        if (result.UpdateAvailable)
        {
            AppendLog(LogEntry.Info(
                Loc.T("Update_AvailableHint", result.LatestVersion, result.CurrentVersion)));
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshRepos))]
    public async Task RefreshReposAsync()
    {
        try
        {
            RepoListStatus = Loc.T("Repos_Loading");
            AppendLog(LogEntry.Info(Loc.T("Log_RefreshingRepos")));
            var previouslySelected = new HashSet<string>(
                Repos.Where(r => r.IsSelected).Select(r => r.FullName),
                StringComparer.OrdinalIgnoreCase);

            var list = await _github.ListUserReposAsync(GithubAccount);
            list = list.Where(r => _settings.ShowArchivedRepos || !r.IsArchived).ToList();
            list = list.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

            PopulateFromCache(list, previouslySelected);
            _cache.Save(list, previouslySelected);
            RepoListStatus = Loc.T("Repos_Loaded", list.Count, GithubAccount);
            AppendLog(LogEntry.Info(Loc.T("Log_ReposRefreshed", list.Count)));
        }
        catch (GithubRateLimitedException ex)
        {
            RepoListStatus = Loc.T("Repos_RateLimited");
            AppendLog(LogEntry.Error(ex.Message));
        }
        catch (Exception ex)
        {
            RepoListStatus = Loc.T("Repos_LoadError");
            AppendLog(LogEntry.Error(Loc.T("Log_ReposLoadFailed", ex.Message)));
            _logger.LogWarning(ex, "Repo-Liste konnte nicht geladen werden");
        }
    }

    private void PopulateFromCache(IReadOnlyList<RepoInfo> repos, HashSet<string> selected)
    {
        Repos.Clear();
        foreach (var repo in repos)
        {
            var item = new RepoItemViewModel(repo, selected.Contains(repo.FullName));
            item.SelectionChanged += OnRepoSelectionChanged;
            Repos.Add(item);
        }
        RecalculateSelectedCount();
    }

    private void OnRepoSelectionChanged(object? sender, EventArgs e)
    {
        RecalculateSelectedCount();
        // Selection sofort persistieren
        var selected = Repos.Where(r => r.IsSelected).Select(r => r.FullName).ToList();
        var repos = Repos.Select(r => r.Repo).ToList();
        try { _cache.Save(repos, selected); }
        catch (Exception ex) { _logger.LogDebug(ex, "Konnte Selektion nicht speichern"); }
    }

    private void RecalculateSelectedCount() => SelectedCount = Repos.Count(r => r.IsSelected);

    private bool CanBuild() => !IsBusy && SelectedCount > 0;

    private bool CanRefreshRepos() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanBuild))]
    public async Task BuildBundleAsync()
    {
        var selectedRepos = Repos.Where(r => r.IsSelected).ToList();
        if (selectedRepos.Count == 0) return;

        IsBusy = true;
        _bundleCts = new CancellationTokenSource();
        try
        {
            LogEntries.Clear();
            Progress = 0;
            StatusText = Loc.T("Status_BundleStarting");

            var probe = await _sdk.ProbeAsync(_settings.CachedDotnetPath, _bundleCts.Token);
            if (probe.Status == SdkStatus.Missing || probe.ExecutablePath is null)
            {
                AppendLog(LogEntry.Error(Loc.T("Log_SdkNotFound")));
                StatusText = Loc.T("Status_SdkMissing");
                return;
            }
            AppendLog(LogEntry.Info(Loc.T("Log_SdkFound", probe.ExecutablePath, probe.Version)));

            var config = new BundleConfig
            {
                SelectedRepos = selectedRepos.Select(r => r.FullName).ToList(),
                TargetRids = _settings.TargetRids,
                OutputDirectory = _settings.OutputDirectory,
                WorkDirectory = _settings.WorkDirectory,
            };
            var branchMap = selectedRepos.ToDictionary(r => r.FullName, r => r.DefaultBranch, StringComparer.OrdinalIgnoreCase);

            var progress = new Progress<BundleProgress>(HandleProgress);
            var result = await _orchestrator.BuildAsync(config, branchMap, probe.ExecutablePath, progress, _bundleCts.Token);

            if (result.Success)
            {
                StatusText = Loc.T("Status_Done", result.PackageCount, result.TotalSizeBytes / 1024.0 / 1024.0);
                AppendLog(LogEntry.Info(StatusText));
            }
            else
            {
                StatusText = Loc.T("Status_Failed", result.ErrorMessage);
                AppendLog(LogEntry.Error(StatusText));
            }
        }
        catch (OperationCanceledException)
        {
            // Kein Fehler, sondern die Antwort auf den Abbrechen-Button. Ohne
            // eigenen catch landete das im Exception-Zweig darunter und meldete
            // dem Nutzer einen "unerwarteten Fehler" fuer seine eigene Aktion.
            StatusText = Loc.T("Status_Cancelled");
            AppendLog(LogEntry.Warn(StatusText));
        }
        catch (Exception ex)
        {
            AppendLog(LogEntry.Error(Loc.T("Log_UnexpectedError", ex.Message)));
            _logger.LogError(ex, "Bundle-Bau fehlgeschlagen");
        }
        finally
        {
            _bundleCts?.Dispose();
            _bundleCts = null;
            IsBusy = false;
        }
    }

    private void HandleProgress(BundleProgress p)
    {
        void Apply()
        {
            if (p.Percent >= 0) Progress = p.Percent;
            if (!string.IsNullOrEmpty(p.StatusText)) StatusText = p.StatusText;
            if (p.LogEntry is not null) LogEntries.Add(p.LogEntry);
        }
        if (Dispatcher.UIThread.CheckAccess()) Apply();
        else Dispatcher.UIThread.Post(Apply);
    }

    private bool CanCancelBuild() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancelBuild))]
    public void CancelBuild()
    {
        _logger.LogInformation("Bundle-Bau vom Nutzer abgebrochen.");
        _bundleCts?.Cancel();
    }

    private void AppendLog(LogEntry entry)
    {
        if (Dispatcher.UIThread.CheckAccess()) LogEntries.Add(entry);
        else Dispatcher.UIThread.Post(() => LogEntries.Add(entry));
    }

    partial void OnOutputDirectoryChanged(string value)
    {
        _settings.OutputDirectory = value;
        try { _settingsService.Save(_settings); } catch { /* ignorieren */ }
    }
}
