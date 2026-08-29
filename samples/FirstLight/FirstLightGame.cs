using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Ember;
using Ember.Input;
using Ember.Render;
using System;
using System.Collections.Generic;
using System.IO;

namespace FirstLight;

/// <summary>
/// A room, a lamp, and someone standing in it.
///
/// The whole of a game built on <see cref="EngineHost"/>. It walks, it looks, it stops at
/// walls, it draws a line of text, and it can photograph itself. That is the engine's entire
/// contract with a game, exercised end to end — read this before writing your own, and copy
/// it when you do.
///
/// It also earns its keep as a test. It is in the solution, so the day a change to the engine
/// stops a game compiling against it, this goes red — which is louder than a document quietly
/// going stale.
/// </summary>
public sealed class FirstLightGame : EngineHost
{
    private const float Half = 8f;
    private const float WallTop = 5f;
    private const float PillarHalf = 1.2f;

    // Built in LoadContent, not in the constructor: Game.GraphicsDevice is still null
    // there, and a SceneRenderer built on a null device fails a hundred frames later
    // inside a texture cache rather than at the line that was wrong.
    private SceneRenderer _scene = null!;
    private readonly FirstPersonView _view = new();
    private readonly List<string> _faults = new();
    private readonly List<PointLight> _lights = new();
    private readonly List<(Vector3 Min, Vector3 Max, Color Colour, string Material)> _room = new();

    public FirstLightGame(string[] args)
        : base(args, logicalWidth: 1280, logicalHeight: 720, title: "First Light")
    {
        BuildRoom();
    }

    /// <summary>Six slabs and a pillar, in the engine's own material names.</summary>
    private void BuildRoom()
    {
        var stone = new Color(150, 142, 130);
        var timber = new Color(146, 108, 68);

        _room.Add((new Vector3(-Half, -0.4f, -Half), new Vector3(Half, 0f, Half), stone, "stone"));
        _room.Add((new Vector3(-Half, WallTop, -Half), new Vector3(Half, WallTop + 0.4f, Half), stone, "stone"));

        _room.Add((new Vector3(-Half, 0f, -Half), new Vector3(Half, WallTop, -Half + 0.4f), stone, "stone"));
        _room.Add((new Vector3(-Half, 0f, Half - 0.4f), new Vector3(Half, WallTop, Half), stone, "stone"));
        _room.Add((new Vector3(-Half, 0f, -Half), new Vector3(-Half + 0.4f, WallTop, Half), stone, "stone"));
        _room.Add((new Vector3(Half - 0.4f, 0f, -Half), new Vector3(Half, WallTop, Half), stone, "stone"));

        // Something to walk around, so the collision callback has work to do.
        _room.Add((new Vector3(-PillarHalf, 0f, -PillarHalf),
            new Vector3(PillarHalf, 2.6f, PillarHalf), timber, "timber"));

        _lights.Add(new PointLight(new Vector3(0f, 3.8f, 0f), new Vector3(1f, 0.86f, 0.62f) * 2.2f, 24f));
    }

    protected override void LoadContent()
    {
        _scene = new SceneRenderer(GraphicsDevice);

        var fonts = Path.Combine(AppContext.BaseDirectory, "Content", "Fonts");
        AttachCanvas(
            Path.Combine(fonts, "NotoSans", "NotoSans-wght.ttf"),
            Path.Combine(fonts, "Cinzel", "Cinzel-wght.ttf"));

        AttachScene(_faults);
        _ui.Resize(GraphicsDevice.Viewport, _uiScalePreference);

        foreach (var fault in _faults) Console.WriteLine($"first light: {fault}");

        // No cave shader is loaded, so SceneRenderer falls back to the BasicEffect path and
        // SetCaveAmbience would be a call that reports nothing and does nothing. Left out
        // rather than left in: a switch with no wire behind it is the single most expensive
        // shape of bug this project has shipped.
        _view.Reset(new Vector3(0f, 1.7f, 5.5f), yaw: 0f, pitch: -0.05f, standingEyeY: 1.7f);
        _view.SetProjection(GraphicsDevice.Viewport.AspectRatio);
    }

    protected override void Update(GameTime gameTime)
    {
        BeginHostFrame();

        _input.Sample();
        var keyboard = _input.CurrentKeyboard;
        if (_input.Pressed(keyboard, Keys.Escape)) Exit();

        var walk = new WalkInput(
            Forward: keyboard.IsKeyDown(Keys.W),
            Back: keyboard.IsKeyDown(Keys.S),
            Left: keyboard.IsKeyDown(Keys.A),
            Right: keyboard.IsKeyDown(Keys.D),
            Sprint: keyboard.IsKeyDown(Keys.LeftShift),
            Jump: _input.Pressed(keyboard, Keys.Space),
            HeldYaw: (keyboard.IsKeyDown(Keys.Right) ? 1f : 0f) - (keyboard.IsKeyDown(Keys.Left) ? 1f : 0f),
            HeldPitch: (keyboard.IsKeyDown(Keys.Down) ? 1f : 0f) - (keyboard.IsKeyDown(Keys.Up) ? 1f : 0f));

        _view.Step(RealSeconds(gameTime), walk, Vector2.Zero, StopAtWalls);
        _view.RebuildView();

        _input.Commit();
        base.Update(gameTime);
    }

    /// <summary>
    /// Collision as a callback, which is how the engine asks about a world it cannot see.
    ///
    /// Crude on purpose — a clamp to the room rather than a swept box — because the point is
    /// that the engine never needs to know how a game answers this, only that it does.
    /// </summary>
    private Vector3 StopAtWalls(Vector3 origin, Vector3 delta, float radius)
    {
        var wanted = origin + delta;

        var inThePillar = MathF.Abs(wanted.X) < PillarHalf + radius
            && MathF.Abs(wanted.Z) < PillarHalf + radius;
        if (inThePillar) return origin;

        var limit = Half - 0.4f - radius;
        return new Vector3(
            Math.Clamp(wanted.X, -limit, limit),
            wanted.Y,
            Math.Clamp(wanted.Z, -limit, limit));
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(10, 12, 16));
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;
        GraphicsDevice.RasterizerState = RasterizerState.CullCounterClockwise;

        _scene.Begin(LitEffect, _view.View, _view.Projection, _view.Position,
            _view.Yaw, StoneTextures.StonePalette.Sandstone, _lights);

        foreach (var (min, max, colour, material) in _room)
            _scene.DrawWorldBox(min, max, colour, material);

        _ui.Begin();
        _ui.Panel(new Rectangle(24, 24, 520, 96), new Color(6, 12, 19, 226), new Color(120, 150, 160));
        _ui.Text("FIRST LIGHT", new Vector2(44, 44), 22, Color.White);
        _ui.Text("A game on the Ember engine. WASD walks, arrows look, Esc leaves.",
            new Vector2(44, 82), 15, new Color(186, 200, 204));
        _ui.End();

        base.Draw(gameTime);
        EndHostFrame(hold: false, exit: Exit);
    }

    protected override void OnDisplayChanged() =>
        _view.SetProjection(GraphicsDevice.Viewport.AspectRatio);

    protected override void UnloadContent()
    {
        DisposeHost();
        base.UnloadContent();
    }
}
