using System.Text;
using Joktan.Agent.Rest;
using Xunit;

namespace Joktan.Agent.Tests.Rest;

public class SwaggerUiTests
{
    [Fact]
    public void Comes_with_the_build()
    {
        Assert.True(SwaggerUi.IsAvailable);
    }

    [Fact]
    public void The_page_loads_its_files_from_here_and_reads_the_document()
    {
        var page = Encoding.UTF8.GetString(SwaggerUi.Page());

        Assert.Contains("""href="/api/docs/swagger-ui.css""", page);
        Assert.Contains("""src="/api/docs/swagger-ui-bundle.js""", page);
        Assert.Contains("/api/openapi.json", page);
        Assert.DoesNotContain("https://", page);
    }

    [Theory]
    [InlineData("swagger-ui-bundle.js", "text/javascript")]
    [InlineData("swagger-ui.css", "text/css")]
    [InlineData("LICENSE", "text/plain")]
    public void Gives_its_files_with_their_types(string name, string type)
    {
        var asset = SwaggerUi.Asset(name);

        Assert.NotNull(asset);
        Assert.StartsWith(type, asset.Value.ContentType);
        Assert.NotEmpty(asset.Value.Content);
    }

    [Theory]
    [InlineData("index.html")]
    [InlineData("../Joktan.Agent.dll")]
    [InlineData("")]
    public void Gives_nothing_else(string name)
    {
        Assert.Null(SwaggerUi.Asset(name));
    }
}
