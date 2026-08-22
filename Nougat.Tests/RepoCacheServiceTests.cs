using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nougat.Models;
using Nougat.Services;
using Xunit;

namespace Nougat.Tests;

public class RepoCacheServiceTests : IDisposable
{
    private readonly string _path;

    public RepoCacheServiceTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"nougat-cache-{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
        if (File.Exists(_path + ".broken")) File.Delete(_path + ".broken");
    }

    [Fact]
    public void TryLoad_returns_false_when_file_missing()
    {
        var svc = new RepoCacheService(NullLogger<RepoCacheService>.Instance, _path);
        svc.TryLoad(out _).Should().BeFalse();
    }

    [Fact]
    public void Roundtrip_stores_and_reads_selection()
    {
        var svc = new RepoCacheService(NullLogger<RepoCacheService>.Instance, _path);
        var repos = new List<RepoInfo>
        {
            new() { Name = "DTM", FullName = "Kroste/DTM", DefaultBranch = "main" },
            new() { Name = "NetScanner", FullName = "Kroste/NetScanner", DefaultBranch = "main" },
        };
        svc.Save(repos, new[] { "Kroste/DTM" });

        svc.TryLoad(out var loaded).Should().BeTrue();
        loaded.Repos.Should().HaveCount(2);
        loaded.SelectedNames.Should().BeEquivalentTo(new[] { "Kroste/DTM" });
        loaded.FetchedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Broken_cache_gets_moved_to_broken()
    {
        File.WriteAllText(_path, "kein json");
        var svc = new RepoCacheService(NullLogger<RepoCacheService>.Instance, _path);
        svc.TryLoad(out _).Should().BeFalse();
        File.Exists(_path + ".broken").Should().BeTrue();
    }

    [Fact]
    public void IsFresh_respects_ttl()
    {
        var svc = new RepoCacheService(NullLogger<RepoCacheService>.Instance, _path);
        var stale = new RepoCache { FetchedAt = DateTime.UtcNow.AddHours(-25) };
        var fresh = new RepoCache { FetchedAt = DateTime.UtcNow.AddHours(-1) };
        svc.IsFresh(stale, TimeSpan.FromHours(24)).Should().BeFalse();
        svc.IsFresh(fresh, TimeSpan.FromHours(24)).Should().BeTrue();
    }
}
