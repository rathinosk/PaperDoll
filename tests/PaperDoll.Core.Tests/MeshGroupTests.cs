using System.Numerics;
using PaperDoll.Evaluation;

namespace PaperDoll.Tests;

/// <summary>MeshGroups deforming the nodes below them.</summary>
public class MeshGroupTests
{
    // A 100 x 100 square of two triangles. At P = 1 its deform binding doubles x across the square: x -> 2x.
    private const string Square = """{ "verts": [0, 0, 100, 0, 100, 100, 0, 100], "uvs": [], "indices": [0, 1, 2, 0, 2, 3] }""";
    private const string Doubling = "[[[[0, 0], [0, 0], [0, 0], [0, 0]]], [[[0, 0], [0, 0], [0, 0], [0, 0]]], [[[0, 0], [100, 0], [100, 0], [0, 0]]]]";
    private const string Quad = """{ "verts": [0, 0, 10, 0, 10, 10, 0, 10], "uvs": [0, 0, 1, 0, 1, 1, 0, 1], "indices": [0, 1, 2, 0, 2, 3] }""";
    private static readonly Vector2 GroupAt = new(50, 0);

    private static readonly (byte, byte[])[] Texture = [((byte)0, TestData.Png(2, 2, TestData.Pixels2x2))];

    private static string Part(uint uuid, float x, float y, string children = "") => $$"""
        { "uuid": {{uuid}}, "name": "P{{uuid}}", "type": "Part", "mesh": {{Quad}}, "textures": [0],
          "transform": { "trans": [{{x}}, {{y}}, 0] }, "children": [ {{children}} ] }
        """;

    private static string Group(string children, bool dynamic = false, bool translate = false, uint uuid = 10) => $$"""
        { "uuid": {{uuid}}, "name": "MG", "type": "MeshGroup", "mesh": {{Square}}, "transform": { "trans": [50, 0, 0] },
          "dynamic_deformation": {{(dynamic ? "true" : "false")}}, "translate_children": {{(translate ? "true" : "false")}},
          "children": [ {{children}} ] }
        """;

    /// <summary>A 1D parameter from -1 to 1 with keyforms at -1, 0 and 1.</summary>
    private static string Param(string bindings) => $$"""
        { "uuid": 50, "name": "P", "is_vec2": false, "min": [-1, 0], "max": [1, 0], "defaults": [0, 0],
          "axis_points": [[0, 0.5, 1], [0]], "bindings": [ {{bindings}} ] }
        """;

    private static string Deform(uint node, string values) =>
        $$"""{ "node": {{node}}, "param_name": "deform", "values": {{values}}, "isSet": [[true], [true], [true]] }""";

    private static PuppetInstance Instance(string children, string bindings)
    {
        var instance = TestData.Load(TestData.Puppet(children, Param(bindings)), Texture).CreateInstance();
        instance.SetParameterValue(instance.Puppet.Parameters[0], new Vector2(1, 0));
        instance.Update(0);
        return instance;
    }

    private static Vector2[] Positions(PuppetInstance instance, uint uuid) => instance.DrawList.Single(i => i.Kind == DrawItemKind.Part && i.Node.Uuid == uuid).Positions.ToArray();

    private static void Near(Vector2 expected, Vector2 actual) =>
        Assert.True(Vector2.Distance(expected, actual) < 1e-3f, $"expected {expected}, got {actual}");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChildVerticesFollowTheDeformedMesh(bool dynamic)
    {
        var instance = Instance(Group(Part(2, 20, 30), dynamic), Deform(10, Doubling));

        var p = Positions(instance, 2);
        Near(GroupAt + new Vector2(40, 30), p[0]);
        Near(GroupAt + new Vector2(60, 30), p[1]);
        Near(GroupAt + new Vector2(60, 40), p[2]);
    }

    [Fact]
    public void AtRestNothingMoves()
    {
        var instance = Instance(Group(Part(2, 20, 30)), Deform(10, Doubling));
        instance.SetParameterValue(instance.Puppet.Parameters[0], Vector2.Zero);
        instance.Update(0);

        Near(GroupAt + new Vector2(30, 40), Positions(instance, 2)[2]);
    }

    [Theory]
    [InlineData(false, 45f)] // the rest vertex is mapped (20 -> 40), then the Part's own +5 is added
    [InlineData(true, 50f)]  // the deformed vertex (25) is mapped -> 50
    public void DynamicMeshGroupsMapTheDeformedVertices(bool dynamic, float x)
    {
        var own = Deform(2, "[[[[0, 0], [0, 0], [0, 0], [0, 0]]], [[[0, 0], [0, 0], [0, 0], [0, 0]]], [[[5, 0], [5, 0], [5, 0], [5, 0]]]]");
        var instance = Instance(Group(Part(2, 20, 30), dynamic), Deform(10, Doubling) + "," + own);

        Near(GroupAt + new Vector2(x, 30), Positions(instance, 2)[0]);
    }

    [Fact]
    public void VerticesOutsideTheMeshStayPut()
    {
        var instance = Instance(Group(Part(2, 200, 30)), Deform(10, Doubling));

        Near(GroupAt + new Vector2(200, 30), Positions(instance, 2)[0]);
    }

