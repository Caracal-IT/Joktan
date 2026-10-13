using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Telemetry;

namespace Joktan.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly MqttViewModel _mqtt;
    private NavItemViewModel? _selectedItem;

    // Used by the XAML previewer.
    public MainViewModel() : this(new MachineMonitor())
    {
    }

    public MainViewModel(MachineMonitor monitor)
    {
        var mqtt = _mqtt = new MqttViewModel();

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
            new NavItemViewModel("Dev Tools", Icons.DevTools,
                new NavItemViewModel("Connectivity", null,
                    new NavItemViewModel("MQTT") { Page = mqtt },
                    new NavItemViewModel("Topic Explorer") { Page = new TopicExplorerViewModel(mqtt) })),
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

    /// <summary>Writes anything not yet saved. Called when the app exits.</summary>
    public void SaveState() => _mqtt.SaveSettings();

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

        var path = string.Join(" / ", PathTo(item).Select(i => i.Title));

        using var activity = TelemetryHost.ActivitySource.StartActivity("navigate");
        activity?.SetTag("joktan.page", path);

        CurrentPage = item.Page ?? new PageViewModel(item.Title, path);
    }

    // Selecting an entry also marks the groups above it, so they stay highlighted.
    private void SetSelected(NavItemViewModel item, bool selected)
    {
        foreach (var entry in PathTo(item))
            entry.IsSelected = selected;
    }

    /// <summary>The entry and the groups above it, outermost first.</summary>
    private List<NavItemViewModel> PathTo(NavItemViewModel item)
    {
        var path = new List<NavItemViewModel>();
        Search(MenuItems);
        return path;

        bool Search(IReadOnlyList<NavItemViewModel> entries)
        {
            foreach (var entry in entries)
            {
                path.Add(entry);
                if (entry == item || Search(entry.Children))
                    return true;
                path.RemoveAt(path.Count - 1);
            }

            return false;
        }
    }
}
