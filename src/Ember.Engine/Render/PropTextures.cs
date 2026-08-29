using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Ember.Render;

/// <summary>
/// The dressing: wood, cloth, flame, the lotus, and the ornate frame the interface is drawn in.
///
/// Same doctrine as <see cref="StoneTextures"/>. These exist to answer one question — whether a
/// generated-art pipeline can reach the fidelity of hand-drawn pixel art — and they are the
/// cheap half of the answer. Surfaces and ornament generate well because they are rules. Icons,
/// faces and creatures do not, and are not attempted here.
/// </summary>
public static class PropTextures
{
    private static readonly Dictionary<string, Texture2D> Cache = new(StringComparer.Ordinal);

    private static readonly Color Gold = new(198, 158, 74);
    private static readonly Color GoldBright = new(236, 206, 128);
    private static readonly Color GoldDark = new(120, 92, 40);

    public static void Clear()
    {
        foreach (var texture in Cache.Values) texture.Dispose();
        Cache.Clear();
    }

    private static Texture2D Get(GraphicsDevice device, string key, int width, int height,
        Func<Color[]> build)
    {
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var texture = new Texture2D(device, width, height);
        texture.SetData(build());
        Cache[key] = texture;
        return texture;
    }

    // ------------------------------------------------------------------ wood

    /// <summary>Vertical planks with iron banding, for doors.</summary>
    public static Texture2D Door(GraphicsDevice device) => Get(device, "door", 128, 192, () =>
    {
        var pixels = new Color[128 * 192];
        var random = new Random(0x2C0D);
        var plank = new Color(122, 74, 40);
        var plankDark = new Color(88, 52, 27);
        var iron = new Color(74, 72, 78);

        for (var y = 0; y < 192; y++)
        for (var x = 0; x < 128; x++)
        {
            const int PlankWidth = 26;
            var inPlank = x % PlankWidth;

            var tone = MathF.Sin(x * 0.7f) * 6f + MathF.Sin(y * 0.13f + x) * 5f;
            var colour = Shift(Color.Lerp(plankDark, plank, 0.5f + tone / 40f), random.Next(-6, 7));

            // The gap between planks, and the lit edge of the plank beside it.
            if (inPlank < 2) colour = Shift(plankDark, -34);
            else if (inPlank < 4) colour = Shift(colour, 14);
            else if (inPlank > PlankWidth - 3) colour = Shift(colour, -18);

            // Two iron bands across, with rivets.
            var band = (y > 26 && y < 44) || (y > 148 && y < 166);
            if (band)
            {
                colour = Shift(iron, random.Next(-8, 9));
                if (y == 27 || y == 149) colour = Shift(iron, 26);
                if (y == 43 || y == 165) colour = Shift(iron, -22);

                var rivet = (x % 32 == 12 || x % 32 == 20) && (y == 34 || y == 35 || y == 156 || y == 157);
                if (rivet) colour = GoldBright;
            }

            pixels[y * 128 + x] = colour;
        }

        StampLotus(pixels, 128, 192, 64, 96, 30, GoldBright, Gold);
        return pixels;
    });

    // ------------------------------------------------------------------ banner

    /// <summary>Hanging cloth with the order's lotus, cut out at the bottom.</summary>
    public static Texture2D Banner(GraphicsDevice device) => Get(device, "banner", 96, 160, () =>
    {
        var pixels = new Color[96 * 160];
        var cloth = new Color(126, 46, 40);
        var clothDark = new Color(86, 28, 26);

        for (var y = 0; y < 160; y++)
        for (var x = 0; x < 96; x++)
        {
            var index = y * 96 + x;

            // The swallowtail: two diagonals meeting at the bottom centre.
            var cutFrom = 160 - 26;
            if (y > cutFrom)
            {
                var depth = y - cutFrom;
                var fromEdge = Math.Min(x, 95 - x);
                if (fromEdge > 24 - depth && Math.Abs(x - 48) < depth)
                {
                    pixels[index] = Color.Transparent;
                    continue;
                }
            }

            // Cloth hangs in folds: a slow sine across the width, plus a lit left edge.
            var fold = MathF.Sin(x * 0.16f) * 0.5f + 0.5f;
            var colour = Color.Lerp(clothDark, cloth, fold);
            if (x < 3 || x > 92) colour = Shift(colour, -30);

            pixels[index] = colour;
        }

        StampLotus(pixels, 96, 160, 48, 66, 28, new Color(226, 190, 120), new Color(180, 140, 70));
        return pixels;
    });

