using Ember.Scene;
using Ember.World;
using ImGuiNET;
using Microsoft.Xna.Framework;
using System;
using System.IO;
using System.Linq;
using NumericsVector2 = System.Numerics.Vector2;

namespace CharacterStudio;

internal sealed partial class CharacterStudioEditorUi
{
    private string _placementTemplatePath = string.Empty;
    private string _placementTemplateNameDraft = string.Empty;
    private string _placementTemplateStatus = "Load or create a placement template library.";
    private WorldEntityPlacementTemplateSet _placementTemplates = new();
    private Guid? _selectedPlacementTemplateId;
    private bool _placementTemplatesLoaded;

    private void DrawPlacementTemplatesTab(SceneGraph scene)
    {
        if (!_placementTemplatesLoaded)
        {
            _placementTemplatePath = DefaultPlacementTemplatePath();
            LoadPlacementTemplates();
        }

        ImGui.BeginChild("RPG placement templates", new NumericsVector2(0f, 0f), ImGuiChildFlags.Borders);
        ImGui.TextWrapped("Templates hold shared RPG definition and transform defaults. Instance override flags preserve local values when defaults change.");
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("Template library file", ref _placementTemplatePath, 1024);
        if (ImGui.Button("Load library")) LoadPlacementTemplates();
        ImGui.SameLine();
        if (ImGui.Button("Save library")) SavePlacementTemplates();

        ImGui.BeginChild("Placement template list", new NumericsVector2(0f, 110f), ImGuiChildFlags.Borders);
        foreach (var template in _placementTemplates.All.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (ImGui.Selectable($"{template.Name} · {template.Kind}:{template.DefinitionId}##template-{template.Id:N}",
                _selectedPlacementTemplateId == template.Id))
            {
                _selectedPlacementTemplateId = template.Id;
                _placementTemplateNameDraft = template.Name;
            }
        }
        ImGui.EndChild();

        var selectedTemplate = GetSelectedPlacementTemplate();
        if (selectedTemplate is not null)
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("Template name", ref _placementTemplateNameDraft, 256);
            var selectedObject = _selectedObjectId is { } objectId ? scene.Find(objectId) : null;
            var canUpdate = !_isPlaying()
                && selectedObject?.WorldEntity?.TemplateId == selectedTemplate.Id;
            if (!canUpdate) ImGui.BeginDisabled();
            if (ImGui.Button("Update defaults from selected placement"))
                UpdatePlacementTemplateFromSelection(scene, selectedTemplate, selectedObject!);
            if (!canUpdate) ImGui.EndDisabled();

            var canPlace = !_isPlaying();
            if (!canPlace) ImGui.BeginDisabled();
            if (ImGui.Button("Place template instance")) PlaceTemplateInstance(scene, selectedTemplate);
            if (!canPlace) ImGui.EndDisabled();

            ImGui.TextDisabled($"Template ID {selectedTemplate.Id:N}; changes affect {scene.Objects.Count(item => item.WorldEntity?.TemplateId == selectedTemplate.Id)} instance(s) in the active scene.");
        }

