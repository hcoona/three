# Scan Restoration Plugin

This private GitHub Copilot CLI plugin packages five Windows-focused skills for
diagnosing, restoring, rectifying, laying out, and validating scanned books.
The skills form one workflow while remaining independently invocable:

1. `scan-batch-diagnostics`
2. `scan-tone-restoration`
3. `scan-page-rectification`
4. `scan-book-layout`
5. `scan-book-quality-control`

The authoritative sources live under `skills/`. APM deploys runtime copies to
the repository's `.agents/skills/scan-*` directories. Edit the sources here,
then regenerate the deployed copies; do not edit `.agents/skills/scan-*`
directly.

## Supported environment

The plugin targets GitHub Copilot CLI on Windows only. Its runners require
PowerShell, mise, and network access to the public Python Package Index (PyPI).
VS Code, Linux execution, offline installation, and third-party package-index
substitution are outside the supported contract.

Install the source plugin locally during development:

```powershell
copilot plugin install .\src\private\lib\scan-restoration
```

Validate package structure and source hygiene through the repository toolchain:

```powershell
mise run scan-restoration-plugin-check
```

Deploy the package through the root APM project:

```powershell
mise run scan-restoration-plugin-install
mise run scan-restoration-plugin-install-frozen
```

These tasks validate the source allowlist before APM copies local skill trees.

## Tests

Run tests from each skill directory. The locked dependencies and Python
versions are intentionally skill-specific.

```powershell
# scan-batch-diagnostics
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1 run_tests.py

# scan-book-layout
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run.ps1

# scan-book-quality-control
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run.ps1

# scan-page-rectification
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run.ps1

# scan-tone-restoration
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\run.ps1
```

Some regression tests use private scan fixtures when available and otherwise
skip those cases. Set `SCAN_RESTORATION_FIXTURE_ROOT` to the directory that
contains the expected `input` and `traditional_harmony` fixture trees.

## License

This package has no project-specific license override, so the repository
default applies:
`LGPL-3.0-or-later WITH LGPL-3.0-linking-exception`.
