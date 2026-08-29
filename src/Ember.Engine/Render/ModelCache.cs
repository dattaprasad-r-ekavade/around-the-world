using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Ember.Render;

/// <summary>
/// Imported models: loaded once, measured once, and drawn at a size that means something.
///
/// **The first piece lifted out of <c>Game1</c>, and chosen because it is genuinely
/// self-contained.** It held five dictionaries and four methods in a 6,600-line class, and
/// depends on nothing but a content manager and the two camera matrices — no lights, no
/// palette, no shader, no screen state. Everything else in that file is more tangled than
/// this, so this is the one that could be moved without arguing about what it is allowed to
/// know.
///
/// **Normalising is the reason this is a cache rather than a dictionary.** An imported model
/// arrives at whatever scale its author used and centred wherever its origin happened to fall,
/// so placing two of them side by side with the same scale number gives one the size of a
/// house. Each model is measured on load — bounding spheres through their absolute bone
/// transforms — and stored with the centre offset and the reciprocal of its extent, so a
/// caller asking for scale 1.4 gets the same apparent size whatever the asset was.
///
/// **Lighting is applied once at load, not per mesh per frame.** <c>EnableDefaultLighting</c>
/// rewrites every light and reselects a shader permutation; it was running for every mesh of
/// every model, every frame, and the only things that actually change between frames are the
/// three matrices.
/// </summary>
public sealed class ModelCache
{
    private readonly Dictionary<string, Model> _models = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> _normalizers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Vector3> _centres = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Matrix[]> _bones = new(StringComparer.Ordinal);
    private readonly List<string> _errors = new();

    /// <summary>
    /// What failed to load, for the on-screen content-error list.
    ///
    /// A missing model is reported rather than thrown: one absent asset should cost its own
    /// prop, not the whole scene, and a build that runs with a hole in it can still be played
    /// and photographed while the hole is fixed.
    /// </summary>
    public IReadOnlyList<string> Errors => _errors;

    public bool Has(string key) => _models.ContainsKey(key);

    /// <summary>Load one model under a short key, measuring and lighting it as it arrives.</summary>
    public void Load(ContentManager content, string key, string contentPath)
    {
        try
        {
            var model = content.Load<Model>(contentPath);
            _models[key] = model;

            var bones = new Matrix[model.Bones.Count];
            if (bones.Length > 0) model.CopyAbsoluteBoneTransformsTo(bones);

            var (centre, extent) = Measure(model, bones);
            _centres[key] = centre;
            _normalizers[key] = 1f / extent;
            _bones[key] = bones;

            ApplyLighting(model);
        }
        catch (Exception exception)
        {
            _errors.Add($"{key}: {exception.GetType().Name}");
        }
    }

    /// <summary>
    /// Draw one, centred on <paramref name="position"/> and normalised to
    /// <paramref name="scale"/> metres regardless of what size the asset was authored at.
    ///
    /// Silently does nothing for a key that failed to load, which is the same decision as
    /// <see cref="Errors"/>: the missing prop is already reported once, and reporting it again
    /// sixty times a second helps nobody.
    /// </summary>
    public void Draw(string key, Vector3 position, float scale, float rotation,
        Matrix view, Matrix projection)
    {
        if (!_models.TryGetValue(key, out var model)) return;

        var normalizer = _normalizers.TryGetValue(key, out var stored) ? stored : 1f;
        var centre = _centres.TryGetValue(key, out var storedCentre) ? storedCentre : Vector3.Zero;

        var world = Matrix.CreateTranslation(-centre)
            * Matrix.CreateScale(scale * normalizer)
            * Matrix.CreateRotationY(rotation)
            * Matrix.CreateTranslation(position);

        var boneTransforms = _bones.TryGetValue(key, out var cached)
            ? cached
            : Array.Empty<Matrix>();

        foreach (var mesh in model.Meshes)
        {
            var meshTransform = boneTransforms.Length > mesh.ParentBone.Index
                ? boneTransforms[mesh.ParentBone.Index]
                : Matrix.Identity;

            foreach (var effect in mesh.Effects)
            {
                // Only what actually changes per frame. Lighting and fog were set at load.
                if (effect is BasicEffect basic)
                {
                    basic.World = meshTransform * world;
                    basic.View = view;
                    basic.Projection = projection;
                }
            }

            mesh.Draw();
        }
    }

    /// <summary>
    /// Lighting, fog and material settings that never change. Applied once per loaded model
    /// rather than per mesh per frame.
    /// </summary>
    private static void ApplyLighting(Model model)
    {
        foreach (var mesh in model.Meshes)
        foreach (var effect in mesh.Effects)
        {
            if (effect is not BasicEffect basic) continue;

            basic.EnableDefaultLighting();
            basic.PreferPerPixelLighting = true;
            basic.AmbientLightColor = new Vector3(0.54f, 0.57f, 0.62f);
            basic.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.45f, -1f, -0.2f));
            basic.DirectionalLight0.DiffuseColor = new Vector3(1f, 0.84f, 0.68f);
            basic.DirectionalLight0.SpecularColor = new Vector3(0.24f);
            basic.FogEnabled = true;
            basic.FogStart = 18f;
            basic.FogEnd = 45f;
            basic.FogColor = new Color(70, 88, 91).ToVector3();
        }
    }

    /// <summary>
    /// The model's centre and half-extent, in its own space.
    ///
    /// Measured through each mesh's absolute bone transform rather than its raw bounding
    /// sphere. Skipping that step is what made the yard's imported props tower over the
    /// buildings: a mesh parented under a scaled bone reports a radius in bone space, and
    /// taking it at face value understates a big model and overstates a small one.
    /// </summary>
    private static (Vector3 Centre, float Extent) Measure(Model model, Matrix[] boneTransforms)
    {
        if (model.Meshes.Count == 0) return (Vector3.Zero, 1f);

        var minimum = new Vector3(float.MaxValue);
        var maximum = new Vector3(float.MinValue);

        foreach (var mesh in model.Meshes)
        {
            var transform = boneTransforms.Length > mesh.ParentBone.Index
                ? boneTransforms[mesh.ParentBone.Index]
                : Matrix.Identity;

            var sphere = mesh.BoundingSphere;
            var centre = Vector3.Transform(sphere.Center, transform);
            var scale = MathF.Max(
                Vector3.TransformNormal(Vector3.Right, transform).Length(),
                MathF.Max(
                    Vector3.TransformNormal(Vector3.Up, transform).Length(),
                    Vector3.TransformNormal(Vector3.Forward, transform).Length()));

            var radius = new Vector3(sphere.Radius * scale);
            minimum = Vector3.Min(minimum, centre - radius);
            maximum = Vector3.Max(maximum, centre + radius);
        }

        var middle = (minimum + maximum) * 0.5f;
        var halfSize = (maximum - minimum) * 0.5f;
        var extent = MathF.Max(halfSize.X, MathF.Max(halfSize.Y, halfSize.Z));

        return (middle, MathF.Max(extent, 0.001f));
    }
}
