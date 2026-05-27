#!/usr/bin/env pwsh
#Requires -Version 7.4

<#
.SYNOPSIS
    Synchronizes Renovate-updated global.json SDK versions back to source files.

.DESCRIPTION
    Renovate's native NuGet manager understands global.json, while global.pkl is
    the repository source file used to generate global.json. This script lets
    Renovate use native global.json extraction instead of a custom regex manager
    for Pkl, then copies changed .NET SDK and msbuild-sdks versions back to
    mise.lock and global.pkl, regenerates global.json from the sources, and
    refreshes .NET lock files.
#>

[CmdletBinding()]
param(
    [string]$GlobalJsonPath = "global.json",

    [string]$GlobalPklPath = "global.pkl",

    [string]$MiseLockPath = "mise.lock",

    [string]$DotNetInstallRoot = (Join-Path $HOME ".dotnet-renovate-sdk"),

    [string]$RestoreProject = "dirs.proj",

    [switch]$SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$dotNetInstallScriptUri =
[uri]"https://raw.githubusercontent.com/dotnet/install-scripts/5147e32300a8e908f5d737c8cff63a76b4b63531/src/dotnet-install.ps1"
$dotNetInstallScriptSha256 = "BB1CE92F4397E24D4736A4658B9728FB8F9DB64A0D3F8E636BA408A866A6661D"

function Test-RequiredCommand {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found on PATH."
    }
}

function Test-SensitiveRestoreEnvironmentName {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $sensitiveNamePatterns = @(
        '^(RENOVATE_TOKEN|GITHUB_TOKEN|GH_TOKEN|GITHUB_PAT|MISE_GITHUB_TOKEN)$',
        '^(RENOVATE|GITHUB|GH|ACTIONS)_.+(TOKEN|SECRET|PASSWORD|CREDENTIAL|PRIVATE_KEY|APP_KEY|PAT)$',
        '^ACTIONS_ID_TOKEN_REQUEST_TOKEN$',
        '^ACTIONS_RUNTIME_TOKEN$'
    )

    foreach ($pattern in $sensitiveNamePatterns) {
        if ($Name -match $pattern) {
            return $true
        }
    }

    return $false
}

function Test-FileSha256 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [ValidatePattern('^[0-9a-fA-F]{64}$')]
        [string]$ExpectedSha256
    )

    $actualSha256 = (Get-FileHash -Path $Path -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualSha256, $ExpectedSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA256 mismatch for '$Path'. Expected '$ExpectedSha256', but found '$actualSha256'."
    }
}

