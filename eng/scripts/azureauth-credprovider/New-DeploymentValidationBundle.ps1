#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Windows', 'Linux')]
    [string]$BuildOs,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$TargetRid,

    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [ValidateNotNullOrEmpty()]
    [string]$ProductVersion = '0.0.0-internal',

    [ValidateNotNullOrEmpty()]
    [string]$SourceRevision = 'unknown',

    [ValidateNotNullOrEmpty()]
    [string]$OutputRoot = 'artifacts/azureauth-credprovider/deployment-validation'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '../../..')).Path
$outputRootPath = if ([System.IO.Path]::IsPathFullyQualified($OutputRoot)) {
    $OutputRoot
}
else {
    Join-Path $repoRoot $OutputRoot
}

$actualBuildOs = if ($IsWindows) {
    'Windows'
}
elseif ($IsLinux) {
    'Linux'
}
else {
    throw 'Deployment validation bundle generation supports only Windows and Linux hosts.'
}
if ($BuildOs -ne $actualBuildOs) {
    throw "Build OS '$BuildOs' does not match the current host '$actualBuildOs'."
}
$hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
if ($hostArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
    throw "Deployment validation bundle generation requires an x64 host, not '$hostArchitecture'."
}

$expectedRid = if ($BuildOs -eq 'Windows') { 'win-x64' } else { 'linux-x64' }
if (-not $TargetRid.Equals($expectedRid, [System.StringComparison]::Ordinal)) {
    throw "Target RID '$TargetRid' does not match build OS '$BuildOs'."
}

$projectPath = Join-Path $repoRoot (
    'src/private/app/azureauth-credprovider/' +
    'Hcoona.AzureAuth.CredProvider.Cli/Hcoona.AzureAuth.CredProvider.Cli.csproj'
)
$pythonProjectPath = Join-Path $repoRoot 'src/private/app/azureauth-credprovider/python'
$stagingRoot = Join-Path $outputRootPath "staging/$BuildOs/$TargetRid"
$appRoot = Join-Path $stagingRoot 'app'
$launcherRoot = Join-Path $stagingRoot 'launchers'
$pythonRoot = Join-Path $stagingRoot 'python'
$packagePath = Join-Path $outputRootPath (
    "azureauth-credprovider-deployment-validation-internal-$BuildOs-$TargetRid.zip"
)

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $appRoot -Force | Out-Null
New-Item -ItemType Directory -Path $launcherRoot -Force | Out-Null
New-Item -ItemType Directory -Path $pythonRoot -Force | Out-Null
New-Item -ItemType Directory -Path $outputRootPath -Force | Out-Null

