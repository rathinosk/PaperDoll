using System.Text.Json;

namespace PaperDoll.Snapshots;

/// <summary>
/// Saves and loads snapshots as JSON under <c>&lt;samples&gt;/snapshots/&lt;puppet path without extension&gt;/&lt;name&gt;.json</c>.
/// They sit beside the sample puppets, so they stay git-ignored with them.
/// </summary>
public sealed class SnapshotStore
{
    /// <summary>The folder under the samples folder that holds snapshots.</summary>
    public const string FolderName = "snapshots";

    /// <summary>Creates a store for a samples folder.</summary>
    /// <param name="samplesRoot">The samples folder (usually <c>.samples/</c>; see <see cref="SampleLocator"/>).</param>
    public SnapshotStore(string samplesRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(samplesRoot);
        SamplesRoot = Path.GetFullPath(samplesRoot);
        Root = Path.Combine(SamplesRoot, FolderName);
    }

    /// <summary>The samples folder that puppet paths are relative to.</summary>
    public string SamplesRoot { get; }

    /// <summary>The folder that holds the snapshots.</summary>
    public string Root { get; }

    /// <summary>
    /// The puppet's path relative to <see cref="SamplesRoot"/> with <c>/</c> separators, or <see langword="null"/> if the file
    /// is outside it (such a puppet cannot have snapshots, since they must replay on other machines).
    /// </summary>
    public string? RelativePuppetPath(string puppetFile)
    {
        var relative = Path.GetRelativePath(SamplesRoot, Path.GetFullPath(puppetFile));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(FolderName + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return null;
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>The full path of a puppet given its path relative to <see cref="SamplesRoot"/>.</summary>
    public string PuppetFile(string relativePuppetPath) =>
        Path.Combine(SamplesRoot, relativePuppetPath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The file a snapshot is saved to.</summary>
    public string PathOf(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return PathOf(snapshot.PuppetPath, snapshot.Name);
    }

    /// <summary>The file a snapshot of the given puppet and name is saved to.</summary>
    public string PathOf(string relativePuppetPath, string name)
    {
        if (!Snapshot.IsValidName(name))
            throw new ArgumentException($"'{name}' is not a valid snapshot name.", nameof(name));
        return Path.Combine(FolderOf(relativePuppetPath), name + ".json");
    }

    /// <summary>Saves a snapshot, replacing any with the same puppet and name. Returns the file written.</summary>
    public string Save(Snapshot snapshot)
    {
        var path = PathOf(snapshot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
            SnapshotJson.Write(snapshot, stream);
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>Loads every saved snapshot, ordered by puppet and name.</summary>
    /// <exception cref="SnapshotFormatException">A snapshot file is malformed.</exception>
    public IReadOnlyList<Snapshot> LoadAll() => Load(Root);

    /// <summary>Loads the snapshots saved for one puppet, ordered by name.</summary>
    /// <param name="relativePuppetPath">The puppet's path relative to <see cref="SamplesRoot"/>.</param>
    /// <exception cref="SnapshotFormatException">A snapshot file is malformed.</exception>
    public IReadOnlyList<Snapshot> LoadFor(string relativePuppetPath)
    {
        var folder = FolderOf(relativePuppetPath);
        return Load(folder, SearchOption.TopDirectoryOnly)
            .Where(s => s.PuppetPath == relativePuppetPath.Replace('\\', '/'))
            .ToList();
    }

    /// <summary>Loads one snapshot file.</summary>
    /// <exception cref="SnapshotFormatException">The file is malformed.</exception>
    public static Snapshot LoadFile(string path)
    {
        using var stream = File.OpenRead(path);
        try
        {
            return SnapshotJson.Read(stream);
        }
        catch (SnapshotFormatException ex)
        {
            throw new SnapshotFormatException($"{path}: {ex.Message}", ex);
        }
    }

    private string FolderOf(string relativePuppetPath)
    {
        var withoutExtension = Path.ChangeExtension(relativePuppetPath.Replace('\\', '/'), null);
        return Path.Combine(Root, withoutExtension.Replace('/', Path.DirectorySeparatorChar));
    }

    private static List<Snapshot> Load(string folder, SearchOption search = SearchOption.AllDirectories)
    {
        if (!Directory.Exists(folder))
            return [];

        return Directory.EnumerateFiles(folder, "*.json", search)
            .Select(LoadFile)
            .OrderBy(s => s.PuppetPath, StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>A snapshot file could not be read.</summary>
public sealed class SnapshotFormatException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SnapshotFormatException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>The snapshot JSON format. Written by hand (no reflection), so the demo stays trimmable.</summary>
internal static class SnapshotJson
{
    public static void Write(Snapshot snapshot, Stream stream)
    {
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteNumber("formatVersion", Snapshot.CurrentFormatVersion);
        json.WriteString("name", snapshot.Name);
        json.WriteString("puppet", snapshot.PuppetPath);

        json.WriteStartObject("inputs");
        json.WriteNumber("time", snapshot.Time);
        json.WriteNumber("step", snapshot.Step);
        WriteParameters(json, "parameters", snapshot.Inputs);
        json.WriteStartArray("animations");
        foreach (var a in snapshot.Animations)
        {
            json.WriteStartObject();
            json.WriteString("name", a.Name);
            json.WriteNumber("frame", a.Frame);
            json.WriteNumber("strength", a.Strength);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();

        json.WriteStartObject("output");
        WriteParameters(json, "parameters", snapshot.Parameters);
        json.WriteStartArray("bounds");
        json.WriteNumberValue(snapshot.Bounds.Min.X);
        json.WriteNumberValue(snapshot.Bounds.Min.Y);
        json.WriteNumberValue(snapshot.Bounds.Max.X);
        json.WriteNumberValue(snapshot.Bounds.Max.Y);
        json.WriteEndArray();
        WriteDrawables(json, snapshot.Drawables);
        WriteLayers(json, snapshot.Layers);
        json.WriteEndObject();

        json.WriteEndObject();
    }

    public static Snapshot Read(Stream stream)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException ex)
        {
            throw new SnapshotFormatException($"not valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            try
            {
                var root = document.RootElement;
                var version = root.GetProperty("formatVersion").GetInt32();
                if (version > Snapshot.CurrentFormatVersion)
                    throw new SnapshotFormatException($"format version {version} is newer than this build reads ({Snapshot.CurrentFormatVersion}).");

                var inputs = root.GetProperty("inputs");
                var output = root.GetProperty("output");
                PuppetBounds bounds = default;
                IReadOnlyList<SnapshotDrawable> drawables = [];
                IReadOnlyList<SnapshotLayer> layers = [];
                if (version >= 2)
                {
                    var b = ReadFloats(output.GetProperty("bounds"));
                    if (b.Length != 4)
                        throw new FormatException("'bounds' needs four numbers.");
                    bounds = new PuppetBounds(new(b[0], b[1]), new(b[2], b[3]));
                    drawables = ReadDrawables(output.GetProperty("drawables"), version);
                }

                if (version >= 4)
                    layers = ReadLayers(output.GetProperty("layers"));

                var animations = new List<SnapshotAnimation>();
                if (version >= 5)
                {
                    foreach (var a in inputs.GetProperty("animations").EnumerateArray())
                        animations.Add(new(a.GetProperty("name").GetString()!, a.GetProperty("frame").GetSingle(), a.GetProperty("strength").GetSingle()));
                }

                return new Snapshot(
                    root.GetProperty("name").GetString()!,
                    root.GetProperty("puppet").GetString()!,
                    inputs.GetProperty("time").GetDouble(),
                    ReadParameters(inputs.GetProperty("parameters")),
                    ReadParameters(output.GetProperty("parameters")),
                    drawables,
                    bounds,
                    version,
                    layers,
                    animations,
                    version >= 6 ? inputs.GetProperty("step").GetDouble() : 0);
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
            {
                throw new SnapshotFormatException($"malformed snapshot: {ex.Message}", ex);
            }
        }
    }

    private static void WriteParameters(Utf8JsonWriter json, string name, IReadOnlyList<SnapshotParameter> parameters)
    {
        json.WriteStartArray(name);
        foreach (var p in parameters)
        {
            json.WriteStartObject();
            json.WriteNumber("uuid", p.Uuid);
            json.WriteString("name", p.Name);
            json.WriteNumber("x", p.X);
            json.WriteNumber("y", p.Y);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteDrawables(Utf8JsonWriter json, IReadOnlyList<SnapshotDrawable> drawables)
    {
        json.WriteStartArray("drawables");
        foreach (var d in drawables)
        {
            json.WriteStartObject();
            json.WriteNumber("uuid", d.Uuid);
            json.WriteString("name", d.Name);
            json.WriteNumber("texture", d.Texture);
            if (d.Layer is { } layer)
                json.WriteNumber("layer", layer);
            else
                json.WriteNull("layer");
            json.WriteNumber("opacity", d.Opacity);
            WriteVector(json, "tint", d.Tint);
            WriteVector(json, "screenTint", d.ScreenTint);
            json.WriteString("blendMode", d.BlendMode);
            json.WriteNumber("zsort", d.ZSort);
            json.WriteNumber("emissionStrength", d.EmissionStrength);
            json.WriteNumber("maskThreshold", d.MaskThreshold);
            WriteMasks(json, d.Masks ?? []);
            json.WriteStartArray("positions");
            foreach (var value in d.Positions)
                json.WriteNumberValue(value);
            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteLayers(Utf8JsonWriter json, IReadOnlyList<SnapshotLayer> layers)
    {
        json.WriteStartArray("layers");
        foreach (var l in layers)
        {
            json.WriteStartObject();
            json.WriteNumber("uuid", l.Uuid);
            json.WriteString("name", l.Name);
            if (l.Layer is { } layer)
                json.WriteNumber("layer", layer);
            else
                json.WriteNull("layer");
            json.WriteNumber("opacity", l.Opacity);
            WriteVector(json, "tint", l.Tint);
            WriteVector(json, "screenTint", l.ScreenTint);
            json.WriteString("blendMode", l.BlendMode);
            json.WriteNumber("zsort", l.ZSort);
            WriteMasks(json, l.Masks);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static void WriteMasks(Utf8JsonWriter json, SnapshotMask[] masks)
    {
        json.WriteStartArray("masks");
        foreach (var m in masks)
        {
            json.WriteStartObject();
            json.WriteNumber("source", m.Source);
            json.WriteString("mode", m.Mode);
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static SnapshotMask[] ReadMasks(JsonElement array)
    {
        var masks = new SnapshotMask[array.GetArrayLength()];
        var i = 0;
        foreach (var m in array.EnumerateArray())
            masks[i++] = new(m.GetProperty("source").GetUInt32(), m.GetProperty("mode").GetString() ?? "");
        return masks;
    }

    private static List<SnapshotLayer> ReadLayers(JsonElement array)
    {
        var layers = new List<SnapshotLayer>(array.GetArrayLength());
        foreach (var l in array.EnumerateArray())
        {
            var layer = l.GetProperty("layer");
            layers.Add(new(
                l.GetProperty("uuid").GetUInt32(),
                l.GetProperty("name").GetString() ?? "",
                layer.ValueKind == JsonValueKind.Null ? null : layer.GetUInt32(),
                l.GetProperty("opacity").GetSingle(),
                ReadVector(l.GetProperty("tint")),
                ReadVector(l.GetProperty("screenTint")),
                l.GetProperty("blendMode").GetString() ?? "",
                l.GetProperty("zsort").GetSingle(),
                ReadMasks(l.GetProperty("masks"))));
        }

        return layers;
    }

    private static void WriteVector(Utf8JsonWriter json, string name, System.Numerics.Vector3 value)
    {
        json.WriteStartArray(name);
        json.WriteNumberValue(value.X);
        json.WriteNumberValue(value.Y);
        json.WriteNumberValue(value.Z);
        json.WriteEndArray();
    }

    private static List<SnapshotDrawable> ReadDrawables(JsonElement array, int version)
    {
        var drawables = new List<SnapshotDrawable>(array.GetArrayLength());
        foreach (var d in array.EnumerateArray())
        {
            var layer = d.GetProperty("layer");
            drawables.Add(new(
                d.GetProperty("uuid").GetUInt32(),
                d.GetProperty("name").GetString() ?? "",
                d.GetProperty("texture").GetInt32(),
                layer.ValueKind == JsonValueKind.Null ? null : layer.GetUInt32(),
                d.GetProperty("opacity").GetSingle(),
                ReadVector(d.GetProperty("tint")),
                ReadVector(d.GetProperty("screenTint")),
                d.GetProperty("blendMode").GetString() ?? "",
                d.GetProperty("zsort").GetSingle(),
                ReadFloats(d.GetProperty("positions")),
                version >= 3 ? d.GetProperty("emissionStrength").GetSingle() : 1f,
                version >= 3 ? d.GetProperty("maskThreshold").GetSingle() : 0.5f,
                version >= 4 ? ReadMasks(d.GetProperty("masks")) : null));
        }

        return drawables;
    }

    private static System.Numerics.Vector3 ReadVector(JsonElement array)
    {
        var values = ReadFloats(array);
        if (values.Length != 3)
            throw new FormatException("A colour needs three numbers.");
        return new(values[0], values[1], values[2]);
    }

    private static float[] ReadFloats(JsonElement array)
    {
        var values = new float[array.GetArrayLength()];
        var i = 0;
        foreach (var value in array.EnumerateArray())
            values[i++] = value.GetSingle();
        return values;
    }

    private static List<SnapshotParameter> ReadParameters(JsonElement array)
    {
        var parameters = new List<SnapshotParameter>(array.GetArrayLength());
        foreach (var p in array.EnumerateArray())
        {
            parameters.Add(new(
                p.GetProperty("uuid").GetUInt32(),
                p.GetProperty("name").GetString() ?? "",
                p.GetProperty("x").GetSingle(),
                p.GetProperty("y").GetSingle()));
        }

        return parameters;
    }
}
