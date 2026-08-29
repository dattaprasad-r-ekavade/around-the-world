using Microsoft.Xna.Framework;
using System;

namespace Ember.Render;

/// <summary>
/// A material: an ordered ramp from its darkest shadow to its brightest catch, plus the colour
/// its outline is drawn in.
///
/// Ramps rather than a base colour and a maths operation, because that is how the look actually
/// works. A pixel artist picks four or five colours per material and shades by choosing among
/// them; shading by multiplying a single hue gives you the muddy, plastic result that says
/// "generated" from across a room. A ramp can also bend hue as it goes — iron cools toward blue
/// in shadow, gold warms toward red — which no multiply will ever do for you.
/// </summary>
public sealed class SpriteMaterial
{
    public required Color[] Ramp { get; init; }
    public required Color Outline { get; init; }

    /// <summary>How tight the specular catch is. Higher is glossier and smaller.</summary>
    public float Gloss { get; init; } = 0f;

    /// <summary>Colour of that catch, when <see cref="Gloss"/> is above zero.</summary>
    public Color Highlight { get; init; } = Color.White;

    /// <summary>
    /// Build a ramp from one base colour.
    ///
    /// The existing character and weapon palettes are single flat colours, and there are a lot
    /// of them; asking every one to be rewritten as a hand-picked ramp would be the real cost
    /// of moving to the forge. This derives one instead, and derives it the way a painter
    /// would rather than the way a computer would: shadows fall toward a cool blue and
    /// highlights climb toward a warm white, so the ramp bends hue across its length.
    ///
    /// Multiplying a base colour up and down — the obvious approach — keeps hue constant and
    /// is exactly what makes generated art look plastic. Skin shaded that way goes orange in
    /// shadow instead of red-brown, and steel goes grey instead of blue.
    /// </summary>
    public static SpriteMaterial FromBase(Color baseColour, int steps = 5, float gloss = 0f)
    {
        var shadow = new Color(
            (byte)(baseColour.R * 0.24f),
            (byte)(baseColour.G * 0.28f + 6),
            (byte)(baseColour.B * 0.46f + 14));

        var highlight = new Color(255, 248, 232);

        var ramp = new Color[Math.Max(3, steps)];
        for (var i = 0; i < ramp.Length; i++)
        {
            // -1 at the darkest end, +1 at the brightest, base sitting a little above centre
            // because most of a lit object is closer to its own colour than to either extreme.
            var t = i / (float)(ramp.Length - 1) * 2f - 1f;

            ramp[i] = t < 0f
                ? Color.Lerp(baseColour, shadow, -t * 0.92f)
                : Color.Lerp(baseColour, highlight, t * 0.46f);
        }

        return new SpriteMaterial
        {
            Ramp = ramp,
            Outline = new Color(
                (byte)(baseColour.R * 0.16f),
                (byte)(baseColour.G * 0.16f + 4),
                (byte)(baseColour.B * 0.22f + 8)),
            Gloss = gloss,
            Highlight = highlight
        };
    }

    public Color Shade(float light)
    {
        var index = (int)MathHelper.Clamp(light * Ramp.Length, 0f, Ramp.Length - 1);
        return Ramp[index];
    }
}

/// <summary>
/// A software canvas for building sprites out of shapes and light, rather than out of pixels.
///
/// The whole method is one idea: shapes do not write colour, they write **thickness**. Every
/// shape lays down a mask, the mask is turned into a height field by measuring how far each
/// pixel is from the shape's own edge, and only at the end is that height field lit, quantised
/// into a material's ramp, and given an outline.
///
/// Doing it this way is what buys the fidelity. A pixel written directly is flat forever; a
/// pixel that knows how thick the object is there can be lit, can catch a highlight on the
/// side facing the lamp, and can fall into shadow on the side that does not — which is the
/// entire difference between a coloured silhouette and something that looks modelled.
///
/// It also means one lighting decision governs every asset in the game at once. Move the light
/// here and the sword, the pickaxe and the preta all relight together, consistently, for free.
/// That consistency is the thing a hand-drawn set has to work hardest to maintain.
/// </summary>
public sealed class SpriteForge
{
    private readonly int _width;
    private readonly int _height;

    /// <summary>Thickness of the finished object at each pixel. Zero is empty space.</summary>
    private readonly float[] _surface;

    /// <summary>Which material owns each pixel: an index into <see cref="_materials"/>, or -1.</summary>
    private readonly int[] _owner;

    private readonly SpriteMaterial?[] _materials = new SpriteMaterial?[32];
    private int _materialCount;

    /// <summary>Scratch mask for the shape currently being drawn.</summary>
    private readonly bool[] _shape;
    private readonly float[] _distance;

    public SpriteForge(int width, int height)
    {
        _width = width;
        _height = height;
        _surface = new float[width * height];
        _owner = new int[width * height];
        _shape = new bool[width * height];
        _distance = new float[width * height];

        Array.Fill(_owner, -1);
    }

