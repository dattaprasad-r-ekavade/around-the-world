using System;
using System.Collections.Generic;

namespace Ember.Rpg;

/// <summary>A stable target-instance gameplay fact emitted after its source action commits.</summary>
public sealed record QuestEvent
{
    public Guid EventId { get; init; }
    public QuestEventKind Kind { get; init; }
    public Guid WorldInstanceId { get; init; }
    public ContentId<ActorContentKind>? ActorId { get; init; }
    public ContentId<ItemContentKind>? ItemId { get; init; }

    public void Validate()
    {
        if (EventId == Guid.Empty) throw new ArgumentException("A quest event needs a stable event ID.");
        if (!Enum.IsDefined(Kind)) throw new ArgumentOutOfRangeException(nameof(Kind));
        if (WorldInstanceId == Guid.Empty) throw new ArgumentException("A quest event needs a stable world-instance ID.");
        if (Kind == QuestEventKind.ActorKilled && ActorId is null)
            throw new ArgumentException("An actor-killed event needs an actor content ID.");
        if (Kind == QuestEventKind.ItemCollected && ItemId is null)
            throw new ArgumentException("An item-collected event needs an item content ID.");
    }
}

/// <summary>Turns committed world events into persistent completion flags for the active quest stage.</summary>
public static class QuestEventSystem
{
    public static int Apply(QuestCatalogue quests, FlagStore flags, QuestEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(quests);
        ArgumentNullException.ThrowIfNull(flags);
        ArgumentNullException.ThrowIfNull(gameEvent);
        gameEvent.Validate();
        var eventFlag = $"quest.event.{gameEvent.EventId:N}.processed";
        if (flags.GetBool(eventFlag)) return 0;
        var completed = 0;
        foreach (var quest in quests.All())
        {
            if (quest.StatusIn(flags) != QuestStatus.Active || quest.StageIn(flags) is not { } stage
                || stage.CompleteOn != gameEvent.Kind)
                continue;
            if (stage.TargetWorldInstanceId is { } worldId && worldId != gameEvent.WorldInstanceId) continue;
            if (stage.TargetActorId is { } actorId && gameEvent.ActorId != actorId) continue;
            if (stage.RequiredItemId is { } itemId && gameEvent.ItemId != itemId) continue;
            var flag = stage.CompletionFlag(quest.Id);
            if (flags.GetBool(flag)) continue;
            flags.Set(flag, true);
            completed++;
        }
        flags.Set(eventFlag, true);
        return completed;
    }
}
