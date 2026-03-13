[CmdletBinding()]

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

. (Join-Path $PSScriptRoot "CopilotHook.Common.ps1")

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

try {
    $stdinRaw = [Console]::In.ReadToEnd()
    $hookInput = if ([string]::IsNullOrWhiteSpace($stdinRaw)) {
        @{}
    }
    else {
        $stdinRaw | ConvertFrom-Json -Depth 30
    }

    $eventTime = Get-HookEventTime -Value $hookInput.timestamp
    $timestampIso = $eventTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    $workspaceContext = Resolve-HookWorkspaceContext -HookInput $hookInput -FallbackPath ((Get-Location).Path)
    $cwd = $workspaceContext.Cwd
    $repoRoot = $workspaceContext.RepoRoot
    $sessionId = if ($hookInput.sessionId) { "$($hookInput.sessionId)" } else { "unknown-session" }

    $notifyStatePaths = Get-NotifyStatePath -StateRoot $cwd
    $runIdSeed = "$repoRoot|$cwd|$sessionId|$($eventTime.ToString('O'))"
    $runId = "copilot-$($eventTime.ToString('yyyyMMddTHHmmssfffZ'))-$(Get-ShortHash -Text $runIdSeed -Length 12)"

    Write-JsonFile -Path $notifyStatePaths.Session -Value ([ordered]@{
            version    = 1
            run_id     = $runId
            repo_root  = $repoRoot
            cwd        = $cwd
            started_at = $timestampIso
        })

    Write-JsonFile -Path $notifyStatePaths.Summary -Value ([ordered]@{
            version       = 1
            run_id        = $runId
            updated_at    = $timestampIso
            status        = "pending"
            summary       = ""
            details       = @()
            changed_files = @()
            next_steps    = @()
        })

    exit 0
}
catch {
    Write-Warning -Message "Summary state hook failed: $($_.Exception.Message)"
    exit 0
}
