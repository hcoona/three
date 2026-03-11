[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Stop", "sessionEnd", "errorOccurred")]
    [string]$EventName
)

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

function Get-ShortHash {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Text,

        [int]$Length = 8
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

    [int64]$timestampMs = 0
    if ($null -ne $hookInput.timestamp) {
        [void][int64]::TryParse("$($hookInput.timestamp)", [ref]$timestampMs)
    }
    if ($timestampMs -le 0) {
        $timestampMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    }

    $eventTime = [DateTimeOffset]::FromUnixTimeMilliseconds($timestampMs).ToUniversalTime()
    $timestampIso = $eventTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
    $timestampCompact = $eventTime.ToString("yyyyMMddTHHmmssZ")

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

    $worktreeLeaf = Split-Path -Leaf $cwd
    if ([string]::IsNullOrWhiteSpace($worktreeLeaf)) {
        $worktreeLeaf = "root"
    }

    $worktreeTag = "$worktreeLeaf-$(Get-ShortHash -Text $cwd -Length 8)"
    $runId = "$hostName|$executionEnvironment|$repoName|$worktreeTag|$branch|$sha|$timestampCompact"

    $source = if ($hookInput.source) { "$($hookInput.source)" } else { $null }

    $headline = "Copilot hook event"
    $emoji = "ℹ️"
    $reason = if ($hookInput.reason) { "$($hookInput.reason)" } else { $null }
    if ($EventName -eq "Stop") {
        $headline = "Copilot agent stopped"
        $emoji = "ℹ️"
        if (-not [string]::IsNullOrWhiteSpace($reason)) {
            switch ($reason) {
                "complete" { $headline = "Copilot session completed"; $emoji = "✅" }
                "error" { $headline = "Copilot session ended with error"; $emoji = "⚠️" }
                "abort" { $headline = "Copilot session aborted"; $emoji = "⚠️" }
                "timeout" { $headline = "Copilot session timed out"; $emoji = "⏱️" }
                "user_exit" { $headline = "Copilot session exited by user"; $emoji = "👋" }
                default { $headline = "Copilot agent stopped"; $emoji = "ℹ️" }
            }
        }
    }
    elseif ($EventName -eq "sessionEnd") {
        switch ($reason) {
            "complete" { $headline = "Copilot session completed"; $emoji = "✅" }
            "error" { $headline = "Copilot session ended with error"; $emoji = "⚠️" }
            "abort" { $headline = "Copilot session aborted"; $emoji = "⚠️" }
            "timeout" { $headline = "Copilot session timed out"; $emoji = "⏱️" }
            "user_exit" { $headline = "Copilot session exited by user"; $emoji = "👋" }
            default { $headline = "Copilot session ended"; $emoji = "ℹ️" }
        }
    }
    elseif ($EventName -eq "errorOccurred") {
        $headline = "Copilot error occurred"
        $emoji = "❌"
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("<b>$emoji $headline</b>")
    $lines.Add("<b>event</b>: <code>$(Escape-Html $EventName)</code>")
    $lines.Add("<b>run_id</b>: <code>$(Escape-Html $runId)</code>")
    $lines.Add("")
    $lines.Add("<b>host</b>: <code>$(Escape-Html $hostName)</code>")
    $lines.Add("<b>env</b>: <code>$(Escape-Html $executionEnvironment)</code>")
    if (-not [string]::IsNullOrWhiteSpace($source)) {
        $lines.Add("<b>source</b>: <code>$(Escape-Html $source)</code>")
    }
    $lines.Add("<b>repo</b>: <code>$(Escape-Html $repoDisplayName)</code>")
    $lines.Add("<b>worktree</b>: <code>$(Escape-Html $cwd)</code>")
    $lines.Add("<b>branch</b>: <code>$(Escape-Html $branch)</code>")
    $lines.Add("<b>sha</b>: <code>$(Escape-Html $sha)</code>")
    $lines.Add("<b>timestamp</b>: <code>$(Escape-Html $timestampIso)</code>")

    if (($EventName -in @("Stop", "sessionEnd")) -and (-not [string]::IsNullOrWhiteSpace($reason))) {
        $lines.Add("<b>reason</b>: <code>$(Escape-Html $reason)</code>")
    }
    elseif ($EventName -eq "errorOccurred") {
        $errorName = if ($hookInput.error.name) { "$($hookInput.error.name)" } else { "UnknownError" }
        $errorMessage = if ($hookInput.error.message) { "$($hookInput.error.message)" } else { "No message provided" }
        $errorStack = if ($hookInput.error.stack) { Normalize-OneLine -Text "$($hookInput.error.stack)" -MaxLength 900 } else { "" }

        $lines.Add("<b>error</b>: <code>$(Escape-Html $errorName)</code>")
        $lines.Add("<b>message</b>: <code>$(Escape-Html (Normalize-OneLine -Text $errorMessage -MaxLength 700))</code>")
        if (-not [string]::IsNullOrWhiteSpace($errorStack)) {
            $lines.Add("<b>stack</b>: <code>$(Escape-Html $errorStack)</code>")
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
    exit 0
}
catch {
    Write-Host "Telegram hook failed: $($_.Exception.Message)"
    exit 0
}
