using Nougat.Chrome;
using Nougat.ViewModels;

namespace Nougat.Views;

public partial class SettingsWindow : ChromeWindow
{
    // Parameterloser Ctor für den XAML-Loader (Design-Time).
    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(SettingsWindowViewModel vm) : this()
    {
        DataContext = vm;
        vm.CloseRequested += (_, _) => Close();
    }
}
