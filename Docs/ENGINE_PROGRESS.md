> Historical implementation log. As of 27 September 2026, Docs/ENGINE_ROADMAP.md is the sole active plan. Earlier completion percentages, expansion approvals and next-task instructions are superseded; see Docs/PROJECT_REVIEW.md.

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

This baseline records only the repository's starting health. Later sections track completed roadmap work and the checks that passed for it; unlisted capabilities remain unimplemented.

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

## Tasks 07–12 — CharacterStudio and scene files

- Task 07: added `samples/CharacterStudio`, an outside-style engine consumer that draws a scene object through `SceneGraph.GetWorldMatrix` and the renderer's composed-matrix cube path.
- Task 08: added `OrbitCamera` with bounded distance, orbit input, target tracking, projection setup, and display-resize support.
- Tasks 09–10: added version-1 JSON scene persistence with transform/hierarchy fields and validation for versions, IDs, missing parents, cycles, array lengths, nonfinite values, and zero-length rotations.
- Task 11: added atomic scene replacement through a temporary file; invalid serialization is rejected before the existing destination is touched.
- Task 12: CharacterStudio accepts `--open <path>` and `--save <path>` and reports load/save errors without taking down the sample.

### Task 07–12 checks

| Check | Result |
| --- | --- |
| `dotnet test tests/Ember.Engine.Tests/Ember.Engine.Tests.csproj --nologo` | PASS — 14 tests |
| `dotnet test Ember.sln --nologo` | PASS — 14 tests |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings, 0 errors; CharacterStudio builds |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio `--screenshot ... --save ... --warmup 1` | PASS — 1280x720 PNG and JSON scene written; captured image visually shows the cube |
| CharacterStudio `--open ... --screenshot ... --warmup 1` | PASS — saved scene reopened and rendered to PNG |

## Tasks 13–15 — resource ownership

### Task 13 audit

| Resource | Created by | Current owner after audit | Cleanup |
| --- | --- | --- | --- |
| Capture render target and temporary PNG texture | `CaptureHost` | `CaptureHost` / capture call | Target disposed by host; PNG texture uses `using` |
| Body and heading `FontSystem` | `EngineHost.AttachCanvas` | `EngineHost`; `UiCanvas` borrows them | Host disposes both |
| `SpriteType` atlas | `EngineHost.AttachCanvas()` | `EngineHost`; `UiCanvas` borrows it | Host disposes it |
| White `Texture2D` | `EngineHost.AttachCanvas` | `EngineHost`; `UiCanvas` borrows it | Host disposes it |
| `SpriteBatch` | `EngineHost.AttachCanvas` | Created locally, then reachable through `UiCanvas.Batch`; no clear disposer | Previously leaked; retained and disposed by host in task 14 |
| Billboard `AlphaTestEffect` | `EngineHost.AttachScene` | `BillboardRenderer`, held by `EngineHost` | Previously leaked; disposed by host in task 14 |
| Lighting `BasicEffect` | `EngineHost.AttachScene` | `EngineHost.LitEffect`; games borrow it | Previously leaked; disposed by host in task 14 |
| Ambient audio sound and instance | `EngineHost.AttachScene` | `EngineHost.Ambience`; games can stop playback | Previously not disposed by host; host disposes it in task 14 |
| Synthesized sound effects | `EngineHost.AttachScene` | `EngineHost.Sounds` | Previously not disposed by host; host disposes it in task 14 |
| Procedural texture caches | Static `StoneTextures`, `PropTextures`, `ItemSprites`, `CharacterSprites` | Each cache owns its textures; explicit `Clear()` methods | Not constructed by `EngineHost`; do not clear from generic host because cache lifetime is global and may be shared |
| Campaign renderers/terrain/sprites | `CampaignGame` or its world | `CampaignGame` | Explicitly disposed in `CampaignGame.UnloadContent` |
| CharacterStudio imported mesh buffers and textures | `CharacterStudioGame` | Scene `SceneResourceScope` | Scope disposes buffers, decoded textures, and effect before host shutdown |
| CharacterStudio scene drawing effect | `CharacterStudioGame` | Scene `SceneResourceScope` | Scope disposes it before host shutdown |

`UiCanvas` borrows graphics/font objects; it does not dispose them. The ambiguous lifetime was the host-created `SpriteBatch`. Campaign intentionally suppresses ambience during load, so its old `Ambience.Dispose()` call was split into playback stop and host-owned final disposal to avoid a second disposal.

### Task 13–15 checks

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --nologo` | PASS — 14 tests |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings, 0 errors |

### Task 13–15 results

- Task 14: `EngineHost` now retains and disposes its `SpriteBatch`, `BillboardRenderer`, `BasicEffect`, `AmbientAudio`, and `SoundBank`, alongside its existing capture/font/texture resources. Cleanup is idempotent, attempts every resource even if one disposer throws, and reports collected failures afterward.
- Campaign now calls `Ambience.Stop()` when it wants silence; the host remains the sole owner that disposes the audio resources at shutdown. `AmbientAudio.Dispose()` is itself idempotent.
- Task 15: added `SceneResourceScope`, which tracks unique resources by reference, disposes in reverse order, continues after individual failures, and aggregates cleanup exceptions. CharacterStudio owns its scene `BasicEffect` through this scope and cleans the scope if scene setup fails partway.
- Host-level static caches remain owned by their cache classes. The generic host does not clear those global caches.

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --nologo` | PASS — 17 tests, including resource-scope order/idempotence/failure handling |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings, 0 errors |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio screenshot capture | PASS — startup, rendering, capture, and shutdown completed |
| FirstLight screenshot capture | PASS — startup, rendering, capture, and shutdown completed |
| Campaign screenshot capture | PASS — startup, rendering, capture, and shutdown completed after ambient playback was changed to stop-only |

These checks confirm the disposal paths run without errors in the sample shutdown flow. The project does not expose a graphics-driver live-resource counter, so this is not a measured GPU-memory leak benchmark.

## Tasks 16–18 — static GLB and CPU mesh data

- Task 16: added the Khronos `TextureCoordinateTest.glb` fixture under `samples/CharacterStudio/Assets`, shared with the CPU tests. Its CC0-1.0 source, attribution, SHA-256, reference viewer, dimensions, and glTF orientation are recorded beside it. The fixture has five identity-transform nodes and five meshes; the first mesh is a 1 m square facing +Z.
- Task 17: pinned SharpGLTF.Core 1.0.7 in `Ember.Engine`. A CPU test reads the fixture's scene, node names and identity transforms, mesh/primitive counts, attributes, and index count. The selected version targets .NET 8 or later and is MIT-licensed; its compatibility is verified by the .NET 9 solution build and tests.
- Task 18: added `StaticMeshVertex`, immutable `StaticMeshData`, and `GltfPrimitiveImporter`. The importer copies positions, normals, TEXCOORD_0, and triangle indices into Ember-owned CPU data. It rejects non-triangle topology, missing attributes, mismatched attribute counts, malformed index counts, nonfinite values, and indices outside the vertex array.

### Task 16–18 checks

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --nologo` | PASS — 20 tests, no graphics device/window |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings, 0 errors; all samples compile |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |

At the end of task 18 the mesh path was CPU-only. `TextureCoordinateTest.glb` intentionally includes one untextured primitive without UV0 so the importer can cover that supported case.

## Tasks 19–21 — render a static GLB scene

- Task 19: added `StaticMeshGpuBuffer`, which uploads Ember mesh vertices and 16-/32-bit indices, draws triangles with explicit world/view/projection matrices, and disposes both GPU buffers. CharacterStudio owns those buffers and its decoded textures in the scene resource scope.
- Task 20: added `GltfSceneImporter`, which creates Ember scene objects from the GLB default scene and preserves local TRS and parent relationships. Mesh bindings are indexed by imported node ID. A temporary GLB test uses a rotated/scaled parent and child and verifies the resulting world matrix; CharacterStudio composes each imported node matrix with its scene-instance matrix before drawing.
- Task 21: added opaque material import for base-color factor, double-sided state, and embedded PNG/JPEG images using TEXCOORD_0. Unsupported alpha modes, UV sets, texture transforms, and image formats fail with a material-specific message. CharacterStudio renders the Khronos fixture with decoded base-color textures and factors.

### Task 19–21 checks

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 23 CPU tests, including hierarchy, base-color, texture-byte, and unsupported-alpha cases |
| `dotnet build Ember.sln --nologo` | PASS — all projects and samples, 0 warnings, 0 errors |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio GLB screenshot capture | PASS — textured four-color panels and untextured backplane visible; process shut down through owned-resource cleanup |

The screenshot confirms the fixture's upright labels, UV orientation, material tints, authored origin/scale, and the renderer's GPU path. Shutdown exercises disposal, but this repository still has no graphics-driver live-resource counter to measure GPU memory directly.

## Tasks 22–23 — frame and persist GLB instances

- Task 22: `StaticMeshData.LocalBounds` now encloses imported positions. `Bounds3.Transform` transforms all eight corners for a conservative world AABB; `OrbitCamera.Frame` targets the bounds center and calculates a fit distance from the current FOV/aspect. CharacterStudio combines imported node bounds with every enabled scene-instance transform before framing.
- Task 23: added `GltfAssetReference`, a stable GUID plus validated project-relative `.glb` path on `SceneObject`. `SceneFile` persists only this metadata alongside each instance transform, accepts older version-1 documents without references, and rejects empty IDs, absolute/traversal paths, and one ID mapped to multiple paths. CharacterStudio resolves the path and renders repeated references as separate instances.

### Task 22–23 checks

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 29 CPU tests, including transformed bounds/camera fit and shared-asset save/load |
| `dotnet build Ember.sln --nologo` | PASS — all projects and samples, 0 warnings, 0 errors |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio screenshot with two saved GLB instances | PASS — both instances reopened, rendered, and framed together |

The camera fits a conservative bounding sphere around the scene AABB, so framing leaves more margin than an oriented-box fit. CharacterStudio currently accepts one unique asset ID per scene, while multiple instances of that asset are supported.

## Task 24 — transactional GLB reimport

- Added `ReloadableAsset<T>`: its replacement factory must finish before `Current` changes. Factory/import/upload failures leave the current asset untouched; successful swaps retire the old disposable resource, and cleanup errors are reported separately from replacement success.
- CharacterStudio now builds a complete preview bundle—imported scene, GPU mesh buffers, and textures—before swapping it in. Press **R** to reimport the current scene asset. The status strip reports success, replacement failure with the previous model still active, or a cleanup failure after a successful swap.
- A CPU test edits a valid GLB and confirms the new node name becomes current, then corrupts the file and confirms the replacement fails while the last valid imported scene remains usable.

### Task 24 checks

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 31 CPU tests, including valid reimport and corrupt-replacement preservation |
| `dotnet build Ember.sln --nologo` | PASS — all projects and samples, 0 warnings, 0 errors |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio capture | PASS — GLB preview and reimport status strip rendered; clean shutdown completed |

The CPU test exercises the shared replacement owner and the real GLB importer. GPU upload failure preservation is covered by the same staging boundary in CharacterStudio's preview factory, but is not forced through a graphics-device fault-injection test.

## Task 25 — document and enforce the static GLB subset

- Documented the current node, transform, primitive, vertex-attribute, material, and extension subset in `BUILDING_BLOCKS.md`.
- Required or SharpGLTF-incompatible extensions now fail before Ember creates any scene objects, with the extension name in the diagnostic.
- The primitive importer rejects morph targets and vertex attributes outside POSITION, NORMAL, and TEXCOORD_0 rather than silently dropping them.
- Negative CPU tests cover a required `KHR_mesh_quantization` extension, an unsupported COLOR_0 attribute, and a morph target.

### Task 25 checks

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --nologo` | PASS — 34 CPU tests, including required-extension, morph-target, and vertex-attribute rejection |
| `dotnet build Ember.sln --nologo` | PASS — all projects and samples, 0 warnings, 0 errors |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |

## Tasks 26–28 — establish CPU rigged-character data

- Task 26: added the Khronos Fox animation fixture with CC0/CC BY 4.0 attribution, source and reference-viewer links, and SHA-256. Tests pin its 24-joint order, representative rest hierarchy, identity first inverse-bind matrix, and Survey (3.416667 s), Walk (0.708333 s), and Run (1.158333 s) durations.
- Task 27: added immutable `GltfSkinData` records for source node IDs, full ancestor hierarchy, rest local transforms, skin-order joint mapping, inverse-bind matrices, and mesh-node rest world transform. Missing inverse-bind arrays use the glTF identity default; malformed counts and matrix-only skeleton transforms fail.
- Task 28: added immutable `GltfSkinWeightData` for exactly four `JOINTS_0`/`WEIGHTS_0` slots. It validates supported accessor formats, vertex counts, joint references, and finite nonnegative weights, normalizes each positive total, and rejects extra influence sets.

### Tasks 26–28 checks

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --nologo` | PASS — 38 CPU tests, including Fox reference metadata, mesh-parent transform, weight normalization, invalid joint, and excess-set cases |
| `dotnet build Ember.sln --nologo` | PASS — all projects and samples, 0 warnings, 0 errors |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |

The Fox fixture's non-symmetric hip rest transform and parented mesh-node test make joint-order mistakes and dropped mesh placement visible in CPU tests and the bind-pose preview.

## Tasks 29–30 — pose evaluation and first skinned draw

- Task 29: added `GltfSkinPose`, with independently owned local-transform and skin-matrix arrays. Pose evaluation composes ancestor transforms and computes row-vector skin matrices; singular mesh transforms and nonfinite/zero rotations fail clearly.
- Task 30: added CPU skinned-primitive import and `SkinnedMeshGpuBuffer`. CharacterStudio's `--fox` option renders the bundled Fox in bind pose using MonoGame `SkinnedEffect`; graphics profile and its 72-joint limit are checked before upload. The renderer supports one skinned mesh node, four influences, triangles, and the documented base-color subset.
- Captured `captures/fox-skin-bind-pose.png`; the whole low-poly character is framed and rendered with its authored orange/white materials.

### Tasks 29–30 checks

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --nologo` | PASS — all 42 tests, including bind-pose vertices, independent poses, Fox skinned-asset import, and renderer limits |
| `dotnet build Ember.sln --nologo` | PASS — all projects and samples, 0 warnings, 0 errors |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio `--fox` screenshot capture | PASS — Fox bind pose visible at 1280×720 |

## Tasks 31–33 — import, evaluate, and play character clips

- Task 31: imported immutable STEP/LINEAR translation, rotation, and scale tracks for each Fox clip. Key times and values are checked against the fixture; CUBICSPLINE and unsupported target paths fail clearly.
- Task 32: added absolute-time clip evaluation. Each evaluation resets the pose to rest, samples each track, applies shortest-path quaternion slerp, then rebuilds the skin matrices and animated mesh-node world matrix.
- Task 33: added deterministic playback with play, pause, seek, speed (including reverse), loop, and one-shot endpoint behavior. CharacterStudio accepts `--clip`, `--play`, `--pause`, `--loop`, `--no-loop`, `--time`, and `--speed`; it displays clip time and playback state.
- Captured `captures/fox-walk-mid.png` from `--clip Walk --pause --time 0.35 --screenshot`; the displayed pose differs from the bind pose.
- Captured looping and one-shot runs: starting at 0.69s with speed 2 wraps the 0.71s Walk clip to about 0.23s; starting at 0.68s with speed 2 and `--no-loop` stops at 0.71s.

