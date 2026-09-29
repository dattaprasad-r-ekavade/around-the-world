# Working on Ember

Guide for anyone who changes this repository: developers and AI coding agents. Read this file
first, then the roadmap. It explains what the project is, where things are, how to build and
check them, and how to record your work.

## What Ember is

Ember is a Windows 3D engine and editor (C#, .NET 9, MonoGame WindowsDX, ImGui editor UI,
BEPU physics, GLB assets, versioned JSON files). The product goal is a **game about becoming
a game developer**: a career mode that teaches game development from a first game to AAA and
online games, using the real editor. Free Create gives every finished tool without progression.

The runtime and much of the editor exist. Editor verification with real users, lesson data,
visual rules, C# behaviours and all career stages are still to do.

## Read in this order

1. [Docs/ENGINE_ROADMAP.md](Docs/ENGINE_ROADMAP.md): the only source of task status. Start at
   **Current status** and **Next actions**. The Acceptance column defines done.
2. The evidence file for your milestone in [Docs/progress/](Docs/progress/): what already exists
   and how it was checked.
3. [Docs/BUILDING_BLOCKS.md](Docs/BUILDING_BLOCKS.md): the runtime API. Check here before adding
   a new system.
4. For UI or teaching work: [Docs/CREATOR_EXPERIENCE.md](Docs/CREATOR_EXPERIENCE.md) (design
   contract) and [Docs/CURRICULUM.md](Docs/CURRICULUM.md) (career stages and missions).
5. [Docs/README.md](Docs/README.md) lists every other document.

## Repository map

| Path | What it is | Notes |
| --- | --- | --- |
| `src/Ember.Engine/` | Runtime | `Engine/` host, `Render/`, `Assets/` GLB import, `Physics/`, `Audio/`, `Scene/` (scene model, `SceneFile`, `SceneCommandHistory`, `ScenePlaySession`), `Sequence/`, `World/`, `Project/` (project file, asset catalog, packaging), `IO/AtomicFile` |
| `src/Ember.Authoring/` | Editor-side services | Validation, recovery, scene templates, gizmo math, `FirstCreationLesson`, learning progress. References `Ember.Rpg` until task E.3. |
| `src/Ember.Scripting/` | Console command router | No MonoGame reference. Keep it that way. |
| `src/Ember.Rpg/` | Optional RPG layer | Never referenced by `Ember.Engine`. |
| `samples/CharacterStudio/` | The editor today | Main files: `CharacterStudioGame.cs`, `CharacterStudioEditorUi.cs`. Moves to `src/Ember.Editor` in E.1. |
| `samples/FirstLight/` | Contract sample | The smallest game on the engine. It must keep compiling. |
| `samples/Campaign/`, `samples/RpgSlice/` | Consumer games and fixtures | Never referenced from `src/`. |
| `templates/MinimalGame/` | New-project starter | Used by `tools/new-engine-project.ps1`. |
| `tests/Ember.Engine.Tests/` | xUnit CPU tests | Run in Windows CI. |
| `tests/Ember.Rpg.Check/` | RPG console check | Separate from the engine suite. |
| `Docs/` | Plans, design, reference, evidence | See [Docs/README.md](Docs/README.md). |
| `captures/` | Old screenshots | Put new evidence captures under `%TEMP%\Ember\...` and record the path. |

## Commands

Windows only. Requires the .NET SDK in `global.json`.

```powershell
dotnet build Ember.sln --configuration Release --nologo
dotnet test Ember.sln --configuration Release --no-restore --nologo
dotnet run --project tests/Ember.Rpg.Check --configuration Release
dotnet samples\CharacterStudio\bin\Release\net9.0-windows\win-x64\CharacterStudio.dll --open samples\CharacterStudio\Scenes\ReleaseAShowcase.json
dotnet samples\CharacterStudio\bin\Release\net9.0-windows\win-x64\CharacterStudio.dll --open samples\CharacterStudio\Scenes\ReleaseAShowcase.json --screenshot capture.png --warmup 8
```

