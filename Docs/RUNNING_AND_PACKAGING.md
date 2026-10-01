# Running samples and packaging projects

Command-line reference for the samples, the editor (CharacterStudio) and external projects.
Moved from the repository README on 29 September 2026. Build first with
`dotnet build Ember.sln`. If you built Release, replace `Debug` with `Release` in the paths.

## About CharacterStudio

`samples/CharacterStudio` is the editor today. It starts on Home (Game, Film, Open) and
opens a scene-first workspace with an Add library, Inspector, Move/Turn/Size tools, undo/redo,
Save, Play/Stop and optional tools under More tools (templates, paths, world cells, RPG
authoring, sequences, export). Its `Scenes/ReleaseAShowcase.json` scene shows a static
environment and two animated characters. Roadmap task E.1 moves the editor into
`src/Ember.Editor`; until then, run it as shown below. The current state of each editor
feature is in the [roadmap status table](ENGINE_ROADMAP.md#current-status). The supported
GLB subset is in [BUILDING_BLOCKS.md](BUILDING_BLOCKS.md).

## Running FirstLight

FirstLight is a windowed WinExe; run its built DLL with `dotnet`:

```powershell
# A room, a lamp, and someone standing in it.
dotnet samples\FirstLight\bin\Debug\net9.0-windows\win-x64\FirstLight.dll --windowed

# The same thing, photographed and exited, with no human present.
dotnet samples\FirstLight\bin\Debug\net9.0-windows\win-x64\FirstLight.dll --screenshot room.png
```

(If you built Release, swap `Debug` for `Release` in the path.)

Read `samples/FirstLight/FirstLightGame.cs` start to finish before you write anything. It is
under two hundred lines and it is the whole contract between the engine and a game. Then copy
it and start deleting.

## Running Campaign

Campaign is a game, not part of the engine — run it the same way:

```powershell
dotnet samples\Campaign\bin\Debug\net9.0-windows\win-x64\Campaign.dll --windowed
```

## Running CharacterStudio

Open the saved mixed environment/character scene in the editor sample:

```powershell
dotnet samples\CharacterStudio\bin\Debug\net9.0-windows\win-x64\CharacterStudio.dll --open samples\CharacterStudio\Scenes\ReleaseAShowcase.json
```

Press **P** to run an isolated scene copy, **E** to play the sample interaction sound, and **P**
again to stop and restore the authored scene. The editor panel also offers these actions and the
interaction volume slider. The separate sequence panel can play, pause, scrub, and preview the
character/camera sequence generated for the first skinned character in the scene.

CharacterStudio can also export that generated sequence to numbered PNGs. The folder must not
already contain an export manifest:

```powershell
dotnet samples\CharacterStudio\bin\Debug\net9.0-windows\win-x64\CharacterStudio.dll --export-sequence captures\walk --export-start 0 --export-end 0.1 --export-fps 30 --export-width 640 --export-height 360
```

The defaults are the whole sequence at 30 fps and 1280×720. The folder receives `frame_000000.png`
and `manifest.json`; incomplete runs are marked canceled or failed in the manifest. Save and reopen
a sequence with its scene using companion versioned JSON files:

```powershell
dotnet samples\CharacterStudio\bin\Debug\net9.0-windows\win-x64\CharacterStudio.dll --save captures\walk.scene.json --save-sequence captures\walk.sequence.json --export-sequence captures\walk-before --export-start 0 --export-end 0.1 --export-fps 30 --export-width 640 --export-height 360 --screenshot captures\walk-before.png --warmup 1
dotnet samples\CharacterStudio\bin\Debug\net9.0-windows\win-x64\CharacterStudio.dll --open captures\walk.scene.json --open-sequence captures\walk.sequence.json --export-sequence captures\walk-after --export-start 0 --export-end 0.1 --export-fps 30 --export-width 640 --export-height 360 --screenshot captures\walk-after.png --warmup 1
```

Sequence JSON stores stable track, scene-object, asset, and camera IDs, clip names, timing,
transforms, and cuts. Loading validates every target, asset, clip, and camera reference.

## Starting an external Ember project

Generate a minimal consumer in an empty folder outside this repository. The generated project
references Ember.Engine from the engine checkout and includes a project file whose startup scene
is resolved relative to that file:

```powershell
.\tools\new-engine-project.ps1 -DestinationPath C:\Games\MyEmberGame -EngineRoot D:\Projects\engine
Set-Location C:\Games\MyEmberGame
dotnet run -- --windowed
```

Edit `ember.project.json` to select another project-relative startup scene. The generator copies
the starter game and scene content only; it does not copy Ember's source tree.

Create a package from the generated consumer without starting a graphics window, then move the
package folder and launch the consumer against its project file:

```powershell
dotnet run -- --validate-package --project C:\Games\MyEmberGame\ember.project.json
dotnet run -- --package-to C:\Games\MyEmberGamePackage
dotnet run -- --project C:\Games\MyEmberGamePackage\ember.project.json
```

`--validate-package` checks the project's referenced content without writing output. It prints scene,
GLB, referenced-audio, sequence and package-file counts when ready, or actionable diagnostics for
discovered dependency errors and a nonzero exit code when blocked. Output destination validation
remains part of the package/publish operation.

Packaging copies the startup scene, referenced GLBs, and project-local buffer/image sidecars while
keeping their project-relative paths.

Publish a self-contained Windows x64 distribution with the .NET runtime and native dependencies,
then launch it directly from the output folder:

```powershell
.\tools\publish-engine-project.ps1 -ProjectDirectory C:\Games\MyEmberGame -DestinationPath C:\Build\MyEmberGame-win-x64
C:\Build\MyEmberGame-win-x64\MinimalEmberGame.exe
```

The publisher bundles the packaged project under `Project/`; the executable finds it without a
command-line project path.

For a complete authored Fox scene, package, and playable Windows build, follow
[ANIMATED_GAME_TUTORIAL.md](ANIMATED_GAME_TUTORIAL.md).
