using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ember;
using Ember.Assets;
using Ember.Input;
using Ember.Project;
using Ember.Render;
using Ember.Scene;
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
        var packageDestination = GetOption(args, "--package-to");
        if (packageDestination is not null)
        {
            var projectPath = GetOption(args, "--project")
                ?? Path.Combine(AppContext.BaseDirectory, EngineProjectFile.DefaultFileName);
            var package = EngineProjectPackage.Create(projectPath, packageDestination);
            Console.WriteLine($"Packaged project to '{package.DirectoryPath}' with startup scene '{package.StartupScenePath}' and {package.GlbAssetCount} GLB asset(s).");
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
    private readonly InputActionMap _actions = CreateInputActions();
    private EngineProjectFile _project = null!;
    private SceneGraph _sceneGraph = null!;
    private SceneRenderer _scene = null!;
    private int _controlSmokeFrame;

    public MinimalGame(string[] args)
        : base(args, logicalWidth: 1280, logicalHeight: 720, title: "Minimal Ember Game")
    {
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        _controlSmoke = args.Contains("--smoke-controls", StringComparer.OrdinalIgnoreCase);
        _projectPath = Path.GetFullPath(ParseOption(args, "--project")
            ?? ResolveDefaultProjectPath());
    }

    private static string ResolveDefaultProjectPath()
    {
        var distributionProject = Path.Combine(AppContext.BaseDirectory, "Project", EngineProjectFile.DefaultFileName);
        return File.Exists(distributionProject)
            ? distributionProject
            : Path.Combine(AppContext.BaseDirectory, EngineProjectFile.DefaultFileName);
    }

    protected override void LoadContent()
    {
        _project = EngineProjectFile.Load(_projectPath);
        _sceneGraph = SceneFile.Load(_project.ResolveStartupScenePath());
        LoadCharacterInstances();
        if (_controlSmoke && _characterInstances.Count == 0)
            throw new InvalidOperationException("--smoke-controls requires a startup scene with a skinned character.");
        _scene = new SceneRenderer(GraphicsDevice);
        AttachCanvas();
        AttachScene(_faults);
        _ui.Resize(GraphicsDevice.Viewport, _uiScalePreference);

        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio, far: 1000f);
        _camera.MaxDistance = 500f;
        var characterBounds = GetCharacterBounds();
        if (characterBounds is { } bounds)
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
        var movement = new Vector3(movement2D.X, 0f, -movement2D.Y);
        if (movement.LengthSquared() > 0f && _characterInstances.Count > 0)
        {
            movement.Normalize();
            var character = _sceneGraph.Objects.First(item => _characterInstances.ContainsKey(item.Id));
            var previousWorldPosition = Vector3.Transform(Vector3.Zero, _sceneGraph.GetWorldMatrix(character.Id));
            var parentWorld = character.ParentId is { } parentId
                ? _sceneGraph.GetWorldMatrix(parentId)
                : Matrix.Identity;
            var parentDeterminant = parentWorld.Determinant();
            if (!float.IsFinite(parentDeterminant) || parentDeterminant == 0f)
                throw new InvalidOperationException(
                    $"Cannot move character {character.Id} because its parent transform is not invertible.");

            var localMovement = Vector3.TransformNormal(movement, Matrix.Invert(parentWorld));
            character.Transform.Position += localMovement * (4f * elapsed);
            var worldPosition = Vector3.Transform(Vector3.Zero, _sceneGraph.GetWorldMatrix(character.Id));
            _camera.Reset(worldPosition, _camera.Distance, _camera.Yaw, _camera.Pitch);
            if (_controlSmoke)
                VerifyControlSmokeMovement(previousWorldPosition, worldPosition, movement, elapsed);
        }
        else if (_controlSmoke && (_controlSmokeFrame & 1) == 0)
        {
            throw new InvalidOperationException("Control smoke failed: a movement key did not move the character.");
        }
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
            if (_characterInstances.TryGetValue(item.Id, out var character))
                character.Asset.Draw(GraphicsDevice, character.Pose,
                    _sceneGraph.GetWorldMatrix(item.Id), _camera.View, _camera.Projection);
            else
                _scene.DrawCube(_sceneGraph.GetWorldMatrix(item.Id), new Color(173, 139, 88));
        }

        _ui.Begin();
        _ui.Panel(new Rectangle(20, 20, 620, 104), new Color(12, 16, 24, 220), new Color(94, 120, 148));
        _ui.Text("MINIMAL EMBER GAME", new Vector2(38, 34), 19, Color.White);
        _ui.TextFit($"Startup scene: {_project.StartupScenePath} | {_sceneGraph.Objects.Count} objects",
            new Vector2(38, 65), 584f, 1f, new Color(197, 207, 220));
        _ui.Text("WASD move character | ESC exit", new Vector2(38, 89), 1f, new Color(164, 190, 207));
        _ui.End();

        base.Draw(gameTime);
        EndHostFrame(hold: false, exit: Exit);
    }

    protected override void OnDisplayChanged() =>
        _camera.SetProjection(GraphicsDevice.Viewport.AspectRatio, far: 1000f);

    protected override void UnloadContent()
    {
        foreach (var character in _characterAssets.Values)
            character.Dispose();
        _characterAssets.Clear();
        _characterInstances.Clear();
        DisposeHost();
        base.UnloadContent();
    }

    private void LoadCharacterInstances()
    {
        foreach (var item in _sceneGraph.Objects)
        {
            if (item.GltfAsset is not { } reference || _characterAssets.ContainsKey(reference.AssetId)) continue;
            var assetPath = _project.ResolveContentPath(reference.SourcePath);
            if (!File.Exists(assetPath))
                throw new FileNotFoundException(
                    $"Scene object {item.Id} references missing GLB asset {reference.AssetId} at '{reference.SourcePath}'.",
                    assetPath);

            var model = ModelRoot.Load(assetPath);
            if (!model.LogicalNodes.Any(node => node.Skin is not null)) continue;

            var character = GltfSkinnedCharacterData.Import(model);
            var asset = new CharacterAsset(GraphicsDevice, character);
            _characterAssets.Add(reference.AssetId, asset);
        }

        foreach (var item in _sceneGraph.Objects)
        {
            if (item.GltfAsset is { } reference && _characterAssets.TryGetValue(reference.AssetId, out var asset))
                _characterInstances.Add(item.Id, new CharacterInstance(item, asset));
        }
    }

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
        Vector3 movement, float elapsedSeconds)
    {
        var actualDelta = worldPosition - previousWorldPosition;
        var expectedDelta = movement * (4f * elapsedSeconds);
        if (Vector3.Distance(actualDelta, expectedDelta) > 0.001f
            || Vector3.Distance(_camera.Target, worldPosition) > 0.001f)
            throw new InvalidOperationException(
                $"Control smoke failed at {_controlSmokeFrame}: expected movement {expectedDelta}, received {actualDelta}.");
        Console.WriteLine($"PASS control smoke: {ControlSmokeMovementKeys[_controlSmokeFrame / 2]} moved {actualDelta}.");
    }

    private Bounds3? GetCharacterBounds()
    {
        Bounds3? combined = null;
        foreach (var item in _sceneGraph.Objects)
        {
            if (!_characterInstances.TryGetValue(item.Id, out var instance)) continue;
            var bounds = instance.Asset.FramingBounds.Transform(_sceneGraph.GetWorldMatrix(item.Id));
            combined = combined is { } current ? current.Encapsulate(bounds) : bounds;
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
                            device, primitive.Mesh, character.Skin.JointNodeIndices.Count));
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
}
