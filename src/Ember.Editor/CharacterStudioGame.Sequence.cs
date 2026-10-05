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
            _sequencePlayer.IsPlaying, _sequencePreviewEnabled, cameraName, _sequence.TriggerObjectId,
            _sequenceIsDirty);
    }

    private void SetSequenceStatus(string status)
    {
        _reimportStatus = status;
        _editorUi?.ReportWorkspaceStatus(status);
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
            if (_playSession is not null)
            {
                try { _playController.BeginSequenceCutscene(); }
                catch (Exception exception)
                {
                    SetSequenceStatus($"Could not pause Play for the sequence: {exception.Message}");
                    return;
                }
            }
            _sequencePlayer.Play();
            _sequencePreviewEnabled = true;
        }
        else
        {
            _sequencePlayer.Pause();
        }
        ApplySequenceAtCurrentTime();
    }

    private void SetSequenceTrigger(Guid? triggerObjectId)
    {
        if (_sequence is null || _sequencePlayer is null) return;
        if (_sequence.TriggerObjectId == triggerObjectId) return;
        if (_playSession is not null)
        {
            SetSequenceStatus("Stop Play before changing the saved cutscene trigger.");
            return;
        }
        if (triggerObjectId is { } id)
        {
            var trigger = _sceneData.Find(id);
            if (trigger is null || !trigger.Enabled || trigger.BoxCollider is not { IsTrigger: true }
                || trigger.TriggerAction?.Kind != SceneTriggerActionKind.ReachGoal)
            {
                SetSequenceStatus($"Choose an enabled Reach goal trigger with a trigger collider; object {id} does not qualify.");
                return;
            }
        }

        try
        {
            var previous = _sequence;
            var previousPlayer = _sequencePlayer;
            _sequence = new SceneSequence(previous.Name, previous.Duration, previous.CharacterTracks,
                previous.CameraTracks, previous.CameraCuts, triggerObjectId);
            _sequencePlayer = new SceneSequencePlayer(_sequence);
            _sequencePlayer.Seek(previousPlayer.Time);
            if (previousPlayer.IsPlaying) _sequencePlayer.Play();
            _sequenceIsDirty = true;
            SetSequenceStatus(triggerObjectId is { } selectedId
                ? $"Cutscene will start at Reach goal trigger '{_sceneData.Find(selectedId)!.Name}'. Save the sequence to keep this link."
                : "Cutscene trigger cleared. Save the sequence to keep this change.");
        }
        catch (Exception exception)
        {
            SetSequenceStatus($"Could not change the cutscene trigger: {exception.Message}");
        }
    }

    private void OpenSequenceFromUi()
    {
        if (_sequenceIsDirty)
        {
            SetSequenceStatus("Save the current sequence before opening another one.");
            return;
        }
        if (_playSession is not null)
        {
            SetSequenceStatus("Stop Play before opening a different sequence.");
            return;
        }

        var initialPath = _sequenceFilePath ?? _sceneSavePath;
        var initialDirectory = initialPath is null
            ? _project?.RootDirectory
            : Path.GetDirectoryName(Path.GetFullPath(initialPath));
        var path = CharacterStudioFilePickers.PickSequenceFile(
            initialDirectory, GraphicsDevice.PresentationParameters.DeviceWindowHandle);
        if (path is null) return;

        try
        {
            var preview = _preview?.Current
                ?? throw new InvalidOperationException("The scene preview is not ready.");
            var candidate = SequenceFile.Load(path, _sceneData, BuildSequenceClipCatalog(preview));
            _sequence = candidate;
            _sequencePlayer = new SceneSequencePlayer(candidate);
            _sequenceFilePath = Path.GetFullPath(path);
            _sequenceIsDirty = false;
            _sequencePreviewEnabled = false;
            _activeSequenceCameraName = null;
            SetSequenceStatus($"Opened sequence '{candidate.Name}'.");
        }
        catch (Exception exception)
        {
            SetSequenceStatus($"Could not open sequence '{Path.GetFileName(path)}': {exception.Message}");
        }
    }

    private void SaveSequenceFromUi()
    {
        if (_playSession is not null)
        {
            SetSequenceStatus("Stop Play before saving an authored sequence.");
            return;
        }
        if (_sequence is null)
        {
            SetSequenceStatus("Add a skinned character before saving a sequence.");
            return;
        }

        var path = PickSequenceSavePath();
        if (path is null) return;

        _ = SaveSequenceToPath(path);
    }

    private bool SaveSequenceForUnsavedDecision()
    {
        if (!_sequenceIsDirty) return true;
        if (_playSession is not null)
        {
            SetSequenceStatus("Stop Play before saving an authored sequence.");
            return false;
        }
        if (_sequence is null)
        {
            SetSequenceStatus("There is no sequence to save.");
            return false;
        }

        var path = _sequenceFilePath ?? PickSequenceSavePath();
        return path is not null && SaveSequenceToPath(path);
    }

    private string? PickSequenceSavePath()
    {
        var initialPath = _sequenceFilePath ?? _sceneSavePath;
        var initialDirectory = initialPath is null
            ? _project?.RootDirectory
            : Path.GetDirectoryName(Path.GetFullPath(initialPath));
        var suggestedName = string.IsNullOrWhiteSpace(_sequenceFilePath)
            ? $"{string.Concat(_sequence!.Name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries))}.sequence.json"
            : Path.GetFileName(_sequenceFilePath);
        return CharacterStudioFilePickers.PickSequenceSaveFile(
            initialDirectory, suggestedName, GraphicsDevice.PresentationParameters.DeviceWindowHandle);
    }

    private bool SaveSequenceToPath(string path)
    {
        if (_sequence is null)
        {
            SetSequenceStatus("Add a skinned character before saving a sequence.");
            return false;
        }
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
            SequenceFile.SaveAtomic(_sequence, _sceneData, fullPath);
            _sequenceFilePath = fullPath;
            _sequenceIsDirty = false;
        }
        catch (Exception exception)
        {
            SetSequenceStatus($"Could not save sequence: {exception.Message}");
            return false;
        }

        var savedMessage = $"Saved sequence '{_sequence.Name}' to {Path.GetFileName(fullPath)}.";
        try
        {
            var packageStatus = RegisterSequenceForProjectPackaging(fullPath);
            SetSequenceStatus(packageStatus is null ? savedMessage : $"{savedMessage} {packageStatus}");
        }
        catch (Exception exception)
        {
            SetSequenceStatus($"{savedMessage} Could not add it to project packaging: {exception.Message}");
        }
        return true;
    }

    private string? RegisterSequenceForProjectPackaging(string sequencePath)
    {
        if (_project is null) return null;
        var fullPath = Path.GetFullPath(sequencePath);
        var relativePath = Path.GetRelativePath(_project.RootDirectory, fullPath);
        if (Path.IsPathRooted(relativePath) || relativePath == ".."
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            return "It is outside the project, so it will not be included when packaging.";

        _project = _project.RegisterExtraContentPath(relativePath);
        return "Added to project packaging.";
    }

    internal void StartSequenceCutsceneFromTrigger()
    {
        if (_playSession is null || _sequence is null || _sequencePlayer is null
            || _sequence.TriggerObjectId is null)
            return;

        try
        {
            _playController.BeginSequenceCutscene();
            _sequencePlayer.Seek(0f);
            _sequencePlayer.Play();
            _sequencePreviewEnabled = true;
            ApplySequenceAtCurrentTime();
            SetSequenceStatus($"Reached the cutscene trigger. Playing '{_sequence.Name}'.");
        }
        catch (Exception exception)
        {
            _playController.CancelSequenceCutscene();
            SetSequenceStatus($"Could not start '{_sequence.Name}': {exception.Message}");
        }
    }

    internal void RestorePlayPreviewAfterCutscene(float cameraTargetDistance, float cameraYaw,
        float cameraPitch, Vector3 cameraTarget)
    {
        _sequencePlayer?.Pause();
        _sequencePreviewEnabled = false;
        _activeSequenceCameraName = null;
        if (_preview?.Current is { } preview)
        {
            foreach (var state in preview.CharacterInstances.Values)
                state.Seek(state.Playback?.Time ?? state.Settings.Time);
        }
        _camera.Reset(cameraTarget, cameraTargetDistance, cameraYaw, cameraPitch);
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
            if (_playController.IsSequenceCutsceneActive)
            {
                _playController.CancelSequenceCutscene();
                return;
            }
            _activeSequenceCameraName = null;
            if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);
        }
    }

    private void BuildSequencePreview(bool preserveCurrentSequence = false)
    {
        var previousSequence = preserveCurrentSequence ? _sequence : null;
        var currentPreview = _preview?.Current;
        if (previousSequence is not null && currentPreview is not null
            && previousSequence.CharacterTracks.All(track =>
                CurrentScene.Find(track.TargetObjectId) is not null
                && currentPreview.CharacterInstances.ContainsKey(track.TargetObjectId))
            && (previousSequence.TriggerObjectId is null
                || CurrentScene.Find(previousSequence.TriggerObjectId.Value) is
                    { Enabled: true, BoxCollider: { IsTrigger: true }, TriggerAction.Kind: SceneTriggerActionKind.ReachGoal }))
        {
            _sequence = previousSequence;
            _sequencePlayer = new SceneSequencePlayer(previousSequence);
            _sequencePreviewEnabled = false;
            _activeSequenceCameraName = null;
            return;
        }

        _sequenceFilePath = null;
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
            if (_playController.IsSequenceCutsceneActive)
            {
                _playController.CompleteSequenceCutscene();
                SetSequenceStatus($"Sequence preview stopped: {exception.Message} Play was restored.");
            }
            else
            {
                SetSequenceStatus($"Sequence preview stopped: {exception.Message}");
            }
        }
    }

}
