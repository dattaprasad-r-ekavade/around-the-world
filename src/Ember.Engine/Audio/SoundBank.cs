using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using System;
using System.Collections.Generic;
using System.IO;

namespace Ember.Audio;

/// <summary>Everything the game can play. One entry per event, not per asset.</summary>
public enum Sfx
{
    /// <summary>A blade moving through air, and nothing there to stop it.</summary>
    Swing,

    /// <summary>Steel into a body. The single most important sound in the game.</summary>
    HitFlesh,

    /// <summary>A blow that was taken on a guard.</summary>
    Block,

    /// <summary>Something stops fighting.</summary>
    Death,

    /// <summary>The player is struck.</summary>
    Hurt,

    /// <summary>A spell leaves the hand.</summary>
    Cast,

    /// <summary>A door, opened.</summary>
    Door,

    /// <summary>Coin, or a stone banked.</summary>
    Coin,

    /// <summary>A rank, a level, or anything else worth a small fanfare.</summary>
    Chime,

    /// <summary>Refused: no stamina, no prana, locked.</summary>
    Denied,

    /// <summary>A boot on stone. The most frequently played sound in the game by far.</summary>
    Step,

    /// <summary>Landing after a jump or a drop.</summary>
    Land
}

/// <summary>
/// The game's sound effects, synthesised at startup.
///
/// There is not one audio file in the repository and there does not need to be. Every sound
/// below is a few layers of shaped noise and swept tone — the same doctrine as the sprites and
/// the masonry, and the same argument: a sound that is a function of numbers cannot go missing
/// from a build, needs no licence, and varies per weapon by changing an argument rather than by
/// commissioning a second recording.
///
/// Each entry is generated in a few variants, and a random one plays each time. That single
/// detail is most of the difference between a game that has sound and a game that sounds good:
/// one buffer replayed at combat frequency stops registering as an impact within about ten
/// swings and starts registering as a machine.
/// </summary>
public sealed class SoundBank : IDisposable
{
    private const int Variants = 4;

    private readonly Dictionary<Sfx, SoundEffect[]> _sounds = new();
    private readonly Random _pick = new();

    /// <summary>Master level. Combat is loud enough to be unpleasant at unity gain.</summary>
    public float Volume { get; set; } = 0.55f;

    public bool IsAvailable { get; private set; }

    public static SoundBank Create(out string error)
    {
        var bank = new SoundBank();
        error = string.Empty;

        try
        {
            bank.Build();
            bank.IsAvailable = true;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException or NoAudioHardwareException or SystemException)
        {
            // A machine with no audio device must still be a playable game. Every call below
            // becomes a no-op rather than an exception on the first swing.
            error = $"Sound effects unavailable: {exception.GetType().Name}.";
        }

        return bank;
    }

    private void Build()
    {
        for (var variant = 0; variant < Variants; variant++)
        {
            Add(Sfx.Swing, variant, Swing);
            Add(Sfx.HitFlesh, variant, HitFlesh);
            Add(Sfx.Block, variant, Block);
            Add(Sfx.Death, variant, Death);
            Add(Sfx.Hurt, variant, Hurt);
            Add(Sfx.Cast, variant, Cast);
            Add(Sfx.Door, variant, Door);
            Add(Sfx.Coin, variant, Coin);
            Add(Sfx.Chime, variant, Chime);
            Add(Sfx.Denied, variant, Denied);
            Add(Sfx.Step, variant, Step);
            Add(Sfx.Land, variant, Land);
        }
    }

    private void Add(Sfx id, int variant, Func<int, SoundForge> build)
    {
        if (!_sounds.TryGetValue(id, out var list))
        {
            list = new SoundEffect[Variants];
            _sounds[id] = list;
        }

        var forge = build(variant);
        list[variant] = new SoundEffect(forge.ToPcm(), SoundForge.SampleRate, AudioChannels.Mono);
    }

    /// <summary>
    /// Play one, at a pitch and volume that vary a little each time.
    ///
    /// <paramref name="weight"/> is how heavy the event was — a light hit and a killing blow
    /// should not be the same sound at the same level. It pushes volume up and pitch down,
    /// which is what "heavier" sounds like.
    /// </summary>
    public void Play(Sfx id, float weight = 0.5f, float volumeScale = 1f)
    {
        if (!IsAvailable || !_sounds.TryGetValue(id, out var variants)) return;

        var effect = variants[_pick.Next(variants.Length)];
        if (effect is null) return;

        var w = MathHelper.Clamp(weight, 0f, 1f);

        // Down for weight, plus a little jitter so repeated hits are not identical.
        var pitch = MathHelper.Clamp(
            -0.28f * w + (float)(_pick.NextDouble() - 0.5) * 0.16f, -1f, 1f);

        var volume = MathHelper.Clamp(
            Volume * volumeScale * (0.72f + w * 0.42f), 0f, 1f);

        try
        {
            effect.Play(volume, pitch, 0f);
        }
        catch (InstancePlayLimitException)
        {
            // XACT caps concurrent instances. A dropped sound in a busy fight is the correct
            // outcome; an exception mid-swing is not.
        }
    }

