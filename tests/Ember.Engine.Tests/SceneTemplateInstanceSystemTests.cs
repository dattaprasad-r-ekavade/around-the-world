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
    public void InitialTemplatePlacementPreservesSavedBehaviourAssignments()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var rootId = Guid.NewGuid();
            var source = new SceneGraph();
            var root = new SceneObject(rootId, "Door");
            root.SetBehaviourAssignments(new[] { "sample.open-door" });
            source.Add(root);
            var template = SceneTemplateFile.Save(source, rootId, "Door", path);
            var destination = new SceneGraph();

            var wrapper = SceneTemplateInstanceSystem.Instantiate(destination, template, Vector3.Zero);
            var instanceObjectId = wrapper.TemplateInstance!.ObjectMappings
                .Single(mapping => mapping.SourceObjectId == rootId).InstanceObjectId;

            Assert.Equal(new[] { "sample.open-door" },
                destination.Find(instanceObjectId)!.BehaviourAssignments);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TemplateUpdatePropagatesBehaviourAssignmentsAndUndoRestoresThem()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var root = new SceneObject(Guid.NewGuid(), "Door");
            root.SetBehaviourAssignments(new[] { "sample.open-door" });
            var source = new SceneGraph();
            source.Add(root);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Door", path);
            var scene = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision, Vector3.Zero);
            var instanceId = wrapper.TemplateInstance!.ObjectMappings.Single().InstanceObjectId;

            root.SetBehaviourAssignments(new[] { "sample.open-door", "sample.close-door" });
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Door", path);
            var history = new SceneCommandHistory();
            history.Execute(scene, new UpdateSceneTemplateCommand(wrapper.Id, secondRevision));

            Assert.Equal(new[] { "sample.open-door", "sample.close-door" },
                scene.Find(instanceId)!.BehaviourAssignments);
            var reopened = SceneFile.FromJson(SceneFile.ToJson(scene));
            var baseline = Assert.Single(reopened.Find(wrapper.Id)!.TemplateInstance!.ObjectBaselines);
            Assert.True(baseline.HasBehaviourAssignmentsBaseline);
            Assert.Equal(new[] { "sample.open-door", "sample.close-door" }, baseline.BehaviourAssignments);

            Assert.True(history.Undo(scene));
            Assert.Equal(new[] { "sample.open-door" }, scene.Find(instanceId)!.BehaviourAssignments);
            Assert.True(history.Redo(scene));
            Assert.Equal(new[] { "sample.open-door", "sample.close-door" },
                scene.Find(instanceId)!.BehaviourAssignments);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TemplateUpdatePreservesLocalBehaviourAssignmentOverride()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var root = new SceneObject(Guid.NewGuid(), "Door");
            root.SetBehaviourAssignments(new[] { "sample.open-door" });
            var source = new SceneGraph();
            source.Add(root);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Door", path);
            var scene = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision, Vector3.Zero);
            var instanceId = wrapper.TemplateInstance!.ObjectMappings.Single().InstanceObjectId;
            scene.Find(instanceId)!.SetBehaviourAssignments(new[] { "local.custom-door" });

            root.SetBehaviourAssignments(new[] { "sample.open-door", "sample.close-door" });
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Door", path);
            SceneTemplateInstanceSystem.Update(scene, wrapper.Id, secondRevision);

            Assert.Equal(new[] { "local.custom-door" }, scene.Find(instanceId)!.BehaviourAssignments);
            var baseline = Assert.Single(SceneFile.FromJson(SceneFile.ToJson(scene))
                .Find(wrapper.Id)!.TemplateInstance!.ObjectBaselines);
            Assert.Equal(new[] { "sample.open-door", "sample.close-door" }, baseline.BehaviourAssignments);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TemplateUpdatePreservesAssignmentsWhenVersionTwentyBaselineIsMissing()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var root = new SceneObject(Guid.NewGuid(), "Door");
            var source = new SceneGraph();
            source.Add(root);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Door", path);
            var scene = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision, Vector3.Zero);
            var instanceId = wrapper.TemplateInstance!.ObjectMappings.Single().InstanceObjectId;
            scene.Find(instanceId)!.SetBehaviourAssignments(new[] { "local.custom-door" });

            var legacyJson = JsonNode.Parse(SceneFile.ToJson(scene))!.AsObject();
            var wrapperData = legacyJson["Objects"]!.AsArray().Single(item =>
                Guid.Parse((string)item!["Id"]!) == wrapper.Id)!;
            var baseline = wrapperData["TemplateInstance"]!["ObjectBaselines"]!.AsArray().Single()!.AsObject();
            baseline.Remove("HasBehaviourAssignmentsBaseline");
            baseline.Remove("BehaviourAssignments");
            var reopened = SceneFile.FromJson(legacyJson.ToJsonString());

            root.SetBehaviourAssignments(new[] { "sample.open-door" });
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Door", path);
            SceneTemplateInstanceSystem.Update(reopened, wrapper.Id, secondRevision);

            Assert.Equal(new[] { "local.custom-door" }, reopened.Find(instanceId)!.BehaviourAssignments);
            var upgradedBaseline = Assert.Single(reopened.Find(wrapper.Id)!.TemplateInstance!.ObjectBaselines);
            Assert.True(upgradedBaseline.HasBehaviourAssignmentsBaseline);
            Assert.Equal(new[] { "sample.open-door" }, upgradedBaseline.BehaviourAssignments);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void RemovedTemplateObjectWithBehaviourOverrideIsRetainedAsOrphan()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var root = new SceneObject(Guid.NewGuid(), "Room");
            var child = new SceneObject(Guid.NewGuid(), "Door");
            child.SetBehaviourAssignments(new[] { "sample.open-door" });
            var source = new SceneGraph();
            source.Add(root);
            source.Add(child);
            source.SetParent(child.Id, root.Id);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);
            var scene = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision, Vector3.Zero);
            var instanceChildId = wrapper.TemplateInstance!.ObjectMappings
                .Single(mapping => mapping.SourceObjectId == child.Id).InstanceObjectId;
            scene.Find(instanceChildId)!.SetBehaviourAssignments(new[] { "local.custom-door" });

            source.Remove(child.Id);
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);
            SceneTemplateInstanceSystem.Update(scene, wrapper.Id, secondRevision);

            Assert.NotNull(scene.Find(instanceChildId));
            Assert.Equal(new[] { "local.custom-door" }, scene.Find(instanceChildId)!.BehaviourAssignments);
            Assert.Contains(instanceChildId, wrapper.TemplateInstance!.OrphanedObjectIds);
            Assert.DoesNotContain(wrapper.TemplateInstance.ObjectMappings,
                mapping => mapping.InstanceObjectId == instanceChildId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

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
    public void TemplateUpdatePreservesColliderOverridesAndUndoRestoresColliderState()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var root = new SceneObject(Guid.NewGuid(), "Room")
            {
                BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, new Vector3(8f, 3f, 8f))
            };
            var prop = new SceneObject(Guid.NewGuid(), "Crate")
            {
                BoxCollider = new SceneBoxColliderComponent(new Vector3(0f, 0.5f, 0f), Vector3.One)
            };
            var source = new SceneGraph();
            source.Add(root);
            source.Add(prop);
            source.SetParent(prop.Id, root.Id);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);

            var scene = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision, Vector3.Zero);
            var mappings = wrapper.TemplateInstance!.ObjectMappings.ToDictionary(
                mapping => mapping.SourceObjectId, mapping => mapping.InstanceObjectId);
            var instanceRoot = scene.Find(mappings[root.Id])!;
            var instanceProp = scene.Find(mappings[prop.Id])!;
            var localOverride = new SceneBoxColliderComponent(
                new Vector3(0f, 1f, 0f), new Vector3(2f, 2f, 2f), isTrigger: true);
            instanceRoot.BoxCollider = localOverride;

            root.BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, new Vector3(10f, 4f, 10f));
            var updatedPropCollider = new SceneBoxColliderComponent(
                new Vector3(0f, 0.75f, 0f), new Vector3(1.5f, 1.5f, 1.5f));
            prop.BoxCollider = updatedPropCollider;
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);
            var history = new SceneCommandHistory();
            var command = new UpdateSceneTemplateCommand(wrapper.Id, secondRevision);

            history.Execute(scene, command);

            Assert.Same(localOverride, instanceRoot.BoxCollider);
            Assert.Equal(updatedPropCollider.Center, instanceProp.BoxCollider!.Center);
            Assert.Equal(updatedPropCollider.Size, instanceProp.BoxCollider.Size);
            var reopened = SceneFile.FromJson(SceneFile.ToJson(scene));
            Assert.Equal(updatedPropCollider.Size,
                reopened.Find(mappings[prop.Id])!.BoxCollider!.Size);

            Assert.True(history.Undo(scene));
            Assert.Same(localOverride, instanceRoot.BoxCollider);
            Assert.Equal(new Vector3(1f), instanceProp.BoxCollider!.Size);
            Assert.True(history.Redo(scene));
            Assert.Same(localOverride, instanceRoot.BoxCollider);
            Assert.Equal(updatedPropCollider.Size, instanceProp.BoxCollider!.Size);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TemplateUpdateCarriesSavedTriggerActionsAndUndoRestoresThePreviousAction()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var firstDestinationCellId = Guid.NewGuid();
            var root = new SceneObject(Guid.NewGuid(), "Goal")
            {
                BoxCollider = new SceneBoxColliderComponent(Vector3.Zero, Vector3.One, isTrigger: true),
                Door = new WorldDoorComponent(firstDestinationCellId, Guid.NewGuid(), Quaternion.Identity),
                TriggerAction = new SceneTriggerActionComponent(SceneTriggerActionKind.Collect)
            };
            var source = new SceneGraph();
            source.Add(root);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Goal", path);

            var scene = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision, Vector3.Zero);
            var instanceId = wrapper.TemplateInstance!.ObjectMappings.Single().InstanceObjectId;
            Assert.Equal(SceneTriggerActionKind.Collect, scene.Find(instanceId)!.TriggerAction!.Kind);

            var secondDestinationCellId = Guid.NewGuid();
            root.Door = new WorldDoorComponent(secondDestinationCellId, Guid.NewGuid(), Quaternion.Identity);
            root.TriggerAction = new SceneTriggerActionComponent(SceneTriggerActionKind.Open);
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Goal", path);
            var history = new SceneCommandHistory();
            var command = new UpdateSceneTemplateCommand(wrapper.Id, secondRevision);
            history.Execute(scene, command);
            Assert.Equal(SceneTriggerActionKind.Open, scene.Find(instanceId)!.TriggerAction!.Kind);
            Assert.Equal(secondDestinationCellId, scene.Find(instanceId)!.Door!.DestinationCellId);
            var reopenedInstance = SceneFile.FromJson(SceneFile.ToJson(scene)).Find(instanceId)!;
            Assert.Equal(SceneTriggerActionKind.Open, reopenedInstance.TriggerAction!.Kind);
            Assert.Equal(secondDestinationCellId, reopenedInstance.Door!.DestinationCellId);

            Assert.True(history.Undo(scene));
            Assert.Equal(SceneTriggerActionKind.Collect, scene.Find(instanceId)!.TriggerAction!.Kind);
            Assert.Equal(firstDestinationCellId, scene.Find(instanceId)!.Door!.DestinationCellId);
            Assert.True(history.Redo(scene));
            Assert.Equal(SceneTriggerActionKind.Open, scene.Find(instanceId)!.TriggerAction!.Kind);
            Assert.Equal(secondDestinationCellId, scene.Find(instanceId)!.Door!.DestinationCellId);
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
                Enabled = true,
                ResetPolicy = WorldInstanceResetPolicy.Preserve,
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
            instanceRoot.Enabled = false;
            instanceRoot.ResetPolicy = WorldInstanceResetPolicy.QuestPersistent;
            instanceChild.Transform = new Transform { Position = new Vector3(0f, 7f, 0f) };
            root.Transform = new Transform { Position = new Vector3(3f, 0f, 0f) };
            child.Name = "Updated lamp";
            child.Enabled = false;
            child.ResetPolicy = WorldInstanceResetPolicy.ResetOnCellReset;
            child.Transform = new Transform { Position = new Vector3(0f, 2f, 0f) };
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Workshop", path);
            var history = new SceneCommandHistory();

            history.Execute(scene, new UpdateSceneTemplateCommand(wrapper.Id, secondRevision));

            Assert.Equal("Local workshop name", instanceRoot.Name);
            Assert.False(instanceRoot.Enabled);
            Assert.Equal(WorldInstanceResetPolicy.QuestPersistent, instanceRoot.ResetPolicy);
            Assert.Equal(new Vector3(3f, 0f, 0f), instanceRoot.Transform.Position);
            Assert.Equal("Updated lamp", instanceChild.Name);
            Assert.False(instanceChild.Enabled);
            Assert.Equal(WorldInstanceResetPolicy.ResetOnCellReset, instanceChild.ResetPolicy);
            Assert.Equal(new Vector3(0f, 7f, 0f), instanceChild.Transform.Position);
            Assert.Equal(2, wrapper.TemplateInstance!.AppliedRevision);
            Assert.Equal(new Vector3(3f, 0f, 0f), wrapper.TemplateInstance.ObjectBaselines
                .Single(baseline => baseline.SourceObjectId == root.Id).Position);
            Assert.Equal(mappings[root.Id], wrapper.TemplateInstance.InstanceRootObjectId);

            var reopened = SceneFile.FromJson(SceneFile.ToJson(scene));
            Assert.Equal(2, reopened.Find(wrapper.Id)!.TemplateInstance!.ObjectBaselines.Count);
            Assert.False(reopened.Find(wrapper.Id)!.TemplateInstance!.ObjectBaselines
                .Single(baseline => baseline.SourceObjectId == child.Id).Enabled);
            Assert.Equal(WorldInstanceResetPolicy.ResetOnCellReset, reopened.Find(wrapper.Id)!.TemplateInstance!.ObjectBaselines
                .Single(baseline => baseline.SourceObjectId == child.Id).ResetPolicy);
            Assert.False(reopened.Find(mappings[root.Id])!.Enabled);
            Assert.Equal(WorldInstanceResetPolicy.QuestPersistent, reopened.Find(mappings[root.Id])!.ResetPolicy);
            Assert.Equal(new Vector3(0f, 7f, 0f), reopened.Find(mappings[child.Id])!.Transform.Position);

            Assert.True(history.Undo(scene));
            Assert.Equal(1, wrapper.TemplateInstance!.AppliedRevision);
            Assert.Equal("Local workshop name", instanceRoot.Name);
            Assert.False(instanceRoot.Enabled);
            Assert.Equal(WorldInstanceResetPolicy.QuestPersistent, instanceRoot.ResetPolicy);
            Assert.Equal("Old lamp", instanceChild.Name);
            Assert.True(instanceChild.Enabled);
            Assert.Equal(WorldInstanceResetPolicy.Preserve, instanceChild.ResetPolicy);
            Assert.Equal(new Vector3(0f, 7f, 0f), instanceChild.Transform.Position);
            Assert.True(history.Redo(scene));
            Assert.Equal(2, wrapper.TemplateInstance!.AppliedRevision);
            Assert.Equal("Local workshop name", instanceRoot.Name);
            Assert.False(instanceRoot.Enabled);
            Assert.Equal(WorldInstanceResetPolicy.QuestPersistent, instanceRoot.ResetPolicy);
            Assert.False(instanceChild.Enabled);
            Assert.Equal(WorldInstanceResetPolicy.ResetOnCellReset, instanceChild.ResetPolicy);
            Assert.Equal(new Vector3(0f, 7f, 0f), instanceChild.Transform.Position);

            root.Name = "Final workshop name";
            root.Transform = new Transform { Position = new Vector3(4f, 0f, 0f) };
            child.Name = "Final lamp";
            child.Enabled = true;
            child.ResetPolicy = WorldInstanceResetPolicy.Preserve;
            child.Transform = new Transform { Position = new Vector3(0f, 3f, 0f) };
            var thirdRevision = SceneTemplateFile.Save(source, root.Id, "Workshop", path);
            history.Execute(scene, new UpdateSceneTemplateCommand(wrapper.Id, thirdRevision));
            Assert.Equal(3, wrapper.TemplateInstance.AppliedRevision);
            Assert.Equal("Local workshop name", instanceRoot.Name);
            Assert.Equal(new Vector3(4f, 0f, 0f), instanceRoot.Transform.Position);
            Assert.Equal("Final lamp", instanceChild.Name);
            Assert.True(instanceChild.Enabled);
            Assert.Equal(WorldInstanceResetPolicy.Preserve, instanceChild.ResetPolicy);
            Assert.Equal(new Vector3(0f, 7f, 0f), instanceChild.Transform.Position);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void UpdateRejectsDifferentTemplateWithoutChangingInstance()
    {
        var path = TemporaryTemplatePath();
        var otherPath = TemporaryTemplatePath();
        try
        {
            var source = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Room");
            source.Add(root);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);
            var otherTemplate = SceneTemplateFile.Save(source, root.Id, "Other room", otherPath);
            var destination = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(destination, firstRevision, Vector3.Zero);
            var instanceRootId = wrapper.TemplateInstance!.InstanceRootObjectId;

            Assert.Throws<InvalidOperationException>(() =>
                new UpdateSceneTemplateCommand(wrapper.Id, otherTemplate).Apply(destination));

            Assert.Equal(1, wrapper.TemplateInstance.AppliedRevision);
            Assert.Equal("Room", destination.Find(instanceRootId)!.Name);
            Assert.Equal(2, destination.Objects.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(otherPath)) File.Delete(otherPath);
        }
    }

    [Fact]
    public void UpdateMergesSharedAssetAndLodComponentsWhilePreservingLocalOverrides()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var sourceAsset = new GltfAssetReference(Guid.NewGuid(), "Assets/source-v1.glb");
            var updatedAsset = new GltfAssetReference(Guid.NewGuid(), "Assets/source-v2.glb");
            var localAsset = new GltfAssetReference(Guid.NewGuid(), "Assets/local.glb");
            var sourceNearV1 = new GltfAssetReference(Guid.NewGuid(), "Assets/near-v1.glb");
            var sourceFarV1 = new GltfAssetReference(Guid.NewGuid(), "Assets/far-v1.glb");
            var sourceNearV2 = new GltfAssetReference(Guid.NewGuid(), "Assets/near-v2.glb");
            var sourceFarV2 = new GltfAssetReference(Guid.NewGuid(), "Assets/far-v2.glb");
            var localNear = new GltfAssetReference(Guid.NewGuid(), "Assets/local-near.glb");
            var localFar = new GltfAssetReference(Guid.NewGuid(), "Assets/local-far.glb");
            var sourceLodV1 = new GltfStaticMeshLod(sourceNearV1, sourceFarV1, 100f, 80f);
            var sourceLodV2 = new GltfStaticMeshLod(sourceNearV2, sourceFarV2, 150f, 120f);
            var localLod = new GltfStaticMeshLod(localNear, localFar, 60f, 45f);
            var source = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Assets")
            {
                GltfAsset = sourceAsset
            };
            var updatedAssetObject = new SceneObject(Guid.NewGuid(), "Default asset")
            {
                GltfAsset = sourceAsset
            };
            var overriddenAssetObject = new SceneObject(Guid.NewGuid(), "Overridden asset")
            {
                GltfAsset = sourceAsset
            };
            var updatedLodObject = new SceneObject(Guid.NewGuid(), "Default LOD")
            {
                StaticMeshLod = sourceLodV1
            };
            var overriddenLodObject = new SceneObject(Guid.NewGuid(), "Overridden LOD")
            {
                StaticMeshLod = sourceLodV1
            };
            source.Add(root);
            source.Add(updatedAssetObject);
            source.Add(overriddenAssetObject);
            source.Add(updatedLodObject);
            source.Add(overriddenLodObject);
            source.SetParent(updatedAssetObject.Id, root.Id);
            source.SetParent(overriddenAssetObject.Id, root.Id);
            source.SetParent(updatedLodObject.Id, root.Id);
            source.SetParent(overriddenLodObject.Id, root.Id);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Assets", path);

            var scene = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision, Vector3.Zero);
            var mappings = wrapper.TemplateInstance!.ObjectMappings.ToDictionary(
                mapping => mapping.SourceObjectId, mapping => mapping.InstanceObjectId);
            scene.Find(mappings[overriddenAssetObject.Id])!.GltfAsset = localAsset;
            scene.Find(mappings[overriddenLodObject.Id])!.StaticMeshLod = localLod;
            updatedAssetObject.GltfAsset = updatedAsset;
            overriddenAssetObject.GltfAsset = updatedAsset;
            updatedLodObject.StaticMeshLod = sourceLodV2;
            overriddenLodObject.StaticMeshLod = sourceLodV2;
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Assets", path);
            var history = new SceneCommandHistory();

            history.Execute(scene, new UpdateSceneTemplateCommand(wrapper.Id, secondRevision));

            Assert.Equal(updatedAsset.AssetId, scene.Find(mappings[updatedAssetObject.Id])!.GltfAsset!.AssetId);
            Assert.Equal(localAsset.AssetId, scene.Find(mappings[overriddenAssetObject.Id])!.GltfAsset!.AssetId);
            var updatedLod = scene.Find(mappings[updatedLodObject.Id])!.StaticMeshLod!;
            Assert.Equal(sourceNearV2.AssetId, updatedLod.NearAsset.AssetId);
            Assert.Equal(sourceFarV2.AssetId, updatedLod.FarAsset.AssetId);
            Assert.Equal(150f, updatedLod.EnterFarDistance);
            Assert.Equal(120f, updatedLod.ExitFarDistance);
            var overriddenLod = scene.Find(mappings[overriddenLodObject.Id])!.StaticMeshLod!;
            Assert.Equal(localNear.AssetId, overriddenLod.NearAsset.AssetId);
            Assert.Equal(localFar.AssetId, overriddenLod.FarAsset.AssetId);
            Assert.Equal(60f, overriddenLod.EnterFarDistance);
            Assert.Equal(45f, overriddenLod.ExitFarDistance);

            var reopened = SceneFile.FromJson(SceneFile.ToJson(scene));
            Assert.Equal(19, SceneFile.CurrentVersion);
            var baselines = reopened.Find(wrapper.Id)!.TemplateInstance!.ObjectBaselines
                .ToDictionary(baseline => baseline.SourceObjectId);
            Assert.True(baselines[updatedAssetObject.Id].HasGltfAssetBaseline);
            Assert.Equal(updatedAsset.AssetId, baselines[updatedAssetObject.Id].GltfAsset!.AssetId);
            Assert.True(baselines[updatedLodObject.Id].HasStaticMeshLodBaseline);
            Assert.Equal(sourceNearV2.AssetId, baselines[updatedLodObject.Id].StaticMeshLod!.NearAsset.AssetId);
            Assert.Equal(150f, baselines[updatedLodObject.Id].StaticMeshLod!.EnterFarDistance);

            Assert.True(history.Undo(scene));
            Assert.Equal(sourceAsset.AssetId, scene.Find(mappings[updatedAssetObject.Id])!.GltfAsset!.AssetId);
            Assert.Equal(localAsset.AssetId, scene.Find(mappings[overriddenAssetObject.Id])!.GltfAsset!.AssetId);
            Assert.Equal(100f, scene.Find(mappings[updatedLodObject.Id])!.StaticMeshLod!.EnterFarDistance);
            Assert.Equal(60f, scene.Find(mappings[overriddenLodObject.Id])!.StaticMeshLod!.EnterFarDistance);
            Assert.True(history.Redo(scene));
            Assert.Equal(updatedAsset.AssetId, scene.Find(mappings[updatedAssetObject.Id])!.GltfAsset!.AssetId);
            Assert.Equal(localAsset.AssetId, scene.Find(mappings[overriddenAssetObject.Id])!.GltfAsset!.AssetId);
            Assert.Equal(150f, scene.Find(mappings[updatedLodObject.Id])!.StaticMeshLod!.EnterFarDistance);
            Assert.Equal(60f, scene.Find(mappings[overriddenLodObject.Id])!.StaticMeshLod!.EnterFarDistance);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void UpdateMergesCharacterPlaybackDefaultsAndPreservesInstanceAttachments()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var source = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Animated character")
            {
                GltfAsset = new GltfAssetReference(Guid.NewGuid(), "Assets/character.glb"),
                CharacterSettings = new GltfCharacterSettings
                {
                    ClipName = "Idle",
                    Time = 0.25f,
                    Speed = 1f,
                    Loop = true,
                    IsPlaying = false,
                    BlendAmount = 0.5f
                }
            };
            var animatedProp = new SceneObject(Guid.NewGuid(), "New animated prop")
            {
                GltfAsset = new GltfAssetReference(Guid.NewGuid(), "Assets/animated-prop.glb")
            };
            var sourceAttachmentId = Guid.NewGuid();
            var addedSourceAttachmentId = Guid.NewGuid();
            root.CharacterSettings.Attachments.Add(new GltfBoneAttachmentReference(
                sourceAttachmentId, "hand_r", Matrix.Identity));
            source.Add(root);
            source.Add(animatedProp);
            source.SetParent(animatedProp.Id, root.Id);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Animated character", path);
            var scene = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision, Vector3.Zero);
            var instanceObjectId = wrapper.TemplateInstance!.InstanceRootObjectId;
            var mappings = wrapper.TemplateInstance.ObjectMappings.ToDictionary(
                mapping => mapping.SourceObjectId, mapping => mapping.InstanceObjectId);
            var animatedPropInstanceId = mappings[animatedProp.Id];
            var targetSettings = scene.Find(instanceObjectId)!.CharacterSettings!;
            var instanceAttachmentId = Assert.Single(targetSettings.Attachments).Id;
            targetSettings.ClipName = "PlayerRun";
            targetSettings.Attachments.Clear();
            targetSettings.Attachments.Add(new GltfBoneAttachmentReference(instanceAttachmentId,
                "local_grip", Matrix.CreateTranslation(1f, 2f, 3f)));

            root.CharacterSettings.ClipName = "Walk";
            root.CharacterSettings.Time = 0.75f;
            root.CharacterSettings.Speed = 2f;
            root.CharacterSettings.Loop = false;
            root.CharacterSettings.IsPlaying = true;
            root.CharacterSettings.CrossfadeClipName = "Attack";
            root.CharacterSettings.BlendAmount = 0.2f;
            root.CharacterSettings.Attachments.Clear();
            root.CharacterSettings.Attachments.Add(new GltfBoneAttachmentReference(
                sourceAttachmentId, "weapon_grip", Matrix.CreateTranslation(4f, 0f, 0f)));
            root.CharacterSettings.Attachments.Add(new GltfBoneAttachmentReference(
                addedSourceAttachmentId, "hand_l", Matrix.Identity));
            animatedProp.CharacterSettings = new GltfCharacterSettings { ClipName = "PropIdle" };
            var addedPropAttachmentId = Guid.NewGuid();
            animatedProp.CharacterSettings.Attachments.Add(new GltfBoneAttachmentReference(
                addedPropAttachmentId, "prop_joint", Matrix.Identity));
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Animated character", path);
            var history = new SceneCommandHistory();

            history.Execute(scene, new UpdateSceneTemplateCommand(wrapper.Id, secondRevision));

            var updated = scene.Find(instanceObjectId)!.CharacterSettings!;
            Assert.Equal("PlayerRun", updated.ClipName);
            Assert.Equal(0.75f, updated.Time);
            Assert.Equal(2f, updated.Speed);
            Assert.False(updated.Loop);
            Assert.True(updated.IsPlaying);
            Assert.Equal("Attack", updated.CrossfadeClipName);
            Assert.Equal(0.2f, updated.BlendAmount);
            Assert.Equal(2, updated.Attachments.Count);
            var retainedAttachment = updated.Attachments.Single(attachment => attachment.Id == instanceAttachmentId);
            Assert.Equal(instanceAttachmentId, retainedAttachment.Id);
            Assert.Equal("local_grip", retainedAttachment.BoneName);
            Assert.Equal(Matrix.CreateTranslation(1f, 2f, 3f), retainedAttachment.LocalOffset);
            var addedCharacterAttachment = Assert.Single(updated.Attachments,
                attachment => attachment.BoneName == "hand_l");
            Assert.NotEqual(addedSourceAttachmentId, addedCharacterAttachment.Id);
            var addedPropSettings = scene.Find(animatedPropInstanceId)!.CharacterSettings!;
            Assert.Equal("PropIdle", addedPropSettings.ClipName);
            var remappedPropAttachment = Assert.Single(addedPropSettings.Attachments);
            Assert.NotEqual(addedPropAttachmentId, remappedPropAttachment.Id);
            Assert.Equal("prop_joint", remappedPropAttachment.BoneName);

            var reopened = SceneFile.FromJson(SceneFile.ToJson(scene));
            Assert.Equal(19, SceneFile.CurrentVersion);
            var characterBaseline = reopened.Find(wrapper.Id)!.TemplateInstance!.ObjectBaselines
                .Single(baseline => baseline.SourceObjectId == root.Id).CharacterSettingsBaseline!;
            Assert.Equal("Walk", characterBaseline.ClipName);
            Assert.Equal(0.75f, characterBaseline.Time);
            Assert.Equal(2f, characterBaseline.Speed);
            Assert.False(characterBaseline.Loop);
            Assert.True(characterBaseline.IsPlaying);
            Assert.Equal("Attack", characterBaseline.CrossfadeClipName);
            Assert.Equal(0.2f, characterBaseline.BlendAmount);
            Assert.Equal(2, reopened.Find(mappings[root.Id])!.CharacterSettings!.Attachments.Count);
            Assert.Contains(characterBaseline.AttachmentBaselines, attachment =>
                attachment.SourceAttachmentId == sourceAttachmentId
                && attachment.InstanceAttachmentId == instanceAttachmentId
                && attachment.BoneName == "weapon_grip");
            Assert.Contains(characterBaseline.AttachmentBaselines, attachment =>
                attachment.SourceAttachmentId == addedSourceAttachmentId
                && attachment.InstanceAttachmentId == addedCharacterAttachment.Id);
            Assert.Equal("PropIdle", reopened.Find(animatedPropInstanceId)!.CharacterSettings!.ClipName);

            Assert.True(history.Undo(scene));
            Assert.Equal("PlayerRun", scene.Find(instanceObjectId)!.CharacterSettings!.ClipName);
            Assert.Null(scene.Find(animatedPropInstanceId)!.CharacterSettings);
            Assert.Single(scene.Find(instanceObjectId)!.CharacterSettings!.Attachments);
            Assert.Equal("local_grip", scene.Find(instanceObjectId)!.CharacterSettings!.Attachments[0].BoneName);
            Assert.True(history.Redo(scene));
            Assert.Equal("PlayerRun", scene.Find(instanceObjectId)!.CharacterSettings!.ClipName);
            Assert.Equal("PropIdle", scene.Find(animatedPropInstanceId)!.CharacterSettings!.ClipName);
            Assert.Equal(0.75f, scene.Find(instanceObjectId)!.CharacterSettings!.Time);
            Assert.Equal(2, scene.Find(instanceObjectId)!.CharacterSettings!.Attachments.Count);
            Assert.Equal("local_grip", scene.Find(instanceObjectId)!.CharacterSettings!.Attachments
                .Single(attachment => attachment.Id == instanceAttachmentId).BoneName);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void UpdateAddsAndRemovesObjectsAndRetainsEditedContentAsOrphans()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var source = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Interior");
            var editedSource = new SceneObject(Guid.NewGuid(), "Cabinet");
            var removedSource = new SceneObject(Guid.NewGuid(), "Old stool");
            var overriddenRemovedSource = new SceneObject(Guid.NewGuid(), "Retired sign")
            {
                GltfAsset = new GltfAssetReference(Guid.NewGuid(), "Assets/retired-sign.glb")
            };
            var overriddenCharacterSource = new SceneObject(Guid.NewGuid(), "Retired actor")
            {
                GltfAsset = new GltfAssetReference(Guid.NewGuid(), "Assets/retired-actor.glb"),
                CharacterSettings = new GltfCharacterSettings { ClipName = "Idle" }
            };
            var unchangedCharacterSource = new SceneObject(Guid.NewGuid(), "Retired mannequin")
            {
                GltfAsset = new GltfAssetReference(Guid.NewGuid(), "Assets/retired-mannequin.glb"),
                CharacterSettings = new GltfCharacterSettings { ClipName = "Idle" }
            };
            var linkedSpawnSourceId = Guid.NewGuid();
            var linkedSpawn = new SceneObject(Guid.NewGuid(), "Linked old spawn")
            {
                SpawnPoint = new WorldSpawnComponent(linkedSpawnSourceId)
            };
            var linkedDoor = new SceneObject(Guid.NewGuid(), "Existing linked door")
            {
                Door = new WorldDoorComponent(Guid.NewGuid(), linkedSpawnSourceId, Quaternion.Identity)
            };
            source.Add(root);
            source.Add(editedSource);
            source.Add(removedSource);
            source.Add(overriddenRemovedSource);
            source.Add(overriddenCharacterSource);
            source.Add(unchangedCharacterSource);
            source.Add(linkedSpawn);
            source.Add(linkedDoor);
            source.SetParent(editedSource.Id, root.Id);
            source.SetParent(removedSource.Id, root.Id);
            source.SetParent(overriddenRemovedSource.Id, root.Id);
            source.SetParent(overriddenCharacterSource.Id, root.Id);
            source.SetParent(unchangedCharacterSource.Id, root.Id);
            source.SetParent(linkedSpawn.Id, root.Id);
            source.SetParent(linkedDoor.Id, root.Id);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Interior", path);

            var scene = new SceneGraph();
            var targetCellId = Guid.NewGuid();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision, Vector3.Zero, targetCellId);
            var originalMappings = wrapper.TemplateInstance!.ObjectMappings.ToDictionary(
                mapping => mapping.SourceObjectId, mapping => mapping.InstanceObjectId);
            var editedInstanceId = originalMappings[editedSource.Id];
            var removedInstanceId = originalMappings[removedSource.Id];
            var overriddenRemovedInstanceId = originalMappings[overriddenRemovedSource.Id];
            var overriddenCharacterInstanceId = originalMappings[overriddenCharacterSource.Id];
            var unchangedCharacterInstanceId = originalMappings[unchangedCharacterSource.Id];
            var linkedSpawnInstanceId = originalMappings[linkedSpawn.Id];
            var linkedDoorInstanceId = originalMappings[linkedDoor.Id];
            var linkedSpawnInstanceSpawnId = scene.Find(linkedSpawnInstanceId)!.SpawnPoint!.Id;
            scene.Find(editedInstanceId)!.Name = "Player cabinet";
            scene.Find(overriddenRemovedInstanceId)!.Enabled = false;
            scene.Find(overriddenRemovedInstanceId)!.ResetPolicy = WorldInstanceResetPolicy.QuestPersistent;
            var localAsset = new GltfAssetReference(Guid.NewGuid(), "Assets/player-sign.glb");
            scene.Find(overriddenRemovedInstanceId)!.GltfAsset = localAsset;
            scene.Find(overriddenCharacterInstanceId)!.CharacterSettings!.ClipName = "Player dance";
            var playerProp = new SceneObject(Guid.NewGuid(), "Stored prop");
            scene.Add(playerProp);
            scene.SetParent(playerProp.Id, editedInstanceId);

            source.Remove(editedSource.Id);
            source.Remove(removedSource.Id);
            source.Remove(overriddenRemovedSource.Id);
            source.Remove(overriddenCharacterSource.Id);
            source.Remove(unchangedCharacterSource.Id);
            source.Remove(linkedSpawn.Id);
            var spawnSourceId = Guid.NewGuid();
            var spawnMarker = new SceneObject(Guid.NewGuid(), "New spawn")
            {
                SpawnPoint = new WorldSpawnComponent(spawnSourceId)
            };
            var door = new SceneObject(Guid.NewGuid(), "New door")
            {
                Door = new WorldDoorComponent(Guid.NewGuid(), spawnSourceId, Quaternion.Identity)
            };
            source.Add(spawnMarker);
            source.Add(door);
            source.SetParent(spawnMarker.Id, root.Id);
            source.SetParent(door.Id, root.Id);
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Interior", path);
            var history = new SceneCommandHistory();
            var command = new UpdateSceneTemplateCommand(wrapper.Id, secondRevision);

            history.Execute(scene, command);

            var updated = wrapper.TemplateInstance!;
            var newMappings = updated.ObjectMappings.ToDictionary(
                mapping => mapping.SourceObjectId, mapping => mapping.InstanceObjectId);
            Assert.Equal(4, updated.ObjectMappings.Count);
            Assert.Equal(originalMappings[root.Id], newMappings[root.Id]);
            Assert.Equal(linkedDoorInstanceId, newMappings[linkedDoor.Id]);
            Assert.NotEqual(spawnMarker.Id, newMappings[spawnMarker.Id]);
            Assert.Equal("Player cabinet", scene.Find(editedInstanceId)!.Name);
            Assert.Null(scene.Find(removedInstanceId));
            Assert.NotNull(scene.Find(overriddenRemovedInstanceId));
            Assert.False(scene.Find(overriddenRemovedInstanceId)!.Enabled);
            Assert.Equal(WorldInstanceResetPolicy.QuestPersistent,
                scene.Find(overriddenRemovedInstanceId)!.ResetPolicy);
            Assert.Equal(localAsset.AssetId, scene.Find(overriddenRemovedInstanceId)!.GltfAsset!.AssetId);
            Assert.Contains(overriddenRemovedInstanceId, command.OrphanedObjectIds);
            Assert.Equal("Player dance", scene.Find(overriddenCharacterInstanceId)!.CharacterSettings!.ClipName);
            Assert.Contains(overriddenCharacterInstanceId, command.OrphanedObjectIds);
            Assert.Null(scene.Find(unchangedCharacterInstanceId));
            Assert.NotNull(scene.Find(linkedSpawnInstanceId));
            Assert.Contains(linkedSpawnInstanceId, command.OrphanedObjectIds);
            Assert.Equal(linkedSpawnInstanceSpawnId,
                scene.Find(linkedDoorInstanceId)!.Door!.DestinationSpawnId);
            Assert.Contains(editedInstanceId, command.OrphanedObjectIds);
            Assert.Contains(playerProp.Id, command.OrphanedObjectIds);
            Assert.Equal(targetCellId, updated.TargetWorldCellId);
            var placedSpawn = scene.Find(newMappings[spawnMarker.Id])!.SpawnPoint!;
            var placedDoor = scene.Find(newMappings[door.Id])!.Door!;
            Assert.NotEqual(spawnSourceId, placedSpawn.Id);
            Assert.Equal(placedSpawn.Id, placedDoor.DestinationSpawnId);
            Assert.Equal(targetCellId, placedDoor.DestinationCellId);

            var reopened = SceneFile.FromJson(SceneFile.ToJson(scene));
            Assert.Equal(updated.OrphanedObjectIds, reopened.Find(wrapper.Id)!.TemplateInstance!.OrphanedObjectIds);
            Assert.Equal(targetCellId, reopened.Find(wrapper.Id)!.TemplateInstance!.TargetWorldCellId);

            Assert.True(history.Undo(scene));
            Assert.Equal(1, wrapper.TemplateInstance!.AppliedRevision);
            Assert.Equal(8, wrapper.TemplateInstance.ObjectMappings.Count);
            Assert.NotNull(scene.Find(removedInstanceId));
            Assert.NotNull(scene.Find(overriddenRemovedInstanceId));
            Assert.NotNull(scene.Find(overriddenCharacterInstanceId));
            Assert.NotNull(scene.Find(unchangedCharacterInstanceId));
            Assert.Null(scene.Find(newMappings[spawnMarker.Id]));
            Assert.Equal(editedInstanceId, scene.Find(playerProp.Id)!.ParentId);

            Assert.True(history.Redo(scene));
            Assert.Equal(2, wrapper.TemplateInstance!.AppliedRevision);
            Assert.Equal(newMappings[spawnMarker.Id], wrapper.TemplateInstance.ObjectMappings
                .Single(mapping => mapping.SourceObjectId == spawnMarker.Id).InstanceObjectId);
            Assert.Contains(editedInstanceId, wrapper.TemplateInstance.OrphanedObjectIds);
            Assert.Contains(linkedSpawnInstanceId, wrapper.TemplateInstance.OrphanedObjectIds);
            Assert.Contains(overriddenRemovedInstanceId, wrapper.TemplateInstance.OrphanedObjectIds);
            Assert.Contains(overriddenCharacterInstanceId, wrapper.TemplateInstance.OrphanedObjectIds);
            Assert.Null(scene.Find(unchangedCharacterInstanceId));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void UpdateMergesWorldComponentBaselinesAndPreservesLocalOverrides()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var sourceSpawnId = Guid.NewGuid();
            var updatedSourceSpawnId = Guid.NewGuid();
            var destinationCellId = Guid.NewGuid();
            var externalSpawnId = Guid.NewGuid();
            var entityInstanceId = Guid.NewGuid();
            var localEntityInstanceId = Guid.NewGuid();
            var source = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Room");
            var spawnObject = new SceneObject(Guid.NewGuid(), "Entry spawn")
            {
                SpawnPoint = new WorldSpawnComponent(sourceSpawnId)
            };
            var doorObject = new SceneObject(Guid.NewGuid(), "Entry door")
            {
                Door = new WorldDoorComponent(Guid.NewGuid(), sourceSpawnId, Quaternion.Identity)
            };
            var entityObject = new SceneObject(Guid.NewGuid(), "Room item")
            {
                WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Item,
                    "items.old", entityInstanceId)
            };
            var overriddenDoorObject = new SceneObject(Guid.NewGuid(), "Local door")
            {
                Door = new WorldDoorComponent(Guid.NewGuid(), sourceSpawnId, Quaternion.Identity)
            };
            var overriddenEntityObject = new SceneObject(Guid.NewGuid(), "Local item")
            {
                WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Item,
                    "items.old", localEntityInstanceId)
            };
            var removedComponentsObject = new SceneObject(Guid.NewGuid(), "Retired components")
            {
                Door = new WorldDoorComponent(Guid.NewGuid(), Guid.NewGuid(), Quaternion.Identity),
                SpawnPoint = new WorldSpawnComponent(Guid.NewGuid()),
                WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Item,
                    "items.retired", Guid.NewGuid())
            };
            foreach (var item in new[] { root, spawnObject, doorObject, entityObject,
                         overriddenDoorObject, overriddenEntityObject, removedComponentsObject })
                source.Add(item);
            foreach (var item in new[] { spawnObject, doorObject, entityObject,
                         overriddenDoorObject, overriddenEntityObject, removedComponentsObject })
                source.SetParent(item.Id, root.Id);

            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);
            var scene = new SceneGraph();
            var targetCellId = Guid.NewGuid();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision,
                Vector3.Zero, targetCellId);
            var wrapperId = wrapper.Id;
            var mappings = wrapper.TemplateInstance!.ObjectMappings.ToDictionary(
                mapping => mapping.SourceObjectId, mapping => mapping.InstanceObjectId);
            var localSpawnId = Guid.NewGuid();
            scene.Find(mappings[spawnObject.Id])!.SpawnPoint = new WorldSpawnComponent(localSpawnId);
            var entityInstanceIdInScene = scene.Find(mappings[entityObject.Id])!.WorldEntity!.InstanceId;
            var localDoor = new WorldDoorComponent(Guid.NewGuid(), externalSpawnId,
                Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2));
            scene.Find(mappings[overriddenDoorObject.Id])!.Door = localDoor;
            scene.Find(mappings[overriddenEntityObject.Id])!.WorldEntity =
                new WorldEntityPlacementComponent(WorldEntityKind.Item, "items.local", localEntityInstanceId);

            // The baseline, including stable component IDs, must survive a scene save/reopen.
            scene = SceneFile.FromJson(SceneFile.ToJson(scene));
            var persistedWrapper = scene.Find(wrapperId)!;
            var persistedBaselines = persistedWrapper.TemplateInstance!.ObjectBaselines
                .ToDictionary(baseline => baseline.SourceObjectId);
            Assert.True(persistedBaselines[spawnObject.Id].HasSpawnPointBaseline);
            Assert.True(persistedBaselines[doorObject.Id].HasDoorBaseline);
            Assert.True(persistedBaselines[entityObject.Id].HasWorldEntityBaseline);

            var updatedFacing = Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2);
            spawnObject.SpawnPoint = new WorldSpawnComponent(updatedSourceSpawnId);
            doorObject.Door = new WorldDoorComponent(Guid.NewGuid(), updatedSourceSpawnId, updatedFacing);
            entityObject.WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Item,
                "items.updated", entityInstanceId);
            overriddenDoorObject.Door = new WorldDoorComponent(Guid.NewGuid(), updatedSourceSpawnId,
                Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.Pi));
            overriddenEntityObject.WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Item,
                "items.updated", localEntityInstanceId);
            removedComponentsObject.Door = null;
            removedComponentsObject.SpawnPoint = null;
            removedComponentsObject.WorldEntity = null;
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);
            var history = new SceneCommandHistory();
            var command = new UpdateSceneTemplateCommand(wrapperId, secondRevision);

            history.Execute(scene, command);

            var updatedSpawn = scene.Find(mappings[spawnObject.Id])!.SpawnPoint!;
            Assert.Equal(localSpawnId, updatedSpawn.Id);
            var updatedDoor = scene.Find(mappings[doorObject.Id])!.Door!;
            Assert.Equal(targetCellId, updatedDoor.DestinationCellId);
            Assert.Equal(localSpawnId, updatedDoor.DestinationSpawnId);
            Assert.True(MathF.Abs(Quaternion.Dot(updatedFacing, updatedDoor.Facing)) > 0.99999f);
            Assert.Equal("items.updated", scene.Find(mappings[entityObject.Id])!.WorldEntity!.DefinitionId);
            Assert.Equal(entityInstanceIdInScene,
                scene.Find(mappings[entityObject.Id])!.WorldEntity!.InstanceId);
            Assert.Equal(localDoor.DestinationCellId,
                scene.Find(mappings[overriddenDoorObject.Id])!.Door!.DestinationCellId);
            Assert.Equal(localDoor.DestinationSpawnId,
                scene.Find(mappings[overriddenDoorObject.Id])!.Door!.DestinationSpawnId);
            Assert.True(MathF.Abs(Quaternion.Dot(localDoor.Facing,
                scene.Find(mappings[overriddenDoorObject.Id])!.Door!.Facing)) > 0.99999f);
            Assert.Equal("items.local",
                scene.Find(mappings[overriddenEntityObject.Id])!.WorldEntity!.DefinitionId);
            Assert.Null(scene.Find(mappings[removedComponentsObject.Id])!.Door);
            Assert.Null(scene.Find(mappings[removedComponentsObject.Id])!.SpawnPoint);
            Assert.Null(scene.Find(mappings[removedComponentsObject.Id])!.WorldEntity);

            var reopened = SceneFile.FromJson(SceneFile.ToJson(scene));
            var savedSpawnBaseline = reopened.Find(wrapperId)!.TemplateInstance!.ObjectBaselines
                .Single(baseline => baseline.SourceObjectId == spawnObject.Id);
            Assert.Equal(updatedSourceSpawnId, savedSpawnBaseline.SourceSpawnPointId);
            Assert.Equal("items.updated",
                reopened.Find(wrapperId)!.TemplateInstance!.ObjectBaselines
                    .Single(baseline => baseline.SourceObjectId == entityObject.Id)
                    .WorldEntity!.DefinitionId);

            Assert.True(history.Undo(scene));
            Assert.Equal("items.old", scene.Find(mappings[entityObject.Id])!.WorldEntity!.DefinitionId);
            Assert.Equal(localSpawnId, scene.Find(mappings[spawnObject.Id])!.SpawnPoint!.Id);
            Assert.NotNull(scene.Find(mappings[removedComponentsObject.Id])!.Door);
            Assert.True(MathF.Abs(Quaternion.Dot(localDoor.Facing,
                scene.Find(mappings[overriddenDoorObject.Id])!.Door!.Facing)) > 0.99999f);
            Assert.True(history.Redo(scene));
            Assert.Equal("items.updated", scene.Find(mappings[entityObject.Id])!.WorldEntity!.DefinitionId);
            Assert.Null(scene.Find(mappings[removedComponentsObject.Id])!.Door);
            Assert.Null(scene.Find(mappings[removedComponentsObject.Id])!.SpawnPoint);
            Assert.Null(scene.Find(mappings[removedComponentsObject.Id])!.WorldEntity);
            Assert.True(MathF.Abs(Quaternion.Dot(localDoor.Facing,
                scene.Find(mappings[overriddenDoorObject.Id])!.Door!.Facing)) > 0.99999f);
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
            legacyJson.Remove("PlaySettings");
            RemoveVersionTwentyBehaviourFields(legacyJson);
            var wrapperData = legacyJson["Objects"]!.AsArray().Single(item =>
                Guid.Parse((string)item!["Id"]!) == wrapper.Id)!;
            wrapperData["TemplateInstance"]!.AsObject().Remove("ObjectBaselines");

            var legacyScene = SceneFile.FromJson(legacyJson.ToJsonString());
            var legacyWrapper = legacyScene.Find(wrapper.Id)!;
            Assert.Empty(legacyWrapper.TemplateInstance!.ObjectBaselines);
            Assert.Contains($"\"Version\": {SceneFile.CurrentVersion}", SceneFile.ToJson(legacyScene), StringComparison.Ordinal);

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

    [Fact]
    public void VersionNineTemplateBaselinesPreserveUnknownScalarOverridesThenUpgrade()
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
            var rootInstanceId = wrapper.TemplateInstance!.InstanceRootObjectId;
            scene.Find(rootInstanceId)!.Enabled = false;
            scene.Find(rootInstanceId)!.ResetPolicy = WorldInstanceResetPolicy.QuestPersistent;

            var versionNineJson = JsonNode.Parse(SceneFile.ToJson(scene))!.AsObject();
            versionNineJson["Version"] = 9;
            versionNineJson.Remove("PlaySettings");
            RemoveVersionTwentyBehaviourFields(versionNineJson);
            var wrapperData = versionNineJson["Objects"]!.AsArray().Single(item =>
                Guid.Parse((string)item!["Id"]!) == wrapper.Id)!;
            foreach (var baseline in wrapperData["TemplateInstance"]!["ObjectBaselines"]!.AsArray())
            {
                baseline!.AsObject().Remove("Enabled");
                baseline.AsObject().Remove("ResetPolicy");
                baseline.AsObject().Remove("HasGltfAssetBaseline");
                baseline.AsObject().Remove("GltfAssetId");
                baseline.AsObject().Remove("GltfAssetPath");
                baseline.AsObject().Remove("HasStaticMeshLodBaseline");
                baseline.AsObject().Remove("StaticMeshLod");
                baseline.AsObject().Remove("HasCharacterSettingsBaseline");
                baseline.AsObject().Remove("CharacterSettingsBaseline");
                RemoveVersionFourteenWorldBaselines(baseline.AsObject());
                RemoveVersionFifteenBoxColliderBaselines(baseline.AsObject());
            }

            var migrated = SceneFile.FromJson(versionNineJson.ToJsonString());
            var migratedWrapper = migrated.Find(wrapper.Id)!;
            var legacyBaseline = Assert.Single(migratedWrapper.TemplateInstance!.ObjectBaselines);
            Assert.Null(legacyBaseline.Enabled);
            Assert.Null(legacyBaseline.ResetPolicy);
            Assert.False(legacyBaseline.HasGltfAssetBaseline);
            Assert.False(legacyBaseline.HasStaticMeshLodBaseline);

            root.Name = "Updated room";
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);
            new UpdateSceneTemplateCommand(wrapper.Id, secondRevision).Apply(migrated);

            var updated = migrated.Find(rootInstanceId)!;
            Assert.Equal("Updated room", updated.Name);
            Assert.False(updated.Enabled);
            Assert.Equal(WorldInstanceResetPolicy.QuestPersistent, updated.ResetPolicy);
            Assert.True(migratedWrapper.TemplateInstance!.ObjectBaselines.Single().Enabled);
            Assert.Equal(WorldInstanceResetPolicy.Preserve,
                migratedWrapper.TemplateInstance.ObjectBaselines.Single().ResetPolicy);
            Assert.True(migratedWrapper.TemplateInstance.ObjectBaselines.Single().HasGltfAssetBaseline);
            Assert.True(migratedWrapper.TemplateInstance.ObjectBaselines.Single().HasStaticMeshLodBaseline);
            Assert.Contains($"\"Version\": {SceneFile.CurrentVersion}", SceneFile.ToJson(migrated), StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void VersionTenTemplateBaselinesPreserveUnknownAssetOverridesThenUpgrade()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var sourceAssetV1 = new GltfAssetReference(Guid.NewGuid(), "Assets/room-v1.glb");
            var sourceAssetV2 = new GltfAssetReference(Guid.NewGuid(), "Assets/room-v2.glb");
            var localAsset = new GltfAssetReference(Guid.NewGuid(), "Assets/local-room.glb");
            var nearV1 = new GltfAssetReference(Guid.NewGuid(), "Assets/near-v1.glb");
            var farV1 = new GltfAssetReference(Guid.NewGuid(), "Assets/far-v1.glb");
            var nearV2 = new GltfAssetReference(Guid.NewGuid(), "Assets/near-v2.glb");
            var farV2 = new GltfAssetReference(Guid.NewGuid(), "Assets/far-v2.glb");
            var localNear = new GltfAssetReference(Guid.NewGuid(), "Assets/local-near.glb");
            var localFar = new GltfAssetReference(Guid.NewGuid(), "Assets/local-far.glb");
            var source = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Room") { GltfAsset = sourceAssetV1 };
            var lodObject = new SceneObject(Guid.NewGuid(), "Room LOD")
            {
                StaticMeshLod = new GltfStaticMeshLod(nearV1, farV1, 100f, 80f)
            };
            var removedLegacyObject = new SceneObject(Guid.NewGuid(), "Untracked removed prop")
            {
                GltfAsset = new GltfAssetReference(Guid.NewGuid(), "Assets/untracked-prop.glb")
            };
            source.Add(root);
            source.Add(lodObject);
            source.Add(removedLegacyObject);
            source.SetParent(lodObject.Id, root.Id);
            source.SetParent(removedLegacyObject.Id, root.Id);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);
            var scene = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision, Vector3.Zero);
            var mappings = wrapper.TemplateInstance!.ObjectMappings.ToDictionary(
                mapping => mapping.SourceObjectId, mapping => mapping.InstanceObjectId);
            var removedLegacyInstanceId = mappings[removedLegacyObject.Id];
            scene.Find(mappings[root.Id])!.GltfAsset = localAsset;
            scene.Find(mappings[lodObject.Id])!.StaticMeshLod =
                new GltfStaticMeshLod(localNear, localFar, 50f, 35f);

            var versionTenJson = JsonNode.Parse(SceneFile.ToJson(scene))!.AsObject();
            versionTenJson["Version"] = 10;
            versionTenJson.Remove("PlaySettings");
            RemoveVersionTwentyBehaviourFields(versionTenJson);
            var wrapperData = versionTenJson["Objects"]!.AsArray().Single(item =>
                Guid.Parse((string)item!["Id"]!) == wrapper.Id)!;
            foreach (var baseline in wrapperData["TemplateInstance"]!["ObjectBaselines"]!.AsArray())
            {
                baseline!.AsObject().Remove("HasGltfAssetBaseline");
                baseline.AsObject().Remove("GltfAssetId");
                baseline.AsObject().Remove("GltfAssetPath");
                baseline.AsObject().Remove("HasStaticMeshLodBaseline");
                baseline.AsObject().Remove("StaticMeshLod");
                baseline.AsObject().Remove("HasCharacterSettingsBaseline");
                baseline.AsObject().Remove("CharacterSettingsBaseline");
                RemoveVersionFourteenWorldBaselines(baseline.AsObject());
                RemoveVersionFifteenBoxColliderBaselines(baseline.AsObject());
            }
            var migrated = SceneFile.FromJson(versionTenJson.ToJsonString());
            var migratedWrapper = migrated.Find(wrapper.Id)!;
            Assert.False(migratedWrapper.TemplateInstance!.ObjectBaselines
                .Single(baseline => baseline.SourceObjectId == root.Id).HasGltfAssetBaseline);
            Assert.False(migratedWrapper.TemplateInstance.ObjectBaselines
                .Single(baseline => baseline.SourceObjectId == lodObject.Id).HasStaticMeshLodBaseline);

            root.GltfAsset = sourceAssetV2;
            lodObject.StaticMeshLod = new GltfStaticMeshLod(nearV2, farV2, 140f, 110f);
            source.Remove(removedLegacyObject.Id);
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Room", path);
            new UpdateSceneTemplateCommand(wrapper.Id, secondRevision).Apply(migrated);

            Assert.Equal(localAsset.AssetId, migrated.Find(mappings[root.Id])!.GltfAsset!.AssetId);
            var retainedLod = migrated.Find(mappings[lodObject.Id])!.StaticMeshLod!;
            Assert.Equal(localNear.AssetId, retainedLod.NearAsset.AssetId);
            Assert.Equal(localFar.AssetId, retainedLod.FarAsset.AssetId);
            Assert.Equal(50f, retainedLod.EnterFarDistance);
            Assert.NotNull(migrated.Find(removedLegacyInstanceId));
            Assert.Contains(removedLegacyInstanceId, migratedWrapper.TemplateInstance!.OrphanedObjectIds);
            Assert.DoesNotContain(removedLegacyObject.Id,
                migratedWrapper.TemplateInstance.ObjectMappings.Select(mapping => mapping.SourceObjectId));
            var upgradedBaselines = migratedWrapper.TemplateInstance!.ObjectBaselines
                .ToDictionary(baseline => baseline.SourceObjectId);
            Assert.True(upgradedBaselines[root.Id].HasGltfAssetBaseline);
            Assert.Equal(sourceAssetV2.AssetId, upgradedBaselines[root.Id].GltfAsset!.AssetId);
            Assert.True(upgradedBaselines[lodObject.Id].HasStaticMeshLodBaseline);
            Assert.Equal(nearV2.AssetId, upgradedBaselines[lodObject.Id].StaticMeshLod!.NearAsset.AssetId);
            Assert.Equal(140f, upgradedBaselines[lodObject.Id].StaticMeshLod!.EnterFarDistance);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void VersionElevenTemplateBaselinesPreserveUnknownCharacterOverridesThenUpgrade()
    {
        var path = TemporaryTemplatePath();
        try
        {
            var source = new SceneGraph();
            var root = new SceneObject(Guid.NewGuid(), "Animated character")
            {
                GltfAsset = new GltfAssetReference(Guid.NewGuid(), "Assets/character.glb"),
                CharacterSettings = new GltfCharacterSettings
                {
                    ClipName = "Idle",
                    Time = 0.2f,
                    Speed = 1f
                }
            };
            source.Add(root);
            var firstRevision = SceneTemplateFile.Save(source, root.Id, "Animated character", path);
            var scene = new SceneGraph();
            var wrapper = SceneTemplateInstanceSystem.Instantiate(scene, firstRevision, Vector3.Zero);
            var instanceObjectId = wrapper.TemplateInstance!.InstanceRootObjectId;
            scene.Find(instanceObjectId)!.CharacterSettings!.ClipName = "PlayerRun";
            scene.Find(instanceObjectId)!.CharacterSettings!.Speed = 1.25f;

            var versionElevenJson = JsonNode.Parse(SceneFile.ToJson(scene))!.AsObject();
            versionElevenJson["Version"] = 11;
            versionElevenJson.Remove("PlaySettings");
            RemoveVersionTwentyBehaviourFields(versionElevenJson);
            var wrapperData = versionElevenJson["Objects"]!.AsArray().Single(item =>
                Guid.Parse((string)item!["Id"]!) == wrapper.Id)!;
            foreach (var baseline in wrapperData["TemplateInstance"]!["ObjectBaselines"]!.AsArray())
            {
                baseline!.AsObject().Remove("HasCharacterSettingsBaseline");
                baseline.AsObject().Remove("CharacterSettingsBaseline");
                RemoveVersionFourteenWorldBaselines(baseline.AsObject());
                RemoveVersionFifteenBoxColliderBaselines(baseline.AsObject());
            }
            var migrated = SceneFile.FromJson(versionElevenJson.ToJsonString());
            var migratedWrapper = migrated.Find(wrapper.Id)!;
            var legacyBaseline = Assert.Single(migratedWrapper.TemplateInstance!.ObjectBaselines);
            Assert.False(legacyBaseline.HasCharacterSettingsBaseline);

            root.CharacterSettings!.ClipName = "Walk";
            root.CharacterSettings.Speed = 2f;
            var secondRevision = SceneTemplateFile.Save(source, root.Id, "Animated character", path);
            new UpdateSceneTemplateCommand(wrapper.Id, secondRevision).Apply(migrated);

            var updatedSettings = migrated.Find(instanceObjectId)!.CharacterSettings!;
            Assert.Equal("PlayerRun", updatedSettings.ClipName);
            Assert.Equal(1.25f, updatedSettings.Speed);
            var upgradedBaseline = migratedWrapper.TemplateInstance!.ObjectBaselines.Single()
                .CharacterSettingsBaseline!;
            Assert.True(migratedWrapper.TemplateInstance.ObjectBaselines.Single().HasCharacterSettingsBaseline);
            Assert.Equal("Walk", upgradedBaseline.ClipName);
            Assert.Equal(2f, upgradedBaseline.Speed);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void RemoveVersionFourteenWorldBaselines(JsonObject baseline)
    {
        baseline.Remove("HasDoorBaseline");
        baseline.Remove("Door");
        baseline.Remove("HasSpawnPointBaseline");
        baseline.Remove("SpawnPoint");
        baseline.Remove("SourceSpawnPointId");
        baseline.Remove("HasWorldEntityBaseline");
        baseline.Remove("WorldEntity");
    }

    private static void RemoveVersionFifteenBoxColliderBaselines(JsonObject baseline)
    {
        baseline.Remove("HasBoxColliderBaseline");
        baseline.Remove("BoxCollider");
        baseline.Remove("HasTriggerActionBaseline");
        baseline.Remove("TriggerAction");
    }

    private static void RemoveVersionTwentyBehaviourFields(JsonObject document)
    {
        foreach (var objectNode in document["Objects"]!.AsArray())
        {
            var sceneObject = objectNode!.AsObject();
            sceneObject.Remove("BehaviourAssignments");
            if (sceneObject["TemplateInstance"]?["ObjectBaselines"] is not JsonArray baselines)
                continue;
            foreach (var baseline in baselines)
            {
                if (baseline is JsonObject baselineObject)
                {
                    baselineObject.Remove("HasBehaviourAssignmentsBaseline");
                    baselineObject.Remove("BehaviourAssignments");
                }
            }
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
