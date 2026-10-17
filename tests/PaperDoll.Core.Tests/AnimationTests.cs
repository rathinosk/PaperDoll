namespace PaperDoll.Tests;

public class AnimationTests
{
    [Fact]
    public void ReadsAnimationsLanesAndKeyframes()
    {
        var puppet = TestData.Load(TestData.Puppet(parameters: """{ "uuid": 50, "name": "P", "bindings": [] }""", extraRoot: """
            , "animations": {
                "wave": { "timestep": 0.0333, "additive": true, "animationWeight": 0.5, "length": 60, "leadIn": 10, "leadOut": 50,
                  "lanes": [{ "interpolation": "Bezier", "uuid": 50, "target": 0, "merge_mode": "Additive",
                    "keyframes": [{ "frame": 0, "value": 0, "tension": 0.5 }, { "frame": 30, "value": 1, "tension": 0.25 }] }] },
                "idle": { "timestep": 0.0166, "additive": false, "length": 10, "leadIn": -1, "leadOut": -1,
                  "lanes": [{ "interpolation": "Linear", "uuid": 404, "target": 1, "keyframes": [] }] }
              }
            """));

        Assert.Equal(2, puppet.Animations.Count);

        var wave = puppet.Animations["wave"];
        Assert.Equal("wave", wave.Name);
        Assert.Equal(0.0333f, wave.Timestep);
        Assert.True(wave.Additive);
        Assert.Equal(0.5f, wave.Weight);
        Assert.Equal((60, 10, 50), (wave.Length, wave.LeadIn, wave.LeadOut));
        var lane = Assert.Single(wave.Lanes);
        Assert.Equal(50u, lane.ParameterUuid);
        Assert.Equal(InterpolateMode.Bezier, lane.Interpolation);
        Assert.Equal(ParameterMergeMode.Additive, lane.MergeMode);
        Assert.Equal(new Keyframe(30, 1, 0.25f), lane.Keyframes[1]);

        var idle = puppet.Animations["idle"];
        Assert.Null(idle.Weight);
        Assert.Equal(ParameterMergeMode.Forced, idle.Lanes[0].MergeMode);
        Assert.Equal(1, idle.Lanes[0].TargetAxis);
        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.DanglingReference && i.Message.Contains("404", StringComparison.Ordinal));
    }

    [Fact]
    public void NullOrEmptyAnimationsGiveNone()
    {
        Assert.Empty(TestData.Load(TestData.Puppet(extraRoot: """, "animations": null""")).Animations);
        Assert.Empty(TestData.Load(TestData.Puppet(extraRoot: """, "animations": {}""")).Animations);
    }

    [Fact]
    public void AutomationIsReportedAsUnsupported()
    {
        var puppet = TestData.Load(TestData.Puppet(extraRoot: """, "automation": [{ "type": "sine" }]"""));

        Assert.Contains(puppet.Issues, i => i.Kind == LoadIssueKind.UnsupportedFeature && i.Message.Contains("sine", StringComparison.Ordinal));
    }
}
