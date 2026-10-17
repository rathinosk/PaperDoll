// Evaluates one instance into its draw list. Parameters first, through their bindings, with physics nodes stepping and
// driving their parameters in tree order; then down the tree, parents before children: each node's world transform
// with its offsets, the MeshGroup that reaches it, and its deformed vertices; then visibility, draw order (by effective
// z-sort, with each Composite drawn as a layer), each item's final opacity, tints and masks, and the bounds.

using System.Numerics;

namespace PaperDoll.Evaluation;

/// <summary>
/// Evaluates one instance into its draw list. Everything is allocated once, when the instance is created; evaluating
/// allocates nothing.
/// </summary>
/// <remarks>
/// Draw order: the root pass holds every Part outside a Composite and every top-level Composite. It is sorted by
/// effective z-sort, larger first (further back), and ties keep the order the items were queued in: Parts in a
/// depth-first walk with each node's children sorted by z-sort, then every Composite after them. A Composite's pass holds
/// the Parts and nested Composites it gathers, sorted the same way, with ties in gathering order, so equal z-sorts never
/// swap between updates (docs/design/nijilive-differences.md).
/// <para>
/// MeshGroups act in one pass over the tree, which is enough because a MeshGroup acts only on nodes below it. For each
/// node, parents first: its world matrix; then the pre-process filter (a MeshGroup moves its origin, or maps its rest
/// vertices and adds the change to its deformation); then, for a MeshGroup, its triangles as its deformation leaves them;
/// then the post-process filter of a dynamic MeshGroup (its deformed vertices are mapped).
/// </para>
/// <para>
/// A Composite is emitted as a begin item, its pass, and an end item that carries its own opacity, tints, blend mode and
/// masks. Mask sources are updated on every evaluation, drawn or not, because a source masks whether or not it is visible.
/// </para>
/// <para>
/// Physics: every parameter no physics node drives is applied first; then each physics node, in tree order, reads its
/// origin as the offsets so far leave it (MeshGroups have not acted yet), steps, and applies the value it forces on its
/// parameter at once, so a physics node further down a chain sees what the ones above it did. A parameter whose driver is
/// disabled is never applied.
/// </para>
/// </remarks>
internal sealed class PuppetEvaluator
{
    private readonly Puppet _puppet;
    private readonly Node[] _nodes;               // an array, so iterating it allocates no enumerator
    private readonly Matrix4x4[] _world;
    private readonly float[] _zSort;
    private readonly bool[] _visible;
    private readonly DrawItem?[] _items;          // by node index: the item of a Part, the begin item of a Composite
    private readonly DrawItem?[] _endItems;       // by node index, for Composites: the end item
    private readonly MaskSource[] _maskSources;   // every drawable some item is masked by
    private readonly int[][] _childOrder;         // by node index: child node indices, re-sorted by z-sort on each evaluation
    private readonly Dictionary<Composite, Node[]> _compositeMembers = [];
    private readonly List<Entry> _rootPass = [];
    private readonly List<Composite> _rootComposites = [];
    private readonly Stack<List<Entry>> _passPool = new();
    private readonly List<DrawItem> _drawList = [];
    private readonly NodeOffsets _offsets;
    private readonly BindingEvaluator _bindings;
    private readonly MeshGroupPlan _meshGroups;
    private readonly Matrix3x2[]?[] _triangleMaps; // by node index, for MeshGroups: each triangle's rest-to-deformed map
    private readonly Matrix4x4[] _groupInverse;   // by node index, for MeshGroups: the inverse of the world matrix
    private readonly bool[] _groupActive;         // by node index, for MeshGroups: whether this evaluation's maps are usable
    private readonly PhysicsDriver[] _drivers;    // every SimplePhysics node whose parameter exists, in tree order
    private readonly PhysicsDriver?[] _driverOf;  // by node index
    private readonly bool[] _driven;              // by parameter index: whether a physics node drives it
    private bool _physicsActive;                  // physics runs in the current evaluation
    private Vector2 _scenePosition;               // the host's scene position, in puppet units

