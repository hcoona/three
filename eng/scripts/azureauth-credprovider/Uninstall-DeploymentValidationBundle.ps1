#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$InstallRoot,

    [string]$LegacyNuGetOwnershipManifestPath,

    [switch]$SkipConfigurationCleanup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runningOnWindows = $IsWindows
$hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
$expectedTargetRid = if (
    $hostArchitecture -eq [System.Runtime.InteropServices.Architecture]::X64
) {
    if ($runningOnWindows) {
        'win-x64'
    }
    elseif ($IsLinux) {
        'linux-x64'
    }
}
$legacySourceRevision = 'f1bf00d412732739713a18e9a07e8738ff80c6f8'
$pathComparison = if ($runningOnWindows) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
$pathComparer = if ($runningOnWindows) {
    [System.StringComparer]::OrdinalIgnoreCase
}
else {
    [System.StringComparer]::Ordinal
}
$bundleRoot = Split-Path -Parent $PSCommandPath
$legacyNuGetSupportPath = Join-Path $bundleRoot 'legacy-nuget.ps1'
if (-not (Test-Path -LiteralPath $legacyNuGetSupportPath -PathType Leaf)) {
    throw 'The deployment validation legacy NuGet support script is missing.'
}
. $legacyNuGetSupportPath
$homeDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
if ([string]::IsNullOrWhiteSpace($homeDirectory)) {
    throw 'The current user profile directory is unavailable.'
}

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = if ($runningOnWindows) {
        $localAppData = [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData
        )
        Join-Path $localAppData 'AzureAuth/CredProvider/installation'
    }
    else {
        Join-Path $homeDirectory '.local/lib/azureauth-credprovider'
    }
}

$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)

$receiptPath = Join-Path $InstallRoot 'installation.json'
if (-not (Test-Path -LiteralPath $receiptPath -PathType Leaf)) {
    throw "The deployment validation installation receipt is missing from '$InstallRoot'."
}

$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
$receiptSchemaVersion = [string]$receipt.schemaVersion
if ($receiptSchemaVersion -notin @(
        'azureauth-credprovider-deployment-validation-install-v1',
        'azureauth-credprovider-deployment-validation-install-v2'
    )) {
    throw 'The deployment validation installation receipt is invalid.'
}
if ($receiptSchemaVersion -eq
    'azureauth-credprovider-deployment-validation-install-v1' -and
    -not (Test-ExactObjectPropertySet `
            -Value $receipt `
            -ExpectedNames @(
            'schemaVersion',
            'productVersion',
            'sourceRevision',
            'targetRid',
            'installRoot',
            'applicationRoot',
            'nugetPluginRoot'
        ))) {
    throw 'The legacy deployment validation installation receipt is invalid.'
}

$applicationRoot = Join-Path $InstallRoot 'app'
if (-not $InstallRoot.Equals(
        [System.IO.Path]::GetFullPath([string]$receipt.installRoot),
        $pathComparison
    ) -or
    -not $applicationRoot.Equals(
        [System.IO.Path]::GetFullPath([string]$receipt.applicationRoot),
        $pathComparison
    )) {
    throw 'The requested removal roots do not match the installation receipt.'
}

$installPathRoot = [System.IO.Path]::GetPathRoot($InstallRoot)
if ($InstallRoot -eq $installPathRoot -or
    $InstallRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) -eq
    $homeDirectory.TrimEnd([System.IO.Path]::DirectorySeparatorChar)) {
    throw "The deployment target '$InstallRoot' is too broad."
}

