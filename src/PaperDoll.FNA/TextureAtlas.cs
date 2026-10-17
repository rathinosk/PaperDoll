#if MONOGAME
namespace PaperDoll.MonoGame;
#else
namespace PaperDoll.Fna;
#endif

/// <summary>
/// Packs a puppet's decoded textures into atlas pages, so parts with different textures can share a draw call. CPU only.
/// </summary>
/// <remarks>
/// <para>
/// Each texture is cropped to the rectangle around its non-zero texels, grown to multiples of <see cref="Padding"/>
/// (Mannequin Character Generator's textures are whole-canvas layers, around 90% transparent), and placed with a
/// transparent margin of <see cref="Padding"/> pixels.
/// Outside the cropped rectangle the texture read transparent before it was cropped, and textures are sampled as with
/// clamp-to-border and a transparent border, so outside its texture a Part draws nothing either: each placement has a
/// clip rectangle, the entry inside its margin less a texel, and the effect reads transparent outside it. Linear filtering
/// at the edges reads the margin, which is transparent as the cropped texels were. Entries start and end on multiples of
/// <see cref="Padding"/>, so mip levels up to log2(<see cref="Padding"/>) never mix two textures.
/// </para>
/// </remarks>
internal static class TextureAtlas
{
    /// <summary>The margin around each entry, in pixels, and the alignment of entries.</summary>
    public const int Padding = 16;

    /// <summary>The largest page edge; the HiDef profile (FNA's and MonoGame's) guarantees 4096.</summary>
    public const int MaxPageSize = 4096;

    /// <summary>A decoded texture to pack. Sources are packed in the order given: pass them in the order they are drawn.</summary>
    /// <param name="Slot">The texture slot.</param>
    /// <param name="Width">Width in pixels.</param>
    /// <param name="Height">Height in pixels.</param>
    /// <param name="Rgba">The pixels.</param>
    public readonly record struct Source(int Slot, int Width, int Height, byte[] Rgba);

    /// <summary>
    /// Where a slot landed: its page, the transform from the original texture's UVs to the page's, and the page-UV
    /// rectangle outside which the slot reads transparent.
    /// </summary>
    public readonly record struct Placement(int Slot, int Page, float OffsetU, float OffsetV, float ScaleU, float ScaleV,
        float ClipMinU, float ClipMinV, float ClipMaxU, float ClipMaxV);

    /// <summary>A packed page: RGBA pixels, premultiplied as the sources were.</summary>
    public sealed record Page(int Width, int Height, byte[] Rgba);

