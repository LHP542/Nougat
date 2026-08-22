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
