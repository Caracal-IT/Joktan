using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Mqtt;

namespace Joktan.ViewModels;

/// <summary>Which certificate file the editor is asking the user for.</summary>
public enum CertificateFileKind
{
    Authority,
    Client,
    Key,
}

/// <summary>
/// Editor for one MQTT client: the broker, the login, and TLS with its certificates.
/// </summary>
public partial class ClientSettingsViewModel : ViewModelBase
{
    private const string PlainPort = "1883";
    private const string TlsPort = "8883";

    private readonly IReadOnlyList<string> _otherNames;
    private bool _loading = true;

    /// <param name="otherNames">The names of the other clients, which this one may not take.</param>
    public ClientSettingsViewModel(ClientProfile profile, string password, string keyPassword, IReadOnlyList<string> otherNames, bool isNew)
    {
        _otherNames = otherNames;
        IsNew = isNew;
        Name = profile.Name;
        Host = profile.Host;
        Port = profile.Port;
        Username = profile.Username;
        Password = password;
        ClientId = profile.ClientId;
        UseTls = profile.UseTls;
        CaCertificate = profile.CaCertificate;
        ClientCertificate = profile.ClientCertificate;
        ClientKey = profile.ClientKey;
        KeyPassword = keyPassword;
        AcceptAnyServerCertificate = profile.AcceptAnyServerCertificate;
        _loading = false;
        Revalidate();
    }

    public bool IsNew { get; }

    public string Title => IsNew ? "New client" : $"Client {Name}";

    /// <summary>Asks the user for a certificate file. Set by the window, which owns the system dialog.</summary>
    public Func<CertificateFileKind, Task<string?>>? PickFile { get; set; }

    /// <summary>Raised when the editor should close; true if the settings were saved.</summary>
    public event Action<bool>? CloseRequested;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Host { get; set; } = "";

    [ObservableProperty]
    public partial string Port { get; set; } = PlainPort;

    [ObservableProperty]
    public partial string Username { get; set; } = "";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial string ClientId { get; set; } = "";

    [ObservableProperty]
    public partial bool UseTls { get; set; }

    [ObservableProperty]
    public partial string CaCertificate { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowKeyFile))]
    public partial string ClientCertificate { get; set; } = "";

    [ObservableProperty]
    public partial string ClientKey { get; set; } = "";

    [ObservableProperty]
    public partial string KeyPassword { get; set; } = "";

    [ObservableProperty]
    public partial bool AcceptAnyServerCertificate { get; set; }

    /// <summary>A PFX/P12 file holds its own key, so the key file is only asked for with PEM.</summary>
    public bool ShowKeyFile => !CertificateLoader.IsPkcs12(ClientCertificate.Trim());

    /// <summary>What the chosen certificate files hold, for example who the client certificate names.</summary>
    [ObservableProperty]
    public partial string CertificateInfo { get; set; } = "";

    /// <summary>Why a certificate file cannot be used. It does not stop the settings being saved.</summary>
    [ObservableProperty]
    public partial string? CertificateProblem { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? Error { get; set; }

    private bool CanSave => Error is null;

    public static bool IsValidHost(string host) => host.Trim() is { Length: > 0 } h && !h.Any(char.IsWhiteSpace);

    public static int? ParsePort(string port)
        => int.TryParse(port.Trim(), out var value) && value is >= 1 and <= 65535 ? value : null;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_loading && e.PropertyName is not (nameof(Error) or nameof(CertificateInfo) or nameof(CertificateProblem)))
            Revalidate();
    }

    // The usual port follows TLS being switched on or off, unless another one was typed in.
    partial void OnUseTlsChanged(bool value)
    {
        if (_loading)
            return;
        if (value && Port.Trim() == PlainPort)
            Port = TlsPort;
        else if (!value && Port.Trim() == TlsPort)
            Port = PlainPort;
    }

    [RelayCommand]
    private async Task BrowseAsync(CertificateFileKind kind)
    {
        if (PickFile is null || await PickFile(kind) is not { } path)
            return;

        switch (kind)
        {
            case CertificateFileKind.Authority:
                CaCertificate = path;
                break;
            case CertificateFileKind.Client:
                ClientCertificate = path;
                break;
            case CertificateFileKind.Key:
                ClientKey = path;
                break;
        }
    }

    [RelayCommand]
    private void ClearFile(CertificateFileKind kind)
    {
        switch (kind)
        {
            case CertificateFileKind.Authority:
                CaCertificate = "";
                break;
            case CertificateFileKind.Client:
                ClientCertificate = "";
                ClientKey = "";
                KeyPassword = "";
                break;
            case CertificateFileKind.Key:
                ClientKey = "";
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => CloseRequested?.Invoke(true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    /// <summary>The settings as entered. Call only after the editor was saved.</summary>
    public ClientProfile ToProfile() => new()
    {
        Name = Name.Trim(),
        Host = Host.Trim(),
        Port = Port.Trim(),
        Username = Username.Trim(),
        ClientId = ClientId.Trim() is { Length: > 0 } id ? id : ClientProfile.NewClientId(),
        UseTls = UseTls,
        CaCertificate = CaCertificate.Trim(),
        ClientCertificate = ClientCertificate.Trim(),
        ClientKey = ShowKeyFile ? ClientKey.Trim() : "",
        AcceptAnyServerCertificate = AcceptAnyServerCertificate,
    };

    private void Revalidate()
    {
        Error = ProfileError();
        CheckCertificates();
    }

    private string? ProfileError()
    {
        if (ClientProfile.CheckName(Name, _otherNames) is { } nameError)
            return nameError;
        if (!IsValidHost(Host))
            return "Enter the broker's host name or IP address, without spaces";
        if (ParsePort(Port) is null)
            return "The port must be a whole number from 1 to 65535";
        if (Password.Length > 0 && Username.Trim().Length == 0)
            return "A password is only sent with a username";
        if (ClientId.Trim().Any(char.IsWhiteSpace))
            return "The client ID must not contain spaces";
        if (UseTls && ShowKeyFile && ClientKey.Trim().Length > 0 && ClientCertificate.Trim().Length == 0)
            return "Choose the client certificate that goes with the private key";
        return null;
    }

    // Reads the files now, so a wrong file or password shows up before connecting.
    private void CheckCertificates()
    {
        var info = new List<string>();
        CertificateProblem = null;
        if (!UseTls)
        {
            CertificateInfo = "";
            return;
        }

        try
        {
            if (CaCertificate.Trim().Length > 0)
            {
                var authorities = CertificateLoader.LoadAuthorities(CaCertificate);
                info.Add(authorities.Count == 1
                    ? $"CA: {authorities[0].GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false)}"
                    : $"CA: {authorities.Count} certificates");
            }

            if (ClientCertificate.Trim().Length > 0)
            {
                using var client = CertificateLoader.LoadClient(ClientCertificate, ShowKeyFile ? ClientKey : null, KeyPassword);
                var subject = client.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false);
                info.Add($"Client certificate: {subject}, valid until {client.NotAfter:yyyy-MM-dd}");
            }
        }
        catch (FormatException e)
        {
            CertificateProblem = e.Message;
        }

        CertificateInfo = string.Join(" · ", info);
    }
}
