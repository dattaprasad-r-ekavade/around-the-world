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
