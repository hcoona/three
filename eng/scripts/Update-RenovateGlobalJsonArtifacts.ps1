#!/usr/bin/env pwsh
#Requires -Version 7.4

<#
.SYNOPSIS
    Refreshes artifacts derived from a Renovate-updated global.json.

.DESCRIPTION
    global.json is the .NET SDK and MSBuild SDK authority, and mise reads its
    SDK selection as an idiomatic version file. After Renovate's native NuGet
    manager updates global.json, this script refreshes the mise.lock .NET SDK
    entry through 'mise lock dotnet' and refreshes the .NET package lock files.
#>

[CmdletBinding()]
param(
    [string]$GlobalJsonPath = "global.json",

    [string]$MiseLockPath = "mise.lock",

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
        [string]$ExpectedMiseLockContent
    )

    $sensitiveNames = Get-ChildItem Env: |
        Where-Object { Test-SensitiveRestoreEnvironmentName -Name $_.Name } |
        ForEach-Object { $_.Name }

    foreach ($name in $sensitiveNames) {
        Remove-Item -Path "Env:$name" -ErrorAction SilentlyContinue
    }

    Test-RequiredCommand -Name "mise"
    mise install --locked dotnet
    $dotNetInstallDir = (& mise where dotnet).Trim()
    $dotNetExecutableName = if ($IsWindows) { "dotnet.exe" } else { "dotnet" }
    $dotNetExecutable = Join-Path $dotNetInstallDir $dotNetExecutableName

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

$dotNetSdkVersion = Get-DotNetSdkVersion -Path $GlobalJsonPath

Test-RequiredCommand -Name "mise"
mise lock dotnet

if (-not $SkipRestore) {
    $expectedMiseLockContent = Get-Content -Path $MiseLockPath -Raw
    Invoke-DotNetRestoreWithoutSensitiveEnvironment -Project $RestoreProject -ExpectedSdkVersion $dotNetSdkVersion -MiseLockPath $MiseLockPath -ExpectedMiseLockContent $expectedMiseLockContent
}
