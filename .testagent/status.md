# Workflow Delivery v3 Snapshot Admission Status

## Phase Status

| Phase | Status |
|---|---|
| Research | Complete |
| Plan | Complete |
| Production implementation | Complete |
| R8 regression test | Complete |
| Final review regression | Complete |
| Narrow validation | Complete |
| Full package, managed HK, and root pytest validation | Complete |
| Pyrefly and Ruff validation | Complete |
| Builds and lock validation | Complete |
| Git diff/status validation | Complete |
| Pre-completion review | Complete |

## Files Modified for This Bounded Request

- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/repository/compiler.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/eligibility.py`
- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit3_contract_boundaries.py`
- `.testagent/research.md`
- `.testagent/plan.md`
- `.testagent/status.md`

Note: the working tree already contained unrelated modified files and the
intentionally untracked v3 package tree before this repair. They were preserved.

## Independent-Review Finding and Fix

The independent review found a surviving TOCTOU payload:
`validate_first_slice_repository_model_snapshot` compared
`snapshot.release_policy_path` with `FIRST_SLICE_POLICY_PATH` before requiring
the field's exact runtime type. A user-defined `__ne__` could therefore mutate
already-validated `release_units` to a digest-equivalent list and restore the
expected path before Live Eligibility compared the digest.

The repair closes that finding by validating `release_policy_path` with the
existing exact scalar style before the comparison. The new R8 regression proves
Live Eligibility rejects the surrogate before its comparison payload runs.

A final adversarial review identified missing negative coverage for the
top-level `RepositoryModelSnapshot` runtime type. The added four-case regression
now rejects Snapshot subclasses, duck objects, mappings, and lists at both
direct admission and Live Eligibility.

## Implementation Summary

- Added recursive exact tuple and exact frozen-record admission for every
  `RepositoryModelSnapshot` tuple/record position in the current commit-3
  model.
- Moved Live Eligibility Snapshot admission before Snapshot digest and
  readiness-dependent use.
- Added an exact nonempty `str` guard for
  `RepositoryModelSnapshot.release_policy_path` before its comparison with
  `FIRST_SLICE_POLICY_PATH`.
- Kept the existing R6 digest-equivalent list-backed Live Eligibility
  regression.
- Added the R8 adversarial TOCTOU regression
  `test_live_eligibility_blocks_toctou_mutation_during_snapshot_admission`.
- Updated `.testagent/research.md` and `.testagent/plan.md` to include
  `release_policy_path` in the bounded scalar closure and to map R8 to the new
  exact test.

## Generated Tests

| Test | Cases | Evidence |
|---|---:|---|
| `test_repository_model_admission_rejects_top_level_tuple_surrogates` | 4 | Rejects list and tuple-subclass substitutes for `project_nodes` and `release_units`. |
| `test_repository_model_snapshot_admission_rejects_nested_tuple_substitutions` | 24 | Rejects list and tuple-subclass substitutes for nested tuple fields, including dependencies, builds, outputs, native projections, quality capabilities, reverse-index facts, and unresolved facts. |
| `test_snapshot_admission_and_live_eligibility_reject_top_level_surrogates` | 4 | Rejects Snapshot subclasses, duck objects, mappings, and lists at direct admission and Live Eligibility. |
| `test_repository_model_snapshot_admission_rejects_record_surrogates` | 28 | Rejects subclasses, duck objects, mappings, and lists for `context`, `ProjectNode`, `CompiledReleaseUnit`, `CompiledBuild`, `CompiledOutput`, `CompiledQualitySelection`, and `NbgvFacts`. |
| `test_live_eligibility_validates_snapshot_admission_before_digest_use` | 1 | Proves admission runs before digest serialization by using a trap object that would fail if read for digesting. |
| `test_live_eligibility_rejects_digest_equivalent_list_backed_snapshot` | 1 | Preserves the R6 regression proving a list-backed mutation can keep the same JSON digest but cannot pass Live Eligibility admission. |
| `test_live_eligibility_blocks_toctou_mutation_during_snapshot_admission` | 1 | Proves the R8 side-effecting `release_policy_path` surrogate is rejected before equality comparison and before `release_units` mutation. |
| `test_repository_model_valid_tuples_keep_canonical_json_arrays` | 1 | Pins canonical JSON arrays and the existing valid Snapshot digest. |

Expected generated case count after the final review regression: 64.

## Requirement Coverage

| Requirement | Evidence |
|---|---|
| R1 exact immutable runtime closure | Recursive validators in `compiler.py`; the three negative matrices and the `release_policy_path` TOCTOU regression. |
| R2 exact top-level tuples | `test_repository_model_admission_rejects_top_level_tuple_surrogates` covers `project_nodes` and `release_units` with list and tuple-subclass substitutes. |
| R3 exact nested tuples | `test_repository_model_snapshot_admission_rejects_nested_tuple_substitutions` covers every tuple field in the current Snapshot model; no `variants` field exists in this commit-3 model. |
| R4 exact frozen record types | `test_snapshot_admission_and_live_eligibility_reject_top_level_surrogates` covers the Snapshot itself; `test_repository_model_snapshot_admission_rejects_record_surrogates` covers subclass, duck, mapping, and list surrogates for every nested Snapshot record position. |
| R5 admission before digest/readiness | `test_live_eligibility_validates_snapshot_admission_before_digest_use`; `_validate_live_context` validates before `snapshot.snapshot_digest`. |
| R6 mutated/list-backed Live Eligibility rejection | `test_live_eligibility_rejects_digest_equivalent_list_backed_snapshot`. |
| R7 comprehensive negative substitutions | Top-level Snapshot, top-level tuple, nested tuple, and nested record-surrogate matrices provide 60 negative cases. |
| R8 TOCTOU admission/digest boundary | `test_live_eligibility_blocks_toctou_mutation_during_snapshot_admission` starts with an admitted Snapshot and valid live-context digest, then proves a side-effecting surrogate cannot mutate `release_units` across admission. |
| R9 canonical JSON preservation | `test_repository_model_valid_tuples_keep_canonical_json_arrays` pins concrete JSON arrays and the unchanged digest. |
| R10 bounded commit-3 scope | Only the three production/test files listed above plus `.testagent` artifacts were changed for this request. |
| R11 `.testagent` artifacts | `research.md`, `plan.md`, and this status file. |
| R12 requested validation | Final commands and results are recorded below. |

## Validation Results

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit3_contract_boundaries.py -k 'repository_model_admission_rejects_top_level_tuple_surrogates or repository_model_snapshot_admission or snapshot_admission_and_live_eligibility_reject_top_level_surrogates or live_eligibility_validates_snapshot_admission_before_digest_use or live_eligibility_rejects_digest_equivalent_list_backed_snapshot or live_eligibility_blocks_toctou_mutation_during_snapshot_admission or repository_model_valid_tuples_keep_canonical_json_arrays'` | `64 passed, 84 deselected` |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit3_contract_boundaries.py` | `148 passed` |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests` | `1288 passed` |
| `mise exec -- hk --no-progress check --step v3-control-pytest --all` | Passed on isolated rerun; `1288 passed` |
| `uv run --python 3.13 pytest -q` | Passed; `3323 passed`; root configuration supplies importlib mode |
| `uv run --python 3.13 pyrefly check` | Passed; `0 errors` |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/repository/compiler.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/eligibility.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit3_contract_boundaries.py` | Passed |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/repository/compiler.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/eligibility.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit3_contract_boundaries.py` | Passed; 3 files already formatted |
| `mise exec -- hk --no-progress check --step ruff --step ruff_format --all` | Passed |
| `uv build --package three-workflow-delivery-v3` | Passed; built sdist and wheel |
| `dotnet build dirs.proj --no-incremental` | Passed; 0 warnings and 0 errors |
| `pnpm run build` | Passed with non-blocking warnings |
| `uv lock --check` | Passed |
| `pnpm install --frozen-lockfile` | Passed; already up to date |
| `dotnet restore --locked-mode` | Passed |
| `git --no-pager diff --check` | Passed |
| `git --no-pager status --short` | Inspected; unrelated pre-existing modified and untracked paths remain |

`pnpm run build` stamped generated NBGV versions into the two smoke-package
manifests. Those command side effects were returned manually to their
pre-validation `0.0.0-placeholder` values; neither manifest remains modified.

Intermediate validation note: the first managed HK package run shared CPU with
the root pytest suite and hit its fixed 300-second wrapper timeout. The package
suite itself and the isolated managed HK rerun both completed successfully with
`1288 passed`. The final Ruff check and format commands also passed.

## Pre-completion Review

`test-gap-analysis` and `assertion-quality` were invoked as requested by the
test-generation workflow. Their language-extension dependency
`test-analysis-extensions` was unavailable, so the equivalent Python/pytest
review was performed manually here.

### Pseudo-mutation review

| Mutation | Killed by |
|---|---|
| Remove the new `release_policy_path` exact-`str` guard | `test_live_eligibility_blocks_toctou_mutation_during_snapshot_admission` would trigger the surrogate `__ne__` and fail its `comparison_triggered is False` assertion. |
| Move the guard after the equality comparison | The R8 test would trigger the payload and mutate `release_units`, failing the comparison and tuple-closure assertions. |
| Accept `isinstance(value, str)` instead of exact `type(value) is str` for `release_policy_path` | The R8 surrogate is a `str` subclass with side-effecting `__ne__`, so an `isinstance` guard would reach the payload and fail the test. |
| Trust digest equality for list-backed snapshots | `test_live_eligibility_rejects_digest_equivalent_list_backed_snapshot`. |
| Compute digest before Snapshot admission in Live Eligibility | `test_live_eligibility_validates_snapshot_admission_before_digest_use`. |

No surviving mutation remains for this bounded repair after closing the
independent-review finding described above.

### Assertion-quality review

- The new R8 test is not assertion-free and is not trivial-only.
- It combines exception assertion, state/side-effect assertion, exact type
  assertion, identity assertion, and digest equality control.
- The R6 regression remains distinct and continues to assert digest-equivalent
  list-backed rejection.

---

# Workflow Delivery v3 Commit 4 Status

This section appends commit-4 execution state without discarding commit-3
history.

## Phase Status

| Phase | Status |
|---|---|
| Research/checklist | Complete |
| Plan | Complete |
| First-slice Node tests | Complete |
| Canonical Package Target Witness | Complete |
| Isolated Node Build Adapter | Complete |
| Isolated Quality Adapters | Complete |
| Strict negative/scenario tests | Complete |
| Full validation | Complete |
| Gap/assertion gate | Complete |

## Commit-4 Files

- `.testagent/research.md`
- `.testagent/plan.md`
- `.testagent/status.md`
- `src/public/lib/hcoona-release-smoke-npm/package.json`
- `src/public/lib/hcoona-release-smoke-npm/test/index.test.js`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py`
- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`

The unrelated modified
`src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`
was not touched. Full pnpm build stamped two package versions; those command
side effects were changed back to their exact pre-build placeholder values.

## Results

| Command | Result |
|---|---|
| `pnpm --dir src/public/lib/hcoona-release-smoke-npm test` | Passed: 1 Node test |
| Narrow adapter pytest | Passed: 49 cases after independent-review fixes |
| Full v3 pytest | Passed: 1329 tests |
| Root pytest | Passed: 3372 tests after independent-review fixes |
| `uv run --python 3.13 pyrefly check` | Passed: 0 errors |
| Ruff check/format for new Python files | Passed |
| Biome check for Node package/test | Passed |
| `uv build --package three-workflow-delivery-v3` | Passed: sdist and wheel |
| `pnpm run build` | Passed |
| `dotnet build dirs.proj --no-incremental` | Passed: 0 warnings, 0 errors |

## Requirement Coverage

| Requirement | Exact evidence |
|---|---|
| C4-R1 project tests | Node test `smokeMessage returns the stable package identity`; package `test` script. |
| C4-R2 witness bindings/exclusions | `test_package_target_witness_is_canonical_and_execution_independent`; `test_package_target_witness_rejects_invalid_binding_matrix`. |
| C4-R3 frozen version/no fallback | `test_build_rejects_missing_placeholder_or_inconsistent_frozen_version`; packed-version assertions in `test_build_is_deterministic_and_preserves_source_checkout`. |
| C4-R4 isolated declared inputs | `test_build_rejects_unsafe_or_incomplete_declared_inputs_before_execution`; `test_project_build_uses_isolated_inputs_and_preserves_source`. |
| C4-R5 exact staged/packed files | `test_build_rejects_non_exact_source_package_files_allowlist`; strict tar matrix cases for dropped, duplicate, and extra entries. |
| C4-R6 direct build/pack validation | `test_build_is_deterministic_and_preserves_source_checkout`; successful staging lacks the NBGV script while `npm pack --ignore-scripts` succeeds. |
| C4-R7 hashes/manifests/provenance | Concrete SHA-256, SHA-512, entries, lifecycle, source-input, toolchain, and witness assertions in the deterministic-build test. |
| C4-R8 contents negative matrix | `test_artifact_contents_rejects_strict_negative_matrix`, `test_artifact_contents_rejects_noncanonical_witness`, and `test_artifact_contents_rejects_list_backed_expectation`. |
| C4-R9 install/import | `test_install_import_uses_tarball_and_verifies_export_and_witness`; wrong concrete export regression. |
| C4-R10 isolated quality/no credentials | `test_project_build_uses_isolated_inputs_and_preserves_source`; `test_project_test_adapter_scrubs_publication_credentials`. |
| C4-R11 deterministic bytes | `test_build_is_deterministic_and_preserves_source_checkout` compares two real tarballs byte-for-byte. |
| C4-R12 checkout preservation | Success assertions in deterministic/project-build tests and `test_failed_build_preserves_source_checkout`. |
| C4-R13 strict negatives | 49-case narrow suite, including witness-schema/binding, input closure/symlink, version, identity, files, toolchain, tar, noncanonical, list-backed, and command-failure cases. |
| C4-R14 bounded scope | Final git status/diff; no CI/release/workflow/destination files changed. |
| C4-R15 validation/gates | Command table above and review below. |

## Mandatory Pre-completion Gate

`test-gap-analysis` and `assertion-quality` were invoked. Their required
`test-analysis-extensions` dependency was unavailable, so the Python/pytest and
Node `node:test` reviews were completed against repository conventions.

Pseudo-mutation review initially found surviving checks for exact source input
closure, exact source package `files`, frozen runtime tool versions, result
source/toolchain manifests, and list-backed artifact expectations. Production
guards and focused tests were added; the final narrow suite passes. The final
matrix kills dropped/added path checks, placeholder/ambient version fallback,
tool version substitution, missing/altered/misplaced/noncanonical witness,
identity/version/script changes, undeclared tar entries, disabled
credential-scrubbing, non-isolated build output, hash changes, and source
mutation.

Assertion-depth review found no assertion-free, trivial-only, or tautological
generated tests. Positive integration tests assert multiple concrete
observables (bytes, hashes, manifests, source state, imported value, installed
witness); exception-only matrix cases are intentional negative contract tests.

## Commit-4 Independent Review Follow-up Status

| Finding | Status | Evidence |
|---|---|---|
| Witness qualification accepted arbitrary canonical bytes | Fixed | `qualify_npm_artifact_contents` now parses both expectation and packed witness as the exact Package Target Witness schema and validates closed keys, schema, first-slice Release Unit, build definition, NBGV bindings, and version binding. Tests: `test_artifact_contents_rejects_arbitrary_canonical_witness_documents`. |
| First-slice npm identity was only self-consistent | Fixed | Build manifest preparation and artifact expectation validation now require `@hcoona/hcoona-release-smoke-npm`; packed manifest identity validation remains exact. Tests: `test_build_rejects_non_first_slice_package_identity_before_build`, `test_artifact_contents_rejects_non_first_slice_expectation_identity`, and existing wrong-name tar case. |
| `_source_input_manifest` read before checkout-boundary validation | Fixed | A shared safe declared-input pass resolves and validates regular files inside the source checkout before hashing, copying, or runner/toolchain execution. Test: `test_build_rejects_outside_root_symlink_before_read_copy_or_runner`. |

Updated focused validation:

| Command | Result |
|---|---|
| `pnpm --dir src/public/lib/hcoona-release-smoke-npm test` | Passed: 1 Node test |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Passed: 49 tests |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Passed |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Passed |
| `uv run --python 3.13 pyrefly check` | Passed: 0 errors |

The unrelated modified
`src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`
was preserved and not edited by this follow-up.

## Commit-4 Normative Hardening Follow-up Status

This append-only section closes C4-R19 through C4-R25. The corrected final v3
suite count is **1346**, replacing the earlier stale 1329 count above.

### Implementation Results

- Credential-free target execution now constructs a minimal allowlisted
  environment instead of copying `os.environ`. Every invocation receives a
  fresh isolated `HOME`, npm user config, npm cache, and XDG config root.
- Project tests run from a declared-input staging tree outside the checkout.
- Lifecycle evidence binds every exact manifest `scripts` entry.
- Install/import pins `hcoona-release-smoke-npm` internally and exposes no
  caller-selected expected value.
- Tar inspection rejects every non-regular member, including explicit
  directory headers, before exact file-allowlist comparison.
- `BuildRequest` freezes PNPM alongside Node and npm; runtime PNPM is verified,
  result provenance includes it, and Adapter identity/version is an internal
  constant rather than request data.
- Preservation tests snapshot all 11 fixture-relevant project files outside
  installed dependencies and cover build, pack, project-test, and install
  command failures.

### Requirement Coverage

| Requirement | Exact evidence |
|---|---|
| C4-R19 minimal credential-free environment and isolation | `test_project_test_adapter_uses_isolated_stage_and_minimal_environment`; `test_target_controlled_commands_use_minimal_isolated_environments` checks build, test, install, and import commands, arbitrary ambient secrets, exact safe keys/config, three distinct isolated homes, and out-of-checkout working directories. |
| C4-R20 exact lifecycle evidence | `test_lifecycle_evidence_binds_every_manifest_script` adds `dependencies`, `preprepare`, and `postprepare` and requires the build expectation, packed manifest, and qualification result to equal the complete script map. |
| C4-R21 pinned `smokeMessage` | `test_install_import_uses_tarball_and_verifies_export_and_witness` proves the caller-facing parameter is absent and asserts the fixed value; `test_install_import_rejects_mutated_artifact_export` changes packed `dist/index.js` and requires rejection. |
| C4-R22 exact tar member closure | `test_artifact_contents_rejects_explicit_directory_member`; the existing strict matrix continues to reject extra files, misplaced files, and non-regular substitutions. |
| C4-R23 PNPM and pinned Adapter provenance | PNPM case of `test_build_rejects_runtime_toolchain_mismatch`; `test_adapter_identity_is_pinned_and_not_request_forgeable`; concrete four-entry toolchain assertion in `test_build_is_deterministic_and_preserves_source_checkout`. |
| C4-R24 complete preservation and failure paths | `test_source_snapshot_covers_complete_fixture_project`; success checks in `test_build_is_deterministic_and_preserves_source_checkout`, `test_project_build_uses_isolated_inputs_and_preserves_source`, and `test_install_import_uses_tarball_and_verifies_export_and_witness`; four cases of `test_failure_paths_preserve_complete_source_checkout[build|pack|test|install]`. |
| C4-R25 validation and unrelated-file preservation | Exact command table below; `specialized_processor.py` remained SHA-256 `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429` before and after. |

### Final Validation

| Command | Result |
|---|---|
| `pnpm --dir src/public/lib/hcoona-release-smoke-npm test` | Passed: 1 test. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Passed: 58 tests. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests` | Passed: 1346 tests in 288.33s. |
| `uv run --python 3.13 pytest -q` | Passed: 3381 tests in 442.90s. |
| `uv run --python 3.13 ruff check .../adapters/node.py .../tests/adapters/test_node.py` | Passed. |
| `uv run --python 3.13 ruff format --check .../adapters/node.py .../tests/adapters/test_node.py` | Passed: 2 files already formatted. |
| `pnpm exec biome check src/public/lib/hcoona-release-smoke-npm/package.json src/public/lib/hcoona-release-smoke-npm/test/index.test.js` | Passed: 2 files checked, no fixes. |
| `uv run --python 3.13 pyrefly check` | Passed: 0 errors. |
| `uv build --package three-workflow-delivery-v3` | Passed: sdist and wheel built. |
| `pnpm run build` | Passed for all pnpm workspace build projects; expected smoke-manifest version stamps were reset with their repository scripts. |
| `dotnet build dirs.proj --no-incremental` | Passed: 0 warnings, 0 errors. |
| `git --no-pager diff --check` | Passed. |

