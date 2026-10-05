# L — Learning path evidence

## L.1 Declarative lesson and mission data — 5 October 2026

The first-creation lesson now loads its steps, explanations, Why text, two hints, choices,
completion feedback, actions and transfer task from an embedded, versioned JSON definition.
The existing guide area renders the lesson data, while scene changes, Play transitions, undo,
save and reopen continue to supply completion facts. A second Tier 1 mission is bundled as
data and stays unavailable until its M1.2 feature gate passes.

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo -p:BaseOutputPath="C:\Users\admin\AppData\Local\Temp\Ember\l1-lesson-data-build\"` | PASS — all solution projects built; 0 warnings and 0 errors |
| PowerShell `Get-Content -Raw | ConvertFrom-Json` over `src/Ember.Authoring/Lessons/*.lesson.json` | PASS — both version-1 files parsed (9 and 2 steps) |
| CPU test suite | NOT RUN — requested by the owner |
| CharacterStudio lesson walkthrough, reopen and replay | NOT RUN — requires visible UI interaction; the owner could not operate the window |
| Novice observation | NOT RUN — no user sessions were performed |

Still open: review the guide in CharacterStudio and complete the UX.4 interaction gate; run the
lesson CPU tests when authorized. L.1 remains In progress because its UX.4 dependency and visible
acceptance are not passed. The second mission remains disabled until M1.2 passes.

## P3 mission replay harness — 5 October 2026

Added one versioned scripted solution for each bundled lesson. The test harness discovers every
bundled lesson and requires exactly one matching script, then replays scene creation and movement
through `SceneCommandHistory`, prediction choices, Play transitions, Undo, and a real scene
save/reopen. Each script declares the expected lesson step after every action and the final replay
must satisfy all completion conditions. The disabled `move-two-objects` mission is replayed as
content validation but remains unavailable in the editor until M1.2 passes.

| Check | Result |
| --- | --- |
| Mission replay script JSON and build-output copy | PASS — both scripts parse and are copied to `Ember.Engine.Tests` output |
| Script-to-definition coverage review | PASS — both scripts name one of the two bundled lesson definitions; the executable coverage assertion compiled but was not run |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — solution and replay harness compiled with 0 warnings and 0 errors |
| CPU replay and lesson tests | NOT RUN — per the owner's instruction; record for the owner to run later |
| First-creation guide walkthrough and lesson replay in CharacterStudio | NOT RUN — visible UX.4 check remains pending |

L.1 remains In progress. The scripted test is ready to provide CI coverage when the CPU suite is
run; this change does not claim the replay passed. The UX.4 and novice-observation gates remain
open.

## L.6 mission project checkpoint service — 5 October 2026

Added the offline checkpoint storage and restore service used by the planned mission rewind flow.
Capture streams the authored project files to a versioned ZIP with per-file SHA-256 and an atomic
replacement, stored outside the project. Restore checks the archive manifest, project-root key,
mission ID, paths, lengths and content hashes before swapping anything. It stages beside the live
project, validates the staged project manifest, then replaces the project directory with rollback
if the second rename fails. The current project-local lesson-progress file and current Git/editor
metadata are copied forward; reparse points are rejected. The editor's Rewind control is not wired
yet, so this is the storage/service slice of L.6.

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — full solution and failure-path test assembly compile with 0 warnings and 0 errors; an initial path-check overload error was corrected before the passing build |
| Checkpoint capture/restore, corruption, identity and failure-path tests | NOT RUN — per the owner's instruction; run later |
| Visible Rewind action in the lesson/mission flow | NOT IMPLEMENTED — later L.6 integration step |

L.6 remains In progress until the service and first-capture behavior tests are run and the visible
Rewind action is walked through with success and failure cases.

## L.6 mission rewind flow — 5 October 2026

The existing lesson guide now creates a mission checkpoint the first time a lesson starts and keeps
that original baseline when the learner replays it. Its Rewind to mission start action rejects Play,
restores through the staged checkpoint service, marks the intentionally discarded scene edits clean,
reloads the project and restarts the same lesson. The guide reports restore failures and retained
recovery copies. Starting a mission with unsaved scene edits is blocked with a save instruction. A
focused CPU test covers first-capture idempotence and restoration; the test has only been compiled.

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — all solution projects, including the new checkpoint test, compiled with 0 warnings and 0 errors |
| Checkpoint store and lesson replay CPU tests | NOT RUN — per the owner's instruction |
| Visible lesson Rewind success, Play rejection, corrupt archive and retained progress checks | NOT RUN — the owner could not operate CharacterStudio |

Still open: run the CPU tests and visible checks above. L.6 remains In progress; this source change
does not claim rewind was exercised in the editor.

## L.1/L.6 lesson and checkpoint CPU verification — 5 October 2026

The Release CPU suite on `8b401b0` ran the previously deferred lesson/checkpoint tests. Full
logs and `engine-tests.trx` are in [the M0 verification artifact root](M0.md#m04-resumed-automated-and-runtime-verification--5-october-2026).

| Check | Result |
| --- | --- |
| FirstCreationLessonTests | PASS — 6/6 |
| LessonMissionScenarioTests | PASS — 1/1 scripted mission scenario |
| MissionProjectCheckpointStoreTests | PASS — 5/5 capture, restore, corruption/identity/failure and first-capture coverage |
| ProjectLearningProgressStoreTests | PASS — 1/1 |
| Visible lesson/replay/rewind, retained progress and novice transfer checks | NOT RUN — no control automation or novice observation |

Still open: data-driven lesson acceptance and the visible Rewind success/failure workflow. These
CPU checks verify services and scripted progression; they do not complete the teaching gates.
