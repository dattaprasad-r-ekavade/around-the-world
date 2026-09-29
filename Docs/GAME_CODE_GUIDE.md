# Writing game code on Ember

How a game uses the runtime directly from C#: the `EngineHost` contract, collision, lighting,
what the engine deliberately leaves to games, and traps that cost real time. Moved from the
repository README on 29 September 2026. For the API of each building block, see
[BUILDING_BLOCKS.md](BUILDING_BLOCKS.md). The contract sample is `samples/FirstLight`.

## What the runtime provides

**What you get:** a window, a variable timestep, a first-person camera with collision as a
callback, fixed-step 3D physics with a controllable capsule, a third-person obstruction camera,
jump/slope handling, named keyboard actions, motion-driven character clips, interpolation, and
layer-filtered raycasts; compiled scene behaviours and scene-owned imported audio; box and model rendering with two lighting paths; procedurally generated wall, floor,
timber and cloth textures, procedurally generated character and item sprites, procedurally
synthesised sound effects and an ambient bed, a 2D canvas with fonts and layout, input sampling
with press-detection, list and grid pickers, a console command router, and screenshot capture
you can drive from the command line.

**What you write:** your game.

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
