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
| [x] | 24 | Add explicit reimport that keeps the old valid asset until the replacement succeeds. | Valid changes appear; corrupt replacement content shows an error and leaves the old model usable. |
| [x] | 25 | Write the supported GLB subset and reject unsupported required extensions/features. | Negative fixtures fail clearly instead of drawing misleading partial content. |

**Stage gate:** reopen a saved scene containing two instances of the imported model. Compare placement and textures with the authored reference.

## Stage 4 — One animated character

Keep the initial supported subset small: triangle meshes, one skin, documented joint/influence limits, and STEP/LINEAR transform tracks. Diagnose other cases.

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [x] | 26 | Add one licensed rigged GLB with a known bind pose and animation; record its hierarchy and expected clip duration. | The source application/reference viewer displays the expected pose and clip. |
| [x] | 27 | Import joint hierarchy, inverse-bind matrices, and rest local transforms into immutable data. | CPU fixtures verify joint ordering and bind transforms, including mesh-node transforms. |
| [x] | 28 | Import joint indices/weights with explicit supported limits and validation. | Invalid joints/weights fail; accepted weights are normalized; excess influences are reported. |
| [x] | 29 | Add per-instance pose arrays and calculate skin matrices from a supplied pose. | Bind-pose fixtures produce expected transformed vertices; instances do not share mutable arrays. |
| [x] | 30 | Add the skinning draw path; validate SkinnedEffect/profile limits before choosing it. | The character's bind pose visually matches the reference; oversized skeletons fail clearly. |
| [x] | 31 | Import STEP/LINEAR translation, quaternion rotation, and scale animation tracks. | Clip duration/key values match the fixture; unsupported interpolation is rejected. |
| [x] | 32 | Evaluate one clip at an absolute time, using rest values for missing channels. | Start/middle/end fixtures pass, including shortest-path rotation interpolation. |
| [x] | 33 | Add play, pause, loop, seek, and speed controls through sample commands. | Seeking to a time matches sequential playback at that time; loop boundaries are correct. |
| [x] | 34 | Draw two characters sharing assets with different playback states. | Pausing/seeking one does not change the other. |
| [x] | 35 | Add a two-clip local-pose crossfade. | Blend endpoints equal their source poses and the midpoint is smooth. |
| [x] | 36 | Add one named bone attachment with a local offset. | A visible hand prop follows the character throughout its animation. |
| [x] | 37 | Add conservative animated bounds, initially sampled per clip with a documented margin. | Limbs remain visible throughout supported clips; unsupported procedural poses can disable culling. |
| [x] | 38 | Save character asset, clip, playback settings, and attachment references. | Reopening the scene restores both characters and their settings. |
| [x] | 38a | Load one static environment GLB alongside shared skinned-character instances in CharacterStudio. | A saved scene renders two independently animated Fox instances and a distinct environment asset at their authored transforms. |
| [x] | 38b | Build and reopen the Release A showcase scene with its environment, characters, and hand prop. | Reopening restores all transforms, clip states, and attachment placement in a captured 1280×720 scene. |

**Release A: Character Studio alpha — PASS (23 September 2026).** The saved courtyard showcase contains two independently configured Fox instances, a distinct environment GLB, a hand attachment, and survives reopen. Evidence: `captures/release-a-before-reopen.png` and `captures/release-a-after-reopen.png`.

## Stage 5 — Usable scene tools and lighting

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [x] | 39 | Integrate a pinned ImGui.NET version in CharacterStudio with a minimal panel. | Text entry, DPI scaling, rendering, and native dependency loading work; UI focus suppresses camera controls. |
| [x] | 40 | Add a hierarchy list that selects one scene object. | Each row carries its stable ID, duplicate names remain distinct, and deleted selections clear safely. |
| [x] | 41 | Add numeric position/rotation/scale fields for the selected object. | Finite numeric edits update the selected transform; scene save/reopen preserves transforms. |
| [x] | 42 | Add a command history for transform edits only. | One completed edit undoes/redoes exactly; redo clears after a new edit. |
| [x] | 43 | Add create/duplicate/delete commands using the same history. | Undo restores IDs, hierarchy, and references without duplication. |
| [x] | 44 | Add an asset list and place-instance command. | An imported asset can be placed twice without source changes. |
| [x] | 45 | Add character clip selection and scrub controls using the existing animation API. | Controls change only the selected character. |
| [x] | 46 | Introduce shared scene ambient/directional lighting for new static and skinned draws. | Changing the light affects both model types consistently; old samples still build. |
| [x] | 47 | Decide whether task 46 needs a separate shader build step. | Shared lighting uses the existing built-in effects, no custom shader assets are required, and the full solution builds. |
| [x] | 47a | Add a pinned MonoGame effect-content build path for later custom render passes. | A `.fx` asset builds reproducibly as part of the CharacterStudio project build. |
| [x] | 48 | Add one directional shadow map for opaque static geometry. | Static scene meshes render through a depth pass into a resized, disposed shadow target; lighting changes update the light-camera matrix. |
| [x] | 49 | Add skinned meshes to that shadow pass. | The shadow pass uploads each character's current pose before drawing; a mixed animated scene renders successfully. |
| [x] | 50 | Add basic draw-count/frame-time diagnostics and static frustum culling. | A separate offscreen static fixture increments the cull count; frame output reports draw counts, interval scope, adapter, and resolution. |

**Release B: basic scene authoring.** Assemble, light, animate, save, reopen, and photograph a scene through the sample UI. This is a small working tool, not yet a full editor.

## Stage 6 — A small playable game

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [x] | 51 | Add a physics adapter spike with pinned BEPU v2: one static floor and one falling box. | Box settles on floor; conversions and cleanup work; dependency/license decision is recorded. |
| [x] | 52 | Add fixed simulation steps with bounded catch-up and render interpolation. | The falling-box test behaves consistently at 30/60/120 render fps; excess backlog is reported. |
| [x] | 53 | Add raycast and collision-layer queries. | Known hit/miss/filter fixtures return correct objects and distances. |
| [x] | 54 | Add a capsule character with flat-ground movement and wall collision only. | Movement stops at walls and remains grounded; camera is not the physics body. |
| [x] | 55 | Add jump and ceiling collision. | Jump/land events occur once; jumping into a low ceiling cannot pass through it. |
| [x] | 56 | Add slope limits. | The capsule climbs an allowed ramp and cannot climb a steeper forbidden ramp. |
| [x] | 57 | Add a third-person follow camera with obstruction checks. | Camera follows the capsule and does not pass through a wall behind it. |
| [x] | 58 | Add named move/jump input actions and focus handling. | UI typing does not move the player; one press is not repeated across physics substeps. |
| [x] | 59 | Drive idle/walk/jump animation from actual character motion. | Running into a wall stops walk animation; jumping selects and exits the jump state. |
| [x] | 60 | Add a compiled C# behavior lifecycle with one interaction example. | A behavior starts/stops once and responds to one interaction without leaking across scene reload. |
| [x] | 61 | Add one imported audio clip with scene ownership and volume control. | Interaction plays the clip; muted volume works; unloading stops/releases it. |
| [x] | 62 | Add editor play-on-clone and stop-to-restore behavior. | Moving/deleting objects during play does not change the authored scene after stopping. |

**Release C: game prototype.** A packaged test level supports movement, collision, jumping, animation, and an audible interaction. Stairs, moving platforms, gamepad rebinding, and root motion remain explicit follow-up tasks.

