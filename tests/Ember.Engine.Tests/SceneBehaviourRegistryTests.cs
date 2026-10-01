using System;
using Ember.Scene;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneBehaviourRegistryTests
{
    [Fact]
    public void RegisteredBehaviourIsCreatedAndStartedForItsSceneObject()
    {
        var ownerId = Guid.NewGuid();
        var scene = new SceneGraph();
        scene.Add(new SceneObject(ownerId, "Door"));
        var registry = new SceneBehaviourRegistry();
        ProbeBehaviour? created = null;
        registry.Register("sample.door-chime", owner => created = new ProbeBehaviour(owner.Id));

        using var runtime = new SceneBehaviourRuntime(scene);
        runtime.AddRegistered(ownerId, "sample.door-chime", registry);
        runtime.Start();

        Assert.NotNull(created);
        Assert.Equal(ownerId, created!.OwnerId);
        Assert.Equal(1, created.StartCount);
        Assert.Equal(1, runtime.Interact(ownerId, "Interact"));
        Assert.Equal("Interact", created.LastAction);
    }

    [Fact]
    public void MissingRegistrationNamesTheSceneObjectAndStableBehaviourId()
    {
        var ownerId = Guid.NewGuid();
        var owner = new SceneObject(ownerId, "Locked door");
        var registry = new SceneBehaviourRegistry();

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.Create("sample.missing", owner));

        Assert.Contains("Locked door", exception.Message, StringComparison.Ordinal);
        Assert.Contains(ownerId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("sample.missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateBehaviourIdsAreRejected()
    {
        var registry = new SceneBehaviourRegistry();
        registry.Register("sample.behaviour", _ => new ProbeBehaviour(Guid.NewGuid()));

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.Register("sample.behaviour", _ => new ProbeBehaviour(Guid.NewGuid())));

        Assert.Contains("sample.behaviour", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisteredIdsAreSortedAndFactoriesCreateFreshInstances()
    {
        var registry = new SceneBehaviourRegistry();
        registry.Register("sample.zeta", _ => new ProbeBehaviour(Guid.NewGuid()));
        registry.Register("sample.alpha", _ => new ProbeBehaviour(Guid.NewGuid()));
        var owner = new SceneObject(Guid.NewGuid(), "Actor");

        Assert.Equal(new[] { "sample.alpha", "sample.zeta" }, registry.RegisteredIds);
        Assert.NotSame(registry.Create("sample.alpha", owner), registry.Create("sample.alpha", owner));
    }

    [Fact]
    public void FactoryFailureIncludesOwningSceneObject()
    {
        var ownerId = Guid.NewGuid();
        var registry = new SceneBehaviourRegistry();
        registry.Register("sample.broken", _ => throw new InvalidOperationException("configuration failed"));

        var exception = Assert.Throws<InvalidOperationException>(
            () => registry.Create("sample.broken", new SceneObject(ownerId, "Broken door")));

        Assert.Contains("Broken door", exception.Message, StringComparison.Ordinal);
        Assert.Contains(ownerId.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("configuration failed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegisterFromMergesDisplayNamesAndFactories()
    {
        var registry = new SceneBehaviourRegistry();
        var module = new SceneBehaviourRegistry();
        module.Register("sample.flash", "Flash light", _ => new ProbeBehaviour(Guid.NewGuid()));

        registry.RegisterFrom(module);

        var registration = Assert.Single(registry.RegisteredBehaviours);
        Assert.Equal("sample.flash", registration.Id);
        Assert.Equal("Flash light", registration.DisplayName);
        Assert.NotNull(registry.Create(registration.Id, new SceneObject(Guid.NewGuid(), "Torch")));
    }

    [Fact]
    public void RegisterFromRejectsDuplicateIdsWithoutPartialChanges()
    {
        var registry = new SceneBehaviourRegistry();
        registry.Register("sample.existing", _ => new ProbeBehaviour(Guid.NewGuid()));
        var module = new SceneBehaviourRegistry();
        module.Register("sample.new", _ => new ProbeBehaviour(Guid.NewGuid()));
        module.Register("sample.existing", _ => new ProbeBehaviour(Guid.NewGuid()));

        Assert.Throws<InvalidOperationException>(() => registry.RegisterFrom(module));

        Assert.Equal(new[] { "sample.existing" }, registry.RegisteredIds);
        Assert.Throws<InvalidOperationException>(() => registry.Create(
            "sample.new", new SceneObject(Guid.NewGuid(), "Unknown")));
    }

    private sealed class ProbeBehaviour(Guid ownerId) : SceneBehaviour
    {
        public Guid OwnerId { get; } = ownerId;
        public int StartCount { get; private set; }
        public string? LastAction { get; private set; }

        protected override void OnStart(SceneBehaviourContext context) => StartCount++;
        protected override void OnInteract(SceneInteraction interaction) => LastAction = interaction.Action;
    }
}
