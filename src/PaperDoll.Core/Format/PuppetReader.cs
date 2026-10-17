// Reads an INP1 puppet: the container, then the JSON payload into the node tree, parameters, bindings and animations,
// with the references between them resolved (binding targets, physics and animation parameters). Nothing is evaluated
// here. Field names and defaults follow nijilive; whatever is skipped, ignored or repaired goes into the load report
// instead of failing the load.

using System.Numerics;
using System.Text.Json;

namespace PaperDoll.Format;

internal sealed class PuppetReader
{
    // Mannequin Character Generator writes these; no Inochi2D runtime reads them.
    private static readonly string[] McgNodeKeys = ["poseID", "mqnFilename", "hiddenBy", "fullyLinkedTo"];
    private static readonly string[] McgBindingKeys = ["lrVariant", "multiplier", "adjustmentMultiplier"];

    // Inochi Creator records each Part's source PSD layer; editor data, as in every sample.
    private static readonly string[] EditorPartKeys = ["psdLayerPath"];

    private static readonly KeySet RootKeys = new("Puppet", ["meta", "physics", "nodes", "param", "automation", "animations", "groups"]);
    private static readonly KeySet MetaKeys = new("Meta", ["name", "version", "rigger", "artist", "rights", "copyright", "licenseURL", "contact", "reference", "thumbnailId", "preservePixels"]);
    private static readonly KeySet RightsKeys = new("Rights", ["allowedUsers", "allowViolence", "allowSexual", "allowCommercial", "allowRedistribution", "allowModification", "requireAttribution"]);
    private static readonly KeySet PhysicsKeys = new("Physics", ["pixelsPerMeter", "gravity"]);
    private static readonly KeySet GroupKeys = new("ParameterGroup", ["groupUUID", "name", "color"], ["count"]);
    private static readonly KeySet TransformKeys = new("Transform", ["trans", "rot", "scale"]);
    private static readonly KeySet MeshKeys = new("Mesh", ["verts", "uvs", "indices", "origin", "grid_axes"]);
    private static readonly KeySet MaskBindingKeys = new("MaskBinding", ["source", "mode"]);

    private static readonly KeySet NodeKeys = new("Node", ["uuid", "name", "type", "enabled", "zsort", "transform", "lockToRoot", "pinToMesh", "children"], McgNodeKeys);
    private static readonly KeySet DrawableKeys = NodeKeys.Extend("Drawable", ["mesh", "weldedLinks"]);
    private static readonly KeySet PartKeys = DrawableKeys.Extend("Part", ["textures", "opacity", "mask_threshold", "tint", "screenTint", "emissionStrength", "blend_mode", "masked_by", "mask_mode", "masks"], EditorPartKeys);
    private static readonly KeySet CompositeKeys = NodeKeys.Extend("Composite", ["opacity", "mask_threshold", "tint", "screenTint", "blend_mode", "masks", "propagate_meshgroup"]);
    private static readonly KeySet MeshGroupKeys = DrawableKeys.Extend("MeshGroup", ["dynamic_deformation", "translate_children"]);
    private static readonly KeySet MaskKeys = DrawableKeys.Extend("Mask", []);
    private static readonly KeySet SimplePhysicsKeys = NodeKeys.Extend("SimplePhysics", ["param", "model_type", "map_mode", "gravity", "length", "frequency", "angle_damping", "length_damping", "output_scale", "local_only"]);

    private static readonly KeySet ParameterKeys = new("Parameter", ["uuid", "name", "is_vec2", "min", "max", "defaults", "axis_points", "merge_mode", "bindings", "parentUUID"], ["poseID"]); // poseID: Mannequin Character Generator (newer versions also write it on parameters)
    private static readonly KeySet BindingKeys = new("Binding", ["node", "param_name", "values", "isSet", "interpolate_mode"], McgBindingKeys);
    private static readonly KeySet AnimationKeys = new("Animation", ["timestep", "additive", "animationWeight", "length", "leadIn", "leadOut", "lanes"]);
    private static readonly KeySet LaneKeys = new("AnimationLane", ["interpolation", "uuid", "target", "keyframes", "merge_mode"]);
    private static readonly KeySet KeyframeKeys = new("Keyframe", ["frame", "value", "tension"]);

    // Node types of the format that are out of PaperDoll's scope (docs/design/format.md). Anything else unknown is reported as such.
    private static readonly HashSet<string> OutOfScopeNodeTypes = ["GridDeformer", "PathDeformer", "DynamicComposite", "Shapes", "AnimatedPart", "Drawable", "Tmp"];

    private readonly ReadContext _ctx = new();
    private readonly List<Node> _nodes = [];
    private readonly Dictionary<uint, Node> _nodesByUuid = [];
    private int _textureCount;

    public static Puppet Read(ReadOnlyMemory<byte> data) => new PuppetReader().ReadPuppet(data);

