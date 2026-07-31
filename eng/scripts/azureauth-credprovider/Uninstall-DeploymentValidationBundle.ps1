#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$InstallRoot,

    [string]$NuGetPluginRoot,

    [switch]$SkipConfigurationCleanup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runningOnWindows = $IsWindows
$pathComparison = if ($runningOnWindows) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
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
if ($receipt.schemaVersion -ne
    'azureauth-credprovider-deployment-validation-install-v1') {
    throw 'The deployment validation installation receipt is invalid.'
}

if ([string]::IsNullOrWhiteSpace($NuGetPluginRoot)) {
    $NuGetPluginRoot = [string]$receipt.nugetPluginRoot
}
$NuGetPluginRoot = [System.IO.Path]::GetFullPath($NuGetPluginRoot)
$applicationRoot = Join-Path $InstallRoot 'app'
if (-not $InstallRoot.Equals(
        [System.IO.Path]::GetFullPath([string]$receipt.installRoot),
        $pathComparison
    ) -or
    -not $applicationRoot.Equals(
        [System.IO.Path]::GetFullPath([string]$receipt.applicationRoot),
        $pathComparison
    ) -or
    -not $NuGetPluginRoot.Equals(
        [System.IO.Path]::GetFullPath([string]$receipt.nugetPluginRoot),
        $pathComparison
    )) {
    throw 'The requested removal roots do not match the installation receipt.'
}

foreach ($path in @($InstallRoot, $NuGetPluginRoot)) {
    $pathRoot = [System.IO.Path]::GetPathRoot($path)
    if ($path -eq $pathRoot -or
        $path.TrimEnd([System.IO.Path]::DirectorySeparatorChar) -eq
        $homeDirectory.TrimEnd([System.IO.Path]::DirectorySeparatorChar)) {
        throw "The deployment target '$path' is too broad."
    }
}
if ($InstallRoot.Equals($NuGetPluginRoot, $pathComparison)) {
    throw 'The product and NuGet plugin roots must be distinct.'
}
$installRootPrefix = $InstallRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
) + [System.IO.Path]::DirectorySeparatorChar
$nugetPluginRootPrefix = $NuGetPluginRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
) + [System.IO.Path]::DirectorySeparatorChar
if ($InstallRoot.StartsWith($nugetPluginRootPrefix, $pathComparison) -or
    $NuGetPluginRoot.StartsWith($installRootPrefix, $pathComparison)) {
    throw 'The product and NuGet plugin roots must not contain one another.'
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

    foreach ($ecosystem in @('git', 'nuget', 'python')) {
        & $productExecutablePath unconfigure $ecosystem
        if ($LASTEXITCODE -ne 0) {
            throw "Configuration cleanup failed for $ecosystem with exit code $LASTEXITCODE."
        }
    }
}

if (Test-Path -LiteralPath $NuGetPluginRoot) {
    Remove-Item -LiteralPath $NuGetPluginRoot -Recurse -Force
}
if (Test-Path -LiteralPath $InstallRoot) {
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
}

Write-Output "Removed internal deployment validation payload: $InstallRoot"
Write-Output "Removed NuGet plugin payload: $NuGetPluginRoot"
