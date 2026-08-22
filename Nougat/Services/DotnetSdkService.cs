using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nougat.Models;

namespace Nougat.Services;

public enum SdkStatus { OnPath, ManagedInstall, Missing }

public sealed record SdkProbe(SdkStatus Status, string? ExecutablePath, string? Version);

/// <summary>
/// Prüft ob ein passendes .NET-10-SDK verfügbar ist. Wenn nicht, kann das
/// SDK per dotnet-install-Skript nach <see cref="PathProvider.ManagedDotnetDirectory"/>
/// gebootstrapt werden — bewusst nicht ~/.dotnet, damit das globale SDK unangetastet bleibt.
/// </summary>
public sealed class DotnetSdkService
{
    private static readonly Regex _sdkLine = new(@"^(?<major>\d+)\.(?<minor>\d+)\.\d+", RegexOptions.Compiled);
    private const string LinuxInstaller = "https://dot.net/v1/dotnet-install.sh";
    private const string WindowsInstaller = "https://dot.net/v1/dotnet-install.ps1";
    private const int DesiredMajor = 10;

    private readonly ILogger<DotnetSdkService> _logger;
    private readonly ProcessRunner _processRunner;
    private readonly IHttpClientFactory _httpFactory;

    public DotnetSdkService(
        ILogger<DotnetSdkService> logger,
        ProcessRunner processRunner,
        IHttpClientFactory httpFactory)
    {
        _logger = logger;
        _processRunner = processRunner;
        _httpFactory = httpFactory;
    }

    public async Task<SdkProbe> ProbeAsync(string? cachedPath = null, CancellationToken ct = default)
    {
        // 1) Cache-Pfad
        if (!string.IsNullOrWhiteSpace(cachedPath) && File.Exists(cachedPath))
        {
            var v = await GetSdkMajorAsync(cachedPath, ct).ConfigureAwait(false);
            if (v is not null && v.Major == DesiredMajor)
                return new SdkProbe(SdkStatus.ManagedInstall, cachedPath, v.ToString());
        }

        // 2) PATH
        var onPath = ResolveFromPath();
        if (onPath is not null)
        {
            var v = await GetSdkMajorAsync(onPath, ct).ConfigureAwait(false);
            if (v is not null && v.Major == DesiredMajor)
                return new SdkProbe(SdkStatus.OnPath, onPath, v.ToString());
        }

        // 3) Managed install (~/.dotnet-nougat)
        var managed = PathProvider.ManagedDotnetExecutable;
        if (File.Exists(managed))
        {
            var v = await GetSdkMajorAsync(managed, ct).ConfigureAwait(false);
            if (v is not null && v.Major == DesiredMajor)
                return new SdkProbe(SdkStatus.ManagedInstall, managed, v.ToString());
        }

        return new SdkProbe(SdkStatus.Missing, null, null);
    }

    /// <summary>Lädt und startet dotnet-install; installiert SDK 10 nach ManagedDotnetDirectory.</summary>
    public async Task<string> InstallAsync(
        string channel,
        IProgress<double>? progress,
        Action<string>? onLog,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(PathProvider.ManagedDotnetDirectory);

        var isWindows = OperatingSystem.IsWindows();
        var installerUrl = isWindows ? WindowsInstaller : LinuxInstaller;
        var installerPath = Path.Combine(Path.GetTempPath(),
            isWindows ? "nougat-dotnet-install.ps1" : "nougat-dotnet-install.sh");

        onLog?.Invoke($"Lade dotnet-install ({installerUrl}) ...");
        var client = _httpFactory.CreateClient(GithubRepoService.HttpClientName);
        var payload = await client.GetStringAsync(installerUrl, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(installerPath, payload, ct).ConfigureAwait(false);

        if (!isWindows)
        {
            try { File.SetUnixFileMode(installerPath, UnixFileMode.UserExecute | UnixFileMode.UserRead | UnixFileMode.UserWrite); }
            catch { /* ignorieren */ }
        }

        var installerArgs = isWindows
            ? new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", installerPath,
                      "-Channel", channel, "-InstallDir", PathProvider.ManagedDotnetDirectory }
            : new[] { installerPath, "--channel", channel, "--install-dir", PathProvider.ManagedDotnetDirectory };

        var fileName = isWindows ? "powershell" : "bash";

        onLog?.Invoke($"Installiere .NET SDK {channel} nach {PathProvider.ManagedDotnetDirectory} ...");
        var result = await _processRunner.RunAsync(
            fileName, installerArgs,
            onLine: line =>
            {
                onLog?.Invoke(line.Line);
                var m = Regex.Match(line.Line, @"(?<p>\d{1,3})\s*%");
                if (m.Success && double.TryParse(m.Groups["p"].Value, out var p))
                    progress?.Report(Math.Clamp(p / 100.0, 0, 1));
            },
            ct: ct
        ).ConfigureAwait(false);

        try { File.Delete(installerPath); } catch { /* ignorieren */ }

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"dotnet-install fehlgeschlagen (ExitCode {result.ExitCode}).");

        onLog?.Invoke($"Installation abgeschlossen ({result.Duration.TotalSeconds:F1} s).");
        return PathProvider.ManagedDotnetExecutable;
    }

    private static string? ResolveFromPath()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;
        var exe = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir, exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* ignorieren */ }
        }
        return null;
    }

    private async Task<Version?> GetSdkMajorAsync(string dotnetPath, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo(dotnetPath, ["--list-sdks"])
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);

            Version? highest = null;
            foreach (var line in output.Split('\n'))
            {
                var m = _sdkLine.Match(line.Trim());
                if (!m.Success) continue;
                var major = int.Parse(m.Groups["major"].Value);
                var minor = int.Parse(m.Groups["minor"].Value);
                var v = new Version(major, minor);
                if (highest is null || v > highest) highest = v;
            }
            return highest;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Konnte SDK-Version für {Path} nicht ermitteln", dotnetPath);
            return null;
        }
    }
}
