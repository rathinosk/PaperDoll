// The SimplePhysics simulation of one node in one instance: a rigid or spring pendulum hung from the node's origin,
// stepped with classic Runge-Kutta in ticks of at most 0.01 s, using the node's gravity, length, frequency and damping
// as its bindings leave them. After each step the bob's position is mapped onto the driven parameter by the node's map
// mode and output scale.

using System.Numerics;

namespace PaperDoll.Evaluation;

/// <summary>
/// The offsets bindings put on one SimplePhysics node's settings, rebuilt on every evaluation: length adds up, the other
/// settings multiply.
/// </summary>
internal struct PhysicsOffsets
{
    public float Gravity;
    public float Length;
    public float Frequency;
    public float AngleDamping;
    public float LengthDamping;
    public Vector2 OutputScale;

    public static PhysicsOffsets Default => new()
    {
        Gravity = 1f,
        Frequency = 1f,
        AngleDamping = 1f,
        LengthDamping = 1f,
        OutputScale = Vector2.One,
    };
}

/// <summary>
/// The simulation of one SimplePhysics node in one instance: a pendulum hung from the node's origin
/// (the anchor), stepped in Runge-Kutta ticks of at most 0.01 s, whose bob is mapped back onto the driven parameter.
/// Positions are in physics space: puppet space moved by the host's scene position (or, for a node with
/// <c>local_only</c>, the node's parent space).
/// </summary>
/// <remarks>
/// HACK: a failed step or mapping (anything not finite) leaves the previous state and drives nothing that update.
/// KLUDGE: a <c>local_only</c> node maps the bob's direction from its parent's origin, not from the anchor. When a
/// MeshGroup moved the node during the last evaluation, the next step keeps that anchor and maps through that matrix.
/// Both as nijilive has them; puppets are tuned against them.
/// </remarks>
internal sealed class PhysicsDriver
{
    private const float MaxDelta = 10f;  // HACK: a longer step counts as 10 s, which bounds the ticks after a stall
    private const float Tick = 0.01f;    // the longest single integration step
    private const float Epsilon = 1.1920929e-7f; // D's float.epsilon (machine epsilon), not .NET's float.Epsilon

    private readonly float _gravityScale;

    // Settings for the current update: the node's values with this evaluation's offsets.
    private float _gravity;
    private float _length;
    private float _frequency;
    private float _angleDamping;
    private float _lengthDamping;

    // Pendulum state.
    private float _angle;
    private float _dAngle;

    // Spring pendulum state (the bob is shared with the pendulum).
    private Vector2 _bob;
    private Vector2 _dBob;
    private Vector2 _lastValidDirection = new(0, 1);

    private bool _prevAnchorSet;
    private Matrix4x4 _prevInverse;

    public PhysicsDriver(SimplePhysics node, Parameter parameter, PuppetPhysicsSettings settings)
    {
        Node = node;
        Parameter = parameter;
        _gravityScale = settings.Gravity * settings.PixelsPerMeter;

        // A driver runs only while it and its ancestors are enabled; nothing changes that after loading.
        Enabled = true;
        for (Node? n = node; n is not null; n = n.Parent)
            Enabled &= n.Enabled;
    }

    public SimplePhysics Node { get; }

    public Parameter Parameter { get; }

    /// <summary>Whether the node and all its ancestors are enabled; a disabled driver never runs.</summary>
    public bool Enabled { get; }

    /// <summary>The pendulum's pivot, in physics space.</summary>
    public Vector2 Anchor { get; private set; }

    /// <summary>The bob after the last tick, in physics space (NaN until the first tick).</summary>
    public Vector2 Output { get; private set; } = new(float.NaN);

    /// <summary>Starts again at rest: the bob hangs below <paramref name="anchor"/>, with no velocity.</summary>
    public void Reset(Vector2 anchor)
    {
        if (float.IsFinite(anchor.X) && float.IsFinite(anchor.Y))
            Anchor = anchor;
        _bob = Anchor + new Vector2(0, Node.Length); // the node's own length, without this update's offsets
        _dBob = Vector2.Zero;
        _angle = 0;
        _dAngle = 0;
        _lastValidDirection = new Vector2(0, 1);
        _prevAnchorSet = false;
        Output = new Vector2(float.NaN);
    }

