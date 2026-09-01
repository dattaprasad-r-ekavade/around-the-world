using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace Ember.Render;

/// <summary>One lamp in the world. Rebuilt every frame from whatever is currently burning.</summary>
public readonly record struct PointLight(Vector3 Position, Vector3 Colour, float Range);

/// <summary>
/// Everything the world is actually made of: boxes, a quad, an octahedron, and the two shaders
/// that light them.
///
/// **The second piece lifted out of <c>Game1</c>, and the one a different game would reuse
/// unchanged.** Nothing here knows about mines, runs, stones or the fort. It knows how to turn
/// an axis-aligned box and a material name into lit geometry, which is the whole of what the
/// authored world and every generated one are built from.
///
/// **The cube is wound so its outside faces the camera**, and its normals point outward to
/// match. Those two facts have to agree: they disagreed for months, so under
/// <c>CullCounterClockwiseFace</c> every box in the game drew its interior and culled its
/// exterior. Slabs hid it — a wall's inner and outer face are centimetres apart — and it was
/// unmissable the moment a box had depth. Change one without the other and every lit surface
/// turns black.
///
/// **Per-frame state is set once through <see cref="Begin"/>** rather than threaded through
/// every call. Six arguments on every draw is how a renderer ends up being called wrongly from
/// one of forty sites; one setup call per frame cannot be got wrong in only some of them.
/// </summary>
public sealed class SceneRenderer
{
    /// <summary>
    /// How many point lights the cave shader can take.
    ///
    /// Four, and it is a hard ceiling rather than a taste decision: the Reach profile allows
    /// 32 constant registers, and eight lights across three arrays did not fit. The range
    /// rides in the w component of the colour so two arrays carry what three used to.
    /// </summary>
    public const int MaxPointLights = 4;

    /// <summary>Below this total, an authored colour is a void rather than a surface.</summary>
    private const int VoidBrightness = 96;

    /// <summary>How many metres one tile of masonry covers.</summary>
    private const float StoneTileMetres = 2.2f;

    private readonly GraphicsDevice _device;

    private readonly VertexPositionNormalTexture[] _cube = new VertexPositionNormalTexture[24];
    private readonly short[] _cubeIndices = new short[36];

    /// <summary>Scratch copy of the cube, rebuilt per draw when its UVs have to be scaled.</summary>
    private readonly VertexPositionNormalTexture[] _texturedCube = new VertexPositionNormalTexture[24];

    private VertexPositionNormalTexture[] _crystal = Array.Empty<VertexPositionNormalTexture>();
    private short[] _crystalIndices = Array.Empty<short>();

    private readonly VertexPositionNormalTexture[] _quad = new VertexPositionNormalTexture[4];
    private readonly short[] _quadIndices = { 0, 1, 2, 0, 2, 3 };

    /// <summary>The glow quad's own vertices: camera-facing, so rebuilt per draw.</summary>
    private readonly VertexPositionNormalTexture[] _glow = new VertexPositionNormalTexture[4];

    private readonly Vector3[] _lightPositions = new Vector3[MaxPointLights];
    private readonly Vector4[] _lightColours = new Vector4[MaxPointLights];

    // --- per frame, set by Begin ---------------------------------------------------
    private BasicEffect _effect = null!;
    private Matrix _view;
    private Matrix _projection;
    private Vector3 _cameraPosition;
    private float _cameraYaw;
    private StoneTextures.StonePalette _stone;
    private List<PointLight> _lights = new();

    public SceneRenderer(GraphicsDevice device)
    {
        _device = device;
        BuildCube();
        BuildCrystal();
    }

    /// <summary>The cave shader, or null while it is still the fixed-function pipeline.</summary>
    public Effect? CaveEffect { get; private set; }

    /// <summary>
    /// Load the cave shader, reporting why rather than whether it failed.
    ///
    /// A missing .xnb and a shader the profile will not compile are entirely different
    /// problems with the same symptom -- flat lighting -- so the exception type is the whole
    /// diagnostic. Returns null on success, and the game runs on BasicEffect either way: a
    /// missing shader must never be the difference between a game and a black screen.
    /// </summary>
    public string? LoadCaveShader(ContentManager content, string assetPath)
    {
        try
        {
            CaveEffect = content.Load<Effect>(assetPath);
            return null;
        }
        catch (Exception exception)
        {
            CaveEffect = null;
            return exception.GetType().Name;
        }
    }

