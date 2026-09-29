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
    private static float? ParseFiniteFloatOption(string[] args, string name)
    {
        var raw = ParseOption(args, name);
        if (raw is null) return null;
        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !float.IsFinite(value))
        {
            throw new ArgumentException($"Option {name} must be a finite number; received '{raw}'.");
        }

        return value;
    }

    private static int ParseIntOption(string[] args, string name, int defaultValue)
    {
        var raw = ParseOption(args, name);
        if (raw is null) return defaultValue;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new ArgumentException($"Option {name} must be an integer; received '{raw}'.");
        return value;
    }

    private void ApplyCommandLineSettings()
    {
        var preview = _preview?.Current
            ?? throw new InvalidOperationException("Character assets must load before command-line playback settings are applied.");
        var characters = _sceneData.Objects
            .Where(item => preview.IsSkinnedObject(item))
            .ToArray();
        if (_playbackOptions.Pair && characters.Length < 2)
            throw new ArgumentException("--pair requires two scene objects that reference a skinned character asset.");

        if (_playbackOptions.AnimationName is { } primaryClip && characters.Length > 0)
        {
            var settings = characters[0].CharacterSettings ??= new GltfCharacterSettings();
            settings.ClipName = primaryClip;
            settings.Time = _playbackOptions.AnimationTime ?? 0f;
            settings.Speed = _playbackOptions.AnimationSpeed;
            settings.Loop = _playbackOptions.LoopAnimation;
            settings.IsPlaying = !_playbackOptions.PauseAnimation;
        }

        if ((_playbackOptions.HasSecondOptions
                || (_playbackOptions.Pair && _playbackOptions.AnimationName is not null))
            && characters.Length > 1)
        {
            var settings = characters[1].CharacterSettings ??= new GltfCharacterSettings();
            if (_playbackOptions.SecondAnimationName is { } secondClip)
                settings.ClipName = secondClip;
            settings.Time = _playbackOptions.SecondAnimationTime
                ?? _playbackOptions.AnimationTime ?? 0f;
            settings.Speed = _playbackOptions.SecondAnimationSpeed;
            settings.Loop = _playbackOptions.SecondLoopAnimation;
            settings.IsPlaying = !_playbackOptions.PauseSecond;
        }

        if (_playbackOptions.CrossfadeAnimationName is { } crossfadeClip && characters.Length > 0)
        {
            var settings = characters[0].CharacterSettings ??= new GltfCharacterSettings();
            settings.CrossfadeClipName = crossfadeClip;
            settings.BlendAmount = _playbackOptions.BlendAmount;
        }

        if (_playbackOptions.AttachHand)
        {
            if (characters.Length == 0)
                throw new ArgumentException("--attach-hand requires a scene object that references a skinned character asset.");
            var settings = characters[0].CharacterSettings ??= new GltfCharacterSettings();
            if (settings.Attachments.All(item => item.Id != HandPreviewAttachmentId))
            {
                settings.Attachments.Add(new GltfBoneAttachmentReference(HandPreviewAttachmentId,
                    "b_RightHand_08", Matrix.CreateScale(9f) * Matrix.CreateTranslation(0f, 0f, 12f)));
            }
        }
    }

    private void AfterSceneStructureChange() => _preview?.Current.RebuildCharacterInstances(CurrentScene);

    private void RunLifecycleSmoke()
    {
        if (_preview is null)
            throw new InvalidOperationException("Lifecycle smoke requires a loaded graphics preview.");

        var runId = Guid.NewGuid();
        var runStartedUtc = DateTimeOffset.UtcNow;
        var outputDirectory = Path.Combine(Path.GetTempPath(), "Ember", "CharacterStudio", "LifecycleChecks");
        Directory.CreateDirectory(outputDirectory);
        var scenePath = Path.Combine(outputDirectory, $"lifecycle-source-{runId:N}.scene.json");
        var verifyPath = Path.Combine(outputDirectory, $"lifecycle-verify-{runId:N}.scene.json");
        var markdownPath = Path.Combine(outputDirectory, $"lifecycle-check-{runId:N}.md");
        var jsonPath = Path.Combine(outputDirectory, $"lifecycle-check-{runId:N}.json");
        SceneFile.SaveAtomic(_sceneData, scenePath);
        _lifecycleSmoke = new LifecycleSmokeState
        {
            RunId = runId,
            StartedUtc = runStartedUtc,
            ScenePath = scenePath,
            VerifyPath = verifyPath,
            MarkdownPath = markdownPath,
            JsonPath = jsonPath,
            AuthoredSnapshot = File.ReadAllText(scenePath),
            InitialResourceCount = _preview.Current.OwnedGraphicsResourceCount,
            InitialSceneIds = _sceneData.Objects.Select(item => item.Id).OrderBy(id => id).ToArray()
        };
        Console.WriteLine($"CharacterStudio: live lifecycle check started; {_lifecycleSmokeCycles} play/stop and reload cycles will run across rendered frames.");
    }

    private void AdvanceLifecycleSmoke()
    {
        if (_lifecycleSmoke is not { } state) return;

        try
        {
            switch (state.Stage)
            {
                case LifecycleSmokeStage.StartPlaySession:
                {
                    var cycle = state.CompletedCycles + 1;
                    StartPlaySession();
                    if (_playSession is null || !_playSession.Behaviours.IsStarted
                        || ReferenceEquals(_sceneData, _playSession.RuntimeScene))
                        throw new InvalidOperationException($"Play cycle {cycle} did not start an isolated runtime scene.");
                    if (_reimportStatus.Contains("cleanup", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Play cycle {cycle} reported a preview cleanup problem: {_reimportStatus}");
                    state.Stage = LifecycleSmokeStage.StopPlaySession;
                    break;
                }
                case LifecycleSmokeStage.StopPlaySession:
                {
                    var cycle = state.CompletedCycles + 1;
                    StopPlaySession();
                    if (_playSession is not null)
                        throw new InvalidOperationException($"Play cycle {cycle} did not stop and restore the authored scene.");
                    if (_reimportStatus.Contains("issue", StringComparison.OrdinalIgnoreCase)
                        || _reimportStatus.Contains("cleanup", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Play cycle {cycle} reported cleanup trouble: {_reimportStatus}");

                    SceneFile.SaveAtomic(_sceneData, state.VerifyPath);
                    if (!string.Equals(state.AuthoredSnapshot, File.ReadAllText(state.VerifyPath), StringComparison.Ordinal))
                        throw new InvalidDataException($"Play cycle {cycle} changed authored scene data.");
                    state.Stage = LifecycleSmokeStage.ReloadScene;
                    break;
                }
                case LifecycleSmokeStage.ReloadScene:
                {
                    var cycle = state.CompletedCycles + 1;
                    var reloadedScene = SceneFile.Load(state.ScenePath);
                    var reloadedIds = reloadedScene.Objects.Select(item => item.Id).OrderBy(id => id).ToArray();
                    if (!state.InitialSceneIds.SequenceEqual(reloadedIds))
                        throw new InvalidDataException($"Scene reload {cycle} changed the authored object IDs.");
                    var cleanupError = _preview!.Reload(() => PreviewResources.Load(
                        GraphicsDevice, ResolveSceneAssets(reloadedScene, _sceneAssetRoot), reloadedScene));
                    if (cleanupError is not null)
                        throw new InvalidOperationException($"Scene reload {cycle} could not release the previous preview.", cleanupError);
                    _sceneData = reloadedScene;
                    _editorHistory = new SceneCommandHistory();
                    _editorUi?.SetHistory(_editorHistory);
                    _editorUi?.ResetSceneSelection();
                    _farLodByObjectId.Clear();
                    BuildSequencePreview();

                    var resources = _preview.Current.OwnedGraphicsResourceCount;
                    if (resources != state.InitialResourceCount)
                        throw new InvalidOperationException($"Preview resource count changed after cycle {cycle}: expected {state.InitialResourceCount}, actual {resources}.");
                    using (var process = Process.GetCurrentProcess())
                    {
                        process.Refresh();
                        state.Samples.Add(new LifecycleCycleSample(cycle, true, true, true,
                            _sceneData.Objects.Count, resources, process.WorkingSet64, GC.GetTotalMemory(false)));
                    }
                    state.CompletedCycles++;
                    state.Stage = state.CompletedCycles == _lifecycleSmokeCycles
                        ? LifecycleSmokeStage.Complete
                        : LifecycleSmokeStage.StartPlaySession;
                    break;
                }
                case LifecycleSmokeStage.Complete:
                    WriteLifecycleSmokeReport(state);
                    break;
            }
        }
        catch (Exception error)
        {
            state.Errors.Add($"{error.GetType().Name}: {error.Message}");
            if (_playSession is not null)
            {
                try
                {
                    StopPlaySession();
                    if (_playSession is not null)
                        state.Errors.Add("Cleanup: play session remained active after the lifecycle check failed.");
                }
                catch (Exception cleanupError)
                {
                    state.Errors.Add($"Cleanup: {cleanupError.GetType().Name}: {cleanupError.Message}");
                }
            }
            state.Stage = LifecycleSmokeStage.Complete;
        }
    }

    private void WriteLifecycleSmokeReport(LifecycleSmokeState state)
    {
        var passed = state.Errors.Count == 0 && state.CompletedCycles == _lifecycleSmokeCycles;
        var payload = new
        {
            schemaVersion = 1,
            run = new
            {
                runId = state.RunId,
                startedUtc = state.StartedUtc,
                generatedUtc = DateTimeOffset.UtcNow,
                machineName = Environment.MachineName,
                operatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                adapter = GraphicsDevice.Adapter.Description,
                viewportWidth = GraphicsDevice.Viewport.Width,
                viewportHeight = GraphicsDevice.Viewport.Height,
                targetCycles = _lifecycleSmokeCycles,
                completedCycles = state.CompletedCycles,
                initialOwnedPreviewGraphicsResources = state.InitialResourceCount,
                passed
            },
            errors = state.Errors,
            cycles = state.Samples
        };
        var report = new StringBuilder();
        report.AppendLine("# CharacterStudio Play/Stop and Scene Reload Lifecycle Check");
        report.AppendLine();
        report.AppendLine($"Run ID: {state.RunId:N}");
        report.AppendLine($"Target/completed cycles: {_lifecycleSmokeCycles}/{state.CompletedCycles}");
        report.AppendLine($"Initial owned preview graphics resources: {state.InitialResourceCount}");
        report.AppendLine($"Adapter/resolution: {GraphicsDevice.Adapter.Description}, {GraphicsDevice.Viewport.Width}x{GraphicsDevice.Viewport.Height}");
        report.AppendLine("Lifecycle steps were advanced across rendered frames.");
        report.AppendLine();
        report.AppendLine("| Cycle | Play started | Play stopped | Scene reloaded | Preview resources | Working set MiB | Managed heap MiB |");
        report.AppendLine("| ---: | --- | --- | --- | ---: | ---: | ---: |");
        foreach (var sample in state.Samples)
        {
            report.AppendLine($"| {sample.Cycle} | {sample.PlayStarted} | {sample.PlayStopped} | {sample.SceneReloaded} | {sample.OwnedPreviewGraphicsResources} | {sample.WorkingSetBytes / (1024d * 1024d):F1} | {sample.ManagedHeapBytes / (1024d * 1024d):F1} |");
        }
        if (state.Errors.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## Errors");
            foreach (var error in state.Errors) report.AppendLine($"- {error}");
        }
        report.AppendLine();
        report.AppendLine(passed ? "**PASS** - play/stop and repeated scene reload completed with stable owned preview resources."
            : "**FAIL** - one or more play/stop or scene reload checks failed.");
        File.WriteAllText(state.JsonPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        File.WriteAllText(state.MarkdownPath, report.ToString(), Encoding.UTF8);
        Console.WriteLine(report.ToString());
        Console.WriteLine($"CharacterStudio lifecycle report saved to {state.MarkdownPath}");
        Console.WriteLine($"CharacterStudio lifecycle samples saved to {state.JsonPath}");
        Environment.ExitCode = passed ? 0 : 1;
        _lifecycleSmoke = null;
        Exit();
    }

    private void RunReferenceSession()
    {
        if (_preview is null)
            throw new InvalidOperationException("Reference session requires a loaded graphics preview.");

        var outputDirectory = Path.Combine(Path.GetTempPath(), "Ember", "CharacterStudio", "ReferenceSessions");
        Directory.CreateDirectory(outputDirectory);
        var referenceScene = _playbackOptions.Pair
            ? "CharacterStudio Fox pair (two skinned instances sharing one GLB)"
            : $"CharacterStudio startup scene ({_sceneData.Objects.Count} authored objects)";
        _referenceSession = new CharacterStudioReferenceSession(
            TimeSpan.FromMinutes(_referenceSessionMinutes), referenceScene);
        _referenceMarkdownPath = Path.Combine(outputDirectory, $"reference-session-{_referenceSession.RunId:N}.md");
        _referenceJsonPath = Path.Combine(outputDirectory, $"reference-session-{_referenceSession.RunId:N}.json");
        _referenceFrameClock.Restart();
        _referenceFirstFrame = true;
        _referenceNextModeSwitchSeconds = 60d;
        _referenceNextMemorySampleSeconds = 1d;
        _referenceSession.RecordModeChange("Editor");
        RecordReferenceMemorySample(_referenceSession);
        Console.WriteLine($"CharacterStudio: mixed editor/runtime reference session started; target={_referenceSessionMinutes} minutes; modes switch every 60 seconds.");
    }

    private void RecordReferenceFrameInterval()
    {
        if (_referenceSession is null) return;
        var interval = _referenceFrameClock.Elapsed.TotalMilliseconds;
        _referenceFrameClock.Restart();
        if (_referenceFirstFrame)
        {
            _referenceFirstFrame = false;
            return;
        }

        _referenceSession.RecordFrameInterval(interval);
    }

    private void AdvanceReferenceSession()
    {
        if (_referenceSession is not { } session) return;

        try
        {
            if (session.TargetReached)
            {
                StopReferenceRuntimeIfNeeded(session);
                RecordReferenceMemorySample(session);
                WriteReferenceSessionReport(session);
                return;
            }

            while (session.ElapsedSeconds >= _referenceNextModeSwitchSeconds
                && _referenceNextModeSwitchSeconds < session.TargetDuration.TotalSeconds)
            {
                var transitionAt = session.ElapsedSeconds;
                if (_playSession is null)
                {
                    StartPlaySession();
                    if (_playSession is null)
                        throw new InvalidOperationException($"Could not enter runtime mode: {_reimportStatus}");
                    if (_reimportStatus.Contains("cleanup", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Runtime preview cleanup reported an issue: {_reimportStatus}");
                    session.RecordModeChange("Runtime");
                }
                else
                {
                    StopPlaySession();
                    if (_playSession is not null)
                        throw new InvalidOperationException($"Could not return to editor mode: {_reimportStatus}");
                    if (_reimportStatus.Contains("issue", StringComparison.OrdinalIgnoreCase)
                        || _reimportStatus.Contains("cleanup", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Editor preview cleanup reported an issue: {_reimportStatus}");
                    session.RecordModeChange("Editor");
                }

                _referenceNextModeSwitchSeconds += 60d;
                if (session.ElapsedSeconds - transitionAt > 10d)
                    throw new TimeoutException("A reference-session editor/runtime transition exceeded 10 seconds.");
            }

            if (session.ElapsedSeconds >= _referenceNextMemorySampleSeconds)
            {
                RecordReferenceMemorySample(session);
                do { _referenceNextMemorySampleSeconds += 1d; }
                while (_referenceNextMemorySampleSeconds <= session.ElapsedSeconds);
            }
        }
        catch (Exception error)
        {
            session.RecordError($"{error.GetType().Name}: {error.Message}");
            try { StopReferenceRuntimeIfNeeded(session); }
            catch (Exception cleanupError)
            {
                session.RecordError($"Cleanup: {cleanupError.GetType().Name}: {cleanupError.Message}");
            }
            RecordReferenceMemorySample(session);
            WriteReferenceSessionReport(session);
        }
    }

    private void StopReferenceRuntimeIfNeeded(CharacterStudioReferenceSession session)
    {
        if (_playSession is null) return;
        StopPlaySession();
        if (_playSession is not null)
            throw new InvalidOperationException($"Runtime session did not stop cleanly: {_reimportStatus}");
        if (_reimportStatus.Contains("issue", StringComparison.OrdinalIgnoreCase)
            || _reimportStatus.Contains("cleanup", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Runtime cleanup reported an issue: {_reimportStatus}");
        session.RecordModeChange("Editor");
    }

    private void RecordReferenceMemorySample(CharacterStudioReferenceSession session)
    {
        var preview = _preview?.Current;
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        session.RecordSample(new CharacterStudioReferenceSession.MemorySample(
            session.ElapsedSeconds,
            _playSession is null ? "Editor" : "Runtime",
            CurrentScene.Objects.Count,
            preview?.Assets.Count ?? 0,
            preview?.CharacterInstances.Count ?? 0,
            preview?.OwnedGraphicsResourceCount ?? 0,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            GC.GetTotalMemory(false)));
    }

    private void WriteReferenceSessionReport(CharacterStudioReferenceSession session)
    {
        var passed = session.TargetReached && session.Errors.Count == 0 && _playSession is null;
        var adapter = GraphicsDevice.Adapter.Description;
        var width = GraphicsDevice.Viewport.Width;
        var height = GraphicsDevice.Viewport.Height;
        try
        {
            File.WriteAllText(_referenceJsonPath!, session.BuildRawData(adapter, width, height, passed), Encoding.UTF8);
            File.WriteAllText(_referenceMarkdownPath!, session.BuildReport(adapter, width, height, passed), Encoding.UTF8);
            Console.WriteLine(session.BuildReport(adapter, width, height, passed));
            Console.WriteLine($"CharacterStudio reference report saved to {_referenceMarkdownPath}");
            Console.WriteLine($"CharacterStudio reference samples saved to {_referenceJsonPath}");
        }
        catch (Exception error)
        {
            passed = false;
            Console.Error.WriteLine($"CharacterStudio reference report export failed: {error}");
        }

        Environment.ExitCode = passed ? 0 : 1;
        _referenceSession = null;
        Exit();
    }

}
