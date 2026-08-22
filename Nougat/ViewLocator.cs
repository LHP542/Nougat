using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Nougat.ViewModels;

namespace Nougat;

/// <summary>
/// Bindet ViewModels an Views nach Namenskonvention:
/// Nougat.ViewModels.FooViewModel -> Nougat.Views.FooView / Nougat.Views.FooWindow
/// </summary>
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null) return null;
        var vmName = param.GetType().FullName!;
        var viewName = vmName.Replace(".ViewModels.", ".Views.", StringComparison.Ordinal);
        var candidates = new[]
        {
            viewName.EndsWith("ViewModel", StringComparison.Ordinal)
                ? viewName[..^"ViewModel".Length] + "View"
                : viewName,
            viewName.EndsWith("ViewModel", StringComparison.Ordinal)
                ? viewName[..^"ViewModel".Length] + "Window"
                : viewName,
        };

        foreach (var candidate in candidates)
        {
            var type = Type.GetType(candidate);
            if (type is not null)
                return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Nicht gefunden: " + vmName };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
