# UX evidence — beginner creation experience

Evidence for roadmap tasks UX.1–UX.6, in date order. Moved from ENGINE_PROGRESS.md on 29 September 2026 (M0.5); section text is unchanged. Add new entries at the end.

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
  [FilmStarter.json](../../samples/CharacterStudio/Scenes/FilmStarter.json). New projects select the
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

## UX.1 opaque Home background and current Release captures — 29 September 2026

- Home now paints an opaque full-window background, preventing the active 3D preview from showing
  through its project choices.
- Move/Turn/Size help text now wraps inside the Inspector instead of clipping at its right edge.
- Release Home and `ReleaseAShowcase.json` were captured at 1280×720 on Windows 11 build 26200
  with .NET 9.0.19. The Home capture shows Game, Film and Open choices; the scene capture shows the
  courtyard, animated characters, Add library, Inspector and viewport gizmo without a black scene.
  The Move hint is fully readable across two lines in the updated capture.
- Both screenshot processes exited successfully and wrote no stderr. Evidence is in
  `%TEMP%/Ember/ManualGraphicsGate/035a4dbcc58c43608d26b0c166b0496e/` (`home.png`,
  `starter-scene.png`, and stdout/stderr files).
- `mcp__cua_repl` failed to initialize twice with `failed to write kernel assets: The system cannot
  find the path specified (os error 3)`. Window resize, DPI scaling, mouse interaction, save/reopen,
  Play/Stop and keyboard-focus checks remain unverified; these captures do not close the manual
  graphics gate or UX.1 acceptance.

### Verification evidence

| Check | Result |
| --- | --- |
| Release solution build after Home background change | PASS — 0 warnings, 0 errors |
| Home and starter-scene captures | PASS — both 1280×720; exit 0; stderr empty |
| Interactive desktop verification | NOT RUN — Windows computer-use helper initialization failed |

UX.1 remains **In progress**. The opaque Home backdrop is verified in a Release capture; resize,
DPI, Reset layout, focus, and the visual create/edit/undo/play/save/reopen actions remain open.

Next: resume the visual workflow gate when desktop input is available, while checking M0.4
persistence coverage and continuing the code work that does not require mouse interaction.

## UX.2 searchable Add library — 29 September 2026

- The existing Add library now filters model assets by a case-insensitive match against their
  project-relative path/name. Selection follows the filtered results, and an empty match shows a
  clear retry hint. This stays inside the existing panel.
- The Release starter-scene capture shows the search field without obscuring the scene. The actual
  typing/filter/selection workflow remains unverified because the desktop helper could not initialize.
  UX.2 remains **In progress**.

### Verification evidence

| Check | Result |
| --- | --- |
| CharacterStudio Release build | PASS — 0 warnings, 0 errors |
| CharacterStudio Home and starter-scene captures | PASS — both exited 0 at 1280×720; stderr files were empty |
| Capture artifacts | `%TEMP%/EmberAssetSearch/0fd96abf81cf4ca18e494cbc17406bda/home.png` and `starter-scene.png`; logs are in the same directory |
| Interactive search, selection and preview | NOT RUN — desktop helper initialization failed with missing kernel assets |

Next: verify the Add library search and project model preview/import workflow through the visible editor;
continue the UX.1–UX.4 and M1.1 workflow gates without treating captures as interactive evidence.

## UX.3 readable editor UI font — 29 September 2026

- Replaced ImGui's pixel-style default with the bundled Source Sans 3 regular font at 16 px, so
  editor labels and help text use a tooling-oriented typeface. The font and its SIL Open Font
  License are copied to build and publish outputs; the editor reports a clear startup error if the
  font asset is missing. The font was sourced from the Campaign sample's existing licensed asset.
- A Release capture shows the new typography in CharacterStudio at 1280×720. UX.3 remains **In
  progress**: 100%/150% scaling, keyboard focus, message behavior and non-color-only states still
  need live checks.

### Verification evidence

