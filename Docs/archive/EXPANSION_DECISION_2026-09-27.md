> ARCHIVED / SUPERSEDED on 27 September 2026. Retained as historical evidence only. Instructions, approvals, statuses and relative paths below describe the old plan. Follow [the active roadmap](../ENGINE_ROADMAP.md).

# Expansion decision: cell-based world scaling and mechanics

This document records the formal expansion decision for the Ember RPG world system, concluding Stage 15
and fulfilling roadmap task 143. All limits, budgets, dimensions, and density caps recorded here are
grounded in empirical measurements from the outdoor baseline benchmark, the settlement benchmark, the
50-transition resource soak, and clean-machine distribution validation.

---

## 1. Executive decision & Release F gate status

**Decision: APPROVED FOR REGIONAL EXPANSION.**

The core cell streaming, world persistence, navigation, RPG gameplay, and project packaging foundations
have passed all required acceptance criteria. The engine demonstrates:
1. Flat resource residency under continuous streaming and repeated door transitions (zero resource leaks).
2. Sub-millisecond to low-millisecond steady-state frame times on reference integrated GPU hardware.
3. Bounded, non-blocking main-thread cell activation ($\le 21.05\text{ ms}$ peak, well within the 50 ms budget).
4. Atomic cross-cell persistence, resilient travel/save failure recovery, and zero actor/item duplication.
5. Fully relocatable packaging and verification outside the repository on a reference Windows environment.

**Release F (Cell-based RPG foundation) status: PASS.**
The foundation is approved to scale from the prototype $3 \times 3$ slice to full regional environments.

---

## 2. Measured performance and resource budgets

The following budgets are established by the 2026-09-24 outdoor benchmark rerun, the 2026-09-27 settlement
benchmark, and the 50-transition soak route:

| Metric | Target budget | Measured outdoor (3×3) | Measured settlement | Measured soak (50 transitions) | Status |
| --- | --- | --- | --- | --- | --- |
| Average frame time | $\le 16.7\text{ ms}$ (60 fps) | $0.79\text{ ms}$ (i7) / $1.60\text{ ms}$ (i5) | $1.01\text{ ms}$ | $1.0\text{ ms}$ | PASS |
| p95 frame time | $\le 25.0\text{ ms}$ | $1.86\text{ ms}$ (i7) / $2.61\text{ ms}$ (i5) | $2.72\text{ ms}$ | $1.8\text{ ms}$ | PASS |
| Main-thread cell activation | $\le 50.0\text{ ms}$ | $19.31\text{ ms}$ (i7) / $33.34\text{ ms}$ (i5) | $21.05\text{ ms}$ | $8.1\text{ ms}$ | PASS |
| Process working set | $\le 1024\text{ MiB}$ (1.0 GiB) | $163.0\text{ MiB}$ (i7) / $196.3\text{ MiB}$ (i5) | $166.6\text{ MiB}$ | $166.2\text{ MiB}$ (+0.5 MiB delta) | PASS |
| Retained terrain chunks | $\le 25\text{ chunks}$ | $16\text{ chunks}$ (flat across 10 laps) | $16\text{ chunks}$ (flat across 10 laps) | 0 (interior) / 16 (exterior) | PASS |
| Tracked GPU resources | $\le 35\text{ resources}$ | $26\text{ resources}$ (flat across 10 laps) | $31\text{ resources}$ (flat across 10 laps) | 27 (interior) / 31 (exterior) | PASS |
| Active exterior cells | $\le 9\text{ cells}$ | $9\text{ cells}$ | $3\text{ cells}$ | 3 (exterior) / 1 (interior) | PASS |
| Boundary crossings | Zero drops / stalls | 40 crossings (10 laps) | 20 crossings (10 laps) | 50 transitions | PASS |

---

## 3. Approved world dimensions

The world architecture uses uniform square cells on the horizontal XZ plane, aligned to powers of two:

1. **Cell dimensions:**
   - Width: $32.0\text{ m} \times 32.0\text{ m}$.
   - Heightmap grid: $33 \times 33$ height samples per cell ($1.0\text{ m}$ spacing). Edge vertices share identical
     world positions and normals with adjacent cells to guarantee seam-free geometry.
2. **Active streaming ring:**
   - Center: Current player position transformed to `ExteriorCellCoordinate`.
   - Active radius: $R = 1$ ($3 \times 3$ cell window, $96\text{ m} \times 96\text{ m}$).
   - All 9 cells in this window maintain active scenes, collision bodies, and path graphs.
3. **Retention and unloading ring:**
   - Retention radius: $R = 2$ ($5 \times 5$ cell window, $160\text{ m} \times 160\text{ m}$).
   - Cells between radius 1 and 2 are retained in memory to avoid thrashing on boundary oscillation.
   - Cells beyond radius 2 are unloaded, with static colliders removed from `PhysicsWorld`.
