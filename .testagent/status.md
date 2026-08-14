# Workflow Delivery v3 Snapshot Admission Status

## 2026-08-13 Commit 8 Governance Observation Error Taxonomy Status

| Phase | Status |
|---|---|
| Research/checklist | Complete |
| Plan | Complete |
| Test implementation | Complete; 36 focused cases added |
| Narrow validation | Complete; expected production gaps retained |
| Full validation | Complete; expected production gaps retained |
| `test-gap-analysis` | Complete |
| `assertion-quality` | Complete |

### Scope guard

Only tests and the three explicitly retained `.testagent` artifacts may be
edited. Current production and all unrelated working-tree changes remain
authoritative and untouched.

### Test changes

- Added
  `tests/release/test_commit8_governance_observation_errors.py`
  with 24 cases covering G1-G7.
- Extended `tests/platform/test_github.py` with 9 cases covering concrete
  protection/content transport distinctions for G7-G8.
- Extended
  `tests/adapters/test_commit8_publish_governance_recheck.py`
  with 3 definitive observation cases covering G9 while preserving its prior
  disabled/expired/changed cases.
- Reused existing exact publisher CLI persistence and post-marker fallback
  tests as G9-G10 evidence.

### Requirement coverage

| Requirement | Evidence |
|---|---|
| G1 authoritative unprotected definitive rejection | `test_unprotected_ref_is_definitive_governance_rejection`; publisher case `authoritative-unprotected`. |
| G2 fetched canonical/schema definitive rejection | `test_fetched_invalid_canonical_or_schema_content_is_definitive_rejection`; publisher case `invalid-schema`. |
| G3 fetched semantic/digest definitive rejection | `test_fetched_invalid_governance_semantics_are_definitive_rejection`; `test_fetched_content_digest_inconsistency_is_definitive_rejection`; publisher case `invalid-semantics`. |
| G4 disabled/expired/changed remain typed | `test_disabled_expired_and_changed_governance_remain_freshness_rejections`; existing publisher cases `disabled`, `expired`, `resolved-commit-changed`, `blob-oid-changed`, and `content-changed`. |
| G5 local source/time errors are not definitive rejection | `test_local_source_and_time_configuration_errors_are_not_governance_rejections`, including zero remote-call assertions. |
| G6 malformed remote identities are not definitive rejection | `test_malformed_remote_identities_are_not_governance_rejections` with exact generic exception types. |
| G7 transport/API failures are not definitive rejection | `test_transport_failures_are_not_governance_rejections`; `test_governance_content_transport_failures_remain_rest_errors`. |
| G8 protection false versus unknown | `test_ref_protection_404_is_authoritative_false`; `test_ref_protection_transport_unknowns_raise`; `test_ref_protection_malformed_success_response_is_unknown`. |
| G9 exact failed/no-side-effect persistence and zero runner | Expanded `test_publish_second_governance_read_returns_terminal_no_side_effect`; `test_publish_cli_persists_governance_terminal_state_before_nonzero`. |
| G10 generic incomplete/possibly-mutated fallback | Generic branch in `test_publish_cli_persists_governance_terminal_state_before_nonzero`; `test_post_marker_governance_terminal_state_lookalikes_are_possibly_mutated`. |

### Validation results

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q tests/release/test_commit8_governance_observation_errors.py tests/platform/test_github.py tests/adapters/test_commit8_publish_governance_recheck.py` (repository-relative paths expanded in the actual command) | `37 passed, 18 failed` as expected. The failures are exactly the missing definitive observation taxonomy (10), protection unknown distinction (5), and publisher propagation of new definitive rejection cases (3). |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q tests/test_cli.py::test_publish_cli_persists_governance_terminal_state_before_nonzero tests/release/test_commit8_live_scenarios.py::test_post_marker_governance_terminal_state_lookalikes_are_possibly_mutated` | `10 passed`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests` | `2229 passed, 18 failed` in 381.83s. All failures are the focused expected production gaps above; no unrelated failures appeared. |
| `uv run --python 3.13 pyrefly check <three focused test files>` | Passed; `0 errors`. |
| `uv run --python 3.13 ruff check <three focused test files>` | Passed. |
| `uv run --python 3.13 ruff format --check <three focused test files>` | Passed; 3 files already formatted. |
| `uv build --package three-workflow-delivery-v3` | Passed; sdist and wheel built. |
| `git --no-pager diff --check` | Passed. |

### Mandatory quality gates

Both `test-gap-analysis` and `assertion-quality` were invoked after the final
test changes. Their shared `test-analysis-extensions` dependency was
unavailable, so the required Python/pytest analyses were completed manually.

Pseudo-mutation review found no remaining in-scope test gap:

- changing exact `is not True` protection admission to truthiness is killed by
  the non-Boolean protection case;
- converting permission/5xx/network/malformed responses to `False` is killed
  by the protection unknown matrix, while 404 remains the positive false
  control;
- removing canonical/schema/semantic wrapping is killed by the definitive
  error-class assertions;
- removing the content digest consistency check is killed by the patched
  attestation/content mismatch case;
- over-wrapping local configuration, malformed identity, or transport errors
  is killed by exact non-Governance type/identity assertions;
- swallowing the publisher rejection or invoking the runner is killed by the
  exact persisted-state, event-sequence, and zero-runner assertions.

Assertion-depth review found no assertion-free, trivial-only, or tautological
generated tests. Each exception test asserts concrete class identity or name
and at least one secondary observable (call sequence, exact message, preserved
exception identity, persisted document, or runner count). The review
strengthened malformed-identity cases with exact exception types, transport
cases with exact messages, and freshness cases with a direct content digest.

### Expected production blockers

No production edits were allowed. The retained failures demonstrate:

1. `GovernanceRejectionError` does not yet exist and fetched definitive
   Governance failures currently escape as `ValueError`, `TypeError`, or
   `JSONDecodeError`.
2. `GitHubRestClient.is_ref_protected` currently converts every
   `GitHubRestError` to `False` and admits malformed success objects as
   protected.
3. The publisher catches only `GovernanceFreshnessRejectionError`, so the new
   definitive unprotected/schema/semantic cases cannot yet persist the exact
   failed/no-side-effect result.

## 2026-08-13 Commit 8 History Admission Findings 10-13 Status

| Phase | Status |
|---|---|
| Research/checklist | Complete |
| Plan | Complete |
| Production implementation | Complete |
| Focused tests | Complete |
| Validation | Complete |

### Files Modified for This Bounded Request

- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/live.py`
- `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_history_admission.py`
- `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py`
- `.testagent/research.md`
- `.testagent/plan.md`
- `.testagent/status.md`

No workflow YAML or `platform/github.py` files were edited.

### Requirement Coverage

| Requirement | Evidence |
|---|---|
| H10 different-target run filtering | `test_discovery_filters_different_target_runs_without_artifact_or_job_queries` |
| H11 explicit historical schema allowlist and unrelated artifact skipping | `test_discovery_skips_unrelated_json_non_json_and_multifile_artifacts`; `test_discovery_fails_recognized_malformed_or_conflicting_history_schemas` |
| H12 strict live lineage and exact phase facts without unsupported provenance | `test_discovery_fails_recognized_malformed_or_conflicting_history_schemas`; `test_discovery_requires_unique_context_owned_finalizer_phase_facts`; `test_same_run_prior_attempt_enumerates_current_artifacts_without_attempt_provenance` |
| H13 same-run prior-attempt proof | `test_same_run_prior_attempt_enumerates_current_artifacts_without_attempt_provenance`; `test_same_run_prior_attempt_fails_closed_when_run_level_proof_is_missing` |

### Validation Results

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_history_admission.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py -k history` | `40 passed, 23 deselected` |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py` | `28 passed` |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/live.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_history_admission.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py` | Passed |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/live.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_history_admission.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py` | `3 files already formatted` |
| `uv run --python 3.13 pyrefly check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/live.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_history_admission.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py` | `0 errors` |

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

## 2026-08-12 Workflow Delivery v3 Commit 7 Status Addendum

| Phase | Status |
|---|---|
| Commit-7 transport records | Complete |
| CLI integration | Complete |
| Official simulation workflow integration | Complete |
| Static/release workflow tests | In progress |
| Ruff/Pyrefly/full v3 validation | Pending |

### Commit-7 Evidence

| Requirement | Evidence |
|---|---|
| C7-1/C7-2 canonical transport bundles | `SimulationObservationSet` and `HypotheticalActionsReport` in `release/simulation.py`; transport tests in `test_commit7_observation.py`. |
| C7-3 npmjs observation CLI and non-success skip | `release observe-npmjs` in `cli.py`; CLI transport test monkeypatches the observer and failed-qualification coverage is in commit-7 tests. |
| C7-4 action materialization | `materialize_hypothetical_actions` and CLI `materialize-hypothetical-actions`; `test_materialize_hypothetical_actions_accepts_only_absent_and_exact`. |
| C7-5 finalizer mapping and substitution rejection | `finalize_simulation`; `test_finalize_simulation_maps_commit7_observation_outcomes` and transport binding/substitution tests. |
| C7-6 workflow permissions/credentials | `test_official_simulation_event_permissions_and_concurrency_are_exact` and `test_commit7_observation_and_hypothetical_actions_are_bounded`. |
| C7-7 exact ID/digest transport | `test_official_simulation_uses_only_raw_id_bound_artifact_transport`. |
| C7-8 raw basename model | `_raw_artifact_name` and `test_upload_artifact_v7_raw_mode_ignores_configured_name`. |

