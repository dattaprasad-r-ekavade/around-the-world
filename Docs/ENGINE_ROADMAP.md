# Ember roadmap: lightweight 3D engine and scene generator

Updated: 29 September 2026. This is the sole active roadmap. It supersedes the
[RPG implementation plan](archive/ENGINE_ROADMAP_RPG_2026-09-27.md), including its
next-task instructions and expansion tasks 145–152. Completed implementation history
remains in [ENGINE_PROGRESS.md](ENGINE_PROGRESS.md); historical completion percentages
are not product readiness. See [PROJECT_REVIEW.md](PROJECT_REVIEW.md) for the baseline.

## Product objective

**Primary goal: a game engine that teaches you how to build a game as you build one.**
A first-time creator should make a small change, see what it does, and build confidence.
Embedded teaching and beginner usability are first-release requirements. Contextual explanations, experiments and feedback must teach transferable concepts, not just button sequences. The longer-term
product is a game in which the player is a game designer, using these same creation tools.
See [CREATOR_EXPERIENCE.md](CREATOR_EXPERIENCE.md) for the layout, language, learning loop,
measurement targets, and designer-game direction. It defines acceptance; implementation and
verification status are recorded below.

Build a compact Windows 3D creation tool in which one person can create or generate a
scene, refine it visually, and use that same scene in a playable game or a cutscene.
A usable engine means a repeatable create → edit → preview → save → reopen → deliver
workflow, with clear failures and predictable resource ownership.

The first release serves small games, short cinematics, and generated environments.
RpgSlice remains an integration fixture and optional RPG example. RPG mechanics and a
large regional map no longer define engine completion.

Scene generation initially means deterministic, parameter-driven composition of licensed
assets and reusable scene templates: rooms, paths, props, lights, and camera starting
points. Text-to-scene AI, mesh generation, and external services are optional future
adapters; they are not prerequisites for the local tool.

## Scope and design constraints

- Retain C#, MonoGame WindowsDX, GLB imports, and versioned JSON initially. Windows x64
  is the first delivery platform; do not promise a trivial cross-platform port.
- Evolve CharacterStudio into the editor incrementally. Preserve FirstLight, Campaign,
  and RpgSlice as consumers; avoid a rewrite and a complex ECS.
- Runtime owns rendering, scene evaluation, physics, audio, and lifecycle. Authoring
  owns commands, validation, recovery, and generation. Editor owns UI. RPG-specific
  authoring remains optional; generic editor startup must not require an RPG content pack.
- Use one project and asset model for games and films. Keep authored data separate from
  transient play/preview state. Reuse the same rendering/evaluation paths for output.
- Prefer one editor executable, project-relative files, offline operation, and visible
  diagnostics. C# extension points are acceptable; common scene tasks must not require code.
- Start with the supported GLB material/animation subset and existing lighting. PBR,
  IK, retargeting, facial animation, multiplayer, automatic navigation meshes, swimming,
  crime simulation, large worlds, and additional platforms are deferred.
- Lightweight is measured: record editor cold-start time, idle/loaded memory, project
  open time, frame p95/p99, import/export time, package size, and dependency footprint.
  Set budgets from a named reference PC and fixed content in M0; avoid invented capacity claims.

## Status and evidence policy

