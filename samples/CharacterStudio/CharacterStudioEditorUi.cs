using Ember.Scene;
using Ember.Render;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace CharacterStudio;

internal sealed record CharacterEditorInfo(
    IReadOnlyList<string> ClipNames, string? ClipName, float Time, float Duration, bool IsPlaying);

/// <summary>Immediate-mode scene hierarchy and transform panel for CharacterStudio.</summary>
internal sealed class CharacterStudioEditorUi : IDisposable
{
    private readonly IntPtr _context;
    private readonly ImGuiIOPtr _io;
    private readonly ImGuiMonoGameRenderer _renderer;
    private SceneCommandHistory _history;
    private readonly Action _beforeStructureChange;
    private readonly Action _afterStructureChange;
    private readonly Func<Guid, CharacterEditorInfo?> _getCharacterInfo;
    private readonly Action<Guid, string> _selectCharacterClip;
    private readonly Action<Guid, float> _seekCharacter;
    private readonly Action<Guid, bool> _setCharacterPlaying;
    private readonly SceneLighting _lighting;
    private readonly Func<bool> _isPlaying;
    private readonly Action _startPlay;
    private readonly Action _stopPlay;
    private readonly Action _interact;
    private readonly Func<float> _getInteractionVolume;
    private readonly Action<float> _setInteractionVolume;
    private readonly int _logicalWidth;
    private readonly int _logicalHeight;
    private string _textEntry = string.Empty;
    private Guid? _selectedObjectId;
    private Guid? _selectedAssetId;
    private Guid? _activeTransformObjectId;
    private Transform? _activeTransformStart;
    private bool _initialSelectionSet;
    private bool _wantsMouse;
    private bool _wantsKeyboard;
    private int _lastWheel;
    private int _letterboxOffsetX;
    private int _letterboxOffsetY;
    private bool _disposed;

    private static readonly (Keys Source, ImGuiKey Target)[] SpecialKeys =
    [
        (Keys.Tab, ImGuiKey.Tab),
        (Keys.Left, ImGuiKey.LeftArrow), (Keys.Right, ImGuiKey.RightArrow),
        (Keys.Up, ImGuiKey.UpArrow), (Keys.Down, ImGuiKey.DownArrow),
        (Keys.PageUp, ImGuiKey.PageUp), (Keys.PageDown, ImGuiKey.PageDown),
        (Keys.Home, ImGuiKey.Home), (Keys.End, ImGuiKey.End),
        (Keys.Insert, ImGuiKey.Insert), (Keys.Delete, ImGuiKey.Delete),
        (Keys.Back, ImGuiKey.Backspace), (Keys.Space, ImGuiKey.Space),
        (Keys.Enter, ImGuiKey.Enter), (Keys.Escape, ImGuiKey.Escape),
        (Keys.OemComma, ImGuiKey.Comma), (Keys.OemPeriod, ImGuiKey.Period),
        (Keys.OemMinus, ImGuiKey.Minus), (Keys.OemPlus, ImGuiKey.Equal),
        (Keys.OemQuestion, ImGuiKey.Slash), (Keys.OemSemicolon, ImGuiKey.Semicolon),
        (Keys.OemOpenBrackets, ImGuiKey.LeftBracket), (Keys.OemCloseBrackets, ImGuiKey.RightBracket),
        (Keys.OemPipe, ImGuiKey.Backslash), (Keys.OemTilde, ImGuiKey.GraveAccent)
    ];

