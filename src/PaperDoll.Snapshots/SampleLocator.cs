namespace PaperDoll.Snapshots;

/// <summary>Finds the sample puppets folder (<c>.samples/</c>, git-ignored), which also holds the snapshots.</summary>
public static class SampleLocator
{
    /// <summary>The environment variable that points at a samples folder elsewhere.</summary>
    public const string EnvironmentVariable = "PAPERDOLL_SAMPLES";

    /// <summary>
    /// The folder named by <c>PAPERDOLL_SAMPLES</c> if set; otherwise the first <c>.samples</c> folder found walking up from
    /// <paramref name="start"/> (default: the application's folder). <see langword="null"/> if there is none.
    /// </summary>
    public static string? FindSamplesRoot(string? start = null)
    {
        var configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrEmpty(configured))
            return Directory.Exists(configured) ? Path.GetFullPath(configured) : null;

        for (var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".samples");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>Every <c>.inp</c> and <c>.inx</c> file under <paramref name="folder"/>, ordered by path.</summary>
    public static IReadOnlyList<string> FindPuppets(string folder) =>
        !Directory.Exists(folder)
            ? []
            : Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(IsPuppetFile)
                .Order(StringComparer.Ordinal)
                .ToList();

    /// <summary>Whether the file name ends in <c>.inp</c> or <c>.inx</c>.</summary>
    public static bool IsPuppetFile(string path) =>
        path.EndsWith(".inp", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".inx", StringComparison.OrdinalIgnoreCase);
}
