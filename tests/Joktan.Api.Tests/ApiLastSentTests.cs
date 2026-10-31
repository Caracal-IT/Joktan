using System;
using System.IO;
using System.Linq;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Api.Tests;

[Collection(ProcessStateCollection.Name)]
public class ApiLastSentTests : DataFolderTest
{
    private static string RequestPath(params string[] parts)
        => string.Join(Path.DirectorySeparatorChar, new[] { "", "w" }.Concat(parts));

    private static ApiSentRequest Sent(string url) => new() { Time = new DateTime(2026, 11, 2), Request = new ApiRequest { Url = url } };

    [Fact]
    public void Remembers_what_was_sent_from_each_request()
    {
        ApiLastSent.Write(RequestPath("a"), Sent("first"));
        ApiLastSent.Write(RequestPath("b"), Sent("second"));
        ApiLastSent.Write(RequestPath("a"), Sent("third"));

        Assert.Equal("third", ApiLastSent.Read(RequestPath("a"))!.Request.Url);
        Assert.Equal("second", ApiLastSent.Read(RequestPath("b"))!.Request.Url);
        Assert.Null(ApiLastSent.Read(RequestPath("c")));
    }

    [Fact]
    public void An_unreadable_store_remembers_nothing()
    {
        File.WriteAllText(Data.At("api-last-sent.json"), "not json");

        Assert.Null(ApiLastSent.Read(RequestPath("a")));
    }

    [Fact]
    public void Follows_a_request_or_a_folder_that_moves()
    {
        ApiLastSent.Write(RequestPath("f", "a"), Sent("a"));
        ApiLastSent.Write(RequestPath("f", "g", "b"), Sent("b"));
        ApiLastSent.Write(RequestPath("f2", "c"), Sent("c"));

        ApiLastSent.Moved(RequestPath("f"), RequestPath("moved"));

        Assert.Equal("a", ApiLastSent.Read(RequestPath("moved", "a"))!.Request.Url);
        Assert.Equal("b", ApiLastSent.Read(RequestPath("moved", "g", "b"))!.Request.Url);
        Assert.Null(ApiLastSent.Read(RequestPath("f", "a")));
        Assert.Equal("c", ApiLastSent.Read(RequestPath("f2", "c"))!.Request.Url);
    }

    [Fact]
    public void Forgets_a_request_or_a_folder_that_was_deleted()
    {
        ApiLastSent.Write(RequestPath("f", "a"), Sent("a"));
        ApiLastSent.Write(RequestPath("f2", "c"), Sent("c"));

        ApiLastSent.Forget(RequestPath("f"));

        Assert.Null(ApiLastSent.Read(RequestPath("f", "a")));
        Assert.NotNull(ApiLastSent.Read(RequestPath("f2", "c")));
    }

    [Fact]
    public void Moving_or_forgetting_what_is_not_there_writes_nothing()
    {
        ApiLastSent.Moved(RequestPath("a"), RequestPath("b"));
        ApiLastSent.Forget(RequestPath("a"));

        Assert.False(File.Exists(Data.At("api-last-sent.json")));
    }
}

public class ApiSentRequestTests
{
    [Fact]
    public void Describes_a_REST_request_leaving_out_secrets_and_what_is_off()
    {
        var sent = new ApiSentRequest
        {
            Request = new ApiRequest
            {
                Method = "POST",
                Url = "http://x",
                Query = [new ApiPair { Name = "q", Value = "1" }, new ApiPair { Name = "off", Value = "2", Enabled = false }, new ApiPair { Name = " " }],
                Headers = [new ApiPair { Name = "h", Value = "v" }],
                AuthKind = ApiAuthKind.Bearer,
                Token = "secret",
                BodyKind = ApiBodyKind.Json,
                Body = "{}",
            },
        };

        Assert.Equal("POST http://x\nQuery: q = 1\nHeader: h = v\nAuth: Bearer\n\n{}", sent.Describe());
    }

    [Fact]
    public void Describes_a_form_by_its_fields()
    {
        var sent = new ApiSentRequest
        {
            Request = new ApiRequest { Url = "x", BodyKind = ApiBodyKind.Form, Form = [new ApiPair { Name = "f", Value = "1" }], Body = "ignored" },
        };

        Assert.Equal("GET x\nField: f = 1", sent.Describe());
    }

    [Fact]
    public void Describes_a_request_with_no_body_on_one_line()
    {
        Assert.Equal("GET x", new ApiSentRequest { Request = new ApiRequest { Url = "x", Body = "ignored" } }.Describe());
    }

    [Fact]
    public void Describes_a_gRPC_request()
    {
        var sent = new ApiSentRequest
        {
            Request = new ApiRequest
            {
                Kind = ApiKind.Grpc,
                Service = "s.S",
                Rpc = "Get",
                Address = "http://x",
                Metadata = [new ApiPair { Name = "m", Value = "v" }],
                Message = "{}",
            },
        };

        Assert.Equal("s.S/Get at http://x\nMetadata: m = v\n\n{}", sent.Describe());
    }
}
