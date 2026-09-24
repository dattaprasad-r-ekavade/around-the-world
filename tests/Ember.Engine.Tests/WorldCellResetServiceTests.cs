using System;
using System.IO;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldCellResetServiceTests
{
    [Fact]
    public void CellResetClearsOnlyResettableAuthoredAndRuntimeState()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-cell-reset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cellId = Guid.NewGuid();
            var resettableObjectId = Guid.NewGuid();
            var questObjectId = Guid.NewGuid();
            var preservedObjectId = Guid.NewGuid();
            var path = Path.Combine(directory, "cell.json");
            var authored = new SceneGraph();
            authored.Add(new SceneObject(resettableObjectId, "Resettable authored")
            {
                ResetPolicy = WorldInstanceResetPolicy.ResetOnCellReset,
                Transform = new Transform { Position = Vector3.Zero }
            });
            authored.Add(new SceneObject(questObjectId, "Quest memorial")
            {
                ResetPolicy = WorldInstanceResetPolicy.QuestPersistent
            });
            authored.Add(new SceneObject(preservedObjectId, "Default preserved"));
            SceneFile.SaveAtomic(authored, path);

            var activeScene = SceneFile.Load(path);
            var identities = new WorldInstanceIdentityMap();
            var authoredIdentities = identities.CreateCellIdentities(cellId, activeScene);
            var changes = new WorldCellChangeStore();
            changes.SetTransform(cellId, authoredIdentities[resettableObjectId],
                new Transform { Position = new Vector3(20f, 1f, 2f) });
            changes.SetEnabled(cellId, authoredIdentities[resettableObjectId], false);
            changes.MarkDeleted(cellId, authoredIdentities[questObjectId]);
            changes.SetTransform(cellId, authoredIdentities[preservedObjectId],
                new Transform { Position = new Vector3(7f, 0f, 0f) });

            var runtimeObjects = new WorldRuntimeObjectStore();
            var resettableRuntime = runtimeObjects.Spawn(cellId, activeScene, new SceneObject(Guid.NewGuid(), "Resettable drop")
            {
                ResetPolicy = WorldInstanceResetPolicy.ResetOnCellReset
            }, identities);
            var questRuntime = runtimeObjects.Spawn(cellId, activeScene, new SceneObject(Guid.NewGuid(), "Quest item")
            {
                ResetPolicy = WorldInstanceResetPolicy.QuestPersistent
            }, identities);
            var defaultRuntime = runtimeObjects.Spawn(cellId, activeScene, new SceneObject(Guid.NewGuid(), "Default item"), identities);

            var baseline = SceneFile.Load(path);
            Assert.Equal(WorldInstanceResetPolicy.ResetOnCellReset,
                baseline.Find(resettableObjectId)!.ResetPolicy);
            Assert.Equal(WorldInstanceResetPolicy.QuestPersistent, baseline.Find(questObjectId)!.ResetPolicy);
            Assert.Equal(WorldInstanceResetPolicy.Preserve, baseline.Find(preservedObjectId)!.ResetPolicy);
            Assert.Equal(2, WorldCellResetService.ResetCell(cellId, baseline, identities, changes, runtimeObjects));

            Assert.False(changes.IsDeleted(cellId, authoredIdentities[resettableObjectId]));
            Assert.True(changes.IsDeleted(cellId, authoredIdentities[questObjectId]));
            Assert.True(identities.TryGet(cellId, resettableObjectId, out var resettableAuthoredId));
            Assert.Equal(authoredIdentities[resettableObjectId], resettableAuthoredId);
            Assert.False(identities.TryGet(cellId, resettableRuntime.SceneObjectId, out _));
            Assert.Equal(2, runtimeObjects.Count);

            var reloaded = SceneFile.Load(path);
            var reloadedIdentities = identities.CreateCellIdentities(cellId, reloaded);
            Assert.Equal(2, runtimeObjects.Restore(cellId, reloaded, identities));
            changes.Apply(cellId, reloaded, reloadedIdentities);

            Assert.True(reloaded.Find(resettableObjectId)!.Enabled);
            Assert.Equal(Vector3.Zero, reloaded.Find(resettableObjectId)!.Transform.Position);
            Assert.Null(reloaded.Find(questObjectId));
            Assert.Equal(new Vector3(7f, 0f, 0f), reloaded.Find(preservedObjectId)!.Transform.Position);
            Assert.Null(reloaded.Find(resettableRuntime.SceneObjectId));
            Assert.NotNull(reloaded.Find(questRuntime.SceneObjectId));
            Assert.NotNull(reloaded.Find(defaultRuntime.SceneObjectId));
            Assert.Equal(4, reloaded.Objects.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
