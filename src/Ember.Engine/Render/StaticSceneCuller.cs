using Ember.Assets;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Ember.Render;

/// <summary>Tests transformed static-mesh bounds against a camera frustum.</summary>
public static class StaticSceneCuller
{
    /// <summary>
    /// Returns false only when the complete transformed local bounds lie outside the frustum.
    /// Meshes without bounds must remain visible because they cannot be safely rejected.
    /// </summary>
    public static bool IsVisible(Bounds3? localBounds, Matrix world, BoundingFrustum frustum)
    {
        ArgumentNullException.ThrowIfNull(frustum);
        if (localBounds is not { } bounds) return true;
        var transformed = bounds.Transform(world);
        return frustum.Intersects(new BoundingBox(transformed.Min, transformed.Max));
    }
}
