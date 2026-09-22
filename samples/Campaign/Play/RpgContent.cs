using System;
using System.Collections.Generic;
using System.IO;
using Ember.Rpg;

namespace Campaign;

/// <summary>
/// The RPG layer's content pack: the inn conversation and the quest catalogue, loaded once
/// from Content/Rpg. Journal lines are derived from the flag store, never stored.
/// </summary>
public static class RpgContent
{
    public static DialogueTree? Inn { get; private set; }

    public static QuestCatalogue Quests { get; private set; } = new();

    public static void Load()
    {
        Inn = null;
        Quests = new QuestCatalogue();
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Content", "Rpg");
            var talk = Path.Combine(dir, "inn-talk.json");
            if (File.Exists(talk)) Inn = DialogueTree.Load(talk);
            var quests = Path.Combine(dir, "relic-quest.json");
            if (File.Exists(quests)) Quests = QuestCatalogue.Load(quests);
        }
        catch
        {
            Inn = null;
            Quests = new QuestCatalogue();
        }
    }

    /// <summary>
    /// Journal rows for every quest the flags have started, in catalogue order. The body is
    /// the current stage's journal line (or the last stage's when complete), run through
    /// <paramref name="text"/> for placeholders like {dungeon}.
    /// </summary>
    public static IReadOnlyList<QuestNote> JournalNotes(FlagStore flags, Func<string, string> text)
    {
        var notes = new List<QuestNote>();
        foreach (var quest in Quests.All())
        {
            var status = quest.StatusIn(flags);
            if (status == QuestStatus.NotStarted) continue;
            var stage = quest.StageIn(flags);
            var body = stage?.Journal
                ?? (quest.Stages.Count > 0 ? quest.Stages[^1].Journal : quest.Title);
            notes.Add(new QuestNote(quest.Title, text(body), status == QuestStatus.Complete));
        }
        return notes;
    }
}
