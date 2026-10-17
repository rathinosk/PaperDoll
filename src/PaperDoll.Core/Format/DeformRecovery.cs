// Rebuilds deform offsets that a file stores as non-finite values: each missing component takes the value of a weighted
// plane fitted through up to 16 of the nearest vertices whose component is finite.

using System.Numerics;

namespace PaperDoll.Format;

internal static class DeformRecovery
{
    private const int MaxSamples = 16;

    /// <summary>
    /// For every component flagged in <paramref name="missing"/> (bit 0 for X, bit 1 for Y), fits a weighted plane through
    /// up to 16 nearest vertices whose component is finite and writes the value at the vertex. Returns how many
    /// components were recovered.
    /// </summary>
    public static int Recover(ReadOnlySpan<Vector2> vertices, Vector2[] offsets, ReadOnlySpan<byte> missing)
    {
        var recovered = 0;
        var usable = Math.Min(vertices.Length, offsets.Length);
        var count = Math.Min(usable, missing.Length);
        var samples = new List<(int Index, double Distance)>();

        for (var i = 0; i < count; i++)
        {
            for (var component = 0; component < 2; component++)
            {
                if ((missing[i] & (1 << component)) == 0)
                    continue;

                samples.Clear();
                for (var j = 0; j < usable; j++)
                {
                    if (j < missing.Length && (missing[j] & (1 << component)) != 0)
                        continue;
                    if (!float.IsFinite(Component(offsets[j], component)))
                        continue;
                    var delta = vertices[j] - vertices[i];
                    var distance = (double)delta.X * delta.X + (double)delta.Y * delta.Y;
                    if (double.IsFinite(distance))
                        samples.Add((j, distance));
                }

                if (samples.Count == 0)
                    continue;

                samples.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Index.CompareTo(b.Index));

                double value = Component(offsets[samples[0].Index], component);
                if (samples[0].Distance > 1e-8)
                    value = FitPlane(vertices, offsets, samples, i, component);

                if (!double.IsFinite(value) || !float.IsFinite((float)value))
                    continue;

                offsets[i] = component == 0 ? offsets[i] with { X = (float)value } : offsets[i] with { Y = (float)value };
                recovered++;
            }
        }

        return recovered;
    }

    // Weighted least squares for v = c0 + c1 * x + c2 * y around vertex i, evaluated at i (so the result is c0).
    private static double FitPlane(ReadOnlySpan<Vector2> vertices, Vector2[] offsets, List<(int Index, double Distance)> samples, int i, int component)
    {
        var count = Math.Min(samples.Count, MaxSamples);
        var radius = Math.Sqrt(samples[count - 1].Distance);
        double a00 = 0, a01 = 0, a02 = 0, a11 = 0, a12 = 0, a22 = 0;
        double b0 = 0, b1 = 0, b2 = 0;

        for (var s = 0; s < count; s++)
        {
            var (index, distance) = samples[s];
            var delta = vertices[index] - vertices[i];
            var x = delta.X / radius;
            var y = delta.Y / radius;
            var weight = 1 / Math.Max(distance, 1e-8);
            double v = Component(offsets[index], component);
            a00 += weight; a01 += weight * x; a02 += weight * y;
            a11 += weight * x * x; a12 += weight * x * y; a22 += weight * y * y;
            b0 += weight * v; b1 += weight * x * v; b2 += weight * y * v;
        }

        var c0 = a11 * a22 - a12 * a12;
        var c1 = a02 * a12 - a01 * a22;
        var c2 = a01 * a12 - a02 * a11;
        var determinant = a00 * c0 + a01 * c1 + a02 * c2;
        return Math.Abs(determinant) > 1e-10 * a00 * a11 * a22
            ? (c0 * b0 + c1 * b1 + c2 * b2) / determinant
            : b0 / a00;
    }

    private static float Component(Vector2 v, int component) => component == 0 ? v.X : v.Y;
}
