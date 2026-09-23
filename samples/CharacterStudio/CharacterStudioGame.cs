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
/// The first external consumer of the scene foundation: shared-asset scene instances, independent
/// character playback, an orbit camera, and screenshot/open/save paths. It deliberately has no game rules.
/// </summary>
public sealed class CharacterStudioGame : EngineHost
{
    private static readonly Guid PreviewInstanceId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
    private static readonly GltfAssetReference DefaultAsset = new(
        Guid.Parse("89abcdef-0123-4567-89ab-cdef01234567"), "Assets/TextureCoordinateTest.glb");
    private static readonly GltfAssetReference FoxAsset = new(
        Guid.Parse("fedcba98-7654-3210-fedc-ba9876543210"), "Assets/Fox.glb");
    private static readonly Guid PairPreviewInstanceId = Guid.Parse("fedcba98-7654-3210-fedc-ba9876543211");

    private readonly SceneGraph _sceneData;
    private readonly OrbitCamera _camera = new() { MaxDistance = 500f };
    private readonly List<string> _faults = new();
    private readonly string? _savePath;
    private readonly PlaybackOptions _playbackOptions;
    private SceneResourceScope? _sceneResources;
    private ReloadableAsset<PreviewResources>? _preview;
    private string? _assetPath;
    private string _reimportStatus = "R: reimport current GLB";
    private BasicEffect _studioEffect = null!;
    private SkinnedEffect? _skinnedEffect;
    private AttachmentBoxRenderer? _attachmentRenderer;
    private MouseState _lastMouse;
    private bool _hasMouse;

