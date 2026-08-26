using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nougat.Models;

namespace Nougat.Services;

/// <summary>
/// Fassade für den kompletten Bundle-Bau: Analyze -> Dedup -> SDK-Check -> Anchor
/// -> Restore -> Assemble -> Configs. Fortschritt via IProgress an das UI.
/// </summary>
public sealed class BundleOrchestrator
{
    private readonly CsprojAnalyzer _analyzer;
    private readonly PackageDeduplicator _deduplicator;
    private readonly DotnetSdkService _sdk;
    private readonly AnchorProjectGenerator _anchor;
    private readonly RestoreRunner _restore;
    private readonly BundleAssembler _assembler;
    private readonly NugetConfigWriter _configWriter;
    private readonly GithubRepoService _github;
    private readonly ILogger<BundleOrchestrator> _logger;

    public BundleOrchestrator(
        CsprojAnalyzer analyzer,
        PackageDeduplicator deduplicator,
        DotnetSdkService sdk,
        AnchorProjectGenerator anchor,
        RestoreRunner restore,
        BundleAssembler assembler,
        NugetConfigWriter configWriter,
        GithubRepoService github,
        ILogger<BundleOrchestrator> logger)
    {
        _analyzer = analyzer;
        _deduplicator = deduplicator;
        _sdk = sdk;
        _anchor = anchor;
        _restore = restore;
        _assembler = assembler;
        _configWriter = configWriter;
        _github = github;
        _logger = logger;
    }

    /// <summary>Führt den kompletten Bundle-Lauf aus. Wirft nur bei fatalen Fehlern.</summary>
    public async Task<BundleResult> BuildAsync(
        BundleConfig config,
        IReadOnlyDictionary<string, string> repoBranchMap,
        string dotnetPath,
        IProgress<BundleProgress>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var allWarnings = new List<string>();
        var hierCache = Path.Combine(config.WorkDirectory, "packages-cache");

        try
        {
            Report(progress, BundlePhase.CheckingSdk, 0.02, "Prüfe .NET-SDK");
            if (!File.Exists(dotnetPath))
                return Fail(sw, "Kein .NET-SDK verfügbar. Bitte in den Einstellungen installieren.");

            Report(progress, BundlePhase.Analyzing, 0.05, "Analysiere Repos ...");
            var perRepo = await AnalyzeAllAsync(config.SelectedRepos, repoBranchMap, progress, ct).ConfigureAwait(false);
            var allPackages = perRepo.Values.SelectMany(v => v.Packages).ToList();
            var allTfms = perRepo.Values
                .SelectMany(v => v.TargetFrameworks)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Der globale Dedup dient nur noch der Übersicht: Welche Pakete
            // brauchen die Repos in unterschiedlichen Versionen? Gefiltert wird
            // damit NICHTS mehr — jedes Repo restored gegen seine eigenen Versionen.
            Report(progress, BundlePhase.Deduplicating, 0.35, "Prüfe Versionsunterschiede");
            var dedup = _deduplicator.Deduplicate(allPackages);
            allWarnings.AddRange(dedup.Warnings);
            foreach (var conflict in dedup.Conflicts)
            {
                var alle = new[] { $"{conflict.ChosenVersion} <- {string.Join(", ", conflict.ChosenRepos)}" }
                    .Concat(conflict.DiscardedSources
                        .Select(d => $"{d.Version} <- {string.Join(", ", d.Repos)}"));
                Log(progress, LogEntry.Info(
                    $"{conflict.PackageId} wird in mehreren Versionen gebraucht "
                    + $"({string.Join(" | ", alle)}) - alle landen im Bundle"));
            }

            Report(progress, BundlePhase.Restoring, 0.4,
                $"Erzeuge Anker-Projekte ({perRepo.Count} Repos)");
            if (Directory.Exists(config.WorkDirectory))
                Directory.Delete(config.WorkDirectory, recursive: true);
            Directory.CreateDirectory(hierCache);

            // EIN ANKER PRO REPO, gemeinsamer Paket-Cache.
            //
            // WARUM NICHT ein gemeinsamer Anker mit der deduplizierten Liste: Ein
            // Projekt kann pro Paket nur EINE Version referenzieren. Der Dedup
            // ("hoechste gewinnt") warf damit genau die Versionen weg, die ein
            // anderes Repo bewusst pinnt — real: Checkmk pinnt
            // Tmds.DBus.Protocol auf 0.94.1, weil Avalonia 12.1.0 sonst eine
            // MissingTypeException wirft; Klemmbrett-Scaffold will 0.94.2, und im
            // Bundle landete nur 0.94.2. Offline baut Checkmk dann trotzdem
            // durch — NuGet loest mit NU1603 still auf die naechsthoehere Version
            // auf — und faellt erst zur Laufzeit um.
            //
            // Pro Repo restored bekommt jedes Repo exakt seine Versionen samt
            // eigener transitiver Aufloesung und eigener TFMs. Der gemeinsame
            // Cache sammelt die Vereinigungsmenge.
            var anchorRoot = Path.Combine(config.WorkDirectory, "anchors");
            var repoNames = perRepo.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            var restoredRepos = 0;

            for (var i = 0; i < repoNames.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var repoName = repoNames[i];
                var analysis = perRepo[repoName];
                if (analysis.Packages.Count == 0)
                {
                    Log(progress, LogEntry.Warn($"{repoName}: keine Pakete gefunden - übersprungen"));
                    continue;
                }

                // Innerhalb eines Repos kann dasselbe Paket aus mehreren csproj
                // kommen; doppelte PackageReference-Eintraege waeren NU1504.
                var repoPackages = _deduplicator.Deduplicate(analysis.Packages).Packages;
                var repoDir = Path.Combine(anchorRoot, Slug(repoName));

                _anchor.WriteToWorkDirectory(
                    repoDir, repoPackages, out var anchorPath, analysis.TargetFrameworks);

                var tfmText = analysis.TargetFrameworks.Count == 0
                    ? "net10.0 (Rückfall)"
                    : string.Join(";", analysis.TargetFrameworks.OrderBy(x => x));
                Log(progress, LogEntry.Info(
                    $"{repoName}: Anker mit {repoPackages.Count} Paketen, TFMs {tfmText}"));

                Report(progress, BundlePhase.Restoring,
                    0.4 + 0.45 * (i + 1) / repoNames.Count,
                    $"restore {i + 1}/{repoNames.Count}: {repoName}");

                var ok = await _restore.RestoreAsync(
                    dotnetPath, anchorPath, hierCache, config.TargetRids,
                    onLine: line => Log(progress, ToLogEntry(line)),
                    ct: ct
                ).ConfigureAwait(false);

                if (!ok)
                    return Fail(sw, $"dotnet restore fehlgeschlagen für {repoName}. Log oben prüfen.");

                restoredRepos++;
            }

            if (restoredRepos == 0)
                return Fail(sw, "Kein einziges Repo lieferte Pakete - Bundle wäre leer.");

            Report(progress, BundlePhase.Assembling, 0.85, "Kopiere .nupkg in Zielordner");
            var assemble = _assembler.Assemble(hierCache, config.OutputDirectory,
                onProgress: n => Report(progress, BundlePhase.Assembling, 0.85 + Math.Min(0.1, n / 5000.0), $"{n} .nupkg kopiert"));

            Report(progress, BundlePhase.WritingConfigs, 0.97, "Schreibe nuget.config.windows + README");
            _configWriter.Write(
                config.OutputDirectory, config.SelectedRepos,
                assemble.PackageCount, assemble.TotalSizeBytes, dedup.Conflicts);

            sw.Stop();
            Report(progress, BundlePhase.Done, 1.0,
                $"Fertig: {assemble.PackageCount} Pakete, {assemble.TotalSizeBytes / 1024.0 / 1024.0:F1} MB");

            return new BundleResult
            {
                Success = true,
                PackageCount = assemble.PackageCount,
                TotalSizeBytes = assemble.TotalSizeBytes,
                Duration = sw.Elapsed,
                Warnings = allWarnings,
                ResolvedPackages = dedup.Packages,
                Conflicts = dedup.Conflicts,
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Bundle-Bau vom Benutzer abgebrochen.");
            Report(progress, BundlePhase.Failed, 0, "Abgebrochen");
            return Fail(sw, "Abgebrochen.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bundle-Bau fehlgeschlagen");
            Report(progress, BundlePhase.Failed, 0, "Fehlgeschlagen: " + ex.Message);
            return Fail(sw, ex.Message);
        }
    }

    /// <summary>Repo-Name in einen Ordnernamen umsetzen ("LHP542/Checkmk" -> "LHP542_Checkmk").</summary>
    private static string Slug(string ownerRepo)
    {
        var chars = ownerRepo.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c is '/' or '\\'
            ? '_' : c).ToArray();
        return new string(chars);
    }