    private Puppet ReadPuppet(ReadOnlyMemory<byte> data)
    {
        var container = InpContainer.Parse(data);
        _textureCount = container.Textures.Count;
        var textures = ReadTextures(container.Textures);

        using var document = ParseJson(container.Payload);
        var json = document.RootElement;
        if (json.ValueKind != JsonValueKind.Object)
            throw new PuppetFormatException("The puppet data is not a JSON object.");
        _ctx.CheckKeys(json, RootKeys);

        var meta = ReadMeta(json);
        var physics = ReadPhysics(json);

        if (!ReadContext.TryGet(json, "nodes", out var nodesJson) || nodesJson.ValueKind != JsonValueKind.Object)
            throw new PuppetFormatException("The puppet data has no node tree ('nodes').");
        var root = ReadRoot(nodesJson);

        var (parameters, parametersByUuid) = ReadParameters(json);
        var groups = ReadGroups(json);
        var animations = ReadAnimations(json, parametersByUuid);
        ReadAutomation(json);

        CheckReferences(parametersByUuid);

        var extensions = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
        foreach (var (name, blob) in container.Extensions)
            extensions[name] = blob;

        if (container.TrailingBytes > 0)
            _ctx.Report(LoadIssueKind.UnknownKey, "Container", $"{container.TrailingBytes} bytes after the last section ignored");

        return new Puppet(meta, physics, root, _nodes, _nodesByUuid, parameters, parametersByUuid, groups,
            animations, textures, extensions, _ctx.BuildIssues());
    }

