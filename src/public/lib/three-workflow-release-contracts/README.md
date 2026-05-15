# three-workflow-release-contracts

Frozen JSON contract models and validation helpers for the Three monorepo
workflow-release design. This package intentionally validates only code-level
contract shapes; it does not implement live GitHub workflows, planners, build
executors, or publish adapters.

## CI affected-validation foundation

The `three_workflow_release_contracts.ci_validation` module contains shared
foundation data for future CI affected-validation implementation work:

- closed API version and `kind` vocabulary for CI validation artifacts;
- closed diagnostic code/detail/severity/verdict-effect vocabulary;
- common-envelope validation for `api-version`, `kind`, repository, run, and
  RFC 3339 `created-at` values, plus schema diagnostics;
- logical artifact-ref to physical GitHub artifact name mapping using
  `three-ci-validation-` plus lowercase SHA-256 of the UTF-8 logical ref;
- canonical JSON bytes and SHA-256 digest helpers.

The canonical JSON helper is intentionally scoped to the contract's I-JSON data
model. It orders object members by RFC 8785 UTF-16 code-unit order and emits
UTF-8 JSON without insignificant whitespace, but rejects floats instead of
pretending to implement full RFC 8785 number serialization.
