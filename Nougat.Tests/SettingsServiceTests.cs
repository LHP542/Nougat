using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nougat.Models;
using Nougat.Services;
using Xunit;

namespace Nougat.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tmpPath;

    public SettingsServiceTests()
    {
        _tmpPath = Path.Combine(Path.GetTempPath(), $"nougat-settings-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tmpPath)) File.Delete(_tmpPath);
        if (File.Exists(_tmpPath + ".broken")) File.Delete(_tmpPath + ".broken");
    }

    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        var svc = new SettingsService(NullLogger<SettingsService>.Instance, _tmpPath);
        var s = svc.Load();
        s.TargetRids.Should().Contain("win-x64");
        s.OutputDirectory.Should().NotBeEmpty();
    }

    [Fact]
    public void Roundtrip_preserves_values()
    {
        var svc = new SettingsService(NullLogger<SettingsService>.Instance, _tmpPath);
        var original = new AppSettings
        {
            OutputDirectory = "/tmp/out",
            WorkDirectory = "/tmp/work",
            RepoCacheTtlHours = 12,
            TargetRids = ["win-x64", "linux-x64"],
            ShowArchivedRepos = true,
            EncryptedPat = "ENC1:abc",
        };
        svc.Save(original);

        var svc2 = new SettingsService(NullLogger<SettingsService>.Instance, _tmpPath);
        var loaded = svc2.Load();

        loaded.OutputDirectory.Should().Be("/tmp/out");
        loaded.WorkDirectory.Should().Be("/tmp/work");
        loaded.RepoCacheTtlHours.Should().Be(12);
        loaded.TargetRids.Should().BeEquivalentTo(new[] { "win-x64", "linux-x64" });
        loaded.ShowArchivedRepos.Should().BeTrue();
        loaded.EncryptedPat.Should().Be("ENC1:abc");
    }

    [Fact]
    public void Broken_json_is_backed_up_and_defaults_returned()
    {
        File.WriteAllText(_tmpPath, "{ das ist kein json");
        var svc = new SettingsService(NullLogger<SettingsService>.Instance, _tmpPath);
        var s = svc.Load();
        s.Should().NotBeNull();
        File.Exists(_tmpPath + ".broken").Should().BeTrue();
    }
}
