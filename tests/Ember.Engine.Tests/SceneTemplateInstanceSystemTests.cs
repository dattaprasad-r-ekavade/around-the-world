using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ember.Authoring;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneTemplateInstanceSystemTests
{
    [Fact]
    public void PlacesIndependentExpandedHierarchiesAndPersistsTheirSourceMappings()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var source = CreateSourceHierarchy(out var rootId, out var childId,
                out var assetId, out var attachmentId, out var sourceSpawnId,
                out var sourceEntityInstanceId, out var sourceCellId);
            var template = SceneTemplateFile.Save(source, rootId, "Workbench", path);
            var scene = new SceneGraph();
            scene.Add(new SceneObject(rootId, "Original ID remains occupied"));
            var targetCellId = Guid.NewGuid();

            var first = SceneTemplateInstanceSystem.Instantiate(scene, template,
                new Vector3(10f, 0f, 0f), targetCellId);
            var second = SceneTemplateInstanceSystem.Instantiate(scene, template,
                new Vector3(-10f, 0f, 0f), targetCellId);

            Assert.NotEqual(first.Id, second.Id);
            Assert.Equal("Workbench", first.Name);
            Assert.Equal("Workbench (2)", second.Name);
            Assert.Equal(template.Id, first.TemplateInstance!.TemplateId);
            Assert.Equal(template.Revision, first.TemplateInstance.AppliedRevision);
            Assert.Equal(rootId, first.TemplateInstance.SourceRootObjectId);
            Assert.Equal(2, first.TemplateInstance.ObjectMappings.Count);

            var firstMap = first.TemplateInstance.ObjectMappings.ToDictionary(
                mapping => mapping.SourceObjectId, mapping => mapping.InstanceObjectId);
            var secondMap = second.TemplateInstance!.ObjectMappings.ToDictionary(
                mapping => mapping.SourceObjectId, mapping => mapping.InstanceObjectId);
            Assert.Equal(firstMap.Keys.OrderBy(id => id), secondMap.Keys.OrderBy(id => id));
            Assert.All(firstMap.Keys, sourceObjectId =>
                Assert.NotEqual(firstMap[sourceObjectId], secondMap[sourceObjectId]));
            Assert.All(firstMap.Values.Concat(secondMap.Values).Append(first.Id).Append(second.Id),
                id => Assert.NotEqual(rootId, id));
            Assert.Equal(first.Id, scene.Find(firstMap[rootId])!.ParentId);
            Assert.Equal(firstMap[rootId], scene.Find(firstMap[childId])!.ParentId);
            Assert.Equal(new Vector3(11f, 2f, 3f),
                new Vector3(scene.GetWorldMatrix(firstMap[rootId]).M41,
                    scene.GetWorldMatrix(firstMap[rootId]).M42,
                    scene.GetWorldMatrix(firstMap[rootId]).M43));
            Assert.Equal(assetId, scene.Find(firstMap[rootId])!.GltfAsset!.AssetId);

            var firstAttachment = Assert.Single(scene.Find(firstMap[rootId])!.CharacterSettings!.Attachments);
            var secondAttachment = Assert.Single(scene.Find(secondMap[rootId])!.CharacterSettings!.Attachments);
            Assert.NotEqual(attachmentId, firstAttachment.Id);
            Assert.NotEqual(firstAttachment.Id, secondAttachment.Id);

            var firstSpawn = scene.Find(firstMap[childId])!.SpawnPoint!.Id;
            var secondSpawn = scene.Find(secondMap[childId])!.SpawnPoint!.Id;
            Assert.NotEqual(sourceSpawnId, firstSpawn);
            Assert.NotEqual(firstSpawn, secondSpawn);
            Assert.Equal(targetCellId, scene.Find(firstMap[rootId])!.Door!.DestinationCellId);
            Assert.Equal(firstSpawn, scene.Find(firstMap[rootId])!.Door!.DestinationSpawnId);

            var firstEntityId = scene.Find(firstMap[childId])!.WorldEntity!.InstanceId;
            var secondEntityId = scene.Find(secondMap[childId])!.WorldEntity!.InstanceId;
            Assert.NotEqual(sourceEntityInstanceId, firstEntityId);
            Assert.NotEqual(firstEntityId, secondEntityId);

            var reopened = SceneFile.FromJson(SceneFile.ToJson(scene));
            var reopenedFirst = reopened.Find(first.Id)!;
            Assert.Equal(template.Id, reopenedFirst.TemplateInstance!.TemplateId);
            Assert.Equal(template.Revision, reopenedFirst.TemplateInstance.AppliedRevision);
            Assert.Equal(first.TemplateInstance.ObjectMappings,
                reopenedFirst.TemplateInstance.ObjectMappings);
            Assert.Equal(firstMap[rootId], reopenedFirst.TemplateInstance.InstanceRootObjectId);
            Assert.Equal(sourceCellId, source.Find(rootId)!.Door!.DestinationCellId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RequiresWorldCellContextForAnInternalDoorAndLeavesSceneUntouched()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var source = CreateSourceHierarchy(out var rootId, out _, out _, out _, out _, out _, out _);
            var template = SceneTemplateFile.Save(source, rootId, "Door room", path);
            var destination = new SceneGraph();

            Assert.Throws<InvalidOperationException>(() =>
                SceneTemplateInstanceSystem.Instantiate(destination, template, Vector3.Zero));

            Assert.Empty(destination.Objects);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RejectsNonFinitePlacementBeforeChangingTheScene()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var source = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Lamp");
            source.Add(root);
            var template = SceneTemplateFile.Save(source, root.Id, "Lamp", path);
            var destination = new SceneGraph();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                SceneTemplateInstanceSystem.Instantiate(destination, template,
                    new Vector3(float.NaN, 0f, 0f)));

            Assert.Empty(destination.Objects);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void PlacementCommandUndoAndRedoRestoreTheSameInstanceAndObjectIds()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var source = CreateSourceHierarchy(out var rootId, out _, out _, out _, out _, out _, out _);
            var template = SceneTemplateFile.Save(source, rootId, "Workbench", path);
            var destination = new SceneGraph();
            var history = new SceneCommandHistory();
            var command = new PlaceSceneTemplateCommand(template, new Vector3(5f, 0f, 0f), Guid.NewGuid());

            history.Execute(destination, command);
            var instanceId = command.InstanceObjectId!.Value;
            var firstMapping = destination.Find(instanceId)!.TemplateInstance!.ObjectMappings.ToArray();
            Assert.Equal(3, destination.Objects.Count);

            Assert.True(history.Undo(destination));
            Assert.Empty(destination.Objects);

            Assert.True(history.Redo(destination));
            var restored = destination.Find(instanceId)!;
            Assert.Equal(3, destination.Objects.Count);
            Assert.Equal(firstMapping, restored.TemplateInstance!.ObjectMappings);
            Assert.Equal(template.Id, restored.TemplateInstance.TemplateId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExplicitUpdatePreservesNameAndTransformOverridesAndCanBeUndone()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var source = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Workshop")
            {
                Transform = new Transform { Position = new Vector3(1f, 0f, 0f) }
            };
            var child = new SceneObject(Guid.NewGuid(), "Old lamp")
            {
                Transform = new Transform { Position = new Vector3(0f, 1f, 0f) }
            };
            source.Add(root);
            source.Add(child);
            source.SetParent(child.Id, root.Id);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Workshop", path);

            var scene = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision, Vector3.Zero);
            var mappings = wrapper.TemplateInstance!.ObjectMappings.ToDictionary(
                mapping => mapping.SourceObjectId, mapping => mapping.InstanceObjectId);
            var instanceRoot = scene.Find(mappings[root.Id])!;
            var instanceChild = scene.Find(mappings[child.Id])!;
            Assert.Equal(2, wrapper.TemplateInstance.ObjectBaselines.Count);

            instanceRoot.Name = "Local workshop name";
            instanceChild.Transform = new Transform { Position = new Vector3(0f, 7f, 0f) };
            root.Transform = new Transform { Position = new Vector3(3f, 0f, 0f) };
            child.Name = "Updated lamp";
            child.Transform = new Transform { Position = new Vector3(0f, 2f, 0f) };
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Workshop", path);
            var history = new SceneCommandHistory();

            history.Execute(scene, new UpdateSceneTemplateCommand(wrapper.Id, secondRevision));

            Assert.Equal("Local workshop name", instanceRoot.Name);
            Assert.Equal(new Vector3(3f, 0f, 0f), instanceRoot.Transform.Position);
            Assert.Equal("Updated lamp", instanceChild.Name);
            Assert.Equal(new Vector3(0f, 7f, 0f), instanceChild.Transform.Position);
            Assert.Equal(2, wrapper.TemplateInstance!.AppliedRevision);
            Assert.Equal(new Vector3(3f, 0f, 0f), wrapper.TemplateInstance.ObjectBaselines
                .Single(baseline => baseline.SourceObjectId == root.Id).Position);
            Assert.Equal(mappings[root.Id], wrapper.TemplateInstance.InstanceRootObjectId);

            var reopened = SceneFile.FromJson(SceneFile.ToJson(scene));
            Assert.Equal(2, reopened.Find(wrapper.Id)!.TemplateInstance!.ObjectBaselines.Count);
            Assert.Equal(new Vector3(0f, 7f, 0f), reopened.Find(mappings[child.Id])!.Transform.Position);

            Assert.True(history.Undo(scene));
            Assert.Equal(1, wrapper.TemplateInstance!.AppliedRevision);
            Assert.Equal("Local workshop name", instanceRoot.Name);
            Assert.Equal("Old lamp", instanceChild.Name);
            Assert.Equal(new Vector3(0f, 7f, 0f), instanceChild.Transform.Position);
            Assert.True(history.Redo(scene));
            Assert.Equal(2, wrapper.TemplateInstance!.AppliedRevision);
            Assert.Equal("Local workshop name", instanceRoot.Name);
            Assert.Equal(new Vector3(0f, 7f, 0f), instanceChild.Transform.Position);

            root.Name = "Final workshop name";
            root.Transform = new Transform { Position = new Vector3(4f, 0f, 0f) };
            child.Name = "Final lamp";
            child.Transform = new Transform { Position = new Vector3(0f, 3f, 0f) };
            var thirdRevision = SceneTemplateFile.Save(source, root.Id, "Workshop", path);
            history.Execute(scene, new UpdateSceneTemplateCommand(wrapper.Id, thirdRevision));
            Assert.Equal(3, wrapper.TemplateInstance.AppliedRevision);
            Assert.Equal("Local workshop name", instanceRoot.Name);
            Assert.Equal(new Vector3(4f, 0f, 0f), instanceRoot.Transform.Position);
            Assert.Equal("Final lamp", instanceChild.Name);
            Assert.Equal(new Vector3(0f, 7f, 0f), instanceChild.Transform.Position);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void UpdateRejectsChangedSourceObjectSetWithoutChangingInstance()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var source = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Room");
            source.Add(root);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);
            var destination = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(destination, firstRevision, Vector3.Zero);
            var instanceRootId = wrapper.TemplateInstance!.InstanceRootObjectId;
            root.Name = "Updated room";
            var added = new SceneObject(Guid.NewGuid(), "New prop");
            source.Add(added);
            source.SetParent(added.Id, root.Id);
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);

            Assert.Throws<InvalidOperationException>(() =>
                new UpdateSceneTemplateCommand(wrapper.Id, secondRevision).Apply(destination));

            Assert.Equal(1, wrapper.TemplateInstance.AppliedRevision);
            Assert.Equal("Room", destination.Find(instanceRootId)!.Name);
            Assert.Equal(2, destination.Objects.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void VersionEightInstancesLoadWithoutBaselinesAndCannotBeUpdatedUnsafely()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var source = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Room");
            source.Add(root);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);
            var scene = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision, Vector3.Zero);
            var legacyJson = JsonNode.Parse(SceneFile.ToJson(scene))!.AsObject();
            legacyJson["Version"] = 8;
            var wrapperData = legacyJson["Objects"]!.AsArray().Single(item =>
                Guid.Parse((string)item!["Id"]!) == wrapper.Id)!;
            wrapperData["TemplateInstance"]!.AsObject().Remove("ObjectBaselines");

            var legacyScene = SceneFile.FromJson(legacyJson.ToJsonString());
            var legacyWrapper = legacyScene.Find(wrapper.Id)!;
            Assert.Empty(legacyWrapper.TemplateInstance!.ObjectBaselines);
            Assert.Contains("\"Version\": 9", SceneFile.ToJson(legacyScene), StringComparison.Ordinal);

            root.Name = "Updated room";
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);
            Assert.Throws<InvalidOperationException>(() =>
                new UpdateSceneTemplateCommand(wrapper.Id, secondRevision).Apply(legacyScene));
            Assert.Equal(1, legacyWrapper.TemplateInstance.AppliedRevision);
            Assert.Equal("Room", legacyScene.Find(legacyWrapper.TemplateInstance.InstanceRootObjectId)!.Name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static SceneGraph CreateSourceHierarchy(out Guid rootId, out Guid childId,
        out Guid assetId, out Guid attachmentId, out Guid spawnId,
        out Guid entityInstanceId, out Guid cellId)
    {
        rootId = Guid.NewGuid();
        childId = Guid.NewGuid();
        assetId = Guid.NewGuid();
        attachmentId = Guid.NewGuid();
        spawnId = Guid.NewGuid();
        entityInstanceId = Guid.NewGuid();
        cellId = Guid.NewGuid();
        var scene = new SceneGraph();
        var root = new SceneObject(rootId, "Workbench surface")
        {
            GltfAsset = new GltfAssetReference(assetId, "Assets/workbench.glb"),
            CharacterSettings = new GltfCharacterSettings(),
            Transform = new Transform { Position = new Vector3(1f, 2f, 3f) },
            Door = new WorldDoorComponent(cellId, spawnId, Quaternion.Identity)
        };
        root.CharacterSettings.Attachments.Add(
            new GltfBoneAttachmentReference(attachmentId, "hand_r", Matrix.Identity));
        var child = new SceneObject(childId, "Spawn marker")
        {
            SpawnPoint = new WorldSpawnComponent(spawnId),
            WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Item,
                "items.workbench-tool", entityInstanceId)
        };
        scene.Add(root);
        scene.Add(child);
        scene.SetParent(childId, rootId);
        return scene;
    }

    private static string TemporaryTemplatePath() => Path.Combine(Path.GetTempPath(),
        $"ember-scene-template-instance-{Guid.NewGuid():N}.json");
}
