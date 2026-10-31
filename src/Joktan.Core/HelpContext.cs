using System;
using Avalonia;
using Avalonia.Controls;

namespace Joktan.Core;

/// <summary>
/// Names the help topic a window is about, so F1 in a settings dialog or a tool window opens that
/// topic rather than the one for the page behind it.
/// </summary>
public sealed class HelpContext : AvaloniaObject
{
    public static readonly AttachedProperty<string?> TopicProperty =
        AvaloniaProperty.RegisterAttached<HelpContext, Window, string?>("Topic");

    /// <summary>Shows help on a topic, or on the page being shown when it is null. Set by the app, which knows the help window.</summary>
    public static Action<string?>? Opener { get; set; }

    /// <summary>Opens help on the topic the window names.</summary>
    public static void Open(Window? window) => Opener?.Invoke(window is null ? null : GetTopic(window));

    public static string? GetTopic(Window window) => window.GetValue(TopicProperty);

    public static void SetTopic(Window window, string? value) => window.SetValue(TopicProperty, value);
}