- A running CharacterStudio locks its output files. Close it before building, or the build
  fails with MSB3027/MSB3021.
- CI (`.github/workflows/windows-engine.yml`) runs restore, the CPU tests and a Release build.
- Graphics, UI and packaging changes also need the manual checks in
  [Docs/MANUAL_GRAPHICS_GATE.md](Docs/MANUAL_GRAPHICS_GATE.md).
- More run, export and packaging commands: [Docs/RUNNING_AND_PACKAGING.md](Docs/RUNNING_AND_PACKAGING.md).

## How to do a task

1. Pick the task from **Next actions** in the roadmap. Do not start work from a later milestone
   unless the owner asks.
2. Read its Acceptance and its evidence file. Inspect the existing code before writing new code.
3. Split the work into small changes that can each be built, tested and checked.
4. Make the change. Follow the architecture rules below.
5. Build and run the tests. Add tests for new behaviour. For UI, graphics or audio acceptance,
   also run the manual checks, or record them as not run.
6. Append an evidence entry to the milestone file in `Docs/progress/` (format in
   [Docs/ENGINE_PROGRESS.md](Docs/ENGINE_PROGRESS.md)).
7. Update the task's row in the roadmap **Current status** table in the same change. Mark a task
   **Passed** only when its full Acceptance is met with evidence.
8. Commit only when the owner asks. Use Conventional Commit prefixes (`feat:`, `fix:`, `test:`,
   `docs:`, `refactor:`), as in the existing history.

## Architecture rules

- **Layers:** runtime (`Ember.Engine`) owns rendering, scene evaluation, physics, audio and
  lifecycle. Authoring owns commands, validation, recovery and generation. The editor owns UI.
  RPG code stays optional; generic editor startup must not need RPG data.
- **References:** nothing in `src/` references a sample. `Ember.Engine` never references
  `Ember.Rpg`. `Ember.Scripting` has no MonoGame reference.
- **Authored edits go through `SceneCommandHistory`** so undo/redo and dirty state work.
- **Authored data and play state stay separate.** Play runs on a copy (`ScenePlaySession`);
  Stop restores the authored scene. Play must never change saved data.
- **File writes use `AtomicFile`.** A failed save or import must keep the previous valid file.
- **Scene format changes:** bump `SceneFile.CurrentVersion`, load older versions, and add
  tests for the migration and for save/reopen.
- **Stable IDs** for scene objects, assets and templates must survive save, reopen, duplicate
  and template updates.
- **Beginner UI rules** (from CREATOR_EXPERIENCE.md): no typed paths, JSON or required
  shortcuts in the basic workflow; advanced values go in More details; errors say what
  happened, which object, and how to fix it.
- **Graphics resources** are created on the graphics-owning thread and have a clear owner.
- Keep the code style of the surrounding file. Avoid a large ECS or engine rewrite.

## Documentation rules

- Task status lives only in the roadmap. Other documents link to it instead of repeating it.
- Evidence goes in `Docs/progress/<milestone>.md`. Create a new milestone file when needed and
  add it to the index in ENGINE_PROGRESS.md.
- Design changes go in the matching design document (CREATOR_EXPERIENCE.md, CURRICULUM.md,
  CAREER_PACING.md). Phase order goes in DEVELOPMENT_PLAN.md.
- Write short, plain sentences. Say what is not verified. Never report an unrun check as passed.
- Historical documents stay unchanged except for a note at the top.

## Do not

- Do not mark UI, usability or novice-observation gates as Passed from unit tests or a
  developer-only walkthrough.
- Do not resume archived RPG expansion (tasks 145–152 in the archive).
- Do not add new default editor panels before the M1 gate passes.
- Do not delete or overwrite user content, projects or captures.
- Do not commit, push or publish unless the owner asks.
