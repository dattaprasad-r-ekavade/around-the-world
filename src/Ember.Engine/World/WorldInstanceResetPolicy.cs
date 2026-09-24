namespace Ember.World;

/// <summary>Controls whether one world instance returns to its authored state on an explicit cell reset.</summary>
public enum WorldInstanceResetPolicy
{
    Preserve = 0,
    ResetOnCellReset = 1,
    QuestPersistent = 2
}
