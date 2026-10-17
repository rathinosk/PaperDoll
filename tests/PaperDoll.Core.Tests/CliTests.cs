using PaperDoll.Tools;

namespace PaperDoll.Tests;

public sealed class CliTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"paperdoll-cli-{Guid.NewGuid():N}");

    public CliTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(string name, byte[] data)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static (int Code, string Out, string Err) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = Cli.Run(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    private string TestPuppet() => Write("test.inx", TestData.Inp(TestData.Puppet("""
        { "uuid": 2, "name": "Default Camera", "type": "Camera" },
        { "uuid": 3, "name": "Face", "type": "Part", "textures": [0], "poseID": null,
          "mesh": { "verts": [0, 0, 1, 0, 0, 1], "uvs": [0, 0, 1, 0, 0, 1], "indices": [0, 1, 2] } }
        """, """
        { "uuid": 50, "name": "Smile", "axis_points": [[0, 1], [0]],
          "bindings": [{ "node": 3, "param_name": "opacity", "values": [[0], [1]], "isSet": [[true], [true]] }] }
        """), [(0, TestData.Png(2, 2, TestData.Pixels2x2))], [("com.example.ext", [1, 2])]));

    [Fact]
    public void InspectPrintsEverySection()
    {
        var (code, output, _) = Run("inspect", TestPuppet());

        Assert.Equal(Cli.Ok, code);
        Assert.Contains("Part 'Face' #3", output, StringComparison.Ordinal);
        Assert.Contains("'Smile' #50", output, StringComparison.Ordinal);
        Assert.Contains("opacity", output, StringComparison.Ordinal);
        Assert.Contains("Png  2x2", output, StringComparison.Ordinal);
        Assert.Contains("com.example.ext (2 bytes)", output, StringComparison.Ordinal);
        Assert.Contains("[SkippedNode] Camera 'Default Camera'", output, StringComparison.Ordinal);
    }

    [Fact]
    public void InspectCanLimitSections()
    {
        var (code, output, _) = Run("inspect", TestPuppet(), "--section", "ext");

        Assert.Equal(Cli.Ok, code);
        Assert.Contains("com.example.ext", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Node tree", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReportsSkippedAndIgnoredItems()
    {
        TestPuppet();

        var (code, output, _) = Run("validate", _folder);

        Assert.Equal(Cli.Ok, code);
        Assert.Contains("textures: 1 decoded", output, StringComparison.Ordinal);
        Assert.Contains("[SkippedNode]", output, StringComparison.Ordinal);
        Assert.Contains("editor key 'poseID'", output, StringComparison.Ordinal);
        Assert.DoesNotContain("'opacity' binding", output, StringComparison.Ordinal); // evaluated, so not listed
    }

    [Fact]
    public void ValidateFailsOnUnloadablePuppets()
    {
        Write("new.inp", "TRNSRTS2"u8.ToArray());

        var (code, output, _) = Run("validate", _folder);

        Assert.Equal(Cli.Failed, code);
        Assert.Contains("INP2", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotsReportsMatchesAndDifferences()
    {
        var puppet = Puppet.Load(TestPuppet());
        var store = new Snapshots.SnapshotStore(_folder);
        var instance = puppet.CreateInstance();
        store.Save(Snapshots.Snapshot.Capture("rest", "test.inx", instance));
        var changed = Snapshots.Snapshot.Capture("changed", "test.inx", instance);
        store.Save(new Snapshots.Snapshot(changed.Name, changed.PuppetPath, changed.Time, changed.Inputs,
            [changed.Parameters[0] with { X = 0.5f }]));

        var (code, output, _) = Run("snapshots", _folder);

        Assert.Equal(Cli.Failed, code);
        Assert.Contains("match    test.inx / rest", output, StringComparison.Ordinal);
        Assert.Contains("DIFFERS  test.inx / changed", output, StringComparison.Ordinal);
        Assert.Contains("1 of 2 snapshots match.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void SnapshotsWithNoneSavedSucceeds()
    {
        var (code, output, _) = Run("snapshots", _folder);

        Assert.Equal(Cli.Ok, code);
        Assert.Contains("No snapshots", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData]
    [InlineData("frobnicate")]
    [InlineData("snapshots", "a", "b")]
    [InlineData("inspect")]
    [InlineData("inspect", "a.inx", "--section", "nope")]
    [InlineData("validate")]
    public void BadUsageReturnsUsageCode(params string[] args)
    {
        Assert.Equal(Cli.Usage, Run(args).Code);
    }
}
