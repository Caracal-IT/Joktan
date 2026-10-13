using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Telemetry;

namespace Joktan.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private NavItemViewModel? _selectedItem;

    // Used by the XAML previewer.
    public MainViewModel() : this(new MachineMonitor())
    {
    }

    public MainViewModel(MachineMonitor monitor)
    {
        MenuItems =
        [
            new NavItemViewModel("Dashboard", Icons.Dashboard,
                new NavItemViewModel("Overview") { Page = new DashboardViewModel(monitor) },
                new NavItemViewModel("Analytics"),
                new NavItemViewModel("Reports")),
            new NavItemViewModel("Users", Icons.Users,
                new NavItemViewModel("All Users"),
                new NavItemViewModel("Roles")),
            new NavItemViewModel("Pages", Icons.Page),
            new NavItemViewModel("Charts", Icons.Chart),
            new NavItemViewModel("Tables", Icons.Table),
            new NavItemViewModel("Settings", Icons.Settings),
        ];

        MenuItems[0].IsExpanded = true;
        Navigate(MenuItems[0].Children[0]);
    }

    public IReadOnlyList<NavItemViewModel> MenuItems { get; }

    public string UserName => "Admin User";

    public string UserRole => "Administrator";

    public string UserInitials => "AU";

    [ObservableProperty]
    public partial bool IsSidebarOpen { get; set; } = true;

    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; set; }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    [RelayCommand]
    private void Navigate(NavItemViewModel item)
    {
        if (item.HasChildren)
        {
            item.IsExpanded = !item.IsExpanded;
            return;
        }

        if (_selectedItem is not null)
            SetSelected(_selectedItem, false);

        _selectedItem = item;
        SetSelected(item, true);

        var parent = FindParent(item);
        var path = parent is null ? item.Title : $"{parent.Title} / {item.Title}";

        using var activity = TelemetryHost.ActivitySource.StartActivity("navigate");
        activity?.SetTag("joktan.page", path);

        CurrentPage = item.Page ?? new PageViewModel(item.Title, path);
    }

    // A selected child also marks its group as selected so the group stays highlighted.
    private void SetSelected(NavItemViewModel item, bool selected)
    {
        item.IsSelected = selected;
        if (FindParent(item) is { } parent)
            parent.IsSelected = selected;
    }

    private NavItemViewModel? FindParent(NavItemViewModel item)
        => MenuItems.FirstOrDefault(m => m.Children.Contains(item));
}
