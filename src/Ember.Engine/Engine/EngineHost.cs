using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace Ember;

/// <summary>
/// Devices, timestep, fonts, the canvas attach, letterboxing and capture framing.
///
/// **This is the piece a second game subclasses instead of <c>Game1</c>.** It knows nothing
/// about mines, quests or Northwatch. Game1 adds Ratna Bay on top: screens, the session, the
/// console's reach into this game.
/// </summary>
public abstract class EngineHost : Game
{
    /// <summary>
    /// Longest step any system is given. Without this a stall, a dragged window or a
    /// breakpoint resumes with one enormous frame and the player arrives somewhere else.
    /// </summary>
    protected const float MaxFrameSeconds = 0.1f;

    protected readonly GraphicsDeviceManager _graphics;
    protected readonly UiCanvas _ui;
    protected readonly InputRouter _input = new();
    protected readonly CaptureHost _capture;

    protected FontSystem _fontSystem = null!;
    protected FontSystem _headingFontSystem = null!;
    protected SpriteType? _spriteType;
    protected Texture2D _white = null!;

    protected int LogicalWidth => _ui.LogicalWidth;
    protected int LogicalHeight => _ui.LogicalHeight;

    /// <summary>
    /// Logical-to-screen scale. Text is rasterized at this many device pixels per logical
    /// pixel so glyphs land 1:1 on the display instead of being resampled.
    /// </summary>
    protected float _uiScalePreference = 1f;

    protected bool _borderlessFullscreen = true;

    /// <summary>
    /// Wall-clock frame rate. Deliberately not derived from GameTime: under a fixed
    /// timestep ElapsedGameTime is always 1/60 no matter how slowly the game is really
    /// running, which is exactly the failure this number exists to expose.
    /// </summary>
    protected float _framesPerSecond;
    private int _fpsFrames;
    private readonly Stopwatch _fpsClock = Stopwatch.StartNew();

    private readonly bool _reportPerf;
    private bool _perfStarted;
    private int _perfFrames;
    private double _perfSumMs;
    private double _perfMinMs = double.MaxValue;
    private double _perfMaxMs;
    private readonly Stopwatch _frameClock = Stopwatch.StartNew();

    protected EngineHost(string[] args, int logicalWidth, int logicalHeight, string title)
    {
        _ui = new UiCanvas(logicalWidth, logicalHeight);
        _capture = new CaptureHost(
            ParseOption(args, "--screenshot"),
            ParseOption(args, "--cover"),
            int.TryParse(ParseOption(args, "--warmup"), out var warmup) ? warmup : null);
        _reportPerf = HasArgument(args, "--perf");

        var displayMode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = Math.Max(displayMode.Width, LogicalWidth),
            PreferredBackBufferHeight = Math.Max(displayMode.Height, LogicalHeight),
            SynchronizeWithVerticalRetrace = true,
            IsFullScreen = true,
            HardwareModeSwitch = false
        };

        Content.RootDirectory = "Content";
        IsMouseVisible = false;

        // MonoGame defaults to a fixed timestep, where ElapsedGameTime is always 1/60 no
        // matter how long the frame really took. At 43 fps that advanced game time at 72%
        // of real time, so walking was a quarter slower than its own speed constant said.
        // A variable timestep makes elapsed time mean elapsed time.
        IsFixedTimeStep = false;
        Window.Title = title;
        Window.IsBorderless = true;

        if (_capture.ApplyWindow(_graphics, Window, LogicalWidth, LogicalHeight))
            _borderlessFullscreen = false;

