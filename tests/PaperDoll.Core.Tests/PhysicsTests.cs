using System.Globalization;
using System.Numerics;
using PaperDoll.Evaluation;

namespace PaperDoll.Tests;

/// <summary>SimplePhysics, its integrator and how it drives parameters.</summary>
public class PhysicsTests
{
    private const float Frame = 1f / 60;

    private static string F(float value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A SimplePhysics node (uuid 3) driving parameter 100, under a plain node (uuid 2) rotated by <paramref name="rotation"/>.</summary>
    private static string Rig(string model = "spring_pendulum", string map = "xy", float rotation = 0, string extra = "",
        float angleDamping = 0.5f, float lengthDamping = 0.5f, string siblings = "") => $$"""
        { "uuid": 2, "name": "Holder", "type": "Node", "transform": { "trans": [0, 0, 0], "rot": [0, 0, {{F(rotation)}}], "scale": [1, 1] },
          "children": [
            { "uuid": 3, "name": "Phys", "type": "SimplePhysics", "param": 100, "model_type": "{{model}}", "map_mode": "{{map}}",
              "length": 100, "frequency": 1, "angle_damping": {{F(angleDamping)}}, "length_damping": {{F(lengthDamping)}},
              "output_scale": [1, 1]{{extra}} }
          ] }{{siblings}}
        """;

    private const string Swing = """
        { "uuid": 100, "name": "Swing", "is_vec2": true, "min": [-1, -1], "max": [1, 1], "defaults": [0, 0],
          "axis_points": [[0, 1], [0, 1]], "bindings": [] }
        """;

    private static PuppetInstance Instance(string children, string parameters = Swing) =>
        TestData.Load(TestData.Puppet(children, parameters)).CreateInstance();

    private static Vector2 Value(PuppetInstance instance) => instance.GetParameterValue(instance.Puppet.Parameters[0]);

    private static void Run(PuppetInstance instance, float seconds)
    {
        for (var t = 0f; t < seconds; t += Frame)
            instance.Update(Frame);
    }

    private static void Near(Vector2 expected, Vector2 actual, float tolerance = 1e-4f) =>
        Assert.True(Vector2.Distance(expected, actual) <= tolerance, $"expected {expected}, got {actual}");

    [Fact]
    public void TheLoaderLinksEachParameterToItsDriver()
    {
        var puppet = TestData.Load(TestData.Puppet(Rig(), Swing));

        Assert.Same(puppet.Nodes.OfType<SimplePhysics>().Single(), puppet.Parameters[0].PhysicsDriver);
    }

    /// <summary>
    /// At rest the bob hangs straight down in puppet space, so under a node turned by 30 degrees it lies at 30 degrees in
    /// the node's own space; each map mode reports that direction its own way, scaled by the output scale.
    /// </summary>
    [Theory]
    [InlineData("spring_pendulum", "xy", 0.5f, 0.1339746f)]
    [InlineData("spring_pendulum", "yx", 0.1339746f, 0.5f)]
    [InlineData("spring_pendulum", "angle_length", -1f / 6, 1f)]
    [InlineData("spring_pendulum", "length_angle", 1f, -1f / 6)]
    [InlineData("pendulum", "xy", 0.5f, 0.1339746f)]
    [InlineData("pendulum", "angle_length", -1f / 6, 1f)]
    public void EachMapModeReportsTheRestDirection(string model, string map, float x, float y)
    {
        var instance = Instance(Rig(model, map, MathF.PI / 6).Replace("\"output_scale\": [1, 1]", "\"output_scale\": [2, 3]"));

        Run(instance, 1);

        Near(new Vector2(2 * x, 3 * y), Value(instance));
    }

    [Theory]
    [InlineData("spring_pendulum")]
    [InlineData("pendulum")]
    public void AtRestNothingMoves(string model)
    {
        var instance = Instance(Rig(model));

        Run(instance, 5);

        Near(Vector2.Zero, Value(instance));
    }

    [Theory]
    [InlineData("spring_pendulum")]
    [InlineData("pendulum")]
    public void MovingThePuppetSwingsItsPhysicsAndThenItSettles(string model)
    {
        var instance = Instance(Rig(model));
        Run(instance, 0.5f);

        for (var i = 0; i < 10; i++) // to the right at 300 units a second: the bob lags behind, to the left
        {
            instance.ScenePosition += new Vector2(5, 0);
            instance.Update(Frame);
        }

        Assert.True(Value(instance).X < -0.01f, $"expected a swing to negative X, got {Value(instance)}");

        Run(instance, 10);
        Near(Vector2.Zero, Value(instance), 1e-3f);
    }

    [Fact]
    public void AConstantScenePositionChangesNothing()
    {
        var instance = Instance(Rig());
        instance.ScenePosition = new Vector2(1000, -400);
        instance.ResetPhysics();

        Run(instance, 2);

        Near(Vector2.Zero, Value(instance));
    }

    [Fact]
    public void InstancesSimulateSeparately()
    {
        var puppet = TestData.Load(TestData.Puppet(Rig(), Swing));
        var moved = puppet.CreateInstance();
        var still = puppet.CreateInstance();

        moved.ScenePosition = new Vector2(30, 0);
        moved.Update(Frame);
        still.Update(Frame);

        Assert.NotEqual(Vector2.Zero, Value(moved));
        Near(Vector2.Zero, Value(still));
    }

    [Fact]
    public void ALongStepIsCutIntoTicksAndCappedAtTenSeconds()
    {
        var puppet = TestData.Load(TestData.Puppet(Rig(), Swing));
        PuppetInstance Disturbed()
        {
            var instance = puppet.CreateInstance();
            instance.ScenePosition = new Vector2(40, 0);
            return instance;
        }

        var once = Disturbed();
        var inTicks = Disturbed();
        once.Update(0.05f);
        for (var i = 0; i < 5; i++)
            inTicks.Update(0.01f);
        Near(Value(inTicks), Value(once), 1e-3f); // nijilive's last tick takes whatever the 0.01 s ticks leave

        var thirty = Disturbed();
        var ten = Disturbed();
        thirty.Update(0.3f); // not yet settled, so the cap shows
        ten.Update(0.3f);
        thirty.ScenePosition = ten.ScenePosition = new Vector2(80, 0);
        thirty.Update(30);
        ten.Update(10);
        Assert.Equal(Value(ten), Value(thirty));
    }

    [Fact]
    public void TurnedOffPhysicsLeavesTheParameterToTheHost()
    {
        var instance = Instance(Rig());
        var parameter = instance.Puppet.Parameters[0];

        instance.SetParameterValue(parameter, new Vector2(0.5f, 0.5f));
        instance.Update(Frame);
        Near(Vector2.Zero, Value(instance)); // physics replaces the host's value

        instance.PhysicsEnabled = false;
        instance.SetParameterValue(parameter, new Vector2(0.5f, 0.5f));
        instance.Update(Frame);
        Assert.Equal(new Vector2(0.5f, 0.5f), Value(instance));
    }

    [Fact]
    public void BindingsScaleThePhysicsOutput()
    {
        var strength = """
            , { "uuid": 101, "name": "Strength", "min": [0, 0], "max": [1, 0], "defaults": [0, 0], "axis_points": [[0, 1], [0]],
                "bindings": [ { "node": 3, "param_name": "outputScale.x", "values": [[1], [2]], "isSet": [[true], [true]] } ] }
            """;
        var instance = Instance(Rig(rotation: MathF.PI / 6), Swing + strength);
        instance.SetParameterValue(instance.Puppet.Parameters[1], new Vector2(1, 0));

        Run(instance, 1);

        Near(new Vector2(1f, 0.1339746f), Value(instance));
    }

    [Fact]
    public void ADisabledDriverLeavesItsParameterUnapplied()
    {
        // nijilive skips a driven parameter in its parameter pass and only its driver applies it; disabled, nothing does.
        var part = """
            , { "uuid": 4, "name": "P", "type": "Part", "mesh": { "verts": [0, 0, 10, 0, 10, 10], "uvs": [0, 0, 1, 0, 1, 1], "indices": [0, 1, 2] }, "textures": [0] }
            """;
        var parameter = Swing.Replace("\"bindings\": []", """
            "bindings": [ { "node": 4, "param_name": "transform.t.x", "values": [[0, 0], [10, 10]], "isSet": [[true, true], [true, true]] } ]
            """);
        var json = TestData.Puppet(Rig(siblings: part).Replace("\"type\": \"SimplePhysics\"", "\"type\": \"SimplePhysics\", \"enabled\": false"), parameter);
        var instance = TestData.Load(json, [((byte)0, TestData.Png(2, 2, TestData.Pixels2x2))]).CreateInstance();

        instance.SetParameterValue(instance.Puppet.Parameters[0], new Vector2(1, 0));
        instance.Update(Frame);
        Assert.Equal(Vector2.Zero, instance.DrawList.Single(i => i.Node.Uuid == 4).Positions[0]);

        instance.PhysicsEnabled = false;
        instance.Update(Frame);
        Assert.Equal(new Vector2(10, 0), instance.DrawList.Single(i => i.Node.Uuid == 4).Positions[0]);
    }

    [Fact]
    public void AMeshGroupMovingThePhysicsNodeSwingsItOneUpdateLater()
    {
        // A translate_children MeshGroup whose deform binding doubles x across its square: at P = 1 the physics node's
        // origin moves from x = 20 to 40 in the group's space. nijilive's driver sees that move from the next update on,
        // through the anchor the MeshGroup filter left (preProcess / postProcess).
        const string group = """
            { "uuid": 10, "name": "MG", "type": "MeshGroup", "translate_children": true, "transform": { "trans": [50, 0, 0] },
              "mesh": { "verts": [0, 0, 100, 0, 100, 100, 0, 100], "uvs": [], "indices": [0, 1, 2, 0, 2, 3] },
              "children": [ { "uuid": 3, "name": "Phys", "type": "SimplePhysics", "param": 100, "model_type": "spring_pendulum",
                              "map_mode": "xy", "length": 100, "transform": { "trans": [20, 30, 0] } } ] }
            """;
        const string deform = """
            , { "uuid": 50, "name": "P", "min": [0, 0], "max": [1, 0], "defaults": [0, 0], "axis_points": [[0, 1], [0]],
                "bindings": [ { "node": 10, "param_name": "deform", "isSet": [[true], [true]],
                                "values": [[[[0, 0], [0, 0], [0, 0], [0, 0]]], [[[0, 0], [100, 0], [100, 0], [0, 0]]]] } ] }
            """;
        var instance = Instance(group, Swing + deform);
        Run(instance, 1);

        instance.SetParameterValue(instance.Puppet.Parameters[1], new Vector2(1, 0));
        instance.Update(Frame);
        Near(Vector2.Zero, Value(instance)); // the driver stepped before the MeshGroup moved the node
        instance.Update(Frame);
        Assert.True(Value(instance).X < -0.01f, $"expected a swing to negative X, got {Value(instance)}");

        Run(instance, 10);
        Near(Vector2.Zero, Value(instance), 1e-3f);
    }

    [Fact]
    public void UpdatingWithPhysicsAllocatesNothing()
    {
        var instance = Instance(Rig() + "," + Rig("pendulum", "angle_length").Replace("\"uuid\": 2", "\"uuid\": 5").Replace("\"uuid\": 3", "\"uuid\": 6"));
        Run(instance, 0.1f);

        var before = GC.GetAllocatedBytesForCurrentThread();
        instance.ScenePosition += new Vector2(3, 1);
        instance.Update(0.05f);
        instance.Update(Frame);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    // ---- The models, stepped directly ----------------------------------------------------------------------------

    private static PhysicsDriver Driver(string model, float angleDamping, float lengthDamping)
    {
        var puppet = TestData.Load(TestData.Puppet(Rig(model, angleDamping: angleDamping, lengthDamping: lengthDamping), Swing));
        var driver = new PhysicsDriver(puppet.Nodes.OfType<SimplePhysics>().Single(), puppet.Parameters[0], puppet.Physics);
        driver.Reset(Vector2.Zero);
        return driver;
    }

    /// <summary>Times between successive crossings of <paramref name="level"/> by <paramref name="signal"/>, stepped every 0.01 s.</summary>
    private static List<float> Crossings(Func<float> step, float level, float seconds)
    {
        var times = new List<float>();
        var previous = step() - level;
        for (var t = 0.01f; t < seconds; t += 0.01f)
        {
            var current = step() - level;
            if (MathF.Sign(current) != MathF.Sign(previous) && current != 0)
                times.Add(t - 0.01f * current / (current - previous)); // linear interpolation inside the step
            previous = current;
        }

        return times;
    }

    [Fact]
    public void AnUndampedPendulumSwingsAtItsNaturalPeriodWithoutLosingAmplitude()
    {
        var driver = Driver("pendulum", angleDamping: 0, lengthDamping: 0);
        var offsets = PhysicsOffsets.Default;
        var anchor = new Vector2(5, 0); // the anchor moves 5 units: the bob now hangs about 2.9 degrees off
        var maxAmplitude = 0f;
        var lateAmplitude = 0f;
        var stepCount = 0;
        var crossings = Crossings(() =>
        {
            driver.Step(0.01f, offsets, anchor, Matrix4x4.CreateTranslation(anchor.X, anchor.Y, 0));
            var x = MathF.Abs(driver.Output.X - anchor.X);
            if (++stepCount < 100)
                maxAmplitude = MathF.Max(maxAmplitude, x);
            else
                lateAmplitude = MathF.Max(lateAmplitude, x);
            return driver.Output.X;
        }, anchor.X, 6.5f);

        // g = 9.8 m/s² at 1000 units a metre, length 100: T = 2π sqrt(100 / 9800) = 0.6347 s (small angle)
        var period = 2 * (crossings[^1] - crossings[0]) / (crossings.Count - 1);
        Assert.InRange(period, 0.6347f * 0.999f, 0.6347f * 1.003f);
        Assert.InRange(lateAmplitude, maxAmplitude * 0.999f, maxAmplitude * 1.001f);
    }

    [Fact]
    public void AnUndampedSpringOscillatesAtItsFrequency()
    {
        var driver = Driver("spring_pendulum", angleDamping: 0, lengthDamping: 0);
        var offsets = PhysicsOffsets.Default;
        var anchor = new Vector2(0, -10); // the anchor rises 10 units: the bob is 10 below its rest point

        var crossings = Crossings(() =>
        {
            driver.Step(0.01f, offsets, anchor, Matrix4x4.CreateTranslation(anchor.X, anchor.Y, 0));
            return driver.Output.Y;
        }, anchor.Y + 100, 5.2f);

        var period = 2 * (crossings[^1] - crossings[0]) / (crossings.Count - 1);
        Assert.InRange(period, 0.999f, 1.001f); // frequency 1 Hz
    }

    [Fact]
    public void ACriticallyDampedSpringReturnsWithoutOvershooting()
    {
        var driver = Driver("spring_pendulum", angleDamping: 1, lengthDamping: 1);
        var offsets = PhysicsOffsets.Default;
        var anchor = new Vector2(0, -10);

        var crossings = Crossings(() =>
        {
            driver.Step(0.01f, offsets, anchor, Matrix4x4.CreateTranslation(anchor.X, anchor.Y, 0));
            return driver.Output.Y;
        }, anchor.Y + 100 - 1e-3f, 5f); // it comes from below (larger Y), so crossing above its rest point would be an overshoot

        Assert.Empty(crossings);
        Assert.InRange(driver.Output.Y, anchor.Y + 100 - 1e-2f, anchor.Y + 100 + 1e-2f);
    }

    [Fact]
    public void InvalidSettingsStopTheForcesButNeverGiveNonFiniteOutput()
    {
        var driver = Driver("spring_pendulum", angleDamping: 0.5f, lengthDamping: 0.5f);
        driver.Step(0.01f, PhysicsOffsets.Default, Vector2.Zero, Matrix4x4.Identity);
        var before = driver.Output;

        var broken = PhysicsOffsets.Default with { Gravity = float.NaN };
        for (var i = 0; i < 10; i++)
        {
            if (driver.Step(0.01f, broken, Vector2.Zero, Matrix4x4.Identity) is { } value)
                Assert.True(float.IsFinite(value.X) && float.IsFinite(value.Y));
        }

        Near(before, driver.Output, 1e-3f); // at rest, with no forces, the bob stays where it was
    }

    // ---- The samples ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Each sample settles, swings when moved from side to side, stays finite, and settles back where it was (skipped
    /// when <c>.samples/</c> is absent).
    /// </summary>
    [Theory]
    [MemberData(nameof(SampleParameterTests.Files), MemberType = typeof(SampleParameterTests))]
    public void SamplesSwingAndSettleBack(string file)
    {
        var puppet = Puppet.Load(SampleTests.SamplePath(file));
        var driven = puppet.Parameters.Where(p => p.PhysicsDriver is not null).ToList();
        Assert.NotEmpty(driven);
        var instance = puppet.CreateInstance();
        Run(instance, 15);
        var rest = driven.Select(instance.GetParameterValue).ToList();

        var swung = false;
        for (var t = 0f; t < 3; t += Frame)
        {
            instance.ScenePosition = new Vector2(300 * MathF.Sin(t * 2 * MathF.PI), 0);
            instance.Update(Frame);
            swung |= driven.Select(instance.GetParameterValue).Zip(rest).Any(v => Vector2.Distance(v.First, v.Second) > 0.05f);
            SampleParameterTests.AssertFinite(instance, $"t = {t}");
        }

        Assert.True(swung, "moving the puppet moved none of its physics");
        Run(instance, 60); // MCG's chest physics is lightly damped and takes well over 15 s to settle
        foreach (var (parameter, value) in driven.Zip(rest))
            Assert.True(Vector2.Distance(value, instance.GetParameterValue(parameter)) <= 1e-2f, $"{parameter.Name}: rest {value}, now {instance.GetParameterValue(parameter)}");
    }
}