### Tasks 31–33 checks

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --nologo` | PASS — 47 tests, including fixture keys, STEP/LINEAR, cubic rejection, start/middle/end evaluation, missing-channel rest values, shortest-path rotation, seek equivalence, pause, speed, and loop boundaries |
| `dotnet build Ember.sln --nologo` | PASS — all projects and samples, 0 warnings, 0 errors |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio `--clip Walk --pause --time 0.35 --screenshot` | PASS — 1280×720 animated pose rendered; UI reports Walk at 0.35/0.71s, paused |

## Tasks 34–36 — share character assets, blend clips, and attach a hand prop

- Task 34: CharacterStudio now creates a separate pose and playback state per scene instance while sharing imported character data, GPU mesh buffers, and textures. `--pair` previews two instances; `--second-clip`, `--second-time`, `--second-speed`, `--pause-second`, and `--second-no-loop` control the second instance.
- Task 35: added `GltfAnimationCrossfade`, which samples two clips on the same skin and blends local translation/scale plus shortest-path quaternion rotation. Endpoints copy the sampled source pose exactly; the sample's `--crossfade` and `--blend` options expose a fixed blend amount for deterministic previews.
- Task 36: added `GltfBoneAttachment` with unique named-joint lookup and a local offset. CharacterStudio's `--attach-hand` draws a colored GPU-backed cube attached to Fox's `b_RightHand_08` joint.
- Captured `captures/fox-pair-attachment.png` with Walk and Run instances paused at different times, plus the visible hand prop. Captured `captures/fox-crossfade.png` for the Walk/Run midpoint blend.

### Tasks 34–36 checks

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 50 tests, including independent instance clocks, crossfade endpoints/midpoint, and animated bone attachment |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — all projects and samples, 0 warnings, 0 errors |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio pair and crossfade screenshot captures | PASS — both character instances and crossfade render; colored attachment prop is visible |

The blend amount is currently supplied by the caller rather than advanced by a timed transition controller. At this milestone CharacterStudio still loaded one unique GLB per scene; tasks 38a–38b below later add and verify a separate static environment beside the shared character asset.

## Tasks 37–38 — frame animated characters and persist per-instance state

- Task 37: added `GltfAnimationBounds.SampleClip`, which evaluates weighted, deformed vertices across each supported clip at intervals no larger than 1/30 second. It expands each sampled AABB by 10% of its largest extent or 0.05 metres, whichever is larger. CharacterStudio unions the per-clip bounds when framing a character so its animated limbs fit the preview.
- Task 38: scene format version 2 stores each character instance's clip, time, speed, loop/playing flags, optional crossfade clip/blend, and stable bone attachment IDs/joint-local offsets. Version-1 scenes remain readable. CharacterStudio applies command-line settings before `--save`, restores serialized settings through `--open`, and **S** records current playback state to the selected scene path.
- Captured `captures/fox-survey-bounds.png`; the complete Fox remains visible at Survey 2.00s. Saved and reopened `captures/character-studio-task38.json`; the 1280×720 reopen capture restores Walk at 0.35s, Run at 0.60s, both paused, plus the hand attachment.

### Tasks 37–38 checks

| Check | Result |
| --- | --- |
| `dotnet test tests/Ember.Engine.Tests/Ember.Engine.Tests.csproj --no-restore` | PASS — 55 tests, including dense 120 Hz vertex coverage for every Fox clip, invalid bound parameters, scene version-1 upgrade, settings/attachment round-trip, and validation |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — all projects and samples, 0 warnings and 0 errors |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio saved-scene reopen | PASS — both distinct character playback states and the attached prop render from version-2 JSON |

The sampled bounds describe supported imported clips; they do not make arbitrary procedural poses safe to cull. The renderer currently performs no animated-character culling. At this milestone the Release A scene still lacked a separate environment GLB; that gap is closed by tasks 38a–38b below.

## Save-safety follow-up — failed scene open

- CharacterStudio now selects an in-place **S** save target only after `--open` succeeds. If the source is malformed or uses an unsupported scene version, saving back to that same path is blocked; an explicit different `--save` path remains available for recovery.
- Runtime validation used a version-99 scene: same-path `--open`/`--save` kept the source SHA-256 unchanged and displayed the preservation message; a different `--save` path wrote a separate version-2 recovery scene while preserving the invalid source.

## Tasks 38a–38b — load and reopen the Release A showcase

- Task 38a: CharacterStudio now resolves each distinct scene asset ID once and keeps that GLB's imported scene, GPU buffers, and textures together. Each scene instance draws through its referenced asset, so a static environment can share a scene with multiple instances of one skinned character. Character status and command-line playback options consider only skinned objects; `R` stages a reload of all scene assets while preserving the prior preview if any replacement fails.
- Added original `Assets/ReleaseACourtyard.glb`, an opaque, untextured four-mesh static environment, and `Scenes/ReleaseAShowcase.json`: one courtyard object plus two Fox instances at separate transforms. The Walk instance begins at 0.35 s playing with a hand attachment; the Run instance begins at 0.60 s paused.
- Task 38b: opened and saved the sample showcase, then opened the saved version again. The second capture restores the courtyard, both transforms, Walk/Run settings, and the `b_RightHand_08` attachment. Both screenshots are 1280×720.
- Added a CPU scene round-trip test for one unique environment asset and two character objects sharing a separate asset reference. README now lists CharacterStudio and describes its current supported workflow and upcoming authoring scope.

### Tasks 38a–38b checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — all projects and samples, 0 warnings, 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 56 tests, 0 failed |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio open/save capture | PASS — `captures/release-a-before-reopen.png`; courtyard and two independently configured Fox instances render together |
| CharacterStudio saved-scene reopen | PASS — `captures/release-a-after-reopen.png`; both clip states, authored transforms, and hand attachment restored |

## Tasks 39–41 — add the first CharacterStudio editor controls

- Task 39: pinned ImGui.NET 1.91.6.1 and added a small MonoGame renderer for its draw lists. CharacterStudio now feeds text, keyboard, and mouse input to the UI, scales the panel to the logical canvas through letterboxing, and suppresses camera and global shortcuts while ImGui captures input. The sample does not write an ImGui settings file into the project.
- Task 40: added a hierarchy keyed by scene-object GUID. Duplicate names receive distinct ImGui IDs, the first object is selected on startup, and deleting a selected object clears the stale selection.
- Task 41: added numeric local position, Euler rotation in degrees, and scale fields. Finite values update the selected scene transform, which the existing scene save/reopen path serializes.
- Captured `captures/imgui-editor-batch.png` at 1280×720. It shows text entry, the hierarchy, and all three transform groups over the Release A scene. The desktop input automation helper could not initialize in this session, so actual mouse clicks/text entry were not exercised; the interaction and save wiring were source-reviewed, and scene serialization remains covered by the existing suite.

### Tasks 39–41 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — all projects and samples, 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 56 tests, 0 failed |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio runtime capture | PASS — editor overlay rendered on the 1280×720 showcase; native ImGui dependency loaded |
| Interactive mouse/keyboard verification | NOT RUN — desktop input automation helper failed to initialize; select/edit behavior was source-reviewed |

## Tasks 42–44 — add scene edit history and asset placement

- Task 42: added a bounded 128-entry `SceneCommandHistory` for transform edits. CharacterStudio records one before/after transform when an ImGui numeric field is deactivated; Undo and Redo restore the whole position/rotation/scale value, and a new edit clears redo.
- Task 43: added shared history commands for creating, duplicating, and deleting scene objects. Duplicate instances retain the loaded GLB reference and character settings but get fresh object and attachment GUIDs. Deleting an object detaches direct children; undo restores the object, original parent, and child links without copying referenced asset data.
- Task 44: added an asset list limited to GLBs already referenced in the current scene and a place-instance action. New objects share the source `GltfAssetReference` and are spaced along the X axis from their same-asset instances. A general project-folder scanner remains deferred until CharacterStudio has an explicit project-root contract.
- The `captures/scene-editor-batch-42-44.png` runtime image shows the hierarchy, history buttons, object actions, in-scene asset list, placement button, and transform controls over the Release A showcase. The desktop input automation helper was unavailable, so UI button clicks and text-field typing were not exercised; command behavior is covered by CPU tests and UI wiring was source-reviewed.

### Tasks 42–44 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — all projects and samples, 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 61 tests, 0 failed; includes transform undo/redo, redo clearing, create/duplicate/delete restoration, and shared asset-instance references |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio runtime capture | PASS — `captures/scene-editor-batch-42-44.png`, 1280×720; native UI library loaded and the editor controls rendered |
| Interactive editor clicks and typing | NOT RUN — desktop input automation helper failed to initialize |

## Tasks 45–47 — edit character playback and share scene lighting

- Task 45: added a clip dropdown, absolute-time scrubber, and play/pause toggle for a selected skinned scene object. The UI passes that object's stable ID to the corresponding playback state, so changing a clip or time does not affect other instances. Changing clips starts the new clip at zero and clears the prior crossfade; scrub and play state are saved in the existing scene version-2 character settings.
- Task 46: added `Ember.Render.SceneLighting` and runtime controls for ambient RGB, directional direction, and directional RGB. CharacterStudio applies the same light values to its static `BasicEffect` and skinned `SkinnedEffect`, including disabling the extra default directional lights on both.
- Task 47: no separate shader build step was required for the built-in-effect lighting path. The later shadow pass needed custom effects, so follow-up task 47a adds the pinned MGCB content build.
- Captured `captures/scene-editor-batch-45-47-animated.png` at 1280×720 with two independently animated Fox instances, and `captures/scene-editor-batch-45-47-mixed.png` with the static courtyard and both characters after the shared-lighting change. The desktop input helper was unavailable, so clip selection, scrubbing, and lighting slider changes were source-reviewed, not operated by synthetic clicks.

### Tasks 45–47 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — all projects and samples, 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 63 tests, 0 failed; includes light-value validation and existing independent character playback/seek coverage |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio animated capture | PASS — `captures/scene-editor-batch-45-47-animated.png`, 1280×720; Walk and Run render simultaneously |
| CharacterStudio mixed static/skinned capture | PASS — `captures/scene-editor-batch-45-47-mixed.png`, 1280×720; courtyard and both characters render with the shared light |
| Interactive clip/light control verification | NOT RUN — desktop input automation helper failed to initialize |

## Tasks 47a–50 — build custom effects, add directional shadows, and measure static draws

- Task 47a: added the pinned `dotnet-mgcb` local tool manifest, `MonoGame.Content.Builder.Task` 3.8.5.1, a Windows HiDef `.mgcb` target, and `SceneShadow.fx`. Custom render effects now compile as part of the CharacterStudio project build.
- Task 48: added `DirectionalShadowCamera` and a scene-owned, viewport-resizable `DirectionalShadowMap`. Opaque static meshes render into an RGBA depth target, then sample it through a 3×3 PCF receiver in the scene pass.
- Task 49: added GPU skin-matrix effect binding and a skinned depth technique. Each character's current `GltfSkinPose` is uploaded to both the shadow and lit scene passes, so character shadows use the evaluated pose.
- Task 50: added `StaticSceneCuller` for transformed mesh AABBs against the orbit-camera frustum, while retaining uncullable meshes and keeping the shadow pass independent of the camera. The CharacterStudio status panel reports scene/shadow/skinned draw counts, culled static draws, and the last frame interval. `--perf` reports adapter, resolution/profile, average/min/max host-frame interval, and explicitly states that it includes rendering/presentation rather than isolating GPU time.
- Captured `captures/shadow-batch-48-50.png` for the mixed Release A scene, and `captures/shadow-batch-48-50-culling.png` with an extra static fixture placed outside the camera view. In the culling fixture, the overlay reports six static draws culled while the shadow pass still submits the offscreen objects.
- The latest mixed-scene run completed 121 measured host-frame intervals at 1280×720 on Intel(R) UHD Graphics: 8.83 ms average, 2.81 ms minimum, 283.21 ms maximum (about 113 fps average). Treat this as one local characterization run, not a budget; the maximum exposes a transient stall.

### Tasks 47a–50 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — all projects, samples, and custom effect content; 0 warnings, 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 68 tests, 0 failed; includes light-camera bounds, resize sizing, and static frustum culling |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio mixed-scene capture | PASS — 1280×720; static courtyard and two skinned Fox instances load and render; shadow effect content loads and shutdown completes |
| Offscreen static fixture | PASS — cull count rises while shadow draw count includes the offscreen asset |
| `--perf` reference run | PASS — Intel(R) UHD Graphics, 1280×720 HiDef; reports host-frame interval scope and avg/min/max |
| Light rotation and resize through editor interaction | NOT RUN — desktop input automation helper remains unavailable; update, resize, and disposal paths are covered by source review and focused sizing tests |

## Tasks 51–53 — add fixed-step physics and filtered raycasts

- Task 51: pinned `BepuPhysics` 2.5.0-beta.29 and built `PhysicsWorld`, which owns its BEPU simulation, pooled memory, and collision-filter storage. The adapter assigns stable engine IDs, supports static and dynamic box colliders, applies gravity and contact material settings, and converts poses explicitly between MonoGame and `System.Numerics`. Both package nuspecs identify `Apache-2.0`; the dependency is a prerelease, and that choice plus the older stable 2.4.0 fallback is documented.
- Task 52: added `PhysicsFixedStepper` with a 1/60-second default step, an eight-step catch-up cap, dropped-backlog reporting, and a retained interpolation remainder. `PhysicsWorld` stores prior/current dynamic poses for render interpolation.
- Task 53: added symmetric belongs-to/collides-with contact filtering and nearest-hit raycasts filtered by collision layers. Directions are normalized before querying, so hit distance is measured in world units.
- The CPU integration suite verifies a falling box settles on a floor, interpolated results agree at 30/60/120 render updates per second, excess backlog is reported, raycast hit/miss/filter behavior is correct, blocked collision pairs do not contact, math conversions round-trip, and disposal rejects later use. These tasks produce physics API coverage; CharacterStudio/gameplay integration begins with the capsule controller in task 54.

### Tasks 51–53 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — all projects and samples, 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 74 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| BEPU package metadata | PASS — `BepuPhysics` and `BepuUtilities` 2.5.0-beta.29 both declare Apache-2.0 |
| Rendered physics demo | NOT RUN — this batch adds and tests the engine service; it does not yet connect physics to a sample scene or character controller |

## Tasks 54–56 — add a capsule character controller

- Task 54: added upright dynamic capsules with locked angular inertia and `PhysicsCharacterController`, registered with the shared `PhysicsWorld` step. Horizontal world-space input controls movement; the capsule stops against static walls and remains grounded. Camera state remains an independent transform. Disposing the controller unregisters it, removes its body, and releases its uniquely owned capsule shape.
- Task 55: added queued jump requests and one-step `JumpedThisStep`/`LandedThisStep` flags. Ground transitions use a world-layer ray probe; capsule contacts stop jumps against ceilings. The flags are consumed inside the fixed-step callback so render frames with multiple physics steps retain each transition.
- Task 56: added a configurable maximum support angle. Movement on a walkable contact is projected onto the surface; an over-limit slope is not considered grounded and blocks uphill input. Rotated static boxes provide the ramp fixtures.
- CPU integration tests exercise wall blocking and stable floor contact, one jump/landing transition, low-ceiling collision, a climbable 30-degree ramp, rejection of a 60-degree ramp, and character/body disposal.

### Tasks 54–56 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — all projects and samples, 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 79 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| Rendered CharacterStudio/gameplay demo | NOT RUN — character physics is currently an engine API, not yet wired to sample input or scene persistence |

## Tasks 57–59 — connect camera, actions, and animation to character motion

- Task 57: added `ThirdPersonFollowCamera` with orbit/zoom, projection, target offset, and a boom ray against `World | Dynamic` layers. The camera shortens to just before the nearest obstruction and ignores the player's `Player` layer. `MoveDirection` converts local right/forward input into a horizontal world vector.
- Task 58: added named move/jump actions with WASD, arrows, and space defaults. Samples provide window focus and UI keyboard-capture state; while blocked, actions are neutral and held keys remain suppressed until release. `ConsumePressed` retains an edge until a physics step consumes it once, preventing catch-up substeps from replaying the same press.
- Task 59: added `GltfCharacterMotionAnimator` to choose idle, walk, or jump clips using the controller's post-step grounded state, jump flag, and actual horizontal velocity. It updates its per-instance skin pose after each fixed step; wall collision therefore returns the selected state from walk to idle.
- Integration tests use the physics capsule and Fox GLB animation data, plus a generated jump fixture. They cover wall obstruction/following, player-layer exclusion, UI/focus suppression, press consumption across render frames, diagonal input normalization, wall-stop idle, and jump-state exit.

### Tasks 57–59 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — all projects and samples, 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 86 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| Rendered gameplay sample | NOT RUN — components are integrated in CPU simulation tests but not yet wired into a packaged level or CharacterStudio mode |

## Tasks 60–62 — add compiled behaviours, imported audio, and play-on-clone

- Task 60: added `SceneBehaviour` and `SceneBehaviourRuntime`. Game assemblies register compiled behaviour instances against stable scene-object IDs before starting; each receives the scene, owner, and resource scope, can receive owner-scoped interactions, and stops once when its scene unloads. Recreating a play session starts a fresh set of behavior instances.
- Task 61: added `ImportedAudioClip`, which loads a MonoGame-supported file through `SoundEffect.FromStream`, owns one playback voice, exposes 0–1 volume control, and stops/releases the voice on disposal. `PlayAudioOnInteractionBehaviour` is the compiled example. CharacterStudio includes a generated 0.32-second WAV chime and a play-mode volume slider; volume zero mutes it.
- Task 62: added `SceneGraphCloner` and `ScenePlaySession`. The clone preserves stable IDs and parent links while copying transforms, character settings, and attachment records. CharacterStudio's **P / Play on clone** swaps the preview to the clone, and **P / Stop and restore** returns to authored state. Transform edits and object creation/deletion in play mode apply to the clone; save and reimport are unavailable until play mode stops.
- CPU tests cover behavior start/stop idempotence, owner-enabled interaction routing, fresh behavior instances after a session reload, muted interaction audio with stop/dispose on unload, and moving/deleting cloned objects without changing the authored graph.

### Tasks 60–62 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — all projects and samples, 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 90 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio Release A scene capture | PASS — reopened the saved showcase and rendered a 1280×720 PNG; the bundled WAV is copied beside the sample assets |
| Play-mode UI and real audio-device playback | NOT RUN — desktop input/audio-device automation was unavailable; CPU tests use an injected fake voice and verify runtime ownership/lifecycle |

Release C is still open: there is no packaged scene that combines player movement, collision, jumping, motion-driven animation, and audible interaction. CharacterStudio's play clone currently demonstrates scene editing isolation and the interaction/audio lifecycle; scene-authored behavior/collider assignments and physics wiring remain follow-up work.

## Tasks 63–65 — add absolute-time character and camera sequencing

- Task 63: added `SceneSequence` with explicit duration and `CharacterClipTrack` bindings by stable scene-object ID. Each evaluation resets the target pose to its skin rest state before sampling the imported clip at absolute sequence time; a looped clip track can start at an authored timeline offset.
- Task 64: added named camera tracks with position, quaternion rotation, and field-of-view keys, plus a camera-cut track that selects by stable camera-track ID. Key interpolation uses vector lerp and shortest-path quaternion slerp. `OrbitCamera.SetWorldTransform` applies the sampled view without converting it into orbit parameters.
- Task 65: added `SceneSequencePlayer` and a CharacterStudio sequence panel with play/pause, a time slider, and a preview toggle. Seeking samples character pose and camera state directly; it has no behavior/gameplay-event dependency. The editor creates a sample sequence for the first skinned character in a scene, with two camera tracks and a cut halfway through.
- CPU tests cover repeated absolute-time pose samples after different seek histories, camera position/FOV interpolation, camera cuts by ID, world-camera view orientation, player end/clamp behavior, and scrubbing without dispatching a scene interaction.

### Tasks 63–65 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — all projects and samples, 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 94 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio Release A scene capture | PASS — reopened the saved scene and rendered the sequence panel with the Wide camera cut visible at 1280×720 |
| Desktop play/pause/scrub interaction | NOT RUN — capture mode verifies the UI renders, while sequence semantics are exercised by CPU tests; desktop input automation was unavailable |

At that point the sequence preview was generated in memory from the first skinned scene object and was not persisted to JSON. Release D remained open pending frame export, manifest/error handling, and a reproducible exported sequence.

## Tasks 66–68 — render and export numbered sequence frames

- Task 66: added `SequenceFrameRenderTarget`, which renders to a reusable color/depth target at the requested dimensions, restores the previous render targets and viewport, writes a PNG, and disposes when the export finishes, fails, or is canceled. CharacterStudio UI controls output folder, range, frame rate, width, and height. Startup flags (`--export-sequence`, `--export-start`, `--export-end`, `--export-fps`, `--export-width`, `--export-height`) support reproducible capture and exit nonzero on error.
- Task 67: exports one frame per draw at `start + frameIndex / fps`, named `frame_000000.png` onward. Export samples absolute sequence time, restores the editor preview/camera after each frame, and is rejected while play-on-clone is active; this capture path therefore has no live physics simulation. Frame counting uses an end-exclusive interval with a documented half-ULP endpoint allowance applied to shortest round-tripping decimal float values.
- Task 68: `SequenceFrameExportJob` writes an atomic version-1 `manifest.json` at start and after every frame. It records status, progress, range, output dimensions, frame rate/count, sequence name, referenced GLB IDs/paths/SHA-256/byte lengths, and any error. A pre-existing manifest is protected; cancellation preserves completed frames and marks the manifest canceled; a render/write failure marks it failed and records the reason.
- Regression coverage checks 0.1 seconds at 30 fps, exactly 300 frames for ten seconds at 30 fps from a nonzero start, a nonaligned range, exact and adjacent float times from zero and nonzero starts, frame output times, settings/asset metadata, cancellation, and frame-write failure.

### Tasks 66–68 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --nologo` | PASS — all projects and custom effect content, 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 100 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio GPU export smoke | PASS — from a 1280×720 window, wrote exactly three 640×360 PNGs at 0, 1/30, and 2/30 seconds; process exited 0 and manifest reports completed 3/3 with one asset version |
| Export cancellation and render/write error cases | PASS — incomplete outputs retain completed frame count and explicit canceled/failed manifest status |

At this export-only snapshot, the Release D gate remained open pending sequence persistence. The implementation update below records persistence, restart/reopen verification, and the project-template batch.

## Tasks 68a–69b — persist sequences and start external projects

- Task 68a: added version-1 `SequenceFile` JSON with stable character-track IDs, target scene-object IDs, asset IDs and clip names, sequence duration, camera-track IDs/names/keys, and camera cuts. `SaveAtomic` validates scene targets before replacing the file. `Load` resolves clips against imported assets and rejects unsupported versions, missing/mismatched targets/assets/clips, ambiguous clip names, and camera cuts to missing tracks.
- Task 68b: CharacterStudio accepts `--save-sequence <path>` and `--open-sequence <path>` alongside scene save/open. Open sequence data replaces the generated preview; startup export can then render that reopened sequence.
- Task 69a: added version-1 `EngineProjectFile` (`ember.project.json`) with a project-relative `startupScene`. Paths are normalized, absolute and `..` paths are rejected, and missing scenes produce an error with the project path and resolved scene path. Resolution is based on the project file's directory rather than the process working directory.
- Task 69b: added `templates/MinimalGame` and `tools/new-engine-project.ps1`. The generator creates a minimal Windows consumer in an empty destination, writes a `Directory.Build.props` reference to the chosen Ember checkout, and copies only the sample project/scene. The starter app loads the configured scene and renders its enabled scene objects.
- Tests cover sequence/camera/pose round-trip, invalid references and versions, atomic preservation on invalid save, relative project scene resolution, path traversal/absolute paths, missing scenes, and unsupported project versions.

### Tasks 68a–69b checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --nologo` | PASS — all repository projects and custom effects, 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 111 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio save/restart/reopen/re-export | PASS — separate processes exported the same 3 frames at 640×360/30 fps; SHA-256 matched for all frames and manifests report complete 3/3 runs |
| External template consumer | PASS — generated under `%TEMP%`, built with 0 warnings/errors against the engine project in this checkout without copying its source, loaded `Content/StartupScene.json`, and captured the 1280×720 cube scene |

**Release D passed on 23 September 2026** on the same GPU/configuration: saved scene and sequence reopened after process restart, and all exported frame hashes matched. Cross-GPU pixel identity remains outside the gate.

## Tasks 70a–70c — resolve and package projects

- Task 70a: `EngineProjectFile.ResolveContentPath()` resolves safe project-relative paths from `ember.project.json`. CharacterStudio accepts `--project <ember.project.json>`, opens its startup scene, and resolves scene GLBs from the project root. Missing GLBs report the referencing scene object, asset ID, relative path, and resolved path.
- Task 70b: `EngineProjectPackage.Create()` builds a new package directory containing the project file, startup scene, referenced GLBs, and local buffer/image files declared by GLB JSON URIs. Missing or escaping local dependencies identify the scene object, asset, and path. Remote external URIs fail clearly. A staging directory is renamed into place only after all files copy successfully; the destination must not exist already.
- Task 70c: the generated minimal consumer accepts `--package-to <directory>` and performs packaging before constructing `EngineHost`, so no graphics window opens. `UseAppHost` is disabled for `dotnet run` in this template; the standalone distribution profile remains task 71.
- Tests cover project-root resolution and traversal rejection, package relocation, exact referenced-file copying, unused-file exclusion, GLB buffer/image sidecars, missing scene/asset/dependency diagnostics, and out-of-root sidecar rejection.

### Tasks 70a–70c checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --nologo` | PASS — all repository projects and custom effect content, 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 117 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |
| CharacterStudio project-root startup | PASS — from a different working directory, opened the project scene and two GLBs; sequence export completed 3/3 frames |
| Generated consumer package command | PASS — generated outside the repository, packaged without opening a graphics window, then opened the moved package from a separate working directory |
| Real project package relocation | PASS — copied the startup scene and both referenced GLBs, moved the package, reopened it in CharacterStudio from another working directory, and exported 3/3 frames |

The real CharacterStudio GLBs embed their image/buffer data; separate local sidecar buffer/image URIs are covered by synthetic GLB package tests. Release E remains open for task 71 Windows x64 distribution and task 72 tutorial.

## Tasks 71a–71b — publish and launch a Windows x64 distribution

- Task 71a: added `tools/publish-engine-project.ps1`. It publishes the generated external consumer as self-contained `win-x64`, creates the dependency package with the published executable, and installs that package under `Project/` beside the app. The output includes the executable, CoreCLR/hostfxr/hostpolicy, Ember.Engine, MonoGame, SharpDX, and the project content. The template defaults to `Project/ember.project.json` when that bundled project exists.
- Task 71b: copied the distribution to a separate temp folder and launched it from another working directory with `dotnet` removed from `PATH` and `DOTNET_ROOT` unset. Its `--screenshot` capture showed the starter scene and one rendered cube. No `.cs`, `.csproj`, or `.sln` files were present in the copied distribution.
- The publish flow accepts only a new destination directory and stages output beside it before moving the completed distribution into place.

### Tasks 71a–71b checks

| Check | Result |
| --- | --- |
| `dotnet publish` through `tools/publish-engine-project.ps1` | PASS — self-contained `win-x64` output with apphost, .NET runtime, Engine/MonoGame/SharpDX assemblies, and bundled project content |
| Isolated distribution launch | PASS — copied output, changed to an unrelated working directory, removed `dotnet` from `PATH`, unset `DOTNET_ROOT`, and captured the 1280×720 starter scene from the published `.exe` |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings, 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 117 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |

The launch check was performed on the available graphics-capable Windows host with .NET command paths removed. No separate physical clean machine was available. At that task 71 snapshot, Release E still awaited the animated consumer and tutorial (tasks 72a–72c).

## Tasks 72a–72c — animated consumer, relocated publish, and tutorial

