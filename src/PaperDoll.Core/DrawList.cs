// What an instance hands a renderer after each update: DrawItems back to front (each Part with its puppet-space
// vertices, texture, opacity, tints, blend mode and masks, and begin and end items around each Composite's layer), the
// MaskSources those items are clipped by, with their shapes, and the bounds of everything drawn.

using System.Numerics;

namespace PaperDoll;

/// <summary>What a <see cref="DrawItem"/> stands for.</summary>
public enum DrawItemKind
{
    /// <summary>A Part to draw.</summary>
    Part,

    /// <summary>
    /// The start of a <see cref="PaperDoll.Composite"/>'s layer: the items up to the matching
    /// <see cref="EndComposite"/> are drawn into a layer of their own, cleared to transparent.
    /// </summary>
    BeginComposite,

    /// <summary>
    /// The end of a Composite's layer: the layer is drawn onto what lies below it like a Part, with the Composite's
    /// opacity, tint, screen tint, blend mode and masks.
    /// </summary>
    EndComposite,
}

/// <summary>
/// One entry of <see cref="PuppetInstance.DrawList"/>: a Part ready to draw (its mesh in puppet space and the state to
/// draw it with), or the start or end of a Composite's layer. Items are back to front; the arrays are owned by the
/// instance and change when it updates.
/// </summary>
/// <remarks>
/// Puppet space is the puppet's own pixel space: x to the right, y down, origin where the puppet file puts it. A renderer
/// maps it to the screen with its own camera. Textures are stored with premultiplied alpha, so a renderer draws a Part as
/// <c>screen(texel) * (Tint * Opacity, Opacity)</c>, where <c>screen</c> applies <see cref="ScreenTint"/>, blended by
/// <see cref="BlendMode"/> (Normal is <c>One, InverseSourceAlpha</c>) and clipped by <see cref="Masks"/>. A Composite's
/// layer is drawn the same way, with the layer as its texture.
/// </remarks>
public sealed class DrawItem
{
    private readonly Vector2[] _positions;

    internal DrawItem(Part part, Composite? layer, DrawMask[] masks)
    {
        Kind = DrawItemKind.Part;
        Node = part;
        Part = part;
        Layer = layer;
        BlendMode = part.BlendMode;
        Masks = masks;
        _positions = new Vector2[part.Mesh.Vertices.Length];
        Texture = MaskSource.AlbedoOf(part);
    }

    internal DrawItem(Composite composite, Composite? layer, DrawItemKind kind, DrawMask[] masks)
    {
        Kind = kind;
        Node = composite;
        Composite = composite;
        Layer = layer;
        BlendMode = composite.BlendMode;
        Masks = masks;
        _positions = [];
    }

    /// <summary>Whether this is a Part or the start or end of a Composite's layer.</summary>
    public DrawItemKind Kind { get; }

    /// <summary>The Part or Composite this item stands for.</summary>
    public Node Node { get; }

    /// <summary>The Part this item draws, or <see langword="null"/> for a Composite's start or end.</summary>
    public Part? Part { get; }

    /// <summary>The Composite whose layer starts or ends here, or <see langword="null"/> for a Part.</summary>
    public Composite? Composite { get; }

    /// <summary>
    /// The innermost <see cref="PaperDoll.Composite"/> this item is drawn in, or <see langword="null"/> at the puppet's
    /// top level. For a Composite's start and end, the Composite that encloses it.
    /// </summary>
    public Composite? Layer { get; }

    /// <summary>
    /// The albedo texture's index in <see cref="Puppet.Textures"/>, or <see langword="null"/> if the Part has none
    /// (nothing to draw) and for Composites.
    /// </summary>
    public int? Texture { get; }

    /// <summary>Vertex positions in puppet space, one per mesh vertex. Empty for Composites.</summary>
    public ReadOnlySpan<Vector2> Positions => _positions;

    /// <summary>Texture coordinates, one per vertex (shared with the loaded puppet's mesh). Empty for Composites.</summary>
    public ReadOnlySpan<Vector2> Uvs => Part is { } part ? part.Mesh.Uvs : default;

    /// <summary>Triangle list indices into <see cref="Positions"/> (shared with the loaded puppet's mesh). Empty for Composites.</summary>
    public ReadOnlySpan<ushort> Indices => Part is { } part ? part.Mesh.Indices : default;

    /// <summary>Opacity, 0 to 1.</summary>
    public float Opacity { get; internal set; }

    /// <summary>Multiplicative tint (RGB, 0 to 1).</summary>
    public Vector3 Tint { get; internal set; }

    /// <summary>
    /// Screen tint (RGB, 0 to 1), applied to the premultiplied texel before the tint:
    /// <c>1 - (1 - texel.rgb) * (1 - ScreenTint * texel.a)</c>.
    /// </summary>
    public Vector3 ScreenTint { get; internal set; }

