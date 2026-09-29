using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ember.Authoring;
using Ember.Assets;
using Ember.Audio;
using Ember;
using Ember.Input;
using Ember.Project;
using Ember.Physics;
using Ember.Scene;
using Ember.Sequence;
using Ember.Render;
using Ember.Rpg;
using Ember.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SharpGLTF.Schema2;

namespace Ember.Editor;

public sealed partial class CharacterStudioGame
{
    private sealed class CharacterInstanceState
    {
        private CharacterInstanceState(GltfCharacterSettings settings, GltfSkinPose pose, GltfAnimationPlayback? playback,
            GltfAnimationPlayback? crossfadePlayback, GltfAnimationCrossfade? crossfade,
            float blendAmount)
        {
            Settings = settings;
            Pose = pose;
            Playback = playback;
            CrossfadePlayback = crossfadePlayback;
            Crossfade = crossfade;
            BlendAmount = blendAmount;
            Evaluate();
        }

        public GltfCharacterSettings Settings { get; }
        public GltfSkinPose Pose { get; }
        public GltfAnimationPlayback? Playback { get; private set; }
        public GltfAnimationPlayback? CrossfadePlayback { get; private set; }
        public GltfAnimationCrossfade? Crossfade { get; private set; }
        public float BlendAmount { get; private set; }
        public string Status => FormatPlaybackStatus(Playback, CrossfadePlayback, BlendAmount);

        public void Advance(float elapsedSeconds)
        {
            Playback?.Advance(elapsedSeconds);
            CrossfadePlayback?.Advance(elapsedSeconds);
            Evaluate();
        }

        public void SelectClip(GltfSkinnedCharacterData character, string clipName)
        {
            var clip = ResolveClip(character, clipName)
                ?? throw new ArgumentException($"Animation '{clipName}' was not found.", nameof(clipName));
            var wasPlaying = Playback?.IsPlaying ?? Settings.IsPlaying;
            var playback = new GltfAnimationPlayback(clip, Settings.Loop, Settings.Speed);
            if (wasPlaying) playback.Play();
            Playback = playback;
            CrossfadePlayback = null;
            Crossfade = null;
            BlendAmount = Settings.BlendAmount;
            Settings.CrossfadeClipName = null;
            Settings.ClipName = clip.Name;
            Settings.Time = 0f;
            Settings.IsPlaying = wasPlaying;
            Evaluate();
        }

        public void Seek(float time)
        {
            if (Playback is null) return;
            Playback.Seek(time);
            CrossfadePlayback?.Seek(time);
            Evaluate();
        }

        public void SetPlaying(bool playing)
        {
            if (Playback is null) return;
            if (playing)
            {
                Playback.Play();
                CrossfadePlayback?.Play();
            }
            else
            {
                Playback.Pause();
                CrossfadePlayback?.Pause();
            }
            Evaluate();
        }

        public static CharacterInstanceState Create(GltfSkinnedCharacterData character,
            SceneObject sceneObject, string? primaryClipName, bool isSecondCharacter)
        {
            var settings = sceneObject.CharacterSettings?.DeepCopy() ?? new GltfCharacterSettings();
            if (isSecondCharacter && settings.ClipName is null && primaryClipName is not null)
                settings.ClipName = ChooseOtherClip(character, primaryClipName);

            var clip = ResolveClip(character, settings.ClipName);
            var pose = character.CreatePose();
            GltfAnimationPlayback? playback = null;
            GltfAnimationPlayback? crossfadePlayback = null;
            GltfAnimationCrossfade? crossfade = null;

            if (clip is not null)
            {
                playback = new GltfAnimationPlayback(clip, settings.Loop, settings.Speed);
                playback.Seek(settings.Time);
                if (settings.IsPlaying) playback.Play();
                clip.Evaluate(pose, playback.Time);

                if (settings.CrossfadeClipName is { } targetName)
                {
                    var targetClip = ResolveClip(character, targetName)
                        ?? throw new InvalidOperationException("A crossfade target clip name is required.");
                    crossfadePlayback = new GltfAnimationPlayback(targetClip, settings.Loop, settings.Speed);
                    crossfadePlayback.Seek(settings.Time);
                    if (settings.IsPlaying) crossfadePlayback.Play();
                    crossfade = new GltfAnimationCrossfade(clip, targetClip);
                }
            }
            else if (settings.CrossfadeClipName is not null || settings.IsPlaying)
            {
                throw new InvalidOperationException($"Scene object '{sceneObject.Name}' has playback settings without a primary animation clip.");
            }

            return new CharacterInstanceState(settings, pose, playback, crossfadePlayback, crossfade,
                settings.BlendAmount);
        }

