param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet("analyze_scans.py", "run_tests.py", "check_runtime.py")]
    [string]$Script,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ScriptArgs
)

$ErrorActionPreference = "Stop"
$scriptsRoot = $PSScriptRoot
$pythonVersion = "3.12.13"
$pythonSpec = "python@$pythonVersion"
$requirements = Join-Path $scriptsRoot "requirements.lock"
$runtime = Join-Path $scriptsRoot (".runtime-" + [Guid]::NewGuid().ToString("N"))
$miseSession = Join-Path $scriptsRoot (
    ".mise-session-" + [Guid]::NewGuid().ToString("N")
)
$python = Join-Path $runtime "Scripts\python.exe"
$scriptPath = Join-Path $scriptsRoot $Script
$savedEnvironment = @{}

function Save-And-ClearEnvironment {
    param([Parameter(Mandatory = $true)][scriptblock]$Predicate)

    Get-ChildItem Env: | Where-Object $Predicate | ForEach-Object {
        if (-not $savedEnvironment.ContainsKey($_.Name)) {
            $savedEnvironment[$_.Name] = $_.Value
        }
        Remove-Item "Env:$($_.Name)" -ErrorAction SilentlyContinue
    }
}

function Restore-Environment {
    Get-ChildItem Env: | Where-Object {
        $_.Name -like "MISE_*" -or
        $_.Name -like "PIP_*" -or
        $_.Name -like "PYTHON*"
    } | ForEach-Object {
        Remove-Item "Env:$($_.Name)" -ErrorAction SilentlyContinue
    }
    foreach ($entry in $savedEnvironment.GetEnumerator()) {
        [Environment]::SetEnvironmentVariable(
            $entry.Key,
            $entry.Value,
            [EnvironmentVariableTarget]::Process
        )
    }
}

try {
    New-Item -ItemType Directory -Path $miseSession | Out-Null

    Save-And-ClearEnvironment { $_.Name -like "MISE_*" }
    Save-And-ClearEnvironment { $_.Name -like "PIP_*" }
    Save-And-ClearEnvironment { $_.Name -like "PYTHON*" }

    $env:MISE_NO_CONFIG = "1"
    $env:MISE_CONFIG_DIR = Join-Path $miseSession "config"
    $env:MISE_STATE_DIR = Join-Path $miseSession "state"
    $env:PYTHONNOUSERSITE = "1"
    $env:PIP_CONFIG_FILE = "nul"

    $miseCommand = Get-Command mise -CommandType Application -ErrorAction Stop
    $miseExe = $miseCommand.Source
    & $miseExe --no-config --quiet install $pythonSpec
    if ($LASTEXITCODE -ne 0) {
        throw "mise could not install Python $pythonVersion."
    }

    $misePython = [string]::Join(
        "",
        @(& $miseExe --no-config --quiet which python "--tool=$pythonSpec")
    ).Trim()
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $misePython -PathType Leaf)) {
        throw "mise could not resolve Python $pythonVersion."
    }
    $resolvedVersion = [string]::Join(
        "",
        @(& $misePython -I -B -c "import platform; print(platform.python_version(), end='')")
    )
    if ($LASTEXITCODE -ne 0 -or $resolvedVersion -cne $pythonVersion) {
        throw "mise resolved Python $resolvedVersion; required $pythonVersion."
    }

    & $misePython -I -B -m venv $runtime
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $python -PathType Leaf)) {
        throw "Could not create the ephemeral Python runtime."
    }

    $null = & $python -I -B -m pip install --quiet `
        --disable-pip-version-check `
        --no-input `
        --no-cache-dir `
        --no-deps `
        --only-binary=:all: `
        --require-hashes `
        --no-compile `
        --requirement $requirements 2>&1
    $pipExitCode = $LASTEXITCODE
    if ($pipExitCode -ne 0) {
        throw "pip could not install the hash-locked PyPI dependencies."
    }

    & $python -I -B (Join-Path $scriptsRoot "check_runtime.py") `
        --runtime $runtime | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The ephemeral runtime failed exact version and origin checks."
    }

    $savedPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & $python -I -B $scriptPath @ScriptArgs
        $scriptExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $savedPreference
    }
}
finally {
    if (Test-Path -LiteralPath $runtime) {
        Remove-Item -LiteralPath $runtime -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $miseSession) {
        Remove-Item -LiteralPath $miseSession -Recurse -Force -ErrorAction SilentlyContinue
    }
    Restore-Environment
}

exit $scriptExitCode
