using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Joktan.Settings.Themes;
using Joktan.Settings.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Settings.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class ColorsViewModelTests : DataFolderTest
{
    private static (ColorsViewModel Page, ThemeEditor Editor, ColorRowViewModel Row) New()
    {
        var editor = ThemeEditor.Load();
        var page = new ColorsViewModel(editor, new ThemeBarViewModel(editor));
        return (page, editor, page.Groups.SelectMany(g => g.Rows).First());
    }

    [Fact]
    public Task Every_colour_is_listed_in_its_group() => Headless.Run(() =>
    {
        var (page, editor, row) = New();

        Assert.Equal(ThemeTokens.Colors.Count, page.Groups.Sum(g => g.Rows.Count));
        Assert.Equal(ThemeTokens.Colors[0].Label, row.Label);
        Assert.Equal(editor.Draft.Colors[ThemeTokens.Colors[0].Key], row.Hex);
        Assert.False(row.IsInvalid);
        Assert.Equal(editor.Draft.IsDark, page.IsDark);
        Assert.NotNull(page.Bar);
    });

    [Fact]
    public Task Picking_a_colour_changes_the_text_and_the_theme() => Headless.Run(() =>
    {
        var (_, editor, row) = New();

        row.Color = Color.Parse("#112233");

        Assert.Equal("#112233", row.Hex);
        Assert.Equal("#112233", editor.Draft.Colors[ThemeTokens.Colors[0].Key]);
        Assert.True(editor.IsDirty);
    });

    [Fact]
    public Task Typing_a_colour_changes_the_picker_and_keeps_what_was_typed() => Headless.Run(() =>
    {
        var (_, editor, row) = New();

        row.Hex = "#abc";

        Assert.Equal("#abc", row.Hex);
        Assert.Equal(Color.Parse("#AABBCC"), row.Color);
        Assert.Equal("#AABBCC", editor.Draft.Colors[ThemeTokens.Colors[0].Key]);
        Assert.False(row.IsInvalid);
    });

    [Fact]
    public Task Text_that_is_not_a_colour_is_marked_and_goes_no_further() => Headless.Run(() =>
    {
        var (_, editor, row) = New();
        var before = editor.Draft.Colors[ThemeTokens.Colors[0].Key];

        row.Hex = "nonsense";

        Assert.True(row.IsInvalid);
        Assert.Equal(before, editor.Draft.Colors[ThemeTokens.Colors[0].Key]);

        row.Color = Color.Parse("#112233");
        Assert.False(row.IsInvalid);
    });

    [Fact]
    public Task Another_theme_refreshes_the_rows() => Headless.Run(() =>
    {
        var (page, editor, row) = New();
        row.Hex = "nonsense";
        editor.Discard();

        editor.Choose(BuiltInThemes.DarkName);

        Assert.False(row.IsInvalid);
        Assert.Equal(editor.Draft.Colors[ThemeTokens.Colors[0].Key], row.Hex);
        Assert.True(page.IsDark);
    });

    [Fact]
    public Task The_dark_switch_changes_the_theme_only_when_it_differs() => Headless.Run(() =>
    {
        var (page, editor, _) = New();
        editor.Choose(BuiltInThemes.LightName);
        Assert.False(page.IsDark);

        page.IsDark = true;

        Assert.True(editor.Draft.IsDark);
        Assert.True(editor.IsDirty);
    });
}

[Collection(ProcessStateCollection.Name)]
public class FontsViewModelTests : DataFolderTest
{
    private static (FontsViewModel Page, ThemeEditor Editor) New()
    {
        var editor = ThemeEditor.Load();
        return (new FontsViewModel(editor, new ThemeBarViewModel(editor)), editor);
    }

    [Fact]
    public Task Each_role_starts_on_its_default() => Headless.Run(() =>
    {
        var (page, _) = New();

        Assert.Equal(ThemeTokens.Fonts.Count, page.Rows.Count);
        Assert.All(page.Rows, row => Assert.Equal("", row.Selected!.Name));
        Assert.NotNull(page.Bar);
        var byKey = ThemeTokens.Fonts.Select((token, i) => (token.Key, Row: page.Rows[i])).ToDictionary(x => x.Key, x => x.Row);
        Assert.Equal("Inter (comes with Joktan)", byKey[ThemeTokens.InterfaceFont].Selected!.ToString());
        Assert.Equal("The system's monospaced font", byKey[ThemeTokens.CodeFont].Selected!.Label);
        Assert.Contains(page.Rows, r => r.Selected!.Label == "Same as the interface");
        Assert.NotEmpty(page.Rows[0].Label);
    });

