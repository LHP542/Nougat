using System.Xml.Linq;
using Nougat.Models;

namespace Nougat.Services;

/// <summary>
/// Erzeugt das Anker-Projekt im Arbeitsverzeichnis: Restore.csproj,
/// Directory.Packages.props, Directory.Build.props und nuget.config.
///
/// Der Anker arbeitet selbst mit Central Package Management und aktiviertem
/// <c>CentralPackageTransitivePinningEnabled</c> — genauso wie die Ziel-Repos.
/// WARUM das nicht egal ist: Ohne Pinning löst NuGet jede transitive Abhängigkeit
/// auf die NIEDRIGSTE passende Version auf. Das Bundle enthielt dadurch z. B.
/// System.Configuration.ConfigurationManager 9.0.11, während Checkmk mit Pinning
/// 10.0.11 auflöst — das Paket fehlte offline, obwohl Nougat "fertig" meldete.
/// Zusätzlich lagen Microsoft.Extensions.* in drei Versionen nebeneinander im Feed.
/// </summary>
public sealed class AnchorProjectGenerator
{
    /// <summary>
    /// Anker-Projekt. Die Versionen stehen bewusst NICHT hier, sondern in der
    /// Directory.Packages.props daneben.
    /// </summary>
    public string GenerateCsproj(IEnumerable<PackageRef> packages, IEnumerable<string>? targetFrameworks = null)
    {
        var doc = new XDocument(
            new XElement("Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    TargetFrameworkElement(targetFrameworks),
                    new XElement("OutputType", "Exe"),
                    new XElement("Nullable", "enable"),
                    new XElement("NuGetAudit", "false"),
                    new XElement("UseAppHost", "false"),
                    new XElement("EnableDefaultItems", "false"),
                    new XElement("NoBuild", "true"),
                    // Erlaubt das Auflösen von net10.0-windows-Abhängigkeiten,
                    // wenn Nougat selbst unter Linux läuft. Ohne das bricht der
                    // Restore mit NETSDK1100 ab, sobald ein Windows-TFM im Spiel ist.
                    new XElement("EnableWindowsTargeting", "true"),
                    // Downgrade-/Vulnerability-/Deprecation-Warnings tolerieren:
                    // Wir SAMMELN Pakete quer über Repos — bewusst mit widersprüchlichen
                    // transitiven Anforderungen. Diese Widersprüche löst jedes echte
                    // Zielprojekt selbst auf; wir wollen einfach alle .nupkg im Bundle haben.
                    new XElement("TreatWarningsAsErrors", "false"),
                    new XElement("NoWarn", "NU1605;NU1701;NU1902;NU1903;NU1904")
                ),
                CreateItemGroup(packages)
            )
        );
        return doc.Declaration is null
            ? doc.ToString()
            : doc.Declaration + "\n" + doc.ToString();
    }

    /// <summary>
    /// Zentrale Versionsliste des Ankers. Enthält jedes gesammelte Paket genau
    /// einmal (der PackageDeduplicator hat vorher entschieden, welche Version
    /// gewinnt) und schaltet das transitive Pinning ein.
    /// </summary>
    public string GeneratePackagesProps(IEnumerable<PackageRef> packages)
    {
        var group = new XElement("ItemGroup");
        foreach (var p in packages.Where(p => !string.IsNullOrWhiteSpace(p.Version)))
        {
            group.Add(new XElement("PackageVersion",
                new XAttribute("Include", p.Id),
                new XAttribute("Version", p.Version!)));
        }

        var doc = new XDocument(
            new XElement("Project",
                new XElement("PropertyGroup",
                    new XElement("ManagePackageVersionsCentrally", "true"),
                    // Der eigentliche Punkt: Jede transitive Abhängigkeit wird auf
                    // die hier gelistete Version gehoben, statt dass NuGet die
                    // niedrigste passende zieht.
                    new XElement("CentralPackageTransitivePinningEnabled", "true")
                ),
                group
            )
        );
        return doc.Declaration is null
            ? doc.ToString()
            : doc.Declaration + "\n" + doc.ToString();
    }

    /// <summary>
    /// Leere Directory.Build.props als Riegel. MSBuild sucht diese Datei aufwärts
    /// im Verzeichnisbaum — liegt das Arbeitsverzeichnis zufällig unterhalb eines
    /// Repos, würden dessen Build-Einstellungen (TargetFramework,
    /// TreatWarningsAsErrors, eigene CPM-Schalter) in den Anker durchschlagen.
    /// </summary>
    public string GenerateBuildProps() => """
<?xml version="1.0" encoding="utf-8"?>
<!-- Absichtlich leer: schirmt den Anker gegen Directory.Build.props aus
     übergeordneten Verzeichnissen ab. -->
<Project />
""";

    public string GenerateNugetConfig() => """
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
""";

    public void WriteToWorkDirectory(
        string workDirectory,
        IEnumerable<PackageRef> packages,
        out string anchorPath,
        IEnumerable<string>? targetFrameworks = null)
    {
        var list = packages as IReadOnlyList<PackageRef> ?? packages.ToList();

        Directory.CreateDirectory(workDirectory);
        anchorPath = Path.Combine(workDirectory, "Restore.csproj");
        File.WriteAllText(anchorPath, GenerateCsproj(list, targetFrameworks));
        File.WriteAllText(Path.Combine(workDirectory, "Directory.Packages.props"), GeneratePackagesProps(list));
        File.WriteAllText(Path.Combine(workDirectory, "Directory.Build.props"), GenerateBuildProps());
        File.WriteAllText(Path.Combine(workDirectory, "nuget.config"), GenerateNugetConfig());
    }

    /// <summary>
    /// Der Anker baut gegen ALLE TargetFrameworks der ausgewählten Repos.
    ///
    /// WARUM: NuGet löst pro TFM einen eigenen Abhängigkeitsgraphen auf. Ein Anker,
    /// der nur net10.0 kennt, zieht für ein net10.0-windows-Projekt weder
    /// Microsoft.WindowsDesktop.App.Ref noch Microsoft.Windows.SDK.NET.Ref — offline
    /// lässt sich so ein Projekt dann gar nicht bauen. Mit allen TFMs deckte das
    /// Bundle im Test den Bedarf von Checkmk vollständig ab (vorher fehlten 26
    /// Paket-IDs und 7 lagen in falscher Version).
    /// </summary>
    private static XElement TargetFrameworkElement(IEnumerable<string>? targetFrameworks)
    {
        var tfms = (targetFrameworks ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Ohne erkennbares TFM bleibt net10.0 der Rückfall — besser ein
        // unvollständiges Bundle als ein Anker, der gar nicht restored.
        if (tfms.Count == 0)
            return new XElement("TargetFramework", "net10.0");

        return tfms.Count == 1
            ? new XElement("TargetFramework", tfms[0])
            : new XElement("TargetFrameworks", string.Join(";", tfms));
    }

    private static XElement CreateItemGroup(IEnumerable<PackageRef> packages)
    {
        var group = new XElement("ItemGroup");
        foreach (var p in packages.Where(p => !string.IsNullOrWhiteSpace(p.Version)))
        {
            // Kein Version-Attribut: Die Version kommt aus Directory.Packages.props.
            group.Add(new XElement("PackageReference", new XAttribute("Include", p.Id)));
        }
        return group;
    }
}
