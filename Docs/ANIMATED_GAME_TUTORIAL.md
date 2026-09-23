# Build a playable Fox project

This walkthrough creates a small Windows game outside the Ember checkout. CharacterStudio saves
a Fox animation scene; the generated consumer loads it, moves it with WASD, and packages it into
a self-contained Windows x64 build. The published build runs without the SDK or engine source.

## Requirements

- Windows x64 with a graphics-capable device.
- The Ember checkout and the .NET SDK pinned by its `global.json` (9.0.302 at this revision).
- A PowerShell session. Replace the sample checkout and output paths below with paths on your PC.

## Create the project and add Fox

Generate a consumer in a new, empty folder. It references Ember.Engine from the checkout while
you develop the game; it does not copy or modify engine source.

```powershell
$engineRoot = 'D:\Projects\engine'
$gameRoot = 'C:\Games\FoxWalk'

& (Join-Path $engineRoot 'tools\new-engine-project.ps1') `
    -DestinationPath $gameRoot -EngineRoot $engineRoot

New-Item -ItemType Directory -Force -Path (Join-Path $gameRoot 'Assets') | Out-Null
Copy-Item (Join-Path $engineRoot 'samples\CharacterStudio\Assets\Fox.glb') `
    (Join-Path $gameRoot 'Assets\Fox.glb')
```

Fox's model is CC0, but its rigging, animation, and glTF conversion are CC BY 4.0. Include the
required attribution in the project so packaging carries it with the game:

```powershell
$notice = @'
Third-party notice

Fox.glb: “Fox model by PixelMannen; rigging and animation by tomkranis; glTF conversion by AsoboStudio and scurest,” from Khronos glTF Sample Assets.
License: Creative Commons Attribution 4.0 International (CC BY 4.0), https://creativecommons.org/licenses/by/4.0/.
Source: https://github.com/KhronosGroup/glTF-Sample-Assets/tree/main/Models/Fox.
The Fox.glb file is unmodified.
'@
[System.IO.File]::WriteAllText(
    (Join-Path $gameRoot 'ThirdPartyNotices.txt'),
    $notice,
    [System.Text.UTF8Encoding]::new($false))
```

## Save an animated scene in CharacterStudio

Build CharacterStudio once, then have it save a single Fox using the `Walk` clip at 0.17 seconds.
The screenshot option closes the sample after saving and capturing the preview.

```powershell
dotnet build (Join-Path $engineRoot 'samples\CharacterStudio\CharacterStudio.csproj') --nologo

$studio = Join-Path $engineRoot 'samples\CharacterStudio\bin\Debug\net9.0-windows\win-x64\CharacterStudio.dll'
$scene = Join-Path $gameRoot 'Content\FoxScene.json'
dotnet $studio --fox --clip Walk --time 0.17 --play --save $scene `
    --screenshot (Join-Path $gameRoot 'characterstudio-preview.png') --warmup 1 --windowed
```

Point the project manifest at that saved scene. Open `ember.project.json` and set its contents to:

```json
{
  "version": 1,
  "startupScene": "Content/FoxScene.json"
}
```

You can reopen the project in CharacterStudio to inspect it. Passing `--project` makes both the
startup scene and `Assets/Fox.glb` resolve from the project folder, even when the current
directory is elsewhere:

```powershell
dotnet $studio --project (Join-Path $gameRoot 'ember.project.json') `
    --screenshot (Join-Path $gameRoot 'project-preview.png') --warmup 1 --windowed
```

## Build and play

Build the generated consumer from its folder. Always pass `--project` while developing from the
source project: the Fox asset lives there, while the default output folder contains only files
copied by the template build.

```powershell
Set-Location $gameRoot
dotnet build --nologo
dotnet run --no-build -- --project (Join-Path $gameRoot 'ember.project.json') --windowed
```

Hold **W/A/S/D** to move the Fox across the X/Z plane; the camera follows it. **Escape** exits.
Run the consumer's deterministic control smoke check to verify all four directions and the exit
action through the same update path:

```powershell
dotnet run --no-build -- --project (Join-Path $gameRoot 'ember.project.json') --smoke-controls --windowed
```

The smoke check prints `PASS control smoke` for each direction and Escape, then exits.

## Package and move the project

The packager copies the project manifest, startup scene, referenced Fox GLB, local GLB sidecars,
and `ThirdPartyNotices.txt`. Pick a destination that does not already exist.

```powershell
$package = 'C:\Build\FoxWalk-project'
dotnet run --no-build -- --project (Join-Path $gameRoot 'ember.project.json') --package-to $package
```

Move the package folder, then launch the consumer against its moved manifest:

```powershell
$movedProject = 'C:\Build\FoxWalk-project-moved'
Move-Item $package $movedProject
dotnet run --no-build -- --project (Join-Path $movedProject 'ember.project.json') `
    --screenshot (Join-Path $movedProject 'preview.png') --warmup 90 --windowed
```

The app should load the Fox from the moved folder and write a 1280×720 capture. `--package-to`
rejects existing destinations and remote GLB buffer/image URIs; local project-relative sidecars
are included.

## Publish a standalone Windows build

The publisher builds a self-contained `win-x64` app and places the packaged project and notices
under `Project/` beside the executable:

```powershell
$distribution = 'C:\Build\FoxWalk-win-x64'
& (Join-Path $engineRoot 'tools\publish-engine-project.ps1') `
    -ProjectDirectory $gameRoot -DestinationPath $distribution
```

Run `MinimalEmberGame.exe` directly. It opens `Project/ember.project.json` by default; it does not
need `dotnet`, the engine checkout, or C# source files beside it.

```powershell
& (Join-Path $distribution 'MinimalEmberGame.exe')
```

For a capture-only launch, pass the normal host capture options:

```powershell
& (Join-Path $distribution 'MinimalEmberGame.exe') `
    --screenshot (Join-Path $distribution 'fox.png') --warmup 90 --windowed
```

Copy the whole distribution folder to another Windows x64 machine or location to relocate it.
The Fox model and its third-party notice are both in the packaged `Project/` folder.

## What this small consumer does not support

- It draws skinned scene objects. Enabled non-skinned scene objects currently appear as cubes,
  so this template does not reproduce static GLB environments.
- It plays one saved clip per character with the saved name, time, speed, loop, and playing state.
  Crossfades and bone attachments are rejected with a clear error.
- Movement is a simple world-axis X/Z translation. There is no turning, camera-relative steering,
  jump, collision, physics, gameplay UI, or movement-driven animation switching.
- The skinned importer supports one skinned mesh node in a GLB's default scene, up to 72 joints,
  and four vertex influences. It does not implement morph animation, root motion, retargeting, or IK.
- Project packaging supports embedded GLB data and local buffer/image sidecars. Remote external
  URIs are rejected.
- The renderer is a basic MonoGame `SkinnedEffect` path, not a PBR pipeline. Unsupported glTF
  features should be reported by the importer rather than assumed to render correctly.

For the engine's precise scene, asset, and animation contracts, see
[`BUILDING_BLOCKS.md`](BUILDING_BLOCKS.md).
