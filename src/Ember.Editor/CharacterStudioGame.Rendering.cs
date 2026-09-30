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
using Ember.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SharpGLTF.Schema2;

namespace Ember.Editor;

public sealed partial class CharacterStudioGame
{
    protected override void Draw(GameTime gameTime)
    {
        if (_sequenceExportJob is { IsRunning: true } export)
        {
            export.ProcessNextFrame(RenderSequenceExportFrame);
            if (!export.IsRunning) FinishSequenceExport();
        }

        _sceneDrawCalls = 0;
        _shadowDrawCalls = 0;
        _culledStaticDrawCalls = 0;
        _skinnedDrawCalls = 0;
        _frameMilliseconds = gameTime.ElapsedGameTime.TotalMilliseconds;
        var sceneBounds = GetSceneBounds() ?? new Bounds3(new Vector3(-1f), Vector3.One);
        var lightViewProjection = DirectionalShadowCamera.CreateViewProjection(
            sceneBounds, _camera.View, _camera.Projection, _sceneLighting.DirectionalDirection,
            _shadowMap!.Size);
        RenderShadowMap(lightViewProjection);

        GraphicsDevice.Clear(new Color(12, 16, 24));
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
        DrawShadowedScene(lightViewProjection);
        if (_playSession is null) DrawPendingAssetPlacementPreview();
        DrawSelectedBoxCollider();
        DrawViewportTransformGizmo();

        if (_showDiagnostics)
        {
            _ui.Begin();
            var statusRows = GetStatusRows();
            var statusHeight = Math.Max(48, 22 * statusRows.Count + 20);
            _ui.Panel(new Rectangle(16, 16, 760, statusHeight), new Color(12, 16, 24, 230), new Color(82, 101, 122));
            for (var row = 0; row < statusRows.Count; row++)
                _ui.TextFit(statusRows[row], new Vector2(28, 25 + (row * 22)), 736f, 1f, Color.White);
            _ui.End();
        }

        base.Draw(gameTime);
        _editorUi?.Render();
        EndHostFrame(hold: _sequenceExportJob?.IsRunning == true, exit: Exit);
    }

    protected override void OnDisplayChanged()
    {
        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio, far: 1000f);
        _shadowMap?.Resize(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
        if (_sequencePreviewEnabled) ApplySequenceAtCurrentTime();
    }

    protected override void UnloadContent()
    {
        Window.TextInput -= HandleTextInput;
        CancelPendingAssetPreview(updateStatus: false, frameScene: false);
        _sequenceExportJob?.Cancel();
        _sequenceExportTarget?.Dispose();
        _sequenceExportTarget = null;
        DisposePathPhysics();
        _playSession?.Dispose();
        _playSession = null;
        _editorUi?.Dispose();
        _editorUi = null;
        _preview?.Dispose();
        _preview = null;
        _shadowMap = null;
        _shadowEffect = null;
        _sceneResources?.Dispose();
        _sceneResources = null;
        _attachmentRenderer = null;
        DisposeHost();
        base.UnloadContent();
    }

    private static SceneGraph CreateDefaultScene(GltfAssetReference asset, bool pair)
    {
        var scene = new SceneGraph();
        scene.Add(new SceneObject(PreviewInstanceId, "GLB Preview")
        {
            Transform = new Transform(),
            GltfAsset = asset
        });
        if (pair) AddPairIfNeeded(scene);
        return scene;
    }

    private static GltfAssetReference ResolveDefaultAsset(string[] args) =>
        HasArgument(args, "--fox") || HasArgument(args, "--pair")
        || ParseOption(args, "--clip") is not null || ParseOption(args, "--crossfade") is not null
        || HasArgument(args, "--attach-hand") || ParseOption(args, "--export-sequence") is not null
        || ParseOption(args, "--open-sequence") is not null || ParseOption(args, "--save-sequence") is not null
            ? FoxAsset
            : DefaultAsset;

