using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Ember.Render;

/// <summary>Owns and resizes the depth-tested color target used by a directional shadow pass.</summary>
public sealed class DirectionalShadowMap : IDisposable
{
    private readonly GraphicsDevice _device;
    private RenderTarget2D? _target;
    private RenderTargetBinding[]? _previousTargets;
    private bool _active;
    private bool _disposed;

    public DirectionalShadowMap(GraphicsDevice device, int viewportWidth, int viewportHeight)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        Resize(viewportWidth, viewportHeight);
    }

    public bool IsDisposed => _disposed;
    public int Size => _target?.Width ?? 0;
    public Texture2D Texture => _target ?? throw new ObjectDisposedException(nameof(DirectionalShadowMap));

    public void Resize(int viewportWidth, int viewportHeight)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DirectionalShadowMap));
        if (_active) throw new InvalidOperationException("A shadow map cannot be resized during its render pass.");
        var size = ComputeSize(viewportWidth, viewportHeight);
        if (_target?.Width == size) return;

        var replacement = new RenderTarget2D(_device, size, size, false, SurfaceFormat.Color,
            DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);
        var retired = _target;
        _target = replacement;
        retired?.Dispose();
    }

    public void Begin()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DirectionalShadowMap));
        if (_active) throw new InvalidOperationException("The shadow map pass is already active.");
        _previousTargets = _device.GetRenderTargets();
        _device.SetRenderTarget(_target);
        _device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.White, 1f, 0);
        _active = true;
    }

    public void End()
    {
        if (!_active) return;
        try
        {
            if (_previousTargets is { Length: > 0 }) _device.SetRenderTargets(_previousTargets);
            else _device.SetRenderTarget(null);
        }
        finally
        {
            _previousTargets = null;
            _active = false;
        }
    }

    public static int ComputeSize(int viewportWidth, int viewportHeight)
    {
        var requested = Math.Max(1, Math.Max(viewportWidth, viewportHeight) / 2);
        var size = 1;
        while (size < requested && size < 2048) size <<= 1;
        return Math.Clamp(size, 512, 2048);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { End(); }
        finally
        {
            _target?.Dispose();
            _target = null;
        }
    }
}
