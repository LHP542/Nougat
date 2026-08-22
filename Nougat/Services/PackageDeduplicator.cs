using System;
using System.Collections.Generic;
using System.Linq;
using Nougat.Infrastructure;
using Nougat.Models;

namespace Nougat.Services;

public sealed record DeduplicationResult(
    IReadOnlyList<PackageRef> Packages,
    IReadOnlyList<ConflictInfo> Conflicts,
    IReadOnlyList<string> Warnings
);

/// <summary>
/// Führt PackageReferences aus mehreren Repos zusammen. Bei Version-Konflikten
/// gewinnt die "höchste" Version (siehe <see cref="PackageVersionSelector"/>).
/// PackageReferences OHNE Version werden verworfen (Warning).
/// ConflictInfo listet pro Version die Herkunfts-Repos.
/// </summary>
public sealed class PackageDeduplicator
{
    public DeduplicationResult Deduplicate(IEnumerable<PackageRef> packages)
    {
        var byId = new Dictionary<string, List<PackageRef>>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();

        foreach (var p in packages)
        {
            if (string.IsNullOrWhiteSpace(p.Version))
            {
                warnings.Add(
                    $"{p.SourceRepo}: '{p.Id}' hat keine Version (weder in {p.SourceFile} noch in " +
                    "Directory.Packages.props). Wird übersprungen.");
                continue;
            }

            if (!byId.TryGetValue(p.Id, out var list))
            {
                list = [];
                byId[p.Id] = list;
            }
            list.Add(p);
        }

        var picked = new List<PackageRef>(byId.Count);
        var conflicts = new List<ConflictInfo>();

        foreach (var (id, refs) in byId)
        {
            // Winner: höchste Version quer über alle Vorkommen
            var winner = refs[0];
            foreach (var candidate in refs.Skip(1))
            {
                var higher = PackageVersionSelector.PickHigher(winner.Version!, candidate.Version!);
                winner = higher == candidate.Version ? candidate : winner;
            }
            picked.Add(winner);

            // Konflikt-Diagnostik: pro Version die eindeutigen Source-Repos zusammenfassen
            var byVersion = refs
                .GroupBy(r => r.Version!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<string>)g.Select(r => r.SourceRepo)
                                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                                 .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                                                 .ToList(),
                    StringComparer.OrdinalIgnoreCase);

            if (byVersion.Count > 1)
            {
                var chosenRepos = byVersion[winner.Version!];
                var discarded = byVersion
                    .Where(kv => !string.Equals(kv.Key, winner.Version, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new VersionSource(kv.Key, kv.Value))
                    .ToList();

                conflicts.Add(new ConflictInfo(id, winner.Version!, chosenRepos, discarded));
            }
        }

        return new DeduplicationResult(picked, conflicts, warnings);
    }
}
