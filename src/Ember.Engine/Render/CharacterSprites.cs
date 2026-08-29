using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Ember.Render;

/// <summary>The colours one character is drawn from. Locked to a small palette per figure.</summary>
public readonly record struct CharacterPalette(
    Color Skin,
    Color Hair,
    Color Garment,
    Color Trim,
    Color Boots)
{
    public static readonly CharacterPalette Bandit = new(
        Skin: new Color(198, 152, 112),
        Hair: new Color(48, 36, 30),
        Garment: new Color(112, 54, 52),
        Trim: new Color(64, 58, 50),
        Boots: new Color(52, 40, 34));

    public static readonly CharacterPalette Wolf = new(
        Skin: new Color(122, 118, 110),
        Hair: new Color(78, 74, 68),
        Garment: new Color(96, 92, 86),
        Trim: new Color(58, 54, 50),
        Boots: new Color(40, 38, 36));

    public static readonly CharacterPalette Citizen = new(
        Skin: new Color(205, 163, 125),
        Hair: new Color(74, 50, 36),
        Garment: new Color(64, 108, 104),
        Trim: new Color(56, 67, 64),
        Boots: new Color(48, 37, 31));

    public static readonly CharacterPalette Guard = new(
        Skin: new Color(190, 145, 108),
        Hair: new Color(38, 42, 45),
        Garment: new Color(57, 86, 116),
        Trim: new Color(174, 140, 68),
        Boots: new Color(34, 38, 44));

    public static readonly CharacterPalette Merchant = new(
        Skin: new Color(218, 170, 125),
        Hair: new Color(97, 61, 34),
        Garment: new Color(125, 78, 112),
        Trim: new Color(195, 152, 72),
        Boots: new Color(59, 41, 37));
}

/// <summary>
/// Characters, drawn in code.
///
/// This is the decision that sidesteps MonoGame's worst gap. A skinned, rigged humanoid needs
/// a mesh, a rig, retargeting and an animation system; a camera-facing quad needs none of
/// them, and it is what Daggerfall itself did. Because the texture is generated rather than
/// authored, a character is data — a palette and a few proportions — rather than an asset
/// somebody has to model.
///
/// Built on <see cref="SpriteForge"/>, which changes what "drawn in code" means here. The old
/// version stacked flat rectangles: a torso was one colour, an arm was one colour, and the
/// only thing separating them was that they were different colours. Now every part writes
/// thickness and the whole figure is lit at the end, from the same direction as every item
/// icon in the game. An arm in front of a torso is now readable because it is *rounder and
/// nearer*, not because somebody remembered to pick a different shade for it.
///
/// The palettes below are untouched, and deliberately so. Each flat colour becomes a five-step
/// ramp through <see cref="SpriteMaterial.FromBase"/>, so nothing that references a palette
/// had to change and the figures gained volume anyway.
/// </summary>
public static class CharacterSprites
{
    private const int Width = 32;
    private const int Height = 48;

    /// <summary>
    /// Upper left, and identical to the light in <c>ItemSprites</c>.
    ///
    /// This is the payoff of one shading model. A bandit and the sword he is holding are now
    /// lit by the same lamp, without anybody maintaining that agreement.
    /// </summary>
    private static readonly Vector3 Key = new(-0.55f, -0.68f, 0.5f);

    private static readonly Dictionary<string, Texture2D> Cache = new(StringComparer.Ordinal);

    /// <summary>Sprite for a palette, generated once and reused.</summary>
    public static Texture2D Get(GraphicsDevice device, string key, CharacterPalette palette)
    {
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var texture = Build(device, palette);
        Cache[key] = texture;
        return texture;
    }

    public static void Clear()
    {
        foreach (var texture in Cache.Values) texture.Dispose();
        Cache.Clear();
    }

    private static Texture2D Build(GraphicsDevice device, CharacterPalette palette)
    {
        var forge = new SpriteForge(Width, Height);

        var skin = SpriteMaterial.FromBase(palette.Skin);
        var hair = SpriteMaterial.FromBase(palette.Hair);
        var garment = SpriteMaterial.FromBase(palette.Garment);
        var trim = SpriteMaterial.FromBase(palette.Trim, gloss: 0.4f);
        var boots = SpriteMaterial.FromBase(palette.Boots);

        // Legs first and lowest, so everything laid down after sits in front of them.
        forge.Begin();
        forge.Capsule(13.2f, 30f, 12.8f, 41f, 2.5f, 2.2f);
        forge.Capsule(18.8f, 30f, 19.2f, 41f, 2.5f, 2.2f);
        forge.Fill(trim, roundness: 1.0f, cap: 2.6f);

        forge.Begin();
        forge.Ellipse(12.6f, 43.5f, 3.2f, 2.6f);
        forge.Ellipse(19.4f, 43.5f, 3.2f, 2.6f);
        forge.Fill(boots, roundness: 1.0f, cap: 2.8f, lift: 0.5f);

        // Torso, tapering to the waist.
        forge.Begin();
        forge.Capsule(16f, 17f, 16f, 30f, 6.6f, 4.9f);
        forge.Fill(garment, roundness: 0.62f, cap: 4.6f, lift: 1.4f);

        forge.Begin();
        forge.Capsule(11.5f, 28.6f, 20.5f, 28.6f, 1.7f, 1.7f);
        forge.Fill(trim, roundness: 1.3f, cap: 2.0f, lift: 4.6f);

        // Arms, hanging slightly clear of the body so the silhouette has gaps in it. A figure
        // whose arms merge into its torso reads as a bottle at any distance.
        forge.Begin();
        forge.Capsule(10.4f, 17.5f, 8.6f, 28f, 2.3f, 1.8f);
        forge.Capsule(21.6f, 17.5f, 23.4f, 28f, 2.3f, 1.8f);
        forge.Fill(garment, roundness: 1.15f, cap: 2.6f, lift: 3.4f);

        forge.Begin();
        forge.Ellipse(8.4f, 30f, 2.1f, 2.3f);
        forge.Ellipse(23.6f, 30f, 2.1f, 2.3f);
        forge.Fill(skin, roundness: 1.2f, cap: 2.6f, lift: 3.8f);

        // Neck, then head.
        forge.Begin();
        forge.Capsule(16f, 13f, 16f, 18f, 2.0f, 2.6f);
        forge.Fill(skin, roundness: 1.1f, cap: 2.6f, lift: 1.8f);

        forge.Begin();
        forge.Ellipse(16f, 9f, 5.0f, 5.6f);
        forge.Fill(skin, roundness: 0.95f, cap: 5.0f, lift: 3.2f);

        // Hair as a cap over the crown, with the face cleared out from under it.
        forge.Begin();
        forge.Ellipse(16f, 7.6f, 5.6f, 5.4f);
        forge.Erase(16f, 12.5f, 4.6f, 3.6f);
        forge.Fill(hair, roundness: 1.0f, cap: 5.4f, lift: 4.2f);

        // Eyes, so the figure reads as facing the player even at a distance. Lifted above
        // everything else so no ordering accident can bury them.
        forge.Begin();
        forge.Ellipse(13.9f, 9.4f, 1.15f, 1.35f);
        forge.Ellipse(18.1f, 9.4f, 1.15f, 1.35f);
        forge.Fill(new SpriteMaterial
        {
            Ramp = new[] { new Color(16, 14, 16), new Color(30, 27, 30), new Color(48, 44, 48) },
            Outline = new Color(10, 9, 10)
        }, roundness: 1.4f, cap: 3f, lift: 7.4f);

        var texture = new Texture2D(device, Width, Height);
        texture.SetData(forge.Resolve(Key));
        return texture;
    }
}
