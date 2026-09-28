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

For learning flows, also check that Why?/hints are optional and work offline, lesson
completion follows actual edits/play outcomes, demonstrations are explicit and undoable,
and no tool is locked behind lesson progress. Observe an unguided variation and ask the
beginner to explain the changed rule; record learning separately from task completion.
