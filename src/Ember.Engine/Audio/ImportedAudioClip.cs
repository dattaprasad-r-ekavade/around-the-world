using System;
using System.IO;
using Ember.Scene;
using Microsoft.Xna.Framework.Audio;

namespace Ember.Audio;

/// <summary>Playback voice used by an imported scene audio clip.</summary>
public interface IAudioClipVoice : IDisposable
{
    float Volume { get; set; }
    bool IsPlaying { get; }
    void Play();
    void Stop();
}

/// <summary>An imported audio file and its single scene-owned playback voice.</summary>
public sealed class ImportedAudioClip : IDisposable
{
    private readonly IAudioClipVoice _voice;
    private bool _disposed;
    private float _volume = 1f;

    public ImportedAudioClip(string sourcePath, IAudioClipVoice voice)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("An audio source path is required.", nameof(sourcePath));
        SourcePath = Path.GetFullPath(sourcePath);
        _voice = voice ?? throw new ArgumentNullException(nameof(voice));
        _voice.Volume = _volume;
    }

    public string SourcePath { get; }
    public bool IsDisposed => _disposed;
    public bool IsPlaying => !_disposed && _voice.IsPlaying;

    public float Volume
    {
        get => _volume;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!float.IsFinite(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(nameof(value), "Volume must be between zero and one.");
            _volume = value;
            _voice.Volume = value;
        }
    }

    /// <summary>Imports a MonoGame-supported audio file from disk.</summary>
    public static ImportedAudioClip Load(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("An audio source path is required.", nameof(sourcePath));
        var fullPath = Path.GetFullPath(sourcePath);
        using var stream = File.OpenRead(fullPath);
        SoundEffect? sound = null;
        SoundEffectInstance? instance = null;
        try
        {
            sound = SoundEffect.FromStream(stream);
            instance = sound.CreateInstance();
            return new ImportedAudioClip(fullPath, new MonoGameAudioClipVoice(sound, instance));
        }
        catch
        {
            instance?.Dispose();
            sound?.Dispose();
            throw;
        }
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _voice.Play();
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _voice.Stop();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _voice.Stop(); }
        finally { _voice.Dispose(); }
    }

    private sealed class MonoGameAudioClipVoice(SoundEffect sound, SoundEffectInstance instance) : IAudioClipVoice
    {
        public float Volume { get => instance.Volume; set => instance.Volume = value; }
        public bool IsPlaying => instance.State == SoundState.Playing;
        public void Play() => instance.Play();
        public void Stop() => instance.Stop();
        public void Dispose()
        {
            try { instance.Dispose(); }
            finally { sound.Dispose(); }
        }
    }
}

/// <summary>Example compiled behaviour that plays one imported clip on the Interact action.</summary>
public sealed class PlayAudioOnInteractionBehaviour(ImportedAudioClip clip, string action = "Interact") : SceneBehaviour
{
    private readonly ImportedAudioClip _clip = clip ?? throw new ArgumentNullException(nameof(clip));
    private readonly string _action = string.IsNullOrWhiteSpace(action)
        ? throw new ArgumentException("An interaction action is required.", nameof(action))
        : action.Trim();

    protected override void OnInteract(SceneInteraction interaction)
    {
        if (string.Equals(interaction.Action, _action, StringComparison.OrdinalIgnoreCase)) _clip.Play();
    }

}
