using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nougat.Models;

namespace Nougat.Services;

/// <summary>
/// Persistiert AppSettings unter $XDG_CONFIG_HOME/Nougat/settings.json (Fallback ~/.config).
/// Atomar (tmp + File.Move) und tolerant bei kaputtem JSON (Backup + Defaults).
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<SettingsService> _logger;
    private readonly string _path;

    public SettingsService(ILogger<SettingsService> logger)
        : this(logger, PathProvider.SettingsFilePath) { }

    // Für Tests: eigener Pfad
    public SettingsService(ILogger<SettingsService> logger, string path)
    {
        _logger = logger;
        _path = path;
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                _logger.LogInformation("Keine Settings gefunden, verwende Defaults ({Path})", _path);
                return Defaults();
            }

            var json = File.ReadAllText(_path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, _json) ?? Defaults();
            ApplyDefaultsForEmpty(settings);
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings-Datei defekt, wird umbenannt zu .broken");
            try
            {
                File.Move(_path, _path + ".broken", overwrite: true);
            }
            catch (Exception moveEx)
            {
                _logger.LogWarning(moveEx, "Konnte defekte Settings-Datei nicht wegsichern");
            }
            return Defaults();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(settings, _json);
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
        _logger.LogDebug("Settings gespeichert ({Path})", _path);
    }

    private static AppSettings Defaults() => new()
    {
        OutputDirectory = PathProvider.DefaultOutputDirectory,
        WorkDirectory = PathProvider.DefaultWorkDirectory,
    };

    private static void ApplyDefaultsForEmpty(AppSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.OutputDirectory))
            s.OutputDirectory = PathProvider.DefaultOutputDirectory;
        if (string.IsNullOrWhiteSpace(s.WorkDirectory))
            s.WorkDirectory = PathProvider.DefaultWorkDirectory;
        if (s.TargetRids.Count == 0)
            s.TargetRids.Add("win-x64");
    }
}
