using System;
using System.Collections.Generic;
using Ember.Physics;
using Ember.Scene;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneStaticColliderSetTests
{
    [Fact]
    public void BuildsScaledHierarchyCollidersAndRemovesItsSnapshot()
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

        Assert.Equal(2, colliders.Count);
        Assert.Equal(1, colliders.TriggerCount);
        Assert.NotNull(physics.Raycast(new Vector3(5.5f, 5f, 0f), -Vector3.Up, 10f));
        Assert.Null(physics.Raycast(new Vector3(6.1f, 5f, 0f), -Vector3.Up, 10f));

        colliders.Dispose();
        colliders.Dispose();

        Assert.True(colliders.IsDisposed);
        Assert.Null(physics.Raycast(new Vector3(5.5f, 5f, 0f), -Vector3.Up, 10f));
    }

    [Fact]
    public void CanExcludeSceneObjectsThatHaveDynamicCharacterBodies()
    {
        var actorId = Guid.NewGuid();
        var scene = new SceneGraph();
        scene.Add(new SceneObject(actorId, "Player body")
        {
            BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One)
        });
        scene.Add(new SceneObject(Guid.NewGuid(), "Wall")
        {
            Transform = new Transform { Position = new Vector3(5f, 0f, 0f) },
            BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One)
        });
        using var physics = new PhysicsWorld();

        using var colliders = new SceneStaticColliderSet(scene, physics, new HashSet<Guid> { actorId });

        Assert.Equal(1, colliders.Count);
        Assert.Null(physics.Raycast(new Vector3(0f, 3f, 0f), -Vector3.Up, 5f));
        Assert.NotNull(physics.Raycast(new Vector3(5f, 3f, 0f), -Vector3.Up, 5f));
    }

    [Fact]
    public void TriggerReportsEnterAndExitWithoutBlockingTheCharacter()
    {
        var triggerSceneObjectId = Guid.NewGuid();
        var scene = new SceneGraph();
        scene.Add(new SceneObject(triggerSceneObjectId, "Goal trigger")
        {
            BoxCollider = new SceneBoxColliderComponent(
                Vector3.Zero, new Vector3(2f, 2f, 2f), isTrigger: true),
            Transform = new Transform { Position = new Vector3(0f, 1f, 0f) }
        });
        using var physics = new PhysicsWorld(Vector3.Zero);
        using var colliders = new SceneStaticColliderSet(scene, physics);
        using var player = new PhysicsCharacterController(physics, new Vector3(-3.5f, 1f, 0f));
        var observedEvents = new System.Collections.Generic.List<SceneTriggerEvent>();
        player.SetMoveInput(Vector3.Right);

        for (var step = 0; step < 72; step++)
        {
            physics.Step(1f / 60f);
            observedEvents.AddRange(colliders.TriggerEvents);
        }

        Assert.True(player.Pose.Position.X > 1f);
        Assert.Collection(observedEvents,
            entered =>
            {
                Assert.Equal(triggerSceneObjectId, entered.TriggerSceneObjectId);
                Assert.Equal(player.PhysicsBodyId, entered.OtherPhysicsObjectId);
                Assert.Equal(PhysicsTriggerTransition.Entered, entered.Transition);
            },
            exited =>
            {
                Assert.Equal(triggerSceneObjectId, exited.TriggerSceneObjectId);
                Assert.Equal(player.PhysicsBodyId, exited.OtherPhysicsObjectId);
                Assert.Equal(PhysicsTriggerTransition.Exited, exited.Transition);
            });
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
