using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Ember.Render;

/// <summary>
/// Tileable stone, generated rather than painted.
///
/// The same argument as <see cref="StambhaCarving"/> and the character sprites: a texture that is
/// a function of a palette and some numbers costs no artist time, varies per cave theme by
/// changing three colours, and cannot go missing from a build. A hand-drawn tileset is better
/// looking and is five times the work at five themes.
///
/// Everything here is authored to tile: the block courses wrap in X, and nothing samples outside
/// its own cell. A seam is the one defect that makes generated texture look generated.
/// </summary>
public static class StoneTextures
{
    /// <summary>
    /// Texture resolution. 256 at roughly two metres per tile puts a texel near a centimetre,
    /// which is about where the mortar lines stop looking like drawn lines and start looking
    /// like gaps.
    /// </summary>
    private const int Size = 256;

    private static readonly Dictionary<string, Texture2D> Cache = new(StringComparer.Ordinal);

    /// <summary>One cave theme's stone, in three colours and nothing else.</summary>
    public readonly record struct StonePalette(string Id, Color Base, Color Mortar, Color Accent)
    {
        /// <summary>The default: cold grey blockwork, the mine as it is authored today.</summary>
        public static readonly StonePalette Granite =
            new("granite", new Color(86, 86, 92), new Color(34, 33, 36), new Color(120, 118, 122));

        /// <summary>
        /// Warm sandstone, for the yard above the mines.
        ///
        /// Coming up out of a mine has to look like arriving somewhere, and the same cold grey
        /// blockwork under daylight simply reads as another room with a sky over it. Warmth is
        /// doing the work the fiction needs at the cheapest possible price.
        /// </summary>
        public static readonly StonePalette Sandstone =
            new("sandstone", new Color(146, 122, 92), new Color(96, 78, 58), new Color(178, 152, 116));
    }

    public static void Clear()
    {
        foreach (var texture in Cache.Values) texture.Dispose();
        Cache.Clear();
    }

    /// <summary>Coursed blockwork for walls.</summary>
    public static Texture2D Wall(GraphicsDevice device, StonePalette palette)
        => Get(device, "wall:" + palette.Id, () => BuildWall(palette));

    /// <summary>Irregular flagstones for floors.</summary>
    public static Texture2D Floor(GraphicsDevice device, StonePalette palette)
        => Get(device, "floor:" + palette.Id, () => BuildFloor(palette));

    /// <summary>Sawn planks, for stall counters, posts, awning frames and winch timber.</summary>
    public static Texture2D Timber(GraphicsDevice device)
        => Get(device, "timber", BuildTimber);

    /// <summary>Coarse woven cloth, for awnings and hangings.</summary>
    public static Texture2D Cloth(GraphicsDevice device)
        => Get(device, "cloth", BuildCloth);

    /// <summary>Packed earth: no courses, no mortar, just grit and a few stones.</summary>
    public static Texture2D Earth(GraphicsDevice device)
        => Get(device, "earth", BuildEarth);

    /// <summary>Opaque ripple, for pools and canals. Not transparent water.</summary>
    public static Texture2D Water(GraphicsDevice device)
        => Get(device, "water", BuildWater);

    /// <summary>Laid fibre with a twist, for the windlass rope.</summary>
    public static Texture2D Rope(GraphicsDevice device)
        => Get(device, "rope", BuildRope);

    /// <summary>
    /// A soft radial falloff, drawn additively to fake the pool of light a torch throws.
    ///
    /// BasicEffect has no point lights, and the mine is generated so its lighting cannot be
    /// baked. Until there is a custom effect, a glow quad on the surface behind the flame is
    /// what puts light on a wall, and at this art scale it is very nearly indistinguishable.
    /// </summary>
    public static Texture2D Glow(GraphicsDevice device)
        => Get(device, "glow", BuildGlow);

    private static Texture2D Get(GraphicsDevice device, string key, Func<Color[]> build)
    {
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var texture = new Texture2D(device, Size, Size);
        texture.SetData(build());
        Cache[key] = texture;
        return texture;
    }

