using System;
using System.Reflection;

namespace Nougat.Infrastructure;

/// <summary>
/// Einzige Quelle für die Anzeige- und Vergleichsversion der App.
///
/// WARUM NICHT AssemblyName.Version: Das ist die vierstellige AssemblyVersion,
/// die MinVer bei Prereleases auf X.Y.Z.0 rundet. "0.2.0-alpha.0.5" erscheint
/// dort als "0.2.0" — die App würde zwischen zwei Tags behaupten, sie sei das
/// Release. InformationalVersion trägt die echte SemVer.
/// </summary>
public static class AppVersion
{
    /// <summary>SemVer inklusive Prerelease-Suffix, z. B. "0.2.0-alpha.0.5".</summary>
    public static string Current { get; } = Read();

    private static string Read()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // MinVer hängt die Commit-SHA als Build-Metadaten an ("1.2.3+abc1234").
            // Für die Anzeige und den Versionsvergleich ist der Teil irrelevant.
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
