using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ember.Rpg;

public sealed record ActorDef
{
    public ContentId<ActorContentKind> Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public ContentId<FactionContentKind>? FactionId { get; init; }
    public ActorStats Stats { get; init; } = new();

    public ActorDef() { }
    public ActorDef(ContentId<ActorContentKind> id, string name,
        ContentId<FactionContentKind>? factionId = null, ActorStats? stats = null)
    {
        Id = id;
        Name = name;
        FactionId = factionId;
        Stats = stats ?? new ActorStats();
    }
}

public sealed record FactionDef(ContentId<FactionContentKind> Id, string Name);

public sealed class ActorCatalogue
{
    private readonly Dictionary<ContentId<ActorContentKind>, ActorDef> _actors = new();
    public IReadOnlyDictionary<ContentId<ActorContentKind>, ActorDef> All => _actors;
    public int Count => _actors.Count;

    public void Add(ActorDef actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(actor.Id.Value)) throw new ArgumentException("An actor needs an id.", nameof(actor));
        if (string.IsNullOrWhiteSpace(actor.Name)) throw new ArgumentException("An actor needs a name.", nameof(actor));
        ArgumentNullException.ThrowIfNull(actor.Stats);
        actor.Stats.Validate();
        _actors[actor.Id] = actor;
    }

    public ActorDef? Get(ContentId<ActorContentKind> id) => _actors.TryGetValue(id, out var actor) ? actor : null;
}

public sealed class FactionCatalogue
{
    private readonly Dictionary<ContentId<FactionContentKind>, FactionDef> _factions = new();
    public IReadOnlyDictionary<ContentId<FactionContentKind>, FactionDef> All => _factions;
    public int Count => _factions.Count;

    public void Add(FactionDef faction)
    {
        ArgumentNullException.ThrowIfNull(faction);
        if (string.IsNullOrWhiteSpace(faction.Id.Value)) throw new ArgumentException("A faction needs an id.", nameof(faction));
        if (string.IsNullOrWhiteSpace(faction.Name)) throw new ArgumentException("A faction needs a name.", nameof(faction));
        _factions[faction.Id] = faction;
    }

    public FactionDef? Get(ContentId<FactionContentKind> id) => _factions.TryGetValue(id, out var faction) ? faction : null;
}

/// <summary>Related RPG content catalogs that can be checked together before a game starts.</summary>
public sealed class RpgContentSet
{
    public ActorCatalogue Actors { get; init; } = new();
    public ItemCatalogue Items { get; init; } = new();
    public FactionCatalogue Factions { get; init; } = new();
    public SpellCatalogue Spells { get; init; } = new();
    public IReadOnlyList<DialogueTree> Dialogues { get; init; } = Array.Empty<DialogueTree>();
    public QuestCatalogue Quests { get; init; } = new();

    public IReadOnlyList<ContentDiagnostic> Validate()
    {
        ArgumentNullException.ThrowIfNull(Actors);
        ArgumentNullException.ThrowIfNull(Items);
        ArgumentNullException.ThrowIfNull(Factions);
        ArgumentNullException.ThrowIfNull(Spells);
        ArgumentNullException.ThrowIfNull(Dialogues);
        ArgumentNullException.ThrowIfNull(Quests);

        var registry = new ContentRegistry();
        var errors = new List<ContentDiagnostic>();
        foreach (var id in Actors.All.Keys) Register(registry, id, "actor", errors);
        foreach (var id in Items.All.Keys) Register(registry, id, "item", errors);
        foreach (var id in Factions.All.Keys) Register(registry, id, "faction", errors);
        foreach (var id in Spells.All.Keys) Register(registry, id, "spell", errors);
        foreach (var dialogue in Dialogues) Register(registry, dialogue.Id, "dialogue", errors);
        foreach (var quest in Quests.All()) Register(registry, quest.Id, "quest", errors);

        foreach (var actor in Actors.All.Values)
            if (actor.FactionId is { } factionId)
                AddReferenceError(registry.CheckReference($"actor '{actor.Id.Value}'.FactionId", factionId), errors);

        foreach (var dialogue in Dialogues)
        {
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in dialogue.Nodes)
                if (!nodeIds.Add(node.Id))
                    errors.Add(new ContentDiagnostic($"dialogue '{dialogue.Id.Value}'", $"duplicate node '{node.Id}'"));

            foreach (var node in dialogue.Nodes)
            {
                if (node.SpeakerActorId is { } actorId)
                    AddReferenceError(registry.CheckReference(
                        $"dialogue '{dialogue.Id.Value}' node '{node.Id}'.SpeakerActorId", actorId), errors);
                for (var i = 0; i < node.Options.Count; i++)
                {
                    var option = node.Options[i];
                    foreach (var requirement in option.RequiresStats) requirement.Validate();
                    foreach (var requirement in option.RequiresFactions)
                        AddReferenceError(registry.CheckReference(
                            $"dialogue '{dialogue.Id.Value}' node '{node.Id}' option {i}.RequiresFactions",
                            requirement.FactionId), errors);
                }
            }

            foreach (var node in dialogue.Nodes)
                for (var i = 0; i < node.Options.Count; i++)
                {
                    var next = node.Options[i].Next;
                    if (next is not null && !nodeIds.Contains(next))
                        errors.Add(new ContentDiagnostic(
                            $"dialogue '{dialogue.Id.Value}' node '{node.Id}' option {i}.Next",
                            $"dialogue node '{next}'"));
                }
        }

