using Microsoft.Xna.Framework.Audio;
using System;

namespace Ember.Audio;

/// <summary>
/// A small deterministic ambient bed generated at runtime so the authored slice has a
/// continuous sound layer without adding a binary asset to the repository.
/// </summary>
public sealed class AmbientAudio : IDisposable
{
    private readonly SoundEffect _sound;
    private readonly SoundEffectInstance _instance;

    private AmbientAudio(SoundEffect sound, SoundEffectInstance instance)
    {
        _sound = sound;
        _instance = instance;
    }

    public static bool TryStart(out AmbientAudio? ambient, out string error)
    {
        ambient = null;
        error = string.Empty;

        try
        {
            const int sampleRate = 22050;
            const int seconds = 8;
            var samples = new short[sampleRate * seconds];

            for (var index = 0; index < samples.Length; index++)
            {
                var time = index / (double)sampleRate;
                var wind = Math.Sin(time * Math.PI * 2d * 0.17d) * 0.35d
                    + Math.Sin(time * Math.PI * 2d * 0.31d) * 0.2d;
                var hum = Math.Sin(time * Math.PI * 2d * 92d) * 0.06d;
                var value = (wind + hum) * 0.035d;
                samples[index] = (short)Math.Clamp(value * short.MaxValue,
                    short.MinValue, short.MaxValue);
            }

            var buffer = new byte[samples.Length * sizeof(short)];
            Buffer.BlockCopy(samples, 0, buffer, 0, buffer.Length);
            var sound = new SoundEffect(buffer, sampleRate, AudioChannels.Mono);
            var instance = sound.CreateInstance();
            instance.IsLooped = true;
            instance.Volume = 0.18f;
            instance.Play();
            ambient = new AmbientAudio(sound, instance);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException or SystemException)
        {
            error = $"Ambient audio unavailable: {exception.GetType().Name}.";
            return false;
        }
    }

    public void Dispose()
    {
        _instance.Stop();
        _instance.Dispose();
        _sound.Dispose();
    }
}
