using System;
using Ember.Render;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class SceneLightingTests
{
    [Fact]
    public void DefaultsProvideOneNormalizedDirectionalLightAndAmbientFill()
    {
        var lighting = new SceneLighting();

        Assert.Equal(new Vector3(0.62f, 0.64f, 0.68f), lighting.AmbientColor);
        Assert.Equal(new Vector3(0.9f), lighting.DirectionalColor);
        Assert.InRange(MathF.Abs(lighting.DirectionalDirection.Length() - 1f), 0f, 0.00001f);
    }

    [Fact]
    public void DirectionIsNormalizedAndInvalidLightingValuesAreRejected()
    {
        var lighting = new SceneLighting { DirectionalDirection = new Vector3(0f, 3f, 4f) };

        Assert.Equal(new Vector3(0f, 0.6f, 0.8f), lighting.DirectionalDirection);
        Assert.Throws<ArgumentOutOfRangeException>(() => lighting.DirectionalDirection = Vector3.Zero);
        Assert.Throws<ArgumentOutOfRangeException>(() => lighting.DirectionalColor = new Vector3(-1f, 0f, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => lighting.AmbientColor = new Vector3(float.NaN));
    }
}
