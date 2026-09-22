using Microsoft.Xna.Framework;

namespace Ember;

/// <summary>A free orbit camera for scene inspection, independent of first-person movement.</summary>
public sealed class OrbitCamera
{
    public Vector3 Target { get; private set; }
    public float Distance { get; private set; } = 8f;
    public float Yaw { get; private set; }
    public float Pitch { get; private set; } = -0.25f;
    public float MinDistance { get; set; } = 1f;
    public float MaxDistance { get; set; } = 100f;
    public float OrbitSensitivity { get; set; } = 0.01f;
    public float ZoomSensitivity { get; set; } = 0.01f;
    public Matrix View { get; private set; } = Matrix.Identity;
    public Matrix Projection { get; private set; } = Matrix.Identity;
    public Vector3 Position { get; private set; }

    public void Reset(Vector3 target, float distance = 8f, float yaw = 0f, float pitch = -0.25f)
    {
        Target = target;
        Distance = MathHelper.Clamp(distance, MinDistance, MaxDistance);
        Yaw = yaw;
        Pitch = MathHelper.Clamp(pitch, -1.5f, 1.5f);
        RebuildView();
    }

    public void SetProjection(float aspect, float fieldOfViewDegrees = 60f,
        float near = 0.05f, float far = 500f)
    {
        if (aspect <= 0f) return;
        Projection = Matrix.CreatePerspectiveFieldOfView(
            MathHelper.ToRadians(fieldOfViewDegrees), aspect, near, far);
    }

    public void Orbit(Vector2 delta)
    {
        Yaw -= delta.X * OrbitSensitivity;
        Pitch = MathHelper.Clamp(Pitch - delta.Y * OrbitSensitivity, -1.5f, 1.5f);
        RebuildView();
    }

    public void Zoom(float wheelDelta)
    {
        Distance = MathHelper.Clamp(Distance - wheelDelta * ZoomSensitivity, MinDistance, MaxDistance);
        RebuildView();
    }

    public void RebuildView()
    {
        var offset = Vector3.Transform(
            new Vector3(0f, 0f, Distance),
            Matrix.CreateRotationX(Pitch) * Matrix.CreateRotationY(Yaw));
        Position = Target + offset;
        View = Matrix.CreateLookAt(Position, Target, Vector3.Up);
    }
}