## Stage 7 — Render a short animated scene

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [x] | 63 | Add a sequence asset with duration and one character clip track. | Evaluating any absolute time produces the same pose regardless of seek history. |
| [x] | 64 | Add camera transform keys and camera-cut tracks. | Known times select the expected camera and pose; scene objects retain stable references. |
| [x] | 65 | Add sequence play/pause/scrub UI. | Forward/backward scrubbing updates the scene without firing gameplay events. |
| [x] | 66 | Add export to a dedicated render target at a requested resolution. | Export dimensions do not depend on window size; resources are released after capture. |
| [x] | 67 | Export numbered PNG frames at `start + frameIndex / fps`; disable live physics during this first capture mode. | Ten seconds at 30 fps exports exactly 300 frames; selected frames match preview sample times. |
| [x] | 68 | Add export manifest, progress, cancel, and write-error handling. | Cancellation/errors leave clearly marked incomplete output; successful manifest records asset versions/settings. |
| [x] | 68a | Add versioned sequence JSON for duration, character tracks, camera keys/cuts, and stable IDs/references. | CPU round-trip preserves every serialized value; unsupported versions and missing target/asset/clip/camera references fail clearly. |
| [x] | 68b | Integrate sequence save/open into CharacterStudio and export a reopened timeline. | Save, restart, reopen the same scene and sequence, then reproduce sampled pose/camera state and frame timing/settings. |

**Release D: scene rendering — PASS (23 September 2026).** CharacterStudio saved and reopened the same scene and sequence, then exported the same three PNG frames with matching SHA-256 hashes on the same configuration. Cross-GPU pixel identity and arbitrary physics seeking are not promised.

## Stage 8 — Make it reusable

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [x] | 69a | Add a versioned project file with a safe project-relative startup-scene setting. | Relative startup scenes resolve from the project file location, independent of process working directory; missing or escaping paths fail clearly. |
| [x] | 69b | Add a minimal outside-consumer project template and generator. | Generate a project outside this repository, build it against Ember by reference without copying engine source, and load its configured startup scene. |
| [x] | 70a | Resolve project content paths from the project file and let CharacterStudio open a project startup scene. | After changing the working directory, the startup scene and each referenced GLB resolve inside the project root; missing/escaping paths identify the scene object, asset ID, and path. |
| [x] | 70b | Build a project package from its startup scene and referenced GLBs. | The package contains the project file, startup scene, referenced GLBs, and local buffer/image files named by GLB URIs; missing or escaping references fail with object ID, asset ID, and path. |
| [x] | 70c | Expose project packaging through the generated minimal consumer. | The generated consumer can create a package without opening a graphics window, and the package opens after being moved to another directory. |
| [x] | 71a | Add a self-contained Windows x64 publish flow for the generated consumer. | Publish output contains the app, .NET runtime, engine assemblies, native dependencies, and project content. |
| [x] | 71b | Run the published distribution from outside the checkout without SDK/runtime lookup. | From a copied output folder and unrelated working directory, the app starts with `dotnet` absent from `PATH` and displays the starter scene. |
| [x] | 72a | Extend the minimal consumer to render skinned scene objects and play their saved clip settings, with simple character movement. | A saved CharacterStudio Fox scene animates in the consumer; WASD moves it and Escape exits. |
| [x] | 72b | Verify the animated project survives packaging, relocation, and self-contained publishing. | The published app opens the relocated project without SDK/source access and captures the animated character. |
| [x] | 72c | Write a tutorial using only the accepted workflow and list unsupported features. | Following it creates an animated scene and playable Windows build without editing engine source. |

**Release E: small reusable engine.** Preserve the sample projects as regression tests. Reassess architecture only after another real project encounters a concrete limitation.

## Stage 9 — Exterior and interior cells

Start after task 72. Put reusable cell/loading code in `Ember.Engine/World`; keep RPG definitions in `Ember.Rpg` and demonstrations in a new `samples/RpgSlice`. Inspect Campaign's `LocationRunner` and `HeightmapTerrain` for reusable ideas, but do not copy their game dependencies into the engine.

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [x] | 73 | Define stable cell IDs and a world manifest listing exterior coordinates and interior scene references. | Duplicate IDs/coordinates and missing scene references fail validation. |
| [x] | 74 | Add world-position-to-exterior-cell conversion using configurable cell width. | Boundary and negative-coordinate fixtures return the expected cells. |
| [x] | 75 | Create RpgSlice with one exterior cell loaded through the manifest. | It renders and supports existing player movement without Campaign dependencies. |
| [x] | 76 | Add an explicit cell lifecycle: unloaded, preparing, ready, active, unloading, failed. | Invalid transitions are rejected and failed loading releases temporary resources. |
| [x] | 77 | Prepare one cell's CPU data asynchronously; activate graphics/physics resources on their owning thread. | Loading records thread ownership correctly and never exposes a partially active cell. |
| [x] | 78 | Add a configurable nearby-cell loading ring around the player. | Crossing a boundary requests the correct neighbors exactly once. |
| [x] | 79 | Add a bounded per-frame activation/upload queue. | A multi-cell load respects configured work limits and exposes queue/timing diagnostics. |
| [x] | 80 | Unload cells outside a wider retention ring using asset reference counts. | Shared assets stay alive while another cell needs them; repeated crossings do not accumulate resources. |
| [x] | 81 | Cancel obsolete load requests and reject stale completion results. | Rapid travel cannot activate old destination cells or leak their prepared resources. |
| [x] | 82 | Prevent movement into a required cell until its collision is ready; expose a loading state. | Artificially delayed loading cannot make the player fall through missing terrain. |
| [x] | 83 | Add a door component with destination cell ID, spawn ID, and facing. | Validation catches invalid destinations; a door enters an interior at its authored spawn. |
| [x] | 84 | Add transactional interior/exterior travel: preserve the source until destination activation succeeds. | Exit returns to the intended exterior; failed destination loading leaves the source playable. |

**Integration gate: Pending.** Engine tests cover two interior trips and explicit returns, plus preparation, activation, placement, and cancellation failures. RpgSlice still needs player door interaction and live destination activation before the gate can pass. Then walk through a 3×3 exterior test grid, enter two independent interiors, and return. Test slow loads, failed loads, and rapid changes of direction. Record frame-time spikes and resource counts.

## Stage 10 — A persistent world

Authored definitions are immutable. Save changes by stable world-instance ID, not by array index or display name. Scene editing files and player save files remain separate.

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [x] | 85 | Add stable world-instance IDs distinct from asset and cell IDs. | Two instances of one asset have different IDs that survive cell reload. |
| [x] | 86 | Add a per-cell change store for object transforms and enabled state. | Move/disable an object, unload its cell, and reload; its changes remain. |
| [x] | 87 | Add deletion tombstones for authored objects. | A removed authored object does not return after reload. |
| [x] | 88 | Add runtime-created object records with persistent IDs. | A spawned test item survives reload without acquiring a duplicate ID. |
| [x] | 89 | Transfer an instance between cell ownership records atomically. | Moving an item across a boundary and reloading both cells leaves exactly one instance. |
| [x] | 90 | Write/read a versioned world save containing player location and cell changes using safe replacement. | Restart restores the same world; a failed write preserves the previous save. |
| [x] | 91 | Validate save/content versions and add one schema migration fixture. | A supported old fixture migrates; unsupported or missing definitions produce actionable diagnostics. |
| [x] | 92 | Queue save requests until a stable simulation boundary, including during travel. | A save requested mid-transition restores one complete location, not mixed source/destination state. |
| [x] | 93 | Add explicit cell reset policy, defaulting to no reset. | Resettable content resets only under its rule; persistent/quest-marked instances remain unchanged. |

