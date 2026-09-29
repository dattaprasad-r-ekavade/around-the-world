using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ember.Authoring;
using Ember.Assets;
using Ember.Audio;
using Ember;
using Ember.Input;
using Ember.Project;
using Ember.Physics;
using Ember.Scene;
using Ember.Sequence;
using Ember.Render;
using Ember.Rpg;
using Ember.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SharpGLTF.Schema2;

namespace Ember.Editor;

public sealed partial class CharacterStudioGame
{
    private SequenceEditorInfo? GetSequenceEditorInfo()
    {
        if (_sequence is null || _sequencePlayer is null) return null;
        var cameraName = _sequence.SampleCamera(_sequencePlayer.Time).CameraName;
        return new SequenceEditorInfo(_sequence.Name, _sequencePlayer.Time, _sequence.Duration,
            _sequencePlayer.IsPlaying, _sequencePreviewEnabled, cameraName);
    }

    private SequenceExportEditorInfo GetSequenceExportEditorInfo()
    {
        var job = _sequenceExportJob;
        return new SequenceExportEditorInfo(job?.IsRunning == true,
            job?.CompletedFrames ?? 0, job?.TotalFrames ?? 0,
            job?.State.ToString() ?? _lastSequenceExportStatus,
            job?.Settings.OutputDirectory ?? _lastSequenceExportDirectory,
            job?.Error ?? _lastSequenceExportError);
    }

    private void StartSequenceExport(SequenceExportEditorRequest request)
    {
        if (_sequenceExportJob?.IsRunning == true) return;
        if (_sequence is null || _sequencePlayer is null)
        {
            _lastSequenceExportStatus = "Failed";
            _lastSequenceExportError = "There is no character sequence to export.";
            return;
        }
        if (_playSession is not null)
        {
            _lastSequenceExportStatus = "Failed";
            _lastSequenceExportError = "Stop play mode before exporting the authored sequence.";
            return;
        }

        try
        {
            var settings = new SequenceFrameExportSettings(request.OutputDirectory,
                request.StartTime, request.EndTime, request.FrameRate, request.Width, request.Height);
            if (settings.EndTime > _sequence.Duration)
                throw new ArgumentOutOfRangeException(nameof(request),
                    $"End time must not exceed the sequence duration of {_sequence.Duration:0.##} seconds.");

            var candidate = new SequenceFrameExportJob(settings, _sequence.Name, CaptureSequenceAssetVersions());
            _sequenceExportRestoreTime = _sequencePlayer.Time;
            _sequenceExportRestoreWasPlaying = _sequencePlayer.IsPlaying;
            _sequenceExportRestorePreviewEnabled = _sequencePreviewEnabled;
            _sequenceExportRestoreCameraPosition = _camera.Position;
            _sequenceExportRestoreCameraRotation = Quaternion.CreateFromRotationMatrix(Matrix.Invert(_camera.View));
            var projectionScale = _camera.Projection.M22;
            _sequenceExportRestoreFieldOfView = projectionScale > 0f
                ? MathHelper.ToDegrees(2f * MathF.Atan(1f / projectionScale))
                : 60f;

            _sequencePlayer.Pause();
            _sequenceExportJob = candidate;
            _lastSequenceExportStatus = "Running";
            _lastSequenceExportDirectory = settings.OutputDirectory;
            _lastSequenceExportError = null;
        }
        catch (Exception exception)
        {
            _lastSequenceExportStatus = "Failed";
            _lastSequenceExportError = $"{exception.GetType().Name}: {exception.Message}";
        }
    }

    private IReadOnlyList<SequenceExportAssetVersion> CaptureSequenceAssetVersions()
    {
        var scene = CurrentScene;
        var references = scene.Objects
            .SelectMany(EnumerateAssetReferences)
            .GroupBy(reference => reference.AssetId)
            .ToDictionary(group => group.Key, group => group.First());
        var resolvedPaths = ResolveSceneAssets(scene);
        var versions = new List<SequenceExportAssetVersion>();
        foreach (var (assetId, reference) in references.OrderBy(pair => pair.Key))
        {
            var fullPath = resolvedPaths[assetId];
            using var stream = File.OpenRead(fullPath);
            var byteLength = stream.Length;
            var sha256 = Convert.ToHexString(SHA256.HashData(stream));
            versions.Add(new SequenceExportAssetVersion(assetId, reference.SourcePath, sha256, byteLength));
        }
        return versions;
    }

