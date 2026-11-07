using Joktan.Containers.Resources;
using Joktan.Core.ViewModels;

namespace Joktan.Containers.ViewModels;

/// <summary>
/// Settings / Container Settings: the compose files on the Containers page, the engine, where the
/// Mosquitto brokers' and the PostgreSQL databases' compose projects are written, and where the
/// databases keep their data. Each change goes through the Containers page,
/// which saves it in containers.json.
/// </summary>
public class ContainerSettingsViewModel : PageViewModel
{
    public ContainerSettingsViewModel(ContainersViewModel containers)
        : base(Strings.ContainerSettings_ContainerSettings, Strings.ContainerSettings_SettingsContainerSettings)
    {
        Containers = containers;
    }

    public ContainersViewModel Containers { get; }

    public MosquittoBrokersViewModel Brokers => Containers.Brokers;

    public PostgresDatabasesViewModel Databases => Containers.Databases;
}
