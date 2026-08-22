using CommunityToolkit.Mvvm.ComponentModel;
using Nougat.Models;

namespace Nougat.ViewModels;

public partial class RepoItemViewModel : ViewModelBase
{
    public RepoInfo Repo { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    public event EventHandler? SelectionChanged;

    public string Name => Repo.Name;
    public string FullName => Repo.FullName;
    public string DefaultBranch => Repo.DefaultBranch;
    public string? Description => Repo.Description;
    public bool IsArchived => Repo.IsArchived;

    public RepoItemViewModel(RepoInfo repo, bool selected)
    {
        Repo = repo;
        IsSelected = selected;
    }

    partial void OnIsSelectedChanged(bool value) => SelectionChanged?.Invoke(this, EventArgs.Empty);
}