    /// <summary>
    /// A MeshGroup moved the node, so the next step keeps this anchor and maps through this matrix instead of the ones it
    /// computes.
    /// </summary>
    public void AnchorMoved(Vector2 anchor, Matrix4x4 toPhysics)
    {
        if (!float.IsFinite(anchor.X) || !float.IsFinite(anchor.Y))
            return;
        Anchor = anchor;
        Matrix4x4.Invert(toPhysics, out _prevInverse);
        _prevAnchorSet = true;
    }

    /// <summary>
    /// Advances the simulation by <paramref name="deltaSeconds"/>, in ticks of at most 0.01 s, and returns the value
    /// to force on the parameter, or <see langword="null"/> if the step or the mapping failed (nothing is driven then).
    /// </summary>
    /// <param name="deltaSeconds">The instance's time step.</param>
    /// <param name="offsets">This evaluation's offsets on the node's settings.</param>
    /// <param name="anchor">The node's origin in physics space as the parameters leave it (without MeshGroups).</param>
    /// <param name="toPhysics">The node's matrix into physics space, used to map the bob back into the node's space.</param>
    public Vector2? Step(float deltaSeconds, in PhysicsOffsets offsets, Vector2 anchor, Matrix4x4 toPhysics)
    {
        _gravity = Node.Gravity * offsets.Gravity * _gravityScale;
        _length = Node.Length + offsets.Length;
        _frequency = Node.Frequency * offsets.Frequency;
        _angleDamping = Node.AngleDamping * offsets.AngleDamping;
        _lengthDamping = Node.LengthDamping * offsets.LengthDamping;
        var outputScale = Node.OutputScale * offsets.OutputScale;

        if (!_prevAnchorSet && float.IsFinite(anchor.X) && float.IsFinite(anchor.Y))
            Anchor = anchor;

        var h = MathF.Min(deltaSeconds, MaxDelta);
        while (h > Tick)
        {
            Advance(Tick);
            h -= Tick;
        }

        Advance(h);
        var value = MapOutput(outputScale, toPhysics);
        _prevAnchorSet = false;
        return value;
    }

    /// <summary>
    /// The bob as a parameter value, by the node's map mode: its direction from the anchor (as an offset or an angle) and
    /// its distance relative to the pendulum's length, times the output scale.
    /// </summary>
    private Vector2? MapOutput(Vector2 outputScale, Matrix4x4 toPhysics)
    {
        var output = Output;
        if (!IsFinite(output) || !IsFinite(Anchor) || !IsFinite(outputScale))
            return null;

        // The bob in the node's own space gives the angle; the length is measured in physics space.
        Vector2 local;
        if (Node.LocalOnly)
            local = output;
        else if (_prevAnchorSet)
            local = Vector2.Transform(output, _prevInverse);
        else if (Matrix4x4.Invert(toPhysics, out var inverse))
            local = Vector2.Transform(output, inverse);
        else
            return null;

        var localLength = local.Length();
        if (!float.IsFinite(localLength) || localLength <= Epsilon)
            return null;
        var direction = local / localLength;

        if (!float.IsFinite(_length) || MathF.Abs(_length) <= Epsilon)
            return null;
        var relativeLength = Vector2.Distance(output, Anchor) / _length;
        if (!float.IsFinite(relativeLength))
            return null;

        Vector2 value;
        switch (Node.MapMode)
        {
            case PhysicsMapMode.XY:
            case PhysicsMapMode.YX:
                value = direction * relativeLength - new Vector2(0, 1);
                value.Y = -value.Y; // parameters have Y up
                if (Node.MapMode == PhysicsMapMode.YX)
                    value = new Vector2(value.Y, value.X);
                break;
            default:
                var angle = MathF.Atan2(-direction.X, direction.Y) / MathF.PI;
                value = Node.MapMode == PhysicsMapMode.LengthAngle ? new Vector2(relativeLength, angle) : new Vector2(angle, relativeLength);
                break;
        }

        value *= outputScale;
        return IsFinite(value) ? value : null;
    }

