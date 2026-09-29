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
    public void Render()
    {
        _renderer.Draw(ImGui.GetDrawData(), _letterboxOffsetX, _letterboxOffsetY);
    }

    private void UpdateKeyboard(KeyboardState keyboard)
    {
        foreach (var (source, target) in SpecialKeys)
            _io.AddKeyEvent(target, keyboard.IsKeyDown(source));

        for (var index = 0; index < 26; index++)
        {
            _io.AddKeyEvent((ImGuiKey)((int)ImGuiKey.A + index),
                keyboard.IsKeyDown((Keys)((int)Keys.A + index)));
        }

        for (var index = 0; index < 10; index++)
        {
            _io.AddKeyEvent((ImGuiKey)((int)ImGuiKey._0 + index),
                keyboard.IsKeyDown((Keys)((int)Keys.D0 + index)));
        }

        var leftControl = keyboard.IsKeyDown(Keys.LeftControl);
        var rightControl = keyboard.IsKeyDown(Keys.RightControl);
        var leftShift = keyboard.IsKeyDown(Keys.LeftShift);
        var rightShift = keyboard.IsKeyDown(Keys.RightShift);
        var leftAlt = keyboard.IsKeyDown(Keys.LeftAlt);
        var rightAlt = keyboard.IsKeyDown(Keys.RightAlt);
        _io.AddKeyEvent(ImGuiKey.LeftCtrl, leftControl);
        _io.AddKeyEvent(ImGuiKey.RightCtrl, rightControl);
        _io.AddKeyEvent(ImGuiKey.LeftShift, leftShift);
        _io.AddKeyEvent(ImGuiKey.RightShift, rightShift);
        _io.AddKeyEvent(ImGuiKey.LeftAlt, leftAlt);
        _io.AddKeyEvent(ImGuiKey.RightAlt, rightAlt);
        _io.AddKeyEvent(ImGuiKey.ModCtrl, leftControl || rightControl);
        _io.AddKeyEvent(ImGuiKey.ModShift, leftShift || rightShift);
        _io.AddKeyEvent(ImGuiKey.ModAlt, leftAlt || rightAlt);
    }

    private readonly ScenePanel _scenePanel;

    private void DrawPanel(SceneGraph scene) => _scenePanel.DrawPanel(scene);

    private sealed class ScenePanel
    {
        private readonly CharacterStudioEditorUi _owner;

        public ScenePanel(CharacterStudioEditorUi owner) =>
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));

        public void DrawPanel(SceneGraph scene)
    {
        if (!_owner._initialSelectionSet)
        {
            _owner._selectedObjectId = scene.Objects.FirstOrDefault(item => item.CharacterSettings is not null)?.Id
                ?? scene.Objects.FirstOrDefault()?.Id;
            _owner._initialSelectionSet = true;
        }
        else if (_owner._selectedObjectId is { } selectedId && scene.Find(selectedId) is null)
            _owner._selectedObjectId = null;

        var inspectorWidth = Math.Min(280f, _owner._logicalWidth * 0.24f);
        ImGui.SetNextWindowPos(new NumericsVector2(Math.Max(0f, _owner._logicalWidth - inspectorWidth), 48f));
        ImGui.SetNextWindowSize(new NumericsVector2(inspectorWidth, Math.Max(160f, _owner._logicalHeight - 48f)));
        if (!ImGui.Begin("Inspector", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        DrawWorkspaceFailure();
        _owner.DrawPlaySettingsControls(scene);

        if (_owner._isPlaying())
        {
            ImGui.TextColored(new NumericsVector4(1f, 0.72f, 0.2f, 1f), "PLAYING ON CLONE");
            ImGui.TextWrapped("Changes made during Play are temporary. Stop to return to the scene you were editing.");
            if (ImGui.Button("Interact")) _owner._interact();
            var volume = _owner._getInteractionVolume();
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("Interaction volume", ref volume, 0f, 1f, "%.2f"))
                _owner._setInteractionVolume(volume);
        }
        ImGui.Text("Objects");
        ImGui.BeginChild("Scene hierarchy", new NumericsVector2(0f, Math.Min(132f, _owner._logicalHeight * 0.2f)), ImGuiChildFlags.Borders);
        DrawSceneHierarchy(scene);
        ImGui.EndChild();

        if ((_owner._activeTransformObjectId is not null && _owner._selectedObjectId != _owner._activeTransformObjectId)
            || (_owner._activeBoxColliderObjectId is not null && _owner._selectedObjectId != _owner._activeBoxColliderObjectId))
            _owner.CommitActiveTransformEdit(scene);

        if (_owner._selectedObjectId is not { } objectId || scene.Find(objectId) is not { } selected)
        {
            var pendingPreviewName = _owner._getPendingAssetPreviewName();
            if (pendingPreviewName is not null)
            {
                ImGui.TextWrapped($"Previewing {pendingPreviewName}. It is not in the scene yet; use the Add panel to place it or cancel the preview.");
                if (!_owner._showAddLibrary && ImGui.Button("Show model actions", new NumericsVector2(-1f, 34f)))
                    _owner._showAddLibrary = true;
            }
            else if (scene.Objects.Count == 0)
            {
                ImGui.TextWrapped("This scene is empty. Add a simple object or a model to give yourself something to build with.");
                if (ImGui.Button("Add an empty object", new NumericsVector2(-1f, 34f)))
                    _owner.CreateEmpty(scene);

                if (_owner._getCurrentProjectPath() is null)
                {
                    if (ImGui.Button("Make or open a project…", new NumericsVector2(-1f, 34f)))
                        _owner._showHome = true;
                    ImGui.TextWrapped("Create or open a project to add a model.");
                }
                else if (ImGui.Button("Browse for a model…", new NumericsVector2(-1f, 34f)))
                {
                    _owner.BrowseForModel();
                }
            }
            else
            {
                ImGui.TextWrapped("Nothing is selected. Choose an object in the list above to edit it.");
            }
            ImGui.End();
            return;
        }

        DrawParentControl(scene, selected);
        ImGui.Separator();
        ImGui.Text($"Selected: {selected.Name}");
        if (ImGui.Button("Duplicate")) _owner.Duplicate(scene, selected);
        ImGui.SameLine();
        if (ImGui.Button("Delete")) _owner.Delete(scene, selected.Id);

        ImGui.Separator();
        _owner.DrawWhatHappensControls(scene, selected);

        ImGui.Separator();
        ImGui.Text("Change this object");
        if (ImGui.RadioButton("Move", _owner._transformTool == TransformTool.Move))
            _owner._transformTool = TransformTool.Move;
        ImGui.SameLine();
        if (ImGui.RadioButton("Turn", _owner._transformTool == TransformTool.Turn))
            _owner._transformTool = TransformTool.Turn;
        ImGui.SameLine();
        if (ImGui.RadioButton("Size", _owner._transformTool == TransformTool.Size))
            _owner._transformTool = TransformTool.Size;
        _owner.DrawTransformToolActions(scene, selected);
        if (ImGui.TreeNode("Why?"))
        {
            ImGui.TextWrapped("A transform combines position, rotation and scale to place an object in the scene. It changes this scene object, not the source model, so several objects can reuse one model with different placements.");
            ImGui.TreePop();
        }

        if (ImGui.TreeNode("More details"))
        {
            var transform = selected.Transform;
            var position = new NumericsVector3(transform.Position.X, transform.Position.Y, transform.Position.Z);
            ImGui.Text("Position");
            ImGui.SetNextItemWidth(-1f);
            var positionChanged = ImGui.InputFloat3("##position", ref position);
            _owner.TrackTransformInput(scene, selected.Id, transform, positionChanged, () =>
            {
                if (CharacterStudioEditorUi.IsFinite(position))
                    transform.Position = new Microsoft.Xna.Framework.Vector3(position.X, position.Y, position.Z);
            });

            var euler = CharacterStudioEditorUi.ToEulerDegrees(transform.Rotation);
            ImGui.Text("Rotation XYZ (degrees)");
            ImGui.SetNextItemWidth(-1f);
            var rotationChanged = ImGui.InputFloat3("##rotation", ref euler);
            _owner.TrackTransformInput(scene, selected.Id, transform, rotationChanged, () =>
            {
                if (CharacterStudioEditorUi.IsFinite(euler))
                {
                    var radians = MathF.PI / 180f;
                    transform.Rotation = Microsoft.Xna.Framework.Quaternion.Normalize(
                        Microsoft.Xna.Framework.Quaternion.CreateFromYawPitchRoll(
                            euler.Y * radians, euler.X * radians, euler.Z * radians));
                }
            });

            var scale = new NumericsVector3(transform.Scale.X, transform.Scale.Y, transform.Scale.Z);
            ImGui.Text("Scale");
            ImGui.SetNextItemWidth(-1f);
            var scaleChanged = ImGui.InputFloat3("##scale", ref scale);
            _owner.TrackTransformInput(scene, selected.Id, transform, scaleChanged, () =>
            {
                if (CharacterStudioEditorUi.IsFinite(scale))
                    transform.Scale = new Microsoft.Xna.Framework.Vector3(scale.X, scale.Y, scale.Z);
            });
            ImGui.TreePop();
        }

        _owner.DrawBoxColliderControls(scene, selected);
        _owner.DrawCharacterControls(selected);
        _owner.DrawLightingControls();
        ImGui.End();
    }

    private void DrawSceneHierarchy(SceneGraph scene)
    {
        var childrenByParent = scene.Objects
            .Where(item => item.ParentId is not null)
            .GroupBy(item => item.ParentId!.Value)
            .ToDictionary(group => group.Key,
                group => group.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Id).ToArray());
        foreach (var root in scene.Objects.Where(item => item.ParentId is null)
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Id))
            DrawSceneHierarchyNode(root, childrenByParent, isRoot: true);
    }

    private void DrawSceneHierarchyNode(SceneObject item,
        IReadOnlyDictionary<Guid, SceneObject[]> childrenByParent, bool isRoot)
    {
        var hasChildren = childrenByParent.TryGetValue(item.Id, out var children) && children.Length > 0;
        var flags = ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.OpenOnArrow;
        if (_owner._selectedObjectId == item.Id) flags |= ImGuiTreeNodeFlags.Selected;
        if (hasChildren && isRoot) flags |= ImGuiTreeNodeFlags.DefaultOpen;
        if (!hasChildren) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        var isOpen = ImGui.TreeNodeEx($"{item.Name}##{item.Id:N}", flags);
        if (ImGui.IsItemClicked()) _owner._selectedObjectId = item.Id;
        if (!hasChildren || !isOpen) return;
        foreach (var child in children!) DrawSceneHierarchyNode(child, childrenByParent, isRoot: false);
        ImGui.TreePop();
    }

    private void DrawParentControl(SceneGraph scene, SceneObject selected)
    {
        var currentParentName = selected.ParentId is { } parentId
            ? scene.Find(parentId)?.Name ?? "Missing parent"
            : "No parent";
        ImGui.Text("Parent");
        if (ImGui.BeginCombo("##scene-parent", currentParentName))
        {
            if (ImGui.Selectable("No parent", selected.ParentId is null)
                && selected.ParentId is not null)
                ReparentObject(scene, selected, null);

            foreach (var candidate in scene.Objects
                         .Where(item => item.Id != selected.Id
                             && !IsHierarchyDescendant(scene, item.Id, selected.Id))
                         .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Id))
            {
                var label = $"{candidate.Name}##parent-{candidate.Id:N}";
                if (ImGui.Selectable(label, selected.ParentId == candidate.Id)
                    && selected.ParentId != candidate.Id)
                    ReparentObject(scene, selected, candidate.Id);
            }
            ImGui.EndCombo();
        }
        ImGui.TextWrapped("Changing parent keeps this object's world placement when it can be represented without changing its shape.");
    }

    private void ReparentObject(SceneGraph scene, SceneObject selected, Guid? parentId)
    {
        try
        {
            _owner._history.Execute(scene, new ReparentSceneObjectCommand(selected.Id, parentId));
            _owner._afterStructureChange();
            _owner._projectWorkspaceStatus = "Parent changed. Undo restores the previous relationship and placement.";
        }
        catch (Exception exception)
        {
            _owner._projectWorkspaceStatus = $"Could not change parent: {exception.Message}";
        }
    }

    private static bool IsHierarchyDescendant(SceneGraph scene, Guid possibleDescendantId, Guid ancestorId)
    {
        var cursor = scene.Find(possibleDescendantId);
        while (cursor?.ParentId is { } parentId)
        {
            if (parentId == ancestorId) return true;
            cursor = scene.Find(parentId);
        }
        return false;
    }

    private void DrawWorkspaceFailure()
    {
        var status = _owner._projectWorkspaceStatus;
        if (!status.StartsWith("Could not", StringComparison.OrdinalIgnoreCase)) return;

        ImGui.Separator();
        ImGui.Text("Action needs attention");
        ImGui.TextWrapped(status);

        var nextStep = status.Contains("save", StringComparison.OrdinalIgnoreCase)
            ? "Check that the project folder is writable, then try Save again."
            : status.Contains("reload", StringComparison.OrdinalIgnoreCase)
                ? "Repair or replace the selected GLB in the project, then reload the model again."
            : status.Contains("cancel", StringComparison.OrdinalIgnoreCase)
                ? "Try canceling the preview again, or add the previewed model from the Add panel."
                : status.Contains("open", StringComparison.OrdinalIgnoreCase)
                    ? "Choose another project file, or return Home and try Open again."
                    : status.Contains("create", StringComparison.OrdinalIgnoreCase)
                        ? "Choose a valid project name and a folder you can write to."
                        : "Choose another model from Browse, or review the supported model features.";

        ImGui.TextWrapped(nextStep);
        if (ImGui.Button("Dismiss message"))
            _owner._projectWorkspaceStatus = string.Empty;
        ImGui.Separator();
    }


    }
}
