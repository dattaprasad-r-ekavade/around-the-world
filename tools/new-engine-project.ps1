param(
    [Parameter(Mandatory = $true)]
    [string] $DestinationPath,

    [Parameter(Mandatory = $true)]
    [string] $EngineRoot
)

$ErrorActionPreference = 'Stop'

$engineRootPath = (Resolve-Path -LiteralPath $EngineRoot).Path
$engineProjectPath = Join-Path $engineRootPath 'src/Ember.Engine/Ember.Engine.csproj'
if (-not (Test-Path -LiteralPath $engineProjectPath -PathType Leaf)) {
    throw "Ember.Engine.csproj was not found under engine root '$engineRootPath'."
}
$engineProjectPath = (Resolve-Path -LiteralPath $engineProjectPath).Path

$destination = [System.IO.Path]::GetFullPath($DestinationPath)
if (Test-Path -LiteralPath $destination) {
    if ((Get-ChildItem -LiteralPath $destination -Force | Measure-Object).Count -gt 0) {
        throw "Destination '$destination' already exists and is not empty."
    }
} else {
    New-Item -ItemType Directory -Path $destination | Out-Null
}

$templatePath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../templates/MinimalGame')).Path
Copy-Item -LiteralPath (Join-Path $templatePath 'MinimalEmberGame.csproj') -Destination $destination
Copy-Item -LiteralPath (Join-Path $templatePath 'Program.cs') -Destination $destination
Copy-Item -LiteralPath (Join-Path $templatePath 'ember.project.json') -Destination $destination
Copy-Item -LiteralPath (Join-Path $templatePath 'Content') -Destination $destination -Recurse

$escapedEngineProjectPath = [System.Security.SecurityElement]::Escape($engineProjectPath)
$props = @"
<Project>
  <PropertyGroup>
    <EmberEngineProject>$escapedEngineProjectPath</EmberEngineProject>
  </PropertyGroup>
</Project>
"@
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText((Join-Path $destination 'Directory.Build.props'), $props, $utf8NoBom)

Write-Output "Created MinimalEmberGame at '$destination'."
Write-Output "It references Ember.Engine at '$engineProjectPath' without copying engine source."
