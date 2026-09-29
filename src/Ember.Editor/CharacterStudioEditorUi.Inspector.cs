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
    private readonly InspectorPanel _inspectorPanel;

    private void DrawTransformToolActions(SceneGraph scene, SceneObject selected) => _inspectorPanel.DrawTransformToolActions(scene, selected);
    private void DrawSequencePanel() => _inspectorPanel.DrawSequencePanel();
    private void DrawBoxColliderControls(SceneGraph scene, SceneObject selected) => _inspectorPanel.DrawBoxColliderControls(scene, selected);
    private void DrawCharacterControls(SceneObject selected) => _inspectorPanel.DrawCharacterControls(selected);
    private void DrawLightingControls() => _inspectorPanel.DrawLightingControls();
    private void DrawWhatHappensControls(SceneGraph scene, SceneObject selected) => _inspectorPanel.DrawWhatHappensControls(scene, selected);
    private void TrackTransformInput(SceneGraph scene, Guid objectId, Transform transform, bool changed, Action applyChange) => _inspectorPanel.TrackTransformInput(scene, objectId, transform, changed, applyChange);
    private void CommitActiveTransformEdit(SceneGraph scene) => _inspectorPanel.CommitActiveTransformEdit(scene);
    private void RunHistoryAction(SceneGraph scene, bool undo) => _inspectorPanel.RunHistoryAction(scene, undo);
    private void CreateEmpty(SceneGraph scene) => _inspectorPanel.CreateEmpty(scene);
    private void Duplicate(SceneGraph scene, SceneObject selected) => _inspectorPanel.Duplicate(scene, selected);
    private void Delete(SceneGraph scene, Guid objectId) => _inspectorPanel.Delete(scene, objectId);
    private void RunStructureChange(SceneGraph scene, Action action) => _inspectorPanel.RunStructureChange(scene, action);
    private static Transform SceneTransformCopy(Transform source) => InspectorPanel.SceneTransformCopy(source);
    private static bool TransformsEqual(Transform first, Transform second) => InspectorPanel.TransformsEqual(first, second);

    private static NumericsVector3 ToEulerDegrees(Microsoft.Xna.Framework.Quaternion rotation)
    {
        var sinPitch = 2f * (rotation.W * rotation.X - rotation.Y * rotation.Z);
        var pitch = MathF.Abs(sinPitch) >= 1f
            ? MathF.CopySign(MathF.PI / 2f, sinPitch)
            : MathF.Asin(sinPitch);
        var yaw = MathF.Atan2(
            2f * (rotation.W * rotation.Y + rotation.X * rotation.Z),
            1f - 2f * (rotation.X * rotation.X + rotation.Y * rotation.Y));
        var roll = MathF.Atan2(
            2f * (rotation.W * rotation.Z + rotation.X * rotation.Y),
            1f - 2f * (rotation.X * rotation.X + rotation.Z * rotation.Z));
        var degrees = 180f / MathF.PI;
        return new NumericsVector3(pitch * degrees, yaw * degrees, roll * degrees);
    }

    private static bool IsFinite(NumericsVector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderer.Dispose();
        foreach (var extension in _toolExtensions.OfType<IDisposable>())
        {
            try { extension.Dispose(); }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Optional editor module cleanup failed: {exception}");
            }
        }
        ImGui.DestroyContext(_context);
    }
    private sealed class InspectorPanel
    {
        private readonly CharacterStudioEditorUi _owner;

        public InspectorPanel(CharacterStudioEditorUi owner) =>
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    public void DrawTransformToolActions(SceneGraph scene, SceneObject selected)
    {
        switch (_owner._transformTool)
        {
            case TransformTool.Move:
                ImGui.TextWrapped("Drag a colored axis or move by 25 scene units.");
                DrawTransformActionPair("X -", "X +",
                    () => NudgePosition(scene, selected, new Microsoft.Xna.Framework.Vector3(-25f, 0f, 0f), "X"),
                    () => NudgePosition(scene, selected, new Microsoft.Xna.Framework.Vector3(25f, 0f, 0f), "X"));
                DrawTransformActionPair("Y -", "Y +",
                    () => NudgePosition(scene, selected, new Microsoft.Xna.Framework.Vector3(0f, -25f, 0f), "Y"),
                    () => NudgePosition(scene, selected, new Microsoft.Xna.Framework.Vector3(0f, 25f, 0f), "Y"));
                DrawTransformActionPair("Z -", "Z +",
                    () => NudgePosition(scene, selected, new Microsoft.Xna.Framework.Vector3(0f, 0f, -25f), "Z"),
                    () => NudgePosition(scene, selected, new Microsoft.Xna.Framework.Vector3(0f, 0f, 25f), "Z"));
                DrawMoveSnapControls();
                break;
            case TransformTool.Turn:
                ImGui.TextWrapped("Drag a ring or turn 15 degrees around a local axis.");
                DrawTransformActionPair("X -", "X +",
                    () => Turn(scene, selected, Microsoft.Xna.Framework.Vector3.UnitX, -15f, "X"),
                    () => Turn(scene, selected, Microsoft.Xna.Framework.Vector3.UnitX, 15f, "X"));
                DrawTransformActionPair("Y -", "Y +",
                    () => Turn(scene, selected, Microsoft.Xna.Framework.Vector3.UnitY, -15f, "Y"),
                    () => Turn(scene, selected, Microsoft.Xna.Framework.Vector3.UnitY, 15f, "Y"));
                DrawTransformActionPair("Z -", "Z +",
                    () => Turn(scene, selected, Microsoft.Xna.Framework.Vector3.UnitZ, -15f, "Z"),
                    () => Turn(scene, selected, Microsoft.Xna.Framework.Vector3.UnitZ, 15f, "Z"));
                DrawTurnSnapControls();
                break;
            case TransformTool.Size:
                ImGui.TextWrapped("Drag a colored axis or change size by 10%.");
                DrawTransformActionPair("Smaller", "Larger",
                    () => Resize(scene, selected, 0.9f),
                    () => Resize(scene, selected, 1.1f));
                DrawSizeSnapControls();
                break;
        }
    }

    private void DrawMoveSnapControls()
    {
        ImGui.Separator();
        ImGui.Checkbox("Snap Move to grid", ref _owner._snapMoveToGrid);
        if (_owner._snapMoveToGrid)
        {
            var gridStep = _owner._moveGridStep;
            ImGui.SetNextItemWidth(110f);
            if (ImGui.InputFloat("Grid step", ref gridStep, 0.5f, 5f, "%.2f"))
                _owner._moveGridStep = Math.Clamp(float.IsFinite(gridStep) ? gridStep : _owner._moveGridStep,
                    0.1f, 1_000f);
            ImGui.TextDisabled("World units");
        }
    }

    private void DrawTurnSnapControls()
    {
        ImGui.Separator();
        ImGui.Checkbox("Snap Turn", ref _owner._snapTurnToStep);
        if (!_owner._snapTurnToStep) return;
        var step = _owner._turnSnapDegrees;
        ImGui.SetNextItemWidth(110f);
        if (ImGui.InputFloat("Angle step", ref step, 1f, 5f, "%.1f"))
            _owner._turnSnapDegrees = Math.Clamp(float.IsFinite(step) ? step : _owner._turnSnapDegrees, 0.1f, 180f);
        ImGui.TextDisabled("Degrees");
    }

    private void DrawSizeSnapControls()
    {
        ImGui.Separator();
        ImGui.Checkbox("Snap Size", ref _owner._snapSizeToStep);
        if (!_owner._snapSizeToStep) return;
        var step = _owner._sizeSnapStep;
        ImGui.SetNextItemWidth(110f);
        if (ImGui.InputFloat("Scale step", ref step, 0.05f, 0.25f, "%.2f"))
            _owner._sizeSnapStep = Math.Clamp(float.IsFinite(step) ? step : _owner._sizeSnapStep, 0.01f, 10f);
        ImGui.TextDisabled("Scale multiplier");
    }

    private static void DrawTransformActionPair(string firstLabel, string secondLabel,
        Action firstAction, Action secondAction)
    {
        if (ImGui.Button(firstLabel, new NumericsVector2(92f, 30f))) firstAction();
        ImGui.SameLine();
        if (ImGui.Button(secondLabel, new NumericsVector2(92f, 30f))) secondAction();
    }

    private void NudgePosition(SceneGraph scene, SceneObject selected,
        Microsoft.Xna.Framework.Vector3 offset, string axis)
    {
        ApplyTransformEdit(scene, selected, transform => transform.Position += offset,
            $"Moved {selected.Name} along {axis}.");
    }

    private void Turn(SceneGraph scene, SceneObject selected,
        Microsoft.Xna.Framework.Vector3 axis, float degrees, string axisName)
    {
        var radians = degrees * (MathF.PI / 180f);
        ApplyTransformEdit(scene, selected, transform =>
        {
            var delta = Microsoft.Xna.Framework.Quaternion.CreateFromAxisAngle(axis, radians);
            transform.Rotation = Microsoft.Xna.Framework.Quaternion.Normalize(transform.Rotation * delta);
        }, $"Turned {selected.Name} {degrees:+#;-#;0} degrees around {axisName}.");
    }

    private void Resize(SceneGraph scene, SceneObject selected, float factor)
    {
        ApplyTransformEdit(scene, selected, transform =>
        {
            transform.Scale = new Microsoft.Xna.Framework.Vector3(
                ResizeAxis(transform.Scale.X, factor),
                ResizeAxis(transform.Scale.Y, factor),
                ResizeAxis(transform.Scale.Z, factor));
        }, factor < 1f ? $"Made {selected.Name} 10% smaller." : $"Made {selected.Name} 10% larger.");
    }

    private static float ResizeAxis(float value, float factor)
    {
        if (!float.IsFinite(value)) return value;
        var magnitude = Math.Clamp(MathF.Abs(value) * factor, 0.05f, 1_000f);
        return MathF.CopySign(magnitude, value == 0f ? 1f : value);
    }

    private void ApplyTransformEdit(SceneGraph scene, SceneObject selected,
        Action<Transform> applyChange, string status)
    {
        CommitActiveTransformEdit(scene);
        var before = SceneTransformCopy(selected.Transform);
        applyChange(selected.Transform);
        var after = SceneTransformCopy(selected.Transform);
        if (TransformsEqual(before, after)) return;

        selected.Transform = SceneTransformCopy(before);
        _owner._history.Execute(scene, new TransformEditCommand(selected.Id, before, after));
        _owner._projectWorkspaceStatus = status;
    }

    public void DrawSequencePanel()
    {
        var sequence = _owner._getSequenceInfo();
        if (sequence is null) return;

        ImGui.SetNextWindowPos(new NumericsVector2(232f, 60f));
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Min(450f, _owner._logicalWidth * 0.5f), 150f));
        if (!ImGui.Begin("Animate and Finish", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        ImGui.Text(sequence.Name);
        if (ImGui.Button(sequence.IsPlaying ? "Pause" : "Play"))
            _owner._setSequencePlaying(!sequence.IsPlaying);
        ImGui.SameLine();
        var previewEnabled = sequence.PreviewEnabled;
        if (ImGui.Checkbox("Preview sequence", ref previewEnabled))
            _owner._setSequencePreviewEnabled(previewEnabled);

        var time = Math.Clamp(sequence.Time, 0f, sequence.Duration);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat("Time (seconds)", ref time, 0f, sequence.Duration, "%.2f"))
            _owner._seekSequence(time);
        ImGui.TextDisabled(sequence.CameraName is null
            ? "No camera cut at this time"
            : $"Camera cut: {sequence.CameraName}");
        ImGui.End();

        DrawSequenceExportPanel(sequence);
    }

    private void DrawSequenceExportPanel(SequenceEditorInfo sequence)
    {
        var export = _owner._getSequenceExportInfo();
        if (!_owner._sequenceExportEndTimeInitialized)
        {
            _owner._sequenceExportEndTime = sequence.Duration;
            _owner._sequenceExportEndTimeInitialized = true;
        }

        ImGui.SetNextWindowPos(new NumericsVector2(232f, 218f));
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Min(450f, _owner._logicalWidth * 0.5f), 330f));
        if (!ImGui.Begin("Finish film", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize))
        {
            ImGui.End();
            return;
        }

        if (export.IsRunning)
        {
            ImGui.Text($"Exporting: {export.CompletedFrames} / {export.TotalFrames} frames");
            var progress = export.TotalFrames > 0
                ? Math.Clamp((float)export.CompletedFrames / export.TotalFrames, 0f, 1f)
                : 0f;
            ImGui.ProgressBar(progress, new NumericsVector2(-1f, 0f));
            if (ImGui.Button("Cancel export")) _owner._cancelSequenceExport();
        }
        else
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("Output folder", "Choose an empty folder", ref _owner._sequenceExportDirectory, 1024);
            ImGui.InputInt("Width", ref _owner._sequenceExportWidth);
            ImGui.InputInt("Height", ref _owner._sequenceExportHeight);
            ImGui.InputInt("Frames per second", ref _owner._sequenceExportFrameRate);
            ImGui.InputFloat("Start time", ref _owner._sequenceExportStartTime, 0f, 0f, "%.2f");
            ImGui.InputFloat("End time", ref _owner._sequenceExportEndTime, 0f, 0f, "%.2f");
            if (ImGui.Button("Export PNG frames"))
            {
                _owner._startSequenceExport(new SequenceExportEditorRequest(_owner._sequenceExportDirectory,
                    _owner._sequenceExportStartTime, _owner._sequenceExportEndTime,
                    _owner._sequenceExportFrameRate, _owner._sequenceExportWidth, _owner._sequenceExportHeight));
            }
        }

        ImGui.Text($"Status: {export.Status}");
        if (!string.IsNullOrWhiteSpace(export.OutputDirectory))
            ImGui.TextWrapped(export.OutputDirectory);
        if (!string.IsNullOrWhiteSpace(export.Error))
            ImGui.TextWrapped(export.Error);
        ImGui.End();
    }

    public void DrawCharacterControls(SceneObject selected)
    {
        if (_owner._activeCharacterTimeObjectId is { } editingObjectId && editingObjectId != selected.Id)
            CommitActiveCharacterTimeEdit();

        var character = _owner._getCharacterInfo(selected.Id);
        ImGui.Separator();
        ImGui.Text("Character animation");
        if (character is null || character.ClipNames.Count == 0)
        {
            ImGui.TextDisabled("Select a character with imported clips.");
            return;
        }

        var currentClip = character.ClipName ?? "Select clip";
        if (ImGui.BeginCombo("Clip", currentClip))
        {
            foreach (var clipName in character.ClipNames)
            {
                var isSelected = string.Equals(character.ClipName, clipName, StringComparison.OrdinalIgnoreCase);
                if (ImGui.Selectable(clipName, isSelected)) _owner._selectCharacterClip(selected.Id, clipName);
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (character.Duration > 0f)
        {
            var time = Math.Clamp(character.Time, 0f, character.Duration);
            ImGui.SetNextItemWidth(-1f);
            var changed = ImGui.SliderFloat("Time (seconds)", ref time, 0f, character.Duration, "%.2f");
            if (ImGui.IsItemActivated())
            {
                _owner._activeCharacterTimeObjectId = selected.Id;
                _owner._activeCharacterTimeStart = character.Time;
                _owner._activeCharacterTimeCurrent = character.Time;
            }
            if (changed)
            {
                if (_owner._activeCharacterTimeObjectId != selected.Id)
                {
                    _owner._activeCharacterTimeObjectId = selected.Id;
                    _owner._activeCharacterTimeStart = character.Time;
                }
                _owner._activeCharacterTimeCurrent = time;
                _owner._seekCharacter(selected.Id, time);
            }
            if (ImGui.IsItemDeactivatedAfterEdit()) CommitActiveCharacterTimeEdit();
        }

        var playing = character.IsPlaying;
        if (ImGui.Checkbox("Preview playing", ref playing)) _owner._setCharacterPlaying(selected.Id, playing);
    }

    public void DrawLightingControls()
    {
        ImGui.Separator();
        if (!ImGui.TreeNode("Scene lighting")) return;

        var ambient = new NumericsVector3(_owner._lighting.AmbientColor.X, _owner._lighting.AmbientColor.Y, _owner._lighting.AmbientColor.Z);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat3("Ambient RGB", ref ambient, 0f, 1.5f))
            _owner._lighting.AmbientColor = new Microsoft.Xna.Framework.Vector3(ambient.X, ambient.Y, ambient.Z);

        var direction = new NumericsVector3(
            _owner._lighting.DirectionalDirection.X, _owner._lighting.DirectionalDirection.Y, _owner._lighting.DirectionalDirection.Z);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat3("Direction", ref direction, -1f, 1f)
            && direction.LengthSquared() > 0.0001f)
            _owner._lighting.DirectionalDirection = new Microsoft.Xna.Framework.Vector3(
                direction.X, direction.Y, direction.Z);

        var directional = new NumericsVector3(
            _owner._lighting.DirectionalColor.X, _owner._lighting.DirectionalColor.Y, _owner._lighting.DirectionalColor.Z);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat3("Sun RGB", ref directional, 0f, 1.5f))
            _owner._lighting.DirectionalColor = new Microsoft.Xna.Framework.Vector3(
                directional.X, directional.Y, directional.Z);
        ImGui.TreePop();
    }

    public void TrackTransformInput(SceneGraph scene, Guid objectId, Transform transform,
        bool changed, Action applyChange)
    {
        if (ImGui.IsItemActivated())
        {
            _owner._activeTransformObjectId = objectId;
            _owner._activeTransformStart = SceneTransformCopy(transform);
        }

        if (changed) applyChange();
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitActiveTransformEdit(scene);
    }

    public void CommitActiveTransformEdit(SceneGraph scene)
    {
        CommitActiveBoxColliderEdit(scene);
        CommitActiveCharacterTimeEdit();
        if (_owner._activeTransformObjectId is not { } objectId || _owner._activeTransformStart is not { } before)
        {
            _owner._activeTransformObjectId = null;
            _owner._activeTransformStart = null;
            return;
        }

        _owner._activeTransformObjectId = null;
        _owner._activeTransformStart = null;
        if (scene.Find(objectId) is not { } item) return;
        var after = SceneTransformCopy(item.Transform);
        if (TransformsEqual(before, after)) return;
        item.Transform = SceneTransformCopy(before);
        _owner._history.Execute(scene, new TransformEditCommand(objectId, before, after));
    }

    public void DrawBoxColliderControls(SceneGraph scene, SceneObject selected)
    {
        ImGui.Separator();
        if (!ImGui.TreeNode("Box collider")) return;

        if (selected.BoxCollider is not { } collider)
        {
            ImGui.TextWrapped("A box collider lets this object block a character in Play mode.");
            if (ImGui.Button("Add box collider", new NumericsVector2(-1f, 30f)))
            {
                CommitActiveBoxColliderEdit(scene);
                var replacement = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One);
                _owner._history.Execute(scene, new EditBoxColliderCommand(selected.Id, null, replacement));
                _owner._projectWorkspaceStatus = $"Added a box collider to {selected.Name}.";
            }
            ImGui.TreePop();
            return;
        }

        ImGui.TextWrapped("The center and size are measured in this object's local space.");
        var center = new NumericsVector3(collider.Center.X, collider.Center.Y, collider.Center.Z);
        ImGui.SetNextItemWidth(-1f);
        var centerChanged = ImGui.InputFloat3("Center", ref center);
        SceneBoxColliderComponent? centerReplacement = null;
        if (centerChanged && IsFinite(center))
            centerReplacement = new SceneBoxColliderComponent(
                new Vector3(center.X, center.Y, center.Z), collider.Size, collider.IsTrigger);
        TrackBoxColliderInput(scene, selected, collider, centerChanged, centerReplacement);

        collider = selected.BoxCollider ?? collider;
        var size = new NumericsVector3(collider.Size.X, collider.Size.Y, collider.Size.Z);
        ImGui.SetNextItemWidth(-1f);
        var sizeChanged = ImGui.InputFloat3("Size", ref size);
        SceneBoxColliderComponent? sizeReplacement = null;
        if (sizeChanged && IsFinite(size))
            sizeReplacement = new SceneBoxColliderComponent(
                collider.Center,
                new Vector3(MathF.Max(0.01f, size.X), MathF.Max(0.01f, size.Y), MathF.Max(0.01f, size.Z)),
                collider.IsTrigger);
        TrackBoxColliderInput(scene, selected, collider, sizeChanged, sizeReplacement);

        collider = selected.BoxCollider ?? collider;
        var isTrigger = collider.IsTrigger;
        var canChangeTriggerMode = selected.TriggerAction is null;
        if (!canChangeTriggerMode) ImGui.BeginDisabled();
        if (ImGui.Checkbox("Use as trigger", ref isTrigger))
        {
            var replacement = new SceneBoxColliderComponent(collider.Center, collider.Size, isTrigger);
            TrackBoxColliderInput(scene, selected, collider, changed: true, replacement);
        }
        if (!canChangeTriggerMode) ImGui.EndDisabled();
        if (!canChangeTriggerMode)
            ImGui.TextWrapped("Remove the saved action before changing this trigger back into a blocking collider.");
        if (isTrigger)
            ImGui.TextWrapped("Trigger volumes detect overlap in Play mode without blocking movement.");

        if (selected.TriggerAction is not null) ImGui.BeginDisabled();
        if (ImGui.Button("Remove box collider", new NumericsVector2(-1f, 30f)))
        {
            CommitActiveBoxColliderEdit(scene);
            _owner._history.Execute(scene, new EditBoxColliderCommand(selected.Id, selected.BoxCollider, null));
            _owner._projectWorkspaceStatus = $"Removed the box collider from {selected.Name}.";
        }
        if (selected.TriggerAction is not null)
        {
            ImGui.EndDisabled();
            ImGui.TextWrapped("Remove the saved action before removing this collider.");
        }
        ImGui.TreePop();
    }

    public void DrawWhatHappensControls(SceneGraph scene, SceneObject selected)
    {
        ImGui.Separator();
        if (!ImGui.TreeNodeEx("What happens?", ImGuiTreeNodeFlags.DefaultOpen)) return;

        ImGui.TextWrapped("Choose what this object does when a character enters its trigger.");
        if (_owner._isPlaying())
        {
            ImGui.TextWrapped("Stop Play to change saved scene actions.");
            ImGui.TreePop();
            return;
        }

        var collider = selected.BoxCollider;
        if (collider is null || !collider.IsTrigger)
        {
            ImGui.TextWrapped("Add a trigger box collider first. It detects overlap without blocking movement.");
            if (collider is null)
            {
                if (ImGui.Button("Add trigger collider", new NumericsVector2(-1f, 30f)))
                {
                    CommitActiveBoxColliderEdit(scene);
                    var trigger = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One, isTrigger: true);
                    _owner._history.Execute(scene, new EditBoxColliderCommand(selected.Id, null, trigger));
                    _owner._projectWorkspaceStatus = $"Added a trigger collider to {selected.Name}.";
                }
            }
            else
            {
                if (ImGui.Button("Use box as trigger", new NumericsVector2(-1f, 30f)))
                {
                    CommitActiveBoxColliderEdit(scene);
                    var trigger = new SceneBoxColliderComponent(collider.Center, collider.Size, isTrigger: true);
                    _owner._history.Execute(scene, new EditBoxColliderCommand(selected.Id, collider, trigger));
                    _owner._projectWorkspaceStatus = $"Changed {selected.Name}'s box collider to a trigger.";
                }
            }
            ImGui.TreePop();
            return;
        }

        var currentKind = selected.TriggerAction?.Kind;
        var currentLabel = currentKind switch
        {
            SceneTriggerActionKind.Collect => "Collect",
            SceneTriggerActionKind.ReachGoal => "Reach goal",
            null => "Nothing yet",
            _ => "Unsupported action"
        };
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("On trigger enter", currentLabel))
        {
            DrawTriggerActionOption(scene, selected, currentKind, null, "Nothing yet");
            DrawTriggerActionOption(scene, selected, currentKind,
                SceneTriggerActionKind.Collect, "Collect");
            DrawTriggerActionOption(scene, selected, currentKind,
                SceneTriggerActionKind.ReachGoal, "Reach goal");
            ImGui.EndCombo();
        }

        if (currentKind == SceneTriggerActionKind.Collect)
            ImGui.TextWrapped("On entry, this object is hidden for the rest of Play. The saved scene stays unchanged.");
        else if (currentKind == SceneTriggerActionKind.ReachGoal)
            ImGui.TextWrapped("On entry, Play marks this goal complete once. The saved scene stays unchanged.");
        else
            ImGui.TextWrapped("Choose an action, or leave this set to Nothing yet.");
        ImGui.TreePop();
    }

    private void DrawTriggerActionOption(SceneGraph scene, SceneObject selected,
        SceneTriggerActionKind? currentKind, SceneTriggerActionKind? nextKind, string label)
    {
        if (!ImGui.Selectable(label, currentKind == nextKind)) return;
        if (currentKind == nextKind) return;
        CommitActiveBoxColliderEdit(scene);
        var before = selected.TriggerAction;
        var after = nextKind is { } kind ? new SceneTriggerActionComponent(kind) : null;
        _owner._history.Execute(scene, new EditSceneTriggerActionCommand(selected.Id, before, after));
        _owner._projectWorkspaceStatus = after is null
            ? $"Removed the saved trigger action from {selected.Name}."
            : $"Set {selected.Name} to {label.ToLowerInvariant()} when its trigger is entered.";
    }

    private void TrackBoxColliderInput(SceneGraph scene, SceneObject selected,
        SceneBoxColliderComponent beforeInput, bool changed, SceneBoxColliderComponent? replacement)
    {
        if (ImGui.IsItemActivated() || (changed && _owner._activeBoxColliderObjectId != selected.Id))
        {
            if (_owner._activeBoxColliderObjectId is not null && _owner._activeBoxColliderObjectId != selected.Id)
                CommitActiveBoxColliderEdit(scene);
            _owner._activeBoxColliderObjectId = selected.Id;
            _owner._activeBoxColliderStart = beforeInput;
        }

        if (changed && replacement is not null) selected.BoxCollider = replacement;
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitActiveBoxColliderEdit(scene);
    }

    private void CommitActiveBoxColliderEdit(SceneGraph scene)
    {
        if (_owner._activeBoxColliderObjectId is not { } objectId
            || _owner._activeBoxColliderStart is not { } before)
        {
            _owner._activeBoxColliderObjectId = null;
            _owner._activeBoxColliderStart = null;
            return;
        }

        _owner._activeBoxColliderObjectId = null;
        _owner._activeBoxColliderStart = null;
        if (scene.Find(objectId) is not { } item) return;
        var after = item.BoxCollider;
        if (BoxCollidersEqual(before, after)) return;
        item.BoxCollider = before;
        _owner._history.Execute(scene, new EditBoxColliderCommand(objectId, before, after));
        _owner._projectWorkspaceStatus = $"Updated the box collider on {item.Name}.";
    }

    private static bool BoxCollidersEqual(SceneBoxColliderComponent? left,
        SceneBoxColliderComponent? right) => left is null ? right is null
        : right is not null && left.Center == right.Center && left.Size == right.Size
            && left.IsTrigger == right.IsTrigger;

    private void CommitActiveCharacterTimeEdit()
    {
        if (_owner._activeCharacterTimeObjectId is not { } objectId) return;
        var before = _owner._activeCharacterTimeStart;
        var after = _owner._activeCharacterTimeCurrent;
        _owner._activeCharacterTimeObjectId = null;
        _owner._activeCharacterTimeStart = 0f;
        _owner._activeCharacterTimeCurrent = 0f;
        if (MathF.Abs(before - after) > 0.0001f)
            _owner._commitCharacterTimeEdit(objectId, before, after);
    }

    public void RunHistoryAction(SceneGraph scene, bool undo)
    {
        CommitActiveTransformEdit(scene);
        var changed = undo ? _owner._history.Undo(scene) : _owner._history.Redo(scene);
        if (undo && changed && _owner._firstCreationLesson is { } lesson
            && CharacterStudioEditorUi.IsSamePath(lesson.ProjectFilePath, _owner._getCurrentProjectPath()))
            lesson.ObserveUndo(scene, undoSucceeded: true);
        _owner._afterStructureChange();
        _owner._projectWorkspaceStatus = undo ? "Undid the last change." : "Redid the last change.";
        if (_owner._selectedObjectId is { } selectedId && scene.Find(selectedId) is null)
            _owner._selectedObjectId = scene.Objects.FirstOrDefault()?.Id;
    }

    public void CreateEmpty(SceneGraph scene)
    {
        CommitActiveTransformEdit(scene);
        var item = new SceneObject(Guid.NewGuid(), UniqueName("New Object", scene.Objects.Select(value => value.Name)));
        RunStructureChange(scene, () => _owner._history.Execute(scene, new CreateSceneObjectCommand(item)));
        _owner._selectedObjectId = item.Id;
        _owner._projectWorkspaceStatus = $"Added {item.Name}. Select it to change its position, rotation or size in the Inspector.";
    }

    public void Duplicate(SceneGraph scene, SceneObject selected)
    {
        CommitActiveTransformEdit(scene);
        var duplicate = SceneObjectDuplicator.CreateDuplicate(scene, selected.Id);
        _owner._history.Execute(scene, new CreateSceneObjectCommand(duplicate));
        _owner._afterStructureChange();
        _owner._selectedObjectId = duplicate.Id;
    }

    public void Delete(SceneGraph scene, Guid objectId)
    {
        CommitActiveTransformEdit(scene);
        RunStructureChange(scene, () => _owner._history.Execute(scene, new DeleteSceneObjectCommand(objectId)));
        _owner._selectedObjectId = scene.Objects.FirstOrDefault()?.Id;
    }

    public void RunStructureChange(SceneGraph scene, Action action)
    {
        action();
        _owner._afterStructureChange();
    }

    private static string UniqueName(string basis, IEnumerable<string> existingNames)
    {
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(basis)) return basis;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{basis} {suffix}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    public static Transform SceneTransformCopy(Transform source) => new()
    {
        Position = source.Position,
        Rotation = source.Rotation,
        Scale = source.Scale
    };

    public static bool TransformsEqual(Transform first, Transform second) =>
        first.Position == second.Position && first.Rotation == second.Rotation && first.Scale == second.Scale;


    }
}