    /// <summary>One tick of the node's model: the spring pendulum moves its bob; the rigid pendulum swings its angle at a fixed length.</summary>
    private void Advance(float h)
    {
        if (Node.Model == PhysicsModel.SpringPendulum)
        {
            Span<float> state = [_bob.X, _bob.Y, _dBob.X, _dBob.Y];
            Integrate(state, h);
            _bob = new Vector2(state[0], state[1]);
            _dBob = new Vector2(state[2], state[3]);
            if (!IsFinite(_bob) || !IsFinite(_dBob) || !IsFinite(Anchor))
                return;
            Output = _bob;
            return;
        }

        // The rigid pendulum works in terms of its angle, measured against the anchor as it is now.
        var offset = _bob - Anchor;
        if (!IsFinite(offset))
            return;
        _angle = MathF.Atan2(-offset.X, offset.Y);
        if (!float.IsFinite(_angle))
            return;

        Span<float> pendulum = [_angle, _dAngle, 0, 0];
        Integrate(pendulum, h);
        _angle = pendulum[0];
        _dAngle = pendulum[1];
        if (!float.IsFinite(_angle) || !float.IsFinite(_dAngle))
            return;

        var unit = new Vector2(-MathF.Sin(_angle), MathF.Cos(_angle));
        if (!float.IsFinite(_length) || MathF.Abs(_length) <= Epsilon)
            return;
        var bob = Anchor + unit * _length;
        if (!IsFinite(bob))
            return;
        _bob = bob;
        Output = bob;
    }

    /// <summary>
    /// One classic (fourth-order) Runge-Kutta step of <paramref name="h"/> seconds. If any result
    /// is not finite, the state is left as it was.
    /// </summary>
    private void Integrate(Span<float> state, float h)
    {
        Span<float> k1 = stackalloc float[4];
        Span<float> k2 = stackalloc float[4];
        Span<float> k3 = stackalloc float[4];
        Span<float> k4 = stackalloc float[4];
        Span<float> probe = stackalloc float[4];

        Derive(state, k1);
        for (var i = 0; i < 4; i++)
            probe[i] = state[i] + h * k1[i] / 2f;
        Derive(probe, k2);
        for (var i = 0; i < 4; i++)
            probe[i] = state[i] + h * k2[i] / 2f;
        Derive(probe, k3);
        for (var i = 0; i < 4; i++)
            probe[i] = state[i] + h * k3[i];
        Derive(probe, k4);

        for (var i = 0; i < 4; i++)
        {
            probe[i] = state[i] + h * (k1[i] + 2 * k2[i] + 2 * k3[i] + k4[i]) / 6f;
            if (!float.IsFinite(probe[i]))
                return; // the step failed; keep the state as it was
        }

        probe.CopyTo(state);
    }

    private void Derive(ReadOnlySpan<float> state, Span<float> derivative)
    {
        derivative.Clear();
        if (Node.Model == PhysicsModel.SpringPendulum)
            DeriveSpringPendulum(state, derivative);
        else
            DerivePendulum(state, derivative);
    }

    /// <summary>
    /// The rigid pendulum's derivative. State is (angle, angular velocity): gravity over length pulls the angle back, and
    /// angle damping is a share of its critical damping.
    /// </summary>
    private void DerivePendulum(ReadOnlySpan<float> state, Span<float> derivative)
    {
        float angle = state[0], dAngle = state[1];
        derivative[0] = dAngle;
        if (!float.IsFinite(_gravity) || !float.IsFinite(_length) || MathF.Abs(_length) <= Epsilon)
            return;

        var lengthRatio = _gravity / _length;
        if (!float.IsFinite(lengthRatio) || lengthRatio < 0)
            return;
        var critDamp = 2 * MathF.Sqrt(lengthRatio);
        if (!float.IsFinite(critDamp) || !float.IsFinite(_angleDamping))
            return;

        var dd = -lengthRatio * MathF.Sin(angle);
        if (!float.IsFinite(dd))
            dd = 0;
        dd -= dAngle * _angleDamping * critDamp;
        if (!float.IsFinite(dd))
            dd = 0;
        derivative[1] = dd;
    }