**Gate: Pending.** CPU fixtures now cover object edits, deletion, runtime objects, cell transfer, and save/restart. The world loader and RpgSlice still need to connect these stores for a live two-cell gameplay pass. Then move, create, and delete objects across two cells; travel inside; save and restart. Every instance must have the expected location/state exactly once.

## Stage 11 — RPG records and interactions

Extend existing `Ember.Rpg` records, flags, inventory, dialogue, and quests where they fit. Inspect their current checks before adding replacements. Keep these systems independent of rendering.

| Done | ID | Implement only this | Pass when |
| --- | --- | --- | --- |
| [x] | 94 | Register typed content IDs for actors, items, factions, dialogue, and quests. | Broken cross-references identify the source record and missing target. |
| [x] | 95 | Add actor attributes, skills, and derived health/magicka/stamina through explicit formulas. | Numeric fixtures verify base values and recalculation without accidental permanent changes. |
| [x] | 96 | Add timed stat modifiers with explicit stacking/expiry rules. | Applying, stacking, expiring, and saving/loading a modifier give expected values. |
| [x] | 97 | Persist container contents by world-instance ID using existing inventory primitives. | Looting and cell reload cannot recreate taken items. |
| [x] | 98 | Add atomic pickup/drop transfers between inventory and world instances. | Failed transfer preserves the original item; successful transfer neither duplicates nor loses it. |
| [x] | 99 | Connect equipment slots to stats and visual bone attachments. | Equip/unequip updates modifiers and attachment once; save/load preserves both. |
| [x] | 100 | Add a basic melee attack with range, cooldown, and damage rules. | One attack applies damage once; out-of-range targets take none. |
| [x] | 101 | Persist actor death and inventory state. | A dead looted actor stays dead and looted after travel/restart. |
| [x] | 102 | Add one targeted spell using the modifier system, resource cost, and casting cooldown. | Invalid casts consume nothing; valid casts apply their cost/effect exactly once. |
| [x] | 103 | Add faction membership and reputation records. | Changes persist and two actors/factions do not share mutable state accidentally. |
| [x] | 104 | Evaluate dialogue choices against stats, flags, and faction conditions using existing dialogue structures. | Choices appear correctly and execute effects once, including after reload. |
| [x] | 105 | Connect quest objectives to interaction/death/item events through stable IDs. | A quest survives target cell unload and handles dead/missing targets explicitly. |
| [x] | 106 | Add one merchant transaction using inventory transfers and currency. | Insufficient funds/stock changes nothing; a valid trade updates both parties atomically. |
| [x] | 107 | Add ownership checks and one witnessed theft event affecting reputation. | Unwitnessed and witnessed test cases follow documented rules without duplicate penalties. |
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

- Last completed tasks: 76–78 — cell lifecycle, owner-thread activation, and exterior loading ring.
- Current task: 79 — add a bounded per-frame activation/upload queue with work limits and timing diagnostics.
- Current checklist: 89 of 154 ordered rows complete (57.8%); task 79 is the first unchecked row.
- Current gate: Release E passed on the tested Windows host; Stage 9 now has a manifest-backed exterior sample, validated cell state, asynchronous CPU preparation, and ring request planning.
- Changed files for the latest batch: `CellLifecycle.cs`, `WorldCellLoadOperation.cs`, `ExteriorCellLoadingRing.cs`, their engine tests, and this roadmap/progress update.
- Checks: `dotnet build Ember.sln --nologo` (0 warnings, 0 errors); `dotnet test Ember.sln --no-build --nologo` (143 passed, 0 failed); RPG save/load check passed. Focused lifecycle/preparation/ring tests passed (16 total), including failure cleanup, worker/owner thread identity, atomic active-resource publication, boundary crossing, exact-once requests, and re-request after forgetting an unloaded cell.
- Results: lifecycle accepts only authored state transitions and disposes preparation data on failure. Cell CPU preparation runs on a worker; activation and unload require the captured owner thread; consumers see active resources only after the activation callback succeeds. The square loading ring uses the shared floor-based grid mapping and suppresses duplicate requests.
- Limitations: thread-affinity tests use disposable fixtures rather than live graphics-device resources. The ring produces coordinate requests; queue budgeting, cancellation, retention, and concrete world activation remain later tasks.
- Next action: task 79, add a bounded per-frame activation/upload queue with diagnostics.

Suggested request to an implementing AI:

> Read Docs/ENGINE_ROADMAP.md. Complete the first unchecked task whose prerequisites are finished. If the user requests a consecutive batch, do those tasks in order and pass each task's acceptance check before marking its row. Follow the implementation rules, update the handoff, and do not claim untested behavior works.

## Plan review — 23 September 2026

### How much is built

Updated against the current working tree on 23 September 2026. **65 of the original 143 task rows are checked (45.5% by task count); 78 remain unchecked.** Tasks 38a–38b and 47a are additional checked rows outside that original denominator. This is not a percentage of engineering effort or RPG readiness: the later world, persistence, physics, tools, and gameplay work is substantially larger than many early rows. Additional tasks proposed below are not included in that denominator.

| Area | Current evidence and status |
| --- | --- |
| Stages 1–2: scene foundation (01–15) | Scene IDs, transforms, hierarchy validation, versioned JSON, atomic file replacement, orbit camera, host cleanup and scene resource ownership exist. CPU fixtures exercise these contracts. Historical visual checks are recorded in ENGINE_PROGRESS.md; this review did not repeat every stage gate. |
| Stage 3: static assets (16–25) | SharpGLTF import, authored transforms, opaque base-color textures, GPU buffers, bounds, stable asset references and staged reimport exist. CharacterStudio now loads and draws distinct static and skinned GLBs together while sharing each asset across its scene instances. |
| Stage 4: characters (26–38b) | Skin/weight import, per-instance poses, playback, local-pose blending, bone attachment, sampled animation bounds and version-2 character persistence exist. Release A passed with the saved courtyard/characters/attachment showcase and a captured reopen. |
| Stages 5–8: tools, gameplay, sequence export, distribution (39–72) | Tasks 39–69b establish CharacterStudio authoring controls, gameplay foundations, persisted/reproducible sequence export (Release D passed), and an outside-consumer project template. Scene/asset packaging and distribution remain. |
| Stages 9–15: cell-based RPG (73–143) | No completed roadmap rows. Ember.Rpg already supplies inventory, equipment, flags, dialogue, quests and a save round-trip check. Campaign contains game-specific world/rendering examples. Neither establishes reusable cell streaming, world-instance persistence, NPC travel or the integrated RPG slice. |

Verification run for this review:

- `dotnet build Ember.sln --nologo`: PASS, 0 warnings and 0 errors.
- `dotnet test Ember.sln --no-build --nologo`: PASS, 94 passed, 0 failed, 0 skipped.
- `dotnet run --project tests/Ember.Rpg.Check --no-build`: PASS, `[OK] save then load equals original`.

The latest renderer batch produced 1280×720 runtime captures with two character instances, directional static/skinned shadows, the render diagnostics overlay, and the sequence preview panel. Physics/input/animation and behavior/audio/clone/sequence lifecycles have CPU coverage, but the new play-mode interaction, sequence scrubbing through desktop input, and real audio playback were not automated. Clean-machine packaging, a resource soak, and direct automated UI input remain unverified.

### Recommended changes, in priority order

