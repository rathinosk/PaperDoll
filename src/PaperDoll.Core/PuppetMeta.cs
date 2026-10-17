// Puppet metadata (name, version, authors, copyright, usage rights, licence URL, contact, pixel-art filtering) and the
// puppet's physics settings (pixels per metre, gravity), as stored in INP1.

namespace PaperDoll;

/// <summary>Information about a puppet and its authors.</summary>
public sealed class PuppetMeta
{
    internal PuppetMeta() { }

    /// <summary>The puppet's name.</summary>
    public string? Name { get; internal set; }

    /// <summary>The version string of the tool that wrote the file.</summary>
    public string? Version { get; internal set; }

    /// <summary>Rigger(s).</summary>
    public string? Rigger { get; internal set; }

    /// <summary>Artist(s).</summary>
    public string? Artist { get; internal set; }

    /// <summary>Usage rights, if the file states them.</summary>
    public PuppetUsageRights? Rights { get; internal set; }

    /// <summary>Copyright notice.</summary>
    public string? Copyright { get; internal set; }

    /// <summary>URL of the licence.</summary>
    public string? LicenseUrl { get; internal set; }

    /// <summary>Contact information.</summary>
    public string? Contact { get; internal set; }

    /// <summary>Link to where the puppet comes from.</summary>
    public string? Reference { get; internal set; }

    /// <summary>The texture slot of the thumbnail, or <see langword="null"/> for none.</summary>
    public int? ThumbnailId { get; internal set; }

    /// <summary>Whether textures should be sampled with nearest-neighbour filtering (pixel art).</summary>
    public bool PreservePixels { get; internal set; }
}

/// <summary>Usage rights a puppet's author states. Strings are kept as written (for example <c>onlyAuthor</c>, <c>everyone</c>).</summary>
public sealed class PuppetUsageRights
{
    internal PuppetUsageRights() { }

    /// <summary>Who may use the puppet.</summary>
    public string? AllowedUsers { get; internal set; }

    /// <summary>Whether violent content is allowed.</summary>
    public bool AllowViolence { get; internal set; }

    /// <summary>Whether sexual content is allowed.</summary>
    public bool AllowSexual { get; internal set; }

    /// <summary>Whether commercial use is allowed.</summary>
    public bool AllowCommercial { get; internal set; }

    /// <summary>Redistribution terms.</summary>
    public string? AllowRedistribution { get; internal set; }

    /// <summary>Modification terms.</summary>
    public string? AllowModification { get; internal set; }

    /// <summary>Whether the authors must be credited.</summary>
    public bool RequireAttribution { get; internal set; }
}

/// <summary>The puppet's physics settings.</summary>
/// <param name="PixelsPerMeter">Puppet units per metre.</param>
/// <param name="Gravity">Gravity in metres per second squared.</param>
public sealed record PuppetPhysicsSettings(float PixelsPerMeter = 1000f, float Gravity = 9.8f);
