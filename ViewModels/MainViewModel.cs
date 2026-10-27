using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Certificates;
using Joktan.Mqtt;
using Joktan.Notes;
using Joktan.Telemetry;

namespace Joktan.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly MqttViewModel _mqtt;
    private readonly ContainersViewModel _containers = new();
    private readonly NotesViewModel _notes;
    private NavItemViewModel? _selectedItem;

    // Used by the XAML previewer.
    public MainViewModel() : this(new MachineMonitor())
    {
    }

    /// <param name="certificates">The certificate store, if already read; otherwise it is read here.</param>
    /// <param name="mqttSettings">The MQTT settings, if already read; otherwise they are read here.</param>
    /// <param name="notes">The notes, if already read; otherwise they are read here.</param>
    public MainViewModel(MachineMonitor monitor, CertificateStore? certificates = null, MqttSettings? mqttSettings = null,
        NoteStore? notes = null)
    {
        certificates ??= CertificateStore.Load();
        _notes = new NotesViewModel(notes ?? NoteStore.Load());
        var mqtt = _mqtt = new MqttViewModel(certificates, mqttSettings);

        MenuItems =
        [
            new NavItemViewModel("Dashboard", Icons.Dashboard,
                new NavItemViewModel("Overview") { Page = new DashboardViewModel(monitor) },
                new NavItemViewModel("Analytics"),
                new NavItemViewModel("Reports")),
            new NavItemViewModel("Users", Icons.Users,
                new NavItemViewModel("All Users"),
                new NavItemViewModel("Roles")),
            new NavItemViewModel("Notes", Icons.Notes) { Page = _notes },
            new NavItemViewModel("Charts", Icons.Chart),
            new NavItemViewModel("Tables", Icons.Table),
            new NavItemViewModel("Dev Tools", Icons.DevTools,
                new NavItemViewModel("Connectivity", null,
                    new NavItemViewModel("MQTT") { Page = mqtt },
                    new NavItemViewModel("Topic Explorer") { Page = new TopicExplorerViewModel(mqtt) },
                    new NavItemViewModel("Certificates") { Page = new CertificatesViewModel(certificates, mqtt) }),
                new NavItemViewModel("Containers") { Page = _containers }),
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

    /// <summary>Writes anything not yet saved and stops the commands still running. Called when the app exits.</summary>
    public void SaveState()
    {
        _mqtt.SaveState();
        _notes.SaveState();
        _containers.Shutdown();
    }

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
