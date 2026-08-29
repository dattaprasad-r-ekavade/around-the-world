<#
.SYNOPSIS
    Pull the reusable half of Ratna Bay into this framework, renaming as it goes.

.DESCRIPTION
    The engine was extracted from a shipping game, and that game is still where it gets
    exercised every day. Rather than fork and drift, this script re-copies the engine sources
    and rewrites their namespaces, so a fix made while working on Ratna Bay can be brought
    across in one command instead of by hand.

    It copies an ALLOW LIST, not a folder. That is the important part. Six files that live in
    Ratna Bay's engine project are game screens wearing an engine's clothes -- a main menu that
    says RATNA BAY, a pause screen that counts stones at risk, a panel stack with a name for
    every one of that game's panels. They are listed in $Excluded below with the reason, and
    they are excluded so that a future sync cannot quietly drag them back in.

    Anything you have edited on this side will be OVERWRITTEN. Edit here only what you intend
    to stop syncing; everything else is better fixed in the source repo and pulled across.

.PARAMETER Source
    The Ratna Bay working copy to pull from.

.PARAMETER Root
    The root namespace to write. Change it here and nowhere else: every namespace, using and
    doc-comment reference is rewritten from RatnaBay.Engine to this. Renaming the framework is
    this parameter plus renaming two folders and the .csproj files.

.PARAMETER WhatIf
    List what would be written, and write nothing.

.EXAMPLE
    .\tools\sync-from-ratnabay.ps1
    .\tools\sync-from-ratnabay.ps1 -WhatIf
#>

[CmdletBinding()]
param(
    [string]$Source = 'D:\Projects\Elder Scrolls 6',
    [string]$Root = 'Ember',
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

# -Encoding UTF8 on both the read and the write, and not by preference. Windows PowerShell
# reads as ANSI by default, which turns every em dash in these comments into three characters
# of mojibake -- it compiles, so nothing catches it, and the damage is only visible to a human
# reading the file months later.
$here = Split-Path -Parent $PSScriptRoot

# Game screens that happen to sit in the engine project. Not building blocks: every one of
# them names something only Ratna Bay has.
$Excluded = @{
    'Ui/MenuRenderer.cs'     = 'draws the words RATNA BAY and that game''s three blurbs'
    'Ui/ConsentRenderer.cs'  = 'the telemetry question, in that game''s voice'
    'Ui/OverlayRenderer.cs'  = 'a pause screen that counts rooms cleared and stones at risk'
    'Ui/OverlayState.cs'     = 'the payload the above reads: RoomsCleared, PendingStones'
    'Input/OverlayInput.cs'  = 'pause and settings actions shaped to that pause screen'
    'Input/ScreenStack.cs'   = 'Shaft, CampTrader, Fort -- one bool per Ratna Bay panel'
    'Ui/UiLayout.cs'         = 'that game''s rectangle table; this repo keeps a trimmed one of its own'
}

function Convert-Source {
    param([string]$Text, [string]$FromNamespace)
    $Text.Replace($FromNamespace, $Root)
}

$engineSource = Join-Path $Source 'src\RatnaBay.Engine'
if (-not (Test-Path $engineSource)) { throw "No engine at '$engineSource'. Pass -Source." }

$target = Join-Path $here "src\$Root.Engine"
$written = 0
$skipped = @()

foreach ($file in Get-ChildItem -Path $engineSource -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notlike '*\bin\*' -and $_.FullName -notlike '*\obj\*' }) {

    $relative = $file.FullName.Substring($engineSource.Length + 1).Replace('\', '/')
    if ($Excluded.ContainsKey($relative)) {
        $skipped += "$relative -- $($Excluded[$relative])"
        continue
    }

    $destination = Join-Path $target ($relative -replace '/', '\')
    $directory = Split-Path -Parent $destination
    if (-not (Test-Path $directory)) {
        if (-not $WhatIf) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
    }

    $text = Convert-Source -Text (Get-Content -Raw -Encoding UTF8 -Path $file.FullName) -FromNamespace 'RatnaBay.Engine'
    if ($WhatIf) { Write-Host "  would write $relative" }
    else { Set-Content -Path $destination -Value $text -Encoding utf8 -NoNewline }
    $written++
}

# The console router is engine-free by design -- it lives in Ratna Bay's domain project and
# knows nothing about MonoGame -- so it comes across into its own project here, where a
# headless test or a script runner can use it without a graphics device.
$router = Join-Path $Source 'src\RatnaBay.Domain\Console\ConsoleRouter.cs'
if (Test-Path $router) {
    $destination = Join-Path $here "src\$Root.Scripting\ConsoleRouter.cs"
    $directory = Split-Path -Parent $destination
    if (-not $WhatIf -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    # Scripting, not Console: a namespace called Console shadows System.Console inside every
    # file that lives in it, and the first Console.WriteLine you write stops compiling.
    $text = Convert-Source -Text (Get-Content -Raw -Encoding UTF8 -Path $router) -FromNamespace 'RatnaBay.Domain'
    $text = $text.Replace("namespace $Root;", "namespace $Root.Scripting;")
    if ($WhatIf) { Write-Host '  would write ConsoleRouter.cs' }
    else { Set-Content -Path $destination -Value $text -Encoding utf8 -NoNewline }
    $written++
}

Write-Host ''
Write-Host "  $written file(s) $(if ($WhatIf) { 'would be ' })synced from $Source"
Write-Host ''
Write-Host '  Left behind on purpose:'
foreach ($line in $skipped) { Write-Host "    $line" }
Write-Host ''
Write-Host '  Now build: dotnet build Ember.sln'
