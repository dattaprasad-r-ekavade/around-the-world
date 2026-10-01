# Ember roadmap: teaching game engine and career game

Updated: 1 October 2026. This is the **only active roadmap** and the only place where task
status is kept. Read [AGENTS.md](../AGENTS.md) first for how to work in this repository.

How to use this file:

1. Check **Current status** and **Next actions** to find the task to work on.
2. Read that task's row in its milestone table. The Acceptance column is the definition of done.
3. Read the linked evidence file in [`progress/`](progress/) to see what already exists.
4. When you finish work, append evidence (format in [ENGINE_PROGRESS.md](ENGINE_PROGRESS.md))
   and update the task's status here in the same change.

The archived [RPG roadmap](archive/ENGINE_ROADMAP_RPG_2026-09-27.md) and its tasks 01–144
are history, not current work.

## Product objective

**Primary goal: a game engine that teaches you how to build a game as you build one.**
A first-time creator should make a small change, see what it does, and build confidence.
Embedded teaching and beginner usability are first-release requirements. Contextual explanations, experiments and feedback must teach transferable concepts, not just button sequences. The longer-term
product is a game in which the player is a game designer, using these same creation tools.
See [CREATOR_EXPERIENCE.md](CREATOR_EXPERIENCE.md) for the layout, language, learning loop,
measurement targets, and designer-game direction. It defines acceptance; implementation and
verification status are recorded below.

