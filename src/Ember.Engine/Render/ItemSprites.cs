using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Ember.Render;

/// <summary>
/// Every item icon in the game, built out of <see cref="SpriteForge"/> shapes.
///
/// This is the part that was said to be impossible without an artist, so it is worth being
/// precise about why it is not. An icon is a small number of parts in a fixed arrangement,
/// each made of one material — a haft, a head, a socket, a blade. That is a description, and a
/// description is code. What genuinely cannot be generated is anything whose appeal comes from
/// an artist's *choices* rather than its construction: a face, a pose, a creature with
/// character. Those are still hand work, and nothing here pretends otherwise.
///
/// One light direction for the whole file, so every icon in the inventory agrees about where
/// the lamp is. Getting that wrong across a hand-drawn set takes constant discipline; here it
/// is a constant.
/// </summary>
public static class ItemSprites
{
    /// <summary>Upper left, slightly toward the viewer. The convention for every icon.</summary>
    private static readonly Vector3 Key = new(-0.55f, -0.68f, 0.5f);

    private static readonly Dictionary<string, Texture2D> Cache = new(StringComparer.Ordinal);

    public static void Clear()
    {
        foreach (var texture in Cache.Values) texture.Dispose();
        Cache.Clear();
    }

    // ------------------------------------------------------------------ materials

    private static readonly SpriteMaterial Iron = new()
    {
        Ramp = new[]
        {
            new Color(38, 40, 52), new Color(60, 64, 78), new Color(88, 93, 107),
            new Color(124, 130, 143), new Color(163, 170, 181), new Color(206, 212, 220)
        },
        Outline = new Color(20, 21, 28),
        Gloss = 0.75f,
        Highlight = new Color(246, 250, 255)
    };

    private static readonly SpriteMaterial Wood = new()
    {
        Ramp = new[]
        {
            new Color(46, 30, 20), new Color(72, 47, 29), new Color(101, 68, 40),
            new Color(130, 90, 54), new Color(158, 114, 72)
        },
        Outline = new Color(28, 18, 12)
    };

    private static readonly SpriteMaterial Gold = new()
    {
        Ramp = new[]
        {
            new Color(92, 58, 18), new Color(134, 92, 28), new Color(179, 132, 42),
            new Color(217, 173, 66), new Color(240, 208, 108), new Color(255, 238, 176)
        },
        Outline = new Color(56, 34, 10),
        Gloss = 0.9f,
        Highlight = new Color(255, 250, 214)
    };

    private static readonly SpriteMaterial Crystal = new()
    {
        Ramp = new[]
        {
            new Color(58, 30, 96), new Color(88, 46, 138), new Color(122, 68, 182),
            new Color(158, 102, 214), new Color(196, 148, 238), new Color(232, 206, 252)
        },
        Outline = new Color(38, 18, 62),
        Gloss = 1.15f,
        Highlight = Color.White
    };

    private static readonly SpriteMaterial Leather = new()
    {
        Ramp = new[]
        {
            new Color(36, 24, 22), new Color(58, 38, 32), new Color(82, 55, 44),
            new Color(107, 74, 58)
        },
        Outline = new Color(22, 14, 12)
    };

    private static Texture2D Get(GraphicsDevice device, string key, int size, Action<SpriteForge> build)
    {
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var forge = new SpriteForge(size, size);
        build(forge);

        var texture = new Texture2D(device, size, size);
        texture.SetData(forge.Resolve(Key));
        Cache[key] = texture;
        return texture;
    }

    // ------------------------------------------------------------------ items