M0.1 through M0.3 have passed; M0.4, M1.1–M1.3 and UX.1–UX.4 are **In progress**; M1.4, UX.5–UX.6
and M2 onward remain **Not started** against their new acceptance criteria. Existing components are
reusable foundations, not a reason to repeat their implementation.
Documentation reset is complete. Review at `3c9620e`: M0.1–M0.3 have recorded passes;
M0.4 remains open despite a commit title saying it was closed. Its Windows workflow now
passes on the pulled baseline; interactive resize/save/reopen/play-stop evidence remains
incomplete. The reviewed activation-queue test now allows valid partial-budget steps and the
full local CPU suite passes. M1.1 services and native project/model pickers exist but the
visible button-driven workflow is unproven. Selected-model reload builds a replacement preview before swapping it in, and the asset-ownership test confirms a corrupt GLB leaves the prior asset active. Verify that recovery through visible editor controls before closing the gate. UX.1 has a first local Home and scene-first
workspace implementation; resize and DPI checks remain. UX.2 has starter thumbnails, atomic
Game/Film project starters, native pickers, visible undoable Move/Turn/Size actions and scene-view
selection code, plus a temporary model preview with explicit Add and Cancel actions. Interactive
verification of these controls remains open. Windows CI for `ababa01` exposed a time-budget-sensitive
cell-queue test; commit `361078f` separates wall-clock scheduling from the cost/cell fairness check,
with elapsed-budget behavior covered by a controllable clock. Hosted run #6 passed on `145c892`.
UX.3 also has an optional Inspector explanation for transforms and shared model assets, plus direct
next actions for an empty scene and an empty model list. The Inspector now identifies a model being
previewed instead of describing the scene as empty, and editor failures remain visible in the
Inspector with a recovery hint. The native graphics interaction gate remains open.
UX.1's optional tools menu and its tool panels now have exclusive draw states to prevent their
shared-position windows from overlapping during a selection change.
UX.4 now has an optional action-driven first-creation lesson, replayable from the workspace, with
per-step Why? explanations, two levels of offline hints, action-based completion checks, and a
project-local completion record. Its button-driven walkthrough and UX.5 novice observations remain
unverified.
UX.1 Home now paints an opaque workspace background; the latest Release capture confirms the
Game/Film/Open choices without the preview scene showing through. Resize, DPI and button-driven
verification remain open.
M1.2 now has a nested selectable object hierarchy, command-based world-preserving reparenting, and
viewport Move/Turn/Size gizmos. Move is world-aligned with parent-aware placement; Turn and Size use
the selected object's local axes. Optional position-grid, angle and scale snapping are implemented.
Live drag verification, undo/redo interaction, and save/reopen evidence remain open.
Preserve completed reliability work.

Execution order: complete M0.4's interactive verification and continue UX.1–UX.4 alongside
completion of M1.1. Do not add more default panels while this work is pending. UX.5 novice
observation is required for the M1 gate; UX.6 follows the first M2 interaction. Work may proceed while
manual evidence is pending, but do not call dependent release gates Passed.

Use Not started / In progress / Blocked / Passed. A Passed task records commit, command
or exact UI steps, fixture, configuration, hardware where relevant, result, and artifact
location in ENGINE_PROGRESS.md. Distinguish code inspection, CPU tests, live integration,
visual/audio review, and relocated distribution checks. A unit test cannot close a UI gate.
Do not compute a product-completion percentage from checked implementation rows.

## M0 — Establish a trustworthy baseline

Dependency: none. Prioritize this before adding features.

| ID | Work | Acceptance | Status |
| --- | --- | --- | --- |
| M0.1 | Repair soak verdicts and diagnostics. | Record actual errors including committed-travel cleanup failures; validate transition count, timeouts, latency, resource trends, and memory separately. Failures produce nonzero exit status. Inject a cleanup error, resource growth, incomplete run, and 1-transition case; each is handled correctly. Export full raw samples and run metadata. | **Passed** |
| M0.2 | Run live lifecycle coverage. | At least 50 transitions spanning two interiors and exterior boundaries, plus repeated scene reload and play/stop; compare like-for-like resource counts after warmup. Exercise delayed/failed loads and retry, save/restart, and unique identities. No unexplained growth or swallowed errors. Keep this separate from a longer timed soak. | **Passed** |
| M0.3 | Establish performance and distribution evidence. | Run a minimum 30-minute mixed editor/runtime session on a named PC; record frame tails, owned resources, memory and dependency/package size. Set explicit reference-scene budgets. Reproduce a relocated build with no checkout dependency; separately record whether an SDK-free machine was tested. | **Passed** |
| M0.4 | Audit persistence and add repeatable validation. | Recheck old data-safety findings against current code; cover atomic restore, failed writes, interrupted save, cancellation cleanup and schema errors. Add [Windows build/CPU-test CI](../.github/workflows/windows-engine.yml) and a documented [graphics/manual gate](MANUAL_GRAPHICS_GATE.md); keep RPG checks separate. | **In progress** |

M0.3 reference budgets apply only to CharacterStudio's paired-Fox scene on the named
reference PC (Windows 10.0.26200, .NET 9.0.7, Intel UHD Graphics, 1280x720). They are
initial guardrails derived from the recorded run, not guarantees for other hardware or content.

| Metric | Initial budget | Recorded result |
| --- | ---: | ---: |
| Frame interval p95 | <= 50 ms | 46.47 ms |
| Frame interval p99 | <= 50 ms | 47.12 ms |
| Maximum frame interval | <= 1,000 ms | 743.99 ms |
| Owned preview graphics resources after warmup | exactly 2; no growth | 2; no growth |
| Post-warmup working-set peak growth | <= 30 MiB | 18.9 MiB |
| Post-warmup private-memory peak growth | <= 30 MiB | 17.4 MiB |
| Post-warmup managed-heap peak growth | <= 15 MiB | 7.9 MiB |
| Self-contained win-x64 package | <= 135 MiB | 128.45 MiB |