    private void CancelSequenceExport()
    {
        if (_sequenceExportJob?.IsRunning != true) return;
        _sequenceExportJob.Cancel();
        FinishSequenceExport();
    }

    private void RenderSequenceExportFrame(SequenceFrameExportRequest frame)
    {
        var sequence = _sequence ?? throw new InvalidOperationException("The sequence was removed during export.");
        var player = _sequencePlayer ?? throw new InvalidOperationException("The sequence player was removed during export.");
        try
        {
            player.Seek(frame.Time);
            _sequencePreviewEnabled = true;
            ApplySequenceAtCurrentTime(frame.Width / (float)frame.Height);
            if (!_sequencePreviewEnabled)
                throw new InvalidOperationException(_reimportStatus);

            var sceneBounds = GetSceneBounds() ?? new Bounds3(new Vector3(-1f), Vector3.One);
            var lightViewProjection = DirectionalShadowCamera.CreateViewProjection(
                sceneBounds, _camera.View, _camera.Projection, _sceneLighting.DirectionalDirection,
                _shadowMap!.Size);
            _sequenceExportTarget ??= new SequenceFrameRenderTarget(GraphicsDevice);
            _sequenceExportTarget.RenderPng(frame.Width, frame.Height, frame.OutputPath, () =>
            {
                RenderShadowMap(lightViewProjection);
                GraphicsDevice.DepthStencilState = DepthStencilState.Default;
                GraphicsDevice.BlendState = BlendState.Opaque;
                GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
                DrawShadowedScene(lightViewProjection);
            });
        }
        finally
        {
            RestoreSequencePreviewAfterExportFrame();
        }
    }

