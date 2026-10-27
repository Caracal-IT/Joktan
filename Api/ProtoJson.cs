using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Type = Google.Protobuf.Reflection.FieldType;

namespace Joktan.Api;

/// <summary>
/// Turns JSON into protobuf messages and back, for any message described at run time, following
/// the protobuf JSON mapping: fields by their JSON or proto name, 64-bit integers and bytes as
/// strings, enums by name. The well-known types (Timestamp, Duration, wrappers, Struct and so on)
/// use their own JSON forms.
/// </summary>
public static class ProtoJson
{
    // The generated well-known types, by full name, which know their own JSON form.
    private static readonly Dictionary<string, MessageDescriptor> WellKnown = new IMessage[]
        {
            new Timestamp(), new Duration(), new Empty(), new FieldMask(), new Struct(), new Value(), new ListValue(),
            new DoubleValue(), new FloatValue(), new Int64Value(), new UInt64Value(), new Int32Value(),
            new UInt32Value(), new BoolValue(), new StringValue(), new BytesValue(),
        }
        .ToDictionary(m => m.Descriptor.FullName, m => m.Descriptor);

    public static byte[] Encode(JsonElement json, MessageDescriptor descriptor)
    {
        if (WellKnown.TryGetValue(descriptor.FullName, out var known))
        {
            try
            {
                return JsonParser.Default.Parse(json.GetRawText(), known).ToByteArray();
            }
            catch (InvalidProtocolBufferException e)
            {
                throw new ProtoJsonException($"{descriptor.Name}: {e.Message}");
            }
        }

        if (descriptor.FullName == "google.protobuf.Any")
            throw new ProtoJsonException("Fields of type google.protobuf.Any cannot be sent yet.");
        if (json.ValueKind != JsonValueKind.Object)
            throw new ProtoJsonException($"A {descriptor.Name} is written as a JSON object.");

        using var buffer = new System.IO.MemoryStream();
        var output = new CodedOutputStream(buffer);
        foreach (var property in json.EnumerateObject())
        {
            var field = descriptor.FindFieldByName(property.Name)
                        ?? descriptor.Fields.InDeclarationOrder().FirstOrDefault(f => f.JsonName == property.Name)
                        ?? throw new ProtoJsonException($"{descriptor.Name} has no field {property.Name}.");
            var value = property.Value;
            if (value.ValueKind == JsonValueKind.Null && field.FieldType != Type.Message)
                continue;

            if (field.IsMap)
            {
                if (value.ValueKind != JsonValueKind.Object)
                    throw new ProtoJsonException($"{descriptor.Name}.{field.Name} is a map, written as a JSON object.");
                var entry = field.MessageType;
                foreach (var pair in value.EnumerateObject())
                {
                    using var entryBuffer = new System.IO.MemoryStream();
                    var entryOutput = new CodedOutputStream(entryBuffer);
                    WriteField(entryOutput, entry.FindFieldByNumber(1), KeyElement(pair.Name));
                    WriteField(entryOutput, entry.FindFieldByNumber(2), pair.Value);
                    entryOutput.Flush();
                    output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                    output.WriteBytes(ByteString.CopyFrom(entryBuffer.ToArray()));
                }
            }
            else if (field.IsRepeated)
            {
                if (value.ValueKind != JsonValueKind.Array)
                    throw new ProtoJsonException($"{descriptor.Name}.{field.Name} is repeated, written as a JSON array.");
                foreach (var item in value.EnumerateArray())
                    WriteField(output, field, item);
            }
            else
            {
                WriteField(output, field, value);
            }
        }

        output.Flush();
        return buffer.ToArray();
    }

