using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Core.ViewModels;
using Joktan.Core.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Core.Tests.Views;

public class PageViewTests
{
    [Fact]
    public Task Shows_the_title_and_subtitle_of_the_page() => Headless.Run(() =>
    {
        var window = Headless.Show(new PageView(), new PageViewModel("A title", "A subtitle"));
        var texts = Headless.All<TextBlock>(window).Select(t => t.Text).ToList();

        Assert.Contains("A title", texts);
        Assert.Contains("A subtitle", texts);
        window.Close();
    });
}
