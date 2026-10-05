# Ember development plan

Draft, 29 September 2026. This plan orders the work in the [active roadmap](ENGINE_ROADMAP.md),
the [career curriculum](CURRICULUM.md) and the [pacing proposals](CAREER_PACING.md) into
phases. The roadmap remains the source of truth for task status and gate evidence. This
plan does not mark anything as done.

Sizes are relative effort, not dates: **S** (days), **M** (1–3 weeks), **L** (1–2 months),
**XL** (more than 2 months) for one developer. The roadmap commits to no calendar estimate,
and neither does this plan. Items marked *(new)* are proposals not yet in the roadmap.

## Guiding rules

1. **Finish before starting.** At most one phase is in active implementation. Close its
   exit criteria before the next phase starts, except the parallel work listed per phase.
2. **Test with people at every phase end.** Each phase that changes what learners see
   ends with at least one novice session, using the UX.5 method. Record results in
   the matching evidence file in `progress/`.
3. **Real work stays real.** Simulation is added around real building, never instead of it.
4. **Free Create is never gated.** Every phase keeps the same project opening in both modes.
5. **Prototype before content.** Every career stage gets a playable paper or rough
   prototype before its full mission content is written.

## Phase overview

| Phase | Goal | Main roadmap IDs | Size | Release point |
| --- | --- | --- | --- | --- |
| P0 | Close baseline and persistence checks | M0.4, M0.5 | S | — |
| P1 | Move the editor into its own project | E.1–E.3 | M | — |
| P2 | Verify the editor workflow with novices | UX.1–UX.5, M1.1–M1.4 | M | — |
| P3 | Lesson data, codex and mission tooling | L.1, L.5 | M | — |
| P4 | Playable game loop in the editor | M2.1–M2.3, UX.6 | L | — |
| P5 | Career shell and Stage 1 | C.1 | L | Internal alpha |
| P6 | Visual rules and Stage 2 | L.2, C.2, EA.1–EA.2 | L | Early access |
| P7 | C# behaviours and Stage 3 | L.3, L.4, C.3 | XL | Early access update |
| P8 | Film, generation and Stage 4 | M3, M4, C.4, M5 | XL | First full release (M5) |
| P9 | Collaboration and Stage 5 | C.5 | L | Expansion |
| P10 | Studio stages 6–7 | C.6, C.7 | XL | Expansion |
| P11 | AAA and online stages 8–9 | C.8, C.9 | XL | Expansion |

## P0 — Close baseline and persistence checks

Goal: a trusted starting point before structural changes.

- Finish M0.4 interactive checks: resize, save/reopen, Play/Stop, recovery cancel/apply/close.
- Rerun Windows CI on the current commit.
- M0.5 (done 29 September 2026): progress evidence split into per-milestone files under `progress/`.

Exit: M0.4 and M0.5 **Passed** with recorded evidence. CI green.

## P1 — Move the editor into its own project

Goal: the editor stops being a sample, so later phases can build on clean boundaries.
Do this before P2's manual verification, so that manual evidence is recorded against the
final editor structure and does not need repeating.

| ID | Work | Exit |
| --- | --- | --- |
| E.1 | Create `src/Ember.Editor`. Move editor UI, tools and play control out of `samples/CharacterStudio`. | CharacterStudio becomes a thin sample that starts the editor. All tests pass. Release captures match before/after. |
| E.2 | Split `CharacterStudioGame.cs` and `CharacterStudioEditorUi.cs` into panels, tools, play controller and project session. | No file over about 800 lines. No behaviour change. |
| E.3 | Remove the `Ember.Authoring` → `Ember.Rpg` reference. Move RPG authoring into an optional module. | Generic authoring builds without Ember.Rpg. RpgSlice and its checks still pass. |

Method: behaviour-preserving refactor, with tests and captures before and after each move.

Exit: E.1–E.3 done; CI green; Release capture comparison recorded.

