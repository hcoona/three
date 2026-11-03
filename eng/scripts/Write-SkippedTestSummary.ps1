#!/usr/bin/env pwsh
#Requires -Version 7.4

<#
.SYNOPSIS
    Names skipped .NET tests and their reasons from TRX results.

.DESCRIPTION
    Test runners report only a skipped count on the console. CI calls this
    script after its .NET test steps so the job log and the step summary name
    each skipped test with the reason its framework recorded. The script only
    reports; the test step itself owns the pass or fail result.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]]$Path,

    [string]$Title = 'Skipped .NET tests'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$skippedOutcomes = @('NotExecuted', 'Inconclusive')
$trxFiles = @(
    foreach ($root in $Path) {
        if (Test-Path -LiteralPath $root -PathType Container) {
            Get-ChildItem -LiteralPath $root -Filter '*.trx' -File -Recurse
        }
    }
)

# A test host killed mid-run can leave a truncated TRX; name it and keep reporting the rest.
$unreadable = [System.Collections.Generic.List[string]]::new()
$skipped = @(
    foreach ($file in $trxFiles) {
        try {
            [xml]$trx = Get-Content -LiteralPath $file.FullName -Raw
            if ($null -eq $trx) {
                throw 'The file is empty.'
            }
        }
        catch {
            Write-Warning "Unreadable TRX $($file.FullName): $($_.Exception.Message)"
            $unreadable.Add($file.FullName)
            continue
        }
        foreach ($result in $trx.SelectNodes("//*[local-name()='UnitTestResult']")) {
            $outcome = $result.GetAttribute('outcome')
            if ($outcome -notin $skippedOutcomes) {
                continue
            }
            $reason = $result.SelectSingleNode(
                "*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']"
            )
            if ($null -eq $reason -or [string]::IsNullOrWhiteSpace($reason.InnerText)) {
                $reason = $result.SelectSingleNode(
                    "*[local-name()='Output']/*[local-name()='StdOut']"
                )
            }
            $reasonText = if ($null -eq $reason -or [string]::IsNullOrWhiteSpace($reason.InnerText)) {
                '(no reason recorded)'
            }
            else {
                ($reason.InnerText.Trim() -split '\r?\n')[0]
            }
            [pscustomobject]@{
                Test    = $result.GetAttribute('testName')
                Outcome = $outcome
                Reason  = $reasonText
            }
        }
    }
)

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("### $Title")
$lines.Add('')
if ($trxFiles.Count -eq 0) {
    $lines.Add('No TRX results were produced.')
}
elseif ($skipped.Count -eq 0) {
    $lines.Add("No skipped tests in $($trxFiles.Count) TRX file(s).")
}
else {
    $lines.Add("$($skipped.Count) skipped test(s) in $($trxFiles.Count) TRX file(s).")
    $lines.Add('')
    $lines.Add('| Test | Outcome | Reason |')
    $lines.Add('| --- | --- | --- |')
    foreach ($item in $skipped | Sort-Object Test) {
        $cells = @($item.Test, $item.Outcome, $item.Reason) | ForEach-Object {
            $_.Replace('|', '\|')
        }
        $lines.Add("| $($cells -join ' | ') |")
    }
}

if ($unreadable.Count -gt 0) {
    $lines.Add('')
    $lines.Add("$($unreadable.Count) TRX file(s) could not be read:")
    foreach ($file in $unreadable) {
        $lines.Add("- ``$file``")
    }
}

foreach ($line in $lines) {
    Write-Output $line
}
if (-not [string]::IsNullOrEmpty($env:GITHUB_STEP_SUMMARY)) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ($lines + '') -Encoding utf8
}
