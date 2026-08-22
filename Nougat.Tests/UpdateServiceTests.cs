using System;
using System.Collections.Generic;
using FluentAssertions;
using Nougat.Infrastructure;
using Nougat.Services;
using Xunit;

namespace Nougat.Tests;

public class VersionComparisonTests
{
    [Theory]
    [InlineData("1.2.0", "1.1.9", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.1.9", "1.2.0", false)]
    [InlineData("2.0.0", "1.99.99", true)]
    public void IsNewer_vergleicht_semantisch(string candidate, string current, bool expected) =>
        PackageVersionSelector.IsNewer(candidate, current).Should().Be(expected);

    [Fact]
    public void IsNewer_Release_schlaegt_Prerelease()
    {
        // MinVer erzeugt zwischen zwei Tags Versionen wie 0.2.0-alpha.0.5.
        // Laeuft so eine Version, ist das Release 0.2.0 ein echtes Update.
        PackageVersionSelector.IsNewer("0.2.0", "0.2.0-alpha.0.5").Should().BeTrue();

        // Umgekehrt darf ein Prerelease kein Update auf das fertige Release ausloesen.
        PackageVersionSelector.IsNewer("0.2.0-alpha.0.5", "0.2.0").Should().BeFalse();
    }

    [Fact]
    public void IsNewer_ohne_Kandidat_meldet_kein_Update() =>
        PackageVersionSelector.IsNewer("", "1.0.0").Should().BeFalse();
}

public class ReleaseAssetSelectionTests
{
    private static List<UpdateService.ReleaseAsset> FullRelease() =>
    [
        new() { Name = "Nougat-1.2.0-win-x64.zip", Url = "https://api/1", Size = 10 },
        new() { Name = "Nougat-1.2.0-linux-x64.tar.gz", Url = "https://api/2", Size = 20 },
        new() { Name = "Nougat-1.2.0-x86_64.AppImage", Url = "https://api/3", Size = 30 },
    ];

    [Fact]
    public void Waehlt_das_Paket_der_laufenden_Plattform()
    {
        var picked = UpdateService.SelectAsset(FullRelease(), "1.2.0");
        picked.Should().NotBeNull();

        var expected = OperatingSystem.IsWindows()
            ? "Nougat-1.2.0-win-x64.zip"
            // Ohne gesetzte APPIMAGE-Variable laeuft die App aus einem Verzeichnis,
            // also ist das Tarball das richtige Paket.
            : Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 }
                ? "Nougat-1.2.0-x86_64.AppImage"
                : "Nougat-1.2.0-linux-x64.tar.gz";

        picked!.Name.Should().Be(expected);
    }

    [Fact]
    public void Ohne_Assets_gibt_es_kein_Self_Update() =>
        UpdateService.SelectAsset([], "1.2.0").Should().BeNull();

    [Fact]
    public void Fremde_Assets_werden_nicht_verwechselt()
    {
        List<UpdateService.ReleaseAsset> onlyChecksums =
        [
            new() { Name = "checksums.txt", Url = "https://api/9", Size = 1 },
        ];

        UpdateService.SelectAsset(onlyChecksums, "1.2.0").Should().BeNull();
    }
}
