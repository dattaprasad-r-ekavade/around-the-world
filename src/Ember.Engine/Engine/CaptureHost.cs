using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.IO;

namespace Ember;

/// <summary>
/// Screenshot warmup, an optional cover-sized render target, and PNG write.
///
/// **This is the piece a different game would reuse unchanged.** What is drawn is the game's;
/// this type only decides when a frame is settled enough to save, where it is drawn (window
/// or an offscreen cover), and how it becomes a file. A script that is still running can hold
/// the capture — that hold is a bool, so this type does not know about consoles.
/// </summary>
public sealed class CaptureHost : IDisposable
{
    /// <summary>itch.io's cover shape, at twice its stated size so the type stays sharp.</summary>
    public const int CoverWidth = 1260;

    public const int CoverHeight = 1000;

    private RenderTarget2D? _coverTarget;
    private int _framesDrawn;
    private string? _pending;

    public CaptureHost(string? screenshotPath, string? coverPath, int? warmupFrames)
    {
        if (coverPath is not null)
        {
            OutputPath = coverPath;
            CoverMode = true;
            // Cover composition settles later than a HUD shot: fonts, the mine, the ladder.
            WarmupFrames = Math.Max(warmupFrames ?? 4, 30);
        }
        else
        {
            OutputPath = screenshotPath;
            WarmupFrames = warmupFrames ?? 4;
        }
    }

    /// <summary>Where --screenshot / --cover will write. Null when this run is interactive.</summary>
    public string? OutputPath { get; }

    /// <summary>
    /// Set by --cover: render the store cover instead of the HUD, at itch.io's aspect.
    ///
    /// Drawn by the game rather than assembled in an image editor, so the cover cannot drift
    /// away from what the game actually looks like. This flag only changes the target size;
    /// the composition is the game's.
    /// </summary>
    public bool CoverMode { get; }

    /// <summary>Frames to render before a --screenshot captures. Raise it to measure the rate.</summary>
    public int WarmupFrames { get; }

    public bool IsCapturing => OutputPath is not null;

    /// <summary>
    /// Windowed, no vsync, exact logical size. A capture that waits on the monitor cannot be
    /// compared frame against frame.
    /// </summary>
    public bool ApplyWindow(GraphicsDeviceManager graphics, GameWindow window,
        int logicalWidth, int logicalHeight)
    {
        if (!IsCapturing) return false;

        graphics.PreferredBackBufferWidth = logicalWidth;
        graphics.PreferredBackBufferHeight = logicalHeight;
        graphics.IsFullScreen = false;
        graphics.SynchronizeWithVerticalRetrace = false;
        window.IsBorderless = false;
        return true;
    }

    /// <summary>
    /// Where the cover is drawn, so its size does not depend on the monitor.
    ///
    /// Asking for a 1260x1000 window on a 1080p display silently gets a shorter one, and the
    /// first cover came out 1260x845 -- the wrong shape for a store page, with the ladder
    /// running over the tagline. An offscreen target is the exact size it says it is.
    /// </summary>
    public void BeginFrame(GraphicsDevice device)
    {
        if (!CoverMode) return;

        _coverTarget ??= new RenderTarget2D(device, CoverWidth, CoverHeight, false,
            SurfaceFormat.Color, DepthFormat.Depth24);
        device.SetRenderTarget(_coverTarget);
    }

    /// <summary>Save a frame without ending the run, unlike --screenshot.</summary>
    public void Queue(string path) => _pending = path;

    /// <summary>
    /// Flush a queued 'shot', unbind the cover target, and quit once warmup and any script hold
    /// have passed.
    /// </summary>
    public void EndFrame(GraphicsDevice device, bool hold, Action exit)
    {
        if (_pending is { } wanted)
        {
            _pending = null;
            Save(device, wanted);
        }

        // Unbind before reading: a render target still bound as output cannot be read back.
        // The back buffer then has nothing in it, so it is cleared rather than left undefined
        // for the frame the driver is about to present.
        if (CoverMode)
        {
            device.SetRenderTarget(null);
            device.Clear(new Color(4, 8, 13));
        }

        if (OutputPath is null) return;

        if (++_framesDrawn <= WarmupFrames) return;

        // A script gets to finish before the picture is taken. Otherwise the two are racing:
        // the capture fires on a frame count while 'wait' asks for seconds, and which one wins
        // depends on how fast the machine happens to be rendering.
        if (hold) return;

        Save(device, OutputPath);
        exit();
    }

    /// <summary>
    /// Write the frame just drawn to a PNG.
    ///
    /// Split out from the exit path so 'shot' can take a picture mid-script without ending
    /// the run -- a test that walks somewhere, photographs it, walks on and photographs that
    /// is worth far more than one that can only ever produce a single frame.
    /// </summary>
    public void Save(GraphicsDevice device, string path)
    {
        var captureWidth = CoverMode ? CoverWidth : device.Viewport.Width;
        var captureHeight = CoverMode ? CoverHeight : device.Viewport.Height;

        var pixels = new Color[captureWidth * captureHeight];
        if (CoverMode && _coverTarget is not null) _coverTarget.GetData(pixels);
        else device.GetBackBufferData(pixels);

        using var texture = new Texture2D(device, captureWidth, captureHeight);
        texture.SetData(pixels);

        var fullPath = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);

        using (var stream = File.Create(fullPath))
            texture.SaveAsPng(stream, captureWidth, captureHeight);

        Console.WriteLine($"Saved {captureWidth}x{captureHeight} screenshot to {fullPath}");
    }

    public void Dispose()
    {
        _coverTarget?.Dispose();
        _coverTarget = null;
    }
}
