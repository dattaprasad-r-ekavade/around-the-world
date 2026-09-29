using System;
using System.IO;
using Ember.Scene;
using ImGuiNET;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector4 = System.Numerics.Vector4;

namespace Ember.Editor;

internal sealed partial class CharacterStudioEditorUi
{
    private readonly Action _confirmExit;
    private readonly UnsavedChangesController _unsavedChangesController;

    public void RequestCloseConfirmation() => _unsavedChangesController.RequestCloseConfirmation();
    private void RequestSaveBeforeContinue(string actionDescription, Action continueAfterSave) => _unsavedChangesController.RequestSaveBeforeContinue(actionDescription, continueAfterSave);
    private void DrawUnsavedChangesDialog(SceneGraph scene) => _unsavedChangesController.DrawUnsavedChangesDialog(scene);
    private bool SaveSceneFromUi(SceneGraph scene) => _unsavedChangesController.SaveSceneFromUi(scene);

    private sealed class UnsavedChangesController
    {
        private readonly CharacterStudioEditorUi _owner;

        public UnsavedChangesController(CharacterStudioEditorUi owner) =>
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));

    private const string UnsavedChangesPopupName = "Unsaved scene changes";
    private Action? _continueAfterSave;
    private bool _unsavedDialogPending;
    private bool _unsavedDialogOpened;
    private bool _exitAfterSave;
    private string _unsavedDialogMessage = string.Empty;

    public void RequestCloseConfirmation()
    {
        if (!_owner._history.IsDirty || _unsavedDialogPending) return;
        _continueAfterSave = null;
        _exitAfterSave = true;
        _unsavedDialogMessage = "This scene has edits that have not been saved.";
        _unsavedDialogPending = true;
        _unsavedDialogOpened = false;
    }

    public void RequestSaveBeforeContinue(string actionDescription, Action continueAfterSave)
    {
        if (!_owner._history.IsDirty)
        {
            continueAfterSave();
            return;
        }

        _continueAfterSave = continueAfterSave;
        _exitAfterSave = false;
        _unsavedDialogMessage = $"Save your scene before {actionDescription}?";
        _unsavedDialogPending = true;
        _unsavedDialogOpened = false;
    }

    public void DrawUnsavedChangesDialog(SceneGraph scene)
    {
        if (_unsavedDialogPending && !_unsavedDialogOpened)
        {
            ImGui.OpenPopup(UnsavedChangesPopupName);
            _unsavedDialogOpened = true;
        }

        if (!ImGui.BeginPopupModal(UnsavedChangesPopupName, ImGuiWindowFlags.AlwaysAutoResize)) return;

        ImGui.TextWrapped(_unsavedDialogMessage);
        if (_exitAfterSave)
        {
            ImGui.TextWrapped("Save before closing, leave without saving, or cancel and keep editing.");
            if (ImGui.Button("Save and close", new NumericsVector2(150f, 32f)))
            {
                if (SaveSceneFromUi(scene)) FinishUnsavedDecision(closeWithoutSaving: false);
            }
            ImGui.SameLine();
            if (ImGui.Button("Close without saving", new NumericsVector2(172f, 32f)))
                FinishUnsavedDecision(closeWithoutSaving: true);
        }
        else
        {
            ImGui.TextWrapped("Your current scene will stay open if you cancel.");
            if (ImGui.Button("Save and continue", new NumericsVector2(160f, 32f)))
            {
                if (SaveSceneFromUi(scene)) FinishUnsavedDecision(closeWithoutSaving: false);
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new NumericsVector2(110f, 32f)))
        {
            _continueAfterSave = null;
            _unsavedDialogPending = false;
            _unsavedDialogOpened = false;
            _exitAfterSave = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    public bool SaveSceneFromUi(SceneGraph scene)
    {
        if (_owner._isPlaying())
        {
            _owner._projectWorkspaceStatus = "Stop Play before saving the authored scene.";
            return false;
        }

        var scenePath = _owner._getCurrentScenePath();
        if (scenePath is null)
        {
            var projectPath = _owner._getCurrentProjectPath();
            var initialDirectory = projectPath is null
                ? Environment.CurrentDirectory
                : Path.Combine(Path.GetDirectoryName(projectPath)!, "Scenes");
            scenePath = CharacterStudioFilePickers.PickSceneSaveFile(
                initialDirectory, "Scene.json", _owner._windowHandle);
            if (scenePath is null) return false;
        }

        try
        {
            _owner._saveSceneAs(scenePath);
            _owner._history.MarkSaved();
            if (_owner._firstCreationLesson is { } lesson
                && CharacterStudioEditorUi.IsSamePath(lesson.ProjectFilePath, _owner._getCurrentProjectPath()))
                lesson.ObserveSaved(_owner._getCurrentProjectPath()!, scenePath, scene);
            _owner._projectWorkspaceStatus = $"Saved {Path.GetFileName(scenePath)}.";
            return true;
        }
        catch (Exception exception)
        {
            _owner._projectWorkspaceStatus = $"Could not save the scene: {exception.Message}";
            return false;
        }
    }

    private void FinishUnsavedDecision(bool closeWithoutSaving)
    {
        var exitAfterDecision = _exitAfterSave;
        var continueAfterSave = _continueAfterSave;
        _continueAfterSave = null;
        _unsavedDialogPending = false;
        _unsavedDialogOpened = false;
        _exitAfterSave = false;
        ImGui.CloseCurrentPopup();

        if (!exitAfterDecision)
        {
            continueAfterSave?.Invoke();
            return;
        }

        if (closeWithoutSaving)
            _owner._projectWorkspaceStatus = "Closed without saving the current scene.";
        _owner._confirmExit();
    }

    }
}
