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

    $savedEnvironment = @{}
    $sensitiveNames = Get-ChildItem Env: |
        Where-Object { Test-SensitiveRestoreEnvironmentName -Name $_.Name } |
        ForEach-Object { $_.Name }

    foreach ($name in $sensitiveNames) {
        $savedEnvironment[$name] = (Get-Item -Path "Env:$name").Value
        Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue
    }

    try {
        $dotNetInstallDir = Join-Path $DotNetInstallRoot $ExpectedSdkVersion
        $dotNetExecutableName = if ($IsWindows) { "dotnet.exe" } else { "dotnet" }
        $dotNetExecutable = Join-Path $dotNetInstallDir $dotNetExecutableName

        if (-not (Test-Path $dotNetExecutable)) {
            New-Item -ItemType Directory -Path $dotNetInstallDir -Force | Out-Null
            $dotNetInstallScript = Join-Path $DotNetInstallRoot "dotnet-install.ps1"
            Invoke-WebRequest -UseBasicParsing -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $dotNetInstallScript
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
        & $dotNetExecutable restore $Project --force-evaluate
        $miseLockContentAfterRestore = Get-Content -Path $MiseLockPath -Raw
        if ($miseLockContentAfterRestore -ne $ExpectedMiseLockContent) {
            throw "dotnet restore changed '$MiseLockPath' after installing exact .NET SDK '$ExpectedSdkVersion'."
        }
    }
    finally {
        foreach ($name in $savedEnvironment.Keys) {
            Set-Item -Path "Env:$name" -Value $savedEnvironment[$name]
        }
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
    $matches = $blockRegex.Matches($content)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one [[tools.dotnet]] block in '$Path', found $($matches.Count)."
    }

    $block = $matches[0].Value
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

    $updatedBlock = $versionRegex.Replace(
        $block,
        {
            param($match)
            return $match.Groups[1].Value + $Version + $match.Groups[3].Value
        },
        1
    )
    $updatedContent = $content.Substring(0, $matches[0].Index) + $updatedBlock + $content.Substring($matches[0].Index + $matches[0].Length)

    if (-not $updatedContent.EndsWith("`n")) {
        $updatedContent += "`n"
    }

    Set-Content -Path $Path -Value $updatedContent -NoNewline
}

function Update-GlobalPklMsBuildSdkVersions {
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
        $matches = $regex.Matches($content)

        if ($matches.Count -ne 1) {
            throw "Expected exactly one global.pkl MSBuild SDK entry for '$name', found $($matches.Count)."
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

    Set-Content -Path $Path -Value $content -NoNewline
}

$dotNetSdkVersion = Get-DotNetSdkVersion -Path $GlobalJsonPath
$msBuildSdkVersions = Get-MsBuildSdkVersionMap -Path $GlobalJsonPath
Update-MiseLockDotNetSdkVersion -Path $MiseLockPath -Version $dotNetSdkVersion
Update-GlobalPklMsBuildSdkVersions -Path $GlobalPklPath -Versions $msBuildSdkVersions

Test-RequiredCommand -Name "mise"
mise run update-global-json

if (-not $SkipRestore) {
    $expectedMiseLockContent = Get-Content -Path $MiseLockPath -Raw
    Invoke-DotNetRestoreWithoutSensitiveEnvironment -Project $RestoreProject -ExpectedSdkVersion $dotNetSdkVersion -MiseLockPath $MiseLockPath -ExpectedMiseLockContent $expectedMiseLockContent -DotNetInstallRoot $DotNetInstallRoot
}