    public PuppetEvaluator(Puppet puppet)
    {
        _puppet = puppet;
        _nodes = [.. puppet.Nodes];
        var count = _nodes.Length;
        _world = new Matrix4x4[count];
        _zSort = new float[count];
        _visible = new bool[count];
        _items = new DrawItem?[count];
        _endItems = new DrawItem?[count];
        _childOrder = new int[count][];
        _offsets = new NodeOffsets(count);
        var sources = new Dictionary<uint, MaskSource>();
        _bindings = new BindingEvaluator(puppet, _offsets);
        _meshGroups = puppet.MeshGroups;
        _triangleMaps = new Matrix3x2[]?[count];
        _groupInverse = new Matrix4x4[count];
        _groupActive = new bool[count];

        foreach (var node in _nodes)
        {
            if (_meshGroups.Shapes[node.Index] is { } shape)
                _triangleMaps[node.Index] = new Matrix3x2[shape.TriangleCount];
            if (node is Drawable drawable && (_triangleMaps[node.Index] is not null
                || _meshGroups.Filter[node.Index] is MeshGroupFilter.Vertices or MeshGroupFilter.DeformedVertices))
                _offsets.Deform[node.Index] ??= new Vector2[drawable.Mesh.Vertices.Length];

            _childOrder[node.Index] = node.Children.Select(c => c.Index).ToArray();
            if (node is Part part)
                _items[node.Index] = new DrawItem(part, EnclosingComposite(part), ResolveMasks(part.Masks, sources));
            if (node is Composite composite)
            {
                var masks = ResolveMasks(composite.Masks, sources);
                var layer = EnclosingComposite(composite);
                _items[node.Index] = new DrawItem(composite, layer, DrawItemKind.BeginComposite, masks);
                _endItems[node.Index] = new DrawItem(composite, layer, DrawItemKind.EndComposite, masks);
                _compositeMembers[composite] = GatherCompositeMembers(composite);
            }
        }

        _maskSources = [.. sources.Values];

        var drivers = new List<PhysicsDriver>();
        _driverOf = new PhysicsDriver?[count];
        _driven = new bool[puppet.Parameters.Count];
        foreach (var node in _nodes)
        {
            if (node is SimplePhysics { ParameterUuid: { } id } physics && puppet.TryGetParameter(id, out var parameter))
            {
                var driver = new PhysicsDriver(physics, parameter, puppet.Physics);
                drivers.Add(driver);
                _driverOf[node.Index] = driver;
                _driven[parameter.Index] = true;
            }
        }

        _drivers = [.. drivers];
        HasPhysics = _drivers.Any(d => d.Enabled);
    }

    /// <summary>Whether any physics node can run, so the instance needs evaluating on every update.</summary>
    public bool HasPhysics { get; }

    /// <summary>
    /// The masks an item is clipped by: each source and mode once, in file order, leaving out sources that are missing or
    /// cannot mask. Parts and Mask nodes can be sources; Composites and MeshGroups cannot (<c>paperdoll validate</c> lists
    /// them).
    /// </summary>
    private DrawMask[] ResolveMasks(IReadOnlyList<MaskBinding> bindings, Dictionary<uint, MaskSource> sources)
    {
        if (bindings.Count == 0)
            return [];

        var masks = new List<DrawMask>(bindings.Count);
        foreach (var binding in bindings)
        {
            if (!_puppet.TryGetNode(binding.SourceUuid, out var node) || node is not (Part or Mask))
                continue;
            if (!sources.TryGetValue(node.Uuid, out var source))
                sources.Add(node.Uuid, source = new MaskSource((Drawable)node));
            var mask = new DrawMask(source, binding.Mode);
            if (!masks.Contains(mask))
                masks.Add(mask);
        }

        return [.. masks];
    }

    public IReadOnlyList<DrawItem> DrawList => _drawList;

