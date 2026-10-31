using System.Collections.ObjectModel;
using System.Linq;
using Joktan.Core.ViewModels;
using Joktan.Settings.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Settings.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class MenuSettingsViewModelTests : DataFolderTest
{
    private readonly NavItemViewModel _inner = new("Inner");
    private readonly ObservableCollection<NavItemViewModel> _menu;
    private readonly MenuSettingsViewModel _model;

    public MenuSettingsViewModelTests()
    {
        _menu = [new NavItemViewModel("A"), new NavItemViewModel("B"), new NavItemViewModel("Group", null, _inner, new NavItemViewModel("Other"))];
        _model = new MenuSettingsViewModel(_menu, new MenuLayout());
    }

    [Fact]
    public void Applies_the_saved_layout_when_it_opens()
    {
        var menu = new ObservableCollection<NavItemViewModel> { new("A"), new("B") };

        _ = new MenuSettingsViewModel(menu, new MenuLayout { Entries = [new MenuLayoutEntry { Title = "B" }, new MenuLayoutEntry { Title = "A" }] });

        Assert.Equal(["B", "A"], menu.Select(i => i.Title));
    }

    [Fact]
    public void Moving_reorders_and_saves()
    {
        _model.MoveDownCommand.Execute(_menu[0]);

        Assert.Equal(["B", "A", "Group"], _menu.Select(i => i.Title));
        Assert.Equal(["B", "A", "Group"], MenuLayout.Load().Entries!.Select(e => e.Title));
    }

    [Fact]
    public void Moving_stays_among_the_entries_beside_it()
    {
        _model.MoveDownCommand.Execute(_inner);

        Assert.Equal(["Other", "Inner"], _menu[2].Children.Select(i => i.Title));
        Assert.Equal(["A", "B", "Group"], _menu.Select(i => i.Title));
    }

    [Fact]
    public void Cannot_move_past_the_ends()
    {
        Assert.False(_model.MoveUpCommand.CanExecute(_menu[0]));
        Assert.True(_model.MoveDownCommand.CanExecute(_menu[0]));
        Assert.True(_model.MoveUpCommand.CanExecute(_menu[2]));
        Assert.False(_model.MoveDownCommand.CanExecute(_menu[2]));
        Assert.False(_model.MoveUpCommand.CanExecute(null));
        Assert.False(_model.MoveDownCommand.CanExecute(null));
    }

    [Fact]
    public void Hiding_an_entry_saves_also_within_a_group()
    {
        _inner.IsShown = false;

        Assert.False(MenuLayout.Load().Entries![2].Children![0].Shown);
    }
}
