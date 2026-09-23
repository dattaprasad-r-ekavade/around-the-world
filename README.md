# Ember

A small first-person game engine for MonoGame — and the building blocks that came with it.

**This repository is the engine.** Ember lives here and is edited here: `src/Ember.Engine` and
`src/Ember.Scripting` are owned in this repo, not mirrored from anywhere. The default workflow
is to open a file under `src/` and change it.

It is not a general-purpose engine and does not want to be. It is the reusable half of a
shipping game — Ratna Bay, a first-person roguelike — lifted out after the fact, with
everything that named that game left behind. Every piece here has been run for real: the frame
loop, the walk, the lighting, the procedural textures and the synthesised audio are the ones a
finished game uses, not a demo's.

Two things in this repo are games built on the engine, and they are consumers of it, never part
of it:

- **`samples/FirstLight`** — the contract sample. Under two hundred lines, it defines what a
  game may assume about the engine, and it fails the build the day a change to the engine stops
  a game from compiling against it. Read it before writing anything.
- **`samples/Campaign`** — a real game on the engine (first-slice design in
  `samples/Campaign/Docs/DESIGN.md`). It references the engine the same way any outside game
  would; nothing in `src/` may reference it.

**`samples/CharacterStudio`** is the engine's scene-preview sample. Its Release A showcase
loads a static environment GLB and two scene instances sharing a skinned character GLB, with
independent clip state, a saved hand attachment, versioned scene open/save, and screenshot
capture. Its editor panel lists scene objects, edits local transforms with undo/redo, creates,
duplicates and deletes objects, places new instances of GLBs already referenced by the scene,
controls the selected character's clip, adjusts shared ambient/directional lighting, and renders
static/skinned directional shadows with draw-count and static-culling diagnostics. **P / Play on
clone** runs the current scene copy; **E / Interact** plays a sample imported chime, and **P / Stop
and restore** discards play-mode edits. Behavior assignments and character physics are not yet
saved/wired into this editor. A general project asset browser and broader gameplay tools remain on
the roadmap. The supported GLB subset is documented in
[`Docs/BUILDING_BLOCKS.md`](Docs/BUILDING_BLOCKS.md).

**What you get:** a window, a variable timestep, a first-person camera with collision as a
callback, fixed-step 3D physics with a controllable capsule, a third-person obstruction camera,
jump/slope handling, named keyboard actions, motion-driven character clips, interpolation, and
layer-filtered raycasts; compiled scene behaviours and scene-owned imported audio; box and model rendering with two lighting paths; procedurally generated wall, floor,
timber and cloth textures, procedurally generated character and item sprites, procedurally
synthesised sound effects and an ambient bed, a 2D canvas with fonts and layout, input sampling
with press-detection, list and grid pickers, a console command router, and screenshot capture
you can drive from the command line.

**What you write:** your game.

---

## Quick start

Requires the .NET 9 SDK on Windows. (Windows-only today: the engine is built on MonoGame's
WindowsDX backend and uses WinForms for the window. Moving to DesktopGL is a backend swap, not
a rewrite of anything here.)

```powershell
dotnet build Ember.sln
```

### Running FirstLight

FirstLight is a windowed WinExe; run its built DLL with `dotnet`:

```powershell
# A room, a lamp, and someone standing in it.
dotnet samples\FirstLight\bin\Debug\net9.0-windows\win-x64\FirstLight.dll --windowed

# The same thing, photographed and exited, with no human present.
dotnet samples\FirstLight\bin\Debug\net9.0-windows\win-x64\FirstLight.dll --screenshot room.png
```

(If you built Release, swap `Debug` for `Release` in the path.)

Read `samples/FirstLight/FirstLightGame.cs` start to finish before you write anything. It is
under two hundred lines and it is the whole contract between the engine and a game. Then copy
it and start deleting.

### Running Campaign

Campaign is a game, not part of the engine — run it the same way:

```powershell
dotnet samples\Campaign\bin\Debug\net9.0-windows\win-x64\Campaign.dll --windowed
```

### Running CharacterStudio

Open the saved mixed environment/character scene in the editor sample:

```powershell
dotnet samples\CharacterStudio\bin\Debug\net9.0-windows\win-x64\CharacterStudio.dll --open samples\CharacterStudio\Scenes\ReleaseAShowcase.json
```

Press **P** to run an isolated scene copy, **E** to play the sample interaction sound, and **P**
again to stop and restore the authored scene. The editor panel also offers these actions and the
interaction volume slider.

---

## The repository

