using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Ember.Render;

/// <summary>Shared ambient and directional lighting values for static and skinned draws.</summary>
public sealed class SceneLighting
{
    private Vector3 _ambientColor = new(0.62f, 0.64f, 0.68f);
    private Vector3 _directionalDirection = Vector3.Normalize(new Vector3(-0.4f, -1f, -0.25f));
    private Vector3 _directionalColor = new(0.9f);
    private Vector3 _specularColor = new(0.12f);

    public Vector3 AmbientColor
    {
        get => _ambientColor;
        set => _ambientColor = ValidateColor(value, nameof(value));
    }

    public Vector3 DirectionalDirection
    {
        get => _directionalDirection;
        set => _directionalDirection = NormalizeDirection(value);
    }

    public Vector3 DirectionalColor
    {
        get => _directionalColor;
        set => _directionalColor = ValidateColor(value, nameof(value));
    }

    public Vector3 SpecularColor
    {
        get => _specularColor;
        set => _specularColor = ValidateColor(value, nameof(value));
    }

    public void Apply(BasicEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        effect.EnableDefaultLighting();
        effect.DirectionalLight1.Enabled = false;
        effect.DirectionalLight2.Enabled = false;
        effect.AmbientLightColor = AmbientColor;
        effect.DirectionalLight0.Direction = DirectionalDirection;
        effect.DirectionalLight0.DiffuseColor = DirectionalColor;
        effect.DirectionalLight0.SpecularColor = SpecularColor;
    }

    public void Apply(SkinnedEffect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        effect.EnableDefaultLighting();
        effect.DirectionalLight1.Enabled = false;
        effect.DirectionalLight2.Enabled = false;
        effect.AmbientLightColor = AmbientColor;
        effect.DirectionalLight0.Direction = DirectionalDirection;
        effect.DirectionalLight0.DiffuseColor = DirectionalColor;
        effect.DirectionalLight0.SpecularColor = SpecularColor;
    }

    /// <summary>Applies the same authored light values to a custom scene effect.</summary>
    public void Apply(Effect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        effect.Parameters["AmbientColor"]?.SetValue(AmbientColor);
        effect.Parameters["DirectionalDirection"]?.SetValue(DirectionalDirection);
        effect.Parameters["DirectionalColor"]?.SetValue(DirectionalColor);
    }

    private static Vector3 NormalizeDirection(Vector3 value)
    {
        if (!IsFinite(value) || value.LengthSquared() < 1e-8f)
            throw new ArgumentOutOfRangeException(nameof(value), "Directional light direction must be finite and nonzero.");
        return Vector3.Normalize(value);
    }

    private static Vector3 ValidateColor(Vector3 value, string parameterName)
    {
        if (!IsFinite(value) || value.X < 0f || value.Y < 0f || value.Z < 0f)
            throw new ArgumentOutOfRangeException(parameterName, "Lighting color values must be finite and nonnegative.");
        return value;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