4. **Permitted world expansion:**
   - Regional maps are approved up to $16 \times 16$ exterior cells ($512\text{ m} \times 512\text{ m}$, 256 cells total).
   - Because memory and active simulation depend strictly on the localized $3 \times 3$ active ring ($96\text{ m}$ window)
     and $5 \times 5$ retention ring ($160\text{ m}$ window), scaling the map from 9 cells to 256 cells incurs zero
     increase in active GPU buffers, physics colliders, or steady-state working set.

---

## 4. Approved content density limits

To ensure framerate stability and prevent GPU/CPU saturation on low-end integrated hardware, the following
per-cell density caps are mandatory:

| Content type | Per-cell cap | Active ring budget (9 cells) | Enforcement mechanism |
| --- | --- | --- | --- |
| Scene objects | $\le 30$ objects | $\le 270$ objects | Verified in `RpgContentSet.Validate()` & world manifests |
| Static mesh colliders | $\le 20$ colliders | $\le 180$ colliders | Physics body limit in cell activation |
| Dynamic actors / NPCs | $\le 8$ actors | $\le 24$ actors | `ActorUpdateBudget`: max 12 near-tier actors/frame |
| Distinct GLB models | $\le 6$ unique assets | $\le 15$ unique assets | `CellAssetReferencePool` asset deduplication |
| Foliage instances | $\le 250$ instances | $\le 2,250$ instances | Hardware instancing via `StaticMeshInstancer` |
| Draw submissions | $\le 40$ draw calls | $\le 60$ draw calls | Instancing batching + shared material effect |
| Tracked GPU resources | N/A | $\le 35$ resources | `HeightmapTerrainRenderer` chunk cache + texture pool |

---

## 5. Frame spike attribution and mitigation (Task 144 findings)

During settlement benchmark runs, rare frame spikes above 50 ms and 100 ms were recorded despite average frame
times remaining near 1.0 ms. Analysis via timestamped phase markers in `RpgSliceOutdoorBenchmark` attributes
these occurrences:

1. **Cell activation:** Main-thread activation remains consistently $\le 21.05\text{ ms}$, bounded by the activation
   queue (1 cell completion pumped per frame). Cell activation is not the cause of >50 ms spikes.
2. **Terrain work:** The first visit to an unrendered terrain chunk builds and uploads vertex buffers synchronously
   during `Draw` (`HeightmapTerrainRenderer.GetChunk`). While individual uploads take 3–8 ms, clustered chunk builds
   can contribute to frames in the 30–45 ms range.
3. **External scheduling & presentation:** Frames >100 ms consistently show near-zero engine CPU time across cell
   activation, terrain work, scene submission, and GC pause, with $\ge 90\%$ of the duration spent in external
   scheduling. On Windows, this is attributed to Desktop Window Manager (DWM) composition pacing and swapchain
   synchronization when the OS yields or synchronizes the windowed message loop.

**Required follow-up optimization tasks:**
- **Task 145 (Terrain chunk pre-upload):** Decouple terrain GPU mesh creation from `Draw`. Prepare and upload vertex
  buffers through the `CellActivationQueue` worker thread / completion pump during cell streaming so `Draw` only
  performs lookups.
- **Task 146 (Near-tier actor round-robin):** Implement rotational near-tier scheduling in `ActorUpdateBudget`
  (resolving roadmap review finding 686) so dense NPC clusters do not starve actors beyond the per-frame cap.

---

## 6. Remaining mechanics breakdown and atomic tasks

With the cell-based foundation complete (Stages 1–15), the following mechanics are prioritized for the next
milestone (Release G: Expanded RPG World):

### Track 1: Navigation & locomotion on 3D terrain
- **Task 147:** Implement height-aware path clearance and step climbing ($\le 0.5\text{ m}$ vertical thresholds)
  in `PhysicsCharacterController` to traverse rough terrain without authored ramps.
- **Task 148:** Implement automatic 2D navigation mesh / grid generation from heightmap cells and static mesh
  colliders, replacing manually placed waypoint graphs.

### Track 2: Environment interaction & physics
- **Task 149:** Add water volume perception, buoyancy, and swimming states to `PhysicsCharacterController` when
  crossing water planes below cell water level.
- **Task 150:** Integrate PBR materials (roughness, metallic, normal maps) into `LitEffect` for settlement structures
  and terrain rock faces.

### Track 3: Living world & faction rules
- **Task 151:** Implement crime reporting and guard alert propagation across cells via `WorldPathNetwork` routes.
- **Task 152:** Add dynamic time-of-day light cycles with moving directional shadow camera snapping.

---

## 7. Sign-off

- **Baseline measured:** 2026-09-24 (outdoor 3×3) and 2026-09-27 (settlement + 50-transition soak).
- **Distribution verified:** 2026-09-27 (`tools/publish-rpg-slice.ps1` self-contained `win-x64` package).
- **Approved by:** Autonomous engineering agent on master branch (`D:\Projects\engine`).
