// Keyform maths for bindings: normalising a parameter value to its range, finding which two axis points it lies
// between and how far along, and the weights of the surrounding keyforms on one axis for Nearest, Linear and Cubic
// (Catmull-Rom, clamped at the ends) interpolation.

using System.Numerics;

namespace PaperDoll.Evaluation;

/// <summary>Where a value falls on one parameter axis: the keyform to its left and how far towards the next one.</summary>
/// <param name="Index">The axis point at or to the left of the value.</param>
/// <param name="Offset">0 at <paramref name="Index"/>, 1 at the next axis point.</param>
internal readonly record struct AxisPosition(int Index, float Offset);

/// <summary>Up to four keyform indices along one axis with their weights. Unused entries have weight 0.</summary>
internal struct AxisWeights
{
    public int Count;
    public Int4 Index;
    public Vector4 Weight;

    [System.Runtime.CompilerServices.InlineArray(4)]
    public struct Int4
    {
        private int _element;
    }

    public void Set(int slot, int index, float weight)
    {
        Index[slot] = index;
        Weight[slot] = weight;
    }
}

internal static class Keyforms
{
    /// <summary>
    /// Maps a parameter value to 0..1 on each axis, clamped. A zero-width range maps to 0
    /// (docs/design/nijilive-differences.md).
    /// </summary>
    public static Vector2 Normalize(Vector2 value, Vector2 min, Vector2 max)
    {
        var range = max - min;
        var x = range.X != 0 ? (value.X - min.X) / range.X : 0f;
        var y = range.Y != 0 ? (value.Y - min.Y) / range.Y : 0f;
        return new Vector2(Clamp01(x), Clamp01(y));
    }

    /// <summary>
    /// Where a normalised value lies on one axis: the first segment whose right point is past the value (or the last
    /// segment), and how far along it. An axis with a single point always gives (0, 0).
    /// </summary>
    public static AxisPosition Locate(ReadOnlySpan<float> axisPoints, float value)
    {
        if (axisPoints.Length < 2)
            return default;

        var last = axisPoints.Length - 2;
        for (var i = 0; i <= last; i++)
        {
            if (axisPoints[i + 1] > value || i == last)
            {
                var width = axisPoints[i + 1] - axisPoints[i];
                return new AxisPosition(i, width != 0 ? (value - axisPoints[i]) / width : 0f);
            }
        }

        return default;
    }

    /// <summary>
    /// The keyforms one axis reads and their weights. Linear and Cubic are linear in the keyform values, so a binding's
    /// result is the sum of keyform * x weight * y weight, for plain values and deform offsets alike.
    /// </summary>
    /// <param name="mode">The binding's interpolation.</param>
    /// <param name="count">Number of axis points on this axis.</param>
    /// <param name="position">Where the value falls.</param>
    /// <param name="active">False for a 1D parameter's Y axis: always keyform 0.</param>
    public static AxisWeights Weights(InterpolateMode mode, int count, AxisPosition position, bool active)
    {
        var weights = default(AxisWeights);
        var left = position.Index;
        var t = position.Offset;
        var last = count - 1;
        if (!active || count < 2)
        {
            weights.Count = 1;
            weights.Set(0, active ? Math.Min(left, last) : 0, 1f);
            return weights;
        }

        switch (mode)
        {
            case InterpolateMode.Nearest:
                weights.Count = 1;
                weights.Set(0, Math.Min(left + (t >= 0.5f ? 1 : 0), last), 1f);
                break;

            case InterpolateMode.Stepped: // not meant for bindings; holds the left keyform
                weights.Count = 1;
                weights.Set(0, left, 1f);
                break;

            case InterpolateMode.Cubic:
                // Catmull-Rom through the keyforms either side, clamped at the ends:
                // ((2 p1) + (p2 - p0) t + (2 p0 - 5 p1 + 4 p2 - p3) t^2 + (3 p1 - 3 p2 + p3 - p0) t^3) / 2
                var t2 = t * t;
                var t3 = t2 * t;
                weights.Count = 4;
                weights.Set(0, Math.Max(left - 1, 0), (-t + 2 * t2 - t3) * 0.5f);
                weights.Set(1, left, (2 - 5 * t2 + 3 * t3) * 0.5f);
                weights.Set(2, Math.Min(left + 1, last), (t + 4 * t2 - 3 * t3) * 0.5f);
                weights.Set(3, Math.Min(left + 2, last), (-t2 + t3) * 0.5f);
                break;

            default: // Linear (and Bezier, which is not a binding mode)
                weights.Count = 2;
                weights.Set(0, left, 1 - t);
                weights.Set(1, Math.Min(left + 1, last), t);
                break;
        }

        return weights;
    }

    private static float Clamp01(float value) => value < 0 ? 0 : value > 1 ? 1 : float.IsNaN(value) ? 0 : value;
}
