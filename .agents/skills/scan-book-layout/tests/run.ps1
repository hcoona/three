$ErrorActionPreference = "Stop"

$skillRoot = Split-Path -Parent $PSScriptRoot
Push-Location $skillRoot
try {
    & (Join-Path $skillRoot "scripts\run.ps1") `
        "tests/test_normalize_book.py" "-v"
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
exit $exitCode
