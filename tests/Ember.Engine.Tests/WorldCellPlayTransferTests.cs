using System;
using System.Collections.Generic;
using System.IO;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldCellPlayTransferTests
{
    [Fact]
    public void TransferCarriesPlayerToSpawnAndBackWithoutResettingVisitedCellState()
    {
        var directory = TemporaryDirectory();
        try
        {
            var exteriorId = Guid.NewGuid();
            var interiorId = Guid.NewGuid();
            var exteriorSpawnId = Guid.NewGuid();
            var interiorSpawnId = Guid.NewGuid();
            var originalPlayerId = Guid.NewGuid();
            var oldInteriorPlayerId = Guid.NewGuid();
            var sourcePlayerId = Guid.NewGuid();
            var exteriorScene = new SceneGraph
            {
                PlaySettings = new ScenePlaySettings { PlayerObjectId = originalPlayerId }
            };
            exteriorScene.AddUnparented(new SceneObject(originalPlayerId, "Original player")
            {
                Transform = new Transform { Position = new Vector3(1f, 2f, 3f) }
            });
            exteriorScene.AddUnparented(new SceneObject(Guid.NewGuid(), "Exterior spawn")
            {
                SpawnPoint = new WorldSpawnComponent(exteriorSpawnId),
                Transform = new Transform { Position = new Vector3(10f, 1f, 20f) }
            });

            var interiorScene = new SceneGraph
            {
                PlaySettings = new ScenePlaySettings { PlayerObjectId = oldInteriorPlayerId }
            };
            interiorScene.AddUnparented(new SceneObject(oldInteriorPlayerId, "Old interior player"));
            var oldInteriorChildId = Guid.NewGuid();
            interiorScene.AddUnparented(new SceneObject(oldInteriorChildId, "Old interior player child"));
            interiorScene.SetParent(oldInteriorChildId, oldInteriorPlayerId);
            var collectibleId = Guid.NewGuid();
            interiorScene.AddUnparented(new SceneObject(collectibleId, "Collected item") { Enabled = false });
            interiorScene.AddUnparented(new SceneObject(Guid.NewGuid(), "Interior spawn")
            {
                SpawnPoint = new WorldSpawnComponent(interiorSpawnId),
                Transform = new Transform { Position = new Vector3(4f, 2f, 6f) }
            });

            var exteriorPath = Path.Combine(directory, "exterior.json");
            var interiorPath = Path.Combine(directory, "interior.json");
            SceneFile.SaveAtomic(exteriorScene, exteriorPath);
            SceneFile.SaveAtomic(interiorScene, interiorPath);
            var manifestPath = Path.Combine(directory, WorldManifest.DefaultFileName);
            WorldManifest.SaveAtomic(manifestPath, 32f,
            [
                new WorldCellDefinition
                {
                    Id = exteriorId,
                    Kind = WorldCellKind.Exterior,
                    ExteriorCoordinate = new ExteriorCellCoordinate(0, 0),
                    ScenePath = "exterior.json"
                },
                new WorldCellDefinition { Id = interiorId, Kind = WorldCellKind.Interior, ScenePath = "interior.json" }
            ]);
            var world = WorldManifest.Load(manifestPath);
            var sourceChildId = Guid.NewGuid();
            var templateRootId = Guid.NewGuid();
            var templateChildId = Guid.NewGuid();
            var sourcePlayer = new SceneObject(sourcePlayerId, "Play character")
            {
                Transform = new Transform
                {
                    Position = new Vector3(2f, 3f, 4f),
                    Scale = new Vector3(2f, 3f, 4f)
                },
                Door = new WorldDoorComponent(interiorId, interiorSpawnId, Quaternion.Identity),
                SpawnPoint = new WorldSpawnComponent(Guid.NewGuid()),
                TemplateInstance = new SceneTemplateInstanceComponent(Guid.NewGuid(), 1,
                    templateRootId, sourcePlayerId,
                    [new SceneTemplateObjectMapping(templateRootId, sourcePlayerId),
                        new SceneTemplateObjectMapping(templateChildId, sourceChildId)])
            };
            var sourceRuntimeScene = new SceneGraph
            {
                PlaySettings = new ScenePlaySettings { PlayerObjectId = sourcePlayerId }
            };
            sourceRuntimeScene.AddUnparented(sourcePlayer);
            sourceRuntimeScene.AddUnparented(new SceneObject(sourceChildId, "Character child")
            {
                Transform = new Transform { Position = new Vector3(0f, 1f, 0f) }
            });
            sourceRuntimeScene.SetParent(sourceChildId, sourcePlayerId);
            var scenes = new Dictionary<Guid, SceneGraph> { [interiorId] = SceneFile.Load(interiorPath) };
            var enterDoor = new WorldDoorComponent(interiorId, interiorSpawnId,
                Quaternion.CreateFromYawPitchRoll(MathHelper.PiOver2, 0f, 0f));

            var entered = WorldCellPlayTransferFactory.Prepare(world, scenes, exteriorId,
                sourceRuntimeScene, sourcePlayerId, enterDoor);
            Assert.Equal(interiorId, entered.DestinationCellId);
            Assert.NotEqual(sourcePlayerId, entered.PlayerObjectId);
            Assert.NotEqual(oldInteriorPlayerId, entered.PlayerObjectId);
            Assert.Equal(entered.PlayerObjectId, entered.DestinationScene.PlaySettings.PlayerObjectId);
            Assert.Null(entered.DestinationScene.Find(oldInteriorPlayerId));
            Assert.Null(entered.DestinationScene.Find(oldInteriorChildId));
            Assert.False(entered.DestinationScene.Find(collectibleId)!.Enabled);
            Assert.Equal(new Vector3(4f, 2f, 6f), entered.DestinationScene.Find(entered.PlayerObjectId)!.Transform.Position);
            Assert.Equal(new Vector3(2f, 3f, 4f), entered.DestinationScene.Find(entered.PlayerObjectId)!.Transform.Scale);
            Assert.Null(entered.DestinationScene.Find(entered.PlayerObjectId)!.Door);
            Assert.Null(entered.DestinationScene.Find(entered.PlayerObjectId)!.SpawnPoint);
            Assert.Null(entered.DestinationScene.Find(entered.PlayerObjectId)!.TemplateInstance);
            var enteredChild = Assert.Single(entered.DestinationScene.Objects,
                item => item.ParentId == entered.PlayerObjectId);
            Assert.NotEqual(sourceChildId, enteredChild.Id);
            Assert.Equal(new Vector3(0f, 1f, 0f), enteredChild.Transform.Position);
            Assert.Same(sourceRuntimeScene, scenes[exteriorId]);

            scenes[interiorId] = entered.DestinationScene;
            var returnDoor = new WorldDoorComponent(exteriorId, exteriorSpawnId, Quaternion.Identity);
            var returned = WorldCellPlayTransferFactory.Prepare(world, scenes, interiorId,
                entered.DestinationScene, entered.PlayerObjectId, returnDoor);
            Assert.Equal(exteriorId, returned.DestinationCellId);
            Assert.NotEqual(entered.PlayerObjectId, returned.PlayerObjectId);
            Assert.Null(returned.DestinationScene.Find(originalPlayerId));
            Assert.Equal(returned.PlayerObjectId, returned.DestinationScene.PlaySettings.PlayerObjectId);
            Assert.Single(returned.DestinationScene.Objects,
                item => item.ParentId == returned.PlayerObjectId);
            Assert.Same(entered.DestinationScene, scenes[interiorId]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MissingDestinationSpawnLeavesCurrentPlaySceneUntouched()
    {
        var directory = TemporaryDirectory();
        try
        {
            var sourceCellId = Guid.NewGuid();
            var destinationCellId = Guid.NewGuid();
            var playerId = Guid.NewGuid();
            var exteriorPath = Path.Combine(directory, "exterior.json");
            var interiorPath = Path.Combine(directory, "interior.json");
            SceneFile.SaveAtomic(new SceneGraph(), exteriorPath);
            SceneFile.SaveAtomic(new SceneGraph(), interiorPath);
            var manifestPath = Path.Combine(directory, WorldManifest.DefaultFileName);
            WorldManifest.SaveAtomic(manifestPath, 32f,
            [
                new WorldCellDefinition
                {
                    Id = sourceCellId,
                    Kind = WorldCellKind.Exterior,
                    ExteriorCoordinate = new ExteriorCellCoordinate(0, 0),
                    ScenePath = "exterior.json"
                },
                new WorldCellDefinition { Id = destinationCellId, Kind = WorldCellKind.Interior, ScenePath = "interior.json" }
            ]);
            var world = WorldManifest.Load(manifestPath);
            var sourceScene = new SceneGraph();
            sourceScene.AddUnparented(new SceneObject(playerId, "Play character")
            {
                Transform = new Transform { Position = new Vector3(3f, 2f, 1f) }
            });
            var scenes = new Dictionary<Guid, SceneGraph>();
            var door = new WorldDoorComponent(destinationCellId, Guid.NewGuid(), Quaternion.Identity);

            var error = Assert.Throws<InvalidDataException>(() => WorldCellPlayTransferFactory.Prepare(
                world, scenes, sourceCellId, sourceScene, playerId, door));

            Assert.Contains("missing spawn", error.Message, StringComparison.Ordinal);
            Assert.Same(sourceScene, scenes[sourceCellId]);
            Assert.Equal(new Vector3(3f, 2f, 1f), sourceScene.Find(playerId)!.Transform.Position);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-world-play-transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
