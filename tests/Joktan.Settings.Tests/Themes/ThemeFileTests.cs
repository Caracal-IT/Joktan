using System.IO;
using Joktan.Core;
using Joktan.Settings.Themes;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Settings.Tests.Themes;

[Collection(ProcessStateCollection.Name)]
public class ThemeFileTests : DataFolderTest
{
    [Fact]
    public void With_no_file_nothing_is_chosen()
    {
        var file = ThemeFile.Load();

        Assert.Null(file.Current);
        Assert.Null(file.Themes);
    }

    [Fact]
    public void What_is_saved_is_loaded()
    {
        var theme = new Theme { Name = "Mine", IsDark = true };
        theme.Colors["AccentBrush"] = "#112233";
        theme.TextSizes["TabFontSize"] = 14.5;

        var problem = new ThemeFile { Current = "Mine", Themes = [theme] }.Save();
        var loaded = ThemeFile.Load();

        Assert.Null(problem);
        Assert.Equal("Mine", loaded.Current);
        Assert.True(loaded.Themes![0].LooksLike(theme));
    }

    [Fact]
    public void A_file_that_cannot_be_read_leaves_the_themes_as_they_come()
    {
        File.WriteAllText(Data.At("themes.json"), "not json");

        Assert.Null(ThemeFile.Load().Current);
    }

    [Fact]
    public void Says_why_it_could_not_save()
    {
        var blocker = Data.Write("file", "x");
        System.Environment.SetEnvironmentVariable(DataFolder.Variable, blocker);

        Assert.NotNull(new ThemeFile().Save());
    }
}
