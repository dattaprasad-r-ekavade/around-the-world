using System;
using System.Collections.Generic;
using System.IO;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldTravelComponentTests
{
    [Fact]
    public void SceneDoorAndSpawnRoundTripAndValidateAgainstWorldManifest()
    {
        var directory = TemporaryDirectory();
        try
        {
            var exteriorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var interiorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var exteriorSpawnId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var interiorSpawnId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var exteriorScenePath = Path.Combine(directory, "exterior.json");
            var interiorScenePath = Path.Combine(directory, "interior.json");
            var facing = Quaternion.CreateFromYawPitchRoll(MathHelper.PiOver2, 0f, 0f);

            var exterior = new SceneGraph();
            exterior.Add(new SceneObject(Guid.Parse("55555555-5555-5555-5555-555555555555"), "HouseDoor")
            {
                Door = new WorldDoorComponent(interiorId, interiorSpawnId, facing)
            });
            exterior.Add(new SceneObject(Guid.Parse("66666666-6666-6666-6666-666666666666"), "ExteriorSpawn")
            {
                SpawnPoint = new WorldSpawnComponent(exteriorSpawnId),
                Transform = new Transform { Position = new Vector3(8f, 1f, 12f) }
            });

            var interior = new SceneGraph();
            interior.Add(new SceneObject(Guid.Parse("77777777-7777-7777-7777-777777777777"), "ReturnDoor")
            {
                Door = new WorldDoorComponent(exteriorId, exteriorSpawnId, Quaternion.Identity)
            });
            interior.Add(new SceneObject(Guid.Parse("88888888-8888-8888-8888-888888888888"), "InteriorSpawn")
            {
                SpawnPoint = new WorldSpawnComponent(interiorSpawnId),
                Transform = new Transform { Position = new Vector3(2f, 1f, 3f) }
            });

            SceneFile.SaveAtomic(exterior, exteriorScenePath);
            SceneFile.SaveAtomic(interior, interiorScenePath);
            var loadedExterior = SceneFile.Load(exteriorScenePath);
            var loadedInterior = SceneFile.Load(interiorScenePath);
            var manifestPath = Path.Combine(directory, "world.json");
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

            WorldTravelValidator.Validate(world, new Dictionary<Guid, SceneGraph>
            {
                [exteriorId] = loadedExterior,
                [interiorId] = loadedInterior
            });

            var loadedDoor = loadedExterior.Find(Guid.Parse("55555555-5555-5555-5555-555555555555"))!.Door!;
            Assert.Equal(3, SceneFile.CurrentVersion);
            Assert.Equal(interiorId, loadedDoor.DestinationCellId);
            Assert.Equal(interiorSpawnId, loadedDoor.DestinationSpawnId);
            Assert.Equal(Quaternion.Normalize(facing), loadedDoor.Facing);
            Assert.Equal(interiorSpawnId,
                loadedInterior.Find(Guid.Parse("88888888-8888-8888-8888-888888888888"))!.SpawnPoint!.Id);

            var destination = WorldTravelValidator.ResolveDestination(world,
                new Dictionary<Guid, SceneGraph>
                {
                    [exteriorId] = loadedExterior,
                    [interiorId] = loadedInterior
                }, loadedDoor);
            Assert.Equal(interiorId, destination.CellId);
            Assert.Equal(interiorSpawnId, destination.SpawnId);
            Assert.Equal(new Vector3(2f, 1f, 3f), destination.Position);
            Assert.Equal(Quaternion.Normalize(facing), destination.Facing);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TravelValidationRejectsUnknownDestinationAndMissingSpawn()
    {
        var directory = TemporaryDirectory();
        try
        {
            var exteriorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var interiorId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var knownSpawn = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
            File.WriteAllText(Path.Combine(directory, "exterior.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "interior.json"), "{}");
            var manifestPath = Path.Combine(directory, "world.json");
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
            var interior = new SceneGraph();
            interior.Add(new SceneObject(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), "Spawn")
            {
                SpawnPoint = new WorldSpawnComponent(knownSpawn)
            });

            var unknownCellScene = new SceneGraph();
            unknownCellScene.Add(new SceneObject(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), "BadDoor")
            {
                Door = new WorldDoorComponent(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), knownSpawn, Quaternion.Identity)
            });
            var unknownCellError = Assert.Throws<InvalidDataException>(() =>
                WorldTravelValidator.Validate(world, new Dictionary<Guid, SceneGraph>
                {
                    [exteriorId] = unknownCellScene,
                    [interiorId] = interior
                }));
            Assert.Contains("unknown cell", unknownCellError.Message, StringComparison.Ordinal);

            var missingSpawnScene = new SceneGraph();
            missingSpawnScene.Add(new SceneObject(Guid.Parse("abababab-abab-abab-abab-abababababab"), "BadSpawnDoor")
            {
                Door = new WorldDoorComponent(interiorId,
                    Guid.Parse("12121212-1212-1212-1212-121212121212"), Quaternion.Identity)
            });
            var missingSpawnError = Assert.Throws<InvalidDataException>(() =>
                WorldTravelValidator.Validate(world, new Dictionary<Guid, SceneGraph>
                {
                    [exteriorId] = missingSpawnScene,
                    [interiorId] = interior
                }));
            Assert.Contains("missing spawn", missingSpawnError.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-world-travel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
