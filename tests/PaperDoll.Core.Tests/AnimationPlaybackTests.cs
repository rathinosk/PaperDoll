using System.Globalization;
using System.Numerics;
using PaperDoll.Evaluation;

namespace PaperDoll.Tests;

/// <summary>Lane interpolation and playback. Animations use 0.125 s frames, so frame arithmetic is exact.</summary>
public class AnimationPlaybackTests
{
    private const float Frame = 0.125f;

    private const string Parameters = """
        { "uuid": 50, "name": "P", "is_vec2": false, "min": [-1, 0], "max": [1, 0], "defaults": [0, 0], "bindings": [] },
        { "uuid": 51, "name": "XY", "is_vec2": true, "min": [-1, -1], "max": [1, 1], "defaults": [0, 0], "bindings": [] }
        """;

    // ---- Lane maths -------------------------------------------------------------------------------------------------

    private static readonly Keyframe[] UpDown = [new(0, 0), new(10, 1), new(20, 0)];

    [Theory]
    [InlineData(-5f, 0f)]
    [InlineData(0f, 0f)]
    [InlineData(5f, 0.5f)]
    [InlineData(15f, 0.5f)]
    [InlineData(25f, 0f)]
    public void LinearInterpolatesAndHoldsTheEnds(float frame, float expected) =>
        Assert.Equal(expected, LaneSampler.Sample(UpDown, InterpolateMode.Linear, frame), 5);

    [Fact]
    public void SteppedHoldsTheEarlierKeyIncludingOnTheLaterOne()
    {
        Assert.Equal(0f, LaneSampler.Sample(UpDown, InterpolateMode.Stepped, 9.9f));
        Assert.Equal(0f, LaneSampler.Sample(UpDown, InterpolateMode.Stepped, 10f)); // nijilive: a key's own frame still reads the one before
        Assert.Equal(1f, LaneSampler.Sample(UpDown, InterpolateMode.Stepped, 10.5f));
    }

    [Fact]
    public void NearestSwitchesJustPastHalfway()
    {
        Assert.Equal(0f, LaneSampler.Sample(UpDown, InterpolateMode.Nearest, 5f));
        Assert.Equal(1f, LaneSampler.Sample(UpDown, InterpolateMode.Nearest, 5.01f));
    }

    [Fact]
    public void CubicIsCatmullRomOverTheKeyValues()
    {
        // p0 = p1 = 0 (clamped at the start), p2 = 1, p3 = 0, t = 0.5.
        Assert.Equal(0.5625f, LaneSampler.Sample(UpDown, InterpolateMode.Cubic, 5f), 5);
        Assert.Equal(1f, LaneSampler.Sample(UpDown, InterpolateMode.Cubic, 10f), 5);
    }

    [Theory]
    [InlineData(0.5f, 0.25f, 0.25f)]       // linear
    [InlineData(0f, 0.25f, 0.15625f)]      // smoothstep: 3t^2 - 2t^3
    [InlineData(1f, 0.25f, 0.34375f)]      // 2t^3 - 3t^2 + 2t: fast at the keys
    [InlineData(0.5f, 0.8f, 0.8f)]
    public void BezierEasesByTheLaterKeysTension(float tension, float t, float expected)
    {
        Keyframe[] frames = [new(0, 0), new(100, 1, tension)];
        Assert.Equal(expected, LaneSampler.Sample(frames, InterpolateMode.Bezier, t * 100), 4);
    }

    [Fact]
    public void BezierIsClampedWhenAHighTensionOvershoots()
    {
        Assert.True(LaneSampler.Hermite(0, 16, 1, 16, 0.3f) > 1); // tension 8
        Keyframe[] frames = [new(0, 0), new(100, 1, 8)];
        Assert.Equal(1f, LaneSampler.Sample(frames, InterpolateMode.Bezier, 30));
    }

    [Fact]
    public void EmptyAndSingleKeyLanes()
    {
        Assert.Equal(0f, LaneSampler.Sample([], InterpolateMode.Linear, 3));
        Assert.Equal(0.7f, LaneSampler.Sample([new(5, 0.7f)], InterpolateMode.Cubic, 0));
    }

