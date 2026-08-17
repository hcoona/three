#Requires -Version 7.0

[CmdletBinding()]
param(
    [string]$InstallRoot,

    [string]$LegacyNuGetOwnershipManifestPath,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$bundleRoot = Split-Path -Parent $PSCommandPath
$manifestPath = Join-Path $bundleRoot 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'The deployment validation bundle manifest is missing.'
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 'azureauth-credprovider-deployment-validation-v1' -or
    $manifest.releaseStatus -ne 'internal-non-release' -or
    -not $manifest.isInternal -or
    $manifest.isRelease) {
    throw 'The bundle is not an internal deployment validation artifact.'
}
if ([string]::IsNullOrWhiteSpace([string]$manifest.productVersion) -or
    [string]::IsNullOrWhiteSpace([string]$manifest.pythonPackageVersion) -or
    [string]::IsNullOrWhiteSpace([string]$manifest.sourceRevision)) {
    throw 'The bundle version identity is incomplete.'
}
$runningOnWindows = $IsWindows
$expectedBuildOs = if ($runningOnWindows) {
    'Windows'
}
elseif ($IsLinux) {
    'Linux'
}
else {
    throw 'Deployment validation bundles support only Windows and Linux hosts.'
}
if ($manifest.buildOs -ne $expectedBuildOs) {
    throw "This $($manifest.buildOs) bundle cannot be installed on $expectedBuildOs."
}
$hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
if ($hostArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
    throw "Deployment validation bundles do not support host architecture '$hostArchitecture'."
}
$expectedTargetRid = if ($runningOnWindows) { 'win-x64' } else { 'linux-x64' }
$legacySourceRevision = 'f1bf00d412732739713a18e9a07e8738ff80c6f8'
if ($manifest.targetRid -ne $expectedTargetRid) {
    throw "This $($manifest.targetRid) bundle cannot be installed on $expectedTargetRid."
}

$pathComparison = if ($runningOnWindows) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
$bundleRoot = [System.IO.Path]::GetFullPath($bundleRoot)
$bundleRootPrefix = $bundleRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
) + [System.IO.Path]::DirectorySeparatorChar
$manifestFiles = @($manifest.files)
if ($manifestFiles.Count -eq 0) {
    throw 'The deployment validation bundle file inventory is empty.'
}

$pathComparer = if ($runningOnWindows) {
    [System.StringComparer]::OrdinalIgnoreCase
}
else {
    [System.StringComparer]::Ordinal
}
$legacyNuGetSupportPath = Join-Path $bundleRoot 'legacy-nuget.ps1'
if (-not (Test-Path -LiteralPath $legacyNuGetSupportPath -PathType Leaf)) {
    throw 'The deployment validation legacy NuGet support script is missing.'
}
. $legacyNuGetSupportPath
$listedPaths = [System.Collections.Generic.HashSet[string]]::new($pathComparer)
foreach ($entry in $manifestFiles) {
    $relativePath = [string]$entry.path
    if ([string]::IsNullOrWhiteSpace($relativePath) -or
        [System.IO.Path]::IsPathFullyQualified($relativePath)) {
        throw "The bundle manifest contains an invalid path '$relativePath'."
    }

    $candidatePath = [System.IO.Path]::GetFullPath(
        (Join-Path $bundleRoot $relativePath)
    )
    if (-not $candidatePath.StartsWith($bundleRootPrefix, $pathComparison) -or
        -not $listedPaths.Add($relativePath)) {
        throw "The bundle manifest contains an unsafe or duplicate path '$relativePath'."
    }
    if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
        throw "The bundle file '$relativePath' is missing."
    }

    $file = Get-Item -LiteralPath $candidatePath -Force
    $actualHash = (
        Get-FileHash -Algorithm SHA256 -LiteralPath $candidatePath
    ).Hash.ToLowerInvariant()
    if ($file.Length -ne [long]$entry.length -or
        $actualHash -ne ([string]$entry.sha256).ToLowerInvariant()) {
        throw "The bundle file '$relativePath' does not match its manifest."
    }
}

$actualFiles = @(
    Get-ChildItem -LiteralPath $bundleRoot -File -Recurse -Force |
        ForEach-Object {
            [System.IO.Path]::GetRelativePath(
                $bundleRoot,
                $_.FullName
            ).Replace('\', '/')
        } |
        Where-Object { $_ -ne 'manifest.json' }
)
if ($actualFiles.Count -ne $listedPaths.Count -or
    @($actualFiles | Where-Object { -not $listedPaths.Contains($_) }).Count -ne 0) {
    throw 'The deployment validation bundle file inventory does not match its manifest.'
}

$homeDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
if ([string]::IsNullOrWhiteSpace($homeDirectory)) {
    throw 'The current user profile directory is unavailable.'
}

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = if ($runningOnWindows) {
        $localAppData = [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData
        )
        Join-Path $localAppData 'AzureAuth/CredProvider/installation'
    }
    else {
        Join-Path $homeDirectory '.local/lib/azureauth-credprovider'
    }
}

