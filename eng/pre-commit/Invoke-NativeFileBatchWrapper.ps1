#!/usr/bin/env pwsh
#requires -Version 7.4

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Tool,

    [Parameter()]
    [string[]]$ToolArgument,

    [Parameter(Position = 0)]
    [string[]]$Path,

    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

if ($AdditionalPath) {
    if ($Path) {
        $Path = @($Path + $AdditionalPath)
    }
    else {
        $Path = $AdditionalPath
    }
}

$allCandidates = [System.Collections.Generic.List[string]]::new()

if ($Path) {
    foreach ($candidate in $Path) {
        if (-not [string]::IsNullOrWhiteSpace($candidate)) {
            [void]$allCandidates.Add($candidate)
        }
    }
}

foreach ($line in $input) {
    if (-not [string]::IsNullOrWhiteSpace($line)) {
        [void]$allCandidates.Add($line.Trim())
    }
}

if ($allCandidates.Count -eq 0) {
    exit 0
}

$resolvedPaths = [System.Collections.Generic.List[string]]::new()
$dedup = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($candidate in $allCandidates) {
    try {
        $pathInfos = Resolve-Path -LiteralPath $candidate -ErrorAction Stop
    }
    catch {
        Write-Verbose ("Skip unresolved path '{0}': {1}" -f $candidate, $_.Exception.Message)
        continue
    }

    foreach ($pathInfo in $pathInfos) {
        $providerPath = $pathInfo.ProviderPath
        if ((Test-Path -LiteralPath $providerPath) -and $dedup.Add($providerPath)) {
            [void]$resolvedPaths.Add($providerPath)
        }
    }
}

if ($resolvedPaths.Count -eq 0) {
    exit 0
}

try {
    if ($ToolArgument) {
        & $Tool @ToolArgument @resolvedPaths
    }
    else {
        & $Tool @resolvedPaths
    }
}
catch {
    Write-Error ("Failed to run '{0}': {1}" -f $Tool, $_.Exception.Message)
    exit 1
}

exit $LASTEXITCODE
