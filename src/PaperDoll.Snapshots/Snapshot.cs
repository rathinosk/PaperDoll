namespace PaperDoll.Snapshots;

/// <summary>
/// A record of a puppet instance at given inputs and what Core produced for them. Saved from the demo once a result
/// looks right, then replayed and compared on later runs to catch regressions.
/// </summary>
/// <remarks>
/// The output is the parameter values read back from the instance and, from format version 2, its draw list and
/// bounds. Format version 3 adds each item's emission strength and mask threshold. Format version 4 adds each Part's masks and the Composite layers; the drawables stay the Parts alone, in draw order, so snapshots saved
/// with version 3 still compare. Format version 5 adds the running animations to the inputs (each replays paused
/// at its frame) and records the evaluated parameter values, animations applied, as output; without animations they are
/// the values read back, so earlier snapshots still compare. Format version 6 records the update step the
/// instance was advanced by, so a puppet whose physics has not settled replays the same steps; earlier snapshots replay
/// their time as one step. Any change to what is recorded raises <see cref="CurrentFormatVersion"/>.
/// </remarks>
public sealed class Snapshot
{
    /// <summary>The format version this build writes.</summary>
    public const int CurrentFormatVersion = 6;

    /// <summary>Creates a snapshot.</summary>
    /// <param name="name">The snapshot's name; letters, digits, <c>-</c>, <c>_</c> and <c>.</c> only (it is the file name).</param>
    /// <param name="puppetPath">The puppet file, relative to the samples folder, with <c>/</c> separators.</param>
    /// <param name="time">Seconds the instance had been updated for.</param>
    /// <param name="inputs">Parameter values set on the instance.</param>
    /// <param name="parameters">Output: parameter values read back from the instance.</param>
    /// <param name="drawables">Output: the draw list, back to front (empty before format version 2).</param>
    /// <param name="bounds">Output: the instance's bounds (zero before format version 2).</param>
    /// <param name="formatVersion">The format version the snapshot was written with.</param>
    /// <param name="layers">Output: the Composite layers, in draw order (empty before format version 4).</param>
    /// <param name="animations">Input: the animations running on the instance, in the puppet's order (format version 5).</param>
    /// <param name="step">Input: the update step the instance was advanced by, in seconds; 0 replays <paramref name="time"/> as one step (format version 6).</param>
    public Snapshot(string name, string puppetPath, double time, IReadOnlyList<SnapshotParameter> inputs,
        IReadOnlyList<SnapshotParameter> parameters, IReadOnlyList<SnapshotDrawable>? drawables = null,
        PuppetBounds bounds = default, int formatVersion = CurrentFormatVersion, IReadOnlyList<SnapshotLayer>? layers = null,
        IReadOnlyList<SnapshotAnimation>? animations = null, double step = 0)
    {
        if (!IsValidName(name))
            throw new ArgumentException($"'{name}' is not a valid snapshot name: use letters, digits, '-', '_' and '.', not starting with '.'.", nameof(name));
        ArgumentException.ThrowIfNullOrEmpty(puppetPath);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(parameters);
        if (!double.IsFinite(step) || step < 0)
            throw new ArgumentOutOfRangeException(nameof(step), step, "The step must be finite and not negative.");

        Name = name;
        PuppetPath = puppetPath.Replace('\\', '/');
        Time = time;
        Step = step;
        Inputs = inputs;
        Parameters = parameters;
        Drawables = drawables ?? [];
        Layers = layers ?? [];
        Animations = animations ?? [];
        Bounds = bounds;
        FormatVersion = formatVersion;
    }

    /// <summary>The snapshot's name, unique per puppet.</summary>
    public string Name { get; }

    /// <summary>The puppet file, relative to the samples folder, with <c>/</c> separators.</summary>
    public string PuppetPath { get; }

    /// <summary>Seconds the instance had been updated for.</summary>
    public double Time { get; }

    /// <summary>
    /// The update step the instance was advanced by, in seconds, replayed as <see cref="Time"/> divided by it (rounded) equal
    /// steps; 0 replays <see cref="Time"/> as one step (format version 6; 0 in earlier ones).
    /// </summary>
    public double Step { get; }

    /// <summary>Parameter values set on the instance.</summary>
    public IReadOnlyList<SnapshotParameter> Inputs { get; }

    /// <summary>Input: the animations running on the instance, each with its frame and strength (format version 5).</summary>
    public IReadOnlyList<SnapshotAnimation> Animations { get; }

