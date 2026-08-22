using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using Nougat.Models;
using Nougat.Services;
using Nougat.ViewModels;

namespace Nougat.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNougat(this IServiceCollection services)
    {
        services.AddLogging(b => b.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddNLog());

        // Settings + Cache
        services.AddSingleton<SettingsService>();
        services.AddSingleton<AppSettings>(sp => sp.GetRequiredService<SettingsService>().Load());
        services.AddSingleton<RepoCacheService>();

        // Secrets
        services.AddSingleton<ISecretStore, LinuxAesGcmSecretStore>();

        // HttpClient für GitHub (User-Agent, Proxy, PAT)
        services.AddGithubHttpClient();

        // Kern-Services
        services.AddSingleton<ProcessRunner>();
        services.AddSingleton<GithubRepoService>();
        services.AddSingleton<CsprojAnalyzer>();
        services.AddSingleton<PackageDeduplicator>();
        services.AddSingleton<DotnetSdkService>();
        services.AddSingleton<AnchorProjectGenerator>();
        services.AddSingleton<RestoreRunner>();
        services.AddSingleton<BundleAssembler>();
        services.AddSingleton<NugetConfigWriter>();
        services.AddSingleton<BundleOrchestrator>();
        services.AddSingleton<UpdateService>();

        // ViewModels (transient - jedes Fenster bekommt frische Instanz)
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<SettingsWindowViewModel>();
        services.AddTransient<InfoWindowViewModel>();
        services.AddTransient<SdkInstallViewModel>();

        return services;
    }
}