An additional full `pnpm test` was attempted. The requested smoke-package Node
test passed, as did the other suites before
`hexo-renderer-asciidoc/test/validate-cleanup-errors.test.mjs` reported one
unrelated pre-existing tool-selection failure: its fixture expects PNPM
11.19.0 while the root `packageManager` selection supplies PNPM 11.17.0. This
is outside commit 4 and was not changed.

### Mandatory Pre-completion Gate

`test-gap-analysis` and `assertion-quality` were invoked. Their
`test-analysis-extensions` discovery skill was unavailable, so the checked-in
Python and JavaScript extension references were read directly and the review
was completed inline.

The pseudo-mutation pass found and closed three test-only gaps before final
validation: an ambient `HOME` substitution could survive, an optional
caller-selected smoke expectation could be reintroduced, and the preservation
snapshot's completeness was not itself pinned. The strengthened tests now
require distinct isolated homes, inspect the public install/import signature,
and enumerate the complete 11-file fixture project. No in-scope mutation from
C4-R19 through C4-R24 remains unguarded.

Assertion-depth review found 25 pytest test functions containing 49 concrete
bare assertions and 16 `pytest.raises` checks, with zero assertion-free or
trivial-only tests. The Node test adds two concrete equality assertions.
Determinism and witness round-trip comparisons are accompanied by concrete
hash, manifest, provenance, source-state, installed-value, and negative
observables, so none is tautological.

## Commit-4 Artifact Build/Pack Environment Review Follow-up Status

The independent-review finding is fixed without production changes.
`test_target_controlled_commands_use_minimal_isolated_environments` now invokes
`build_node_package` under the `_run` monkeypatch and observes the direct
artifact build and `npm pack --ignore-scripts` boundaries. The test requires
the exact six-command target sequence, four isolated execution homes, the
shared artifact build/pack stage and environment, exact safe environment keys,
frozen epoch/locale/timezone, exact isolated npm config/cache/XDG paths and
config contents, and exclusion of ambient credentials and sentinels.

### Requirement Coverage

| Requirement | Exact evidence |
|---|---|
| C4-R26 artifact build/pack environment isolation | `test_target_controlled_commands_use_minimal_isolated_environments` invokes `build_node_package`; asserts direct build and pack command presence/order, `--ignore-scripts`, shared isolated stage/environment, exact environment closure and values, exact npm paths/config, four isolated homes, and ambient-secret exclusion. |

### Validation

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_target_controlled_commands_use_minimal_isolated_environments` | Passed: 1 test in 4.93s. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Passed: 58 tests in 25.19s. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests` | Passed: 1346 tests in 292.22s. |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Passed: all checks. |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Passed: 1 file already formatted. |
| `uv run --python 3.13 pyrefly check` | Passed: 0 errors; 36 suppressed, 122 warnings not shown. |
| `uv build --package three-workflow-delivery-v3` | Passed: sdist and wheel built. |
| Biome | Not run: this follow-up changed no JavaScript or JSON files. |
| `git --no-pager diff --check` | Passed. |

The unrelated modified
`src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`
remained SHA-256
`91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`.

### Pre-completion Test Review

`test-gap-analysis` and `assertion-quality` were invoked. Their requested
`test-analysis-extensions` dependency was unavailable, so the bounded Python
review was completed inline. The focused test has 20 explicit assertions.
Pseudo-mutations that omit `build_node_package` or `npm pack`, inherit ambient
state, drop required safe keys or `SOURCE_DATE_EPOCH`, change npm config/cache
paths or contents, reuse ambient `HOME`, or split the artifact build/pack
environment are killed by concrete assertions. The test has equality,
collection, negative, structural, and state/side-effect checks; none is
assertion-free, trivial-only, self-referential, or tautological.

<!-- BEGIN RUN: adjudicated-workflow-delivery-v3-commit4-focused-tests-status-2026-08-10 -->

---

# Adjudicated Workflow Delivery v3 Commit 4 Focused Test Status

## Phase 5 Result

**STATUS: BLOCKED — focused tests are collected, but current production lacks
C4-R27 through C4-R30 and C4-R32 behavior.**

Phase 5 edited no production, configuration, or test file. All 80 cases in the
target pytest file collected, so no syntax/collection repair was necessary or
permitted. The 22 cases added in Phases 1-4 remain present and unskipped.

## Requirement-to-Evidence Mapping

| Requirement | Exact test evidence and concrete assertion | Result |
|---|---|---|
| C4-R27 | `test_build_reads_declared_inputs_once_and_reuses_immutable_bytes` asserts one `Path.read_bytes` capture for each declared path, manifest SHA-256 from those captured bytes, captured bytes in staging and `package/dist/index.js`, exclusion of `mutated-after-capture`, and retention of the checkout mutation. `test_build_rejects_outside_root_symlink_before_read_copy_or_runner` asserts rejection before runner execution. `test_build_is_deterministic_and_preserves_source_checkout` asserts identical tar bytes, exact-byte hashes/manifests, and an unchanged checkout. | **BLOCKED:** the new test fails at staged-byte equality; both retained regressions pass. |
| C4-R28 | `test_artifact_contents_rejects_suffix_smuggling[raw-suffix]`, `[second-gzip-member]`, `test_artifact_contents_rejects_concatenated_tar_archive`, and `test_artifact_contents_rejects_malformed_or_premature_streams[malformed-gzip\|missing-gzip-trailer\|halfway-truncated-gzip]` each require `ValueError`. `test_artifact_contents_accepts_exact_tarball` asserts manifest equality and exact input size. `test_artifact_contents_rejects_strict_negative_matrix[extra-entry]` and `test_artifact_contents_rejects_explicit_directory_member` retain regular-member closure. `test_build_is_deterministic_and_preserves_source_checkout` retains exact-byte SHA-256 and deterministic bytes. | **BLOCKED:** malformed gzip passes; the other five new stream probes fail. All retained regressions pass. |
| C4-R29 | `test_runtime_request_is_minimal_frozen_and_exported` asserts exactly `node_version`/`npm_version`, exact `str`, frozen/slotted behavior, no PNPM/future fields, and package identity. All 12 exact cases of `test_quality_adapters_probe_frozen_runtime_before_operations` assert invalid-request rejection before `_run`, mismatch short-circuiting, and Node/npm probes before project-test and install/import operations. `test_target_controlled_commands_use_minimal_isolated_environments` asserts an existing isolated empty `NPM_CONFIG_GLOBALCONFIG` and closed credential-free environments for every command. | **BLOCKED:** `RuntimeRequest` is absent and every observed command lacks `NPM_CONFIG_GLOBALCONFIG`. |
| C4-R30 | `test_subprocess_sequence_is_complete_and_forbids_nbgv_or_restoration_commands` asserts the complete unfiltered 16-command sequence, operation-specific dynamic arguments, and no NBGV stamp/reset or Git restoration command. | **BLOCKED:** the forbidden-command scan passes, but production emits 12 commands and omits four quality-runtime probes. |
| C4-R31 | This delimited status run records exact commands/counts, every parser probe, the actual subprocess sequence, changed-test scope, blockers, and exclusions. The pre-append 28,667-byte status prefix retains SHA-256 `2f8bc12afc547b6f67ad17e3b30b50be14cd2e982eb6ed13fe3281d7343a467a`. | **PASS for append-only evidence; regression gate remains blocked.** |
| C4-R32 | `test_adapter_public_api_exports_closed_types_and_functions` asserts package/module identity for all 12 closed exports, exact signatures `("project_root", "request")` and `("tarball", "expectation", "request")`, request annotations identical to `RuntimeRequest`, and no future contract export. `test_runtime_request_is_minimal_frozen_and_exported` also asserts RuntimeRequest export identity. | **BLOCKED:** `RuntimeRequest` and its export are absent; both quality signatures omit `request`. |

### C4-R27: One Capture, Manifest, Staging, and Preservation

The following assertions completed before the focused failure:

| Declared path | Captured `Path.read_bytes` count | Captured/manifest SHA-256 |
|---|---:|---|
| `README.md` | 1 | `sha256:f4471e75cbbea51ca6fffe8a417fff373fc010cbba2e3f303cbb2c22545cf46c` |
| `package.json` | 1 | `sha256:a7d84bac91fe5f9fa7ccfbf46cd065cd85ded95188046d96f6f2c9ce97775566` |
| `scripts/build.mjs` | 1 | `sha256:97b0352825517db85e707bfc8d69af01a4c4166d56381c6e4cab21533c2a8750` |
| `src/index.js` | 1 | `sha256:b61fbccbf0cf9830e4032cba82a4d3b4f22fb56c3f4e28be184d66f4b1d0ace0` |

`observed_source_reads` matched declared order; all captured values had exact
type `bytes`; `source_input_manifest` matched SHA-256 of the captured bytes;
the index digest did not match the deliberate later mutation; and the prepared
and runner staging roots matched. The next assertion failed:

- **Test:** `test_build_reads_declared_inputs_once_and_reuses_immutable_bytes`
- **Input:** replace temporary `src/index.js` with
  `return 'mutated-after-capture'` immediately after its first captured read.
- **Expected:** staged `src/index.js` equals captured
  `return 'hcoona-release-smoke-npm'`.
- **Actual:** staged `src/index.js` contains
  `return 'mutated-after-capture'`.
- **Responsible symbols:** `_source_input_manifest` reads bytes for hashing,
  while `_copy_declared_inputs` independently calls `shutil.copyfile` on the
  mutable source path.

Because the failure occurs at `staged_sources == captured_sources`, the later
packed-byte and retained-mutation assertions in that test are present but not
reached. The retained outside-root symlink and deterministic checkout tests
pass in the full target run.

### C4-R28: Complete Compressed/Tar Stream Probes

| Exact test/input | Expected | Actual | Result |
|---|---|---|---|
| `test_artifact_contents_rejects_suffix_smuggling[raw-suffix]`; valid `.tgz + b"RAW-SUFFIX"` | `ValueError` | Returned successfully; no exception. | BLOCKED |
| `test_artifact_contents_rejects_suffix_smuggling[second-gzip-member]`; valid `.tgz` plus gzip member containing `package/smuggled.txt = b"second-member"` | `ValueError` | Returned successfully; no exception. | BLOCKED |
| `test_artifact_contents_rejects_concatenated_tar_archive`; one gzip payload containing valid tar plus a second tar with `package/smuggled.txt = b"second-archive"` | `ValueError` | Returned successfully; no exception. | BLOCKED |
| `test_artifact_contents_rejects_malformed_or_premature_streams[malformed-gzip]`; `b"not-a-gzip-stream"` | `ValueError` | `ValueError` raised. | PASS |
| `test_artifact_contents_rejects_malformed_or_premature_streams[missing-gzip-trailer]`; valid `.tgz[:-8]` | `ValueError` | Returned successfully; no exception. | BLOCKED |
| `test_artifact_contents_rejects_malformed_or_premature_streams[halfway-truncated-gzip]`; valid `.tgz[:len(.tgz)//2]` | `ValueError` | Raw `EOFError: Compressed file ended before the end-of-stream marker was reached`. | BLOCKED |