    /// <summary>Everything that changes once a frame, rather than once a draw.</summary>
    public void Begin(BasicEffect effect, Matrix view, Matrix projection, Vector3 cameraPosition,
        float cameraYaw, StoneTextures.StonePalette stone, List<PointLight> lights)
    {
        _cameraYaw = cameraYaw;
        _effect = effect;
        _view = view;
        _projection = projection;
        _cameraPosition = cameraPosition;
        _stone = stone;
        _lights = lights;
    }

    /// <summary>Set the shader's ambient and directional fill for the room being drawn.</summary>
    public void SetCaveAmbience(Vector3 ambient, Vector3 keyDirection, Vector3 keyColour)
    {
        if (CaveEffect is null) return;

        CaveEffect.Parameters["AmbientColour"].SetValue(ambient);
        CaveEffect.Parameters["KeyDirection"].SetValue(Vector3.Normalize(keyDirection));
        CaveEffect.Parameters["KeyColour"].SetValue(keyColour);
    }

    // ------------------------------------------------------------------ world geometry

    /// <summary>
    /// One axis-aligned box, textured by a material name and tinted by its authored colour.
    ///
    /// Material is a string on purpose: <c>stone</c>, <c>timber</c>, <c>cloth</c>, <c>earth</c>,
    /// <c>rope</c>. Anything else is stone. The engine does not import the game's material
    /// table — a different game can send the same names or fall through to stone.
    /// </summary>
    public void DrawWorldBox(Vector3 min, Vector3 max, Color color, string material = "stone")
    {
        var centre = new Vector3(
            (min.X + max.X) * 0.5f,
            (min.Y + max.Y) * 0.5f,
            (min.Z + max.Z) * 0.5f);
        var scale = new Vector3(max.X - min.X, max.Y - min.Y, max.Z - min.Z);

        // A slab is anything much wider than it is tall: floors, ceilings, and the lintels over
        // doorways. Everything else is a wall. Getting this wrong is very visible — coursed
        // blockwork laid across a floor reads immediately as a wall someone dropped.
        var isSlab = scale.Y < scale.X * 0.5f && scale.Y < scale.Z * 0.5f;

        // Colour alone could not say what a thing was made of: every surface was blockwork
        // tinted toward its authored colour, so the yard's timber counter, cloth awning and
        // packed-earth ground all came out as sandy brick. The material says it instead.
        var texture = material switch
        {
            "timber" => StoneTextures.Timber(_device),
            "cloth" => StoneTextures.Cloth(_device),
            "earth" => StoneTextures.Earth(_device),
            "water" => StoneTextures.Water(_device),
            "rope" => StoneTextures.Rope(_device),
            _ => isSlab
                ? StoneTextures.Floor(_device, _stone)
                : StoneTextures.Wall(_device, _stone)
        };

        // Planks and weave carry their own colour, so tinting them by the authored colour
        // would put the sandstone back over the thing that just stopped being it.
        var painted = material is "stone" or null or "" ? TintFor(color) : Color.White;

        // A rope is a few centimetres across and a couple of metres long. At the stone tiling
        // its whole length is a fraction of one texture repeat, which is what drew plank grain
        // down the windlass rope and made it read as a hanging board.
        var tiling = material switch
        {
            "rope" => 0.24f,
            "water" => 1.1f,
            "timber" or "cloth" => StoneTileMetres * 0.5f,
            _ => StoneTileMetres
        };

        // Something authored almost black is meant to be a hole, not a wall. TintFor pulls
        // every colour toward white so it modulates the texture rather than drowning it, which
        // turns a deliberate void into pale blockwork.
        if (color.R + color.G + color.B < VoidBrightness)
        {
            DrawVoid(centre, scale);
            return;
        }

        DrawTexturedCube(centre, scale, painted, texture, tiling);
    }

    /// <summary>
    /// The manifest's colour, pulled toward white so it modulates the texture instead of
    /// drowning it. A mid-grey tint over mid-grey stone lands at quarter brightness otherwise,
    /// and every room goes black the moment it is textured.
    /// </summary>
    public static Color TintFor(Color authored) => new(
        (byte)(155 + authored.R * 0.39f),
        (byte)(155 + authored.G * 0.39f),
        (byte)(155 + authored.B * 0.39f));

