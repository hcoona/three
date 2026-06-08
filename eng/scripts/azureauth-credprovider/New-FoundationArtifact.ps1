#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Windows', 'Linux', 'macOS')]
    [string]$BuildOs,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$TargetRid,

    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [ValidateNotNullOrEmpty()]
    [string]$ProductVersion = '0.0.0-internal',

    [ValidateNotNullOrEmpty()]
    [string]$SourceRevision = 'unknown',

    [ValidateNotNullOrEmpty()]
    [string]$OutputRoot = 'artifacts/azureauth-credprovider/foundation',

    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '../../..')).Path

function Test-WindowsReservedDeviceName {
    param([Parameter(Mandatory = $true)][string]$Segment)

    $nameWithoutExtension = $Segment.Split('.')[0]
    return $nameWithoutExtension -in @(
        'CON',
        'PRN',
        'AUX',
        'NUL',
        'CONIN$',
        'CONOUT$',
        'COM1',
        'COM2',
        'COM3',
        'COM4',
        'COM5',
        'COM6',
        'COM7',
        'COM8',
        'COM9',
        ('COM' + [char]0x00B9),
        ('COM' + [char]0x00B2),
        ('COM' + [char]0x00B3),
        'LPT1',
        'LPT2',
        'LPT3',
        'LPT4',
        'LPT5',
        'LPT6',
        'LPT7',
        'LPT8',
        'LPT9',
        ('LPT' + [char]0x00B9),
        ('LPT' + [char]0x00B2),
        ('LPT' + [char]0x00B3)
    )
}

function Test-WindowsInvalidFileNameCharacter {
    param([Parameter(Mandatory = $true)][char]$Character)

    return [char]::IsControl($Character) -or $Character -in [char[]]'<>:"\|?*'
}

function Test-ContainsUnicodeFormatCharacter {
    param([Parameter(Mandatory = $true)][string]$Segment)

    for ($index = 0; $index -lt $Segment.Length; $index++) {
        $category = [System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($Segment, $index)
        if ($category -eq [System.Globalization.UnicodeCategory]::Format) {
            return $true
        }

        if ([char]::IsHighSurrogate($Segment[$index]) -and
            $index + 1 -lt $Segment.Length -and
            [char]::IsLowSurrogate($Segment[$index + 1])) {
            $index++
        }
    }

    return $false
}

function Test-SafePathSegment {
    param([Parameter(Mandatory = $true)][string]$Segment)

    if ($Segment.Length -eq 0 -or
        $Segment -eq '.' -or
        $Segment -eq '..' -or
        $Segment.EndsWith(' ', [System.StringComparison]::Ordinal) -or
        $Segment.EndsWith('.', [System.StringComparison]::Ordinal) -or
        (Test-ContainsUnicodeFormatCharacter -Segment $Segment) -or
        (Test-WindowsReservedDeviceName -Segment $Segment)) {
        return $false
    }

    foreach ($char in $Segment.ToCharArray()) {
        if (Test-WindowsInvalidFileNameCharacter -Character $char) {
            return $false
        }
    }

    return $true
}

function Assert-SafeTargetRid {
    param([Parameter(Mandatory = $true)][string]$Rid)

    if ([string]::IsNullOrWhiteSpace($Rid) -or $Rid -ne $Rid.Trim()) {
        throw "Unsafe target RID '$Rid'. Use a non-empty single RID segment without leading or trailing whitespace."
    }

    if (-not (Test-SafePathSegment -Segment $Rid) -or
        $Rid.Contains('..', [System.StringComparison]::Ordinal) -or
        $Rid -eq '.' -or
        $Rid -notmatch '^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$') {
        throw "Unsafe target RID '$Rid'. Use a single RID segment containing only letters, digits, dots, underscores, and hyphens."
    }
}

function Assert-SafeArtifactPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path) -or
        $Path.StartsWith('/', [System.StringComparison]::Ordinal) -or
        $Path.Contains('\', [System.StringComparison]::Ordinal) -or
        $Path.Contains(':', [System.StringComparison]::Ordinal) -or
        $Path.Contains([char]0, [System.StringComparison]::Ordinal)) {
        throw "Unsafe artifact path '$Path'. Use a non-empty forward-slash relative path."
    }

    foreach ($segment in $Path.Split('/')) {
        if (-not (Test-SafePathSegment -Segment $segment)) {
            throw "Unsafe artifact path '$Path'. Empty, '.', '..', trailing-space, trailing-dot, reserved device name, and Windows-invalid character segments are not allowed."
        }
    }
}

function ConvertTo-ArtifactPath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$File,
        [Parameter(Mandatory = $true)][string]$Prefix
    )

    $rootUri = [System.Uri]::new((Resolve-Path $Root).Path.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar)
    $fileUri = [System.Uri]::new((Resolve-Path $File).Path)
    $relative = [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($fileUri).ToString())
    $artifactPath = "$Prefix/$relative"
    Assert-SafeArtifactPath -Path $artifactPath
    return $artifactPath
}