Gate: corrected machine-readable evidence supports the small-scene baseline. Regional
expansion remains unapproved. A longer soak establishes bounded behavior for its run,
not proof that no leak exists under any workload.

## UX — Beginner creation experience (required, early)

Dependency: reuse current project/scene services. Functional prototype can be developed
alongside M1; accepting the M1 release gate requires M0 and UX.1–UX.5. Use the design
contract in CREATOR_EXPERIENCE.md. These tasks precede additional advanced editor scope.

| ID | Work | Acceptance | Status |
| --- | --- | --- | --- |
| UX.1 | Replace all-panels startup with Home and a scene-first workspace. | Home offers Game, Film and Open; the default editor has one toolbar, an Add library and contextual selection panel. World/RPG/debug/export panels are closed until requested. No overlapping windows; at least 60% scene area at 1280×720; reset layout, resize and DPI checks pass. | **In progress** |
| UX.2 | Make common actions visual and understandable. | File/folder pickers, starter thumbnails, model preview, click selection and visible Move/Turn/Size/Play/Undo/Save actions complete the basic loop. No typed paths, JSON, manifests or required shortcuts. Advanced values remain discoverable in More details. | **In progress** |
| UX.3 | Provide safe feedback and contextual help. | State-aware empty/disabled/error messages offer a next action; a missing model can be located through Browse. Save status, undo, recovery and Play/Stop are understandable. Keyboard focus, readable scaling and non-color-only states pass manual checks. | **In progress** |
| UX.4 | Build a skippable first-creation lesson. | A starter enables add → move → play → undo → save/reopen without code; one concept per step, detected from real actions, with replayable help. Teach objects and transforms through explain → change → predict → play → reflect → vary. Contextual Why?/hints work offline; free creation is always available. The lesson uses normal commands and project data. | **In progress** |
| UX.5 | Observe novice use and revise. | Three first-time users complete the starter loop within 10 minutes each without facilitator intervention; record errors/help/confusion and a later unaided repeat. All can undo, Play/Stop and reopen safely, explain their change, and repeat it on a different object without step-by-step hints. No participants means In progress with the usability gate pending, not a developer-inferred pass. | Not started |
| UX.6 | Prototype one designer-game mission. | After M2.1, build/test/save a small challenge through an optional mission using the same authoring commands. Teach triggers, actions, goals and playtesting; after guidance, the creator makes a different interaction and explains its rule. Explain consequences and recognize completion; never lock editing behind progression. Free edit and mission mode reopen the same project unchanged. | Not started |

Gate: users learn by making an observable change and trying it. A themed skin, extra
menus, or a tutorial over the existing crowded UI does not meet this gate.

## M1 — One coherent editor project workflow

Dependency: M0 for gate closure; UX.1–UX.5 are part of acceptance. Extract shared services only as needed. Reuse the existing M1.1 services behind the simpler workspace.

| ID | Work | Acceptance | Status |
| --- | --- | --- | --- |
| M1.1 | Project create/open, recent projects, asset browser and import/reimport. | Create a project outside the checkout, import static and animated GLBs, relocate and reopen it. Invalid reimport preserves the prior valid asset and explains the error. Complete these actions through Browse/visual controls; typed-path service tests alone do not pass the gate. | **In progress** |
| M1.2 | Viewport picking, transform gizmos, hierarchy, snapping and inspector. | Place, parent, duplicate, delete and transform objects visually; undo/redo then save/reopen preserves IDs, hierarchy and appearance. All authored edits use the command history. Selection and Move/Turn/Size work in the viewport; numeric transforms are optional details. | **In progress** |
| M1.3 | Reusable scene templates and overrides. | Save a reusable hierarchy; place two instances, edit one override, reload and verify stable independent instances. Define update and broken-reference behavior before implementation. | **In progress** |
| M1.4 | Dirty state, recovery and editor service boundaries. | Open/reload/close cannot silently discard edits; recover interrupted work after validation. Paths and sequences follow the same policy. Generic startup works without RPG data; play/stop cannot mutate authored state. | Not started |

### M1.3 template update and broken-reference policy

- A template is a project-relative, versioned snapshot of one selected root and its descendants.
  Saving to a new path assigns a stable template ID; saving over a valid file keeps that ID and
  advances its revision. Object IDs inside the snapshot remain stable source keys.
