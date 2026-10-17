// Turns parameter values into per-node offsets. Each parameter's value, merged with any animation inputs, is placed on
// its keyform grid; each of its bindings is interpolated there, and the result is added to or multiplied into the
// target node's offsets: translation, rotation, scale, z-sort, opacity, tints, deforms, and the settings of physics
// nodes. The offsets are rebuilt from their starting values on every evaluation, and nothing is allocated.

using System.Numerics;

namespace PaperDoll.Evaluation;

/// <summary>The node properties a binding can drive (Stages 2, 3 and 5).</summary>
internal enum BoundProperty : byte
{
    TranslationX, TranslationY, TranslationZ,
    RotationX, RotationY, RotationZ,
    ScaleX, ScaleY,
    ZSort,
    Opacity,
    TintR, TintG, TintB,
    ScreenTintR, ScreenTintG, ScreenTintB,
    EmissionStrength,
    AlphaThreshold,
    Deform,
    PhysicsGravity, PhysicsLength, PhysicsFrequency, PhysicsAngleDamping, PhysicsLengthDamping,
    PhysicsOutputScaleX, PhysicsOutputScaleY,
}

/// <summary>
/// The offsets the bindings of one instance put on each node, by node index, rebuilt on every evaluation. Each starts at
/// its neutral value and combines with every binding on it: translation, rotation, z-sort, screen tint and emission
/// strength add up; scale, opacity, tint and alpha threshold multiply. On SimplePhysics nodes, length adds up and the
/// other settings multiply.
/// </summary>
/// <remarks>
/// KLUDGE: the alpha threshold offset starts at 0 and is multiplied, so <c>alphaThreshold</c> bindings never change
/// anything; emission strength starts at 1 and is added to, then multiplies the Part's own. Both are what nijilive and
/// nicxlive do, so they are kept.
/// </remarks>
internal sealed class NodeOffsets
{
    public NodeOffsets(int count)
    {
        Translation = new Vector3[count];
        Rotation = new Vector3[count];
        Scale = new Vector2[count];
        ZSort = new float[count];
        Opacity = new float[count];
        Tint = new Vector3[count];
        ScreenTint = new Vector3[count];
        EmissionStrength = new float[count];
        AlphaThreshold = new float[count];
        Deform = new Vector2[count][];
        Physics = new PhysicsOffsets[count];
    }

    public Vector3[] Translation { get; }
    public Vector3[] Rotation { get; }
    public Vector2[] Scale { get; }
    public float[] ZSort { get; }
    public float[] Opacity { get; }
    public Vector3[] Tint { get; }
    public Vector3[] ScreenTint { get; }
    public float[] EmissionStrength { get; }
    public float[] AlphaThreshold { get; }

    /// <summary>
    /// Per-vertex deformation of each drawable that has <c>deform</c> bindings or a MeshGroup acting on it, or is a
    /// MeshGroup with triangles; otherwise <see langword="null"/>. Bindings add to it, then MeshGroups.
    /// </summary>
    public Vector2[]?[] Deform { get; }

    /// <summary>Offsets on SimplePhysics settings; unused on other nodes.</summary>
    public PhysicsOffsets[] Physics { get; }

    public void Reset()
    {
        Array.Clear(Translation);
        Array.Clear(Rotation);
        Array.Fill(Scale, Vector2.One);
        Array.Clear(ZSort);
        Array.Fill(Opacity, 1f);
        Array.Fill(Tint, Vector3.One);
        Array.Clear(ScreenTint);
        Array.Fill(EmissionStrength, 1f);
        Array.Clear(AlphaThreshold);
        Array.Fill(Physics, PhysicsOffsets.Default);
        foreach (var deform in Deform)
        {
            if (deform is not null)
                Array.Clear(deform);
        }
    }
}

/// <summary>
/// Evaluates every parameter of one instance into <see cref="NodeOffsets"/>: each parameter's value is merged with its
/// inputs, mapped to 0..1, located between its axis points, and each binding adds its interpolated keyform to its target.
/// Everything is prepared when the instance is created; evaluating allocates nothing.
/// </summary>
internal sealed class BindingEvaluator
{
    private readonly Puppet _puppet;
    private readonly Compiled[][] _bindings; // by parameter index
    private readonly ParameterInputs[] _inputs;
    private readonly Vector2[] _merged;      // by parameter index: the value last applied, inputs merged

    public BindingEvaluator(Puppet puppet, NodeOffsets offsets)
    {
        _puppet = puppet;
        Offsets = offsets;
        _inputs = new ParameterInputs[puppet.Parameters.Count];
        _merged = new Vector2[puppet.Parameters.Count];
        _bindings = new Compiled[puppet.Parameters.Count][];
        foreach (var parameter in puppet.Parameters)
        {
            var list = new List<Compiled>();
            foreach (var binding in parameter.Bindings)
            {
                if (Compile(parameter, binding) is { } compiled)
                    list.Add(compiled);
            }

            _bindings[parameter.Index] = [.. list];
        }
    }

