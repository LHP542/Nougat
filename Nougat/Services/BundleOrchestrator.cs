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
            var allPackages = perRepo.Values.SelectMany(v => v).ToList();

            Report(progress, BundlePhase.Deduplicating, 0.35, "Dedupliziere Pakete");
            var dedup = _deduplicator.Deduplicate(allPackages);
            allWarnings.AddRange(dedup.Warnings);
            foreach (var conflict in dedup.Conflicts)
            {
                var chosenRepos = string.Join(", ", conflict.ChosenRepos);
                var discarded = string.Join(" | ",
                    conflict.DiscardedSources.Select(d => $"{d.Version} <- {string.Join(", ", d.Repos)}"));
                Log(progress, LogEntry.Warn(
                    $"Konflikt: {conflict.PackageId} -> {conflict.ChosenVersion} " +
                    $"(aus: {chosenRepos}; verworfen: {discarded})"));
            }

            Report(progress, BundlePhase.Restoring, 0.4, $"Erzeuge Anker-Projekt ({dedup.Packages.Count} Pakete)");
            if (Directory.Exists(config.WorkDirectory))
                Directory.Delete(config.WorkDirectory, recursive: true);
            _anchor.WriteToWorkDirectory(config.WorkDirectory, dedup.Packages, out var anchorPath);
            Directory.CreateDirectory(hierCache);

            Report(progress, BundlePhase.Restoring, 0.45, "dotnet restore ...");
            var restored = await _restore.RestoreAsync(
                dotnetPath, anchorPath, hierCache, config.TargetRids,
                onLine: line => Log(progress, ToLogEntry(line)),
                ct: ct
            ).ConfigureAwait(false);
            if (!restored)
                return Fail(sw, "dotnet restore fehlgeschlagen. Log oben prüfen.");

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

    private async Task<Dictionary<string, List<PackageRef>>> AnalyzeAllAsync(
        IReadOnlyList<string> selectedRepos,
        IReadOnlyDictionary<string, string> repoBranchMap,
        IProgress<BundleProgress>? progress,
        CancellationToken ct)
    {
        using var throttle = new SemaphoreSlim(4);
        var results = new Dictionary<string, List<PackageRef>>(StringComparer.OrdinalIgnoreCase);
        var lockObj = new object();

        var done = 0;
        var total = selectedRepos.Count;

        var tasks = selectedRepos.Select(async fullName =>
        {
            await throttle.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var branch = repoBranchMap.TryGetValue(fullName, out var b) ? b : "main";
                var pkgs = await _analyzer.AnalyzeAsync(fullName, branch, ct).ConfigureAwait(false);
                lock (lockObj)
                {
                    results[fullName] = pkgs;
                    done++;
                    Report(progress, BundlePhase.Analyzing, 0.05 + 0.3 * done / total,
                        $"Analyse {done}/{total}: {fullName} ({pkgs.Count} Pakete)");
                    Log(progress, LogEntry.Info($"{fullName}: {pkgs.Count} PackageReferences"));
                }
            }
            catch (Exception ex)
            {
                lock (lockObj)
                {
                    results[fullName] = [];
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
