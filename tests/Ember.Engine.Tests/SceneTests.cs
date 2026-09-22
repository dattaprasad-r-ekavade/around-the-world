using System;
using Ember.Scene;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneTests
{
    [Fact]
    public void AddFindAndRemoveUseStableIds()
    {
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var scene = new SceneGraph();
        var one = new SceneObject(id, "one");

        scene.Add(one);

        Assert.Same(one, scene.Find(id));
        Assert.True(scene.Remove(id));
        Assert.Null(scene.Find(id));
        Assert.False(scene.Remove(id));
    }

    [Fact]
    public void DuplicateIdsAreRejected()
    {
        var id = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var scene = new SceneGraph();
        scene.Add(new SceneObject(id, "one"));

        Assert.Throws<ArgumentException>(() => scene.Add(new SceneObject(id, "two")));
    }

    [Fact]
    public void ParentWorldTransformMovesChild()
    {
        var parentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var childId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var scene = new SceneGraph();
        var parent = new SceneObject(parentId, "parent")
        {
            Transform = new Transform { Position = new Vector3(10f, 0f, 0f) }
        };
        var child = new SceneObject(childId, "child")
        {
            Transform = new Transform { Position = new Vector3(2f, 0f, 0f) }
        };
        scene.Add(parent);
        scene.Add(child);

        scene.SetParent(childId, parentId);

        var world = scene.GetWorldMatrix(childId);
        AssertClose(new Vector3(12f, 0f, 0f), Vector3.Transform(Vector3.Zero, world));
    }

    [Fact]
    public void ParentRotationAffectsChildWorldPosition()
    {
        var parentId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var childId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var scene = new SceneGraph();
        scene.Add(new SceneObject(parentId, "parent")
        {
            Transform = new Transform
            {
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2)
            }
        });
        scene.Add(new SceneObject(childId, "child")
        {
            Transform = new Transform { Position = new Vector3(0f, 0f, -2f) }
        });

        scene.SetParent(childId, parentId);

        var world = scene.GetWorldMatrix(childId);
        AssertClose(new Vector3(-2f, 0f, 0f), Vector3.Transform(Vector3.Zero, world));
    }

    [Fact]
    public void SelfAndAncestorCyclesAreRejected()
    {
        var a = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var b = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var c = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var scene = new SceneGraph();
        scene.Add(new SceneObject(a, "a"));
        scene.Add(new SceneObject(b, "b"));
        scene.Add(new SceneObject(c, "c"));

        Assert.Throws<InvalidOperationException>(() => scene.SetParent(a, a));
        scene.SetParent(b, a);
        scene.SetParent(c, b);
        Assert.Throws<InvalidOperationException>(() => scene.SetParent(a, c));
    }

    [Fact]
    public void RemovingParentDetachesChildren()
    {
        var parentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var childId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var scene = new SceneGraph();
        scene.Add(new SceneObject(parentId, "parent"));
        scene.Add(new SceneObject(childId, "child"));
        scene.SetParent(childId, parentId);

        Assert.True(scene.Remove(parentId));
        Assert.Null(scene.Find(parentId));
        Assert.Null(scene.Find(childId)!.ParentId);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f,
            $"Expected {expected}, got {actual}");
    }
}