$legacyNuGetCleanupPlan = $null
if ($receiptSchemaVersion -eq
    'azureauth-credprovider-deployment-validation-install-v1') {
    if ($receipt.sourceRevision -cne $legacySourceRevision -or
        [string]::IsNullOrWhiteSpace($expectedTargetRid) -or
        $receipt.targetRid -cne $expectedTargetRid -or
        [string]::IsNullOrWhiteSpace([string]$receipt.nugetPluginRoot)) {
        throw 'The legacy deployment validation installation receipt is invalid.'
    }
    $legacyNuGetPluginRoot = [System.IO.Path]::GetFullPath(
        [string]$receipt.nugetPluginRoot
    )
    $legacyPathRoot = [System.IO.Path]::GetPathRoot($legacyNuGetPluginRoot)
    if ($legacyNuGetPluginRoot -eq $legacyPathRoot -or
        $legacyNuGetPluginRoot.TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar
        ) -eq $homeDirectory.TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar
        )) {
        throw "The legacy NuGet deployment target '$legacyNuGetPluginRoot' is too broad."
    }
    $installRootPrefix = $InstallRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    ) + [System.IO.Path]::DirectorySeparatorChar
    $legacyNuGetPluginRootPrefix = $legacyNuGetPluginRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    ) + [System.IO.Path]::DirectorySeparatorChar
    if ($InstallRoot.Equals($legacyNuGetPluginRoot, $pathComparison) -or
        $InstallRoot.StartsWith($legacyNuGetPluginRootPrefix, $pathComparison) -or
        $legacyNuGetPluginRoot.StartsWith($installRootPrefix, $pathComparison)) {
        throw 'The product and legacy NuGet plugin roots must not overlap.'
    }
    if ([string]::IsNullOrWhiteSpace($LegacyNuGetOwnershipManifestPath)) {
        $LegacyNuGetOwnershipManifestPath = Join-Path $homeDirectory (
            '.azureauth-credprovider/phase10/manifests/' +
            'nuget-plugin-layout-ownership-manifest.json'
        )
    }
    $LegacyNuGetOwnershipManifestPath = [System.IO.Path]::GetFullPath(
        $LegacyNuGetOwnershipManifestPath
    )
    $legacyNuGetCleanupPlan = Get-LegacyNuGetCleanupPlan `
        -ApplicationRoot $applicationRoot `
        -NuGetPluginRoot $legacyNuGetPluginRoot `
        -OwnershipManifestPath $LegacyNuGetOwnershipManifestPath `
        -PathComparer $pathComparer `
        -PathComparison $pathComparison
}

$productExecutableName = if ($runningOnWindows) {
    'azureauth-credprovider.exe'
}
else {
    'azureauth-credprovider'
}
$productExecutablePath = Join-Path $InstallRoot "app/$productExecutableName"

if (-not $SkipConfigurationCleanup) {
    if (-not (Test-Path -LiteralPath $productExecutablePath -PathType Leaf)) {
        throw (
            'The installed product executable is missing. ' +
            'Use -SkipConfigurationCleanup only after configuration was cleaned up separately.'
        )
    }

    foreach ($ecosystem in @('git', 'nuget', 'python', 'npm', 'pnpm', 'yarn')) {
        & $productExecutablePath unconfigure $ecosystem
        if ($LASTEXITCODE -ne 0) {
            throw "Configuration cleanup failed for $ecosystem with exit code $LASTEXITCODE."
        }
    }

    $azurePipelinesJobId = [string]$env:SYSTEM_JOBID
    $isAzurePipelinesJob = $env:TF_BUILD -ieq 'True' -and
    -not [string]::IsNullOrWhiteSpace($azurePipelinesJobId) -and
    $azurePipelinesJobId.Length -le 128 -and
    $azurePipelinesJobId -ne '.' -and
    $azurePipelinesJobId -ne '..' -and
    $azurePipelinesJobId -match '^[A-Za-z0-9._-]+$'
    if ($isAzurePipelinesJob) {
        & $productExecutablePath cleanup --ci azure-pipelines
        if ($LASTEXITCODE -ne 0) {
            throw (
                'Azure Pipelines job configuration cleanup failed with exit code ' +
                "$LASTEXITCODE."
            )
        }
    }
}

if ($null -ne $legacyNuGetCleanupPlan) {
    Remove-LegacyNuGetPayload -Plan $legacyNuGetCleanupPlan
}

if (Test-Path -LiteralPath $InstallRoot) {
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
}

Write-Output "Removed internal deployment validation payload: $InstallRoot"
