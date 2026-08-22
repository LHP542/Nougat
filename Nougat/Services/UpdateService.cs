using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Nougat.Infrastructure;

namespace Nougat.Services;

/// <summary>Ergebnis eines Update-Checks.</summary>
public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleasePageUrl,
    bool AssetAvailable,
    string? Error)
{
    public static UpdateCheckResult Failed(string current, string error) =>
        new(false, current, null, null, false, error);
}

/// <summary>
/// Update-Check und echtes Self-Update gegen GitHub Releases (Kroste-Standard:
/// reine Benachrichtigung reicht nicht).
///
/// Besonderheit gegenüber der RenPack-Vorlage: Nougat lebt in einer privaten
/// Organisation. Dort ist browser_download_url NICHT anonym abrufbar — der
/// Download muss über die Asset-API-URL mit "Accept: application/octet-stream"
/// und dem PAT laufen. Der bestehende named HttpClient "github" bringt PAT und
/// System-Proxy bereits mit.
/// </summary>
public sealed class UpdateService
{
    private const string Owner = "LHP542";
    private const string Repo = "Nougat";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UpdateService> _logger;

    private UpdateCheckResult? _cached;
    private ReleaseAsset? _cachedAsset;

    public UpdateService(IHttpClientFactory httpClientFactory, ILogger<UpdateService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public static string ReleasesPageUrl => $"https://github.com/{Owner}/{Repo}/releases";

    /// <summary>
    /// Fragt das neueste Release ab. Fehler werden geloggt und als Ergebnis
    /// zurückgegeben, nie geworfen: Ein Update-Check darf die App nicht stören,
    /// wenn der Rechner offline ist oder der Proxy dazwischenfunkt.
    /// </summary>
    public async Task<UpdateCheckResult> CheckAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cached is not null)
            return _cached;

        var current = AppVersion.Current;
        var sw = Stopwatch.StartNew();
        var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

