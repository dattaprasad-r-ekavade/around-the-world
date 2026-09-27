# Ember roadmap: lightweight 3D engine and scene generator

Updated: 27 September 2026. This is the sole active roadmap. It supersedes the
[RPG implementation plan](archive/ENGINE_ROADMAP_RPG_2026-09-27.md), including its
next-task instructions and expansion tasks 145–152. Completed implementation history
remains in [ENGINE_PROGRESS.md](ENGINE_PROGRESS.md); historical completion percentages
are not product readiness. See [PROJECT_REVIEW.md](PROJECT_REVIEW.md) for the baseline.

## Product objective

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

All milestones after M0.2 remain **Not started** against their new acceptance criteria;
existing components are reusable foundations, not a reason to repeat their implementation.
M0.1 and M0.2 have passed their recorded acceptance checks. Documentation reset is complete.

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
| M0.3 | Establish performance and distribution evidence. | Run a minimum 30-minute mixed editor/runtime session on a named PC; record frame tails, owned resources, memory and dependency/package size. Set explicit reference-scene budgets. Reproduce a relocated build with no checkout dependency; separately record whether an SDK-free machine was tested. | Not started |
| M0.4 | Audit persistence and add repeatable validation. | Recheck old data-safety findings against current code; cover atomic restore, failed writes, interrupted save, cancellation cleanup and schema errors. Add Windows build/CPU-test CI and a documented graphics/manual gate; keep RPG checks separate. | Not started |

Gate: corrected machine-readable evidence supports the small-scene baseline. Regional
expansion remains unapproved. A longer soak establishes bounded behavior for its run,
not proof that no leak exists under any workload.

## M1 — One coherent editor project workflow

Dependency: M0. Extract shared services only as needed by these tasks.

| ID | Work | Acceptance |
| --- | --- | --- |
| M1.1 | Project create/open, recent projects, asset browser and import/reimport. | Create a project outside the checkout, import static and animated GLBs, relocate and reopen it. Invalid reimport preserves the prior valid asset and explains the error. |
| M1.2 | Viewport picking, transform gizmos, hierarchy, snapping and inspector. | Place, parent, duplicate, delete and transform objects visually; undo/redo then save/reopen preserves IDs, hierarchy and appearance. All authored edits use the command history. |
| M1.3 | Reusable scene templates and overrides. | Save a reusable hierarchy; place two instances, edit one override, reload and verify stable independent instances. Define update and broken-reference behavior before implementation. |
| M1.4 | Dirty state, recovery and editor service boundaries. | Open/reload/close cannot silently discard edits; recover interrupted work after validation. Paths and sequences follow the same policy. Generic startup works without RPG data; play/stop cannot mutate authored state. |

Gate: create a furnished, lit scene using editor actions, with no handwritten JSON or
source changes; restart and recover the same scene. Record the complete action sequence.

## M2 — Make and deliver a small game

Dependency: M1. Reuse existing controller, physics, behavior, audio, and packaging code.

| ID | Work | Acceptance |
| --- | --- | --- |
| M2.1 | Persist and inspect colliders, player/camera settings, input actions and behavior assignments. | Author a controllable character, collision, trigger and interaction in the editor; save/reopen and run them through the shared runtime. Invalid assignments report the owning object. |
| M2.2 | Complete play/pause/stop and game–sequence handoff. | Repeated play/stop restores the scene and input/audio ownership; trigger a cutscene, then return control to the correct player/camera without duplicate behaviors. |
| M2.3 | Build panel and dependency-complete runtime output. | Validate and publish a self-contained Windows game with referenced scenes, assets, audio and sequences. Move output outside the checkout and play it; missing dependencies block publication with useful diagnostics. |

Gate: an editor-authored 3–5 minute interaction demo starts, plays, saves/reloads its
small state, and exits from a relocated package. No sample-specific source wiring is
needed for the demonstrated workflow. Full RPG systems are not this gate.

## M3 — Author and export cutscenes

Dependency: M1; runtime-triggered sequence proof also depends on M2.2.

