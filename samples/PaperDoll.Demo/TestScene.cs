using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace PaperDoll.Demo;

/// <summary>
/// A puppet built in memory to check what the samples do not use: one cell per blend mode, drawn over a backdrop that
/// fades to transparent at the bottom, then masks and Composite layers. Each cell has a label under it.
/// </summary>
internal static class TestScene
{
    /// <summary>The name the demo opens the scene by, in place of a file path.</summary>
    public const string Name = "Blend test scene";

    private const int CellWidth = 220;
    private const int CellHeight = 150;
    private const int Columns = 5;

    // Texture slots.
    private const int BackdropTexture = 0, SourceTexture = 1, CircleTexture = 2, RedTexture = 3, WhiteTexture = 4, StripesTexture = 5;

    /// <summary>The scene as an INP1 file, and the labels to draw (puppet space, centre of the text's bottom edge).</summary>
    public static (byte[] File, IReadOnlyList<(string Text, Vector2 Position)> Labels) Build()
    {
        var nodes = new List<string>();
        var labels = new List<(string, Vector2)>();
        uint next = 100;
        var cell = 0;

        void Cell(string label, Func<float, float, string> content)
        {
            var x = cell % Columns * CellWidth;
            var y = cell / Columns * (CellHeight + 40) + 40;
            nodes.Add(content(x, y));
            labels.Add((label, new Vector2(x + CellWidth / 2f - 10, y)));
            cell++;
        }

        string Backdrop(float x, float y) => Quad(next++, x, y, x + CellWidth - 20, y + CellHeight, 10, BackdropTexture);

        foreach (var mode in Enum.GetValues<BlendMode>())
        {
            Cell(mode.ToString(), (x, y) =>
                Backdrop(x, y) + ",\n" + Quad(next++, x + 20, y + 15, x + CellWidth - 40, y + CellHeight - 15, 0, SourceTexture, $", \"blend_mode\": \"{mode}\""));
        }

        cell = (cell + Columns - 1) / Columns * Columns; // masks start a new row

        Cell("Mask (hidden source)", (x, y) =>
        {
            var source = next++;
            return Backdrop(x, y) + ",\n" + Quad(source, x + 40, y + 10, x + 170, y + 140, 5, CircleTexture, ", \"enabled\": false") + ",\n"
                   + Quad(next++, x + 10, y + 40, x + 190, y + 110, 0, RedTexture, Masks((source, "Mask")));
        });
        Cell("DodgeMask", (x, y) =>
        {
            var source = next++;
            return Backdrop(x, y) + ",\n" + Quad(source, x + 40, y + 10, x + 170, y + 140, 5, CircleTexture, ", \"enabled\": false") + ",\n"
                   + Quad(next++, x + 10, y + 40, x + 190, y + 110, 0, RedTexture, Masks((source, "DodgeMask")));
        });
        Cell("Mask + DodgeMask", (x, y) =>
        {
            uint circle = next++, bar = next++;
            return Backdrop(x, y) + ",\n" + Quad(circle, x + 40, y + 10, x + 170, y + 140, 5, CircleTexture, ", \"enabled\": false") + ",\n"
                   + Quad(bar, x + 95, y, x + 115, y + CellHeight, 5, WhiteTexture, ", \"enabled\": false") + ",\n"
                   + Quad(next++, x + 10, y + 40, x + 190, y + 110, 0, RedTexture, Masks((circle, "Mask"), (bar, "DodgeMask")));
        });
        Cell("Mask node (triangle)", (x, y) =>
        {
            var mask = next++;
            var triangle = $$"""
                { "uuid": {{mask}}, "name": "Triangle", "type": "Mask", "zsort": 5,
                  "mesh": { "verts": [{{F(x + 100)}}, {{F(y + 10)}}, {{F(x + 180)}}, {{F(y + 140)}}, {{F(x + 20)}}, {{F(y + 140)}}],
                            "uvs": [0, 0, 0, 0, 0, 0], "indices": [0, 1, 2] } }
                """;
            return Backdrop(x, y) + ",\n" + triangle + ",\n" + Quad(next++, x + 10, y + 40, x + 190, y + 110, 0, RedTexture, Masks((mask, "Mask")));
        });
        Cell("Mask threshold 0.9", (x, y) =>
        {
            var source = next++;
            return Backdrop(x, y) + ",\n" + Quad(source, x + 40, y + 10, x + 170, y + 140, 5, CircleTexture, ", \"enabled\": false, \"mask_threshold\": 0.9") + ",\n"
                   + Quad(next++, x + 10, y + 40, x + 190, y + 110, 0, RedTexture, Masks((source, "Mask")));
        });

        Cell("Composite, opacity 0.5", (x, y) => Backdrop(x, y) + ",\n" + Composite(next++, 0, ", \"opacity\": 0.5",
            Quad(next++, x + 20, y + 20, x + 120, y + 130, 1, RedTexture) + ",\n" + Quad(next++, x + 80, y + 20, x + 180, y + 130, 0, SourceTexture)));
        Cell("Composite, Multiply", (x, y) => Backdrop(x, y) + ",\n" + Composite(next++, 0, ", \"blend_mode\": \"Multiply\"",
            Quad(next++, x + 20, y + 20, x + 120, y + 130, 1, RedTexture) + ",\n" + Quad(next++, x + 80, y + 20, x + 180, y + 130, 0, SourceTexture)));
        Cell("ClipToLower in a Composite", (x, y) => Backdrop(x, y) + ",\n" + Composite(next++, 0, "",
            Quad(next++, x + 40, y + 10, x + 170, y + 140, 1, CircleTexture) + ",\n"
            + Quad(next++, x + 10, y + 20, x + 190, y + 130, 0, StripesTexture, ", \"blend_mode\": \"ClipToLower\"")));
        Cell("SliceFromLower in a Composite", (x, y) => Backdrop(x, y) + ",\n" + Composite(next++, 0, "",
            Quad(next++, x + 40, y + 10, x + 170, y + 140, 1, CircleTexture) + ",\n"
            + Quad(next++, x + 10, y + 20, x + 190, y + 130, 0, StripesTexture, ", \"blend_mode\": \"SliceFromLower\"")));
        Cell("Masked Composite", (x, y) =>
        {
            var source = next++;
            return Backdrop(x, y) + ",\n" + Quad(source, x + 40, y + 10, x + 170, y + 140, 5, CircleTexture, ", \"enabled\": false") + ",\n"
                   + Composite(next++, 0, Masks((source, "Mask")),
                       Quad(next++, x + 10, y + 40, x + 120, y + 110, 1, RedTexture) + ",\n" + Quad(next++, x + 90, y + 40, x + 190, y + 110, 0, SourceTexture));
        });

        var json = $$"""
            {
              "meta": { "name": "{{Name}}", "version": "PaperDoll demo" },
              "physics": { "pixelsPerMeter": 1000, "gravity": 9.8 },
              "nodes": { "uuid": 1, "name": "Root", "type": "Node", "children": [ {{string.Join(",\n", nodes)}} ] },
              "param": []
            }
            """;
        byte[][] textures = [BackdropImage(), SourceImage(), CircleImage(), Solid(255, 40, 40), Solid(255, 255, 255), StripesImage()];
        return (Container(json, textures), labels);
    }

