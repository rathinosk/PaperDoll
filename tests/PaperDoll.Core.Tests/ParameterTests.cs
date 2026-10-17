using System.Numerics;

namespace PaperDoll.Tests;

public class ParameterTests
{
    private const string Mesh = """
        { "uuid": 2, "name": "Face", "type": "Part",
          "mesh": { "verts": [0, 0, 10, 0, 0, 10, 10, 10], "indices": [0, 1, 2, 1, 3, 2] } }
        """;

    [Fact]
    public void ReadsParametersAndBindings()
    {
        var puppet = TestData.Load(TestData.Puppet(Mesh, """
            { "uuid": 50, "name": "Head\u0000", "is_vec2": false, "min": [-1, 0], "max": [1, 0], "defaults": [0.5, 0],
              "axis_points": [[0, 0.5, 1], [0]], "merge_mode": "Weighted", "parentUUID": 7,
              "bindings": [
                { "node": 2, "param_name": "transform.t.x", "values": [[-5], [0], [5]], "isSet": [[true], [false], [true]],
                  "interpolate_mode": "Cubic", "lrVariant": "left", "multiplier": 1 },
                { "node": 2, "param_name": "deform",
                  "values": [[[[0, 0], [1, 0], [0, 1], [1, 1]]], [[[0, 0], [0, 0], [0, 0], [0, 0]]], [[[2, 2], [2, 2], [2, 2], [2, 2]]]],
                  "isSet": [[true], [true], [true]] }
              ] }
            """, extraRoot: """, "groups": [{ "groupUUID": 7, "name": "Head", "color": [1, 0, 0], "count": 1 }]"""));

        var parameter = Assert.Single(puppet.Parameters);
        Assert.Equal("Head", parameter.Name);
        Assert.Equal(0, parameter.Index);
        Assert.False(parameter.IsVec2);
        Assert.Equal(new Vector2(-1, 0), parameter.Min);
        Assert.Equal(new Vector2(0.5f, 0), parameter.Defaults);
        Assert.Equal([0f, 0.5f, 1f], parameter.GetAxisPoints(0).ToArray());
        Assert.Equal(1, parameter.GetAxisPointCount(1));
        Assert.Equal(ParameterMergeMode.Weighted, parameter.MergeMode);
        Assert.Equal(7u, parameter.GroupUuid);
        Assert.Equal(new ParameterGroup(7, "Head", new Vector3(1, 0, 0)), Assert.Single(puppet.ParameterGroups));
        Assert.Same(parameter, puppet.FindParameter("Head"));
        Assert.True(puppet.TryGetParameter(50, out _));

        var value = Assert.IsType<ValueBinding>(parameter.Bindings[0]);
        Assert.Equal(2u, value.TargetUuid);
        Assert.Equal("transform.t.x", value.PropertyName);
        Assert.Equal(InterpolateMode.Cubic, value.Interpolation);
        Assert.Equal((3, 1), (value.XCount, value.YCount));
        Assert.Equal(5f, value.GetValue(2, 0));
        Assert.False(value.IsSet(1, 0));

        var deform = Assert.IsType<DeformBinding>(parameter.Bindings[1]);
        Assert.Equal(InterpolateMode.Linear, deform.Interpolation);
        Assert.Equal(new Vector2(1, 1), deform.GetOffsets(0, 0)[3]);
        Assert.Equal(4, deform.GetOffsets(2, 0).Length);

        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.IgnoredKey && i.Message.Contains("lrVariant", StringComparison.Ordinal));
        Assert.DoesNotContain(puppet.Issues, i => i.Kind == LoadIssueKind.UnknownKey);
    }

    [Fact]
    public void OneDimensionalParameterWithoutAxisPointsGetsNijiliveDefaults()
    {
        var puppet = TestData.Load(TestData.Puppet(parameters: """{ "uuid": 50, "name": "P", "bindings": [] }"""));

        var parameter = puppet.Parameters[0];
        Assert.Equal([0f, 1f], parameter.GetAxisPoints(0).ToArray());
        Assert.Equal([0f], parameter.GetAxisPoints(1).ToArray());
        Assert.Equal(ParameterMergeMode.Additive, parameter.MergeMode);
        Assert.Equal(Vector2.One, parameter.Max);
    }

    [Fact]
    public void KeyformCountMismatchIsAFormatError()
    {
        var json = TestData.Puppet(Mesh, """
            { "uuid": 50, "name": "P", "axis_points": [[0, 1], [0]],
              "bindings": [{ "node": 2, "param_name": "opacity", "values": [[1]], "isSet": [[true]] }] }
            """);

        var ex = Assert.Throws<PuppetFormatException>(() => TestData.Load(json));
        Assert.Contains("2 rows", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BindingsToMissingNodesAreDropped()
    {
        var puppet = TestData.Load(TestData.Puppet(Mesh, """
            { "uuid": 50, "name": "P", "axis_points": [[0, 1], [0]],
              "bindings": [
                { "node": 999, "param_name": "opacity", "values": [[1], [0]], "isSet": [[true], [true]] },
                { "node": 2, "values": [[1], [0]], "isSet": [[true], [true]] },
                { "node": 2, "param_name": "opacity", "values": [[1], [0]], "isSet": [[true], [true]] }
              ] }
            """));

        var binding = Assert.Single(puppet.Parameters[0].Bindings);
        Assert.Equal("opacity", binding.PropertyName);
        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.DanglingReference && i.Message.Contains("999", StringComparison.Ordinal));
        Assert.Contains(puppet.Issues, i => i.Message.Contains("no 'param_name'", StringComparison.Ordinal));
    }

    [Fact]
    public void ParameterToParameterBindingsAreRead()
    {
        var puppet = TestData.Load(TestData.Puppet(parameters: """
            { "uuid": 50, "name": "Driver", "axis_points": [[0, 1], [0]],
              "bindings": [
                { "node": 51, "param_name": "Y", "values": [[0], [1]], "isSet": [[true], [true]] },
                { "node": 51, "param_name": 0, "values": [[0], [1]], "isSet": [[true], [true]] }
              ] },
            { "uuid": 51, "name": "Driven", "is_vec2": true, "bindings": [] }
            """));

        var bindings = puppet.Parameters[0].Bindings;
        Assert.Equal(1, Assert.IsType<ParameterAxisBinding>(bindings[0]).TargetAxis);
        Assert.Equal(0, Assert.IsType<ParameterAxisBinding>(bindings[1]).TargetAxis);
        Assert.Equal("X", bindings[1].PropertyName);
    }

    [Fact]
    public void NonFiniteDeformOffsetsAreRebuiltFromNeighbours()
    {
        var puppet = TestData.Load(TestData.Puppet(Mesh, """
            { "uuid": 50, "name": "P", "axis_points": [[0, 1], [0]],
              "bindings": [{ "node": 2, "param_name": "deform",
                "values": [[[[3, 1], [3, 1], ["nan", 1], [3, 1]]], [[[0, 0], [0, 0], [0, "inf"], [0, 0]]]],
                "isSet": [[true], [true]] }] }
            """));

        var deform = (DeformBinding)puppet.Parameters[0].Bindings[0];
        Assert.Equal(3f, deform.GetOffsets(0, 0)[2].X, 4);
        Assert.Equal(0f, deform.GetOffsets(1, 0)[2].Y, 4);
        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.Recovered);
    }

    [Fact]
    public void NumbersAcceptIntegersFloatsAndNonFiniteStrings()
    {
        var puppet = TestData.Load(TestData.Puppet(Mesh, """
            { "uuid": 50, "name": "P", "axis_points": [[0, 1], [0]],
              "bindings": [{ "node": 2, "param_name": "opacity", "values": [[1], ["-inf"]], "isSet": [[true], [true]] }] }
            """));

        var binding = (ValueBinding)puppet.Parameters[0].Bindings[0];
        Assert.Equal(1f, binding.GetValue(0, 0));
        Assert.Equal(float.NegativeInfinity, binding.GetValue(1, 0));
    }
}
