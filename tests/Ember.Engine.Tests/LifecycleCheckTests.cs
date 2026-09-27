using System;
using System.IO;
using Ember.Scene;
using Ember.World;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class LifecycleCheckTests
{
    [Fact]
    public void CompleteFiftyTransitionRouteWithRetryStableResourcesAndRestartPasses()
    {
        var check = CreateCompletedRun();

        Assert.True(check.IsComplete);
        Assert.True(check.Passed);
        Assert.Equal(0, check.ExitCode);
        var report = check.BuildReport("Test adapter", 1280, 720);
        Assert.Contains("Transition route | **PASS**", report, StringComparison.Ordinal);
        Assert.Contains("Delayed load failure and retry | **PASS**", report, StringComparison.Ordinal);
        Assert.Contains("Stable unique identities | **PASS**", report, StringComparison.Ordinal);
        Assert.Contains("Per-cell resource trend | **PASS**", report, StringComparison.Ordinal);
        Assert.Contains("Save/restart | **PASS**", report, StringComparison.Ordinal);
    }

    [Fact]
    public void PostWarmupResourceGrowthFailsEvenWhenRouteAndRestartPass()
    {
        var check = CreateCompletedRun(resourceGrowthAtTransition: 11);

        Assert.False(check.Passed);
        Assert.Equal(1, check.ExitCode);
        Assert.Contains("Per-cell resource trend | **FAIL**", check.BuildReport("Test adapter", 1280, 720), StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedWorldInstanceIdentityAfterReloadFails()
    {
        var check = new RpgSlice.RpgSliceLifecycleCheck();
        var cellId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var firstScene = new SceneGraph();
        firstScene.Add(new SceneObject(objectId, "Fixture"));
        var firstIdentities = new WorldInstanceIdentityMap();
        firstIdentities.GetOrCreate(cellId, firstScene.Find(objectId)!);
        check.RecordCellIdentities(cellId, firstScene, firstIdentities);

        var reloadedScene = new SceneGraph();
        reloadedScene.Add(new SceneObject(objectId, "Fixture"));
        var changedIdentities = new WorldInstanceIdentityMap();
        changedIdentities.GetOrCreate(cellId, reloadedScene.Find(objectId)!);
        check.RecordCellIdentities(cellId, reloadedScene, changedIdentities);

        Assert.False(check.Passed);
        Assert.Contains("changed identity after reload", check.BuildReport("Test adapter", 1280, 720), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSuccessfulRetryDoesNotPass()
    {
        var check = new RpgSlice.RpgSliceLifecycleCheck();
        check.RecordExpectedPreparationFailure(new IOException("injected delayed failure"));

        Assert.False(check.Passed);
        Assert.Contains("successful retries=0", check.BuildReport("Test adapter", 1280, 720), StringComparison.Ordinal);
    }

    [Fact]
    public void FewerThanFiftyTransitionsCannotBeConfiguredAsAcceptanceRun()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RpgSlice.RpgSliceLifecycleCheck(49));
    }

    private static RpgSlice.RpgSliceLifecycleCheck CreateCompletedRun(int? resourceGrowthAtTransition = null)
    {
        var check = new RpgSlice.RpgSliceLifecycleCheck();
        var exterior = Guid.NewGuid();
        var houseA = Guid.NewGuid();
        var houseB = Guid.NewGuid();
        var cellScenes = new[]
        {
            CreateCell("Exterior (0, 0)"),
            CreateCell("House_A"),
            CreateCell("House_B")
        };
        var identities = new WorldInstanceIdentityMap();
        check.RecordExpectedPreparationFailure(new IOException("injected delayed destination failure"));

        for (var i = 1; i <= RpgSlice.RpgSliceLifecycleCheck.MinimumTransitions; i++)
        {
            var destinationIndex = (i % 4) switch
            {
                1 => 1,
                2 => 0,
                3 => 2,
                _ => 0
            };
            var (cellId, scene, label, active, chunks, resourceCount) = destinationIndex switch
            {
                0 => (exterior, cellScenes[0].Scene, cellScenes[0].Label, 9, 12, 22),
                1 => (houseA, cellScenes[1].Scene, cellScenes[1].Label, 1, 0, 10),
                _ => (houseB, cellScenes[2].Scene, cellScenes[2].Label, 1, 0, 10)
            };
            if (resourceGrowthAtTransition == i) resourceCount++;
            identities.CreateCellIdentities(cellId, scene);
            check.RecordCellIdentities(cellId, scene, identities);
            check.RecordTransition("Exterior (0, 0)", label, active, chunks, resourceCount, 100L * 1024 * 1024);
        }

        check.RecordSaveRestart(true, "50 transitions saved and restored with stable unique IDs.");
        return check;
    }

    private static (Guid CellId, SceneGraph Scene, string Label) CreateCell(string label)
    {
        var scene = new SceneGraph();
        scene.Add(new SceneObject(Guid.NewGuid(), label + " object"));
        return (Guid.NewGuid(), scene, label);
    }
}