function Save-VerifiedDotNetInstallScript {
    param(
        [Parameter(Mandatory = $true)]
        [uri]$Uri,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedSha256,

        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $Path

    try {
        Test-FileSha256 -Path $Path -ExpectedSha256 $ExpectedSha256
    }
    catch {
        Remove-Item -Path $Path -Force -ErrorAction SilentlyContinue
        throw
    }
}

function Invoke-DotNetRestoreWithoutSensitiveEnvironment {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Project,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedSdkVersion,

        [Parameter(Mandatory = $true)]
        [string]$MiseLockPath,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedMiseLockContent,

        [Parameter(Mandatory = $true)]
        [string]$DotNetInstallRoot
    )

    $sensitiveNames = Get-ChildItem Env: |
        Where-Object { Test-SensitiveRestoreEnvironmentName -Name $_.Name } |
        ForEach-Object { $_.Name }

    foreach ($name in $sensitiveNames) {
        Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue
    }

    $dotNetInstallDir = Join-Path $DotNetInstallRoot $ExpectedSdkVersion
    $dotNetExecutableName = if ($IsWindows) { "dotnet.exe" } else { "dotnet" }
    $dotNetExecutable = Join-Path $dotNetInstallDir $dotNetExecutableName

    if (-not (Test-Path $dotNetExecutable)) {
        New-Item -ItemType Directory -Path $dotNetInstallDir -Force | Out-Null
        $dotNetInstallScript = Join-Path $DotNetInstallRoot "dotnet-install.ps1"
        Save-VerifiedDotNetInstallScript `
            -Uri $dotNetInstallScriptUri `
            -ExpectedSha256 $dotNetInstallScriptSha256 `
            -Path $dotNetInstallScript
        & $dotNetInstallScript -Version $ExpectedSdkVersion -InstallDir $dotNetInstallDir -NoPath
    }

    $miseLockContentAfterInstall = Get-Content -Path $MiseLockPath -Raw
    if ($miseLockContentAfterInstall -ne $ExpectedMiseLockContent) {
        throw "Installing exact .NET SDK '$ExpectedSdkVersion' changed '$MiseLockPath'."
    }

    $actualSdkVersion = (& $dotNetExecutable --version).Trim()
    if ($actualSdkVersion -ne $ExpectedSdkVersion) {
        throw "Expected dotnet SDK version '$ExpectedSdkVersion', but found '$actualSdkVersion'."
    }

    $env:DOTNET_ROOT = $dotNetInstallDir
    $env:DOTNET_MULTILEVEL_LOOKUP = "0"
    & $dotNetExecutable restore $Project --force-evaluate -p:RestoreLockedMode=false
    $miseLockContentAfterRestore = Get-Content -Path $MiseLockPath -Raw
    if ($miseLockContentAfterRestore -ne $ExpectedMiseLockContent) {
        throw "dotnet restore changed '$MiseLockPath' after installing exact .NET SDK '$ExpectedSdkVersion'."
    }
}

function Get-DotNetSdkVersion {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path $Path)) {
        throw "global.json file '$Path' does not exist."
    }

    $globalJson = Get-Content -Path $Path -Raw | ConvertFrom-Json
    $sdkProperty = $globalJson.PSObject.Properties['sdk']
    if (-not $sdkProperty) {
        throw "global.json file '$Path' does not contain an 'sdk' object."
    }

    $versionProperty = $sdkProperty.Value.PSObject.Properties['version']
    if (-not $versionProperty -or $versionProperty.Value -isnot [string]) {
        throw "global.json sdk.version must be a string."
    }

    return $versionProperty.Value
}

function Get-MsBuildSdkVersionMap {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path $Path)) {
        throw "global.json file '$Path' does not exist."
    }

    $globalJson = Get-Content -Path $Path -Raw | ConvertFrom-Json
    $msbuildSdksProperty = $globalJson.PSObject.Properties['msbuild-sdks']
    if (-not $msbuildSdksProperty) {
        throw "global.json file '$Path' does not contain an 'msbuild-sdks' object."
    }

    $versions = [ordered]@{}
    foreach ($property in $msbuildSdksProperty.Value.PSObject.Properties) {
        if ($property.Value -isnot [string]) {
            throw "global.json msbuild-sdks entry '$($property.Name)' must be a string."
        }

        $versions[$property.Name] = $property.Value
    }

    if ($versions.Count -eq 0) {
        throw "global.json file '$Path' does not contain any MSBuild SDK versions."
    }

    return $versions
}

function Update-MiseLockDotNetSdkVersion {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Version
    )

    if (-not (Test-Path $Path)) {
        throw "mise lock file '$Path' does not exist."
    }

    $content = Get-Content -Path $Path -Raw
    $blockRegex = [regex]::new('(?ms)(^\[\[tools\.dotnet\]\]\r?\n.*?)(?=^\[\[|\z)')
    $dotNetToolBlockMatches = $blockRegex.Matches($content)
    if ($dotNetToolBlockMatches.Count -ne 1) {
        throw "Expected exactly one [[tools.dotnet]] block in '$Path', found $($dotNetToolBlockMatches.Count)."
    }

    $block = $dotNetToolBlockMatches[0].Value
    $backendMatches = [regex]::Matches($block, '(?m)^\s*backend\s*=\s*"([^"]+)"\s*$')
    if ($backendMatches.Count -ne 1) {
        throw "mise.lock [[tools.dotnet]] must contain exactly one backend entry; found $($backendMatches.Count)."
    }

    if ($backendMatches[0].Groups[1].Value -ne "core:dotnet") {
        throw "mise.lock [[tools.dotnet]] backend must be `"core:dotnet`"."
    }

    $versionRegex = [regex]::new('(?m)^(\s*version\s*=\s*")([^"]+)("\s*)$')
    $versionMatches = $versionRegex.Matches($block)
    if ($versionMatches.Count -ne 1) {
        throw "mise.lock [[tools.dotnet]] must contain exactly one version entry; found $($versionMatches.Count)."
    }

    $replacementVersion = $Version.Replace('$', '$$')
    $updatedBlock = $versionRegex.Replace($block, "`${1}$replacementVersion`${3}", 1)
    $updatedContent = $content.Substring(0, $dotNetToolBlockMatches[0].Index) + $updatedBlock + $content.Substring($dotNetToolBlockMatches[0].Index + $dotNetToolBlockMatches[0].Length)

    if (-not $updatedContent.EndsWith("`n")) {
        $updatedContent += "`n"
    }

    if ($PSCmdlet.ShouldProcess($Path, "Update .NET SDK version in mise lock file")) {
        Set-Content -Path $Path -Value $updatedContent -NoNewline
    }
}

function Update-GlobalPklMsBuildSdkVersion {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [System.Collections.IDictionary]$Versions
    )

    if (-not (Test-Path $Path)) {
        throw "global.pkl file '$Path' does not exist."
    }

    $content = Get-Content -Path $Path -Raw

    foreach ($name in $Versions.Keys) {
        $pattern = '(?m)(\["' + [regex]::Escape($name) + '"\]\s*=\s*")([^"]+)(")'
        $regex = [regex]::new($pattern)
        $sdkEntryMatches = $regex.Matches($content)

        if ($sdkEntryMatches.Count -ne 1) {
            throw "Expected exactly one global.pkl MSBuild SDK entry for '$name', found $($sdkEntryMatches.Count)."
        }

        $version = $Versions[$name]
        $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
            param($match)
            return $match.Groups[1].Value + $version + $match.Groups[3].Value
        }

        $content = $regex.Replace($content, $evaluator, 1)
    }

    if (-not $content.EndsWith("`n")) {
        $content += "`n"
    }

    if ($PSCmdlet.ShouldProcess($Path, "Update MSBuild SDK versions in global.pkl")) {
        Set-Content -Path $Path -Value $content -NoNewline
    }
}

$dotNetSdkVersion = Get-DotNetSdkVersion -Path $GlobalJsonPath
$msBuildSdkVersions = Get-MsBuildSdkVersionMap -Path $GlobalJsonPath
Update-MiseLockDotNetSdkVersion -Path $MiseLockPath -Version $dotNetSdkVersion
Update-GlobalPklMsBuildSdkVersion -Path $GlobalPklPath -Versions $msBuildSdkVersions

Test-RequiredCommand -Name "pkl"
pkl eval -f json $GlobalPklPath -o $GlobalJsonPath

if (-not $SkipRestore) {
    $expectedMiseLockContent = Get-Content -Path $MiseLockPath -Raw
    Invoke-DotNetRestoreWithoutSensitiveEnvironment -Project $RestoreProject -ExpectedSdkVersion $dotNetSdkVersion -MiseLockPath $MiseLockPath -ExpectedMiseLockContent $expectedMiseLockContent -DotNetInstallRoot $DotNetInstallRoot
}
