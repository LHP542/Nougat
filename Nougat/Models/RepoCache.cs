
namespace Nougat.Models;

/// <summary>Persistierter Cache der GitHub-Repo-Liste + zuletzt selektierte Repos.</summary>
public sealed class RepoCache
{
    public DateTime FetchedAt { get; set; }
    public List<RepoInfo> Repos { get; set; } = [];
    public List<string> SelectedNames { get; set; } = [];
}