**Pitch: Ember is a game about becoming a game developer, and its tools are a real engine.**
Career mode takes the player through nine stages: My First Game, Game Jam Weekend, a first
indie-store release, a large-store launch, working with a friend, an indie studio, a
specialist role in an AA studio, a planning role in a AAA studio, and an online world.
Each stage explains its basics, then adds new tools and people. Free Create exposes every
finished tool without progression. [CURRICULUM.md](CURRICULUM.md) defines the stages,
missions and transfer checks. The L track below builds the shared learning foundation
(lesson data, visual rules, C# behaviours); the C track builds each career stage.
[CAREER_PACING.md](CAREER_PACING.md) covers pacing and ease of use, and
[DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) orders all tracks into phases P0–P11.

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

Status values: **Not started**, **In progress**, **Blocked**, **Passed**. A task is **Passed** only
when its full Acceptance is met and its evidence file records the commit, command or exact UI
steps, fixture, configuration, hardware where relevant, result and artifact location.

- Name the kind of evidence: code inspection, CPU test, live integration, visual/audio review,
  or relocated distribution check. A unit test cannot close a UI gate.
- Novice-observation gates (UX.5 and later) need real first-time users. With no participants,
  the gate stays pending; a developer walkthrough is not a pass.
- Do not compute a product-completion percentage from checked rows.
- Existing components are reusable foundations. Inspect them before adding new ones.

## Current status

Passed: M0.1, M0.2, M0.3, M0.5, E.1, E.2, E.3. Every task not listed here or in the table is **Not started**.

The dated build and M2.1 reviews below are historical checkpoints. Newer M2.2 and M2.3
implementation evidence is appended after them. The runtime and editor foundations are substantial,
but the complete beginner workflow and deliverable game/film gates remain open. No acceptance status
was promoted.

| Task | Status | Exists now | Remaining to pass | Evidence |
| --- | --- | --- | --- | --- |
| M0.4 | In progress | Hosted CI [36822554692](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36822554692) passed all 373 CPU tests and built with 0 warnings and 0 errors; `--windowed` now permits resizing, and Home maximize/restore plus 1280×720 Home/showcase captures were verified; recovery snapshots now use the shared `AtomicFile` writer and Authoring/Editor Release builds pass | Run the deferred CPU suite on the updated commit; verify visible folder/project controls, then scene transform save/reopen, Play/Stop and recovery cancel/apply/close/reopen | [M0](progress/M0.md) |
| UX.1 | In progress | Home with Game/Film/Open on an opaque background; scene-first workspace; More tools menu with exclusive panels | Scene area of at least 60% at 1280×720; reset layout; resize; 100% and 150% DPI; button-driven check | [UX](progress/UX.md) |
| UX.2 | In progress | Starter thumbnails; atomic Game/Film starters; native pickers; Move/Turn/Size buttons; scene-view selection; model preview with Add/Cancel; searchable Add library | Interactive walkthrough of the whole loop with no typed paths or required shortcuts | [UX](progress/UX.md) |
| UX.3 | In progress | Inspector explanations; next actions for empty states; error recovery hints; bundled 16 px Source Sans 3 font with explicit editor punctuation glyphs; fresh Home and scene captures render ellipses correctly; ImGui keyboard navigation enabled with existing Tab/Enter input mapping | Live check of messages, visible keyboard focus, readable 100%/150% scaling and non-color states | [UX](progress/UX.md) |
| UX.4 | In progress | Optional action-driven first-creation lesson (`FirstCreationLesson`) with Why?, two hint levels, replay and a project-local completion record | Button-driven walkthrough through the final reopen and replay | [UX](progress/UX.md) |
| M1.1 | In progress | Project create/open/recent; GLB import with rollback; stable asset catalog; project GLBs in the Add library; reload with replacement validation; world-only projects open | Browse → preview → Add/Cancel, a valid reload and a corrupt reload through visible controls; relocate and reopen through the UI | [M1](progress/M1.md) |
| M1.2 | In progress | Nested hierarchy; world-preserving reparent; Move/Turn/Size gizmos; position, angle and scale snapping | Live drag; one-step undo/redo; save/reopen keeps IDs, hierarchy and appearance | [M1](progress/M1.md) |
| M1.3 | In progress | Template snapshots, instances, explicit updates, overrides, orphans and relink; collider and trigger baselines; Scene Templates tool; current scene format is version 21 | Interactive use of the tool; two instances with one override survive reload | [M1](progress/M1.md) |
| M1.4 | In progress | Dirty state; Save As; project-change and close prompts; Play isolation for character preview; path-graph recovery; extracted project/Play services and optional RPG module (E.2–E.3) | Manual close-cancel, Save As and project switch; interrupted-work recovery; repeated Play/Stop restoration | [M1](progress/M1.md) |
| M2.1 | In progress | Box colliders; selected-collider wireframe with visually reviewed solid/trigger captures; Collect and ReachGoal actions (scene version 16); saved Open actions request linked world-door destinations and CharacterStudio Play resolves cells/spawns, carries the controlled character, and returns to the authored starting cell on Stop; optional editor modules register stable-ID behaviours with creator-facing names and owner-specific diagnostics; scene version 20 saves assignments with undo/redo, Inspector choices, Play resolution, clone/placement and template-update baselines; template updates merge source assignments, preserve local overrides, retain removed overridden objects as orphans, and support undo/redo; the earlier Open-action slice and complete 373-test suite passed hosted CI [36708744681](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36708744681). What happens? panel; saved Play settings and stable player choice (scene version 18); selection-independent character control | Manual verification of door travel, collider bounds, template behaviour overrides, behaviour choices, and the complete Play workflow; current editor travel loads a single destination cell synchronously | [M2](progress/M2.md) |
| M2.2 | In progress | CharacterStudio pauses gameplay input, physics, interactions, animation advancement and its interaction-audio voice; SequenceFile v2 saves an optional stable-ID Reach goal trigger with version-1 migration; the existing sequence tool assigns, opens and saves sequences; reaching the bound trigger in Play starts the sequence on the runtime scene copy and restores the previous player pose and camera when it ends | Run the saved open/reopen, trigger playback, player/camera restore, repeated Play/Pause/Stop and interaction-audio checks; verify no duplicate behaviour callbacks | [M2](progress/M2.md) |
| M2.3 | In progress | Project-content staging; startup/cell scenes, GLBs, LOD assets and external buffer/image dependencies; world validation and packaged path data; registered sequences are reference-validated; scene version 21 stores stable-ID audio references; read-only preflight and `--validate-package` return scene/GLB/audio/sequence/file counts or diagnostics for discovered content errors; every file under `Assets/Audio` is included automatically | Build panel and executable publication; relocated playable game; run the authored package checks and visible publish workflow | [M2](progress/M2.md) |
| E.1 | Passed | Editor code is in `src/Ember.Editor`; CharacterStudio is a thin launcher and content sample | — | [E](progress/E.md) |
| E.2 | Passed | Feature-grouped files and independent panel/controller types; `EditorProjectSession`; largest current `Ember.Editor` C# file is 690 lines | — | [E](progress/E.md) |
| E.3 | Passed | Generic authoring and `Ember.Editor` have no RPG assembly references; RPG validation, recovery and Placement/Dialogue/Quest authoring load from the optional module; CharacterStudio builds and starts with that module disabled | — | [E](progress/E.md) |

## Next actions

Follow this order. Later phases are in [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md). Do not add
new default editor panels until the M1 gate passes.

1. **M0.4:** finish the visible editor checks in [MANUAL_GRAPHICS_GATE.md](MANUAL_GRAPHICS_GATE.md):
   folder/project controls, scene transform save/reopen, Play/Stop and recovery
   cancel/apply/close/reopen. Windowed Home resize/restore and the Home/showcase captures are
   recorded in [M0](progress/M0.md); hosted workflow
   [36817425269](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36817425269)
   confirms the centralized MGCB restore builds without MSBuild warnings.
2. **UX.1–UX.4 and M1 manual checks** through visible controls on the extracted editor:
   - project pickers; Browse → preview → Add/Cancel; a valid and a corrupt model reload (the
     prior preview stays active and the message explains the next action);
   - empty-scene actions; More tools switching; scene-view picking; layout reset; resize; DPI;
   - Move/Turn/Size with hover and drag, snapping, parent selection and reparenting; one-step
     undo/redo; save/reopen keeps transforms, IDs, hierarchy and appearance;
   - Scene Templates: two instances, one override, reload;
   - close-cancel, Save As and project-switch prompts;
   - the first-creation lesson through its final reopen and replay.
3. **UX.5:** run three novice sessions. Fix blockers and repeat the affected task.
4. Then continue the remaining M2.1 work, finish M2.2 interaction verification, and continue M2.3
   (phase P4). M2.3 now has persisted audio references and package preflight validation; next add
   the build panel and relocated executable workflow after the M1 gate. Use the
   [review's bounded follow-up checks](#recommended-follow-up-checks) to avoid treating existing
   runtime APIs as completed editor workflows.

## M0 — Establish a trustworthy baseline

Dependency: none. Prioritize this before adding features.

| ID | Work | Acceptance | Status |
| --- | --- | --- | --- |
| M0.1 | Repair soak verdicts and diagnostics. | Record actual errors including committed-travel cleanup failures; validate transition count, timeouts, latency, resource trends, and memory separately. Failures produce nonzero exit status. Inject a cleanup error, resource growth, incomplete run, and 1-transition case; each is handled correctly. Export full raw samples and run metadata. | **Passed** |
| M0.2 | Run live lifecycle coverage. | At least 50 transitions spanning two interiors and exterior boundaries, plus repeated scene reload and play/stop; compare like-for-like resource counts after warmup. Exercise delayed/failed loads and retry, save/restart, and unique identities. No unexplained growth or swallowed errors. Keep this separate from a longer timed soak. | **Passed** |
| M0.3 | Establish performance and distribution evidence. | Run a minimum 30-minute mixed editor/runtime session on a named PC; record frame tails, owned resources, memory and dependency/package size. Set explicit reference-scene budgets. Reproduce a relocated build with no checkout dependency; separately record whether an SDK-free machine was tested. | **Passed** |
| M0.4 | Audit persistence and add repeatable validation. | Recheck old data-safety findings against current code; cover atomic restore, failed writes, interrupted save, cancellation cleanup and schema errors. Add [Windows build/CPU-test CI](../.github/workflows/windows-engine.yml) and a documented [graphics/manual gate](MANUAL_GRAPHICS_GATE.md); keep RPG checks separate. | **In progress** |
| M0.5 | Split progress evidence by milestone. | Move ENGINE_PROGRESS.md evidence into one file per milestone under `Docs/progress/`, keeping every record, commit reference and artifact path. ENGINE_PROGRESS.md becomes a short index. New evidence goes into the matching milestone file. | **Passed** |

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

## E — Editor project extraction

Dependency: M0.4. Complete before the remaining UX.1–UX.4 and M1 interactive verification.
This is a behaviour-preserving restructure: no new editor features, and no change to project
or scene file formats. Record tests and Release captures before and after each step.

| ID | Work | Acceptance | Status |
| --- | --- | --- | --- |
| E.1 | Create `src/Ember.Editor` and move the editor out of `samples/CharacterStudio`. | Editor UI, tools, play control and project session live in `Ember.Editor`. CharacterStudio is a thin sample that starts the editor; Home remains the default, and the bundled showcase scene opens through `--open`. All tests pass; Release captures of Home, starter scene and showcase scene match the pre-move captures. | **Passed** |
| E.2 | Split `CharacterStudioGame.cs` and `CharacterStudioEditorUi.cs`. | Separate panels, tools, play controller and project session types; no source file over about 800 lines. Behaviour, command history and saved data are unchanged; tests and captures match. | **Passed** |
| E.3 | Remove the `Ember.Authoring` → `Ember.Rpg` reference. | RPG authoring moves to an optional module. Generic authoring and the editor build and start without Ember.Rpg. RpgSlice, Campaign and the RPG check still build and pass. | **Passed** |

Gate: the editor is a separate project with clear boundaries, and no user-visible behaviour
changed. Manual UX and M1 verification then runs against this structure.

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
| M1.4 | Dirty state, recovery and editor service boundaries. | Open/reload/close cannot silently discard edits; recover interrupted work after validation. Paths and sequences follow the same policy. Generic startup works without RPG data; play/stop cannot mutate authored state. | **In progress** |

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

Gate: create a furnished, lit scene using editor actions, with no handwritten JSON or
source changes; restart and recover the same scene. Record the complete action sequence and pass UX.1–UX.5; a developer-only walkthrough is insufficient.

## M2 — Make and deliver a small game

Dependency: M1. Reuse existing controller, physics, behavior, audio, and packaging code.

| ID | Work | Acceptance | Status |
| --- | --- | --- | --- |
| M2.1 | Persist and inspect colliders, player/camera settings, input actions and behavior assignments. | Author a controllable character, collision, trigger and interaction in the editor; save/reopen and run them through the shared runtime. Invalid assignments report the owning object. Offer beginner action presets (collect, open, reach goal) through a What happens? panel; advanced component bindings remain optional. | **In progress** |
| M2.2 | Complete play/pause/stop and game–sequence handoff. | Repeated play/stop restores the scene and input/audio ownership; trigger a cutscene, then return control to the correct player/camera without duplicate behaviors. | **In progress** |
| M2.3 | Build panel and dependency-complete runtime output. | Validate and publish a self-contained Windows game with referenced scenes, assets, audio and sequences. Move output outside the checkout and play it; missing dependencies block publication with useful diagnostics. | **In progress** |

Gate: an editor-authored 3–5 minute interaction demo starts, plays, saves/reloads its
small state, and exits from a relocated package. No sample-specific source wiring is
needed for the demonstrated workflow. Full RPG systems are not this gate. Include the optional UX.6 designer mission using the same saved project and editor commands.

## L — Learning path from presets to code

Dependency: L.1 depends on UX.4; L.2 depends on M2.1 and L.1; L.3 depends on L.2 and M2.2;
L.4 and L.5 depend on L.3. The concept order, missions and transfer checks are in
[CURRICULUM.md](CURRICULUM.md). The L track extends the teaching goal beyond action presets,
so learners can progress to semi-professional skills. It does not replace M1–M5 gates, and
a mission becomes available only after its feature gate passes.

| ID | Work | Acceptance | Status |
| --- | --- | --- | --- |
| L.1 | Declarative lesson and mission data. | Move the first-creation lesson from code into bundled versioned data with steps, "Why?" text, a hint ladder, completion conditions over scene state, play events, command history and saved files, plus a transfer task. The existing lesson behaves the same from data. Invalid lesson data reports the file and step without affecting the project. A new Tier 1 mission is added without engine code changes. | Not started |
| L.2 | Visual When → If → Do rules. | Author event, condition and action blocks with variables and timers in the Inspector, through normal undoable commands; save/reopen and run them through the shared runtime. A rule trace in Play shows which rules fired and why others did not. Existing Collect/ReachGoal presets are expressible as rules. Tier 3 missions 3.1–3.5 are playable and pass novice transfer checks. | Not started |
| L.3 | C# behaviours with reload in Play. | Create a project behaviour from the editor, edit it in an in-editor or external editor, and reload it without restarting the editor. Compile and run-time errors name the file, line and owning object in plain language. Public properties appear in the Inspector. A visual rule can be shown as equivalent C#. Behaviours package with M2.3 output. Custom code cannot corrupt authored data during Play. | Not started |
| L.4 | Programming puzzles. | Ship at least three code missions with fixed goals checked from game state, and optional measures (time, steps or code size) shown as feedback, never as locks. A learner who solves one puzzle solves a different one without hints. | Not started |
| L.5 | Concept progress view. | Show learned concepts and creations from the project-local progress record, with links to replay any mission. No tool is hidden or disabled by progress. | Not started |

Gate: an observed learner moves one interaction from preset to visual rule to C#
behaviour in the same project, explains the rule at each level, and makes a different
behaviour in code without step-by-step hints.

## C — Career mode stages

Dependency: C.1 depends on L.1 and the M2 gate. Each later stage depends on the previous
stage's gate and the features listed for its missions in [CURRICULUM.md](CURRICULUM.md).
Career mode reveals tools by stage; Free Create stays ungated and opens the same projects.
Stages 1–2 are the first-release target. Stages 3–4 follow. Stages 5–9 are post-release
expansions and each needs a playable prototype before full content.

| ID | Work | Acceptance | Status |
| --- | --- | --- | --- |
| C.1 | Career shell and Stage 1, My First Game. | Career save, stage briefing and debrief, staged tool reveal and Stage 1 missions run from L.1 data. A novice completes Stage 1 and its transfer checks (UX.5 method). Switching to Free Create shows all tools and opens the same project unchanged. | Not started |
| C.2 | Stage 2, Game Jam Weekend. | Theme draw, scope picker, story-time budget and playtest recorder. Jam ratings react only to measurable results (completable, rule clarity from test data, theme tag). Missing the story deadline changes the outcome but never blocks editing. Needs L.2. | Not started |
| C.3 | Stage 3, first indie-store release. | HUD/menu builder, runtime save API, store page editor and simulated reviews tied to crashes, completion rate and store-page accuracy. The stage project packages and runs outside the checkout. Needs L.3 and M2.3. | Not started |
| C.4 | Stage 4, large-store launch. | Profiler panel, options and rebinding builder, achievements, QA checklist and simulated sales tied to performance, bugs and content. Needs M3 and M4. | Not started |
| C.5 | Stage 5, co-op with a friend. | Named checkpoints, change packages, scene diff and three-way merge with conflict resolution, and review comments. Works with a real second person and with a simulated collaborator. Concepts map to Git terms. | Not started |
| C.6 | Stage 6, indie studio. | Task board, milestones, bug tracker and simulated teammates who deliver pre-authored content from the player's specs. Spec gaps produce visible, explained gaps in the delivery. | Not started |
| C.7 | Stage 7, AA specialist. | At least two discipline tracks, each with its own missions and handoffs to simulated colleagues. Replaying with another track reuses the same studio project. | Not started |
| C.8 | Stage 8, AAA planning. | Design document editor, UML class/sequence/state diagrams, dependency planner and a simulated production run that assembles a playable result from pre-built modules. Plan gaps appear as specific explained defects. Prototype one system before full stage content. | Not started |
| C.9 | Stage 9, online world. | Local network simulator with several clients, latency and loss, server-authority checks, persistence and economy tools, reusing Ember.Rpg quests and world streaming. No real online service is required. | Not started |

Gate for each stage: observed novices complete the stage project and its transfer checks,
and most choose to continue to the next stage.

## EA — Early access release

Dependency: C.2 gate (which requires M2, L.1 and L.2). M3 and M4 are not required. This
release makes career Stages 1–2 and Free Create public before the full release. M5 remains
the full release definition.

| ID | Work | Acceptance | Status |
| --- | --- | --- | --- |
| EA.1 | Early access package and installation. | Self-contained Windows installer or archive with the editor, Stage 1–2 content, starters and Free Create. Clean install on a machine without the SDK or checkout; career save and projects survive an update. Publish supported features, known issues and licenses. | Not started |
| EA.2 | Early access feedback and evidence. | In-app way to report a problem with an optional project attachment, chosen by the user. Re-run the Stage 1–2 novice sessions on the packaged build; record completion, transfer, crashes and whether players continue. Fix blockers before publishing. | Not started |

Gate: a packaged early access build passes clean installation and observed novice sessions
for Stages 1–2. Film, generation and later stages remain clearly marked as not yet available.

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

## Working rules

1. Read [AGENTS.md](../AGENTS.md), this roadmap and [BUILDING_BLOCKS.md](BUILDING_BLOCKS.md).
   Inspect existing implementations before adding one.
2. Keep scope tied to the current milestone gate. Do not resume archived RPG expansion.
3. Split large tasks into independently verifiable changes before coding.
4. Run the relevant tests and the solution build for code changes, plus UI, graphics or audio
   checks when the acceptance requires them. Record limitations and failed checks honestly.
5. Append evidence to the matching file in `progress/` and update the status here.
6. Preserve user content. Do not commit, push or publish unless the owner asks.

## After the first release — career expansion

Use UX.6 and C.1–C.2 feedback before building later stages. Reuse editor commands, project
formats and runtime; keep Free Create available and teach transferable game-development
concepts. Prototype each later stage (C.5–C.9) with one playable session before writing its
full content. Real online services, social features and an immersive UI rewrite are not
required for the first release.

## Review of the current build — 30 September 2026

Reviewed `c13749b6de5a60be6cbdc78b958a3a7b18131e28` after a clean fast-forward pull
from `16d92cf`. This review covers source, project references, milestone evidence and
live hosted CI results. Its UX.3 follow-up below records a concrete font rendering defect
found in a saved capture and fixed afterward.

### What is built

| Area | Source-backed findings | What that enables today |
| --- | --- | --- |
| Runtime and rendering | `Ember.Engine` owns the host, static/skinned GLB import, animation playback/crossfade, attachments, lighting/shadows, LOD/culling/instancing, terrain/water, input and audio. Supported import restrictions remain documented in BUILDING_BLOCKS.md. | Code-driven scenes and existing character/rendering samples. This does not establish full PBR, arbitrary character rigs or large-map capacity. |
| Physics and game interactions | BEPU world, fixed stepping, capsule controller, input maps and collision-aware path following exist. `SceneStaticColliderSet` and `ScenePlaySession` dispatch saved Collect/ReachGoal actions. Scene version 18 persists the selected player object alongside movement, capsule, camera and key settings. | The editor can preview a selected or deterministic first character and simple trigger interactions on a runtime scene copy. |
| World and optional RPG | Cell streaming/travel, exterior loading, world identities, navigation and persistence exist. Optional RPG code supplies inventory/equipment, dialogue/quests, stats, combat, schedules, trade and related systems; RpgSlice and Campaign remain consumers. | Reusable mechanics and integration fixtures. A smaller Morrowind-style game remains a capacity/content experiment, not a verified editor-authored product. Archived RPG expansion stays closed. |
| Editor architecture | CharacterStudio's Program is a thin launcher. `Ember.Editor` has separate panels, project session and Play controller. Generic engine/authoring/editor references exclude RPG and samples; `Ember.Scripting` has no MonoGame dependency. RPG tools load through an optional module. | E.1–E.3 foundations are present. Historical capture and no-RPG startup/publish evidence is in progress/E.md. |
| Scene/project authoring | Home starters, native pickers, asset catalog/import/reload, hierarchy, gizmos, snapping, templates/overrides/relink, undo/redo, dirty state and recovery are implemented. The tooling font is bundled with its license. | Most of the first editing loop has code and CPU coverage. Full mouse/button use, DPI and loss-prevention acceptance still need observation. |
| Teaching | `FirstCreationLesson` detects actual editing/Play/save/reopen actions. Why?, hints, replay and project-local learning progress exist. | A coded first-creation lesson foundation; it is not the declarative mission system or career mode. |
| Film and export foundations | Sequence evaluation, camera tracks/cuts, file serialization, playback/seek and numbered PNG export with a manifest exist. `BuildSequencePreview` constructs a sequence from the first skinned actor and fixed Wide/Close camera tracks. | Generated sequence previews and frame-export plumbing. The Film starter does not establish an editable multi-actor timeline or the M3 film gate. |
| Packaging foundations | `EngineProjectPackage` stages validated startup/world scenes, referenced GLBs/LOD assets and their external buffers/images, stable-ID audio references, registered sequences, path files and explicit extra content. Its read-only preflight reports package counts or diagnostics for independently discoverable content errors. | Relocatable project content and a backend for future Build UI. It does not itself publish a playable executable. |

### Findings that affect the next work

1. **The current bottleneck is workflow evidence.** M0.4, UX.1–UX.4 and M1 have
   implementations but incomplete interactive acceptance. Additional screenshots or CPU
   tests cannot close resize/DPI/focus, undo/save/reopen, recovery or novice-observation gates.
   E extraction is finished work; its M0.4 dependency and the overall release gate remain open.
2. **Player selection now persists.** Scene version 18 stores a selected object ID; Play no
   longer changes the controlled character when Inspector selection changes. Older scenes choose
   a stable actor/character ID where available. The Play setup lets creators choose an animated
   character/actor or use automatic stable-ID order; static GLBs are excluded from automatic control.
   Open/custom actions, collider visualization and interactive verification remain open.
3. **M2 needs a complete game loop.** Open/custom behaviours and collider visualization are
   missing. Play/Stop exists, but game pause and a saved trigger → cutscene → correct
   player/camera handoff are not established. Sequence pause alone does not meet M2.2.
4. **Content packaging is ahead of game delivery.** The GLB dependency work should be reused,
   not reimplemented. M2.3 still needs audio/sequence closure, visible build diagnostics,
   executable publication and a relocated game that plays without sample-specific wiring.
5. **Film, generation and teaching are different levels of readiness.** M3 has runtime/export
   foundations but no editable timeline. M4 recipes and generators remain planned work.
   L.1–L.5, C.1–C.9 and EA.1–EA.2 remain planned work: the coded lesson and console router
   do not constitute visual rules, reloadable project C# behaviours or career stages.
6. **Capacity claims remain narrow.** M0.2/M0.3 have recorded historical lifecycle and
   performance evidence. This review did not rerun the long soak, GPU captures or package
   installation. The paired-Fox budgets above do not predict a dense town, regional map or
   many animated NPCs. Repeat final-scene measurements under M5 before publishing limits.
7. **Font coverage needed more than a font file.** The Home capture showed question marks
   where the source used ellipses. ImGui's default glyph range omitted several punctuation
   characters used by editor labels. UX.3 now loads those explicit ranges; fresh Home and
   scene captures show the ellipses. Interactive DPI and focus checks remain open.

### Recommended follow-up checks

Follow Next actions first. These are small checkpoints within existing tasks, not new
milestones or permission to skip M0/UX/M1 dependencies.

- [ ] **M0.4:** perform one temporary-project save/reopen/resize/PlayStop session, then
  recovery cancel/apply/close/reopen and a project/world switch during review. Retain logs
  and exact steps; verify no stale recovery is applied to another target.
- [ ] **UX.1/UX.3:** measure scene area at 1280×720; check reset/resize, 100%/150% DPI,
  keyboard focus and readable error/disabled states. Record each result separately.
- [ ] **UX.2/UX.4/M1:** run the visible starter → import/preview → Add → transform → undo
  → Play/Stop → save → relocate/reopen loop; test corrupt reimport, template instances,
  close-cancel and lesson replay. Fix observed failures within these tasks.
- [ ] **UX.5:** observe three first-time users and the later unaided repeat; retain timings,
  confusion and transfer results. Keep the gate pending until participants exist.
- [ ] **M2.1, after M1:** saved player identity, collider wireframe, saved Open action, stable-ID
  behaviour registry, scene-version-20 behaviour IDs and Inspector choices are implemented. Verify
  player choice, collider display, Open-action save/reopen, delete/undo, template guard, movement,
  triggers and behaviour resolution; preserve assignments through template updates and connect the
  Open request to world cell travel.
- [ ] **M2.2:** implement game pause and one saved cutscene trigger; verify repeated handoff
  restores player/camera/input/audio ownership and Stop restores authored data.
- [ ] **M2.3:** complete dependency collection and failure diagnostics before the build panel;
  then publish and play a 3–5 minute editor-authored demo outside the checkout. Test missing
  dependencies and keep valid output safe on failure.

Review validation: local Release build passed with 0 warnings/errors; all **370 CPU tests**
passed with 0 failed/skipped; the separate RPG save/load check passed. Hosted Windows CI
[36604834954](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36604834954)
was confirmed successful at the reviewed commit. Fresh interactive graphics, audio, DPI,
novice sessions, no-RPG publish/startup, soak and relocated-install checks were **not run**.
Detailed review evidence is in [progress/roadmap-changes.md](progress/roadmap-changes.md),
with current build/CI evidence in [progress/M0.md](progress/M0.md).

## Review of synchronized code at 5d13487 — 30 September 2026

Fetched `origin` and confirmed the clean local `master` already matched `origin/master` at
`5d134877604d4f7677dcb1998e905bd41f9a6a10`; there were no newer commits to pull. Reviewed
the changes since `c13749b`, the current status table, source paths, milestone records and
the latest hosted Windows workflow. This review did not run local tests.

### Current implementation and roadmap state

- **Passed:** M0.1–M0.3 and M0.5, plus editor extraction E.1–E.3, retain their recorded
  acceptance evidence. This review did not re-run their soak, distribution or no-RPG gates.
- **In progress:** M0.4, UX.1–UX.4, M1.1–M1.4, M2.1 and M2.3 have implementation foundations,
  but their acceptance columns still list missing checks. The 30 September UX.3 glyph fix
  adds explicit punctuation ranges; current Home and scene captures render ellipses correctly.
- **M2.1 update:** scene version 18 persists an explicit player object ID alongside movement,
  camera and input settings. Play uses that saved choice independently of Inspector selection;
  old scenes migrate to a stable eligible actor/character where available. Missing or invalid
  assignments report an object-specific problem. Deleting the player clears the choice in the
  same undoable edit, and template updates that would remove it are refused. The Release build
  and legacy-scene capture are recorded in [M2 evidence](progress/M2.md). Dropdown interaction,
  save/reopen, Play control, delete/undo and template-update behavior remain unverified manually.
- **Still open in M2.1:** the selected-collider wireframe is implemented, but its visible behavior
  remains unverified; the saved Open action and custom behaviour registration are also open.
  M2.2 remains Not started. M2.3 stages and
  validates referenced project content, but audio/sequence dependency closure, the build panel,
  executable publication and a relocated playable game remain open.
- **Later work remains Not started:** UX.5–UX.6, L.1–L.5, C.1–C.9, EA.1–EA.2 and M3–M5.
  The coded first-creation lesson is not the declarative mission system or career mode; sequence
  runtime/export foundations are not an editable film timeline.

### Latest verification and issue

| Check | Result |
| --- | --- |
| `git fetch origin` and branch comparison | PASS — clean worktree; `master` equals `origin/master` at `5d13487`; no pull was needed |
| Release solution build | PASS locally; the latest hosted build failed on concurrent MGCB tool restoration (see run [36697453266](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36697453266)) |
| Hosted Windows CI [36696334028](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36696334028) | FAIL — 368/370 tests passed; two `SceneFileTests` expected version 17 while current scene serialization writes version 18. The workflow's subsequent solution build succeeded. |
| Corrective change and local Release build | PASS — both assertions now compare against `SceneFile.CurrentVersion`; `dotnet build Ember.sln --configuration Release --no-restore --nologo` succeeded with 0 warnings and 0 errors |
| Hosted Windows CI [36697453266](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36697453266) | TESTS PASS — 370/370; BUILD FAIL — concurrent `dotnet tool restore` calls contended for `dotnet-mgcb.3.8.5.1.nupkg` while building RpgSlice |
| Hosted Windows CI [36702138994](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36702138994) | PASS — 370/370 tests; solution build succeeded with one MSB3073 warning for a concurrent MGCB tool restore, which completed successfully later in the build |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo -m:1` | PASS locally — 0 warnings and 0 errors |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` with `Directory.Build.rsp` | PASS locally — 0 warnings and 0 errors; hosted confirmation was pending at this historical review point (see latest review below) |
| Manual editor, DPI, recovery and novice checks | NOT RUN — no new interactive acceptance evidence was gathered |
| Local test suite | NOT RUN — per the user's instruction; the hosted run was triggered by the earlier push |

The first CI failure came from stale expected-version checks in
`SceneFileTests.SaveAndLoadPreservesBoxColliderLocalShapeAndTriggerFlag` and
`SceneFileTests.SaveAndLoadPreservesTriggerActionAndRequiresTriggerCollider`; both now follow
`SceneFile.CurrentVersion`, and the next hosted run passed all 370 tests. That run's parallel
solution build hit contention between MonoGame tool restores. The repository now sets MSBuild
maximum parallelism to one in `Directory.Build.rsp`; the ordinary local CI build command passes
with 0 warnings and 0 errors. At this review point, hosted confirmation was pending; see the
latest review below.
The native app inventory returned no open
windows, so the M0.4 manual checks remain unavailable here. M0.4 remains In progress, and no
acceptance status was promoted by this review.

## Review of code at ad88043 — 30 September 2026

Ran `git pull --ff-only`; `origin/master` was already current at `ad88043b`. The worktree was
clean. Reviewed commits since `5d13487`, the affected source and progress records, the roadmap,
and hosted Windows CI [36703285933](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36703285933).
No local tests were run, and no acceptance status was promoted.

### Current implementation and roadmap state

- **Passed evidence remains unchanged:** M0.1–M0.3 and M0.5, plus E.1–E.3, retain their
  recorded acceptance evidence. This review did not repeat the soak, distribution, or no-RPG
  startup checks. The earlier [build review](#review-of-the-current-build--30-september-2026)
  records the wider runtime, editor, teaching, film, packaging, and capacity findings.
- **M0.4:** the two scene serialization assertions now follow `SceneFile.CurrentVersion`;
  hosted CI passes 370 CPU tests and completes the Release solution build. However, run
  36703285933 still reports one MSB3073 warning: MonoGame's `dotnet tool restore` exits 1 for
  `RpgSlice`, while another restore later succeeds and the solution build finishes. The
  repository `Directory.Build.rsp` sets `-maxcpucount:1`, but this hosted result shows that it
  did not remove the warning. The recorded ordinary local Release build is clean. Manual
  resize, save/reopen, Play/Stop, and recovery checks remain open.
- **M2.1:** scene version 18 persists an explicit player object ID alongside movement, camera
  and input settings. Play uses that saved choice independently of Inspector selection; old scenes
  migrate to a stable eligible actor/character where available. Missing or invalid assignments
  report an object-specific problem. Deleting the player clears the choice in the same undoable
  edit, and template updates that would remove it are refused. Dropdown interaction, save/reopen,
  Play control, delete/undo and template-update behavior remain unverified manually.
- **Still open in M2.1:** the selected-collider wireframe is implemented, but its visible behavior
  remains unverified; the saved Open action and custom behaviour registration are also open.
  M2.2 remains Not started. M2.3 stages and validates referenced project content, but
  audio/sequence dependency closure, the build panel, executable publication and a relocated
  playable game remain open.
- **Other in-progress tasks:** UX.1–UX.4, M1.1–M1.4, and M2.3 retain code foundations but
  have incomplete acceptance. Their remaining interaction, recovery, novice, dependency-closure,
  build-panel, and relocated-playable-game checks are listed in the status table and milestone
  files. M2.2, UX.5–UX.6, L.1–L.5, C.1–C.9, EA.1–EA.2, and M3–M5 remain Not started.
- **Release boundary:** the current code has useful runtime and authoring foundations, but there
  is not yet evidence for the complete beginner workflow or a packaged editor-authored game or
  film. The paired-Fox performance budget does not establish dense-town or regional-world capacity.

### Latest verification

| Check | Result |
| --- | --- |
| `git pull --ff-only` and branch state | PASS — already up to date; clean `master` at `ad88043`, equal to `origin/master` |
| Hosted Windows CI [36703285933](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36703285933) | PASS — 370/370 CPU tests; Release solution build succeeded with one MSB3073 `dotnet tool restore` warning for `RpgSlice` |
| Hosted Windows CI [36705346633](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36705346633) | PASS — 370/370 CPU tests; Release solution build succeeded with 0 warnings and 0 errors |
| `Directory.Build.rsp` effect | The warning did not recur in the next hosted run; retain the earlier warning as an intermittent issue to monitor, not as a current build failure |
| Local Release solution build with the repository response file | PASS in the recorded M0 evidence — 0 warnings, 0 errors; not repeated in this review |
| Selected solid and trigger collider Release captures | PASS in [M2 evidence](progress/M2.md) — both 1280×720 captures visually show the expected wireframes |
| Manual editor, DPI, recovery, collider-interaction, and novice checks | NOT RUN — no new interactive acceptance evidence was gathered |
| Local test suite | NOT RUN — per the user's instruction; hosted tests ran automatically in CI |

The MGCB restore warning occurred in run 36703285933 but did not recur in 36705346633. Keep the
repository response setting and monitor later builds for recurrence. M0.4 remains open for its
manual workflow checks. The collider captures establish that the overlay renders, not that editor
selection and transform updates work. The next actions above reflect these remaining checks.

## M2.1 Open action slice 30 September 2026

The saved Open action uses the trigger owner's existing `WorldDoorComponent`. Scene format version
19 accepts this action only when the trigger has a linked door; older-version documents and invalid
orphaned template baselines are rejected. Template updates and undo/redo preserve both the action and
its door destination.

On trigger entry, `ScenePlaySession` reports the stable trigger and character identities plus the
linked destination to the world host through `SceneAuthoredActionEvent`. The What happens? Inspector
offers **Open door** only after a link is present, and CharacterStudio reports the request during its
single-scene preview. It does not switch cells; integrating a world host with editor Play remains
open.

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — all solution projects, including tests, compiled with 0 warnings and 0 errors |
| Open-action save/load, validation, runtime-event and template-update tests | PASS in hosted rerun [36708744681](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36708744681); all 373 tests passed after four existing version-18 assertions were corrected |
| Manual Open selection, save/reopen, and cell travel | NOT RUN — desktop interaction and world-host handoff remain unverified |
| M0.4 hosted check [36705346633](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36705346633) | PASS — 370/370 tests and warning-free Release build on the preceding documentation-only commit |
| Follow-up hosted check [36708152257](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36708152257) | FAIL — 369/373 tests; the new Open-action tests passed, while four existing tests asserted scene version 18; commit `71e6888` corrected them |
| Follow-up local Release build after correcting those expectations | PASS — 0 warnings and 0 errors; tests were not run locally |
| Corrected hosted rerun [36708744681](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36708744681) | PASS — 373/373 tests; Release build succeeded with 0 errors and one intermittent MGCB restore warning for `RpgSlice` |

No task status was promoted. Remaining M2.1 work is the world-host cell-change handoff, custom
behaviour registration, and manual verification of collider editing and the Play workflow.

## M2.2 saved trigger-to-sequence checkpoint — 1 October 2026

The existing sequence workflow now saves an optional Reach goal trigger in `SequenceFile` v2 and
loads v1 files without a trigger. CharacterStudio exposes sequence open/save and trigger selection.
During Play, the selected trigger starts the sequence against the runtime scene copy, pauses gameplay
and interaction audio, then restores the previous preview pose and camera. Play resumes only when it
was running before the cutscene. A sequence evaluation failure also restores the Play session.

| Check | Result |
| --- | --- |
| Release solution build redirected to a temporary output directory | PASS — all solution projects compiled with 0 warnings and 0 errors |
| Sequence serialization and atomic-save regression tests | NOT RUN — per the owner's instruction |
| CharacterStudio saved trigger and cutscene handoff workflow | NOT RUN — window interaction was unavailable; follow the steps in `MANUAL_GRAPHICS_GATE.md` |

M2.2 remains In progress. The task is not ready to pass until the authored tests and repeated visible
Play, trigger, camera/player restoration, audio and Stop checks are complete.

## M2.3 sequence/audio content checkpoint — 1 October 2026

Saving a `.sequence.json` file inside an open project now registers its project-relative path in the
existing `extraContent` list. The same registration runs for the editor's command-line save option.
Files saved outside the project remain saved, but CharacterStudio reports that packaging will omit
them. Before staging, `EngineProjectPackage` resolves each registered sequence against one packaged
scene, its skinned GLB assets, animation clips, camera tracks and optional trigger. Missing or
ambiguous references stop package creation with a sequence-specific diagnostic. Audio discovery,
the build panel and a relocated playable executable remain open M2.3 work. Files under
`Assets/Audio` are now staged recursively as well. The scene format has no persisted audio reference,
so package preflight cannot yet distinguish missing sounds from unused ones; that closure remains
open until audio references are authored and validated.

| Check | Result |
| --- | --- |
| Release solution build to a temporary output directory | PASS — all projects compiled with 0 warnings and 0 errors |
| Sequence/audio package relocation and missing-sequence-reference regression coverage | NOT RUN — test source added; execution is deferred to the owner |
| CharacterStudio save-sequence registration and package/reopen workflow | NOT RUN — desktop interaction was not performed; steps are in `MANUAL_GRAPHICS_GATE.md` |

No M2.3 acceptance status was promoted.

## M2.3 scene audio references — 1 October 2026

Scene format version 21 now persists an audio asset catalog with stable IDs and normalized,
project-relative paths. References reject empty IDs, absolute paths, parent traversal, duplicate IDs
and duplicate paths. Scene cloning preserves the catalog. Package collection resolves every audio
reference for the startup scene and world cells, rejects missing files and reparse points before
creating output, and stages referenced audio even when it is outside `Assets/Audio`. The standard
`Assets/Audio` folder remains recursively included for convenient project imports.

| Check | Result |
| --- | --- |
| Release solution build to a temporary output directory | PASS — all projects compiled with 0 warnings and 0 errors; no tests were run |
| Scene version-21 audio reference round-trip and pre-version rejection coverage | NOT RUN — regression coverage is authored; execution is deferred to the owner |
| Package inclusion, relocation and missing-audio preflight coverage | NOT RUN — regression coverage is authored; execution is deferred to the owner |
| CharacterStudio audio authoring and relocated publication workflow | NOT RUN — no editor interaction was performed; a creator-facing audio panel is not implemented |

M2.3 remains In progress. Next: add the build/publication workflow after the M1 gate, then verify a
relocated playable output. Runtime playback authoring still uses the existing behaviour APIs; this
slice establishes persisted asset references and dependency validation only.

## M2.3 read-only package preflight — 1 October 2026

`EngineProjectPackage.Validate(projectFilePath)` now runs the package collector without creating a
destination. A valid result contains scene, GLB, unique referenced-audio, sequence and package-file
counts; an invalid result contains actionable diagnostics for discovered content errors. Destination
validity remains a separate caller responsibility.
`Create()` remains the publishing boundary and independently runs the same preflight before staging
files. Regression coverage exercises both a valid audio/sequence
project and multiple missing referenced-audio diagnostics, and confirms that failure leaves the
package destination and its parent absent. The eventual Build panel can call `Validate()` to display
readiness before enabling publication; this API does not add editor UI.

| Check | Result |
| --- | --- |
| Release solution build to a temporary output directory | PASS — all projects, including CharacterStudio and the test assembly, compiled with 0 warnings and 0 errors; tests remain deferred to the owner |
| Valid, invalid and multi-error read-only preflight regression coverage | NOT RUN — test source is authored; execution is deferred to the owner |
| MinimalGame `--validate-package` output and exit-code smoke | NOT RUN — the command was compiled but not launched; its process-level behavior remains unverified |
| Visible Build-panel preview and publish workflow | NOT RUN — no panel was added before the M1 interaction gate |

M2.3 remains In progress. Next: use this backend from the creator-facing Build panel after the M1
gate, then verify a relocated playable output. See [the roadmap](../ENGINE_ROADMAP.md).