The responsible production symbol is `_read_tarball`: `tarfile.open(...,
"r:gz")` is not followed by a complete compressed-stream/tar-stream closure
check, and its exception wrapper catches `tarfile.TarError` but not `EOFError`.

Retained exact-input evidence passes. A read-only build probe over the same
fixture produced `byte_size=983` and
`sha256:0e615dbe7cf23a5192d9565518ff741784a0092df23d3433bee9b4eb52c818dd`;
`test_artifact_contents_accepts_exact_tarball` asserts size against
`len(input_tgz_bytes)` and manifest identity, while the deterministic test
asserts SHA-256 against those exact bytes. The undeclared regular member,
explicit directory member, and repeated-build byte regressions all pass.

### C4-R29 and C4-R32: Runtime, Environment, and Public Closure

`test_runtime_request_is_minimal_frozen_and_exported` fails its first focused
assertion: `getattr(node_adapter, "RuntimeRequest", None)` is `None`.
Consequently each exact quality case fails at the same production precondition:

- `test_quality_adapters_probe_frozen_runtime_before_operations[matching-versions-project-tests]`
- `test_quality_adapters_probe_frozen_runtime_before_operations[matching-versions-install-import]`
- `test_quality_adapters_probe_frozen_runtime_before_operations[node-version-mismatch-project-tests]`
- `test_quality_adapters_probe_frozen_runtime_before_operations[node-version-mismatch-install-import]`
- `test_quality_adapters_probe_frozen_runtime_before_operations[npm-version-mismatch-project-tests]`
- `test_quality_adapters_probe_frozen_runtime_before_operations[npm-version-mismatch-install-import]`
- `test_quality_adapters_probe_frozen_runtime_before_operations[empty-node-version-project-tests]`
- `test_quality_adapters_probe_frozen_runtime_before_operations[empty-node-version-install-import]`
- `test_quality_adapters_probe_frozen_runtime_before_operations[empty-npm-version-project-tests]`
- `test_quality_adapters_probe_frozen_runtime_before_operations[empty-npm-version-install-import]`
- `test_quality_adapters_probe_frozen_runtime_before_operations[surrogate-request-project-tests]`
- `test_quality_adapters_probe_frozen_runtime_before_operations[surrogate-request-install-import]`

The expected success sequences are exactly `node --version`, `npm --version`,
then `npm test --ignore-scripts`; and `node --version`, `npm --version`, `npm
install --ignore-scripts --no-audit --no-fund --package-lock=false
<consumer/package.tgz>`, then the fixed import. Mismatches must stop after the
failing probe, and empty/surrogate requests must leave `_run` unobserved. None
of those branches can be reached until production defines the exact request.

`test_target_controlled_commands_use_minimal_isolated_environments` separately
observed all 12 current commands and found `NPM_CONFIG_GLOBALCONFIG` missing
from every environment. `_credential_free_environment` creates only the npm
user config/cache and never creates or exports an isolated empty global config.

`test_adapter_public_api_exports_closed_types_and_functions` fails because
`RuntimeRequest` is absent. Production currently has signatures
`run_node_project_tests(source_root: Path)` and
`qualify_npm_install_import(tarball, expectation)`; Pyrefly reports six
`bad-argument-count` errors at the planned request call sites. The package
`adapters.__init__` does not import or list `RuntimeRequest`.

### C4-R30: Complete Unfiltered Command Evidence

The test observed this exact 12-command shape, with the two dynamic paths bound
to their isolated temporary directories:

1. `node --version`
2. `pnpm --version`
3. `npm --version`
4. `node scripts/build.mjs`
5. `npm pack --ignore-scripts --json --pack-destination <build-output>`
6. `node --version`
7. `pnpm --version`
8. `npm --version`
9. `node scripts/build.mjs`
10. `npm test --ignore-scripts`
11. `npm install --ignore-scripts --no-audit --no-fund --package-lock=false <consumer/package.tgz>`
12. `node --input-type=module -e 'import {smokeMessage} from "@hcoona/hcoona-release-smoke-npm";process.stdout.write(smokeMessage());'`

The expected list has `node --version` and `npm --version` before command 10
and again before command 11, for 16 commands total. Pytest reports the first
diff at index 9: actual `npm test --ignore-scripts`, expected `node --version`;
the expected list contains four additional probes. The forbidden scan executed
first and passed: no observed command contains `nbgv-version.mjs` or `stamp`,
no token is lifecycle `reset`, and no command begins with `git checkout`,
`git restore`, `git reset`, or `git clean`.

## Commands and Results

Commands are numbered by the Phase 5 plan. No workspace-wide test body was
executed.

| Step | Exact command | Exit/result |
|---:|---|---|
| 1 | `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'reads_declared_inputs_once or suffix_smuggling or concatenated_tar_archive or malformed_or_premature_streams or runtime_request_is_minimal_frozen_and_exported or quality_adapters_probe_frozen_runtime_before_operations or subprocess_sequence_is_complete_and_forbids_nbgv_or_restoration_commands or adapter_public_api_exports_closed_types_and_functions or target_controlled_commands_use_minimal_isolated_environments'` | Exit 1; 80 collected, 1 passed, 22 failed, 57 deselected in 6.86s. An exact diagnostic rerun had the same counts in 6.96s. |
| 2 | `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 1; 80 collected, 58 passed, 22 failed in 22.87s. |
| 3 | `pnpm --dir src/public/lib/hcoona-release-smoke-npm test` | Exit 0; 1 collected, 1 passed, 0 failed. `smokeMessage returns the stable package identity`. |
| 4 | `uv run --python 3.13 pytest --collect-only -q` | Exit 0; 3,403 tests collected in 0.43s; collection only, no workspace-wide tests executed. The target file contributes 80 cases, +22 over the research-recorded 58-case pre-Phase-1 target. |
| 5a | `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 1; 17 test-file findings: PLR0915 ×4, PT011 ×3, PLC0415 ×1, B010 ×1, TRY003 ×1, EM102 ×1, E501 ×4, PLR0911 ×1, PLR2004 ×1. A diagnostic rerun returned the same 17. No auto-fix was used. |
| 5b | `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 1; `test_node.py` would be reformatted; two production files already formatted. No file was changed. |
| 6 | `uv run --python 3.13 pyrefly check` | Exit 1; 7 errors: six `bad-argument-count` request-call errors and one `archive.extractfile(...).read()` possible-`None` error; 36 suppressed and 122 warnings not shown. |
| 7 | `uv build --package three-workflow-delivery-v3` | Exit 0; built `three_workflow_delivery_v3-0.1.0.tar.gz` and `three_workflow_delivery_v3-0.1.0-py3-none-any.whl`. |
| 8 | `pnpm test` | **NOT RUN**: the current user explicitly prohibited workspace-wide tests. No exit code or test count is claimed; the previously documented unrelated `hexo-renderer-asciidoc` condition was not exercised. |

The supplemental read-only valid-artifact build probe exited 0 and printed the
983-byte SHA-256 evidence above. `sha256sum` over the four current declared
inputs exited 0 and produced the per-path digests in the C4-R27 table.

## Explicit Acceptance Checklist

| Plan acceptance item | Exact evidence | Outcome |
|---|---|---|
| Phase 1 new test collected under its exact name | Step 1 collected `test_build_reads_declared_inputs_once_and_reuses_immutable_bytes`. | PASS |
| One immutable capture asserted for every declared file | Read-count and manifest assertions passed for all four paths; staged equality failed on mutated `src/index.js`. | BLOCKED |
| Outside-root symlink and deterministic checkout regressions retained | `test_build_rejects_outside_root_symlink_before_read_copy_or_runner` and `test_build_is_deterministic_and_preserves_source_checkout` are absent from the full-run failure list. | PASS |
| Phase 1 missing behavior recorded precisely | C4-R27 blocker above names input, expected/actual bytes, and `_source_input_manifest`/`_copy_declared_inputs`. | PASS |
| All three Phase 2 parser test functions collected | Step 1 collected six parameter cases across the three exact function names. | PASS |
| Every raw suffix/member/archive/malformed/premature probe independently asserted | Six-row C4-R28 table records each exact input and outcome. | PASS as evidence; behavior BLOCKED |
| Valid size/hash and deterministic-byte regressions retained | Exact tarball test and deterministic build pass; supplemental evidence is 983 bytes and the recorded SHA-256. | PASS |
| Undeclared and non-regular member rejection retained | Strict `extra-entry` and explicit-directory tests pass in Step 2. | PASS |
| RuntimeRequest exact two-field frozen/slotted contract asserted | `test_runtime_request_is_minimal_frozen_and_exported` contains the exact assertions but fails because the symbol is absent. | BLOCKED |
| Empty and surrogate requests fail before all operations | Six exact parameter cases are present; all stop at missing `RuntimeRequest` before the intended production branch. | BLOCKED |
| Match/mismatch probes covered for both quality adapters | Six exact matching/mismatch parameter cases are present; all stop at missing `RuntimeRequest`. | BLOCKED |
| Isolated empty global npm config is closed into every command environment | Existing strengthened environment test fails with the exact 12-command missing-key list. | BLOCKED |
| Public identities/signatures asserted without future contracts | API test is present; production lacks RuntimeRequest/export/signature closure. No future contract was added. | BLOCKED |
| Exact unfiltered 16-command assertion | Sequence test compares all commands directly; actual count is 12. | BLOCKED |
| Dynamic destination/tarball/import arguments separately asserted | Exact assertions are present after the sequence equality; they are not reached because equality fails first. | BLOCKED |
| Probe ordering and forbidden-command scan asserted | Probe ordering fails; all forbidden-command assertions pass before the sequence failure. | PARTIAL |
| Phase 5 test changes limited to intended pytest file | Phase 5 changed no test. Phases 1-4 changed only `tests/adapters/test_node.py`. | PASS |
| No production/config/smoke Node test edit in this run | Phase 5 changed only this appended status section; the authoritative workspace's pre-existing commit-4 production/config changes remain untouched. | PASS |
| Every relevant result or blocker appended | Command table and C4-R27–R32 sections above. | PASS |
| Prior research/plan/status history retained | Pre-append status prefix size/hash recorded; plan and research were not edited in Phase 5. | PASS |
| Specialized processor and future contracts excluded | The specialized processor and future Snapshot/Evidence/Finalizer/Planner/publication/destination contracts were not opened, imported, tested, edited, or added. | PASS |

## Changed-Test and Preservation Evidence

- **Phases 1-4 changed test file:** only
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
  (eight new test functions representing 22 collected parameter cases, plus
  the strengthened existing isolated-environment test).
- **Regression-only, unchanged by Phases 1-5:**
  `src/public/lib/hcoona-release-smoke-npm/test/index.test.js`, SHA-256
  `146189b206c0523d005ec124df62c2c05d15766ea446bbfdd3c23be9fb076178`.
- **Phase 5 changed file:** `.testagent/status.md` only, by this appended
  delimited section. The pre-append target-test SHA-256 remains
  `98162f6db305b79c9c460ee9d461c6b0bf12052bea1f8101c08892a5ecb5bfdb`.
- `.testagent/plan.md` remains SHA-256
  `88ef11622ce1a5505531a5ecfe0145489dd079d047bb73db4a77f888e72f3aeb`;
  `.testagent/research.md` remains SHA-256
  `79890f2619283ebc6edb47dee6e74dba6d396e74017ecad6933a3b02bf15df43`.
- The excluded
  `src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`
  retains the research-recorded baseline SHA-256
  `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`.
  It was never opened, imported, tested, edited, or re-hashed in this run.
- No Snapshot, Evidence, Finalizer, Planner, workflow, publication, or
  destination contract was added. Those concerns remain out of scope.

## Blocker Summary

1. `_source_input_manifest` and `_copy_declared_inputs` do not share one
   immutable captured byte set, so staging can diverge after manifest hashing.
2. `_read_tarball` does not prove complete gzip/tar consumption and does not
   normalize premature-stream `EOFError` to the required `ValueError`.
3. `_credential_free_environment` omits isolated empty
   `NPM_CONFIG_GLOBALCONFIG`.
4. `RuntimeRequest` and its package export do not exist; quality signatures
   and Node/npm probes are therefore absent.
5. The full Adapter command sequence has 12 rather than 16 commands because
   project-test and install/import omit Node/npm probes.
6. The Phase 1-4 test file has 17 Ruff findings, one format-check failure, and
   7 Pyrefly errors. They are not syntax/collection defects, so Phase 5 did not
   alter tests merely to pass checks.

<!-- END RUN: adjudicated-workflow-delivery-v3-commit4-focused-tests-status-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-commit-4-final-validation-2026-08-10 -->

# Commit-4 Final Validation

## Final Scope and Commands

- Final commit-4 test changes are only
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`.
- No test or production file was edited during this final-validation append.
- Final build:
  `uv build --package three-workflow-delivery-v3`
  exited **0** and produced
  `dist/three_workflow_delivery_v3-0.1.0.tar.gz` and
  `dist/three_workflow_delivery_v3-0.1.0-py3-none-any.whl`.
- Final focused test:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
  exited **1** with **53 passed, 29 failed, 0 skipped (82 total)** in
  18.33 seconds.

## Final Blocker Grouping

The 29 focused failures group without remainder:

1. **23 Runtime/API/probe/environment failures:** production has no
   `RuntimeRequest`, package export, request-bearing quality signatures, or
   quality Node/npm probes. This group comprises six retained tests that now
   stop in `_make_runtime_request`, one direct RuntimeRequest contract test,
   fourteen exact quality-operation cases, one public-API closure test, and
   one full-sequence test. The isolated-global-config assertions are currently
   masked by the earlier missing-RuntimeRequest precondition.
2. **1 immutable-source-byte reuse failure:**
   `test_build_reads_declared_inputs_once_and_reuses_immutable_bytes` proves
   manifest capture occurs once but staging re-reads mutable source bytes.
3. **5 strict gzip/tar failures:** raw suffix, second gzip member,
   concatenated tar archive, missing gzip trailer, and halfway truncation.
   The first four are accepted instead of raising `ValueError`; halfway
   truncation leaks raw `EOFError`.

No focused test is skipped, ignored, or marked inconclusive.

## C4-R27–C4-R32 Final Acceptance Map

