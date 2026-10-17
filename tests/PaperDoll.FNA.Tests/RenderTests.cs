using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PaperDoll.Snapshots;
using PaperDoll.Tests;

#if MONOGAME
namespace PaperDoll.MonoGame.Tests;
#else
namespace PaperDoll.Fna.Tests;
#endif

/// <summary>
/// A graphics device on a hidden window: FNA and MonoGame create their window hidden and only show it in
/// <see cref="Game.Run"/>, which these tests never call. Every test renders into a <see cref="RenderTarget2D"/> and reads
/// the pixels back.
/// </summary>
public sealed class GraphicsFixture : IDisposable
{
    private readonly Game? _game;

    public GraphicsFixture()
    {
        try
        {
            _game = new Game();
            // HiDef, as PuppetRenderer needs (32-bit indices, 4096-pixel textures); MonoGame defaults to Reach.
            var manager = new GraphicsDeviceManager(_game) { GraphicsProfile = GraphicsProfile.HiDef };
            ((IGraphicsDeviceManager)manager).CreateDevice();
            Device = manager.GraphicsDevice;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Error = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    public GraphicsDevice? Device { get; }

    /// <summary>Why no device could be created (no GPU, missing native libraries), or <see langword="null"/>.</summary>
    public string? Error { get; }

    public GraphicsDevice Require()
    {
        if (Device is null)
            Assert.Skip($"No graphics device: {Error}");
        return Device;
    }

    public void Dispose() => _game?.Dispose();
}

[CollectionDefinition(Name)]
public sealed class GraphicsCollection : ICollectionFixture<GraphicsFixture>
{
    public const string Name = "Graphics";
}

[Collection(GraphicsCollection.Name)]
public sealed class RenderTests(GraphicsFixture graphics)
{
    private const int Size = 64;

    // 32x32, so bilinear filtering fades into the transparent border (as nijilive's clamp-to-border) over half a pixel at most.
    private static byte[] Solid(byte r, byte g, byte b, byte a) =>
        TestData.Png(32, 32, [.. Enumerable.Range(0, 32 * 32).SelectMany(_ => new[] { r, g, b, a })]);

    private static string Quad(uint uuid, int x0, int y0, int x1, int y1, float z, int texture, string extra = "") => $$"""
        { "uuid": {{uuid}}, "name": "Q{{uuid}}", "type": "Part", "zsort": {{z.ToString(System.Globalization.CultureInfo.InvariantCulture)}},
          "textures": [{{texture}}]{{extra}},
          "mesh": { "verts": [{{x0}}, {{y0}}, {{x1}}, {{y0}}, {{x1}}, {{y1}}, {{x0}}, {{y1}}],
                    "uvs": [0, 0, 1, 0, 1, 1, 0, 1], "indices": [0, 1, 2, 0, 2, 3] } }
        """;

    /// <summary>
    /// Blue at the back (0..40), red in front (20..60), and a white quad tinted orange at half opacity (0..16, 48..64).
    /// Puppet space is pixel space here: the tests draw with the identity transform.
    /// </summary>
    private static Puppet ThreeQuads() => TestData.Load(
        TestData.Puppet($"""
            {Quad(2, 0, 0, 40, 40, 1, 1)},
            {Quad(3, 20, 20, 60, 60, 0, 0)},
            {Quad(4, 0, 48, 16, 64, 0, 2, ", \"tint\": [1, 0.5, 0], \"opacity\": 0.5, \"blend_mode\": \"Multiply\"")}
            """),
        [(0, Solid(255, 0, 0, 255)), (0, Solid(0, 0, 255, 255)), (0, Solid(255, 255, 255, 255))]);

    private Color[] Render(Puppet puppet, bool useAtlas, Matrix transform, out int drawCalls, int size = Size, Predicate<DrawItem>? filter = null)
    {
        var device = graphics.Require();
        using var target = new RenderTarget2D(device, size, size, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        using var textures = new PuppetTextures(device, puppet, useAtlas);
        using var renderer = new PuppetRenderer(device);
        var instance = puppet.CreateInstance();

        device.SetRenderTarget(target);
        device.Clear(Color.Transparent);
        renderer.Draw(instance, textures, transform, filter);
        device.SetRenderTarget(null);
        drawCalls = renderer.LastDrawCalls;

        var pixels = new Color[size * size];
        target.GetData(pixels);
        return pixels;
    }

    private static Color At(Color[] pixels, int x, int y, int size = Size) => pixels[y * size + x];

    private static void AssertColour(Color expected, Color actual, int tolerance = 2, string? what = null)
    {
        Assert.True(Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance
                    && Math.Abs(expected.B - actual.B) <= tolerance && Math.Abs(expected.A - actual.A) <= tolerance,
            $"{what}{(what is null ? "" : ": ")}expected {expected}, got {actual}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DrawsBackToFrontWithPremultipliedTintAndOpacity(bool useAtlas)
    {
        var pixels = Render(ThreeQuads(), useAtlas, Matrix.Identity, out _);

        AssertColour(new Color(0, 0, 255, 255), At(pixels, 10, 10));  // the back quad alone
        AssertColour(new Color(255, 0, 0, 255), At(pixels, 30, 30));  // the front quad covers the back one
        AssertColour(new Color(255, 0, 0, 255), At(pixels, 50, 50));
        AssertColour(Color.Transparent, At(pixels, 50, 5));            // nothing drawn
        AssertColour(new Color(128, 64, 0, 128), At(pixels, 8, 56));   // white * (1, 0.5, 0) * 0.5, premultiplied (Multiply over nothing)
    }

    [Fact]
    public void AtlasAndSeparateTexturesGiveTheSamePicture()
    {
        var atlas = Render(ThreeQuads(), useAtlas: true, Matrix.Identity, out var atlasCalls);
        var separate = Render(ThreeQuads(), useAtlas: false, Matrix.Identity, out var separateCalls);

        // Atlas UVs differ from a separate texture's, so a rasteriser may round an edge pixel by a level (WARP does); a
        // neighbour bleeding in from the atlas page would differ by far more.
        Assert.Equal(separate.Length, atlas.Length);
        for (var i = 0; i < atlas.Length; i++)
            AssertColour(separate[i], atlas[i], what: $"pixel ({i % Size}, {i / Size})");
        // The puppet's layer is cleared over its rectangle, the Multiply quad is drawn on its own (with a copy of the layer
        // below it), and the layer is drawn onto the target: one page gives one call for the two Normal quads, separate
        // textures one call each.
        Assert.Equal(1 + 4, atlasCalls);
        Assert.Equal(2 + 4, separateCalls);
    }

    [Fact]
    public void UvsOutsideTheTextureAreTransparentWithOrWithoutTheAtlas()
    {
        // A 4x4 texture with a different colour per texel, stretched over 64 pixels with UVs from -1.5 to 2.5: the outer
        // parts read nothing, as nijilive's transparent clamp-to-border. Magnified, so only the full-size level is sampled.
        var rgba = new byte[4 * 4 * 4];
        for (var i = 0; i < 16; i++)
            (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = ((byte)(i * 16), (byte)(255 - i * 16), (byte)(i % 4 * 80), 255);
        var puppet = TestData.Load(
            TestData.Puppet("""
                { "uuid": 2, "name": "Stretched", "type": "Part", "textures": [1],
                  "mesh": { "verts": [0, 0, 64, 0, 64, 64, 0, 64], "uvs": [-1.5, -1.5, 2.5, -1.5, 2.5, 2.5, -1.5, 2.5], "indices": [0, 1, 2, 0, 2, 3] } }
                """),
            [(0, Solid(255, 255, 255, 255)), (0, TestData.Png(4, 4, rgba)), (0, Solid(0, 255, 0, 255))]);

        var atlas = Render(puppet, useAtlas: true, Matrix.Identity, out _);
        var separate = Render(puppet, useAtlas: false, Matrix.Identity, out _);

        for (var i = 0; i < atlas.Length; i++)
            AssertColour(separate[i], atlas[i], tolerance: 1);
        AssertColour(Color.Transparent, At(atlas, 5, 32));    // UV -1.2
        AssertColour(Color.Transparent, At(atlas, 60, 60));   // UV 2.3
        Assert.Equal(255, At(atlas, 32, 32).A);               // UV 0.5
    }

    [Fact]
    public void TransformPlacesThePuppet()
    {
        var pixels = Render(ThreeQuads(), true, Matrix.CreateScale(0.5f) * Matrix.CreateTranslation(32, 0, 0), out _);

        AssertColour(new Color(0, 0, 255, 255), At(pixels, 34, 4));
        AssertColour(new Color(255, 0, 0, 255), At(pixels, 60, 28));
        AssertColour(Color.Transparent, At(pixels, 10, 10));
    }

    [Fact]
    public void FilterLeavesItemsOut()
    {
        var pixels = Render(ThreeQuads(), true, Matrix.Identity, out _, filter: item => item.Node.Uuid != 4);

        AssertColour(Color.Transparent, At(pixels, 8, 56)); // the Multiply quad
        AssertColour(new Color(255, 0, 0, 255), At(pixels, 30, 30));
    }

    [Fact]
    public void TexturesOfAnotherPuppetAreRejected()
    {
        var device = graphics.Require();
        using var textures = new PuppetTextures(device, ThreeQuads());
        using var renderer = new PuppetRenderer(device);

        Assert.Throws<ArgumentException>(() => renderer.Draw(ThreeQuads().CreateInstance(), textures, Matrix.Identity));
    }

    public static TheoryData<string> Samples => new()
    {
        "99days-samples/claire.inx", "99days-samples/keito.inx", "99days-samples/liam.inx", "99days-samples/rei.inx",
        "99days-samples/taylor.inx", "99days-samples/zeehua.inx", "inochi3d-samples/Aka.inx", "inochi3d-samples/Midori.inx",
        "mannequin-samples/Lena.inx", "mannequin-samples/Mina.inx",
        "inochi3d-samples/ada-static.inx", "inochi3d-samples/Yumi/Yumi.inx", "mannequin-samples/Bunny.inx", "mannequin-samples/Gladys.inx",
        "mannequin-samples/Katie.inx", "mannequin-samples/Ryan.inx",
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void EverySampleRendersIntoItsBounds(string file)
    {
        var root = SampleLocator.FindSamplesRoot();
        var path = root is null ? null : Path.Combine(root, file);
        if (path is null || !File.Exists(path))
            Assert.Skip($"Sample puppet '{file}' is not present in .samples/.");

        const int size = 256;
        var puppet = Puppet.Load(path);
        var bounds = puppet.CreateInstance().Bounds;
        var scale = size / Math.Max(bounds.Size.X, bounds.Size.Y);
        var transform = Matrix.CreateTranslation(-bounds.Center.X, -bounds.Center.Y, 0) * Matrix.CreateScale(scale)
                        * Matrix.CreateTranslation(size / 2f, size / 2f, 0);

        var pixels = Render(puppet, true, transform, out var drawCalls, size);

        // The puppet fills a good part of its bounds, and nothing is drawn far outside them.
        var covered = pixels.Count(p => p.A > 0);
        Assert.InRange(covered, size * size / 20, size * size);
        Assert.InRange(drawCalls, 1, 4 * puppet.Nodes.Count(n => n.Kind is NodeKind.Part or NodeKind.Composite));
    }
}
