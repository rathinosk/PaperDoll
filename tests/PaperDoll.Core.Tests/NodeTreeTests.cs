using System.Numerics;

namespace PaperDoll.Tests;

public class NodeTreeTests
{
    private const string AllTypes = """
        { "uuid": 10, "name": "Group", "type": "Node", "zsort": -29, "lockToRoot": true,
          "transform": { "trans": [1, 2, 3], "rot": [0, 0, 0.5], "scale": [2, 3] },
          "children": [
            { "uuid": 11, "name": "Body\u0000\u0000", "type": "Part", "enabled": false, "zsort": -0.03,
              "mesh": { "verts": [0, 0, 1, 0, 0, 1], "uvs": [0, 0, 1, 0, 0, 1], "indices": [0, 1, 2], "origin": [0.5, 0.5] },
              "textures": [0, 4294967295, 4294967295], "opacity": 1, "mask_threshold": 0.25,
              "tint": [1, 0.5, 0.25], "screenTint": [0.1, 0.2, 0.3], "emissionStrength": 2,
              "blend_mode": "Multiply", "masks": [{ "source": 13, "mode": "DodgeMask" }] },
            { "uuid": 12, "name": "Layer", "type": "Composite", "opacity": 0.5, "blend_mode": "ColorBurn",
              "propagate_meshgroup": true, "children": [] },
            { "uuid": 13, "name": "Clip", "type": "Mask",
              "mesh": { "verts": [0, 0, 1, 0, 1, 1], "indices": [0, 1, 2] } },
            { "uuid": 14, "name": "Deformer", "type": "MeshGroup", "dynamic_deformation": true,
              "mesh": { "verts": [0, 0, 1, 0, 1, 1], "indices": [0, 1, 2], "grid_axes": [[0, 1], [0, 0.5, 1]] } },
            { "uuid": 15, "name": "Hair", "type": "SimplePhysics", "param": 100, "model_type": "spring_pendulum",
              "map_mode": "XY", "length": 50, "output_scale": [0.5, 2], "local_only": true }
          ] }
        """;

    private const string OneParameter = """
        { "uuid": 100, "name": "Swing", "is_vec2": true, "min": [-1, -1], "max": [1, 1], "defaults": [0, 0],
          "axis_points": [[0, 1], [0, 1]], "bindings": [] }
        """;

