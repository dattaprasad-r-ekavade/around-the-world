using System;
using System.IO;
using Ember.Project;
using Ember.Scene;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class EngineProjectAssetCatalogTests
{
    [Fact]
    public void ListAssetsPersistsIdentityAcrossRefreshAndProjectRelocation()
    {
        var parent = NewDirectory();
        try
        {
            var project = EngineProjectWorkspace.CreateEmpty(Path.Combine(parent, "Game"));
            var assetPath = Path.Combine(project.ResolveContentPath("Assets"), "Props", "crate.glb");
            Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
            File.Copy(FixturePath(), assetPath);

            var first = Assert.Single(EngineProjectAssetCatalog.ListAssets(project));
            var refreshed = Assert.Single(EngineProjectAssetCatalog.ListAssets(project));
            Assert.Equal("Assets/Props/crate.glb", first.SourcePath);
            Assert.Equal(first.AssetId, refreshed.AssetId);

            var relocatedRoot = Path.Combine(parent, "RelocatedGame");
            Directory.Move(project.RootDirectory, relocatedRoot);
            var relocatedProject = EngineProjectFile.Load(
                Path.Combine(relocatedRoot, EngineProjectFile.DefaultFileName));
            var relocated = Assert.Single(EngineProjectAssetCatalog.ListAssets(relocatedProject));
            Assert.Equal(first.AssetId, relocated.AssetId);
            Assert.Equal(first.SourcePath, relocated.SourcePath);
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void SceneReferenceSeedsCatalogSoAssetIdentitySurvivesRemovingItsLastInstance()
    {
        var parent = NewDirectory();
        try
        {
            var project = EngineProjectWorkspace.CreateEmpty(Path.Combine(parent, "Game"));
            var assetPath = Path.Combine(project.ResolveContentPath("Assets"), "legacy.glb");
            File.Copy(FixturePath(), assetPath);
            var sceneReference = new GltfAssetReference(Guid.NewGuid(), "Assets/legacy.glb");

            var seeded = Assert.Single(EngineProjectAssetCatalog.ListAssets(project, [sceneReference]));
            var afterSceneRemoval = Assert.Single(EngineProjectAssetCatalog.ListAssets(project));

            Assert.Equal(sceneReference.AssetId, seeded.AssetId);
            Assert.Equal(sceneReference.AssetId, afterSceneRemoval.AssetId);
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void RegisterRejectsConflictingAssetIdsOrPaths()
    {
        var parent = NewDirectory();
        try
        {
            var project = EngineProjectWorkspace.CreateEmpty(Path.Combine(parent, "Game"));
            File.Copy(FixturePath(), Path.Combine(project.ResolveContentPath("Assets"), "first.glb"));
            File.Copy(FixturePath(), Path.Combine(project.ResolveContentPath("Assets"), "second.glb"));
            var first = new GltfAssetReference(Guid.NewGuid(), "Assets/first.glb");
            EngineProjectAssetCatalog.Register(project, first);

            Assert.Throws<InvalidDataException>(() => EngineProjectAssetCatalog.Register(project,
                new GltfAssetReference(Guid.NewGuid(), first.SourcePath)));
            Assert.Throws<InvalidDataException>(() => EngineProjectAssetCatalog.Register(project,
                new GltfAssetReference(first.AssetId, "Assets/second.glb")));
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void ListAssetsReportsMalformedCatalogInsteadOfReplacingIt()
    {
        var parent = NewDirectory();
        try
        {
            var project = EngineProjectWorkspace.CreateEmpty(Path.Combine(parent, "Game"));
            var catalogPath = project.ResolveContentPath("Assets/.ember-assets.json");
            File.WriteAllText(catalogPath, "{ malformed");

            var error = Assert.Throws<InvalidDataException>(() => EngineProjectAssetCatalog.ListAssets(project));

            Assert.Contains("Project asset catalog is invalid", error.Message, StringComparison.Ordinal);
            Assert.Equal("{ malformed", File.ReadAllText(catalogPath));
        }
        finally
        {
            if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    private static string FixturePath() => Path.Combine(AppContext.BaseDirectory, "Assets", "TextureCoordinateTest.glb");

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ember-project-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
