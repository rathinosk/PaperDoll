using System.Numerics;
using PaperDoll.Snapshots;

namespace PaperDoll.Tests;

public sealed class SnapshotTests : IDisposable
{
    private readonly string _samples = Path.Combine(Path.GetTempPath(), $"paperdoll-snapshots-{Guid.NewGuid():N}");
    private readonly SnapshotStore _store;

    public SnapshotTests()
    {
        Directory.CreateDirectory(Path.Combine(_samples, "set"));
        File.WriteAllBytes(Path.Combine(_samples, "set", "test.inx"), TestData.Inp(TestData.Puppet(children: """
            { "uuid": 2, "name": "Body", "type": "Part", "zsort": 1, "textures": [0],
              "mesh": { "verts": [0, 0, 10, 0, 0, 10], "uvs": [0, 0, 1, 0, 0, 1], "indices": [0, 1, 2] } },
            { "uuid": 3, "name": "Layer", "type": "Composite", "children": [
              { "uuid": 4, "name": "Face", "type": "Part", "textures": [0], "tint": [1, 0.5, 0.25],
                "transform": { "trans": [5, 5, 0], "rot": [0, 0, 0], "scale": [1, 1] },
                "mesh": { "verts": [0, 0, 4, 0, 0, 4], "uvs": [0, 0, 1, 0, 0, 1], "indices": [0, 1, 2] } } ] }
            """, parameters: """
            { "uuid": 50, "name": "Mouth", "min": [0, 0], "max": [1, 0], "defaults": [0.25, 0], "bindings": [] },
            { "uuid": 51, "name": "Head", "is_vec2": true, "min": [-1, -1], "max": [1, 1], "defaults": [0, 0.5], "bindings": [] }
            """, extraRoot: """
            , "animations": { "nod": { "timestep": 0.125, "additive": false, "length": 16, "leadIn": -1, "leadOut": -1, "lanes": [
                { "interpolation": "Linear", "uuid": 51, "target": 1, "merge_mode": "Forced",
                  "keyframes": [{ "frame": 0, "value": 0, "tension": 0.5 }, { "frame": 8, "value": -1, "tension": 0.5 }] }] } }
            """), [((byte)0, TestData.Png(2, 2, TestData.Pixels2x2))]));
        _store = new SnapshotStore(_samples);
    }

    public void Dispose() => Directory.Delete(_samples, recursive: true);

    private Snapshot CapturePose(string name = "pose")
    {
        var puppet = Puppet.Load(_store.PuppetFile("set/test.inx"));
        var instance = puppet.CreateInstance();
        instance.SetParameterValue(puppet.FindParameter("Head")!, new Vector2(0.75f, -0.125f));
        instance.Update(0.5f);
        return Snapshot.Capture(name, "set/test.inx", instance);
    }

    [Fact]
    public void SavedSnapshotReplaysToTheSameOutput()
    {
        _store.Save(CapturePose());

        var results = SnapshotCheck.Run(_store, _store.LoadAll());

        var result = Assert.Single(results);
        Assert.True(result.Passed, string.Join("; ", result.Differences));
        Assert.Equal("pose", result.Snapshot.Name);
    }