        var activeSelection = _selectedObjectId is { } selectedId ? scene.Find(selectedId) : null;
        var selectedPlacement = activeSelection?.WorldEntity;
        ImGui.Separator();
        if (selectedPlacement is null)
            ImGui.TextDisabled("Select an RPG placement to create a template or edit its override flags.");
        else
        {
            ImGui.TextWrapped($"Selected {selectedPlacement.Kind}: {selectedPlacement.DefinitionId} · instance {selectedPlacement.InstanceId:N}");
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText("New template name", ref _placementTemplateNameDraft, 256);
            if (!_isPlaying() && ImGui.Button("Create or update template from selected"))
                CreateOrUpdatePlacementTemplate(scene, activeSelection!);

            if (selectedPlacement.TemplateId is { } templateId
                && _placementTemplates.Get(templateId) is not null)
            {
                var position = (selectedPlacement.TemplateOverrides & PlacementTemplateOverrideFlags.Position) != 0;
                if (ImGui.Checkbox("Override position", ref position))
                    SetPlacementOverride(scene, activeSelection!, PlacementTemplateOverrideFlags.Position, position);
                var rotation = (selectedPlacement.TemplateOverrides & PlacementTemplateOverrideFlags.Rotation) != 0;
                if (ImGui.Checkbox("Override rotation", ref rotation))
                    SetPlacementOverride(scene, activeSelection!, PlacementTemplateOverrideFlags.Rotation, rotation);
                var scale = (selectedPlacement.TemplateOverrides & PlacementTemplateOverrideFlags.Scale) != 0;
                if (ImGui.Checkbox("Override scale", ref scale))
                    SetPlacementOverride(scene, activeSelection!, PlacementTemplateOverrideFlags.Scale, scale);
                var definition = (selectedPlacement.TemplateOverrides & PlacementTemplateOverrideFlags.Definition) != 0;
                if (ImGui.Checkbox("Override RPG definition", ref definition))
                    SetPlacementOverride(scene, activeSelection!, PlacementTemplateOverrideFlags.Definition, definition);
            }
            else if (selectedPlacement.TemplateId is not null)
                ImGui.TextColored(new System.Numerics.Vector4(1f, 0.5f, 0.3f, 1f),
                    $"Missing template reference: {selectedPlacement.TemplateId:N}");
        }

