// A puppet's texture slots, kept encoded as stored, with their size read from the image header, and decoded to 8-bit
// RGBA on request (PNG and TGA; BC7 is not supported). Pixels stay premultiplied, as INP1 stores them.
// Decoding uses StbImageSharp.

using System.Runtime.InteropServices;
using StbImageSharp;

namespace PaperDoll;

/// <summary>
/// One texture slot of a puppet, kept encoded. Decode it with <see cref="Decode"/> when a renderer needs the pixels;
/// the result is not cached, so the caller owns it.
/// </summary>
public sealed class PuppetTexture
{
    private readonly ReadOnlyMemory<byte> _data;

    internal PuppetTexture(int index, byte encoding, ReadOnlyMemory<byte> data, int width, int height)
    {
        Index = index;
        RawEncoding = encoding;
        _data = data;
        Width = width;
        Height = height;
    }

    /// <summary>The slot's index, as referenced by <see cref="Part.TextureSlots"/>.</summary>
    public int Index { get; }

    /// <summary>The encoding byte as stored in the file.</summary>
    public byte RawEncoding { get; }

    /// <summary>The encoding, or <see langword="null"/> if the file uses a value PaperDoll does not know.</summary>
    public TextureEncoding? Encoding => RawEncoding <= (byte)TextureEncoding.Bc7 ? (TextureEncoding)RawEncoding : null;

    /// <summary>The encoded bytes.</summary>
    public ReadOnlyMemory<byte> EncodedData => _data;

    /// <summary>Whether the slot is empty (the file stores zero bytes for it).</summary>
    public bool IsEmpty => _data.IsEmpty;

    /// <summary>Width in pixels from the image header, or 0 if empty or unreadable.</summary>
    public int Width { get; }

    /// <summary>Height in pixels from the image header, or 0 if empty or unreadable.</summary>
    public int Height { get; }

    /// <summary>Whether <see cref="Decode"/> can decode this slot (PNG or TGA with a readable header).</summary>
    public bool CanDecode => Encoding is TextureEncoding.Png or TextureEncoding.Tga && Width > 0 && Height > 0;

    /// <summary>
    /// Decodes the texture to 8-bit RGBA, rows top to bottom, with the pixels as stored: INP1 textures are premultiplied
    /// (Inochi Creator premultiplies on import, and they are drawn with premultiplied blending).
    /// </summary>
    /// <exception cref="NotSupportedException">The slot is empty, BC7, or uses an unknown encoding.</exception>
    /// <exception cref="PuppetFormatException">The image data is corrupt.</exception>
    public TextureImage Decode()
    {
        if (!CanDecode)
        {
            throw new NotSupportedException(IsEmpty
                ? $"Texture {Index} is empty."
                : $"Texture {Index} uses encoding {RawEncoding} ({Encoding?.ToString() ?? "unknown"}), which PaperDoll does not decode.");
        }

        ImageResult result;
        try
        {
            using var stream = OpenStream(_data);
            result = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new PuppetFormatException($"Texture {Index} ({Encoding}) could not be decoded: {ex.Message}", ex);
        }

        return new TextureImage(result.Width, result.Height, result.Data);
    }

    internal static (int Width, int Height) ReadHeader(ReadOnlyMemory<byte> data)
    {
        try
        {
            using var stream = OpenStream(data);
            var info = ImageInfo.FromStream(stream);
            return info is { } i ? (i.Width, i.Height) : (0, 0);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return (0, 0);
        }
    }

    private static MemoryStream OpenStream(ReadOnlyMemory<byte> data) =>
        MemoryMarshal.TryGetArray(data, out var segment) && segment.Array is { } array
            ? new MemoryStream(array, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(data.ToArray(), writable: false);
}

/// <summary>Decoded texture pixels: 8-bit RGBA as stored in the puppet (premultiplied for INP1), rows top to bottom.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Rgba">Pixel data, <c>Width * Height * 4</c> bytes.</param>
public sealed record TextureImage(int Width, int Height, byte[] Rgba);