    public static JsonNode Decode(ByteString data, MessageDescriptor descriptor)
    {
        if (WellKnown.TryGetValue(descriptor.FullName, out var known))
            return JsonNode.Parse(JsonFormatter.Default.Format(known.Parser.ParseFrom(data)))!;

        var json = new JsonObject();
        var input = new CodedInputStream(data.ToByteArray());
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            var field = descriptor.FindFieldByNumber(WireFormat.GetTagFieldNumber(tag));
            if (field is null || field.FieldType == Type.Group)
            {
                input.SkipLastField();
                continue;
            }

            if (field.IsMap)
            {
                var entry = (JsonObject)Decode(input.ReadBytes(), field.MessageType);
                var map = json[field.JsonName] as JsonObject ?? new JsonObject();
                json[field.JsonName] = map;
                var key = entry[field.MessageType.FindFieldByNumber(1).JsonName]?.ToString() ?? "";
                map[key] = entry[field.MessageType.FindFieldByNumber(2).JsonName]?.DeepClone();
            }
            else if (field.IsRepeated)
            {
                var array = json[field.JsonName] as JsonArray ?? new JsonArray();
                json[field.JsonName] = array;
                if (WireFormat.GetTagWireType(tag) == WireFormat.WireType.LengthDelimited && IsPackable(field))
                {
                    var packed = new CodedInputStream(input.ReadBytes().ToByteArray());
                    while (!packed.IsAtEnd)
                        array.Add(ReadValue(packed, field));
                }
                else
                {
                    array.Add(ReadValue(input, field));
                }
            }
            else
            {
                json[field.JsonName] = ReadValue(input, field);
            }
        }