### Validation Results

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest src/public/lib/three-workflow-delivery-v3/tests/release/test_commit7_observation.py -q` | `10 passed` |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest src/public/lib/three-workflow-delivery-v3/tests/release/test_commit6_transport_cli.py::test_release_cli_transports_current_attempt_through_commit6_stop_line -q` | `1 passed` |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest src/public/lib/three-workflow-delivery-v3/tests/contracts/test_official_simulation_workflow.py -q` | `8 passed` |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest src/public/lib/three-workflow-delivery-v3/tests/release -q` | `232 passed` |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest src/public/lib/three-workflow-delivery-v3/tests/release -q` | Final rerun after cleanup: `234 passed` |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest src/public/lib/three-workflow-delivery-v3/tests/contracts/test_official_simulation_workflow.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_npmjs.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py -q` | `274 passed` |
| `uv run --python 3.13 ruff check ...changed v3 files...` | Passed |
| `uv run --python 3.13 ruff format --check ...changed v3 files...` | Passed after formatting `test_commit6_qualification.py` |
| `uv run --python 3.13 pyrefly check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3 src/public/lib/three-workflow-delivery-v3/tests/release/test_commit7_observation.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | `0 errors` |
| `actionlint .github/workflows/workflow-delivery-v3-official-simulate.yml` | Passed |
| `git --no-pager diff --check` | Passed |

### Full-v3 Validation Blockers

- `uv run --python 3.13 --package three-workflow-delivery-v3 pytest src/public/lib/three-workflow-delivery-v3/tests -q` was attempted. It reached `1958 passed` with two failures before cleanup: one stale commit-6 CLI exposure test fixed in this change, and `test_installed_nbgv_api_returns_exact_head_and_native_projection`, which fails inside `pnpm install --frozen-lockfile --ignore-scripts --ignore-pnpmfile` in a copied temp repository before reaching commit-7 code.
- Rerunning the node-provider failure directly still failed in the same `pnpm install` prerequisite with exit 228 and empty stderr.
- A broader rerun ignoring `test_node_provider.py` reached `1785 passed` and then failed in `test_real_hk_plan_triggers_consumer_policy_for_each_cataloged_surface[postinstall-ts-nested]` because hk panicked while loading configuration in a synthetic temporary repository. This is outside the commit-7 release CLI/workflow scope.
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

<!-- BEGIN APPEND: workflow-delivery-v3-commit7-observer-core-status -->

# Workflow Delivery v3 Commit 7 Observer Core Status

## Outcome

Complete for the bounded observer core. The npmjs observer, strict
ProjectionObservation transport/admission, and simulation finalizer outcome
mapping are implemented and validated. CLI/workflow integration, live identity,
Authorization, Capability, Receipt, mutation, GitHub Packages behavior, Buddy
tag behavior, and commit-8 code were not added.

## Files added or extended for this phase

- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/npmjs.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release_transport.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/finalizer.py`
- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_npmjs.py`
- `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit7_observation.py`
- `.testagent/research.md`, `.testagent/plan.md`, `.testagent/status.md`

Pre-existing modified design docs were left untouched in the working tree.

## Requirement evidence

| Requirement | Evidence |
|---|---|
| Credential-free injectable npmjs transport | `ScriptedTransport`; `test_npmjs_observer_does_not_fetch_after_failed_qualification`; `StdlibHttpTransport` has no auth/cookie/npm config. |
| Exact first-slice coordinate/version and registry policy | `test_npmjs_observer_rejects_wrong_coordinate_before_network`; malformed/wrong metadata assertions. |
| HTTP/network classification | `test_npmjs_observer_classifies_exact_404_as_absent`; hard-4xx, retryable, and timeout tests. |
| Redirect, encoding, size, and truncation handling | `test_npmjs_observer_rejects_off_host_redirect_and_nonidentity_encoding`; `test_npmjs_observer_size_truncation_is_unknown`. |
| Exact bytes and in-package witness | `test_npmjs_observer_accepts_exact_bytes_and_witness`. |
| Byte/witness conflict and integrity-only rejection | `test_npmjs_observer_reports_byte_conflict`; `test_npmjs_observer_reports_target_witness_conflict`; `test_npmjs_observer_integrity_only_is_not_exact`. |
| Strict ProjectionObservation transport bindings | `test_projection_observation_crosses_transport_with_current_bindings`; `test_projection_observation_rejects_purpose_and_target_substitution`. |
| Simulation outcome mapping | `test_finalize_simulation_maps_commit7_observation_outcomes`. |
| Hypothetical actions absent/exact only | `test_materialize_hypothetical_actions_accepts_only_absent_and_exact`. |
| Failed/incomplete qualification does not require observation | `test_failed_or_incomplete_qualification_needs_no_observation`; adapter no-fetch failure test. |

## Validation results

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q ...test_node.py::test_adapter_public_api_exports_closed_types_and_functions ...test_npmjs.py ...test_commit7_observation.py ...test_commit6_transport_cli.py ...test_commit6_qualification.py` | `64 passed` |
| Focused release + adapter suite | `75 passed` |
| Full v3 package excluding real HK trigger file | `1903 passed` |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q ...tests/test_hk_trigger.py` | Blocked by existing HK config-load panic in copied temp repos, not by observer code |
| `uv run --python 3.13 pyrefly check` | `0 errors` |
| Focused Ruff check | Passed |
| Focused Ruff format check | `8 files already formatted` after formatting two test files |
| `git --no-pager diff --check` | Passed |

## Inline gap and assertion-quality review

- Every explicit classification branch has a concrete assertion.
- No test performs a real network request; all HTTP facts are injected.
- Exactness requires downloaded bytes and witness, not `dist.integrity`.
- Current purpose, target, run attempt, and producer substitutions are negative
  tested through transported `ProjectionObservation` admission.
- The private synthetic seam remains non-public and cannot complete simulation
  through `finalize_simulation`.

<!-- END APPEND: workflow-delivery-v3-commit7-observer-core-status -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit7-adjudicated-fixes-2026-08-12 -->

# Workflow Delivery v3 Commit 7 Adjudicated Fixes

Status: **complete**.

- npmjs tarball observation now enforces a positive exact-integer expanded
  payload bound, performs one bounded parse/decompression, and classifies
  expansion-limit overflow as unprovable.
- Readable packed manifests with a wrong package name or version classify as
  conflicting while retaining coordinate, SHA-512, and any valid witness.
- Observer and stdlib transport size limits reject zero, negative, and Boolean
  values before network/read activity.
- Stdlib HTTP reads validate declared `Content-Length`, map incomplete normal
  and `HTTPError` bodies to unknown network state, retain byte caps, and disable
  inherited proxies with `ProxyHandler({})`.
- `ProjectionObservation` now retains closed typed canonical request and
  bounded response facts. Admission derives and verifies request and response
  digests and rejects independent fact/digest tampering.
- The v3 handoff checkpoint now truthfully states commits 1 through 7.

## Validation

| Command | Result |
|---|---|
| Focused npmjs, commit-7 observation/finalizer, commit-6 transport/qualification, CLI, and Official workflow tests | `150 passed` |
| Ruff format check | Passed; 47 files already formatted |
| Ruff check | Passed |
| Pyrefly package check | Passed; 0 errors |
| Full v3 package attempt 1 | `1987 passed`, one unrelated transient HK configuration-load panic; the failed test passed in isolation |
| Full v3 package attempt 2 | `1986 passed`, two unrelated transient HK configuration-load panics; both failed tests passed in isolation |
| `git diff --check` | Passed |

No commit was created.

<!-- END APPEND: workflow-delivery-v3-commit7-adjudicated-fixes-2026-08-12 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit7-final-closure-2026-08-13 -->

# Workflow Delivery v3 Commit 7 Final Closure

Status: **complete**.

Commit 7 now provides credential-free, exact-version npmjs observation for
Official simulation, strict retained request/response facts, SHA-512 and packed
witness classification, and hypothetical action reporting without registry
mutation or live authority.

## Review closure

- Four independent GPT-5.6 Sol reviewers examined npmjs protocol behavior,
  observation transport/finalization, workflow runtime/security, and v3 scope.
- Eight atomic findings were independently adjudicated: six true positives and
  two false positives.
- All six true positives were fixed. The original four reviewers then reported
  no remaining findings.

## Final validation

| Command | Result |
|---|---|
| Focused commit-7 pytest suite | `77 passed` |
| Full v3 pytest | `1988 passed` |
| Managed HK `v3-control-pytest` gate | `1988 passed` |
| Root pytest | `4023 passed` |
| `uv run --python 3.13 pyrefly check` | `0 errors` |
| V3 Ruff check and format check | Passed; 47 files formatted |
| `actionlint .github/workflows/workflow-delivery-v3-official-simulate.yml` | Passed |
| Managed HK `pkl-eval` and `pkl-format` gates | Passed |
| `uv build --package three-workflow-delivery-v3` | Built sdist and wheel |
| `dotnet build dirs.proj --no-incremental` | Passed; 0 warnings and 0 errors |
| `pnpm run build` | Passed; generated smoke-package versions reset afterward |
| `uv lock --check` | Passed |
| `pnpm install --frozen-lockfile` | Passed |
| `dotnet restore --locked-mode` | Passed |
| `git diff --check` | Passed |

The first managed HK attempt encountered the known transient HK
configuration-load panic in a copied temporary repository. The exact failed
scenario passed in isolation, and the complete managed gate passed on rerun.
The first root pytest attempt ran concurrently with `pnpm run build`; the build
temporarily version-stamped two smoke-package manifests. After resetting those
generated versions, the clean root suite passed.

## Requirement evidence

| Requirement | Evidence |
|---|---|
| Credential-free direct npmjs transport | `test_stdlib_transport_ignores_environment_proxies`; workflow permission and credential contract tests |
| Complete bounded HTTP bodies | `test_stdlib_transport_rejects_incomplete_content_length`; metadata and tarball truncation observation tests |
| Bounded single-pass tar expansion | `test_npmjs_observer_bounds_expanded_tarball_and_parses_once` |
| Exact bytes, identity, version, and target witness | `test_npmjs_observer_accepts_exact_bytes_and_witness`; packed name/version conflict tests |
| Retained canonical request/response facts | ProjectionObservation transport round-trip and independent fact/digest tamper tests |
| Truthful simulation outcomes | `test_finalize_simulation_maps_commit7_observation_outcomes` |
| No mutation or live authority | Official workflow contract tests and final holistic reviewer closure |

<!-- END APPEND: workflow-delivery-v3-commit7-final-closure-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-phase1-tests-2026-08-13 -->

# Workflow Delivery v3 Commit 8 Phase 1 Test Evidence

## Implemented scope

- Added `tests/release/test_commit8_contracts.py`.
- Added `tests/release/test_commit8_history_admission.py`.
- No production, workflow, transport, publish, research, or plan files changed.

## Focused validation

| Command | Result |
|---|---|
| Focused commit-8 pytest | `27 passed, 1 blocked contract failure` |
| Release suite excluding unavailable contract API | `267 passed, 1 deselected` |
| Complete admission regression suite | `423 passed` |
| Ruff check for both phase-1 files | Passed |

The focused contract availability test reports these production API blockers:
`HistoricalExecutionRecord`, `ExecutionHistoryAdmissionSnapshot`,
`ReleaseAttemptBinding`, `AuthorizationRecord`,
`CapabilityAdmissionDecision`, `ActionResult`,
`CapabilityGroupResultBundle`, `Receipt`, and `AttemptOutcome`.

## Pre-completion gate

- Pseudo-mutation review added exact successful current/history assertions,
  payload-digest substitution rejection, diagnostic-only history claims,
  same-run prior-attempt proof, lifecycle, phase, and transport substitution
  coverage.
- Assertion-quality review found no assertion-free, trivial-only, or
  tautological tests. Successful admissions assert primary identity plus
  transport, authority, platform-fact, and diagnostic secondary observables.
- Prompt-scenario review confirms the two requested files are the only test
  files added and no workflow YAML or HTTP/publish transport was implemented.

<!-- END APPEND: workflow-delivery-v3-commit8-phase1-tests-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-phase1-format-validation-2026-08-13 -->

Focused Ruff check and format-check both passed after applying the canonical
line wrapping to `test_commit8_history_admission.py`.
The Workflow Delivery v3 package build also passed and produced its sdist and
wheel.

<!-- END APPEND: workflow-delivery-v3-commit8-phase1-format-validation-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-phase1-production-2026-08-13 -->

# Workflow Delivery v3 Commit 8 Phase 1 Production Evidence

## Implemented scope

- Added strict Buddy live Intent normalization, Execution derivation, and
  pre-Attempt `ReleaseAttemptBinding`.
- Added immutable `HistoricalExecutionRecord` and exhaustive
  `ExecutionHistoryAdmissionSnapshot` contracts plus caller-selected,
  history-only admission and deterministic sorting.
- Added strict `AuthorizationRecord`, `CapabilityAdmissionDecision`,
  `ActionResult`, `Receipt`, `CapabilityGroupResultBundle`, and
  `AttemptOutcome` contracts.
- Added pure live finalization mappings for exact no-op, platform termination,
  exact group coverage, durable Receipts, and possible mutation.
- Closed canonical transport deserialization/admission and public exports for
  the new records, `PublicationAction`, and `PublicationSnapshot`.
- Extended Buddy publication keys to exact coordinate-plus-target-tag keys,
  `github/packages-write-v1`, and normalized destination/package conservative
  lock grouping while preserving Official simulation behavior.
- Did not add workflow YAML, GitHub Packages HTTP transport, publication
  transport, Approval Outcome Evidence emission, PAT, OIDC, or activation.

## Exact validation

| Command | Result |
|---|---|
| New commit-8 files only | **62 passed** |
| Affected commit-6, commit-7, and commit-8 Release files | **126 passed** |
| Ruff check for changed production/tests | Passed |
| Ruff format check for changed production/tests | Passed; 9 files formatted |
| `uv run --python 3.13 pyrefly check` | Passed; 0 errors |
| `git diff --check` | Passed |

## Focused test-quality gate

- `test-gap-analysis` and `assertion-quality` were invoked. Their requested
  `test-analysis-extensions` helper is unavailable, so the Python/pytest review
  used repository conventions directly.
- Pseudo-mutation coverage kills caller-authority inversion, incomplete or
  truncated pagination, malformed/count-substituted query results, duplicate
  history, same-run unverified prior attempts, post-Attempt history use,
  Attempt/history substitution, unsuccessful approval, stale Governance
  success, incomplete coordinate/tag keys, inexact group action sets, false
  success after Receipt loss, capability records on an exact no-op, and
  pre-/post-capability termination inversion.
- Assertion review found no assertion-free or trivial-only generated tests.
  The suite uses deep equality, exact canonical round trips, collection/set
  equality, negative absence checks, immutable-state checks, and anchored
  exception assertions. Round-trip assertions use nontrivial nested records
  and are paired with independent field substitution tests.

## Requirement evidence

| Requirement | Evidence |
|---|---|
| Strict Buddy normalization and Attempt ordering | `test_buddy_request_normalization_and_execution_derivation_are_strict`; `test_attempt_binding_requires_preexisting_exact_history_snapshot` |
| Caller-selected history-only authority | `test_history_snapshot_rejects_incomplete_or_substituted_query_results`; `test_history_payload_cannot_select_its_own_authority` |
| Deterministic exhaustive history | `test_history_snapshot_sorts_records_and_round_trips_closed_schema` |
| Same-run prior attempts without unsupported provenance | `test_same_run_prior_attempt_remains_history_only_without_provenance_claims` |
| Successful approval only | `test_diagnostic_review_cannot_authorize` |
| Capability freshness and exact manifests | `test_blocked_capability_decision_is_non_authorizing_and_attempt_local`; `test_group_bundle_requires_exact_action_set_equality` |
| Coordinate-plus-tag keys and conservative grouping | `test_buddy_complete_keys_are_distinct_from_conservative_group` |
| Durable Receipt and uncertainty | `test_lost_receipt_after_possible_mutation_can_never_be_success` |
| Exact no-op and platform termination mappings | `test_exact_preobserved_noop_requires_authorization_and_zero_capability`; `test_platform_termination_maps_by_capability_phase` |
| Closed canonical transport | `test_commit8_records_round_trip_through_closed_transport`; history Snapshot round-trip test |
| Independent record substitutions | `test_commit8_records_reject_independent_binding_substitutions`; historical record and Snapshot authority substitution tests |

## Scope and preservation

The pre-existing uncommitted documentation and `.testagent` research/plan
edits were preserved. No workflow YAML or destination transport was added, and
no commit was created.

<!-- END APPEND: workflow-delivery-v3-commit8-phase1-production-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-phase2-tests-2026-08-13 -->

# Workflow Delivery v3 Commit 8 Phase 2 Test Evidence

## Implemented scope

- Added
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_github_packages.py`.
- Appended phase-2 research and plan sections without replacing prior
  `.testagent` history.
