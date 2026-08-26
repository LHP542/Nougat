using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nougat.Services;
using Xunit;

namespace Nougat.Tests;

/// <summary>
/// Integrationstest gegen die echte GitHub-API. Läuft NUR, wenn
/// NOUGAT_INTEGRATION=1 gesetzt ist — im CI gibt es weder Netz-Garantie noch
/// API-Rate-Limit-Reserve, und ein Test, der eine Dev-Umgebung voraussetzt,
/// färbt sonst die Pipeline rot.
///
/// Zweck: belegt, dass die Analyse eines Repos mit
/// CentralPackageTransitivePinningEnabled auch die Einträge einsammelt, die kein
/// Projekt direkt referenziert. Der erzeugte Anker landet in
/// NOUGAT_INTEGRATION_OUT und kann dort von Hand restored werden.
/// </summary>
public class CheckmkIntegrationTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("NOUGAT_INTEGRATION") == "1";

    [Fact]
    public async Task Checkmk_Analyse_enthaelt_die_reinen_Pins()
    {
        if (!Enabled)
            Assert.Skip("NOUGAT_INTEGRATION=1 setzen, um gegen die echte API zu testen.");

        var services = new ServiceCollection();
        services.AddHttpClient(GithubRepoService.HttpClientName, c =>
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Nougat-Test/1.0"));
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        var github = new GithubRepoService(factory, NullLogger<GithubRepoService>.Instance);
        var analyzer = new CsprojAnalyzer(github, NullLogger<CsprojAnalyzer>.Instance);

        var analysis = await analyzer.AnalyzeRepoAsync("LHP542/Checkmk", "main", TestContext.Current.CancellationToken);

        // Steht nur in der Directory.Packages.props, kein Projekt referenziert es.
        // Vor dem Fix fehlte es komplett und landete als 9.0.11 im Bundle.
        analysis.Packages.Should().Contain(p =>
            p.Id == "System.Configuration.ConfigurationManager" && p.Version == "10.0.11");
        analysis.Packages.Should().Contain(p => p.Id == "Microsoft.Data.SqlClient");
        analysis.Packages.Should().Contain(p => p.Id == "Microsoft.Extensions.Primitives");

        // Checkmk.App baut gegen ein Windows-TFM — ohne das fehlen die
        // Windows-Ref-/Runtime-Packs im Bundle.
        analysis.TargetFrameworks.Should().Contain("net10.0");
        analysis.TargetFrameworks.Should().Contain(t => t.StartsWith("net10.0-windows", StringComparison.OrdinalIgnoreCase));

        var dedup = new PackageDeduplicator().Deduplicate(analysis.Packages);

        var outDir = Environment.GetEnvironmentVariable("NOUGAT_INTEGRATION_OUT");
        if (!string.IsNullOrWhiteSpace(outDir))
        {
            new AnchorProjectGenerator().WriteToWorkDirectory(
                outDir, dedup.Packages, out _, analysis.TargetFrameworks);
            File.Exists(Path.Combine(outDir, "Directory.Packages.props")).Should().BeTrue();
        }
    }

    /// <summary>
    /// Zwei Repos, die dasselbe Paket in unterschiedlichen Versionen festlegen.
    /// Checkmk pinnt Tmds.DBus.Protocol bewusst auf 0.94.1 (Avalonia 12.1.0 wirft
    /// sonst eine MissingTypeException), Klemmbrett-Scaffold nutzt 0.94.2. Beide
    /// Versionen müssen ins Bundle — sonst löst der Offline-Restore mit NU1603
    /// still auf die falsche auf und die App fällt erst zur Laufzeit um.
    /// </summary>
    [Fact]
    public async Task Zwei_Repos_mit_unterschiedlichen_Versionen_bekommen_eigene_Anker()
    {
        if (!Enabled)
            Assert.Skip("NOUGAT_INTEGRATION=1 setzen, um gegen die echte API zu testen.");

        var services = new ServiceCollection();
        services.AddHttpClient(GithubRepoService.HttpClientName, c =>
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Nougat-Test/1.0"));
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        var github = new GithubRepoService(factory, NullLogger<GithubRepoService>.Instance);
        var analyzer = new CsprojAnalyzer(github, NullLogger<CsprojAnalyzer>.Instance);
        var dedup = new PackageDeduplicator();
        var gen = new AnchorProjectGenerator();

        var outRoot = Environment.GetEnvironmentVariable("NOUGAT_INTEGRATION_OUT");
        var versions = new List<string>();

        foreach (var repo in new[] { "LHP542/Checkmk", "LHP542/Klemmbrett-Scaffold" })
        {
            var analysis = await analyzer.AnalyzeRepoAsync(repo, "main", TestContext.Current.CancellationToken);
            var packages = dedup.Deduplicate(analysis.Packages).Packages;

            var tmds = packages.FirstOrDefault(p => p.Id == "Tmds.DBus.Protocol");
            if (tmds?.Version is { } v) versions.Add(v);

            if (!string.IsNullOrWhiteSpace(outRoot))
            {
                gen.WriteToWorkDirectory(
                    Path.Combine(outRoot, "anchors", repo.Replace('/', '_')),
                    packages, out _, analysis.TargetFrameworks);
            }
        }

        // Kein Dedup ueber Repo-Grenzen: Jedes Repo behaelt seine Version.
        versions.Should().HaveCount(2);
        versions.Should().Contain("0.94.1");
        versions.Should().Contain("0.94.2");
    }
}
