#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$BundlePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).ProviderPath
$installerSource = Join-Path $repoRoot 'eng/scripts/azureauth-credprovider/Install-DeploymentValidationBundle.ps1'
$uninstallerSource = Join-Path $repoRoot 'eng/scripts/azureauth-credprovider/Uninstall-DeploymentValidationBundle.ps1'
$legacyNuGetSupportSource = Join-Path $repoRoot 'eng/scripts/azureauth-credprovider/DeploymentValidationLegacyNuGet.ps1'
$bundleGeneratorSource = Join-Path $repoRoot 'eng/scripts/azureauth-credprovider/New-DeploymentValidationBundle.ps1'
$testBase = Join-Path $repoRoot 'artifacts/azureauth-credprovider/deployment-installer-tests'
$testRoot = Join-Path $testBase ([System.Guid]::NewGuid().ToString('N'))
$runningOnWindows = $IsWindows
$testPathComparison = if ($runningOnWindows) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
$buildOs = if ($runningOnWindows) { 'Windows' } else { 'Linux' }
$targetRid = if ($runningOnWindows) { 'win-x64' } else { 'linux-x64' }
$productExecutableName = if ($runningOnWindows) {
    'azureauth-credprovider.exe'
}
else {
    'azureauth-credprovider'
}
$deploymentPackageName = (
    "azureauth-credprovider-deployment-validation-internal-$buildOs-$targetRid.zip"
)

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

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Expected,

        [Parameter(Mandatory = $true)]
        [object]$Actual,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ($Expected -cne $Actual) {
        throw "$Message Expected '$Expected', actual '$Actual'."
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

function Get-FileSnapshotSet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$LiteralPath
    )

    $snapshots = [System.Collections.Generic.Dictionary[string, object]]::new(
        [System.StringComparer]::Ordinal
    )
    foreach ($path in $LiteralPath) {
        $snapshots.Add(
            $path,
            [pscustomobject]@{
                Content      = [System.IO.File]::ReadAllBytes($path)
                UnixFileMode = if ($IsWindows) {
                    $null
                }
                else {
                    [int][System.IO.File]::GetUnixFileMode($path)
                }
            }
        )
    }
    return $snapshots
}

function Assert-FileSnapshotSet {
    param(
        [Parameter(Mandatory = $true)]
        [System.Collections.Generic.Dictionary[string, object]]$Expected,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    foreach ($entry in $Expected.GetEnumerator()) {
        Assert-True (
            Test-Path -LiteralPath $entry.Key -PathType Leaf
        ) "A snapshotted file was removed during $Context`: $($entry.Key)"
        Assert-BytesEqual `
            -Expected $entry.Value.Content `
            -Actual ([System.IO.File]::ReadAllBytes($entry.Key)) `
            -Message "A snapshotted file changed during $Context`: $($entry.Key)"
        if ($null -ne $entry.Value.UnixFileMode) {
            Assert-Equal `
                -Expected $entry.Value.UnixFileMode `
                -Actual ([int][System.IO.File]::GetUnixFileMode($entry.Key)) `
                -Message "A snapshotted file mode changed during $Context`: $($entry.Key)"
        }
    }
}

function Assert-InvocationFailure {
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

function New-TestBundle {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BundleRoot
    )

    if (-not $PSCmdlet.ShouldProcess($BundleRoot, 'Create deployment validation test bundle')) {
        return
    }

    $appRoot = Join-Path $BundleRoot 'app'
    $launcherRoot = Join-Path $BundleRoot 'launchers'
    $pythonRoot = Join-Path $BundleRoot 'python'
    New-Item -ItemType Directory -Path $appRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $launcherRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $pythonRoot -Force | Out-Null

    Copy-Item -LiteralPath $installerSource -Destination (Join-Path $BundleRoot 'install.ps1')
    Copy-Item -LiteralPath $uninstallerSource -Destination (Join-Path $BundleRoot 'uninstall.ps1')
    Copy-Item `
        -LiteralPath $legacyNuGetSupportSource `
        -Destination (Join-Path $BundleRoot 'legacy-nuget.ps1')
    Set-Content `
        -LiteralPath (Join-Path $appRoot $productExecutableName) `
        -Value 'new executable payload' `
        -NoNewline
    Set-Content `
        -LiteralPath (Join-Path $appRoot 'azureauth-credprovider.dll') `
        -Value 'new plugin payload' `
        -NoNewline
    Set-Content `
        -LiteralPath (Join-Path $launcherRoot 'azureauth-credprovider') `
        -Value 'new launcher payload' `
        -NoNewline
    Set-Content `
        -LiteralPath (Join-Path $pythonRoot 'azureauth_credprovider-1.0.0-py3-none-any.whl') `
        -Value 'new wheel payload' `
        -NoNewline

    $files = @(
        Get-ChildItem -LiteralPath $BundleRoot -File -Recurse -Force |
            ForEach-Object {
                [ordered]@{
                    path   = [System.IO.Path]::GetRelativePath(
                        $BundleRoot,
                        $_.FullName
                    ).Replace('\', '/')
                    length = $_.Length
                    sha256 = (
                        Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
                    ).Hash.ToLowerInvariant()
                }
            } |
            Sort-Object path
    )
    $manifest = [ordered]@{
        schemaVersion  = 'azureauth-credprovider-deployment-validation-v1'
        buildOs        = $buildOs
        targetRid      = $targetRid
        productVersion = '2.0.0-test'
        sourceRevision = 'new-revision'
        releaseStatus  = 'internal-non-release'
        isInternal     = $true
        isRelease      = $false
        files          = $files
    }
    $manifest | ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath (Join-Path $BundleRoot 'manifest.json') -Encoding utf8
}

function New-DeploymentGeneratorStaging {
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot
    )

    if (-not $PSCmdlet.ShouldProcess(
            $OutputRoot,
            'Create deployment generator staging content'
        )) {
        return
    }

    $stagingRoot = Join-Path $OutputRoot "staging/$buildOs/$targetRid"
    $appRoot = Join-Path $stagingRoot 'app'
    $pythonRoot = Join-Path $stagingRoot 'python'
    New-Item -ItemType Directory -Path $appRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $pythonRoot -Force | Out-Null
    Set-Content `
        -LiteralPath (Join-Path $appRoot $productExecutableName) `
        -Value 'deployment generator executable' `
        -NoNewline
    Set-Content `
        -LiteralPath (Join-Path $appRoot 'azureauth-credprovider.dll') `
        -Value 'deployment generator plugin' `
        -NoNewline
    Set-Content `
        -LiteralPath (Join-Path $pythonRoot 'azureauth_credprovider-1.0.0-py3-none-any.whl') `
        -Value 'deployment generator wheel' `
        -NoNewline
}

function Invoke-DeploymentGenerator {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot,

        [scriptblock]$BeforeArchiveEntry,

        [scriptblock]$BeforePackageReplace
    )

    $parameters = @{
        BuildOs        = $buildOs
        TargetRid      = $targetRid
        OutputRoot     = $OutputRoot
        ProductVersion = '2.0.0-test'
        SourceRevision = 'generator-test'
        NoBuild        = $true
    }
    if ($null -ne $BeforeArchiveEntry) {
        $parameters.BeforeArchiveEntry = $BeforeArchiveEntry
    }
    if ($null -ne $BeforePackageReplace) {
        $parameters.BeforePackageReplace = $BeforePackageReplace
    }

    & $bundleGeneratorSource @parameters | Out-Null
}

function Assert-NoTemporaryDeploymentPackage {
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    $temporaryPackages = @(
        Get-ChildItem -LiteralPath $OutputRoot -File -Force |
            Where-Object Name -Like "$deploymentPackageName.*.tmp"
    )
    Assert-True (
        $temporaryPackages.Count -eq 0
    ) "A temporary deployment package remains after $Context."
}

function New-ExistingInstallation {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallRoot
    )

    if (-not $PSCmdlet.ShouldProcess($InstallRoot, 'Create existing test installation')) {
        return
    }

    $applicationRoot = Join-Path $InstallRoot 'app'
    New-Item -ItemType Directory -Path $applicationRoot -Force | Out-Null
    Set-Content `
        -LiteralPath (Join-Path $applicationRoot 'previous-product.txt') `
        -Value 'previous product payload' `
        -NoNewline

    $receipt = [ordered]@{
        schemaVersion   = 'azureauth-credprovider-deployment-validation-install-v2'
        productVersion  = '1.0.0-test'
        sourceRevision  = 'previous-revision'
        targetRid       = $targetRid
        installRoot     = [System.IO.Path]::GetFullPath($InstallRoot)
        applicationRoot = [System.IO.Path]::GetFullPath($applicationRoot)
    }
    $receipt | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $InstallRoot 'installation.json') -Encoding utf8
}

