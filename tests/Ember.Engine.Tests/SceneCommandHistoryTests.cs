using System;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneCommandHistoryTests
{
    [Fact]
    public void DirtyStateTracksSavedPositionAcrossUndoRedoAndNewBranches()
    {
        var scene = new SceneGraph();
        var history = new SceneCommandHistory();
        Assert.False(history.IsDirty);

        var first = new SceneObject(Guid.NewGuid(), "First");
        history.Execute(scene, new CreateSceneObjectCommand(first));
        Assert.True(history.IsDirty);
        history.MarkSaved();
        Assert.False(history.IsDirty);

        var second = new SceneObject(Guid.NewGuid(), "Second");
        history.Execute(scene, new CreateSceneObjectCommand(second));
        Assert.True(history.IsDirty);
        Assert.True(history.Undo(scene));
        Assert.False(history.IsDirty);
        Assert.True(history.Redo(scene));
        Assert.True(history.IsDirty);

        Assert.True(history.Undo(scene));
        var replacement = new SceneObject(Guid.NewGuid(), "Replacement");
        history.Execute(scene, new CreateSceneObjectCommand(replacement));
        Assert.False(history.CanRedo);
        Assert.True(history.IsDirty);
        history.MarkSaved();
        Assert.False(history.IsDirty);
        Assert.True(history.Undo(scene));
        Assert.True(history.IsDirty);
        Assert.True(history.Redo(scene));
        Assert.False(history.IsDirty);
    }

    [Fact]
    public void CharacterSettingsDeepCopyOwnsMutablePlaybackAndAttachmentData()
    {
        var settings = new GltfCharacterSettings
        {
            ClipName = "Idle",
            Time = 0.25f,
            Speed = 1.5f,
            Loop = false,
            IsPlaying = true,
            CrossfadeClipName = "Walk",
            BlendAmount = 0.4f
        };
        var attachmentId = Guid.NewGuid();
        settings.Attachments.Add(new GltfBoneAttachmentReference(
            attachmentId, "Hand", Matrix.CreateTranslation(1f, 2f, 3f)));

        var copy = settings.DeepCopy();
        copy.Time = 2f;
        copy.Attachments.Clear();

        Assert.Equal("Idle", settings.ClipName);
        Assert.Equal(0.25f, settings.Time);
        Assert.Equal(1.5f, settings.Speed);
        Assert.False(settings.Loop);
        Assert.True(settings.IsPlaying);
        Assert.Equal("Walk", settings.CrossfadeClipName);
        Assert.Equal(0.4f, settings.BlendAmount);
        Assert.Equal(attachmentId, Assert.Single(settings.Attachments).Id);
    }

    [Fact]
    public void CharacterSettingEditsRestoreTheSavedDirtyPositionAcrossUndoRedo()
    {
        var item = new SceneObject(Guid.NewGuid(), "Animated object")
        {
            CharacterSettings = new GltfCharacterSettings { ClipName = "Idle", Time = 0.25f }
        };
        var scene = new SceneGraph();
        scene.Add(item);
        var history = new SceneCommandHistory();
        var original = item.CharacterSettings!.DeepCopy();
        var saved = original.DeepCopy();
        saved.ClipName = "Run";
        saved.Time = 0.5f;
        history.Execute(scene, new EditCharacterSettingsCommand(item.Id, original, saved));
        history.MarkSaved();

        var later = saved.DeepCopy();
        later.Time = 0.75f;
        history.Execute(scene, new EditCharacterSettingsCommand(item.Id, saved, later));

        Assert.True(history.IsDirty);
        Assert.True(history.Undo(scene));
        Assert.False(history.IsDirty);
        Assert.Equal("Run", item.CharacterSettings!.ClipName);
        Assert.Equal(0.5f, item.CharacterSettings.Time);
        Assert.True(history.Redo(scene));
        Assert.True(history.IsDirty);
        Assert.Equal(0.75f, item.CharacterSettings!.Time);
    }

    [Fact]
    public void BoxColliderEditsUndoRedoAndDuplicatePreservesTheShape()
    {
        var original = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One);
        var replacement = new SceneBoxColliderComponent(
            new Vector3(0f, 1f, 0f), new Vector3(2f, 1f, 2f), isTrigger: true);
        var item = new SceneObject(Guid.NewGuid(), "Trigger") { BoxCollider = original };
        var scene = new SceneGraph();
        scene.Add(item);
        var history = new SceneCommandHistory();

        history.Execute(scene, new EditBoxColliderCommand(item.Id, original, replacement));
        Assert.Same(replacement, item.BoxCollider);
        Assert.True(history.IsDirty);
        Assert.True(history.Undo(scene));
        Assert.Same(original, item.BoxCollider);
        Assert.True(history.Redo(scene));
        Assert.Same(replacement, item.BoxCollider);

        var duplicate = SceneObjectDuplicator.CreateDuplicate(scene, item.Id);
        Assert.NotEqual(item.Id, duplicate.Id);
        Assert.Equal(replacement.Center, duplicate.BoxCollider!.Center);
        Assert.Equal(replacement.Size, duplicate.BoxCollider.Size);
        Assert.True(duplicate.BoxCollider.IsTrigger);
    }

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
    public void CharacterPlaybackEditUndoesAndRedoesWithoutSharingMutableSettings()
    {
        var scene = new SceneGraph();
        var item = new SceneObject(Guid.NewGuid(), "Animated object")
        {
            CharacterSettings = new GltfCharacterSettings
            {
                ClipName = "Idle",
                Time = 0.25f,
                Speed = 1f,
                IsPlaying = false
            }
        };
        var attachmentId = Guid.NewGuid();
        item.CharacterSettings!.Attachments.Add(new GltfBoneAttachmentReference(
            attachmentId, "Hand", Matrix.CreateTranslation(1f, 2f, 3f)));
        scene.Add(item);
        var before = new GltfCharacterSettings
        {
            ClipName = "Idle",
            Time = 0.25f,
            Speed = 1f
        };
        before.Attachments.Add(new GltfBoneAttachmentReference(
            attachmentId, "Hand", Matrix.CreateTranslation(1f, 2f, 3f)));
        var after = new GltfCharacterSettings
        {
            ClipName = "Run",
            Time = 0.6f,
            Speed = 1f,
            IsPlaying = true
        };
        after.Attachments.Add(new GltfBoneAttachmentReference(
            attachmentId, "Hand", Matrix.CreateTranslation(1f, 2f, 3f)));
        var history = new SceneCommandHistory();

        history.Execute(scene, new EditCharacterSettingsCommand(item.Id, before, after));
        after.ClipName = "Mutated after execute";

        Assert.True(history.IsDirty);
        Assert.Equal("Run", item.CharacterSettings!.ClipName);
        Assert.Equal(0.6f, item.CharacterSettings.Time);
        Assert.Equal(attachmentId, Assert.Single(item.CharacterSettings.Attachments).Id);
        Assert.True(history.Undo(scene));
        Assert.Equal("Idle", item.CharacterSettings!.ClipName);
        Assert.Equal(0.25f, item.CharacterSettings.Time);
        Assert.False(item.CharacterSettings.IsPlaying);
        Assert.Equal(attachmentId, Assert.Single(item.CharacterSettings.Attachments).Id);
        Assert.True(history.Redo(scene));
        Assert.Equal("Run", item.CharacterSettings!.ClipName);
        Assert.Equal(0.6f, item.CharacterSettings.Time);
        Assert.True(item.CharacterSettings.IsPlaying);
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

    [Fact]
    public void ReparentCommandPreservesWorldTransformAcrossUndoAndRedo()
    {
        var scene = new SceneGraph();
        var oldParent = new SceneObject(Guid.NewGuid(), "Old parent")
        {
            Transform = new Transform
            {
                Position = new Vector3(-8f, 3f, 6f),
                Rotation = Quaternion.CreateFromYawPitchRoll(0.25f, 0f, 0f),
                Scale = new Vector3(2f)
            }
        };
        var newParent = new SceneObject(Guid.NewGuid(), "New parent")
        {
            Transform = new Transform
            {
                Position = new Vector3(20f, -4f, 11f),
                Rotation = Quaternion.CreateFromYawPitchRoll(-0.4f, 0.1f, 0f),
                Scale = new Vector3(0.75f)
            }
        };
        var child = new SceneObject(Guid.NewGuid(), "Child")
        {
            Transform = new Transform
            {
                Position = new Vector3(3f, 5f, -2f),
                Rotation = Quaternion.CreateFromYawPitchRoll(0.1f, 0.3f, -0.2f),
                Scale = new Vector3(1.2f)
            }
        };
        scene.Add(oldParent);
        scene.Add(newParent);
        scene.Add(child);
        scene.SetParent(child.Id, oldParent.Id);
        var originalWorld = scene.GetWorldMatrix(child.Id);
        var history = new SceneCommandHistory();

        history.Execute(scene, new ReparentSceneObjectCommand(child.Id, newParent.Id));
        Assert.Equal(newParent.Id, scene.Find(child.Id)!.ParentId);
        AssertMatrixClose(originalWorld, scene.GetWorldMatrix(child.Id));

        Assert.True(history.Undo(scene));
        Assert.Equal(oldParent.Id, scene.Find(child.Id)!.ParentId);
        AssertMatrixClose(originalWorld, scene.GetWorldMatrix(child.Id));

        Assert.True(history.Redo(scene));
        Assert.Equal(newParent.Id, scene.Find(child.Id)!.ParentId);
        AssertMatrixClose(originalWorld, scene.GetWorldMatrix(child.Id));
    }

    [Fact]
    public void ReparentCommandRejectsCyclesAndUnrepresentableShearWithoutMutation()
    {
        var scene = new SceneGraph();
        var ancestor = new SceneObject(Guid.NewGuid(), "Ancestor");
        var descendant = new SceneObject(Guid.NewGuid(), "Descendant");
        var shearingParent = new SceneObject(Guid.NewGuid(), "Nonuniform parent")
        {
            Transform = new Transform
            {
                Scale = new Vector3(2f, 1f, 0.5f),
                Rotation = Quaternion.CreateFromYawPitchRoll(0.4f, 0f, 0f)
            }
        };
        var rotatedChild = new SceneObject(Guid.NewGuid(), "Rotated child")
        {
            Transform = new Transform
            {
                Position = new Vector3(2f, 3f, 4f),
                Rotation = Quaternion.CreateFromYawPitchRoll(0f, 0.5f, 0f)
            }
        };
        scene.Add(ancestor);
        scene.Add(descendant);
        scene.SetParent(descendant.Id, ancestor.Id);
        scene.Add(shearingParent);
        scene.Add(rotatedChild);
        var history = new SceneCommandHistory();

        Assert.Throws<InvalidOperationException>(() => history.Execute(scene,
            new ReparentSceneObjectCommand(ancestor.Id, descendant.Id)));
        Assert.Null(scene.Find(ancestor.Id)!.ParentId);
        Assert.Equal(ancestor.Id, scene.Find(descendant.Id)!.ParentId);

        var originalLocal = scene.Find(rotatedChild.Id)!.Transform;
        var originalLocalPosition = originalLocal.Position;
        var originalLocalRotation = originalLocal.Rotation;
        var originalLocalScale = originalLocal.Scale;
        var originalWorld = scene.GetWorldMatrix(rotatedChild.Id);
        var error = Assert.Throws<InvalidOperationException>(() => history.Execute(scene,
            new ReparentSceneObjectCommand(rotatedChild.Id, shearingParent.Id)));
        Assert.Contains("introduce shear", error.Message, StringComparison.Ordinal);
        Assert.Null(scene.Find(rotatedChild.Id)!.ParentId);
        AssertMatrixClose(originalWorld, scene.GetWorldMatrix(rotatedChild.Id));
        Assert.Equal(originalLocalPosition, scene.Find(rotatedChild.Id)!.Transform.Position);
        Assert.Equal(originalLocalRotation, scene.Find(rotatedChild.Id)!.Transform.Rotation);
        Assert.Equal(originalLocalScale, scene.Find(rotatedChild.Id)!.Transform.Scale);
        Assert.False(history.CanUndo);
    }

    private static void AssertMatrixClose(Matrix expected, Matrix actual)
    {
        var expectedValues = new[]
        {
            expected.M11, expected.M12, expected.M13, expected.M14,
            expected.M21, expected.M22, expected.M23, expected.M24,
            expected.M31, expected.M32, expected.M33, expected.M34,
            expected.M41, expected.M42, expected.M43, expected.M44
        };
        var actualValues = new[]
        {
            actual.M11, actual.M12, actual.M13, actual.M14,
            actual.M21, actual.M22, actual.M23, actual.M24,
            actual.M31, actual.M32, actual.M33, actual.M34,
            actual.M41, actual.M42, actual.M43, actual.M44
        };
        for (var index = 0; index < expectedValues.Length; index++)
            Assert.InRange(MathF.Abs(expectedValues[index] - actualValues[index]), 0f, 0.001f);
    }
}