    // ------------------------------------------------------------------ flame

    /// <summary>How many frames one loop of the fire is cut into.</summary>
    public const int FlameFrames = 6;

    /// <summary>
    /// A teardrop of fire, in bands, at one point in its cycle.
    ///
    /// Opaque inside its silhouette and fully transparent outside, with no soft rim at all.
    /// That is not a compromise, it is the requirement: <see cref="BillboardRenderer"/> draws
    /// cutouts through AlphaTestEffect so that sprites write depth and sort correctly, and a
    /// soft gradient pushed through a hard alpha test comes out as a stack of steps.
    ///
    /// Fire is motion, and a still sprite cannot be fire however well its palette is chosen.
    /// So the shape is a function of phase and the frames are generated rather than drawn: the
    /// flame leans, its tip whips, and its hot core rises and falls. Six frames is enough
    /// because fire is not periodic to the eye — it only has to stop being still.
    /// </summary>
    public static Texture2D Flame(GraphicsDevice device, int frame = 0)
    {
        var index = ((frame % FlameFrames) + FlameFrames) % FlameFrames;
        return Get(device, "flame" + index, 64, 96, () => BuildFlame(index));
    }

    private static Color[] BuildFlame(int frame)
    {
        var pixels = new Color[64 * 96];

        // Seeded per frame, so the grain that breaks up the bands is different each time and
        // the loop does not read as one image being nudged.
        var random = new Random(0x1F1A + frame * 977);
        var phase = frame / (float)FlameFrames * MathF.PI * 2f;

        var core = new Color(255, 248, 214);
        var mid = new Color(255, 186, 66);
        var outer = new Color(232, 108, 30);
        var rim = new Color(176, 62, 20);

        // Three things move, and they move out of step with each other, which is most of why
        // it reads as fire rather than as a wobbling triangle.
        var lean = MathF.Sin(phase) * 3.1f;
        var whip = MathF.Sin(phase * 2f + 0.9f) * 2.4f;
        var breath = 1f + MathF.Sin(phase * 1.5f + 2.1f) * 0.085f;
        var heatRise = MathF.Sin(phase * 2f) * 0.07f;

        for (var y = 0; y < 96; y++)
        for (var x = 0; x < 64; x++)
        {
            var index = y * 64 + x;

            // 0 at the tip, 1 at the base.
            var t = y / 95f;

            // Full and round at the bottom, drawn to a point at the top.
            var halfWidth = MathF.Pow(t, 0.62f) * 25f * breath * (1f - MathF.Pow(t, 6f) * 0.55f);

            // The lean is strongest at the tip and nothing at the base, because the base is
            // held in the bracket and only the free end of a flame moves.
            var sway = lean * (1f - t) + whip * MathF.Pow(1f - t, 2.2f);
            var axis = 32f + MathF.Sin(t * 2.4f) * 2.6f + sway;
            var dx = MathF.Abs(x - axis);

            if (halfWidth < 0.8f || dx > halfWidth)
            {
                pixels[index] = Color.Transparent;
                continue;
            }

            var edge = dx / halfWidth;
            var heat = (1f - edge) * (0.30f + t * 0.95f + heatRise);
            heat += (float)random.NextDouble() * 0.06f;

            var colour = heat > 0.78f ? core
                : heat > 0.52f ? mid
                : heat > 0.26f ? outer
                : rim;

            pixels[index] = new Color(colour.R, colour.G, colour.B, (byte)255);
        }

        return pixels;
    }

    // ------------------------------------------------------------------ ui frame

    /// <summary>
    /// A nine-slice panel: dark ground, a double gold rule, and a corner ornament.
    ///
    /// Sixteen-pixel corners over a 64-pixel texture, so the middle 32 stretch and the corners
    /// never do. This is the whole difference between the interface reading as a debug overlay
    /// and reading as part of the game.
    /// </summary>
    public const int FrameCorner = 16;

