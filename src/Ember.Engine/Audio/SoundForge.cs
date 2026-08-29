using Microsoft.Xna.Framework;
using System;

namespace Ember.Audio;

/// <summary>
/// A buffer of samples being built, and the handful of operations that build one.
///
/// The same argument as <see cref="SpriteForge"/>, applied to audio: a sound that is a function
/// of a few numbers costs nothing to store, cannot go missing from a build, varies per weapon or
/// per cave by changing an argument, and needs no licence attached to it.
///
/// It also settles the question the project would otherwise have to answer about generated
/// audio. This is synthesis from rules somebody wrote, not a model trained on other people's
/// recordings — the same distinction that already applies to every texture and sprite in the
/// game, and one worth being able to state plainly.
///
/// Everything here is mono at 22.05 kHz. A combat sound is 100–400 milliseconds of noise and
/// transient; stereo and CD rate would quadruple the memory to carry information nobody can
/// hear in a thing that short.
/// </summary>
public sealed class SoundForge
{
    public const int SampleRate = 22050;

    private readonly float[] _samples;
    private readonly Random _random;

    public SoundForge(float seconds, int seed)
    {
        _samples = new float[Math.Max(1, (int)(seconds * SampleRate))];
        _random = new Random(seed);
    }

    public int Length => _samples.Length;

    private static float Clamp01(float value) => MathHelper.Clamp(value, 0f, 1f);

    /// <summary>Seconds to a sample index, clamped into the buffer.</summary>
    private int At(float seconds) =>
        Math.Clamp((int)(seconds * SampleRate), 0, _samples.Length);

    /// <summary>
    /// Noise through a one-pole low-pass, which is what most physical impacts actually are.
    ///
    /// A hit is not a tone. It is a burst of broadband noise shaped by whatever was struck, and
    /// the cutoff is what says *what*: high and open reads as metal or a slap, low and dull
    /// reads as meat or stone. Nothing else in this file changes a sound's character as much.
    /// </summary>
    public void Noise(float start, float duration, float amplitude, float cutoffHz, float decay = 4f)
    {
        var from = At(start);
        var to = At(start + duration);

        // Standard one-pole coefficient. Higher cutoff lets more of each new sample through.
        var alpha = 1f - MathF.Exp(-MathF.Tau * cutoffHz / SampleRate);
        var state = 0f;

        for (var i = from; i < to; i++)
        {
            var white = (float)(_random.NextDouble() * 2.0 - 1.0);
            state += alpha * (white - state);

            var t = (i - from) / (float)Math.Max(1, to - from);
            _samples[i] += state * amplitude * MathF.Exp(-decay * t);
        }
    }

    /// <summary>A decaying sine, for anything with a pitch: a ring, a thud, a chime.</summary>
    public void Tone(float start, float duration, float amplitude, float fromHz, float toHz,
        float decay = 5f)
    {
        var from = At(start);
        var to = At(start + duration);
        var phase = 0f;

        for (var i = from; i < to; i++)
        {
            var t = (i - from) / (float)Math.Max(1, to - from);

            // Sweeping the frequency is what separates a thud from a beep. Falling reads as
            // weight landing; rising reads as something being drawn or charged.
            var hz = MathHelper.Lerp(fromHz, toHz, t);
            phase += MathF.Tau * hz / SampleRate;

            _samples[i] += MathF.Sin(phase) * amplitude * MathF.Exp(-decay * t);
        }
    }

    /// <summary>
    /// A swept band of noise, for a blade moving through air.
    ///
    /// A whoosh is noise whose brightness rises and falls as the thing passes you. Doing it as
    /// a moving cutoff rather than a moving volume is the whole trick: volume alone reads as
    /// someone turning a hiss up and down, and the sweep reads as travel.
    /// </summary>
    public void Whoosh(float start, float duration, float amplitude, float peakHz)
    {
        var from = At(start);
        var to = At(start + duration);
        var state = 0f;

        for (var i = from; i < to; i++)
        {
            var t = (i - from) / (float)Math.Max(1, to - from);

            // Cutoff arcs up and back down; loudness peaks slightly later, the way a real pass
            // does, because the loudest moment is just after the closest one.
            var arc = MathF.Sin(t * MathF.PI);
            var cutoff = 220f + peakHz * arc;
            var alpha = 1f - MathF.Exp(-MathF.Tau * cutoff / SampleRate);

            var white = (float)(_random.NextDouble() * 2.0 - 1.0);
            state += alpha * (white - state);

            var loudness = MathF.Sin(Clamp01(t * 1.08f) * MathF.PI);
            _samples[i] += state * amplitude * loudness * loudness;
        }
    }

    /// <summary>
    /// Fade the first and last few milliseconds to zero.
    ///
    /// Not cosmetic. A buffer that starts or ends on a non-zero sample produces a click on
    /// every play, and at the rate combat sounds fire that click becomes the most audible thing
    /// in the mix.
    /// </summary>
    public void Declick(float seconds = 0.004f)
    {
        var window = Math.Clamp((int)(seconds * SampleRate), 1, _samples.Length / 2);

        for (var i = 0; i < window; i++)
        {
            var gain = i / (float)window;
            _samples[i] *= gain;
            _samples[_samples.Length - 1 - i] *= gain;
        }
    }

    /// <summary>
    /// Bring the loudest peak to <paramref name="target"/>, then soft-clip.
    ///
    /// Layered noise and tones routinely sum past full scale, and hard clipping sounds like a
    /// fault rather than like loudness. tanh rounds the peaks instead, which reads as a sound
    /// that was struck hard.
    /// </summary>
    public void Normalise(float target = 0.85f)
    {
        var peak = 0f;
        foreach (var sample in _samples) peak = MathF.Max(peak, MathF.Abs(sample));
        if (peak <= 1e-6f) return;

        var gain = target / peak;
        for (var i = 0; i < _samples.Length; i++)
            _samples[i] = MathF.Tanh(_samples[i] * gain * 1.15f);
    }

    /// <summary>The finished buffer, as the 16-bit PCM MonoGame wants.</summary>
    public byte[] ToPcm()
    {
        Declick();
        Normalise();

        var bytes = new byte[_samples.Length * 2];
        for (var i = 0; i < _samples.Length; i++)
        {
            var value = (short)MathHelper.Clamp(_samples[i] * short.MaxValue,
                short.MinValue, short.MaxValue);

            bytes[i * 2] = (byte)(value & 0xFF);
            bytes[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
        }

        return bytes;
    }
}
