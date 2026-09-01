using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Ember.Ui;

/// <summary>
/// An 8×8 glyph atlas drawn as sprites. Same sampling as the rest of the HUD: nearest,
/// integer scale, one-pixel outline. Public-domain IBM VGA 8×8 (font8x8).
/// </summary>
public sealed class SpriteType : IDisposable
{
    public const int Glyph = 8;

    private const int Cols = 16;
    private const int First = 32;
    private const int Count = 95;
    private const int Up = 95;
    private const int Down = 96;
    private const int Times = 97;
    private const int Dot = 98;
    private const int Slots = 99;

    private readonly Texture2D _atlas;
    private readonly byte[] _advance = new byte[Slots];

    /// <summary>
    /// font8x8_basic printable ASCII (U+0020–U+007E). Public domain, Daniel Hepper /
    /// Marcel Sondaar / IBM VGA.
    /// Each glyph is 8 row bytes; the least-significant bit is the left pixel.
    /// </summary>
    private static readonly byte[] Bits =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 24, 60, 60, 24, 24, 0, 24, 0, 54, 54, 0, 0, 0, 0, 0, 0,
        54, 54, 127, 54, 127, 54, 54, 0, 12, 62, 3, 30, 48, 31, 12, 0, 0, 99, 51, 24, 12, 102, 99, 0,
        28, 54, 28, 110, 59, 51, 110, 0, 6, 6, 3, 0, 0, 0, 0, 0, 24, 12, 6, 6, 6, 12, 24, 0,
        6, 12, 24, 24, 24, 12, 6, 0, 0, 102, 60, 255, 60, 102, 0, 0, 0, 12, 12, 63, 12, 12, 0, 0,
        0, 0, 0, 0, 0, 12, 12, 6, 0, 0, 0, 63, 0, 0, 0, 0, 0, 0, 0, 0, 0, 12, 12, 0,
        96, 48, 24, 12, 6, 3, 1, 0, 62, 99, 115, 123, 111, 103, 62, 0, 12, 14, 12, 12, 12, 12, 63, 0,
        30, 51, 48, 28, 6, 51, 63, 0, 30, 51, 48, 28, 48, 51, 30, 0, 56, 60, 54, 51, 127, 48, 120, 0,
        63, 3, 31, 48, 48, 51, 30, 0, 28, 6, 3, 31, 51, 51, 30, 0, 63, 51, 48, 24, 12, 12, 12, 0,
        30, 51, 51, 30, 51, 51, 30, 0, 30, 51, 51, 62, 48, 24, 14, 0, 0, 12, 12, 0, 0, 12, 12, 0,
        0, 12, 12, 0, 0, 12, 12, 6, 24, 12, 6, 3, 6, 12, 24, 0, 0, 0, 63, 0, 0, 63, 0, 0,
        6, 12, 24, 48, 24, 12, 6, 0, 30, 51, 48, 24, 12, 0, 12, 0, 62, 99, 123, 123, 123, 3, 30, 0,
        12, 30, 51, 51, 63, 51, 51, 0, 63, 102, 102, 62, 102, 102, 63, 0, 60, 102, 3, 3, 3, 102, 60, 0,
        31, 54, 102, 102, 102, 54, 31, 0, 127, 70, 22, 30, 22, 70, 127, 0, 127, 70, 22, 30, 22, 6, 15, 0,
        60, 102, 3, 3, 115, 102, 124, 0, 51, 51, 51, 63, 51, 51, 51, 0, 30, 12, 12, 12, 12, 12, 30, 0,
        120, 48, 48, 48, 51, 51, 30, 0, 103, 102, 54, 30, 54, 102, 103, 0, 15, 6, 6, 6, 70, 102, 127, 0,
        99, 119, 127, 127, 107, 99, 99, 0, 99, 103, 111, 123, 115, 99, 99, 0, 28, 54, 99, 99, 99, 54, 28, 0,
        63, 102, 102, 62, 6, 6, 15, 0, 30, 51, 51, 51, 59, 30, 56, 0, 63, 102, 102, 62, 54, 102, 103, 0,
        30, 51, 7, 14, 56, 51, 30, 0, 63, 45, 12, 12, 12, 12, 30, 0, 51, 51, 51, 51, 51, 51, 63, 0,
        51, 51, 51, 51, 51, 30, 12, 0, 99, 99, 99, 107, 127, 119, 99, 0, 99, 99, 54, 28, 28, 54, 99, 0,
        51, 51, 51, 30, 12, 12, 30, 0, 127, 99, 49, 24, 76, 102, 127, 0, 30, 6, 6, 6, 6, 6, 30, 0,
        3, 6, 12, 24, 48, 96, 64, 0, 30, 24, 24, 24, 24, 24, 30, 0, 8, 28, 54, 99, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 255, 12, 12, 24, 0, 0, 0, 0, 0, 0, 0, 30, 48, 62, 51, 110, 0,
        7, 6, 6, 62, 102, 102, 59, 0, 0, 0, 30, 51, 3, 51, 30, 0, 56, 48, 48, 62, 51, 51, 110, 0,
        0, 0, 30, 51, 63, 3, 30, 0, 28, 54, 6, 15, 6, 6, 15, 0, 0, 0, 110, 51, 51, 62, 48, 31,
        7, 6, 54, 110, 102, 102, 103, 0, 12, 0, 14, 12, 12, 12, 30, 0, 48, 0, 48, 48, 48, 51, 51, 30,
        7, 6, 102, 54, 30, 54, 103, 0, 14, 12, 12, 12, 12, 12, 30, 0, 0, 0, 51, 127, 127, 107, 99, 0,
        0, 0, 31, 51, 51, 51, 51, 0, 0, 0, 30, 51, 51, 51, 30, 0, 0, 0, 59, 102, 102, 62, 6, 15,
        0, 0, 110, 51, 51, 62, 48, 120, 0, 0, 59, 110, 102, 6, 15, 0, 0, 0, 62, 3, 30, 48, 31, 0,
        8, 12, 62, 12, 12, 44, 24, 0, 0, 0, 51, 51, 51, 51, 110, 0, 0, 0, 51, 51, 51, 30, 12, 0,
        0, 0, 99, 107, 127, 127, 54, 0, 0, 0, 99, 54, 28, 54, 99, 0, 0, 0, 51, 51, 51, 62, 48, 31,
        0, 0, 63, 25, 12, 38, 63, 0, 56, 12, 12, 7, 12, 12, 56, 0, 24, 24, 24, 0, 24, 24, 24, 0,
        7, 12, 12, 56, 12, 12, 7, 0, 110, 59, 0, 0, 0, 0, 0, 0
    ];

    private SpriteType(Texture2D atlas) => _atlas = atlas;

    public static SpriteType Bake(GraphicsDevice device)
    {
        var rows = (Slots + Cols - 1) / Cols;
        var width = Cols * Glyph;
        var height = rows * Glyph;
        var pixels = new Color[width * height];
        var type = new SpriteType(new Texture2D(device, width, height, false, SurfaceFormat.Color));

        for (var i = 0; i < Count; i++)
        {
            Stamp(pixels, width, i, Bits.AsSpan(i * Glyph, Glyph));
            type._advance[i] = AdvanceOf(Bits.AsSpan(i * Glyph, Glyph));
        }

        Stamp(pixels, width, Up, Triangle(up: true));
        Stamp(pixels, width, Down, Triangle(up: false));
        Stamp(pixels, width, Times, Cross());
        Stamp(pixels, width, Dot, MidDot());
        type._advance[Up] = 8;
        type._advance[Down] = 8;
        type._advance[Times] = 7;
        type._advance[Dot] = 4;
        type._advance[0] = 4;

        type._atlas.SetData(pixels);
        return type;
    }

    /// <summary>Map a UI size onto 1× / 2× / 3× sprite cells. Keep scale low so stems stay open.</summary>
    public static int PixelScale(float requested, bool heading)
    {
        if (heading && requested >= 28f) return 3;
        if (heading || requested >= 12f) return 2;
        return 1;
    }

    public static float LineHeight(int pixelScale) => Glyph * pixelScale + 2;

    public float Measure(string value, int pixelScale)
    {
        if (string.IsNullOrEmpty(value)) return 0f;
        var w = 0;
        foreach (var c in value)
            w += _advance[IndexOf(c)];
        return w * pixelScale;
    }

    public void Draw(SpriteBatch batch, string value, Vector2 position, int pixelScale, Color color)
    {
        if (string.IsNullOrEmpty(value) || color.A == 0) return;

        var x = (int)MathF.Round(position.X);
        var y = (int)MathF.Round(position.Y);
        var shadow = new Color((byte)0, (byte)0, (byte)0, (byte)Math.Min(color.A, (byte)110));

        foreach (var c in value)
        {
            var index = IndexOf(c);
            var src = Cell(index);
            var dest = new Rectangle(x, y, Glyph * pixelScale, Glyph * pixelScale);
            if (shadow.A > 20)
                batch.Draw(_atlas, Offset(dest, 1, 1), src, shadow);
            batch.Draw(_atlas, dest, src, color);
            x += _advance[index] * pixelScale;
        }
    }

    public void Dispose() => _atlas.Dispose();

    private static Rectangle Cell(int index) =>
        new(index % Cols * Glyph, index / Cols * Glyph, Glyph, Glyph);

    private static Rectangle Offset(Rectangle r, int x, int y) =>
        new(r.X + x, r.Y + y, r.Width, r.Height);

    private static int IndexOf(char c) => c switch
    {
        '▲' or '\u25B2' => Up,
        '▼' or '\u25BC' => Down,
        '×' or '✕' or '✖' => Times,
        '·' or '•' => Dot,
        '—' or '–' or '−' => '-' - First,
        '’' or '‘' or '‛' => '\'' - First,
        '“' or '”' => '"' - First,
        _ when c >= First && c < First + Count => c - First,
        _ => '?' - First
    };

    private static void Stamp(Color[] pixels, int width, int index, ReadOnlySpan<byte> rows)
    {
        var ox = index % Cols * Glyph;
        var oy = index / Cols * Glyph;
        for (var y = 0; y < Glyph; y++)
        {
            var bits = rows[y];
            for (var x = 0; x < Glyph; x++)
            {
                if ((bits & (1 << x)) == 0) continue;
                pixels[(oy + y) * width + ox + x] = Color.White;
            }
        }
    }

    private static byte AdvanceOf(ReadOnlySpan<byte> rows)
    {
        var max = 0;
        var any = false;
        for (var y = 0; y < Glyph; y++)
        {
            var bits = rows[y];
            for (var x = 0; x < Glyph; x++)
            {
                if ((bits & (1 << x)) == 0) continue;
                any = true;
                if (x > max) max = x;
            }
        }

        return (byte)(any ? Math.Min(8, max + 2) : 4);
    }

    private static byte[] Triangle(bool up)
    {
        var rows = new byte[Glyph];
        for (var y = 0; y < 7; y++)
        {
            var t = up ? y : 6 - y;
            var left = 3 - t / 2;
            var right = 4 + t / 2;
            byte bits = 0;
            for (var x = left; x <= right; x++)
                bits |= (byte)(1 << x);
            rows[y] = bits;
        }

        return rows;
    }

    private static byte[] Cross()
    {
        return
        [
            0b01000010,
            0b00100100,
            0b00011000,
            0b00011000,
            0b00100100,
            0b01000010,
            0, 0
        ];
    }

    private static byte[] MidDot() => [0, 0, 0, 0b00011000, 0b00011000, 0, 0, 0];
}
