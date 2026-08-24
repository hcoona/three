param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet("rectify_pages.py", "tests/test_rectify_pages.py")]
    [string]$Script,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ScriptArgs
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$pythonVersion = "3.12.10"
$pythonSpec = "python@$pythonVersion"
$miseVersion = "2026.8.8"
$scriptsRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$skillRoot = Split-Path -Parent $scriptsRoot
$scriptPath = if ($Script -ieq "rectify_pages.py") {
    Join-Path $scriptsRoot "rectify_pages.py"
}
else {
    Join-Path $skillRoot "tests\test_rectify_pages.py"
}
$requirementsPath = Join-Path $scriptsRoot "requirements.lock"
$sessionRoot = Join-Path $scriptsRoot (
    ".session-{0}-{1}" -f $PID, [Guid]::NewGuid().ToString("N")
)
$runtimeRoot = Join-Path $sessionRoot "runtime"
$python = Join-Path $runtimeRoot "Scripts\python.exe"
$savedEnvironment = @{}
$exitCode = 1

function Initialize-RunnerEnvironmentVariable {
    param([Parameter(Mandatory = $true)][string]$Name, [AllowNull()][string]$Value)

    if (-not $savedEnvironment.ContainsKey($Name)) {
        $savedEnvironment[$Name] = [Environment]::GetEnvironmentVariable(
            $Name, [EnvironmentVariableTarget]::Process
        )
    }
    [Environment]::SetEnvironmentVariable(
        $Name, $Value, [EnvironmentVariableTarget]::Process
    )
}

function Restore-RunnerEnvironment {
    foreach ($entry in $savedEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable(
            $entry.Key, $entry.Value, [EnvironmentVariableTarget]::Process
        )
    }
}

function Invoke-Mise {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "mise failed: $([string]::Join(' ', $Arguments))"
    }
}

try {
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw "The requested script is missing."
    }
    if (-not (Test-Path -LiteralPath $requirementsPath -PathType Leaf)) {
        throw "requirements.lock is missing."
    }
    $miseCommand = Get-Command mise.exe -CommandType Application `
        -ErrorAction Stop | Select-Object -First 1
    $mise = $miseCommand.Source

    Get-ChildItem Env: | Where-Object {
        $_.Name -like "MISE_*" -or
        $_.Name -like "PIP_*" -or
        $_.Name -like "PYTHON*"
    } | ForEach-Object {
        Initialize-RunnerEnvironmentVariable -Name $_.Name -Value $null
    }

    $miseRoots = @{
        MISE_CONFIG_FILE        = "NUL"
        MISE_GLOBAL_CONFIG_FILE = "NUL"
        MISE_SYSTEM_CONFIG_FILE = "NUL"
        MISE_CONFIG_DIR         = (Join-Path $sessionRoot "config")
        MISE_DATA_DIR           = (Join-Path $sessionRoot "data")
        MISE_INSTALLS_DIR       = (Join-Path $sessionRoot "installs")
        MISE_CACHE_DIR          = (Join-Path $sessionRoot "cache")
        MISE_STATE_DIR          = (Join-Path $sessionRoot "state")
        MISE_DOWNLOADS_DIR      = (Join-Path $sessionRoot "downloads")
        MISE_SHIMS_DIR          = (Join-Path $sessionRoot "shims")
        MISE_YES                = "1"
    }
    foreach ($entry in $miseRoots.GetEnumerator()) {
        Initialize-RunnerEnvironmentVariable -Name $entry.Key -Value $entry.Value
    }
    Initialize-RunnerEnvironmentVariable -Name "PIP_CONFIG_FILE" -Value "NUL"
    Initialize-RunnerEnvironmentVariable `
        -Name "PIP_DISABLE_PIP_VERSION_CHECK" -Value "1"
    Initialize-RunnerEnvironmentVariable -Name "PIP_NO_INPUT" -Value "1"
    Initialize-RunnerEnvironmentVariable -Name "PYTHONNOUSERSITE" -Value "1"
    New-Item -ItemType Directory -Path $sessionRoot | Out-Null

    $reportedMiseVersion = (& $mise --version | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or
        $reportedMiseVersion -notmatch (
            "^{0}(?:\s|$)" -f [regex]::Escape($miseVersion)
        )) {
        throw "mise must be exact version $miseVersion."
    }
    Invoke-Mise $mise @("--no-config", "--quiet", "install", $pythonSpec)
    $basePython = (& $mise --no-config --quiet which python `
            "--tool=$pythonSpec" | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-Path -LiteralPath $basePython -PathType Leaf)) {
        throw "mise could not resolve Python $pythonVersion."
    }
    $reportedPythonVersion = (& $basePython -I -B -c `
            "import platform; print(platform.python_version(), end='')" |
            Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $reportedPythonVersion -cne $pythonVersion) {
        throw "mise did not provide exact Python $pythonVersion."
    }

    & $basePython -I -B -m venv $runtimeRoot
    if ($LASTEXITCODE -ne 0 -or
        -not (Test-Path -LiteralPath $python -PathType Leaf)) {
        throw "Could not create the ephemeral Python runtime."
    }
    & $python -I -B -m pip install --quiet --disable-pip-version-check `
        --no-input --no-cache-dir --require-hashes --only-binary=:all: `
        --no-deps --requirement $requirementsPath
    if ($LASTEXITCODE -ne 0) {
        throw "pip could not install the hash-locked PyPI dependencies."
    }

    & $python -I -B -m pip check
    if ($LASTEXITCODE -ne 0) {
        throw "The ephemeral runtime failed dependency validation."
    }
    $versionCheck = @'
import cv2, importlib.metadata as m, numpy, PIL, platform, sys
sys.exit(
    platform.python_version() != '3.12.10'
    or numpy.__version__ != '2.2.6'
    or cv2.__version__ != '4.12.0'
    or PIL.__version__ != '12.3.0'
    or m.version('opencv-python-headless') != '4.12.0.88'
)
'@
    & $python -I -B -c $versionCheck
    if ($LASTEXITCODE -ne 0) {
        throw "The ephemeral runtime does not contain the pinned dependencies."
    }

    & $python -I -B $scriptPath @ScriptArgs
    $exitCode = $LASTEXITCODE
}
catch {
    Write-Error $_
    $exitCode = 1
}
finally {
    Restore-RunnerEnvironment
    try {
        if (Test-Path -LiteralPath $sessionRoot) {
            Remove-Item -LiteralPath $sessionRoot -Recurse -Force
        }
    }
    catch {
        Write-Error "Could not remove the ephemeral runtime: $_"
        $exitCode = 1
    }
}
exit $exitCode