try {
    dotnet publish $projectPath `
        -c $Configuration `
        -r $TargetRid `
        --self-contained false `
        -o $appRoot `
        -p:ContinuousIntegrationBuild=true `
        -p:Deterministic=true
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Push-Location $pythonProjectPath
    try {
        uv build --wheel --out-dir $pythonRoot
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
    finally {
        Pop-Location
    }

    $productExecutableName = if ($BuildOs -eq 'Windows') {
        'azureauth-credprovider.exe'
    }
    else {
        'azureauth-credprovider'
    }
    $productExecutablePath = Join-Path $appRoot $productExecutableName
    $nugetEntrypointPath = Join-Path $appRoot 'azureauth-credprovider.dll'
    if (-not (Test-Path -LiteralPath $productExecutablePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $nugetEntrypointPath -PathType Leaf)) {
        throw 'The published application payload is incomplete.'
    }

    $wheel = @(Get-ChildItem -LiteralPath $pythonRoot -Filter '*.whl' -File)
    if ($wheel.Count -ne 1) {
        throw 'The deployment validation bundle requires exactly one Python wheel.'
    }

    if ($BuildOs -eq 'Windows') {
        @"
@echo off
"%~dp0..\app\azureauth-credprovider.exe" %*
"@ | Set-Content -LiteralPath (Join-Path $launcherRoot 'azureauth-credprovider.cmd') -Encoding ascii
        @"
@echo off
"%~dp0..\app\azureauth-credprovider.exe" git credential-helper %*
"@ | Set-Content -LiteralPath (
            Join-Path $launcherRoot 'git-credential-azureauth-credprovider.cmd'
        ) -Encoding ascii
        $cliLauncherPath = 'launchers/azureauth-credprovider.cmd'
        $gitLauncherPath = 'launchers/git-credential-azureauth-credprovider.cmd'
    }
    else {
        @'
#!/bin/sh
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec "$script_dir/../app/azureauth-credprovider" "$@"
'@ | Set-Content -LiteralPath (Join-Path $launcherRoot 'azureauth-credprovider') -Encoding utf8
        @'
#!/bin/sh
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec "$script_dir/../app/azureauth-credprovider" git credential-helper "$@"
'@ | Set-Content -LiteralPath (
            Join-Path $launcherRoot 'git-credential-azureauth-credprovider'
        ) -Encoding utf8
        $cliLauncherPath = 'launchers/azureauth-credprovider'
        $gitLauncherPath = 'launchers/git-credential-azureauth-credprovider'
    }

    Copy-Item -LiteralPath (
        Join-Path $scriptRoot 'Install-DeploymentValidationBundle.ps1'
    ) -Destination (Join-Path $stagingRoot 'install.ps1')
    Copy-Item -LiteralPath (
        Join-Path $scriptRoot 'Uninstall-DeploymentValidationBundle.ps1'
    ) -Destination (Join-Path $stagingRoot 'uninstall.ps1')

    $files = @(
        Get-ChildItem -LiteralPath $stagingRoot -File -Recurse -Force |
            ForEach-Object {
                $relativePath = [System.IO.Path]::GetRelativePath(
                    $stagingRoot,
                    $_.FullName
                ).Replace('\', '/')
                [ordered]@{
                    path   = $relativePath
                    length = $_.Length
                    sha256 = (
                        Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
                    ).Hash.ToLowerInvariant()
                }
            } |
            Sort-Object path
    )

    $manifest = [ordered]@{
        schemaVersion   = 'azureauth-credprovider-deployment-validation-v1'
        artifactName    = 'azureauth-credprovider-deployment-validation'
        buildOs         = $BuildOs
        targetRid       = $TargetRid
        productVersion  = $ProductVersion
        sourceRevision  = $SourceRevision
        producedBy      = 'eng/scripts/azureauth-credprovider/New-DeploymentValidationBundle.ps1'
        releaseStatus   = 'internal-non-release'
        signatureStatus = 'unsigned'
        isInternal      = $true
        isRelease       = $false
        isSigned        = $false
        entrypoints     = [ordered]@{
            cli         = "app/$productExecutableName"
            cliLauncher = $cliLauncherPath
            gitLauncher = $gitLauncherPath
            nugetPlugin = 'app/azureauth-credprovider.dll'
            pythonWheel = "python/$($wheel[0].Name)"
            installer   = 'install.ps1'
            uninstaller = 'uninstall.ps1'
        }
        files           = $files
    }
    $manifest | ConvertTo-Json -Depth 10 -Compress |
        Set-Content -LiteralPath (Join-Path $stagingRoot 'manifest.json') -Encoding utf8

    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }

    $packageStream = [System.IO.File]::Create($packagePath)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $packageStream,
            [System.IO.Compression.ZipArchiveMode]::Create
        )
        try {
            foreach ($file in Get-ChildItem -LiteralPath $stagingRoot -File -Recurse -Force |
                    Sort-Object FullName) {
                $entryName = [System.IO.Path]::GetRelativePath(
                    $stagingRoot,
                    $file.FullName
                ).Replace('\', '/')
                $entry = $archive.CreateEntry(
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::NoCompression
                )
                $entry.LastWriteTime = [System.DateTimeOffset]::new(
                    1980,
                    1,
                    1,
                    0,
                    0,
                    0,
                    [System.TimeSpan]::Zero
                )
                $source = [System.IO.File]::OpenRead($file.FullName)
                try {
                    $destination = $entry.Open()
                    try {
                        $source.CopyTo($destination)
                    }
                    finally {
                        $destination.Dispose()
                    }
                }
                finally {
                    $source.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $packageStream.Dispose()
    }
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

Write-Output "Created internal non-release unsigned deployment validation bundle: $packagePath"
