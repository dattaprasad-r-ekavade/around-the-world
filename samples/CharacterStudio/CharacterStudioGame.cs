using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Ember.Assets;
using Ember;
using Ember.Input;
using Ember.Scene;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SharpGLTF.Schema2;

namespace CharacterStudio;

/// <summary>
/// The first external consumer of the scene foundation: one saved scene object, an orbit
/// camera, command-line clip playback, and screenshot/open/save paths. It deliberately has no game rules.
/// </summary>
public sealed class CharacterStudioGame : EngineHost
{
    private static readonly Guid PreviewInstanceId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
    private static readonly GltfAssetReference DefaultAsset = new(
        Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567"), "Assets/TextureCoordinateTest.glb");
    private static readonly GltfAssetReference FoxAsset = new(
        Guid.Parse("fedcba98-7654-3210-fedc-ba9876543210"), "Assets/Fox.glb");

    private readonly SceneGraph _sceneData;
    private readonly OrbitCamera _camera = new() { MaxDistance = 500f };
    private readonly List<string> _faults = new();
    private readonly string? _savePath;
    private readonly string? _requestedAnimation;
    private readonly float? _requestedAnimationTime;
    private readonly float _requestedAnimationSpeed;
    private readonly bool _pauseAnimation;
    private readonly bool _loopAnimation;
    private SceneResourceScope? _sceneResources;
    private ReloadableAsset<PreviewResources>? _preview;
    private string? _assetPath;
    private string _reimportStatus = "R: reimport current GLB";
    private BasicEffect _studioEffect = null!;
    private SkinnedEffect? _skinnedEffect;
    private MouseState _lastMouse;
    private bool _hasMouse;

    public CharacterStudioGame(string[] args)
        : base(args, logicalWidth: 1280, logicalHeight: 720, title: "Ember Character Studio")
    {
        _savePath = ParseOption(args, "--save");
        var openPath = ParseOption(args, "--open");
        _requestedAnimation = ParseOption(args, "--clip");
        _requestedAnimationTime = ParseFiniteFloatOption(args, "--time");
        _requestedAnimationSpeed = ParseFiniteFloatOption(args, "--speed") ?? 1f;
        _pauseAnimation = HasArgument(args, "--pause");
        var startPlayback = HasArgument(args, "--play");
        var forceLoop = HasArgument(args, "--loop");
        var forceNoLoop = HasArgument(args, "--no-loop");
        _loopAnimation = !forceNoLoop;

        if (_pauseAnimation && startPlayback)
            throw new ArgumentException("Use either --play or --pause with --clip, not both.");
        if (forceLoop && forceNoLoop)
            throw new ArgumentException("Use either --loop or --no-loop with --clip, not both.");
        var hasAnimationOptions = _requestedAnimationTime is not null
            || ParseOption(args, "--speed") is not null
            || _pauseAnimation || startPlayback || forceLoop || forceNoLoop;
        if (_requestedAnimation is null && hasAnimationOptions)
            throw new ArgumentException("Animation playback options require --clip <name>.");

        if (openPath is null)
        {
            _sceneData = CreateDefaultScene(HasArgument(args, "--fox") || _requestedAnimation is not null
                ? FoxAsset
                : DefaultAsset);
        }
        else
        {
            try
            {
                _sceneData = SceneFile.Load(openPath);
            }
            catch (Exception exception)
            {
                _sceneData = CreateDefaultScene(DefaultAsset);
                _faults.Add($"open {openPath}: {exception.Message}");
            }
        }

        if (_savePath is not null)
        {
            try
            {
                SceneFile.SaveAtomic(_sceneData, _savePath);
                Console.WriteLine($"Saved scene to {System.IO.Path.GetFullPath(_savePath)}");
            }
            catch (Exception exception)
            {
                _faults.Add($"save {_savePath}: {exception.Message}");
            }
        }
    }

    protected override void LoadContent()
    {
        _sceneResources = new SceneResourceScope();
        try
        {
            AttachCanvas();
            _studioEffect = _sceneResources.Own(new BasicEffect(GraphicsDevice)
            {
                VertexColorEnabled = false,
                TextureEnabled = false,
                LightingEnabled = true,
                PreferPerPixelLighting = true
            });
            _studioEffect.EnableDefaultLighting();
            _studioEffect.AmbientLightColor = new Vector3(0.62f, 0.64f, 0.68f);
            _studioEffect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.25f));
            _studioEffect.DirectionalLight0.DiffuseColor = new Vector3(0.9f);
            _studioEffect.DirectionalLight0.SpecularColor = new Vector3(0.12f);

            _assetPath = Path.GetFullPath(ResolveSceneAsset().SourcePath, AppContext.BaseDirectory);
            _preview = new ReloadableAsset<PreviewResources>(PreviewResources.Load(GraphicsDevice, _assetPath,
                _requestedAnimation, _requestedAnimationTime, _requestedAnimationSpeed,
                _pauseAnimation, _loopAnimation));
            if (_preview.Current.SkinnedCharacter is not null)
            {
                SkinnedEffectCompatibility.Validate(GraphicsDevice.GraphicsProfile,
                    _preview.Current.SkinnedCharacter.Skin.JointNodeIndices.Count);
                var skinnedEffect = _sceneResources.Own(new SkinnedEffect(GraphicsDevice)
                {
                    PreferPerPixelLighting = true,
                    SpecularPower = 24f
                });
                _skinnedEffect = skinnedEffect;
                skinnedEffect.EnableDefaultLighting();
                skinnedEffect.AmbientLightColor = new Vector3(0.58f, 0.60f, 0.64f);
                skinnedEffect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.25f));
                skinnedEffect.DirectionalLight0.DiffuseColor = new Vector3(0.9f);
            }

            _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio, far: 1000f);
            var sceneBounds = GetSceneBounds();
            if (sceneBounds is { } bounds)
            {
                _camera.Reset(bounds.Center, distance: 4.8f, yaw: 0.5f, pitch: -0.22f);
                _camera.Frame(bounds);
            }
            else
                _camera.Reset(Vector3.Zero, distance: 4.8f, yaw: 0.5f, pitch: -0.22f);

            foreach (var fault in _faults) Console.WriteLine($"character studio: {fault}");
        }
        catch
        {
            _preview?.Dispose();
            _preview = null;
            _sceneResources.Dispose();
            _sceneResources = null;
            throw;
        }
    }

    protected override void Update(GameTime gameTime)
    {
        BeginHostFrame();
        _input.Sample();
        var mouse = _input.CurrentMouse;

        if (_input.Pressed(_input.CurrentKeyboard, Keys.Escape)) Exit();
        if (_input.Pressed(_input.CurrentKeyboard, Keys.R)) ReimportAsset();
        if (!_hasMouse)
        {
            _lastMouse = mouse;
            _hasMouse = true;
        }

        if (mouse.LeftButton == ButtonState.Pressed)
        {
            _camera.Orbit(new Vector2(mouse.X - _lastMouse.X, mouse.Y - _lastMouse.Y));
        }

        _camera.Zoom(mouse.ScrollWheelValue - _lastMouse.ScrollWheelValue);
        _lastMouse = mouse;
        _input.Commit();
        var preview = _preview?.Current;
        if (preview?.Playback is { } playback)
        {
            playback.Advance((float)gameTime.ElapsedGameTime.TotalSeconds);
            playback.Clip.Evaluate(preview.SkinPose!, playback.Time);
        }
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(12, 16, 24));
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.BlendState = BlendState.Opaque;
        GraphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;

        foreach (var item in _sceneData.Objects)
        {
            if (!item.Enabled) continue;
            var instanceWorld = _sceneData.GetWorldMatrix(item.Id);
            var preview = _preview!.Current;
            if (preview.SkinnedCharacter is { } character)
            {
                DrawSkinnedCharacter(preview, character, instanceWorld);
                continue;
            }

            foreach (var (nodeId, parts) in preview.Scene!.MeshesByNodeId)
            {
                var world = preview.Scene.Scene.GetWorldMatrix(nodeId) * instanceWorld;
                foreach (var part in parts)
                {
                    var factor = part.Material.BaseColorFactor;
                    _studioEffect.DiffuseColor = new Vector3(factor.X, factor.Y, factor.Z);
                    _studioEffect.Alpha = 1f;
                    _studioEffect.TextureEnabled = part.Material.HasBaseColorImage;
                    _studioEffect.Texture = part.Material.HasBaseColorImage
                        ? preview.Textures[part.Material.BaseColorImageIndex!.Value]
                        : null;
                    GraphicsDevice.RasterizerState = part.Material.DoubleSided
                        ? RasterizerState.CullNone
                        : RasterizerState.CullCounterClockwise;
                    preview.MeshBuffers[part.Mesh].Draw(_studioEffect, world, _camera.View, _camera.Projection);
                }
            }
        }

        _ui.Begin();
        if (_preview?.Current.Playback is { } playback)
        {
            _ui.Panel(new Rectangle(16, 16, 760, 66), new Color(12, 16, 24, 230), new Color(82, 101, 122));
            _ui.TextFit(FormatPlaybackStatus(playback), new Vector2(28, 25), 736f, 1f, Color.White);
            _ui.TextFit(_reimportStatus, new Vector2(28, 47), 736f, 1f, Color.White);
        }
        else
        {
            _ui.Panel(new Rectangle(16, 16, 760, 48), new Color(12, 16, 24, 230), new Color(82, 101, 122));
            _ui.TextFit(_reimportStatus, new Vector2(28, 31), 736f, 1f, Color.White);
        }
        _ui.End();

        base.Draw(gameTime);
        EndHostFrame(hold: false, exit: Exit);
    }

    protected override void OnDisplayChanged() =>
        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio, far: 1000f);

    protected override void UnloadContent()
    {
        _preview?.Dispose();
        _preview = null;
        _sceneResources?.Dispose();
        _sceneResources = null;
        DisposeHost();
        base.UnloadContent();
    }

    private static SceneGraph CreateDefaultScene(GltfAssetReference asset)
    {
        var scene = new SceneGraph();
        scene.Add(new SceneObject(PreviewInstanceId, "GLB Preview")
        {
            Transform = new Transform(),
            GltfAsset = asset
        });
        return scene;
    }

    private GltfAssetReference ResolveSceneAsset()
    {
        GltfAssetReference? result = null;
        foreach (var item in _sceneData.Objects)
        {
            if (item.GltfAsset is not { } reference) continue;
            if (result is null)
            {
                result = reference;
                continue;
            }

            if (result.AssetId != reference.AssetId)
                throw new NotSupportedException("CharacterStudio currently previews one unique GLB asset per scene.");
            if (!string.Equals(result.SourcePath, reference.SourcePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"GLB asset ID {reference.AssetId} refers to conflicting paths.");
        }

        return result ?? DefaultAsset;
    }

    private Bounds3? GetSceneBounds()
    {
        if (_preview is null) return null;
        Bounds3? result = null;
        var importedScene = _preview.Current.Scene;
        foreach (var item in _sceneData.Objects)
        {
            if (!item.Enabled) continue;
            var instanceWorld = _sceneData.GetWorldMatrix(item.Id);
            if (_preview.Current.SkinnedCharacter is { } character)
            {
                var world = character.Skin.MeshNodeRestWorldMatrix * instanceWorld;
                var bounds = character.LocalBounds.Transform(world);
                result = result is { } current ? current.Encapsulate(bounds) : bounds;
                continue;
            }

            if (importedScene is null) continue;
            foreach (var (nodeId, parts) in importedScene.MeshesByNodeId)
            {
                var world = importedScene.Scene.GetWorldMatrix(nodeId) * instanceWorld;
                foreach (var part in parts)
                {
                    if (part.Mesh.LocalBounds is not { } localBounds) continue;
                    var bounds = localBounds.Transform(world);
                    result = result is { } current ? current.Encapsulate(bounds) : bounds;
                }
            }
        }

        return result;
    }

    private void DrawSkinnedCharacter(PreviewResources preview, GltfSkinnedCharacterData character,
        Matrix instanceWorld)
    {
        var effect = _skinnedEffect
            ?? throw new InvalidOperationException("SkinnedEffect was not initialized for the character preview.");
        var pose = preview.SkinPose
            ?? throw new InvalidOperationException("The skinned character preview has no per-instance pose.");

        foreach (var primitive in character.Primitives)
        {
            var factor = primitive.Material.BaseColorFactor;
            effect.DiffuseColor = new Vector3(factor.X, factor.Y, factor.Z);
            effect.Alpha = factor.W;
            effect.Texture = primitive.Material.BaseColorImageIndex is { } imageIndex
                ? preview.Textures[imageIndex]
                : null;
            GraphicsDevice.RasterizerState = primitive.Material.DoubleSided
                ? RasterizerState.CullNone
                : RasterizerState.CullCounterClockwise;
            preview.SkinnedMeshBuffers[primitive.Mesh].Draw(
                effect,
                pose,
                pose.MeshNodeWorldMatrix * instanceWorld,
                _camera.View,
                _camera.Projection,
                effect.Texture);
        }
    }

    private static string FormatPlaybackStatus(GltfAnimationPlayback playback)
    {
        var state = playback.IsPlaying ? "playing" : "paused";
        var loop = playback.Loop ? "loop" : "once";
        return $"{playback.Clip.Name} {playback.Time:0.00}/{playback.Clip.Duration:0.00}s x{playback.Speed:0.##} {loop} {state}";
    }

    private static float? ParseFiniteFloatOption(string[] args, string name)
    {
        var raw = ParseOption(args, name);
        if (raw is null) return null;
        if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !float.IsFinite(value))
        {
            throw new ArgumentException($"Option {name} must be a finite number; received '{raw}'.");
        }

        return value;
    }

    private void ReimportAsset()
    {
        if (_preview is null || _assetPath is null) return;
        try
        {
            var cleanupError = _preview.Reload(() => PreviewResources.Load(GraphicsDevice, _assetPath,
                _requestedAnimation, _requestedAnimationTime, _requestedAnimationSpeed,
                _pauseAnimation, _loopAnimation));
            if (cleanupError is null)
            {
                _reimportStatus = "GLB reimport succeeded.";
                Console.WriteLine($"Reimported GLB asset from {_assetPath}.");
            }
            else
            {
                _reimportStatus = $"Reimport succeeded; old resource cleanup failed: {cleanupError.Message}";
                Console.WriteLine($"Reimport succeeded, but previous asset cleanup failed: {cleanupError.Message}");
            }

            if (GetSceneBounds() is { } bounds) _camera.Frame(bounds);
        }
        catch (Exception exception)
        {
            _reimportStatus = $"Reimport failed; previous asset still active: {exception.Message}";
            Console.WriteLine($"GLB reimport failed; the previous asset remains active: {exception.Message}");
        }
    }

    private sealed class PreviewResources : IDisposable
    {
        private readonly SceneResourceScope _resources;

        private PreviewResources(ImportedGltfScene? scene, GltfSkinnedCharacterData? skinnedCharacter,
            GltfSkinPose? skinPose, GltfAnimationPlayback? playback, SceneResourceScope resources,
            Dictionary<StaticMeshData, StaticMeshGpuBuffer> meshBuffers,
            Dictionary<GltfSkinnedMeshData, SkinnedMeshGpuBuffer> skinnedMeshBuffers,
            Dictionary<int, Texture2D> textures)
        {
            Scene = scene;
            SkinnedCharacter = skinnedCharacter;
            SkinPose = skinPose;
            Playback = playback;
            _resources = resources;
            MeshBuffers = meshBuffers;
            SkinnedMeshBuffers = skinnedMeshBuffers;
            Textures = textures;
        }

        public ImportedGltfScene? Scene { get; }
        public GltfSkinnedCharacterData? SkinnedCharacter { get; }
        public GltfSkinPose? SkinPose { get; }
        public GltfAnimationPlayback? Playback { get; }
        public Dictionary<StaticMeshData, StaticMeshGpuBuffer> MeshBuffers { get; }
        public Dictionary<GltfSkinnedMeshData, SkinnedMeshGpuBuffer> SkinnedMeshBuffers { get; }
        public Dictionary<int, Texture2D> Textures { get; }

        public static PreviewResources Load(GraphicsDevice device, string assetPath, string? animationName,
            float? animationTime, float animationSpeed, bool pauseAnimation, bool loopAnimation)
        {
            var resources = new SceneResourceScope();
            try
            {
                var model = ModelRoot.Load(assetPath);
                var meshBuffers = new Dictionary<StaticMeshData, StaticMeshGpuBuffer>();
                var skinnedMeshBuffers = new Dictionary<GltfSkinnedMeshData, SkinnedMeshGpuBuffer>();
                var textures = new Dictionary<int, Texture2D>();

                if (model.LogicalNodes.Any(node => node.Skin is not null))
                {
                    var character = GltfSkinnedCharacterData.Import(model);
                    var pose = character.CreatePose();
                    GltfAnimationPlayback? playback = null;
                    if (animationName is not null)
                    {
                        var matchingClips = character.Animations
                            .Where(clip => string.Equals(clip.Name, animationName, StringComparison.OrdinalIgnoreCase))
                            .ToArray();
                        if (matchingClips.Length != 1)
                        {
                            var names = string.Join(", ", character.Animations.Select(clip => clip.Name));
                            throw new InvalidOperationException(matchingClips.Length == 0
                                ? $"Animation '{animationName}' was not found. Available clips: {names}."
                                : $"Animation name '{animationName}' is ambiguous in this asset.");
                        }

                        var clip = matchingClips[0];
                        playback = new GltfAnimationPlayback(clip, loopAnimation, animationSpeed);
                        if (animationTime is { } startTime) playback.Seek(startTime);
                        if (!pauseAnimation) playback.Play();
                        clip.Evaluate(pose, playback.Time);
                    }

                    foreach (var primitive in character.Primitives)
                    {
                        var buffer = resources.Own(new SkinnedMeshGpuBuffer(
                            device, primitive.Mesh, character.Skin.JointNodeIndices.Count));
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

                    return new PreviewResources(null, character, pose, playback, resources,
                        meshBuffers, skinnedMeshBuffers, textures);
                }

                if (animationName is not null)
                    throw new NotSupportedException("--clip can only be used with a skinned character asset.");

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
                        var texture = resources.Own(Texture2D.FromStream(device, imageStream));
                        textures.Add(imageIndex, texture);
                    }
                }

                return new PreviewResources(scene, null, null, null, resources,
                    meshBuffers, skinnedMeshBuffers, textures);
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
