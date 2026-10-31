using System;
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
    /// gzip after all, or when it would unzip to more than <see cref="MaxUnzippedLength"/> bytes.
    /// </summary>
    public static byte[]? TryDecompress(byte[] data)
    {
        if (!LooksZipped(data))
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

            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
