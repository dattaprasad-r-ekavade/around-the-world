using System;
using Ember.Physics;
using Ember.Scene;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneStaticColliderSetTests
{
    [Fact]
    public void BuildsScaledHierarchyCollidersSkipsTriggersAndDisabledObjectsAndRemovesItsSnapshot()
    {
        var scene = new SceneGraph();
        var parent = new SceneObject(Guid.NewGuid(), "Scaled room")
        {
            Transform = new Transform
            {
                Position = new Vector3(2f, 0f, 0f),
                Scale = new Vector3(2f, 1f, 1f)
            }
        };
        var wall = new SceneObject(Guid.NewGuid(), "Wall")
        {
            Transform = new Transform { Position = new Vector3(1f, 0f, 0f) },
            BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, new Vector3(2f, 2f, 2f))
        };
        var trigger = new SceneObject(Guid.NewGuid(), "Trigger")
        {
            Transform = new Transform { Position = new Vector3(20f, 0f, 0f) },
            BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One, isTrigger: true)
        };
        var disabled = new SceneObject(Guid.NewGuid(), "Disabled wall")
        {
            Enabled = false,
            Transform = new Transform { Position = new Vector3(40f, 0f, 0f) },
            BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One)
        };
        scene.Add(parent);
        scene.Add(wall);
        scene.Add(trigger);
        scene.Add(disabled);
        scene.SetParent(wall.Id, parent.Id);
        using var physics = new PhysicsWorld();

        var colliders = new SceneStaticColliderSet(scene, physics);

        Assert.Equal(1, colliders.Count);
        Assert.Equal(1, colliders.SkippedTriggerCount);
        Assert.NotNull(physics.Raycast(new Vector3(5.5f, 5f, 0f), -Vector3.Up, 10f));
        Assert.Null(physics.Raycast(new Vector3(6.1f, 5f, 0f), -Vector3.Up, 10f));

        colliders.Dispose();
        colliders.Dispose();

        Assert.True(colliders.IsDisposed);
        Assert.Null(physics.Raycast(new Vector3(5.5f, 5f, 0f), -Vector3.Up, 10f));
    }

    [Fact]
    public void RejectsShearedWorldTransformWithOwningObjectDiagnostic()
    {
        var scene = new SceneGraph();
        var parent = new SceneObject(Guid.NewGuid(), "Scaled parent")
        {
            Transform = new Transform { Scale = new Vector3(2f, 1f, 1f) }
        };
        var child = new SceneObject(Guid.NewGuid(), "Rotated collider")
        {
            Transform = new Transform
            {
                Rotation = Quaternion.CreateFromYawPitchRoll(0.4f, 0f, 0f)
            },
            BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One)
        };
        scene.Add(parent);
        scene.Add(child);
        scene.SetParent(child.Id, parent.Id);
        using var physics = new PhysicsWorld();

        var error = Assert.Throws<InvalidOperationException>(() =>
            new SceneStaticColliderSet(scene, physics));

        Assert.Contains("Rotated collider", error.Message, StringComparison.Ordinal);
        Assert.Contains(child.Id.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Null(physics.Raycast(new Vector3(0f, 5f, 0f), -Vector3.Up, 10f));
    }
}
