using Ember.Scene;
using Ember.World;
using ImGuiNET;
using Microsoft.Xna.Framework.Input;
using System;
using System.Linq;
using NumericsVector4 = System.Numerics.Vector4;

namespace Ember.Editor;

internal sealed partial class CharacterStudioEditorUi
{
    private readonly PlaySettingsPanel _playSettingsPanel;

    private void DrawPlaySettingsControls(SceneGraph scene) => _playSettingsPanel.DrawPlaySettingsControls(scene);
    private void CommitActivePlaySettingsEdit(SceneGraph scene) => _playSettingsPanel.CommitActivePlaySettingsEdit(scene);

    private sealed class PlaySettingsPanel
    {
        private readonly CharacterStudioEditorUi _owner;

        public PlaySettingsPanel(CharacterStudioEditorUi owner) =>
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private static readonly Keys[] PlayKeyOptions = Enum.GetValues<Keys>()
        .Where(key => key != Keys.None)
        .OrderBy(key => key.ToString(), StringComparer.Ordinal)
        .ToArray();

    private ScenePlaySettings? _activePlaySettingsStart;
    private string? _playSettingsError;

    public void DrawPlaySettingsControls(SceneGraph scene)
    {
        if (!ImGui.TreeNode("Play setup")) return;

        if (_owner._isPlaying())
        {
            ImGui.TextWrapped("Stop Play to change saved movement, camera, or key settings.");
            ImGui.TreePop();
            return;
        }

        var settings = scene.PlaySettings;
        ImGui.TextWrapped("These settings are saved with this scene and used when you press Play.");
        DrawPlayerCharacter(scene, settings);
        settings = scene.PlaySettings;
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

    private void DrawPlayerCharacter(SceneGraph scene, ScenePlaySettings settings)
    {
        var selected = settings.PlayerObjectId is { } playerId ? scene.Find(playerId) : null;
        var currentLabel = selected?.Name
            ?? (settings.PlayerObjectId is { } missingId
                ? $"Missing character ({missingId.ToString("N")[..8]})"
                : "First enabled character (automatic)");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo("Player character", currentLabel))
        {
            if (ImGui.Selectable("First enabled character (automatic)", settings.PlayerObjectId is null))
                ApplyPlaySettingsChoice(scene, scene.PlaySettings with { PlayerObjectId = null });
            foreach (var item in scene.Objects.Where(IsPlayableCharacterCandidate))
            {
                var label = $"{item.Name} · {item.Id.ToString("N")[..8]}";
                var isSelected = settings.PlayerObjectId == item.Id;
                if (ImGui.Selectable(label, isSelected))
                    ApplyPlaySettingsChoice(scene, scene.PlaySettings with { PlayerObjectId = item.Id });
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.TextWrapped("Choose which character receives movement input. Automatic picks the first enabled character by stable scene ID.");
        if (settings.PlayerObjectId is { } missingPlayerId && selected is null)
            ImGui.TextColored(new NumericsVector4(1f, 0.45f, 0.35f, 1f),
                $"Saved player {missingPlayerId} is missing. Choose a character or use automatic selection.");
        else if (selected is not null && !IsPlayableCharacterCandidate(selected))
            ImGui.TextColored(new NumericsVector4(1f, 0.45f, 0.35f, 1f),
                $"'{selected.Name}' is disabled or is no longer a character. Choose another player.");
    }

    private bool IsPlayableCharacterCandidate(SceneObject item) => item.Enabled
        && item.TriggerAction is null
        && (item.CharacterSettings is not null
            || item.WorldEntity?.Kind == WorldEntityKind.Actor
            || _owner._getCharacterInfo(item.Id) is not null);

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
            _owner._history.Execute(scene, new EditScenePlaySettingsCommand(before, replacement));
            _playSettingsError = null;
        }
        catch (ArgumentException exception)
        {
            _playSettingsError = exception.Message;
        }
    }

    public void CommitActivePlaySettingsEdit(SceneGraph scene)
    {
        if (_activePlaySettingsStart is not { } before) return;
        _activePlaySettingsStart = null;
        var after = scene.PlaySettings;
        if (before == after) return;
        scene.PlaySettings = before;
        try
        {
            _owner._history.Execute(scene, new EditScenePlaySettingsCommand(before, after));
            _playSettingsError = null;
        }
        catch (ArgumentException exception)
        {
            _playSettingsError = exception.Message;
        }
    }

    }
}