    private static JsonDocument ParseJson(ReadOnlyMemory<byte> payload)
    {
        var span = payload.Span;
        var start = span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        var end = span.Length;
        while (end > start && span[end - 1] is 0 or (byte)' ' or (byte)'\n' or (byte)'\r' or (byte)'\t')
            end--;

        try
        {
            return JsonDocument.Parse(payload[start..end], new JsonDocumentOptions
            {
                MaxDepth = 1024,
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            throw new PuppetFormatException($"The puppet data is not valid JSON: {ex.Message}", ex);
        }
    }

    // ---- Textures -------------------------------------------------------------------------------------------------

    private List<PuppetTexture> ReadTextures(List<TextureBlob> blobs)
    {
        var textures = new List<PuppetTexture>(blobs.Count);
        for (var i = 0; i < blobs.Count; i++)
        {
            var (encoding, data) = blobs[i];
            int width = 0, height = 0;
            if (!data.IsEmpty)
            {
                switch (encoding)
                {
                    case (byte)TextureEncoding.Png or (byte)TextureEncoding.Tga:
                        (width, height) = PuppetTexture.ReadHeader(data);
                        if (width <= 0 || height <= 0)
                            _ctx.Report(LoadIssueKind.InvalidValue, $"Texture {i}", $"the {(TextureEncoding)encoding} header could not be read; the texture cannot be decoded");
                        break;
                    case (byte)TextureEncoding.Bc7:
                        _ctx.Report(LoadIssueKind.UnsupportedFeature, "Textures", "BC7 textures are not supported");
                        break;
                    default:
                        _ctx.Report(LoadIssueKind.UnsupportedFeature, $"Texture {i}", $"unknown texture encoding {encoding}");
                        break;
                }
            }

            textures.Add(new PuppetTexture(i, encoding, data, width, height));
        }

        return textures;
    }

    // ---- Meta and physics -----------------------------------------------------------------------------------------

    private PuppetMeta ReadMeta(JsonElement json)
    {
        const string loc = "meta";
        if (!ReadContext.TryGet(json, "meta", out var m) || m.ValueKind != JsonValueKind.Object)
        {
            _ctx.Report(LoadIssueKind.InvalidValue, loc, "missing; using empty metadata");
            return new PuppetMeta();
        }

        _ctx.CheckKeys(m, MetaKeys);
        var thumbnail = _ctx.Id(m, "thumbnailId", loc);
        return new PuppetMeta
        {
            Name = _ctx.String(m, "name", loc),
            Version = _ctx.String(m, "version", loc),
            Rigger = _ctx.String(m, "rigger", loc),
            Artist = _ctx.String(m, "artist", loc),
            Rights = ReadRights(m),
            Copyright = _ctx.String(m, "copyright", loc),
            LicenseUrl = _ctx.String(m, "licenseURL", loc),
            Contact = _ctx.String(m, "contact", loc),
            Reference = _ctx.String(m, "reference", loc),
            ThumbnailId = thumbnail is { } t && t <= int.MaxValue ? (int)t : null,
            PreservePixels = _ctx.Bool(m, "preservePixels", false, loc),
        };
    }

    private PuppetUsageRights? ReadRights(JsonElement meta)
    {
        const string loc = "meta.rights";
        if (!ReadContext.TryGet(meta, "rights", out var r) || r.ValueKind != JsonValueKind.Object)
            return null;

        _ctx.CheckKeys(r, RightsKeys);
        return new PuppetUsageRights
        {
            AllowedUsers = _ctx.String(r, "allowedUsers", loc),
            AllowViolence = _ctx.Bool(r, "allowViolence", false, loc),
            AllowSexual = _ctx.Bool(r, "allowSexual", false, loc),
            AllowCommercial = _ctx.Bool(r, "allowCommercial", false, loc),
            AllowRedistribution = _ctx.String(r, "allowRedistribution", loc),
            AllowModification = _ctx.String(r, "allowModification", loc),
            RequireAttribution = _ctx.Bool(r, "requireAttribution", false, loc),
        };
    }

    private PuppetPhysicsSettings ReadPhysics(JsonElement json)
    {
        var defaults = new PuppetPhysicsSettings();
        if (!ReadContext.TryGet(json, "physics", out var p) || p.ValueKind != JsonValueKind.Object)
            return defaults;

        _ctx.CheckKeys(p, PhysicsKeys);
        return new PuppetPhysicsSettings(
            _ctx.Float(p, "pixelsPerMeter", defaults.PixelsPerMeter, "physics"),
            _ctx.Float(p, "gravity", defaults.Gravity, "physics"));
    }

    // ---- Nodes ----------------------------------------------------------------------------------------------------

    private Node ReadRoot(JsonElement json)
    {
        // The root is read as a plain Node whatever its type, and named "Root".
        var type = ReadContext.TryGet(json, "type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "Node";
        if (type != "Node")
            _ctx.Report(LoadIssueKind.InvalidValue, "Root", $"the root node has type '{type}'; it is read as a plain Node");

        _ctx.CheckKeys(json, KeysFor(type) ?? NodeKeys);
        var root = new Node();
        ReadNodeFields(root, json);
        root.Name = "Root";
        Register(root);
        ReadChildren(root, json);
        return root;
    }

    private static KeySet? KeysFor(string type) => type switch
    {
        "Node" => NodeKeys,
        "Part" => PartKeys,
        "Composite" => CompositeKeys,
        "MeshGroup" => MeshGroupKeys,
        "Mask" => MaskKeys,
        "SimplePhysics" => SimplePhysicsKeys,
        _ => null,
    };

    private void ReadChildren(Node parent, JsonElement json)
    {
        if (!ReadContext.TryGet(json, "children", out var children))
            return;
        if (children.ValueKind != JsonValueKind.Array)
        {
            _ctx.Report(LoadIssueKind.InvalidValue, Describe(parent), $"'children' is {ReadContext.Describe(children)}, not an array; ignored");
            return;
        }

        var list = new List<Node>(children.GetArrayLength());
        foreach (var child in children.EnumerateArray())
        {
            if (child.ValueKind != JsonValueKind.Object || !ReadContext.TryGet(child, "type", out var typeJson) || typeJson.ValueKind != JsonValueKind.String)
                throw new PuppetFormatException($"A child of {Describe(parent)} has no node type.");

            var type = typeJson.GetString()!;
            var node = CreateNode(type);
            if (node is null)
            {
                var name = _ctx.Name(child, "name", "Node");
                var uuid = _ctx.Id(child, "uuid", "Node");
                var skipped = CountSubtree(child);
                var why = OutOfScopeNodeTypes.Contains(type) ? "not supported (out of scope)" : "unknown node type";
                var extra = skipped > 1 ? $", with {skipped - 1} descendant node(s)" : "";
                _ctx.Report(LoadIssueKind.SkippedNode, $"{type} '{name}' ({uuid?.ToString() ?? "no ID"})",
                    $"{why}; skipped under {Describe(parent)}{extra}");
                continue;
            }

            _ctx.CheckKeys(child, KeysFor(type)!);
            node.Parent = parent;
            ReadNodeFields(node, child);
            Register(node);
            ReadTypeFields(node, child);
            ReadChildren(node, child);
            list.Add(node);
        }

        parent.Children = list;
    }

    private static Node? CreateNode(string type) => type switch
    {
        "Node" => new Node(),
        "Part" => new Part(),
        "Composite" => new Composite(),
        "MeshGroup" => new MeshGroup(),
        "Mask" => new Mask(),
        "SimplePhysics" => new SimplePhysics(),
        _ => null,
    };

    private static int CountSubtree(JsonElement json)
    {
        var count = 1;
        if (ReadContext.TryGet(json, "children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                count += CountSubtree(child);
        }

        return count;
    }

    private void Register(Node node)
    {
        node.Index = _nodes.Count;
        _nodes.Add(node);
        if (!_nodesByUuid.TryAdd(node.Uuid, node))
            _ctx.Report(LoadIssueKind.InvalidValue, Describe(node), $"duplicate node ID {node.Uuid}; lookups find the first node with it");
    }

    private void ReadNodeFields(Node node, JsonElement json)
    {
        var loc = node.Kind.ToString();
        node.Uuid = _ctx.Id(json, "uuid", loc) ?? uint.MaxValue;
        node.Name = _ctx.Name(json, "name", loc);
        node.Enabled = _ctx.Bool(json, "enabled", true, loc);
        node.ZSort = _ctx.Float(json, "zsort", 0f, loc);
        node.LockToRoot = _ctx.Bool(json, "lockToRoot", false, loc);
        node.PinToMesh = _ctx.Bool(json, "pinToMesh", false, loc);
        if (node.PinToMesh)
            _ctx.Report(LoadIssueKind.UnsupportedFeature, "Node", "'pinToMesh' is not supported");

        if (ReadContext.TryGet(json, "transform", out var transform) && transform.ValueKind == JsonValueKind.Object)
        {
            _ctx.CheckKeys(transform, TransformKeys);
            node.Transform = new NodeTransform(
                _ctx.Vec3(transform, "trans", Vector3.Zero, "Transform"),
                _ctx.Vec3(transform, "rot", Vector3.Zero, "Transform"),
                _ctx.Vec2(transform, "scale", Vector2.One, "Transform"));
        }
    }

    private void ReadTypeFields(Node node, JsonElement json)
    {
        var loc = node.Kind.ToString();
        if (node is Drawable drawable)
        {
            drawable.Mesh = ReadMesh(json, node);
            drawable.HasWeldedLinks = ReadContext.TryGet(json, "weldedLinks", out var welded) &&
                                      (welded.ValueKind != JsonValueKind.Array || welded.GetArrayLength() > 0);
            if (drawable.HasWeldedLinks)
                _ctx.Report(LoadIssueKind.UnsupportedFeature, loc, "vertex welding ('weldedLinks') is not supported");
        }

        switch (node)
        {
            case Part part:
                part.TextureSlots = ReadTextureSlots(json, part);
                part.Opacity = _ctx.Float(json, "opacity", 1f, loc);
                part.MaskThreshold = _ctx.Float(json, "mask_threshold", 0.5f, loc);
                part.Tint = _ctx.Vec3(json, "tint", Vector3.One, loc);
                part.ScreenTint = _ctx.Vec3(json, "screenTint", Vector3.Zero, loc);
                part.EmissionStrength = _ctx.Float(json, "emissionStrength", 1f, loc);
                part.BlendMode = _ctx.Enum(json, "blend_mode", BlendMode.Normal, loc);
                part.Masks = ReadMasks(json, loc, allowLegacy: true);
                break;

            case Composite composite:
                composite.Opacity = _ctx.Float(json, "opacity", 1f, loc);
                composite.MaskThreshold = _ctx.Float(json, "mask_threshold", 0.5f, loc);
                composite.Tint = _ctx.Vec3(json, "tint", Vector3.One, loc);
                composite.ScreenTint = _ctx.Vec3(json, "screenTint", Vector3.Zero, loc);
                composite.BlendMode = _ctx.Enum(json, "blend_mode", BlendMode.Normal, loc);
                composite.Masks = ReadMasks(json, loc, allowLegacy: false);
                composite.PropagateMeshGroup = _ctx.Bool(json, "propagate_meshgroup", false, loc);
                break;

            case MeshGroup meshGroup:
                meshGroup.DynamicDeformation = _ctx.Bool(json, "dynamic_deformation", false, loc);
                meshGroup.TranslateChildren = _ctx.Bool(json, "translate_children", false, loc);
                break;

            case SimplePhysics physics:
                physics.ParameterUuid = _ctx.Id(json, "param", loc);
                physics.Model = _ctx.Enum(json, "model_type", PhysicsModel.Pendulum, loc);
                physics.MapMode = _ctx.Enum(json, "map_mode", PhysicsMapMode.AngleLength, loc);
                physics.Gravity = _ctx.Float(json, "gravity", 1f, loc);
                physics.Length = _ctx.Float(json, "length", 100f, loc);
                physics.Frequency = _ctx.Float(json, "frequency", 1f, loc);
                physics.AngleDamping = _ctx.Float(json, "angle_damping", 0.5f, loc);
                physics.LengthDamping = _ctx.Float(json, "length_damping", 0.5f, loc);
                physics.OutputScale = _ctx.Vec2(json, "output_scale", Vector2.One, loc);
                physics.LocalOnly = _ctx.Bool(json, "local_only", false, loc);
                break;
        }
    }

    private MeshData ReadMesh(JsonElement json, Node node)
    {
        if (!ReadContext.TryGet(json, "mesh", out var mesh) || mesh.ValueKind != JsonValueKind.Object)
            return MeshData.Empty;

        _ctx.CheckKeys(mesh, MeshKeys);
        var where = Describe(node);
        var vertices = ReadPoints(mesh, "verts", where);
        var uvs = ReadPoints(mesh, "uvs", where);
        var origin = _ctx.Vec2(mesh, "origin", Vector2.Zero, "Mesh");

        var indices = new List<ushort>();
        if (ReadContext.TryGet(mesh, "indices", out var indexJson) && indexJson.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in indexJson.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Number && element.TryGetUInt16(out var index))
                    indices.Add(index);
                else
                    _ctx.Report(LoadIssueKind.InvalidValue, where, $"mesh index {ReadContext.Describe(element)} is not a 16-bit index; dropped");
            }
        }

        var gridAxes = new List<float[]>();
        if (ReadContext.TryGet(mesh, "grid_axes", out var axesJson) && axesJson.ValueKind == JsonValueKind.Array)
        {
            foreach (var axis in axesJson.EnumerateArray())
                gridAxes.Add(ReadFloats(axis, $"{where} mesh.grid_axes"));
        }

        if (uvs.Length != 0 && uvs.Length != vertices.Length)
            _ctx.Report(LoadIssueKind.InvalidValue, where, $"mesh has {vertices.Length} vertices but {uvs.Length} UVs");
        if (indices.Count % 3 != 0)
            _ctx.Report(LoadIssueKind.InvalidValue, where, $"mesh index count {indices.Count} is not a multiple of 3");
        foreach (var index in indices)
        {
            if (index >= vertices.Length)
            {
                _ctx.Report(LoadIssueKind.InvalidValue, where, $"mesh index {index} is past the last vertex ({vertices.Length})");
                break;
            }
        }

        return new MeshData(vertices, uvs, [.. indices], origin, [.. gridAxes]);
    }

    // Points are stored flat: [x0, y0, x1, y1, ...].
    private Vector2[] ReadPoints(JsonElement mesh, string key, string where)
    {
        if (!ReadContext.TryGet(mesh, key, out var json))
            return [];
        var flat = ReadFloats(json, $"{where} mesh.{key}");
        if (flat.Length % 2 != 0)
            _ctx.Report(LoadIssueKind.InvalidValue, where, $"mesh.{key} has an odd number of values; the last is dropped");

        var points = new Vector2[flat.Length / 2];
        for (var i = 0; i < points.Length; i++)
            points[i] = new Vector2(flat[i * 2], flat[i * 2 + 1]);
        return points;
    }

    private float[] ReadFloats(JsonElement json, string location)
    {
        if (json.ValueKind != JsonValueKind.Array)
        {
            _ctx.Report(LoadIssueKind.InvalidValue, location, $"expected an array, found {ReadContext.Describe(json)}");
            return [];
        }

        var values = new float[json.GetArrayLength()];
        var i = 0;
        foreach (var element in json.EnumerateArray())
            values[i++] = _ctx.Float(element, 0f, location);
        return values;
    }

    private int?[] ReadTextureSlots(JsonElement json, Part part)
    {
        if (!ReadContext.TryGet(json, "textures", out var slots) || slots.ValueKind != JsonValueKind.Array)
            return [];

        var result = new int?[slots.GetArrayLength()];
        var i = 0;
        foreach (var slot in slots.EnumerateArray())
        {
            var id = _ctx.Id(slot, $"{Describe(part)} textures");
            if (id is { } value && value >= (uint)_textureCount)
            {
                _ctx.Report(LoadIssueKind.DanglingReference, Describe(part), $"texture slot {i} points at texture {value}, but the file has {_textureCount}");
                id = null;
            }

            result[i++] = (int?)id;
        }

        return result;
    }

    private MaskBinding[] ReadMasks(JsonElement json, string loc, bool allowLegacy)
    {
        // Current form: "masks": [{ "source": id, "mode": "Mask" }]. It replaces the legacy form when both are present.
        if (ReadContext.TryGet(json, "masks", out var masks) && masks.ValueKind == JsonValueKind.Array)
        {
            var list = new List<MaskBinding>();
            foreach (var mask in masks.EnumerateArray())
            {
                if (mask.ValueKind != JsonValueKind.Object)
                    continue;
                _ctx.CheckKeys(mask, MaskBindingKeys);
                if (_ctx.Id(mask, "source", "MaskBinding") is { } source)
                    list.Add(new MaskBinding(source, _ctx.Enum(mask, "mode", MaskingMode.Mask, "MaskBinding")));
            }

            return [.. list];
        }

        // Legacy form (Parts only): "masked_by": [ids] with one "mask_mode" for all.
        if (allowLegacy && ReadContext.TryGet(json, "masked_by", out var maskedBy) && maskedBy.ValueKind == JsonValueKind.Array)
        {
            var mode = _ctx.Enum(json, "mask_mode", MaskingMode.Mask, loc);
            var list = new List<MaskBinding>();
            foreach (var id in maskedBy.EnumerateArray())
            {
                if (_ctx.Id(id, $"{loc}.masked_by") is { } source)
                    list.Add(new MaskBinding(source, mode));
            }

            return [.. list];
        }

        return [];
    }

    // ---- Parameters -----------------------------------------------------------------------------------------------

    private (List<Parameter>, Dictionary<uint, Parameter>) ReadParameters(JsonElement json)
    {
        var parameters = new List<Parameter>();
        var byUuid = new Dictionary<uint, Parameter>();
        if (!ReadContext.TryGet(json, "param", out var array))
            return (parameters, byUuid);
        if (array.ValueKind != JsonValueKind.Array)
        {
            _ctx.Report(LoadIssueKind.InvalidValue, "param", $"expected an array, found {ReadContext.Describe(array)}; no parameters read");
            return (parameters, byUuid);
        }

        var pending = new List<(Parameter Parameter, JsonElement Json)>();
        foreach (var p in array.EnumerateArray())
        {
            if (p.ValueKind != JsonValueKind.Object)
                continue;
            _ctx.CheckKeys(p, ParameterKeys);
            var parameter = ReadParameter(p);
            parameter.Index = parameters.Count;
            parameters.Add(parameter);
            if (!byUuid.TryAdd(parameter.Uuid, parameter))
                _ctx.Report(LoadIssueKind.InvalidValue, Describe(parameter), $"duplicate parameter ID {parameter.Uuid}; lookups find the first parameter with it");
            pending.Add((parameter, p));
        }

        // Bindings may target other parameters, so they are read once every parameter is known.
        foreach (var (parameter, p) in pending)
            parameter.Bindings = ReadBindings(parameter, p, byUuid);

        return (parameters, byUuid);
    }

    private Parameter ReadParameter(JsonElement json)
    {
        const string loc = "Parameter";
        var isVec2 = _ctx.Bool(json, "is_vec2", false, loc);
        var parameter = new Parameter
        {
            Uuid = _ctx.Id(json, "uuid", loc) ?? uint.MaxValue,
            Name = _ctx.Name(json, "name", loc),
            IsVec2 = isVec2,
            Min = _ctx.Vec2(json, "min", Vector2.Zero, loc),
            Max = _ctx.Vec2(json, "max", Vector2.One, loc),
            Defaults = _ctx.Vec2(json, "defaults", Vector2.Zero, loc),
            MergeMode = _ctx.Enum(json, "merge_mode", ParameterMergeMode.Additive, loc),
            GroupUuid = _ctx.Id(json, "parentUUID", loc),
        };

        float[][] axisPoints = isVec2 ? [[0f, 1f], [0f, 1f]] : [[0f, 1f], [0f]];
        if (ReadContext.TryGet(json, "axis_points", out var axes))
        {
            if (axes.ValueKind == JsonValueKind.Array && axes.GetArrayLength() == 2)
            {
                var i = 0;
                foreach (var axis in axes.EnumerateArray())
                    axisPoints[i++] = ReadFloats(axis, $"{Describe(parameter)} axis_points");
            }
            else
            {
                _ctx.Report(LoadIssueKind.InvalidValue, Describe(parameter), "'axis_points' is not two arrays; using the defaults");
            }
        }

        if (axisPoints[0].Length == 0 || axisPoints[1].Length == 0)
            throw new PuppetFormatException($"{Describe(parameter)} has an axis with no axis points.");

        parameter.AxisPointArrays = axisPoints;
        return parameter;
    }

    private List<ParameterBinding> ReadBindings(Parameter parameter, JsonElement json, Dictionary<uint, Parameter> parameters)
    {
        var bindings = new List<ParameterBinding>();
        if (!ReadContext.TryGet(json, "bindings", out var array) || array.ValueKind != JsonValueKind.Array)
            return bindings;

        foreach (var b in array.EnumerateArray())
        {
            if (b.ValueKind != JsonValueKind.Object)
                continue;
            _ctx.CheckKeys(b, BindingKeys);

            // A binding without a target property is skipped.
            if (!ReadContext.TryGet(b, "param_name", out var nameJson))
            {
                _ctx.Report(LoadIssueKind.InvalidValue, Describe(parameter), "a binding has no 'param_name'; skipped");
                continue;
            }

            var target = _ctx.Id(b, "node", "Binding");
            var binding = ReadBinding(parameter, b, nameJson, target ?? uint.MaxValue);

            // A binding whose target is neither a node nor a parameter is dropped.
            var exists = target is { } id && (_nodesByUuid.ContainsKey(id) || parameters.ContainsKey(id));
            if (!exists)
            {
                _ctx.Report(LoadIssueKind.DanglingReference, Describe(parameter),
                    $"binding '{binding.PropertyName}' targets {target?.ToString() ?? "nothing"}, which is not in the puppet (or was skipped); dropped");
                continue;
            }

            bindings.Add(binding);
        }

        return bindings;
    }

    private ParameterBinding ReadBinding(Parameter parameter, JsonElement json, JsonElement nameJson, uint target)
    {
        var name = nameJson.ValueKind == JsonValueKind.String ? nameJson.GetString()! : nameJson.GetRawText();
        var axisIndex = nameJson.ValueKind == JsonValueKind.Number && nameJson.TryGetInt32(out var n) ? n : -1;
        var interpolation = _ctx.Enum(json, "interpolate_mode", InterpolateMode.Linear, "Binding");
        var context = $"{Describe(parameter)}, binding '{name}' on {target}";

        ParameterBinding binding;
        if (name == "deform")
        {
            var deform = new DeformBinding { TargetUuid = target, PropertyName = name, Interpolation = interpolation };
            deform.OffsetArrays = ReadGrid(parameter, json, context, cell => ReadOffsets(cell, context));
            RecoverDeforms(deform, context);
            binding = deform;
        }
        else if (name is "X" or "Y" || axisIndex is 0 or 1)
        {
            var axis = name == "Y" || axisIndex == 1 ? 1 : 0;
            binding = new ParameterAxisBinding
            {
                TargetUuid = target,
                PropertyName = axis == 0 ? "X" : "Y",
                TargetAxis = axis,
                Interpolation = interpolation,
                ValueArrays = ReadGrid(parameter, json, context, cell => _ctx.Float(cell, 0f, context)),
            };
        }
        else
        {
            binding = new ValueBinding
            {
                TargetUuid = target,
                PropertyName = name,
                Interpolation = interpolation,
                ValueArrays = ReadGrid(parameter, json, context, cell => _ctx.Float(cell, 0f, context)),
            };
        }

        binding.IsSetArrays = ReadGrid(parameter, json, context, cell => cell.ValueKind == JsonValueKind.True, "isSet");
        return binding;
    }

    // Reads values[x][y] (or isSet[x][y]); the shape must match the parameter's axis points.
    private static T[][] ReadGrid<T>(Parameter parameter, JsonElement json, string context, Func<JsonElement, T> readCell, string key = "values")
    {
        var xCount = parameter.GetAxisPointCount(0);
        var yCount = parameter.GetAxisPointCount(1);
        if (!ReadContext.TryGet(json, key, out var rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() != xCount)
            throw new PuppetFormatException($"{context}: '{key}' must have {xCount} rows, one per X axis point.");

        var grid = new T[xCount][];
        var x = 0;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != yCount)
                throw new PuppetFormatException($"{context}: '{key}' row {x} must have {yCount} entries, one per Y axis point.");

            var cells = new T[yCount];
            var y = 0;
            foreach (var cell in row.EnumerateArray())
                cells[y++] = readCell(cell);
            grid[x++] = cells;
        }

        return grid;
    }

    private Vector2[] ReadOffsets(JsonElement cell, string context)
    {
        if (cell.ValueKind != JsonValueKind.Array)
            throw new PuppetFormatException($"{context}: a deform keyform is {ReadContext.Describe(cell)}, not an array of offsets.");

        var offsets = new Vector2[cell.GetArrayLength()];
        var i = 0;
        foreach (var element in cell.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() < 2)
                throw new PuppetFormatException($"{context}: deform vertex {i} has fewer than two components.");

            var x = ReadContext.TryNumber(element[0], out var vx) ? (float)vx : float.NaN;
            var y = ReadContext.TryNumber(element[1], out var vy) ? (float)vy : float.NaN;
            offsets[i++] = new Vector2(x, y);
        }

        return offsets;
    }