    private async Task<Dictionary<string, RepoAnalysis>> AnalyzeAllAsync(
        IReadOnlyList<string> selectedRepos,
        IReadOnlyDictionary<string, string> repoBranchMap,
        IProgress<BundleProgress>? progress,
        CancellationToken ct)
    {
        using var throttle = new SemaphoreSlim(4);
        var results = new Dictionary<string, RepoAnalysis>(StringComparer.OrdinalIgnoreCase);
        var lockObj = new object();

        var done = 0;
        var total = selectedRepos.Count;

        var tasks = selectedRepos.Select(async fullName =>
        {
            await throttle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var branch = repoBranchMap.TryGetValue(fullName, out var b) ? b : "main";
                var analysis = await _analyzer.AnalyzeRepoAsync(fullName, branch, ct).ConfigureAwait(false);
                lock (lockObj)
                {
                    results[fullName] = analysis;
                    done++;
                    var count = analysis.Packages.Count;
                    Report(progress, BundlePhase.Analyzing, 0.05 + 0.3 * done / total,
                        $"Analyse {done}/{total}: {fullName} ({count} Pakete)");
                    Log(progress, LogEntry.Info(
                        $"{fullName}: {count} Pakete, TFMs: "
                        + (analysis.TargetFrameworks.Count == 0
                            ? "keine erkannt"
                            : string.Join(";", analysis.TargetFrameworks))));
                }
            }
            catch (Exception ex)
            {
                lock (lockObj)
                {
                    // Leeres Ergebnis: Ein kaputtes Repo darf den Bundle-Bau nicht
                    // abbrechen, die Fehlerzeile steht im Log.
                    results[fullName] = new RepoAnalysis([], []);
                    done++;
                    Log(progress, LogEntry.Error($"{fullName}: Analyse fehlgeschlagen - {ex.Message}"));
                }
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private static void Report(IProgress<BundleProgress>? progress, BundlePhase phase, double percent, string text)
        => progress?.Report(new BundleProgress(phase, percent, text));

    private static void Log(IProgress<BundleProgress>? progress, LogEntry entry)
        => progress?.Report(new BundleProgress(BundlePhase.Restoring, -1, "", entry));

    private static LogEntry ToLogEntry(ProgressLine line) =>
        line.Stream == ProcessStream.StdErr
            ? LogEntry.Warn(line.Line)
            : LogEntry.Debug(line.Line);

    private static BundleResult Fail(Stopwatch sw, string message)
    {
        sw.Stop();
        return new BundleResult
        {
            Success = false,
            Duration = sw.Elapsed,
            ErrorMessage = message,
        };
    }
}
