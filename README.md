# Ember

**A game about becoming a game developer, where the tools are a real engine.**

Ember is a lightweight 3D engine and editor for Windows, built on C# and MonoGame. It is
being shaped into a teaching product: in career mode, you grow from making your first
one-room game to a game jam, store releases, working with a friend, running a studio, and
planning AAA and online games. Each stage explains the basics, then adds new tools. Free
Create gives you every finished tool with no progression.

**Current state (29 September 2026):** the runtime (rendering, GLB models and animation,
physics, audio, world streaming, sequences, packaging) and much of the editor exist. The
editor workflow is still being verified, and the teaching and career features are planned,
not built. See the [roadmap status](Docs/ENGINE_ROADMAP.md#current-status) for exact state.

## Start here

| If you want to... | Read |
| --- | --- |
| Work on this repository (people or AI agents) | [AGENTS.md](AGENTS.md) |
| Know what to do next and what is done | [Docs/ENGINE_ROADMAP.md](Docs/ENGINE_ROADMAP.md) |
| Understand the product and teaching design | [Docs/CREATOR_EXPERIENCE.md](Docs/CREATOR_EXPERIENCE.md), [Docs/CURRICULUM.md](Docs/CURRICULUM.md) |
| Find any document | [Docs/README.md](Docs/README.md) |
| Run the samples or package a project | [Docs/RUNNING_AND_PACKAGING.md](Docs/RUNNING_AND_PACKAGING.md) |
| Write game code against the runtime | [Docs/GAME_CODE_GUIDE.md](Docs/GAME_CODE_GUIDE.md), [Docs/BUILDING_BLOCKS.md](Docs/BUILDING_BLOCKS.md) |

## Quick start

Requires Windows and the .NET 9 SDK (see `global.json`). Ember uses MonoGame's WindowsDX
backend and WinForms, so it is Windows-only today.

```powershell
dotnet build Ember.sln
dotnet test Ember.sln
```

Open the editor with the showcase scene:

```powershell
dotnet samples\CharacterStudio\bin\Debug\net9.0-windows\win-x64\CharacterStudio.dll --open samples\CharacterStudio\Scenes\ReleaseAShowcase.json
```

Run the smallest game built on the engine:

```powershell
dotnet samples\FirstLight\bin\Debug\net9.0-windows\win-x64\FirstLight.dll --windowed
```

More commands (screenshots, sequence export, new projects, packaging and publishing) are in
[Docs/RUNNING_AND_PACKAGING.md](Docs/RUNNING_AND_PACKAGING.md).

## Repository layout

```
src/Ember.Engine/        Runtime: host, rendering, GLB assets, animation, physics, audio,
                         scenes, sequences, worlds, packaging. Windows, MonoGame.
src/Ember.Authoring/     Editor-side services: validation, recovery, scene templates,
                         gizmo math, lessons. (Still references Ember.Rpg; see E.3.)
src/Ember.Scripting/     Console command router. No MonoGame reference, on purpose.
src/Ember.Rpg/           Optional RPG layer: entities, items, dialogue, quests.
samples/CharacterStudio/ The editor today (moves to src/Ember.Editor in roadmap task E.1).
samples/FirstLight/      Contract sample: the smallest game on the engine (under 200 lines).
samples/Campaign/        A game built on the engine; a consumer, never part of src/.
samples/RpgSlice/        RPG and world-streaming integration fixture.
templates/MinimalGame/   Starter used by tools/new-engine-project.ps1.
tests/Ember.Engine.Tests CPU test suite (xUnit), run in Windows CI.
tests/Ember.Rpg.Check    Console check for the RPG layer.
tools/                   Project creation, publishing and optional import scripts.
Docs/                    Roadmap, design, reference, procedures and evidence.
```

Dependency rules: nothing under `src/` references a sample; `Ember.Engine` never references
`Ember.Rpg`; `Ember.Scripting` has no MonoGame reference.

## Licences

The engine sources are owned by this project.

The two fonts under `samples/FirstLight/Content/Fonts` are third-party: Noto Sans and Cinzel
are both under the SIL Open Font Licence, and `OFL.txt` sits beside each of them. Ship the
licence with anything you ship the font in. Sample GLB fixtures list their sources and
licences in `samples/CharacterStudio/Assets/README.md`.

MonoGame is under the Microsoft Public Licence; FontStashSharp is MIT.

BEPU v2 is pinned as `BepuPhysics` 2.5.0-beta.29 (Apache-2.0), with `BepuUtilities` pulled in
transitively under the same license. It is a prerelease dependency; include the Apache license and
required notices when distributing a build that contains it.
