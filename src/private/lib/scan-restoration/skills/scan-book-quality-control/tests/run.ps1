$ErrorActionPreference = "Stop"

& mise --no-config exec python@3.12.11 -- python -I -B `
(Join-Path $PSScriptRoot "test_validate_book.py") "-v"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