    [Fact]
    public void UnsettledPhysicsReplaysWithTheRecordedStep()
    {
        // A lightly damped pendulum whose holder the host moves: still swinging after 12 s, past the 10 s that one update covers.
        File.WriteAllBytes(Path.Combine(_samples, "set", "swing.inx"), TestData.Inp(TestData.Puppet(children: """
            { "uuid": 2, "name": "Holder", "type": "Node", "children": [
              { "uuid": 3, "name": "Phys", "type": "SimplePhysics", "param": 100, "model_type": "pendulum", "map_mode": "xy",
                "length": 100, "frequency": 1, "angle_damping": 0.01, "length_damping": 0.01, "output_scale": [1, 1] } ] }
            """, parameters: """
            { "uuid": 100, "name": "Swing", "is_vec2": true, "min": [-1, -1], "max": [1, 1], "defaults": [0, 0],
              "axis_points": [[0, 1], [0, 1]], "bindings": [] },
            { "uuid": 101, "name": "Move", "min": [0, 0], "max": [1, 0], "defaults": [0, 0], "axis_points": [[0, 1], [0]],
              "bindings": [ { "node": 2, "param_name": "transform.t.x", "values": [[0], [300]], "isSet": [[true], [true]] } ] }
            """)));
        var puppet = Puppet.Load(_store.PuppetFile("set/swing.inx"));
        var instance = puppet.CreateInstance();
        instance.SetParameterValue(puppet.FindParameter("Move")!, new Vector2(1, 0));
        const float step = 1f / 60;
        for (var i = 0; i < 720; i++)
            instance.Update(step);

        Assert.True(instance.GetParameterValue(puppet.FindParameter("Swing")!).Length() > 0.01f, $"not swinging: {instance.GetParameterValue(puppet.FindParameter("Swing")!)}");
        var stepped = Snapshot.Capture("swing", "set/swing.inx", instance, step);
        Assert.Equal(step, stepped.Step, 6);
        var replayed = Snapshot.Replay(stepped, puppet);
        Assert.Empty(SnapshotComparer.Compare(stepped, replayed, 1e-6f));

        // Replayed as one step (what format 5 did), only the first 10 s are simulated.
        var oneStep = Snapshot.Capture("swing", "set/swing.inx", instance);
        Assert.NotEmpty(SnapshotComparer.Compare(oneStep, Snapshot.Replay(oneStep, puppet), 1e-6f));

        var loaded = SnapshotStore.LoadFile(_store.Save(stepped));
        Assert.Equal(stepped.Step, loaded.Step);
    }

    [Fact]
    public void AnimatedPoseReplaysPausedAtItsFrame()
    {
        var puppet = Puppet.Load(_store.PuppetFile("set/test.inx"));
        var instance = puppet.CreateInstance();
        var nod = instance.PlayAnimation("nod", loop: true);
        nod.Strength = 0.5f;
        for (var i = 0; i < 3; i++)
            instance.Update(0.125f);
        var snapshot = Snapshot.Capture("nod", "set/test.inx", instance);

        Assert.Equal(new SnapshotAnimation("nod", 3, 0.5f), Assert.Single(snapshot.Animations));
        var head = snapshot.Parameters.Single(p => p.Name == "Head");
        Assert.Equal(0.5f + (-0.375f - 0.5f) * 0.5f, head.Y, 5); // blended halfway from the default to the lane
        Assert.Equal(0.5f, snapshot.Inputs.Single(p => p.Name == "Head").Y);

        var path = _store.Save(snapshot);
        Assert.Equal(snapshot.Animations, SnapshotStore.LoadFile(path).Animations);
        var result = Assert.Single(SnapshotCheck.Run(_store, _store.LoadAll()));
        Assert.True(result.Passed, string.Join("; ", result.Differences));
    }

    [Fact]
    public void FileRoundTripsEveryField()
    {
        var saved = CapturePose();
        var path = _store.Save(saved);

        Assert.Equal(Path.Combine(_samples, "snapshots", "set", "test", "pose.json"), path);
        var loaded = SnapshotStore.LoadFile(path);
        Assert.Equal(saved.Name, loaded.Name);
        Assert.Equal(saved.PuppetPath, loaded.PuppetPath);
        Assert.Equal(saved.Time, loaded.Time, 6);
        Assert.Equal(saved.Inputs, loaded.Inputs);
        Assert.Equal(saved.Parameters, loaded.Parameters);
        Assert.Equal(saved.Bounds, loaded.Bounds);
        Assert.Equal(saved.Drawables.Count, loaded.Drawables.Count);
        for (var i = 0; i < saved.Drawables.Count; i++)
        {
            var (want, got) = (saved.Drawables[i], loaded.Drawables[i]);
            Assert.Equal(want with { Positions = [], Masks = null }, got with { Positions = [], Masks = null });
            Assert.Equal(want.Positions, got.Positions);
            Assert.Equal(want.Masks, got.Masks);
        }

        Assert.Equal(saved.Layers.Count, loaded.Layers.Count);
        for (var i = 0; i < saved.Layers.Count; i++)
        {
            Assert.Equal(saved.Layers[i] with { Masks = [] }, loaded.Layers[i] with { Masks = [] });
            Assert.Equal(saved.Layers[i].Masks, loaded.Layers[i].Masks);
        }

        Assert.Equal(Snapshot.CurrentFormatVersion, loaded.FormatVersion);
    }

