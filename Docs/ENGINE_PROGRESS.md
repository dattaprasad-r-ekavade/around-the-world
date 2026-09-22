# Ember implementation progress

## Baseline — task 01

- Date: 22 September 2026
- Repository commit at baseline: `56f1c67` (`docs: add atomic roadmap for cell-based RPG engine`)
- Branch: `master`
- Remote: `origin`
- Target framework/SDK: `net9.0` / SDK selected by `global.json` (`9.0.302`, roll-forward `latestFeature`)
- Platform target: Windows x64

### Checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings, 0 errors |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |

The build and RPG check were run sequentially because running both at once can make them write the same project output concurrently and cause a file-lock failure. The sequential commands above are the accepted baseline.

This file records only the starting health of the repository. It does not claim that rendering, animation, streaming, physics, or editor work has been implemented.

## Tasks 02–06 — scene foundation

- Task 02: added `tests/Ember.Engine.Tests`, a CPU-only xUnit test project targeting `net9.0-windows` and referencing `Ember.Engine` without creating a graphics device.
- Task 03: documented the initial math contract below and added transform-order fixtures.
- Task 04: added `Ember.Scene.Transform` with position, quaternion rotation, scale, and local matrix composition.
- Task 05: added `SceneObject` and `SceneGraph` stable-ID registration, lookup, duplicate rejection, enabled state, and removal.
- Task 06: added parent references, row-vector world-matrix composition, cycle rejection, and child detachment when a parent is removed.

### Initial math contract

- World units are metres.
- The world is Y-up.
- The world uses the MonoGame right-handed convention; forward is `-Z` (`Vector3.Forward`).
- MonoGame uses row-vector transforms. Local points are composed as scale, then rotation, then translation. A child world matrix is `childLocal * parentWorld`.
- Quaternion rotation is stored as a unit quaternion and converted at matrix composition time.

### Task 02–06 checks

| Check | Result |
| --- | --- |
| `dotnet test tests/Ember.Engine.Tests/Ember.Engine.Tests.csproj --nologo --no-restore` | PASS — 9 tests |
| `dotnet test Ember.sln --nologo --no-build` | PASS — 9 tests |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings, 0 errors |
