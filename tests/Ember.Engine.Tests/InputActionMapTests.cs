using Ember.Input;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class InputActionMapTests
{
    [Fact]
    public void NamedMoveAndJumpActionsAreEdgeTriggeredAndRespectUiCapture()
    {
        var actions = new InputActionMap();
        var held = new KeyboardState(Keys.W, Keys.Space);

        var firstFrame = actions.Sample(held, windowFocused: true, uiCapturesKeyboard: false);
        Assert.Equal(new Microsoft.Xna.Framework.Vector2(0f, 1f), firstFrame.ReadMovement());
        Assert.True(firstFrame[GameplayActionNames.Jump].IsDown);
        Assert.True(firstFrame[GameplayActionNames.Jump].WasPressed);
        Assert.True(actions.ConsumePressed(GameplayActionNames.Jump));
        Assert.False(actions.ConsumePressed(GameplayActionNames.Jump));

        var captured = actions.Sample(held, windowFocused: true, uiCapturesKeyboard: true);
        Assert.Equal(Microsoft.Xna.Framework.Vector2.Zero, captured.ReadMovement());
        Assert.False(captured[GameplayActionNames.Jump].IsDown);
        Assert.False(captured[GameplayActionNames.Jump].WasPressed);

        var released = actions.Sample(new KeyboardState(), windowFocused: true, uiCapturesKeyboard: false);
        Assert.False(released[GameplayActionNames.Jump].IsDown);

        var pressedAgain = actions.Sample(held, windowFocused: true, uiCapturesKeyboard: false);
        Assert.True(pressedAgain[GameplayActionNames.Jump].WasPressed);
        var heldAgain = actions.Sample(held, windowFocused: true, uiCapturesKeyboard: false);
        Assert.True(heldAgain[GameplayActionNames.Jump].IsDown);
        Assert.False(heldAgain[GameplayActionNames.Jump].WasPressed);
    }

    [Fact]
    public void KeysHeldAcrossFocusLossStaySuppressedUntilReleased()
    {
        var actions = new InputActionMap();
        var held = new KeyboardState(Keys.W);
        Assert.True(actions.Sample(held, true, false).ReadMovement().Y > 0f);

        Assert.Equal(Microsoft.Xna.Framework.Vector2.Zero, actions.Sample(held, false, false).ReadMovement());
        Assert.Equal(Microsoft.Xna.Framework.Vector2.Zero, actions.Sample(held, true, false).ReadMovement());
        Assert.Equal(Microsoft.Xna.Framework.Vector2.Zero,
            actions.Sample(new KeyboardState(), true, false).ReadMovement());
        Assert.True(actions.Sample(held, true, false).ReadMovement().Y > 0f);
    }

    [Fact]
    public void DiagonalMovementIsNormalizedAndCustomActionsCanBeBound()
    {
        var actions = new InputActionMap(bindDefaultGameplayActions: false);
        actions.Bind("MoveForward", Keys.I);
        actions.Bind("MoveRight", Keys.L);

        var input = actions.Sample(new KeyboardState(Keys.I, Keys.L), true, false);

        Assert.InRange(input.ReadVector2("MoveLeft", "MoveRight", "MoveBackward", "MoveForward").Length(), 0.999f, 1.001f);
        Assert.True(input["MoveForward"].WasPressed);
    }

    [Fact]
    public void JumpPressCanWaitForASimulationStepAndIsConsumedOnlyOnce()
    {
        var actions = new InputActionMap();
        var held = new KeyboardState(Keys.Space);
        actions.Sample(held, true, false);

        var laterRenderFrame = actions.Sample(held, true, false);
        Assert.False(laterRenderFrame[GameplayActionNames.Jump].WasPressed);
        Assert.True(actions.ConsumePressed(GameplayActionNames.Jump));
        Assert.False(actions.ConsumePressed(GameplayActionNames.Jump));
    }
}
