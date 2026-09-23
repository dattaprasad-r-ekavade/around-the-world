# Ember: small-step implementation plan

Status: in progress. Implement the first unchecked task. When the user requests a batch,
implement consecutive tasks in order; mark each row done only after that task's own check passes.

**Product target:** a single-player, cell-based 3D RPG engine supporting Morrowind-style mechanics and visual fidelity, with a Vice City-like map footprint and separately loaded interiors. The map comparison describes extent, not vehicles, traffic, or crowd simulation. Actual dimensions and active-object budgets must be chosen through benchmarks; they are not performance promises.

Stages 1–8 deliver the reusable foundation and scene-rendering workflow. Stages 9–15 are required for the RPG target, not optional stretch goals. Complete the small RPG proof before filling a full-sized world with content. These stages establish expandable systems; they do not promise every mechanic of either reference game.

## Rules for the implementing AI

1. Read this file and `Docs/BUILDING_BLOCKS.md`. Inspect the code named by the current task.
2. Pick the first unchecked task. Earlier tasks are prerequisites; do not skip ahead.
3. Implement only that row. Do not refactor unrelated code, upgrade packages, or add future features.
4. Prefer one behavior change and at most three source files. If a task needs more, first split that row into numbered subtasks with individual checks. Necessary project/test registration files do not count toward the limit.
5. Run its acceptance check, then `dotnet build Ember.sln --nologo`. Run relevant existing tests; do not launch unrelated games.
6. Mark the row `[x]` only after its check passes. Compilation is not proof of visual correctness.
7. Update the handoff at the bottom: files changed, commands/checks and results, limitations, next task ID.
8. On failure, leave the task unchecked and record the exact blocker. Do not hide the failure or start a later task.
9. Do not commit, push, or publish unless requested. Keep FirstLight and Campaign compiling.

## Fixed choices

- Keep C#, MonoGame WindowsDX, and the current SDK/package versions initially.
- Target Windows x64. Use GLB files for imported assets and versioned JSON for scenes.
- Use simple scene objects and components; do not introduce a complex ECS.
- Preserve imported units, pivots, and hierarchy. Do not use `ModelCache` normalization for the new importer.
- Keep runtime code under `src/Ember.Engine`. Add folders only when the task needs them.
- Keep game rules in games or `Ember.Rpg`; never reference Campaign from engine code.
- Use SharpGLTF as the first importer candidate, BEPU v2 as the physics candidate, and ImGui.NET as the tools UI candidate. Each needs its listed compatibility check before adoption. Pin the version that passes.
- Unsupported asset features must produce a clear error. Supporting GLB does not mean supporting every glTF feature.
- Start with ordinary lighting. PBR, root motion, IK, retargeting, facial animation, multiplayer, terrain editing, and additional platforms are later work.