    /// <summary>
    /// Write every sound to .wav files, one per variant, and return how many were written.
    ///
    /// Audio is the one part of this project that cannot be checked by looking at it, and a
    /// synthesised sound that is silent, clipped, or three seconds of hiss all build and all
    /// pass a test suite. This exists so the sounds can be listened to and inspected as
    /// waveforms without playing the game to the moment each one fires.
    /// </summary>
    public static int Dump(string directory)
    {
        Directory.CreateDirectory(directory);

        var written = 0;
        var builders = new (Sfx Id, Func<int, SoundForge> Build)[]
        {
            (Sfx.Swing, Swing), (Sfx.HitFlesh, HitFlesh), (Sfx.Block, Block),
            (Sfx.Death, Death), (Sfx.Hurt, Hurt), (Sfx.Cast, Cast),
            (Sfx.Door, Door), (Sfx.Coin, Coin), (Sfx.Chime, Chime), (Sfx.Denied, Denied),
            (Sfx.Step, Step), (Sfx.Land, Land)
        };

        foreach (var (id, build) in builders)
        for (var variant = 0; variant < Variants; variant++)
        {
            var pcm = build(variant).ToPcm();
            var path = Path.Combine(directory, $"{id.ToString().ToLowerInvariant()}{variant}.wav");

            File.WriteAllBytes(path, Wav(pcm));
            written++;
        }

        return written;
    }

