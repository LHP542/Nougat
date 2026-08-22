using FluentAssertions;
using Nougat.Services;
using Xunit;

namespace Nougat.Tests;

public class LinuxAesGcmSecretStoreTests : IDisposable
{
    private readonly string _keyPath;

    public LinuxAesGcmSecretStoreTests()
    {
        _keyPath = Path.Combine(Path.GetTempPath(), $"nougat-key-{Guid.NewGuid():N}.bin");
    }

    public void Dispose()
    {
        if (File.Exists(_keyPath)) File.Delete(_keyPath);
    }

    [Fact]
    public void Empty_and_null_pass_through()
    {
        var store = new LinuxAesGcmSecretStore(_keyPath);
        store.Protect(null).Should().BeNull();
        store.Protect("").Should().Be("");
        store.Unprotect(null).Should().BeNull();
        store.Unprotect("").Should().Be("");
    }

    [Fact]
    public void Roundtrip_recovers_plaintext()
    {
        var store = new LinuxAesGcmSecretStore(_keyPath);
        var encrypted = store.Protect("ghp_secret_token_1234");
        encrypted.Should().StartWith("ENC1:");
        store.Unprotect(encrypted).Should().Be("ghp_secret_token_1234");
    }

    [Fact]
    public void Second_encryption_produces_different_ciphertext()
    {
        var store = new LinuxAesGcmSecretStore(_keyPath);
        var a = store.Protect("hallo");
        var b = store.Protect("hallo");
        a.Should().NotBe(b); // Random Nonce -> unterschiedliches Cipher
    }

    [Fact]
    public void Legacy_plain_string_returned_as_is()
    {
        var store = new LinuxAesGcmSecretStore(_keyPath);
        // Kein ENC1-Prefix -> Klartext, tolerant zurückgeben
        store.Unprotect("klartext").Should().Be("klartext");
    }
}