    public CharacterStudioEditorUi(GraphicsDevice device, int logicalWidth, int logicalHeight,
        SceneCommandHistory history, Action beforeStructureChange, Action afterStructureChange,
        Func<Guid, CharacterEditorInfo?> getCharacterInfo, Action<Guid, string> selectCharacterClip,
        Action<Guid, float> seekCharacter, Action<Guid, bool> setCharacterPlaying, SceneLighting lighting,
        Func<bool> isPlaying, Action startPlay, Action stopPlay, Action interact,
        Func<float> getInteractionVolume, Action<float> setInteractionVolume)
    {
        _logicalWidth = Math.Max(1, logicalWidth);
        _logicalHeight = Math.Max(1, logicalHeight);
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _beforeStructureChange = beforeStructureChange ?? throw new ArgumentNullException(nameof(beforeStructureChange));
        _afterStructureChange = afterStructureChange ?? throw new ArgumentNullException(nameof(afterStructureChange));
        _getCharacterInfo = getCharacterInfo ?? throw new ArgumentNullException(nameof(getCharacterInfo));
        _selectCharacterClip = selectCharacterClip ?? throw new ArgumentNullException(nameof(selectCharacterClip));
        _seekCharacter = seekCharacter ?? throw new ArgumentNullException(nameof(seekCharacter));
        _setCharacterPlaying = setCharacterPlaying ?? throw new ArgumentNullException(nameof(setCharacterPlaying));
        _lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
        _isPlaying = isPlaying ?? throw new ArgumentNullException(nameof(isPlaying));
        _startPlay = startPlay ?? throw new ArgumentNullException(nameof(startPlay));
        _stopPlay = stopPlay ?? throw new ArgumentNullException(nameof(stopPlay));
        _interact = interact ?? throw new ArgumentNullException(nameof(interact));
        _getInteractionVolume = getInteractionVolume ?? throw new ArgumentNullException(nameof(getInteractionVolume));
        _setInteractionVolume = setInteractionVolume ?? throw new ArgumentNullException(nameof(setInteractionVolume));
        _context = ImGui.CreateContext();
        try
        {
            ImGui.SetCurrentContext(_context);
            ImGui.StyleColorsDark();
            _io = ImGui.GetIO();
            unsafe { _io.NativePtr->IniFilename = null; }
            _io.DisplaySize = new NumericsVector2(_logicalWidth, _logicalHeight);
            _renderer = new ImGuiMonoGameRenderer(device);
        }
        catch
        {
            ImGui.DestroyContext(_context);
            throw;
        }
    }

    public bool WantsMouse => _wantsMouse;
    public bool WantsKeyboard => _wantsKeyboard;
    public Guid? SelectedObjectId => _selectedObjectId;

    public void SetHistory(SceneCommandHistory history) =>
        _history = history ?? throw new ArgumentNullException(nameof(history));

    public void CompletePendingEdit(SceneGraph scene) => CommitActiveTransformEdit(scene);

    public void AddTextInput(char character)
    {
        if (_disposed || char.IsControl(character)) return;
        _io.AddInputCharacterUTF16(character);
    }

