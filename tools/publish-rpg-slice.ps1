param(
    [Parameter(Mandatory = $true)]
    [string] $DestinationPath,

    [switch] $Validate
)

$ErrorActionPreference = 'Stop'

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectFile = Join-Path $repoRoot 'samples\RpgSlice\RpgSlice.csproj'
if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
    throw "RpgSlice.csproj was not found at '$projectFile'."
}

$destination = [System.IO.Path]::GetFullPath($DestinationPath)
if (Test-Path -LiteralPath $destination) {
    throw "Distribution destination already exists: '$destination'."
}
$destinationParent = [System.IO.Path]::GetDirectoryName($destination)
if ([string]::IsNullOrWhiteSpace($destinationParent)) {
    throw "Distribution destination has no parent directory: '$destination'."
}
[System.IO.Directory]::CreateDirectory($destinationParent) | Out-Null

$stagingName = '.ember-rpgslice-publish-' + [guid]::NewGuid().ToString('N')
$stagingRoot = [System.IO.Path]::GetFullPath((Join-Path $destinationParent $stagingName))
$resolvedParent = [System.IO.Path]::GetFullPath($destinationParent)
$stagingIsSibling = [string]::Equals([System.IO.Path]::GetDirectoryName($stagingRoot), $resolvedParent, [System.StringComparison]::OrdinalIgnoreCase)
$stagingNameIsOwned = [System.IO.Path]::GetFileName($stagingRoot) -match '^\.ember-rpgslice-publish-[0-9a-f]{32}$'
if (-not $stagingIsSibling -or -not $stagingNameIsOwned) {
    throw "Refusing to use unexpected staging path '$stagingRoot'."
}

try {
    [System.IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
    Write-Output "Publishing RpgSlice as self-contained win-x64 distribution..."
    & dotnet publish $projectFile --configuration Release --runtime win-x64 --self-contained true `
        --output $stagingRoot -p:UseAppHost=true -p:PublishSingleFile=false -p:PublishTrimmed=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $requiredFiles = @(
        'RpgSlice.exe',
        'coreclr.dll',
        'hostfxr.dll',
        'hostpolicy.dll',
        'Ember.Engine.dll',
        'Ember.Rpg.dll',
        'Ember.Authoring.dll',
        'MonoGame.Framework.dll',
        'SharpDX.dll',
        'SharpDX.Direct3D11.dll',
        'Content\RpgContent.json',
        'Content\World\settlement.json',
        'Content\World\world.json',
        'Content\World\Assets\ReleaseACourtyard.glb',
        'Content\Effects\InstancedStaticMesh.xnb'
    )
    foreach ($relativePath in $requiredFiles) {
        $requiredPath = Join-Path $stagingRoot $relativePath
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Published distribution is missing required file '$relativePath'."
        }
    }

    # Verify no source files are present
    $sourceFiles = @(Get-ChildItem -LiteralPath $stagingRoot -Recurse -File | Where-Object { $_.Extension -in '.cs', '.csproj', '.sln' })
    if ($sourceFiles.Count -gt 0) {
        throw "Published distribution contains source files ($($sourceFiles.Count) found)."
    }

    [System.IO.Directory]::Move($stagingRoot, $destination)
    Write-Output "Created self-contained win-x64 RPG slice distribution at '$destination'."

    if ($Validate) {
        Write-Output "Validating packaged RPG slice outside repository..."
        $app = Join-Path $destination 'RpgSlice.exe'

        # Test settlement smoke with --perf diagnostics
        & cmd /c "$app --settlement-smoke --windowed --perf"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged settlement smoke failed with exit code $LASTEXITCODE."
        }

        # Test persistence smoke (with isolated outside save)
        $outsideSave = Join-Path $destination 'test-save.json'
        & cmd /c "$app --persistence-smoke --save $outsideSave --windowed"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged persistence smoke failed with exit code $LASTEXITCODE."
        }
        if (Test-Path -LiteralPath $outsideSave) {
            Remove-Item -LiteralPath $outsideSave -Force
        }
        $rpgSave = "$outsideSave.rpg.json"
        if (Test-Path -LiteralPath $rpgSave) {
            Remove-Item -LiteralPath $rpgSave -Force
        }

        # Test travel smoke
        & cmd /c "$app --travel-smoke --windowed"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged travel smoke failed with exit code $LASTEXITCODE."
        }

        # Test quest smoke
        & cmd /c "$app --quest-smoke --windowed"
        if ($LASTEXITCODE -ne 0) {
            throw "Packaged quest smoke failed with exit code $LASTEXITCODE."
        }

        Write-Output "Packaged RPG slice validation PASSED cleanly outside repository."
    }
}
finally {
    if (Test-Path -LiteralPath $stagingRoot -PathType Container) {
        $cleanupTarget = [System.IO.Path]::GetFullPath($stagingRoot)
        $cleanupIsSibling = [string]::Equals([System.IO.Path]::GetDirectoryName($cleanupTarget), $resolvedParent, [System.StringComparison]::OrdinalIgnoreCase)
        $cleanupNameIsOwned = [System.IO.Path]::GetFileName($cleanupTarget) -match '^\.ember-rpgslice-publish-[0-9a-f]{32}$'
        if ($cleanupIsSibling -and $cleanupNameIsOwned) {
            Remove-Item -LiteralPath $cleanupTarget -Recurse -Force
        }
    }
}
