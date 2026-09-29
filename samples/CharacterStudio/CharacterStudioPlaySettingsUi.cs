using Ember.Scene;
using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using System;
using System.Linq;
using NumericsVector4 = System.Numerics.Vector4;

namespace CharacterStudio;

internal sealed partial class CharacterStudioEditorUi
{
    private static readonly Keys[] PlayKeyOptions = Enum.GetValues<Keys>()
        .Where(key => key != Keys.None)
        .OrderBy(key => key.ToString(), StringComparer.Ordinal)
        .ToArray();

    private ScenePlaySettings? _activePlaySettingsStart;
    private string? _playSettingsError;

    private void DrawPlaySettingsControls(SceneGraph scene)
    {
        if (!ImGui.TreeNode("Play setup")) return;

        if (_isPlaying())
        {
            ImGui.TextWrapped("Stop Play to change saved movement, camera, or key settings.");
            ImGui.TreePop();
            return;
        }

        var settings = scene.PlaySettings;
        ImGui.TextWrapped("These settings are saved with this scene and used when you press Play.");
        DrawPlaySettingsSlider(scene, "Move speed", settings.MoveSpeed, 0f, 12f,
            value => settings with { MoveSpeed = value }, "%.1f m/s");
        DrawPlaySettingsSlider(scene, "Jump speed", settings.JumpSpeed, 0.5f, 12f,
            value => settings with { JumpSpeed = value }, "%.1f m/s");
        DrawPlaySettingsSlider(scene, "Capsule radius", settings.CapsuleRadius, 0.15f, 1.2f,
            value => settings with { CapsuleRadius = value }, "%.2f m");
        DrawPlaySettingsSlider(scene, "Capsule length", settings.CapsuleCylinderLength, 0f, 2.5f,
            value => settings with { CapsuleCylinderLength = value }, "%.2f m");
        DrawPlaySettingsSlider(scene, "Camera distance", settings.CameraDistance, 0.5f, 20f,
            value => settings with { CameraDistance = value }, "%.1f m");
        DrawPlaySettingsSlider(scene, "Camera target height", settings.CameraTargetOffsetY, -1f, 2f,
            value => settings with { CameraTargetOffsetY = value }, "%.2f m");
        DrawPlaySettingsSlider(scene, "Camera orbit sensitivity", settings.CameraOrbitSensitivity, 0.002f, 0.05f,
            value => settings with { CameraOrbitSensitivity = value }, "%.3f");

        ImGui.Separator();
        ImGui.Text("Keyboard actions");
        settings = scene.PlaySettings;
        DrawPlayKeyBinding(scene, "Move forward", settings.MoveForward, false,
            (value, key) => value with { MoveForward = key });
        settings = scene.PlaySettings;
        DrawPlayKeyBinding(scene, "Forward alternate", settings.MoveForwardAlternate, true,
            (value, key) => value with { MoveForwardAlternate = key });
        settings = scene.PlaySettings;
        DrawPlayKeyBinding(scene, "Move backward", settings.MoveBackward, false,
            (value, key) => value with { MoveBackward = key });
        settings = scene.PlaySettings;
        DrawPlayKeyBinding(scene, "Backward alternate", settings.MoveBackwardAlternate, true,
            (value, key) => value with { MoveBackwardAlternate = key });
        settings = scene.PlaySettings;
        DrawPlayKeyBinding(scene, "Move left", settings.MoveLeft, false,
            (value, key) => value with { MoveLeft = key });
        settings = scene.PlaySettings;
        DrawPlayKeyBinding(scene, "Left alternate", settings.MoveLeftAlternate, true,
            (value, key) => value with { MoveLeftAlternate = key });
        settings = scene.PlaySettings;
        DrawPlayKeyBinding(scene, "Move right", settings.MoveRight, false,
            (value, key) => value with { MoveRight = key });
        settings = scene.PlaySettings;
        DrawPlayKeyBinding(scene, "Right alternate", settings.MoveRightAlternate, true,
            (value, key) => value with { MoveRightAlternate = key });
        settings = scene.PlaySettings;
        DrawPlayKeyBinding(scene, "Jump", settings.Jump, false,
            (value, key) => value with { Jump = key });
        settings = scene.PlaySettings;
        DrawPlayKeyBinding(scene, "Jump alternate", settings.JumpAlternate, true,
            (value, key) => value with { JumpAlternate = key });

        if (_playSettingsError is not null)
            ImGui.TextColored(new NumericsVector4(1f, 0.45f, 0.35f, 1f), _playSettingsError);
        ImGui.TreePop();
    }

    private void DrawPlaySettingsSlider(SceneGraph scene, string label, float value,
        float minimum, float maximum, Func<float, ScenePlaySettings> replace, string format)
    {
        ImGui.SetNextItemWidth(-1f);
        var changed = ImGui.SliderFloat(label, ref value, minimum, maximum, format);
        if (ImGui.IsItemActivated())
        {
            CommitActivePlaySettingsEdit(scene);
            _activePlaySettingsStart = scene.PlaySettings;
        }
        if (changed)
        {
            try { scene.PlaySettings = replace(value); }
            catch (ArgumentException exception) { _playSettingsError = exception.Message; }
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitActivePlaySettingsEdit(scene);
    }

    private void DrawPlayKeyBinding(SceneGraph scene, string label, Keys current,
        bool optional, Func<ScenePlaySettings, Keys, ScenePlaySettings> replace)
    {
        ImGui.SetNextItemWidth(-1f);
        if (!ImGui.BeginCombo(label, current == Keys.None ? "Unbound" : current.ToString())) return;
        if (optional && ImGui.Selectable("Unbound", current == Keys.None))
            ApplyPlaySettingsChoice(scene, replace(scene.PlaySettings, Keys.None));
        foreach (var key in PlayKeyOptions)
        {
            var selected = current == key;
            if (!ImGui.Selectable(key.ToString(), selected)) continue;
            ApplyPlaySettingsChoice(scene, replace(scene.PlaySettings, key));
            if (selected) ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    private void ApplyPlaySettingsChoice(SceneGraph scene, ScenePlaySettings replacement)
    {
        CommitActivePlaySettingsEdit(scene);
        var before = scene.PlaySettings;
        try
        {
            replacement = replacement.ValidatedCopy();
            if (replacement == before) return;
            _history.Execute(scene, new EditScenePlaySettingsCommand(before, replacement));
            _playSettingsError = null;
        }
        catch (ArgumentException exception)
        {
            _playSettingsError = exception.Message;
        }
    }

    private void CommitActivePlaySettingsEdit(SceneGraph scene)
    {
        if (_activePlaySettingsStart is not { } before) return;
        _activePlaySettingsStart = null;
        var after = scene.PlaySettings;
        if (before == after) return;
        scene.PlaySettings = before;
        try
        {
            _history.Execute(scene, new EditScenePlaySettingsCommand(before, after));
            _playSettingsError = null;
        }
        catch (ArgumentException exception)
        {
            _playSettingsError = exception.Message;
        }
    }
}
