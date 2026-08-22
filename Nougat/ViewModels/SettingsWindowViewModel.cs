using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nougat.Localization;
using Nougat.Models;
using Nougat.Services;

namespace Nougat.ViewModels;

public partial class SettingsWindowViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<SettingsWindowViewModel> _logger;

    public event EventHandler? CloseRequested;

    [ObservableProperty] public partial string Pat { get; set; } = "";
    [ObservableProperty] public partial string OutputDirectory { get; set; } = "";
    [ObservableProperty] public partial string WorkDirectory { get; set; } = "";
    [ObservableProperty] public partial int RepoCacheTtlHours { get; set; }
    [ObservableProperty] public partial bool ShowArchivedRepos { get; set; }
    [ObservableProperty] public partial bool RidWinX64 { get; set; }
    [ObservableProperty] public partial bool RidLinuxX64 { get; set; }
    [ObservableProperty] public partial bool RidOsxX64 { get; set; }

    /// <summary>Auswahlliste im Format "🇩🇪 Deutsch" — Länderflaggen sind Kroste-Standard.</summary>
    public IReadOnlyList<LanguageChoice> Languages { get; } =
        LocalizationService.SupportedCultures
            .Select(c => new LanguageChoice(c.Iso, $"{c.Flag} {c.Display}"))
            .ToList();

    private LanguageChoice _selectedLanguage = null!;

    /// <summary>
    /// Wechselt die Sprache SOFORT in allen Fenstern — kein Neustart-Hinweis.
    /// Persistiert wird sie erst beim Speichern; wer abbricht, behält also die
    /// Vorschau, bis er das Fenster erneut öffnet. Das ist gewollt: Man will
    /// sehen, was man wählt.
    /// </summary>
    public LanguageChoice SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value) && value is not null)
                LocalizationService.Instance.SetCulture(value.Iso);
        }
    }

    public SettingsWindowViewModel(
        SettingsService settingsService,
        AppSettings settings,
        ISecretStore secretStore,
        ILogger<SettingsWindowViewModel> logger)
    {
        _settingsService = settingsService;
        _settings = settings;
        _secretStore = secretStore;
        _logger = logger;

        Pat = _secretStore.Unprotect(_settings.EncryptedPat) ?? "";
        OutputDirectory = _settings.OutputDirectory;
        WorkDirectory = _settings.WorkDirectory;
        RepoCacheTtlHours = _settings.RepoCacheTtlHours;
        ShowArchivedRepos = _settings.ShowArchivedRepos;
        RidWinX64 = _settings.TargetRids.Contains("win-x64");
        RidLinuxX64 = _settings.TargetRids.Contains("linux-x64");
        RidOsxX64 = _settings.TargetRids.Contains("osx-x64");

        var iso = _settings.UiCulture ?? LocalizationService.Instance.CurrentIso;
        _selectedLanguage = Languages.FirstOrDefault(l => l.Iso == iso) ?? Languages[0];
    }

    [RelayCommand]
    private void Save()
    {
        _settings.EncryptedPat = _secretStore.Protect(Pat);
        _settings.OutputDirectory = OutputDirectory;
        _settings.WorkDirectory = WorkDirectory;
        _settings.RepoCacheTtlHours = Math.Max(1, RepoCacheTtlHours);
        _settings.ShowArchivedRepos = ShowArchivedRepos;
        _settings.TargetRids.Clear();
        if (RidWinX64) _settings.TargetRids.Add("win-x64");
        if (RidLinuxX64) _settings.TargetRids.Add("linux-x64");
        if (RidOsxX64) _settings.TargetRids.Add("osx-x64");
        if (_settings.TargetRids.Count == 0) _settings.TargetRids.Add("win-x64");

        _settings.UiCulture = SelectedLanguage.Iso;

        _settingsService.Save(_settings);
        MaskingLayoutRenderer.Register(Pat);
        _logger.LogInformation("Einstellungen gespeichert");
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        // Live-Vorschau der Sprache zuruecknehmen, wenn nicht gespeichert wird.
        var persisted = _settings.UiCulture;
        if (persisted is not null)
            LocalizationService.Instance.SetCulture(persisted);

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Ein Eintrag der Sprachauswahl.</summary>
public sealed record LanguageChoice(string Iso, string Display);
