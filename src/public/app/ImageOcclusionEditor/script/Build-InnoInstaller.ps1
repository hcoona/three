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

It then stages the Inno Setup inputs and runs the Inno Setup compiler (ISCC) on a generated wrapper for
`script/Setup.iss`, passing the detected publish directory.
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
    [ValidateSet('Release', 'Debug')]
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
        [ValidateSet('Info', 'Warn', 'Error', 'Success')]
        [string]$Level = 'Info'
    )
    # Use Write-Information/Warning/Error for host-agnostic output. Avoid non-ASCII symbols for encoding portability.
    switch ($Level) {
        'Info' { Write-Information "[>] $Message" -InformationAction Continue }
        'Warn' { Write-Warning     "[!] $Message" }
        'Error' { Write-Error       "[x] $Message" }
        'Success' { Write-Information "[OK] $Message" -InformationAction Continue }
    }
}

function ConvertTo-InnoStringLiteral {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Value
    )

    return '"' + ($Value -replace '"', '""') + '"'
}

function Get-InnoTempBase {
    $candidatePaths = @(
        $env:RUNNER_TEMP,
        $env:TEMP,
        [System.IO.Path]::GetTempPath()
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    $resolvedCandidates = @(
        foreach ($candidatePath in $candidatePaths) {
            try {
                (Resolve-Path -LiteralPath $candidatePath -ErrorAction Stop).Path
            }
            catch {
                $null
            }
        }
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    if ($resolvedCandidates.Count -eq 0) {
        return $InstallerOutputPath
    }

    return $resolvedCandidates | Sort-Object { $_.Length } | Select-Object -First 1
}

# Load shared helpers and resolve repo paths
. (Join-Path $PSScriptRoot 'Helpers.ps1')
$ScriptDir = $PSScriptRoot
$RepoRoot = Get-RepoRoot
# WinUI3 csproj sits in project folder at repo root
$WinUIProj = Join-Path $RepoRoot 'ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj'
# Use Setup.iss in the same directory as this script
$SetupDir = $ScriptDir
$SetupIss = Join-Path $SetupDir  'Setup.iss'

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

# Validate that publish artifacts exist (produced by Publish-ImageOcclusionEditor.ps1)
$exePath = Join-Path $PublishOutputPath ("{0}.exe" -f $AssemblyName)
if (-not (Test-Path -LiteralPath $exePath)) {
    Write-Status 'Expected published artifacts not found.' 'Error'
    Write-Status "Missing: $exePath" 'Error'
    throw 'Please run script/Publish-ImageOcclusionEditor.ps1 first to produce publish output.'
}
$exePath = (Resolve-Path -LiteralPath $exePath).Path
$PublishOutputPath = (Resolve-Path -LiteralPath $PublishOutputPath).Path
$ProjectDir = (Resolve-Path -LiteralPath $RepoRoot).Path

$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath)
$AppVersion = $versionInfo.FileVersion
if ([string]::IsNullOrWhiteSpace($AppVersion)) {
    throw "Published executable FileVersion is empty: $exePath"
}
$AppVersion = $AppVersion.Trim()
if ($AppVersion -notmatch '^\d+(\.\d+){1,3}$') {
    throw "Published executable FileVersion is not a dotted numeric version: $AppVersion"
}

if (-not $InstallerOutputPath) {
    # Default installer output to repo root 'out' directory
    $InstallerOutputPath = Join-Path $RepoRoot 'out'
}
$InstallerOutputPath = (Resolve-Path -LiteralPath (New-Item -ItemType Directory -Force -Path $InstallerOutputPath)).Path

Write-Status "Configuration: $Configuration | RID: $RuntimeIdentifier" 'Info'
Write-Status "TFM: $TargetFramework | App Name: $AssemblyName" 'Info'
Write-Status "Publish Output: $PublishOutputPath" 'Info'
Write-Status "Installer Output: $InstallerOutputPath" 'Info'
Write-Status "Published exe FileVersion: $AppVersion" 'Info'

# Clean options removed; always ensure installer output directory exists later

## Use shared helper to locate ISCC
$ISCC = Get-ISCCPath -Hint $InnoSetupCompiler
Write-Status "Using ISCC: $ISCC" 'Info'

# Build installer
Write-Status 'Building installer with Inno Setup...' 'Info'
# Ensure output directory exists
if (-not (Test-Path -LiteralPath $InstallerOutputPath)) { New-Item -ItemType Directory -Force -Path $InstallerOutputPath | Out-Null }

$requiredInnoInputs = @(
    (Join-Path $ProjectDir 'imageocclusioneditor.ico'),
    (Join-Path $ProjectDir 'README.md'),
    (Join-Path $ProjectDir 'LICENSE'),
    (Join-Path $ProjectDir 'LICENSE.GPL3.txt'),
    (Join-Path $ProjectDir 'LICENSE.MIT.txt'),
    (Join-Path $ProjectDir 'THIRD-PARTY-NOTICES.TXT')
)
foreach ($requiredInnoInput in $requiredInnoInputs) {
    if (-not (Test-Path -LiteralPath $requiredInnoInput -PathType Leaf)) {
        throw "Required Inno Setup input not found: $requiredInnoInput"
    }
}

# Stage Inno inputs outside the validation worktree so ISCC consumes short, deterministic paths.
$InnoTempBase = Get-InnoTempBase
$InnoWorkRoot = Join-Path $InnoTempBase ("image-occlusion-inno-{0}" -f [guid]::NewGuid().ToString('N'))
$StagedPublishDir = Join-Path $InnoWorkRoot 'publish'
$StagedProjectDir = Join-Path $InnoWorkRoot 'project'
$ShortInstallerOutputPath = Join-Path $InnoWorkRoot 'out'
New-Item -ItemType Directory -Force -Path $StagedPublishDir, $StagedProjectDir, $ShortInstallerOutputPath | Out-Null

$publishItems = @(Get-ChildItem -LiteralPath $PublishOutputPath -Force)
if ($publishItems.Count -eq 0) {
    throw "Publish output is empty: $PublishOutputPath"
}
foreach ($publishItem in $publishItems) {
    Copy-Item -LiteralPath $publishItem.FullName -Destination $StagedPublishDir -Recurse -Force
}

$projectAssetRelativePaths = @(
    'imageocclusioneditor.ico',
    'README.md',
    'LICENSE',
    'LICENSE.GPL3.txt',
    'LICENSE.MIT.txt',
    'THIRD-PARTY-NOTICES.TXT',
    'Resources/Template_IIOT.txt',
    'Resources/Template_IIOTT.txt'
)
foreach ($relativePath in $projectAssetRelativePaths) {
    $sourcePath = Join-Path $ProjectDir $relativePath
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        continue
    }
    $destinationPath = Join-Path $StagedProjectDir $relativePath
    $destinationDirectory = Split-Path -Parent $destinationPath
    if (-not (Test-Path -LiteralPath $destinationDirectory)) {
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    }
    Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
}

