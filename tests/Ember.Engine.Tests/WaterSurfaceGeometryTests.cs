using Ember.Render;
using Microsoft.Xna.Framework;
using System;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class WaterSurfaceGeometryTests
{
    [Fact]
    public void PlaneCoversConfiguredExtentAndRepeatsTextureInWorldUnits()
    {
        var (vertices, indices) = WaterSurfaceGeometry.CreatePlane(64f, textureTileMetres: 8f);

        Assert.Equal(4, vertices.Length);
        Assert.Equal(6, indices.Length);
        Assert.Equal(new Vector3(-64f, 0f, -64f), vertices[0].Position);
        Assert.Equal(new Vector3(64f, 0f, 64f), vertices[3].Position);
        Assert.Equal(new Vector2(16f, 16f), vertices[3].TextureCoordinate);
        Assert.All(vertices, vertex => Assert.Equal(0f, vertex.Position.Y));
    }

    [Fact]
    public void PlaneRejectsInvalidDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WaterSurfaceGeometry.CreatePlane(0f, 8f));
        Assert.Throws<ArgumentOutOfRangeException>(() => WaterSurfaceGeometry.CreatePlane(16f, float.NaN));
    }
}
