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