Library references: [SharpGLTF](https://github.com/vpenades/SharpGLTF), [BEPU](https://github.com/bepu/bepuphysics2), [ImGui.NET](https://github.com/ImGuiNET/ImGui.NET), [glTF specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html).

## Stage 1 — A scene containing one cube

Start here. Do not build an editor or importer yet.

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [x] | 01 | Create `Docs/ENGINE_PROGRESS.md`; record current build and RPG check results. | `dotnet build Ember.sln --nologo` and `dotnet run --project tests/Ember.Rpg.Check` succeed; output is recorded. |
| [x] | 02 | Add a CPU-only engine test project to the solution using one test framework. | One numeric test passes through `dotnet test`; no graphics device/window is needed. |
| [x] | 03 | Document metres, Y-up, right-handed coordinates, forward -Z, and MonoGame matrix multiplication order in the progress file. Add one transform-order fixture. | A known rotated/translated point matches a manually calculated expected result. |
| [x] | 04 | Add `Scene/Transform.cs`: position, quaternion rotation, scale, and local matrix. | Identity, translation, rotation, and scale fixtures pass. |
| [x] | 05 | Add a scene object with stable ID, name, enabled flag, and transform; add scene add/remove/find operations. | Duplicate IDs are rejected; removing an object removes it from lookup. |
| [x] | 06 | Add parent references and world-transform calculation; reject cycles. | A translated parent moves its child correctly; self-parenting and ancestor cycles fail clearly. |
| [x] | 07 | Create `samples/CharacterStudio` as a new engine consumer. Draw one cube from a scene object's transform using existing rendering facilities. | Sample builds and a captured image visibly contains the expected cube. |
| [x] | 08 | Add an orbit camera independent of `FirstPersonView`. | Drag rotates around the cube; zoom is bounded; framing returns the cube to view. |

**Stage gate:** a scene object controls the visible cube. Record its screenshot path in the handoff.

## Stage 2 — Save, reopen, and clean up

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [x] | 09 | Add version-1 scene JSON for object IDs, names, transforms, and parents. | Save/load preserves the hierarchy and world transforms. |
| [x] | 10 | Validate scene input: duplicate IDs, missing parents, cycles, nonfinite transforms, unsupported versions. | Each malformed fixture gives an actionable error without replacing the current scene. |
| [x] | 11 | Add safe scene saving using a temporary file and replacement. | A simulated write failure leaves the previous valid scene readable. |
| [x] | 12 | Add open/save commands to CharacterStudio, initially through fixed command-line paths or existing console infrastructure. | Restarting the sample opens the saved cube placement. |
| [x] | 13 | Audit `EngineHost` resource ownership and document each created resource and its disposer. | Every host-created graphics/audio resource has one named owner; borrowed resources are distinguished. |
| [x] | 14 | Fix host cleanup according to task 13; split by resource group if necessary. | Every host-owned resource has one idempotent disposal path; each sample builds and exits cleanly in capture mode. |
| [x] | 15 | Add a scene-owned resource collection for the new sample; do not rewrite all legacy static caches. | Resources dispose once in reverse order, cleanup continues after a disposal failure, and partial sample setup disposes registered resources. |

**Stage gate:** save/reopen works, and 20 sample scene reloads return owned resource counts to baseline. Do not infer GPU cleanup solely from managed memory.

## Stage 3 — One imported static model

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [x] | 16 | Add one redistributable static GLB fixture and its license/source; document expected dimensions and orientation. | Asset provenance is recorded and its authored appearance is available for comparison. |
| [x] | 17 | Evaluate SharpGLTF by reading fixture nodes and primitives; pin the compatible version. | A CPU test reads expected counts/transforms; chosen version and license are recorded. |
| [x] | 18 | Define Ember-owned mesh data for positions, normals, UV0, and triangle indices. Convert one primitive. | Numeric fixture checks match expected vertices and indices; unsupported topology is rejected. |
| [x] | 19 | Upload that mesh to vertex/index buffers and draw it with an explicit world matrix. | The fixture renders at its authored size/origin; buffers are disposed on unload. |
| [x] | 20 | Import the GLB node hierarchy into scene objects. | Rotated/scaled child meshes match reference transforms and visual placement. |
| [x] | 21 | Import base-color factors and PNG/JPEG base-color textures for opaque materials only. | A textured reference object has correct UV orientation and tint; unsupported material modes are reported. |
| [x] | 22 | Add local mesh bounds, transformed bounds, and camera framing. | Rotated/scaled objects frame correctly; bounds contain their transformed vertices. |
| [x] | 23 | Add stable asset IDs and scene references to source GLB paths via metadata. | Two scene objects reference one asset; save/reopen reloads both without embedding mesh data in scene JSON. |
| [ ] | 24 | Add explicit reimport that keeps the old valid asset until the replacement succeeds. | Valid changes appear; corrupt replacement content shows an error and leaves the old model usable. |
| [ ] | 25 | Write the supported GLB subset and reject unsupported required extensions/features. | Negative fixtures fail clearly instead of drawing misleading partial content. |

**Stage gate:** reopen a saved scene containing two instances of the imported model. Compare placement and textures with the authored reference.

## Stage 4 — One animated character

Keep the initial supported subset small: triangle meshes, one skin, documented joint/influence limits, and STEP/LINEAR transform tracks. Diagnose other cases.

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [ ] | 26 | Add one licensed rigged GLB with a known bind pose and animation; record its hierarchy and expected clip duration. | The source application/reference viewer displays the expected pose and clip. |
| [ ] | 27 | Import joint hierarchy, inverse-bind matrices, and rest local transforms into immutable data. | CPU fixtures verify joint ordering and bind transforms, including mesh-node transforms. |
| [ ] | 28 | Import joint indices/weights with explicit supported limits and validation. | Invalid joints/weights fail; accepted weights are normalized; excess influences are reported. |
| [ ] | 29 | Add per-instance pose arrays and calculate skin matrices from a supplied pose. | Bind-pose fixtures produce expected transformed vertices; instances do not share mutable arrays. |
| [ ] | 30 | Add the skinning draw path; validate SkinnedEffect/profile limits before choosing it. | The character's bind pose visually matches the reference; oversized skeletons fail clearly. |
| [ ] | 31 | Import STEP/LINEAR translation, quaternion rotation, and scale animation tracks. | Clip duration/key values match the fixture; unsupported interpolation is rejected. |
| [ ] | 32 | Evaluate one clip at an absolute time, using rest values for missing channels. | Start/middle/end fixtures pass, including shortest-path rotation interpolation. |
| [ ] | 33 | Add play, pause, loop, seek, and speed controls through sample commands. | Seeking to a time matches sequential playback at that time; loop boundaries are correct. |
| [ ] | 34 | Draw two characters sharing assets with different playback states. | Pausing/seeking one does not change the other. |
| [ ] | 35 | Add a two-clip local-pose crossfade. | Blend endpoints equal their source poses and the midpoint is smooth. |
| [ ] | 36 | Add one named bone attachment with a local offset. | A visible hand prop follows the character throughout its animation. |
| [ ] | 37 | Add conservative animated bounds, initially sampled per clip with a documented margin. | Limbs remain visible throughout supported clips; unsupported procedural poses can disable culling. |
| [ ] | 38 | Save character asset, clip, playback settings, and attachment references. | Reopening the scene restores both characters and their settings. |

**Release A: Character Studio alpha.** Two independently animated characters, one environment, one attachment, save/load, and screenshot capture must work before continuing.

## Stage 5 — Usable scene tools and lighting

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [ ] | 39 | Integrate a pinned ImGui.NET version in CharacterStudio with a minimal panel. | Text entry, DPI scaling, rendering, and native dependency loading work; UI focus suppresses camera controls. |
| [ ] | 40 | Add a hierarchy list that selects one scene object. | Clicking a name selects the correct stable ID; deleted selections clear safely. |
| [ ] | 41 | Add numeric position/rotation/scale fields for the selected object. | Edits update the view and survive save/reopen. |
| [ ] | 42 | Add a command history for transform edits only. | One completed edit undoes/redoes exactly; redo clears after a new edit. |
| [ ] | 43 | Add create/duplicate/delete commands using the same history. | Undo restores IDs, hierarchy, and references without duplication. |
| [ ] | 44 | Add an asset list and place-instance command. | An imported asset can be placed twice without source changes. |
| [ ] | 45 | Add character clip selection and scrub controls using the existing animation API. | Controls change only the selected character. |
| [ ] | 46 | Introduce shared scene ambient/directional lighting for new static and skinned draws. | Changing the light affects both model types consistently; old samples still build. |
| [ ] | 47 | Add an explicit shader build step if not already required by task 46. | Clean checkout builds/packages shaders; compilation errors fail clearly. |
| [ ] | 48 | Add one directional shadow map for opaque static geometry. | A cube casts a moving shadow when the light rotates; resize/unload releases targets. |
| [ ] | 49 | Add skinned meshes to that shadow pass. | A character's shadow follows its pose rather than its bind pose. |
| [ ] | 50 | Add basic draw-count/frame-time diagnostics and static frustum culling. | Offscreen objects reduce submitted draws; frame measurements identify their scope and reference hardware. |

**Release B: basic scene authoring.** Assemble, light, animate, save, reopen, and photograph a scene through the sample UI. This is a small working tool, not yet a full editor.

## Stage 6 — A small playable game

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [ ] | 51 | Add a physics adapter spike with pinned BEPU v2: one static floor and one falling box. | Box settles on floor; conversions and cleanup work; dependency/license decision is recorded. |
| [ ] | 52 | Add fixed simulation steps with bounded catch-up and render interpolation. | The falling-box test behaves consistently at 30/60/120 render fps; excess backlog is reported. |
| [ ] | 53 | Add raycast and collision-layer queries. | Known hit/miss/filter fixtures return correct objects and distances. |
| [ ] | 54 | Add a capsule character with flat-ground movement and wall collision only. | Movement stops at walls and remains grounded; camera is not the physics body. |
| [ ] | 55 | Add jump and ceiling collision. | Jump/land events occur once; jumping into a low ceiling cannot pass through it. |
| [ ] | 56 | Add slope limits. | The capsule climbs an allowed ramp and cannot climb a steeper forbidden ramp. |
| [ ] | 57 | Add a third-person follow camera with obstruction checks. | Camera follows the capsule and does not pass through a wall behind it. |
| [ ] | 58 | Add named move/jump input actions and focus handling. | UI typing does not move the player; one press is not repeated across physics substeps. |
| [ ] | 59 | Drive idle/walk/jump animation from actual character motion. | Running into a wall stops walk animation; jumping selects and exits the jump state. |
| [ ] | 60 | Add a compiled C# behavior lifecycle with one interaction example. | A behavior starts/stops once and responds to one interaction without leaking across scene reload. |
| [ ] | 61 | Add one imported audio clip with scene ownership and volume control. | Interaction plays the clip; muted volume works; unloading stops/releases it. |
| [ ] | 62 | Add editor play-on-clone and stop-to-restore behavior. | Moving/deleting objects during play does not change the authored scene after stopping. |

**Release C: game prototype.** A packaged test level supports movement, collision, jumping, animation, and an audible interaction. Stairs, moving platforms, gamepad rebinding, and root motion remain explicit follow-up tasks.

## Stage 7 — Render a short animated scene

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [ ] | 63 | Add a sequence asset with duration and one character clip track. | Evaluating any absolute time produces the same pose regardless of seek history. |
| [ ] | 64 | Add camera transform keys and camera-cut tracks. | Known times select the expected camera and pose; scene objects retain stable references. |
| [ ] | 65 | Add sequence play/pause/scrub UI. | Forward/backward scrubbing updates the scene without firing gameplay events. |
| [ ] | 66 | Add export to a dedicated render target at a requested resolution. | Export dimensions do not depend on window size; resources are released after capture. |
| [ ] | 67 | Export numbered PNG frames at `start + frameIndex / fps`; disable live physics during this first capture mode. | Ten seconds at 30 fps exports exactly 300 frames; selected frames match preview sample times. |
| [ ] | 68 | Add export manifest, progress, cancel, and write-error handling. | Cancellation/errors leave clearly marked incomplete output; successful manifest records asset versions/settings. |

**Release D: scene rendering.** A saved sequence can produce the same animated shot again on the same configuration. Cross-GPU pixel identity and arbitrary physics seeking are not promised.

## Stage 8 — Make it reusable

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [ ] | 69 | Add a minimal outside-consumer project template and startup-scene setting. | A project outside this repository builds against Ember without copying engine source. |
| [ ] | 70 | Package scene/asset dependencies with missing-reference validation. | Required content is present; a broken reference fails the package step with its path/ID. |
| [ ] | 71 | Produce a Windows x64 distribution including runtime/native dependencies. | It runs without the SDK/source tree on a clean graphics-capable Windows machine. |
| [ ] | 72 | Write one tutorial using only features proven above and list unsupported features. | Following it creates an animated scene and a playable build without editing engine source. |

**Release E: small reusable engine.** Preserve the sample projects as regression tests. Reassess architecture only after another real project encounters a concrete limitation.

## Stage 9 — Exterior and interior cells

Start after task 72. Put reusable cell/loading code in `Ember.Engine/World`; keep RPG definitions in `Ember.Rpg` and demonstrations in a new `samples/RpgSlice`. Inspect Campaign's `LocationRunner` and `HeightmapTerrain` for reusable ideas, but do not copy their game dependencies into the engine.

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [ ] | 73 | Define stable cell IDs and a world manifest listing exterior coordinates and interior scene references. | Duplicate IDs/coordinates and missing scene references fail validation. |
| [ ] | 74 | Add world-position-to-exterior-cell conversion using configurable cell width. | Boundary and negative-coordinate fixtures return the expected cells. |
| [ ] | 75 | Create RpgSlice with one exterior cell loaded through the manifest. | It renders and supports existing player movement without Campaign dependencies. |
| [ ] | 76 | Add an explicit cell lifecycle: unloaded, preparing, ready, active, unloading, failed. | Invalid transitions are rejected and failed loading releases temporary resources. |
| [ ] | 77 | Prepare one cell's CPU data asynchronously; activate graphics/physics resources on their owning thread. | Loading records thread ownership correctly and never exposes a partially active cell. |
| [ ] | 78 | Add a configurable nearby-cell loading ring around the player. | Crossing a boundary requests the correct neighbors exactly once. |
| [ ] | 79 | Add a bounded per-frame activation/upload queue. | A multi-cell load respects configured work limits and exposes queue/timing diagnostics. |
| [ ] | 80 | Unload cells outside a wider retention ring using asset reference counts. | Shared assets stay alive while another cell needs them; repeated crossings do not accumulate resources. |
| [ ] | 81 | Cancel obsolete load requests and reject stale completion results. | Rapid travel cannot activate old destination cells or leak their prepared resources. |
| [ ] | 82 | Prevent movement into a required cell until its collision is ready; expose a loading state. | Artificially delayed loading cannot make the player fall through missing terrain. |
| [ ] | 83 | Add a door component with destination cell ID, spawn ID, and facing. | Validation catches invalid destinations; a door enters an interior at its authored spawn. |
| [ ] | 84 | Add transactional interior/exterior travel: preserve the source until destination activation succeeds. | Exit returns to the intended exterior; failed destination loading leaves the source playable. |

**Gate:** walk through a 3×3 exterior test grid, enter two independent interiors, and return. Test slow loads, failed loads, and rapid changes of direction. Record frame-time spikes and resource counts.

## Stage 10 — A persistent world

Authored definitions are immutable. Save changes by stable world-instance ID, not by array index or display name. Scene editing files and player save files remain separate.

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [ ] | 85 | Add stable world-instance IDs distinct from asset and cell IDs. | Two instances of one asset have different IDs that survive cell reload. |
| [ ] | 86 | Add a per-cell change store for object transforms and enabled state. | Move/disable an object, unload its cell, and reload; its changes remain. |
| [ ] | 87 | Add deletion tombstones for authored objects. | A removed authored object does not return after reload. |
| [ ] | 88 | Add runtime-created object records with persistent IDs. | A spawned test item survives reload without acquiring a duplicate ID. |
| [ ] | 89 | Transfer an instance between cell ownership records atomically. | Moving an item across a boundary and reloading both cells leaves exactly one instance. |
| [ ] | 90 | Write/read a versioned world save containing player location and cell changes using safe replacement. | Restart restores the same world; a failed write preserves the previous save. |
| [ ] | 91 | Validate save/content versions and add one schema migration fixture. | A supported old fixture migrates; unsupported or missing definitions produce actionable diagnostics. |
| [ ] | 92 | Queue save requests until a stable simulation boundary, including during travel. | A save requested mid-transition restores one complete location, not mixed source/destination state. |
| [ ] | 93 | Add explicit cell reset policy, defaulting to no reset. | Resettable content resets only under its rule; persistent/quest-marked instances remain unchanged. |

**Gate:** move, create, and delete objects across two cells; travel inside; save and restart. Every instance must have the expected location/state exactly once.

## Stage 11 — RPG records and interactions

Extend existing `Ember.Rpg` records, flags, inventory, dialogue, and quests where they fit. Inspect their current checks before adding replacements. Keep these systems independent of rendering.

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [ ] | 94 | Register typed content IDs for actors, items, factions, dialogue, and quests. | Broken cross-references identify the source record and missing target. |
| [ ] | 95 | Add actor attributes, skills, and derived health/magicka/stamina through explicit formulas. | Numeric fixtures verify base values and recalculation without accidental permanent changes. |
| [ ] | 96 | Add timed stat modifiers with explicit stacking/expiry rules. | Applying, stacking, expiring, and saving/loading a modifier give expected values. |
| [ ] | 97 | Persist container contents by world-instance ID using existing inventory primitives. | Looting and cell reload cannot recreate taken items. |
| [ ] | 98 | Add atomic pickup/drop transfers between inventory and world instances. | Failed transfer preserves the original item; successful transfer neither duplicates nor loses it. |
| [ ] | 99 | Connect equipment slots to stats and visual bone attachments. | Equip/unequip updates modifiers and attachment once; save/load preserves both. |
| [ ] | 100 | Add a basic melee attack with range, cooldown, and damage rules. | One attack applies damage once; out-of-range targets take none. |
| [ ] | 101 | Persist actor death and inventory state. | A dead looted actor stays dead and looted after travel/restart. |
| [ ] | 102 | Add one targeted spell using the modifier system, resource cost, and casting cooldown. | Invalid casts consume nothing; valid casts apply their cost/effect exactly once. |
| [ ] | 103 | Add faction membership and reputation records. | Changes persist and two actors/factions do not share mutable state accidentally. |
| [ ] | 104 | Evaluate dialogue choices against stats, flags, and faction conditions using existing dialogue structures. | Choices appear correctly and execute effects once, including after reload. |
| [ ] | 105 | Connect quest objectives to interaction/death/item events through stable IDs. | A quest survives target cell unload and handles dead/missing targets explicitly. |
| [ ] | 106 | Add one merchant transaction using inventory transfers and currency. | Insufficient funds/stock changes nothing; a valid trade updates both parties atomically. |
| [ ] | 107 | Add ownership checks and one witnessed theft event affecting reputation. | Unwitnessed and witnessed test cases follow documented rules without duplicate penalties. |
| [ ] | 108 | Add data-driven skill-use progression. | Repeated qualifying actions advance only the intended skill; progress survives restart. |

**Gate:** complete one branching quest involving dialogue, an item, a merchant, and an enemy. Equipment, effects, faction changes, loot, and progression survive a save/restart.

## Stage 12 — NPC navigation and simulation

Begin with authored path nodes rather than a navmesh generator. Implement the navigation behind an interface so automatic mesh generation can be evaluated later without rewriting actor behavior.

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [ ] | 109 | Add per-cell path nodes/edges with obstacle clearance metadata. | Invalid endpoints are rejected and a small graph can be serialized/reopened. |
| [ ] | 110 | Implement route search on that graph. | Known shortest paths and unreachable targets pass fixtures. |
| [ ] | 111 | Move one NPC along a route through the character controller. | It reaches the destination while respecting collision; blocked motion times out safely. |
| [ ] | 112 | Connect exterior boundary nodes and interior door links. | A route can describe travel across cells and through a door without teleporting during local movement. |
| [ ] | 113 | Add NPC travel execution using cell loading and persistent ownership transfer. | A following NPC crosses an exterior boundary, enters an interior, and remains unique after save/reload. |
| [ ] | 114 | Add sight/range perception with physics line-of-sight queries. | An obstacle blocks detection; removing it allows detection within range. |
| [ ] | 115 | Add a small idle/chase/attack state machine using existing combat actions. | One enemy notices, pursues, attacks, and stops after losing its target or dying. |
| [ ] | 116 | Add near/far/dormant actor update tiers with per-frame work budgets. | Distant actors stop expensive animation/perception updates; reactivation preserves their state. |
| [ ] | 117 | Add a world clock and a two-destination daily NPC schedule. | Time changes select the correct destination without creating duplicate travel requests. |
| [ ] | 118 | Define dormant schedule catch-up without replaying every missed frame. | Returning after a time skip produces the expected NPC location/state with bounded work. |

**Gate:** several NPCs follow schedules while one enemy uses combat AI. A follower travels through a door and survives restart; dormant actors do not require their full scenes to remain active.

## Stage 13 — Outdoor presentation and budgets

This stage targets the requested visual style. It does not require modern PBR. Preserve the distinction between outdoor extent, resident cells, visible geometry, and actively simulated actors.

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [ ] | 119 | Define a fixed benchmark route, reference PC, resolution, content counts, and provisional frame/memory/loading budgets. | Results can be reproduced; target budgets are labeled as targets rather than measured capability. |
| [ ] | 120 | Adapt Campaign terrain chunk generation behind a game-independent height/material source. | RpgSlice draws terrain without referencing Campaign; adjacent chunk edges match. |
| [ ] | 121 | Stream terrain/collision through cell lifecycle with shared boundary samples. | Crossing seams causes no visible gaps or loss of ground contact. |
| [ ] | 122 | Add alpha-cutout material support for foliage and fences, including shadows. | Cutout regions neither write solid depth nor cast solid rectangular shadows. |
| [ ] | 123 | Add simple time-of-day sky, fog, and directional light parameters. | A fixed clock value reproduces the same appearance; changing time updates all cells consistently. |
| [ ] | 124 | Add a basic water surface with explicit transparency/depth rules. | Shoreline geometry remains visible as intended and sorting limitations are documented. |
| [ ] | 125 | Add authored near/far mesh representations with distance thresholds and hysteresis. | Repeated threshold crossings do not flicker; distant scenery uses the cheaper representation. |
| [ ] | 126 | Add shared static-mesh instancing for one repeated opaque prop type. | The benchmark shows reduced draw submissions with equivalent placement/materials. |
| [ ] | 127 | Run the benchmark through populated cells and record average/p95 frame time, loading spikes, and memory/resource counts. | Measured failures become specific optimization tasks before increasing world density. |

**Gate:** choose actual cell dimensions, visibility distances, active actor budgets, and proposed map extent from recorded measurements. Do not extrapolate whole-world performance from an empty terrain test.

## Stage 14 — Tools for building the RPG world

Use the existing scene tool. Add one panel or command at a time; all authored data must use the same runtime validators.

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [ ] | 128 | Add a cell browser for creating/opening exterior and interior cells. | IDs remain stable after renaming; coordinates cannot collide. |
| [ ] | 129 | Add an actor/item placement palette backed by registered RPG definitions. | New placements get unique instance IDs and reopen correctly. |
| [ ] | 130 | Add a door destination/spawn picker. | Selecting a destination creates a valid travel link and exposes broken links visibly. |
| [ ] | 131 | Add path-node/edge editing with reachability visualization. | A route authored in the tool can be followed by an NPC in play mode. |
| [ ] | 132 | Add a dialogue record panel with condition/effect editing. | The branching conversation can be edited and validated without source changes. |
| [ ] | 133 | Add a quest objective/event-reference panel. | Missing target IDs are identified before play/package, and valid objectives run. |
| [ ] | 134 | Add reusable placement templates with explicit instance overrides. | Updating a template preserves documented overrides and stable world-instance IDs. |
| [ ] | 135 | Add project-wide reference validation for cells, doors, paths, actors, items, dialogue, and quests. | A deliberately broken fixture reports every known error with its owning file/record. |
| [ ] | 136 | Add autosave/recovery for authored content, separate from player saves. | Interrupted editing can be recovered without replacing a valid player save or silently accepting invalid content. |

**Gate:** author one small settlement, two interiors, an NPC route, and a quest through the tools without handwritten placement or reference wiring.

## Stage 15 — Prove the intended RPG before enlarging it

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [ ] | 137 | Assemble a compact settlement across several exterior cells with two interiors and representative props/NPCs. | It uses the normal asset pipeline and meets task 119's agreed budgets or records blocking failures. |
| [ ] | 138 | Add one complete quest using dialogue, exploration, combat, and persistent loot. | It can be completed through normal play without debug commands. |
| [ ] | 139 | Add a repeatable persistence scenario: drop item, loot container, kill enemy, move follower across cells, enter interior, save/restart. | Every resulting state is correct and each persistent instance exists exactly once. |
| [ ] | 140 | Run repeated travel/save/load with delayed or failed asset reads. | Failures remain recoverable; no duplicate actors, lost items, or partially restored scenes occur. |
| [ ] | 141 | Run a documented hour-long route with at least 50 interior/exterior transitions. | Resource counts stabilize after warmup; no unbounded growth or accumulating errors occur. |
| [ ] | 142 | Package this RPG slice and validate it on the reference clean Windows setup. | Content, native dependencies, player saves, and diagnostics work outside the repository. |
| [ ] | 143 | Record an expansion decision: measured budgets, approved world dimensions, content density, and remaining mechanics. | Larger-map work has explicit limits and new atomic tasks; failed gates remain blockers. |

**Release F: cell-based RPG foundation.** A functioning small piece of the intended larger game establishes readiness to expand. Producing the full map, quests, characters, and art remains a separate content workload.

## Later: pick one need, then write new atomic tasks

These remain outside the required 143-task baseline. Cell streaming, persistent worlds, NPC routing, RPG authoring, and outdoor budgets are required above. Do not implement a whole optional row as one task.

| Need | Next feature to break down | Start after |
| --- | --- | --- |
| Better asset compatibility | External glTF resources, CUBICSPLINE, multiple skins, alpha blend, robust reimport metadata | Release A |
| Better lighting | Linear/HDR pipeline, metallic/roughness materials, normal mapping, environment lighting | Release B |
| Faster authoring | Gizmos, snapping, file watching | Release B |
| Better movement | Step climbing, moving platforms, continuous collision, controller input/rebinding | Release C |
| Richer characters | Animation events, root motion, layered poses, IK, retargeting, morph targets | Release C |
| Richer scenes | Additional timeline tracks, baked physics playback, synchronized audio/video encoding | Release D |
| More advanced world systems | Automatic navmesh generation, swimming, flying/levitation, richer crime/guard rules, terrain painting | Release F or a concrete slice requirement |
| Wider distribution | SDK support refresh, additional platforms, package distribution/version policy | Before a public release |

For each chosen feature, append tasks with the same four columns. Each task needs one observable result, named code area, and an acceptance check. Keep speculative implementation details out until prerequisites exist.

## Handoff — update after every implementation session

- Last completed tasks: 22–23 (tasks 16–21 remain complete from previous batches).
- Current task: 24.
- Changed files: `src/Ember.Engine/Assets/StaticMeshData.cs`, `src/Ember.Engine/Engine/OrbitCamera.cs`, `src/Ember.Engine/Scene/SceneObject.cs`, and `src/Ember.Engine/Scene/SceneFile.cs`; CharacterStudio game; new `tests/Ember.Engine.Tests/MeshBoundsTests.cs`, updated `SceneFileTests.cs`; `Docs/BUILDING_BLOCKS.md`, `Docs/ENGINE_PROGRESS.md`, and this roadmap.
- Checks run: `dotnet test Ember.sln --no-build --nologo`; `dotnet build Ember.sln --nologo`; `dotnet run --project tests/Ember.Rpg.Check --no-build`; CharacterStudio captures of one and two GLB instances.
- Results: 29 CPU tests passed; solution build passed with 0 warnings and 0 errors; RPG check passed; both GLB instances reopened and rendered together inside the calculated frame.
- Blockers: none recorded.
- Next action: implement explicit asset reimport so a replacement is fully validated before it replaces a working GLB.

Suggested request to an implementing AI:

> Read Docs/ENGINE_ROADMAP.md. Complete the first unchecked task whose prerequisites are finished. If the user requests a consecutive batch, do those tasks in order and pass each task's acceptance check before marking its row. Follow the implementation rules, update the handoff, and do not claim untested behavior works.
