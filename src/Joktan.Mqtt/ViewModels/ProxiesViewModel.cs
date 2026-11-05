using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Certificates;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.Proxy;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// Dev Tools / Brokers / MQTT Proxy: proxies that devices connect to instead of a broker, each on
/// a tab of its own, which forward to a target broker and can intercept chosen messages. Kept in
/// mqtt/proxies.json. Specified in docs/features/dev-tools/connectivity/mqtt-proxy/spec.md.
/// </summary>
public partial class ProxiesViewModel : PageViewModel
{
    private readonly ProxySettings _settings;
    private readonly string? _folder;

    /// <param name="mqtt">The MQTT page, for the brokers on Dev Tools / Containers and the certificates.</param>
    /// <param name="settings">The proxies, if already read; otherwise they are read here.</param>
    /// <param name="credentials">Where the target brokers' passwords are kept; the system's credential store otherwise.</param>
    /// <param name="folder">The folder proxies.json is in; Joktan's mqtt folder otherwise.</param>
    public ProxiesViewModel(MqttViewModel mqtt, ProxySettings? settings = null, CredentialStore? credentials = null, string? folder = null)
        : base(Strings.Proxies_MqttProxy, Strings.Proxies_DevToolsBrokersMqttProxy)
    {
        Mqtt = mqtt;
        _folder = folder;
        _settings = settings ?? ProxySettings.Load(folder);
        Credentials = credentials ?? CredentialStore.Create();
        foreach (var profile in _settings.Proxies)
            Proxies.Add(new ProxyViewModel(profile, this));

        // Targets linked to a broker catch up with it, and with its password, once every proxy is here to be saved.
        foreach (var proxy in Proxies)
            proxy.BrokersChanged();
        Proxies.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasProxies));
        SelectedProxy = Proxies.FirstOrDefault(p => p.Profile.Id == _settings.SelectedProxy) ?? Proxies.FirstOrDefault();
        if (mqtt.Certificates is { } certificates)
        {
            certificates.Changed += () =>
            {
                foreach (var proxy in Proxies)
                    proxy.CertificatesChanged();
            };
        }

        mqtt.BrokerChoicesChanged += () =>
        {
            foreach (var proxy in Proxies)
                proxy.BrokersChanged();
        };
    }

    public MqttViewModel Mqtt { get; }

    internal CredentialStore Credentials { get; }

    public ObservableCollection<ProxyViewModel> Proxies { get; } = [];

    public bool HasProxies => Proxies.Count > 0;

    [ObservableProperty]
    public partial ProxyViewModel? SelectedProxy { get; set; }

    /// <summary>Shows the client editor, for a proxy's target, and returns whether it was saved. Set by the view.</summary>
    public Func<ClientSettingsViewModel, Task<bool>>? EditClientSettings { get; set; }

    /// <summary>Shows a folder message's settings and returns whether they were saved. Set by the view.</summary>
    public Func<MessageSettingsViewModel, Task<bool>>? EditSettings { get; set; }

    /// <summary>Asks for a folder of messages. Set by the view.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    partial void OnSelectedProxyChanged(ProxyViewModel? value)
    {
        if (value is not null && _settings.SelectedProxy != value.Profile.Id)
            Save();
    }

    /// <summary>Adds a proxy on the first port free from 1885, forwarding to the first broker on Dev Tools / Containers if there is one.</summary>
    [RelayCommand]
    private void Add()
    {
        var profile = new ProxyProfile { Name = FreeName(), ListenPort = FreePort(ProxyProfile.DefaultPort) };
        profile.Target.Name = profile.Name;
        profile.Target.ClientId = ClientProfile.NewClientId();
        if (Mqtt.BrokerChoices.FirstOrDefault() is { } broker)
        {
            profile.Target.BrokerId = broker.BrokerId;
            profile.Target = Mqtt.Followed(profile.Target, broker);
        }

        var proxy = new ProxyViewModel(profile, this);
        Proxies.Add(proxy);
        SelectedProxy = proxy;
        proxy.BrokersChanged();
        Save();
    }

    /// <summary>Stops a proxy and removes it. Its folder of messages is left as it is.</summary>
    public async Task RemoveAsync(ProxyViewModel proxy)
    {
        await proxy.StopAsync();
        Credentials.Delete(proxy.Profile.DevicePasswordAccount);
        proxy.Dispose();
        var index = Proxies.IndexOf(proxy);
        Proxies.Remove(proxy);
        if (SelectedProxy == proxy || SelectedProxy is null)
            SelectedProxy = Proxies.Count == 0 ? null : Proxies[Math.Clamp(index, 0, Proxies.Count - 1)];
        Save();
    }

    internal bool IsNameFree(string name, ProxyViewModel except)
        => Proxies.All(p => p == except || !p.Profile.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    internal bool IsPortFree(int port, ProxyViewModel except) => Proxies.All(p => p == except || !p.Profile.Ports.Contains(port));

    /// <summary>The first port from <paramref name="from"/> up that no other proxy uses and is not in <paramref name="alsoTaken"/>.</summary>
    internal int FreePort(int from, ProxyViewModel? except = null, IEnumerable<int>? alsoTaken = null)
    {
        var taken = Proxies.Where(p => p != except).SelectMany(p => p.Profile.Ports).Concat(alsoTaken ?? []).ToHashSet();
        var port = from;
        while (taken.Contains(port))
            port++;
        return port;
    }

    private string FreeName()
    {
        for (var i = Proxies.Count + 1; ; i++)
        {
            var name = string.Format(Strings.Proxies_Proxy, i);
            if (Proxies.All(p => !p.Profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return name;
        }
    }

    internal void Save()
    {
        _settings.Proxies = Proxies.Select(p => p.Profile).ToList();
        _settings.SelectedProxy = SelectedProxy?.Profile.Id ?? "";
        try
        {
            _settings.Save(_folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (SelectedProxy is { } proxy)
                proxy.Error = string.Format(Strings.Proxies_CouldNotSave, e.Message);
        }
    }

    /// <summary>Stops every proxy. Called when the app exits.</summary>
    public void Shutdown()
    {
        var stops = Proxies.Select(p => p.StopForExit()).ToArray();
        Task.Run(() => Task.WhenAll(stops)).Wait(TimeSpan.FromSeconds(3));
    }
}

/// <summary>
/// A certificate from the Certificates page to choose for a proxy. <see cref="IsNone"/> turns off
/// what it is for; with no ID otherwise, it stands for the CAs that signed the server certificate.
/// </summary>
public sealed record ProxyCertificateOption(string? Id, string Name, bool IsNone = false)
{
    public override string ToString() => Name;
}

/// <summary>A message in a proxy's folder, which is intercepted while it is switched on.</summary>
public partial class ProxyMessageRow : ObservableObject
{
    private readonly ProxyViewModel _proxy;

    public ProxyMessageRow(FolderMessage message, string key, bool selected, string brokerTopic, ProxyViewModel proxy)
    {
        Message = message;
        Key = key;
        _proxy = proxy;
        _isSelected = selected;
        _brokerTopicText = brokerTopic;
        BrokerTopic = brokerTopic;
    }

    /// <summary>The topic it goes to the broker on while intercepted, as saved; empty for its own.</summary>
    public string BrokerTopic { get; private set; }

    private string _brokerTopicText;

    /// <summary>The topic at the broker typed, saved once valid.</summary>
    public string BrokerTopicText
    {
        get => _brokerTopicText;
        set
        {
            if (!SetProperty(ref _brokerTopicText, value))
                return;
            BrokerTopicError = TopicRewrite.Check(value);
            if (BrokerTopicError is not null)
                return;
            BrokerTopic = value.Trim();
            _proxy.BrokerTopicChanged(this);
        }
    }

    [ObservableProperty]
    public partial string? BrokerTopicError { get; set; }

    /// <summary>Shown in the empty topic at the broker: its own.</summary>
    public string BrokerTopicPlaceholder => Message.Topic;

    public FolderMessage Message { get; }

    /// <summary>Its path inside the folder, with forward slashes, which the selection is saved by.</summary>
    public string Key { get; }

    public string Topic => Message.Topic;

    public string FileName => Key;

    public bool IsValid => Message.IsValid;

    public bool CanEdit => Message.Body is not null;

    /// <summary>What intercepting it does.</summary>
    public string ModeText => !Message.IsValid ? Message.Error ?? ""
        : Message.HasSchedule
            ? string.Format(Strings.ProxyMessageRow_SentOnSchedule, Message.Settings.Schedule.Describe())
            : Strings.ProxyMessageRow_MergedIntoEachMessage;

    /// <summary>For a scheduled message being sent, how it is getting on.</summary>
    public string StateText => Message.HasSchedule && IsSelected && Message.State is FolderMessageState.Scheduled or FolderMessageState.Finished or FolderMessageState.Missed
        ? Message.NextDue is { } due
            ? string.Format(Strings.ProxyMessageRow_StateNext, Message.State.Text(), due.ToString("HH:mm:ss"), Message.SentCount)
            : string.Format(Strings.ProxyMessageRow_State, Message.State.Text(), Message.SentCount)
        : "";

    private bool _isSelected;

    /// <summary>Whether it is intercepted.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!IsValid || !SetProperty(ref _isSelected, value))
                return;
            _proxy.SelectionChanged(this);
            OnPropertyChanged(nameof(StateText));
        }
    }

    [RelayCommand]
    private Task EditAsync() => _proxy.EditMessageAsync(this);

    internal void Refresh() => OnPropertyChanged(nameof(StateText));
}

/// <summary>
/// One proxy on its tab: where devices connect, the broker it forwards to, its topic rules, the
/// messages it intercepts, and what it has done.
/// </summary>
public partial class ProxyViewModel : ViewModelBase, IDisposable
{
    private const int MaxEntries = 500;

    private readonly ProxiesViewModel _page;
    private readonly MqttProxy _proxy;
    private readonly FolderPublisher _publisher;
    private readonly ConcurrentQueue<ActivityEntry> _incoming = new();
    private readonly DispatcherTimer _timer;
    private volatile bool _changed = true;
    private Dictionary<string, MessageSettings> _fileSettings = [];
    private int _reads;

    public ProxyViewModel(ProxyProfile profile, ProxiesViewModel page)
    {
        Profile = profile;
        _page = page;
        _proxy = new MqttProxy(_incoming.Enqueue);
        _proxy.Changed += () => _changed = true;
        _proxy.Login = profile.DeviceLogin;
        _proxy.Username = profile.DeviceUsername;
        _devicePassword = page.Credentials.Read(profile.DevicePasswordAccount) ?? "";
        _proxy.Password = _devicePassword;
        _publisher = new FolderPublisher(_proxy.Upstream, _incoming.Enqueue) { MapTopic = _proxy.ToBroker };
        _nameText = profile.Name;
        _portText = Text(profile.ListenPort);
        _tlsPortText = Text(profile.TlsPort);
        _mutualTlsPortText = Text(profile.MutualTlsPort);
        CertificatesChanged();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, (_, _) => _ = TickAsync());
        _timer.Start();
        _ = LoadFolderAsync();
    }

    public ProxyProfile Profile { get; }

    public ObservableCollection<ProxyMessageRow> Rows { get; } = [];

    /// <summary>What has happened, newest first.</summary>
    public ObservableCollection<ActivityEntry> Entries { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedBody))]
    public partial ActivityEntry? SelectedEntry { get; set; }

    public string? SelectedBody => SelectedEntry?.Body;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChange), nameof(StartStopText))]
    [NotifyCanExecuteChangedFor(nameof(EditTargetCommand), nameof(RemoveCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartStopCommand))]
    public partial bool IsStarting { get; set; }

    /// <summary>The port, target and name are only changed while it is stopped.</summary>
    public bool CanChange => !IsRunning;

    public string StartStopText => IsRunning ? Strings.Proxy_Stop : Strings.Proxy_Start;

    [ObservableProperty]
    public partial string? Error { get; set; }

    public string Name => Profile.Name;

    private string _nameText;

    public string NameText
    {
        get => _nameText;
        set
        {
            if (!SetProperty(ref _nameText, value))
                return;
            NameError = ClientProfile.CheckName(value, []) ?? (_page.IsNameFree(value, this) ? null : string.Format(Strings.Proxy_AProxyIsAlreadyCalled, value.Trim()));
            if (NameError is null)
            {
                Profile.Name = value.Trim();
                OnPropertyChanged(nameof(Name));
                _page.Save();
            }
        }
    }

    [ObservableProperty]
    public partial string? NameError { get; set; }

    // The ports devices connect to, each empty for none, saved once all three are valid.

    private string _portText;

    /// <summary>The port for plain MQTT.</summary>
    public string PortText
    {
        get => _portText;
        set
        {
            if (SetProperty(ref _portText, value))
                PortsChanged();
        }
    }

    private string _tlsPortText;

    /// <summary>The port for TLS, which needs a server certificate.</summary>
    public string TlsPortText
    {
        get => _tlsPortText;
        set
        {
            if (SetProperty(ref _tlsPortText, value))
                PortsChanged();
        }
    }

    private string _mutualTlsPortText;

    /// <summary>The port for mutual TLS, where devices show a certificate too.</summary>
    public string MutualTlsPortText
    {
        get => _mutualTlsPortText;
        set
        {
            if (SetProperty(ref _mutualTlsPortText, value))
                PortsChanged();
        }
    }

    [ObservableProperty]
    public partial string? PortError { get; set; }

    private void PortsChanged()
    {
        PortError = CheckPorts(out var plain, out var tls, out var mutual);
        if (PortError is not null)
            return;
        Profile.ListenPort = plain;
        Profile.TlsPort = tls;
        Profile.MutualTlsPort = mutual;
        OnPropertyChanged(nameof(AddressText));
        _page.Save();
    }

    private string? CheckPorts(out int? plain, out int? tls, out int? mutual)
    {
        var problem = Parse(PortText, out plain);
        problem = Parse(TlsPortText, out tls) ?? problem;
        problem = Parse(MutualTlsPortText, out mutual) ?? problem;
        if (problem is not null)
            return problem;

        var ports = new[] { plain, tls, mutual }.OfType<int>().ToList();
        if (ports.Count == 0)
            return Strings.MqttProxy_ChooseAtLeastOnePort;
        if (ports.Distinct().Count() < ports.Count)
            return Strings.Proxy_EachPortMustBeDifferent;
        if (ports.FirstOrDefault(p => !_page.IsPortFree(p, this)) is var taken and > 0)
            return string.Format(Strings.Proxy_AnotherProxyUsesPort, taken);
        return null;

        static string? Parse(string text, out int? port)
        {
            port = null;
            if (text.Trim().Length == 0)
                return null;
            port = ClientSettingsViewModel.ParsePort(text);
            return port is null ? Strings.Proxy_ThePortMustBeAWhole : null;
        }
    }

    // The first port from this one that no other proxy uses, nor this one on another of its ports.
    private int FreePort(int from)
    {
        var typed = new[] { PortText, TlsPortText, MutualTlsPortText }.Select(ClientSettingsViewModel.ParsePort).OfType<int>();
        return _page.FreePort(from, this, typed);
    }

    public bool AllowOtherComputers
    {
        get => Profile.AllowOtherComputers;
        set
        {
            if (Profile.AllowOtherComputers == value)
                return;
            Profile.AllowOtherComputers = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AddressText));
            _page.Save();
        }
    }

    // How devices sign in, which need not be how the proxy signs in to the broker.

    public IReadOnlyList<DeviceLogin> DeviceLogins { get; } = Enum.GetValues<DeviceLogin>();

    public static readonly Avalonia.Data.Converters.FuncValueConverter<DeviceLogin, string> DeviceLoginConverter = new(value => value.Text());

    public DeviceLogin SelectedDeviceLogin
    {
        get => Profile.DeviceLogin;
        set
        {
            if (Profile.DeviceLogin == value)
                return;
            Profile.DeviceLogin = value;
            _proxy.Login = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UsesOwnLogin));
            _page.Save();
        }
    }

    public bool UsesOwnLogin => Profile.DeviceLogin == DeviceLogin.Own;

    /// <summary>The username devices sign in with, with the proxy's own login.</summary>
    public string DeviceUsername
    {
        get => Profile.DeviceUsername;
        set
        {
            if (Profile.DeviceUsername == value.Trim())
                return;
            Profile.DeviceUsername = value.Trim();
            _proxy.Username = Profile.DeviceUsername;
            OnPropertyChanged();
            _page.Save();
        }
    }

    private string _devicePassword = "";

    /// <summary>The password devices sign in with, kept in the credential store as it is typed.</summary>
    public string DevicePassword
    {
        get => _devicePassword;
        set
        {
            if (!SetProperty(ref _devicePassword, value))
                return;
            _proxy.Password = value;
            if (value.Length == 0)
                _page.Credentials.Delete(Profile.DevicePasswordAccount);
            else if (!_page.Credentials.Write(Profile.DevicePasswordAccount, value))
                Error = Strings.Proxy_ThePasswordCouldNotBeKept;
        }
    }

    /// <summary>Where devices are told to connect, on each port.</summary>
    public string AddressText => string.Format(Strings.Proxy_DevicesConnectTo,
        Profile.AllowOtherComputers ? Strings.Proxy_ThisComputersAddress : "localhost",
        string.Join(", ", new ProxyListeners(Profile.ListenPort, Profile.TlsPort, Profile.MutualTlsPort, false).Ports()
            .Select(p => string.Format(Strings.Proxy_OnPort, p.Kind.Text(), p.Port))));

    // The certificates devices are shown, and whose certificates they must show.

    /// <summary>The server certificates made on the Certificates page, after none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoServerCertificates))]
    public partial IReadOnlyList<ProxyCertificateOption> ServerCertificates { get; private set; } = [];

    [ObservableProperty]
    public partial ProxyCertificateOption? SelectedServerCertificate { get; set; }

    public bool HasNoServerCertificates => ServerCertificates.Count <= 1;

    /// <summary>No mutual TLS, the CAs that signed the server certificate, then every CA on the Certificates page.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ProxyCertificateOption> ClientAuthorities { get; private set; } = [];

    [ObservableProperty]
    public partial ProxyCertificateOption? SelectedClientAuthority { get; set; }

    // Set while the choices are filled in, so that only the user's choices fill in ports.
    private bool _choosing;

    /// <summary>Reads the certificates again, keeping what was chosen if it is still there.</summary>
    internal void CertificatesChanged()
    {
        var store = _page.Mqtt.Certificates;
        var usable = store?.Entries.Where(e => e.Certificate is not null && e.Problem is null).ToList() ?? [];
        string Named(Joktan.Certificates.StoredCertificate e) => store!.Find(e.IssuerId) is { } issuer
            ? string.Format(Strings.Proxy_SignedBy, e.DisplayName, issuer.DisplayName)
            : e.DisplayName;

        _choosing = true;
        try
        {
            ServerCertificates = usable.Where(e => e.Kind == Joktan.Certificates.CertificateKind.Server)
                .Select(e => new ProxyCertificateOption(e.Id, Named(e)))
                .Prepend(new ProxyCertificateOption(null, Strings.Proxy_None, IsNone: true)).ToList();
            ClientAuthorities = usable.Where(e => e.Kind.IsAuthority())
                .Select(e => new ProxyCertificateOption(e.Id, Named(e)))
                .Prepend(new ProxyCertificateOption(null, Strings.Proxy_TheCAsThatSignedTheServer))
                .Prepend(new ProxyCertificateOption(null, Strings.Proxy_NoMutualTls, IsNone: true)).ToList();
            SelectedServerCertificate = ServerCertificates.FirstOrDefault(c => c.Id is not null && c.Id == Profile.ServerCertificateId) ?? ServerCertificates[0];
            SelectedClientAuthority = Profile.MutualTlsPort is null ? ClientAuthorities[0]
                : ClientAuthorities.FirstOrDefault(c => c.Id is not null && c.Id == Profile.ClientAuthorityId) ?? ClientAuthorities[1];
        }
        finally
        {
            _choosing = false;
        }
    }

    partial void OnSelectedServerCertificateChanged(ProxyCertificateOption? value)
    {
        if (_choosing || value is null)
            return;
        Profile.ServerCertificateId = value.Id;
        if (value.IsNone)
        {
            TlsPortText = "";
            SelectedClientAuthority = ClientAuthorities[0];
        }
        else if (TlsPortText.Trim().Length == 0)
        {
            TlsPortText = FreePort(ProxyProfile.DefaultTlsPort).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        _page.Save();
    }

    partial void OnSelectedClientAuthorityChanged(ProxyCertificateOption? value)
    {
        if (_choosing || value is null)
            return;
        Profile.ClientAuthorityId = value.IsNone ? null : value.Id;
        if (value.IsNone)
            MutualTlsPortText = "";
        else if (MutualTlsPortText.Trim().Length == 0)
            MutualTlsPortText = FreePort(ProxyProfile.DefaultMutualTlsPort).ToString(System.Globalization.CultureInfo.InvariantCulture);
        _page.Save();
    }

    /// <summary>
    /// The ports to listen on, with the server certificate and its key, and the CAs devices'
    /// certificates must chain to. Every failure is reported as a <see cref="FormatException"/>
    /// whose message can be shown as it is.
    /// </summary>
    private ProxyListeners Listeners()
    {
        var store = _page.Mqtt.Certificates;
        System.Security.Cryptography.X509Certificates.X509Certificate2? certificate = null;
        System.Security.Cryptography.X509Certificates.X509Certificate2Collection? authorities = null;
        var server = store?.Find(Profile.ServerCertificateId);
        if (Profile.TlsPort is not null || Profile.MutualTlsPort is not null)
        {
            if (server is null)
                throw new FormatException(Strings.MqttProxy_TlsNeedsAServerCertificate);
            try
            {
                certificate = System.Security.Cryptography.X509Certificates.X509Certificate2.CreateFromPemFile(server.CertificatePath, server.KeyPath);
                if (Profile.MutualTlsPort is not null)
                {
                    authorities = [];
                    authorities.ImportFromPemFile((store!.Find(Profile.ClientAuthorityId) ?? server).AuthorityPath);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException)
            {
                throw new FormatException(string.Format(Strings.Proxy_TheCertificateCannotBeRead, server.DisplayName, e.Message), e);
            }
        }

        return new ProxyListeners(Profile.ListenPort, Profile.TlsPort, Profile.MutualTlsPort, Profile.AllowOtherComputers, certificate, authorities);
    }

    // The broker everything is forwarded to.

    public string TargetText => $"{Profile.Target.Host}:{Profile.Target.Port}";

    public string TargetSecurity => !Profile.Target.UseTls ? Strings.Proxy_NoTls
        : Profile.Target.UsesClientCertificate ? Strings.Proxy_MutualTls : Strings.Proxy_Tls;

    public string TargetLogin => Profile.Target.Username.Trim() is { Length: > 0 } user ? user : Strings.Proxy_Anonymous;

    /// <summary>The broker port on Dev Tools / Containers the target follows, if any.</summary>
    public string? FollowsText => _page.Mqtt.ChoiceFor(Profile.Target) is { } choice ? string.Format(Strings.Proxy_Follows, choice.Text) : null;

    public string UpstreamText => _proxy.Upstream.State == ConnectionState.Failed && _proxy.Upstream.FailureReason is { } reason
        ? $"{_proxy.Upstream.State.Text()}: {reason}"
        : _proxy.Upstream.State.Text();

    public bool IsUpstreamConnected => _proxy.Upstream.State == ConnectionState.Connected;

    public bool IsUpstreamPending => _proxy.Upstream.State is ConnectionState.Connecting or ConnectionState.Reconnecting;

    public bool IsUpstreamFailed => _proxy.Upstream.State == ConnectionState.Failed;

    /// <summary>How many devices are connected, and what has been passed on.</summary>
    public string StatsText => string.Format(Strings.Proxy_Stats, _proxy.ClientCount, _proxy.Forwarded, _proxy.Modified, _proxy.Blocked, _proxy.Delivered);

    /// <summary>What does the forwarding, with its counts and connection to the broker.</summary>
    public MqttProxy Engine => _proxy;

    [RelayCommand(CanExecute = nameof(CanChange))]
    private async Task EditTargetAsync()
    {
        if (_page.EditClientSettings is null)
            return;

        var target = Profile.Target.Clone();
        target.Name = Profile.Name;
        var (password, keyPassword) = Secrets(target);
        var editor = new ClientSettingsViewModel(target, password, keyPassword, [], isNew: false, _page.Mqtt.Certificates, _page.Mqtt.BrokerChoices);
        if (!await _page.EditClientSettings(editor))
            return;

        Profile.Target = editor.ToProfile();
        Remember(Profile.Target, editor.Password, editor.KeyPassword);
        TargetChanged();
    }

    /// <summary>Follows the target's broker on Dev Tools / Containers after the brokers changed, and reconnects if running.</summary>
    internal void BrokersChanged()
    {
        if (Profile.Target.BrokerId is null)
            return;

        if (_page.Mqtt.ChoiceFor(Profile.Target) is not { } choice)
        {
            Profile.Target.BrokerId = null;
            Profile.Target.BrokerListener = null;
            TargetChanged();
            return;
        }

        // A login of the user's own stays; the broker's is taken from it otherwise.
        var followed = _page.Mqtt.Followed(Profile.Target, choice);
        var (password, _) = Secrets(Profile.Target);
        var wanted = Profile.Target.OwnLogin ? password : choice.Password;
        if (Same(followed, Profile.Target) && password == wanted)
        {
            OnPropertyChanged(nameof(FollowsText));
            return;
        }

        Profile.Target = followed;
        Remember(followed, wanted, "");
        TargetChanged();
        if (IsRunning)
            _ = _proxy.Upstream.ConnectAsync(Endpoint());
    }

    private static bool Same(ClientProfile a, ClientProfile b)
        => a.Host == b.Host && a.Port == b.Port && a.Username == b.Username && a.UseTls == b.UseTls && a.CaCertificate == b.CaCertificate
           && a.ClientCertificate == b.ClientCertificate && a.ClientKey == b.ClientKey && a.BrokerListener == b.BrokerListener;

    private void TargetChanged()
    {
        OnPropertyChanged(nameof(TargetText));
        OnPropertyChanged(nameof(TargetSecurity));
        OnPropertyChanged(nameof(TargetLogin));
        OnPropertyChanged(nameof(FollowsText));
        _page.Save();
    }

    // Kept as the MQTT page keeps its clients', so a target and a client of the same broker share them.
    private (string Password, string KeyPassword) Secrets(ClientProfile target)
    {
        var password = target.Username.Trim() is { Length: > 0 } user
            ? _page.Credentials.Read(CredentialStore.AccountFor(user, target.Host.Trim(), target.Port.Trim())) ?? ""
            : "";
        var key = target.UsesClientCertificate ? _page.Credentials.Read(KeyAccount(target)) ?? "" : "";
        return (password, key);
    }

    private void Remember(ClientProfile target, string password, string keyPassword)
    {
        if (target.Username.Trim() is { Length: > 0 } user)
            Store(CredentialStore.AccountFor(user, target.Host.Trim(), target.Port.Trim()), password);
        if (target.UsesClientCertificate)
            Store(KeyAccount(target), keyPassword);

        void Store(string account, string secret)
        {
            if (secret.Length == 0)
                _page.Credentials.Delete(account);
            else
                _page.Credentials.Write(account, secret);
        }
    }

    private static string KeyAccount(ClientProfile target)
        => CredentialStore.AccountForKey(target.ClientKey.Trim().Length > 0 && !CertificateLoader.IsPkcs12(target.ClientCertificate) ? target.ClientKey : target.ClientCertificate);

    private BrokerEndpoint Endpoint()
    {
        var target = Profile.Target;
        if (target.ClientId.Trim().Length == 0)
        {
            target.ClientId = ClientProfile.NewClientId();
            _page.Save();
        }

        var (password, keyPassword) = Secrets(target);
        var username = target.Username.Trim();
        var tls = !target.UseTls ? null : new TlsEndpoint(
            NullIfEmpty(target.CaCertificate), NullIfEmpty(target.ClientCertificate), NullIfEmpty(target.ClientKey),
            target.UsesClientCertificate && keyPassword.Length > 0 ? keyPassword : null, target.AcceptAnyServerCertificate);
        return new BrokerEndpoint(target.Host.Trim(), ClientSettingsViewModel.ParsePort(target.Port) ?? 1883, target.ClientId,
            username.Length > 0 ? username : null, username.Length > 0 ? password : null, tls);

        static string? NullIfEmpty(string value) => value.Trim().Length > 0 ? value.Trim() : null;
    }

    private bool CanStartStop => !IsStarting;

    [RelayCommand(CanExecute = nameof(CanStartStop))]
    private async Task StartStopAsync()
    {
        if (IsRunning)
            await StopAsync();
        else
            await StartAsync();
    }

    /// <summary>Starts listening and forwarding, and sends the selected scheduled messages on their schedules.</summary>
    public async Task StartAsync()
    {
        if (IsRunning || PortError is not null)
            return;

        IsStarting = true;
        Error = null;
        try
        {
            _proxy.Selected = SelectedMessages();
            ProxyListeners listeners;
            try
            {
                listeners = Listeners();
            }
            catch (FormatException e)
            {
                Error = e.Message;
                return;
            }

            if (await _proxy.StartAsync(listeners, Endpoint()) is { } problem)
            {
                Error = problem;
                return;
            }

            _publisher.Start(DateTime.Now);
            IsRunning = true;
        }
        finally
        {
            IsStarting = false;
            _changed = true;
        }
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
            return;
        _publisher.Stop();
        IsRunning = false;
        await _proxy.StopAsync();
        _changed = true;
        foreach (var row in Rows)
            row.Refresh();
    }

    internal Task StopForExit()
    {
        _timer.Stop();
        _publisher.Stop();
        return _proxy.StopAsync();
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private Task RemoveAsync() => _page.RemoveAsync(this);

    // The folder of messages.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderText))]
    public partial string? FolderError { get; set; }

    public string FolderText => Profile.Folder is { Length: > 0 } folder ? folder : Strings.Proxy_NoFolderChosen;

    public bool HasRows => Rows.Count > 0;

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (_page.PickFolder is null || await _page.PickFolder() is not { } folder)
            return;
        Profile.Folder = folder;
        Profile.Selected.Clear();
        OnPropertyChanged(nameof(FolderText));
        _page.Save();
        await LoadFolderAsync();
    }

    [RelayCommand]
    private Task ReloadAsync() => LoadFolderAsync();

    /// <summary>The form for a new message in the folder, while it is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewMessage))]
    public partial NewFolderMessageViewModel? NewMessage { get; set; }

    public bool HasNewMessage => NewMessage is not null;

    /// <summary>Shows the form for a new message in the folder, which then opens its settings.</summary>
    [RelayCommand]
    private void AddMessage()
    {
        NewMessage = new NewFolderMessageViewModel(() => Profile.Folder, CreatedAsync, () => NewMessage = null);

        async Task CreatedAsync(string path)
        {
            await LoadFolderAsync();
            if (Rows.FirstOrDefault(r => r.Message.FilePath == path) is { } row)
                await EditMessageAsync(row);
        }
    }

    /// <summary>Reads the folder again, keeping which messages are switched on and how far their schedules got.</summary>
    public async Task LoadFolderAsync()
    {
        var read = ++_reads;
        var folder = Profile.Folder;
        var scan = await Task.Run(() => FolderPublisher.Read(folder));
        if (read != _reads)
            return;

        // A message is sent on its schedule only while it is switched on here; its file's own setting is kept for the editor.
        var selected = Profile.Selected.ToHashSet(StringComparer.Ordinal);
        _fileSettings = scan.Messages.ToDictionary(m => m.FilePath, m => m.Settings);
        foreach (var message in scan.Messages)
            message.Settings = message.Settings with { Enabled = selected.Contains(KeyOf(message)) };
        _publisher.Apply(scan, DateTime.Now);
        FolderError = scan.Error;

        Rows.Clear();
        foreach (var message in _publisher.Messages)
            Rows.Add(new ProxyMessageRow(message, KeyOf(message), message.IsValid && selected.Contains(KeyOf(message)),
                Profile.BrokerTopics.GetValueOrDefault(KeyOf(message)) ?? "", this));
        OnPropertyChanged(nameof(HasRows));
        _proxy.Selected = SelectedMessages();
    }

    private string KeyOf(FolderMessage message)
        => Path.GetRelativePath(Profile.Folder ?? "", message.FilePath).Replace(Path.DirectorySeparatorChar, '/');

    private List<Interception> SelectedMessages() => Rows.Where(r => r.IsSelected).Select(r => new Interception(r.Message, r.BrokerTopic)).ToList();

    internal void SelectionChanged(ProxyMessageRow row)
    {
        Profile.Selected = Rows.Where(r => r.IsSelected).Select(r => r.Key).ToList();
        _page.Save();
        _publisher.SetEnabled(row.Message, row.IsSelected, DateTime.Now);
        _proxy.Selected = SelectedMessages();
    }

    /// <summary>Keeps the topic a message goes to the broker on, and uses it at once.</summary>
    internal void BrokerTopicChanged(ProxyMessageRow row)
    {
        if (row.BrokerTopic.Length == 0)
            Profile.BrokerTopics.Remove(row.Key);
        else
            Profile.BrokerTopics[row.Key] = row.BrokerTopic;
        _page.Save();
        _proxy.Selected = SelectedMessages();
    }

    internal async Task EditMessageAsync(ProxyMessageRow row)
    {
        if (_page.EditSettings is null || row.Message.Body is null)
            return;

        // The editor writes the file, so it sees the file's own settings, not whether it is switched on here.
        var original = row.Message;
        var copy = new FolderMessage
        {
            FilePath = original.FilePath, FileName = original.FileName, Folder = original.Folder, Topic = original.Topic,
            Text = original.Text, Body = original.Body, Error = original.Error,
            Settings = _fileSettings.GetValueOrDefault(original.FilePath) ?? original.Settings,
        };
        if (await _page.EditSettings(new MessageSettingsViewModel(copy)))
            await LoadFolderAsync();
    }

    // Activity, and the scheduled sends.

    [RelayCommand]
    private void ClearLog()
    {
        SelectedEntry = null;
        Entries.Clear();
    }

    private async Task TickAsync()
    {
        if (IsRunning && await _publisher.TickAsync(DateTime.Now))
        {
            foreach (var row in Rows)
                row.Refresh();
        }

        while (_incoming.TryDequeue(out var entry))
            Entries.Insert(0, entry);
        while (Entries.Count > MaxEntries)
            Entries.RemoveAt(Entries.Count - 1);

        if (!_changed)
            return;
        _changed = false;
        OnPropertyChanged(nameof(StatsText));
        OnPropertyChanged(nameof(UpstreamText));
        OnPropertyChanged(nameof(IsUpstreamConnected));
        OnPropertyChanged(nameof(IsUpstreamPending));
        OnPropertyChanged(nameof(IsUpstreamFailed));
        OnPropertyChanged(nameof(AddressText));
    }

    /// <summary>Takes in what has happened now, rather than on the timer. For tests.</summary>
    public Task FlushAsync() => TickAsync();

    private static string Text(int? port) => port?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";

    public void Dispose()
    {
        _timer.Stop();
        GC.SuppressFinalize(this);
    }
}
