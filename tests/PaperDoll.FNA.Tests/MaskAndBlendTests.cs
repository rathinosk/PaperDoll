using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PaperDoll.Tests;

#if MONOGAME
namespace PaperDoll.MonoGame.Tests;
#else
namespace PaperDoll.Fna.Tests;
#endif

/// <summary>Masks, Composite layers and blend modes, checked pixel by pixel against the formulas.</summary>
[Collection(GraphicsCollection.Name)]
public sealed class MaskAndBlendTests(GraphicsFixture graphics)
{
    private const int Size = 32;

    private static string F(float value) => value.ToString(CultureInfo.InvariantCulture);

    // 32x32, so bilinear filtering fades into the transparent border (as nijilive's clamp-to-border) over half a pixel at most.
    private static byte[] Solid(byte r, byte g, byte b, byte a) =>
        TestData.Png(32, 32, [.. Enumerable.Range(0, 32 * 32).SelectMany(_ => new[] { r, g, b, a })]);

    private static string Quad(uint uuid, float x0, float y0, float x1, float y1, float z, int texture, string extra = "", string type = "Part") => $$"""
        { "uuid": {{uuid}}, "name": "Q{{uuid}}", "type": "{{type}}", "zsort": {{F(z)}}, "textures": [{{texture}}]{{extra}},
          "mesh": { "verts": [{{F(x0)}}, {{F(y0)}}, {{F(x1)}}, {{F(y0)}}, {{F(x1)}}, {{F(y1)}}, {{F(x0)}}, {{F(y1)}}],
                    "uvs": [0, 0, 1, 0, 1, 1, 0, 1], "indices": [0, 1, 2, 0, 2, 3] } }
        """;

    private static string Composite(uint uuid, float z, string children, string extra = "") => $$"""
        { "uuid": {{uuid}}, "name": "C{{uuid}}", "type": "Composite", "zsort": {{F(z)}}{{extra}}, "children": [ {{children}} ] }
        """;

    private static string Masks(params (uint Source, string Mode)[] masks) =>
        ", \"masks\": [" + string.Join(", ", masks.Select(m => $$"""{ "source": {{m.Source}}, "mode": "{{m.Mode}}" }""")) + "]";

    // Texture slots: 0 red, 1 blue, 2 white, 3 white at alpha 0.4 (premultiplied), 4 grey (0.5).
    private static readonly (byte, byte[])[] Textures =
    [
        (0, Solid(255, 0, 0, 255)), (0, Solid(0, 0, 255, 255)), (0, Solid(255, 255, 255, 255)),
        (0, Solid(102, 102, 102, 102)), (0, Solid(128, 128, 128, 255)),
    ];

