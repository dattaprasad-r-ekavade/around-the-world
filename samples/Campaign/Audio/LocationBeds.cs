using Ember.Audio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using System;
using System.IO;
using System.Text;

namespace Campaign;

public enum LocationBed
{
    Wilderness,
    Town,
    Dungeon
}

/// <summary>
/// Looping beds. Old noise-heavy wavs sounded like radio static; these are quiet tones.
/// Always rewritten so a previous forge cannot linger on disk.
/// </summary>
public sealed class LocationBeds : IDisposable
{
    private readonly SoundEffect[] _sounds = new SoundEffect[4];
    private readonly SoundEffectInstance[] _voices = new SoundEffectInstance[4];
    private readonly float[] _volume = new float[4];
    private int _target;
    private bool _rain;
    private float _wind = 1f;
    private bool _available;

    public bool Enabled { get; set; }

    public static LocationBeds Create(string audioDirectory, out string fault)
    {
        var beds = new LocationBeds();
        fault = string.Empty;
        try
        {
            Directory.CreateDirectory(audioDirectory);
            WriteWav(Path.Combine(audioDirectory, "wind.wav"), Forge(BedKind.Wind));
            WriteWav(Path.Combine(audioDirectory, "street.wav"), Forge(BedKind.Town));
            WriteWav(Path.Combine(audioDirectory, "under.wav"), Forge(BedKind.Cave));

            WriteWav(Path.Combine(audioDirectory, "rain.wav"), Forge(BedKind.Rain));

            beds.Load(0, Path.Combine(audioDirectory, "wind.wav"));
            beds.Load(1, Path.Combine(audioDirectory, "street.wav"));
            beds.Load(2, Path.Combine(audioDirectory, "under.wav"));
            beds.Load(3, Path.Combine(audioDirectory, "rain.wav"));
            beds._available = true;
            beds._target = 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException or IOException or SystemException)
        {
            fault = $"Location beds unavailable: {exception.GetType().Name}.";
            beds._available = false;
        }

        return beds;
    }

    public void SetTarget(LocationBed bed)
    {
        _target = (int)bed;
        if (_available && _voices[_target].State != SoundState.Playing)
            _voices[_target].Play();
    }

    public void SetRain(bool rain) => _rain = rain;

    public void SetWind(float scale) => _wind = MathHelper.Clamp(scale, 0.6f, 1.8f);

    public void Update(float seconds)
    {
        if (!_available) return;
        if (!Enabled)
        {
            for (var i = 0; i < 4; i++)
            {
                _volume[i] = 0f;
                if (_voices[i] is null) continue;
                _voices[i].Volume = 0f;
                if (_voices[i].State == SoundState.Playing)
                    _voices[i].Pause();
            }

            return;
        }

        var rate = seconds * 1.6f;
        for (var i = 0; i < 4; i++)
        {
            var goal = i == 3
                ? (_rain ? 0.1f : 0f)
                : i == _target ? Gain(i) : 0f;
            if (i == 0) goal *= _wind;
            _volume[i] = MathHelper.Lerp(_volume[i], goal, MathF.Min(1f, rate));
            _voices[i].Volume = _volume[i];
            var want = _volume[i] > 0.008f || i == _target || (i == 3 && _rain);
            if (!want && _voices[i].State == SoundState.Playing)
                _voices[i].Pause();
            if (want && _voices[i].State != SoundState.Playing)
                _voices[i].Play();
        }
    }

    public void Dispose()
    {
        foreach (var voice in _voices)
        {
            voice?.Stop();
            voice?.Dispose();
        }

        foreach (var sound in _sounds)
            sound?.Dispose();
    }

    private static float Gain(int index) => index switch
    {
        0 => 0.11f,
        1 => 0.09f,
        _ => 0.1f
    };

    private void Load(int index, string path)
    {
        using var stream = File.OpenRead(path);
        var sound = SoundEffect.FromStream(stream);
        var voice = sound.CreateInstance();
        voice.IsLooped = true;
        voice.Volume = 0f;
        _sounds[index] = sound;
        _voices[index] = voice;
        _volume[index] = 0f;
    }

    private enum BedKind { Wind, Town, Cave, Rain }

    private static byte[] Forge(BedKind kind)
    {
        var forge = new SoundForge(seconds: 10f, seed: kind switch
        {
            BedKind.Wind => 0xA21,
            BedKind.Town => 0xB32,
            BedKind.Rain => 0xD54,
            _ => 0xC43
        });

        switch (kind)
        {
            case BedKind.Wind:
                forge.Tone(0f, 10f, 0.07f, 78f, 74f, decay: 0.02f);
                forge.Tone(0f, 10f, 0.045f, 116f, 112f, decay: 0.02f);
                forge.Noise(0f, 10f, 0.025f, 90f, decay: 0.04f);
                break;
            case BedKind.Town:
                forge.Tone(0f, 10f, 0.05f, 98f, 98f, decay: 0.02f);
                forge.Tone(0f, 10f, 0.03f, 147f, 145f, decay: 0.02f);
                forge.Noise(0f, 10f, 0.02f, 140f, decay: 0.04f);
                break;
            case BedKind.Rain:
                forge.Noise(0f, 10f, 0.07f, 180f, decay: 0.02f);
                forge.Noise(0f, 10f, 0.04f, 320f, decay: 0.03f);
                forge.Tone(0f, 10f, 0.02f, 52f, 48f, decay: 0.02f);
                break;
            default:
                forge.Tone(0f, 10f, 0.08f, 46f, 44f, decay: 0.02f);
                forge.Noise(0f, 10f, 0.03f, 70f, decay: 0.04f);
                break;
        }

        forge.Declick(0.12f);
        forge.Normalise(0.4f);
        return forge.ToPcm();
    }

    private static void WriteWav(string path, byte[] pcm)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + pcm.Length);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SoundForge.SampleRate);
        writer.Write(SoundForge.SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(pcm.Length);
        writer.Write(pcm);
    }
}