| Requirement | Exact final test names and concrete assertions | Final result |
|---|---|---|
| C4-R27 | `test_build_reads_declared_inputs_once_and_reuses_immutable_bytes` asserts exactly one `Path.read_bytes` capture for each of `README.md`, `package.json`, `scripts/build.mjs`, and `src/index.js`; exact captured-byte SHA-256 values in `source_input_manifest`; staging/runner reuse of the captured bytes; packed byte-size, bytes-hex, and SHA-256 evidence; exclusion of every `mutated-after-capture` payload; and retention of the deliberate checkout mutation. `test_build_rejects_outside_root_symlink_before_read_copy_or_runner` asserts a non-regular/outside-root symlink is rejected before `_run`. `test_build_is_deterministic_and_preserves_source_checkout` asserts two tarballs are byte-identical, SHA-256/SHA-512 and manifest entries match those bytes, and the complete source snapshot remains unchanged. | **BLOCKED only on immutable reuse:** captured counts/digests pass, but staged `src/index.js` uses the later mutation. Symlink and deterministic retained evidence passes. |
| C4-R28 | `test_artifact_contents_rejects_suffix_smuggling[raw-suffix]` and `[second-gzip-member]` prove the suffix bytes are actually appended and require `ValueError`; `test_artifact_contents_rejects_concatenated_tar_archive` proves the second tar contains `package/smuggled.txt` and requires `ValueError`; `test_artifact_contents_rejects_malformed_or_premature_streams[malformed-gzip]`, `[missing-gzip-trailer]`, and `[halfway-truncated-gzip]` require normalized `ValueError`. Retained closure evidence is `test_artifact_contents_rejects_strict_negative_matrix[extra-entry]` for an undeclared regular member, `test_artifact_contents_rejects_explicit_directory_member` for a non-regular member, `test_artifact_contents_accepts_exact_tarball` for valid manifest/size/basename, and `test_build_is_deterministic_and_preserves_source_checkout` for deterministic exact bytes and digests. | **BLOCKED on five strict-stream cases:** malformed gzip and all retained undeclared/non-regular/valid/deterministic cases pass. |
| C4-R29 | `test_runtime_request_is_minimal_frozen_and_exported` asserts the exact required fields/signature `("node_version", "npm_version")`, no defaults, exact `str` values, slots/no `__dict__`, frozen mutation rejection, forbidden-field absence, and identical package export. The fourteen exact cases of `test_quality_adapters_probe_frozen_runtime_before_operations` cover matching versions, Node mismatch, npm mismatch, empty Node, empty npm, surrogate request, and RuntimeRequest subclass for both project-test and install/import; they assert invalid requests make no `_run` call, mismatches stop after the failing probe, successful operations follow Node/npm probes, and every command receives one closed isolated environment. `test_target_controlled_commands_use_minimal_isolated_environments` asserts an isolated, existing, empty `NPM_CONFIG_GLOBALCONFIG`, distinct from user config, with no ambient credential inheritance. | **BLOCKED:** `RuntimeRequest` is absent; all quality paths and the isolated-global-config gate are currently masked or absent in production. |
| C4-R30 | `test_subprocess_sequence_is_complete_and_forbids_nbgv_or_restoration_commands` captures `_run` argv without filtering, asserts the exact 16-command order below, directly binds the dynamic build-output and consumer-tarball paths, pins the fixed import script, and scans every command/token for forbidden NBGV stamping/reset and Git restoration. | **BLOCKED:** final execution stops at missing `RuntimeRequest`; retained unmasked evidence shows production emitted 12 commands and omitted four quality probes. Forbidden scans found no NBGV/restoration command. |
| C4-R31 | This append-only final-validation section records the verbatim final commands, exit/count results, grouped blockers, exact acceptance mapping, command sequence, forbidden scans, review gates, scoped file evidence, and prefix witness. | **PASS for complete append-only evidence; production acceptance remains blocked as above.** |
| C4-R32 | `test_adapter_public_api_exports_closed_types_and_functions` asserts package/module identity for exactly `PackageTargetWitness`, `BuildRequest`, `ArtifactExpectation`, `ArtifactManifest`, `BuildResult`, `InstallImportResult`, `RuntimeRequest`, `build_node_package`, `run_node_project_build`, `run_node_project_tests`, `qualify_npm_artifact_contents`, and `qualify_npm_install_import`; exact signatures `("project_root", "request")` and `("tarball", "expectation", "request")`; required request parameters; type-hint identity with `RuntimeRequest`; the exact two RuntimeRequest fields; and no Snapshot/Evidence/Finalizer/Planner export. `test_runtime_request_is_minimal_frozen_and_exported` independently asserts RuntimeRequest package identity. | **BLOCKED:** RuntimeRequest/export/request parameters are absent. The asserted API remains closed to future contracts. |

## Complete Unfiltered Command Contract

The final C4-R30 test pins this complete, unfiltered required sequence:

1. `node --version`
2. `pnpm --version`
3. `npm --version`
4. `node scripts/build.mjs`
5. `npm pack --ignore-scripts --json --pack-destination <build-output>`
6. `node --version`
7. `pnpm --version`
8. `npm --version`
9. `node scripts/build.mjs`
10. `node --version`
11. `npm --version`
12. `npm test --ignore-scripts`
13. `node --version`
14. `npm --version`
15. `npm install --ignore-scripts --no-audit --no-fund --package-lock=false <consumer/package.tgz>`
16. `node --input-type=module -e 'import {smokeMessage} from "@hcoona/hcoona-release-smoke-npm";process.stdout.write(smokeMessage());'`

The retained last unmasked production observation was also unfiltered:

1. `node --version`
2. `pnpm --version`
3. `npm --version`
4. `node scripts/build.mjs`
5. `npm pack --ignore-scripts --json --pack-destination <build-output>`
6. `node --version`
7. `pnpm --version`
8. `npm --version`
9. `node scripts/build.mjs`
10. `npm test --ignore-scripts`
11. `npm install --ignore-scripts --no-audit --no-fund --package-lock=false <consumer/package.tgz>`
12. `node --input-type=module -e 'import {smokeMessage} from "@hcoona/hcoona-release-smoke-npm";process.stdout.write(smokeMessage());'`

The forbidden-command assertions lowercase/join argv only for scanning and
assert:

- no command contains `nbgv-version.mjs`;
- no command contains `stamp`;
- no argv token is lifecycle `reset`; and
- no command begins with `git checkout`, `git restore`, `git reset`, or
  `git clean`.

All forbidden scans passed on the retained unmasked observation.

## Mandatory Final Review Gates

- The `test-gap-analysis` skill was invoked.
- The `assertion-quality` skill was invoked.
- The required `test-analysis-extensions` invocation was attempted, but the
  skill is unavailable in this environment (`Skill "test-analysis-extensions"
  not found`).
- Independent final review found no remaining in-scope pseudo-mutation,
  assertion-depth, or scenario gap.
- The eight generated test functions contain behavior-bearing equality,
  structural, negative, exception, state, byte/digest, ordering, environment,
  and API-identity assertions. None is assertion-free, trivial-only,
  self-referential, or tautological; no constant assertion was found.
- The review specifically retained independent observables for suffix
  construction, second-member contents, exact archive bytes/digests,
  source-byte capture versus staging/packing, invalid-request no-call behavior,
  probe ordering, dynamic command arguments, and forbidden-command absence.

## Append-Only and Out-of-Scope Evidence

- This final-validation operation appended only `.testagent/status.md`; it did
  not edit tests, production, plan, or research.
- Commit-4 final test changes remain limited to
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`.
- The pre-append status prefix is exactly lines **1–634**, **49,567 bytes**,
  SHA-256
  `aff6d87d39c86c518d00aa4471e29a8093f668c374af628a122e25e1e515aa48`.
  A post-append prefix check over the first 49,567 bytes reproduced that exact
  digest, proving byte-for-byte prefix preservation.
- `src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`
  was not opened, imported, tested, edited, or re-hashed by this commit-4
  final-validation operation. Any pre-existing workspace status outside this
  scope is not attributed to this run.
- Snapshot/Evidence/Finalizer/Planner contracts remain out of scope: no such
  production or test contract was added or modified. The only in-scope
  reference is the negative public-API assertion that these future contract
  names are not exported.

<!-- END RUN: workflow-delivery-v3-commit-4-final-validation-2026-08-10 -->

<!-- BEGIN ADDENDUM: final-workspace-validation-2026-08-10 -->

## Final workspace-validation addendum (2026-08-10)

- `pnpm build` exited 0; all eight reported package builds passed.
- `pnpm test` exited 1 with 349/350 tests passed. The one unrelated failure
  was at `hexo-renderer-asciidoc/validate-cleanup-errors.test.mjs:342` because
  installed pnpm 11.17.0 did not meet required pnpm 11.19.0.
- `uv run --python 3.13 pytest -q` exited 1 after 420.95s with 3,376 passed,
  29 failed, and 0 skipped (3,405 total). All 29 failures were confined to
  generated commit-4
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`:
  23 RuntimeRequest/API/probe/env failures, 1 immutable source-byte reuse
  failure, and 5 strict tar/gzip failures. There were no unrelated Python
  failures.
- The narrower target build/test results already recorded above are retained
  by reference and are not superseded by this workspace-level validation.
- Validation edited no test or production files; no files other than this
  append-only status addendum were edited. `specialized_processor.py` was
  untouched.
- Prefix-integrity baseline: the pre-append 60,292-byte prefix had SHA-256
  `c8eee2755093129242e78080276fadd6d233c1177d28096232dee3eb0b900cea`.

<!-- END ADDENDUM: final-workspace-validation-2026-08-10 -->

<!-- BEGIN CORRECTION ADDENDUM: root-build-version-stamp-2026-08-10 -->

## Correction to final workspace validation

- After the mandatory root `pnpm build`, final Git status newly showed the
  tracked `src/public/lib/hcoona-release-smoke-npm-dual/package.json` modified
  from version `0.0.0-placeholder` to `1.0.0-beta.265.g39f41f1`.
- This is an observed root-build version-stamp side effect, not an intentional
  test-generation edit. It was not reverted because the workspace must be
  preserved and version-control restore, reset, or reconstruction is
  prohibited.
- This side effect is outside the focused commit-4 test output and is a
  validation blocker/working-tree side effect. All intentional edits remain
  the focused test file plus append-only `.testagent` artifacts.
- `src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`
  was pre-existing modified and remained untouched.
- Prefix-integrity baseline: pre-append lines **1–797** had SHA-256
  `facb30d0859f62595d45c6fe04a2ec693974951f2b9ccdce9b589b96f6fa2571`.

<!-- END CORRECTION ADDENDUM: root-build-version-stamp-2026-08-10 -->

<!-- BEGIN IMPLEMENTATION RESULT: workflow-delivery-v3-commit-4-2026-08-10 -->

## Adjudicated commit-4 implementation result

The 29 generated production failures are resolved. Source inputs are captured
once as immutable bytes and reused for both manifest hashing and staging. npm
tarball parsing now requires one complete gzip member and one closed tar
archive, normalizing malformed and premature streams to `ValueError`. Quality
operations use the exported frozen `RuntimeRequest`, isolated empty global npm
configuration, and ordered Node/npm version probes.

| Requirement | Passing evidence |
|---|---|
| C4-R27 | `test_build_reads_declared_inputs_once_and_reuses_immutable_bytes`; `test_build_rejects_outside_root_symlink_before_read_copy_or_runner`; `test_build_is_deterministic_and_preserves_source_checkout` |
| C4-R28 | `test_artifact_contents_rejects_suffix_smuggling`; `test_artifact_contents_rejects_concatenated_tar_archive`; `test_artifact_contents_rejects_malformed_or_premature_streams`; retained exact/undeclared/non-regular member tests |
| C4-R29 | `test_runtime_request_is_minimal_frozen_and_exported`; all cases of `test_quality_adapters_probe_frozen_runtime_before_operations`; `test_target_controlled_commands_use_minimal_isolated_environments` |
| C4-R30 | `test_subprocess_sequence_is_complete_and_forbids_nbgv_or_restoration_commands` pins the complete 16-command sequence including probes |
| C4-R31 | This section was appended after the existing 62,755-byte status prefix; the prefix SHA-256 was `db9498ae0e75ba85ad5ce7f3307acc412d3468a5665ded8ec2f08b604774e5d3` |
| C4-R32 | `test_adapter_public_api_exports_closed_types_and_functions` verifies exports, exact signatures, and absence of future Snapshot/Evidence/Planner contracts |

## Final validation

| Command | Result |
|---|---|
| `pnpm --dir src/public/lib/hcoona-release-smoke-npm test` | Passed: 1 test |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Passed: 82 tests |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Passed |
| `uv run --python 3.13 ruff format ...` then `uv run --python 3.13 ruff format --check ...` | Applied formatting to one file; final check passed with all 3 files formatted |
| `uv run --python 3.13 pyrefly check` | Passed: 0 errors |
| `git diff --check` | Passed |

The unintended
`src/public/lib/hcoona-release-smoke-npm-dual/package.json` version stamp was
restored exactly to `0.0.0-placeholder`, leaving no diff for that file. The
pre-existing `specialized_processor.py` one-line diff remained unchanged and
was not edited. No broad version-mutating build command was run.

<!-- END IMPLEMENTATION RESULT: workflow-delivery-v3-commit-4-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-commit4-umask-padding-regressions-status-2026-08-10 -->

# Commit-4 Cross-Umask and Tar-Padding Regression Status

**STATUS: BLOCKED BY THE TWO ADJUDICATED PRODUCTION BEHAVIORS.**

Exactly two pytest functions were added to the existing Adapter test file.
They are collected and unskipped. No production/config/smoke-package file was
edited. The smoke source manifest assertion reached and passed with version
exactly `0.0.0-placeholder`.

## Requirement evidence

| Requirement | Exact evidence | Result |
|---|---|---|
| C4-R33: cross-umask deterministic npm pack, exact hashes/modes, no executable, no umask leak | `test_build_is_deterministic_across_process_umasks_and_normalizes_modes` runs real isolated builds under `022` and `077`; compares exact tar bytes, byte sizes, SHA-256/SHA-512 values and exact-byte bindings, staged directory/regular modes, packed member types/modes, executable bits, and restoration after each mask. | **BLOCKED:** `077` paths/members retain restrictive modes and change the tar bytes/hashes. Both exact-byte hash bindings, both no-executable assertions, and both umask-restoration checks pass. |
| C4-R34: reject nonzero ordinary-member alignment padding | `test_artifact_contents_rejects_nonzero_member_alignment_padding` changes the first alignment byte after non-final `package/dist/index.js` to `0xA5`, proves the next header boundary, unchanged ordinary member contents, and an untouched all-zero final trailer of at least two blocks, then requires `ValueError("invalid npm tarball")`. | **BLOCKED:** qualification returns successfully; no `ValueError` is raised. |
| C4-R35: append-only/scope preservation | Test code changed only `tests/adapters/test_node.py`; this run appended delimited sections to `.testagent/research.md`, `plan.md`, and `status.md`. Scoped status still shows the unrelated specialized processor only as its pre-existing `M` entry. | **PASS.** |

## Exact blocker evidence

### Umask `022` versus `077`

| Evidence | `022` | `077` |
|---|---|---|
| Tar byte size | `983` | `985` |
| SHA-256 | `sha256:0e615dbe7cf23a5192d9565518ff741784a0092df23d3433bee9b4eb52c818dd` | `sha256:84c371fb4c834c06a1fe156b207a7c24ce05114ab5423cee2adc2b10ae8041f3` |
| SHA-512 | `sha512:2603bbdd033d01a08ecd1dce293506a6821975b2b25f5d94a183702557cc0ae3fb3e057a144d8fadbfaf69ac0d95b1f1f4ba035cb27c1c2d2ef2069b004530d4` | `sha512:d2c3b65563ad1edf4f36f609d487a796efdc7a3b6f8a94c7d2046fa4a93438e3a8451a8605fc248ea82cd8197f2d6d316f338a01bdeab1c9305fec42d6d20b6b` |
| Staged directories | all `0755` | all `0700`, expected `0755` |
| Staged regular files | all `0644` | all `0600`, expected `0644` |
| Four packed regular members | all `0644` | all `0600`, expected `0644` |
| Packed executable members | none | none |
| Original process umask restored | yes | yes |

The exact SHA-256 and SHA-512 manifest values bind correctly to each build's
own tar bytes; the blocker is cross-umask inequality. The affected isolated
staging observations cover the stage root, build output, declared `scripts`
and `src` directories, witness directory, README, built output, build script,
staged manifest, source, and witness.

### Tar alignment padding

The selected ordinary member is `package/dist/index.js`. Its content ends
before the next 512-byte boundary, and that boundary is the next member header.
The test changes only the first byte of that alignment region to `0xA5`.
Python's tar reader still returns the exact same four ordinary member contents,
and the final trailer remains all zero. Current `_read_tarball` checks the
trailer after the maximum data end but not each member's alignment bytes, so
the malformed archive is accepted.

