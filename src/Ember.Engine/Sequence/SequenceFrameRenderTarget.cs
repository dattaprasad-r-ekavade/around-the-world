using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Ember.Sequence;

/// <summary>Reusable exact-size color/depth target that writes a rendered frame to a PNG.</summary>
public sealed class SequenceFrameRenderTarget : IDisposable
{
    private readonly GraphicsDevice _device;
    private RenderTarget2D? _target;
    private bool _disposed;

    public SequenceFrameRenderTarget(GraphicsDevice device) =>
        _device = device ?? throw new ArgumentNullException(nameof(device));

    public int Width => _target?.Width ?? 0;
    public int Height => _target?.Height ?? 0;

    /// <summary>
    /// Binds a target at the requested resolution, invokes the scene draw, restores the caller's
    /// render targets and viewport, then writes the detached target as a new PNG file.
    /// </summary>
    public void RenderPng(int width, int height, string outputPath, Action drawScene,
        Color? clearColor = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(drawScene);
        if (width < 1 || height < 1)
            throw new ArgumentOutOfRangeException(nameof(width), "Render-target dimensions must be positive.");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("An output path is required.", nameof(outputPath));

        EnsureTarget(width, height);
        var target = _target!;
        var previousTargets = _device.GetRenderTargets();
        var previousViewport = _device.Viewport;
        try
        {
            _device.SetRenderTarget(target);
            _device.Viewport = new Viewport(0, 0, width, height);
            _device.Clear(clearColor ?? new Color(12, 16, 24));
            drawScene();
        }
        finally
        {
            if (previousTargets.Length == 0) _device.SetRenderTarget(null);
            else _device.SetRenderTargets(previousTargets);
            _device.Viewport = previousViewport;
        }

        var fullPath = Path.GetFullPath(outputPath);
        var createdOutput = false;
        try
        {
            using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            createdOutput = true;
            target.SaveAsPng(stream, width, height);
        }
        catch
        {
            if (createdOutput)
            {
                try { File.Delete(fullPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    }

    private void EnsureTarget(int width, int height)
    {
        if (_target is { } current && current.Width == width && current.Height == height) return;
        _target?.Dispose();
        _target = null;
        _target = new RenderTarget2D(_device, width, height, mipMap: false,
            SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _target?.Dispose();
        _target = null;
    }
}