        if (HasArgument(args, "--windowed"))
        {
            _borderlessFullscreen = false;
            _graphics.PreferredBackBufferWidth = LogicalWidth;
            _graphics.PreferredBackBufferHeight = LogicalHeight;
            _graphics.IsFullScreen = false;
            Window.IsBorderless = false;
        }
    }

    protected static float RealSeconds(GameTime gameTime) =>
        MathF.Min((float)gameTime.ElapsedGameTime.TotalSeconds, MaxFrameSeconds);

    /// <summary>The pointer in logical canvas space, so UI hit tests match what is drawn.</summary>
    protected Vector2 LogicalMouse(MouseState mouse) =>
        _ui.PointerFromDevice(mouse.X, mouse.Y, GraphicsDevice.Viewport);

    protected void CentreMouse()
    {
        var viewport = GraphicsDevice.Viewport;
        Mouse.SetPosition(viewport.Width / 2, viewport.Height / 2);
    }

    /// <summary>Billboards, for anything drawn as a camera-facing quad.</summary>
    protected BillboardRenderer Billboards { get; private set; } = null!;

    /// <summary>The lit effect untextured world geometry is drawn with.</summary>
    protected BasicEffect LitEffect { get; private set; } = null!;

    /// <summary>The ambient bed, or null when no audio device would take it.</summary>
    protected AmbientAudio? Ambience { get; private set; }

    /// <summary>
    /// Synthesised effects, or null when the device refused them.
    ///
    /// Named Sounds rather than Sfx because Sfx is already the enum of effect ids, and a
    /// property with that name shadows it at every call site: Sfx.Play(Sfx.Coin) stops
    /// compiling the moment the property exists.
    /// </summary>
    protected SoundBank? Sounds { get; private set; }

    /// <summary>
    /// Everything a first-person scene needs before a game loads a single asset of its own.
    ///
    /// Billboards, a lit effect, and the two audio banks — none of which know what game this
    /// is. A new game calls this and then loads its models; it does not re-derive which order
    /// BasicEffect wants its lighting set in.
    ///
    /// Failures are collected rather than thrown. A machine with no audio device should still
    /// get a playable game and a line saying what is missing, which is why every one of these
    /// returns a message instead of an exception.
    /// </summary>
    protected void AttachScene(ICollection<string> faults)
    {
        Billboards = new BillboardRenderer(GraphicsDevice);

        if (!AmbientAudio.TryStart(out var ambience, out var ambientFault)
            && !string.IsNullOrWhiteSpace(ambientFault))
            faults.Add(ambientFault);

        Ambience = ambience;

        Sounds = SoundBank.Create(out var sfxFault);
        if (!string.IsNullOrWhiteSpace(sfxFault)) faults.Add(sfxFault);

        LitEffect = new BasicEffect(GraphicsDevice)
        {
            VertexColorEnabled = false,
            TextureEnabled = false,
            LightingEnabled = true,
            PreferPerPixelLighting = true
        };

        // EnableDefaultLighting overwrites the ambient colour and all three lights, so it has
        // to run before they are set. It was once called after, which made every ambient value
        // below it dead code and left the scene near-black — a mistake worth keeping the order
        // in one place to prevent.
        LitEffect.EnableDefaultLighting();
        LitEffect.AmbientLightColor = new Vector3(0.54f, 0.57f, 0.62f);
        LitEffect.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.25f));
        LitEffect.DirectionalLight0.DiffuseColor = new Vector3(1f, 0.83f, 0.64f);
        LitEffect.DirectionalLight0.SpecularColor = new Vector3(0.28f);
    }

    /// <summary>Hand the canvas its device resources. Call from LoadContent after fonts exist on disk.</summary>
    protected void AttachCanvas(string bodyFontPath, string headingFontPath)
    {
        var spriteBatch = new SpriteBatch(GraphicsDevice);

        _fontSystem = new FontSystem { UseKernings = true };
        _fontSystem.AddFont(File.ReadAllBytes(bodyFontPath));

        _headingFontSystem = new FontSystem { UseKernings = true };
        _headingFontSystem.AddFont(File.ReadAllBytes(headingFontPath));

        _white = new Texture2D(GraphicsDevice, 1, 1);
        _white.SetData(new[] { Color.White });

        _ui.Attach(spriteBatch, _white, _fontSystem, _headingFontSystem);
    }

    /// <summary>8×8 glyph sprites, nearest-neighbour. No TrueType face.</summary>
    protected void AttachCanvas()
    {
        var spriteBatch = new SpriteBatch(GraphicsDevice);
        _white = new Texture2D(GraphicsDevice, 1, 1);
        _white.SetData(new[] { Color.White });
        _spriteType = SpriteType.Bake(GraphicsDevice);
        _ui.Attach(spriteBatch, _white, _spriteType);
    }

    protected void BeginHostFrame()
    {
        _capture.BeginFrame(GraphicsDevice);
        NotePerfFrame();

        _fpsFrames++;
        var elapsed = _fpsClock.Elapsed.TotalSeconds;
        if (elapsed < 0.5) return;

        _framesPerSecond = (float)(_fpsFrames / elapsed);
        _fpsFrames = 0;
        _fpsClock.Restart();
    }

    protected void EndHostFrame(bool hold, Action exit)
    {
        _capture.EndFrame(GraphicsDevice, hold, exit);
    }

    protected void SetBorderlessFullscreen(bool enabled)
    {
        _borderlessFullscreen = enabled;

        if (enabled)
        {
            var displayMode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            _graphics.PreferredBackBufferWidth = Math.Max(displayMode.Width, LogicalWidth);
            _graphics.PreferredBackBufferHeight = Math.Max(displayMode.Height, LogicalHeight);
            _graphics.IsFullScreen = true;
            _graphics.HardwareModeSwitch = false;
            Window.IsBorderless = true;
        }
        else
        {
            _graphics.PreferredBackBufferWidth = LogicalWidth;
            _graphics.PreferredBackBufferHeight = LogicalHeight;
            _graphics.IsFullScreen = false;
            Window.IsBorderless = false;
        }

        _graphics.ApplyChanges();
        _ui.Resize(GraphicsDevice.Viewport, _uiScalePreference);
        OnDisplayChanged();
    }

    /// <summary>Projection, cameras, anything that must follow a resize. Empty on the host.</summary>
    protected virtual void OnDisplayChanged()
    {
    }

    protected void DisposeHost()
    {
        WritePerfSummary();
        _capture.Dispose();
        _fontSystem?.Dispose();
        _headingFontSystem?.Dispose();
        _spriteType?.Dispose();
        _white?.Dispose();
    }

    protected static bool HasArgument(string[] args, string argument)
    {
        foreach (var value in args)
        {
            if (string.Equals(value, argument, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Reads `--name value` from the command line.</summary>
    protected static string? ParseOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];

        return null;
    }

    private void NotePerfFrame()
    {
        if (!_reportPerf) return;

        var ms = _frameClock.Elapsed.TotalMilliseconds;
        _frameClock.Restart();
        if (!_perfStarted)
        {
            _perfStarted = true;
            return;
        }

        _perfFrames++;
        _perfSumMs += ms;
        if (ms < _perfMinMs) _perfMinMs = ms;
        if (ms > _perfMaxMs) _perfMaxMs = ms;
    }

    private void WritePerfSummary()
    {
        if (!_reportPerf || _perfFrames <= 0) return;

        var avg = _perfSumMs / _perfFrames;
        var fps = avg > 0 ? 1000.0 / avg : 0;
        Console.WriteLine(
            $"perf: {_perfFrames} frames, avg {avg:0.00}ms ({fps:0} fps), min {_perfMinMs:0.00}, max {_perfMaxMs:0.00}");
    }
}