    public void Update(float elapsedSeconds, KeyboardState keyboard, MouseState mouse,
        Vector2 logicalMouse, SceneGraph scene)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CharacterStudioEditorUi));
        var viewport = _renderer.Viewport;
        _io.DisplaySize = new NumericsVector2(_logicalWidth, _logicalHeight);
        var framebufferScale = MathF.Min(
            viewport.Width / (float)_logicalWidth,
            viewport.Height / (float)_logicalHeight);
        _letterboxOffsetX = (int)MathF.Floor((viewport.Width - _logicalWidth * framebufferScale) * 0.5f);
        _letterboxOffsetY = (int)MathF.Floor((viewport.Height - _logicalHeight * framebufferScale) * 0.5f);
        _io.DisplayFramebufferScale = new NumericsVector2(framebufferScale, framebufferScale);
        _io.DeltaTime = Math.Max(1f / 1000f, elapsedSeconds);
        _io.AddMousePosEvent(logicalMouse.X, logicalMouse.Y);
        _io.AddMouseButtonEvent(0, mouse.LeftButton == ButtonState.Pressed);
        _io.AddMouseButtonEvent(1, mouse.RightButton == ButtonState.Pressed);
        _io.AddMouseButtonEvent(2, mouse.MiddleButton == ButtonState.Pressed);
        var wheel = (mouse.ScrollWheelValue - _lastWheel) / 120f;
        if (wheel != 0f) _io.AddMouseWheelEvent(0f, wheel);
        _lastWheel = mouse.ScrollWheelValue;
        UpdateKeyboard(keyboard);

        ImGui.NewFrame();
        DrawPanel(scene);
        ImGui.Render();
        _wantsMouse = _io.WantCaptureMouse;
        _wantsKeyboard = _io.WantCaptureKeyboard;
    }

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

    private void DrawPanel(SceneGraph scene)
    {
        if (!_initialSelectionSet)
        {
            _selectedObjectId = scene.Objects.FirstOrDefault()?.Id;
            _initialSelectionSet = true;
        }
        else if (_selectedObjectId is { } selectedId && scene.Find(selectedId) is null)
            _selectedObjectId = null;

        ImGui.SetNextWindowPos(new NumericsVector2(16f, 116f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new NumericsVector2(370f, 570f), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Character Studio", ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        ImGui.Text("Scene tools");
        if (_isPlaying())
        {
            ImGui.TextColored(new NumericsVector4(1f, 0.72f, 0.2f, 1f), "PLAYING ON CLONE");
            ImGui.SameLine();
            if (ImGui.Button("Stop and restore"))
            {
                _stopPlay();
                ImGui.End();
                return;
            }
            ImGui.SameLine();
            if (ImGui.Button("Interact")) _interact();
            var volume = _getInteractionVolume();
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("Interaction volume", ref volume, 0f, 1f, "%.2f"))
                _setInteractionVolume(volume);
        }
        else if (ImGui.Button("Play on clone"))
        {
            _startPlay();
            ImGui.End();
            return;
        }
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##textEntry", "Click here and type", ref _textEntry, 128);
        ImGui.TextDisabled("Orbit pauses while a tool window is active.");
        if (!_history.CanUndo) ImGui.BeginDisabled();
        if (ImGui.Button("Undo")) RunHistoryAction(scene, undo: true);
        if (!_history.CanUndo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (!_history.CanRedo) ImGui.BeginDisabled();
        if (ImGui.Button("Redo")) RunHistoryAction(scene, undo: false);
        if (!_history.CanRedo) ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Create Empty")) CreateEmpty(scene);
        ImGui.Separator();
        ImGui.Text("Hierarchy");
        ImGui.BeginChild("Scene hierarchy", new NumericsVector2(0f, 145f), ImGuiChildFlags.Borders);
        foreach (var item in scene.Objects)
        {
            var label = $"{item.Name}##{item.Id:N}";
            if (ImGui.Selectable(label, _selectedObjectId == item.Id))
                _selectedObjectId = item.Id;
        }
        ImGui.EndChild();

        if (_activeTransformObjectId is not null && _selectedObjectId != _activeTransformObjectId)
            CommitActiveTransformEdit(scene);

        var availableAssets = scene.Objects
            .Where(item => item.GltfAsset is not null)
            .Select(item => item.GltfAsset!)
            .GroupBy(asset => asset.AssetId)
            .Select(group => group.First())
            .ToArray();
        if (_selectedAssetId is null || availableAssets.All(asset => asset.AssetId != _selectedAssetId))
            _selectedAssetId = availableAssets.FirstOrDefault()?.AssetId;
        ImGui.Separator();
        ImGui.Text("Assets");
        ImGui.BeginChild("Scene assets", new NumericsVector2(0f, 80f), ImGuiChildFlags.Borders);
        foreach (var asset in availableAssets)
        {
            var label = $"{Path.GetFileName(asset.SourcePath)}##asset-{asset.AssetId:N}";
            if (ImGui.Selectable(label, _selectedAssetId == asset.AssetId))
                _selectedAssetId = asset.AssetId;
        }
        ImGui.EndChild();
        var selectedAsset = availableAssets.FirstOrDefault(asset => asset.AssetId == _selectedAssetId);
        if (selectedAsset is null) ImGui.BeginDisabled();
        if (ImGui.Button("Place instance")) PlaceAsset(scene, selectedAsset!);
        if (selectedAsset is null) ImGui.EndDisabled();

        if (_selectedObjectId is not { } objectId || scene.Find(objectId) is not { } selected)
        {
            ImGui.TextDisabled("Select an object to edit its local transform.");
            ImGui.End();
            return;
        }

        ImGui.Separator();
        ImGui.Text($"Selected: {selected.Name}");
        if (ImGui.Button("Duplicate")) Duplicate(scene, selected);
        ImGui.SameLine();
        if (ImGui.Button("Delete")) Delete(scene, selected.Id);
        var transform = selected.Transform;
        var position = new NumericsVector3(transform.Position.X, transform.Position.Y, transform.Position.Z);
        ImGui.Text("Position");
        ImGui.SetNextItemWidth(-1f);
        var positionChanged = ImGui.InputFloat3("##position", ref position);
        TrackTransformInput(scene, selected.Id, transform, positionChanged, () =>
        {
            if (IsFinite(position))
                transform.Position = new Microsoft.Xna.Framework.Vector3(position.X, position.Y, position.Z);
        });

        var euler = ToEulerDegrees(transform.Rotation);
        ImGui.Text("Rotation XYZ (degrees)");
        ImGui.SetNextItemWidth(-1f);
        var rotationChanged = ImGui.InputFloat3("##rotation", ref euler);
        TrackTransformInput(scene, selected.Id, transform, rotationChanged, () =>
        {
            if (IsFinite(euler))
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
        TrackTransformInput(scene, selected.Id, transform, scaleChanged, () =>
        {
            if (IsFinite(scale))
                transform.Scale = new Microsoft.Xna.Framework.Vector3(scale.X, scale.Y, scale.Z);
        });

        DrawCharacterControls(selected);
        DrawLightingControls();
        ImGui.End();
    }

    private void DrawCharacterControls(SceneObject selected)
    {
        var character = _getCharacterInfo(selected.Id);
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
                if (ImGui.Selectable(clipName, isSelected)) _selectCharacterClip(selected.Id, clipName);
                if (isSelected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (character.Duration > 0f)
        {
            var time = Math.Clamp(character.Time, 0f, character.Duration);
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("Time (seconds)", ref time, 0f, character.Duration, "%.2f"))
                _seekCharacter(selected.Id, time);
        }

        var playing = character.IsPlaying;
        if (ImGui.Checkbox("Playing", ref playing)) _setCharacterPlaying(selected.Id, playing);
    }

    private void DrawLightingControls()
    {
        ImGui.Separator();
        if (!ImGui.TreeNode("Scene lighting")) return;

        var ambient = new NumericsVector3(_lighting.AmbientColor.X, _lighting.AmbientColor.Y, _lighting.AmbientColor.Z);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat3("Ambient RGB", ref ambient, 0f, 1.5f))
            _lighting.AmbientColor = new Microsoft.Xna.Framework.Vector3(ambient.X, ambient.Y, ambient.Z);

        var direction = new NumericsVector3(
            _lighting.DirectionalDirection.X, _lighting.DirectionalDirection.Y, _lighting.DirectionalDirection.Z);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat3("Direction", ref direction, -1f, 1f)
            && direction.LengthSquared() > 0.0001f)
            _lighting.DirectionalDirection = new Microsoft.Xna.Framework.Vector3(
                direction.X, direction.Y, direction.Z);

        var directional = new NumericsVector3(
            _lighting.DirectionalColor.X, _lighting.DirectionalColor.Y, _lighting.DirectionalColor.Z);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.SliderFloat3("Sun RGB", ref directional, 0f, 1.5f))
            _lighting.DirectionalColor = new Microsoft.Xna.Framework.Vector3(
                directional.X, directional.Y, directional.Z);
        ImGui.TreePop();
    }

    private void TrackTransformInput(SceneGraph scene, Guid objectId, Transform transform,
        bool changed, Action applyChange)
    {
        if (ImGui.IsItemActivated())
        {
            _activeTransformObjectId = objectId;
            _activeTransformStart = SceneTransformCopy(transform);
        }

        if (changed) applyChange();
        if (ImGui.IsItemDeactivatedAfterEdit()) CommitActiveTransformEdit(scene);
    }

    private void CommitActiveTransformEdit(SceneGraph scene)
    {
        if (_activeTransformObjectId is not { } objectId || _activeTransformStart is not { } before)
        {
            _activeTransformObjectId = null;
            _activeTransformStart = null;
            return;
        }

        _activeTransformObjectId = null;
        _activeTransformStart = null;
        if (scene.Find(objectId) is not { } item) return;
        var after = SceneTransformCopy(item.Transform);
        if (TransformsEqual(before, after)) return;
        item.Transform = SceneTransformCopy(before);
        _history.Execute(scene, new TransformEditCommand(objectId, before, after));
    }

    private void RunHistoryAction(SceneGraph scene, bool undo)
    {
        CommitActiveTransformEdit(scene);
        _beforeStructureChange();
        if (undo) _history.Undo(scene);
        else _history.Redo(scene);
        _afterStructureChange();
        if (_selectedObjectId is { } selectedId && scene.Find(selectedId) is null)
            _selectedObjectId = scene.Objects.FirstOrDefault()?.Id;
    }

    private void CreateEmpty(SceneGraph scene)
    {
        CommitActiveTransformEdit(scene);
        var item = new SceneObject(Guid.NewGuid(), UniqueName("New Object", scene.Objects.Select(value => value.Name)));
        RunStructureChange(scene, () => _history.Execute(scene, new CreateSceneObjectCommand(item)));
        _selectedObjectId = item.Id;
    }

    private void Duplicate(SceneGraph scene, SceneObject selected)
    {
        CommitActiveTransformEdit(scene);
        _beforeStructureChange();
        var duplicate = SceneObjectDuplicator.CreateDuplicate(scene, selected.Id);
        _history.Execute(scene, new CreateSceneObjectCommand(duplicate));
        _afterStructureChange();
        _selectedObjectId = duplicate.Id;
    }

    private void Delete(SceneGraph scene, Guid objectId)
    {
        CommitActiveTransformEdit(scene);
        RunStructureChange(scene, () => _history.Execute(scene, new DeleteSceneObjectCommand(objectId)));
        _selectedObjectId = scene.Objects.FirstOrDefault()?.Id;
    }

    private void PlaceAsset(SceneGraph scene, GltfAssetReference asset)
    {
        CommitActiveTransformEdit(scene);
        var existing = scene.Objects.Where(item => item.GltfAsset?.AssetId == asset.AssetId).ToArray();
        var positionX = existing.Length == 0 ? 0f : existing.Max(item => item.Transform.Position.X) + 100f;
        var item = SceneObjectFactory.CreateAssetInstance(scene, asset,
            new Microsoft.Xna.Framework.Vector3(positionX, 0f, 0f));
        RunStructureChange(scene, () => _history.Execute(scene, new CreateSceneObjectCommand(item)));
        _selectedObjectId = item.Id;
    }

    private void RunStructureChange(SceneGraph scene, Action action)
    {
        _beforeStructureChange();
        action();
        _afterStructureChange();
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

    private static Transform SceneTransformCopy(Transform source) => new()
    {
        Position = source.Position,
        Rotation = source.Rotation,
        Scale = source.Scale
    };

    private static bool TransformsEqual(Transform first, Transform second) =>
        first.Position == second.Position && first.Rotation == second.Rotation && first.Scale == second.Scale;

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
        ImGui.DestroyContext(_context);
    }
}