    /// <summary>
    /// The spring pendulum's derivative. State is (bob, bob velocity). A spring towards the anchor, gravity, and
    /// damping split into the angular and the radial direction, each as a share of its critical damping.
    /// </summary>
    private void DeriveSpringPendulum(ReadOnlySpan<float> state, Span<float> derivative)
    {
        var bob = new Vector2(state[0], state[1]);
        var dBob = new Vector2(state[2], state[3]);
        derivative[0] = dBob.X;
        derivative[1] = dBob.Y;

        // These are normalised against the mass.
        if (!float.IsFinite(_frequency) || MathF.Abs(_frequency) <= Epsilon)
            return;
        var springKsqrt = _frequency * 2 * MathF.PI;
        var springK = springKsqrt * springKsqrt;
        if (!float.IsFinite(springKsqrt) || !float.IsFinite(springK) || MathF.Abs(springK) <= Epsilon)
            return;
        var g = _gravity;
        if (!float.IsFinite(g) || !float.IsFinite(_length) || MathF.Abs(_length) <= Epsilon)
            return;
        var restLength = _length - g / springK;
        if (!float.IsFinite(restLength))
            return;

        var offset = bob - Anchor;
        if (!IsFinite(offset))
            return;
        var offsetLength = offset.Length();
        Vector2 direction;
        if (!float.IsFinite(offsetLength))
            return;
        if (offsetLength <= Epsilon)
        {
            direction = IsFinite(_lastValidDirection) ? _lastValidDirection : new Vector2(0, 1);
        }
        else
        {
            direction = offset / offsetLength;
            if (!IsFinite(direction))
                return;
            _lastValidDirection = direction;
        }

        var lengthRatio = g / _length;
        if (!float.IsFinite(lengthRatio) || lengthRatio < 0)
            return;
        var critDampAngle = 2 * MathF.Sqrt(lengthRatio);
        var critDampLength = 2 * springKsqrt;
        if (!float.IsFinite(critDampAngle) || !float.IsFinite(critDampLength))
            return;

        var dist = MathF.Abs(offsetLength);
        var ddBob = new Vector2(0, g) - direction * (dist - restLength) * springK;
        if (!IsFinite(ddBob))
            ddBob = Vector2.Zero;

        // The velocity in the frame of the spring: x across it, y along it.
        var dBobRot = new Vector2(
            dBob.X * direction.Y + dBob.Y * direction.X,
            dBob.Y * direction.Y - dBob.X * direction.X);

        if (!float.IsFinite(_angleDamping) || !float.IsFinite(_lengthDamping))
            return;
        var ddBobRot = -new Vector2(
            dBobRot.X * _angleDamping * critDampAngle,
            dBobRot.Y * _lengthDamping * critDampLength);
        if (!IsFinite(ddBobRot))
            ddBobRot = Vector2.Zero;

        // KLUDGE: the second terms rotate back with dBobRot.y and dBobRot.x, not ddBobRot, which looks like a slip but is
        // what every Inochi2D runtime does; puppets are tuned against it, so it is kept.
        var ddBobDamping = new Vector2(
            ddBobRot.X * direction.Y - dBobRot.Y * direction.X,
            ddBobRot.Y * direction.Y + dBobRot.X * direction.X);
        if (!IsFinite(ddBobDamping))
            ddBobDamping = Vector2.Zero;

        ddBob += ddBobDamping;
        if (!IsFinite(ddBob))
            ddBob = Vector2.Zero;
        derivative[2] = ddBob.X;
        derivative[3] = ddBob.Y;
    }

    private static bool IsFinite(Vector2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
}
