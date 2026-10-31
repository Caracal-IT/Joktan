using Xunit;

namespace Joktan.Notes.Tests;

public class NotePageTests
{
    [Fact]
    public void A_new_page_has_an_ID_of_its_own_and_is_open()
    {
        var page = new NotePage();

        Assert.Equal(32, page.Id.Length);
        Assert.NotEqual(page.Id, new NotePage().Id);
        Assert.True(page.IsExpanded);
        Assert.Empty(page.Children);
    }
}
