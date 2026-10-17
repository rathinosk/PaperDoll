#if MONOGAME
namespace PaperDoll.MonoGame.Tests;
#else
namespace PaperDoll.Fna.Tests;
#endif

/// <summary>Atlas packing and mipmaps: CPU only, no graphics device needed.</summary>
public class AtlasTests
{
    private static byte[] Image(int width, int height, Func<int, int, (byte R, byte G, byte B, byte A)> pixel)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = pixel(x, y);
                var i = (y * width + x) * 4;
                (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = (r, g, b, a);
            }
        }

        return rgba;
    }

    private static (byte, byte, byte, byte) PageAt(TextureAtlas.Page page, int x, int y)
    {
        var i = (y * page.Width + x) * 4;
        return (page.Rgba[i], page.Rgba[i + 1], page.Rgba[i + 2], page.Rgba[i + 3]);
    }

    /// <summary>The page pixel a UV in the source lands on, as the renderer maps it.</summary>
    private static (int X, int Y) PixelOf(TextureAtlas.Placement p, TextureAtlas.Page page, float u, float v) =>
        ((int)MathF.Floor((u * p.ScaleU + p.OffsetU) * page.Width), (int)MathF.Floor((v * p.ScaleV + p.OffsetV) * page.Height));

    /// <summary>The page pixel at the top-left corner of the source: UV (0, 0) falls on a pixel edge, so round rather than floor.</summary>
    private static (int X, int Y) Corner(TextureAtlas.Placement p, TextureAtlas.Page page) =>
        ((int)MathF.Round(p.OffsetU * page.Width), (int)MathF.Round(p.OffsetV * page.Height));

    [Fact]
    public void EveryTexelLandsWhereItsUvPoints()
    {
        var sources = new List<TextureAtlas.Source>();
        for (var slot = 0; slot < 6; slot++)
        {
            var (w, h) = (7 + slot * 13, 5 + slot * 9);
            var s = slot;
            sources.Add(new(slot, w, h, Image(w, h, (x, y) => ((byte)x, (byte)y, (byte)s, 255))));
        }

        var (pages, placements) = TextureAtlas.Pack(sources);

        Assert.Single(pages);
        Assert.Equal(sources.Select(s => s.Slot), placements.Select(p => p.Slot));
        foreach (var source in sources)
        {
            var placement = placements[source.Slot];
            var page = pages[placement.Page];
            for (var y = 0; y < source.Height; y++)
            {
                for (var x = 0; x < source.Width; x++)
                {
                    // The centre of texel (x, y) in UVs.
                    var (px, py) = PixelOf(placement, page, (x + 0.5f) / source.Width, (y + 0.5f) / source.Height);
                    Assert.Equal(((byte)x, (byte)y, (byte)source.Slot, (byte)255), PageAt(page, px, py));
                }
            }
        }
    }

    [Fact]
    public void PaddingIsTransparentAsNijilivesClampToBorder()
    {
        var source = new TextureAtlas.Source(0, 3, 2, Image(3, 2, (x, y) => ((byte)(x * 10), (byte)(y * 10), 0, 255)));

        var (pages, placements) = TextureAtlas.Pack([source]);
        var page = pages[0];
        var p = placements[0];

        var (left, top) = Corner(p, page);
        Assert.Equal((0, 0), (left - TextureAtlas.Padding, top - TextureAtlas.Padding)); // the entry starts at the page corner
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)255), PageAt(page, left, top));    // texel (0, 0)
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)0), PageAt(page, 0, 0));           // the margin reads transparent
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)0), PageAt(page, left - 1, top + 1));
        Assert.Equal(((byte)0, (byte)0, (byte)0, (byte)0), PageAt(page, page.Width - 1, page.Height - 1));
        Assert.Equal(0, page.Width % TextureAtlas.Padding);
        Assert.Equal(0, page.Height % TextureAtlas.Padding);
    }

    [Fact]
    public void TexturesAreCroppedToTheirVisibleTexelsAndClipped()
    {
        // 40x30, visible only in x 10..19, y 5..9; one texel there has zero alpha but some colour (premultiplied: it adds light).
        var source = new TextureAtlas.Source(0, 40, 30, Image(40, 30, (x, y) =>
            x is >= 10 and < 20 && y is >= 5 and < 10 ? ((byte)x, (byte)y, (byte)0, (byte)(x == 10 && y == 5 ? 0 : 255)) : ((byte)0, (byte)0, (byte)0, (byte)0)));
        var neighbour = new TextureAtlas.Source(1, 8, 8, Image(8, 8, (_, _) => (9, 9, 9, 255)));

        var (pages, placements) = TextureAtlas.Pack([source, neighbour]);
        var (page, p) = (pages[placements[0].Page], placements[0]);

        Assert.Equal((10, 5, 10, 5), TextureAtlas.VisibleRect(40, 30, source.Rgba));
        for (var y = 5; y < 10; y++)
        {
            for (var x = 10; x < 20; x++)
            {
                var (px, py) = PixelOf(p, page, (x + 0.5f) / 40, (y + 0.5f) / 30);
                Assert.Equal(((byte)x, (byte)y, (byte)0, (byte)(x == 10 && y == 5 ? 0 : 255)), PageAt(page, px, py));
            }
        }

        // The clip rectangle holds the visible texels with a transparent ring around them, and no texel of the neighbour.
        var (clipX0, clipY0) = ((int)MathF.Floor(p.ClipMinU * page.Width), (int)MathF.Floor(p.ClipMinV * page.Height));
        var (clipX1, clipY1) = ((int)MathF.Ceiling(p.ClipMaxU * page.Width), (int)MathF.Ceiling(p.ClipMaxV * page.Height));
        var visibleLeft = (int)MathF.Round((10f / 40 * p.ScaleU + p.OffsetU) * page.Width);
        var visibleRight = (int)MathF.Round((20f / 40 * p.ScaleU + p.OffsetU) * page.Width);
        Assert.True(visibleLeft - clipX0 >= TextureAtlas.Padding - 1, $"{clipX0} {visibleLeft}");
        Assert.True(clipX1 - visibleRight >= TextureAtlas.Padding - 1, $"{visibleRight} {clipX1}");
        for (var y = clipY0; y < clipY1; y++)
        {
            for (var x = clipX0; x < clipX1; x++)
                Assert.NotEqual(((byte)9, (byte)9, (byte)9, (byte)255), PageAt(page, x, y));
        }
    }

    [Fact]
    public void AnEmptyTextureTakesOneTexel()
    {
        var (pages, placements) = TextureAtlas.Pack([new TextureAtlas.Source(0, 300, 200, new byte[300 * 200 * 4])]);

        Assert.Equal((0, 0, 1, 1), TextureAtlas.VisibleRect(300, 200, new byte[300 * 200 * 4]));
        Assert.Equal(TextureAtlas.Padding * 3, pages[0].Width); // 1 + 2 margins, aligned up
        Assert.Single(placements);
    }

    [Fact]
    public void EntriesDoNotOverlap()
    {
        var random = new Random(7);
        var sources = Enumerable.Range(0, 60)
            .Select(i => new TextureAtlas.Source(i, random.Next(1, 900), random.Next(1, 900), []))
            .Select(s => s with { Rgba = Enumerable.Repeat((byte)255, s.Width * s.Height * 4).ToArray() })
            .ToList();

        var (pages, placements) = TextureAtlas.Pack(sources);

        var rectangles = placements.Select(p =>
        {
            var page = pages[p.Page];
            var source = sources[p.Slot];
            var (x, y) = Corner(p, page);
            return (p.Page, X: x - TextureAtlas.Padding, Y: y - TextureAtlas.Padding,
                W: source.Width + 2 * TextureAtlas.Padding, H: source.Height + 2 * TextureAtlas.Padding);
        }).ToList();

        foreach (var a in rectangles)
        {
            Assert.InRange(a.X + a.W, 0, pages[a.Page].Width);
            Assert.InRange(a.Y + a.H, 0, pages[a.Page].Height);
            Assert.Equal(0, a.X % TextureAtlas.Padding);
            Assert.Equal(0, a.Y % TextureAtlas.Padding);
            foreach (var b in rectangles)
            {
                if (a == b || a.Page != b.Page)
                    continue;
                var overlap = a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;
                Assert.False(overlap, $"{a} overlaps {b}");
            }
        }

        Assert.All(pages, p => Assert.InRange(p.Width, 1, TextureAtlas.MaxPageSize));
    }

    [Fact]
    public void TextureTooBigForAPageGetsItsOwn()
    {
        var big = new TextureAtlas.Source(0, TextureAtlas.MaxPageSize, 4, Enumerable.Repeat((byte)255, TextureAtlas.MaxPageSize * 4 * 4).ToArray());
        var small = new TextureAtlas.Source(1, 4, 4, Enumerable.Repeat((byte)255, 64).ToArray());

        var (pages, placements) = TextureAtlas.Pack([big, small]);

        Assert.Equal(2, pages.Count);
        Assert.NotEqual(placements[0].Page, placements[1].Page);
    }

    [Fact]
    public void MipmapAveragesTwoByTwoBlocks()
    {
        var rgba = Image(3, 2, (x, y) => ((byte)(x * 40 + y * 100), 0, 0, 200));

        var (level, width, height) = Mipmaps.Downsample(rgba, 3, 2);

        Assert.Equal((1, 1), (width, height));
        Assert.Equal((byte)((0 + 40 + 100 + 140 + 2) / 4), level[0]);
        Assert.Equal(200, level[3]);
    }
}
