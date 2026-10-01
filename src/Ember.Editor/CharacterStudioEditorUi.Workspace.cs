using Ember.Scene;
using Ember.Render;
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
    private readonly WorkspacePanel _workspacePanel;

    private void DrawMainToolbar(SceneGraph scene) => _workspacePanel.DrawMainToolbar(scene);
    private bool DrawMoreToolsMenu() => _workspacePanel.DrawMoreToolsMenu();
    private void DrawAddLibrary(SceneGraph scene) => _workspacePanel.DrawAddLibrary(scene);
    private void BrowseForModel() => _workspacePanel.BrowseForModel();

    private sealed class WorkspacePanel
    {
        private readonly CharacterStudioEditorUi _owner;

        public WorkspacePanel(CharacterStudioEditorUi owner) =>
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    public void DrawMainToolbar(SceneGraph scene)
    {
        ImGui.SetNextWindowPos(NumericsVector2.Zero);
        ImGui.SetNextWindowSize(new NumericsVector2(_owner._logicalWidth, 48f));
        var flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;
        if (!ImGui.Begin("Main toolbar", flags))
        {
            ImGui.End();
            return;
        }

        if (ImGui.Button("Home"))
        {
            _owner.CommitActiveTransformEdit(scene);
            if (_owner._getPendingAssetPreviewName() is not null)
            {
                _owner._cancelPendingAssetPreview();
                _owner._projectWorkspaceStatus = "Model preview canceled; the asset was not added to the scene.";
            }
            _owner._showHome = true;
        }
        ImGui.SameLine();
        var scenePath = _owner._getCurrentScenePath();
        var canSave = !_owner._isPlaying();
        if (!canSave) ImGui.BeginDisabled();
        if (ImGui.Button(scenePath is null ? "Save as…" : "Save")) _owner.SaveSceneFromUi(scene);
        if (!canSave) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!_owner._history.CanUndo) ImGui.BeginDisabled();
        if (ImGui.Button("Undo")) _owner.RunHistoryAction(scene, undo: true);
        if (!_owner._history.CanUndo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!_owner._history.CanRedo) ImGui.BeginDisabled();
        if (ImGui.Button("Redo")) _owner.RunHistoryAction(scene, undo: false);
        if (!_owner._history.CanRedo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button(_owner._isPlaying() ? "Stop" : "Play"))
        {
            if (_owner._isPlaying()) _owner._stopPlay();
            else _owner._startPlay();
        }
        if (_owner._isPlaying())
        {
            ImGui.SameLine();
            if (ImGui.Button(_owner._isPlayPaused() ? "Resume" : "Pause"))
                _owner._togglePlayPause();
        }
        ImGui.SameLine();
        var guideProjectPath = _owner._getCurrentProjectPath();
        var currentLesson = _owner._firstCreationLesson;
        var lessonMatchesProject = currentLesson is not null
            && CharacterStudioEditorUi.IsSamePath(currentLesson.ProjectFilePath, guideProjectPath);
        var guideLabel = lessonMatchesProject && currentLesson is not null
            ? currentLesson.Step == FirstCreationLessonStep.Complete
                ? "Replay guide"
                : _owner._showFirstCreationLesson ? "Hide guide" : "Show guide"
            : "First creation";
        if (guideProjectPath is null) ImGui.BeginDisabled();
        if (ImGui.Button(guideLabel) && guideProjectPath is not null)
        {
            if (lessonMatchesProject && currentLesson is not null
                && currentLesson.Step != FirstCreationLessonStep.Complete)
            {
                _owner._showFirstCreationLesson = !_owner._showFirstCreationLesson;
                if (_owner._showFirstCreationLesson) _owner._showAddLibrary = true;
            }
            else
            {
                _owner.BeginFirstCreationLesson(scene, guideProjectPath);
            }
        }
        if (guideProjectPath is null) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Animate"))
        {
            _owner._showToolMenu = false;
            _owner._showSequenceTools = true;
            _owner._showWorldTools = false;
            _owner._showWorldAuthoringTools = false;
            _owner._showSceneTemplateTools = false;
            if (_owner._getSequenceInfo() is null)
                _owner._projectWorkspaceStatus = "Add an animated character to this scene to create a film sequence.";
        }
        ImGui.SameLine();
        if (ImGui.Button("Finish"))
        {
            _owner._showToolMenu = false;
            _owner._showSequenceTools = true;
            _owner._showWorldTools = false;
            _owner._showWorldAuthoringTools = false;
            _owner._showSceneTemplateTools = false;
            if (_owner._getSequenceInfo() is null)
                _owner._projectWorkspaceStatus = "A film sequence is needed before frames can be exported.";
        }
        ImGui.SameLine();
        if (ImGui.Button(_owner._showAddLibrary ? "Hide Add" : "Add")) _owner._showAddLibrary = !_owner._showAddLibrary;
        ImGui.SameLine();
        if (ImGui.Button(_owner._showToolMenu ? "Close tools" : "More tools"))
        {
            _owner._showToolMenu = !_owner._showToolMenu;
            if (_owner._showToolMenu)
                _owner._showSequenceTools = _owner._showWorldTools = _owner._showWorldAuthoringTools = _owner._showSceneTemplateTools = false;
        }

        var currentProject = _owner._getCurrentProjectPath();
        if (currentProject is not null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(Path.GetFileName(Path.GetDirectoryName(currentProject)));
        }
        ImGui.SameLine();
        if (_owner._history.IsDirty)
            ImGui.TextColored(new NumericsVector4(1f, 0.76f, 0.30f, 1f), "Unsaved changes");
        else
            ImGui.TextDisabled("Saved");
        ImGui.End();
    }

    public bool DrawMoreToolsMenu()
    {
        if (!_owner._showToolMenu) return false;
        ImGui.SetNextWindowPos(new NumericsVector2(232f, 48f));
        var optionalToolRows = Math.Max(1, _owner._toolExtensions.Count) + _owner._toolExtensionLoadErrors.Count;
        var menuHeight = 246f + optionalToolRows * 22f;
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Min(260f, _owner._logicalWidth),
            Math.Min(menuHeight, Math.Max(180f, _owner._logicalHeight - 56f))));
        if (!ImGui.Begin("More tools", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return true;
        }

        var showSequenceTools = _owner._showSequenceTools;
        if (ImGui.Checkbox("Animate and Finish", ref showSequenceTools))
        {
            _owner._showSequenceTools = showSequenceTools;
            if (showSequenceTools) _owner._showWorldTools = _owner._showWorldAuthoringTools = _owner._showSceneTemplateTools = false;
            if (showSequenceTools) _owner._activeToolExtensionId = null;
            _owner._showToolMenu = false;
        }
        var showWorldTools = _owner._showWorldTools;
        if (ImGui.Checkbox("World Cells", ref showWorldTools))
        {
            _owner._showWorldTools = showWorldTools;
            if (showWorldTools) _owner._showSequenceTools = _owner._showWorldAuthoringTools = _owner._showSceneTemplateTools = false;
            if (showWorldTools) _owner._activeToolExtensionId = null;
            _owner._showToolMenu = false;
        }
        var showWorldAuthoringTools = _owner._showWorldAuthoringTools;
        if (ImGui.Checkbox("World authoring", ref showWorldAuthoringTools))
        {
            _owner._showWorldAuthoringTools = showWorldAuthoringTools;
            if (showWorldAuthoringTools) _owner._showSequenceTools = _owner._showWorldTools = _owner._showSceneTemplateTools = false;
            if (showWorldAuthoringTools) _owner._activeToolExtensionId = null;
            _owner._showToolMenu = false;
        }
        var showSceneTemplateTools = _owner._showSceneTemplateTools;
        if (ImGui.Checkbox("Scene templates", ref showSceneTemplateTools))
        {
            _owner._showSceneTemplateTools = showSceneTemplateTools;
            if (showSceneTemplateTools) _owner._showSequenceTools = _owner._showWorldTools = _owner._showWorldAuthoringTools = false;
            if (showSceneTemplateTools) _owner._activeToolExtensionId = null;
            _owner._showToolMenu = false;
        }
        if (_owner._toolExtensions.Count > 0)
        {
            ImGui.Separator();
            ImGui.TextDisabled("Optional tools");
            foreach (var extension in _owner._toolExtensions)
            {
                var selected = string.Equals(_owner._activeToolExtensionId, extension.Id,
                    StringComparison.OrdinalIgnoreCase);
                if (!ImGui.Checkbox($"{extension.DisplayName}##tool-{extension.Id}", ref selected)) continue;
                _owner._activeToolExtensionId = selected ? extension.Id : null;
                if (!selected) continue;
                _owner._showSequenceTools = _owner._showWorldTools = _owner._showWorldAuthoringTools = false;
                _owner._showSceneTemplateTools = false;
                _owner._showToolMenu = false;
            }
        }
        else if (_owner._toolExtensionLoadErrors.Count == 0)
        {
            ImGui.TextDisabled("No optional tools installed.");
        }
        foreach (var error in _owner._toolExtensionLoadErrors)
            ImGui.TextWrapped(error);
        if (ImGui.Checkbox("Performance details", ref _owner._showDiagnostics))
            _owner._setDiagnosticsVisible(_owner._showDiagnostics);
        ImGui.Separator();
        if (ImGui.Button("Reset workspace layout"))
        {
            _owner._showAddLibrary = true;
            _owner._showToolMenu = false;
            _owner._showSequenceTools = false;
            _owner._showWorldTools = false;
            _owner._showWorldAuthoringTools = false;
            _owner._showSceneTemplateTools = false;
            _owner._activeToolExtensionId = null;
            _owner._showDiagnostics = false;
            _owner._setDiagnosticsVisible(false);
        }
        ImGui.End();
        return true;
    }

    public void DrawAddLibrary(SceneGraph scene)
    {
        if (!_owner._showAddLibrary || _owner._logicalWidth < 1_050) return;
        const float libraryWidth = 220f;
        ImGui.SetNextWindowPos(new NumericsVector2(0f, 48f));
        ImGui.SetNextWindowSize(new NumericsVector2(libraryWidth, Math.Max(160f, _owner._logicalHeight - 48f)));
        if (!ImGui.Begin("Add to scene", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        if (ImGui.Button("Add empty object", new NumericsVector2(-1f, 34f))) _owner.CreateEmpty(scene);
        ImGui.Separator();
        ImGui.Text("Add a model");
        var canImport = _owner._getCurrentProjectPath() is not null;
        if (!canImport) ImGui.BeginDisabled();
        var pendingPreviewName = _owner._getPendingAssetPreviewName();
        if (pendingPreviewName is not null)
        {
            ImGui.TextWrapped($"Previewing {pendingPreviewName}. It is not in the scene yet.");
            if (ImGui.Button("Add to scene", new NumericsVector2(-1f, 34f)))
            {
                try { _owner._projectWorkspaceStatus = _owner._acceptPendingAssetPreview(); }
                catch (Exception exception) { _owner._projectWorkspaceStatus = $"Could not add the model: {exception.Message}"; }
            }
            if (ImGui.Button("Cancel preview", new NumericsVector2(-1f, 30f)))
            {
                try
                {
                    _owner._cancelPendingAssetPreview();
                    _owner._projectWorkspaceStatus = "Model preview canceled; the asset was not added to the scene.";
                }
                catch (Exception exception) { _owner._projectWorkspaceStatus = $"Could not cancel the preview: {exception.Message}"; }
            }
        }
        if (ImGui.Button("Browse for a model…", new NumericsVector2(-1f, 34f))) BrowseForModel();
        if (!canImport) ImGui.EndDisabled();
        if (!canImport) ImGui.TextWrapped("Create or open a project before adding a model.");

        ImGui.Separator();
        ImGui.Text("Models in this project");
        if (ImGui.SmallButton("Refresh models")) _owner._refreshProjectAssetReferences();

        var availableAssets = GetAvailableAssets(scene);
        if (_owner._selectedAssetId is null || availableAssets.All(asset => asset.AssetId != _owner._selectedAssetId))
            _owner._selectedAssetId = availableAssets.FirstOrDefault()?.AssetId;
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##assetSearch", "Search models", ref _owner._assetSearchQuery, 128);
        var searchQuery = _owner._assetSearchQuery.Trim();
        var matchingAssets = string.IsNullOrEmpty(searchQuery)
            ? availableAssets
            : availableAssets.Where(asset => asset.SourcePath.Contains(searchQuery, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(asset.SourcePath).Contains(searchQuery, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matchingAssets.All(asset => asset.AssetId != _owner._selectedAssetId))
            _owner._selectedAssetId = matchingAssets.FirstOrDefault()?.AssetId;
        var lessonVisibleForProject = _owner._showFirstCreationLesson
            && _owner._firstCreationLesson is { } activeLesson
            && CharacterStudioEditorUi.IsSamePath(activeLesson.ProjectFilePath, _owner._getCurrentProjectPath());
        var modelListHeight = Math.Max(64f, _owner._logicalHeight * (lessonVisibleForProject ? 0.15f : 0.2f));
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
            if (ImGui.Selectable(label, _owner._selectedAssetId == asset.AssetId))
                _owner._selectedAssetId = asset.AssetId;
        }
        ImGui.EndChild();
        var selectedAsset = matchingAssets.FirstOrDefault(asset => asset.AssetId == _owner._selectedAssetId);
        var canPreviewSelectedAsset = selectedAsset is not null && _owner._getCurrentProjectPath() is not null;
        if (!canPreviewSelectedAsset) ImGui.BeginDisabled();
        if (ImGui.Button("Preview selected model", new NumericsVector2(-1f, 34f))
            && canPreviewSelectedAsset && selectedAsset is not null)
        {
            try { _owner._projectWorkspaceStatus = _owner._previewProjectAsset(selectedAsset); }
            catch (Exception exception) { _owner._projectWorkspaceStatus = $"Could not preview the model: {exception.Message}"; }
        }
        if (!canPreviewSelectedAsset) ImGui.EndDisabled();
        if (selectedAsset is not null && !canPreviewSelectedAsset)
            ImGui.TextWrapped("Create or open a project to preview and place a model.");
        var canReloadSelectedAsset = selectedAsset is not null && _owner._getCurrentProjectPath() is not null
            && pendingPreviewName is null;
        if (!canReloadSelectedAsset) ImGui.BeginDisabled();
        if (ImGui.Button("Reload selected model", new NumericsVector2(-1f, 30f))
            && canReloadSelectedAsset && selectedAsset is not null)
        {
            try { _owner._projectWorkspaceStatus = _owner._reloadProjectAsset(selectedAsset); }
            catch (Exception exception) { _owner._projectWorkspaceStatus = $"Could not reload the model: {exception.Message}"; }
        }
        if (!canReloadSelectedAsset) ImGui.EndDisabled();
        if (pendingPreviewName is not null)
            ImGui.TextWrapped("Add or cancel the current preview before reloading a project model.");
        else if (canReloadSelectedAsset)
            ImGui.TextWrapped("Reloads from project files. A failed reload keeps the current scene preview.");
        if (!string.IsNullOrWhiteSpace(_owner._projectWorkspaceStatus)
            && !_owner._projectWorkspaceStatus.StartsWith("Could not", StringComparison.OrdinalIgnoreCase))
            ImGui.TextWrapped(_owner._projectWorkspaceStatus);
        DrawFirstCreationLesson(scene);
        ImGui.End();
    }

    private void DrawFirstCreationLesson(SceneGraph scene)
    {
        if (!_owner._showFirstCreationLesson || _owner._firstCreationLesson is not { } lesson
            || !CharacterStudioEditorUi.IsSamePath(lesson.ProjectFilePath, _owner._getCurrentProjectPath())) return;

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
                    _owner.BeginFirstCreationLesson(scene, lesson.ProjectFilePath);
                break;
        }

        if (lesson.Step != FirstCreationLessonStep.Complete)
        {
            if (ImGui.SmallButton(_owner._showLessonWhy ? "Hide Why?" : "Why?"))
                _owner._showLessonWhy = !_owner._showLessonWhy;
            ImGui.SameLine();
            var hintButton = _owner._lessonHintLevel switch
            {
                0 => "Show a hint",
                1 => "More help",
                _ => "Hide hints"
            };
            if (ImGui.SmallButton(hintButton))
                _owner._lessonHintLevel = _owner._lessonHintLevel switch { 0 => 1, 1 => 2, _ => 0 };
            ImGui.SameLine();
            if (ImGui.SmallButton("Skip guide")) _owner._showFirstCreationLesson = false;
            if (_owner._showLessonWhy) ImGui.TextWrapped(lesson.Why);
            if (_owner._lessonHintLevel > 0) ImGui.TextWrapped(lesson.Hint);
            if (_owner._lessonHintLevel > 1) ImGui.TextWrapped(lesson.MoreSpecificHint);
        }
        else if (ImGui.SmallButton("Hide guide"))
        {
            _owner._showFirstCreationLesson = false;
        }
    }

    private void ReopenFirstCreationProject(FirstCreationLesson lesson)
    {
        try
        {
            _owner._projectWorkspaceStatus = _owner._openProject(lesson.ProjectFilePath);
            _owner._lessonProjectOpenCheck = lesson.ProjectFilePath;
            _owner._showHome = false;
        }
        catch (Exception exception)
        {
            _owner._projectWorkspaceStatus = $"Could not reopen the project: {exception.Message}";
        }
    }

    private GltfAssetReference[] GetAvailableAssets(SceneGraph scene)
    {
        var byPath = new Dictionary<string, GltfAssetReference>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var asset in _owner._getProjectAssetReferences())
                byPath[asset.SourcePath] = asset;
        }
        catch (Exception exception)
        {
            _owner._projectWorkspaceStatus = $"Could not list project models: {exception.Message}";
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

    public void BrowseForModel()
    {
        var projectPath = _owner._getCurrentProjectPath();
        if (projectPath is null)
        {
            _owner._projectWorkspaceStatus = "Create or open a project before adding a model.";
            return;
        }

        var modelPath = CharacterStudioFilePickers.PickGltfFile(
            Path.GetDirectoryName(projectPath), _owner._windowHandle);
        if (modelPath is null) return;

        try
        {
            _owner._projectWorkspaceStatus = _owner._importGlb(modelPath);
        }
        catch (Exception exception)
        {
            _owner._projectWorkspaceStatus = $"Could not add the model: {exception.Message}";
        }
    }


    }
}
