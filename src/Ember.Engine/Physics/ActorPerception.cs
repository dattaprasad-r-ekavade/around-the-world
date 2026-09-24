using System;
using Microsoft.Xna.Framework;

namespace Ember.Physics;

/// <summary>Range and physics line-of-sight result for one observer/target pair.</summary>
public readonly record struct ActorPerceptionResult(float Distance, bool InRange, bool HasLineOfSight)
{
    public bool CanSee => InRange && HasLineOfSight;
}

/// <summary>Small physics-backed sight query for gameplay AI.</summary>
public static class ActorPerception
{
    private const float TargetEndpointEpsilon = 0.02f;

    public static ActorPerceptionResult Evaluate(PhysicsWorld world, Vector3 observerPosition,
        Vector3 targetPosition, float sightRange)
    {
        ArgumentNullException.ThrowIfNull(world);
        ValidateFinite(observerPosition, nameof(observerPosition));
        ValidateFinite(targetPosition, nameof(targetPosition));
        if (!float.IsFinite(sightRange) || sightRange <= 0f)
            throw new ArgumentOutOfRangeException(nameof(sightRange), "Sight range must be finite and positive.");

        var offset = targetPosition - observerPosition;
        var distance = offset.Length();
        if (!float.IsFinite(distance))
            throw new ArgumentOutOfRangeException(nameof(targetPosition), "Observer-to-target distance must be finite.");
        if (distance > sightRange)
            return new ActorPerceptionResult(distance, false, false);
        if (distance <= TargetEndpointEpsilon)
            return new ActorPerceptionResult(distance, true, true);

        // Stop just before the target so its own collision shape cannot occlude itself.
        var queryDistance = distance - TargetEndpointEpsilon;
        var obstruction = world.Raycast(observerPosition, offset, queryDistance, PhysicsCollisionLayer.World);
        return new ActorPerceptionResult(distance, true, obstruction is null);
    }

    private static void ValidateFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(parameterName, "Perception positions must be finite.");
    }
}
