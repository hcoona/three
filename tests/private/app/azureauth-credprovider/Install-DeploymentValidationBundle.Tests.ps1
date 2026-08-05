#Requires -Version 7.0
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$installerSource = Join-Path $repoRoot 'eng/scripts/azureauth-credprovider/Install-DeploymentValidationBundle.ps1'
$testBase = Join-Path $repoRoot 'artifacts/azureauth-credprovider/deployment-installer-tests'
$testRoot = Join-Path $testBase ([System.Guid]::NewGuid().ToString('N'))
$runningOnWindows = $IsWindows
$buildOs = if ($runningOnWindows) { 'Windows' } else { 'Linux' }
$targetRid = if ($runningOnWindows) { 'win-x64' } else { 'linux-x64' }
$productExecutableName = if ($runningOnWindows) {
    'azureauth-credprovider.exe'
}
else {
    'azureauth-credprovider'
}

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

try {
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
        Remove-Item -LiteralPath Function:\global:Set-Content -Force
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
            $Destination -ceq $failureDestination) {
            throw 'Injected deterministic plugin activation failure.'
        }
        Microsoft.PowerShell.Management\Move-Item @PSBoundParameters
    }
    Set-Item `
        -LiteralPath Function:\global:Move-Item `
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
        Remove-Item -LiteralPath Function:\global:Move-Item -Force
    }
    Assert-PreviousInstallationRestored `
        -InstallRoot $switchFailureInstallRoot `
        -NuGetPluginRoot $switchFailurePluginRoot `
        -Context 'plugin activation failure'

    Write-Output 'All Install-DeploymentValidationBundle regression tests passed.'
}
finally {
    if (Test-Path -LiteralPath Function:\global:Set-Content) {
        Remove-Item -LiteralPath Function:\global:Set-Content -Force
    }
    if (Test-Path -LiteralPath Function:\global:Move-Item) {
        Remove-Item -LiteralPath Function:\global:Move-Item -Force
    }
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
    if ((Test-Path -LiteralPath $testBase) -and
        @(Get-ChildItem -LiteralPath $testBase -Force).Count -eq 0) {
        Remove-Item -LiteralPath $testBase -Force
    }
}