- Used only strict in-memory HTTP and command fakes. No workflow YAML, Adapter
  production, export, network, publication, credential, or activation change
  was made.
- The unavailable `code-testing-extensions` guidance was replaced by explicit
  inference from the existing Python/pytest Adapter tests, as recorded in
  research.

## Validation

| Command | Result |
|---|---|
| Focused new Adapter file | **4 passed, 16 failed** across 20 collected cases; every failure is the explicit missing `three_workflow_delivery_v3.adapters.github_packages` production blocker. |
| Production-independent new contract cases | **4 passed, 16 deselected**. |
| Existing commit-8 contract/history regression files | **62 passed**. |
| Workflow Delivery v3 package build | Passed; sdist and wheel built. |
| Ruff check | Passed after the final E501 suppression for the intentionally explicit scenario name. |
| Ruff format check | Passed. |

## Pre-completion self-review

`test-gap-analysis` and `assertion-quality` were invoked. Their required
`test-analysis-extensions` helper is unavailable, so the review used repository
pytest conventions directly.

- Pseudo-mutation result: the existing record-level mutations for omitted
  witness, wrong tag, current-Attempt substitution, complete key count,
  normalized grouping, created/conflict/lost-response distinction, and
  identical/differing race distinction are killed by concrete assertions.
  Adapter mutations cannot yet be executed because the production Adapter does
  not exist; the suite reports this as no coverage/blocking rather than
  skipping it.
- Assertion-depth result: no assertion-free, trivial-only, round-trip-only, or
  tautological generated test remains. Tests assert complete tuples, exact
  values, exception messages, call-log emptiness, filesystem cleanup and mode,
  secret absence, result plus mutation disposition, Receipt absence/presence,
  complete keys, and conservative grouping.
- Fixes from review: removed a permissive response-digest assertion, added
  concrete response-binding validation, added cleanup assertions for both
  runner paths, asserted token placement only in the temporary config, and
  repaired the complete-key fixtures so the production-independent tests
  execute.

## Requirement coverage

| Requirement | Evidence |
|---|---|
| Exact endpoints, pagination, headers, and bounds | `test_github_packages_requests_exact_escaped_endpoints_headers_and_pages` (BLOCKED on missing Adapter). |
| Wrong basis before transport | `test_github_packages_rejects_wrong_basis_before_transport` and its empty recording-transport assertion (BLOCKED). |
| All six classifications | Six parameter cases of `test_github_packages_classifies_all_six_closed_states` (BLOCKED). |
| Exact tar, witness, and tag | `test_github_packages_exact_requires_tar_witness_and_target_tag` (PASS for exact record/witness/tag closure; Adapter tar readback remains BLOCKED). |
| REST/npm inconsistency | `test_github_packages_rest_npm_inconsistency_is_blocking` (BLOCKED). |
| Token redaction and redirect-origin policy | `test_github_packages_redacts_token_and_rejects_cross_origin_redirect` (BLOCKED). |
| Exact publish argv, temporary config cleanup, and mode | `test_publish_uses_exact_argv_private_config_and_cleans_up`; `test_publish_cleans_config_after_runner_failure` (BLOCKED). |
| Forbidden operations | `test_publish_never_uses_forbidden_operations_or_credentials` (BLOCKED). |
| Created/conflict/lost-response semantics | `test_publish_created_conflict_and_lost_response_are_distinct` (PASS). |
| Identical/differing race semantics | `test_publish_identical_and_differing_races_fail_closed` (PASS). |
| Receipt/response substitution | `test_publish_rejects_receipt_and_response_substitution` (record substitutions specified; response admission BLOCKED). |
| Complete keys and conservative grouping | `test_complete_keys_remain_distinct_while_grouping_is_conservative` (PASS). |
| No-network/no-publish preconditions | `test_github_packages_rejects_wrong_basis_before_transport`; `test_publish_preconditions_block_runner_and_network` (BLOCKED with exact empty-log assertions present). |
| No real network/publish/workflow/activation | Recording fakes in `test_github_packages.py`; repository diff contains no new workflow or Adapter production file. |

## Blocker

The authoritative workspace has no
`three_workflow_delivery_v3.adapters.github_packages` module. Per the requested
test-only phase boundary, it was not reconstructed or implemented. The red
cases are retained as focused test-first contracts and are not skipped.

<!-- END APPEND: workflow-delivery-v3-commit8-phase2-tests-2026-08-13 -->

## Commit 8 Phase 2 Final Review Addendum

The mandatory pre-completion gate identified and fixed two test-quality gaps:

- all-six-state coverage no longer passes the desired classification into the
  Adapter; it supplies independent REST/npm/tar/witness/tag facts;
- publish outcome/race coverage no longer constructs adjacent `ActionResult`
  records; it exercises the expected Adapter result classifier.

Exact-state assertions now kill independent byte, witness, and tag mutations.
Receipt validation names all requested substitution dimensions. Endpoint
coverage includes first and subsequent pages plus the page upper bound.

### Final validation

| Command | Result |
|---|---|
| Focused GitHub Packages Adapter pytest | `1 passed, 19 failed`; all 19 failures are the explicit missing `three_workflow_delivery_v3.adapters.github_packages` blocker. |
| Existing commit-8 contract/history pytest | `62 passed`. |
| Focused Ruff check and format check | Passed after the final `PLR0913` test-matrix annotation. |
| `git diff --check` | Passed. |

### Final pseudo-mutation and assertion-depth review

`test-gap-analysis` and `assertion-quality` were invoked. Their requested
`test-analysis-extensions` dependency is unavailable, so the Python review was
completed directly.

- **Killed by the contract:** returning the requested classification
  unconditionally, treating missing tar/witness/tag as exact, accepting
  differing bytes/witness/tag, collapsing created/conflict/lost response,
  accepting differing races, retaining credentials across origins, changing
  exact argv/config mode/cleanup, executing before preconditions, and accepting
  enumerated Receipt substitutions.
- **Blocked:** executable mutations inside observation, publication, and
  response-binding production code, because the Adapter module does not exist.
- **Assertion quality:** no assertion-free, trivial-only, tautological
  round-trip, or type-only generated test remains. Tests use concrete equality,
  exception, negative secret/operation, fake-call side-effect, filesystem
  cleanup/mode, and structural Receipt/key assertions.

<!-- END APPEND: workflow-delivery-v3-commit8-phase2-final-status-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-phase2-implementation-2026-08-13 -->

## Workflow Delivery v3 Commit 8 Phase 2 Implementation

Complete. The strict first-slice GitHub Packages npm Adapter is implemented
without workflow YAML or live activation.

### Implementation evidence

- `adapters/github_packages.py` fixes the destination, registry, package,
  observation contract, operation, owner, and repository identities.
- Observation uses injected bounded REST/npm/tarball GET seams, exhaustive
  version pagination, exact version and dist-tag reads, raw tarball SHA-512,
  and the commit-7 single-pass packed manifest/witness validator.
- Ordered response exchanges are retained as redacted canonical facts.
  Credential-bearing redirect hops cannot cross origins, and credentials are
  absent from retained observations, command facts, and diagnostics.
