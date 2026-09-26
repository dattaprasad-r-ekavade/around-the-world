using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public SkillProgressionCatalogue SkillProgression { get; init; } = new();
    public IReadOnlyList<DialogueTree> Dialogues { get; init; } = Array.Empty<DialogueTree>();
    public QuestCatalogue Quests { get; init; } = new();

    public IReadOnlyList<ContentDiagnostic> Validate()
    {
        ArgumentNullException.ThrowIfNull(Actors);
        ArgumentNullException.ThrowIfNull(Items);
        ArgumentNullException.ThrowIfNull(Factions);
        ArgumentNullException.ThrowIfNull(Spells);
        ArgumentNullException.ThrowIfNull(SkillProgression);
        ArgumentNullException.ThrowIfNull(Dialogues);
        ArgumentNullException.ThrowIfNull(Quests);

        var registry = new ContentRegistry();
        var errors = new List<ContentDiagnostic>();
        foreach (var id in Actors.All.Keys) Register(registry, id, "actor", errors);
        foreach (var id in Items.All.Keys) Register(registry, id, "item", errors);
        foreach (var id in Factions.All.Keys) Register(registry, id, "faction", errors);
        foreach (var id in Spells.All.Keys) Register(registry, id, "spell", errors);
        foreach (var dialogue in Dialogues)
        {
            if (dialogue is null)
            {
                errors.Add(new ContentDiagnostic("dialogue catalogue", "null dialogue record"));
                continue;
            }
            if (string.IsNullOrWhiteSpace(dialogue.Id.Value))
                errors.Add(new ContentDiagnostic("dialogue", "dialogue ID cannot be empty"));
            else
                Register(registry, dialogue.Id, "dialogue", errors);
        }
        foreach (var quest in Quests.All()) Register(registry, quest.Id, "quest", errors);

        foreach (var actor in Actors.All.Values)
            if (actor.FactionId is { } factionId)
                AddReferenceError(registry.CheckReference($"actor '{actor.Id.Value}'.FactionId", factionId), errors);

        foreach (var dialogue in Dialogues)
        {
            if (dialogue is null) continue;
            var dialogueSource = $"dialogue '{dialogue.Id.Value}'";
            if (dialogue.Nodes is null || dialogue.Nodes.Count == 0)
            {
                errors.Add(new ContentDiagnostic(dialogueSource, "conversation needs at least one node"));
                continue;
            }

            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in dialogue.Nodes)
            {
                if (node is null) continue;
                if (string.IsNullOrWhiteSpace(node.Id))
                    errors.Add(new ContentDiagnostic(dialogueSource, "node ID cannot be empty"));
                else if (!nodeIds.Add(node.Id))
                    errors.Add(new ContentDiagnostic(dialogueSource, $"duplicate node '{node.Id}'"));
            }

            foreach (var node in dialogue.Nodes)
            {
                if (node is null) continue;
                var nodeSource = $"{dialogueSource} node '{node.Id}'";
                if (string.IsNullOrWhiteSpace(node.Text))
                    errors.Add(new ContentDiagnostic(nodeSource, "dialogue text cannot be empty"));
                if (node.SpeakerActorId is null && string.IsNullOrWhiteSpace(node.Speaker))
                    errors.Add(new ContentDiagnostic(nodeSource, "speaker name or registered speaker actor is required"));
                if (node.SpeakerActorId is { } actorId)
                    AddReferenceError(registry.CheckReference(
                        $"dialogue '{dialogue.Id.Value}' node '{node.Id}'.SpeakerActorId", actorId), errors);
                if (node.Options is null)
                {
                    errors.Add(new ContentDiagnostic(nodeSource, "choice list cannot be null"));
                    continue;
                }

                var optionIds = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < node.Options.Count; i++)
                {
                    var option = node.Options[i];
                    var optionSource = $"{nodeSource} option {i}";
                    if (option is null)
                    {
                        errors.Add(new ContentDiagnostic(optionSource, "choice record cannot be null"));
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(option.Label))
                        errors.Add(new ContentDiagnostic(optionSource, "choice text cannot be empty"));
                    if (!string.IsNullOrWhiteSpace(option.Id) && !optionIds.Add(option.Id))
                        errors.Add(new ContentDiagnostic(optionSource, $"duplicate choice ID '{option.Id}'"));

                    if (option.Requires is null)
                        errors.Add(new ContentDiagnostic(optionSource, "flag conditions cannot be null"));
                    else
                    {
                        for (var requirementIndex = 0; requirementIndex < option.Requires.Count; requirementIndex++)
                        {
                            var requirement = option.Requires[requirementIndex];
                            var requirementSource = $"{optionSource} flag condition {requirementIndex}";
                            if (requirement is null)
                            {
                                errors.Add(new ContentDiagnostic(requirementSource, "condition cannot be null"));
                                continue;
                            }
                            if (string.IsNullOrWhiteSpace(requirement.Flag))
                                errors.Add(new ContentDiagnostic(requirementSource, "flag name cannot be empty"));
                            var kinds = (requirement.Bool is null ? 0 : 1)
                                + (requirement.AtLeast is null && requirement.AtMost is null ? 0 : 1)
                                + (requirement.Text is null ? 0 : 1);
                            if (kinds > 1)
                                errors.Add(new ContentDiagnostic(requirementSource,
                                    "boolean, numeric, and text constraints cannot be combined for one flag"));
                            if (requirement.AtLeast is { } minimum && !double.IsFinite(minimum))
                                errors.Add(new ContentDiagnostic(requirementSource, "minimum must be finite"));
                            if (requirement.AtMost is { } maximum && !double.IsFinite(maximum))
                                errors.Add(new ContentDiagnostic(requirementSource, "maximum must be finite"));
                            if (requirement.AtLeast is { } lower && requirement.AtMost is { } upper
                                && double.IsFinite(lower) && double.IsFinite(upper) && lower > upper)
                                errors.Add(new ContentDiagnostic(requirementSource, "minimum cannot exceed maximum"));
                        }
                    }

                    if (option.RequiresStats is null)
                        errors.Add(new ContentDiagnostic(optionSource, "stat requirements cannot be null"));
                    else
                        for (var requirementIndex = 0; requirementIndex < option.RequiresStats.Count; requirementIndex++)
                            try { option.RequiresStats[requirementIndex].Validate(); }
                            catch (Exception exception)
                            {
                                errors.Add(new ContentDiagnostic($"{optionSource} stat requirement {requirementIndex}", exception.Message));
                            }

                    if (option.RequiresFactions is null)
                        errors.Add(new ContentDiagnostic(optionSource, "faction requirements cannot be null"));
                    else
                        for (var requirementIndex = 0; requirementIndex < option.RequiresFactions.Count; requirementIndex++)
                        {
                            var requirement = option.RequiresFactions[requirementIndex];
                            if (requirement is null)
                            {
                                errors.Add(new ContentDiagnostic($"{optionSource} faction requirement {requirementIndex}",
                                    "requirement cannot be null"));
                                continue;
                            }
                            AddReferenceError(registry.CheckReference(
                                $"{optionSource}.RequiresFactions[{requirementIndex}]", requirement.FactionId), errors);
                        }

                    if (option.Sets is null)
                        errors.Add(new ContentDiagnostic(optionSource, "choice effects cannot be null"));
                    else
                        foreach (var (name, value) in option.Sets)
                        {
                            if (string.IsNullOrWhiteSpace(name))
                                errors.Add(new ContentDiagnostic(optionSource, "effect flag name cannot be empty"));
                            if (value.Kind == FlagKind.Number && !double.IsFinite(value.AsNumber()))
                                errors.Add(new ContentDiagnostic(optionSource, $"numeric effect '{name}' must be finite"));
                        }
                }
            }

            foreach (var node in dialogue.Nodes)
            {
                if (node is null || node.Options is null) continue;
                for (var i = 0; i < node.Options.Count; i++)
                {
                    var option = node.Options[i];
                    if (option is null) continue;
                    var next = option.Next;
                    if (next is not null && string.IsNullOrWhiteSpace(next))
                        errors.Add(new ContentDiagnostic(
                            $"dialogue '{dialogue.Id.Value}' node '{node.Id}' option {i}.Next",
                            "target node ID cannot be empty"));
                    else if (next is not null && !nodeIds.Contains(next))
                        errors.Add(new ContentDiagnostic(
                            $"dialogue '{dialogue.Id.Value}' node '{node.Id}' option {i}.Next",
                            $"dialogue node '{next}'"));
                }
            }
        }

        foreach (var quest in Quests.All())
        {
            if (quest.StartDialogueId is { } dialogueId)
                AddReferenceError(registry.CheckReference($"quest '{quest.Id.Value}'.StartDialogueId", dialogueId), errors);
            var stageIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var stage in quest.Stages)
            {
                var stageSource = $"quest '{quest.Id.Value}' stage '{stage.Id}'";
                if (string.IsNullOrWhiteSpace(stage.Id) || !stageIds.Add(stage.Id))
                    errors.Add(new ContentDiagnostic($"quest '{quest.Id.Value}'", $"empty or duplicate stage id '{stage.Id}'"));
                if (stage.TargetWorldInstanceId == Guid.Empty)
                    errors.Add(new ContentDiagnostic(stageSource, "TargetWorldInstanceId cannot be empty"));
                if (stage.CompleteOn is { } eventKind)
                {
                    if (!Enum.IsDefined(eventKind))
                        errors.Add(new ContentDiagnostic(stageSource, $"unknown completion event '{eventKind}'"));
                    else if (eventKind == QuestEventKind.Interaction
                        && stage.TargetWorldInstanceId is null && stage.TargetActorId is null)
                        errors.Add(new ContentDiagnostic(stageSource,
                            "an interaction objective needs a stable actor or world-instance target"));
                    else if (eventKind == QuestEventKind.ActorKilled
                        && stage.TargetActorId is null && stage.TargetWorldInstanceId is null)
                        errors.Add(new ContentDiagnostic(stageSource,
                            "an actor-killed objective needs an actor or world-instance target"));
                    else if (eventKind == QuestEventKind.ItemCollected
                        && stage.RequiredItemId is null && stage.TargetWorldInstanceId is null)
                        errors.Add(new ContentDiagnostic(stageSource,
                            "an item-collected objective needs an item or world-instance target"));
                }
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
        {
            AddReferenceError(registry.CheckReference(
                $"world item '{entry.WorldInstanceId}'", entry.ItemId), errors);
            if (entry.OwnerFactionId is { } ownerFactionId)
                AddReferenceError(registry.CheckReference(
                    $"world item '{entry.WorldInstanceId}'.OwnerFactionId", ownerFactionId), errors);
        }
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
    private static JsonSerializerOptions CreateJsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { FlagValueJson.Instance, new JsonStringEnumConverter() }
    };

    public static RpgContentSet FromJson(string json)
    {
        var result = ParseForValidation(json);
        if (result.Diagnostics.Count > 0)
            throw new InvalidDataException("RPG content validation failed: " + string.Join(" ", result.Diagnostics));
        return result.Content;
    }

    /// <summary>Parses a content pack and returns every semantic diagnostic without rejecting the parsed records.</summary>
    public static RpgContentValidationResult ParseForValidation(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var document = JsonSerializer.Deserialize<ContentDocument>(json, CreateJsonOptions())
            ?? throw new JsonException("The RPG content document was empty.");

        var actors = new ActorCatalogue();
        foreach (var actor in document.Actors ?? new List<ActorDef>()) actors.Add(actor);
        var items = new ItemCatalogue();
        foreach (var item in document.Items ?? new List<ItemDef>()) items.Add(item);
        var factions = new FactionCatalogue();
        foreach (var faction in document.Factions ?? new List<FactionDef>()) factions.Add(faction);
        var spells = new SpellCatalogue();
        foreach (var spell in document.Spells ?? new List<TargetedSpellDef>()) spells.Add(spell);
        var skillProgression = new SkillProgressionCatalogue();
        foreach (var rule in document.SkillUseRules ?? new List<SkillUseRule>()) skillProgression.Add(rule);
        var quests = new QuestCatalogue();
        foreach (var quest in document.Quests ?? new List<QuestDef>()) quests.Add(quest);
        var content = new RpgContentSet
        {
            Actors = actors,
            Items = items,
            Factions = factions,
            Spells = spells,
            SkillProgression = skillProgression,
            Dialogues = document.Dialogues is null ? Array.Empty<DialogueTree>() : document.Dialogues,
            Quests = quests
        };
        return new RpgContentValidationResult(content, content.Validate());
    }

    public static RpgContentSet Load(string path) => FromJson(File.ReadAllText(path));

    public static RpgContentValidationResult LoadForValidation(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return ParseForValidation(File.ReadAllText(path));
    }

    public static string ToJson(RpgContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var errors = content.Validate();
        if (errors.Count > 0)
            throw new InvalidDataException("RPG content validation failed: " + string.Join(" ", errors));

        return SerializeContent(content);
    }

    /// <summary>Serializes an editor recovery draft, even when it has semantic validation errors.</summary>
    public static string ToJsonForRecovery(RpgContentSet content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return SerializeContent(content);
    }

    private static string SerializeContent(RpgContentSet content)
    {
        return JsonSerializer.Serialize(new ContentDocument
        {
            Actors = content.Actors.All.Values.OrderBy(value => value.Id.Value, StringComparer.Ordinal).ToList(),
            Items = content.Items.All.Values.OrderBy(value => value.Id.Value, StringComparer.Ordinal).ToList(),
            Factions = content.Factions.All.Values.OrderBy(value => value.Id.Value, StringComparer.Ordinal).ToList(),
            Spells = content.Spells.All.Values.OrderBy(value => value.Id.Value, StringComparer.Ordinal).ToList(),
            SkillUseRules = content.SkillProgression.All.Values.OrderBy(value => value.ActionId, StringComparer.Ordinal).ToList(),
            Dialogues = content.Dialogues.OrderBy(value => value.Id.Value, StringComparer.Ordinal).ToList(),
            Quests = content.Quests.All().OrderBy(value => value.Id.Value, StringComparer.Ordinal).ToList()
        }, CreateJsonOptions());
    }

    public static void SaveAtomic(string path, RpgContentSet content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = ToJson(content);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidDataException("RPG content path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
            else File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private sealed class ContentDocument
    {
        public ContentDocument() { }
        public List<ActorDef>? Actors { get; init; }
        public List<ItemDef>? Items { get; init; }
        public List<FactionDef>? Factions { get; init; }
        public List<TargetedSpellDef>? Spells { get; init; }
        public List<SkillUseRule>? SkillUseRules { get; init; }
        public List<DialogueTree>? Dialogues { get; init; }
        public List<QuestDef>? Quests { get; init; }
    }
}
