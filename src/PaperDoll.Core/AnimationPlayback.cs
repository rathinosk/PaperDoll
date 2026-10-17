// One animation playing on one instance. Play starts it from frame 0 (or resumes it), a loop repeats the frames between
// its lead-in and lead-out, Stop lets a loop finish through its lead-out, and Pause, Seek, Speed and Strength adjust it.
// Each update advances its clock and pushes its lanes' values onto a copy of the host's parameter values.
// Differs on purpose (docs/design/nijilive-differences.md): time wraps with its remainder, so playback does not depend on the update rate; strength
// blends from the value underneath instead of scaling towards 0; speed is applied; the host's values are not overwritten.

using System.Numerics;
using PaperDoll.Evaluation;

namespace PaperDoll;

/// <summary>
/// One animation's playback on one <see cref="PuppetInstance"/>, advanced by the instance's <see cref="PuppetInstance.Update"/>.
/// Every instance has one per animation in its puppet (<see cref="PuppetInstance.Animations"/>); several can run at once.
/// </summary>
/// <remarks>
/// <para>
/// A playback runs from frame 0 to the animation's last frame (<see cref="Animation.Length"/> − 1). Looping, it plays its
/// lead-in once (frames 0 to <see cref="Animation.LeadIn"/>), then repeats the loop until <see cref="Animation.LeadOut"/>
/// (or the end, if it has no lead-out). Stopping a looping playback that has a lead-out lets it finish the loop and play
/// the lead-out; otherwise it stops at once.
/// </para>
/// <para>
/// While a playback runs, each lane drives one axis of its parameter by the lane's merge mode: a
/// <see cref="ParameterMergeMode.Forced"/> lane replaces the value the host set (and any earlier playback's), an
/// <see cref="ParameterMergeMode.Additive"/> lane adds to it, and a <see cref="ParameterMergeMode.Multiplicative"/> or
/// <see cref="ParameterMergeMode.Weighted"/> lane multiplies it. The host's values are not changed: once the playback
/// stops, each parameter goes back to the value the host set. Physics still decides the parameters it drives.
/// </para>
/// </remarks>
public sealed class AnimationPlayback
{
    private readonly PuppetInstance _owner;
    private readonly float _timestep;
    private float _time;            // seconds into the animation
    private float _strength = 1;
    private float _speed = 1;
    private bool _playing;
    private bool _paused;
    private bool _looping;
    private bool _stopping;
    private bool _playLeadOut;

    internal AnimationPlayback(PuppetInstance owner, Animation animation)
    {
        _owner = owner;
        Animation = animation;
        _timestep = float.IsFinite(animation.Timestep) && animation.Timestep > 0 ? animation.Timestep : 0.0166f;
    }

    /// <summary>The animation this plays.</summary>
    public Animation Animation { get; }

    /// <summary>The animation's name.</summary>
    public string Name => Animation.Name;

    /// <summary>Whether the playback is playing its main part (not stopped, and not only finishing its lead-out).</summary>
    public bool IsPlaying => _playing;

    /// <summary>Whether the playback was stopped and is finishing its loop and lead-out.</summary>
    public bool IsStopping => _stopping;

    /// <summary>Whether the playback is paused: it keeps its frame and still drives its parameters.</summary>
    public bool IsPaused => _paused;

    /// <summary>Whether the playback drives its parameters: playing, paused, or finishing its lead-out.</summary>
    public bool IsRunning => _playing || IsPlayingLeadOut;

    /// <summary>Whether the playback will stop once it reaches its last frame (not looping, or stopping).</summary>
    public bool IsPlayingLeadOut => ((_playing && !_looping) || _stopping) && _playLeadOut && FramePosition < LastFrame + 0.5f;

    /// <summary>Whether the playback loops. Set to <see langword="false"/> while playing, it plays on to the end and stops.</summary>
    public bool IsLooping
    {
        get => _looping;
        set => _looping = value;
    }

    /// <summary>How many times the playback has looped since it started or was last sought.</summary>
    public int LoopCount { get; private set; }