    public NodeOffsets Offsets { get; }

    public ref ParameterInputs Inputs(int parameterIndex) => ref _inputs[parameterIndex];

    /// <summary>Clears every parameter's inputs, including those of parameters that were not applied.</summary>
    public void ClearInputs() => Array.Clear(_inputs);

    /// <summary>The value a parameter was last applied at, its inputs merged (or the value given, if it was skipped).</summary>
    public Vector2 Merged(int parameterIndex) => _merged[parameterIndex];

    /// <summary>
    /// Resets the offsets and applies every parameter, in the puppet's order. Parameters marked
    /// in <paramref name="skip"/> are left for their physics driver to apply (<see cref="ApplyParameter"/>).
    /// </summary>
    public void Evaluate(Vector2[] values, bool[]? skip = null)
    {
        Offsets.Reset();
        for (var p = 0; p < values.Length; p++)
        {
            if (skip is null || !skip[p])
                ApplyParameter(p, values[p]);
            else
                _merged[p] = values[p];
        }
    }

    /// <summary>Applies one parameter: its value merged with its inputs (which are then cleared), through its bindings.</summary>
    public void ApplyParameter(int p, Vector2 value)
    {
        var parameter = _puppet.Parameters[p];
        var merged = _inputs[p].Resolve(value);
        _inputs[p].Clear();
        _merged[p] = merged;

        var bindings = _bindings[p];
        if (bindings.Length == 0)
            return;

        var normalized = Keyforms.Normalize(merged, parameter.Min, parameter.Max);
        var x = Keyforms.Locate(parameter.GetAxisPoints(0), normalized.X);
        var y = parameter.IsVec2 ? Keyforms.Locate(parameter.GetAxisPoints(1), normalized.Y) : default;
        var xCount = parameter.GetAxisPointCount(0);
        var yCount = parameter.GetAxisPointCount(1);

        foreach (var binding in bindings)
        {
            var wx = Keyforms.Weights(binding.Mode, xCount, x, active: true);
            var wy = Keyforms.Weights(binding.Mode, yCount, y, active: parameter.IsVec2);
            if (binding.Property == BoundProperty.Deform)
                ApplyDeform(binding, in wx, in wy);
            else
                Apply(binding.Node, binding.Property, Sample(binding.Values!, in wx, in wy));
        }
    }

    private static float Sample(float[][] values, in AxisWeights wx, in AxisWeights wy)
    {
        var sum = 0f;
        for (var i = 0; i < wx.Count; i++)
        {
            var row = values[wx.Index[i]];
            var inner = 0f;
            for (var j = 0; j < wy.Count; j++)
                inner += row[wy.Index[j]] * wy.Weight[j];
            sum += inner * wx.Weight[i];
        }

        return sum;
    }

    private void ApplyDeform(Compiled binding, in AxisWeights wx, in AxisWeights wy)
    {
        var target = Offsets.Deform[binding.Node]!;
        var keyforms = binding.Offsets!;
        for (var i = 0; i < wx.Count; i++)
        {
            for (var j = 0; j < wy.Count; j++)
            {
                var weight = wx.Weight[i] * wy.Weight[j];
                if (weight == 0)
                    continue;
                var source = keyforms[wx.Index[i]][wy.Index[j]];
                for (var v = 0; v < target.Length; v++)
                    target[v] += source[v] * weight;
            }
        }
    }

    /// <summary>Adds or multiplies one binding's value into a node's offset. Non-finite values are ignored.</summary>
    private void Apply(int node, BoundProperty property, float value)
    {
        if (!float.IsFinite(value))
            return;

        var o = Offsets;
        switch (property)
        {
            case BoundProperty.TranslationX: o.Translation[node].X += value; break;
            case BoundProperty.TranslationY: o.Translation[node].Y += value; break;
            case BoundProperty.TranslationZ: o.Translation[node].Z += value; break;
            case BoundProperty.RotationX: o.Rotation[node].X += value; break;
            case BoundProperty.RotationY: o.Rotation[node].Y += value; break;
            case BoundProperty.RotationZ: o.Rotation[node].Z += value; break;
            case BoundProperty.ScaleX: o.Scale[node].X = ScaleBase(o.Scale[node].X) * value; break;
            case BoundProperty.ScaleY: o.Scale[node].Y = ScaleBase(o.Scale[node].Y) * value; break;
            case BoundProperty.ZSort: o.ZSort[node] += value; break;
            case BoundProperty.Opacity: o.Opacity[node] *= value; break;
            case BoundProperty.TintR: o.Tint[node].X *= value; break;
            case BoundProperty.TintG: o.Tint[node].Y *= value; break;
            case BoundProperty.TintB: o.Tint[node].Z *= value; break;
            case BoundProperty.ScreenTintR: o.ScreenTint[node].X += value; break;
            case BoundProperty.ScreenTintG: o.ScreenTint[node].Y += value; break;
            case BoundProperty.ScreenTintB: o.ScreenTint[node].Z += value; break;
            case BoundProperty.EmissionStrength: o.EmissionStrength[node] += value; break;
            case BoundProperty.AlphaThreshold: o.AlphaThreshold[node] *= value; break;
            case BoundProperty.PhysicsGravity: o.Physics[node].Gravity *= value; break;
            case BoundProperty.PhysicsLength: o.Physics[node].Length += value; break;
            case BoundProperty.PhysicsFrequency: o.Physics[node].Frequency *= value; break;
            case BoundProperty.PhysicsAngleDamping: o.Physics[node].AngleDamping *= value; break;
            case BoundProperty.PhysicsLengthDamping: o.Physics[node].LengthDamping *= value; break;
            case BoundProperty.PhysicsOutputScaleX: o.Physics[node].OutputScale.X *= value; break;
            case BoundProperty.PhysicsOutputScaleY: o.Physics[node].OutputScale.Y *= value; break;
        }

        // HACK: a zero (or non-finite) scale offset is reset to 1 before multiplying, so one zero keyform cannot pin the
        // scale at 0 for every later binding. nijilive does the same.
        static float ScaleBase(float current) => current == 0 || !float.IsFinite(current) ? 1f : current;
    }

