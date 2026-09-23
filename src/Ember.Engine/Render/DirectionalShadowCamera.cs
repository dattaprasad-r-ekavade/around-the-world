using Ember.Assets;
using Microsoft.Xna.Framework;
using System;

namespace Ember.Render;

/// <summary>Builds an orthographic light camera that encloses a scene bounds volume.</summary>
public static class DirectionalShadowCamera
{
    public static Matrix CreateViewProjection(Bounds3 bounds, Vector3 lightDirection)
    {
        if (!IsFinite(lightDirection) || lightDirection.LengthSquared() < 1e-8f)
            throw new ArgumentOutOfRangeException(nameof(lightDirection), "Light direction must be finite and nonzero.");

        var direction = Vector3.Normalize(lightDirection);
        var radius = MathF.Max(bounds.Size.Length() * 0.5f, 1f);
        var center = bounds.Center;
        var eye = center - direction * (radius * 2.5f);
        var up = MathF.Abs(Vector3.Dot(direction, Vector3.Up)) > 0.98f
            ? Vector3.Forward
            : Vector3.Up;
        var view = Matrix.CreateLookAt(eye, center, up);
        var diameter = radius * 2.2f;
        var projection = Matrix.CreateOrthographic(diameter, diameter, 0.1f, radius * 5f + 2f);
        return view * projection;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
