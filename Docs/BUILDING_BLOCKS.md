# Building blocks

One section per piece: what it is, how to call it, and what it will do to you if you assume.
Everything here is `Ember.*`; the global usings in `src/Ember.Engine/GlobalUsings.cs` mean you
rarely have to name the sub-namespace inside the engine, but a game project should `using
Ember;` plus whichever of `Ember.Render`, `Ember.Ui`, `Ember.Input`, `Ember.Audio` it needs.

---

## Host and frame

### `EngineHost`

`abstract class EngineHost : Game`. Subclass this instead of `Game`.

```csharp
protected EngineHost(string[] args, int logicalWidth, int logicalHeight, string title)
```

It owns the graphics device manager, the window, a **variable** timestep clamped to
`MaxFrameSeconds` (100 ms), the letterboxed `UiCanvas`, an `InputRouter`, and a `CaptureHost`.
It parses the command line for you.

| Member | What it does |
| --- | --- |
| `AttachCanvas(bodyFont, headingFont)` | Loads two `.ttf` files off disk and gives the canvas its device resources. Call from `LoadContent`. |
| `AttachScene(faults)` | Builds `Billboards`, `LitEffect`, `Ambience` and `Sounds`. Failures are appended to `faults` rather than thrown — a machine with no audio device should still get a playable game and a line saying what is missing. |
| `BeginHostFrame()` | First line of `Update`. Frame clock, capture warmup, fps counter. |
| `EndHostFrame(hold, exit)` | Last line of `Draw`. Writes any queued screenshot; quits once warmup is done and `hold` is false. |
| `RealSeconds(gameTime)` | Elapsed seconds, clamped. Use this, not `ElapsedGameTime`. |
| `LogicalMouse(mouse)` | Pointer in canvas space, so hit tests match what is drawn. |
| `SetBorderlessFullscreen(bool)` | Switches mode, resizes the canvas, calls `OnDisplayChanged`. |
| `OnDisplayChanged()` | Override to rebuild your projection after a resize. |
| `DisposeHost()` | Call from `UnloadContent`. Writes the `--perf` summary and disposes fonts and capture. |
| `HasArgument(args, "--x")` / `ParseOption(args, "--x")` | Read your own switches from the same command line. |

Protected fields worth knowing: `_ui`, `_input`, `_capture`, `_graphics`, `_uiScalePreference`,
`_framesPerSecond`, and the properties `Billboards`, `LitEffect`, `Ambience`, `Sounds`.

> `Sounds`, not `Sfx`. `Sfx` is the enum of effect ids, and a property of that name shadows it
> at every call site: `Sfx.Play(Sfx.Coin)` stops compiling the moment the property exists.

**Command line, free:** `--windowed`, `--perf`, `--screenshot <path>`, `--cover <path>`,
`--warmup <frames>`.

### `CaptureHost`

Screenshot warmup, an optional cover-sized render target (1260×1000), PNG write. `EngineHost`
constructs and drives it; you touch it directly only to `Queue(path)` a shot mid-run or to
check `IsCapturing` / `CoverMode`.

The warmup exists because the first frames of a MonoGame program are not representative — the
first `--screenshot` of the source game caught an unlit room. Default 4 frames, 30 minimum for
`--cover`.

> A capture run renders a few frames and exits with nobody watching. Anything that owns the
> screen — a consent dialog, a first-run prompt — must be suppressed when `IsCapturing`, or
> every store asset you generate is a picture of that dialog and looks fine until someone
> opens the file.

### `FirstPersonView`

Look, walk, jump, crouch. Knows nothing about your world.

```csharp
view.SetProjection(GraphicsDevice.Viewport.AspectRatio);   // also fieldOfView, near, far
view.Reset(position, yaw, pitch, standingEyeY);
MoveResult moved = view.Step(seconds, walk, lookPixels, collide);
view.RebuildView(shakeYaw, shakePitch);
```

- `WalkInput(Forward, Back, Left, Right, Sprint, Jump, HeldYaw, HeldPitch)` — buttons plus
  held-key turning. Mouse travel goes in separately as `lookPixels`.
- `ResolveWalk collide` — `(origin, delta, radius) => resolvedPosition`. `null` or
  `NoClip = true` walks through everything.
- `MoveResult(MetresWalked, Landed)` — how far the body *actually* moved, so a footstep does
  not play while you stand pushing into a wall.