        return json;
    }

    /// <summary>An example message with every field filled in, to start from.</summary>
    public static JsonNode? Template(MessageDescriptor descriptor, int depth = 0)
    {
        switch (descriptor.FullName)
        {
            case "google.protobuf.Timestamp":
                return DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            case "google.protobuf.Duration":
                return "1s";
            case "google.protobuf.Empty" or "google.protobuf.Struct":
                return new JsonObject();
            case "google.protobuf.Value":
                return null;
            case "google.protobuf.ListValue":
                return new JsonArray();
            case "google.protobuf.FieldMask":
                return "";
        }

        if (descriptor.FullName.StartsWith("google.protobuf.", StringComparison.Ordinal) && descriptor.Name.EndsWith("Value", StringComparison.Ordinal))
            return Sample(descriptor.FindFieldByNumber(1), depth);

        var json = new JsonObject();
        if (depth > 4)
            return json;
        var oneofs = new HashSet<OneofDescriptor>();
        foreach (var field in descriptor.Fields.InDeclarationOrder())
        {
            // One field of each oneof, since only one can be set.
            if (field.RealContainingOneof is { } oneof && !oneofs.Add(oneof))
                continue;
            json[field.JsonName] = field.IsMap
                ? new JsonObject { ["key"] = Sample(field.MessageType.FindFieldByNumber(2), depth) }
                : field.IsRepeated
                    ? new JsonArray(Sample(field, depth))
                    : Sample(field, depth);
        }

        return json;
    }

    private static JsonNode? Sample(FieldDescriptor field, int depth) => field.FieldType switch
    {
        Type.String => "",
        Type.Bytes => "",
        Type.Bool => false,
        Type.Int64 or Type.UInt64 or Type.Fixed64 or Type.SFixed64 or Type.SInt64 => "0",
        Type.Double or Type.Float or Type.Int32 or Type.UInt32 or Type.Fixed32 or Type.SFixed32 or Type.SInt32 => 0,
        Type.Enum => field.EnumType.Values.FirstOrDefault()?.Name ?? "0",
        Type.Message => Template(field.MessageType, depth + 1),
        _ => null,
    };

    private static void WriteField(CodedOutputStream output, FieldDescriptor field, JsonElement value)
    {
        var name = field.Name;
        switch (field.FieldType)
        {
            case Type.Double:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed64);
                output.WriteDouble(Floating(value, name));
                break;
            case Type.Float:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed32);
                output.WriteFloat((float)Floating(value, name));
                break;
            case Type.Int64:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                output.WriteInt64(Integer<long>(value, name));
                break;
            case Type.UInt64:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                output.WriteUInt64(Integer<ulong>(value, name));
                break;
            case Type.Int32:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                output.WriteInt32(Integer<int>(value, name));
                break;
            case Type.UInt32:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                output.WriteUInt32(Integer<uint>(value, name));
                break;
            case Type.Fixed64:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed64);
                output.WriteFixed64(Integer<ulong>(value, name));
                break;
            case Type.Fixed32:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed32);
                output.WriteFixed32(Integer<uint>(value, name));
                break;
            case Type.SFixed64:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed64);
                output.WriteSFixed64(Integer<long>(value, name));
                break;
            case Type.SFixed32:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Fixed32);
                output.WriteSFixed32(Integer<int>(value, name));
                break;
            case Type.SInt64:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                output.WriteSInt64(Integer<long>(value, name));
                break;
            case Type.SInt32:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                output.WriteSInt32(Integer<int>(value, name));
                break;
            case Type.Bool:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                output.WriteBool(value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
                    _ => throw new ProtoJsonException($"{name} is true or false."),
                });
                break;
            case Type.String:
                if (value.ValueKind != JsonValueKind.String)
                    throw new ProtoJsonException($"{name} is a string.");
                output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                output.WriteString(value.GetString()!);
                break;
            case Type.Bytes:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                try
                {
                    output.WriteBytes(ByteString.FromBase64(value.GetString() ?? ""));
                }
                catch (Exception e) when (e is FormatException or InvalidOperationException)
                {
                    throw new ProtoJsonException($"{name} is bytes, written as a base64 string.");
                }

                break;
            case Type.Enum:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.Varint);
                output.WriteEnum(value.ValueKind == JsonValueKind.String
                    ? field.EnumType.FindValueByName(value.GetString()!)?.Number
                      ?? throw new ProtoJsonException($"{value.GetString()} is not one of {field.EnumType.Name}'s values: "
                                                      + string.Join(", ", field.EnumType.Values.Select(v => v.Name)) + ".")
                    : Integer<int>(value, name));
                break;
            case Type.Message:
                output.WriteTag(field.FieldNumber, WireFormat.WireType.LengthDelimited);
                output.WriteBytes(ByteString.CopyFrom(Encode(value, field.MessageType)));
                break;
            default:
                throw new ProtoJsonException($"{name} is a group, which cannot be sent.");
        }
    }

    private static JsonNode? ReadValue(CodedInputStream input, FieldDescriptor field) => field.FieldType switch
    {
        Type.Double => Floating(input.ReadDouble()),
        Type.Float => Floating(input.ReadFloat()),
        Type.Int64 => input.ReadInt64().ToString(CultureInfo.InvariantCulture),
        Type.UInt64 => input.ReadUInt64().ToString(CultureInfo.InvariantCulture),
        Type.Fixed64 => input.ReadFixed64().ToString(CultureInfo.InvariantCulture),
        Type.SFixed64 => input.ReadSFixed64().ToString(CultureInfo.InvariantCulture),
        Type.SInt64 => input.ReadSInt64().ToString(CultureInfo.InvariantCulture),
        Type.Int32 => input.ReadInt32(),
        Type.UInt32 => input.ReadUInt32(),
        Type.Fixed32 => input.ReadFixed32(),
        Type.SFixed32 => input.ReadSFixed32(),
        Type.SInt32 => input.ReadSInt32(),
        Type.Bool => input.ReadBool(),
        Type.String => input.ReadString(),
        Type.Bytes => input.ReadBytes().ToBase64(),
        Type.Enum => input.ReadEnum() is var number && field.EnumType.FindValueByNumber(number) is { } known
            ? known.Name
            : number,
        Type.Message => Decode(input.ReadBytes(), field.MessageType),
        _ => null,
    };

    private static JsonNode Floating(double value)
        => double.IsNaN(value) ? "NaN"
            : double.IsPositiveInfinity(value) ? "Infinity"
            : double.IsNegativeInfinity(value) ? "-Infinity"
            : JsonValue.Create(value);

    private static bool IsPackable(FieldDescriptor field)
        => field.FieldType is not (Type.String or Type.Bytes or Type.Message or Type.Group);

    private static double Floating(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Number)
            return value.GetDouble();
        if (value.ValueKind == JsonValueKind.String)
        {
            switch (value.GetString())
            {
                case "NaN": return double.NaN;
                case "Infinity": return double.PositiveInfinity;
                case "-Infinity": return double.NegativeInfinity;
            }

            if (double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }

        throw new ProtoJsonException($"{name} is a number.");
    }

    // Integers may be JSON numbers or strings, since 64-bit values do not fit in a JSON number.
    private static T Integer<T>(JsonElement value, string name) where T : struct, INumber<T>
    {
        var text = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.String => value.GetString()!.Trim(),
            _ => throw new ProtoJsonException($"{name} is a whole number."),
        };
        if (T.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        // 1.0 and 1e3 are whole numbers too.
        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number == decimal.Truncate(number))
        {
            try
            {
                return T.CreateChecked(number);
            }
            catch (OverflowException)
            {
            }
        }

        throw new ProtoJsonException($"{name} is a whole number that fits its type; {text} does not.");
    }

    // Map keys are always JSON strings; a number or bool key is read back from the string.
    private static JsonElement KeyElement(string key) => JsonSerializer.SerializeToElement(key);
}

public sealed class ProtoJsonException(string message) : Exception(message);
