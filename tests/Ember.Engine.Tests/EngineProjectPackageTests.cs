using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ember.Assets;
using Ember.Project;
using Ember.Sequence;
using Ember.Scene;
using Ember.World;
using SharpGLTF.Schema2;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class EngineProjectPackageTests
{
    [Fact]
    public void PackageContainsReferencedGlbsProjectAudioAndRegisteredSequenceAndCanMove()
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
            var audioRelativePath = "Assets/Audio/Intro.wav";
            var audioPath = Path.Combine(sourceRoot, audioRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var referencedAudioRelativePath = "Content/Audio/Theme.wav";
            var referencedAudioPath = Path.Combine(sourceRoot,
                referencedAudioRelativePath.Replace('/', Path.DirectorySeparatorChar));
            var sequenceRelativePath = "Content/Sequences/Intro.sequence.json";
            var sequencePath = Path.Combine(sourceRoot, sequenceRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(referencedPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(audioPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(referencedAudioPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(sequencePath)!);
            var foxPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fox.glb");
            File.Copy(foxPath, referencedPath);
            File.WriteAllBytes(unusedPath, [5, 6, 7, 8]);
            File.WriteAllBytes(audioPath, [31, 32, 33, 34]);
            File.WriteAllBytes(referencedAudioPath, [41, 42, 43, 44]);
            File.WriteAllText(noticesPath, "Fox attribution and license notice.");

            var objectId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            var startupScenePath = Path.Combine(sourceRoot, "Content", "Scenes", "Start.json");
            Directory.CreateDirectory(Path.GetDirectoryName(startupScenePath)!);
            var scene = new SceneGraph();
            var audioAssetId = Guid.NewGuid();
            scene.SetAudioAssets([new SceneAudioAssetReference(audioAssetId, referencedAudioRelativePath)]);
            scene.Add(new SceneObject(objectId, "Hero")
            {
                GltfAsset = new GltfAssetReference(assetId, "Content/Models/hero.glb")
            });
            SceneFile.SaveAtomic(scene, startupScenePath);
            var character = GltfSkinnedCharacterData.Import(ModelRoot.Load(referencedPath));
            var sequence = new SceneSequence("Intro", 2f,
                [new CharacterClipTrack(objectId, character.Animations.Single(clip => clip.Name == "Walk"), loop: true)]);
            SequenceFile.SaveAtomic(sequence, scene, sequencePath);
            var projectFilePath = Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectFilePath, "Content/Scenes/Start.json");
            var project = EngineProjectFile.Load(projectFilePath)
                .RegisterExtraContentPath(sequenceRelativePath);

            var validation = EngineProjectPackage.Validate(projectFilePath);
            Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Diagnostics));
            Assert.Equal(1, validation.SceneCount);
            Assert.Equal(1, validation.GlbAssetCount);
            Assert.Equal(1, validation.AudioAssetCount);
            Assert.Equal(1, validation.SequenceCount);
            Assert.True(validation.PackagedFileCount > 0);

            var result = EngineProjectPackage.Create(projectFilePath, packagePath);

            Assert.Equal(packagePath, result.DirectoryPath);
            Assert.Equal(1, result.SceneCount);
            Assert.Equal(1, result.GlbAssetCount);
            Assert.Equal(1, result.AudioAssetCount);
            Assert.Equal(1, result.SequenceCount);
            Assert.True(File.Exists(Path.Combine(packagePath, EngineProjectFile.DefaultFileName)));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "Scenes", "Start.json")));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "Models", "hero.glb")));
            Assert.True(File.Exists(Path.Combine(packagePath, audioRelativePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.True(File.Exists(Path.Combine(packagePath, referencedAudioRelativePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.True(File.Exists(Path.Combine(packagePath, sequenceRelativePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal(sequenceRelativePath, Assert.Single(project.ExtraContentPaths));
            Assert.Equal(File.ReadAllBytes(audioPath),
                File.ReadAllBytes(Path.Combine(packagePath, audioRelativePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal(File.ReadAllBytes(referencedAudioPath),
                File.ReadAllBytes(Path.Combine(packagePath, referencedAudioRelativePath.Replace('/', Path.DirectorySeparatorChar))));
            Assert.False(File.Exists(Path.Combine(packagePath, "Content", "Models", "unused.glb")));
            Assert.Equal(File.ReadAllText(noticesPath), File.ReadAllText(Path.Combine(packagePath, "ThirdPartyNotices.txt")));
            Assert.Equal(File.ReadAllBytes(referencedPath),
                File.ReadAllBytes(Path.Combine(packagePath, "Content", "Models", "hero.glb")));

            Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
            Directory.Move(packagePath, movedPath);
            var movedProject = EngineProjectFile.Load(Path.Combine(movedPath, EngineProjectFile.DefaultFileName));
            Assert.True(File.Exists(movedProject.ResolveContentPath(sequenceRelativePath)));
            var movedScene = SceneFile.Load(movedProject.ResolveStartupScenePath());
            Assert.Equal(objectId, Assert.Single(movedScene.Objects).Id);
            var movedAudio = Assert.Single(movedScene.AudioAssets);
            Assert.Equal(audioAssetId, movedAudio.AssetId);
            Assert.True(File.Exists(movedProject.ResolveContentPath(movedAudio.SourcePath)));
            Assert.True(File.Exists(movedProject.ResolveContentPath("Content/Models/hero.glb")));
            Assert.Equal(File.ReadAllBytes(audioPath),
                File.ReadAllBytes(movedProject.ResolveContentPath(audioRelativePath)));
            var movedCharacter = GltfSkinnedCharacterData.Import(
                ModelRoot.Load(movedProject.ResolveContentPath("Content/Models/hero.glb")));
            var reopenedSequence = SequenceFile.Load(movedProject.ResolveContentPath(sequenceRelativePath), movedScene,
                new Dictionary<Guid, IReadOnlyList<GltfAnimationClipData>>
                {
                    [assetId] = movedCharacter.Animations
                });
            Assert.Equal("Intro", reopenedSequence.Name);
            Assert.Equal(objectId, Assert.Single(reopenedSequence.CharacterTracks).TargetObjectId);
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
    public void PackageRejectsMissingReferencedAudioBeforeCreatingOutput()
    {
        var root = NewDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "source");
            var packagePath = Path.Combine(root, "output", "StarterPackage");
            var scenePath = Path.Combine(sourceRoot, "Content", "Scenes", "Start.json");
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
            var assetId = Guid.NewGuid();
            var scene = new SceneGraph();
            scene.SetAudioAssets([new SceneAudioAssetReference(assetId, "Assets/Audio/Missing.wav")]);
            SceneFile.SaveAtomic(scene, scenePath);
            EngineProjectFile.SaveAtomic(Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName),
                "Content/Scenes/Start.json");

            var validation = EngineProjectPackage.Validate(
                Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName));
            Assert.False(validation.IsValid);
            var diagnostic = Assert.Single(validation.Diagnostics);
            Assert.Contains(assetId.ToString(), diagnostic, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Assets/Audio/Missing.wav", diagnostic, StringComparison.Ordinal);

            var exception = Assert.Throws<FileNotFoundException>(() =>
                EngineProjectPackage.Create(Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName), packagePath));

            Assert.Contains(assetId.ToString(), exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Assets/Audio/Missing.wav", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(packagePath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(packagePath)!));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackagePreflightReturnsDiagnosticForInvalidSceneData()
    {
        var root = NewDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "source");
            var scenePath = Path.Combine(sourceRoot, "Content", "Scenes", "Start.json");
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
            File.WriteAllText(scenePath, "not a scene document");
            var projectPath = Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectPath, "Content/Scenes/Start.json");

            var validation = EngineProjectPackage.Validate(projectPath);

            Assert.False(validation.IsValid);
            Assert.Null(validation.GlbAssetCount);
            Assert.Null(validation.PackagedFileCount);
            Assert.Contains("Scene JSON is invalid", Assert.Single(validation.Diagnostics),
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackagePreflightReportsEveryMissingAudioReferenceBeforeCreatingOutput()
    {
        var root = NewDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "source");
            var packagePath = Path.Combine(root, "output", "BrokenPackage");
            var scenePath = Path.Combine(sourceRoot, "Content", "Scenes", "Start.json");
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
            var references = new[]
            {
                new SceneAudioAssetReference(Guid.NewGuid(), "Assets/Audio/Footsteps.wav"),
                new SceneAudioAssetReference(Guid.NewGuid(), "Assets/Audio/Theme.ogg"),
                new SceneAudioAssetReference(Guid.NewGuid(), "Content/Audio/Voice.wav")
            };
            var scene = new SceneGraph();
            scene.SetAudioAssets(references);
            SceneFile.SaveAtomic(scene, scenePath);
            var projectPath = Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectPath, "Content/Scenes/Start.json");

            var validation = EngineProjectPackage.Validate(projectPath);

            Assert.False(validation.IsValid);
            Assert.Equal(references.Length, validation.Diagnostics.Count);
            foreach (var reference in references)
                Assert.Contains(validation.Diagnostics,
                    diagnostic => diagnostic.Contains(reference.AssetId.ToString(), StringComparison.OrdinalIgnoreCase)
                                  && diagnostic.Contains(reference.SourcePath, StringComparison.Ordinal));

            var exception = Assert.Throws<InvalidDataException>(() =>
                EngineProjectPackage.Create(projectPath, packagePath));

            foreach (var reference in references)
                Assert.Contains(reference.AssetId.ToString(), exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(packagePath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(packagePath)!));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageRejectsRegisteredSequenceWithMissingSceneObjectBeforeCreatingOutput()
    {
        var root = NewDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "source");
            var scenePath = Path.Combine(sourceRoot, "Content", "Scenes", "Start.json");
            Directory.CreateDirectory(Path.GetDirectoryName(scenePath)!);
            SceneFile.SaveAtomic(new SceneGraph(), scenePath);
            var projectPath = Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectPath, "Content/Scenes/Start.json");

            var missingObjectId = Guid.NewGuid();
            var assetId = Guid.NewGuid();
            const string sequenceRelativePath = "Content/Sequences/Broken.sequence.json";
            var sequencePath = Path.Combine(sourceRoot, sequenceRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(sequencePath)!);
            File.WriteAllText(sequencePath, $$"""
            {
              "version": 2,
              "name": "Broken",
              "duration": 1,
              "characterTracks": [
                { "id": "{{Guid.NewGuid()}}", "targetObjectId": "{{missingObjectId}}", "assetId": "{{assetId}}", "clipName": "Walk", "startTime": 0, "playbackSpeed": 1, "loop": true }
              ],
              "cameraTracks": []
            }
            """);
            _ = EngineProjectFile.Load(projectPath).RegisterExtraContentPath(sequenceRelativePath);
            var packagePath = Path.Combine(root, "output", "package");

            var error = Assert.Throws<InvalidDataException>(() =>
                EngineProjectPackage.Create(projectPath, packagePath));

            Assert.Contains(sequenceRelativePath, error.Message, StringComparison.Ordinal);
            Assert.Contains(missingObjectId.ToString(), error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(packagePath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(packagePath)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackageRejectsInvalidWorldBeforeCreatingOutputAndReportsCellScene()
    {
        var root = NewDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "source");
            var manifestPath = Path.Combine(sourceRoot, "Content", "World", "world.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            var cellId = Guid.NewGuid();
            var manifestJson = $$"""
            {
              "version": 2,
              "exteriorCellWidth": 32,
              "cells": [
                { "id": "{{cellId}}", "kind": "Exterior", "exteriorCoordinate": { "x": 0, "z": 0 }, "scenePath": "Scenes/Missing.json" }
              ]
            }
            """;
            File.WriteAllText(manifestPath, manifestJson);

            var projectFilePath = Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectFilePath, startupScenePath: null,
                worldManifestPath: "Content/World/world.json");
            var packagePath = Path.Combine(root, "output", "BrokenWorld");

            var error = Assert.Throws<InvalidDataException>(() =>
                EngineProjectPackage.Create(projectFilePath, packagePath));

            Assert.Contains("world validation failed", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Missing.json", error.Message, StringComparison.Ordinal);
            Assert.Contains(cellId.ToString(), error.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(packagePath));
            Assert.False(Directory.Exists(Path.GetDirectoryName(packagePath)));
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

    [Fact]
    public void PackageContainsWorldManifestCellsScenesGlbsAndExtraContentAndCanRelocate()
    {
        var root = NewDirectory();
        try
        {
            var sourceRoot = Path.Combine(root, "source");
            var packagePath = Path.Combine(root, "output", "WorldPackage");
            var movedPath = Path.Combine(root, "moved", "WorldPackage");

            var glbSource = Path.Combine(AppContext.BaseDirectory, "Assets", "TextureCoordinateTest.glb");
            var glbDest = Path.Combine(sourceRoot, "Content", "Models", "hero.glb");
            Directory.CreateDirectory(Path.GetDirectoryName(glbDest)!);
            File.Copy(glbSource, glbDest);

            var exteriorScenePath = Path.Combine(sourceRoot, "Content", "World", "Scenes", "Exterior_0_0.json");
            Directory.CreateDirectory(Path.GetDirectoryName(exteriorScenePath)!);
            var exteriorScene = new SceneGraph();
            var heroId = Guid.NewGuid();
            exteriorScene.Add(new SceneObject(heroId, "Hero")
            {
                GltfAsset = new GltfAssetReference(Guid.NewGuid(), "Content/Models/hero.glb")
            });
            SceneFile.SaveAtomic(exteriorScene, exteriorScenePath);

            var interiorScenePath = Path.Combine(sourceRoot, "Content", "World", "Interiors", "House_A.json");
            Directory.CreateDirectory(Path.GetDirectoryName(interiorScenePath)!);
            var interiorScene = new SceneGraph();
            interiorScene.Add(new SceneObject(Guid.NewGuid(), "Table"));
            SceneFile.SaveAtomic(interiorScene, interiorScenePath);

            var manifestPath = Path.Combine(sourceRoot, "Content", "World", "world.json");
            var exteriorCellId = Guid.NewGuid();
            var interiorCellId = Guid.NewGuid();
            var manifestJson = $$"""
            {
              "version": 2,
              "exteriorCellWidth": 32,
              "cells": [
                { "id": "{{exteriorCellId}}", "kind": "Exterior", "exteriorCoordinate": { "x": 0, "z": 0 }, "scenePath": "Scenes/Exterior_0_0.json" },
                { "id": "{{interiorCellId}}", "kind": "Interior", "scenePath": "Interiors/House_A.json" }
              ]
            }
            """;
            File.WriteAllText(manifestPath, manifestJson);

            var exteriorPathGraph = new CellPathGraph
            {
                CellId = exteriorCellId,
                Kind = WorldCellKind.Exterior,
                Nodes = [new CellPathNode(Guid.NewGuid(), new NavigationPoint(0f, 0f, 0f))]
            };
            var interiorPathGraph = new CellPathGraph
            {
                CellId = interiorCellId,
                Kind = WorldCellKind.Interior,
                Nodes = [new CellPathNode(Guid.NewGuid(), new NavigationPoint(2f, 1f, 3f))]
            };
            var pathsDirectory = Path.Combine(sourceRoot, "Content", "World", "Paths");
            var editorNavigationDirectory = Path.Combine(sourceRoot, "Content", "World", "Navigation");
            CellPathGraphFile.SaveAtomic(Path.Combine(pathsDirectory, $"{exteriorCellId:N}.paths.json"),
                exteriorPathGraph);
            CellPathGraphFile.SaveAtomic(Path.Combine(editorNavigationDirectory, $"{interiorCellId:N}.paths.json"),
                interiorPathGraph);
            WorldPathNetworkFile.SaveAtomic(Path.Combine(pathsDirectory, "world-paths.json"),
                new WorldPathNetwork());

            var rpgContentPath = Path.Combine(sourceRoot, "Content", "RpgContent.json");
            File.WriteAllText(rpgContentPath, "{\"version\": 1}");

            var effectDir = Path.Combine(sourceRoot, "Content", "Effects");
            Directory.CreateDirectory(effectDir);
            File.WriteAllText(Path.Combine(effectDir, "TestEffect.fx"), "// fx shader");

            var projectFilePath = Path.Combine(sourceRoot, EngineProjectFile.DefaultFileName);
            EngineProjectFile.SaveAtomic(projectFilePath,
                startupScenePath: null,
                worldManifestPath: "Content/World/world.json",
                extraContentPaths: ["Content/RpgContent.json", "Content/Effects"]);

            var result = EngineProjectPackage.Create(projectFilePath, packagePath);

            Assert.Equal(packagePath, result.DirectoryPath);
            Assert.Equal(1, result.GlbAssetCount);
            Assert.Equal("Content/World/world.json", result.WorldManifestPath);
            Assert.True(File.Exists(Path.Combine(packagePath, EngineProjectFile.DefaultFileName)));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "World", "world.json")));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "World", "Scenes", "Exterior_0_0.json")));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "World", "Interiors", "House_A.json")));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "World", "Paths", $"{exteriorCellId:N}.paths.json")));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "World", "Paths", "world-paths.json")));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "World", "Navigation", $"{interiorCellId:N}.paths.json")));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "Models", "hero.glb")));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "RpgContent.json")));
            Assert.True(File.Exists(Path.Combine(packagePath, "Content", "Effects", "TestEffect.fx")));

            Directory.CreateDirectory(Path.GetDirectoryName(movedPath)!);
            Directory.Move(packagePath, movedPath);

            var movedProject = EngineProjectFile.Load(Path.Combine(movedPath, EngineProjectFile.DefaultFileName));
            Assert.Null(movedProject.StartupScenePath);
            Assert.Equal("Content/World/world.json", movedProject.WorldManifestPath);
            var movedManifest = WorldManifest.Load(movedProject.ResolveWorldManifestPath()!);
            Assert.Equal(2, movedManifest.Cells.Count);
            var movedValidation = WorldProjectValidator.Validate(movedProject.ResolveWorldManifestPath()!);
            Assert.True(movedValidation.IsValid, string.Join(Environment.NewLine, movedValidation.Diagnostics));
            Assert.Equal(2, movedValidation.PathGraphs.Count);

            var movedExterior = SceneFile.Load(movedManifest.ResolveScenePath(exteriorCellId));
            Assert.Equal(heroId, Assert.Single(movedExterior.Objects).Id);
            Assert.True(File.Exists(movedProject.ResolveContentPath("Content/Models/hero.glb")));
            Assert.True(File.Exists(movedProject.ResolveContentPath("Content/RpgContent.json")));
            Assert.True(File.Exists(movedProject.ResolveContentPath("Content/Effects/TestEffect.fx")));
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
