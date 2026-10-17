// Parameters and their bindings as stored in INP1: each parameter's range, defaults, axis points and merge mode, and for
// each binding its keyform grid of values (ValueBinding), vertex offsets (DeformBinding) or another parameter's axis
// (ParameterAxisBinding, read but not evaluated). Evaluated by Evaluation/BindingEvaluator.cs.

using System.Numerics;

namespace PaperDoll;

/// <summary>A puppet parameter: a 1D or 2D control the host sets, with the bindings it drives.</summary>
public sealed class Parameter
{
    internal float[][] AxisPointArrays = [[0f, 1f], [0f]];

    internal Parameter() { }

    /// <summary>The parameter's position in <see cref="Puppet.Parameters"/>.</summary>
    public int Index { get; internal set; }

    /// <summary>The parameter's ID.</summary>
    public uint Uuid { get; internal set; }

    /// <summary>The parameter's name.</summary>
    public string Name { get; internal set; } = "";

    /// <summary>Whether the parameter has two axes.</summary>
    public bool IsVec2 { get; internal set; }

    /// <summary>Minimum value per axis.</summary>
    public Vector2 Min { get; internal set; } = Vector2.Zero;

    /// <summary>Maximum value per axis.</summary>
    public Vector2 Max { get; internal set; } = Vector2.One;

    /// <summary>Default value per axis.</summary>
    public Vector2 Defaults { get; internal set; }

    /// <summary>How this parameter's inputs combine.</summary>
    public ParameterMergeMode MergeMode { get; internal set; } = ParameterMergeMode.Additive;

    /// <summary>The parameter group's ID (<c>parentUUID</c>), if the file assigns one. Editor data.</summary>
    public uint? GroupUuid { get; internal set; }

    /// <summary>Keyform positions on an axis (0 for X, 1 for Y), normalised 0 to 1.</summary>
    public ReadOnlySpan<float> GetAxisPoints(int axis) => AxisPointArrays[axis];

    /// <summary>Number of keyform positions on an axis (0 for X, 1 for Y).</summary>
    public int GetAxisPointCount(int axis) => AxisPointArrays[axis].Length;

    /// <summary>
    /// The SimplePhysics node that drives this parameter, or <see langword="null"/>. While an instance's physics is on
    /// (<see cref="PuppetInstance.PhysicsEnabled"/>), the simulation replaces whatever value the host sets. If several
    /// nodes name this parameter, the last in the tree drives it.
    /// </summary>
    public SimplePhysics? PhysicsDriver { get; internal set; }

    /// <summary>The bindings this parameter drives, in file order.</summary>
    public IReadOnlyList<ParameterBinding> Bindings { get; internal set; } = [];

    /// <inheritdoc/>
    public override string ToString() => $"Parameter '{Name}' ({Uuid})";
}

/// <summary>A named group of parameters, for editors and tools. No effect at runtime.</summary>
/// <param name="Uuid">The group's ID, matched by <see cref="Parameter.GroupUuid"/>.</param>
/// <param name="Name">The group's name.</param>
/// <param name="Color">The group's colour (RGB).</param>
public sealed record ParameterGroup(uint Uuid, string Name, Vector3 Color);

/// <summary>A link from a parameter to one property of one node, with a keyform per axis point.</summary>
public abstract class ParameterBinding
{
    internal bool[][] IsSetArrays = [];

    internal ParameterBinding() { }

    /// <summary>The ID of the target node (or, for <see cref="ParameterAxisBinding"/>, the target parameter).</summary>
    public uint TargetUuid { get; internal set; }

    /// <summary>The bound property, as written in the file (for example <c>transform.t.x</c>, <c>deform</c>, <c>opacity</c>).</summary>
    public string PropertyName { get; internal set; } = "";

    /// <summary>Interpolation between keyforms.</summary>
    public InterpolateMode Interpolation { get; internal set; } = InterpolateMode.Linear;

    /// <summary>Number of keyforms along X (the parameter's X axis point count).</summary>
    public int XCount => IsSetArrays.Length;

    /// <summary>Number of keyforms along Y (the parameter's Y axis point count).</summary>
    public int YCount => IsSetArrays.Length == 0 ? 0 : IsSetArrays[0].Length;

    /// <summary>Whether the keyform at axis point (x, y) was set in the editor. Unset keyforms are filled in when evaluated.</summary>
    public bool IsSet(int x, int y) => IsSetArrays[x][y];
}

/// <summary>A binding to a scalar node property (transforms, z-sort, opacity, tints, physics settings).</summary>
public sealed class ValueBinding : ParameterBinding
{
    internal float[][] ValueArrays = [];

    internal ValueBinding() { }

    /// <summary>The keyform value at axis point (x, y).</summary>
    public float GetValue(int x, int y) => ValueArrays[x][y];
}

/// <summary>A binding of per-vertex offsets to a mesh (<c>deform</c>).</summary>
public sealed class DeformBinding : ParameterBinding
{
    internal Vector2[][][] OffsetArrays = [];

    internal DeformBinding() { }

    /// <summary>The vertex offsets of the keyform at axis point (x, y).</summary>
    public ReadOnlySpan<Vector2> GetOffsets(int x, int y) => OffsetArrays[x][y];
}

/// <summary>
/// A parameter-to-parameter binding: drives one axis (<c>X</c> or <c>Y</c>) of another parameter.
/// Read but not supported yet; <c>paperdoll validate</c> reports it.
/// </summary>
public sealed class ParameterAxisBinding : ParameterBinding
{
    internal float[][] ValueArrays = [];

    internal ParameterAxisBinding() { }

    /// <summary>The driven axis of the target parameter: 0 for X, 1 for Y.</summary>
    public int TargetAxis { get; internal set; }

    /// <summary>The keyform value at axis point (x, y).</summary>
    public float GetValue(int x, int y) => ValueArrays[x][y];
}
