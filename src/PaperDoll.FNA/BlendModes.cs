// How each blend mode is drawn: the fixed-function blend state of every mode (Normal for the modes that have none),
// and which modes are advanced, drawn instead by the shader from a copy of what lies below.

using Microsoft.Xna.Framework.Graphics;

#if MONOGAME
namespace PaperDoll.MonoGame;
#else
namespace PaperDoll.Fna;
#endif

internal static class BlendModes
{
    private static readonly BlendState[] FixedFunction = CreateFixedFunctionStates();

    /// <summary>A blend state that writes no colour, for drawing mask sources into the stencil buffer.</summary>
    public static readonly BlendState NoColour = new() { ColorWriteChannels = ColorWriteChannels.None };

    /// <summary>
    /// Whether the mode needs an advanced blend equation (KHR_blend_equation_advanced). PaperDoll draws these in a shader
    /// that reads a copy of the target.
    /// </summary>
    public static bool IsAdvanced(BlendMode mode) => mode is BlendMode.Multiply or BlendMode.Screen or BlendMode.Overlay
        or BlendMode.Darken or BlendMode.Lighten or BlendMode.ColorDodge or BlendMode.ColorBurn or BlendMode.HardLight
        or BlendMode.SoftLight or BlendMode.Difference or BlendMode.Exclusion;

    /// <summary>
    /// The fixed-function blending for a mode, used for every mode without an advanced equation, and for all of them when
    /// advanced blending is off.
    /// KLUDGE: modes with no fixed-function equation (Overlay, Darken, ColorBurn, HardLight, SoftLight, Difference) are
    /// drawn as Normal, as nijilive's fallback draws them.
    /// </summary>
    public static BlendState FixedFunctionState(BlendMode mode) =>
        (uint)mode < (uint)FixedFunction.Length ? FixedFunction[(int)mode] : FixedFunction[(int)BlendMode.Normal];

    private static BlendState[] CreateFixedFunctionStates()
    {
        var states = new BlendState[Enum.GetValues<BlendMode>().Length];
        var normal = State("Normal", Blend.One, Blend.InverseSourceAlpha);
        Array.Fill(states, normal);

        // OpenGL applies a colour factor to alpha as the matching alpha factor (DST_COLOR is Ad for alpha, and so on).
        states[(int)BlendMode.Multiply] = State("Multiply", Blend.DestinationColor, Blend.InverseSourceAlpha, Blend.DestinationAlpha);
        states[(int)BlendMode.Screen] = State("Screen", Blend.One, Blend.InverseSourceColor, alphaDestination: Blend.InverseSourceAlpha);
        states[(int)BlendMode.Lighten] = State("Lighten", Blend.One, Blend.One, function: BlendFunction.Max);
        states[(int)BlendMode.ColorDodge] = State("ColorDodge", Blend.DestinationColor, Blend.One, Blend.DestinationAlpha);
        states[(int)BlendMode.LinearDodge] = State("LinearDodge", Blend.One, Blend.InverseSourceColor, alphaDestination: Blend.InverseSourceAlpha);
        states[(int)BlendMode.AddGlow] = State("AddGlow", Blend.One, Blend.One, alphaDestination: Blend.InverseSourceAlpha);
        states[(int)BlendMode.Subtract] = new BlendState
        {
            Name = "PaperDoll.Subtract",
            ColorBlendFunction = BlendFunction.ReverseSubtract,
            ColorSourceBlend = Blend.InverseDestinationColor,
            ColorDestinationBlend = Blend.One,
            AlphaBlendFunction = BlendFunction.Add,
            AlphaSourceBlend = Blend.InverseDestinationAlpha,
            AlphaDestinationBlend = Blend.One,
        };
        states[(int)BlendMode.Exclusion] = State("Exclusion", Blend.InverseDestinationColor, Blend.InverseSourceColor, Blend.One, Blend.One);
        states[(int)BlendMode.Inverse] = State("Inverse", Blend.InverseDestinationColor, Blend.InverseSourceAlpha, Blend.InverseDestinationAlpha);
        states[(int)BlendMode.DestinationIn] = State("DestinationIn", Blend.Zero, Blend.SourceAlpha);
        states[(int)BlendMode.ClipToLower] = State("ClipToLower", Blend.DestinationAlpha, Blend.InverseSourceAlpha);
        states[(int)BlendMode.SliceFromLower] = State("SliceFromLower", Blend.Zero, Blend.InverseSourceAlpha);
        return states;
    }

    private static BlendState State(string name, Blend source, Blend destination, Blend? alphaSource = null,
        Blend? alphaDestination = null, BlendFunction function = BlendFunction.Add) => new()
    {
        Name = "PaperDoll." + name,
        ColorBlendFunction = function,
        AlphaBlendFunction = function,
        ColorSourceBlend = source,
        ColorDestinationBlend = destination,
        AlphaSourceBlend = alphaSource ?? source,
        AlphaDestinationBlend = alphaDestination ?? destination,
    };
}
