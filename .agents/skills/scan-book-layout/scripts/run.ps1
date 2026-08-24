param(
    [Parameter(Position = 0, Mandatory = $true)]
    [string]$Script,
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$ScriptArgs
)

$ErrorActionPreference = "Stop"

$skillRoot = Split-Path -Parent $PSScriptRoot
if ($Script -ceq "normalize_book.py") {
    $program = Join-Path $PSScriptRoot "normalize_book.py"
}
elseif ($Script -ceq "tests/test_normalize_book.py") {
    $program = Join-Path $skillRoot "tests\test_normalize_book.py"
}
else {
    throw (
        "The runner accepts only normalize_book.py or " +
        "tests/test_normalize_book.py."
    )
}

$pythonVersion = "3.12.10"
$requirements = Join-Path $PSScriptRoot "requirements.lock"
$sessionRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) (".scan-book-layout-" + [Guid]::NewGuid().ToString("N"))
$runtime = Join-Path $sessionRoot "runtime"
$python = Join-Path $runtime "Scripts\python.exe"

$savedEnvironment = @{}
Get-ChildItem Env: | Where-Object {
    $_.Name.StartsWith("MISE_", [StringComparison]::OrdinalIgnoreCase) -or
    $_.Name -match '^(PYTHON|PIP|VIRTUAL_ENV$|CONDA|MAMBA|__PYVENV_LAUNCHER__$)'
} | ForEach-Object {
    $savedEnvironment[$_.Name] = $_.Value
    Remove-Item -LiteralPath "Env:$($_.Name)"
}

$result = 1
try {
    if (-not (Test-Path -LiteralPath $requirements -PathType Leaf) -or
        -not (Test-Path -LiteralPath $program -PathType Leaf)) {
        throw "The pinned dependency lock or requested script is missing."
    }

    $miseCommand = Get-Command mise -CommandType Application -ErrorAction Stop
    $mise = $miseCommand.Source

    New-Item -ItemType Directory -Path $sessionRoot | Out-Null
    $env:MISE_NO_CONFIG = "1"
    $env:MISE_CONFIG_DIR = Join-Path $sessionRoot "config"
    $env:MISE_DATA_DIR = Join-Path $sessionRoot "data"
    $env:MISE_INSTALLS_DIR = Join-Path $sessionRoot "installs"
    $env:MISE_CACHE_DIR = Join-Path $sessionRoot "cache"
    $env:MISE_STATE_DIR = Join-Path $sessionRoot "state"
    $env:MISE_DOWNLOADS_DIR = Join-Path $sessionRoot "downloads"
    $env:MISE_SHIMS_DIR = Join-Path $sessionRoot "shims"
    $env:PIP_CONFIG_FILE = "nul"
    $env:PYTHONDONTWRITEBYTECODE = "1"
    $env:PYTHONNOUSERSITE = "1"

    & $mise --no-config --quiet install "python@$pythonVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "mise could not install Python $pythonVersion."
    }
    & $mise --no-config exec "python@$pythonVersion" -- python -I -B -m venv $runtime
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $python -PathType Leaf)) {
        throw "mise could not create the ephemeral Python runtime."
    }

    & $python -I -B -m pip install --quiet --disable-pip-version-check `
        --no-input --no-cache-dir --no-deps --only-binary=:all: `
        --require-hashes --requirement $requirements
    if ($LASTEXITCODE -ne 0) {
        throw "pip could not install the exact pinned PyPI dependencies."
    }

    & $python -I -B $program @ScriptArgs
    $result = $LASTEXITCODE
}
finally {
    if (Test-Path -LiteralPath $sessionRoot) {
        Remove-Item -LiteralPath $sessionRoot -Recurse -Force
    }
    Get-ChildItem Env: | Where-Object {
        $_.Name.StartsWith("MISE_", [StringComparison]::OrdinalIgnoreCase) -or
        $_.Name -match '^(PYTHON|PIP|VIRTUAL_ENV$|CONDA|MAMBA|__PYVENV_LAUNCHER__$)'
    } | ForEach-Object {
        Remove-Item -LiteralPath "Env:$($_.Name)" -ErrorAction SilentlyContinue
    }
    foreach ($entry in $savedEnvironment.GetEnumerator()) {
        Set-Item -LiteralPath "Env:$($entry.Key)" -Value $entry.Value
    }
}

exit $result
