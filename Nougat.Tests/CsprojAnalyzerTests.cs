using FluentAssertions;
using Nougat.Models;
using Nougat.Services;
using Xunit;

namespace Nougat.Tests;

public class CsprojAnalyzerTests
{
    [Fact]
    public void Parses_classic_csproj_with_versions()
    {
        var xml = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <PackageReference Include="NLog" Version="6.1.4" />
            <PackageReference Include="Avalonia" Version="12.1.0" />
          </ItemGroup>
        </Project>
        """;
        var result = new List<PackageRef>();
        CsprojAnalyzer.ParseCsproj(xml, "Kroste/Foo", "Foo/Foo.csproj", new Dictionary<string, string>(), result);

        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Id == "NLog" && p.Version == "6.1.4");
        result.Should().Contain(p => p.Id == "Avalonia" && p.Version == "12.1.0");
    }

    [Fact]
    public void Parses_cpm_csproj_pulling_version_from_directory_packages_props()
    {
        var propsXml = """
        <Project>
          <ItemGroup>
            <PackageVersion Include="NLog" Version="6.1.4" />
            <PackageVersion Include="Avalonia" Version="12.1.0" />
          </ItemGroup>
        </Project>
        """;
        var csproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <PackageReference Include="NLog" />
            <PackageReference Include="Avalonia" />
          </ItemGroup>
        </Project>
        """;

        var cpm = new Dictionary<string, string>();
        CsprojAnalyzer.ParseCpm(propsXml, cpm);

        var result = new List<PackageRef>();
        CsprojAnalyzer.ParseCsproj(csproj, "Kroste/Bar", "Bar/Bar.csproj", cpm, result);

        result.Should().HaveCount(2);
        result.Should().Contain(p => p.Id == "NLog" && p.Version == "6.1.4");
        result.Should().Contain(p => p.Id == "Avalonia" && p.Version == "12.1.0");
    }

    [Fact]
    public void Missing_version_yields_null_version()
    {
        var csproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <PackageReference Include="Ghost" />
          </ItemGroup>
        </Project>
        """;
        var result = new List<PackageRef>();
        CsprojAnalyzer.ParseCsproj(csproj, "Kroste/Baz", "Baz.csproj", new Dictionary<string, string>(), result);

        result.Should().ContainSingle();
        result[0].Version.Should().BeNull();
    }

    [Fact]
    public void Reads_condition_attribute()
    {
        var csproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup Condition="'$(Configuration)' == 'Debug'">
            <PackageReference Include="AvaloniaUI.DiagnosticsSupport" Version="2.2.3" Condition="'$(Configuration)' == 'Debug'" />
          </ItemGroup>
        </Project>
        """;
        var result = new List<PackageRef>();
        CsprojAnalyzer.ParseCsproj(csproj, "Kroste/Qux", "Qux.csproj", new Dictionary<string, string>(), result);

        result.Should().ContainSingle();
        result[0].Condition.Should().Be("'$(Configuration)' == 'Debug'");
    }
}

public class CsprojAnalyzerPinTests
{
    private const string PropsMitPinning = """
        <Project>
          <PropertyGroup>
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
            <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
          </PropertyGroup>
          <ItemGroup>
            <PackageVersion Include="NLog" Version="6.2.0" />
            <PackageVersion Include="System.Configuration.ConfigurationManager" Version="10.0.11" />
          </ItemGroup>
        </Project>
        """;

    [Fact]
    public void Erkennt_aktives_transitives_Pinning()
    {
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CsprojAnalyzer.ParseCpm(PropsMitPinning, versions).Should().BeTrue();
        versions.Should().HaveCount(2);
    }

    [Fact]
    public void Ohne_Schalter_kein_Pinning()
    {
        const string ohne = """
            <Project>
              <ItemGroup><PackageVersion Include="NLog" Version="6.2.0" /></ItemGroup>
            </Project>
            """;
        CsprojAnalyzer.ParseCpm(ohne, new Dictionary<string, string>()).Should().BeFalse();
    }

    [Theory]
    [InlineData("<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>", "net10.0")]
    [InlineData("<Project><PropertyGroup><TargetFramework>net10.0-windows10.0.19041.0</TargetFramework></PropertyGroup></Project>", "net10.0-windows10.0.19041.0")]
    [InlineData("<Project><PropertyGroup><TargetFrameworks>net10.0;net8.0</TargetFrameworks></PropertyGroup></Project>", "net10.0,net8.0")]
    public void Liest_TargetFrameworks(string xml, string erwartet)
    {
        var tfms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CsprojAnalyzer.CollectTargetFrameworks(xml, tfms);
        string.Join(",", tfms.OrderBy(t => t)).Should().Be(erwartet);
    }

    [Fact]
    public void Unaufloesbare_MSBuild_Properties_werden_verworfen()
    {
        // $(...) kann Nougat nicht auswerten; im Anker wuerde der Platzhalter
        // den Restore zerlegen. Lieber der net10.0-Rueckfall.
        var tfms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CsprojAnalyzer.CollectTargetFrameworks(
            "<Project><PropertyGroup><TargetFramework>$(MeinTfm)</TargetFramework></PropertyGroup></Project>", tfms);
        tfms.Should().BeEmpty();
    }
}