function New-F1LegacyInstallation {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallRoot,

        [Parameter(Mandatory = $true)]
        [string]$NuGetPluginRoot,

        [Parameter(Mandatory = $true)]
        [string]$OwnershipManifestPath
    )

    if (-not $PSCmdlet.ShouldProcess($InstallRoot, 'Create f1bf00d4 test installation')) {
        return
    }

    $applicationRoot = Join-Path $InstallRoot 'app'
    New-Item -ItemType Directory -Path (Join-Path $applicationRoot 'nested') -Force |
        Out-Null
    New-Item -ItemType Directory -Path (Join-Path $NuGetPluginRoot 'nested') -Force |
        Out-Null
    $payload = [ordered]@{
        $productExecutableName       = 'legacy executable payload'
        'azureauth-credprovider.dll' = 'legacy plugin entrypoint'
        'nested/dependency.dll'      = 'legacy plugin dependency'
    }
    foreach ($entry in $payload.GetEnumerator()) {
        Set-Content `
            -LiteralPath (Join-Path $applicationRoot $entry.Key) `
            -Value $entry.Value `
            -NoNewline
        Set-Content `
            -LiteralPath (Join-Path $NuGetPluginRoot $entry.Key) `
            -Value $entry.Value `
            -NoNewline
    }
    Set-Content `
        -LiteralPath (Join-Path $NuGetPluginRoot 'preserve.txt') `
        -Value 'unrelated legacy-root content' `
        -NoNewline
    $legacyMarker = (
        "azureauth-credprovider nuget-plugin-layout`n" +
        "phase=10`n" +
        "runtime=netcore`n" +
        "entrypoint=azureauth-credprovider.dll`n"
    )
    [System.IO.File]::WriteAllText(
        (Join-Path $NuGetPluginRoot '.azureauth-credprovider.nuget-plugin-layout'),
        $legacyMarker,
        [System.Text.UTF8Encoding]::new($false)
    )
    $ownershipManifest = [ordered]@{
        schemaVersion  = 1
        manifestId     = 'phase10-nuget-plugin-layout'
        ownerProductId = 'azureauth-credprovider'
        scope          = 'user'
        entrySelector  = 'nuget.plugin-layout'
        productVersion = 'phase10'
        safeMetadata   = [ordered]@{}
        entries        = @(
            [ordered]@{
                sequence         = 1
                targetKind       = 'nuGetPluginLayout'
                targetPathOrName = [System.IO.Path]::GetFullPath($NuGetPluginRoot)
                key              = 'physical-target'
            }
        )
    }
    $ownershipManifestDirectory = Split-Path -Parent $OwnershipManifestPath
    New-Item -ItemType Directory -Path $ownershipManifestDirectory -Force | Out-Null
    [System.IO.File]::WriteAllText(
        $OwnershipManifestPath,
        ($ownershipManifest | ConvertTo-Json -Depth 10 -Compress),
        [System.Text.UTF8Encoding]::new($false)
    )

    $receipt = [ordered]@{
        schemaVersion   = 'azureauth-credprovider-deployment-validation-install-v1'
        productVersion  = '1.0.0-test'
        sourceRevision  = 'f1bf00d412732739713a18e9a07e8738ff80c6f8'
        targetRid       = $targetRid
        installRoot     = [System.IO.Path]::GetFullPath($InstallRoot)
        applicationRoot = [System.IO.Path]::GetFullPath($applicationRoot)
        nugetPluginRoot = [System.IO.Path]::GetFullPath($NuGetPluginRoot)
    }
    $receipt | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath (Join-Path $InstallRoot 'installation.json') -Encoding utf8
}

function Assert-PreviousInstallationRestored {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallRoot,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    Assert-Equal `
        -Expected 'previous product payload' `
        -Actual (Get-Content -LiteralPath (
            Join-Path $InstallRoot 'app/previous-product.txt'
        ) -Raw) `
        -Message "The previous product payload was not restored after $Context."

    $receipt = Get-Content -LiteralPath (
        Join-Path $InstallRoot 'installation.json'
    ) -Raw | ConvertFrom-Json
    Assert-Equal `
        -Expected 'previous-revision' `
        -Actual $receipt.sourceRevision `
        -Message "The previous receipt was not restored after $Context."
    Assert-True (
        -not (Test-Path -LiteralPath (
                Join-Path $InstallRoot "app/$productExecutableName"
            ))
    ) "The replacement product payload remains after $Context."

    $trimmedPath = $InstallRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
    $parentPath = [System.IO.Path]::GetDirectoryName($trimmedPath)
    $leafName = [System.IO.Path]::GetFileName($trimmedPath)
    $workingPaths = @(
        Get-ChildItem -LiteralPath $parentPath -Force |
            Where-Object {
                $_.Name -like ".$leafName.installing.*" -or
                $_.Name -like ".$leafName.backup.*"
            }
    )
    Assert-True (
        $workingPaths.Count -eq 0
    ) "Replacement staging remains after $Context."
}

