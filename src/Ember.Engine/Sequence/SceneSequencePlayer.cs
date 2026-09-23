using System;

namespace Ember.Sequence;

/// <summary>Play/pause/seek clock for a scene sequence. Seeking evaluates no gameplay events.</summary>
public sealed class SceneSequencePlayer
{
    public SceneSequencePlayer(SceneSequence sequence) =>
        Sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));

    public SceneSequence Sequence { get; }
    public float Time { get; private set; }
    public bool IsPlaying { get; private set; }

    public void Play()
    {
        if (Time >= Sequence.Duration) Time = 0f;
        IsPlaying = true;
    }

    public void Pause() => IsPlaying = false;

    public void Seek(float time)
    {
        if (!float.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time), "Seek time must be finite.");
        Time = Math.Clamp(time, 0f, Sequence.Duration);
    }

    public void Advance(float elapsedSeconds)
    {
        if (!float.IsFinite(elapsedSeconds) || elapsedSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Elapsed time must be finite and nonnegative.");
        if (!IsPlaying || elapsedSeconds == 0f) return;
        var nextTime = (double)Time + elapsedSeconds;
        if (nextTime >= Sequence.Duration)
        {
            Time = Sequence.Duration;
            IsPlaying = false;
        }
        else
        {
            Time = (float)nextTime;
        }
    }
}
