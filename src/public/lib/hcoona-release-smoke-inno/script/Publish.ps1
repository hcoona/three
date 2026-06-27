param(
    [string]$Configuration = 'Release',
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [string]$TelemetryOutputPath,
    [string]$MsBuildBinlogDirectory
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..' 'hcoona-release-smoke-inno.csproj'
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
    $record = [ordered]@{
        phase = $Phase
        'started-at' = $StartedAt.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
        'completed-at' = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ')
        'duration-ms' = [int64]$Stopwatch.Elapsed.TotalMilliseconds
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

$publishArgs = @(
    'publish', $project,
    '--configuration', $Configuration,
    '--runtime', 'win-x64',
    '--self-contained', 'true',
    '--output', $OutputRoot,
    '/nologo'
)
$binlogPath = $null
if ($MsBuildBinlogDirectory) {
    New-Item -ItemType Directory -Force -Path $MsBuildBinlogDirectory | Out-Null
    $binlogPath = Join-Path $MsBuildBinlogDirectory 'dotnet-publish.binlog'
    $publishArgs += "/bl:$binlogPath"
}

$startedAt = Get-Date
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $previousNativeCommandPreference = $PSNativeCommandUseErrorActionPreference
    try {
        $PSNativeCommandUseErrorActionPreference = $false
        try {
            & dotnet @publishArgs
            $exitCode = $LASTEXITCODE
        }
        catch {
            if ($stopwatch.IsRunning) {
                $stopwatch.Stop()
            }
            Add-ProfilePhase -Phase 'dotnet-publish' -StartedAt $startedAt -Stopwatch $stopwatch -Outcome 'failure' -Argv (@('dotnet') + $publishArgs) -ExitCode $null -OutputPaths @($OutputRoot) -BinlogPath $binlogPath -ErrorMessage $_.Exception.Message
            throw
        }
    }
    finally {
        $PSNativeCommandUseErrorActionPreference = $previousNativeCommandPreference
    }
    $outcome = if ($exitCode -eq 0) { 'success' } else { 'failure' }
    Add-ProfilePhase -Phase 'dotnet-publish' -StartedAt $startedAt -Stopwatch $stopwatch -Outcome $outcome -Argv (@('dotnet') + $publishArgs) -ExitCode $exitCode -OutputPaths @($OutputRoot) -BinlogPath $binlogPath
    if ($exitCode -ne 0) {
        throw "dotnet publish failed, exit code: $exitCode"
    }
}
finally {
    if ($stopwatch.IsRunning) {
        $stopwatch.Stop()
    }
    Write-ProfileTelemetry
}