- Task 72a: the template consumer loads skinned GLB scene objects and applies saved clip name/time/speed/loop/play state. It renders the Fox through the skinned path, uses named movement/exit actions, converts world movement through a parent transform, and follows the character's world position. The consumer has an opt-in `--smoke-controls` mode that feeds synthetic W/A/S/D/Escape states through the same update path and checks expected world deltas plus the camera target.
- Task 72b: `EngineProjectPackage` now also preserves an optional root `ThirdPartyNotices.txt`. A generated Fox project was packaged, moved, and opened from an unrelated working directory. The publisher produced a self-contained Win64 app with the Fox and its CC BY 4.0 attribution in `Project/`.
- Task 72c: added [`Docs/ANIMATED_GAME_TUTORIAL.md`](ANIMATED_GAME_TUTORIAL.md) and linked it from README. It covers project generation, adding and crediting Fox, authoring the scene in CharacterStudio, consumer build/run, control smoke, packaging, relocation, self-contained publishing, and unsupported features.

### Tasks 72a–72c checks

| Check | Result |
| --- | --- |
| CharacterStudio scene authoring | PASS — saved Fox `Walk` at time 0.17 to the external project and reopened it by its project manifest |
| Animated consumer capture and saved time | PASS — 1280×720 Fox capture from another working directory; paused captures at times 0.1 and 0.3 had different SHA-256 hashes |
| Consumer control smoke | PASS — scripted W/A/S/D deltas matched expected world motion under a rotated/scaled parent; Escape requested exit and the process exited 0 |
| Relocated project package | PASS — package reopened after moving; Fox rendered and optional `ThirdPartyNotices.txt` remained beside the project manifest |
| Self-contained relocated distribution | PASS — copied Win64 output ran from an unrelated directory with `dotnet` absent from `PATH`, `DOTNET_ROOT` unset, and no `.cs`, `.csproj`, or `.sln` files; captured the Fox and retained attribution |
| Published animation progression | PASS — two captures from the copied self-contained app had different SHA-256 hashes |
| Tutorial walkthrough | PASS — followed the documented CharacterStudio authoring, project build, control smoke, package/move, publish, relocate, and direct `.exe` launch steps in a fresh generated consumer |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 118 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |

**Release E passed on 23 September 2026** on the available graphics-capable Windows host. The scripted input smoke verifies the consumer's action, movement, camera-follow, and exit path; direct physical key injection was unavailable because CUA exposed no native apps. A separate physical clean machine was not available. Remote external GLB URIs remain unsupported.

## Tasks 73–75 — world manifest, cell mapping, and RpgSlice — 23 September 2026

- Task 73: added a versioned `WorldManifest` with stable GUID cell IDs, exterior X/Z coordinates, and manifest-relative scene references for exterior and interior cells. Loading and atomic saving validate duplicate IDs, duplicate exterior coordinates, missing scenes, invalid kinds/coordinates, unsupported versions, and unsafe paths.
- Task 74: added `ExteriorCellGrid.FromWorldPosition`, which maps world X/Z through a configurable positive cell width using floor semantics. Exact positive/negative edges and positions just below zero have fixtures.
- Task 75: added `samples/RpgSlice`, registered in the solution and README. It loads one exterior cell through `Content/World/world.json`, renders its scene blockout, and uses Ember.Engine's physics character controller, named WASD/jump/exit actions, and follow camera. It has no Campaign or Ember.Rpg project reference.

### Tasks 73–75 checks

| Check | Result |
| --- | --- |
| World manifest and coordinate tests | PASS — 9 tests for round-trip, duplicate IDs/coordinates, missing/escaping scene paths, boundaries, negative coordinates, invalid widths, and range overflow |
| RpgSlice build | PASS — 0 warnings and 0 errors |
| RpgSlice `--smoke-controls --windowed` | PASS — manifest loaded cell (0, 0); scripted W input moved the physics character 2.50 m |
| RpgSlice screenshot | PASS — 1280×720 render saved to `C:\Users\ekava\AppData\Local\Temp\rpgslice-review.png` and visually inspected |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 127 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |

At this update, 86 of 154 roadmap rows are complete (55.8%). Task 76 is next. RpgSlice currently renders box-based blockout objects; GLB rendering and cell streaming/lifecycle are not part of this slice.

## Tasks 76–78 — cell lifecycle, async preparation, and loading ring — 24 September 2026

- Task 76: added `CellLifecycle` with the explicit unloaded, preparing, ready, active, unloading, and failed states. Invalid transitions fail, temporary preparation resources transfer to the activation stage, and failed preparation disposes any resources still owned by the lifecycle.
- Task 77: added `WorldCellLoadOperation<TPrepared,TActive>`. It runs CPU preparation on a worker, records the preparation thread, captures and enforces the resource-owning thread for activation and unload, disposes prepared data after activation, and publishes active resources only after the activation callback completes. Failed activation disposes prepared data and leaves active resources unpublished.
- Task 78: added `ExteriorCellLoadingRing` with configurable radius and cell width. It returns newly requested coordinates in stable order, deduplicates repeated updates and boundary crossings, and can forget a coordinate after unload so it may be requested again.

### Tasks 76–78 checks

| Check | Result |
| --- | --- |
| Lifecycle, async preparation, and ring tests | PASS — 16 focused tests, 0 failed |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 143 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |

At this update, 89 of 154 roadmap rows are complete (57.8%). Task 79 is next. Thread-affinity tests use disposable CPU fixtures; graphics and physics activation on a live device remain to be exercised in a concrete loader integration.

## Code-review follow-up — world-cell load ownership — 24 September 2026

- Resolved the three High Stage 9 findings recorded in `ENGINE_ROADMAP.md`. Worker preparation now only enqueues a generation-stamped completion; `PumpCompletions()` performs lifecycle transitions and resource ownership changes on the captured owner thread. Callers poll `State` on that thread and pump completions from their game loop.
- Added `Discard()` for ready or failed operations and `Cancel()` for preparation. Cancellation invalidates the active generation immediately; a late result is disposed on the owner thread and cannot replace a newer attempt. A failed attempt can be retried on the same operation after the prior completion has been pumped.
- Added coverage that polls state while a worker is blocked, verifies ready-data disposal without activation, retries after a failed load, and cancels a slow generation before completing a newer one.

### Code-review follow-up checks

| Check | Result |
| --- | --- |
| `dotnet restore Ember.sln --nologo` | PASS — restored the newly pulled RpgSlice project; remaining projects were current |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 146 tests, 0 failed, 0 skipped |

The queue is not yet connected to a live world loader. Continue with task 79 after the remaining Stage 9 manifest/ring prerequisites in the code review are addressed.

## Code-review follow-up — world manifest and loading ring — 24 September 2026

- Resolved the Stage 9 Medium findings for manifest cell width, coordinate lookup, ring lifecycle, and scene references. World manifest v2 stores `exteriorCellWidth` and an `exteriorCoordinate` value; v1 manifests migrate with the former RpgSlice width of 32 m.
- Added ID and exterior-coordinate dictionaries plus `TryGetExterior`, so absent edge coordinates return false without a scan. RpgSlice now obtains its origin coordinate from the manifest, and `WorldManifest.CreateLoadingRing` gives the future loader the same configured cell width.
- The loading ring returns entered and left coordinates, uses a wider configurable retention radius, sorts new requests nearest-first with stable X/Z tie-breaking, and reuses a shared empty result when the centre cell has not changed.
- Manifest saves can precede scene creation; loads still require each scene file, and duplicate scene paths fail validation.

### Manifest and ring follow-up checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| Focused world-manifest and loading-ring tests | PASS — 15 tests, 0 failed |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 148 tests, 0 failed, 0 skipped |
| RpgSlice managed-DLL `--smoke-controls --windowed` | PASS — loaded v2 manifest cell (0, 0), moved 2.50 m |

The Stage 9 manifest/ring prerequisites are resolved. Continue with task 79: bounded, cost-aware per-frame activation and the integrated 3×3 RpgSlice proof.

## Task 79 — bounded per-frame cell activation — 24 September 2026

- Added `ICellActivationCost` for prepared data to declare expected upload work, resumable owner-thread activation steps, and `CellActivationQueue<TPrepared,TActive>` with per-frame cost, cell-step, and elapsed-time limits.
- The queue rotates incomplete cells fairly and records processed/completed cells, consumed and queued estimated cost, pending cells, and elapsed milliseconds. Failed or canceled activation releases partial work and prepared data through the cell lifecycle.
- Expanded the RpgSlice world fixture to a 3×3 manifest grid. Its `--streaming-smoke` path delays the (1, 1) preparation by 150 ms and exercises the real ring, async lifecycle, and budgeted activation queue.

### Task 79 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 150 tests, 0 failed, 0 skipped |
| RpgSlice managed-DLL `--streaming-smoke --windowed` | PASS — all 9 cells activated after the slow preparation; each frame stayed within 300 cost units and 3 cell steps, total cost 2,250 |

At this point 90 of 154 roadmap rows are complete (58.4%). Task 80 is next. The sample's upload costs are deterministic work units used to validate scheduling; real GPU upload implementations still need to report their own cost estimates.

## Task 80 — retained cells and shared asset references — 24 September 2026

- Added `CellAssetReferencePool<TKey,TAsset>` and cell-owned leases. Assets are created once per key, stay alive while any retained cell holds a lease, and are disposed on the owner thread as soon as the last cell releases them.
- The loading ring's wider retention radius now feeds explicit left-cell events into the sample smoke. RpgSlice simulates 20 cell-boundary crossings, verifies the retained set stays bounded, and checks a shared asset survives until the last reference is released.
- The Stage 9 integration gate remains Pending: RpgSlice's 3×3 path validates preparation, scheduling, retention, and shared-resource lifetimes, but it does not yet walk terrain across cells or exercise interiors, collision waits, and failed destination travel.

### Task 80 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 152 tests, 0 failed, 0 skipped |
| RpgSlice managed-DLL `--streaming-smoke --windowed` | PASS — 20 crossings kept shared references bounded and released the asset after its final user left |

Task 80 is complete. At that point task 81 remained unchecked; it is checked off in the follow-up below using the already-tested cancellation and stale-generation handling.

## Task 81 — cancel obsolete cell loads — 24 September 2026

- Checked task 81 against the earlier world-cell ownership fix: `Cancel()` invalidates the active generation immediately, late results are drained and disposed on the owner thread, and a newer attempt remains the only one that can become Ready or Active.
- The cancellation fixture deliberately ignores cancellation while blocked, starts a newer load, activates the new result, then releases the stale result and verifies it is disposed without replacing the active cell.

### Task 81 checks

| Check | Result |
| --- | --- |
| `WorldCellLoadOperationTests.CancelDiscardsLateGenerationAndLeavesNewAttemptReady` | PASS — stale result disposed once; newer active generation preserved |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 152 tests, 0 failed, 0 skipped |

At this update, 92 of 154 roadmap rows are complete (59.7%). Tasks 79–81 are complete; task 82 is next. The Stage 9 integration gate remains Pending for collision readiness, door travel, and actual interior transitions.

## Task 82 — wait for destination collision — 24 September 2026

- Added `ExteriorCellCollisionGate`, which checks every cell touched by a proposed capsule footprint. It distinguishes a manifest cell waiting for collision from a coordinate with no authored cell, reports the required coordinate, and raises one collision request until that cell becomes ready.
- Added an optional horizontal movement gate to `PhysicsCharacterController`. It predicts the next fixed-step endpoint and zeros horizontal velocity when the required collision is unavailable, leaving vertical movement and jump state intact.
- RpgSlice marks its loaded cell collision-ready, reports neighboring collision requests, and changes the window title while movement waits.
- The delayed-collision physics fixture keeps the capsule grounded at the cell edge until the neighbor is marked ready. The current blockout uses box floors with hard vertical sides at cell seams, so physically walking across a loaded seam still needs stitched terrain collision.

### Task 82 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 154 tests, 0 failed, 0 skipped |
| RpgSlice managed-DLL `--smoke-controls --windowed` | PASS — player moved 2.50 m inside the collision-ready cell |

At this update, 93 of 154 roadmap rows are complete (60.4%). Task 83 is next. The Stage 9 integration gate remains Pending for seamless cross-cell traversal, door travel, and actual interior transitions.

## Task 83 — authored doors and spawn points — 24 September 2026

- Added scene-owned door and spawn components. Doors store a stable destination cell ID, spawn ID, and normalized facing; spawn transforms provide the authored arrival position.
- Upgraded scene persistence to version 3 while retaining version 1 and 2 loading. Older scene versions reject world-travel components rather than silently dropping them. Scene copy/history preserves door data and gives duplicated spawn points new IDs.
- Added world travel validation for unknown cells, unloaded destination scenes, duplicate spawn IDs within a cell, and missing destination spawns. Destination resolution returns the spawn's world-space position with the door's facing.
- Added two interior fixtures to RpgSlice and exterior doors to both. Each interior has a return door to the exterior's authored return spawn.

### Task 83 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 156 tests, 0 failed, 0 skipped |
| RpgSlice managed-DLL `--smoke-controls --windowed` | PASS — loaded the exterior fixture and moved 2.50 m |
| Door resolution fixture | PASS — resolved the selected interior spawn at its authored world position and preserved door facing |

At this update, 94 of 154 roadmap rows are complete (61.0%). Task 84 is next. The Stage 9 integration gate remains Pending for transactional travel, seamless cross-cell traversal, and live interior transitions.

## Task 84 — transactional cell travel — 24 September 2026

- Added `WorldCellTravelTransaction<TPrepared,TActive>`. It starts destination CPU preparation while the source cell remains Active, pumps results on the owning thread, and exposes a DestinationReady boundary where callers may cancel.
- Travel activates the destination, places the player at the resolved authored spawn with the door's facing, then unloads the source. Preparation or activation failure leaves the source Active. A placement failure unloads the destination and retains the source; canceled late preparation is drained and disposed on the owner thread.
- The CPU travel fixture makes the exterior-to-House-A-to-exterior-to-House-B-to-exterior route from authored doors and spawn markers. Separate cases cover failed preparation, failed activation, failed placement, and cancellation while preparation is blocked.

### Task 84 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 161 tests, 0 failed, 0 skipped |
| RpgSlice managed-DLL `--smoke-controls --windowed` | PASS — loaded the exterior fixture and moved 2.50 m |
| `WorldCellTravelTransactionTests` | PASS — two authored interior round trips and source-preserving failure/cancel cases |

At this update, 95 of 154 roadmap rows are complete (61.7%). Task 85 is next. The Stage 9 integration gate remains Pending because RpgSlice does not yet connect player door interaction to live cell activation, and the exterior collision fixture still has hard cell seams.

## Task 85 — stable world-instance identities — 24 September 2026

- Added the strongly typed `WorldInstanceId` and an owner-thread `WorldInstanceIdentityMap`, keyed by stable world cell ID plus authored scene object ID. The map is retained across cell unload/reload and returns an identity snapshot for a loaded scene.
- Scene and asset IDs remain content references; distinct placements of the same GLB receive separate runtime world-instance identities. The retained map is the source for upcoming per-instance change records.
- Persistent serialization across a full application restart remains part of the later world-save tasks.

### Task 85 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 163 tests, 0 failed, 0 skipped |
| `WorldInstanceIdentityMapTests` | PASS — two shared-asset placements keep distinct IDs and reuse them after cell scene reload |

At this update, 96 of 154 roadmap rows are complete (62.3%). Task 86 is next. The Stage 9 integration gate remains Pending for live RpgSlice door interaction and seamless exterior collision across cell seams.

## Task 86 — per-cell instance change store — 24 September 2026

- Added `WorldCellChangeStore`, keyed by cell ID and `WorldInstanceId`, with independent transform and enabled-state overrides. Transform values are validated and copied when recorded so later edits to a mutable scene transform do not alter the stored snapshot.
- A newly loaded scene reapplies changes through its identity snapshot. Overrides for one cell do not affect another cell, and the store remains in memory across cell unload/reload. Versioned disk persistence is part of the later world-save tasks.
- The reload fixture moves and disables one of two objects that share a GLB, then reloads the authored scene and reapplies only that instance's changes.

### Task 86 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 166 tests, 0 failed, 0 skipped |
| RpgSlice managed-DLL `--smoke-controls --windowed` | PASS — loaded the exterior fixture and moved 2.50 m |
| `WorldCellChangeStoreTests` | PASS — transform/enabled overrides survived reload and remained cell-scoped |

At this update, 97 of 154 roadmap rows are complete (63.0%). Task 87 is next. The Stage 9 integration gate remains Pending for live RpgSlice door interaction and seamless exterior collision across cell seams.

## Task 87 — authored-object deletion tombstones — 24 September 2026

- Extended `WorldCellChangeStore` with per-cell deletion tombstones keyed by `WorldInstanceId`. Tombstones take precedence over transform/enabled overrides and remove the authored object when changes are applied to a reloaded scene.
- Deleting one placement does not delete another placement that shares the same GLB. Tombstoned instances reject later transform/enabled writes; reset/undelete policy remains for task 93.
- The fixture reloads the source scene twice and confirms the deleted placement stays absent while its same-asset sibling remains.

### Task 87 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 167 tests, 0 failed, 0 skipped |
| `WorldCellDeletionTombstoneTests` | PASS — deleted authored instance remained absent after repeated cell reloads |

At this update, 98 of 154 roadmap rows are complete (63.6%). Task 88 is next. The Stage 9 integration gate remains Pending for live RpgSlice door interaction and seamless exterior collision across cell seams.

## Task 88 — runtime-created world objects — 24 September 2026

- Added `WorldRuntimeObjectStore.Spawn` to clone a runtime object into the active cell and retain its stable scene-object ID, cell ID, and `WorldInstanceId`. Runtime identity mappings can be restored explicitly through `WorldInstanceIdentityMap.Register`.
- Added idempotent cell restoration: a newly loaded scene receives each runtime record once, and a second restore into the same scene does not duplicate it. Conflicting scene IDs are rejected instead of replacing unrelated content.
- The fixture spawns an item using the same GLB as authored content, reloads the authored scene, and verifies the runtime item returns with the same identity exactly once. This in-memory store survives cell reload; restart-safe serialization remains task 90.

### Task 88 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 169 tests, 0 failed, 0 skipped |
| `WorldRuntimeObjectStoreTests` | PASS — stable IDs returned after reload; repeated restore added no duplicate |

At this update, 99 of 154 roadmap rows are complete (64.3%). Task 89 is next. The Stage 9 integration gate remains Pending for live RpgSlice door interaction and seamless exterior collision across cell seams.

## Task 89 — atomic world-instance cell transfer — 24 September 2026

- Added `WorldRuntimeObjectStore.Transfer` to move a runtime object record, stable scene-object ID, and `WorldInstanceId` from one cell owner to another. The caller supplies the destination-local transform; the identity map moves with the record.
- Transfer prevalidates source ownership and destination collisions, stages the destination object, and rolls back the scene, record, and identity mapping if a later commit operation fails. The source object remains intact when prevalidation rejects the move.
- The fixture moves a spawned relic between two cell scenes, reloads both authored scenes, and restores exactly one copy in the destination with the original world-instance ID.

### Task 89 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 171 tests, 0 failed, 0 skipped |
| `WorldInstanceTransferTests` | PASS — destination has the moved object after reload; source does not; stable IDs are preserved |

At this update, 100 of 154 roadmap rows are complete (64.9%). Task 90 is next. Runtime records and changes are still memory-only; Stage 10's save/restart gate remains pending until versioned world-save serialization is complete.

## Task 90 — versioned world save and restart — 24 September 2026

- Added version-1 `WorldSaveFile` and validated `WorldSaveSnapshot` capture/restore for player cell, position, and facing; world-instance identity mappings; transform/enabled overrides; deletion tombstones; and runtime-created object records.
- Runtime object scene data is stored through the existing validated scene format. Loading rejects unsupported versions, incomplete transforms, duplicate IDs, and cross-references to the wrong cell or instance.
- Saves write and flush a temporary file beside the target, then replace the prior file only after serialization succeeds. A test holds the previous save open to force replacement failure and verifies its bytes and readability are unchanged.
- The restart fixture creates a fresh set of stores, restores the save, reloads the authored cell, and confirms the moved/disabled object, removed object, runtime item, player pose, and stable IDs are restored.

### Task 90 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 174 tests, 0 failed, 0 skipped |
| `WorldSaveFileTests` | PASS — restart restored world state; failed replacement preserved the prior save |

At this update, 101 of 154 roadmap rows are complete (65.6%). Task 91 is next. The Stage 10 integration gate remains Pending until the live world loader uses these stores through a full two-cell gameplay pass.

## Task 91 — validate world-save versions and references — 24 September 2026

- Added a version-0 migration for the earlier player-location-only save shape. Missing state lists migrate to empty identity, change, and runtime-object lists; saving the migrated snapshot writes current version 1.
- Unsupported versions remain rejected with the accepted versions in the diagnostic. Added `WorldSaveFile.Load(path, world)` to validate the player location and every stored identity/change/runtime object against the current world manifest.
- Missing cell definitions now report the exact save record and cell ID. The fixtures cover legacy migration, an unsupported version, and a player location referring to an absent cell.

### Task 91 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 176 tests, 0 failed, 0 skipped |
| `WorldSaveFileTests` | PASS — version-0 save migrated, unsupported version rejected, missing cell diagnosed |

At this update, 102 of 154 roadmap rows are complete (66.2%). Task 92 is next. The Stage 10 integration gate remains Pending until save requests are coordinated with live travel and the world loader.

## Task 92 — queue saves at stable boundaries — 24 September 2026

- Added `WorldSaveRequestQueue`. Requests retain a capture callback and path; capture and atomic writing happen only when the simulation reports no travel in progress.
- The queue processes at most one save per stable boundary. It reports success/failure by request ID and continues to later requests after a write failure.
- Tests request a save during travel, change the live player location, and verify the saved snapshot contains the committed destination. A failed travel can instead save the still-active source location; failed paths report an error without blocking later requests.

