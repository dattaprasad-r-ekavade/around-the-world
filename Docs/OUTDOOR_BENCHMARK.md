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
The 24 September 2026 rerun used an additional validation host: 13th Gen Intel Core i7-13650HX
(14 cores / 20 logical processors) with Intel UHD Graphics. Keep its result separate from the original
i5-1035G1 reference-PC measurements.

## Fixed world and route

Use the nine exterior cells in `samples/RpgSlice/Content/World/world.json`, coordinates −1…1 on X
and Z. Each exterior cell is 32 m wide, so the authored blockout spans 96 m × 96 m. The current
fixtures contain 39 scene objects total, 38 enabled objects, and 2 doors; they contain no active NPCs.
Interiors are excluded from this outdoor route.

Start at `(16, 1.1, 23)` in exterior cell `(0, 0)`. Follow this closed loop at 5 m/s, with no jump or
sprint input. The return leg detours around the blockout boulder in cell `(0, 1)` at `(20, 0.7, 48)`;
the initial straight return crossed its collider and could not complete in play mode:

1. `(16, 23)` to `(48, 23)` — cross into `(1, 0)`.
2. `(48, 23)` to `(48, 48)` — cross into `(1, 1)`.
3. `(48, 48)` to `(22.5, 48)` — cross into `(0, 1)` and stop clear of the boulder.
4. `(22.5, 48)` to `(22.5, 52)` — pass south of it.
5. `(22.5, 52)` to `(16, 52)` — clear the blockout prop row.
6. `(16, 52)` to `(16, 23)` — return to `(0, 0)`.

One lap is 122 m. Task 127 runs ten consecutive laps after warmup, with cell streaming and terrain
enabled, and records each boundary crossing. Keep camera distance, field of view, window size, and
content unchanged between runs.

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

Reproduce the recorded run from the Release output with `--benchmark --windowed --time-paused
--time-hours 12`. The benchmark disables vertical sync and exits after the warmup and ten measured
laps.

| Date | Build | Resolution / adapter | Route | Avg / p95 / max frame | Cell activation | Peak working set | Active cells / terrain chunks / tracked renderer resources | Result |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 2026-09-24 | Release x64 worktree based on `2b20b6d` | 1280×720, Intel UHD Graphics, HiDef | 10 × 122 m; 40 cell crossings; 166,693 frames | 1.50 / 2.43 / 347.69 ms; 18 frames >50 ms, 14 >100 ms | 9 attempts; longest 33.34 ms | 196.4 MiB | 9 / 16 / 26; identical peaks in all ten laps | Prior run; outliers not reproduced in final repeat |
| 2026-09-24 | Release x64 final worktree based on `2b20b6d` | 1280×720, Intel UHD Graphics, HiDef | 10 × 122 m; 40 cell crossings; 155,465 frames | 1.60 / 2.61 / 28.88 ms; 0 frames >50 ms | 9 attempts; longest 33.34 ms | 196.3 MiB | 9 / 16 / 26; identical peaks in all ten laps | Provisional budgets pass |
| 2026-09-24 | Release x64 worktree with engine `WorldCellStreamer` integration | 1280×720, Intel UHD Graphics, HiDef; i7-13650HX | 10 × 122 m; 40 cell crossings; 316,032 frames | 0.79 / 1.86 / 14.00 ms; 0 frames >50 ms, 0 >100 ms | 38 steps; longest 19.31 ms | 163.0 MiB | 9 / 16 / 26; identical counts in all ten laps | Provisional budgets pass |

Both runs used the Intel Core i5-1035G1 reference PC, WindowsDX, Release x64, paused noon lighting,
and no vertical sync. In the final repeat, lap times were 24.831–24.836 seconds, the active-cell,
terrain-chunk, and tracked-renderer-resource peaks were 9, 16, and 26 on every lap, and per-lap
working set ranged from 194.2 to 196.3 MiB. Foliage batching reduced the peak submission count from
nine individual draws to one instanced draw with the same nine transforms and tints. The tracked
resource count covers terrain, water, and instancing renderers; it excludes driver-internal
allocations.

The final run passed average, p95, activation, working-set, chunk-count, repeated-lap resource, and
frame-tail targets. One earlier run recorded 14 intervals above 100 ms, while the repeat recorded
none; the source change between those runs was the player/camera interpolation fix. Cell activation
peaked at 33.34 ms in both runs. Task 144 adds phase attribution if long intervals recur before
world density increases.

The 24 September engine-streamer rerun used the prescribed 1280×720, paused-noon settings on the
i7-13650HX validation host. It completed all ten laps with instancing enabled: nine foliage instances
were submitted in one batched draw. Average/p95/maximum frame times were 0.79/1.86/14.00 ms, longest
activation was 19.31 ms, peak working set was 163.0 MiB, and the active-cell/chunk/resource counts
stayed at 9/16/26 on every lap. All provisional targets passed; the different CPU means these figures
are additional evidence, not a direct comparison with the original i5 reference run.
