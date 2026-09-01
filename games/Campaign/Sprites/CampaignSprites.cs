using Ember.Render;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Campaign;

public enum Facing
{
    Front,
    Side,
    Back
}

/// <summary>
/// Campaign catalogues. Ratna Bay names stay in the engine as recipes; these are the game's.
/// Generated once in LoadContent.
/// </summary>
public sealed class CampaignSprites
{
    private static readonly Vector3 Key = new(-0.55f, -0.68f, 0.5f);

    private readonly Dictionary<string, Texture2D> _cache = new(StringComparer.Ordinal);
    private readonly GraphicsDevice _device;

    public CampaignSprites(GraphicsDevice device)
    {
        _device = device;
        BuildPeople();
        BuildFlats();
        BuildHorse();
        BuildIcons();
    }

    public Texture2D Get(string key, float cameraYaw, float facingYaw)
    {
        if (key is "pine" or "oak" or "palm" or "boulder" or "mouth" or "sword" or "key" or "mappin"
            or "wagon" or "fire" or "horse-ride")
            return Take(key);

        if (key.StartsWith("board:", StringComparison.Ordinal))
            return Take(key);

        var facing = FacingOf(cameraYaw, facingYaw);
        var faced = $"{key}:{facing}";
        if (_cache.ContainsKey(faced)) return _cache[faced];
        return Take(key);
    }

    public Texture2D Icon(string key) => Take(key);

