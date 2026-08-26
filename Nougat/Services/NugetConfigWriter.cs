using System.Text;
using Nougat.Models;

namespace Nougat.Services;

/// <summary>Schreibt nuget.config.windows + README.txt in den Zielordner (analog zum Skript).</summary>
public sealed class NugetConfigWriter
{
    private const string WindowsConfig = """
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="C:\NuGet-Local" />
  </packageSources>

  <config>
    <add key="globalPackagesFolder" value="C:\NuGet-GlobalCache" />
  </config>

  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
""";

    public void Write(
        string outputDirectory,
        IEnumerable<string> includedRepos,
        int packageCount,
        long totalSizeBytes,
        IReadOnlyList<ConflictInfo> conflicts)
    {
        Directory.CreateDirectory(outputDirectory);

        File.WriteAllText(Path.Combine(outputDirectory, "nuget.config.windows"), WindowsConfig);
        File.WriteAllText(Path.Combine(outputDirectory, "README.txt"), BuildReadme(includedRepos, packageCount, totalSizeBytes, conflicts));
    }

    private static string BuildReadme(
        IEnumerable<string> includedRepos,
        int packageCount,
        long totalSizeBytes,
        IReadOnlyList<ConflictInfo> conflicts)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Offline-NuGet-Bundle (erzeugt von Nougat)");
        sb.AppendLine("=========================================");
        sb.AppendLine();
        sb.AppendLine($"Erzeugt am: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Pakete:     {packageCount}");
        sb.AppendLine($"Größe:    {totalSizeBytes / 1024.0 / 1024.0:F1} MB");
        sb.AppendLine();
        sb.AppendLine("Einbezogene Repos:");
        foreach (var r in includedRepos)
            sb.AppendLine($"  - {r}");
        sb.AppendLine();

        if (conflicts.Count > 0)
        {
            sb.AppendLine("Pakete, die in mehreren Versionen gebraucht werden:");
            sb.AppendLine("(alle aufgeführten Versionen liegen im Bundle - jedes Repo");
            sb.AppendLine(" restored gegen die Version, die es selbst festlegt)");
            foreach (var c in conflicts.OrderBy(c => c.PackageId, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"  {c.PackageId}");
                sb.AppendLine($"    {c.ChosenVersion}  <- {string.Join(", ", c.ChosenRepos)}");
                foreach (var d in c.DiscardedSources)
                    sb.AppendLine($"    {d.Version}  <- {string.Join(", ", d.Repos)}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Anwendung auf dem Windows-Rechner:");
        sb.AppendLine("  1) Diesen Ordner (alle *.nupkg + nuget.config.windows) nach C:\\NuGet-Local kopieren.");
        sb.AppendLine("  2) 'nuget.config.windows' zu 'nuget.config' umbenennen (neben die .sln oder nach %APPDATA%\\NuGet\\NuGet.Config).");
        sb.AppendLine("  3) In der .csproj bzw. Directory.Build.props setzen: <NuGetAudit>false</NuGetAudit>.");
        sb.AppendLine("  4) dotnet nuget locals all --clear && dotnet restore --force");

        return sb.ToString();
    }
}
