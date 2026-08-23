$ErrorActionPreference = "Stop"
& mise --no-config exec python@3.12.10 -- python -I -B `
(Join-Path $PSScriptRoot "test_normalize_book.py") "-v"
exit $LASTEXITCODE