- Placing a template creates an expanded scene hierarchy with new scene-object IDs for that
  instance. GLB asset IDs and project-relative paths remain shared. Instances record the source
  template ID and applied revision; authored scene data does not depend on the template file at
  runtime.
- Template updates are explicit. Opening a project never changes instances. An update matches
  objects by their stable source keys, retains instance object IDs and placement, refreshes fields
  that still match the previous template defaults, and preserves explicit per-instance overrides.
  New source objects are added. Removed objects with local overrides or surviving children remain
  as orphaned instance content with a warning; unmodified removed objects can be deleted.
- If a template file is missing or invalid, existing expanded instances remain editable and
  playable from their saved scene data. The editor reports the missing source and offers relinking;
  it never clears the instance, discards overrides, or silently falls back to a different template.
  Updating stays unavailable until the source is repaired or relinked. A missing GLB used by an
  instance follows the normal project asset recovery flow.

The M1.3 API saves atomic, versioned hierarchy snapshots, places expanded instances with remapped
IDs and persisted source mappings, and explicitly updates changed hierarchies. Scene version 14
stores source name/transform, enabled-state, reset-policy, shared GLB asset, static-mesh LOD,
character playback and attachment baselines, door/spawn/world-entity component baselines, plus
orphan IDs and the target world cell. Explicit updates preserve stable mappings and local overrides,
synchronize source component and attachment additions/removals, retain edited component content,
add and remove source objects, track uncertain removed content as orphans, and support undo/redo.
Version-12 and version-13 baselines without newer source mappings load conservatively and upgrade on
update. Visible orphan warnings, the editor placement/update workflow and source relinking remain
open.

Gate: create a furnished, lit scene using editor actions, with no handwritten JSON or
source changes; restart and recover the same scene. Record the complete action sequence and pass UX.1–UX.5; a developer-only walkthrough is insufficient.

## M2 — Make and deliver a small game

Dependency: M1. Reuse existing controller, physics, behavior, audio, and packaging code.

| ID | Work | Acceptance |
| --- | --- | --- |
| M2.1 | Persist and inspect colliders, player/camera settings, input actions and behavior assignments. | Author a controllable character, collision, trigger and interaction in the editor; save/reopen and run them through the shared runtime. Invalid assignments report the owning object. Offer beginner action presets (collect, open, reach goal) through a What happens? panel; advanced component bindings remain optional. |
| M2.2 | Complete play/pause/stop and game–sequence handoff. | Repeated play/stop restores the scene and input/audio ownership; trigger a cutscene, then return control to the correct player/camera without duplicate behaviors. |
| M2.3 | Build panel and dependency-complete runtime output. | Validate and publish a self-contained Windows game with referenced scenes, assets, audio and sequences. Move output outside the checkout and play it; missing dependencies block publication with useful diagnostics. |

Gate: an editor-authored 3–5 minute interaction demo starts, plays, saves/reloads its
small state, and exits from a relocated package. No sample-specific source wiring is
needed for the demonstrated workflow. Full RPG systems are not this gate. Include the optional UX.6 designer mission using the same saved project and editor commands.

## M3 — Author and export cutscenes

Dependency: M1; runtime-triggered sequence proof also depends on M2.2.

| ID | Work | Acceptance |
| --- | --- | --- |
| M3.1 | Editable timeline for clips, object transforms, camera keys and cuts. | Add/move/delete keys and tracks, undo/redo, save/reopen, and validate missing targets. Expose authoring, not only playback of a generated sequence. Reveal the timeline only in Animate/Film work; begin with a shot, camera and clip rather than an empty collection of technical tracks. |
| M3.2 | Preview parity, audio cues and scene lighting controls. | Scrub and play a multi-shot scene; define seek/mute rules so scrubbing does not replay unwanted audio. Reopening preserves timing, camera and lighting. |
| M3.3 | Export UI over deterministic frame export. | Choose range, rate, resolution and output folder. Export numbered PNGs plus timing/asset manifest and synchronized audio output; cancellation/failure leaves an accurate manifest. Compare selected output frames with preview. |
| M3.4 | Optional video encoding adapter. | If included, encode image/audio outputs through an explicitly configured encoder with progress, cancel and actionable errors. Core scene authoring and PNG export work without it. This task does not block the first release. |

Gate: author a 30–60 second film with two animated actors, three camera shots, prop
motion and audio, then restart and export matching output. Report repeatability for the
tested backend; do not promise byte-identical GPU images across machines. Observe a first-time user making a short starter shot, previewing and exporting frames within the provisional 15-minute target in CREATOR_EXPERIENCE.md.

