// MeshGroup deformation, the part fixed per loaded puppet: which MeshGroup acts on which node below it, and how (its
// rest vertices, its deformed vertices, or only its origin); and each MeshGroup's rest mesh prepared for lookups, with
// a grid that finds the triangle under a position and each triangle's map out of rest space. Built once per puppet and
// shared by its instances; mapping vertices through the deformed triangles on each update is in PuppetEvaluator.

using System.Numerics;

namespace PaperDoll.Evaluation;

/// <summary>How a MeshGroup acts on one node below it.</summary>
internal enum MeshGroupFilter : byte
{
    /// <summary>Not affected.</summary>
    None,

    /// <summary>A drawable whose rest vertices are mapped before its own deformation is used (the pre-process filter).</summary>
    Vertices,

    /// <summary>A drawable whose deformed vertices are mapped after its deformation (a dynamic MeshGroup's post-process filter).</summary>
    DeformedVertices,

    /// <summary>A node whose origin is moved with the mesh (<c>translate_children</c>).</summary>
    Translation,
}

/// <summary>
/// Which MeshGroup acts on which node, and each MeshGroup's triangle lookup. Depends only on the loaded puppet, so it is
/// built once per puppet and shared by its instances.
/// </summary>
/// <remarks>
/// A MeshGroup with triangles acts on each child, and goes on into the children of nodes
/// that propagate (Parts and Masks, and Composites with <c>propagate_meshgroup</c>), but not into plain nodes, physics
/// nodes or nested MeshGroups (a nested MeshGroup is itself deformed, and acts on its own children). A propagating
/// Composite is not moved itself. Drawables have their vertices mapped; other nodes are moved only with
/// <c>translate_children</c>. Each node is reached by at most one MeshGroup.
/// </remarks>
internal sealed class MeshGroupPlan
{
    public MeshGroupPlan(Puppet puppet)
    {
        var count = puppet.Nodes.Count;
        Shapes = new MeshGroupShape?[count];
        Source = new int[count];
        Filter = new MeshGroupFilter[count];
        Array.Fill(Source, -1);

        foreach (var node in puppet.Nodes) // depth first: an outer MeshGroup is set up before an inner one
        {
            if (node is not MeshGroup group || MeshGroupShape.Create(group.Mesh) is not { } shape)
                continue;
            Shapes[group.Index] = shape;
            foreach (var child in group.Children)
                Setup(group, child);
        }
    }

    /// <summary>By node index: the lookup of each MeshGroup that has triangles; otherwise <see langword="null"/>.</summary>
    public MeshGroupShape?[] Shapes { get; }

    /// <summary>By node index: the index of the MeshGroup acting on the node, or -1.</summary>
    public int[] Source { get; }

    /// <summary>By node index: how the MeshGroup in <see cref="Source"/> acts on the node.</summary>
    public MeshGroupFilter[] Filter { get; }

    private void Setup(MeshGroup group, Node node)
    {
        var filter = node switch
        {
            Composite { PropagateMeshGroup: true } => MeshGroupFilter.None,
            Drawable => group.DynamicDeformation ? MeshGroupFilter.DeformedVertices : MeshGroupFilter.Vertices,
            _ => group.TranslateChildren ? MeshGroupFilter.Translation : MeshGroupFilter.None,
        };
        if (filter != MeshGroupFilter.None)
        {
            Source[node.Index] = group.Index;
            Filter[node.Index] = filter;
        }

        if (!MustPropagate(node))
            return;
        foreach (var child in node.Children)
            Setup(group, child);
    }

    private static bool MustPropagate(Node node) => node switch
    {
        MeshGroup => false,
        Drawable => true,
        Composite composite => composite.PropagateMeshGroup,
        _ => false,
    };
}

/// <summary>
/// A MeshGroup's rest mesh prepared for lookups: for each triangle, the map from a rest position to its coordinates in the
/// triangle, and a grid over the mesh's bounds, one cell per puppet unit, that names the triangle covering each cell.
/// </summary>
/// <remarks>
/// HACK: the grid cell at integer point (x, y) holds the last triangle (in index order) that contains that point, edges
/// included, and a position is looked up by the cell it falls in, so a position near an edge may use the neighbouring
/// triangle's map. Cheap, and what nijilive does; puppets are rigged against it. A degenerate triangle (zero area) claims
/// no cells (docs/design/nijilive-differences.md).
/// </remarks>
internal sealed class MeshGroupShape
{
    private const int MaxCells = 1 << 26; // 128 MB of lookup; a mesh this large is treated as having no triangles

    private readonly ushort[] _cells;
    private readonly int _width;
    private readonly int _height;
    private readonly Vector2 _min;
    private readonly Vector2 _max;

    private MeshGroupShape(ushort[] indices, Matrix3x2[] restInverse, bool[] valid, ushort[] cells, int width, int height, Vector2 min, Vector2 max)
    {
        Indices = indices;
        RestInverse = restInverse;
        Valid = valid;
        _cells = cells;
        _width = width;
        _height = height;
        _min = min;
        _max = max;
    }

