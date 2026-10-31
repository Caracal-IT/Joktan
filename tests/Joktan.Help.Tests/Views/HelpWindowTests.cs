using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Joktan.Core.Controls;
using Joktan.Help.ViewModels;
using Joktan.Help.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Help.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class HelpWindowTests
{
    private static HelpWindow OpenWindow(HelpViewModel vm)
    {
        var window = new HelpWindow { DataContext = vm, Width = 900, Height = 700 };
        Headless.Show(window);
        return window;
    }

    [Fact]
    public Task Shows_the_topic_being_read() => Headless.Run(() =>
    {
        var vm = new HelpViewModel();
        var window = OpenWindow(vm);

        Assert.Equal(vm.Current.Markdown, Headless.Find<MarkdownView>(window).Markdown);

        vm.Open("mqtt");
        Headless.Settle();
        Assert.Equal(vm.Current.Markdown, Headless.Find<MarkdownView>(window).Markdown);

        vm.SearchText = "broker";
        Headless.Settle();
        vm.SearchText = "zzzz-not-in-any-topic";
        Headless.Settle();
        window.Close();
    });

    [Fact]
    public Task Reading_a_new_topic_starts_from_its_top() => Headless.Run(() =>
    {
        var vm = new HelpViewModel();
        var window = OpenWindow(vm);
        var scroller = window.FindControl<ScrollViewer>("TopicScroller")!;
        vm.Open("mqtt");
        Headless.Settle();
        window.UpdateLayout();

        scroller.Offset = new Vector(0, 50);
        vm.Open("notes");

        Assert.Equal(default, scroller.Offset);
        window.Close();
    });

    [Fact]
    public Task The_buttons_minimise_maximise_and_close() => Headless.Run(() =>
    {
        var window = OpenWindow(new HelpViewModel());
        var maximize = window.FindControl<Button>("MaximizeButton")!;

        Headless.Click(maximize);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        Assert.Equal("Restore", ToolTip.GetTip(maximize));

        window.Show();
        Headless.Settle();
        Headless.Click(maximize);
        Assert.Equal(WindowState.Normal, window.WindowState);
        Assert.Equal("Maximise", ToolTip.GetTip(maximize));

        var closed = false;
        window.Closed += (_, _) => closed = true;
        var buttons = Headless.All<Button>(window).ToList();
        var minimize = buttons[buttons.IndexOf(maximize) - 1];
        var close = buttons[buttons.IndexOf(maximize) + 1];
        Headless.Click(minimize);
        Assert.Equal(WindowState.Minimized, window.WindowState);

        window.WindowState = WindowState.Normal;
        Headless.Settle();
        Headless.Click(close);
        Assert.True(closed);
    });

    [Fact]
    public Task Double_clicking_the_top_bar_maximises_but_not_over_its_buttons() => Headless.Run(() =>
    {
        var window = OpenWindow(new HelpViewModel());
        var point = new Point(window.Bounds.Width / 2, 12);

        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Assert.Equal(WindowState.Normal, window.WindowState);

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        window.Close();
    });

    [Fact]
    public Task There_is_one_help_window_shown_again_for_each_topic() => Headless.Run(() =>
    {
        var vm = new HelpViewModel();
        var opened = new List<HelpWindow>();
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<HelpWindow>((window, _) => opened.Add(window));

        HelpWindow.Open(vm, "mqtt");
        Headless.Settle();
        var window = Assert.Single(opened);
        Assert.Equal("mqtt", vm.Current.Topic.Id);

        window.WindowState = WindowState.Minimized;
        HelpWindow.Open(vm, "notes");
        Headless.Settle();
        Assert.Single(opened);
        Assert.Equal("notes", vm.Current.Topic.Id);
        Assert.Equal(WindowState.Normal, window.WindowState);

        HelpWindow.Open(vm, "themes");
        Assert.Single(opened);

        window.Close();
        Headless.Settle();
        HelpWindow.Open(vm, "mqtt");
        Headless.Settle();
        Assert.Equal(2, opened.Count);
        opened[1].Close();
    });

    [Fact]
    public Task A_help_link_is_followed_in_the_window_and_another_is_not() => Headless.Run(() =>
    {
        var vm = new HelpViewModel();
        var window = OpenWindow(vm);
        var markdown = Headless.Find<MarkdownView>(window);

        var help = new LinkClickedEventArgs(MarkdownView.LinkClickedEvent, new Uri("help:notes"));
        markdown.RaiseEvent(help);
        Assert.True(help.Handled);
        Assert.Equal("notes", vm.Current.Topic.Id);

        var web = new LinkClickedEventArgs(MarkdownView.LinkClickedEvent, new Uri("https://example.com"));
        markdown.RaiseEvent(web);
        Assert.False(web.Handled);
        window.Close();
    });
}
