using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#if MONOGAME
namespace PaperDoll.MonoGame;
#else
namespace PaperDoll.Fna;
#endif

/// <summary>The vertex <c>PaperDoll.fx</c> reads: position, tint and opacity, screen tint and mask threshold, UV, and the UV rectangle outside which the texture reads transparent.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct PuppetVertex(Vector2 position, Color color, Color screen, Vector2 uv, Vector4 clip) : IVertexType
{
    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector2, VertexElementUsage.Position, 0),
        new VertexElement(8, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(12, VertexElementFormat.Color, VertexElementUsage.Color, 1),
        new VertexElement(16, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(24, VertexElementFormat.Vector4, VertexElementUsage.TextureCoordinate, 1));

    /// <summary>Position in the space of the effect's <c>Transform</c> (puppet space, or pixels for layer quads).</summary>
    public Vector2 Position = position;

    /// <summary>Premultiplied tint and opacity: <c>(tint * opacity, opacity)</c>.</summary>
    public Color Color = color;

    /// <summary>Screen tint in RGB; the mask threshold in alpha when the vertex belongs to a mask source.</summary>
    public Color Screen = screen;

    /// <summary>Texture coordinates on the bound texture (an atlas page or a layer).</summary>
    public Vector2 Uv = uv;

    /// <summary>The area of the bound texture the drawable covers, as (min U, min V, max U, max V); outside it the texture reads transparent.</summary>
    public Vector4 Clip = clip;

    readonly VertexDeclaration IVertexType.VertexDeclaration => Declaration;
}