    public PuppetBounds Bounds { get; private set; }

    public BindingEvaluator Bindings => _bindings;

    /// <summary>Every drawable some item is masked by.</summary>
    public IReadOnlyList<MaskSource> MaskSources => _maskSources;

    /// <summary>
    /// Applies the parameter values (one per parameter, by index), then recomputes transforms, z-sort, visibility, the
    /// draw order, every visible item's vertices and colours, and the mask sources. Without <paramref name="physics"/>,
    /// the parameters are applied as given; with it, each physics node steps and writes the value it forces into
    /// <paramref name="parameterValues"/> (and <paramref name="physicsValues"/>, if given), replacing the host's value.
    /// </summary>
    public void Evaluate(Vector2[] parameterValues, PhysicsStep? physics = null, Vector2[]? physicsValues = null)
    {
        _physicsActive = physics is not null && _drivers.Length > 0;
        if (_physicsActive)
        {
            var step = physics!.Value;
            _scenePosition = step.ScenePosition;
            _bindings.Evaluate(parameterValues, _driven);
            RunPhysics(parameterValues, step, physicsValues);
        }
        else
        {
            _bindings.Evaluate(parameterValues);
        }

        _bindings.ClearInputs(); // a parameter whose physics node is disabled is never applied
        EvaluateNodes();
        BuildDrawList();
        UpdateItems();
        UpdateMaskSources();
    }

    private void EvaluateNodes()
    {
        var rootLocal = LocalMatrix(OffsetTransform(_puppet.Root));
        foreach (var node in _nodes) // depth first, so parents are done before their children
        {
            var i = node.Index;
            var zSort = node.ZSort + _offsets.ZSort[i];
            if (node.Parent is not { } parent)
            {
                _world[i] = LocalMatrix(OffsetTransform(node));
                _zSort[i] = zSort;
                _visible[i] = node.Enabled;
                continue;
            }

            // A node locked to the root follows the root's own transform instead of its parent's.
            _world[i] = LocalMatrix(OffsetTransform(node)) * (node.LockToRoot ? rootLocal : _world[parent.Index]);
            ApplyMeshGroups(node, parent, rootLocal);
            _zSort[i] = _zSort[parent.Index] + zSort;
            _visible[i] = node.Enabled && _visible[parent.Index];
        }
    }

    /// <summary>
    /// Steps each enabled physics node from its origin as the offsets so far leave it, and applies its parameter at once
    /// with the value it forces (not at all if the step failed).
    /// </summary>
    private void RunPhysics(Vector2[] values, PhysicsStep step, Vector2[]? physicsValues)
    {
        foreach (var driver in _drivers)
        {
            if (!driver.Enabled)
                continue;

            var node = driver.Node;
            var toPhysics = ToPhysics(node, OffsetWorld(node));
            var anchor = Anchor(node, toPhysics);
            if (step.Reset)
                driver.Reset(anchor);
            if (driver.Step(step.DeltaSeconds, in _offsets.Physics[node.Index], anchor, toPhysics) is not { } forced)
                continue;

            var parameter = driver.Parameter;
            values[parameter.Index] = parameter.IsVec2 ? forced : new Vector2(forced.X, parameter.Defaults.Y);
            physicsValues?[parameter.Index] = values[parameter.Index];
            _bindings.ApplyParameter(parameter.Index, values[parameter.Index]);
        }
    }

    /// <summary>A node's world matrix from its offsets alone, before MeshGroups act.</summary>
    private Matrix4x4 OffsetWorld(Node node)
    {
        var local = LocalMatrix(OffsetTransform(node));
        if (node.Parent is not { } parent)
            return local;
        return local * (node.LockToRoot ? LocalMatrix(OffsetTransform(_puppet.Root)) : OffsetWorld(parent));
    }