- Publication permits one `npm publish` create-only command with the exact
  target-derived tag, `--ignore-scripts`, and a cleaned mode-0600 temporary
  npm config. It has no dist-tag repair, force, overwrite, unpublish, delete,
  restore, `latest`, PAT, or OIDC path.
- Current semantics emit a Receipt only after successful mutation, exact
  post-readback, exact current binding validation, and successful persistence.
  Publish conflicts fail even if readback later appears exact. Lost responses
  and persistence failures remain incomplete and possibly mutated.
- The Adapter API is exported through `adapters/__init__.py`.

### Validation

| Command | Result |
|---|---|
| Focused GitHub Packages Adapter pytest | `20 passed` |
| npmjs Adapter pytest | `46 passed` |
| Node Adapter pytest | `198 passed` |
| Commit-8 contract/history pytest | `62 passed` |
| Combined related pytest | `326 passed` |
| Focused Ruff check | Passed |
| Focused Ruff format check | Passed: 4 files already formatted |
| `uv run --python 3.13 pyrefly check` | Passed: 0 errors |

The earlier missing-Adapter blocker in this status file is superseded by this
implementation evidence. No real network request or npm publication ran.

<!-- END APPEND: workflow-delivery-v3-commit8-phase2-implementation-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-phase34-tests-2026-08-13 -->

# Workflow Delivery v3 Commit 8 Phase 3/4 Test Evidence

## Implemented scope

- Added `tests/release/test_commit8_live_scenarios.py`.
- Added `tests/contracts/test_buddy_workflows.py`.
- Extended `tests/test_cli.py` only for the six commit-8 live commands, their
  required transport options, and the exact terminal exit-status map.
- Appended research, plan, and status evidence without replacing prior
  `.testagent` history.
- Did not edit production, workflow YAML, HK, Governance, credentials,
  activation, or later commit scope. No GitHub API, network request, or package
  publication ran.

## Validation

| Command | Result |
|---|---|
| Two added phase-3/4 files | **5 passed, 27 failed**. The passing cases cover exact no-op, Receipt loss, both termination phases, and mixed-attempt replay. All failures are missing phase-3 orchestration APIs or the two absent v3 Buddy workflow files. |
| Focused commit-8 CLI contract | **7 failed**: six commands and `LIVE_OUTCOME_EXIT_STATUS` are absent. |
| Existing commit-8 contract/history/Adapter regression | **82 passed**. |
| Full Workflow Delivery v3 pytest | **2075 passed, 34 failed**; the 34 failures are exactly the 27 added phase blockers plus seven CLI blockers. |
| Package build | Passed; sdist and wheel built. |
| Ruff check | Passed. |
| Ruff format check | Passed after apply-patch-only formatting. |
| `uv run --python 3.13 pyrefly check` | Passed; 0 errors. |
| `git diff --check` | Passed. |

## Mandatory pre-completion gate

`test-gap-analysis` and `assertion-quality` were invoked.
`test-analysis-extensions` was attempted and unavailable, so the final review
used repository pytest conventions.

- Pseudo-mutation review: closed CLI status inversion, partial page traversal,
  artifact-name selection, duplicate/rate/denial/truncation acceptance,
  reviewer byte or digest substitution, uppercase/short/long/ref target
  acceptance, moving-ref fallback, Governance substitution, restoration in the
  same Attempt, no-op Capability emission, diagnostic denial authorization,
  false success after Receipt loss, pre/post Capability termination inversion,
  mixed-attempt admission, permission broadening, concurrency cancellation,
  name-selected artifact transport, retention drift, mutable action pins,
  activation, and later-scope introduction all have concrete killing
  assertions.
- Review fix: the anonymous-fetch success test now also asserts the exact
  public repository URL and absence of every `refs/heads/` fallback.
- No-cover zones remain only where the corresponding phase-3 APIs, CLI
  commands/status map, and workflow files do not exist. They are production
  blockers, not skipped or weakened tests.
- Assertion-depth review found no assertion-free, trivial-only, type-only, or
  tautological generated tests. Tests combine exact equality/deep structure,
  exception assertions, negative credential/scope assertions, fake-call side
  effects, immutable byte/digest bindings, and state-transition assertions.

## Requirement coverage

| Requirement | Exact evidence |
|---|---|
| Strict CLI transport/status mappings | `test_cli_exposes_strict_commit8_live_transport_commands`; `test_cli_commit8_live_outcome_status_mapping_is_closed` (BLOCKED on missing CLI surface). |
| Exhaustive injectable history | `test_history_exhausts_every_run_artifact_and_job_page_by_id`; `test_history_duplicate_rate_denial_and_truncation_fail_before_attempt` (BLOCKED on missing API). |
| Reviewer byte identity and bindings | `test_reviewer_artifact_preserves_exact_bytes_and_all_digest_bindings` (BLOCKED on missing API). |
| Anonymous exact-SHA verification | `test_anonymous_fetch_rejects_every_non_exact_target_without_transport`; `test_anonymous_fetch_verifies_exact_commit_and_detached_head_without_network` (BLOCKED on missing API; fake runner only). |
| Governance substitutions/restoration | `test_governance_freshness_substitution_blocks_and_restoration_needs_new_attempt` (BLOCKED on missing API). |
| Exact no-op | `test_exact_noop_still_requires_authorization_and_emits_no_capability` (PASS). |
| Diagnostic-only rejection | `test_diagnostic_only_rejection_never_authorizes_or_starts_capability` (BLOCKED on missing API). |
| Receipt loss | `test_receipt_loss_after_possible_mutation_requires_reobservation` (PASS). |
| Pre/post Capability termination | Two cases of `test_platform_termination_mapping_is_capability_phase_exact` (PASS). |
| Replay/mixed Attempt | `test_whole_release_replay_rejects_mixed_attempt_capability_records` (PASS). |
| DAG/permissions/concurrency/Environments | `test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact`; `test_buddy_permission_ceiling_and_effective_permissions_are_exact`; `test_live_attempt_dag_environments_and_capability_gate_are_exact` (BLOCKED on absent workflows). |
| Action pins/artifact transport/retention | `test_all_actions_are_full_sha_pinned_with_version_comments`; `test_reviewer_artifact_transport_is_raw_id_bound_and_retained_45_days` (BLOCKED on absent workflows). |
| Disabled activation/no later scope | `test_buddy_workflow_files_are_the_disabled_commit8_pair_only`; `test_workflows_forbid_secrets_oidc_publication_bypasses_and_later_scope` (BLOCKED on absent workflows). |
| No real network/publication | `RecordingHistoryClient`, injected anonymous-fetch runner, and static YAML readers in the two added files; no live command was executed. |

## Blockers

The authoritative workspace lacks:

1. `discover_execution_history`, `materialize_reviewer_artifact`,
   `fetch_exact_public_revision`, `form_authorization_record`, and
   `admit_live_capability`;
2. the six commit-8 live CLI commands and closed status constant; and
3. `.github/workflows/workflow-delivery-v3-buddy-smoke.yml` plus
   `.github/workflows/workflow-delivery-v3-live-attempt.yml`.

Per the requested test-only phase boundary, none was implemented or
reconstructed. Activation and later commit scope remain untouched.

<!-- END APPEND: workflow-delivery-v3-commit8-phase34-tests-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-phase34-final-gate-2026-08-13 -->

## Commit 8 Phase 3/4 Final Gate Addendum

The final `test-gap-analysis` and `assertion-quality` pass found and fixed
contract-test gaps before completion:

- history coverage now traverses two artifact pages and two job pages for
  every run, asserts the exact page sequence, and downloads all four records
  strictly by artifact ID;
- duplicate history now comes from the injected platform response instead of a
  production-only `inject_duplicate` test parameter, while truncated history
  supplies an explicit incomplete terminal page;
- reviewer bytes now contain the actual canonical Snapshot payload digest and
  assert both Snapshot and Markdown payload digests independently of the
  upload digest and artifact ID;
- the closed CLI status map now includes the record contract's `incomplete`
  terminal result as fail-closed;
- workflow contracts now pin the complete immediate DAG, require explicit
  job-level permissions, require `needs`-sourced artifact IDs, and inspect the
  protected Governance document for `live_enabled: false`.

Pseudo-mutation review after those fixes found no remaining test-only gap in the
requested scope. Executable mutations in the five orchestration APIs, six CLI
commands/status constant, and two workflows remain no-coverage production
blockers because those surfaces do not exist. Assertion review found no
assertion-free, trivial-only, type-only, or tautological test. The final tests
combine exact/deep equality, exception matching, negative scope and credential
checks, injected call logs, immutable byte/digest bindings, and state/phase
assertions.

### Final exact validation

| Command | Result |
|---|---|
| Two added phase-3/4 files | **5 passed, 27 failed**; all failures are the missing orchestration APIs or absent workflows. |
| Focused commit-8 CLI tests | **7 failed, 44 deselected**; all six commands and the closed status constant are absent. |
| Existing commit-8 contract/history/Adapter regression | **82 passed**. |
| Full Workflow Delivery v3 pytest | **2075 passed, 34 failed** in 383.25 seconds; failures are exactly the 27 phase-3/4 production blockers plus seven CLI blockers. |
| Package build | Passed; sdist and wheel built. |
| Ruff check and format check | Passed; all three touched test files are formatted. |
| `uv run --python 3.13 pyrefly check` | Passed with 0 errors. |

<!-- END APPEND: workflow-delivery-v3-commit8-phase34-final-gate-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-phase34-implementation-2026-08-13 -->

# Workflow Delivery v3 Commit 8 Phase 3/4 Implementation Evidence

## Implemented but uncommitted

- Added injectable GitHub Actions platform/history traversal and strict
  execution-history admission.
- Completed the live CLI chain, reviewer payload binding, anonymous exact-SHA
  Authorization formatter, Governance freshness/Capability admission,
  GitHub Packages publication result/Receipt handling, and live finalization.
- Added the disabled Buddy caller and reusable Attempt workflows with the
  required DAG, permission ceilings, Environments, concurrency boundaries,
  immutable ID-only artifact transport, and 45-day retention.
- Kept the protected Governance document absent. Normal live execution
  therefore fails closed before Attempt creation.
- Did not add acceptance bootstrap, CODEOWNERS scope, legacy retirement,
  activation, or live Official mutation.

## Exact validation evidence

| Validation | Result |
|---|---|
| Focused commit-8 contract/history/live/Adapter/workflow/CLI pytest | **165 passed**. |
| Focused GitHub Packages Adapter plus live scenarios after import-cycle fix | **44 passed**. |
| Full Workflow Delivery v3 pytest | **2109 passed** in 383.79 seconds. |
| CLI parser-help contract | **30 passed**. |
| `actionlint` on both Buddy workflows | Passed. |
| Ruff check and format check | Passed; **64 files formatted**. |
| Pyrefly | Passed with **0 errors**. |
| Pkl evaluation of `hk.pkl` | Passed; emitted **115602 bytes**. |
| Permanent smoke-package consumer policy | Passed with no consumers. |
| `git diff --check` | Passed. |

## Remaining validation blocker

