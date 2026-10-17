namespace PaperDoll.Snapshots;

/// <summary>Compares a saved snapshot with a replayed one.</summary>
public static class SnapshotComparer
{
    /// <summary>The default absolute tolerance for floating-point values.</summary>
    public const float DefaultTolerance = 1e-4f;

    /// <summary>Lists every difference between <paramref name="expected"/> and <paramref name="actual"/>; empty when they match.</summary>
    /// <param name="expected">The saved, accepted snapshot.</param>
    /// <param name="actual">The snapshot replayed from <paramref name="expected"/>'s inputs.</param>
    /// <param name="tolerance">The largest absolute difference accepted between two floating-point values.</param>
    public static IReadOnlyList<string> Compare(Snapshot expected, Snapshot actual, float tolerance = DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var differences = new List<string>();
        if (expected.PuppetPath != actual.PuppetPath)
            differences.Add($"puppet: expected '{expected.PuppetPath}', got '{actual.PuppetPath}'");
        if (Math.Abs(expected.Time - actual.Time) > tolerance)
            differences.Add($"time: expected {expected.Time}, got {actual.Time}");

        CompareParameters(expected.Parameters, actual.Parameters, tolerance, differences);
        if (expected.FormatVersion < 2)
        {
            differences.Add($"saved with format version {expected.FormatVersion}, which has no draw list; check the pose in the demo and save it again");
            return differences;
        }

        if (!Close(expected.Bounds.Min, actual.Bounds.Min, tolerance) || !Close(expected.Bounds.Max, actual.Bounds.Max, tolerance))
            differences.Add($"bounds: expected {expected.Bounds}, got {actual.Bounds}");
        CompareDrawables(expected.Drawables, actual.Drawables, expected.FormatVersion >= 3, expected.FormatVersion >= 4, tolerance, differences);
        if (expected.FormatVersion >= 4)
            CompareLayers(expected.Layers, actual.Layers, tolerance, differences);
        return differences;
    }

    private static void CompareDrawables(IReadOnlyList<SnapshotDrawable> expected, IReadOnlyList<SnapshotDrawable> actual,
        bool stage2Outputs, bool stage4Outputs, float tolerance, List<string> differences)
    {
        if (expected.Count != actual.Count || !expected.Select(d => d.Uuid).SequenceEqual(actual.Select(d => d.Uuid)))
        {
            differences.Add($"draw order: expected {expected.Count} items [{Describe(expected)}], got {actual.Count} [{Describe(actual)}]");
            return;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            var want = expected[i];
            var got = actual[i];
            var where = $"drawable {i} '{want.Name}' ({want.Uuid})";
            if (want.Texture != got.Texture)
                differences.Add($"{where}: texture expected {want.Texture}, got {got.Texture}");
            if (want.Layer != got.Layer)
                differences.Add($"{where}: layer expected {want.Layer?.ToString() ?? "none"}, got {got.Layer?.ToString() ?? "none"}");
            if (want.BlendMode != got.BlendMode)
                differences.Add($"{where}: blend mode expected {want.BlendMode}, got {got.BlendMode}");
            if (!(Math.Abs(want.Opacity - got.Opacity) <= tolerance))
                differences.Add($"{where}: opacity expected {want.Opacity}, got {got.Opacity}");
            if (!(Math.Abs(want.ZSort - got.ZSort) <= tolerance))
                differences.Add($"{where}: z-sort expected {want.ZSort}, got {got.ZSort}");
            if (!Close(want.Tint, got.Tint, tolerance))
                differences.Add($"{where}: tint expected {want.Tint}, got {got.Tint}");
            if (!Close(want.ScreenTint, got.ScreenTint, tolerance))
                differences.Add($"{where}: screen tint expected {want.ScreenTint}, got {got.ScreenTint}");
            if (stage2Outputs && !(Math.Abs(want.EmissionStrength - got.EmissionStrength) <= tolerance))
                differences.Add($"{where}: emission strength expected {want.EmissionStrength}, got {got.EmissionStrength}");
            if (stage2Outputs && !(Math.Abs(want.MaskThreshold - got.MaskThreshold) <= tolerance))
                differences.Add($"{where}: mask threshold expected {want.MaskThreshold}, got {got.MaskThreshold}");
            if (stage4Outputs && !(want.Masks ?? []).SequenceEqual(got.Masks ?? []))
                differences.Add($"{where}: masks expected [{Describe(want.Masks)}], got [{Describe(got.Masks)}]");
            ComparePositions(where, want.Positions, got.Positions, tolerance, differences);
        }
    }

