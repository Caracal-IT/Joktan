using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Containers.ViewModels;
using Joktan.Core.ViewModels;
using Joktan.Data.Resources;

namespace Joktan.Data.ViewModels;

/// <summary>
/// Data: where data is kept, a tab each. MQTT has the Mosquitto brokers, which run as containers
/// through Dev Tools / Containers; Database has nothing yet. Specified in docs/features/data/spec.md.
/// </summary>
public partial class DataViewModel : PageViewModel
{
    public const int MqttTab = 0;
    public const int DatabaseTab = 1;

    public DataViewModel(ContainersViewModel containers) : base(Strings.Data_Data, Strings.Data_BrokersAndDatabases)
    {
        Containers = containers;
    }

    /// <summary>The Containers page, which runs the brokers' commands and shows what is under way.</summary>
    public ContainersViewModel Containers { get; }

    public MosquittoBrokersViewModel Brokers => Containers.Brokers;

    [ObservableProperty]
    public partial int SelectedTab { get; set; } = MqttTab;

    /// <summary>Starts checking the brokers' containers every few seconds. Called while the page is shown.</summary>
    public void Activate() => Containers.Activate();

    public void Deactivate() => Containers.Deactivate();
}