    [Fact]
    public void CaptureRecordsTheDrawList()
    {
        var snapshot = CapturePose();

        Assert.Equal([2u, 4], snapshot.Drawables.Select(d => d.Uuid));
        var face = snapshot.Drawables[1];
        Assert.Equal(3u, face.Layer);
        Assert.Equal(0, face.Texture);
        Assert.Equal(new Vector3(1, 0.5f, 0.25f), face.Tint);
        Assert.Equal([5f, 5, 9, 5, 5, 9], face.Positions);
        Assert.Equal(new PuppetBounds(Vector2.Zero, new Vector2(10, 10)), snapshot.Bounds);
        var layer = Assert.Single(snapshot.Layers);
        Assert.Equal(3u, layer.Uuid);
        Assert.Null(layer.Layer);
        Assert.Equal(1f, layer.Opacity);
    }

    [Fact]
    public void ComparerReportsDrawListChanges()
    {
        var expected = CapturePose();
        var moved = expected.Drawables.ToArray();
        moved[1] = moved[1] with { Positions = [5f, 5, 9.5f, 5, 5, 9], Opacity = 0.5f };
        var actual = new Snapshot(expected.Name, expected.PuppetPath, expected.Time, expected.Inputs, expected.Parameters, moved, expected.Bounds,
            layers: expected.Layers);
        var reordered = new Snapshot(expected.Name, expected.PuppetPath, expected.Time, expected.Inputs, expected.Parameters,
            [.. expected.Drawables.Reverse()], expected.Bounds, layers: expected.Layers);

        var differences = SnapshotComparer.Compare(expected, actual);

        Assert.Equal(2, differences.Count);
        Assert.Contains(differences, d => d.StartsWith("drawable 1 'Face' (4): opacity expected 1, got 0.5", StringComparison.Ordinal));
        Assert.Contains(differences, d => d.StartsWith("drawable 1 'Face' (4): 1 vertex coordinate(s) moved", StringComparison.Ordinal));
        Assert.StartsWith("draw order:", Assert.Single(SnapshotComparer.Compare(expected, reordered)), StringComparison.Ordinal);
    }

    [Fact]
    public void ComparerReportsLayerAndMaskChanges()
    {
        var expected = CapturePose();
        var drawables = expected.Drawables.ToArray();
        drawables[0] = drawables[0] with { Masks = [new SnapshotMask(4, "Mask")] };
        var layers = expected.Layers.ToArray();
        layers[0] = layers[0] with { Opacity = 0.25f, BlendMode = "Multiply" };
        var actual = new Snapshot(expected.Name, expected.PuppetPath, expected.Time, expected.Inputs, expected.Parameters, drawables,
            expected.Bounds, layers: layers);

        var differences = SnapshotComparer.Compare(expected, actual);

        Assert.Equal(3, differences.Count);
        Assert.Contains(differences, d => d.StartsWith("drawable 0 'Body' (2): masks expected [], got [4 Mask]", StringComparison.Ordinal));
        Assert.Contains(differences, d => d.StartsWith("layer 0 'Layer' (3): opacity expected 1, got 0.25", StringComparison.Ordinal));
        Assert.Contains(differences, d => d.StartsWith("layer 0 'Layer' (3): blend mode expected Normal, got Multiply", StringComparison.Ordinal));
    }

