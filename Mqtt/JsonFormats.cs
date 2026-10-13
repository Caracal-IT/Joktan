using System.Text.Encodings.Web;
using System.Text.Json;

namespace Joktan.Mqtt;

internal static class JsonFormats
{
    // Relaxed escaping keeps non-ASCII text readable instead of writing \uXXXX.
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static readonly JsonSerializerOptions Compact = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
