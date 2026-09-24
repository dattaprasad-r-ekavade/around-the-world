using System;
using System.Numerics;

namespace Ember.Rpg;

public enum EnemyCombatAiState
{
    Idle,
    Chasing,
    Attacking,
    Dead
}

/// <summary>Short-lived target and mode memory for one enemy decision loop.</summary>
public sealed record EnemyCombatAiMemory
{
    public EnemyCombatAiState State { get; init; }
    public Guid? TargetWorldInstanceId { get; init; }
}

/// <summary>One deterministic AI decision and any combat-state changes it produced.</summary>
public sealed record EnemyCombatAiDecision(
    EnemyCombatAiMemory Memory,
    ActorRuntimeState Enemy,
    ActorRuntimeState? Target,
    Vector3 DesiredMoveDirection,
    bool AttackLanded);

/// <summary>
/// Converts one physics perception result into a chase/attack intent. A game loop applies
/// DesiredMoveDirection to its character controller and supplies fresh perception next frame.
/// </summary>
public static class EnemyCombatAi
{
    public static EnemyCombatAiDecision Tick(EnemyCombatAiMemory memory, ActorRuntimeState enemy,
        ActorRuntimeState? target, Vector3 enemyPosition, Vector3 targetPosition, bool canSeeTarget,
        MeleeAttackProfile attackProfile)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(enemy);
        ArgumentNullException.ThrowIfNull(attackProfile);
        enemy.Validate();
        attackProfile.Validate();
        if (!Enum.IsDefined(memory.State))
            throw new ArgumentOutOfRangeException(nameof(memory), "Enemy AI memory has an unknown state.");
        if (memory.TargetWorldInstanceId == Guid.Empty)
            throw new ArgumentException("Enemy AI target ID cannot be empty.", nameof(memory));
        ValidateFinite(enemyPosition, nameof(enemyPosition));

        if (enemy.IsDead)
            return Stop(EnemyCombatAiState.Dead, enemy, target);
        if (target is null)
            return Stop(EnemyCombatAiState.Idle, enemy, null);
        target.Validate();
        ValidateFinite(targetPosition, nameof(targetPosition));
        if (target.IsDead || target.WorldInstanceId == enemy.WorldInstanceId || !canSeeTarget)
            return Stop(EnemyCombatAiState.Idle, enemy, target);

        var offset = targetPosition - enemyPosition;
        var distance = offset.Length();
        if (!float.IsFinite(distance))
            throw new ArgumentOutOfRangeException(nameof(targetPosition), "Enemy-to-target distance must be finite.");

        var tracked = new EnemyCombatAiMemory
        {
            TargetWorldInstanceId = target.WorldInstanceId
        };
        if (distance > attackProfile.Range)
        {
            offset.Y = 0f;
            var direction = offset.LengthSquared() > 1e-8f ? Vector3.Normalize(offset) : Vector3.Zero;
            return new EnemyCombatAiDecision(tracked with { State = EnemyCombatAiState.Chasing },
                enemy, target, direction, false);
        }

        var attacked = MeleeCombat.TryAttack(enemy, target, distance, attackProfile,
            out var updatedEnemy, out var updatedTarget);
        return new EnemyCombatAiDecision(tracked with { State = EnemyCombatAiState.Attacking },
            updatedEnemy, updatedTarget, Vector3.Zero, attacked);
    }

    private static EnemyCombatAiDecision Stop(EnemyCombatAiState state,
        ActorRuntimeState enemy, ActorRuntimeState? target) => new(
        new EnemyCombatAiMemory { State = state }, enemy, target, Vector3.Zero, false);

    private static void ValidateFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(parameterName, "AI positions must be finite.");
    }
}
