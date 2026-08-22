namespace Nougat.Services;

/// <summary>Ver-/Entschlüsselt Secrets für die inline-Persistierung in settings.json.</summary>
public interface ISecretStore
{
    /// <summary>Verschlüsselt Klartext, gibt "ENC1:&lt;base64&gt;" zurück (leer wenn null/leer).</summary>
    string? Protect(string? plaintext);

    /// <summary>Entschlüsselt "ENC1:..."-Wert; leer/null bleibt leer/null.</summary>
    string? Unprotect(string? encrypted);
}