    /// <summary>
    /// A physics node's matrix into physics space: puppet space moved by the host's scene position, so moving the puppet
    /// moves every anchor. A <c>local_only</c> node ignores the scene position.
    /// </summary>
    private Matrix4x4 ToPhysics(SimplePhysics node, Matrix4x4 world) =>
        node.LocalOnly ? world : world * Matrix4x4.CreateTranslation(_scenePosition.X, _scenePosition.Y, 0);

    /// <summary>The point a physics node's pendulum hangs from: its origin in physics space, or its own translation with <c>local_only</c>.</summary>
    private Vector2 Anchor(SimplePhysics node, Matrix4x4 toPhysics)
    {
        if (!node.LocalOnly)
            return new Vector2(toPhysics.M41, toPhysics.M42);
        var t = OffsetTransform(node).Translation;
        return new Vector2(t.X, t.Y);
    }

    /// <summary>What MeshGroups do to one node, in order (see the remarks on this class).</summary>
    private void ApplyMeshGroups(Node node, Node parent, Matrix4x4 rootLocal)
    {
        var i = node.Index;
        var source = _meshGroups.Source[i];
        var filter = source >= 0 && _groupActive[source] ? _meshGroups.Filter[i] : MeshGroupFilter.None;
        switch (filter)
        {
            case MeshGroupFilter.Translation:
                var before = _world[i];
                var anchorBefore = node is SimplePhysics physicsNode ? Anchor(physicsNode, ToPhysics(physicsNode, before)) : default;
                if (TranslateOrigin(node, parent, source))
                {
                    _world[i] = LocalMatrix(OffsetTransform(node)) * (node.LockToRoot ? rootLocal : _world[parent.Index]);
                    if (_physicsActive && _driverOf[i] is { } driver)
                        PhysicsNodeMoved(driver, anchorBefore);
                }

                break;
            case MeshGroupFilter.Vertices:
                MapVertices((Drawable)node, source, deformed: false);
                break;
        }

        // KLUDGE: a MeshGroup's triangles are taken before a dynamic MeshGroup above it maps its vertices, so a nested
        // MeshGroup ignores what the outer one does to it. nijilive does the same, and puppets are rigged against it.
        if (_triangleMaps[i] is { } maps)
            UpdateTriangleMaps((MeshGroup)node, maps);

        if (filter == MeshGroupFilter.DeformedVertices)
            MapVertices((Drawable)node, source, deformed: true);
    }

    /// <summary>
    /// Tells a physics node's driver that a MeshGroup moved its anchor (from <paramref name="previous"/>), so the next
    /// step keeps the moved anchor and maps through the moved matrix.
    /// KLUDGE: physics has already stepped this update from the unmoved anchor, so the move reaches the pendulum one
    /// update late, as in nijilive.
    /// </summary>
    private void PhysicsNodeMoved(PhysicsDriver driver, Vector2 previous)
    {
        var node = driver.Node;
        var toPhysics = ToPhysics(node, _world[node.Index]);
        var anchor = Anchor(node, toPhysics);
        if (anchor != previous)
            driver.AnchorMoved(anchor, toPhysics);
    }

    /// <summary>
    /// Each of a MeshGroup's triangles' maps from rest to deformed position, and whether its world matrix can be inverted
    /// (if not, it acts on nothing this evaluation).
    /// </summary>
    private void UpdateTriangleMaps(MeshGroup group, Matrix3x2[] maps)
    {
        var i = group.Index;
        var shape = _meshGroups.Shapes[i]!;
        var vertices = group.Mesh.VertexArray;
        var deform = _offsets.Deform[i]!;
        var indices = shape.Indices;
        for (var t = 0; t < maps.Length; t++)
        {
            if (!shape.Valid[t])
                continue;
            int a = indices[3 * t], b = indices[3 * t + 1], c = indices[3 * t + 2];
            maps[t] = shape.RestInverse[t] * MeshGroupShape.Basis(vertices[a] + deform[a], vertices[b] + deform[b], vertices[c] + deform[c]);
        }

        _groupActive[i] = Matrix4x4.Invert(_world[i], out _groupInverse[i]);
    }