**1. Release A scheduling gap.** Resolved by completed tasks 38a–38b. `PreviewResources` now loads each referenced asset ID once, keeps GPU resources/material textures scoped per asset, and dispatches static or skinned draws per scene object. The authored Release A courtyard scene and its before/reopen captures are the gate evidence.

**2. Failed-open save protection.** Addressed in CharacterStudio: S targets an opened scene only after a successful load; a failed open disables saving back to that path, including when `--save` names the same invalid source. An explicit different `--save` path still writes the recovery scene. A runtime check confirmed the invalid source's SHA-256 stayed unchanged while Save As produced version-2 JSON.

**3. Define a project/content root before adding a general filesystem asset browser.** Task 44's asset list intentionally shows GLBs already referenced by the open scene and places more instances of those loaded assets. `GltfAssetReference` calls its paths project-relative, while CharacterStudio resolves them against `AppContext.BaseDirectory`; arbitrary external projects still have no explicit root contract. Add one resolver used by open, reimport and packaging, with diagnostics containing asset ID and resolved path. Acceptance: an external project reopens and reimports from a different working directory, then still works after the complete project folder is moved. Preserve stable IDs and relative serialized paths. Keep this a small project-root contract, not a general asset database.

**4. Tighten importer and animation-bound claims before relying on them.** `GltfSkinnedCharacterData.Import` collects the one skinned mesh but does not import or explicitly reject additional unskinned mesh nodes in the same GLB. This can omit authored geometry despite the plan's no-silent-partial-import rule. Add a negative fixture and reject this combination until intentionally supported. Separately, `GltfAnimationBounds.SampleClip` uses uniform samples plus padding: dense coverage of the bundled Fox clips is evidence for that fixture, not a proof for every accepted clip or blend. Before animated culling is introduced, add fast/short-key-interval and crossfade fixtures, include attachment extents where needed, and retain an uncullable fallback for unverified poses. Do not label all STEP/LINEAR clips conservatively bounded solely because they parse.

**5. Bring measurement forward, while keeping the populated-world benchmark in Stage 13.** Task 50 already introduces diagnostics; use it to establish reference hardware, resolution and an initial representative mixed scene. Record draw counts, frame-time percentiles, import/reimport latency and owned resources. Then measure task 79's activation queue under deliberately slow loads. Keep tasks 119/127 for the richer outdoor benchmark. Current CharacterStudio loading samples every vertex across every animation synchronously, so import cost should be measured as well as steady-state rendering. Set budgets from measurements rather than adding speculative performance promises.

**6. Make the first gameplay proof earlier and make deferred prerequisites explicit.** Keep Release A and the editor foundation first, but consider moving Stage 7's cinematic export branch after a minimal RPG interaction proof if the RPG is the main delivery goal. At the end of Stage 6, demonstrate movement, a door or interaction target, a simple inventory change and save/restart in one small level using existing Ember.Rpg primitives. This is a smoke test, not an early replacement for Stages 9–12. Before task 137's settlement, explicitly decide whether authored ramps are sufficient or promote step climbing from the optional list into required tasks. Likewise, record whether third-person alone satisfies the intended RPG slice or whether a first-person mode is required. If reordering is adopted, update stage prerequisites and the first-unchecked-task rule together.

**7. Add authored runtime-component data before relying on play mode for the RPG.** Tasks 60–62 provide compiled behavior registration, a scene-owned audio example, and an isolated CharacterStudio play clone, but the sample currently wires its behavior in code and does not simulate the roadmap's physics character. Add small tasks for assigning a collider and compiled behavior to a scene object, persisting their settings, and validating references. Acceptance: save/reopen a level with a floor collider and one interaction behavior, start play, observe movement and interaction, stop, and confirm authored state is restored. Reuse the existing scene model and avoid introducing a broad component framework in advance.

**8. Separate task completion from release-gate evidence.** Release A now has dated scenario and capture evidence. Keep the Stage 2 reload/resource gate separate from CPU disposal fixtures. Attachments currently persist a bone name and local offset and render as a preview cube; task 99 should explicitly add an equipment asset reference rather than assume arbitrary prop persistence already exists. Keep README's CharacterStudio capability summary current as more of the supported import subset is added.

### Suggested next sequence

Release A, editor foundation tasks 39–50, gameplay foundation tasks 51–62, and sequence-preview tasks 63–65 are complete; start task 66. Resolve the project-root contract before adding arbitrary filesystem browsing, and retain the remaining importer, measurement, authoring, and release-gate recommendations at their stated boundaries. Keep the current architecture, pinned dependencies and small-task approach; finish integrated workflows with explicit evidence.

This section records current scope and review recommendations. Roadmap checkboxes reflect the task evidence recorded in `ENGINE_PROGRESS.md`.

## Updated implementation and co-op readiness review — 23 September 2026

This is a fresh review of the working tree, including uncommitted sequence-export work. It supersedes the earlier review's progress and test totals for this snapshot. Recommendations below do not change task priorities or authorize implementation; the existing RPG track is retained until a product-priority decision is adopted.

### Actual completion and verification

**75 of 149 ordered roadmap rows are checked (50.3% by row count): original tasks 01–68 plus 38a, 38b, 47a, 68a/b, and 69a/b.** Against the original baseline this is 68/143 (47.6%). There are 74 unchecked ordered rows. Neither percentage measures remaining effort, commercial readiness, or completion of the proposed studio game.

| Area | What is present | Remaining boundary |
| --- | --- | --- |
| Scene and asset foundation, 01–38b | Hierarchy, transforms, validated scene JSON, resource scopes, static/skinned GLB assets, independent character playback, attachments and mixed-asset preview. | Release A has recorded before/reopen captures. This review did not repeat its visual checks or the resource soak. |
| Authoring and rendering, 39–50 plus 47a | ImGui panel, selection, transform and object history, loaded-asset placement, animation controls, shared lighting, custom effect build, static/skinned shadows, static culling and diagnostics. | Release B still needs an end-to-end authoring acceptance session. Rendering a panel does not verify typing, focus, selection, undo or all DPI interactions. |
| Gameplay components, 51–62 | BEPU world and capsule controller, fixed stepping, jumps/slopes, obstruction camera, named actions, motion-driven animation, behavior/audio ownership and isolated play clones. | CharacterStudio's play session wires an audio interaction but does not construct the physics controller. The packaged integrated Release C level remains absent, as ENGINE_PROGRESS.md explicitly records. |
| Sequence and export, 63–68b | Absolute-time character/camera tracks, versioned sequence JSON, exact-size PNG output, manifests, cancellation/error handling, and CharacterStudio restart/reopen workflow. | Release D passed on this machine/configuration with identical before/after frame hashes. Cross-GPU identity is not promised. |
| Project and distribution, 69a–72 | Versioned project startup-scene config and an external consumer template build against the engine checkout. | Generated consumers still require the checkout/SDK; task 70 content packaging and task 71 clean-machine distribution remain unchecked. |
| Cell-based RPG, 73–143 | Existing Ember.Rpg primitives and Campaign examples remain reusable starting points. | Planned cell streaming, world persistence, navigation and integrated RPG slice remain unchecked. |
| Co-op studio simulator | Rendering, input, audio and scene primitives can be reused. | No studio economy/project simulation or multiplayer implementation was found in the inspected engine/test sources. Engine progress must not be reported as game completion. |

Historical checks at the review snapshot (superseded for export work by the implementation update at the end of this file):

