using System;
using Ember.Scene;
using Microsoft.Xna.Framework;
using Xunit;

namespace Ember.Engine.Tests;

public sealed class TransformTests
{
    [Fact]
    public void IdentityHasNoTranslationRotationOrScale()
    {
        var transform = new Transform();

        Assert.Equal(Vector3.Zero, transform.Position);
        Assert.Equal(Quaternion.Identity, transform.Rotation);
        Assert.Equal(Vector3.One, transform.Scale);
        Assert.Equal(Matrix.Identity, transform.LocalMatrix);
    }

    [Fact]
    public void LocalMatrixAppliesScaleThenRotationThenTranslation()
    {
        var transform = new Transform
        {
            Position = new Vector3(10f, 2f, -4f),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.Up, MathHelper.PiOver2),
            Scale = new Vector3(2f, 3f, 4f)
        };

        var result = Vector3.Transform(new Vector3(1f, 1f, 1f), transform.LocalMatrix);

        // Row-vector MonoGame transforms apply S, then R, then T: (2,3,4) rotated +90° around
        // Y becomes (4,3,-2), then receives the translation.
        AssertClose(new Vector3(14f, 5f, -6f), result);
    }

    [Fact]
    public void RotationAndTranslationFixtureUsesDocumentedOrder()
    {
        var local = Matrix.CreateRotationY(MathHelper.PiOver2)
            * Matrix.CreateTranslation(3f, 0f, 0f);

        var result = Vector3.Transform(Vector3.Forward, local);

        AssertClose(new Vector3(2f, 0f, 0f), result);
    }

    private static void AssertClose(Vector3 expected, Vector3 actual)
    {
        Assert.True(Vector3.Distance(expected, actual) < 0.0001f,
            $"Expected {expected}, got {actual}");
    }
}
