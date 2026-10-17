// The node tree of a loaded puppet: Node (transform and grouping) and the types built on it (Part, Composite,
// MeshGroup, Mask, SimplePhysics), each with the fields it reads from INP1 and their defaults, and MeshData for their
// meshes. Read-only once loaded; everything that changes per instance lives in the evaluator.

using System.Numerics;

namespace PaperDoll;

/// <summary>A node's local transform as stored in the file.</summary>
/// <param name="Translation">Translation (x, y, z).</param>
/// <param name="Rotation">Rotation around each axis, in radians.</param>
/// <param name="Scale">Scale (x, y).</param>
public readonly record struct NodeTransform(Vector3 Translation, Vector3 Rotation, Vector2 Scale)
{
    /// <summary>The identity transform.</summary>
    public static NodeTransform Identity => new(Vector3.Zero, Vector3.Zero, Vector2.One);
}

/// <summary>A node in a puppet's tree. Plain <c>Node</c> entries are groups with a transform; the other types derive from it.</summary>
public class Node
{
    internal Node() { }

    /// <summary>The node's type.</summary>
    public virtual NodeKind Kind => NodeKind.Node;

    /// <summary>The node's ID, unique within the puppet.</summary>
    public uint Uuid { get; internal set; }

    /// <summary>The node's name.</summary>
    public string Name { get; internal set; } = "";

    /// <summary>Whether the node (and so its subtree) is drawn and updated.</summary>
    public bool Enabled { get; internal set; } = true;

    /// <summary>The node's own z-sort value; draw order uses the sum along the path from the root.</summary>
    public float ZSort { get; internal set; }

    /// <summary>The node's local transform.</summary>
    public NodeTransform Transform { get; internal set; } = NodeTransform.Identity;

    /// <summary>Whether the node ignores its parents' transforms and follows the puppet root only.</summary>
    public bool LockToRoot { get; internal set; }

    /// <summary>The <c>pinToMesh</c> flag. Read but not supported.</summary>
    public bool PinToMesh { get; internal set; }

    /// <summary>The parent node, or <see langword="null"/> for the root.</summary>
    public Node? Parent { get; internal set; }

    /// <summary>The child nodes, in file order. Children of unsupported types are not included.</summary>
    public IReadOnlyList<Node> Children { get; internal set; } = [];

    /// <summary>The node's position in <see cref="Puppet.Nodes"/> (depth first, so a parent comes before its children).</summary>
    internal int Index { get; set; }

    /// <inheritdoc/>
    public override string ToString() => $"{Kind} '{Name}' ({Uuid})";
}

/// <summary>A mesh: vertices, UVs and a triangle list.</summary>
public sealed class MeshData
{
    internal static readonly MeshData Empty = new([], [], [], Vector2.Zero, []);

    internal readonly Vector2[] VertexArray;
    internal readonly Vector2[] UvArray;
    internal readonly ushort[] IndexArray;
    internal readonly float[][] GridAxisArrays;

    internal MeshData(Vector2[] vertices, Vector2[] uvs, ushort[] indices, Vector2 origin, float[][] gridAxes)
    {
        VertexArray = vertices;
        UvArray = uvs;
        IndexArray = indices;
        Origin = origin;
        GridAxisArrays = gridAxes;
    }

    /// <summary>Vertex positions in the node's local space.</summary>
    public ReadOnlySpan<Vector2> Vertices => VertexArray;

    /// <summary>Texture coordinates, one per vertex. Empty for meshes without a texture.</summary>
    public ReadOnlySpan<Vector2> Uvs => UvArray;

    /// <summary>Triangle list indices into <see cref="Vertices"/>.</summary>
    public ReadOnlySpan<ushort> Indices => IndexArray;

    /// <summary>The mesh's origin.</summary>
    public Vector2 Origin { get; }

    /// <summary>Grid axes (<c>grid_axes</c>), present on grid meshes only; otherwise empty.</summary>
    internal IReadOnlyList<float[]> GridAxes => GridAxisArrays;
}

/// <summary>A node with a mesh.</summary>
public abstract class Drawable : Node
{
    internal Drawable() { }

    /// <summary>The node's mesh.</summary>
    public MeshData Mesh { get; internal set; } = MeshData.Empty;

    /// <summary>Whether the file has vertex welding (<c>weldedLinks</c>), which PaperDoll does not support.</summary>
    public bool HasWeldedLinks { get; internal set; }
}

/// <summary>A mask entry on a part or composite.</summary>
/// <param name="SourceUuid">The node whose shape is the mask.</param>
/// <param name="Mode">Whether to draw inside or outside the source.</param>
public readonly record struct MaskBinding(uint SourceUuid, MaskingMode Mode);

/// <summary>A textured mesh.</summary>
public sealed class Part : Drawable
{
    internal Part() { }