    // ------------------------------------------------------------------ walls

    private const int CourseHeight = 32;
    private const int BlockWidth = 64;
    private const int Mortar = 3;

    private static Color[] BuildWall(StonePalette palette)
    {
        var pixels = new Color[Size * Size];
        var random = new Random(palette.Id.GetHashCode() ^ 0x5711);

        // Per-block tone, picked once so a block is one stone rather than a field of noise.
        var courses = Size / CourseHeight;
        var perCourse = Size / BlockWidth;
        var tone = new float[courses, perCourse + 1];
        for (var c = 0; c < courses; c++)
        for (var b = 0; b <= perCourse; b++)
            tone[c, b] = (float)random.NextDouble();

        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            var course = y / CourseHeight;
            var rowY = y % CourseHeight;

            // Every other course steps half a block, the way a wall is actually laid. The
            // offset wraps with the texture, so the tiling seam falls inside a block rather
            // than along its edge.
            var offset = (course % 2 == 0) ? 0 : BlockWidth / 2;
            var shifted = (x + offset) % Size;
            var block = shifted / BlockWidth;
            var colX = shifted % BlockWidth;

            var index = y * Size + x;

            if (rowY < Mortar || colX < Mortar)
            {
                pixels[index] = Jitter(palette.Mortar, random, 5);
                continue;
            }

            var shade = MathHelper.Lerp(-14f, 16f, tone[course, block]);

            // A cut face is lit from above: the top of a block catches, the bottom sits in the
            // shadow of the course over it. Two pixels of each is the whole bevel, and it is
            // what stops flat colour reading as wallpaper.
            if (rowY < Mortar + 2) shade += 22f;
            else if (rowY > CourseHeight - 3) shade -= 18f;
            if (colX < Mortar + 2) shade += 10f;
            else if (colX > BlockWidth - 3) shade -= 10f;

            var grain = random.Next(-7, 8);
            pixels[index] = Shift(palette.Base, shade + grain);
        }