    /// <summary>
    /// How strongly the playback drives its parameters, 0 to 1 (1 by default). At 0 it has no effect; in between, a
    /// Forced lane blends from the value underneath it to its own, an Additive lane adds that share of its value, and a
    /// Multiplicative or Weighted lane multiplies by a factor that share of the way from 1 to its value. Changing it
    /// over a few updates fades an animation in or out.
    /// </summary>
    public float Strength
    {
        get => _strength;
        set
        {
            if (float.IsNaN(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The strength must be a number.");
            _strength = Math.Clamp(value, 0, 1);
            _owner.AnimationChanged();
        }
    }

    /// <summary>How fast the playback runs, 1 being the animation's own rate (0 or more; 1 by default).</summary>
    public float Speed
    {
        get => _speed;
        set
        {
            if (!float.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The speed must be finite and not negative.");
            _speed = value;
        }
    }

    /// <summary>The current frame, rounded.</summary>
    public int Frame => (int)MathF.Round(FramePosition);

    /// <summary>The current position in frames, with the fraction between frames.</summary>
    public float FramePosition => _time / _timestep;

    /// <summary>Seconds into the animation.</summary>
    public float Seconds => _time;

    /// <summary>Whether the animation has a lead-in: a <see cref="Animation.LeadIn"/> after frame 0 and before the last frame.</summary>
    public bool HasLeadIn => Animation.LeadIn > 0 && Animation.LeadIn + 1 < Animation.Length;

    /// <summary>Whether the animation has a lead-out: a <see cref="Animation.LeadOut"/> after frame 0 and before the last frame.</summary>
    public bool HasLeadOut => Animation.LeadOut > 0 && Animation.LeadOut + 1 < Animation.Length;

    /// <summary>The frame a loop starts again from: the end of the lead-in, or 0.</summary>
    public int LoopStart => HasLeadIn ? Animation.LeadIn : 0;

    /// <summary>The frame a loop ends at: the start of the lead-out, or <see cref="Animation.Length"/>.</summary>
    public int LoopEnd => HasLeadOut ? Animation.LeadOut : Animation.Length;

    private float LastFrame => Math.Max(Animation.Length - 1, 0);

    /// <summary>Whether the last update played this playback's final frame: it then drives its parameters once more.</summary>
    internal bool FinishedThisUpdate { get; private set; }

    /// <summary>Whether the playback drives its parameters in the next evaluation.</summary>
    internal bool Applies => IsRunning || FinishedThisUpdate;

    /// <summary>Whether time moves for the playback, so the instance must be evaluated on every update.</summary>
    internal bool Advancing => IsRunning && !_paused && _speed > 0;

    /// <summary>
    /// Plays the animation from the start, or resumes it if paused (then the arguments are ignored).
    /// </summary>
    /// <param name="loop">Whether to loop until stopped.</param>
    /// <param name="playLeadOut">
    /// Whether to stop at the end. Without it, a playback that does not loop holds its last frame until stopped, and a
    /// looping one stops at once when stopped instead of playing its lead-out.
    /// </param>
    public void Play(bool loop = false, bool playLeadOut = true)
    {
        if (_paused)
        {
            _paused = false;
        }
        else
        {
            LoopCount = 0;
            _time = 0;
            _stopping = false;
            _playing = true;
            _looping = loop;
            _playLeadOut = playLeadOut;
        }

        FinishedThisUpdate = false;
        _owner.AnimationStarted(this);
    }

    /// <summary>Pauses a running playback where it is. It keeps driving its parameters; <see cref="Play"/> resumes it.</summary>
    public void Pause()
    {
        if (IsRunning)
            _paused = true;
    }

    /// <summary>
    /// Stops the playback. A looping playback with a lead-out finishes its loop and plays the lead-out first, unless
    /// <paramref name="immediate"/>, paused, at frame 0, or without a lead-out; then it stops at once, back at frame 0.
    /// Its parameters go back to the host's values once it has stopped.
    /// </summary>
    public void Stop(bool immediate = false)
    {
        if (_stopping)
            return;

        var now = immediate || Frame == 0 || _paused || !HasLeadOut;
        _stopping = !now;
        _looping = false;
        _paused = false;
        _playing = false;
        _playLeadOut = !now;
        if (now)
        {
            _time = 0;
            LoopCount = 0;
        }

        FinishedThisUpdate = false;
        _owner.AnimationChanged();
    }

    /// <summary>Moves the playback to a frame (clamped to 0 to <see cref="Animation.Length"/>) without starting or stopping it.</summary>
    public void Seek(float frame)
    {
        if (float.IsNaN(frame))
            throw new ArgumentOutOfRangeException(nameof(frame), frame, "The frame must be a number.");
        _time = Math.Clamp(frame, 0, Math.Max(Animation.Length, 0)) * _timestep;
        LoopCount = 0;
        _owner.AnimationChanged();
    }

    /// <summary>Advances the playback by one instance update.</summary>
    internal void Advance(float deltaSeconds)
    {
        FinishedThisUpdate = false;
        if (!IsRunning || _paused)
            return;

        var frame = (_time + deltaSeconds * _speed) / _timestep;
        if (_looping)
        {
            // Wrapping with the remainder keeps the timing the same at any update rate, and gives the frames nijilive
            // shows at 60 updates a second (docs/design/nijilive-differences.md).
            float start = LoopStart, end = LoopEnd;
            if (frame >= end)
            {
                var span = end - start;
                if (span > 0)
                {
                    var loops = (int)((frame - start) / span);
                    frame = start + (frame - start - loops * span);
                    LoopCount += loops;
                }
                else
                {
                    frame = start;
                    LoopCount++;
                }
            }
        }
        else if (frame >= LastFrame)
        {
            frame = LastFrame;
            if (IsPlayingLeadOutAt(frame))
            {
                // The final frame drives its parameters in this update; then the playback is stopped and back at frame 0.
                _playing = false;
                _playLeadOut = false;
                _stopping = false;
                LoopCount = 0;
                _time = 0;
                SampleFrame = frame;
                FinishedThisUpdate = true;
                return;
            }
        }

        _time = frame * _timestep;
        SampleFrame = Math.Min(frame, LastFrame);
    }

    /// <summary>The frame the lanes are sampled at in the next evaluation.</summary>
    internal float SampleFrame { get; private set; }

    /// <summary>Pushes every lane's value at the current frame onto the instance's parameters.</summary>
    internal void Apply(Vector2[] values, BindingEvaluator bindings)
    {
        if (!FinishedThisUpdate)
            SampleFrame = Math.Min(FramePosition, LastFrame);

        var strength = _strength;
        var lanes = Animation.Lanes;
        for (var l = 0; l < lanes.Count; l++) // by index: a foreach over the interface would allocate
        {
            var lane = lanes[l];
            if (lane.Parameter is not { } parameter || lane.TargetAxis is not (0 or 1) || (lane.TargetAxis == 1 && !parameter.IsVec2))
                continue;

            var axis = lane.TargetAxis;
            var value = LaneSampler.Sample(lane.Frames, lane.Interpolation, SampleFrame);
            var mode = lane.MergeMode == ParameterMergeMode.Passthrough ? parameter.MergeMode : lane.MergeMode;
            ref var current = ref values[parameter.Index];
            switch (mode)
            {
                case ParameterMergeMode.Forced:
                    current[axis] += (value - current[axis]) * strength;
                    break;
                case ParameterMergeMode.Additive:
                    bindings.Inputs(parameter.Index).PushAxis(axis, value * strength, mode, parameter.MergeMode, 1, ref current);
                    break;
                case ParameterMergeMode.Multiplicative:
                case ParameterMergeMode.Weighted:
                    bindings.Inputs(parameter.Index).PushAxis(axis, 1 + (value - 1) * strength, mode, parameter.MergeMode, 1, ref current);
                    break;
            }
        }
    }

    private bool IsPlayingLeadOutAt(float frame) => ((_playing && !_looping) || _stopping) && _playLeadOut && frame < LastFrame + 0.5f;
}