    public CharacterStudioGame(string[] args)
        : base(args, logicalWidth: 1280, logicalHeight: 720, title: "Ember Character Studio")
    {
        _savePath = ParseOption(args, "--save");
        var openPath = ParseOption(args, "--open");
        var requestedAnimation = ParseOption(args, "--clip");
        var requestedAnimationTime = ParseFiniteFloatOption(args, "--time");
        var requestedAnimationSpeed = ParseFiniteFloatOption(args, "--speed") ?? 1f;
        var pauseAnimation = HasArgument(args, "--pause");
        var startPlayback = HasArgument(args, "--play");
        var forceLoop = HasArgument(args, "--loop");
        var forceNoLoop = HasArgument(args, "--no-loop");
        var pair = HasArgument(args, "--pair");
        var secondClip = ParseOption(args, "--second-clip");
        var secondTime = ParseFiniteFloatOption(args, "--second-time");
        var secondSpeed = ParseFiniteFloatOption(args, "--second-speed") ?? 1f;
        var pauseSecond = HasArgument(args, "--pause-second");
        var secondNoLoop = HasArgument(args, "--second-no-loop");
        var crossfadeClip = ParseOption(args, "--crossfade");
        var blendAmount = ParseFiniteFloatOption(args, "--blend") ?? 0.5f;
        var attachHand = HasArgument(args, "--attach-hand");

        if (pauseAnimation && startPlayback)
            throw new ArgumentException("Use either --play or --pause with --clip, not both.");
        if (forceLoop && forceNoLoop)
            throw new ArgumentException("Use either --loop or --no-loop with --clip, not both.");
        var hasAnimationOptions = requestedAnimationTime is not null
            || ParseOption(args, "--speed") is not null
            || pauseAnimation || startPlayback || forceLoop || forceNoLoop;
        if (requestedAnimation is null && hasAnimationOptions)
            throw new ArgumentException("Animation playback options require --clip <name>.");
        var hasSecondOptions = secondClip is not null || secondTime is not null
            || ParseOption(args, "--second-speed") is not null || pauseSecond || secondNoLoop;
        if (hasSecondOptions && !pair)
            throw new ArgumentException("Second-character options require --pair.");
        if (hasSecondOptions && requestedAnimation is null)
            throw new ArgumentException("Second-character playback options require --clip <name> for the first character.");
        if (crossfadeClip is not null && requestedAnimation is null)
            throw new ArgumentException("--crossfade requires --clip <name>.");
        if (ParseOption(args, "--blend") is not null && crossfadeClip is null)
            throw new ArgumentException("--blend requires --crossfade <name>.");
        if (!float.IsFinite(blendAmount) || blendAmount < 0f || blendAmount > 1f)
            throw new ArgumentOutOfRangeException(nameof(blendAmount), "--blend must be between zero and one.");

        _playbackOptions = new PlaybackOptions(requestedAnimation, requestedAnimationTime,
            requestedAnimationSpeed, pauseAnimation, !forceNoLoop, pair, secondClip,
            secondTime, secondSpeed, pauseSecond, !secondNoLoop, crossfadeClip,
            blendAmount, attachHand);

        if (openPath is null)
        {
            _sceneData = CreateDefaultScene(ResolveDefaultAsset(args), pair);
        }
        else
        {
            try
            {
                _sceneData = SceneFile.Load(openPath);
                if (pair) AddPairIfNeeded(_sceneData);
            }
            catch (Exception exception)
            {
                _sceneData = CreateDefaultScene(ResolveDefaultAsset(args), pair);
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
            _preview = new ReloadableAsset<PreviewResources>(PreviewResources.Load(
                GraphicsDevice, _assetPath, _sceneData, _playbackOptions));
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

                if (_preview.Current.Attachment is not null)
                    _attachmentRenderer = _sceneResources.Own(new AttachmentBoxRenderer(GraphicsDevice));
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
        if (preview is not null)
        {
            foreach (var state in preview.CharacterInstances.Values)
                state.Advance((float)gameTime.ElapsedGameTime.TotalSeconds);
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
                if (!preview.CharacterInstances.TryGetValue(item.Id, out var state))
                    throw new InvalidOperationException($"No character playback state exists for scene object '{item.Name}'.");
                DrawSkinnedCharacter(preview, character, state, instanceWorld);
                if (preview.AttachmentInstanceId == item.Id && preview.Attachment is { } attachment)
                {
                    (_attachmentRenderer
                        ?? throw new InvalidOperationException("The attachment prop renderer was not initialized."))
                        .Draw(attachment.GetWorldMatrix(state.Pose, instanceWorld), _camera.View, _camera.Projection);
                }
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
        var statusRows = GetStatusRows();
        var statusHeight = Math.Max(48, 22 * statusRows.Count + 20);
        _ui.Panel(new Rectangle(16, 16, 760, statusHeight), new Color(12, 16, 24, 230), new Color(82, 101, 122));
        for (var row = 0; row < statusRows.Count; row++)
            _ui.TextFit(statusRows[row], new Vector2(28, 25 + (row * 22)), 736f, 1f, Color.White);
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
        || HasArgument(args, "--attach-hand")
            ? FoxAsset
            : DefaultAsset;

    private static void AddPairIfNeeded(SceneGraph scene)
    {
        if (scene.Find(PairPreviewInstanceId) is not null) return;
        var characters = scene.Objects.Where(item => item.GltfAsset is not null).ToArray();
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
        CharacterInstanceState state, Matrix instanceWorld)
    {
        var effect = _skinnedEffect
            ?? throw new InvalidOperationException("SkinnedEffect was not initialized for the character preview.");
        var pose = state.Pose;

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

    private List<string> GetStatusRows()
    {
        var rows = new List<string>();
        if (_preview?.Current is { } preview)
        {
            foreach (var item in _sceneData.Objects)
            {
                if (preview.CharacterInstances.TryGetValue(item.Id, out var state))
                    rows.Add($"{item.Name}: {state.Status}");
            }
        }

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
            var cleanupError = _preview.Reload(() => PreviewResources.Load(
                GraphicsDevice, _assetPath, _sceneData, _playbackOptions));
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

    private readonly record struct PlaybackOptions(
        string? AnimationName,
        float? AnimationTime,
        float AnimationSpeed,
        bool PauseAnimation,
        bool LoopAnimation,
        bool Pair,
        string? SecondAnimationName,
        float? SecondAnimationTime,
        float SecondAnimationSpeed,
        bool PauseSecond,
        bool SecondLoopAnimation,
        string? CrossfadeAnimationName,
        float BlendAmount,
        bool AttachHand);

    private sealed class CharacterInstanceState
    {
        private CharacterInstanceState(GltfSkinPose pose, GltfAnimationPlayback? playback,
            GltfAnimationPlayback? crossfadePlayback, GltfAnimationCrossfade? crossfade,
            float blendAmount)
        {
            Pose = pose;
            Playback = playback;
            CrossfadePlayback = crossfadePlayback;
            Crossfade = crossfade;
            BlendAmount = blendAmount;
            Evaluate();
        }

        public GltfSkinPose Pose { get; }
        public GltfAnimationPlayback? Playback { get; }
        public GltfAnimationPlayback? CrossfadePlayback { get; }
        public GltfAnimationCrossfade? Crossfade { get; }
        public float BlendAmount { get; }
        public string Status => FormatPlaybackStatus(Playback, CrossfadePlayback, BlendAmount);

        public void Advance(float elapsedSeconds)
        {
            Playback?.Advance(elapsedSeconds);
            CrossfadePlayback?.Advance(elapsedSeconds);
            Evaluate();
        }

        public static CharacterInstanceState Create(GltfSkinnedCharacterData character,
            PlaybackOptions options, bool isSecondCharacter)
        {
            var animationName = isSecondCharacter ? options.SecondAnimationName : options.AnimationName;
            if (isSecondCharacter && options.Pair && animationName is null && options.AnimationName is not null)
                animationName = ChooseOtherClip(character, options.AnimationName);

            var clip = ResolveClip(character, animationName);
            var pose = character.CreatePose();
            GltfAnimationPlayback? playback = null;
            GltfAnimationPlayback? crossfadePlayback = null;
            GltfAnimationCrossfade? crossfade = null;
            var animationTime = isSecondCharacter
                ? options.SecondAnimationTime ?? options.AnimationTime ?? 0f
                : options.AnimationTime ?? 0f;
            var animationSpeed = isSecondCharacter ? options.SecondAnimationSpeed : options.AnimationSpeed;
            var loop = isSecondCharacter ? options.SecondLoopAnimation : options.LoopAnimation;
            var paused = isSecondCharacter ? options.PauseSecond : options.PauseAnimation;

            if (clip is not null)
            {
                playback = new GltfAnimationPlayback(clip, loop, animationSpeed);
                playback.Seek(animationTime);
                if (!paused) playback.Play();
                clip.Evaluate(pose, playback.Time);

                // The crossfade demonstration belongs to the first instance; another instance can
                // still run its own clip clock and pose independently beside it.
                if (!isSecondCharacter && options.CrossfadeAnimationName is { } targetName)
                {
                    var targetClip = ResolveClip(character, targetName)
                        ?? throw new InvalidOperationException("A crossfade target clip name is required.");
                    crossfadePlayback = new GltfAnimationPlayback(targetClip, loop, animationSpeed);
                    crossfadePlayback.Seek(animationTime);
                    if (!paused) crossfadePlayback.Play();
                    crossfade = new GltfAnimationCrossfade(clip, targetClip);
                }
            }
            else if (isSecondCharacter && options.SecondAnimationName is not null)
            {
                throw new InvalidOperationException("The second character requires --clip <name> for the first character.");
            }

            return new CharacterInstanceState(pose, playback, crossfadePlayback, crossfade,
                options.BlendAmount);
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

    private sealed class PreviewResources : IDisposable
    {
        private readonly SceneResourceScope _resources;

        private PreviewResources(ImportedGltfScene? scene, GltfSkinnedCharacterData? skinnedCharacter,
            Dictionary<Guid, CharacterInstanceState> characterInstances, GltfBoneAttachment? attachment,
            Guid? attachmentInstanceId, SceneResourceScope resources,
            Dictionary<StaticMeshData, StaticMeshGpuBuffer> meshBuffers,
            Dictionary<GltfSkinnedMeshData, SkinnedMeshGpuBuffer> skinnedMeshBuffers,
            Dictionary<int, Texture2D> textures)
        {
            Scene = scene;
            SkinnedCharacter = skinnedCharacter;
            CharacterInstances = characterInstances;
            Attachment = attachment;
            AttachmentInstanceId = attachmentInstanceId;
            _resources = resources;
            MeshBuffers = meshBuffers;
            SkinnedMeshBuffers = skinnedMeshBuffers;
            Textures = textures;
        }

        public ImportedGltfScene? Scene { get; }
        public GltfSkinnedCharacterData? SkinnedCharacter { get; }
        public Dictionary<Guid, CharacterInstanceState> CharacterInstances { get; }
        public GltfBoneAttachment? Attachment { get; }
        public Guid? AttachmentInstanceId { get; }
        public Dictionary<StaticMeshData, StaticMeshGpuBuffer> MeshBuffers { get; }
        public Dictionary<GltfSkinnedMeshData, SkinnedMeshGpuBuffer> SkinnedMeshBuffers { get; }
        public Dictionary<int, Texture2D> Textures { get; }

        public static PreviewResources Load(GraphicsDevice device, string assetPath,
            SceneGraph sceneData, PlaybackOptions options)
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
                    var characterObjects = sceneData.Objects.Where(item => item.GltfAsset is not null).ToArray();
                    var characterInstances = new Dictionary<Guid, CharacterInstanceState>();
                    for (var index = 0; index < characterObjects.Length; index++)
                    {
                        var item = characterObjects[index];
                        characterInstances.Add(item.Id,
                            CharacterInstanceState.Create(character, options, options.Pair && index > 0));
                    }

                    if (options.Pair && characterObjects.Length < 2)
                        throw new InvalidOperationException("--pair requires two scene objects that reference the character asset.");
                    if ((options.AnimationName is not null || options.CrossfadeAnimationName is not null)
                        && characterObjects.Length == 0)
                        throw new InvalidOperationException("Animation playback requires a scene object that references the character asset.");

                    GltfBoneAttachment? attachment = null;
                    Guid? attachmentInstanceId = null;
                    if (options.AttachHand)
                    {
                        if (characterObjects.Length == 0)
                            throw new InvalidOperationException("--attach-hand requires a scene object that references the character asset.");
                        attachment = new GltfBoneAttachment(character.Skin, "b_RightHand_08",
                            Matrix.CreateScale(9f) * Matrix.CreateTranslation(0f, 0f, 12f));
                        attachmentInstanceId = characterObjects[0].Id;
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

                    return new PreviewResources(null, character, characterInstances, attachment,
                        attachmentInstanceId, resources, meshBuffers, skinnedMeshBuffers, textures);
                }

                if (options.AnimationName is not null || options.Pair || options.CrossfadeAnimationName is not null
                    || options.AttachHand)
                    throw new NotSupportedException("Character playback, pairing, crossfades, and hand attachments require a skinned character asset.");

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

                return new PreviewResources(scene, null, new Dictionary<Guid, CharacterInstanceState>(),
                    null, null, resources, meshBuffers, skinnedMeshBuffers, textures);
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