    /// <inheritdoc/>
    public override NodeKind Kind => NodeKind.Part;

    /// <summary>
    /// Texture indices into <see cref="Puppet.Textures"/>, in file order (albedo, emissive, bump).
    /// <see langword="null"/> marks a slot the file leaves empty.
    /// </summary>
    public IReadOnlyList<int?> TextureSlots { get; internal set; } = [];

    /// <summary>Opacity, 0 to 1.</summary>
    public float Opacity { get; internal set; } = 1f;

    /// <summary>Multiplicative tint (RGB).</summary>
    public Vector3 Tint { get; internal set; } = Vector3.One;

    /// <summary>Screen tint (RGB).</summary>
    public Vector3 ScreenTint { get; internal set; } = Vector3.Zero;

    /// <summary>Strength of the emissive texture.</summary>
    public float EmissionStrength { get; internal set; } = 1f;

    /// <summary>Blend mode.</summary>
    public BlendMode BlendMode { get; internal set; } = BlendMode.Normal;

    /// <summary>The masks applied to this part.</summary>
    public IReadOnlyList<MaskBinding> Masks { get; internal set; } = [];

    /// <summary>Alpha below which this part does not count when it is used as a mask source.</summary>
    public float MaskThreshold { get; internal set; } = 0.5f;
}

/// <summary>A group whose children are drawn as one layer.</summary>
public sealed class Composite : Node
{
    internal Composite() { }

    /// <inheritdoc/>
    public override NodeKind Kind => NodeKind.Composite;

    /// <summary>Opacity of the layer, 0 to 1.</summary>
    public float Opacity { get; internal set; } = 1f;

    /// <summary>Multiplicative tint (RGB).</summary>
    public Vector3 Tint { get; internal set; } = Vector3.One;

    /// <summary>Screen tint (RGB).</summary>
    public Vector3 ScreenTint { get; internal set; } = Vector3.Zero;

    /// <summary>Blend mode of the layer.</summary>
    public BlendMode BlendMode { get; internal set; } = BlendMode.Normal;

    /// <summary>The masks applied to the layer.</summary>
    public IReadOnlyList<MaskBinding> Masks { get; internal set; } = [];

    /// <summary>Alpha threshold for masks.</summary>
    public float MaskThreshold { get; internal set; } = 0.5f;

    /// <summary>Whether an enclosing mesh group deforms this composite's children (<c>propagate_meshgroup</c>). False when absent.</summary>
    public bool PropagateMeshGroup { get; internal set; }
}

/// <summary>A mesh that deforms the meshes of its children.</summary>
public sealed class MeshGroup : Drawable
{
    internal MeshGroup() { }

    /// <inheritdoc/>
    public override NodeKind Kind => NodeKind.MeshGroup;

    /// <summary>Whether deformation is recomputed every frame (<c>dynamic_deformation</c>).</summary>
    public bool DynamicDeformation { get; internal set; }

    /// <summary>Whether children are translated with the mesh (<c>translate_children</c>). False when absent.</summary>
    public bool TranslateChildren { get; internal set; }
}

/// <summary>A mesh used only as a mask source.</summary>
public sealed class Mask : Drawable
{
    internal Mask() { }

    /// <inheritdoc/>
    public override NodeKind Kind => NodeKind.Mask;
}

/// <summary>A pendulum simulation that drives a parameter.</summary>
public sealed class SimplePhysics : Node
{
    internal SimplePhysics() { }

    /// <inheritdoc/>
    public override NodeKind Kind => NodeKind.SimplePhysics;

    /// <summary>The driven parameter's ID, or <see langword="null"/> if none is set.</summary>
    public uint? ParameterUuid { get; internal set; }

    /// <summary>The pendulum model.</summary>
    public PhysicsModel Model { get; internal set; } = PhysicsModel.Pendulum;

    /// <summary>How the simulation maps onto the parameter.</summary>
    public PhysicsMapMode MapMode { get; internal set; } = PhysicsMapMode.AngleLength;

    /// <summary>Gravity multiplier.</summary>
    public float Gravity { get; internal set; } = 1f;

    /// <summary>Pendulum length.</summary>
    public float Length { get; internal set; } = 100f;

    /// <summary>Resonant frequency.</summary>
    public float Frequency { get; internal set; } = 1f;

    /// <summary>Angle damping.</summary>
    public float AngleDamping { get; internal set; } = 0.5f;

    /// <summary>Length damping.</summary>
    public float LengthDamping { get; internal set; } = 0.5f;

    /// <summary>Scale applied to the output on each axis.</summary>
    public Vector2 OutputScale { get; internal set; } = Vector2.One;

    /// <summary>Whether the simulation follows the node's local transform only.</summary>
    public bool LocalOnly { get; internal set; }
}
