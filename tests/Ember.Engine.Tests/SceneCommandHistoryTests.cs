using System;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneCommandHistoryTests
{
    [Fact]
    public void TransformEditUndoesAndRedoesAndNewEditClearsRedo()
    {
        var id = Guid.NewGuid();
        var scene = new SceneGraph();
        var item = new SceneObject(id, "Object");
        scene.Add(item);
        var history = new SceneCommandHistory();
        var before = new Transform { Position = new Vector3(1f, 2f, 3f) };
        var after = new Transform
        {
            Position = new Vector3(4f, 5f, 6f),
            Rotation = Quaternion.CreateFromYawPitchRoll(0.2f, 0.3f, 0.4f),
            Scale = new Vector3(2f, 3f, 4f)
        };

        history.Execute(scene, new TransformEditCommand(id, before, after));
        Assert.Equal(after.Position, item.Transform.Position);
        Assert.True(history.Undo(scene));
        Assert.Equal(before.Position, item.Transform.Position);
        Assert.Equal(before.Rotation, item.Transform.Rotation);
        Assert.Equal(before.Scale, item.Transform.Scale);
        Assert.True(history.Redo(scene));
        Assert.Equal(after.Position, item.Transform.Position);
        Assert.Equal(after.Rotation, item.Transform.Rotation);
        Assert.Equal(after.Scale, item.Transform.Scale);

        Assert.True(history.Undo(scene));
        var replacement = new Transform { Position = new Vector3(8f, 0f, 0f) };
        history.Execute(scene, new TransformEditCommand(id, before, replacement));
        Assert.False(history.CanRedo);
        Assert.False(history.Redo(scene));
        Assert.Equal(replacement.Position, item.Transform.Position);
    }

    [Fact]
    public void CreateCommandUndoAndRedoKeepsTheSameStableId()
    {
        var scene = new SceneGraph();
        var history = new SceneCommandHistory();
        var item = new SceneObject(Guid.NewGuid(), "Added")
        {
            Transform = new Transform { Position = new Vector3(7f, 0f, 0f) }
        };

        history.Execute(scene, new CreateSceneObjectCommand(item));
        Assert.Equal(item.Transform.Position, scene.Find(item.Id)!.Transform.Position);
        Assert.True(history.Undo(scene));
        Assert.Null(scene.Find(item.Id));
        Assert.True(history.Redo(scene));
        Assert.Equal(item.Id, Assert.Single(scene.Objects).Id);
    }

    [Fact]
    public void DuplicateGetsFreshIdsAndKeepsInstanceReferencesAndSettings()
    {
        var scene = new SceneGraph();
        var asset = new GltfAssetReference(Guid.NewGuid(), "Assets/Fox.glb");
        var source = new SceneObject(Guid.NewGuid(), "Fox")
        {
            GltfAsset = asset,
            Transform = new Transform { Position = new Vector3(12f, 0f, -4f) },
            CharacterSettings = new GltfCharacterSettings
            {
                ClipName = "Walk",
                Time = 0.4f,
                Speed = 1.25f,
                IsPlaying = true
            }
        };
        var sourceAttachmentId = Guid.NewGuid();
        source.CharacterSettings.Attachments.Add(new GltfBoneAttachmentReference(
            sourceAttachmentId, "Hand", Matrix.CreateTranslation(1f, 2f, 3f)));
        scene.Add(source);
        var history = new SceneCommandHistory();

        var duplicate = SceneObjectDuplicator.CreateDuplicate(scene, source.Id);
        history.Execute(scene, new CreateSceneObjectCommand(duplicate));

        var copy = scene.Find(duplicate.Id)!;
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("Fox Copy", copy.Name);
        Assert.Equal(source.Transform.Position, copy.Transform.Position);
        Assert.Same(asset, copy.GltfAsset);
        Assert.Equal("Walk", copy.CharacterSettings!.ClipName);
        Assert.Equal(0.4f, copy.CharacterSettings.Time);
        Assert.Equal(1.25f, copy.CharacterSettings.Speed);
        Assert.True(copy.CharacterSettings.IsPlaying);
        var attachment = Assert.Single(copy.CharacterSettings.Attachments);
        Assert.NotEqual(sourceAttachmentId, attachment.Id);
        Assert.Equal("Hand", attachment.BoneName);
        Assert.Equal(Matrix.CreateTranslation(1f, 2f, 3f), attachment.LocalOffset);

        Assert.True(history.Undo(scene));
        Assert.Null(scene.Find(copy.Id));
        Assert.True(history.Redo(scene));
        Assert.NotSame(copy, scene.Find(copy.Id));
        Assert.Equal(copy.Id, scene.Find(copy.Id)!.Id);
        Assert.Equal(attachment.Id, Assert.Single(scene.Find(copy.Id)!.CharacterSettings!.Attachments).Id);
    }

    [Fact]
    public void TwoPlacedInstancesShareTheImportedAssetWithoutCopyingIt()
    {
        var scene = new SceneGraph();
        var history = new SceneCommandHistory();
        var asset = new GltfAssetReference(Guid.NewGuid(), "Assets/Fox.glb");
        var first = SceneObjectFactory.CreateAssetInstance(scene, asset, Vector3.Zero);
        var second = SceneObjectFactory.CreateAssetInstance(scene, asset, new Vector3(100f, 0f, 0f));

        history.Execute(scene, new CreateSceneObjectCommand(first));
        history.Execute(scene, new CreateSceneObjectCommand(second));

        Assert.Equal(2, scene.Objects.Count);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Same(asset, scene.Find(first.Id)!.GltfAsset);
        Assert.Same(asset, scene.Find(second.Id)!.GltfAsset);
        Assert.Equal("Assets/Fox.glb", scene.Find(first.Id)!.GltfAsset!.SourcePath);
        Assert.Equal("Assets/Fox.glb", scene.Find(second.Id)!.GltfAsset!.SourcePath);
        Assert.Equal(new Vector3(100f, 0f, 0f), scene.Find(second.Id)!.Transform.Position);
    }

    [Fact]
    public void RpgPlacementFactoryAndDuplicateUseUniqueStableInstanceIds()
    {
        var scene = new SceneGraph();
        var history = new SceneCommandHistory();
        var first = SceneObjectFactory.CreateWorldEntityPlacement(scene, WorldEntityKind.Actor,
            "actors.guard", "Guard", new Vector3(2f, 0f, 3f));
        history.Execute(scene, new CreateSceneObjectCommand(first));
        var second = SceneObjectFactory.CreateWorldEntityPlacement(scene, WorldEntityKind.Actor,
            "actors.guard", "Guard", new Vector3(4f, 0f, 3f));
        history.Execute(scene, new CreateSceneObjectCommand(second));
        var item = SceneObjectFactory.CreateWorldEntityPlacement(scene, WorldEntityKind.Item,
            "items.sword", "Iron Sword", new Vector3(3f, 0f, 3f));
        var duplicate = SceneObjectDuplicator.CreateDuplicate(scene, first.Id);

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.WorldEntity!.InstanceId, second.WorldEntity!.InstanceId);
        Assert.NotEqual(first.Name, second.Name);
        Assert.Equal("actors.guard", second.WorldEntity.DefinitionId);
        Assert.Equal(WorldEntityKind.Item, item.WorldEntity!.Kind);
        Assert.NotEqual(first.WorldEntity.InstanceId, item.WorldEntity.InstanceId);
        Assert.NotEqual(first.WorldEntity.InstanceId, duplicate.WorldEntity!.InstanceId);
        Assert.Equal(WorldEntityKind.Actor, duplicate.WorldEntity.Kind);
    }

    [Fact]
    public void SpawnMarkerFactoryAndDoorEditUndoRedoPreserveStableReferences()
    {
        var scene = new SceneGraph();
        var history = new SceneCommandHistory();
        var marker = SceneObjectFactory.CreateSpawnMarker(scene, "Entrance", new Vector3(1f, 2f, 3f));
        scene.Add(marker);
        var otherMarker = SceneObjectFactory.CreateSpawnMarker(scene, "Entrance", Vector3.Zero);
        scene.Add(otherMarker);
        var doorObject = new SceneObject(Guid.NewGuid(), "Door");
        scene.Add(doorObject);
        var link = new WorldDoorComponent(Guid.NewGuid(), marker.SpawnPoint!.Id, Quaternion.Identity);

        history.Execute(scene, new WorldDoorEditCommand(doorObject.Id, link));
        Assert.Same(link, doorObject.Door);
        Assert.True(history.Undo(scene));
        Assert.Null(doorObject.Door);
        Assert.True(history.Redo(scene));
        Assert.Same(link, scene.Find(doorObject.Id)!.Door);
        Assert.NotEqual(marker.Id, otherMarker.Id);
        Assert.NotEqual(marker.SpawnPoint.Id, otherMarker.SpawnPoint!.Id);
        Assert.NotEqual(marker.Name, otherMarker.Name);
    }

    [Fact]
    public void DeleteUndoRestoresParentChildrenAndAssetReferences()
    {
        var scene = new SceneGraph();
        var asset = new GltfAssetReference(Guid.NewGuid(), "Assets/house.glb");
        var outerParent = new SceneObject(Guid.NewGuid(), "Outer");
        var deleted = new SceneObject(Guid.NewGuid(), "Building")
        {
            GltfAsset = asset,
            Transform = new Transform { Position = new Vector3(2f, 3f, 4f) },
            CharacterSettings = new GltfCharacterSettings { ClipName = "Idle", Time = 0.6f }
        };
        var child = new SceneObject(Guid.NewGuid(), "Door");
        var grandchild = new SceneObject(Guid.NewGuid(), "Handle");
        scene.Add(outerParent);
        scene.Add(deleted);
        scene.Add(child);
        scene.Add(grandchild);
        scene.SetParent(deleted.Id, outerParent.Id);
        scene.SetParent(child.Id, deleted.Id);
        scene.SetParent(grandchild.Id, child.Id);
        var history = new SceneCommandHistory();

        history.Execute(scene, new DeleteSceneObjectCommand(deleted.Id));
        Assert.Null(scene.Find(deleted.Id));
        Assert.Null(scene.Find(child.Id)!.ParentId);
        Assert.True(history.Undo(scene));

        var restored = scene.Find(deleted.Id)!;
        Assert.Equal(outerParent.Id, restored.ParentId);
        Assert.Equal(deleted.Id, scene.Find(child.Id)!.ParentId);
        Assert.Equal(child.Id, scene.Find(grandchild.Id)!.ParentId);
        Assert.Equal(asset.AssetId, restored.GltfAsset!.AssetId);
        Assert.Equal(asset.SourcePath, restored.GltfAsset.SourcePath);
        Assert.Equal(new Vector3(2f, 3f, 4f), restored.Transform.Position);
        Assert.Equal("Idle", restored.CharacterSettings!.ClipName);
        Assert.Equal(0.6f, restored.CharacterSettings.Time);
    }
}