    /// <summary>The mesh's triangle list, three indices per triangle.</summary>
    public ushort[] Indices { get; }

    /// <summary>Per triangle: maps a rest position to (s, t), where the position is <c>p1 + s * (p2 - p1) + t * (p3 - p1)</c>.</summary>
    public Matrix3x2[] RestInverse { get; }

    /// <summary>Per triangle: whether it has a map (false for degenerate triangles and indices outside the mesh).</summary>
    public bool[] Valid { get; }

    public int TriangleCount => RestInverse.Length;

    public static MeshGroupShape? Create(MeshData mesh)
    {
        var vertices = mesh.VertexArray;
        var indices = mesh.IndexArray;
        var triangleCount = Math.Min(indices.Length / 3, ushort.MaxValue);
        if (triangleCount == 0 || vertices.Length == 0)
            return null;

        // The bounds cover every vertex, floored and ceiled to whole units.
        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        foreach (var v in vertices)
        {
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y))
                return null;
            min = Vector2.Min(min, v);
            max = Vector2.Max(max, v);
        }

        min = new Vector2(MathF.Floor(min.X), MathF.Floor(min.Y));
        max = new Vector2(MathF.Ceiling(max.X), MathF.Ceiling(max.Y));
        var width = (long)(max.X - min.X) + 1;
        var height = (long)(max.Y - min.Y) + 1;
        if (width * height > MaxCells)
            return null;

        var cells = new ushort[width * height];
        var restInverse = new Matrix3x2[triangleCount];
        var valid = new bool[triangleCount];
        for (var t = 0; t < triangleCount; t++)
        {
            int i1 = indices[3 * t], i2 = indices[3 * t + 1], i3 = indices[3 * t + 2];
            if (i1 >= vertices.Length || i2 >= vertices.Length || i3 >= vertices.Length)
                continue;
            Vector2 p1 = vertices[i1], p2 = vertices[i2], p3 = vertices[i3];
            if (!Matrix3x2.Invert(Basis(p1, p2, p3), out var inverse) || !IsFinite(inverse))
                continue;
            restInverse[t] = inverse;
            valid[t] = true;

            var left = (int)MathF.Floor(MathF.Min(p1.X, MathF.Min(p2.X, p3.X)));
            var top = (int)MathF.Floor(MathF.Min(p1.Y, MathF.Min(p2.Y, p3.Y)));
            var right = (int)MathF.Ceiling(MathF.Max(p1.X, MathF.Max(p2.X, p3.X)));
            var bottom = (int)MathF.Ceiling(MathF.Max(p1.Y, MathF.Max(p2.Y, p3.Y)));
            for (var y = top; y <= bottom; y++)
            {
                for (var x = left; x <= right; x++)
                {
                    if (Contains(new Vector2(x, y), p1, p2, p3))
                        cells[(y - (int)min.Y) * width + (x - (int)min.X)] = (ushort)(t + 1);
                }
            }
        }

        return new MeshGroupShape([.. indices.AsSpan(0, triangleCount * 3)], restInverse, valid, cells, (int)width, (int)height, min, max);
    }

    /// <summary>The triangle whose map applies at a position in the MeshGroup's space, or -1 outside the mesh.</summary>
    public int Lookup(Vector2 p)
    {
        // The far edges are excluded, so a vertex exactly on them is not mapped.
        if (!(p.X >= _min.X && p.X < _max.X && p.Y >= _min.Y && p.Y < _max.Y))
            return -1;
        var x = (int)MathF.Floor(p.X - _min.X);
        var y = (int)MathF.Floor(p.Y - _min.Y);
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height)
            return -1;
        return _cells[y * _width + x] - 1;
    }

    /// <summary>
    /// The affine map that takes a triangle's coordinates (s, t) to <c>p1 + s * (p2 - p1) + t * (p3 - p1)</c>
    /// (row vectors, as System.Numerics transforms them).
    /// </summary>
    public static Matrix3x2 Basis(Vector2 p1, Vector2 p2, Vector2 p3) =>
        new(p2.X - p1.X, p2.Y - p1.Y, p3.X - p1.X, p3.Y - p1.Y, p1.X, p1.Y);

    // Inside or on an edge.
    private static bool Contains(Vector2 p, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        var d1 = Sign(p, p1, p2);
        var d2 = Sign(p, p2, p3);
        var d3 = Sign(p, p3, p1);
        var hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPositive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNegative && hasPositive);

        static float Sign(Vector2 a, Vector2 b, Vector2 c) => (a.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (a.Y - c.Y);
    }

    private static bool IsFinite(Matrix3x2 m) =>
        float.IsFinite(m.M11) && float.IsFinite(m.M12) && float.IsFinite(m.M21) && float.IsFinite(m.M22) && float.IsFinite(m.M31) && float.IsFinite(m.M32);
}