$InstallRoot = [System.IO.Path]::GetFullPath($InstallRoot)
$installPathRoot = [System.IO.Path]::GetPathRoot($InstallRoot)
if ($InstallRoot -eq $installPathRoot -or
    $InstallRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) -eq
    $homeDirectory.TrimEnd([System.IO.Path]::DirectorySeparatorChar)) {
    throw "The deployment target '$InstallRoot' is too broad."
}

$applicationRoot = Join-Path $InstallRoot 'app'
$binRoot = Join-Path $InstallRoot 'bin'
$pythonRoot = Join-Path $InstallRoot 'python'

function Test-EmptyDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath
    )

    return @(Get-ChildItem -LiteralPath $LiteralPath -Force).Count -eq 0
}

function Copy-DirectoryContent {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,

        [Parameter(Mandatory = $true)]
        [string]$Destination
    )

    foreach ($entry in Get-ChildItem -LiteralPath $Source -Force) {
        Copy-Item -LiteralPath $entry.FullName -Destination $Destination -Recurse -Force
    }
}

function Get-SiblingDeploymentPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath,

        [Parameter(Mandatory = $true)]
        [string]$Purpose
    )

    $trimmedPath = $LiteralPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
    $parentPath = [System.IO.Path]::GetDirectoryName($trimmedPath)
    $leafName = [System.IO.Path]::GetFileName($trimmedPath)
    do {
        $candidatePath = Join-Path $parentPath (
            ".$leafName.$Purpose.$([System.Guid]::NewGuid().ToString('N'))"
        )
    } while (Test-Path -LiteralPath $candidatePath)

    return $candidatePath
}