    public static Texture2D Pickaxe(GraphicsDevice device) => Get(device, "pickaxe", 64, forge =>
    {
        // Haft first, lying corner to corner, thick enough to read as a pole.
        forge.Begin();
        forge.Capsule(17f, 52f, 44f, 14f, 3.1f, 2.6f);
        forge.Fill(Wood, roundness: 1.15f, cap: 3.2f);

        // Binding where the head is lashed on.
        forge.Begin();
        forge.Capsule(37f, 24f, 42f, 17f, 3.6f, 3.2f);
        forge.Fill(Leather, roundness: 1.2f, cap: 3f, lift: 1.4f);

        // The head: two swept horns meeting at the socket. Drawn as tapered capsules because a
        // pick horn is exactly that — thick at the eye, drawn to a point.
        forge.Begin();
        forge.Capsule(41f, 16f, 15f, 22f, 4.4f, 0.9f);
        forge.Capsule(41f, 16f, 57f, 33f, 4.2f, 0.9f);
        forge.Fill(Iron, roundness: 0.95f, cap: 4.6f, lift: 2.2f);

        // Grip at the base.
        forge.Begin();
        forge.Capsule(18f, 51f, 24f, 43f, 3.5f, 3.2f);
        forge.Fill(Leather, roundness: 1.2f, cap: 3f, lift: 1.6f);
    });

    public static Texture2D Sword(GraphicsDevice device) => Get(device, "sword", 64, forge =>
    {
        // Blade: long and narrow. The first pass was as wide as a cleaver over half the height
        // of the frame, which reads as a dagger however well it is lit — proportion decides
        // what an icon *is* long before shading decides how good it looks.
        forge.Begin();
        forge.Polygon(new Vector2(32f, 3f), new Vector2(37f, 12f), new Vector2(37f, 41f),
            new Vector2(27f, 41f), new Vector2(27f, 12f));
        forge.Fill(Iron, roundness: 0.7f, cap: 2.8f);

        // The fuller: a raised spine down the centre, so the light breaks along the blade
        // rather than washing evenly across it.
        forge.Begin();
        forge.Capsule(32f, 9f, 32f, 39f, 1.3f, 1.7f);
        forge.Fill(Iron, roundness: 1.5f, cap: 4.4f, lift: 1.0f);

        forge.Begin();
        forge.Capsule(14f, 44f, 50f, 44f, 2.8f, 2.8f);
        forge.Ellipse(32f, 44f, 5.0f, 3.6f);
        forge.Fill(Gold, roundness: 1.2f, cap: 3.0f, lift: 3.6f);

        forge.Begin();
        forge.Capsule(32f, 47f, 32f, 56f, 2.7f, 2.5f);
        forge.Fill(Leather, roundness: 1.3f, cap: 2.8f, lift: 3.8f);

        forge.Begin();
        forge.Ellipse(32f, 58f, 4.4f, 4.0f);
        forge.Fill(Gold, roundness: 1.35f, cap: 4.6f, lift: 4.0f);
    });

    public static Texture2D JivaCrystal(GraphicsDevice device) => Get(device, "jiva", 64, forge =>
    {
        // Facets are committed flat — roundness zero — because a cut stone is planes meeting at
        // edges, and rounding turns it into a pebble. Each shard sits at its own lift, and the
        // steps between those lifts are what make them read as separate stones rather than as
        // one lumpy mass.
        //
        // The shards are drawn back to front and none of them overlap in plan. The first pass
        // had the short shard crossing the tall one, which left a notch where the outline of
        // the near shard cut into the far one.
        forge.Begin();
        forge.Polygon(new Vector2(20f, 24f), new Vector2(27f, 31f),
            new Vector2(26f, 53f), new Vector2(17f, 53f), new Vector2(15f, 33f));
        forge.Fill(Crystal, roundness: 0f, cap: 3.2f, lift: 3.2f);

        forge.Begin();
        forge.Polygon(new Vector2(44f, 27f), new Vector2(50f, 35f),
            new Vector2(48f, 53f), new Vector2(40f, 53f), new Vector2(39f, 34f));
        forge.Fill(Crystal, roundness: 0f, cap: 4.0f, lift: 4.0f);

        // The tall one, last and highest, so it owns every pixel it shares.
        forge.Begin();
        forge.Polygon(new Vector2(33f, 9f), new Vector2(41f, 22f),
            new Vector2(39f, 54f), new Vector2(27f, 54f), new Vector2(26f, 22f));
        forge.Fill(Crystal, roundness: 0f, cap: 6.2f, lift: 6.2f);

        // A bright inner sliver, wholly inside the tall shard's outline: the light the stone
        // holds rather than the light falling on it.
        forge.Begin();
        forge.Polygon(new Vector2(34f, 19f), new Vector2(36f, 26f),
            new Vector2(35f, 46f), new Vector2(32f, 46f), new Vector2(32f, 25f));
        forge.Fill(new SpriteMaterial
        {
            Ramp = new[] { new Color(206, 168, 248), new Color(236, 214, 255), Color.White },
            Outline = new Color(150, 104, 208)
        }, roundness: 0f, cap: 7.0f, lift: 7.0f);
    });

