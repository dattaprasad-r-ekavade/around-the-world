using System;
using System.IO;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldPersistenceSessionTests
{
    [Fact]
    public void CellActivationRestoresEditsAndRuntimeObjectsAfterQueuedSaveAndRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-persistence-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var exteriorId = Guid.NewGuid();
            var interiorId = Guid.NewGuid();
            var markerId = Guid.NewGuid();
            var exteriorPath = Path.Combine(directory, "exterior.json");
            var interiorPath = Path.Combine(directory, "interior.json");
            var manifestPath = Path.Combine(directory, "world.json");
            var savePath = Path.Combine(directory, "world-save.json");
            var authored = new SceneGraph();
            authored.Add(new SceneObject(markerId, "Persistent Marker")
            {
                Transform = new Transform { Position = new Vector3(2f, 1f, 3f) }
            });
            SceneFile.SaveAtomic(authored, exteriorPath);
            SceneFile.SaveAtomic(new SceneGraph(), interiorPath);
            WorldManifest.SaveAtomic(manifestPath, 32f,
            [
                new WorldCellDefinition
                {
                    Id = exteriorId,
                    Kind = WorldCellKind.Exterior,
                    ExteriorCoordinate = new ExteriorCellCoordinate(0, 0),
                    ScenePath = "exterior.json"
                },
                new WorldCellDefinition
                {
                    Id = interiorId,
                    Kind = WorldCellKind.Interior,
                    ScenePath = "interior.json"
                }
            ]);
            var world = WorldManifest.Load(manifestPath);
            var session = new WorldPersistenceSession(world);
            var activeScene = SceneFile.Load(exteriorPath);
            var identities = session.PrepareCell(exteriorId, activeScene);
            var originalIdentity = identities[markerId];
            var marker = activeScene.Find(markerId)!;
            var moved = new Transform
            {
                Position = new Vector3(8f, 1.5f, -4f),
                Rotation = Quaternion.CreateFromYawPitchRoll(0.7f, 0.1f, 0f),
                Scale = new Vector3(1.5f, 1f, 0.75f)
            };
            session.SetTransform(exteriorId, marker, moved);
            session.SetEnabled(exteriorId, marker, false);
            var runtime = session.Spawn(exteriorId, activeScene, new SceneObject(Guid.NewGuid(), "Runtime Gem")
            {
                Transform = new Transform { Position = new Vector3(-3f, 1f, 5f) }
            });

            var committedLocation = new WorldPlayerLocation(exteriorId, new Vector3(10f, 1f, 9f), Quaternion.Identity);
            var pendingLocation = committedLocation;
            session.RequestSave(savePath, () => pendingLocation);
            Assert.False(session.ProcessStableBoundary(travelInProgress: true));
            Assert.Equal(1, session.PendingSaveCount);
            pendingLocation = new WorldPlayerLocation(interiorId, new Vector3(6f, 1f, 4f),
                Quaternion.CreateFromYawPitchRoll(MathHelper.Pi, 0f, 0f));
            Assert.True(session.ProcessStableBoundary(travelInProgress: false));
            Assert.True(session.TryDequeueSaveResult(out var result));
            Assert.Null(result.Failure);

            var saved = WorldSaveFile.Load(savePath, world);
            Assert.Equal(pendingLocation, saved.PlayerLocation);
            var restarted = new WorldPersistenceSession(world, saved);
            var reloadedScene = SceneFile.Load(exteriorPath);
            var reloadedIdentities = restarted.PrepareCell(exteriorId, reloadedScene);
            var restoredMarker = reloadedScene.Find(markerId)!;
            Assert.Equal(originalIdentity, reloadedIdentities[markerId]);
            Assert.False(restoredMarker.Enabled);
            Assert.Equal(moved.Position, restoredMarker.Transform.Position);
            Assert.Equal(moved.Scale, restoredMarker.Transform.Scale);
            Assert.Equal(Quaternion.Normalize(moved.Rotation), restoredMarker.Transform.Rotation);
            Assert.Equal(runtime.InstanceId, reloadedIdentities[runtime.SceneObjectId]);
            Assert.Equal(1, restarted.RuntimeObjects.Count);
            Assert.NotSame(activeScene.Find(runtime.SceneObjectId), reloadedScene.Find(runtime.SceneObjectId));

            var objectCount = reloadedScene.Objects.Count;
            restarted.PrepareCell(exteriorId, reloadedScene);
            Assert.Equal(objectCount, reloadedScene.Objects.Count);
            Assert.Equal(1, restarted.RuntimeObjects.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
