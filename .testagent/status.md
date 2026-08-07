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
