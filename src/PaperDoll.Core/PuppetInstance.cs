using System.Numerics;
using PaperDoll.Evaluation;

namespace PaperDoll;

/// <summary>
/// One copy of a <see cref="Puppet"/> on screen. It holds everything that changes (parameter values, its own clock, its
/// physics state, its animation playbacks, its draw list); the puppet it was created from stays unchanged.
/// Instances never share state, so the same puppet can be shown several times. An instance is not thread-safe.
/// </summary>
public sealed class PuppetInstance
{
    private readonly Vector2[] _parameterValues;  // as the host set them (and physics, for the parameters it drives)
    private readonly Vector2[] _evaluated;        // per evaluation: the host's values with animation lanes applied
    private readonly PuppetEvaluator _evaluator;
    private readonly AnimationPlayback[] _playbacks;
    private readonly Dictionary<string, AnimationPlayback> _playbackByName;
    private readonly List<AnimationPlayback> _running; // in the order they were started: a later Forced lane wins
    private bool _dirty;
    private bool _resetPhysics = true;
    private bool _physicsEnabled = true;
    private Vector2 _scenePosition;

    internal PuppetInstance(Puppet puppet)
    {
        Puppet = puppet;
        _parameterValues = new Vector2[puppet.Parameters.Count];
        _evaluated = new Vector2[puppet.Parameters.Count];
        _evaluator = new PuppetEvaluator(puppet);
        _playbacks = [.. puppet.Animations.Values.Select(a => new AnimationPlayback(this, a))];
        _playbackByName = _playbacks.ToDictionary(p => p.Name, StringComparer.Ordinal);
        _running = new List<AnimationPlayback>(_playbacks.Length);
        ResetParameters();
        Evaluate(0);
    }

    /// <summary>The loaded puppet this instance shows.</summary>
    public Puppet Puppet { get; }

    /// <summary>Seconds this instance has been updated for, the sum of every <see cref="Update"/> step.</summary>
    public double Time { get; private set; }

    /// <summary>
    /// What to draw, back to front, as of the last <see cref="Update"/> (or creation): one item per visible Part, and for
    /// each visible Composite a <see cref="DrawItemKind.BeginComposite"/> and an <see cref="DrawItemKind.EndComposite"/>
    /// item around the items drawn in its layer. Items and their arrays belong to this instance and are reused, so read them
    /// before the next update.
    /// </summary>
    public IReadOnlyList<DrawItem> DrawList => _evaluator.DrawList;

    /// <summary>
    /// Every drawable that some item in the puppet is masked by, with its shape as of the last <see cref="Update"/>; the
    /// same objects as in each item's <see cref="DrawItem.Masks"/>. Updated whether or not the source is drawn itself.
    /// </summary>
    public IReadOnlyList<MaskSource> MaskSources => _evaluator.MaskSources;

    /// <summary>The puppet-space rectangle around every textured item in <see cref="DrawList"/>; empty if nothing is drawn.</summary>
    public PuppetBounds Bounds => _evaluator.Bounds;

    /// <summary>
    /// Whether the puppet's physics runs (on by default). While it is on, each parameter
    /// with a <see cref="Parameter.PhysicsDriver"/> takes its value from the simulation. Turned off, the simulation stops
    /// where it is and those parameters keep the last value it gave them until the host sets them.
    /// </summary>
    public bool PhysicsEnabled
    {
        get => _physicsEnabled;
        set
        {
            _dirty |= value != _physicsEnabled;
            _physicsEnabled = value;
        }
    }

