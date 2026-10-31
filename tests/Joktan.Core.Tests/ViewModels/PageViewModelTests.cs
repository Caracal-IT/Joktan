using Joktan.Core.ViewModels;
using Xunit;

namespace Joktan.Core.Tests.ViewModels;

public class PageViewModelTests
{
    [Fact]
    public void A_page_keeps_its_title_and_subtitle()
    {
        var page = new PageViewModel("Title", "Sub");

        Assert.Equal("Title", page.Title);
        Assert.Equal("Sub", page.Subtitle);
    }
}
