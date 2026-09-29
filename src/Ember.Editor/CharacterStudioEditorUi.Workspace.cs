using Ember.Scene;
using Ember.Render;
using Ember.Rpg;
using Ember.Authoring;
using Ember.Project;
using Ember.World;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace Ember.Editor;

internal sealed partial class CharacterStudioEditorUi
{
    private void DrawMainToolbar(SceneGraph scene)
    {
        ImGui.SetNextWindowPos(NumericsVector2.Zero);
        ImGui.SetNextWindowSize(new NumericsVector2(_logicalWidth, 48f));
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;
        if (!ImGui.Begin("Main toolbar", flags))
        {
            ImGui.End();
            return;
        }

        if (ImGui.Button("Home"))
        {
            CommitActiveTransformEdit(scene);
            if (_getPendingAssetPreviewName() is not null)
            {
                _cancelPendingAssetPreview();
                _projectWorkspaceStatus = "Model preview canceled; the asset was not added to the scene.";
            }
            _showHome = true;
        }
        ImGui.SameLine();
        var scenePath = _getCurrentScenePath();
        var canSave = !_isPlaying();
        if (!canSave) ImGui.BeginDisabled();
        if (ImGui.Button(scenePath is null ? "Save as…" : "Save")) SaveSceneFromUi(scene);
        if (!canSave) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!_history.CanUndo) ImGui.BeginDisabled();
        if (ImGui.Button("Undo")) RunHistoryAction(scene, undo: true);
        if (!_history.CanUndo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!_history.CanRedo) ImGui.BeginDisabled();
        if (ImGui.Button("Redo")) RunHistoryAction(scene, undo: false);
        if (!_history.CanRedo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(_isPlaying() ? "Stop" : "Play"))
        {
            if (_isPlaying()) _stopPlay();
            else _startPlay();
        }
        ImGui.SameLine();
        var guideProjectPath = _getCurrentProjectPath();
        var currentLesson = _firstCreationLesson;
        var lessonMatchesProject = currentLesson is not null
            && IsSamePath(currentLesson.ProjectFilePath, guideProjectPath);
        var guideLabel = lessonMatchesProject && currentLesson is not null
            ? currentLesson.Step == FirstCreationLessonStep.Complete
                ? "Replay guide"
                : _showFirstCreationLesson ? "Hide guide" : "Show guide"
            : "First creation";
        if (guideProjectPath is null) ImGui.BeginDisabled();
        if (ImGui.Button(guideLabel) && guideProjectPath is not null)
        {
            if (lessonMatchesProject && currentLesson is not null
                && currentLesson.Step != FirstCreationLessonStep.Complete)
            {
                _showFirstCreationLesson = !_showFirstCreationLesson;
                if (_showFirstCreationLesson) _showAddLibrary = true;
            }
            else
            {
                BeginFirstCreationLesson(scene, guideProjectPath);
            }
        }
        if (guideProjectPath is null) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Animate"))
        {
            _showToolMenu = false;
            _showSequenceTools = true;
            _showWorldTools = false;
            _showRpgTools = false;
            _showSceneTemplateTools = false;
            if (_getSequenceInfo() is null)
                _projectWorkspaceStatus = "Add an animated character to this scene to create a film sequence.";
        }
        ImGui.SameLine();
        if (ImGui.Button("Finish"))
        {
            _showToolMenu = false;
            _showSequenceTools = true;
            _showWorldTools = false;
            _showRpgTools = false;
            _showSceneTemplateTools = false;
            if (_getSequenceInfo() is null)
                _projectWorkspaceStatus = "A film sequence is needed before frames can be exported.";
        }
        ImGui.SameLine();
        if (ImGui.Button(_showAddLibrary ? "Hide Add" : "Add")) _showAddLibrary = !_showAddLibrary;
        ImGui.SameLine();
        if (ImGui.Button(_showToolMenu ? "Close tools" : "More tools"))
        {
            _showToolMenu = !_showToolMenu;
            if (_showToolMenu)
                _showSequenceTools = _showWorldTools = _showRpgTools = _showSceneTemplateTools = false;
        }

