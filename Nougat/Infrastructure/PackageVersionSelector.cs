using System;
using Semver;

namespace Nougat.Infrastructure;

/// <summary>
/// Vergleicht zwei Paket-Version-Strings und liefert die "höhere". Reihenfolge der Versuche:
/// 1) SemVer 2.0 (Semver-NuGet-Paket) — deckt "1.2.3-preview.1+build" ab.
/// 2) NuGet-Range mit eckigen Klammern "[7.2.2,8.0.0)" — untere Grenze verwenden.
/// 3) System.Version — vier Segmente wie "10.0.7".
/// 4) StringComparer.OrdinalIgnoreCase als letzte Not — alphabetisch letztes.
/// </summary>
public static class PackageVersionSelector
{
    /// <summary>Wählt aus zwei Versions-Strings den "höheren".</summary>
    public static string PickHigher(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a)) return b;
        if (string.IsNullOrWhiteSpace(b)) return a;
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return a;

        var normA = Normalize(a);
        var normB = Normalize(b);

        // SemVer bevorzugt
        if (SemVersion.TryParse(normA, SemVersionStyles.Any, out var sa) &&
            SemVersion.TryParse(normB, SemVersionStyles.Any, out var sb))
        {
            return SemVersion.SortOrderComparer.Compare(sa, sb) >= 0 ? a : b;
        }

        // System.Version fallback
        if (Version.TryParse(normA, out var va) && Version.TryParse(normB, out var vb))
        {
            return va >= vb ? a : b;
        }

        return string.CompareOrdinal(normA, normB) >= 0 ? a : b;
    }

    /// <summary>Wandelt NuGet-Range-Notation "[1.0,2.0)" in die untere Grenze um.</summary>
    private static string Normalize(string version)
    {
        var v = version.Trim();
        if (v.Length == 0) return v;

        if (v[0] == '[' || v[0] == '(')
        {
            var inner = v.Trim('[', '(', ']', ')');
            var comma = inner.IndexOf(',');
            if (comma > 0)
                return inner[..comma].Trim();
            return inner;
        }

        return v;
    }
}
