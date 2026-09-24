using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ember.Rpg;

/// <summary>
/// Every quest a game ships, loaded from one JSON document shaped
/// <c>{ "Quests": [ ... ] }</c>. Content only — status and stage come from the flags
/// (<see cref="QuestDef.StatusIn"/>), so this never writes into a save.
/// </summary>
public sealed class QuestCatalogue
{
    private readonly Dictionary<ContentId<QuestContentKind>, QuestDef> _quests = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public int Count => _quests.Count;

    /// <summary>Upsert by id; last write wins.</summary>
    public void Add(QuestDef quest)
    {
        ArgumentNullException.ThrowIfNull(quest);
        if (string.IsNullOrWhiteSpace(quest.Id.Value))
            throw new ArgumentException("A quest needs an id.", nameof(quest));
        _quests[quest.Id] = quest;
    }

    public bool TryGet(ContentId<QuestContentKind> id, out QuestDef quest)
    {
        return _quests.TryGetValue(id, out quest!);
    }

    public bool TryGet(string id, out QuestDef quest) => TryGet(new ContentId<QuestContentKind>(id), out quest);

    public QuestDef? Get(ContentId<QuestContentKind> id) => TryGet(id, out var quest) ? quest : null;
    public QuestDef? Get(string id) => Get(new ContentId<QuestContentKind>(id));

    public IReadOnlyList<QuestDef> All()
    {
        var list = new List<QuestDef>(_quests.Count);
        foreach (var quest in _quests.Values) list.Add(quest);
        return list;
    }

    public static QuestCatalogue FromJson(string json)
    {
        var root = JsonSerializer.Deserialize<QuestRoot>(json, Json)
            ?? throw new JsonException("The quest document was empty.");

        var catalogue = new QuestCatalogue();
        foreach (var quest in root.Quests ?? new List<QuestDef>())
            catalogue.Add(quest);
        return catalogue;
    }

    public static QuestCatalogue Load(string path) => FromJson(File.ReadAllText(path));

    private sealed class QuestRoot
    {
        public List<QuestDef>? Quests { get; init; }
    }
}