The focused HK path gate is not clean because `typos` reports the pre-existing
hex fixture substring `ba` in
`src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py:61`.
That file is unchanged by commit 8. HK aborted later aggregate steps after this
unrelated failure; the equivalent v3 pytest, actionlint, Ruff, Pyrefly, Pkl,
and consumer-policy validations were run directly and passed.

Final independent review and repository-wide validation are not claimed
complete.

<!-- END APPEND: workflow-delivery-v3-commit8-phase34-implementation-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-phase34-test-review-2026-08-13 -->

## Commit 8 Phase 3/4 Test Gap and Assertion Review

`test-gap-analysis` and `assertion-quality` were invoked. The optional
`test-analysis-extensions` skill was unavailable, so the review used the
repository's pytest conventions directly.

- The two generated phase-3/4 files contain **20 tests**, **114 bare
  assertions**, and **5 `pytest.raises` assertions**, averaging **5.95**
  assertions per test.
- There are **0 assertion-free** and **0 trivial-only** tests.
- Pseudo-mutations for pagination truncation/duplication, artifact-name
  fallback, digest or Attempt substitution, anonymous-fetch ref fallback,
  Governance restoration in the same Attempt, no-op Capability creation,
  diagnostic denial authorization, Receipt-loss success, termination-phase
  inversion, mixed-attempt admission, permission broadening, mutable action
  pins, retention drift, activation, and later-scope additions are killed by
  explicit assertions.
- Assertions cover equality/deep structure, exceptions, negative scope and
  credential properties, state transitions, fake-client side effects,
  deterministic byte/digest identity, and workflow topology.
- No high-risk survived mutation or no-coverage zone was found in the bounded
  generated-test checklist. Final independent review is still not claimed.

<!-- END APPEND: workflow-delivery-v3-commit8-phase34-test-review-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-acceptance34-audit-2026-08-14 -->
## Commit 8 final 34-item acceptance audit

**Result:** complete; no production defect was exposed and no production file
was changed. The standalone `c8-test-map` report requested by name was not
present, so the LLD/user checklist was reconstructed into exactly 34 rows.

### Discovery and implementation delta

- Baseline focused discovery: **165 tests**.
- Final focused discovery/execution: **167 passed**.
- Delta: **+2 tests**, both in
  `tests/release/test_commit8_live_scenarios.py`.
- `test_successful_approval_only_forms_bound_authorization_without_scheduling`
  adds direct successful-approval coverage with exact Attempt, Snapshot,
  reviewer artifact, approval job, Environment, and zero scheduling assertions.
- `test_capability_admission_closes_exact_planned_action_and_resource_sets`
  adds exact non-empty action digest, artifact digest, complete resource-key
  set, lock group, and capability-group manifest assertions.
- The existing `_closure` fixture was strengthened to construct a valid live
  Release Artifact (live purpose, exact transport basename, recomputed
  provenance digest), enabling the real non-empty Capability path.

### Requirement coverage

| Requirement | Evidence |
|---|---|
| C8-A01 strict records | `test_commit8_record_contract_api_is_available`; `test_commit8_records_round_trip_through_closed_transport` |
| C8-A02 substitutions | `test_commit8_records_reject_independent_binding_substitutions`; current/history transport and digest substitution matrices |
| C8-A03 current Attempt | `test_exact_current_attempt_authority_preserves_every_trusted_binding`; `test_current_attempt_authority_rejects_every_binding_substitution` |
| C8-A04 exhaustive history | `test_history_exhausts_every_run_artifact_and_job_page_by_id` asserts every run/artifact/job cursor and immutable-ID download |
| C8-A05 same-run prior attempts | `test_same_run_history_requires_verified_earlier_attempt_existence` |
| C8-A06 unsupported provenance | `test_history_producer_and_workflow_claims_remain_diagnostic_only`; `test_same_run_prior_attempt_remains_history_only_without_provenance_claims` |
| C8-A07 approval success only | `test_successful_approval_only_forms_bound_authorization_without_scheduling`; `test_diagnostic_review_cannot_authorize` |
| C8-A08 denial diagnostic-only | `test_diagnostic_only_rejection_never_authorizes_or_starts_capability` |
| C8-A09 mutual exclusion | The preceding success/denial tests prove only `success` creates Authorization and denial cannot schedule; no Approval Outcome Evidence type is exported in the first-slice contract |
| C8-A10 exact Capability closure | `test_capability_admission_closes_exact_planned_action_and_resource_sets` |
| C8-A11 immediate Governance freshness | `test_governance_freshness_substitution_blocks_and_restoration_needs_new_attempt` covers disabled, expired, resolved commit, blob, content, and binding substitutions |
| C8-A12 restoration/new Attempt | Same test asserts restored Attempt differs and alone authorizes |
| C8-A13 anonymous exact SHA | `test_anonymous_fetch_rejects_every_non_exact_target_without_transport`; `test_anonymous_fetch_verifies_exact_commit_and_detached_head_without_network`; workflow contract counterpart |
| C8-A14 workflow permissions | `test_buddy_permission_ceiling_and_effective_permissions_are_exact` |
| C8-A15 reusable ceiling | Same test asserts caller ceiling is exactly contents-read/actions-read/packages-write and every called job narrows it |
| C8-A16 absent/create | `test_materialize_hypothetical_actions_accepts_only_absent_and_exact` plus `test_publish_uses_exact_argv_private_config_and_cleans_up` proves one create-only `npm publish` |
| C8-A17 approved exact no-op | `test_exact_noop_still_requires_authorization_and_emits_no_capability`; contract-level counterpart |
| C8-A18 partial/no mutation | `test_materialize_hypothetical_actions_accepts_only_absent_and_exact` rejects partial; six-state Adapter classification test pins `partial` |
| C8-A19 conflict/no mutation | Same materialization test rejects conflict; `test_publish_identical_and_differing_races_fail_closed` asserts no-side-effect |
| C8-A20 unknown/no mutation | Same materialization test rejects unknown; six-state classification pins `unknown` |
| C8-A21 unprovable/no mutation | Same materialization test rejects unprovable; six-state classification pins `unprovable` |
| C8-A22 identical race | `test_publish_identical_and_differing_races_fail_closed` asserts failed/no-side-effect and no Receipt |
| C8-A23 differing race | Same test's differing case asserts failed/no-side-effect and no Receipt |
| C8-A24 lost response/Receipt/bindings | `test_publish_created_conflict_and_lost_response_are_distinct`; `test_publish_rejects_receipt_and_response_substitution`; `test_receipt_loss_after_possible_mutation_requires_reobservation` |
| C8-A25 exact group equality | `test_group_bundle_requires_exact_action_set_equality` |
| C8-A26 pre-Capability termination | `test_platform_termination_mapping_is_capability_phase_exact[False-...]` |
| C8-A27 post-Capability termination | `test_platform_termination_mapping_is_capability_phase_exact[True-...]` |
| C8-A28 whole-release replay | `test_whole_release_replay_rejects_mixed_attempt_capability_records` |
| C8-A29 mixed failed-job rejection | Same test requires `Mixed-attempt failed-job reruns` rejection |
| C8-A30 keys versus projection | `test_complete_keys_remain_distinct_while_grouping_is_conservative`; contract-level Buddy key test |
| C8-A31 caller-held Execution concurrency | `test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact` asserts concurrency on the reusable caller with `cancel-in-progress: false` |
| C8-A32 ID-only/exact reviewer bytes | `test_reviewer_artifact_preserves_exact_bytes_and_all_digest_bindings`; `test_reviewer_artifact_transport_is_raw_id_bound_and_retained_45_days` |
| C8-A33 disabled activation | `test_buddy_workflow_files_are_the_disabled_commit8_pair_only` |
| C8-A34 no commit-9+ scope | `test_workflows_forbid_secrets_oidc_publication_bypasses_and_later_scope`; absence of acceptance workflow is asserted by the disabled-pair test |

### Mandatory pre-completion review

`test-gap-analysis` and `assertion-quality` were invoked. Their required
`test-analysis-extensions` helper is unavailable, so Python/pytest analysis was
completed inline.

- **Pseudo-mutation finding fixed:** replacing successful Authorization output
  with a default/unbound record, or deleting any non-empty Capability closure
  projection, previously lacked direct scenario assertions. The two added tests
  now kill those mutations through exact values and deep tuples.
- Existing matrices kill current/history binding substitution, Governance
  freshness removal, race-result inversion, Receipt acceptance without durable
  binding, group subset/superset acceptance, and termination-phase inversion.
- No remaining high-risk survived mutation was found inside the 34-row scope.
- **Assertion-depth result:** neither new test is assertion-free,
  trivial-only, self-referential, or tautological. Both assert concrete primary
  values and secondary observables (scheduler call list or complete closure
  tuples). No weakening or extra test was needed.

### Validation commands

| Command | Result |
|---|---|
| Focused six-file pytest | **167 passed** in 7.42s |
| Focused Ruff check | Passed |
| Focused Ruff format-check | Initially identified one line-wrap only; corrected with `apply_patch`; final rerun recorded below |
| `uv run --python 3.13 pyrefly check` | Passed: **0 errors** (76 suppressed, 149 warnings not shown) |
| `mise exec -- actionlint` on both Buddy workflows | Passed |

No commit, checkout, restore, reset, clean, stash, tracked deletion, external
network call, package publication, or activation was performed.

<!-- END APPEND: workflow-delivery-v3-commit8-acceptance34-audit-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-acceptance34-final-gate-2026-08-14 -->
### Final gate rerun

- Final collection: **167 tests collected** in 0.08s (baseline 165; delta +2).
- Final focused execution: **167 passed** in 6.62s.
- Ruff: all checks passed; **6 files already formatted**.
- Pyrefly: **0 errors**.
- actionlint: both relevant workflows passed.
- `git diff --check`: passed. `git status --short` confirmed the pre-existing
  authoritative modifications/untracked commit-8 files remain present; nothing
  was restored, deleted, committed, or otherwise destructively altered.
- Blockers: none. Production fixes: none.

<!-- END APPEND: workflow-delivery-v3-commit8-acceptance34-final-gate-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-parent-verification-2026-08-13 -->
### Parent verification

- Invoked `test-gap-analysis` and `assertion-quality` after the final test edits.
  Their required `test-analysis-extensions` dependency returned `not found`, so
  the recorded Python/pytest inline reviews remain the applicable equivalent.
  Reinspection confirmed the two added scenarios kill the previously identified
  default/unbound Authorization and dropped Capability-closure mutations, use
  concrete deep equality plus secondary side-effect/closure assertions, and
  contain no assertion-free, trivial-only, or tautological checks.
- Focused six-file pytest rerun: **167 passed in 7.74s**.
- Focused Ruff check and format-check: passed; **6 files already formatted**.
- Pyrefly: **0 errors** (76 suppressed, 149 warnings not shown).
- actionlint on both commit-8 workflows: passed.
- `git diff --check`: passed before this documentation-only append.

<!-- END APPEND: workflow-delivery-v3-commit8-parent-verification-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-redacted-auth-review-2026-08-13 -->
### Commit 8 redacted Authorization review

**Result:** complete; both reported findings are invalid. No production file or
test file was changed for this review.