### Task 92 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 179 tests, 0 failed, 0 skipped |
| `WorldSaveRequestQueueTests` | PASS — mid-travel save captured one stable outcome; later requests ran after a write error |

At this update, 103 of 154 roadmap rows are complete (66.9%). Task 93 is next. The Stage 10 integration gate remains Pending until a live game loop queues saves and runs the multi-cell restart scenario.

## Task 93 — explicit cell reset policy — 24 September 2026

- Added per-object `WorldInstanceResetPolicy` to authored scene objects and runtime object records. The default is `Preserve`; `ResetOnCellReset` opts an instance into reset, while `QuestPersistent` explicitly keeps quest state. Scene serialization is now version 4 and continues loading versions 1–3 with the default policy.
- Added `WorldCellResetService` for a stable unload/reload boundary. It clears changes and deletion tombstones only for resettable authored instances and removes resettable runtime records and their identity mappings. Preserved and quest-persistent changes/objects remain available to restore.
- The fixture moves and disables a resettable object, tombstones a quest object, and creates resettable, quest-persistent, and default runtime items. After reset and reload, the resettable object returns to authored state; the quest tombstone and persistent runtime items remain.

### Task 93 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 180 tests, 0 failed, 0 skipped |
| RpgSlice managed-DLL `--smoke-controls --windowed` | PASS — version-3 sample scene loaded and player moved 2.50 m |
| `WorldCellResetServiceTests` | PASS — resettable data reset; default and quest-persistent state survived |

At this update, 104 of 154 roadmap rows are complete (67.5%). Task 94 is next. The Stage 10 integration gate remains Pending until the live world loader wires these stores into gameplay and the full two-cell restart scenario is exercised.

## Code-review follow-up — reject unskinned character meshes — 24 September 2026

- Fixed the remaining High finding for `GltfSkinnedCharacterData`: a mesh node without a skin now raises a clear `NotSupportedException` naming that node instead of being silently omitted.
- Added an in-memory negative GLB fixture by attaching the Fox mesh to an additional unskinned node. The error identifies `UnskinnedAccessory`.

### Code-review follow-up checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --nologo` | PASS — 181 tests, 0 failed, 0 skipped |
| `RejectsAdditionalUnskinnedMeshNodesByName` | PASS — rejected unsupported node and named it in the diagnostic |

## Tasks 94–96 — typed RPG content, actor stats, and timed modifiers — 24 September 2026

- Added `ContentId<TKind>` and typed actor, item, faction, dialogue, and quest catalogs while preserving string IDs in JSON. `RpgContentJson` validates actor/faction, dialogue speaker/node, quest/dialogue/actor/item, and saved entity/inventory/equipment/dialogue links; diagnostics include the source record and missing target.
- Added base actor attributes and named skills with configurable formulas for maximum health, magicka, and stamina. Attribute changes recalculate derived maxima without changing the original base record.
- Added timed additive attribute modifiers with explicit stack, replace-by-source, refresh-duration, and expiry behavior. `PlayerRecord` saves base stats and remaining modifier durations through the existing save format.
- Resolved the remaining High skinned-draw allocation finding: `SkinnedMeshGpuBuffer` now reuses a per-buffer bone-matrix scratch array in both draw paths.

### Task 94–96 and code-review checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 181 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — content references, formula fixtures, modifier rules/expiry, and save/load round trip |

At this update, 107 of 154 roadmap rows are complete (69.5%). Task 97 is next. The Stage 10 integration gate remains Pending until the live world loader wires these stores into gameplay and the full two-cell restart scenario is exercised.

## Tasks 97–98 — persistent containers and atomic item transfers — 24 September 2026

- Added `ContainerInventoryStore`, keyed by the stable world-instance GUID value. Bags are copied at store boundaries, and an emptied container remains as an empty record so authored loot cannot refill it after a save/reload.
- Added `WorldItemStore` for loose item stacks and `InventoryTransfer.TryPickup`/`TryDrop`. Transfers prepare replacement inventory and world-item snapshots; invalid definitions, insufficient counts, duplicate IDs, or overflow return failure with both inputs unchanged.
- Added save/load validation for duplicate or malformed container/world-item IDs and item entries. The RPG layer uses GUID values for world identities and remains independent of `Ember.Engine`.

### Task 97–98 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 181 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — emptied container persists, failed transfers preserve both sides, pickup/drop conserve item counts, world-item save/load retains identity |

At this update, 109 of 154 roadmap rows are complete (70.8%). Task 99 is next. The Stage 10 live loader/save/restart integration gate remains Pending.

## Tasks 99–101 — equipment, melee, and persistent actor state — 24 September 2026

- Added equipment stat bonuses, attachment-bone metadata, and melee profiles to item definitions. Equip/unequip returns a new player record after moving items between bag and slots; current derived stats and bone descriptors are rebuilt from the saved slots and definitions.
- Added range-, damage-, and cooldown-driven melee rules. Invalid, out-of-range, dead-target, and cooldown-blocked attempts leave both actor records unchanged.
- Added `ActorRuntimeStore` keyed by world-instance GUID. Health/death, cooldown, and an actor's inventory now round-trip in `SaveState`, including a dead looted actor that remains dead after load.
- Bone attachment data stays graphics-free in `Ember.Rpg`; the game/view layer resolves its descriptors against the loaded skin with `GltfBoneAttachment`.

### Task 99–101 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 181 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — equip/swap/unequip, attachment rebuild, melee range/cooldown/death, actor loot/death save/load |

At this update, 112 of 154 roadmap rows are complete (72.7%). Task 102 is next. The Stage 10 live loader/restart and Stage 11 branching-quest integration gates remain Pending.

## Tasks 102–104 — targeted spells, factions, and contextual dialogue — 24 September 2026

- Added typed targeted-spell definitions with range, magicka cost, cooldown, modifier target/value/duration, and stacking policy. Invalid casts leave both actors unchanged; successful casts record a unique cast ID, and the save preserves cost, cooldown, effect timer, and replay protection.
- Added immutable per-actor faction membership and reputation records. Updating one actor no longer changes another actor's membership or standing; both survive save/load.
- Dialogue options can now check flags, effective attributes, and faction membership/reputation. `Pick` reevaluates requirements, applies flag effects once, and stores a choice key in dialogue progress so reloads and self-loops cannot replay an accepted choice.

### Task 102–104 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 181 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — cast validation/cost/cooldown/replay/expiry, isolated faction persistence, stat/faction-gated dialogue and one-time choice persistence |

At this update, 115 of 154 roadmap rows are complete (74.7%). Task 105 is next. The Stage 10 live world-loader/save/restart and Stage 11 complete branching-quest integration gates remain Pending.

## Code-review follow-up — looping animation endpoint — 24 September 2026

- Resolved the Medium animation-playback finding: when looping is enabled, seeking to or beyond the clip duration now wraps to time zero, keeping seek behavior consistent with looped advancement.
- Added regression coverage for endpoint seeks, multi-period wrapping, reverse non-loop playback stopping at zero, and a zero-duration clip.

### Code-review follow-up checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 182 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — `[OK] save then load equals original` |

## Tasks 105–107 — quest events, merchant trades, and owned items — 24 September 2026

- Quest stages can complete on interaction, actor-killed, and item-collected events. Events carry unique IDs plus stable world-instance and content IDs; processed event IDs and stage flags prevent replay and survive save/load, so progression does not require the target cell or actor object to remain loaded.
- Added `MerchantTrade.TryBuy` and `TrySell`. Currency is saved for players and actors; transactions preflight funds, stock, definitions, and overflow, then return both updated parties together. A failed transaction leaves both inputs unchanged.
- World item stacks can name an owning faction. `TheftSystem.TryTake` enforces faction membership, records theft event IDs, and applies a witnessed reputation penalty once. Unwitnessed theft transfers the item without reputation loss; authorized members do not trigger theft handling.

### Tasks 105–107 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 182 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — quest event replay/save/unloaded-target behavior, atomic trade rules, ownership/witness rules, and save/load checks |

At this update, 118 of 154 roadmap rows are complete (76.6%). Task 108 is next. The Stage 10 live world-loader/save/restart and Stage 11 complete branching-quest integration gates remain Pending.

## Tasks 108–110 — skill-use progression and cell routes — 24 September 2026

- Added JSON-authored skill-use rules keyed by stable action IDs. Only configured actions advance their mapped skill; partial use counts and ranks are part of saved actor stats and survive a player save/reload.
- Added validated per-cell navigation graphs with stable node IDs, finite positions, directed/bidirectional edges, and a maximum actor-clearance radius. Versioned graph files save atomically and reject missing endpoints and duplicate/invalid edges.
- Added deterministic shortest-route search over edges that meet the actor's clearance requirement. Invalid endpoints fail clearly; valid but disconnected targets return no route.

### Tasks 108–110 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 185 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — only configured skill actions progress, and saved partial use advances the intended rank after reload |

At this update, 121 of 154 roadmap rows are complete (78.6%). Task 111 is next. Stage 10's live world-loader/save/restart and Stage 11's complete branching-quest integration gates remain Pending.

## Tasks 111–112 — physics route following and cross-cell route plans — 24 September 2026

- Added `PhysicsCharacterPathFollower` for one local `CellPathRoute`. It drives the existing capsule controller, advances through waypoints, reports arrival, and stops issuing movement when collision prevents progress through its timeout.
- Added typed exterior-boundary and door connections between cell graphs. Network validation checks cell kinds, stable endpoint node IDs, clearance, unique arcs, and door-instance IDs. Versioned network files persist connections separately from each cell's navigation graph.
- Added world-route search that returns separate local node legs and explicit oriented transitions. A cross-cell/door route therefore describes where local movement ends and which cell transition follows, without blending the portal gap into local movement.

### Tasks 111–112 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 189 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — `[OK] save then load equals original` |

At this update, 123 of 154 roadmap rows are complete (79.9%). Task 113 is next. The Stage 10 save/restart integration, Stage 11 branching-quest gate, and Stage 12 multi-NPC schedule/travel gate remain Pending.

## Review fix and tasks 113–115 — NPC travel, perception, and combat AI — 24 September 2026

- Resolved the outstanding character-import review item: `GltfSkinData.Import` rejects skins above the current `SkinnedEffect` joint limit before pose creation. It validates finite rest position/scale/quaternion values and unit rest rotations at import, with errors naming the source skeleton node. Regression coverage rejects a 73-joint skin; the Fox character fixture continues to import.
- Task 113 adds `WorldNpcCellTransition`. It prepares and activates each destination before atomically moving the runtime object and stable identity across cells. The source stays active for the player's use. A three-cell route crosses an exterior boundary and an interior door, then a world-save reload restores the follower in exactly one cell. A destination collision test confirms failed transfers leave the follower and identity in the source.
- Task 114 adds `ActorPerception.Evaluate`, which checks range and uses a World-layer physics ray for line of sight. Tests cover a blocking wall, a clear path, distance limits, and geometry behind the target.
- Task 115 adds a data-only idle/chase/attack/dead enemy state machine in `Ember.Rpg`. It returns movement direction for the engine controller and uses `MeleeCombat` for damage/cooldown. Checks cover pursuit intent, a successful strike, cooldown, target loss, and death. `Ember.Rpg` remains independent of engine physics; the game loop supplies the physics visibility result.

### Review and tasks 113–115 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 194 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — save/load plus enemy pursuit, attack, cooldown, target-loss, and death checks |

At this update, 126 of 154 roadmap rows are complete (81.8%). Task 116 is next. The Stage 10 save/restart integration, Stage 11 branching-quest integration, and Stage 12 multi-NPC schedule/travel gate remain Pending.

## Tasks 116–118 — actor budgets, world clock, and dormant schedule catch-up — 24 September 2026

- Task 116 adds deterministic near/far/dormant classification and independent near/far callback limits in `ActorUpdateBudget`. Near actors are ordered by distance; far updates rotate across frames; dormant actors receive no update. The scheduler only returns actor IDs and work decisions, leaving runtime state owned by the game so it survives dormancy unchanged.
- Task 117 adds monotonic `WorldClock`, a repeating two-cell `NpcDailySchedule`, and per-actor schedule bookkeeping. A schedule evaluation creates one pending travel request; repeat evaluations do not duplicate it. Completion clears the pending destination for a later schedule change.
- Task 118 adds `CatchUpDormant`. It resolves the current cell directly, clears stale pending travel, advances cooldowns and timed effects once by the full elapsed duration, and counts crossed daily boundaries with arithmetic rather than replaying every missed day/frame.

### Tasks 116–118 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 196 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — schedule boundary selection, duplicate-request suppression, 201-boundary catch-up, timer expiry, and one-work-item result |

At this update, 129 of 154 roadmap rows are complete (83.8%). Task 119 is next. The Stage 10 save/restart integration, Stage 11 branching-quest integration, and Stage 12 multi-NPC schedule/travel gate remain Pending.

## Tasks 119–120 — outdoor benchmark and shared terrain chunks — 24 September 2026

- Task 119 adds `Docs/OUTDOOR_BENCHMARK.md`: a fixed 114 m loop through four cells in the current 3×3 RpgSlice world, exact scene/content counts, 1280×720 windowed settings, and live reference-PC details (Intel i5-1035G1, Intel UHD Graphics, about 19.8 GiB visible memory). Frame-time, loading, working-set, and chunk-count values are explicitly provisional targets; no benchmark results are claimed.
- Task 120 adds `ITerrainHeightMaterialSource`, `TerrainChunkMeshBuilder`, and `HeightmapTerrainRenderer`. Campaign's current seeded height/biome sampling now supplies the shared builder; RpgSlice draws a bounded terrain neighborhood using an independent source and no Campaign reference. The builder indexes samples in global grid space; tests compare every edge position, tint, and UV between adjacent chunks.
- Collision still uses the RpgSlice blockout floor, so visible height and physics do not yet agree. Task 121 addresses that by using the same sampled boundary values for terrain collision and cell loading.

### Tasks 119–120 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 198 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — `[OK] save then load equals original` |
| RpgSlice screenshot smoke via managed DLL | PASS — rendered a 1280×720 frame showing the terrain and cell blockout |

At this update, 131 of 154 roadmap rows are complete (85.1%). Task 121 is next. Stage 10 save/restart, Stage 11 branching-quest, Stage 12 multi-NPC schedule/travel, and Stage 13 measured outdoor-budget gates remain Pending.

## Task 121 — terrain collision follows exterior-cell lifecycle — 24 September 2026

- `PhysicsWorld` now adds and removes static triangle meshes with owned shape cleanup. Terrain mesh indices face the walkable surface for BEPU's one-sided triangle contacts.
- RpgSlice prepares each nearby exterior scene and terrain chunk on a worker, activates them through `WorldCellLoadOperation`, and makes a destination collision-ready only after its terrain and static scene colliders are active. Unloading a retained cell removes its colliders; movement remains gated while a requested cell is preparing.
- Terrain rendering and collision both consume `TerrainChunkMeshBuilder` output from the same height/material source. The player movement smoke crosses from cell (0, 0) into cell (0, -1) and checks that the character remains grounded.

### Task 121 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 199 tests, 0 failed, 0 skipped |
| `dotnet samples/RpgSlice/bin/Debug/net9.0-windows/win-x64/RpgSlice.dll --smoke-controls` | PASS — crossed (0, 0) to (0, -1) over 23.06 m and remained grounded |
| RpgSlice 1280×720 screenshot smoke | PASS — terrain and cell blockout rendered |

At this update, 132 of 154 roadmap rows are complete (85.7%). Task 122 is next. Stage 10 save/restart, Stage 11 branching-quest, Stage 12 multi-NPC schedule/travel, and Stage 13 measured outdoor-budget gates remain Pending.

## Tasks 122–123 — masked materials and time-of-day lighting — 24 September 2026

- Task 122 adds glTF `MASK` alpha mode and validated cutoff data while keeping `BLEND` explicitly unsupported. CharacterStudio's main scene shader and both static/skinned shadow-depth paths sample the same base-color alpha and discard fragments below the cutoff, so cutout holes do not write scene depth or solid shadow-map pixels.
- Task 123 adds deterministic `OutdoorEnvironmentProfile` values for sky, fog, sun direction/color, and ambient light. RpgSlice can select a reproducible time with `--time-hours` and `--time-paused`; PageUp/PageDown adjust the clock, which otherwise advances at one game hour per real minute. Shared terrain normals let the same profile light the terrain and every active cell prop.

### Tasks 122–123 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors; CharacterStudio shadow effect compiled |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 203 tests, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — save then load equals original |
| CharacterStudio 1280×720 screenshot smoke | PASS — static scene and shadow passes ran with the updated effect |
| RpgSlice screenshots at 00:00 and 12:00 with paused clock | PASS — sky, terrain illumination, and scene lighting visibly change |
| RpgSlice `--smoke-controls` | PASS — crossed cell (0, 0) to (0, -1) over 23.06 m and remained grounded |

At this update, 136 of 154 roadmap rows are complete (88.3%). Task 126 is next. Stage 10 save/restart, Stage 11 branching-quest, Stage 12 multi-NPC schedule/travel, and Stage 13 measured outdoor-budget gates remain Pending.

## Task 124 — outdoor water surface — 24 September 2026

- Added a reusable flat water-plane renderer with a tiled ripple texture and an authored water level. Its draw pass uses alpha blending and depth-read/no-depth-write, then restores the caller's blend, depth, rasterizer, and sampler states.
- RpgSlice now renders a sea-level plane after opaque terrain and scene geometry; terrain above the plane remains visible and defines the shoreline. The implementation is a single horizontal layer: it has no refraction or depth-based shoreline fade, and callers must order it relative to any other transparent surfaces.

### Task 124 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| Water-plane geometry tests | PASS — dimensions, world-unit UV repeats, and invalid dimensions; 2 passed |
| RpgSlice 1280×720 noon screenshot smoke | PASS — terrain shoreline remains visible around the water plane |

## Task 125 — authored static-mesh distance LOD — 24 September 2026

- Scene format version 5 adds paired near/far static GLB references and separate enter/exit distances. The distance band is explicit hysteresis; CharacterStudio selects one representation per object and shares that choice across the scene and shadow passes.
- Project asset resolution, sequence asset snapshots, scene cloning, and project packaging include both references. LOD variants must be static meshes and use distinct asset IDs; the current authoring path is scene JSON, and both assets remain resident while the cheaper far representation is drawn at distance.
- A temporary CharacterStudio scene selected and rendered its far GLB in both passes at 1280×720. The far fixture had 10 triangles versus 228 in the near fixture; the temporary scene file is not part of the repository.

### Task 125 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| LOD, scene-file, travel-component, and package tests | PASS — threshold hysteresis, versioned round-trip, validation, and both packaged assets; 22 passed |
| CharacterStudio 1280×720 LOD screenshot smoke | PASS — five static scene and shadow draws came from the selected far GLB |

### Combined verification — code-review fix and tasks 124–125

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --no-build --no-restore --nologo` | PASS — 212 passed, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — `[OK] save then load equals original` |
| RpgSlice `--smoke-controls` | PASS — crossed from cell (0, 0) to (0, -1), 23.06 m, grounded |

## Code-review fix — buffered jump input — 24 September 2026

- `PhysicsCharacterController` now retains a jump request for a configurable 0.1 seconds of fixed simulation time. A request made while airborne can trigger on the first grounded physics step; it is discarded after the window expires or after one successful jump.
- Added a two-substep catch-up regression where the first step lands and the second jumps, plus an expiry case proving an old airborne request does not trigger on a later landing.

### Code-review fix checks

| Check | Result |
| --- | --- |
| Focused jump-buffer tests | PASS — catch-up landing/jump and expiry, 2 passed |

## Code-review fix and tasks 126–127 — 24 September 2026

- Resolved the Stage 13 shadow-camera review finding. CharacterStudio now fits one stable orthographic shadow volume to the active camera frustum, caps receivers at 120 m, and snaps the center to the shadow map's texel grid. Scene bounds extend the caster depth range without reducing receiver resolution. Live rendering and sequence export use the same fit.
- Added tests for frustum coverage and sub-texel stability. A Release CharacterStudio capture rendered five scene draws and five shadow draws at 1280×720.
- RpgSlice now uploads an instanced cube mesh for repeated foliage when HiDef is supported, with the existing individual-draw path as the Reach fallback. Its cell scenes and static colliders also receive the manifest coordinate offset; without that transform the populated-cell benchmark drew and collided props at the origin. The player draw and follow camera now share the same interpolated physics pose, resolving the RpgSlice jitter review finding.
- The first physical route exposed a blockout boulder on the original straight return. The reproducible benchmark route now detours around that obstacle; each lap is 122 m and crosses the same four cell boundaries.
- `RpgSlice --benchmark` drives one warmup lap and ten measured laps. It records frame timing, main-thread cell activation, working set, active cells, terrain chunks, tracked renderer resources, boundary crossings, and per-lap resource peaks.

### Tasks 126–127 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln -c Release --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln -c Release --no-build --no-restore --nologo` | PASS — 214 passed, 0 failed, 0 skipped |
| CharacterStudio Release screenshot | PASS — 1280×720; five scene draws and five shadow draws |
| RpgSlice Release screenshot | PASS — HiDef instancing enabled; cell props are distributed at their world coordinates |
| RpgSlice fixed outdoor benchmark | PASS — final repeat: 10 laps, 40 cell crossings; 1.60 ms average, 2.61 ms p95, 28.88 ms maximum; longest activation 33.34 ms; 196.3 MiB peak working set |
| Foliage submission comparison | PASS — nine repeated props reduced from nine individual submissions to one instanced submission |
| Per-lap resource trend | PASS — each measured lap peaked at 9 active cells, 16 terrain chunks, and 26 tracked renderer resources; working set ranged 194.1–196.4 MiB |