    [Fact]
    public void KeyframesOutOfOrderAreSortedAtLoad()
    {
        var puppet = Load(Anim("a", 10, Lane(50, 0, "Linear", "Forced", (10, 1), (0, 0))));
        Assert.Equal([new Keyframe(0, 0), new Keyframe(10, 1)], puppet.Animations["a"].Lanes[0].Keyframes);
        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.Recovered && i.Message.Contains("sorted", StringComparison.Ordinal));
        Assert.Same(puppet.Parameters[0], puppet.Animations["a"].Lanes[0].Parameter);
    }

    // ---- Playback ---------------------------------------------------------------------------------------------------

    [Fact]
    public void OneShotPlaysToItsLastFrameThenGivesTheValueBack()
    {
        var (instance, p) = Create(Anim("a", 5, Lane(50, 0, "Linear", "Forced", (0, 0), (4, 1))));
        instance.SetParameterValue(p, new(-0.5f, 0));
        var playback = instance.PlayAnimation("a");

        float[] expected = [0.25f, 0.5f, 0.75f, 1f];
        foreach (var value in expected)
        {
            instance.Update(Frame);
            Assert.Equal(value, instance.GetEvaluatedValue(p).X, 5);
            Assert.Equal(-0.5f, instance.GetParameterValue(p).X); // the host's value is never overwritten
        }

        Assert.False(playback.IsRunning);
        Assert.Equal(0, playback.Frame);
        instance.Update(Frame);
        Assert.Equal(-0.5f, instance.GetEvaluatedValue(p).X);
    }

    [Fact]
    public void LoopAtOneFramePerUpdateShowsNijilivesFrames()
    {
        var (instance, _) = Create(Anim("a", 4, Lane(50, 0, "Linear", "Forced", (0, 0), (3, 1))));
        var playback = instance.PlayAnimation("a", loop: true);

        var frames = new List<int>();
        for (var i = 0; i < 6; i++)
        {
            instance.Update(Frame);
            frames.Add(playback.Frame);
        }

        Assert.Equal([1, 2, 3, 0, 1, 2], frames); // nijilive wraps when the frame reaches the length
        Assert.Equal(1, playback.LoopCount);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(144)]
    [InlineData(240)]
    public void LoopTimingDoesNotDependOnTheUpdateRate(int rate)
    {
        // nijilive holds a loop that ends at its length on its last frame for good when a step is under half a frame.
        var (instance, _) = Create(Anim("a", 600, Lane(50, 0, "Linear", "Forced", (0, 0), (10, 1)), timestep: 1 / 60f, leadIn: 1, leadOut: 600));
        var playback = instance.PlayAnimation("a", loop: true);

        for (var i = 0; i < rate * 25; i++) // 25 s: two full loops of 599 frames (frames 1 to 600) and a bit
            instance.Update(1f / rate);

        Assert.Equal(2, playback.LoopCount);
        Assert.Equal(25 * 60 - 2 * 599, playback.FramePosition, 0.5f);
    }

    [Fact]
    public void StoppingALoopPlaysOnThroughTheLeadOut()
    {
        var (instance, p) = Create(Anim("a", 20, Lane(50, 0, "Linear", "Forced", (0, 0), (19, 0.95f)), leadIn: 4, leadOut: 12));
        var playback = instance.PlayAnimation("a", loop: true);
        Assert.True(playback.HasLeadIn && playback.HasLeadOut);
        Assert.Equal((4, 12), (playback.LoopStart, playback.LoopEnd));

        Step(instance, 12);
        Assert.Equal((4, 1), (playback.Frame, playback.LoopCount)); // reached the lead-out, so back to the end of the lead-in

        playback.Stop();
        Assert.True(playback.IsStopping && playback.IsRunning && !playback.IsPlaying);
        Step(instance, 14);
        Assert.Equal(18, playback.Frame);
        Step(instance, 1);
        Assert.False(playback.IsRunning);
        Assert.Equal(0.95f, instance.GetEvaluatedValue(p).X, 5); // the last frame is shown once
        Step(instance, 1);
        Assert.Equal(0f, instance.GetEvaluatedValue(p).X);
    }

    [Fact]
    public void StopIsImmediateWithoutALeadOutOrWhenAskedFor()
    {
        var (instance, p) = Create(
            Anim("plain", 10, Lane(50, 0, "Linear", "Forced", (0, 0), (9, 0.9f))) + "," +
            Anim("leadout", 20, Lane(50, 0, "Linear", "Forced", (0, 0), (19, 0.95f)), leadOut: 12));

        var plain = instance.PlayAnimation("plain", loop: true);
        Step(instance, 3);
        plain.Stop();
        Assert.False(plain.IsRunning);
        Assert.Equal(0, plain.Frame);

        var leadOut = instance.PlayAnimation("leadout", loop: true);
        Step(instance, 3);
        leadOut.Stop(immediate: true);
        Assert.False(leadOut.IsRunning);
        Step(instance, 1);
        Assert.Equal(0f, instance.GetEvaluatedValue(p).X);
    }

    [Fact]
    public void PauseKeepsTheFrameAndKeepsDriving()
    {
        var (instance, p) = Create(Anim("a", 10, Lane(50, 0, "Linear", "Forced", (0, 0), (8, 1))));
        var playback = instance.PlayAnimation("a");
        Step(instance, 2);
        playback.Pause();
        Step(instance, 5);
        Assert.True(playback.IsPaused && playback.IsRunning);
        Assert.Equal(2, playback.Frame);
        Assert.Equal(0.25f, instance.GetEvaluatedValue(p).X, 5);

        playback.Play(); // resumes
        Step(instance, 1);
        Assert.Equal(3, playback.Frame);
    }

    [Fact]
    public void WithoutLeadOutAOneShotHoldsItsLastFrame()
    {
        var (instance, p) = Create(Anim("a", 5, Lane(50, 0, "Linear", "Forced", (0, 0), (4, 1))));
        var playback = instance.PlayAnimation("a", loop: false, playLeadOut: false);
        Step(instance, 10);
        Assert.True(playback.IsRunning);
        Assert.Equal(4, playback.Frame);
        Assert.Equal(1f, instance.GetEvaluatedValue(p).X);
    }

    [Theory]
    [InlineData("Forced", 1f, 0.6f)]          // 0.2 blended halfway to 1
    [InlineData("Additive", 0.4f, 0.4f)]      // 0.2 + 0.4 / 2
    [InlineData("Multiplicative", 3f, 0.4f)]  // 0.2 * (halfway from 1 to 3)
    [InlineData("Weighted", 3f, 0.4f)]
    public void StrengthTakesEachMergeModeHalfway(string mode, float laneValue, float expected)
    {
        var (instance, p) = Create(Anim("a", 10, Lane(50, 0, "Linear", mode, (0, laneValue))));
        instance.SetParameterValue(p, new(0.2f, 0));
        instance.PlayAnimation("a", loop: true).Strength = 0.5f;
        Step(instance, 1);
        Assert.Equal(expected, instance.GetEvaluatedValue(p).X, 5);
    }

    [Fact]
    public void ZeroStrengthHasNoEffect()
    {
        var (instance, p) = Create(Anim("a", 10, Lane(50, 0, "Linear", "Multiplicative", (0, 3))));
        instance.SetParameterValue(p, new(0.2f, 0));
        instance.PlayAnimation("a", loop: true).Strength = 0;
        Step(instance, 1);
        Assert.Equal(0.2f, instance.GetEvaluatedValue(p).X, 5);
    }

    [Fact]
    public void TheLaterStartedForcedLaneWins()
    {
        var (instance, p) = Create(
            Anim("a", 10, Lane(50, 0, "Linear", "Forced", (0, 0.3f))) + "," + Anim("b", 10, Lane(50, 0, "Linear", "Forced", (0, 0.7f))));
        instance.PlayAnimation("a", loop: true);
        instance.PlayAnimation("b", loop: true);
        Step(instance, 1);
        Assert.Equal(0.7f, instance.GetEvaluatedValue(p).X, 5);

        instance.PlayAnimation("a", loop: true);
        Step(instance, 1);
        Assert.Equal(0.3f, instance.GetEvaluatedValue(p).X, 5);

        instance.StopAnimations();
        Step(instance, 1);
        Assert.Equal(0f, instance.GetEvaluatedValue(p).X);
    }

    [Fact]
    public void AnAdditiveLaneOnOneAxisLeavesTheOtherAlone()
    {
        var (instance, _) = Create(Anim("a", 10, Lane(51, 0, "Linear", "Additive", (0, 0.5f))));
        var xy = instance.Puppet.Parameters[1];
        instance.SetParameterValue(xy, new(0.1f, 0.2f));
        instance.PlayAnimation("a", loop: true);
        Step(instance, 1);
        Assert.Equal(new Vector2(0.6f, 0.2f), instance.GetEvaluatedValue(xy)); // nijilive would give Y 1.2
    }

    [Fact]
    public void LanesWithNoParameterOrAMissingAxisAreSkipped()
    {
        var (instance, p) = Create(Anim("a", 10,
            Lane(404, 0, "Linear", "Forced", (0, 1)) + "," + Lane(50, 1, "Linear", "Forced", (0, 1)) + "," + Lane(50, 2, "Linear", "Forced", (0, 1))));
        instance.PlayAnimation("a", loop: true);
        Step(instance, 1);
        Assert.Equal(Vector2.Zero, instance.GetEvaluatedValue(p));
    }

    [Fact]
    public void SpeedAndSeek()
    {
        var (instance, p) = Create(Anim("a", 20, Lane(50, 0, "Linear", "Forced", (0, 0), (10, 1))));
        var playback = instance.PlayAnimation("a");
        playback.Speed = 2;
        Step(instance, 2);
        Assert.Equal(4, playback.Frame);

        playback.Pause();
        playback.Seek(7.5f);
        Step(instance, 1);
        Assert.Equal(0.75f, instance.GetEvaluatedValue(p).X, 5);
        Assert.Equal(7.5f * Frame, playback.Seconds, 5);
    }

    [Fact]
    public void PlaybacksFollowThePuppetsAnimations()
    {
        var (instance, _) = Create(Anim("b", 10, "") + "," + Anim("a", 10, ""));
        Assert.Equal(["b", "a"], instance.Animations.Select(a => a.Name));
        Assert.True(instance.TryGetAnimation("a", out var a));
        Assert.Same(a, instance.GetAnimation("a"));
        Assert.Throws<KeyNotFoundException>(() => instance.PlayAnimation("missing"));
        Assert.All(instance.Animations, playback => Assert.False(playback.IsRunning));
    }

    [Fact]
    public void AnimatingAllocatesNothing()
    {
        var (instance, _) = Create(Anim("a", 40, Lane(50, 0, "Cubic", "Forced", (0, 0), (10, 1), (20, -1))) + "," +
                                   Anim("b", 40, Lane(51, 1, "Bezier", "Additive", (0, 0), (30, 1))));
        instance.PlayAnimation("a", loop: true);
        instance.PlayAnimation("b", loop: true);
        Step(instance, 50); // warm up, through a few loops

        var before = GC.GetAllocatedBytesForCurrentThread();
        Step(instance, 100);
        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
    }

    // ---- Yumi (Inochi2D's sample with Inochi Creator animations) --------------------------------------------------------

    [Fact]
    public void YumiPlaysEachAnimationWithFiniteOutput()
    {
        var puppet = Puppet.Load(SampleTests.SamplePath("inochi3d-samples/Yumi/Yumi.inx"));
        Assert.Contains("Blink", puppet.Animations.Keys);
        Assert.Contains("Head Sway", puppet.Animations.Keys);
        Assert.All(puppet.Animations.Values.SelectMany(a => a.Lanes), lane => Assert.NotNull(lane.Parameter));

        foreach (var name in puppet.Animations.Keys)
        {
            var instance = puppet.CreateInstance();
            var playback = instance.PlayAnimation(name, loop: true);
            for (var i = 0; i < 300; i++)
            {
                instance.Update(1 / 144f);
                foreach (var item in instance.DrawList)
                {
                    foreach (var v in item.Positions)
                        Assert.True(float.IsFinite(v.X) && float.IsFinite(v.Y), $"{name}: {item.Part?.Name}");
                }
            }

            Assert.True(playback.IsRunning, name);
        }
    }

    [Fact]
    public void YumisBlinkLoopsAt240Hz()
    {
        var puppet = Puppet.Load(SampleTests.SamplePath("inochi3d-samples/Yumi/Yumi.inx"));
        var instance = puppet.CreateInstance();
        var blink = instance.PlayAnimation("Blink", loop: true);
        for (var i = 0; i < 240 * 21; i++)
            instance.Update(1 / 240f);
        Assert.Equal(2, blink.LoopCount); // 599 frames a loop, about 10 s
    }

    // ---- Helpers ----------------------------------------------------------------------------------------------------

    private static void Step(PuppetInstance instance, int updates)
    {
        for (var i = 0; i < updates; i++)
            instance.Update(Frame);
    }

    private static (PuppetInstance Instance, Parameter P) Create(string animations)
    {
        var instance = Load(animations).CreateInstance();
        return (instance, instance.Puppet.Parameters[0]);
    }

    private static Puppet Load(string animations) =>
        TestData.Load(TestData.Puppet(parameters: Parameters, extraRoot: $$""", "animations": { {{animations}} }"""));

    private static string Anim(string name, int length, string lanes, float timestep = Frame, int leadIn = -1, int leadOut = -1) =>
        string.Create(CultureInfo.InvariantCulture,
            $$"""
            "{{name}}": { "timestep": {{timestep:R}}, "additive": false, "length": {{length}}, "leadIn": {{leadIn}}, "leadOut": {{leadOut}},
              "animationWeight": 1, "lanes": [{{lanes}}] }
            """);

    private static string Lane(uint uuid, int axis, string interpolation, string mode, params (int Frame, float Value)[] keys) =>
        string.Create(CultureInfo.InvariantCulture,
            $$"""
            { "interpolation": "{{interpolation}}", "uuid": {{uuid}}, "target": {{axis}}, "merge_mode": "{{mode}}",
              "keyframes": [{{string.Join(",", keys.Select(k => string.Create(CultureInfo.InvariantCulture, $$"""{ "frame": {{k.Frame}}, "value": {{k.Value:R}}, "tension": 0.5 }""")))}}] }
            """);
}