#### Acceptance/evidence

| Requirement | Evidence |
|---|---|
| Preserve existing uncommitted work | Final `git status --short` still shows the pre-existing modified/untracked commit-8 files; no restore/clean/revert/reset/stash/checkout/commit was run. |
| Assess `github_packages.py` live GitHub REST headers | Source inspection shows `github_api_headers(token)` / `_github_transport_headers(token)` build `Authorization: Bearer {token}`. The retained-evidence helper is separate: `_retained_github_headers()` returns only `_REDACTED`. |
| Assess `github_packages.py` live npm headers | Source inspection shows `_npm_transport_headers(token)` builds `Authorization: Bearer {token}`; redirect tests retain it only on same-origin redirects and strip it cross-origin. |
| Assess `platform/github.py` live REST client | Source inspection shows `GitHubRestClient._request()` builds `Authorization: Bearer {self._token}`. |
| Avoid token leakage into diagnostics/evidence | Existing `test_github_packages_redacts_token_and_rejects_cross_origin_redirect` and retained-header separation cover redacted diagnostics/evidence; no real token was added to docs or tests. |
| Production-fix decision | No fix applied: the apparent `******` values are display/log redaction of bearer-token expressions, not the source value sent to live transport. |

#### Exact counts and validation

| Command | Result |
|---|---|
| `uv run --python 3.13 pytest src/public/lib/three-workflow-delivery-v3/tests/adapters/test_github_packages.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_history_admission.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | **167 passed** in 6.88s. |
| `uv run --python 3.13 ruff check ...six focused files...` | Passed: all checks passed. |
| `uv run --python 3.13 ruff format --check ...six focused files...` | Passed: 6 files already formatted. |
| `uv run --python 3.13 pyrefly check` | Passed: 0 errors (76 suppressed, 149 warnings not shown). |
| `mise exec -- actionlint .github/workflows/workflow-delivery-v3-buddy-smoke.yml .github/workflows/workflow-delivery-v3-live-attempt.yml` | Passed. |
| `git --no-pager diff --check` | Passed. |

Blockers: none.

<!-- END APPEND: workflow-delivery-v3-commit8-redacted-auth-review-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-core-review-correction-2026-08-13 -->

## Commit 8 independent-review findings correction

The earlier 34-item completion and “no production defect” claims are
superseded for the duration of this remediation. Independent adjudication
retained two false positives:

1. keep the Planner field name `release-binding-digest`;
2. do not add same-domain-Attempt resume behavior based on the original G1
   claim.

The adjudicated core true positives are now remediated:

- outbound GitHub REST and GitHub Packages transports use the stored Bearer
  token while retained headers and diagnostics remain redacted;
- authenticated redirects and REST/package pagination fail closed on malformed,
  cyclic, off-origin, over-limit, truncated, or changing-count chains;
- public publication requires the complete authority closure and validates the
  exact safe tarball bytes, package identity, version, witness, action, keys,
  lock projection, and capability-group manifest before credentials or npm;
- Publication Snapshot, Capability Admission, Action Result, Receipt, transport,
  Governance freshness, and finalizer bindings are recomputed exactly;
- history discovery filters other targets, admits only recognized strict
  schemas, derives conservative context-owned phase facts, and separately
  proves same-run earlier attempts without artifact-to-attempt provenance.

Validation after remediation:

| Command set | Result |
|---|---|
| Commit-8 Adapter/platform/contract/history/live/CLI tests | **179 passed** |
| Affected commit-6 and commit-7 regressions | **52 passed** |
| Existing Buddy workflow contract tests | **8 passed** |
| Ruff check and format check | Passed |
| Pyrefly | **0 errors** |
| `git diff --check` | Passed |

Workflow YAML orchestration findings remain intentionally workflow-owned and
were not remediated in this core pass.

<!-- END APPEND: workflow-delivery-v3-commit8-core-review-correction-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-workflow-fix-contracts-2026-08-13 -->
## Commit 8 workflow-fix contract test pass

- Added bounded workflow and CLI contracts for the 12 requested commit-8 fixes.
- No production/workflow file was edited.
- Validation and mandatory review results follow in the next append after the
  focused run.
<!-- END APPEND: workflow-delivery-v3-commit8-workflow-fix-contracts-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-workflow-fix-results-2026-08-13 -->
### Focused validation and blocker

- Focused four-file pytest collected **137 tests**: **125 passed, 12 failed**.
- All 12 failures are the newly added workflow contract tests and expose
  production/workflow blockers; the CLI status-evidence test and existing live
  scenarios pass.
- Production changes are required to satisfy the contracts. Per user
  instruction, no production or workflow edit was attempted.
- Ruff initially reported one quote-style issue in a new assertion; it was
  corrected with `apply_patch`.

### Mandatory final-test review

- `test-gap-analysis` was explicitly invoked. Its requested
  `test-analysis-extensions` helper is unavailable, so the Python/pytest
  pseudo-mutation review was completed inline.
- The 12 workflow contracts kill mutations of the exact result literal,
  no-op/finalizer condition, platform fact flags, receipt/bundle ordering and
  transport IDs, failure continuation, retained outcome/summary uploads,
  offline formatter invocation, job/check-run correlation, freshness evidence
  ordering, missing-Authorization dependency routing, retention, and status
  artifact paths. No requested behavior is represented only by an existence
  assertion.
- `assertion-quality` was explicitly invoked. The same unavailable helper was
  noted and pytest assertions were reviewed inline: the 13 added tests contain
  **59 concrete assertions plus one `pytest.raises` context**, with zero
  assertion-free, trivial-only, null-only, or tautological tests. They combine
  exact equality, negative string checks, collection/deep structure, order,
  permissions, paths, and error-propagation observables.
- Remaining gaps are not test gaps: they are the 12 accurately failing
  workflow implementation contracts listed by pytest.
<!-- END APPEND: workflow-delivery-v3-commit8-workflow-fix-results-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-workflow-remediation-2026-08-13 -->

## Commit 8 workflow/orchestration remediation

Status: **complete; no blockers**. This evidence supersedes the immediately
preceding 12-failure workflow blocker while preserving its history.

### Implemented corrections

- Capability results and the publisher gate now use the exact
  `success`/`blocked` vocabulary.
- Approved exact pre-observed state skips Capability admission and publication,
  conditionally omits Capability inputs, and finalizes as `finalized-no-op`.
- Exact-attempt Jobs API phase facts plus the durable capability-job marker
  distinguish a publisher that did not start from cancelled or ambiguous work
  that may have reached Capability.
- Publication now persists the single-file Receipt first, then forms the
  Action Result and exactly one capability-group bundle with the returned
  artifact ID, upload digest, and payload digest. Failed/incomplete publication
  forms and uploads its bundle before failure propagation and emits no fake
  Receipt.
- Release finalization retains the exact Attempt Outcome JSON and summary
  Markdown together in one immutable, run-attempt-unique 45-day artifact for
  every executable finalizer path.
- Approval uses a dependency-free exact-revision formatter under isolated
  Python, with no pip, index, cache, or installed-package dependency.
- Approval job identity is anonymously correlated from the exact run-attempt
  Jobs API response and its documented `check_run_url`; no fabricated fallback
  remains.
- Governance freshness blocks are named and uploaded before nonzero status
  propagation. Missing Authorization reaches the finalizer as
  `unknown-replayable-approval-contract`.

### Exact validation

| Command | Result |
|---|---|
| Focused commit-8 contract/history/live/platform/CLI pytest | **179 passed** |
| Full Workflow Delivery v3 pytest | **2146 passed** in 351.25 seconds |
| Ruff check and format check | Passed; **57 files formatted** |
| Pyrefly package check | Passed; **0 errors** |
| actionlint on both v3 Buddy workflows | Passed |
| `pkl eval -f json global.pkl` | Passed |
| `git diff --check` | Passed |

No commit was created. Commit 9+, acceptance, activation, CODEOWNERS, and
legacy retirement were not added.

Post-implementation pseudo-mutation and assertion-depth review found no
remaining high-risk gap in the requested scope. Exact comparisons, no-op
Capability omission, phase inversion, placeholder artifact IDs, Receipt/bundle
reordering, failure propagation before evidence upload, pip/index reintroduction,
job-identity fallback, and missing final status retention are all killed by
named contract or scenario assertions. The focused tests use equality,
negative, structural, ordering, exception, and state/side-effect assertions;
none of the added tests is assertion-free, trivial-only, self-referential, or
tautological. The optional `test-analysis-extensions` helper was unavailable,
so the Python/pytest review used repository conventions directly.

<!-- END APPEND: workflow-delivery-v3-commit8-workflow-remediation-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-second-round-remediation-2026-08-13 -->

## Commit 8 second-round adjudicated remediation

Status: **complete; no blockers**. This section supersedes the earlier
commit-8 no-blocker/final-closure claims only for the second-round review and
records the adjudicated result as **14 true positives and 1 false positive**.
Prior status history remains unchanged above.

### Adjudication preservation

- The false-positive token claim was not implemented: the existing credential
  transport helpers already send the real token while retained diagnostics and
  tool output remain redacted.
- Planner `release-binding-digest` behavior was preserved.
- No activation, acceptance bootstrap, CODEOWNERS expansion, legacy
  retirement, commit-9 scope, or commit was added.

### Remediated true positives

1. CLI live observation/publication now select the manual-redirect
   `GitHubPackagesHttpTransport`; the legacy urlopen transport was removed.
2. Publication validates the exact Qualification Snapshot, Decision, Artifact,
   expectation, Publication Snapshot, selected action, Attempt, projection,
   and digests before config creation, transport, or runner execution.
3. Publication Snapshot now retains closed immutable observation references
   and requires exact planned coverage plus exact absent/action equality.
4. History normalizes the literal YAML finalizer/publisher display names with
   an optional reusable-workflow prefix.
5. History keeps finalizer/publisher facts optional and admits failed,
   cancelled, blocked, unknown, and possibly-mutated durable outcomes without
   requiring successful Finalizer existence.
6. Same-run history classifies candidates first, queries only referenced prior
   attempts once, and attaches matching prior-attempt jobs; an explicit empty
   complete response can prove existence.
7. Finalization preserves and validates Receipt artifact ID, artifact name,
   upload digest, payload digest, and decoded Receipt digest exactly.
8. Missing Authorization plus post-authorization evidence is contradictory and
   finalizes as possibly mutated with mandatory reobservation.
9. Release Finalizer has no Actions permission and uses retained needs/start
   facts conservatively.
10. Workflow API correlation uses the exact reusable composite job names and
    rejects zero, duplicate, and wrong-prefix matches.
11. Pre-mutation publisher admission/Governance failure emits a closed
    failed/no-side-effect execution state before propagation.
12. Post-execution failed/incomplete publication retains CLI status, forms a
    nonempty bundle path/digest/output, uploads under `always`, then propagates
    the saved failure.
13. Every successful, failed, or no-op finalizer path uploads AttemptOutcome
    plus summary with exact permissions and 45-day retention.
14. Focused contract/history/live/Adapter/workflow/CLI regressions cover the
    complete second-round behavior, including observation/action mismatch,
    candidate-specific attempts, Receipt transport substitution, contradictory
    Authorization absence, and durable failed bundles.

### Exact validation

| Command scope | Result |
|---|---|
| Focused commit-8 contract/history/live/Adapter/workflow/CLI | **210 passed** |
| Focused commit-6/7/8 regression set | **274 passed** |
| Full Workflow Delivery v3 suite | **2159 passed** in 351.40 seconds |
| Scoped Ruff check | Passed |
| Scoped Ruff format check | **12 files already formatted** |
| Pyrefly | **0 errors** |
| actionlint on both commit-8 workflows | Passed |
| `pkl eval -f json global.pkl` | Passed |
| `git diff --check` | Passed |

Blockers: **none**. No commit was created.

<!-- END APPEND: workflow-delivery-v3-commit8-second-round-remediation-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-third-round-remediation-2026-08-13 -->

## Commit 8 third-round adjudicated remediation

Status: **complete; no blockers**. This append preserves all prior evidence and
records the third-round adjudication as **3 true positives and 1 false
positive**.

### Adjudication preservation

- The repeated token-rendering claim remains a false positive and was not
  changed. Runtime adjudication proved that actual transport requests use the
  real credential while retained tool output and diagnostics are redacted.
- No activation, acceptance, CODEOWNERS, legacy retirement, commit-9 scope, or
  commit was added.

### Remediated true positives

1. Historical discovery now treats every recognized payload run-attempt value
   only as an exact-attempt query selector. It obtains and caches one
   artifact-independent `GitHubRunAttemptFact` plus exact-attempt Jobs pages per
   `(run_id, run_attempt)`, validates run ID, node ID, head SHA, attempt, and
   status metadata, and uses the listed attempt only as the latest watermark.
   A non-current run listed at attempt 3 now admits retained attempt-1 and
   attempt-3 candidates independently. Missing, malformed, mismatched, 404-like,
   or cross-head exact-attempt proof fails closed without adding artifact
   provenance claims.
2. GitHub Packages publication now has a mutation-free preflight followed by an
   immutable uploaded mutation-may-have-started marker binding the Attempt,
   Publication Snapshot, action digest, complete lock group, and preflight
   digest. The publisher requires the admitted marker transport before npm.
   The always-running terminal bundle derivation no longer synthesizes generic
   no-side-effect state: missing, truncated, substituted, Receipt-lost, or
   persistence-failed state after the marker becomes
   incomplete/possibly-mutated; only preflight failure before a marker or the
   narrow create-conflict fact can prove no side effect.
3. Release-finalizer workflow derivation now computes platform termination and
   capability-start evidence independently. Cancellation or publisher failure
   without a durable bundle sets platform termination; the uploaded start
   marker independently sets capability-may-have-started. Failure plus marker
   plus no bundle therefore supplies both facts and finalizes as
   incomplete-possibly-mutated/post-capability-termination with
   reobserve-and-replay, while a skipped publisher supplies neither.

### Added regression evidence

- Exact REST-client and history tests cover exact-attempt endpoint shape,
  per-run/per-attempt cache keys, distinct same-number attempts in different
  runs, attempt-1 plus attempt-3 admission under a latest-attempt-3 watermark,
  and malformed or mismatched run/node/head/attempt/status facts.
- Workflow and live-scenario truth tables cover independent termination/start
  derivation and skipped publication.
- Crash-state tests cover interruption after marker before npm, runner
  mutation followed by raise, generic nonzero/lost response/readback failure,
  Receipt persistence failure, execution-state persistence failure, and
  truncated/substituted terminal state. Every post-marker case remains
  possibly mutated. Preflight/Governance failure has no marker and forms
  failed/no-side-effect evidence without invoking npm.

### Exact validation

| Command scope | Result |
|---|---|
| Focused commit-8 history/platform/Adapter/workflow/live/CLI plus public-export regression | **203 passed** |
| Affected commit-6 and commit-7 regressions | **64 passed** |
| Full Workflow Delivery v3 suite | **2184 passed** in 382.07 seconds |
| Scoped Ruff check | Passed |
| Scoped Ruff format check | **13 files already formatted** |
| Pyrefly | **0 errors** |
| actionlint on both commit-8 workflows | Passed |
| `pkl eval -f json global.pkl` | Passed |
| `git diff --check` | Passed before this append |

Blockers: **none**. No commit was created.

<!-- END APPEND: workflow-delivery-v3-commit8-third-round-remediation-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-fourth-round-governance-recheck-2026-08-13 -->
## Commit 8 fourth-round Governance publish recheck status

Status: **test-first regressions complete; production blocker intentionally
retained**. No production, workflow, adjudicated artifact-attempt finding, or
version-control state was modified.

### Implemented regression evidence

- Added
  `tests/adapters/test_commit8_publish_governance_recheck.py` with seven
  collected publish-boundary cases: explicit reader seam, disabled, expired,
  resolved-commit changed, blob-OID changed, content changed, unchanged, and
  exact marker/read/runner ordering.
- Appended
  `test_after_marker_governance_failure_requires_reobservation` to
  `tests/release/test_commit8_live_scenarios.py`.
- The publish regressions use an actual immutable preflight/marker record,
  fixed repository/ref/path constants, canonical Governance bytes, injected
  protected-source and runner fakes, exact event ordering, runner call counts,
  and no network or subprocess.
- Current production lacks `governance_client` and
  `governance_observed_at` on `publish_github_packages_action`; the seven new
  adapter test items therefore remain intentionally red. The live finalization
  regression passes.

### Pre-completion self-review

- `test-gap-analysis` was invoked. Pseudo-mutation review found that testing
  only resolved-commit provenance would allow a dropped blob-OID comparison to
  survive, so a distinct `blob-oid-changed` case was added. Disabled, expiry,
  both provenance components, content digest, omitted recheck, reordered
  pre-marker recheck, dropped runner suppression, and duplicate runner
  invocation are now each pinned by concrete inputs, event order, or call
  counts. No remaining in-scope pseudo-mutation gap was found; execution is
  blocked only by the deliberately missing production seam.
- `assertion-quality` was invoked. Generated tests contain no assertion-free,
  null-only, type-only, or tautological cases. A tautological parameter-name
  assertion was removed. The final set combines exception assertions, exact
  structural event equality, fixed-source argument equality, negative
  side-effect checks, exact call counts, and four-field final outcome checks.
- Prompt-scenario mapping was rechecked against the final test names and all
  requested behavior rows have dedicated evidence.

### Exact validation

| Command | Result |
|---|---|
| `uv build --package three-workflow-delivery-v3` | Passed; sdist and wheel built |
| `uv run --python 3.13 ruff check <two changed test files>` | Passed |
| `uv run --python 3.13 ruff format --check <two changed test files>` | Passed; 2 files already formatted |
| `uv run --python 3.13 pyrefly check` | Passed; 0 errors |
| Focused adapter + live pytest | **7 failed, 39 passed**; all seven failures identify the missing publish Governance seam |
| Full Workflow Delivery v3 pytest | **7 failed, 2185 passed** in 383.49 seconds; no other failure |

### Requirement coverage

| Requirement | Evidence |
|---|---|
| Second fresh read after marker and before runner | `test_publish_unchanged_second_governance_read_runs_exactly_once`; exact event sequence starts with `marker-admitted`, then fixed-source protected/resolve/read, then `runner.run` |
| Disabled/expired/provenance/content changes run zero times | `test_publish_second_governance_read_blocks_before_runner[disabled]`, `[expired]`, `[resolved-commit-changed]`, `[blob-oid-changed]`, and `[content-changed]`; each asserts `runner.calls == 0` |
| Unchanged runs once | `test_publish_unchanged_second_governance_read_runs_exactly_once` asserts `runner.calls == 1` |
| After-marker failure finalization | `test_after_marker_governance_failure_requires_reobservation` asserts incomplete-possibly-mutated, post-capability termination, `possibly_mutated is True`, and `reobserve-and-replay` |
| Recheck belongs to publish, not preflight | `test_publish_api_requires_fresh_governance_reader_seam` and all behavioral calls target `publish_github_packages_action` |
| Preserve artifact-attempt FP | No artifact/history finding or production file changed |
| Pytest conventions | New adapter pytest file, parameterized cases, injected fakes, concrete assertions; Ruff passed |
| Preserve workspace/no commit | No restore, delete, revert, reset, clean, stash, checkout, or commit command was used |
| Append-only agent artifacts | Bounded append sections added to research, plan, and status; no prior content truncated |
| Missing API remains meaningful failure | Seven exact failures report the absent publish reader parameters/keyword rather than weakening to a preflight-only assertion |
<!-- END APPEND: workflow-delivery-v3-commit8-fourth-round-governance-recheck-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-fourth-round-adjudicated-fix-2026-08-13 -->

## Commit 8 fourth-round adjudicated remediation

Status: **complete; no blockers**. This append supersedes only the preceding
test-first production-blocker status and records the fourth-round adjudication
as **1 true positive and 1 false positive**.

### Adjudication preservation

- The single Governance publish-path freshness true positive was remediated.
- The artifact-attempt finding remains the adjudicated false positive and was
  not changed.
- No history rewrite, activation, acceptance bootstrap, legacy retirement,
  commit-9 scope, or commit was performed.

### Remediation

- Extracted one canonical fixed-source Governance provenance/freshness
  comparison and reused it for publisher preflight and the actual publish path.
- The immutable publish preflight now carries the exact Governance provenance,
  content digest, expiry, and live-enabled identity admitted by the
  Capability Admission Decision.
- After durable mutation-start marker admission, the publish path freshly
  resolves and reads the policy-fixed protected Governance source at the last
  possible point immediately before `runner.run`. It requires exact
  repository/ref/path, resolved commit, blob OID, content digest, expiry,
  current validity, `live_enabled: true`, and identity equality with the
  admitted Capability Decision and preflight.
- Any second-read disablement, expiry, provenance/content substitution, or
  preflight/Capability mismatch raises before npm. Because the durable marker
  already exists, the workflow's always-running result derivation retains
  incomplete/possibly-mutated and reobserve-and-replay semantics.
- Handoff and slice LLD wording now distinguishes architecture-wide optional
  repetition from the repetition elected and required by this slice.

### Regression evidence

- `test_publish_second_governance_read_blocks_before_runner[disabled]`
- `test_publish_second_governance_read_blocks_before_runner[expired]`
- `test_publish_second_governance_read_blocks_before_runner[resolved-commit-changed]`
- `test_publish_second_governance_read_blocks_before_runner[blob-oid-changed]`
- `test_publish_second_governance_read_blocks_before_runner[content-changed]`
- `test_publish_unchanged_second_governance_read_runs_exactly_once`
- `test_publish_api_requires_fresh_governance_reader_seam`
- `test_after_marker_governance_failure_requires_reobservation`

Each publish behavior test first executes a successful real preflight
Governance observation, then exercises the second read through
`publish_github_packages_action`. Exact event ordering proves marker admission,
fixed-source protected/resolve/read, and only then the single runner call.

### Test quality review

- Pseudo-mutation review found no remaining requested gap: removing the second
  read, moving it before marker admission, dropping live/expiry/commit/blob/
  content comparisons, accepting a changed preflight, invoking npm on failure,
  or invoking npm twice changes a concrete assertion.
- Assertion review found no assertion-free, trivial-only, self-referential, or
  tautological generated test. The tests combine exception, equality,
  structural ordering, negative side-effect, exact-call-count, and final-state
  assertions. The optional `test-analysis-extensions` helper was unavailable,
  so Python/pytest conventions were reviewed directly.

### Exact validation

| Command scope | Result |
|---|---|
| Focused commit-8 Adapter/contract/history/live/workflow/CLI suite | **236 passed** |
| Full Workflow Delivery v3 suite | **2192 passed** in 383.64 seconds |
| Scoped Ruff check | Passed |
| Scoped Ruff format check | **6 files already formatted** |
| Pyrefly | **0 errors** |
| actionlint on both commit-8 workflows | Passed |
| `git diff --check` | Passed after this append |

Blockers: **none**. No commit was created.

<!-- END APPEND: workflow-delivery-v3-commit8-fourth-round-adjudicated-fix-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-fifth-round-adjudicated-fix-2026-08-13 -->

## Commit 8 fifth-round adjudicated remediation

Status: **complete; no blockers**. This append records the single fifth-round
non-blocking true positive.

### Remediation

- Added the narrow
  `PublisherGovernanceRecheckRejectionError`, carrying an exact typed
  `DeferredPublicationExecutionResult` only when a valid second Governance
  observation is rejected after mutation-marker admission and before
  `runner.run`.
- Added the closed diagnostic
  `governance-recheck-failed-before-runner`. Its terminal classification is
  exactly `failed` plus `no-side-effect`, with no observation, response
  identity, or Receipt.
- The CLI catches only that adapter exception, persists the closed publication
  execution state, records its digest, and returns nonzero. Generic
  missing/unreadable/malformed/post-marker exceptions are not converted into
  this proof.
- Marker-present result formation admits `no-side-effect` only for the exact
  Governance diagnostic and the existing proven `create-conflict` case.
  Lookalikes and invalid outcome/disposition combinations fall back to
  `incomplete` plus `possibly-mutated`.
- Finalization maps the exact publisher Governance rejection to
  `capability-blocked`, `failure`, and `new-attempt`. Ordinary failed
  no-side-effect publication remains finalized and replayable.
- Existing worktree edits were preserved. No history rewrite, reset, checkout,
  clean, stash, commit, activation, or later-commit work was performed.

### Regression evidence

- `test_publish_second_governance_read_returns_terminal_no_side_effect`
  covers disabled, expired, resolved-commit substitution, blob-OID
  substitution, and content substitution; every case asserts the typed
  exception/result and `runner.calls == 0`.
- `test_publish_unchanged_second_governance_read_runs_exactly_once` retains the
  exact marker/read/runner ordering and one runner call.
- `test_publish_cli_persists_governance_terminal_state_before_nonzero` proves
  state persistence before nonzero return and proves a generic `ValueError` is
  not caught or persisted as the typed terminal condition.
- `test_post_marker_no_side_effect_terminal_state_allowlist_forms_failed_bundle`
  accepts only the exact Governance diagnostic and `create-conflict`.
- `test_post_marker_governance_terminal_state_lookalikes_are_possibly_mutated`
  rejects diagnostic and outcome/disposition lookalikes.
- `test_start_marker_without_valid_terminal_state_additional_conservative_cases`
  retains unreadable, malformed, and generic states as incomplete and
  possibly mutated.
- `test_publisher_governance_blocked_bundle_requires_new_attempt` and
  `test_non_governance_failed_bundle_remains_replayable` pin the distinct final
  Attempt semantics.

### Exact validation

| Command scope | Result |
|---|---|
| Focused fifth-round adapter/CLI/live regressions | **122 passed** |
| Focused commit-8 Adapter/contract/history/live/workflow/CLI suite | **267 passed** |
| Full Workflow Delivery v3 suite | **2211 passed** in 383.64 seconds |
| Scoped Ruff check | Passed |
| Scoped Ruff format check | **9 files already formatted** |
| Pyrefly | **0 errors** |
| actionlint on both commit-8 workflows | Passed |

Blockers: **none**. No commit was created.

<!-- END APPEND: workflow-delivery-v3-commit8-fifth-round-adjudicated-fix-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-sixth-round-governance-rejection-fix-2026-08-13 -->

## Commit 8 Sixth-Round Governance Rejection Adjudication

**Result:** complete; the final adjudicated non-blocking true positive is
fixed. No commit, activation, acceptance bootstrap, CODEOWNERS expansion,
legacy retirement, or history rewrite was performed.

### Production correction

- Added `GovernanceRejectionError` as the definitive, authoritatively observed
  Governance rejection base; `GovernanceFreshnessRejectionError` now derives
  from it.
- `observe_governance_source` classifies only exact authoritative unprotected
  state, successfully fetched invalid canonical/schema/policy-binding/lifetime/
  inventory/attestation content, and canonical content-digest inconsistency as
  definitive rejection.
- Local fixed-source/time configuration errors, malformed protection/commit/
  blob/API identities, and transport/HTTP/permission/protocol/base64/API-JSON
  failures remain generic unknown failures.
- GitHub branch lookup now reads the authoritative exact-Boolean `protected`
  field through the contents-readable branch endpoint, preserves HTTP status
  identity, validates successful response shape, and propagates 403/404,
  server, network, and malformed-response failures as unknown.
- The publisher catches the complete `GovernanceRejectionError` family and
  retains the exact failed/no-side-effect terminal result before returning
  nonzero. Generic post-marker failures remain outside that terminal exception
  and continue through the conservative incomplete/possibly-mutated fallback.

### Requirement evidence

| Requirement | Evidence |
|---|---|
| Definitive base taxonomy | `test_governance_freshness_rejection_derives_from_definitive_base` |
| Authoritative unprotected rejection | `test_unprotected_ref_is_definitive_governance_rejection` |
| Canonical/schema/semantic/digest rejection | `test_fetched_invalid_canonical_or_schema_content_is_definitive_rejection`, `test_fetched_invalid_governance_semantics_are_definitive_rejection`, `test_fetched_content_digest_inconsistency_is_definitive_rejection` |
| Local/time/identity/transport remain unknown | `test_local_source_and_time_configuration_errors_are_not_governance_rejections`, `test_malformed_remote_identities_are_not_governance_rejections`, `test_transport_failures_are_not_governance_rejections` |
| Protection false versus unknown | `test_ref_protection_false_is_authoritative`, `test_ref_protection_http_failures_are_unknown`, `test_ref_protection_transport_unknowns_raise`, `test_ref_protection_malformed_success_response_is_unknown` |
| Successful protected response | `test_ref_protection_success_is_authoritative_true` |
| Exact publisher terminal state and zero runner | `test_publish_second_governance_read_returns_terminal_no_side_effect`; CLI persistence assertion in `test_publish_cli_persists_governance_terminal_state_before_nonzero` |
| Generic conservative fallback | `test_post_marker_governance_terminal_state_lookalikes_are_possibly_mutated`; generic CLI exception remains without a forged definitive terminal state |

### Test-quality review

`test-gap-analysis` and `assertion-quality` were invoked. Their requested
`test-analysis-extensions` dependency was unavailable, so the focused
Python/pytest review was completed inline.

- Pseudo-mutations that collapse every REST failure to `False`, remove
  `status_code` propagation, accept malformed successful protection payloads,
  invert protected/unprotected outcomes, remove the definitive base
  inheritance, wrap malformed remote identities as definitive, or let
  definitive publisher rejection reach the runner are killed by direct
  assertions.
- The focused tests combine exact type/inheritance, equality, exception,
  negative classification, call-order/zero-call side-effect, deep terminal
  state, and transport-status assertions.
- No assertion-free, trivial-only, tautological, high-risk survived, or
  no-coverage case remains in the adjudicated scope.

### Exact validation

| Command | Result |
|---|---|
| Focused nine-file commit-8 pytest | **309 passed** in 9.31 seconds. |
| Full Workflow Delivery v3 pytest | **2253 passed** in 384.23 seconds. |
| Ruff check and format check over v3 source/tests | Passed; **59 files already formatted**. |
| `uv run --python 3.13 pyrefly check` | Passed with **0 errors** (76 suppressed, 157 warnings not shown). |
| `mise exec -- actionlint` on both commit-8 Buddy workflows | Passed. |
| `git diff --check` | Recorded in the final diff gate after this append. |

Blockers: none.

<!-- END APPEND: workflow-delivery-v3-commit8-sixth-round-governance-rejection-fix-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-final-closure-2026-08-14 -->

# Workflow Delivery v3 Commit 8 Final Closure

Status: **complete**. Live activation remains disabled.

Commit 8 now provides the complete disabled first-slice live Buddy boundary:
history-only Execution admission, exact live Attempt binding, GitHub Packages
observation and create-only publication, immutable reviewer summary,
credential-free exact-SHA Authorization formation, fresh Capability admission,
durable mutation uncertainty, exact Action Result/Receipt/group bundles,
platform termination handling, live finalization, and caller/reusable workflow
permission and concurrency boundaries.

## Review closure

- Five independent GPT-5.6 Sol reviewers covered contracts/history,
  Destination Adapter security, workflow/runtime permissions, authorization and
  finalization, and holistic architecture/scope.
- Every atomic finding was independently adjudicated TP or FP.
- True positives were fixed through six remediation rounds.
- All five original reviewers explicitly reported `RAW_FINDINGS: none`.

## Final validation

| Command | Result |
|---|---|
| Full v3 pytest | `2253 passed` |
| Managed HK `v3-control-pytest` | `2253 passed` |
| Root pytest | `4288 passed` |
| `uv run --python 3.13 pyrefly check` | `0 errors` |
| V3 Ruff check and format check | Passed; 68 files formatted |
| actionlint on both Buddy workflows | Passed |
| Managed HK `pkl-eval` and `pkl-format` | Passed |
| `uv build --package three-workflow-delivery-v3` | Built sdist and wheel |
| `dotnet build dirs.proj --no-incremental` | Passed; 0 warnings and 0 errors |
| `pnpm run build` | Passed; generated smoke-package versions reset afterward |
| `uv lock --check` | Passed |
| `pnpm install --frozen-lockfile` | Passed |
| `dotnet restore --locked-mode` | Passed |
| `git diff --check` | Passed |

## Requirement evidence

| Requirement | Evidence |
|---|---|
| Strict live/history authority | Commit-8 contract and exact-attempt history suites |
| Approval and Capability closure | Authorization, Governance freshness, no-op, and substitution scenarios |
| Credential-safe GitHub Packages behavior | Authenticated redirect, pagination, observation, publication, race, and readback tests |
| Durable mutation truthfulness | Preflight/start-marker/terminal-state and crash-after-mutation scenarios |
| Exact Receipt and result bundles | Receipt transport/content cross-binding and exact action-set tests |
| Least-privilege workflow topology | Buddy workflow DAG, permission-negative, Environment, artifact, and concurrency contracts |
| Disabled activation and bounded scope | Workflow and holistic scope tests; protected Governance source remains absent |

<!-- END APPEND: workflow-delivery-v3-commit8-final-closure-2026-08-14 -->