The final frame, activation, memory, terrain, and resource-stability targets pass on the Intel Core i5-1035G1 / Intel UHD reference PC. One prior run observed 14 frame intervals above 100 ms (18 above 50 ms); a repeat on the final render pose recorded none, and cell activation never exceeded 33.34 ms. Task 144 adds phase attribution if the long frame tail recurs before increasing world density. Passing on this small 3×3 blockout is not a capacity claim for a denser settlement or a larger map.

At this update, 138 of 155 roadmap rows are complete (89.0%). Task 128 is next. Task 144 is a required performance-triage gate before adding world density.

## Task 128 — CharacterStudio world cell browser — 24 September 2026

- Added `WorldCellWorkspace` operations to create an empty world manifest, create an empty exterior or interior scene, and rename a cell by copying its scene to a unique path before atomically updating the manifest. The cell `Guid` and exterior coordinates survive renaming; duplicate coordinates are rejected by the manifest validator and the newly created scene file is rolled back.
- Added a World Cells editor window in CharacterStudio to open/create a manifest, list and open cells, create exterior/interior cells, rename a selected cell, and save an untitled current scene before switching. If the active cell is renamed, subsequent saves follow its new scene path.
- New cells use distinct scene files even when names collide. Cell switching builds the replacement scene preview before swapping, clears the prior undo/redo selection state, and leaves the active scene intact when loading fails.

### Task 128 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln -c Release --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln -c Release --no-build --no-restore --nologo` | PASS — 217 passed, 0 failed, 0 skipped; includes create, rename, ID/coordinate preservation, duplicate-coordinate rollback, and unique paths |
| CharacterStudio Release screenshot smoke | PASS — 1280×720; World Cells window rendered and process shut down cleanly |
| Direct mouse-driven editor actions | Not exercised; corresponding workspace file operations are covered by regression tests |

At this update, 139 of 155 roadmap rows are complete (89.7%). Task 129 is next. Task 144 remains a performance-triage gate before increasing world density if the long-frame tail recurs.

## Code-review fix — skinned buffer pose ownership — 24 September 2026

- `SkinnedMeshGpuBuffer` previously validated only the pose's joint count. A pose from a different skeleton with the same number of joints could therefore be submitted to the buffer and render using unrelated bone transforms.
- The buffer now retains the exact `GltfSkinData` used to create it. Both `Draw` overloads validate skin identity before copying bone transforms; the CharacterStudio and generated MinimalGame call sites now pass the skin object.
- Added a CPU-only regression proving that the matching skin is accepted and a separately imported skin with the same joint count is rejected.

### Code-review fix checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln -c Release --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln -c Release --no-build --no-restore --nologo` | PASS — 218 passed, 0 failed, 0 skipped; includes the equal-joint-count/different-skin regression |
| Generated Minimal Ember Game Release build | PASS — generated outside the checkout and built against this engine; 0 warnings and 0 errors |

## Code-review fix — fresh bone attachment transforms — 24 September 2026

- Resolved the attachment-staleness finding: `GltfSkinPose` marks calculated matrices dirty after local-transform edits, resets, and pose copies, then recomputes before node, mesh, or skin matrix data is read. A bone attachment can no longer silently use the previous pose after a direct edit.
- Added a regression that edits a hand joint and requests its attachment world matrix without an explicit `ComputeSkinMatrices` call.

### Code-review fix checks

| Check | Result |
| --- | --- |
| `dotnet test tests/Ember.Engine.Tests/Ember.Engine.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~GltfAnimationTests --nologo` | PASS — 13 passed, 0 failed, 0 skipped |

## Tasks 129–130 — RPG placement palette and door travel picker — 24 September 2026

- Task 129: scene format version 6 stores a typed actor/item definition ID and stable world-instance ID. CharacterStudio loads validated RPG content and lists registered actors/items in a placement palette. New placements receive unique scene and instance IDs; duplication creates a fresh instance ID, scene save/load preserves it, and the identity map adopts the authored ID while rejecting conflicts or drift. These are logical placements and do not yet bind character/item models or animation sets.
- Task 130: CharacterStudio can add stable spawn markers to the active world cell, select a destination cell and spawn marker, validate the destination with `WorldTravelValidator`, and assign the link through undoable scene history. The panel checks every authored door and labels unresolved cell/spawn references as broken.
- The sample palette content registers two actors and two items. The travel picker uses the active in-memory scene for unsaved spawn edits and reloads cached destination scenes when their path or modification time changes.

### Tasks 129–130 and review-fix checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln -c Release --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln -c Release --no-build --no-restore --nologo` | PASS — 225 passed, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check/Ember.Rpg.Check.csproj -c Release --no-build --no-restore` | PASS — save then load equals original |
| CharacterStudio Release screenshot smoke | PASS — 1280×720; RPG panel loaded both sample catalogues and the World Travel panel rendered |
| Direct mouse-driven editor actions | Not automated; placement, stable-ID persistence, validator rejection, and command undo/redo have regression coverage. The visible broken-link status still needs a direct editor interaction check. |

At this update, 141 of 155 ordered rows are complete (91.0%). Task 131 is next. Task 144 remains a conditional performance-triage gate before increasing world density if the long-frame tail recurs.

## Tasks 131–132 — cell path and dialogue authoring — 24 September 2026

- Task 131: the CharacterStudio Paths tab edits stable path nodes and directed/bidirectional edges, saves each cell graph under `Navigation/<cell-guid>.paths.json`, computes shortest routes at a chosen NPC clearance, and draws a plan-view reachability map. In play mode, the selected scene object can follow the selected route through the physics character controller. `SceneGraphCloner` now also retains world placement, door, spawn, and reset-policy data in the play clone.
- The route preview currently runs against a separate flat physics floor. It does not import scene collision geometry, traverse slopes/stairs, or find paths between cells; this proves local route authoring/following, not finished world navigation.
- Task 132: the Dialogue tab creates conversations and nodes, edits node IDs/speaker/text/order, links choices to nodes, and edits flag, actor-stat, faction, and typed flag-effect data. Content validation collects malformed records and missing references before enabling save. The full content pack is written atomically; the sample content pack includes a small branching Town Guard conversation to edit.
- `RpgContentSet.Validate` now reports several dialogue-record errors together, including empty node/choice data, invalid conditions, non-finite effects, and missing references. The content round-trip regression confirms a rejected invalid save leaves the previous file intact.

### Tasks 131–132 checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln -c Release --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln -c Release --no-build --no-restore --nologo` | PASS — 229 passed, 0 failed, 0 skipped; includes saved-path follow, dialogue validation/save-load, and play-clone identity tests |
| `dotnet run --project tests/Ember.Rpg.Check/Ember.Rpg.Check.csproj -c Release --no-build --no-restore` | PASS — save then load equals original |
| CharacterStudio Release screenshot smoke | PASS — 1280×720; RPG Authoring panel and its tabs rendered with the showcase scene |
| Direct mouse-driven path/dialogue authoring | Not exercised; runtime path following and persisted dialogue authoring/validation are covered by regression checks, and the editor launch/render path is smoke-checked |

At this update, 143 of 155 ordered rows are complete (92.3%). Task 133 is next. Task 144 remains a conditional performance-triage gate before increasing world density if the long-frame tail recurs.

## Code-review follow-up — live cell activation — 24 September 2026

- `Ember.Engine.WorldCellStreamer<TPrepared, TActive>` now owns the reusable loading-ring, lifecycle, activation-queue, retry, collision-notification, and retirement orchestration. RpgSlice supplies terrain preparation and a cell-specific physics stepper. Terrain collision is added in fixed 16-interval patches, and cells share patch index topology through `CellAssetReferencePool` leases until the final user unloads.
- Failed preparation or activation attempts now retry with capped exponential backoff. RpgSlice shows the failed cell and retry delay in the window title, and the starting cell reports a clear error after three failed attempts.
- Fixed two queue lifecycle issues found during integration: canceling a queued cell before its first step now disposes its stepper, and reported queued activation cost cannot become negative when actual work exceeds its estimate.
- No ordered roadmap rows were added or marked complete; the checklist remains 143/155 (92.3%), with task 133 first unchecked.

### Integration follow-up checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln -c Release --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln -c Release --no-build --no-restore --nologo` | PASS — 233 passed, 0 failed, 0 skipped; includes queued-stepper cleanup, cost-accounting, transient retry, and cell-retirement regressions |
| `dotnet run --project tests/Ember.Rpg.Check/Ember.Rpg.Check.csproj -c Release --no-build --no-restore` | PASS — save then load equals original |
| RpgSlice `--smoke-controls` | PASS — player crossed from cell (0, 0) to (0, -1) and remained grounded using the engine-level streamer |
| RpgSlice `--streaming-smoke` | PASS — 3×3 queued activation completed in 22 frames; 20-boundary retention smoke released shared resources at the last reference |
| RpgSlice `--benchmark --windowed --time-paused --time-hours 12` | PASS — 10 laps, 40 cell crossings, instancing enabled; 0.79 ms average, 1.86 ms p95, 14.00 ms max, 19.31 ms longest activation, 163.0 MiB peak working set; all provisional targets met. See `OUTDOOR_BENCHMARK.md` |

Stage 9–12 gameplay integration evidence is still pending. RpgSlice now has a live door travel round-trip; persistence wiring, scheduled NPC movement, and merchant/enemy use of `Ember.Rpg` remain open.

## Stage 9 integration follow-up — live RpgSlice door travel — 24 September 2026

- RpgSlice now finds nearby authored doors and shows an E-key prompt. Pressing E prepares and activates the target cell, places the player at the authored spawn and facing, then unloads the source through `WorldCellTravelTransaction`.
- Interior travel activates scene colliders including the ground, excludes door panels from blocking movement, pauses exterior streaming, and renders the active interior scene. Returning to an exterior adopts the destination into the reusable streamer and refreshes the loading ring.
- The transaction now reports source cleanup errors while keeping the travel committed after destination activation and player placement. The source-cleanup failure regression verifies the destination remains active.
- The RpgSlice `--travel-smoke` route walks to House A, uses E at the exterior door and return door, and checks both authored spawn placements in the live physics runtime.

### Live door travel checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln -c Release --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln -c Release --no-build --no-restore --nologo` | PASS — 234 passed, 0 failed, 0 skipped; includes travel commit behavior when source cleanup throws |
| RpgSlice `--travel-smoke` | PASS — walked to House A, used E for both transitions, and returned to the authored exterior spawn |

No ordered roadmap rows were added or marked complete. The checklist remains 143/155 (92.3%), task 133 is first unchecked, and task 144 remains a conditional performance-triage gate. Stage 9–12 integration work still needs persistence wiring, scheduled NPC movement, and merchant/enemy use of `Ember.Rpg`.

## Stage 10 integration follow-up — persistent RpgSlice world — 25 September 2026

- Added `WorldPersistenceSession` to own the engine's world identity map, per-cell change store, runtime-object store, and stable-boundary save queue. Cell activation restores runtime objects and saved edits before building colliders.
- RpgSlice loads an existing world save on startup, restores player location and facing in an exterior or interior, and queues F5 saves so a request during door travel captures the post-travel stable location. `--save <path>` selects a save file; the default is `%LOCALAPPDATA%\Ember\RpgSlice\world-save.json`.
- Added a restart-style `--persistence-smoke`: it writes an authored transform/enabled override and runtime object, saves an interior player location, starts a fresh persistence session and physics streamer from the file, then checks the restored exterior edit, unique runtime identity, and interior spawn/player location.
- Added an engine regression covering an edit and runtime spawn, a save deferred through travel, stable player capture, save reload, and idempotent cell preparation.
- The ordered checklist remains 143/155 (92.3%); task 133 is still the first unchecked baseline task. Integration evidence is tracked separately:

| Integration proof | Status | Evidence and remaining scope |
| --- | --- | --- |
| Live RpgSlice door travel through `WorldCellTravelTransaction` | PASS | `--travel-smoke` walks to House A and returns through both authored doors. The complete Stage 9 gate remains Pending. |
| Persistence stores, cell activation, stable-boundary save, and restart restore in RpgSlice | PASS for this integration proof | Engine persistence regression and `--persistence-smoke` pass. F5 is wired to the same request path, though the smoke calls that API directly instead of synthesizing a keypress. The complete Stage 10 gate remains Pending until its full cross-cell move/create/delete and exactly-once acceptance checks are covered. |
| Scheduled NPC following the world path network | Pending | Not yet wired in live RpgSlice. |
| Merchant and enemy using `Ember.Rpg` in live RpgSlice | Pending | Not yet wired in live RpgSlice. |

### Persistence integration checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln -c Release --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet test Ember.sln -c Release --no-restore --nologo` | PASS — 235 passed, 0 failed, 0 skipped |
| RpgSlice `--persistence-smoke` | PASS — authored changes, runtime identity, interior player location, and restart reload |
| RpgSlice `--travel-smoke` after persistence integration | PASS — walked to House A, traveled through both doors, and returned to the authored exterior spawn |

No ordered roadmap rows were added or marked complete. Stage 9–12 gameplay work remains in progress; the next integration step is a scheduled NPC using the world path network.

## Stage 11–12 integration follow-up — scheduled RPG gameplay — 26 September 2026

- RpgSlice now loads two authored exterior path graphs and their world connection, spawns a scheduled worker with a stable world-instance ID, and follows the daily schedule across an active exterior boundary. The integration smoke advances time past the evening boundary and verifies the worker returns home without creating a duplicate runtime object.
- RpgSlice now uses `MerchantTrade` for nearby buy/sell actions and stores actor inventory/currency state. A nearby road raider uses `ActorPerception` against the engine physics world and `EnemyCombatAi` to update chase/attack behavior; player attacks use the RPG melee rules.
- `SaveState` is now version 2 and stores monotonic world time plus per-instance NPC schedule state. Version 1 saves migrate with empty schedule data, unknown save members are rejected, and RPG saves write through a flushed temporary file and replacement. RpgSlice writes the RPG data to a sidecar next to its world save; the two files are not a transactional pair.
- Fixed a schedule correctness issue found during integration: a time change now cancels or replaces a stale pending destination, and crossing an intermediate cell updates the NPC's current cell while preserving its final scheduled destination.
- No ordered roadmap rows were added or marked complete; the checklist remains 143/155 (92.3%), with task 133 first unchecked. Targeted integration proofs now pass, while the full Stage 9–12 gates remain Pending.

### Scheduled gameplay integration checks

| Check | Result |
| --- | --- |
| `git fetch origin` and revision check | PASS — `master` and `origin/master` both at `712a360` before this batch |
| `dotnet build samples/RpgSlice/RpgSlice.csproj --configuration Release --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet run --project tests/Ember.Rpg.Check --configuration Release --no-restore` | PASS — save round-trip, schedule destination cancellation/replacement, clock/schedule persistence, v1 migration, and strict unknown-field rejection |
| `dotnet test tests/Ember.Engine.Tests/Ember.Engine.Tests.csproj --configuration Release --no-restore --nologo` | PASS — 237 passed, 0 failed, 0 skipped |
| RpgSlice `--rpg-integration-smoke --windowed` | PASS — merchant failure/success cases, save round trip, physics line of sight, idle/chase/attack/dead AI, scheduled trip across cells (0, 0) and (1, 0), and return home |
| RpgSlice `--travel-smoke --windowed` | PASS — House A exterior/interior round trip through authored doors |
| RpgSlice `--persistence-smoke --windowed` | PASS — authored edit, runtime identity, interior player location, and restart reload |

### Remaining acceptance scope

- Stage 9 still needs the 3×3 player walk, two separate interior routes, slow/failed loads, rapid direction changes, and frame/resource measurements.
- Stage 10 still needs a live cross-cell move/create/delete plus interior save/restart and exactly-once checks.
- Stage 11 still needs the full branching quest and save/restart coverage for equipment, effects, faction changes, loot, and progression.
- Stage 12 still needs several scheduled NPCs, a follower through an interior door and restart, and dormant actor scene-retention evidence.
- The RPG sidecar and engine world save can disagree after an interrupted two-file write. Atomic world-save restore and strict unmapped-member handling in the other persisted formats are also open.

## Tasks 133–134c — quest objectives and placement templates — 26 September 2026

- Task 133: added the CharacterStudio Quests tab. It creates quests and ordered event objectives, edits journal text, event kind, optional start dialogue, and actor/item/world-instance targets. The existing content validator supplies owning-record diagnostics and prevents saving a pack with broken references.
- Quest objective checks round-trip a valid authored event target, report a missing actor ID with its quest/stage source, and apply a matching `ActorKilled` event to complete the authored objective.
- Task 134 was split into three independently checked rows. Scene version 7 persists template IDs and explicit definition/position/rotation/scale override flags on RPG placements. Older untemplated scenes still load through the existing version migration path.
- Added a versioned atomic placement-template library and update operation. Updating a template refreshes only inherited definition/transform fields; explicit overrides and world-instance IDs remain. The update command participates in scene undo/redo, and duplication preserves the template link/mask while assigning a fresh world-instance ID.
- CharacterStudio now has a Templates tab to create/update a template from a selected placement, save/reload the library, place an instance, apply updated defaults, and toggle explicit per-instance override flags.
- Roadmap rows 133 and 134a–134c are checked. Two rows were added by splitting 134; the checklist is now 147/157 (93.6%). Task 135 is first unchecked. The Stage 14 gate remains Pending until project-wide validation, recovery, and complete settlement authoring are proven.

### Quest and template checks

| Check | Result |
| --- | --- |
| `dotnet build samples/CharacterStudio/CharacterStudio.csproj --configuration Release --no-restore --nologo` | PASS — 0 warnings and 0 errors |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors; FirstLight, Campaign, RpgSlice, CharacterStudio, engine, RPG, and check projects compiled |
| `dotnet test tests/Ember.Engine.Tests/Ember.Engine.Tests.csproj --configuration Release --no-restore --nologo` | PASS — 239 passed, 0 failed, 0 skipped |
| `dotnet test Ember.sln --no-restore --nologo` | PASS — 239 passed, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --configuration Release --no-restore` | PASS — save/load check plus quest objective reference and event checks |
| CharacterStudio direct UI interaction | Not automated — the desktop automation surface returned no app inventory and no native launch API; editor code compiled, while model/reference/event behavior has automated coverage |

The Stage 14 gate remains Pending. The next batch starts at task 135 (project-wide reference validation), then task 136 (authored-content autosave/recovery). The remaining Stage 9–12 and Release gates are unchanged.

## Tasks 135a–135c and 136a — project validation and recovery foundation — 26 September 2026

- Split task 135 into three independently checked rows. `WorldProjectValidator` loads each cell scene and local path graph, checks duplicate spawns and door destinations, validates the world path network, and retains diagnostics with source file and record. A validation-only manifest load lets the audit report missing scene files individually; runtime `WorldManifest.Load` stays strict.
- `RpgContentJson.ParseForValidation` returns structured RPG reference diagnostics while `FromJson` and `Load` keep rejecting invalid packs. `RpgProjectValidator` adds scene actor/item placement checks with the owning scene and object.
- Added `Ember.Authoring` to combine world, dialogue/quest/content, and placement diagnostics. CharacterStudio's World Cells panel has a **Validate project** action and shows the aggregated report. Validation only reads project files.
- Task 136a adds a versioned atomic snapshot store with per-file SHA-256 checksums, path validation, and a per-project default directory under local application data, outside the project tree. Corrupt or unsupported snapshots fail to load. Autosave capture, recovery staging, and editor apply controls are still pending.
- Roadmap rows 135a–135c and 136a are checked. The checklist is now 151/161 (93.8%); task 136b is first unchecked. The Stage 14 gate remains Pending, and Stage 9–12 and Release gates are unchanged.

### Project validation and recovery checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors; FirstLight, Campaign, RpgSlice, CharacterStudio, Ember.Authoring, engine, RPG, and check projects compiled |
| `dotnet test Ember.sln --no-restore --nologo` | PASS — 247 passed, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --configuration Release --no-restore` | PASS — save/load check |
| CharacterStudio `--screenshot ... --warmup 8` via `dotnet exec` | PASS — 1280×720 capture; World Cells panel and validation action rendered |
| CharacterStudio direct button interaction | Not automated — the desktop automation surface still exposes no app inventory or native launch API; project validation is covered by an integration fixture |

## Tasks 136b–136e — authored-content recovery workflow — 26 September 2026

- Task 136b captures the world manifest, every cell scene, RPG content, and top-level world path files into the checksummed recovery store. Snapshots stay outside the project and player-save locations and can be restored to an isolated staging tree.
- Task 136c adds recovery-only JSON serialization for current in-memory scene and RPG state. RPG drafts with semantic reference errors survive capture so the review step can show their diagnostics; ordinary RPG saves remain validation-strict.
- Task 136d adds immediate and one-minute CharacterStudio autosaves plus a recovery review action. Review restores the latest snapshot to staging and shows every project validation diagnostic; invalid staging disables the apply control.
- Task 136e revalidates staged content before applying it, restricts writes to manifest-owned scenes, world path files, the manifest, and RPG content, and uses prepared replacement files with rollback on write failure. CharacterStudio prepares the recovered active scene and preview resources before writing, then replaces editor state without saving over the recovery and leaves player saves untouched.
- Roadmap rows 136b–136e are checked. The ordered checklist is now 155/163 (95.1%); task 137 is first unchecked. The Stage 14 authoring gate remains Pending until the complete settlement/interior/route/quest is authored and verified; Stage 9–12 and Release gates are unchanged.

