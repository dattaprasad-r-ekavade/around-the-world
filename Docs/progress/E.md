# E — Editor project extraction

## E.1 Extract the editor project — 29 September 2026

Created `src/Ember.Editor` and moved the editor game, UI, tools, file pickers, project workflow,
play controls and supporting editor types out of `samples/CharacterStudio`. The editor now lives in
the `Ember.Editor` assembly and namespace. CharacterStudio contains only its 14-line launcher and
sample project/content pipeline; it references the editor project and keeps the existing Home-first
startup. The bundled showcase remains available through the normal `--open` argument. The E.1
acceptance wording was clarified to preserve the established Home-first UX documented in
`RUNNING_AND_PACKAGING.md` while keeping the showcase scene as the sample fixture.

The pre-move baseline was built from source commit `16d92cf` in a temporary archive. The post-move
Release build and captures use code commit `71b0031`. Before and after screenshots were visually
reviewed as matching at 1280×720.

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --configuration Release --nologo` | PASS — pre-move baseline and post-move build; 0 warnings, 0 errors |
| `dotnet test Ember.sln --configuration Release --no-build --no-restore --nologo` | PASS — post-move, 364 passed, 0 failed, 0 skipped |
| Home, Film starter and Release A showcase captures | PASS — before and after each exited 0 at 1280×720; stderr files empty; captures visually match |
| Capture configuration | Windows 11 Pro 10.0.26200; .NET SDK 9.0.302 / runtime 9.0.7; desktop 1536×864; NVIDIA GeForce RTX 4060 Laptop GPU driver 32.0.15.9174 and Intel UHD Graphics driver 32.0.101.7026; Release |
| Hosted Windows CI | PASS — [run 36580281473](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36580281473) for `8cc1a0e` |
| Before artifacts | `%TEMP%/Ember/ManualGraphicsGate/e1-git-baseline-e7739dd1f7e14818bb819c68c98dd4f4/` (`home.png`, `starter-scene.png`, `showcase-scene.png`, logs) |
| After artifacts | `%TEMP%/Ember/ManualGraphicsGate/after-editor-extraction-3b0ebad37e8c4f98b02b4b157bae8b0b/` (`home.png`, `starter-scene.png`, `showcase.png`, logs) |
| Code commit | PASS — `71b0031` (`refactor: extract editor project`) |

Still open: E.3 must remove the Authoring → RPG dependency. M0.4 and UX still need their live desktop
interaction gates.

## E.2 Editor feature groups, session, controller and panels — 29 September 2026

Split `CharacterStudioGame` into feature-group files for viewport input, rendering, diagnostics,
play, sequences, projects and assets. Extracted the actual editor UI state and behavior into
independent Workspace, World, Scene, Inspector, Dialogue, Quest, Path, Placement Template, Play
Settings and Project Validation panel types, alongside the existing Home and Scene Template panels.
Moved unsaved-change prompts into `UnsavedChangesController`. Added `EditorProjectSession` to own the
open project, cached asset catalog and recent-project persistence; project switching invalidates the
catalog through that session. `CharacterStudioPlayController` now owns and performs play start/stop,
runtime updates, input, character/path following, interaction audio and physics cleanup; the game
class keeps thin forwarding methods. The largest C# file under `src/Ember.Editor` is 790 lines.

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — 0 warnings, 0 errors |
| `dotnet test Ember.sln --configuration Release --no-build --no-restore --nologo` | PASS — 364 passed, 0 failed, 0 skipped |
| Home, Film starter and Release A showcase captures | PASS — each exited 0 at 1280×720; stderr files empty; Home and starter are byte-identical to prior captures, and showcase is visually unchanged |
| After artifacts | `%TEMP%/Ember/ManualGraphicsGate/after-e2-all-panels-ba869fdaa9fd4a868a5fe22775db5bb1/` (`home.png`, `starter-scene.png`, `showcase-scene.png`, logs) |
| Max editor source file | PASS — 790 lines (`CharacterStudioEditorUi.WorldPanel.cs`) |
| Hosted Windows CI | PASS — [run 36590746893](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36590746893) for `c0f0536` |

E.2 is **Passed**: the panels and Play controller now own their feature behavior, all editor source
files are within the size target, and build, tests and captures pass. The live desktop interaction
gate remains unverified under M0.4.

## E.3 Optional RPG authoring module — passed, 29 September 2026

Moved `AuthoredProjectValidator` and `AuthoredProjectRecoveryService` into the new
`Ember.Authoring.Rpg` assembly and removed the `Ember.Authoring` → `Ember.Rpg` project reference.
Added a generic editor tool extension contract and loader for app-local `Modules`, then moved the
Placement, Dialogue and Quest authoring UI into the optional `Ember.Editor.Rpg` module. CharacterStudio
copies that module beside the app by default and can omit it with `EnableRpgAuthoringModule=false`.
The generic World authoring panel retains Templates, Travel and Paths.

| Check | Result |
| --- | --- |
| `dotnet build src/Ember.Authoring/Ember.Authoring.csproj --configuration Release --nologo` | PASS — 0 warnings, 0 errors |
| `dotnet build src/Ember.Editor/Ember.Editor.csproj --configuration Release --nologo` | PASS — 0 warnings, 0 errors; project has no RPG project references |
| `dotnet build Ember.sln --configuration Release --nologo` | PASS — 0 warnings, 0 errors; builds CharacterStudio, Campaign, RpgSlice and both optional RPG modules |
| `dotnet test Ember.sln --configuration Release --no-build --no-restore --nologo` | PASS — 368 passed, 0 failed, 0 skipped, including generic authoring/editor boundary and extension-loading tests |
| `dotnet run --project tests/Ember.Rpg.Check --configuration Release --no-build --no-restore` | PASS — save then load equals original |
| RpgSlice `--persistence-smoke` | PASS — drop, loot, kill, follower travel, interior entry, save/restart and exactly-once verification; stderr empty |
| Campaign `--windowed --seed 173 --bot --bot-minutes 0.05` | PASS — bot reached town and exited at its time limit with zero deaths; stderr empty |
| CharacterStudio publish with `EnableRpgAuthoringModule=false` | PASS — publish contains no `Ember.Rpg.dll`, `Ember.Authoring.Rpg.dll`, `Ember.Editor.Rpg.dll` or `Modules` folder |
| Generic CharacterStudio Home and Release A showcase captures | PASS — both exited successfully and saved 1280×720 PNGs under `%TEMP%/Ember/ManualGraphicsGate/e3-no-rpg-a94a9517143b4b2ab977363702412592/` |
| CharacterStudio with RPG module enabled, Home/showcase captures | PASS — 1280×720; empty stderr; Home is byte-identical to the prior capture and showcase is visually unchanged under `%TEMP%/Ember/ManualGraphicsGate/e3-rpg-module-ab8aca92a17349db8ff2e3200ede7a15/` |
| Before captures | PASS — Home/showcase PNGs under `%TEMP%/Ember/ManualGraphicsGate/e3-plugin-4be9b42ae65e4313ab5bf996de06d6d2/`; Home after change is byte-identical, and showcase is visually unchanged |

E.3 is **Passed**: generic `Ember.Authoring` and `Ember.Editor` have no direct RPG assembly references,
the RPG panels, validation and recovery live in the optional module, and generic CharacterStudio
publishes and starts without any RPG assembly present. RpgSlice persistence and Campaign bot smokes,
the RPG round-trip check and all engine tests pass. The M0.4 live resize, save/reopen, Play/Stop and
recovery-dialog checks remain a separate manual desktop gate.
