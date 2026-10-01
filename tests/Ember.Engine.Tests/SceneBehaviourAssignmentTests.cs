using System;
using System.IO;
using Ember.Scene;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneBehaviourAssignmentTests
{
    [Fact]
    public void AssignmentsRoundTripAndSupportUndoRedo()
    {
        var ownerId = Guid.NewGuid();
        var scene = new SceneGraph();
        scene.Add(new SceneObject(ownerId, "Door"));
        var history = new SceneCommandHistory();

        history.Execute(scene, new EditSceneBehaviourAssignmentsCommand(ownerId,
            new[] { "sample.open-door", "sample.play-chime" }));

        Assert.Equal(new[] { "sample.open-door", "sample.play-chime" },
            scene.Find(ownerId)!.BehaviourAssignments);
        Assert.True(history.Undo(scene));
        Assert.Empty(scene.Find(ownerId)!.BehaviourAssignments);
        Assert.True(history.Redo(scene));

        var loaded = SceneFile.FromJson(SceneFile.ToJson(scene));
        Assert.Equal(new[] { "sample.open-door", "sample.play-chime" },
            loaded.Find(ownerId)!.BehaviourAssignments);
    }

    [Fact]
    public void PreviousSceneVersionCannotContainBehaviourAssignments()
    {
        var ownerId = Guid.NewGuid();
        var scene = new SceneGraph();
        var owner = new SceneObject(ownerId, "Door");
        owner.SetBehaviourAssignments(new[] { "sample.open-door" });
        scene.Add(owner);
        var json = SceneFile.ToJson(scene).Replace(
            $"\"Version\": {SceneFile.CurrentVersion}", "\"Version\": 19", StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidDataException>(() => SceneFile.FromJson(json));

        Assert.Contains(ownerId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("scene version 20", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateOrUnstableAssignmentIdsAreRejectedBeforeEditing()
    {
        var ownerId = Guid.NewGuid();
        var owner = new SceneObject(ownerId, "Door");
        Assert.Throws<ArgumentException>(() => owner.SetBehaviourAssignments(
            new[] { "sample.open-door", "sample.open-door" }));
        Assert.Throws<ArgumentException>(() => new EditSceneBehaviourAssignmentsCommand(
            ownerId, new[] { " sample.open-door" }));
        Assert.Empty(owner.BehaviourAssignments);
    }

    [Fact]
    public void PlayResolvesSavedAssignmentsFromTheRegistryOnItsRuntimeCopy()
    {
        var ownerId = Guid.NewGuid();
        var scene = new SceneGraph();
        var owner = new SceneObject(ownerId, "Door");
        owner.SetBehaviourAssignments(new[] { "sample.open-door" });
        scene.Add(owner);
        var registry = new SceneBehaviourRegistry();
        ProbeBehaviour? created = null;
        registry.Register("sample.open-door", _ => created = new ProbeBehaviour());

        using var session = new ScenePlaySession(scene, registry: registry);

        Assert.NotNull(created);
        Assert.Equal(1, created!.StartCount);
        Assert.Equal(new[] { "sample.open-door" },
            session.RuntimeScene.Find(ownerId)!.BehaviourAssignments);
        Assert.Equal(new[] { "sample.open-door" }, scene.Find(ownerId)!.BehaviourAssignments);
        Assert.Equal(1, session.Behaviours.Interact(ownerId, "Interact"));
        Assert.Equal("Interact", created.LastAction);
    }

    [Fact]
    public void MissingPlayRegistryNamesTheAssignedSceneObject()
    {
        var ownerId = Guid.NewGuid();
        var scene = new SceneGraph();
        var owner = new SceneObject(ownerId, "Door");
        owner.SetBehaviourAssignments(new[] { "sample.open-door" });
        scene.Add(owner);

        var exception = Assert.Throws<InvalidOperationException>(() => new ScenePlaySession(scene));

        Assert.Contains("Door", exception.Message, StringComparison.Ordinal);
        Assert.Contains(ownerId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("sample.open-door", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SceneClonePreservesSavedBehaviourAssignments()
    {
        var ownerId = Guid.NewGuid();
        var scene = new SceneGraph();
        var owner = new SceneObject(ownerId, "Door");
        owner.SetBehaviourAssignments(new[] { "sample.open-door" });
        scene.Add(owner);

        var clone = SceneGraphCloner.Clone(scene);

        Assert.Equal(new[] { "sample.open-door" }, clone.Find(ownerId)!.BehaviourAssignments);
    }

    [Fact]
    public void DuplicatedSceneObjectPreservesSavedBehaviourAssignments()
    {
        var ownerId = Guid.NewGuid();
        var scene = new SceneGraph();
        var owner = new SceneObject(ownerId, "Door");
        owner.SetBehaviourAssignments(new[] { "sample.open-door" });
        scene.Add(owner);

        var duplicate = SceneObjectDuplicator.CreateDuplicate(scene, ownerId);

        Assert.NotEqual(ownerId, duplicate.Id);
        Assert.Equal(new[] { "sample.open-door" }, duplicate.BehaviourAssignments);
    }

    private sealed class ProbeBehaviour : SceneBehaviour
    {
        public int StartCount { get; private set; }
        public string? LastAction { get; private set; }

        protected override void OnStart(SceneBehaviourContext context) => StartCount++;
        protected override void OnInteract(SceneInteraction interaction) => LastAction = interaction.Action;
    }
}
