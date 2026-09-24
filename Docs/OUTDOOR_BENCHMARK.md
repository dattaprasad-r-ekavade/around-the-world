# Outdoor benchmark baseline

This is the fixed baseline for roadmap tasks 119 and 127. The budgets below are targets for later
measurement; they are not claims about current performance. Task 127 will record actual frame-time,
loading, memory, and resource-count results against this same scenario.

## Reference PC and run settings

| Setting | Baseline |
| --- | --- |
| CPU | Intel Core i5-1035G1, 4 cores / 8 logical processors |
| GPU | Intel UHD Graphics, driver 27.20.100.9664 |
| Visible system memory | 20,761,848 KiB (about 19.8 GiB) |
| OS/backend | Windows, MonoGame WindowsDX, .NET 9, Release x64 |
| Resolution | 1280 × 720, windowed (`RpgSlice --windowed`) |
| World seed | The fixed RpgSlice terrain source; no randomized inputs |

If the reference PC changes, append a new dated hardware row rather than replacing a prior result.

## Fixed world and route

Use the nine exterior cells in `samples/RpgSlice/Content/World/world.json`, coordinates −1…1 on X
and Z. Each exterior cell is 32 m wide, so the authored blockout spans 96 m × 96 m. The current
fixtures contain 39 scene objects total, 38 enabled objects, and 2 doors; they contain no active NPCs.
Interiors are excluded from this outdoor route.

Start at `(16, 1.1, 23)` in exterior cell `(0, 0)`. Follow this closed loop at 5 m/s, with no jump or
sprint input:

1. `(16, 23)` to `(48, 23)` — cross into `(1, 0)`.
2. `(48, 23)` to `(48, 48)` — cross into `(1, 1)`.
3. `(48, 48)` to `(16, 48)` — cross into `(0, 1)`.
4. `(16, 48)` to `(16, 23)` — return to `(0, 0)`.

One lap is 114 m. Task 127 should run ten consecutive laps after warmup, with cell streaming and
terrain enabled, and record each boundary crossing. Keep camera distance, field of view, window size,
and content unchanged between runs.

## Provisional targets

| Measure | Target (not measured) |
| --- | --- |
| Average frame time over the ten measured laps | ≤ 16.7 ms |
| p95 frame time | ≤ 25 ms |
| Longest cell activation on the main thread | ≤ 50 ms |
| Process working set | ≤ 1.0 GiB |
| Terrain chunks retained around the route | ≤ 25 |
| Repeated-run trend | No continuing growth in resident chunks or graphics resources after warmup |

These are initial acceptance targets for this modest 3×3 fixture. Task 127 may recommend different
limits only with recorded measurements and an explicit reason. Passing this blockout does not establish
capacity for the final world extent or dense settlement content.

## Results log

No performance measurements have been recorded yet. Add dated rows after task 127 with build
revision, actual resolution, average/p95/max frame time, longest activation, working set, retained
terrain chunks, and graphics-resource counts.
