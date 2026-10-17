// The enumerations of the INP1 data model: blend and masking modes, parameter merge and interpolation modes, physics
// models and map modes, texture encodings and node kinds.

namespace PaperDoll;

/// <summary>How a drawable is blended with what is drawn below it.</summary>
public enum BlendMode
{
    /// <summary>Normal alpha blending.</summary>
    Normal,
    /// <summary>Multiply.</summary>
    Multiply,
    /// <summary>Screen.</summary>
    Screen,
    /// <summary>Overlay.</summary>
    Overlay,
    /// <summary>Darken.</summary>
    Darken,
    /// <summary>Lighten.</summary>
    Lighten,
    /// <summary>Colour dodge.</summary>
    ColorDodge,
    /// <summary>Linear dodge (additive).</summary>
    LinearDodge,
    /// <summary>Additive glow.</summary>
    AddGlow,
    /// <summary>Colour burn.</summary>
    ColorBurn,
    /// <summary>Hard light.</summary>
    HardLight,
    /// <summary>Soft light.</summary>
    SoftLight,
    /// <summary>Difference.</summary>
    Difference,
    /// <summary>Exclusion.</summary>
    Exclusion,
    /// <summary>Subtract.</summary>
    Subtract,
    /// <summary>Inverse.</summary>
    Inverse,
    /// <summary>Keep the destination only where the source is drawn.</summary>
    DestinationIn,
    /// <summary>Draw only where something is already drawn below.</summary>
    ClipToLower,
    /// <summary>Cut the source out of what is drawn below.</summary>
    SliceFromLower,
}

/// <summary>How a mask source affects the drawable it masks.</summary>
public enum MaskingMode
{
    /// <summary>Draw only where the mask sources are.</summary>
    Mask,
    /// <summary>Draw only where the mask sources are not.</summary>
    DodgeMask,
}

/// <summary>How a parameter's value or an animation lane combines with other inputs to the same parameter.</summary>
public enum ParameterMergeMode
{
    /// <summary>Values are added.</summary>
    Additive,
    /// <summary>Values are averaged by weight.</summary>
    Weighted,
    /// <summary>Values are multiplied.</summary>
    Multiplicative,
    /// <summary>The value replaces the others.</summary>
    Forced,
    /// <summary>The value passes through unchanged.</summary>
    Passthrough,
}

/// <summary>Interpolation between keyforms (bindings) or keyframes (animation lanes).</summary>
public enum InterpolateMode
{
    /// <summary>Snap to the nearest key.</summary>
    Nearest,
    /// <summary>Linear interpolation.</summary>
    Linear,
    /// <summary>Hold the previous key (animation lanes).</summary>
    Stepped,
    /// <summary>Cubic interpolation.</summary>
    Cubic,
    /// <summary>Bezier curve using each keyframe's tension (animation lanes).</summary>
    Bezier,
}

/// <summary>The pendulum model a <see cref="SimplePhysics"/> node simulates.</summary>
public enum PhysicsModel
{
    /// <summary>Rigid pendulum.</summary>
    Pendulum,
    /// <summary>Springy pendulum.</summary>
    SpringPendulum,
}

/// <summary>How a <see cref="SimplePhysics"/> node maps its simulation onto its parameter's axes.</summary>
public enum PhysicsMapMode
{
    /// <summary>X is the angle, Y the length.</summary>
    AngleLength,
    /// <summary>X and Y are the bob's offset.</summary>
    XY,
    /// <summary>X is the length, Y the angle.</summary>
    LengthAngle,
    /// <summary>X and Y are the bob's offset, swapped.</summary>
    YX,
}

/// <summary>How a texture slot is encoded in the puppet file.</summary>
public enum TextureEncoding : byte
{
    /// <summary>PNG.</summary>
    Png = 0,
    /// <summary>TGA, uncompressed or RLE.</summary>
    Tga = 1,
    /// <summary>BC7 block compression. Not supported.</summary>
    Bc7 = 2,
}

/// <summary>The node types PaperDoll reads.</summary>
public enum NodeKind
{
    /// <summary>A grouping node with a transform.</summary>
    Node,
    /// <summary>A textured mesh.</summary>
    Part,
    /// <summary>A group drawn as one layer.</summary>
    Composite,
    /// <summary>A mesh that deforms its children.</summary>
    MeshGroup,
    /// <summary>A mesh used only as a mask source.</summary>
    Mask,
    /// <summary>A pendulum that drives a parameter.</summary>
    SimplePhysics,
}