- `dotnet build Ember.sln --nologo`: PASS, 0 warnings, 0 errors.
- `dotnet test Ember.sln --no-build --nologo`: FAIL, 97 passed, 1 failed, 98 total, 0 skipped.
- Failing test: `SequenceFrameExportTests.CompletingFramesWritesHashesSettingsAndCompleteManifest`, line 48 in the tested source: expected 3 frames, actual 4.
- `dotnet run --project tests/Ember.Rpg.Check --no-build`: PASS, `[OK] save then load equals original`.

The checkout contains work in progress and changed during inspection, so these results describe the build/test snapshot, not a promise about subsequent edits. No new graphics launch, desktop input test, audio-device test, multiplayer session or clean-machine install was performed. Existing screenshots and earlier passing totals are historical evidence.

### Engine-plan changes to adopt

1. **Frame-boundary semantics for 66–68 — resolved.** The interval is end-exclusive. Frame counting interprets shortest round-tripping decimal float values and allows half an endpoint ULP for a value rounded from a frame boundary. Regression coverage includes 0.1 and 10 seconds at 30 fps, nonzero starts, a nonaligned endpoint, and adjacent floats on either side of boundaries.
2. **Track component implementation and release gates separately.** Keep existing completed rows as the implementation record, but add explicit Pending/Pass/Blocked gate entries with evidence. Revisit tasks whose acceptance was narrowed to source inspection or a static capture. In particular, complete Release B through actual select/edit/undo/save/reopen actions and Release C through movement, collision, jump, animation and audible interaction in a packaged level. Add atomic integration rows instead of assuming all checked components establish the gate.
3. **Sequence persistence before Release D — verified.** Tasks 68a/68b store and reload track IDs, target IDs, asset/clip references, camera keys/cuts, and duration. CharacterStudio reopened a saved scene and sequence in a new process; all three selected GPU frame hashes matched the original export. Record the scene/sequence and loaded asset versions actually used by the renderer when packaging; a file changed after import can still make a source-file hash misdescribe in-memory data.
4. **Resolve content roots before packaging.** Task 69a gives projects a root relative to `ember.project.json` for their startup scene. GLB asset loading/reimport still resolves against `AppContext.BaseDirectory`; task 70 must make packaging resolve content from the project root. Verify after changing the working directory and moving a complete project folder. Include native libraries and compiled effects in a clean-machine package check.
5. **Retain unresolved correctness and measurement work.** The skinned importer still collects a single skinned mesh without explicitly rejecting additional unskinned mesh geometry in the same GLB. Reject or support that case with a fixture. Sampled animation bounds remain fixture-tested estimates; keep unverified poses uncullable. Measure import latency, frame-time percentiles and owned resource counts on a representative mixed scene; perform repeated reload/play/stop/export cancellation checks rather than infer leak freedom from CPU disposal tests.

### Fit for the proposed game

The current target says single-player, cell-based RPG and lists multiplayer as later work. The new pitch is **Game Dev Tycoon with friends, where players learn how games are actually made**. These are separate product tracks. Recommend retaining the RPG roadmap and creating a dedicated studio-game plan, with an explicit prerequisite list rather than forcing that game through all 143 original tasks. If the studio game becomes the priority, update the opening target and first-unchecked-task scheduling rule deliberately; do not silently skip existing tasks.

Use Ember for presentation, input, audio and reusable controls. Keep studio rules, economy, employee skills, task dependencies and learning scenarios in a game-owned module that can run without graphics. The player-facing interface needs task boards, tooltips, scrolling, focus and clear feedback; CharacterStudio's ImGui tools do not establish that interface. Terrain, cell streaming and RPG combat are not prerequisites for a management prototype.

Co-op should use host-authoritative studio state: players submit validated commands and the host applies them in a defined order. Local UI selections/cameras stay local. Use stable player/task/project IDs, command IDs, state revisions and explicit permissions. Do not require deterministic physics lockstep for the management simulation. The existing physics catch-up policy drops excess elapsed time; define a separate management-clock policy so fast-forward or a slow frame cannot silently skip wages, deadlines or completed work.

### Proposed studio-game milestones, separate from the current ordered tasks

These are recommendations, not additional completed roadmap rows. Split each milestone into the existing small-task format before implementation.

| Order | Deliverable | Acceptance evidence |
| --- | --- | --- |
| 1 | CPU-only studio state, simulation clock and command processing | Given the same seed and accepted ordered commands, outcomes repeat. Pause and speed changes obey documented rules; budgets cannot be spent twice and invalid commands change nothing. |
| 2 | One solo production loop with design, programming and QA | Scope a small project, implement a feature, discover/fix a defect, release, and receive feedback tied to those decisions. A player can explain one learned trade-off without being told the answer. |
| 3 | Versioned studio save and recovery | Save/restart restores projects, staff, money, pending work, clock and random state; an interrupted write preserves the previous valid save. Scene files remain separate from studio progression. |
| 4 | Two-player authoritative command/snapshot proof | Two processes join one studio. Simultaneous assignment/spending resolves once, duplicate commands do not duplicate effects, and stale or unauthorized edits are rejected clearly. Both players see the same resulting state. |
| 5 | Rejoin, version compatibility and host-loss behavior | A returning client receives a coherent snapshot and resumes without duplicated work. Incompatible builds/content fail clearly. For the first release, host exit ends the session and recovery uses the host save; host migration is deferred. Test delayed, duplicate and disconnected requests. |
| 6 | Four-player roles, permissions and shared time controls | Four connected clients can contribute at once; players may switch roles and vacant roles are handled by staff. Pause/fast-forward policy is explicit, one player cannot silently disrupt everyone, and simultaneous edits have visible outcomes. |
| 7 | One controlled playable game template | Development choices visibly change an authored prototype. If players can play it together, validate its separate real-time networking needs; shared management commands alone do not provide multiplayer movement. Avoid arbitrary game generation in the first version. |
| 8 | Internet invites, packaged playtest and onboarding | Select and test a transport/lobby integration, connection failure UX and compatibility handshake. Four packaged Windows clients on separate machines complete a project, save, disconnect and rejoin. Validate remote connectivity, not only localhost. |

Recommended product-validation scope: one office, one genre, three hands-on disciplines, one hire, one release and one patch. Prove two-player collaboration before four-player polish. Represent later large studios through departments and aggregate work; measure load before promising thousands of individually simulated staff. The educational gate should test understanding of consequences, while the co-op gate should test whether everyone has useful work and reasons to interact.

**At this review snapshot**, sequence export tasks 66–68 were still in progress. The implementation updates at the end of this file record their completion and subsequent sequence/project work, superseding that action. **Recommended planning action for the new game:** write the separate studio-game milestones and their engine prerequisites before committing to RPG-specific expansion.

## Implementation update — tasks 66–68 — 23 September 2026

Tasks 66–68 are complete. Frame ranges are end-exclusive, with explicit float boundary handling and tests on both sides of exact boundaries. CharacterStudio renders numbered PNGs through a dedicated target at the requested resolution, one frame per draw; it blocks export during play-on-clone. The version-1 manifest records range, dimensions, frame rate/count, sequence name, asset IDs/paths/hashes/lengths, progress, and canceled/failed/completed state. Startup export flags support a reproducible GPU smoke run and return a failing process exit code when export fails.

Verification:

- `dotnet build Ember.sln --nologo`: PASS, 0 warnings and 0 errors.
- `dotnet test Ember.sln --no-build --nologo`: PASS, 100 passed, 0 failed, 0 skipped.
- `dotnet run --project tests/Ember.Rpg.Check --no-build`: PASS, save/load equality.
- CharacterStudio GPU export from a 1280×720 window: PASS, three 640×360 frames for `[0, 0.1)` at 30 fps; all PNG headers are 640×360; manifest is `completed`, 3/3 frames, with one GLB version; process exited 0.
- CPU tests cover 300 frames for ten seconds at 30 fps, nonzero starts, a nonaligned endpoint, neighboring float values, cancellation, and frame-write failure.

At this update's snapshot, task 68a was first unchecked. The newer implementation update below records its completion and the restart/reopen evidence.

## Implementation update — tasks 68a–69b — 23 September 2026

Tasks 68a–69b are complete. Sequence data now round-trips as a companion versioned JSON file and CharacterStudio can reopen it with its scene before export. The release-gate run started separate processes for save/export and open/re-export; all three corresponding PNGs had identical SHA-256 hashes, and both manifests reported complete 3/3 exports with the same resolution, frame rate, and range.

The new project manifest resolves startup scenes from its own directory and rejects unsafe or missing startup paths. A generated Minimal Ember Game under `%TEMP%` built outside the repository while referencing this checkout's engine project, then loaded its configured startup scene and produced a visible 1280×720 screenshot. The generated project contains its starter source and content; it does not copy Ember source.

Verification for this batch:

- `dotnet build Ember.sln --nologo`: PASS, 0 warnings and 0 errors.
- `dotnet test Ember.sln --no-build --nologo`: PASS, 111 passed, 0 failed, 0 skipped.
- `dotnet run --project tests/Ember.Rpg.Check --no-build`: PASS, save/load equality.
- CharacterStudio restart/reopen/re-export and the external-consumer build/run: PASS, as detailed in ENGINE_PROGRESS.md.

At that earlier snapshot, after tasks 68a–69b and before tasks 70a–71b, 75 of 149 ordered rows were complete (50.3%), task 70 was first unchecked, and external consumers still depended on the engine checkout and SDK.

## Code review — commits through `ff85c39` (tasks 69a–78) — 23 September 2026

Scope: a read-through of the whole commit history, with the closest attention on `ff85c39` ("Complete engine roadmap work through task 78"). That commit contains tasks 69a–78 in a single change of 37 files and about 3,800 lines. **89 of 154 ordered rows are checked. Task 79 is the first unchecked row; task 80 comes after it.** Findings cite file and line at `ff85c39`. Severity labels: **High** means fix before building the next task on top of it; **Medium** means fix within Stage 9; **Low** means clean up opportunistically.

Build and test verification was not repeated for this review. Every project targets `net9.0-windows` with WindowsDX/WinForms, and the review machine was Linux without a .NET SDK. All findings come from reading the code. The recorded results (143 passed, 0 warnings) are the implementer's, from their Windows host.

### Overall

The code quality is high for a project at this stage. Validation errors name the object, asset, and path involved. Saves and packages are staged and then committed. Resource ownership is explicit, including disposal during partial failure. Tests exist for almost every contract. The roadmap discipline (small rows, recorded evidence, release gates) is the project's biggest strength, and it held up well through task 68b. The main risks now are:

1. The Stage 9 primitives pass their unit tests, but the tests do not exercise the way a game loop will actually use them.
2. The last batch skipped the one-task-per-change discipline that made the earlier work easy to review.

### Stage 9 (cell streaming): fix before tasks 79–82

| Sev | Finding | Where | Suggested fix |
| --- | --- | --- | --- |
| High | **Preparation completes on a thread-pool thread, not the owning thread.** `PrepareAsync` awaits `Task.Run(...).ConfigureAwait(false)`. Everything after that await runs on a pool thread: `SetPreparationResource`, `TransitionTo(Ready)`, and, in the catch block, `MarkFailed`. `CellLifecycle` has no synchronization, while the owner thread reads `State` every frame. The tests hide this because they block with `GetAwaiter().GetResult()`. A MonoGame `Update` loop cannot block like that. It has to start the task and poll, which is exactly where the race happens. Task 77's "records thread ownership correctly" holds for activation but not for the Ready transition. | `World/WorldCellLoadOperation.cs:53-75`; `tests/Ember.Engine.Tests/WorldCellLoadOperationTests.cs:101-103` | Keep the worker pure: it returns `TPrepared` and touches nothing else. Post the result to an owner-thread completion queue (`ConcurrentQueue`) that the game drains once per frame. Only the owner thread should call `SetPreparationResource`, `TransitionTo`, or `MarkFailed`. Add a test that polls `State` from the owner while preparation is blocked on a gate, without calling `GetResult()`. Do this before task 79, because the activation queue will consume this completion path. |
| High | **No way to discard a cell that never becomes Active.** `CellLifecycle` allows `Ready → Unloading`, but `WorldCellLoadOperation.Unload()` accepts only `Active`. `Preparing` can only become `Ready` or `Failed`. There is no reset from `Failed` either, so the `Failed → Unloaded` transition is unreachable. When the player turns back, the ready cell's prepared data just sits there. Tasks 80 and 81 depend on this path. | `World/CellLifecycle.cs:23-28`; `World/WorldCellLoadOperation.cs:104-108` | Add `Discard()` for Ready (dispose the prepared data, move to Unloaded) and `Cancel()` for Preparing (cancel the token; when the result arrives, dispose it instead of publishing it). Decide explicitly whether one operation instance can be retried after failing, or whether each attempt gets a new instance. Then delete whichever lifecycle edge is left unused. |
| High | **Stale completions are indistinguishable from current ones.** If preparation returns a value after the token was cancelled, the value is still published as `Ready`. Task 81 needs a request generation/ID, and the completion must be checked against it on the owner thread. | `World/WorldCellLoadOperation.cs:53-66` | Stamp each request with a monotonically increasing generation. When a completion's generation is not the current one for that cell, dispose it on the owner thread. Also call `cancellationToken.ThrowIfCancellationRequested()` after `prepare` returns. |
| Medium | **`Activate` is all-or-nothing, so task 79 can only cap cells per frame, not work per frame.** One large cell will still spike the frame no matter what queue limit is set. | `World/WorldCellLoadOperation.cs:82-101` | For task 79, have the prepared data declare an upload cost (bytes, vertices, or textures). Budget the queue on that cost, and allow activation to run in resumable steps. Queue diagnostics should report cost, as well as cell count and milliseconds. |
| Medium | **The loading ring only reports additions, and in X/Z order rather than by distance.** Task 80 needs the set of cells that left the retention ring. The request order should put the player's own cell first, then the nearest ones. Right now (-1,-1) is requested before (0,0). The ring also allocates a `List`, a LINQ sort, and an array on every call, including frames where the centre cell did not change. | `World/ExteriorCellLoadingRing.cs:28-48` | Cache the last centre cell and return an empty result early if it is unchanged. Return `(entered, left)` computed against a larger retention radius, which gives hysteresis. Sort requests by Chebyshev distance, then X/Z, so the order stays stable. |
| Medium | **Cell width has no single source of truth.** `RpgSlice` hard-codes `cellWidth: 32f`. The ring and the grid each take it as a parameter, and `world.json` does not store it. If two call sites disagree, requests and loads will silently disagree too. | `samples/RpgSlice/RpgSliceGame.cs:47`; `World/WorldManifest.cs:27-48` | Add `exteriorCellWidth` to a version-2 world manifest, with a migration fixture. Build the ring and the grid from the manifest. |
| Medium | **Manifest lookups are linear, and exterior cells are keyed by loose `int?` pairs.** `RpgSlice` scans with `SingleOrDefault`, and the ring's requests may name coordinates the manifest doesn't contain (world edges). | `World/WorldManifest.cs:21-22,54`; `RpgSliceGame.cs:48` | Store `ExteriorCellCoordinate?` and build `Dictionary<Guid,…>` and `Dictionary<ExteriorCellCoordinate,…>` indices at load. Add `TryGetExterior(coordinate)` so edge coordinates resolve to "no cell" rather than an error. |
| Medium | **The manifest requires every scene file to exist, even when saving.** Because of that, `SaveAtomic` cannot write a manifest for a cell whose scene hasn't been created yet. That will block task 128 (the cell browser). The manifest also allows two cells to point at the same scene path, which will produce duplicate instance IDs once task 85 lands. | `World/WorldManifest.cs:106,156-159` | Keep the file-existence check in `Load` (or a separate validation pass) and drop it from the save path. Reject duplicate scene paths now, or document why sharing is allowed. |
| Low | Unknown JSON properties are ignored across the manifest, project, sequence, and scene files. A typo such as `exteriorY` passes silently. A missing `kind` defaults to `Exterior`. | `WorldManifest.cs:32-38`, `EngineProjectFile.cs:13-18` | Set `UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow` (available since .NET 8). Make `kind` required. |
| Low | `RpgSlice` does not use any of the task 76–78 systems. It loads one cell synchronously. The only proof of the lifecycle, preparation, and ring code is the unit-test fixtures. | `RpgSliceGame.cs:44-74` | Make task 79's acceptance check run inside `RpgSlice` on a 3×3 grid that includes an artificially slow cell, not just in a unit test. |

