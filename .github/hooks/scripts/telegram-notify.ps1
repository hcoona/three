[CmdletBinding()]

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Get-RepoRootFromScript {
    return (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
}

function Import-DotEnvFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("#")) {
            continue
        }

        $match = [regex]::Match($trimmed, '^(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$')
        if (-not $match.Success) {
            continue
        }

        $name = $match.Groups[1].Value
        $value = $match.Groups[2].Value.Trim()

        if (
            ($value.Length -ge 2) -and (
                (($value.StartsWith('"')) -and ($value.EndsWith('"'))) -or
                (($value.StartsWith("'")) -and ($value.EndsWith("'")))
            )
        ) {
            $value = $value.Substring(1, $value.Length - 2)
        }

        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name, "Process"))) {
            [Environment]::SetEnvironmentVariable($name, $value, "Process")
        }
    }
}

function Test-IsPlaceholderValue {
    param([AllowNull()][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $true
    }

    return $Value -match '^__.+__$'
}

function Get-ExecutionEnvironment {
    if ($IsWindows) {
        return "windows"
    }

    if ($IsLinux) {
        if (-not [string]::IsNullOrWhiteSpace($env:WSL_DISTRO_NAME)) {
            return "wsl"
        }

        try {
            if (Test-Path -LiteralPath "/proc/sys/kernel/osrelease") {
                $osRelease = Get-Content -LiteralPath "/proc/sys/kernel/osrelease" -Raw
                if ($osRelease -match "(?i)microsoft|wsl") {
                    return "wsl"
                }
            }
        }
        catch {
        }

        return "linux"
    }

    if ($IsMacOS) {
        return "macos"
    }

    return "unknown"
}

function Invoke-GitSafe {
    param(
        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    try {
        $output = & git -C $WorkingDirectory @Arguments 2>$null
        if ($LASTEXITCODE -eq 0) {
            return (($output -join "`n").Trim())
        }
    }
    catch {
    }

    return $null
}

function Get-RepositoryDisplayName {
    param(
        [AllowNull()][string]$RemoteUrl,

        [Parameter(Mandatory = $true)]
        [string]$FallbackName
    )

    if ([string]::IsNullOrWhiteSpace($RemoteUrl)) {
        return $FallbackName
    }

    $trimmedRemoteUrl = $RemoteUrl.Trim()

    if ($trimmedRemoteUrl -match '^(?:https://|ssh://git@)github\.com[/:](?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$') {
        return "$($Matches.owner)/$($Matches.repo)"
    }

    if ($trimmedRemoteUrl -match '^git@github\.com:(?<owner>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$') {
        return "$($Matches.owner)/$($Matches.repo)"
    }

    if ($trimmedRemoteUrl -match '^https://(?:[^@/]+@)?dev\.azure\.com/(?<org>[^/]+)/(?<project>[^/]+)/_git/(?<repo>[^/]+?)(?:\.git)?/?$') {
        return "$($Matches.org)/$($Matches.project)/$($Matches.repo)"
    }

    if ($trimmedRemoteUrl -match '^https://(?<org>[^./]+)\.visualstudio\.com/(?<project>[^/]+)/_git/(?<repo>[^/]+?)(?:\.git)?/?$') {
        return "$($Matches.org)/$($Matches.project)/$($Matches.repo)"
    }

    if ($trimmedRemoteUrl -match '^git@ssh\.dev\.azure\.com:v3/(?<org>[^/]+)/(?<project>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$') {
        return "$($Matches.org)/$($Matches.project)/$($Matches.repo)"
    }

    if ($trimmedRemoteUrl -match '^ssh://git@ssh\.dev\.azure\.com/v3/(?<org>[^/]+)/(?<project>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$') {
        return "$($Matches.org)/$($Matches.project)/$($Matches.repo)"
    }

    return $FallbackName
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

function Escape-Html {
    param([AllowNull()][string]$Text)

    if ($null -eq $Text) {
        return ""
    }

    return $Text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
}

function Normalize-OneLine {
    param(
        [AllowNull()][string]$Text,

        [int]$MaxLength = 400
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    $normalized = ($Text -replace "\r?\n", " ").Trim()
    if ($normalized.Length -le $MaxLength) {
        return $normalized
    }

    return $normalized.Substring(0, $MaxLength - 1) + "…"
}

function Read-JsonFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    try {
        $raw = Get-Content -LiteralPath $Path -Raw
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return $null
        }

        return $raw | ConvertFrom-Json -Depth 30
    }
    catch {
        return $null
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
        LastSent = Join-Path $stateDirectory "notify-last-sent.json"
    }
}

function Convert-ToStringArray {
    param([AllowNull()]$Value)

    $items = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $Value) {
        return @()
    }

    if (($Value -is [System.Collections.IEnumerable]) -and (-not ($Value -is [string]))) {
        foreach ($item in $Value) {
            if ($null -eq $item) {
                continue
            }

            $text = "$item".Trim()
            if (-not [string]::IsNullOrWhiteSpace($text)) {
                $items.Add($text)
            }
        }

        return $items.ToArray()
    }

    $singleValue = "$Value".Trim()
    if ([string]::IsNullOrWhiteSpace($singleValue)) {
        return @()
    }

    return @($singleValue)
}

function Get-MatchingSummaryRecord {
    param(
        [AllowNull()]$SummaryRecord,
        [AllowNull()]$SessionRecord
    )

    if ($null -eq $SummaryRecord) {
        return $null
    }

    $summaryRunId = if ($SummaryRecord.run_id) { "$($SummaryRecord.run_id)" } else { $null }
    $sessionRunId = if (($null -ne $SessionRecord) -and $SessionRecord.run_id) { "$($SessionRecord.run_id)" } else { $null }
    if ((-not [string]::IsNullOrWhiteSpace($sessionRunId)) -and ($summaryRunId -ne $sessionRunId)) {
        return $null
    }

    $summaryStatus = if ($SummaryRecord.status) { "$($SummaryRecord.status)" } else { $null }
    $summaryText = if ($SummaryRecord.summary) { "$($SummaryRecord.summary)" } else { $null }
    if ([string]::IsNullOrWhiteSpace($summaryStatus) -or ($summaryStatus -eq "pending")) {
        return $null
    }

    if ([string]::IsNullOrWhiteSpace($summaryText)) {
        return $null
    }

    return $SummaryRecord
}

function Should-SkipDuplicateNotification {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [AllowNull()][string]$RunId,

        [AllowNull()][string]$SummaryUpdatedAt
    )

    if ([string]::IsNullOrWhiteSpace($RunId)) {
        return $false
    }

    $previousNotification = Read-JsonFile -Path $Path
    if ($null -eq $previousNotification) {
        return $false
    }

    $previousRunId = if ($previousNotification.run_id) { "$($previousNotification.run_id)" } else { $null }
    if ($previousRunId -ne $RunId) {
        return $false
    }

    $previousSummaryUpdatedAt = if ($previousNotification.summary_updated_at) { "$($previousNotification.summary_updated_at)" } else { $null }
    if ([string]::IsNullOrWhiteSpace($SummaryUpdatedAt)) {
        return [string]::IsNullOrWhiteSpace($previousSummaryUpdatedAt)
    }

    return $previousSummaryUpdatedAt -eq $SummaryUpdatedAt
}

