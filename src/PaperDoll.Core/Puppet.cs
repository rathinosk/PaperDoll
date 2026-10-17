using System.Diagnostics.CodeAnalysis;
using PaperDoll.Evaluation;
using PaperDoll.Format;

namespace PaperDoll;

/// <summary>
/// A loaded puppet: its node tree, textures, parameters, animations and extension data. Immutable once loaded,
/// so it can be shared between any number of <see cref="PuppetInstance"/>s and read from several threads.
/// </summary>
public sealed class Puppet
{
    private readonly Dictionary<uint, Node> _nodesByUuid;
    private readonly Dictionary<uint, Parameter> _parametersByUuid;
    private readonly Lazy<MeshGroupPlan> _meshGroups;

    internal Puppet(
        PuppetMeta meta,
        PuppetPhysicsSettings physics,
        Node root,
        IReadOnlyList<Node> nodes,
        Dictionary<uint, Node> nodesByUuid,
        IReadOnlyList<Parameter> parameters,
        Dictionary<uint, Parameter> parametersByUuid,
        IReadOnlyList<ParameterGroup> parameterGroups,
        IReadOnlyDictionary<string, Animation> animations,
        IReadOnlyList<PuppetTexture> textures,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> extensions,
        IReadOnlyList<LoadIssue> issues)
    {
        Meta = meta;
        Physics = physics;
        Root = root;
        Nodes = nodes;
        _nodesByUuid = nodesByUuid;
        Parameters = parameters;
        _parametersByUuid = parametersByUuid;
        ParameterGroups = parameterGroups;
        Animations = animations;
        Textures = textures;
        Extensions = extensions;
        Issues = issues;
        _meshGroups = new Lazy<MeshGroupPlan>(() => new MeshGroupPlan(this));
    }

    /// <summary>Information about the puppet and its authors.</summary>
    public PuppetMeta Meta { get; }

    /// <summary>The puppet's physics settings.</summary>
    public PuppetPhysicsSettings Physics { get; }

    /// <summary>The root of the node tree. Always a plain <see cref="Node"/> named <c>Root</c>, whatever the file says.</summary>
    public Node Root { get; }

    /// <summary>Every node in the tree, depth first, root first.</summary>
    public IReadOnlyList<Node> Nodes { get; }

    /// <summary>The parameters, in file order.</summary>
    public IReadOnlyList<Parameter> Parameters { get; }

    /// <summary>Parameter groups, for editors and tools.</summary>
    public IReadOnlyList<ParameterGroup> ParameterGroups { get; }

    /// <summary>The animations, by name.</summary>
    public IReadOnlyDictionary<string, Animation> Animations { get; }

    /// <summary>The texture slots, in file order.</summary>
    public IReadOnlyList<PuppetTexture> Textures { get; }

    /// <summary>Extension sections (<c>EXT_SECT</c>) by name, as raw bytes. PaperDoll does not interpret them.</summary>
    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Extensions { get; }

    /// <summary>What the loader skipped, ignored or repaired.</summary>
    public IReadOnlyList<LoadIssue> Issues { get; }

    /// <summary>Which MeshGroup deforms which node, and the MeshGroups' triangle lookups; built on first use, shared by every instance.</summary>
    internal MeshGroupPlan MeshGroups => _meshGroups.Value;

    /// <summary>Finds a node by ID.</summary>
    public bool TryGetNode(uint uuid, [NotNullWhen(true)] out Node? node) => _nodesByUuid.TryGetValue(uuid, out node);

    /// <summary>Finds a parameter by ID.</summary>
    public bool TryGetParameter(uint uuid, [NotNullWhen(true)] out Parameter? parameter) => _parametersByUuid.TryGetValue(uuid, out parameter);

    /// <summary>Finds the first parameter with the given name, or <see langword="null"/>.</summary>
    public Parameter? FindParameter(string name)
    {
        foreach (var parameter in Parameters)
        {
            if (parameter.Name == name)
                return parameter;
        }

        return null;
    }

    /// <summary>Creates an instance: one copy of the puppet on screen, with its own state.</summary>
    public PuppetInstance CreateInstance() => new(this);

    /// <summary>Loads a puppet from an <c>.inp</c> or <c>.inx</c> file.</summary>
    /// <exception cref="PuppetFormatException">The file is not a readable INP1 puppet (including INP2 files).</exception>
    public static Puppet Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return Load(File.ReadAllBytes(path));
    }

    /// <summary>Loads a puppet from a stream. The stream is read to its end.</summary>
    /// <exception cref="PuppetFormatException">The data is not a readable INP1 puppet (including INP2 data).</exception>
    public static Puppet Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Load(buffer.ToArray());
    }

    /// <summary>Loads a puppet from memory. Texture and extension data refer to <paramref name="data"/>, which must not change afterwards.</summary>
    /// <exception cref="PuppetFormatException">The data is not a readable INP1 puppet (including INP2 data).</exception>
    public static Puppet Load(ReadOnlyMemory<byte> data) => PuppetReader.Read(data);
}
