using System;
using System.IO;
using System.Text.Json.Nodes;
using Ember.Project;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class EngineProjectFileTests
{
    [Fact]
    public void ProjectResolvesItsStartupSceneFromItsOwnDirectory()
    {
        var root = NewDirectory();
        try
        {
            var scene = Path.Combine(root, "Content", "Scenes", "Start.json");
            Directory.CreateDirectory(Path.GetDirectoryName(scene)!);
            File.WriteAllText(scene, "{}");
            var projectFile = Path.Combine(root, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectFile, "Content\\Scenes\\Start.json");

            var project = EngineProjectFile.Load(projectFile);

            Assert.Equal(Path.GetFullPath(projectFile), project.FilePath);
            Assert.Equal(root, project.RootDirectory);
            Assert.Equal("Content/Scenes/Start.json", project.StartupScenePath);
            Assert.Equal(scene, project.ResolveStartupScenePath());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("Content/../../outside.json")]
    [InlineData("C:/outside.json")]
    [InlineData("/outside.json")]
    [InlineData("Content/start.scene")]
    public void SaveRejectsEscapingOrNonSceneStartupPaths(string startupScene)
    {
        var root = NewDirectory();
        try
        {
            var projectPath = Path.Combine(root, EngineProjectFile.DefaultFileName);
            var error = Assert.Throws<ArgumentException>(() =>
                EngineProjectFile.SaveAtomic(projectPath, startupScene));
            Assert.Contains("Startup scene path", error.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(projectPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadReportsMissingStartupSceneAndUnsupportedProjectVersion()
    {
        var root = NewDirectory();
        try
        {
            var projectPath = Path.Combine(root, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectPath, "Content/Start.json");

            var missing = Assert.Throws<FileNotFoundException>(() => EngineProjectFile.Load(projectPath));
            Assert.Contains("Content/Start.json", missing.Message, StringComparison.Ordinal);
            Assert.Contains(Path.Combine(root, "Content", "Start.json"), missing.Message, StringComparison.Ordinal);

            var json = JsonNode.Parse(File.ReadAllText(projectPath))!;
            json["version"] = EngineProjectFile.CurrentVersion + 1;
            File.WriteAllText(projectPath, json.ToJsonString());
            var versionError = Assert.Throws<InvalidDataException>(() => EngineProjectFile.Load(projectPath));
            Assert.Contains("Unsupported project version", versionError.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadRejectsUnknownProjectMemberAsSchemaError()
    {
        var root = NewDirectory();
        try
        {
            var projectPath = Path.Combine(root, EngineProjectFile.DefaultFileName);
            File.WriteAllText(projectPath,
                "{\"version\":1,\"startupScene\":\"Start.json\",\"unexpected\":true}");

            var exception = Assert.Throws<InvalidDataException>(() => EngineProjectFile.Load(projectPath));
            Assert.Contains("Project JSON is invalid", exception.Message, StringComparison.Ordinal);
            Assert.IsType<System.Text.Json.JsonException>(exception.InnerException);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ContentPathsResolveFromTheProjectDirectoryAndRejectEscapes()
    {
        var root = NewDirectory();
        try
        {
            var projectPath = Path.Combine(root, EngineProjectFile.DefaultFileName);
            var scenePath = Path.Combine(root, "Content", "Start.json");
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
            File.WriteAllText(scenePath, "{}");
            EngineProjectFile.SaveAtomic(projectPath, "Content/Start.json");

            var project = EngineProjectFile.Load(projectPath);

            Assert.Equal(Path.Combine(root, "Content", "Models", "hero.glb"),
                project.ResolveContentPath("Content\\Models\\hero.glb"));
            Assert.Throws<ArgumentException>(() => project.ResolveContentPath("Content/../../outside.glb"));
            Assert.Throws<ArgumentException>(() => project.ResolveContentPath("D:/outside.glb"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ProjectWithWorldManifestAndExtraContent_SavesAndLoadsSuccessfully()
    {
        var root = NewDirectory();
        try
        {
            var projectPath = Path.Combine(root, EngineProjectFile.DefaultFileName);
            var manifestPath = Path.Combine(root, "Content", "World", "settlement.json");
            var extraPath = Path.Combine(root, "Content", "RpgContent.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, "{}");
            File.WriteAllText(extraPath, "{}");

            EngineProjectFile.SaveAtomic(projectPath, startupScenePath: null,
                worldManifestPath: "Content/World/settlement.json",
                extraContentPaths: ["Content/RpgContent.json"]);

            var project = EngineProjectFile.Load(projectPath);

            Assert.Null(project.StartupScenePath);
            Assert.Equal("Content/World/settlement.json", project.WorldManifestPath);
            Assert.Single(project.ExtraContentPaths, "Content/RpgContent.json");
            Assert.Equal(manifestPath, project.ResolveWorldManifestPath());
            Assert.Equal(extraPath, project.ResolveContentPath("Content/RpgContent.json"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Project_RequiresAtLeastStartupSceneOrWorldManifest()
    {
        var root = NewDirectory();
        try
        {
            var projectPath = Path.Combine(root, EngineProjectFile.DefaultFileName);
            Assert.Throws<ArgumentException>(() =>
                EngineProjectFile.SaveAtomic(projectPath, startupScenePath: null, worldManifestPath: null));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-project-file-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
