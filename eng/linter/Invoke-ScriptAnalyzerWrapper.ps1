#!/usr/bin/env pwsh
#requires -Version 7.4
#requires -Modules PSScriptAnalyzer

<#
.SYNOPSIS
Provides a consistent check/fix surface for Invoke-ScriptAnalyzer so HK tooling
can batch PowerShell diagnostics.

.DESCRIPTION
Invoke-ScriptAnalyzer already understands IncludeDefaultRules, Fix, and EnableExit semantics,
but hk callers expect a single entry point that mirrors the formatter wrapper. This script
filters the incoming file list, runs Invoke-ScriptAnalyzer per file with IncludeDefaultRules,
and either reports violations (check mode) or applies automatic fixes (fix mode). Check mode
prints the diagnostics and exits with the number of violations (capped at 255). Fix mode applies
available fixes and exits successfully.
#>
[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet('Check', 'Fix')]
    [string]$Mode = 'Check',

    [Parameter(Position = 0)]
    [string[]]$Path,

    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$AdditionalPath,

    [Parameter()]
    [string[]]$IncludeExtension = @('.ps1', '.psm1', '.psd1'),

    [Parameter()]
    [string[]]$CustomRulePath,

    [Parameter()]
    [switch]$RecurseCustomRulePath,

    [Parameter()]
    [string[]]$IncludeRule,

    [Parameter()]
    [string[]]$ExcludeRule,

    [Parameter()]
    [string[]]$Severity,

    [Parameter()]
    [object]$Settings,

    [Parameter()]
    [switch]$ReportSummary
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

if ($AdditionalPath) {
    if ($Path) {
        $Path = @($Path + $AdditionalPath)
    }
    else {
        $Path = $AdditionalPath
    }
}

function Get-ExtensionFilter {
    [OutputType([System.Collections.Generic.HashSet[string]])]
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Extensions
    )

    $filter = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($extension in $Extensions) {
        if ([string]::IsNullOrWhiteSpace($extension)) {
            continue
        }

        $normalized = if ($extension.StartsWith('.')) { $extension } else { ".${extension}" }
        [void]$filter.Add($normalized)
    }

    return $filter
}

function Add-TargetFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Candidate,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$Registry,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.List[string]]$Collector,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [System.Collections.Generic.HashSet[string]]$ExtensionFilter
    )

    try {
        $resolved = Resolve-Path -LiteralPath $Candidate -ErrorAction Stop
    }
    catch {
        Write-Warning ("Skip '{0}': {1}" -f $Candidate, $_.Exception.Message)
        return
    }

    foreach ($pathInfo in $resolved) {
        $providerPath = $pathInfo.ProviderPath
        if (-not (Test-Path -LiteralPath $providerPath -PathType Leaf)) {
            continue
        }

        $extension = [System.IO.Path]::GetExtension($providerPath)
        if (($ExtensionFilter.Count -gt 0) -and -not $ExtensionFilter.Contains($extension)) {
            continue
        }

        if ($Registry.Add($providerPath)) {
            [void]$Collector.Add($providerPath)
        }
    }
}

function ConvertTo-RelativePath {
    [OutputType([string])]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FullPath
    )

    try {
        $current = (Get-Location).ProviderPath
        $relative = [System.IO.Path]::GetRelativePath($current, $FullPath)
        if (-not [string]::IsNullOrWhiteSpace($relative)) {
            return $relative
        }
    }
    catch {
        Write-Verbose ("Unable to compute relative path for {0}: {1}" -f $FullPath, $_.Exception.Message)
    }

    return $FullPath
}

function Invoke-Analyzer {
    [OutputType([Microsoft.Windows.PowerShell.ScriptAnalyzer.Generic.DiagnosticRecord[]])]
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetPath,

        [Parameter(Mandatory = $true)]
        [hashtable]$CommonParameters,

        [Parameter(Mandatory = $true)]
        [string]$Mode
    )

    $invokeParams = @{
        Path                = $TargetPath
        IncludeDefaultRules = $true
    }

    foreach ($key in $CommonParameters.Keys) {
        $invokeParams[$key] = $CommonParameters[$key]
    }

    if ($Mode -eq 'Fix') {
        $invokeParams['Fix'] = $true
    }

    return Invoke-ScriptAnalyzer @invokeParams
}

$extensionFilter = Get-ExtensionFilter -Extensions $IncludeExtension
$trackedPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$targetFiles = [System.Collections.Generic.List[string]]::new()

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
    Write-Verbose 'No PowerShell files were provided; exiting.'
    exit 0
}

foreach ($candidate in $allCandidates) {
    Add-TargetFile -Candidate $candidate -Registry $trackedPaths -Collector $targetFiles -ExtensionFilter $extensionFilter
}

if ($targetFiles.Count -eq 0) {
    Write-Verbose 'No files matched the PowerShell extension filter.'
    exit 0
}

$commonAnalyzerParams = @{}
if ($CustomRulePath) {
    $commonAnalyzerParams['CustomRulePath'] = $CustomRulePath
}
if ($RecurseCustomRulePath.IsPresent) {
    $commonAnalyzerParams['RecurseCustomRulePath'] = $true
}
if ($IncludeRule) {
    $commonAnalyzerParams['IncludeRule'] = $IncludeRule
}
if ($ExcludeRule) {
    $commonAnalyzerParams['ExcludeRule'] = $ExcludeRule
}
if ($Severity) {
    $commonAnalyzerParams['Severity'] = $Severity
}
if ($PSBoundParameters.ContainsKey('Settings')) {
    $commonAnalyzerParams['Settings'] = $Settings
}
if ($ReportSummary.IsPresent) {
    $commonAnalyzerParams['ReportSummary'] = $true
}

$diagnostics = [System.Collections.Generic.List[psobject]]::new()
foreach ($file in $targetFiles) {
    $relativePath = ConvertTo-RelativePath -FullPath $file

    try {
        $result = Invoke-Analyzer -TargetPath $file -CommonParameters $commonAnalyzerParams -Mode $Mode
    }
    catch {
        Write-Error ("Invoke-ScriptAnalyzer failed for '{0}': {1}" -f $relativePath, $_.Exception.Message)
        continue
    }

    if ($Mode -eq 'Check' -and $null -ne $result) {
        foreach ($record in $result) {
            $diagnostics.Add([pscustomobject]@{
                    Path     = $relativePath
                    Rule     = $record.RuleName
                    Severity = $record.Severity
                    Line     = $record.Line
                    Message  = $record.Message
                }) | Out-Null
        }
    }
}

if ($Mode -eq 'Check') {
    if ($diagnostics.Count -gt 0) {
        $report = $diagnostics | Sort-Object Severity, Path, Line | Format-Table -AutoSize | Out-String
        Write-Error -Message ("Invoke-ScriptAnalyzer detected issues:{0}{1}" -f [Environment]::NewLine, $report) -Category InvalidData
        $exitCode = [Math]::Min(255, $diagnostics.Count)
        exit $exitCode
    }

    Write-Verbose 'All files passed Invoke-ScriptAnalyzer.'
}
else {
    Write-Verbose 'Fix mode completed; re-run in check mode to confirm zero findings.'
}
