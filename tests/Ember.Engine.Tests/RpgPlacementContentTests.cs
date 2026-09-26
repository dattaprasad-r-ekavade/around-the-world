using System;
using System.Collections.Generic;
using System.IO;
using Ember.Rpg;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class RpgPlacementContentTests
{
    [Fact]
    public void CharacterStudioSampleRegistersActorAndItemDefinitions()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "RpgPlacementDefinitions.json");
        var content = RpgContentJson.Load(path);

        Assert.Equal(2, content.Actors.Count);
        Assert.Equal("Town Guard", content.Actors.Get(new ContentId<ActorContentKind>("actors.town-guard"))!.Name);
        Assert.Equal(2, content.Items.Count);
        Assert.Equal("Iron Sword", content.Items.Get("items.iron-sword")!.Name);
        var dialogue = Assert.Single(content.Dialogues);
        Assert.Equal("The gate is under watch. What do you need?", dialogue.Node("greeting")!.Text);
        Assert.Equal(3, dialogue.Node("greeting")!.Options.Count);
        Assert.Empty(content.Validate());
    }

    [Fact]
    public void DialogueAuthoringPackRoundTripsConditionsEffectsAndReferencesAtomically()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ember-dialogue-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "content.json");
        var actorId = new ContentId<ActorContentKind>("actors.elder");
        var factionId = new ContentId<FactionContentKind>("factions.village");
        var tree = new DialogueTree
        {
            Id = new ContentId<DialogueContentKind>("dialogue.elder"),
            Nodes = new List<DialogueNode>
            {
                new()
                {
                    Id = "greeting",
                    Speaker = "Elder",
                    SpeakerActorId = actorId,
                    Text = "Have you found the lost seal?",
                    Options = new[]
                    {
                        new DialogueOption
                        {
                            Id = "report-seal",
                            Label = "I found it.",
                            Next = "thanks",
                            Requires = new[] { new FlagCondition { Flag = "seal_found", Bool = true } },
                            RequiresStats = new[] { new DialogueStatRequirement(ActorAttribute.Personality, 25) },
                            RequiresFactions = new[] { new DialogueFactionRequirement(factionId, 10, true) },
                            Sets = new Dictionary<string, FlagValue>
                            {
                                ["elder_helped"] = FlagValue.From(true),
                                ["reputation_bonus"] = FlagValue.From(5d),
                                ["last_topic"] = FlagValue.From("lost_seal")
                            }
                        }
                    }
                },
                new() { Id = "thanks", Speaker = "Elder", Text = "Then the village is in your debt." }
            }
        };
        var actors = new ActorCatalogue();
        actors.Add(new ActorDef(actorId, "Village Elder"));
        var factions = new FactionCatalogue();
        factions.Add(new FactionDef(factionId, "Village"));
        var content = new RpgContentSet
        {
            Actors = actors,
            Factions = factions,
            Dialogues = new[] { tree }
        };

        try
        {
            RpgContentJson.SaveAtomic(path, content);
            var firstSave = File.ReadAllText(path);
            RpgContentJson.SaveAtomic(path, content);
            var loaded = RpgContentJson.Load(path);
            var savedTree = Assert.Single(loaded.Dialogues);
            var savedChoice = Assert.Single(savedTree.Node("greeting")!.Options);

            Assert.Equal(firstSave, File.ReadAllText(path));
            Assert.Equal("thanks", savedChoice.Next);
            Assert.Equal("seal_found", Assert.Single(savedChoice.Requires).Flag);
            Assert.Equal(ActorAttribute.Personality, Assert.Single(savedChoice.RequiresStats).Attribute);
            Assert.Equal(factionId, Assert.Single(savedChoice.RequiresFactions).FactionId);
            Assert.Equal(FlagValue.From(true), savedChoice.Sets["elder_helped"]);
            Assert.Equal(FlagValue.From(5d), savedChoice.Sets["reputation_bonus"]);
            Assert.Equal(FlagValue.From("lost_seal"), savedChoice.Sets["last_topic"]);
            Assert.Empty(loaded.Validate());

            var invalidTree = new DialogueTree
            {
                Id = tree.Id,
                Nodes = new List<DialogueNode> { new() { Id = "start", SpeakerActorId = new ContentId<ActorContentKind>("actors.missing") } }
            };
            var invalidContent = new RpgContentSet
            {
                Actors = actors,
                Factions = factions,
                Dialogues = new[] { invalidTree }
            };
            Assert.Throws<InvalidDataException>(() => RpgContentJson.SaveAtomic(path, invalidContent));
            Assert.Equal(firstSave, File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DialogueValidationReportsSeveralAuthoringErrorsTogether()
    {
        var tree = new DialogueTree
        {
            Id = new ContentId<DialogueContentKind>("dialogue.broken"),
            Nodes = new List<DialogueNode>
            {
                new()
                {
                    Id = string.Empty,
                    Speaker = string.Empty,
                    Text = string.Empty,
                    Options = new[]
                    {
                        new DialogueOption
                        {
                            Label = string.Empty,
                            Next = "missing-node",
                            Requires = new[] { new FlagCondition { Flag = string.Empty, AtLeast = 8, AtMost = 2 } },
                            RequiresStats = new[] { new DialogueStatRequirement((ActorAttribute)99, -1) },
                            Sets = new Dictionary<string, FlagValue> { ["bad-number"] = FlagValue.From(double.NaN) }
                        }
                    }
                }
            }
        };
        var diagnostics = new RpgContentSet { Dialogues = new[] { tree } }.Validate();

        Assert.True(diagnostics.Count >= 8, string.Join(Environment.NewLine, diagnostics));
        Assert.Contains(diagnostics, diagnostic => diagnostic.MissingTarget.Contains("node ID", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.MissingTarget.Contains("choice text", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.MissingTarget.Contains("minimum cannot exceed maximum", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.MissingTarget.Contains("missing-node", StringComparison.Ordinal));
    }

    [Fact]
    public void ProjectValidationKeepsAllContentAndPlacementReferencesWithFileOwners()
    {
        const string json = """
            {
              "actors": [{ "id": "actors.guard", "name": "Guard" }],
              "items": [],
              "dialogues": [{
                "id": "dialogue.guard",
                "nodes": [{ "id": "greeting", "speaker": "Guard", "speakerActorId": "actors.missing", "text": "Hello." }]
              }],
              "quests": [{
                "id": "quests.find-item",
                "title": "Find the item",
                "startDialogueId": "dialogue.missing",
                "stages": [{ "id": "find", "targetActorId": "actors.absent", "requiredItemId": "items.absent" }]
              }]
            }
            """;
        var contentPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "rpg-definitions.json"));
        var scenePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "exterior.json"));
        var parsed = RpgContentJson.ParseForValidation(json);

        var diagnostics = RpgProjectValidator.Validate(parsed, contentPath,
        [
            new RpgPlacementReference(scenePath, "object 'GuardPlacement' (11111111-1111-1111-1111-111111111111)",
                RpgPlacementDefinitionKind.Actor, "actors.unknown"),
            new RpgPlacementReference(scenePath, "object 'PotionPlacement' (22222222-2222-2222-2222-222222222222)",
                RpgPlacementDefinitionKind.Item, "items.unknown")
        ]);

        Assert.Equal(6, diagnostics.Count);
        Assert.Contains(diagnostics, diagnostic => diagnostic.SourceFile == contentPath
            && diagnostic.SourceRecord.Contains("dialogue 'dialogue.guard'", StringComparison.Ordinal)
            && diagnostic.Message.Contains("actors.missing", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.SourceFile == contentPath
            && diagnostic.SourceRecord.Contains("quests.find-item", StringComparison.Ordinal)
            && diagnostic.Message.Contains("dialogue.missing", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.SourceFile == contentPath
            && diagnostic.SourceRecord.Contains("stage 'find'", StringComparison.Ordinal)
            && diagnostic.Message.Contains("actors.absent", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.SourceFile == contentPath
            && diagnostic.SourceRecord.Contains("stage 'find'", StringComparison.Ordinal)
            && diagnostic.Message.Contains("items.absent", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.SourceFile == scenePath
            && diagnostic.SourceRecord.Contains("GuardPlacement", StringComparison.Ordinal)
            && diagnostic.Message.Contains("actors.unknown", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic => diagnostic.SourceFile == scenePath
            && diagnostic.SourceRecord.Contains("PotionPlacement", StringComparison.Ordinal)
            && diagnostic.Message.Contains("items.unknown", StringComparison.Ordinal));

        Assert.Throws<InvalidDataException>(() => RpgContentJson.FromJson(json));
    }
}
