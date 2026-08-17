#Requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Windows', 'Linux')]
    [string]$BuildOs,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$TargetRid,

    [ValidateNotNullOrEmpty()]
    [string]$Configuration = 'Release',

    [ValidateNotNullOrEmpty()]
    [string]$OutputRoot = 'artifacts/azureauth-credprovider/deployment-validation',

    [switch]$NoBuild,

    [Parameter(DontShow = $true)]
    [scriptblock]$BeforeArchiveEntry,

    [Parameter(DontShow = $true)]
    [scriptblock]$BeforePackageReplace
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = (Resolve-Path (Join-Path $scriptRoot '../../..')).Path
$outputRootPath = if ([System.IO.Path]::IsPathFullyQualified($OutputRoot)) {
    $OutputRoot
}
else {
    Join-Path $repoRoot $OutputRoot
}

$actualBuildOs = if ($IsWindows) {
    'Windows'
}
elseif ($IsLinux) {
    'Linux'
}
else {
    throw 'Deployment validation bundle generation supports only Windows and Linux hosts.'
}
if ($BuildOs -ne $actualBuildOs) {
    throw "Build OS '$BuildOs' does not match the current host '$actualBuildOs'."
}
$hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
if ($hostArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
    throw "Deployment validation bundle generation requires an x64 host, not '$hostArchitecture'."
}

$expectedRid = if ($BuildOs -eq 'Windows') { 'win-x64' } else { 'linux-x64' }
if (-not $TargetRid.Equals($expectedRid, [System.StringComparison]::Ordinal)) {
    throw "Target RID '$TargetRid' does not match build OS '$BuildOs'."
}

$projectPath = Join-Path $repoRoot (
    'src/private/app/azureauth-credprovider/' +
    'Hcoona.AzureAuth.CredProvider.Cli/Hcoona.AzureAuth.CredProvider.Cli.csproj'
)
$projectRoot = Split-Path -Parent $projectPath
Push-Location -LiteralPath $repoRoot
try {
    $versionOutput = dotnet tool run nbgv get-version -f json -p $projectRoot
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
    $versionInfo = ($versionOutput -join [System.Environment]::NewLine) | ConvertFrom-Json
    $productVersion = [string]$versionInfo.AssemblyInformationalVersion
    $sourceRevision = [string]$versionInfo.GitCommitId
    $semVer2 = [string]$versionInfo.SemVer2
    if ([string]::IsNullOrWhiteSpace($productVersion) -or
        [string]::IsNullOrWhiteSpace($sourceRevision) -or
        [string]::IsNullOrWhiteSpace($semVer2)) {
        throw 'NBGV did not resolve the required AzureAuth component version fields.'
    }
    $pythonPackageVersion = uv run `
        --package nbgv-python `
        python -c (
        'from nbgv_python.versioning import normalize_version_field; ' +
        'import sys; print(normalize_version_field(sys.argv[1], field="SemVer2"))'
    ) $semVer2
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}
$pythonPackageVersion = ([string]$pythonPackageVersion).Trim()
if ([string]::IsNullOrWhiteSpace($pythonPackageVersion)) {
    throw 'The nbgv-python version normalizer returned an empty Python package version.'
}

$stagingRoot = Join-Path $outputRootPath "staging/$BuildOs/$TargetRid"
$appRoot = Join-Path $stagingRoot 'app'
$launcherRoot = Join-Path $stagingRoot 'launchers'
$pythonRoot = Join-Path $stagingRoot 'python'
$buildIdentityPath = Join-Path $stagingRoot '.build-identity.json'
$productExecutableName = if ($BuildOs -eq 'Windows') {
    'azureauth-credprovider.exe'
}
else {
    'azureauth-credprovider'
}
$productExecutablePath = Join-Path $appRoot $productExecutableName
$packagePath = Join-Path $outputRootPath (
    "azureauth-credprovider-deployment-validation-internal-$BuildOs-$TargetRid.zip"
)
$temporaryPackagePath = "$packagePath.$([System.Guid]::NewGuid().ToString('N')).tmp"

function Assert-DeploymentPayloadMatchesBuildIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApplicationRoot,

        [Parameter(Mandatory = $true)]
        [string]$ProductExecutablePath,

        [Parameter(Mandatory = $true)]
        [string]$PythonRoot,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedProductVersion,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedPythonPackageVersion
    )

    $nugetEntrypointPath = Join-Path $ApplicationRoot 'azureauth-credprovider.dll'
    if (-not (Test-Path -LiteralPath $ProductExecutablePath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $nugetEntrypointPath -PathType Leaf)) {
        throw 'The published application payload is incomplete.'
    }

    $componentVersionOutput = & $ProductExecutablePath --version
    if ($LASTEXITCODE -ne 0 -or
        ([string]$componentVersionOutput).Trim() -cne
        "azureauth-credprovider $ExpectedProductVersion") {
        throw 'The published application version does not match the NBGV build identity.'
    }

    $distributionName = 'azureauth-credprovider-keyring'
    $wheelDistributionName = 'azureauth_credprovider_keyring'
    $expectedWheelName = (
        "$wheelDistributionName-$ExpectedPythonPackageVersion-py3-none-any.whl"
    )
    $expectedDistInfoRoot = (
        "$wheelDistributionName-$ExpectedPythonPackageVersion.dist-info"
    )
    $expectedPackageModules = @(
        "$wheelDistributionName/__init__.py"
        "$wheelDistributionName/backend.py"
        "$wheelDistributionName/contracts.py"
        "$wheelDistributionName/endpoint.py"
        "$wheelDistributionName/helper.py"
        "$wheelDistributionName/integrity.py"
        "$wheelDistributionName/shim.py"
    )
    $expectedEntries = @(
        "$expectedDistInfoRoot/METADATA"
        "$expectedDistInfoRoot/WHEEL"
        "$expectedDistInfoRoot/RECORD"
        "$expectedDistInfoRoot/entry_points.txt"
    ) + $expectedPackageModules

    $wheel = @(Get-ChildItem -LiteralPath $PythonRoot -Filter '*.whl' -File)
    if ($wheel.Count -ne 1) {
        throw 'The deployment validation bundle requires exactly one Python wheel.'
    }
    if ($wheel[0].Name -cne $expectedWheelName) {
        throw "The Python wheel filename must be '$expectedWheelName'."
    }

    $wheelArchive = [System.IO.Compression.ZipFile]::OpenRead($wheel[0].FullName)
    try {
        $archiveEntries = [System.Collections.Generic.Dictionary[string, object]]::new(
            [System.StringComparer]::Ordinal
        )
        foreach ($entry in $wheelArchive.Entries) {
            if ($archiveEntries.ContainsKey($entry.FullName)) {
                throw "The Python wheel contains duplicate entry '$($entry.FullName)'."
            }
            $archiveEntries.Add($entry.FullName, $entry)
        }
        foreach ($expectedEntry in $expectedEntries) {
            if (-not $archiveEntries.ContainsKey($expectedEntry)) {
                throw "The Python wheel is missing required entry '$expectedEntry'."
            }
        }
        $packageModules = @(
            $wheelArchive.Entries |
                Where-Object {
                    $_.FullName.StartsWith(
                        "$wheelDistributionName/",
                        [System.StringComparison]::Ordinal
                    ) -and
                    $_.FullName.EndsWith('.py', [System.StringComparison]::Ordinal)
                } |
                ForEach-Object FullName |
                Sort-Object
        )
        if (($packageModules | ConvertTo-Json -Compress) -cne
            ($expectedPackageModules | Sort-Object | ConvertTo-Json -Compress)) {
            throw 'The Python wheel package module inventory is invalid.'
        }

        $distInfoEntries = @(
            $wheelArchive.Entries |
                Where-Object FullName -Like '*.dist-info/*'
        )
        if ($distInfoEntries.Count -eq 0 -or
            @(
                $distInfoEntries |
                    Where-Object {
                        -not $_.FullName.StartsWith(
                            "$expectedDistInfoRoot/",
                            [System.StringComparison]::Ordinal
                        )
                    }
            ).Count -ne 0) {
            throw "The Python wheel dist-info identity must be '$expectedDistInfoRoot'."
        }

        $metadataReader = [System.IO.StreamReader]::new(
            $archiveEntries["$expectedDistInfoRoot/METADATA"].Open()
        )
        try {
            $wheelMetadata = $metadataReader.ReadToEnd()
        }
        finally {
            $metadataReader.Dispose()
        }

        $wheelReader = [System.IO.StreamReader]::new(
            $archiveEntries["$expectedDistInfoRoot/WHEEL"].Open()
        )
        try {
            $wheelDescriptor = $wheelReader.ReadToEnd()
        }
        finally {
            $wheelReader.Dispose()
        }

        $entryPointsReader = [System.IO.StreamReader]::new(
            $archiveEntries["$expectedDistInfoRoot/entry_points.txt"].Open()
        )
        try {
            $entryPoints = $entryPointsReader.ReadToEnd()
        }
        finally {
            $entryPointsReader.Dispose()
        }
    }
    finally {
        $wheelArchive.Dispose()
    }
    $wheelNameMatch = [System.Text.RegularExpressions.Regex]::Match(
        $wheelMetadata,
        '(?m)^Name: (?<name>[^\r\n]+)\r?$'
    )
    if (-not $wheelNameMatch.Success -or
        $wheelNameMatch.Groups['name'].Value -cne $distributionName) {
        throw "The Python wheel metadata Name must be '$distributionName'."
    }
    $wheelVersionMatch = [System.Text.RegularExpressions.Regex]::Match(
        $wheelMetadata,
        '(?m)^Version: (?<version>[^\r\n]+)\r?$'
    )
    if (-not $wheelVersionMatch.Success -or
        $wheelVersionMatch.Groups['version'].Value -cne $ExpectedPythonPackageVersion) {
        throw 'The Python wheel version does not match the NBGV build identity.'
    }
    if ($wheelDescriptor -cnotmatch '(?m)^Root-Is-Purelib: true\r?$' -or
        $wheelDescriptor -cnotmatch '(?m)^Tag: py3-none-any\r?$') {
        throw 'The Python wheel descriptor must declare a pure Python py3-none-any wheel.'
    }

    $normalizedEntryPoints = (
        $entryPoints -replace "`r`n", "`n"
    ).TrimEnd("`n")
    $expectedEntryPoints = @(
        '[console_scripts]'
        'azureauth-keyring = azureauth_credprovider_keyring.shim:main'
        ''
        '[keyring.backends]'
        'azureauth = azureauth_credprovider_keyring.backend:AzureAuthKeyringBackend'
    ) -join "`n"
    if ($normalizedEntryPoints -cne $expectedEntryPoints) {
        throw 'The Python wheel entry points do not match the AzureAuth package contract.'
    }

    return $wheel[0]
}

