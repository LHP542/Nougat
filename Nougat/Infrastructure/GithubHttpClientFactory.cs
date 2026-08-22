using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Nougat.Models;
using Nougat.Services;

namespace Nougat.Infrastructure;

/// <summary>Konfiguriert den named HttpClient "github" mit User-Agent, PAT und System-Proxy.</summary>
public static class GithubHttpClientFactory
{
    public static IServiceCollection AddGithubHttpClient(this IServiceCollection services)
    {
        // Dieselbe Quelle wie Info-Fenster und Update-Check: AssemblyVersion
        // rundet Prereleases auf X.Y.Z.0 und wuerde im User-Agent luegen.
        var version = AppVersion.Current;

        services.AddHttpClient(GithubRepoService.HttpClientName, (sp, client) =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Nougat", version));

            var settings = sp.GetService<AppSettings>();
            var secretStore = sp.GetService<ISecretStore>();
            var pat = secretStore?.Unprotect(settings?.EncryptedPat);
            if (!string.IsNullOrWhiteSpace(pat))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", pat);
                MaskingLayoutRenderer.Register(pat);
            }
        })
        .ConfigurePrimaryHttpMessageHandler(_ => new HttpClientHandler
        {
            UseProxy = true,
            Proxy = WebRequest.GetSystemWebProxy(),
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        });

        return services;
    }
}
