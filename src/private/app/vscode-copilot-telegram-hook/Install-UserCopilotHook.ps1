[CmdletBinding()]
param(
    [ValidateSet("Auto", "Copy", "Hardlink", "Cow")]
    [string]$InstallMode = "Auto",

    [string]$TelegramBotToken,

    [string]$TelegramChatId,

    [switch]$SkipSecretPrompt
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$gopassPrefix = "copilot/vscode-copilot-telegram-hook"
$managedHookId = "hcoona.vscode-copilot-telegram-hook"

function Get-HeadlessInstallGuidance {
    return "Re-run the installer without -NonInteractive for guided prompts, or run it headlessly with -TelegramBotToken, -TelegramChatId, and optionally -SkipSecretPrompt. Environment variables TG_BOT_TOKEN and TG_CHAT_ID are also accepted as input."
}

function Test-IsPromptUnavailableError {
    param([AllowNull()][System.Exception]$Exception)

    if ($null -eq $Exception) {
        return $false
    }

    return "$($Exception.Message)" -like "*PowerShell is in NonInteractive mode. Read and Prompt functionality is not available.*"
}

function Get-UserHomeDirectory {
    if ($IsWindows) {
        return $env:USERPROFILE
    }

    return $HOME
}

function Get-UserInstallRoot {
    if ($IsWindows) {
        $localAppData = if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
            $env:LOCALAPPDATA
        }
        else {
            Join-Path (Get-UserHomeDirectory) "AppData/Local"
        }

        return (Join-Path (Join-Path $localAppData "hcoona") "vscode-copilot-telegram-hook")
    }

    return (Join-Path (Join-Path (Join-Path (Get-UserHomeDirectory) ".local") "share/hcoona") "vscode-copilot-telegram-hook")
}

function Get-UserHookConfigPath {
    $claudeDirectory = Join-Path -Path (Get-UserHomeDirectory) -ChildPath ".claude"
    return (Join-Path -Path $claudeDirectory -ChildPath "settings.json")
}

function Get-UserInstructionsRoot {
    return (Join-Path (Join-Path (Get-UserHomeDirectory) ".copilot") "instructions")
}

function Get-UserInstructionFilePath {
    return (Join-Path (Get-UserInstructionsRoot) "hcoona-vscode-copilot-telegram-hook.instructions.md")
}

function Initialize-Directory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Read-JsonMapFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return [ordered]@{}
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return [ordered]@{}
    }

    return ($raw | ConvertFrom-Json -AsHashtable -Depth 50)
}

function Write-JsonMapFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [hashtable]$Value
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        Initialize-Directory -Path $directory
    }

    $Value | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Test-MapContainsKey {
    param(
        [AllowNull()]$Map,

        [Parameter(Mandatory = $true)]
        [string]$Key
    )

    if ($null -eq $Map) {
        return $false
    }

    if ($Map -is [hashtable]) {
        return $Map.ContainsKey($Key)
    }

    if ($Map -is [System.Collections.IDictionary]) {
        return $Map.Contains($Key)
    }

    return $false
}

function Test-IsDictionaryObject {
    param([AllowNull()]$Value)

    if ($null -eq $Value) {
        return $false
    }

    return $Value -is [System.Collections.IDictionary]
}

function Test-CommandAvailable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$CommandName
    )

    return $null -ne (Get-Command $CommandName -ErrorAction SilentlyContinue)
}

function Test-GopassReady {
    if (-not (Test-CommandAvailable -CommandName "gopass")) {
        throw "gopass is required but was not found in PATH."
    }

    try {
        & gopass mounts 1>$null 2>$null
        if ($LASTEXITCODE -ne 0) {
            throw "gopass returned exit code $LASTEXITCODE"
        }
    }
    catch {
        throw "gopass is not initialized or not ready in this environment."
    }
}