    /// <summary>Output: the parameter values the instance was evaluated at (from format version 5, animations applied).</summary>
    public IReadOnlyList<SnapshotParameter> Parameters { get; }

    /// <summary>Output: the draw list's Parts, back to front.</summary>
    public IReadOnlyList<SnapshotDrawable> Drawables { get; }

    /// <summary>Output: the draw list's Composite layers, in the order they end (format version 4).</summary>
    public IReadOnlyList<SnapshotLayer> Layers { get; }

    /// <summary>Output: the instance's bounds in puppet space.</summary>
    public PuppetBounds Bounds { get; }

    /// <summary>The format version the snapshot was written with.</summary>
    public int FormatVersion { get; }

    /// <summary>Records an instance's current inputs and output.</summary>
    /// <param name="name">The snapshot's name (see <see cref="IsValidName"/>).</param>
    /// <param name="puppetPath">The puppet file, relative to the samples folder.</param>
    /// <param name="instance">The instance to record.</param>
    /// <param name="step">
    /// The step every <see cref="PuppetInstance.Update"/> of the instance was given, if they were all the same (as with a
    /// fixed time step), so replay takes the same steps; 0 replays the instance's time as one step.
    /// </param>
    public static Snapshot Capture(string name, string puppetPath, PuppetInstance instance, double step = 0)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return new Snapshot(name, puppetPath, instance.Time, ReadParameters(instance, evaluated: false),
            ReadParameters(instance, evaluated: true), ReadDrawables(instance), instance.Bounds, layers: ReadLayers(instance),
            animations: ReadAnimations(instance), step: step);
    }

    /// <summary>
    /// Replays a snapshot's inputs on a new instance of <paramref name="puppet"/> and records the result under the same name,
    /// ready to compare with <see cref="SnapshotComparer.Compare"/>.
    /// </summary>
    public static Snapshot Replay(Snapshot expected, Puppet puppet)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(puppet);

        var instance = puppet.CreateInstance();
        foreach (var input in expected.Inputs)
        {
            if (puppet.TryGetParameter(input.Uuid, out var parameter))
                instance.SetParameterValue(parameter, new(input.X, input.Y));
        }

        foreach (var animation in expected.Animations)
        {
            if (!instance.TryGetAnimation(animation.Name, out var playback))
                continue;
            playback.Play();
            playback.Pause();
            playback.Seek(animation.Frame);
            playback.Strength = animation.Strength;
        }

        if (expected.Step > 0)
        {
            var steps = (long)Math.Round(expected.Time / expected.Step);
            for (var i = 0L; i < steps; i++)
                instance.Update((float)expected.Step);
        }
        else
        {
            instance.Update((float)expected.Time);
        }

        return Capture(expected.Name, expected.PuppetPath, instance, expected.Step);
    }

    /// <summary>Whether <paramref name="name"/> can name a snapshot: letters, digits, <c>-</c>, <c>_</c> and <c>.</c>, not starting with <c>.</c>.</summary>
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 100 || name[0] == '.')
            return false;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.'))
                return false;
        }

        return true;
    }

    private static SnapshotDrawable[] ReadDrawables(PuppetInstance instance)
    {
        var drawables = new List<SnapshotDrawable>();
        foreach (var item in instance.DrawList)
        {
            if (item.Part is not { } part)
                continue;
            var positions = new float[item.Positions.Length * 2];
            for (var v = 0; v < item.Positions.Length; v++)
            {
                positions[v * 2] = item.Positions[v].X;
                positions[v * 2 + 1] = item.Positions[v].Y;
            }

            drawables.Add(new(part.Uuid, part.Name, item.Texture ?? -1, item.Layer?.Uuid, item.Opacity,
                item.Tint, item.ScreenTint, item.BlendMode.ToString(), item.ZSort, positions, item.EmissionStrength, item.MaskThreshold,
                ReadMasks(item)));
        }

        return [.. drawables];
    }

    private static SnapshotLayer[] ReadLayers(PuppetInstance instance)
    {
        var layers = new List<SnapshotLayer>();
        foreach (var item in instance.DrawList)
        {
            if (item.Kind == DrawItemKind.EndComposite && item.Composite is { } composite)
            {
                layers.Add(new(composite.Uuid, composite.Name, item.Layer?.Uuid, item.Opacity, item.Tint, item.ScreenTint,
                    item.BlendMode.ToString(), item.ZSort, ReadMasks(item)));
            }
        }

        return [.. layers];
    }

    private static SnapshotMask[] ReadMasks(DrawItem item)
    {
        var masks = new SnapshotMask[item.Masks.Count];
        for (var i = 0; i < masks.Length; i++)
            masks[i] = new(item.Masks[i].Source.Node.Uuid, item.Masks[i].Mode.ToString());
        return masks;
    }

    private static SnapshotAnimation[] ReadAnimations(PuppetInstance instance) =>
        [.. instance.Animations.Where(a => a.IsRunning).Select(a => new SnapshotAnimation(a.Name, a.FramePosition, a.Strength))];

    private static SnapshotParameter[] ReadParameters(PuppetInstance instance, bool evaluated)
    {
        var parameters = instance.Puppet.Parameters;
        var values = new SnapshotParameter[parameters.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var value = evaluated ? instance.GetEvaluatedValue(parameters[i]) : instance.GetParameterValue(parameters[i]);
            values[i] = new(parameters[i].Uuid, parameters[i].Name, value.X, value.Y);
        }

        return values;
    }
}

