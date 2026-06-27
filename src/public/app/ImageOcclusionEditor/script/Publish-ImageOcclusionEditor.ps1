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
    Publish ImageOcclusionEditorWinUI3 to an organized "out" folder structure.

.DESCRIPTION
    Runs "dotnet publish" to build and publish the WinUI3 desktop app.
    Output folder structure example:
        out/ImageOcclusionEditor/<Configuration>/<TargetFramework>/<RuntimeIdentifier>/

.PARAMETER Configuration
    Build configuration. Default: Release.

.PARAMETER OutputRoot
    Root output folder. Default: repository-root/out.

.NOTES
    Target Framework (TFM) and Runtime Identifier (RID) are read from the project file.

.EXAMPLE
    ./script/Publish-ImageOcclusionEditor.ps1 -Configuration Release

.NOTES
    Requires .NET SDK and necessary workloads.
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$OutputRoot,
    [string]$TelemetryOutputPath,
    [string]$MsBuildBinlogDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$InformationPreference = 'Continue'
$PSNativeCommandUseErrorActionPreference = $true
$PSStyle.OutputRendering = 'Ansi'
$profilePhases = [System.Collections.Generic.List[object]]::new()

function Add-ProfilePhase {
    param(
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][datetime]$StartedAt,
        [Parameter(Mandatory = $true)][System.Diagnostics.Stopwatch]$Stopwatch,
        [Parameter(Mandatory = $true)][string]$Outcome,
        [object[]]$Argv = @(),
        [AllowNull()][object]$ExitCode = 0,
        [string]$Cwd = (Get-Location).Path,
        [string[]]$OutputPaths = @(),
        [string]$BinlogPath,
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
        argv = @($Argv)
        cwd = $Cwd
        'output-paths' = @($OutputPaths)
    }
    if ($PSBoundParameters.ContainsKey('ExitCode')) {
        if ($null -ne $ExitCode) {
            $record['exit-code'] = $ExitCode
        }
    }
    else {
        $record['exit-code'] = 0
    }
    if ($BinlogPath) {
        $record['binlog-path'] = $BinlogPath
        $record['binlog-exists'] = Test-Path -LiteralPath $BinlogPath -PathType Leaf
    }
    if ($ErrorMessage) {
        $record['error'] = $ErrorMessage
    }
    $profilePhases.Add([pscustomobject]$record)
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

# Dot-source shared helpers
. (Join-Path $PSScriptRoot 'Helpers.ps1')

# 1) Resolve paths and inputs
$repoRoot = Get-RepoRoot
$projectDir = Join-Path $repoRoot 'ImageOcclusionEditorWinUI3'
$csprojPath = Join-Path $projectDir 'ImageOcclusionEditorWinUI3.csproj'
$proj = Get-ProjectInfo -CsprojPath $csprojPath -DefaultAssemblyName 'ImageOcclusionEditor'
$assemblyName = $proj.AssemblyName
${Framework} = $proj.TargetFramework
${Runtime} = $proj.RuntimeIdentifier

if (-not $OutputRoot) {
    $OutputRoot = Join-Path $repoRoot 'out'
}
$publishDir = Get-PublishOutputPath -PublishOutputRoot $OutputRoot -Configuration $Configuration -TargetFramework $Framework -RuntimeIdentifier $Runtime

Write-Information "Publish info:"
Write-Information "  Project:       $csprojPath"
Write-Information "  Configuration: $Configuration"
Write-Information "  Framework:     $Framework"
Write-Information "  Runtime:       $Runtime"
Write-Information "  Output:        $publishDir"

