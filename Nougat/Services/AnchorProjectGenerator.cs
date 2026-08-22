using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Nougat.Models;

namespace Nougat.Services;

/// <summary>
/// Erzeugt das Anker-.csproj + eine begleitende nuget.config im Arbeitsverzeichnis.
/// Nachbau des Blocks aus nuget-offline-bundle.sh (Zeilen 95-147).
/// </summary>
public sealed class AnchorProjectGenerator
{
    public string GenerateCsproj(IEnumerable<PackageRef> packages)
    {
        var doc = new XDocument(
            new XElement("Project",
                new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup",
                    new XElement("TargetFramework", "net10.0"),
                    new XElement("OutputType", "Exe"),
                    new XElement("Nullable", "enable"),
                    new XElement("NuGetAudit", "false"),
                    new XElement("UseAppHost", "false"),
                    new XElement("EnableDefaultItems", "false"),
                    new XElement("NoBuild", "true"),
                    new XElement("ManagePackageVersionsCentrally", "false"),
                    // Downgrade-/Vulnerability-/Deprecation-Warnings tolerieren:
                    // Wir SAMMELN Pakete quer über Repos — bewusst mit widersprüchlichen
                    // transitiven Anforderungen. Diese Widersprüche lost jedes echte
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
        out string anchorPath)
    {
        Directory.CreateDirectory(workDirectory);
        anchorPath = Path.Combine(workDirectory, "Restore.csproj");
        File.WriteAllText(anchorPath, GenerateCsproj(packages));
        File.WriteAllText(Path.Combine(workDirectory, "nuget.config"), GenerateNugetConfig());
    }

    private static XElement CreateItemGroup(IEnumerable<PackageRef> packages)
    {
        var group = new XElement("ItemGroup");
        foreach (var p in packages)
        {
            group.Add(new XElement("PackageReference",
                new XAttribute("Include", p.Id),
                new XAttribute("Version", p.Version ?? "")));
        }
        return group;
    }
}
