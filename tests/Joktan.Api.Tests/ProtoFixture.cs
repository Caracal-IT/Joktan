using System;
using Google.Protobuf.Reflection;
using Joktan.Tests.TestSupport;

namespace Joktan.Api.Tests;

public sealed class ProtoFixture : IDisposable
{
    private const string Proto = """
        syntax = "proto3";
        package test;

        import "google/protobuf/timestamp.proto";
        import "google/protobuf/wrappers.proto";
        import "google/protobuf/any.proto";
        import "google/protobuf/struct.proto";

        enum Color { RED = 0; GREEN = 1; }

        message Inner { string name = 1; Inner next = 2; }

        message Everything {
          double d = 1;
          float f = 2;
          int32 i32 = 3;
          int64 i64 = 4;
          uint32 u32 = 5;
          uint64 u64 = 6;
          sint32 s32 = 7;
          sint64 s64 = 8;
          fixed32 fx32 = 9;
          fixed64 fx64 = 10;
          sfixed32 sfx32 = 11;
          sfixed64 sfx64 = 12;
          bool flag = 13;
          string text = 14;
          bytes raw = 15;
          Color color = 16;
          Inner inner = 17;
          repeated int32 numbers = 18;
          repeated string names = 19;
          map<string, int32> counts = 20;
          map<int32, Inner> by_id = 21;
          google.protobuf.Timestamp at = 22;
          google.protobuf.StringValue label = 23;
          oneof choice { string first = 24; string second = 25; }
          string snake_name = 26;
          google.protobuf.Any any = 27;
          google.protobuf.Value value = 28;
          repeated Inner inners = 29;
          repeated Color colors = 30;
        }

        service Things {
          rpc Get (Everything) returns (Inner);
          rpc Chat (stream Inner) returns (stream Inner);
        }
        """;

    private readonly TempFolder _folder = new();

    public ProtoFixture()
    {
        var path = _folder.Write("things.proto", Proto);
        Schema = GrpcSchema.FromProtoFiles([path], []);
        Everything = Schema.FindMethod("test.Things", "Get")!.InputType;
        Inner = Schema.FindMethod("test.Things", "Get")!.OutputType;
    }

    public GrpcSchema Schema { get; }

    public MessageDescriptor Everything { get; }

    public MessageDescriptor Inner { get; }

    public void Dispose() => _folder.Dispose();
}
