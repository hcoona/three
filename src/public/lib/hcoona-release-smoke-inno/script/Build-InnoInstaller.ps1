param(
    [string]$Configuration = 'Release',
    [Parameter(Mandatory = $true)][string]$PublishOutputRoot,
    [Parameter(Mandatory = $true)][string]$InstallerOutputPath,
    [string]$InstallerFileName = 'hcoona-release-smoke-inno-setup.exe',
    [string]$InnoSetupCompiler,
    [string]$TelemetryOutputPath
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$profilePhases = [System.Collections.Generic.List[object]]::new()
$maxDiagnosticCharacters = 4000

function Add-ProfilePhase {
    param(
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][datetime]$StartedAt,
        [Parameter(Mandatory = $true)][System.Diagnostics.Stopwatch]$Stopwatch,
        [Parameter(Mandatory = $true)][string]$Outcome,
        [object[]]$Argv = @(),
        [AllowNull()][System.Nullable[int]]$ExitCode = $null,
        [string]$Cwd = (Get-Location).Path,
        [string[]]$OutputPaths = @(),
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
    if ($null -ne $ExitCode) {
        $record['exit-code'] = $ExitCode
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
    if ($text.Length -gt $MaxCharacters) {
        $remaining = $text.Length - $MaxCharacters
        $text = $text.Substring(0, $MaxCharacters) + [System.Environment]::NewLine + "[truncated $remaining characters]"
    }
    return $text
}

function Join-FailureMessageWithDiagnostic {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [object[]]$Output = @()
    )
    $diagnostic = Format-SafeDiagnosticText -Output $Output
    if ([string]::IsNullOrWhiteSpace($diagnostic)) {
        return $Message
    }
    return "$Message$([System.Environment]::NewLine)ISCC output:$([System.Environment]::NewLine)$diagnostic"
}

function Get-ISCCPath {
    param([string]$Hint)
    if ($Hint) {
        if (Test-Path -LiteralPath $Hint -PathType Leaf) {
            return (Resolve-Path -LiteralPath $Hint).Path
        }
        throw "Inno Setup compiler not found at: $Hint"
    }

    $command = Get-Command -Name 'iscc', 'iscc.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        "$($env:LOCALAPPDATA)\Programs\Inno Setup 6\ISCC.exe",
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw 'Inno Setup compiler (ISCC.exe) not found. Install Inno Setup 6 or pass -InnoSetupCompiler.'
}

$setupScript = Join-Path $PSScriptRoot 'Setup.iss'
if (-not (Test-Path -LiteralPath $setupScript -PathType Leaf)) {
    throw "Inno Setup script not found: $setupScript"
}

$publishOutput = (Resolve-Path -LiteralPath $PublishOutputRoot).Path
$publishedExe = Join-Path $publishOutput 'hcoona-release-smoke-inno.exe'
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "Published smoke executable not found: $publishedExe"
}

New-Item -ItemType Directory -Force -Path $InstallerOutputPath | Out-Null
$installerOutput = (Resolve-Path -LiteralPath $InstallerOutputPath).Path
$compilerResolutionStartedAt = Get-Date
$compilerResolutionStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
try {
    $compiler = Get-ISCCPath -Hint $InnoSetupCompiler
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
$outputBaseName = [System.IO.Path]::GetFileNameWithoutExtension($InstallerFileName)
if ([string]::IsNullOrWhiteSpace($outputBaseName)) {
    throw "Installer file name must include a non-empty base name: $InstallerFileName"
}

if ($publishOutput.EndsWith('\')) {
    $publishOutput = $publishOutput.TrimEnd('\')
}

$isccArgs = @($setupScript, "/O$installerOutput", "/F$outputBaseName", "/DPublishDir=$publishOutput")
$startedAt = Get-Date
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$previousNativeCommandPreference = $PSNativeCommandUseErrorActionPreference
$phaseRecorded = $false
$exitCode = $null
$isccOutput = @()
try {
    try {
        $PSNativeCommandUseErrorActionPreference = $false
        $isccOutput = @(& $compiler @isccArgs 2>&1)
        $lastExitCodeVariable = Get-Variable -Name LASTEXITCODE -ErrorAction SilentlyContinue
        if ($lastExitCodeVariable -and $null -ne $lastExitCodeVariable.Value) {
            $exitCode = [int]$lastExitCodeVariable.Value
        }
        $errorMessage = $null
        if ($null -eq $exitCode) {
            $errorMessage = Join-FailureMessageWithDiagnostic -Message 'ISCC launch failed before producing an exit code.' -Output $isccOutput
            Add-ProfilePhase -Phase 'iscc-compile' -StartedAt $startedAt -Stopwatch $stopwatch -Outcome 'failure' -Argv (@($compiler) + $isccArgs) -OutputPaths @($installerOutput) -ErrorMessage $errorMessage
            $phaseRecorded = $true
            throw $errorMessage
        }
        $outcome = if ($exitCode -eq 0) { 'success' } else { 'failure' }
        if ($exitCode -ne 0) {
            $errorMessage = Join-FailureMessageWithDiagnostic -Message "Inno Setup failed, exit code: $exitCode" -Output $isccOutput
        }
        Add-ProfilePhase -Phase 'iscc-compile' -StartedAt $startedAt -Stopwatch $stopwatch -Outcome $outcome -Argv (@($compiler) + $isccArgs) -ExitCode $exitCode -OutputPaths @($installerOutput) -ErrorMessage $errorMessage
        $phaseRecorded = $true
        if ($exitCode -ne 0) {
            throw $errorMessage
        }
    }
    catch {
        if (-not $phaseRecorded) {
            $errorMessage = if ($null -ne $exitCode) {
                Join-FailureMessageWithDiagnostic -Message $_.Exception.Message -Output $isccOutput
            }
            else {
                Join-FailureMessageWithDiagnostic -Message "ISCC launch failed before producing an exit code: $($_.Exception.Message)" -Output $isccOutput
            }
            if ($null -ne $exitCode) {
                Add-ProfilePhase -Phase 'iscc-compile' -StartedAt $startedAt -Stopwatch $stopwatch -Outcome 'failure' -Argv (@($compiler) + $isccArgs) -ExitCode $exitCode -OutputPaths @($installerOutput) -ErrorMessage $errorMessage
            }
            else {
                Add-ProfilePhase -Phase 'iscc-compile' -StartedAt $startedAt -Stopwatch $stopwatch -Outcome 'failure' -Argv (@($compiler) + $isccArgs) -OutputPaths @($installerOutput) -ErrorMessage $errorMessage
            }
        }
        throw
    }
    finally {
        $stopwatch.Stop()
        $PSNativeCommandUseErrorActionPreference = $previousNativeCommandPreference
    }

    $setupPath = Join-Path $installerOutput $InstallerFileName
    $validationStartedAt = Get-Date
    $validationStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
            $message = "Inno Setup completed but expected installer was not found: $setupPath"
            Add-ProfilePhase -Phase 'installer-output-validation' -StartedAt $validationStartedAt -Stopwatch $validationStopwatch -Outcome 'failure' -Argv @() -ExitCode 1 -OutputPaths @($setupPath) -ErrorMessage $message
            throw $message
        }
        Add-ProfilePhase -Phase 'installer-output-validation' -StartedAt $validationStartedAt -Stopwatch $validationStopwatch -Outcome 'success' -Argv @() -ExitCode 0 -OutputPaths @($setupPath)
    }
    finally {
        $validationStopwatch.Stop()
    }
}
finally {
    Write-ProfileTelemetry
}
