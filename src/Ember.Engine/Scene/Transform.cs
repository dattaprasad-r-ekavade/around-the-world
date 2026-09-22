using Microsoft.Xna.Framework;

namespace Ember.Scene;

/// <summary>Local position, rotation, and scale using Ember's documented world conventions.</summary>
public sealed class Transform
{
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector3 Scale { get; set; } = Vector3.One;

    /// <summary>
    /// MonoGame uses row-vector transforms. A point therefore receives scale, rotation, then
    /// translation in this order.
    /// </summary>
    public Matrix LocalMatrix =>
        Matrix.CreateScale(Scale)
        * Matrix.CreateFromQuaternion(Rotation)
        * Matrix.CreateTranslation(Position);
}