function Get-GopassSecret {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SecretPath
    )

    foreach ($arguments in @(
            @("show", "--password", $SecretPath),
            @("show", "-o", $SecretPath)
        )) {
        try {
            $raw = & gopass @arguments 2>$null
            $value = ($raw -join "`n").Trim()
            if (($LASTEXITCODE -eq 0) -and (-not [string]::IsNullOrWhiteSpace($value))) {
                return $value
            }
        }
        catch {
            Write-Verbose ("gopass lookup failed for '{0}' with arguments '{1}': {2}" -f $SecretPath, ($arguments -join " "), $_.Exception.Message)
        }
    }

    return $null
}

function ConvertTo-PlainText {
    param(
        [Parameter(Mandatory = $true)]
        [Security.SecureString]$Value
    )

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        if ($bstr -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        }
    }
}

function Set-GopassSecret {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [string]$SecretPath,

        [Parameter(Mandatory = $true)]
        [string]$SecretValue
    )

    if ([string]::IsNullOrWhiteSpace($SecretValue)) {
        throw "Secret value for '$SecretPath' cannot be empty."
    }

    if ($PSCmdlet.ShouldProcess($SecretPath, "Write gopass secret")) {
        $SecretValue | & gopass insert -f $SecretPath 1>$null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to write '$SecretPath' into gopass."
        }
    }
}

function Confirm-UpdateExistingValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    try {
        $reply = Read-Host "$Label already exists in gopass. Update it now? [y/N]"
        return $reply -match '^(?i)y(?:es)?$'
    }
    catch {
        if (Test-IsPromptUnavailableError -Exception $_.Exception) {
            return $false
        }

        throw
    }
}

function Read-RequiredSecret {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt
    )

    while ($true) {
        $value = ConvertTo-PlainText -Value (Read-Host -AsSecureString $Prompt)

        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value
        }

        Write-Warning "A value is required."
    }
}

function Read-RequiredText {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Prompt
    )

    while ($true) {
        $value = (Read-Host $Prompt).Trim()

        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value
        }

        Write-Warning "A value is required."
    }
}

function Get-SecretValueResolution {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$ShouldUpdate,

        [AllowNull()][string]$Value
    )

    return [ordered]@{
        ShouldUpdate = $ShouldUpdate
        Value        = $Value
    }
}

function Read-ResolvedSecretValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,

        [Parameter(Mandatory = $true)]
        [bool]$IsSecure
    )

    if ($IsSecure) {
        return (Read-RequiredSecret -Prompt $Label)
    }

    return (Read-RequiredText -Prompt $Label)
}

function Resolve-SecretValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Label,

        [AllowNull()][string]$ProvidedValue,

        [AllowNull()][string]$ExistingValue,

        [Parameter(Mandatory = $true)]
        [bool]$IsSecure,

        [Parameter(Mandatory = $true)]
        [bool]$SkipPrompt
    )

    if (-not [string]::IsNullOrWhiteSpace($ProvidedValue)) {
        return (Get-SecretValueResolution -ShouldUpdate $true -Value $ProvidedValue)
    }

    if ($SkipPrompt) {
        if (-not [string]::IsNullOrWhiteSpace($ExistingValue)) {
            return (Get-SecretValueResolution -ShouldUpdate $false -Value $ExistingValue)
        }

        throw "$Label is required when -SkipSecretPrompt is used and no existing gopass value is present."
    }

    if ([string]::IsNullOrWhiteSpace($ExistingValue)) {
        $value = Read-ResolvedSecretValue -Label $Label -IsSecure $IsSecure
        return (Get-SecretValueResolution -ShouldUpdate $true -Value $value)
    }

    if (-not (Confirm-UpdateExistingValue -Label $Label)) {
        return (Get-SecretValueResolution -ShouldUpdate $false -Value $ExistingValue)
    }

    $updatedValue = Read-ResolvedSecretValue -Label $Label -IsSecure $IsSecure
    return (Get-SecretValueResolution -ShouldUpdate $true -Value $updatedValue)
}

