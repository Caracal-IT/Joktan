using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Core.ViewModels;
using Joktan.Settings.Backup;
using Joktan.Settings.Themes;
using Joktan.Settings.ViewModels;
using Joktan.Settings.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Settings.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class ThemePagesViewTests : DataFolderTest
{
    private static (ThemeEditor Editor, ThemeBarViewModel Bar) Editor()
    {
        var editor = ThemeEditor.Load();
        return (editor, new ThemeBarViewModel(editor));
    }

    [Fact]
    public Task The_colours_page_lists_each_colour() => Headless.Run(() =>
    {
        var (editor, bar) = Editor();
        var window = Headless.Show(new ColorsView(), new ColorsViewModel(editor, bar));
        var texts = Headless.All<TextBlock>(window).Select(t => t.Text).ToList();

        Assert.Contains(ThemeTokens.Colors[0].Label, texts);

        editor.SetColor(ThemeTokens.AccentBrush, "#123456");
        bar.Error = "An error";
        Headless.Settle();
        window.Close();
    });

    [Fact]
    public Task The_fonts_page_lists_each_role() => Headless.Run(() =>
    {
        var (editor, bar) = Editor();
        var window = Headless.Show(new FontsView(), new FontsViewModel(editor, bar));

        Assert.Contains(ThemeTokens.Fonts[0].Label, Headless.All<TextBlock>(window).Select(t => t.Text));
        window.Close();
    });

    [Fact]
    public Task The_text_sizes_page_lists_each_size() => Headless.Run(() =>
    {
        var (editor, bar) = Editor();
        var window = Headless.Show(new TextSizesView(), new TextSizesViewModel(editor, bar));

        Assert.Contains(ThemeTokens.TextSizes[0].Label, Headless.All<TextBlock>(window).Select(t => t.Text));
        window.Close();
    });

    [Fact]
    public Task The_themes_page_lists_each_theme() => Headless.Run(() =>
    {
        var (editor, bar) = Editor();
        editor.SaveAs("Mine");
        var window = Headless.Show(new ThemesView(), new ThemesViewModel(editor, bar));

        Assert.Contains("Mine", Headless.All<RadioButton>(window).Select(r => r.Content as string)
            .Concat(Headless.All<TextBlock>(window).Select(t => t.Text)));
        window.Close();
    });

    [Fact]
    public Task The_bar_says_what_is_being_edited() => Headless.Run(() =>
    {
        var (editor, bar) = Editor();
        var window = Headless.Show(new ThemeBar(), bar);
        editor.SetColor(ThemeTokens.AccentBrush, "#123456");
        Headless.Settle();

        Assert.Contains(bar.Status, Headless.All<TextBlock>(window).Select(t => t.Text));
        window.Close();
    });
}

[Collection(ProcessStateCollection.Name)]
public class MenuSettingsViewTests : DataFolderTest
{
    [Fact]
    public Task Lists_the_menu_with_what_is_below_each_entry() => Headless.Run(() =>
    {
        ObservableCollection<NavItemViewModel> menu = [new NavItemViewModel("First"), new NavItemViewModel("Group", null, new NavItemViewModel("Inner"))];
        var window = Headless.Show(new MenuSettingsView(), new MenuSettingsViewModel(menu, new MenuLayout()));
        var texts = Headless.All<TextBlock>(window).Select(t => t.Text).ToList();

        Assert.Contains("First", texts);
        Assert.Contains("Group", texts);
        window.Close();
    });
}

[Collection(ProcessStateCollection.Name)]
public class BackupViewTests : DataFolderTest
{
    private readonly List<string> _opened = [];

    private BackupViewModel Page(string notes, bool withClient = false)
    {
        IReadOnlyList<FolderSource> Sources() =>
        [
            new FolderSource
            {
                Id = "notes",
                Name = "Notes",
                Hint = "A hint",
                CurrentPath = () => notes,
                DefaultPath = notes,
                Change = _ => Task.FromResult(new FolderChange(null, "Done")),
                OpenPage = () => _opened.Add("notes"),
                OpenPageText = "Open Notes",
                Warning = "A warning",
                Item = () => new BackupItem("notes", "Notes", [new BackupRoot("folder", notes)]),
            },
            .. withClient
                ? new[]
                {
                    new FolderSource
                    {
                        Id = "client:one",
                        Name = "one",
                        IsMessageFolder = true,
                        AllowNone = true,
                        CurrentPath = () => "",
                        Change = _ => Task.FromResult(new FolderChange(null)),
                    },
                }
                : [],
        ];

        return new BackupViewModel(Sources, new BackupSettings { Folder = Data.At("backups") }, () => Task.CompletedTask, () => { }, "2.0.0");
    }

    [Fact]
    public Task Shows_the_folders_and_the_backups_there_are() => Headless.Run(async () =>
    {
        var notes = Path.GetDirectoryName(Data.Write("notes/a.md", "text"))!;
        var page = Page(notes, withClient: true);
        var window = Headless.Show(new BackupView(), page);
        Assert.Contains("Notes", Headless.All<TextBlock>(window).Select(t => t.Text));

        await page.BackUpNowCommand.ExecuteAsync(null);
        Headless.Settle();
        window.UpdateLayout();
        window.Close();
    });

    [Fact]
    public Task Being_shown_reads_the_folders_again() => Headless.Run(() =>
    {
        var notes = Path.GetDirectoryName(Data.Write("notes/a.md", "text"))!;
        var page = Page(notes);
        var refreshed = 0;
        page.Rows[0].PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FolderRowViewModel.CurrentText))
                refreshed++;
        };

        var window = Headless.Show(new BackupView(), page);

        Assert.True(refreshed > 0);
        window.Close();
    });

    [Fact]
    public Task The_pickers_give_nothing_without_a_system_dialog() => Headless.Run(async () =>
    {
        var notes = Path.GetDirectoryName(Data.Write("notes/a.md", "text"))!;
        var page = Page(notes);
        var view = new BackupView { DataContext = page };
        Assert.Null(await page.PickFolder!());
        Assert.Null(await page.PickZip!());

        var window = Headless.Show(view);
        Assert.Null(await page.PickFolder!());
        Assert.Null(await page.PickZip!());
        window.Close();
    });

    [Fact]
    public Task A_row_says_what_it_can_do() => Headless.Run(async () =>
    {
        var notes = Path.GetDirectoryName(Data.Write("notes/a.md", "text"))!;
        var page = Page(notes);
        var row = page.Rows[0];

        Assert.Equal("A hint", row.Hint);
        Assert.True(row.HasHint);
        Assert.False(row.IsFixed);
        Assert.True(row.HasDefault);
        Assert.True(row.CanOpenPage);
        Assert.Equal("Open Notes", row.OpenPageText);
        Assert.Equal(notes, row.CurrentText);
        Assert.Equal("A warning", row.Warning);
        Assert.False(row.HasMessage);
        Assert.False(row.HasProblem);

        row.OpenPageCommand.Execute(null);
        Assert.Equal(["notes"], _opened);

        await row.BrowseCommand.ExecuteAsync(null);
        page.PickFolder = () => Task.FromResult<string?>(null);
        await row.BrowseCommand.ExecuteAsync(null);
        Assert.Equal(notes, row.PathText);
        page.PickFolder = () => Task.FromResult<string?>("/picked");
        await row.BrowseCommand.ExecuteAsync(null);
        Assert.Equal("/picked", row.PathText);

        Assert.Contains(5, BackupViewModel.KeepChoices);
    });
}