    /// <summary>The smallest rectangle holding every texel with a non-zero byte (premultiplied texels with zero alpha can still add colour); at least 1x1.</summary>
    public static (int X, int Y, int Width, int Height) VisibleRect(int width, int height, byte[] rgba)
    {
        int x0 = width, y0 = height, x1 = -1, y1 = -1;
        var pixels = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>(rgba.AsSpan(0, width * height * 4));
        for (var y = 0; y < height; y++)
        {
            var row = pixels.Slice(y * width, width);
            var first = row.IndexOfAnyExcept(0u);
            if (first < 0)
                continue;
            var last = row.LastIndexOfAnyExcept(0u);
            x0 = Math.Min(x0, first);
            x1 = Math.Max(x1, last);
            y0 = Math.Min(y0, y);
            y1 = y;
        }

        return x1 < 0 ? (0, 0, 1, 1) : (x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    public static (IReadOnlyList<Page> Pages, IReadOnlyList<Placement> Placements) Pack(IReadOnlyList<Source> sources)
    {
        var entries = new Entry[sources.Count];
        Parallel.For(0, sources.Count, i => entries[i] = new Entry(sources[i]));

        // Pages are filled in the order the sources come (the order Parts draw in), so Parts drawn one after another
        // mostly share a page and a draw call: each page takes the longest run of sources that fits it.
        var pages = new List<PageLayout>();
        var queue = new List<Entry>(entries.Length);
        foreach (var entry in entries)
        {
            if (entry.Width > MaxPageSize || entry.Height > MaxPageSize)
            {
                // Too big to share a page: it gets its own, exactly its size.
                var own = new PageLayout(entry.Width, entry.Height);
                own.Place(entry, 0, 0);
                pages.Add(own);
            }
            else
            {
                queue.Add(entry);
            }
        }

        var start = 0;
        while (start < queue.Count)
        {
            // The longest run from start that fits one page (one source always does).
            int low = 1, high = queue.Count - start;
            while (low < high)
            {
                var mid = (low + high + 1) / 2;
                if (PackPage(queue.GetRange(start, mid)) is not null)
                    low = mid;
                else
                    high = mid - 1;
            }

            pages.Add(PackPage(queue.GetRange(start, low))!);
            start += low;
        }

        var result = new List<Page>(pages.Count);
        var placements = new List<Placement>(entries.Length);
        for (var p = 0; p < pages.Count; p++)
        {
            var layout = pages[p];
            var width = layout.UsedWidth;
            var height = layout.UsedHeight;
            var rgba = new byte[width * height * 4];
            foreach (var entry in layout.Entries)
            {
                Blit(entry, rgba, width);
                placements.Add(PlacementOf(entry, p, width, height));
            }

            result.Add(new Page(width, height, rgba));
        }

        placements.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        return (result, placements);
    }

    /// <summary>Places the entries on one page, largest first, or returns <see langword="null"/> if they do not all fit.</summary>
    private static PageLayout? PackPage(List<Entry> entries)
    {
        var page = new PageLayout(MaxPageSize, MaxPageSize);
        foreach (var entry in entries.OrderByDescending(e => (long)e.Width * e.Height).ThenByDescending(e => e.Height).ThenBy(e => e.Source.Slot))
        {
            if (!page.TryPlace(entry))
                return null;
        }

        return page;
    }

    private static Placement PlacementOf(Entry entry, int page, int pageWidth, int pageHeight)
    {
        // Original pixel u * width lands at entry.X + Padding + (u * width - crop.X).
        var s = entry.Source;
        var originX = entry.X + Padding - entry.Crop.X;
        var originY = entry.Y + Padding - entry.Crop.Y;
        return new Placement(s.Slot, page,
            originX / (float)pageWidth,
            originY / (float)pageHeight,
            s.Width / (float)pageWidth,
            s.Height / (float)pageHeight,
            (entry.X + 1) / (float)pageWidth,
            (entry.Y + 1) / (float)pageHeight,
            (entry.X + entry.Width - 1) / (float)pageWidth,
            (entry.Y + entry.Height - 1) / (float)pageHeight);
    }

    /// <summary>Copies the cropped texture inside its margin; the margin stays transparent (the page starts zeroed).</summary>
    private static void Blit(Entry entry, byte[] page, int pageWidth)
    {
        var s = entry.Source;
        var crop = entry.Crop;
        for (var y = 0; y < crop.Height; y++)
        {
            var source = ((crop.Y + y) * s.Width + crop.X) * 4;
            var target = ((entry.Y + Padding + y) * pageWidth + entry.X + Padding) * 4;
            Buffer.BlockCopy(s.Rgba, source, page, target, crop.Width * 4);
        }
    }

    private static int AlignUp(int value) => (value + Padding - 1) / Padding * Padding;

    /// <summary>
    /// The visible rectangle grown to start on multiples of <see cref="Padding"/>, so the texture keeps the alignment to
    /// mip blocks it would have uncropped and its mip levels (up to log2(<see cref="Padding"/>)) hold the same texels.
    /// </summary>
    private static (int X, int Y, int Width, int Height) AlignedCrop(Source source)
    {
        var (x, y, width, height) = VisibleRect(source.Width, source.Height, source.Rgba);
        var x0 = x / Padding * Padding;
        var y0 = y / Padding * Padding;
        var x1 = Math.Min(AlignUp(x + width), source.Width);
        var y1 = Math.Min(AlignUp(y + height), source.Height);
        return (x0, y0, x1 - x0, y1 - y0);
    }

    private sealed class Entry
    {
        public Entry(Source source)
        {
            Source = source;
            Crop = source.Width > 0 && source.Height > 0 ? AlignedCrop(source) : (0, 0, 0, 0);
            Width = AlignUp(Math.Max(Crop.Width, 1) + 2 * Padding);
            Height = AlignUp(Math.Max(Crop.Height, 1) + 2 * Padding);
        }

        public Source Source { get; }
        public (int X, int Y, int Width, int Height) Crop { get; }
        public int Width { get; }
        public int Height { get; }
        public int X { get; set; }
        public int Y { get; set; }
    }

    /// <summary>
    /// A page being packed with the MaxRects algorithm (best short side fit): it keeps every maximal free rectangle, puts
    /// each entry in the one it fits most snugly, and splits the free rectangles it overlaps.
    /// </summary>
    private sealed class PageLayout
    {
        private readonly List<(int X, int Y, int Width, int Height)> _free = [];

        public PageLayout(int maxWidth, int maxHeight) => _free.Add((0, 0, maxWidth, maxHeight));

        public List<Entry> Entries { get; } = [];
        public int UsedWidth { get; private set; }
        public int UsedHeight { get; private set; }

        public bool TryPlace(Entry entry)
        {
            var best = -1;
            int bestShort = int.MaxValue, bestLong = int.MaxValue;
            for (var i = 0; i < _free.Count; i++)
            {
                var f = _free[i];
                if (f.Width < entry.Width || f.Height < entry.Height)
                    continue;
                var leftX = f.Width - entry.Width;
                var leftY = f.Height - entry.Height;
                var shortSide = Math.Min(leftX, leftY);
                var longSide = Math.Max(leftX, leftY);
                if (shortSide < bestShort || (shortSide == bestShort && longSide < bestLong))
                    (best, bestShort, bestLong) = (i, shortSide, longSide);
            }

            if (best < 0)
                return false;
            Place(entry, _free[best].X, _free[best].Y);
            return true;
        }

        public void Place(Entry entry, int x, int y)
        {
            entry.X = x;
            entry.Y = y;
            Entries.Add(entry);
            UsedWidth = Math.Max(UsedWidth, x + entry.Width);
            UsedHeight = Math.Max(UsedHeight, y + entry.Height);
            Split(x, y, entry.Width, entry.Height);
        }

        /// <summary>Replaces every free rectangle the placed one overlaps by the parts of it left free, then drops those inside others.</summary>
        private void Split(int x, int y, int width, int height)
        {
            var count = _free.Count;
            for (var i = 0; i < count; i++)
            {
                var f = _free[i];
                if (x >= f.X + f.Width || x + width <= f.X || y >= f.Y + f.Height || y + height <= f.Y)
                    continue;
                if (x > f.X)
                    _free.Add((f.X, f.Y, x - f.X, f.Height));
                if (x + width < f.X + f.Width)
                    _free.Add((x + width, f.Y, f.X + f.Width - (x + width), f.Height));
                if (y > f.Y)
                    _free.Add((f.X, f.Y, f.Width, y - f.Y));
                if (y + height < f.Y + f.Height)
                    _free.Add((f.X, y + height, f.Width, f.Y + f.Height - (y + height)));
                _free[i] = (0, 0, 0, 0); // removed below
            }

            _free.RemoveAll(f => f.Width == 0 || f.Height == 0);
            for (var i = 0; i < _free.Count; i++)
            {
                for (var j = 0; j < _free.Count; j++)
                {
                    if (i == j || !Contains(_free[j], _free[i]))
                        continue;
                    _free.RemoveAt(i--);
                    break;
                }
            }
        }

        private static bool Contains((int X, int Y, int Width, int Height) outer, (int X, int Y, int Width, int Height) inner) =>
            inner.X >= outer.X && inner.Y >= outer.Y && inner.X + inner.Width <= outer.X + outer.Width && inner.Y + inner.Height <= outer.Y + outer.Height;
    }
}
