using System.Collections.Generic;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Joktan.ViewModels;

public partial class NavItemViewModel : ViewModelBase
{
    public NavItemViewModel(string title, Geometry? icon = null, params NavItemViewModel[] children)
    {
        Title = title;
        Icon = icon;
        Children = children;
    }

    public string Title { get; }

    public Geometry? Icon { get; }

    public IReadOnlyList<NavItemViewModel> Children { get; }

    public bool HasChildren => Children.Count > 0;

    /// <summary>The page this entry opens; null shows the placeholder page.</summary>
    public PageViewModel? Page { get; init; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