/// <summary>One item of an instance's draw list in a snapshot.</summary>
/// <param name="Uuid">The Part's ID.</param>
/// <param name="Name">The Part's name, for people reading the file.</param>
/// <param name="Texture">The albedo texture slot, or -1.</param>
/// <param name="Layer">The enclosing Composite's ID, or <see langword="null"/>.</param>
/// <param name="Opacity">Opacity.</param>
/// <param name="Tint">Tint (RGB).</param>
/// <param name="ScreenTint">Screen tint (RGB).</param>
/// <param name="BlendMode">The blend mode's name.</param>
/// <param name="ZSort">The effective z-sort.</param>
/// <param name="Positions">Vertex positions in puppet space, x and y interleaved.</param>
/// <param name="EmissionStrength">Emission strength (format version 3).</param>
/// <param name="MaskThreshold">Mask threshold (format version 3).</param>
/// <param name="Masks">The Part's masks (format version 4).</param>
public sealed record SnapshotDrawable(uint Uuid, string Name, int Texture, uint? Layer, float Opacity,
    System.Numerics.Vector3 Tint, System.Numerics.Vector3 ScreenTint, string BlendMode, float ZSort, float[] Positions,
    float EmissionStrength = 1f, float MaskThreshold = 0.5f, SnapshotMask[]? Masks = null);

/// <summary>A Composite's layer in a snapshot (format version 4).</summary>
/// <param name="Uuid">The Composite's ID.</param>
/// <param name="Name">The Composite's name, for people reading the file.</param>
/// <param name="Layer">The enclosing Composite's ID, or <see langword="null"/>.</param>
/// <param name="Opacity">Opacity of the layer.</param>
/// <param name="Tint">Tint (RGB).</param>
/// <param name="ScreenTint">Screen tint (RGB).</param>
/// <param name="BlendMode">The blend mode's name.</param>
/// <param name="ZSort">The effective z-sort.</param>
/// <param name="Masks">The layer's masks.</param>
public sealed record SnapshotLayer(uint Uuid, string Name, uint? Layer, float Opacity, System.Numerics.Vector3 Tint,
    System.Numerics.Vector3 ScreenTint, string BlendMode, float ZSort, SnapshotMask[] Masks);

/// <summary>One mask in a snapshot: the source's ID and the mode's name.</summary>
/// <param name="Source">The mask source's ID.</param>
/// <param name="Mode">The masking mode's name.</param>
public readonly record struct SnapshotMask(uint Source, string Mode);

/// <summary>An animation running when a snapshot was taken (format version 5); replayed paused at its frame.</summary>
/// <param name="Name">The animation's name.</param>
/// <param name="Frame">Its position in frames.</param>
/// <param name="Strength">Its strength, 0 to 1.</param>
public readonly record struct SnapshotAnimation(string Name, float Frame, float Strength);

/// <summary>A parameter's value in a snapshot. The name is kept for people reading the file; the ID identifies it.</summary>
/// <param name="Uuid">The parameter's ID.</param>
/// <param name="Name">The parameter's name.</param>
/// <param name="X">The X value.</param>
/// <param name="Y">The Y value (the default for 1D parameters).</param>
public readonly record struct SnapshotParameter(uint Uuid, string Name, float X, float Y);
