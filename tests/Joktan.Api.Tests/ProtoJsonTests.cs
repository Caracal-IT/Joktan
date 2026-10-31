using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Api.Tests;

public class ProtoJsonTests(ProtoFixture proto) : IClassFixture<ProtoFixture>
{
    private byte[] Encode(string json) => Encode(json, proto.Everything);

    private static byte[] Encode(string json, MessageDescriptor descriptor)
    {
        using var document = JsonDocument.Parse(json);
        return ProtoJson.Encode(document.RootElement, descriptor);
    }

    private JsonNode RoundTrip(string json)
        => ProtoJson.Decode(ByteString.CopyFrom(Encode(json)), proto.Everything);

    [Theory]
    [InlineData("""{"d":1.5}""")]
    [InlineData("""{"f":2.5}""")]
    [InlineData("""{"i32":-3}""")]
    [InlineData("""{"i64":"-4"}""")]
    [InlineData("""{"u32":5}""")]
    [InlineData("""{"u64":"18446744073709551615"}""")]
    [InlineData("""{"s32":-7}""")]
    [InlineData("""{"s64":"-8"}""")]
    [InlineData("""{"fx32":9}""")]
    [InlineData("""{"fx64":"10"}""")]
    [InlineData("""{"sfx32":-11}""")]
    [InlineData("""{"sfx64":"-12"}""")]
    [InlineData("""{"flag":true}""")]
    [InlineData("""{"text":"héllo"}""")]
    [InlineData("""{"raw":"AQID"}""")]
    [InlineData("""{"color":"GREEN"}""")]
    [InlineData("""{"inner":{"name":"a","next":{"name":"b"}}}""")]
    [InlineData("""{"numbers":[1,2,3]}""")]
    [InlineData("""{"names":["a","b"]}""")]
    [InlineData("""{"counts":{"a":1,"b":2}}""")]
    [InlineData("""{"byId":{"1":{"name":"a"}}}""")]
    [InlineData("""{"at":"2026-11-02T08:30:00Z"}""")]
    [InlineData("""{"label":"x"}""")]
    [InlineData("""{"first":"x"}""")]
    [InlineData("""{"snakeName":"x"}""")]
    [InlineData("""{"inners":[{"name":"a"},{"name":"b"}]}""")]
    [InlineData("""{"colors":["GREEN","RED"]}""")]
    [InlineData("""{}""")]
    public void What_is_encoded_decodes_to_the_same(string json)
    {
        Assert.Equal(JsonNode.Parse(json)!.ToJsonString(), RoundTrip(json).ToJsonString());
    }

    [Fact]
    public void A_field_goes_by_its_proto_name_or_its_JSON_name()
    {
        Assert.Equal(RoundTrip("""{"snake_name":"x"}""").ToJsonString(), RoundTrip("""{"snakeName":"x"}""").ToJsonString());
    }

