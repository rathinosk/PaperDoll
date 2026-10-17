namespace PaperDoll.Snapshots;

/// <summary>The result of replaying one saved snapshot.</summary>
/// <param name="Snapshot">The saved snapshot.</param>
/// <param name="Differences">What differs from the replay; empty when it matches.</param>
/// <param name="Error">Why it could not be replayed (puppet missing or unreadable), or <see langword="null"/>.</param>
public sealed record SnapshotResult(Snapshot Snapshot, IReadOnlyList<string> Differences, string? Error)
{
    /// <summary>Whether the replay matched the saved snapshot.</summary>
    public bool Passed => Error is null && Differences.Count == 0;
}

/// <summary>Replays saved snapshots and compares them with what Core produces now.</summary>
public static class SnapshotCheck
{
    /// <summary>Replays every snapshot in <paramref name="snapshots"/>, loading each puppet once.</summary>
    public static IReadOnlyList<SnapshotResult> Run(SnapshotStore store, IEnumerable<Snapshot> snapshots, float tolerance = SnapshotComparer.DefaultTolerance)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(snapshots);

        var puppets = new Dictionary<string, (Puppet? Puppet, string? Error)>(StringComparer.Ordinal);
        var results = new List<SnapshotResult>();
        foreach (var snapshot in snapshots)
        {
            if (!puppets.TryGetValue(snapshot.PuppetPath, out var loaded))
            {
                loaded = LoadPuppet(store.PuppetFile(snapshot.PuppetPath));
                puppets.Add(snapshot.PuppetPath, loaded);
            }

            results.Add(loaded.Puppet is null
                ? new SnapshotResult(snapshot, [], loaded.Error)
                : new SnapshotResult(snapshot, SnapshotComparer.Compare(snapshot, Snapshot.Replay(snapshot, loaded.Puppet), tolerance), null));
        }

        return results;
    }

    private static (Puppet?, string?) LoadPuppet(string path)
    {
        try
        {
            return (Puppet.Load(path), null);
        }
        catch (Exception ex) when (ex is PuppetFormatException or IOException or UnauthorizedAccessException)
        {
            return (null, $"{path}: {ex.Message}");
        }
    }
}
