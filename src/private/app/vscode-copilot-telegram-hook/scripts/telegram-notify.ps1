[CmdletBinding()]

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

. (Join-Path $PSScriptRoot "CopilotHook.Common.ps1")

function ConvertTo-HtmlEscapedText {
    param([AllowNull()][string]$Text)

    if ($null -eq $Text) {
        return ""
    }

    return $Text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
}

function Format-OneLineText {
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

    return $normalized.Substring(0, $MaxLength - 3) + "..."
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

function Test-NotificationAlreadySent {
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

function Set-LastNotificationState {
    [CmdletBinding(SupportsShouldProcess)]
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

    if ($PSCmdlet.ShouldProcess($Path, "Write last notification state")) {
        Write-JsonFile -Path $Path -Value ([ordered]@{
                version            = 1
                run_id             = $RunId
                sent_at            = $TimestampIso
                summary_updated_at = $SummaryUpdatedAt
            })
    }
}

function Get-NotificationHeading {
    param([AllowNull()]$SummaryRecord)

    $status = if (($null -ne $SummaryRecord) -and $SummaryRecord.status) {
        "$($SummaryRecord.status)".Trim().ToLowerInvariant()
    }
    else {
        ""
    }

    switch ($status) {
        "success" {
            return [ordered]@{
                Prefix   = "[OK]"
                Headline = "Copilot task finished"
            }
        }
        "info" {
            return [ordered]@{
                Prefix   = "[INFO]"
                Headline = "Copilot task finished"
            }
        }
        { $_ -in @("failure", "failed", "error") } {
            return [ordered]@{
                Prefix   = "[FAIL]"
                Headline = "Copilot task failed"
            }
        }
        default {
            return [ordered]@{
                Prefix   = "[DONE]"
                Headline = "Copilot task finished"
            }
        }
    }
}

function Assert-TelegramSendResponse {
    param([AllowNull()]$Response)

    if ($null -eq $Response) {
        throw "Telegram API returned no response."
    }

    $okProperty = $Response.PSObject.Properties["ok"]
    if ($null -eq $okProperty) {
        throw "Telegram API response did not include an 'ok' flag."
    }

    if (-not [bool]$okProperty.Value) {
        $descriptionProperty = $Response.PSObject.Properties["description"]
        $description = if ($null -ne $descriptionProperty) {
            "$($descriptionProperty.Value)"
        }
        else {
            "Telegram API returned ok=false."
        }

        throw "Telegram API rejected the message: $description"
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
    $resolvedRepoRoot = $workspaceContext.RepoRoot
    $branch = $workspaceContext.Branch
    $sha = $workspaceContext.Sha
    $remoteUrl = $workspaceContext.RemoteUrl

    $credentials = Get-TelegramCredential
    $botToken = $credentials.BotToken
    $chatId = $credentials.ChatId
    if ((Test-IsPlaceholderValue -Value $botToken) -or (Test-IsPlaceholderValue -Value $chatId)) {
        Write-Verbose -Message "Telegram hook skipped: configure TG_BOT_TOKEN/TG_CHAT_ID or gopass secrets under '$($credentials.SecretPrefix)'."
        exit 0
    }

    $hostName = [System.Net.Dns]::GetHostName()
    $executionEnvironment = Get-ExecutionEnvironment
    $notifyStatePaths = Get-NotifyStatePath -StateRoot $cwd
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

    if (Test-NotificationAlreadySent -Path $notifyStatePaths.LastSent -RunId $runId -SummaryUpdatedAt $summaryUpdatedAt) {
        exit 0
    }

    $notificationHeading = Get-NotificationHeading -SummaryRecord $matchingSummary
    $headline = $notificationHeading.Headline
    $statusPrefix = $notificationHeading.Prefix

    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("<b>$statusPrefix $headline</b>")
    $lines.Add("<b>run_id</b>: <code>$(ConvertTo-HtmlEscapedText $runId)</code>")
    if (-not [string]::IsNullOrWhiteSpace($sessionId)) {
        $lines.Add("<b>session_id</b>: <code>$(ConvertTo-HtmlEscapedText $sessionId)</code>")
    }
    $lines.Add("")
    $lines.Add("<b>host</b>: <code>$(ConvertTo-HtmlEscapedText $hostName)</code>")
    $lines.Add("<b>env</b>: <code>$(ConvertTo-HtmlEscapedText $executionEnvironment)</code>")
    $lines.Add("<b>repo</b>: <code>$(ConvertTo-HtmlEscapedText $repoDisplayName)</code>")
    $lines.Add("<b>worktree</b>: <code>$(ConvertTo-HtmlEscapedText $cwd)</code>")
    $lines.Add("<b>branch</b>: <code>$(ConvertTo-HtmlEscapedText $branch)</code>")
    $lines.Add("<b>sha</b>: <code>$(ConvertTo-HtmlEscapedText $sha)</code>")
    $lines.Add("<b>timestamp</b>: <code>$(ConvertTo-HtmlEscapedText $timestampIso)</code>")
    if ($stopHookActive) {
        $lines.Add("<b>stop_hook_active</b>: <code>true</code>")
    }
    if (-not [string]::IsNullOrWhiteSpace($transcriptPath)) {
        $lines.Add("<b>transcript_path</b>: <code>$(ConvertTo-HtmlEscapedText $transcriptPath)</code>")
    }

    if ($null -ne $matchingSummary) {
        $summaryStatus = Format-OneLineText -Text "$($matchingSummary.status)" -MaxLength 40
        $summaryText = Format-OneLineText -Text "$($matchingSummary.summary)" -MaxLength 800
        $detailItems = Convert-ToStringArray -Value $matchingSummary.details
        $changedFiles = Convert-ToStringArray -Value $matchingSummary.changed_files
        $nextSteps = Convert-ToStringArray -Value $matchingSummary.next_steps

        $lines.Add("")
        $lines.Add("<b>summary_status</b>: <code>$(ConvertTo-HtmlEscapedText $summaryStatus)</code>")
        $lines.Add("<b>summary</b>: $(ConvertTo-HtmlEscapedText $summaryText)")

        if ($detailItems.Count -gt 0) {
            $lines.Add("<b>details</b>:")
            $detailPreview = @($detailItems | Select-Object -First 5)
            foreach ($detailItem in $detailPreview) {
                $lines.Add("- $(ConvertTo-HtmlEscapedText (Format-OneLineText -Text $detailItem -MaxLength 220))")
            }

            $remainingDetails = $detailItems.Count - $detailPreview.Count
            if ($remainingDetails -gt 0) {
                $lines.Add("- <i>+$remainingDetails more</i>")
            }
        }

        if ($changedFiles.Count -gt 0) {
            $lines.Add("<b>changed_files</b>:")
            $filePreview = @($changedFiles | Select-Object -First 8)
            foreach ($changedFile in $filePreview) {
                $lines.Add("- <code>$(ConvertTo-HtmlEscapedText (Format-OneLineText -Text $changedFile -MaxLength 160))</code>")
            }

            $remainingFiles = $changedFiles.Count - $filePreview.Count
            if ($remainingFiles -gt 0) {
                $lines.Add("- <i>+$remainingFiles more</i>")
            }
        }

        if ($nextSteps.Count -gt 0) {
            $lines.Add("<b>next_steps</b>:")
            $nextStepPreview = @($nextSteps | Select-Object -First 5)
            foreach ($nextStep in $nextStepPreview) {
                $lines.Add("- $(ConvertTo-HtmlEscapedText (Format-OneLineText -Text $nextStep -MaxLength 220))")
            }

            $remainingSteps = $nextSteps.Count - $nextStepPreview.Count
            if ($remainingSteps -gt 0) {
                $lines.Add("- <i>+$remainingSteps more</i>")
            }
        }
    }

    $message = $lines -join "`n"
    if ($message.Length -gt 3900) {
        $message = $message.Substring(0, 3880) + "`n<i>(truncated)</i>"
    }

    $payload = @{
        chat_id                  = $chatId
        text                     = $message
        parse_mode               = "HTML"
        disable_web_page_preview = $true
    }

    $uri = "https://api.telegram.org/bot$botToken/sendMessage"
    $response = Invoke-RestMethod -Method Post -Uri $uri -ContentType "application/json" -Body ($payload | ConvertTo-Json -Depth 10 -Compress) -TimeoutSec 5
    Assert-TelegramSendResponse -Response $response
    Set-LastNotificationState -Path $notifyStatePaths.LastSent -RunId $runId -TimestampIso $timestampIso -SummaryUpdatedAt $summaryUpdatedAt
    exit 0
}
catch {
    Write-Warning -Message "Telegram hook failed: $($_.Exception.Message)"
    exit 0
}