function Update-LastNotificationState {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [AllowNull()][string]$RunId,

        [Parameter(Mandatory = $true)]
        [string]$TimestampIso,

        [AllowNull()][string]$SummaryUpdatedAt
    )

    if ([string]::IsNullOrWhiteSpace($RunId)) {
        return
    }

    Write-JsonFile -Path $Path -Value ([ordered]@{
        version = 1
        run_id = $RunId
        sent_at = $TimestampIso
        summary_updated_at = $SummaryUpdatedAt
    })
}

try {
    $repoRoot = Get-RepoRootFromScript
    Import-DotEnvFile -Path (Join-Path $repoRoot ".env")

    $botToken = $env:TG_BOT_TOKEN
    $chatId = $env:TG_CHAT_ID
    if ((Test-IsPlaceholderValue $botToken) -or (Test-IsPlaceholderValue $chatId)) {
        Write-Host "Telegram hook skipped: TG_BOT_TOKEN or TG_CHAT_ID is not configured."
        exit 0
    }

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
    $hostName = [System.Net.Dns]::GetHostName()
    $executionEnvironment = Get-ExecutionEnvironment

    $gitAvailable = $null -ne (Get-Command git -ErrorAction SilentlyContinue)
    $gitWorkingDirectory = if (Test-Path -LiteralPath $cwd) { $cwd } else { $repoRoot }

    $resolvedRepoRoot = $null
    $branch = $null
    $sha = $null
    $remoteUrl = $null
    if ($gitAvailable) {
        $resolvedRepoRoot = Invoke-GitSafe -WorkingDirectory $gitWorkingDirectory -Arguments @("rev-parse", "--show-toplevel")
        $branch = Invoke-GitSafe -WorkingDirectory $gitWorkingDirectory -Arguments @("rev-parse", "--abbrev-ref", "HEAD")
        $sha = Invoke-GitSafe -WorkingDirectory $gitWorkingDirectory -Arguments @("rev-parse", "--short=12", "HEAD")
        $remoteUrl = Invoke-GitSafe -WorkingDirectory $gitWorkingDirectory -Arguments @("remote", "get-url", "origin")
    }

    if ([string]::IsNullOrWhiteSpace($resolvedRepoRoot)) {
        $resolvedRepoRoot = $repoRoot
    }

    $notifyStatePaths = Get-NotifyStatePaths -RepoRoot $resolvedRepoRoot
    $sessionState = Read-JsonFile -Path $notifyStatePaths.Session
    $summaryState = Read-JsonFile -Path $notifyStatePaths.Summary
    $matchingSummary = Get-MatchingSummaryRecord -SummaryRecord $summaryState -SessionRecord $sessionState
    $sessionId = if ($hookInput.sessionId) { "$($hookInput.sessionId)" } else { $null }
    $transcriptPath = if ($hookInput.transcript_path) { "$($hookInput.transcript_path)" } else { $null }
    $stopHookActive = $false
    if ($null -ne $hookInput.stop_hook_active) {
        $stopHookActive = [bool]$hookInput.stop_hook_active
    }

    $repoName = Split-Path -Leaf $resolvedRepoRoot
    if ([string]::IsNullOrWhiteSpace($repoName)) {
        $repoName = "unknown-repo"
    }
    $repoDisplayName = Get-RepositoryDisplayName -RemoteUrl $remoteUrl -FallbackName $repoName

    if ([string]::IsNullOrWhiteSpace($branch) -or $branch -eq "HEAD") {
        $branch = "detached"
    }

    if ([string]::IsNullOrWhiteSpace($sha)) {
        $sha = "unknown"
    }

    $sessionRunId = if (($null -ne $sessionState) -and $sessionState.run_id) { "$($sessionState.run_id)" } else { $null }

    if ([string]::IsNullOrWhiteSpace($sessionRunId)) {
        exit 0
    }

    $runId = $sessionRunId

    $summaryUpdatedAt = if (($null -ne $matchingSummary) -and $matchingSummary.updated_at) { "$($matchingSummary.updated_at)" } else { $null }

    if (Should-SkipDuplicateNotification -Path $notifyStatePaths.LastSent -RunId $runId -SummaryUpdatedAt $summaryUpdatedAt) {
        exit 0
    }

    $headline = "Copilot task finished"
    $emoji = "✅"

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("<b>$emoji $headline</b>")
    $lines.Add("<b>run_id</b>: <code>$(Escape-Html $runId)</code>")
    if (-not [string]::IsNullOrWhiteSpace($sessionId)) {
        $lines.Add("<b>session_id</b>: <code>$(Escape-Html $sessionId)</code>")
    }
    $lines.Add("")
    $lines.Add("<b>host</b>: <code>$(Escape-Html $hostName)</code>")
    $lines.Add("<b>env</b>: <code>$(Escape-Html $executionEnvironment)</code>")
    $lines.Add("<b>repo</b>: <code>$(Escape-Html $repoDisplayName)</code>")
    $lines.Add("<b>worktree</b>: <code>$(Escape-Html $cwd)</code>")
    $lines.Add("<b>branch</b>: <code>$(Escape-Html $branch)</code>")
    $lines.Add("<b>sha</b>: <code>$(Escape-Html $sha)</code>")
    $lines.Add("<b>timestamp</b>: <code>$(Escape-Html $timestampIso)</code>")
    if ($stopHookActive) {
        $lines.Add("<b>stop_hook_active</b>: <code>true</code>")
    }
    if (-not [string]::IsNullOrWhiteSpace($transcriptPath)) {
        $lines.Add("<b>transcript_path</b>: <code>$(Escape-Html $transcriptPath)</code>")
    }

    if ($null -ne $matchingSummary) {
        $summaryStatus = Normalize-OneLine -Text "$($matchingSummary.status)" -MaxLength 40
        $summaryText = Normalize-OneLine -Text "$($matchingSummary.summary)" -MaxLength 800
        $detailItems = Convert-ToStringArray -Value $matchingSummary.details
        $changedFiles = Convert-ToStringArray -Value $matchingSummary.changed_files
        $nextSteps = Convert-ToStringArray -Value $matchingSummary.next_steps

        $lines.Add("")
        $lines.Add("<b>summary_status</b>: <code>$(Escape-Html $summaryStatus)</code>")
        $lines.Add("<b>summary</b>: $(Escape-Html $summaryText)")

        if ($detailItems.Count -gt 0) {
            $lines.Add("<b>details</b>:")
            $detailPreview = @($detailItems | Select-Object -First 5)
            foreach ($detailItem in $detailPreview) {
                $lines.Add("• $(Escape-Html (Normalize-OneLine -Text $detailItem -MaxLength 220))")
            }

            $remainingDetails = $detailItems.Count - $detailPreview.Count
            if ($remainingDetails -gt 0) {
                $lines.Add("• <i>+$remainingDetails more</i>")
            }
        }

        if ($changedFiles.Count -gt 0) {
            $lines.Add("<b>changed_files</b>:")
            $filePreview = @($changedFiles | Select-Object -First 8)
            foreach ($changedFile in $filePreview) {
                $lines.Add("• <code>$(Escape-Html (Normalize-OneLine -Text $changedFile -MaxLength 160))</code>")
            }

            $remainingFiles = $changedFiles.Count - $filePreview.Count
            if ($remainingFiles -gt 0) {
                $lines.Add("• <i>+$remainingFiles more</i>")
            }
        }

        if ($nextSteps.Count -gt 0) {
            $lines.Add("<b>next_steps</b>:")
            $nextStepPreview = @($nextSteps | Select-Object -First 5)
            foreach ($nextStep in $nextStepPreview) {
                $lines.Add("• $(Escape-Html (Normalize-OneLine -Text $nextStep -MaxLength 220))")
            }

            $remainingSteps = $nextSteps.Count - $nextStepPreview.Count
            if ($remainingSteps -gt 0) {
                $lines.Add("• <i>+$remainingSteps more</i>")
            }
        }
    }

    $message = $lines -join "`n"
    if ($message.Length -gt 3900) {
        $message = $message.Substring(0, 3880) + "`n<i>(truncated)</i>"
    }

    $payload = @{
        chat_id = $chatId
        text = $message
        parse_mode = "HTML"
        disable_web_page_preview = $true
    }

    $uri = "https://api.telegram.org/bot$botToken/sendMessage"
    [void](Invoke-RestMethod -Method Post -Uri $uri -ContentType "application/json" -Body ($payload | ConvertTo-Json -Depth 10 -Compress) -TimeoutSec 5)
    Update-LastNotificationState -Path $notifyStatePaths.LastSent -RunId $runId -TimestampIso $timestampIso -SummaryUpdatedAt $summaryUpdatedAt
    exit 0
}
catch {
    Write-Host "Telegram hook failed: $($_.Exception.Message)"
    exit 0
}
