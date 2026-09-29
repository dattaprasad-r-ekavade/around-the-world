using System;
using System.IO;
using Ember.Scene;
using ImGuiNET;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector4 = System.Numerics.Vector4;

namespace CharacterStudio;

internal sealed partial class CharacterStudioEditorUi
{
    private const string UnsavedChangesPopupName = "Unsaved scene changes";
    private readonly Action _confirmExit;
    private Action? _continueAfterSave;
    private bool _unsavedDialogPending;
    private bool _unsavedDialogOpened;
    private bool _exitAfterSave;
    private string _unsavedDialogMessage = string.Empty;

    public void RequestCloseConfirmation()
    {
        if (!_history.IsDirty || _unsavedDialogPending) return;
        _continueAfterSave = null;
        _exitAfterSave = true;
        _unsavedDialogMessage = "This scene has edits that have not been saved.";
        _unsavedDialogPending = true;
        _unsavedDialogOpened = false;
    }

    private void RequestSaveBeforeContinue(string actionDescription, Action continueAfterSave)
    {
        if (!_history.IsDirty)
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

    private void DrawUnsavedChangesDialog(SceneGraph scene)
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

    private bool SaveSceneFromUi(SceneGraph scene)
    {
        if (_isPlaying())
        {
            _projectWorkspaceStatus = "Stop Play before saving the authored scene.";
            return false;
        }

        var scenePath = _getCurrentScenePath();
        if (scenePath is null)
        {
            var projectPath = _getCurrentProjectPath();
            var initialDirectory = projectPath is null
                ? Environment.CurrentDirectory
                : Path.Combine(Path.GetDirectoryName(projectPath)!, "Scenes");
            scenePath = CharacterStudioFilePickers.PickSceneSaveFile(
                initialDirectory, "Scene.json", _windowHandle);
            if (scenePath is null) return false;
        }

        try
        {
            _saveSceneAs(scenePath);
            _history.MarkSaved();
            if (_firstCreationLesson is { } lesson
                && IsSamePath(lesson.ProjectFilePath, _getCurrentProjectPath()))
                lesson.ObserveSaved(_getCurrentProjectPath()!, scenePath, scene);
            _projectWorkspaceStatus = $"Saved {Path.GetFileName(scenePath)}.";
            return true;
        }
        catch (Exception exception)
        {
            _projectWorkspaceStatus = $"Could not save the scene: {exception.Message}";
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
            _projectWorkspaceStatus = "Closed without saving the current scene.";
        _confirmExit();
    }
}