    /// <summary>
    /// Deforms a drawable with a MeshGroup: its vertices (with its deformation, for a dynamic MeshGroup) in the MeshGroup's
    /// space are mapped by the triangle they fall in, and the change, brought back into the drawable's space, is added to
    /// its deformation. Vertices outside the MeshGroup's mesh stay where they are.
    /// </summary>
    /// <remarks>
    /// RISK: the mesh vertices are used without subtracting the mesh origin, as nijilive uses them. That is right only
    /// while the origin is zero, which the editors write; a puppet with another origin would deform from the wrong place.
    /// </remarks>
    private void MapVertices(Drawable drawable, int group, bool deformed)
    {
        var i = drawable.Index;
        var toGroup = _world[i] * _groupInverse[group];
        if (!Matrix4x4.Invert(toGroup, out var back))
            return;
        var shape = _meshGroups.Shapes[group]!;
        var maps = _triangleMaps[group]!;
        var vertices = drawable.Mesh.VertexArray;
        var deform = _offsets.Deform[i]!;
        for (var v = 0; v < vertices.Length; v++)
        {
            var p = Vector2.Transform(vertices[v], toGroup);
            if (deformed)
                p += Vector2.TransformNormal(deform[v], toGroup);
            var t = shape.Lookup(p);
            if (t < 0 || !shape.Valid[t])
                continue;
            deform[v] += Vector2.TransformNormal(Vector2.Transform(p, maps[t]) - p, back);
        }
    }

    /// <summary>
    /// Moves a node that is not drawable with a MeshGroup (<c>translate_children</c>): its origin in the MeshGroup's space is mapped like a vertex, and the change is added to its translation offset. A dynamic MeshGroup
    /// maps the origin with the parameter offset included; otherwise the rest origin. Returns whether the node moved.
    /// </summary>
    private bool TranslateOrigin(Node node, Node parent, int group)
    {
        var i = node.Index;
        // KLUDGE: the parent's matrix, even for a node locked to the root, as nijilive has it.
        var toGroup = _world[parent.Index] * _groupInverse[group];
        if (!Matrix4x4.Invert(toGroup, out var back))
            return false;
        var shape = _meshGroups.Shapes[group]!;
        var offset = _offsets.Translation[i];
        var p = Vector2.Transform(new Vector2(node.Transform.Translation.X, node.Transform.Translation.Y), toGroup);
        if (((MeshGroup)_nodes[group]).DynamicDeformation)
            p += Vector2.TransformNormal(new Vector2(offset.X, offset.Y), toGroup);
        var t = shape.Lookup(p);
        if (t < 0 || !shape.Valid[t])
            return false;
        var delta = Vector2.TransformNormal(Vector2.Transform(p, _triangleMaps[group]![t]) - p, back);
        _offsets.Translation[i] = new Vector3(offset.X + delta.X, offset.Y + delta.Y, offset.Z);
        return true;
    }

    /// <summary>The node's transform with its parameter offsets: translation and rotation added, scale multiplied.</summary>
    private NodeTransform OffsetTransform(Node node)
    {
        var t = node.Transform;
        var i = node.Index;
        return new NodeTransform(t.Translation + _offsets.Translation[i], t.Rotation + _offsets.Rotation[i], t.Scale * _offsets.Scale[i]);
    }

    /// <summary>The tint times its offset, clamped to 0..1 (a NaN offset leaves the tint as is).</summary>
    private static Vector3 ClampedTint(Vector3 tint, Vector3 offset) => new(
        float.IsNaN(offset.X) ? tint.X : Math.Clamp(tint.X * offset.X, 0f, 1f),
        float.IsNaN(offset.Y) ? tint.Y : Math.Clamp(tint.Y * offset.Y, 0f, 1f),
        float.IsNaN(offset.Z) ? tint.Z : Math.Clamp(tint.Z * offset.Z, 0f, 1f));

