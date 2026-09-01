using Ember;
using Microsoft.Xna.Framework;
using System;

namespace Campaign;

/// <summary>One active location. Swaps with a fade so enter-town is a load, not a seam.</summary>
public sealed class LocationRunner
{
    public ILocation Current { get; private set; } = null!;
    public float Fade { get; private set; }

    private ILocation? _pending;
    private Vector3 _pendingPosition;
    private float _pendingYaw;
    private Action<string>? _onSwapped;

    public void Bind(ILocation location, Vector3 position, float yaw, GroundedView view,
        Action<string>? onSwapped = null)
    {
        Current = location;
        _onSwapped = onSwapped;
        view.Reset(position, yaw, pitch: -0.08f, standingEyeY: WorldScale.EyeHeight);
        Fade = 0f;
        _pending = null;
        _onSwapped?.Invoke(Current.Name);
    }

    public void Request(ILocation next, Vector3 position, float yaw)
    {
        _pending = next;
        _pendingPosition = position;
        _pendingYaw = yaw;
    }

    public bool IsFading => Fade > 0.02f || _pending is not null;

    public void Update(float seconds, GroundedView view)
    {
        if (_pending is not null)
        {
            Fade = MathF.Min(1f, Fade + seconds * 3.2f);
            if (Fade < 1f) return;

            Current = _pending;
            _pending = null;
            view.Reset(_pendingPosition, _pendingYaw, pitch: -0.08f, WorldScale.EyeHeight);
            _onSwapped?.Invoke(Current.Name);
        }
        else if (Fade > 0f)
        {
            Fade = MathF.Max(0f, Fade - seconds * 3.2f);
        }
    }
}
