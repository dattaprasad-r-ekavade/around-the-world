> Reference decision (27 September 2026), still in force: regional RPG expansion is not approved. Current work is in ENGINE_ROADMAP.md.

# Expansion status: limited baseline, further scaling unproven

Updated 27 September 2026. This replaces the previous approval, preserved in
[the historical decision](archive/EXPANSION_DECISION_2026-09-27.md).
The active product direction is [ENGINE_ROADMAP.md](ENGINE_ROADMAP.md).

**Decision: do not treat regional expansion or density caps as validated.** Continue
using the small RPG slice as a regression fixture while proving the shared editor,
game, film, and generation workflows.

## Evidence that remains useful

- OUTDOOR_BENCHMARK.md records specific 3×3 and settlement runs on named hardware.
  Their measurements apply to those scenes/settings, not all content at that map size.
- TRANSITION_SOAK.md records 50 transitions over 29.7 seconds, average/max transition
  latency 1.0/8.1 ms and peak working set 164.0 MiB. Exterior counts finish at 9 cells,
  12 terrain chunks and 22 tracked graphics resources; interior counts are 1/0/10.
- ENGINE_PROGRESS.md records outside-repository packaging validation. This is useful
  relocation evidence; it does not by itself establish a clean, SDK-free machine test.

## Corrections to the previous decision

The previous table disagreed with the soak artifact and presented transition latency
as average/p95 frame time. No soak p95 frame-time measurement is established by that
report. Its zero-error row is hardcoded, and its verdict tests only absolute end-minus-
warmup working-set change below 30 MiB. Resource stability and leak freedom are not
validated by that predicate.

A 16×16 region, zero incremental memory growth, and the proposed per-cell object,
collider, actor and draw caps have no demonstrated acceptance run here. Asset sharing
and actor update budgets are mechanisms, not enforcement or proof of those capacities.
The old universal foundation PASS is withdrawn as a current readiness claim; the old
roadmap itself retained pending editor and integration gates.

Unattributed frame time is not sufficient evidence to identify DWM as its cause.
Keep phase measurements, but require a suitable trace before assigning an external
root cause. GPU uploads belong on the graphics-owning thread, not a worker thread.

## Conditions for a future scaling decision

Complete M0 reliability gates first. Then increase extent and density independently,
using a recorded asset mix, route, cold/warm runs and hardware. Include unload/reload,
save/restart, failures, active and retained resources, all relevant memory stores,
frame p95/p99 and maximum stalls. Test any proposed cap at and above its threshold
and distinguish warning/enforcement from performance acceptance. Only publish the
capacity actually exercised; leave wider bounds as experimental targets.
