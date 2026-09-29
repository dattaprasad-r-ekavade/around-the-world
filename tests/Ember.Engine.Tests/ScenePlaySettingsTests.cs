using System;
using System.IO;
using Ember.Input;
using Ember.Scene;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class ScenePlaySettingsTests
{
    [Fact]
    public void SavedSettingsRoundTripAndLegacyScenesReceiveTheDefaults()
    {
        var settings = new ScenePlaySettings
        {
            CapsuleRadius = 0.38f,
            CapsuleCylinderLength = 1.1f,
            MoveSpeed = 5.25f,
            JumpSpeed = 7.5f,
            CameraTargetOffsetY = 0.65f,
            CameraDistance = 6.4f,
            CameraOrbitSensitivity = 0.025f,
            MoveForward = Keys.I,
            MoveForwardAlternate = Keys.None,
            MoveBackward = Keys.K,
            MoveBackwardAlternate = Keys.None,
            MoveLeft = Keys.J,
            MoveLeftAlternate = Keys.None,
            MoveRight = Keys.L,
            MoveRightAlternate = Keys.None,
            Jump = Keys.Space,
            JumpAlternate = Keys.None
        };
        var scene = new SceneGraph { PlaySettings = settings };

        var json = SceneFile.ToJson(scene);
        var loaded = SceneFile.FromJson(json);

        Assert.Contains("\"Version\": 17", json, StringComparison.Ordinal);
        Assert.Equal(settings, loaded.PlaySettings);
        Assert.Equal(new ScenePlaySettings(), SceneFile.FromJson("{\"Version\":16,\"Objects\":[]}").PlaySettings);
        Assert.Throws<InvalidDataException>(() => SceneFile.FromJson(
            "{\"Version\":16,\"PlaySettings\":{},\"Objects\":[]}"));
    }

    [Fact]
    public void SavedBindingsDriveNamedInputActions()
    {
        var settings = new ScenePlaySettings
        {
            MoveForward = Keys.I,
            MoveForwardAlternate = Keys.None
        };
        var actions = new InputActionMap(bindDefaultGameplayActions: false);
        settings.ApplyInputBindings(actions);

        var snapshot = actions.Sample(new KeyboardState(Keys.I), windowFocused: true,
            uiCapturesKeyboard: false);

        Assert.Equal(new Vector2(0f, 1f), snapshot.ReadMovement());
        Assert.True(snapshot[GameplayActionNames.MoveForward].WasPressed);
    }

    [Fact]
    public void InvalidValuesAndConflictingBindingsAreRejected()
    {
        var scene = new SceneGraph();

        Assert.Throws<ArgumentOutOfRangeException>(() => scene.PlaySettings =
            new ScenePlaySettings { MoveSpeed = float.NaN });
        Assert.Throws<ArgumentException>(() => scene.PlaySettings =
            new ScenePlaySettings { MoveBackward = Keys.W });
        Assert.Throws<ArgumentOutOfRangeException>(() => scene.PlaySettings =
            new ScenePlaySettings { MoveForward = (Keys)int.MaxValue });
    }

    [Fact]
    public void PlaySettingsEditsUndoRedoAndSurvivePlayCloning()
    {
        var scene = new SceneGraph();
        var before = scene.PlaySettings;
        var after = before with { MoveSpeed = 8f, CameraDistance = 9f };
        var history = new SceneCommandHistory();

        history.Execute(scene, new EditScenePlaySettingsCommand(before, after));
        Assert.Equal(after, scene.PlaySettings);
        Assert.Equal(after, SceneGraphCloner.Clone(scene).PlaySettings);
        Assert.True(history.Undo(scene));
        Assert.Equal(before, scene.PlaySettings);
        Assert.True(history.Redo(scene));
        Assert.Equal(after, scene.PlaySettings);
    }
}