## Validation commands and results

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'deterministic_across_process_umasks or nonzero_member_alignment_padding'` | Exit 0; both exact tests collected, 82 deselected. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'deterministic_across_process_umasks or nonzero_member_alignment_padding'` | Exit 1; 2 failed, 82 deselected in 6.59s, with the two production blockers above. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'build_is_deterministic_and_preserves_source_checkout or artifact_contents_accepts_exact_tarball or artifact_contents_rejects_explicit_directory_member or artifact_contents_rejects_suffix_smuggling or artifact_contents_rejects_concatenated_tar_archive or artifact_contents_rejects_malformed_or_premature_streams'` | Exit 0; 9 passed, 75 deselected in 6.46s. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 1; 82 passed, only the 2 new blocking regressions failed, in 27.88s. |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; all checks passed. |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; already formatted. |
| `git diff --check -- .testagent/research.md .testagent/plan.md .testagent/status.md` | Exit 0. |

## Pre-completion gate

- **Pseudo-mutation review (`test-gap-analysis`)**: no uncovered requested
  mutation remains in the two added tests. Cross-umask byte/hash drift,
  missing mode normalization for either directories or regular files,
  executable-bit introduction, placeholder-version drift, and omitted umask
  restoration all change concrete assertions. For tar parsing, removal or
  off-by-one placement of a per-member alignment-padding check is exposed
  while unchanged member contents and the independently zero final trailer
  rule out adjacent failure causes.
- **Assertion-depth review (`assertion-quality`)**: neither test is
  assertion-free, trivial-only, or tautological. The cross-umask test combines
  concrete equality, deep structural mode/hash evidence, negative executable
  assertions, and process-state restoration. The padding test combines exact
  position/boundary assertions, unchanged-content evidence, final-trailer
  evidence, and an exception assertion.
- **Prompt-scenario mapping**: C4-R33 maps only to the exact cross-umask test
  and exercises both `022` and `077`; C4-R34 maps only to the exact
  ordinary-member-padding test and does not substitute final-trailer
  corruption.
- The requested `test-analysis-extensions` helper was unavailable; analysis
  used existing pytest conventions and direct bounded review as instructed.

## Changed files for this run

- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- `.testagent/research.md` (append-only)
- `.testagent/plan.md` (append-only)
- `.testagent/status.md` (this append-only section)

The unrelated
`src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`
was not opened or edited. No production source was edited, no tracked file was
deleted/restored/reverted/reset/cleaned, and no commit was created.

<!-- END RUN: workflow-delivery-v3-commit4-umask-padding-regressions-status-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-commit4-production-fixes-status-2026-08-10 -->

# Commit-4 Production Fix Status

**STATUS: COMPLETE. No blockers.**

| Requirement | Implementation and evidence | Result |
|---|---|---|
| C4-R33 | `build_node_package` invokes `_normalize_staging_modes(staging_root)` after the build and before `npm pack`. Only the isolated staging root is traversed; directories become `0755`, regular files become `0644`, and symlinks/non-regular nodes fail closed. The cross-umask regression verifies identical tar bytes and hashes, normalized staged/packed modes, no executables, and restored caller umask. | PASS |
| C4-R34 | `_read_tarball` checks every parsed member's bytes from `offset_data + size` through the next 512-byte boundary and rejects any nonzero byte before final-trailer validation. | PASS |
| C4-R35 | Production edits are limited to the Node Adapter; this research/plan/status evidence is append-only. The existing smoke and test-agent changes were preserved. | PASS |

## Exact validation

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'deterministic_across_process_umasks or nonzero_member_alignment_padding'` | Exit 0; 2 passed, 82 deselected in 6.14s. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; 84 passed in 27.59s. |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; all checks passed. |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; all 3 files already formatted. |
| `uv run --python 3.13 pyrefly check` | Exit 0; 0 errors, with existing suppressed warnings. |
| `git diff --check` | Exit 0. |
| `python -c 'import json, pathlib; p=pathlib.Path("src/public/lib/hcoona-release-smoke-npm/package.json"); print(json.loads(p.read_text())["version"])'` | Exit 0; `0.0.0-placeholder`. |
| `sha256sum src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py` | Exit 0; unchanged `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`. |

## Files changed by this production-fix run

- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py`
- `.testagent/research.md` (append-only)
- `.testagent/plan.md` (append-only)
- `.testagent/status.md` (append-only)

The pre-existing Adapter regressions, smoke package changes, smoke version, and
unrelated specialized-processor diff were preserved. No commit was created.

<!-- END RUN: workflow-delivery-v3-commit4-production-fixes-status-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-hidden-physical-tar-padding-regressions-status-2026-08-10 -->

# Hidden Physical Tar-Extension Padding Regression Status

**STATUS: EXPECTED FAILING REGRESSIONS RETAINED. Production blocker confirmed.**

The focused Research → Plan → Implement pass added one parameterized canonical
Adapter regression with three collected variants:

- `test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding[gnu-long-name]`
- `test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding[pax-extended]`
- `test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding[pax-global]`

Each variant inserts a valid hidden physical record immediately before
`package/dist/index.js`, confirms the valid zero-padded form is accepted and
hash-bound, mutates one padding byte at both the first parser-ignored and final
padding positions, confirms `tarfile.getmembers()` still returns the exact
original logical names and contents, confirms the final trailer remains all
zero, and requires the anchored diagnostic
`ValueError("^invalid npm tarball$")`.

## Exact focused pytest results

Only the exact generated parameterized pytest node was run. No full file,
project, or workspace test selection was executed.

| Attempt and exact command | Result |
|---|---|
| Initial fixture: `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding` | Exit 1; 3 failed in 2.43s. GNU reached strict validation and failed with `DID NOT RAISE`. PAX `x` and `g` changed their first padding byte, which Python treats as the end-of-record zero sentinel, so both failed earlier with `tarfile.ReadError: invalid header`. |
| Parser-aware fixture: same exact command | Exit 1; 3 failed in 2.26s. All GNU `L`, local PAX `x`, and global PAX `g` cases reached the strict assertion and failed with `DID NOT RAISE <class 'ValueError'>`. |
| Final boundary-strengthened fixture: same exact command | Exit 1; 3 failed in 2.35s. All three final generated variants failed only at `test_node.py:1899`: `DID NOT RAISE <class 'ValueError'>`. |

The final failures are the intended regression signal. Tests remain collected,
ordinary, and unmarked: no skip or xfail was added. Production currently
accepts nonzero alignment padding in all three hidden physical extension
record types.

## Mandatory pre-completion gate

- `test-gap-analysis` was invoked. Its requested
  `test-analysis-extensions` helper was also attempted and was unavailable, so
  the focused Python pseudo-mutation review used repository pytest conventions.
  The final cases kill omission of physical-record scanning, omission of any
  one of `L`/`x`/`g`, removal or message drift of the strict exception, and
  start/end off-by-one scans by mutating both the first parser-ignored and last
  padding positions. The current production mutation-equivalent behavior
  (logical `getmembers()` scanning only) survives and is exactly the reported
  blocker. No additional requested test gap remains.
- `assertion-quality` was invoked. Its language-extension helper was
  unavailable as above. The generated test has no assertion-free,
  trivial-only, or tautological variant. It combines concrete equality,
  comparison, Boolean/negative, structural collection, hash/size binding, and
  anchored exception assertions. Secondary observables include the valid
  extension archive manifest, exact logical name/content closure, hidden
  physical type, one-byte-only mutation, and independently zero final trailer.
- Prompt-scenario coverage was checked against C4-R36 through C4-R44. GNU,
  local PAX, and global PAX are dedicated parameter IDs; both parser-ignored
  padding boundaries are exercised for every physical variant.

## Requirement evidence

| Requirement | Evidence | Result |
|---|---|---|
| C4-R36 / GNU long-name | `test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding[gnu-long-name]` constructs physical typeflag `L` and tests its first and final padding positions. | Regression added; expected failure exposes production acceptance. |
| C4-R37 / per-file PAX | `test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding[pax-extended]` constructs physical typeflag `x`, preserves the parser sentinel, and tests the second and final padding bytes. | Regression added; expected failure exposes production acceptance. |
| C4-R38 / all feasible named PAX variants | The `pax-extended` (`x`) case plus `test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding[pax-global]` (`g`) cover both Python-supported PAX physical variants. | Regression added; both expected failures expose production acceptance. |
| C4-R39 / strict rejection | All three exact test IDs require `ValueError("^invalid npm tarball$")`; final pytest output is `DID NOT RAISE` for each. | **BLOCKED by current production behavior.** |
| C4-R40 / concrete non-vacuous diagnostic | Exact typeflag/size/bounds, valid zero-padding acceptance, exact hash and byte size, one-byte-only mutations, unchanged logical names/contents, hidden type, zero final trailer, and anchored exception checks are in the generated test. | Covered by test; diagnostic expectation currently fails as intended. |
| C4-R41 / canonical conventions | Test appended to `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` and reuses `built_result`, `_tar_entries`, in-memory gzip/tar mutation, and `pytest.raises`. | Covered. |
| C4-R42 / protected processor | Baseline and final SHA-256 are both `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`. Its pre-existing one-line working-tree diff is unchanged. | Covered; byte-identical to run baseline. |
| C4-R43 / smoke version | `src/public/lib/hcoona-release-smoke-npm/package.json` baseline/final SHA-256 is `a7d84bac91fe5f9fa7ccfbf46cd065cd85ded95188046d96f6f2c9ce97775566`; parsed version is exactly `0.0.0-placeholder`. | Covered; unchanged. |
| C4-R44 / preservation and focused scope | Adapter production `node.py` baseline/final SHA-256 is `e1fd61081b7d7221476bbcc9971c62288dc0f21740c1627743a0b16beb322a62`; only the exact generated pytest node was selected. No production edit, skip/xfail, commit, restore/reset/clean/stash, tracked deletion, or broad test command occurred. | Covered. |

## Exact files changed by this run

- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
  (appended focused parameterized regression to the existing authoritative
  untracked Adapter test file)
- `.testagent/research.md` (append-only)
- `.testagent/plan.md` (append-only)
- `.testagent/status.md` (this append-only section)

No production or smoke-package file was edited. All unrelated modified and
untracked user work shown by the initial status remains present. No commit was
created.

<!-- END RUN: workflow-delivery-v3-hidden-physical-tar-padding-regressions-status-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-physical-tar-padding-fix-status-2026-08-10 -->

# Physical Tar Padding Fix Status

**STATUS: COMPLETE. No blockers.**

## Implementation

- `_validate_physical_tar_stream` now walks every physical tar header in the
  uncompressed payload and validates its declared alignment padding directly.
- PAX payload framing is parsed before acceptance. PAX `size`, GNU sparse PAX
  metadata, and old GNU sparse members are rejected so `tarfile` cannot select
  a competing physical traversal.
- The logical `tarfile.getmembers()` pass remains responsible for the existing
  unique-regular-file closure, extraction, allowlist, manifest, and witness
  validation. Its ordinary-member padding check is retained.

## Focused regression evidence

- `test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding`
  covers GNU `L`, local PAX `x`, and global PAX `g`; every valid zero-padded
  form is accepted before first/final padding-byte mutations are rejected.
- `test_artifact_contents_rejects_pax_size_traversal_ambiguity` covers both
  shrinking and expanding PAX `size` interpretations around a hidden GNU
  extension and requires strict rejection.

## Exact validation

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'hidden_physical_extension_padding or pax_size_traversal_ambiguity'` | Exit 0; 5 passed, 84 deselected in 2.07s. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters` | Exit 0; 89 passed in 27.50s. |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; all checks passed. |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; all 3 files already formatted. |
| `uv run --python 3.13 pyrefly check` | Exit 0; 0 errors, 36 suppressed, 123 warnings not shown. |
| `git diff --check` | Exit 0. |
| Smoke manifest JSON version check | Exit 0; `0.0.0-placeholder`. |
| `sha256sum src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py` | Exit 0; unchanged `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`. |

## Test-quality gate

- Pseudo-mutation review found the focused cases kill omission of physical
  scanning, omission of any `L`/`x`/`g` record, first/last padding off-by-one
  errors, and acceptance of shrinking or expanding PAX-size ambiguity.
- Assertion review found no assertion-free, trivial-only, or tautological
  focused case. The tests combine structural/equality, boundary, negative,
  hash/size, unchanged-content, and anchored exception assertions.
- The requested `test-analysis-extensions` helper was attempted but is not
  installed; repository pytest conventions were used directly.

## Requirement evidence

| Requirement | Evidence |
|---|---|
| Strict physical padding for hidden GNU/PAX records | `test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding[gnu-long-name]`, `[pax-extended]`, and `[pax-global]` |
| No PAX-size traversal smuggling | `test_artifact_contents_rejects_pax_size_traversal_ambiguity[pax-size-shrink]` and `[pax-size-expand]` |
| Existing allowlist/content validation preserved | `_read_tarball` retains the logical unique regular-file extraction and downstream manifest/witness validation |
| Protected user file preserved | Baseline/final SHA-256 above is identical |
| Smoke version preserved | Exact JSON value `0.0.0-placeholder` |
| No commit | No commit command was run |

## Files changed by this fix

- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py`
- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- `.testagent/research.md` (append-only)
- `.testagent/plan.md` (append-only)
- `.testagent/status.md` (append-only)

The pre-existing smoke-package and specialized-processor working-tree changes
were not edited. No commit was created.

<!-- END RUN: workflow-delivery-v3-physical-tar-padding-fix-status-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-pax-physical-closure-regressions-status-2026-08-10 -->

# Adjudicated PAX Physical-Closure Regression Status

## Implementation

Added one parameterized regression with two exact pytest nodes:

- `test_artifact_contents_rejects_nonzero_suffix_inside_declared_pax_payload[pax-local]`
- `test_artifact_contents_rejects_nonzero_suffix_inside_declared_pax_payload[pax-global]`

Both fixtures begin with one complete length-prefixed PAX record, enlarge the
physical header's declared size by two bytes, and place `NUL`/`0xA5` inside
that declared range. Concrete assertions prove the following TAR
block-alignment padding is still all zero and Python's logical member
names/content remain identical to the original accepted artifact.