| Check | Result |
| --- | --- |
| `dotnet build Ember.sln --configuration Release --no-restore --nologo` | PASS — 0 warnings, 0 errors |
| `dotnet test Ember.sln --configuration Release --no-build --no-restore --nologo` | PASS — 370 passed, 0 failed, 0 skipped |
| `EditorUiAssetsTests.ReadableEditorFontAndLicenseAreCopiedBesideTheHost` | PASS — font and license are present in test output; Release build also copied them beside CharacterStudio |
| CharacterStudio Release scene capture | PASS — exit 0, stderr empty; `%TEMP%/Ember/UxReadableFont/98c1aa22e0b44c03a7c4b86e17ed43db/starter-scene.png` |
| Hosted Windows CI | PASS — [run 36603972718](https://github.com/dattaprasad-r-ekavade/around-the-world/actions/runs/36603972718) for `fad1f66` |
| Interactive typography and DPI checks | NOT RUN — 100%/150% scaling and keyboard focus were not exercised |

Next: complete the live UX.1–UX.4 workflow checks, including readable typography at both required
scales; do not treat the 1280×720 capture as evidence for DPI scaling.

## UX.3 explicit editor punctuation glyphs — 30 September 2026

The saved Home capture showed question marks where Home buttons use an ellipsis. The Source Sans 3
file was present, but ImGui's default font range did not include all punctuation used by Ember's
labels. CharacterStudio now loads Basic Latin/Latin-1 plus the en/em dash, curly quotes, bullet,
ellipsis and right arrow glyphs. This fixes the capture evidence for button labels and preserves the
arrows/quotes used by other editor tools.

| Check | Result |
| --- | --- |
| `dotnet build src/Ember.Editor/Ember.Editor.csproj --configuration Release --no-restore --nologo` | PASS — 0 warnings, 0 errors |
| `dotnet build samples/CharacterStudio/CharacterStudio.csproj --configuration Release --no-restore --nologo` | PASS — 0 warnings, 0 errors |
| CharacterStudio Home capture at 1280×720 | PASS — exit 0, empty stderr; “Choose a folder...” and “Open a project...” render with ellipses at `%TEMP%/Ember/UxGlyphRange/0cc562895e12465692bc4e9de3f0d43f/home.png` |
| CharacterStudio ReleaseAShowcase capture at 1280×720 | PASS — exit 0, empty stderr; `%TEMP%/Ember/UxGlyphRange/0cc562895e12465692bc4e9de3f0d43f/scene.png` |
| Automated test suite | NOT RUN — per the user's instruction |
| Interactive 100%/150% DPI, focus and input checks | NOT RUN — a screenshot capture does not exercise these gates |

Still open: live typography/DPI/focus checks and the full beginner workflow. UX.3 remains
**In progress**.

## UX.3 keyboard navigation — 1 October 2026

Enabled ImGui keyboard navigation in CharacterStudio's shared editor context. The editor already
forwards Tab, Enter, arrow, text and modifier key events; ImGui can now use those events to navigate
the existing controls. This is an implementation change, not evidence that the focus order or
visual focus treatment is usable.

An isolated copy of the Release editor opened on Home at 1461×887 from
`%TEMP%/Ember/KeyboardNavigation/f67c3f344ce74440b6f74c00589e39e7`. No project was created and no
sample scene was changed. Automated Tab input did not produce a clearly visible focus change. The
user confirmed physical mouse clicks work, but was unavailable to operate the desktop; therefore
keyboard focus remains unverified.

| Check | Result |
| --- | --- |
| `dotnet build src/Ember.Editor/Ember.Editor.csproj --configuration Release --no-restore --nologo -p:AutoRestoreMGCBTool=false` | PASS — 0 warnings and 0 errors |
| Automated test suite | NOT RUN — per the user's instruction |
| Interactive keyboard focus | NOT VERIFIED — automation could not confirm focus movement; physical input was unavailable |

UX.3 remains **In progress**. M0.4's project and scene interaction checks also remain open.
