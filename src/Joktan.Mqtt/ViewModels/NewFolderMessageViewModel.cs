using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// The form for a new folder message, shown on a folder messages card: its topic, file name and
/// body. Create writes the file and hands its path back, and the card reads the folder again.
/// </summary>
public partial class NewFolderMessageViewModel : ViewModelBase
{
    private readonly Func<string?> _folder;
    private readonly Func<string, Task> _created;
    private readonly Action _close;

    /// <param name="folder">The folder the message goes in, read when it is created.</param>
    /// <param name="created">Given the new file's path, once it is written.</param>
    /// <param name="close">Takes the form away.</param>
    public NewFolderMessageViewModel(Func<string?> folder, Func<string, Task> created, Action close, string topic = "")
    {
        _folder = folder;
        _created = created;
        _close = close;
        Topic = topic;
    }

    [ObservableProperty]
    public partial string Topic { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "message";

    [ObservableProperty]
    public partial string Body { get; set; } = "{\n  \n}";

    [ObservableProperty]
    public partial string? Error { get; set; }

    [RelayCommand]
    private async Task CreateAsync()
    {
        string path;
        try
        {
            path = FolderMessageFile.Create(_folder(), Topic, Name, Body);
        }
        catch (FormatException e)
        {
            Error = e.Message;
            return;
        }

        Error = null;
        _close();
        await _created(path);
    }

    [RelayCommand]
    private void Cancel() => _close();
}