    public static Texture2D GoldBars(GraphicsDevice device) => Get(device, "goldbars", 64, forge =>
    {
        // Two on the bottom, one on top. Each bar is a trapezium — wider at its base — so the
        // stack reads as ingots rather than as bricks.
        void Bar(float cx, float cy, float lift)
        {
            forge.Begin();
            forge.Polygon(
                new Vector2(cx - 13f, cy + 6f), new Vector2(cx + 13f, cy + 6f),
                new Vector2(cx + 10f, cy - 6f), new Vector2(cx - 10f, cy - 6f));
            forge.Fill(Gold, roundness: 0.85f, cap: 4.2f, lift: lift);
        }

        Bar(20f, 46f, 0.4f);
        Bar(44f, 46f, 0.4f);
        Bar(32f, 32f, 4.6f);
    });

    /// <summary>
    /// One risen figure, at whatever tier the mountain has made of it.
    ///
    /// Three creatures out of one description, because that is what they are in the fiction: a
    /// chhaya is what is left of a miner, a vetala kept enough of itself to be deliberate, and
    /// a pishacha is something taken long before there was a town. They differ by mass, by
    /// posture, and by how much of the stone still burns in them.
    ///
    /// This is the case that was supposed to need an artist, and the honest limit is narrower
    /// than that. Construction gets a figure correctly built and correctly lit at any scale.
    /// What it does not get is *intent* — the tilt of a head that reads as grief rather than as
    /// a head at an angle. For enemies meant to be barely-people that gap is small. It would
    /// not be, for a named character.
    /// </summary>
    private sealed record RisenBuild(
        float Scale,
        float ShoulderWidth,
        float Stoop,
        Color Flesh,
        Color Ember,
        int Stones);

    private static readonly RisenBuild Chhaya =
        new(0.88f, 6.6f, 0f, new Color(54, 72, 78), new Color(214, 146, 52), 1);

    private static readonly RisenBuild Vetala =
        new(1.04f, 8.4f, 1.6f, new Color(64, 66, 92), new Color(120, 198, 236), 2);

    private static readonly RisenBuild Pishacha =
        new(1.2f, 11.2f, 3.4f, new Color(78, 54, 52), new Color(255, 122, 46), 3);

    public static Texture2D ChhayaSprite(GraphicsDevice device) =>
        Get(device, "risen.chhaya", 64, forge => Risen(forge, Chhaya));

    public static Texture2D VetalaSprite(GraphicsDevice device) =>
        Get(device, "risen.vetala", 64, forge => Risen(forge, Vetala));

    public static Texture2D PishachaSprite(GraphicsDevice device) =>
        Get(device, "risen.pishacha", 64, forge => Risen(forge, Pishacha));

    /// <summary>The sprite for an enemy archetype, or null if it is not one of the risen.</summary>
    public static Texture2D? Risen(GraphicsDevice device, string archetypeId) => archetypeId switch
    {
        "chhaya" or "preta" => ChhayaSprite(device),
        "vetala" => VetalaSprite(device),
        "pishacha" => PishachaSprite(device),
        _ => null
    };

