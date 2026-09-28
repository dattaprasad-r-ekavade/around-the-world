# Latest progress and creator-experience review — 28 September 2026

Reviewed revision: `3c9620e`. The 27 September assessment below is historical; its
soak defects and old test failure are not the current baseline. No runtime/UI changes
were made in this review. The active design is CREATOR_EXPERIENCE.md and ENGINE_ROADMAP.md.

## Progress since the previous review

- M0.1–M0.3 have recorded passes: corrected soak reporting, live multi-interior and
  editor lifecycle checks, a 30-minute reference session and relocated distribution.
  These are prior recorded runs, not freshly repeated live tests in this review.
- Atomic writes, staged world restore, cancellation cleanup and Windows CI were added.
  M0.4 remains In progress: the latest recorded remote run failed and interactive
  manual checks remain unrun. Commit 36dd000's title does not override this evidence.
- M1.1 now has project create/open/recent services and GLB import with rollback.
  Relocated static/animated content has recorded graphics coverage. Native pickers,
  full asset-browser usability and button-driven integration are still incomplete.
- Fresh local `dotnet test Ember.sln --configuration Release --no-restore --nologo`:
  **283 passed, 0 failed, 0 skipped**. The previous review's 254/255 result is historical.
- Fresh RPG Release check: **PASS**, save/load equals original.
- Fresh Release solution build: **blocked by output file locks** from the already-running
  CharacterStudio process (PID 39692), with MSB3027/MSB3021 copy failures. It was left open;
  this run is not a successful full build. Existing progress records contain prior passes.
- Remote CI status was not refreshed: `gh` is unavailable. Treat the failed run in the
  progress log as last recorded evidence, not a confirmed current remote status.

## Current usability finding — high priority

Inspected the M1.1 `relocated-project.png` artifact and current UI code. The image is
stored under `%TEMP%/Ember/CharacterStudio/M11ProjectWorkflow-39786d2da1694cacbd16f61e6a8236e4`.
It shows project/scene panels on the left, World Cells across the middle, and RPG tools
covering sequence controls on the right. The scene is largely obscured. In
`CharacterStudioEditorUi.cs`, Draw calls all these panels; RPG and sequence preview
share initial coordinates. `CharacterStudioProjectUi.cs` requires typed paths.

This is a confirmed default-layout problem, not just a subjective preference about
styling. Raw manifests, cloning terminology, numeric inputs and unrelated RPG controls
precede the beginner's first useful action. This review used a saved screenshot and
source inspection; it did not claim a fresh interactive usability study.

## Updated direction

Usability moves from M5 polish to required UX.1–UX.5 work at M1: Home, scene-first layout,
visual/picker-based actions, contextual detail, safe feedback, a short creation lesson,
and observed novice completion. M2 adds simple interaction presets and UX.6, an optional
mission to build and play a challenge using the same commands and project format.
M3/M4 inherit the same simplicity requirements. A later immersive designer-game shell
can build on evidence from this loop; it is not a separate engine rewrite.

The next verification priority remains M0.4. The next editor implementation priority
is UX.1 with M1.1 integration, before adding further advanced default panels. New UX
work is Not started; this update changes the plan and acceptance criteria only.

## Follow-up verification — 28 September 2026

