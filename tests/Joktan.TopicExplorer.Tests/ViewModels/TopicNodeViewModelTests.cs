using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using Joktan.Mqtt;
using Joktan.TopicExplorer.ViewModels;
using Xunit;

namespace Joktan.TopicExplorer.Tests.ViewModels;

public class TopicNodeViewModelTests
{
    private static ReceivedMessage Message(string payload = "1", bool retain = false)
        => new("a/b", Encoding.UTF8.GetBytes(payload), 0, retain, new DateTime(2026, 11, 2));

    private static TopicNodeViewModel Node(string name = "n", TopicNodeViewModel? parent = null) => new(name, name, parent);

    [Fact]
    public void An_empty_level_says_so()
    {
        Assert.Equal("(empty level)", Node("").DisplayName);
        Assert.Equal("a", Node("a").DisplayName);
    }

    [Fact]
    public void Starts_without_a_value()
    {
        var node = Node();

        Assert.False(node.HasValue);
        Assert.Equal("", node.Preview);
        Assert.False(node.IsRetained);
        Assert.Equal("", node.Info);
    }

    [Fact]
    public void Keeps_children_in_order_ignoring_case_and_after_equals()
    {
        var list = new ObservableCollection<TopicNodeViewModel>();
        var firstB = Node("b");
        var secondB = Node("B");

        foreach (var node in new[] { firstB, Node("c"), Node("A"), secondB })
            TopicNodeViewModel.Insert(list, node);

        Assert.Equal(["A", "b", "B", "c"], list.Select(n => n.Name));
        Assert.Same(firstB, list[1]);
        Assert.Same(secondB, list[2]);
    }

    [Fact]
    public void Receiving_records_the_message_newest_first()
    {
        var node = Node();
        var first = Message("1");
        var second = Message("2", retain: true);

        node.Receive(first);
        node.Receive(second);

        Assert.Equal(2, node.MessageCount);
        Assert.True(node.HasValue);
        Assert.Same(second, node.LastMessage);
        Assert.Equal([second, first], node.History);
        Assert.Equal("= 2", node.Preview);
        Assert.True(node.IsRetained);
    }

    [Fact]
    public void The_history_keeps_its_length()
    {
        var node = Node();

        foreach (var _ in Enumerable.Range(0, TopicNodeViewModel.HistoryLength + 5))
            node.Receive(Message());

        Assert.Equal(TopicNodeViewModel.HistoryLength, node.History.Count);
        Assert.Equal(TopicNodeViewModel.HistoryLength + 5, node.MessageCount);
    }

    [Fact]
    public void Everything_above_counts_the_message_and_the_topic_once()
    {
        var top = Node("top");
        var middle = Node("middle", top);
        var leaf = Node("leaf", middle);

        leaf.Receive(Message());
        leaf.Receive(Message());

        Assert.Equal((1, 2), (middle.TopicCount, middle.SubtreeMessages));
        Assert.Equal((1, 2), (top.TopicCount, top.SubtreeMessages));
        Assert.Equal(0, top.MessageCount);
        Assert.Equal(0, leaf.TopicCount);
    }

    [Fact]
    public void Describes_what_sits_below_it()
    {
        var top = Node("top");
        Assert.Equal("", top.Info);

        var first = Node("first", top);
        first.Receive(Message());
        Assert.Equal("(1 topic, 1 message)", top.Info);

        first.Receive(Message());
        Assert.Equal("(1 topic, 2 messages)", top.Info);
        Assert.Equal("(2 messages)", first.Info);

        Node("second", top).Receive(Message());
        Assert.Equal("(2 topics, 3 messages)", top.Info);
    }

    [Fact]
    public void Remembers_what_the_user_chose_to_draw()
    {
        var node = Node();
        Assert.False(node.IsChosenByUser);

        node.ChosenPaths = ["a", "b"];

        Assert.True(node.IsChosenByUser);
        Assert.Equal(["a", "b"], node.ChosenPaths!.Order());

        node.ChosenPaths = null;
        Assert.False(node.IsChosenByUser);
    }

    [Fact]
    public void Says_how_many_more_trends_there_are()
    {
        var node = Node();
        Assert.False(node.HasMoreTrends);

        node.MoreTrends = 3;

        Assert.True(node.HasMoreTrends);
        Assert.Equal("3 more not shown", node.MoreTrendsText);
    }
}