    private static void Risen(SpriteForge forge, RisenBuild build)
    {
        var flesh = SpriteMaterial.FromBase(build.Flesh);

        var ember = new SpriteMaterial
        {
            Ramp = new[]
            {
                Shade(build.Ember, -0.35f), build.Ember, Shade(build.Ember, 0.45f)
            },
            Outline = Shade(build.Ember, -0.62f),
            Gloss = 1.2f,
            Highlight = Color.White
        };

        var stone = SpriteMaterial.FromBase(new Color(150, 96, 206), gloss: 1.1f);

        // Everything is measured from the centre line and scaled about the feet, so a heavier
        // tier grows upward and outward from where it stands rather than floating.
        const float Mid = 32f;
        const float Ground = 61f;

        float Y(float from) => Ground - (Ground - from) * build.Scale;
        float W(float w) => w * build.Scale;

        var shoulder = W(build.ShoulderWidth);
        var lean = build.Stoop;

        // Legs dissolve before they reach the ground: the lower body tapers to nothing, which
        // is cheaper than feet and the correct read for something that is not quite here.
        forge.Begin();
        forge.Capsule(Mid - 2f, Y(43f), Mid - 5f, Y(61f), W(4.9f), 0.6f);
        forge.Capsule(Mid + 2f, Y(43f), Mid + 6f, Y(59f), W(4.7f), 0.6f);
        forge.Fill(flesh, roundness: 0.85f, cap: 4.4f);

        // Torso. The stoop pushes the shoulders forward and down, which is most of what makes
        // the heavy tier read as heavy rather than as the small one enlarged.
        forge.Begin();
        forge.Capsule(Mid, Y(30f) + lean, Mid, Y(45f), shoulder, W(5.8f));
        forge.Fill(flesh, roundness: 0.7f, cap: 5.6f, lift: 1.2f);

        // Arms, one hanging and one half-raised. They hang further from the body as the figure
        // widens, or the silhouette closes up and the whole thing becomes a barrel.
        forge.Begin();
        forge.Capsule(Mid - shoulder * 0.78f, Y(31f) + lean, Mid - shoulder - W(1f), Y(47f),
            W(2.9f), W(1.9f));
        forge.Capsule(Mid + shoulder * 0.78f, Y(31f) + lean, Mid + shoulder + W(2f), Y(38f),
            W(2.9f), W(1.9f));
        forge.Fill(flesh, roundness: 1.0f, cap: 3.6f, lift: 2.4f);

        forge.Begin();
        forge.Capsule(Mid, Y(19f) + lean, Mid, Y(31f) + lean, W(2.3f), W(2.9f));
        forge.Fill(flesh, roundness: 1.1f, cap: 3.0f, lift: 1.6f);

        // A skull is an ellipse with the jaw drawn in and the crown kept narrow.
        var headY = Y(12f) + lean;
        forge.Begin();
        forge.Ellipse(Mid, headY, W(5.6f), W(6.6f));
        forge.Erase(Mid, headY + W(7.5f), W(4.6f), W(2.6f));
        forge.Fill(flesh, roundness: 0.95f, cap: 6.0f, lift: 3.4f);

        // The eyes: the only warm thing on it, and the reason it reads as looking at you. They
        // widen with the tier, because that is the cheapest way to make a bigger thing scarier.
        var eye = W(1.6f) * (1f + build.Stones * 0.12f);
        forge.Begin();
        forge.Ellipse(Mid - W(2.3f), headY, eye, eye * 1.25f);
        forge.Ellipse(Mid + W(2.3f), headY, eye, eye * 1.25f);
        forge.Fill(ember, roundness: 1.6f, cap: 4f, lift: 7.6f);

        // The stones still lodged in it, which are what hold it here. More of them, higher up,
        // the older the thing is.
        for (var i = 0; i < build.Stones; i++)
        {
            var offset = (i - (build.Stones - 1) / 2f) * W(4.6f);
            var y = Y(35f + i % 2 * 4f) + lean;

            forge.Begin();
            forge.Polygon(
                new Vector2(Mid + offset, y - W(3.4f)),
                new Vector2(Mid + offset + W(2.6f), y),
                new Vector2(Mid + offset, y + W(4.2f)),
                new Vector2(Mid + offset - W(2.6f), y));
            forge.Fill(stone, roundness: 0f, cap: 7.4f, lift: 7.4f);
        }
    }

    private static Color Shade(Color colour, float amount) => amount < 0f
        ? Color.Lerp(colour, new Color(20, 14, 26), -amount)
        : Color.Lerp(colour, Color.White, amount);
}
