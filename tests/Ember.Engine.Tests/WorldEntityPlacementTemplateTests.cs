using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using System;
using System.IO;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WorldEntityPlacementTemplateTests
{
    [Fact]
    public void TemplateUpdatesPreserveExplicitOverridesAndWorldInstanceIds()
    {
        var template = new WorldEntityPlacementTemplate
        {
            Id = Guid.NewGuid(),
            Name = "Town Guard",
            Kind = WorldEntityKind.Actor,
            DefinitionId = "actors.guard",
            Transform = PlacementTemplateTransform.From(new Transform
            {
                Position = new Vector3(1f, 2f, 3f),
                Scale = new Vector3(1f, 2f, 1f)
            })
        };
        var templates = new WorldEntityPlacementTemplateSet();
        templates.Add(template);

        var scene = new SceneGraph();
        var inherited = WorldEntityPlacementTemplateSystem.CreatePlacement(scene, template);
        scene.Add(inherited);
        var overridden = WorldEntityPlacementTemplateSystem.CreatePlacement(scene, template);
        overridden.WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Item,
            "items.unique-idol", overridden.WorldEntity!.InstanceId, template.Id,
            PlacementTemplateOverrideFlags.Definition | PlacementTemplateOverrideFlags.Position);
        overridden.Transform.Position = new Vector3(20f, 4f, -8f);
        scene.Add(overridden);
        var inheritedInstanceId = inherited.WorldEntity!.InstanceId;
        var overriddenInstanceId = overridden.WorldEntity!.InstanceId;

        var replacement = template with
        {
            Name = "Armored Town Guard",
            DefinitionId = "actors.guard.armored",
            Transform = PlacementTemplateTransform.From(new Transform
            {
                Position = new Vector3(5f, 0f, 7f),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.Up, MathF.PI / 2f),
                Scale = new Vector3(2f, 2f, 2f)
            })
        };
        templates.Replace(replacement);

        var history = new SceneCommandHistory();
        history.Execute(scene, new WorldEntityPlacementTemplateApplyCommand(replacement));
        Assert.Equal(inheritedInstanceId, inherited.WorldEntity!.InstanceId);
        Assert.Equal(overriddenInstanceId, overridden.WorldEntity!.InstanceId);
        Assert.Equal(replacement.Id, inherited.WorldEntity.TemplateId);
        Assert.Equal("actors.guard.armored", inherited.WorldEntity.DefinitionId);
        Assert.Equal(new Vector3(5f, 0f, 7f), inherited.Transform.Position);
        Assert.Equal(new Vector3(2f, 2f, 2f), inherited.Transform.Scale);
        Assert.Equal("items.unique-idol", overridden.WorldEntity.DefinitionId);
        Assert.Equal(new Vector3(20f, 4f, -8f), overridden.Transform.Position);
        Assert.Equal(new Vector3(2f, 2f, 2f), overridden.Transform.Scale);
        Assert.True(history.Undo(scene));
        Assert.Equal("actors.guard", inherited.WorldEntity!.DefinitionId);
        Assert.Equal(new Vector3(1f, 2f, 3f), inherited.Transform.Position);
        Assert.Equal(new Vector3(1f, 2f, 1f), inherited.Transform.Scale);
        Assert.Equal(new Vector3(20f, 4f, -8f), overridden.Transform.Position);
        Assert.True(history.Redo(scene));
        Assert.Equal("actors.guard.armored", inherited.WorldEntity!.DefinitionId);
        Assert.Equal(new Vector3(5f, 0f, 7f), inherited.Transform.Position);
        var duplicate = SceneObjectDuplicator.CreateDuplicate(scene, overridden.Id);
        Assert.NotEqual(overriddenInstanceId, duplicate.WorldEntity!.InstanceId);
        Assert.Equal(replacement.Id, duplicate.WorldEntity.TemplateId);
        Assert.Equal(overridden.WorldEntity.TemplateOverrides, duplicate.WorldEntity.TemplateOverrides);

        var path = Path.Combine(Path.GetTempPath(), $"ember-placement-templates-{Guid.NewGuid():N}.json");
        try
        {
            templates.SaveAtomic(path);
            var reopened = WorldEntityPlacementTemplateSet.Load(path);
            Assert.Single(reopened.All);
            var loadedTemplate = reopened.Get(replacement.Id)!;
            Assert.Equal(replacement.Name, loadedTemplate.Name);
            Assert.Equal(replacement.Kind, loadedTemplate.Kind);
            Assert.Equal(replacement.DefinitionId, loadedTemplate.DefinitionId);
            Assert.Equal(replacement.Transform.Position, loadedTemplate.Transform.Position);
            Assert.Equal(replacement.Transform.Rotation, loadedTemplate.Transform.Rotation);
            Assert.Equal(replacement.Transform.Scale, loadedTemplate.Transform.Scale);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TemplateInstancesRequireValidTemplateAndOverrideMetadata()
    {
        Assert.Throws<ArgumentException>(() => new WorldEntityPlacementComponent(
            WorldEntityKind.Actor, "actors.guard", Guid.NewGuid(), null,
            PlacementTemplateOverrideFlags.Position));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldEntityPlacementComponent(
            WorldEntityKind.Actor, "actors.guard", Guid.NewGuid(), Guid.NewGuid(),
            (PlacementTemplateOverrideFlags)128));
    }
}