- Tunables: `WalkSpeed`, `SprintSpeed`, `MouseSensitivity`, `KeyboardTurnSpeed`, `PitchLimit`,
  `CollisionRadius`, `Gravity`, `JumpSpeed`, `CrouchDrop`.

Two sign conventions that will bite:

1. **Yaw increases clockwise; `CreateRotationY` turns the other way**, so the look transform
   negates yaw. Get it wrong and mouse and strafe invert against each other.
2. **Camera forward is `(sin yaw, 0, -cos yaw)`.** Writing `(-sin yaw, …)` in a console `goto`
   command sent the player away from what they aimed at, and it read for an hour as a combat
   bug where standing close made enemies unhittable.

`Forward` is deliberately **unshaken**: aim, movement and the weapon read it, while
`RebuildView` applies shake to the view matrix only. Add shake anywhere else and a running
shake walks the player's aim off by degrees with nobody able to say why.

---

## Rendering

### `SceneRenderer`

Axis-aligned boxes, a crystal, a carved quad, a glow, and the two lighting paths. This is what
the world is made of.

```csharp
scene.Begin(LitEffect, view, projection, cameraPosition, cameraYaw, stonePalette, lights);
scene.DrawWorldBox(min, max, colour, material: "stone");
```

Per-frame state goes in once through `Begin` rather than on every draw — six arguments per call
is how a renderer ends up called wrongly from one of forty sites.

**Materials are strings:** `stone`, `timber`, `cloth`, `earth`, `rope`. Anything else is stone.
The engine does not import your material table; a different game sends the same names or falls
through. Colour alone could not say what a thing was made of — a timber counter, a cloth awning
and packed earth all came out as sandy brick until the material said otherwise.

`DrawWorldBox` decides *slab or wall* from the shape (`Y` much smaller than `X` and `Z`) and
picks the floor or wall texture accordingly. Coursed blockwork laid across a floor reads
instantly as a wall someone dropped, so this matters more than it sounds.

Anything authored near-black (`R+G+B` under the void threshold) is drawn as a hole, not a wall.

| Also | |
| --- | --- |
| `DrawCube`, `DrawTexturedCube` | untinted / tiled boxes |
| `DrawCrystal(centre, radius, colour, emissive, spin)` | eight facets, drawn emissive because it *is* the light |
| `DrawCarvedFace(centre, width, height, texture)` | a quad for a face or a sign |
| `DrawGlow(centre, radius, colour)` | a soft billboarded blob |
| `DrawVoid(centre, scale)` | a deliberate hole |
| `TintFor(colour)` | pulls an authored colour toward white so it modulates a texture instead of drowning it |

**Point lights** need a shader:

```csharp
if (scene.LoadCaveShader(Content, "Effects/CaveLighting") is { } fault)
    faults.Add($"cave lighting: {fault}");
scene.SetCaveAmbience(ambient, keyDirection, keyColour);
```

Without it, everything falls back to `BasicEffect` and `SetCaveAmbience` does nothing. The
shader must expose: `World`, `View`, `Projection`, `WorldInverseTranspose`, `DiffuseColour`,
`Surface`, `CameraPosition`, `PointCount`, `PointPosition[4]`, `PointColour[4]` (xyz colour,
w range), `AmbientColour`, `KeyDirection`, `KeyColour`. `MaxPointLights` is 4 and the nearest
are chosen per draw, because a torch across the room matters to the wall beside it and not at
all to the wall behind you.

> **The cube is wound so its outside faces the camera, and its normals point outward to match.**
> Those two facts have to agree. They disagreed for months in the source game: every box drew
> its interior and culled its exterior, and slabs hid it because a wall's two faces are
> centimetres apart. Change one without the other and every lit surface turns black.

### `ModelCache`

Load, measure, normalise and draw imported models (`.gltf`/`.fbx` through the MonoGame content
pipeline).

```csharp
models.Load(Content, "tree", "Models/tree_pineRoundA");
models.Draw("tree", position, scale, rotation, view, projection);
```

Normalisation is the point: models arrive at wildly different authored sizes, so each is
measured once and scaled to a unit, and `scale` then means metres rather than "whatever the
artist exported". `Errors` collects load failures instead of throwing — draw them on screen in
a debug build.

### `StaticMeshData` / `GltfPrimitiveImporter`

Read a static glTF scene into engine-owned CPU data:

```csharp
var imported = GltfSceneImporter.Load("Content/Props/crate.glb");
foreach (var nodeId in imported.MeshesByNodeId.Keys)
{
    var world = imported.Scene.GetWorldMatrix(nodeId);
}
```

`GltfSceneImporter` follows the GLB default scene, preserves each node's local position,
rotation, scale, and parent, and binds each mesh node to its imported primitives. Ask the
imported `SceneGraph` for its world matrix; the matrices use Ember's row-vector convention.
`StaticMeshData` stores position, normal, `TEXCOORD_0`, triangle indices, and a local AABB.
`Bounds3.Transform(matrix)` transforms all eight corners and returns an enclosing world AABB;
combine bounds with `Encapsulate` when framing a scene. `OrbitCamera.Frame(bounds)` targets its
center and fits its bounding sphere to the current field of view and aspect ratio.

The static import subset is deliberately narrow:

- The default scene and its reachable node hierarchy; nodes use position/rotation/scale
  transforms. Matrix-only transforms, skinned nodes, and animated models fail.
- Triangle primitives with `POSITION` and `NORMAL`; `TEXCOORD_0` is required for textured
  materials and optional for untextured ones. Other vertex attributes, morph targets, and other
  primitive topologies fail instead of being silently discarded. Missing indices mean
  sequential triangle indices.
- OPAQUE base-color materials with a factor and optional PNG/JPEG image using `TEXCOORD_0`.
  Alpha mask/blend, texture transforms, other UV sets, and unsupported image formats fail.
- No required glTF extensions are implemented. `extensionsRequired` entries and extensions
  SharpGLTF reports as incompatible fail before Ember creates scene objects. Optional extension
  metadata is not imported; those assets rely on their core glTF fallback data.

Metallic/roughness, normal, emissive, occlusion, alpha-cutout rendering, morph animation,
skinned deformation, and animation playback are outside this static draw path. Add support only
with a fixture that proves the imported result; do not assume a successful parse means every
visual feature was rendered.

`GltfMaterialData` reads base-color factors, double-sidedness, and resolved PNG/JPEG base-color
images for OPAQUE materials with TEXCOORD_0. It rejects blend/mask modes, other UV sets,
non-identity texture transforms, and unsupported image formats. It copies resolved image bytes
so the SharpGLTF model can be released independently; unresolved image references fail clearly.
The material reader and static draw path do not import metallic / roughness, normal, emissive,
alpha-cutout, or animation data.

### Skinned-character animation and drawing

`GltfSkinnedCharacterData.Import(model)` currently accepts one skinned mesh node in the default
scene. `GltfSkinData.Import(model, meshNode)` reads immutable node records, rest local
transforms, skin-order joint mapping, inverse-bind matrices, and the mesh-node rest world matrix.
It includes non-joint ancestors so transforms above either the skeleton or mesh are retained.
`GltfSkinWeightData.Import(primitive, vertexCount, jointCount)` reads `JOINTS_0` and
`WEIGHTS_0`, validates references and values, and normalizes each vertex's weights. The current
limit is four influences per vertex; additional joint/weight sets fail clearly. Skinned
primitives support triangles, POSITION, optional NORMAL (computed when absent), optional
TEXCOORD_0 when untextured, and OPAQUE base-color materials.

Each `GltfSkinPose` owns its mutable local transforms and calculates skin matrices from a pose.
`SkinnedMeshGpuBuffer` uploads the four influence slots and draws through MonoGame's
`SkinnedEffect`; this path validates Reach/HiDef and the current 72-joint limit before upload.
CharacterStudio's `--fox` option loads the bundled Fox fixture and draws its bind pose. Animation
clips import translation, rotation, and scale tracks with STEP or LINEAR interpolation;
CUBICSPLINE, morph-weight, and non-transform channels fail with a diagnostic.
`Evaluate(pose, time)` evaluates absolute clip time, starts from rest values for missing channels, and uses
shortest-path quaternion interpolation. The pose also retains its evaluated mesh-node world
matrix so animated mesh transforms reach both skinning and the draw transform. The static
`GltfSceneImporter` continues to reject skinned nodes rather than silently omitting deformation.

CharacterStudio accepts `--clip Walk` to select and loop an animation. Add `--pause --time 0.35`
to seek to a fixed pose, `--speed 1.5` to change playback rate, or `--no-loop` to stop at the
clip endpoint. `--play` and `--loop` are explicit forms of the default play/loop behavior when
`--clip` is selected. Selecting a clip without `--fox` loads the bundled Fox fixture; `--open`
can supply another skinned asset.

