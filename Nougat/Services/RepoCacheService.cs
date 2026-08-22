using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nougat.Models;

namespace Nougat.Services;

/// <summary>
/// Persistiert die Repo-Liste + Selektion unter ~/.config/Nougat/repos.cache.json.
/// TTL wird vom Aufrufer geprüft — dieser Service kennt kein Zeitfenster,
/// er speichert/liefert nur den Cache.
/// </summary>
public sealed class RepoCacheService
{
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<RepoCacheService> _logger;
    private readonly string _path;

    public RepoCacheService(ILogger<RepoCacheService> logger)
        : this(logger, PathProvider.RepoCacheFilePath) { }

    public RepoCacheService(ILogger<RepoCacheService> logger, string path)
    {
        _logger = logger;
        _path = path;
    }

    public bool TryLoad(out RepoCache cache)
    {
        cache = new RepoCache();
        if (!File.Exists(_path)) return false;

        try
        {
            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<RepoCache>(json, _json);
            if (loaded is null) return false;
            cache = loaded;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Repo-Cache defekt, sichere nach .broken");
            try { File.Move(_path, _path + ".broken", overwrite: true); }
            catch { /* ignorieren */ }
            return false;
        }
    }

    public void Save(List<RepoInfo> repos, IEnumerable<string> selectedNames)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var cache = new RepoCache
        {
            FetchedAt = DateTime.UtcNow,
            Repos = repos,
            SelectedNames = [.. selectedNames],
        };
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cache, _json));
        File.Move(tmp, _path, overwrite: true);
        _logger.LogDebug("Repo-Cache gespeichert ({Count} Repos)", repos.Count);
    }

    public bool IsFresh(RepoCache cache, TimeSpan ttl) =>
        (DateTime.UtcNow - cache.FetchedAt) < ttl;
}
