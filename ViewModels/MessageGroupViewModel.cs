using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Joktan.ViewModels;

/// <summary>
/// A named group of rows in the folder message list, with one switch for all of them.
/// </summary>
public partial class MessageGroupViewModel : ViewModelBase
{
    public const string UngroupedName = "Ungrouped";

    private readonly Action<MessageGroupViewModel, bool> _setEnabled;

    public MessageGroupViewModel(
        string name, IReadOnlyList<FolderMessageRowViewModel> rows, bool isExpanded,
        Action<MessageGroupViewModel, bool> setEnabled)
    {
        Name = name;
        Rows = rows;
        IsExpanded = isExpanded;
        _setEnabled = setEnabled;
    }

    /// <summary>The group name as stored in the message settings; empty for ungrouped messages.</summary>
    public string Name { get; }

    public string DisplayName => Name.Length == 0 ? UngroupedName : Name;

    public IReadOnlyList<FolderMessageRowViewModel> Rows { get; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    public bool CanSwitch => Rows.Any(r => r.CanSwitch);

    public string Summary
    {
        get
        {
            var switchable = Rows.Count(r => r.CanSwitch);
            var on = Rows.Count(r => r.IsOn);
            var count = Rows.Count == 1 ? "1 message" : $"{Rows.Count} messages";
            return switchable == 0 ? count : $"{count}, {on} on";
        }
    }

    /// <summary>On when every message that can be switched is on. Changing it switches them all.</summary>
    public bool IsOn
    {
        get => CanSwitch && Rows.Where(r => r.CanSwitch).All(r => r.IsOn);
        set
        {
            if (value != IsOn && CanSwitch)
                _setEnabled(this, value);
        }
    }
}
