using Microsoft.Extensions.Logging;
using Nougat.Models;

namespace Nougat.Services;

/// <summary>
/// Führt "dotnet restore" für das Anker-Projekt aus - portable + je RID.
/// Live-Ausgabe geht ans UI und parallel als Debug/Warn ins NLog-File.
/// Bei Fehlschlag werden die letzten Ausgabezeilen als Error ins Log geschrieben,
/// damit die tatsächliche Ursache auch offline nachvollziehbar ist.
/// </summary>
public sealed class RestoreRunner
{
    private readonly ProcessRunner _processRunner;
    private readonly ILogger<RestoreRunner> _logger;

    public RestoreRunner(ProcessRunner processRunner, ILogger<RestoreRunner> logger)
    {
        _processRunner = processRunner;
        _logger = logger;
    }

    public async Task<bool> RestoreAsync(
        string dotnetPath,
        string anchorPath,
        string packagesDirectory,
        IEnumerable<string> targetRids,
        Action<ProgressLine>? onLine,
        CancellationToken ct = default)
    {
        if (!await RunPhaseAsync(
                "portable",
                dotnetPath,
                ["restore", anchorPath, "--packages", packagesDirectory, "--verbosity", "minimal"],
                onLine, ct).ConfigureAwait(false))
        {
            return false;
        }

        foreach (var rid in targetRids)
        {
            if (!await RunPhaseAsync(
                    rid,
                    dotnetPath,
                    ["restore", anchorPath, "--packages", packagesDirectory, "--runtime", rid, "--verbosity", "minimal"],
                    onLine, ct).ConfigureAwait(false))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> RunPhaseAsync(
        string phase,
        string dotnetPath,
        string[] args,
        Action<ProgressLine>? onLine,
        CancellationToken ct)
    {
        _logger.LogInformation("Restore {Phase} ...", phase);
        var tail = new Queue<string>(capacity: 30);

        var result = await _processRunner.RunAsync(
            dotnetPath, args,
            onLine: line =>
            {
                onLine?.Invoke(line);
                if (line.Stream == ProcessStream.StdErr)
                    _logger.LogWarning("dotnet[{Phase}]: {Line}", phase, line.Line);
                else
                    _logger.LogDebug("dotnet[{Phase}]: {Line}", phase, line.Line);

                tail.Enqueue(line.Line);
                if (tail.Count > 30) tail.Dequeue();
            },
            ct: ct
        ).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            _logger.LogError(
                "Restore {Phase} fehlgeschlagen (ExitCode {Code}). Letzte Ausgabe:\n{Tail}",
                phase, result.ExitCode, string.Join('\n', tail));
            return false;
        }
        return true;
    }
}