Windows CI run #4 failed on the nine-cell queue test's tight wall-clock budget. Commit `361078f`
made elapsed-time checks controllable and separated them from broad scheduling assertions. Commit
`145c892` added forced atomic destination-replacement failure coverage; hosted run
[#6](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36390512874)
passed its Windows build and CPU suite. The current local suite passes 285 tests and the Release
build passes. A 1280×720 Release scene capture rendered the courtyard and animated characters on
Windows 11, .NET 9.0.19 and Intel UHD Graphics; resize, save/reopen and Play/Stop remain unverified.
The desktop-control helper failed to initialize in this session, so M0.4 remains In progress.

The local UX.3 slice adds direct actions for an empty scene and a clear prompt when the scene has no
imported models. The Release build passes and a 1280×720 empty-scene capture shows the guidance;
button behavior, picker flow, focus, resize and DPI still require interactive verification. See
[ENGINE_PROGRESS.md](ENGINE_PROGRESS.md) for the capture and current evidence boundary.
The optional tools menu now closes competing panels during selection; its mouse transitions still
need the same interactive verification.
The Inspector also reports a pending model preview as a preview rather than an empty scene; this
state still needs a live Browse/Add/Cancel walkthrough.
Save and model-operation failures now receive Inspector recovery hints, but the rendered error and
dismissal states still need live verification.

The local M1.1 follow-up adds a stable project asset catalog, exposes unreferenced project GLBs in
the Add library, and adds a selected-model reload action with replacement-resource validation.
Targeted tests and the Release build pass; browse/preview/Add/Cancel and reload failure feedback
remain unverified in the running editor.

---

# Project and scope review — 27 September 2026

Reviewed source baseline: `d081422`, plus existing untracked user content (not modified).
Scope: repository architecture, active documentation, selected runtime/editor/sequence/
packaging code, soak implementation, solution build and CPU checks. This is not a full
rendering audit or a fresh live UI, soak, export, or distribution acceptance run.

## Assessment

The initial assessment is broadly right: Ember has substantial reusable 3D foundations
and recorded evidence of a packaged playable RPG slice. The central gap is a consistent,
proven creation workflow. More RPG features would not directly deliver the requested
lightweight engine/scene generator for both games and cutscenes.

Keep the implementation and change the product priorities. CharacterStudio is a useful
editor starting point, not yet evidence of a complete editor-authored game or film.

## Existing foundations and their limits

| Area | Evidence inspected | Assessment |
| --- | --- | --- |
| Runtime | Ember.Engine scene/assets/render/physics/audio modules; FirstLight and other solution consumers | Reusable runtime exists; current build succeeds. Windows/backend constraints remain. |
| Scenes and editor | SceneFile, SceneCommandHistory, ScenePlaySession, CharacterStudio editor | Stable IDs, persistence, undo infrastructure, clone-based play and editing exist. Full editor workflow and generic behavior authoring still need integrated proof. |
| Cinematics | SceneSequence, SceneSequencePlayer, SequenceFile, SequenceFrameExport and editor export controls | Clip/camera evaluation, persistence, preview and frame export exist. Do not recreate these; add complete timeline authoring and prove output parity. |
| Worlds and RPG | WorldCellStreamer, persistence/travel tests, RpgSlice and Ember.Rpg | Useful optional world/game layer. Broad legacy integration gates remain pending despite checked implementation rows. |
| Packaging | EngineProjectPackage and publish scripts | Startup scenes, world cell scenes, GLBs and extra content are collected. Historical outside-repo runs are documented; arbitrary editor-authored dependency closure needs proof. |
| Generation | Terrain/procedural graphics utilities and placement infrastructure | Building blocks exist; a reusable recipe → preview → apply → editable scene workflow is new scope. |
| Modularity | Ember.Authoring references both Engine and Rpg | Generic authoring should become usable independently of RPG content; split by workflow needs, not a wholesale rewrite. |

## Priority findings

1. **High — soak verdict overclaims coverage.** `RpgSliceTransitionSoak.BuildReport`
   hardcodes zero accumulated errors and labels counts stable; final PASS checks only
   absolute working-set delta below 30 MiB. Collected post-warmup records are not used
   to establish resource trends. `RpgSliceGame` logs committed-travel cleanup failures
   without feeding them to the report, and the completion path exits without using a
   structured verdict. A one-transition run also produces a zero warmup cutoff and
   indexes `warmupCutoff - 1`. Fix and failure-test the harness before accepting its gate.
2. **High — expansion approval exceeds evidence.** The previous decision disagrees with
   the actual soak resource counts, confuses transition latency with frame time, and
   approves 16×16 expansion and density limits without a corresponding acceptance run.
   Replaced with a limited-baseline decision; historical text is retained for traceability.
3. **Resolved after review — activation-step assertion was too exact.** The original full
   run returned 254 passed and one failure in
   `CellActivationQueueTests.NineCellsRespectPerFrameCostAndCellLimitsIncludingSlowPreparation`:
   expected three activation steps, observed four (`CellActivationQueueTests.cs:68`). On
   28 September, the test was corrected to accept additional partial-budget steps while still
   requiring at least three, and to yield while async preparation is incomplete. The engine
   queue was unchanged; see the follow-up verification below.
4. **Medium — editor and runtime claims need a shared acceptance scene.** The old roadmap
   still lists Release B/C, editor authorship, and broad world-system gates as pending.
   Prove selection/edit/undo/save/reopen, play/stop, collision/interaction, and output
   from the same authored project rather than combining unrelated sample successes.
5. **Medium — frame-tail attribution and optimization need care.** Terrain GPU buffers
   are created in `HeightmapTerrainRenderer.GetChunk` during rendering. Move CPU work
   to preparation and bounded GPU uploads to the graphics-owning thread if profiling
   confirms the bottleneck. An unmeasured timing remainder does not prove DWM caused it.
6. **Medium — roadmap and product identity conflict.** README calls Ember deliberately
   nongeneral while the roadmap targets an expansive RPG, and implementation history
   declares 100% while integration gates remain pending. The replacement roadmap
   establishes one direction and separates evidence from planned capability.

## Verification in this review

- `dotnet build Ember.sln --nologo`: passed, 0 warnings, 0 errors.
- `dotnet test tests/Ember.Engine.Tests/Ember.Engine.Tests.csproj --no-build --nologo`:
  254 passed, 1 failed, 0 skipped; failure described above.
- `dotnet exec tests/Ember.Rpg.Check/bin/Debug/net9.0/Ember.Rpg.Check.dll`:
  passed (`save then load equals original`).
- No new live graphics, audio, soak or packaging run was performed. Prior runs remain
  historical evidence, with limitations described in EXPANSION_DECISION.md.

## Scope decision

The active sequence is reliability → unified scene editor → playable output → cutscene
output → deterministic scene generation → usability/release proof. Existing components
are retained. New RPG mechanics and regional scaling are deferred. The roadmap defines
small end-to-end acceptance projects and explicit dependencies without promising dates
or treating the old checklist count as engine readiness.

Resolution verification, 28 September: Release build passed with 0 warnings/errors; the full
suite passed 283/283; and the focused activation-queue test passed five consecutive runs. Its
exact-step expectation was not a queue invariant because the last partial frame budget can be
shared with another cell. A separate earlier run also reported async preparation timeouts while
the full suite and RPG check ran concurrently; the full suite passed when run alone, so that
contention hypothesis remains unproven and should be revisited if the timeouts recur.
