using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Joktan.Mqtt.ViewModels;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

public class MessageGroupViewModelTests
{
    private static FolderMessageRowViewModel Row(string group, MessageSettings? settings = null)
        => new(
            new FolderMessage
            {
                FilePath = Path.Combine("/x", Guid.NewGuid().ToString()),
                FileName = "m.json",
                Topic = "a",
                Settings = (settings ?? MessageSettings.Default) with { Group = group },
            },
            (row, on) => row.Message.Settings = row.Message.Settings with { Enabled = on });

    private static List<MessageGroupViewModel> Build(Func<string, bool>? isExpanded, params FolderMessageRowViewModel[] rows)
        => MessageGroupViewModel.Build(rows, isExpanded ?? (_ => true), (_, _) => { });

    [Fact]
    public void Makes_the_groups_above_a_group_in_tree_order_with_ungrouped_last()
    {
        var groups = Build(null, Row("B"), Row(""), Row("A/Two"), Row("A/One/Deep"), Row("a/two"));

        Assert.Equal(["A/One", "A/One/Deep", "A/Two", "B", ""], groups.Skip(1).Select(g => g.Path));
        Assert.Equal("A", groups[0].Path);
    }

    [Fact]
    public void Groups_differing_in_case_are_one_group()
    {
        var groups = Build(null, Row("Plant"), Row("plant"));

        Assert.Equal(2, Assert.Single(groups).Rows.Count);
    }

    [Fact]
    public void Groups_know_their_place()
    {
        var groups = Build(null, Row("A/B"), Row(""));
        var (top, inner, ungrouped) = (groups[0], groups[1], groups[2]);

        Assert.Equal("A", top.DisplayName);
        Assert.Equal("B", inner.DisplayName);
        Assert.Equal(MessageGroupViewModel.UngroupedName, ungrouped.DisplayName);
        Assert.Same(top, inner.Parent);
        Assert.Equal((0, 1), (top.Depth, inner.Depth));
        Assert.Equal(20, inner.Indent.Left);
        Assert.Contains(inner, top.Children);
    }

    [Fact]
    public void All_rows_include_those_of_sub_groups()
    {
        var groups = Build(null, Row("A"), Row("A/B"), Row("A/B/C"));

        Assert.Equal(3, groups[0].AllRows.Count());
        Assert.Single(groups[0].Rows);
    }

    [Fact]
    public void A_group_shows_while_every_group_it_is_inside_is_expanded()
    {
        var groups = Build(path => path != "A", Row("A/B/C"));
        var (a, b, c) = (groups[0], groups[1], groups[2]);

        Assert.True(a.IsVisible);
        Assert.False(b.IsVisible);
        Assert.False(c.IsVisible);
    }
}
