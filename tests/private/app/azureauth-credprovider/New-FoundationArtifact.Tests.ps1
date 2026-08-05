#Requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$scriptPath = Join-Path $repoRoot 'eng/scripts/azureauth-credprovider/New-FoundationArtifact.ps1'
$testBase = Join-Path $repoRoot 'artifacts/azureauth-credprovider/foundation-script-tests'
$testRoot = Join-Path $testBase ([System.Guid]::NewGuid().ToString('N'))
$buildOs = 'Linux'
$targetRid = 'linux-x64'
$packageName = "azureauth-credprovider-foundation-internal-$buildOs-$targetRid.zip"

function Assert-True {
    param(
        [Parameter(Mandatory = $true)]
        [bool]$Condition,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-BytesEqual {
    param(
        [Parameter(Mandatory = $true)]
        [byte[]]$Expected,

        [Parameter(Mandatory = $true)]
        [byte[]]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if (-not [System.Linq.Enumerable]::SequenceEqual($Expected, $Actual)) {
        throw $Message
    }
}

function Read-ZipEntryBytes {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchiveEntry]$Entry
    )

    $source = $Entry.Open()
    try {
        $destination = [System.IO.MemoryStream]::new()
        try {
            $source.CopyTo($destination)
            return $destination.ToArray()
        }
        finally {
            $destination.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}

function New-CanonicalStaging {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot
    )

    $stagingRoot = Join-Path $OutputRoot "staging/$buildOs/$targetRid"
    $contractsRoot = Join-Path $stagingRoot 'Contracts'
    $platformRoot = Join-Path $stagingRoot 'Platform'
    New-Item -ItemType Directory -Path $contractsRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $platformRoot -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $contractsRoot 'contracts.dll') -Value 'contracts' -NoNewline
    Set-Content -LiteralPath (Join-Path $platformRoot 'platform.dll') -Value 'platform' -NoNewline
    return $stagingRoot
}

function Invoke-FoundationArtifact {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot,

        [scriptblock]$AfterArtifactCapture,

        [scriptblock]$BeforePackageReplace
    )

    $parameters = @{
        BuildOs        = $buildOs
        TargetRid      = $targetRid
        OutputRoot     = $OutputRoot
        ProductVersion = '1.2.3-test'
        SourceRevision = 'test-revision'
        NoBuild        = $true
    }
    if ($null -ne $AfterArtifactCapture) {
        $parameters.AfterArtifactCapture = $AfterArtifactCapture
    }
    if ($null -ne $BeforePackageReplace) {
        $parameters.BeforePackageReplace = $BeforePackageReplace
    }

    & $scriptPath @parameters | Out-Null
}

function Assert-InvocationFails {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    try {
        & $Action
    }
    catch {
        return
    }

    throw $Message
}

function Assert-NoTemporaryPackagingArtifacts {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $temporaryArtifacts = @(
        Get-ChildItem -LiteralPath $OutputRoot -Force |
            Where-Object {
                $_.Name -like "$packageName.*.tmp" -or
                $_.Name -like "$packageName.*.snapshot"
            }
    )
    Assert-True (
        $temporaryArtifacts.Count -eq 0
    ) "Temporary snapshots or package partials remain after $Context."
}

function Test-ManifestMetadataMatchesCapturedZipEntryAfterStagedSourceReplacement {
    $snapshotOutput = Join-Path $testRoot 'captured-source-replacement'
    $snapshotStagingRoot = New-CanonicalStaging -OutputRoot $snapshotOutput
    $capturedSourcePath = Join-Path $snapshotStagingRoot 'Contracts/contracts.dll'
    $capturedBytes = [System.IO.File]::ReadAllBytes($capturedSourcePath)
    $replacementBytes = [System.Text.Encoding]::UTF8.GetBytes('contracts replaced after capture')

    Invoke-FoundationArtifact -OutputRoot $snapshotOutput -AfterArtifactCapture {
        param([string]$capturedStagingRoot)

        $sourcePath = Join-Path $capturedStagingRoot 'Contracts/contracts.dll'
        $replacementPath = Join-Path $capturedStagingRoot 'Contracts/contracts.replacement'
        [System.IO.File]::WriteAllBytes(
            $replacementPath,
            [System.Text.Encoding]::UTF8.GetBytes('contracts replaced after capture')
        )
        [System.IO.File]::Move($replacementPath, $sourcePath, $true)
    }

    Assert-BytesEqual $replacementBytes (
        [System.IO.File]::ReadAllBytes($capturedSourcePath)
    ) 'The staged source was not replaced after artifact capture.'

    $snapshotPackagePath = Join-Path $snapshotOutput $packageName
    $archive = [System.IO.Compression.ZipFile]::OpenRead($snapshotPackagePath)
    try {
        $manifestEntry = $archive.GetEntry('manifest.json')
        Assert-True ($null -ne $manifestEntry) 'The snapshot package has no manifest.'
        [byte[]]$manifestBytes = Read-ZipEntryBytes -Entry $manifestEntry
        $manifest = [System.Text.Encoding]::UTF8.GetString($manifestBytes) | ConvertFrom-Json
        $manifestFiles = @($manifest.files | Where-Object path -CEQ 'Contracts/contracts.dll')
        Assert-True ($manifestFiles.Count -eq 1) 'The manifest does not contain exactly one Contracts/contracts.dll entry.'

        $contractsEntry = $archive.GetEntry('Contracts/contracts.dll')
        Assert-True ($null -ne $contractsEntry) 'The snapshot package has no Contracts/contracts.dll entry.'
        [byte[]]$contractsBytes = Read-ZipEntryBytes -Entry $contractsEntry
        $contractsSha256 = [System.Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData($contractsBytes)
        ).ToLowerInvariant()

        Assert-True (
            $manifestFiles[0].length -eq $contractsBytes.LongLength
        ) 'Manifest length does not match the ZIP entry bytes after staged source replacement.'
        Assert-True (
            $manifestFiles[0].sha256 -ceq $contractsSha256
        ) 'Manifest SHA-256 does not match the ZIP entry bytes after staged source replacement.'
        Assert-BytesEqual $capturedBytes $contractsBytes 'The ZIP entry does not contain the captured source bytes.'
    }
    finally {
        $archive.Dispose()
    }
    Assert-NoTemporaryPackagingArtifacts -OutputRoot $snapshotOutput -Context 'successful source replacement packaging'
}

