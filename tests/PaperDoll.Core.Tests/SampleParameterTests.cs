using System.Numerics;
using PaperDoll.Evaluation;

namespace PaperDoll.Tests;

/// <summary>Parameters and bindings on the sample puppets (skipped when <c>.samples/</c> is absent).</summary>
public class SampleParameterTests
{
    public static TheoryData<string> Files => new()
    {
        "99days-samples/claire.inx", "99days-samples/keito.inx", "99days-samples/liam.inx", "99days-samples/rei.inx",
        "99days-samples/taylor.inx", "99days-samples/zeehua.inx", "inochi3d-samples/Aka.inx", "inochi3d-samples/Midori.inx",
        "mannequin-samples/Lena.inx", "mannequin-samples/Mina.inx",
        "inochi3d-samples/Yumi/Yumi.inx", "mannequin-samples/Bunny.inx", "mannequin-samples/Gladys.inx",
        "mannequin-samples/Katie.inx", "mannequin-samples/Ryan.inx",
    };

    /// <summary>Every parameter at its minimum, maximum and midpoint, alone, gives finite output.</summary>
    [Theory]
    [MemberData(nameof(Files))]
    public void EveryParameterEvaluatesAcrossItsRange(string file)
    {
        var puppet = Puppet.Load(SampleTests.SamplePath(file));
        var instance = puppet.CreateInstance();
        foreach (var parameter in puppet.Parameters)
        {
            foreach (var value in new[] { parameter.Min, parameter.Max, (parameter.Min + parameter.Max) / 2 })
            {
                instance.SetParameterValue(parameter, value);
                instance.Update(0);
                AssertFinite(instance, $"{parameter.Name} = {value}");
            }

            instance.SetParameterValue(parameter, parameter.Defaults);
        }
    }

    [Fact]
    public void ParametersMoveTheSample()
    {
        var puppet = Puppet.Load(SampleTests.SamplePath("inochi3d-samples/Midori.inx"));
        var instance = puppet.CreateInstance();
        var rest = instance.DrawList.Where(i => i.Kind == DrawItemKind.Part).ToDictionary(i => i.Node.Uuid, i => i.Positions.ToArray());

        var parameter = puppet.Parameters.First(p => p.Bindings.Any(b => b is DeformBinding));
        instance.SetParameterValue(parameter, parameter.Max);
        instance.Update(0);

        Assert.Contains(instance.DrawList, i => i.Kind == DrawItemKind.Part && !i.Positions.SequenceEqual(rest[i.Node.Uuid]));
    }

    internal static void AssertFinite(PuppetInstance instance, string context)
    {
        foreach (var item in instance.DrawList)
        {
            foreach (var p in item.Positions)
            {
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y))
                    Assert.Fail($"{context}: '{item.Node.Name}' has a non-finite vertex");
            }

            Assert.True(float.IsFinite(item.Opacity) && float.IsFinite(item.ZSort) && IsFinite(item.Tint), $"{context}: '{item.Node.Name}'");
        }
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