## Exact validation

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_nonzero_suffix_inside_declared_pax_payload` | Exit 1; 2 failed in 2.34s. Both `[pax-local]` and `[pax-global]` reached the final strict assertion and failed with `Failed: DID NOT RAISE <class 'ValueError'>`. This is the expected pending parent production-validator fix; assertions were not weakened. |
| `uv build --package three-workflow-delivery-v3` | Exit 0; source distribution and wheel built successfully. |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; all checks passed. |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; 1 file already formatted. |
| `sha256sum src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py` | Final SHA-256 `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`, identical to the pre-edit baseline. |
| Smoke package version read | `0.0.0-placeholder`; unchanged. |

## Test-quality gate

- `test-gap-analysis` was invoked. Its requested
  `test-analysis-extensions` dependency is unavailable, so the focused
  pseudo-mutation review was completed inline. The local/global cases kill
  early termination at `NUL`, omission of validation for either PAX physical
  type, exclusion of the nonzero byte from the declared-size range, and
  removal of strict qualification rejection. Existing dedicated padding tests
  remain responsible for mutations in alignment-padding validation.
- `assertion-quality` was invoked. Its same extension dependency is
  unavailable, so pytest assertions were classified inline. There are no
  assertion-free, trivial-only, or tautological generated cases. Each case
  combines header/type/size equality, exact suffix equality, boundary and
  zero-padding checks, logical structural/content equality, negative physical
  type checks, and an anchored exception assertion.
- Prompt-scenario review maps both requested local/global variants to exact
  parameter IDs and confirms the declared suffix and alignment padding are
  asserted as disjoint ranges.

## Requirement coverage

| Requirement | Evidence |
|---|---|
| C4-R45 local PAX case | `test_artifact_contents_rejects_nonzero_suffix_inside_declared_pax_payload[pax-local]` |
| C4-R46 global PAX case | `test_artifact_contents_rejects_nonzero_suffix_inside_declared_pax_payload[pax-global]` |
| C4-R47 separate alignment concern | Exact `NUL`/`0xA5` equality inside `[payload_start, payload_end)` plus independent all-zero `[payload_end, padding_end)` assertion |
| C4-R48 Adapter conventions | Canonical `tests/adapters/test_node.py`, `built_result`, existing physical-extension helpers, `_tar_entries`, and anchored `pytest.raises` |
| C4-R49 narrow pytest | Exact two-case test-node command/result above |
| C4-R50 evidence preservation | Appended delimited sections in all three `.testagent` Markdown files |
| C4-R51 specialized processor | Identical before/final SHA-256 above; file was not edited |
| C4-R52 no commit | No commit command was run |
| C4-R53 placeholders | Smoke version remains `0.0.0-placeholder`; no package/version file was edited |
| C4-R54 tests/evidence only | New code is confined to `test_node.py`; production remains pending and both strict regressions intentionally fail |
| C4-R55 authoritative workspace | No restore, checkout, reset, clean, deletion, or reconstruction command was run |

## Files changed by this focused run

- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- `.testagent/research.md` (append-only)
- `.testagent/plan.md` (append-only)
- `.testagent/status.md` (append-only)

No production source, specialized processor, package/version manifest, or
unrelated existing working-tree path was edited. No commit was created.

<!-- END RUN: workflow-delivery-v3-pax-physical-closure-regressions-status-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-pax-physical-closure-fix-status-2026-08-10 -->

# Adjudicated PAX Physical-Closure Fix Status

**STATUS: COMPLETE. No blockers.**

## Production correction

`_validate_pax_payload` now continues until `position == len(content)` instead
of treating an in-payload NUL byte as an end sentinel. Every byte in the TAR
header's declared PAX payload must therefore belong to a valid
length-prefixed record. `_validate_physical_tar_stream` retains its separate
zero check for bytes between the declared payload end and the next 512-byte
TAR boundary.

## Exact validation

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_nonzero_suffix_inside_declared_pax_payload` | Exit 0; 2 passed in 2.06s. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; 91 passed in 27.59s. |
| `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; all checks passed. |
| `uv run --python 3.13 ruff format --force-exclude --check -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; 2 files already formatted. |
| `uv run --python 3.13 pyrefly check` | Exit 0; 0 errors, 36 suppressed, 123 warnings not shown. |
| `git diff --check` | Exit 0. |
| Protected-file SHA-256 and smoke-version verification | Both hashes matched the run baselines; smoke version was exactly `0.0.0-placeholder`. |

## Focused test-quality review

- Pseudo-mutation review confirms the two new parameter cases kill restoration
  of the early-NUL loop condition. The existing valid local/global PAX
  extension cases kill changing the loop boundary to parse beyond the declared
  payload, and the dedicated hidden-extension-padding cases retain independent
  coverage of alignment padding. No focused mutation gap remains.
- Assertion review found no assertion-free, trivial-only, or tautological
  generated case. Each parameter case verifies physical type and declared
  size, exact in-payload suffix bytes, the disjoint all-zero alignment range,
  preserved logical names/content, absence of the hidden physical type, and
  the exact qualification exception.
- The requested `test-analysis-extensions` helper was unavailable; both
  reviews used the repository's existing pytest conventions directly.

## Preservation evidence

- `src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`
  remains byte-identical to the run baseline:
  `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`.
- `src/public/lib/hcoona-release-smoke-npm/package.json` remains byte-identical
  to the run baseline:
  `a7d84bac91fe5f9fa7ccfbf46cd065cd85ded95188046d96f6f2c9ce97775566`.
  Its version remains `0.0.0-placeholder`.
- No commit, restore, checkout, reset, clean, stash, or destructive Git command
  was run.

<!-- END RUN: workflow-delivery-v3-pax-physical-closure-fix-status-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-robust-first-slice-tar-profile-status-2026-08-10 -->

# Robust First-Slice TAR Physical Profile Status

**Strategy: Direct. STATUS: TEST WORK COMPLETE; BLOCKED BY READ-ONLY
PRODUCTION VALIDATOR.**

This bounded run changed only the canonical Adapter pytest file and appended
evidence to the three existing `.testagent` files. No production/configuration
file was edited.

## Exact test changes

### Added

Eight focused pytest functions add 57 collected parameter cases:

| Test | Cases | Concrete evidence |
|---|---:|---|
| `test_artifact_contents_accepts_actual_frozen_npm_pack_ustar_profile` | 1 | Real `npm pack` artifact, exact four headers, type `0`, USTAR magic/version, all field encodings, padding/trailer, manifest, SHA-256, and SHA-512. |
| `test_artifact_contents_rejects_gnu_long_name_or_long_link_header` | 2 | Well-formed zero-padded GNU long-name `L` and long-link `K` records preserve the logical file closure but must fail raw physical qualification. |
| `test_artifact_contents_rejects_pax_physical_header` | 3 | Well-formed local `x`, global `g`, and Solaris `X` PAX records preserve logical entries but must fail. |
| `test_artifact_contents_rejects_noncanonical_ustar_magic_or_version` | 4 | GNU magic/version, v7 zero magic/version, space magic, and unsupported version `01`. |
| `test_artifact_contents_rejects_nonzero_suffix_after_nul_in_fixed_string_field` | 5 | Exact `name`, `linkname`, `uname`, `gname`, and `prefix` cases retain the first NUL and change only a hidden suffix byte. |
| `test_artifact_contents_rejects_noncanonical_unused_header_field` | 9 | UID/GID octal-zero and hidden suffixes, reserved suffix, and device nonzero/all-NUL substitutions. |
| `test_artifact_contents_rejects_noncanonical_numeric_header_encoding` | 19 | Alternate width/terminator, hidden suffix, GNU base-256, and checksum variants for mode, UID/GID, size, mtime, and device fields while parsed values/logical entries remain equal. |
| `test_artifact_contents_rejects_every_nonordinary_tar_type` | 14 | Old regular NUL, hard/symbolic links, character/block devices, directory, FIFO, contiguous, GNU `D/M/N/S/V`, and unknown `?`; exact raw-profile rejection is required. |

### Removed as dead under the strict profile

Only these obsolete extension-validator tests were removed from the existing
canonical file:

- `test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding`
  (3 cases);
- `test_artifact_contents_rejects_nonzero_suffix_inside_declared_pax_payload`
  (2 cases);
- `test_artifact_contents_rejects_pax_size_traversal_ambiguity` (2 cases).

Their now-unused `_pax_prefix_with_nonzero_declared_suffix` and
`_tar_header_with_size` helpers were removed. The former positive qualification
of valid GNU/PAX extension archives was inverted: the new GNU/PAX tests require
those same logically equivalent, well-formed extension archives to fail.

All unrelated gzip/full-consumption, member-padding, final-trailer, allowlist,
manifest, witness, deterministic-byte, exact-hash, runtime, environment, and
quality tests remain present.

## Actual frozen npm-pack evidence

The real `built_result` build emitted:

- gzip byte size: `983`;
- SHA-256:
  `0e615dbe7cf23a5192d9565518ff741784a0092df23d3433bee9b4eb52c818dd`;
- four physical headers in exact order:
  `package/dist/index.js`, `package/package.json`,
  `package/workflow-delivery/provenance.json`, `package/README.md`;
- every typeflag `b"0"`;
- every magic/version `b"ustar\0"` / `b"00"`;
- exact npm field forms recorded in research and asserted by the positive
  profile test;
- exactly two zero trailer blocks.

The positive profile test passes and returns the exact existing artifact
manifest with SHA-256/SHA-512 bound to the input bytes.

## Exact validation

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q --tb=no src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'actual_frozen_npm_pack_ustar_profile or gnu_long_name_or_long_link_header or pax_physical_header or every_nonordinary_tar_type or noncanonical_ustar_magic_or_version or nonzero_suffix_after_nul_in_fixed_string_field or noncanonical_unused_header_field or noncanonical_numeric_header_encoding'` | Exit 1; **2 passed, 55 failed, 84 deselected** in 2.37s. The passing cases are the exact frozen npm profile and existing raw GNU-sparse rejection. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q --tb=no src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 1; **86 passed, 55 failed** in 27.07s. Every failure is one of the new robust-profile cases; all unrelated retained Adapter tests pass. |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; all checks passed. |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Exit 0; file already formatted. |
| `uv build --package three-workflow-delivery-v3` | Exit 0; sdist and wheel built. |
| `git --no-pager diff --check` | Exit 0. |
| Smoke manifest version read | `0.0.0-placeholder`. |
| Protected processor SHA-256 | `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`, identical to the retained pre-run baseline. |

A root `pnpm build` was not run because prior retained evidence proves that it
stamps tracked package versions. The scoped Python package build is the
appropriate non-mutating build validation under the explicit no-production-
edit constraint.

## Production blockers

Current `_validate_physical_tar_stream` checks stream traversal, PAX framing,
GNU sparse, and alignment padding, then delegates header semantics to
`tarfile.TarInfo.frombuf`. It does not enforce the adjudicated exact physical
profile:

1. Valid GNU `L`/`K` and PAX `x`/`g`/`X` records are accepted.
2. GNU/v7/alternate USTAR magic and wrong version are accepted.
3. `TarInfo` ignores nonzero fixed-string bytes after the first NUL.
4. UID/GID, reserved, and device fields are not compared with the frozen npm
   byte forms.
5. Alternate-width, alternate-terminator, hidden-suffix, and GNU base-256
   numeric encodings are accepted when parser semantics match.
6. GNU sparse `S` is rejected at the raw validator and passes its new case.
   Other special types are rejected only later by logical regular-file or
   allowlist checks, so their cases correctly fail the required raw
   `ValueError("^invalid npm tarball$")` boundary.

Tests remain ordinary and unskipped. No assertion was weakened to accommodate
the read-only production behavior.

## Mandatory pre-completion gate

`test-gap-analysis` and `assertion-quality` were invoked against the final
source/test pair. Their required `test-analysis-extensions` discovery helper
was attempted and is unavailable, so the bounded Python/pytest review used
repository conventions directly.

The pseudo-mutation pass initially identified missing equal-value GNU
base-256 numeric encodings, checksum hidden suffix, UID/GID hidden suffixes,
and an unknown typeflag. Those cases were added before final validation.
The final matrices kill omission of any GNU/PAX physical type, accepting an
alternate typeflag, magic/version weakening, first-NUL early termination,
partial unused-field closure, semantic-only numeric parsing, alternate
checksum termination, base-256 acceptance, and off-by-one suffix scans. The
positive frozen-artifact test kills over-rejection and drift from actual npm
bytes. No remaining in-scope test gap was found.

Assertion review over the eight generated functions found 61 bare concrete
assertions plus seven anchored `pytest.raises` assertions. Every negative
function first proves its physical mutation and relevant logical/semantic
equivalence or structural shape before asserting rejection. Categories include
equality, Boolean, comparison, negative, collection/structural, hash/state, and
exception assertions. There are no assertion-free, trivial-only,
self-referential, or tautological generated tests.

## Requirement coverage

| Requirement | Evidence |
|---|---|
| TAR-R1 | `test_artifact_contents_accepts_actual_frozen_npm_pack_ustar_profile` plus all seven negative test groups. |
| TAR-R2 | GNU `L/K`, PAX `x/g/X`, and 14-case nonordinary/special type tests; current production blocker recorded above. |
| TAR-R3 | Four cases of `test_artifact_contents_rejects_noncanonical_ustar_magic_or_version`. |
| TAR-R4 | Exact positive name filling and five cases of `test_artifact_contents_rejects_nonzero_suffix_after_nul_in_fixed_string_field`. |
| TAR-R5 | Positive exact unused bytes, five hidden string-field cases, and nine unused UID/GID/reserved/device cases. |
| TAR-R6 | Nineteen canonical numeric/checksum cases covering alternate widths, terminators, hidden bytes, and GNU base-256. |
| TAR-R7 | Full target result shows all 86 unrelated retained cases pass; strict stream/padding/trailer/allowlist/manifest/witness/hash tests were not removed. |
| TAR-R8 | Former valid-extension acceptance was removed and replaced with five exact GNU/PAX rejection cases. |
| TAR-R9 | Exact parameter IDs `gnu-long-name-L`, `gnu-long-link-K`, `pax-local-x`, `pax-global-g`, `pax-solaris-X`, and `name/linkname/uname/gname/prefix`. |
| TAR-R10 | Three obsolete extension-validator test functions (7 cases) and two now-unused helpers removed in-place; the canonical file itself was preserved. |
| TAR-R11 | Intentional files are only the canonical pytest plus append-only `.testagent/{research,plan,status}.md`; protected processor hash is unchanged and no production file was edited. |
| TAR-R12 | Narrow/full results, build/style/diff checks, exact blockers, and changed-test inventory are recorded above. |

## Append-only and scope evidence

Before this section, `.testagent/status.md` was exactly 96,213 bytes with
SHA-256
`b8844835f2bb0c0edbbf57915a3618ce66d69ac5bbe236ee678342991c34ace9`.
That prefix is retained byte-for-byte.

Intentional files for this run:

- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`;
- `.testagent/research.md` (append-only);
- `.testagent/plan.md` (append-only);
- `.testagent/status.md` (append-only).

The pre-existing modified smoke manifest and specialized processor, untracked
Adapter production tree, smoke test, and all other unrelated working-tree
content were preserved. No commit, checkout, restore, reset, clean, stash,
tracked deletion, production edit, or broad mutating build occurred.

<!-- END RUN: workflow-delivery-v3-robust-first-slice-tar-profile-status-2026-08-10 -->

<!-- BEGIN CORRECTION: workflow-delivery-v3-robust-tar-retained-count-2026-08-10 -->

The TAR-R7 row above should read: all **84 retained** cases pass. The full-file
total of 86 passing cases consists of those 84 retained cases plus two new
passing cases (the exact frozen npm profile and GNU sparse raw rejection).

<!-- END CORRECTION: workflow-delivery-v3-robust-tar-retained-count-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-robust-tar-gate-remediation-status-2026-08-10 -->

## Gate-remediation outcome

Status: **test-only follow-up complete, with expected production-profile
blockers retained**.

Intentional edit set for this phase:

- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`;
- `.testagent/research.md` (append-only);
- `.testagent/plan.md` (append-only);
- `.testagent/status.md` (append-only).

No production/config/package file was edited.

## Changed test functions and parameter cases

| Test function | Case change |
|---|---|
| `_tarball_with_first_header_fields` | Now delegates to new member-index helper while preserving first-header callers. |
| `_tarball_with_member_header_fields` | New helper targeting any physical member header by index and recomputing that member checksum. |
| `_tar_member_observables` | New helper capturing member index/name, parsed structural fields, and raw offsets for pre-rejection equivalence evidence. |
| `test_artifact_contents_rejects_later_member_ustar_profile_mutations` | New 6-case matrix: 3 later member indexes (`package/package.json`, `package/workflow-delivery/provenance.json`, `package/README.md`) x 2 mutations (`mode-alt-terminator`, `noncanonical-magic`). |
| `test_artifact_contents_rejects_nonzero_suffix_after_nul_in_fixed_string_field` | Expanded from 5 to 10 cases: every fixed string field (`name`, `linkname`, `uname`, `gname`, `prefix`) now covers `after-first-nul` and `final` suffix positions. |
| `test_artifact_contents_rejects_noncanonical_numeric_header_encoding` | Expanded from 19 to 34 cases. Added equal-value alternate terminator/hidden-byte forms for `mode`, `uid`, `gid`, `size`, `mtime`, checksum, and supported device fields (`devmajor`, `devminor`). |

## Exact case counts

| Matrix | Count before | Count after | Added |
|---|---:|---:|---:|
| Later-member profile mutations | 0 | 6 | 6 |
| Fixed-string suffix mutations | 5 | 10 | 5 |
| Numeric/checksum canonical encoding mutations | 19 | 34 | 15 |
| Total focused gate-remediation cases | 24 | 50 | 26 |

The positive frozen npm-pack profile remains unchanged and still asserts the
actual four-member frozen artifact, byte size 983, and SHA-256
`0e615dbe7cf23a5192d9565518ff741784a0092df23d3433bee9b4eb52c818dd`.

## Requirement evidence

| Requirement | Evidence |
|---|---|
| Later-member profile coverage | `test_artifact_contents_rejects_later_member_ustar_profile_mutations` asserts concrete member indexes 1, 2, and 3, exact member names, unchanged parser observables, unchanged logical entries, and raw qualification rejection. |
| Fixed-string immediate and final suffix bytes | `test_artifact_contents_rejects_nonzero_suffix_after_nul_in_fixed_string_field` now asserts first-NUL location, nonzero mutation at `after-first-nul` or `final`, unchanged parser observables, unchanged logical entries, and raw qualification rejection for all five fields. |
| Canonical numeric/checksum breadth | `test_artifact_contents_rejects_noncanonical_numeric_header_encoding` now has 34 cases covering equal-value alternate `NUL+space`/space terminators where applicable, immediate hidden bytes for zero UID/GID fields, checksum space termination, GNU base-256, and supported device fields. |
| Positive artifact unaffected | `test_artifact_contents_accepts_actual_frozen_npm_pack_ustar_profile` remains the positive exact frozen-output oracle. |
| Production read-only | No production/config/package file was edited; `node.py` and `specialized_processor.py` remained read-only/out of scope. |

## Pre-completion review results

`test-gap-analysis` and `assertion-quality` were invoked. Their requested
`test-analysis-extensions` helper is not available in this workspace, so the
Python/pytest review was completed inline.

Pseudo-mutation review:

- A validator that inspects only member 0 is killed by the new 6-case
  later-member matrix.
- A fixed-string validator that checks only one suffix byte is killed by the
  two-position five-field matrix.
- A numeric validator that accepts equal-value alternate terminators or scans
  only a final hidden byte is targeted by the expanded 34-case matrix.

Assertion-quality review:

- The added tests are not assertion-free and not trivial-only.
- Assertions include exact byte checks, concrete member identity/index checks,
  parser-visible structural equivalence, logical-entry equivalence, and
  negative `pytest.raises` assertions.

## Validation commands and results

1. `uv run --no-sync ruff check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
   - Result: **passed**, `All checks passed!`
2. `uv run --no-sync ruff format --check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
   - Result: **passed**, `1 file already formatted`
3. `uv run --no-sync pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_suffix_smuggling src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_concatenated_tar_archive src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_nonzero_member_alignment_padding src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_accepts_actual_frozen_npm_pack_ustar_profile src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_gnu_long_name_or_long_link_header src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_pax_physical_header src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_noncanonical_ustar_magic_or_version src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_later_member_ustar_profile_mutations src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_nonzero_suffix_after_nul_in_fixed_string_field src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_noncanonical_unused_header_field src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_noncanonical_numeric_header_encoding src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_every_nonordinary_tar_type src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_malformed_or_premature_streams`
   - Result: **expected blocker retained**, 9 passed / 81 failed.
   - Failing cases are the intentionally red robust physical-profile
     regressions against read-only production.
4. `uv run --no-sync pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py`
   - Result: **passed**, 47 passed.
5. `uv build --package three-workflow-delivery-v3 --wheel --out-dir /tmp/wdv3-build-20260810T223326 --no-create-gitignore --no-build-isolation --offline`
   - Result: **passed**, built
     `/tmp/wdv3-build-20260810T223326/three_workflow_delivery_v3-0.1.0-py3-none-any.whl`.
6. `uv run --no-sync pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
   - Result: **expected blocker retained**, 86 passed / 81 failed.
   - The 86 passing cases show unrelated retained tests still pass; failures
     are the intentional production-profile red regressions.
7. `uv run --no-sync pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_later_member_ustar_profile_mutations src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_nonzero_suffix_after_nul_in_fixed_string_field src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_noncanonical_numeric_header_encoding`
   - Result: **passed collection**, 50 tests collected.

## Explicit blockers

Production remains intentionally read-only. The robust physical-profile
validator still accepts the negative physical TAR variants, so the new and
existing red regression assertions fail until production validation is allowed
to reject noncanonical raw TAR headers/extension records.

<!-- END RUN: workflow-delivery-v3-robust-tar-gate-remediation-status-2026-08-10 -->
<!-- BEGIN APPEND: workflow-delivery-v3-robust-tar-final-gate-status-2026-08-10 -->

## Final gate follow-up status

The fixed-string hidden-suffix matrix now covers immediate, middle, and final
positions after the first NUL for `name`, `linkname`, `uname`, `gname`, and
`prefix` (15 cases total). This closes the independent review's remaining
in-scope suffix-position pseudo-mutation gap without changing production.
Final command results are reported by the orchestrating response.

<!-- END APPEND: workflow-delivery-v3-robust-tar-final-gate-status-2026-08-10 -->
<!-- BEGIN APPEND: workflow-delivery-v3-robust-tar-final-validation-2026-08-10 -->

## Final validation and pre-completion gate

- Focused nine-function robust-profile selection: exit 1; 2 passed, 86
  expected production-blocker failures, 84 deselected in 2.61s.
- Full canonical Adapter file: exit 1; 86 retained/passing tests and 86
  robust-profile production-blocker failures in 27.15s.
- Ruff check and format check: passed.
- Safe offline scoped wheel build: passed and wrote only to
  `/tmp/wdv3-build-final-20260810`.
- `test-gap-analysis` and `assertion-quality` were invoked after the final
  change. `test-analysis-extensions` was unavailable, so Python/pytest
  conventions were applied directly.
- Final pseudo-mutation result: no remaining in-scope gap. The matrices kill
  first-header-only validation, boundary/interior fixed-string suffix scans,
  type/magic/version weakening, unused-field relaxation, semantic-only octal
  parsing, alternate checksum termination, and base-256 acceptance. The
  frozen-artifact positive case kills over-rejection.
- Final assertion review: nine generated functions, 88 parameter cases, 71
  explicit bare assertion statements and eight anchored `pytest.raises`
  statements. No generated function is assertion-free, trivial-only,
  tautological, or missing a relevant secondary observable.
- Production remains intentionally unchanged. The red regressions accurately
  expose the missing raw 512-byte profile validator and were not skipped or
  weakened.

<!-- END APPEND: workflow-delivery-v3-robust-tar-final-validation-2026-08-10 -->

<!-- BEGIN APPEND: workflow-delivery-v3-robust-tar-adjudication-closure-status-2026-08-10 -->

## Final adjudication closure status

### Focused test set

- Nine focused test functions now collect **112 parameter cases**.
- Later-member profile coverage is **24 cases**: eight mutations across each
  of the three non-first physical headers.
- Fixed-string suffix coverage remains **15 cases**: immediate, middle, and
  final nonzero bytes after the first NUL for `name`, `linkname`, `uname`,
  `gname`, and `prefix`.
- Unused-field closure is **15 cases**, including nonempty unused string
  fields and first/middle/final reserved bytes.
- No generated test is assertion-free, trivial-only, or tautological. Static
  review found **76 bare assertions** and **8 anchored `pytest.raises`
  assertions** across the nine functions.

### Final commands and results

1. `uv run --no-sync pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'actual_frozen_npm_pack_ustar_profile or gnu_long_name_or_long_link_header or pax_physical_header or every_nonordinary_tar_type or noncanonical_ustar_magic_or_version or later_member_ustar_profile_mutations or nonzero_suffix_after_nul_in_fixed_string_field or noncanonical_unused_header_field or noncanonical_numeric_header_encoding'`
   - **112 collected, 84 deselected**.
2. `uv run --no-sync pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'actual_frozen_npm_pack_ustar_profile or gnu_long_name_or_long_link_header or pax_physical_header or every_nonordinary_tar_type or noncanonical_ustar_magic_or_version or later_member_ustar_profile_mutations or nonzero_suffix_after_nul_in_fixed_string_field or noncanonical_unused_header_field or noncanonical_numeric_header_encoding'`
   - Exit 1: **2 passed, 110 expected production-blocker failures, 84
     deselected** in 6.73s.
3. `uv run --no-sync pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
   - Exit 1: **86 passed, 110 expected production-blocker failures** in
     31.43s. All 84 unrelated retained cases pass; the other two passes are
     the frozen-artifact positive case and the already-enforced GNU sparse
     rejection.
4. `uv run --no-sync ruff check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
   - **Passed**.
5. `uv run --no-sync ruff format --check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
   - **Passed**, one file already formatted.
6. `git diff --check`
   - **Passed**.

The safe offline scoped wheel build had already passed after the focused
test-only implementation and production remained unchanged after the final
matrix expansions. A root build was not run because existing repository
evidence shows that it stamps tracked version files, which would violate the
user's test/evidence-only scope.

### Final pre-completion gate

- `test-gap-analysis` and `assertion-quality` were re-invoked after the final
  changes. `test-analysis-extensions` remains unavailable; the Python/pytest
  rules were applied directly.
- No remaining in-scope pseudo-mutation gap was found: the final tests kill
  first-header-only and later-header category weakening, boundary/interior
  fixed-string scans, nonempty unused strings, partial reserved-byte scans,
  type/magic/version relaxation, semantic-only numeric parsing, alternate
  checksum termination, and base-256 acceptance.
- Prompt-scenario mapping is complete across the frozen positive artifact,
  GNU `L/K`, PAX `x/g/X`, every nonordinary type, exact USTAR identity, fixed
  strings, unused fields, numeric/checksum encodings, and retained
  gzip/padding/trailer/allowlist/manifest/witness/hash coverage.

### Scope and blocker

The intentional edit set remains:

- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`;
- append-only `.testagent/research.md`;
- append-only `.testagent/plan.md`;
- append-only `.testagent/status.md`.

No production/config/package file was intentionally edited. The protected
`specialized_processor.py` SHA-256 remains
`91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`.
The 110 red cases are the requested production blocker: the current
read-only validator does not yet enforce the adjudicated raw 512-byte USTAR
profile before parser/logical validation.

<!-- END APPEND: workflow-delivery-v3-robust-tar-adjudication-closure-status-2026-08-10 -->

<!-- BEGIN APPEND: workflow-delivery-v3-robust-tar-production-implementation-status-2026-08-10 -->

## Robust TAR production implementation status

Status: **complete**.

### Requirement evidence

| Requirement | Evidence |
|---|---|
| Only frozen-profile ordinary regular USTAR headers qualify | `_validate_ustar_regular_file_header`; `test_artifact_contents_accepts_actual_frozen_npm_pack_ustar_profile`; GNU/PAX and 14-type rejection matrices |
| Validate raw headers before semantic TAR parsing | `_validate_physical_tar_stream` derives traversal size only from the raw validator; `tarfile.open` remains after complete physical validation in `_read_tarball` |
| Exact magic/version and canonical fixed strings | Magic/version, later-member, 15 fixed-string suffix, and 15 unused-field cases |
| Canonical numeric/checksum encodings | 34-case numeric/checksum matrix plus exact positive header assertions |
| Retain gzip, padding, trailer, allowlist, manifest, witness, and hashes | Full Adapter suite: 196 passed |
| Remove extension parsing | `_validate_pax_payload` and its length constants removed; GNU/PAX records are rejected by type without payload interpretation |
| Preserve placeholder and unrelated processor | Version `0.0.0-placeholder`; processor SHA-256 `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429` |

### Validation

