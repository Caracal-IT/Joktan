using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Certificates;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// Dev Tools / Brokers / MQTT: one tab per MQTT client. Each client connects to its own
/// broker and publishes test messages, typed in by hand or read from its own folder of JSON files.
/// Specified in docs/features/dev-tools/connectivity/mqtt/spec.md.
/// </summary>
public partial class MqttViewModel : PageViewModel
{
    private readonly MqttSettings _settings;
    private readonly CredentialStore _credentials;

    private readonly CertificateStore? _certificates;

    /// <param name="certificates">The certificates made on the Certificates page, offered in the client settings.</param>
    /// <param name="settings">The settings, if already read; otherwise they are read here.</param>
    /// <param name="credentials">Where the clients' passwords are kept; the system's credential store otherwise.</param>
    public MqttViewModel(CertificateStore? certificates = null, MqttSettings? settings = null, CredentialStore? credentials = null)
        : base(Strings.Mqtt_MQTT, Strings.Mqtt_DevToolsBrokersMQTT)
    {
        _certificates = certificates;
        _credentials = credentials ?? CredentialStore.Create();
        _settings = settings ?? MqttSettings.Load();
        foreach (var profile in _settings.Clients)
            Clients.Add(NewClient(profile));
        MessagingClient = NewClient(_settings.MessagingServer());
        if (_settings.Messaging.ShowInBrokers)
            Clients.Add(MessagingClient);
        UpdateRemovable();
        SelectedClient = Find(_settings.SelectedClient) ?? Clients[0];
        _settings.Save();
    }

    /// <summary>The clients, one tab each. Shared with the topic explorer.</summary>
    public ObservableCollection<MqttClientViewModel> Clients { get; } = [];

    /// <summary>The client for the alerts and messages server. It is in <see cref="Clients"/> only while <see cref="IsMessagingClientShown"/>.</summary>
    public MqttClientViewModel MessagingClient { get; }

    /// <summary>The server for alerts and messages, and how the user appears in messages.</summary>
    public MessagingSettings Messaging => _settings.Messaging;

    /// <summary>
    /// Whether the alerts and messages server has a tab on this page and in the topic explorer. Taking
    /// it away leaves it connected, but stops what only those tabs could stop.
    /// </summary>
    public bool IsMessagingClientShown
    {
        get => Clients.Contains(MessagingClient);
        set
        {
            if (value == IsMessagingClientShown)
                return;

            Messaging.ShowInBrokers = value;
            if (value)
            {
                Clients.Add(MessagingClient);
            }
            else
            {
                MessagingClient.StopSending();
                Clients.Remove(MessagingClient);
                if (SelectedClient == MessagingClient || SelectedClient is null)
                    SelectedClient = Clients[0];
            }

            SaveSettings();
            OnPropertyChanged();
        }
    }

    // Every client, with the alerts and messages server even when it has no tab.
    private IEnumerable<MqttClientViewModel> AllClients => IsMessagingClientShown ? Clients : Clients.Append(MessagingClient);