    private static string F(float value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Quad(uint uuid, float x0, float y0, float x1, float y1, float z, int texture, string extra = "") => $$"""
        { "uuid": {{uuid}}, "name": "Part {{uuid}}", "type": "Part", "zsort": {{F(z)}}, "textures": [{{texture}}]{{extra}},
          "mesh": { "verts": [{{F(x0)}}, {{F(y0)}}, {{F(x1)}}, {{F(y0)}}, {{F(x1)}}, {{F(y1)}}, {{F(x0)}}, {{F(y1)}}],
                    "uvs": [0, 0, 1, 0, 1, 1, 0, 1], "indices": [0, 1, 2, 0, 2, 3] } }
        """;

    private static string Composite(uint uuid, float z, string extra, string children) => $$"""
        { "uuid": {{uuid}}, "name": "Composite {{uuid}}", "type": "Composite", "zsort": {{F(z)}}{{extra}}, "children": [ {{children}} ] }
        """;

    private static string Masks(params (uint Source, string Mode)[] masks) =>
        ", \"masks\": [" + string.Join(", ", masks.Select(m => $$"""{ "source": {{m.Source}}, "mode": "{{m.Mode}}" }""")) + "]";

    /// <summary>Hue across, opaque at the top and fading to transparent over the bottom third.</summary>
    private static byte[] BackdropImage() => Image(64, 64, (u, v) =>
    {
        var hue = Hue(u);
        var alpha = v < 2 / 3f ? 1f : 1 - (v - 2 / 3f) * 3;
        return new Vector4(hue * (0.35f + 0.65f * (1 - v)), 1) * alpha;
    });

    /// <summary>A grey ramp across the top half; below, orange fading out across.</summary>
    private static byte[] SourceImage() => Image(64, 64, (u, v) => v < 0.5f
        ? new Vector4(u, u, u, 1)
        : new Vector4(1f, 0.55f, 0.1f, 1) * (1 - u));

    /// <summary>A disc whose alpha falls off towards its edge, for mask thresholds.</summary>
    private static byte[] CircleImage() => Image(64, 64, (u, v) =>
    {
        var r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f));
        var alpha = Math.Clamp((0.5f - r) / 0.25f, 0f, 1f);
        return new Vector4(0.9f, 0.9f, 1f, 1) * alpha;
    });

    private static byte[] StripesImage() => Image(64, 64, (u, v) => (int)(v * 8) % 2 == 0 ? new Vector4(0.1f, 0.3f, 0.9f, 1) : Vector4.Zero);

    private static byte[] Solid(byte r, byte g, byte b) => Image(2, 2, (_, _) => new Vector4(r, g, b, 255) / 255);

    private static Vector3 Hue(float h)
    {
        var r = Math.Clamp(Math.Abs(h * 6 - 3) - 1, 0, 1);
        var g = Math.Clamp(2 - Math.Abs(h * 6 - 2), 0, 1);
        var b = Math.Clamp(2 - Math.Abs(h * 6 - 4), 0, 1);
        return new Vector3(r, g, b);
    }

    /// <summary>An uncompressed 32-bit TGA, top row first, from premultiplied colours.</summary>
    private static byte[] Image(int width, int height, Func<float, float, Vector4> colour)
    {
        var tga = new byte[18 + width * height * 4];
        tga[2] = 2; // uncompressed true colour
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(12), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(tga.AsSpan(14), (ushort)height);
        tga[16] = 32;
        tga[17] = 0x28; // 8 alpha bits, top-left origin
        var i = 18;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var c = Vector4.Clamp(colour((x + 0.5f) / width, (y + 0.5f) / height), Vector4.Zero, Vector4.One) * 255;
                tga[i++] = (byte)MathF.Round(c.Z);
                tga[i++] = (byte)MathF.Round(c.Y);
                tga[i++] = (byte)MathF.Round(c.X);
                tga[i++] = (byte)MathF.Round(c.W);
            }
        }

        return tga;
    }

    /// <summary>An INP1 container: the JSON payload and the textures (encoding 1, TGA).</summary>
    private static byte[] Container(string json, byte[][] textures)
    {
        using var stream = new MemoryStream();
        var number = new byte[4];
        void WriteUInt(int value)
        {
            BinaryPrimitives.WriteUInt32BigEndian(number, (uint)value);
            stream.Write(number);
        }

        stream.Write("TRNSRTS\0"u8);
        var payload = Encoding.UTF8.GetBytes(json);
        WriteUInt(payload.Length);
        stream.Write(payload);
        stream.Write("TEX_SECT"u8);
        WriteUInt(textures.Length);
        foreach (var texture in textures)
        {
            WriteUInt(texture.Length);
            stream.WriteByte(1);
            stream.Write(texture);
        }

        return stream.ToArray();
    }
}