        try
        {
            _logger.LogDebug("Update-Check startet: {Url}", url);

            using var client = _httpClientFactory.CreateClient(GithubRepoService.HttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                // 404 heißt bei privaten Repos meist "kein Release" ODER "PAT fehlt".
                var hint = response.StatusCode == HttpStatusCode.NotFound
                    ? "Kein Release gefunden (bei privatem Repo: PAT in den Einstellungen prüfen)."
                    : $"GitHub antwortete mit {(int)response.StatusCode}.";
                _logger.LogWarning("Update-Check fehlgeschlagen: {Hint}", hint);
                return _cached = UpdateCheckResult.Failed(current, hint);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var release = JsonSerializer.Deserialize<ReleaseDto>(json);
            var tag = release?.TagName?.TrimStart('v', 'V');

            if (string.IsNullOrWhiteSpace(tag))
                return _cached = UpdateCheckResult.Failed(current, "Release ohne Tag-Namen.");

            var newer = PackageVersionSelector.IsNewer(tag, current);
            _cachedAsset = SelectAsset(release?.Assets, tag);

            _logger.LogInformation(
                "Update-Check fertig in {Ms} ms: aktuell {Current}, neuestes {Latest}, Update {State}, Asset {Asset}",
                sw.ElapsedMilliseconds, current, tag, newer ? "verfügbar" : "nicht nötig",
                _cachedAsset?.Name ?? "keins passend");

            return _cached = new UpdateCheckResult(
                newer, current, tag,
                release?.HtmlUrl ?? ReleasesPageUrl,
                _cachedAsset is not null,
                null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update-Check fehlgeschlagen ({Url}).", url);
            return _cached = UpdateCheckResult.Failed(current, ex.Message);
        }
    }

    /// <summary>
    /// Lädt das Asset des letzten Checks und startet das plattformspezifische
    /// Austausch-Skript. Bei true MUSS der Aufrufer <see cref="TerminateForUpdate"/>
    /// rufen — das Skript wartet auf das Prozessende, sonst hängt die UI bei 100 %.
    /// </summary>
    public async Task<bool> DownloadAndApplyAsync(IProgress<double>? progress, CancellationToken ct = default)
    {
        if (_cachedAsset is null)
        {
            _logger.LogWarning("Kein passendes Release-Asset für diese Plattform — Self-Update nicht möglich.");
            return false;
        }

        try
        {
            var workDir = Path.Combine(Path.GetTempPath(), $"nougat-update-{Environment.ProcessId}");
            Directory.CreateDirectory(workDir);
            var archivePath = Path.Combine(workDir, _cachedAsset.Name);

            await DownloadAsync(_cachedAsset, archivePath, progress, ct);
            _logger.LogInformation("Update heruntergeladen: {Path} ({Bytes} Bytes)",
                archivePath, new FileInfo(archivePath).Length);

            var started = StartInstaller(archivePath, workDir);
            if (!started)
                return false;

            _logger.LogInformation("Installer gestartet — die App muss sich jetzt beenden.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Self-Update fehlgeschlagen.");
            return false;
        }
    }

    /// <summary>
    /// Beendet die App, damit der wartende Installer weiterlaufen kann.
    ///
    /// FALLE (real in DTM v2.3.6): Wird das vergessen, wartet das Skript ewig auf
    /// das Prozessende und die UI bleibt bei "Update lädt: 100 %" stehen. Jeder
    /// Aufrufer von DownloadAndApplyAsync muss das bei Erfolg rufen.
    /// </summary>
    public void TerminateForUpdate()
    {
        _logger.LogInformation("App wird für den Update-Austausch beendet.");
        NLog.LogManager.Shutdown();

        // Fail-Safe: Falls Exit(0) an einem blockierenden Finalizer hängen bleibt,
        // beendet der Kill nach 1,5 s hart. Der Installer braucht kein sauberes
        // Ende, nur das Verschwinden der PID.
        var killer = new Thread(() =>
        {
            Thread.Sleep(1500);
            try { Process.GetCurrentProcess().Kill(); } catch { /* egal, wir sterben ohnehin */ }
        })
        { IsBackground = true };
        killer.Start();

        Environment.Exit(0);
    }

    private async Task DownloadAsync(ReleaseAsset asset, string targetPath, IProgress<double>? progress, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient(GithubRepoService.HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        // Pflicht für private Repos: Die API liefert nur mit diesem Accept-Header
        // die Binärdaten statt der JSON-Metadaten.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? asset.Size;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = File.Create(targetPath);

        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            if (total > 0)
                progress?.Report((double)done / total);
        }
    }

    /// <summary>
    /// Wählt das Asset, das zur laufenden Plattform passt. Namensschema kommt aus
    /// release.yml: Nougat-X.Y.Z-win-x64.zip, -linux-x64.tar.gz, -x86_64.AppImage.
    /// </summary>
    internal static ReleaseAsset? SelectAsset(IReadOnlyList<ReleaseAsset>? assets, string version)
    {
        if (assets is null || assets.Count == 0)
            return null;

        ReleaseAsset? ByEnding(string ending) =>
            assets.FirstOrDefault(a => a.Name.EndsWith(ending, StringComparison.OrdinalIgnoreCase));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ByEnding("win-x64.zip");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Läuft die App als AppImage, muss sie sich auch als AppImage ersetzen —
            // sonst landet ein tar.gz-Inhalt im read-only Squashfs-Mount.
            var runningAsAppImage = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPIMAGE"));
            return runningAsAppImage
                ? ByEnding(".AppImage") ?? ByEnding("linux-x64.tar.gz")
                : ByEnding("linux-x64.tar.gz") ?? ByEnding(".AppImage");
        }

        return null;
    }

    private bool StartInstaller(string archivePath, string workDir)
    {
        var pid = Environment.ProcessId;
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var exePath = Environment.ProcessPath;

        if (exePath is null)
        {
            _logger.LogError("Eigener Prozesspfad unbekannt — Austausch nicht möglich.");
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return StartWindowsInstaller(archivePath, workDir, appDir, exePath, pid);

        var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
        return !string.IsNullOrEmpty(appImage) && archivePath.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase)
            ? StartAppImageInstaller(archivePath, workDir, appImage, pid)
            : StartTarballInstaller(archivePath, workDir, appDir, exePath, pid);
    }

    private bool StartWindowsInstaller(string archivePath, string workDir, string appDir, string exePath, int pid)
    {
        var extractDir = Path.Combine(workDir, "new");
        System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, extractDir, overwriteFiles: true);

