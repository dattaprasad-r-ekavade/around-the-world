using System;
using System.IO;
using System.Linq;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldProjectValidatorTests
{
    [Fact]
    public void ValidationAggregatesDoorSpawnAndPathErrorsWithTheirOwningFiles()
    {
        var directory = TemporaryDirectory();
        try
        {
            var exteriorA = Id("11111111-1111-1111-1111-111111111111");
            var exteriorB = Id("22222222-2222-2222-2222-222222222222");
            var interior = Id("33333333-3333-3333-3333-333333333333");
            var missingSceneCell = Id("aaaaaaaa-1111-1111-1111-111111111111");
            var duplicateSpawn = Id("44444444-4444-4444-4444-444444444444");
            var unknownCell = Id("55555555-5555-5555-5555-555555555555");
            var missingSpawn = Id("66666666-6666-6666-6666-666666666666");
            var exteriorAPath = Path.Combine(directory, "Scenes", "Exterior_A.json");
            var exteriorBPath = Path.Combine(directory, "Scenes", "Exterior_B.json");
            var interiorPath = Path.Combine(directory, "Interiors", "House.json");

            var sceneA = new SceneGraph();
            sceneA.Add(new SceneObject(Id("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "UnknownCellDoor")
            {
                Door = new WorldDoorComponent(unknownCell, missingSpawn, Quaternion.Identity)
            });
            sceneA.Add(new SceneObject(Id("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "MissingSpawnDoor")
            {
                Door = new WorldDoorComponent(interior, missingSpawn, Quaternion.Identity)
            });
            SceneFile.SaveAtomic(sceneA, exteriorAPath);

            var sceneB = new SceneGraph();
            sceneB.Add(new SceneObject(Id("cccccccc-cccc-cccc-cccc-cccccccccccc"), "SpawnA")
            {
                SpawnPoint = new WorldSpawnComponent(duplicateSpawn)
            });
            sceneB.Add(new SceneObject(Id("dddddddd-dddd-dddd-dddd-dddddddddddd"), "SpawnB")
            {
                SpawnPoint = new WorldSpawnComponent(duplicateSpawn)
            });
            SceneFile.SaveAtomic(sceneB, exteriorBPath);
            SceneFile.SaveAtomic(new SceneGraph(), interiorPath);

            var manifestPath = Path.Combine(directory, "world.json");
            WorldManifest.SaveAtomic(manifestPath, 32f,
            [
                Cell(exteriorA, WorldCellKind.Exterior, "Scenes/Exterior_A.json", new ExteriorCellCoordinate(0, 0)),
                Cell(exteriorB, WorldCellKind.Exterior, "Scenes/Exterior_B.json", new ExteriorCellCoordinate(1, 0)),
                Cell(interior, WorldCellKind.Interior, "Interiors/House.json"),
                Cell(missingSceneCell, WorldCellKind.Exterior, "Scenes/Missing.json", new ExteriorCellCoordinate(2, 0))
            ]);

            var pathDirectory = Path.Combine(directory, "Paths");
            Directory.CreateDirectory(pathDirectory);
            var pathGraphPath = Path.Combine(pathDirectory, "Exterior_A.paths.json");
            File.WriteAllText(pathGraphPath, $$"""
                {
                  "version": 1,
                  "graph": {
                    "cellId": "{{exteriorA}}",
                    "kind": "Exterior",
                    "nodes": [{ "id": "77777777-7777-7777-7777-777777777777", "position": { "x": 0, "y": 0, "z": 0 } }],
                    "edges": [{ "fromNodeId": "77777777-7777-7777-7777-777777777777", "toNodeId": "88888888-8888-8888-8888-888888888888", "clearanceRadius": 1, "bidirectional": true }]
                  }
                }
                """);
            var networkPath = Path.Combine(pathDirectory, "world-paths.json");
            File.WriteAllText(networkPath, $$"""
                {
                  "version": 1,
                  "connections": [{
                    "id": "99999999-9999-9999-9999-999999999999",
                    "from": { "cellId": "{{exteriorA}}", "nodeId": "77777777-7777-7777-7777-777777777777" },
                    "to": { "cellId": "{{exteriorB}}", "nodeId": "aaaaaaaa-1111-1111-1111-111111111111" },
                    "kind": "ExteriorBoundary", "clearanceRadius": 1, "transitionCost": 1,
                    "bidirectional": true, "doorInstanceId": null
                  }]
                }
                """);

            var strictLoad = Assert.Throws<InvalidDataException>(() => WorldManifest.Load(manifestPath));
            Assert.Contains("refers to missing scene", strictLoad.Message, StringComparison.Ordinal);
            var result = WorldProjectValidator.Validate(manifestPath);

            Assert.False(result.IsValid);
            Assert.Equal(6, result.Diagnostics.Count);
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == exteriorAPath
                && issue.SourceRecord.Contains("UnknownCellDoor", StringComparison.Ordinal)
                && issue.Message.Contains("unknown cell", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == exteriorAPath
                && issue.SourceRecord.Contains("MissingSpawnDoor", StringComparison.Ordinal)
                && issue.Message.Contains("missing spawn", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == exteriorBPath
                && issue.SourceRecord.Contains(duplicateSpawn.ToString(), StringComparison.Ordinal)
                && issue.Message.Contains("duplicated", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == pathGraphPath
                && issue.SourceRecord.Contains("77777777-7777-7777-7777-777777777777 -> 88888888-8888-8888-8888-888888888888", StringComparison.Ordinal)
                && issue.Message.Contains("missing node", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == networkPath
                && issue.SourceRecord.Contains("99999999-9999-9999-9999-999999999999", StringComparison.Ordinal)
                && issue.Message.Contains("missing", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == Path.Combine(directory, "Scenes", "Missing.json")
                && issue.SourceRecord.Contains(missingSceneCell.ToString(), StringComparison.Ordinal)
                && issue.Message.Contains("could not find", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(3, result.Scenes.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ValidWorldReturnsLoadedCellsAndPathsWithoutDiagnostics()
    {
        var directory = TemporaryDirectory();
        try
        {
            var cellId = Id("11111111-1111-1111-1111-111111111111");
            var scenePath = Path.Combine(directory, "Exterior.json");
            SceneFile.SaveAtomic(new SceneGraph(), scenePath);
            var manifestPath = Path.Combine(directory, "world.json");
            WorldManifest.SaveAtomic(manifestPath, 32f,
            [Cell(cellId, WorldCellKind.Exterior, "Exterior.json", new ExteriorCellCoordinate(0, 0))]);
            var pathDirectory = Path.Combine(directory, "Paths");
            Directory.CreateDirectory(pathDirectory);
            CellPathGraphFile.SaveAtomic(Path.Combine(pathDirectory, "Exterior.paths.json"), new CellPathGraph
            {
                CellId = cellId,
                Kind = WorldCellKind.Exterior,
                Nodes = [new CellPathNode(Id("22222222-2222-2222-2222-222222222222"), new NavigationPoint(0, 0, 0))]
            });

            var result = WorldProjectValidator.Validate(manifestPath);

            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.NotNull(result.Manifest);
            Assert.Single(result.Scenes);
            Assert.Single(result.PathGraphs);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static WorldCellDefinition Cell(Guid id, WorldCellKind kind, string scenePath,
        ExteriorCellCoordinate? coordinate = null) => new()
    {
        Id = id,
        Kind = kind,
        ScenePath = scenePath,
        ExteriorCoordinate = coordinate
    };

    private static Guid Id(string value) => Guid.Parse(value);

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-world-project-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
