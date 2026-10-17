using System.Numerics;

namespace PaperDoll.Tests;

public class InstanceTests
{
    private static Puppet TwoParameters() => TestData.Load(TestData.Puppet(parameters: """
        { "uuid": 50, "name": "Mouth", "min": [0, 0], "max": [1, 0], "defaults": [0.25, 0], "bindings": [] },
        { "uuid": 51, "name": "Head", "is_vec2": true, "min": [-1, -1], "max": [1, 1], "defaults": [0, 0.5], "bindings": [] }
        """));

    [Fact]
    public void NewInstanceStartsAtDefaults()
    {
        var puppet = TwoParameters();
        var instance = puppet.CreateInstance();

        Assert.Same(puppet, instance.Puppet);
        Assert.Equal(new Vector2(0.25f, 0), instance.GetParameterValue(puppet.Parameters[0]));
        Assert.Equal(new Vector2(0, 0.5f), instance.GetParameterValue(puppet.Parameters[1]));
        Assert.Equal(0, instance.Time);
    }

    [Fact]
    public void InstancesOfOnePuppetAreIndependent()
    {
        var puppet = TwoParameters();
        var head = puppet.FindParameter("Head")!;
        var a = puppet.CreateInstance();
        var b = puppet.CreateInstance();

        a.SetParameterValue(head, new Vector2(1, -1));
        a.Update(0.5f);
        b.Update(0.1f);

        Assert.Equal(new Vector2(1, -1), a.GetParameterValue(head));
        Assert.Equal(new Vector2(0, 0.5f), b.GetParameterValue(head));
        Assert.Equal(0.5, a.Time, 6);
        Assert.Equal(0.1, b.Time, 6);
    }

    [Fact]
    public void OneDimensionalParametersKeepTheirDefaultY()
    {
        var puppet = TwoParameters();
        var instance = puppet.CreateInstance();

        instance.SetParameterValue(puppet.Parameters[0], new Vector2(0.75f, 9));

        Assert.Equal(new Vector2(0.75f, 0), instance.GetParameterValue(puppet.Parameters[0]));
    }

    [Fact]
    public void ResetRestoresDefaults()
    {
        var puppet = TwoParameters();
        var instance = puppet.CreateInstance();
        instance.SetParameterValue(puppet.Parameters[1], Vector2.One);

        instance.ResetParameters();

        Assert.Equal(new Vector2(0, 0.5f), instance.GetParameterValue(puppet.Parameters[1]));
    }

    [Fact]
    public void ParametersOfAnotherPuppetAreRejected()
    {
        var instance = TwoParameters().CreateInstance();
        var foreign = TwoParameters().Parameters[0];

        Assert.Throws<ArgumentException>(() => instance.GetParameterValue(foreign));
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void UpdateRejectsBadTimeSteps(float delta)
    {
        var instance = TwoParameters().CreateInstance();

        Assert.Throws<ArgumentOutOfRangeException>(() => instance.Update(delta));
    }
}