## M4 — Generate editable scenes

Dependency: M1; generated game/film proofs depend on M2 and M3 respectively.

| ID | Work | Acceptance |
| --- | --- | --- |
| M4.1 | Versioned generation recipes and deterministic output. | Recipe stores seed, dimensions, asset palette, placement rules and generator version. Same inputs yield the same transforms/content; unsupported or invalid recipes fail without changing the scene. |
| M4.2 | Room/layout and prop-scatter generators. | Generate a small connected room layout and a bounded outdoor prop scene. Check overlaps, clearance and placement limits; emit normal editable scene objects with provenance. Do not imply generated navigation or gameplay correctness. |
| M4.3 | Preview, apply, undo and regeneration policy. | Offer Room/Outdoor presets and a few visual parameters; preview generation before applying, and one undo removes the operation. Seed and rule internals are optional details. Explicitly preserve locked/manual edits or show affected objects before replacement. Save/reopen preserves recipe and generated result. |
| M4.4 | Reuse generated content in both outputs. | Generate from a bundled licensed palette, refine visually, play with collision, and export a camera sequence without editing engine code. Report generation time and object/resource budgets. |

Gate: a recipe produces a useful starting scene that remains ordinary editable content,
with predictable regeneration and no dependency on an online service.

## M5 — First usable release

Dependency: M0–M4 and UX gates, excluding optional M3.4. Usability work begins at UX, not here.

| ID | Work | Acceptance |
| --- | --- | --- |
| M5.1 | Rendering and lifecycle consistency. | Static/skinned assets, supported materials, shadows, cameras and audio match between editor, runtime and export reference scenes. Fix measured bottlenecks; prepare terrain on workers but create/upload GPU resources on the graphics-owning thread. |
| M5.2 | Onboarding, templates and installation. | Ship empty scene, small game and short film templates; a fresh user completes each documented workflow without modifying engine sources. Re-test the early UX workflows on the packaged release; record observed completion time, wrong turns, help, retention and friction. Preserve a free creation route alongside lessons and designer missions. |
| M5.3 | Regression and release evidence. | Re-run M0 reliability/budgets against final scenes; test malformed projects and missing assets, clean installation and relocated outputs. Publish supported formats, tested limits, known issues and licenses. |

Release definition: one local tool supports create/import/generate → edit → save/reopen
→ play or sequence → package or export. Expand only in response to a demonstrated
workflow need and a measured capacity experiment. No calendar estimate is committed
until M0 identifies the reliability work and M1 establishes editor integration cost.

## Working rules and next handoff

1. Read this roadmap and BUILDING_BLOCKS.md; inspect existing implementations before adding one.
2. Continue M0.4's interactive checks and finish UX.1–UX.4 with the M1.1 workflow. The local M1.1 slice catalogs persisted project GLBs and supports preview-before-placement; verify project pickers and Browse → preview → Add/Cancel through visible controls. Selected-model reload and invalid-replacement preservation are already implemented and have an asset-ownership test; exercise a valid reload and a corrupt replacement through visible editor controls, confirming the prior scene preview stays active and the recovery message explains the next action. Also verify empty-scene actions, More tools panel switching, scene-view picking, layout reset, resize, DPI, edit/save/play actions, and the first-creation lesson through its final reopen and replay before closing any gate. Run the UX.5 novice observation before accepting the M1 gate. M1.2 has Move/Turn/Size viewport gizmos and optional position, angle and scale snapping. Exercise all three tools, parent selection and reparenting through visible controls; verify hover/drag, one-step undo/redo, and save/reopen preserve transforms, IDs, hierarchy and appearance. Rerun Windows CI after later code changes. Follow the dependency order above; do not skip early usability for advanced systems. Split large tasks into independently verifiable changes before coding.
3. Keep scope tied to the milestone gate; do not resume archived RPG expansion automatically.
4. Run relevant tests and solution build for code changes, plus UI/graphics/audio checks
   when the acceptance requires them. Record limitations and failed checks honestly.
5. Append evidence to ENGINE_PROGRESS.md and update only the current milestone status.
6. Preserve user content; do not commit, push or publish unless requested.

## After the first release — designer-game presentation experiment

Use UX.6 feedback to explore an in-world workshop and a series of optional creation
missions. Reuse editor commands, project formats and runtime; keep direct editing
available and teach transferable game-development concepts. Validate one playable
creator session before committing to a campaign, economy, social platform or immersive
UI rewrite. Those larger features are not required for the lightweight first release.