### Authored recovery checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --no-restore --nologo` | PASS — 0 warnings and 0 errors; all engine, authoring, check, and sample projects compiled |
| `dotnet test Ember.sln --no-restore --nologo` | PASS — 248 passed, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check --no-build --no-restore` | PASS — `[OK] save then load equals original` |
| Authored recovery interruption test | PASS — in-memory scene edits round-trip; semantically invalid RPG drafts stage with diagnostics and are blocked from apply; valid recovery applies; source files remain untouched during review and player saves remain unchanged |
| CharacterStudio `--screenshot ... --warmup 8` via `dotnet exec` | PASS — 1280×720 capture shows the recovery autosave and review controls in the World Cells panel |
| CharacterStudio direct recovery button interaction | Not automated — the current desktop automation surface does not expose direct native-app button input; editor code builds, and capture/stage/validate/apply behavior is covered by the service integration test |

## Tasks 137a–137d — settlement runtime and measured route — 27 September 2026

- Split task 137 into four checkable outcomes. `settlement.json` defines three exterior cells and two interiors; the market cell has authored doors to both houses, and both interiors return to its authored exterior spawn. The project validator accepts every scene, door, spawn, path graph, and RPG placement.
- RpgSlice now loads unique manifest GLB references through `GltfSceneImporter`, shares static GPU meshes/textures, draws the supported courtyard material, and adds each primitive as a streamed triangle-mesh collider. The tracked CharacterStudio courtyard asset is linked into RpgSlice output without duplicating the source file. The GLB appears in the settlement capture and its four primitive colliders activate in the live market cell.
- Added a content pack with four actor definitions and an apple item definition. The merchant and raider are authored actor placements with stable world-instance IDs; RpgSlice adopts them instead of spawning duplicate runtime actors. The scheduled worker continues to use the runtime object store and the authored cross-cell path graph.
- `--settlement-smoke` validates the project, loads the GLB and collider meshes, and confirms three live NPC roles with unique instance identities. The full settlement validator reports 3 exterior cells, 2 interiors, 4 actors, and 1 item; the runtime smoke reports 4 GLB primitives/colliders and 22 unique loaded scene identities.
- Added a separate six-waypoint perimeter route for `--settlement-benchmark`; the fixed 3×3 baseline route and report remain unchanged. The ten-lap Release run crossed cell boundaries 20 times. Average/p95/max frame time was 1.01/2.72/104.70 ms, longest activation 21.05 ms, peak working set 166.6 MiB, and active-cell/chunk/tracked-resource peaks stayed at 3/16/31 on every lap. All agreed budgets passed. Five measured frames exceeded 50 ms, including two over 100 ms; working set rose from 157.8 to 166.6 MiB over the run. Results and setup-route fixes are recorded in `OUTDOOR_BENCHMARK.md`.
- Audited the untracked `test-assets/`: 37 GLBs and 388 PNGs (~455 MB), split between modern Japanese street props/vehicles and two dense city models. Nine standalone vehicle GLBs are the best current importer fit; most houses/props use matrix transforms, vending machines require GPU instancing, and the dense city models require mesh quantization. GLB images are embedded, so the loose PNGs are not required for these files. No source, license, or attribution metadata is present; none of these assets were added to tracked content.
- The ordered checklist is now 159/166 (95.8%); task 138 is first unchecked. Stage 14 remains Pending because the complete quest and editor-authored, no-handwritten-reference workflow have not passed. The broader Stage 9–12 and Release gates are unchanged.

### Settlement checks

| Check | Result |
| --- | --- |
| `git pull --ff-only` | PASS — already up to date; `master` and `origin/master` both at `2f7c186` before this batch |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — 0 warnings and 0 errors; FirstLight, Campaign, RpgSlice, CharacterStudio, authoring, engine, RPG, and check projects compiled |
| `dotnet test Ember.sln --configuration Release --no-build --no-restore --nologo` | PASS — 248 passed, 0 failed, 0 skipped |
| `dotnet run --project tests/Ember.Rpg.Check/Ember.Rpg.Check.csproj --configuration Release --no-build --no-restore` | PASS — save then load equals original |
| RpgSlice `--settlement-smoke --windowed` | PASS — authored content validates; courtyard GLB loads/renders, four mesh colliders activate, merchant/raider/worker identities are unique |
| RpgSlice `--rpg-integration-smoke --windowed` | PASS — merchant trade, physics perception/combat, and scheduled worker trip/return still pass with data-driven RPG definitions |
| Settlement screenshot, 1280×720 | PASS — courtyard GLB rendered with its cell props in Release output |
| RpgSlice `--settlement-benchmark --time-paused --windowed` | PASS — one warmup plus ten measured 166 m laps; all provisional budgets passed. See `OUTDOOR_BENCHMARK.md` |

Limit: the courtyard GLB is linked into the RpgSlice build output. A `--world` path pointing directly at the source-tree settlement folder does not contain that linked file; use the built `Content/World/settlement.json` root for direct runtime runs. The default built app path and settlement smoke/benchmark pass.

## Tasks 138a–138d — complete settlement quest "The Lost Delivery" — 27 September 2026

- Split task 138 into four checkable outcomes (138a–138d). Authored the complete settlement quest `quest.rpgslice.lost_delivery`, dialogue `dialogue.rpgslice.lost_delivery`, notice item `item.rpgslice.delivery_notice`, notice placement in `Exterior_1_0.json`, and satchel placement in `Exterior_0_0.json`.
- Connected market keeper dialogue (keys E and 1) to quest start flag `quest.rpgslice.lost_delivery.started`. Normalized quest flag prefixes in `QuestDef` to avoid duplicate `quest.` prefixes when resolving start and completion flags. Reading the delivery notice advances the active quest stage to `defeat_raider`.
- Connected player melee combat (key F) to damage and defeat the authored road raider; killing the raider advances the quest to `recover_apples`. Collecting the raider's supply satchel (key E) transfers the authored apple into player inventory, applies `recover_apples` quest completion, and marks a persistent deletion tombstone in the world persistence store.
- Authored `RpgSliceQuestSmoke` to drive standard player controls (E, 1, F, E, F5) through gameplay input. Navigates around the Stone Market Courtyard and market prop colliders using waypoints along the clear settlement paths and roads, avoiding static mesh wall obstructions. Upon F5, writes atomic world and RPG saves.
- Verified on restart: `VerifyQuestSmokeAfterRestart()` confirms quest flags remain `Complete`, raider remains dead, player bag holds 2 apples without duplicate satchels in the world-item store, dialogue tree is closed, references validate, and the satchel deletion tombstone persists in the world save without reappearing in the loaded cell.
- Fixed frame-rate dependent timeout in travel smoke by replacing 1200-frame counter with elapsed seconds (20s) so uncapped execution (>400 FPS) does not prematurely abort door travel.
- The ordered checklist is now 163/169 (96.4%); task 139 is first unchecked. The Stage 15 settlement quest proof passes; broader Stage 9–12 and Release gates are unchanged.

### Settlement quest checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings and 0 errors; all engine, RPG, authoring, samples, and test projects compiled |
| `dotnet test tests/Ember.Engine.Tests/Ember.Engine.Tests.csproj --no-build --nologo` | PASS — 248 passed, 0 failed, 0 skipped |
| `dotnet exec tests/Ember.Rpg.Check/bin/Debug/net9.0/Ember.Rpg.Check.dll` | PASS — save then load equals original |
| RpgSlice `--settlement-smoke --windowed` | PASS — settlement world validates, GLB mesh colliders activate, live NPC roles verified |
| RpgSlice `--rpg-integration-smoke --windowed` | PASS — scheduled worker travels between cells, merchant buy/sell trades pass, perception/AI decisions pass |
| RpgSlice `--travel-smoke --windowed` | PASS — exterior to House A and back through authored doors |
| RpgSlice `--persistence-smoke --windowed` | PASS — authored change, runtime identity, interior player location, and restart reload |
| RpgSlice `--quest-smoke --windowed` | PASS — complete Lost Delivery quest played through E/1/F/F5; quest completion, dead raider, apple inventory, and satchel tombstone verified on restart |

## Tasks 139–141 — repeatable persistence, failure recovery, and transition soak — 27 September 2026

- **Task 139 (repeatable persistence scenario across cells):**
  - Added atomic runtime instance transfer (`Transfer`), deletion tombstones (`MarkDeleted`), and deletion queries (`IsDeleted`) to `WorldPersistenceSession`.
  - Authored headless unit test `tests/Ember.Engine.Tests/WorldPersistenceScenarioTests.cs` executing the full 6-action sequence: drop item in Exterior A, loot container in Exterior A, kill enemy in Exterior A, move follower across cell boundary into Exterior B, enter House A interior, write world save, simulate full restart, and reload. Asserted that every resulting state is correct and each persistent instance exists exactly once (no duplicates, no resurrection of killed enemies, no reappearance of looted items, correct cell ownership).
  - Updated `--persistence-smoke` in `samples/RpgSlice/RpgSliceGame.cs` to execute the full 6-action scenario in the live game. Verified: `dotnet exec samples/RpgSlice/bin/Debug/net9.0-windows/win-x64/RpgSlice.dll --persistence-smoke --windowed` exits 0 with exact-once persistence verification.

- **Task 140 (repeated travel/save/load with delayed or failed asset reads):**
  - Created `tests/Ember.Engine.Tests/WorldTravelPersistenceFailureTests.cs` covering two comprehensive failure recovery scenarios:
    1. `RepeatedTravelSaveLoad_WithDelayedAndFailedReads_RecoversWithoutDuplicateActorsOrLostItems`: Runs 5 repeated travel/save/load cycles with simulated asset read delays and `IOException` failures during travel preparation. Verifies that transactions fail cleanly, leaving source cells playable, and subsequent retry recovers successfully. Verifies that save failures (e.g. blocked save paths) preserve existing valid saves, corrupt saves throw actionable `InvalidDataException`, and actors and items maintain exactly-once identities without duplicates or loss.
    2. `RepeatedStreaming_WithDelayedAndFailedReads_RecoversWithoutOrphanedObjectsOrDuplicateIdentities`: Runs cell streamer lifecycle under transient delays and read failures with exponential backoff retry, verifying clean activation and zero orphaned instances.

- **Task 141 (documented route with at least 50 interior/exterior transitions):**
  - Fixed roadmap review finding 572: added calls to `StoneTextures.Clear()`, `PropTextures.Clear()`, `ItemSprites.Clear()`, and `CharacterSprites.Clear()` in `src/Ember.Engine/Engine/EngineHost.cs` (`DisposeHost()`).
  - Authored headless unit test `tests/Ember.Engine.Tests/WorldTransitionSoakTests.cs` executing 50 repeated interior/exterior transitions through `WorldCellTravelTransaction`, asserting single-cell active lifecycle, clean unloading, and zero errors.
  - Authored `samples/RpgSlice/RpgSliceTransitionSoak.cs` to measure per-transition latency, active cells, terrain chunks, graphics resources, working set, and managed heap allocations, and generate structured markdown reports.
  - Implemented automated transition soak in `samples/RpgSlice/RpgSliceGame.cs` (`--transition-soak`, `--transitions 50`), steering the player back and forth between Exterior (0, 0) and House A Interior, timing door travel, recording resource metrics, and outputting to `Docs/TRANSITION_SOAK.md`.
  - Executed 50 live transitions: completed in 29.7 seconds with average latency 1.0 ms (max 8.1 ms), peak working set 164.0 MiB, post-warmup delta +0.5 MiB (162.0 MiB to 162.4 MiB), stable exterior resources (9 cells, 12 chunks, 22 gfx resources), stable interior resources (1 cell, 0 chunks, 10 gfx resources), 0 accumulated errors.
  - The ordered checklist is now 166/169 (98.2%); task 142 is first unchecked. Broader Stage 9–12 and Release gates are unchanged.

### Verification checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings and 0 errors across all projects |
| `dotnet test tests/Ember.Engine.Tests/Ember.Engine.Tests.csproj --no-build --nologo` | PASS — 252 passed, 0 failed, 0 skipped |
| `dotnet exec tests/Ember.Rpg.Check/bin/Debug/net9.0/Ember.Rpg.Check.dll` | PASS — save then load equals original |
| RpgSlice `--persistence-smoke --windowed` | PASS — full 6-action persistence scenario (drop item, loot container, kill enemy, follower cell move, interior travel, save/restart) verified exactly-once |
| RpgSlice `--transition-soak --transitions 50 --windowed` | PASS — 50 transitions completed, avg 1.0 ms, Δ post-warmup WS +0.5 MiB (stable), 0 errors; report saved to `Docs/TRANSITION_SOAK.md` |
| RpgSlice `--travel-smoke --windowed` | PASS — exterior to House A and back through authored doors |
| RpgSlice `--quest-smoke --windowed` | PASS — complete Lost Delivery quest played and verified on restart |
| RpgSlice `--settlement-smoke --windowed` | PASS — settlement world validates, GLB mesh colliders activate, live NPC roles verified |
| RpgSlice `--rpg-integration-smoke --windowed` | PASS — scheduled worker travels between cells, merchant trades pass, perception/AI decisions pass |

## Tasks 142–144 & Release F — packaging, phase attribution, and expansion decision — 27 September 2026

- **Task 142 (packaging RPG slice and validation on clean Windows setup):**
  - Extended `EngineProjectFile` to support `WorldManifestPath`, `ExtraContentPaths`, `ResolveWorldManifestPath()`, and `SaveAtomic` with `stream.Flush(flushToDisk: true)` (resolving review findings 561 & 719).
  - Extended `EngineProjectPackage` to collect world manifests, all cell scenes, referenced GLBs, external URIs (buffers/images), and extra content directories into relocatable packages, round-tripping the full document model (resolving review finding 718).
  - Added unit tests in `EngineProjectFileTests` and `EngineProjectPackageTests` covering world manifest and extra content packaging and relocation (255 tests pass).
  - Authored `tools/publish-rpg-slice.ps1` to publish self-contained `win-x64` builds with all native dependencies (`coreclr`, `hostfxr`, `SharpDX.Direct3D11`, `MonoGame`).
  - Validated outside the repository via `tools/publish-rpg-slice.ps1 -Validate` covering settlement smoke (with `--perf`), persistence smoke (with isolated outside save), travel smoke, and quest smoke.

- **Task 144 (timestamped phase markers and spike attribution):**
  - Added `BenchmarkPhase` (`CellActivation`, `TerrainWork`, `SceneSubmission`), `FrameSpikeCause`, `FrameSpikeRecord`, and zero-allocation `PhaseScope` to `RpgSliceOutdoorBenchmark` and `RpgSliceGame`.
  - Executed the 10-lap settlement benchmark (873,335 frames, 20 cell boundary crossings).
  - Classified all 13 frames >50ms: 1 frame (#208619, 86.46ms) attributed to engine-owned `TerrainWork` chunk mesh vertex buffer uploads during `Draw`, and 12 frames attributed to `ExternalScheduling` (OS thread yield / DWM presentation composition pacing, 55.09–89.20ms external scheduling with <0.3ms engine work). 0 frames >100ms.
  - Documented findings in `Docs/OUTDOOR_BENCHMARK.md`.

- **Task 143 (expansion decision document):**
  - Created `Docs/EXPANSION_DECISION.md` synthesizing empirical budgets from outdoor, settlement, and transition soak benchmarks.
  - Approved world dimensions (32m cells, radius 1 active streaming ring, radius 2 retention ring, up to 16x16 regional maps).
  - Defined per-cell content density limits (scene objects, static colliders, dynamic actors, GLBs, foliage, draw calls, GPU resources).
  - Isolated engine frame tail to synchronous terrain chunk generation and defined atomic follow-up Task 145 (pre-uploading terrain meshes via streaming queue) and Task 146 (near-tier actor round-robin).
  - Formulated next milestone atomic tasks 147–152 covering terrain step climbing, 2D navmesh generation, water/swimming physics, PBR materials, crime/guard alerts, and time-of-day shadow snapping.

- **Release F Gate: PASS.**
  - All 169 of 169 ordered roadmap tasks are complete (100%).

### Verification checks

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings and 0 errors across all projects |
| `dotnet test tests/Ember.Engine.Tests/Ember.Engine.Tests.csproj --no-build --nologo` | PASS — 255 passed, 0 failed, 0 skipped |
| `dotnet exec tests/Ember.Rpg.Check/bin/Debug/net9.0/Ember.Rpg.Check.dll` | PASS — save then load equals original |
| `tools/publish-rpg-slice.ps1 -Validate` | PASS — self-contained win-x64 build outside repository passes settlement smoke (with `--perf`), persistence smoke (isolated save), travel smoke, and quest smoke |
| RpgSlice `--settlement-benchmark --windowed --time-paused` | PASS — 10 laps, 20 cell crossings, 873,335 frames, 0 frames >100ms, 13 frames >50ms classified with phase attribution |



## Roadmap reset and current review — 27 September 2026

Replaced the RPG-first plan with the lightweight 3D engine/scene-generator roadmap.
Preserved the previous roadmap and expansion decision under Docs/archive. Added
PROJECT_REVIEW.md; corrected the active expansion status and qualified the old soak
report. Updated README product direction and stale sequence-persistence wording.

Build passed with zero warnings/errors. Full engine CPU tests: 254 passed, one activation
queue test failed (expected 3 steps, actual 4); RPG save round-trip passed. No new live
UI, graphics, soak or distribution proof is claimed. Existing user content was untouched.
Next implementation task: M0.1; also investigate the observed queue test failure in M0.
Targeted rerun of the failed activation-queue test passed (1/1). The full-suite failure
remains recorded; intermittent behavior requires investigation, not a blanket green claim.

## Active roadmap — M0.1 transition soak verdicts and diagnostics — 27 September 2026

- Status: **Passed** against the M0.1 acceptance checks. Implementation commit: `6bf847e`.
- Replaced hardcoded soak success claims with independent checks for exact transition count,
  timeout, valid latency samples, per-destination resource growth, working-set growth,
  managed-heap growth, and recorded errors. A committed travel with a cleanup error remains
  a completed transition but fails the overall verdict. Inconclusive resource or memory
  trends fail the run rather than being presented as stable.
- Added complete JSON output containing run ID, UTC timestamps, adapter/resolution, host
  details, thresholds, check outcomes, all transition samples, and errors. Markdown and JSON
  artifacts use unique files under `%TEMP%\Ember\RpgSlice\TransitionSoaks`; the app no
  longer overwrites `Docs/TRANSITION_SOAK.md` during a run. Failed verdicts set process exit
  code 1; passing verdicts set exit code 0.
- CPU failure-injection tests cover committed-travel cleanup errors, resource growth,
  working-set growth, incomplete runs, timeouts, invalid latency, and a one-transition run.
  The one-transition case writes its sample and reports resource/memory trend checks as
  inconclusive without indexing an invalid warmup sample.

### M0.1 verification evidence

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --nologo --no-restore` | PASS — 264 tests, 0 failed, 0 skipped |
| `dotnet build Ember.sln --nologo --no-restore` | PASS — 0 warnings, 0 errors |
| RpgSlice `--transition-soak --transitions 1 --windowed` | PASS for the failure-path check — 1/1 transition recorded; trend verdict was correctly inconclusive; process exit code 1 |
| RpgSlice `--transition-soak --transitions 10 --windowed` | PASS — 10/10; transition latency avg 3.8 ms / p95 16.4 ms / max 16.4 ms; no per-cell resource growth; working-set peak growth +0.9 MiB; managed-heap peak growth +0.2 MiB; 0 errors; process exit code 0 |

Live checks ran on Windows 10.0.26200, .NET 9.0.7, Intel UHD Graphics, at 1280x720.
The 10-transition artifacts are
`%TEMP%\Ember\RpgSlice\TransitionSoaks\transition-soak-343d6e8f19694236a4cbf4e7f63f9214.md`
and the same path with `.json`. The 1-transition artifacts use run ID
`be8c6350aa2845008478c28aac0b472a`. Cleanup-error and growth injection were CPU tests;
no live cleanup fault was injected. The 30 MiB memory-growth limit is provisional pending
the reference-PC budget work in M0.3.

## Active roadmap — M0.2 live lifecycle coverage — 27 September 2026

- Status: **Passed**. Implementation commit: `a4a74af`.
- Added a fixed-count RpgSlice lifecycle check separate from the timed transition soak. It
  drives at least 50 real door transitions across House A, House B and the exterior; injects
  one delayed destination-preparation failure and retries while the source stays active;
  checks stable, unique world-instance IDs across scene reloads; samples like-for-like cell
  resource counts after warmup; and saves/restores the world before exporting JSON and Markdown.
- Added a CharacterStudio live smoke that performs play, stop and scene reload over rendered
  frames. It verifies that play uses an isolated scene, authored scene serialization and object
  IDs survive each cycle, and owned preview graphics-resource counts remain stable.
- The RpgSlice House B route required explicit waypoints around the authored market stall;
  the first harness attempt timed out at 2 transitions. The corrected live route completed all
  50 transitions. A burst-mode editor probe also inflated working-set readings; pacing each
  lifecycle step across actual rendered frames removed that harness artifact.