        var currentProject = _getCurrentProjectPath();
        if (currentProject is not null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Path.GetFileName(Path.GetDirectoryName(currentProject)));
        }
        ImGui.SameLine();
        if (_history.IsDirty)
            ImGui.TextColored(new NumericsVector4(1f, 0.76f, 0.30f, 1f), "Unsaved changes");
        else
            ImGui.TextDisabled("Saved");
        ImGui.End();
    }

    private bool DrawMoreToolsMenu()
    {
        if (!_showToolMenu) return false;
        ImGui.SetNextWindowPos(new NumericsVector2(232f, 48f));
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Min(230f, _logicalWidth), 236f));
        if (!ImGui.Begin("More tools", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return true;
        }

        var showSequenceTools = _showSequenceTools;
        if (ImGui.Checkbox("Animate and Finish", ref showSequenceTools))
        {
            _showSequenceTools = showSequenceTools;
            if (showSequenceTools) _showWorldTools = _showRpgTools = _showSceneTemplateTools = false;
            _showToolMenu = false;
        }
        var showWorldTools = _showWorldTools;
        if (ImGui.Checkbox("World Cells", ref showWorldTools))
        {
            _showWorldTools = showWorldTools;
            if (showWorldTools) _showSequenceTools = _showRpgTools = _showSceneTemplateTools = false;
            _showToolMenu = false;
        }
        var showRpgTools = _showRpgTools;
        if (ImGui.Checkbox("RPG authoring", ref showRpgTools))
        {
            _showRpgTools = showRpgTools;
            if (showRpgTools) _showSequenceTools = _showWorldTools = _showSceneTemplateTools = false;
            _showToolMenu = false;
        }
        var showSceneTemplateTools = _showSceneTemplateTools;
        if (ImGui.Checkbox("Scene templates", ref showSceneTemplateTools))
        {
            _showSceneTemplateTools = showSceneTemplateTools;
            if (showSceneTemplateTools) _showSequenceTools = _showWorldTools = _showRpgTools = false;
            _showToolMenu = false;
        }
        if (ImGui.Checkbox("Performance details", ref _showDiagnostics))
            _setDiagnosticsVisible(_showDiagnostics);
        ImGui.Separator();
        if (ImGui.Button("Reset workspace layout"))
        {
            _showAddLibrary = true;
            _showToolMenu = false;
            _showSequenceTools = false;
            _showWorldTools = false;
            _showRpgTools = false;
            _showSceneTemplateTools = false;
            _showDiagnostics = false;
            _setDiagnosticsVisible(false);
        }
        ImGui.End();
        return true;
    }

    private void DrawAddLibrary(SceneGraph scene)
    {
        if (!_showAddLibrary || _logicalWidth < 1_050) return;
        const float libraryWidth = 220f;
        ImGui.SetNextWindowPos(new NumericsVector2(0f, 48f));
        ImGui.SetNextWindowSize(new NumericsVector2(libraryWidth, Math.Max(160f, _logicalHeight - 48f)));
        if (!ImGui.Begin("Add to scene", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        if (ImGui.Button("Add empty object", new NumericsVector2(-1f, 34f))) CreateEmpty(scene);
        ImGui.Separator();
        ImGui.Text("Add a model");
        var canImport = _getCurrentProjectPath() is not null;
        if (!canImport) ImGui.BeginDisabled();
        var pendingPreviewName = _getPendingAssetPreviewName();
        if (pendingPreviewName is not null)
        {
            ImGui.TextWrapped($"Previewing {pendingPreviewName}. It is not in the scene yet.");
            if (ImGui.Button("Add to scene", new NumericsVector2(-1f, 34f)))
            {
                try { _projectWorkspaceStatus = _acceptPendingAssetPreview(); }
                catch (Exception exception) { _projectWorkspaceStatus = $"Could not add the model: {exception.Message}"; }
            }
            if (ImGui.Button("Cancel preview", new NumericsVector2(-1f, 30f)))
            {
                try
                {
                    _cancelPendingAssetPreview();
                    _projectWorkspaceStatus = "Model preview canceled; the asset was not added to the scene.";
                }
                catch (Exception exception) { _projectWorkspaceStatus = $"Could not cancel the preview: {exception.Message}"; }
            }
        }
        if (ImGui.Button("Browse for a model…", new NumericsVector2(-1f, 34f))) BrowseForModel();
        if (!canImport) ImGui.EndDisabled();
        if (!canImport) ImGui.TextWrapped("Create or open a project before adding a model.");

        ImGui.Separator();
        ImGui.Text("Models in this project");
        if (ImGui.SmallButton("Refresh models")) _refreshProjectAssetReferences();

        var availableAssets = GetAvailableAssets(scene);
        if (_selectedAssetId is null || availableAssets.All(asset => asset.AssetId != _selectedAssetId))
            _selectedAssetId = availableAssets.FirstOrDefault()?.AssetId;
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##assetSearch", "Search models", ref _assetSearchQuery, 128);
        var searchQuery = _assetSearchQuery.Trim();
        var matchingAssets = string.IsNullOrEmpty(searchQuery)
            ? availableAssets
            : availableAssets.Where(asset => asset.SourcePath.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(asset.SourcePath).Contains(searchQuery, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matchingAssets.All(asset => asset.AssetId != _selectedAssetId))
            _selectedAssetId = matchingAssets.FirstOrDefault()?.AssetId;
        var lessonVisibleForProject = _showFirstCreationLesson
            && _firstCreationLesson is { } activeLesson
            && IsSamePath(activeLesson.ProjectFilePath, _getCurrentProjectPath());
        var modelListHeight = Math.Max(64f, _logicalHeight * (lessonVisibleForProject ? 0.15f : 0.2f));
        ImGui.BeginChild("Scene assets", new NumericsVector2(0f, modelListHeight), ImGuiChildFlags.Borders);
        if (availableAssets.Length == 0)
        {
            if (pendingPreviewName is not null)
                ImGui.TextWrapped($"Previewing {pendingPreviewName}. Add it above to place it in the scene.");
            else
                ImGui.TextWrapped("No models are in this project yet. Browse above to import one.");
        }
        else if (matchingAssets.Length == 0)
        {
            ImGui.TextWrapped($"No models match ‘{searchQuery}’. Try another search.");
        }
        var sceneAssetPaths = GetSceneAssets(scene)
            .Select(asset => asset.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in matchingAssets)
        {
            var usage = sceneAssetPaths.Contains(asset.SourcePath) ? " · In scene" : " · In project";
            var label = $"{Path.GetFileName(asset.SourcePath)}{usage}##asset-{asset.AssetId:N}";
            if (ImGui.Selectable(label, _selectedAssetId == asset.AssetId))
                _selectedAssetId = asset.AssetId;
        }
        ImGui.EndChild();
        var selectedAsset = matchingAssets.FirstOrDefault(asset => asset.AssetId == _selectedAssetId);
        var canPreviewSelectedAsset = selectedAsset is not null && _getCurrentProjectPath() is not null;
        if (!canPreviewSelectedAsset) ImGui.BeginDisabled();
        if (ImGui.Button("Preview selected model", new NumericsVector2(-1f, 34f))
            && canPreviewSelectedAsset && selectedAsset is not null)
        {
            try { _projectWorkspaceStatus = _previewProjectAsset(selectedAsset); }
            catch (Exception exception) { _projectWorkspaceStatus = $"Could not preview the model: {exception.Message}"; }
        }
        if (!canPreviewSelectedAsset) ImGui.EndDisabled();
        if (selectedAsset is not null && !canPreviewSelectedAsset)
            ImGui.TextWrapped("Create or open a project to preview and place a model.");
        var canReloadSelectedAsset = selectedAsset is not null && _getCurrentProjectPath() is not null
            && pendingPreviewName is null;
        if (!canReloadSelectedAsset) ImGui.BeginDisabled();
        if (ImGui.Button("Reload selected model", new NumericsVector2(-1f, 30f))
            && canReloadSelectedAsset && selectedAsset is not null)
        {
            try { _projectWorkspaceStatus = _reloadProjectAsset(selectedAsset); }
            catch (Exception exception) { _projectWorkspaceStatus = $"Could not reload the model: {exception.Message}"; }
        }
        if (!canReloadSelectedAsset) ImGui.EndDisabled();
        if (pendingPreviewName is not null)
            ImGui.TextWrapped("Add or cancel the current preview before reloading a project model.");
        else if (canReloadSelectedAsset)
            ImGui.TextWrapped("Reloads from project files. A failed reload keeps the current scene preview.");
        if (!string.IsNullOrWhiteSpace(_projectWorkspaceStatus)
            && !_projectWorkspaceStatus.StartsWith("Could not", StringComparison.OrdinalIgnoreCase))
            ImGui.TextWrapped(_projectWorkspaceStatus);
        DrawFirstCreationLesson(scene);
        ImGui.End();
    }

    private void DrawFirstCreationLesson(SceneGraph scene)
    {
        if (!_showFirstCreationLesson || _firstCreationLesson is not { } lesson
            || !IsSamePath(lesson.ProjectFilePath, _getCurrentProjectPath())) return;

        ImGui.Separator();
        ImGui.TextDisabled($"FIRST CREATION · {lesson.StepNumber}/{FirstCreationLesson.TotalSteps}");
        ImGui.Text(lesson.Title);
        ImGui.TextWrapped(lesson.Explanation);
        if (!string.IsNullOrWhiteSpace(lesson.Feedback))
            ImGui.TextWrapped(lesson.Feedback);

        switch (lesson.Step)
        {
            case FirstCreationLessonStep.PredictPlay:
                if (ImGui.Button("The saved scene changes", new NumericsVector2(-1f, 28f)))
                    lesson.AnswerPlayPrediction(savedSceneChanges: true);
                if (ImGui.Button("Play uses a temporary copy", new NumericsVector2(-1f, 28f)))
                    lesson.AnswerPlayPrediction(savedSceneChanges: false);
                break;
            case FirstCreationLessonStep.Reflect:
                if (ImGui.Button("My authored scene stayed the same", new NumericsVector2(-1f, 28f)))
                    lesson.AnswerPlayReflection(authoredSceneStayedUnchanged: true);
                if (ImGui.Button("Play edits were saved", new NumericsVector2(-1f, 28f)))
                    lesson.AnswerPlayReflection(authoredSceneStayedUnchanged: false);
                break;
            case FirstCreationLessonStep.Reopen:
                if (ImGui.Button("Reopen this project", new NumericsVector2(-1f, 30f)))
                    ReopenFirstCreationProject(lesson);
                break;
            case FirstCreationLessonStep.Complete:
                if (ImGui.Button("Replay guide", new NumericsVector2(-1f, 28f)))
                    BeginFirstCreationLesson(scene, lesson.ProjectFilePath);
                break;
        }

        if (lesson.Step != FirstCreationLessonStep.Complete)
        {
            if (ImGui.SmallButton(_showLessonWhy ? "Hide Why?" : "Why?"))
                _showLessonWhy = !_showLessonWhy;
            ImGui.SameLine();
            var hintButton = _lessonHintLevel switch
            {
                0 => "Show a hint",
                1 => "More help",
                _ => "Hide hints"
            };
            if (ImGui.SmallButton(hintButton))
                _lessonHintLevel = _lessonHintLevel switch { 0 => 1, 1 => 2, _ => 0 };
            ImGui.SameLine();
            if (ImGui.SmallButton("Skip guide")) _showFirstCreationLesson = false;
            if (_showLessonWhy) ImGui.TextWrapped(lesson.Why);
            if (_lessonHintLevel > 0) ImGui.TextWrapped(lesson.Hint);
            if (_lessonHintLevel > 1) ImGui.TextWrapped(lesson.MoreSpecificHint);
        }
        else if (ImGui.SmallButton("Hide guide"))
        {
            _showFirstCreationLesson = false;
        }
    }

    private void ReopenFirstCreationProject(FirstCreationLesson lesson)
    {
        try
        {
            _projectWorkspaceStatus = _openProject(lesson.ProjectFilePath);
            _lessonProjectOpenCheck = lesson.ProjectFilePath;
            _showHome = false;
        }
        catch (Exception exception)
        {
            _projectWorkspaceStatus = $"Could not reopen the project: {exception.Message}";
        }
    }

    private GltfAssetReference[] GetAvailableAssets(SceneGraph scene)
    {
        var byPath = new Dictionary<string, GltfAssetReference>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var asset in _getProjectAssetReferences())
                byPath[asset.SourcePath] = asset;
        }
        catch (Exception exception)
        {
            _projectWorkspaceStatus = $"Could not list project models: {exception.Message}";
        }

        foreach (var asset in GetSceneAssets(scene))
            byPath[asset.SourcePath] = asset;
        return byPath.Values.OrderBy(asset => asset.SourcePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static GltfAssetReference[] GetSceneAssets(SceneGraph scene) => scene.Objects
        .SelectMany(GetAssetReferences)
        .GroupBy(asset => asset.SourcePath, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.First())
        .ToArray();

    private static IEnumerable<GltfAssetReference> GetAssetReferences(SceneObject item)
    {
        if (item.GltfAsset is { } asset) yield return asset;
        if (item.StaticMeshLod is { } lod)
        {
            yield return lod.NearAsset;
            yield return lod.FarAsset;
        }
    }

    private void BrowseForModel()
    {
        var projectPath = _getCurrentProjectPath();
        if (projectPath is null)
        {
            _projectWorkspaceStatus = "Create or open a project before adding a model.";
            return;
        }

        var modelPath = CharacterStudioFilePickers.PickGltfFile(
            Path.GetDirectoryName(projectPath), _windowHandle);
        if (modelPath is null) return;

        try
        {
            _projectWorkspaceStatus = _importGlb(modelPath);
        }
        catch (Exception exception)
        {
            _projectWorkspaceStatus = $"Could not add the model: {exception.Message}";
        }
    }

}