    public static Texture2D Frame(GraphicsDevice device) => Get(device, "frame", 64, 64, () =>
    {
        var pixels = new Color[64 * 64];
        var ground = new Color(28, 22, 18);

        for (var y = 0; y < 64; y++)
        for (var x = 0; x < 64; x++)
        {
            var edge = Math.Min(Math.Min(x, 63 - x), Math.Min(y, 63 - y));

            var colour = edge switch
            {
                0 => GoldDark,
                1 => Gold,
                2 => GoldBright,
                3 => Gold,
                4 => GoldDark,
                5 => Shift(ground, -6),
                _ => ground
            };

            pixels[y * 64 + x] = colour;
        }

        // Corner ornament: a small filled lozenge set inside each corner, so the four corners
        // read as cast metal rather than as a mitre.
        foreach (var (cx, cy) in new[] { (10, 10), (53, 10), (10, 53), (53, 53) })
        for (var y = -5; y <= 5; y++)
        for (var x = -5; x <= 5; x++)
        {
            if (Math.Abs(x) + Math.Abs(y) > 5) continue;

            var px = cx + x;
            var py = cy + y;
            if (px < 0 || py < 0 || px > 63 || py > 63) continue;

            pixels[py * 64 + px] = Math.Abs(x) + Math.Abs(y) > 3 ? GoldDark
                : Math.Abs(x) + Math.Abs(y) > 1 ? Gold
                : GoldBright;
        }

        return pixels;
    });

    // ------------------------------------------------------------------ lotus

    /// <summary>The order's mark, on its own with a transparent ground.</summary>
    public static Texture2D Lotus(GraphicsDevice device) => Get(device, "lotus", 64, 64, () =>
    {
        var pixels = new Color[64 * 64];
        for (var i = 0; i < pixels.Length; i++) pixels[i] = Color.Transparent;
        StampLotus(pixels, 64, 64, 32, 36, 26, GoldBright, Gold);
        return pixels;
    });

    /// <summary>
    /// Draw a lotus into an existing buffer.
    ///
    /// Five petals on an arc plus a centre boss. Each petal is an ellipse rotated about the
    /// base point, which is enough shape to be unmistakable at banner size and still legible
    /// as a mark at 24 pixels on a door.
    /// </summary>
    private static void StampLotus(Color[] pixels, int width, int height,
        int cx, int cy, int radius, Color light, Color dark)
    {
        const int Petals = 5;

        for (var petal = 0; petal < Petals; petal++)
        {
            // Fan across the top half, centre petal upright.
            var angle = -MathF.PI / 2f + (petal - (Petals - 1) / 2f) * 0.62f;
            var sin = MathF.Sin(angle);
            var cos = MathF.Cos(angle);

            var length = radius * (petal == 2 ? 1f : petal % 2 == 0 ? 0.72f : 0.88f);
            var halfWidth = radius * 0.26f;

            for (var y = -radius; y <= radius; y++)
            for (var x = -radius; x <= radius; x++)
            {
                // Into petal space: along the petal, and across it.
                var along = x * cos + y * sin;
                var across = -x * sin + y * cos;

                if (along < 0f || along > length) continue;

                // Petals are pointed, so the half-width tapers to nothing at the tip.
                var taper = MathF.Sin(along / length * MathF.PI);
                var limit = halfWidth * taper;
                if (MathF.Abs(across) > limit) continue;

                var px = cx + x;
                var py = cy + y;
                if (px < 0 || py < 0 || px >= width || py >= height) continue;

                // Outline dark, fill light: a drawn contour is the art direction everywhere
                // else in this game, and the mark should not be the exception.
                var onEdge = MathF.Abs(across) > limit - 1.4f || along > length - 1.6f;
                pixels[py * width + px] = onEdge ? dark : light;
            }
        }

        for (var y = -4; y <= 3; y++)
        for (var x = -6; x <= 6; x++)
        {
            if (x * x * 0.4f + y * y > 12f) continue;

            var px = cx + x;
            var py = cy + y + 2;
            if (px < 0 || py < 0 || px >= width || py >= height) continue;

            pixels[py * width + px] = y < -1 ? light : dark;
        }
    }

    private static Color Shift(Color colour, float amount) => new(
        (byte)Math.Clamp(colour.R + amount, 0, 255),
        (byte)Math.Clamp(colour.G + amount, 0, 255),
        (byte)Math.Clamp(colour.B + amount, 0, 255),
        colour.A);
}