try {
    $reuseOutput = Join-Path $testRoot 'reuse'
    $stagingRoot = New-CanonicalStaging -OutputRoot $reuseOutput
    $markerPath = Join-Path $stagingRoot 'reuse-marker.txt'
    Set-Content -LiteralPath $markerPath -Value 'preserve staging' -NoNewline
    $reusePackagePath = Join-Path $reuseOutput $packageName
    $supersededArchive = [System.Text.Encoding]::UTF8.GetBytes('superseded archive')
    [System.IO.File]::WriteAllBytes($reusePackagePath, $supersededArchive)

    Invoke-FoundationArtifact -OutputRoot $reuseOutput

    Assert-True (Test-Path -LiteralPath $markerPath -PathType Leaf) '-NoBuild recreated canonical staging.'
    Assert-True (Test-Path -LiteralPath $reusePackagePath -PathType Leaf) '-NoBuild did not create the package.'
    Assert-True (
        -not [System.Linq.Enumerable]::SequenceEqual(
            $supersededArchive,
            [System.IO.File]::ReadAllBytes($reusePackagePath)
        )
    ) '-NoBuild did not replace the existing package after successful validation.'
    $archive = [System.IO.Compression.ZipFile]::OpenRead($reusePackagePath)
    try {
        Assert-True ($null -ne $archive.GetEntry('manifest.json')) 'The replacement package is invalid.'
    }
    finally {
        $archive.Dispose()
    }
    Assert-NoTemporaryPackagingArtifacts -OutputRoot $reuseOutput -Context 'successful package replacement'

    foreach ($case in @('absent', 'empty', 'partial')) {
        $invalidOutput = Join-Path $testRoot $case
        New-Item -ItemType Directory -Path $invalidOutput -Force | Out-Null
        $packagePath = Join-Path $invalidOutput $packageName
        $existingArchive = [System.Text.Encoding]::UTF8.GetBytes("existing archive: $case")
        [System.IO.File]::WriteAllBytes($packagePath, $existingArchive)

        $invalidStagingRoot = Join-Path $invalidOutput "staging/$buildOs/$targetRid"
        if ($case -eq 'empty') {
            New-Item -ItemType Directory -Path (Join-Path $invalidStagingRoot 'Contracts') -Force | Out-Null
            New-Item -ItemType Directory -Path (Join-Path $invalidStagingRoot 'Platform') -Force | Out-Null
        }
        elseif ($case -eq 'partial') {
            $contractsRoot = Join-Path $invalidStagingRoot 'Contracts'
            New-Item -ItemType Directory -Path $contractsRoot -Force | Out-Null
            Set-Content -LiteralPath (Join-Path $contractsRoot 'contracts.dll') -Value 'contracts' -NoNewline
        }

        Assert-InvocationFails {
            Invoke-FoundationArtifact -OutputRoot $invalidOutput
        } "Expected $case canonical staging to be rejected."
        Assert-BytesEqual $existingArchive ([System.IO.File]::ReadAllBytes($packagePath)) "Existing archive changed after $case staging failure."
        Assert-NoTemporaryPackagingArtifacts -OutputRoot $invalidOutput -Context "$case staging failure"
    }

    $failureOutput = Join-Path $testRoot 'replacement-failure'
    $null = New-CanonicalStaging -OutputRoot $failureOutput
    $packagePath = Join-Path $failureOutput $packageName
    $existingArchive = [System.Text.Encoding]::UTF8.GetBytes('existing archive before replacement')
    [System.IO.File]::WriteAllBytes($packagePath, $existingArchive)

    Assert-InvocationFails {
        Invoke-FoundationArtifact -OutputRoot $failureOutput -BeforePackageReplace {
            throw 'Injected deterministic replacement failure.'
        }
    } 'Expected the injected replacement failure.'
    Assert-BytesEqual $existingArchive ([System.IO.File]::ReadAllBytes($packagePath)) 'Existing archive changed after replacement failure.'
    Assert-NoTemporaryPackagingArtifacts -OutputRoot $failureOutput -Context 'replacement failure'

    Test-ManifestMetadataMatchesCapturedZipEntryAfterStagedSourceReplacement

    Write-Output 'All New-FoundationArtifact regression tests passed.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
    if ((Test-Path -LiteralPath $testBase) -and
        @(Get-ChildItem -LiteralPath $testBase -Force).Count -eq 0) {
        Remove-Item -LiteralPath $testBase -Force
    }
}
