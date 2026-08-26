namespace Nougat.Models;

/// <summary>
/// Ergebnis der Repo-Analyse: die gefundenen Pakete plus die TargetFrameworks,
/// gegen die das Repo baut. Beides fließt in das Anker-Projekt.
/// </summary>
public sealed record RepoAnalysis(
    List<PackageRef> Packages,
    HashSet<string> TargetFrameworks
);