$existingReceipt = $null
$legacyNuGetCleanupPlan = $null
if (Test-Path -LiteralPath $InstallRoot) {
    if (-not $Force) {
        throw "The deployment target '$InstallRoot' already exists. Use -Force to replace it."
    }
    if (-not (Test-Path -LiteralPath $InstallRoot -PathType Container)) {
        throw "The deployment target '$InstallRoot' is not a directory."
    }

    $existingReceiptPath = Join-Path $InstallRoot 'installation.json'
    if (Test-Path -LiteralPath $existingReceiptPath -PathType Leaf) {
        $existingReceipt = Get-Content -LiteralPath $existingReceiptPath -Raw |
            ConvertFrom-Json
        $receiptSchemaVersion = [string]$existingReceipt.schemaVersion
        if ($receiptSchemaVersion -notin @(
                'azureauth-credprovider-deployment-validation-install-v1',
                'azureauth-credprovider-deployment-validation-install-v2'
            )) {
            throw 'The existing deployment validation installation receipt is invalid.'
        }
        if ($receiptSchemaVersion -eq
            'azureauth-credprovider-deployment-validation-install-v1' -and
            -not (Test-ExactObjectPropertySet `
                    -Value $existingReceipt `
                    -ExpectedNames @(
                    'schemaVersion',
                    'productVersion',
                    'sourceRevision',
                    'targetRid',
                    'installRoot',
                    'applicationRoot',
                    'nugetPluginRoot'
                ))) {
            throw 'The legacy deployment validation installation receipt is invalid.'
        }

        $receiptInstallRoot = [System.IO.Path]::GetFullPath(
            [string]$existingReceipt.installRoot
        )
        $receiptApplicationRoot = [System.IO.Path]::GetFullPath(
            [string]$existingReceipt.applicationRoot
        )
        if (-not $InstallRoot.Equals($receiptInstallRoot, $pathComparison) -or
            -not $applicationRoot.Equals($receiptApplicationRoot, $pathComparison)) {
            throw 'The replacement roots do not match the existing installation receipt.'
        }

        if ($receiptSchemaVersion -eq
            'azureauth-credprovider-deployment-validation-install-v1') {
            if ($existingReceipt.sourceRevision -cne $legacySourceRevision -or
                $existingReceipt.targetRid -cne $expectedTargetRid -or
                [string]::IsNullOrWhiteSpace([string]$existingReceipt.nugetPluginRoot)) {
                throw 'The legacy deployment validation installation receipt is invalid.'
            }
            $legacyNuGetPluginRoot = [System.IO.Path]::GetFullPath(
                [string]$existingReceipt.nugetPluginRoot
            )
            $legacyPathRoot = [System.IO.Path]::GetPathRoot($legacyNuGetPluginRoot)
            if ($legacyNuGetPluginRoot -eq $legacyPathRoot -or
                $legacyNuGetPluginRoot.TrimEnd(
                    [System.IO.Path]::DirectorySeparatorChar,
                    [System.IO.Path]::AltDirectorySeparatorChar
                ) -eq $homeDirectory.TrimEnd(
                    [System.IO.Path]::DirectorySeparatorChar,
                    [System.IO.Path]::AltDirectorySeparatorChar
                )) {
                throw "The legacy NuGet deployment target '$legacyNuGetPluginRoot' is too broad."
            }
            $legacyNuGetPluginRootPrefix = $legacyNuGetPluginRoot.TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar
            ) + [System.IO.Path]::DirectorySeparatorChar
            $currentInstallRootPrefix = $InstallRoot.TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar
            ) + [System.IO.Path]::DirectorySeparatorChar
            if ($InstallRoot.Equals($legacyNuGetPluginRoot, $pathComparison) -or
                $InstallRoot.StartsWith($legacyNuGetPluginRootPrefix, $pathComparison) -or
                $legacyNuGetPluginRoot.StartsWith($currentInstallRootPrefix, $pathComparison)) {
                throw 'The product and legacy NuGet plugin roots must not overlap.'
            }
            if ([string]::IsNullOrWhiteSpace($LegacyNuGetOwnershipManifestPath)) {
                $LegacyNuGetOwnershipManifestPath = Join-Path $homeDirectory (
                    '.azureauth-credprovider/phase10/manifests/' +
                    'nuget-plugin-layout-ownership-manifest.json'
                )
            }
            $LegacyNuGetOwnershipManifestPath = [System.IO.Path]::GetFullPath(
                $LegacyNuGetOwnershipManifestPath
            )
            $legacyNuGetCleanupPlan = Get-LegacyNuGetCleanupPlan `
                -ApplicationRoot $receiptApplicationRoot `
                -NuGetPluginRoot $legacyNuGetPluginRoot `
                -OwnershipManifestPath $LegacyNuGetOwnershipManifestPath `
                -PathComparer $pathComparer `
                -PathComparison $pathComparison
        }
    }
    elseif (-not (Test-EmptyDirectory -LiteralPath $InstallRoot)) {
        throw (
            "The deployment target '$InstallRoot' is non-empty and does not contain " +
            'a recognized installation receipt.'
        )
    }
}

$stagedInstallRoot = Get-SiblingDeploymentPath -LiteralPath $InstallRoot -Purpose 'installing'
$backupInstallRoot = Get-SiblingDeploymentPath -LiteralPath $InstallRoot -Purpose 'backup'
$stagedApplicationRoot = Join-Path $stagedInstallRoot 'app'
$stagedBinRoot = Join-Path $stagedInstallRoot 'bin'
$stagedPythonRoot = Join-Path $stagedInstallRoot 'python'

try {
    New-Item -ItemType Directory -Path $stagedApplicationRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $stagedBinRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $stagedPythonRoot -Force | Out-Null

    Copy-DirectoryContent `
        -Source (Join-Path $bundleRoot 'app') `
        -Destination $stagedApplicationRoot
    Copy-DirectoryContent `
        -Source (Join-Path $bundleRoot 'launchers') `
        -Destination $stagedBinRoot
    Copy-DirectoryContent `
        -Source (Join-Path $bundleRoot 'python') `
        -Destination $stagedPythonRoot

    $productExecutableName = if ($runningOnWindows) {
        'azureauth-credprovider.exe'
    }
    else {
        'azureauth-credprovider'
    }
    $productExecutablePath = Join-Path $stagedApplicationRoot $productExecutableName
    if (-not (Test-Path -LiteralPath $productExecutablePath -PathType Leaf)) {
        throw 'The installed application payload is incomplete.'
    }

    $nugetEntrypointPath = Join-Path $stagedApplicationRoot 'azureauth-credprovider.dll'
    if (-not (Test-Path -LiteralPath $nugetEntrypointPath -PathType Leaf)) {
        throw 'The installed NuGet plugin payload is incomplete.'
    }

    if (-not $runningOnWindows) {
        $executableMode =
        [System.IO.UnixFileMode]::UserRead -bor
        [System.IO.UnixFileMode]::UserWrite -bor
        [System.IO.UnixFileMode]::UserExecute -bor
        [System.IO.UnixFileMode]::GroupRead -bor
        [System.IO.UnixFileMode]::GroupExecute -bor
        [System.IO.UnixFileMode]::OtherRead -bor
        [System.IO.UnixFileMode]::OtherExecute
        [System.IO.File]::SetUnixFileMode($productExecutablePath, $executableMode)
        foreach ($launcher in Get-ChildItem -LiteralPath $stagedBinRoot -File) {
            [System.IO.File]::SetUnixFileMode($launcher.FullName, $executableMode)
        }
    }

    $receipt = [ordered]@{
        schemaVersion        = 'azureauth-credprovider-deployment-validation-install-v2'
        productVersion       = $manifest.productVersion
        pythonPackageVersion = $manifest.pythonPackageVersion
        sourceRevision       = $manifest.sourceRevision
        targetRid            = $manifest.targetRid
        installRoot          = $InstallRoot
        applicationRoot      = $applicationRoot
    }
    $receipt | ConvertTo-Json -Depth 5 |
        Set-Content `
            -LiteralPath (Join-Path $stagedInstallRoot 'installation.json') `
            -Encoding utf8
}
catch {
    if (Test-Path -LiteralPath $stagedInstallRoot) {
        Remove-Item -LiteralPath $stagedInstallRoot -Recurse -Force
    }
    throw
}

$installBackupCreated = $false
$installRootActivated = $false
try {
    if (Test-Path -LiteralPath $InstallRoot) {
        Move-Item -LiteralPath $InstallRoot -Destination $backupInstallRoot
        $installBackupCreated = $true
    }
    Move-Item -LiteralPath $stagedInstallRoot -Destination $InstallRoot
    $installRootActivated = $true
    if ($null -ne $legacyNuGetCleanupPlan) {
        Remove-LegacyNuGetPayload -Plan $legacyNuGetCleanupPlan
    }
}
catch {
    $switchFailure = $_
    $rollbackFailures = [System.Collections.Generic.List[System.Exception]]::new()

    if ($installRootActivated -and (Test-Path -LiteralPath $InstallRoot)) {
        try {
            Remove-Item -LiteralPath $InstallRoot -Recurse -Force
        }
        catch {
            $rollbackFailures.Add($_.Exception)
        }
    }

    if ($installBackupCreated -and (Test-Path -LiteralPath $backupInstallRoot)) {
        try {
            Move-Item `
                -LiteralPath $backupInstallRoot `
                -Destination $InstallRoot
        }
        catch {
            $rollbackFailures.Add($_.Exception)
        }
    }

    if (Test-Path -LiteralPath $stagedInstallRoot) {
        try {
            Remove-Item -LiteralPath $stagedInstallRoot -Recurse -Force
        }
        catch {
            $rollbackFailures.Add($_.Exception)
        }
    }

    if ($rollbackFailures.Count -gt 0) {
        $failures = [System.Collections.Generic.List[System.Exception]]::new()
        $failures.Add($switchFailure.Exception)
        foreach ($failure in $rollbackFailures) {
            $failures.Add($failure)
        }
        throw [System.AggregateException]::new(
            'Deployment replacement failed and the previous installation could not be fully restored.',
            $failures
        )
    }

    throw $switchFailure
}

$undeletedBackupPaths = [System.Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath $backupInstallRoot) {
    try {
        Remove-Item -LiteralPath $backupInstallRoot -Recurse -Force
    }
    catch {
        if (Test-Path -LiteralPath $backupInstallRoot) {
            $undeletedBackupPaths.Add($backupInstallRoot)
            Write-Warning (
                "Post-commit cleanup could not completely remove deployment backup data at " +
                "'$backupInstallRoot': $($_.Exception.Message) The active installation remains " +
                'committed; data left at this path may be incomplete and must not be treated as ' +
                'a fully recoverable backup.'
            ) -WarningAction Continue
        }
        else {
            Write-Warning (
                "Post-commit cleanup reported a failure after removing deployment backup " +
                "data at '$backupInstallRoot': $($_.Exception.Message) The active installation " +
                'remains committed.'
            ) -WarningAction Continue
        }
    }
}

if ($undeletedBackupPaths.Count -gt 0) {
    Write-Warning (
        'Undeleted post-commit backup paths: ' +
        ($undeletedBackupPaths -join [System.Environment]::NewLine)
    ) -WarningAction Continue
}

Write-Output "Installed internal deployment validation payload: $InstallRoot"
Write-Output "CLI activation directory: $binRoot"
Write-Output "Python wheel directory: $pythonRoot"
Write-Output 'No global PATH, shell profile, registry, Git, NuGet, or Python environment was modified.'
