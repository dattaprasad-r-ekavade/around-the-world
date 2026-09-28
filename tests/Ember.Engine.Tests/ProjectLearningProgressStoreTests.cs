using System;
using System.IO;
using Ember.Authoring;
using Ember.Project;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class ProjectLearningProgressStoreTests
{
    [Fact]
    public void RecordsLessonCompletionInProjectAndCountsReplay()
    {
        var parent = NewDirectory();
        try
        {
            var project = EngineProjectWorkspace.CreateEmpty(Path.Combine(parent, "Game"));
            var scenePath = project.ResolveStartupScenePath();
            var firstTime = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.FromHours(5.5));
            var first = ProjectLearningProgressStore.RecordCompletion(
                project, "first-creation", scenePath, firstTime);

            Assert.Equal(1, first.CompletionCount);
            Assert.Equal("Scenes/Main.json", first.ScenePath);
            Assert.Equal(firstTime.ToUniversalTime(), first.CompletedAtUtc);
            Assert.Equal(first, Assert.Single(ProjectLearningProgressStore.Load(project)));

            var secondTime = firstTime.AddDays(2);
            var second = ProjectLearningProgressStore.RecordCompletion(
                project, "first-creation", scenePath, secondTime);

            var stored = Assert.Single(ProjectLearningProgressStore.Load(project));
            Assert.Equal(2, second.CompletionCount);
            Assert.Equal(2, stored.CompletionCount);
            Assert.Equal(secondTime.ToUniversalTime(), stored.CompletedAtUtc);
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void RejectsScenePathsOutsideProjectAndPreservesMalformedProgress()
    {
        var parent = NewDirectory();
        try
        {
            var project = EngineProjectWorkspace.CreateEmpty(Path.Combine(parent, "Game"));
            var outsideScene = Path.Combine(parent, "Outside.json");
            File.WriteAllText(outsideScene, "{}");
            Assert.Throws<ArgumentException>(() => ProjectLearningProgressStore.RecordCompletion(
                project, "first-creation", outsideScene));

            var progressPath = project.ResolveContentPath(ProjectLearningProgressStore.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(progressPath)!);
            File.WriteAllText(progressPath, "{ malformed");
            var error = Assert.Throws<InvalidDataException>(() => ProjectLearningProgressStore.RecordCompletion(
                project, "first-creation", project.ResolveStartupScenePath()));

            Assert.Contains("Project learning progress is invalid", error.Message, StringComparison.Ordinal);
            Assert.Equal("{ malformed", File.ReadAllText(progressPath));
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ember-learning-progress-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
