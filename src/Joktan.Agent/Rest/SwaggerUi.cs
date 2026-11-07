using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Joktan.Agent.Rest;

/// <summary>
/// The REST API's docs page: Swagger UI (Apache 2.0, by SmartBear) reading <c>/api/openapi.json</c>.
/// Its files are fetched and their hashes checked when the app is built, and built into this assembly,
/// so the page works without the internet and the app downloads nothing.
/// </summary>
public static class SwaggerUi
{
    public const string Path = RestApi.Root + "/docs";

    private const string ResourcePrefix = "Joktan.Agent.SwaggerUi.";

    private static readonly Dictionary<string, string> Files = new()
    {
        ["swagger-ui-bundle.js"] = "text/javascript; charset=utf-8",
        ["swagger-ui.css"] = "text/css; charset=utf-8",
        ["LICENSE"] = "text/plain; charset=utf-8",
        ["NOTICE"] = "text/plain; charset=utf-8",
    };

    /// <summary>Whether this build came with Swagger UI's files.</summary>
    public static bool IsAvailable => Read("swagger-ui-bundle.js") is not null;

    /// <summary>One of Swagger UI's files and its type, or null for a name that is not one.</summary>
    public static (byte[] Content, string ContentType)? Asset(string name)
        => Files.TryGetValue(name, out var type) && Read(name) is { } content ? (content, type) : null;

    /// <summary>The page, or when this build has no Swagger UI, a page that says so and links to the document.</summary>
    public static byte[] Page()
    {
        var html = IsAvailable
            ? $$"""
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width, initial-scale=1">
                  <title>Joktan REST API</title>
                  <link rel="stylesheet" href="{{Path}}/swagger-ui.css">
                </head>
                <body>
                  <div id="swagger-ui"></div>
                  <script src="{{Path}}/swagger-ui-bundle.js"></script>
                  <script>
                    SwaggerUIBundle({
                      url: "{{RestApi.Root}}/openapi.json",
                      dom_id: "#swagger-ui",
                      docExpansion: "list",
                      filter: true,
                      tryItOutEnabled: true,
                      tagsSorter: "alpha",
                      operationsSorter: "alpha",
                    });
                  </script>
                </body>
                </html>
                """
            : $"""
                <!doctype html>
                <html lang="en">
                <head><meta charset="utf-8"><title>Joktan REST API</title></head>
                <body>
                  <p>This copy of Joktan was built without Swagger UI. The API is described in <a href="{RestApi.Root}/openapi.json">openapi.json</a>.</p>
                </body>
                </html>
                """;
        return Encoding.UTF8.GetBytes(html);
    }

    private static byte[]? Read(string name)
    {
        using var stream = typeof(SwaggerUi).Assembly.GetManifestResourceStream(ResourcePrefix + name);
        if (stream is null)
            return null;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
