using Avalonia.Controls;
using Joktan.Core.ViewModels;
using Joktan.Tests.Samples.ViewModels;
using Joktan.Tests.Samples.Views;
using Xunit;

namespace Joktan.Tests;

public class ViewLocatorTests
{
    private readonly ViewLocator _locator = new();

    [Fact]
    public void Matches_view_models_only()
    {
        Assert.True(_locator.Match(new SampleViewModel()));
        Assert.True(_locator.Match(new PageViewModel("a", "b")));
        Assert.False(_locator.Match("text"));
        Assert.False(_locator.Match(null));
    }

    [Fact]
    public void Builds_nothing_from_nothing()
    {
        Assert.Null(_locator.Build(null));
    }

    [Fact]
    public void Finds_the_view_named_after_the_view_model()
    {
        Assert.IsType<SampleView>(_locator.Build(new SampleViewModel()));
    }

    [Fact]
    public void Says_which_view_it_did_not_find()
    {
        var text = Assert.IsType<TextBlock>(_locator.Build(new ViewlessViewModel()));

        Assert.Equal("Not Found: Joktan.Tests.Samples.Views.ViewlessView", text.Text);
    }
}