        Pit(pixels, palette, random, count: 90);
        return pixels;
    }

    // ------------------------------------------------------------------ floor

    private static Color[] BuildFloor(StonePalette palette)
    {
        var pixels = new Color[Size * Size];
        var random = new Random(palette.Id.GetHashCode() ^ 0x21A7);

        const int flag = 42;
        var cells = Size / flag;
        var tone = new float[cells + 1, cells + 1];
        for (var a = 0; a <= cells; a++)
        for (var b = 0; b <= cells; b++)
            tone[a, b] = (float)random.NextDouble();

        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            var cx = x / flag;
            var cy = y / flag;
            var inX = x % flag;
            var inY = y % flag;
            var index = y * Size + x;

            if (inX < 2 || inY < 2)
            {
                pixels[index] = Jitter(palette.Mortar, random, 4);
                continue;
            }

            // Floors are walked on: less bevel than a wall, more wear toward the middle of
            // each flag.
            var shade = MathHelper.Lerp(-16f, 12f, tone[cy, cx]);
            if (inY < 4) shade += 10f;
            else if (inY > flag - 4) shade -= 10f;

            var grain = random.Next(-6, 7);
            pixels[index] = Shift(palette.Base, shade + grain - 10f);
        }

        Pit(pixels, palette, random, count: 140);
        return pixels;
    }

    // ------------------------------------------------------------------ other materials

    /// <summary>
    /// Sawn planks running the length of the tile.
    ///
    /// Timber is the material the yard needed most: every post, counter and beam in the camp
    /// was drawn as coursed blockwork, so the stall read as a brick bench and the winch over
    /// the shaft read as brick posts. Grain runs one way and the boards are unequal widths,
    /// which is most of what separates a plank from a stripe.
    /// </summary>
    private static Color[] BuildTimber()
    {
        var pixels = new Color[Size * Size];
        var random = new Random(0x7100BE);

        var body = new Color(150, 108, 66);
        var gap = new Color(58, 40, 26);

        // Board edges at uneven intervals, so the eye does not find a repeat.
        var edges = new List<int> { 0 };
        for (var y = random.Next(24, 40); y < Size; y += random.Next(26, 46)) edges.Add(y);
        edges.Add(Size);

        for (var board = 0; board < edges.Count - 1; board++)
        {
            var top = edges[board];
            var bottom = edges[board + 1];
            var tone = random.Next(-14, 15);

            for (var y = top; y < bottom; y++)
            for (var x = 0; x < Size; x++)
            {
                var index = y * Size + x;

                if (y - top < 2)
                {
                    pixels[index] = Jitter(gap, random, 5);
                    continue;
                }

                // Long grain: a slow ripple along the board, plus fine noise across it.
                var grain = MathF.Sin((x * 0.11f) + board * 2.3f) * 5f
                    + MathF.Sin(x * 0.031f + y * 0.7f) * 3f;
                var shade = tone + grain + random.Next(-4, 5);

                // A darker line near each edge reads as the round of a sawn board.
                var toEdge = MathF.Min(y - top, bottom - 1 - y);
                if (toEdge < 4) shade -= (4 - toEdge) * 3f;

                pixels[index] = Shift(body, shade);
            }
        }

        // Knots. Two or three per tile is plenty; more reads as damage.
        for (var knot = 0; knot < 3; knot++)
        {
            var cx = random.Next(Size);
            var cy = random.Next(Size);
            var radius = random.Next(4, 8);

            for (var y = cy - radius; y <= cy + radius; y++)
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || y < 0 || x >= Size || y >= Size) continue;

                var dx = (x - cx) / (float)radius;
                var dy = (y - cy) / (float)radius;
                var falloff = 1f - MathF.Min(1f, MathF.Sqrt(dx * dx + dy * dy));
                if (falloff <= 0f) continue;

                pixels[y * Size + x] = Shift(pixels[y * Size + x], -34f * falloff);
            }
        }

        return pixels;
    }

    /// <summary>A coarse over-under weave. Close up it is threads; at stall size it is canvas.</summary>
    private static Color[] BuildCloth()
    {
        var pixels = new Color[Size * Size];
        var random = new Random(0xC107F);
        var body = new Color(158, 92, 78);

        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            // Which thread is on top alternates in both directions, four texels to a thread.
            var overUnder = (x / 4 + y / 4) % 2 == 0;
            var alongThread = overUnder ? y % 4 : x % 4;

            var shade = overUnder ? 8f : -8f;
            shade += alongThread is 0 or 3 ? -6f : 3f;

            // A slow sag across the weave, so a large panel is not flat.
            shade += MathF.Sin(x * 0.02f) * 4f + MathF.Sin(y * 0.017f) * 4f;

            pixels[y * Size + x] = Shift(body, shade + random.Next(-3, 4));
        }

        return pixels;
    }

    /// <summary>
    /// Packed earth: grit, a few trodden stones, and no courses at all.
    ///
    /// The yard floor was flagstones, which made the camp read as a paved room with a sky
    /// over it rather than as ground somebody dug a hole in.
    /// </summary>
    /// <summary>
    /// Three laid strands, twisting.
    ///
    /// The twist is the whole thing: a rope drawn as vertical fibres reads as a bundle of
    /// wires. Shearing the strand boundaries across the height gives the diagonal banding the
    /// eye actually uses to tell rope from cable, and it costs one term.
    /// </summary>
    private static Color[] BuildRope()
    {
        var pixels = new Color[Size * Size];
        var random = new Random(0x120BE);

        var body = new Color(156, 138, 100);
        var shade = new Color(96, 82, 56);

        const int Strands = 3;
        var strandWidth = Size / (float)Strands;

        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            // One full turn of the lay over the height of the tile, so it repeats seamlessly.
            var twisted = (x + y * (Strands / (float)Size) * Size / Strands) % Size;
            var within = twisted % strandWidth / strandWidth;

            // Round the strand: dark at its edges, lit along its centre.
            var round = 1f - MathF.Abs(within - 0.5f) * 2f;
            var lit = MathF.Pow(round, 0.6f);

            var colour = new Color(
                (int)(shade.R + (body.R - shade.R) * lit),
                (int)(shade.G + (body.G - shade.G) * lit),
                (int)(shade.B + (body.B - shade.B) * lit));

            pixels[y * Size + x] = Jitter(colour, random, 9);
        }

        return pixels;
    }

    private static Color[] BuildEarth()
    {
        var pixels = new Color[Size * Size];
        var random = new Random(0xEA27D);
        var body = new Color(132, 112, 84);

        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            // Broad patches of damp and dry, then grit on top of it.
            var patch = MathF.Sin(x * 0.021f + y * 0.013f) * 7f
                + MathF.Sin(x * 0.007f - y * 0.031f) * 5f;

            pixels[y * Size + x] = Shift(body, patch + random.Next(-9, 10));
        }

        // Small stones trodden into the surface.
        for (var stone = 0; stone < 90; stone++)
        {
            var cx = random.Next(Size);
            var cy = random.Next(Size);
            var radius = random.Next(1, 4);
            var lighter = random.Next(2) == 0;

            for (var y = cy - radius; y <= cy + radius; y++)
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || y < 0 || x >= Size || y >= Size) continue;

                var dx = (x - cx) / (float)radius;
                var dy = (y - cy) / (float)radius;
                if (dx * dx + dy * dy > 1f) continue;

                pixels[y * Size + x] = Shift(pixels[y * Size + x], lighter ? 22f : -20f);
            }
        }

        return pixels;
    }

    // ------------------------------------------------------------------ shared

    /// <summary>Chips and pocks, so no two square metres of the tile read as identical.</summary>
    private static void Pit(Color[] pixels, StonePalette palette, Random random, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var cx = random.Next(Size);
            var cy = random.Next(Size);
            var radius = random.Next(1, 5);
            var darken = random.Next(10, 30);

            for (var y = cy - radius; y <= cy + radius; y++)
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                var dx = (x - cx) / (float)radius;
                var dy = (y - cy) / (float)radius;
                if (dx * dx + dy * dy > 1f) continue;

                // Wrap rather than clip, or every chip near an edge becomes a visible seam.
                var wx = ((x % Size) + Size) % Size;
                var wy = ((y % Size) + Size) % Size;
                var index = wy * Size + wx;

                pixels[index] = Shift(pixels[index], -darken);
            }
        }
    }

    private static Color[] BuildWater()
    {
        var pixels = new Color[Size * Size];
        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            var u = x / (float)Size;
            var v = y / (float)Size;
            var wave = MathF.Sin((u * 8f + v * 2.5f) * MathF.Tau) * 0.5f
                + MathF.Sin((u * 2.5f - v * 10f) * MathF.Tau) * 0.35f;
            var t = MathHelper.Clamp(0.52f + wave * 0.24f, 0.22f, 0.95f);
            pixels[y * Size + x] = new Color(
                (byte)(32 + t * 36),
                (byte)(88 + t * 72),
                (byte)(148 + t * 96));
        }

        return pixels;
    }

    private static Color[] BuildGlow()
    {
        var pixels = new Color[Size * Size];
        const float centre = Size / 2f;

        for (var y = 0; y < Size; y++)
        for (var x = 0; x < Size; x++)
        {
            var dx = (x - centre) / centre;
            var dy = (y - centre) / centre;
            var distance = MathF.Sqrt(dx * dx + dy * dy);

            // Squared falloff, hard zero at the edge so the quad has no visible boundary.
            var strength = MathHelper.Clamp(1f - distance, 0f, 1f);
            strength *= strength;

            var value = (byte)(strength * 255f);
            pixels[y * Size + x] = new Color(value, value, value, value);
        }

        return pixels;
    }

    private static Color Shift(Color colour, float amount) => new(
        (byte)Math.Clamp(colour.R + amount, 0, 255),
        (byte)Math.Clamp(colour.G + amount, 0, 255),
        (byte)Math.Clamp(colour.B + amount, 0, 255));

    private static Color Jitter(Color colour, Random random, int spread) =>
        Shift(colour, random.Next(-spread, spread + 1));
}
