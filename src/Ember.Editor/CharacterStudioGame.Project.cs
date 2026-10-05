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
    private bool SaveScene()
    {
        if (_playSession is not null)
        {
            _reimportStatus = "Stop play mode before saving the authored scene.";
            return false;
        }
        if (_sceneSavePath is null)
        {
            _reimportStatus = _blockedSaveReason ?? "Pass --save <path> to enable S: save scene";
            return false;
        }

        _editorUi?.CompletePendingEdit(_sceneData);
        try
        {
            SceneFile.SaveAtomic(_sceneData, _sceneSavePath);
            _editorHistory.MarkSaved();
            _reimportStatus = $"Scene saved to {Path.GetFileName(_sceneSavePath)}.";
            Console.WriteLine($"Saved scene to {Path.GetFullPath(_sceneSavePath)}");
            return true;
        }
        catch (Exception exception)
        {
            _reimportStatus = $"Scene save failed: {exception.Message}";
            Console.WriteLine($"CharacterStudio scene save failed: {exception.Message}");
            return false;
        }
    }

    private void OnGameExiting(object? sender, ExitingEventArgs args)
    {
        if (_allowExitAfterConfirmation || _editorUi is null) return;
        if (_playSession is not null)
        {
            StopPlaySession();
            if (_playSession is not null)
            {
                args.Cancel = true;
                return;
            }
        }

        _editorUi.CompletePendingEdit(_sceneData);
        if (!_editorHistory.IsDirty && !_sequenceIsDirty) return;
        args.Cancel = true;
        _editorUi.RequestCloseConfirmation();
    }

    private void ConfirmExitAfterSaveOrDiscard()
    {
        _allowExitAfterConfirmation = true;
        Exit();
    }

    private void SaveSceneAs(string path)
    {
        if (_playSession is not null)
            throw new InvalidOperationException("Stop play mode before saving the authored scene.");
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("A scene save path is required.", nameof(path));

        _editorUi?.CompletePendingEdit(_sceneData);
        var fullPath = Path.GetFullPath(path);
        SceneFile.SaveAtomic(_sceneData, fullPath);
        _sceneSavePath = fullPath;
        _editorHistory.MarkSaved();
        _reimportStatus = $"Scene saved to {Path.GetFileName(fullPath)}.";
        Console.WriteLine($"Saved scene to {fullPath}");
    }

    private string CreateProjectForEditor(string projectDirectory, bool isFilm)
    {
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDirectory));
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException($"Project destination already exists: '{destination}'.");

        var parent = Path.GetDirectoryName(destination)
            ?? throw new InvalidDataException("Project destination has no parent directory.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".{Path.GetFileName(destination)}.starter-{Guid.NewGuid():N}");
        try
        {
            var stagedProject = EngineProjectWorkspace.CreateEmpty(staging);
            var bundledContent = AppContext.BaseDirectory;
            var projectAssets = stagedProject.ResolveContentPath("Assets");
            foreach (var assetName in new[] { "Fox.glb", "ReleaseACourtyard.glb", "README.md" })
            {
                var source = Path.Combine(bundledContent, "Assets", assetName);
                if (!File.Exists(source))
                    throw new FileNotFoundException($"The bundled starter asset '{assetName}' is missing.", source);
                File.Copy(source, Path.Combine(projectAssets, assetName));
            }

            var templateName = isFilm ? "FilmStarter.json" : "ReleaseAShowcase.json";
            var sourceScene = Path.Combine(bundledContent, "Scenes", templateName);
            if (!File.Exists(sourceScene))
                throw new FileNotFoundException($"The bundled starter scene '{templateName}' is missing.", sourceScene);
            File.Copy(sourceScene, stagedProject.ResolveStartupScenePath(), overwrite: true);
            Directory.Move(staging, destination);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }

        var projectPath = Path.Combine(destination, EngineProjectFile.DefaultFileName);
        var openedMessage = OpenProjectForEditor(projectPath, created: true);
        if (_sceneData.Objects.FirstOrDefault(item => item.CharacterSettings is not null) is { } starterCharacter)
            _editorUi?.SelectObject(starterCharacter.Id, starterCharacter.GltfAsset?.AssetId);
        return isFilm
            ? $"{openedMessage} The starter includes an animated character and a courtyard for your first shot."
            : $"{openedMessage} The starter includes a courtyard and two animated characters to explore and edit.";
    }

    private string OpenProjectForEditor(string projectPath, bool created = false)
    {
        if (_playSession is not null)
            throw new InvalidOperationException("Stop play mode before opening another project.");
        if (_sequenceIsDirty)
            throw new IOException("The current sequence has unsaved edits. Save it before switching projects.");
        if (_sequenceExportJob?.IsRunning == true)
            throw new InvalidOperationException("Wait for sequence export to finish before opening another project.");
        if (string.IsNullOrWhiteSpace(projectPath))
            throw new ArgumentException("A project file or project directory is required.", nameof(projectPath));

        var fullProjectPath = Directory.Exists(projectPath)
            ? Path.Combine(Path.GetFullPath(projectPath), EngineProjectFile.DefaultFileName)
            : Path.GetFullPath(projectPath);
        var project = EngineProjectFile.Load(fullProjectPath);
        var scenePath = EngineProjectWorkspace.ResolveInitialScenePath(project);
        var scene = SceneFile.Load(scenePath);
        PreviewResources? candidatePreview = null;
        try
        {
            var loadedPreview = PreviewResources.Load(GraphicsDevice,
                ResolveSceneAssets(scene, project.RootDirectory, project), scene);
            candidatePreview = loadedPreview;

            _editorUi?.CompletePendingEdit(_sceneData);
            if (_editorHistory.IsDirty)
            {
                if (_sceneSavePath is null)
                    throw new IOException("The current scene has unsaved edits. Save it before switching projects.");
                if (!SaveScene())
                    throw new IOException($"The current scene could not be saved; project switch cancelled. {_reimportStatus}");
            }
            if (_preview is null)
                throw new InvalidOperationException("The editor preview is not ready to switch projects.");

            CancelPendingAssetPreview();
            var cleanupError = _preview.Reload(() => loadedPreview);
            candidatePreview = null;
            _project = project;
            _sceneAssetRoot = project.RootDirectory;
            _sceneData = scene;
            InvalidateProjectAssetCatalog();
            _sceneSavePath = scenePath;
            _blockedSaveReason = null;
            _editorHistory = new SceneCommandHistory();
            _editorUi?.SetHistory(_editorHistory);
            _sequence = null;
            _sequencePlayer = null;
            _sequenceFilePath = null;
            _sequenceIsDirty = false;
            _sequencePreviewEnabled = false;
            _activeSequenceCameraName = null;
            _editorUi?.OnProjectOpened(project.RootDirectory, project.WorldManifestPath is null
                ? null
                : project.ResolveWorldManifestPath());
            BuildSequencePreview();
            if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);
            RecordRecentProject(project.FilePath);

            var verb = created ? "Created and opened" : "Opened";
            var message = $"{verb} project '{Path.GetFileName(project.RootDirectory)}'.";
            if (cleanupError is not null) message += $" Previous preview cleanup reported: {cleanupError.Message}";
            return message;
        }
        finally
        {
            candidatePreview?.Dispose();
        }
    }

    private IReadOnlyList<string> LoadRecentProjectPaths() => _projectSession.LoadRecentProjects();

    private IReadOnlyList<GltfAssetReference> GetProjectAssetReferencesForEditor()
    {
        var catalog = _projectSession.GetAssetReferences(_sceneData);

        if (_pendingAssetPlacementPreview is { Import.IsCommitted: false } pendingImport)
            return catalog
                .Where(asset => asset.AssetId != pendingImport.Reference.AssetId)
                .ToArray();

        return catalog;
    }

    private void InvalidateProjectAssetCatalog() => _projectSession.InvalidateAssetCatalog();

    private static IEnumerable<GltfAssetReference> EnumerateProjectSceneAssetReferences(SceneGraph scene)
        => EditorProjectSession.EnumerateProjectSceneAssetReferences(scene);

    private void RecordRecentProject(string projectPath)
    {
        try
        {
            _projectSession.RecordRecentProject(projectPath);
        }
        catch (Exception exception)
        {
            _faults.Add($"Could not update recent projects: {exception.Message}");
        }
    }

    private string PreviewProjectAssetForEditor(GltfAssetReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (_project is null)
            throw new InvalidOperationException("Create or open a project before previewing a project model.");
        if (_playSession is not null)
            throw new InvalidOperationException("Stop play mode before previewing a model.");
        if (_sequenceExportJob?.IsRunning == true)
            throw new InvalidOperationException("Wait for sequence export to finish before previewing a model.");

        var previewScene = new SceneGraph();
        var item = SceneObjectFactory.CreateAssetInstance(previewScene, reference, Vector3.Zero);
        previewScene.Add(item);
        PreviewResources? candidatePreview = null;
        try
        {
            candidatePreview = PreviewResources.Load(GraphicsDevice,
                ResolveSceneAssets(previewScene, _project.RootDirectory, _project), previewScene);
            if (candidatePreview.HasSkinnedCharacters)
                SkinnedEffectCompatibility.Validate(GraphicsDevice.GraphicsProfile,
                    candidatePreview.MaximumJointCount);

            if (GetSceneBounds(includePendingAssetPreview: false) is { } sceneBounds
                && GetSceneObjectBounds(previewScene, item, candidatePreview) is { } modelBounds)
            {
                var gap = Math.Clamp(modelBounds.Size.X * 0.15f, 2f, 20f);
                item.Transform.Position += new Vector3(sceneBounds.Max.X + gap - modelBounds.Min.X, 0f, 0f);
            }

            var pending = new PendingAssetPlacementPreview(reference, null,
                candidatePreview, previewScene, item);
            candidatePreview = null;
            var previousPending = _pendingAssetPlacementPreview;
            _pendingAssetPlacementPreview = pending;
            InvalidateProjectAssetCatalog();
            try { previousPending?.Dispose(); }
            catch (Exception exception) { _faults.Add($"Previous model preview cleanup: {exception.Message}"); }

            if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);
            var message = $"Previewing {Path.GetFileName(reference.SourcePath)} from the project. Choose Add to scene or Cancel preview.";
            _reimportStatus = message;
            return message;
        }
        finally
        {
            candidatePreview?.Dispose();
        }
    }

    private string ReloadProjectAssetForEditor(GltfAssetReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (_project is null)
            throw new InvalidOperationException("Create or open a project before reloading a model.");
        if (_playSession is not null)
            throw new InvalidOperationException("Stop play mode before reloading a model.");
        if (_sequenceExportJob?.IsRunning == true)
            throw new InvalidOperationException("Wait for sequence export to finish before reloading a model.");
        if (_pendingAssetPlacementPreview is not null)
            throw new InvalidOperationException("Add or cancel the current model preview before reloading a project model.");
        if (_preview is null)
            throw new InvalidOperationException("The editor preview is not ready to reload a model.");

        _editorUi?.CompletePendingEdit(_sceneData);
        var candidateScene = SceneGraphCloner.Clone(_sceneData);
        if (!EnumerateProjectSceneAssetReferences(_sceneData)
            .Any(asset => asset.AssetId == reference.AssetId))
        {
            candidateScene.Add(SceneObjectFactory.CreateAssetInstance(candidateScene, reference, Vector3.Zero));
        }

        PreviewResources? candidatePreview = null;
        Exception? cleanupError;
        try
        {
            candidatePreview = PreviewResources.Load(GraphicsDevice,
                ResolveSceneAssets(candidateScene, _project.RootDirectory, _project), candidateScene);
            if (candidatePreview.HasSkinnedCharacters)
                SkinnedEffectCompatibility.Validate(GraphicsDevice.GraphicsProfile,
                    candidatePreview.MaximumJointCount);
            candidatePreview.RebuildCharacterInstances(_sceneData);

            var loadedPreview = candidatePreview;
            cleanupError = _preview.Reload(() => loadedPreview);
            candidatePreview = null;
        }
        catch (Exception exception)
        {
            _reimportStatus = $"Model reload failed; the previous preview remains active: {exception.Message}";
            throw;
        }
        finally
        {
            candidatePreview?.Dispose();
        }

        BuildSequencePreview();
        if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);
        var message = $"Reloaded {Path.GetFileName(reference.SourcePath)} from project files.";
        if (cleanupError is not null) message += $" Previous preview cleanup reported: {cleanupError.Message}";
        _reimportStatus = message;
        return message;
    }

    private string ImportGlbForEditor(string sourcePath)
    {
        if (_project is null)
            throw new InvalidOperationException("Create or open a project before importing a GLB asset.");
        if (_playSession is not null)
            throw new InvalidOperationException("Stop play mode before importing an asset.");
        if (_sequenceExportJob?.IsRunning == true)
            throw new InvalidOperationException("Wait for sequence export to finish before importing an asset.");
        if (_preview is null)
            throw new InvalidOperationException("The editor preview is not ready to import an asset.");

        EngineProjectAssetImport? imported = null;
        PreviewResources? candidatePreview = null;
        try
        {
            imported = EngineProjectWorkspace.ImportGlb(_project, sourcePath);
            var previewScene = new SceneGraph();
            var item = SceneObjectFactory.CreateAssetInstance(previewScene, imported.Reference, Vector3.Zero);
            previewScene.Add(item);
            candidatePreview = PreviewResources.Load(GraphicsDevice,
                ResolveSceneAssets(previewScene, _project.RootDirectory, _project), previewScene);
            if (candidatePreview.HasSkinnedCharacters)
                SkinnedEffectCompatibility.Validate(GraphicsDevice.GraphicsProfile,
                    candidatePreview.MaximumJointCount);
            imported.RegisterForPreview();

            if (GetSceneBounds(includePendingAssetPreview: false) is { } sceneBounds
                && GetSceneObjectBounds(previewScene, item, candidatePreview) is { } modelBounds)
            {
                var gap = Math.Clamp(modelBounds.Size.X * 0.15f, 2f, 20f);
                item.Transform.Position += new Vector3(sceneBounds.Max.X + gap - modelBounds.Min.X, 0f, 0f);
            }

            var pending = new PendingAssetPlacementPreview(imported.Reference, imported,
                candidatePreview, previewScene, item);
            imported = null;
            candidatePreview = null;
            var previousPending = _pendingAssetPlacementPreview;
            _pendingAssetPlacementPreview = pending;
            InvalidateProjectAssetCatalog();
            try { previousPending?.Dispose(); }
            catch (Exception exception) { _faults.Add($"Previous model preview cleanup: {exception.Message}"); }

            if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);
            var cleanupNote = previousPending is null ? string.Empty : " Replaced the previous preview.";
            var message = $"Previewing {Path.GetFileName(sourcePath)} in the scene.{cleanupNote} Choose Add to scene or Cancel preview.";
            _reimportStatus = message;
            return message;
        }
        finally
        {
            candidatePreview?.Dispose();
            imported?.Dispose();
        }
    }

    private string? GetPendingAssetPreviewName() => _pendingAssetPlacementPreview is { } pending
        ? Path.GetFileName(pending.Reference.SourcePath)
        : null;

    private string AcceptPendingAssetPreview()
    {
        var pending = _pendingAssetPlacementPreview
            ?? throw new InvalidOperationException("Browse for a model before adding it to the scene.");
        if (_project is null || _preview is null)
            throw new InvalidOperationException("The project preview is not ready to add this model.");
        if (_playSession is not null)
            throw new InvalidOperationException("Stop play mode before adding a model to the scene.");
        if (_sequenceExportJob?.IsRunning == true)
            throw new InvalidOperationException("Wait for sequence export to finish before adding a model.");

        _editorUi?.CompletePendingEdit(_sceneData);
        var candidateScene = SceneGraphCloner.Clone(_sceneData);
        var item = SceneObjectFactory.CreateAssetInstance(candidateScene, pending.Reference,
            pending.Item.Transform.Position);
        item.Transform.Rotation = pending.Item.Transform.Rotation;
        item.Transform.Scale = pending.Item.Transform.Scale;
        candidateScene.Add(item);

        PreviewResources? candidatePreview = null;
        try
        {
            var loadedPreview = PreviewResources.Load(GraphicsDevice,
                ResolveSceneAssets(candidateScene, _project.RootDirectory, _project), candidateScene);
            candidatePreview = loadedPreview;
            if (candidatePreview.HasSkinnedCharacters)
                SkinnedEffectCompatibility.Validate(GraphicsDevice.GraphicsProfile,
                    candidatePreview.MaximumJointCount);

            pending.Import?.Commit();
            _projectSession.InvalidateAssetCatalog();
            _editorHistory.Execute(_sceneData, new CreateSceneObjectCommand(item));
            var cleanupError = _preview.Reload(() => loadedPreview);
            candidatePreview = null;
            _pendingAssetPlacementPreview = null;
            _editorUi?.SelectObject(item.Id, pending.Reference.AssetId);
            BuildSequencePreview();
            if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);

            var message = $"Added '{item.Name}' to the scene. Save the scene to keep the placement.";
            if (cleanupError is not null) message += $" Previous preview cleanup reported: {cleanupError.Message}";
            try { pending.Dispose(); }
            catch (Exception exception) { message += $" Preview cleanup reported: {exception.Message}"; }
            _reimportStatus = message;
            return message;
        }
        finally
        {
            candidatePreview?.Dispose();
        }
    }

    private void CancelPendingAssetPreview() => CancelPendingAssetPreview(updateStatus: true, frameScene: true);

    private void CancelPendingAssetPreview(bool updateStatus, bool frameScene)
    {
        var pending = _pendingAssetPlacementPreview;
        if (pending is null) return;
        _pendingAssetPlacementPreview = null;
        try { pending.Dispose(); }
        finally
        {
            InvalidateProjectAssetCatalog();
            if (updateStatus) _reimportStatus = "Model preview canceled; the asset was not added to the scene.";
            if (frameScene && GetSceneBounds() is { } bounds) _camera.Frame(bounds);
        }
    }

    private string? OpenWorldCell(string scenePath, string worldRoot)
    {
        if (_playSession is not null) return "Stop play mode before opening another cell.";
        if (_sequenceExportJob?.IsRunning == true) return "Wait for sequence export to finish before opening another cell.";
        if (_sceneSavePath is null)
            return "Save the current scene first with Save As; cell switching preserves saved work.";
        if (_preview is null) return "Character preview is not ready.";

        _editorUi?.CompletePendingEdit(_sceneData);
        if (!SaveScene()) return _reimportStatus;

        PreviewResources? replacement = null;
        AttachmentBoxRenderer? attachmentCandidate = null;
        try
        {
            var fullScenePath = Path.GetFullPath(scenePath);
            var assetRoot = _project?.RootDirectory ?? Path.GetFullPath(worldRoot);
            var candidate = SceneFile.Load(fullScenePath);
            var loadedPreview = PreviewResources.Load(GraphicsDevice,
                ResolveSceneAssets(candidate, assetRoot), candidate);
            replacement = loadedPreview;
            if (replacement.HasSkinnedCharacters)
            {
                SkinnedEffectCompatibility.Validate(GraphicsDevice.GraphicsProfile,
                    replacement.MaximumJointCount);
                if (replacement.AttachmentsByInstanceId.Count > 0 && _attachmentRenderer is null)
                    attachmentCandidate = new AttachmentBoxRenderer(GraphicsDevice);
            }

            CancelPendingAssetPreview();
            var cleanupError = _preview.Reload(() => loadedPreview);
            replacement = null; // ownership transferred to ReloadableAsset
            if (attachmentCandidate is not null)
            {
                _attachmentRenderer = _sceneResources!.Own(attachmentCandidate);
                attachmentCandidate = null;
            }
            _sceneData = candidate;
            _sceneSavePath = fullScenePath;
            _sceneAssetRoot = assetRoot;
            _editorHistory = new SceneCommandHistory();
            _editorUi?.SetHistory(_editorHistory);
            _editorUi?.ResetSceneSelection();
            _farLodByObjectId.Clear();

            BuildSequencePreview();
            if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);
            var opened = $"Opened {Path.GetFileName(fullScenePath)}.";
            if (cleanupError is not null)
                opened += $" Previous preview cleanup reported: {cleanupError.Message}";
            _reimportStatus = opened;
            return opened;
        }
        catch (Exception exception)
        {
            replacement?.Dispose();
            attachmentCandidate?.Dispose();
            _reimportStatus = $"Could not open cell: {exception.Message}";
            return _reimportStatus;
        }
    }

    private string ApplyRecoveredProject(string stagedScenePath, string projectRoot, Action applyProjectFiles)
    {
        if (_playSession is not null)
            throw new InvalidOperationException("Stop play mode before applying an authoring recovery.");
        if (_sequenceExportJob?.IsRunning == true)
            throw new InvalidOperationException("Wait for sequence export to finish before applying recovery.");
        if (_preview is null || _sceneSavePath is null)
            throw new InvalidOperationException("The active scene preview is not ready for recovery.");
        ArgumentNullException.ThrowIfNull(applyProjectFiles);

        var sourceScenePath = Path.GetFullPath(_sceneSavePath);
        var relativeScenePath = Path.GetRelativePath(Path.GetFullPath(projectRoot), sourceScenePath);
        if (Path.IsPathRooted(relativeScenePath) || relativeScenePath == ".."
            || relativeScenePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relativeScenePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("The active scene is outside the recovered project.");

        // Prepare the recovered scene and every GPU resource before modifying project files.
        var candidate = SceneFile.Load(Path.GetFullPath(stagedScenePath));
        var loadedPreview = PreviewResources.Load(GraphicsDevice,
            ResolveSceneAssets(candidate, _sceneAssetRoot), candidate);
        AttachmentBoxRenderer? attachmentCandidate = null;
        var previewTransferred = false;
        try
        {
            if (loadedPreview.HasSkinnedCharacters)
            {
                SkinnedEffectCompatibility.Validate(GraphicsDevice.GraphicsProfile,
                    loadedPreview.MaximumJointCount);
                if (loadedPreview.AttachmentsByInstanceId.Count > 0 && _attachmentRenderer is null)
                    attachmentCandidate = new AttachmentBoxRenderer(GraphicsDevice);
            }

            applyProjectFiles();
            var cleanupError = _preview.Reload(() => loadedPreview);
            previewTransferred = true;
            if (attachmentCandidate is not null)
            {
                _attachmentRenderer = _sceneResources!.Own(attachmentCandidate);
                attachmentCandidate = null;
            }

            DisposePathPhysics();
            _sceneData = candidate;
            _sceneSavePath = sourceScenePath;
            _editorHistory = new SceneCommandHistory();
            _editorUi?.SetHistory(_editorHistory);
            _editorUi?.ResetSceneSelection();
            _farLodByObjectId.Clear();
            BuildSequencePreview();
            if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);

            _reimportStatus = cleanupError is null
                ? $"Applied recovery and reloaded {Path.GetFileName(sourceScenePath)}."
                : $"Applied recovery; previous preview cleanup reported: {cleanupError.Message}";
            return _reimportStatus;
        }
        catch
        {
            if (!previewTransferred) loadedPreview.Dispose();
            attachmentCandidate?.Dispose();
            throw;
        }
    }
    private void OnWorldCellRenamed(string previousScenePath, string renamedScenePath, Guid cellId)
    {
        if (_sceneSavePath is null || !PathsReferToSameFile(_sceneSavePath, previousScenePath)) return;
        _sceneSavePath = Path.GetFullPath(renamedScenePath);
        _reimportStatus = $"Active cell {cellId} renamed; future saves target {Path.GetFileName(_sceneSavePath)}.";
    }

    private static bool PathsReferToSameFile(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void ReimportAsset()
    {
        if (_preview is null || _playSession is not null) return;
        try
        {
            var cleanupError = _preview.Reload(() => PreviewResources.Load(
                GraphicsDevice, ResolveSceneAssets(_sceneData), _sceneData));
            BuildSequencePreview();
            if (cleanupError is null)
            {
                _reimportStatus = "Scene GLB reimport succeeded. | S: save scene";
                Console.WriteLine("Reimported all GLB assets referenced by the scene.");
            }
            else
            {
                _reimportStatus = $"Reimport succeeded; cleanup failed: {cleanupError.Message} | S: save scene";
                Console.WriteLine($"Reimport succeeded, but previous asset cleanup failed: {cleanupError.Message}");
            }

            if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);
        }
        catch (Exception exception)
        {
            _reimportStatus = $"Reimport failed; previous asset still active: {exception.Message}";
            Console.WriteLine($"GLB reimport failed; the previous asset remains active: {exception.Message}");
        }
    }

    private readonly record struct PlaybackOptions(
        string? AnimationName,
        float? AnimationTime,
        float AnimationSpeed,
        bool PauseAnimation,
        bool LoopAnimation,
        bool Pair,
        string? SecondAnimationName,
        float? SecondAnimationTime,
        float SecondAnimationSpeed,
        bool PauseSecond,
        bool SecondLoopAnimation,
        string? CrossfadeAnimationName,
        float BlendAmount,
        bool AttachHand,
        bool HasSecondOptions);

}