function Invoke-Installer {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$BundleRoot,

        [Parameter(Mandatory = $true)]
        [string]$InstallRoot,

        [string]$LegacyNuGetOwnershipManifestPath
    )

    $parameters = @{
        InstallRoot = $InstallRoot
        Force       = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($LegacyNuGetOwnershipManifestPath)) {
        $parameters.LegacyNuGetOwnershipManifestPath = $LegacyNuGetOwnershipManifestPath
    }
    if ($PSBoundParameters.ContainsKey('WarningAction')) {
        $parameters.WarningAction = $PSBoundParameters.WarningAction
    }

    & (Join-Path $BundleRoot 'install.ps1') @parameters | Out-Null
}

function Assert-ReplacementInstallationActive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallRoot,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    Assert-Equal `
        -Expected 'new executable payload' `
        -Actual (Get-Content -LiteralPath (
            Join-Path $InstallRoot "app/$productExecutableName"
        ) -Raw) `
        -Message "The replacement product payload is not active after $Context."

    $receipt = Get-Content -LiteralPath (
        Join-Path $InstallRoot 'installation.json'
    ) -Raw | ConvertFrom-Json
    Assert-Equal `
        -Expected 'new-revision' `
        -Actual $receipt.sourceRevision `
        -Message "The replacement receipt is not active after $Context."
}

