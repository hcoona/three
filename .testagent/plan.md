# Plan: final Python package regressions

## Phase 1: optional keyring compatibility

Create
`src/private/app/azureauth-credprovider/python/tests/test_final_package_regressions.py`
and update existing subprocess assertions for byte capture.

- Prove that a genuinely absent top-level `keyring` selects the fallback.
- Prove that nested `ImportError` and `ModuleNotFoundError` failures propagate.
- With the workspace's installed keyring, make the backend return a concrete
  public `keyring.credentials.SimpleCredential` implementing
  `Credential`.
- Assert that package source does not add keyring's private `_vars` protocol.
- Send the backend result through keyring's command path and assert exact plain
  and JSON output.

Checklist coverage: 1-6.

## Phase 2: real wheel and process boundaries

- Build the actual wheel once in a session fixture.
- Load the wheel's `keyring.backends` entry point from its real dist-info.
- Start a `python -I` subprocess in a fresh no-pip virtual environment, add
  only the wheel to `sys.path`, prove top-level keyring is absent, and assert
  the wheel uses its local fallback credential.
- Install only the local wheel into a temporary virtual environment and invoke
  the generated `azureauth-keyring` binary for exact plain and JSON output
  against a local deterministic helper executable.

Checklist coverage: 7-9 and 15.

## Phase 3: helper byte protocol and redaction

- Mock the validated helper and subprocess boundary.
- Require `capture_output=True`, no `text`/`encoding`, and byte stdout/stderr.
- Assert explicit strict UTF-8 decoding produces exact credential fields.
- Feed invalid UTF-8 on successful stdout and failing stderr (including a
  secret sentinel); require fixed `HelperProtocolError` messages and verify
  raw bytes, sentinels, and low-level exception details are absent.
- Preserve exact valid failure stderr/exit behavior.
- Preserve no-credential behavior even when process streams are unusable.

Checklist coverage: 10-14.

## Phase 4: sequential validation and review

1. Run the new regression file only.
2. Implement the scoped backend and helper fixes.
3. Run Ruff against the new test.
4. Run the complete Python package test directory.
5. Invoke `test-gap-analysis` and `assertion-quality`; if either cannot run,
   perform the equivalent inline mutation/assertion review.
6. Record commands, exact outcomes, mapped test names, and production-fix
   blockers in `.testagent/status.md`.
7. Verify the diff contains only scoped production, test, and `.testagent`
   changes, then commit with the required trailers.

Checklist coverage: 16-17 and final audit of 1-15.