function Get-Sha256Hex {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

Assert-SafeTargetRid -Rid $TargetRid

$outputRootPath = if ([System.IO.Path]::IsPathFullyQualified($OutputRoot)) {
    $OutputRoot
}
else {
    Join-Path $repoRoot $OutputRoot
}

$projects = @(
    @{
        Name = 'Contracts'
        Path = 'src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Contracts/Hcoona.AzureAuth.CredProvider.Contracts.csproj'
    },
    @{
        Name = 'Platform'
        Path = 'src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform/Hcoona.AzureAuth.CredProvider.Platform.csproj'
    }
)

$stagingRoot = Join-Path $outputRootPath "staging/$BuildOs/$TargetRid"
$packagePath = Join-Path $outputRootPath "azureauth-credprovider-foundation-internal-$BuildOs-$TargetRid.zip"
$pathMap = "$repoRoot=/_/three"

function Write-ArchiveEntryFromByteArray {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$EntryName,
        [Parameter(Mandatory = $true)][byte[]]$Bytes
    )

    $entry = $Archive.CreateEntry($EntryName, [System.IO.Compression.CompressionLevel]::NoCompression)
    $entry.LastWriteTime = [System.DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
    $stream = $entry.Open()
    try {
        $stream.Write($Bytes, 0, $Bytes.Length)
    }
    finally {
        $stream.Dispose()
    }
}

function Write-ArchiveEntryFromFile {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$EntryName,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $entry = $Archive.CreateEntry($EntryName, [System.IO.Compression.CompressionLevel]::NoCompression)
    $entry.LastWriteTime = [System.DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [System.TimeSpan]::Zero)
    $source = [System.IO.File]::OpenRead($Path)
    try {
        $destination = $entry.Open()
        try {
            $source.CopyTo($destination)
        }
        finally {
            $destination.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }
}

if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path $outputRootPath -Force | Out-Null

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project.Path
    $projectOutput = Join-Path $stagingRoot $project.Name
    New-Item -ItemType Directory -Path $projectOutput -Force | Out-Null

    if (-not $NoBuild) {
        dotnet restore $projectPath -p:RestoreLockedMode=true -p:ContinuousIntegrationBuild=true -p:Deterministic=true "-p:PathMap=$pathMap"
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }

        dotnet build $projectPath -c $Configuration -r $TargetRid --no-restore -o $projectOutput -p:RestoreLockedMode=true -p:ContinuousIntegrationBuild=true -p:Deterministic=true "-p:PathMap=$pathMap"
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }
    }
}

$artifactFiles = [System.Collections.Generic.List[object]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$null = $seen.Add('manifest.json')
foreach ($project in $projects) {
    $projectOutput = Join-Path $stagingRoot $project.Name
    foreach ($file in Get-ChildItem -LiteralPath $projectOutput -File -Recurse | Sort-Object FullName) {
        $artifactPath = ConvertTo-ArtifactPath -Root $projectOutput -File $file.FullName -Prefix $project.Name
        if (-not $seen.Add($artifactPath)) {
            throw "Duplicate or case-ambiguous artifact path '$artifactPath'."
        }

        $artifactFiles.Add([pscustomobject][ordered]@{
                sourcePath = $file.FullName
                path       = $artifactPath
                length     = $file.Length
                sha256     = Get-Sha256Hex -Path $file.FullName
            })
    }
}

$manifestFiles = @(
    $artifactFiles |
        Sort-Object path |
        ForEach-Object {
            [ordered]@{
                path   = $_.path
                length = $_.length
                sha256 = $_.sha256
            }
        }
)

$manifest = [ordered]@{
    schemaVersion   = 'azureauth-credprovider-foundation-artifact-v1'
    artifactName    = 'azureauth-credprovider-foundation'
    buildOs         = $BuildOs
    targetRid       = $TargetRid
    productVersion  = $ProductVersion
    sourceRevision  = $SourceRevision
    producedBy      = 'eng/scripts/azureauth-credprovider/New-FoundationArtifact.ps1'
    releaseStatus   = 'internal-non-release'
    signatureStatus = 'unsigned'
    isInternal      = $true
    isRelease       = $false
    isSigned        = $false
    files           = $manifestFiles
}

$manifestJson = $manifest | ConvertTo-Json -Depth 10 -Compress
$manifestBytes = [System.Text.Encoding]::UTF8.GetBytes($manifestJson)

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

$packageStream = [System.IO.File]::Create($packagePath)
try {
    $archive = [System.IO.Compression.ZipArchive]::new($packageStream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        Write-ArchiveEntryFromByteArray -Archive $archive -EntryName 'manifest.json' -Bytes $manifestBytes
        foreach ($file in ($artifactFiles | Sort-Object path)) {
            Write-ArchiveEntryFromFile -Archive $archive -EntryName $file.path -Path $file.sourcePath
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    $packageStream.Dispose()
}

Write-Output "Created internal non-release unsigned foundation artifact: $packagePath"