Each scene object that references the loaded skinned asset gets its own pose and playback clocks;
the imported character data, mesh buffers, and textures remain shared. `--pair --clip Walk
--second-clip Run --second-time 0.6 --pause-second` previews two Fox instances with independent
clip, time, speed, loop, and pause state. If `--second-clip` is omitted, CharacterStudio chooses
another available clip. `--crossfade Run --blend 0.5` samples a second clip and mixes both local
poses; callers control the blend amount over time. `--attach-hand` draws a colored preview cube
on the first instance's `b_RightHand_08` joint using a local offset. This is a rendering/API
demonstration. Scene format version 2 persists each character's playback state and named attachment
references. The preview loads each unique asset ID once and resolves the asset per scene object,
so static GLBs and shared skinned-character instances can appear together. Each skinned GLB is
still limited to the importer-supported single skinned mesh node.

Upload one mesh with `new StaticMeshGpuBuffer(device, mesh)` and call `Draw(effect, world,
view, projection)` for each instance. It owns its vertex and index buffers; register it with the
scene's `SceneResourceScope` so reload and shutdown dispose GPU resources. The
`samples/CharacterStudio` GLB preview exercises this complete static path. SharpGLTF.Core is
pinned at 1.0.7; imported vertices retain source coordinates and authored size and do not use
`ModelCache` normalization.

Attach a stable, project-relative source reference to a scene object with
`new GltfAssetReference(assetId, "Assets/Props/guard.glb")`. `SceneFile` saves its ID and path
as metadata beside the instance transform; it never serializes the imported vertices or
textures into scene JSON. Multiple instances may share one ID and path, while a scene may also
reference distinct asset IDs and paths. CharacterStudio loads the GLB for each unique ID once,
keeps that asset's GPU buffers and textures together, then draws each object through its own
referenced asset and authored transform. `Scenes/ReleaseAShowcase.json` demonstrates one static
environment plus two character instances sharing Fox.

`GltfAnimationBounds.SampleClip(character, clip)` deforms every source vertex through its four
normalized skin influences at intervals no larger than 1/30 second, then adds a margin equal to
the larger of 10% of the largest extent or 0.05 metres. CharacterStudio unions these per-clip
envelopes when it frames an animated character. The bounds cover the imported STEP/LINEAR clips;
a procedural pose without a matching sampled envelope must not be culled using these bounds.
The current renderer does not yet cull animated characters.

Scene format version 2 optionally stores each character object's clip name, time, speed, loop and
playing state, crossfade clip/blend, and stable bone-attachment IDs with joint-local matrices.
Version-1 files still load and are upgraded on the next save. CharacterStudio's `--save <path>`
stores the initial selected state, **S** saves current playback times, and `--open <path>` restores
the per-instance settings and attached preview props.

CharacterStudio's editor panel provides a text-entry field, a scene-object hierarchy keyed by
stable IDs, numeric local position, Euler rotation (degrees), and scale controls, and bounded
undo/redo history. Transform edits become one history entry when a field is committed. Create,
duplicate, and delete commands share that history; undoing deletion restores the object's asset
and character references plus its parent/child links. Duplicates get new object and attachment
IDs while retaining the imported GLB reference. The asset list shows GLBs already referenced by
the open scene, and **Place instance** adds another scene object pointing to the same source path.
It does not scan arbitrary project folders. A selected skinned instance exposes its imported clip
list, absolute-time scrubber, and playback toggle; changing those controls updates only that
instance and the version-2 scene settings saved by **S**.

`SceneLighting` applies one ambient color and normalized directional light to both `BasicEffect`
and `SkinnedEffect`. CharacterStudio exposes the ambient RGB, direction, and directional RGB as
runtime controls. The sample uses MonoGame's built-in effect shaders, so these paths do not need
an `.fx` content build or custom `.mgfx` files. Non-finite transform input is ignored; **S** saves
the edited scene. The panel uses pinned ImGui.NET 1.91.6.1 with a small MonoGame renderer backend.

`ReloadableAsset<T>` stages replacements through a factory. If importing or uploading throws,
the current resource stays active. After a complete candidate is built, it swaps the reference
and disposes the retired resource. CharacterStudio uses **R** to reimport all GLBs referenced by
the scene as one candidate preview; its status strip reports success or leaves an on-screen error
while the old preview remains usable.

### `BillboardRenderer`

