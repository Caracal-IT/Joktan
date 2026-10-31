using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json;
using System;
using Google.Protobuf.Reflection;
using Google.Protobuf;
using Joktan.Api;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Api.Tests;

public class GrpcSchemaTests(ProtoFixture proto) : IClassFixture<ProtoFixture>
{
    [Fact]
    public void Finds_services_and_methods()
    {
        var schema = proto.Schema;

        Assert.Equal("test.Things", Assert.Single(schema.Services).FullName);
        Assert.NotNull(schema.FindService("test.Things"));
        Assert.Null(schema.FindService("test.Nothing"));
        Assert.NotNull(schema.FindMethod("test.Things", "Get"));
        Assert.Null(schema.FindMethod("test.Things", "Nothing"));
        Assert.Null(schema.FindMethod("test.Nothing", "Get"));
    }

    [Fact]
    public void Knows_a_streaming_method()
    {
        var chat = proto.Schema.FindMethod("test.Things", "Chat")!;

        Assert.True(chat.IsClientStreaming);
        Assert.True(chat.IsServerStreaming);
        Assert.False(proto.Schema.FindMethod("test.Things", "Get")!.IsClientStreaming);
    }

    [Fact]
    public void Needs_a_file()
    {
        var error = Assert.Throws<GrpcSchemaException>(() => GrpcSchema.FromProtoFiles([], []));

        Assert.Equal("Add the .proto files that describe the service.", error.Message);
    }

    [Fact]
    public void Says_when_a_file_does_not_exist()
    {
        using var folder = new TempFolder();
        var missing = folder.At("missing.proto");

        var error = Assert.Throws<GrpcSchemaException>(() => GrpcSchema.FromProtoFiles([missing], []));

        Assert.Equal($"{missing} does not exist.", error.Message);
    }

    [Fact]
    public void Reports_errors_in_a_file()
    {
        using var folder = new TempFolder();
        var bad = folder.Write("bad.proto", "syntax = \"proto3\"; message {");

        var error = Assert.Throws<GrpcSchemaException>(() => GrpcSchema.FromProtoFiles([bad], []));

        Assert.Contains("bad.proto", error.Message);
    }

    [Fact]
    public void A_file_without_services_is_no_use()
    {
        using var folder = new TempFolder();
        var file = folder.Write("m.proto", "syntax = \"proto3\"; message M { string a = 1; }");

        var error = Assert.Throws<GrpcSchemaException>(() => GrpcSchema.FromProtoFiles([file], []));

        Assert.Equal("No services were found.", error.Message);
    }

    [Fact]
    public void Looks_up_imports_beside_the_file()
    {
        using var folder = new TempFolder();
        folder.Write("types.proto", "syntax = \"proto3\"; package t; message M { string a = 1; }");
        var file = folder.Write("s.proto", """
            syntax = "proto3";
            import "types.proto";
            service S { rpc Get (t.M) returns (t.M); }
            """);

        var schema = GrpcSchema.FromProtoFiles([file], []);

        Assert.Equal("t.M", schema.FindMethod("S", "Get")!.InputType.FullName);
    }

    [Fact]
    public void Looks_up_imports_in_the_import_folders()
    {
        using var folder = new TempFolder();
        folder.Write("lib/types.proto", "syntax = \"proto3\"; package t; message M { string a = 1; }");
        var file = folder.Write("src/s.proto", """
            syntax = "proto3";
            import "types.proto";
            service S { rpc Get (t.M) returns (t.M); }
            """);

        Assert.Throws<GrpcSchemaException>(() => GrpcSchema.FromProtoFiles([file], []));
        Assert.NotNull(GrpcSchema.FromProtoFiles([file], [folder.At("lib")]).FindService("S"));
    }

    [Fact]
    public void Reads_several_files_and_lists_services_by_name()
    {
        using var folder = new TempFolder();
        var b = folder.Write("b.proto", "syntax = \"proto3\"; message B {} service Z { rpc Get (B) returns (B); }");
        var a = folder.Write("a.proto", "syntax = \"proto3\"; message A {} service Y { rpc Get (A) returns (A); }");

        var schema = GrpcSchema.FromProtoFiles([b, a], []);

        Assert.Equal(["Y", "Z"], schema.Services.Select(s => s.FullName));
    }
}
