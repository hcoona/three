#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$BundlePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).ProviderPath
$installerSource = Join-Path $repoRoot 'eng/scripts/azureauth-credprovider/Install-DeploymentValidationBundle.ps1'
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
    Set-Content -LiteralPath (Join-Path $BundleRoot 'uninstall.ps1') -Value 'exit 0'
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
    param(
        [Parameter(Mandatory = $true)]
        [string]$OutputRoot
    )

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
        [string]$InstallRoot,

        [Parameter(Mandatory = $true)]
        [string]$NuGetPluginRoot
    )

    if (-not $PSCmdlet.ShouldProcess($InstallRoot, 'Create existing test installation')) {
        return
    }

    $applicationRoot = Join-Path $InstallRoot 'app'
    New-Item -ItemType Directory -Path $applicationRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $NuGetPluginRoot -Force | Out-Null
    Set-Content `
        -LiteralPath (Join-Path $applicationRoot 'previous-product.txt') `
        -Value 'previous product payload' `
        -NoNewline
    Set-Content `
        -LiteralPath (Join-Path $NuGetPluginRoot 'previous-plugin.txt') `
        -Value 'previous plugin payload' `
        -NoNewline

    $receipt = [ordered]@{
        schemaVersion   = 'azureauth-credprovider-deployment-validation-install-v1'
        productVersion  = '1.0.0-test'
        sourceRevision  = 'previous-revision'
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
        [string]$NuGetPluginRoot,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    Assert-Equal `
        -Expected 'previous product payload' `
        -Actual (Get-Content -LiteralPath (
            Join-Path $InstallRoot 'app/previous-product.txt'
        ) -Raw) `
        -Message "The previous product payload was not restored after $Context."
    Assert-Equal `
        -Expected 'previous plugin payload' `
        -Actual (Get-Content -LiteralPath (
            Join-Path $NuGetPluginRoot 'previous-plugin.txt'
        ) -Raw) `
        -Message "The previous plugin payload was not restored after $Context."

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
    Assert-True (
        -not (Test-Path -LiteralPath (
                Join-Path $NuGetPluginRoot 'azureauth-credprovider.dll'
            ))
    ) "The replacement plugin payload remains after $Context."

    foreach ($targetPath in @($InstallRoot, $NuGetPluginRoot)) {
        $trimmedPath = $targetPath.TrimEnd(
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
}

function Invoke-Installer {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BundleRoot,

        [Parameter(Mandatory = $true)]
        [string]$InstallRoot,

        [Parameter(Mandatory = $true)]
        [string]$NuGetPluginRoot
    )

    & (Join-Path $BundleRoot 'install.ps1') `
        -InstallRoot $InstallRoot `
        -NuGetPluginRoot $NuGetPluginRoot `
        -Force | Out-Null
}

function Assert-ReplacementInstallationActive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$InstallRoot,

        [Parameter(Mandatory = $true)]
        [string]$NuGetPluginRoot,

        [Parameter(Mandatory = $true)]
        [string]$Context
    )

    Assert-Equal `
        -Expected 'new executable payload' `
        -Actual (Get-Content -LiteralPath (
            Join-Path $InstallRoot "app/$productExecutableName"
        ) -Raw) `
        -Message "The replacement product payload is not active after $Context."
    Assert-Equal `
        -Expected 'new plugin payload' `
        -Actual (Get-Content -LiteralPath (
            Join-Path $NuGetPluginRoot 'azureauth-credprovider.dll'
        ) -Raw) `
        -Message "The replacement plugin payload is not active after $Context."

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
    $pluginRoot = Join-Path $actualRoot 'plugin'
    $isolatedHome = Join-Path $actualRoot 'home'
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
        & (Join-Path $extractedBundleRoot 'install.ps1') `
            -InstallRoot $installRoot `
            -NuGetPluginRoot $pluginRoot | Out-Null

        $productExecutablePath = Join-Path $installRoot "app/$productExecutableName"
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

        & (Join-Path $extractedBundleRoot 'uninstall.ps1') `
            -InstallRoot $installRoot `
            -NuGetPluginRoot $pluginRoot | Out-Null

        Assert-True (
            -not (Test-Path -LiteralPath $installRoot)
        ) 'Actual uninstall left the installed product payload.'
        Assert-True (
            -not (Test-Path -LiteralPath $pluginRoot)
        ) 'Actual uninstall left the NuGet plugin payload.'
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

    $receiptFailureRoot = Join-Path $testRoot 'receipt-failure'
    $receiptFailureInstallRoot = Join-Path $receiptFailureRoot 'install'
    $receiptFailurePluginRoot = Join-Path $receiptFailureRoot 'plugin'
    New-ExistingInstallation `
        -InstallRoot $receiptFailureInstallRoot `
        -NuGetPluginRoot $receiptFailurePluginRoot

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
                -InstallRoot $receiptFailureInstallRoot `
                -NuGetPluginRoot $receiptFailurePluginRoot
        } 'Expected the injected receipt write failure.'
    }
    finally {
        Microsoft.PowerShell.Management\Remove-Item `
            -LiteralPath Function:\Set-Content `
            -Force
    }
    Assert-PreviousInstallationRestored `
        -InstallRoot $receiptFailureInstallRoot `
        -NuGetPluginRoot $receiptFailurePluginRoot `
        -Context 'receipt staging failure'

    $switchFailureRoot = Join-Path $testRoot 'switch-failure'
    $switchFailureInstallRoot = Join-Path $switchFailureRoot 'install'
    $switchFailurePluginRoot = Join-Path $switchFailureRoot 'plugin'
    New-ExistingInstallation `
        -InstallRoot $switchFailureInstallRoot `
        -NuGetPluginRoot $switchFailurePluginRoot

    $failureDestination = $switchFailurePluginRoot
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
            throw 'Injected deterministic plugin activation failure.'
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
                -InstallRoot $switchFailureInstallRoot `
                -NuGetPluginRoot $switchFailurePluginRoot
        } 'Expected the injected plugin activation failure.'
    }
    finally {
        Microsoft.PowerShell.Management\Remove-Item `
            -LiteralPath Function:\Move-Item `
            -Force
    }
    Assert-PreviousInstallationRestored `
        -InstallRoot $switchFailureInstallRoot `
        -NuGetPluginRoot $switchFailurePluginRoot `
        -Context 'plugin activation failure'

    $cleanupFailureRoot = Join-Path $testRoot 'cleanup-failure'
    $cleanupFailureInstallRoot = Join-Path $cleanupFailureRoot 'install'
    $cleanupFailurePluginRoot = Join-Path $cleanupFailureRoot 'plugin'
    New-ExistingInstallation `
        -InstallRoot $cleanupFailureInstallRoot `
        -NuGetPluginRoot $cleanupFailurePluginRoot

    $global:DeploymentInstallerCleanupFailureInjected = $false
    function global:Remove-Item {
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
            if (-not $global:DeploymentInstallerCleanupFailureInjected -and
                $requestedPath -like '*.install.backup.*') {
                $global:DeploymentInstallerCleanupFailureInjected = $true
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
    try {
        $cleanupWarnings = @(
            Invoke-Installer `
                -BundleRoot $bundleRoot `
                -InstallRoot $cleanupFailureInstallRoot `
                -NuGetPluginRoot $cleanupFailurePluginRoot 3>&1
        )
    }
    finally {
        Microsoft.PowerShell.Management\Remove-Item `
            -LiteralPath Function:\Remove-Item `
            -Force
    }

    Assert-ReplacementInstallationActive `
        -InstallRoot $cleanupFailureInstallRoot `
        -NuGetPluginRoot $cleanupFailurePluginRoot `
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
        -InstallRoot $cleanupFailureInstallRoot `
        -NuGetPluginRoot $cleanupFailurePluginRoot
    Assert-ReplacementInstallationActive `
        -InstallRoot $cleanupFailureInstallRoot `
        -NuGetPluginRoot $cleanupFailurePluginRoot `
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
    Remove-Variable `
        -Name DeploymentInstallerCleanupFailureInjected `
        -Scope Global `
        -ErrorAction SilentlyContinue
}