    // ------------------------------------------------------------------ shapes

    /// <summary>Clear the scratch mask and start collecting a new shape.</summary>
    public void Begin() => Array.Clear(_shape);

    /// <summary>A tapered capsule: the workhorse. Handles, hafts, blades, limbs, fingers.</summary>
    public void Capsule(float x0, float y0, float x1, float y1, float r0, float r1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var lengthSquared = MathF.Max(0.0001f, dx * dx + dy * dy);

        for (var y = 0; y < _height; y++)
        for (var x = 0; x < _width; x++)
        {
            // Project the pixel onto the segment, clamped to its ends, then compare the
            // distance against the radius interpolated to that point.
            var t = MathHelper.Clamp(((x - x0) * dx + (y - y0) * dy) / lengthSquared, 0f, 1f);
            var px = x0 + dx * t;
            var py = y0 + dy * t;
            var radius = MathHelper.Lerp(r0, r1, t);

            var ox = x - px;
            var oy = y - py;
            if (ox * ox + oy * oy <= radius * radius) _shape[y * _width + x] = true;
        }
    }

    public void Ellipse(float cx, float cy, float rx, float ry)
    {
        for (var y = 0; y < _height; y++)
        for (var x = 0; x < _width; x++)
        {
            var dx = (x - cx) / MathF.Max(0.001f, rx);
            var dy = (y - cy) / MathF.Max(0.001f, ry);
            if (dx * dx + dy * dy <= 1f) _shape[y * _width + x] = true;
        }
    }

