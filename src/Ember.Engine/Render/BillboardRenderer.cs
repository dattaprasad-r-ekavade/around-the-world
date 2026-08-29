using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;

namespace Ember.Render;

/// <summary>
/// Draws camera-facing quads.
///
/// Uses <see cref="AlphaTestEffect"/> rather than alpha blending: a cutout writes depth, so
/// sprites sort correctly against the world and against each other without being sorted back
/// to front every frame. Soft edges are not wanted here anyway — flat pigment with a drawn
/// contour is the art direction.
/// </summary>
public sealed class BillboardRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly AlphaTestEffect _effect;
    private readonly VertexPositionTexture[] _quad = new VertexPositionTexture[4];
    private readonly short[] _indices = { 0, 1, 2, 0, 2, 3 };

    public BillboardRenderer(GraphicsDevice device)
    {
        _device = device;
        _effect = new AlphaTestEffect(device)
        {
            // Anything under half opaque is a hole, not a soft edge.
            ReferenceAlpha = 128,
            AlphaFunction = CompareFunction.Greater,

            // The quads carry position and texture coordinates only. Asking the shader for a
            // colour element the vertex declaration does not have fails at draw time.
            VertexColorEnabled = false
        };
    }

    public void Begin(Matrix view, Matrix projection)
    {
        _effect.View = view;
        _effect.Projection = projection;
        _effect.World = Matrix.Identity;

        _device.BlendState = BlendState.AlphaBlend;
        _device.DepthStencilState = DepthStencilState.Default;
        _device.RasterizerState = RasterizerState.CullNone;
        _device.SamplerStates[0] = SamplerState.PointClamp;
    }

    /// <summary>
    /// Draw one sprite standing on the ground at <paramref name="feet"/>.
    /// </summary>
    /// <param name="cameraYaw">
    /// Only yaw is used. Tilting a standing figure to follow the camera's pitch makes it look
    /// like it is falling over when the player looks up.
    /// </param>
    public void Draw(Texture2D texture, Vector3 feet, float height, float cameraYaw, Color tint)
    {
        var aspect = texture.Width / (float)texture.Height;
        var halfWidth = height * aspect * 0.5f;

        // Face the camera by rotating about the vertical axis only.
        var right = new Vector3(MathF.Cos(cameraYaw), 0f, MathF.Sin(cameraYaw)) * halfWidth;
        var up = Vector3.Up * height;

        _quad[0] = new VertexPositionTexture(feet - right + up, new Vector2(0f, 0f));
        _quad[1] = new VertexPositionTexture(feet + right + up, new Vector2(1f, 0f));
        _quad[2] = new VertexPositionTexture(feet + right, new Vector2(1f, 1f));
        _quad[3] = new VertexPositionTexture(feet - right, new Vector2(0f, 1f));

        _effect.Texture = texture;
        _effect.DiffuseColor = tint.ToVector3();
        _effect.Alpha = tint.A / 255f;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList, _quad, 0, 4, _indices, 0, 2);
        }
    }

    public void Dispose() => _effect.Dispose();
}