    private void RestoreSequencePreviewAfterExportFrame()
    {
        if (_sequencePlayer is not null) _sequencePlayer.Seek(_sequenceExportRestoreTime);
        _sequencePreviewEnabled = _sequenceExportRestorePreviewEnabled;
        if (_sequencePreviewEnabled)
        {
            ApplySequenceAtCurrentTime();
            return;
        }

        if (_preview?.Current is { } preview)
        {
            foreach (var state in preview.CharacterInstances.Values)
                state.Seek(state.Playback?.Time ?? state.Settings.Time);
        }
        _activeSequenceCameraName = null;
        _camera.SetWorldTransform(_sequenceExportRestoreCameraPosition, _sequenceExportRestoreCameraRotation);
        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio,
            _sequenceExportRestoreFieldOfView, far: 1000f);
    }

    private void FinishSequenceExport()
    {
        var job = _sequenceExportJob;
        if (job is null) return;
        RestoreSequencePreviewAfterExportFrame();
        if (_sequencePlayer is not null)
        {
            if (_sequenceExportRestoreWasPlaying) _sequencePlayer.Play();
            else _sequencePlayer.Pause();
        }
        _sequenceExportTarget?.Dispose();
        _sequenceExportTarget = null;
        _lastSequenceExportStatus = job.State.ToString();
        _lastSequenceExportDirectory = job.Settings.OutputDirectory;
        _lastSequenceExportError = job.Error;
        if (job.State == SequenceFrameExportState.Completed)
            Console.WriteLine($"Sequence export completed: {job.CompletedFrames} frames in {job.Settings.OutputDirectory}");
        else
            Console.Error.WriteLine($"Sequence export {job.State.ToString().ToLowerInvariant()}: {job.Error ?? "canceled"}");

        if (_startupSequenceExportDirectory is not null)
        {
            if (job.State != SequenceFrameExportState.Completed) Environment.ExitCode = 1;
            Exit();
        }
    }

    private void SetSequencePlaying(bool playing)
    {
        if (_sequencePlayer is null) return;
        if (playing)
        {
            _sequencePlayer.Play();
            _sequencePreviewEnabled = true;
        }
        else
        {
            _sequencePlayer.Pause();
        }
        ApplySequenceAtCurrentTime();
    }

    private void SeekSequence(float time)
    {
        if (_sequencePlayer is null) return;
        _sequencePlayer.Seek(time);
        _sequencePreviewEnabled = true;
        ApplySequenceAtCurrentTime();
    }

    private void SetSequencePreviewEnabled(bool enabled)
    {
        _sequencePreviewEnabled = enabled;
        if (enabled)
        {
            ApplySequenceAtCurrentTime();
        }
        else
        {
            _sequencePlayer?.Pause();
            _activeSequenceCameraName = null;
            if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);
        }
    }

    private void BuildSequencePreview()
    {
        _sequence = null;
        _sequencePlayer = null;
        _sequencePreviewEnabled = false;
        _activeSequenceCameraName = null;
        if (_preview?.Current is not { } preview) return;

        var characterObject = CurrentScene.Objects.FirstOrDefault(preview.IsSkinnedObject);
        if (characterObject?.GltfAsset is not { } reference
            || !preview.Assets.TryGetValue(reference.AssetId, out var asset)
            || asset.SkinnedCharacter is not { } character) return;
        var clip = character.Animations.FirstOrDefault(item =>
            string.Equals(item.Name, "Walk", StringComparison.OrdinalIgnoreCase))
            ?? character.Animations.FirstOrDefault();
        if (clip is null) return;

        var duration = MathF.Max(2f, clip.Duration * 2f);
        var bounds = GetSceneBounds() ?? new Bounds3(characterObject.Transform.Position - Vector3.One,
            characterObject.Transform.Position + Vector3.One);
        var center = bounds.Center;
        var radius = MathF.Max(4f, bounds.Size.Length() * 0.35f);
        var wideStart = center + new Vector3(0f, radius * 0.22f, radius * 1.55f);
        var wideEnd = center + new Vector3(-radius * 0.42f, radius * 0.25f, radius * 1.48f);
        var closeStart = center + new Vector3(radius * 1.15f, radius * 0.32f, radius * 0.62f);
        var closeEnd = center + new Vector3(radius * 0.98f, radius * 0.28f, -radius * 0.55f);
        var cameras = new[]
        {
            new SequenceCameraTrack(WideCameraTrackId, "Wide", [
                new SequenceCameraKeyframe(0f, CreateCameraKey(wideStart, center, 48f)),
                new SequenceCameraKeyframe(duration, CreateCameraKey(wideEnd, center, 48f))
            ]),
            new SequenceCameraTrack(CloseCameraTrackId, "Close", [
                new SequenceCameraKeyframe(0f, CreateCameraKey(closeStart, center, 42f)),
                new SequenceCameraKeyframe(duration, CreateCameraKey(closeEnd, center, 42f))
            ])
        };
        _sequence = new SceneSequence($"{characterObject.Name} - {clip.Name}", duration,
            [new CharacterClipTrack(characterObject.Id, clip, loop: true)], cameras,
            new SequenceCameraCutTrack([
                new SequenceCameraCutKeyframe(0f, WideCameraTrackId),
                new SequenceCameraCutKeyframe(duration * 0.5f, CloseCameraTrackId)
            ]));
        _sequencePlayer = new SceneSequencePlayer(_sequence);
    }

    private static SequenceCameraTransform CreateCameraKey(Vector3 position, Vector3 target, float fieldOfView)
    {
        var view = Matrix.CreateLookAt(position, target, Vector3.Up);
        var rotation = Quaternion.CreateFromRotationMatrix(Matrix.Invert(view));
        return new SequenceCameraTransform(position, rotation, fieldOfView);
    }

    private void ApplySequenceAtCurrentTime(float? cameraAspect = null)
    {
        if (!_sequencePreviewEnabled || _sequence is null || _sequencePlayer is null
            || _preview?.Current is not { } preview) return;
        try
        {
            var poses = preview.CharacterInstances.ToDictionary(pair => pair.Key, pair => pair.Value.Pose);
            var frame = _sequence.Evaluate(_sequencePlayer.Time, CurrentScene, poses);
            _activeSequenceCameraName = frame.CameraName;
            if (frame.CameraTransform is { } camera)
            {
                _camera.SetWorldTransform(camera.Position, camera.Rotation);
                _camera.SetProjection(cameraAspect ?? GraphicsDevice.Viewport.AspectRatio,
                    camera.FieldOfViewDegrees, far: 1000f);
            }
        }
        catch (InvalidOperationException exception)
        {
            _sequencePlayer.Pause();
            _sequencePreviewEnabled = false;
            _activeSequenceCameraName = null;
            _reimportStatus = $"Sequence preview stopped: {exception.Message}";
        }
    }

}
