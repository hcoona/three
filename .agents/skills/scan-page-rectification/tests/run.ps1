$ErrorActionPreference = "Stop"

$skillRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "test_runner.ps1")
& (Join-Path $skillRoot "scripts\run.ps1") `
    "tests/test_rectify_pages.py" "-v"
