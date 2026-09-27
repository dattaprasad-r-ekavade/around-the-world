# Manual Windows graphics gate

This gate supplements the Windows build and CPU-test workflow. It checks the graphics-backed
CharacterStudio tool on a real Windows desktop; a green unit-test run does not replace it.
Run it for changes to rendering, asset loading, editor UI, runtime play/stop, or packaging.

## Record the run

Capture the commit, date, Windows version, .NET runtime, GPU/driver, display resolution, and
whether the run uses Debug or Release. Save the screenshot and console output with the review
or release evidence. Report failures as failures; do not count an unrun check as a pass.

## Checks

1. Build and capture the startup editor window:

   ```powershell
   dotnet build Ember.sln --configuration Release --no-restore --nologo
   $studio = Join-Path $PWD 'samples/CharacterStudio/bin/Release/net9.0-windows/win-x64/CharacterStudio.exe'
   $capture = Join-Path $env:TEMP 'Ember/ManualGraphicsGate/character-studio-start.png'
   $arguments = @('--screenshot', "`"$capture`"", '--warmup', '8')
   $process = Start-Process -FilePath $studio -ArgumentList $arguments `
     -WorkingDirectory $env:TEMP -WindowStyle Hidden -Wait -PassThru
   if ($process.ExitCode -ne 0) { throw "CharacterStudio exited with $($process.ExitCode)" }
   ```

   Confirm the capture exists and shows the CharacterStudio editor, an asset preview, and no
   loading error or black render region. Check the console for missing-device, shader, and
   asset-load faults.

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
