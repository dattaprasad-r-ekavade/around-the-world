> Review qualification (27 September 2026): retained historical measurements, not an accepted reliability gate. The generator hardcodes zero errors and bases PASS only on working-set delta; resource trends and full cleanup are not validated. See PROJECT_REVIEW.md and roadmap M0.1. The latency below is transition latency, not frame time.

# RPG Slice — Transition Soak Report

Run date: 2026-09-26 20:50:17 UTC
Host adapter: Intel(R) UHD Graphics (1280x720)
Route: Exterior (0, 0) <-> House A Interior
Target transitions: 50
Completed transitions: 50
Total duration: 29.7s (0.50 min)

## Summary Metrics

| Metric | Warmup (t=4) | Final (t=50) | Peak | Result |
| --- | --- | --- | --- | --- |
| Working set | 162.0 MiB | 162.4 MiB | 164.0 MiB | Δ post-warmup: +0.5 MiB (stable) |
| Exterior active cells | 9 | 9 | 9 | 1 distinct value(s) (stable) |
| Exterior terrain chunks | 9 | 12 | 12 | 2 distinct value(s) (stable) |
| Exterior graphics resources | 19 | 22 | 22 | 2 distinct value(s) (stable) |
| Interior active cells | 1 | 1 | 1 | 1 distinct value(s) (stable) |
| Interior terrain chunks | 0 | 0 | 0 | 1 distinct value(s) (stable) |
| Interior graphics resources | 10 | 10 | 10 | 1 distinct value(s) (stable) |
| Transition latency | avg 1.0 ms | max 8.1 ms | - | PASS |
| Accumulated errors | 0 | 0 | 0 | PASS |

## Sampled Transitions

| # | From -> To | Latency (ms) | Active cells | Terrain chunks | Gfx resources | Working set (MiB) |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | Exterior (0, 0) -> House A Interior | 8.1 | 1 | 0 | 10 | 159.5 |
| 2 | House A Interior -> Exterior (0, 0) | 1.8 | 9 | 9 | 19 | 159.2 |
| 3 | Exterior (0, 0) -> House A Interior | 0.7 | 1 | 0 | 10 | 162.0 |
| 4 | House A Interior -> Exterior (0, 0) | 1.0 | 9 | 12 | 22 | 162.0 |
| 5 | Exterior (0, 0) -> House A Interior | 0.8 | 1 | 0 | 10 | 163.5 |
| ... | ... | ... | ... | ... | ... | ... |
| 26 | House A Interior -> Exterior (0, 0) | 1.2 | 9 | 12 | 22 | 162.0 |
| 27 | Exterior (0, 0) -> House A Interior | 0.7 | 1 | 0 | 10 | 162.0 |
| ... | ... | ... | ... | ... | ... | ... |
| 46 | House A Interior -> Exterior (0, 0) | 1.1 | 9 | 12 | 22 | 162.4 |
| 47 | Exterior (0, 0) -> House A Interior | 0.6 | 1 | 0 | 10 | 162.4 |
| 48 | House A Interior -> Exterior (0, 0) | 0.9 | 9 | 12 | 22 | 162.4 |
| 49 | Exterior (0, 0) -> House A Interior | 0.5 | 1 | 0 | 10 | 162.4 |
| 50 | House A Interior -> Exterior (0, 0) | 1.1 | 9 | 12 | 22 | 162.4 |

## Verification Verdict

**PASS** — Resource counts stabilized after warmup; no unbounded growth or accumulating errors occurred across all transitions.
