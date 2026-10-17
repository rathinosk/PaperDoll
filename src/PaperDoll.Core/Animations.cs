// Animations as stored in INP1: frame duration, length, lead-in and lead-out, and lanes of keyframes, each lane driving
// one axis of one parameter with its own interpolation and merge mode. Read-only; AnimationPlayback plays them per instance.

namespace PaperDoll;

/// <summary>A named animation: lanes of keyframes that drive parameter axes.</summary>
public sealed class Animation
{
    internal Animation() { }

    /// <summary>The animation's name (its key in the file).</summary>
    public string Name { get; internal set; } = "";

    /// <summary>Seconds per frame.</summary>
    public float Timestep { get; internal set; } = 0.0166f;

    /// <summary>
    /// Whether the file marks the animation as additive. Read for tools; it has no effect on playback:
    /// each lane's <see cref="AnimationLane.MergeMode"/> decides how it combines.
    /// </summary>
    public bool Additive { get; internal set; }

    /// <summary>
    /// The weight the file stores for an additive animation, or <see langword="null"/> if it has none. Read for tools; it
    /// has no effect on playback (see <see cref="AnimationPlayback.Strength"/>).
    /// </summary>
    public float? Weight { get; internal set; }

    /// <summary>Length in frames.</summary>
    public int Length { get; internal set; }

    /// <summary>The frame where the lead-in ends, or -1 for none.</summary>
    public int LeadIn { get; internal set; } = -1;

    /// <summary>The frame where the lead-out starts, or -1 for none.</summary>
    public int LeadOut { get; internal set; } = -1;

    /// <summary>The lanes, in file order.</summary>
    public IReadOnlyList<AnimationLane> Lanes { get; internal set; } = [];
}

/// <summary>Keyframes for one axis of one parameter.</summary>
public sealed class AnimationLane
{
    internal AnimationLane() { }

    /// <summary>The target parameter's ID.</summary>
    public uint ParameterUuid { get; internal set; }

    /// <summary>The target parameter, or <see langword="null"/> if it is not in the puppet (the lane is then not played).</summary>
    public Parameter? Parameter { get; internal set; }

    /// <summary>The target axis: 0 for X, 1 for Y.</summary>
    public int TargetAxis { get; internal set; }

    /// <summary>Interpolation between keyframes.</summary>
    public InterpolateMode Interpolation { get; internal set; } = InterpolateMode.Nearest;

    /// <summary>How the lane's value combines with the parameter's other inputs.</summary>
    public ParameterMergeMode MergeMode { get; internal set; } = ParameterMergeMode.Forced;

    /// <summary>The keyframes, sorted by frame (keys on the same frame keep their file order).</summary>
    public IReadOnlyList<Keyframe> Keyframes => Frames;

    internal Keyframe[] Frames { get; set; } = [];
}

/// <summary>A keyframe in an animation lane.</summary>
/// <param name="Frame">The frame it occurs at.</param>
/// <param name="Value">The parameter axis value at that frame.</param>
/// <param name="Tension">Tension for Bezier interpolation.</param>
public readonly record struct Keyframe(int Frame, float Value, float Tension = 0.5f);
