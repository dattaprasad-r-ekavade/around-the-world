using Ember;
using Ember.Rpg;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;

namespace RpgSlice;

/// <summary>Drives the same keyboard actions a player uses to complete the authored quest.</summary>
internal sealed class RpgSliceQuestSmoke
{
    private const string NoticeStageId = "read_road_notice";
    private const string RaiderStageId = "defeat_raider";
    private const string SatchelStageId = "recover_apples";
    private bool _releaseNextAction;
    private string? _activeStageId;
    private readonly List<Vector3> _waypoints = new();

    public bool SaveKeyIssued { get; private set; }

    public KeyboardState CreateKeyboard(Vector3 playerPosition, ThirdPersonFollowCamera camera,
        RpgSliceGameplayIntegration gameplay)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(gameplay);
        if (!gameplay.IsInitialized) return new KeyboardState();
        if (_releaseNextAction)
        {
            _releaseNextAction = false;
            return new KeyboardState();
        }

        if (gameplay.HasOpenDialogue)
        {
            if (gameplay.CurrentDialogueOptions.Count == 0)
                throw new InvalidOperationException("Quest smoke reached a dialogue node without a selectable reply.");
            _releaseNextAction = true;
            return new KeyboardState(Keys.D1);
        }

        if (gameplay.LostDeliveryStatus == QuestStatus.Complete)
        {
            if (!SaveKeyIssued)
            {
                SaveKeyIssued = true;
                _releaseNextAction = true;
                return new KeyboardState(Keys.F5);
            }
            return new KeyboardState();
        }

        var stage = gameplay.LostDeliveryStage;
        var stageId = gameplay.LostDeliveryStatus == QuestStatus.NotStarted ? "not_started" : stage?.Id;
        var targetName = gameplay.LostDeliveryStatus == QuestStatus.NotStarted
            ? "keeper"
            : stage?.Id switch
            {
                NoticeStageId => "notice",
                RaiderStageId => "raider",
                SatchelStageId => "satchel",
                _ => throw new InvalidOperationException($"Quest smoke found unsupported quest stage '{stage?.Id}'.")
            };
        if (!gameplay.TryGetQuestSmokeTarget(targetName, out var target))
            throw new InvalidOperationException($"Quest smoke could not resolve its '{targetName}' target.");

        if (_activeStageId != stageId)
        {
            _activeStageId = stageId;
            _waypoints.Clear();
            if (stageId == NoticeStageId)
            {
                // Route around the Stone Market Courtyard and market props:
                _waypoints.Add(new Vector3(16f, playerPosition.Y, 22f));
                _waypoints.Add(new Vector3(16f, playerPosition.Y, 28f));
                _waypoints.Add(new Vector3(31f, playerPosition.Y, 28f));
                _waypoints.Add(new Vector3(31f, playerPosition.Y, 14f));
                _waypoints.Add(target);
            }
            else if (stageId == RaiderStageId)
            {
                // Return from east road to the raider on the market road:
                _waypoints.Add(new Vector3(31f, playerPosition.Y, 14f));
                _waypoints.Add(new Vector3(31f, playerPosition.Y, 23f));
                _waypoints.Add(target);
            }
            else if (stageId == SatchelStageId)
            {
                // Approach the satchel slightly from the east to avoid the raider's box collider:
                _waypoints.Add(new Vector3(28.5f, playerPosition.Y, 25f));
                _waypoints.Add(target);
            }
            else
            {
                _waypoints.Add(target);
            }
        }
        else if (_waypoints.Count == 1 && stageId == RaiderStageId)
        {
            // Keep target position tracking live enemy
            _waypoints[0] = target;
        }

        while (_waypoints.Count > 1)
        {
            var toNext = _waypoints[0] - playerPosition;
            toNext.Y = 0f;
            if (toNext.Length() <= 1.2f)
            {
                _waypoints.RemoveAt(0);
            }
            else
            {
                break;
            }
        }

        var currentTarget = _waypoints.Count > 0 ? _waypoints[0] : target;
        var toTarget = currentTarget - playerPosition;
        toTarget.Y = 0f;
        var distance = toTarget.Length();
        var reach = targetName switch
        {
            "keeper" => 4.2f,
            "notice" => 2.4f,
            "raider" => 1.8f,
            "satchel" => 2.5f,
            _ => 1.8f
        };

        var directToFinal = target - playerPosition;
        directToFinal.Y = 0f;
        if (directToFinal.Length() <= reach)
        {
            _releaseNextAction = true;
            return new KeyboardState(targetName == "raider" ? Keys.F : Keys.E);
        }

        if (distance < 1e-4f) return new KeyboardState();

        var moveDir = toTarget / distance;
        var forward = camera.MoveDirection(new Vector2(0f, 1f));
        var right = camera.MoveDirection(new Vector2(1f, 0f));
        var localRight = Vector3.Dot(moveDir, right);
        var localForward = Vector3.Dot(moveDir, forward);
        var keys = new List<Keys>(2);
        if (MathF.Abs(localRight) > 0.2f) keys.Add(localRight > 0f ? Keys.D : Keys.A);
        if (MathF.Abs(localForward) > 0.2f) keys.Add(localForward > 0f ? Keys.W : Keys.S);
        return new KeyboardState(keys.ToArray());
    }
}
