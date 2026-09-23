using System;

namespace Ember.Assets;

/// <summary>Small deterministic clock for playing, pausing, seeking, and looping one clip.</summary>
public sealed class GltfAnimationPlayback
{
    private float _speed;

    public GltfAnimationPlayback(GltfAnimationClipData clip, bool loop = true, float speed = 1f)
    {
        Clip = clip ?? throw new ArgumentNullException(nameof(clip));
        Loop = loop;
        Speed = speed;
    }

    public GltfAnimationClipData Clip { get; }
    public float Time { get; private set; }
    public bool IsPlaying { get; private set; }
    public bool Loop { get; set; }

    public float Speed
    {
        get => _speed;
        set
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value), "Playback speed must be finite.");
            _speed = value;
        }
    }

    public void Play()
    {
        if (Clip.Duration <= 0f)
        {
            IsPlaying = false;
            Time = 0f;
            return;
        }

        if (!Loop && Speed > 0f && Time >= Clip.Duration) Time = 0f;
        else if (!Loop && Speed < 0f && Time <= 0f) Time = Clip.Duration;
        IsPlaying = true;
    }

    public void Pause() => IsPlaying = false;

    /// <summary>Seeks to an absolute time, clamped to the clip's endpoints.</summary>
    public void Seek(float time)
    {
        if (!float.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time), "Seek time must be finite.");
        Time = Math.Clamp(time, 0f, Clip.Duration);
    }

    public void Advance(float elapsedSeconds)
    {
        if (!float.IsFinite(elapsedSeconds) || elapsedSeconds < 0f)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Elapsed time must be finite and nonnegative.");
        if (!IsPlaying || elapsedSeconds == 0f || Speed == 0f || Clip.Duration <= 0f) return;

        var nextTime = (double)Time + ((double)Speed * elapsedSeconds);
        if (Loop)
        {
            var duration = Clip.Duration;
            nextTime %= duration;
            if (nextTime < 0d) nextTime += duration;
            Time = (float)nextTime;
            return;
        }

        if (Speed > 0f && nextTime >= Clip.Duration)
        {
            Time = Clip.Duration;
            IsPlaying = false;
        }
        else if (Speed < 0f && nextTime <= 0d)
        {
            Time = 0f;
            IsPlaying = false;
        }
        else
        {
            Time = (float)nextTime;
        }
    }
}