    /// <summary>
    /// Every MQTT client whose TLS settings use a file of this certificate, or a copy of one, by name.
    /// </summary>
    public List<string> ClientsUsing(StoredCertificate entry)
    {
        var folder = Path.GetFullPath(entry.Folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return AllClients
            .Select(c => c.Profile)
            .Where(c => c.CaCertificateId == entry.Id || c.ClientCertificateId == entry.Id
                        || new[] { c.CaCertificate, c.ClientCertificate, c.ClientKey }.Any(path => IsIn(path, folder)))
            .Select(c => c.Name)
            .ToList();

        static bool IsIn(string path, string folder)
        {
            if (path.Trim().Length == 0)
                return false;
            try
            {
                return Path.GetFullPath(path.Trim()).StartsWith(folder, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }
    }

    [ObservableProperty]
    public partial MqttClientViewModel? SelectedClient { get; set; }

    /// <summary>Shows the client editor and returns whether it was saved. Set by the view.</summary>
    public Func<ClientSettingsViewModel, Task<bool>>? EditClientSettings { get; set; }

    [RelayCommand]
    private async Task AddClientAsync()
    {
        if (EditClientSettings is null)
            return;

        var profile = new ClientProfile { Name = UnusedName(), Host = "localhost", ClientId = ClientProfile.NewClientId() };
        var editor = new ClientSettingsViewModel(profile, "", "", AllClients.Select(c => c.Name).ToList(), isNew: true, _certificates, _brokerChoices);
        if (!await EditClientSettings(editor))
            return;

        var client = NewClient(editor.ToProfile());
        client.Apply(client.Profile, editor.Password, editor.KeyPassword);
        // The alerts and messages server stays the last tab.
        Clients.Insert(IsMessagingClientShown ? Clients.Count - 1 : Clients.Count, client);
        UpdateRemovable();
        SelectedClient = client;
        SaveSettings();
    }

    private async Task EditClientAsync(MqttClientViewModel client)
    {
        if (EditClientSettings is null)
            return;

        var others = AllClients.Where(c => c != client).Select(c => c.Name).ToList();
        var editor = new ClientSettingsViewModel(client.Profile.Clone(), client.Password, client.KeyPassword, others, isNew: false, _certificates, _brokerChoices);
        if (!await EditClientSettings(editor) || !client.CanEdit)
            return;

        client.Apply(editor.ToProfile(), editor.Password, editor.KeyPassword);
        SaveSettings();
    }

    private void RemoveClient(MqttClientViewModel client)
    {
        if (!client.IsRemovable || !client.CanEdit)
            return;

        var index = Clients.IndexOf(client);
        Clients.Remove(client);
        client.Dispose();
        UpdateRemovable();
        if (SelectedClient == client || SelectedClient is null)
            SelectedClient = Clients[Math.Min(index, Clients.Count - 1)];
        SaveSettings();
    }

    private IReadOnlyList<BrokerChoice> _brokerChoices = [];

    /// <summary>The ways to connect to the brokers made on other pages, as last given to <see cref="SyncBrokers"/>.</summary>
    public IReadOnlyList<BrokerChoice> BrokerChoices => _brokerChoices;

    /// <summary>Raised after <see cref="SyncBrokers"/>, when the brokers' ports may have changed.</summary>
    public event Action? BrokerChoicesChanged;

    /// <summary>
    /// Links a client to one port of a broker made on another page, taking how it connects from it
    /// and following it from then on; null or the choice of none unlinks it, keeping its settings.
    /// </summary>
    public void LinkToBroker(MqttClientViewModel client, BrokerChoice? choice)
    {
        if (choice is not { IsNone: false })
        {
            client.Profile.BrokerId = null;
            client.Profile.BrokerListener = null;
        }
        else
        {
            client.Profile.BrokerId = choice.BrokerId;
            client.Profile.BrokerListener = choice.Listener;
            Follow(client, choice, null);
        }

        SaveSettings();
    }

    /// <summary>
    /// Brings the clients linked to brokers made on other pages, such as the Mosquitto brokers on
    /// Dev Tools / Containers, up to date with <paramref name="choices"/>, every broker's ports.
    /// A linked client keeps to the port it uses, or the broker's first when that has gone; only
    /// how it connects changes, and its name when it was named after the broker. Clients of a
    /// broker that has gone are removed, but for the last client, which stays as the user's own.
    /// </summary>
    /// <param name="add">Brokers that get a client, on their first port, if none is linked to them yet.</param>
    public void SyncBrokers(IReadOnlyList<BrokerChoice> choices, IEnumerable<string> add)
    {
        var previous = _brokerChoices;
        _brokerChoices = choices;
        var changed = false;

        foreach (var client in AllClients.Where(c => c.Profile.BrokerId is not null).ToList())
        {
            var id = client.Profile.BrokerId!;
            var choice = choices.FirstOrDefault(c => c.BrokerId == id && c.Listener == client.Profile.BrokerListener)
                         ?? choices.FirstOrDefault(c => c.BrokerId == id);
            if (choice is null)
                changed |= ForgetBroker(client);
            else
                changed |= Follow(client, choice, previous.FirstOrDefault(c => c.BrokerId == id)?.BrokerName);
        }

        foreach (var id in add.Distinct())
        {
            if (AllClients.Any(c => c.Profile.BrokerId == id) || choices.FirstOrDefault(c => c.BrokerId == id) is not { } choice)
                continue;

            var profile = new ClientProfile { Name = UnusedName(choice.BrokerName), ClientId = ClientProfile.NewClientId(), BrokerId = id };
            var client = NewClient(profile);
            // The alerts and messages server stays the last tab.
            Clients.Insert(IsMessagingClientShown ? Clients.Count - 1 : Clients.Count, client);
            UpdateRemovable();
            Follow(client, choice, null);
            changed = true;
        }

        if (changed)
            SaveSettings();
        BrokerChoicesChanged?.Invoke();
    }

    // Takes how the choice connects. Returns whether anything changed.
    private bool Follow(MqttClientViewModel client, BrokerChoice choice, string? previousName)
    {
        var connection = choice.Connection;
        var profile = client.Profile.Clone();
        if (previousName is not null && profile.Name.Equals(previousName, StringComparison.OrdinalIgnoreCase)
            && !previousName.Equals(choice.BrokerName, StringComparison.OrdinalIgnoreCase)
            && ClientProfile.CheckName(choice.BrokerName, AllClients.Where(c => c != client).Select(c => c.Name)) is null)
            profile.Name = choice.BrokerName;
        if (profile.BrokerListener is not null)
            profile.BrokerListener = choice.Listener;
        profile.Host = connection.Host;
        profile.Port = connection.Port;
        profile.Username = connection.Username;
        profile.UseTls = connection.UseTls;
        profile.AcceptAnyServerCertificate = false;
        profile.CaCertificateId = connection.CaCertificateId;
        profile.ClientCertificateId = connection.ClientCertificateId;
        (profile.CaCertificate, profile.ClientCertificate, profile.ClientKey) = Copies(profile.Name, connection);

        var old = client.Profile;
        if (profile.Name == old.Name && profile.Host == old.Host && profile.Port == old.Port && profile.Username == old.Username
            && profile.UseTls == old.UseTls && profile.CaCertificate == old.CaCertificate && profile.ClientCertificate == old.ClientCertificate
            && profile.ClientKey == old.ClientKey && profile.CaCertificateId == old.CaCertificateId && profile.ClientCertificateId == old.ClientCertificateId
            && profile.BrokerListener == old.BrokerListener && !old.AcceptAnyServerCertificate && client.Password == choice.Password)
            return false;

        client.Apply(profile, choice.Password, connection.ClientCertificateId is null ? "" : client.KeyPassword);
        return true;
    }

    // Certificates from the Certificates page are used from copies in the client's own folder, as
    // the client editor does, so the client never reads the folders that hold the CAs' keys.
    private (string Authority, string Certificate, string Key) Copies(string name, ClientProfile connection)
    {
        var paths = (connection.CaCertificate, connection.ClientCertificate, connection.ClientKey);
        if (_certificates is null || !connection.UseTls)
            return paths;
        var authority = _certificates.Find(connection.CaCertificateId);
        var certificate = _certificates.Find(connection.ClientCertificateId);
        try
        {
            var (a, c, k) = _certificates.CopyForClient(name, authority, certificate);
            return (a ?? paths.CaCertificate, c ?? paths.ClientCertificate, k ?? paths.ClientKey);
        }
        catch (FormatException)
        {
            return paths;
        }
    }

    // A client whose broker has gone is removed, connected or not; the last one is kept, unlinked.
    private bool ForgetBroker(MqttClientViewModel client)
    {
        if (client == MessagingClient || !client.IsRemovable)
        {
            client.Profile.BrokerId = null;
            client.Profile.BrokerListener = null;
            return true;
        }

        var index = Clients.IndexOf(client);
        Clients.Remove(client);
        client.Dispose();
        UpdateRemovable();
        if (SelectedClient == client || SelectedClient is null)
            SelectedClient = Clients[Math.Min(index, Clients.Count - 1)];
        return true;
    }

    /// <summary>The client with this name, ignoring case, or null.</summary>
    public MqttClientViewModel? Find(string? name)
        => Clients.FirstOrDefault(c => c.Name.Equals(name?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Writes the settings to disk now. Edits are otherwise saved shortly after typing stops, so
    /// this is called on exit to keep anything typed in the last moment.
    /// </summary>
    public void SaveSettings()
    {
        foreach (var client in AllClients)
            client.StoreSettings();
        _settings.Clients = Clients.Where(c => c != MessagingClient).Select(c => c.Profile).ToList();
        Messaging.Server = MessagingClient.Profile;
        _settings.SelectedClient = SelectedClient?.Name ?? "";
        _settings.Save();
    }

    /// <summary>Saves everything and waits for message switches still being written. Called when the app exits.</summary>
    public void SaveState()
    {
        SaveSettings();
        foreach (var client in AllClients)
            client.FinishWrites();
    }

    partial void OnSelectedClientChanged(MqttClientViewModel? value)
    {
        if (value is not null && _settings.SelectedClient != value.Name)
        {
            _settings.SelectedClient = value.Name;
            _settings.Save();
        }
    }

    private MqttClientViewModel NewClient(ClientProfile profile)
        => new(profile, _credentials, SaveSettings)
        {
            EditClientRequested = EditClientAsync,
            RemoveClientRequested = RemoveClient,
        };

    // The last client cannot be removed, and the alerts and messages server neither counts as one nor can be.
    private void UpdateRemovable()
    {
        var removable = Clients.Count(c => c != MessagingClient) > 1;
        foreach (var client in Clients)
            client.IsRemovable = removable && client != MessagingClient;
    }

    // The name wanted, or with a number after it when that is taken.
    private string UnusedName(string wanted)
    {
        if (!AllClients.Any(c => c.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase)))
            return wanted;
        for (var i = 2; ; i++)
        {
            if (!AllClients.Any(c => c.Name.Equals($"{wanted} {i}", StringComparison.OrdinalIgnoreCase)))
                return $"{wanted} {i}";
        }
    }

    private string UnusedName()
    {
        for (var i = Clients.Count + 1; ; i++)
        {
            if (!AllClients.Any(c => c.Name.Equals(string.Format(Strings.Mqtt_Client, i), StringComparison.OrdinalIgnoreCase)))
                return string.Format(Strings.Mqtt_Client, i);
        }
    }
}
