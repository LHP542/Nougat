using System.Collections.Generic;

namespace Nougat.Models;

/// <summary>Persistierte Anwendungseinstellungen (unter $XDG_CONFIG_HOME/Nougat/settings.json).</summary>
public sealed class AppSettings
{
    /// <summary>Verschlüsselter GitHub-PAT (Format "ENC1:&lt;base64&gt;"). Optional.</summary>
    public string? EncryptedPat { get; set; }

    /// <summary>Zielordner für das erzeugte NuGet-Local-Bundle.</summary>
    public string OutputDirectory { get; set; } = "";

    /// <summary>Arbeitsverzeichnis für den hierarchischen Restore-Cache.</summary>
    public string WorkDirectory { get; set; } = "";

    /// <summary>Aktive Target-RIDs (Default: win-x64).</summary>
    public List<string> TargetRids { get; set; } = ["win-x64"];

    /// <summary>Cache-Time-To-Live für die Repo-Liste in Stunden.</summary>
    public int RepoCacheTtlHours { get; set; } = 24;

    /// <summary>Gecacheter Pfad zur dotnet-Executable (falls von uns installiert).</summary>
    public string? CachedDotnetPath { get; set; }

    /// <summary>Archivierte Repos in der Liste anzeigen?</summary>
    public bool ShowArchivedRepos { get; set; }
}
