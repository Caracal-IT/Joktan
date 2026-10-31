using Xunit;

namespace Joktan.TopicDashboards.Tests;

public class GraphColorsTests
{
    [Fact]
    public void There_is_a_name_for_every_colour()
    {
        Assert.Equal(GraphColors.Count, GraphColors.Names.Count);
    }
}
