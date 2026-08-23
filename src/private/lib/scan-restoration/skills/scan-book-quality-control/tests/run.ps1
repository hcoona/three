$ErrorActionPreference = "Stop"

& mise exec python@3.12.11 -- python `
(Join-Path $PSScriptRoot "test_validate_book.py") "-v"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