# 2) Clean target output directory
if (Test-Path -LiteralPath $publishDir) {
    Write-Information "Cleaning output directory..."
    Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction Stop
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

# 3) Compose dotnet publish arguments
$publishArgs = @(
    'publish', $csprojPath,
    '-c', $Configuration,
    '-f', $Framework,
    '-o', $publishDir,
    '--nologo'
)

if ($Runtime) {
    $publishArgs += @('-r', $Runtime)
}
$publishBinlogPath = $null
if ($MsBuildBinlogDirectory) {
    New-Item -ItemType Directory -Force -Path $MsBuildBinlogDirectory | Out-Null
    $publishBinlogPath = Join-Path $MsBuildBinlogDirectory 'dotnet-publish.binlog'
    $publishArgs += "/bl:$publishBinlogPath"
}

# Use locked restore mode if lock file exists
$lockFile = Join-Path $projectDir 'packages.lock.json'
if (Test-Path -LiteralPath $lockFile) {
    $publishArgs += @('/p:RestoreLockedMode=true')
}

# 4) Run publish
$cmdLine = 'dotnet ' + ($publishArgs -join ' ')
Write-Verbose "Run: $cmdLine"
$publishStartedAt = Get-Date
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$publishOutput = @()
$exit = $null
$previousNativePreference = $PSNativeCommandUseErrorActionPreference
try {
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        $publishOutput = & dotnet @publishArgs 2>&1
        $exit = $LASTEXITCODE
    }
    catch {
        if ($stopwatch.IsRunning) {
            $stopwatch.Stop()
        }
        Add-ProfilePhase -Phase 'dotnet-publish' -StartedAt $publishStartedAt -Stopwatch $stopwatch -Outcome 'failure' -Argv (@('dotnet') + $publishArgs) -ExitCode $null -OutputPaths @($publishDir) -BinlogPath $publishBinlogPath -ErrorMessage $_.Exception.Message
        Write-ProfileTelemetry
        throw
    }
}
finally {
    $PSNativeCommandUseErrorActionPreference = $previousNativePreference
}
$stopwatch.Stop()
foreach ($line in $publishOutput) {
    Write-Information ($line.ToString())
}
Add-ProfilePhase -Phase 'dotnet-publish' -StartedAt $publishStartedAt -Stopwatch $stopwatch -Outcome $(if ($exit -eq 0) { 'success' } else { 'failure' }) -Argv (@('dotnet') + $publishArgs) -ExitCode $exit -OutputPaths @($publishDir) -BinlogPath $publishBinlogPath
if ($exit -ne 0) {
    Write-Error "dotnet publish command failed: $cmdLine" -ErrorAction Continue
    Write-Error "dotnet publish exit code: $exit" -ErrorAction Continue
    if ($publishOutput) {
        Write-Error "dotnet publish combined stdout/stderr:" -ErrorAction Continue
        foreach ($line in $publishOutput) {
            Write-Error ($line.ToString()) -ErrorAction Continue
        }
    }
    Write-ProfileTelemetry
    throw "dotnet publish failed, exit code: $exit"
}
Write-Information ("Publish done in {0}s" -f [Math]::Round($stopwatch.Elapsed.TotalSeconds, 2))

# Generate SBOM via CycloneDX (shared helper)
$cycloneStartedAt = Get-Date
$cycloneStopwatch = $null
$manifestPath = Join-Path $publishDir "_manifest"
$cycloneArgv = @(
    'dotnet',
    'tool',
    'run',
    'dotnet-CycloneDX',
    '--',
    $csprojPath,
    '-o',
    $manifestPath,
    '--exclude-dev',
    '--exclude-test-projects',
    '--output-format',
    'Json',
    '--disable-package-restore'
)
$cycloneExitCode = $null
try {
    Write-Information "Generating SBOM with CycloneDX..."
    $cycloneStartedAt = Get-Date
    $cycloneStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $bomPath = Invoke-CycloneDX -ProjectPath $csprojPath -OutDir $manifestPath -DisablePackageRestore -CommandArgv ([ref]$cycloneArgv) -ExitCode ([ref]$cycloneExitCode)
    $cycloneStopwatch.Stop()
    Add-ProfilePhase -Phase 'cyclonedx-sbom' -StartedAt $cycloneStartedAt -Stopwatch $cycloneStopwatch -Outcome 'success' -Argv $cycloneArgv -OutputPaths @($bomPath)
    Write-Information "SBOM generated at: $bomPath"
}
catch {
    if ($cycloneStopwatch) {
        $cycloneStopwatch.Stop()
        Add-ProfilePhase -Phase 'cyclonedx-sbom' -StartedAt $cycloneStartedAt -Stopwatch $cycloneStopwatch -Outcome 'failure' -Argv $cycloneArgv -ExitCode $cycloneExitCode -OutputPaths @($manifestPath) -ErrorMessage $_.Exception.Message
    }
    Write-Error "CycloneDX failed: $($_.Exception.Message)"
    throw
}
finally {
    Write-ProfileTelemetry
}

# 5) Show key outputs (AOT vs non-AOT layouts may differ)
$exe = Join-Path $publishDir ($assemblyName + '.exe')
$runtimeConfig = Join-Path $publishDir ($assemblyName + '.runtimeconfig.json')
$deps = Join-Path $publishDir ($assemblyName + '.deps.json')
$sbom = Join-Path -Path $publishDir -ChildPath "_manifest" "bom.json"

Write-Information "Output file check:"
foreach ($f in @($exe, $runtimeConfig, $deps, $sbom)) {
    $exists = Test-Path -LiteralPath $f
    $mark = if ($exists) { '[x]' } else { '[ ]' }
    Write-Information ("  {0} {1}" -f $mark, $f)
}
Write-Information "It's normal to see missing runtimeconfig.json and deps.json files for AOT builds."
