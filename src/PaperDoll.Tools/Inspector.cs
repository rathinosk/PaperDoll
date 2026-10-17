using System.Globalization;
using System.Numerics;
using System.Text;

namespace PaperDoll.Tools;

/// <summary>Prints a loaded puppet for <c>paperdoll inspect</c>.</summary>
internal sealed class Inspector(TextWriter output, int maxDepth)
{
    public static readonly string[] Sections = ["meta", "tree", "params", "anims", "textures", "ext", "issues"];

    public void Write(Puppet puppet, string path, IReadOnlySet<string> sections)
    {
        output.WriteLine($"{Path.GetFileName(path)}: {puppet.Nodes.Count} nodes, {puppet.Parameters.Count} parameters, " +
                         $"{puppet.Animations.Count} animations, {puppet.Textures.Count} textures, {puppet.Extensions.Count} extensions");

        if (sections.Contains("meta"))
            WriteMeta(puppet);
        if (sections.Contains("tree"))
            WriteTree(puppet);
        if (sections.Contains("params"))
            WriteParameters(puppet);
        if (sections.Contains("anims"))
            WriteAnimations(puppet);
        if (sections.Contains("textures"))
            WriteTextures(puppet);
        if (sections.Contains("ext"))
            WriteExtensions(puppet);
        if (sections.Contains("issues"))
            WriteIssues(puppet);
    }

    private void Heading(string title)
    {
        output.WriteLine();
        output.WriteLine($"== {title}");
    }

    private void WriteMeta(Puppet puppet)
    {
        Heading("Meta");
        var m = puppet.Meta;
        Field("name", m.Name);
        Field("version", m.Version);
        Field("rigger", m.Rigger);
        Field("artist", m.Artist);
        Field("copyright", m.Copyright);
        Field("licence", m.LicenseUrl);
        Field("contact", m.Contact);
        Field("reference", m.Reference);
        Field("thumbnail", m.ThumbnailId?.ToString(CultureInfo.InvariantCulture));
        Field("preservePixels", m.PreservePixels ? "yes" : "no");
        if (m.Rights is { } r)
        {
            Field("rights", $"users={r.AllowedUsers}, commercial={r.AllowCommercial}, violence={r.AllowViolence}, sexual={r.AllowSexual}, " +
                            $"redistribution={r.AllowRedistribution}, modification={r.AllowModification}, attribution={r.RequireAttribution}");
        }

        Field("physics", $"pixelsPerMeter={F(puppet.Physics.PixelsPerMeter)}, gravity={F(puppet.Physics.Gravity)}");

        void Field(string name, string? value) => output.WriteLine($"  {name,-15} {(string.IsNullOrEmpty(value) ? "-" : value)}");
    }