    /// <summary>
    /// Where the host has placed the puppet, in puppet units (for example its screen position divided by its scale).
    /// Physics treats a change as the whole puppet moving, so hair and cloth swing when the puppet slides across the
    /// scene. It does not move the draw list, which stays in puppet space. A jump (placing the puppet somewhere new)
    /// swings it as hard as a fast move would; call <see cref="ResetPhysics"/> after one. nijilive has no such input:
    /// there, physics reacts only to what parameters move, which is what a constant scene position gives.
    /// </summary>
    public Vector2 ScenePosition
    {
        get => _scenePosition;
        set
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The scene position must be finite.");
            _scenePosition = value;
        }
    }

    /// <summary>
    /// One playback per animation in the puppet, in the puppet's order. Each is stopped until the host plays it; several can
    /// run at once (see <see cref="AnimationPlayback"/> for how they combine with the host's values).
    /// </summary>
    public IReadOnlyList<AnimationPlayback> Animations => _playbacks;

    /// <summary>The playback of the animation with this name.</summary>
    /// <exception cref="KeyNotFoundException">The puppet has no animation by that name.</exception>
    public AnimationPlayback GetAnimation(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _playbackByName.TryGetValue(name, out var playback)
            ? playback
            : throw new KeyNotFoundException($"The puppet has no animation named '{name}'.");
    }

    /// <summary>Finds the playback of the animation with this name.</summary>
    public bool TryGetAnimation(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out AnimationPlayback? playback)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _playbackByName.TryGetValue(name, out playback);
    }

    /// <summary>Plays the animation with this name; see <see cref="AnimationPlayback.Play"/>.</summary>
    /// <exception cref="KeyNotFoundException">The puppet has no animation by that name.</exception>
    public AnimationPlayback PlayAnimation(string name, bool loop = false, bool playLeadOut = true)
    {
        var playback = GetAnimation(name);
        playback.Play(loop, playLeadOut);
        return playback;
    }

    /// <summary>Stops every playback; see <see cref="AnimationPlayback.Stop"/>.</summary>
    public void StopAnimations(bool immediate = false)
    {
        foreach (var playback in _playbacks)
        {
            if (playback.IsRunning)
                playback.Stop(immediate);
        }
    }

    /// <summary>
    /// Starts every physics node again at rest, hanging below its anchor with no motion, at the next
    /// <see cref="Update"/>.
    /// </summary>
    public void ResetPhysics()
    {
        _resetPhysics = true;
        _dirty = true;
    }

    /// <summary>
    /// Gets a parameter's current value: the value the host set or, for a parameter physics drives, the value the
    /// simulation gave it at the last <see cref="Update"/>. Animations do not change it; see <see cref="GetEvaluatedValue"/>.
    /// </summary>
    public Vector2 GetParameterValue(Parameter parameter) => _parameterValues[IndexOf(parameter)];

    /// <summary>
    /// Gets the value a parameter was evaluated at in the last <see cref="Update"/>: its value with animations and
    /// physics applied, as its bindings saw it (before it is clamped to the parameter's range).
    /// </summary>
    public Vector2 GetEvaluatedValue(Parameter parameter) => _evaluator.Bindings.Merged(IndexOf(parameter));

    /// <summary>
    /// Sets a parameter's value, applied at the next <see cref="Update"/>. The Y component is ignored for 1D parameters.
    /// Values outside the parameter's range are kept as given and evaluated as the nearest end of the range.
    /// While <see cref="PhysicsEnabled"/> is on, a parameter with a <see cref="Parameter.PhysicsDriver"/> takes the
    /// simulation's value instead.
    /// </summary>
    public void SetParameterValue(Parameter parameter, Vector2 value)
    {
        var index = IndexOf(parameter);
        if (!parameter.IsVec2)
            value.Y = parameter.Defaults.Y;
        _parameterValues[index] = value;
        _dirty = true;
    }

    /// <summary>Sets every parameter back to its default value.</summary>
    public void ResetParameters()
    {
        foreach (var parameter in Puppet.Parameters)
            _parameterValues[parameter.Index] = parameter.Defaults;
        _dirty = true;
    }

    /// <summary>
    /// Advances this instance by its own time step and brings <see cref="DrawList"/> up to date. There is no global clock:
    /// each instance is stepped separately. Physics steps by <paramref name="deltaSeconds"/> in
    /// ticks of at most 0.01 s (a step longer than 10 s counts as 10 s), and each running animation advances by it. The
    /// draw list is re-evaluated on every update while physics or an animation runs, and otherwise only when something
    /// changed.
    /// </summary>
    /// <param name="deltaSeconds">Seconds since this instance's last update; zero or more.</param>
    public void Update(float deltaSeconds)
    {
        if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds), deltaSeconds, "The time step must be finite and not negative.");

        Time += deltaSeconds;
        var animating = false;
        foreach (var playback in _running)
        {
            playback.Advance(deltaSeconds);
            animating |= playback.Advancing || playback.FinishedThisUpdate;
        }

        if (_dirty || animating || (_physicsEnabled && _evaluator.HasPhysics))
            Evaluate(deltaSeconds);

        // A playback that has stopped drives nothing from now on; one that finished in this update needs one more
        // evaluation to give its parameters back to the host.
        for (var i = _running.Count - 1; i >= 0; i--)
        {
            if (_running[i].IsRunning)
                continue;
            _dirty |= _running[i].FinishedThisUpdate;
            _running.RemoveAt(i);
        }
    }

    internal void AnimationStarted(AnimationPlayback playback)
    {
        _running.Remove(playback);
        _running.Add(playback);
        _dirty = true;
    }

    internal void AnimationChanged() => _dirty = true;

    private void Evaluate(float deltaSeconds)
    {
        Array.Copy(_parameterValues, _evaluated, _evaluated.Length);
        foreach (var playback in _running)
        {
            if (playback.Applies)
                playback.Apply(_evaluated, _evaluator.Bindings);
        }

        if (_physicsEnabled)
        {
            _evaluator.Evaluate(_evaluated, new PhysicsStep(deltaSeconds, _scenePosition, _resetPhysics), _parameterValues);
            _resetPhysics = false;
        }
        else
        {
            _evaluator.Evaluate(_evaluated);
        }

        _dirty = false;
    }

    private int IndexOf(Parameter parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var index = parameter.Index;
        if ((uint)index >= (uint)_parameterValues.Length || !ReferenceEquals(Puppet.Parameters[index], parameter))
            throw new ArgumentException("The parameter belongs to a different puppet.", nameof(parameter));
        return index;
    }
}
