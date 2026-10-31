using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Joktan.Mqtt;

/// <summary>
/// Gzip for message payloads. MQTT has no header that says a payload is compressed, so a received
/// payload is recognised by the two bytes every gzip stream starts with.
/// </summary>
public static class Gzip
{
    /// <summary>The most a received payload is unzipped to, so that a small message cannot fill the memory.</summary>
    public const int MaxUnzippedLength = 16 * 1024 * 1024;

    // A header of 10 bytes, an empty body of 2 and a trailer of 8.
    private const int MinLength = 20;

    public static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            gzip.Write(data);
        return output.ToArray();
    }

    public static bool LooksZipped(byte[] data) => data is [0x1f, 0x8b, ..];

    /// <summary>
    /// Unzips a payload that starts like gzip. Returns null when it does not, when it is not valid
    /// gzip after all, when it was cut off, or when it would unzip to more than
    /// <see cref="MaxUnzippedLength"/> bytes.
    /// </summary>
    public static byte[]? TryDecompress(byte[] data)
    {
        if (!LooksZipped(data) || data.Length < MinLength)
            return null;

        try
        {
            using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = gzip.Read(buffer)) > 0)
            {
                if (output.Length + read > MaxUnzippedLength)
                    return null;
                output.Write(buffer, 0, read);
            }

            // GZipStream stops quietly at the end of a stream that was cut off. A whole one ends with its
            // own length, which what was cut off does not. Several streams joined together are not expected.
            var expected = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(data.Length - 4));
            return (uint)output.Length == expected ? output.ToArray() : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
