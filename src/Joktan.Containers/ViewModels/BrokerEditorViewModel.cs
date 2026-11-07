using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Certificates;
using Joktan.Containers.Resources;

namespace Joktan.Containers.ViewModels;

/// <summary>
/// A certificate from the Certificates page to choose for a broker. <see cref="IsNone"/> turns off
/// what it is for; with no ID otherwise, it stands for the CAs that signed the server certificate.
/// </summary>
public sealed record CertificateOption(string? Id, string Name, bool IsNone = false)
{
    public override string ToString() => Name;
}

/// <summary>
/// Adds a Mosquitto broker on Data / MQTT, or changes one. Choosing a server certificate
/// fills in a TLS port, and choosing whose client certificates to accept a mutual TLS port, each
/// the first one free; choosing none empties them. Nothing is saved until Save.
/// </summary>
public partial class BrokerEditorViewModel : ObservableObject
{
    private readonly MosquittoBroker? _original;
    private readonly MosquittoBrokersViewModel _list;

    // Set while the choices are filled in, so that only the user's choices fill in ports.
    private bool _loading;

    public BrokerEditorViewModel(MosquittoBroker? original, MosquittoBrokersViewModel list)
    {
        _original = original;
        _list = list;
        NameText = original?.Name ?? list.FreeName();
        PortText = Text(original is null ? list.FreePort(MosquittoBroker.ContainerPort) : original.Port);
        TlsPortText = Text(original?.TlsPort);
        MutualTlsPortText = Text(original?.MutualTlsPort);
        Username = original?.Username ?? "";
        HasSavedPassword = original is { HasLogin: true } && list.HasSavedPassword(original);
        CertificatesChanged();
        if (original is not { MutualTlsPort: not null })
            ChooseQuietly(() => SelectedClientAuthority = ClientAuthorities[0]);
    }

    public bool IsNew => _original is null;

    public string Title => _original is null ? Strings.BrokerEditor_NewBroker : string.Format(Strings.BrokerEditor_Edit, _original.Name);

    [ObservableProperty]
    public partial string NameText { get; set; }

    /// <summary>The port for plain MQTT; empty for none.</summary>
    [ObservableProperty]
    public partial string PortText { get; set; }

    [ObservableProperty]
    public partial string TlsPortText { get; set; }

    [ObservableProperty]
    public partial string MutualTlsPortText { get; set; }

    /// <summary>The user clients sign in as; empty to let anyone connect.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PasswordHint))]
    public partial string Username { get; set; }

    /// <summary>The password typed; empty keeps the one saved.</summary>
    [ObservableProperty]
    public partial string Password { get; set; } = "";

    public bool HasSavedPassword { get; }

    public string PasswordHint => HasSavedPassword ? Strings.BrokerEditor_ASavedPasswordIsKept : Strings.BrokerEditor_NeededWithAUsername;

    /// <summary>The server certificates made on the Certificates page, after none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoServerCertificates))]
    public partial IReadOnlyList<CertificateOption> ServerCertificates { get; set; } = [];

    [ObservableProperty]
    public partial CertificateOption? SelectedServerCertificate { get; set; }

    public bool HasNoServerCertificates => ServerCertificates.Count <= 1;

    /// <summary>The CAs made on the Certificates page, after the server certificate's own.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<CertificateOption> ClientAuthorities { get; set; } = [];

