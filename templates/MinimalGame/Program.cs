using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember;
using Ember.Assets;
using Ember.Input;
using Ember.Physics;
using Ember.Project;
using Ember.Render;
using Ember.Scene;
using Ember.World;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SharpGLTF.Schema2;

namespace MinimalEmberGame;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--validate-package", StringComparer.OrdinalIgnoreCase))
        {
            var projectPath = GetOption(args, "--project") ?? ResolveDefaultProjectPath();
            var validation = EngineProjectPackage.Validate(projectPath);
            if (!validation.IsValid)
            {
                foreach (var diagnostic in validation.Diagnostics)
                    Console.Error.WriteLine($"Cannot publish project: {diagnostic}");
                Environment.ExitCode = 1;
                return;
            }

            Console.WriteLine(
                $"Project content is ready: {validation.SceneCount} scene(s), {validation.GlbAssetCount} GLB asset(s), " +
                $"{validation.AudioAssetCount} referenced audio asset(s), {validation.SequenceCount} sequence(s), " +
                $"{validation.PackagedFileCount} package file(s).");
            return;
        }

        var packageDestination = GetOption(args, "--package-to");
        if (packageDestination is not null)
        {
            var projectPath = GetOption(args, "--project")
                ?? ResolveDefaultProjectPath();
            var package = EngineProjectPackage.Create(projectPath, packageDestination);
            Console.WriteLine($"Packaged project to '{package.DirectoryPath}' with {package.SceneCount} scene(s), " +
                $"{package.GlbAssetCount} GLB asset(s), {package.AudioAssetCount} referenced audio asset(s), " +
                $"{package.SequenceCount} sequence(s), and {package.PackagedFileCount} file(s).");
            return;
        }

        using var game = new MinimalGame(args);
        game.Run();
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.Ordinal)) continue;
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                throw new ArgumentException($"{name} requires a path value.");
            return args[index + 1];
        }
        return null;
    }

    internal static string ResolveDefaultProjectPath()
    {
        var distributionProject = Path.Combine(AppContext.BaseDirectory, "Project", EngineProjectFile.DefaultFileName);
        return File.Exists(distributionProject)
            ? distributionProject
            : Path.Combine(AppContext.BaseDirectory, EngineProjectFile.DefaultFileName);
    }
}

internal sealed class MinimalGame : EngineHost
{
    private const string ExitActionName = "Exit";
    private static readonly Keys[] ControlSmokeMovementKeys = { Keys.W, Keys.A, Keys.S, Keys.D };

    private readonly string _projectPath;
    private readonly bool _controlSmoke;
    private readonly List<string> _faults = new();
    private readonly OrbitCamera _camera = new();
    private readonly List<PointLight> _lights = new();
    private readonly Dictionary<Guid, CharacterAsset> _characterAssets = new();
    private readonly Dictionary<Guid, CharacterInstance> _characterInstances = new();
    private readonly Dictionary<Guid, StaticAsset> _staticAssets = new();
    private readonly InputActionMap _actions = CreateInputActions();
    private EngineProjectFile _project = null!;
    private SceneGraph _sceneGraph = null!;
    private ScenePlaySession? _playSession;
    private PhysicsWorld? _physicsWorld;
    private SceneStaticColliderSet? _sceneColliders;
    private PhysicsFixedStepper? _physicsStepper;
    private PhysicsCharacterController? _playerController;
    private Guid? _playerObjectId;
    private SceneRenderer _scene = null!;
    private BasicEffect? _staticEffect;
    private AlphaTestEffect? _maskedStaticEffect;
    private int _controlSmokeFrame;
    private string _actionStatus = "Walk into marked triggers to collect items or reach goals.";

    public MinimalGame(string[] args)
        : base(args, logicalWidth: 1280, logicalHeight: 720, title: "Minimal Ember Game")
    {
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        _controlSmoke = args.Contains("--smoke-controls", StringComparer.OrdinalIgnoreCase);
        _projectPath = Path.GetFullPath(ParseOption(args, "--project")
            ?? Program.ResolveDefaultProjectPath());
    }

