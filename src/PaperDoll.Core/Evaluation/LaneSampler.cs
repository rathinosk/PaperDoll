// The value of an animation lane at a frame, fractions included. Before the first key and after the last, the nearest
// key's value; between two keys, Nearest, Stepped, Linear, Cubic (Catmull-Rom) or Bezier (linear, eased by the later
// key's tension through the cubic Hermite basis, written from its standard definition).

namespace PaperDoll.Evaluation;

/// <summary>Samples an animation lane's keyframes.</summary>
internal static class LaneSampler
{
    /// <summary>
    /// The lane's value at <paramref name="frame"/> (fractional frames interpolate). Before the first key it is the first
    /// key's value, after the last the last key's; with no keys, 0. Between two keys, <c>t</c> runs from 0 at the earlier
    /// key to 1 at the later one, and the later key's tension shapes a Bezier segment.
    /// </summary>
    /// <param name="frames">The keyframes, sorted by frame.</param>
    /// <param name="mode">The lane's interpolation.</param>
    /// <param name="frame">The frame to sample.</param>
    public static float Sample(Keyframe[] frames, InterpolateMode mode, float frame)
    {
        if (frames.Length == 0)
            return 0;
        if (frames.Length == 1)
            return frames[0].Value;

        for (var i = 0; i < frames.Length; i++)
        {
            if (frames[i].Frame < frame)
                continue;
            if (i == 0)
                return frames[0].Value;

            // frames[i - 1].Frame < frame <= frames[i].Frame, so the segment has a length.
            var from = frames[i - 1];
            var to = frames[i];
            var t = 1 - (to.Frame - frame) / (to.Frame - from.Frame);
            return mode switch
            {
                InterpolateMode.Nearest => t > 0.5f ? to.Value : from.Value, // the earlier key at exactly halfway
                InterpolateMode.Stepped => from.Value,
                InterpolateMode.Cubic => CatmullRom(
                    frames[Math.Max(i - 2, 0)].Value, from.Value, to.Value, frames[Math.Min(i + 1, frames.Length - 1)].Value, t),
                InterpolateMode.Bezier => Lerp(from.Value, to.Value, Math.Clamp(Hermite(0, 2 * to.Tension, 1, 2 * to.Tension, t), 0, 1)),
                _ => Lerp(from.Value, to.Value, t),
            };
        }

        return frames[^1].Value;
    }

    /// <summary>
    /// inmath's <c>hermite(x, tx, y, ty, t)</c>: from <paramref name="x"/> with tangent <paramref name="tx"/> to
    /// <paramref name="y"/> with tangent <paramref name="ty"/>. With the arguments a Bezier lane uses (0, 2τ, 1, 2τ), a tension of 0.5 is
    /// linear, below it the segment eases in and out (0 is smoothstep), above it the segment is fast at the keys and slow
    /// between them; above a tension of 4.5 the curve leaves 0 to 1, and the caller clamps it.
    /// </summary>
    internal static float Hermite(float x, float tx, float y, float ty, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * x + (t3 - 2 * t2 + t) * tx + (-2 * t3 + 3 * t2) * y + (t3 - t2) * ty;
    }

    private static float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5f * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (3 * p1 - 3 * p2 + p3 - p0) * t3);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
