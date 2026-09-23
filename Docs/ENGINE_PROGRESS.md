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

This file records only the starting health of the repository. It does not claim that rendering, animation, streaming, physics, or editor work has been implemented.

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
| CharacterStudio scene renderer | `CharacterStudioGame` | Sample; currently only CPU geometry arrays and a borrowed device | No GPU disposal required |
| CharacterStudio scene drawing effect | `CharacterStudioGame` | New scene resource scope in task 15 | Scope disposes it before host shutdown |

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

- Task 16: added the Khronos `TextureCoordinateTest.glb` fixture under `tests/Ember.Engine.Tests/Assets`. Its CC0-1.0 source, attribution, SHA-256, reference viewer, dimensions, and glTF orientation are recorded beside it. The fixture has five identity-transform nodes and five meshes; the first mesh is a 1 m square facing +Z.
- Task 17: pinned SharpGLTF.Core 1.0.7 in `Ember.Engine`. A CPU test reads the fixture's scene, node names and identity transforms, mesh/primitive counts, attributes, and index count. The selected version targets .NET 8 or later and is MIT-licensed; its compatibility is verified by the .NET 9 solution build and tests.
- Task 18: added `StaticMeshVertex`, immutable `StaticMeshData`, and `GltfPrimitiveImporter`. The importer copies positions, normals, TEXCOORD_0, and triangle indices into Ember-owned CPU data. It rejects non-triangle topology, missing attributes, mismatched attribute counts, malformed index counts, nonfinite values, and indices outside the vertex array.

### Task 16–18 checks

| Check | Result |
| --- | --- |
| `dotnet test Ember.sln --nologo` | PASS — 20 tests, no graphics device/window |
| `dotnet build Ember.sln --nologo` | PASS — 0 warnings, 0 errors; all samples compile |
| `dotnet run --project tests/Ember.Rpg.Check --no-build` | PASS — `[OK] save then load equals original` |

The current mesh path is CPU-only. It does not upload buffers, apply node transforms, or import materials and textures. `TextureCoordinateTest.glb` intentionally includes one primitive without UV0 so the failure path stays covered.