        ImGui.TextWrapped(_placementTemplateStatus);
        ImGui.EndChild();
    }

    private string DefaultPlacementTemplatePath()
    {
        if (_worldManifest is not null)
            return Path.Combine(_worldManifest.RootDirectory, "PlacementTemplates.json");
        if (_getCurrentScenePath() is { } scenePath)
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(scenePath))!, "PlacementTemplates.json");
        return Path.Combine(Environment.CurrentDirectory, "PlacementTemplates.json");
    }

    private WorldEntityPlacementTemplate? GetSelectedPlacementTemplate() =>
        _selectedPlacementTemplateId is { } id ? _placementTemplates.Get(id) : null;

    private void LoadPlacementTemplates()
    {
        _placementTemplatesLoaded = true;
        _selectedPlacementTemplateId = null;
        try
        {
            _placementTemplates = File.Exists(_placementTemplatePath)
                ? WorldEntityPlacementTemplateSet.Load(_placementTemplatePath)
                : new WorldEntityPlacementTemplateSet();
            _placementTemplateStatus = File.Exists(_placementTemplatePath)
                ? $"Loaded {_placementTemplates.Count} template(s)."
                : "No library exists yet; create a template and save the library.";
        }
        catch (Exception exception)
        {
            _placementTemplateStatus = $"Could not load template library: {exception.Message}";
        }
    }

    private void SavePlacementTemplates()
    {
        try
        {
            _placementTemplates.SaveAtomic(_placementTemplatePath);
            _placementTemplateStatus = $"Saved {_placementTemplates.Count} template(s) to {_placementTemplatePath}.";
        }
        catch (Exception exception)
        {
            _placementTemplateStatus = $"Could not save template library: {exception.Message}";
        }
    }

    private void CreateOrUpdatePlacementTemplate(SceneGraph scene, SceneObject sceneObject)
    {
        var placement = sceneObject.WorldEntity;
        if (placement is null) return;
        var name = _placementTemplateNameDraft.Trim();
        if (name.Length == 0) name = sceneObject.Name;
        var existing = placement.TemplateId is { } existingId ? _placementTemplates.Get(existingId) : null;
        var template = new WorldEntityPlacementTemplate
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            Name = name,
            Kind = placement.Kind,
            DefinitionId = placement.DefinitionId,
            Transform = PlacementTemplateTransform.From(sceneObject.Transform)
        };
        try
        {
            if (existing is null) _placementTemplates.Add(template);
            else _placementTemplates.Replace(template);
            var replacement = new WorldEntityPlacementComponent(placement.Kind,
                placement.DefinitionId, placement.InstanceId, template.Id,
                existing is null ? PlacementTemplateOverrideFlags.None : placement.TemplateOverrides);
            RunStructureChange(scene, () => _history.Execute(scene,
                new WorldEntityPlacementEditCommand(sceneObject.Id, replacement)));
            RunStructureChange(scene, () => _history.Execute(scene,
                new WorldEntityPlacementTemplateApplyCommand(template)));
            _selectedPlacementTemplateId = template.Id;
            _placementTemplateNameDraft = template.Name;
            _placementTemplateStatus = $"Created/updated template '{template.Name}'. Save the library to keep it on disk.";
        }
        catch (Exception exception)
        {
            _placementTemplateStatus = $"Could not create/update template: {exception.Message}";
        }
    }

    private void UpdatePlacementTemplateFromSelection(SceneGraph scene,
        WorldEntityPlacementTemplate template, SceneObject selectedObject)
    {
        var placement = selectedObject.WorldEntity;
        if (placement is null) return;
        try
        {
            var updated = template with
            {
                Name = string.IsNullOrWhiteSpace(_placementTemplateNameDraft)
                    ? template.Name : _placementTemplateNameDraft.Trim(),
                Kind = placement.Kind,
                DefinitionId = placement.DefinitionId,
                Transform = PlacementTemplateTransform.From(selectedObject.Transform)
            };
            _placementTemplates.Replace(updated);
            var affected = scene.Objects.Count(item => item.WorldEntity?.TemplateId == updated.Id);
            RunStructureChange(scene, () => _history.Execute(scene,
                new WorldEntityPlacementTemplateApplyCommand(updated)));
            _placementTemplateStatus = $"Updated '{updated.Name}' and refreshed {affected} active-scene instance(s).";
        }
        catch (Exception exception)
        {
            _placementTemplateStatus = $"Could not update template: {exception.Message}";
        }
    }

    private void PlaceTemplateInstance(SceneGraph scene, WorldEntityPlacementTemplate template)
    {
        try
        {
            var placement = WorldEntityPlacementTemplateSystem.CreatePlacement(scene, template);
            CommitActiveTransformEdit(scene);
            RunStructureChange(scene, () => _history.Execute(scene, new CreateSceneObjectCommand(placement)));
            _selectedObjectId = placement.Id;
            _placementTemplateStatus = $"Placed '{template.Name}' as world instance {placement.WorldEntity!.InstanceId:N}.";
        }
        catch (Exception exception)
        {
            _placementTemplateStatus = $"Could not place template: {exception.Message}";
        }
    }

    private void SetPlacementOverride(SceneGraph scene, SceneObject sceneObject,
        PlacementTemplateOverrideFlags flag, bool enabled)
    {
        var placement = sceneObject.WorldEntity;
        if (placement is null) return;
        var overrides = enabled ? placement.TemplateOverrides | flag : placement.TemplateOverrides & ~flag;
        var replacement = new WorldEntityPlacementComponent(placement.Kind, placement.DefinitionId,
            placement.InstanceId, placement.TemplateId, overrides);
        RunStructureChange(scene, () => _history.Execute(scene,
            new WorldEntityPlacementEditCommand(sceneObject.Id, replacement)));
    }

    private sealed class WorldEntityPlacementEditCommand(Guid objectId,
        WorldEntityPlacementComponent replacement) : ISceneCommand
    {
        private WorldEntityPlacementComponent? _before;

        public void Apply(SceneGraph scene)
        {
            var sceneObject = scene.Find(objectId)
                ?? throw new InvalidOperationException($"Cannot edit missing placement object {objectId}.");
            _before ??= sceneObject.WorldEntity;
            sceneObject.WorldEntity = replacement;
        }

        public void Revert(SceneGraph scene)
        {
            var sceneObject = scene.Find(objectId)
                ?? throw new InvalidOperationException($"Cannot undo missing placement object {objectId}.");
            sceneObject.WorldEntity = _before;
        }
    }
}
