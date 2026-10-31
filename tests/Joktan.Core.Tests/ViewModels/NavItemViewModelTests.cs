using System.Collections.Generic;
using Joktan.Core.ViewModels;
using Xunit;

namespace Joktan.Core.Tests.ViewModels;

public class NavItemViewModelTests
{
    [Fact]
    public void Children_know_their_parent()
    {
        var child = new NavItemViewModel("Child");
        var group = new NavItemViewModel("Group", null, child);

        Assert.Same(group, child.Parent);
        Assert.True(group.HasChildren);
        Assert.False(child.HasChildren);
    }

    [Fact]
    public void A_group_leaves_the_menu_when_nothing_in_it_is_shown()
    {
        var first = new NavItemViewModel("First");
        var second = new NavItemViewModel("Second");
        var group = new NavItemViewModel("Group", null, first, second);
        Assert.True(group.IsInMenu);

        first.IsShown = false;
        Assert.True(group.IsInMenu);

        second.IsShown = false;
        Assert.False(group.IsInMenu);
    }

    [Fact]
    public void A_hidden_group_is_not_in_the_menu_even_with_children_shown()
    {
        var group = new NavItemViewModel("Group", null, new NavItemViewModel("Child")) { IsShown = false };

        Assert.False(group.IsInMenu);
    }

    [Fact]
    public void Hiding_an_entry_tells_every_group_above_it()
    {
        var leaf = new NavItemViewModel("Leaf");
        var inner = new NavItemViewModel("Inner", null, leaf);
        var outer = new NavItemViewModel("Outer", null, inner);
        var changed = new List<string?>();
        outer.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        leaf.IsShown = false;

        Assert.Contains(nameof(NavItemViewModel.IsInMenu), changed);
        Assert.False(outer.IsInMenu);
    }

    [Fact]
    public void An_entry_can_be_hidden_unless_it_says_otherwise()
    {
        Assert.True(new NavItemViewModel("A").CanHide);
        Assert.False(new NavItemViewModel("B") { CanHide = false }.CanHide);
    }
}
