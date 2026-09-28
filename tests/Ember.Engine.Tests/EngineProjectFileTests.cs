using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Ember.Project;
using Ember.Scene;
using SharpGLTF.Schema2;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class EngineProjectFileTests
{
    [Fact]
    public void CreateEmptyProjectPublishesCompleteProjectAndStartupScene()
    {
        var parent = NewDirectory();
        var destination = Path.Combine(parent, "MyGame");
        try
        {
            var project = EngineProjectWorkspace.CreateEmpty(destination);

            Assert.Equal(Path.Combine(destination, EngineProjectFile.DefaultFileName), project.FilePath);
            Assert.Equal("Scenes/Main.json", project.StartupScenePath);
            Assert.Empty(SceneFile.Load(project.ResolveStartupScenePath()).Objects);
            Assert.True(Directory.Exists(Path.Combine(destination, "Assets")));
            Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(parent),
                path => Path.GetFileName(path).Contains(".creating-", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void CreateEmptyProjectDoesNotModifyAnExistingDestination()
    {
        var parent = NewDirectory();
        var destination = Path.Combine(parent, "ExistingGame");
        Directory.CreateDirectory(destination);
        var sentinel = Path.Combine(destination, "keep.txt");
        File.WriteAllText(sentinel, "keep");
        try
        {
            Assert.Throws<IOException>(() => EngineProjectWorkspace.CreateEmpty(destination));
            Assert.Equal("keep", File.ReadAllText(sentinel));
            Assert.Single(Directory.EnumerateFileSystemEntries(parent));
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void RecentProjectsMoveOpenedPathToFrontAndKeepAtMostTwelve()
    {
        var root = NewDirectory();
        var store = Path.Combine(root, "settings", "recent.json");
        try
        {
            for (var index = 0; index < 14; index++)
                EngineProjectWorkspace.RecordRecent(store, Path.Combine(root, $"Game{index}", EngineProjectFile.DefaultFileName));
            var repeated = Path.Combine(root, "Game3", EngineProjectFile.DefaultFileName);
            EngineProjectWorkspace.RecordRecent(store, repeated);

            var recent = EngineProjectWorkspace.LoadRecent(store);

            Assert.Equal(12, recent.Count);
            Assert.Equal(Path.GetFullPath(repeated), recent[0]);
            Assert.Equal(1, recent.Count(path => string.Equals(path, Path.GetFullPath(repeated), StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("TextureCoordinateTest.glb", false)]
    [InlineData("Fox.glb", true)]
    public void ImportGlbCopiesStaticAndAnimatedAssetsIntoProject(string assetFile, bool animated)
    {
        var parent = NewDirectory();
        try
        {
            var project = EngineProjectWorkspace.CreateEmpty(Path.Combine(parent, "Game"));
            var source = Path.Combine(AppContext.BaseDirectory, "Assets", assetFile);
            string importedPath;
            Guid importedId;
            using (var imported = EngineProjectWorkspace.ImportGlb(project, source))
            {
                importedPath = project.ResolveContentPath(imported.Reference.SourcePath);
                importedId = imported.Reference.AssetId;
                var model = ModelRoot.Load(importedPath);
                Assert.Equal(animated, model.LogicalNodes.Any(node => node.Skin is not null));
                Assert.True(animated ? model.LogicalAnimations.Count > 0 : model.LogicalAnimations.Count == 0);
                Assert.StartsWith("Assets/", imported.Reference.SourcePath, StringComparison.Ordinal);
                imported.Commit();
            }

            Assert.True(File.Exists(importedPath));
            var cataloged = Assert.Single(EngineProjectAssetCatalog.ListAssets(project));
            Assert.Equal(importedId, cataloged.AssetId);
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void UncommittedGlbImportIsRemovedOnDispose()
    {
        var parent = NewDirectory();
        try
        {
            var project = EngineProjectWorkspace.CreateEmpty(Path.Combine(parent, "Game"));
            var source = Path.Combine(AppContext.BaseDirectory, "Assets", "TextureCoordinateTest.glb");
            string importedPath;
            Guid importedId;
            using (var imported = EngineProjectWorkspace.ImportGlb(project, source))
            {
                importedPath = project.ResolveContentPath(imported.Reference.SourcePath);
                importedId = imported.Reference.AssetId;
                imported.RegisterForPreview();
                Assert.Equal(importedId, Assert.Single(EngineProjectAssetCatalog.ListAssets(project)).AssetId);
            }

            Assert.False(File.Exists(importedPath));
            Assert.Empty(Directory.EnumerateDirectories(project.ResolveContentPath("Assets")));
            Assert.Empty(EngineProjectAssetCatalog.ListAssets(project));
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

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