## P2 — Verify the editor workflow with novices

Goal: close the M1 gate.

- Walk through every open manual check in the roadmap handoff: pickers, Browse → preview →
  Add/Cancel, reload with a corrupt GLB, gizmos, snapping, reparenting, undo/redo,
  save/reopen, layout reset, resize and 150% DPI.
- Fix what fails. Do not add new default panels.
- Run UX.5 with three first-time users. Fix blockers and repeat the affected task.

Exit: UX.1–UX.5 and M1.1–M1.4 **Passed**. M1 gate recorded.

## P3 — Lesson data, codex and mission tooling

Goal: lessons become content, not code, so stages can be written quickly.

- L.1: lesson and mission data format and runner. Convert `FirstCreationLesson` to data with
  the same behaviour.
- *(new)* Mission test harness: CI replays a scripted solution for each mission and checks
  its completion conditions.
- L.6: "Rewind mission": a versioned project checkpoint before each mission, with safe restore.
- *(new)* Codex skeleton: concept pages with plain text, industry term and a "try it" scene.
- *(new)* Mission authoring template with a "pain point" field for each mission.
- L.5: concept progress view.

Parallel: write Stage 1 mission drafts as data while the runner is built.

Exit: first lesson runs from data; one new Tier 1 mission added without engine code;
harness runs in CI; rewind tested.

## P4 — Playable game loop in the editor

Goal: a small game can be made, played and shipped from the editor. Stage 1 depends on it.

- M2.1: explicit player choice, Open action, sound feedback assignment, collider visualization,
  custom behaviour registration, manual verification.
- M2.2: repeated play/stop ownership and cutscene handoff.
- M2.3: build panel, full asset/audio/sequence closure, relocated playable package.
- UX.6: "Build a tiny playable challenge" mission, written as L.1 data.

Exit: M2 gate. An editor-made 3–5 minute demo runs from a relocated package.

## P5 — Career shell and Stage 1 (internal alpha)

Goal: the first playable piece of the game.

1. Paper-prototype Stage 1 with one novice before building the shell.
2. Build the career shell: career save, briefing and debrief screens, staged tool reveal,
   return recap, Relaxed and Standard modes, Free Create entry.
3. Build the first hub scene (bedroom studio) as an Ember scene.
4. Add Ember, the mentor character, with the hint ladder and "Show me" demonstrations.
5. Write Stage 1 missions and transfer checks from CURRICULUM.md.
6. Add a simple release screen with measured results (completable, time to finish).

Exit: C.1 gate. Novices finish Stage 1 in the 45–60 minute target range, pass transfer
checks, and most want to continue. Internal alpha build packaged.

## P6 — Visual rules and Stage 2 (early access)

Goal: the first public release: Stages 1–2 plus Free Create.

- L.2: When → If → Do rules, variables, timers, rule trace in Play.
- *(new)* Bot playtester: one configurable bot that plays a build and records stops, fails,
  finish time and stuck spots.
- *(new)* Reviews that teach: comments from bot measurements, each linking to a mission.
- *(new)* Contracts v1: "Fix my broken game" and "Build to spec", used as spaced review.
- C.2: jam theme draw, scope picker, story-time budget, playtest recorder, jam ratings.
- *(new)* "One game grows with you": the jam project carries into Stage 3.
- EA.1–EA.2: early access package, installer, known issues and feedback channel. This
  release does not require M3/M4; M5 remains the full release definition.

Exit: C.2 gate; early access checklist passed (clean install, relocated run, novice test).

## P7 — C# behaviours and Stage 3

Goal: learners write real code; the jam game becomes a polished release.

- L.3: C# behaviours with reload in Play, plain-language errors, Inspector properties,
  rule → C# view. Decide the compiler approach (for example Roslyn) with a spike first.
- L.4: three programming puzzles with optional optimization targets and bundled histograms.
- HUD and menu builder; runtime save API; store page editor; simulated store reviews.
- C.3 missions.