    protected override void LoadContent()
    {
        _project = EngineProjectFile.Load(_projectPath);
        var authoredScene = SceneFile.Load(_project.ResolveStartupScenePath());
        _playSession = new ScenePlaySession(authoredScene);
        _playSession.AuthoredActionExecuted += OnAuthoredActionExecuted;
        _sceneGraph = _playSession.RuntimeScene;
        _sceneGraph.PlaySettings.ApplyInputBindings(_actions);
        _staticEffect = new BasicEffect(GraphicsDevice);
        _staticEffect.EnableDefaultLighting();
        _staticEffect.AmbientLightColor = new Vector3(0.54f, 0.57f, 0.62f);
        _maskedStaticEffect = new AlphaTestEffect(GraphicsDevice);
        LoadSceneAssets();
        InitializeGameplayRuntime();
        if (_controlSmoke && (_playerObjectId is null || _characterInstances.Count == 0))
            throw new InvalidOperationException("--smoke-controls requires a startup scene with a skinned character.");
        _scene = new SceneRenderer(GraphicsDevice);
        AttachCanvas();
        AttachScene(_faults);
        _ui.Resize(GraphicsDevice.Viewport, _uiScalePreference);

        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio, far: 1000f);
        _camera.MaxDistance = 500f;
        var sceneBounds = GetFramingBounds();
        if (_playerObjectId is { } playerId)
        {
            var settings = _sceneGraph.PlaySettings;
            var playerPosition = _sceneGraph.GetWorldMatrix(playerId).Translation;
            _camera.Reset(playerPosition + new Vector3(0f, settings.CameraTargetOffsetY, 0f),
                settings.CameraDistance, yaw: 0.55f, pitch: -0.24f);
        }
        else if (sceneBounds is { } bounds)
        {
            _camera.Reset(bounds.Center, distance: 8f, yaw: 0.55f, pitch: -0.24f);
            _camera.Frame(bounds);
        }
        else
        {
            var firstObject = _sceneGraph.Objects.FirstOrDefault(item => item.Enabled);
            var target = firstObject is null
                ? Vector3.Zero
                : Vector3.Transform(Vector3.Zero, _sceneGraph.GetWorldMatrix(firstObject.Id));
            _camera.Reset(target, distance: 6f, yaw: 0.55f, pitch: -0.24f);
        }
        Console.WriteLine($"Loaded startup scene '{_project.StartupScenePath}' with {_sceneGraph.Objects.Count} objects and {_characterInstances.Count} animated character(s).");
        foreach (var fault in _faults) Console.WriteLine($"ember project: {fault}");
    }

    protected override void Update(GameTime gameTime)
    {
        BeginHostFrame();
        _input.Sample();
        if (_controlSmoke && _controlSmokeFrame > 8)
            throw new InvalidOperationException("Control smoke failed: Escape did not exit the consumer.");
        var keyboard = _controlSmoke ? ControlSmokeKeyboard(_controlSmokeFrame) : _input.CurrentKeyboard;
        var input = _actions.Sample(keyboard, _controlSmoke || IsActive, uiCapturesKeyboard: false);
        if (input[ExitActionName].WasPressed)
        {
            if (_controlSmoke)
            {
                if (_controlSmokeFrame != 8)
                    throw new InvalidOperationException("Control smoke received Escape before all WASD directions ran.");
                Console.WriteLine("PASS control smoke: W/A/S/D moved the character and Escape requested exit.");
            }
            Exit();
            return;
        }
        if (_controlSmoke && _controlSmokeFrame == 8)
            throw new InvalidOperationException("Control smoke failed: Escape action was not detected.");

        var elapsed = _controlSmoke ? 0.1f : RealSeconds(gameTime);
        foreach (var character in _characterInstances.Values)
            character.Advance(elapsed);

        var movement2D = input.ReadMovement();
        var previousPlayerPosition = _playerObjectId is { } selectedId
            ? _sceneGraph.GetWorldMatrix(selectedId).Translation
            : Vector3.Zero;
        if (_playerController is { } controller)
        {
            controller.SetMoveInput(ToOrbitCameraMovement(movement2D, _camera.Yaw));
            if (_actions.ConsumePressed(GameplayActionNames.Jump)) controller.RequestJump();
        }

        PhysicsStepResult? physicsStep = null;
        if (_physicsWorld is { } world && _physicsStepper is { } stepper)
        {
            physicsStep = stepper.Advance(elapsed, delta =>
            {
                world.Step(delta);
                if (_playSession is { } session && _sceneColliders is { } colliders)
                    session.DispatchTriggerEvents(colliders.TriggerEvents);
            });
        }

        if (_playerController is { } playerController
            && _playerObjectId is { } playerObjectId
            && _sceneGraph.Find(playerObjectId) is { } player)
        {
            var step = physicsStep ?? throw new InvalidOperationException("Player physics did not advance.");
            var settings = _sceneGraph.PlaySettings;
            var centerOffset = CharacterCenterOffset(settings);
            var position = _physicsWorld!.GetInterpolatedPose(playerController.PhysicsBodyId,
                step.InterpolationAlpha).Position;
            SetObjectWorldPosition(_sceneGraph, player, position - new Vector3(0f, centerOffset, 0f));
            var worldPosition = _sceneGraph.GetWorldMatrix(playerObjectId).Translation;
            _camera.Reset(worldPosition + new Vector3(0f, settings.CameraTargetOffsetY, 0f),
                settings.CameraDistance, _camera.Yaw, _camera.Pitch);
            if (_controlSmoke && movement2D.LengthSquared() > 0f)
                VerifyControlSmokeMovement(previousPlayerPosition, worldPosition, movement2D, elapsed);
        }

        if (_controlSmoke && (_controlSmokeFrame & 1) == 0 && movement2D.LengthSquared() == 0f)
            throw new InvalidOperationException("Control smoke failed: a movement key did not move the character.");
        if (_controlSmoke) _controlSmokeFrame++;
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(12, 16, 24));
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;
        _scene.Begin(LitEffect, _camera.View, _camera.Projection, _camera.Position, 0f,
            StoneTextures.StonePalette.Sandstone, _lights);

        foreach (var item in _sceneGraph.Objects.Where(item => item.Enabled))
        {
            var world = _sceneGraph.GetWorldMatrix(item.Id);
            if (_characterInstances.TryGetValue(item.Id, out var character))
                character.Asset.Draw(GraphicsDevice, character.Pose,
                    world, _camera.View, _camera.Projection);
            else if (GetMeshAssetReference(item) is { } reference
                     && _staticAssets.TryGetValue(reference.AssetId, out var staticAsset))
                staticAsset.Draw(GraphicsDevice, _staticEffect!, _maskedStaticEffect!,
                    world, _camera.View, _camera.Projection);
            else
                _scene.DrawCube(world, new Color(173, 139, 88));
        }

        _ui.Begin();
        _ui.Panel(new Rectangle(20, 20, 720, 132), new Color(12, 16, 24, 220), new Color(94, 120, 148));
        _ui.Text("MINIMAL EMBER GAME", new Vector2(38, 34), 19, Color.White);
        _ui.TextFit($"Startup scene: {_project.StartupScenePath} | {_sceneGraph.Objects.Count} objects",
            new Vector2(38, 65), 684f, 1f, new Color(197, 207, 220));
        var settings = _sceneGraph.PlaySettings;
        var controls = _playerController is null
            ? "ESC exit"
            : $"{settings.MoveForward}/{settings.MoveLeft}/{settings.MoveBackward}/{settings.MoveRight} move | {settings.Jump} jump | ESC exit";
        _ui.TextFit(controls, new Vector2(38, 89), 684f, 1f, new Color(164, 190, 207));
        var actionStatus = _playSession?.HasReachedGoal == true
            && !_actionStatus.StartsWith("Reached goal:", StringComparison.Ordinal)
                ? "Goal reached. " + _actionStatus
            : _actionStatus;
        _ui.TextFit(actionStatus, new Vector2(38, 111), 684f, 1f, new Color(222, 205, 150));
        _ui.End();

        base.Draw(gameTime);
        EndHostFrame(hold: false, exit: Exit);
    }

    protected override void OnDisplayChanged() =>
        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio, far: 1000f);

    protected override void UnloadContent()
    {
        DisposeGameplayRuntime();
        foreach (var character in _characterAssets.Values)
            character.Dispose();
        _characterAssets.Clear();
        _characterInstances.Clear();
        foreach (var asset in _staticAssets.Values)
            asset.Dispose();
        _staticAssets.Clear();
        _staticEffect?.Dispose();
        _staticEffect = null;
        _maskedStaticEffect?.Dispose();
        _maskedStaticEffect = null;
        DisposeHost();
        base.UnloadContent();
    }

    private void InitializeGameplayRuntime()
    {
        var session = _playSession ?? throw new InvalidOperationException("The Play scene is not initialized.");
        var scene = session.RuntimeScene;
        var settings = scene.PlaySettings.ValidatedCopy();
        _playerObjectId = FindPlayCharacter(scene, settings);
        if (_playerObjectId is null)
            _actionStatus = "No playable character found. Add an animated character or actor to control the scene.";

        var world = new PhysicsWorld();
        SceneStaticColliderSet? colliders = null;
        PhysicsCharacterController? controller = null;
        try
        {
            world.AddStaticBox(new Vector3(0f, -0.5f, 0f), new Vector3(2000f, 1f, 2000f));
            var dynamicCharacterIds = scene.Objects
                .Where(item => item.CharacterSettings is not null
                    || item.WorldEntity?.Kind == WorldEntityKind.Actor)
                .Select(item => item.Id).ToHashSet();
            if (_playerObjectId is { } playerId) dynamicCharacterIds.Add(playerId);
            colliders = new SceneStaticColliderSet(scene, world, dynamicCharacterIds);

            if (_playerObjectId is { } objectId)
            {
                var centerOffset = CharacterCenterOffset(settings);
                var worldPosition = scene.GetWorldMatrix(objectId).Translation;
                controller = new PhysicsCharacterController(world,
                    worldPosition + new Vector3(0f, centerOffset, 0f), new PhysicsCharacterSettings
                    {
                        Radius = settings.CapsuleRadius,
                        CylinderLength = settings.CapsuleCylinderLength,
                        MoveSpeed = settings.MoveSpeed,
                        JumpSpeed = settings.JumpSpeed
                    });
                session.BindPhysicsCharacter(controller.PhysicsBodyId, objectId);
            }

            _physicsWorld = world;
            _sceneColliders = colliders;
            _physicsStepper = new PhysicsFixedStepper();
            _playerController = controller;
        }
        catch
        {
            if (controller is not null)
            {
                session.UnbindPhysicsCharacter(controller.PhysicsBodyId);
                controller.Dispose();
            }
            colliders?.Dispose();
            world.Dispose();
            throw;
        }
    }

    private Guid? FindPlayCharacter(SceneGraph scene, ScenePlaySettings settings)
    {
        bool IsPlayableCharacter(SceneObject item) => item.Enabled && item.TriggerAction is null
            && (item.CharacterSettings is not null
                || item.WorldEntity?.Kind == WorldEntityKind.Actor
                || _characterInstances.ContainsKey(item.Id));

        if (settings.PlayerObjectId is { } assignedId)
        {
            var assigned = scene.Find(assignedId)
                ?? throw new InvalidOperationException(
                    $"Saved Play player {assignedId} is missing. Choose a character in Play setup.");
            if (!IsPlayableCharacter(assigned))
                throw new InvalidOperationException(
                    $"Saved Play player '{assigned.Name}' ({assigned.Id}) is disabled or is not an animated character/actor.");
            return assigned.Id;
        }

        return scene.Objects.Where(IsPlayableCharacter).OrderBy(item => item.Id)
            .Select(item => (Guid?)item.Id).FirstOrDefault();
    }

    private void OnAuthoredActionExecuted(SceneAuthoredActionEvent action)
    {
        _actionStatus = action.Kind switch
        {
            SceneTriggerActionKind.Collect => $"Collected: {action.SceneObjectName}.",
            SceneTriggerActionKind.ReachGoal => $"Reached goal: {action.SceneObjectName}.",
            SceneTriggerActionKind.Open =>
                $"Door reached: {action.SceneObjectName}. This starter does not load world cells yet.",
            _ => $"Triggered: {action.SceneObjectName}."
        };
    }

    private void DisposeGameplayRuntime()
    {
        if (_playerController is { } controller)
        {
            _playSession?.UnbindPhysicsCharacter(controller.PhysicsBodyId);
            controller.Dispose();
            _playerController = null;
        }
        _sceneColliders?.Dispose();
        _sceneColliders = null;
        _physicsWorld?.Dispose();
        _physicsWorld = null;
        _physicsStepper = null;
        if (_playSession is { } session)
        {
            session.AuthoredActionExecuted -= OnAuthoredActionExecuted;
            session.Dispose();
            _playSession = null;
        }
    }

    private static float CharacterCenterOffset(ScenePlaySettings settings) =>
        settings.CapsuleRadius + settings.CapsuleCylinderLength * 0.5f;

    private static Vector3 ToOrbitCameraMovement(Vector2 localMovement, float cameraYaw)
    {
        if (localMovement.LengthSquared() > 1f) localMovement.Normalize();
        var rotation = Matrix.CreateRotationY(cameraYaw);
        var right = Vector3.Transform(Vector3.Right, rotation);
        var forward = Vector3.Transform(Vector3.Forward, rotation);
        var movement = right * localMovement.X + forward * localMovement.Y;
        movement.Y = 0f;
        return movement.LengthSquared() > 1f ? Vector3.Normalize(movement) : movement;
    }

    private static void SetObjectWorldPosition(SceneGraph scene, SceneObject item, Vector3 worldPosition)
    {
        if (item.ParentId is { } parentId)
        {
            var parentWorld = scene.GetWorldMatrix(parentId);
            var determinant = parentWorld.Determinant();
            if (!float.IsFinite(determinant) || MathF.Abs(determinant) < 1e-8f)
                throw new InvalidOperationException(
                    $"Cannot move character '{item.Name}' because its parent transform is not invertible.");
            worldPosition = Vector3.Transform(worldPosition, Matrix.Invert(parentWorld));
        }
        item.Transform.Position = worldPosition;
    }

    private void LoadSceneAssets()
    {
        foreach (var item in _sceneGraph.Objects)
        {
            var reference = GetMeshAssetReference(item);
            if (reference is null
                || _characterAssets.ContainsKey(reference.AssetId)
                || _staticAssets.ContainsKey(reference.AssetId)) continue;
            var assetPath = _project.ResolveContentPath(reference.SourcePath);
            if (!File.Exists(assetPath))
                throw new FileNotFoundException(
                    $"Scene object {item.Id} references missing GLB asset {reference.AssetId} at '{reference.SourcePath}'.",
                    assetPath);

            var model = ModelRoot.Load(assetPath);
            if (model.LogicalNodes.Any(node => node.Skin is not null))
            {
                var character = GltfSkinnedCharacterData.Import(model);
                var asset = new CharacterAsset(GraphicsDevice, character);
                _characterAssets.Add(reference.AssetId, asset);
            }
            else
            {
                var imported = GltfSceneImporter.Import(model);
                _staticAssets.Add(reference.AssetId, new StaticAsset(GraphicsDevice, imported));
            }
        }

        foreach (var item in _sceneGraph.Objects)
        {
            if (item.GltfAsset is { } reference && _characterAssets.TryGetValue(reference.AssetId, out var asset))
                _characterInstances.Add(item.Id, new CharacterInstance(item, asset));
        }
    }

    private static GltfAssetReference? GetMeshAssetReference(SceneObject item) =>
        item.GltfAsset ?? item.StaticMeshLod?.NearAsset;

    private static InputActionMap CreateInputActions()
    {
        var actions = new InputActionMap();
        actions.Bind(ExitActionName, Keys.Escape);
        return actions;
    }

    private static KeyboardState ControlSmokeKeyboard(int frame)
    {
        if (frame == 8) return new KeyboardState(Keys.Escape);
        if ((frame & 1) != 0) return new KeyboardState();
        return new KeyboardState(ControlSmokeMovementKeys[frame / 2]);
    }

    private void VerifyControlSmokeMovement(Vector3 previousWorldPosition, Vector3 worldPosition,
        Vector2 movement, float elapsedSeconds)
    {
        var actualDelta = worldPosition - previousWorldPosition;
        actualDelta.Y = 0f;
        var settings = _sceneGraph.PlaySettings;
        var expectedDelta = ToOrbitCameraMovement(movement, _camera.Yaw) * (settings.MoveSpeed * elapsedSeconds);
        var target = worldPosition + new Vector3(0f, settings.CameraTargetOffsetY, 0f);
        if (Vector3.Distance(actualDelta, expectedDelta) > MathF.Max(0.06f, expectedDelta.Length() * 0.35f)
            || Vector3.Dot(actualDelta, expectedDelta) <= 0f
            || Vector3.Distance(_camera.Target, target) > 0.001f)
            throw new InvalidOperationException(
                $"Control smoke failed at {_controlSmokeFrame}: expected movement {expectedDelta}, received {actualDelta}.");
        Console.WriteLine($"PASS control smoke: {ControlSmokeMovementKeys[_controlSmokeFrame / 2]} moved {actualDelta}.");
    }

    private Bounds3? GetFramingBounds()
    {
        Bounds3? combined = null;
        foreach (var item in _sceneGraph.Objects)
        {
            var world = _sceneGraph.GetWorldMatrix(item.Id);
            Bounds3? bounds = null;
            if (_characterInstances.TryGetValue(item.Id, out var character))
                bounds = character.Asset.FramingBounds.Transform(world);
            else if (GetMeshAssetReference(item) is { } reference
                     && _staticAssets.TryGetValue(reference.AssetId, out var staticAsset))
                bounds = staticAsset.GetWorldBounds(world);
            if (bounds is not { } objectBounds) continue;
            combined = combined is { } current ? current.Encapsulate(objectBounds) : objectBounds;
        }
        return combined;
    }

    private sealed class CharacterInstance
    {
        public CharacterInstance(SceneObject item, CharacterAsset asset)
        {
            Asset = asset;
            Pose = asset.Character.CreatePose();
            var settings = item.CharacterSettings;
            if (settings is { CrossfadeClipName: not null })
                throw new NotSupportedException($"Scene object {item.Id} requests crossfade clip '{settings.CrossfadeClipName}', which the minimal consumer does not support.");
            if (settings is { Attachments.Count: > 0 })
                throw new NotSupportedException($"Scene object {item.Id} has bone attachments, which the minimal consumer does not support.");

            var clip = settings?.ClipName is { } clipName
                ? asset.Character.Animations.FirstOrDefault(value =>
                    string.Equals(value.Name, clipName, StringComparison.OrdinalIgnoreCase))
                : asset.Character.Animations.FirstOrDefault();
            if (settings?.ClipName is { } missingClip && clip is null)
                throw new InvalidDataException($"Scene object {item.Id} requests missing clip '{missingClip}' from asset {item.GltfAsset!.AssetId}.");

            if (clip is null) return;
            Playback = new GltfAnimationPlayback(clip, settings?.Loop ?? true, settings?.Speed ?? 1f);
            Playback.Seek(settings?.Time ?? 0f);
            if (settings is null || settings.IsPlaying) Playback.Play();
            clip.Evaluate(Pose, Playback.Time);
        }

        public CharacterAsset Asset { get; }
        public GltfSkinPose Pose { get; }
        private GltfAnimationPlayback? Playback { get; }

        public void Advance(float elapsedSeconds)
        {
            if (Playback is null) return;
            Playback.Advance(elapsedSeconds);
            Playback.Clip.Evaluate(Pose, Playback.Time);
        }
    }

    private sealed class CharacterAsset : IDisposable
    {
        private readonly Dictionary<GltfSkinnedMeshData, SkinnedMeshGpuBuffer> _buffers = new();
        private readonly Dictionary<int, Texture2D> _textures = new();
        private readonly Texture2D _whiteTexture;

        public CharacterAsset(GraphicsDevice device, GltfSkinnedCharacterData character)
        {
            Character = character;
            Effect = new SkinnedEffect(device);
            _whiteTexture = new Texture2D(device, 1, 1);
            _whiteTexture.SetData(new[] { Color.White });
            Effect.EnableDefaultLighting();
            Effect.AmbientLightColor = new Vector3(0.54f, 0.57f, 0.62f);
            try
            {
                SkinnedEffectCompatibility.Validate(device.GraphicsProfile, character.Skin.JointNodeIndices.Count);
                foreach (var primitive in character.Primitives)
                {
                    if (!_buffers.ContainsKey(primitive.Mesh))
                        _buffers.Add(primitive.Mesh, new SkinnedMeshGpuBuffer(
                            device, primitive.Mesh, character.Skin));
                    if (primitive.Material.HasBaseColorImage
                        && primitive.Material.BaseColorImageIndex is { } imageIndex
                        && !_textures.ContainsKey(imageIndex))
                    {
                        using var imageStream = new MemoryStream(
                            primitive.Material.BaseColorImage.ToArray(), writable: false);
                        _textures.Add(imageIndex, Texture2D.FromStream(device, imageStream));
                    }
                }

                Bounds3? bounds = null;
                foreach (var clip in character.Animations)
                {
                    var clipBounds = GltfAnimationBounds.SampleClip(character, clip);
                    bounds = bounds is { } current ? current.Encapsulate(clipBounds) : clipBounds;
                }
                FramingBounds = bounds ?? character.LocalBounds.Transform(character.Skin.MeshNodeRestWorldMatrix);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public GltfSkinnedCharacterData Character { get; }
        public SkinnedEffect Effect { get; }
        public Bounds3 FramingBounds { get; }

        public void Draw(GraphicsDevice device, GltfSkinPose pose, Matrix instanceWorld, Matrix view, Matrix projection)
        {
            foreach (var primitive in Character.Primitives)
            {
                var texture = primitive.Material.BaseColorImageIndex is { } imageIndex
                    ? _textures[imageIndex]
                    : _whiteTexture;
                var factor = primitive.Material.BaseColorFactor;
                Effect.DiffuseColor = new Vector3(factor.X, factor.Y, factor.Z);
                Effect.Alpha = factor.W;
                device.RasterizerState = primitive.Material.DoubleSided
                    ? RasterizerState.CullNone
                    : RasterizerState.CullCounterClockwise;
                var world = pose.MeshNodeWorldMatrix * instanceWorld;
                _buffers[primitive.Mesh].Draw(Effect, pose, world, view, projection, texture);
            }
            device.RasterizerState = RasterizerState.CullCounterClockwise;
        }

        public void Dispose()
        {
            foreach (var buffer in _buffers.Values) buffer.Dispose();
            foreach (var texture in _textures.Values) texture.Dispose();
            _whiteTexture.Dispose();
            _buffers.Clear();
            _textures.Clear();
            Effect.Dispose();
        }
    }

    private sealed class StaticAsset : IDisposable
    {
        private readonly Dictionary<ImportedGltfPrimitive, StaticPrimitive> _primitives = new();
        private readonly Dictionary<int, Texture2D> _textures = new();
        private bool _disposed;

        public StaticAsset(GraphicsDevice device, ImportedGltfScene imported)
        {
            Imported = imported;
            try
            {
                foreach (var primitive in imported.MeshesByNodeId.Values
                             .SelectMany(value => value).Distinct())
                {
                    Texture2D? texture = null;
                    if (primitive.Material.HasBaseColorImage)
                    {
                        var imageIndex = primitive.Material.BaseColorImageIndex
                            ?? throw new InvalidDataException(
                                $"Material '{primitive.Material.Name}' has image data without an image ID.");
                        if (!_textures.TryGetValue(imageIndex, out texture))
                        {
                            using var imageStream = new MemoryStream(
                                primitive.Material.BaseColorImage.ToArray(), writable: false);
                            texture = Texture2D.FromStream(device, imageStream);
                            _textures.Add(imageIndex, texture);
                        }
                    }

                    _primitives.Add(primitive, new StaticPrimitive(
                        new StaticMeshGpuBuffer(device, primitive.Mesh), primitive.Material, texture));
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public ImportedGltfScene Imported { get; }

        public Bounds3? GetWorldBounds(Matrix instanceWorld)
        {
            Bounds3? combined = null;
            foreach (var (nodeId, primitives) in Imported.MeshesByNodeId)
            {
                var nodeWorld = Imported.Scene.GetWorldMatrix(nodeId) * instanceWorld;
                foreach (var primitive in primitives)
                {
                    if (primitive.Mesh.LocalBounds is not { } localBounds) continue;
                    var worldBounds = localBounds.Transform(nodeWorld);
                    combined = combined is { } current
                        ? current.Encapsulate(worldBounds)
                        : worldBounds;
                }
            }
            return combined;
        }

        public void Draw(GraphicsDevice device, BasicEffect opaqueEffect, AlphaTestEffect maskedEffect,
            Matrix instanceWorld, Matrix view, Matrix projection)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                foreach (var (nodeId, primitives) in Imported.MeshesByNodeId)
                {
                    var nodeWorld = Imported.Scene.GetWorldMatrix(nodeId) * instanceWorld;
                    foreach (var primitive in primitives)
                    {
                        var runtime = _primitives[primitive];
                        var material = runtime.Material;
                        var factor = material.BaseColorFactor;
                        device.RasterizerState = material.DoubleSided
                            ? RasterizerState.CullNone
                            : RasterizerState.CullCounterClockwise;

                        if (material.AlphaMode == GltfAlphaMode.Mask && runtime.Texture is not null)
                        {
                            maskedEffect.World = nodeWorld;
                            maskedEffect.View = view;
                            maskedEffect.Projection = projection;
                            maskedEffect.DiffuseColor = new Vector3(factor.X, factor.Y, factor.Z);
                            maskedEffect.Alpha = factor.W;
                            maskedEffect.Texture = runtime.Texture;
                            maskedEffect.AlphaFunction = CompareFunction.GreaterEqual;
                            maskedEffect.ReferenceAlpha = (byte)Math.Clamp(
                                (int)MathF.Round(material.AlphaCutoff * 255f), 0, 255);
                            runtime.Buffer.Draw(maskedEffect);
                            continue;
                        }

                        if (material.AlphaMode == GltfAlphaMode.Mask
                            && factor.W < material.AlphaCutoff)
                            continue;

                        opaqueEffect.DiffuseColor = new Vector3(factor.X, factor.Y, factor.Z);
                        opaqueEffect.Alpha = factor.W;
                        opaqueEffect.Texture = runtime.Texture;
                        opaqueEffect.TextureEnabled = runtime.Texture is not null;
                        runtime.Buffer.Draw(opaqueEffect, nodeWorld, view, projection);
                    }
                }
            }
            finally
            {
                device.RasterizerState = RasterizerState.CullCounterClockwise;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var primitive in _primitives.Values)
                primitive.Buffer.Dispose();
            foreach (var texture in _textures.Values)
                texture.Dispose();
            _primitives.Clear();
            _textures.Clear();
        }

        private sealed record StaticPrimitive(
            StaticMeshGpuBuffer Buffer, GltfMaterialData Material, Texture2D? Texture);
    }
}