### Correctness issues outside the cell system

| Sev | Finding | Where | Suggested fix |
| --- | --- | --- | --- |
| Medium | **The minimal template draws non-skinned GLBs as brown cubes, with no warning.** `LoadCharacterInstances` skips any GLB without a skin, and `Draw` falls back to `DrawCube` for every object that isn't a character. A CharacterStudio scene with a static environment, like the Release A courtyard, therefore renders as boxes in a packaged game. That violates the "unsupported content must fail clearly" rule. | `templates/MinimalGame/Program.cs:181-188,227` | Either render static GLBs (see the shared-renderer suggestion below) or refuse to load, naming the object and asset. Add the Release A scene as a fixture for the consumer. |
| Medium | **The RpgSlice player jitters against its camera.** The camera follows `GetInterpolatedPose(...)`, but the player cube is drawn at `_player.Pose.Position`, which is the latest physics step. Unless the frame rate is an exact multiple of the step rate, the two drift apart every frame. | `samples/RpgSlice/RpgSliceGame.cs:111-112,148-149` | Store the interpolated position in `UpdateMovement` and use it for both the camera and the draw. |
| Medium | **Packaging only follows the startup scene.** World manifests, cell and interior scenes, sequences, audio clips, and compiled `.fx` output are not collected. As a result, `RpgSlice` cannot be packaged, and task 142 would find this late. | `Project/EngineProjectPackage.cs:23-25,50-109` | Before task 84, collect packaging roots from the project file (startup scene, optional world manifest, and an explicit list of extra content). Walk each root through the same validators. Add a packaged-`RpgSlice` fixture. |
| Low | The package step rewrites `ember.project.json` from only `StartupScenePath`. Any field added to the project file later will be dropped from packages without an error. | `EngineProjectPackage.cs:35` | Copy the validated project document, or round-trip the full document model. |
| Low | In `CharacterAsset`, the `SkinnedEffect` and the white texture are created before the `try`, so an exception in the `Texture2D` constructor leaks the effect. | `templates/MinimalGame/Program.cs:326-331` | Move both allocations inside the `try` block and make `Dispose` null-safe. |

### Earlier subsystems (scene, physics, input, rendering)

| Sev | Finding | Where | Suggested fix |
| --- | --- | --- | --- |
| Medium | **A jump request is dropped if the first physics substep is airborne.** `PreparePhysicsStep` clears `_jumpRequested` on every substep, grounded or not. When a render frame catches up several steps and the player lands on the second one, the press made that frame is lost. | `Physics/PhysicsCharacterController.cs:108-110` | Keep the request until it succeeds or a short buffer expires (about 0.1 s). Clear it only after a jump actually happens. Add a two-substep "land then jump" fixture. |
| Medium | **The shadow camera fits the whole scene's bounds, with no texel snapping.** Shadow resolution falls as the scene grows, and the shadows shimmer whenever the bounds move (for example, when animated characters move). This is fine for a single CharacterStudio showcase. It will not hold up once streamed cells make the "scene" several hundred metres across. | `Render/DirectionalShadowCamera.cs:10-25` | Before Stage 13, fit the shadow volume to the camera's view frustum (one cascade to start). Snap its centre to shadow-map texels. Put a fixed-camera shadow capture into the benchmark from task 119. |
| Medium | **`SceneGraph.GetWorldMatrix` allocates a `HashSet` on every call.** CharacterStudio calls it for each object in the shadow pass and again in the main pass. Stage 9 cells will multiply the object count. | `Scene/Scene.cs:68-72` | Cycles are already rejected by `SetParent` and at load, so walk the parent chain with a depth limit and no allocation. Alternatively, cache world matrices with a dirty flag. |
| Low | Per-frame allocations: `InputActionMap.Sample` builds a new dictionary each frame (`Input/InputActionMap.cs:100`). The shadow pass calls `GetRenderTargets()` each frame (`Render/DirectionalShadowMap.cs:44`). | as listed | Reuse buffers. Measure first with the task 50 diagnostics. |
| Low | The legacy static texture caches (`StoneTextures`, `PropTextures`, `ItemSprites`, `CharacterSprites`) each have `Clear()`, but nothing calls them, so their textures outlive the host's `GraphicsDevice`. Task 15 deliberately left these caches alone. | `Render/StoneTextures.cs:48` and the others | Call the four `Clear()` methods from `EngineHost.DisposeHost` as a one-line safety net. Do this before task 141's hour-long soak, or that soak will measure them. |
| Low (latent) | `CreateSceneObjectCommand.Revert` removes the object. `SceneGraph.Remove` then detaches its children, and redo does not reattach them. It can't happen today, because reparenting isn't recorded in the history and CharacterStudio never calls `SetParent`. | `Scene/SceneCommandHistory.cs:99-105`; `Scene/Scene.cs:33-41` | When a reparent tool is added, make it a history command. Then create/undo stays symmetric in a linear history, and a test can cover it. |
| Low | The culler tests only use scaled and translated bounds, not rotated ones. The code (8-corner transform) is correct. | `tests/Ember.Engine.Tests/DirectionalShadowTests.cs:68-79` | Add a 45° yaw fixture. |

### Earlier subsystems (glTF import, skinning, animation)

