using Ember.Scene;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Linq;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace CharacterStudio;

/// <summary>Immediate-mode scene hierarchy and transform panel for CharacterStudio.</summary>
internal sealed class CharacterStudioEditorUi : IDisposable
{
    private readonly IntPtr _context;
    private readonly ImGuiIOPtr _io;
    private readonly ImGuiMonoGameRenderer _renderer;
    private readonly int _logicalWidth;
    private readonly int _logicalHeight;
    private string _textEntry = string.Empty;
    private Guid? _selectedObjectId;
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

    public CharacterStudioEditorUi(GraphicsDevice device, int logicalWidth, int logicalHeight)
    {
        _logicalWidth = Math.Max(1, logicalWidth);
        _logicalHeight = Math.Max(1, logicalHeight);
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
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##textEntry", "Click here and type", ref _textEntry, 128);
        ImGui.TextDisabled("Orbit pauses while a tool window is active.");
        ImGui.Separator();
        ImGui.Text("Hierarchy");
        ImGui.BeginChild("Scene hierarchy", new NumericsVector2(0f, 185f), ImGuiChildFlags.Borders);
        foreach (var item in scene.Objects)
        {
            var label = $"{item.Name}##{item.Id:N}";
            if (ImGui.Selectable(label, _selectedObjectId == item.Id))
                _selectedObjectId = item.Id;
        }
        ImGui.EndChild();

        if (_selectedObjectId is not { } objectId || scene.Find(objectId) is not { } selected)
        {
            ImGui.TextDisabled("Select an object to edit its local transform.");
            ImGui.End();
            return;
        }

        ImGui.Separator();
        ImGui.Text($"Selected: {selected.Name}");
        var transform = selected.Transform;
        var position = new NumericsVector3(transform.Position.X, transform.Position.Y, transform.Position.Z);
        ImGui.Text("Position");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputFloat3("##position", ref position) && IsFinite(position))
            transform.Position = new Microsoft.Xna.Framework.Vector3(position.X, position.Y, position.Z);

        var euler = ToEulerDegrees(transform.Rotation);
        ImGui.Text("Rotation XYZ (degrees)");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputFloat3("##rotation", ref euler) && IsFinite(euler))
        {
            var radians = MathF.PI / 180f;
            transform.Rotation = Microsoft.Xna.Framework.Quaternion.Normalize(
                Microsoft.Xna.Framework.Quaternion.CreateFromYawPitchRoll(
                    euler.Y * radians, euler.X * radians, euler.Z * radians));
        }

        var scale = new NumericsVector3(transform.Scale.X, transform.Scale.Y, transform.Scale.Z);
        ImGui.Text("Scale");
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputFloat3("##scale", ref scale) && IsFinite(scale))
            transform.Scale = new Microsoft.Xna.Framework.Vector3(scale.X, scale.Y, scale.Z);
        ImGui.End();
    }

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
