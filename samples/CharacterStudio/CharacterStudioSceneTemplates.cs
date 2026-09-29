using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Authoring;
using Ember.Project;
using Ember.Scene;
using ImGuiNET;
using Microsoft.Xna.Framework;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector4 = System.Numerics.Vector4;

namespace CharacterStudio;

internal sealed partial class CharacterStudioEditorUi
{
    private SceneTemplateLibrary? _sceneTemplateLibrary;
    private IReadOnlyList<SceneTemplateLibraryEntry> _sceneTemplateEntries = Array.Empty<SceneTemplateLibraryEntry>();
    private string? _sceneTemplateProjectPath;
    private string _sceneTemplateNameDraft = "Room template";
    private string _sceneTemplateStatus = "Save a selected hierarchy to reuse it in another scene.";
    private Guid? _selectedSceneTemplateId;
    private Guid? _sceneTemplateDraftObjectId;

    private void DrawSceneTemplatePanel(SceneGraph scene)
    {
        ImGui.SetNextWindowPos(new NumericsVector2(232f, 56f));
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Min(430f, _logicalWidth),
            Math.Max(220f, _logicalHeight - 64f)));
        if (!ImGui.Begin("Scene templates", ImGuiWindowFlags.NoCollapse
                | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        var projectPath = _getCurrentProjectPath();
        if (projectPath is null)
        {
            _sceneTemplateLibrary = null;
            _sceneTemplateProjectPath = null;
            ImGui.TextWrapped("Scene templates are stored with a project. Create or open a project to save, place and update reusable hierarchies.");
            ImGui.End();
            return;
        }

        try
        {
            EnsureSceneTemplateLibrary(projectPath);
        }
        catch (Exception exception)
        {
            ImGui.TextWrapped($"Could not open the project template folder: {exception.Message}");
            ImGui.End();
            return;
        }

        var selected = _selectedObjectId is { } selectedId ? scene.Find(selectedId) : null;
        var wrapper = selected is null ? null : FindSceneTemplateWrapper(scene, selected);

        ImGui.TextWrapped("A template is a reusable copy of a selected object and its children. Place instances independently, then update them only when you choose.");
        if (ImGui.Button("Refresh templates")) RefreshSceneTemplateEntries();
        ImGui.SameLine();
        ImGui.TextDisabled($"{_sceneTemplateEntries.Count(entry => entry.IsValid)} available");

        ImGui.BeginChild("Project scene templates", new NumericsVector2(0f,
            Math.Max(78f, _logicalHeight * 0.19f)), ImGuiChildFlags.Borders);
        foreach (var entry in _sceneTemplateEntries)
        {
            if (entry.Template is { } template)
            {
                var label = $"{template.Name} · revision {template.Revision}##scene-template-{template.Id:N}";
                if (ImGui.Selectable(label, _selectedSceneTemplateId == template.Id))
                {
                    _selectedSceneTemplateId = template.Id;
                    _sceneTemplateNameDraft = template.Name;
                }
            }
            else
            {
                ImGui.TextColored(new NumericsVector4(1f, 0.65f, 0.35f, 1f),
                    $"Needs repair: {Path.GetFileName(entry.Path)}");
                ImGui.TextWrapped(entry.Error ?? "This template could not be read.");
            }
        }
        if (_sceneTemplateEntries.Count == 0)
            ImGui.TextWrapped("No templates yet. Select an object in the hierarchy and save it below.");
        ImGui.EndChild();

        var templateEntry = _sceneTemplateEntries.FirstOrDefault(entry =>
            entry.Template?.Id == _selectedSceneTemplateId);
        var chosenTemplate = templateEntry?.Template;
        if (chosenTemplate is not null)
        {
            ImGui.TextWrapped($"{chosenTemplate.Name} · {chosenTemplate.Scene.Objects.Count} objects · revision {chosenTemplate.Revision}");
            var canPlace = !_isPlaying();
            if (!canPlace) ImGui.BeginDisabled();
            if (ImGui.Button("Place beside selection", new NumericsVector2(-1f, 32f)))
                PlaceSceneTemplate(scene, chosenTemplate, selected);
            if (!canPlace) ImGui.EndDisabled();
        }

        ImGui.Separator();
        if (selected is null)
        {
            ImGui.TextWrapped("Select an object in the hierarchy to save its hierarchy as a template.");
        }
        else
        {
            if (_sceneTemplateDraftObjectId != selected.Id)
            {
                _sceneTemplateDraftObjectId = selected.Id;
                _sceneTemplateNameDraft = $"{selected.Name} template";
            }
            ImGui.TextDisabled($"Selected: {selected.Name}");
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##sceneTemplateName", "Template name",
                ref _sceneTemplateNameDraft, 128);

            var canSaveNew = !_isPlaying() && selected.TemplateInstance is null;
            if (!canSaveNew) ImGui.BeginDisabled();
            if (ImGui.Button("Save selection as new template", new NumericsVector2(-1f, 32f)))
                SaveSceneTemplate(scene, selected);
            if (!canSaveNew) ImGui.EndDisabled();
            if (selected.TemplateInstance is not null)
                ImGui.TextWrapped("Select an object inside this instance to save its own hierarchy.");

            if (chosenTemplate is not null && chosenTemplate.RootObjectId == selected.Id)
            {
                var canSaveRevision = !_isPlaying();
                if (!canSaveRevision) ImGui.BeginDisabled();
                if (ImGui.Button("Save changes to selected template", new NumericsVector2(-1f, 32f)))
                    SaveSceneTemplateRevision(scene, selected, chosenTemplate);
                if (!canSaveRevision) ImGui.EndDisabled();
            }
        }

        if (wrapper?.TemplateInstance is { } instance)
            DrawSceneTemplateInstanceActions(scene, wrapper, instance);

        ImGui.Separator();
        ImGui.TextWrapped(_sceneTemplateStatus);
        ImGui.End();
    }

    private void DrawSceneTemplateInstanceActions(SceneGraph scene, SceneObject wrapper,
        SceneTemplateInstanceComponent instance)
    {
        ImGui.Separator();
        ImGui.Text("Selected template instance");
        ImGui.TextWrapped($"Applied revision {instance.AppliedRevision}");
        DrawSceneTemplateOrphans(scene, instance);

        var sourceEntry = _sceneTemplateEntries.FirstOrDefault(entry =>
            entry.Template?.Id == instance.TemplateId);
        var source = sourceEntry?.Template;
        if (source is null)
        {
            ImGui.TextColored(new NumericsVector4(1f, 0.65f, 0.35f, 1f),
                "Template source is missing or unreadable. The expanded scene remains available and playable.");
            if (_isPlaying()) ImGui.BeginDisabled();
            if (ImGui.Button("Locate matching template source…", new NumericsVector2(-1f, 32f)))
                RelinkSceneTemplate(instance.TemplateId);
            if (_isPlaying()) ImGui.EndDisabled();
            return;
        }

        ImGui.TextWrapped($"Source: {source.Name} · revision {source.Revision}");
        if (source.Revision > instance.AppliedRevision)
        {
            if (_isPlaying()) ImGui.BeginDisabled();
            if (ImGui.Button("Update this instance", new NumericsVector2(-1f, 32f)))
                UpdateSceneTemplateInstance(scene, wrapper, source);
            if (_isPlaying()) ImGui.EndDisabled();
            ImGui.TextWrapped("Only fields that still match the previous template are refreshed. Local edits are kept.");
        }
        else if (source.Revision == instance.AppliedRevision)
        {
            ImGui.TextDisabled("This instance uses the latest saved template revision.");
        }
        else
        {
            ImGui.TextColored(new NumericsVector4(1f, 0.65f, 0.35f, 1f),
                "The project template is older than this instance. Locate the newer source before updating.");
            if (_isPlaying()) ImGui.BeginDisabled();
            if (ImGui.Button("Locate newer template source…", new NumericsVector2(-1f, 32f)))
                RelinkSceneTemplate(instance.TemplateId);
            if (_isPlaying()) ImGui.EndDisabled();
        }
    }

    private static void DrawSceneTemplateOrphans(SceneGraph scene,
        SceneTemplateInstanceComponent instance)
    {
        if (instance.OrphanedObjectIds.Count == 0) return;
        ImGui.TextColored(new NumericsVector4(1f, 0.78f, 0.28f, 1f),
            $"{instance.OrphanedObjectIds.Count} older object(s) were kept because they contain local edits or children.");
        ImGui.BeginChild("Retained template objects", new NumericsVector2(0f,
            Math.Min(96f, 26f * instance.OrphanedObjectIds.Count)), ImGuiChildFlags.Borders);
        foreach (var id in instance.OrphanedObjectIds)
        {
            var item = scene.Find(id);
            ImGui.TextWrapped(item is null
                ? $"Missing retained object {id:N}"
                : $"Kept: {item.Name}");
        }
        ImGui.EndChild();
    }

    private void EnsureSceneTemplateLibrary(string projectPath)
    {
        var fullProjectPath = Path.GetFullPath(projectPath);
        if (_sceneTemplateLibrary is not null
            && string.Equals(_sceneTemplateProjectPath, fullProjectPath, StringComparison.OrdinalIgnoreCase))
            return;

        var project = EngineProjectFile.Load(fullProjectPath);
        _sceneTemplateLibrary = new SceneTemplateLibrary(project.RootDirectory);
        _sceneTemplateProjectPath = fullProjectPath;
        _selectedSceneTemplateId = null;
        _sceneTemplateEntries = _sceneTemplateLibrary.ReadAll();
        _sceneTemplateStatus = _sceneTemplateEntries.Count == 0
            ? "Save a selected hierarchy to reuse it in another scene."
            : "Choose a template to place, or select an instance in the scene to review its source.";
    }

    private void RefreshSceneTemplateEntries()
    {
        if (_sceneTemplateLibrary is null) return;
        try
        {
            _sceneTemplateEntries = _sceneTemplateLibrary.ReadAll();
            if (_selectedSceneTemplateId is { } selectedId
                && !_sceneTemplateEntries.Any(entry => entry.Template?.Id == selectedId))
                _selectedSceneTemplateId = null;
            _sceneTemplateStatus = $"Found {_sceneTemplateEntries.Count(entry => entry.IsValid)} usable template(s).";
        }
        catch (Exception exception)
        {
            _sceneTemplateStatus = $"Could not refresh templates: {exception.Message}";
        }
    }

    private void SaveSceneTemplate(SceneGraph scene, SceneObject selected)
    {
        if (_sceneTemplateLibrary is null) return;
        try
        {
            var template = _sceneTemplateLibrary.SaveNew(scene, selected.Id, _sceneTemplateNameDraft);
            RefreshSceneTemplateEntries();
            _selectedSceneTemplateId = template.Id;
            _sceneTemplateStatus = $"Saved {template.Name} as revision {template.Revision} in the project Templates folder.";
        }
        catch (Exception exception)
        {
            _sceneTemplateStatus = $"Could not save the template: {exception.Message}";
        }
    }

    private void SaveSceneTemplateRevision(SceneGraph scene, SceneObject selected,
        SceneTemplateSnapshot template)
    {
        if (_sceneTemplateLibrary is null) return;
        try
        {
            var saved = _sceneTemplateLibrary.SaveRevision(scene, selected.Id, template.Id,
                _sceneTemplateNameDraft);
            RefreshSceneTemplateEntries();
            _selectedSceneTemplateId = saved.Id;
            _sceneTemplateStatus = $"Saved revision {saved.Revision} of {saved.Name}. Instances update only when you choose Update.";
        }
        catch (Exception exception)
        {
            _sceneTemplateStatus = $"Could not save template changes: {exception.Message}";
        }
    }

    private void PlaceSceneTemplate(SceneGraph scene, SceneTemplateSnapshot template,
        SceneObject? selected)
    {
        try
        {
            var position = selected is null
                ? Vector3.Zero
                : scene.GetWorldMatrix(selected.Id).Translation + new Vector3(2f, 0f, 0f);
            var command = new PlaceSceneTemplateCommand(template, position, FindCurrentWorldCell()?.Id);
            _history.Execute(scene, command);
            if (command.InstanceObjectId is { } wrapperId) SelectObject(wrapperId);
            _sceneTemplateStatus = $"Placed {template.Name}. Undo is available; the original hierarchy is unchanged.";
        }
        catch (Exception exception)
        {
            _sceneTemplateStatus = $"Could not place the template: {exception.Message}";
        }
    }

    private void UpdateSceneTemplateInstance(SceneGraph scene, SceneObject wrapper,
        SceneTemplateSnapshot template)
    {
        try
        {
            var command = new UpdateSceneTemplateCommand(wrapper.Id, template);
            _history.Execute(scene, command);
            _sceneTemplateStatus = command.OrphanedObjectIds.Count == 0
                ? $"Updated this instance to revision {template.Revision}. Undo is available."
                : $"Updated to revision {template.Revision}; kept {command.OrphanedObjectIds.Count} edited or referenced older object(s). Review the warning above.";
        }
        catch (Exception exception)
        {
            _sceneTemplateStatus = $"Could not update this instance: {exception.Message}";
        }
    }

    private void RelinkSceneTemplate(Guid expectedTemplateId)
    {
        if (_sceneTemplateLibrary is null) return;
        var sourcePath = CharacterStudioFilePickers.PickSceneTemplateFile(
            _sceneTemplateLibrary.DirectoryPath, _windowHandle);
        if (sourcePath is null) return;
        try
        {
            var template = _sceneTemplateLibrary.Relink(expectedTemplateId, sourcePath);
            RefreshSceneTemplateEntries();
            _selectedSceneTemplateId = template.Id;
            _sceneTemplateStatus = $"Linked {template.Name} revision {template.Revision} into this project's Templates folder.";
        }
        catch (Exception exception)
        {
            _sceneTemplateStatus = $"Could not relink the template: {exception.Message}";
        }
    }

    private static SceneObject? FindSceneTemplateWrapper(SceneGraph scene, SceneObject selected)
    {
        var visited = new HashSet<Guid>();
        SceneObject? current = selected;
        while (current is not null && visited.Add(current.Id))
        {
            if (current.TemplateInstance is not null) return current;
            current = current.ParentId is { } parentId ? scene.Find(parentId) : null;
        }
        return null;
    }
}
