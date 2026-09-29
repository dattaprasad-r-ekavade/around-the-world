using System;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;

namespace Ember.Editor;

public sealed partial class CharacterStudioGame
{
    private void StartPlaySession() => _playController.StartPlaySession();
    private void StopPlaySession() => _playController.StopPlaySession();
    private void TriggerInteraction() => _playController.TriggerSelectedInteraction();
    private void SetInteractionVolume(float volume) => _playController.SetInteractionVolume(volume);
    private string StartPathFollow(Guid objectId, CellPathGraph graph, CellPathRoute route) =>
        _playController.StartPathFollow(objectId, graph, route);
    private string? GetPathFollowStatus(Guid objectId) => _playController.GetPathFollowStatus(objectId);
    private void StopPathFollow(Guid objectId) => _playController.StopPathFollow(objectId);
    private void UpdatePathFollowers(float elapsedSeconds) => _playController.UpdatePathFollowers(elapsedSeconds);
    private void DisposePathPhysics(ScenePlaySession? bindingSession = null) =>
        _playController.DisposePhysics(bindingSession);
    private static Vector3 ToOrbitCameraMovement(Vector2 localMovement, float cameraYaw) =>
        CharacterStudioPlayController.ToOrbitCameraMovement(localMovement, cameraYaw);
}
