param(
    [string]$Configuration = 'Release',
    [Parameter(Mandatory = $true)][string]$PublishOutputRoot,
    [Parameter(Mandatory = $true)][string]$InstallerOutputPath,
    [string]$InstallerFileName = 'hcoona-release-smoke-inno-setup.exe',
    [string]$InnoSetupCompiler
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

function Get-ISCCPath {
    param([string]$Hint)
    if ($Hint) {
        if (Test-Path -LiteralPath $Hint -PathType Leaf) {
            return (Resolve-Path -LiteralPath $Hint).Path
        }
        throw "Inno Setup compiler not found at: $Hint"
    }

    $command = Get-Command -Name 'iscc', 'iscc.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) {
        return $command.Source
    }

    $candidates = @(
        "$($env:LOCALAPPDATA)\Programs\Inno Setup 6\ISCC.exe",
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    throw 'Inno Setup compiler (ISCC.exe) not found. Install Inno Setup 6 or pass -InnoSetupCompiler.'
}

$setupScript = Join-Path $PSScriptRoot 'Setup.iss'
if (-not (Test-Path -LiteralPath $setupScript -PathType Leaf)) {
    throw "Inno Setup script not found: $setupScript"
}

$publishOutput = (Resolve-Path -LiteralPath $PublishOutputRoot).Path
$publishedExe = Join-Path $publishOutput 'hcoona-release-smoke-inno.exe'
if (-not (Test-Path -LiteralPath $publishedExe -PathType Leaf)) {
    throw "Published smoke executable not found: $publishedExe"
}

New-Item -ItemType Directory -Force -Path $InstallerOutputPath | Out-Null
$installerOutput = (Resolve-Path -LiteralPath $InstallerOutputPath).Path
$compiler = Get-ISCCPath -Hint $InnoSetupCompiler
$outputBaseName = [System.IO.Path]::GetFileNameWithoutExtension($InstallerFileName)
if ([string]::IsNullOrWhiteSpace($outputBaseName)) {
    throw "Installer file name must include a non-empty base name: $InstallerFileName"
}

if ($publishOutput.EndsWith('\')) {
    $publishOutput = $publishOutput.TrimEnd('\')
}

& $compiler $setupScript "/O$installerOutput" "/F$outputBaseName" "/DPublishDir=$publishOutput"

$setupPath = Join-Path $installerOutput $InstallerFileName
if (-not (Test-Path -LiteralPath $setupPath -PathType Leaf)) {
    throw "Inno Setup completed but expected installer was not found: $setupPath"
}