Camera-facing cutout quads, for sprites standing in a 3D world.

```csharp
billboards.Begin(view, projection);
billboards.Draw(texture, feet, height, cameraYaw, tint);
```

Anchored at the **feet**, not the centre, because a sprite whose height changes — a recoil, a
crouch, a death — must not sink into the floor.

### The forges: `SpriteForge`, `FaceField`, and their catalogues

There is no artist here. Sprites and textures are generated in code, and the technique is worth
more than the specific art.

- **`SpriteForge`** — draw shapes (`Capsule`, `Ellipse`, `Polygon`, `Rect`) into a signed
  field, then resolve them through a `SpriteMaterial` (a colour ramp, an outline, optional
  gloss). One shading model for every sprite is why a character and the sword they are holding
  look like they belong in the same game.
- **`SpriteMaterial.FromBase(colour, steps, gloss)`** — a full ramp from one colour.
- **`CharacterSprites`** — palettes (`Bandit`, `Wolf`, `Citizen`, `Guard`, `Merchant`) and a
  cache keyed by name. Copy the shape of it; the names are Ratna Bay's.
- **`ItemSprites`** — worked examples of the same, including a faceted crystal.
- **`FaceField`** — a height-field face renderer: `Ellipsoid`, `Tube`, `Carve`, `Bump`,
  `Stain`, then `Resolve()` to pixels. Built for close-up portraits, where a sprite ramp reads
  as flat. `ResolveFresco` paints it as a wall painting rather than a lit sculpture.
- **`StoneTextures`** — `Wall`, `Floor`, `Timber`, `Cloth`, `Earth`, `Rope`, `Glow`, keyed by a
  three-colour `StonePalette` (`Granite` and `Sandstone` provided). All cached; call `Clear()`
  if you swap palettes at run time.
- **`PropTextures`** — props, plus a six-frame `Flame` animation.

---

## Interface

### `UiCanvas`

A 2D canvas in **logical pixels**. You lay out once against a fixed size (1280×720 by default)
and it scales and letterboxes to whatever the display is.

```csharp
_ui.Begin();
_ui.Panel(bounds, fill, border);
_ui.Row(bounds, fill, border);
_ui.Text("hello", position, scale, colour);
_ui.TextFit(value, position, maxWidth, scale, colour);      // shrinks to fit
_ui.TextWrapped(value, position, maxWidth, scale, colour);  // returns height used
_ui.TextCentred / TextRight / TextFitCentred
_ui.MeasureText(value, scale);
_ui.End();
```

Text is rasterised at the current scale so glyphs land 1:1 on the display instead of being
resampled — that is what `Resize(viewport, preference)` is recalculating. Colours are always
arguments: the canvas does not know your palette.

### `UiTheme`

The palette, named by *role* rather than hue: `Panel`, `PanelRaised`, `PanelSheer`, `Scrim`,
`Border`, `BorderDim`, `Accent`, `GoldDim`, `Row(selected)`, `RowText(selected)`.

These were literals once — one colour appeared twenty-five times across the screens, and a
missed copy did not fail a build, it just left one panel a different shade of teal that nobody
reports and nobody can find.

### `UiLayout`

Trimmed here to the canvas size and the prompt rectangles (`Ember` owns this file; it is not
synced). **Take the pattern, not the numbers:** one table of rectangles that both the renderer
and the hit test read. A clickable row that is not exactly the row on screen is how a menu
starts ignoring the mouse, and that happens when the drawing code and the input code each hold
their own copy of a number.

### `WorldProjector`

World point → logical canvas point, for nameplates, floating damage and markers.

```csharp
var projector = new WorldProjector(GraphicsDevice.Viewport, view, projection,
                                   _ui.Scale, _ui.LogicalWidth, _ui.LogicalHeight);
if (projector.TryProject(head, out var anchor)) { /* draw at anchor */ }
```

Project in the same frame you draw, with the same camera. Project with last frame's camera and
markers lag the world by exactly one frame, which reads as jitter nobody can source.

### `PromptRenderer` / `PromptState`

The "press E" strip. `PromptState` is a list of `PromptChip(Line, Bounds, Role, …)`; the
renderer paints it and knows nothing else.

### `FramePresenter`

Draw order for the 2D pass, as two static methods taking callbacks: combat HUD under panels,
then toasts, panels, watches, console. It holds the *order* so the order lives in one place —
it does not hold any of your screens.

---

## Input

### `InputRouter`