    // Non-finite deform components are set to 0 and then, for mesh targets, rebuilt from their neighbours.
    private void RecoverDeforms(DeformBinding deform, string context)
    {
        var mesh = _nodesByUuid.TryGetValue(deform.TargetUuid, out var node) && node is Drawable drawable ? drawable.Mesh : null;
        var total = 0;
        foreach (var row in deform.OffsetArrays)
        {
            foreach (var offsets in row)
            {
                byte[]? missing = null;
                var expected = 0;
                for (var v = 0; v < offsets.Length; v++)
                {
                    var flags = (byte)((float.IsFinite(offsets[v].X) ? 0 : 1) | (float.IsFinite(offsets[v].Y) ? 0 : 2));
                    if (flags == 0)
                        continue;
                    missing ??= new byte[offsets.Length];
                    missing[v] = flags;
                    expected += (flags & 1) + (flags >> 1);
                    offsets[v] = new Vector2(float.IsFinite(offsets[v].X) ? offsets[v].X : 0f, float.IsFinite(offsets[v].Y) ? offsets[v].Y : 0f);
                }

                if (missing is null)
                    continue;
                total += expected;
                if (mesh is null)
                    continue;

                var recovered = DeformRecovery.Recover(mesh.Vertices, offsets, missing);
                if (recovered != expected)
                    throw new PuppetFormatException($"{context}: cannot rebuild {expected - recovered} non-finite deform components.");
            }
        }

        if (total > 0)
            _ctx.Report(LoadIssueKind.Recovered, context, $"{total} non-finite deform components {(mesh is null ? "set to 0" : "rebuilt from neighbouring vertices")}");

        if (mesh is not null)
        {
            foreach (var row in deform.OffsetArrays)
            {
                foreach (var offsets in row)
                {
                    if (offsets.Length != mesh.Vertices.Length)
                    {
                        _ctx.Report(LoadIssueKind.InvalidValue, context, $"a deform keyform has {offsets.Length} offsets for a mesh of {mesh.Vertices.Length} vertices");
                        return;
                    }
                }
            }
        }
    }

