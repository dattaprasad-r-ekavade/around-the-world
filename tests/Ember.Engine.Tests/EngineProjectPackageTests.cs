using System;
using System.IO;
using System.Text;
using Ember.Project;
using Ember.Scene;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class EngineProjectPackageTests
{
    [Fact]
    public void PackageContainsStartupSceneAndOnlyItsReferencedGlbsAndCanMove()
    {
        var root = NewDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "source");
            var packagePath = Path.Combine(root, "output", "StarterPackage");
            var movedPath = Path.Combine(root, "moved", "StarterPackage");
            var referencedPath = Path.Combine(sourceRoot, "Content", "Models", "hero.glb");
            var unusedPath = Path.Combine(sourceRoot, "Content", "Models", "unused.glb");
            var noticesPath = Path.Combine(sourceRoot, "ThirdPartyNotices.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(referencedPath)!);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Assets", "TextureCoordinateTest.glb"), referencedPath);
            File.WriteAllBytes(unusedPath, [5, 6, 7, 8]);
            File.WriteAllText(noticesPath, "Fox attribution and license notice.");

            var objectId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var startupScenePath = Path.Combine(sourceRoot, "Content", "Scenes", "Start.json");
            Directory.CreateDirectory(Path.GetDirectoryName(startupScenePath)!);
            var scene = new SceneGraph();
            scene.Add(new SceneObject(objectId, "Hero")
            {
                GltfAsset = new GltfAssetReference(assetId, "Content/Models/hero.glb")
            });
            SceneFile.SaveAtomic(scene, startupScenePath);
            var projectFilePath = Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectFilePath, "Content/Scenes/Start.json");

            var result = EngineProjectPackage.Create(projectFilePath, packagePath);

            Assert.Equal(packagePath, result.DirectoryPath);
            Assert.Equal(1, result.GlbAssetCount);
            Assert.True(File.Exists(Path.Combine(packagePath, EngineProjectFile.DefaultFileName)));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "Scenes", "Start.json")));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "Models", "hero.glb")));
            Assert.False(File.Exists(Path.Combine(packagePath, "Content", "Models", "unused.glb")));
            Assert.Equal(File.ReadAllText(noticesPath), File.ReadAllText(Path.Combine(packagePath, "ThirdPartyNotices.txt")));
            Assert.Equal(File.ReadAllBytes(referencedPath),
                File.ReadAllBytes(Path.Combine(packagePath, "Content", "Models", "hero.glb")));

            Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
            Directory.Move(packagePath, movedPath);
            var movedProject = EngineProjectFile.Load(Path.Combine(movedPath, EngineProjectFile.DefaultFileName));
            var movedScene = SceneFile.Load(movedProject.ResolveStartupScenePath());
            Assert.Equal(objectId, Assert.Single(movedScene.Objects).Id);
            Assert.True(File.Exists(movedProject.ResolveContentPath("Content/Models/hero.glb")));
            Assert.Equal("Fox attribution and license notice.", File.ReadAllText(Path.Combine(movedPath, "ThirdPartyNotices.txt")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageIncludesBothStaticMeshLodRepresentations()
    {
        var root = NewDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "source");
            var models = Path.Combine(sourceRoot, "Content", "Models");
            Directory.CreateDirectory(models);
            var fixture = Path.Combine(AppContext.BaseDirectory, "Assets", "TextureCoordinateTest.glb");
            File.Copy(fixture, Path.Combine(models, "near.glb"));
            File.Copy(fixture, Path.Combine(models, "far.glb"));

            var scene = new SceneGraph();
            scene.Add(new SceneObject(Guid.NewGuid(), "House")
            {
                StaticMeshLod = new GltfStaticMeshLod(
                    new GltfAssetReference(Guid.NewGuid(), "Content/Models/near.glb"),
                    new GltfAssetReference(Guid.NewGuid(), "Content/Models/far.glb"),
                    enterFarDistance: 80f, exitFarDistance: 64f)
            });
            var scenePath = Path.Combine(sourceRoot, "Content", "Start.json");
            SceneFile.SaveAtomic(scene, scenePath);
            var projectPath = Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectPath, "Content/Start.json");
            var packagePath = Path.Combine(root, "package");

            var result = EngineProjectPackage.Create(projectPath, packagePath);

            Assert.Equal(2, result.GlbAssetCount);
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "Models", "near.glb")));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "Models", "far.glb")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageCopiesLocalBufferAndImageDependenciesReferencedInsideGlb()
    {
        var root = NewDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "source");
            var glbRelativePath = "Content/Models/Hero.glb";
            var glbPath = Path.Combine(sourceRoot, "Content", "Models", "Hero.glb");
            var bufferPath = Path.Combine(sourceRoot, "Content", "Models", "mesh.bin");
            var imagePath = Path.Combine(sourceRoot, "Content", "Textures", "hero color.png");
            Directory.CreateDirectory(Path.GetDirectoryName(glbPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
            WriteGlbWithExternalUris(glbPath,
                "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"mesh.bin\",\"byteLength\":4}],\"images\":[{\"uri\":\"../Textures/hero%20color.png\"}]}");
            File.WriteAllBytes(bufferPath, [11, 12, 13, 14]);
            File.WriteAllBytes(imagePath, [21, 22, 23, 24]);

            var scene = new SceneGraph();
            scene.Add(new SceneObject(Guid.NewGuid(), "Hero")
            {
                GltfAsset = new GltfAssetReference(Guid.NewGuid(), glbRelativePath)
            });
            var startupScenePath = Path.Combine(sourceRoot, "Content", "Scenes", "Start.json");
            Directory.CreateDirectory(Path.GetDirectoryName(startupScenePath)!);
            SceneFile.SaveAtomic(scene, startupScenePath);
            var projectFilePath = Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectFilePath, "Content/Scenes/Start.json");
            var packagePath = Path.Combine(root, "package");

            var result = EngineProjectPackage.Create(projectFilePath, packagePath);

            Assert.Equal(1, result.GlbAssetCount);
            Assert.Equal(File.ReadAllBytes(glbPath), File.ReadAllBytes(Path.Combine(packagePath, glbRelativePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal(File.ReadAllBytes(bufferPath), File.ReadAllBytes(Path.Combine(packagePath, "Content", "Models", "mesh.bin")));
            Assert.Equal(File.ReadAllBytes(imagePath), File.ReadAllBytes(Path.Combine(packagePath, "Content", "Textures", "hero color.png")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageReportsMissingLocalGlbDependencyWithObjectIdAssetIdAndPath()
    {
        var root = NewDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "source");
            var glbPath = Path.Combine(sourceRoot, "Content", "Models", "Hero.glb");
            Directory.CreateDirectory(Path.GetDirectoryName(glbPath)!);
            WriteGlbWithExternalUris(glbPath,
                "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"missing.bin\",\"byteLength\":4}]}");
            var objectId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var scene = new SceneGraph();
            scene.Add(new SceneObject(objectId, "Hero")
            {
                GltfAsset = new GltfAssetReference(assetId, "Content/Models/Hero.glb")
            });
            var startupScenePath = Path.Combine(sourceRoot, "Content", "Start.json");
            Directory.CreateDirectory(Path.GetDirectoryName(startupScenePath)!);
            SceneFile.SaveAtomic(scene, startupScenePath);
            var projectFilePath = Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectFilePath, "Content/Start.json");
            var packagePath = Path.Combine(root, "package");

            var error = Assert.Throws<FileNotFoundException>(() =>
                EngineProjectPackage.Create(projectFilePath, packagePath));

            Assert.Contains(objectId.ToString(), error.Message, StringComparison.Ordinal);
            Assert.Contains(assetId.ToString(), error.Message, StringComparison.Ordinal);
            Assert.Contains("missing.bin", error.Message, StringComparison.Ordinal);
            Assert.Contains("Content/Models/missing.bin", error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(packagePath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageReportsMissingAssetObjectIdAssetIdAndRelativePathWithoutCreatingOutput()
    {
        var root = NewDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "source");
            var startupScenePath = Path.Combine(sourceRoot, "Content", "Start.json");
            Directory.CreateDirectory(Path.GetDirectoryName(startupScenePath)!);
            var objectId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var scene = new SceneGraph();
            scene.Add(new SceneObject(objectId, "Missing asset")
            {
                GltfAsset = new GltfAssetReference(assetId, "Content/Missing/hero.glb")
            });
            SceneFile.SaveAtomic(scene, startupScenePath);
            var projectFilePath = Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectFilePath, "Content/Start.json");
            var packagePath = Path.Combine(root, "output", "BrokenPackage");

            var error = Assert.Throws<FileNotFoundException>(() =>
                EngineProjectPackage.Create(projectFilePath, packagePath));

            Assert.Contains(objectId.ToString(), error.Message, StringComparison.Ordinal);
            Assert.Contains(assetId.ToString(), error.Message, StringComparison.Ordinal);
            Assert.Contains("Content/Missing/hero.glb", error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(packagePath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageRejectsGlbDependencyThatEscapesProjectRoot()
    {
        var root = NewDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "source");
            var glbPath = Path.Combine(sourceRoot, "Content", "Models", "Hero.glb");
            Directory.CreateDirectory(Path.GetDirectoryName(glbPath)!);
            WriteGlbWithExternalUris(glbPath,
                "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"../../../outside.bin\",\"byteLength\":4}]}");
            var objectId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var scene = new SceneGraph();
            scene.Add(new SceneObject(objectId, "Hero")
            {
                GltfAsset = new GltfAssetReference(assetId, "Content/Models/Hero.glb")
            });
            var startupScenePath = Path.Combine(sourceRoot, "Content", "Start.json");
            Directory.CreateDirectory(Path.GetDirectoryName(startupScenePath)!);
            SceneFile.SaveAtomic(scene, startupScenePath);
            var projectFilePath = Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectFilePath, "Content/Start.json");
            var packagePath = Path.Combine(root, "package");

            var error = Assert.Throws<InvalidDataException>(() =>
                EngineProjectPackage.Create(projectFilePath, packagePath));

            Assert.Contains(objectId.ToString(), error.Message, StringComparison.Ordinal);
            Assert.Contains(assetId.ToString(), error.Message, StringComparison.Ordinal);
            Assert.Contains("../../../outside.bin", error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(packagePath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ember-project-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteGlbWithExternalUris(string path, string json)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var paddedLength = (jsonBytes.Length + 3) & ~3;
        var paddedJson = new byte[paddedLength];
        Array.Fill(paddedJson, (byte)' ');
        Array.Copy(jsonBytes, paddedJson, jsonBytes.Length);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(0x46546C67u);
        writer.Write(2u);
        writer.Write((uint)(12 + 8 + paddedJson.Length));
        writer.Write((uint)paddedJson.Length);
        writer.Write(0x4E4F534Au);
        writer.Write(paddedJson);
    }
}