**One keyboard and mouse read per frame.**

```csharp
_input.Sample();
var keyboard = _input.CurrentKeyboard;
if (_input.Pressed(keyboard, Keys.Space)) { … }   // edge, not level
if (_input.Clicked(mouse)) { … }
_input.Commit();          // or CommitKeyboard() / CommitMouse() separately
```

Two `GetState()` calls in one frame can disagree, and a `Pressed` check against a stale
snapshot fires twice or never. Sample once, commit once.

### `ListPicker`

Selection for any list or grid, with the bounds supplied as a callback so it never needs to
know your layout.

```csharp
var pick = ListPicker.Step(selection, _input, keyboard, mouse, pointer, count,
                           row: i => UiLayout.MenuItem(i), axis: ListAxis.Vertical, wrap: true);
```

Returns `ListPick(Selection, Hovered, KeyboardMoved)`. `StepGrid` walks a grid; `DigitIndex`
reads the 1–9 shortcuts; `Hovered` does a bare hit test. `KeyboardMoved` is there so you can
suppress the mouse hover that would otherwise fight the arrow keys.

### `ConsoleInput`

The typing half of a developer console: buffer, history cursor, open/closed. `Step` returns a
`ConsoleAction` (`None`, `Toggle`, `Close`, `Submit`, `Complete`, `HistoryUp`, `HistoryDown`) and
leaves *what a line means* to the router below.

---

## Scripting

### `ConsoleRouter` (`Ember.Scripting`, no MonoGame)

Register commands, run lines, run whole scripts.

```csharp
var router = new ConsoleRouter();
router.Register("teleport", "teleport <x> <y> <z>", "Move the player.",
    args => { Move(args.Number(0), args.Number(1), args.Number(2)); return "Moved."; });

foreach (var line in router.Execute("teleport 4 1.7 -8")) Print(line);
```

- `ConsoleArgs` does the parsing: `Text(i, fallback)`, `Number`, `Integer`, `TryNumber`,
  `Switch(i)` for on/off, `Rest(i)` for everything after an index. A console that throws on a
  typo is a console people stop using.
- `Complete(prefix)` for tab completion, `History` for the up arrow, `Help(name)` for `help`.
- `SplitStatements(text)`, `ReadScript(lines)` and `Tokenise(statement)` turn a file into
  statements — `#` comments and blank lines dropped.
- **`UnknownCommands(statements)` before you run anything.** A script naming a command nothing
  registered was written against a different build; finding that out at statement forty means
  the thirty-nine asserts above it already reported success on a run that was never going to
  finish. Check the whole script first, fail the process, exit non-zero.

This is the piece that turns a game into something testable without a human: a script that
walks to a place, asserts what is there, and exits 1 if it is not, is a build gate. Make `wait`
take **seconds**, not frames — capture mode runs uncapped, and a frame count buys an
unpredictable and usually tiny slice of game time.

---

## Audio

### `SoundBank`

Synthesised effects, no `.wav` files. `Sfx` covers `Swing`, `HitFlesh`, `Block`, `Death`,
`Hurt`, `Cast`, `Door`, `Coin`, `Chime`, `Denied`, `Step`, `Land`.

```csharp
Sounds?.Play(Sfx.Coin, weight: 0.5f, volumeScale: 1f);
```

`weight` picks between the variations built for each id, so a repeated footstep is not the
identical sample twelve times. `Create(out fault)` returns a bank and a message rather than
throwing; `Dump(directory)` writes every sound to disk, which is how you audition them without
playing.

### `SoundForge`

How those are made: `Noise`, `Tone`, `Whoosh`, then `Declick()` and `Normalise()` before
`ToPcm()`. `Declick` is not optional — a buffer that starts or ends mid-waveform pops, and the
pop is the loudest thing in the effect.

### `AmbientAudio`

A looping ambient bed. `TryStart(out ambient, out fault)` — false with a message on a machine
with no audio device, which must not be fatal.

---

## The sample

`samples/FirstLight` is 178 lines and uses: `EngineHost`, `AttachCanvas`, `AttachScene`,
`BeginHostFrame`/`EndHostFrame`, `FirstPersonView` with a collision callback, `SceneRenderer`
with the `BasicEffect` fallback, `UiCanvas`, `InputRouter`, and `--screenshot`.

Keep it in the solution. It is the only thing that can tell you a change to the engine has
broken a game's ability to compile against it, and it is louder than a document going stale.
