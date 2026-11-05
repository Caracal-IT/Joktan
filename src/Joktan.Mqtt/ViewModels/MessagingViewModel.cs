using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// Settings / Alerts and Messages: the MQTT server they go through, the name and picture on the user's
/// messages, and whether the server also shows under Dev Tools / Brokers. Specified in
/// docs/features/alerts-and-messages/spec.md.
/// </summary>
public partial class MessagingViewModel : PageViewModel
{
    /// <summary>The width and height a picture is kept at.</summary>
    public const int AvatarPixels = 128;

    private readonly MqttViewModel _mqtt;
    private readonly InboxViewModel _inbox;

    public MessagingViewModel(MqttViewModel mqtt, InboxViewModel inbox)
        : base(Strings.Messaging_AlertsAndMessages, Strings.Messaging_SettingsAlertsAndMessages)
    {
        _mqtt = mqtt;
        _inbox = inbox;
        ServerChoices = ChoicesNow();
        mqtt.BrokerChoicesChanged += () =>
        {
            ServerChoices = ChoicesNow();
            OnPropertyChanged(nameof(ServerChoices));
            OnPropertyChanged(nameof(HasServerChoices));
            OnPropertyChanged(nameof(SelectedServer));
        };
        mqtt.MessagingClient.PropertyChanged += OnClientChanged;
        DisplayName = mqtt.Messaging.DisplayName;
        TopicPrefix = mqtt.Messaging.TopicPrefix;
        Avatar = ReadAvatar();
    }

    /// <summary>The server's client, which owns its connection.</summary>
    public MqttClientViewModel Client => _mqtt.MessagingClient;

    /// <summary>Asks the user for a picture. Set by the view, which owns the system dialog.</summary>
    public Func<Task<string?>>? PickAvatar { get; set; }

    /// <summary>Shows the client editor and returns whether it was saved. Set by the view, as the MQTT page's view is not showing here.</summary>
    public Func<ClientSettingsViewModel, Task<bool>>? EditClientSettings
    {
        set => _mqtt.EditClientSettings = value;
    }

    /// <summary>A server of the user's own, then each port of the Mosquitto brokers made on Dev Tools / Containers.</summary>
    public IReadOnlyList<BrokerChoice> ServerChoices { get; private set; }

    public bool HasServerChoices => ServerChoices.Count > 1;

    /// <summary>
    /// The broker's port the server is, which it then follows; the first choice when it is one of
    /// the user's own, set with Edit server. Only changed while the server is disconnected.
    /// </summary>
    public BrokerChoice? SelectedServer
    {
        get
        {
            var profile = Client.Profile;
            return profile.BrokerId is { } id
                ? ServerChoices.FirstOrDefault(c => c.BrokerId == id && c.Listener == profile.BrokerListener)
                  ?? ServerChoices.FirstOrDefault(c => c.BrokerId == id) ?? ServerChoices[0]
                : ServerChoices[0];
        }
        set
        {
            if (value is null || value == SelectedServer || !Client.CanEdit)
            {
                OnPropertyChanged();
                return;
            }

            _mqtt.LinkToBroker(Client, value);
            OnPropertyChanged();
        }
    }

    private List<BrokerChoice> ChoicesNow()
        => _mqtt.BrokerChoices.Prepend(new BrokerChoice("", "", BrokerListener.Plain, Strings.Messaging_AServerOfYourOwn, new ClientProfile(), "")).ToList();

