$ErrorActionPreference = "Stop"

$skillBase = Split-Path -Parent $PSScriptRoot
Push-Location $skillBase
try {
    & (Join-Path $skillBase "scripts\run.ps1") `
        "tests/test_validate_book.py" "-v"
    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
exit $exitCode