function Invoke-ActualUninstallRegression {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackagePath
    )

    $resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
    $actualRoot = Join-Path $testRoot 'actual-uninstall'
    $extractedBundleRoot = Join-Path $actualRoot 'bundle'
    $installRoot = Join-Path $actualRoot 'install'
    $isolatedHome = Join-Path $actualRoot 'home'
    $pluginRoot = Join-Path $isolatedHome '.nuget/plugins/netcore/azureauth-credprovider'
    $isolatedLocalAppData = Join-Path $actualRoot 'local-app-data'
    $isolatedTemp = Join-Path $actualRoot 'temp'
    $npmConfigPath = Join-Path $actualRoot 'npm/userconfig.npmrc'
    New-Item -ItemType Directory -Path $extractedBundleRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $isolatedHome -Force | Out-Null
    New-Item -ItemType Directory -Path $isolatedLocalAppData -Force | Out-Null
    New-Item -ItemType Directory -Path $isolatedTemp -Force | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory(
        $resolvedPackagePath,
        $extractedBundleRoot
    )

    $jobId = 'deployment-validation-current-job'
    $token = 'deployment-validation-package-manager-token'
    $environment = [System.Collections.Generic.Dictionary[string, string]]::new(
        [System.StringComparer]::Ordinal
    )
    $environment['HOME'] = $isolatedHome
    $environment['USERPROFILE'] = $isolatedHome
    $environment['LOCALAPPDATA'] = $isolatedLocalAppData
    $environment['XDG_CONFIG_HOME'] = Join-Path $actualRoot 'xdg-config'
    $environment['XDG_DATA_HOME'] = Join-Path $actualRoot 'xdg-data'
    $environment['TMPDIR'] = $isolatedTemp
    $environment['TMP'] = $isolatedTemp
    $environment['TEMP'] = $isolatedTemp
    $environment['NPM_CONFIG_USERCONFIG'] = $npmConfigPath
    $environment['npm_config_userconfig'] = $npmConfigPath
    $environment['AZUREAUTH_CREDPROVIDER_CONFIG_ROOT'] = Join-Path $actualRoot 'azureauth-config'
    $environment['SYSTEM_ACCESSTOKEN'] = $token
    $environment['SYSTEM_JOBID'] = $jobId
    $environment['TF_BUILD'] = 'True'
    $savedEnvironment = [System.Collections.Generic.Dictionary[string, string]]::new(
        [System.StringComparer]::Ordinal
    )
    foreach ($name in $environment.Keys) {
        $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, $environment[$name])
    }
    $savedYarnRcFilename = [Environment]::GetEnvironmentVariable('YARN_RC_FILENAME')
    [Environment]::SetEnvironmentVariable('YARN_RC_FILENAME', $null)
    $gitMarkerPath = Join-Path $repoRoot '.git'
    $heldGitMarkerPath = Join-Path $repoRoot '.git.deployment-validation-test'

    try {
        & (Join-Path $extractedBundleRoot 'install.ps1') -InstallRoot $installRoot | Out-Null

        Assert-True (
            -not (Test-Path -LiteralPath $pluginRoot)
        ) 'Physical installation unexpectedly activated the NuGet plugin.'

        & (Join-Path $extractedBundleRoot 'uninstall.ps1') -InstallRoot $installRoot | Out-Null
        Assert-True (
            -not (Test-Path -LiteralPath $installRoot)
        ) 'Uninstall before NuGet configuration left the product payload.'
        Assert-True (
            -not (Test-Path -LiteralPath $pluginRoot)
        ) 'Uninstall before NuGet configuration created a NuGet activation.'

        & (Join-Path $extractedBundleRoot 'install.ps1') -InstallRoot $installRoot | Out-Null

        $productExecutablePath = Join-Path $installRoot "app/$productExecutableName"
        & $productExecutablePath configure nuget | Out-Null
        Assert-Equal `
            -Expected 0 `
            -Actual $LASTEXITCODE `
            -Message 'Actual NuGet configuration failed.'
        Assert-True (
            Test-Path -LiteralPath (
                Join-Path $pluginRoot 'azureauth-credprovider.dll'
            ) -PathType Leaf
        ) 'NuGet configuration did not create a discoverable plugin activation.'
        Set-Content `
            -LiteralPath (Join-Path $pluginRoot 'preserve.txt') `
            -Value 'unrelated activation-root content' `
            -NoNewline

        if (-not $runningOnWindows) {
            & $productExecutablePath configure python | Out-Null
            Assert-Equal `
                -Expected 0 `
                -Actual $LASTEXITCODE `
                -Message 'Actual Python subprocess bootstrap configuration failed.'

            $keyringShimPath = Join-Path (
                Join-Path $environment['XDG_DATA_HOME'] 'azureauth-credprovider/keyring-shim'
            ) 'keyring'
            Assert-True (
                Test-Path -LiteralPath $keyringShimPath -PathType Leaf
            ) 'Actual Python configuration did not create the keyring shim.'
            Assert-True (
                [System.IO.File]::GetUnixFileMode($keyringShimPath).HasFlag(
                    [System.IO.UnixFileMode]::UserExecute
                )
            ) 'Actual Python keyring shim is not executable.'
            $keyringShimContent = Get-Content -LiteralPath $keyringShimPath -Raw
            Assert-True (
                $keyringShimContent.Contains(
                    $productExecutablePath,
                    [System.StringComparison]::Ordinal
                )
            ) 'Actual Python keyring shim does not bind to the installed apphost.'
            Assert-True (
                -not $keyringShimContent.Contains(
                    'azureauth-keyring',
                    [System.StringComparison]::Ordinal
                )
            ) 'Actual Python keyring shim still depends on a Python environment console script.'

            & $keyringShimPath get 'https://example.com/simple/' 'requested-user' | Out-Null
            Assert-Equal `
                -Expected 1 `
                -Actual $LASTEXITCODE `
                -Message 'The installed keyring shim did not return no-credential for an unrelated host.'
        }

        $registryUrl = 'https://pkgs.dev.azure.com/example/project/_packaging/feed/npm/registry/'
        Move-Item -LiteralPath $gitMarkerPath -Destination $heldGitMarkerPath
        try {
            foreach ($ecosystem in @('npm', 'pnpm', 'yarn')) {
                & $productExecutablePath `
                    configure $ecosystem `
                    --registry-url $registryUrl `
                    --ci azure-pipelines | Out-Null
                Assert-Equal `
                    -Expected 0 `
                    -Actual $LASTEXITCODE `
                    -Message "Actual CI configuration failed for $ecosystem."
            }
        }
        finally {
            Move-Item -LiteralPath $heldGitMarkerPath -Destination $gitMarkerPath
        }

        $currentJobRoot = Join-Path (
            Join-Path $isolatedTemp 'azureauth-credprovider/phase14.2/ci-jobs'
        ) $jobId
        $manifestFiles = @(
            Get-ChildItem -LiteralPath $currentJobRoot -File -Recurse -Force |
                Where-Object Name -Like '*ownership-manifest.json'
        )
        Assert-True (
            $manifestFiles.Count -ge 2
        ) 'Actual CI configuration did not create package-manager ownership manifests.'
        $tokenFiles = @(
            Get-ChildItem -LiteralPath $currentJobRoot -File -Recurse -Force |
                Where-Object {
                    [string](Get-Content -LiteralPath $_.FullName -Raw) -like "*$token*"
                }
        )
        Assert-True (
            $tokenFiles.Count -ge 2
        ) 'Actual CI configuration did not materialize package-manager tokens.'

        $otherJobRoot = Join-Path (
            Split-Path -Parent $currentJobRoot
        ) 'deployment-validation-other-job'
        New-Item -ItemType Directory -Path $otherJobRoot -Force | Out-Null
        Set-Content `
            -LiteralPath (Join-Path $otherJobRoot 'preserve.txt') `
            -Value 'another job remains' `
            -NoNewline

        & (Join-Path $extractedBundleRoot 'uninstall.ps1') -InstallRoot $installRoot | Out-Null

        Assert-True (
            -not (Test-Path -LiteralPath $installRoot)
        ) 'Actual uninstall left the installed product payload.'
        Assert-True (
            -not (Test-Path -LiteralPath (
                    Join-Path $pluginRoot 'azureauth-credprovider.dll'
                ))
        ) 'Actual uninstall left the product-owned NuGet plugin activation.'
        Assert-Equal `
            -Expected 'unrelated activation-root content' `
            -Actual (Get-Content -LiteralPath (
                Join-Path $pluginRoot 'preserve.txt'
            ) -Raw) `
            -Message 'Actual uninstall removed unrelated NuGet activation-root content.'
        $remainingCurrentJobFiles = [System.Collections.Generic.List[string]]::new()
        if (Test-Path -LiteralPath $currentJobRoot) {
            foreach ($file in Get-ChildItem -LiteralPath $currentJobRoot -File -Recurse -Force) {
                if ($file.Name -ne '.lock') {
                    $remainingCurrentJobFiles.Add($file.FullName)
                }
            }
        }
        Assert-True (
            $remainingCurrentJobFiles.Count -eq 0
        ) (
            'Actual uninstall left generated current Azure Pipelines job state: ' +
            ($remainingCurrentJobFiles -join ', ')
        )
        Assert-True (
            Test-Path -LiteralPath (Join-Path $otherJobRoot 'preserve.txt') -PathType Leaf
        ) 'Actual uninstall removed another Azure Pipelines job state.'
    }
    finally {
        if (Test-Path -LiteralPath $heldGitMarkerPath) {
            Move-Item -LiteralPath $heldGitMarkerPath -Destination $gitMarkerPath
        }
        foreach ($name in $environment.Keys) {
            [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name])
        }
        [Environment]::SetEnvironmentVariable('YARN_RC_FILENAME', $savedYarnRcFilename)
    }
}

