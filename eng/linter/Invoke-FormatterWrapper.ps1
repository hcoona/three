#!/usr/bin/env pwsh
#requires -Version 7.4
#requires -Modules PSScriptAnalyzer

<#
.SYNOPSIS
Routes batched formatting requests to Invoke-Formatter with uniform check/fix semantics.

.DESCRIPTION
Invoke-Formatter emits formatted text without touching source files. This wrapper accepts the
same file list that HK tooling would pass (for example via xargs) and either checks
for formatting drift or rewrites files in-place. Check mode reports every file that would be
changed and returns a non-zero exit code. Fix mode updates the files and exits successfully.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
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
    [object]$FormatterSettings
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

function Write-Utf8FileContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetPath,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Content
    )

    $encoding = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($TargetPath, $Content, $encoding)
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

$pendingViolations = [System.Collections.Generic.List[psobject]]::new()
foreach ($file in $targetFiles) {
    $relativePath = ConvertTo-RelativePath -FullPath $file

    try {
        $originalContent = Get-Content -LiteralPath $file -Raw
    }
    catch {
        Write-Error ("Failed to read '{0}': {1}" -f $relativePath, $_.Exception.Message)
        continue
    }

    try {
        if ($PSBoundParameters.ContainsKey('FormatterSettings')) {
            $formattedContent = Invoke-Formatter -ScriptDefinition $originalContent -Settings $FormatterSettings
        }
        else {
            $formattedContent = Invoke-Formatter -ScriptDefinition $originalContent
        }
    }
    catch {
        Write-Error ("Invoke-Formatter failed for '{0}': {1}" -f $relativePath, $_.Exception.Message)
        continue
    }

    if ($null -eq $formattedContent) {
        $formattedContent = ''
    }

    if ($formattedContent -ne $originalContent) {
        if ($Mode -eq 'Fix') {
            if ($PSCmdlet.ShouldProcess($relativePath, 'Apply formatted content')) {
                Write-Utf8FileContent -TargetPath $file -Content $formattedContent
                Write-Verbose ("Formatted {0}" -f $relativePath)
            }
        }
        else {
            $pendingViolations.Add([pscustomobject]@{
                    Path    = $relativePath
                    Message = 'Formatting required'
                }) | Out-Null
        }
    }
}

if ($Mode -eq 'Check') {
    if ($pendingViolations.Count -gt 0) {
        $report = $pendingViolations | Format-Table -AutoSize | Out-String
        Write-Error -Message ("Invoke-Formatter detected unformatted files:{0}{1}" -f [Environment]::NewLine, $report) -Category InvalidData
        $exitCode = [Math]::Min(255, $pendingViolations.Count)
        exit $exitCode
    }

    Write-Verbose 'All files follow the current formatting profile.'
}
else {
    Write-Verbose 'Fix mode completed without blocking errors.'
}
