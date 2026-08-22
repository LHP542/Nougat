using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Nougat.Services;

/// <summary>
/// Datei-basierter Secret-Store für Linux/macOS: AES-GCM mit lokalem Master-Key
/// unter ~/.config/Nougat/protect.key (Modus 0600). Nicht so stark wie DPAPI oder
/// libsecret, aber deutlich besser als Klartext und kommt ohne DBus-Dependency aus.
/// Prefix "ENC1:" markiert den Chiffre-String.
/// </summary>
public sealed class LinuxAesGcmSecretStore : ISecretStore
{
    private const string Prefix = "ENC1:";
    private const int KeySize = 32;   // AES-256
    private const int NonceSize = 12; // AES-GCM Standard
    private const int TagSize = 16;

    private readonly string _keyPath;

    public LinuxAesGcmSecretStore() : this(PathProvider.ProtectKeyPath) { }

    public LinuxAesGcmSecretStore(string keyPath)
    {
        _keyPath = keyPath;
    }

    public string? Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return plaintext;

        var key = LoadOrCreateKey();
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        // Format: nonce | tag | cipher
        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);

        return Prefix + Convert.ToBase64String(payload);
    }

    public string? Unprotect(string? encrypted)
    {
        if (string.IsNullOrEmpty(encrypted)) return encrypted;
        if (!encrypted.StartsWith(Prefix, StringComparison.Ordinal))
            return encrypted; // Legacy-/Klartext-Fallback (bewusst tolerant)

        var payload = Convert.FromBase64String(encrypted[Prefix.Length..]);
        if (payload.Length < NonceSize + TagSize)
            throw new CryptographicException("Chiffre zu kurz.");

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var cipher = new byte[payload.Length - NonceSize - TagSize];
        Buffer.BlockCopy(payload, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(payload, NonceSize, tag, 0, TagSize);
        Buffer.BlockCopy(payload, NonceSize + TagSize, cipher, 0, cipher.Length);

        var key = LoadOrCreateKey();
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    private byte[] LoadOrCreateKey()
    {
        var dir = Path.GetDirectoryName(_keyPath)!;
        Directory.CreateDirectory(dir);

        if (File.Exists(_keyPath))
        {
            var existing = File.ReadAllBytes(_keyPath);
            if (existing.Length == KeySize) return existing;
        }

        var key = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllBytes(_keyPath, key);
        TrySetPermissions(_keyPath);
        return key;
    }

    private static void TrySetPermissions(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best-effort - Filesystem unterstützt evtl. keine POSIX-Modi.
        }
    }
}
