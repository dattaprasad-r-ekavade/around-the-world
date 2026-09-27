# Creator experience: learn by making

Product direction, 28 September 2026. This is an implementation contract for the
[active roadmap](ENGINE_ROADMAP.md), not a claim that the redesigned UI exists.

## Who this is for

The primary user is a first-time game creator, including the engine's owner learning
game development. The tool should teach through small visible changes and immediate
play. Experienced users can reveal precise controls without forcing beginners through
engine vocabulary. The eventual product can be a game about being a game designer;
the underlying editor must already make that interaction possible.

The first-session promise: choose a starter, add something, change it, press Play,
undo a mistake, and keep the result. No code, JSON, typed file paths, manifest setup,
or RPG content registration is required for this loop.

## Why the current layout needs restructuring

At source revision 3c9620e, CharacterStudioEditorUi draws project, scene, sequence,
world, and RPG panels together. RPG Authoring and Sequence preview start at the same
position (800,16); export occupies that same right column. World Cells covers the
middle of the scene. The recorded M1.1 screenshot shows most of the viewport hidden,
clipped content, raw paths, numeric fields and unrelated setup controls.

This is structural crowding, not simply a color/theme problem. Keep working services
and optional tools, but reorganize when and where they appear. Do not solve it by
adding a tutorial overlay on top of the same default panel arrangement.

## Core promise: the engine itself teaches game development

**Ember is a game engine that teaches you how to build a game as you build one.**
Teaching is part of the everyday creation workflow from the first usable release.
The later game-about-game-design presentation builds on this foundation; learning
must not wait for that presentation or depend on a separate course.

Use a repeatable loop: **choose a goal → explain one concept → make a change → predict
what will happen → play and observe → explain the result → try a variation**.
Keep explanations brief and optional, next to the object or action they concern.
For example, when adding a collectible, show how touching it triggers an action,
then let the creator change the action and test the consequence. Teach cause and
effect rather than only telling the user which button to press.

| Learning step | Real creation outcome | What the creator should understand |
| --- | --- | --- |
| Place and change | A small scene with movable objects | Objects, position, rotation, size and undo |
| Look and move | A controllable character and camera | Input, viewpoint and movement |
| Touch and react | A collectible or opening door | Collision, triggers, events and actions |
| Set a goal | A short playable challenge | Rules, feedback, success and failure |
| Test and improve | A revised version after playtesting | Observe a problem, change a rule and compare results |
| Keep and share | A reopened project and runnable output | Saving a project versus exporting a game |

Map lessons to available features: placement at M1/UX.4, interactions at M2.1,
challenge design at UX.6, delivery at M2.3. Film lessons teach camera shots, timing
and animation when M3 is available. Do not expose nonfunctional lesson steps.

Provide an optional "Why?" explanation, a worked example, and a hint ladder from a
small suggestion to a concrete demonstration. Errors should help users understand
and repair their own scene. Any demonstration that changes content must be explicit
and undoable. Users can dismiss help, revisit a concept, or continue free creation.
Progress records learned concepts and completed creations; it never gates core tools.
Built-in lessons work offline without an AI service or account. An optional future
assistant may supplement them but is not required for learning or operation.

Acceptance includes transfer: after a guided example, a beginner creates a similar
interaction with a different object or outcome without step-by-step hints and can
explain the rule they changed. Finishing a checklist alone is not evidence of learning.
Record where understanding failed and revise the explanation or interaction design.

## Default interaction model

**Home:** Make a game, Make a film, or Open a project. Each creation route offers a
small visual starter and an optional guided first creation. Free creation is always
available. Choose a project name; use a sensible local default folder with Browse.

**Create workspace:** one stable top bar (Home, Save, Undo/Redo, Play/Stop, Finish),
a compact searchable Add library, the scene in the center, and one contextual selection
panel. Show only the controls relevant to the selected object or current action.
The object list is collapsible. World/RPG/debug tools are opt-in under More tools.
A film project reveals a timeline when the user chooses Animate; export opens at Finish.
No stacked floating windows on first launch. Provide Reset layout.

At 1280×720, the default creation view must leave at least 60% of the usable workspace
unobscured for the scene. Validate at 100% and 150% Windows display scaling, including
resize, scroll and keyboard focus. A small window may collapse side panels rather
than cover the scene. Do not satisfy the area target by shrinking text below readability.