    [Fact]
    public void ReadsEveryNodeTypeWithItsFields()
    {
        var puppet = TestData.Load(TestData.Puppet(AllTypes, OneParameter), [(0, TestData.Png(2, 2, TestData.Pixels2x2))]);

        Assert.Equal(7, puppet.Nodes.Count);
        Assert.Empty(puppet.Issues);

        var group = Assert.Single(puppet.Root.Children);
        Assert.Same(puppet.Root, group.Parent);
        Assert.Equal(-29f, group.ZSort);
        Assert.True(group.LockToRoot);
        Assert.Equal(new NodeTransform(new Vector3(1, 2, 3), new Vector3(0, 0, 0.5f), new Vector2(2, 3)), group.Transform);
        Assert.Equal(5, group.Children.Count);

        var part = Assert.IsType<Part>(group.Children[0]);
        Assert.Equal("Body", part.Name);
        Assert.False(part.Enabled);
        Assert.Equal(-0.03f, part.ZSort);
        Assert.Equal([0, null, null], part.TextureSlots);
        Assert.Equal(3, part.Mesh.Vertices.Length);
        Assert.Equal(new Vector2(1, 0), part.Mesh.Vertices[1]);
        Assert.Equal(new Vector2(0, 1), part.Mesh.Uvs[2]);
        Assert.Equal([0, 1, 2], part.Mesh.Indices.ToArray());
        Assert.Equal(new Vector2(0.5f, 0.5f), part.Mesh.Origin);
        Assert.Equal(0.25f, part.MaskThreshold);
        Assert.Equal(new Vector3(1, 0.5f, 0.25f), part.Tint);
        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), part.ScreenTint);
        Assert.Equal(2f, part.EmissionStrength);
        Assert.Equal(BlendMode.Multiply, part.BlendMode);
        Assert.Equal([new MaskBinding(13, MaskingMode.DodgeMask)], part.Masks);

        var composite = Assert.IsType<Composite>(group.Children[1]);
        Assert.Equal(0.5f, composite.Opacity);
        Assert.Equal(BlendMode.ColorBurn, composite.BlendMode);
        Assert.True(composite.PropagateMeshGroup);

        Assert.IsType<Mask>(group.Children[2]);

        var meshGroup = Assert.IsType<MeshGroup>(group.Children[3]);
        Assert.True(meshGroup.DynamicDeformation);
        Assert.False(meshGroup.TranslateChildren);
        Assert.Equal([0f, 0.5f, 1f], meshGroup.Mesh.GridAxes[1]);

        var physics = Assert.IsType<SimplePhysics>(group.Children[4]);
        Assert.Equal(100u, physics.ParameterUuid);
        Assert.Equal(PhysicsModel.SpringPendulum, physics.Model);
        Assert.Equal(PhysicsMapMode.XY, physics.MapMode);
        Assert.Equal(50f, physics.Length);
        Assert.Equal(1f, physics.Frequency);
        Assert.Equal(new Vector2(0.5f, 2), physics.OutputScale);
        Assert.True(physics.LocalOnly);

        Assert.True(puppet.TryGetNode(15, out var found));
        Assert.Same(physics, found);
    }

    [Fact]
    public void DefaultsMatchNijilive()
    {
        var puppet = TestData.Load(TestData.Puppet("""
            { "uuid": 2, "name": "P", "type": "Part" },
            { "uuid": 3, "name": "C", "type": "Composite" },
            { "uuid": 4, "name": "S", "type": "SimplePhysics" }
            """));

        var part = Assert.IsType<Part>(puppet.Nodes[1]);
        Assert.True(part.Enabled);
        Assert.Equal(NodeTransform.Identity, part.Transform);
        Assert.Equal(1f, part.Opacity);
        Assert.Equal(Vector3.One, part.Tint);
        Assert.Equal(Vector3.Zero, part.ScreenTint);
        Assert.Equal(1f, part.EmissionStrength);
        Assert.Equal(0.5f, part.MaskThreshold);
        Assert.Equal(BlendMode.Normal, part.BlendMode);
        Assert.Equal(0, part.Mesh.Vertices.Length);

        Assert.False(Assert.IsType<Composite>(puppet.Nodes[2]).PropagateMeshGroup);

        var physics = Assert.IsType<SimplePhysics>(puppet.Nodes[3]);
        Assert.Null(physics.ParameterUuid);
        Assert.Equal(PhysicsModel.Pendulum, physics.Model);
        Assert.Equal(PhysicsMapMode.AngleLength, physics.MapMode);
        Assert.Equal((1f, 100f, 1f, 0.5f, 0.5f), (physics.Gravity, physics.Length, physics.Frequency, physics.AngleDamping, physics.LengthDamping));
    }

    [Fact]
    public void UnknownNodeTypesAreSkippedWithTheirSubtreeAndReported()
    {
        var puppet = TestData.Load(TestData.Puppet("""
            { "uuid": 2, "name": "Default Camera", "type": "Camera", "viewport": [2560, 5120],
              "children": [{ "uuid": 3, "name": "Inside", "type": "Part" }] },
            { "uuid": 4, "name": "Grid", "type": "GridDeformer" },
            { "uuid": 5, "name": "Kept", "type": "Part" }
            """));

        Assert.Equal(["Root", "Kept"], puppet.Nodes.Select(n => n.Name));
        Assert.False(puppet.TryGetNode(3, out _));

        var skipped = puppet.Issues.Where(i => i.Kind == LoadIssueKind.SkippedNode).ToList();
        Assert.Equal(2, skipped.Count);
        Assert.Contains("unknown node type", skipped[0].Message, StringComparison.Ordinal);
        Assert.Contains("1 descendant", skipped[0].Message, StringComparison.Ordinal);
        Assert.Contains("out of scope", skipped[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EditorKeysAreIgnoredAndListedSeparatelyFromUnknownKeys()
    {
        var puppet = TestData.Load(TestData.Puppet("""
            { "uuid": 2, "name": "A", "type": "Part", "poseID": null, "mqnFilename": "a.png", "hiddenBy": [], "fullyLinkedTo": [] },
            { "uuid": 3, "name": "B", "type": "Part", "poseID": null, "somethingNew": 1 }
            """));

        Assert.Contains(puppet.Issues, i => i is { Kind: LoadIssueKind.IgnoredKey, Location: "Part", Count: 2 } && i.Message.Contains("poseID", StringComparison.Ordinal));
        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.IgnoredKey && i.Message.Contains("mqnFilename", StringComparison.Ordinal));
        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.UnknownKey && i.Message.Contains("somethingNew", StringComparison.Ordinal));
    }

    [Fact]
    public void RootIsAPlainNodeNamedRoot()
    {
        var json = TestData.Puppet().Replace("\"type\": \"Node\"", "\"type\": \"Part\"", StringComparison.Ordinal);

        var puppet = TestData.Load(json);

        Assert.Equal(NodeKind.Node, puppet.Root.Kind);
        Assert.Equal("Root", puppet.Root.Name);
        Assert.Contains(puppet.Issues, i => i.Location == "Root");
    }

    [Fact]
    public void LegacyMaskFormIsRead()
    {
        var puppet = TestData.Load(TestData.Puppet("""
            { "uuid": 2, "name": "M", "type": "Part", "mesh": { "verts": [0, 0, 1, 0, 1, 1], "indices": [0, 1, 2] } },
            { "uuid": 3, "name": "P", "type": "Part", "masked_by": [2], "mask_mode": "DodgeMask" }
            """));

        Assert.Equal([new MaskBinding(2, MaskingMode.DodgeMask)], ((Part)puppet.Nodes[2]).Masks);
    }

    [Fact]
    public void MetaStringsLoseTheirNulPadding()
    {
        var json = TestData.Puppet().Replace("\"name\": \"Test\"", "\"name\": \"Test\", \"rigger\": \"seagetch\\u0000\\u0000\", \"thumbnailId\": 4294967295", StringComparison.Ordinal);

        var puppet = TestData.Load(json);

        Assert.Equal("seagetch", puppet.Meta.Rigger);
        Assert.Null(puppet.Meta.ThumbnailId);
    }

    [Fact]
    public void BadValuesFallBackToDefaultsAndAreReported()
    {
        var puppet = TestData.Load(TestData.Puppet("""
            { "uuid": 2, "name": "P", "type": "Part", "opacity": "half", "blend_mode": "Glitter", "textures": [5] }
            """));

        var part = (Part)puppet.Nodes[1];
        Assert.Equal(1f, part.Opacity);
        Assert.Equal(BlendMode.Normal, part.BlendMode);
        Assert.Equal([null], part.TextureSlots);
        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.InvalidValue && i.Location == "Part.opacity");
        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.InvalidValue && i.Message.Contains("Glitter", StringComparison.Ordinal));
        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.DanglingReference && i.Message.Contains("texture 5", StringComparison.Ordinal));
    }

    [Fact]
    public void ChildWithoutATypeIsAFormatError()
    {
        Assert.Throws<PuppetFormatException>(() => TestData.Load(TestData.Puppet("""{ "uuid": 2, "name": "P" }""")));
    }
}
