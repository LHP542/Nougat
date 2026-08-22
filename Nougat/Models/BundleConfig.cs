
namespace Nougat.Models;

/// <summary>Konfiguration eines Bundle-Laufs.</summary>
public sealed class BundleConfig
{
    public required IReadOnlyList<string> SelectedRepos { get; init; }
    public required IReadOnlyList<string> TargetRids { get; init; }
    public required string OutputDirectory { get; init; }
    public required string WorkDirectory { get; init; }
    public string DotnetChannel { get; init; } = "10.0";
    public bool ClearOutput { get; init; } = true;
}