    [Fact]
    public Task Choosing_a_font_changes_the_theme_and_every_sample() => Headless.Run(() =>
    {
        var (page, editor) = New();
        editor.Apply();
        var row = page.Rows[0];
        var other = page.Rows[1];
        var changed = 0;
        other.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FontRowViewModel.Sample))
                changed++;
        };

        row.Selected = new FontChoice("Some Font", "Some Font");

        Assert.Equal("Some Font", editor.Draft.Fonts[ThemeTokens.Fonts[0].Key]);
        Assert.Equal(1, changed);
        Assert.Contains("Some Font", row.Sample.Name);

        row.Selected = null;
        Assert.Equal("Some Font", editor.Draft.Fonts[ThemeTokens.Fonts[0].Key]);
    });

    [Fact]
    public Task A_font_this_machine_does_not_have_is_kept_and_marked() => Headless.Run(() =>
    {
        var (page, editor) = New();
        var row = page.Rows[0];
        var usual = row.Choices.Count;
        editor.SetFont(ThemeTokens.Fonts[0].Key, "Not On This Machine");
        editor.SaveAs("Mine");

        row.Refresh();

        Assert.Equal(usual + 1, row.Choices.Count);
        Assert.Equal("Not On This Machine (not installed)", row.Selected!.Label);

        // Another theme takes the extra choice away again.
        editor.Choose(BuiltInThemes.DarkName);
        Assert.Equal(usual, row.Choices.Count);
        Assert.Equal("", row.Selected!.Name);
    });

    [Fact]
    public Task A_role_with_no_font_in_the_resources_samples_the_default() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        var row = new FontRowViewModel(new ThemeToken("NoSuchFontResource", "Label", "Group"), [], editor);

        Assert.Equal(FontFamily.Default, row.Sample);
    });
}

[Collection(ProcessStateCollection.Name)]
public class TextSizesViewModelTests : DataFolderTest
{
    private static (TextSizesViewModel Page, ThemeEditor Editor, TextSizeRowViewModel Row) New()
    {
        var editor = ThemeEditor.Load();
        var page = new TextSizesViewModel(editor, new ThemeBarViewModel(editor));
        return (page, editor, page.Groups.SelectMany(g => g.Rows).First());
    }

    [Fact]
    public Task Every_size_is_listed_with_its_limits() => Headless.Run(() =>
    {
        var (page, editor, row) = New();

        Assert.Equal(ThemeTokens.TextSizes.Count, page.Groups.Sum(g => g.Rows.Count));
        Assert.Equal((decimal)editor.Draft.TextSizes[ThemeTokens.TextSizes[0].Key], row.Size);
        Assert.Equal((decimal)Theme.MinTextSize, row.MinSize);
        Assert.Equal((decimal)Theme.MaxTextSize, row.MaxSize);
        Assert.Equal(ThemeTokens.TextSizes[0].Label, row.Label);
        Assert.NotNull(page.Bar);
    });

    [Fact]
    public Task A_new_size_changes_the_theme_and_a_cleared_box_does_not() => Headless.Run(() =>
    {
        var (_, editor, row) = New();
        var key = ThemeTokens.TextSizes[0].Key;

        row.Size = 17;
        Assert.Equal(17, editor.Draft.TextSizes[key]);
        Assert.Equal(17, row.SampleSize);

        row.Size = null;
        Assert.Equal(17, editor.Draft.TextSizes[key]);
    });

    [Fact]
    public Task Another_theme_refreshes_the_rows() => Headless.Run(() =>
    {
        var (_, editor, row) = New();
        row.Size = 17;
        editor.Discard();

        Assert.Equal((decimal)editor.Draft.TextSizes[ThemeTokens.TextSizes[0].Key], row.Size);
    });
}

[Collection(ProcessStateCollection.Name)]
public class ThemeBarViewModelTests : DataFolderTest
{
    [Fact]
    public Task Says_what_is_being_edited() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        var bar = new ThemeBarViewModel(editor);
        Assert.Same(editor, bar.Editor);
        Assert.EndsWith("(following the system)", bar.Status);
        Assert.False(bar.ShowBuiltInHint);

        editor.Choose(BuiltInThemes.DarkName);
        Assert.Equal(BuiltInThemes.DarkName, bar.Status);

