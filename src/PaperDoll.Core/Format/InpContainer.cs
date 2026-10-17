// Splits an INP1 file into its parts: the magic bytes, the JSON payload, the texture section (each texture with its
// encoding byte) and the optional extension section (named byte blobs), all lengths big-endian. INP2 data is recognised
// and rejected with an explanation; bytes after the last section are counted for the load report.

using System.Buffers.Binary;
using System.Text;

namespace PaperDoll.Format;

internal readonly record struct TextureBlob(byte Encoding, ReadOnlyMemory<byte> Data);

internal sealed class InpContainer
{
    internal static ReadOnlySpan<byte> Inp1Magic => "TRNSRTS\0"u8;
    internal static ReadOnlySpan<byte> Inp2Magic => "TRNSRTS2"u8;
    internal static ReadOnlySpan<byte> TextureSection => "TEX_SECT"u8;
    internal static ReadOnlySpan<byte> ExtensionSection => "EXT_SECT"u8;

    public required ReadOnlyMemory<byte> Payload { get; init; }
    public required List<TextureBlob> Textures { get; init; }
    public required List<KeyValuePair<string, ReadOnlyMemory<byte>>> Extensions { get; init; }
    public int TrailingBytes { get; init; }

    public static InpContainer Parse(ReadOnlyMemory<byte> data)
    {
        var span = data.Span;
        if (span.Length >= 8 && span[..8].SequenceEqual(Inp2Magic))
        {
            throw new PuppetFormatException(
                "This is an INP2 puppet (Inochi2D 0.9 format), which PaperDoll does not read. " +
                "PaperDoll reads INP1 puppets, as written by Inochi Creator 0.7/0.8, nijigenerate and Mannequin Character Generator.");
        }

        if (span.Length < 8 || !span[..8].SequenceEqual(Inp1Magic))
            throw new PuppetFormatException("Not an INP puppet: the file does not start with the INP1 magic bytes 'TRNSRTS\\0'.");

        var offset = 8;
        var payloadLength = ReadLength(span, ref offset, "puppet data length");
        var payload = Slice(data, ref offset, payloadLength, "puppet data");

        if (!TryReadTag(span, ref offset, TextureSection))
            throw new PuppetFormatException($"Expected the texture section 'TEX_SECT' at byte {offset}.");

        var textureCount = ReadLength(span, ref offset, "texture count");
        var textures = new List<TextureBlob>(Math.Min(textureCount, 4096));
        for (var i = 0; i < textureCount; i++)
        {
            var length = ReadLength(span, ref offset, $"texture {i} length");
            if (offset >= span.Length)
                throw Truncated(offset, $"texture {i} encoding");
            var encoding = span[offset++];
            textures.Add(new TextureBlob(encoding, Slice(data, ref offset, length, $"texture {i}")));
        }

        var extensions = new List<KeyValuePair<string, ReadOnlyMemory<byte>>>();
        if (span.Length - offset >= 8 && TryReadTag(span, ref offset, ExtensionSection))
        {
            var count = ReadLength(span, ref offset, "extension count");
            for (var i = 0; i < count; i++)
            {
                var nameLength = ReadLength(span, ref offset, $"extension {i} name length");
                var name = Slice(data, ref offset, nameLength, $"extension {i} name");
                var payloadSize = ReadLength(span, ref offset, $"extension {i} length");
                var blob = Slice(data, ref offset, payloadSize, $"extension {i}");
                extensions.Add(new(Encoding.UTF8.GetString(name.Span), blob));
            }
        }

        return new InpContainer
        {
            Payload = payload,
            Textures = textures,
            Extensions = extensions,
            TrailingBytes = span.Length - offset,
        };
    }

    private static bool TryReadTag(ReadOnlySpan<byte> span, ref int offset, ReadOnlySpan<byte> tag)
    {
        if (span.Length - offset < tag.Length || !span.Slice(offset, tag.Length).SequenceEqual(tag))
            return false;
        offset += tag.Length;
        return true;
    }

    private static int ReadLength(ReadOnlySpan<byte> span, ref int offset, string what)
    {
        if (span.Length - offset < 4)
            throw Truncated(offset, what);
        var value = BinaryPrimitives.ReadUInt32BigEndian(span.Slice(offset, 4));
        offset += 4;
        if (value > int.MaxValue)
            throw new PuppetFormatException($"The {what} at byte {offset - 4} is too large ({value}).");
        return (int)value;
    }

    private static ReadOnlyMemory<byte> Slice(ReadOnlyMemory<byte> data, ref int offset, int length, string what)
    {
        if (data.Length - offset < length)
            throw Truncated(offset, $"{what} ({length} bytes)");
        var slice = data.Slice(offset, length);
        offset += length;
        return slice;
    }

    private static PuppetFormatException Truncated(int offset, string what) =>
        new($"The file ends early: expected the {what} at byte {offset}.");
}
