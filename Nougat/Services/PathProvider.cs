using System;
using System.IO;

namespace Nougat.Services;

/// <summary>Einheitliche Ableitung aller Nougat-Pfade (XDG bzw. AppData).</summary>
public static class PathProvider
{
    public const string AppName = "Nougat";

    public static string ConfigDirectory
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var baseDir = !string.IsNullOrWhiteSpace(xdg)
                ? xdg
                : Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(baseDir, AppName);
        }
    }

    public static string SettingsFilePath => Path.Combine(ConfigDirectory, "settings.json");
    public static string RepoCacheFilePath => Path.Combine(ConfigDirectory, "repos.cache.json");
    public static string ProtectKeyPath => Path.Combine(ConfigDirectory, "protect.key");

    public static string DataDirectory
    {
        get
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            var baseDir = !string.IsNullOrWhiteSpace(xdg)
                ? xdg
                : Environment.OSVersion.Platform == PlatformID.Win32NT
                    ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(baseDir, AppName);
        }
    }

    public static string DefaultWorkDirectory => Path.Combine(DataDirectory, "work");

    public static string DefaultOutputDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                     "nuget-offline-work", "NuGet-Local");

    /// <summary>Eigener SDK-Installationspfad (nicht ~/.dotnet überschreiben!).</summary>
    public static string ManagedDotnetDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet-nougat");

    public static string ManagedDotnetExecutable =>
        OperatingSystem.IsWindows()
            ? Path.Combine(ManagedDotnetDirectory, "dotnet.exe")
            : Path.Combine(ManagedDotnetDirectory, "dotnet");

    public static void EnsureConfigDirectory()
    {
        Directory.CreateDirectory(ConfigDirectory);
    }
}
