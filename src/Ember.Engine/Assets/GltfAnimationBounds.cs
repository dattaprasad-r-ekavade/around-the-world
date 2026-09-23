using System;
using Microsoft.Xna.Framework;

namespace Ember.Assets;

/// <summary>Sampled, padded bounds for the supported skeletal clip format.</summary>
public static class GltfAnimationBounds
{
    public const float DefaultSampleIntervalSeconds = 1f / 30f;
    public const float DefaultMarginFraction = 0.10f;
    public const float DefaultMinimumMargin = 0.05f;
    private const int MaximumSamples = 1_000_000;

    /// <summary>
    /// Samples the fully deformed mesh through a clip and expands its AABB by the larger of
    /// <paramref name="minimumMargin"/> and <paramref name="marginFraction"/> times its largest extent.
    /// This envelope is for the imported STEP/LINEAR clip; procedural pose changes need uncullled handling.
    /// </summary>
    public static Bounds3 SampleClip(GltfSkinnedCharacterData character, GltfAnimationClipData clip,
        float sampleIntervalSeconds = DefaultSampleIntervalSeconds,
        float marginFraction = DefaultMarginFraction,
        float minimumMargin = DefaultMinimumMargin)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(clip);
        if (!ReferenceEquals(character.Skin, clip.Skin))
            throw new ArgumentException("The animation clip belongs to a different imported skin.", nameof(clip));
        if (!float.IsFinite(sampleIntervalSeconds) || sampleIntervalSeconds <= 0f)
            throw new ArgumentOutOfRangeException(nameof(sampleIntervalSeconds), "Sample interval must be finite and positive.");
        if (!float.IsFinite(marginFraction) || marginFraction < 0f)
            throw new ArgumentOutOfRangeException(nameof(marginFraction), "Margin fraction must be finite and nonnegative.");
        if (!float.IsFinite(minimumMargin) || minimumMargin < 0f)
            throw new ArgumentOutOfRangeException(nameof(minimumMargin), "Minimum margin must be finite and nonnegative.");

        var requestedSamples = Math.Ceiling((double)clip.Duration / sampleIntervalSeconds);
        if (requestedSamples > MaximumSamples)
            throw new ArgumentOutOfRangeException(nameof(sampleIntervalSeconds),
                $"Sampling would require more than {MaximumSamples} frames.");
        var intervals = Math.Max(1, (int)requestedSamples);
        var pose = character.CreatePose();
        var minimum = new Vector3(float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity);

        for (var sampleIndex = 0; sampleIndex <= intervals; sampleIndex++)
        {
            var time = clip.Duration * (sampleIndex / (float)intervals);
            clip.Evaluate(pose, time);
            var meshWorld = pose.MeshNodeWorldMatrix;
            foreach (var primitive in character.Primitives)
            foreach (var vertex in primitive.Mesh.Vertices)
            {
                var weights = vertex.JointWeights;
                var deformedMeshPosition = Vector3.Zero;
                for (var influence = 0; influence < 4; influence++)
                {
                    var weight = weights.GetWeight(influence);
                    if (weight <= 0f) continue;
                    var jointIndex = weights.GetJointIndex(influence);
                    deformedMeshPosition += Vector3.Transform(
                        vertex.Position, pose.SkinMatrices[jointIndex]) * weight;
                }

                var deformedCharacterPosition = Vector3.Transform(deformedMeshPosition, meshWorld);
                minimum = Vector3.Min(minimum, deformedCharacterPosition);
                maximum = Vector3.Max(maximum, deformedCharacterPosition);
            }
        }

        if (!IsFinite(minimum) || !IsFinite(maximum))
            throw new InvalidOperationException("The sampled character bounds are not finite.");

        var extent = maximum - minimum;
        var margin = MathF.Max(minimumMargin,
            MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z)) * marginFraction);
        var padding = new Vector3(margin);
        return new Bounds3(minimum - padding, maximum + padding);
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
