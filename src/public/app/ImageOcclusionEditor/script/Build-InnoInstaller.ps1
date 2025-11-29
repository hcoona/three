<#
.COPYRIGHT
    Copyright (C) 2025 Shuai Zhang

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program.  If not, see <https://www.gnu.org/licenses/>.

.SYNOPSIS
Builds the installer via Inno Setup using the artifacts published by Publish-ImageOcclusionEditor.ps1.

.DESCRIPTION
This script does NOT run dotnet publish and does NOT re-implement publish logic.
It assumes the app has already been published by `script/Publish-ImageOcclusionEditor.ps1` into the conventional layout:

    out/ImageOcclusionEditor/<Configuration>/<TargetFramework>/<RuntimeIdentifier>/

It then runs the Inno Setup compiler (ISCC) on `script/Setup.iss`, passing the detected publish directory.
It follows PowerShell best practices and treats non-zero exit codes from native commands as terminating errors.

.PARAMETER Configuration
Build configuration. Defaults to Release. Accepted: Release, Debug

.PARAMETER InstallerOutputPath
Output folder for the built installer. Defaults to repository root `out` directory.

.PARAMETER InnoSetupCompiler
Path to Inno Setup compiler (ISCC.exe). If not provided, the script searches PATH and common install locations.

.PARAMETER PublishOutputRoot
Root folder that contains the published app output from Publish-ImageOcclusionEditor.ps1. Default: repository-root/out

.EXAMPLE
# From repository root or any location
pwsh -File .\script\Build-InnoInstaller.ps1 -Configuration Release

.NOTES
- Requires .NET SDK and Inno Setup 6.
- The publish output path is derived from the WinUI3 csproj's TargetFramework and RID.
#>

[CmdletBinding(PositionalBinding = $false, SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [ValidateSet('Release','Debug')]
    [string]$Configuration = 'Release',

    [string]$InnoSetupCompiler,

    [string]$InstallerOutputPath,

    [string]$PublishOutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'
$PSNativeCommandUseErrorActionPreference = $true
$PSStyle.OutputRendering = 'Ansi'

# Utility: Write compact status
function Write-Status {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet('Info','Warn','Error','Success')]
        [string]$Level = 'Info'
    )
    # Use Write-Information/Warning/Error for host-agnostic output. Avoid non-ASCII symbols for encoding portability.
    switch ($Level) {
        'Info'    { Write-Information "[>] $Message" -InformationAction Continue }
        'Warn'    { Write-Warning     "[!] $Message" }
        'Error'   { Write-Error       "[x] $Message" }
        'Success' { Write-Information "[OK] $Message" -InformationAction Continue }
    }
}

# Load shared helpers and resolve repo paths
. (Join-Path $PSScriptRoot 'Helpers.ps1')
$ScriptDir = $PSScriptRoot
$RepoRoot  = Get-RepoRoot
# WinUI3 csproj sits in project folder at repo root
$WinUIProj = Join-Path $RepoRoot 'ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj'
# Use Setup.iss in the same directory as this script
$SetupDir  = $ScriptDir
$SetupIss  = Join-Path $SetupDir  'Setup.iss'

if (-not (Test-Path -Path $WinUIProj -PathType Leaf)) {
    throw "WinUI3 project not found: $WinUIProj"
}
if (-not (Test-Path -Path $SetupIss -PathType Leaf)) {
    throw "Inno Setup script not found: $SetupIss"
}

# Use shared helper to read project info
$projInfo = Get-ProjectInfo -CsprojPath $WinUIProj -DefaultAssemblyName 'ImageOcclusionEditor' -DefaultRid 'win-x64'
$TargetFramework = $projInfo.TargetFramework
$AssemblyName = $projInfo.AssemblyName
$RuntimeIdentifier = $projInfo.RuntimeIdentifier

# Compute paths based on Publish-ImageOcclusionEditor.ps1's output layout
if (-not $PublishOutputRoot) {
    $PublishOutputRoot = Join-Path $RepoRoot 'out'
}
$PublishOutputPath = Get-PublishOutputPath -PublishOutputRoot $PublishOutputRoot -Configuration $Configuration -TargetFramework $TargetFramework -RuntimeIdentifier $RuntimeIdentifier

if (-not $InstallerOutputPath) {
    # Default installer output to repo root 'out' directory
    $InstallerOutputPath = Join-Path $RepoRoot 'out'
}
$InstallerOutputPath = (Resolve-Path -LiteralPath (New-Item -ItemType Directory -Force -Path $InstallerOutputPath)).Path

Write-Status "Configuration: $Configuration | RID: $RuntimeIdentifier" 'Info'
Write-Status "TFM: $TargetFramework | App Name: $AssemblyName" 'Info'
Write-Status "Publish Output: $PublishOutputPath" 'Info'
Write-Status "Installer Output: $InstallerOutputPath" 'Info'

# Clean options removed; always ensure installer output directory exists later

## Use shared helper to locate ISCC
$ISCC = Get-ISCCPath -Hint $InnoSetupCompiler
Write-Status "Using ISCC: $ISCC" 'Info'

# Validate that publish artifacts exist (produced by Publish-ImageOcclusionEditor.ps1)
$exePath = Join-Path $PublishOutputPath ("{0}.exe" -f $AssemblyName)
if (-not (Test-Path -LiteralPath $exePath)) {
    Write-Status 'Expected published artifacts not found.' 'Error'
    Write-Status "Missing: $exePath" 'Error'
    throw 'Please run script/Publish-ImageOcclusionEditor.ps1 first to produce publish output.'
}

# Build installer
Write-Status 'Building installer with Inno Setup...' 'Info'
# Ensure output directory exists
if (-not (Test-Path -LiteralPath $InstallerOutputPath)) { New-Item -ItemType Directory -Force -Path $InstallerOutputPath | Out-Null }

# Invoke ISCC. /O specifies output folder. Always pass PublishDir to align with actual publish output.
# Do not embed quotes; ensure no trailing backslash for PublishDir.
$outArg = '/O' + $InstallerOutputPath
if ($PublishOutputPath.EndsWith('\')) { $PublishOutputPath = $PublishOutputPath.TrimEnd('\') }
$definePublishDir = '/DPublishDir=' + $PublishOutputPath
& $ISCC $SetupIss $outArg $definePublishDir

# Try to discover the output installer file (by convention from .csproj)
$expectedInstaller = Join-Path $InstallerOutputPath 'ImageOcclusionEditorWinUI3_Setup.exe'
if (Test-Path -LiteralPath $expectedInstaller) {
    Write-Status "Installer built: $expectedInstaller" 'Success'
} else {
    # Fallback: list most recent .exe in output folder
    $latest = Get-ChildItem -LiteralPath $InstallerOutputPath -Filter '*.exe' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest) {
        Write-Status "Installer built (detected): $($latest.FullName)" 'Success'
    } else {
        Write-Status 'ISCC finished but no installer .exe was found in the output folder.' 'Error'
        throw 'Installer output not found.'
    }
}
