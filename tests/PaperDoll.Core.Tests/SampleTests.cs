using PaperDoll.Snapshots;

namespace PaperDoll.Tests;

/// <summary>
/// Loads the sample puppets in <c>.samples/</c> (git-ignored, never tracked). Each test skips when its sample is absent.
/// Set <c>PAPERDOLL_SAMPLES</c> to use a different folder. Expected counts are what each file holds.
/// </summary>
public class SampleTests
{
    public static TheoryData<string, int, int, int, int, int, int, int, int> Samples => new()
    {
        // file, Part, Composite, MeshGroup, SimplePhysics, Node (with root), parameters, textures, animations
        { "99days-samples/claire.inx", 90, 8, 2, 33, 20, 45, 81, 0 },
        { "99days-samples/keito.inx", 113, 7, 2, 33, 20, 38, 105, 0 },
        { "99days-samples/liam.inx", 100, 8, 2, 33, 20, 46, 89, 0 },
        { "99days-samples/rei.inx", 161, 7, 2, 33, 20, 42, 149, 0 },
        { "99days-samples/taylor.inx", 178, 7, 2, 33, 20, 64, 137, 0 },
        { "99days-samples/zeehua.inx", 90, 7, 2, 33, 20, 35, 79, 0 },
        { "inochi3d-samples/Aka.inx", 76, 3, 0, 19, 2, 34, 76, 0 },
        { "inochi3d-samples/Midori.inx", 81, 3, 8, 20, 14, 35, 81, 0 },
        // Mannequin Character Generator's own samples (same licence as the 99Days puppets).
        { "mannequin-samples/Lena.inx", 189, 8, 2, 33, 20, 74, 166, 0 },
        { "mannequin-samples/Mina.inx", 225, 8, 2, 33, 20, 75, 188, 0 },
        // Inochi2D's static example, and four newer Mannequin Character Generator samples,
        // which carry seven animations each (sweat and tear drops) and write poseID on parameters too.
        { "inochi3d-samples/ada-static.inx", 1, 0, 0, 0, 1, 0, 1, 0 },
        { "mannequin-samples/Bunny.inx", 405, 8, 2, 49, 45, 111, 294, 7 },
        { "mannequin-samples/Gladys.inx", 678, 8, 2, 49, 46, 135, 678, 7 },
        { "mannequin-samples/Katie.inx", 330, 8, 2, 49, 45, 119, 274, 7 },
        { "mannequin-samples/Ryan.inx", 441, 8, 2, 41, 33, 99, 331, 7 },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void SampleLoadsWithTheAuditedContents(string file, int parts, int composites, int meshGroups, int physics, int groups, int parameters, int textures, int animations)
    {
        var puppet = Puppet.Load(SamplePath(file));

        Assert.Equal(parts, puppet.Nodes.Count(n => n.Kind == NodeKind.Part));
        Assert.Equal(composites, puppet.Nodes.Count(n => n.Kind == NodeKind.Composite));
        Assert.Equal(meshGroups, puppet.Nodes.Count(n => n.Kind == NodeKind.MeshGroup));
        Assert.Equal(physics, puppet.Nodes.Count(n => n.Kind == NodeKind.SimplePhysics));
        Assert.Equal(groups, puppet.Nodes.Count(n => n.Kind == NodeKind.Node));
        Assert.Equal(parameters, puppet.Parameters.Count);
        Assert.Equal(textures, puppet.Textures.Count);
        Assert.Equal(animations, puppet.Animations.Count);
        Assert.DoesNotContain(puppet.Issues, i => i.Kind == LoadIssueKind.UnknownKey);

        var isMcg = !file.StartsWith("inochi3d", StringComparison.Ordinal);
        Assert.Equal(isMcg ? 1 : 0, puppet.Issues.Count(i => i.Kind == LoadIssueKind.SkippedNode && i.Location.StartsWith("Camera", StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void EverySampleTextureDecodes(string file, int parts, int composites, int meshGroups, int physics, int groups, int parameters, int textures, int animations)
    {
        _ = (parts, composites, meshGroups, physics, groups, parameters, animations);
        var puppet = Puppet.Load(SamplePath(file));

        var decoded = 0;
        foreach (var texture in puppet.Textures)
        {
            Assert.True(texture.CanDecode, $"texture {texture.Index}");
            var image = texture.Decode();
            Assert.Equal((texture.Width, texture.Height), (image.Width, image.Height));
            Assert.Equal(image.Width * image.Height * 4, image.Rgba.Length);
            AssertPremultiplied(image, texture.Index);
            decoded++;
        }

        Assert.Equal(textures, decoded);
    }

    [Fact]
    public void SamePuppetRunsAsSeveralInstances()
    {
        var puppet = Puppet.Load(SamplePath("99days-samples/claire.inx"));
        var instances = Enumerable.Range(0, 5).Select(_ => puppet.CreateInstance()).ToList();
        var parameter = puppet.Parameters[0];

        for (var i = 0; i < instances.Count; i++)
        {
            instances[i].SetParameterValue(parameter, parameter.Max * (i / 4f));
            instances[i].Update(i * 0.01f);
        }

        for (var i = 0; i < instances.Count; i++)
        {
            Assert.Equal(parameter.Max.X * (i / 4f), instances[i].GetParameterValue(parameter).X, 5);
            Assert.Equal(i * 0.01, instances[i].Time, 5);
        }
    }

    internal static string SamplePath(string relative)
    {
        var root = SampleRoot();
        var path = root is null ? null : Path.Combine(root, relative);
        if (path is null || !File.Exists(path))
            Assert.Skip($"Sample puppet '{relative}' is not present in .samples/.");
        return path;
    }

    private static string? SampleRoot() => SampleLocator.FindSamplesRoot();

    /// <summary>INP1 textures are stored premultiplied (Inochi Creator premultiplies on import): no colour channel exceeds alpha.</summary>
    private static void AssertPremultiplied(TextureImage image, int index)
    {
        var rgba = image.Rgba;
        for (var i = 0; i < rgba.Length; i += 4)
        {
            var a = rgba[i + 3];
            if (rgba[i] > a || rgba[i + 1] > a || rgba[i + 2] > a)
                Assert.Fail($"texture {index}: pixel {i / 4} is ({rgba[i]}, {rgba[i + 1]}, {rgba[i + 2]}, {a}), not premultiplied");
        }
    }
}
