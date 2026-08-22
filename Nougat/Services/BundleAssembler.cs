using Microsoft.Extensions.Logging;

namespace Nougat.Services;

public sealed record AssembleResult(int PackageCount, long TotalSizeBytes);

/// <summary>
/// Nachbau des Skript-Blocks Zeilen 154-192: leert den Zielordner (rekursiv, ein
/// Syscall - keine per-File-Schleife wegen Antivirus-Falle) und kopiert alle
/// .nupkg-Dateien aus dem hierarchischen Cache flach hinein.
/// </summary>
public sealed class BundleAssembler
{
    private readonly ILogger<BundleAssembler> _logger;

    public BundleAssembler(ILogger<BundleAssembler> logger)
    {
        _logger = logger;
    }

    public AssembleResult Assemble(string hierarchicalCache, string outputDirectory, Action<int>? onProgress = null)
    {
        if (Directory.Exists(outputDirectory))
        {
            _logger.LogDebug("Lösche Zielordner {Dir}", outputDirectory);
            Directory.Delete(outputDirectory, recursive: true);
        }
        Directory.CreateDirectory(outputDirectory);

        var count = 0;
        long size = 0;

        foreach (var src in Directory.EnumerateFiles(hierarchicalCache, "*.nupkg", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(src);
            var dst = Path.Combine(outputDirectory, name);
            if (File.Exists(dst)) continue; // erstes Vorkommen gewinnt

            File.Copy(src, dst);
            count++;
            size += new FileInfo(dst).Length;

            if (count % 25 == 0) onProgress?.Invoke(count);
        }

        onProgress?.Invoke(count);
        _logger.LogInformation("Zielordner bestückt: {Count} Pakete, {SizeMB:F1} MB", count, size / 1024.0 / 1024.0);
        return new AssembleResult(count, size);
    }
}
