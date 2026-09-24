using System;
using System.IO;
using Ember.Scene;
using Ember.World;
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
    public void SaveAndLoadPreservesRegisteredRpgPlacementAndStableInstanceId()
    {
        var scene = new SceneGraph();
        var objectId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        scene.Add(new SceneObject(objectId, "Town Guard")
        {
            WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Actor, "actors.town-guard", instanceId),
            Transform = new Transform { Position = new Vector3(3f, 0f, 5f) }
        });
        var path = TemporaryPath();
        try
        {
            SceneFile.SaveAtomic(scene, path);
            var json = File.ReadAllText(path);
            var loaded = SceneFile.Load(path);

            Assert.Contains("\"Version\": 6", json, StringComparison.Ordinal);
            Assert.Equal(WorldEntityKind.Actor, loaded.Find(objectId)!.WorldEntity!.Kind);
            Assert.Equal("actors.town-guard", loaded.Find(objectId)!.WorldEntity!.DefinitionId);
            Assert.Equal(instanceId, loaded.Find(objectId)!.WorldEntity!.InstanceId);
            Assert.Equal(new Vector3(3f, 0f, 5f), loaded.Find(objectId)!.Transform.Position);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void SaveRejectsDuplicateAuthoredWorldEntityInstanceIds()
    {
        var sharedInstanceId = Guid.NewGuid();
        var scene = new SceneGraph();
        scene.Add(new SceneObject(Guid.NewGuid(), "Actor")
        {
            WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Actor, "actors.guard", sharedInstanceId)
        });
        scene.Add(new SceneObject(Guid.NewGuid(), "Item")
        {
            WorldEntity = new WorldEntityPlacementComponent(WorldEntityKind.Item, "items.sword", sharedInstanceId)
        });

        Assert.Throws<InvalidDataException>(() => SceneFile.SaveAtomic(scene, TemporaryPath()));
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
    public void SaveAndLoadPreservesAuthoredStaticMeshLodAssetsAndThresholds()
    {
        var objectId = Guid.NewGuid();
        var near = new GltfAssetReference(Guid.NewGuid(), "Assets/house-near.glb");
        var far = new GltfAssetReference(Guid.NewGuid(), "Assets/house-far.glb");
        var scene = new SceneGraph();
        scene.Add(new SceneObject(objectId, "House")
        {
            StaticMeshLod = new GltfStaticMeshLod(near, far, enterFarDistance: 80f, exitFarDistance: 64f)
        });
        var path = TemporaryPath();
        try
        {
            SceneFile.SaveAtomic(scene, path);
            var json = File.ReadAllText(path);
            var loaded = SceneFile.Load(path);
            var lod = loaded.Find(objectId)!.StaticMeshLod!;

            Assert.Contains("\"Version\": 6", json, StringComparison.Ordinal);
            Assert.Equal(near.AssetId, lod.NearAsset.AssetId);
            Assert.Equal(near.SourcePath, lod.NearAsset.SourcePath);
            Assert.Equal(far.AssetId, lod.FarAsset.AssetId);
            Assert.Equal(far.SourcePath, lod.FarAsset.SourcePath);
            Assert.Equal(80f, lod.EnterFarDistance);
            Assert.Equal(64f, lod.ExitFarDistance);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void StaticMeshLodRequiresVersionFiveAndCannotMixWithDirectAsset()
    {
        const string lodJson = "\"StaticMeshLod\":{\"NearAssetId\":\"11111111-1111-4111-8111-111111111111\",\"NearAssetPath\":\"Assets/near.glb\",\"FarAssetId\":\"22222222-2222-4222-8222-222222222222\",\"FarAssetPath\":\"Assets/far.glb\",\"EnterFarDistance\":50,\"ExitFarDistance\":40}";
        var objectId = "33333333-3333-4333-8333-333333333333";
        var v4Path = TemporaryPath();
        var mixedPath = TemporaryPath();
        try
        {
            File.WriteAllText(v4Path,
                $"{{\"Version\":4,\"Objects\":[{{\"Id\":\"{objectId}\",\"Name\":\"LOD\",{lodJson},\"Position\":[0,0,0],\"Rotation\":[0,0,0,1],\"Scale\":[1,1,1]}}]}}");
            Assert.Throws<InvalidDataException>(() => SceneFile.Load(v4Path));

            File.WriteAllText(mixedPath,
                $"{{\"Version\":5,\"Objects\":[{{\"Id\":\"{objectId}\",\"Name\":\"LOD\",\"GltfAssetId\":\"44444444-4444-4444-8444-444444444444\",\"GltfAssetPath\":\"Assets/direct.glb\",{lodJson},\"Position\":[0,0,0],\"Rotation\":[0,0,0,1],\"Scale\":[1,1,1]}}]}}");
            Assert.Throws<InvalidDataException>(() => SceneFile.Load(mixedPath));
        }
        finally
        {
            Delete(v4Path);
            Delete(mixedPath);
        }
    }

    [Fact]
    public void SaveAndLoadPreservesDistinctEnvironmentAndSharedCharacterAssets()
    {
        var environment = new GltfAssetReference(
            Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111"), "Assets/ReleaseACourtyard.glb");
        var character = new GltfAssetReference(
            Guid.Parse("bbbbbbbb-2222-4222-8222-222222222222"), "Assets/Fox.glb");
        var environmentId = Guid.Parse("cccccccc-3333-4333-8333-333333333333");
        var firstCharacterId = Guid.Parse("dddddddd-4444-4444-8444-444444444444");
        var secondCharacterId = Guid.Parse("eeeeeeee-5555-4555-8555-555555555555");
        var scene = new SceneGraph();
        scene.Add(new SceneObject(environmentId, "Courtyard")
        {
            GltfAsset = environment,
            Transform = new Transform { Position = new Vector3(2f, 0f, -3f) }
        });
        scene.Add(new SceneObject(firstCharacterId, "Walker")
        {
            GltfAsset = character,
            Transform = new Transform { Position = new Vector3(-62f, 0f, 2f) },
            CharacterSettings = new GltfCharacterSettings { ClipName = "Walk", Time = 0.35f, IsPlaying = true }
        });
        scene.Add(new SceneObject(secondCharacterId, "Runner")
        {
            GltfAsset = character,
            Transform = new Transform { Position = new Vector3(62f, 0f, 2f) },
            CharacterSettings = new GltfCharacterSettings { ClipName = "Run", Time = 0.6f }
        });

        var path = TemporaryPath();
        try
        {
            SceneFile.SaveAtomic(scene, path);
            var loaded = SceneFile.Load(path);

            Assert.Equal(environment.AssetId, loaded.Find(environmentId)!.GltfAsset!.AssetId);
            Assert.Equal(environment.SourcePath, loaded.Find(environmentId)!.GltfAsset!.SourcePath);
            Assert.Equal(character.AssetId, loaded.Find(firstCharacterId)!.GltfAsset!.AssetId);
            Assert.Equal(character.AssetId, loaded.Find(secondCharacterId)!.GltfAsset!.AssetId);
            Assert.Equal(new Vector3(-62f, 0f, 2f), loaded.Find(firstCharacterId)!.Transform.Position);
            Assert.Equal("Walk", loaded.Find(firstCharacterId)!.CharacterSettings!.ClipName);
            Assert.True(loaded.Find(firstCharacterId)!.CharacterSettings!.IsPlaying);
            Assert.Equal("Run", loaded.Find(secondCharacterId)!.CharacterSettings!.ClipName);
            Assert.Equal(0.6f, loaded.Find(secondCharacterId)!.CharacterSettings!.Time);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void SaveAndLoadPreservesPerCharacterPlaybackAndAttachmentReferences()
    {
        var asset = new GltfAssetReference(Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567"),
            "Assets/Characters/fox.glb");
        var firstId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var secondId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var attachmentId = Guid.Parse("12345678-9abc-def0-1234-56789abcdef0");
        var offset = Matrix.CreateScale(2f, 3f, 4f) * Matrix.CreateRotationY(0.4f)
            * Matrix.CreateTranslation(1f, 2f, 3f);
        var scene = new SceneGraph();
        scene.Add(new SceneObject(firstId, "Walker")
        {
            GltfAsset = asset,
            CharacterSettings = new GltfCharacterSettings
            {
                ClipName = "Walk",
                Time = 0.35f,
                Speed = 1.5f,
                Loop = false,
                IsPlaying = true,
                CrossfadeClipName = "Run",
                BlendAmount = 0.4f
            }
        });
        scene.Find(firstId)!.CharacterSettings!.Attachments.Add(
            new GltfBoneAttachmentReference(attachmentId, "b_RightHand_08", offset));
        scene.Add(new SceneObject(secondId, "Runner")
        {
            GltfAsset = asset,
            CharacterSettings = new GltfCharacterSettings
            {
                ClipName = "Run",
                Time = 0.6f,
                Speed = 0.75f,
                Loop = true,
                IsPlaying = false
            }
        });

        var path = TemporaryPath();
        try
        {
            SceneFile.SaveAtomic(scene, path);
            var json = File.ReadAllText(path);
            var loaded = SceneFile.Load(path);
            var first = loaded.Find(firstId)!.CharacterSettings!;
            var second = loaded.Find(secondId)!.CharacterSettings!;

            Assert.Contains("\"Version\": 6", json, StringComparison.Ordinal);
            Assert.Equal("Walk", first.ClipName);
            Assert.Equal(0.35f, first.Time);
            Assert.Equal(1.5f, first.Speed);
            Assert.False(first.Loop);
            Assert.True(first.IsPlaying);
            Assert.Equal("Run", first.CrossfadeClipName);
            Assert.Equal(0.4f, first.BlendAmount);
            var attachment = Assert.Single(first.Attachments);
            Assert.Equal(attachmentId, attachment.Id);
            Assert.Equal("b_RightHand_08", attachment.BoneName);
            Assert.Equal(offset, attachment.LocalOffset);
            Assert.Equal("Run", second.ClipName);
            Assert.Equal(0.6f, second.Time);
            Assert.Equal(0.75f, second.Speed);
            Assert.True(second.Loop);
            Assert.False(second.IsPlaying);
            Assert.DoesNotContain("TriangleIndices", json, StringComparison.Ordinal);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void VersionOneSceneLoadsWithoutCharacterSettingsAndUpgradesOnSave()
    {
        var path = TemporaryPath();
        try
        {
            File.WriteAllText(path,
                "{\"Version\":1,\"Objects\":[{\"Id\":\"30303030-3030-3030-3030-303030303030\",\"Name\":\"Legacy\",\"Position\":[0,0,0],\"Rotation\":[0,0,0,1],\"Scale\":[1,1,1]}]}");
            var loaded = SceneFile.Load(path);

            Assert.Null(loaded.Find(Guid.Parse("30303030-3030-3030-3030-303030303030"))!.CharacterSettings);
            SceneFile.SaveAtomic(loaded, path);
            Assert.Contains("\"Version\": 6", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void SceneSaveRejectsInvalidCharacterPlaybackAndDuplicateAttachmentIds()
    {
        var asset = new GltfAssetReference(Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567"),
            "Assets/Characters/fox.glb");
        var attachmentId = Guid.Parse("12345678-9abc-def0-1234-56789abcdef0");
        var scene = new SceneGraph();
        var first = new SceneObject(Guid.Parse("11111111-2222-3333-4444-555555555555"), "First")
        {
            GltfAsset = asset,
            CharacterSettings = new GltfCharacterSettings { ClipName = "Walk", IsPlaying = true }
        };
        var second = new SceneObject(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"), "Second")
        {
            GltfAsset = asset,
            CharacterSettings = new GltfCharacterSettings { ClipName = "Run" }
        };
        first.CharacterSettings.Attachments.Add(
            new GltfBoneAttachmentReference(attachmentId, "b_RightHand_08", Matrix.Identity));
        second.CharacterSettings.Attachments.Add(
            new GltfBoneAttachmentReference(attachmentId, "b_LeftHand_08", Matrix.Identity));
        scene.Add(first);
        scene.Add(second);

        Assert.Throws<InvalidDataException>(() => SceneFile.SaveAtomic(scene, TemporaryPath()));
        first.CharacterSettings.Time = float.NaN;
        second.CharacterSettings.Attachments.Clear();
        Assert.Throws<InvalidDataException>(() => SceneFile.SaveAtomic(scene, TemporaryPath()));
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