    // ---- Groups, animations, automation ---------------------------------------------------------------------------

    private List<ParameterGroup> ReadGroups(JsonElement json)
    {
        var groups = new List<ParameterGroup>();
        if (!ReadContext.TryGet(json, "groups", out var array) || array.ValueKind != JsonValueKind.Array)
            return groups;

        foreach (var g in array.EnumerateArray())
        {
            if (g.ValueKind != JsonValueKind.Object)
                continue;
            _ctx.CheckKeys(g, GroupKeys);
            if (_ctx.Id(g, "groupUUID", "ParameterGroup") is { } uuid)
                groups.Add(new ParameterGroup(uuid, _ctx.Name(g, "name", "ParameterGroup"), _ctx.Vec3(g, "color", Vector3.One, "ParameterGroup")));
        }

        return groups;
    }

    private Dictionary<string, Animation> ReadAnimations(JsonElement json, Dictionary<uint, Parameter> parameters)
    {
        var animations = new Dictionary<string, Animation>(StringComparer.Ordinal);
        if (!ReadContext.TryGet(json, "animations", out var map))
            return animations;
        if (map.ValueKind != JsonValueKind.Object)
        {
            _ctx.Report(LoadIssueKind.InvalidValue, "animations", $"expected an object of named animations, found {ReadContext.Describe(map)}; none read");
            return animations;
        }

        foreach (var property in map.EnumerateObject())
        {
            var a = property.Value;
            if (a.ValueKind != JsonValueKind.Object)
                continue;
            _ctx.CheckKeys(a, AnimationKeys);
            var loc = $"Animation '{property.Name}'";
            var animation = new Animation
            {
                Name = property.Name,
                Timestep = _ctx.Float(a, "timestep", 0.0166f, loc),
                Additive = _ctx.Bool(a, "additive", false, loc),
                Weight = ReadContext.TryGet(a, "animationWeight", out var weight) ? _ctx.Float(weight, 1f, loc) : null,
                Length = _ctx.Int(a, "length", 0, loc),
                LeadIn = _ctx.Int(a, "leadIn", -1, loc),
                LeadOut = _ctx.Int(a, "leadOut", -1, loc),
                Lanes = ReadLanes(a, loc, parameters),
            };
            animations[property.Name] = animation;
        }

        return animations;
    }

