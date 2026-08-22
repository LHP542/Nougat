using FluentAssertions;
using Nougat.Models;
using Nougat.Services;
using Xunit;

namespace Nougat.Tests;

public class NugetConfigWriterTests : IDisposable
{
    private readonly string _outDir;

    public NugetConfigWriterTests()
    {
        _outDir = Path.Combine(Path.GetTempPath(), $"nougat-writer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_outDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_outDir)) Directory.Delete(_outDir, true);
    }

    [Fact]
    public void Writes_nuget_config_and_readme_with_expected_content()
    {
        var writer = new NugetConfigWriter();
        var conflicts = new[]
        {
            new ConflictInfo(
                "NLog",
                "6.1.4",
                new[] { "Kroste/DTM" },
                new[] { new VersionSource("5.3.4", new[] { "Kroste/NetScanner" }) }),
        };

        writer.Write(_outDir, new[] { "Kroste/DTM", "Kroste/NetScanner" }, 42, 12_345_678, conflicts);

        var configPath = Path.Combine(_outDir, "nuget.config.windows");
        var readmePath = Path.Combine(_outDir, "README.txt");
        File.Exists(configPath).Should().BeTrue();
        File.Exists(readmePath).Should().BeTrue();

        var config = File.ReadAllText(configPath);
        config.Should().Contain("C:\\NuGet-Local");
        config.Should().Contain("packageSourceMapping");

        var readme = File.ReadAllText(readmePath);
        readme.Should().Contain("Kroste/DTM");
        readme.Should().Contain("Kroste/NetScanner");
        readme.Should().Contain("42");
        readme.Should().Contain("NLog");
        readme.Should().Contain("6.1.4");
        readme.Should().Contain("5.3.4");
        readme.Should().Contain("<- Kroste/DTM");
        readme.Should().Contain("<- Kroste/NetScanner");
    }
}
