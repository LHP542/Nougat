using CommunityToolkit.Mvvm.ComponentModel;
using Nougat.Infrastructure;

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
        Version = "Version " + AppVersion.Current;
    }
}