    private List<AnimationLane> ReadLanes(JsonElement json, string loc, Dictionary<uint, Parameter> parameters)
    {
        var lanes = new List<AnimationLane>();
        if (!ReadContext.TryGet(json, "lanes", out var array) || array.ValueKind != JsonValueKind.Array)
            return lanes;

        foreach (var l in array.EnumerateArray())
        {
            if (l.ValueKind != JsonValueKind.Object)
                continue;
            _ctx.CheckKeys(l, LaneKeys);
            var uuid = _ctx.Id(l, "uuid", "AnimationLane") ?? uint.MaxValue;
            if (!parameters.ContainsKey(uuid))
                _ctx.Report(LoadIssueKind.DanglingReference, loc, $"a lane targets parameter {uuid}, which is not in the puppet; kept unresolved");

            var keyframes = new List<Keyframe>();
            if (ReadContext.TryGet(l, "keyframes", out var frames) && frames.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in frames.EnumerateArray())
                {
                    if (f.ValueKind != JsonValueKind.Object)
                        continue;
                    _ctx.CheckKeys(f, KeyframeKeys);
                    keyframes.Add(new Keyframe(
                        _ctx.Int(f, "frame", 0, "Keyframe"),
                        _ctx.Float(f, "value", 0f, "Keyframe"),
                        _ctx.Float(f, "tension", 0.5f, "Keyframe")));
                }
            }

            // Lanes are sampled as if sorted (the editor sorts them, stable, on every edit); sort here so any file plays.
            var sorted = keyframes.ToArray();
            if (!keyframes.SequenceEqual(sorted.OrderBy(f => f.Frame)))
            {
                sorted = [.. keyframes.OrderBy(f => f.Frame)];
                _ctx.Report(LoadIssueKind.Recovered, loc, $"lane keyframes for parameter {uuid} were out of order; sorted by frame");
            }

            lanes.Add(new AnimationLane
            {
                ParameterUuid = uuid,
                Parameter = parameters.GetValueOrDefault(uuid),
                TargetAxis = _ctx.Int(l, "target", 0, "AnimationLane"),
                Interpolation = _ctx.Enum(l, "interpolation", InterpolateMode.Nearest, "AnimationLane"),
                MergeMode = _ctx.Enum(l, "merge_mode", ParameterMergeMode.Forced, "AnimationLane"),
                Frames = sorted,
            });
        }

