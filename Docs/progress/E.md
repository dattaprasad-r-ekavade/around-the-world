# E — Editor project extraction

## E.1 Extract the editor project — 29 September 2026

Created `src/Ember.Editor` and moved the editor game, UI, tools, file pickers, project workflow,
play controls and supporting editor types out of `samples/CharacterStudio`. The editor now lives in
the `Ember.Editor` assembly and namespace. CharacterStudio contains only its 14-line launcher and
sample project/content pipeline; it references the editor project and keeps the existing Home-first
startup. The bundled showcase remains available through the normal `--open` argument. The E.1
acceptance wording was clarified to preserve the established Home-first UX documented in
`RUNNING_AND_PACKAGING.md` while keeping the showcase scene as the sample fixture.

The pre-move baseline was built from source commit `16d92cf` in a temporary archive. The post-move
Release build and captures use code commit `71b0031`. Before and after screenshots were visually
reviewed as matching at 1280×720.

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --configuration Release --nologo` | PASS — pre-move baseline and post-move build; 0 warnings, 0 errors |
| `dotnet test Ember.sln --configuration Release --no-build --no-restore --nologo` | PASS — post-move, 364 passed, 0 failed, 0 skipped |
| Home, Film starter and Release A showcase captures | PASS — before and after each exited 0 at 1280×720; stderr files empty; captures visually match |
| Capture configuration | Windows 11 Pro 10.0.26200; .NET SDK 9.0.302 / runtime 9.0.7; desktop 1536×864; NVIDIA GeForce RTX 4060 Laptop GPU driver 32.0.15.9174 and Intel UHD Graphics driver 32.0.101.7026; Release |
| Hosted Windows CI | PASS — [run 36580281473](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36580281473) for `8cc1a0e` |
| Before artifacts | `%TEMP%/Ember/ManualGraphicsGate/e1-git-baseline-e7739dd1f7e14818bb819c68c98dd4f4/` (`home.png`, `starter-scene.png`, `showcase-scene.png`, logs) |
| After artifacts | `%TEMP%/Ember/ManualGraphicsGate/after-editor-extraction-3b0ebad37e8c4f98b02b4b157bae8b0b/` (`home.png`, `starter-scene.png`, `showcase.png`, logs) |
| Code commit | PASS — `71b0031` (`refactor: extract editor project`) |

Still open: E.2 must split the two large editor files into focused types/files; E.3 must remove the
Authoring → RPG dependency. M0.4 and UX still need their live desktop interaction gates.

## E.2 Editor feature groups, session, controller and panels — 29 September 2026

Split `CharacterStudioGame` into feature-group files for viewport input, rendering, diagnostics,
play, sequences, projects and assets. Split `CharacterStudioEditorUi` into Home, workspace, World,
scene and inspector files. These remain partial declarations of the existing host/UI types, so this
is an organizational step rather than the final panel architecture. Added `EditorProjectSession` to
own the open project, cached asset catalog and recent-project persistence; project switching
invalidates the catalog through that session. Added `CharacterStudioPlayController` to own the play
session, history, input map, interaction audio, character/path-following state and physics cleanup.
Home and Scene Templates now have independent panel types. Every C# file under `src/Ember.Editor`
is at most 782 lines.

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — 0 warnings, 0 errors |
| `dotnet test Ember.sln --configuration Release --no-build --no-restore --nologo` | PASS — 364 passed, 0 failed, 0 skipped |
| Home, Film starter and Release A showcase captures | PASS — each exited 0 at 1280×720; stderr files empty; visually match the prior captures |
| After artifacts | `%TEMP%/Ember/ManualGraphicsGate/after-e2-panels-14dd799776324e1aa422afaca113701b/` (`home.png`, `starter-scene.png`, `showcase-scene.png`, logs) |
| Max editor source file | PASS — 782 lines (`CharacterStudioEditorUi.World.cs`) |

E.2 remains **In progress**. Workspace, World, scene and inspector UI still live on the shared
`CharacterStudioEditorUi` host. `CharacterStudioGame` still coordinates play start/stop and runtime
updates around the extracted controller. Finish those boundaries, then rerun the same checks before
marking E.2 Passed. This local work is not included in hosted CI run 36580281473, which covered the
prior pushed E.1 state. The manual desktop interaction gate remains unverified under M0.4.
