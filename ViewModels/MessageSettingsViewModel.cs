using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Mqtt;

namespace Joktan.ViewModels;

/// <summary>
/// Editor for a folder message's settings file: schedule, delivery options and varying fields.
/// </summary>
public partial class MessageSettingsViewModel : ViewModelBase
{
    private const string DateDisplayFormat = "yyyy-MM-dd HH:mm:ss";
    private const string DateExample = "2026-11-02 08:30:00";

    private readonly FolderMessage _message;
    private bool _loading = true;

    public MessageSettingsViewModel(FolderMessage message, IReadOnlyList<string>? groupNames = null)
    {
        _message = message;
        GroupNames = groupNames ?? [];
        BodyPaths = VaryingFields.ListPaths(message.Body);

        var settings = message.Settings;
        var schedule = settings.Schedule;
        Enabled = settings.Enabled;
        Group = settings.Group;
        Topic = settings.Topic;
        QosIndex = settings.Qos;
        Retain = settings.Retain;
        ScheduleTypeIndex = (int)schedule.Type;
        OnceAt = FormatDate(schedule.At);
        Every = schedule.Every.ToString(CultureInfo.InvariantCulture);
        UnitIndex = (int)schedule.Unit;
        IntervalStart = FormatDate(schedule.Start);
        IntervalEnd = FormatDate(schedule.End);
        MaxSends = schedule.MaxSends?.ToString(CultureInfo.InvariantCulture) ?? "";

        if (schedule.Type == ScheduleType.Daily)
        {
            DailyTimes = string.Join(", ", schedule.Times.Order().Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)));
            Monday = schedule.Days.Contains(DayOfWeek.Monday);
            Tuesday = schedule.Days.Contains(DayOfWeek.Tuesday);
            Wednesday = schedule.Days.Contains(DayOfWeek.Wednesday);
            Thursday = schedule.Days.Contains(DayOfWeek.Thursday);
            Friday = schedule.Days.Contains(DayOfWeek.Friday);
            Saturday = schedule.Days.Contains(DayOfWeek.Saturday);
            Sunday = schedule.Days.Contains(DayOfWeek.Sunday);
        }

        foreach (var rule in settings.Fields)
            Add(Fields, new FieldRuleViewModel(rule));
        foreach (var rule in settings.TopicFields)
            Add(TopicFields, new FieldRuleViewModel(rule));

        _loading = false;
        Revalidate();
    }

    public string Title => $"{_message.Topic} · {_message.FileName}";

    public IReadOnlyList<string> BodyPaths { get; }

    /// <summary>Groups already in use in the folder, offered as suggestions.</summary>
    public IReadOnlyList<string> GroupNames { get; }

    public ObservableCollection<FieldRuleViewModel> Fields { get; } = [];

    public ObservableCollection<FieldRuleViewModel> TopicFields { get; } = [];

    public string TopicPlaceholder => _message.Folder.Length == 0
        ? "Required: the file is not inside a topic folder"
        : $"{_message.Folder} (the folder path)";

    /// <summary>The {name} parts of the topic, offered as topic field names.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> TopicNames { get; set; } = [];

    /// <summary>Raised when the editor should close; true if the settings were saved.</summary>
    public event Action<bool>? CloseRequested;

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial string Group { get; set; } = "";

    /// <summary>The topic to send to instead of the folder path; empty to use the folder path.</summary>
    [ObservableProperty]
    public partial string Topic { get; set; } = "";

    [ObservableProperty]
    public partial int QosIndex { get; set; }

    [ObservableProperty]
    public partial bool Retain { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOnce), nameof(IsInterval), nameof(IsDaily))]
    public partial int ScheduleTypeIndex { get; set; }

    [ObservableProperty]
    public partial string OnceAt { get; set; } = "";

    [ObservableProperty]
    public partial string Every { get; set; } = "5";

    [ObservableProperty]
    public partial int UnitIndex { get; set; }

    [ObservableProperty]
    public partial string IntervalStart { get; set; } = "";

    [ObservableProperty]
    public partial string IntervalEnd { get; set; } = "";

    [ObservableProperty]
    public partial string MaxSends { get; set; } = "";

    [ObservableProperty]
    public partial string DailyTimes { get; set; } = "08:00";

    [ObservableProperty]
    public partial bool Monday { get; set; } = true;

    [ObservableProperty]
    public partial bool Tuesday { get; set; } = true;

    [ObservableProperty]
    public partial bool Wednesday { get; set; } = true;

    [ObservableProperty]
    public partial bool Thursday { get; set; } = true;

    [ObservableProperty]
    public partial bool Friday { get; set; } = true;

    [ObservableProperty]
    public partial bool Saturday { get; set; }

    [ObservableProperty]
    public partial bool Sunday { get; set; }

    /// <summary>The body with the varying fields filled in, as it would be sent now.</summary>
    [ObservableProperty]
    public partial string Preview { get; set; } = "";

    /// <summary>The topic with its fields filled in, as it would be sent now.</summary>
    [ObservableProperty]
    public partial string TopicPreview { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? Error { get; set; }

    public ScheduleType ScheduleType => (ScheduleType)Math.Max(ScheduleTypeIndex, 0);

    public bool IsOnce => ScheduleType == ScheduleType.Once;

    public bool IsInterval => ScheduleType == ScheduleType.Interval;

    public bool IsDaily => ScheduleType == ScheduleType.Daily;

    private bool CanSave => Error is null;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is not (nameof(Preview) or nameof(TopicPreview) or nameof(TopicNames) or nameof(Error)))
            Revalidate();
    }

    [RelayCommand]
    private void AddField()
    {
        // Suggest the first field of the body that has no rule yet.
        var used = Fields.Select(f => f.Path).ToHashSet();
        Add(Fields, new FieldRuleViewModel { Path = BodyPaths.FirstOrDefault(p => !used.Contains(p)) ?? "" });
        Revalidate();
    }

    [RelayCommand]
    private void AddTopicField()
    {
        // Suggest the first {name} in the topic that has no rule yet; "Each in turn" suits a topic best.
        var used = TopicFields.Select(f => f.Path).ToHashSet();
        Add(TopicFields, new FieldRuleViewModel
        {
            Path = TopicNames.FirstOrDefault(n => !used.Contains(n)) ?? "",
            TypeIndex = (int)FieldType.Cycle,
        });
        Revalidate();
    }

    [RelayCommand]
    private void RemoveField(FieldRuleViewModel field)
    {
        field.PropertyChanged -= OnFieldChanged;
        Fields.Remove(field);
        TopicFields.Remove(field);
        Revalidate();
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        try
        {
            File.WriteAllText(MessageSettings.PathFor(_message.FilePath), Build().ToJson());
            CloseRequested?.Invoke(true);
        }
        catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    private void Add(ICollection<FieldRuleViewModel> list, FieldRuleViewModel field)
    {
        field.PropertyChanged += OnFieldChanged;
        list.Add(field);
    }

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e) => Revalidate();

    private void Revalidate()
    {
        if (_loading)
            return;

        var topic = TopicTemplate.Effective(_message.Folder, (Topic ?? "").Trim());
        var names = TopicTemplate.Names(topic);
        if (!names.SequenceEqual(TopicNames))
            TopicNames = names;

        try
        {
            var settings = Build();
            var now = DateTimeOffset.Now;
            Preview = _message.Body is null
                ? ""
                : VaryingFields.Apply(_message.Body, settings.Fields, new Dictionary<string, long>(), now)
                    .ToJsonString(JsonFormats.Indented);
            TopicPreview = TopicTemplate.Resolve(topic, settings.TopicFields, new Dictionary<string, long>(), now);
            Error = null;
        }
        catch (FormatException e)
        {
            Error = e.Message;
        }
    }

    private MessageSettings Build()
    {
        var fields = Fields.Select(f => f.ToRule()).ToList();

        var duplicate = fields.GroupBy(f => f.Path).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new FormatException($"Field {duplicate.Key} is listed more than once");
        if (_message.Body is not null && VaryingFields.FindMissing(_message.Body, fields) is { } missing)
            throw new FormatException($"Field {missing} is not in the message body");

        var topicFields = TopicFields.Select(f => f.ToRule()).ToList();
        var topic = (Topic ?? "").Trim();
        var effective = TopicTemplate.Effective(_message.Folder, topic);
        if (topicFields.GroupBy(f => f.Path).FirstOrDefault(g => g.Count() > 1) is { } twice)
            throw new FormatException($"Topic field {twice.Key} is listed more than once");
        if (topicFields.FirstOrDefault(f => !TopicTemplate.IsValidName(f.Path)) is { } badName)
            throw new FormatException($"{badName.Path} cannot be a topic field name; use letters, digits, _, - and .");
        if (effective.Length == 0)
            throw new FormatException("The file is not inside a topic folder, so enter a topic");
        if (TopicTemplate.Check(effective, topicFields) is { } topicError)
            throw new FormatException(topicError);

        return new MessageSettings
        {
            Enabled = Enabled,
            Group = MessageSettings.NormalizeGroup(Group),
            Topic = topic,
            TopicFields = topicFields,
            Qos = Math.Clamp(QosIndex, 0, 2),
            Retain = Retain,
            Schedule = BuildSchedule(),
            Fields = fields,
        };
    }

    private MessageSchedule BuildSchedule()
    {
        switch (ScheduleType)
        {
            case ScheduleType.Once:
                return new MessageSchedule
                {
                    Type = ScheduleType.Once,
                    At = ParseDate(OnceAt, "date and time")
                         ?? throw new FormatException($"Enter the date and time to send at, for example {DateExample}"),
                };

            case ScheduleType.Interval:
                if (!int.TryParse(Every.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var every) || every < 1)
                    throw new FormatException("The interval must be a whole number, 1 or more");

                int? maxSends = null;
                if (MaxSends.Trim().Length > 0)
                {
                    if (!int.TryParse(MaxSends.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var max) || max < 1)
                        throw new FormatException("The maximum number of sends must be a whole number, 1 or more");
                    maxSends = max;
                }

                var start = ParseDate(IntervalStart, "start");
                var end = ParseDate(IntervalEnd, "end");
                if (start > end)
                    throw new FormatException("The end must not be before the start");

                return new MessageSchedule
                {
                    Type = ScheduleType.Interval,
                    Every = every,
                    Unit = (IntervalUnit)Math.Max(UnitIndex, 0),
                    Start = start,
                    End = end,
                    MaxSends = maxSends,
                };

            case ScheduleType.Daily:
                var times = DailyTimes.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => TimeOnly.TryParseExact(t, ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var time)
                        ? time
                        : throw new FormatException($"{t} is not a time of day such as 08:30"))
                    .Distinct()
                    .ToArray();
                if (times.Length == 0)
                    throw new FormatException("Enter at least one time of day, for example 08:00, 12:30");

                (bool Chosen, DayOfWeek Day)[] week =
                [
                    (Monday, DayOfWeek.Monday), (Tuesday, DayOfWeek.Tuesday), (Wednesday, DayOfWeek.Wednesday),
                    (Thursday, DayOfWeek.Thursday), (Friday, DayOfWeek.Friday), (Saturday, DayOfWeek.Saturday),
                    (Sunday, DayOfWeek.Sunday),
                ];
                var days = week.Where(d => d.Chosen).Select(d => d.Day).ToArray();
                if (days.Length == 0)
                    throw new FormatException("Choose at least one day");

                return new MessageSchedule { Type = ScheduleType.Daily, Times = times, Days = days };

            default:
                return MessageSchedule.Manual;
        }
    }

    private static string FormatDate(DateTime? value)
        => value?.ToString(DateDisplayFormat, CultureInfo.InvariantCulture) ?? "";

    private static DateTime? ParseDate(string text, string name)
    {
        if (text.Trim().Length == 0)
            return null;

        return DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value)
            ? value
            : throw new FormatException($"The {name} is not a date and time such as {DateExample}");
    }
}