| Sev | Finding | Where | Suggested fix |
| --- | --- | --- | --- |
| High — Resolved 24 September 2026 | **The character importer silently dropped unskinned meshes.** It collected the one skinned node and ignored every other node that had a mesh, so props or armour baked into a character GLB disappeared without an error. | `Assets/GltfSkinnedCharacterData.cs` | Import now rejects an unskinned mesh node with a `NotSupportedException` naming it; `RejectsAdditionalUnskinnedMeshNodesByName` is the negative fixture. |
| High — Resolved 24 September 2026 | **Every skinned draw allocates a new bone array.** Both `Draw` overloads run `new Matrix[pose.JointCount]`, then copy the skin matrices into it. That is one allocation per mesh, per pass (shadow and main), per character, per frame. NPC counts in Stage 12 will multiply it. | `Assets/SkinnedMeshGpuBuffer.cs:110-112,132-136` | Each GPU buffer now owns one joint-count-sized scratch array and copies both draw paths into it; no bone array is allocated per draw. |
| Medium — Resolved 24 September 2026 | **A looping clip could sit at exactly `Duration`.** `Seek` allowed the endpoint while looped `Advance` always wrapped into `[0, Duration)`, causing a one-frame endpoint pose before the next advance. | `Assets/GltfAnimationPlayback.cs`; `GltfAnimationTests.cs` | Looped seeks at or beyond the duration now wrap to 0. Regression tests cover endpoint seeking, multiple-period wrapping, reverse non-loop playback reaching 0, and a zero-duration clip. |
| Medium | **Skeleton and rest-pose limits are checked late.** A skeleton with more than 72 joints imports successfully on the CPU and fails only when uploaded. A rest transform that is NaN or has a zero-length quaternion surfaces later as a pose error rather than an import error. | `Assets/GltfSkinData.cs:101-110`; `SkinnedEffectCompatibility` | Validate the joint count and finite/unit rest TRS inside `GltfSkinData.Import`, reusing the same messages. |
| Medium | **Attachment placement assumes the pose's world matrices are current.** `GltfBoneAttachment.GetWorldMatrix` reads the cached node matrices from the last `ComputeSkinMatrices`. A `SetLocalTransform` without a recompute gives a stale prop position. This matters for task 99 (equipment attachments). | `Assets/GltfBoneAttachment.cs:37-43`; `Assets/GltfSkinPose.cs:51-84` | Add a pose generation counter and assert that it is current, or recompute lazily. |
| Low | Neither GPU buffer checks for `GraphicsProfile.Reach` before creating a 32-bit index buffer, even though the skinning compatibility check accepts Reach. The shipped consumers use HiDef, so this is latent. | `Assets/StaticMeshGpuBuffer.cs:39-52`; `Assets/SkinnedMeshGpuBuffer.cs:54` | Reject indices above 65,535 on Reach with a clear error, or require HiDef throughout the engine. |
| Low | `SkinnedMeshGpuBuffer.Draw` accepts any pose with the same joint count, even one from a different skin. Other code uses `UsesSkin`/`ReferenceEquals`. | `Assets/SkinnedMeshGpuBuffer.cs:102,129` | Store the skin and require `pose.UsesSkin(skin)`. |

Done well in these areas:
- Unsupported glTF features fail loudly with good negative tests: CUBICSPLINE, morph targets, `JOINTS_1`, required extensions, and non-opaque materials.
- The skinning math is right: inverse-bind × joint-world × inverse-mesh-world gives identity at the bind pose on the Fox fixture, and slerp takes the shortest path.
- `ReloadableAsset` swaps only after a successful load.
- GPU buffer constructors clean up partial uploads.

Done well in the scene, physics, input, and rendering areas:
- `PhysicsFixedStepper` reports the backlog it drops and keeps its interpolation alpha in range, with tests.
- Play-on-clone deep-copies the scene, and a test shows the authored scene survives runtime moves and deletes.
- Delete-undo restores both the parent and the direct children.
- The shadow pass restores render targets in a `finally` block.

### Structure and duplication

- **Atomic-write code exists in five copies**: `SceneFile.cs:55-67`, `SequenceFile.cs:35-46`, `WorldManifest.cs:110-121`, `EngineProjectFile.cs:105-116`, and `SequenceFrameExport.cs:249-260`. The copies have drifted apart. `SceneFile` uses `File.WriteAllText` rather than an exclusive `CreateNew` stream. The export manifest replaces its file with `File.Move(overwrite: true)` rather than `File.Replace`. Task 11 is tested only for validation failures, not for a write or replace that fails partway. **Path-escape checks exist in four more**: `EngineProjectFile.cs:45-51`, `WorldManifest.cs:163-186`, and `EngineProjectPackage.cs:199-205,230-236`. Extract them into `SafeFile.WriteAtomic(path, Action<Stream>)` and `ContentPath.ResolveInside(root, relative)`, and test both once. Add `stream.Flush(flushToDisk: true)` before `File.Replace`. Without it, a power loss right after the replace can leave an empty file on NTFS. Player saves (task 90) will depend on this.
- **The template carries engine code.** About 120 lines of `CharacterAsset`/`CharacterInstance` (GPU buffers, textures, clip selection, bounds) duplicate `PreviewResources` in CharacterStudio (`CharacterStudioGame.cs:1507`). Every generated project will get a private fork that won't pick up engine fixes. Move a `SceneAssetSet` (static and skinned GLB, per-asset GPU resources, per-instance playback) into `Ember.Engine/Assets` and have both consumers use it. The template's `--smoke-controls` harness (`Program.cs:121-168,248-265`) also belongs in a test harness, not in user starter code.
- **`CharacterStudioGame.cs` is 1,646 lines.** Split the play session, the sequence/export flow, and the asset preview into their own classes before Stage 14 adds more panels.
- **CPU-only code can't be tested off Windows.** `Scene`, `World`, `Project`, `Sequence` persistence, and most of `Assets` don't need a graphics device, but everything is `net9.0-windows` + WinForms. There is also no CI. Move the CPU-only folders into an `Ember.Core` library targeting `net9.0`, and run `dotnet test` for it on every push (a Windows GitHub Actions runner for the full solution, Linux for the core). That keeps "143 passed" verifiable by someone other than the implementer.

### Process feedback

1. **`ff85c39` batches about 12 roadmap rows** (69a–78: project files, packaging, publishing, template, tutorial, world manifest, `RpgSlice`, lifecycle, preparation, ring). Rule 4 asks for one behavior and at most three source files per row. Earlier commits (`458d65c` through `87ad894`) followed that well. The batch makes each row's acceptance hard to review or bisect, and it is how the threading issue above got through. Go back to one commit per row, with the row ID in the message.
2. **The roadmap now contains three reviews with contradictory totals** (65/143, 75/149, 89/154) and superseded instructions. For example, "start task 66" and "task 70 remains unchecked" are both stale. Keep one current review and one handoff in this file, and move dated history to `ENGINE_PROGRESS.md`.
3. **Some checked rows rest on narrower evidence than their acceptance text.** Tasks 77 and 78 are CPU fixtures with disposable probes. That is fine, but the Stage 9 gate (3×3 walk, slow and failed loads, frame-time spikes) should be the first integrated proof, not deferred until after task 84. Record gate status explicitly as Pending, Pass, or Blocked.
4. Two author identities appear in history (`pixel-image-admin`, `justforfreecopilot`). If both are you, set a consistent `user.email` so the history is attributable.

### Recommended order before continuing

1. **Resolved 24 September 2026:** the owner-thread completion path, `Discard`/`Cancel`, and generation stamping are implemented; see the follow-up in `ENGINE_PROGRESS.md`. These were prerequisites for tasks 79–81.
2. **Resolved 24 September 2026:** world manifest v2 owns cell width, indexes cells by ID and exterior coordinate, migrates v1, and supports distance-ordered entered/left ring updates with hysteresis. Saving no longer requires scenes to exist; duplicate scene paths are rejected.
3. **Resolved 24 September 2026:** task 79 uses a cost-based budget and was verified in `RpgSlice` on a 3×3 grid with one delayed cell.
4. Extract the shared atomic-write and content-path helpers before task 90 (world save) adds a sixth copy.