    /// <summary>
    /// A node's local matrix: scale, then rotation (x, then y, then z), then translation. The format's
    /// order is <c>T * Rz * Ry * Rx * S</c> for column vectors; System.Numerics multiplies row vectors, so the order reverses.
    /// </summary>
    internal static Matrix4x4 LocalMatrix(NodeTransform t) =>
        Matrix4x4.CreateScale(t.Scale.X, t.Scale.Y, 1f)
        * Matrix4x4.CreateRotationX(t.Rotation.X)
        * Matrix4x4.CreateRotationY(t.Rotation.Y)
        * Matrix4x4.CreateRotationZ(t.Rotation.Z)
        * Matrix4x4.CreateTranslation(t.Translation);

    private void BuildDrawList()
    {
        foreach (var order in _childOrder)
            SortByZSort(order);

        _rootPass.Clear();
        _rootComposites.Clear();
        var sequence = 0;
        CollectRootPass(_puppet.Root, ref sequence);
        foreach (var composite in _rootComposites) // Composites are queued after every Part, so they draw last among ties
            _rootPass.Add(new Entry(composite, _zSort[composite.Index], sequence++));
        _rootPass.Sort(CompareEntries);

        _drawList.Clear();
        foreach (var entry in _rootPass)
            Emit(entry.Node);
    }

    private void CollectRootPass(Node node, ref int sequence)
    {
        if (node is Composite composite)
        {
            if (_visible[node.Index])
                _rootComposites.Add(composite);
            return; // its subtree is drawn in its own pass
        }

        if (node is Part && _visible[node.Index])
            _rootPass.Add(new Entry(node, _zSort[node.Index], sequence++));

        foreach (var child in _childOrder[node.Index])
            CollectRootPass(_nodes[child], ref sequence);
    }

    private void Emit(Node node)
    {
        if (node is not Composite composite)
        {
            _drawList.Add(_items[node.Index]!);
            return;
        }

        _drawList.Add(_items[node.Index]!);
        var pass = _passPool.Count > 0 ? _passPool.Pop() : [];
        var members = _compositeMembers[composite];
        for (var i = 0; i < members.Length; i++)
        {
            if (_visible[members[i].Index])
                pass.Add(new Entry(members[i], _zSort[members[i].Index], i));
        }

        pass.Sort(CompareEntries);
        foreach (var entry in pass)
            Emit(entry.Node);
        pass.Clear();
        _passPool.Push(pass);
        _drawList.Add(_endItems[node.Index]!);
    }

    private void UpdateItems()
    {
        var min = new Vector2(float.PositiveInfinity);
        var max = new Vector2(float.NegativeInfinity);
        foreach (var item in _drawList)
        {
            var i = item.Node.Index;
            item.ZSort = _zSort[i];
            if (item.Part is not { } part)
            {
                // A Composite's values combine with its offsets the same way a Part's do.
                var composite = item.Composite!;
                SetColours(item, i, composite.Opacity, composite.Tint, composite.ScreenTint, composite.MaskThreshold);
                continue;
            }

            var positions = item.PositionArray;
            ComputePositions(part, positions);
            if (item.Texture is not null)
            {
                foreach (var p in positions)
                {
                    min = Vector2.Min(min, p);
                    max = Vector2.Max(max, p);
                }
            }

            SetColours(item, i, part.Opacity, part.Tint, part.ScreenTint, part.MaskThreshold);
            item.EmissionStrength = part.EmissionStrength * _offsets.EmissionStrength[i];
        }

        Bounds = min.X <= max.X ? new PuppetBounds(min, max) : default;
    }

