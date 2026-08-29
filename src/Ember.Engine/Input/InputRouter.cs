using Microsoft.Xna.Framework.Input;

namespace Ember.Input;

/// <summary>
/// Samples device input once per frame and owns edge detection between frames.
///
/// Screen handlers (and `OverlayInput`) decide what a key means, but they should all observe
/// the same snapshot. Keeping sampling here prevents a handler from seeing a different mouse
/// state than the one used by the frame coordinator.
/// </summary>
public sealed class InputRouter
{
    private KeyboardState _previousKeyboard;
    private MouseState _previousMouse;

    public KeyboardState CurrentKeyboard { get; private set; }
    public MouseState CurrentMouse { get; private set; }

    public void Sample()
    {
        CurrentKeyboard = Keyboard.GetState();
        CurrentMouse = Mouse.GetState();
    }

    public bool Pressed(KeyboardState current, Keys key) =>
        current.IsKeyDown(key) && !_previousKeyboard.IsKeyDown(key);

    /// <summary>
    /// Was this key already down last frame?
    ///
    /// The console types from key transitions -- there is no TextInput path here -- and it
    /// needs to ask about keys it cannot name in advance, which Pressed cannot answer because
    /// it takes one key at a time.
    /// </summary>
    public bool WasDown(Keys key) => _previousKeyboard.IsKeyDown(key);

    public bool Clicked(MouseState current) =>
        current.LeftButton == ButtonState.Pressed && _previousMouse.LeftButton == ButtonState.Released;

    public void CommitKeyboard() => _previousKeyboard = CurrentKeyboard;

    public void CommitMouse() => _previousMouse = CurrentMouse;

    public void Commit()
    {
        CommitKeyboard();
        CommitMouse();
    }
}
