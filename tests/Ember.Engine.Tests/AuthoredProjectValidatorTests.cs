using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember.Authoring;
using Ember.Rpg;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class AuthoredProjectValidatorTests
{
    [Fact]
    public void ProjectValidationReportsWorldContentAndPlacementErrorsWithoutChangingFiles()
    {
        var directory = TemporaryDirectory();
        try
        {
            var cellId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var missingSceneCellId = Guid.Parse("aaaaaaaa-1111-1111-1111-111111111111");
            var scenePath = Path.Combine(directory, "Scenes", "Exterior.json");
            var contentPath = Path.Combine(directory, "RpgContent.json");
            var scene = new SceneGraph();
            scene.Add(new SceneObject(Guid.Parse("22222222-2222-2222-2222-222222222222"), "BadDoor")
            {
                Door = new WorldDoorComponent(Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    Guid.Parse("44444444-4444-4444-4444-444444444444"), Quaternion.Identity)
            });
            scene.Add(new SceneObject(Guid.Parse("55555555-5555-5555-5555-555555555555"), "UnknownActor")
            {
                WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Actor, "actors.unknown",
                    Guid.Parse("66666666-6666-6666-6666-666666666666"))
            });
            scene.Add(new SceneObject(Guid.Parse("77777777-7777-7777-7777-777777777777"), "UnknownItem")
            {
                WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Item, "items.unknown",
                    Guid.Parse("88888888-8888-8888-8888-888888888888"))
            });
            SceneFile.SaveAtomic(scene, scenePath);

            var manifestPath = Path.Combine(directory, "world.json");
            WorldManifest.SaveAtomic(manifestPath, 32f,
            [
                new WorldCellDefinition
                {
                    Id = cellId,
                    Kind = WorldCellKind.Exterior,
                    ExteriorCoordinate = new ExteriorCellCoordinate(0, 0),
                    ScenePath = "Scenes/Exterior.json"
                },
                new WorldCellDefinition
                {
                    Id = missingSceneCellId,
                    Kind = WorldCellKind.Exterior,
                    ExteriorCoordinate = new ExteriorCellCoordinate(1, 0),
                    ScenePath = "Scenes/Missing.json"
                }
            ]);
            File.WriteAllText(contentPath, """
                {
                  "actors": [{ "id": "actors.guard", "name": "Guard" }],
                  "items": [],
                  "dialogues": [{ "id": "dialogue.guard", "nodes": [{
                    "id": "greeting", "speaker": "Guard", "speakerActorId": "actors.missing", "text": "Hello."
                  }]}],
                  "quests": [{
                    "id": "quests.find-item", "title": "Find the item", "startDialogueId": "dialogue.missing",
                    "stages": [{ "id": "find", "targetActorId": "actors.absent", "requiredItemId": "items.absent" }]
                  }]
                }
                """);
            var pathDirectory = Path.Combine(directory, "Paths");
            Directory.CreateDirectory(pathDirectory);
            var pathGraphPath = Path.Combine(pathDirectory, "Exterior.paths.json");
            File.WriteAllText(pathGraphPath, $$"""
                {
                  "version": 1,
                  "graph": {
                    "cellId": "{{cellId}}", "kind": "Exterior",
                    "nodes": [{ "id": "99999999-9999-9999-9999-999999999999", "position": { "x": 0, "y": 0, "z": 0 } }],
                    "edges": [{ "fromNodeId": "99999999-9999-9999-9999-999999999999", "toNodeId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "clearanceRadius": 1, "bidirectional": true }]
                  }
                }
                """);
            var networkPath = Path.Combine(pathDirectory, "world-paths.json");
            File.WriteAllText(networkPath, $$"""
                {
                  "version": 1,
                  "connections": [{
                    "id": "cccccccc-cccc-cccc-cccc-cccccccccccc",
                    "from": { "cellId": "{{cellId}}", "nodeId": "99999999-9999-9999-9999-999999999999" },
                    "to": { "cellId": "{{missingSceneCellId}}", "nodeId": "dddddddd-dddd-dddd-dddd-dddddddddddd" },
                    "kind": "ExteriorBoundary", "clearanceRadius": 1, "transitionCost": 1,
                    "bidirectional": true, "doorInstanceId": null
                  }]
                }
                """);
            var originalFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllText, StringComparer.OrdinalIgnoreCase);

            var result = AuthoredProjectValidator.Validate(manifestPath, contentPath);

            Assert.False(result.IsValid);
            Assert.Equal(10, result.Diagnostics.Count);
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == scenePath
                && issue.SourceRecord.Contains("BadDoor", StringComparison.Ordinal)
                && issue.Message.Contains("unknown cell", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == scenePath
                && issue.SourceRecord.Contains("UnknownActor", StringComparison.Ordinal)
                && issue.Message.Contains("actors.unknown", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == scenePath
                && issue.SourceRecord.Contains("UnknownItem", StringComparison.Ordinal)
                && issue.Message.Contains("items.unknown", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == contentPath
                && issue.SourceRecord.Contains("dialogue.guard", StringComparison.Ordinal)
                && issue.Message.Contains("actors.missing", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == contentPath
                && issue.SourceRecord.Contains("quests.find-item", StringComparison.Ordinal)
                && issue.Message.Contains("dialogue.missing", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == contentPath
                && issue.SourceRecord.Contains("stage 'find'", StringComparison.Ordinal)
                && issue.Message.Contains("actors.absent", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == contentPath
                && issue.SourceRecord.Contains("stage 'find'", StringComparison.Ordinal)
                && issue.Message.Contains("items.absent", StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == Path.Combine(directory, "Scenes", "Missing.json")
                && issue.SourceRecord.Contains(missingSceneCellId.ToString(), StringComparison.Ordinal));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == pathGraphPath
                && issue.SourceRecord.Contains("99999999-9999-9999-9999-999999999999 -> bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", StringComparison.Ordinal)
                && issue.Message.Contains("missing node", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Diagnostics, issue => issue.SourceFile == networkPath
                && issue.SourceRecord.Contains("cccccccc-cccc-cccc-cccc-cccccccccccc", StringComparison.Ordinal)
                && issue.Message.Contains("missing", StringComparison.OrdinalIgnoreCase));

            var filesAfterValidation = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllText, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(originalFiles.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
                filesAfterValidation.Keys.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
            foreach (var (path, originalContent) in originalFiles)
                Assert.Equal(originalContent, filesAfterValidation[path]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ValidProjectWithRegisteredRpgReferencesHasNoDiagnostics()
    {
        var directory = TemporaryDirectory();
        try
        {
            var exteriorId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var interiorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var exteriorSpawn = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var interiorSpawn = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var actorId = new ContentId<ActorContentKind>("actors.guard");
            var itemId = new ContentId<ItemContentKind>("items.apple");
            var dialogueId = new ContentId<DialogueContentKind>("dialogue.guard");
            var content = new RpgContentSet
            {
                Actors = Actors(new ActorDef(actorId, "Guard")),
                Items = Items(new ItemDef(itemId, "Apple", null, true)),
                Dialogues =
                [new DialogueTree
                {
                    Id = dialogueId,
                    Nodes = [new DialogueNode { Id = "greeting", Speaker = "Guard", SpeakerActorId = actorId, Text = "Hello." }]
                }],
                Quests = Quests(new QuestDef
                {
                    Id = new ContentId<QuestContentKind>("quests.delivery"),
                    Title = "Deliver an apple",
                    StartDialogueId = dialogueId,
                    Stages = [new QuestStage { Id = "deliver", RequiredItemId = itemId }]
                })
            };

            var exteriorPath = Path.Combine(directory, "Scenes", "Exterior.json");
            var interiorPath = Path.Combine(directory, "Interiors", "House.json");
            var exterior = new SceneGraph();
            exterior.Add(new SceneObject(Guid.Parse("55555555-5555-5555-5555-555555555555"), "TownDoor")
            {
                Door = new WorldDoorComponent(interiorId, interiorSpawn, Quaternion.Identity)
            });
            exterior.Add(new SceneObject(Guid.Parse("66666666-6666-6666-6666-666666666666"), "TownSpawn")
            {
                SpawnPoint = new WorldSpawnComponent(exteriorSpawn)
            });
            exterior.Add(new SceneObject(Guid.Parse("77777777-7777-7777-7777-777777777777"), "GuardPlacement")
            {
                WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Actor, actorId.Value,
                    Guid.Parse("88888888-8888-8888-8888-888888888888"))
            });
            exterior.Add(new SceneObject(Guid.Parse("99999999-9999-9999-9999-999999999999"), "ApplePlacement")
            {
                WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Item, itemId.Value,
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"))
            });
            var interior = new SceneGraph();
            interior.Add(new SceneObject(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "ReturnDoor")
            {
                Door = new WorldDoorComponent(exteriorId, exteriorSpawn, Quaternion.Identity)
            });
            interior.Add(new SceneObject(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "HouseSpawn")
            {
                SpawnPoint = new WorldSpawnComponent(interiorSpawn)
            });
            SceneFile.SaveAtomic(exterior, exteriorPath);
            SceneFile.SaveAtomic(interior, interiorPath);

            var manifestPath = Path.Combine(directory, "world.json");
            WorldManifest.SaveAtomic(manifestPath, 32f,
            [
                new WorldCellDefinition
                {
                    Id = exteriorId, Kind = WorldCellKind.Exterior,
                    ExteriorCoordinate = new ExteriorCellCoordinate(0, 0), ScenePath = "Scenes/Exterior.json"
                },
                new WorldCellDefinition { Id = interiorId, Kind = WorldCellKind.Interior, ScenePath = "Interiors/House.json" }
            ]);
            var contentPath = Path.Combine(directory, "RpgContent.json");
            RpgContentJson.SaveAtomic(contentPath, content);

            var result = AuthoredProjectValidator.Validate(manifestPath, contentPath);

            Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Diagnostics));
            Assert.Empty(result.Diagnostics);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ActorCatalogue Actors(params ActorDef[] actors)
    {
        var result = new ActorCatalogue();
        foreach (var actor in actors) result.Add(actor);
        return result;
    }

    private static ItemCatalogue Items(params ItemDef[] items)
    {
        var result = new ItemCatalogue();
        foreach (var item in items) result.Add(item);
        return result;
    }

    private static QuestCatalogue Quests(params QuestDef[] quests)
    {
        var result = new QuestCatalogue();
        foreach (var quest in quests) result.Add(quest);
        return result;
    }

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ember-authored-project-validation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