    private Color[] Render(string children, bool advanced = true, Color? clear = null, Matrix? transform = null, int size = Size)
    {
        var device = graphics.Require();
        var puppet = TestData.Load(TestData.Puppet(children), Textures);
        using var target = new RenderTarget2D(device, size, size, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        using var textures = new PuppetTextures(device, puppet);
        using var renderer = new PuppetRenderer(device) { AdvancedBlending = advanced };

        device.SetRenderTarget(target);
        device.Clear(clear ?? Color.Transparent);
        renderer.Draw(puppet.CreateInstance(), textures, transform ?? Matrix.Identity);
        device.SetRenderTarget(null);

        var pixels = new Color[size * size];
        target.GetData(pixels);
        return pixels;
    }

    private static Color At(Color[] pixels, int x, int y, int size = Size) => pixels[y * size + x];

    private static void AssertColour(Color expected, Color actual, int tolerance = 2, string? what = null)
    {
        Assert.True(Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance
                    && Math.Abs(expected.B - actual.B) <= tolerance && Math.Abs(expected.A - actual.A) <= tolerance,
            $"{what}: expected {expected}, got {actual}");
    }

    [Fact]
    public void MaskDrawsOnlyWhereTheSourceIs()
    {
        // Blue at the back; red masked by a white source over x 8..16 (the source itself is drawn too, behind the red).
        var pixels = Render($"""
            {Quad(2, 0, 0, 32, 32, 2, 1)},
            {Quad(3, 8, 0, 16, 32, 1, 2)},
            {Quad(4, 0, 0, 32, 16, 0, 0, Masks((3, "Mask")))}
            """);

        AssertColour(new Color(255, 0, 0, 255), At(pixels, 12, 8));    // inside the source
        AssertColour(new Color(0, 0, 255, 255), At(pixels, 4, 8));     // outside it
        AssertColour(new Color(255, 255, 255, 255), At(pixels, 12, 24)); // the source below the red quad
    }

    [Fact]
    public void DodgeMaskDrawsOnlyWhereTheSourceIsNot()
    {
        var pixels = Render($"""
            {Quad(3, 8, 0, 16, 32, 1, 2, ", \"enabled\": false")},
            {Quad(4, 0, 0, 32, 32, 0, 0, Masks((3, "DodgeMask")))}
            """);

        AssertColour(Color.Transparent, At(pixels, 12, 8));          // a hidden source still masks, as in nijilive
        AssertColour(new Color(255, 0, 0, 255), At(pixels, 4, 8));
    }

    [Fact]
    public void DodgeMasksCutHolesInMasks()
    {
        // Masked to x 0..16, with a hole at x 8..12.
        var pixels = Render($"""
            {Quad(3, 0, 0, 16, 32, 1, 2, ", \"enabled\": false")},
            {Quad(5, 8, 0, 12, 32, 1, 2, ", \"enabled\": false")},
            {Quad(4, 0, 0, 32, 32, 0, 0, Masks((3, "Mask"), (5, "DodgeMask")))}
            """);

        AssertColour(new Color(255, 0, 0, 255), At(pixels, 4, 8));
        AssertColour(Color.Transparent, At(pixels, 10, 8));
        AssertColour(Color.Transparent, At(pixels, 20, 8));
    }

    [Fact]
    public void SuccessiveMaskPassesInOneLayerDoNotSeeEachOther()
    {
        // Hidden sources over x 0..8 (A) and x 16..24 (B). Rows, front to back: y 0..8 masked by A, y 8..16 masked by B,
        // y 16..24 dodged by A, y 24..32 masked by A with B dodged, then masked by A again (blue over the red row).
        // Each pass reuses the layer's stencil, so none may see what an earlier one wrote.
        var pixels = Render($$"""
            {{Quad(3, 0, 0, 8, 32, 9, 2, ", \"enabled\": false")}},
            {{Quad(5, 16, 0, 24, 32, 9, 2, ", \"enabled\": false")}},
            {{Quad(10, 0, 0, 32, 8, 5, 0, Masks((3, "Mask")))}},
            {{Quad(11, 0, 8, 32, 16, 4, 0, Masks((5, "Mask")))}},
            {{Quad(12, 0, 16, 32, 24, 3, 0, Masks((3, "DodgeMask")))}},
            {{Quad(13, 0, 24, 32, 32, 2, 0, Masks((3, "Mask"), (5, "DodgeMask")))}},
            {{Quad(14, 0, 0, 32, 8, 1, 1, Masks((3, "Mask")))}}
            """);

        AssertColour(new Color(0, 0, 255, 255), At(pixels, 4, 4), what: "row 1 inside A, blue on top");
        AssertColour(Color.Transparent, At(pixels, 20, 4), what: "row 1 outside A");
        AssertColour(Color.Transparent, At(pixels, 4, 12), what: "row 2 inside A only");
        AssertColour(new Color(255, 0, 0, 255), At(pixels, 20, 12), what: "row 2 inside B");
        AssertColour(Color.Transparent, At(pixels, 4, 20), what: "row 3 under the dodge");
        AssertColour(new Color(255, 0, 0, 255), At(pixels, 20, 20), what: "row 3 outside A");
        AssertColour(new Color(255, 0, 0, 255), At(pixels, 4, 28), what: "row 4 inside A");
        AssertColour(Color.Transparent, At(pixels, 20, 28), what: "row 4 outside A");
    }

    [Theory]
    [InlineData(0.5f, false)] // alpha 0.4 is at or below the threshold: the source masks nothing
    [InlineData(0.3f, true)]
    public void SourcesBelowTheirThresholdDoNotMask(float threshold, bool masks)
    {
        var pixels = Render($"""
            {Quad(3, 0, 0, 32, 32, 1, 3, $", \"enabled\": false, \"mask_threshold\": {F(threshold)}")},
            {Quad(4, 0, 0, 32, 32, 0, 0, Masks((3, "Mask")))}
            """);

        AssertColour(masks ? new Color(255, 0, 0, 255) : Color.Transparent, At(pixels, 16, 16));
    }

    [Fact]
    public void MaskNodesMaskWithTheirWholeMesh()
    {
        var pixels = Render($"""
            {Quad(3, 0, 0, 10, 32, 1, 0, type: "Mask")},
            {Quad(4, 0, 0, 32, 32, 0, 0, Masks((3, "Mask")))}
            """);

        AssertColour(new Color(255, 0, 0, 255), At(pixels, 5, 16));
        AssertColour(Color.Transparent, At(pixels, 20, 16));
    }

    [Fact]
    public void ACompositeLayerIsDrawnPixelForPixel()
    {
        // The same quads at fractional positions, with and without a Normal Composite around them, through a transform:
        // the layer must not shift or blur what it holds.
        var quads = $"""
            {Quad(2, 0.3f, 1.7f, 20.2f, 22.9f, 1, 1)},
            {Quad(3, 9.6f, 8.1f, 30.4f, 29.5f, 0, 4, ", \"opacity\": 0.6")}
            """;
        var transform = Matrix.CreateScale(1.37f) * Matrix.CreateTranslation(-3.3f, -1.1f, 0);
        var plain = Render(quads, transform: transform);
        var layered = Render(Composite(10, 0, quads), transform: transform);

        for (var i = 0; i < plain.Length; i++)
            AssertColour(plain[i], layered[i], tolerance: 1, what: $"pixel ({i % Size}, {i / Size})");
    }

    [Fact]
    public void ACompositeIsBlendedAsOneLayer()
    {
        // Two opaque quads overlapping in a Composite at half opacity: the overlap shows the front quad at half opacity,
        // not the two blended one over the other.
        var pixels = Render(Composite(10, 0, $"""
            {Quad(2, 0, 0, 20, 32, 1, 1)},
            {Quad(3, 10, 0, 32, 32, 0, 0)}
            """, ", \"opacity\": 0.5, \"tint\": [1, 0.5, 1]"));

        AssertColour(new Color(0, 0, 128, 128), At(pixels, 5, 16));
        AssertColour(new Color(128, 0, 0, 128), At(pixels, 15, 16));
    }

    [Fact]
    public void ScreenTintIsDrawn()
    {
        // nijilive: 1 - (1 - texel) * (1 - screen * alpha), before the tint.
        var pixels = Render(Quad(2, 0, 0, 32, 32, 0, 1, ", \"screenTint\": [0.5, 0, 0], \"tint\": [1, 1, 0.5]"));

        AssertColour(new Color(128, 0, 128, 255), At(pixels, 16, 16));
    }

    [Fact]
    public void ClipToLowerClipsToThePuppetNotTheScene()
    {
        // A ClipToLower quad over an opaque scene draws nothing: the puppet is drawn into its own layer, as in nijilive.
        var scene = new Color(10, 20, 30, 255);
        var pixels = Render($"""
            {Quad(2, 0, 0, 16, 32, 1, 1)},
            {Quad(3, 0, 0, 32, 32, 0, 0, ", \"blend_mode\": \"ClipToLower\"")}
            """, clear: scene);

        AssertColour(new Color(255, 0, 0, 255), At(pixels, 8, 16));
        AssertColour(scene, At(pixels, 24, 16));
    }

    public static TheoryData<BlendMode, bool, float> BlendCases()
    {
        var cases = new TheoryData<BlendMode, bool, float>();
        foreach (var mode in Enum.GetValues<BlendMode>())
        {
            foreach (var backdropOpacity in new[] { 1f, 0.5f })
            {
                cases.Add(mode, true, backdropOpacity);
                if (BlendModes.IsAdvanced(mode))
                    cases.Add(mode, false, backdropOpacity);
            }
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(BlendCases))]
    public void BlendModesMatchTheirEquations(BlendMode mode, bool advanced, float backdropOpacity)
    {
        // Backdrop: (0.2, 0.6, 0.9) at the given opacity. Source: (0.8, 0.3, 0.5) at opacity 0.75.
        var backdrop = new Vector4(0.2f, 0.6f, 0.9f, 1) * backdropOpacity;
        var source = new Vector4(0.8f, 0.3f, 0.5f, 1) * 0.75f;
        var pixels = Render($"""
            {Quad(2, 0, 0, 32, 32, 1, 2, $", \"tint\": [0.2, 0.6, 0.9], \"opacity\": {F(backdropOpacity)}")},
            {Quad(3, 0, 0, 32, 32, 0, 2, $", \"tint\": [0.8, 0.3, 0.5], \"opacity\": 0.75, \"blend_mode\": \"{mode}\"")}
            """, advanced);

        var expected = advanced && BlendModes.IsAdvanced(mode) ? Advanced(mode, source, backdrop) : FixedFunction(mode, source, backdrop);
        AssertColour(new Color(Vector4.Clamp(expected, Vector4.Zero, Vector4.One)), At(pixels, 16, 16), tolerance: 3, what: $"{mode}");
    }

    /// <summary>KHR_blend_equation_advanced with premultiplied colours (X = Y = Z = 1).</summary>
    private static Vector4 Advanced(BlendMode mode, Vector4 s, Vector4 d)
    {
        var cs = new Vector3(s.X, s.Y, s.Z) / s.W;
        var cd = new Vector3(d.X, d.Y, d.Z) / d.W;
        var f = new Vector3(Channel(mode, cs.X, cd.X), Channel(mode, cs.Y, cd.Y), Channel(mode, cs.Z, cd.Z));
        var rgb = f * s.W * d.W + new Vector3(s.X, s.Y, s.Z) * (1 - d.W) + new Vector3(d.X, d.Y, d.Z) * (1 - s.W);
        return new Vector4(rgb, s.W + d.W - s.W * d.W);

        static float Channel(BlendMode mode, float s, float d) => mode switch
        {
            BlendMode.Multiply => s * d,
            BlendMode.Screen => s + d - s * d,
            BlendMode.Overlay => d <= 0.5f ? 2 * s * d : 1 - 2 * (1 - s) * (1 - d),
            BlendMode.Darken => Math.Min(s, d),
            BlendMode.Lighten => Math.Max(s, d),
            BlendMode.ColorDodge => d <= 0 ? 0 : s >= 1 ? 1 : Math.Min(1, d / (1 - s)),
            BlendMode.ColorBurn => d >= 1 ? 1 : s <= 0 ? 0 : 1 - Math.Min(1, (1 - d) / s),
            BlendMode.HardLight => s <= 0.5f ? 2 * s * d : 1 - 2 * (1 - s) * (1 - d),
            BlendMode.SoftLight => s <= 0.5f ? d - (1 - 2 * s) * d * (1 - d)
                : d <= 0.25f ? d + (2 * s - 1) * d * ((16 * d - 12) * d + 3)
                : d + (2 * s - 1) * (MathF.Sqrt(d) - d),
            BlendMode.Difference => Math.Abs(d - s),
            BlendMode.Exclusion => s + d - 2 * s * d,
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    /// <summary>The fixed-function blending of each mode, written out.</summary>
    private static Vector4 FixedFunction(BlendMode mode, Vector4 s, Vector4 d)
    {
        Vector4 One = Vector4.One;
        Vector4 SrcA = new(s.W), DstA = new(d.W);
        Vector4 InvSrcColor = new(1 - s.X, 1 - s.Y, 1 - s.Z, 1 - s.W);
        Vector4 InvDstColor = new(1 - d.X, 1 - d.Y, 1 - d.Z, 1 - d.W);
        Vector4 Split(Vector4 rgb, Vector4 alpha) => new(rgb.X, rgb.Y, rgb.Z, alpha.W);
        return mode switch
        {
            BlendMode.Multiply => s * d + d * (One - SrcA),
            BlendMode.Screen => s + d * InvSrcColor,
            BlendMode.Lighten => Vector4.Max(s, d),
            BlendMode.ColorDodge => s * d + d,
            BlendMode.LinearDodge => s + d * InvSrcColor,
            BlendMode.AddGlow => Split(s + d, s + d * (One - SrcA)),
            BlendMode.Subtract => Split(d - s * InvDstColor, s * InvDstColor + d),
            BlendMode.Exclusion => Split(s * InvDstColor + d * InvSrcColor, s + d),
            BlendMode.Inverse => s * InvDstColor + d * (One - SrcA),
            BlendMode.DestinationIn => d * SrcA,
            BlendMode.ClipToLower => s * DstA + d * (One - SrcA),
            BlendMode.SliceFromLower => d * (One - SrcA),
            _ => s + d * (One - SrcA), // Normal, and the modes nijilive has no fixed-function equation for
        };
    }

    [Fact]
    public void AdvancedModesInsideCompositesBlendWithTheLayer()
    {
        // ColorBurn inside a Composite (as Mannequin Character Generator puppets use it) burns the Composite's own content.
        var backdrop = new Vector4(0.2f, 0.6f, 0.9f, 1);
        var source = new Vector4(0.8f, 0.3f, 0.5f, 1) * 0.75f;
        var pixels = Render(Composite(10, 0, $"""
            {Quad(2, 0, 0, 32, 32, 1, 2, ", \"tint\": [0.2, 0.6, 0.9]")},
            {Quad(3, 0, 0, 32, 32, 0, 2, ", \"tint\": [0.8, 0.3, 0.5], \"opacity\": 0.75, \"blend_mode\": \"ColorBurn\"")}
            """));

        AssertColour(new Color(Advanced(BlendMode.ColorBurn, source, backdrop)), At(pixels, 16, 16), tolerance: 3);
    }

    [Fact]
    public void TheTargetMustKeepItsContents()
    {
        var device = graphics.Require();
        var puppet = TestData.Load(TestData.Puppet(Quad(2, 0, 0, 8, 8, 0, 0)), Textures);
        using var target = new RenderTarget2D(device, Size, Size, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
        using var textures = new PuppetTextures(device, puppet);
        using var renderer = new PuppetRenderer(device);

        device.SetRenderTarget(target);
        try
        {
            Assert.Throws<InvalidOperationException>(() => renderer.Draw(puppet.CreateInstance(), textures, Matrix.Identity));
        }
        finally
        {
            device.SetRenderTarget(null);
        }
    }

    [Fact]
    public void TheCompiledEffectMatchesItsSource()
    {
        // Each build-shaders.ps1 records the source its effects were compiled from; run it after changing PaperDoll.fx
        // (PaperDoll.FNA's, shared with PaperDoll.MonoGame).
        var source = File.ReadAllText(Path.Combine(FindShaderFolder("PaperDoll.FNA"), "PaperDoll.fx")).Replace("\r\n", "\n");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
#if MONOGAME
        var compiled = FindShaderFolder("PaperDoll.MonoGame");
#else
        var compiled = FindShaderFolder("PaperDoll.FNA");
#endif

        Assert.Equal(File.ReadAllText(Path.Combine(compiled, "PaperDoll.fx.sha256")).Trim(), hash);
    }

    private static string FindShaderFolder(string project)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", project, "Shaders");
            if (Directory.Exists(candidate))
                return candidate;
        }

        Assert.Skip($"The {project} sources are not above the test's folder.");
        return "";
    }
}
