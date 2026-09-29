using System;
using System.Collections.Generic;
using Ember.Scene;
using Microsoft.Xna.Framework;

namespace Ember.Physics;

/// <summary>
/// Builds a removable snapshot of enabled, non-trigger scene box colliders in one physics world.
/// Rebuild this set after changing scene transforms or collider components.
/// </summary>
public sealed class SceneStaticColliderSet : IDisposable
{
    private readonly PhysicsWorld _physicsWorld;
    private readonly List<PhysicsObjectId> _colliderIds = new();
    private bool _disposed;

    public SceneStaticColliderSet(SceneGraph scene, PhysicsWorld physicsWorld)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _physicsWorld = physicsWorld ?? throw new ArgumentNullException(nameof(physicsWorld));

        try
        {
            foreach (var sceneObject in scene.Objects)
            {
                if (sceneObject.BoxCollider is not { } collider || !IsEffectivelyEnabled(scene, sceneObject))
                    continue;
                if (collider.IsTrigger)
                {
                    SkippedTriggerCount++;
                    continue;
                }

                try
                {
                    var (center, size, orientation) = ResolveWorldBox(scene, sceneObject, collider);
                    _colliderIds.Add(_physicsWorld.AddStaticBox(center, size, orientation));
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    throw new InvalidOperationException(
                        $"Cannot create the box collider for '{sceneObject.Name}' ({sceneObject.Id}): {exception.Message}",
                        exception);
                }
            }
        }
        catch
        {
            RemoveCreatedColliders();
            throw;
        }
    }

    public int Count => _colliderIds.Count;

    /// <summary>Number of trigger components skipped because trigger overlaps are not wired yet.</summary>
    public int SkippedTriggerCount { get; }

    public bool IsDisposed => _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_physicsWorld.IsDisposed)
        {
            _colliderIds.Clear();
            return;
        }
        RemoveCreatedColliders();
    }

    private void RemoveCreatedColliders()
    {
        List<Exception>? failures = null;
        for (var index = _colliderIds.Count - 1; index >= 0; index--)
        {
            try
            {
                if (!_physicsWorld.IsDisposed)
                    _physicsWorld.RemoveStatic(_colliderIds[index]);
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>()).Add(exception);
            }
        }
        _colliderIds.Clear();
        if (failures is not null)
            throw new AggregateException("One or more scene colliders could not be removed from the physics world.", failures);
    }

    private static bool IsEffectivelyEnabled(SceneGraph scene, SceneObject sceneObject)
    {
        var current = sceneObject;
        while (true)
        {
            if (!current.Enabled) return false;
            if (current.ParentId is not { } parentId) return true;
            current = scene.Find(parentId)
                ?? throw new InvalidOperationException(
                    $"Scene object '{sceneObject.Name}' ({sceneObject.Id}) has a missing parent {parentId}.");
        }
    }

    private static (Vector3 Center, Vector3 Size, Quaternion Orientation) ResolveWorldBox(
        SceneGraph scene, SceneObject sceneObject, SceneBoxColliderComponent collider)
    {
        var world = scene.GetWorldMatrix(sceneObject.Id);
        if (!IsFinite(world) || !world.Decompose(out var scale, out var orientation, out _))
            throw new InvalidOperationException("The world transform is not a finite box transform.");

        var reconstructed = Matrix.CreateScale(scale)
            * Matrix.CreateFromQuaternion(orientation)
            * Matrix.CreateTranslation(world.Translation);
        if (!NearlyEqual(world, reconstructed))
            throw new InvalidOperationException(
                "The world transform contains shear; box colliders require a non-sheared scale and rotation.");
        if (!IsFinite(scale) || !IsFinite(orientation) || orientation.LengthSquared() < 1e-8f)
            throw new InvalidOperationException("The world transform has an invalid scale or rotation.");

        var size = new Vector3(
            MathF.Abs(scale.X) * collider.Size.X,
            MathF.Abs(scale.Y) * collider.Size.Y,
            MathF.Abs(scale.Z) * collider.Size.Z);
        if (size.X <= 0f || size.Y <= 0f || size.Z <= 0f || !IsFinite(size))
            throw new InvalidOperationException("The world box collider dimensions must be finite and positive.");

        var center = Vector3.Transform(collider.Center, world);
        return (center, size, Quaternion.Normalize(orientation));
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Quaternion value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y)
        && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static bool IsFinite(Matrix value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14)
        && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24)
        && float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
        && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);

    private static bool NearlyEqual(Matrix left, Matrix right) =>
        Near(left.M11, right.M11) && Near(left.M12, right.M12) && Near(left.M13, right.M13) && Near(left.M14, right.M14)
        && Near(left.M21, right.M21) && Near(left.M22, right.M22) && Near(left.M23, right.M23) && Near(left.M24, right.M24)
        && Near(left.M31, right.M31) && Near(left.M32, right.M32) && Near(left.M33, right.M33) && Near(left.M34, right.M34)
        && Near(left.M41, right.M41) && Near(left.M42, right.M42) && Near(left.M43, right.M43) && Near(left.M44, right.M44);

    private static bool Near(float left, float right) =>
        MathF.Abs(left - right) <= 1e-4f * MathF.Max(1f, MathF.Max(MathF.Abs(left), MathF.Abs(right)));
}
