$ErrorActionPreference = "Stop"
$skillRoot = Split-Path -Parent $PSScriptRoot
$scriptsRoot = Join-Path $skillRoot "scripts"
$runner = Join-Path $scriptsRoot "run.ps1"
$document = Join-Path $skillRoot "SKILL.md"
$testEntryPoint = Join-Path $PSScriptRoot "run.ps1"

$tokens = $null
$errors = $null
$null = [Management.Automation.Language.Parser]::ParseFile(
    $runner, [ref]$tokens, [ref]$errors
)
if ($errors.Count -ne 0) {
    throw "run.ps1 does not parse: $($errors[0].Message)"
}

$source = Get-Content -LiteralPath $runner -Raw
$docs = Get-Content -LiteralPath $document -Raw
$testEntryPointSource = Get-Content -LiteralPath $testEntryPoint -Raw

foreach ($required in @(
        '[ValidateSet("restore_tone.py", "tests/test_restore_tone.py")]',
        '$pythonVersion = "3.12.10"',
        '$imagecodecsVersion = "2026.6.26"',
        '$tifffileVersion = "2026.7.31"',
        '"PIP_CONFIG_FILE" "nul"',
        "--disable-pip-version-check",
        "--require-hashes",
        "--only-binary=:all:",
        "--no-cache-dir",
        "providers = metadata.packages_distributions().get(package, ())",
        "distribution.casefold() not in",
        '".runtime-" + [Guid]::NewGuid()',
        "Remove-Item -LiteralPath `$runtime -Recurse -Force",
        "& `$python -I -B `$scriptPath @ScriptArgs"
    )) {
    if (-not $source.Contains($required)) {
        throw "run.ps1 is missing practical runner behavior: $required"
    }
}

foreach ($required in @(
        'Join-Path $PSScriptRoot "test_runner.ps1"',
        'Join-Path $skillRoot "scripts\run.ps1"',
        '"tests/test_restore_tone.py" "-v"'
    )) {
    if (-not $testEntryPointSource.Contains($required)) {
        throw "Test entrypoint is missing required behavior: $required"
    }
}

if ($source -match "PIP_INDEX_URL|ado token|EscapeDataString") {
    throw "run.ps1 contains obsolete private-feed bootstrap behavior."
}
if (([Regex]::Matches(
            $docs,
            'powershell\.exe -NoProfile -File "\.\\scripts\\run\.ps1"'
        )).Count -ne 3) {
    throw "Every documented invocation must use run.ps1 with -NoProfile."
}
foreach ($removed in @(
        "invoke_trusted_launcher.ps1",
        "startup_launcher.c",
        "startup_launcher.exe",
        "startup_launcher.obj",
        "startup_launcher.pdb"
    )) {
    if (Test-Path -LiteralPath (Join-Path $scriptsRoot $removed)) {
        throw "Removed launcher artifact remains: $removed"
    }
}