**Select and change:** click the object in the scene, use visible Move/Turn/Size tools,
and see the result immediately. Offer snap and reset. Numeric transforms are in More
details; object names and familiar options come first. Every action has a visible UI
path; shortcuts are optional accelerators. Keyboard navigation, readable focus, text
labels beside unfamiliar icons, and non-color-only state cues are acceptance requirements.

**Play and return:** a clear Play state changes the primary button to Stop. Returning restores authored data and selection.
Explain once that play changes are temporary, without requiring users to understand cloning.
Save status and recovery are visible; routine recovery should not require managing staging IDs.

**Finish:** offer the output matching the project intent: Playable game or Film frames.
Show a small set of presets, destination Browse, progress, cancel and Open output.
Expose codecs, resolution details and diagnostic manifests only when needed. Do not
label image-sequence export as a finished video until encoding is implemented.

## Plain language and progressive detail

| Current concept | First-use presentation | Detailed view |
| --- | --- | --- |
| Play on clone | Play; explain temporary preview changes once | Runtime/session diagnostics |
| GLB path / import | Add model… with file picker and preview | File format, source path and reimport details |
| Hierarchy / transform | Objects / Move, Turn, Size | Parenting and numeric local/world transforms |
| World manifest / cells | Optional connected areas workflow | Streaming grid and manifest settings |
| Behavior assignment | What happens? with simple action cards | Component types and C# extension settings |
| Sequence frame export | Finish film → Export frames | Frame range, rate and manifest |
| Validation exception | What happened, affected object, how to fix | Expandable technical details |

Teach real concepts as users need them; keep accurate technical terms available in
contextual help. Hidden tools must remain discoverable by search/More tools, and novice
projects must open unchanged in detailed mode. Do not maintain separate project formats.

## First creations and learning loop

The M1 lesson first covers placing and changing objects in preview. The collectible
interaction below follows M2.1; it is not a prerequisite that blocks M1 on M2.

1. **Game:** open a furnished starter with a controllable character and working camera;
   add a collectible, place it, choose a ready-made interaction, and Play to collect it.
2. **Film:** open a staged character, position a camera, choose an animation, preview a
   short shot, save and export frames.
3. **Generated scene:** choose Room or Outdoor clearing, adjust a few visual parameters,
   preview variations, keep one and edit it like any other scene. Seed/rule details stay optional.

Each guided step introduces one concept and asks for one visible action. A small hint
can be skipped, replayed or dismissed without disabling any tool. Detect success from
actual scene state/actions, not from pressing Next. Hints never cover the required target.
An empty project must still explain a useful first action.

## Path toward a game about designing games

First prove one optional creator mission: "Build a tiny playable challenge." The player
places a goal, adds one obstacle, chooses an interaction, tests the result and saves it.
Feedback recognizes the creation and explains what it did; it should not grade artistic
quality. Optional celebrations, a workshop setting and later narrative can build on this.
No XP requirement, timer, forced tutorial, daily reward or unlock should gate core tools.

Mission steps must use the same authoring command/service layer as normal editor actions,
including undo, validation, recovery and saved project data. Missions are declarative
lesson data with observable completion conditions; do not introduce a second engine or
copy of editing logic. Reserve a later experiment for an immersive designer-game shell;
its presentation should follow evidence from the useful creator loop.

## Usability acceptance, not a screenshot-only gate

Initial targets are hypotheses to test, not measured outcomes:

- Three first-time users complete the starter game loop within 10 minutes each without
  facilitator intervention, code, JSON or typed paths. Built-in optional hints are allowed.
- Each can select/move an object, undo a mistake, Play/Stop, save and reopen the result;
  no unintended data loss. Observe the film loop separately, targeting 15 minutes.
- Record completion time, wrong turns, requests for help, terminology confusion and
  whether users can explain the effect of the change they made. Obtain a short comfort
  rating (1–5); investigate ratings below 4 rather than hiding them in an average.
- Test the workflow again after a break to check whether users can find the actions
  without replaying the lesson. Record this separately from guided completion.
- Developer self-testing and automation verify functionality/layout but cannot substitute
  for novice observation. If participants are unavailable, keep the usability gate Pending.

Fix observed blockers and repeat the affected task. This small formative study is a
release gate for the chosen workflows, not a claim of universal usability.
