using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Containers;
using Joktan.Containers.ViewModels;
using Joktan.Core.ViewModels;
using Joktan.Data.Postgres;
using Joktan.Data.Resources;

namespace Joktan.Data.ViewModels;

/// <summary>
/// Data: where data is kept, a tab each. MQTT has the Mosquitto brokers and Database the PostgreSQL
/// servers, which run as containers through Dev Tools / Containers. Specified in docs/features/data/spec.md.
/// </summary>
public partial class DataViewModel : PageViewModel
{
    public const int MqttTab = 0;
    public const int DatabaseTab = 1;

    /// <param name="connect">Opens a database for the browser; over the network otherwise.</param>
    public DataViewModel(ContainersViewModel containers, Func<PostgresDatabase, string, IDatabaseConnection>? connect = null)
        : base(Strings.Data_Data, Strings.Data_BrokersAndDatabases)
    {
        Containers = containers;
        Browser = new DatabaseBrowserViewModel(containers.Databases, connect);
    }

    /// <summary>The Containers page, which runs the brokers' commands and shows what is under way.</summary>
    public ContainersViewModel Containers { get; }

    public MosquittoBrokersViewModel Brokers => Containers.Brokers;

    public PostgresDatabasesViewModel Databases => Containers.Databases;

    /// <summary>The schemas, tables and rows of the database chosen on the Database tab.</summary>
    public DatabaseBrowserViewModel Browser { get; }

    [ObservableProperty]
    public partial int SelectedTab { get; set; } = MqttTab;

    /// <summary>Starts checking the brokers' and databases' containers every few seconds. Called while the page is shown.</summary>
    public void Activate() => Containers.Activate();

    public void Deactivate() => Containers.Deactivate();
}
