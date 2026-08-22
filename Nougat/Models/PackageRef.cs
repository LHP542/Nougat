namespace Nougat.Models;

/// <summary>Ein referenziertes NuGet-Paket aus einem Repo (möglicherweise ohne Version).</summary>
public sealed record PackageRef(
    string Id,
    string? Version,
    string? Condition,
    string SourceRepo,
    string SourceFile
);
