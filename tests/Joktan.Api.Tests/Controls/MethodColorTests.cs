using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Api.Controls;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Api.Tests.Controls;

public class MethodColorTests
{
    [Fact]
    public Task A_method_becomes_a_class_replacing_the_last() => Headless.Run(() =>
    {
        var text = new TextBlock();

        MethodColor.SetMethod(text, "POST");
        Assert.Contains("post", text.Classes);
        Assert.Equal("POST", MethodColor.GetMethod(text));

        MethodColor.SetMethod(text, "GET");
        Assert.Contains("get", text.Classes);
        Assert.DoesNotContain("post", text.Classes);
    });

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-word")]
    public Task Anything_else_gets_no_class(string? method) => Headless.Run(() =>
    {
        var text = new TextBlock();

        MethodColor.SetMethod(text, method);

        Assert.Empty(text.Classes);
    });
}
