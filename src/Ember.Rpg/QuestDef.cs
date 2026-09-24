using System;
using System.Collections.Generic;

namespace Ember.Rpg;

/// <summary>Where a quest has got to, derived from the flags rather than stored.</summary>
public enum QuestStatus
{
    NotStarted,
    Active,
    Complete
}

/// <summary>One step of a quest: an id, the journal line it shows, and the flags that finish it.</summary>
public sealed record QuestStage
{
    public string Id { get; init; } = "";

    public ContentId<ActorContentKind>? TargetActorId { get; init; }

    public ContentId<ItemContentKind>? RequiredItemId { get; init; }

    /// <summary>What the journal says while this is the current stage.</summary>
    public string Journal { get; init; } = "";

    /// <summary>
    /// Every flag condition that must hold before this stage counts as done — ANDed, empty
    /// meaning the stage never finishes on its own (a stage you only leave by another path).
    /// </summary>
    public IReadOnlyList<FlagCondition> DoneWhen { get; init; } = Array.Empty<FlagCondition>();

    public bool IsDone(FlagStore flags)
    {
        if (DoneWhen is not { Count: > 0 }) return false;
        foreach (var condition in DoneWhen)
            if (!condition.Matches(flags))
                return false;
        return true;
    }
}

/// <summary>
/// A quest as content: a title and an ordered list of stages. Progress is never stored on the
/// quest or the save — it is derived from the flag store every time you ask, so a flag set
/// from anywhere (dialogue, loot, console) moves the quest, and a save only has to round-trip
/// the flags it already carries.
///
/// The start flag is <c>quest.{id}.started</c>; stages run in order, each finishing when its
/// DoneWhen holds; the quest completes when every stage has.
/// </summary>
public sealed record QuestDef
{
    public ContentId<QuestContentKind> Id { get; init; }

    public string Title { get; init; } = "";

    public ContentId<DialogueContentKind>? StartDialogueId { get; init; }

    public IReadOnlyList<QuestStage> Stages { get; init; } = Array.Empty<QuestStage>();

    public string StartFlag() => $"quest.{Id.Value}.started";

    public void Start(FlagStore flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        flags.Set(StartFlag(), true);
    }

    public QuestStatus StatusIn(FlagStore flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        if (!flags.GetBool(StartFlag())) return QuestStatus.NotStarted;
        return StageIn(flags) is null ? QuestStatus.Complete : QuestStatus.Active;
    }

    /// <summary>
    /// The first unfinished stage once started; null before the start flag and when every
    /// stage has finished (which is what StatusIn reads as Complete).
    /// </summary>
    public QuestStage? StageIn(FlagStore flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        if (!flags.GetBool(StartFlag())) return null;

        foreach (var stage in Stages)
            if (!stage.IsDone(flags))
                return stage;
        return null;
    }
}
