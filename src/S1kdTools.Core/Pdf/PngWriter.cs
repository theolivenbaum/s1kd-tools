using System.Buffers.Binary;
using System.IO.Compression;

namespace S1kdTools.Pdf;

/// <summary>
/// Writes 8-bit greyscale and 24-bit RGB PNGs. A PNG is four chunks around a zlib
/// stream, and <see cref="ZLibStream"/> is in the BCL, so the diff images cost no
/// dependency and no native code.
/// </summary>
public static class PngWriter
{
    public static void WriteGray(string path, int width, int height, byte[] pixels) =>
        Write(path, width, height, pixels, channels: 1, colourType: 0);

    public static void WriteRgb(string path, int width, int height, byte[] pixels) =>
        Write(path, width, height, pixels, channels: 3, colourType: 2);

    private static void Write(string path, int width, int height, byte[] pixels, int channels, byte colourType)
    {
        int stride = width * channels;
        // Every scanline is prefixed with its filter type; 0 (None) keeps this simple and
        // the images small enough — they are diagnostics, not deliverables.
        var raw = new byte[(stride + 1) * height];
        for (int y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = 0;
            Array.Copy(pixels, y * stride, raw, (y * (stride + 1)) + 1, stride);
        }

        using var deflated = new MemoryStream();
        using (var z = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
        {
            z.Write(raw, 0, raw.Length);
        }

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var file = File.Create(path);
        file.Write(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A });

        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8;              // bit depth
        ihdr[9] = colourType;
        ihdr[10] = 0;             // deflate
        ihdr[11] = 0;             // adaptive filtering
        ihdr[12] = 0;             // no interlace
        WriteChunk(file, "IHDR", ihdr);
        WriteChunk(file, "IDAT", deflated.ToArray());
        WriteChunk(file, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream stream, string tag, byte[] body)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
        stream.Write(length);

        var tagBytes = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            tagBytes[i] = (byte)tag[i];
        }
        stream.Write(tagBytes);
        stream.Write(body);

        uint crc = Crc32(tagBytes, body);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(byte[] tag, byte[] body)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in tag)
        {
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }
        foreach (byte b in body)
        {
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }
        return c ^ 0xFFFFFFFFu;
    }
}