    private static void CompareLayers(IReadOnlyList<SnapshotLayer> expected, IReadOnlyList<SnapshotLayer> actual,
        float tolerance, List<string> differences)
    {
        if (!expected.Select(l => l.Uuid).SequenceEqual(actual.Select(l => l.Uuid)))
        {
            differences.Add($"layers: expected [{string.Join(", ", expected.Select(l => l.Uuid))}], got [{string.Join(", ", actual.Select(l => l.Uuid))}]");
            return;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            var want = expected[i];
            var got = actual[i];
            var where = $"layer {i} '{want.Name}' ({want.Uuid})";
            if (want.Layer != got.Layer)
                differences.Add($"{where}: enclosing layer expected {want.Layer?.ToString() ?? "none"}, got {got.Layer?.ToString() ?? "none"}");
            if (want.BlendMode != got.BlendMode)
                differences.Add($"{where}: blend mode expected {want.BlendMode}, got {got.BlendMode}");
            if (!(Math.Abs(want.Opacity - got.Opacity) <= tolerance))
                differences.Add($"{where}: opacity expected {want.Opacity}, got {got.Opacity}");
            if (!(Math.Abs(want.ZSort - got.ZSort) <= tolerance))
                differences.Add($"{where}: z-sort expected {want.ZSort}, got {got.ZSort}");
            if (!Close(want.Tint, got.Tint, tolerance))
                differences.Add($"{where}: tint expected {want.Tint}, got {got.Tint}");
            if (!Close(want.ScreenTint, got.ScreenTint, tolerance))
                differences.Add($"{where}: screen tint expected {want.ScreenTint}, got {got.ScreenTint}");
            if (!want.Masks.SequenceEqual(got.Masks))
                differences.Add($"{where}: masks expected [{Describe(want.Masks)}], got [{Describe(got.Masks)}]");
        }
    }

    private static string Describe(SnapshotMask[]? masks) =>
        string.Join(", ", (masks ?? []).Select(m => $"{m.Source} {m.Mode}"));

    private static void ComparePositions(string where, float[] expected, float[] actual, float tolerance, List<string> differences)
    {
        if (expected.Length != actual.Length)
        {
            differences.Add($"{where}: expected {expected.Length / 2} vertices, got {actual.Length / 2}");
            return;
        }

        int first = -1, count = 0;
        var worst = 0f;
        for (var i = 0; i < expected.Length; i++)
        {
            var delta = Math.Abs(expected[i] - actual[i]);
            if (delta <= tolerance)
                continue;
            if (first < 0)
                first = i;
            count++;
            worst = float.IsNaN(delta) || float.IsNaN(worst) ? float.NaN : Math.Max(worst, delta);
        }

        if (count > 0)
        {
            var v = first / 2;
            differences.Add($"{where}: {count} vertex coordinate(s) moved (largest by {worst}); the first is vertex {v}: " +
                            $"expected ({expected[v * 2]}, {expected[v * 2 + 1]}), got ({actual[v * 2]}, {actual[v * 2 + 1]})");
        }
    }

    private static string Describe(IReadOnlyList<SnapshotDrawable> list) =>
        string.Join(", ", list.Take(8).Select(d => d.Uuid)) + (list.Count > 8 ? ", ..." : "");

    private static bool Close(System.Numerics.Vector2 a, System.Numerics.Vector2 b, float tolerance) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;

    private static bool Close(System.Numerics.Vector3 a, System.Numerics.Vector3 b, float tolerance) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance && Math.Abs(a.Z - b.Z) <= tolerance;

    private static void CompareParameters(IReadOnlyList<SnapshotParameter> expected, IReadOnlyList<SnapshotParameter> actual,
        float tolerance, List<string> differences)
    {
        var actualByUuid = new Dictionary<uint, SnapshotParameter>(actual.Count);
        foreach (var parameter in actual)
            actualByUuid.TryAdd(parameter.Uuid, parameter);

        foreach (var want in expected)
        {
            if (!actualByUuid.Remove(want.Uuid, out var got))
            {
                differences.Add($"parameter '{want.Name}' ({want.Uuid}): missing");
                continue;
            }

            if (Math.Abs(want.X - got.X) > tolerance || Math.Abs(want.Y - got.Y) > tolerance)
                differences.Add($"parameter '{want.Name}' ({want.Uuid}): expected ({want.X}, {want.Y}), got ({got.X}, {got.Y})");
        }

        foreach (var extra in actualByUuid.Values)
            differences.Add($"parameter '{extra.Name}' ({extra.Uuid}): not in the saved snapshot");
    }
}