$InnoPublishDir = (Resolve-Path -LiteralPath $StagedPublishDir).Path
$InnoProjectDir = (Resolve-Path -LiteralPath $StagedProjectDir).Path
$ShortInstallerOutputPath = (Resolve-Path -LiteralPath $ShortInstallerOutputPath).Path
$stagedExePath = Join-Path $InnoPublishDir ("{0}.exe" -f $AssemblyName)
if (-not (Test-Path -LiteralPath $stagedExePath -PathType Leaf)) {
    throw "Staged published executable not found: $stagedExePath"
}
foreach ($requiredInnoInput in $requiredInnoInputs) {
    $relativeInputPath = [System.IO.Path]::GetRelativePath($ProjectDir, $requiredInnoInput)
    $stagedInputPath = Join-Path $InnoProjectDir $relativeInputPath
    if (-not (Test-Path -LiteralPath $stagedInputPath -PathType Leaf)) {
        throw "Staged Inno Setup input not found: $stagedInputPath"
    }
}

$StagedSetupIss = Join-Path $InnoWorkRoot 'Setup.iss'
Copy-Item -LiteralPath $SetupIss -Destination $StagedSetupIss -Force
$GeneratedSetupIss = Join-Path $InnoWorkRoot 'Setup.generated.iss'
$generatedSetupContent = @(
    '#define ProjectDir ' + (ConvertTo-InnoStringLiteral $InnoProjectDir),
    '#define PublishDir ' + (ConvertTo-InnoStringLiteral $InnoPublishDir),
    '#define MyAppVersion ' + (ConvertTo-InnoStringLiteral $AppVersion),
    '#pragma message("ProjectDir=" + ProjectDir)',
    '#pragma message("PublishDir=" + PublishDir)',
    '#pragma message("MyAppVersion=" + MyAppVersion)',
    '#include ' + (ConvertTo-InnoStringLiteral $StagedSetupIss)
)
Set-Content -LiteralPath $GeneratedSetupIss -Value $generatedSetupContent -Encoding UTF8

Write-Status "Inno Staged Publish: $InnoPublishDir" 'Info'
Write-Status "Inno Staged Project: $InnoProjectDir" 'Info'
Write-Status "Inno Short Output: $ShortInstallerOutputPath" 'Info'
Write-Status "Generated Inno Script: $GeneratedSetupIss" 'Info'

