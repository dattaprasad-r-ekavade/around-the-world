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

The blend amount is currently supplied by the caller rather than advanced by a timed transition controller. CharacterStudio still previews one unique GLB per scene; task 38 will add persistence for character playback and attachment references. Animated clip bounds remain task 37.