        editor.SetColor(ThemeTokens.AccentBrush, "#123456");
        Assert.Equal($"{BuiltInThemes.DarkName}: changes not saved", bar.Status);
        Assert.True(bar.ShowBuiltInHint);
    });

    [Fact]
    public Task Save_needs_changes_to_a_theme_that_can_be_overwritten() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        var bar = new ThemeBarViewModel(editor);
        Assert.False(bar.SaveCommand.CanExecute(null));
        Assert.False(bar.DiscardCommand.CanExecute(null));

        editor.SetColor(ThemeTokens.AccentBrush, "#123456");
        Assert.False(bar.SaveCommand.CanExecute(null));
        Assert.True(bar.DiscardCommand.CanExecute(null));

        bar.DiscardCommand.Execute(null);
        Assert.False(editor.IsDirty);

        editor.SaveAs("Mine");
        editor.SetColor(ThemeTokens.AccentBrush, "#654321");
        Assert.True(bar.SaveCommand.CanExecute(null));

        bar.SaveCommand.Execute(null);
        Assert.False(editor.IsDirty);
        Assert.Null(bar.Error);
    });

    [Fact]
    public Task Saving_as_new_needs_a_free_name() => Headless.Run(() =>
    {
        var editor = ThemeEditor.Load();
        var bar = new ThemeBarViewModel(editor);
        Assert.False(bar.SaveAsNewCommand.CanExecute(null));

        bar.NewName = BuiltInThemes.DarkName;
        Assert.True(bar.SaveAsNewCommand.CanExecute(null));
        bar.SaveAsNewCommand.Execute(null);
        Assert.NotNull(bar.Error);
        Assert.Equal(BuiltInThemes.DarkName, bar.NewName);

        bar.NewName = "Mine";
        bar.SaveAsNewCommand.Execute(null);
        Assert.Null(bar.Error);
        Assert.Equal("", bar.NewName);
        Assert.Equal("Mine", editor.Current);
    });
}

[Collection(ProcessStateCollection.Name)]
public class ThemesViewModelTests : DataFolderTest
{
    private static (ThemesViewModel Page, ThemeEditor Editor) New()
    {
        var editor = ThemeEditor.Load();
        return (new ThemesViewModel(editor, new ThemeBarViewModel(editor)), editor);
    }

    [Fact]
    public Task Lists_following_the_system_and_every_theme() => Headless.Run(() =>
    {
        var (page, _) = New();

        Assert.Equal(ThemeEditor.FollowSystem, page.Rows[0].Name);
        Assert.Equal(4, page.Rows.Count);
        Assert.True(page.Rows[0].IsCurrent);
        Assert.False(page.Rows[1].IsCurrent);
        Assert.All(page.Rows, r =>
        {
            Assert.False(r.CanDelete);
            Assert.Equal("Built in", r.Kind);
        });
        Assert.True(page.CanChoose);
    });

    [Fact]
    public Task Marking_a_row_chooses_its_theme() => Headless.Run(() =>
    {
        var (page, editor) = New();

        page.Rows[2].IsCurrent = true;

        Assert.Equal(page.Rows[2].Name, editor.Current);
        Assert.False(page.Rows[0].IsCurrent);
    });

    [Fact]
    public Task With_changes_to_save_no_other_theme_can_be_chosen() => Headless.Run(() =>
    {
        var (page, editor) = New();

        editor.SetColor(ThemeTokens.AccentBrush, "#123456");
        Assert.False(page.CanChoose);

        page.Rows[2].IsCurrent = true;
        Assert.Equal(ThemeEditor.FollowSystem, editor.Current);
        Assert.False(page.Rows[2].IsCurrent);
    });

    [Fact]
    public Task A_saved_theme_appears_and_can_be_deleted_but_a_built_in_one_cannot() => Headless.Run(() =>
    {
        var (page, editor) = New();
        editor.SaveAs("Mine");
        var mine = page.Rows.Single(r => r.Name == "Mine");
        Assert.True(mine.CanDelete);
        Assert.Equal("Saved", mine.Kind);
        Assert.True(mine.IsCurrent);

        page.Rows[0].DeleteCommand.Execute(null);
        Assert.Equal(5, page.Rows.Count);

        mine.DeleteCommand.Execute(null);

        Assert.DoesNotContain(page.Rows, r => r.Name == "Mine");
        Assert.Equal(ThemeEditor.FollowSystem, editor.Current);
    });
}