    [Fact]
    public void MeshGroupTransformAndPartTransformAreTakenIntoAccount()
    {
        // The Part is scaled by 2 inside the MeshGroup: its vertex (10, 10) sits at (40, 50) in the MeshGroup's space.
        var part = $$"""
            { "uuid": 2, "name": "P2", "type": "Part", "mesh": {{Quad}}, "textures": [0],
              "transform": { "trans": [20, 30, 0], "scale": [2, 2] } }
            """;
        var instance = Instance(Group(part), Deform(10, Doubling));

        Near(GroupAt + new Vector2(80, 50), Positions(instance, 2)[2]);
    }

    [Theory]
    [InlineData(false, 20f)]
    [InlineData(true, 40f)]
    public void TranslateChildrenMovesNodesByTheirOrigin(bool translate, float x)
    {
        var node = """{ "uuid": 3, "name": "N", "type": "Node", "transform": { "trans": [20, 30, 0] }, "children": [ """ + Part(2, 0, 0) + " ] }";
        var instance = Instance(Group(node, translate: translate), Deform(10, Doubling));

        // A plain node does not pass the MeshGroup on, so its Part moves with it but is not deformed.
        var p = Positions(instance, 2);
        Near(GroupAt + new Vector2(x, 30), p[0]);
        Near(GroupAt + new Vector2(x + 10, 30), p[1]);
    }

    [Theory]
    [InlineData(true, 40f)]
    [InlineData(false, 20f)]
    public void CompositesPassTheMeshGroupOnOnlyWithPropagate(bool propagate, float x)
    {
        var composite = $$"""
            { "uuid": 4, "name": "C", "type": "Composite", "propagate_meshgroup": {{(propagate ? "true" : "false")}},
              "children": [ {{Part(2, 20, 30)}} ] }
            """;
        var instance = Instance(Group(composite), Deform(10, Doubling));

        Near(GroupAt + new Vector2(x, 30), Positions(instance, 2)[0]);
    }

    [Fact]
    public void PartsPassTheMeshGroupOnToTheirChildren()
    {
        var instance = Instance(Group(Part(2, 0, 0, Part(3, 20, 30))), Deform(10, Doubling));

        Near(GroupAt + new Vector2(40, 30), Positions(instance, 3)[0]);
    }

    [Fact]
    public void NestedMeshGroupsActOnTheirOwnChildren()
    {
        // The inner MeshGroup (at 50 inside the outer one, so 100 in the root) has no deformation of its own, so its child
        // stays where it is even though the outer MeshGroup doubles x: the outer one deforms only the inner one's mesh.
        var inner = Group(Part(2, 20, 30), dynamic: true, uuid: 11);
        var instance = Instance(Group(inner, dynamic: true), Deform(10, Doubling));

        Near(GroupAt + GroupAt + new Vector2(20, 30), Positions(instance, 2)[0]);
    }

    [Fact]
    public void ANonDynamicMeshGroupDeformsANestedOneBeforeItMapsItsChildren()
    {
        // The inner MeshGroup's square ends at 90, inside the outer one's lookup (whose far edge at 100 is excluded).
        var inner = """
            { "uuid": 11, "name": "Inner", "type": "MeshGroup",
              "mesh": { "verts": [0, 0, 90, 0, 90, 90, 0, 90], "uvs": [], "indices": [0, 1, 2, 0, 2, 3] },
              "children": [
            """ + Part(2, 20, 30) + " ] }";
        var instance = Instance(Group(inner), Deform(10, Doubling));

        // The outer map doubles the inner mesh's x, so the inner one doubles its child's.
        Near(GroupAt + new Vector2(40, 30), Positions(instance, 2)[0]);
    }

    [Fact]
    public void ShapeLookupUsesTheLastTriangleOnSharedCellsAndExcludesTheFarEdges()
    {
        var mesh = TestData.Load(TestData.Puppet(Group(""))).Nodes.OfType<MeshGroup>().Single().Mesh;
        var shape = MeshGroupShape.Create(mesh)!;

        Assert.Equal(2, shape.TriangleCount);
        Assert.Equal(1, shape.Lookup(new Vector2(0.5f, 0.5f)));  // on the shared diagonal: the later triangle
        Assert.Equal(0, shape.Lookup(new Vector2(90.5f, 10.5f)));
        Assert.Equal(1, shape.Lookup(new Vector2(10.5f, 90.5f)));
        Assert.Equal(-1, shape.Lookup(new Vector2(100f, 50f)));
        Assert.Equal(-1, shape.Lookup(new Vector2(-0.1f, 50f)));
    }

    [Fact]
    public void EvaluatingWithMeshGroupsAllocatesNothing()
    {
        var instance = Instance(Group(Part(2, 20, 30), dynamic: true), Deform(10, Doubling));
        var parameter = instance.Puppet.Parameters[0];
        instance.SetParameterValue(parameter, new Vector2(0.3f, 0));
        instance.Update(0);

        var before = GC.GetAllocatedBytesForCurrentThread();
        instance.SetParameterValue(parameter, new Vector2(0.6f, 0));
        instance.Update(0);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