# Invoke ISCC. /O specifies output folder; the generated wrapper supplies Inno defines.
$outArg = '/O' + $ShortInstallerOutputPath
if ($InnoPublishDir.EndsWith('\')) { $InnoPublishDir = $InnoPublishDir.TrimEnd('\') }
if ($InnoProjectDir.EndsWith('\')) { $InnoProjectDir = $InnoProjectDir.TrimEnd('\') }
$previousPublishDir = $env:IMAGE_OCCLUSION_EDITOR_INNO_PUBLISH_DIR
$previousProjectDir = $env:IMAGE_OCCLUSION_EDITOR_INNO_PROJECT_DIR
$previousAppVersion = $env:IMAGE_OCCLUSION_EDITOR_INNO_APP_VERSION
$previousNativeCommandPreference = $PSNativeCommandUseErrorActionPreference
try {
    $env:IMAGE_OCCLUSION_EDITOR_INNO_PUBLISH_DIR = $InnoPublishDir
    $env:IMAGE_OCCLUSION_EDITOR_INNO_PROJECT_DIR = $InnoProjectDir
    $env:IMAGE_OCCLUSION_EDITOR_INNO_APP_VERSION = $AppVersion
    $isccArgs = @($GeneratedSetupIss, $outArg)
    $PSNativeCommandUseErrorActionPreference = $false
    $isccOutput = & $ISCC @isccArgs 2>&1
    $isccExitCode = $LASTEXITCODE
    $isccOutputLines = @($isccOutput | ForEach-Object { $_.ToString() })
    foreach ($isccOutputLine in $isccOutputLines) {
        Write-Information "[ISCC] $isccOutputLine" -InformationAction Continue
    }
    if ($isccExitCode -ne 0) {
        $originalPublishFiles = @(Get-ChildItem -LiteralPath $PublishOutputPath -Recurse -File -Force)
        $stagedPublishFiles = @(Get-ChildItem -LiteralPath $InnoPublishDir -Recurse -File -Force)
        $longestOriginalPublishPaths = $originalPublishFiles |
            Sort-Object { $_.FullName.Length } -Descending |
            Select-Object -First 10 |
            ForEach-Object { "{0} ({1})" -f $_.FullName, $_.FullName.Length }
        $longestStagedPublishPaths = $stagedPublishFiles |
            Sort-Object { $_.FullName.Length } -Descending |
            Select-Object -First 10 |
            ForEach-Object { "{0} ({1})" -f $_.FullName, $_.FullName.Length }
        $isccOutputText = if ($isccOutputLines.Count -gt 0) {
            $isccOutputLines -join [Environment]::NewLine
        }
        else {
            '<no ISCC output captured>'
        }
        $diagnostics = @(
            "ISCC failed with exit code $isccExitCode.",
            'ISCC output:',
            $isccOutputText,
            'Diagnostics:',
            "ISCC: $ISCC",
            "Arguments: $($isccArgs -join ' ')",
            "Working Directory: $((Get-Location).Path)",
            "Temp Work Root: $InnoWorkRoot",
            "ProjectDir: $InnoProjectDir",
            "PublishDir: $InnoPublishDir",
            "InstallerOutputPath: $InstallerOutputPath",
            "ShortInstallerOutputPath: $ShortInstallerOutputPath",
            "Original publish file count: $($originalPublishFiles.Count)",
            "Staged publish file count: $($stagedPublishFiles.Count)",
            "Generated setup path length: $($GeneratedSetupIss.Length)",
            "Short output path length: $($ShortInstallerOutputPath.Length)",
            'Longest original publish paths:',
            ($longestOriginalPublishPaths -join [Environment]::NewLine),
            'Longest staged publish paths:',
            ($longestStagedPublishPaths -join [Environment]::NewLine)
        )
        throw ($diagnostics -join [Environment]::NewLine)
    }
}
finally {
    $PSNativeCommandUseErrorActionPreference = $previousNativeCommandPreference
    $env:IMAGE_OCCLUSION_EDITOR_INNO_PUBLISH_DIR = $previousPublishDir
    $env:IMAGE_OCCLUSION_EDITOR_INNO_PROJECT_DIR = $previousProjectDir
    $env:IMAGE_OCCLUSION_EDITOR_INNO_APP_VERSION = $previousAppVersion
}

# Copy the short-path compiler output back to the release-build executor contract directory.
$builtInstallers = @(Get-ChildItem -LiteralPath $ShortInstallerOutputPath -Filter '*.exe' -File -ErrorAction SilentlyContinue)
if ($builtInstallers.Count -eq 0) {
    throw "ISCC finished but no installer .exe was found in the short output folder: $ShortInstallerOutputPath"
}
foreach ($builtInstaller in $builtInstallers) {
    Copy-Item -LiteralPath $builtInstaller.FullName -Destination (Join-Path $InstallerOutputPath $builtInstaller.Name) -Force
}
if (-not $env:IMAGE_OCCLUSION_EDITOR_KEEP_INNO_TEMP) {
    Remove-Item -LiteralPath $InnoWorkRoot -Recurse -Force
}

# Try to discover the output installer file (by convention from .csproj)
$expectedInstaller = Join-Path $InstallerOutputPath 'ImageOcclusionEditorWinUI3_Setup.exe'
if (Test-Path -LiteralPath $expectedInstaller) {
    Write-Status "Installer built: $expectedInstaller" 'Success'
}
else {
    # Fallback: list most recent .exe in output folder
    $latest = Get-ChildItem -LiteralPath $InstallerOutputPath -Filter '*.exe' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($latest) {
        Write-Status "Installer built (detected): $($latest.FullName)" 'Success'
    }
    else {
        Write-Status 'ISCC finished but no installer .exe was found in the output folder.' 'Error'
        throw 'Installer output not found.'
    }
}
