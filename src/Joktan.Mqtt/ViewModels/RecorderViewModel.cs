using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// Dev Tools / Brokers / Recorder: records what arrives on a broker through one of the MQTT clients,
/// and plays recordings back through any of them, faster or slower, with fields of the payloads changed
/// as on folder messages. Specified in docs/features/dev-tools/connectivity/mqtt-recorder/spec.md.
/// </summary>
public partial class RecorderViewModel : PageViewModel
{
    private const string DefaultFilters = "#";

    private readonly DispatcherTimer _timer;
    private MessageRecorder? _recorder;
    private MqttClientViewModel? _recordingClient;

    /// <param name="folder">Where recordings are kept; the folder chosen for them otherwise.</param>
    public RecorderViewModel(MqttViewModel mqtt, string? folder = null)
        : base(Strings.Recorder_Recorder, Strings.Recorder_DevToolsBrokersRecorder)
    {
        Clients = mqtt.Clients;
        Folder = Path.GetFullPath(folder ?? Recording.ChosenFolder);
        RecordClient = mqtt.SelectedClient ?? Clients.FirstOrDefault();
        LoadRecordings();
        Clients.CollectionChanged += OnClientsChanged;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => Refresh());
        _timer.Start();
    }

    /// <summary>The clients of the MQTT page, which own the connections.</summary>
    public ObservableCollection<MqttClientViewModel> Clients { get; }

    [ObservableProperty]
    public partial MqttClientViewModel? RecordClient { get; set; }

    /// <summary>Topic filters to record, separated by commas.</summary>
    [ObservableProperty]
    public partial string Filters { get; set; } = DefaultFilters;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordText), nameof(IsNotRecording))]
    public partial bool IsRecording { get; private set; }

    public bool IsNotRecording => !IsRecording;

    public string RecordText => IsRecording ? Strings.Recorder_StopRecording : Strings.Recorder_Record;

    [ObservableProperty]
    public partial string RecordStatus { get; private set; } = Strings.Recorder_PressRecordToStart;

    [ObservableProperty]
    public partial string? RecordError { get; private set; }

    /// <summary>Recordings, newest first.</summary>
    public ObservableCollection<RecordingViewModel> Recordings { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial RecordingViewModel? SelectedRecording { get; set; }

    public bool HasSelection => SelectedRecording is not null;

    public bool HasRecordings => Recordings.Count > 0;

    /// <summary>What happened to the folder or a recording opened from a file.</summary>
    [ObservableProperty]
    public partial string? Message { get; private set; }

    // ---- The recordings folder ----

    /// <summary>Where recordings are kept.</summary>
    [ObservableProperty]
    public partial string Folder { get; private set; }

    public string DefaultFolder => Recording.DefaultFolder;

    [ObservableProperty]
    public partial bool IsChoosingFolder { get; set; }

    [ObservableProperty]
    public partial string FolderText { get; set; } = "";

    [ObservableProperty]
    public partial string? FolderMessage { get; private set; }

    /// <summary>Asks for a folder. Set by the view.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    /// <summary>Asks for a recording file. Set by the view.</summary>
    public Func<Task<string?>>? PickRecording { get; set; }

    // ---- Its own window ----

    /// <summary>Opens the recorder in its own window. Set by the view.</summary>
    public Action? OpenWindow { get; set; }

    /// <summary>Closes the recorder's own window. Set by the view while the window is open.</summary>
    public Action? CloseWindow { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPage))]
    public partial bool IsPoppedOut { get; set; }

    public bool ShowPage => !IsPoppedOut;

    [RelayCommand]
    private void PopOut() => OpenWindow?.Invoke();

    [RelayCommand]
    private void Dock() => CloseWindow?.Invoke();

    [RelayCommand]
    private void ToggleFolder()
    {
        IsChoosingFolder = !IsChoosingFolder;
        FolderText = Folder;
        FolderMessage = null;
    }

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (PickFolder is not null && await PickFolder() is { } folder)
            FolderText = folder;
    }

    [RelayCommand]
    private void UseDefaultFolder() => FolderText = DefaultFolder;

    /// <summary>
    /// Keeps recordings in the folder typed in. One that holds recordings already is opened as it is; otherwise
    /// the recordings are copied there, and the originals stay where they were.
    /// </summary>
    [RelayCommand]
    private void ApplyFolder()
    {
        FolderMessage = null;
        var folder = FolderText.Trim();
        if (folder.Length == 0)
            folder = DefaultFolder;
        if (folder.StartsWith('~'))
            folder = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder[1..].TrimStart('/', '\\'));
        if (!Path.IsPathFullyQualified(folder))
        {
            FolderMessage = Strings.Recorder_GiveTheWholePathOfTheFolder;
            return;
        }

        folder = Path.GetFullPath(folder);
        if (Recording.SameFolder(folder, Folder))
        {
            IsChoosingFolder = false;
            return;
        }

        SelectedRecording?.Save();
        var copied = !Recording.HasRecordings(folder) && Recording.HasRecordings(Folder);
        try
        {
            Directory.CreateDirectory(folder);
            if (copied)
                Recording.CopyAll(Folder, folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            FolderMessage = string.Format(Strings.Recorder_CouldNotUseTheFolder, folder, e.Message);
            return;
        }

        var previous = Folder;
        Recording.SaveChosenFolder(folder);
        Folder = folder;
        LoadRecordings();
        IsChoosingFolder = false;
        Message = copied
            ? string.Format(Strings.Recorder_TheRecordingsWereCopiedTo, folder, previous)
            : string.Format(Strings.Recorder_OpenedTheRecordingsIn, folder);
    }

    /// <summary>Asks for a recording file, kept anywhere, and lists it to be played.</summary>
    [RelayCommand]
    private async Task OpenRecordingAsync()
    {
        if (PickRecording is not null && await PickRecording() is { } file)
            OpenRecording(file);
    }

    /// <summary>
    /// Lists a recording file to be played, copying it into the recordings folder unless it is there already,
    /// and selects it.
    /// </summary>
    public void OpenRecording(string file)
    {
        Message = null;
        Recording recording;
        try
        {
            recording = Recording.Import(file, Folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            Message = string.Format(Strings.Recorder_CouldNotOpen, file, e.Message);
            return;
        }

        if (Recordings.FirstOrDefault(r => r.Recording.Id == recording.Id) is { } listed)
        {
            SelectedRecording = listed;
            return;
        }

        var added = New(recording);
        var index = Recordings.TakeWhile(r => r.Recording.Created >= recording.Created).Count();
        Recordings.Insert(index, added);
        OnPropertyChanged(nameof(HasRecordings));
        SelectedRecording = added;
        Message = string.Format(Strings.Recorder_OpenedRecording, added.Name, Folder);
    }

    // Lists the recordings in the folder, stopping any that were playing from the one before.
    private void LoadRecordings()
    {
        foreach (var recording in Recordings)
            recording.Shutdown();
        Recordings.Clear();
        foreach (var recording in Recording.LoadAll(Folder))
            Recordings.Add(New(recording));
        OnPropertyChanged(nameof(HasRecordings));
        SelectedRecording = Recordings.FirstOrDefault();
    }

    /// <summary>Starts recording, connecting the client first if need be, or stops and keeps what was recorded.</summary>
    [RelayCommand]
    private async Task ToggleRecordAsync()
    {
        if (IsRecording)
        {
            await StopRecordingAsync();
            return;
        }

        RecordError = null;
        if (RecordClient is not { } client)
        {
            RecordError = Strings.Recorder_ChooseAClientToRecord;
            return;
        }

        var filters = Filters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (filters.Length == 0)
        {
            RecordError = Strings.Recorder_EnterAtLeastOneTopicFilter;
            return;
        }

        if (!client.IsConnected)
        {
            await client.ConnectCommand.ExecuteAsync(null);
            if (!client.IsConnected)
            {
                RecordError = string.Format(Strings.Recorder_CouldNotConnect, client.Name, client.Connection.FailureReason ?? client.StateText);
                return;
            }
        }

        var recorder = new MessageRecorder(client.Connection);
        if (await recorder.StartAsync(filters) is { } error)
        {
            RecordError = error;
            return;
        }

        _recorder = recorder;
        _recordingClient = client;
        IsRecording = true;
        Refresh();
    }

    private async Task StopRecordingAsync()
    {
        IsRecording = false;
        if (await FinishRecordingAsync() is not { } recording)
        {
            RecordStatus = Strings.Recorder_NothingArrived;
            return;
        }

        var added = New(recording);
        Recordings.Insert(0, added);
        OnPropertyChanged(nameof(HasRecordings));
        SelectedRecording = added;
        RecordStatus = string.Format(Strings.Recorder_Saved, added.Summary);
    }

    // Stops the recorder and saves what it has, if anything arrived. Touches nothing on screen.
    private async Task<Recording?> FinishRecordingAsync()
    {
        if (_recorder is not { } recorder || _recordingClient is not { } client)
            return null;

        _recorder = null;
        var folder = Folder;
        var started = DateTime.Now - recorder.Elapsed;
        var name = string.Format(CultureInfo.CurrentCulture, Strings.Recorder_RecordingName, client.Name, started);
        var recording = await recorder.StopAsync(Recording.NewId(folder, started), name, client.Name);
        if (recording.Messages.Count == 0)
            return null;

        recording.Save(folder);
        return recording;
    }

    partial void OnSelectedRecordingChanged(RecordingViewModel? oldValue, RecordingViewModel? newValue)
    {
        oldValue?.Save();
        if (oldValue is not null)
            oldValue.IsConfirmingDelete = false;
    }

    private RecordingViewModel New(Recording recording)
        => new(recording, Clients, Folder) { Deleted = OnDeleted };

    private void OnDeleted(RecordingViewModel recording)
    {
        var index = Recordings.IndexOf(recording);
        Recordings.Remove(recording);
        OnPropertyChanged(nameof(HasRecordings));
        if (SelectedRecording is null || SelectedRecording == recording)
            SelectedRecording = Recordings.Count == 0 ? null : Recordings[Math.Min(index, Recordings.Count - 1)];
    }

    private void Refresh()
    {
        if (_recorder is { } recorder)
        {
            var count = recorder.Count;
            RecordStatus = string.Format(Strings.Recorder_RecordingFor, RecordingViewModel.Duration(recorder.Elapsed),
                count == 1 ? Strings.Recorder_1Message : string.Format(Strings.Recorder_Messages, count));
            if (recorder.IsFull)
                RecordStatus += " · " + string.Format(Strings.Recorder_Full, MessageRecorder.MaxMessages);
        }

        foreach (var recording in Recordings)
        {
            if (recording.IsPlaying)
                recording.UpdateProgress();
        }
    }

    // A client taken away from the MQTT page can no longer be used to record or play.
    private void OnClientsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsRecording && !Clients.Contains(_recordingClient!))
            _ = StopRecordingAsync();
        if (RecordClient is not null && !Clients.Contains(RecordClient))
            RecordClient = Clients.FirstOrDefault();

        foreach (var recording in Recordings.Where(r => r.PlayClient is not null && !Clients.Contains(r.PlayClient)))
        {
            recording.StopCommand.Execute(null);
            recording.PlayClient = Clients.FirstOrDefault();
        }
    }

    /// <summary>Keeps what is being recorded, and stops playing, when the app closes.</summary>
    public void Shutdown()
    {
        _timer.Stop();
        if (IsRecording)
            Task.Run(FinishRecordingAsync).Wait(TimeSpan.FromSeconds(3));
        foreach (var recording in Recordings)
            recording.Shutdown();
    }
}
