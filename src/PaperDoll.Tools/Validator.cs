using System.Diagnostics;

namespace PaperDoll.Tools;

/// <summary>
/// <c>paperdoll validate</c>: loads a puppet and lists what PaperDoll skips, ignores or does not support
/// (docs/features.md).
/// </summary>
internal sealed class Validator(TextWriter output, bool decode)
{
    // Binding targets PaperDoll evaluates, per node type (docs/features.md).
    private static readonly string[] TransformTargets =
        ["transform.t.x", "transform.t.y", "transform.t.z", "transform.r.x", "transform.r.y", "transform.r.z", "transform.s.x", "transform.s.y", "zSort"];
    private static readonly string[] VisualTargets =
        ["opacity", "tint.r", "tint.g", "tint.b", "screenTint.r", "screenTint.g", "screenTint.b", "emissionStrength", "alphaThreshold"];
    private static readonly string[] PhysicsTargets =
        ["gravity", "length", "frequency", "angleDamping", "lengthDamping", "outputScale.x", "outputScale.y"];

    public bool Validate(string path)
    {
        output.WriteLine($"{path}");
        Puppet puppet;
        var clock = Stopwatch.StartNew();
        try
        {
            puppet = Puppet.Load(path);
        }
        catch (Exception ex) when (ex is PuppetFormatException or IOException or UnauthorizedAccessException)
        {
            output.WriteLine($"  FAILED to load: {ex.Message}");
            output.WriteLine();
            return false;
        }

        output.WriteLine($"  loaded in {clock.ElapsedMilliseconds} ms: {puppet.Nodes.Count} nodes, {puppet.Parameters.Count} parameters, " +
                         $"{puppet.Animations.Count} animations, {puppet.Textures.Count} textures (writer: {puppet.Meta.Version ?? "unknown"})");

        var ok = true;
        if (decode)
            ok &= DecodeTextures(puppet);

        WriteIssues(puppet);
        WriteUnsupported(puppet);
        output.WriteLine();
        return ok;
    }

    private bool DecodeTextures(Puppet puppet)
    {
        var clock = Stopwatch.StartNew();
        int decoded = 0, failed = 0;
        foreach (var texture in puppet.Textures)
        {
            if (!texture.CanDecode)
                continue;
            try
            {
                var image = texture.Decode();
                if (image.Width != texture.Width || image.Height != texture.Height || image.Rgba.Length != image.Width * image.Height * 4)
                    throw new PuppetFormatException("decoded size does not match the header");
                decoded++;
            }
            catch (PuppetFormatException ex)
            {
                output.WriteLine($"  texture {texture.Index}: {ex.Message}");
                failed++;
            }
        }

        output.WriteLine($"  textures: {decoded} decoded to RGBA in {clock.ElapsedMilliseconds} ms" + (failed > 0 ? $", {failed} FAILED" : ""));
        return failed == 0;
    }

    private void WriteIssues(Puppet puppet)
    {
        if (puppet.Issues.Count == 0)
            return;

        output.WriteLine("  Skipped, ignored or repaired while loading:");
        foreach (var group in puppet.Issues.GroupBy(i => i.Kind).OrderBy(g => g.Key))
        {
            foreach (var issue in group)
                output.WriteLine($"    {issue}");
        }
    }

    private void WriteUnsupported(Puppet puppet)
    {
        var nodes = puppet.Nodes;
        var parts = nodes.OfType<Part>().ToList();
        var composites = nodes.OfType<Composite>().ToList();

        var unsupported = new List<string>();
        var bindings = puppet.Parameters.SelectMany(p => p.Bindings).ToList();
        foreach (var group in bindings.GroupBy(b => (Target: TargetKind(puppet, b), b.PropertyName)).OrderBy(g => g.Key.PropertyName, StringComparer.Ordinal))
        {
            if (!IsEvaluated(group.Key.Target, group.Key.PropertyName))
                unsupported.Add($"{group.Count()} '{group.Key.PropertyName}' binding(s) on {group.Key.Target}");
        }

        if (nodes.Any(n => n.PinToMesh))
            unsupported.Add($"{nodes.Count(n => n.PinToMesh)} node(s) pinned to a mesh (pinToMesh)");
        if (nodes.OfType<Drawable>().Any(d => d.HasWeldedLinks))
            unsupported.Add($"{nodes.OfType<Drawable>().Count(d => d.HasWeldedLinks)} drawable(s) with vertex welding");
        var badSources = parts.SelectMany(p => p.Masks).Concat(composites.SelectMany(c => c.Masks))
            .Count(m => puppet.TryGetNode(m.SourceUuid, out var source) && source is not (Part or Mask));
        if (badSources > 0)
            unsupported.Add($"{badSources} mask(s) whose source is not a Part or Mask (left out)");
        var extraTextures = parts.Count(p => p.TextureSlots.Where(s => s is not null).Skip(1).Any());
        if (extraTextures > 0)
            unsupported.Add($"{extraTextures} Part(s) with emissive or bump textures (only the albedo is drawn)");
        if (puppet.Textures.Any(t => !t.IsEmpty && !t.CanDecode))
            unsupported.Add($"{puppet.Textures.Count(t => !t.IsEmpty && !t.CanDecode)} texture(s) that cannot be decoded");

        if (unsupported.Count > 0)
        {
            output.WriteLine("  Out of scope (not planned):");
            foreach (var item in unsupported)
                output.WriteLine($"    {item}");
        }
    }

    private static string TargetKind(Puppet puppet, ParameterBinding binding) =>
        binding is ParameterAxisBinding ? "Parameter"
        : puppet.TryGetNode(binding.TargetUuid, out var node) ? node.Kind.ToString()
        : "?";

    private static bool IsEvaluated(string target, string property) => (target, property) switch
    {
        ("Parameter", _) => false,
        _ when TransformTargets.Contains(property) => true,
        ("Part" or "MeshGroup" or "Mask", "deform") => true,
        ("Part" or "Composite", _) => VisualTargets.Contains(property),
        ("SimplePhysics", _) => PhysicsTargets.Contains(property),
        _ => false,
    };
}
