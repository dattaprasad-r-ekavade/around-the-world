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
