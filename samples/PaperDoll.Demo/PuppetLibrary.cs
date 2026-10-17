using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#if MONOGAME
using PaperDoll.MonoGame;
#else
using PaperDoll.Fna;
#endif
using PaperDoll.Snapshots;

namespace PaperDoll.Demo;

/// <summary>A puppet loaded once and shared by every slot that shows it, with its GPU textures.</summary>
internal sealed class LoadedPuppet(string path, string? samplesPath, Puppet puppet, PuppetTextures textures)
{
    /// <summary>Text to draw over the puppet, in puppet space (the blend test scene's cell names).</summary>
    public IReadOnlyList<(string Text, System.Numerics.Vector2 Position)> Labels { get; init; } = [];

    /// <summary>The full path of the file.</summary>
    public string Path { get; } = path;

    /// <summary>The path relative to the samples folder, or <see langword="null"/> if outside it (no snapshots then).</summary>
    public string? SamplesPath { get; } = samplesPath;

    public Puppet Puppet { get; } = puppet;

    /// <summary>The GPU textures; replaced when the atlas setting changes.</summary>
    public PuppetTextures Textures { get; set; } = textures;

    /// <summary>How many slots show this puppet.</summary>
    public int Users { get; set; }

    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>Loads each puppet file once, however many slots show it, and frees it when the last one goes.</summary>
internal sealed class PuppetLibrary(GraphicsDevice device, SnapshotStore? store) : IDisposable
{
    private readonly Dictionary<string, LoadedPuppet> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private bool _useAtlas = true;
    private float _textureScale = 1f;

    public IEnumerable<LoadedPuppet> Loaded => _loaded.Values;

    /// <summary>Whether textures are packed into atlas pages. Changing it re-uploads every loaded puppet's textures.</summary>
    public bool UseAtlas
    {
        get => _useAtlas;
        set
        {
            if (value == _useAtlas)
                return;
            _useAtlas = value;
            Reupload();
        }
    }

    /// <summary>The resolution textures are uploaded at (1, 0.5, 0.25 or 0.125). Changing it re-uploads every loaded puppet's textures.</summary>
    public float TextureScale
    {
        get => _textureScale;
        set
        {
            if (value == _textureScale)
                return;
            _textureScale = value;
            Reupload();
        }
    }

    private void Reupload()
    {
        foreach (var loaded in _loaded.Values)
        {
            loaded.Textures.Dispose();
            loaded.Textures = CreateTextures(loaded.Puppet);
        }
    }

    private PuppetTextures CreateTextures(Puppet puppet) => new(device, puppet, _useAtlas, _textureScale);

    /// <summary>Loads the file (or reuses it if loaded) and counts one more user.</summary>
    /// <exception cref="PuppetFormatException">The file is not a readable puppet.</exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    public LoadedPuppet Acquire(string path)
    {
        if (path == TestScene.Name)
            return AcquireTestScene();

        var full = System.IO.Path.GetFullPath(path);
        if (!_loaded.TryGetValue(full, out var loaded))
        {
            var puppet = Puppet.Load(full);
            loaded = new LoadedPuppet(full, store?.RelativePuppetPath(full), puppet, CreateTextures(puppet));
            _loaded.Add(full, loaded);
        }

        loaded.Users++;
        return loaded;
    }

    /// <summary>The blend test scene, built in memory the first time (<see cref="TestScene"/>).</summary>
    private LoadedPuppet AcquireTestScene()
    {
        if (!_loaded.TryGetValue(TestScene.Name, out var loaded))
        {
            var (file, labels) = TestScene.Build();
            var puppet = Puppet.Load(file);
            loaded = new LoadedPuppet(TestScene.Name, null, puppet, CreateTextures(puppet)) { Labels = labels };
            _loaded.Add(TestScene.Name, loaded);
        }

        loaded.Users++;
        return loaded;
    }

    /// <summary>Counts one user less, and frees the puppet's textures once nothing shows it.</summary>
    public void Release(LoadedPuppet loaded)
    {
        if (--loaded.Users > 0)
            return;
        _loaded.Remove(loaded.Path);
        loaded.Textures.Dispose();
    }

    public void Dispose()
    {
        foreach (var loaded in _loaded.Values)
            loaded.Textures.Dispose();
        _loaded.Clear();
    }
}

/// <summary>One puppet on screen: an instance of a loaded puppet with its own placement.</summary>
internal sealed class PuppetSlot
{
    public PuppetSlot(LoadedPuppet source, int copy)
    {
        Source = source;
        Copy = copy;
        Instance = source.Puppet.CreateInstance();
        Anchor = Instance.Bounds.Center;
    }

    private System.Numerics.Vector2 _fedPosition;
    private bool _fed;

    public LoadedPuppet Source { get; }

    public PuppetInstance Instance { get; }

    /// <summary>Which copy of the puppet this is (1 for the first slot showing it).</summary>
    public int Copy { get; }

    /// <summary>Where the centre of the puppet's rest bounds goes on screen, in pixels.</summary>
    public System.Numerics.Vector2 Position { get; set; }

    /// <summary>Screen pixels per puppet pixel.</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>Draw calls the renderer made for this puppet in the last frame.</summary>
    public int LastDrawCalls { get; set; }

    /// <summary>Milliseconds spent in the instance's updates since it was added (for the benchmark).</summary>
    public double UpdateMilliseconds { get; set; }

    /// <summary>The puppet-space point placed at <see cref="Position"/>: the centre of the bounds when the slot was created.</summary>
    public System.Numerics.Vector2 Anchor { get; }

    public string Label => Copy == 1 ? Source.DisplayName : $"{Source.DisplayName} #{Copy}";

    /// <summary>
    /// Tells the instance how far the slot moved on screen since the last frame, in puppet units, so dragging a puppet
    /// swings its physics. Only the movement is passed on, so changing the scale or placing a new slot swings nothing.
    /// </summary>
    public void FeedScenePosition()
    {
        if (_fed && Position != _fedPosition)
            Instance.ScenePosition += (Position - _fedPosition) / Scale;
        _fedPosition = Position;
        _fed = true;
    }

    /// <summary>Puppet space to screen pixels.</summary>
    public Matrix Transform =>
        Matrix.CreateTranslation(-Anchor.X, -Anchor.Y, 0) * Matrix.CreateScale(Scale, Scale, 1) * Matrix.CreateTranslation(Position.X, Position.Y, 0);

    /// <summary>The instance's current bounds on screen, at least a few pixels wide so an empty puppet can still be picked.</summary>
    public Rectangle Bounds
    {
        get
        {
            var b = Instance.Bounds;
            var min = (b.Min - Anchor) * Scale + Position;
            var max = (b.Max - Anchor) * Scale + Position;
            var width = Math.Max(16, (int)MathF.Ceiling(max.X - min.X));
            var height = Math.Max(16, (int)MathF.Ceiling(max.Y - min.Y));
            return b.IsEmpty
                ? new Rectangle((int)Position.X - 8, (int)Position.Y - 8, 16, 16)
                : new Rectangle((int)MathF.Floor(min.X), (int)MathF.Floor(min.Y), width, height);
        }
    }

    /// <summary>The scale that fits the puppet's rest bounds into a box of the given size.</summary>
    public float FitScale(float width, float height)
    {
        var size = Instance.Bounds.Size;
        return Instance.Bounds.IsEmpty ? 1f : Math.Min(width / size.X, height / size.Y);
    }
}