try {
    $generatorSuccessOutput = Join-Path $testRoot 'generator-success'
    New-DeploymentGeneratorStaging -OutputRoot $generatorSuccessOutput
    $generatorSuccessPackage = Join-Path $generatorSuccessOutput $deploymentPackageName
    $supersededPackage = [System.Text.Encoding]::UTF8.GetBytes('superseded deployment package')
    [System.IO.File]::WriteAllBytes($generatorSuccessPackage, $supersededPackage)

    Invoke-DeploymentGenerator -OutputRoot $generatorSuccessOutput

    Assert-True (
        -not [System.Linq.Enumerable]::SequenceEqual(
            $supersededPackage,
            [System.IO.File]::ReadAllBytes($generatorSuccessPackage)
        )
    ) 'Successful deployment packaging did not replace the existing ZIP.'
    $generatorArchive = [System.IO.Compression.ZipFile]::OpenRead($generatorSuccessPackage)
    try {
        Assert-True (
            $null -ne $generatorArchive.GetEntry('manifest.json')
        ) 'Successful deployment packaging produced an invalid ZIP.'
    }
    finally {
        $generatorArchive.Dispose()
    }

    Invoke-DeploymentGenerator -OutputRoot $generatorSuccessOutput
    $twiceNoBuildBundleRoot = Join-Path $testRoot 'twice-no-build-bundle'
    [System.IO.Compression.ZipFile]::ExtractToDirectory(
        $generatorSuccessPackage,
        $twiceNoBuildBundleRoot
    )
    $twiceNoBuildInstallRoot = Join-Path $testRoot 'twice-no-build-install'
    Invoke-Installer `
        -BundleRoot $twiceNoBuildBundleRoot `
        -InstallRoot $twiceNoBuildInstallRoot
    Assert-True (
        Test-Path -LiteralPath (
            Join-Path $twiceNoBuildInstallRoot "app/$productExecutableName"
        ) -PathType Leaf
    ) 'The twice-NoBuild bundle did not install its product payload.'
    $twiceNoBuildReceipt = Get-Content -LiteralPath (
        Join-Path $twiceNoBuildInstallRoot 'installation.json'
    ) -Raw | ConvertFrom-Json
    Assert-Equal `
        -Expected 'generator-test' `
        -Actual $twiceNoBuildReceipt.sourceRevision `
        -Message 'The twice-NoBuild bundle installed an unexpected receipt.'

    Assert-NoTemporaryDeploymentPackage `
        -OutputRoot $generatorSuccessOutput `
        -Context 'successful replacement'

    $generatorFailureOutput = Join-Path $testRoot 'generator-failure'
    New-DeploymentGeneratorStaging -OutputRoot $generatorFailureOutput
    $generatorFailurePackage = Join-Path $generatorFailureOutput $deploymentPackageName
    $preservedPackage = [System.Text.Encoding]::UTF8.GetBytes('preserved deployment package')
    [System.IO.File]::WriteAllBytes($generatorFailurePackage, $preservedPackage)

    Assert-InvocationFailure {
        Invoke-DeploymentGenerator `
            -OutputRoot $generatorFailureOutput `
            -BeforeArchiveEntry {
            param([string]$EntryName)
            throw "Injected packaging failure before '$EntryName'."
        }
    } 'Expected the injected deployment packaging failure.'
    Assert-BytesEqual `
        -Expected $preservedPackage `
        -Actual ([System.IO.File]::ReadAllBytes($generatorFailurePackage)) `
        -Message 'Deployment packaging failure changed the existing final ZIP.'
    Assert-NoTemporaryDeploymentPackage `
        -OutputRoot $generatorFailureOutput `
        -Context 'injected packaging failure'

    $bundleRoot = Join-Path $testRoot 'bundle'
    New-TestBundle -BundleRoot $bundleRoot

    $legacyRevisionRoot = Join-Path $testRoot 'legacy-revision-mismatch'
    $legacyRevisionInstallRoot = Join-Path $legacyRevisionRoot 'install'
    $legacyRevisionNuGetRoot = Join-Path $legacyRevisionRoot 'nuget'
    $legacyRevisionManifestPath = Join-Path $legacyRevisionRoot 'state/ownership.json'
    New-F1LegacyInstallation `
        -InstallRoot $legacyRevisionInstallRoot `
        -NuGetPluginRoot $legacyRevisionNuGetRoot `
        -OwnershipManifestPath $legacyRevisionManifestPath
    $legacyRevisionReceiptPath = Join-Path $legacyRevisionInstallRoot 'installation.json'
    $legacyRevisionReceipt = Get-Content -LiteralPath $legacyRevisionReceiptPath -Raw |
        ConvertFrom-Json
    $legacyRevisionReceipt.sourceRevision = 'not-f1bf00d4'
    $legacyRevisionReceipt | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $legacyRevisionReceiptPath -Encoding utf8
    $legacyRevisionSnapshots = Get-FileSnapshotSet -LiteralPath @(
        $legacyRevisionReceiptPath,
        (Join-Path $legacyRevisionInstallRoot 'app/azureauth-credprovider.dll'),
        (Join-Path $legacyRevisionNuGetRoot 'azureauth-credprovider.dll'),
        (Join-Path $legacyRevisionNuGetRoot '.azureauth-credprovider.nuget-plugin-layout'),
        $legacyRevisionManifestPath
    )

    Assert-InvocationFailure {
        Invoke-Installer `
            -BundleRoot $bundleRoot `
            -InstallRoot $legacyRevisionInstallRoot `
            -LegacyNuGetOwnershipManifestPath $legacyRevisionManifestPath
    } 'Expected a non-f1bf00d4 legacy receipt to block deployment replacement.'
    Assert-FileSnapshotSet `
        -Expected $legacyRevisionSnapshots `
        -Context 'non-f1bf00d4 installer validation'

    $legacyRidRoot = Join-Path $testRoot 'legacy-rid-mismatch'
    $legacyRidInstallRoot = Join-Path $legacyRidRoot 'install'
    $legacyRidNuGetRoot = Join-Path $legacyRidRoot 'nuget'
    $legacyRidManifestPath = Join-Path $legacyRidRoot 'state/ownership.json'
    New-F1LegacyInstallation `
        -InstallRoot $legacyRidInstallRoot `
        -NuGetPluginRoot $legacyRidNuGetRoot `
        -OwnershipManifestPath $legacyRidManifestPath
    $legacyRidReceiptPath = Join-Path $legacyRidInstallRoot 'installation.json'
    $legacyRidReceipt = Get-Content -LiteralPath $legacyRidReceiptPath -Raw |
        ConvertFrom-Json
    $legacyRidReceipt.targetRid = if ($runningOnWindows) { 'linux-x64' } else { 'win-x64' }
    $legacyRidReceipt | ConvertTo-Json -Depth 5 |
        Set-Content -LiteralPath $legacyRidReceiptPath -Encoding utf8
    $legacyRidSnapshots = Get-FileSnapshotSet -LiteralPath @(
        $legacyRidReceiptPath,
        (Join-Path $legacyRidInstallRoot 'app/azureauth-credprovider.dll'),
        (Join-Path $legacyRidNuGetRoot 'azureauth-credprovider.dll'),
        (Join-Path $legacyRidNuGetRoot '.azureauth-credprovider.nuget-plugin-layout'),
        $legacyRidManifestPath
    )

    Assert-InvocationFailure {
        & (Join-Path $bundleRoot 'uninstall.ps1') `
            -InstallRoot $legacyRidInstallRoot `
            -LegacyNuGetOwnershipManifestPath $legacyRidManifestPath `
            -SkipConfigurationCleanup |
            Out-Null
    } 'Expected a mismatched legacy receipt RID to block uninstall.'
    Assert-FileSnapshotSet `
        -Expected $legacyRidSnapshots `
        -Context 'legacy RID uninstaller validation'

    $legacyUpgradeRoot = Join-Path $testRoot 'legacy-upgrade'
    $legacyUpgradeInstallRoot = Join-Path $legacyUpgradeRoot 'install'
    $legacyUpgradeNuGetRoot = Join-Path $legacyUpgradeRoot 'nuget'
    $legacyUpgradeManifestPath = Join-Path $legacyUpgradeRoot 'state/ownership.json'
    New-F1LegacyInstallation `
        -InstallRoot $legacyUpgradeInstallRoot `
        -NuGetPluginRoot $legacyUpgradeNuGetRoot `
        -OwnershipManifestPath $legacyUpgradeManifestPath

    Invoke-Installer `
        -BundleRoot $bundleRoot `
        -InstallRoot $legacyUpgradeInstallRoot `
        -LegacyNuGetOwnershipManifestPath $legacyUpgradeManifestPath

    $upgradedReceipt = Get-Content -LiteralPath (
        Join-Path $legacyUpgradeInstallRoot 'installation.json'
    ) -Raw | ConvertFrom-Json
    Assert-Equal `
        -Expected 'azureauth-credprovider-deployment-validation-install-v2' `
        -Actual $upgradedReceipt.schemaVersion `
        -Message 'Installer did not migrate the exact f1bf00d4 receipt to v2.'
    foreach ($relativePath in @(
            $productExecutableName,
            'azureauth-credprovider.dll',
            'nested/dependency.dll',
            '.azureauth-credprovider.nuget-plugin-layout'
        )) {
        Assert-True (
            -not (Test-Path -LiteralPath (Join-Path $legacyUpgradeNuGetRoot $relativePath))
        ) "Installer left legacy product-owned NuGet content '$relativePath'."
    }
    Assert-Equal `
        -Expected 'unrelated legacy-root content' `
        -Actual (Get-Content -LiteralPath (
            Join-Path $legacyUpgradeNuGetRoot 'preserve.txt'
        ) -Raw) `
        -Message 'Installer removed unrelated content from the legacy NuGet root.'
    Assert-True (
        -not (Test-Path -LiteralPath $legacyUpgradeManifestPath)
    ) 'Installer left the exact f1bf00d4 ownership manifest.'

    $legacyRetryRoot = Join-Path $testRoot 'legacy-cleanup-retry'
    $legacyRetryInstallRoot = Join-Path $legacyRetryRoot 'install'
    $legacyRetryNuGetRoot = Join-Path $legacyRetryRoot 'nuget'
    $legacyRetryManifestPath = Join-Path $legacyRetryRoot 'state/ownership.json'
    New-F1LegacyInstallation `
        -InstallRoot $legacyRetryInstallRoot `
        -NuGetPluginRoot $legacyRetryNuGetRoot `
        -OwnershipManifestPath $legacyRetryManifestPath
    $legacyRetrySnapshots = Get-FileSnapshotSet -LiteralPath @(
        (Join-Path $legacyRetryInstallRoot 'installation.json'),
        (Join-Path $legacyRetryInstallRoot 'app/azureauth-credprovider.dll'),
        (Join-Path $legacyRetryNuGetRoot 'azureauth-credprovider.dll'),
        (Join-Path $legacyRetryNuGetRoot 'nested/dependency.dll'),
        (Join-Path $legacyRetryNuGetRoot '.azureauth-credprovider.nuget-plugin-layout'),
        $legacyRetryManifestPath
    )
    $legacyNuGetCleanupFailureState = [pscustomobject]@{
        FailurePath = Join-Path $legacyRetryNuGetRoot 'nested/dependency.dll'
        Injected    = $false
    }
    $legacyNuGetRemoveItemOverride = {
        [CmdletBinding(DefaultParameterSetName = 'Path')]
        param(
            [Parameter(Mandatory = $true, ParameterSetName = 'LiteralPath')]
            [string[]]$LiteralPath,

            [Parameter(Mandatory = $true, ParameterSetName = 'Path')]
            [string[]]$Path,

            [switch]$Recurse,

            [switch]$Force
        )

        $requestedPaths = if ($PSCmdlet.ParameterSetName -eq 'LiteralPath') {
            $LiteralPath
        }
        else {
            $Path
        }
        foreach ($requestedPath in $requestedPaths) {
            if (-not $legacyNuGetCleanupFailureState.Injected -and
                $requestedPath -eq $legacyNuGetCleanupFailureState.FailurePath) {
                $legacyNuGetCleanupFailureState.Injected = $true
                throw 'Injected deterministic legacy NuGet cleanup failure.'
            }

            if ($PSCmdlet.ParameterSetName -eq 'LiteralPath') {
                Microsoft.PowerShell.Management\Remove-Item `
                    -LiteralPath $requestedPath `
                    -Recurse:$Recurse `
                    -Force:$Force
            }
            else {
                Microsoft.PowerShell.Management\Remove-Item `
                    -Path $requestedPath `
                    -Recurse:$Recurse `
                    -Force:$Force
            }
        }
    }
    Set-Item `
        -LiteralPath Function:\Remove-Item `
        -Value $legacyNuGetRemoveItemOverride.GetNewClosure()
    try {
        $legacyRetryFailure = $null
        try {
            Invoke-Installer `
                -BundleRoot $bundleRoot `
                -InstallRoot $legacyRetryInstallRoot `
                -LegacyNuGetOwnershipManifestPath $legacyRetryManifestPath
        }
        catch {
            $legacyRetryFailure = $_
        }
        Assert-True `
            -Condition ($null -ne $legacyRetryFailure) `
            -Message 'Expected the injected legacy NuGet cleanup failure.'
    }
    finally {
        Microsoft.PowerShell.Management\Remove-Item `
            -LiteralPath Function:\Remove-Item `
            -Force
    }
    Assert-True (
        $legacyNuGetCleanupFailureState.Injected
    ) (
        'The legacy NuGet cleanup failure was not injected. Installer failure: ' +
        $legacyRetryFailure.Exception.Message
    )
    Assert-FileSnapshotSet `
        -Expected $legacyRetrySnapshots `
        -Context 'legacy NuGet cleanup rollback'

    Invoke-Installer `
        -BundleRoot $bundleRoot `
        -InstallRoot $legacyRetryInstallRoot `
        -LegacyNuGetOwnershipManifestPath $legacyRetryManifestPath
    $legacyRetryReceipt = Get-Content -LiteralPath (
        Join-Path $legacyRetryInstallRoot 'installation.json'
    ) -Raw | ConvertFrom-Json
    Assert-Equal `
        -Expected 'azureauth-credprovider-deployment-validation-install-v2' `
        -Actual $legacyRetryReceipt.schemaVersion `
        -Message 'Retry did not commit the v2 installation receipt.'
    Assert-True (
        -not (Test-Path -LiteralPath $legacyRetryManifestPath)
    ) 'Retry left the legacy NuGet ownership manifest.'
    foreach ($relativePath in @(
            'azureauth-credprovider.dll',
            'nested/dependency.dll',
            '.azureauth-credprovider.nuget-plugin-layout'
        )) {
        Assert-True (
            -not (Test-Path -LiteralPath (Join-Path $legacyRetryNuGetRoot $relativePath))
        ) "Retry left legacy NuGet content '$relativePath'."
    }
    Assert-Equal `
        -Expected 'unrelated legacy-root content' `
        -Actual (Get-Content -LiteralPath (
            Join-Path $legacyRetryNuGetRoot 'preserve.txt'
        ) -Raw) `
        -Message 'Retry removed unrelated legacy NuGet content.'

    $legacyDriftRoot = Join-Path $testRoot 'legacy-drift'
    $legacyDriftInstallRoot = Join-Path $legacyDriftRoot 'install'
    $legacyDriftNuGetRoot = Join-Path $legacyDriftRoot 'nuget'
    $legacyDriftManifestPath = Join-Path $legacyDriftRoot 'state/ownership.json'
    New-F1LegacyInstallation `
        -InstallRoot $legacyDriftInstallRoot `
        -NuGetPluginRoot $legacyDriftNuGetRoot `
        -OwnershipManifestPath $legacyDriftManifestPath
    Set-Content `
        -LiteralPath (Join-Path $legacyDriftNuGetRoot 'azureauth-credprovider.dll') `
        -Value 'drifted legacy plugin entrypoint' `
        -NoNewline

    Assert-InvocationFailure {
        Invoke-Installer `
            -BundleRoot $bundleRoot `
            -InstallRoot $legacyDriftInstallRoot `
            -LegacyNuGetOwnershipManifestPath $legacyDriftManifestPath
    } 'Expected legacy NuGet payload drift to block deployment replacement.'
    $driftedReceipt = Get-Content -LiteralPath (
        Join-Path $legacyDriftInstallRoot 'installation.json'
    ) -Raw | ConvertFrom-Json
    Assert-Equal `
        -Expected 'azureauth-credprovider-deployment-validation-install-v1' `
        -Actual $driftedReceipt.schemaVersion `
        -Message 'Drifted legacy replacement changed the existing receipt.'
    Assert-Equal `
        -Expected 'drifted legacy plugin entrypoint' `
        -Actual (Get-Content -LiteralPath (
            Join-Path $legacyDriftNuGetRoot 'azureauth-credprovider.dll'
        ) -Raw) `
        -Message 'Drifted legacy replacement mutated the NuGet payload.'

    $legacyUninstallRoot = Join-Path $testRoot 'legacy-uninstall'
    $legacyUninstallInstallRoot = Join-Path $legacyUninstallRoot 'install'
    $legacyUninstallNuGetRoot = Join-Path $legacyUninstallRoot 'nuget'
    $legacyUninstallManifestPath = Join-Path $legacyUninstallRoot 'state/ownership.json'
    New-F1LegacyInstallation `
        -InstallRoot $legacyUninstallInstallRoot `
        -NuGetPluginRoot $legacyUninstallNuGetRoot `
        -OwnershipManifestPath $legacyUninstallManifestPath

    & (Join-Path $bundleRoot 'uninstall.ps1') `
        -InstallRoot $legacyUninstallInstallRoot `
        -LegacyNuGetOwnershipManifestPath $legacyUninstallManifestPath `
        -SkipConfigurationCleanup |
        Out-Null

    Assert-True (
        -not (Test-Path -LiteralPath $legacyUninstallInstallRoot)
    ) 'SkipConfigurationCleanup uninstall left the legacy product payload.'
    foreach ($relativePath in @(
            $productExecutableName,
            'azureauth-credprovider.dll',
            'nested/dependency.dll',
            '.azureauth-credprovider.nuget-plugin-layout'
        )) {
        Assert-True (
            -not (Test-Path -LiteralPath (Join-Path $legacyUninstallNuGetRoot $relativePath))
        ) "SkipConfigurationCleanup uninstall left legacy NuGet content '$relativePath'."
    }
    Assert-Equal `
        -Expected 'unrelated legacy-root content' `
        -Actual (Get-Content -LiteralPath (
            Join-Path $legacyUninstallNuGetRoot 'preserve.txt'
        ) -Raw) `
        -Message 'SkipConfigurationCleanup uninstall removed unrelated NuGet content.'
    Assert-True (
        -not (Test-Path -LiteralPath $legacyUninstallManifestPath)
    ) 'SkipConfigurationCleanup uninstall left the exact f1bf00d4 ownership manifest.'

    $receiptFailureRoot = Join-Path $testRoot 'receipt-failure'
    $receiptFailureInstallRoot = Join-Path $receiptFailureRoot 'install'
    New-ExistingInstallation -InstallRoot $receiptFailureInstallRoot

    function global:Set-Content {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory = $true)]
            [string]$LiteralPath,

            [Parameter(ValueFromPipeline = $true)]
            [object]$Value,

            [string]$Encoding,

            [switch]$NoNewline
        )

        process {
            if ($LiteralPath -like '*.installing.*installation.json') {
                throw 'Injected deterministic receipt write failure.'
            }
            Microsoft.PowerShell.Management\Set-Content @PSBoundParameters
        }
    }
    try {
        Assert-InvocationFailure {
            Invoke-Installer `
                -BundleRoot $bundleRoot `
                -InstallRoot $receiptFailureInstallRoot
        } 'Expected the injected receipt write failure.'
    }
    finally {
        Microsoft.PowerShell.Management\Remove-Item `
            -LiteralPath Function:\Set-Content `
            -Force
    }
    Assert-PreviousInstallationRestored `
        -InstallRoot $receiptFailureInstallRoot `
        -Context 'receipt staging failure'

    $switchFailureRoot = Join-Path $testRoot 'switch-failure'
    $switchFailureInstallRoot = Join-Path $switchFailureRoot 'install'
    New-ExistingInstallation -InstallRoot $switchFailureInstallRoot

    $failureDestination = $switchFailureInstallRoot
    $moveItemOverride = {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory = $true)]
            [string]$LiteralPath,

            [Parameter(Mandatory = $true)]
            [string]$Destination,

            [switch]$Force
        )

        if ($LiteralPath -like '*.installing.*' -and
            [System.IO.Path]::GetFullPath($Destination).Equals(
                [System.IO.Path]::GetFullPath($failureDestination),
                $testPathComparison
            )) {
            throw 'Injected deterministic product activation failure.'
        }
        Microsoft.PowerShell.Management\Move-Item @PSBoundParameters
    }
    Set-Item `
        -LiteralPath Function:\Move-Item `
        -Value $moveItemOverride.GetNewClosure()
    try {
        Assert-InvocationFailure {
            Invoke-Installer `
                -BundleRoot $bundleRoot `
                -InstallRoot $switchFailureInstallRoot
        } 'Expected the injected product activation failure.'
    }
    finally {
        Microsoft.PowerShell.Management\Remove-Item `
            -LiteralPath Function:\Move-Item `
            -Force
    }
    Assert-PreviousInstallationRestored `
        -InstallRoot $switchFailureInstallRoot `
        -Context 'product activation failure'

    $cleanupFailureRoot = Join-Path $testRoot 'cleanup-failure'
    $cleanupFailureInstallRoot = Join-Path $cleanupFailureRoot 'install'
    New-ExistingInstallation -InstallRoot $cleanupFailureInstallRoot

    $deploymentInstallerCleanupFailureState = [pscustomobject]@{
        Injected = $false
    }
    $deploymentInstallerRemoveItemOverride = {
        [CmdletBinding(DefaultParameterSetName = 'Path')]
        param(
            [Parameter(Mandatory = $true, ParameterSetName = 'LiteralPath')]
            [string[]]$LiteralPath,

            [Parameter(Mandatory = $true, ParameterSetName = 'Path')]
            [string[]]$Path,

            [switch]$Recurse,

            [switch]$Force
        )

        $requestedPaths = if ($PSCmdlet.ParameterSetName -eq 'LiteralPath') {
            $LiteralPath
        }
        else {
            $Path
        }
        foreach ($requestedPath in $requestedPaths) {
            if (-not $deploymentInstallerCleanupFailureState.Injected -and
                $requestedPath -like '*.install.backup.*') {
                $deploymentInstallerCleanupFailureState.Injected = $true
                $removedPayloadPath = Join-Path $requestedPath 'app/previous-product.txt'
                if (Test-Path -LiteralPath $removedPayloadPath) {
                    Microsoft.PowerShell.Management\Remove-Item `
                        -LiteralPath $removedPayloadPath `
                        -Force
                }
                throw 'Injected deterministic post-commit cleanup failure.'
            }

            if ($PSCmdlet.ParameterSetName -eq 'LiteralPath') {
                Microsoft.PowerShell.Management\Remove-Item `
                    -LiteralPath $requestedPath `
                    -Recurse:$Recurse `
                    -Force:$Force
            }
            else {
                Microsoft.PowerShell.Management\Remove-Item `
                    -Path $requestedPath `
                    -Recurse:$Recurse `
                    -Force:$Force
            }
        }
    }
    Set-Item `
        -LiteralPath Function:\Remove-Item `
        -Value $deploymentInstallerRemoveItemOverride.GetNewClosure()
    try {
        $savedWarningPreference = $WarningPreference
        $WarningPreference = 'Stop'
        $cleanupWarnings = @(
            Invoke-Installer `
                -BundleRoot $bundleRoot `
                -InstallRoot $cleanupFailureInstallRoot `
                -WarningAction Stop 3>&1
        )
    }
    finally {
        $WarningPreference = $savedWarningPreference
        Microsoft.PowerShell.Management\Remove-Item `
            -LiteralPath Function:\Remove-Item `
            -Force
    }

    Assert-ReplacementInstallationActive `
        -InstallRoot $cleanupFailureInstallRoot `
        -Context 'post-commit cleanup failure'
    $retainedBackups = @(
        Get-ChildItem -LiteralPath $cleanupFailureRoot -Directory -Force |
            Where-Object Name -Like '.install.backup.*'
    )
    Assert-Equal `
        -Expected 1 `
        -Actual $retainedBackups.Count `
        -Message 'Post-commit cleanup failure did not retain the undeleted backup path.'
    $cleanupWarningText = ($cleanupWarnings | ForEach-Object ToString) -join "`n"
    Assert-True (
        $cleanupWarningText.Contains(
            $retainedBackups[0].FullName,
            [System.StringComparison]::Ordinal
        )
    ) 'Post-commit cleanup warning did not report the retained backup path.'
    Assert-True (
        $cleanupWarningText.Contains(
            'must not be treated as a fully recoverable backup',
            [System.StringComparison]::Ordinal
        )
    ) 'Post-commit cleanup warning incorrectly implied the partial backup is recoverable.'

    Invoke-Installer `
        -BundleRoot $bundleRoot `
        -InstallRoot $cleanupFailureInstallRoot
    Assert-ReplacementInstallationActive `
        -InstallRoot $cleanupFailureInstallRoot `
        -Context 'rerun after post-commit cleanup failure'
    Assert-True (
        Test-Path -LiteralPath $retainedBackups[0].FullName -PathType Container
    ) 'Installer rerun unexpectedly removed a previously retained partial backup.'

    if (-not [string]::IsNullOrWhiteSpace($BundlePath)) {
        Invoke-ActualUninstallRegression -PackagePath $BundlePath
    }

    Write-Output 'All Install-DeploymentValidationBundle regression tests passed.'
}
finally {
    if (Test-Path -LiteralPath Function:\Set-Content) {
        Microsoft.PowerShell.Management\Remove-Item `
            -LiteralPath Function:\Set-Content `
            -Force
    }
    if (Test-Path -LiteralPath Function:\Move-Item) {
        Microsoft.PowerShell.Management\Remove-Item `
            -LiteralPath Function:\Move-Item `
            -Force
    }
    if (Test-Path -LiteralPath Function:\Remove-Item) {
        Microsoft.PowerShell.Management\Remove-Item `
            -LiteralPath Function:\Remove-Item `
            -Force
    }
    if (Test-Path -LiteralPath $testRoot) {
        Microsoft.PowerShell.Management\Remove-Item `
            -LiteralPath $testRoot `
            -Recurse `
            -Force
    }
    if ((Test-Path -LiteralPath $testBase) -and
        @(Get-ChildItem -LiteralPath $testBase -Force).Count -eq 0) {
        Microsoft.PowerShell.Management\Remove-Item -LiteralPath $testBase -Force
    }
}
