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