    [Fact]
    public void FormatVersionOneAsksToBeSavedAgain()
    {
        var expected = CapturePose();
        var old = new Snapshot(expected.Name, expected.PuppetPath, expected.Time, expected.Inputs, expected.Parameters, formatVersion: 1);

        var difference = Assert.Single(SnapshotComparer.Compare(old, expected));
        Assert.Contains("save it again", difference, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparerReportsEachDifference()
    {
        var expected = CapturePose();
        var changed = expected.Parameters.ToArray();
        changed[1] = changed[1] with { X = changed[1].X + 0.01f };
        var actual = new Snapshot(expected.Name, expected.PuppetPath, expected.Time, expected.Inputs, changed[1..], expected.Drawables, expected.Bounds,
            layers: expected.Layers);

        var differences = SnapshotComparer.Compare(expected, actual);

        Assert.Equal(2, differences.Count);
        Assert.Contains(differences, d => d.StartsWith("parameter 'Mouth' (50): missing", StringComparison.Ordinal));
        Assert.Contains(differences, d => d.StartsWith("parameter 'Head' (51): expected (0.75, -0.125)", StringComparison.Ordinal));
    }

    [Fact]
    public void ComparerAcceptsDifferencesWithinTolerance()
    {
        var expected = CapturePose();
        var nudged = expected.Parameters.Select(p => p with { X = p.X + SnapshotComparer.DefaultTolerance / 2 }).ToList();
        var actual = new Snapshot(expected.Name, expected.PuppetPath, expected.Time, expected.Inputs, nudged, expected.Drawables, expected.Bounds,
            layers: expected.Layers);

        Assert.Empty(SnapshotComparer.Compare(expected, actual));
    }

    [Fact]
    public void MissingPuppetIsReportedNotThrown()
    {
        _store.Save(CapturePose());
        File.Delete(_store.PuppetFile("set/test.inx"));

        var result = Assert.Single(SnapshotCheck.Run(_store, _store.LoadAll()));

        Assert.False(result.Passed);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void LoadForListsOnlyThatPuppet()
    {
        _store.Save(CapturePose("b"));
        _store.Save(CapturePose("a"));

        Assert.Equal(["a", "b"], _store.LoadFor("set/test.inx").Select(s => s.Name));
        Assert.Empty(_store.LoadFor("set/other.inx"));
    }

    [Fact]
    public void PuppetsOutsideTheSamplesFolderHaveNoRelativePath()
    {
        Assert.Equal("set/test.inx", _store.RelativePuppetPath(Path.Combine(_samples, "set", "test.inx")));
        Assert.Null(_store.RelativePuppetPath(Path.Combine(Path.GetTempPath(), "elsewhere.inx")));
        Assert.Null(_store.RelativePuppetPath(Path.Combine(_samples, "snapshots", "x.inx")));
    }

    [Theory]
    [InlineData("rest", true)]
    [InlineData("mouth-open_1.5", true)]
    [InlineData("", false)]
    [InlineData(".hidden", false)]
    [InlineData("../escape", false)]
    [InlineData("a b", false)]
    public void NamesAreSafeFileNames(string name, bool valid) => Assert.Equal(valid, Snapshot.IsValidName(name));

    [Fact]
    public void NewerFormatIsRejected()
    {
        var path = _store.Save(CapturePose());
        File.WriteAllText(path, File.ReadAllText(path).Replace($"\"formatVersion\": {Snapshot.CurrentFormatVersion}", "\"formatVersion\": 99", StringComparison.Ordinal));

        var ex = Assert.Throws<SnapshotFormatException>(() => SnapshotStore.LoadFile(path));
        Assert.Contains("newer", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedFileIsRejected()
    {
        var path = _store.Save(CapturePose());
        File.WriteAllText(path, """{ "formatVersion": 1, "name": "pose" }""");

        Assert.Throws<SnapshotFormatException>(() => SnapshotStore.LoadFile(path));
    }

    /// <summary>
    /// The regression check: every snapshot the author accepted in the demo (in <c>.samples/snapshots/</c>) must still match.
    /// Skips when there are no samples or no snapshots.
    /// </summary>
    [Fact]
    public void AcceptedSnapshotsStillMatch()
    {
        var root = SampleLocator.FindSamplesRoot();
        if (root is null)
            Assert.Skip("No .samples/ folder.");
        var store = new SnapshotStore(root);
        var snapshots = store.LoadAll();
        if (snapshots.Count == 0)
            Assert.Skip("No snapshots saved in .samples/snapshots/ yet.");

        var failures = SnapshotCheck.Run(store, snapshots)
            .Where(r => !r.Passed)
            .Select(r => $"{r.Snapshot.PuppetPath} / {r.Snapshot.Name}: {r.Error ?? string.Join("; ", r.Differences.Take(10))}")
            .ToList();

        Assert.True(failures.Count == 0, $"{failures.Count} of {snapshots.Count} snapshots differ:\n{string.Join("\n", failures)}");
    }
}