    /// <summary>
    /// A hole: drawn with the lighting switched off entirely.
    ///
    /// Painting it near-black was not enough. The bottom of the shaft sits thirteen metres
    /// under a lantern with a twenty-six metre range, and a lit surface takes that light
    /// whatever colour it was authored — so looking down the mine showed a bright yellow floor,
    /// which is the exact opposite of what a void is for.
    /// </summary>
    public void DrawVoid(Vector3 centre, Vector3 scale)
    {
        _effect.LightingEnabled = false;
        DrawCube(centre, scale, new Color(9, 9, 11), 0f);
        _effect.LightingEnabled = true;
    }

    public void DrawCube(Vector3 position, Vector3 scale, Color color, float rotation)
    {
        _effect.World = Matrix.CreateScale(scale)
            * Matrix.CreateRotationY(rotation)
            * Matrix.CreateTranslation(position);
        _effect.View = _view;
        _effect.Projection = _projection;
        _effect.DiffuseColor = color.ToVector3();
        _effect.Alpha = color.A / 255f;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList, _cube, 0, _cube.Length,
                _cubeIndices, 0, _cubeIndices.Length / 3);
        }
    }

    public void DrawTexturedCube(Vector3 position, Vector3 scale, Color tint,
        Texture2D texture, float metresPerTile)
    {
        Array.Copy(_cube, _texturedCube, _cube.Length);

        // Face order matches BuildCube: +Z, -Z, -X, +X, +Y, -Y.
        var faceSize = new[]
        {
            new Vector2(scale.X, scale.Y),
            new Vector2(scale.X, scale.Y),
            new Vector2(scale.Z, scale.Y),
            new Vector2(scale.Z, scale.Y),
            new Vector2(scale.X, scale.Z),
            new Vector2(scale.X, scale.Z)
        };

        for (var face = 0; face < 6; face++)
        {
            var tiles = faceSize[face] / MathHelper.Max(0.01f, metresPerTile);
            for (var vertex = 0; vertex < 4; vertex++)
                _texturedCube[face * 4 + vertex].TextureCoordinate *= tiles;
        }

        var world = Matrix.CreateScale(scale) * Matrix.CreateTranslation(position);

        // Wrap, or the UV scaling above simply clamps and every face becomes one stretched
        // brick. Linear rather than point: at 720p a 256-pixel tile repeated down a corridor
        // aliases into noise without it.
        _device.SamplerStates[0] = SamplerState.LinearWrap;

        if (CaveEffect is not null)
        {
            DrawWithCaveLighting(world, tint, texture, _texturedCube, _cubeIndices);
            return;
        }

        _effect.World = world;
        _effect.View = _view;
        _effect.Projection = _projection;
        _effect.TextureEnabled = true;
        _effect.Texture = texture;
        _effect.DiffuseColor = tint.ToVector3();
        _effect.Alpha = 1f;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList, _texturedCube, 0, _texturedCube.Length,
                _cubeIndices, 0, _cubeIndices.Length / 3);
        }

        _effect.TextureEnabled = false;
        _effect.Texture = null;
    }

    /// <summary>
    /// Draw geometry through the cave shader, with the nearest lights bound.
    ///
    /// The lights are chosen per draw rather than per frame because "nearest" is only
    /// meaningful relative to something: a torch across the room matters to the wall beside it
    /// and not at all to the wall behind the player.
    /// </summary>
    private void DrawWithCaveLighting(Matrix world, Color tint, Texture2D texture,
        VertexPositionNormalTexture[] vertices, short[] indices)
    {
        var effect = CaveEffect!;
        var centre = Vector3.Transform(Vector3.Zero, world);

        _lights.Sort((a, b) =>
            Vector3.DistanceSquared(a.Position, centre)
                .CompareTo(Vector3.DistanceSquared(b.Position, centre)));

        var count = Math.Min(MaxPointLights, _lights.Count);
        for (var i = 0; i < count; i++)
        {
            _lightPositions[i] = _lights[i].Position;
            _lightColours[i] = new Vector4(_lights[i].Colour, _lights[i].Range);
        }

        effect.Parameters["World"].SetValue(world);
        effect.Parameters["View"].SetValue(_view);
        effect.Parameters["Projection"].SetValue(_projection);
        effect.Parameters["WorldInverseTranspose"].SetValue(Matrix.Transpose(Matrix.Invert(world)));
        effect.Parameters["DiffuseColour"].SetValue(tint.ToVector3());
        effect.Parameters["Surface"].SetValue(texture);
        effect.Parameters["CameraPosition"].SetValue(_cameraPosition);
        effect.Parameters["PointCount"].SetValue(count);

        if (count > 0)
        {
            effect.Parameters["PointPosition"].SetValue(_lightPositions);
            effect.Parameters["PointColour"].SetValue(_lightColours);
        }

        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList, vertices, 0, vertices.Length,
                indices, 0, indices.Length / 3);
        }
    }

    /// <summary>A jiva stone: eight facets, drawn emissive because it is the light, not lit by it.</summary>
    public void DrawCrystal(Vector3 centre, float radius, Color colour, Vector3 emissive, float spin)
    {
        var previousEmissive = _effect.EmissiveColor;

        _effect.World = Matrix.CreateScale(radius)
            * Matrix.CreateRotationZ(0.32f)
            * Matrix.CreateRotationY(spin)
            * Matrix.CreateTranslation(centre);
        _effect.View = _view;
        _effect.Projection = _projection;
        _effect.DiffuseColor = colour.ToVector3();
        _effect.EmissiveColor = emissive;
        _effect.Alpha = colour.A / 255f;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList, _crystal, 0, _crystal.Length,
                _crystalIndices, 0, _crystalIndices.Length / 3);
        }

        _effect.EmissiveColor = previousEmissive;
    }

    /// <summary>
    /// A texture lying flat on a surface that faces the camera down -Z, lit like everything
    /// else in the scene.
    ///
    /// The carved verse used to go through <see cref="BillboardRenderer"/>, and that was wrong
    /// twice over. A billboard turns to face the camera, so the writing slid off a flat pillar
    /// as the shot moved; and <c>AlphaTestEffect</c> is unlit, so the band stayed at full
    /// brightness while the stone around it fell into shadow.
    /// </summary>
    public void DrawCarvedFace(Vector3 centre, float width, float height, Texture2D texture)
    {
        var halfWidth = width * 0.5f;
        var halfHeight = height * 0.5f;
        var normal = Vector3.Backward;

        _quad[0] = new VertexPositionNormalTexture(
            centre + new Vector3(-halfWidth, halfHeight, 0f), normal, new Vector2(0f, 0f));
        _quad[1] = new VertexPositionNormalTexture(
            centre + new Vector3(halfWidth, halfHeight, 0f), normal, new Vector2(1f, 0f));
        _quad[2] = new VertexPositionNormalTexture(
            centre + new Vector3(halfWidth, -halfHeight, 0f), normal, new Vector2(1f, 1f));
        _quad[3] = new VertexPositionNormalTexture(
            centre + new Vector3(-halfWidth, -halfHeight, 0f), normal, new Vector2(0f, 1f));

        var wasTextured = _effect.TextureEnabled;
        _effect.World = Matrix.Identity;
        _effect.View = _view;
        _effect.Projection = _projection;

        // The texture carries the stone's colour, so the diffuse term has to be neutral or it
        // would be tinted twice.
        _effect.TextureEnabled = true;
        _effect.Texture = texture;
        _effect.DiffuseColor = Vector3.One;
        _effect.Alpha = 1f;

        _device.SamplerStates[0] = SamplerState.LinearClamp;

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList, _quad, 0, 4, _quadIndices, 0, 2);
        }

        _effect.TextureEnabled = wasTextured;
        _effect.Texture = null;
    }

    /// <summary>
    /// An unlit additive quad, for the pool of light a flame throws onto the surface behind it.
    ///
    /// This is the honest stopgap for having no point lights everywhere, and it is still scene
    /// geometry: world-space, drawn through the same view and projection as the walls it lands
    /// on. What is different is only its state — additive, unlit, and depth-read rather than
    /// depth-write, so two overlapping torches do not punch holes in each other and a glow
    /// behind a wall stays behind it.
    ///
    /// It turns to face the camera, which is why its vertices are rebuilt per draw rather than
    /// built once like the cube.
    /// </summary>
    public void DrawGlow(Vector3 centre, float radius, Color colour)
    {
        var previousBlend = _device.BlendState;
        var previousDepth = _device.DepthStencilState;

        _effect.World = Matrix.Identity;
        _effect.View = _view;
        _effect.Projection = _projection;
        _effect.TextureEnabled = true;
        _effect.Texture = StoneTextures.Glow(_device);
        _effect.LightingEnabled = false;
        _effect.DiffuseColor = colour.ToVector3();
        _effect.Alpha = colour.A / 255f;

        _device.BlendState = BlendState.Additive;
        _device.DepthStencilState = DepthStencilState.DepthRead;
        _device.SamplerStates[0] = SamplerState.LinearClamp;

        var right = new Vector3(MathF.Cos(_cameraYaw), 0f, MathF.Sin(_cameraYaw)) * radius;
        var up = Vector3.Up * radius;

        _glow[0] = new VertexPositionNormalTexture(centre - right + up, Vector3.Forward, new Vector2(0f, 0f));
        _glow[1] = new VertexPositionNormalTexture(centre + right + up, Vector3.Forward, new Vector2(1f, 0f));
        _glow[2] = new VertexPositionNormalTexture(centre + right - up, Vector3.Forward, new Vector2(1f, 1f));
        _glow[3] = new VertexPositionNormalTexture(centre - right - up, Vector3.Forward, new Vector2(0f, 1f));

        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserIndexedPrimitives(
                PrimitiveType.TriangleList, _glow, 0, 4, _quadIndices, 0, 2);
        }

        _effect.LightingEnabled = true;
        _effect.TextureEnabled = false;
        _effect.Texture = null;
        _device.BlendState = previousBlend;
        _device.DepthStencilState = previousDepth;
    }

    // ------------------------------------------------------------------ the meshes

    private void BuildCube()
    {
        var positions = new[]
        {
            new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
            new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f)
        };

        // Outward normals, matching the winding below. These two have to agree — see the note
        // on the class. Fixing one without the other turns every lit surface black.
        var normals = new[]
        {
            Vector3.Backward, Vector3.Forward, Vector3.Left, Vector3.Right, Vector3.Up, Vector3.Down
        };

        for (var face = 0; face < 6; face++)
        {
            for (var vertex = 0; vertex < 4; vertex++)
            {
                _cube[face * 4 + vertex] = new VertexPositionNormalTexture(
                    positions[face * 4 + vertex],
                    normals[face],
                    new Vector2(vertex == 1 || vertex == 2 ? 1f : 0f, vertex >= 2 ? 0f : 1f));
            }

            // Wound so the *outside* of each face is the one presented to the camera.
            var index = face * 6;
            var vertexIndex = face * 4;
            _cubeIndices[index] = (short)vertexIndex;
            _cubeIndices[index + 1] = (short)(vertexIndex + 2);
            _cubeIndices[index + 2] = (short)(vertexIndex + 1);
            _cubeIndices[index + 3] = (short)vertexIndex;
            _cubeIndices[index + 4] = (short)(vertexIndex + 3);
            _cubeIndices[index + 5] = (short)(vertexIndex + 2);
        }
    }

    /// <summary>
    /// An octahedron with flat shading: every triangle carries its own three vertices so each
    /// facet gets one normal. Sharing vertices would average the normals and smooth the stone
    /// back into a ball, which is the one thing it must not look like.
    /// </summary>
    private void BuildCrystal()
    {
        var top = new Vector3(0f, 1f, 0f);
        var bottom = new Vector3(0f, -1f, 0f);
        var waist = new[]
        {
            new Vector3(1f, 0f, 0f),
            new Vector3(0f, 0f, 1f),
            new Vector3(-1f, 0f, 0f),
            new Vector3(0f, 0f, -1f)
        };

        var triangles = new List<(Vector3 A, Vector3 B, Vector3 C)>();
        for (var i = 0; i < 4; i++)
        {
            var a = waist[i];
            var b = waist[(i + 1) % 4];
            triangles.Add((top, a, b));
            triangles.Add((bottom, b, a));
        }

        _crystal = new VertexPositionNormalTexture[triangles.Count * 3];
        _crystalIndices = new short[triangles.Count * 3];

        for (var t = 0; t < triangles.Count; t++)
        {
            var (a, b, c) = triangles[t];
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            var baseIndex = t * 3;

            _crystal[baseIndex] = new VertexPositionNormalTexture(a, normal, Vector2.Zero);
            _crystal[baseIndex + 1] = new VertexPositionNormalTexture(b, normal, Vector2.UnitX);
            _crystal[baseIndex + 2] = new VertexPositionNormalTexture(c, normal, Vector2.One);

            _crystalIndices[baseIndex] = (short)baseIndex;
            _crystalIndices[baseIndex + 1] = (short)(baseIndex + 1);
            _crystalIndices[baseIndex + 2] = (short)(baseIndex + 2);
        }
    }
}
