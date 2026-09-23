using System;
using System.Numerics;
using XnaQuaternion = Microsoft.Xna.Framework.Quaternion;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;

namespace Ember.Physics;

/// <summary>A stable engine-side identifier for a static or dynamic physics object.</summary>
public readonly record struct PhysicsObjectId(int Value);

/// <summary>One physics pose expressed in the engine's MonoGame math types.</summary>
public readonly record struct PhysicsPose(XnaVector3 Position, XnaQuaternion Orientation);

[Flags]
public enum PhysicsCollisionLayer : uint
{
    None = 0,
    World = 1u << 0,
    Dynamic = 1u << 1,
    Player = 1u << 2,
    Interaction = 1u << 3,
    All = uint.MaxValue
}

/// <summary>Category and mask used for contact filtering and ray query filtering.</summary>
public readonly record struct PhysicsCollisionFilter(
    PhysicsCollisionLayer BelongsTo,
    PhysicsCollisionLayer CollidesWith)
{
    public static PhysicsCollisionFilter DefaultWorld => new(PhysicsCollisionLayer.World, PhysicsCollisionLayer.All);
    public static PhysicsCollisionFilter DefaultDynamic => new(PhysicsCollisionLayer.Dynamic, PhysicsCollisionLayer.All);

    public bool IsIncludedIn(PhysicsCollisionLayer mask) => (BelongsTo & mask) != 0;

    public bool AllowsCollisionWith(PhysicsCollisionFilter other) =>
        (BelongsTo & other.CollidesWith) != 0
        && (other.BelongsTo & CollidesWith) != 0;
}

public readonly record struct PhysicsRaycastHit(
    PhysicsObjectId ObjectId,
    XnaVector3 Position,
    XnaVector3 Normal,
    float Distance,
    PhysicsCollisionLayer Layer);

/// <summary>Explicit conversions across MonoGame's XNA and BEPU's System.Numerics types.</summary>
public static class PhysicsConversions
{
    public static Vector3 ToNumerics(XnaVector3 value) => new(value.X, value.Y, value.Z);
    public static XnaVector3 ToXna(Vector3 value) => new(value.X, value.Y, value.Z);

    public static Quaternion ToNumerics(XnaQuaternion value) => new(value.X, value.Y, value.Z, value.W);
    public static XnaQuaternion ToXna(Quaternion value) => new(value.X, value.Y, value.Z, value.W);
}
