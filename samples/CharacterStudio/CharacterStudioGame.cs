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
    private static readonly Guid HandPreviewAttachmentId = Guid.Parse("fedcba98-7654-3210-fedc-ba9876543212");

    private readonly SceneGraph _sceneData;
    private readonly OrbitCamera _camera = new() { MaxDistance = 500f };
    private readonly List<string> _faults = new();
    private readonly string? _savePath;
    private readonly string? _sceneSavePath;
    private readonly PlaybackOptions _playbackOptions;
    private SceneResourceScope? _sceneResources;
    private ReloadableAsset<PreviewResources>? _preview;
    private string? _blockedSaveReason;
    private string _reimportStatus = "R: reimport scene GLBs | S: save scene";
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
            blendAmount, attachHand, hasSecondOptions);

        var openedExistingScene = false;
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
                openedExistingScene = true;
            }
            catch (Exception exception)
            {
                _sceneData = CreateDefaultScene(ResolveDefaultAsset(args), pair);
                _faults.Add($"open {openPath}: {exception.Message}");
                _reimportStatus = "Open failed; recovery scene is unsaved unless you use --save to another path.";
            }
        }

        // Never let S overwrite a malformed or unsupported source with the fallback scene.
        // An explicit --save path remains available for recovery/Save As.
        var saveTargetsFailedSource = !openedExistingScene && openPath is not null && _savePath is not null
            && PathsReferToSameFile(openPath, _savePath);
        _sceneSavePath = saveTargetsFailedSource
            ? null
            : _savePath ?? (openedExistingScene ? openPath : null);
        if (saveTargetsFailedSource)
            _blockedSaveReason = "Invalid source preserved; choose a different --save path.";

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

            _preview = new ReloadableAsset<PreviewResources>(PreviewResources.Load(
                GraphicsDevice, ResolveSceneAssets(), _sceneData));
            ApplyCommandLineSettings();
            _preview.Current.RebuildCharacterInstances(_sceneData);
            if (_preview.Current.HasSkinnedCharacters)
            {
                SkinnedEffectCompatibility.Validate(GraphicsDevice.GraphicsProfile,
                    _preview.Current.MaximumJointCount);
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

                if (_preview.Current.AttachmentsByInstanceId.Count > 0)
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

            if (_savePath is not null) SaveScene();
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
        if (_input.Pressed(_input.CurrentKeyboard, Keys.S)) SaveScene();
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
            if (item.GltfAsset is not { } reference) continue;
            var instanceWorld = _sceneData.GetWorldMatrix(item.Id);
            var preview = _preview!.Current;
            if (!preview.Assets.TryGetValue(reference.AssetId, out var asset))
                throw new InvalidOperationException($"No loaded GLB asset exists for scene object '{item.Name}'.");
            if (asset.SkinnedCharacter is { } character)
            {
                if (!preview.CharacterInstances.TryGetValue(item.Id, out var state))
                    throw new InvalidOperationException($"No character playback state exists for scene object '{item.Name}'.");
                DrawSkinnedCharacter(asset, character, state, instanceWorld);
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
            {
                var world = importedScene.Scene.GetWorldMatrix(nodeId) * instanceWorld;
                foreach (var part in parts)
                {
                    var factor = part.Material.BaseColorFactor;
                    _studioEffect.DiffuseColor = new Vector3(factor.X, factor.Y, factor.Z);
                    _studioEffect.Alpha = 1f;
                    _studioEffect.TextureEnabled = part.Material.HasBaseColorImage;
                    _studioEffect.Texture = part.Material.HasBaseColorImage
                        ? asset.Textures[part.Material.BaseColorImageIndex!.Value]
                        : null;
                    GraphicsDevice.RasterizerState = part.Material.DoubleSided
                        ? RasterizerState.CullNone
                        : RasterizerState.CullCounterClockwise;
                    asset.MeshBuffers[part.Mesh].Draw(_studioEffect, world, _camera.View, _camera.Projection);
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

    private Dictionary<Guid, string> ResolveSceneAssets()
    {
        var result = new Dictionary<Guid, string>();
        var references = new Dictionary<Guid, GltfAssetReference>();
        foreach (var item in _sceneData.Objects)
        {
            if (item.GltfAsset is not { } reference) continue;
            if (references.TryGetValue(reference.AssetId, out var existing)
                && !string.Equals(existing.SourcePath, reference.SourcePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"GLB asset ID {reference.AssetId} refers to conflicting paths.");
            references[reference.AssetId] = reference;
        }

        if (references.Count == 0) references.Add(DefaultAsset.AssetId, DefaultAsset);
        foreach (var (assetId, reference) in references)
            result.Add(assetId, Path.GetFullPath(reference.SourcePath, AppContext.BaseDirectory));
        return result;
    }

    private Bounds3? GetSceneBounds()
    {
        if (_preview is null) return null;
        Bounds3? result = null;
        foreach (var item in _sceneData.Objects)
        {
            if (!item.Enabled) continue;
            if (item.GltfAsset is not { } reference
                || !_preview.Current.Assets.TryGetValue(reference.AssetId, out var asset)) continue;
            var instanceWorld = _sceneData.GetWorldMatrix(item.Id);
            if (asset.SkinnedCharacter is { } character)
            {
                var localBounds = asset.AnimatedBounds
                    ?? character.LocalBounds.Transform(character.Skin.MeshNodeRestWorldMatrix);
                var bounds = localBounds.Transform(instanceWorld);
                result = result is { } current ? current.Encapsulate(bounds) : bounds;
                continue;
            }

            if (asset.Scene is not { } importedScene) continue;
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

    private void DrawSkinnedCharacter(AssetPreview preview, GltfSkinnedCharacterData character,
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

    private void ApplyCommandLineSettings()
    {
        var preview = _preview?.Current
            ?? throw new InvalidOperationException("Character assets must load before command-line playback settings are applied.");
        var characters = _sceneData.Objects
            .Where(item => preview.IsSkinnedObject(item))
            .ToArray();
        if (_playbackOptions.Pair && characters.Length < 2)
            throw new ArgumentException("--pair requires two scene objects that reference a skinned character asset.");

        if (_playbackOptions.AnimationName is { } primaryClip && characters.Length > 0)
        {
            var settings = characters[0].CharacterSettings ??= new GltfCharacterSettings();
            settings.ClipName = primaryClip;
            settings.Time = _playbackOptions.AnimationTime ?? 0f;
            settings.Speed = _playbackOptions.AnimationSpeed;
            settings.Loop = _playbackOptions.LoopAnimation;
            settings.IsPlaying = !_playbackOptions.PauseAnimation;
        }

        if ((_playbackOptions.HasSecondOptions
                || (_playbackOptions.Pair && _playbackOptions.AnimationName is not null))
            && characters.Length > 1)
        {
            var settings = characters[1].CharacterSettings ??= new GltfCharacterSettings();
            if (_playbackOptions.SecondAnimationName is { } secondClip)
                settings.ClipName = secondClip;
            settings.Time = _playbackOptions.SecondAnimationTime
                ?? _playbackOptions.AnimationTime ?? 0f;
            settings.Speed = _playbackOptions.SecondAnimationSpeed;
            settings.Loop = _playbackOptions.SecondLoopAnimation;
            settings.IsPlaying = !_playbackOptions.PauseSecond;
        }

        if (_playbackOptions.CrossfadeAnimationName is { } crossfadeClip && characters.Length > 0)
        {
            var settings = characters[0].CharacterSettings ??= new GltfCharacterSettings();
            settings.CrossfadeClipName = crossfadeClip;
            settings.BlendAmount = _playbackOptions.BlendAmount;
        }

        if (_playbackOptions.AttachHand)
        {
            if (characters.Length == 0)
                throw new ArgumentException("--attach-hand requires a scene object that references a skinned character asset.");
            var settings = characters[0].CharacterSettings ??= new GltfCharacterSettings();
            if (settings.Attachments.All(item => item.Id != HandPreviewAttachmentId))
            {
                settings.Attachments.Add(new GltfBoneAttachmentReference(HandPreviewAttachmentId,
                    "b_RightHand_08", Matrix.CreateScale(9f) * Matrix.CreateTranslation(0f, 0f, 12f)));
            }
        }
    }

    private void CaptureCharacterSettings()
    {
        if (_preview is null) return;
        foreach (var (objectId, state) in _preview.Current.CharacterInstances)
        {
            var sceneObject = _sceneData.Find(objectId)
                ?? throw new InvalidOperationException($"Scene object '{objectId}' disappeared while saving character settings.");
            state.StoreSettings(sceneObject.CharacterSettings ??= new GltfCharacterSettings());
        }
    }

    private void SaveScene()
    {
        if (_sceneSavePath is null)
        {
            _reimportStatus = _blockedSaveReason ?? "Pass --save <path> to enable S: save scene";
            return;
        }

        try
        {
            CaptureCharacterSettings();
            SceneFile.SaveAtomic(_sceneData, _sceneSavePath);
            _reimportStatus = $"Scene saved to {Path.GetFileName(_sceneSavePath)}.";
            Console.WriteLine($"Saved scene to {Path.GetFullPath(_sceneSavePath)}");
        }
        catch (Exception exception)
        {
            _reimportStatus = $"Scene save failed: {exception.Message}";
            Console.WriteLine($"CharacterStudio scene save failed: {exception.Message}");
        }
    }

    private static bool PathsReferToSameFile(string first, string second)
    {
        try
        {
            return string.Equals(Path.GetFullPath(first), Path.GetFullPath(second),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void ReimportAsset()
    {
        if (_preview is null) return;
        try
        {
            CaptureCharacterSettings();
            var cleanupError = _preview.Reload(() => PreviewResources.Load(
                GraphicsDevice, ResolveSceneAssets(), _sceneData));
            if (cleanupError is null)
            {
                _reimportStatus = "Scene GLB reimport succeeded. | S: save scene";
                Console.WriteLine("Reimported all GLB assets referenced by the scene.");
            }
            else
            {
                _reimportStatus = $"Reimport succeeded; cleanup failed: {cleanupError.Message} | S: save scene";
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
        bool AttachHand,
        bool HasSecondOptions);

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
            StoreSettings(Settings);
        }

        public void StoreSettings(GltfCharacterSettings settings)
        {
            if (Playback is null)
            {
                settings.Time = 0f;
                settings.IsPlaying = false;
                return;
            }

            settings.ClipName = Playback.Clip.Name;
            settings.Time = Playback.Time;
            settings.Speed = Playback.Speed;
            settings.Loop = Playback.Loop;
            settings.IsPlaying = Playback.IsPlaying;
            if (CrossfadePlayback is not null)
            {
                settings.CrossfadeClipName = CrossfadePlayback.Clip.Name;
                settings.BlendAmount = BlendAmount;
            }
        }

        public static CharacterInstanceState Create(GltfSkinnedCharacterData character,
            SceneObject sceneObject, string? primaryClipName, bool isSecondCharacter)
        {
            var settings = sceneObject.CharacterSettings ??= new GltfCharacterSettings();
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