function Test-IsLikelyTelegramBotToken {
    param([AllowNull()][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    return $Value -match '^\d{6,}:[A-Za-z0-9_-]{20,}$'
}

function Test-IsLikelyTelegramChatId {
    param([AllowNull()][string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    return $Value -match '^-?\d{5,}$'
}

function Assert-ValidTelegramConfig {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BotToken,

        [Parameter(Mandatory = $true)]
        [string]$ChatId
    )

    if (-not (Test-IsLikelyTelegramBotToken -Value $BotToken)) {
        throw "Telegram bot token does not look valid. Expected a value like '<digits>:<token>' from BotFather."
    }

    if (-not (Test-IsLikelyTelegramChatId -Value $ChatId)) {
        throw "Telegram chat ID does not look valid. Expected a signed or unsigned numeric chat identifier."
    }
}

function Install-ManagedFileOnce {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Copy", "Hardlink", "Cow")]
        [string]$Mode
    )

    $destinationDirectory = Split-Path -Parent $DestinationPath
    if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
        Initialize-Directory -Path $destinationDirectory
    }

    if (Test-Path -LiteralPath $DestinationPath) {
        Remove-Item -LiteralPath $DestinationPath -Force
    }

    switch ($Mode) {
        "Copy" {
            Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force
        }
        "Hardlink" {
            [void](New-Item -ItemType HardLink -Path $DestinationPath -Target $SourcePath)
        }
        "Cow" {
            if ($IsWindows) {
                throw "Copy-on-write installation is not supported in native Windows mode. Use Copy or Hardlink instead."
            }

            $cpCommand = Get-Command -Name "cp" -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($null -eq $cpCommand) {
                throw "cp is required for copy-on-write installation mode but was not found in PATH."
            }

            $cpPath = $cpCommand.Source
            & $cpPath "--reflink=always" "--preserve=mode,timestamps" "--" $SourcePath $DestinationPath 2>$null
            if (($LASTEXITCODE -ne 0) -or (-not (Test-Path -LiteralPath $DestinationPath))) {
                throw "cp --reflink=always failed for '$SourcePath'."
            }
        }
    }
}

function Install-ManagedFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$SourcePath,

        [Parameter(Mandatory = $true)]
        [string]$DestinationPath,

        [Parameter(Mandatory = $true)]
        [ValidateSet("Auto", "Copy", "Hardlink", "Cow")]
        [string]$Mode
    )

    $candidateModes = switch ($Mode) {
        "Auto" {
            if ($IsWindows) {
                @("Hardlink", "Copy")
            }
            else {
                @("Cow", "Hardlink", "Copy")
            }
        }
        "Cow" {
            if ($IsWindows) {
                @("Hardlink", "Copy")
            }
            else {
                @("Cow", "Hardlink", "Copy")
            }
        }
        default {
            @($Mode)
        }
    }

    $lastError = $null
    foreach ($candidateMode in $candidateModes) {
        try {
            Install-ManagedFileOnce -SourcePath $SourcePath -DestinationPath $DestinationPath -Mode $candidateMode
            return $candidateMode
        }
        catch {
            $lastError = $_
        }
    }

    throw $lastError
}

function Format-HookCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptPath
    )

    return ('pwsh -NoLogo -NoProfile -NonInteractive -File "{0}"' -f $ScriptPath)
}

function Test-IsManagedHookEntry {
    param(
        [AllowNull()]$Entry,

        [Parameter(Mandatory = $true)]
        [string]$InstallRoot
    )

    if ($null -eq $Entry) {
        return $false
    }

    $environment = $Entry["env"]
    if (($null -ne $environment) -and ($environment["COPILOT_TELEGRAM_HOOK_ID"] -eq $managedHookId)) {
        return $true
    }

    foreach ($propertyName in @("command", "windows", "linux", "osx")) {
        $propertyValue = $Entry[$propertyName]
        if ([string]::IsNullOrWhiteSpace($propertyValue)) {
            continue
        }

        if ($propertyValue.Contains($InstallRoot)) {
            return $true
        }
    }

    return $false
}

function Get-ManagedHookEventEntryCollection {
    param(
        [AllowEmptyCollection()]
        [AllowNull()]
        [array]$ExistingEntries,

        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Entry,

        [Parameter(Mandatory = $true)]
        [string]$InstallRoot
    )

    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($existingEntry in $existingEntries) {
        if (-not (Test-IsManagedHookEntry -Entry $existingEntry -InstallRoot $InstallRoot)) {
            $entries.Add($existingEntry)
        }
    }

    $entries.Add($Entry)
    return $entries.ToArray()
}

