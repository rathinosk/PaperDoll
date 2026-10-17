namespace PaperDoll;

/// <summary>The file cannot be read as an INP1 puppet.</summary>
public sealed class PuppetFormatException : Exception
{
    /// <summary>Creates the exception.</summary>
    public PuppetFormatException(string message) : base(message) { }

    /// <summary>Creates the exception with an inner exception.</summary>
    public PuppetFormatException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>What a load issue is about.</summary>
public enum LoadIssueKind
{
    /// <summary>A node of a type PaperDoll does not read was skipped, with its subtree.</summary>
    SkippedNode,
    /// <summary>A key known to be editor data (such as Mannequin Character Generator's) was ignored.</summary>
    IgnoredKey,
    /// <summary>A key PaperDoll does not recognise was ignored.</summary>
    UnknownKey,
    /// <summary>A feature present in the file is read but not supported.</summary>
    UnsupportedFeature,
    /// <summary>A value was out of range, of the wrong type or unknown, and a default was used.</summary>
    InvalidValue,
    /// <summary>A reference (node, parameter, texture or mask source) points at nothing; the referring item was dropped or kept unresolved.</summary>
    DanglingReference,
    /// <summary>Data was repaired while loading.</summary>
    Recovered,
}

/// <summary>Something the loader skipped, ignored or repaired. None of these stops a puppet from loading.</summary>
/// <param name="Kind">What the issue is about.</param>
/// <param name="Location">Where in the file: an object kind, node, parameter or section.</param>
/// <param name="Message">A readable description.</param>
/// <param name="Count">How many times it occurred (repeated issues are reported once).</param>
public sealed record LoadIssue(LoadIssueKind Kind, string Location, string Message, int Count = 1)
{
    /// <inheritdoc/>
    public override string ToString() => Count > 1 ? $"[{Kind}] {Location}: {Message} (x{Count})" : $"[{Kind}] {Location}: {Message}";
}