    // Edit server can link or unlink it too.
    private void OnClientChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MqttClientViewModel.Address) or nameof(MqttClientViewModel.Security))
            OnPropertyChanged(nameof(SelectedServer));
    }

    public int MaxDisplayNameLength => MessagingSettings.MaxDisplayNameLength;

    public bool ShowInBrokers
    {
        get => _mqtt.IsMessagingClientShown;
        set
        {
            _mqtt.IsMessagingClientShown = value;
            OnPropertyChanged();
        }
    }

    public bool ConnectOnStart
    {
        get => _mqtt.Messaging.ConnectOnStart;
        set
        {
            _mqtt.Messaging.ConnectOnStart = value;
            _mqtt.SaveSettings();
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TopicPrefixError))]
    public partial string TopicPrefix { get; set; } = "";

    /// <summary>Why the prefix typed in cannot be used. The one before stays in use until it can.</summary>
    public string? TopicPrefixError => TopicPrefix.Trim().Length == 0 ? Strings.Messaging_EnterWhatTheTopicsStartWith : TopicRules.Validate(TopicPrefix.Trim());

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvatarInitial), nameof(DisplayNameError), nameof(DisplayNameWarning))]
    public partial string DisplayName { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    [NotifyCanExecuteChangedFor(nameof(RemoveAvatarCommand))]
    public partial IImage? Avatar { get; private set; }

    public bool HasAvatar => Avatar is not null;

    /// <summary>Why the name typed in cannot be used. The one before stays in use until it can.</summary>
    public string? DisplayNameError => MessagingSettings.CheckName(DisplayName.Trim());

    /// <summary>Says when someone else on the server has the name typed in.</summary>
    public string? DisplayNameWarning => DisplayNameError is null && _inbox.IsNameTaken(DisplayName.Trim())
        ? string.Format(Strings.Messaging_SomeoneElseOnThisServerIs, DisplayName.Trim(), DisplayName.Trim())
        : null;

    /// <summary>What stands in for a picture: the first letter of the name.</summary>
    public string AvatarInitial => InboxViewModel.InitialOf(DisplayName);

    /// <summary>Why the chosen picture could not be used.</summary>
    [ObservableProperty]
    public partial string? AvatarError { get; private set; }

    partial void OnDisplayNameChanged(string value)
    {
        if (DisplayNameError is not null)
            return;

        _mqtt.Messaging.DisplayName = value.Trim();
        _mqtt.SaveSettings();
        _inbox.IdentityChanged();
    }

    partial void OnTopicPrefixChanged(string value)
    {
        var prefix = value.Trim();
        if (TopicPrefixError is not null || prefix == _mqtt.Messaging.TopicPrefix)
            return;

        _mqtt.Messaging.TopicPrefix = prefix;
        _mqtt.SaveSettings();
        _ = _inbox.Subscribe();
        _inbox.IdentityChanged();
    }

    [RelayCommand]
    private async Task ChooseAvatarAsync()
    {
        if (PickAvatar is not null && await PickAvatar() is { } path)
            UseAvatar(path);
    }

    /// <summary>Keeps a small square copy of this picture, cut from its middle, in place of any before.</summary>
    public void UseAvatar(string path)
    {
        try
        {
            using var picture = new Bitmap(path);
            using var small = Shrink(picture);
            Directory.CreateDirectory(DataFolder.For("messaging"));
            small.Save(MessagingSettings.AvatarPath, PngBitmapEncoderOptions.Default);
            Avatar = ReadAvatar();
            AvatarError = null;
            _inbox.IdentityChanged();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AvatarError = string.Format(Strings.Messaging_CouldNotUse, Path.GetFileName(path), e.Message);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            AvatarError = string.Format(Strings.Messaging_IsNotAPictureThatCan, Path.GetFileName(path));
        }
    }

    private bool CanRemoveAvatar => HasAvatar;

    [RelayCommand(CanExecute = nameof(CanRemoveAvatar))]
    private void RemoveAvatar()
    {
        try
        {
            File.Delete(MessagingSettings.AvatarPath);
            Avatar = null;
            AvatarError = null;
            _inbox.IdentityChanged();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AvatarError = string.Format(Strings.Messaging_CouldNotRemoveThePicture, e.Message);
        }
    }

    private static RenderTargetBitmap Shrink(Bitmap picture)
    {
        var side = Math.Min(picture.Size.Width, picture.Size.Height);
        var middle = new Rect((picture.Size.Width - side) / 2, (picture.Size.Height - side) / 2, side, side);
        var small = new RenderTargetBitmap(new PixelSize(AvatarPixels, AvatarPixels));
        using var context = small.CreateDrawingContext();
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            context.DrawImage(picture, middle, new Rect(0, 0, AvatarPixels, AvatarPixels));
        return small;
    }

    // A picture that has gone or been damaged is treated as none.
    private static Bitmap? ReadAvatar()
    {
        try
        {
            return File.Exists(MessagingSettings.AvatarPath) ? new Bitmap(MessagingSettings.AvatarPath) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
