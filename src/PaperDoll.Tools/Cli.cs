using PaperDoll.Snapshots;

namespace PaperDoll.Tools;

/// <summary>The <c>paperdoll</c> command line: <c>inspect</c>, <c>validate</c> and <c>snapshots</c>.</summary>
internal static class Cli
{
    public const int Ok = 0;
    public const int Failed = 1;
    public const int Usage = 2;

    private const string Help = """
        paperdoll - inspect and validate Inochi2D INP1 puppets (.inp, .inx)

        Usage:
          paperdoll inspect <file> [--section <name>]... [--depth <n>]
              Prints the puppet's metadata, node tree, parameters, animations, textures,
              extensions and load issues. Sections: meta, tree, params, anims, textures,
              ext, issues (default: all). --depth limits the tree's depth.

          paperdoll validate <file-or-folder>... [--no-decode]
              Loads each puppet (folders are searched for .inp and .inx files), decodes
              its textures, and reports what PaperDoll skipped, ignored or does not
              support yet. Exit code 1 if any puppet fails to load.

          paperdoll snapshots [<samples-folder>]
              Replays every snapshot saved in <samples-folder>/snapshots (default: .samples,
              or PAPERDOLL_SAMPLES) and compares it with what PaperDoll produces now.
              Snapshots are saved from the demo. Exit code 1 if any differs.

          paperdoll help
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length == 0 || args[0] is "help" or "-h" or "--help" or "-?")
        {
            output.WriteLine(Help);
            return args.Length == 0 ? Usage : Ok;
        }

        try
        {
            return args[0] switch
            {
                "inspect" => RunInspect(args[1..], output, error),
                "validate" => RunValidate(args[1..], output, error),
                "snapshots" => RunSnapshots(args[1..], output, error),
                _ => UsageError(error, $"Unknown command '{args[0]}'."),
            };
        }
        catch (UsageException ex)
        {
            return UsageError(error, ex.Message);
        }
    }

    private static int RunInspect(string[] args, TextWriter output, TextWriter error)
    {
        string? path = null;
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var depth = int.MaxValue;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--section":
                    var section = Next(args, ref i, "--section");
                    if (!Inspector.Sections.Contains(section, StringComparer.OrdinalIgnoreCase))
                        throw new UsageException($"Unknown section '{section}'. Sections: {string.Join(", ", Inspector.Sections)}.");
                    sections.Add(section);
                    break;
                case "--depth":
                    if (!int.TryParse(Next(args, ref i, "--depth"), out depth) || depth < 0)
                        throw new UsageException("--depth needs a whole number of 0 or more.");
                    break;
                default:
                    if (args[i].StartsWith('-') || path is not null)
                        throw new UsageException($"Unexpected argument '{args[i]}'.");
                    path = args[i];
                    break;
            }
        }

        if (path is null)
            throw new UsageException("inspect needs a puppet file.");
        if (sections.Count == 0)
            sections.UnionWith(Inspector.Sections);

        Puppet puppet;
        try
        {
            puppet = Puppet.Load(path);
        }
        catch (Exception ex) when (ex is PuppetFormatException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"{path}: {ex.Message}");
            return Failed;
        }

        new Inspector(output, depth).Write(puppet, path, sections);
        return Ok;
    }

    private static int RunValidate(string[] args, TextWriter output, TextWriter error)
    {
        var decode = true;
        var inputs = new List<string>();
        foreach (var arg in args)
        {
            if (arg == "--no-decode")
                decode = false;
            else if (arg.StartsWith('-'))
                throw new UsageException($"Unexpected argument '{arg}'.");
            else
                inputs.Add(arg);
        }

        if (inputs.Count == 0)
            throw new UsageException("validate needs at least one puppet file or folder.");

        var files = new List<string>();
        foreach (var input in inputs)
        {
            if (Directory.Exists(input))
            {
                var found = Directory.EnumerateFiles(input, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".inp", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".inx", StringComparison.OrdinalIgnoreCase))
                    .Order(StringComparer.Ordinal)
                    .ToList();
                if (found.Count == 0)
                    error.WriteLine($"{input}: no .inp or .inx files found.");
                files.AddRange(found);
            }
            else if (File.Exists(input))
            {
                files.Add(input);
            }
            else
            {
                error.WriteLine($"{input}: not found.");
                return Failed;
            }
        }

        var failures = 0;
        var validator = new Validator(output, decode);
        foreach (var file in files)
        {
            if (!validator.Validate(file))
                failures++;
        }

        if (files.Count > 1)
            output.WriteLine($"{files.Count - failures} of {files.Count} puppets loaded.");
        return failures == 0 ? Ok : Failed;
    }

    private static int RunSnapshots(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length > 1 || (args.Length == 1 && args[0].StartsWith('-')))
            throw new UsageException("snapshots takes at most one samples folder.");

        var root = args.Length == 1 ? args[0] : SampleLocator.FindSamplesRoot(Directory.GetCurrentDirectory());
        if (root is null || !Directory.Exists(root))
        {
            error.WriteLine(root is null ? "No .samples folder found; name one or set PAPERDOLL_SAMPLES." : $"{root}: not found.");
            return Failed;
        }

        var store = new SnapshotStore(root);
        IReadOnlyList<SnapshotResult> results;
        try
        {
            results = SnapshotCheck.Run(store, store.LoadAll());
        }
        catch (SnapshotFormatException ex)
        {
            error.WriteLine(ex.Message);
            return Failed;
        }

        if (results.Count == 0)
        {
            output.WriteLine($"No snapshots in {store.Root}.");
            return Ok;
        }

        foreach (var result in results)
        {
            output.WriteLine($"{(result.Passed ? "match  " : "DIFFERS")}  {result.Snapshot.PuppetPath} / {result.Snapshot.Name}");
            if (result.Error is not null)
                output.WriteLine($"    {result.Error}");
            foreach (var difference in result.Differences)
                output.WriteLine($"    {difference}");
        }

        var failed = results.Count(r => !r.Passed);
        output.WriteLine($"{results.Count - failed} of {results.Count} snapshots match.");
        return failed == 0 ? Ok : Failed;
    }

    private static string Next(string[] args, ref int i, string option)
    {
        if (i + 1 >= args.Length)
            throw new UsageException($"{option} needs a value.");
        return args[++i];
    }

    private static int UsageError(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine("Run 'paperdoll help' for usage.");
        return Usage;
    }

    private sealed class UsageException(string message) : Exception(message);
}