        return lanes;
    }

    private void ReadAutomation(JsonElement json)
    {
        if (!ReadContext.TryGet(json, "automation", out var array) || array.ValueKind != JsonValueKind.Array)
            return;

        foreach (var a in array.EnumerateArray())
        {
            var type = ReadContext.TryGet(a, "type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : "unknown";
            _ctx.Report(LoadIssueKind.UnsupportedFeature, "automation", $"'{type}' automator not supported; ignored");
        }
    }

    // ---- References -----------------------------------------------------------------------------------------------

    private void CheckReferences(Dictionary<uint, Parameter> byUuid)
    {
        foreach (var node in _nodes)
        {
            var masks = node switch
            {
                Part part => part.Masks,
                Composite composite => composite.Masks,
                _ => [],
            };
            foreach (var mask in masks)
            {
                if (!_nodesByUuid.TryGetValue(mask.SourceUuid, out var source))
                    _ctx.Report(LoadIssueKind.DanglingReference, Describe(node), $"mask source {mask.SourceUuid} is not in the puppet (or was skipped)");
                else if (source is not Drawable)
                    _ctx.Report(LoadIssueKind.InvalidValue, Describe(node), $"mask source {Describe(source)} has no mesh");
            }

            if (node is SimplePhysics { ParameterUuid: { } id } physics)
            {
                if (byUuid.TryGetValue(id, out var driven))
                    driven.PhysicsDriver = physics; // the last driver in the tree wins
                else
                    _ctx.Report(LoadIssueKind.DanglingReference, "SimplePhysics", "drives a parameter that is not in the puppet; it has nothing to drive");
            }
        }
    }

    private static string Describe(Node node) => $"{node.Kind} '{node.Name}' ({node.Uuid})";

    private static string Describe(Parameter parameter) => $"Parameter '{parameter.Name}' ({parameter.Uuid})";
}
