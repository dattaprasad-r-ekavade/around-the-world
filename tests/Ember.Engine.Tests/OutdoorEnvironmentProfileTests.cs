using System;
using Ember.Render;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class OutdoorEnvironmentProfileTests
{
    [Fact]
    public void FixedClockTimeProducesTheSameEnvironmentAndWrapsByDay()
    {
        var noon = OutdoorEnvironmentProfile.Evaluate(12f);
        var repeatedNoon = OutdoorEnvironmentProfile.Evaluate(12f);
        var nextDayNoon = OutdoorEnvironmentProfile.Evaluate(36f);

        Assert.Equal(noon, repeatedNoon);
        Assert.Equal(noon, nextDayNoon);
    }

    [Fact]
    public void ClockTimeUpdatesSkyFogAndDirectionalLightAcrossDayAndNight()
    {
        var dawn = OutdoorEnvironmentProfile.Evaluate(6f);
        var noon = OutdoorEnvironmentProfile.Evaluate(12f);
        var night = OutdoorEnvironmentProfile.Evaluate(0f);

        Assert.NotEqual(dawn.SkyColor, noon.SkyColor);
        Assert.NotEqual(noon.FogColor, night.FogColor);
        Assert.NotEqual(dawn.LightDirection, noon.LightDirection);
        Assert.True(noon.DirectionalLightColor.X > night.DirectionalLightColor.X);
        Assert.InRange(noon.LightDirection.Length(), 0.999f, 1.001f);
    }

    [Fact]
    public void RejectsInvalidTimesAndFogRanges()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OutdoorEnvironmentProfile.Evaluate(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutdoorEnvironmentProfile.Evaluate(12f, fogStart: -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => OutdoorEnvironmentProfile.Evaluate(12f, fogStart: 50f, fogEnd: 40f));
    }
}
