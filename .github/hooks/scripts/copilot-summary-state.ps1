[CmdletBinding()]

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Get-RepoRootFromScript {
    return (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
}

function Get-ShortHash {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text,

        [int]$Length = 12
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha256.ComputeHash($bytes)
        $hex = -join ($hash | ForEach-Object { $_.ToString("x2") })
        return $hex.Substring(0, [Math]::Min($Length, $hex.Length))
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-HookEventTime {
    param([AllowNull()][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [DateTimeOffset]::UtcNow
    }

    try {
        return [DateTimeOffset]::Parse(
            $Value,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::AssumeUniversal -bor [System.Globalization.DateTimeStyles]::AdjustToUniversal
        )
    }
    catch {
        return [DateTimeOffset]::UtcNow
    }
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        $Value
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory) -and (-not (Test-Path -LiteralPath $directory))) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $Value | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Get-NotifyStatePaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $stateDirectory = Join-Path $RepoRoot ".copilot"

    return [ordered]@{
        Directory = $stateDirectory
        Session = Join-Path $stateDirectory "notify-session.json"
        Summary = Join-Path $stateDirectory "notify-summary.json"
    }
}

try {
    $repoRoot = Get-RepoRootFromScript
    $stdinRaw = [Console]::In.ReadToEnd()
    $hookInput = if ([string]::IsNullOrWhiteSpace($stdinRaw)) {
        @{}
    }
    else {
        $stdinRaw | ConvertFrom-Json -Depth 30
    }

    $eventTime = Get-HookEventTime -Value $hookInput.timestamp
    $timestampIso = $eventTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    $cwd = if ($hookInput.cwd) { "$($hookInput.cwd)" } else { $repoRoot }
    $sessionId = if ($hookInput.sessionId) { "$($hookInput.sessionId)" } else { "unknown-session" }

    $notifyStatePaths = Get-NotifyStatePaths -RepoRoot $repoRoot
    $runIdSeed = "$repoRoot|$cwd|$sessionId|$($eventTime.ToString('O'))"
    $runId = "copilot-$($eventTime.ToString('yyyyMMddTHHmmssfffZ'))-$(Get-ShortHash -Text $runIdSeed -Length 12)"

    Write-JsonFile -Path $notifyStatePaths.Session -Value ([ordered]@{
        version = 1
        run_id = $runId
        repo_root = $repoRoot
        cwd = $cwd
        started_at = $timestampIso
    })

    Write-JsonFile -Path $notifyStatePaths.Summary -Value ([ordered]@{
        version = 1
        run_id = $runId
        updated_at = $timestampIso
        status = "pending"
        summary = ""
        details = @()
        changed_files = @()
        next_steps = @()
    })

    exit 0
}
catch {
    Write-Host "Summary state hook failed: $($_.Exception.Message)"
    exit 0
}
