using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Nougat.Models;

namespace Nougat.Services;

public sealed record ProcessResult(int ExitCode, TimeSpan Duration);

/// <summary>
/// Startet einen Prozess und streamt stdout/stderr live. Cancellation killt den
/// Prozess. Nachbau des Musters aus Allpaca (Services/ProcessRunner.cs).
/// </summary>
public sealed class ProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(ILogger<ProcessRunner> logger)
    {
        _logger = logger;
    }

    public async IAsyncEnumerable<ProgressLine> StreamAsync(
        string fileName,
        IEnumerable<string> args,
        string? workingDirectory = null,
        IDictionary<string, string?>? env = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        if (env is not null)
            foreach (var (k, v) in env)
                psi.Environment[k] = v;

        _logger.LogDebug("Starte {File} {Args}", fileName, string.Join(' ', psi.ArgumentList));

        using var proc = new Process { StartInfo = psi };
        var channel = Channel.CreateUnbounded<ProgressLine>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) channel.Writer.TryWrite(new ProgressLine(ProcessStream.StdOut, e.Data));
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) channel.Writer.TryWrite(new ProgressLine(ProcessStream.StdErr, e.Data));
        };

        if (!proc.Start())
            throw new InvalidOperationException($"Prozess {fileName} konnte nicht gestartet werden.");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        // Registriere Cancellation → Prozess killen
        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
            catch { /* ignorieren */ }
        });

        // Prozess-Ende signalisiert Channel-Complete
        _ = proc.WaitForExitAsync(ct).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        await foreach (var line in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            yield return line;
        }
    }

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> args,
        Action<ProgressLine>? onLine = null,
        string? workingDirectory = null,
        IDictionary<string, string?>? env = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);
        if (env is not null)
            foreach (var (k, v) in env)
                psi.Environment[k] = v;

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) onLine?.Invoke(new ProgressLine(ProcessStream.StdOut, e.Data));
        };
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null) onLine?.Invoke(new ProgressLine(ProcessStream.StdErr, e.Data));
        };

        var sw = Stopwatch.StartNew();
        if (!proc.Start())
            throw new InvalidOperationException($"Prozess {fileName} konnte nicht gestartet werden.");
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
            catch { /* ignorieren */ }
        });

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        sw.Stop();

        return new ProcessResult(proc.ExitCode, sw.Elapsed);
    }
}
