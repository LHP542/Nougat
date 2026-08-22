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
using Nougat.Models;
using Nougat.Services;

namespace Nougat.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    /// <summary>
    /// GitHub-Konto, dessen Repos zur Auswahl stehen. Seit dem Umzug der
    /// dienstlichen Projekte die Organisation <c>LHP542</c> statt des
    /// persoenlichen Kontos — sonst listet Nougat die privaten Repos und
    /// findet genau die nicht, fuer die das Offline-Bundle gebaut wird.
    /// </summary>
    private const string GithubAccount = "LHP542";

    private readonly GithubRepoService _github;
    private readonly RepoCacheService _cache;
    private readonly BundleOrchestrator _orchestrator;
    private readonly DotnetSdkService _sdk;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly ILogger<MainWindowViewModel> _logger;

    private CancellationTokenSource? _bundleCts;

    public ObservableCollection<RepoItemViewModel> Repos { get; } = [];
    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    [ObservableProperty] public partial string OutputDirectory { get; set; } = "";
    [ObservableProperty] public partial string StatusText { get; set; } = "Bereit.";
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial bool IsBusy { get; set; }
    [ObservableProperty] public partial int SelectedCount { get; set; }
    [ObservableProperty] public partial string RepoListStatus { get; set; } = "";

    public string Title => "Nougat - Offline-NuGet-Bundle-Builder";

    public MainWindowViewModel(
        GithubRepoService github,
        RepoCacheService cache,
        BundleOrchestrator orchestrator,
        DotnetSdkService sdk,
        SettingsService settingsService,
        AppSettings settings,
        ILogger<MainWindowViewModel> logger)
    {
        _github = github;
        _cache = cache;
        _orchestrator = orchestrator;
        _sdk = sdk;
        _settingsService = settingsService;
        _settings = settings;
        _logger = logger;

        OutputDirectory = _settings.OutputDirectory;

        // Beim Start: Cache lesen, dann im Hintergrund refresh.
        _ = LoadInitialAsync();
    }

    private async Task LoadInitialAsync()
    {
        HashSet<string> selectedFromCache = new(StringComparer.OrdinalIgnoreCase);

        if (_cache.TryLoad(out var cached))
        {
            selectedFromCache = new HashSet<string>(cached.SelectedNames, StringComparer.OrdinalIgnoreCase);
            PopulateFromCache(cached.Repos, selectedFromCache);
            RepoListStatus = $"Cache: {cached.Repos.Count} Repos, Stand {cached.FetchedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
            if (_cache.IsFresh(cached, TimeSpan.FromHours(_settings.RepoCacheTtlHours)))
                return;
        }

        // Hintergrund-Refresh
        await RefreshReposAsync();
    }

    [RelayCommand]
    public async Task RefreshReposAsync()
    {
        try
        {
            RepoListStatus = "Lade Repo-Liste von GitHub ...";
            AppendLog(LogEntry.Info("Aktualisiere Repo-Liste ..."));
            var previouslySelected = new HashSet<string>(
                Repos.Where(r => r.IsSelected).Select(r => r.FullName),
                StringComparer.OrdinalIgnoreCase);

            var list = await _github.ListUserReposAsync(GithubAccount);
            list = list.Where(r => _settings.ShowArchivedRepos || !r.IsArchived).ToList();
            list = list.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

            PopulateFromCache(list, previouslySelected);
            _cache.Save(list, previouslySelected);
            RepoListStatus = $"{list.Count} Repos geladen ({GithubAccount})";
            AppendLog(LogEntry.Info($"{list.Count} Repos aktualisiert"));
        }
        catch (GithubRateLimitedException ex)
        {
            RepoListStatus = "Rate-Limit erreicht - PAT setzen empfohlen";
            AppendLog(LogEntry.Error(ex.Message));
        }
        catch (Exception ex)
        {
            RepoListStatus = "Fehler beim Laden";
            AppendLog(LogEntry.Error($"Repo-Liste konnte nicht geladen werden: {ex.Message}"));
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
        BuildBundleCommand.NotifyCanExecuteChanged();
        // Selection sofort persistieren
        var selected = Repos.Where(r => r.IsSelected).Select(r => r.FullName).ToList();
        var repos = Repos.Select(r => r.Repo).ToList();
        try { _cache.Save(repos, selected); }
        catch (Exception ex) { _logger.LogDebug(ex, "Konnte Selektion nicht speichern"); }
    }

    private void RecalculateSelectedCount() => SelectedCount = Repos.Count(r => r.IsSelected);

    private bool CanBuild() => !IsBusy && SelectedCount > 0;

    [RelayCommand(CanExecute = nameof(CanBuild))]
    public async Task BuildBundleAsync()
    {
        var selectedRepos = Repos.Where(r => r.IsSelected).ToList();
        if (selectedRepos.Count == 0) return;

        IsBusy = true;
        BuildBundleCommand.NotifyCanExecuteChanged();
        _bundleCts = new CancellationTokenSource();
        try
        {
            LogEntries.Clear();
            Progress = 0;
            StatusText = "Bundle-Bau startet ...";

            var probe = await _sdk.ProbeAsync(_settings.CachedDotnetPath, _bundleCts.Token);
            if (probe.Status == SdkStatus.Missing || probe.ExecutablePath is null)
            {
                AppendLog(LogEntry.Error(".NET 10 SDK nicht gefunden. Bitte in den Einstellungen installieren."));
                StatusText = "SDK fehlt.";
                return;
            }
            AppendLog(LogEntry.Info($".NET SDK gefunden: {probe.ExecutablePath} ({probe.Version})"));

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
                StatusText = $"Fertig: {result.PackageCount} Pakete ({result.TotalSizeBytes / 1024.0 / 1024.0:F1} MB)";
                AppendLog(LogEntry.Info(StatusText));
            }
            else
            {
                StatusText = "Fehlgeschlagen: " + result.ErrorMessage;
                AppendLog(LogEntry.Error(StatusText));
            }
        }
        catch (Exception ex)
        {
            AppendLog(LogEntry.Error("Unerwarteter Fehler: " + ex.Message));
            _logger.LogError(ex, "Bundle-Bau fehlgeschlagen");
        }
        finally
        {
            IsBusy = false;
            BuildBundleCommand.NotifyCanExecuteChanged();
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

    [RelayCommand]
    public void CancelBuild() => _bundleCts?.Cancel();

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
