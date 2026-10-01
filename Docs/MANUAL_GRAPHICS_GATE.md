# Manual Windows graphics gate

This gate supplements the Windows build and CPU-test workflow. It checks the graphics-backed
CharacterStudio tool on a real Windows desktop; a green unit-test run does not replace it.
Run it for changes to rendering, asset loading, editor UI, runtime play/stop, or packaging.

## Record the run

Capture the commit, date, Windows version, .NET runtime, GPU/driver, display resolution, and
whether the run uses Debug or Release. Save the screenshot and console output with the review
or release evidence. Report failures as failures; do not count an unrun check as a pass.

## Checks

1. Build and capture Home and a starter scene as separate windows:

   ```powershell
   dotnet build Ember.sln --configuration Release --no-restore --nologo
   $studio = Join-Path $PWD 'samples/CharacterStudio/bin/Release/net9.0-windows/win-x64/CharacterStudio.exe'
   $captureDirectory = Join-Path $env:TEMP ("Ember/ManualGraphicsGate/" + [guid]::NewGuid().ToString('N'))
   New-Item -ItemType Directory -Path $captureDirectory -Force | Out-Null
   $homeCapture = Join-Path $captureDirectory 'home.png'
   $sceneCapture = Join-Path $captureDirectory 'starter-scene.png'
   $scene = Join-Path $PWD 'samples/CharacterStudio/bin/Release/net9.0-windows/win-x64/Scenes/ReleaseAShowcase.json'
   $homeArguments = @('--screenshot', "`"$homeCapture`"", '--warmup', '8')
   $homeProcess = Start-Process -FilePath $studio -ArgumentList $homeArguments `
     -WorkingDirectory $env:TEMP -WindowStyle Hidden -Wait -PassThru `
     -RedirectStandardOutput (Join-Path $captureDirectory 'home-stdout.txt') `
     -RedirectStandardError (Join-Path $captureDirectory 'home-stderr.txt')
   if ($homeProcess.ExitCode -ne 0) { throw "CharacterStudio Home capture exited with $($homeProcess.ExitCode)" }
   $sceneArguments = @('--open', "`"$scene`"", '--screenshot', "`"$sceneCapture`"", '--warmup', '8')
   $sceneProcess = Start-Process -FilePath $studio -ArgumentList $sceneArguments `
     -WorkingDirectory $env:TEMP -WindowStyle Hidden -Wait -PassThru `
     -RedirectStandardOutput (Join-Path $captureDirectory 'scene-stdout.txt') `
     -RedirectStandardError (Join-Path $captureDirectory 'scene-stderr.txt')
   if ($sceneProcess.ExitCode -ne 0) { throw "CharacterStudio scene capture exited with $($sceneProcess.ExitCode)" }
   ```

   Confirm Home shows the Game and Film choices. Confirm the scene capture shows the courtyard,
   animated characters and editor controls without a black viewport. Check both stderr files and
   console output for missing-device, shader, and asset-load faults; retain both captures and logs.

2. Start CharacterStudio interactively with `Start-Process -FilePath $studio -ArgumentList
   '--windowed'`. Confirm the window opens at a usable size, the viewport renders, and the
   editor panels remain legible. Resize and restore the window; confirm the viewport and
   controls still line up.

3. Use the current editor controls to select the preview object, change a transform, save the
   scene, close the app, reopen the saved scene with
   `Start-Process -FilePath $studio -ArgumentList @('--open', '<scene.json>', '--windowed')`,
   and confirm the transform remains visible. Press **Play on clone**, make a runtime change
   if available, then stop; confirm the authored scene returns to its saved state.

4. Close normally. Record visible errors, the last console output, and any graphics artifacts.
   For capture runs, record the output path and image dimensions. Attach before/after captures
   when the change is visual.

## Keep RPG checks separate

This gate covers the generic engine/editor path. It does not claim that RpgSlice gameplay,
RPG save sidecars, or RPG content authoring passed. Run and report those checks separately when
an RPG change requires them; do not make them prerequisites for a generic CharacterStudio run.

## Beginner workspace acceptance