    /// <summary>The 44-byte canonical WAV header, then the samples.</summary>
    private static byte[] Wav(byte[] pcm)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        const short Channels = 1;
        const short BitsPerSample = 16;
        var byteRate = SoundForge.SampleRate * Channels * BitsPerSample / 8;

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + pcm.Length);
        writer.Write("WAVE"u8.ToArray());

        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(Channels);
        writer.Write(SoundForge.SampleRate);
        writer.Write(byteRate);
        writer.Write((short)(Channels * BitsPerSample / 8));
        writer.Write(BitsPerSample);

        writer.Write("data"u8.ToArray());
        writer.Write(pcm.Length);
        writer.Write(pcm);

        writer.Flush();
        return stream.ToArray();
    }

    public void Dispose()
    {
        foreach (var variants in _sounds.Values)
        foreach (var effect in variants)
            effect?.Dispose();

        _sounds.Clear();
        IsAvailable = false;
    }

    // ------------------------------------------------------------------ the sounds

    /// <summary>Air, moving. No impact — this is the sound of hitting nothing.</summary>
    private static SoundForge Swing(int variant)
    {
        var forge = new SoundForge(0.26f, 8100 + variant);
        forge.Whoosh(0f, 0.22f, 0.55f, peakHz: 2600f + variant * 260f);
        return forge;
    }

    /// <summary>
    /// Steel into a body.
    ///
    /// Three layers, and all three are needed. A low thump is the mass; a short mid burst is
    /// the impact itself; a very brief bright tick is the edge. Drop the tick and it reads as
    /// a punch, drop the thump and it reads as a slap on a table.
    /// </summary>
    private static SoundForge HitFlesh(int variant)
    {
        var forge = new SoundForge(0.30f, 2200 + variant);

        forge.Tone(0f, 0.14f, 0.70f, fromHz: 190f - variant * 8f, toHz: 55f, decay: 12f);
        forge.Noise(0f, 0.11f, 0.55f, cutoffHz: 900f + variant * 90f, decay: 15f);
        forge.Noise(0f, 0.02f, 0.30f, cutoffHz: 6500f, decay: 30f);

        return forge;
    }

    /// <summary>Steel onto steel: bright, ringing, and it does not sink in.</summary>
    private static SoundForge Block(int variant)
    {
        var forge = new SoundForge(0.42f, 3300 + variant);

        forge.Noise(0f, 0.03f, 0.6f, cutoffHz: 7200f, decay: 26f);
        forge.Tone(0f, 0.34f, 0.34f, fromHz: 1750f + variant * 120f, toHz: 1620f, decay: 7f);
        forge.Tone(0.002f, 0.30f, 0.20f, fromHz: 2680f + variant * 150f, toHz: 2500f, decay: 9f);

        return forge;
    }

    /// <summary>
    /// Something stops. Falling pitch, because that is what "over" sounds like in every
    /// language a player has ever heard.
    /// </summary>
    private static SoundForge Death(int variant)
    {
        var forge = new SoundForge(0.62f, 4400 + variant);

        forge.Tone(0f, 0.46f, 0.55f, fromHz: 330f - variant * 14f, toHz: 62f, decay: 4.2f);
        forge.Noise(0f, 0.34f, 0.30f, cutoffHz: 620f, decay: 5.5f);
        forge.Noise(0.20f, 0.26f, 0.16f, cutoffHz: 300f, decay: 4f);

        return forge;
    }

    /// <summary>The player takes one. Duller and closer than a hit landed outward.</summary>
    private static SoundForge Hurt(int variant)
    {
        var forge = new SoundForge(0.34f, 5500 + variant);

        forge.Tone(0f, 0.20f, 0.72f, fromHz: 148f, toHz: 44f, decay: 9f);
        forge.Noise(0f, 0.14f, 0.36f, cutoffHz: 520f + variant * 40f, decay: 11f);

        return forge;
    }

    /// <summary>Prana leaving the hand: a rising sweep, the opposite shape of a death.</summary>
    private static SoundForge Cast(int variant)
    {
        var forge = new SoundForge(0.40f, 6600 + variant);

        forge.Tone(0f, 0.30f, 0.42f, fromHz: 300f, toHz: 1180f + variant * 70f, decay: 3.4f);
        forge.Tone(0.01f, 0.26f, 0.22f, fromHz: 450f, toHz: 1760f, decay: 4.4f);
        forge.Noise(0f, 0.16f, 0.18f, cutoffHz: 3400f, decay: 6f);

        return forge;
    }

    /// <summary>Stone and timber, unwillingly. The one sound allowed to be slow.</summary>
    private static SoundForge Door(int variant)
    {
        var forge = new SoundForge(0.85f, 7700 + variant);

        forge.Noise(0f, 0.62f, 0.40f, cutoffHz: 480f + variant * 40f, decay: 2.2f);
        forge.Tone(0.02f, 0.34f, 0.20f, fromHz: 96f, toHz: 68f, decay: 3.2f);
        forge.Noise(0.58f, 0.16f, 0.45f, cutoffHz: 900f, decay: 14f);

        return forge;
    }

    /// <summary>Metal on metal, small and bright, two struck close together.</summary>
    private static SoundForge Coin(int variant)
    {
        var forge = new SoundForge(0.34f, 9900 + variant);

        forge.Tone(0f, 0.26f, 0.34f, fromHz: 2450f + variant * 130f, toHz: 2320f, decay: 9f);
        forge.Tone(0.035f, 0.22f, 0.26f, fromHz: 3180f + variant * 90f, toHz: 3020f, decay: 11f);
        forge.Noise(0f, 0.012f, 0.20f, cutoffHz: 8200f, decay: 34f);

        return forge;
    }

    /// <summary>Two notes up. Reserved for things that are actually good news.</summary>
    private static SoundForge Chime(int variant)
    {
        var forge = new SoundForge(0.78f, 1300 + variant);

        forge.Tone(0f, 0.40f, 0.34f, 523f, 523f, decay: 4.2f);
        forge.Tone(0.11f, 0.56f, 0.36f, 784f, 784f, decay: 3.4f);
        forge.Tone(0.11f, 0.52f, 0.16f, 1568f, 1568f, decay: 4.8f);

        return forge;
    }

    /// <summary>
    /// A boot on stone.
    ///
    /// Short, dry and quiet, because this plays roughly twice a second for the entire game. A
    /// footstep that is interesting is a footstep that becomes unbearable in ninety seconds;
    /// the job is to be *present* rather than to be heard. Grit on top of a small body thump is
    /// the whole recipe.
    /// </summary>
    private static SoundForge Step(int variant)
    {
        var forge = new SoundForge(0.13f, 3100 + variant);

        forge.Noise(0f, 0.055f, 0.34f, cutoffHz: 1700f + variant * 220f, decay: 26f);
        forge.Tone(0f, 0.05f, 0.24f, fromHz: 120f, toHz: 72f, decay: 24f);

        return forge;
    }

    /// <summary>Landing. The same idea as a step, with the weight of a body behind it.</summary>
    private static SoundForge Land(int variant)
    {
        var forge = new SoundForge(0.26f, 3700 + variant);

        forge.Tone(0f, 0.13f, 0.62f, fromHz: 132f, toHz: 52f, decay: 13f);
        forge.Noise(0f, 0.09f, 0.34f, cutoffHz: 1300f, decay: 17f);

        return forge;
    }

    /// <summary>Refused. Short, low, and deliberately unpleasant enough to stop a repeat.</summary>
    private static SoundForge Denied(int variant)
    {
        var forge = new SoundForge(0.20f, 1700 + variant);

        forge.Tone(0f, 0.15f, 0.40f, fromHz: 190f, toHz: 128f, decay: 10f);
        forge.Noise(0f, 0.06f, 0.16f, cutoffHz: 700f, decay: 18f);

        return forge;
    }
}
