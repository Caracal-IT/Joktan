using System;
using Joktan.Mqtt;

namespace Joktan.ViewModels;

/// <summary>
/// One row of the folder message list.
/// </summary>
public class FolderMessageRowViewModel : ViewModelBase
{
    private const string None = "–";

    public FolderMessageRowViewModel(FolderMessage message)
    {
        Message = message;
    }

    public FolderMessage Message { get; }

    public string Topic => Message.Topic.Length == 0 ? None : Message.Topic;

    public string File => Message.FileName;

    public string Schedule => Message.IsValid ? Message.Settings.Schedule.Describe() : None;

    public string NextSend => Format(Message.NextDue);

    public string LastSent => Format(Message.LastSent);

    public string Sent => Message.SentCount.ToString();

    public string State => Message.IsValid ? Message.State.ToString() : $"Invalid: {Message.Error}";

    public bool IsInvalid => !Message.IsValid;

    /// <summary>Re-reads every column after the message's progress has changed.</summary>
    public void Refresh() => OnPropertyChanged(string.Empty);

    private static string Format(DateTime? time)
    {
        if (time is not { } t)
            return None;
        return t.Date == DateTime.Today ? t.ToString("HH:mm:ss") : t.ToString("yyyy-MM-dd HH:mm");
    }
}