    /// <summary>A convex polygon, by half-plane test. Points must wind consistently.</summary>
    public void Polygon(params Vector2[] points)
    {
        if (points.Length < 3) return;

        for (var y = 0; y < _height; y++)
        for (var x = 0; x < _width; x++)
        {
            var inside = true;
            var sign = 0;

            for (var i = 0; i < points.Length && inside; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Length];
                var cross = (b.X - a.X) * (y - a.Y) - (b.Y - a.Y) * (x - a.X);

                var current = cross > 0f ? 1 : cross < 0f ? -1 : 0;
                if (current == 0) continue;
                if (sign == 0) sign = current;
                else if (sign != current) inside = false;
            }

            if (inside) _shape[y * _width + x] = true;
        }
    }

    public void Rect(float x0, float y0, float x1, float y1) => Polygon(
        new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1));

    /// <summary>Remove the current shape's coverage from an area, for notches and teeth.</summary>
    public void Erase(float cx, float cy, float rx, float ry)
    {
        for (var y = 0; y < _height; y++)
        for (var x = 0; x < _width; x++)
        {
            var dx = (x - cx) / MathF.Max(0.001f, rx);
            var dy = (y - cy) / MathF.Max(0.001f, ry);
            if (dx * dx + dy * dy <= 1f) _shape[y * _width + x] = false;
        }
    }

    // ------------------------------------------------------------------ commit

    /// <summary>
    /// Commit the scratch shape as solid material.
    /// </summary>
    /// <param name="roundness">
    /// Metres of thickness gained per pixel away from the shape's edge. High values give a
    /// tube — a haft, an arm; low values give a plate. Zero gives a flat plateau, which is what
    /// a cut facet wants.
    /// </param>
    /// <param name="cap">The most thickness this shape may reach, so a wide shape stays a slab.</param>
    /// <param name="lift">
    /// Thickness added everywhere under the shape, before rounding. This is how a part is
    /// placed *in front of* another: a guard lifted above a blade owns the pixels where they
    /// cross, without either needing to know about the other.
    /// </param>
    public void Fill(SpriteMaterial material, float roundness = 0.9f, float cap = 7f, float lift = 0f)
    {
        var id = Register(material);
        DistanceInsideShape();

        for (var i = 0; i < _surface.Length; i++)
        {
            if (!_shape[i]) continue;

            var thickness = lift + MathF.Min(cap, _distance[i] * roundness);

            // The taller shape owns the pixel. That is the whole depth test, and it means parts
            // can be laid down in any order as long as their lifts are right.
            if (thickness <= _surface[i]) continue;

            _surface[i] = thickness;
            _owner[i] = id;
        }
    }

    private int Register(SpriteMaterial material)
    {
        for (var i = 0; i < _materialCount; i++)
            if (ReferenceEquals(_materials[i], material)) return i;

        _materials[_materialCount] = material;
        return _materialCount++;
    }

    /// <summary>
    /// How far each covered pixel is from the nearest uncovered one, by two-pass chamfer.
    ///
    /// An exact Euclidean transform is not worth it here: a 3-4 chamfer is within a few percent
    /// over the handful of pixels a bevel spans, and it is two linear sweeps instead of a
    /// search. The result is the shape's own cross-section, which is what becomes its volume.
    /// </summary>
    private void DistanceInsideShape()
    {
        const float Straight = 1f;
        const float Diagonal = 1.41421356f;
        const float Far = 1e6f;

        for (var i = 0; i < _distance.Length; i++) _distance[i] = _shape[i] ? Far : 0f;

        float At(int x, int y) => x < 0 || y < 0 || x >= _width || y >= _height
            ? 0f
            : _distance[y * _width + x];

        for (var y = 0; y < _height; y++)
        for (var x = 0; x < _width; x++)
        {
            var index = y * _width + x;
            if (_distance[index] == 0f) continue;

            var best = _distance[index];
            best = MathF.Min(best, At(x - 1, y) + Straight);
            best = MathF.Min(best, At(x, y - 1) + Straight);
            best = MathF.Min(best, At(x - 1, y - 1) + Diagonal);
            best = MathF.Min(best, At(x + 1, y - 1) + Diagonal);
            _distance[index] = best;
        }

        for (var y = _height - 1; y >= 0; y--)
        for (var x = _width - 1; x >= 0; x--)
        {
            var index = y * _width + x;
            if (_distance[index] == 0f) continue;

            var best = _distance[index];
            best = MathF.Min(best, At(x + 1, y) + Straight);
            best = MathF.Min(best, At(x, y + 1) + Straight);
            best = MathF.Min(best, At(x + 1, y + 1) + Diagonal);
            best = MathF.Min(best, At(x - 1, y + 1) + Diagonal);
            _distance[index] = best;
        }
    }

    // ------------------------------------------------------------------ resolve

    /// <summary>
    /// Light the height field and quantise it into each material's ramp.
    ///
    /// The normal is read off the surface with finite differences, which is the same trick a
    /// bump map uses: where thickness changes fast the surface is steep, and a steep face
    /// pointing at the lamp is the bright one. Then the continuous result is thrown away and
    /// snapped to a ramp entry — the banding is the style, not an artefact of it.
    /// </summary>
    public Color[] Resolve(Vector3 lightDirection, bool outline = true)
    {
        var light = Vector3.Normalize(lightDirection);
        var pixels = new Color[_surface.Length];

        // Where a flat surface lands on the ramp.
        //
        // Without this every unsloped pixel — which is most of a sprite — sat wherever the
        // lamp's Z component happened to put it, and with the light tilted toward the viewer
        // that was three quarters of the way up the ramp. Whole figures came out bleached,
        // and the brighter the ramp's top end the worse it was. Re-centring so an unsloped
        // pixel is exactly the material's own colour makes the ramp mean what it says: base
        // where the surface faces you, lighter where it tilts into the lamp, darker where it
        // tilts away.
        var flat = Vector3.Dot(Vector3.UnitZ, light) * 0.5f + 0.5f;
        var recentre = flat - 0.5f;

        for (var y = 0; y < _height; y++)
        for (var x = 0; x < _width; x++)
        {
            var index = y * _width + x;
            var owner = _owner[index];

            if (owner < 0)
            {
                pixels[index] = outline && TouchesSolid(x, y)
                    ? OutlineAt(x, y)
                    : Color.Transparent;
                continue;
            }

            var material = _materials[owner]!;

            var dx = Surface(x + 1, y) - Surface(x - 1, y);
            var dy = Surface(x, y + 1) - Surface(x, y - 1);

            // The Z term sets how much the slope matters. Small values exaggerate the form,
            // which is what a sprite this size needs — real-scale normals read as flat.
            var normal = Vector3.Normalize(new Vector3(-dx, -dy, 1.6f));

            var diffuse = Vector3.Dot(normal, light) * 0.5f + 0.5f - recentre;
            var colour = material.Shade(diffuse);

            if (material.Gloss > 0f)
            {
                var spec = MathF.Pow(MathHelper.Clamp(diffuse, 0f, 1f), 1f + material.Gloss * 22f);
                if (spec > 0.62f) colour = Color.Lerp(colour, material.Highlight, (spec - 0.62f) * 2.4f);
            }

            pixels[index] = colour;
        }

        return pixels;
    }

    private float Surface(int x, int y) => x < 0 || y < 0 || x >= _width || y >= _height
        ? 0f
        : _surface[y * _width + x];

    private bool TouchesSolid(int x, int y) =>
        Surface(x - 1, y) > 0f || Surface(x + 1, y) > 0f ||
        Surface(x, y - 1) > 0f || Surface(x, y + 1) > 0f;

    /// <summary>
    /// The outline takes its colour from whatever it is outlining, so a gold rim and an iron
    /// rim are not the same black. A single black contour around everything is the other
    /// reliable tell of a generated set.
    /// </summary>
    private Color OutlineAt(int x, int y)
    {
        foreach (var (ox, oy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
        {
            var nx = x + ox;
            var ny = y + oy;
            if (nx < 0 || ny < 0 || nx >= _width || ny >= _height) continue;

            var owner = _owner[ny * _width + nx];
            if (owner >= 0) return _materials[owner]!.Outline;
        }

        return Color.Transparent;
    }
}
