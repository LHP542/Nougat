using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Nougat.Models;

namespace Nougat.Services;

/// <summary>
/// Extrahiert die Paketliste eines Repos aus seinen csproj-Dateien und der
/// Directory.Packages.props (Central Package Management).
///
/// Zwei Quellen, beide nötig:
/// 1. <c>PackageReference</c> aus jeder csproj. Ohne Version-Attribut wird die
///    Version aus der Directory.Packages.props desselben Repos ergänzt.
/// 2. <c>PackageVersion</c>-Einträge, die von KEINEM Projekt direkt referenziert
///    werden. Die sind kein Müll, sondern transitive Pins: Mit
///    <c>CentralPackageTransitivePinningEnabled</c> hebt NuGet jede transitive
///    Abhängigkeit auf die dort angegebene Version. Ohne sie fehlen im
///    Offline-Bundle genau die Versionen, die das Zielprojekt am Ende auflöst —
///    real bei Checkmk: 11 von 30 Einträgen stehen nur wegen des Pinnings dort,
///    darunter System.Configuration.ConfigurationManager 10.0.11, das im Bundle
///    stattdessen als 9.0.11 landete.
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
        string ownerRepo, string branch, CancellationToken ct = default) =>
        (await AnalyzeRepoAsync(ownerRepo, branch, ct).ConfigureAwait(false)).Packages;

    /// <summary>
    /// Wie <see cref="AnalyzeAsync"/>, liefert zusätzlich die TargetFrameworks des
    /// Repos. Die braucht der Anker: Ein Repo mit net10.0-windows löst andere
    /// Pakete auf als eines mit net10.0 — ohne das passende TFM fehlen im Bundle
    /// die Windows-Ref- und Runtime-Packs, und offline lässt sich das Projekt
    /// gar nicht erst bauen.
    /// </summary>
    public async Task<RepoAnalysis> AnalyzeRepoAsync(
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

        // CPM-Version-Map über alle Directory.Packages.props zusammenführen.
        // origin merkt sich, aus welcher Datei ein Eintrag kam — das steht später
        // in der README.txt des Bundles als Herkunft.
        var cpmVersions = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        var origin = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        var transitivePinning = false;

        foreach (var cpm in cpmPaths)
        {
            var xml = await _github.GetRawFileAsync(ownerRepo, branch, cpm, ct).ConfigureAwait(false);
            var before = new HashSet<string>(cpmVersions.Keys, System.StringComparer.OrdinalIgnoreCase);

            transitivePinning |= ParseCpm(xml, cpmVersions);

            foreach (var id in cpmVersions.Keys)
                if (!before.Contains(id))
                    origin[id] = cpm;
        }

        var result = new List<PackageRef>();
        var tfms = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        // Directory.Build.props kann das TargetFramework für alle Projekte setzen.
        foreach (var props in tree.Where(p =>
            Path.GetFileName(p).Equals("Directory.Build.props", System.StringComparison.OrdinalIgnoreCase)))
        {
            var xml = await _github.GetRawFileAsync(ownerRepo, branch, props, ct).ConfigureAwait(false);
            CollectTargetFrameworks(xml, tfms);
        }

        foreach (var proj in csprojPaths)
        {
            var xml = await _github.GetRawFileAsync(ownerRepo, branch, proj, ct).ConfigureAwait(false);
            ParseCsproj(xml, ownerRepo, proj, cpmVersions, result);
            CollectTargetFrameworks(xml, tfms);
        }

        var pins = AddUnreferencedPins(ownerRepo, cpmVersions, origin, result);

        _logger.LogInformation(
            "{Repo}@{Branch}: {Csproj} csproj, {Refs} PackageReferences, {Pins} reine Pins aus "
            + "Directory.Packages.props (transitives Pinning im Repo: {Pinning}), TFMs: {Tfms}",
            ownerRepo, branch, csprojPaths.Count, result.Count - pins, pins,
            transitivePinning ? "an" : "aus",
            tfms.Count == 0 ? "keine" : string.Join(";", tfms));

        return new RepoAnalysis(result, tfms);
    }

    /// <summary>
    /// Ergänzt alle PackageVersion-Einträge, die keine direkte PackageReference haben.
    ///
    /// Bewusst unabhängig davon, ob CentralPackageTransitivePinningEnabled im Repo
    /// gesetzt ist: Ein Eintrag ohne Referenz ist entweder ein transitiver Pin (dann
    /// wird er gebraucht) oder ein Überbleibsel (dann kostet er im Feed ein paar
    /// hundert KB). Ein Paket zu viel ist harmlos, ein fehlendes bricht den
    /// Offline-Build — und ein vergessenes Pinning-Flag im Quell-Repo darf hier
    /// nicht zum stillen Verlust führen.
    /// </summary>
    private static int AddUnreferencedPins(
        string ownerRepo,
        Dictionary<string, string> cpmVersions,
        Dictionary<string, string> origin,
        List<PackageRef> result)
    {
        var referenced = new HashSet<string>(
            result.Select(r => r.Id), System.StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var (id, version) in cpmVersions)
        {
            if (referenced.Contains(id)) continue;

            var file = origin.TryGetValue(id, out var f) ? f : "Directory.Packages.props";
            result.Add(new PackageRef(id, version, null, ownerRepo, file));
            added++;
        }
        return added;
    }

    /// <summary>
    /// Liest TargetFramework/TargetFrameworks. MSBuild-Properties wie
    /// <c>$(MeinTfm)</c> werden verworfen — die kann Nougat nicht auflösen, und
    /// ein unaufgelöster Platzhalter im Anker bricht den Restore.
    /// </summary>
    internal static void CollectTargetFrameworks(string xml, HashSet<string> target)
    {
        var doc = XDocument.Parse(xml);
        foreach (var el in doc.Descendants().Where(e =>
                     e.Name.LocalName is "TargetFramework" or "TargetFrameworks"))
        {
            foreach (var raw in el.Value.Split(';', System.StringSplitOptions.RemoveEmptyEntries
                                                  | System.StringSplitOptions.TrimEntries))
            {
                if (IsUsableTfm(raw))
                    target.Add(raw);
            }
        }
    }

    private static bool IsUsableTfm(string tfm) =>
        !tfm.Contains('$', System.StringComparison.Ordinal)
        && System.Text.RegularExpressions.Regex.IsMatch(
            tfm, @"^(net\d+\.\d+(-[a-z]+[\d.]*)?|netstandard\d\.\d|net\d{3})$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

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

    /// <summary>
    /// Liest die PackageVersion-Einträge und meldet zurück, ob das Repo
    /// <c>CentralPackageTransitivePinningEnabled</c> aktiviert hat.
    /// </summary>
    internal static bool ParseCpm(string xml, Dictionary<string, string> versions)
    {
        var doc = XDocument.Parse(xml);
        foreach (var el in doc.Descendants("PackageVersion"))
        {
            var id = (string?)el.Attribute("Include");
            var version = (string?)el.Attribute("Version");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(version))
                versions[id] = version;
        }

        return doc.Descendants("CentralPackageTransitivePinningEnabled")
            .Any(e => string.Equals(e.Value.Trim(), "true", System.StringComparison.OrdinalIgnoreCase));
    }
}
