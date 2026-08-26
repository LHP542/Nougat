using FluentAssertions;
using Nougat.Models;
using Nougat.Services;
using Xunit;

namespace Nougat.Tests;

public class AnchorProjectGeneratorTests
{
    private static readonly PackageRef[] Packages =
    [
        new("NLog", "6.1.4", null, "LHP542/X", "X.csproj"),
        new("Avalonia", "12.1.0", null, "LHP542/Y", "Y.csproj"),
    ];

    [Fact]
    public void Anker_referenziert_Pakete_ohne_eigene_Version()
    {
        var xml = new AnchorProjectGenerator().GenerateCsproj(Packages);

        xml.Should().Contain("<Project Sdk=\"Microsoft.NET.Sdk\"");
        xml.Should().Contain("<NoBuild>true</NoBuild>");
        xml.Should().Contain("<NuGetAudit>false</NuGetAudit>");

        // Die Versionen stehen in der Directory.Packages.props daneben. Stünden sie
        // zusätzlich hier, wäre das mit aktivem CPM ein Fehler (NU1008).
        xml.Should().Contain("<PackageReference Include=\"NLog\" />");
        xml.Should().Contain("<PackageReference Include=\"Avalonia\" />");
        xml.Should().NotContain("Version=\"6.1.4\"");
    }

    [Fact]
    public void Packages_props_pinnt_auch_transitive_Abhaengigkeiten()
    {
        var props = new AnchorProjectGenerator().GeneratePackagesProps(Packages);

        props.Should().Contain("<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>");
        // Der Kern des Fixes: Ohne diesen Schalter zieht NuGet transitive
        // Abhaengigkeiten in der niedrigsten passenden Version, und im Bundle
        // landet z. B. ConfigurationManager 9.0.11 statt der gebrauchten 10.0.11.
        props.Should().Contain("<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>");
        props.Should().Contain("<PackageVersion Include=\"NLog\" Version=\"6.1.4\" />");
        props.Should().Contain("<PackageVersion Include=\"Avalonia\" Version=\"12.1.0\" />");
    }

    [Fact]
    public void Ohne_erkanntes_TFM_bleibt_net10_der_Rueckfall() =>
        new AnchorProjectGenerator().GenerateCsproj(Packages)
            .Should().Contain("<TargetFramework>net10.0</TargetFramework>");

    [Fact]
    public void Ein_einzelnes_TFM_wird_als_TargetFramework_geschrieben() =>
        new AnchorProjectGenerator().GenerateCsproj(Packages, ["net10.0-windows10.0.19041.0"])
            .Should().Contain("<TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>");

    [Fact]
    public void Mehrere_TFMs_landen_als_TargetFrameworks_im_Anker()
    {
        // Ein Anker nur mit net10.0 zieht fuer ein net10.0-windows-Projekt weder
        // Microsoft.WindowsDesktop.App.Ref noch Microsoft.Windows.SDK.NET.Ref —
        // offline liesse sich so ein Projekt gar nicht bauen.
        var xml = new AnchorProjectGenerator()
            .GenerateCsproj(Packages, ["net10.0-windows10.0.19041.0", "net10.0"]);

        xml.Should().Contain("<TargetFrameworks>net10.0;net10.0-windows10.0.19041.0</TargetFrameworks>");
        xml.Should().NotContain("<TargetFramework>");
    }

    [Fact]
    public void Doppelte_TFMs_werden_zusammengefasst() =>
        new AnchorProjectGenerator().GenerateCsproj(Packages, ["net10.0", "NET10.0", "net10.0"])
            .Should().Contain("<TargetFramework>net10.0</TargetFramework>");

    [Fact]
    public void Anker_erlaubt_Windows_Targeting()
    {
        // Ohne das bricht der Restore unter Linux mit NETSDK1100 ab, sobald ein
        // Windows-TFM im Spiel ist — Nougat laeuft aber auch auf Bazzite.
        new AnchorProjectGenerator().GenerateCsproj(Packages)
            .Should().Contain("<EnableWindowsTargeting>true</EnableWindowsTargeting>");
    }

    [Fact]
    public void Pakete_ohne_Version_landen_in_keiner_der_beiden_Dateien()
    {
        PackageRef[] mitLuecke =
        [
            new("NLog", "6.1.4", null, "LHP542/X", "X.csproj"),
            new("Unaufloesbar", null, null, "LHP542/X", "X.csproj"),
        ];
        var gen = new AnchorProjectGenerator();

        gen.GenerateCsproj(mitLuecke).Should().NotContain("Unaufloesbar");
        gen.GeneratePackagesProps(mitLuecke).Should().NotContain("Unaufloesbar");
    }

    [Fact]
    public void Schreibt_alle_vier_Dateien_ins_Arbeitsverzeichnis()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nougat-anchor-" + Path.GetRandomFileName());
        try
        {
            new AnchorProjectGenerator().WriteToWorkDirectory(dir, Packages, out var anchorPath, ["net10.0"]);

            File.Exists(anchorPath).Should().BeTrue();
            File.Exists(Path.Combine(dir, "Directory.Packages.props")).Should().BeTrue();
            File.Exists(Path.Combine(dir, "nuget.config")).Should().BeTrue();

            // Riegel gegen Vererbung: Liegt das Arbeitsverzeichnis unterhalb eines
            // Repos, wuerden dessen Build-Einstellungen sonst in den Anker durchschlagen.
            var buildProps = Path.Combine(dir, "Directory.Build.props");
            File.Exists(buildProps).Should().BeTrue();
            File.ReadAllText(buildProps).Should().Contain("<Project />");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Generates_nuget_config_with_only_nuget_org()
    {
        var xml = new AnchorProjectGenerator().GenerateNugetConfig();
        xml.Should().Contain("<clear />");
        xml.Should().Contain("api.nuget.org/v3/index.json");
    }
}
