# Research: final Python package regressions

## Scope and authority

- Worktree: `/home/shuaizhang/.copilot/session-state/95cabf48-3a10-4649-a1ed-355a4dc6580c/files/worktrees/final-python-package`
- Branch: `copilot/fix-final-python-package`
- Test target: `src/private/app/azureauth-credprovider/python`
- The current working tree is authoritative. Production changes are limited to
  the keyring backend and helper subprocess boundary. Dependency manifests and
  lock files remain unchanged.
- `code-testing-extensions` is unavailable by explicit instruction. Python
  conventions were derived from the repository tests and
  `unit-test-generation.prompt.md`.

## Bounded target inventory

| Source / manifest | Relevant behavior |
| --- | --- |
| `python/src/azureauth_credprovider_keyring/backend.py` | Optional keyring import, fallback classes, `get_credential` result type |
| `python/src/azureauth_credprovider_keyring/helper.py` | Subprocess stream capture/decoding, protocol errors, exit handling |
| `python/src/azureauth_credprovider_keyring/shim.py` | Plain/JSON formatting and controlled error output |
| `python/src/azureauth_credprovider_keyring/contracts.py` | Fixed error classes, exit codes, credential contract |
| `python/pyproject.toml` | Wheel entry points, console script, intentionally empty runtime dependency list |
| `python/tests/test_backend_and_shim.py` | Canonical pytest style and existing helper/shim coverage |
| `python/tests/test_package_metadata.py` | Canonical real-wheel build fixture pattern |

No unrelated source is in scope.

## Existing conventions

- Pytest function tests with descriptive `test_*` names and short docstrings.
- Exact assertions on return codes, stdout, stderr, credentials, subprocess
  keyword arguments, and side effects.
- `tmp_path`, `monkeypatch`, parameterization, `subprocess.run`, and real local
  wheel builds are established patterns.
- Tests use `# ruff: noqa: S101`; subprocess calls use targeted `# noqa: S603`.
- Package command: `uv run --all-packages pytest ...` when the locked workspace
  `keyring` installation is required.
- Build command: `uv build --package azureauth-credprovider-keyring --wheel`.
- Installed keyring 25.6.0 exposes public
  `keyring.credentials.Credential`/`SimpleCredential`; its public CLI supports
  `--mode=creds` with `--output=plain|json`.

## Current behavior and regression seams

- `backend.get_credential` always returns the package-local
  `_SimpleCredential`, even when keyring is installed.
- `_load_keyring_backend` catches every `ImportError`, so an ImportError raised
  by a nested dependency is mistaken for an absent top-level keyring.
- The local credential deliberately has no private `_vars` formatter method.
  Compatibility must come from returning keyring's public credential class,
  not by reproducing keyring internals.
- `helper.invoke_helper` currently requests text mode directly from
  `subprocess.run`; this delegates decoding and can expose low-level decode
  failures instead of fixed protocol errors.
- Failure stderr currently becomes the execution error message verbatim.
- The wheel declares no runtime dependencies. Tests must establish optional
  behavior without changing that policy.

## Acceptance checklist and planned evidence

1. Installed keyring credential compatibility:
   `test_backend_get_credential_returns_public_keyring_simple_credential`.
2. Local fallback only for absent top-level keyring:
   `test_load_keyring_backend_falls_back_only_for_absent_top_level_keyring`
   and `test_load_keyring_backend_rejects_broken_installed_module`
   and `test_built_wheel_uses_local_credential_in_isolated_no_keyring_runtime`.
3. Nested ImportError propagation:
   `test_load_keyring_backend_propagates_nested_import_error` and
   `test_load_keyring_backend_propagates_nested_module_not_found_error`.
4. No private `_vars` duck typing:
   exact public class/module assertions in
   `test_backend_get_credential_returns_public_keyring_simple_credential`;
   source assertion in `test_backend_does_not_implement_private_keyring_vars_protocol`.
5. Installed `Credential`/`SimpleCredential`:
   public `isinstance` and exact-type assertions in the installed-keyring test.
6. Keyring CLI plain and JSON:
   `test_keyring_cli_formats_backend_credential_via_public_command_path`
   parameterized for both formats.
7. Actual built-wheel entry-point loading:
   `test_built_wheel_keyring_backend_entry_point_loads`.
8. Isolated no-keyring fallback:
   `test_built_wheel_uses_local_credential_in_isolated_no_keyring_runtime`.
9. Dependency policy unchanged:
   no manifest edit; final scoped diff records this evidence.
10. Binary capture and strict UTF-8 decoding:
    `test_invoke_helper_captures_bytes_and_strictly_decodes_utf8`.
11. Invalid success stdout redaction:
    `test_invalid_success_stdout_raises_redacted_helper_protocol_error`.
12. Invalid failure stderr redaction:
    `test_invalid_failure_stderr_raises_redacted_helper_protocol_error`.
13. Valid failure unchanged:
    `test_invoke_helper_preserves_valid_failure_stderr_and_exit_code`.
14. No-credential unchanged:
    `test_invoke_helper_preserves_no_credential_exit_behavior`.
15. Binary invocation:
    `test_built_wheel_console_script_invocation` for plain and JSON output.
16. Scoped implementation:
    final `git status`, `git diff --name-only`, and committed branch evidence.
17. Narrow tests:
    run only the new regression file first, then the two package test files as
    scoped validation. Expected production-regression failures are recorded
    without weakening tests.

## Exact validation commands

```text
uv run --frozen --all-packages pytest -q src/private/app/azureauth-credprovider/python/tests/test_final_package_regressions.py
uv run --frozen --all-packages pytest -q
uv run --frozen --all-packages ruff check src/private/app/azureauth-credprovider/python
uv run --frozen --all-packages pyrefly check <changed-source-files>
```