For the planned UX redesign, also follow CREATOR_EXPERIENCE.md: confirm Home routes,
no overlapping default panels, at least 60% unobscured scene area at 1280x720, readable
100%/150% scaling, Reset layout, keyboard focus, and visible action labels. Exercise
Browse → add model → select/move → undo → Play/Stop → save/reopen without typed paths
or required shortcuts. Advanced World/RPG/debug tools must be opt-in and discoverable.

These are planned acceptance checks, not passes for the current UI. Record novice
observations separately under UX.5; screenshots and developer automation cannot close
that gate. The optional designer mission must use the same saved project as free editing.

## Registered behaviour workflow acceptance

Use a build with an optional editor module that registers at least one behaviour ID and display name.
The base editor does not include a game-specific behaviour module by default.

1. Select an object and open **Custom behaviours** in the Inspector. Assign a behaviour, clear it,
   then Undo and Redo. Confirm the saved list changes once per action and the display name is shown
   instead of requiring a typed ID.
2. Save and reopen the scene. Confirm the same assignment remains. Duplicate the object and place a
   template instance; confirm both preserve the assignment.
3. Start Play and invoke the behaviour. Confirm it affects only the runtime copy. Stop Play and
   confirm the authored scene remains unchanged.
4. Close the editor, remove or rename the module, and reopen the scene. Confirm the Inspector names
   the missing behaviour and offers an undoable clear action. Restore the module and confirm the saved
   behaviour runs again.
5. Save a template with an assigned behaviour, place an instance, change the template assignment,
   and update the instance. Confirm unchanged instance assignments follow the new template, local
   assignment overrides survive, and removing a source object with an override keeps it as an orphan.
   Undo and redo the update and confirm assignments and the saved baseline return each time.
6. Record the module build, project and scene paths, exact action sequence, console output and result.

## Scene-template workflow acceptance

Use a project with a parent object and at least two children. Open **More tools → Scene templates**.

1. Save the selected hierarchy as a template, place two instances, and confirm both appear as
   separate hierarchies. Save and reopen the scene; confirm both instances and their object IDs
   remain stable.
2. Change the original source hierarchy and save a new template revision. Edit one placed instance
   locally, then update only that instance. Confirm its local edit remains, the second instance
   stays on its prior revision, and Undo/Redo restores the expected state.
3. Remove an edited or referenced source child, save another revision, and update an instance.
   Confirm the retained child is visible in the orphan warning and remains editable. Unmodified
   removed content may be deleted.
4. Move the matching template file out of the project's `Templates` folder while CharacterStudio
   is closed. Reopen the project, select an instance, and confirm the editor reports the missing
   source while keeping the expanded scene usable. Locate the matching file and relink it; a file
   with a different template ID must be rejected with an actionable message.
5. Record the project, scene, template file, screenshots and console output. These checks do not
   replace the UX.5 novice observation or the generic resize/save/reopen/play-stop gate above.
6. Make an edit, click **Home**, and confirm the unsaved state remains visible. Choose another
   project: cancel the unsaved prompt once, then save and continue. For a scene without a path,
   confirm Save As opens and the selected file is written before switching. Make another edit and
   close the window; cancel once, then reopen the close prompt, save and close, and verify the saved
   edit after reopening. Also verify **Close without saving** is explicit and returns to the OS only
   after that choice.
7. On an animated character, select a clip and scrub its time. Confirm each deliberate change turns
   the scene to Unsaved and can be undone/redone. Start **Preview playing**, let it advance, then stop
   or save; preview clock movement alone must not change the Saved status or the saved clip/time.
   Save a clip selection and scrub, reopen the scene, and confirm those authored values persist while
   preview playback starts paused unless the scene explicitly stored a playing default.
8. Make an authored edit, start **Play on clone**, then close the window. Confirm CharacterStudio
   stops the play clone and still prompts for the authored edit. Save and close, reopen, and verify
   that the authored edit persisted and the transient play-session changes did not.

For learning flows, also check that Why?/hints are optional and work offline, lesson
completion follows actual edits/play outcomes, demonstrations are explicit and undoable,
and no tool is locked behind lesson progress. Observe an unguided variation and ask the
beginner to explain the changed rule; record learning separately from task completion.
