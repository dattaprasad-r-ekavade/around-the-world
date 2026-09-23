using System;
using System.IO;
using Ember.Scene;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneFileTests
{
    [Fact]
    public void SaveAndLoadPreservesHierarchyAndTransforms()
    {
        var parentId = Guid.Parse("10101010-1010-1010-1010-101010101010");
        var childId = Guid.Parse("20202020-2020-2020-2020-202020202020");
        var scene = new SceneGraph();
        scene.Add(new SceneObject(parentId, "Parent")
        {
            Transform = new Transform { Position = new Vector3(4f, 1f, -2f) }
        });
        scene.Add(new SceneObject(childId, "Child")
        {
            Enabled = false,
            Transform = new Transform { Position = new Vector3(0f, 2f, 3f) }
        });
        scene.SetParent(childId, parentId);

        var path = TemporaryPath();
        try
        {
            SceneFile.SaveAtomic(scene, path);
            var loaded = SceneFile.Load(path);

            Assert.Equal(2, loaded.Objects.Count);
            Assert.Equal("Child", loaded.Find(childId)!.Name);
            Assert.False(loaded.Find(childId)!.Enabled);
            Assert.Equal(parentId, loaded.Find(childId)!.ParentId);
            AssertClose(Vector3.Transform(Vector3.Zero, scene.GetWorldMatrix(childId)),
                Vector3.Transform(Vector3.Zero, loaded.GetWorldMatrix(childId)));
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void SaveAndLoadPreservesSharedGltfAssetReferencesWithoutEmbeddingMeshData()
    {
        var assetId = Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567");
        var firstId = Guid.Parse("70707070-7070-7070-7070-707070707070");
        var secondId = Guid.Parse("80808080-8080-8080-8080-808080808080");
        var reference = new GltfAssetReference(assetId, @"Assets\Characters\guard.glb");
        var scene = new SceneGraph();
        scene.Add(new SceneObject(firstId, "Guard One") { GltfAsset = reference });
        scene.Add(new SceneObject(secondId, "Guard Two") { GltfAsset = reference });

        var path = TemporaryPath();
        try
        {
            SceneFile.SaveAtomic(scene, path);
            var json = File.ReadAllText(path);
            var loaded = SceneFile.Load(path);

            Assert.Contains(assetId.ToString(), json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Assets/Characters/guard.glb", json, StringComparison.Ordinal);
            Assert.DoesNotContain("Vertices", json, StringComparison.Ordinal);
            Assert.DoesNotContain("TriangleIndices", json, StringComparison.Ordinal);
            Assert.Equal(assetId, loaded.Find(firstId)!.GltfAsset!.AssetId);
            Assert.Equal(assetId, loaded.Find(secondId)!.GltfAsset!.AssetId);
            Assert.Equal("Assets/Characters/guard.glb", loaded.Find(firstId)!.GltfAsset!.SourcePath);
            Assert.Equal(loaded.Find(firstId)!.GltfAsset!.SourcePath, loaded.Find(secondId)!.GltfAsset!.SourcePath);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void GltfAssetReferenceRejectsAbsoluteAndParentTraversalPaths()
    {
        var assetId = Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567");

        Assert.Throws<ArgumentException>(() => new GltfAssetReference(assetId, @"C:\Assets\hero.glb"));
        Assert.Throws<ArgumentException>(() => new GltfAssetReference(assetId, "../../outside.glb"));
        Assert.Throws<ArgumentException>(() => new GltfAssetReference(assetId, "Assets/hero.gltf"));
    }

    [Fact]
    public void SaveRejectsConflictingPathsForOneGltfAssetId()
    {
        var assetId = Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567");
        var scene = new SceneGraph();
        scene.Add(new SceneObject(Guid.Parse("90909090-9090-9090-9090-909090909090"), "One")
        {
            GltfAsset = new GltfAssetReference(assetId, "Assets/one.glb")
        });
        scene.Add(new SceneObject(Guid.Parse("a0a0a0a0-a0a0-a0a0-a0a0-a0a0a0a0a0a0"), "Two")
        {
            GltfAsset = new GltfAssetReference(assetId, "Assets/two.glb")
        });

        Assert.Throws<InvalidDataException>(() => SceneFile.SaveAtomic(scene, TemporaryPath()));
    }

    [Fact]
    public void InvalidVersionAndMissingParentAreRejected()
    {
        var versionPath = TemporaryPath();
        var parentPath = TemporaryPath();
        var duplicatePath = TemporaryPath();
        var cyclePath = TemporaryPath();
        try
        {
            File.WriteAllText(versionPath, "{\"Version\":99,\"Objects\":[]}");
            Assert.Throws<InvalidDataException>(() => SceneFile.Load(versionPath));

            File.WriteAllText(parentPath, "{\"Version\":1,\"Objects\":[{\"Id\":\"30303030-3030-3030-3030-303030303030\",\"Name\":\"Child\",\"ParentId\":\"40404040-4040-4040-4040-404040404040\",\"Position\":[0,0,0],\"Rotation\":[0,0,0,1],\"Scale\":[1,1,1]}]}");
            Assert.Throws<InvalidDataException>(() => SceneFile.Load(parentPath));

            File.WriteAllText(duplicatePath, "{\"Version\":1,\"Objects\":[{\"Id\":\"30303030-3030-3030-3030-303030303030\",\"Name\":\"One\",\"Position\":[0,0,0],\"Rotation\":[0,0,0,1],\"Scale\":[1,1,1]},{\"Id\":\"30303030-3030-3030-3030-303030303030\",\"Name\":\"Two\",\"Position\":[0,0,0],\"Rotation\":[0,0,0,1],\"Scale\":[1,1,1]}]}");
            Assert.Throws<InvalidDataException>(() => SceneFile.Load(duplicatePath));

            File.WriteAllText(cyclePath, "{\"Version\":1,\"Objects\":[{\"Id\":\"50505050-5050-5050-5050-505050505050\",\"Name\":\"One\",\"ParentId\":\"60606060-6060-6060-6060-606060606060\",\"Position\":[0,0,0],\"Rotation\":[0,0,0,1],\"Scale\":[1,1,1]},{\"Id\":\"60606060-6060-6060-6060-606060606060\",\"Name\":\"Two\",\"ParentId\":\"50505050-5050-5050-5050-505050505050\",\"Position\":[0,0,0],\"Rotation\":[0,0,0,1],\"Scale\":[1,1,1]}]}");
            Assert.Throws<InvalidDataException>(() => SceneFile.Load(cyclePath));
        }
        finally
        {
            Delete(versionPath);
            Delete(parentPath);
            Delete(duplicatePath);
            Delete(cyclePath);
        }
    }

    [Fact]
    public void InvalidReplacementLeavesPreviousSaveReadable()
    {
        var path = TemporaryPath();
        try
        {
            var scene = new SceneGraph();
            var item = new SceneObject(Guid.Parse("50505050-5050-5050-5050-505050505050"), "Valid");
            scene.Add(item);
            SceneFile.SaveAtomic(scene, path);

            item.Transform.Position = new Vector3(float.NaN, 0f, 0f);
            Assert.Throws<InvalidDataException>(() => SceneFile.SaveAtomic(scene, path));

            var loaded = SceneFile.Load(path);
            Assert.Equal("Valid", loaded.Find(item.Id)!.Name);
            Assert.Equal(Vector3.Zero, loaded.Find(item.Id)!.Transform.Position);
        }
        finally
        {
            Delete(path);
        }
    }

    private static string TemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"ember-scene-{Guid.NewGuid():N}.json");

    private static void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f,
            $"Expected {expected}, got {actual}");
    }
}
