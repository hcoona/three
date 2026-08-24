$ErrorActionPreference = "Stop"
$skillRoot = Split-Path -Parent $PSScriptRoot
$scripts = Join-Path $skillRoot "scripts"
$runner = Join-Path $scripts "run.ps1"
$runnerText = Get-Content -LiteralPath $runner -Raw
$lock = Get-Content -LiteralPath (
    Join-Path $scripts "requirements.lock"
) -Raw
$testEntryPoint = Join-Path $PSScriptRoot "run.ps1"
$testEntryPointText = Get-Content -LiteralPath $testEntryPoint -Raw

$tokens = $null
$errors = $null
$null = [Management.Automation.Language.Parser]::ParseFile(
    $runner, [ref]$tokens, [ref]$errors
)
if ($errors.Count -ne 0) {
    throw "run.ps1 does not parse: $($errors[0].Message)"
}

foreach ($removed in @(
        "run.cmd",
        "startup_launcher.exe",
        "startup_launcher.c",
        "invoke_trusted_launcher.ps1",
        "install-dependencies.ps1",
        "environment.ps1",
        "python-runtime-manifest.json",
        "trusted-scripts.manifest.json",
        "update-python-runtime-pin.ps1"
    )) {
    if (Test-Path -LiteralPath (Join-Path $scripts $removed)) {
        throw "Removed hardening artifact still exists: $removed"
    }
}

foreach ($required in @(
        '[ValidateSet("rectify_pages.py", "tests/test_rectify_pages.py")]',
        '$pythonVersion = "3.12.10"',
        '$miseVersion = "2026.8.8"',
        "MISE_INSTALLS_DIR",
        '$_.Name -like "PIP_*"',
        '$_.Name -like "PYTHON*"',
        'Initialize-RunnerEnvironmentVariable -Name "PIP_CONFIG_FILE"',
        "--no-input --no-cache-dir --require-hashes --only-binary=:all:",
        "--no-deps --requirement `$requirementsPath",
        "-m pip check",
        "Remove-Item -LiteralPath `$sessionRoot -Recurse -Force"
    )) {
    if (-not $runnerText.Contains($required)) {
        throw "Runner is missing required behavior: $required"
    }
}

foreach ($required in @(
        'Join-Path $PSScriptRoot "test_runner.ps1"',
        'Join-Path $skillRoot "scripts\run.ps1"',
        '"tests/test_rectify_pages.py" "-v"'
    )) {
    if (-not $testEntryPointText.Contains($required)) {
        throw "Test entrypoint is missing required behavior: $required"
    }
}

if ($runnerText -notmatch 'MISE_CONFIG_FILE\s*=\s*"NUL"') {
    throw "Runner is missing the isolated mise configuration root."
}
if ($runnerText -match "PIP_INDEX_URL|ado token|EncodedCommand|SCAN_RECTIFY_") {
    throw "Runner contains obsolete private-feed bootstrap behavior."
}
if (($lock | Select-String -Pattern "--hash=sha256:" -AllMatches).
    Matches.Count -ne 3) {
    throw "Every pinned dependency must have exactly one SHA-256 hash."
}