    /// <summary>
    /// Strength of the emissive texture: the Part's own times its parameter offset.
    /// <c>PaperDoll.FNA</c> does not draw emission. 1 for Composites.
    /// </summary>
    public float EmissionStrength { get; internal set; } = 1f;

    /// <summary>The node's mask threshold with its offset, 0 to 1 (it applies when the node is a mask source, see <see cref="MaskSource.Threshold"/>).</summary>
    public float MaskThreshold { get; internal set; }

    /// <summary>Blend mode.</summary>
    public BlendMode BlendMode { get; }

    /// <summary>
    /// The masks this item is clipped by: draw where any <see cref="MaskingMode.Mask"/> source is (if there is one) and
    /// where no <see cref="MaskingMode.DodgeMask"/> source is. Empty if the item is not masked. Sources missing from the
    /// puppet are left out, and each source and mode is listed once.
    /// </summary>
    public IReadOnlyList<DrawMask> Masks { get; }

    /// <summary>The effective z-sort the item was ordered by: the sum along the path from the root (larger is further back).</summary>
    public float ZSort { get; internal set; }

    internal Vector2[] PositionArray => _positions;

    /// <inheritdoc/>
    public override string ToString() => Kind switch
    {
        DrawItemKind.BeginComposite => $"begin {Node}",
        DrawItemKind.EndComposite => $"end {Node}",
        _ => $"{Node} z={ZSort}",
    };
}

/// <summary>One mask on a <see cref="DrawItem"/>.</summary>
/// <param name="Source">The drawable whose shape is the mask.</param>
/// <param name="Mode">Whether the item is drawn inside the source (<see cref="MaskingMode.Mask"/>) or outside it (<see cref="MaskingMode.DodgeMask"/>).</param>
public readonly record struct DrawMask(MaskSource Source, MaskingMode Mode);

/// <summary>
/// The shape of a drawable used as a mask, as of the instance's last update. A <see cref="PaperDoll.Part"/> masks where
/// its texture's alpha is above <see cref="Threshold"/>; a <see cref="PaperDoll.Mask"/> node masks with its whole mesh.
/// </summary>
/// <remarks>
/// A source masks whether or not it is drawn itself (hidden and disabled sources still mask), and its
/// own masks do not apply to it.
/// </remarks>
public sealed class MaskSource
{
    private readonly Vector2[] _positions;

    internal MaskSource(Drawable node)
    {
        Node = node;
        _positions = new Vector2[node.Mesh.Vertices.Length];
        if (node is Part part)
            Texture = AlbedoOf(part);
    }

    /// <summary>The Part or Mask node.</summary>
    public Drawable Node { get; }

    /// <summary>Whether the whole mesh masks (a <see cref="PaperDoll.Mask"/> node) rather than the texture's alpha.</summary>
    public bool IsShape => Node is Mask;

    /// <summary>
    /// For a Part, its albedo texture's index in <see cref="Puppet.Textures"/>; <see langword="null"/> for a Mask node,
    /// and for a Part without a texture (it masks nothing).
    /// </summary>
    public int? Texture { get; }

    /// <summary>Vertex positions in puppet space, one per mesh vertex.</summary>
    public ReadOnlySpan<Vector2> Positions => _positions;

    /// <summary>Texture coordinates, one per vertex (shared with the loaded puppet's mesh). Empty for a Mask node.</summary>
    public ReadOnlySpan<Vector2> Uvs => IsShape ? default : Node.Mesh.Uvs;

    /// <summary>Triangle list indices into <see cref="Positions"/> (shared with the loaded puppet's mesh).</summary>
    public ReadOnlySpan<ushort> Indices => Node.Mesh.Indices;

    /// <summary>
    /// For a Part, the alpha at or below which a texel does not mask: the Part's <c>mask_threshold</c> with its offset,
    /// clamped to 0..1. 0 for a Mask node.
    /// </summary>
    public float Threshold { get; internal set; }

    internal Vector2[] PositionArray => _positions;

    /// <summary>The Part's albedo texture: its first non-empty slot, since empty slots are skipped.</summary>
    internal static int? AlbedoOf(Part part)
    {
        foreach (var slot in part.TextureSlots)
        {
            if (slot is { } index)
                return index;
        }

        return null;
    }

    /// <inheritdoc/>
    public override string ToString() => $"mask {Node}";
}

/// <summary>An axis-aligned rectangle in puppet space.</summary>
/// <param name="Min">The top-left corner (smallest x and y).</param>
/// <param name="Max">The bottom-right corner (largest x and y).</param>
public readonly record struct PuppetBounds(Vector2 Min, Vector2 Max)
{
    /// <summary>Width and height.</summary>
    public Vector2 Size => Max - Min;

    /// <summary>The centre.</summary>
    public Vector2 Center => (Min + Max) / 2;

    /// <summary>Whether the rectangle has no area (nothing drawn).</summary>
    public bool IsEmpty => !(Max.X > Min.X && Max.Y > Min.Y);
}