        private void Evaluate()
        {
            if (Crossfade is not null && Playback is not null && CrossfadePlayback is not null)
                Crossfade.Evaluate(Pose, Playback.Time, CrossfadePlayback.Time, BlendAmount);
            else if (Playback is not null)
                Playback.Clip.Evaluate(Pose, Playback.Time);
        }

        private static GltfAnimationClipData? ResolveClip(GltfSkinnedCharacterData character, string? name)
        {
            if (name is null) return null;
            var matches = character.Animations
                .Where(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 1) return matches[0];
            var names = string.Join(", ", character.Animations.Select(candidate => candidate.Name));
            throw new InvalidOperationException(matches.Length == 0
                ? $"Animation '{name}' was not found. Available clips: {names}."
                : $"Animation name '{name}' is ambiguous in this asset.");
        }

        private static string? ChooseOtherClip(GltfSkinnedCharacterData character, string primaryName)
        {
            var preferred = string.Equals(primaryName, "Run", StringComparison.OrdinalIgnoreCase) ? "Walk" : "Run";
            return character.Animations.FirstOrDefault(clip =>
                    string.Equals(clip.Name, preferred, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(clip.Name, primaryName, StringComparison.OrdinalIgnoreCase))?.Name
                ?? character.Animations.FirstOrDefault(clip =>
                    !string.Equals(clip.Name, primaryName, StringComparison.OrdinalIgnoreCase))?.Name
                ?? primaryName;
        }
    }

    private sealed class AssetPreview
    {
        public AssetPreview(ImportedGltfScene? scene, GltfSkinnedCharacterData? skinnedCharacter,
            Bounds3? animatedBounds, Dictionary<StaticMeshData, StaticMeshGpuBuffer> meshBuffers,
            Dictionary<GltfSkinnedMeshData, SkinnedMeshGpuBuffer> skinnedMeshBuffers,
            Dictionary<int, Texture2D> textures)
        {
            Scene = scene;
            SkinnedCharacter = skinnedCharacter;
            AnimatedBounds = animatedBounds;
            MeshBuffers = meshBuffers;
            SkinnedMeshBuffers = skinnedMeshBuffers;
            Textures = textures;
        }

        public ImportedGltfScene? Scene { get; }
        public GltfSkinnedCharacterData? SkinnedCharacter { get; }
        public Bounds3? AnimatedBounds { get; }
        public Dictionary<StaticMeshData, StaticMeshGpuBuffer> MeshBuffers { get; }
        public Dictionary<GltfSkinnedMeshData, SkinnedMeshGpuBuffer> SkinnedMeshBuffers { get; }
        public Dictionary<int, Texture2D> Textures { get; }
    }

    private sealed class PreviewResources : IDisposable
    {
        private readonly SceneResourceScope _resources;

        private PreviewResources(Dictionary<Guid, AssetPreview> assets, SceneResourceScope resources)
        {
            Assets = assets;
            _resources = resources;
        }

        public Dictionary<Guid, AssetPreview> Assets { get; }
        public Dictionary<Guid, CharacterInstanceState> CharacterInstances { get; } = new();
        public Dictionary<Guid, List<GltfBoneAttachment>> AttachmentsByInstanceId { get; } = new();
        public bool HasSkinnedCharacters => Assets.Values.Any(asset => asset.SkinnedCharacter is not null);
        public int OwnedGraphicsResourceCount => Assets.Values.Sum(asset =>
            asset.MeshBuffers.Count + asset.SkinnedMeshBuffers.Count + asset.Textures.Count);
        public int MaximumJointCount => Assets.Values
            .Where(asset => asset.SkinnedCharacter is not null)
            .Max(asset => asset.SkinnedCharacter!.Skin.JointNodeIndices.Count);

        public bool IsSkinnedObject(SceneObject item) => item.GltfAsset is { } reference
            && Assets.TryGetValue(reference.AssetId, out var asset)
            && asset.SkinnedCharacter is not null;

