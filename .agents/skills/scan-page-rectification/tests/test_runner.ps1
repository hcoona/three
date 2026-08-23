$ErrorActionPreference = "Stop"
$skillRoot = Split-Path -Parent $PSScriptRoot
$scripts = Join-Path $skillRoot "scripts"
$runner = Join-Path $scripts "run.ps1"
$runnerText = Get-Content -LiteralPath $runner -Raw

$tokens = $null
$errors = $null
[Management.Automation.Language.Parser]::ParseFile(
    $runner, [ref]$tokens, [ref]$errors
) | Out-Null
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
        "azureauth-0.9.5.manifest.json",
        "trusted-scripts.manifest.json",
        "update-python-runtime-pin.ps1"
    )) {
    if (Test-Path -LiteralPath (Join-Path $scripts $removed)) {
        throw "Removed hardening artifact still exists: $removed"
    }
}

foreach ($required in @(
        '$pythonVersion = "3.12.10"',
        '$miseVersion = "2026.8.8"',
        "MISE_INSTALLS_DIR",
        "--require-hashes --only-binary=:all: --no-deps",
        "ado token --output token",
        "-NoProfile -NonInteractive -EncodedCommand",
        'Remove-Item Env:PIP_INDEX_URL',
        "Remove-Item -LiteralPath `$sessionRoot -Recurse -Force"
    )) {
    if (-not $runnerText.Contains($required)) {
        throw "Runner is missing required behavior: $required"
    }
}

if ($runnerText -notmatch 'MISE_CONFIG_FILE\s*=\s*"NUL"') {
    throw "Runner is missing the isolated mise configuration root."
}

if ($runnerText -match "AzureAuthSha256|runtimeManifest|FileShare|AccessControl") {
    throw "Runner still contains rolled-back hardening machinery."
}

$lock = Get-Content -LiteralPath (Join-Path $scripts "requirements.lock") -Raw
if (($lock | Select-String -Pattern "--hash=sha256:" -AllMatches).
    Matches.Count -ne 3) {
    throw "Every pinned dependency must have exactly one SHA-256 hash."
}
