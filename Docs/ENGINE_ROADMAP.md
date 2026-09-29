# Ember roadmap: teaching game engine and career game

Updated: 29 September 2026. This is the **only active roadmap** and the only place where task
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

| Task | Status | Exists now | Remaining to pass | Evidence |
| --- | --- | --- | --- | --- |
| M0.4 | In progress | Windows CI (hosted run [36590746893](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36590746893) passed on `c0f0536`); atomic writes; failure injection for locked snapshot replacement, multi-file apply rollback and path collisions; cancellable recovery staging with a review UI | Interactive resize, save/reopen and Play/Stop checks; visible recovery cancel/apply/close/reopen | [M0](progress/M0.md) |
| UX.1 | In progress | Home with Game/Film/Open on an opaque background; scene-first workspace; More tools menu with exclusive panels | Scene area of at least 60% at 1280×720; reset layout; resize; 100% and 150% DPI; button-driven check | [UX](progress/UX.md) |
| UX.2 | In progress | Starter thumbnails; atomic Game/Film starters; native pickers; Move/Turn/Size buttons; scene-view selection; model preview with Add/Cancel; searchable Add library | Interactive walkthrough of the whole loop with no typed paths or required shortcuts | [UX](progress/UX.md) |
| UX.3 | In progress | Inspector explanations; next actions for an empty scene and an empty model list; pending-preview state; error recovery hints; bundled 16 px Source Sans 3 tooling font | Live check of messages, keyboard focus, readable 100%/150% scaling and non-color states | [UX](progress/UX.md) |
| UX.4 | In progress | Optional action-driven first-creation lesson (`FirstCreationLesson`) with Why?, two hint levels, replay and a project-local completion record | Button-driven walkthrough through the final reopen and replay | [UX](progress/UX.md) |
| M1.1 | In progress | Project create/open/recent; GLB import with rollback; stable asset catalog; project GLBs in the Add library; reload with replacement validation; world-only projects open | Browse → preview → Add/Cancel, a valid reload and a corrupt reload through visible controls; relocate and reopen through the UI | [M1](progress/M1.md) |
| M1.2 | In progress | Nested hierarchy; world-preserving reparent; Move/Turn/Size gizmos; position, angle and scale snapping | Live drag; one-step undo/redo; save/reopen keeps IDs, hierarchy and appearance | [M1](progress/M1.md) |
| M1.3 | In progress | Template snapshots, instances, explicit updates, overrides, orphans and relink (scene version 15); Scene Templates tool | Interactive use of the tool; two instances with one override survive reload | [M1](progress/M1.md) |
| M1.4 | In progress | Dirty state; Save As; project-change and close prompts; Play isolation for character preview; path-graph recovery | Manual close-cancel, Save As and project switch; interrupted-work recovery; service boundaries (see E.3) | [M1](progress/M1.md) |
| M2.1 | In progress | Box colliders; Collect and ReachGoal trigger actions (scene version 16); What happens? panel; controllable Play character; saved Play settings (scene version 17) | Saved player choice; Open action; custom behaviour registration; collider visualization; manual verification | [M2](progress/M2.md) |
| M2.3 | In progress | World validation before packaging; path data packaged; relocation tests reopen packaged worlds | Asset/audio/sequence closure; build panel; relocated playable game | [M2](progress/M2.md) |
| E.1 | Passed | Editor code is in `src/Ember.Editor`; CharacterStudio is a thin launcher and content sample | — | [E](progress/E.md) |
| E.2 | Passed | Feature-grouped files and independent panel/controller types; `EditorProjectSession`; no editor source file over 800 lines | — | [E](progress/E.md) |
| E.3 | Passed | Generic authoring and `Ember.Editor` have no RPG assembly references; RPG validation, recovery and Placement/Dialogue/Quest authoring load from the optional module; CharacterStudio builds and starts with that module disabled | — | [E](progress/E.md) |

## Next actions

Follow this order. Later phases are in [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md). Do not add
new default editor panels until the M1 gate passes.

1. **M0.4:** run the interactive checks in [MANUAL_GRAPHICS_GATE.md](MANUAL_GRAPHICS_GATE.md):
   resize, save/reopen, Play/Stop, and recovery cancel/apply/close/reopen. Rerun Windows CI.
2. **UX.1–UX.4 and M1 manual checks** through visible controls on the extracted editor:
   - project pickers; Browse → preview → Add/Cancel; a valid and a corrupt model reload (the
     prior preview stays active and the message explains the next action);
   - empty-scene actions; More tools switching; scene-view picking; layout reset; resize; DPI;
   - Move/Turn/Size with hover and drag, snapping, parent selection and reparenting; one-step
     undo/redo; save/reopen keeps transforms, IDs, hierarchy and appearance;
   - Scene Templates: two instances, one override, reload;
   - close-cancel, Save As and project-switch prompts;
   - the first-creation lesson through its final reopen and replay.
4. **UX.5:** run three novice sessions. Fix blockers and repeat the affected task.
5. Then continue the remaining M2.1 work, M2.2 and M2.3 (phase P4).

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
| M2.2 | Complete play/pause/stop and game–sequence handoff. | Repeated play/stop restores the scene and input/audio ownership; trigger a cutscene, then return control to the correct player/camera without duplicate behaviors. | Not started |
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
