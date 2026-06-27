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

It then stages the Inno Setup inputs and runs the Inno Setup compiler (ISCC) on a short-path copy of
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

    [string]$PublishOutputRoot,

    [string]$TelemetryOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'
$PSNativeCommandUseErrorActionPreference = $true
$PSStyle.OutputRendering = 'Ansi'
$profilePhases = [System.Collections.Generic.List[object]]::new()
$maxDiagnosticCharacters = 4000

function Convert-ProfileTelemetryText {
    param([AllowNull()][object]$Value)

    if ($null -eq $Value) {
        return $null
    }

    $text = $Value.ToString()
    $workRootVariable = Get-Variable -Name 'InnoWorkRoot' -Scope Script -ErrorAction SilentlyContinue
    if ($workRootVariable -and -not [string]::IsNullOrWhiteSpace($workRootVariable.Value)) {
        $trimChars = @([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        $workRoot = $workRootVariable.Value.ToString().TrimEnd($trimChars)
        $candidateRoots = @($workRoot, ($workRoot -replace '\\', '/'), ($workRoot -replace '/', '\')) | Select-Object -Unique
        foreach ($candidateRoot in $candidateRoots) {
            if ([string]::IsNullOrWhiteSpace($candidateRoot)) {
                continue
            }
            $text = [System.Text.RegularExpressions.Regex]::Replace(
                $text,
                [System.Text.RegularExpressions.Regex]::Escape($candidateRoot),
                'inno-work:',
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
            )
        }
    }

    return $text
}

function Add-ProfilePhase {
    param(
        [Parameter(Mandatory)][string]$Phase,
        [Parameter(Mandatory)][datetime]$StartedAt,
        [Parameter(Mandatory)][System.Diagnostics.Stopwatch]$Stopwatch,
        [Parameter(Mandatory)][string]$Outcome,
        [object[]]$Argv = @(),
        [AllowNull()][System.Nullable[int]]$ExitCode = $null,
        [string]$Cwd = (Get-Location).Path,
        [string[]]$OutputPaths = @(),
        [string]$ErrorMessage
    )
    $durationMs = [int64]$Stopwatch.Elapsed.TotalMilliseconds
    $startedAtUtc = $StartedAt.ToUniversalTime()
    $completedAtUtc = (Get-Date).ToUniversalTime()
    $elapsedMs = [int64](($completedAtUtc - $startedAtUtc).TotalMilliseconds)
    if ($completedAtUtc -lt $startedAtUtc -or [Math]::Abs($elapsedMs - $durationMs) -gt 1000) {
        $completedAtUtc = $startedAtUtc.AddMilliseconds($durationMs)
    }
    $record = [ordered]@{
        phase = $Phase
        'started-at' = $startedAtUtc.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
        'completed-at' = $completedAtUtc.ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
        'duration-ms' = $durationMs
        outcome = $Outcome
        argv = @($Argv | ForEach-Object { Convert-ProfileTelemetryText $_ })
        cwd = $Cwd
        'output-paths' = @($OutputPaths | ForEach-Object { Convert-ProfileTelemetryText $_ })
    }
    if ($null -ne $ExitCode) {
        $record['exit-code'] = $ExitCode
    }
    if ($ErrorMessage) {
        $record['error'] = Convert-ProfileTelemetryText $ErrorMessage
    }
    $profilePhases.Add([pscustomobject]$record)
}

function Format-SafeDiagnosticText {
    param(
        [object[]]$Output = @(),
        [int]$MaxCharacters = $maxDiagnosticCharacters
    )

    $text = (@($Output | Where-Object { $null -ne $_ } | ForEach-Object { $_.ToString() }) -join [System.Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($text)) {
        return ''
    }
    $text = $text -replace '(?i)(authorization:\s*bearer\s+)[A-Za-z0-9._~+/\-]+=*', '$1<redacted>'
    $text = $text -replace '(?i)((?:token|secret|password|passwd|api[-_]?key)\s*[:=]\s*)[^\s;]+', '$1<redacted>'
    $text = Convert-ProfileTelemetryText $text
    if ($text.Length -gt $MaxCharacters) {
        $remaining = $text.Length - $MaxCharacters
        $text = $text.Substring(0, $MaxCharacters) + [System.Environment]::NewLine + "[truncated $remaining characters]"
    }
    return $text
}

function Write-ProfileTelemetry {
    if (-not $TelemetryOutputPath) { return }
    try {
        $parent = Split-Path -Parent $TelemetryOutputPath
        if ($parent) {
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
        }
        @{
            kind = 'powershell-release-build-profile-telemetry'
            'schema-version' = 1
            script = $PSCommandPath
            phases = @($profilePhases)
        } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $TelemetryOutputPath -Encoding UTF8
    }
    catch {
        Write-Warning "Profile telemetry could not be written to '$TelemetryOutputPath': $($_.Exception.Message)" -WarningAction Continue
    }
}

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
$compilerResolutionStartedAt = Get-Date
$compilerResolutionStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $ISCC = Get-ISCCPath -Hint $InnoSetupCompiler
}
catch {
    $compilerResolutionStopwatch.Stop()
    $compilerResolutionArgv = @()
    if (-not [string]::IsNullOrWhiteSpace($InnoSetupCompiler)) {
        $compilerResolutionArgv = @($InnoSetupCompiler)
    }
    Add-ProfilePhase -Phase 'iscc-compiler-resolution' -StartedAt $compilerResolutionStartedAt -Stopwatch $compilerResolutionStopwatch -Outcome 'failure' -Argv $compilerResolutionArgv -ErrorMessage $_.Exception.Message
    Write-ProfileTelemetry
    throw
}
finally {
    if ($compilerResolutionStopwatch.IsRunning) {
        $compilerResolutionStopwatch.Stop()
    }
}
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

# Stage Inno inputs outside the validation worktree so ISCC consumes short, deterministic paths.
$stagingStartedAt = Get-Date
$stagingStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$InnoTempBase = Get-InnoTempBase
$InnoWorkRoot = Join-Path $InnoTempBase ("image-occlusion-inno-{0}" -f [guid]::NewGuid().ToString('N'))
$StagedPublishDir = Join-Path $InnoWorkRoot 'publish'
$StagedProjectDir = Join-Path $InnoWorkRoot 'project'
$ShortInstallerOutputPath = Join-Path $InnoWorkRoot 'out'
$StagedSetupIss = Join-Path $InnoWorkRoot 'Setup.iss'
$stagingPhaseRecorded = $false
try {
    New-Item -ItemType Directory -Force -Path $StagedPublishDir, $StagedProjectDir, $ShortInstallerOutputPath -ErrorAction Stop | Out-Null

    foreach ($requiredInnoInput in $requiredInnoInputs) {
        if (-not (Test-Path -LiteralPath $requiredInnoInput -PathType Leaf)) {
            throw "Required Inno Setup input not found: $requiredInnoInput"
        }
    }

    $publishItems = @(Get-ChildItem -LiteralPath $PublishOutputPath -Force -ErrorAction Stop)
    if ($publishItems.Count -eq 0) {
        throw "Publish output is empty: $PublishOutputPath"
    }
    foreach ($publishItem in $publishItems) {
        Copy-Item -LiteralPath $publishItem.FullName -Destination $StagedPublishDir -Recurse -Force -ErrorAction Stop
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
            New-Item -ItemType Directory -Force -Path $destinationDirectory -ErrorAction Stop | Out-Null
        }
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force -ErrorAction Stop
    }

    $InnoPublishDir = (Resolve-Path -LiteralPath $StagedPublishDir -ErrorAction Stop).Path
    $InnoProjectDir = (Resolve-Path -LiteralPath $StagedProjectDir -ErrorAction Stop).Path
    $ShortInstallerOutputPath = (Resolve-Path -LiteralPath $ShortInstallerOutputPath -ErrorAction Stop).Path
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

    Copy-Item -LiteralPath $SetupIss -Destination $StagedSetupIss -Force -ErrorAction Stop
    $stagingStopwatch.Stop()
    $stagingPhaseRecorded = $true
    Add-ProfilePhase -Phase 'inno-staging-copy' -StartedAt $stagingStartedAt -Stopwatch $stagingStopwatch -Outcome 'success' -OutputPaths @($StagedPublishDir, $StagedProjectDir, $StagedSetupIss)
}
catch {
    if ($stagingStopwatch.IsRunning) {
        $stagingStopwatch.Stop()
    }
    if (-not $stagingPhaseRecorded) {
        Add-ProfilePhase -Phase 'inno-staging-copy' -StartedAt $stagingStartedAt -Stopwatch $stagingStopwatch -Outcome 'failure' -OutputPaths @($StagedPublishDir, $StagedProjectDir, $StagedSetupIss) -ErrorMessage $_.Exception.Message
    }
    if (-not $env:IMAGE_OCCLUSION_EDITOR_KEEP_INNO_TEMP -and (Test-Path -LiteralPath $InnoWorkRoot)) {
        try {
            Remove-Item -LiteralPath $InnoWorkRoot -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Warning "Inno temporary staging cleanup failed: $($_.Exception.Message)" -WarningAction Continue
        }
    }
    Write-ProfileTelemetry
    throw
}

Write-Status "Inno Staged Publish: $InnoPublishDir" 'Info'
Write-Status "Inno Staged Project: $InnoProjectDir" 'Info'
Write-Status "Inno Short Output: $ShortInstallerOutputPath" 'Info'
Write-Status "Staged Inno Script: $StagedSetupIss" 'Info'

# Invoke ISCC. /O specifies output folder; environment variables supply short Inno input paths.
$outArg = '/O' + $ShortInstallerOutputPath
if ($InnoPublishDir.EndsWith('\')) { $InnoPublishDir = $InnoPublishDir.TrimEnd('\') }
if ($InnoProjectDir.EndsWith('\')) { $InnoProjectDir = $InnoProjectDir.TrimEnd('\') }
$previousPublishDir = $env:IMAGE_OCCLUSION_EDITOR_INNO_PUBLISH_DIR
$previousProjectDir = $env:IMAGE_OCCLUSION_EDITOR_INNO_PROJECT_DIR
$previousAppVersion = $env:IMAGE_OCCLUSION_EDITOR_INNO_APP_VERSION
$previousNativeCommandPreference = $PSNativeCommandUseErrorActionPreference
$isccStartedAt = Get-Date
$isccStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$isccPhaseRecorded = $false
$isccExitCode = $null
$isccArgs = @()
try {
    $env:IMAGE_OCCLUSION_EDITOR_INNO_PUBLISH_DIR = $InnoPublishDir
    $env:IMAGE_OCCLUSION_EDITOR_INNO_PROJECT_DIR = $InnoProjectDir
    $env:IMAGE_OCCLUSION_EDITOR_INNO_APP_VERSION = $AppVersion
    $isccArgs = @($StagedSetupIss, $outArg)
    $PSNativeCommandUseErrorActionPreference = $false
    $isccOutput = @(& $ISCC @isccArgs 2>&1)
    $lastExitCodeVariable = Get-Variable -Name LASTEXITCODE -ErrorAction SilentlyContinue
    if ($lastExitCodeVariable -and $null -ne $lastExitCodeVariable.Value) {
        $isccExitCode = [int]$lastExitCodeVariable.Value
    }
    $isccOutputLines = @($isccOutput | ForEach-Object { $_.ToString() })
    foreach ($isccOutputLine in $isccOutputLines) {
        Write-Information "[ISCC] $isccOutputLine" -InformationAction Continue
    }
    $isccStopwatch.Stop()
    $isccErrorMessage = $null
    if ($null -eq $isccExitCode) {
        $isccOutputText = if ($isccOutputLines.Count -gt 0) {
            $isccOutputLines -join [Environment]::NewLine
        }
        else {
            '<no ISCC output captured>'
        }
        $isccErrorMessage = Format-SafeDiagnosticText -Output @(
            'ISCC launch failed before producing an exit code.',
            'ISCC output:',
            $isccOutputText
        )
        Add-ProfilePhase -Phase 'iscc-compile' -StartedAt $isccStartedAt -Stopwatch $isccStopwatch -Outcome 'failure' -Argv (@($ISCC) + $isccArgs) -OutputPaths @($ShortInstallerOutputPath) -ErrorMessage $isccErrorMessage
        $isccPhaseRecorded = $true
        throw $isccErrorMessage
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
            "Staged setup path length: $($StagedSetupIss.Length)",
            "Short output path length: $($ShortInstallerOutputPath.Length)",
            'Longest original publish paths:',
            ($longestOriginalPublishPaths -join [Environment]::NewLine),
            'Longest staged publish paths:',
            ($longestStagedPublishPaths -join [Environment]::NewLine)
        )
        $isccErrorMessage = Format-SafeDiagnosticText -Output $diagnostics
    }
    Add-ProfilePhase -Phase 'iscc-compile' -StartedAt $isccStartedAt -Stopwatch $isccStopwatch -Outcome $(if ($isccExitCode -eq 0) { 'success' } else { 'failure' }) -Argv (@($ISCC) + $isccArgs) -ExitCode $isccExitCode -OutputPaths @($ShortInstallerOutputPath) -ErrorMessage $isccErrorMessage
    $isccPhaseRecorded = $true
    if ($isccExitCode -ne 0) {
        throw $isccErrorMessage
    }
}
catch {
    if ($isccStopwatch.IsRunning) {
        $isccStopwatch.Stop()
    }
    if (-not $isccPhaseRecorded) {
        $isccErrorMessage = if ($null -ne $isccExitCode) {
            $_.Exception.Message
        }
        else {
            "ISCC launch failed before producing an exit code: $($_.Exception.Message)"
        }
        if ($null -ne $isccExitCode) {
            Add-ProfilePhase -Phase 'iscc-compile' -StartedAt $isccStartedAt -Stopwatch $isccStopwatch -Outcome 'failure' -Argv (@($ISCC) + $isccArgs) -ExitCode $isccExitCode -OutputPaths @($ShortInstallerOutputPath) -ErrorMessage $isccErrorMessage
        }
        else {
            Add-ProfilePhase -Phase 'iscc-compile' -StartedAt $isccStartedAt -Stopwatch $isccStopwatch -Outcome 'failure' -Argv (@($ISCC) + $isccArgs) -OutputPaths @($ShortInstallerOutputPath) -ErrorMessage $isccErrorMessage
        }
    }
    $cleanupStartedAt = Get-Date
    $cleanupStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $env:IMAGE_OCCLUSION_EDITOR_KEEP_INNO_TEMP -and (Test-Path -LiteralPath $InnoWorkRoot)) {
            Remove-Item -LiteralPath $InnoWorkRoot -Recurse -Force -ErrorAction Stop
        }
        $cleanupStopwatch.Stop()
        Add-ProfilePhase -Phase 'inno-temp-cleanup' -StartedAt $cleanupStartedAt -Stopwatch $cleanupStopwatch -Outcome 'success' -OutputPaths @()
    }
    catch {
        if ($cleanupStopwatch.IsRunning) {
            $cleanupStopwatch.Stop()
        }
        Add-ProfilePhase -Phase 'inno-temp-cleanup' -StartedAt $cleanupStartedAt -Stopwatch $cleanupStopwatch -Outcome 'failure' -OutputPaths @() -ErrorMessage $_.Exception.Message
        Write-Warning "Inno temporary staging cleanup failed: $($_.Exception.Message)" -WarningAction Continue
    }
    Write-ProfileTelemetry
    throw
}
finally {
    if ($isccStopwatch.IsRunning) {
        $isccStopwatch.Stop()
    }
    $PSNativeCommandUseErrorActionPreference = $previousNativeCommandPreference
    $env:IMAGE_OCCLUSION_EDITOR_INNO_PUBLISH_DIR = $previousPublishDir
    $env:IMAGE_OCCLUSION_EDITOR_INNO_PROJECT_DIR = $previousProjectDir
    $env:IMAGE_OCCLUSION_EDITOR_INNO_APP_VERSION = $previousAppVersion
}

# Copy the short-path compiler output back to the release-build executor contract directory.
$copyBackStartedAt = Get-Date
$copyBackStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$copyBackPhaseRecorded = $false
$copyBackFailure = $null
try {
    $builtInstallers = @(Get-ChildItem -LiteralPath $ShortInstallerOutputPath -Filter '*.exe' -File -ErrorAction SilentlyContinue)
    if ($builtInstallers.Count -eq 0) {
        $copyBackStopwatch.Stop()
        $copyBackPhaseRecorded = $true
        Add-ProfilePhase -Phase 'installer-copy-back' -StartedAt $copyBackStartedAt -Stopwatch $copyBackStopwatch -Outcome 'failure' -OutputPaths @($InstallerOutputPath) -ErrorMessage "No installer .exe was found in $ShortInstallerOutputPath."
        throw "ISCC finished but no installer .exe was found in the short output folder: $ShortInstallerOutputPath"
    }
    foreach ($builtInstaller in $builtInstallers) {
        Copy-Item -LiteralPath $builtInstaller.FullName -Destination (Join-Path $InstallerOutputPath $builtInstaller.Name) -Force -ErrorAction Stop
    }
    $copyBackStopwatch.Stop()
    $copyBackPhaseRecorded = $true
    Add-ProfilePhase -Phase 'installer-copy-back' -StartedAt $copyBackStartedAt -Stopwatch $copyBackStopwatch -Outcome 'success' -OutputPaths @($InstallerOutputPath)
}
catch {
    $copyBackFailure = $_
    if ($copyBackStopwatch.IsRunning) {
        $copyBackStopwatch.Stop()
    }
    if (-not $copyBackPhaseRecorded) {
        Add-ProfilePhase -Phase 'installer-copy-back' -StartedAt $copyBackStartedAt -Stopwatch $copyBackStopwatch -Outcome 'failure' -OutputPaths @($InstallerOutputPath) -ErrorMessage $_.Exception.Message
    }
    throw
}
finally {
    $cleanupFailure = $null
    $cleanupStartedAt = Get-Date
    $cleanupStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $env:IMAGE_OCCLUSION_EDITOR_KEEP_INNO_TEMP) {
            Remove-Item -LiteralPath $InnoWorkRoot -Recurse -Force -ErrorAction Stop
        }
        $cleanupStopwatch.Stop()
        Add-ProfilePhase -Phase 'inno-temp-cleanup' -StartedAt $cleanupStartedAt -Stopwatch $cleanupStopwatch -Outcome 'success' -OutputPaths @()
    }
    catch {
        $cleanupFailure = $_
        if ($cleanupStopwatch.IsRunning) {
            $cleanupStopwatch.Stop()
        }
        Add-ProfilePhase -Phase 'inno-temp-cleanup' -StartedAt $cleanupStartedAt -Stopwatch $cleanupStopwatch -Outcome 'failure' -OutputPaths @() -ErrorMessage $_.Exception.Message
        Write-Warning "Inno temporary staging cleanup failed: $($_.Exception.Message)" -WarningAction Continue
    }
    finally {
        Write-ProfileTelemetry
    }

    if ($null -ne $cleanupFailure -and $null -eq $copyBackFailure) {
        throw $cleanupFailure
    }
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
