$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

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
        Write-Verbose -Message "Ignoring git command failure while probing repository context."
    }

    return $null
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

function Get-NotifyStatePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$StateRoot
    )

    $stateDirectory = Join-Path $StateRoot ".copilot"

    return [ordered]@{
        Directory = $stateDirectory
        Session   = Join-Path $stateDirectory "notify-session.json"
        Summary   = Join-Path $stateDirectory "notify-summary.json"
        LastSent  = Join-Path $stateDirectory "notify-last-sent.json"
    }
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
            Write-Verbose -Message "Unable to inspect the Linux kernel release while detecting the execution environment."
        }

        return "linux"
    }

    if ($IsMacOS) {
        return "macos"
    }

    return "unknown"
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

function Test-IsPlaceholderValue {
    param([AllowNull()][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $true
    }

    return $Value -match '^__.+__$'
}

function Resolve-HookWorkspaceContext {
    param(
        [AllowNull()]$HookInput,

        [Parameter(Mandatory = $true)]
        [string]$FallbackPath
    )

    $cwd = if (($null -ne $HookInput) -and $HookInput.cwd) {
        "$($HookInput.cwd)"
    }
    else {
        $FallbackPath
    }

    if ([string]::IsNullOrWhiteSpace($cwd) -or (-not (Test-Path -LiteralPath $cwd))) {
        $cwd = $FallbackPath
    }

    $resolvedRepoRoot = $null
    $branch = $null
    $sha = $null
    $remoteUrl = $null
    $gitAvailable = $null -ne (Get-Command git -ErrorAction SilentlyContinue)
    if ($gitAvailable) {
        $resolvedRepoRoot = Invoke-GitSafe -WorkingDirectory $cwd -Arguments @("rev-parse", "--show-toplevel")
        $branch = Invoke-GitSafe -WorkingDirectory $cwd -Arguments @("rev-parse", "--abbrev-ref", "HEAD")
        $sha = Invoke-GitSafe -WorkingDirectory $cwd -Arguments @("rev-parse", "--short=12", "HEAD")
        $remoteUrl = Invoke-GitSafe -WorkingDirectory $cwd -Arguments @("remote", "get-url", "origin")
    }

    if ([string]::IsNullOrWhiteSpace($resolvedRepoRoot)) {
        $resolvedRepoRoot = $cwd
    }

    return [ordered]@{
        Cwd       = $cwd
        RepoRoot  = $resolvedRepoRoot
        Branch    = $branch
        Sha       = $sha
        RemoteUrl = $remoteUrl
    }
}

function Get-GopassSecret {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SecretPath
    )

    $gopassCommand = Get-Command gopass -ErrorAction SilentlyContinue
    if ($null -eq $gopassCommand) {
        return $null
    }

    foreach ($arguments in @(
            @("show", "--password", $SecretPath),
            @("show", "-o", $SecretPath)
        )) {
        try {
            $raw = & $gopassCommand.Source @arguments 2>$null
            $value = ($raw -join "`n").Trim()
            if (($LASTEXITCODE -eq 0) -and (-not [string]::IsNullOrWhiteSpace($value))) {
                return $value
            }
        }
        catch {
            Write-Verbose -Message "Ignoring gopass retrieval failure while resolving Telegram credentials."
        }
    }

    return $null
}

function Get-TelegramSecretPrefix {
    if (-not [string]::IsNullOrWhiteSpace($env:COPILOT_TELEGRAM_GOPASS_PREFIX)) {
        return $env:COPILOT_TELEGRAM_GOPASS_PREFIX.Trim()
    }

    return "copilot/vscode-copilot-telegram-hook"
}

function Get-TelegramCredential {
    $secretPrefix = Get-TelegramSecretPrefix
    $botToken = $env:TG_BOT_TOKEN
    $chatId = $env:TG_CHAT_ID

    if (Test-IsPlaceholderValue -Value $botToken) {
        $botToken = Get-GopassSecret -SecretPath "$secretPrefix/bot-token"
    }

    if (Test-IsPlaceholderValue -Value $chatId) {
        $chatId = Get-GopassSecret -SecretPath "$secretPrefix/chat-id"
    }

    return [ordered]@{
        BotToken     = $botToken
        ChatId       = $chatId
        SecretPrefix = $secretPrefix
    }
}
