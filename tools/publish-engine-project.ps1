param(
    [Parameter(Mandatory = $true)]
    [string] $ProjectDirectory,

    [Parameter(Mandatory = $true)]
    [string] $DestinationPath
)

$ErrorActionPreference = 'Stop'

$projectRoot = (Resolve-Path -LiteralPath $ProjectDirectory).Path
$projectFile = Join-Path $projectRoot 'MinimalEmberGame.csproj'
$engineProjectFile = Join-Path $projectRoot 'ember.project.json'
if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
    throw "MinimalEmberGame.csproj was not found in '$projectRoot'."
}
if (-not (Test-Path -LiteralPath $engineProjectFile -PathType Leaf)) {
    throw "ember.project.json was not found in '$projectRoot'."
}

$destination = [System.IO.Path]::GetFullPath($DestinationPath)
$destinationRelativeToProject = [System.IO.Path]::GetRelativePath($projectRoot, $destination)
$destinationOutsideProject = [System.IO.Path]::IsPathRooted($destinationRelativeToProject) `
    -or ($destinationRelativeToProject -eq '..') `
    -or $destinationRelativeToProject.StartsWith('..' + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) `
    -or $destinationRelativeToProject.StartsWith('..' + [System.IO.Path]::AltDirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
if (-not $destinationOutsideProject) {
    throw "Distribution destination must be outside the project directory '$projectRoot'."
}
if (Test-Path -LiteralPath $destination) {
    throw "Distribution destination already exists: '$destination'."
}
$destinationParent = [System.IO.Path]::GetDirectoryName($destination)
if ([string]::IsNullOrWhiteSpace($destinationParent)) {
    throw "Distribution destination has no parent directory: '$destination'."
}
[System.IO.Directory]::CreateDirectory($destinationParent) | Out-Null

$stagingName = '.ember-publish-' + [guid]::NewGuid().ToString('N')
$stagingRoot = [System.IO.Path]::GetFullPath((Join-Path $destinationParent $stagingName))
$resolvedParent = [System.IO.Path]::GetFullPath($destinationParent)
$stagingIsSibling = [string]::Equals([System.IO.Path]::GetDirectoryName($stagingRoot), $resolvedParent, [System.StringComparison]::OrdinalIgnoreCase)
$stagingNameIsOwned = [System.IO.Path]::GetFileName($stagingRoot) -match '^\.ember-publish-[0-9a-f]{32}$'
if (-not $stagingIsSibling -or -not $stagingNameIsOwned) {
    throw "Refusing to use unexpected staging path '$stagingRoot'."
}

$publishDirectory = Join-Path $stagingRoot 'publish'
$packageDirectory = Join-Path $stagingRoot 'project-package'
try {
    [System.IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
    & dotnet publish $projectFile --configuration Release --runtime win-x64 --self-contained true `
        --output $publishDirectory -p:UseAppHost=true -p:PublishSingleFile=false -p:PublishTrimmed=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $application = Join-Path $publishDirectory 'MinimalEmberGame.exe'
    if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
        throw "Published apphost was not created at '$application'."
    }
    $managedAssembly = Join-Path $publishDirectory 'MinimalEmberGame.dll'
    if (-not (Test-Path -LiteralPath $managedAssembly -PathType Leaf)) {
        throw "Published game assembly was not created at '$managedAssembly'."
    }

    Write-Output 'Checking project dependencies before packaging...'
    & dotnet $managedAssembly --validate-package --project $engineProjectFile
    $preflightExitCode = $LASTEXITCODE
    if ($preflightExitCode -ne 0) {
        throw "Project content preflight failed with exit code $preflightExitCode; see the diagnostics above."
    }

    Write-Output 'Copying validated project content into the distribution...'
    & dotnet $managedAssembly --project $engineProjectFile --package-to $packageDirectory
    $packageExitCode = $LASTEXITCODE
    if ($packageExitCode -ne 0) {
        throw "The published app could not package project content (exit code $packageExitCode)."
    }

    $projectContentDirectory = Join-Path $publishDirectory 'Project'
    [System.IO.Directory]::Move($packageDirectory, $projectContentDirectory)

    $requiredFiles = @(
        'MinimalEmberGame.exe',
        'MinimalEmberGame.dll',
        'coreclr.dll',
        'hostfxr.dll',
        'hostpolicy.dll',
        'Ember.Engine.dll',
        'MonoGame.Framework.dll',
        'SharpDX.dll',
        'SharpDX.Direct3D11.dll',
        'Project\ember.project.json'
    )
    foreach ($relativePath in $requiredFiles) {
        $requiredPath = Join-Path $publishDirectory $relativePath
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Published distribution is missing required runtime/content file '$relativePath'."
        }
    }

    Write-Output 'Rechecking the staged project content...'
    $packagedProjectFile = Join-Path $projectContentDirectory 'ember.project.json'
    & dotnet $managedAssembly --validate-package --project $packagedProjectFile
    $packagedPreflightExitCode = $LASTEXITCODE
    if ($packagedPreflightExitCode -ne 0) {
        throw "Packaged project content preflight failed with exit code $packagedPreflightExitCode; see the diagnostics above."
    }

    [System.IO.Directory]::Move($publishDirectory, $destination)
    Write-Output "Created self-contained win-x64 distribution at '$destination'."
    Write-Output "Launch 'MinimalEmberGame.exe'; bundled project content is under 'Project'."
}
finally {
    if (Test-Path -LiteralPath $stagingRoot -PathType Container) {
        $cleanupTarget = [System.IO.Path]::GetFullPath($stagingRoot)
        $cleanupIsSibling = [string]::Equals([System.IO.Path]::GetDirectoryName($cleanupTarget), $resolvedParent, [System.StringComparison]::OrdinalIgnoreCase)
        $cleanupNameIsOwned = [System.IO.Path]::GetFileName($cleanupTarget) -match '^\.ember-publish-[0-9a-f]{32}$'
        if ($cleanupIsSibling -and $cleanupNameIsOwned) {
            Remove-Item -LiteralPath $cleanupTarget -Recurse -Force
        }
    }
}
