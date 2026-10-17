using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace PaperDoll.Tests;

/// <summary>Builds INP1 files and small images in memory, so most tests need no sample puppet.</summary>
internal static class TestData
{
    public static byte[] Inp(string json, IEnumerable<(byte Encoding, byte[] Data)>? textures = null,
        IEnumerable<(string Name, byte[] Data)>? extensions = null, byte[]? trailing = null)
    {
        using var stream = new MemoryStream();
        stream.Write("TRNSRTS\0"u8);
        var payload = Encoding.UTF8.GetBytes(json);
        WriteUInt(stream, (uint)payload.Length);
        stream.Write(payload);

        var texList = textures?.ToList() ?? [];
        stream.Write("TEX_SECT"u8);
        WriteUInt(stream, (uint)texList.Count);
        foreach (var (encoding, data) in texList)
        {
            WriteUInt(stream, (uint)data.Length);
            stream.WriteByte(encoding);
            stream.Write(data);
        }

        if (extensions is not null)
        {
            var extList = extensions.ToList();
            stream.Write("EXT_SECT"u8);
            WriteUInt(stream, (uint)extList.Count);
            foreach (var (name, data) in extList)
            {
                var nameBytes = Encoding.UTF8.GetBytes(name);
                WriteUInt(stream, (uint)nameBytes.Length);
                stream.Write(nameBytes);
                WriteUInt(stream, (uint)data.Length);
                stream.Write(data);
            }
        }

        if (trailing is not null)
            stream.Write(trailing);
        return stream.ToArray();
    }

    /// <summary>A puppet JSON with the given root children and parameters, as raw JSON fragments.</summary>
    public static string Puppet(string children = "", string parameters = "", string extraRoot = "") => $$"""
        {
          "meta": { "name": "Test", "version": "v0.8.6" },
          "physics": { "pixelsPerMeter": 1000, "gravity": 9.8 },
          "nodes": { "uuid": 1, "name": "root", "type": "Node", "enabled": true, "zsort": 0,
                     "transform": { "trans": [0, 0, 0], "rot": [0, 0, 0], "scale": [1, 1] },
                     "children": [ {{children}} ] },
          "param": [ {{parameters}} ]{{extraRoot}}
        }
        """;

    public static Puppet Load(string json, IEnumerable<(byte, byte[])>? textures = null) => PaperDoll.Puppet.Load(Inp(json, textures));

    private static void WriteUInt(Stream stream, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    /// <summary>An 8-bit RGBA PNG (colour type 6, no interlace).</summary>
    public static byte[] Png(int width, int height, byte[] rgba)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        Chunk(stream, "IHDR", header);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                z.WriteByte(0); // filter: none
                z.Write(rgba, y * width * 4, width * 4);
            }
        }

        Chunk(stream, "IDAT", raw.ToArray());
        Chunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        WriteUInt(stream, (uint)data.Length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        WriteUInt(stream, Crc32([.. typeBytes, .. data]));
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        return ~crc;
    }

    /// <summary>A 32-bit TGA, top-left origin; RLE (type 10) packs each row as one run if <paramref name="rle"/>.</summary>
    public static byte[] Tga(int width, int height, byte[] rgba, bool rle)
    {
        using var stream = new MemoryStream();
        var header = new byte[18];
        header[2] = (byte)(rle ? 10 : 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)height);
        header[16] = 32;
        header[17] = 0x28; // 8 alpha bits, top-left origin
        stream.Write(header);

        for (var i = 0; i < width * height; i++)
        {
            if (rle && i % width == 0)
                stream.WriteByte((byte)(width - 1)); // raw packet of one row
            stream.Write([rgba[i * 4 + 2], rgba[i * 4 + 1], rgba[i * 4], rgba[i * 4 + 3]]); // BGRA
        }

        return stream.ToArray();
    }

    public static readonly byte[] Pixels2x2 =
    [
        255, 0, 0, 255,    0, 255, 0, 128,
        0, 0, 255, 0,      10, 20, 30, 40,
    ];
}