function Get-ManagedHookEntry {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ScriptPath,

        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds,

        [Parameter(Mandatory = $true)]
        [string]$SecretPrefix
    )

    return [ordered]@{
        type    = "command"
        command = (Format-HookCommand -ScriptPath $ScriptPath)
        cwd     = "."
        env     = [ordered]@{
            COPILOT_TELEGRAM_HOOK_ID       = $managedHookId
            COPILOT_TELEGRAM_GOPASS_PREFIX = $SecretPrefix
        }
        timeout = $TimeoutSeconds
    }
}

function Write-InstallManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$InstallMode,

        [Parameter(Mandatory = $true)]
        [string]$GopassPrefix,

        [Parameter(Mandatory = $true)]
        [string]$HookConfigPath,

        [Parameter(Mandatory = $true)]
        [string]$InstructionFilePath,

        [Parameter(Mandatory = $true)]
        [array]$Files
    )

    $manifest = [ordered]@{
        version               = 1
        installed_at          = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        install_mode          = $InstallMode
        gopass_prefix         = $GopassPrefix
        hook_config_path      = $HookConfigPath
        instruction_file_path = $InstructionFilePath
        files                 = $Files
    }

    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Path -Encoding utf8
}

try {
    if (-not (Test-CommandAvailable -CommandName "pwsh")) {
        throw "pwsh must be available in PATH so VS Code can run the installed hooks."
    }

    Test-GopassReady

    $sourceRoot = $PSScriptRoot
    $sourceInstructionsRoot = Join-Path $sourceRoot "instructions"
    $installRoot = Get-UserInstallRoot
    $installScriptsRoot = Join-Path $installRoot "scripts"
    $hookConfigPath = Get-UserHookConfigPath
    $instructionFilePath = Get-UserInstructionFilePath
    $manifestPath = Join-Path $installRoot "install-manifest.json"
    $hookConfigDirectory = Split-Path -Parent $hookConfigPath
    $instructionDirectory = Split-Path -Parent $instructionFilePath

    Initialize-Directory -Path $installRoot
    Initialize-Directory -Path $installScriptsRoot
    Initialize-Directory -Path $hookConfigDirectory
    Initialize-Directory -Path $instructionDirectory

    $botTokenSecretPath = "$gopassPrefix/bot-token"
    $chatIdSecretPath = "$gopassPrefix/chat-id"
    $existingBotToken = Get-GopassSecret -SecretPath $botTokenSecretPath
    $existingChatId = Get-GopassSecret -SecretPath $chatIdSecretPath

    $providedBotToken = if (-not [string]::IsNullOrWhiteSpace($TelegramBotToken)) {
        $TelegramBotToken
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:TG_BOT_TOKEN)) {
        $env:TG_BOT_TOKEN
    }
    else {
        $null
    }

    $providedChatId = if (-not [string]::IsNullOrWhiteSpace($TelegramChatId)) {
        $TelegramChatId
    }
    elseif (-not [string]::IsNullOrWhiteSpace($env:TG_CHAT_ID)) {
        $env:TG_CHAT_ID
    }
    else {
        $null
    }

    $botTokenResolution = Resolve-SecretValue `
        -Label "Telegram bot token" `
        -ProvidedValue $providedBotToken `
        -ExistingValue $existingBotToken `
        -IsSecure $true `
        -SkipPrompt $SkipSecretPrompt.IsPresent
    $chatIdResolution = Resolve-SecretValue `
        -Label "Telegram chat ID" `
        -ProvidedValue $providedChatId `
        -ExistingValue $existingChatId `
        -IsSecure $false `
        -SkipPrompt $SkipSecretPrompt.IsPresent

    Assert-ValidTelegramConfig -BotToken $botTokenResolution.Value -ChatId $chatIdResolution.Value

    if ($botTokenResolution.ShouldUpdate) {
        Set-GopassSecret -SecretPath $botTokenSecretPath -SecretValue $botTokenResolution.Value
    }

    if ($chatIdResolution.ShouldUpdate) {
        Set-GopassSecret -SecretPath $chatIdSecretPath -SecretValue $chatIdResolution.Value
    }

    $installedFiles = [System.Collections.Generic.List[hashtable]]::new()
    foreach ($relativePath in @(
            "scripts/CopilotHook.Common.ps1",
            "scripts/copilot-summary-state.ps1",
            "scripts/telegram-notify.ps1"
        )) {
        $sourcePath = Join-Path $sourceRoot $relativePath
        $destinationPath = Join-Path $installRoot $relativePath
        $resolvedMode = Install-ManagedFile -SourcePath $sourcePath -DestinationPath $destinationPath -Mode $InstallMode
        $installedFiles.Add([ordered]@{
                source      = $sourcePath
                destination = $destinationPath
                mode        = $resolvedMode
            })
    }

    $instructionSourcePath = Join-Path $sourceInstructionsRoot "copilot-notify-summary.instructions.md"
    $instructionResolvedMode = Install-ManagedFile -SourcePath $instructionSourcePath -DestinationPath $instructionFilePath -Mode $InstallMode
    $installedFiles.Add([ordered]@{
            source      = $instructionSourcePath
            destination = $instructionFilePath
            mode        = $instructionResolvedMode
        })

    $hookConfig = Read-JsonMapFile -Path $hookConfigPath
    if (-not (Test-MapContainsKey -Map $hookConfig -Key "hooks") -or ($null -eq $hookConfig["hooks"])) {
        $hookConfig["hooks"] = [ordered]@{}
    }

    $hooks = $hookConfig["hooks"]
    if (-not (Test-IsDictionaryObject -Value $hooks)) {
        throw "The existing ~/.claude/settings.json file has a non-object 'hooks' field."
    }

    $sessionStartScriptPath = Join-Path $installScriptsRoot "copilot-summary-state.ps1"
    $stopScriptPath = Join-Path $installScriptsRoot "telegram-notify.ps1"
    $sessionStartHookEntry = Get-ManagedHookEntry -ScriptPath $sessionStartScriptPath -TimeoutSeconds 10 -SecretPrefix $gopassPrefix
    $stopHookEntry = Get-ManagedHookEntry -ScriptPath $stopScriptPath -TimeoutSeconds 10 -SecretPrefix $gopassPrefix

    $sessionStartEntries = if (Test-MapContainsKey -Map $hooks -Key "SessionStart") {
        @($hooks["SessionStart"])
    }
    else {
        @()
    }
    $stopEntries = if (Test-MapContainsKey -Map $hooks -Key "Stop") {
        @($hooks["Stop"])
    }
    else {
        @()
    }

    $hooks["SessionStart"] = Get-ManagedHookEventEntryCollection -ExistingEntries $sessionStartEntries -Entry $sessionStartHookEntry -InstallRoot $installRoot
    $hooks["Stop"] = Get-ManagedHookEventEntryCollection -ExistingEntries $stopEntries -Entry $stopHookEntry -InstallRoot $installRoot
    $hookConfig["hooks"] = $hooks

    Write-JsonMapFile -Path $hookConfigPath -Value $hookConfig
    $installedFilesArray = $installedFiles.ToArray()
    Write-InstallManifest -Path $manifestPath -InstallMode $InstallMode -GopassPrefix $gopassPrefix -HookConfigPath $hookConfigPath -InstructionFilePath $instructionFilePath -Files $installedFilesArray

    Write-Output "Installed VS Code Copilot Telegram hooks into '$installRoot'."
    Write-Output "Updated user hook configuration at '$hookConfigPath'."
    Write-Output "Installed VS Code GitHub Copilot user instructions at '$instructionFilePath'."
    Write-Output "Using gopass prefix '$gopassPrefix'."
    exit 0
}
catch {
    $errorMessage = $_.Exception.Message
    if (Test-IsPromptUnavailableError -Exception $_.Exception) {
        $headlessInstallGuidance = Get-HeadlessInstallGuidance
        $errorMessage = "$errorMessage $headlessInstallGuidance"
    }

    Write-Error $errorMessage
    exit 1
}