    public Texture2D Board(string title, string bearing)
    {
        var key = $"board:{title}|{bearing}";
        if (_cache.TryGetValue(key, out var texture)) return texture;
        texture = BakeBoard(_device, title, bearing);
        _cache[key] = texture;
        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in _cache.Values) texture.Dispose();
        _cache.Clear();
    }

    private Texture2D Take(string key)
    {
        if (_cache.TryGetValue(key, out var texture)) return texture;
        return _cache["traveler:Front"];
    }

    private static Facing FacingOf(float cameraYaw, float facingYaw)
    {
        var delta = facingYaw - cameraYaw;
        while (delta > MathF.PI) delta -= MathF.Tau;
        while (delta < -MathF.PI) delta += MathF.Tau;
        var abs = MathF.Abs(delta);
        if (abs < 0.7f) return Facing.Front;
        if (abs > 2.4f) return Facing.Back;
        return Facing.Side;
    }

    private void BuildPeople()
    {
        BakePerson("traveler", Palettes.Traveler);
        BakePerson("watch", Palettes.Watch);
        BakePerson("trader", Palettes.Trader);
        BakePerson("innkeep", Palettes.Innkeep);
        BakePerson("dummy", Palettes.Dummy);
        BakePerson("fighter", Palettes.Fighter);
        BakePerson("mage", Palettes.Mage);
        BakePerson("priest", Palettes.Priest);
        BakePerson("thief", Palettes.Thief);
        BakePerson("bandit", CharacterPalette.Bandit);
        BakePerson("wolf", CharacterPalette.Wolf);
        BakePerson("wisp", Palettes.Wisp);
    }

    private void BakePerson(string key, CharacterPalette palette)
    {
        foreach (var facing in new[] { Facing.Front, Facing.Side, Facing.Back })
            _cache[$"{key}:{facing}"] = BuildPerson(_device, palette, facing);
        _cache[key] = _cache[$"{key}:{Facing.Front}"];
    }

    private void BuildFlats()
    {
        _cache["pine"] = BuildPine(_device);
        _cache["oak"] = BuildOak(_device);
        _cache["palm"] = BuildPalm(_device);
        _cache["boulder"] = BuildBoulder(_device);
        _cache["mouth"] = BuildMouth(_device);
        _cache["fire"] = BakeFire(_device);
    }

    private void BuildHorse()
    {
        foreach (var facing in new[] { Facing.Front, Facing.Side, Facing.Back })
            _cache[$"horse:{facing}"] = BakeHorse(_device, facing);
        _cache["horse"] = _cache[$"horse:{Facing.Side}"];
        _cache["horse-ride"] = BakeHorseRide(_device);
        _cache["wagon"] = BakeWagon(_device);
    }

    private void BuildIcons()
    {
        _cache["sword"] = BuildSword(_device);
        _cache["key"] = BuildKey(_device);
        _cache["mappin"] = BuildMapPin(_device);
    }

    private static Texture2D Pixels(GraphicsDevice device, int width, int height, Color[] data)
    {
        var texture = new Texture2D(device, width, height);
        texture.SetData(data);
        return texture;
    }

    private static Texture2D BuildPerson(GraphicsDevice device, CharacterPalette palette, Facing facing)
    {
        const int Width = 32;
        const int Height = 48;
        var forge = new SpriteForge(Width, Height);
        var skin = SpriteMaterial.FromBase(palette.Skin);
        var hair = SpriteMaterial.FromBase(palette.Hair);
        var garment = SpriteMaterial.FromBase(palette.Garment);
        var trim = SpriteMaterial.FromBase(palette.Trim, gloss: 0.4f);
        var boots = SpriteMaterial.FromBase(palette.Boots);

        var cx = facing == Facing.Side ? 17f : 16f;

        forge.Begin();
        if (facing == Facing.Side)
        {
            forge.Capsule(cx, 30f, cx + 0.4f, 41f, 2.6f, 2.2f);
        }
        else
        {
            forge.Capsule(13.2f, 30f, 12.8f, 41f, 2.5f, 2.2f);
            forge.Capsule(18.8f, 30f, 19.2f, 41f, 2.5f, 2.2f);
        }

        forge.Fill(trim, roundness: 1.0f, cap: 2.6f);

        forge.Begin();
        if (facing == Facing.Side)
        {
            forge.Ellipse(cx + 0.3f, 43.5f, 3.2f, 2.6f);
        }
        else
        {
            forge.Ellipse(12.6f, 43.5f, 3.2f, 2.6f);
            forge.Ellipse(19.4f, 43.5f, 3.2f, 2.6f);
        }

        forge.Fill(boots, roundness: 1.0f, cap: 2.8f, lift: 0.5f);

        forge.Begin();
        forge.Capsule(cx, 17f, cx, 30f, facing == Facing.Side ? 5.2f : 6.6f, facing == Facing.Side ? 4.2f : 4.9f);
        forge.Fill(garment, roundness: 0.62f, cap: 4.6f, lift: 1.4f);

        forge.Begin();
        if (facing == Facing.Side)
        {
            forge.Capsule(cx + 4.2f, 17.5f, cx + 6.4f, 28f, 2.3f, 1.8f);
        }
        else if (facing == Facing.Back)
        {
            forge.Capsule(10.4f, 17.5f, 8.6f, 28f, 2.3f, 1.8f);
            forge.Capsule(21.6f, 17.5f, 23.4f, 28f, 2.3f, 1.8f);
        }
        else
        {
            forge.Capsule(10.4f, 17.5f, 8.6f, 28f, 2.3f, 1.8f);
            forge.Capsule(21.6f, 17.5f, 23.4f, 28f, 2.3f, 1.8f);
        }

        forge.Fill(garment, roundness: 1.15f, cap: 2.6f, lift: 3.4f);

        forge.Begin();
        forge.Capsule(cx, 13f, cx, 18f, 2.0f, 2.6f);
        forge.Fill(skin, roundness: 1.1f, cap: 2.6f, lift: 1.8f);

        forge.Begin();
        forge.Ellipse(cx, 9f, facing == Facing.Side ? 4.2f : 5.0f, 5.6f);
        forge.Fill(skin, roundness: 0.95f, cap: 5.0f, lift: 3.2f);

        forge.Begin();
        forge.Ellipse(cx, 7.6f, facing == Facing.Side ? 4.8f : 5.6f, 5.4f);
        if (facing != Facing.Back)
            forge.Erase(cx, 12.5f, 4.6f, 3.6f);
        forge.Fill(hair, roundness: 1.0f, cap: 5.4f, lift: 4.2f);

        if (facing == Facing.Front)
        {
            forge.Begin();
            forge.Ellipse(13.9f, 9.4f, 1.15f, 1.35f);
            forge.Ellipse(18.1f, 9.4f, 1.15f, 1.35f);
            forge.Fill(Eye, roundness: 1.4f, cap: 3f, lift: 7.4f);
        }
        else if (facing == Facing.Side)
        {
            forge.Begin();
            forge.Ellipse(cx + 1.6f, 9.4f, 1.15f, 1.35f);
            forge.Fill(Eye, roundness: 1.4f, cap: 3f, lift: 7.4f);
        }

        return Pixels(device, Width, Height, forge.Resolve(Key));
    }

    private static readonly SpriteMaterial Eye = new()
    {
        Ramp = new[] { new Color(16, 14, 16), new Color(30, 27, 30), new Color(48, 44, 48) },
        Outline = new Color(10, 9, 10)
    };

    private static Texture2D BuildPine(GraphicsDevice device)
    {
        var forge = new SpriteForge(32, 56);
        var bark = SpriteMaterial.FromBase(new Color(74, 52, 34));
        var needle = SpriteMaterial.FromBase(new Color(42, 78, 48));
        forge.Begin();
        forge.Capsule(16f, 38f, 16f, 54f, 2.4f, 2.8f);
        forge.Fill(bark, roundness: 0.8f, cap: 2.4f);
        forge.Begin();
        forge.Polygon(new Vector2(16f, 4f), new Vector2(6f, 24f), new Vector2(26f, 24f));
        forge.Polygon(new Vector2(16f, 14f), new Vector2(4f, 36f), new Vector2(28f, 36f));
        forge.Polygon(new Vector2(16f, 24f), new Vector2(5f, 44f), new Vector2(27f, 44f));
        forge.Fill(needle, roundness: 0.5f, cap: 6f, lift: 1.2f);
        return Pixels(device, 32, 56, forge.Resolve(Key));
    }

    private static Texture2D BuildPalm(GraphicsDevice device)
    {
        var forge = new SpriteForge(36, 64);
        var bark = SpriteMaterial.FromBase(new Color(118, 82, 48));
        var frond = SpriteMaterial.FromBase(new Color(52, 118, 58));
        forge.Begin();
        forge.Capsule(18f, 28f, 18f, 62f, 2.2f, 2.6f);
        forge.Fill(bark, roundness: 0.7f, cap: 2.2f);
        forge.Begin();
        forge.Ellipse(18f, 16f, 16f, 8f);
        forge.Ellipse(8f, 20f, 10f, 5f);
        forge.Ellipse(28f, 20f, 10f, 5f);
        forge.Ellipse(12f, 10f, 8f, 5f);
        forge.Ellipse(24f, 10f, 8f, 5f);
        forge.Fill(frond, roundness: 0.55f, cap: 6f, lift: 1.4f);
        return Pixels(device, 36, 64, forge.Resolve(Key));
    }

    private static Texture2D BuildOak(GraphicsDevice device)
    {
        var forge = new SpriteForge(40, 52);
        var bark = SpriteMaterial.FromBase(new Color(88, 60, 38));
        var leaf = SpriteMaterial.FromBase(new Color(58, 102, 52));
        forge.Begin();
        forge.Capsule(20f, 30f, 20f, 50f, 3.2f, 3.6f);
        forge.Fill(bark, roundness: 0.7f, cap: 3f);
        forge.Begin();
        forge.Ellipse(20f, 18f, 14f, 16f);
        forge.Fill(leaf, roundness: 0.85f, cap: 8f, lift: 1.5f);
        return Pixels(device, 40, 52, forge.Resolve(Key));
    }

    private static Texture2D BuildBoulder(GraphicsDevice device)
    {
        var forge = new SpriteForge(28, 20);
        var rock = SpriteMaterial.FromBase(new Color(110, 108, 102));
        forge.Begin();
        forge.Ellipse(14f, 12f, 12f, 8f);
        forge.Fill(rock, roundness: 0.7f, cap: 6f, lift: 0.8f);
        return Pixels(device, 28, 20, forge.Resolve(Key));
    }

    private static Texture2D BuildMouth(GraphicsDevice device)
    {
        var forge = new SpriteForge(48, 40);
        var rock = SpriteMaterial.FromBase(new Color(86, 82, 76));
        var voidMat = new SpriteMaterial
        {
            Ramp = new[] { new Color(8, 8, 10), new Color(14, 14, 16), new Color(22, 22, 24) },
            Outline = new Color(6, 6, 8)
        };
        forge.Begin();
        forge.Ellipse(24f, 24f, 20f, 16f);
        forge.Fill(rock, roundness: 0.6f, cap: 8f);
        forge.Begin();
        forge.Ellipse(24f, 26f, 11f, 12f);
        forge.Fill(voidMat, roundness: 1.2f, cap: 6f, lift: 2f);
        return Pixels(device, 48, 40, forge.Resolve(Key));
    }

    private static Texture2D BakeHorse(GraphicsDevice device, Facing facing)
    {
        var hide = SpriteMaterial.FromBase(new Color(118, 78, 44));
        var mane = SpriteMaterial.FromBase(new Color(42, 28, 20));
        var leather = SpriteMaterial.FromBase(new Color(72, 48, 28));
        if (facing == Facing.Side)
        {
            var forge = new SpriteForge(56, 40);
            forge.Begin();
            forge.Capsule(18f, 28f, 18f, 38f, 2.2f, 1.8f);
            forge.Capsule(26f, 28f, 26f, 38f, 2.2f, 1.8f);
            forge.Capsule(38f, 28f, 38f, 38f, 2.1f, 1.7f);
            forge.Capsule(46f, 28f, 46f, 38f, 2.1f, 1.7f);
            forge.Fill(hide, roundness: 0.7f, cap: 2f);
            forge.Begin();
            forge.Ellipse(32f, 22f, 18f, 9f);
            forge.Fill(hide, roundness: 0.65f, cap: 6f, lift: 1.2f);
            forge.Begin();
            forge.Capsule(16f, 18f, 8f, 12f, 3.4f, 2.6f);
            forge.Ellipse(6f, 10f, 4.4f, 3.4f);
            forge.Fill(hide, roundness: 0.8f, cap: 3.2f, lift: 1.4f);
            forge.Begin();
            forge.Capsule(16f, 10f, 28f, 16f, 2.4f, 1.6f);
            forge.Fill(mane, roundness: 0.9f, cap: 2.2f);
            forge.Begin();
            forge.Capsule(48f, 18f, 54f, 24f, 1.6f, 1.1f);
            forge.Fill(mane, roundness: 1f, cap: 1.6f);
            forge.Begin();
            forge.Rect(24f, 18f, 40f, 22f);
            forge.Fill(leather, roundness: 0.4f, cap: 1.4f);
            return Pixels(device, 56, 40, forge.Resolve(Key));
        }

        if (facing == Facing.Back)
        {
            var forge = new SpriteForge(36, 40);
            forge.Begin();
            forge.Capsule(12f, 26f, 12f, 38f, 2.4f, 1.8f);
            forge.Capsule(24f, 26f, 24f, 38f, 2.4f, 1.8f);
            forge.Fill(hide, roundness: 0.7f, cap: 2f);
            forge.Begin();
            forge.Ellipse(18f, 18f, 11f, 12f);
            forge.Fill(hide, roundness: 0.7f, cap: 6f, lift: 1.2f);
            forge.Begin();
            forge.Capsule(18f, 8f, 18f, 16f, 3.2f, 2.2f);
            forge.Fill(mane, roundness: 0.9f, cap: 2.6f);
            return Pixels(device, 36, 40, forge.Resolve(Key));
        }

        {
            var forge = new SpriteForge(36, 40);
            forge.Begin();
            forge.Capsule(12f, 26f, 12f, 38f, 2.4f, 1.8f);
            forge.Capsule(24f, 26f, 24f, 38f, 2.4f, 1.8f);
            forge.Fill(hide, roundness: 0.7f, cap: 2f);
            forge.Begin();
            forge.Ellipse(18f, 20f, 10f, 11f);
            forge.Fill(hide, roundness: 0.7f, cap: 6f, lift: 1.2f);
            forge.Begin();
            forge.Ellipse(18f, 10f, 5.2f, 5.6f);
            forge.Capsule(18f, 14f, 18f, 20f, 3.4f, 3.8f);
            forge.Fill(hide, roundness: 0.85f, cap: 4f, lift: 1.6f);
            forge.Begin();
            forge.Capsule(18f, 4f, 18f, 12f, 2.8f, 1.8f);
            forge.Fill(mane, roundness: 0.9f, cap: 2.4f);
            return Pixels(device, 36, 40, forge.Resolve(Key));
        }
    }

    private static Texture2D BakeHorseRide(GraphicsDevice device)
    {
        var hide = SpriteMaterial.FromBase(new Color(118, 78, 44));
        var mane = SpriteMaterial.FromBase(new Color(42, 28, 20));
        var leather = SpriteMaterial.FromBase(new Color(72, 48, 28));
        var forge = new SpriteForge(40, 48);

        forge.Begin();
        forge.Ellipse(20f, 42f, 16f, 7f);
        forge.Fill(hide, roundness: 0.7f, cap: 5f, lift: 1.2f);

        forge.Begin();
        forge.Rect(12f, 36f, 28f, 42f);
        forge.Fill(leather, roundness: 0.45f, cap: 1.6f);

        forge.Begin();
        forge.Capsule(20f, 36f, 20f, 16f, 7.2f, 4.4f);
        forge.Fill(hide, roundness: 0.8f, cap: 4f, lift: 1.4f);

        forge.Begin();
        forge.Ellipse(20f, 14f, 6.2f, 5.4f);
        forge.Fill(hide, roundness: 0.85f, cap: 3.6f, lift: 1.5f);

        forge.Begin();
        forge.Capsule(16.2f, 12f, 13.4f, 4.2f, 1.7f, 1.15f);
        forge.Capsule(23.8f, 12f, 26.6f, 4.2f, 1.7f, 1.15f);
        forge.Fill(hide, roundness: 0.9f, cap: 1.8f);

        forge.Begin();
        forge.Capsule(20f, 32f, 20f, 12f, 2.3f, 1.5f);
        forge.Fill(mane, roundness: 0.95f, cap: 2.2f);

        return Pixels(device, 40, 48, forge.Resolve(Key));
    }

    private static Texture2D BakeWagon(GraphicsDevice device)
    {
        var wood = SpriteMaterial.FromBase(new Color(118, 78, 42));
        var iron = SpriteMaterial.FromBase(new Color(70, 72, 78), gloss: 0.4f);
        var hide = SpriteMaterial.FromBase(new Color(108, 72, 42));
        var forge = new SpriteForge(64, 40);
        forge.Begin();
        forge.Ellipse(12f, 32f, 6f, 6f);
        forge.Ellipse(36f, 32f, 6f, 6f);
        forge.Fill(iron, roundness: 0.8f, cap: 3f);
        forge.Begin();
        forge.Rect(8f, 16f, 46f, 28f);
        forge.Rect(18f, 8f, 42f, 18f);
        forge.Fill(wood, roundness: 0.45f, cap: 4f, lift: 1.1f);
        forge.Begin();
        forge.Capsule(50f, 22f, 60f, 18f, 2.4f, 2f);
        forge.Ellipse(58f, 14f, 4f, 3.2f);
        forge.Fill(hide, roundness: 0.8f, cap: 2.4f, lift: 1.2f);
        return Pixels(device, 64, 40, forge.Resolve(Key));
    }

    private static Texture2D BakeFire(GraphicsDevice device)
    {
        var ember = SpriteMaterial.FromBase(new Color(186, 72, 28), gloss: 0.3f);
        var flame = SpriteMaterial.FromBase(new Color(232, 168, 48), gloss: 0.5f);
        var forge = new SpriteForge(24, 28);
        forge.Begin();
        forge.Ellipse(12f, 22f, 8f, 4f);
        forge.Fill(ember, roundness: 0.7f, cap: 3f);
        forge.Begin();
        forge.Capsule(12f, 18f, 12f, 6f, 5.5f, 2.4f);
        forge.Fill(flame, roundness: 0.85f, cap: 4f, lift: 2f);
        return Pixels(device, 24, 28, forge.Resolve(Key));
    }

    private static Texture2D BuildSword(GraphicsDevice device)
    {
        var forge = new SpriteForge(32, 32);
        var iron = SpriteMaterial.FromBase(new Color(150, 156, 168), gloss: 0.8f);
        var wood = SpriteMaterial.FromBase(new Color(92, 62, 38));
        forge.Begin();
        forge.Capsule(16f, 6f, 16f, 22f, 2.4f, 1.4f);
        forge.Fill(iron, roundness: 0.4f, cap: 2.2f, lift: 1f);
        forge.Begin();
        forge.Rect(10f, 20f, 22f, 23f);
        forge.Fill(iron, roundness: 0.3f, cap: 1.4f);
        forge.Begin();
        forge.Capsule(16f, 22f, 16f, 30f, 1.6f, 1.8f);
        forge.Fill(wood, roundness: 0.8f, cap: 1.8f);
        return Pixels(device, 32, 32, forge.Resolve(Key));
    }

    private static Texture2D BuildKey(GraphicsDevice device)
    {
        var forge = new SpriteForge(24, 24);
        var gold = SpriteMaterial.FromBase(new Color(212, 168, 64), gloss: 0.7f);
        forge.Begin();
        forge.Ellipse(12f, 8f, 6f, 6f);
        forge.Erase(12f, 8f, 3f, 3f);
        forge.Fill(gold, roundness: 0.9f, cap: 3f, lift: 1f);
        forge.Begin();
        forge.Rect(11f, 12f, 13f, 21f);
        forge.Rect(13f, 17f, 18f, 19f);
        forge.Rect(13f, 20f, 16f, 22f);
        forge.Fill(gold, roundness: 0.5f, cap: 1.6f);
        return Pixels(device, 24, 24, forge.Resolve(Key));
    }

    private static Texture2D BuildMapPin(GraphicsDevice device)
    {
        var forge = new SpriteForge(20, 28);
        var gold = SpriteMaterial.FromBase(new Color(196, 72, 58));
        forge.Begin();
        forge.Ellipse(10f, 8f, 7f, 7f);
        forge.Polygon(new Vector2(10f, 26f), new Vector2(4f, 12f), new Vector2(16f, 12f));
        forge.Fill(gold, roundness: 0.7f, cap: 4f, lift: 1.2f);
        return Pixels(device, 20, 28, forge.Resolve(Key));
    }

    private static Texture2D BakeBoard(GraphicsDevice device, string title, string bearing)
    {
        const int Width = 128;
        const int Height = 48;
        var pixels = new Color[Width * Height];
        var rng = new Random(title.GetHashCode(StringComparison.Ordinal) ^ 0x51A2);

        var plank = new Color(118, 78, 42);
        var plankDark = new Color(78, 50, 28);
        var plankLight = new Color(148, 104, 58);
        var frame = new Color(52, 32, 18);
        var paint = new Color(236, 220, 176);
        var paintDim = new Color(196, 168, 112);

        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width; x++)
        {
            var edge = x < 3 || y < 3 || x >= Width - 3 || y >= Height - 3;
            var grain = rng.Next(0, 18);
            var colour = edge ? frame
                : grain < 3 ? plankDark
                : grain > 15 ? plankLight
                : plank;
            pixels[y * Width + x] = colour;
        }

        var top = string.IsNullOrWhiteSpace(title) ? " " : title.ToUpperInvariant();
        Stamp(pixels, Width, Height, top, 7, paint, paintDim);
        if (!string.IsNullOrWhiteSpace(bearing))
            Stamp(pixels, Width, Height, bearing.ToUpperInvariant(), 28, paintDim, plankDark);

        return Pixels(device, Width, Height, pixels);
    }

    private static void Stamp(Color[] pixels, int width, int height, string text,
        int top, Color ink, Color shade)
    {
        var gw = 5;
        var gh = 7;
        var step = text.Length > 16 ? 5 : 6;
        var total = text.Length * step - 1;
        var x0 = Math.Max(6, (width - total) / 2);
        var y0 = top;
        for (var i = 0; i < text.Length; i++)
        {
            BlitGlyph(pixels, width, height, text[i], x0 + i * step, y0, gw, gh, ink, shade);
        }
    }

    private static void BlitGlyph(Color[] pixels, int width, int height, char letter,
        int x0, int y0, int gw, int gh, Color ink, Color shade)
    {
        var rows = Glyph(char.ToUpperInvariant(letter));
        for (var row = 0; row < rows.Length && row < gh; row++)
        {
            var bits = rows[row];
            for (var col = 0; col < gw; col++)
            {
                if ((bits & (1 << (gw - 1 - col))) == 0) continue;
                var x = x0 + col;
                var y = y0 + row;
                if ((uint)x >= (uint)width || (uint)y >= (uint)height) continue;
                pixels[y * width + x] = ink;
                var sx = x + 1;
                var sy = y + 1;
                if (sx < width && sy < height && pixels[sy * width + sx].R < 90)
                    pixels[sy * width + sx] = shade;
            }
        }
    }

    private static int[] Glyph(char letter) => letter switch
    {
        'A' => [0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
        'B' => [0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110],
        'C' => [0b01110, 0b10001, 0b10000, 0b10000, 0b10000, 0b10001, 0b01110],
        'D' => [0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110],
        'E' => [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111],
        'F' => [0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000],
        'G' => [0b01110, 0b10001, 0b10000, 0b10111, 0b10001, 0b10001, 0b01110],
        'H' => [0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001],
        'I' => [0b01110, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110],
        'J' => [0b00111, 0b00010, 0b00010, 0b00010, 0b00010, 0b10010, 0b01100],
        'K' => [0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001],
        'L' => [0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111],
        'M' => [0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001],
        'N' => [0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001],
        'O' => [0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110],
        'P' => [0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000],
        'Q' => [0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101],
        'R' => [0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001],
        'S' => [0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110],
        'T' => [0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100],
        'U' => [0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110],
        'V' => [0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100],
        'W' => [0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b10101, 0b01010],
        'X' => [0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001],
        'Y' => [0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100],
        'Z' => [0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111],
        '0' => [0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110],
        '1' => [0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110],
        '2' => [0b01110, 0b10001, 0b00001, 0b00110, 0b01000, 0b10000, 0b11111],
        '3' => [0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110],
        '4' => [0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010],
        '5' => [0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110],
        '6' => [0b01110, 0b10000, 0b11110, 0b10001, 0b10001, 0b10001, 0b01110],
        '7' => [0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000],
        '8' => [0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110],
        '9' => [0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00001, 0b01110],
        '-' => [0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000],
        '.' => [0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00100, 0b00100],
        '\'' => [0b00100, 0b00100, 0b01000, 0b00000, 0b00000, 0b00000, 0b00000],
        '·' or '•' => [0b00000, 0b00000, 0b00100, 0b01110, 0b00100, 0b00000, 0b00000],
        '/' => [0b00001, 0b00010, 0b00010, 0b00100, 0b01000, 0b01000, 0b10000],
        _ => [0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000]
    };

    private static class Palettes
    {
        public static readonly CharacterPalette Traveler = new(
            Skin: new Color(198, 152, 112),
            Hair: new Color(64, 42, 30),
            Garment: new Color(78, 92, 64),
            Trim: new Color(120, 96, 58),
            Boots: new Color(48, 36, 28));

        public static readonly CharacterPalette Watch = new(
            Skin: new Color(190, 145, 108),
            Hair: new Color(38, 42, 45),
            Garment: new Color(57, 86, 116),
            Trim: new Color(174, 140, 68),
            Boots: new Color(34, 38, 44));

        public static readonly CharacterPalette Trader = new(
            Skin: new Color(218, 170, 125),
            Hair: new Color(97, 61, 34),
            Garment: new Color(125, 78, 112),
            Trim: new Color(195, 152, 72),
            Boots: new Color(59, 41, 37));

        public static readonly CharacterPalette Innkeep = new(
            Skin: new Color(205, 163, 125),
            Hair: new Color(74, 50, 36),
            Garment: new Color(128, 72, 48),
            Trim: new Color(168, 132, 72),
            Boots: new Color(48, 37, 31));

        public static readonly CharacterPalette Dummy = new(
            Skin: new Color(176, 142, 96),
            Hair: new Color(92, 68, 40),
            Garment: new Color(140, 108, 70),
            Trim: new Color(90, 70, 48),
            Boots: new Color(70, 54, 40));

        public static readonly CharacterPalette Fighter = new(
            Skin: new Color(188, 148, 110),
            Hair: new Color(48, 40, 36),
            Garment: new Color(86, 92, 102),
            Trim: new Color(168, 140, 72),
            Boots: new Color(40, 38, 42));

        public static readonly CharacterPalette Mage = new(
            Skin: new Color(210, 176, 150),
            Hair: new Color(72, 52, 88),
            Garment: new Color(92, 58, 128),
            Trim: new Color(186, 154, 72),
            Boots: new Color(52, 36, 58));

        public static readonly CharacterPalette Priest = new(
            Skin: new Color(198, 160, 122),
            Hair: new Color(210, 206, 198),
            Garment: new Color(214, 210, 200),
            Trim: new Color(186, 148, 64),
            Boots: new Color(92, 70, 48));

        public static readonly CharacterPalette Thief = new(
            Skin: new Color(168, 132, 102),
            Hair: new Color(28, 26, 32),
            Garment: new Color(48, 52, 46),
            Trim: new Color(92, 78, 48),
            Boots: new Color(32, 30, 28));

        public static readonly CharacterPalette Wisp = new(
            Skin: new Color(186, 214, 220),
            Hair: new Color(140, 196, 210),
            Garment: new Color(72, 118, 138),
            Trim: new Color(210, 232, 236),
            Boots: new Color(48, 72, 82));
    }
}
