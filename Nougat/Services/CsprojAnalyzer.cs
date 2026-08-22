using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Nougat.Models;

namespace Nougat.Services;

/// <summary>
/// Extrahiert PackageReferences aus einem Repo. Unterstützt Central Package Management:
/// wenn eine PackageReference keine Version tragt, wird sie aus der
/// Directory.Packages.props (PackageVersion) desselben Repos ergänzt.
/// </summary>
public sealed class CsprojAnalyzer
{
    private readonly GithubRepoService _github;
    private readonly ILogger<CsprojAnalyzer> _logger;

    public CsprojAnalyzer(GithubRepoService github, ILogger<CsprojAnalyzer> logger)
    {
        _github = github;
        _logger = logger;
    }

    public async Task<List<PackageRef>> AnalyzeAsync(
        string ownerRepo, string branch, CancellationToken ct = default)
    {
        var tree = await _github.ListRepoTreeAsync(ownerRepo, branch, ct).ConfigureAwait(false);

        var csprojPaths = new List<string>();
        var cpmPaths = new List<string>();
        foreach (var p in tree)
        {
            var name = Path.GetFileName(p);
            if (p.EndsWith(".csproj", System.StringComparison.OrdinalIgnoreCase))
                csprojPaths.Add(p);
            else if (name.Equals("Directory.Packages.props", System.StringComparison.OrdinalIgnoreCase))
                cpmPaths.Add(p);
        }

        // CPM-Version-Map über alle Directory.Packages.props zusammenführen
        var cpmVersions = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var cpm in cpmPaths)
        {
            var xml = await _github.GetRawFileAsync(ownerRepo, branch, cpm, ct).ConfigureAwait(false);
            ParseCpm(xml, cpmVersions);
        }

        var result = new List<PackageRef>();
        foreach (var proj in csprojPaths)
        {
            var xml = await _github.GetRawFileAsync(ownerRepo, branch, proj, ct).ConfigureAwait(false);
            ParseCsproj(xml, ownerRepo, proj, cpmVersions, result);
        }

        _logger.LogInformation("{Repo}@{Branch}: {Csproj} csproj, {Refs} PackageReferences",
            ownerRepo, branch, csprojPaths.Count, result.Count);
        return result;
    }

    internal static void ParseCsproj(
        string xml, string repo, string file,
        IReadOnlyDictionary<string, string> cpm,
        List<PackageRef> result)
    {
        var doc = XDocument.Parse(xml);
        foreach (var refEl in doc.Descendants("PackageReference"))
        {
            var id = (string?)refEl.Attribute("Include");
            if (string.IsNullOrWhiteSpace(id)) continue;

            var version = (string?)refEl.Attribute("Version");
            if (string.IsNullOrWhiteSpace(version))
            {
                // CPM: aus Directory.Packages.props holen
                cpm.TryGetValue(id, out var cpmVersion);
                version = cpmVersion;
            }

            var condition = (string?)refEl.Attribute("Condition");
            result.Add(new PackageRef(id, version, condition, repo, file));
        }
    }

    internal static void ParseCpm(string xml, Dictionary<string, string> versions)
    {
        var doc = XDocument.Parse(xml);
        foreach (var el in doc.Descendants("PackageVersion"))
        {
            var id = (string?)el.Attribute("Include");
            var version = (string?)el.Attribute("Version");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(version))
                versions[id] = version;
        }
    }
}
