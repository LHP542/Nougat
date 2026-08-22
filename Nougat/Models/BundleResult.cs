
namespace Nougat.Models;

/// <summary>Eine konkrete Version + die Repos, in denen sie gepinnt war.</summary>
public sealed record VersionSource(string Version, IReadOnlyList<string> Repos);

/// <summary>Diagnostik zu einem Package, das in verschiedenen Repos in verschiedenen Versionen referenziert wurde.</summary>
public sealed record ConflictInfo(
    string PackageId,
    string ChosenVersion,
    IReadOnlyList<string> ChosenRepos,
    IReadOnlyList<VersionSource> DiscardedSources
);

/// <summary>Ergebnis eines Bundle-Laufs.</summary>
public sealed class BundleResult
{
    public required bool Success { get; init; }
    public int PackageCount { get; init; }
    public long TotalSizeBytes { get; init; }
    public TimeSpan Duration { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<PackageRef> ResolvedPackages { get; init; } = [];
    public IReadOnlyList<ConflictInfo> Conflicts { get; init; } = [];
    public string? ErrorMessage { get; init; }
}