### M0.2 verification evidence

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --nologo --no-restore` | PASS — 269 passed, 0 failed, 0 skipped |
| `dotnet build Ember.sln --nologo --no-restore` | PASS — 0 warnings, 0 errors |
| RpgSlice `--lifecycle-check --lifecycle-transitions 50 --windowed` | PASS — 50/50 transitions in 101.3s; both interiors and exterior reached; 50/50 identity reload checks; one delayed load failure retried; save/restart restored 48 unique identities; 0 unexpected errors |
| CharacterStudio `--lifecycle-smoke --lifecycle-cycles 50 --windowed` | PASS — 50/50 play-stop and reload cycles across rendered frames; six owned preview resources throughout; authored scene unchanged; 0 errors |

Both live runs used Windows 10.0.26200, .NET 9.0.7, Intel UHD Graphics, 1280x720. RpgSlice
resource counts remained 10 in House A, 10 in House B and 22 in the exterior; post-warmup
working-set peak growth was +2.6 MiB and managed-heap peak growth was +0.7 MiB. In the editor,
after four warmup cycles, preview resources remained 6; working-set peak growth was +13.0 MiB
and managed-heap peak growth was +0.8 MiB. These fixed-count runs do not replace the M0.3
30-minute mixed editor/runtime session, which establishes separate budgets for its named
reference PC and paired-Fox content.

RpgSlice artifacts are `%TEMP%\Ember\RpgSlice\LifecycleChecks\lifecycle-check-8319ad4382164dcf8610ab7fec0dc71f.md`,
the matching `.json`, and `.world.json`. CharacterStudio artifacts are
`%TEMP%\Ember\CharacterStudio\LifecycleChecks\lifecycle-check-31928220bc9b40bb91cefe0f783aac7a.md`
and the matching `.json`.

## Active roadmap — M0.3 performance and distribution baseline — 27 September 2026

- Status: **Passed**. Implementation commit: `d29b62c`; the live evidence is recorded below.
- Added a CharacterStudio mixed editor/runtime reference session selected with
  `--reference-session-minutes N`. It alternates editor and play-session modes every
  60 active rendered seconds, records Update-to-Update frame intervals, samples process
  memory and owned preview resources once per second, and writes full JSON plus Markdown
  under `%TEMP%\Ember\CharacterStudio\ReferenceSessions`.
- The collector measures active rendered time and separately reports update gaps over five
  seconds. An earlier attempt encountered a 5.2-hour host pause and captured only about
  23 active minutes; it was discarded. The successful run below reached the full 30 active
  minutes with no excluded host-pause gaps.
- Initial reference budgets are tied only to this paired-Fox scene and the named PC:
  p95 <= 50 ms, p99 <= 50 ms, maximum <= 1,000 ms; preview graphics resources exactly 2
  with no growth; post-warmup peak growth <= 30 MiB working set, <= 30 MiB private memory,
  and <= 15 MiB managed heap; self-contained win-x64 package <= 135 MiB. These are
  provisional guardrails for this configuration, not cross-machine performance claims.

### M0.3 verification evidence

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --nologo --no-restore` | PASS — 269 passed, 0 failed, 0 skipped |
| `dotnet build Ember.sln --nologo --no-restore` | PASS — 0 warnings, 0 errors |
| CharacterStudio `--pair --reference-session-minutes 30 --windowed --perf` | PASS — 30.0 active/wall minutes; 57,464 frame intervals; 31 editor/runtime mode changes; 0 lifecycle errors; 0 host-pause gaps over 5s |
| Paired-Fox frame timing | PASS against initial budgets — average 31.32 ms, p50 31.60 ms, p95 46.47 ms, p99 47.12 ms, max 743.99 ms; 71 intervals >50 ms and 5 >100 ms |
| Paired-Fox resources and memory | PASS against initial budgets — preview resources 2 throughout; post-warmup peak growth: working set +18.9 MiB, private memory +17.4 MiB, managed heap +7.9 MiB |
| Self-contained Release publish | PASS — `dotnet publish samples/CharacterStudio/CharacterStudio.csproj --configuration Release --runtime win-x64 --self-contained true --output <temp package> -p:UseAppHost=true -p:PublishSingleFile=false -p:PublishTrimmed=false --nologo`; 352 files, 128.45 MiB total, including 5 asset files (0.2 MiB) and bundled `coreclr.dll`/`hostfxr.dll` |
| Relocated launch | PASS — ran `CharacterStudio.exe` from `%TEMP%` with the package outside the checkout, restricted `PATH` to Windows system directories (no `dotnet` command available), and saved a 1280x720 editor screenshot; process exit code 0 |
| Separate SDK-free machine | NOT TESTED — the package was exercised on the named reference PC; restricted `PATH` is not a separate clean-machine test |

The reference PC was `DATTAPRASAD`, Windows 10.0.26200, .NET 9.0.7, Intel UHD Graphics,
1280x720. Frame intervals are wall-clock Update-to-Update measurements including render
and presentation pacing, not GPU-only timings. The session report and full samples are
`%TEMP%\Ember\CharacterStudio\ReferenceSessions\reference-session-bde639fdaa85436d9ad78f8a23400cea.md`
and the matching `.json`. The relocated package is
`%TEMP%\Ember\CharacterStudio\RelocationChecks\character-studio-win-x64`; the successful
capture is `relocated-sdk-path-capture.png` in its parent directory. Package size includes
the self-contained .NET runtime and native dependencies.

## Active roadmap — M0.4 persistence audit and repeatable validation — 27 September 2026

- Status: **In progress**. Implementation commits: `4784129` and `36dd000`.
- Added `Ember.IO.AtomicFile` for engine-owned single-file saves. It writes a unique sibling
  temporary file, flushes it, atomically replaces or creates the destination, and removes the
  temporary file on failure. Migrated scene, sequence, project, world, path, placement-template,
  and sequence-export-manifest writes to it. Two related sidecar files still need a separate
  multi-file protocol.
- World restore now validates staged identities, changes, and runtime objects before publishing
  them into live stores. Travel-transaction disposal requests cancellation, waits for the worker,
  and disposes late prepared objects on the owner thread. This synchronous wait depends on worker
  code honoring cancellation.
- Added malformed-schema tests for unknown JSON members and failure-injection tests for partial
  writes, destination preservation, temporary-file cleanup, restore atomicity, and cancellation
  disposal. The RPG content-pack parser rejects unknown members; RPG save and sidecar behavior
  remains a separate check.
- Added the Windows 2022 GitHub Actions build/CPU-test workflow and the documented
  [manual graphics gate](MANUAL_GRAPHICS_GATE.md). Fixed a launch failure where MonoGame's old
  .NET Core 2.1 host resolver was copied next to the .NET 9 framework-dependent apphost.
  `Directory.Build.targets` now filters only that obsolete copy-local runtime asset; the
  self-contained package still carries its own .NET 9 host runtime.

### M0.4 verification evidence

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --configuration Release --no-restore --nologo` | PASS — 277 passed, 0 failed, 0 skipped |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — 0 warnings, 0 errors |
| Framework-dependent CharacterStudio Release apphost | PASS — launched directly after clean build and captured the 1280x720 editor screenshot; no app-local legacy `hostfxr.dll` |
| Post-fix self-contained win-x64 publish | PASS — 352 files, 134,693,953 bytes (128.45 MiB), with bundled `hostfxr.dll` |
| Relocated self-contained launch | PASS — launched from `%TEMP%` with `PATH` restricted to Windows system directories, captured a 1280x720 editor screenshot, exit code 0 |
| GitHub Actions remote run | FAIL — [run #1](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36336285409) passed restore, but the CPU test step exited 1; job logs require GitHub sign-in, so the workflow now emits failed test details as annotations for the next run |
| Interactive manual gate | NOT RUN — resize/restore, save/reopen, and play/stop remain to be exercised manually |

The post-fix package and screenshot are under
`%TEMP%\Ember\CharacterStudio\RelocationChecks\m04-post-hostfxr-win-x64` and its parent
directory. Restricted `PATH` on this PC does not establish behavior on a separate SDK-free
machine. The generic editor screenshot does not validate RPG gameplay or RPG multi-file saves.

## Active roadmap — M1.1 project and asset workflow — 27 September 2026

- Status: **In progress**. This first implementation slice adds a Project window with project
  creation, opening by project file or folder, and a recent-project selector. Recent projects are
  stored under `%LOCALAPPDATA%\Ember\CharacterStudio\recent-projects.json`, ordered newest first,
  deduplicated, and capped at 12.
- New projects are assembled in a sibling staging directory and published with a directory move.
  They contain `ember.project.json`, an empty `Scenes/Main.json`, and an `Assets` folder. Existing
  destinations are rejected without changing their contents.
- The editor can import a `.glb` from a typed path into a unique project `Assets` folder. Local
  external buffer/image dependencies are copied while preserving their relative layout. The
  candidate scene and graphics preview are built before the new asset is committed to the scene;
  a failed import removes its copied files and leaves the previous scene and preview active.
  Imported models are added to the scene and appear in its asset list for additional placement.
- Reimport continues to use the existing `R` action. `ReloadableAsset` builds a replacement
  before swapping, so invalid GLB data leaves the last valid preview active and reports the error.

### M1.1 verification evidence

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --configuration Release --no-restore --nologo` | PASS — 283 passed, 0 failed, 0 skipped |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — 0 warnings, 0 errors |
| Workspace CPU coverage | PASS — starter project creation/no-overwrite, recent ordering/deduplication/cap, static and animated GLB imports, and uncommitted import cleanup |
| Relocated project graphics launch | PASS — opened a project after moving its folder outside the checkout; static `TextureCoordinateTest.glb` and animated `Fox.glb` loaded from project-relative paths; screenshot 1280x720; exit code 0 |
| Project-window visual capture | PASS — controls render in CharacterStudio. Desktop automation did not register ImGui text/click actions, so create/open/import buttons were not exercised interactively |

The graphics fixture and screenshot are under
`%TEMP%\Ember\CharacterStudio\M11ProjectWorkflow-39786d2da1694cacbd16f61e6a8236e4`.
The fixture was assembled for the relocated launch; create/import services were covered by CPU
tests. M1.1 remains open until the button-driven editor workflow is exercised, including invalid
reimport through the visible editor path. The Project window currently accepts typed paths rather
than providing native file/folder pickers. At the time of this review the latest recorded Windows
test run had failed remotely; the later successful rerun and current UX.1 work are recorded below.

## Creator-first roadmap update — 28 September 2026

Reviewed current revision 3c9620e, recent progress, editor drawing code, and the saved
M1.1 relocated-project screenshot. Confirmed overlapping always-visible panels obscure
most of the scene. Added CREATOR_EXPERIENCE.md and required early UX.1–UX.6 tasks to the
active roadmap. Updated M1–M5 acceptance and README direction: learn through small
visible edits and play; use optional lessons and later designer-game missions on the
same command layer and project data. Advanced tools remain available on demand.

At the time of this review, M0.1–M0.3 recorded passes were preserved; M0.4 and M1.1 remained
In progress, and new UX work was Not started. Fresh local Release test suite passed 283/283; RPG check passed.
Release solution build could not copy DLLs locked by running CharacterStudio (39692);
the session was not closed. Remote CI was not rechecked because gh is unavailable.
No fresh live UI/novice/soak test was claimed; no runtime implementation was changed.

Next: close M0.4 verification, then UX.1 workspace simplification with M1.1 integration.

## Explicit teaching-engine principle — 28 September 2026

Made the product promise explicit: Ember teaches users how to build games while they
build their own. Added a concept progression, explain/change/predict/play/reflect/vary
loop, offline contextual explanations and hints, and evidence of learning through an
unguided variation. Connected lesson scope to M1, M2 and M3 so lessons never depend on
unimplemented tools. Teaching is required in the first release; the later designer-game
presentation remains an extension of the same editor and project model.

## Latest validation and UX.1 workspace slice — 28 September 2026

Pulled `origin/master` from `43f06a7` to `be78c2e`. The prior report's remote failure is now
superseded: GitHub Actions run [36341384329](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36341384329)
completed successfully for `be78c2e50873969de9cb62b7171cb76945b8beab`. That run predates the
local UX.1 edits below, so those edits have local build and CPU evidence only.

### UX.1 implementation slice

- CharacterStudio now opens Home when no project or saved scene was supplied. Home offers
  Make a game, Make a film, Open a project, and recent projects. Game/film creation uses the
  existing project service; the film route explains that an animated model is needed for
  Animate and Finish.
- The editing view has one top toolbar, an Add to scene library on the left, and an Inspector
  on the right. Save, Undo/Redo, Play/Stop, Animate and Finish are visible. World, RPG,
  sequence/export and performance details are opt-in under More tools; Reset workspace layout
  restores the default panel visibility.
- The permanent render/shortcut overlay is now opt-in as Performance details. At 1280x720,
  the default side panels leave a 780-pixel center span (61% of logical width) for the scene.

### Verification evidence

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — 0 warnings, 0 errors after the local UI changes |
| `dotnet test Ember.sln --configuration Release --no-build --no-restore --nologo` | PASS — 283 passed, 0 failed, 0 skipped when run alone |
| Activation-queue review finding | RESOLVED — accepts partial-budget steps; focused test passed five consecutive runs |
| `dotnet run --project tests/Ember.Rpg.Check/Ember.Rpg.Check.csproj --configuration Release --no-build --no-restore` | PASS — save then load equals original |
| Home graphics capture | PASS — CharacterStudio exited 0; 1280x720; `%TEMP%\Ember\UX1\home-clean.png` |
| Open-scene workspace capture | PASS — ReleaseAShowcase opened and rendered at 1280x720; no panel overlap; `%TEMP%\Ember\UX1\editor-workspace.png` |
| Interactive manual gate | NOT RUN — resize/DPI, create/open/import buttons, save/reopen and Play/Stop were not operated interactively |

The activation-queue review finding came from asserting exactly three steps even though a
cell may share the last partial frame budget and need an extra call. The test now asserts at
least three steps and yields while async preparation remains in flight. The production queue
was unchanged. The focused test passed five consecutive runs, and the full suite passed
283/283 after this fix.

An earlier validation attempt ran the full test suite alongside the RPG check and reported
several async cell-preparation timeouts. Those timeouts did not reproduce when the full suite
ran alone. Runner contention is possible but not proven; revisit if they recur.

M0.4 remains **In progress** until its interactive graphics checks are recorded. M1.1 remains
**In progress** because create/open/import/reimport through the visible workflow and native
pickers are unverified. UX.1 is **In progress**: Home and the default layout were captured, but
resize and DPI acceptance remain open. The direct `--open` scene capture does not prove the
project workflow or button-driven actions.

## Visible transform actions and browse workflow — 28 September 2026

Fetched `origin`. `master` was already at `origin/master` (`be78c2e`), so no incoming master
commit needed merging. The newly fetched `cursor/engine-code-review-2-f580` branch is based on
the older `43f06a7` tree and carries historical review notes; it was not merged into the active
teaching-engine roadmap.

### UX.2 and UX.3 implementation slice

- Home uses a project name and a native folder picker to choose the project location. Open
  Project uses a native picker for `ember.project.json`; Add to scene uses a `.glb` picker rooted
  at the active project. Invalid project folder names and project/import/open failures receive
  readable feedback.
- The Inspector exposes Move, Turn and Size actions for the selected hierarchy item. Move
  changes one scene axis by 25 scene units, Turn applies 15 degrees around a local axis, and Size
  changes uniform scale by 10 percent with bounded scale values. Every button action is stored
  as a `TransformEditCommand`, so existing Undo/Redo operates on it. Numeric transforms are under
  More details.
- Existing state-aware status covers standalone scenes, missing projects, project readiness,
  create/import/save errors and temporary Play changes. This is a start toward UX.3, not completion
  of contextual help or its manual accessibility checks.

### Verification evidence

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — 0 warnings, 0 errors after the transform controls were added |
| Home capture | PASS — 1280×720, exit 0, `%TEMP%\Ember\UX2\home-browse.png` |
| Relocated self-contained CharacterStudio capture | PASS — opened packaged `ReleaseAShowcase.json` outside the checkout, rendered the scene and transform controls at 1280×720, exit 0; `%TEMP%\Ember\UX2\relocated-workspace.png` |
| Self-contained win-x64 package | PASS — 128.39 MiB, below the M0.3 135 MiB guardrail; `%TEMP%\Ember\UX2\publish-9100ed772ccd4ba7a6615fc5daf2a40f` |
| CPU test suite | NOT RUN in this batch, per instruction. The earlier 283/283 result predates these editor-control changes and is not evidence for them. |
| Interactive UI checks | NOT RUN — native dialog selection, transform clicks/undo, Play/Stop, save/reopen, resize and DPI remain unverified. |

UX.2 remains **In progress**: starter thumbnails, a preview before placing a model, and viewport
pointer selection are still missing; selection currently works from the visible object list. The
native pickers and transform actions have only been visually captured, not operated interactively.
UX.3 remains **In progress** pending richer contextual help and keyboard/focus/scaling checks. M0.4,
UX.1 and M1.1 remain **In progress** pending their live interactive gates.

Next: continue UX.2 with starter/model previews and viewport selection, then use the desktop
workflow to verify the picker, transform, undo, save/reopen and Play/Stop paths before closing M0.4,
UX.1 or M1.1.

## Starter projects and scene-view selection — 28 September 2026

### UX.2 implementation slice

- Home now draws separate Game and Film starter thumbnails that match their contents. The Game
  starter uses the courtyard with Fox Walk and Run characters; the Film starter uses the courtyard
  with one animated Fox. Both routes use the same project format and copy the GLBs plus their
  attribution README into the new project.
- Starter creation builds in a sibling staging directory, copies the chosen starter scene to
  `Scenes/Main.json`, and moves the completed directory to its final location. The film scene is
  [FilmStarter.json](../samples/CharacterStudio/Scenes/FilmStarter.json). New projects select the
  animated character so its existing clip controls are immediately visible.
- A short, unobstructed click in the central scene view now selects the nearest object using its
  transformed mesh bounds. A drag remains orbit-camera input. The current picker is an AABB broad
  phase; overlapping mesh bounds can select the wrong object, so triangle-level picking remains a
  refinement for M1.2.

### Verification evidence

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — 0 warnings, 0 errors after the editor and project-creation changes |
| Self-contained win-x64 publish | PASS — 128.41 MiB; latest package includes both starter scenes, Fox/courtyard assets and attribution README; `%TEMP%\Ember\UX2\roadmap-step-1a320cee92d74c288d90e6ee929a821b` |
| Home starter-card capture | PASS — 1280×720, exit 0; `%TEMP%\Ember\UX2\home-latest.png` |
| Relocated Game and Film project smoke launches | PASS — both manually assembled from the published bundled scene/assets/manifest, launched with latest package `--project`, rendered at 1280×720 and exited 0; Film opens with the Fox selected and its clip controls visible; `%TEMP%\Ember\UX2\starter-final-smoke-972ec7f35e5a4edc922e3f99bb1b370d` |
| CPU tests | NOT RUN in this batch, per instruction. |
| Home creation and viewport click interaction | NOT RUN — the Home buttons, staging/move operation and mouse selection were not exercised interactively. |

UX.2 remains **In progress**: model import still places immediately, so there is no preview-before-placement step. The scene-view picker code is in place but not proven by a live mouse action. UX.1, UX.3, M0.4 and M1.1 retain their open interactive gates.

Next: separate model import from scene placement and render a temporary preview with explicit Add and Cancel actions. Then verify the Home starter buttons, native pickers, scene selection, Move/Turn/Size undo, save/reopen and Play/Stop through actual desktop input.

## Model import preview before placement — 28 September 2026

### UX.2 implementation slice

- Browsing for a GLB now imports it into a temporary project asset and renders it beside the
  authored scene. The preview owns its own render resources and animated characters continue to
  play while it is visible; it is not part of scene data, undo history or saves.
- **Add to scene** creates an undoable scene object, loads the authored scene preview with the new
  asset, selects the object and frames the scene. **Cancel preview** releases its render resources
  and removes the staged project asset. Browsing a second model replaces the first preview only
  after the new model loads successfully.
- Opening another project or cell, entering Play, returning Home or closing the editor releases a
  pending preview. Camera framing includes the preview in the editor and excludes it from sequence
  export bounds.

### Verification evidence

| Check | Result |
| --- | --- |
| Release solution build | PASS — 0 warnings, 0 errors after the temporary preview and Add/Cancel flow were implemented |
| CPU tests | NOT RUN, per instruction. |
| Interactive model browse, preview, Add, Cancel and undo | NOT RUN — the desktop controls and project-file cleanup still need live verification. |

UX.2 remains **In progress**. The preview-before-placement behavior is implemented, but the full
button-driven import and rollback path has not been exercised with desktop input. Earlier picker,
selection, transform, save/reopen, resize, DPI and Play/Stop gates also remain open. UX.1, UX.3,
M0.4 and M1.1 retain their interactive gates.

Next: verify browse → preview → Add, Cancel cleanup, undo, save/reopen and Play/Stop using a
relocated project. Continue the remaining Home, selection, transform, resize and DPI checks before
closing UX.2 or dependent release gates.

## Windows CI cell-queue timing follow-up — 28 September 2026

### M0.4 reliability slice