| ID | Work | Acceptance |
| --- | --- | --- |
| M3.1 | Editable timeline for clips, object transforms, camera keys and cuts. | Add/move/delete keys and tracks, undo/redo, save/reopen, and validate missing targets. Expose authoring, not only playback of a generated sequence. |
| M3.2 | Preview parity, audio cues and scene lighting controls. | Scrub and play a multi-shot scene; define seek/mute rules so scrubbing does not replay unwanted audio. Reopening preserves timing, camera and lighting. |
| M3.3 | Export UI over deterministic frame export. | Choose range, rate, resolution and output folder. Export numbered PNGs plus timing/asset manifest and synchronized audio output; cancellation/failure leaves an accurate manifest. Compare selected output frames with preview. |
| M3.4 | Optional video encoding adapter. | If included, encode image/audio outputs through an explicitly configured encoder with progress, cancel and actionable errors. Core scene authoring and PNG export work without it. This task does not block the first release. |

Gate: author a 30–60 second film with two animated actors, three camera shots, prop
motion and audio, then restart and export matching output. Report repeatability for the
tested backend; do not promise byte-identical GPU images across machines.

## M4 — Generate editable scenes

Dependency: M1; generated game/film proofs depend on M2 and M3 respectively.

| ID | Work | Acceptance |
| --- | --- | --- |
| M4.1 | Versioned generation recipes and deterministic output. | Recipe stores seed, dimensions, asset palette, placement rules and generator version. Same inputs yield the same transforms/content; unsupported or invalid recipes fail without changing the scene. |
| M4.2 | Room/layout and prop-scatter generators. | Generate a small connected room layout and a bounded outdoor prop scene. Check overlaps, clearance and placement limits; emit normal editable scene objects with provenance. Do not imply generated navigation or gameplay correctness. |
| M4.3 | Preview, apply, undo and regeneration policy. | Preview generation before applying; one undo removes the operation. Explicitly preserve locked/manual edits or show affected objects before replacement. Save/reopen preserves recipe and generated result. |
| M4.4 | Reuse generated content in both outputs. | Generate from a bundled licensed palette, refine visually, play with collision, and export a camera sequence without editing engine code. Report generation time and object/resource budgets. |

Gate: a recipe produces a useful starting scene that remains ordinary editable content,
with predictable regeneration and no dependency on an online service.

## M5 — First usable release

Dependency: M0–M4 gates, excluding optional M3.4.

| ID | Work | Acceptance |
| --- | --- | --- |
| M5.1 | Rendering and lifecycle consistency. | Static/skinned assets, supported materials, shadows, cameras and audio match between editor, runtime and export reference scenes. Fix measured bottlenecks; prepare terrain on workers but create/upload GPU resources on the graphics-owning thread. |
| M5.2 | Onboarding, templates and installation. | Ship empty scene, small game and short film templates; a fresh user completes each documented workflow without modifying engine sources. Record observed completion time and friction. |
| M5.3 | Regression and release evidence. | Re-run M0 reliability/budgets against final scenes; test malformed projects and missing assets, clean installation and relocated outputs. Publish supported formats, tested limits, known issues and licenses. |

Release definition: one local tool supports create/import/generate → edit → save/reopen
→ play or sequence → package or export. Expand only in response to a demonstrated
workflow need and a measured capacity experiment. No calendar estimate is committed
until M0 identifies the reliability work and M1 establishes editor integration cost.

## Working rules and next handoff

1. Read this roadmap and BUILDING_BLOCKS.md; inspect existing implementations before adding one.
2. Continue with the earliest not-Passed task: M0.3. Split large tasks into independently verifiable changes before coding.
3. Keep scope tied to the milestone gate; do not resume archived RPG expansion automatically.
4. Run relevant tests and solution build for code changes, plus UI/graphics/audio checks
   when the acceptance requires them. Record limitations and failed checks honestly.
5. Append evidence to ENGINE_PROGRESS.md and update only the current milestone status.
6. Preserve user content; do not commit, push or publish unless requested.