    /// <summary>An item's opacity, tints and mask threshold: the node's own values with its parameter offsets, clamped.</summary>
    private void SetColours(DrawItem item, int i, float opacity, Vector3 tint, Vector3 screenTint, float maskThreshold)
    {
        item.Opacity = Math.Clamp(opacity * _offsets.Opacity[i], 0f, 1f);
        item.Tint = ClampedTint(tint, _offsets.Tint[i]);
        var screen = _offsets.ScreenTint[i];
        item.ScreenTint = new Vector3(
            float.IsNaN(screen.X) ? screenTint.X : Math.Clamp(screenTint.X + screen.X, 0f, 1f),
            float.IsNaN(screen.Y) ? screenTint.Y : Math.Clamp(screenTint.Y + screen.Y, 0f, 1f),
            float.IsNaN(screen.Z) ? screenTint.Z : Math.Clamp(screenTint.Z + screen.Z, 0f, 1f));
        item.MaskThreshold = Math.Clamp(maskThreshold + _offsets.AlphaThreshold[i], 0f, 1f);
    }

    /// <summary>
    /// Every mask source's shape, drawn or not. A Part's threshold is its own <c>mask_threshold</c> with its offset, not
    /// the masked item's.
    /// </summary>
    private void UpdateMaskSources()
    {
        foreach (var source in _maskSources)
        {
            ComputePositions(source.Node, source.PositionArray);
            if (source.Node is Part part)
                source.Threshold = Math.Clamp(part.MaskThreshold + _offsets.AlphaThreshold[part.Index], 0f, 1f);
        }
    }

    /// <summary>A drawable's vertices in puppet space.</summary>
    private void ComputePositions(Drawable drawable, Vector2[] target)
    {
        var i = drawable.Index;
        var world = _world[i];
        var origin = drawable.Mesh.Origin;
        var source = drawable.Mesh.Vertices;
        var deform = _offsets.Deform[i];
        for (var v = 0; v < source.Length; v++)
        {
            // The deformation is added and the mesh origin subtracted before the world matrix.
            var local = source[v] - origin;
            if (deform is not null)
                local += deform[v];
            target[v] = Vector2.Transform(local, world);
        }
    }

    /// <summary>Stable insertion sort of child indices by z-sort, larger first. Child lists are short.</summary>
    private void SortByZSort(int[] order)
    {
        for (var i = 1; i < order.Length; i++)
        {
            var current = order[i];
            var z = _zSort[current];
            var j = i - 1;
            while (j >= 0 && _zSort[order[j]] < z)
            {
                order[j + 1] = order[j];
                j--;
            }

            order[j + 1] = current;
        }
    }

    private static Composite? EnclosingComposite(Node node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is Composite composite)
                return composite;
        }

        return null;
    }

    /// <summary>
    /// The Parts and nested Composites a Composite draws, in gathering order: depth first through its subtree,
    /// into Parts' and Masks' children, into enabled plain nodes, but not into nested Composites (they gather their own).
    /// </summary>
    private static Node[] GatherCompositeMembers(Composite composite)
    {
        var members = new List<Node>();
        foreach (var child in composite.Children)
            Gather(child);
        return [.. members];

        void Gather(Node node)
        {
            switch (node)
            {
                case Composite:
                    members.Add(node);
                    return;
                case Part:
                    members.Add(node);
                    break;
                case Mask:
                    break;
                default:
                    if (!node.Enabled)
                        return;
                    break;
            }

            foreach (var child in node.Children)
                Gather(child);
        }
    }

    private readonly record struct Entry(Node Node, float ZSort, int Sequence);

    // Larger z-sort first; ties keep their sequence.
    private static readonly Comparison<Entry> CompareEntries = static (a, b) =>
    {
        var byZ = b.ZSort.CompareTo(a.ZSort);
        return byZ != 0 ? byZ : a.Sequence.CompareTo(b.Sequence);
    };
}

/// <summary>One evaluation's physics inputs.</summary>
/// <param name="DeltaSeconds">The instance's time step.</param>
/// <param name="ScenePosition">Where the host has placed the puppet, in puppet units.</param>
/// <param name="Reset">Whether every physics node starts again at rest before stepping.</param>
internal readonly record struct PhysicsStep(float DeltaSeconds, Vector2 ScenePosition, bool Reset);
