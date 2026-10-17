using System.Numerics;
using PaperDoll.Evaluation;

namespace PaperDoll.Tests;

/// <summary>Keyform maths: locating values between axis points, interpolation weights, merge modes.</summary>
public class KeyformTests
{
    [Theory]
    [InlineData(0f, 0, 0f)]
    [InlineData(0.25f, 0, 0.5f)]
    [InlineData(0.5f, 1, 0f)]
    [InlineData(0.75f, 1, 0.5f)]
    [InlineData(1f, 1, 1f)] // the last segment takes the end point
    public void LocateFindsTheSegmentAndOffset(float value, int index, float offset)
    {
        var position = Keyforms.Locate([0f, 0.5f, 1f], value);

        Assert.Equal(index, position.Index);
        Assert.Equal(offset, position.Offset, 1e-6f);
    }

    [Fact]
    public void LocateOnASinglePointAxisIsZero() => Assert.Equal(default, Keyforms.Locate([0f], 0.7f));

    [Fact]
    public void NormalizeMapsTheRangeAndClamps()
    {
        Assert.Equal(new Vector2(0.5f, 0.25f), Keyforms.Normalize(new(0, -0.5f), new(-1, -1), new(1, 1)));
        Assert.Equal(new Vector2(0, 1), Keyforms.Normalize(new(-5, 5), new(-1, -1), new(1, 1)));
        Assert.Equal(Vector2.Zero, Keyforms.Normalize(new(3, 3), new(1, 1), new(1, 1))); // zero-width range
    }

    [Theory]
    [InlineData(0.49f, 0)]
    [InlineData(0.5f, 1)] // nijilive rounds up at the midpoint
    public void NearestPicksOneKeyform(float t, int expected)
    {
        var w = Keyforms.Weights(InterpolateMode.Nearest, 3, new AxisPosition(0, t), active: true);

        Assert.Equal(1, w.Count);
        Assert.Equal(expected, w.Index[0]);
        Assert.Equal(1f, w.Weight[0]);
    }

    [Fact]
    public void LinearWeightsTheTwoNeighbours()
    {
        var w = Keyforms.Weights(InterpolateMode.Linear, 3, new AxisPosition(1, 0.25f), active: true);

        Assert.Equal(2, w.Count);
        Assert.Equal((1, 2), (w.Index[0], w.Index[1]));
        Assert.Equal((0.75f, 0.25f), (w.Weight[0], w.Weight[1]));
    }

    [Fact]
    public void InactiveAxisAlwaysReadsTheFirstKeyform()
    {
        var w = Keyforms.Weights(InterpolateMode.Cubic, 3, new AxisPosition(1, 0.5f), active: false);

        Assert.Equal(1, w.Count);
        Assert.Equal((0, 1f), (w.Index[0], w.Weight[0]));
    }

    [Theory]
    [InlineData(0f, 1f)]
    [InlineData(1f, 4f)]
    [InlineData(0.5f, 2.5f)] // Catmull-Rom through 0, 1, 4, 5 at the midpoint of the 1..4 segment
    public void CubicIsCatmullRom(float t, float expected)
    {
        float[] keys = [0, 1, 4, 5];
        var w = Keyforms.Weights(InterpolateMode.Cubic, 4, new AxisPosition(1, t), active: true);

        var value = 0f;
        for (var i = 0; i < w.Count; i++)
            value += keys[w.Index[i]] * w.Weight[i];
        Assert.Equal(expected, value, 1e-5f);
    }

    [Fact]
    public void CubicClampsAtTheEnds()
    {
        var w = Keyforms.Weights(InterpolateMode.Cubic, 2, new AxisPosition(0, 0.3f), active: true);

        Assert.Equal([0, 0, 1, 1], [w.Index[0], w.Index[1], w.Index[2], w.Index[3]]);
        Assert.Equal(1f, w.Weight[0] + w.Weight[1] + w.Weight[2] + w.Weight[3], 1e-6f);
    }

    // ---- Merge modes -----------------------------------------------------------------------------------------------

    [Fact]
    public void NoInputsLeaveTheValue()
    {
        var inputs = default(ParameterInputs);
        Assert.Equal(new Vector2(0.3f, -0.2f), inputs.Resolve(new(0.3f, -0.2f)));
    }

    [Fact]
    public void AdditiveInputsAddAndMultiplicativeOnesAverage()
    {
        var inputs = default(ParameterInputs);
        var host = new Vector2(1, 2);
        inputs.Push(new(0.5f, 0.5f), ParameterMergeMode.Additive, ParameterMergeMode.Additive, 1, ref host);
        inputs.Push(new(2, 2), ParameterMergeMode.Multiplicative, ParameterMergeMode.Additive, 1, ref host);
        inputs.Push(new(4, 0), ParameterMergeMode.Multiplicative, ParameterMergeMode.Additive, 1, ref host);

        // (1.5, 2.5) * ((2, 2) + (4, 0)) / 2
        Assert.Equal(new Vector2(4.5f, 2.5f), inputs.Resolve(host));
    }

    [Fact]
    public void WeightedInputsDivideByTheirCountNotTheirWeights()
    {
        var inputs = default(ParameterInputs);
        var host = new Vector2(2, 2);
        inputs.Push(new(1, 1), ParameterMergeMode.Weighted, ParameterMergeMode.Additive, 0.5f, ref host);
        inputs.Push(new(1, 1), ParameterMergeMode.Weighted, ParameterMergeMode.Additive, 0.5f, ref host);

        Assert.Equal(new Vector2(1, 1), inputs.Resolve(host)); // 2 * (0.5 + 0.5) / 2
    }

    [Fact]
    public void ForcedReplacesTheValueAndPassthroughUsesTheParametersMode()
    {
        var inputs = default(ParameterInputs);
        var host = new Vector2(2, 2);
        inputs.Push(new(7, 8), ParameterMergeMode.Forced, ParameterMergeMode.Additive, 1, ref host);
        inputs.Push(new(1, 1), ParameterMergeMode.Passthrough, ParameterMergeMode.Additive, 1, ref host);

        Assert.Equal(new Vector2(7, 8), host);
        Assert.Equal(new Vector2(8, 9), inputs.Resolve(host));

        inputs.Clear();
        Assert.Equal(host, inputs.Resolve(host));
    }

    [Fact]
    public void SingleAxisInputsLeaveTheOtherAxisUnchanged()
    {
        var inputs = default(ParameterInputs);
        var host = new Vector2(0.25f, 0);
        inputs.PushAxis(1, 0.5f, ParameterMergeMode.Additive, ParameterMergeMode.Additive, 1, ref host);
        Assert.Equal(new Vector2(0.25f, 0.5f), inputs.Resolve(host)); // nijilive would add 1 to X (docs/design/nijilive-differences.md)

        inputs.Clear();
        inputs.PushAxis(0, 2, ParameterMergeMode.Multiplicative, ParameterMergeMode.Additive, 1, ref host);
        Assert.Equal(new Vector2(0.5f, 0), inputs.Resolve(host));

        inputs.PushAxis(0, 3, ParameterMergeMode.Forced, ParameterMergeMode.Additive, 1, ref host);
        Assert.Equal(new Vector2(3, 0), host);
    }
}