```
src/Ember.Engine/       The engine. MonoGame, WinForms, FontStashSharp. Windows.
  Engine/               EngineHost, CaptureHost, FirstPersonView
  Render/               SceneRenderer, ModelCache, BillboardRenderer, and the texture forges
  Ui/                   UiCanvas, UiTheme, UiLayout, WorldProjector, FramePresenter, prompts
  Input/                InputRouter, ListPicker, ConsoleInput
  Audio/                SoundBank, SoundForge, AmbientAudio

src/Ember.Scripting/    ConsoleRouter. No MonoGame reference, on purpose.

src/Ember.Rpg/          Entity records, flags, items, dialogue, and quests. Games reference it; Ember.Engine never does. Check: tests/Ember.Rpg.Check.

samples/FirstLight/     The contract sample: the smallest game that can be built on the above.

samples/Campaign/       A game built on the engine. A consumer of src/, never part of it.

tools/                  Optional helpers. sync-from-ratnabay.ps1 re-imports engine sources
                        from the Ratna Bay working copy; you do not need it — Ember is edited
                        here. bake_earth_land.py bakes Campaign's terrain data.

Docs/BUILDING_BLOCKS.md What each piece does and how to call it.
```

---

## Starting a game

Subclass `EngineHost`. That is the whole of it.

```csharp
public sealed class MyGame : EngineHost
{
    private SceneRenderer _scene = null!;
    private readonly FirstPersonView _view = new();
    private readonly List<string> _faults = new();
    private readonly List<PointLight> _lights = new();

    public MyGame(string[] args)
        : base(args, logicalWidth: 1280, logicalHeight: 720, title: "My Game") { }

    protected override void LoadContent()
    {
        // GraphicsDevice does not exist until now. Nothing that needs it can be built in the
        // constructor - see the traps below; this one costs an hour if you get it wrong.
        _scene = new SceneRenderer(GraphicsDevice);

        var fonts = Path.Combine(AppContext.BaseDirectory, "Content", "Fonts");
        AttachCanvas(Path.Combine(fonts, "NotoSans", "NotoSans-wght.ttf"),
                     Path.Combine(fonts, "Cinzel", "Cinzel-wght.ttf"));

        AttachScene(_faults);   // billboards, the lit effect, and both audio banks
        _ui.Resize(GraphicsDevice.Viewport, _uiScalePreference);

        _view.Reset(new Vector3(0, 1.7f, 5), yaw: 0, pitch: 0, standingEyeY: 1.7f);
        _view.SetProjection(GraphicsDevice.Viewport.AspectRatio);
    }

    protected override void Update(GameTime gameTime)
    {
        BeginHostFrame();               // frame clock, capture warmup, fps
        _input.Sample();                // one keyboard/mouse read per frame, and only one

        var walk = new WalkInput(Forward: ..., Back: ..., /* ... */);
        _view.Step(RealSeconds(gameTime), walk, lookPixels, MyCollision);
        _view.RebuildView();

        _input.Commit();                // this is what makes Pressed() mean "pressed"
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.Black);
        GraphicsDevice.DepthStencilState = DepthStencilState.Default;

        _scene.Begin(LitEffect, _view.View, _view.Projection, _view.Position,
                     _view.Yaw, StoneTextures.StonePalette.Granite, _lights);
        _scene.DrawWorldBox(min, max, colour, "stone");

        _ui.Begin();
        _ui.Text("hello", new Vector2(40, 40), 18, Color.White);
        _ui.End();

        base.Draw(gameTime);
        EndHostFrame(hold: false, exit: Exit);   // writes a queued screenshot, then quits
    }
}
```

`EngineHost` gives you, free and already argued about: the window and a borderless fullscreen
toggle, a **variable** timestep clamped to 100 ms, letterboxed logical-pixel layout at any
resolution, font loading, and the command-line switches `--windowed`, `--perf`,
`--screenshot <path>`, `--cover <path>` and `--warmup <frames>`.

### Collision is a callback

The engine never asks what your world is made of. `FirstPersonView.Step` takes a `ResolveWalk`
— *"I want to move from here by this much, with this radius; where do I actually end up?"* —
and you answer however you like. Pass `null` and it walks through walls. The sample answers
with a clamp to a box; Ratna Bay answers with a sweep against its room geometry.

### Two ways to light a room

`SceneRenderer` draws through `BasicEffect` by default: one directional light, no point lights,
works immediately with no content pipeline at all. If you want the point-light path — up to
four nearest lamps, chosen *per draw* because "nearest" is only meaningful relative to
something — build a shader through MonoGame's content pipeline and hand it over with
`LoadCaveShader(Content, "Effects/YourShader")`. The parameters it binds are listed in
`Docs/BUILDING_BLOCKS.md`.

The sample deliberately does neither, so it exercises the fallback path and proves that path
still works.

---

## What is deliberately not here

Seven files that live in Ratna Bay's engine project are game screens wearing an engine's
clothes. They are excluded from `tools/sync-from-ratnabay.ps1` on purpose, not by accident:

| Left behind | Because |
| --- | --- |
| `MenuRenderer` | draws the words RATNA BAY and that game's three blurbs |
| `ConsentRenderer` | a telemetry question, in that game's voice |
| `OverlayRenderer` | a pause screen that counts rooms cleared and stones at risk |
| `OverlayState` | the payload the above reads: `RoomsCleared`, `PendingStones` |
| `OverlayInput` | pause and settings actions shaped to that pause screen |
| `ScreenStack` | `Shaft`, `CampTrader`, `Fort` — one bool per Ratna Bay panel |
| `UiLayout` | that game's rectangle table; this repo keeps a trimmed one of its own |

Menus, pause, settings and panel stacking are yours to write. They are not hard, and every
attempt to make them generic in the source game produced something that fit exactly one game
anyway.

**Also not here: any game rules.** No inventory, no combat, no saves, no quests, no world
generation. Those belong to the game — to `samples/Campaign` or to whatever you write — and the
engine must never reference them. Campaign consumes the engine; nothing under `src/` may
reference Campaign.

**Ratna Bay residue that did ship:** `CharacterSprites` has a `Bandit` palette, `ItemSprites`
knows what a jiva crystal and a vetala look like, and `SoundBank`'s `Sfx` enum names a `Cast`.
These are *worked examples* of `SpriteForge` and `SoundForge` — the technique is the reusable
part and the vocabulary is not. Read them as recipes, then write your own and delete theirs.

---

## Traps that cost real time

Every one of these was a bug that shipped, or nearly did.

**Build anything that needs `GraphicsDevice` in `LoadContent`, never in the constructor.**
`Game.GraphicsDevice` is null until then. A `SceneRenderer` built early captures the null and
fails a hundred frames later inside a texture cache, nowhere near the line that was wrong.

**Call `_input.Commit()` once, at the end of your update.** `Pressed()` compares this frame's
keyboard against the committed one. Forget the commit and every key repeats every frame; commit
in the middle and half your handlers see the wrong edge.

**Read input in the order your screens stack.** If a panel owns the frame, the keys that open
*other* panels must be read after the check that hands it the frame — otherwise the journal
opens on top of the modal that was supposed to be swallowing input. Not hypothetical:
collapsing seven key reads to the top of one frame update did exactly that.

**`EnableDefaultLighting()` overwrites the ambient colour and all three lights.** Call it
*before* setting them, or every value you set below it is dead code and the scene goes
near-black. `AttachScene` gets this right; if you build your own `BasicEffect`, keep the order.

**Never trust a switch that reports success.** Three separate bugs in the source game were a
flag that was set, logged as set, and read by nothing. If you add a debug toggle, prove it does
something — take two screenshots and compare pixels. *A log with no word for something reports
its absence as a fact.*

**A wall-clock frame rate is not `GameTime`.** Under a fixed timestep `ElapsedGameTime` is
always 1/60 no matter how slowly the game is really running — which is exactly the failure the
number exists to expose. `EngineHost` runs a variable timestep and measures fps off a
stopwatch.

**Cube winding and normals have to agree.** `SceneRenderer`'s cube is wound outward with
outward normals. Change one without the other and every lit surface in your game turns black —
and slabs will hide it from you for weeks, because a wall's two faces are centimetres apart.

**Screenshot waits are in seconds, not frames.** Capture mode runs uncapped, so waiting "120
frames" buys an unpredictable and usually tiny amount of game time. It once made a still-rising
enemy look like a rendering fault.

---

## Optional: pulling from Ratna Bay

Ember is owned in this repo. Edit `src/Ember.Engine` and `src/Ember.Scripting` here; nothing
in the build runs a sync, and you never need one.

If a fix you want was made in the Ratna Bay working copy instead, you can re-import it with
the optional script. It keeps its hash guard (`tools/.sync-state.json`): a file you have
edited here is skipped and listed, never overwritten, unless you pass `-Force`.

```powershell
.\tools\sync-from-ratnabay.ps1 -WhatIf    # show what would be written, write nothing
.\tools\sync-from-ratnabay.ps1            # copy, rewriting RatnaBay.Engine -> Ember
dotnet build Ember.sln
```

It copies an allow-list, not a folder, and prints what it excluded and why. **With `-Force`,
anything you have edited under `src/Ember.Engine` is overwritten**, except the files on the
exclusion list — so if you want to own a file here permanently, add it to that list with a
reason, the way `Ui/UiLayout.cs` already is.

Renaming the framework is `-Root MyName` plus renaming two folders and their `.csproj` files.

---

## Licences

The engine sources came out of your own game and are yours.

The two fonts under `samples/FirstLight/Content/Fonts` are third-party and are **not**: Noto
Sans and Cinzel are both under the SIL Open Font Licence, and `OFL.txt` sits beside each of
them. Ship the licence with anything you ship the font in.

MonoGame is under the Microsoft Public Licence; FontStashSharp is MIT.

BEPU v2 is pinned as `BepuPhysics` 2.5.0-beta.29 (Apache-2.0), with `BepuUtilities` pulled in
transitively under the same license. It is a prerelease dependency; include the Apache license and
required notices when distributing a build that contains it.
