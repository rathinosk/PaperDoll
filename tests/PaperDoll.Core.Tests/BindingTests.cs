using System.Globalization;
using System.Numerics;

namespace PaperDoll.Tests;

/// <summary>Parameters driving node properties through their bindings, seen in an instance's draw list.</summary>
public class BindingTests
{
    private const string Quad = """{ "verts": [0, 0, 10, 0, 10, 10, 0, 10], "uvs": [0, 0, 1, 0, 1, 1, 0, 1], "indices": [0, 1, 2, 0, 2, 3] }""";

    private static string F(float value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Part(uint uuid, float z = 0, string extra = "") => $$"""
        { "uuid": {{uuid}}, "name": "P{{uuid}}", "type": "Part", "zsort": {{F(z)}}, "mesh": {{Quad}}, "textures": [0]{{extra}} }
        """;

    /// <summary>A 1D parameter from -1 to 1 with keyforms at -1, 0 and 1 and one binding per (node, property, values).</summary>
    private static string Param1D(string bindings, string mode = "Linear", string isSet = "[[true], [true], [true]]") => $$"""
        { "uuid": 50, "name": "P", "is_vec2": false, "min": [-1, 0], "max": [1, 0], "defaults": [0, 0],
          "axis_points": [[0, 0.5, 1], [0]], "bindings": [ {{bindings}} ] }
        """.Replace("$MODE", mode).Replace("$SET", isSet);

    private static string Binding(uint node, string property, string values) =>
        $$"""{ "node": {{node}}, "param_name": "{{property}}", "values": {{values}}, "isSet": $SET, "interpolate_mode": "$MODE" }""";

    private static readonly (byte, byte[])[] Texture = [((byte)0, TestData.Png(2, 2, TestData.Pixels2x2))];

    private static PuppetInstance Instance(string children, string parameters) =>
        TestData.Load(TestData.Puppet(children, parameters), Texture).CreateInstance();

    private static DrawItem Item(PuppetInstance instance, uint uuid) => instance.DrawList.Single(i => i.Kind == DrawItemKind.Part && i.Node.Uuid == uuid);

    private static void Set(PuppetInstance instance, float x, float y = 0)
    {
        instance.SetParameterValue(instance.Puppet.Parameters[0], new Vector2(x, y));
        instance.Update(0);
    }

    [Theory]
    [InlineData(-1f, -10f)]
    [InlineData(-0.5f, -5f)]
    [InlineData(0f, 0f)]
    [InlineData(0.5f, 10f)]
    [InlineData(1f, 20f)]
    [InlineData(3f, 20f)] // out of range: evaluated at the end of the range
    public void TranslationFollowsTheParameterLinearly(float value, float x)
    {
        var instance = Instance(Part(2), Param1D(Binding(2, "transform.t.x", "[[-10], [0], [20]]")));

        Set(instance, value);

        Assert.Equal(new Vector2(x, 0), Item(instance, 2).Positions[0]);
        Assert.Equal(new Vector2(value, 0), instance.GetParameterValue(instance.Puppet.Parameters[0])); // kept as set
    }

    [Fact]
    public void TranslationsAddAcrossParametersAndScalesMultiply()
    {
        var second = """
            , { "uuid": 51, "name": "Q", "min": [0, 0], "max": [1, 0], "defaults": [1, 0], "axis_points": [[0, 1], [0]],
                "bindings": [ { "node": 2, "param_name": "transform.t.x", "values": [[0], [5]], "isSet": [[true], [true]] },
                              { "node": 2, "param_name": "transform.s.x", "values": [[1], [3]], "isSet": [[true], [true]] } ] }
            """;
        var instance = Instance(Part(2), Param1D(Binding(2, "transform.t.x", "[[0], [0], [10]]") + "," + Binding(2, "transform.s.x", "[[1], [1], [2]]")) + second);

        Set(instance, 1);

        // translation 10 + 5, scale 2 * 3: vertex (10, 0) lands at 15 + 60
        Assert.Equal(new Vector2(75, 0), Item(instance, 2).Positions[1]);
    }

    [Fact]
    public void ParentTransformsCarryChildren()
    {
        var group = """{ "uuid": 3, "name": "G", "type": "Node", "children": [ """ + Part(2) + " ] }";
        var instance = Instance(group, Param1D(Binding(3, "transform.r.z", $"[[0], [0], [{F(MathF.PI / 2)}]]")));

        Set(instance, 1);

        var p = Item(instance, 2).Positions[1]; // (10, 0) rotated a quarter turn
        Assert.Equal(0f, p.X, 1e-4f);
        Assert.Equal(10f, p.Y, 1e-4f);
    }

    [Fact]
    public void ZSortOffsetsReorderTheDrawList()
    {
        var instance = Instance($"{Part(2, 1)}, {Part(3, 0)}", Param1D(Binding(3, "zSort", "[[0], [0], [2]]")));
        Assert.Equal([2u, 3], instance.DrawList.Select(i => i.Node.Uuid));

        Set(instance, 1);

        Assert.Equal([3u, 2], instance.DrawList.Select(i => i.Node.Uuid));
        Assert.Equal(2f, Item(instance, 3).ZSort);
    }

    [Fact]
    public void VisualPropertiesCombineAsNijiliveDoes()
    {
        var bindings = string.Join(",",
            Binding(2, "opacity", "[[1], [1], [0.5]]"),
            Binding(2, "tint.g", "[[1], [1], [0.25]]"),
            Binding(2, "screenTint.b", "[[0], [0], [0.5]]"),
            Binding(2, "emissionStrength", "[[0], [0], [-1]]"),
            Binding(2, "alphaThreshold", "[[0], [0], [0.9]]"));
        var instance = Instance(Part(2, extra: """, "opacity": 0.8, "tint": [1, 0.8, 1], "screenTint": [0, 0, 0.25], "emissionStrength": 2, "mask_threshold": 0.3"""),
            Param1D(bindings));

        Set(instance, 1);

        var item = Item(instance, 2);
        Assert.Equal(0.4f, item.Opacity, 1e-6f);
        Assert.Equal(new Vector3(1, 0.2f, 1), item.Tint);
        Assert.Equal(new Vector3(0, 0, 0.75f), item.ScreenTint);
        Assert.Equal(0f, item.EmissionStrength); // 2 * (1 - 1)
        Assert.Equal(0.3f, item.MaskThreshold); // nijilive's alpha threshold offset starts at 0 and is multiplied
    }

    [Fact]
    public void OpacityAndTintAreClamped()
    {
        var instance = Instance(Part(2), Param1D(Binding(2, "opacity", "[[1], [1], [3]]") + "," + Binding(2, "tint.r", "[[1], [1], [-2]]")));

        Set(instance, 1);

        Assert.Equal(1f, Item(instance, 2).Opacity);
        Assert.Equal(0f, Item(instance, 2).Tint.X);
    }

    [Fact]
    public void CompositeBindingsApplyToItsLayer()
    {
        var composite = $$"""{ "uuid": 3, "name": "C", "type": "Composite", "tint": [1, 0.5, 1], "opacity": 0.8, "children": [ {{Part(2, extra: """, "tint": [0.5, 1, 1]""")}} ] }""";
        var instance = Instance($"{composite}, {Part(4)}",
            Param1D($"{Binding(3, "tint.b", "[[1], [1], [0]]")}, {Binding(3, "opacity", "[[1], [1], [0.5]]")}, {Binding(3, "screenTint.r", "[[0], [0], [0.25]]")}"));

        Set(instance, 1);

        // The Composite's values stay on its layer (the end item) instead of being folded into its Parts.
        var end = instance.DrawList.Single(i => i.Kind == DrawItemKind.EndComposite);
        Assert.Equal(new Vector3(1, 0.5f, 0), end.Tint);
        Assert.Equal(0.4f, end.Opacity, 1e-6f);
        Assert.Equal(new Vector3(0.25f, 0, 0), end.ScreenTint);
        Assert.Equal(new Vector3(0.5f, 1, 1), Item(instance, 2).Tint);
        Assert.Equal(Vector3.One, Item(instance, 4).Tint);
    }

    [Fact]
    public void DeformOffsetsMoveVerticesBeforeTheTransform()
    {
        var deform = """[[[[0, 0], [0, 0], [0, 0], [0, 0]]], [[[0, 0], [0, 0], [0, 0], [0, 0]]], [[[4, 0], [0, 0], [0, 0], [0, -2]]]]""";
        var instance = Instance(Part(2, extra: """, "transform": { "trans": [0, 0, 0], "rot": [0, 0, 0], "scale": [2, 2] }"""),
            Param1D(Binding(2, "deform", deform)));

        Set(instance, 0.5f);

        Assert.Equal(new Vector2(4, 0), Item(instance, 2).Positions[0]); // (0 + 2) * 2
        Assert.Equal(new Vector2(0, 18), Item(instance, 2).Positions[3]); // (10 - 1) * 2
    }

    [Theory]
    [InlineData("Nearest", 0.4f, 0f)]
    [InlineData("Nearest", 0.5f, 20f)]
    [InlineData("Linear", 0.5f, 10f)]
    [InlineData("Cubic", 0.5f, 10f)] // Catmull-Rom over 0, 0, 20, 20
    [InlineData("Cubic", 0.25f, 4.0625f)]
    public void InterpolationModes(string mode, float value, float x)
    {
        var instance = Instance(Part(2), Param1D(Binding(2, "transform.t.x", "[[0], [0], [20]]"), mode));

        Set(instance, value);

        Assert.Equal(x, Item(instance, 2).Positions[0].X, 1e-4f);
    }

    [Fact]
    public void TwoDimensionalParametersInterpolateBilinearly()
    {
        var parameter = """
            { "uuid": 50, "name": "XY", "is_vec2": true, "min": [0, 0], "max": [1, 1], "defaults": [0, 0], "axis_points": [[0, 1], [0, 1]],
              "bindings": [ { "node": 2, "param_name": "transform.t.y", "values": [[0, 10], [20, 40]], "isSet": [[true, true], [true, true]] } ] }
            """;
        var instance = Instance(Part(2), parameter);

        Set(instance, 0.5f, 0.5f);

        Assert.Equal(17.5f, Item(instance, 2).Positions[0].Y, 1e-5f);
    }

    [Fact]
    public void UnsetKeyformsUseTheirStoredValues()
    {
        // As nijilive does: the middle keyform is unset, and its stored 100 is used (not 5, from its neighbours).
        var instance = Instance(Part(2), Param1D(Binding(2, "transform.t.x", "[[0], [100], [10]]"), isSet: "[[true], [false], [true]]"));

        Set(instance, 0);

        Assert.Equal(100f, Item(instance, 2).Positions[0].X);
    }

    [Fact]
    public void InstancesDoNotShareParameterState()
    {
        var puppet = TestData.Load(TestData.Puppet(Part(2), Param1D(Binding(2, "transform.t.x", "[[-10], [0], [10]]"))), Texture);
        var a = puppet.CreateInstance();
        var b = puppet.CreateInstance();

        Set(a, 1);
        b.Update(0);

        Assert.Equal(10f, Item(a, 2).Positions[0].X);
        Assert.Equal(0f, Item(b, 2).Positions[0].X);
    }

    [Fact]
    public void EvaluatingAllocatesNothing()
    {
        var bindings = Binding(2, "transform.t.x", "[[-10], [0], [10]]") + "," +
                       Binding(2, "deform", "[[[[1, 1], [1, 1], [1, 1], [1, 1]]], [[[0, 0], [0, 0], [0, 0], [0, 0]]], [[[2, 2], [2, 2], [2, 2], [2, 2]]]]");
        var instance = Instance(Part(2), Param1D(bindings, "Cubic"));
        Set(instance, 0.3f); // warm up

        var before = GC.GetAllocatedBytesForCurrentThread();
        Set(instance, 0.6f);
        Set(instance, -0.2f);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }
}
