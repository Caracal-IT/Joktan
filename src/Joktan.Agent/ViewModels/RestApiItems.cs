using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Agent.Mcp;
using Joktan.Agent.Resources;
using Joktan.Agent.Rest;
using Joktan.Core.ViewModels;

namespace Joktan.Agent.ViewModels;

/// <summary>One API key on Settings / REST API.</summary>
public sealed class ApiKeyViewModel(ApiKey key, Action save, Func<DateOnly> today) : ViewModelBase
{
    public ApiKey Key { get; } = key;

    public string Name => Key.Name;

    public string Hint => $"{Key.Hint}…";

    public string CreatedText => string.Format(Strings.RestApiSettings_Created, Key.Created.LocalDateTime.ToString("yyyy-MM-dd"));

    public string ExpiresText => Key.Expires is { } last
        ? string.Format(Key.StateOn(today()) == ApiKeyState.Expired ? Strings.RestApiSettings_ExpiredOn : Strings.RestApiSettings_WorksUntil, last.ToString("yyyy-MM-dd"))
        : Strings.RestApiSettings_NeverExpires;

    public bool IsExpired => Key.StateOn(today()) == ApiKeyState.Expired;

    public bool IsEnabled
    {
        get => Key.Enabled;
        set
        {
            if (value == Key.Enabled)
                return;
            Key.Enabled = value;
            save();
            OnPropertyChanged();
        }
    }
}

/// <summary>A row of the tree of calls on Settings / REST API.</summary>
public abstract partial class RestTreeItemViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}

/// <summary>A category of calls, such as Notes, with a switch for all of those listed in it.</summary>
public sealed class RestCategoryViewModel : RestTreeItemViewModel
{
    private readonly IReadOnlyList<RestCallViewModel> _all;

    public RestCategoryViewModel(string name, IReadOnlyList<RestCallViewModel> calls)
    {
        Name = name;
        _all = calls;
        foreach (var call in calls)
            call.Changed += Refresh;
        Calls = new ObservableCollection<RestCallViewModel>(calls);
    }

    public string Name { get; }

    /// <summary>The calls listed, after the page's filter.</summary>
    public ObservableCollection<RestCallViewModel> Calls { get; }

    public string Summary => string.Format(Strings.RestApiSettings_OfOn, _all.Count(c => c.IsOn), _all.Count);

    /// <summary>On when every call listed is on, off when none is, and neither in between. Setting it switches them all.</summary>
    public bool? IsOn
    {
        get => Calls.All(c => c.IsOn) ? true : Calls.Any(c => c.IsOn) ? null : false;
        set
        {
            if (value is not { } on)
                return;
            foreach (var call in Calls.ToList())
                call.IsOn = on;
        }
    }

    /// <summary>Lists the calls whose name or description holds the filter, and says whether there were any.</summary>
    public bool Filter(string filter)
    {
        Calls.Clear();
        foreach (var call in _all.Where(c => filter.Length == 0 || c.Matches(filter)))
            Calls.Add(call);
        Refresh();
        return Calls.Count > 0;
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(Summary));
    }
}

/// <summary>One REST call on Settings / REST API, with its switch.</summary>
public sealed class RestCallViewModel(McpTool tool, RestApiSettings settings) : RestTreeItemViewModel
{
    public McpTool Tool { get; } = tool;

    public string Name => Tool.Name;

    public string Description => Tool.Description;

    public string Usage => OpenApi.TakesGet(Tool) ? $"GET, POST {RestApi.Root}/{Tool.Name}" : $"POST {RestApi.Root}/{Tool.Name}";

    /// <summary>Whether the call changes something, which no one is asked about when it comes over REST.</summary>
    public bool Changes => Tool.Risk != ToolRisk.Read;

    public bool IsRisky => Tool.Risk == ToolRisk.Risky;

    /// <summary>Raised when the call is switched on or off.</summary>
    public event Action? Changed;

    public bool IsOn
    {
        get => settings.IsCallOn(Tool);
        set
        {
            if (value == IsOn)
                return;
            settings.SetCall(Tool, value);
            settings.Save();
            OnPropertyChanged();
            Changed?.Invoke();
        }
    }

    public bool Matches(string filter)
        => Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || Description.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
