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
3. **High — baseline test failure observed.** The full run returned 254 passed and one
   failure in `CellActivationQueueTests.NineCellsRespectPerFrameCostAndCellLimitsIncludingSlowPreparation`:
   expected three activation steps, observed four (`CellActivationQueueTests.cs:68`).
   Investigate timing assumptions and queue behavior; do not silently inherit the old
   255/255 claim. This documentation task does not change runtime/test implementation.
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

Targeted follow-up: the failing activation-queue test passed when rerun alone with
`--filter FullyQualifiedName~NineCellsRespectPerFrameCostAndCellLimitsIncludingSlowPreparation`.
This suggests an intermittent failure; it does not erase the full-suite failure or
establish its root cause. Keep the baseline issue open until investigated.
