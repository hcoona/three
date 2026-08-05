#!/usr/bin/env pwsh
#Requires -Version 7.4

<#
.SYNOPSIS
    Validates that a generated file is in sync with its Pkl source.

.DESCRIPTION
    Compares the generated output from a Pkl source file with an existing
    target file to ensure they are synchronized.

    Can also regenerate the target file from the source using the -Fix switch.

    Uses git diff --no-index for cross-platform comparison.

.EXAMPLE
    Test-PklEvalSync.ps1 -Source global.pkl -Target global.json -Format json
    Test-PklEvalSync.ps1 -Source global.pkl -Target global.json -Format json -Fix

.NOTES
    This script requires pkl and git to be installed.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({
            if ($_ -match '\.pkl$') {
                return $true
            }
            throw "Source file must have a .pkl extension."
        })]
    [string]$Source,

    [Parameter(Mandatory = $true)]
    [string]$Target,

    [Parameter(Mandatory = $true)]
    [ValidateSet('json', 'jsonnet', 'pcf', 'properties', 'plist', 'textproto', 'xml', 'yaml')]
    [string]$Format,

    [string[]]$Properties,

    [switch]$Fix
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

function Get-PklEvalArgument {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Format,

        [Parameter(Mandatory = $true)]
        [string]$Source,

        [string[]]$Properties,

        [string]$Output
    )

    $arguments = @('eval', '-f', $Format)

    if ($Properties) {
        foreach ($property in $Properties) {
            $arguments += @('-p', $property)
        }
    }

    $arguments += $Source

    if ($Output) {
        $arguments += @('-o', $Output)
    }

    return $arguments
}

# Verify required tools
if (-not (Get-Command 'pkl' -ErrorAction SilentlyContinue)) {
    Write-Error "pkl is not installed. Install via: mise install pkl"
}

# Verify source file exists
if (-not (Test-Path $Source)) {
    Write-Error "Source file '$Source' not found"
}

if ($Fix) {
    $pklEvalArgs = Get-PklEvalArgument -Format $Format -Source $Source -Properties $Properties -Output $Target

    # Regenerate the target file
    Write-Verbose "Regenerating '$Target' from '$Source'..."
    pkl @pklEvalArgs
    Write-Information "Successfully updated '$Target'." -InformationAction Continue
}
else {
    if (-not (Get-Command 'git' -ErrorAction SilentlyContinue)) {
        Write-Error "git is not installed"
    }

    # Verify target file exists
    if (-not (Test-Path $Target)) {
        Write-Error "Target file '$Target' not found. Run with -Fix to generate it."
    }

    # Generate to temp file and compare
    $tempFile = [System.IO.Path]::GetTempFileName()
    try {
        $pklEvalArgs = Get-PklEvalArgument -Format $Format -Source $Source -Properties $Properties -Output $tempFile

        # Generate output from Pkl to temp file
        Write-Verbose "pkl $($pklEvalArgs -join ' ')"
        pkl @pklEvalArgs

        # Use git diff --no-index for comparison
        # Temporarily disable error action preference for git diff since exit code 1 is expected when files differ
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'

        $diffOutput = git --no-pager diff --no-index --color=always $Target $tempFile 2>&1
        $diffExitCode = $LASTEXITCODE

        $ErrorActionPreference = $previousErrorActionPreference

        if ($diffExitCode -ne 0) {
            Write-Error "Error: '$Target' is not in sync with '$Source'.`nRun with -Fix switch to update." -ErrorAction Continue
            if ($diffOutput) {
                $diffOutput | Write-Information -InformationAction Continue
            }
            exit 1
        }

        Write-Verbose "'$Target' is in sync with '$Source'"
        exit 0
    }
    finally {
        if (Test-Path $tempFile) {
            Remove-Item $tempFile -Force
        }
    }
}