    [Fact]
    public void Numbers_can_be_written_in_other_ways()
    {
        Assert.Equal(3, RoundTrip("""{"i32":"3"}""")["i32"]!.GetValue<int>());
        Assert.Equal(1000, RoundTrip("""{"i32":1e3}""")["i32"]!.GetValue<int>());
        Assert.Equal(2, RoundTrip("""{"i32":2.0}""")["i32"]!.GetValue<int>());
        Assert.Equal("4", RoundTrip("""{"i64":4}""")["i64"]!.GetValue<string>());
        Assert.Equal(1.5, RoundTrip("""{"d":"1.5"}""")["d"]!.GetValue<double>());
        Assert.True(RoundTrip("""{"flag":"true"}""")["flag"]!.GetValue<bool>());
        Assert.Equal("GREEN", RoundTrip("""{"color":1}""")["color"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void Special_numbers_are_written_as_words(string word)
    {
        var json = $$"""{"d":"{{word}}"}""";

        Assert.Equal(word, RoundTrip(json)["d"]!.GetValue<string>());
    }

    [Fact]
    public void An_enum_number_with_no_name_stays_a_number()
    {
        Assert.Equal(9, RoundTrip("""{"color":9}""")["color"]!.GetValue<int>());
    }

    [Fact]
    public void Null_leaves_a_field_out()
    {
        Assert.Equal("{}", RoundTrip("""{"text":null,"i32":null}""").ToJsonString());
    }

    [Fact]
    public void A_value_field_can_hold_null()
    {
        Assert.Contains("null", RoundTrip("""{"value":null}""").ToJsonString());
    }

    [Theory]
    [InlineData("""{"nope":1}""", "Everything has no field nope.")]
    [InlineData("""{"i32":"abc"}""", "i32 is a whole number that fits its type; abc does not.")]
    [InlineData("""{"i32":1.5}""", "i32 is a whole number that fits its type; 1.5 does not.")]
    [InlineData("""{"i32":3000000000}""", "i32 is a whole number that fits its type; 3000000000 does not.")]
    [InlineData("""{"u32":-1}""", "u32 is a whole number that fits its type; -1 does not.")]
    [InlineData("""{"i32":true}""", "i32 is a whole number.")]
    [InlineData("""{"d":true}""", "d is a number.")]
    [InlineData("""{"d":"abc"}""", "d is a number.")]
    [InlineData("""{"flag":1}""", "flag is true or false.")]
    [InlineData("""{"flag":"maybe"}""", "flag is true or false.")]
    [InlineData("""{"text":1}""", "text is a string.")]
    [InlineData("""{"raw":"!!!"}""", "raw is bytes, written as a base64 string.")]
    [InlineData("""{"raw":1}""", "raw is bytes, written as a base64 string.")]
    [InlineData("""{"color":"BLUE"}""", "BLUE is not one of Color's values: RED, GREEN.")]
    [InlineData("""{"numbers":1}""", "Everything.numbers is repeated, written as a JSON array.")]
    [InlineData("""{"counts":1}""", "Everything.counts is a map, written as a JSON object.")]
    [InlineData("""{"inner":1}""", "A Inner is written as a JSON object.")]
    [InlineData("""{"inner":null}""", "A Inner is written as a JSON object.")]
    [InlineData("""{"any":1}""", "An Any is written as a JSON object with its type in \"@type\".")]
    [InlineData("""{"any":{"name":"a"}}""", "An Any with fields needs its type in \"@type\".")]
    [InlineData("""{"any":{"@type":"test.Inner"}}""", "The \"@type\" of an Any is written like type.googleapis.com/package.Message.")]
    [InlineData("""{"any":{"@type":1}}""", "The \"@type\" of an Any is written like type.googleapis.com/package.Message.")]
    [InlineData("""{"any":{"@type":"x/test.Nothing"}}""", "x/test.Nothing is not among the loaded services, so it cannot be put in an Any. With .proto files, add the file that defines it.")]
    [InlineData("""{"any":{"@type":"x/google.protobuf.Duration"}}""", "An Any that holds a Duration has it in \"value\".")]
    [InlineData("""{"any":{"@type":"x/test.Inner","nope":1}}""", "Inner has no field nope.")]
    public void Says_what_is_wrong_with_a_message(string json, string message)
    {
        var error = Assert.Throws<ProtoJsonException>(() => Encode(json));

        Assert.Equal(message, error.Message);
    }

    [Fact]
    public void A_message_must_be_an_object()
    {
        Assert.Throws<ProtoJsonException>(() => Encode("[]", proto.Inner));
    }

    [Fact]
    public void A_bad_well_known_value_is_reported()
    {
        var error = Assert.Throws<ProtoJsonException>(() => Encode("""{"at":"yesterday"}"""));

        Assert.StartsWith("Timestamp:", error.Message);
    }

    [Fact]
    public void Decoding_skips_fields_it_does_not_know()
    {
        using var buffer = new MemoryStream();
        var output = new CodedOutputStream(buffer);
        output.WriteTag(99, WireFormat.WireType.Varint);
        output.WriteInt32(1);
        output.WriteTag(1, WireFormat.WireType.LengthDelimited);
        output.WriteString("kept");
        output.Flush();

        var json = ProtoJson.Decode(ByteString.CopyFrom(buffer.ToArray()), proto.Inner);

        Assert.Equal("""{"name":"kept"}""", json.ToJsonString());
    }

    [Fact]
    public void Decoding_reads_packed_numbers()
    {
        using var packed = new MemoryStream();
        var packedOutput = new CodedOutputStream(packed);
        packedOutput.WriteInt32(1);
        packedOutput.WriteInt32(300);
        packedOutput.Flush();

        using var buffer = new MemoryStream();
        var output = new CodedOutputStream(buffer);
        output.WriteTag(18, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(ByteString.CopyFrom(packed.ToArray()));
        output.Flush();

        var json = ProtoJson.Decode(ByteString.CopyFrom(buffer.ToArray()), proto.Everything);

        Assert.Equal("""{"numbers":[1,300]}""", json.ToJsonString());
    }

    [Fact]
    public void The_template_has_a_sample_of_every_field()
    {
        var template = ProtoJson.Template(proto.Everything)!.AsObject();

        Assert.Equal(0, template["i32"]!.GetValue<int>());
        Assert.Equal("0", template["i64"]!.GetValue<string>());
        Assert.False(template["flag"]!.GetValue<bool>());
        Assert.Equal("", template["text"]!.GetValue<string>());
        Assert.Equal("RED", template["color"]!.GetValue<string>());
        Assert.Equal(0, template["numbers"]![0]!.GetValue<int>());
        Assert.Equal(0, template["counts"]!["key"]!.GetValue<int>());
        Assert.NotNull(template["byId"]!["0"]);
        Assert.Equal("", template["inner"]!["name"]!.GetValue<string>());
        Assert.Equal("", template["label"]!.GetValue<string>());
        Assert.EndsWith("Z", template["at"]!.GetValue<string>());
    }

    [Fact]
    public void The_template_has_one_field_of_a_oneof()
    {
        var template = ProtoJson.Template(proto.Everything)!.AsObject();

        Assert.True(template.ContainsKey("first"));
        Assert.False(template.ContainsKey("second"));
    }

    [Fact]
    public void The_template_of_a_message_that_holds_itself_stops()
    {
        var depth = 0;
        for (var node = ProtoJson.Template(proto.Inner); node?["next"] is { } next; node = next)
            depth++;

        Assert.InRange(depth, 1, 6);
    }

    [Fact]
    public void A_filled_in_template_can_be_sent()
    {
        var template = ProtoJson.Template(proto.Everything)!.AsObject();

        Assert.NotNull(Encode(template.ToJsonString()));
    }

    [Theory]
    [InlineData("""{"any":{"@type":"type.googleapis.com/test.Inner","name":"a","next":{"name":"b"}}}""")]
    [InlineData("""{"any":{"@type":"type.googleapis.com/google.protobuf.Duration","value":"1.500s"}}""")]
    [InlineData("""{"any":{"@type":"type.googleapis.com/google.protobuf.StringValue","value":"a"}}""")]
    [InlineData("""{"any":{"@type":"type.googleapis.com/google.protobuf.Empty","value":{}}}""")]
    [InlineData("""{"any":{"@type":"type.googleapis.com/google.protobuf.Any","value":{"@type":"type.googleapis.com/test.Inner","name":"a"}}}""")]
    public void An_any_comes_back_as_it_was_written(string json)
        => Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), RoundTrip(json)), RoundTrip(json).ToJsonString());

    [Fact]
    public void An_any_holds_the_message_in_protobuf_form()
    {
        var bytes = Encode("""{"any":{"@type":"type.googleapis.com/test.Inner","name":"a"}}""");

        var any = Any.Parser.ParseFrom(Holder(bytes));

        Assert.Equal("type.googleapis.com/test.Inner", any.TypeUrl);
        Assert.Equal("a", ProtoJson.Decode(any.Value, proto.Inner)["name"]!.GetValue<string>());
    }

    // What is in field 27 of an Everything, the Any.
    private static byte[] Holder(byte[] everything)
    {
        var input = new CodedInputStream(everything);
        while (input.ReadTag() is var tag and not 0)
        {
            if (WireFormat.GetTagFieldNumber(tag) == 27)
                return input.ReadBytes().ToByteArray();
            input.SkipLastField();
        }

        throw new InvalidDataException("There is no Any.");
    }

    [Fact]
    public void An_empty_any_is_sent_as_empty_and_comes_back_empty()
    {
        var json = RoundTrip("""{"any":{}}""");

        Assert.Empty(json["any"]!.AsObject());
    }

    [Fact]
    public void An_any_inside_a_repeated_field_or_map_is_followed()
    {
        var schema = Schema("""
            syntax = "proto3";
            package t;
            import "google/protobuf/any.proto";
            message Named { string name = 1; }
            message Holder { repeated google.protobuf.Any list = 1; map<string, google.protobuf.Any> map = 2; }
            service S { rpc Get (Holder) returns (Holder); }
            """);
        var holder = schema.FindMethod("t.S", "Get")!.InputType;
        const string json = """{"list":[{"@type":"x/t.Named","name":"a"}],"map":{"k":{"@type":"x/t.Named","name":"b"}}}""";

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), ProtoJson.Decode(ByteString.CopyFrom(Encode(json, holder)), holder)));
    }

    [Fact]
    public void An_answer_with_a_type_that_is_not_known_is_still_shown()
    {
        var any = new Any { TypeUrl = "x/not.Known", Value = ByteString.CopyFromUtf8("abc") };
        var buffer = new MemoryStream();
        var output = new CodedOutputStream(buffer);
        output.WriteTag(27, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(any.ToByteString());
        output.Flush();

        var json = ProtoJson.Decode(ByteString.CopyFrom(buffer.ToArray()), proto.Everything);

        Assert.Equal("x/not.Known", json["any"]!["typeUrl"]!.GetValue<string>());
        Assert.Equal("YWJj", json["any"]!["value"]!.GetValue<string>());
    }

    [Fact]
    public void An_answer_with_a_damaged_any_is_still_shown()
    {
        var any = new Any { TypeUrl = "x/test.Inner", Value = ByteString.CopyFrom(0xFF) };
        var buffer = new MemoryStream();
        var output = new CodedOutputStream(buffer);
        output.WriteTag(27, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(any.ToByteString());
        output.Flush();

        var json = ProtoJson.Decode(ByteString.CopyFrom(buffer.ToArray()), proto.Everything);

        Assert.Equal("x/test.Inner", json["any"]!["typeUrl"]!.GetValue<string>());
    }

    [Fact]
    public void An_any_can_hold_a_type_from_a_file_that_is_not_imported()
    {
        using var folder = new TempFolder();
        var main = folder.Write("main.proto", """
            syntax = "proto3";
            package t;
            import "google/protobuf/any.proto";
            message Holder { google.protobuf.Any any = 1; }
            service S { rpc Get (Holder) returns (Holder); }
            """);
        var other = folder.Write("other.proto", """
            syntax = "proto3";
            package t;
            message Apart { string name = 1; }
            """);
        var schema = GrpcSchema.FromProtoFiles([main, other], []);
        var holder = schema.FindMethod("t.S", "Get")!.InputType;
        const string json = """{"any":{"@type":"x/t.Apart","name":"a"}}""";
        using var document = JsonDocument.Parse(json);

        var bytes = ProtoJson.Encode(document.RootElement, holder, schema.Types);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), ProtoJson.Decode(ByteString.CopyFrom(bytes), holder, schema.Types)));
        Assert.Throws<ProtoJsonException>(() => ProtoJson.Encode(document.RootElement, holder));
    }

    private static GrpcSchema Schema(string text)
    {
        using var folder = new TempFolder();
        return GrpcSchema.FromProtoFiles([folder.Write("s.proto", text)], []);
    }
}