    private void HandleTextInput(object? sender, TextInputEventArgs args) =>
        _editorUi?.AddTextInput(args.Character);

    private CharacterEditorInfo? GetCharacterEditorInfo(Guid objectId)
    {
        if (_preview?.Current is not { } preview || CurrentScene.Find(objectId)?.GltfAsset is not { } reference
            || !preview.Assets.TryGetValue(reference.AssetId, out var asset)
            || asset.SkinnedCharacter is not { } character)
            return null;

        preview.CharacterInstances.TryGetValue(objectId, out var state);
        var playback = state?.Playback;
        return new CharacterEditorInfo(character.Animations.Select(clip => clip.Name).ToArray(),
            playback?.Clip.Name, playback?.Time ?? 0f, playback?.Clip.Duration ?? 0f,
            playback?.IsPlaying ?? false);
    }

    private void SelectCharacterClip(Guid objectId, string clipName)
    {
        if (!TryGetCharacterState(objectId, out var character, out var state)) return;
        if (_playSession is not null)
        {
            state.SelectClip(character, clipName);
            return;
        }

        var sceneObject = _sceneData.Find(objectId);
        if (sceneObject is null) return;
        var clip = character.Animations.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, clipName, StringComparison.OrdinalIgnoreCase));
        if (clip is null) return;

        var before = sceneObject.CharacterSettings?.DeepCopy();
        if (string.Equals(before?.ClipName, clip.Name, StringComparison.OrdinalIgnoreCase)
            && MathF.Abs(before?.Time ?? 0f) <= 0.0001f
            && before?.CrossfadeClipName is null)
            return;
        var after = before?.DeepCopy() ?? new GltfCharacterSettings();
        after.ClipName = clip.Name;
        after.Time = 0f;
        after.CrossfadeClipName = null;
        after.IsPlaying = before?.IsPlaying ?? false;
        var wasPreviewPlaying = state.Playback?.IsPlaying ?? false;
        _editorHistory.Execute(_sceneData,
            new EditCharacterSettingsCommand(objectId, before, after));
        AfterSceneStructureChange();
        if (wasPreviewPlaying && TryGetCharacterState(objectId, out _, out var updatedState))
            updatedState.SetPlaying(true);
    }

    private void SeekCharacter(Guid objectId, float time)
    {
        if (TryGetCharacterState(objectId, out _, out var state)) state.Seek(time);
    }

    private void CommitCharacterTimeEdit(Guid objectId, float beforeTime, float afterTime)
    {
        if (_playSession is not null || !float.IsFinite(afterTime)
            || MathF.Abs(beforeTime - afterTime) <= 0.0001f) return;
        var sceneObject = _sceneData.Find(objectId);
        if (sceneObject is null || !TryGetCharacterState(objectId, out _, out var state)) return;

        var before = sceneObject.CharacterSettings?.DeepCopy();
        var after = before?.DeepCopy() ?? new GltfCharacterSettings();
        after.ClipName = state.Playback?.Clip.Name ?? after.ClipName;
        after.Time = Math.Max(0f, afterTime);
        var wasPreviewPlaying = state.Playback?.IsPlaying ?? false;
        _editorHistory.Execute(_sceneData,
            new EditCharacterSettingsCommand(objectId, before, after));
        AfterSceneStructureChange();
        if (wasPreviewPlaying && TryGetCharacterState(objectId, out _, out var updatedState))
            updatedState.SetPlaying(true);
    }

    private void SetCharacterPlaying(Guid objectId, bool playing)
    {
        if (TryGetCharacterState(objectId, out _, out var state)) state.SetPlaying(playing);
    }

    private bool TryGetCharacterState(Guid objectId, out GltfSkinnedCharacterData character,
        out CharacterInstanceState state)
    {
        if (_preview?.Current is { } preview
            && CurrentScene.Find(objectId)?.GltfAsset is { } reference
            && preview.Assets.TryGetValue(reference.AssetId, out var asset)
            && asset.SkinnedCharacter is { } loadedCharacter
            && preview.CharacterInstances.TryGetValue(objectId, out var loadedState))
        {
            character = loadedCharacter;
            state = loadedState;
            return true;
        }

        character = null!;
        state = null!;
        return false;
    }

    private static void AddPairIfNeeded(SceneGraph scene)
    {
        if (scene.Find(PairPreviewInstanceId) is not null) return;
        var characters = scene.Objects.Where(item => item.GltfAsset?.AssetId == FoxAsset.AssetId).ToArray();
        if (characters.Length != 1) return;

        var source = characters[0];
        scene.Add(new SceneObject(PairPreviewInstanceId, $"{source.Name} (2)")
        {
            Transform = new Transform
            {
                Position = source.Transform.Position + new Vector3(90f, 0f, 0f),
                Rotation = source.Transform.Rotation,
                Scale = source.Transform.Scale
            },
            GltfAsset = source.GltfAsset
        });
    }

    private Dictionary<Guid, string> ResolveSceneAssets(SceneGraph? scene = null, string? contentRoot = null,
        EngineProjectFile? projectOverride = null)
    {
        scene ??= CurrentScene;
        var project = projectOverride ?? _project;
        var result = new Dictionary<Guid, string>();
        var references = new Dictionary<Guid, (GltfAssetReference Reference, Guid ObjectId)>();
        foreach (var item in scene.Objects)
        {
            foreach (var reference in EnumerateAssetReferences(item))
            {
                if (references.TryGetValue(reference.AssetId, out var existing)
                    && !string.Equals(existing.Reference.SourcePath, reference.SourcePath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"GLB asset ID {reference.AssetId} refers to conflicting paths.");
                references[reference.AssetId] = (reference, item.Id);
            }
        }

        if (references.Count == 0 && _project is null && contentRoot is null)
            references.Add(DefaultAsset.AssetId, (DefaultAsset, Guid.Empty));
        foreach (var (assetId, entry) in references)
        {
            var (reference, objectId) = entry;
            var resolvedPath = project is not null
                ? project.ResolveContentPath(reference.SourcePath)
                : Path.GetFullPath(reference.SourcePath, contentRoot ?? _sceneAssetRoot);
            if (!File.Exists(resolvedPath))
                throw new FileNotFoundException(
                    $"Scene object {objectId} references missing GLB asset {assetId} at project-relative path '{reference.SourcePath}' resolved to '{resolvedPath}'.",
                    resolvedPath);
            result.Add(assetId, resolvedPath);
        }
        return result;
    }

    private static IEnumerable<GltfAssetReference> EnumerateAssetReferences(SceneObject item)
    {
        if (item.GltfAsset is { } asset) yield return asset;
        if (item.StaticMeshLod is { } lod)
        {
            yield return lod.NearAsset;
            yield return lod.FarAsset;
        }
    }

    private GltfAssetReference? SelectMeshAsset(SceneObject item, Matrix instanceWorld)
    {
        if (item.StaticMeshLod is not { } lod) return item.GltfAsset;

        var currentlyFar = _farLodByObjectId.TryGetValue(item.Id, out var wasFar) && wasFar;
        var distance = Vector3.Distance(instanceWorld.Translation, _camera.Position);
        var selectFar = lod.SelectFar(currentlyFar, distance);
        _farLodByObjectId[item.Id] = selectFar;
        return selectFar ? lod.FarAsset : lod.NearAsset;
    }

    private static IReadOnlyDictionary<Guid, IReadOnlyList<GltfAnimationClipData>> BuildSequenceClipCatalog(
        PreviewResources preview)
    {
        var result = new Dictionary<Guid, IReadOnlyList<GltfAnimationClipData>>();
        foreach (var (assetId, asset) in preview.Assets)
            result.Add(assetId, asset.SkinnedCharacter?.Animations ?? Array.Empty<GltfAnimationClipData>());
        return result;
    }

    private Bounds3? GetSceneObjectBounds(SceneObject item)
    {
        if (_preview is null) return null;
        return GetSceneObjectBounds(CurrentScene, item, _preview.Current);
    }

    private static Bounds3? GetSceneObjectBounds(SceneGraph scene, SceneObject item, PreviewResources preview)
    {
        var reference = item.StaticMeshLod?.NearAsset ?? item.GltfAsset;
        if (reference is null || !preview.Assets.TryGetValue(reference.AssetId, out var asset)) return null;

        var instanceWorld = scene.GetWorldMatrix(item.Id);
        if (asset.SkinnedCharacter is { } character)
        {
            var localBounds = asset.AnimatedBounds
                ?? character.LocalBounds.Transform(character.Skin.MeshNodeRestWorldMatrix);
            return localBounds.Transform(instanceWorld);
        }

        if (asset.Scene is not { } importedScene) return null;
        Bounds3? result = null;
        foreach (var (nodeId, parts) in importedScene.MeshesByNodeId)
        foreach (var part in parts)
        {
            if (part.Mesh.LocalBounds is not { } localBounds) continue;
            var bounds = localBounds.Transform(importedScene.Scene.GetWorldMatrix(nodeId) * instanceWorld);
            result = result is { } current ? current.Encapsulate(bounds) : bounds;
        }

        return result;
    }

    private Bounds3? GetSceneBounds(bool includePendingAssetPreview = true)
    {
        if (_preview is null) return null;
        Bounds3? result = null;
        foreach (var item in CurrentScene.Objects)
        {
            if (!item.Enabled) continue;
            if (GetSceneObjectBounds(CurrentScene, item, _preview.Current) is not { } bounds) continue;
            result = result is { } current ? current.Encapsulate(bounds) : bounds;
        }

        if (includePendingAssetPreview && _playSession is null && _sequenceExportJob?.IsRunning != true
            && _pendingAssetPlacementPreview is { } pending
            && GetSceneObjectBounds(pending.Scene, pending.Item, pending.Resources) is { } previewBounds)
            result = result is { } current ? current.Encapsulate(previewBounds) : previewBounds;

        return result;
    }

    private void RenderShadowMap(Matrix lightViewProjection)
    {
        var shadowMap = _shadowMap ?? throw new InvalidOperationException("The directional shadow map was not initialized.");
        var effect = _shadowEffect ?? throw new InvalidOperationException("The scene shadow effect was not loaded.");
        shadowMap.Begin();
        try
        {
            GraphicsDevice.DepthStencilState = DepthStencilState.Default;
            GraphicsDevice.BlendState = BlendState.Opaque;
            foreach (var item in CurrentScene.Objects)
            {
                if (!item.Enabled) continue;
                var instanceWorld = CurrentScene.GetWorldMatrix(item.Id);
                var reference = SelectMeshAsset(item, instanceWorld);
                if (reference is null || !_preview!.Current.Assets.TryGetValue(reference.AssetId, out var asset)) continue;
                if (asset.SkinnedCharacter is { } character)
                {
                    if (!_preview.Current.CharacterInstances.TryGetValue(item.Id, out var state)) continue;
                    foreach (var primitive in character.Primitives)
                    {
                        SetShadowMaterial(effect, primitive.Material.BaseColorFactor,
                            primitive.Material.BaseColorImageIndex is { } imageIndex
                                ? asset.Textures[imageIndex]
                                : _white,
                            primitive.Material.HasBaseColorImage,
                            primitive.Material.AlphaMode == GltfAlphaMode.Mask,
                            primitive.Material.AlphaCutoff);
                        if (primitive.Material.DoubleSided) GraphicsDevice.RasterizerState = RasterizerState.CullNone;
                        else GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
                        effect.CurrentTechnique = effect.Techniques["SkinnedDepth"];
                        SetMatrix(effect, "World", state.Pose.MeshNodeWorldMatrix * instanceWorld);
                        SetMatrix(effect, "LightViewProjection", lightViewProjection);
                        _shadowDrawCalls++;
                        asset.SkinnedMeshBuffers[primitive.Mesh].Draw(effect, state.Pose);
                    }
                    continue;
                }

                if (asset.Scene is not { } importedScene) continue;
                foreach (var (nodeId, parts) in importedScene.MeshesByNodeId)
                foreach (var part in parts)
                {
                    SetShadowMaterial(effect, part.Material.BaseColorFactor,
                        part.Material.HasBaseColorImage
                            ? asset.Textures[part.Material.BaseColorImageIndex!.Value]
                            : _white,
                        part.Material.HasBaseColorImage,
                        part.Material.AlphaMode == GltfAlphaMode.Mask,
                        part.Material.AlphaCutoff);
                    GraphicsDevice.RasterizerState = part.Material.DoubleSided
                        ? RasterizerState.CullNone
                        : RasterizerState.CullCounterClockwise;
                    effect.CurrentTechnique = effect.Techniques["StaticDepth"];
                    SetMatrix(effect, "World", importedScene.Scene.GetWorldMatrix(nodeId) * instanceWorld);
                    SetMatrix(effect, "LightViewProjection", lightViewProjection);
                    _shadowDrawCalls++;
                    asset.MeshBuffers[part.Mesh].Draw(effect);
                }
            }
        }
        finally
        {
            shadowMap.End();
        }
    }

    private void DrawShadowedScene(Matrix lightViewProjection)
    {
        var effect = _shadowEffect ?? throw new InvalidOperationException("The scene shadow effect was not loaded.");
        var shadowMap = _shadowMap ?? throw new InvalidOperationException("The directional shadow map was not initialized.");
        _sceneLighting.Apply(effect);
        effect.Parameters["ShadowTexture"]?.SetValue(shadowMap.Texture);
        effect.Parameters["ShadowTexelSize"]?.SetValue(1f / shadowMap.Size);
        effect.Parameters["ShadowDepthBias"]?.SetValue(0.0015f);
        SetMatrix(effect, "View", _camera.View);
        SetMatrix(effect, "Projection", _camera.Projection);
        SetMatrix(effect, "LightViewProjection", lightViewProjection);
        var cameraFrustum = new BoundingFrustum(_camera.View * _camera.Projection);

        foreach (var item in CurrentScene.Objects)
        {
            if (!item.Enabled) continue;
            var preview = _preview!.Current;
            var instanceWorld = CurrentScene.GetWorldMatrix(item.Id);
            var reference = SelectMeshAsset(item, instanceWorld);
            if (reference is null) continue;
            if (!preview.Assets.TryGetValue(reference.AssetId, out var asset))
                throw new InvalidOperationException($"No loaded GLB asset exists for scene object '{item.Name}'.");
            if (asset.SkinnedCharacter is { } character)
            {
                if (!preview.CharacterInstances.TryGetValue(item.Id, out var state))
                    throw new InvalidOperationException($"No character playback state exists for scene object '{item.Name}'.");
                foreach (var primitive in character.Primitives)
                {
                    var world = state.Pose.MeshNodeWorldMatrix * instanceWorld;
                    SetSceneMaterial(effect, world, primitive.Material.BaseColorFactor,
                        primitive.Material.BaseColorImageIndex is { } imageIndex
                            ? asset.Textures[imageIndex]
                            : _white,
                        primitive.Material.HasBaseColorImage,
                        primitive.Material.AlphaMode == GltfAlphaMode.Mask,
                        primitive.Material.AlphaCutoff);
                    GraphicsDevice.RasterizerState = primitive.Material.DoubleSided
                        ? RasterizerState.CullNone
                        : RasterizerState.CullCounterClockwise;
                    effect.CurrentTechnique = effect.Techniques["SkinnedScene"];
                    _sceneDrawCalls++;
                    _skinnedDrawCalls++;
                    asset.SkinnedMeshBuffers[primitive.Mesh].Draw(effect, state.Pose);
                }

                if (preview.AttachmentsByInstanceId.TryGetValue(item.Id, out var attachments))
                {
                    var renderer = _attachmentRenderer
                        ?? throw new InvalidOperationException("The attachment prop renderer was not initialized.");
                    foreach (var attachment in attachments)
                        renderer.Draw(attachment.GetWorldMatrix(state.Pose, instanceWorld), _camera.View, _camera.Projection);
                }
                continue;
            }

            if (asset.Scene is not { } importedScene) continue;
            foreach (var (nodeId, parts) in importedScene.MeshesByNodeId)
            foreach (var part in parts)
            {
                var world = importedScene.Scene.GetWorldMatrix(nodeId) * instanceWorld;
                if (part.Mesh.LocalBounds is { } localBounds
                    && !StaticSceneCuller.IsVisible(localBounds, world, cameraFrustum))
                {
                    _culledStaticDrawCalls++;
                    continue;
                }

                SetSceneMaterial(effect, world, part.Material.BaseColorFactor,
                    part.Material.HasBaseColorImage
                        ? asset.Textures[part.Material.BaseColorImageIndex!.Value]
                        : _white,
                    part.Material.HasBaseColorImage,
                    part.Material.AlphaMode == GltfAlphaMode.Mask,
                    part.Material.AlphaCutoff);
                GraphicsDevice.RasterizerState = part.Material.DoubleSided
                    ? RasterizerState.CullNone
                    : RasterizerState.CullCounterClockwise;
                effect.CurrentTechnique = effect.Techniques["StaticScene"];
                _sceneDrawCalls++;
                asset.MeshBuffers[part.Mesh].Draw(effect);
            }
        }
    }

    private void DrawPendingAssetPlacementPreview()
    {
        if (_pendingAssetPlacementPreview is not { } pending || _shadowEffect is not { } effect) return;
        if (pending.Item.GltfAsset is not { } reference
            || !pending.Resources.Assets.TryGetValue(reference.AssetId, out var asset)) return;

        var instanceWorld = pending.Scene.GetWorldMatrix(pending.Item.Id);
        if (asset.SkinnedCharacter is { } character)
        {
            if (!pending.Resources.CharacterInstances.TryGetValue(pending.Item.Id, out var state)) return;
            foreach (var primitive in character.Primitives)
            {
                var world = state.Pose.MeshNodeWorldMatrix * instanceWorld;
                SetSceneMaterial(effect, world, primitive.Material.BaseColorFactor,
                    primitive.Material.BaseColorImageIndex is { } imageIndex
                        ? asset.Textures[imageIndex]
                        : _white,
                    primitive.Material.HasBaseColorImage,
                    primitive.Material.AlphaMode == GltfAlphaMode.Mask,
                    primitive.Material.AlphaCutoff);
                GraphicsDevice.RasterizerState = primitive.Material.DoubleSided
                    ? RasterizerState.CullNone
                    : RasterizerState.CullCounterClockwise;
                effect.CurrentTechnique = effect.Techniques["SkinnedScene"];
                _sceneDrawCalls++;
                _skinnedDrawCalls++;
                asset.SkinnedMeshBuffers[primitive.Mesh].Draw(effect, state.Pose);
            }
            return;
        }

        if (asset.Scene is not { } importedScene) return;
        var cameraFrustum = new BoundingFrustum(_camera.View * _camera.Projection);
        foreach (var (nodeId, parts) in importedScene.MeshesByNodeId)
        foreach (var part in parts)
        {
            var world = importedScene.Scene.GetWorldMatrix(nodeId) * instanceWorld;
            if (part.Mesh.LocalBounds is { } localBounds
                && !StaticSceneCuller.IsVisible(localBounds, world, cameraFrustum))
            {
                _culledStaticDrawCalls++;
                continue;
            }

            SetSceneMaterial(effect, world, part.Material.BaseColorFactor,
                part.Material.HasBaseColorImage
                    ? asset.Textures[part.Material.BaseColorImageIndex!.Value]
                    : _white,
                part.Material.HasBaseColorImage,
                part.Material.AlphaMode == GltfAlphaMode.Mask,
                part.Material.AlphaCutoff);
            GraphicsDevice.RasterizerState = part.Material.DoubleSided
                ? RasterizerState.CullNone
                : RasterizerState.CullCounterClockwise;
            effect.CurrentTechnique = effect.Techniques["StaticScene"];
            _sceneDrawCalls++;
            asset.MeshBuffers[part.Mesh].Draw(effect);
        }
    }

    private static void SetShadowMaterial(Effect effect, Vector4 materialColor,
        Texture2D texture, bool textureEnabled, bool alphaCutoutEnabled, float alphaCutoff)
    {
        effect.Parameters["MaterialColor"]?.SetValue(materialColor);
        effect.Parameters["BaseTexture"]?.SetValue(texture);
        effect.Parameters["BaseTextureEnabled"]?.SetValue(textureEnabled);
        effect.Parameters["AlphaCutoutEnabled"]?.SetValue(alphaCutoutEnabled);
        effect.Parameters["AlphaCutoff"]?.SetValue(alphaCutoff);
    }

    private static void SetSceneMaterial(Effect effect, Matrix world, Vector4 materialColor,
        Texture2D texture, bool textureEnabled, bool alphaCutoutEnabled, float alphaCutoff)
    {
        SetMatrix(effect, "World", world);
        SetMatrix(effect, "WorldInverseTranspose", GetWorldInverseTranspose(world));
        effect.Parameters["MaterialColor"]?.SetValue(materialColor);
        effect.Parameters["BaseTexture"]?.SetValue(texture);
        effect.Parameters["BaseTextureEnabled"]?.SetValue(textureEnabled);
        effect.Parameters["AlphaCutoutEnabled"]?.SetValue(alphaCutoutEnabled);
        effect.Parameters["AlphaCutoff"]?.SetValue(alphaCutoff);
    }

    private static Matrix GetWorldInverseTranspose(Matrix world)
    {
        var inverse = Matrix.Invert(world);
        if (!IsFinite(inverse)) return Matrix.Identity;
        return Matrix.Transpose(inverse);
    }

    private static bool IsFinite(Matrix value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14)
        && float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24)
        && float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34)
        && float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);

    private static void SetMatrix(Effect effect, string name, Matrix value)
    {
        var parameter = effect.Parameters[name]
            ?? throw new InvalidOperationException($"Effect is missing the {name} parameter.");
        parameter.SetValue(value);
    }

    private List<string> GetStatusRows()
    {
        var rows = new List<string>();
        if (_preview?.Current is { } preview)
        {
            foreach (var item in CurrentScene.Objects)
            {
                if (preview.CharacterInstances.TryGetValue(item.Id, out var state))
                    rows.Add($"{item.Name}: {state.Status}");
            }
        }

        rows.Add($"Render: {_sceneDrawCalls} scene draws ({_culledStaticDrawCalls} static culled, {_skinnedDrawCalls} skinned), {_shadowDrawCalls} shadow draws | frame interval {_frameMilliseconds:0.0} ms");
        rows.Add(_reimportStatus);
        return rows;
    }

    private static string FormatPlaybackStatus(GltfAnimationPlayback? playback,
        GltfAnimationPlayback? crossfadePlayback, float blend)
    {
        if (playback is null) return "bind pose";
        var state = playback.IsPlaying ? "playing" : "paused";
        var loop = playback.Loop ? "loop" : "once";
        var summary = $"{playback.Clip.Name} {playback.Time:0.00}/{playback.Clip.Duration:0.00}s x{playback.Speed:0.##} {loop} {state}";
        return crossfadePlayback is null
            ? summary
            : $"{summary} → {crossfadePlayback.Clip.Name} ({blend:0.00})";
    }

}