    private Compiled? Compile(Parameter parameter, ParameterBinding binding)
    {
        if (binding is ParameterAxisBinding || !_puppet.TryGetNode(binding.TargetUuid, out var node))
            return null;
        if (Property(node, binding.PropertyName) is not { } property)
            return null; // unknown names, or names this node type does not take

        // Keyforms are read as stored, unset ones included (docs/design/format.md).
        var compiled = new Compiled(binding, node.Index, property);
        if (property == BoundProperty.Deform)
        {
            var deform = (DeformBinding)binding;
            var vertexCount = ((Drawable)node).Mesh.Vertices.Length;
            // A deformation whose length does not match the mesh is ignored (the loader reports it).
            foreach (var row in deform.OffsetArrays)
            {
                foreach (var keyform in row)
                {
                    if (keyform.Length != vertexCount)
                        return null;
                }
            }

            Offsets.Deform[node.Index] ??= new Vector2[vertexCount];
        }

        return compiled;
    }

    /// <summary>The property a binding drives on a node of this type, or <see langword="null"/> if the type does not take it.</summary>
    internal static BoundProperty? Property(Node node, string name)
    {
        BoundProperty? property = name switch
        {
            "transform.t.x" => BoundProperty.TranslationX,
            "transform.t.y" => BoundProperty.TranslationY,
            "transform.t.z" => BoundProperty.TranslationZ,
            "transform.r.x" => BoundProperty.RotationX,
            "transform.r.y" => BoundProperty.RotationY,
            "transform.r.z" => BoundProperty.RotationZ,
            "transform.s.x" => BoundProperty.ScaleX,
            "transform.s.y" => BoundProperty.ScaleY,
            "zSort" => BoundProperty.ZSort,
            _ => null,
        };
        if (property is not null)
            return property;

        // Every drawable (Part, MeshGroup, Mask) takes deform offsets.
        if (name == "deform")
            return node is Drawable ? BoundProperty.Deform : null;

        if (node is SimplePhysics)
        {
            return name switch
            {
                "gravity" => BoundProperty.PhysicsGravity,
                "length" => BoundProperty.PhysicsLength,
                "frequency" => BoundProperty.PhysicsFrequency,
                "angleDamping" => BoundProperty.PhysicsAngleDamping,
                "lengthDamping" => BoundProperty.PhysicsLengthDamping,
                "outputScale.x" => BoundProperty.PhysicsOutputScaleX,
                "outputScale.y" => BoundProperty.PhysicsOutputScaleY,
                _ => null,
            };
        }

        // A Composite is drawn like a Part, so it takes the same visual properties.
        if (node is not (Part or Composite))
            return null;
        return name switch
        {
            "opacity" => BoundProperty.Opacity,
            "tint.r" => BoundProperty.TintR,
            "tint.g" => BoundProperty.TintG,
            "tint.b" => BoundProperty.TintB,
            "screenTint.r" => BoundProperty.ScreenTintR,
            "screenTint.g" => BoundProperty.ScreenTintG,
            "screenTint.b" => BoundProperty.ScreenTintB,
            "emissionStrength" => BoundProperty.EmissionStrength,
            "alphaThreshold" => BoundProperty.AlphaThreshold,
            _ => null,
        };
    }

    private sealed class Compiled(ParameterBinding binding, int node, BoundProperty property)
    {
        public int Node { get; } = node;
        public BoundProperty Property { get; } = property;
        public InterpolateMode Mode { get; } = binding.Interpolation;
        public float[][]? Values { get; } = (binding as ValueBinding)?.ValueArrays;
        public Vector2[][][]? Offsets { get; } = (binding as DeformBinding)?.OffsetArrays;
    }
}
