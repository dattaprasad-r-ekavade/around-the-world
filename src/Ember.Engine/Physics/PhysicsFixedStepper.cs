using System;

namespace Ember.Physics;

public readonly record struct PhysicsStepResult(int Steps, float InterpolationAlpha, double DroppedSeconds);

/// <summary>Accumulates render time into fixed physics steps with a bounded catch-up budget.</summary>
public sealed class PhysicsFixedStepper
{
    private double _accumulator;

    public PhysicsFixedStepper(float fixedDeltaSeconds = 1f / 60f, int maxCatchUpSteps = 8)
    {
        if (!float.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
        if (maxCatchUpSteps <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCatchUpSteps));

        FixedDeltaSeconds = fixedDeltaSeconds;
        MaxCatchUpSteps = maxCatchUpSteps;
    }

    public float FixedDeltaSeconds { get; }
    public int MaxCatchUpSteps { get; }
    public float InterpolationAlpha => (float)(_accumulator / FixedDeltaSeconds);
    public double TotalDroppedSeconds { get; private set; }

    public PhysicsStepResult Advance(double elapsedSeconds, Action<float> simulate)
    {
        ArgumentNullException.ThrowIfNull(simulate);
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0d)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Elapsed time must be finite and nonnegative.");

        _accumulator += elapsedSeconds;
        var stepCount = 0;
        while (_accumulator >= FixedDeltaSeconds && stepCount < MaxCatchUpSteps)
        {
            simulate(FixedDeltaSeconds);
            _accumulator -= FixedDeltaSeconds;
            stepCount++;
        }

        var dropped = 0d;
        if (_accumulator >= FixedDeltaSeconds)
        {
            var remainder = _accumulator % FixedDeltaSeconds;
            dropped = _accumulator - remainder;
            _accumulator = remainder;
            TotalDroppedSeconds += dropped;
        }

        return new PhysicsStepResult(stepCount, InterpolationAlpha, dropped);
    }

    public void Reset()
    {
        _accumulator = 0d;
        TotalDroppedSeconds = 0d;
    }
}