| Command | Result |
|---|---|
| `cd src/public/lib/hcoona-release-smoke-npm && node --test` | Passed: 1 test |
| `uv run --no-sync pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Passed: 196 tests |
| `uv run --no-sync ruff check .../adapters/node.py .../tests/adapters/test_node.py` | Passed |
| `uv run --no-sync ruff format --check .../adapters/node.py .../tests/adapters/test_node.py` | Passed: 2 files formatted |
| `uv run --no-sync pyrefly check` | Passed: 0 errors |
| `git --no-pager diff --check` | Passed |

### Pre-completion review

`test-gap-analysis` and `assertion-quality` were invoked after implementation.
The nine focused functions cover 112 parameter cases with exact byte,
parser-observable, logical-entry, exception, negative, comparison, collection,
and structural assertions. No focused test is assertion-free, trivial-only,
self-referential, or tautological. The pseudo-mutation pass found no remaining
in-scope behavioral gap.

No commit was created.

<!-- END APPEND: workflow-delivery-v3-robust-tar-production-implementation-status-2026-08-10 -->

<!-- BEGIN APPEND: workflow-delivery-v3-robust-tar-checksum-order-review-status-2026-08-10 -->

## Raw checksum-order review closure

The independent review finding is resolved by
`test_artifact_contents_rejects_bad_checksum_before_tarfile_parse`. The
focused inventory is now ten functions and 113 parameter cases. The added test
proves a canonically encoded but incorrect checksum is rejected while a
monkeypatched `tarfile.open` sentinel remains uncalled. This replaces the
earlier statement that raw checksum arithmetic was only implementation-reviewed
and closes the last identified pseudo-mutation gap.

<!-- END APPEND: workflow-delivery-v3-robust-tar-checksum-order-review-status-2026-08-10 -->

<!-- BEGIN CORRECTION: workflow-delivery-v3-robust-tar-final-count-2026-08-10 -->

The final Adapter suite result supersedes the earlier 196-test result:
`uv run --no-sync pytest -q
src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` passed
**197 tests** after the checksum-order regression was added. Final Ruff
check/format, Pyrefly, and `git diff --check` also passed.

<!-- END CORRECTION: workflow-delivery-v3-robust-tar-final-count-2026-08-10 -->

<!-- BEGIN APPEND: workflow-delivery-v3-exact-tar-trailer-status-2026-08-10 -->

## Exact TAR trailer closure

The closed first-slice USTAR profile now requires exactly two zero trailer
blocks. `test_artifact_contents_rejects_extra_zero_trailer_block` proves that
an otherwise logically identical archive with a third zero block is rejected.
The final Adapter suite passed **198 tests**. Managed HK passed with **1,486
tests**, and root pytest passed with **3,521 tests**.

<!-- END APPEND: workflow-delivery-v3-exact-tar-trailer-status-2026-08-10 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit5-ci-status-2026-08-12 -->

## Workflow Delivery v3 Commit 5 Status

The cancelled oversized implementation was decomposed and salvaged into three
bounded scopes: CI model core, consumer-policy/HK gate, and workflow/CLI.
Nonfunctional custom artifact transport and unrelated Provider/lock changes
were removed.

Focused commit-5 validation currently passes 269 tests. Independent GPT-5.6
Sol reviews identified and closed partial-Plan admission, incomplete-model
planning, lane-specific npm artifact Evidence, SLO cohort, summary, empty-diff,
consumer-policy coverage, and workflow timing defects. Pseudo-mutation and
assertion-quality review added exact current-candidate admission, CLI output,
dependency-configuration, Repository Model identity, and Node global-input
regressions. `test-analysis-extensions` was unavailable, so pytest conventions
were applied directly.

<!-- END APPEND: workflow-delivery-v3-commit5-ci-status-2026-08-12 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit5-final-closure-2026-08-12 -->

## Workflow Delivery v3 Commit 5 Final Closure

Commit 5 passed the complete v3 suite with **1,838 tests**, the managed
`v3-control-pytest` HK gate with **1,838 tests**, and root pytest with
**3,873 tests**. The consumer-policy suite passed **148 tests**; the focused HK
trigger suite passed **50 tests**. Pyrefly, Ruff check/format, actionlint, Pkl
evaluation, lock checks, .NET build, JavaScript build, and diff integrity
passed.

Five successive GPT-5.6 Sol review rounds covered CI contracts and workflow
transport, consumer-policy/HK behavior, and holistic LLD compliance. Every
atomic finding was independently adjudicated as TP or FP. The final round
returned no findings from all three original reviewers.

<!-- END APPEND: workflow-delivery-v3-commit5-final-closure-2026-08-12 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit6-release-core-status -->

## Workflow Delivery v3 Commit 6 Release Core Status

Implementation and focused requirement coverage are complete. The exact
commit-6 suite passes 23 tests, Repository Model compiler tests pass 200 tests,
Ruff check/format pass, and Pyrefly reports 0 errors. Canonical JSON fixtures
contain exact RFC 8785 bytes without terminal whitespace.

### Requirement evidence

| Requirement | Concrete evidence |
|---|---|
| Canonical records and Repository Model admission | `test_canonical_intent_and_repository_model_fixtures`, `test_repository_model_admission_rejects_noncanonical_unknown_and_tampered`, `test_simulation_identity_requires_admitted_current_model` |
| Exact identities and immutable records | `test_release_records_are_exact_frozen_slotted_dataclasses`, `test_identity_field_order_and_live_identity_shapes_are_exact`, `test_simulation_identity_document_contains_no_live_identity` |
| Exact first-slice plan and DAG | `test_official_simulation_plan_is_the_exact_closed_first_slice`, `test_official_simulation_plan_has_exact_four_obligations_and_closed_dag` |
| Artifact/Evidence binding and success | `test_complete_qualification_succeeds_with_exact_artifact_binding`, `test_build_transport_rejects_prior_attempt_substitution` |
| Failure, missing, duplicate, and substituted Evidence | `test_build_adapter_failure_forms_failed_evidence_and_no_artifact`, `test_definitive_failure_continues_to_closed_failed_decision`, `test_missing_evidence_finalizes_incomplete_without_false_success`, `test_duplicate_evidence_is_rejected`, `test_cross_purpose_and_prior_attempt_evidence_are_rejected` |
| Observation, hypothetical action, and simulation boundary | `test_synthetic_absent_and_exact_observations_plan_actions_only`, `test_synthetic_observations_can_complete_simulation_report`, `test_unsupported_observation_finishes_truthful_incomplete_simulation` |
| Guarded Publication Snapshot | `test_publication_snapshot_guards_success_observation_and_artifacts` |
| No live authority/workflow/remote observation | Source scope and `test_simulation_identity_document_contains_no_live_identity`; no workflow, HK, policy, catalog, or docs paths changed |

### Validation

- `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q
  .../test_commit6_contracts.py .../test_commit6_qualification.py`:
  23 passed.
- `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q
  .../tests/repository/test_compiler.py`: 200 passed.
- `uv run --python 3.13 pyrefly check`: 0 errors.
- Scoped Ruff check and format check: passed.
- `git diff --check`: passed.
- Full package validation passed with 1,861 tests. One earlier full run hit
  a transient pnpm temporary-directory rename failure in the existing real
  Node-provider test; that test passed immediately in isolation, and the next
  full package run passed.

### Final test-gap and assertion review

Inline review was used because the user prohibited subagents. It identified
and closed three in-scope gaps: forged admitted Repository Model wrappers,
Publication Snapshot artifact/observation cardinality mismatches, and
`exact-satisfied` observations whose content or witness did not match the
desired artifact. The focused tests contain concrete outcome, digest,
cardinality, ordering, binding, and serialization assertions; no assertion-free
or truthiness-only commit-6 scenarios remain.

<!-- END APPEND: workflow-delivery-v3-commit6-release-core-status -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit6-core-correction -->

## Workflow Delivery v3 Commit 6 Core Correction

The focused correction is complete.

### API and contract changes

- `ReleaseIntent` now binds `workflow_ref` and `run_attempt`; Request IDs remain
  stable across reruns while Intent and Simulation identities change.
- Ready `RepositoryModelSnapshot` records now contain the complete immutable
  `CompiledReleasePolicy`; incomplete snapshots contain no compiled policy.
- `plan_official_simulation_qualification` consumes only the admitted
  Repository Model and no longer accepts an external Release policy.
- `execute_release_build` now returns verified `MechanicalBuildResult` bytes
  without upload metadata. `form_uploaded_release_artifact` later binds exact
  `ArtifactTransportIdentity` and forms the Release Artifact and successful
  build Evidence without rebuilding.
- Release Artifact admission now binds repository, exact GitHub Actions
  artifact URL, deterministic purpose/role/attempt name, producer, workflow
  run, and run attempt.

### Validation

| Scope | Result |
|---|---|
| Corrected commit-6 contracts and qualification | 25 passed |
| Repository tests | 358 passed |
| Commit-3 canonical contract regression | 164 passed |
| Complete CI test directory | 259 passed |
| Release eligibility regression | 176 passed |
| Ruff check and format check | Passed |
| Pyrefly | 0 errors |
| `git diff --check` | Passed |

The full package was attempted twice. The first run completed with 1,876
passing tests and three failures in existing real-HK integration tests. All
three passed immediately when rerun in isolation. A later serial run was
blocked when HK attempted to fetch
`https://github.com/jdx/hk/releases/download/v1.53.0/hk@1.53.0.zip` while
evaluating temporary test configurations and the HTTP request failed. The
complete CI directory had already passed before that external fetch became
unavailable.

Inline test-gap review found no remaining in-scope correction gap.
`MechanicalBuildResult` now rejects malformed primitive bindings and
content/output substitution before the post-upload formation boundary.

No CLI, workflow, documentation, HK, consumer-policy, or live-authority files
were changed, and no commit was created.

<!-- END APPEND: workflow-delivery-v3-commit6-core-correction -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit6-cli-workflow-status -->

# Workflow Delivery v3 Commit 6 CLI and Official Simulation Status

## Outcome

Complete. The bounded commit-6 CLI transport, strict record admission, and
Official simulation workflow are implemented and validated. No commit was
created. Commit-7 remote observation, live authority, publication mutation,
authorization, capability, and Receipt work was not added.

## Files added or extended for this phase

- `.github/workflows/workflow-delivery-v3-official-simulate.yml`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release_transport.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/simulation.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/workflow.py`
- `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit6_transport_cli.py`
- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_official_simulation_workflow.py`
- `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
- `.testagent/research.md`, `.testagent/plan.md`, `.testagent/status.md`

The current uncommitted commit-6 Release core and unrelated user changes were
preserved.

## Requirement evidence

| Requirement | Exact evidence |
|---|---|
| C6T-1 strict transported record parsing/admission | `test_every_transported_commit6_release_record_round_trips_closed_schema`; admitted model assertion in the same test. |
| C6T-2 complete bounded CLI chain | `test_release_cli_transports_current_attempt_through_commit6_stop_line`; `test_compile_simulation_model_consumes_uploaded_provider_without_rerun`. |
| C6T-3 rerun and adversarial rejection | `test_release_cli_request_id_is_rerun_stable_but_transport_is_attempt_bound`; `test_release_transport_rejects_canonical_binding_and_substitution_attacks`. |
| C6T-4 exact workflow event/DAG/permissions/concurrency/pins/deadlines | `test_official_simulation_event_permissions_and_concurrency_are_exact`; `test_official_simulation_dag_runner_and_deadlines_are_exact`; `test_official_simulation_actions_and_checkouts_are_immutable`. |
| C6T-5 immutable raw ID-only artifact transport | `test_official_simulation_uses_only_raw_id_bound_artifact_transport`. |
| C6T-6 two-stage build and upload ordering | `test_build_is_uploaded_before_artifact_and_evidence_are_formed`; existing `test_upload_metadata_binds_after_single_mechanical_build`. |
| C6T-7 exact four Evidence finalization | CLI end-to-end decision equality in `test_release_cli_transports_current_attempt_through_commit6_stop_line`; four-Evidence workflow assertions in `test_build_is_uploaded_before_artifact_and_evidence_are_formed`. |
| C6T-8 truthful commit-6 stop line and result preservation | `test_commit6_observation_and_publication_stop_line_is_truthful`; `test_simulation_finalizer_preserves_non_successful_qualification`; CLI Outcome assertions in the end-to-end test. |
| C6T-9 CI compatibility and commit-7+ absence | `test_cli_exposes_only_the_commit6_release_transport_commands`; `test_cli_rejects_unapproved_commands`; full v3 package suite. |
| C6T-10 validation | Command table below. |

## Validation results

| Command | Result |
|---|---|
| New transport/workflow scenario files | `11 passed` |
| Release + Official workflow + CLI selection | `256 passed` |
| Full Workflow Delivery v3 package tests | `1906 passed` |
| Real HK trigger integration retry | `50 passed` |
| `uv run --python 3.13 pyrefly check` | Passed: `0 errors` |
| Focused Ruff check | Passed |
| Focused Ruff format check | Passed: 8 files already formatted |
| `mise exec -- actionlint .github/workflows/workflow-delivery-v3-official-simulate.yml` | Passed |
| `mise exec -- hk --no-progress check --step pkl-eval --step pkl-format --all` | Passed |
| `git --no-pager diff --check` | Passed |

An initial full-suite run reported 1,897 passes and eight transient real-HK
configuration-loading failures. The entire HK file then passed with 50 tests,
and the complete package retry passed all 1,906 tests.

## Inline gap and assertion-quality review

- Every transported record type is admitted from canonical bytes under a
  caller-selected runtime type and current authority bindings.
- Negative assertions cover noncanonical bytes, unknown fields, digest
  substitution, record-type substitution, cross-purpose use, stale attempts,
  and producer substitution.
- The CLI scenario asserts exact canonical bytes at identity, plan, Decision,
  observation boundary, empty actions boundary, deterministic summary, and
  incomplete Outcome stages.
- Workflow assertions inspect concrete step ordering and exact configuration,
  rather than only searching for job names.
- The observation stop-line test rejects registry/network commands,
  credentials, live identities, capability/authorization/Receipt, projection
  observation, and PublicationSnapshot text.
- No assertion-free or trivial-only generated test remains, and no explicit
  checklist item lacks named evidence.

<!-- END APPEND: workflow-delivery-v3-commit6-cli-workflow-status -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit6-raw-name-correction-status -->

# Workflow Delivery v3 Commit 6 Raw Artifact Name Correction Status

Complete. All 17 `archive: false` uploads now use a digest-bound file basename
as the actual physical artifact identity. Configured `name` and
`basename(path)` are identical, and every consumed basename is propagated into
downstream CLI paths.

## Evidence

| Requirement | Evidence |
|---|---|
| v7 raw-name behavior | `test_upload_artifact_v7_raw_mode_ignores_configured_name` |
| Every raw upload has exact physical identity | `test_official_simulation_uses_only_raw_id_bound_artifact_transport` |
| No stale fixed downstream names | Fixed `.wdv3/input/*.json` negative matrix in the raw transport contract test |
| Exact `.tgz` tarball identity | `test_build_is_uploaded_before_artifact_and_evidence_are_formed`; `.tgz` assertions and missing-suffix rejection in `test_upload_metadata_binds_after_single_mechanical_build` |
| Plan emits complete tarball basename | `test_release_cli_transports_current_attempt_through_commit6_stop_line` |
| Explicit ID/digest admission preserved | Existing raw transport assertions in `test_official_simulation_uses_only_raw_id_bound_artifact_transport` |

## Validation

| Command | Result |
|---|---|
| Raw-name workflow/release scenarios | `25 passed` |
| Release + Official workflow + CLI selection | `257 passed` |
| Full Workflow Delivery v3 package tests | `1907 passed` |
| `uv run --python 3.13 pyrefly check` | Passed: `0 errors` |
| Focused Ruff check/format | Passed: 4 files already formatted |
| Official workflow actionlint | Passed |
| `git --no-pager diff --check` | Passed |

No blockers. No commit was created, and docs, consumer policy, live release,
and observation implementation were not changed.

<!-- END APPEND: workflow-delivery-v3-commit6-raw-name-correction-status -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit6-final-closure -->

# Workflow Delivery v3 Commit 6 Final Closure

Commit 6 is complete and ready to commit. Live activation remains disabled,
and commit-7 npmjs observation was not implemented.

## Final independent review

Four independent GPT-5.6 Sol reviews covered Release contracts/admission,
qualification mechanics, workflow runtime/security, and holistic v3 scope.
Every finding received separate TP/FP adjudication.

| Finding | Verdict | Resolution |
|---|---|---|
| Digest-mismatched workflow transport could become missing Evidence | TP | Present artifact IDs now require successful download, digest metadata, and exact physical file presence; empty IDs still permit incomplete finalization. |
| Failed artifact-dependent Evidence could reference an unadmitted Artifact | TP | Artifact-binding completeness now dominates quality-failure classification. |
| Publication Snapshot reused potential instead of concrete actions | TP | Added immutable `PublicationAction` with exact artifact, input, key, lock, capability, result, and Receipt bindings. |
| Synthetic observations could produce commit-6 simulation success | TP | Removed the helper from public exports and rejected nonempty observations before commit 7. |
| Publication Action constructor trusted builder-derived fields | TP | Shared pure derivations now enforce every concrete binding at construction; negative substitution tests cover each category. |
| Strict transport lacks live Publication records | FP | Live observation/publication transport is outside commit 6; no hosted Publication Snapshot is emitted. |
| Separate Buddy tag-key claim in this commit-6 correction | FP | The adjudication did not sustain a separate in-scope correction beyond the approved current materialization contract. |

The original contract, qualification, workflow, and holistic reviewers all
reported no findings after the fixes.

## Final validation

| Command | Result |
|---|---|
| Full Workflow Delivery v3 package tests | `1924 passed` |
| Managed `v3-control-pytest` HK gate | Passed; `1924 passed` |
| Root Python tests | `3959 passed` |
| Focused Release tests after formatting | `222 passed` |
| Pyrefly | `0 errors` |
| HK Ruff, Ruff format, actionlint, Pkl eval, and Pkl format steps | Passed |
| `uv build --package three-workflow-delivery-v3` | Built sdist and wheel |
| `dotnet build dirs.proj --no-incremental` | Passed with 0 warnings and 0 errors |
| `pnpm run build` | Passed; generated package-version stamps were restored to placeholders |
| `uv lock --check` | Passed |
| `pnpm install --frozen-lockfile` | Passed |
| `dotnet restore --locked-mode` | Passed |
| `git diff --check` | Passed |

The first managed HK attempt failed because `/tmp` exhausted its inode quota;
after removing only current-user pytest/workflow temporary trees, the isolated
HK gate passed. The complete repository-wide HK gate reaches an unrelated
pre-existing `shfmt` failure in untouched `eng/scripts/*.sh`; the relevant
commit-6 hook steps pass and no unrelated script was modified.

## Requirement evidence

| Requirement | Evidence |
|---|---|
| Exact Official simulation identity and current-attempt transport | `test_release_cli_request_id_is_rerun_stable_but_transport_is_attempt_bound`; `test_release_transport_rejects_canonical_binding_and_substitution_attacks` |
| Complete two-snapshot contracts without live activation | Publication Action positive and substitution tests in `test_commit6_qualification.py`; guarded live-only materialization tests |
| Exact four-obligation qualification and failure continuation | Commit-6 qualification scenarios, unknown Artifact binding regressions, and workflow finalizer contract tests |
| Truthful unsupported observation boundary | `test_commit6_observation_and_publication_stop_line_is_truthful`; nonempty-observation rejection regression |
| Actual raw artifact identity and fail-closed transport | `test_official_simulation_uses_only_raw_id_bound_artifact_transport`; raw v7 naming regression; optional-download fail-closed contract |
| Commit-7 and live authority exclusion | CLI command boundary tests, workflow security contract, and final independent holistic review |

<!-- END APPEND: workflow-delivery-v3-commit6-final-closure -->
