# Roadmap and direction changes

Records of roadmap resets and product-direction decisions. Moved from ENGINE_PROGRESS.md on 29 September 2026 (M0.5); section text is unchanged. Add new entries at the end.

## Roadmap reset and current review — 27 September 2026

Replaced the RPG-first plan with the lightweight 3D engine/scene-generator roadmap.
Preserved the previous roadmap and expansion decision under Docs/archive. Added
PROJECT_REVIEW.md; corrected the active expansion status and qualified the old soak
report. Updated README product direction and stale sequence-persistence wording.

Build passed with zero warnings/errors. Full engine CPU tests: 254 passed, one activation
queue test failed (expected 3 steps, actual 4); RPG save round-trip passed. No new live
UI, graphics, soak or distribution proof is claimed. Existing user content was untouched.
Next implementation task: M0.1; also investigate the observed queue test failure in M0.
Targeted rerun of the failed activation-queue test passed (1/1). The full-suite failure
remains recorded; intermittent behavior requires investigation, not a blanket green claim.

## Creator-first roadmap update — 28 September 2026

Reviewed current revision 3c9620e, recent progress, editor drawing code, and the saved
M1.1 relocated-project screenshot. Confirmed overlapping always-visible panels obscure
most of the scene. Added CREATOR_EXPERIENCE.md and required early UX.1–UX.6 tasks to the
active roadmap. Updated M1–M5 acceptance and README direction: learn through small
visible edits and play; use optional lessons and later designer-game missions on the
same command layer and project data. Advanced tools remain available on demand.

At the time of this review, M0.1–M0.3 recorded passes were preserved; M0.4 and M1.1 remained
In progress, and new UX work was Not started. Fresh local Release test suite passed 283/283; RPG check passed.
Release solution build could not copy DLLs locked by running CharacterStudio (39692);
the session was not closed. Remote CI was not rechecked because gh is unavailable.
No fresh live UI/novice/soak test was claimed; no runtime implementation was changed.

Next: close M0.4 verification, then UX.1 workspace simplification with M1.1 integration.

## Explicit teaching-engine principle — 28 September 2026

Made the product promise explicit: Ember teaches users how to build games while they
build their own. Added a concept progression, explain/change/predict/play/reflect/vary
loop, offline contextual explanations and hints, and evidence of learning through an
unguided variation. Connected lesson scope to M1, M2 and M3 so lessons never depend on
unimplemented tools. Teaching is required in the first release; the later designer-game
presentation remains an extension of the same editor and project model.

## Roadmap status notes moved to evidence — 29 September 2026

These paragraphs were the roadmap's narrative status before the documentation cleanup. They are kept here unchanged; the roadmap now carries a short status table.

M0.1 through M0.3 have passed; M0.4, M1.1–M1.4, M2.1, M2.3 and UX.1–UX.4 are **In progress**; UX.5–UX.6,
M0.5, E.1–E.3, M2.2, L.1–L.5, C.1–C.9, EA.1–EA.2 and M3 onward remain **Not started** against their new acceptance criteria. Existing
components are reusable foundations, not a reason to repeat their implementation.
Documentation reset is complete. Review at `3c9620e`: M0.1–M0.3 have recorded passes;
M0.4 remains open despite a commit title saying it was closed. Its Windows workflow now
passes on the pulled baseline; interactive resize/save/reopen/play-stop evidence remains
incomplete. Recovery now has failure-injection coverage proving a locked snapshot replacement
preserves the prior snapshot, a failed multi-file apply rolls back earlier writes, and impossible
file/directory path collisions are rejected before staging. The reviewed activation-queue test now
allows valid partial-budget steps and the full local CPU suite passes. M1.1 services and native
project/model pickers exist but the
visible button-driven workflow is unproven. Selected-model reload builds a replacement preview before swapping it in, and the asset-ownership test confirms a corrupt GLB leaves the prior asset active. Verify that recovery through visible editor controls before closing the gate. UX.1 has a first local Home and scene-first
workspace implementation; resize and DPI checks remain. UX.2 has starter thumbnails, atomic
Game/Film project starters, native pickers, visible undoable Move/Turn/Size actions and scene-view
selection code, plus a temporary model preview with explicit Add and Cancel actions. Interactive
verification remains open. The Add library model list now has a case-insensitive path/name search
and a no-match message. Windows CI for `ababa01` exposed a time-budget-sensitive
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