        foreach (var quest in Quests.All())
        {
            if (quest.StartDialogueId is { } dialogueId)
                AddReferenceError(registry.CheckReference($"quest '{quest.Id.Value}'.StartDialogueId", dialogueId), errors);
            foreach (var stage in quest.Stages)
            {
                if (stage.TargetActorId is { } actorId)
                    AddReferenceError(registry.CheckReference(
                        $"quest '{quest.Id.Value}' stage '{stage.Id}'.TargetActorId", actorId), errors);
                if (stage.RequiredItemId is { } itemId)
                    AddReferenceError(registry.CheckReference(
                        $"quest '{quest.Id.Value}' stage '{stage.Id}'.RequiredItemId", itemId), errors);
            }
        }

        return errors;
    }

    /// <summary>Checks save references against this pack and the save's own item definitions.</summary>
    public IReadOnlyList<ContentDiagnostic> ValidateSaveReferences(SaveState save)
    {
        ArgumentNullException.ThrowIfNull(save);
        var registry = new ContentRegistry();
        foreach (var id in Actors.All.Keys) registry.Register(id);
        foreach (var dialogue in Dialogues) registry.Register(dialogue.Id);
        foreach (var quest in Quests.All()) registry.Register(quest.Id);
        foreach (var id in Factions.All.Keys) registry.Register(id);
        foreach (var (id, _) in save.ItemDefs.All) registry.Register(id);

        var errors = new List<ContentDiagnostic>();
        foreach (var actor in save.ActorStates.Entries)
        {
            AddReferenceError(registry.CheckReference($"actor instance '{actor.WorldInstanceId}'.ActorId", actor.ActorId), errors);
            foreach (var standing in actor.Factions)
                AddReferenceError(registry.CheckReference(
                    $"actor instance '{actor.WorldInstanceId}' faction", standing.FactionId), errors);
            foreach (var item in actor.Inventory.Entries)
                AddReferenceError(registry.CheckReference(
                    $"actor instance '{actor.WorldInstanceId}' inventory", item.ItemId), errors);
        }
        foreach (var entity in save.Entities)
            if (entity.ActorId is { } actorId)
                AddReferenceError(registry.CheckReference($"entity '{entity.Id}'.ActorId", actorId), errors);
        foreach (var entry in save.Player.Bag.Entries)
            AddReferenceError(registry.CheckReference($"player bag entry '{entry.ItemId.Value}'", entry.ItemId), errors);
        foreach (var container in save.ContainerInventories.Entries)
            foreach (var entry in container.Contents.Entries)
                AddReferenceError(registry.CheckReference(
                    $"container '{container.WorldInstanceId}' item '{entry.ItemId.Value}'", entry.ItemId), errors);
        foreach (var entry in save.WorldItems.Entries)
            AddReferenceError(registry.CheckReference(
                $"world item '{entry.WorldInstanceId}'", entry.ItemId), errors);
        foreach (var (slot, itemId) in save.Player.Equip.All)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                errors.Add(new ContentDiagnostic($"player equipment slot '{slot}'", "missing item id"));
                continue;
            }
            AddReferenceError(registry.CheckReference($"player equipment slot '{slot}'",
                new ContentId<ItemContentKind>(itemId)), errors);
        }
        if (save.Dialogue.Tree is { } dialogueId)
            AddReferenceError(registry.CheckReference("save Dialogue.Tree", dialogueId), errors);
        return errors;
    }

    private static void Register<TKind>(ContentRegistry registry, ContentId<TKind> id,
        string kindName, List<ContentDiagnostic> errors)
    {
        if (!registry.Register(id)) errors.Add(new ContentDiagnostic(kindName, $"duplicate id '{id.Value}'"));
    }

    private static void AddReferenceError(ContentDiagnostic? diagnostic, List<ContentDiagnostic> errors)
    {
        if (diagnostic is not null) errors.Add(diagnostic);
    }
}

/// <summary>Loads a content pack and reports cross-reference failures with source and target IDs.</summary>
public static class RpgContentJson
{
    public static RpgContentSet FromJson(string json)
    {
        var document = JsonSerializer.Deserialize<ContentDocument>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { FlagValueJson.Instance, new JsonStringEnumConverter() }
        }) ?? throw new JsonException("The RPG content document was empty.");

        var actors = new ActorCatalogue();
        foreach (var actor in document.Actors ?? new List<ActorDef>()) actors.Add(actor);
        var items = new ItemCatalogue();
        foreach (var item in document.Items ?? new List<ItemDef>()) items.Add(item);
        var factions = new FactionCatalogue();
        foreach (var faction in document.Factions ?? new List<FactionDef>()) factions.Add(faction);
        var spells = new SpellCatalogue();
        foreach (var spell in document.Spells ?? new List<TargetedSpellDef>()) spells.Add(spell);
        var quests = new QuestCatalogue();
        foreach (var quest in document.Quests ?? new List<QuestDef>()) quests.Add(quest);
        var content = new RpgContentSet
        {
            Actors = actors,
            Items = items,
            Factions = factions,
            Spells = spells,
            Dialogues = document.Dialogues is null ? Array.Empty<DialogueTree>() : document.Dialogues,
            Quests = quests
        };
        var errors = content.Validate();
        if (errors.Count > 0)
            throw new InvalidDataException("RPG content validation failed: " + string.Join(" ", errors));
        return content;
    }

    public static RpgContentSet Load(string path) => FromJson(File.ReadAllText(path));

    private sealed class ContentDocument
    {
        public ContentDocument() { }
        public List<ActorDef>? Actors { get; init; }
        public List<ItemDef>? Items { get; init; }
        public List<FactionDef>? Factions { get; init; }
        public List<TargetedSpellDef>? Spells { get; init; }
        public List<DialogueTree>? Dialogues { get; init; }
        public List<QuestDef>? Quests { get; init; }
    }
}
