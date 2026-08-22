using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Nougat.ViewModels;

public partial class InfoWindowViewModel : ViewModelBase
{
    public string Version { get; }
    public string GithubUrl => "https://github.com/LHP542/Nougat";
    public string CoffeeUrl => "https://www.buymeacoffee.com/kroste";
    /// <summary>Arbeitsgruppe, unter der das Werkzeug entsteht.</summary>
    public string Organisation => "Arbeitsgruppe 5424 IT-Basis-Dienste";

    public string Description =>
        "Offline-NuGet-Bundle-Builder für die Repos der GitHub-Organisation LHP542.\n" +
        "Wähle Repos aus, Nougat sammelt PackageReferences, führt dotnet restore aus\n" +
        "und schreibt einen NuGet-Local-Ordner (analog zu nuget-offline-bundle.sh).";

    public InfoWindowViewModel()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        Version = "Version " + (v?.ToString(3) ?? "0.1.0");
    }
}