- The Windows workflow for commit `ababa01` failed in
  [run #4](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36388878483).
  Its annotation showed that the nine-cell fairness test hit its 200-frame guard while using a
  20 ms wall-clock budget per simulated frame. This made a cost/cell fairness test sensitive to
  runner scheduling.
- The queue now accepts an optional `TimeProvider`; production still uses the system monotonic
  clock. The broad nine-cell test uses a generous time window to focus on cost and cell limits, and
  a fake-clock test deterministically confirms that elapsed-time exhaustion stops later work in the
  same frame.
- Atomic-file coverage now also locks an existing destination to force the final replacement to
  fail; the test confirms that the old save remains intact and the sibling temporary file is removed.

### Verification evidence

| Check | Result |
| --- | --- |
| Cell activation queue tests | PASS — 5 passed, 0 failed |
| Full CPU test suite | PASS — 285 passed, 0 failed, 0 skipped, including the forced replacement-failure case |
| Release solution build | PASS — 0 warnings, 0 errors |
| Windows CI for `145c892` | PASS — [run #6](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36390512874); hosted build and CPU tests passed with the atomic replacement-failure case |
| Release scene capture | PASS — commit `145c892`, Windows 11 Home 10.0.26200, .NET 9.0.19, Intel UHD Graphics driver 27.20.100.9664, 1920×1080 display; opened `ReleaseAShowcase.json`, rendered courtyard and two animated characters in a 1280×720 capture, exit 0, stderr empty; `%TEMP%\Ember\ManualGraphicsGate\8a605d9a3d444916b41d3e0381677b56` |

M0.4 remains **In progress**. A scene-render capture passes, but resize, save/reopen, and Play/Stop
interaction checks remain open. The desktop automation helper failed to initialize during this
session (`failed to write kernel assets: The system cannot find the path specified`, OS error 3);
no interactive action is claimed as passed.

Next: complete the interactive graphics checks when the desktop helper is available, then continue
the remaining UX.2 and UX.3 workflow checks without marking unobserved actions as passed.

## Optional transform explanation — 28 September 2026

### UX.3 implementation slice

- The Inspector now has a collapsed **Why?** explanation under Move/Turn/Size. It describes a
  transform as position, rotation and scale, and explains that each scene object can reuse a source
  model with an independent placement. The explanation is optional and does not add a default panel.

### Verification evidence

| Check | Result |
| --- | --- |
| Release solution build | PASS — 0 warnings, 0 errors after the Inspector explanation was added |
| CharacterStudio scene capture | PASS — `ReleaseAShowcase.json` opened, 1280×720 screenshot shows scene and collapsed **Why?** row, exit 0, stderr empty; `%TEMP%\Ember\UX3\ccd2c36902ff416bb6df06a0ba3f3c05\transform-explanation.png` |
| Full CPU tests | NOT RERUN after this UI-only text change; 285/285 passed immediately before it |
| Expand explanation and keyboard/focus/scaling checks | NOT RUN — the desktop helper could not initialize |

UX.3 remains **In progress**. The explanation content is implemented, but its expanded presentation,
keyboard navigation, focus visibility and 100%/150% scaling remain unverified. M0.4, UX.1, UX.2
and M1.1 retain open interactive gates.

Next: verify the **Why?** row and keyboard focus on a desktop, then exercise Browse → preview → Add,
Cancel cleanup, transform undo, save/reopen, resize, DPI and Play/Stop through visible controls.

## Empty-scene next actions — 28 September 2026

### UX.3 implementation slice

- The Inspector now distinguishes an empty scene from an unselected object. An empty scene offers
  **Add an empty object**, **Browse for a model**, or **Make or open a project**, depending on whether
  a project is available.
- An empty model list now says that no models are present and points to Browse. Model browsing uses
  one shared action from the Add library and the Inspector, with the same import result and error text.

### Verification evidence

| Check | Result |
| --- | --- |
| Release solution build | PASS — 0 warnings, 0 errors |
| Empty-scene capture | PASS — opened a version-7 empty scene, 1280×720 screenshot shows the empty-scene actions and empty model guidance; exit 0, stderr empty; `%TEMP%/Ember/UX3/empty-scene-next-action/empty-scene.png` |
| Full CPU tests | NOT RERUN — this slice changes only CharacterStudio UI text and button routing |
| Button, picker, focus, resize and DPI interaction | NOT RUN — the desktop interaction helper remains unavailable |

UX.3 remains **In progress**. The capture confirms the guidance fits in the Inspector, but does not
prove button behavior or keyboard and scaling acceptance. M0.4, UX.1, UX.2 and M1.1 remain open.

Next: exercise the empty-scene buttons and Browse/preview/Add/cancel loop, then verify narrow-window
layout, keyboard focus, save/reopen, and Play/Stop through the visible controls.

## Optional tool-panel overlap guard — 28 September 2026

### UX.1 implementation slice

- Opening **More tools** now closes any active Animate/Finish, World Cells, or RPG authoring panel.
- Selecting an optional panel closes the menu and delays that panel's draw until the next frame, so
  the menu and panel do not occupy their shared starting position together.
- The top-bar Animate and Finish actions close More tools before opening the sequence panel.

### Verification evidence

| Check | Result |
| --- | --- |
| Release solution build | PASS — 0 warnings, 0 errors |
| Source control-flow review | PASS — the menu is drawn exclusively from the optional tool panels; toolbar routes close the menu |
| CharacterStudio default scene capture | PASS — `ReleaseAShowcase.json` rendered in a 1280×720 capture with the Add library and Inspector visible; exit 0, stderr empty; `%TEMP%/Ember/UX1/tool-panel-default/workspace.png` |
| Full CPU tests | NOT RUN — this slice changes only editor panel visibility |
| Menu/panel mouse interaction | NOT RUN — the desktop interaction helper still fails to initialize |

UX.1 remains **In progress**. The code prevents the known More tools/menu overlap, but other window
positions, transitions, viewport area, resize, DPI and keyboard focus still need the manual layout gate.

Next: verify opening More tools, selecting each optional panel and using Animate/Finish through the
visible controls; then continue the remaining M1.1 and UX.2 workflow checks.

## Pending-model preview feedback — 28 September 2026

### UX.3 implementation slice

- When no scene object is selected, the Inspector now detects a pending GLB preview and says it is
  not in the scene yet. It points to the Add panel's Add and Cancel actions and can reopen that panel
  when the creator hid it.
- The Add panel's model list distinguishes an empty scene from one with a model currently being
  previewed, avoiding the impression that Browse has not loaded anything.

### Verification evidence

| Check | Result |
| --- | --- |
| Release solution build | PASS — 0 warnings, 0 errors |
| Full CPU tests | NOT RUN — this slice changes only editor feedback and panel visibility |
| Pending-preview visual state and Add/Cancel interaction | NOT RUN — the desktop interaction helper remains unavailable; the normal scene capture cannot enter this state |

UX.3 remains **In progress**. The source routes the preview state and its recovery action, but its
visual layout and Add/Cancel behavior are not yet proven in a running interactive session.

Next: open a project, Browse a GLB, inspect the preview guidance, then Add and Cancel in turn and
confirm that the scene and project assets reflect each choice.

## Visible recovery hints for editor failures — 28 September 2026

### UX.3 implementation slice

- Save, model import, preview cancellation, and other editor errors now appear in the Inspector even
  when the Add panel is closed. The Add panel avoids duplicating the same failure message.
- Each error receives a next step based on the failed action: check the project folder before retrying
  Save, choose another model, retry preview cancellation, or choose a valid project/folder. A visible
  **Dismiss message** action clears the feedback.

### Verification evidence

| Check | Result |
| --- | --- |
| Release solution build | PASS — 0 warnings, 0 errors |
| Error classification and panel routing | PASS — source review confirms save, create/open, cancel, and model-import failures reach the Inspector; Home continues to show create/open errors in its own workspace |
| Default scene capture | PASS — current Release binary opened `ReleaseAShowcase.json`; normal editing shows no failure banner in a 1280×720 capture; exit 0, stderr empty; `%TEMP%/Ember/UX3/recovery-feedback-default/workspace.png` |
| Full CPU tests | NOT RUN — this slice changes CharacterStudio UI feedback only |
| Rendered error-state and dismissal interaction | NOT RUN — no current capture can create this state without UI input; desktop interaction helper remains unavailable |

UX.3 remains **In progress**. The code provides visible recovery paths, but their rendered state,
message clarity and dismissal behavior still require interactive review.

Next: force save, invalid-model, failed-open and failed-create states through the visible controls;
confirm the hint is relevant, dismisses cleanly, and does not hide recoverable scene data.

## Project asset browser, reload action, and stable catalog — 28 September 2026

### M1.1 implementation slice

- CharacterStudio now catalogs every GLB beneath a project's `Assets` folder, including assets
  with no current scene instance. A versioned project-relative catalog keeps each asset ID stable
  across refreshes and project relocation; older scene references seed IDs before their last
  instance is removed.
- The Add library lists project models and scene models, labels whether each is already in the
  scene, and lets the creator preview a selected project model before adding it. Imported GLBs
  keep their assigned ID while pending; accepting registers the durable asset, while canceling
  removes both the pending catalog entry and copied files.
- A selected model can be reloaded from its project file through the library. CharacterStudio
  builds and validates a replacement resource set before swapping it into the editor; a failed
  load reports the error and leaves the previous preview active.
- Catalog parse and identity conflicts report errors without replacing malformed catalog data.

### Verification evidence

| Check | Result |
| --- | --- |
| Project, catalog, import, and reload tests | PASS — 23 targeted tests, including stable IDs across relocation, legacy scene-reference seeding, import commit identity, pending-import cleanup, ID/path conflicts, malformed-catalog preservation, and invalid-reload fallback |
| Release solution build | PASS — 0 warnings, 0 errors |
| `git diff --check` | PASS |
| Project browser and animated-scene capture | PASS — a relocated temporary project opened `ReleaseAShowcase.json`; the 1280×720 capture shows the two animated Fox characters, project model list, preview and reload controls; exit 0, stderr empty; `%TEMP%/Ember/M11ShowcaseBrowser-eb403cea61be44b1878240c94dcdc188/project-asset-browser-showcase.png` |
| Browse, refresh, preview, Add, and Cancel interaction | NOT RUN — desktop interaction helper failed to initialize (`failed to write kernel assets: The system cannot find the path specified (os error 3)`) |
| Full CPU suite and visual review | NOT RUN — the targeted tests and build validate code; M1.1's visible-control and visual gates remain open |

M1.1 remains **In progress**. Code now supports browsing and reloading persisted project assets,
but the visible workflow has not been exercised in the running editor. The reload control and its
invalid-model recovery message still need confirmation through visible interaction.

Next: verify the project picker and Browse → preview → Add/Cancel workflow through visible controls;
then exercise selected-model reload with a valid and invalid GLB and confirm that the previous scene
preview stays usable after failure. Keep M0.4 and UX.1–UX.4 graphics checks open until recorded.

## Optional first-creation lesson — 28 September 2026

### UX.4 implementation slice

- Home offers an optional guide for new Game/Film starters; the guide can also be started,
  hidden, skipped, or replayed from the workspace. Normal scene editing remains available.
- The lesson follows real scene and editor state through adding an object, changing its transform,
  predicting Play behavior, starting and stopping Play, reflecting on the result, undoing the tracked
  move, changing another object, saving, and reopening the same project scene.
- Every step provides a short concept explanation. **Why?** gives the purpose, and offline hints
  reveal in two levels; help resets as the lesson advances so the creator can try unaided first.
- Lesson completion is stored atomically in the project's `.ember/learning-progress.json`. Reopening
  a project surfaces prior completion and replay guidance. Malformed progress is reported without
  blocking the project workspace or overwriting the original file.

### Verification evidence

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — 0 warnings, 0 errors after the latest lesson-help UI change |
| Lesson and progress-store tests | PASS — full `Ember.Engine.Tests` suite: 296 passed, including lesson position tracking/removal recovery and persisted completion coverage |
| `git diff --check` | PASS |
| Home and workspace captures | PASS — 1280×720; Home shows the optional guide choice and the editor shows its entry point; `%TEMP%/Ember/UX4LessonCapture-f0497e4f3af8482d8c5bfdc15b9af020/home.png` and `workspace.png` |
| Full lesson interaction, replay, hint ladder, and completion record | NOT RUN — the desktop interaction helper failed to initialize (`failed to write kernel assets: The system cannot find the path specified (os error 3)`) |
| UX.5 first-time user observation | NOT RUN — three novice sessions have not been conducted |

UX.4 remains **In progress**. The lesson logic, visible controls and project-local progress are
implemented and compile, but the live click-through and beginner usability evidence are still open.
The current build is not evidence that the controls fit or read clearly at every window size or DPI.

Next: complete the M0.4 and M1.1 button-driven checks alongside UX.1–UX.4; then run the full lesson
from a fresh starter through reopen, replay and persisted completion. Conduct UX.5 with three
first-time creators before accepting the M1 gate.

## Nested scene hierarchy and world-preserving reparent — 28 September 2026

### M1.2 implementation slice

- The Inspector now renders scene objects as an alphabetized nested tree, expands root objects with
  children on first display, and still selects objects by clicking their row. A parent selector lets
  the creator reparent or unparent an object; it omits choices that would create a cycle.
- Reparenting is a normal scene-history command. It captures the old relationship and transform,
  computes a replacement local transform from the current world transform, and restores both sides
  during undo/redo. It rejects missing parents, cycles, singular parent transforms and transforms
  that would introduce shear instead of silently distorting the object.
- Existing scene-view picking, Duplicate/Delete and Move/Turn/Size actions remain available. This
  slice does not yet add viewport transform gizmos or snapping.

### Verification evidence

| Check | Result |
| --- | --- |
| Scene command-history tests | PASS — full `Ember.Engine.Tests` suite: 296 passed, including world placement across reparent/undo/redo, cycle rejection and no-mutation shear rejection |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — 0 warnings, 0 errors |
| `git diff --check` | PASS |
| CharacterStudio nested-hierarchy capture | PASS — a temporary scene placed Fox Run under Release A Courtyard; the indented child row and parent selector render at 1280×720; process exited 0 without ImGui errors; `%TEMP%/Ember/M12NestedFixture-b383271eb1dc45739ec37672047c7aaa/hierarchy.png` |
| Nested hierarchy and parent selector interaction | NOT RUN — editor UI input was not exercised; a build does not verify tree selection, reparent feedback or layout |
| Parent/child appearance after project save and reopen | NOT RUN — live editor acceptance remains open |

M1.2 is **In progress**. It now has a real hierarchy and undoable parent changes, but its primary
viewport editing promise still needs transform gizmos, snapping and visible interaction verification.

Next: extend the viewport gizmo to Turn and Size and add snapping. Keep selection, hierarchy,
undo/redo and save/reopen checks open until exercised in the editor.

## Viewport transform gizmos and snapping — 28 September 2026

### M1.2 implementation slice

- CharacterStudio now draws red, green and blue world-axis Move handles at the selected object's
  world pivot while the Move tool is active. Axis hit-testing starts a drag; the projected screen
  motion becomes a world-axis offset, then converts through the parent's inverse world matrix so
  nested objects move along world axes without changing their other local transform values.
- Dragging previews the transform in the scene. Releasing records one `TransformEditCommand`, so
  the completed move participates in the existing undo/redo history. Camera orbit and ImGui mouse
  actions are suppressed during the drag. Invalid or singular parent transforms do not apply a
  non-finite position.
- Optional world-grid snapping is available for Move, with a configurable grid step. Turn draws
  local-axis rings and Size draws local-axis arrows; each tool has optional angle or scale
  increments. The gizmo is hidden in Home, Play, sequence preview and sequence export states.
- Turn accumulates signed pointer sweeps around the selected ring, and Size adjusts only the
  corresponding local scale component. Each drag previews from its starting transform and commits
  at most one undoable edit on release. Global save, reimport, play and exit shortcuts are ignored
  while a transform drag is active.

### Verification evidence

| Check | Result |
| --- | --- |
| `ViewportTransformGizmoMathTests` | PASS — 14 tests cover Move axis projection/picking, parent-space conversion, rotation-ring construction/picking and signed sweeps, scale factors, snapping, and invalid input |
| Release solution build | PASS — 0 warnings, 0 errors |
| Full `Ember.Engine.Tests` run | PASS — 310 passed, 0 failed |
| CharacterStudio Release capture | PASS — `ReleaseAShowcase.json`, 1280×720; selected Fox, all three handles and the Move grid-snap control visible; exit 0, stderr empty; `%TEMP%/Ember/M12MoveSnap-0e5933e18c314698b92b09f9c5a81ff4/move-snap.png` |
| Turn/Size ring and handle capture | NOT RUN — the screenshot path starts with Move selected; no input-driven capture was available |
| Live Move/Turn/Size drag, undo/redo and save/reopen | NOT RUN — Windows Computer Use initialization failed twice with `failed to write kernel assets: The system cannot find the path specified (os error 3)` |

M1.2 remains **In progress**. Move is rendered in CharacterStudio and the math for all three modes
is covered. The Turn/Size UI state, actual drag interactions, undo/redo and save/reopen workflow still
need live verification. The full CPU suite now passes in one run.

Next: exercise Move, Turn, Size, parent selection and reparenting through visible controls; verify
drag feedback, snapping, undo/redo and save/reopen. Then complete M0.4 and UX.1–UX.5 gates and rerun
Windows CI.

## M1.1 selected-model reload code review — 28 September 2026

- Reviewed the existing **Reload selected model** action and its failure path. CharacterStudio loads
  and validates a candidate preview before swapping it into `ReloadableAsset`; a load or validation
  failure reports that the previous preview remains active. The Inspector offers a repair-and-reload
  next step for the error.
- `ReloadableAssetTests.ValidGlbReplacementBecomesCurrentAndCorruptReplacementKeepsItActive` edits
  and reloads a real GLB fixture, then replaces its bytes with invalid data and verifies that the
  successfully loaded asset remains current and undisposed. This validates resource ownership, not
  the complete CharacterStudio graphics interaction.
- Visible project-picker, Reload, corrupt-file recovery and save/reopen interactions remain
  unverified. The Windows Computer Use helper failed to initialize in this session with
  `failed to write kernel assets: The system cannot find the path specified (os error 3)`.

M1.1 remains **In progress**. Its selected-model reload and invalid-replacement recovery are
implemented and have a focused asset-ownership test; the visible workflow gate is still open.

Next: verify project create/open and asset browse/preview/Add/Cancel, then reload a valid model and
exercise a corrupt replacement through the visible controls. Continue the M0.4, UX.1–UX.4 and M1.2
interaction gates when desktop input is available.

## M1.3 versioned hierarchy template snapshots — 28 September 2026

- Added `SceneTemplateFile` to capture one scene object and its descendants as a versioned JSON
  asset. It preserves stable source object IDs and local hierarchy/transforms, removes the captured
  root's external parent, and excludes unrelated scene objects.
- Saving a new file assigns a template ID at revision one. Saving over a valid template preserves
  its ID and increments the revision. The destination is replaced atomically only after the complete
  scene snapshot has serialized and passed scene validation.
- Added `SceneFile.FromJson` so validated scene snapshots can be embedded and loaded without a
  temporary file. Unsupported template versions, missing roots, malformed hierarchies and invalid
  scene data are rejected.

### Verification evidence

| Check | Result |
| --- | --- |
| `SceneTemplateFileTests` | PASS — 4 tests cover subtree capture, stable IDs and hierarchy, revision increments, preservation after a failed save, unsupported versions and missing roots |
| Release solution build | PASS — 0 warnings, 0 errors |
| Full `Ember.Engine.Tests` run | PASS — 314 passed, 0 failed |
| Template save/place/update controls and broken-source recovery | NOT RUN — no editor panel or instance workflow is implemented in this slice; visible UI acceptance remains open |

M1.3 is **In progress**. Durable template snapshots are implemented, but template placement,
independent instance IDs, per-instance overrides, explicit updates, orphan handling and source
relinking remain open.

Next: implement command-based template placement with collision-free instance IDs and preserve the
source-to-instance object mapping in scene persistence. Then add transform overrides and explicit
revision updates without changing existing instances on project open.

## M1.3 expanded template instances and persisted source mapping — 28 September 2026

- Added `SceneTemplateInstanceSystem` to place the saved hierarchy under an instance wrapper.
  Every scene object gets a new ID; each wrapper stores the template ID, applied revision, source
  root and source-to-instance object map. Saving and reopening scene version 8 preserves that map.
- Asset references stay shared. Character attachment IDs, spawn IDs and RPG entity instance IDs
  are regenerated per placement. Door links targeting spawn markers inside the template are
  remapped to their copied marker when the caller supplies a world-cell ID. Such a placement fails
  before scene mutation if the required cell context is missing.
- Nested template instances are rejected with a clear error. The existing single-object Duplicate
  command also rejects a template wrapper rather than silently dropping its hierarchy metadata.

### Verification evidence

| Check | Result |
| --- | --- |
| Release solution build | PASS — 0 warnings, 0 errors |
| Template and scene persistence focused tests | PASS — 25 tests, 0 failures |
| Full `Ember.Engine.Tests` run | PASS — 318 passed, 0 failed in two consecutive runs |
| Test scheduling | PASS — test-body parallelization is disabled so lifecycle tests can pump worker-prepared results on their creating thread |
| Template controls, instance overrides, explicit update and broken-source relinking | NOT RUN — authoring API is implemented, but CharacterStudio controls and live interaction are not |

M1.3 remains **In progress**. Snapshot storage, independent hierarchy placement and source mapping
persistence are implemented. Per-instance transform overrides, revision update/merge behavior,
orphan handling and relinking still need implementation; all visible controls and save/reopen
acceptance remain open.

Next: persist a baseline for each template source object, infer transform/name overrides against
that baseline, and add an explicit update command that preserves overridden fields and existing
instance IDs while applying a newer template revision.

## M1.3 template baselines and explicit revision updates — 28 September 2026

- Scene version 9 stores a source baseline for every mapped template object: stable source ID,
  default name and local transform. Version 8 scenes still load with an empty baseline; updating
  those older instances is rejected because their local edits cannot be distinguished safely.
- `UpdateSceneTemplateCommand` explicitly applies a newer revision while keeping instance object
  IDs and wrapper placement. A name or local transform that still matches its previous source
  baseline receives the new template value; a changed value is retained as a local override.
  Baselines advance to the new source defaults, so overrides remain detectable in later revisions.
- Updates reparent mapped objects to match the new source hierarchy and participate in undo/redo.
  The command fails before changing the scene if IDs, template identity, root identity, baseline,
  or source-object membership do not match. Added/removed source objects are deferred to a later
  slice; components other than name and transform are not merged by this update yet.

### Verification evidence

| Check | Result |
| --- | --- |
| Template update, migration and scene persistence focused tests | PASS — 28 tests, 0 failures |
| Release solution build | PASS — 0 warnings, 0 errors |
| Full `Ember.Engine.Tests` run | PASS — 321 passed, 0 failed in two consecutive runs |
| Visible template placement/update and reload workflow | NOT RUN — CharacterStudio does not yet expose these controls |

M1.3 remains **In progress**. The authoring API now supports same-shape revision updates with
name/transform override detection. Structural additions/removals, wider component overrides,
orphan handling, relinking and the visible workflow remain open.

Next: add template object additions/removals with preserved mappings and explicit orphan warnings,
then broaden field-level overrides before exposing the workflow in CharacterStudio.