    private void WriteTree(Puppet puppet)
    {
        Heading("Node tree");
        var counts = puppet.Nodes.GroupBy(n => n.Kind).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key}");
        output.WriteLine($"  {string.Join(", ", counts)}");
        WriteNode(puppet.Root, 0);
    }

    private void WriteNode(Node node, int depth)
    {
        if (depth > maxDepth)
            return;

        var line = new StringBuilder();
        line.Append(' ', 2 + depth * 2).Append(node.Kind).Append(" '").Append(node.Name).Append("' #").Append(node.Uuid);
        if (node.ZSort != 0)
            line.Append(" z=").Append(F(node.ZSort));
        if (!node.Enabled)
            line.Append(" disabled");
        if (node.LockToRoot)
            line.Append(" lockToRoot");

        switch (node)
        {
            case Part part:
                line.Append($" verts={part.Mesh.Vertices.Length} tris={part.Mesh.Indices.Length / 3}");
                line.Append(" tex=[").Append(string.Join(",", part.TextureSlots.Select(s => s?.ToString(CultureInfo.InvariantCulture) ?? "-"))).Append(']');
                if (part.Opacity != 1)
                    line.Append(" opacity=").Append(F(part.Opacity));
                if (part.Tint != Vector3.One)
                    line.Append(" tint=").Append(V(part.Tint));
                if (part.BlendMode != BlendMode.Normal)
                    line.Append(" blend=").Append(part.BlendMode);
                AppendMasks(line, part.Masks);
                break;
            case Composite composite:
                if (composite.Opacity != 1)
                    line.Append(" opacity=").Append(F(composite.Opacity));
                if (composite.BlendMode != BlendMode.Normal)
                    line.Append(" blend=").Append(composite.BlendMode);
                if (composite.PropagateMeshGroup)
                    line.Append(" propagateMeshGroup");
                AppendMasks(line, composite.Masks);
                break;
            case MeshGroup group:
                line.Append($" verts={group.Mesh.Vertices.Length} tris={group.Mesh.Indices.Length / 3}");
                if (group.DynamicDeformation)
                    line.Append(" dynamic");
                if (group.TranslateChildren)
                    line.Append(" translateChildren");
                break;
            case Mask mask:
                line.Append($" verts={mask.Mesh.Vertices.Length} tris={mask.Mesh.Indices.Length / 3}");
                break;
            case SimplePhysics physics:
                line.Append($" {physics.Model}/{physics.MapMode} param={physics.ParameterUuid?.ToString(CultureInfo.InvariantCulture) ?? "-"} " +
                            $"length={F(physics.Length)} freq={F(physics.Frequency)} out={V(physics.OutputScale)}");
                break;
        }

        output.WriteLine(line.ToString());
        if (depth == maxDepth && node.Children.Count > 0)
        {
            output.WriteLine($"{new string(' ', 4 + depth * 2)}... {node.Children.Count} children");
            return;
        }

        foreach (var child in node.Children)
            WriteNode(child, depth + 1);
    }

    private static void AppendMasks(StringBuilder line, IReadOnlyList<MaskBinding> masks)
    {
        if (masks.Count > 0)
            line.Append(" masks=[").Append(string.Join(",", masks.Select(m => $"{(m.Mode == MaskingMode.Mask ? "" : "dodge:")}{m.SourceUuid}"))).Append(']');
    }

    private void WriteParameters(Puppet puppet)
    {
        Heading("Parameters");
        var groups = puppet.ParameterGroups.ToDictionary(g => g.Uuid, g => g.Name);
        foreach (var p in puppet.Parameters)
        {
            var range = p.IsVec2
                ? $"2D x[{F(p.Min.X)}..{F(p.Max.X)}] y[{F(p.Min.Y)}..{F(p.Max.Y)}] default {V(p.Defaults)}"
                : $"1D [{F(p.Min.X)}..{F(p.Max.X)}] default {F(p.Defaults.X)}";
            var keys = p.IsVec2 ? $"{p.GetAxisPointCount(0)}x{p.GetAxisPointCount(1)}" : $"{p.GetAxisPointCount(0)}";
            var group = p.GroupUuid is { } g && groups.TryGetValue(g, out var name) ? $" group '{name}'" : "";
            output.WriteLine($"  '{p.Name}' #{p.Uuid} {range}, {keys} keys, {p.MergeMode}{group}");

            foreach (var target in p.Bindings.GroupBy(b => b.PropertyName).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                var modes = string.Join("/", target.Select(b => b.Interpolation).Distinct());
                output.WriteLine($"      {target.Key,-20} {target.Count(),3} binding(s), {modes}");
            }
        }

        if (puppet.ParameterGroups.Count > 0)
            output.WriteLine($"  groups: {string.Join(", ", puppet.ParameterGroups.Select(g => $"'{g.Name}'"))}");
    }

    private void WriteAnimations(Puppet puppet)
    {
        Heading("Animations");
        if (puppet.Animations.Count == 0)
            output.WriteLine("  none");
        foreach (var (name, a) in puppet.Animations.OrderBy(a => a.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"  '{name}' {a.Length} frames at {F(a.Timestep)}s, leadIn {a.LeadIn}, leadOut {a.LeadOut}" +
                             $"{(a.Additive ? ", additive" : "")}{(a.Weight is { } w ? $", weight {F(w)}" : "")}, {a.Lanes.Count} lanes, " +
                             $"{a.Lanes.Sum(l => l.Keyframes.Count)} keyframes");
            foreach (var lane in a.Lanes)
            {
                var target = lane.Parameter is { } p ? $"'{p.Name}'{(p.IsVec2 ? (lane.TargetAxis == 0 ? " X" : " Y") : "")}" : $"missing parameter {lane.ParameterUuid}";
                var keys = lane.Keyframes.Count == 0 ? "no keys" : $"{lane.Keyframes.Count} keys, frames {lane.Keyframes[0].Frame}-{lane.Keyframes[^1].Frame}";
                output.WriteLine($"      {target}: {lane.Interpolation}, {lane.MergeMode}, {keys}");
            }
        }
    }

    private void WriteTextures(Puppet puppet)
    {
        Heading("Textures");
        var used = puppet.Nodes.OfType<Part>().SelectMany(p => p.TextureSlots).OfType<int>().ToHashSet();
        foreach (var t in puppet.Textures)
        {
            var encoding = t.Encoding?.ToString() ?? $"unknown({t.RawEncoding})";
            var size = t.IsEmpty ? "empty" : $"{t.Width}x{t.Height}, {t.EncodedData.Length:N0} bytes";
            output.WriteLine($"  {t.Index,4} {encoding,-4} {size}{(used.Contains(t.Index) ? "" : ", unused")}");
        }
    }

    private void WriteExtensions(Puppet puppet)
    {
        Heading("Extensions");
        if (puppet.Extensions.Count == 0)
            output.WriteLine("  none");
        foreach (var (name, data) in puppet.Extensions.OrderBy(e => e.Key, StringComparer.Ordinal))
            output.WriteLine($"  {name} ({data.Length:N0} bytes)");
    }

    private void WriteIssues(Puppet puppet)
    {
        Heading("Load issues");
        if (puppet.Issues.Count == 0)
            output.WriteLine("  none");
        foreach (var issue in puppet.Issues)
            output.WriteLine($"  {issue}");
    }

    private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string V(Vector2 v) => $"({F(v.X)}, {F(v.Y)})";

    private static string V(Vector3 v) => $"({F(v.X)}, {F(v.Y)}, {F(v.Z)})";
}
