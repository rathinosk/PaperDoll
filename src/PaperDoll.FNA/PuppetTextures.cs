using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

#if MONOGAME
namespace PaperDoll.MonoGame;
#else
namespace PaperDoll.Fna;
#endif

/// <summary>
/// Where a puppet texture slot is on the GPU: a texture, how to map the slot's UVs into it, and the rectangle of it the
/// slot covers, outside which the slot reads transparent.
/// </summary>
/// <param name="Texture">The GPU texture: an atlas page, or the slot's own texture.</param>
/// <param name="UvOffset">Added to the scaled UV.</param>
/// <param name="UvScale">Multiplies the slot's UV.</param>
/// <param name="ClipMin">The top-left corner, in <see cref="Texture"/>'s UVs, of the area the slot covers.</param>
/// <param name="ClipMax">The bottom-right corner, in <see cref="Texture"/>'s UVs, of the area the slot covers.</param>
public readonly record struct TextureRegion(Texture2D Texture, Vector2 UvOffset, Vector2 UvScale, Vector2 ClipMin, Vector2 ClipMax)
{
    /// <summary>Maps a UV in the slot's own texture to <see cref="Texture"/>.</summary>
    public Vector2 Map(Vector2 uv) => uv * UvScale + UvOffset;
}

/// <summary>
/// A puppet's textures on the GPU. Like the <see cref="Puppet"/> it belongs to, it is shared by every instance of that
/// puppet: create one per loaded puppet, not per instance. Nothing is uploaded until a slot is asked for; then every slot
/// is decoded (in parallel) and uploaded at once.
/// </summary>
/// <remarks>
/// <para>
/// Each texture is cropped to its visible texels before upload, and outside that area (and outside 0 to 1) it reads
/// transparent, as with clamp-to-border and a transparent border: <see cref="TextureRegion.ClipMin"/> and
/// <see cref="TextureRegion.ClipMax"/> mark the area, and <see cref="PuppetRenderer"/> clips to it. With
/// <see cref="UsesAtlas"/> on (the default), the cropped textures are packed into a few atlas pages, so the renderer can
/// draw many parts in one call; otherwise each slot is its own texture. Both give the same picture. Every texture has
/// mipmaps.
/// </para>
/// <para>
/// Pixels are uploaded as stored. INP1 textures are premultiplied (Inochi Creator premultiplies on import, and they are
/// drawn with premultiplied blending), so draw them with <see cref="BlendState.AlphaBlend"/>.
/// </para>
/// </remarks>
public sealed class PuppetTextures : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly TextureRegion?[] _regions;
    private readonly string?[] _errors;
    private readonly List<Texture2D> _textures = [];
    private bool _loaded;
    private bool _disposed;

    /// <summary>Creates the texture set. Nothing is uploaded until a slot is asked for (or <see cref="LoadAll"/> is called).</summary>
    /// <param name="device">The graphics device.</param>
    /// <param name="puppet">The puppet whose textures these are.</param>
    /// <param name="useAtlas">Whether to pack the textures into atlas pages (fewer draw calls) or upload each on its own.</param>
    /// <param name="textureScale">
    /// The resolution to upload at: 1 (full size, the default), 0.5, 0.25 or 0.125. Half size takes a quarter of the GPU
    /// memory and looks the same wherever the puppet is drawn at half its size or less.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="textureScale"/> is not one of the allowed values.</exception>
    public PuppetTextures(GraphicsDevice device, Puppet puppet, bool useAtlas = true, float textureScale = 1f)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(puppet);
        if (textureScale is not (1f or 0.5f or 0.25f or 0.125f))
            throw new ArgumentOutOfRangeException(nameof(textureScale), textureScale, "The texture scale must be 1, 0.5, 0.25 or 0.125.");
        _device = device;
        Puppet = puppet;
        UsesAtlas = useAtlas;
        TextureScale = textureScale;
        _regions = new TextureRegion?[puppet.Textures.Count];
        _errors = new string?[puppet.Textures.Count];
    }

    /// <summary>The puppet the textures belong to.</summary>
    public Puppet Puppet { get; }

    /// <summary>The number of texture slots.</summary>
    public int Count => _regions.Length;

    /// <summary>Whether the textures are packed into atlas pages.</summary>
    public bool UsesAtlas { get; }

    /// <summary>The resolution the textures are uploaded at, relative to the puppet's: 1, 0.5, 0.25 or 0.125.</summary>
    public float TextureScale { get; }

    /// <summary>The GPU textures created so far: atlas pages, or one per decodable slot.</summary>
    public IReadOnlyList<Texture2D> GpuTextures => _textures;

    /// <summary>
    /// The sampler the puppet asks for: point filtering if <see cref="PuppetMeta.PreservePixels"/> is set, otherwise
    /// linear with mipmaps.
    /// </summary>
    public SamplerState Sampler => Puppet.Meta.PreservePixels ? SamplerState.PointClamp : SamplerState.LinearClamp;

    /// <summary>
    /// Where a slot is on the GPU, uploading every texture on first use; <see langword="null"/> if the slot is out of range,
    /// empty, in an encoding Core does not decode (see <see cref="PuppetTexture.CanDecode"/>), or corrupt (see <see cref="ErrorOf"/>).
    /// </summary>
    public TextureRegion? Get(int slot)
    {
        LoadAll();
        return (uint)slot < (uint)_regions.Length ? _regions[slot] : null;
    }

    /// <summary>Why a slot could not be decoded, or <see langword="null"/>. Valid once the textures are loaded.</summary>
    public string? ErrorOf(int slot) => (uint)slot < (uint)_errors.Length ? _errors[slot] : null;

    /// <summary>Decodes and uploads every slot that can be decoded. Does nothing after the first call.</summary>
    public void LoadAll()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loaded)
            return;
        _loaded = true;
        Load(); // kept apart: its lambda's captures would otherwise be allocated on every call, loaded or not
    }

    private void Load()
    {
        var decoded = new TextureAtlas.Source?[_regions.Length];
        var halvings = TextureScale switch { 0.5f => 1, 0.25f => 2, 0.125f => 3, _ => 0 };
        Parallel.For(0, _regions.Length, slot =>
        {
            var source = Puppet.Textures[slot];
            if (!source.CanDecode)
                return;
            try
            {
                var image = source.Decode();
                var (rgba, width, height) = (image.Rgba, image.Width, image.Height);
                for (var i = 0; i < halvings; i++)
                    (rgba, width, height) = Mipmaps.Downsample(rgba, width, height);
                decoded[slot] = new TextureAtlas.Source(slot, width, height, rgba);
            }
            catch (PuppetFormatException ex)
            {
                _errors[slot] = ex.Message;
            }
        });

        var sources = DrawOrder().Select(slot => decoded[slot]).OfType<TextureAtlas.Source>().ToList();
        if (UsesAtlas)
        {
            Upload(sources, "atlas page");
        }
        else
        {
            foreach (var source in sources)
                Upload([source], $"texture {source.Slot}");
        }
    }

    /// <summary>
    /// Every slot, in the order a new instance first draws with it (Parts, then mask sources), then the slots nothing draws
    /// at rest: the atlas packs pages in this order so Parts drawn one after another share a page.
    /// </summary>
    private IEnumerable<int> DrawOrder()
    {
        var seen = new bool[_regions.Length];
        var order = new List<int>(_regions.Length);
        var instance = Puppet.CreateInstance();
        foreach (var item in instance.DrawList)
            Add(item.Texture);
        foreach (var source in instance.MaskSources)
            Add(source.Texture);
        for (var slot = 0; slot < seen.Length; slot++)
            Add(slot);
        return order;

        void Add(int? slot)
        {
            if (slot is { } s && (uint)s < (uint)seen.Length && !seen[s])
            {
                seen[s] = true;
                order.Add(s);
            }
        }
    }

    /// <summary>Packs the sources into pages (one page for a single source), uploads them and records each slot's region.</summary>
    private void Upload(IReadOnlyList<TextureAtlas.Source> sources, string what)
    {
        if (sources.Count == 0)
            return;

        var (pages, placements) = TextureAtlas.Pack(sources);
        var textures = new Texture2D[pages.Count];
        for (var p = 0; p < pages.Count; p++)
            textures[p] = Upload(pages[p].Width, pages[p].Height, pages[p].Rgba, pages.Count == 1 ? what : $"{what} {p}");

        foreach (var placement in placements)
        {
            _regions[placement.Slot] = new TextureRegion(textures[placement.Page],
                new Vector2(placement.OffsetU, placement.OffsetV), new Vector2(placement.ScaleU, placement.ScaleV),
                new Vector2(placement.ClipMinU, placement.ClipMinV), new Vector2(placement.ClipMaxU, placement.ClipMaxV));
        }
    }

    private Texture2D Upload(int width, int height, byte[] rgba, string what)
    {
        var texture = new Texture2D(_device, width, height, true, SurfaceFormat.Color)
        {
            Name = $"{Puppet.Meta.Name ?? "puppet"} {what}",
        };

        var level = rgba;
        for (var i = 0; i < texture.LevelCount; i++)
        {
            texture.SetData(i, null, level, 0, level.Length);
            if (i + 1 < texture.LevelCount)
                (level, width, height) = Mipmaps.Downsample(level, width, height);
        }

        _textures.Add(texture);
        return texture;
    }

    /// <summary>Releases the GPU textures.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var texture in _textures)
            texture.Dispose();
        _textures.Clear();
        Array.Clear(_regions);
    }
}

/// <summary>Mipmap generation on the CPU. A 2x2 box filter is correct for premultiplied pixels.</summary>
internal static class Mipmaps
{
    /// <summary>Halves an RGBA image (each side to at least 1), averaging 2x2 blocks; odd edges reuse the last row or column.</summary>
    public static (byte[] Rgba, int Width, int Height) Downsample(byte[] rgba, int width, int height)
    {
        var w = Math.Max(1, width / 2);
        var h = Math.Max(1, height / 2);
        var result = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var y0 = Math.Min(y * 2, height - 1) * width;
            var y1 = Math.Min(y * 2 + 1, height - 1) * width;
            for (var x = 0; x < w; x++)
            {
                var x0 = Math.Min(x * 2, width - 1);
                var x1 = Math.Min(x * 2 + 1, width - 1);
                var target = (y * w + x) * 4;
                for (var c = 0; c < 4; c++)
                {
                    var sum = rgba[(y0 + x0) * 4 + c] + rgba[(y0 + x1) * 4 + c] + rgba[(y1 + x0) * 4 + c] + rgba[(y1 + x1) * 4 + c];
                    result[target + c] = (byte)((sum + 2) / 4);
                }
            }
        }

        return (result, w, h);
    }
}