Exit: C.3 gate; L gate (preset → rule → C# in one project, observed).

## P8 — Film, generation and Stage 4 (first full release)

Goal: all roadmap M-milestones done; the career reaches a large-store launch.

- M3: timeline authoring, preview parity, export UI.
- M4: generation recipes, room/outdoor generators, preview/apply/undo.
- Profiler panel, options and rebinding builder, achievements, QA checklist.
- C.4 missions, trailer mission from frame export, simulated sales.
- M5: rendering consistency, templates, installation, release evidence.

Exit: M5 release definition met; C.4 gate.

## P9 — Collaboration and Stage 5

Goal: two people can safely work on one project.

- Named checkpoints and history view.
- Change packages (export/import of a set of changes).
- Scene diff and three-way merge with conflict resolution by object and property.
- Review comments on changes.
- Sam, the simulated collaborator, who sends scripted change packages with planted mistakes.
- C.5 missions with Git term mapping.

Risk: scene merge is the hardest engine feature here. Spike it first on the stable scene IDs.

Exit: C.5 gate with one real pair of people and one simulated run.

## P10 — Studio stages 6–7

Goal: the player leads and then specializes.

- Task board, milestones, bug tracker, build automation mission.
- Simulated teammates with traits, delivering pre-authored content based on player specs;
  spec gaps produce explained defects.
- Office progression in the hub.
- C.6 missions.
- C.7: start with two discipline tracks (suggested: gameplay programmer and level designer),
  then add more based on player feedback.

Risk: content cost for teammate deliveries. Build a reusable library of modules first.

Exit: C.6 and C.7 gates.

## P11 — AAA and online stages 8–9

Goal: the planning and live-service end of the career.

- C.8: prototype one system first (for example a door state machine planned in a state
  diagram, built by the simulated studio). Continue only if the prototype is fun and teaches.
  Then add the design document editor, UML diagram editors, dependency planner and risk register.
- C.9: local network simulator with several clients, latency and loss; server-authority,
  persistence and economy missions; reuse Ember.Rpg quests and world streaming.

Risk: both stages need new systems with uncertain fun. Each has a stop decision after
its prototype.

Exit: C.8 and C.9 gates, or a recorded decision to redesign or drop a stage.

## Cross-cutting work

| Area | Work | When |
| --- | --- | --- |
| Testing | Unit tests per feature; mission harness from P3; Windows CI on every change | Always |
| Novice sessions | At least one per phase from P2; three for each gate | Phase ends |
| Performance | Keep M0.3 budgets; add editor start and Play-enter time budgets | From P1 |
| Content pipeline | Mission, contract, review and dialogue data; authoring template | From P3 |
| Art and audio | Licensed asset palette for starters, hub offices and characters | From P5 |
| Documentation | Keep roadmap short; evidence in per-milestone files; codex from P3 | Always |
| Accessibility | Scaling, keyboard use, non-color cues, captions | Every UI phase |

## Main risks

| Risk | Effect | Mitigation |
| --- | --- | --- |
| Solo developer bandwidth | Phases slip; many items stay half done | One active phase; strict exit criteria |
| Manual verification backlog | Gates never close | P2 as a dedicated phase; no new panels until it passes |
| Career mode is not fun | Learners stop after Stage 1 | Paper prototypes; "want to continue" is a gate measure |
| Content cost in Stages 6–11 | Later stages never ship | Data-driven missions, module library, one game grows with you |
| Scene merge and network simulation complexity | P9 and P11 stall | Spikes first, with stop decisions |
| Windows-only runtime | Smaller audience | Accept for early access; revisit after M5 |

## Immediate next actions

1. Finish M0.4 interactive checks (P0).
2. Start E.1 by moving editor code into `src/Ember.Editor` with before/after captures (P1).
3. Paper-prototype Stage 1 with one novice while P1 runs; it needs no code.