    [ObservableProperty]
    public partial CertificateOption? SelectedClientAuthority { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Reads the certificates again, keeping what was chosen if it is still there.</summary>
    internal void CertificatesChanged()
    {
        var store = _list.Certificates;
        var usable = store.Entries.Where(e => e.Certificate is not null && e.Problem is null).ToList();
        // The first time, what the broker has; after that, what is chosen.
        var serverId = SelectedServerCertificate is { } server ? server.Id : _original?.ServerCertificateId;
        var authorityId = SelectedClientAuthority is { } authority ? authority.Id : _original?.ClientAuthorityId;

        ServerCertificates = usable.Where(e => e.Kind == CertificateKind.Server)
            .Select(e => new CertificateOption(e.Id, Signed(store, e)))
            .Prepend(new CertificateOption(null, Strings.BrokerEditor_None, IsNone: true))
            .ToList();
        var noMutualTls = SelectedClientAuthority?.IsNone == true;
        ClientAuthorities = usable.Where(e => e.Kind.IsAuthority())
            .Select(e => new CertificateOption(e.Id, Signed(store, e)))
            .Prepend(new CertificateOption(null, Strings.BrokerEditor_TheCAsThatSignedTheServer))
            .Prepend(new CertificateOption(null, Strings.BrokerEditor_NoMutualTls, IsNone: true))
            .ToList();
        ChooseQuietly(() =>
        {
            SelectedServerCertificate = ServerCertificates.FirstOrDefault(c => c.Id == serverId) ?? ServerCertificates[0];
            SelectedClientAuthority = noMutualTls ? ClientAuthorities[0]
                : ClientAuthorities.FirstOrDefault(c => !c.IsNone && c.Id == authorityId) ?? ClientAuthorities[1];
        });
    }

    private void ChooseQuietly(Action choose)
    {
        _loading = true;
        try
        {
            choose();
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnSelectedServerCertificateChanged(CertificateOption? value)
    {
        if (_loading || value is null)
            return;
        if (value.IsNone)
        {
            TlsPortText = "";
            SelectedClientAuthority = ClientAuthorities[0];
        }
        else if (TlsPortText.Trim().Length == 0)
        {
            TlsPortText = Text(FreePort(MosquittoBroker.ContainerTlsPort));
        }
    }

    partial void OnSelectedClientAuthorityChanged(CertificateOption? value)
    {
        if (_loading || value is null)
            return;
        if (value.IsNone)
            MutualTlsPortText = "";
        else if (MutualTlsPortText.Trim().Length == 0)
            MutualTlsPortText = Text(FreePort(MosquittoBroker.ContainerMutualTlsPort));
    }

    // The first port from this one that no other broker uses, nor this one on another of its ports.
    private int FreePort(int from)
    {
        var typed = new[] { PortText, TlsPortText, MutualTlsPortText }
            .Select(t => MosquittoBroker.ParsePort(t, out var port) is null ? port : null).OfType<int>();
        return _list.FreePort(from, _original?.Id, typed);
    }

    private static string Signed(CertificateStore store, StoredCertificate entry)
        => store.Find(entry.IssuerId) is { } issuer ? string.Format(Strings.BrokerEditor_SignedBy, entry.DisplayName, issuer.DisplayName) : entry.DisplayName;

    /// <summary>Chooses a server certificate by its name on the Certificates page. Returns false when there is none of that name.</summary>
    public bool ChooseServerCertificate(string name) => Choose(ServerCertificates, name, c => SelectedServerCertificate = c);

    /// <summary>Chooses the CA for clients' certificates by its name on the Certificates page. Returns false when there is none of that name.</summary>
    public bool ChooseClientAuthority(string name) => Choose(ClientAuthorities, name, c => SelectedClientAuthority = c);

    private bool Choose(IEnumerable<CertificateOption> options, string name, Action<CertificateOption> choose)
    {
        var entry = _list.Certificates.Entries.FirstOrDefault(e => e.DisplayName.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (options.FirstOrDefault(o => !o.IsNone && o.Id is not null && o.Id == entry?.Id) is not { } option)
            return false;
        choose(option);
        return true;
    }

    private bool CanSave => _list.CanRun;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => SaveBrokerAsync();

    /// <summary>Checks and saves the broker. Returns false, with <see cref="Error"/> set, when it cannot be saved.</summary>
    public async Task<bool> SaveBrokerAsync()
    {
        if (Build(out var problem) is not { } broker)
        {
            Error = problem;
            return false;
        }

        Error = await _list.SaveAsync(broker, _original, Password.Length > 0 ? Password : null);
        return Error is null;
    }

    [RelayCommand]
    private void Cancel() => _list.CloseEditor();

    private MosquittoBroker? Build(out string? problem)
    {
        var others = _list.Brokers.Where(b => b.Id != _original?.Id).ToList();
        var portProblem = MosquittoBroker.ParsePort(PortText, out var port);
        var tlsProblem = MosquittoBroker.ParsePort(TlsPortText, out var tls);
        var mutualProblem = MosquittoBroker.ParsePort(MutualTlsPortText, out var mutual);
        problem = MosquittoBroker.CheckName(NameText, others.Select(b => b.Name)) ?? portProblem ?? tlsProblem ?? mutualProblem;
        if (problem is not null)
            return null;

        var broker = _original?.Clone() ?? new MosquittoBroker();
        broker.Name = NameText.Trim();
        broker.Port = port;
        broker.TlsPort = tls;
        broker.MutualTlsPort = mutual;
        broker.Username = Username.Trim();
        broker.ServerCertificateId = SelectedServerCertificate?.Id;
        broker.ClientAuthorityId = SelectedClientAuthority is { IsNone: false } authority ? authority.Id : null;

        if (broker.HasLogin && Password.Length > 0 && !_list.CanKeepPasswords)
        {
            problem = Strings.BrokerEditor_ThisComputerHasNoCredentialStore;
            return null;
        }

        problem = broker.Check(others, _list.Certificates, Password.Length > 0 || HasSavedPassword);
        return problem is null ? broker : null;
    }

    internal void BusyChanged() => SaveCommand.NotifyCanExecuteChanged();

    private static string Text(int? port) => port?.ToString(CultureInfo.InvariantCulture) ?? "";
}
