using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.Controls;

/// <summary>
/// Topic filters to subscribe to, one chip each, with a box at the end for the next. Enter or a comma adds what
/// was typed; each chip can be removed. <see cref="Text"/> is the filters separated by commas, including the one
/// still being typed, so pages keep reading and saving them as before.
/// </summary>
public partial class TopicList : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<TopicList, string>(nameof(Text), "", defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<TopicList, string?>(nameof(PlaceholderText));

    private readonly List<string> _topics = [];

    // True while this control writes Text, so the change is not read back in.
    private bool _writing;

    public TopicList()
    {
        InitializeComponent();
        // As it changes; TextChanged comes later, after a load has finished.
        Entry.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                OnEntryChanged();
        };
        Entry.AddHandler(KeyDownEvent, OnEntryKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Entry.LostFocus += (_, _) => Commit();
    }

    /// <summary>The filters, separated by commas.</summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Shown in the box while there are no filters.</summary>
    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>The filters as chips, without the one being typed.</summary>
    public IReadOnlyList<string> Topics => _topics;

    /// <summary>Splits filters typed or saved together, as the pages always have: by commas.</summary>
    public static string[] Split(string? text)
        => (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && !_writing)
        {
            // Shown as given; it is only written back once the user changes it.
            _writing = true;
            try
            {
                _topics.Clear();
                Add(Split(Text));
                Entry.Text = "";
                Rebuild();
            }
            finally
            {
                _writing = false;
            }
        }
        else if (change.Property == PlaceholderTextProperty)
            UpdatePlaceholder();
    }

    /// <summary>Makes a chip of what is in the box.</summary>
    public void Commit()
    {
        if (string.IsNullOrWhiteSpace(Entry.Text))
            return;

        Add(Split(Entry.Text));
        Entry.Text = "";
        Rebuild();
        Write();
    }

    /// <summary>Takes a filter away.</summary>
    public void Remove(string topic)
    {
        if (!_topics.Remove(topic))
            return;

        Rebuild();
        Write();
    }

    private void Add(IEnumerable<string> topics)
    {
        foreach (var topic in topics)
            if (!_topics.Contains(topic))
                _topics.Add(topic);
    }

    // A comma, typed or pasted, ends a filter.
    private void OnEntryChanged()
    {
        var text = Entry.Text ?? "";
        var comma = text.LastIndexOf(',');
        if (comma >= 0)
        {
            Add(Split(text[..comma]));
            Entry.Text = text[(comma + 1)..].TrimStart();
            Entry.CaretIndex = Entry.Text.Length;
            Rebuild();
        }

        if (!_writing)
            Write();
    }

    private void OnEntryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(Entry.Text))
        {
            Commit();
            e.Handled = true;
        }
        else if (e.Key == Key.Back && string.IsNullOrEmpty(Entry.Text) && _topics.Count > 0)
        {
            Remove(_topics[^1]);
            e.Handled = true;
        }
    }

    private void Write()
    {
        var pending = (Entry.Text ?? "").Trim();
        var all = pending.Length > 0 && !_topics.Contains(pending) ? _topics.Append(pending) : _topics;
        var text = string.Join(", ", all);
        if (text == Text)
            return;

        _writing = true;
        try
        {
            SetCurrentValue(TextProperty, text);
        }
        finally
        {
            _writing = false;
        }
    }

    // The chips, then the box. The box stays put, so it keeps the focus.
    private void Rebuild()
    {
        Items.Children.RemoveAll(Items.Children.Where(c => c != Entry).ToList());
        Items.Children.InsertRange(0, _topics.Select(Chip));
        UpdatePlaceholder();
    }

    private Border Chip(string topic)
    {
        var error = TopicRules.ValidateFilter(topic);
        var remove = new Button { Content = "×" };
        ToolTip.SetTip(remove, Strings.TopicList_RemoveThisTopic);
        remove.Click += (_, _) => Remove(topic);

        var chip = new Border
        {
            Classes = { "topic-chip" },
            Child = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Children = { new TextBlock { Text = topic }, remove },
            },
        };
        chip.Classes.Set("invalid", error is not null);
        ToolTip.SetTip(chip, error ?? topic);
        return chip;
    }

    private void UpdatePlaceholder()
        => Entry.PlaceholderText = _topics.Count == 0 ? PlaceholderText : Strings.TopicList_AddATopic;
}