        var batPath = Path.Combine(workDir, "install.bat");
        var logPath = Path.Combine(workDir, "update.log");

        // FALLE: Zeilen OHNE führende Einrückung schreiben. Ein eingerücktes
        // ":label" ist für cmd kein gültiges Sprungziel — goto scheitert still,
        // xcopy läuft los, während die alte App die Dateien noch sperrt, und die
        // ALTE Version startet neu. Deshalb WriteAllLines statt Raw-String.
        var lines = new[]
        {
            "@echo off",
            $"echo Update gestartet %DATE% %TIME% > \"{logPath}\"",
            // Wait-Process blockiert sauber; eine tasklist-Polling-Schleife ist unzuverlaessig.
            $"powershell -NoProfile -Command \"Wait-Process -Id {pid} -ErrorAction SilentlyContinue\" >> \"{logPath}\" 2>&1",
            "timeout /t 2 /nobreak > nul",
            $"xcopy /E /Y /I \"{extractDir}\\*\" \"{appDir}\\\" >> \"{logPath}\" 2>&1",
            $"start \"\" \"{exePath}\"",
        };
        File.WriteAllLines(batPath, lines);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{batPath}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir,
        });
        return true;
    }

    private bool StartAppImageInstaller(string archivePath, string workDir, string appImage, int pid)
    {
        var script = Path.Combine(workDir, "install.sh");
        var logPath = UpdateLogPath();

        // cp -f statt mv/rm: Das laufende AppImage ist als Loop-Device gemountet,
        // ein Ersetzen scheitert mit "Text file busy". cp behaelt den Inode.
        // Der Log geht bewusst NICHT nach BaseDirectory — das ist beim laufenden
        // AppImage der read-only Squashfs-Mount, bash braeche dort sofort ab.
        var sh = string.Join('\n', new[]
        {
            "#!/bin/bash",
            $"exec >>\"{logPath}\" 2>&1",
            "set -x",
            $"while kill -0 {pid} 2>/dev/null; do sleep 0.3; done",
            "sleep 1",
            $"cp -f \"{archivePath}\" \"{appImage}\"",
            $"chmod +x \"{appImage}\"",
            $"setsid \"{appImage}\" >/dev/null 2>&1 &",
            "exit 0",
        }) + "\n";

        File.WriteAllText(script, sh);
        MakeExecutable(script);
        Process.Start(new ProcessStartInfo("/bin/bash", $"\"{script}\"") { UseShellExecute = false });
        return true;
    }

    private bool StartTarballInstaller(string archivePath, string workDir, string appDir, string exePath, int pid)
    {
        var script = Path.Combine(workDir, "install.sh");
        var logPath = UpdateLogPath();

        var sh = string.Join('\n', new[]
        {
            "#!/bin/bash",
            $"exec >>\"{logPath}\" 2>&1",
            "set -x",
            $"while kill -0 {pid} 2>/dev/null; do sleep 0.3; done",
            "sleep 1",
            $"tar -xzf \"{archivePath}\" -C \"{appDir}\"",
            $"chmod +x \"{exePath}\"",
            $"setsid \"{exePath}\" >/dev/null 2>&1 &",
            "exit 0",
        }) + "\n";

        File.WriteAllText(script, sh);
        MakeExecutable(script);
        Process.Start(new ProcessStartInfo("/bin/bash", $"\"{script}\"") { UseShellExecute = false });
        return true;
    }

    /// <summary>Schreibbarer Log-Pfad — niemals BaseDirectory (AppImage: read-only).</summary>
    private static string UpdateLogPath()
    {
        var state = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        if (string.IsNullOrWhiteSpace(state))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            state = Path.Combine(home, ".local", "state");
        }

        var dir = Path.Combine(state, "Nougat");
        try
        {
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "update.log");
        }
        catch (Exception)
        {
            return Path.Combine(Path.GetTempPath(), "nougat-update.log");
        }
    }

    private static void MakeExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("assets")] public List<ReleaseAsset>? Assets { get; set; }
    }

    public sealed class ReleaseAsset
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        /// <summary>API-URL des Assets — für private Repos der einzig nutzbare Weg.</summary>
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("size")] public long Size { get; set; }
    }
}