New-Item -ItemType Directory -Path $outputRootPath -Force | Out-Null
$generationSucceeded = $false

try {
    if (-not $NoBuild) {
        if (Test-Path -LiteralPath $stagingRoot) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
        New-Item -ItemType Directory -Path $appRoot -Force | Out-Null
        New-Item -ItemType Directory -Path $pythonRoot -Force | Out-Null

        dotnet publish $projectPath `
            -c $Configuration `
            -r $TargetRid `
            --self-contained false `
            -o $appRoot `
            -p:RestoreLockedMode=true `
            -p:ContinuousIntegrationBuild=true `
            -p:Deterministic=true
        if ($LASTEXITCODE -ne 0) {
            exit $LASTEXITCODE
        }

        Push-Location $repoRoot
        try {
            uv build `
                --package azureauth-credprovider-keyring `
                --wheel `
                --out-dir $pythonRoot
            if ($LASTEXITCODE -ne 0) {
                exit $LASTEXITCODE
            }
        }
        finally {
            Pop-Location
        }

        [ordered]@{
            schemaVersion        = 'azureauth-credprovider-build-identity-v1'
            productVersion       = $productVersion
            pythonPackageVersion = $pythonPackageVersion
            sourceRevision       = $sourceRevision
        } | ConvertTo-Json -Compress |
            Set-Content -LiteralPath $buildIdentityPath -Encoding utf8
    }
    elseif (-not (Test-Path -LiteralPath $stagingRoot -PathType Container)) {
        throw "Canonical staging directory '$stagingRoot' does not exist. Run without -NoBuild first."
    }
    elseif (-not (Test-Path -LiteralPath $buildIdentityPath -PathType Leaf)) {
        throw "Canonical staging build identity '$buildIdentityPath' does not exist."
    }

    $buildIdentity = Get-Content -LiteralPath $buildIdentityPath -Raw | ConvertFrom-Json
    if ($buildIdentity.schemaVersion -ne 'azureauth-credprovider-build-identity-v1' -or
        $buildIdentity.productVersion -cne $productVersion -or
        $buildIdentity.pythonPackageVersion -cne $pythonPackageVersion -or
        $buildIdentity.sourceRevision -cne $sourceRevision) {
        throw 'The canonical staging payload does not match the current NBGV build identity.'
    }

    $wheel = Assert-DeploymentPayloadMatchesBuildIdentity `
        -ApplicationRoot $appRoot `
        -ProductExecutablePath $productExecutablePath `
        -PythonRoot $pythonRoot `
        -ExpectedProductVersion $productVersion `
        -ExpectedPythonPackageVersion $pythonPackageVersion

    New-Item -ItemType Directory -Path $launcherRoot -Force | Out-Null

    if ($BuildOs -eq 'Windows') {
        @"
@echo off
"%~dp0..\app\azureauth-credprovider.exe" %*
"@ | Set-Content -LiteralPath (Join-Path $launcherRoot 'azureauth-credprovider.cmd') -Encoding ascii
        @"
@echo off
"%~dp0..\app\azureauth-credprovider.exe" git credential-helper %*
"@ | Set-Content -LiteralPath (
            Join-Path $launcherRoot 'git-credential-azureauth-credprovider.cmd'
        ) -Encoding ascii
        $cliLauncherPath = 'launchers/azureauth-credprovider.cmd'
        $gitLauncherPath = 'launchers/git-credential-azureauth-credprovider.cmd'
    }
    else {
        @'
#!/bin/sh
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec "$script_dir/../app/azureauth-credprovider" "$@"
'@ | Set-Content -LiteralPath (Join-Path $launcherRoot 'azureauth-credprovider') -Encoding utf8
        @'
#!/bin/sh
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
exec "$script_dir/../app/azureauth-credprovider" git credential-helper "$@"
'@ | Set-Content -LiteralPath (
            Join-Path $launcherRoot 'git-credential-azureauth-credprovider'
        ) -Encoding utf8
        $cliLauncherPath = 'launchers/azureauth-credprovider'
        $gitLauncherPath = 'launchers/git-credential-azureauth-credprovider'
    }

    Copy-Item -LiteralPath (
        Join-Path $scriptRoot 'Install-DeploymentValidationBundle.ps1'
    ) -Destination (Join-Path $stagingRoot 'install.ps1')
    Copy-Item -LiteralPath (
        Join-Path $scriptRoot 'Uninstall-DeploymentValidationBundle.ps1'
    ) -Destination (Join-Path $stagingRoot 'uninstall.ps1')
    Copy-Item -LiteralPath (
        Join-Path $scriptRoot 'DeploymentValidationLegacyNuGet.ps1'
    ) -Destination (Join-Path $stagingRoot 'legacy-nuget.ps1')

    $manifestPath = Join-Path $stagingRoot 'manifest.json'
    if (Test-Path -LiteralPath $manifestPath) {
        Remove-Item -LiteralPath $manifestPath -Force
    }

    $files = @(
        Get-ChildItem -LiteralPath $stagingRoot -File -Recurse -Force |
            ForEach-Object {
                $relativePath = [System.IO.Path]::GetRelativePath(
                    $stagingRoot,
                    $_.FullName
                ).Replace('\', '/')
                [ordered]@{
                    path   = $relativePath
                    length = $_.Length
                    sha256 = (
                        Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
                    ).Hash.ToLowerInvariant()
                }
            } |
            Sort-Object path
    )

    $manifest = [ordered]@{
        schemaVersion        = 'azureauth-credprovider-deployment-validation-v1'
        artifactName         = 'azureauth-credprovider-deployment-validation'
        buildOs              = $BuildOs
        targetRid            = $TargetRid
        productVersion       = $productVersion
        pythonPackageVersion = $pythonPackageVersion
        sourceRevision       = $sourceRevision
        producedBy           = 'eng/scripts/azureauth-credprovider/New-DeploymentValidationBundle.ps1'
        releaseStatus        = 'internal-non-release'
        signatureStatus      = 'unsigned'
        isInternal           = $true
        isRelease            = $false
        isSigned             = $false
        entrypoints          = [ordered]@{
            cli         = "app/$productExecutableName"
            cliLauncher = $cliLauncherPath
            gitLauncher = $gitLauncherPath
            nugetPlugin = 'app/azureauth-credprovider.dll'
            pythonWheel = "python/$($wheel[0].Name)"
            installer   = 'install.ps1'
            uninstaller = 'uninstall.ps1'
        }
        files                = $files
    }
    $manifest | ConvertTo-Json -Depth 10 -Compress |
        Set-Content -LiteralPath $manifestPath -Encoding utf8

    $packageStream = [System.IO.File]::Open(
        $temporaryPackagePath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None
    )
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $packageStream,
            [System.IO.Compression.ZipArchiveMode]::Create
        )
        try {
            foreach ($file in Get-ChildItem -LiteralPath $stagingRoot -File -Recurse -Force |
                    Sort-Object FullName) {
                $entryName = [System.IO.Path]::GetRelativePath(
                    $stagingRoot,
                    $file.FullName
                ).Replace('\', '/')
                if ($null -ne $BeforeArchiveEntry) {
                    & $BeforeArchiveEntry $entryName
                }
                $entry = $archive.CreateEntry(
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::NoCompression
                )
                $entry.LastWriteTime = [System.DateTimeOffset]::new(
                    1980,
                    1,
                    1,
                    0,
                    0,
                    0,
                    [System.TimeSpan]::Zero
                )
                $source = [System.IO.File]::OpenRead($file.FullName)
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
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $packageStream.Dispose()
    }

    $validationArchive = [System.IO.Compression.ZipFile]::OpenRead($temporaryPackagePath)
    try {
        foreach ($entry in $validationArchive.Entries) {
            $source = $entry.Open()
            try {
                $source.CopyTo([System.IO.Stream]::Null)
            }
            finally {
                $source.Dispose()
            }
        }
    }
    finally {
        $validationArchive.Dispose()
    }

    if ($null -ne $BeforePackageReplace) {
        & $BeforePackageReplace $temporaryPackagePath $packagePath
    }

    [System.IO.File]::Move($temporaryPackagePath, $packagePath, $true)
    $generationSucceeded = $true
}
finally {
    if (Test-Path -LiteralPath $temporaryPackagePath) {
        Remove-Item -LiteralPath $temporaryPackagePath -Force
    }
    if (-not $NoBuild -and
        -not $generationSucceeded -and
        (Test-Path -LiteralPath $stagingRoot)) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

Write-Output "Created internal non-release unsigned deployment validation bundle: $packagePath"