        public void RebuildCharacterInstances(SceneGraph sceneData)
        {
            CharacterInstances.Clear();
            AttachmentsByInstanceId.Clear();

            foreach (var group in sceneData.Objects.Where(IsSkinnedObject)
                         .GroupBy(item => item.GltfAsset!.AssetId))
            {
                var asset = Assets[group.Key];
                var character = asset.SkinnedCharacter!;
                var characterObjects = group.ToArray();
                var primaryClipName = characterObjects.FirstOrDefault()?.CharacterSettings?.ClipName;
                for (var index = 0; index < characterObjects.Length; index++)
                {
                    var item = characterObjects[index];
                    CharacterInstances.Add(item.Id,
                        CharacterInstanceState.Create(character, item, primaryClipName, index > 0));
                    if (item.CharacterSettings is { Attachments.Count: > 0 } settings)
                    {
                        AttachmentsByInstanceId.Add(item.Id, settings.Attachments
                            .Select(reference => new GltfBoneAttachment(character.Skin,
                                reference.BoneName, reference.LocalOffset))
                            .ToList());
                    }
                }
            }
        }

        public static PreviewResources Load(GraphicsDevice device,
            IReadOnlyDictionary<Guid, string> assetPaths, SceneGraph sceneData)
        {
            var resources = new SceneResourceScope();
            try
            {
                var assets = new Dictionary<Guid, AssetPreview>();
                foreach (var (assetId, assetPath) in assetPaths)
                {
                    var model = ModelRoot.Load(assetPath);
                    var meshBuffers = new Dictionary<StaticMeshData, StaticMeshGpuBuffer>();
                    var skinnedMeshBuffers = new Dictionary<GltfSkinnedMeshData, SkinnedMeshGpuBuffer>();
                    var textures = new Dictionary<int, Texture2D>();

                    if (model.LogicalNodes.Any(node => node.Skin is not null))
                    {
                        var lodObject = sceneData.Objects.FirstOrDefault(item =>
                            item.StaticMeshLod is { } lod
                            && (lod.NearAsset.AssetId == assetId || lod.FarAsset.AssetId == assetId));
                        if (lodObject is not null)
                            throw new NotSupportedException(
                                $"Static mesh LODs require static GLBs; object '{lodObject.Name}' references skinned asset {assetId}.");

                        var character = GltfSkinnedCharacterData.Import(model);
                        Bounds3? animatedBounds = null;
                        foreach (var clip in character.Animations)
                        {
                            var clipBounds = GltfAnimationBounds.SampleClip(character, clip);
                            animatedBounds = animatedBounds is { } current
                                ? current.Encapsulate(clipBounds)
                                : clipBounds;
                        }

                        foreach (var primitive in character.Primitives)
                        {
                            var buffer = resources.Own(new SkinnedMeshGpuBuffer(
                                device, primitive.Mesh, character.Skin));
                            skinnedMeshBuffers.Add(primitive.Mesh, buffer);

                            if (primitive.Material.HasBaseColorImage
                                && primitive.Material.BaseColorImageIndex is { } imageIndex
                                && !textures.ContainsKey(imageIndex))
                            {
                                using var imageStream = new MemoryStream(
                                    primitive.Material.BaseColorImage.ToArray(), writable: false);
                                textures.Add(imageIndex, resources.Own(Texture2D.FromStream(device, imageStream)));
                            }
                        }

                        assets.Add(assetId, new AssetPreview(null, character, animatedBounds,
                            meshBuffers, skinnedMeshBuffers, textures));
                        continue;
                    }

                    if (sceneData.Objects.Any(item => item.GltfAsset?.AssetId == assetId
                            && item.CharacterSettings is not null))
                    {
                        throw new NotSupportedException(
                            $"Character playback settings and attachments require a skinned GLB (asset {assetId}).");
                    }

                    var scene = GltfSceneImporter.Import(model);
                    foreach (var parts in scene.MeshesByNodeId.Values)
                    foreach (var part in parts)
                    {
                        if (!meshBuffers.ContainsKey(part.Mesh))
                        {
                            var buffer = resources.Own(new StaticMeshGpuBuffer(device, part.Mesh));
                            meshBuffers.Add(part.Mesh, buffer);
                        }

                        if (part.Material.HasBaseColorImage && part.Material.BaseColorImageIndex is { } imageIndex
                            && !textures.ContainsKey(imageIndex))
                        {
                            using var imageStream = new MemoryStream(part.Material.BaseColorImage.ToArray(), writable: false);
                            textures.Add(imageIndex, resources.Own(Texture2D.FromStream(device, imageStream)));
                        }
                    }

                    assets.Add(assetId, new AssetPreview(scene, null, null,
                        meshBuffers, skinnedMeshBuffers, textures));
                }

                var preview = new PreviewResources(assets, resources);
                preview.RebuildCharacterInstances(sceneData);
                return preview;
            }
            catch
            {
                resources.Dispose();
                throw;
            }
        }

        public void Dispose() => _resources.Dispose();
    }
}
