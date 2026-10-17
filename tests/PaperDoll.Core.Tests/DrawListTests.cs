using System.Globalization;
using System.Numerics;
using PaperDoll.Evaluation;

namespace PaperDoll.Tests;

/// <summary>Transforms, visibility, draw order and the draw list.</summary>
public class DrawListTests
{
    private const string Quad = """{ "verts": [0, 0, 10, 0, 10, 10, 0, 10], "uvs": [0, 0, 1, 0, 1, 1, 0, 1], "indices": [0, 1, 2, 0, 2, 3] }""";

    private static string F(float value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Transform(float tx = 0, float ty = 0, float rz = 0, float sx = 1, float sy = 1) =>
        $$"""{ "trans": [{{F(tx)}}, {{F(ty)}}, 0], "rot": [0, 0, {{F(rz)}}], "scale": [{{F(sx)}}, {{F(sy)}}] }""";

    private static string Part(uint uuid, float z = 0, string children = "", string extra = "", string? mesh = null, string textures = "[0]") => $$"""
        { "uuid": {{uuid}}, "name": "P{{uuid}}", "type": "Part", "zsort": {{F(z)}}, "mesh": {{mesh ?? Quad}},
          "textures": {{textures}}{{extra}}, "children": [ {{children}} ] }
        """;

    private static string Group(uint uuid, string type, float z = 0, string children = "", string extra = "") => $$"""
        { "uuid": {{uuid}}, "name": "{{type}}{{uuid}}", "type": "{{type}}", "zsort": {{F(z)}}{{extra}}, "children": [ {{children}} ] }
        """;

    /// <summary>Four 2x2 texture slots, so Parts can refer to slots 0 to 3.</summary>
    private static readonly (byte, byte[])[] Textures = [.. Enumerable.Range(0, 4).Select(_ => ((byte)0, TestData.Png(2, 2, TestData.Pixels2x2)))];

    private static PuppetInstance Instance(string children) => TestData.Load(TestData.Puppet(children), Textures).CreateInstance();

    private static uint[] Order(PuppetInstance instance) => instance.DrawList.Where(i => i.Kind == DrawItemKind.Part).Select(i => i.Node.Uuid).ToArray();

    private static DrawItem Item(PuppetInstance instance, uint uuid) => instance.DrawList.Single(i => i.Kind == DrawItemKind.Part && i.Node.Uuid == uuid);

    [Fact]
    public void LargerZSortIsDrawnFirst()
    {
        var instance = Instance($"{Part(2, 0)}, {Part(3, 1)}, {Part(4, -1)}");

        Assert.Equal([3u, 2, 4], Order(instance));
    }

    [Fact]
    public void ZSortAddsUpAlongThePath()
    {
        var instance = Instance($"""{Group(2, "Node", 5, Part(3, -1))}, {Part(4, 4.5f)}""");

        Assert.Equal(4f, Item(instance, 3).ZSort);
        Assert.Equal([4u, 3], Order(instance));
    }

    [Fact]
    public void TiesKeepTreeOrderWithParentsBeforeChildren()
    {
        var instance = Instance($"""{Group(2, "Node", 0, $"{Part(3)}, {Part(4, 0, Part(5))}")}, {Part(6)}""");

        Assert.Equal([3u, 4, 5, 6], Order(instance));
    }

    [Fact]
    public void CompositeChildrenAreSortedAsOneUnit()
    {
        var composite = Group(3, "Composite", 0, $"{Part(4, 10)}, {Part(5, -10)}");
        var instance = Instance($"{Part(2, 0.5f)}, {composite}, {Part(6, -0.5f)}");

        // P4's effective z-sort (10) is larger than P2's, but it stays inside the composite, which sorts at 0.
        Assert.Equal([2u, 4, 5, 6], Order(instance));
        Assert.Equal(3u, Item(instance, 4).Layer?.Uuid);
        Assert.Null(Item(instance, 2).Layer);
    }

    [Fact]
    public void CompositeIsQueuedAfterPartsWithTheSameZSort()
    {
        var instance = Instance($"""{Group(2, "Composite", 0, Part(3))}, {Part(4, 0)}""");

        Assert.Equal([4u, 3], Order(instance));
    }

    [Fact]
    public void NestedCompositesAreGroupedInsideTheirParent()
    {
        var inner = Group(4, "Composite", -1, $"{Part(5, 3)}, {Part(6, -3)}");
        var outer = Group(2, "Composite", 0, $"{Part(3, 0)}, {inner}, {Part(7, -0.5f)}");
        var instance = Instance(outer);

        Assert.Equal([3u, 7, 5, 6], Order(instance));
        Assert.Equal(4u, Item(instance, 5).Layer?.Uuid);
        Assert.Equal(2u, Item(instance, 7).Layer?.Uuid);

        // Each Composite's items are bracketed by its begin and end items.
        Assert.Equal(["begin 2", "P3", "P7", "begin 4", "P5", "P6", "end 4", "end 2"], instance.DrawList.Select(Describe));
        var innerEnd = instance.DrawList.Single(i => i.Kind == DrawItemKind.EndComposite && i.Node.Uuid == 4);
        Assert.Equal(2u, innerEnd.Layer?.Uuid);
        Assert.Equal(-1f, innerEnd.ZSort);
        Assert.True(innerEnd.Positions.IsEmpty && innerEnd.Indices.IsEmpty && innerEnd.Texture is null);
    }

    private static string Describe(DrawItem item) => item.Kind switch
    {
        DrawItemKind.BeginComposite => $"begin {item.Node.Uuid}",
        DrawItemKind.EndComposite => $"end {item.Node.Uuid}",
        _ => item.Node.Name,
    };

    [Fact]
    public void HiddenCompositesHaveNoItems()
    {
        var instance = Instance(Group(2, "Composite", 0, Part(3), extra: ", \"enabled\": false"));

        Assert.Empty(instance.DrawList);
    }

    [Fact]
    public void DisabledNodesHideTheirSubtree()
    {
        var instance = Instance($$"""
            {{Group(2, "Node", 0, Part(3), ", \"enabled\": false")}},
            {{Part(4, 0, Part(5), ", \"enabled\": false")}},
            {{Part(6)}}
            """);

        Assert.Equal([6u], Order(instance));
    }

    [Fact]
    public void OnlyPartsAreDrawn()
    {
        var mesh = $$""", "mesh": {{Quad}}""";
        var instance = Instance($"""
            {Group(2, "MeshGroup", 0, Part(3), mesh)},
            {Group(4, "Mask", 0, Part(5), mesh)},
            {Group(6, "SimplePhysics")}
            """);

        // The children of a MeshGroup and a Mask draw; Masks and MeshGroups themselves are not drawn.
        Assert.Equal([3u, 5], Order(instance));
    }

    [Fact]
    public void TransformScalesThenRotatesThenTranslates()
    {
        var instance = Instance(Group(2, "Node", 0, Part(3), $", \"transform\": {Transform(100, 50, MathF.PI / 2, 2, 2)}"));

        // Vertex (10, 0): scaled to (20, 0), rotated a quarter turn to (0, 20), moved to (100, 70).
        AssertClose(new Vector2(100, 70), Item(instance, 3).Positions[1]);
    }

    [Fact]
    public void ChildTransformsAreInTheParentsSpace()
    {
        var child = Part(3, 0, extra: $", \"transform\": {Transform(0, 10)}");
        var instance = Instance(Group(2, "Node", 0, child, $", \"transform\": {Transform(100, 0, MathF.PI / 2)}"));

        // The child's offset (0, 10) is turned by the parent to (-10, 0).
        AssertClose(new Vector2(90, 0), Item(instance, 3).Positions[0]);
        AssertClose(new Vector2(90, 10), Item(instance, 3).Positions[1]);
    }

    [Fact]
    public void LockToRootIgnoresTheParent()
    {
        var child = Part(3, 0, extra: $", \"transform\": {Transform(1, 2)}, \"lockToRoot\": true");
        var instance = Instance(Group(2, "Node", 0, child, $", \"transform\": {Transform(100, 0, 1, 3, 3)}"));

        AssertClose(new Vector2(1, 2), Item(instance, 3).Positions[0]);
    }

    [Fact]
    public void MeshOriginIsSubtracted()
    {
        var mesh = """{ "verts": [10, 0, 0, 10, 0, 0], "uvs": [0, 0, 1, 0, 0, 1], "indices": [0, 1, 2], "origin": [5, 5] }""";
        var instance = Instance(Part(2, mesh: mesh));

        AssertClose(new Vector2(5, -5), Item(instance, 2).Positions[0]);
    }

    [Fact]
    public void LocalMatrixMatchesTheReferenceRotationOrder()
    {
        var t = new NodeTransform(new Vector3(3, -4, 5), new Vector3(0.3f, -0.7f, 1.1f), new Vector2(2, 0.5f));
        var m = PuppetEvaluator.LocalMatrix(t);

        // nicxlive writes T * Rz * Ry * Rx * S out element by element, for column vectors.
        float cx = MathF.Cos(0.3f), sx = MathF.Sin(0.3f), cy = MathF.Cos(-0.7f), sy = MathF.Sin(-0.7f), cz = MathF.Cos(1.1f), sz = MathF.Sin(1.1f);
        float[,] r =
        {
            { cy * cz, cz * sx * sy - cx * sz, cx * cz * sy + sx * sz },
            { cy * sz, cx * cz + sx * sy * sz, -cz * sx + cx * sy * sz },
            { -sy, cy * sx, cx * cy },
        };
        var p = new Vector3(1.5f, -2.5f, 0);
        var scaled = new Vector3(p.X * 2, p.Y * 0.5f, 0);
        var expected = new Vector3(
            r[0, 0] * scaled.X + r[0, 1] * scaled.Y + r[0, 2] * scaled.Z + 3,
            r[1, 0] * scaled.X + r[1, 1] * scaled.Y + r[1, 2] * scaled.Z - 4,
            r[2, 0] * scaled.X + r[2, 1] * scaled.Y + r[2, 2] * scaled.Z + 5);

        var actual = Vector3.Transform(p, m);
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }

    [Fact]
    public void AlbedoIsTheFirstNonEmptyTextureSlot()
    {
        var instance = Instance($"{Part(2, textures: "[4294967295, 3, 4294967295]")}, {Part(4, textures: "[]")}");

        Assert.Equal(3, Item(instance, 2).Texture);
        Assert.Null(Item(instance, 4).Texture);
    }

    [Fact]
    public void ColoursAreCopiedAndClamped()
    {
        var item = Item(Instance(Part(2, extra: """, "opacity": 1.5, "tint": [0.5, 2, -1], "screenTint": [0.25, 0, 0]""")), 2);

        Assert.Equal(1f, item.Opacity);
        Assert.Equal(new Vector3(0.5f, 1, 0), item.Tint);
        Assert.Equal(new Vector3(0.25f, 0, 0), item.ScreenTint);
    }

    [Fact]
    public void BoundsCoverTexturedItemsOnly()
    {
        var instance = Instance($"""
            {Part(2, extra: $", \"transform\": {Transform(-5, 20)}")},
            {Part(3, textures: "[]", extra: $", \"transform\": {Transform(500, 500)}")}
            """);

        Assert.Equal(new PuppetBounds(new Vector2(-5, 20), new Vector2(5, 30)), instance.Bounds);
        Assert.True(Instance("").Bounds.IsEmpty);
    }

    [Fact]
    public void MasksAreListedOncePerSourceAndModeWithoutMissingSources()
    {
        var masks = """
            , "masks": [{ "source": 2, "mode": "Mask" }, { "source": 99, "mode": "Mask" }, { "source": 3, "mode": "DodgeMask" },
                        { "source": 2, "mode": "Mask" }, { "source": 2, "mode": "DodgeMask" }, { "source": 5, "mode": "Mask" }]
            """;
        var instance = Instance($"{Part(2)}, {Part(3)}, {Part(4, extra: masks)}, {Group(5, "Node")}");

        var item = Item(instance, 4);
        Assert.Equal([(2u, MaskingMode.Mask), (3u, MaskingMode.DodgeMask), (2u, MaskingMode.DodgeMask)],
            item.Masks.Select(m => (m.Source.Node.Uuid, m.Mode)));
        Assert.Same(item.Masks[0].Source, item.Masks[2].Source);
        Assert.Equal([2u, 3], instance.MaskSources.Select(s => s.Node.Uuid).Order().ToArray());
        Assert.Equal(BlendMode.Normal, item.BlendMode);
        Assert.Empty(Item(instance, 2).Masks);
    }

    [Fact]
    public void MaskSourcesHaveTheirShapeWhetherOrNotTheyAreDrawn()
    {
        // The source is disabled and moved; it still masks, with its own threshold.
        var source = Part(2, extra: $", \"enabled\": false, \"mask_threshold\": 0.25, \"transform\": {Transform(100, 50)}");
        var instance = Instance($$"""{{source}}, {{Part(3, extra: ", \"masks\": [{ \"source\": 2, \"mode\": \"Mask\" }]")}}""");

        Assert.Equal([3u], Order(instance));
        var mask = Assert.Single(Item(instance, 3).Masks).Source;
        Assert.False(mask.IsShape);
        Assert.Equal(0, mask.Texture);
        Assert.Equal(0.25f, mask.Threshold);
        Assert.Equal([new(100, 50), new(110, 50), new(110, 60), new(100, 60)], mask.Positions.ToArray());
        Assert.Equal(4, mask.Uvs.Length);
    }

    [Fact]
    public void MaskNodesMaskWithTheirWholeMesh()
    {
        var maskNode = $$"""{ "uuid": 2, "name": "M", "type": "Mask", "mesh": {{Quad}}, "transform": {{Transform(0, 5)}} }""";
        var instance = Instance($$"""{{maskNode}}, {{Part(3, extra: ", \"masks\": [{ \"source\": 2, \"mode\": \"DodgeMask\" }]")}}""");

        var mask = Assert.Single(Item(instance, 3).Masks);
        Assert.Equal(MaskingMode.DodgeMask, mask.Mode);
        Assert.True(mask.Source.IsShape);
        Assert.Null(mask.Source.Texture);
        Assert.True(mask.Source.Uvs.IsEmpty);
        Assert.Equal(new Vector2(10, 15), mask.Source.Positions[2]);
    }

    [Fact]
    public void InstancesHaveTheirOwnOutput()
    {
        var puppet = TestData.Load(TestData.Puppet(Part(2)), Textures);
        var a = puppet.CreateInstance();
        var b = puppet.CreateInstance();

        Assert.NotSame(a.DrawList[0], b.DrawList[0]);
        Assert.Equal(a.DrawList[0].Positions.ToArray(), b.DrawList[0].Positions.ToArray());
    }

    [Theory]
    [MemberData(nameof(SampleTests.Samples), MemberType = typeof(SampleTests))]
    public void EverySampleDrawsEveryPartAtRest(string file, int parts, int composites, int meshGroups, int physics, int groups, int parameters, int textures, int animations)
    {
        _ = (meshGroups, physics, groups, parameters, textures, animations);
        var puppet = Puppet.Load(SampleTests.SamplePath(file));
        var instance = puppet.CreateInstance();
        instance.Update(1f / 60);

        // No sample disables a node, so every Part is drawn, once, and every Composite is a layer.
        var partItems = instance.DrawList.Where(i => i.Kind == DrawItemKind.Part).ToList();
        Assert.Equal(parts, partItems.Select(i => i.Part).Distinct().Count());
        Assert.Equal(parts, partItems.Count);
        Assert.Equal(composites, instance.DrawList.Count(i => i.Kind == DrawItemKind.BeginComposite));
        Assert.Equal(composites, instance.DrawList.Count(i => i.Kind == DrawItemKind.EndComposite));
        Assert.Equal(parts + 2 * composites, instance.DrawList.Count);
        Assert.False(instance.Bounds.IsEmpty);
        foreach (var source in instance.MaskSources)
        {
            foreach (var p in source.Positions)
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"{source} has a non-finite vertex");
        }

        foreach (var item in partItems)
        {
            Assert.True(item.Texture is { } t && puppet.Textures[t].CanDecode, $"{item} has texture {item.Texture}");
            foreach (var p in item.Positions)
                Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y), $"{item} has a non-finite vertex");
        }
    }

    private static void AssertClose(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
    }
}
