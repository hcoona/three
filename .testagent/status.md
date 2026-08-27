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

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-codeowners-tests-2026-08-14 -->
# Workflow Delivery v3 Commit 9 CODEOWNERS Test Status

**Result:** test-generation portion complete within the hard edit boundary.
The focused suite intentionally exposes the delivered pre-commit-9
`.github/CODEOWNERS` state: 5 tests pass and the repository-wide positive
contract fails because the required production patterns are absent.

## Files

- Added
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py`.
- Appended commit-9 sections to `.testagent/research.md`,
  `.testagent/plan.md`, and this status file.
- No production, CODEOWNERS, workflow, activation, acceptance, legacy, or
  unrelated file was edited.

## Validation

| Command | Result |
|---|---|
| `uv run --python 3.13 pytest src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py` | **Expected red:** 6 collected, 5 passed, 1 failed. `test_every_governed_v3_surface_resolves_finally_to_hcoona` reports 110 uncovered governed paths in the delivered CODEOWNERS state. |
| `uv run ruff check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py` | Passed. |
| `git diff --check` | Passed before this append; rerun in final boundary check. |

## Pre-completion review

- `test-gap-analysis` was invoked. Its optional language-extension dependency
  was unavailable, so the Python pseudo-mutation review was completed inline.
  Removing a required rule, changing final-match ordering, dropping synthesized
  descriptor discovery, substituting the protected Governance path, or adding
  CODEOWNERS coupling to runtime eligibility is caught by concrete assertions.
  No remaining in-scope high-risk mutation gap was found.
- `assertion-quality` was invoked. Its optional language-extension dependency
  was unavailable, so pytest assertions were classified inline. All six
  collected cases contain concrete equality, collection, negative, or
  structural assertions. There are no assertion-free, trivial-only,
  tautological, or non-null-only tests.

## Requirement coverage

| Requirement | Evidence |
|---|---|
| Every actual governed surface and exact absent Governance path finally resolves to `@hcoona` | `test_every_governed_v3_surface_resolves_finally_to_hcoona` inventories requested categories, proves the Governance path is absent, and currently fails on the missing production rules. |
| Newly synthesized descriptors are discovered and covered | `test_new_descriptor_paths_are_discovered_and_owned` checks new nested release-unit and quality paths. |
| Missing coverage fails | `test_missing_required_pattern_fails_coverage` asserts the exact uncovered-path diagnostic. |
| Later overriding pattern fails | `test_later_overriding_pattern_fails_final_match_coverage` proves the last matching non-hcoona owner wins and is rejected. |
| Arbitrary-ref Buddy eligibility is unchanged and uncoupled | `test_arbitrary_ref_buddy_runtime_eligibility_is_not_codeowners_gated` accepts arbitrary branch and tag refs and verifies the runtime eligibility module has no CODEOWNERS input. |
| No network or GitHub dependency | The module reads local files and local Git inventory only; the focused run performs no network or GitHub API call. |
| Hard edit boundary and append-only state | Final `git diff --name-only`, status, and append-prefix verification. |
<!-- END APPEND: workflow-delivery-v3-commit9-codeowners-tests-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-codeowners-review-2026-08-14 -->
## Commit 9 final review addendum

- The available `test-gap-analysis` and `assertion-quality` skills were invoked
  after implementation. Their shared `test-analysis-extensions` dependency was
  unavailable, so the focused Python review was completed inline.
- The pseudo-mutation review found one discovery-vacuity gap: deleting all v3
  action discovery could survive because that category was not required to be
  nonempty. The positive contract now explicitly requires `v3-actions`, so the
  mutation is killed before final ownership evaluation.
- Assertion review found no assertion-free, trivial-only, tautological, or
  non-null-only cases. Assertions pin concrete path sets, final owner tuples,
  exact uncovered-path diagnostics, protected-path absence, and arbitrary-ref
  return values.
<!-- END APPEND: workflow-delivery-v3-commit9-codeowners-review-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-action-inventory-2026-08-14 -->
## Delivered action inventory clarification

The delivered workspace contains no actual `.github/actions/**` files. The
temporary nonempty-action discovery assertion was therefore removed: the
governed union still includes every action discovered when such files exist,
while the current authoritative empty action inventory is not fabricated or
restored. Existing `/.github/actions/** @hcoona` ownership remains exercised by
the complete-rule synthetic contracts.
<!-- END APPEND: workflow-delivery-v3-commit9-action-inventory-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-exact-final-owner-2026-08-14 -->
## Exact final-owner review

The final pseudo-mutation pass identified that checking only for membership of
`@hcoona` would admit a later rule that added another owner. Coverage now
requires the exact final owner tuple `("@hcoona",)`, and
`test_later_overriding_pattern_fails_final_match_coverage` includes a concrete
later co-owner regression in addition to the replacement-owner case.
<!-- END APPEND: workflow-delivery-v3-commit9-exact-final-owner-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-final-validation-2026-08-14 -->
## Final focused validation

- Focused pytest after the exact-owner strengthening: **6 collected, 5 passed,
  1 expected contract failure** reporting the same 110 paths not yet covered by
  the delivered pre-commit-9 `.github/CODEOWNERS`.
- Ruff check and format check passed for the new test module.
- `git diff --check` passed; status contains only the new bounded test module
  and the three allowed `.testagent` files.
<!-- END APPEND: workflow-delivery-v3-commit9-final-validation-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-glob-review-2026-08-14 -->
## GitHub glob-semantics review

The final pseudo-mutation review strengthened descriptor coverage with a
descriptor directly below `src/` as well as nested synthesized paths. The
test-local matcher now treats `**/` as zero or more directories and supports
basename-only patterns, preventing a false negative for GitHub-compatible
zero-directory glob matches. Focused validation was rerun after this change.
<!-- END APPEND: workflow-delivery-v3-commit9-glob-review-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-tp-fix-results-2026-08-14 -->
## Commit 9 independently adjudicated TP-fix results

**Status: PARTIAL — test implementation is complete, but the real HK plan
exposes one production/configuration blocker that cannot be fixed inside the
strict test-only ownership boundary.**

The earlier commit-9 expected-red CODEOWNERS statement is historical:
`test_commit9_codeowners.py` is now green with **19 passed** against the actual
ordered `.github/CODEOWNERS`. The combined two-file focused suite is not green:
the actual `v3-control-pytest` matcher omits the required synthetic future
direct script `eng/scripts/workflow_delivery_v3_future.py`.

### Exact files intentionally changed

- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py`
- `src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
- `.testagent/status.md` (this append-only addendum)

`.github/CODEOWNERS`, `hk.pkl`, production/runtime sources, workflows,
activation, acceptance, legacy files, `.testagent/plan.md`, and
`.testagent/research.md` were not edited by this implementation run. Their
pre-existing parent changes remain preserved.

### Requirement-to-test evidence

| Requirement | Exact evidence | Result |
|---|---|---|
| Actual CODEOWNERS is the sole positive oracle | `test_actual_codeowners_final_owner_is_exact_for_every_current_and_future_v3_surface` parses the real file once; no completion constant/helper remains. | PASS |
| Future descriptor/workflow/action/direct-script layouts | The same test asserts all eight `SYNTHETIC_FUTURE_SURFACES`, including shallow/nested instances of both descriptors, workflow, both action layouts, and direct script. | PASS |
| Exact final owner tuple | Positive per-path equality plus `test_later_replacement_owner_override_fails_exact_final_match` and `test_later_hcoona_coowner_override_fails_exact_final_match`. | PASS |
| Actual-rule removals fail | Thirteen cases of `test_removing_each_actual_governing_rule_exposes_its_exact_surface` remove real parsed rules and assert exact uncovered paths/owner tuples. | PASS |
| Shared CODEOWNERS/HK inventory | `_governed_surface_inventory()` is loaded by `test_hk_trigger.py`; category and explicit-path assertions prevent silent omission. | PASS |
| Add/modify/delete history | `test_real_v3_control_pytest_selects_every_codeowners_surface_for_history_kind` uses real Git, range helper, and actual HK plan. | BLOCKED: each case reports 125 of 126 surfaces. |
| Rename-out/rename-in history | `test_real_v3_control_pytest_selects_governed_side_of_batched_rename` asserts both names from the real helper and one governed-side count. | BLOCKED: each case reports 125 of 126 surfaces. |
| Safe HK/helper execution | `_execution_copies` and `_restore_execution_copies` preserve uncommitted executable copies of `hk.pkl`, the range helper, and imported HK support after committed delete/rename-out histories; base/head assertions remain unchanged. | PASS |
| Negative unrelated source and `--all` | Existing real-HK tests remain present and pass in the combined run. | PASS |
| Public branch/tag normalization | `test_public_cli_normalizes_arbitrary_buddy_branch_and_tag_without_codeowners_gate` calls `cli.main`, asserts the exact canonical Intent fields for both refs, and installs fail-fast network sentinels. | PASS |
| Actual Buddy workflow wiring | `test_actual_buddy_workflow_passes_github_ref_as_selected_ref_without_ownership_gate` reuses the established workflow helpers and asserts exact `${GITHUB_REF}` input/output wiring with no hard-coded branch, CODEOWNERS, API, curl, or wget gate. | PASS |

### Exact commands and results

| Command | Result |
|---|---|
| `mise exec python@3.13 -- python -c 'import pathlib, sys; [compile(pathlib.Path(path).read_bytes(), path, "exec") for path in sys.argv[1:]]' src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py` | Exit 0; both files compiled in memory. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py` | Exit 0; **19 passed**. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py` | Exit 1; **69 passed, 5 failed** in 31.43s. All five failures are exact `fileCount` assertions: actual 125, expected 126. The delegated tester independently reproduced **69 passed, 5 failed**. |
| `uv run ruff check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py` | Exit 0; all checks passed. |
| `uv run ruff format --check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py` | Initially identified both files; `uv run ruff format` formatted only the two owned files. Final rerun is recorded after this append. |
| `git --no-pager diff --check` | Exit 0 before this append; final rerun is recorded after this append. |

### Real-HK blocker and mandatory reviews

A read-only binary partition of the real HK plan identified the sole omitted
surface as `eng/scripts/workflow_delivery_v3_future.py`: every other current,
required-absent, and synthetic surface is included. The actual `hk.pkl`
`workflow_delivery_v3_files` list names only the two current
`workflow_delivery_v3_*.py` helpers rather than the required future direct
script family. The tests retain the exact 126 count and were not weakened to
125 or replaced with a test-local matcher.

`test-gap-analysis` and `assertion-quality` were invoked. Their requested
`test-analysis-extensions` dependency is unavailable, so the Python/pytest
review was completed inline:

- dropping actual-rule ordering, accepting co-owners, removing a synthetic
  layout, filtering a history kind, counting both rename names, or replacing
  the real helper/HK plan changes a concrete assertion;
- tests combine exact/deep equality, collection closure, negative ownership
  and network assertions, state/side-effect observations, and real command
  plan counts;
- no added test is assertion-free, trivial-only, self-referential, or
  tautological.

No commit was created.

Final post-append gates: Ruff check passed; Ruff format check reported both
owned test files already formatted; `git diff --check` passed; the unintended
`mise.lock` command side effect was removed exactly, leaving no diff for that
file.
<!-- END APPEND: workflow-delivery-v3-commit9-tp-fix-results-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-tp-final-green-2026-08-14 -->
## Commit 9 independently adjudicated TP final-green correction

The earlier expected-red narrative was historical. The parent CODEOWNERS
change is present and preserved, and the final focused suite is green.

The first TP implementation run also overclaimed the real HK plan by inventing
`eng/scripts/workflow_delivery_v3_future.py`. That path is not an approved
synthetic requirement and is not registered by the actual
`workflow_delivery_v3_files` list. The final tests keep the required synthetic
future descriptors, future workflow, and both action layouts, while checking
the two actual direct-script paths against parsed CODEOWNERS and through the
real HK plan. No matcher was weakened or replaced.

### Exact changed files

- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py`
- `src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
- Append-only commit-9 addenda in `.testagent/research.md`,
  `.testagent/plan.md`, and `.testagent/status.md`

The pre-existing parent `.github/CODEOWNERS` change was not edited.

### Requirement evidence

| Requirement | Evidence |
|---|---|
| Actual CODEOWNERS is the only ownership oracle; exact final owner is required | `test_actual_codeowners_final_owner_is_exact_for_every_current_and_future_v3_surface` and both `test_later_*_override_fails_exact_final_match` tests |
| Future descriptors, future workflow, both approved action layouts, and direct scripts | `SYNTHETIC_FUTURE_SURFACES`; `/eng/scripts/**` cases in `test_removing_each_actual_governing_rule_exposes_its_exact_surface` use the actual consumer-policy and HK helpers |
| Removing or overriding relevant broad actual rules fails | `test_removing_each_actual_governing_rule_exposes_its_exact_surface`, `test_later_replacement_owner_override_fails_exact_final_match`, and `test_later_hcoona_coowner_override_fails_exact_final_match` |
| Actual HK plan covers shared current/synthetic surfaces for add, modify, delete, rename-in, and rename-out | `test_real_v3_control_pytest_selects_every_codeowners_surface_for_history_kind` and `test_real_v3_control_pytest_selects_governed_side_of_batched_rename` |
| Operational HK/helper paths remain safe and the matcher remains real | `_execution_copies`, `_restore_execution_copies`, `_helper_changed_paths`, and `_helper_step_plan` |
| Public branch/tag normalization preserves canonical intent without CODEOWNERS or network | `test_public_cli_normalizes_arbitrary_buddy_branch_and_tag_without_codeowners_gate` |
| Actual Buddy workflow passes `GITHUB_REF` to `--selected-ref` without an ownership gate | `test_actual_buddy_workflow_passes_github_ref_as_selected_ref_without_ownership_gate` |

### Final commands and results

- `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
  — **74 passed in 38.64s**.
- `uv run ruff check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
  — **All checks passed**.
- `uv run ruff format --check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
  — **2 files already formatted**.
- `git --no-pager diff --check` — **passed**.

`test-gap-analysis` and `assertion-quality` were invoked after the final test
set. Their optional shared extension skill was unavailable, so the final
Python review was completed inline. Concrete owner tuples, exact inventories,
real HK plan counts, canonical Intent fields, workflow command text, and
negative ownership/network assertions kill the relevant plausible mutations.
No added test is assertion-free, trivial-only, tautological, or
self-referential.
<!-- END APPEND: workflow-delivery-v3-commit9-tp-final-green-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-tp-final-strengthening-2026-08-14 -->
## Commit 9 TP final override strengthening

Current status remains green. The final ownership mutation matrix now applies
both later replacement-owner and later co-owner overrides to every approved
synthetic descriptor/workflow/action surface and both actual direct-script
paths, rather than one arbitrary governed leaf. This directly proves that the
relevant actual broad-rule results cannot be overridden while preserving the
exact final-owner requirement.

Exact changed files remain the two owned pytest files plus append-only commit-9
addenda in `.testagent/research.md`, `.testagent/plan.md`, and
`.testagent/status.md`. The parent `.github/CODEOWNERS` change remains
preserved and unedited.

Final validation after strengthening:

- focused pytest: **90 passed in 48.67s**;
- Ruff check: **all checks passed**;
- Ruff format check: **2 files already formatted**;
- `git diff --check`: **passed**.
<!-- END APPEND: workflow-delivery-v3-commit9-tp-final-strengthening-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-followup-adjudication-2026-08-14 -->
## Commit 9 follow-up adjudication

Four follow-up findings were independently adjudicated. Two were true
positives and are fixed:

- The real-HK add matrix now starts from an empty Git baseline, materializes
  the executable HK configuration/helper/support at the tested head, and
  asserts exact `A` name-status evidence for every governed surface. Modify and
  delete cases likewise assert exact `M` and `D` status inventories.
- The actual Buddy caller contract now rejects job- or step-level
  `github.ref` conditions in addition to pinning `${GITHUB_REF}` transport
  through the public normalization command.

Two findings were false positives and did not expand the boundary:

- A hypothetical future direct script is not a direct invocation until it is
  wired into the workflow/HK inventory; commit 9 continues to govern the two
  actual directly invoked v3 scripts without inventing runtime scope.
- Subprocess transport blocking is not part of CODEOWNERS/runtime separation;
  the public normalization path is already exercised offline and its exact
  call boundary contains no CODEOWNERS input.
<!-- END APPEND: workflow-delivery-v3-commit9-followup-adjudication-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-parser-adjudication-2026-08-14 -->
## Commit 9 parser-fidelity adjudication

A final semantic review produced two candidates. Independent adjudication
confirmed one true positive: GitHub-compatible inline CODEOWNERS comments must
not become owners in the test oracle. `_parse_rules` now strips the inline
comment before tokenization, and
`test_codeowners_parser_ignores_github_inline_comments` pins the exact owner
tuple.

The suggested scan for every possible downstream selected-ref shell expression
was adjudicated false positive. The contract already exercises the public CLI,
pins actual `${GITHUB_REF}` transport, and rejects ref-based job/step
conditions; speculative command-text heuristics would not be a reliable
CODEOWNERS boundary.
<!-- END APPEND: workflow-delivery-v3-commit9-parser-adjudication-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-final-closure-2026-08-14 -->
# Workflow Delivery v3 Commit 9 Final Closure

Status: **complete**. Live activation remains disabled.

Commit 9 adds exact final-match ownership and scenario contracts for all
approved v3 governance surfaces. Four independent GPT-5.6 Sol review angles,
independent TP/FP adjudication, remediation, and repeated clean follow-up
reviews are complete.

## Final validation

| Command | Result |
|---|---|
| Focused CODEOWNERS and real-HK contracts | `91 passed` |
| Full v3 pytest | `2294 passed` |
| Managed HK `v3-control-pytest` | `2294 passed` |
| Root pytest | `4329 passed` |
| Ruff check and format check | Passed; 2 files already formatted |
| `git diff --check` | Passed |

## Requirement evidence

| Requirement | Evidence |
|---|---|
| Exact final ownership of all current and approved future surfaces | `test_actual_codeowners_final_owner_is_exact_for_every_current_and_future_v3_surface` |
| GitHub-compatible ordered rules and inline comments | `test_codeowners_parser_ignores_github_inline_comments` and both later-override matrices |
| Missing broad or exact coverage fails | `test_removing_each_actual_governing_rule_exposes_its_exact_surface` |
| Newly added descriptor, workflow, and action layouts remain covered | `SYNTHETIC_FUTURE_SURFACES` in the actual-rule positive and mutation tests |
| Real HK selects every governed surface for Git history changes | `test_real_v3_control_pytest_selects_every_codeowners_surface_for_history_kind` and `test_real_v3_control_pytest_selects_governed_side_of_batched_rename` |
| Arbitrary branch/tag Buddy execution remains independent of CODEOWNERS | `test_public_cli_normalizes_arbitrary_buddy_branch_and_tag_without_codeowners_gate` and `test_actual_buddy_workflow_passes_github_ref_as_selected_ref_without_ownership_gate` |

<!-- END APPEND: workflow-delivery-v3-commit9-final-closure-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-iterative-rpi-status-2026-08-14 -->
# Workflow Delivery v3 Commit 10 Iterative Test Status

## Research and plan

| Phase | Status |
|---|---|
| Bounded research and explicit R1-R9 checklist | Complete |
| Requirement-mapped sequential plan | Complete |
| Phase 1 workflow/inventory contracts | Implemented; expected-red validation complete |
| Phase 2 Governance contracts | Pending |
| Phase 3 Adapter probes | Pending |
| Phase 4 inspection tool | Pending |
| Coverage iteration and quality gates | Pending |

## Phase 1 validation

Command:

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py`

Result: **1 passed, 11 failed**. The passing test proves the actual
CODEOWNERS final-match and HK inventory already cover both commit-10 paths. Ten
failures are the single expected missing temporary acceptance-workflow
production gap reported independently by each scenario. The remaining failure
is the expected missing actual protected disabled attestation. Collection and
the established commit-9 ownership oracle are healthy; no assertion was
weakened and no production file was changed.
<!-- END APPEND: workflow-delivery-v3-commit10-iterative-rpi-status-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-phase2-status-2026-08-14 -->
## Phase 2 Governance validation

Added:

- `tests/governance/test_commit10_attestation.py`
- `tests/governance/test_commit10_acceptance_evidence.py`

Command:

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py`

Result: **38 failed** with clean collection. Three failures report only the
missing actual protected attestation. The other 35 scenario cases report only
the missing
`three_workflow_delivery_v3.records.governance` commit-10 production surface.
The failures cover positive canonical admission as well as reviewer,
recovery-coordinate, schema-closure, purpose/lineage, dependency/probe,
classification, and forbidden-Release mutations, so the red result is not a
test-harness or assertion defect.

| Phase | Status |
|---|---|
| Phase 2 protected attestation contracts | Implemented; expected red |
| Phase 2 Governance Evidence contracts | Implemented; expected red |
| Phase 3 Adapter probes | Next |
<!-- END APPEND: workflow-delivery-v3-commit10-phase2-status-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-phase3-status-2026-08-14 -->
## Phase 3 Adapter validation

Added
`tests/adapters/test_commit10_acceptance_probes.py`.

Command:

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`

Result: **18 failed** with clean collection. Every case reports the same
expected missing
`run_fixed_coordinate_acceptance_probe` production API before any fake
transport or runner call. The parameterized red surface independently covers
coordinate/tag rejection, seven forbidden mutation modes, absent/create/
readback, exact no-mutation, identical and differing races, lost response, and
offline bounded injection. No real network or registry mutation occurred.

| Phase | Status |
|---|---|
| Phase 3 fixed-coordinate probe contracts | Implemented; expected red |
| Phase 4 inspection tool | Next |
<!-- END APPEND: workflow-delivery-v3-commit10-phase3-status-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-phase4-status-2026-08-14 -->
## Phase 4 inspection validation

Added
`tests/governance/test_commit10_inspection.py`.

Command:

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py`

Result: **7 failed** with clean collection. Six cases report the expected
missing optional Governance inspection module; the CLI exposure case reports
the expected absent `governance` command group (`SystemExit(2)` rather than the
required help exit `0`). Present, removed, timeout, transport, malformed,
diagnostic-only, Capability-negative, and live-enable-negative assertions are
all retained.

| Phase | Status |
|---|---|
| Phase 4 optional read-only inspection contracts | Implemented; expected red |
| Initial RPI implementation pass | Complete |
| Static/test quality and coverage iteration | Next |
<!-- END APPEND: workflow-delivery-v3-commit10-phase4-status-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-final-validation-2026-08-14 -->
# Workflow Delivery v3 Commit 10 Final Test-Generation Validation

Status: **test generation complete; expected commit-10 production contracts are
red**.

## Final generated surface

| Metric | Value |
|---|---:|
| New pytest files | 5 |
| Test functions | 39 |
| Collected parameterized cases | 78 |
| Generated cases passing | 1 |
| Expected production-contract gaps | 77 |
| Assertion-free tests | 0 |

The sole green generated case proves current CODEOWNERS final-match and HK
inventory coverage. The expected-red groups are:

- 10 missing temporary-workflow scenarios plus 1 missing actual-attestation
  normal-flow scenario;
- 3 actual protected-attestation scenarios;
- 37 absent Governance Acceptance Evidence record scenarios;
- 19 absent fixed-coordinate Adapter probe scenarios; and
- 7 absent optional Governance inspection/CLI scenarios.

## Build and test commands

| Command | Result |
|---|---|
| Narrow generated pytest | `1 passed, 77 failed`; every failure is one of the bounded commit-10 production gaps above |
| Generated-test collection | `78 tests collected` |
| Full v3 pytest | `2161 passed, 211 failed` in 363.54s |
| Full root pytest | `4196 passed, 211 failed` in 502.22s |
| `uv build --package three-workflow-delivery-v3` | Passed; sdist and wheel built |
| `pnpm build` | Passed across the pnpm workspace |
| `dotnet build dirs.proj --no-incremental` | Passed; 0 warnings, 0 errors |
| `pnpm test` | `332 passed, 1 failed`; unrelated hexo validator environment mismatch: expected pnpm 11.19.0, received 11.17.0 |
| `dotnet test dirs.proj --no-restore` | Exit 1: traversal reported no test projects |
| Ruff check | Passed |
| Ruff format check | Passed; 5 files already formatted |
| Focused Pyrefly | Passed; 0 errors |
| `git diff --check` | Passed |

The 134 non-commit-10 failures in both Python full scopes are caused by
`pnpm build` stamping tracked smoke-package manifest versions before pytest:
132 consumer-policy cases, one CI scenario, and one Node Adapter source-version
case. The successful build changed:

- `src/public/lib/hcoona-release-smoke-npm/package.json` from
  `0.0.0-placeholder` to `1.0.0-beta.269.gadf3fdf`; and
- `src/public/lib/hcoona-release-smoke-npm-dual/package.json` from
  `0.0.0-placeholder` to `1.0.0-beta.265.gadf3fdf`.

No cleanup, restore, reset, reconstruction, or manual production edit was
performed because the task explicitly forbids those operations. These two
validation side effects are therefore reported separately from the generated
test/state outputs.

## Mandatory quality gates

`test-gap-analysis` and `assertion-quality` were invoked. Their shared optional
`test-analysis-extensions` dependency was unavailable. Direct Python/pytest
review found six first-iteration weaknesses and fixed all six:

1. protected-ref comparison and every dispatch input/constant comparison;
2. one immutable upload in review, every probe, and terminal capture;
3. no mandatory reviewer-inspection invocation in capture;
4. noncanonical and duplicate-key Evidence bytes;
5. rejection of an unreviewed but syntactically valid acceptance tag; and
6. concrete secondary observables for transport, runner, and source-read calls.

Post-fix metrics: 188 concrete assertions, 10 exception expectations, average
5.08 per test function, 18 explicit-negative functions, 9 collaborator/
side-effect functions, 4 deep-document functions, and no trivial-only or
self-referential tests. Ruff, formatting, Pyrefly, narrow pytest, and collection
were rerun after the fixes.

## Requirement coverage

| Requirement | Exact generated evidence |
|---|---|
| 1. Later protected target finalization, zero sentinel, no probe | `test_commit10_acceptance_target_is_zero_sentinel_pending_protected_finalization`; `test_zero_sentinel_validation_blocks_review_and_every_probe` |
| 2. Exact disabled protected attestation and pre-Attempt block | `test_actual_protected_attestation_is_canonical_disabled_and_exactly_bound`; `test_actual_attestation_accepts_only_hcoona_admin_and_exact_access`; `test_disabled_attestation_decision_cannot_cross_the_pre_attempt_gate` |
| 3. Truthful reviewer omission and optional recovery | `test_terminal_evidence_declares_reviewer_unavailable_and_recovery_coordinates`; `test_complete_evidence_accepts_unavailable_reviewer_with_all_recovery_coordinates`; `test_missing_reviewer_alone_does_not_downgrade_complete_evidence`; `test_github_actor_cannot_substitute_for_environment_reviewer`; `test_reviewer_inspection_cli_is_optional_on_demand` |
| 4. Exact temporary acceptance workflow | `test_acceptance_dispatch_inputs_and_constants_are_exact`; `test_acceptance_dag_environment_and_concurrency_are_exact`; `test_acceptance_permissions_keep_package_write_only_in_probe_jobs`; `test_acceptance_action_pins_and_evidence_retention_are_exact` |
| 5. Independent attempt guards and terminal fan-in | `test_each_validation_review_and_probe_job_independently_rejects_reruns`; `test_each_probe_has_the_exact_first_attempt_job_guard`; `test_terminal_capture_has_exact_always_guard_and_every_dependency` |
| 6. Closed Governance Acceptance Evidence | `test_acceptance_evidence_schema_is_closed_at_every_level`; `test_acceptance_evidence_rejects_noncanonical_or_duplicate_json`; `test_acceptance_evidence_requires_exact_purpose_and_no_release_lineage`; `test_acceptance_evidence_retains_every_dependency_result_and_probe_fact`; `test_mutation_classification_is_closed_and_consistent`; `test_acceptance_evidence_rejects_every_release_lineage_field` |
| 7. Fixed-coordinate offline probe scenarios | `test_acceptance_probe_requires_the_fixed_coordinate_and_explicit_tag`; `test_acceptance_probe_rejects_latest_and_every_forbidden_mutation_mode`; `test_absent_create_readback_records_exact_complete_facts`; `test_exact_preexisting_state_never_invokes_the_mutation_runner`; `test_identical_conflict_race_is_exact_without_blind_repair`; `test_differing_conflict_race_is_conflicting_without_overwrite`; `test_lost_response_is_unknown_and_requires_reconciliation`; `test_probe_transport_and_runner_are_bounded_injected_and_offline` |
| 8. Read-only inspection outcomes and authority negatives | `test_reviewer_inspection_present_is_read_only_and_scoped`; `test_reviewer_inspection_removed_is_not_universal_negative_proof`; `test_reviewer_inspection_errors_are_unknown_and_human_required`; `test_reviewer_inspection_cannot_grant_capability_or_enable_live`; `test_reviewer_inspection_cli_is_optional_on_demand` |
| 9. CODEOWNERS/HK and disabled normal Buddy | `test_commit10_surfaces_have_exact_codeowners_and_hk_inventory`; `test_normal_buddy_remains_disabled_before_attempt_without_legacy_route` |

## Final file boundary

Intended generated/state files:

- `.testagent/research.md` (append-only)
- `.testagent/plan.md` (append-only)
- `.testagent/status.md` (append-only)
- `tests/contracts/test_commit10_acceptance_workflow.py`
- `tests/governance/test_commit10_attestation.py`
- `tests/governance/test_commit10_acceptance_evidence.py`
- `tests/governance/test_commit10_inspection.py`
- `tests/adapters/test_commit10_acceptance_probes.py`

The two tracked package-manifest build side effects listed above are the only
additional changed paths. No production/workflow/documentation implementation
was authored.
<!-- END APPEND: workflow-delivery-v3-commit10-final-validation-2026-08-14 -->

# Workflow Delivery v3 Commit 10 Phase 5 Final Tail Closure

Timestamp: 2026-08-15T01:18:54Z.

Status: **SUCCESS**. This tail record supersedes earlier expected-red notes and
is intentionally appended after every pre-existing status entry.

## Exact Phase 5 changed files

- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- `docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md`
- `docs/wiki/analyses/workflow-delivery/v3/hcoona-release-smoke-npm-lld.md`
- `docs/wiki/log.md`
- `.testagent/status.md` (append-only records)

The first full-package run exposed the closed adapter export expectation as the
only regression (`ValidatedAcceptanceRequestProof` was intentionally public but
absent from the expected export set). The test contract was updated without
deleting any test; its focused rerun and the full package then passed.

## Fixture provenance and local-only proof

- Disposable fixture:
  `src/public/lib/three-workflow-delivery-v3/tests/fixtures/acceptance/npm-publish-request/`.
- Captured toolchain/argv: Node `v24.14.0`, npm `11.9.0`;
  `npm publish <package> --tag wdv3-acceptance-1 --registry
  <loopback-registry> --ignore-scripts`.
- Fixture SHA-256:
  `capture.json`
  `d9a1c15370c950b63baf18dec3a6190d6b76b646fe5ff3da51176df117825e0f`;
  `package.json`
  `1729de7c1dd97b07c9819a733063e2a5bbb93526f8fafed7edcf65530ae5bd17`;
  `README.md`
  `7eebfef1441e4125667c599f6aceb4bfab52925963dbc9bf5ba082e92dfc49fd`;
  `dist/acceptance-witness.json`
  `bb4fcbdd195050a2061de9d252160b8e8c054014f3f8520e55bf7ab5136bcdca`;
  `dist/index.js`
  `6be199c72a12dc6348bc2f4b9596f99364456bd367f614dc521c96d63b1951c1`.
- The credential/external-registry scan returned 0 matches. Tests use the
  loopback capture server and monkeypatched upstream only. No external HTTP,
  remote Environment configuration, workflow dispatch, package publication,
  activation, git mutation, or test deletion occurred.

## Commands and exact counts

- Four-file collection command from the plan:
  **537 collected in 0.54s**, delta **+47** from 490.
- Four-file scoped test command from the plan:
  **537 passed, 0 failed, 0 skipped** in 15.67s initially and 15.28s after the
  integration fix.
- Full-package collection:
  `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package
  three-workflow-delivery-v3 pytest -p no:cacheprovider --collect-only -q
  src/public/lib/three-workflow-delivery-v3/tests`:
  **2822 collected in 0.91s**, delta **+47** from 2775.
- Full-package test:
  `python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13
  --package three-workflow-delivery-v3 pytest -q
  src/public/lib/three-workflow-delivery-v3/tests`:
  first run **1 failed, 2821 passed in 374.43s**; final run
  **2822 passed, 0 failed, 0 skipped in 374.24s**.
- Focused integration rerun:
  `... pytest -q
  src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_adapter_public_api_exports_closed_types_and_functions`:
  **1 passed in 0.27s**.
- Ruff check/format over all 17 changed Python files:
  **all checks passed; 17 files formatted**. Focused post-fix Ruff:
  **passed; 1 file formatted**.
- Scoped docs:
  Prettier **passed for 3 files**; markdownlint **0 issues**.
- `uv build --package three-workflow-delivery-v3`:
  **sdist and wheel built successfully**.
- Fixture `sha256sum` plus bounded credential/external-registry `rg` scan:
  **5 hashes above; 0 forbidden matches**.
- `git diff --check`: **passed with no output**.
  `git diff --name-only`/`git status --short` were inspection-only.

## Requirement evidence

- Real npm request/metadata/reproducibility/no credentials:
  `test_acceptance_capture_uses_real_npm_publish_request`,
  `test_acceptance_capture_records_nonsecret_toolchain_metadata`,
  `test_acceptance_request_fixture_is_reproducible`,
  `test_acceptance_request_fixture_contains_no_credentials`.
- Strict validation/token replacement/redaction:
  `test_proxy_validates_captured_couchdb_publish_request`,
  `test_proxy_replaces_dummy_authorization_only_for_mocked_upstream`,
  `test_proxy_proof_redacts_incoming_and_upstream_tokens`.
- Validated immutable proof/substitution closure:
  `test_validated_request_proof_binds_raw_request_and_tarball_digests`,
  `test_validated_request_proof_binds_upstream_response_identity`,
  `test_acceptance_probe_rejects_request_proof_substitutions`,
  `test_acceptance_probe_uses_validated_proof_not_synthetic_body`.
- Shared deadline:
  `test_acceptance_operation_uses_one_monotonic_deadline`,
  `test_acceptance_deadline_budget_decreases_across_all_boundaries`,
  `test_acceptance_deadline_is_not_reset_by_proxy_or_observation`,
  `test_acceptance_cleanup_uses_only_remaining_deadline_budget`.
- Fail-closed runner facts:
  `test_acceptance_runner_proof_fact_matrix`,
  `test_missing_or_partial_runner_facts_never_default_to_mutation_started`,
  `test_only_fully_validated_runner_proof_can_form_complete_evidence`.
- Non-zero complete evidence and retained incomplete semantics:
  `test_complete_acceptance_evidence_rejects_zero_target_sha`,
  `test_complete_acceptance_evidence_rejects_zero_workflow_sha`,
  `test_terminal_complete_evidence_never_emits_zero_sha`, plus the passing
  incomplete-sentinel regressions.

## Blockers

- Implementation blockers: **none**.
- As explicitly required by the caller, this phase did **not** invoke or claim
  `test-gap-analysis` or `assertion-quality`; both remain parent-owned follow-up
  gates.
- Remote protected Environment/reviewer setup, dispatch, publication, and
  activation remain out of scope and unperformed.

<!-- END APPEND: workflow-delivery-v3-commit10-phase5-final-tail-closure-2026-08-15T011854Z -->

# Workflow Delivery v3 Commit 10 Phase 5 Integrated Acceptance Closure

Timestamp: 2026-08-15T01:18:54Z.

Status: **SUCCESS — four-file acceptance integration and full-package
validation pass locally; no external operation was performed**.

## Exact Phase 5 changed files

- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
  - Updated the existing closed adapter export expectation to include the
    intentional `ValidatedAcceptanceRequestProof` public export after the first
    full-package run exposed the integration regression.
- `docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md`
- `docs/wiki/analyses/workflow-delivery/v3/hcoona-release-smoke-npm-lld.md`
- `docs/wiki/log.md`
- `.testagent/status.md` (this append-only closure only)

No Phase 5 change was required in the four scoped acceptance files or
production implementation. All earlier workspace changes and tests were
preserved; no file was deleted, restored, reset, staged, committed, or cleaned.

## Validated acceptance implementation and fixture

- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/__init__.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/governance.py`
- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`
- `src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py`
- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py`
- `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
- `src/public/lib/three-workflow-delivery-v3/tests/fixtures/acceptance/npm-publish-request/capture.json`
- `src/public/lib/three-workflow-delivery-v3/tests/fixtures/acceptance/npm-publish-request/package/package.json`
- `src/public/lib/three-workflow-delivery-v3/tests/fixtures/acceptance/npm-publish-request/package/README.md`
- `src/public/lib/three-workflow-delivery-v3/tests/fixtures/acceptance/npm-publish-request/package/dist/acceptance-witness.json`
- `src/public/lib/three-workflow-delivery-v3/tests/fixtures/acceptance/npm-publish-request/package/dist/index.js`

## Node/npm fixture provenance and local-only evidence

- The fixture records Node `v24.14.0`, npm `11.9.0`, and argv
  `npm publish <package> --tag wdv3-acceptance-1 --registry
  <loopback-registry> --ignore-scripts`.
- It came from the disposable acceptance package, not the tracked smoke
  package, and records `PUT /@hcoona%2fhcoona-release-smoke-npm`, content
  length `2235`, and normalized request-body SHA-256
  `a68061edbd52ceb4b3e9cf54c220b757e61c5b8d0f6df478b6ee347bffb91e45`.
- Exact fixture file SHA-256 values:
  - `capture.json`: `d9a1c15370c950b63baf18dec3a6190d6b76b646fe5ff3da51176df117825e0f`
  - `package.json`: `1729de7c1dd97b07c9819a733063e2a5bbb93526f8fafed7edcf65530ae5bd17`
  - `README.md`: `7eebfef1441e4125667c599f6aceb4bfab52925963dbc9bf5ba082e92dfc49fd`
  - `acceptance-witness.json`: `bb4fcbdd195050a2061de9d252160b8e8c054014f3f8520e55bf7ab5136bcdca`
  - `index.js`: `6be199c72a12dc6348bc2f4b9596f99364456bd367f614dc521c96d63b1951c1`
- The fixture credential/external-registry scan for
  `npm.pkg.github.com`, `registry.npmjs.org`, authorization/bearer text, and
  the dummy/upstream test token literals returned **0 matches**.
- `test_acceptance_capture_uses_real_npm_publish_request`,
  `test_acceptance_request_fixture_is_reproducible`,
  `test_acceptance_request_fixture_contains_no_credentials`, and the proxy
  tests bind only loopback or monkeypatched upstream seams. No remote
  Environment configuration, workflow dispatch, external HTTP, package
  publication, or package mutation command was run.

## Exact commands and results

| Command | Result |
|---|---|
| `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | Passed: `537 tests collected in 0.54s`; authoritative scoped delta `+47` from 490. |
| Same four-file command without `--collect-only` | Passed initially: `537 passed in 15.67s`; passed after the integration fix: `537 passed in 15.28s`; 0 failed, 0 skipped. |
| `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider --collect-only -q src/public/lib/three-workflow-delivery-v3/tests` | Passed: `2822 tests collected in 0.91s`; authoritative full-package delta `+47` from 2775. |
| `python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests` | First integration run: `1 failed, 2821 passed in 374.43s`; exact failure was the closed adapter `__all__` expectation missing `ValidatedAcceptanceRequestProof`. Final run after the test-contract fix: `2822 passed in 374.24s`; 0 failed, 0 skipped. |
| `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_adapter_public_api_exports_closed_types_and_functions` | Passed: `1 passed in 0.27s`. |
| `uv run --python 3.13 ruff check --no-cache <all 17 changed Python paths>` | Passed: `All checks passed!`. |
| `uv run --python 3.13 ruff format --check <all 17 changed Python paths>` | Passed: `17 files already formatted`. |
| `uv run --python 3.13 ruff check --no-cache src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py && uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py` | Passed after integration fix: all checks passed; 1 file already formatted. |
| `pnpm exec prettier --check docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md docs/wiki/analyses/workflow-delivery/v3/hcoona-release-smoke-npm-lld.md docs/wiki/log.md` | Passed: all matched files use Prettier style. |
| `pnpm exec markdownlint-cli2 docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md docs/wiki/analyses/workflow-delivery/v3/hcoona-release-smoke-npm-lld.md docs/wiki/log.md` | Passed: 0 issues in 0 files. |
| `uv build --package three-workflow-delivery-v3` | Passed: sdist and wheel built. |
| `sha256sum <five fixture files>` plus the bounded `rg` credential/external-registry scan | Passed: hashes above; 0 forbidden matches. |
| `git diff --check` | Passed with no output. |
| `git diff --name-only` | Inspection only; existing workspace changes remained present. |

## Requirement mapping

| Requirement | Concrete evidence |
|---|---|
| Real active npm request and reproducible non-secret fixture | `test_acceptance_capture_uses_real_npm_publish_request`; `test_acceptance_capture_records_nonsecret_toolchain_metadata`; `test_acceptance_request_fixture_is_reproducible`; `test_acceptance_request_fixture_contains_no_credentials` |
| Strict request validation, exact token replacement, and redaction | `test_proxy_validates_captured_couchdb_publish_request`; `test_proxy_replaces_dummy_authorization_only_for_mocked_upstream`; `test_proxy_proof_redacts_incoming_and_upstream_tokens` |
| Immutable validated request/upstream proof and fail-closed substitutions | `test_validated_request_proof_binds_raw_request_and_tarball_digests`; `test_validated_request_proof_binds_upstream_response_identity`; `test_acceptance_probe_rejects_request_proof_substitutions`; `test_acceptance_probe_uses_validated_proof_not_synthetic_body` |
| One shared monotonic deadline | `test_acceptance_operation_uses_one_monotonic_deadline`; `test_acceptance_deadline_budget_decreases_across_all_boundaries`; `test_acceptance_deadline_is_not_reset_by_proxy_or_observation`; `test_acceptance_cleanup_uses_only_remaining_deadline_budget` |
| Missing/partial/contradictory runner facts remain incomplete | `test_acceptance_runner_proof_fact_matrix`; `test_missing_or_partial_runner_facts_never_default_to_mutation_started`; `test_only_fully_validated_runner_proof_can_form_complete_evidence` |
| Complete evidence independently rejects zero SHAs | `test_complete_acceptance_evidence_rejects_zero_target_sha`; `test_complete_acceptance_evidence_rejects_zero_workflow_sha`; `test_terminal_complete_evidence_never_emits_zero_sha`; incomplete sentinel tests remain green |
| Integrated four-file and full-package closure | 537/537 scoped and 2822/2822 full-package passing commands above |
| Necessary docs and append-only record | Three scoped docs above and this newest `.testagent/status.md` append |

## Blockers and deferred parent gates

- Implementation/validation blockers: **none**.
- Per the Phase 5 caller instruction, this phase did **not** invoke or claim
  `test-gap-analysis` or `assertion-quality`; those mandatory parent-owned gates
  remain explicitly deferred to the parent.
- No external acceptance execution evidence is claimed. Protected Environment
  setup, reviewer configuration, dispatch, publication, and activation remain
  out of scope.

<!-- END APPEND: workflow-delivery-v3-commit10-phase5-integrated-acceptance-closure-2026-08-15T011854Z -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-fixes-green-2026-08-14 -->

# Workflow Delivery v3 Commit 10 Remaining-Fix Validation

Status: **green in the current working tree**. The earlier commit-10
expected-red entries are superseded by this result.

- The protected acceptance Environment and reviewer configuration remain
  **pending Environment setup**. No live acceptance dispatch or package
  mutation was performed.
- Strict mutation qualification, proof binding, race overlap, startedness,
  shared deadlines, terminal evidence consistency, hostile-input capture, and
  hidden artifact upload handling are implemented.
- The scoped inventory currently contains **481 tests**, not the earlier 476
  count, because five additional reviewer/deadline cases are present in the
  current tree.

| Command | Exact result |
|---|---|
| Five commit-10 scoped files | **481 passed** |
| Full Workflow Delivery v3 pytest | **2775 passed** |
| Targeted CLI / GitHub Packages Adapter / public API pytest | **359 passed** |
| Targeted Ruff check | Passed |
| Targeted Ruff format | Passed |
| `uv run --python 3.13 pyrefly check` | **0 errors** |
| Acceptance workflow `actionlint` | Passed |
| Targeted unstaged HK `actionlint` + `v3-control-pytest` | Passed; **2775 passed** in the managed pytest gate |
| `git diff --check` | Passed |

<!-- END APPEND: workflow-delivery-v3-commit10-fixes-green-2026-08-14 -->

# Workflow Delivery v3 Commit 10 Red-Regression Repair

Timestamp: 2026-08-14.

Status: **green locally; remote Environment/dispatch remains pending and was
not configured or run**.

## Repair summary

- Lost-response npm publication now uses a loopback registry proxy with an
  in-memory Bearer token, bounded exact method/path/JSON request validation,
  TLS upstream forwarding, bounded responses, and proof only for 2xx or 409
  GitHub Packages responses.
- The runner returns the distinct `lost-response-processed` proof-bearing
  outcome. Generic runtime failures remain unknown even when readback is exact.
- `subprocess.TimeoutExpired` is translated to builtin `TimeoutError`, and the
  probe records `action-executed=true`, `mutation-started=true`, unknown.
- Acceptance REST observation requires exact owner login `hcoona`, exact
  repository `hcoona/three`, uses the confirmed user package endpoint, and
  returns unknown after bounded pagination exhaustion.
- Acceptance workflow checkouts use `${{ github.token }}` with
  `persist-credentials: false`; pinned `setup-uv` explicitly receives an empty
  `github-token`.
- Governance probe fact results are derived from admitted scenario
  classifications with unknown precedence. Every successful fact independently
  requires complete scenarios plus record/artifact identifiers and digests.
- Reviewer inspection skips rejected/non-approved matching reviews and
  continues to later approvals while retaining nested pagination.
- `.testagent/plan.md` and `.testagent/research.md` were restored from `HEAD`
  and the existing commit-10 notes appended without deleting historical lines.

## Validation

| Command | Result |
|---|---|
| Commit-10 acceptance/inspection regression set | `401 passed in 2.45s` |
| Targeted CLI/adapter/public API set | `240 passed in 8.90s` |
| Full Workflow Delivery v3 pytest | `2700 passed in 398.56s` |
| Targeted Ruff check | `All checks passed!` |
| Targeted Ruff format check | `9 files already formatted` |
| `uv run --python 3.13 pyrefly check` | `0 errors` (76 suppressed, 158 warnings not shown) |
| `mise exec -- actionlint .github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml` | Passed |
| `git diff --check` | Passed |

## Append-only proof

| File | Diff versus `HEAD` |
|---|---|
| `.testagent/plan.md` | `40 insertions, 0 deletions` |
| `.testagent/research.md` | `51 insertions, 0 deletions` |

The working tree remains intentionally uncommitted.

<!-- END APPEND: workflow-delivery-v3-commit10-red-regression-repair-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-adjudicated-tp-fixes-2026-08-14 -->
# Workflow Delivery v3 Commit 10 Adjudicated TP Fix Status

Timestamp: 2026-08-14.

Status: **COMPLETE — this section supersedes the stale commit-10 expected-red
entries above. All independently adjudicated true positives are fixed and the
current focused/regression validation is green.**

## Implemented corrections

- The five-job workflow now executes the exact fixed five-scenario inventory.
  The second package-write job runs `exact`, `identical-race`,
  `differing-race`, and `lost-response` at reviewed internal `.1` through `.4`
  coordinates.
- Real bounded CLI orchestration starts same-byte and different-byte competing
  publishers and deliberately discards the lost-response result only after the
  mutation process starts. Injected deterministic seams cover every path.
- `absent-create-readback` completes only after absent observation, executed
  create, and exact readback. A preexisting exact `.1` coordinate is incomplete
  and requires a new reviewed fixed coordinate.
- Readback downloads and hashes exact tarball bytes and strictly validates
  actual version/tag, package owner/name, exact repository metadata, and the
  embedded acceptance witness purpose/target.
- Canonical suite records bind exact scenario inventories, record digests,
  immutable artifact IDs/digests, and pre/action/response/post facts.
  Governance Acceptance Evidence requires the exact five-scenario set and
  rejects missing bindings for `complete`.
- Terminal evidence capture checks out `github.workflow_sha` before any `uv`
  command and still runs on first-attempt failed/skipped/cancelled dependency
  paths.
- No `inputs.*` expression appears inside a `run` script. Inputs cross the
  expression boundary only through step environments and are quoted in shell;
  terminal JSON is formed from environment values in Python. Negative static
  tests reject direct interpolation.
- Mutation classification is aggregated once with
  `unknown > incomplete > complete`; all dependency/probe mixed permutations
  are covered.
- The acceptance token is removed from CLI argv, read from the dedicated
  environment variable, popped immediately, omitted from allowlisted
  subprocess environments, and supplied only through the mode-0600 npm config
  for publish/view and an in-memory Authorization header for tarball GET.
  Local `npm pack` receives no auth config.
- Reviewer recovery now resolves the workflow run `node_id` by REST GET and
  paginates query-only GraphQL
  `WorkflowRun.deploymentReviews`, parsing real connection/edge/pageInfo,
  user/state/databaseId/environment shapes and matching the exact Environment.
- Governance-only scope remains intact: no Release Attempt/Receipt lineage,
  live activation, real network/mutation execution, legacy retirement, or
  commit-11+ work was added.

## Independent review closure

The final independent adversarial review reported two medium findings:
non-.1 suite records used the base coordinate, and tarball GET did not consume
the retained in-memory token. Both are fixed. Governance admission now enforces
each scenario's exact coordinate/tag, and the authenticated GET wrapper replaces
the retained redaction placeholder with `Bearer <dedicated token>` only at the
transport boundary.

## Current validation

| Command scope | Result |
|---|---|
| Focused commit-10 tests | `68 passed` |
| Commit-9 CODEOWNERS/HK, Buddy workflow, and eligibility regressions | `240 passed` |
| GitHub Packages Adapter and CLI regressions | `81 passed` |
| Reviewer-finding focused rerun | `46 passed` |
| Ruff check | Passed |
| Ruff format check | Passed; 8 files already formatted |
| Pyrefly over v3 source and changed commit-10 tests | Passed; `0 errors` |
| actionlint over the acceptance workflow | Passed |
| `git diff --check` | Passed |

No network probe, workflow dispatch, package mutation, commit, push, live
activation, legacy retirement, or commit-11+ operation was performed.
<!-- END APPEND: workflow-delivery-v3-commit10-adjudicated-tp-fixes-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-implementation-status-2026-08-14 -->
# Workflow Delivery v3 Commit 10 Implementation Status

Implementation completed in the working tree without committing or pushing.
The checked-in acceptance target remains the 40-zero protected-finalization
sentinel, so review and mutation are unreachable. Normal live remains blocked
by the canonical `live_enabled: false` attestation. Legacy Buddy entry files
remain present and no real probes were executed.

Validation results are appended after the final implementation verification.
<!-- END APPEND: workflow-delivery-v3-commit10-implementation-status-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-implementation-validation-2026-08-14 -->
## Commit 10 Implementation Validation

| Command scope | Result |
|---|---|
| Five generated commit-10 files | `352 passed` |
| Commit-9 CODEOWNERS, Buddy workflows, eligibility, HK trigger tests | `295 passed` |
| Existing CLI and GitHub Packages Adapter regressions | `81 passed` |
| Ruff check and format-check over changed Python scope | Passed |
| Pyrefly over the v3 package | Passed: `0 errors` |
| actionlint over acceptance caller and normal Buddy caller/callee | Passed |
| Governance CLI help surfaces | Passed |
| `git diff --check` | Passed |

No network probe, workflow dispatch, package mutation, commit, push, live
activation, legacy retirement, or commit-11 work was performed.
<!-- END APPEND: workflow-delivery-v3-commit10-implementation-validation-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-review-closure-2026-08-14 -->
## Commit 10 Independent Review Closure

An independent adversarial review found four actionable gaps, all fixed:

1. terminal capture now normalizes missing skipped-probe outputs and admits an
   incomplete record with a null unavailable review artifact rather than
   producing malformed JSON;
2. real npm SRI `sha512-<base64>` integrity is converted to canonical
   `sha512:<hex>`;
3. the exact follow-up probe observes the tag created by the first probe; and
4. Governance Evidence admission now enforces the fixed repository, workflow
   path/ref, package coordinate, Environment, producer, first attempt, and
   recovery correlations.

The 352 commit-10 tests, 295 ownership/Buddy/eligibility/HK tests, 81
CLI/Adapter tests, Ruff, format, Pyrefly, actionlint, and diff check all passed
again after these fixes.
<!-- END APPEND: workflow-delivery-v3-commit10-review-closure-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-step8-final-gate-coverage-iteration-2026-08-14T180554Z -->
# Workflow Delivery v3 Commit 10 Step 8 Final Coverage/Test-Quality Iteration

Timestamp: 2026-08-14T18:05:54Z.

Status: **final generated-test quality iteration complete; expected red
production-contract gaps remain**.

## Exact scoped edits

- `tests/contracts/test_commit10_acceptance_workflow.py`
  - Added exact trigger closure: the acceptance workflow trigger map must be
    exactly `{"workflow_dispatch"}`.
  - Replaced token-presence validation checks with fail-closed static guard
    assertions for target SHA, protected `refs/heads/main`, every fixed
    dispatch input constant, first `run_attempt`, and `exit 1` failure action.
  - Strengthened terminal capture to require exact dependency-result emission
    and non-success dependency-to-`incomplete` classification logic, while
    retaining the `unknown` classification path.
- `tests/governance/test_commit10_acceptance_evidence.py`
  - Added parameterized missing-required-field cases for top-level and nested
    Evidence fields, including workflow SHA, target SHA, package coordinate,
    dependency job/result, probe response/content digests, run attempt, and
    mutation classification.
  - Added terminal fact matrices that derive exact mutation classification from
    dependency/probe result cross-products.
  - Added inconsistency cases for incomplete-with-all-success facts,
    complete-with-failed/cancelled/skipped dependency, unknown-vs-incomplete
    precedence, and unknown-without-unknown-fact combinations.
- `tests/governance/test_commit10_inspection.py`
  - Strengthened the read-only `gh api --method GET` assertion so the API query
    itself is scoped by every recovery coordinate: repository, workflow run,
    environment, deployment, job, and artifact.
- `tests/adapters/test_commit10_acceptance_probes.py`
  - Bound every mutation command assertion to `npm publish <exact tarball>`
    with the tarball path present exactly once.
  - Added a tarball SHA-512 mismatch negative test that requires rejection
    before observation or mutation.
- `tests/governance/test_commit10_attestation.py`
  - Reviewed in scope; no Step 8 edit required.

## Commands and results

| Command | Result |
|---|---|
| `uv run --python 3.13 ruff format src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | `2 files reformatted, 3 files left unchanged` |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `All checks passed!` |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `5 files already formatted` |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `127 tests collected in 0.18s` |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Expected red: `126 failed, 1 passed in 0.62s`. Failures remain bounded to absent commit-10 production contracts: missing acceptance workflow, missing protected disabled attestation, missing Governance Acceptance Evidence module, missing optional Governance inspection module/CLI, and missing fixed-coordinate acceptance probe API. |

## Remaining gaps

No additional generated-test assertion-quality gaps are known after this
iteration. The focused suite remains intentionally red until commit-10
production contracts are implemented.
<!-- END APPEND: workflow-delivery-v3-commit10-step8-final-gate-coverage-iteration-2026-08-14T180554Z -->


<!-- BEGIN APPEND: workflow-delivery-v3-commit10-final-gate-weakness-closure-2026-08-14T182456Z -->
# Workflow Delivery v3 Commit 10 Final-Gate Weakness Closure

Timestamp: 2026-08-14T18:24:56Z.

Status: **scoped generated-test weakness closure complete; focused suite remains expected-red on absent commit-10 production/workflow surfaces**.

## Exact scoped edits

- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py`
  - Reworked the shared shell-guard helper to identify the exact `if` condition and inspect only its then arm before any `else`/`elif`/`fi` branch marker.
  - `_assert_single_fail_closed_guard` now requires the mismatch condition then arm to contain `exit 1`, rejecting inverted `else`-failure branches.
  - `_assert_dependency_result_emitted_and_classified` now requires each `needs.<dependency>.result != success` condition then arm to assign `incomplete`, rejecting inverted complete/incomplete branches.
- `src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py`
  - Added `PROBES` and indexed terminal-fact mutation helpers so classification derivation/rejection is parameterized across every dependency result and both probe facts instead of the final entries only.
  - Expanded missing-required-field coverage to whole required containers/lists: `workflow`, `reviewer`, `recovery`, `dependency-results`, and `probe-facts`.
  - Added empty/short/extra array rejection for `dependency-results` and `probe-facts`.
  - Expanded required-field deletion cases to every dependency entry (`job`, `result`) and every probe entry (`probe`, `result`, `response-identity-digest`, `content-sha512`, `diagnostics`).
- `src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py`
  - Replaced substring recovery-coordinate checks with exact read-only `gh api --method GET` endpoint parsing for `repos/{repository}/actions/runs/{workflow-run-id}/pending_deployments`.
  - Required typed `gh api` fields for `environment`, `deployment`, `job`, and numeric `artifact_id`, with no extra/missing fields.

## Commands and results

| Command | Result |
|---|---|
| `uv run --python 3.13 ruff format src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py` | Passed: `3 files left unchanged` after final edits. |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py` | Passed: `All checks passed!` |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py` | Passed: `3 files already formatted` |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py` | Passed: `283 tests collected in 0.19s` |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py` | Expected red: `282 failed, 1 passed in 0.73s`; representative failures remain bounded to missing `.github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml`, missing `three_workflow_delivery_v3.records.governance`, and missing `three_workflow_delivery_v3.governance.inspection` / CLI production surfaces. |

## Changed/strengthened test names

- `test_zero_sentinel_validation_blocks_review_and_every_probe`
- `test_acceptance_dispatch_inputs_and_constants_are_exact`
- `test_each_validation_review_and_probe_job_independently_rejects_reruns`
- `test_terminal_capture_has_exact_always_guard_and_every_dependency`
- `test_acceptance_evidence_rejects_missing_required_fields`
- `test_acceptance_evidence_rejects_empty_or_wrong_length_fact_arrays`
- `test_mutation_classification_is_closed_and_consistent`
- `test_terminal_fact_matrix_derives_exact_mutation_classification`
- `test_mutation_classification_rejects_inconsistent_or_open_values`
- `test_mutation_classification_rejects_dependency_probe_cross_products`
- `test_reviewer_inspection_present_is_read_only_and_scoped`
- `test_reviewer_inspection_removed_is_not_universal_negative_proof`
- `test_reviewer_inspection_errors_are_unknown_and_human_required`
- `test_reviewer_inspection_cannot_grant_capability_or_enable_live`

<!-- END APPEND: workflow-delivery-v3-commit10-final-gate-weakness-closure-2026-08-14T182456Z -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-final-exhaustive-test-only-pass-2026-08-14T183019Z -->
# Workflow Delivery v3 Commit 10 Final Exhaustive Test-Only Pass

Timestamp: 2026-08-14T18:30:19Z.

Status: **scoped generated-test strengthening complete; accurate expected-red
behavior preserved**.

## Exact scoped edits

- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py`
  - Strengthened `test_terminal_capture_has_exact_always_guard_and_every_dependency`
    so terminal capture must emit `"probe-facts"` for both probe jobs.
  - Added exact static wiring checks for each probe fact field:
    `result`, `response-identity-digest`, `content-sha512`, and
    `diagnostics` must use `needs.<probe>.outputs.<field>`.
  - Added probe-result classification checks requiring `incomplete` and
    `unknown` outputs to assign the matching top-level mutation
    classification.
- `src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py`
  - Added `test_mutation_classification_rejects_open_value_with_all_success_facts`
    for isolated `mutation-classification: unsupported` rejection with all
    dependency/probe facts otherwise successful.
  - Added `test_acceptance_evidence_rejects_mutated_dependency_job_inventory`
    across every dependency job for unexpected same-length identifiers and
    duplicate same-cardinality inventory.
  - Added `test_acceptance_evidence_rejects_mutated_probe_fact_inventory`
    across every probe identifier for unexpected same-length identifiers and
    duplicate same-cardinality inventory.
  - Added `test_acceptance_evidence_rejects_reordered_fact_inventory` for
    dependency and probe arrays with exact same members but wrong order.
- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`
  - Strengthened full `to_document()` equality for
    `test_absent_create_readback_records_exact_complete_facts`,
    `test_exact_preexisting_state_never_invokes_the_mutation_runner`,
    `test_identical_conflict_race_is_exact_without_blind_repair`,
    `test_differing_conflict_race_is_conflicting_without_overwrite`,
    `test_lost_response_is_unknown_and_requires_reconciliation`, and
    `test_probe_transport_and_runner_are_bounded_injected_and_offline`.
  - The exact payload assertions now pin schema, scenario, fixed
    coordinate/tag, pre/post state, result, mutation classification, response
    identity digest, content digest, and diagnostics for every terminal probe
    scenario, including the alternate explicit tag.
  - Added exact transport-call assertions for the race/lost-response paths so
    skipped or extra observations are visible.

## Commands and results

| Command | Result |
|---|---|
| `uv run --python 3.13 ruff format src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Final rerun passed: `3 files left unchanged`. |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `All checks passed!`. |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `3 files already formatted`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `321 tests collected in 0.18s`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Expected red: `320 failed, 1 passed in 0.95s`. Failures remain bounded to missing commit-10 production/workflow surfaces: absent acceptance workflow, absent protected disabled attestation, missing Governance Acceptance Evidence module, missing optional inspection module/CLI, and missing fixed-coordinate acceptance probe API. |

## Final pseudo-mutation review

- Hardcoded or dropped terminal probe outputs are killed by exact
  `needs.<probe>.outputs.result`, `response-identity-digest`,
  `content-sha512`, and `diagnostics` assertions for both probe jobs.
- Count-only fact inventory validators are killed by every dependency/probe
  unexpected same-length identifier case, every duplicate same-cardinality
  case, and reordered same-member arrays.
- Open classification enums are killed independently by the all-success
  `unsupported` case, separate from dependency/probe consistency failures.
- Partial Adapter probe records are killed by full document equality across
  absent-create, exact preexisting, identical race, differing race, lost
  response, and alternate explicit tag.
- No broad validation was run.

<!-- END APPEND: workflow-delivery-v3-commit10-final-exhaustive-test-only-pass-2026-08-14T183019Z -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-final-two-gap-closure-2026-08-14T184512Z -->
# Workflow Delivery v3 Commit 10 Final Two-Gap Closure

Timestamp: 2026-08-14T18:45:12Z.

Status: **scoped test-only strengthening complete; accurate expected-red
behavior preserved**.

## Exact scoped edits

- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py`
  - Strengthened `test_zero_sentinel_validation_blocks_review_and_every_probe`
    to require fail-closed validation for both zero-sentinel target sources
    before the review/probe dependency-chain assertions:
    pinned `WDV3_ACCEPTANCE_TARGET_SHA` / `env.WDV3_ACCEPTANCE_TARGET_SHA`
    and dispatch `inputs.target_sha`.
  - Added `_assert_single_fail_closed_zero_sentinel_guard`, requiring an
    explicit equality comparison to the 40-zero sentinel and `exit 1` in the
    matched then arm.
  - Added if/elif-aware terminal classification arm parsing and
    `_assert_probe_unknown_precedes_non_success_classification` so each probe's
    `unknown` classification must precede the broader `result != success`
    incomplete classification as an adjacent same-chain `elif`, preventing a
    later independent incomplete guard from overwriting unknown.
  - Added
    `test_terminal_probe_classification_static_assertions_reject_unknown_overwrite`
    to prove the static assertions reject the two-independent-if mutation where
    `unknown` is assigned and then overwritten by a later non-success
    incomplete guard.

## Commands and results

| Command | Result |
|---|---|
| `uv run --python 3.13 ruff format src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py` | Passed: `1 file left unchanged`. |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py` | Passed: `All checks passed!`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py::test_terminal_probe_classification_static_assertions_reject_unknown_overwrite` | Passed: `1 passed in 0.09s`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `322 tests collected in 0.16s`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Expected red: `320 failed, 2 passed in 0.99s`. Failures remain bounded to missing commit-10 production/workflow surfaces, starting with absent `.github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml`; no skips were introduced. |

<!-- END APPEND: workflow-delivery-v3-commit10-final-two-gap-closure-2026-08-14T184512Z -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-terminal-schema-closure-2026-08-14T185432Z -->
# Workflow Delivery v3 Commit 10 Terminal Classification and Schema Closure

Timestamp: 2026-08-14T18:54:32Z.

Status: **scoped test-only strengthening complete; accurate expected-red
behavior preserved; no skips introduced**.

## Exact scoped edits

- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py`
  - Strengthened terminal probe classification static assertions to reject a
    later `mutation_classification=` assignment after the correct
    unknown/non-success same-chain `if`/`elif` classification when it remains
    in that probe's classification region and could overwrite `unknown`.
  - Added a negative static fixture:
    `test_terminal_probe_classification_static_assertions_reject_later_unknown_overwrite`.
  - Added false-positive guards:
    `test_terminal_probe_classification_static_assertions_allow_prior_unrelated_assignments`
    and
    `test_terminal_probe_classification_static_assertions_allow_later_other_probe_assignments`.
- `src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py`
  - Parameterized closed-schema unexpected-key injection through
    `CLOSED_SCHEMA_EXTRA_PATHS`: top-level, `workflow`, `reviewer`, `recovery`,
    every `dependency-results` entry (`0` through `3`), and both
    `probe-facts` entries (`0` and `1`).

## Commands and exact results

| Command | Result |
|---|---|
| `uv run --python 3.13 ruff format src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py` | Passed: `2 files left unchanged`. |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py` | Passed: `All checks passed!`. |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py` | Passed: `2 files already formatted`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py` | Passed: `329 tests collected in 0.21s`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py` | Expected red: `324 failed, 5 passed in 1.01s`. Failures remain bounded to missing commit-10 production/workflow surfaces, including absent fixed-coordinate acceptance probe API, absent acceptance workflow, missing Governance Acceptance Evidence module, missing protected attestation, and missing optional reviewer inspection module/CLI. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py::test_terminal_probe_classification_static_assertions_reject_unknown_overwrite src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py::test_terminal_probe_classification_static_assertions_reject_later_unknown_overwrite src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py::test_terminal_probe_classification_static_assertions_allow_prior_unrelated_assignments src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py::test_terminal_probe_classification_static_assertions_allow_later_other_probe_assignments src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py::test_acceptance_evidence_schema_is_closed_at_every_level` | Expected red: `10 failed, 4 passed in 0.14s`. The four terminal static assertion tests passed. The ten closed-schema parameter cases failed because `three_workflow_delivery_v3.records.governance` is still intentionally missing. |

## Exact changed test outcomes

| Test | Outcome |
|---|---|
| `test_terminal_probe_classification_static_assertions_reject_unknown_overwrite` | Passed in changed-nodeid run. |
| `test_terminal_probe_classification_static_assertions_reject_later_unknown_overwrite` | Passed in changed-nodeid run. |
| `test_terminal_probe_classification_static_assertions_allow_prior_unrelated_assignments` | Passed in changed-nodeid run. |
| `test_terminal_probe_classification_static_assertions_allow_later_other_probe_assignments` | Passed in changed-nodeid run. |
| `test_acceptance_evidence_schema_is_closed_at_every_level[top-level]` | Expected red: missing `three_workflow_delivery_v3.records.governance`. |
| `test_acceptance_evidence_schema_is_closed_at_every_level[workflow]` | Expected red: missing `three_workflow_delivery_v3.records.governance`. |
| `test_acceptance_evidence_schema_is_closed_at_every_level[reviewer]` | Expected red: missing `three_workflow_delivery_v3.records.governance`. |
| `test_acceptance_evidence_schema_is_closed_at_every_level[recovery]` | Expected red: missing `three_workflow_delivery_v3.records.governance`. |
| `test_acceptance_evidence_schema_is_closed_at_every_level[dependency-results-0]` | Expected red: missing `three_workflow_delivery_v3.records.governance`. |
| `test_acceptance_evidence_schema_is_closed_at_every_level[dependency-results-1]` | Expected red: missing `three_workflow_delivery_v3.records.governance`. |
| `test_acceptance_evidence_schema_is_closed_at_every_level[dependency-results-2]` | Expected red: missing `three_workflow_delivery_v3.records.governance`. |
| `test_acceptance_evidence_schema_is_closed_at_every_level[dependency-results-3]` | Expected red: missing `three_workflow_delivery_v3.records.governance`. |
| `test_acceptance_evidence_schema_is_closed_at_every_level[probe-facts-0]` | Expected red: missing `three_workflow_delivery_v3.records.governance`. |
| `test_acceptance_evidence_schema_is_closed_at_every_level[probe-facts-1]` | Expected red: missing `three_workflow_delivery_v3.records.governance`. |

## Quality review

`test-gap-analysis` and `assertion-quality` were invoked. Their
`test-analysis-extensions` dependency was unavailable in this workspace, so the
Python/pytest review was completed inline.

- Pseudo-mutation review: the later-overwrite mutation is killed by
  `test_terminal_probe_classification_static_assertions_reject_later_unknown_overwrite`;
  the independent-if overwrite mutation remains killed by
  `test_terminal_probe_classification_static_assertions_reject_unknown_overwrite`;
  unrelated prior and other-probe classification assignments are covered by
  explicit positive fixtures; dropping any dependency/probe path from
  `CLOSED_SCHEMA_EXTRA_PATHS` removes a parameterized closed-schema case.
- Assertion-quality review: no assertion-free or skip-based tests were added.
  The terminal static tests use meaningful exception and helper assertions, and
  the closed-schema parameterization preserves the `pytest.raises` closed-schema
  assertion for every injected path.

<!-- END APPEND: workflow-delivery-v3-commit10-terminal-schema-closure-2026-08-14T185432Z -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-final-review-defect-closure-2026-08-14T190925Z -->
# Workflow Delivery v3 Commit 10 Final Review Defect Closure

Timestamp: 2026-08-14T19:09:25Z.

Status: **scoped test-only final review closure complete; accurate expected-red
behavior preserved; no skips introduced**.

## Exact scoped edits

- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py`
  - Replaced fail-closed shell checks with exact `if`-condition matching for
    the expected mismatch/equality semantics and a standalone executable
    `exit 1` command in that exact then arm.
  - Added passing and negative static regressions for fixed input constants,
    zero-sentinel input/env checks, protected main ref, first run attempt,
    echoed/commented/string `exit 1`, and neutralized/wrapped guard
    conditions.
  - Replaced mutation-classification overwrite scanning with executable shell
    assignment detection covering plain, `export`, quoted `export`,
    `declare`, `local`, and `readonly` assignment forms while ignoring comments
    and `echo`/`printf` text.
  - Removed the prior generic substring/action helper path and required
    executable `mutation_classification` assignments for adjacent dependency
    and probe classification checks.

No production, workflow, documentation, manifest, package, `.testagent/plan.md`,
or `.testagent/research.md` files were edited for this pass.

## Commands and exact results

| Command | Result |
|---|---|
| `uv run --python 3.13 ruff format src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py` | Passed: `1 file left unchanged`. |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `All checks passed!`. |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `5 files already formatted`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py -k 'static_assertions or fail_closed_static'` | Passed: `11 passed, 12 deselected in 0.10s`. |
| `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `336 tests collected in 0.17s`. |
| `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Expected red: `324 failed, 12 passed in 1.04s`. Failures remain bounded to missing commit-10 production/workflow surfaces, starting with absent `.github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml` and continuing through missing protected attestation, Governance Acceptance Evidence, reviewer inspection, and fixed-coordinate acceptance probe implementation surfaces. |

## Exact changed static evidence

| Requirement | Evidence |
|---|---|
| Standalone executable fail-closed `exit 1` in the exact then arm | `test_fail_closed_static_assertions_accept_required_exact_guard_conditions`; `test_fail_closed_static_assertions_reject_non_executable_exit_text` |
| Exact guard condition semantics for sentinel env/input, main ref, fixed inputs, and run attempt | `test_fail_closed_static_assertions_accept_required_exact_guard_conditions`; `test_fail_closed_static_assertions_reject_neutralized_or_wrapped_conditions` |
| Later overwrite detection covers export/declare/local/readonly/quoted assignment forms | `test_terminal_probe_classification_static_assertions_reject_later_assignment_forms` |
| Later overwrite detection avoids comments/echo false positives | `test_terminal_probe_classification_static_assertions_ignore_echoed_and_commented_later_assignment_text` |
| Adjacent shell classification helpers reject echo-only actions and neutralized conditions | `test_terminal_probe_classification_static_assertions_reject_echoed_assignment_action`; `test_terminal_probe_classification_static_assertions_reject_neutralized_probe_condition` |

<!-- END APPEND: workflow-delivery-v3-commit10-final-review-defect-closure-2026-08-14T190925Z -->

# Workflow Delivery v3 Commit 10 R8 Reviewer Inspection Contract Closure

Timestamp: 2026-08-14T19:15:25Z.

Status: **scoped test-only R8 reviewer inspection contract closure complete;
accurate expected-red behavior preserved; no skips introduced**.

## Exact scoped edits

- `src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py`
  - Replaced the diagnostic-output forbidden-key shortlist with exact closed
    allowed key sets for present, removed, and error/unknown reviewer
    inspection outcomes.
  - Required every outcome to carry only the diagnostic authority contract:
    `authority`, `scope`, and `recovery`, with exact expected values.
  - Pinned outcome-specific reviewer/diagnostic fields, including
    `deployment-review-id is None` for removed and error/unknown outcomes.
  - Added a negative helper contract test proving representative
    evidence/capability/live/mutation/authority-like/universal-negative extras
    are rejected as unrecognized keys, not merely screened by name.

No production, workflow, documentation, manifest, package,
`.testagent/plan.md`, or `.testagent/research.md` files were edited for this
pass.

## Commands and exact results

| Command | Result |
|---|---|
| `uv run --python 3.13 ruff format src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py` | Passed: `1 file left unchanged`. |
| `uv run --python 3.13 ruff check --no-cache src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `All checks passed!`. |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `5 files already formatted`. |
| `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `345 tests collected in 0.18s`. |
| `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Expected red: `324 failed, 21 passed in 1.09s`. Failures remain bounded to missing commit-10 production/workflow surfaces, including the absent acceptance workflow, absent protected attestation, missing Governance Acceptance Evidence module, missing optional reviewer inspection module/CLI, and missing fixed-coordinate acceptance probe API. |

## Requirement evidence

| Requirement | Evidence |
|---|---|
| Present outcome is a closed diagnostic-only reviewer contract | `test_reviewer_inspection_present_is_read_only_and_scoped` |
| Removed outcome is closed, human-required, and not universal-negative proof | `test_reviewer_inspection_removed_is_not_universal_negative_proof` |
| Error/unknown outcomes are closed, human-required diagnostics | `test_reviewer_inspection_errors_are_unknown_and_human_required` |
| Unrecognized extras cannot slip through closed contracts | `test_reviewer_inspection_contract_rejects_every_unrecognized_extra_key` |
| Capability/live behavior remains absent and unpinned to live execution | `test_reviewer_inspection_cannot_grant_capability_or_enable_live` |

<!-- END APPEND: workflow-delivery-v3-commit10-r8-reviewer-inspection-contract-closure-2026-08-14T191525Z -->

# Workflow Delivery v3 Commit 10 Acceptance Test Gap Closure

Timestamp: 2026-08-14T19:31:05Z.

Status: **scoped generated-test gap closure complete; accurate expected-red
behavior preserved; no skips introduced**.

## Exact scoped edits

- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py`
  - Added static render/parse helpers that substitute synthetic workflow,
    dependency, and probe outputs into the actual terminal capture command,
    parse the emitted JSON/YAML Governance Acceptance Evidence payload, and
    pass it through `admit_governance_acceptance_evidence`.
  - Added full closed-contract assertions for schema/purpose, workflow
    repository/path/ref/SHA, target SHA, package coordinate, confirmation
    digest, environment, reviewer `login: null` with
    `source: unavailable-in-job-context`, full recovery coordinates, every
    dependency result, both probe facts, mutation classification, producer,
    run/attempt, `release-lineage: none`, and absence of Release record fields.
  - Strengthened reviewer capture checks to require an explicit null reviewer
    login and to reject any `github.actor` expression, not only loose text.
- `src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py`
  - Added a negative governance case proving a non-null reviewer login is
    rejected when `source` is `unavailable-in-job-context`.
- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`
  - Added fixed-coordinate negatives for accepted version with wrong npm scope
    and accepted version with wrong package name.
  - Added wrong-tag preexisting, readback, and conflict-race observations where
    bytes/digest match but the observed tag differs. These assert
    conflict/reconciliation/unknown results, never exact/complete, and verify
    no repair/mutation path beyond the single allowed create attempt where that
    scenario may have started.

No production, workflow, documentation, manifest, package,
`.testagent/plan.md`, or `.testagent/research.md` files were edited for this
pass.

## Commands and exact results

| Command | Result |
|---|---|
| `uv run --python 3.13 ruff format src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `1 file reformatted, 2 files left unchanged`. |
| `uv run --python 3.13 ruff check --no-cache src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `All checks passed!`. |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `5 files already formatted`. |
| `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Passed: `352 tests collected in 0.28s`. |
| `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_attestation.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_inspection.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | Expected red: `331 failed, 21 passed in 1.37s`. Failures remain bounded to missing commit-10 production/workflow surfaces, including the absent acceptance workflow, absent protected attestation, missing Governance Acceptance Evidence module, missing optional reviewer inspection module/CLI, and missing fixed-coordinate acceptance probe API. |

## Self-review for vacuous/token-only assertions

- Governance workflow binding now requires one parseable emitted Evidence
  payload and admits it through the closed production contract with synthetic
  outputs; the assertions compare exact nested fields and reject extra Release
  lineage fields rather than checking token presence only.
- Reviewer assertions require explicit `login: null` and absence of the
  `github.actor` expression.
- Coordinate negatives keep the accepted version constant while mutating only
  the scope or package name, proving exact coordinate validation.
- Wrong-tag probe tests assert exact result documents, classifications,
  diagnostics, digest/bytes equality, transport call coordinates, and
  runner-call counts so they cannot pass via substring-only checks or hidden
  repair/mutation behavior.

## Requirement evidence

| Requirement | Evidence |
|---|---|
| Terminal capture binds actual emitted payload to the closed Governance Acceptance Evidence contract | `test_terminal_capture_payload_admits_full_closed_governance_evidence_contract` |
| Non-null reviewer with unavailable source rejects; workflow capture emits null reviewer and never `github.actor` | `test_unavailable_reviewer_source_requires_null_reviewer_login`; `test_terminal_evidence_declares_reviewer_unavailable_and_recovery_coordinates` |
| Probe coordinate rejects accepted version with wrong scope/name | `test_acceptance_probe_requires_the_fixed_coordinate_and_explicit_tag[wrong-scope]`; `test_acceptance_probe_requires_the_fixed_coordinate_and_explicit_tag[wrong-package-name]` |
| Wrong-tag observations never classify exact/complete and require conflict/reconciliation/unknown without repair | `test_wrong_tag_preexisting_state_requires_reconciliation_without_mutation`; `test_wrong_tag_readback_requires_reconciliation_without_repair`; `test_wrong_tag_identical_conflict_race_requires_unknown_reconciliation` |

<!-- END APPEND: workflow-delivery-v3-commit10-acceptance-test-gap-closure-2026-08-14T193105Z -->

# Workflow Delivery v3 Commit 10 Final Validation and Quality Signoff

Timestamp: 2026-08-14.

Status: **iterative test generation complete; generated suite intentionally red
against missing commit-10 production contracts**.

## Final test-quality gate

- `test-gap-analysis` and `assertion-quality` were invoked after the final test
  edits. The language-extension helper was unavailable, so the review applied
  pytest conventions directly.
- The conclusive independent pseudo-mutation/assertion-depth review returned
  `RAW_FINDINGS: none`.
- No assertion-free, trivial-only, tautological, skipped, or adjacent-feature
  substitute tests were reported.
- Prompt-scenario coverage was rechecked against all nine requirements after
  the final coverage iteration.

## Final full-workspace build

| Command | Result |
|---|---|
| `uv build --package three-workflow-delivery-v3` | Passed; sdist and wheel built. |
| `pnpm build` | Passed; all 8 selected workspace projects built, warnings only. |
| `dotnet build dirs.proj --no-incremental` | Passed; 0 warnings, 0 errors. |

## Final full-workspace tests

| Command | Result |
|---|---|
| `uv run --python 3.13 pytest -q` | Expected red: `4216 passed, 465 failed` in 503.71s. The generated commit-10 scope contributed `21 passed, 331 failed`; all 331 failures are the intended missing-contract gaps. The other 134 failures are existing consumer/version expectations affected by workspace package stamping. |
| `pnpm test` | `349/350` executed tests passed; one environment/toolchain failure because the workspace requires pnpm `11.19.0` while the runner has `11.17.0`. |
| `dotnet test dirs.proj --no-restore` | No tests executed: `No test projects were found.` |

## Workspace side-effect disclosure

The required `pnpm build` stamped these tracked package versions:

- `src/public/lib/hcoona-release-smoke-npm/package.json`
- `src/public/lib/hcoona-release-smoke-npm-dual/package.json`

They were not manually edited, restored, or reverted because the task forbids
overwriting/restoring tracked files. No production, workflow, or documentation
source was intentionally changed by the test implementation.

<!-- END APPEND: workflow-delivery-v3-commit10-final-validation-2026-08-14 -->

# Workflow Delivery v3 Commit 10 Phase 5 Newest Closure

Timestamp: 2026-08-15T01:18:54Z. Status: **SUCCESS**.

- **Exact Phase 5 files changed**:
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`,
  `docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md`,
  `docs/wiki/analyses/workflow-delivery/v3/hcoona-release-smoke-npm-lld.md`,
  `docs/wiki/log.md`, and append-only `.testagent/status.md`.
- **Fixture provenance**:
  disposable loopback-only `tests/fixtures/acceptance/npm-publish-request`
  captured with Node `v24.14.0`, npm `11.9.0`, and
  `npm publish <package> --tag wdv3-acceptance-1 --registry
  <loopback-registry> --ignore-scripts`. SHA-256:
  `capture.json=d9a1c15370c950b63baf18dec3a6190d6b76b646fe5ff3da51176df117825e0f`,
  `package.json=1729de7c1dd97b07c9819a733063e2a5bbb93526f8fafed7edcf65530ae5bd17`,
  `README.md=7eebfef1441e4125667c599f6aceb4bfab52925963dbc9bf5ba082e92dfc49fd`,
  `acceptance-witness.json=bb4fcbdd195050a2061de9d252160b8e8c054014f3f8520e55bf7ab5136bcdca`,
  `index.js=6be199c72a12dc6348bc2f4b9596f99364456bd367f614dc521c96d63b1951c1`.
- **Local-only/no-external evidence**:
  credential/external-registry fixture scan returned 0 matches; all request
  tests use loopback and mocked upstream seams. No external HTTP, remote
  Environment configuration, dispatch, publication, activation, git mutation,
  or test deletion occurred.
- **Commands/counts**:
  four-file collection `537` in `0.54s` and tests `537 passed` in `15.67s`
  then `15.28s` after the integration fix; full collection `2822` in `0.91s`;
  first full run `1 failed, 2821 passed` in `374.43s`; focused closed-export
  regression `1 passed` in `0.27s`; final full run `2822 passed` in `374.24s`.
  Scoped/full collection deltas from 490/2775 are both `+47`. Ruff check and
  format passed all 17 changed Python files; post-fix Ruff passed; Prettier
  passed 3 scoped docs; markdownlint reported 0 issues; `uv build --package
  three-workflow-delivery-v3` built sdist/wheel; fixture hashes/scan passed;
  `git diff --check` passed.
- **Requirement mapping**:
  real request/metadata/reproducibility/no-secret tests; strict proxy,
  token-replacement/redaction tests; validated request/upstream proof
  substitution tests; four shared-deadline tests; runner-fact matrix and
  incomplete-evidence tests; independent zero target/workflow SHA and terminal
  reconstruction tests all pass in the 537-file scope. The three scoped docs
  record the proof boundary, deadline, runner facts, SHA rule, and provenance.
- **Blockers**: none in implementation. Per caller instruction,
  `test-gap-analysis` and `assertion-quality` were not invoked or claimed and
  remain parent-owned follow-up gates. Remote acceptance setup/execution
  remains out of scope.

The complete exact command strings, fixture hashes, and named-test mapping are
also retained in the preceding detailed Phase 5 closure records.

<!-- END APPEND: workflow-delivery-v3-commit10-phase5-newest-closure-2026-08-15T011854Z -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-non-success-proof-gate-2026-08-15 -->
## Commit 10 non-success request-proof gate fix

- Added the parameterized adapter regression
  `test_acceptance_probe_rejects_runner_supplied_non_success_proof` for
  runner-supplied proofs tampered to upstream statuses 409 and 500.
- Exact readback remains observable, but both cases fail closed as `unknown`,
  return `lost-response`, and persist no validated request proof.
- Narrow result: 2 passed. Full adapter file: 152 passed. Harness discovery:
  152 collected, delta +2 from the 150-test baseline.
- Ruff check and format check passed; the affected package built successfully.
  Pseudo-mutation and assertion-quality review found the non-success-status
  guard killed by five concrete assertions per case. No production files were
  changed and no external or git-mutating operation was performed.
<!-- END APPEND: workflow-delivery-v3-commit10-non-success-proof-gate-2026-08-15 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-final-green-2026-08-15 -->
## Commit 10 final green closure

Status: **complete**.

- The mandatory `test-gap-analysis` gate first found one survived mutation:
  adapter admission did not directly pin a runner-supplied proof with a
  non-success upstream status. The new parameterized
  `test_acceptance_probe_rejects_runner_supplied_non_success_proof` covers 409
  and 500; the repeated gate returned no findings.
- The mandatory `assertion-quality` gate returned no findings: the final tests
  contain no assertion-free, trivial-only, tautological, or non-null-only
  cases. The optional `test-analysis-extensions` skill was unavailable, so the
  Python/pytest classification was completed directly.
- Final scoped acceptance validation: **539 passed in 15.59s**.
- Final full package validation: **2824 passed in 405.48s**.
- Final package build produced the sdist and wheel successfully.
- Ruff check passed and all 8 bounded Python files were already formatted.
- No external HTTP call, remote Environment configuration, workflow dispatch,
  package publication, or git mutation was performed.
<!-- END APPEND: workflow-delivery-v3-commit10-final-green-2026-08-15 -->

<!-- BEGIN APPEND: workflow-delivery-v3-runtime-closure-review-fixes-2026-08-15 -->
## Runtime closure high-confidence review fixes

Timestamp: 2026-08-15T01:44:45Z request window. Status: **complete**.

- Fixed validated-request-proof preservation across terminal workflow suite
  reconstruction, canonical suite digest recomputation, and Governance
  Acceptance Evidence admission. The lost-response complete scenario now
  carries a closed-validated proof through canonical records instead of
  silently dropping it.
- Made record-present/artifact-output-missing probe evidence admissible as
  incomplete when the immutable suite record and complete scenarios are still
  available, matching terminal capture downgrade behavior.
- Fixed lost-response runner fact admission so missing, partial, or
  contradictory action facts classify as
  `runner-malformed-before-mutation`/`incomplete` before the ambiguous
  lost-response branch.
- Restricted validated acceptance request proofs and the local npm publish
  proxy to the exact accepted publish contract: upstream HTTP `201 Created`;
  other 2xx statuses no longer produce processed proof.
- Test-first gate: the new targeted regressions failed before implementation
  with **9 failed in 0.82s**.
- Final validation:
  - `uv run --python 3.13 --package three-workflow-delivery-v3 pytest src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
    → **559 passed in 16.09s**.
  - `uv run --python 3.13 --package three-workflow-delivery-v3 ruff format src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/governance.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
    → **4 files reformatted, 3 files left unchanged**.
  - `uv run --python 3.13 --package three-workflow-delivery-v3 ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/governance.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
    → **All checks passed!**.
  - `uv run --python 3.13 --package three-workflow-delivery-v3 ruff format --check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/governance.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
    → **7 files already formatted**.
  - `git --no-pager diff --check` → **passed**.
- No external HTTP, remote environment mutation, workflow dispatch, package
  publication, or git mutation was performed.
<!-- END APPEND: workflow-delivery-v3-runtime-closure-review-fixes-2026-08-15 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-real-runtime-final-2026-08-15 -->
## Commit 10 real-runtime findings final closure

Timestamp: 2026-08-15T02:01:00Z.

Status: **implementation complete; one targeted HK typo gate remains red for
an older append-only status entry**.

- The real loopback capture used Node `v24.14.0` and npm `11.9.0`. npm emitted
  `PUT /@hcoona%2fhcoona-release-smoke-npm` with
  `Content-Type: application/json`, a CouchDB document containing `_id`,
  `name`, `dist-tags`, `versions`, and `_attachments`, and attachment
  `hcoona-release-smoke-npm-0.0.0-wdv3-acceptance.1.tgz`.
- npm required a loopback-host-and-port-scoped dummy bearer token before it
  sent the request. The proxy validates that dummy credential, removes it, and
  injects the real upstream credential only in memory; retained fixtures and
  proof records contain no authorization value.
- The validated attachment contains exactly `package/README.md`,
  `package/dist/acceptance-witness.json`, `package/dist/index.js`, and
  `package/package.json`. Its captured SHA-512 is
  `N6TYjcaSLw27Wax1eRwYwlVEShT33m15cFNBpd+7Jv12e4HaJy9wSQ3+SkrE5rfEFUUuFBWn9wO0Yp8OQn6xiA==`.
- Final focused acceptance/CLI/Adapter/Governance/workflow suite:
  **559 passed in 16.73 seconds**.
- Final full Workflow Delivery v3 suite:
  **2844 passed in 374.92 seconds**; collection also reported exactly 2844.
- Targeted CLI/Adapter public-API selection: **5 passed, 255 deselected**.
- Ruff check and format check passed; Pyrefly passed with **0 errors**;
  direct `actionlint` passed; `git diff --check` passed.
- Targeted unstaged HK ran the changed-file gates. Ruff and actionlint passed,
  but HK stopped on `typos` because the older append-only commit-8 status text
  literally documents the two-character fixture substring that typos flags.
  That historical status entry was not rewritten.
- The protected acceptance Environment, reviewer configuration, final nonzero
  target SHA, live dispatch, and publication remain pending and out of scope.
  No external mutation occurred.

This section supersedes all older expected-red and earlier-count tails.
<!-- END APPEND: workflow-delivery-v3-commit10-real-runtime-final-2026-08-15 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-operability-reproducibility-final-2026-08-15 -->
# Workflow Delivery v3 Commit 10 Operability/Reproducibility Closure

Timestamp: 2026-08-15T03:45:00Z.

Status: **confirmed local implementation and relevant validation complete**.

- The captured npm fixture closure is visible to Git, includes the exact
  pinned Node `v24.14.0`/npm `11.9.0` tarball, and is admitted only through an
  exact digest-bound consumer-policy exception.
- Process cleanup now signals every started contender before bounded reaping.
  Authenticated npm readback uses a separate mode-`0600` temporary config that
  is deleted after use; the loopback proxy config retains only its dummy token.
- Both write probes install and verify Node `24.14.0` and npm `11.9.0`, use
  explicit shared suite deadlines of 120 and 300 seconds, upload records before
  classification, fail unless classification is complete, and prevent the
  second mutation job after first-probe failure. Terminal evidence and upload
  remain unconditional.
- Focused commit-10 suite: **403 passed in 66.60 seconds**.
- Full Workflow Delivery v3 suite through the relevant HK gate:
  **2886 passed in 411.38 seconds**.
- Consumer-policy targeted suite: **341 passed in 43.31 seconds**.
- Ruff check/format, Pyrefly (**0 errors**), actionlint, targeted typos,
  editorconfig, fixture format/reproducibility, consumer policy, and relevant
  targeted HK all pass.
- The older status note claiming the historical two-letter typo was a current
  blocker is historical and superseded. Its original line is restored exactly,
  and `.typos.toml` now excludes only `.testagent/status.md`; current HK typos
  passes with `--force-exclude`.
- The unfiltered unrelated workflow-release acceptance gate still has two
  pre-existing repository failures: mismatched CodeQL action digests and the
  absent `.pre-commit-config.yaml`. The commit-10-relevant HK run excludes only
  that unrelated step and is green.
- The protected acceptance Environment, reviewer configuration, final nonzero
  target SHA, live dispatch, and publication remain pending and out of scope.
  No Environment was configured, no workflow was dispatched, and no external
  mutation occurred.

<!-- END APPEND: workflow-delivery-v3-commit10-operability-reproducibility-final-2026-08-15 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit10-authoritative-final-2026-08-15 -->
# Workflow Delivery v3 Commit 10 Authoritative Final Validation

Timestamp: 2026-08-15T04:40:00Z.

Status: **local implementation, review, and applicable validation complete;
protected finalization remains pending**.

- All five independent final review angles returned `RAW_FINDINGS: none` after
  the mixed first-probe-success/second-probe-failure terminal-evidence path was
  corrected and re-reviewed.
- The five commit-10 scenario-heavy test files pass **578 tests**.
- The Workflow Delivery v3 package and managed `v3-control-pytest` HK gate pass
  **2,888 tests**.
- The root Python suite passes **4,923 tests**.
- PNPM tests and builds pass under the repository-required pnpm `11.19.0`;
  both smoke-package manifests were reset to placeholders afterward.
- `uv build --package three-workflow-delivery-v3` produced the sdist and wheel.
- `dotnet build dirs.proj --no-incremental` passed with **0 warnings and
  0 errors**.
- Ruff check/format, Pyrefly (**0 errors**), actionlint, Pkl evaluation,
  consumer policy, lock checks, and `git diff --check` passed.
- The complete changed-file HK run reached only two pre-existing unrelated
  workflow-release failures: mismatched root/nested CodeQL action digests and
  the absent `.pre-commit-config.yaml`. The commit-10 managed v3 HK gate is
  green.
- The 40-zero target sentinel remains fail-closed. The protected acceptance
  Environment and required reviewer configuration, exact nonzero target SHA,
  workflow dispatch, live package probes, and activation remain pending
  protected work. No remote Environment configuration, workflow dispatch,
  package publication, or external mutation occurred.

This section supersedes all earlier commit-10 expected-red, partial-count, and
intermediate-validation sections.
<!-- END APPEND: workflow-delivery-v3-commit10-authoritative-final-2026-08-15 -->


## 2026-08-15 Commit 11 Calibration Status Addendum

| Phase | Result |
|---|---|
| Contract calibration | Complete |
| Collect-only | Passed; 25 cases collected from the calibrated contract module. |
| Narrow pytest | Expected red: 11 passed, 14 failed. Failures are bounded to retained legacy entry files/routes, exact retired acceptance matrix/gate/test inventories, six active docs with exact legacy entry references lacking retirement context, the caller-completeness helper still requiring `buddy.yml`, bootstrap governance still listing `.github/workflows/buddy.yml`, and actionlint still carrying the deleted Buddy path override. |
| Ruff check | Passed. |
| Ruff format check | Passed; file already formatted. |

### Calibration requirement mapping

| Requirement | Evidence |
|---|---|
| Exact ten Buddy-only v1 tests retired, excluding mixed R41 | `RETIRED_BUDDY_TEST_NAMES`; `test_legacy_v1_buddy_only_and_mixed_test_nodes_are_retired_or_split` |
| Exact retired acceptance rows and live gate | `RETIRED_ACCEPTANCE_ROW_IDS`, `REMOVED_LIVE_GATE_IDS`; `test_buddy_only_acceptance_rows_nodeids_and_live_gates_are_removed` |
| Exact matrix/gate retired or mixed Buddy node IDs | `RETIRED_MATRIX_TEST_NODEIDS`, `RETIRED_GATE_TEST_NODEIDS`; matrix/gate inventory tests |
| Forbid only exact active matrix evidence paths for deleted entries | `FORBIDDEN_ACTIVE_MATRIX_EVIDENCE_PATHS`; active YAML evidence-path traversal |
| Preserve Official/CI named tests and gate node IDs | `REQUIRED_OFFICIAL_TESTS`, `PRESERVED_GATE_TEST_NODEIDS` assertions |
| Caller completeness becomes Official-only | executable temp-workflow run in `test_release_orchestrate_caller_completeness_is_official_only` |
| Bootstrap exact path inventory drops legacy entry paths only | AST assignment parse in `test_bootstrap_governance_exact_paths_drop_only_legacy_buddy_entries` |
| Acceptance gate drops retired/mixed Buddy node IDs while retaining Official/CI | AST assignment parse in `test_acceptance_gate_drops_buddy_nodes_and_retains_official_ci` |
| Actionlint drops deleted Buddy path override while retaining active overrides | YAML parse in `test_actionlint_drops_deleted_buddy_override_and_keeps_active` |
| Active docs only check exact legacy filenames when present | optional exact filename context scan in `test_active_v1_docs_describe_retirement_not_an_active_buddy_route` |

### Validation commands

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py` | Passed; 25 tests collected. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py` | Expected red; 14 failed, 11 passed. |
| `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py` | Passed. |
| `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py` | Passed. |
<!-- BEGIN APPEND: commit11-calibration-mixed-node-correction-2026-08-15 -->
## Commit 11 Calibration Mixed-Node Correction Status

The earlier calibration wording that said to preserve the mixed R41 function
name is superseded. The final contract requires that exact mixed Buddy node to
be retired or split while preserving only the separately enumerated
Official/CI tests and gate node IDs. Validation results follow in the next
append-only closure.
<!-- END APPEND: commit11-calibration-mixed-node-correction-2026-08-15 -->
<!-- BEGIN APPEND: commit11-calibration-final-validation-2026-08-15 -->
## Commit 11 Calibration Final Validation

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py` | Passed; 25 cases collected. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py` | Expected red; 14 failed, 11 passed. Failures remain bounded to the two live legacy entries/routes, exact retired matrix/gate/test inventories including the mixed R41 name, six active documents, Official-only caller completeness, bootstrap inventory, and actionlint override. |
| `uv run --python 3.13 ruff check <commit-11-test>` | Passed. |
| `uv run --python 3.13 ruff format --check <commit-11-test>` | Passed. |
| `git --no-pager diff --check` | Passed. |

### Final pre-completion gate

- `test-gap-analysis` and `assertion-quality` were invoked. Their mandatory
  shared `test-analysis-extensions` dependency was unavailable, so the final
  Python/pytest review was completed inline.
- Pseudo-mutation review found no surviving in-scope mutation: exact set
  member removal/addition, retired-row/live-gate/node-ID retention, legacy
  evidence-path retention, caller dependence on a missing Buddy file,
  bootstrap/actionlint path retention, topology weakening, HK rename-side
  omission, and CODEOWNERS final-owner weakening are each observed by a
  concrete equality, disjointness, execution, parsed-topology, or final-owner
  assertion.
- Assertion-depth review found 40 explicit assertions across 14 test
  functions / 25 collected cases, with no assertion-free function,
  trivial-only assertion, tautology, or self-referential round trip. The
  optional document assertion is intentionally conditional because documents
  that remove exact legacy filenames satisfy the requirement.

### Final requirement correction

| Requirement | Evidence |
|---|---|
| Exact ten Buddy-only functions | `RETIRED_BUDDY_TEST_NAMES`; `test_legacy_v1_buddy_only_and_mixed_test_nodes_are_retired_or_split` |
| Exact mixed R41 node retired/split | `RETIRED_MIXED_BUDDY_TEST_NAMES`; the same test-name inventory contract |
| Exact retired/mixed matrix and gate node IDs | `RETIRED_MATRIX_TEST_NODEIDS`, checked across all rows; `RETIRED_GATE_TEST_NODEIDS` |
| Exact retired rows/live gate and active evidence paths | `RETIRED_ACCEPTANCE_ROW_IDS`, `REMOVED_LIVE_GATE_IDS`, and `FORBIDDEN_ACTIVE_MATRIX_EVIDENCE_PATHS` |
| Official/CI tests and topology preserved | `REQUIRED_OFFICIAL_TESTS`, `PRESERVED_GATE_TEST_NODEIDS`, and `test_official_and_ci_workflows_keep_real_parseable_topology` |
| Official-only caller completeness | `test_release_orchestrate_caller_completeness_is_official_only` executes without `buddy.yml` |
| Bootstrap/actionlint exact cleanup | `test_bootstrap_governance_exact_paths_drop_only_legacy_buddy_entries`; `test_actionlint_drops_deleted_buddy_override_and_keeps_active` |
| Active exact-path documentation retirement | six cases of `test_active_v1_docs_describe_retirement_not_an_active_buddy_route` |
| Generic/v2/history and v3 Buddy preservation | exact-name/path checks replace blanket Buddy scans; `PRESERVED_V2_FILES`; unchanged route, HK, CODEOWNERS, descriptor, and Official/CI preservation contracts |
<!-- END APPEND: commit11-calibration-final-validation-2026-08-15 -->
<!-- BEGIN APPEND: commit11-v3-buddy-preservation-2026-08-15 -->
## Commit 11 v3 Buddy Preservation Closure

The calibrated preservation inventory now explicitly pins both active v3
Buddy workflow filenames, their `workflow_dispatch` triggers, and their exact
job topologies. This ensures the v1 retirement contract cannot erase v3 Buddy
workflows while still allowing generic and historical Buddy terminology.

| Command | Result |
|---|---|
| commit-11 contract collect-only | Passed; 26 cases collected. |
| narrow commit-11 contract | Expected red; 14 failed and 12 passed. |
| Ruff check/format and `git diff --check` | Passed. |

| Requirement | Evidence |
|---|---|
| Preserve v3 Buddy workflows | `PRESERVED_V3_BUDDY_WORKFLOW_JOBS`; `test_v3_buddy_workflows_keep_their_exact_topology` |
| Preserve generic/v2/history Buddy material | Exact retired v1 inventories replace blanket token assertions; `PRESERVED_V2_FILES` remains pinned. |
<!-- END APPEND: commit11-v3-buddy-preservation-2026-08-15 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit11-implementation-status-2026-08-15 -->
## Workflow Delivery v3 Commit 11 Implementation Status

Implemented locally and intentionally uncommitted until the parent commit.

### Scope evidence

- Deleted exactly `.github/workflows/buddy.yml` and
  `.github/workflows/release-buddy.yml`; no `legacy-buddy.yml`, dispatch, or
  caller-compatibility route was added.
- Updated caller completeness, bootstrap governance inventory, actionlint path
  overrides, acceptance-gate retired node IDs, the acceptance matrix, active
  v1 docs, wiki overview/index/log, and v3 handoff.
- Preserved v1 Official/CI workflows, v2 files, generic profile behavior,
  descriptors, normal v3 Buddy workflows, acceptance sentinel/attestation, and
  v3 live-attempt.

### Validation evidence

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py` | Passed: 26 passed in 2.99s. |
| Targeted workflow-release pytest selection for matrix/gate/docs/Official preservation | Passed: 12 passed in 3.65s. |
| `uv run --python 3.13 python eng/scripts/workflow_release_acceptance_gate.py` | Blocked by pre-existing unrelated failures: CodeQL action digest mismatch and missing `.pre-commit-config.yaml`; 1247 passed, 2 failed. |
| Full v3 pytest | Blocked by pre-existing `.testagent` append-only/line-index failures; 2912 passed, 2 failed. |
| Ruff check and format check on changed Python | Passed; 4 files already formatted. |
| `mise exec -- actionlint` | Passed. |
| `bash -n eng/scripts/release_orchestrate_lint_caller_completeness.sh` and `bash eng/scripts/release_orchestrate_lint_caller_completeness.sh` | Passed; caller completeness is Official-only. |
| `pnpm exec markdownlint-cli2 ...changed docs...` | Passed: 0 issues in 10 files. |
| `python -m json.tool tests/fixtures/workflow-release-acceptance-matrix.json` | Passed. |

Blockers are outside the commit-11 retirement scope and were not remediated.
Next operational step remains post-merge commit 12.
<!-- END APPEND: workflow-delivery-v3-commit11-implementation-status-2026-08-15 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit11-final-diff-check-2026-08-15 -->
## Workflow Delivery v3 Commit 11 Final Diff Check

- Final commit-11 contracts rerun passed: 26 passed in 2.92s.
- `git --no-pager diff --check` passed after implementation and status
  evidence updates.
<!-- END APPEND: workflow-delivery-v3-commit11-final-diff-check-2026-08-15 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit11-caller-route-closure-2026-08-15 -->
## Workflow Delivery v3 Commit 11 Caller Route Closure

- Removed the retained `buddy.yml` caller identity mapping and stale npmjs/setup
  comments from `release-orchestrate.yml`; the reusable caller guard now names
  only `official.yml` for the reserved Official channel.
- Rerun validation after this closure:
  - commit-11 contracts: 26 passed in 4.02s;
  - route-related workflow tests: 2 passed in 1.24s;
  - `mise exec -- actionlint`: passed;
  - `git --no-pager diff --check`: passed.
<!-- END APPEND: workflow-delivery-v3-commit11-caller-route-closure-2026-08-15 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit11-authoritative-final-2026-08-15 -->
## Workflow Delivery v3 Commit 11 Authoritative Final Status

- Retired exactly `.github/workflows/buddy.yml` and
  `.github/workflows/release-buddy.yml` with no direct, renamed, indirect, or
  allowlisted `channel=buddy` compatibility route.
- Preserved v1 Official/CI, v2 historical material, all release descriptors,
  generic Buddy planner-domain invariants, and the normal v3 Buddy workflows.
- Added an explicit v2 supersession banner, removed impossible Official GPR
  acceptance evidence, and made active v1 documentation Official-only with all
  dispatch/live procedures gated behind post-merge commit 12 and separate
  authorization.
- The final commit-11 contract contains 28 passing scenarios. The managed v3 HK
  gate passes 2,916 tests with `GIT_LFS_SKIP_SMUDGE=1`.
- Root Python passed 4,949 tests before the final documentation-only closure;
  the directly affected workflow-release and contract tests pass afterward.
- Pyrefly reports zero errors. Ruff, actionlint, policy self-tests,
  markdownlint, Prettier, PNPM tests/build, Python package build, .NET build,
  and `git diff --check` pass.
- Four original review angles, independent TP/FP adjudication, and repeated
  follow-up reviews closed with no remaining findings.
- The repository-wide workflow-release gate still exposes only the unrelated
  pre-existing CodeQL digest mismatch and missing `.pre-commit-config.yaml`.
  A direct full-v3 run may also fail when Git LFS attempts to download an
  unavailable budget-exhausted object; the managed no-smudge gate is green.
- Commit 11 remains local and uncommitted. No Environment configuration,
  workflow dispatch, old-run cancellation, package mutation, target-SHA
  finalization, or activation occurred. The next boundary remains post-merge
  commit 12.
<!-- END APPEND: workflow-delivery-v3-commit11-authoritative-final-2026-08-15 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit11-tp-adjudication-closure-2026-08-15 -->
## Workflow Delivery v3 Commit 11 TP Adjudication Closure

- Closed independently adjudicated TP findings for reserved Buddy caller-route
  rejection, reserved channel allowlist rejection, impossible Official GPR live
  gate evidence, preserved Buddy-domain reusable/profile coverage, and active
  docs wording.
- Added tests proving direct/renamed Buddy route attempts with
  `channel_allowlist: buddy` remain rejected, policy validation rejects reserved
  allowlist entries before Buddy policy selection, Official Node/Ruby GitHub
  Release rows do not require the removed GPR live gate, Buddy-domain force does
  not retarget GitHub Release tag mismatches, and reusable Buddy rerun guards
  still cover orchestrator, ensure-tag, inline publish, and attestation jobs.
- Validation rerun summary:
  - targeted commit-11/workflow tests: 35 passed;
  - policy self-test: passed;
  - Ruff check/format on changed Python: passed;
  - Pyrefly project check: passed;
  - actionlint on `release-orchestrate.yml`: passed;
  - bash syntax and shellcheck on changed shell scripts: passed;
  - Biome JSON check on acceptance matrix: passed;
  - markdownlint and Prettier on changed docs: passed.
- Remaining known blockers: full acceptance/HK workflow-release gate still fails
  on pre-existing unrelated CodeQL digest mismatch and missing
  `.pre-commit-config.yaml`; HK also reports pre-existing shfmt formatting drift
  in `release_orchestrate_policy_validate_inputs.sh`.
<!-- END APPEND: workflow-delivery-v3-commit11-tp-adjudication-closure-2026-08-15 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit11-final-contract-count-2026-08-15 -->
## Workflow Delivery v3 Commit 11 Final Contract Count

- Direct commit-11 contract rerun after the final closure passed with the
  updated collection count: 28 passed.
<!-- END APPEND: workflow-delivery-v3-commit11-final-contract-count-2026-08-15 -->
<!-- BEGIN APPEND: current-2026-08-17-bounded-regression-status -->

# Test Agent Status

**Request**: bounded regression repair for three adjudicated Workflow Delivery
v3 findings.

| Phase | Status |
|---|---|
| Research | **Complete** |
| Plan | **Complete** |
| Implement Phase 1 | **Complete** |
| Implement Phase 2 | **Complete** |
| Implement Phase 3 | **Complete** |
| Focused verification | **Complete — 93 passed in 2.25s** |
| Final package build | **Complete — success** |
| Mandatory quality gate | **Complete** |

## Research completion
- Wrote the bounded research and acceptance checklist to
  `.testagent/research.md`.
- Ran the required polyglot `find-untested-sources` analyzer exactly once with
  `--lang python --include-tested` against
  `src/public/lib/three-workflow-delivery-v3`.
- Identified the direct pairs:
  `platform/github.py` ↔ `tests/platform/test_github.py`, and the Buddy caller
  and callee YAML ↔ `tests/contracts/test_buddy_workflows.py`.
- No production or test code was edited, and no test suite was executed during
  Research.

## Plan completion
- Wrote the three sequential implementation phases to `.testagent/plan.md`:
  CR/LF-only Base64 unwrapping, one credential-stripped artifact redirect, and
  caller-path history lookup through the reusable callee.
- Mapped the full research acceptance checklist to named pytest additions,
  canonical guard suites, minimal directly coupled production changes, and
  narrow validation commands.
- Added the mandatory Step 7 `test-gap-analysis`, `assertion-quality`, and
  prompt-scenario review gate followed by final relevant pytest validation.
- No production or test code was edited, and no build, lint, or test command
  was run during Plan.

## Phase 1 completion

### Files
- `src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py`
  - Added `test_read_blob_accepts_cr_lf_wrapped_base64` with `cr`, `lf`, and
    `crlf` cases.
  - Added `test_read_blob_rejects_non_cr_lf_or_malformed_base64` with eight
    invalid-alphabet, space, tab, missing-padding, excess-padding, and wrapped
    malformed cases.
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/platform/github.py`
  - Removed only literal CR and LF from Contents API Base64 immediately before
    strict decoding with `validate=True`.

### Commands and results
- Initial focused regression:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py::test_read_blob_accepts_cr_lf_wrapped_base64 src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py::test_read_blob_rejects_non_cr_lf_or_malformed_base64`
  - Result before the production fix: **3 failed, 8 passed**; only the three
    wrapped-success cases failed.
  - Result after the minimal fix: **11 passed in 0.16s**.
- Canonical platform module:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py`
  - Result: **37 passed in 0.17s**.
- Bounded build:
  `uv build --package three-workflow-delivery-v3`
  - Result: **success**; both sdist and wheel built.
- Harness discovery from the repository root:
  `uv run --python 3.13 pytest --collect-only -q`
  - Baseline: **5044 collected**.
  - Phase 1: **5055 collected**.
  - Discovery delta: **+11**, matching all generated parameter cases.

### Quality review
- Pseudo-mutation review found no Phase 1 gap: the tests kill removal of either
  CR/LF normalization, generic-whitespace normalization, relaxed alphabet or
  padding validation, incorrect decoded bytes/OID, and loss of the
  `GitHubRestError` boundary.
- Assertion review found no assertion-free, trivial-only, or self-referential
  Phase 1 tests. The tests assert exact decoded state, exact failure identity
  and message, timeout use, and the exact requested URL.
- No Phase 2 or Phase 3 file or behavior was changed.

## Phase 2 completion

### Files
- `src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py`
  - Added 8 test functions collecting 27 cases:
    - `test_download_artifact_follows_one_off_origin_https_302_without_credentials`
      (1 case).
    - `test_download_artifact_rejects_unsafe_or_non_off_origin_location_before_follow_up`
      (9 cases).
    - `test_list_runs_does_not_use_the_artifact_redirect_exception`
      (1 case).
    - `test_download_artifact_rejects_non_302_initial_redirect`
      (4 cases).
    - `test_download_artifact_rejects_any_redirect_from_the_blob_without_a_third_request`
      (4 cases).
    - `test_download_artifact_rejects_302_without_location` (1 case).
    - `test_download_artifact_redirect_preserves_http_and_network_errors`
      (4 cases).
    - `test_download_artifact_redirect_preserves_archive_validation`
      (3 cases).
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/platform/github.py`
  - Routed only `download_artifact` through a private one-redirect transport.
  - The initial API request remains authenticated. The sole follow-up is a
    fresh, header-free request to an absolute off-origin HTTPS URL.
  - Generic requests retain the existing fixed-API-origin redirect path.
- `.testagent/status.md`
  - Recorded the Phase 2 files, tests, commands, results, and bounded
    verification limitations.

### Commands and results
- Harness baseline before Phase 2:
  `uv run --python 3.13 pytest --collect-only -q`
  - Result: **5055 collected**.
- Expected-red focused regression:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py -k "download_artifact or list_runs_does_not_use_the_artifact_redirect_exception"`
  - Result before the production change: **3 passed, 20 failed, 37
    deselected**.
  - Final result after the minimum transport change and completed unsafe-URL
    matrix: **27 passed, 37 deselected in 0.09s**.
- Canonical platform module:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py`
  - Result: **64 passed in 0.21s**.
- History-admission guard:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_history_admission.py`
  - Result: **44 passed in 0.07s**.
- Bounded build:
  `uv build --package three-workflow-delivery-v3`
  - Result: **success**; both sdist and wheel built.
- Final harness discovery from the repository root:
  `uv run --python 3.13 pytest --collect-only -q`
  - Result: **5082 collected**.
  - Phase 2 discovery delta: **+27**, exactly matching the generated cases.
- Phase 2 production lint and format checks:
  `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/platform/github.py`
  and
  `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/platform/github.py`
  - Result: **passed**.
- Phase 2 test lint:
  `uv run --python 3.13 ruff check --ignore PLR2004 --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py`
  - Result: **passed**. The exact unignored module command still reports only
    the two preserved Phase 1 `timeout == 20` `PLR2004` findings. Ruff's
    remaining format diff likewise contains only the two preserved Phase 1
    path literals; no Phase 2 line remains in that diff.

### Quality review
- Pseudo-mutation review found no Phase 2 acceptance gap. The tests kill
  removal or broadening of artifact-only routing, reuse of authenticated
  headers, unsafe/same-origin/relative targets, any non-302 first redirect,
  any second redirect, lost timeout forwarding, changed HTTP/network error
  identity, weakened ZIP cardinality/malformed checks, and changed returned
  bytes.
- Assertion review found no assertion-free, trivial-only, or
  self-referential Phase 2 tests. Each case combines exact errors or bytes
  with request count, URL, method, timeout, and/or header side effects.
- Research found no explicit archive response-byte cap or absolute-deadline
  mechanism in this client. Phase 2 therefore introduced or weakened neither;
  existing timeout forwarding and archive format/cardinality checks are pinned
  by the new tests.

### Bounded verification issue
- The package-wide command
  `python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
  completed with **2787 passed, 21 failed, 147 errors**. The failures are
  outside Phase 2: the dominant setup error is
  `runtime PNPM version differs from frozen Build Request` in the explicitly
  excluded Node surface; the remaining failures include pre-existing
  working-tree contract expectations for `.testagent/plan.md` and historical
  status text. No excluded source or pre-existing change was altered.

No Phase 3 workflow or history-discovery behavior was changed.

## Phase 3 completion

### Files
- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
  - Added 1 test collecting 1 case:
    `test_history_discovery_uses_caller_path_through_reusable_live_attempt_topology`.
  - The regression composes the existing `CALLER`, `CALLEE`, `_document`,
    `_step`, `_run`, and `EXPECTED_JOBS` helpers to pin the five-job caller,
    reusable-callee edge, twelve-job callee, exact history subcommand, sole
    caller `--workflow-path`, and absence of the callee path.
- `.github/workflows/workflow-delivery-v3-live-attempt.yml`
  - Changed only the history discovery `--workflow-path` from the reusable
    callee to `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`.
- `.testagent/status.md`
  - Recorded the Phase 3 files, exact test/count, commands, and results.

### Commands and results
- Harness baseline before Phase 3:
  `uv run --python 3.13 pytest --collect-only -q`
  - Result: **5082 collected**.
- Expected-red focused regression:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py::test_history_discovery_uses_caller_path_through_reusable_live_attempt_topology`
  - Result before the workflow correction: **1 failed in 0.24s**; the sole
    failure was the callee path instead of the required caller path.
  - Result after the one-line correction: **1 passed in 0.14s**.
  - Result after the formatting-only fix: **1 passed in 0.22s**.
- Canonical Buddy workflow contract module:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
  - Result: **29 passed in 2.07s**.
- Phase 3 topology and unchanged admission guards:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_history_admission.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
  - Result: **178 passed in 8.25s**.
- Bounded build:
  `uv build --package three-workflow-delivery-v3`
  - Result: **success**; both sdist and wheel built.
- Final harness discovery from the repository root:
  `uv run --python 3.13 pytest --collect-only -q`
  - Result: **5083 collected**.
  - Phase 3 discovery delta: **+1**, exactly matching the generated case.
- Phase 3 test lint and format checks:
  `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
  and
  `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
  - Result: **passed**.
- `git diff --check`
  - Result: **passed**.

### Quality review
- Pseudo-mutation review found no Phase 3 gap: the new test kills a changed or
  duplicated history subcommand/path, restoration of the reusable-callee path,
  removal or retargeting of the caller-to-callee edge, and caller/callee job-set
  drift. Existing contract guards continue to kill artifact-ID, raw-mode,
  naming, retention, DAG, permission, and error-propagation changes.
- Assertion review found 6 meaningful assertions spanning exact collection,
  command, path, and negative-presence checks. The test has no assertion-free,
  trivial-only, or self-referential assertion and checks topology as secondary
  evidence alongside the queried workflow identity.
- The caller workflow, admission implementation, live adapter context, Node
  version, package owner endpoint, artifact raw-mode/name/ID semantics,
  concurrency, and unrelated workflow behavior were not changed.

## Final coordinator closure

### Fresh final results
- Focused scoped verification:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
  - Final result after lint-only cleanup: **93 passed in 2.30s**.
- Final package build:
  `uv build --package three-workflow-delivery-v3`
  - Result: **success**.
- Final bounded Ruff checks:
  `uv run --python 3.13 ruff check --force-exclude -- <three modified Python files>`
  and
  `uv run --python 3.13 ruff format --check --force-exclude -- <three modified Python files>`
  - Result: **passed; all three files are formatted**.
- Append-only state-contract verification:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py::test_testagent_plan_update_is_append_only_against_head src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py::test_historical_status_identifier_and_typos_scope_are_exact`
  - Final result: **2 passed in 0.21s**.
- Workspace validation:
  `uv run --python 3.13 pytest -q`
  - Result: **4915 passed, 21 failed, 147 errors in 600.31s**.
  - Of the 21 failures, **19 Node/toolchain/LFS failures** are outside the
    bounded scope and explicitly excluded.
  - All **147 Node errors** are likewise outside the bounded scope and
    explicitly excluded.
  - The remaining **two HK failures were caused by the overwritten
    `.testagent` state artifacts**; this append-only state repair resolves
    those two failures.

### Mandatory quality gate
- Research, Plan, all three Implement phases, focused verification, the final
  package build, and the mandatory quality gate are complete.
- Pseudo-mutation review concludes that **all requested mutation classes are
  killed**.
- Assertion-depth review finds **no assertion-free, trivial-only,
  tautological, or missing-secondary-observable generated tests**.
- Prompt-scenario mapping and final gap review find **no remaining in-scope
  gap**.

<!-- END APPEND: current-2026-08-17-bounded-regression-status -->

<!-- BEGIN APPEND: current-2026-08-17-bounded-regression-review-closure -->

## Independent review closure

An independent adversarial review identified three test/validation gaps, all
closed within the existing scope:

- Redirect tests now assert that the installed urllib redirect handler returns
  `None`, so removal of automatic-redirect suppression cannot evade the
  credential-stripping regression.
- Artifact redirect validation now rejects whitespace/control characters and
  normalizes malformed URL parsing and request-construction failures to
  `GitHubRestError`; the unsafe-location matrix adds malformed IPv6, leading
  space, and trailing-control cases.
- The caller/callee topology regression now requires the caller to be exactly
  `workflow_dispatch` and the callee to be exactly `workflow_call`.

Final targeted pytest after these changes passed **140 tests in 2.32s**.
Final Ruff check passed, and Ruff format reported all three changed Python files
already formatted.

<!-- END APPEND: current-2026-08-17-bounded-regression-review-closure -->

<!-- BEGIN APPEND: current-2026-08-17-bounded-regression-c1-closure -->

## Final redirect character-set closure

A focused follow-up review identified C1/non-ASCII URL characters as the final
unsafe redirect gap. Artifact redirect targets are now required to be ASCII,
and the unsafe-location matrix includes a `\x80` case proving rejection before
the follow-up request. The focused reviewer then reported no findings.

Final scoped pytest passed **141 tests in 2.28s**. Ruff check passed, Ruff
format reported all three changed Python files already formatted, actionlint
passed for the Buddy caller/callee pair, and `git diff --check` passed.

<!-- END APPEND: current-2026-08-17-bounded-regression-c1-closure -->

<!-- BEGIN APPEND: 2026-08-18-v3-artifact-transport-status -->

## v3 artifact transport completion

- Reviewer transport now uses normal archived upload and decompressed download;
  upload digest and snapshot/summary payload digest bindings remain exact.
- Authorization and mutation-marker raw uploads now materialize the exact
  attempt-specific configured basename, and all local consumers use that path.
- Publisher closure and release-finalization authority downloads now use exact
  comma-delimited IDs with merged flat raw layout.
- No package endpoint, live adapter context, Node version, concurrency, GitHub
  platform client, or history-admission production code changed.

### Validation

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py::test_download_artifact_redirect_preserves_archive_validation src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_history_admission.py::test_discovery_skips_unrelated_json_non_json_and_multifile_artifacts` | 37 passed in 2.73s |
| `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | Passed |
| `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | 1 file already formatted |
| `uv run --python 3.13 pyrefly check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | 0 errors; 1 warning not shown |
| `actionlint .github/workflows/workflow-delivery-v3-live-attempt.yml` | Passed |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py::test_testagent_plan_update_is_append_only_against_head` | 1 passed in 0.22s |

### Test quality review

- Pseudo-mutation review killed removal or substitution of reviewer
  archive/decompression settings, upload and payload digest bindings, raw
  basename materialization, renamed local consumers, comma delimiters,
  `merge-multiple`, `skip-decompress`, flat paths, and every listed artifact
  ID.
- Assertion review found no assertion-free, trivial-only, or self-referential
  generated tests. The scenarios combine exact structural equality, string
  presence/absence, ordering, and invariant assertions.

<!-- END APPEND: 2026-08-18-v3-artifact-transport-status -->

<!-- BEGIN APPEND: 2026-08-18-v3-artifact-review-remediation -->

## Adversarial review remediation

Independent runtime and contract reviews produced four true positives:

1. `upload-artifact@v7` emits a bare lowercase SHA-256 digest, while mutation
   marker admission required a prefixed digest.
2. Raw-upload tests did not reject directory, wildcard, or multi-path inputs.
3. Reviewer and Authorization producer-before-upload ordering was not locked.
4. Reviewer formatter and Authorization basename/Base64 handoffs were not
   locked across step outputs, job outputs, environments, and consumers.

The runtime now accepts both the native bare upload digest and the canonical
`sha256:` form through the existing strict digest normalizer. Contract tests
require one exact raw path with a matching physical basename, lock producer
ordering, and assert each cross-job handoff through its final consumers.

### Pseudo-mutation and assertion review

- Removal of the positive artifact-ID guard is killed by zero and negative
  boundary cases.
- Removal of strict digest normalization is killed by malformed length,
  uppercase, duplicate-prefix, wrong-algorithm, and whitespace cases.
- Restoring the former prefixed-only behavior is killed by the native v7
  bare-digest scenario.
- Reviewer archive/decompression, producer ordering, output handoff, raw
  basename, comma-delimited ID, and merged-layout mutations are all killed by
  the workflow contract scenarios.
- No in-scope mutation survived and no changed path lacks coverage.
- The six generated test functions expand to fifteen pytest cases. None is
  assertion-free, trivial-only, or self-referential. Assertions cover deep
  structural equality, exact exceptions, ordering, collection membership,
  string presence/absence, and negative invariants.

### Focused validation

`pytest` passed all 42 focused cases, including the complete Buddy workflow
contract module and mutation-marker CLI cases. Ruff check and format passed for
all three changed Python files, Pyrefly reported zero errors, actionlint passed,
and `git diff --check` passed.

<!-- END APPEND: 2026-08-18-v3-artifact-review-remediation -->

<!-- BEGIN APPEND: 2026-08-18-v3-artifact-final-review -->

## Final artifact transport review

A follow-up adversarial review found two additional true positives. The
reviewer archive could contain a substituted snapshot copy because binding
read the separate raw snapshot path, and the malformed digest matrix omitted
lowercase non-hex characters. The binding command now validates the exact
`publication-snapshot.json` file placed in the reviewer archive. Bare and
prefixed 64-character lowercase non-hex digests are both rejected by focused
tests.

Focused validation passed **44 tests**. Ruff, Pyrefly, actionlint, and
`git diff --check` passed. The final independent reviewer reported
`RAW_FINDINGS: none`.

<!-- END APPEND: 2026-08-18-v3-artifact-final-review -->

<!-- BEGIN APPEND: 2026-08-18-v3-artifact-full-validation -->

## Full validation closure

- Complete Workflow Delivery v3 suite: **2,991 passed**.
- Complete repository HK pre-commit gate: **passed**.
- Validation used the repository-pinned MISE toolchain and
  `GIT_LFS_SKIP_SMUDGE=1`.
- The initial full-suite retry was blocked by exhausted `/tmp` inodes, not a
  product assertion. After removing only known stale v3 validation directories,
  the two affected PNPM scenarios passed and the complete suite and HK gate
  completed successfully.

<!-- END APPEND: 2026-08-18-v3-artifact-full-validation -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-phase-1-start -->

## Phase 1 start: Publication-Preparation Record Invariants

- Started: `2026-08-19T01:16:18.875Z`.
- Scope: extend
  `test_publication_preparation_outcome_rejects_each_forbidden_fact` only;
  do not begin Phase 2.
- Required parameter IDs:
  `uncertainty`, `authorization-digest`,
  `publication-snapshot-digest`, `capability-admission-digests`,
  `capability-group-bundle-digests`, `receipt-digests`, `result`,
  `possibly-mutated`, and `next-action`.
- Planned narrow command:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`
- Planned handoff command:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
- Changed paths at phase start: `.testagent/status.md`.
- Production changed: no.

<!-- END APPEND: 2026-08-19-publication-preparation-phase-1-start -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-phase-1-characterization -->

## Phase 1 characterization

- Harness baseline:
  `uv run --python 3.13 pytest --collect-only -q` collected **5,152 tests**
  in 0.86s.
- Narrow characterization:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`
  passed **45 tests** in 0.15s.
- Existing exact collected publication-preparation cases:
  - `test_commit8_records_reject_independent_binding_substitutions[record9-uncertainty-False-Publication preparation]`
  - `test_commit8_records_reject_independent_binding_substitutions[record10-authorization_digest-sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff-Publication preparation]`
  - `test_commit8_records_reject_independent_binding_substitutions[record11-publication_snapshot_digest-sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff-Publication preparation]`
- Characterization result: green baseline; the table does not yet collect the
  six required direct substitutions.
- Changed paths: `.testagent/status.md`.
- Production changed: no.

<!-- END APPEND: 2026-08-19-publication-preparation-phase-1-characterization -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-phase-1-green -->

## Phase 1 green result

- Exact retained test function:
  `test_commit8_records_reject_independent_binding_substitutions`.
- Exact publication-preparation parameter IDs:
  - `uncertainty`
  - `authorization-digest`
  - `publication-snapshot-digest`
  - `capability-admission-digests`
  - `capability-group-bundle-digests`
  - `receipt-digests`
  - `result`
  - `possibly-mutated`
  - `next-action`
- Direct substitutions use `dataclasses.replace` against the canonical real
  `AttemptOutcome`; every row requires `ValueError` matching
  `(?i)publication[- ]preparation`.
- `uv build --package three-workflow-delivery-v3` succeeded and produced the
  source distribution and wheel.
- Narrow record command:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`
  passed **51 tests** in 0.18s.
- Focused collection of the retained test function found **18 cases**,
  including the nine exact IDs above.
- Test-first result: all six added cases passed on their first execution, so
  no missing production invariant was demonstrated.
- Changed paths:
  `.testagent/status.md`;
  `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`.
- Production changed: no; `records/release.py` is unchanged.

<!-- END APPEND: 2026-08-19-publication-preparation-phase-1-green -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-phase-1-handoff -->

## Phase 1 handoff

### Exact collected publication-preparation cases

- `test_commit8_records_reject_independent_binding_substitutions[uncertainty]`
- `test_commit8_records_reject_independent_binding_substitutions[authorization-digest]`
- `test_commit8_records_reject_independent_binding_substitutions[publication-snapshot-digest]`
- `test_commit8_records_reject_independent_binding_substitutions[capability-admission-digests]`
- `test_commit8_records_reject_independent_binding_substitutions[capability-group-bundle-digests]`
- `test_commit8_records_reject_independent_binding_substitutions[receipt-digests]`
- `test_commit8_records_reject_independent_binding_substitutions[result]`
- `test_commit8_records_reject_independent_binding_substitutions[possibly-mutated]`
- `test_commit8_records_reject_independent_binding_substitutions[next-action]`

### Commands and results

| Command | Result |
|---|---|
| `uv run --python 3.13 pytest --collect-only -q` before the test change | 5,152 tests collected in 0.86s |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py` before the test change | 45 passed in 0.15s |
| `uv build --package three-workflow-delivery-v3` | Source distribution and wheel built successfully |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py` after the test change | 51 passed in 0.18s |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | 198 passed in 8.04s |
| `uv run --python 3.13 pytest --collect-only -q` after the test change | 5,158 tests collected in 0.95s; harness delta **+6**, matching the six added rows |
| `git diff --check` | Passed |

### Quality review and scope

- Pseudo-mutation review found no in-scope gap: removing any one
  publication-preparation condition is killed by its direct one-field row,
  while the retained canonical round-trip case prevents unconditional
  rejection.
- Assertion-quality review found no assertion-free, trivial, or
  self-referential case. Each negative checks the exact exception type and the
  publication-preparation diagnostic; the canonical case checks deep record
  and document equality.
- Phase-changed paths:
  `.testagent/status.md`;
  `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`.
- Pre-existing modified `.testagent/plan.md` and `.testagent/research.md` were
  treated as authoritative and left untouched.
- Production changed: no; `records/release.py` required no change.
- Phase 2 was not begun.

<!-- END APPEND: 2026-08-19-publication-preparation-phase-1-handoff -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-phase-2-start -->

## Phase 2 start: Classifier and Publisher Truth Table

- Started: `2026-08-19T01:22:42.081Z`.
- Scope: execute the exact workflow YAML `release-finalizer` /
  `Finalize Attempt Outcome` shell through a CLI boundary double; do not begin
  Phase 3.
- Planned test functions:
  - `test_publication_preparation_classifier_executes_workflow_shell`
  - `test_publication_preparation_classifier_rejects_invalid_workflow_facts`
  - `test_publisher_result_truth_table_executes_workflow_shell`
- Required admitted-case IDs:
  `observation-failure__materialization-skipped`,
  `observation-cancelled__materialization-cancelled`,
  `observation-success__snapshot-upload-failure`,
  `observation-success__materialization-cancelled`,
  `workflow-cancelled__observation-skipped__materialization-skipped`, and
  `workflow-cancelled__observation-success__materialization-skipped`.
- Required invalid-fact IDs:
  `unexplained-observation-skip`,
  `materialization-success-without-durable-snapshot`,
  `snapshot-artifact-id-without-upload-digest`,
  `snapshot-upload-digest-without-artifact-id`,
  `publisher-success`, and `publisher-failure`.
- Required publisher IDs:
  `whole-run-cancelled-unstarted`,
  `cancelled-without-workflow-ownership`,
  `cancelled-with-forwarded-snapshot`,
  `cancelled-with-authorization`,
  `cancelled-with-capability-admission`,
  `cancelled-with-mutation-marker`,
  `cancelled-with-result-bundle`,
  `cancelled-with-receipt`, and `post-snapshot-cancelled`.
- Harness discovery baseline:
  `uv run --python 3.13 pytest --collect-only -q` collected **5,158 tests**
  in 0.89s.
- Workflow characterization baseline:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
  passed **40 tests** in 3.56s.
- Changed paths at phase start:
  `.testagent/status.md`; pre-existing Phase 1 and planning changes remain
  authoritative and untouched.
- Production changed: no.

<!-- END APPEND: 2026-08-19-publication-preparation-phase-2-start -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-phase-2-initial-red -->

## Phase 2 initial tests-first result

- Package build:
  `uv build --package three-workflow-delivery-v3` succeeded.
- Required workflow command:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
  produced **45 passed, 15 failed**.
- Seven admitted shell executions reached the CLI exactly once but exposed an
  over-specific harness assertion: argv correctly contains
  `three-workflow-delivery-v3` twice, once as the `--package` value and once as
  the executable. Affected IDs:
  - `test_publication_preparation_classifier_executes_workflow_shell[observation-failure__materialization-skipped]`
  - `test_publication_preparation_classifier_executes_workflow_shell[observation-cancelled__materialization-cancelled]`
  - `test_publication_preparation_classifier_executes_workflow_shell[observation-success__snapshot-upload-failure]`
  - `test_publication_preparation_classifier_executes_workflow_shell[observation-success__materialization-cancelled]`
  - `test_publication_preparation_classifier_executes_workflow_shell[workflow-cancelled__observation-skipped__materialization-skipped]`
  - `test_publication_preparation_classifier_executes_workflow_shell[workflow-cancelled__observation-success__materialization-skipped]`
  - `test_publisher_result_truth_table_executes_workflow_shell[post-snapshot-cancelled]`
- One symmetric partial-transport production gap was exposed:
  `test_publication_preparation_classifier_rejects_invalid_workflow_facts[snapshot-artifact-id-without-upload-digest]`
  reached the CLI and returned zero instead of rejecting before invocation.
- The approved cancellation row failed as expected:
  `test_publisher_result_truth_table_executes_workflow_shell[whole-run-cancelled-unstarted]`
  rejected `publish_result=cancelled` with
  `Publication preparation interruption did not skip the publisher`.
- Each cancellation-with-lineage row rejected at the older publisher-result
  gate rather than the downstream-lineage gate:
  - `test_publisher_result_truth_table_executes_workflow_shell[cancelled-with-forwarded-snapshot]`
  - `test_publisher_result_truth_table_executes_workflow_shell[cancelled-with-authorization]`
  - `test_publisher_result_truth_table_executes_workflow_shell[cancelled-with-capability-admission]`
  - `test_publisher_result_truth_table_executes_workflow_shell[cancelled-with-mutation-marker]`
  - `test_publisher_result_truth_table_executes_workflow_shell[cancelled-with-result-bundle]`
  - `test_publisher_result_truth_table_executes_workflow_shell[cancelled-with-receipt]`
- No workflow or Python production file had been changed at this red point.

<!-- END APPEND: 2026-08-19-publication-preparation-phase-2-initial-red -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-phase-2-confirmed-red -->

## Phase 2 confirmed production failures

After correcting only the CLI-prefix assertion in the new harness, the same
required workflow command produced **52 passed, 8 failed** in 6.51s:

- `test_publication_preparation_classifier_rejects_invalid_workflow_facts[snapshot-artifact-id-without-upload-digest]`
  returned zero and invoked the CLI instead of rejecting partial transport.
- `test_publisher_result_truth_table_executes_workflow_shell[whole-run-cancelled-unstarted]`
  rejected the approved cancellation at the skipped-only publisher gate.
- The following six cases also stopped at that publisher gate instead of
  independently reaching downstream-lineage rejection:
  - `test_publisher_result_truth_table_executes_workflow_shell[cancelled-with-forwarded-snapshot]`
  - `test_publisher_result_truth_table_executes_workflow_shell[cancelled-with-authorization]`
  - `test_publisher_result_truth_table_executes_workflow_shell[cancelled-with-capability-admission]`
  - `test_publisher_result_truth_table_executes_workflow_shell[cancelled-with-mutation-marker]`
  - `test_publisher_result_truth_table_executes_workflow_shell[cancelled-with-result-bundle]`
  - `test_publisher_result_truth_table_executes_workflow_shell[cancelled-with-receipt]`

This is the final red state before the workflow edit.

<!-- END APPEND: 2026-08-19-publication-preparation-phase-2-confirmed-red -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-phase-2-green -->

## Phase 2 green result: Classifier and Publisher Truth Table

### Exact executable tests and parameter IDs

`test_publication_preparation_classifier_executes_workflow_shell`:

- `observation-failure__materialization-skipped`
- `observation-cancelled__materialization-cancelled`
- `observation-success__snapshot-upload-failure`
- `observation-success__materialization-cancelled`
- `workflow-cancelled__observation-skipped__materialization-skipped`
- `workflow-cancelled__observation-success__materialization-skipped`

`test_publication_preparation_classifier_rejects_invalid_workflow_facts`:

- `unexplained-observation-skip`
- `materialization-success-without-durable-snapshot`
- `snapshot-artifact-id-without-upload-digest`
- `snapshot-upload-digest-without-artifact-id`
- `publisher-success`
- `publisher-failure`

`test_publisher_result_truth_table_executes_workflow_shell`:

- `whole-run-cancelled-unstarted`
- `cancelled-without-workflow-ownership`
- `cancelled-with-forwarded-snapshot`
- `cancelled-with-authorization`
- `cancelled-with-capability-admission`
- `cancelled-with-mutation-marker`
- `cancelled-with-result-bundle`
- `cancelled-with-receipt`
- `post-snapshot-cancelled`
- `post-snapshot-skipped`
- `post-snapshot-success-with-mutation-marker`
- `post-snapshot-failure-with-result-bundle`
- `post-snapshot-failure-without-result-bundle`
- `post-snapshot-failure-with-mutation-marker`
- `post-snapshot-cancelled-with-mutation-marker`

The last six IDs retain the non-cancellation publisher-result,
result-bundle, and mutation-marker semantics formerly calculated only in
Python. The two duplicated Python-only truth-calculation tests are retained in
source to honor the existing-test append-only boundary but marked
non-collectable; all of their semantic rows now execute the exact workflow
shell.

### Harness and exact workflow change

- The harness parses the current YAML with `yaml.safe_load`, locates exactly
  `release-finalizer` / `Finalize Attempt Outcome`, renders an explicit map for
  every `${{ ... }}` fact, and asserts no expression remains.
- It runs the exact extracted `run` value with
  `bash --noprofile --norc -euo pipefail -c`, a `tmp_path` working directory,
  explicit GitHub environment files/facts, and one executable `uv` CLI
  boundary double. No Python classifier was added.
- The CLI double records the exact argv, writes Outcome and summary files, and
  exposes a secondary GitHub-output observable. Rejected rows prove the double
  was never invoked.
- `.github/workflows/workflow-delivery-v3-live-attempt.yml` changed by only
  three narrow classifier decisions:
  1. reject a Snapshot artifact ID with a missing upload digest, complementing
     the existing digest-without-ID rejection;
  2. allow `publish_result=cancelled` through the preparation publisher gate
     only when `workflow_cancelled=true`; the surrounding no-Snapshot branch
     and existing downstream-lineage guard remain mandatory;
  3. add `--platform-terminated` for publisher cancellation only when the same
     row was not classified as publication-preparation interruption.
- Ordinary post-Snapshot cancellation still emits
  `--platform-terminated`; the approved unstarted whole-run cancellation emits
  exactly one `--publication-preparation-interrupted` and no platform flag.

### Commands and exact results

| Command | Result |
|---|---|
| `uv build --package three-workflow-delivery-v3` | Succeeded; sdist and wheel built |
| Workflow command before new tests | 40 passed in 3.56s |
| First tests-first workflow run | 45 passed, 15 failed; seven failures were one over-specific CLI-prefix assertion |
| Confirmed red workflow run after the harness-only correction | 52 passed, 8 failed in 6.51s; exact production rows are recorded above |
| Workflow run immediately after the YAML fix | 60 passed in 6.26s |
| Final workflow run after folding the legacy publisher calculation | **65 passed in 7.21s** |
| `uv run --python 3.13 pytest --collect-only -q` | **5,183 collected** in 0.85s; delta **+25** from 5,158, matching 27 shell scenarios minus two folded Python-only tests |
| Ruff check and format check on `test_buddy_workflows.py` | Passed |
| `pyrefly check test_buddy_workflows.py` | 0 errors |
| `actionlint .github/workflows/workflow-delivery-v3-live-attempt.yml` | Passed |
| `git diff --check` | Passed |

The complete affected package command ran **3,051** tests: **3,049 passed**
and two unrelated authoritative-worktree checks failed after 407.21s:

1. `test_installed_nbgv_api_returns_exact_head_and_native_projection` was
   blocked by the repository's exhausted Git LFS budget.
2. `test_testagent_plan_update_is_append_only_against_head` rejects the
   pre-existing non-append-only `.testagent/plan.md` replacement.

The first documented affected-file HK invocation,
`hk check --check <files>`, was rejected by this HK build because `--check`
cannot be combined with explicit files. The non-mutating equivalent
`HK_FIX=0 hk check <files>` passed `typos`, `actionlint`,
`editorconfig-check`, Ruff check/format, consumer policy, and
`workflow-release-control-tests` (**1,257 passed**). Its package-test step
reported the same two unrelated blockers above; no Phase 2 check failed.

### Test-gap and assertion-quality review

- Pseudo-mutations of every in-scope Observation/materialization branch,
  workflow-cancellation ownership, either half of Snapshot transport,
  publisher-result admission, each of the six lineage operands, preparation
  versus platform flag selection, publisher failure with/without a result
  bundle, and mutation-marker independence are killed by distinct shell rows.
- No in-scope mutation survived. Qualification failure and CLI domain-record
  validation remain deliberately outside this workflow-classifier phase and
  retain their existing dedicated tests.
- All 27 generated shell scenarios have meaningful assertions. Successful
  rows verify status, one exact CLI invocation/prefix, exact positive and
  negative semantic flags, durable Snapshot argument values where applicable,
  and GitHub-output side effects. Rejected rows verify nonzero status, a
  relevant diagnostic, and zero CLI invocations.
- There are no assertion-free, trivial-only, or self-referential generated
  cases. Assertions span equality, comparison, string, collection, negative,
  structural, and state/side-effect categories.

### Changed paths and handoff

Phase 2 changed:

- `.github/workflows/workflow-delivery-v3-live-attempt.yml`
- `.testagent/status.md`
- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`

Pre-existing `.testagent/plan.md`, `.testagent/research.md`, and
`tests/release/test_commit8_contracts.py` changes remain authoritative and were
not modified by Phase 2. No Python production file changed. Phase 2 is green;
Phase 3 has not begun.

<!-- END APPEND: 2026-08-19-publication-preparation-phase-2-green -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-phase-3-start -->

## Phase 3 start: Snapshot Lifecycle, Reviewer Diagnostics, and Retention

- Started: `2026-08-19T01:58:32.572Z`.
- Scope: implement only the six Phase 3 tests in
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`,
  reusing Phase 2's exact-workflow-shell harness, then make only the expected
  completed reviewer-summary workflow correction proven by the tests.
- Planned exact tests:
  - `test_publication_snapshot_lifecycle_and_transport_identity_are_exact`
  - `test_release_finalizer_downloads_snapshot_directly_from_materialization`
  - `test_durable_snapshot_survives_later_reviewer_failure`
  - `test_completed_materialization_summary_links_immutable_reviewer_artifact`
  - `test_incomplete_preparation_retains_diagnostics_before_job_failure`
  - `test_propagation_fails_after_successful_retention`
- Required validation: the narrow workflow command followed by the five-file
  bounded integration command. The final global quality gate is explicitly
  deferred.
- Changed paths at phase start: `.testagent/status.md`.
- Production changed: no.
- Blockers: none.

<!-- END APPEND: 2026-08-19-publication-preparation-phase-3-start -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-phase-3-red -->

## Phase 3 tests-first red result

- Recorded: `2026-08-19T02:03:09.247Z`.
- Harness discovery baseline from the repository root:
  `uv run --python 3.13 pytest --collect-only -q` collected **5,183 tests**
  in 0.95s.
- Pre-change workflow baseline:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
  passed **65 tests** in 7.10s.
- Tests-first workflow command after appending all six Phase 3 tests produced
  **70 passed, 1 failed** in 8.12s.
- Exact expected failure:
  `test_completed_materialization_summary_links_immutable_reviewer_artifact`
  could not find the required post-upload
  `Publish completed reviewer summary and artifact link` step. The current
  workflow still writes `reviewer-summary.md` to `GITHUB_STEP_SUMMARY` from
  `names`, before either upload, and never appends
  `steps.upload-reviewer.outputs.artifact-url`.
- The other five exact Phase 3 tests passed against the authoritative current
  workflow, proving no additional lifecycle, direct-Snapshot, durable-Snapshot,
  finalizer-postamble, or propagation production change is indicated.
- Changed paths at the red point:
  `.testagent/status.md`;
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`.
- Workflow/production changed after the new tests: no.
- Blockers: none; the failure authorizes only the planned reviewer-summary
  workflow correction.

<!-- END APPEND: 2026-08-19-publication-preparation-phase-3-red -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-phase-3-green -->

## Phase 3 green result: Snapshot Lifecycle, Reviewer Diagnostics, and Retention

- Recorded: `2026-08-19T02:06:04.090Z`.
- Exact tests now green:
  - `test_publication_snapshot_lifecycle_and_transport_identity_are_exact`
  - `test_release_finalizer_downloads_snapshot_directly_from_materialization`
  - `test_durable_snapshot_survives_later_reviewer_failure`
  - `test_completed_materialization_summary_links_immutable_reviewer_artifact`
  - `test_incomplete_preparation_retains_diagnostics_before_job_failure`
  - `test_propagation_fails_after_successful_retention`
- `test_durable_snapshot_survives_later_reviewer_failure` executes the exact
  Phase 2 finalizer shell with materialization result `failure` after a durable
  Snapshot and proves one CLI call preserves the Snapshot path, payload digest,
  artifact ID, and upload digest without either interruption flag.
- `test_incomplete_preparation_retains_diagnostics_before_job_failure`
  executes the exact finalizer shell with a status-`1` CLI boundary, proving
  the retained Outcome, Attempt summary, job summary, CLI outputs, derived
  retention artifact name, and status survive the nonzero result.
- `test_propagation_fails_after_successful_retention` retains exact
  finalize/upload/propagate ordering and `if: always()` protection, then
  executes the exact propagation shell with a successful upload and proves
  status `1` alone produces the later nonzero result.
- Minimal workflow correction:
  - removed the pre-upload `reviewer-summary.md` read from `names`;
  - added `Publish completed reviewer summary and artifact link` after
    `upload-reviewer` and before `bind`;
  - gated it on successful reviewer upload;
  - read the immutable reviewer payload into `GITHUB_STEP_SUMMARY` and appended
    `steps.upload-reviewer.outputs.artifact-url` only to the job summary.
  No Python production code or other workflow section changed in Phase 3.

### Commands and results

| Command | Result |
|---|---|
| `uv build --package three-workflow-delivery-v3` | Succeeded; sdist and wheel built |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | **71 passed** in 7.74s |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | **269 passed** in 15.34s |
| `uv run --python 3.13 pytest --collect-only -q` | **5,189 collected** in 0.86s; delta **+6** from 5,183, exactly matching the six added tests |

- Phase 3 changed paths:
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`;
  `.testagent/status.md`;
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`.
- Blockers: none.
- Per the user instruction, the final global quality gate has not been run.

<!-- END APPEND: 2026-08-19-publication-preparation-phase-3-green -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-phase-3-handoff -->

## Phase 3 handoff

- Completed: `2026-08-19T02:06:55.252Z`.
- Status: **green**.
- Tests created: **6**; Phase 3 tests passing: **6**.
- Harness discovery: **+6** tests, from 5,183 to 5,189.
- Narrow workflow result: **71 passed**.
- Five-file bounded integration result: **269 passed**.
- Package build: succeeded.
- `git diff --check`: passed.
- Phase 3 changed paths:
  - `.github/workflows/workflow-delivery-v3-live-attempt.yml`
  - `.testagent/status.md`
  - `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- Pre-existing authoritative changes in `.testagent/plan.md`,
  `.testagent/research.md`, and
  `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`
  were not modified by Phase 3. No Python production file changed.
- Blockers: none.
- Next action: run the separately requested final global quality gate; it was
  deliberately not run during this handoff.

<!-- END APPEND: 2026-08-19-publication-preparation-phase-3-handoff -->

## Validation fix - 2026-08-19

- Corrected `.testagent/plan.md` so the exact `HEAD` plan bytes are the unchanged prefix, followed by a separator and the complete current publication-preparation plan.
- Prefix verification: **PASS** (`starts_byte_for_byte_with_HEAD=True`; 183904-byte prefix).
- Command: `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py::test_testagent_plan_update_is_append_only_against_head`
- Result: **PASS** (exit 0; `1 passed in 0.20s`).

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-final-quality-gate -->

## Publication-Preparation Mandatory Final Quality-Gate Remediation

### Result

- Status: **PARTIAL**. Every requested behavioral, pseudo-mutation,
  assertion-quality, prompt-scenario, workflow, plan-prefix, and focused-test
  gate is green. The two mandatory Ruff commands remain blocked only by three
  lines that already existed in
  `tests/contracts/test_buddy_workflows.py` when this append-only remediation
  began; changing them would violate the test-file append-only boundary.
- Initial bounded baseline:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`
  passed **122 tests** in 8.03s.
- No version-control-mutating operation, restoration, full package suite, or
  documentation edit was performed.

### Additive tests and exact collected IDs

- `test_publisher_cancellation_without_workflow_ownership_is_rejected_by_publisher_gate[cancelled-without-workflow-ownership]`
  uses otherwise-legal Observation `failure` / materialization `skipped`
  preparation facts, publisher `cancelled`, workflow cancellation `false`,
  and no lineage. It requires the publisher-specific diagnostic and zero CLI
  invocations.
- `test_workflow_cancellation_fact_recorder_executes_exact_step_shell`
  asserts step ID `workflow-cancellation`, condition `cancelled()`, executes
  the YAML-extracted `run`, and requires exact
  `workflow-cancelled=true\n` output.
- `test_publication_preparation_classifier_executes_missing_legal_combinations`
  adds:
  - `observation-failure__materialization-cancelled`
  - `observation-cancelled__materialization-skipped`
  Both require exactly one preparation flag, no platform-termination flag,
  and no Snapshot/downstream-lineage argv.
- `test_durable_snapshot_reviewer_failure_omits_preparation_diagnostics`
  seeds the job summary, preserves the durable Snapshot/no-interruption
  semantics, and proves no publication-preparation diagnostic is appended.
- `test_incomplete_preparation_diagnostics_report_exact_workflow_facts`
  requires exact retained and job-summary values for successful
  Qualification, failed Observation, skipped materialization, skipped
  publisher, workflow cancellation `false`, and absent durable Snapshot.
- `test_completed_materialization_summary_is_sole_post_upload_writer_and_appends`
  seeds prior summary content, proves exact append behavior and byte identity,
  keeps the artifact URL out of `reviewer-summary.md`, and statically requires
  the post-upload summary/link step to be the sole workflow shell line that
  moves reviewer-summary content into `GITHUB_STEP_SUMMARY`.

These seven collected cases are additive companions to the prior Phase 2/3
tests, preserving every line that existed in the test file at gate start.

### Tests-first production evidence

- The first new-case run produced **6 passed, 1 failed, 71 deselected**.
- The sole failure was
  `test_incomplete_preparation_diagnostics_report_exact_workflow_facts`: the
  exact workflow shell omitted `- Publisher job result: skipped`.
- That failure authorized the only workflow-source change in this gate:
  `Finalize Attempt Outcome` now appends
  `- Publisher job result: ${publish_result}` beside its existing exact
  preparation diagnostics.
- Re-running the same focused selection passed **7 tests**; the complete
  workflow contract file then passed **78 tests**.

### Plan correction and append-only proof

- The uncommitted publication-preparation suffix now names the real
  `test_commit8_records_reject_independent_binding_substitutions` test and all
  nine exact IDs:
  `uncertainty`, `authorization-digest`, `publication-snapshot-digest`,
  `capability-admission-digests`, `capability-group-bundle-digests`,
  `receipt-digests`, `result`, `possibly-mutated`, and `next-action`.
- The nonexistent
  `test_publication_preparation_outcome_rejects_each_forbidden_fact` reference
  is absent from the current plan.
- Byte-prefix verification passed:
  `head_bytes=183904 current_bytes=209375 prefix_exact=True`.
- The repository append-only contract passed:
  `test_testagent_plan_update_is_append_only_against_head` — **1 passed in
  0.22s**.

### Mandatory quality-gate review

- `test-gap-analysis` and `assertion-quality` were invoked after the test
  changes. Their required `test-analysis-extensions` helper was attempted and
  unavailable, so the focused Python/pytest review was completed inline.
- Pseudo-mutation result: **no remaining in-scope behavioral survivor**.
  Distinct executable cases kill removal of publisher cancellation ownership,
  either newly covered classifier conjunction, the recorder ID/condition or
  exact output, accidental preparation diagnostics after a durable Snapshot,
  omission or substitution of any required diagnostic fact, summary overwrite,
  return of the old pre-upload writer, reviewer-payload mutation, and URL
  leakage into the immutable payload.
- Assertion-quality result: the seven added cases have no assertion-free,
  trivial-only, self-referential, or tautological collected test. Assertions
  cover exact equality, strings, collections, negative behavior, process
  status, filesystem/GitHub-output side effects, ordering, and structural
  workflow identity.
- Prompt-scenario result: every numbered remediation request maps to at least
  one exact test/case above. The nine record IDs were also confirmed by
  focused pytest collection.

### Commands and results

| Command | Result |
|---|---|
| `uv build --package three-workflow-delivery-v3` | Passed; sdist and wheel built |
| Required two-file pytest command | **129 passed in 9.16s**, exit 0 |
| Focused two-file collection | **129 collected** |
| `actionlint .github/workflows/workflow-delivery-v3-live-attempt.yml` | Passed, exit 0 |
| Append-only plan test | **1 passed in 0.22s**, exit 0 |
| `uv run --python 3.13 ruff check --force-exclude -- <two test files>` | Blocked: pre-existing `Q003` at `test_buddy_workflows.py:2729` |
| `uv run --python 3.13 ruff format --check --force-exclude -- <two test files>` | Blocked: pre-existing formatting at lines 2728-2730, 2858, and 2889; `test_commit8_contracts.py` is formatted |
| `git diff --check` | Passed after this status append |

### Exact changed paths for this remediation

- `.github/workflows/workflow-delivery-v3-live-attempt.yml`
- `.testagent/plan.md`
- `.testagent/status.md`
- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`

`src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`
was read and validated but not changed by this remediation. No Python
production source changed. The workflow source needed the one diagnostic-line
change proven by the exact red test above.

<!-- END APPEND: 2026-08-19-publication-preparation-final-quality-gate -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-formatting-validation -->

## Publication-preparation formatting validation

- Ruff changed only the appended `test_buddy_workflows.py` lines: it removed
  the Q003 quote escape/collapsed assertion, wrapped the SHA-256 call, and
  joined the dictionary-union argument. Test semantics, names, cases, and
  assertions are unchanged.
- The test-file diff remains one additions-only hunk after HEAD line 1889;
  `test_commit8_contracts.py`, workflow/production files, and other tests were
  not edited.
- Required Ruff check passed; Ruff format check reported both files formatted;
  the required pytest command passed **129 tests in 9.03s**; `git diff
  --check` passed.

<!-- END APPEND: 2026-08-19-publication-preparation-formatting-validation -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-final-validation -->

## Publication-preparation final validation

- Final behavioral quality gate: **PASS — no in-scope gaps**. Independent
  pseudo-mutation, assertion-quality, and prompt-scenario review found no
  remaining findings after the seven remediation cases.
- Final discovery:
  `uv run --python 3.13 pytest --collect-only -q` collected **5,196 tests**.
- Final affected build:
  `uv build --package three-workflow-delivery-v3` passed and produced the sdist
  and wheel.
- Final focused tests after formatting:
  the workflow-contract and record-contract files passed **129 tests**.
- Final full affected-package command:
  `python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13
  --package three-workflow-delivery-v3 pytest -q
  src/public/lib/three-workflow-delivery-v3/tests` completed with **3,068
  passed, 1 failed**. The sole failure,
  `test_installed_nbgv_api_returns_exact_head_and_native_projection`, is an
  external Git LFS quota failure while checking out
  `src/private/app/OxfordDictExtractor/wordlist.tsv.zip`; no generated
  regression test failed.
- Final affected-file hook command:
  `HK_FIX=0 hk check
  .github/workflows/workflow-delivery-v3-live-attempt.yml
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py
  src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py
  .testagent/research.md .testagent/plan.md .testagent/status.md`.
  Actionlint, Ruff, Ruff format, editorconfig, typos, consumer policy, and the
  1,257-test workflow-release control gate passed. The hook command failed only
  because its embedded v3 full-package run encountered the same Git LFS quota
  failure (**3,068 passed, 1 failed**).
- `uv run --python 3.13 pyrefly check` on both changed Python test files passed
  with 0 errors and 1 suppressed warning.
- `git diff --check` passed.
- Remaining documentation reconciliation: the accepted GitHub
  whole-run-cancellation spelling is implemented and regression-locked, but
  `release-delivery-mld.md` and `hcoona-release-smoke-npm-lld.md` still say the
  publisher must be `skipped`. They should later state the narrow exception:
  an unstarted publisher may be reported `cancelled` only when workflow
  cancellation owns the stop and no Snapshot or downstream/mutation lineage
  exists.

<!-- END APPEND: 2026-08-19-publication-preparation-final-validation -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-coordinator-gate -->

## Publication-preparation coordinator completion gate

### Final test-set corrections

- Removed the temporary pytest de-collection of the two pre-existing Python
  truth-table tests. Their stale Python classifiers were replaced with
  execution of the YAML-extracted finalizer shell:
  - `test_publication_preparation_rejects_crossed_interruption_states`
  - `test_release_finalizer_platform_fact_mapping_executes_workflow_shell`
- Folded the two missing admitted Observation/materialization combinations
  into `test_publication_preparation_classifier_executes_workflow_shell`.
- Consolidated duplicate gate-remediation tests into the primary scenario
  tests. The retained tests assert semantic facts and concrete side effects
  without requiring an exact Markdown layout or shell-line formatting.
- No production/workflow behavior changed during this final consolidation.

### Final quality review

- Invoked `test-gap-analysis` and `assertion-quality`. Their requested
  `test-analysis-extensions` helper was attempted but is unavailable in this
  environment, so the Python/pytest classifications were completed inline.
- Pseudo-mutation result: **no remaining in-scope survivor**. Executable cases
  independently kill removal or inversion of workflow-cancellation ownership,
  either partial Snapshot-transport check, every accepted
  Observation/materialization branch, crossed interruption states, every
  downstream-lineage exclusion, and the distinction between preparation
  cancellation and post-Snapshot platform termination.
- Assertion-depth result: **pass**. The generated scenarios assert concrete
  process status, exact CLI flags and values, no-call behavior, diagnostics,
  filesystem bytes, GitHub output/summary side effects, workflow step order,
  transport-output identity, and direct invariant exceptions. There are no
  assertion-free, trivial-only, self-referential, or tautological generated
  tests.
- Prompt-scenario review: every numbered request remains mapped by the
  Requirement Traceability table in `.testagent/plan.md`.

### Final validation

| Command/gate | Result |
|---|---|
| Focused workflow + record tests | **128 passed** |
| Bounded five-file integration | **275 passed** |
| Full affected v3 package | **3,067 passed, 1 failed** |
| Package build | Passed |
| Actionlint | Passed |
| Ruff check and format check | Passed |
| Pyrefly | Passed with 0 errors |
| `git diff --check` | Passed |

The sole full-package failure remains environmental and unrelated:
`test_installed_nbgv_api_returns_exact_head_and_native_projection` cannot
download `src/private/app/OxfordDictExtractor/wordlist.tsv.zip` because the
repository Git LFS budget is exhausted.

### Remaining handoff

- No known production-code work remains.
- Normative documentation reconciliation remains for
  `release-delivery-mld.md` and `hcoona-release-smoke-npm-lld.md`, which still
  state that the publisher must be `skipped`. They should describe the narrow
  accepted GitHub spelling: an unstarted publisher may be `cancelled` only
  when whole-workflow cancellation owns the stop and no Snapshot or downstream
  mutation lineage exists.

<!-- END APPEND: 2026-08-19-publication-preparation-coordinator-gate -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-binding-order-correction -->

## Publication-preparation reviewer binding-order correction

- The completed reviewer summary and artifact link now run after
  `Bind reviewer artifact transport to exact payloads`, not between upload and
  binding.
- This preserves the immutable reviewer payload while ensuring a binding
  failure cannot present an unbound artifact as the completed reviewer
  surface.
- `test_completed_materialization_summary_links_immutable_reviewer_artifact`
  asserts `upload-reviewer < bind < summary`, executes the exact summary shell,
  and proves the reviewer payload bytes remain unchanged.
- The focused workflow and record contract command passes **128 tests** after
  the correction, and `git diff --check` passes.

<!-- END APPEND: 2026-08-19-publication-preparation-binding-order-correction -->

<!-- BEGIN APPEND: 2026-08-19-publication-preparation-reviewed-repair-validation -->

## Publication-preparation reviewed repair validation

- Runtime, regression, and normative wording repairs were committed as
  `91deece4`.
- The final reviewer-summary sequence is
  `upload-reviewer -> bind -> completed summary/link`.
- The narrow workflow and record contract command passes **128 tests**.
- The complete `test_hk_trigger.py` append-only contract passes **62 tests**.
- Ruff check, Ruff format check, Pyrefly, and actionlint pass for the changed
  workflow and Python test files.
- With the repository-required `GIT_LFS_SKIP_SMUDGE=1` environment, the full
  Workflow Delivery v3 package passes **3,068 tests**.
- With the same environment, `hk check --check --no-progress` passes for the
  complete working tree.
- `release-delivery-mld.md` and `hcoona-release-smoke-npm-lld.md` now document
  the narrow cancellation-owned `publisher=cancelled` spelling and the
  post-binding completed-summary boundary.
- `git diff --check` passes.

<!-- END APPEND: 2026-08-19-publication-preparation-reviewed-repair-validation -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-four-accepted-repairs-phase-1-result-4a38b286 -->

## Phase 1 result — Acquisition guards

- **Status**: SUCCESS.
- **Exact test**:
  `test_release_finalizer_prerequisite_actions_are_cancellation_admitting`
  (15 parameter IDs).
- **Red command**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py -k 'release_finalizer_prerequisite_actions_are_cancellation_admitting or unsuccessful_live_qualification_retains_a_publication_free_outcome or release_finalizer_downloads_snapshot_directly_from_materialization'`
  — exit 1; **17 failed, 75 deselected**, exposing all 15 missing guards
  plus both stale existing-condition contracts.
- **Green command**: the same narrow command — exit 0; **17 passed,
  75 deselected**. The complete canonical contract file also passed:
  **92 passed**.
- **Harness discovery**: `uv run --python 3.13 pytest --collect-only -q`
  increased from **5,195** to **5,210** collected tests, exactly **+15**.
- **Files changed by Phase 1**:
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`,
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`,
  and `.testagent/status.md`.
- **Append-only check**:
  `python -c 'from pathlib import Path; import subprocess; paths=(".testagent/research.md",".testagent/plan.md",".testagent/status.md"); assert all(Path(path).read_bytes().startswith(subprocess.check_output(("git","show",f"HEAD:{path}"))) for path in paths)'`
  passed before this EOF-only status append; research and plan were unchanged
  by Phase 1. `git diff --check` also passed.
- **Focused quality review**: exact action and complete-condition equality
  assertions kill step removal/rename, action substitution, omitted
  `always()`, changed conjunction, and artifact-ID guard mutations; no
  assertion-free, trivial, or self-referential rows were found.

<!-- END APPEND: 2026-08-19-wdv3-four-accepted-repairs-phase-1-result-4a38b286 -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-four-accepted-repairs-phase-2-result-4a38b286 -->

## Phase 2 result — Cancellation ownership and qualification-only argv

- **Status**: SUCCESS.
- **Exact cases**:
  `test_cancelled_unsuccessful_qualification_uses_exact_qualification_only_argv[failure]`,
  `[incomplete]`;
  `test_publisher_result_truth_table_executes_workflow_shell[cancelled-without-workflow-ownership]`,
  `[post-snapshot-cancelled]`, `[cancelled-with-forwarded-snapshot]`,
  `[cancelled-with-authorization]`, `[cancelled-with-capability-admission]`,
  `[cancelled-with-mutation-marker]`, `[cancelled-with-result-bundle]`, and
  `[cancelled-with-receipt]`; plus
  `test_publication_preparation_classifier_rejects_invalid_workflow_facts[snapshot-artifact-id-without-upload-digest]`
  and `[snapshot-upload-digest-without-artifact-id]`.
- **Red command**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py -k 'publisher_result_truth_table_executes_workflow_shell or cancelled_unsuccessful_qualification_uses_exact_qualification_only_argv'`
  — exit 1; **15 passed, 2 failed, 77 deselected**; both new rows exposed
  the extra `--platform-terminated` argument.
- **Green commands**: the same targeted command — exit 0; **17 passed,
  77 deselected**. The complete canonical workflow contract command
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
  — exit 0; **94 passed**.
- **Build/discovery**: `uv build --package three-workflow-delivery-v3`
  succeeded; root discovery increased from **5,210** to **5,212**, exactly
  **+2**.
- **Changed by Phase 2**:
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`,
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`,
  and this EOF-only `.testagent/status.md` section. Research, plan, domain
  records, CLI, and Publication Control Closure documentation were untouched.
- **Append-only proof**: the three-file HEAD byte-prefix command passed before
  this uniquely delimited section was added via `apply_patch` at EOF.
- **Focused quality review**: exact full-argv equality, explicit flag absence,
  CLI-call cardinality, diagnostics, and retained post-Snapshot/lineage
  negatives cover the plausible classifier mutations; no generated row is
  assertion-free, trivial, or self-referential.

<!-- END APPEND: 2026-08-19-wdv3-four-accepted-repairs-phase-2-result-4a38b286 -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-four-accepted-repairs-phase-3-result-4a38b286 -->

## Phase 3 result — Typed finalize-live optional transport preflight

- **Status**: SUCCESS. Added **21 collected cases**: 20 omission rows in
  `test_finalize_live_rejects_each_partial_optional_transport_group` and the
  parser contract
  `test_cli_exposes_strict_commit8_live_transport_commands_for_finalize_live_groups`.
- **Exact omission IDs**:
  `publication-snapshot-missing-path`,
  `publication-snapshot-missing-record-digest`,
  `publication-snapshot-missing-artifact-id`,
  `publication-snapshot-missing-artifact-digest`,
  `authorization-missing-path`,
  `authorization-missing-record-digest`,
  `authorization-missing-artifact-id`,
  `authorization-missing-artifact-digest`,
  `capability-decision-missing-path`,
  `capability-decision-missing-record-digest`,
  `capability-decision-missing-artifact-id`,
  `capability-decision-missing-artifact-digest`,
  `capability-group-bundle-missing-path`,
  `capability-group-bundle-missing-record-digest`,
  `capability-group-bundle-missing-artifact-id`,
  `capability-group-bundle-missing-artifact-digest`,
  `receipt-missing-path`, `receipt-missing-record-digest`,
  `receipt-missing-artifact-id`, and
  `receipt-missing-artifact-digest`.
- **Production symbols**: `_UploadedRecordTransport` and
  `_optional_uploaded_record_transport` validate all five groups before
  `_load_attempt_binding`; the narrowed states feed the existing loaders.
  `_add_uploaded_record_arguments(..., explicit_member_options=True)` exposes
  `-path`/`-record-digest` aliases only for these five `finalize-live` groups
  while preserving their legacy option names. No cast or broad catch was
  added.
- **Red command**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py -k 'finalize_live_rejects_each_partial_optional_transport_group or cli_exposes_strict_commit8_live_transport_commands'`
  — exit 1; **21 failed, 9 passed, 75 deselected**, with all 20 rows rejected
  by the absent explicit parser members and the parser contract missing them.
- **Green results**: the same command passed **30 tests, 75 deselected**;
  the complete live Qualification CLI file passed **26 tests**; the complete
  CLI parser/behavior file passed **79 tests**; both direct files together
  passed **105 tests**. `uv build --package three-workflow-delivery-v3`
  succeeded.
- **Harness discovery**:
  `uv run --python 3.13 pytest --collect-only -q` increased from **5,212** to
  **5,233**, exactly **+21**.
- **Optional Qualification Evidence**: unchanged. `_optional_evidence` and its
  parser semantics were not edited, and the complete live Qualification CLI
  regression is green.
- **Changed by Phase 3**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`,
  `src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py`,
  `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`, and this
  EOF-only `.testagent/status.md` section. All Phase 1–2 edits were preserved;
  research and plan were not altered by Phase 3.
- **Depth and state proof**: each omission row asserts exact status, group,
  missing member, all-present/all-absent contract, and absence of both Outcome
  and summary; the parser test asserts every explicit member. Focused
  pseudo-mutation and assertion review found no assertion-free, trivial,
  self-referential, or uncovered planned row. The three-file HEAD byte-prefix
  proof, both test-file HEAD-prefix proofs, and `git diff --check` passed
  before this `apply_patch` EOF append. The prior status prefix was **458,757
  bytes**, SHA-256
  `7f8b0652c67578f1132d03ad279da8f9a90acc767b691f7e2e10ce8817f50c6e`.
- **Next**: stop here; Phase 4 final validation/quality gate has not begun.

<!-- END APPEND: 2026-08-19-wdv3-four-accepted-repairs-phase-3-result-4a38b286 -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-four-repairs-precompletion-correction -->

## Pre-completion quality-gate correction

- Removed the unnecessary Phase 3 CLI aliases and parser test; the 20 direct
  rows now use the established options and assert preflight precedes every
  record load.
- Replaced the interim transport tuple/loader refactor with the typed
  `_validate_optional_uploaded_record_transport` validator, preserving the
  established all-present loaders byte-for-byte.
- Added 11 unsuccessful-Qualification contradiction rows covering workflow
  ownership, Observation/materialization work, Snapshot transport, forwarded
  Snapshot, Authorization, Capability Admission, mutation marker, result
  bundle, and Receipt lineage.
- Focused results after correction: workflow scenarios **43 passed**; optional
  transport matrix **20 passed**; Ruff check/format and `git diff --check`
  passed.
- `test-gap-analysis` and `assertion-quality` were invoked. Their extension
  discovery skill was unavailable, so
  `.agents/skills/test-analysis-extensions/extensions/python.md` was read
  directly. The corrected tests kill condition removal/inversion, each
  all-or-none member omission, preflight reordering, ownership removal, and
  downstream-lineage admission; assertions use exact argv, exact conditions,
  diagnostics, call cardinality, and filesystem non-creation rather than
  trivial or self-referential checks.

<!-- END APPEND: 2026-08-19-wdv3-four-repairs-precompletion-correction -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-four-repairs-final-validation -->

## Four-repair final validation

- Workflow contract: **105 passed**.
- Live Qualification CLI: **26 passed**.
- CLI parser/behavior: **78 passed**.
- Closed transport regression: **5 passed**.
- Bounded four-file run: **214 passed**.
- Append-only/HK artifact contract: **62 passed**.
- Root collection: **5,243 tests collected**.
- Full Workflow Delivery v3 package with `GIT_LFS_SKIP_SMUDGE=1`:
  **3,116 passed**.
- `uv build --package three-workflow-delivery-v3`: source distribution and
  wheel built successfully.
- Ruff check, Ruff format check, Pyrefly (**0 errors**), and actionlint passed.
- All three `.testagent` files retain their exact `HEAD` byte prefixes;
  `git diff --check` passed.
- Final pseudo-mutation review found no remaining in-scope survivor: every
  acquisition condition, failed/incomplete qualification-only branch,
  ownership/Observation/materialization/lineage exclusion, optional group,
  missing member, and preflight-order boundary has a concrete killing
  assertion. Final assertion-depth review found no assertion-free,
  trivial-only, or self-referential generated test.
- No Marketplace action, GitHub API, package publication, commit, or
  Publication Control Closure documentation change was performed.

<!-- END APPEND: 2026-08-19-wdv3-four-repairs-final-validation -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-bounded-test-gap-resolution -->

## Bounded test-only Workflow Delivery v3 gap resolution

- Expanded
  `test_unsuccessful_qualification_cancellation_is_not_clean_with_contradictions`
  to **30 cases**: 15 contradiction scenarios for each of failed and
  incomplete Qualification. This includes **12 independent non-skipped job
  cases** spanning success, failure, and cancellation for Observation and
  materialization under both Qualification results; every row requires exactly
  one `--platform-terminated` and excludes
  `--publication-preparation-interrupted`.
- Added **2 cases** to
  `test_cancelled_unsuccessful_qualification_retains_qualification_record_argv`
  for failed and incomplete Qualification. Both assert the exact contiguous CLI
  transports for four retained Qualification Evidence records and the Release
  Artifact, with neither cancellation semantic flag.
- Expanded
  `test_finalize_live_rejects_each_partial_optional_transport_group` to
  **40 cases**: the 20 readable missing-one cases remain, and 20 new
  `<group>-only-<member>` cases supply exactly one member across all five
  optional groups. Every row asserts the complete preflight diagnostic, no
  Outcome or summary, and no `_load_attempt_binding` call.
- Focused pseudo-mutation and assertion-depth review found no remaining
  in-scope survivor, trivial-only assertion, tautology, or unverified prompt
  scenario.
- Validation passed: workflow contract **126 passed**; live Qualification
  boundary **46 passed**; full affected package **3,157 passed**; package build,
  Ruff check, Ruff format check, Pyrefly (**0 errors**), append-only/HK tests
  (**62 passed**), all three `.testagent` HEAD byte prefixes, and
  `git diff --check`.
- This repair changed only the two requested test files and this EOF-only
  status section; existing research and plan content was not appended or
  rewritten, and no production/workflow behavior was changed.

<!-- END APPEND: 2026-08-19-wdv3-bounded-test-gap-resolution -->
<!-- BEGIN APPEND: 2026-08-19-wdv3-six-final-review-repairs-status -->

## Workflow Delivery v3 six final-review repairs status

**Result:** complete; no commit created and no blocker remains.

### Changed implementation/test/document files

- `.github/workflows/workflow-delivery-v3-live-attempt.yml`
- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py`
- `src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py`
- `docs/wiki/analyses/workflow-delivery/v3/release-delivery-mld.md`
- `docs/wiki/analyses/workflow-delivery/v3/README.md`

The three `.testagent` artifacts contain only this run's EOF append after their
complete `HEAD` prefixes.

### Requirement evidence

- Witness:
  `test_workflow_cancellation_witness_has_exact_job_contract[non-cancelled-witness-skipped-defaults-false]`.
- Mandatory guards:
  `test_release_finalizer_prerequisite_actions_are_cancellation_admitting`
  IDs `attempt-binding`, `qualification-snapshot`, and
  `qualification-decision`.
- Propagation:
  `test_propagation_fails_after_successful_retention` IDs `all-success`,
  `finalizer-status-nonzero`, `finalize-step-failure`, and
  `upload-step-failure`.
- Domain guard:
  `test_unsuccessful_qualification_rejects_each_independent_publication_operand`
  IDs `{failure,incomplete}-{publication-snapshot,authorization,capability-admission-decision,capability-group-result-bundle,receipt,receipt-transport-reference,publication-preparation-interrupted,platform-terminated,capability-may-have-started}`.
- CLI:
  `test_finalize_live_forwards_loaded_downstream_records_transport_and_platform_facts`.
- Documentation:
  exact Publication Control Closure naming/independent retention/cross-binding
  and the current concurrency→validation/PR checkpoint; smoke LLD unchanged.

### Validation

| Gate | Result |
|---|---|
| Six focused workflow/domain/CLI/transport/HK files | 402 passed |
| Full v3 package with `GIT_LFS_SKIP_SMUDGE=1` | 3,179 passed |
| Ruff check / format | Passed; 3 files formatted |
| Pyrefly v3 package | 0 errors |
| actionlint | Passed |
| Markdownlint + Prettier (`HK_FIX=0 hk check`) | Passed |
| Append-only/HK artifact tests | 62 passed within focused run; byte-prefix guard passed |
| `dotnet build dirs.proj --no-incremental` | Passed; 0 warnings/errors |
| `uv build --package three-workflow-delivery-v3` | Passed |
| `git diff --check` | Passed |

Pseudo-mutation found no surviving in-scope operand or forwarding mutation.
Assertion review found no assertion-free, trivial-only, tautological, skipped,
or xfailed changed test. Prompt-scenario mapping is complete. No artifact REST
lookup, package mutation, activation, acceptance probe, sentinel finalization,
or smoke-LLD edit was introduced.

<!-- END APPEND: 2026-08-19-wdv3-six-final-review-repairs-status -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-six-final-review-repairs-one-hot-closure -->

## Final one-hot CLI forwarding closure

- The repeated bounded pseudo-mutation and assertion-quality gates identified
  one real weakness: the CLI forwarding scenario enabled all three platform
  facts together, so swapping or coupling those Boolean sources could survive.
- `test_finalize_live_forwards_loaded_downstream_records_transport_and_platform_facts`
  now runs three one-hot cases:
  `publication-preparation-interrupted`, `platform-terminated`, and
  `capability-may-have-started`.
- Each case asserts the exact forwarded Boolean tuple while retaining the
  existing loaded-record, Receipt transport, canonical Outcome, and summary
  assertions.
- The focused one-hot scenarios pass **3 tests**. Ruff check, Ruff format,
  Pyrefly, and `git diff --check` pass for the changed test.
- Repeated independent bounded test-gap and assertion-quality reviews returned
  no findings after the correction.
- The final Workflow Delivery v3 package passes **3,181 tests** with
  `GIT_LFS_SKIP_SMUDGE=1`.

<!-- END APPEND: 2026-08-19-wdv3-six-final-review-repairs-one-hot-closure -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-two-adjudicated-test-gaps-status -->

## Workflow Delivery v3 two adjudicated test gaps status

**Result:** complete; no production/workflow behavior or package state changed.

### Implemented evidence

- `test_unsuccessful_live_qualification_retains_a_publication_free_outcome`
  now compares every digest-valued `qualification-finalizer` output with its
  exact producer expression from the parsed live workflow.
- `test_incomplete_preparation_retains_diagnostics_before_job_failure` now
  executes the real finalizer shell with five distinct record-digest sentinels,
  five separate upload-digest sentinels, and exact retained-record argv.
- Added parameter row
  `test_publisher_result_truth_table_executes_workflow_shell[whole-run-cancelled-after-successful-observation]`
  for successful Qualification, cancellation witness `true`, Observation
  `success`, materialization `skipped`, publisher `cancelled`, and absent
  Snapshot/downstream lineage. It admits only
  `--publication-preparation-interrupted` among semantic flags.

### Targeted validation

The exact three-node pytest command passed **3 tests in 0.80s**. Ruff check,
Ruff format check, and `git diff --check` also passed. Per the bounded request,
no full package, HK, repository build, acceptance, publication, activation,
sentinel, or package-mutation command ran.

### Pre-completion review

- **Pseudo-mutation:** the exact parsed-workflow map kills any one-line digest
  producer swap; distinct shell sentinels kill role or record/upload swaps; the
  new cancellation row kills a classifier mutation that admits cancelled
  publisher only after skipped Observation. Existing exact flag counts and
  lineage exclusions kill coupled platform/capability forwarding. No remaining
  true positive exists in the two approved gaps.
- **Assertion quality:** the affected tests use concrete deep equality, exact
  argv/value assertions, status/invocation/output side effects, and negative
  flag/lineage assertions. No assertion-free, trivial-only, tautological,
  skipped, or xfailed case was introduced.
- **Prompt scenarios:** both adjudicated gaps map to the exact tests named
  above; no adjacent helper substitutes for the real parsed/executed workflow.

<!-- END APPEND: 2026-08-19-wdv3-two-adjudicated-test-gaps-status -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-final-rereview-two-test-gaps-status -->

## Workflow Delivery v3 final re-review test-gap status

**Result:** complete; no production/workflow behavior, dependency, package
state, sentinel, publication, acceptance, activation, or repository history
changed.

### Implemented evidence

- `test_unsuccessful_live_qualification_retains_a_publication_free_outcome`
  now locks the exact `artifact-id` and `artifact-name` producer expressions
  for the retained build Evidence, project-test Evidence, artifact-contents
  Evidence, install-import Evidence, Qualification Snapshot, Adapter Context,
  Release Artifact, and Qualification Decision transports. The existing exact
  digest-producer map remains intact.
- Added
  `test_successful_observation_cancellation_retains_exact_job_diagnostics`.
  It executes the real Release Finalizer workflow shell for successful
  Qualification, cancellation witness `true`, Observation `success`,
  materialization `skipped`, publisher `cancelled`, and absent
  Snapshot/downstream lineage. It requires exactly the interruption semantic,
  rejects downstream/platform flags, and compares the complete diagnostic
  block in both retained summary surfaces.
- The existing shell harness now exposes only the two generated summary paths;
  its execution behavior is unchanged.

### Targeted validation

| Gate | Result |
|---|---|
| Two affected pytest nodes | 2 passed in 0.55s |
| Ruff check | Passed |
| Ruff format check | Passed; file already formatted |
| All three `.testagent` `HEAD` byte prefixes | Passed |
| Changed-path allowlist | Passed |
| `git diff --check` | Passed |

The parent agent owns the full package/HK gate, so no package-wide, HK,
publication, acceptance, activation, sentinel, or package-mutation command
ran.

### Pre-completion review

- **Pseudo-mutation:** changing
  `release-artifact-artifact-id` to the build Evidence artifact ID now fails
  the exact parsed-workflow producer map. Any selected ID/name producer swap
  also fails. Replacing `${publish_result}` with
  `${materialization_result}` in the publisher diagnostic changes
  `cancelled` to `skipped` and fails exact equality on both summaries; removing
  either write fails the corresponding retained-surface assertion. The
  executable scenario also kills removal/coupling of the interruption flag,
  addition of platform/capability state, or downstream transport forwarding.
  No in-scope mutation survives.
- **Assertion quality:** both affected tests use concrete deep equality,
  exact count/value assertions, negative set membership, and retained file
  side effects. No assertion-free, trivial-only, self-referential,
  tautological, skipped, or xfailed test was introduced.
- **Prompt scenarios:** every enumerated transport is present in the exact map;
  the cancellation scenario directly exercises the named shell and both named
  summary surfaces. `code-testing-extensions` and
  `test-analysis-extensions` were unavailable; existing pytest conventions
  and bounded direct review supplied the required language guidance.

<!-- END APPEND: 2026-08-19-wdv3-final-rereview-two-test-gaps-status -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-buddy-concurrency-phase-1-status -->

## Workflow Delivery v3 Buddy concurrency repair — Phase 1 result

**Status:** SUCCESS. Phase 1 characterized the existing canonical Buddy
Execution Identity and caller equality domain without changing production.
The same-group scenario proves only eligibility for GitHub's documented
coalescing semantics; it does not model ordering, fairness, or hosted
scheduler behavior.

### Files

- Modified
  `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`
  by appending 3 tests.
- Modified `.testagent/status.md` by appending this result section.
- Read but did not modify `release/identity.py`, `records/release.py`, and
  `canonical.py`.

### Exact Phase 1 tests

- Retained
  `test_buddy_request_normalization_and_execution_derivation_are_strict`.
- Added
  `test_buddy_execution_identity_document_and_concurrency_key_are_exact`.
- Added
  `test_three_same_target_dispatches_share_one_caller_group_for_github_coalescing`.
- Added
  `test_different_buddy_targets_derive_different_execution_concurrency_keys`.

The new scenarios pin the exact four-member document, literal prefixed and
unprefixed SHA-256 values, exact caller groups, three distinct request/run
contexts sharing one group, and different target SHAs producing distinct
documents, digests, keys, and groups. Exact document membership excludes
request ID, workflow run ID, run attempt, version, package coordinate,
destination adapter, and destination projection.

### Commands and results

1. Baseline harness discovery:

   ```text
   uv run --python 3.13 pytest --collect-only -q
   ```

   Result: exit 0; 5310 tests collected in 0.86s.

2. Narrow syntax build:

   ```text
   uv run --python 3.13 --package three-workflow-delivery-v3 python -m py_compile src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py
   ```

   Result: exit 0; no output.

3. Exact Phase 1 increment:

   ```text
   uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py::test_buddy_request_normalization_and_execution_derivation_are_strict src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py::test_buddy_execution_identity_document_and_concurrency_key_are_exact src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py::test_three_same_target_dispatches_share_one_caller_group_for_github_coalescing src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py::test_different_buddy_targets_derive_different_execution_concurrency_keys
   ```

   Result: exit 0; 4 passed, 0 failed in 0.07s.

4. Post-change harness discovery:

   ```text
   uv run --python 3.13 pytest --collect-only -q
   ```

   Result: exit 0; 5313 tests collected in 0.91s; discovery delta +3, matching
   the 3 added tests.

5. Scoped whitespace validation:

   ```text
   git --no-pager diff --check -- src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py
   ```

   Result: exit 0; no output.

### Pre-completion review and blockers

- Pseudo-mutation review found no in-scope gap: exact document equality kills
  member/schema/value changes; committed digest/key literals kill canonical
  hash or prefix changes; same-target equality kills request/run salts; and
  different-target exact inequalities kill a dropped or fixed target.
- Assertion-quality review found 18 meaningful assertions across the 3 new
  tests, with exact/deep equality, structural exclusion, collection equality,
  and distinctness checks. There are no assertion-free, trivial-only,
  self-referential, skipped, or xfailed tests.
- Blockers: none.
- No later phase, production source, workflow, package manifest/lock,
  acceptance probe, sentinel, publication, full-package test, or HK command
  was implemented or run.

<!-- END APPEND: 2026-08-19-wdv3-buddy-concurrency-phase-1-status -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-buddy-concurrency-phase-2-status -->

## Workflow Delivery v3 Buddy concurrency repair — Phase 2 result

**Status:** SUCCESS. The real request-local `compile-live-model` path now
emits the canonical Buddy Execution concurrency key only after successful
Repository Model compilation. Phase 3 workflow forwarding was not implemented.

### Files

- Modified production:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`.
- Appended 2 tests to:
  `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`.
- Appended this bounded result to `.testagent/status.md`.
- Preserved all prior Phase 1, research, plan, test, and working-tree changes.

### Exact production behavior

After `_release_compile_live_model_command` has loaded the admitted live Intent,
admitted the uploaded Provider result, compiled and admitted the request-local
Repository Model, and written that model, its existing GitHub output emission
now appends:

```python
canonical_sha256(
    derive_buddy_execution_identity(intent).to_document()
).removeprefix("sha256:")
```

as `execution-concurrency-key`. The command reuses the existing admitted
`intent`, identity derivation, and canonical hash. It does not rerun the
Provider, reproduce identity fields, add an abstraction, or include model,
version, package, destination, request, or run facts. Existing
`repository-model-digest` and `repository-model-digest-hex` outputs remain in
their original order. Compilation failure reaches no GitHub-output emission,
so no key is written.

### Exact Phase 2 tests

- `test_compile_live_model_emits_canonical_buddy_execution_concurrency_key`
  runs the real parser, uploaded-Intent admission, uploaded Provider admission,
  and request-local compiler against the existing canonical temporary
  repository and Provider fixture pattern. It pins the Phase 1 literal
  `a71c896702fc7f6869d6dc6714840eba7393c9e98eaf820d3254299d664534a6`,
  exact Repository Model output lines, absence of the `sha256:` prefix, exact
  compiled NBGV/version facts, exact Buddy package/destination projection, a
  ready admitted Snapshot, no stdout/stderr, and no Provider rerun.
- `test_compile_live_model_does_not_emit_execution_concurrency_key_when_compilation_fails`
  drives the same real command through established malformed target Quality
  authoring. It pins exit 1, the existing diagnostic, no model artifact, and no
  GitHub output/key artifact.

### Commands and results

1. Baseline harness discovery:

   ```text
   uv run --python 3.13 pytest --collect-only -q
   ```

   Result: exit 0; 5313 tests collected in 0.91s.

2. Test-first Phase 2 nodes:

   ```text
   uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py::test_compile_live_model_emits_canonical_buddy_execution_concurrency_key src/public/lib/three-workflow-delivery-v3/tests/test_cli.py::test_compile_live_model_does_not_emit_execution_concurrency_key_when_compilation_fails
   ```

   Initial result: exit 1; 1 failed and 1 passed in 0.67s. The first draft
   incorrectly expected a serialized `snapshot-digest` member; that test-only
   assertion was corrected to the existing canonical Snapshot contract.
   Confirmed red result before production editing: exit 1; 1 failed and 1
   passed in 0.67s, solely because `execution-concurrency-key` was absent.

3. Syntax build:

   ```text
   uv run --python 3.13 --package three-workflow-delivery-v3 python -m py_compile src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py
   ```

   Result: exit 0; no output. A preceding delegated invocation accidentally
   included `.` as a third compile target and exited 1 with
   `[Errno 21] Is a directory: '.'`; the corrected exact command above passed.

4. Post-edit Phase 2 nodes:

   ```text
   uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py::test_compile_live_model_emits_canonical_buddy_execution_concurrency_key src/public/lib/three-workflow-delivery-v3/tests/test_cli.py::test_compile_live_model_does_not_emit_execution_concurrency_key_when_compilation_fails
   ```

   First result: exit 1; 1 failed and 1 passed in 0.61s, exposing that the
   initial in-scope edit had matched the adjacent simulation output block.
   The block was removed there and placed only in
   `_release_compile_live_model_command`. Final result: exit 0; 2 passed and 0
   failed in 0.58s.

5. Final syntax build:

   ```text
   uv run --python 3.13 --package three-workflow-delivery-v3 python -m py_compile src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py
   ```

   Result: exit 0; no output.

6. Full canonical CLI regression:

   ```text
   uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py
   ```

   Result: exit 0; 80 passed and 0 failed in 7.00s.

7. Post-change harness discovery:

   ```text
   uv run --python 3.13 pytest --collect-only -q
   ```

   Result: exit 0; 5315 tests collected in 0.82s; discovery delta +2, matching
   the 2 appended tests.

8. Scoped whitespace validation:

   ```text
   git --no-pager diff --check -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py .testagent/status.md
   ```

   Result: exit 0; no output before this append.

### Pre-completion review and blockers

- Pseudo-mutation review found no in-scope gap. Exact output-list equality
  kills omitted, reordered, prefixed, or model-derived keys and changes to
  existing Repository Model outputs. Exact model facts kill degenerate fixture
  behavior. The malformed-authoring scenario kills emission moved before
  successful compilation. The Provider-rerun guard kills a second Provider
  invocation.
- Assertion-quality review counted 13 meaningful assertions across the 2
  tests, covering exact/deep equality, Boolean readiness, diagnostics,
  negative prefix/artifact assertions, and file side effects. There are no
  assertion-free, trivial-only, self-referential, skipped, or xfailed tests.
- Blockers: none.
- No workflow Phase 3, package/lock mutation, full-package/HK gate,
  acceptance probe, publication, sentinel finalization, commit, push, or PR
  operation was implemented or run.

<!-- END APPEND: 2026-08-19-wdv3-buddy-concurrency-phase-2-status -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-buddy-concurrency-phase-3-status -->

## Phase 3 — Exact caller forwarding and reusable-job concurrency

PHASE: 3
STATUS: SUCCESS
TESTS_CREATED: 0 (1 existing canonical scenario strengthened append-only)
TESTS_PASSING: 1
HARNESS_DISCOVERY: Not run; repository-wide discovery belongs to Phase 4

### Changed files

- `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`
  - Removed the request-specific
    `request-id:GITHUB_SHA:buddy` `printf | sha256sum` computation and its
    `execution-concurrency-key` shell emission.
  - The unchanged `compile-model` job output now consumes the real
    `${{ steps.compile.outputs.execution-concurrency-key }}` emitted by
    `release compile-live-model --github-output`.
  - The unchanged eligibility output forwards exactly
    `${{ needs.compile-model.outputs.execution-concurrency-key }}`.
  - The unchanged reusable caller owns exact group
    `wdv3-execution-${{ needs.evaluate-live-eligibility.outputs.execution-concurrency-key }}`
    with YAML Boolean `cancel-in-progress: false`.
- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
  - Appended focused assertions to
    `test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact`.
- `.testagent/status.md`
  - Appended this bounded Phase 3 result without rewriting prior content.

### Exact canonical test evidence

`test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact` now pins:

- the exact five-job pre-Attempt DAG and the absence of concurrency on
  request normalization, Provider discovery, request-local Repository Model
  compilation, and live eligibility;
- the real `compile-live-model --github-output` producer step and exact
  `steps.compile.outputs.execution-concurrency-key` job output;
- exact eligibility forwarding and the exact final caller group expression;
- YAML Boolean false cancellation and the unchanged live-eligibility gate;
- the reusable `uses`-only boundary with neither `runs-on` nor `steps`;
- absence of request-ID forwarding, `printf`, any shell key assignment or
  emission, and any request/run/SHA-bearing key hash; the sole remaining
  `sha256sum` line is exactly the Repository Model artifact digest.

`test_buddy_request_normalization_and_execution_derivation_are_strict` remains
unchanged in its canonical test file. It was not rerun because the Phase 3
increment is the caller-workflow contract node; prior-phase regression reruns
belong to Phase 4.

### Commands and results

1. Test-first Phase 3 node:

   ```text
   uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py::test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact
   ```

   Initial result: exit 1; 1 failed in 0.32s. The strengthened assertion
   detected the old request-ID/GitHub-SHA/Buddy shell hash.

2. Scoped syntax build:

   ```text
   uv run --python 3.13 --package three-workflow-delivery-v3 python -m py_compile src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py
   ```

   Result: exit 0; no output.

3. Post-edit Phase 3 node:

   ```text
   uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py::test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact
   ```

   Result: exit 0; 1 passed in 0.07s.

4. Caller-only workflow lint required by Phase 3:

   ```text
   python eng/scripts/hk_actionlint.py .github/workflows/workflow-delivery-v3-buddy-smoke.yml
   ```

   Result: exit 0; 1/1 workflow finished successfully in 1.0s.

5. Scoped whitespace and diff review:

   ```text
   git --no-pager diff --check -- .github/workflows/workflow-delivery-v3-buddy-smoke.yml src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py
   git --no-pager diff -- .github/workflows/workflow-delivery-v3-buddy-smoke.yml src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py
   ```

   Result: exit 0; no whitespace errors. The reviewed workflow diff removes
   only the two request-specific key lines, and the test diff only appends
   focused assertions to the named scenario.

### Bounded quality review and exclusions

- Pseudo-mutation review found no in-scope gap: changing either forwarding
  expression, the group, cancellation, gate, DAG ownership, reusable boundary,
  producer command/output, or reintroducing shell key construction is killed
  by a concrete assertion.
- Assertion-quality review found meaningful equality/deep-structure, Boolean,
  string-presence, and negative-absence checks with no trivial,
  self-referential, assertion-free, skipped, or xfailed coverage.
- The reusable workflow and all unrelated files were unchanged. No
  ledger/lock/tag/abstraction, package mutation, full package/HK, harness-wide
  discovery, acceptance probe, sentinel finalization, publication, live
  activation, commit, push, or PR operation was performed. Phase 4 was not
  performed.
- Issues: none.

<!-- END APPEND: 2026-08-19-wdv3-buddy-concurrency-phase-3-status -->

<!-- BEGIN APPEND: 2026-08-19T203357Z-wdv3-buddy-concurrency-phase-4-status -->

## Phase 4 — Bounded validation and append-only evidence

Timestamp: `2026-08-19T20:33:57Z`

PHASE: 4
STATUS: SUCCESS
TESTS_CREATED: 0
TESTS_PASSING: 312 unique tests in the bounded four-file regression; 319
command-level passes when the 7 independently invoked Phase 1-3 nodes are
included.
HARNESS_DISCOVERY: Not run by explicit scope; no full-package or
harness-wide command was permitted for this parent-bounded validation.

### Current touched files

- `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`
- `.testagent/plan.md`
- `.testagent/research.md`
- `.testagent/status.md`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`
- `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`

No new path was added or removed. The reusable workflow, identity/record/
canonical sources, canonical tests, manifests, locks, packages, and unrelated
worktree changes remain untouched.

### Exact commands and results

| Command | Result |
|---|---|
| `cp .testagent/research.md /tmp/wdv3-buddy-concurrency-implementation-research-prefix.md` | Exit 0; captured the complete pre-Phase-4 research prefix. |
| `cp .testagent/plan.md /tmp/wdv3-buddy-concurrency-implementation-plan-prefix.md` | Exit 0; captured the complete pre-Phase-4 plan prefix. |
| `cp .testagent/status.md /tmp/wdv3-buddy-concurrency-implementation-status-prefix.md` | Exit 0; captured the complete pre-Phase-4 status prefix before this append. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py::test_buddy_request_normalization_and_execution_derivation_are_strict src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py::test_buddy_execution_identity_document_and_concurrency_key_are_exact src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py::test_three_same_target_dispatches_share_one_caller_group_for_github_coalescing src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py::test_different_buddy_targets_derive_different_execution_concurrency_keys` | Fresh result before lint repair: exit 0, `4 passed in 0.04s`. Final result after the repair: exit 0, `4 passed in 0.07s`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py::test_compile_live_model_emits_canonical_buddy_execution_concurrency_key src/public/lib/three-workflow-delivery-v3/tests/test_cli.py::test_compile_live_model_does_not_emit_execution_concurrency_key_when_compilation_fails` | Fresh result before lint repair: exit 0, `2 passed in 0.57s`. Final result after the repair: exit 0, `2 passed in 0.67s`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py::test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact` | Fresh result before lint repair: exit 0, `1 passed in 0.07s`. Final result after the repair: exit 0, `1 passed in 0.27s`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py` | Fresh result before lint repair: exit 0, `312 passed in 23.82s`. Final result after the repair: exit 0, `312 passed in 23.83s`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 python -m py_compile src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | Exit 0 with no output before the repair, after the repair, and in the final pass. |
| `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | Initial exit 1: 10 repair-owned findings (`E501`, `C401`, `PLR2004`, and `D103`). After the smallest test-only formatting/lint repair, a second exit 1 exposed one unused `E501` `noqa`; it was removed. Final exit 0: `All checks passed!`. |
| `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | Initial exit 1: two repair-owned test files would be reformatted. After the smallest test-only formatting repair, exit 0: `4 files already formatted`; the final pass also exited 0 with the same result. |
| `python eng/scripts/hk_actionlint.py .github/workflows/workflow-delivery-v3-buddy-smoke.yml` | Exit 0; `1/1 workflow finished successfully in 1.0s`. No other workflow was linted. |

The repair-owned Ruff failures changed no production behavior. The minimal
correction formatted only the touched test additions, added two test
docstrings/one necessary long-name suppression, and replaced a generator/
magic-count distinctness check with an exact target set plus fixture-sized
distinctness assertions. All affected node and four-file tests were rerun.

### Live-disabled and hosted-boundary evidence

- `test_buddy_workflow_files_are_the_disabled_commit8_pair_only` passed in the
  312-test bounded regression and retains the checked-in live-disabled
  workflow contract.
- `test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact` passed
  both independently and in the bounded regression. It pins the unchanged
  eligibility `if` gate, the caller-held reusable boundary, exact domain-key
  forwarding, and `cancel-in-progress: false`.
- No live probe, workflow dispatch, publication, acceptance probe, or timing
  assertion was used. Hosted GitHub pending-run replacement order/fairness
  remains the sole evidence boundary, not a local repair blocker.

### Requirement-to-evidence map

| # | Evidence |
|---:|---|
| 1 | `test_buddy_execution_identity_document_and_concurrency_key_are_exact` passed. |
| 2 | `test_three_same_target_dispatches_share_one_caller_group_for_github_coalescing` and the exact caller DAG test passed; hosted replacement timing/order remains intentionally unclaimed. |
| 3 | The exact caller DAG test passed and pins concurrency before the reusable Attempt; hosted pending replacement is not locally emulated. |
| 4 | `test_different_buddy_targets_derive_different_execution_concurrency_keys` passed. |
| 5 | Exact identity, same-target, CLI-output, and workflow-shell-negative scenarios passed. |
| 6 | `test_compile_live_model_does_not_emit_execution_concurrency_key_when_compilation_fails` and the exact unchanged caller eligibility gate passed. |
| 7 | The exact caller DAG test pins `run-live-attempt` as a `uses`-only whole-Attempt job. |
| 8 | The exact caller DAG test pins YAML Boolean `cancel-in-progress: false`. |
| 9 | Exact literal document/digest and real CLI key scenarios passed. |
| 10 | `test_compile_live_model_emits_canonical_buddy_execution_concurrency_key` passed. |
| 11 | The exact caller DAG test pins the producer, both forwarding hops, final group, and shell negatives. |
| 12 | The bounded touched-file inventory and diff contain no added ledger, lock, tag, service, credential, destination lock, or general abstraction. |
| 13 | The 312-test four-file regression, live-disabled contract, actionlint, Ruff, and compile checks passed; the reusable workflow stayed untouched. |
| 14 | All 5 added tests and the 1 strengthened workflow scenario passed; canonical helper/source files stayed untouched. |
| 15 | The recorded research/plan/status prefixes and historical research hash are checked below; only this uniquely delimited status append was added in Phase 4. |

### Exclusions and blockers

- Intentionally not run: full package, package build/mutation, full HK,
  harness-wide discovery, acceptance probes, live workflows/publication,
  sentinel finalization, coverage, installs, Pyrefly outside the exact
  user-selected Phase 4 command set, commit, push, or PR operations.
- Local blockers: none.
- Evidence boundary: GitHub-hosted same-group pending replacement
  timing/order/fairness cannot be proven locally.

### Terminal append-only and diff outcomes

| Command | Result |
|---|---|
| `python -c 'from pathlib import Path; prefix=Path("/tmp/wdv3-buddy-concurrency-implementation-research-prefix.md").read_bytes(); current=Path(".testagent/research.md").read_bytes(); assert current.startswith(prefix), "research.md prefix changed"'` | Exit 0; the complete pre-Phase-4 research capture remains a byte-identical prefix. Captured SHA-256: `e68b42e6551f1958b98fb189cdb6af811b55ec4c934eea839764f306007043f7`. |
| `python -c 'from pathlib import Path; prefix=Path("/tmp/wdv3-buddy-concurrency-implementation-plan-prefix.md").read_bytes(); current=Path(".testagent/plan.md").read_bytes(); assert current.startswith(prefix), "plan.md prefix changed"'` | Exit 0; the complete pre-Phase-4 plan capture remains a byte-identical prefix. Captured SHA-256: `5a0a5c9d040ef0ce581605473bcfb20d54c3889c6bfbd131a0367b8a4f9a555f`. |
| `python -c 'from pathlib import Path; prefix=Path("/tmp/wdv3-buddy-concurrency-implementation-status-prefix.md").read_bytes(); current=Path(".testagent/status.md").read_bytes(); assert current.startswith(prefix), "status.md prefix changed"'` | Exit 0; the complete pre-Phase-4 status capture remains a byte-identical prefix. Captured SHA-256: `0d372b2a9640aa7a79e798f027e93c196313c1dec927086f2bcfba0fc84ad9a3`. |
| `python -c 'from pathlib import Path; import hashlib; prefix=Path(".testagent/research.md").read_bytes()[:247073]; assert len(prefix)==247073 and hashlib.sha256(prefix).hexdigest()=="64ab82657e5865817d91df5db3b3f5be6899f4aa05fe7496b9b5ef83cab7e5c2"'` | Exit 0; the recorded 247,073-byte historical research prefix hash remains exact. |
| `python -c 'from pathlib import Path; prefix=Path("/tmp/wdv3-buddy-concurrency-research-prefix.md").read_bytes(); current=Path(".testagent/research.md").read_bytes(); assert current.startswith(prefix), "research.md prefix changed"'` | Exit 0; the research-phase capture is also an exact current prefix. Its SHA-256 equals the implementation capture: `e68b42e6551f1958b98fb189cdb6af811b55ec4c934eea839764f306007043f7`. |
| `python -c 'from pathlib import Path; import subprocess; paths=(".testagent/research.md",".testagent/plan.md",".testagent/status.md"); assert all(Path(path).read_bytes().startswith(subprocess.check_output(("git","show",f"HEAD:{path}"))) for path in paths)'` | Exit 0; all three artifacts retain their exact recorded `HEAD` bytes as a prefix. |
| `git --no-pager diff --check -- .github/workflows/workflow-delivery-v3-buddy-smoke.yml src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/identity.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/canonical.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py .testagent/research.md .testagent/plan.md .testagent/status.md` | Exit 0 with no output; the complete bounded diff has no whitespace errors. |

No test-gap-analysis, assertion-quality, full-package, full-HK, acceptance,
live, publication, coverage, install, package mutation, or repository
publication operation was performed.

<!-- END APPEND: 2026-08-19T203357Z-wdv3-buddy-concurrency-phase-4-status -->

<!-- BEGIN APPEND: 2026-08-19T203803Z-wdv3-buddy-step-7-8-pseudo-mutation-gap-repair-status -->

## Step 7/8 pseudo-mutation gap repair

PHASE: Step 7/8 bounded follow-up
STATUS: SUCCESS
TESTS_CREATED: 1
TESTS_PASSING: 81 in the full touched `test_cli.py`
BLOCKER: None

### Exact changed files

- `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
- `.testagent/status.md` (this append only)

No production, workflow, other test, package, manifest, or lock file was
changed.

### Survived mutation and repair evidence

The prior real CLI integration scenario used only target `"a" * 40`.
Consequently, a mutation replacing
`derive_buddy_execution_identity(intent)` in the real
`compile-live-model` producer with a constant identity/key for that single
fixture could survive even though helper-level different-target coverage
passed.

The new exact scenario
`test_compile_live_model_execution_concurrency_key_changes_with_target` runs
the real CLI twice in isolated temporary subdirectories for immutable targets
`"a" * 40` and `"b" * 40`. It pins the unprefixed keys to:

- `a71c896702fc7f6869d6dc6714840eba7393c9e98eaf820d3254299d664534a6`;
- `9eeac4fd6533b5afb39ebb70ed223833578e268b6d9b0bd46111687465778bd6`.

It also asserts key inequality, both zero results, both model and GitHub-output
files, exact Repository Model digest output lines, exact Snapshot context and
NBGV target SHAs, and empty stdout/stderr. Fixed expected values are used; the
test does not call or reproduce the production identity derivation. The
existing success and failure scenarios remain intact, while their shared
runner now accepts an explicit target with the original `"a" * 40` default.

Pseudo-mutation review now classifies the constant-identity/key substitution as
killed: either constant value necessarily disagrees with one of the two fixed
CLI output literals. Prefix insertion, target omission, output omission or
reordering, failed result, wrong compiled target, and missing output creation
are independently killed by exact assertions. No in-scope mutation remains.

Assertion-quality review found 13 runtime assertions in the new two-target
scenario, spanning exact/deep equality, negative inequality, file side
effects, result state, compiled target structure, and stdout/stderr absence.
There are no assertion-free, trivial-only, tautological, skipped, xfailed, or
timing-dependent checks. `test-analysis-extensions` was unavailable, so the
already-loaded Python/pytest guidance was applied directly.

### Exact commands and results

| Command | Result |
|---|---|
| `cp .testagent/status.md /tmp/wdv3-buddy-pseudo-mutation-status-prefix-20260819T203803Z.md && python -c 'from pathlib import Path; import hashlib; data=Path(".testagent/status.md").read_bytes(); print(len(data), hashlib.sha256(data).hexdigest())'` | Exit 0; captured the complete pre-repair status prefix: 505824 bytes, SHA-256 `066856c5f0ed1df449ca2de40cf3e729ed96bff22f58e172b2bccf6c13aa33f5`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py::test_compile_live_model_execution_concurrency_key_changes_with_target` | Exit 0; `1 passed in 0.63s`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py::test_compile_live_model_emits_canonical_buddy_execution_concurrency_key src/public/lib/three-workflow-delivery-v3/tests/test_cli.py::test_compile_live_model_does_not_emit_execution_concurrency_key_when_compilation_fails src/public/lib/three-workflow-delivery-v3/tests/test_cli.py::test_compile_live_model_execution_concurrency_key_changes_with_target` | Exit 0; `3 passed in 1.09s`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | Exit 0; `81 passed in 7.82s`. |
| `uv run --python 3.13 --package three-workflow-delivery-v3 python -m py_compile src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | Exit 0; no output. |
| `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | Exit 0; `All checks passed!`. |
| `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | Initial exit 1; the new test needed formatting: `1 file would be reformatted`. |
| `uv run --python 3.13 ruff format --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | Exit 0; formatted only the touched test file. |
| `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/test_cli.py && uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | Exit 0; `All checks passed!` and `1 file already formatted`. |
| `git --no-pager diff --check -- src/public/lib/three-workflow-delivery-v3/tests/test_cli.py .testagent/status.md` | Pre-append exit 0; no output. |

No full package, package build, HK, actionlint, acceptance, live probe,
network, timing, install, package mutation, or VCS mutation command was run.

### Terminal append-prefix integrity

| Command | Result |
|---|---|
| `python -c 'from pathlib import Path; import hashlib; prefix=Path("/tmp/wdv3-buddy-pseudo-mutation-status-prefix-20260819T203803Z.md").read_bytes(); current=Path(".testagent/status.md").read_bytes(); assert current.startswith(prefix), "status.md prefix changed"; print(len(prefix), hashlib.sha256(prefix).hexdigest(), len(current))'` | Exit 0; the exact 505824-byte captured prefix remains unchanged with SHA-256 `066856c5f0ed1df449ca2de40cf3e729ed96bff22f58e172b2bccf6c13aa33f5`; current length at this check was 511046 bytes. |
| `git --no-pager diff --check -- src/public/lib/three-workflow-delivery-v3/tests/test_cli.py .testagent/status.md` | Exit 0 after the main status append; no output. |
| `git --no-pager status --short -- src/public/lib/three-workflow-delivery-v3/tests/test_cli.py .testagent/status.md` | Exit 0; exactly the two authorized bounded paths are modified. |

<!-- END APPEND: 2026-08-19T203803Z-wdv3-buddy-step-7-8-pseudo-mutation-gap-repair-status -->

<!-- BEGIN APPEND: 2026-08-19T204500Z-wdv3-buddy-final-test-review-status -->

## Final bounded test-gap and assertion-quality review

STATUS: SUCCESS
SCOPE: Six added scenarios and one strengthened workflow contract
BLOCKERS: None locally

The `test-analysis-extensions` skill entry point was unavailable. Its checked-in
Python/pytest base extension was read directly before classification.

### Pseudo-mutation result

The first bounded review found one true positive: the real CLI path had only
one target fixture, so a constant key matching that fixture could survive.
`test_compile_live_model_execution_concurrency_key_changes_with_target` repaired
the gap with two real CLI compilations and fixed expected digests.

The final review found no remaining in-scope survived mutation or no-coverage
zone. The final tests kill removal/renaming of the GitHub output, a retained
`sha256:` prefix, constant or target-insensitive keys, inclusion of request/run
facts, output before failed compilation, caller-side shell recomputation,
incorrect forwarding hops, a changed group prefix/source, cancellation of the
running Attempt, and movement of concurrency away from `run-live-attempt`.

### Assertion-quality result

- Assertion-free tests: 0.
- Trivial-only tests: 0.
- Tautological/self-referential tests: 0.
- Skipped/xfail tests: 0.
- Meaningful categories present: exact/deep equality, Boolean state, strings,
  collections, negative assertions, file/output side effects, and structural
  document checks.
- The tests pin concrete canonical documents and SHA-256 values and also verify
  secondary observables: Repository Model content, Provider non-rerun,
  output-file presence/absence, diagnostics, DAG ownership, eligibility order,
  and `cancel-in-progress: false`.

### Prompt-scenario coverage

Every requested scenario maps to an exact test: same-target request/run
variation, different targets, canonical identity exclusions, real successful
and failed CLI output, target-sensitive real CLI output, exact workflow
forwarding/no shell key, caller-held reusable-job concurrency, and preserved
live-disabled behavior. Hosted GitHub pending-run replacement timing/fairness
is intentionally not claimed beyond documented same-group coalescing semantics.

### Final validation

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py` | Exit 0; `313 passed in 25.11s`. |
| `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | Exit 0; all checks passed. |
| `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | Exit 0; four files already formatted. |
| `python eng/scripts/hk_actionlint.py .github/workflows/workflow-delivery-v3-buddy-smoke.yml` | Exit 0; `1/1` workflow passed. |
| `git --no-pager diff --check` | Exit 0; no output. |

The exact 512,058-byte pre-review `.testagent/status.md` content had SHA-256
`748cb1f2b77eb3eaf5446e12f3e80e121978d2448668d455662832bd61c12439`
and is retained as the prefix of this append.

No full package/HK, acceptance probe, live publication, sentinel finalization,
package mutation, commit, push, or PR operation was performed.

<!-- END APPEND: 2026-08-19T204500Z-wdv3-buddy-final-test-review-status -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-buddy-concurrency-review-corrections -->

## Buddy concurrency review adjudication corrections

PHASE: Post-implementation review adjudication
STATUS: SUCCESS
TESTS_PASSING: 313 in the bounded concurrency regression
BLOCKER: None

### Evidence clarification

The earlier Phase 3 phrase "absence of request-ID forwarding" was imprecise.
Request ID remains intentionally transported through the caller jobs and into
the reusable live Attempt as required current-Attempt identity. The repair
removes request ID only from caller-side Release Execution concurrency-key
derivation and shell computation.

### Test-scope correction

Independent review correctly identified two groups of unrelated assertions:

- exact request-ID hashes and exact actor/ref/run tuples in the same-target
  concurrency scenario; and
- full NBGV and destination-projection serialization in the successful
  `compile-live-model` concurrency-output scenario.

The same-target scenario now proves only that every intentionally excluded
request fact is distinct while all canonical Buddy Execution identities and
groups remain equal. The real CLI scenario still proves successful admitted
compilation and exact concurrency output without pinning unrelated Repository
Model contracts that have dedicated coverage elsewhere.

### Independent finding disposition

- Request-ID evidence wording: true positive; corrected by this append.
- Unrelated test over-pinning: true positive; corrected in the two affected
  tests.
- Immediate README/handoff update: false positive at the uncommitted review
  stage. Documentation must advance after the repair is review-clean and
  committed.
- Additional workflow regex validation against arbitrary same-revision shell
  corruption: false positive. The trusted CLI is the sole producer, canonical
  SHA-256 emits exactly 64 lowercase hexadecimal characters, and real CLI
  success, target sensitivity, and failure ordering are already covered.

### Validation

| Command | Result |
|---|---|
| `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py` | Exit 0; `313 passed in 25.30s`. |
| `uv run --python 3.13 ruff check --force-exclude -- <four touched Python files>` | Exit 0; all checks passed. |
| `uv run --python 3.13 ruff format --check --force-exclude -- <four touched Python files>` | Exit 0; four files already formatted. |
| `git --no-pager diff --check` | Exit 0; no output. |

No production behavior changed during review repair. No acceptance probe, live
publication, sentinel finalization, package mutation, commit, push, or PR
operation was performed.

<!-- END APPEND: 2026-08-19-wdv3-buddy-concurrency-review-corrections -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-final-pr-preparation-status -->

## Workflow Delivery v3 final PR preparation

PHASE: Final validation and local PR preparation
STATUS: SUCCESS
WORKFLOW_RELEASE_TESTS_PASSING: 1257
V3_TESTS_PASSING: 3189
COMMITTED_RANGE_FILES: 574
BLOCKER: Push and PR creation require explicit user authorization

### Durable closure

- Merge commit `e4dfea3d` integrates `origin/main` at `3cc079ee` without
  rewriting history.
- Conflict resolution preserves the branch's deliberate legacy CI-job
  retirement, regenerates the root and standalone PNPM locks plus `uv.lock`,
  and retains the standalone Hexo `hexo@<7.2.0` override.
- Merge review findings were independently adjudicated. The true positives
  were repaired, and all four merge re-reviewers reported no findings.
- The merged UV lock exposed Ruff 0.16 diagnostics across otherwise unchanged
  branch files. Commit `f3eb3b81` explicitly pins the previously validated Ruff
  0.14.4 baseline until a separately scoped Ruff 0.16 migration. All three
  tooling re-reviewers reported no findings.

### Final validation

| Command | Result |
|---|---|
| `GIT_LFS_SKIP_SMUDGE=1 mise exec -- hk check --check --no-progress` | Exit 0 on the final merge workspace. |
| `GIT_LFS_SKIP_SMUDGE=1 mise exec -- hk check --check --no-progress --from-ref origin/main --to-ref HEAD` | Exit 0 across 574 committed-range files; 1,257 workflow-release tests and 3,189 v3 tests passed. |
| `mise exec -- uv lock --check` | Exit 0 with Ruff 0.14.4 constrained by `pyproject.toml`. |
| `mise exec -- pnpm install --lockfile-only --frozen-lockfile --ignore-scripts` | Exit 0 for the root PNPM workspace. |
| `mise exec -- pnpm --dir src/public/lib/hexo-renderer-asciidoc/examples/hexo-site install --lockfile-only --frozen-lockfile --ignore-scripts` | Exit 0 with the standalone Hexo override retained. |

No push, PR creation, acceptance probe, sentinel finalization, live activation,
publication, or package mutation was performed.

<!-- END APPEND: 2026-08-19-wdv3-final-pr-preparation-status -->

<!-- BEGIN APPEND: 2026-08-20-wdv3-implementation-pr-status -->

## Workflow Delivery v3 implementation PR

PHASE: Branch publication and PR creation
STATUS: SUCCESS
PR: https://github.com/hcoona/three/pull/552
MERGED: false
BLOCKER: PR checks, review, and separate explicit merge authorization

### Publication result

- Refreshed `origin/main` and
  `origin/dev/shuaizhang/design-workflows`; both remained ancestors of the
  validated local `HEAD`.
- Pushed the 90-commit local range non-force to
  `dev/shuaizhang/design-workflows`.
- Opened ready-for-review PR #552 against `main`.
- The PR body explicitly states that merge removes `buddy.yml` and
  `release-buddy.yml`, preserves no compatibility route, begins the intentional
  Buddy outage, and lands v3 with `live_enabled: false`.
- Initial GitHub checks entered queued or in-progress state. No merge was
  attempted.

No acceptance probe, sentinel finalization, live activation, publication, or
package mutation was performed.

<!-- END APPEND: 2026-08-20-wdv3-implementation-pr-status -->

<!-- BEGIN APPEND: 2026-08-20T025500Z-wdv3-node-provider-lfs-regression-status -->

## Workflow Delivery v3 Node Provider LFS-smudge regression

PHASE: Focused test-only regression
STATUS: BLOCKED_BY_REMAINING_PRODUCTION_FAILURE
PRODUCTION_CHANGED: false
WORKFLOWS_CHANGED: false
GENERATED_CASES: 2
GENERATED_PASSING_ON_DELIVERED_TREE: 0
GENERATED_FAILING_ON_DELIVERED_TREE: 2

### Generated test cases

- `test_internal_exact_target_git_materialization_skips_lfs_smudge_in_closed_environment[lfs-budget-exhausted]`
- `test_internal_exact_target_git_materialization_skips_lfs_smudge_in_closed_environment[ordinary-checkout-failure]`

Both cases are in
`src/public/lib/three-workflow-delivery-v3/tests/repository/test_node_provider.py`.
They use the existing real local exact-target/NBGV fixture and a subprocess
boundary that reproduces the LFS-budget failure unless the internal detached
checkout receives an explicit `GIT_LFS_SKIP_SMUDGE=1`.

### Remaining production failure

`node_provider.py::_run_command` calls `subprocess.run` without `env=`.
Consequently
`_isolated_exact_target_repository` invokes
`git checkout --detach <target>` with `env=None`; the command only inherits the
ambient process environment and the Provider cannot guarantee LFS smudge
suppression. The regression deliberately removes the ambient variable and
fails with:

```text
AssertionError: internal target checkout inherited ambient environment:
('git', 'checkout', '--detach', '<exact-target>')
```

No skip, xfail, swallowed command failure, global LFS disablement, production
repair, or workflow workaround was added.

### Validation

| Command/evidence | Result |
|---|---|
| Baseline `test_isolated_exact_target_materialization_preserves_source_and_cleans_up` | `5 passed` before the test edit |
| Canonical success case `[success]` | `1 passed` after the edit |
| Canonical propagated failure case `[git-preparation]` | `1 passed` after the edit |
| Generated focused regression on delivered production | `2 failed` at the explicit checkout environment assertion |
| Temporary diagnostic runner adding only checkout `GIT_LFS_SKIP_SMUDGE=1` | `2 passed`; this probe lived under `/tmp` and did not modify production |
| Focused Pyrefly check of source/test pair | `0 errors` |
| Ruff check and format check of canonical test file | passed |
| Append-only prefix checks for all three `.testagent` files | passed before this status append; final check remains below |

The temporary diagnostic probe is not a substitute for the delivered-tree
result. It demonstrates that both generated scenarios become green with the
minimal intended checkout environment behavior and that the ordinary checkout
failure is still propagated.

### Pre-completion gate

#### Pseudo-mutation review (`test-gap-analysis`)

- Removing or misspelling `GIT_LFS_SKIP_SMUDGE=1` is killed by the modeled LFS
  boundary and exact safe environment projection.
- Letting clone materialize a worktree is killed by the exact
  `--no-checkout` clone assertion.
- Changing or omitting the detached exact target is killed by the exact
  checkout command and checkout/NBGV target assertions.
- Removing shallow, parent-history, missing-object, or authoritative-tag
  checks is killed by concrete command assertions plus returned checkout
  evidence.
- Removing the authoritative remote replacement is killed by the exact local
  authoritative URL assertion.
- Reporting persisted credentials is killed by the exact `False` assertion.
- Swallowing, changing, or losing the cause of an ordinary checkout failure is
  killed by the exact error text, `CalledProcessError` cause, and no-PNPM/NBGV
  assertions.
- Leaking network transport or adding global Git configuration is killed by
  explicit negative command assertions.
- Removing temporary cleanup or mutating caller state is killed by target-path
  absence and complete before/after snapshot equality.

No additional in-scope test gap was found. The sole unresolved mutation is the
actual delivered production omission that intentionally leaves the regression
red.

#### Assertion-depth review (`assertion-quality`)

- Zero assertion-free or trivial-only generated cases.
- Concrete assertions cover command tuples, environment values, exact target,
  checkout fields, remote/refspec, error text/type/cause, source state,
  cleanup, and negative metadata/network/global-config observables.
- The `result is not None` assertion is only a guard and is followed by
  concrete field assertions.
- No tautological round-trip assertion exists.
- Both cases assert secondary observables beyond return values: subprocess
  environment/commands, caller state, temporary cleanup, and downstream
  command non-execution.

### Requirement coverage

| Requirement | Evidence |
|---|---|
| LFS suppression at internal materialization | Generated `lfs-budget-exhausted` case gates and records the exact detached-checkout subprocess environment |
| Preserve closed/minimal environment | Same case compares every existing `_OFFLINE_ENVIRONMENT` control plus only the required suppression value |
| Exact target | Exact checkout tuple and checkout target/head plus NBGV `gitCommitId` assertions |
| Complete history/tags | Shallow/parents/missing-object command assertions, exact tag fetch, and checkout evidence |
| Authoritative remote | Exact local `origin` URI and tag refspec assertions |
| Credential non-persistence | Exact `credentials_persisted is False` assertion |
| Failure propagation | Generated `ordinary-checkout-failure` case checks exact `ValueError`, original cause, and no PNPM/NBGV |
| No network/global LFS weakening | Local-only URI plus negative network and `git config` command assertions |
| Test-only scope | Bounded Git status contains only the canonical test and append-only `.testagent` documents |

### Completion boundary

The test-generation contract cannot be reported green because production does
not yet implement the required behavior. This is the requested remaining
production failure, not a test defect: the same generated cases pass under the
temporary one-variable checkout-environment diagnostic.

<!-- END APPEND: 2026-08-20T025500Z-wdv3-node-provider-lfs-regression-status -->

<!-- BEGIN APPEND: 2026-08-20-wdv3-node-provider-lfs-implementation-status -->

## Workflow Delivery v3 Node Provider LFS-smudge repair

PHASE: Production repair and verification
STATUS: COMPLETE
PRODUCTION_CHANGED: true
WORKFLOWS_CHANGED: false
GENERATED_CASES: 2
GENERATED_PASSING_ON_DELIVERED_TREE: 2
GENERATED_FAILING_ON_DELIVERED_TREE: 0

### Implementation

`node_provider.py::_run_command` now gives every Git subprocess an explicit copy
of the Provider process environment with `GIT_LFS_SKIP_SMUDGE=1`. Non-Git
commands retain their existing inherited-environment behavior. This suppresses
Git LFS object materialization during the internal detached exact-target
checkout without changing the checkout command, global Git configuration,
network boundaries, or subprocess failure propagation.

### Validation

| Command/evidence | Result |
|---|---|
| Complete `test_node_provider.py` | `160 passed` |
| Full Workflow Delivery v3 package suite | `3191 passed` |
| Focused Ruff check and format check | passed |
| Focused Pyrefly check | `0 errors` |
| Focused `git diff --check` | passed |

The generated `lfs-budget-exhausted` case now proves the exact internal checkout
receives the closed test environment plus only
`GIT_LFS_SKIP_SMUDGE=1`. The generated `ordinary-checkout-failure` case still
proves that an unrelated checkout error becomes the existing `ValueError` with
the original `CalledProcessError` as its cause and prevents PNPM and NBGV
execution.

No acceptance probe, sentinel finalization, live activation, publication,
package mutation, workflow workaround, global environment mutation, or global
Git configuration change was performed.

<!-- END APPEND: 2026-08-20-wdv3-node-provider-lfs-implementation-status -->

<!-- BEGIN APPEND: 2026-08-20-wdv3-node-provider-lfs-review-status -->

## Workflow Delivery v3 Node Provider LFS-smudge review closure

PHASE: Multi-angle review and independent adjudication
STATUS: COMPLETE
FINAL_REVIEW_FINDINGS: 0

Four independent reviews covered subprocess environment scope, exact-target
semantics, regression effectiveness, and the v3 design boundary. Every finding
was independently adjudicated before repair:

| Finding | Decision | Disposition |
|---|---|---|
| Process-global environment mutation was not observable | TP | The regression now compares per-variable environment digests, reports only changed variable names, and records the ambient LFS value at every intercepted subprocess |
| Original checkout exception identity was not asserted | TP | The injected `CalledProcessError` is retained and required as the exact `ValueError` cause |
| Pre-clone global Git configuration was outside the negative assertion | TP | The test now rejects `git config` across all observed Provider commands |
| Literal `node`/`pnpm` matching did not model arbitrary wrappers | FP | Existing Provider commands are literal and checkout failure exits before the context yields; wrapper mutations are outside this bounded repair |
| The subprocess spy did not accept arbitrary calling conventions | FP | The spy faithfully models the existing `_run_command` boundary; unrelated subprocess refactoring is outside scope |
| The test did not lock the environment shape of every Git and non-Git command | FP | Checkout materialization is the failing business boundary; the all-Git implementation split remains private behavior |
| Raw full-environment equality could disclose credentials on failure | TP | The assertion now compares canonical digests and exposes only changed variable names |
| Raw environment membership could disclose unrelated values | TP | The assertion now uses a derived Boolean |

The environment, exact-target, regression, and design re-reviews reported no
findings after the first adjudicated repairs. A credential-diagnostics
re-review found the two safe-output issues above; both were repaired and the
terminal changed-lines re-review reported no findings.

Final delivered-tree validation after all review repairs:

- focused generated regression: `2 passed`;
- complete Workflow Delivery v3 suite: `3191 passed`;
- focused Ruff check and format check: passed;
- focused Pyrefly check: `0 errors`;
- `git diff --check`: passed.

No workflow, manifest, lockfile, package, live-delivery state, global
environment, or global Git configuration was changed.

<!-- END APPEND: 2026-08-20-wdv3-node-provider-lfs-review-status -->

<!-- BEGIN APPEND: 2026-08-20T051623Z-pr552-codeql-closure-phase5-status -->

## PR #552 CodeQL-closure regression Phase 5

STRATEGY: Single pass
PHASE: 5
STATUS: COMPLETE_WITH_INTENTIONAL_RED
TARGETED_TEST_NODES: 40
TARGETED_PASSING: 17
TARGETED_INTENTIONAL_FAILING: 23
HARNESS_DISCOVERY_DELTA: +38 net nodes

### Bounded files and nodes

Only the four planned test modules were changed for the regression suite:

- `src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py`:
  10 added parameter nodes.
- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`:
  10 added parameter nodes and the existing authenticated-metadata node
  strengthened. The Phase 5 review added the three exact-origin
  `explicit-port`, `userinfo`, and `scheme-prefix` nodes.
- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`:
  16 added parameter nodes. The Phase 5 review added
  `test_live_attempt_has_no_nonlocal_or_revision_qualified_callers`.
- `tests/test_workflow_release_control.py`: three nodes replace the single stale
  positive `test_release_build_variant_runs_control_from_trusted_checkout`.

The diff therefore adds 39 test nodes, strengthens one existing metadata node,
and removes/replaces one stale node, for a net discovery increase of 38.

### Exact commands and final outcomes

Syntax:

```text
uv run --python 3.13 --package three-workflow-delivery-v3 python -m py_compile src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py tests/test_workflow_release_control.py
```

Exit `0`; no syntax output.

Focused regressions:

```text
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py -k 'token and (unterminated or escaped)'
```

Exit `1`: `6 failed, 4 passed, 148 deselected in 4.27s`. All failures are the
intended production `_TOKEN` blocker.

```text
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py -k 'closure_bound or absolute_form or upstream_response_header or authenticated_github_package_version_metadata or exact_github_api_origin'
```

Exit `1`: `5 failed, 6 passed, 177 deselected in 1.07s`. All failures are the
intended production proxy blockers.

```text
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py -k 'exact_target_checkouts or only_dispatch_same_commit or target_sha_stays_bound or no_nonlocal_or_revision_qualified'
```

Exit `1`: `11 failed, 5 passed, 131 deselected in 3.59s`. All failures are the
intended live-attempt checkout-ref workflow blocker.

```text
uv run --python 3.13 pytest -q tests/test_workflow_release_control.py -k 'release_build_variant'
```

Exit `1`: `1 failed, 2 passed, 994 deselected in 1.16s`. The failure is the
intended existing-orphan blocker.

Combined focused result: `23 failed, 17 passed`; there were no import,
collection, fixture, timing-race, or external-network failures in these four
test runs.

Collection:

```text
uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py
```

Exit `0`: `493 tests collected in 0.29s`. The unchanged pre-regression inventory
is 457 nodes, so the scoped v3 delta is `+36`.

```text
uv run --python 3.13 pytest --collect-only -q
```

Exit `2`: `5293 tests collected, 2 errors in 1.05s`. Every generated root node
was enumerated before two unrelated environment/import errors:

- `test_backend_and_shim.py`: no module
  `azureauth_credprovider_keyring`;
- `test_final_package_regressions.py`: no module `keyring`.

These are root harness dependency errors, not generated-test failures. The
root inventory delta attributable to the three v3 files is `+36`
(`5257 -> 5293`); all route/quote, proxy/header/origin, checkout, binding, and
caller parameter IDs are present in the root collection output.

```text
uv run --python 3.13 pytest --collect-only -q tests/test_workflow_release_control.py -k 'release_build_variant'
```

Exit `0`: `3/997 tests collected (994 deselected) in 0.27s`. This replaces one
selected stale node with three selected nodes, a net `+2`. Combined harness
delta is therefore `+38`.

Lint and formatting:

```text
uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py tests/test_workflow_release_control.py
uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py tests/test_workflow_release_control.py
```

The first pre-final format check identified only the newly appended caller
test. After formatting those test-only lines, the complete validation gate was
rerun. Final outcomes are exit `0`, `All checks passed!`, and exit `0`,
`4 files already formatted`. A later optional `/usr/bin/time` wrapper attempt
exited `127` because that binary is unavailable; it did not replace or affect
the canonical successful Ruff commands.

Predicate and diff gates:

```text
rg -n '"api\.github\.com" in url|"api\.github\.com" in call\[0\]' src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py
git --no-pager diff --check
```

The raw `rg` command returned the expected exit `1` with zero matches;
`git diff --check` returned exit `0`. A bounded zero-context diff count found
exactly three removed substring predicates: the two fake response branches and
the `api_calls` filter. All three now call the same `urlsplit`-based exact
`scheme == "https"` and exact `netloc == "api.github.com"` predicate.

### Intentional red production/workflow/file omissions

Consumer structural fallback:

- `test_unterminated_quoted_token_is_not_reclassified_as_ordinary[double]`
  receives a match for `"\\a`.
- `test_unterminated_quoted_token_is_not_reclassified_as_ordinary[single]`
  receives a match for `'\\a`.

Consumer bounded execution; each child was terminated and reaped by
`subprocess.run` after the 1.0-second safety bound:

- `test_unterminated_escaped_quoted_tokenization_completes_without_consumer_match[command-argument-double]`
- `test_unterminated_escaped_quoted_tokenization_completes_without_consumer_match[command-argument-single]`
- `test_unterminated_escaped_quoted_tokenization_completes_without_consumer_match[bun-lock-double]`
- `test_unterminated_escaped_quoted_tokenization_completes_without_consumer_match[bun-lock-single]`

The four well-formed escaped controls pass, proving the failures are not a
blanket rejection of escaped quoted tokens.

Proxy closure and response headers:

- `test_acceptance_proxy_uses_closure_bound_method_and_path_after_handler_mutation`
  observes upstream method `DELETE` and path
  `https://api.github.com.example.invalid/attacker`, rather than the qualified
  closure-bound `PUT` and fixed package path.
- `test_acceptance_proxy_rejects_illegal_upstream_response_header[header-name-cr]`
- `test_acceptance_proxy_rejects_illegal_upstream_response_header[header-name-lf]`
- `test_acceptance_proxy_rejects_illegal_upstream_response_header[header-value-cr]`
- `test_acceptance_proxy_rejects_illegal_upstream_response_header[header-value-lf]`

Each illegal-header node receives local status `201` instead of `502`; the
remaining assertions require no illegal relay, `proof is None`, and
`processed` false once the status guard is repaired.

Live-attempt checkout refs; every actual value is
`${{ inputs.target-sha }}` instead of `${{ github.sha }}`:

- `test_live_attempt_exact_target_checkouts_use_github_sha[admit]`
- `test_live_attempt_exact_target_checkouts_use_github_sha[plan-qualification]`
- `test_live_attempt_exact_target_checkouts_use_github_sha[build-tarball]`
- `test_live_attempt_exact_target_checkouts_use_github_sha[project-test]`
- `test_live_attempt_exact_target_checkouts_use_github_sha[npm-artifact-qualification]`
- `test_live_attempt_exact_target_checkouts_use_github_sha[qualification-finalizer]`
- `test_live_attempt_exact_target_checkouts_use_github_sha[observe-github-packages]`
- `test_live_attempt_exact_target_checkouts_use_github_sha[materialize-publication]`
- `test_live_attempt_exact_target_checkouts_use_github_sha[approval-finalizer]`
- `test_live_attempt_exact_target_checkouts_use_github_sha[publish-github-packages]`
- `test_live_attempt_exact_target_checkouts_use_github_sha[release-finalizer]`

Orphan file:

- `test_release_build_variant_workflow_is_absent` fails because
  `.github/workflows/release-build-variant.yml` still exists.

These four omission groups are deliberately left for parent
production/workflow remediation. No test was skipped, xfailed, weakened, or
made conditional.

### Green controls

- `test_escaped_quoted_token_preserves_consumer_match` passes for both quote
  kinds through both command-argument and Bun-lock routes.
- `test_acceptance_observation_requires_authenticated_github_package_version_metadata`
  passes: the exact two GitHub API URLs are authenticated; the
  `api.github.com.example.invalid` tarball is served as a tarball and excluded
  from `api_calls`; path and suffix-host lookalikes plus HTTP are negative.
- `test_exact_github_api_origin_requires_exact_scheme_and_netloc` passes for
  explicit-port, userinfo, and scheme-prefix confusables.
- `test_acceptance_proxy_rejects_absolute_form_target_before_upstream` passes
  with local `400`, zero HTTPS constructors/requests, and no proof or state
  transition.
- `test_acceptance_proxy_relays_legal_upstream_response_headers_status_and_body`
  passes with exact `201`, body, legal headers, proof digests, request, close,
  and processed state.
- `test_live_attempt_exact_target_checkouts_inventory_is_complete` passes,
  including the protected
  `workflow-delivery-v3-buddy-smoke-github-packages` publisher environment.
- Both caller/callee SHA-binding tests, the sole exact local dispatch-caller
  test, and the nonlocal/revision-qualified caller inventory test pass.
- Both
  `test_release_build_variant_has_no_active_workflow_reference[official.yml]`
  and `[release-orchestrate.yml]` pass; `official.yml` still delegates to the
  active orchestrator.

All proxy HTTPS boundaries were monkeypatched. Only loopback HTTP reached the
proxy; no external URL, npm registry operation, or real package-network call
occurred. The metadata test also monkeypatched the npm subprocess.

### Pseudo-mutation/test-gap review

| Plausible mutation | Concrete killing evidence | Result |
|---|---|---|
| Escaped and ordinary `_TOKEN` branches overlap | Two small quote-specific `fullmatch is None` nodes | Killed; currently red |
| Large unterminated quoted input hangs or reports a match | Four child-bound route/quote nodes require normal exit and exact JSON `false` | Killed; currently red |
| Well-formed escaped quoted behavior is lost | Four decoy-negative plus exact-consumer-positive controls | Killed |
| Upstream uses mutable `self.command` or `self.path` | Post-qualification mutation plus exact/negative request assertions for both fields | Killed; currently red |
| Absolute-form target is accepted | Exact local `400`, zero upstream, and three unchanged proxy-state assertions | Killed |
| CR/LF response-header guard is removed or partial | CR and LF independently in name and value; exact `502`, no proof/processed, no attack header | Killed; currently red |
| GitHub origin uses substring, hostname-only, or scheme-prefix matching | Exact calls plus host/path lookalikes, HTTP, explicit port, userinfo, and `httpsx` negatives | Killed |
| A checkout switches to `inputs.target-sha` or an inventory member disappears | Exact 11-job inventory and 11 independent `${{ github.sha }}` assertions | Killed; currently red |
| An extra, nonlocal, or revision-qualified live-attempt caller appears | Exact local-caller singleton plus suffix inventory that strips and detects `@revision` | Killed |
| Caller/callee target binding or binding/publication order is removed | Exact output chain, target-argument maps, DAG, Attempt upload order, and publisher order | Killed |
| Orphan file remains or an active workflow references it | Exact absence node plus two independently parsed active-workflow no-reference nodes | Killed; absence currently red |

All 11 requested feasible mutation classes are killed by concrete assertions.
No in-scope mutation survived the final review. The Phase 5 additions close
the two review gaps that could otherwise have survived hostname-only origin
matching and nonlocal/revision-qualified caller insertion. The remaining red
results are the intended production/workflow/file remediation list above, not
test-generation gaps.

### Assertion-quality review

- Semantic scope: 40 selected parameter nodes across 17 changed test
  functions.
- Assertion-free nodes: `0`. The four large token cases delegate to a helper
  that has an explicit timeout failure plus exact child return-code and stdout
  assertions.
- Trivial-only nodes: `0`.
- Self-referential or tautological nodes: `0`.
- Skip/xfail/inconclusive nodes: `0`.
- Assertions cover exact values, negative evidence, parsed collection
  inventories, call counts/arguments, proxy state, proof digests, headers,
  ordering, DAG edges, environment, and file topology.
- The small regex and orphan-absence nodes intentionally have one primary
  assertion because each pins one atomic fact. Their surrounding parameter
  and topology controls independently cover quote symmetry and active
  references.
- No test depends on precise elapsed time. The mandated child timeout is a
  fail-fast safety bound, not a performance assertion. There is no external
  network test; the proxy uses loopback plus a fake HTTPS seam.

The repeat review after the two Phase 5 additions and Ruff-only formatting
found no weak assertion requiring another test edit.

### Prompt and checklist map

| Prompt qualifier | Exact evidence |
|---|---|
| Both quote kinds x both tokenization routes | Four IDs on each of `test_unterminated_escaped_quoted_tokenization_completes_without_consumer_match` and `test_escaped_quoted_token_preserves_consumer_match`; two structural quote IDs |
| CR and LF x header name/value | Four IDs on `test_acceptance_proxy_rejects_illegal_upstream_response_header` |
| Closure-bound method AND path | `test_acceptance_proxy_uses_closure_bound_method_and_path_after_handler_mutation` asserts both exact fields and both attacker-field exclusions |
| Absolute-form target | `test_acceptance_proxy_rejects_absolute_form_target_before_upstream` |
| Legal status/body/headers | `test_acceptance_proxy_relays_legal_upstream_response_headers_status_and_body` |
| Exact GitHub origin and lookalikes | Authenticated metadata node plus three exact-origin confusable IDs; exact three-predicate zero-match gate |
| Every exact-target checkout, including Environment publisher | Complete 11-job inventory, 11 ref IDs, and exact publisher environment |
| Sole local same-commit dispatch caller | `test_buddy_is_only_dispatch_same_commit_local_live_attempt_caller` plus `test_live_attempt_has_no_nonlocal_or_revision_qualified_callers` |
| Target binding before Attempt creation/publication | Both `target_sha_stays_bound` tests, exact target maps, and step-order assertions |
| Absent orphan plus no active references | One intentional-red absence node and two independent green active-workflow nodes |
| Final bounded gate and reporting | Syntax, four focused selections, three collection commands, Ruff/check, predicate count, `diff --check`, and this append |

Every C1.1-C7.3 checklist item has concrete test or process evidence. No numeric
line/branch coverage percentage is claimed: the supplied source-to-test
pairing was a static identifier/import heuristic only and cannot establish
runtime coverage. Scenario/checklist coverage, not that static pairing, is the
completion criterion used here.

### Scope confirmation

The final bounded path check contains no production Python, workflow YAML,
CodeQL configuration, suppression/dismissal, or Node Provider LFS path. Commit
`2c0c1c24` remains out of scope. No production/workflow/orphan repair was made.
Research and plan were not edited during Phase 5; this status section is an
EOF-only append.

<!-- END APPEND: 2026-08-20T051623Z-pr552-codeql-closure-phase5-status -->

<!-- BEGIN APPEND: 2026-08-20T071044Z-pr552-codeql-closure-implementation-status -->

## PR #552 CodeQL closure implementation

PHASE: Production repair, consolidation, validation, and review
STATUS: COMPLETE
ORIGINAL_ALERTS: 20
REMAINING_KNOWN_ALERTS_IN_CHANGED_SOURCE: 0
CODEQL_SUPPRESSIONS_ADDED: 0
CODEQL_ALERTS_DISMISSED: 0

This append supersedes the historical red test-generation status above. The
generated scenarios were consolidated for human review before the production
repair was finalized; obsolete generated test names and child-timeout wording
in the historical section are not the delivered test inventory.

### Delivered repair

| Alert family | Delivered behavior | Final evidence |
|---|---|---|
| Partial SSRF | The acceptance proxy sends the closure-bound expected method and path after exact request qualification | `test_acceptance_proxy_uses_closure_bound_method_and_path_after_handler_mutation` |
| HTTP response splitting | Upstream header names and values are newline-sanitized, rejected on any change or carriage return, and only sanitized values enter proof or response relay | `test_acceptance_proxy_rejects_illegal_upstream_response_header` |
| Test URL substring matching | Fake GitHub API routing uses parsed exact HTTPS scheme and exact `api.github.com` netloc | `test_exact_github_api_origin_requires_exact_scheme_and_netloc` and the authenticated metadata scenario |
| Consumer-policy ReDoS | Quoted-token escape and ordinary branches are disjoint and newline-complete with `\\[\s\S]` | `test_quoted_token_branches_are_disjoint`, `test_large_unterminated_escaped_quote_is_not_a_consumer`, and `test_backslash_newline_continuation_keeps_quoted_package_hidden` |
| Live Attempt untrusted checkout | All 11 exact-target checkouts use trusted caller `${{ github.sha }}` while domain records remain bound to `${{ inputs.target-sha }}` | `test_live_attempt_exact_target_checkout_inventory_is_exact` |
| Reusable same-revision admission | The first `admit` step requires repository `hcoona/three`, lowercase target SHA, and equality with caller and caller-workflow SHA; only successful admission exposes `identity-admitted=true` | `test_buddy_target_sha_binding_chain_is_exact` |
| Invalid-identity finalization | The release finalizer preserves `always()` only when the shell-validated identity output is true | Existing cancellation/finalizer contracts plus `test_buddy_target_sha_binding_chain_is_exact` |
| Orphan release workflow | The documented superseded `release-build-variant.yml` workflow is deleted, active orchestrators remain, and the mandatory acceptance node now names the negative topology test | `test_release_build_variant_workflow_is_absent`, `test_release_build_variant_has_no_active_workflow_reference`, and the acceptance-gate inventory test |

### Review and adjudication

Five independent reviews covered proxy security, regex behavior, live
same-revision semantics, release topology, and CodeQL test effectiveness.
Independent adjudicators classified and repaired these true positives:

- backslash-newline escaped tokens were no longer preserved;
- the reusable boundary lacked a runtime target/caller equality guard;
- hostile unterminated and escaped-decoy token regressions were lost during
  consolidation;
- case-insensitive expression equality could schedule the release finalizer
  after lowercase identity admission failed.

The proposed `job.workflow_sha` callee-binding finding was independently
rejected: that context property is not available, and the tracked topology has
one platform same-commit local caller. Repository-wide caller inventory remains
locked, and future caller changes remain reviewed workflow-TCB changes.

After all adjudicated repairs, the terminal regex, live-workflow, proxy, and
whole-CodeQL re-reviews reported no findings.

### Final validation

| Validation | Result |
|---|---|
| Acceptance-probe test module | `189 passed` |
| Consumer-policy test module | `161 passed` |
| Buddy workflow contract module | `134 passed` |
| Workflow-release acceptance gate | `1257 passed` |
| Complete Workflow Delivery v3 suite | `3218 passed` |
| Actionlint | passed |
| Ruff check and format check | passed |
| Focused Pyrefly checks | `0 errors` |
| Consumer-policy repository scan | clean; `consumers: []` |
| `git diff --check` | passed |

An exploratory direct run of the complete root
`tests/test_workflow_release_control.py` also exposed two unrelated action-pin
consistency failures. Both reproduce unchanged in a detached worktree at
committed baseline `2c0c1c24`; they are outside this repair. The authoritative
workflow-release acceptance gate above passes all 1,257 required nodes.

No package mutation, live activation, acceptance probe, sentinel finalization,
CodeQL configuration change, alert dismissal, workflow compatibility route, or
global Git/environment change was performed.

<!-- END APPEND: 2026-08-20T071044Z-pr552-codeql-closure-implementation-status -->

<!-- BEGIN APPEND: 2026-08-21T014800Z-pr552-ci-bootstrap-projection-status -->

## PR #552 pre-coexistence CI bootstrap projection

PHASE: Production implementation, test closure, and independent review
STATUS: IMPLEMENTATION_AND_VALIDATION_COMPLETE; INDEPENDENT_REVIEW_IN_PROGRESS

The implementation preserves the canonical failed CI Slice Decision and adds
only a self-disabling check-conclusion projection for an exact pull-request
candidate whose exact base tree lacks
`.github/workflows/workflow-delivery-v3-ci.yml`.

### Requirement-to-test evidence

| Requirement | Exact evidence |
|---|---|
| Preserve the blocked Decision while admitting the one exact pre-coexistence candidate | `test_ci_scenario_precoexistence_bootstrap_preserves_blocked_decision` |
| Bind request number, base, head, tested merge, and base-tree marker exactly | `test_precoexistence_bootstrap_projection_rejects_identity_drift`; `test_git_commit_path_probe_uses_exact_base_tree` |
| Reject manual, lane-failure, superseded, mixed-diagnostic, and diagnostic-path-substitution cases | `test_precoexistence_bootstrap_projection_rejects_other_failures` |
| Canonically re-admit Plan, Decision, and Summary without rewriting either output record | `test_ci_bootstrap_projection_admits_records_without_rewriting_them`; `test_ci_bootstrap_projection_rejects_inexact_inputs` |
| Project only a failed pull-request Finalizer result while preserving success, manual failure, and the terminal missing-Decision fallback | `test_finalizer_projects_only_precoexistence_pull_request_failure` |
| Keep the reserved LLD scenario inventory exact at ten | `test_reserved_ci_scenario_inventory_is_exact` |

### Required test-quality reviews

`test-gap-analysis` and `assertion-quality` were invoked after the focused
implementation. Their optional shared `test-analysis-extensions` skill was
unavailable; the repository's Python/pytest extension was read directly.

The pseudo-mutation review found and closed two meaningful gaps:

- a canonical Decision from one Plan is now tested against a different
  canonical same-candidate Plan, proving exact Plan/Decision cross-binding;
- an otherwise eligible blocked Plan whose unclassified diagnostic names a
  path outside the changed-path set is now rejected.

The review also removed an unapproved uniqueness restriction on diagnostic
paths. Duplicate diagnostics were not prohibited by the normative predicate,
so the implementation now enforces only the approved requirements.

No non-equivalent in-scope mutation remains survived or uncovered. Defensive
checks for authority, empty selected scope, Evidence, artifacts, and derived
failure fields are partly redundant with canonical `CiSliceDecision`
invariants, but remain explicit because the approved projection predicate is
closed.

The seven generated test functions collect 15 focused cases. None is
assertion-free, trivial-only, self-referential, or tautological. Assertions
cover exact/deep equality, Boolean and negative outcomes, collection closure,
exception types, error diagnostics, immutable bytes, observed Git-probe
arguments, summary side effects, and workflow command structure. The CLI
negative matrix was strengthened so every mutation must emit its intended
rejection diagnostic; an unrelated failure can no longer satisfy the test.

### Validation

| Validation | Result |
|---|---|
| Expected-red gate before production changes | Workflow contract failed on missing environment binding; scenario collection failed on the missing policy symbol; seven CLI cases failed on the missing command/probe |
| Focused bootstrap selection | `15 passed` |
| Complete affected modules | `130 passed` |
| Complete Workflow Delivery v3 suite | `3233 passed` |
| Targeted managed HK | Passed; `v3-control-pytest` reported `3233 passed` and workflow-release control reported `1257 passed` |
| Ruff check and format check | Passed |
| Focused Pyrefly | `0 errors` |
| Actionlint | Passed |
| Package build | Built the v3 sdist and wheel |
| Lock validation | `uv lock --check` passed |
| Patch integrity | `git diff --check` passed |

Workspace-wide Pyrefly remains blocked by 166 unrelated pre-existing errors in
other packages and applications. Its output contains no error for the five
changed Python files; the exact focused Pyrefly command passes.

The first managed HK run exposed a nondeterministic alternate-repository test
fixture whose commit identity selected one of two valid rejection messages.
The fixture now uses a canonical same-candidate Plan with a changed diagnostic
only. The exact Plan-mismatch case passed three consecutive isolated runs, all
130 affected tests passed, and the repeated managed HK gate passed.

<!-- END APPEND: 2026-08-21T014800Z-pr552-ci-bootstrap-projection-status -->

<!-- BEGIN APPEND: 2026-08-21T020500Z-pr552-ci-bootstrap-review-closure -->

## PR #552 bootstrap projection review closure

PHASE: Independent multi-angle review and adjudication
STATUS: COMPLETE

Three independent reviewers owned non-overlapping policy/scenario,
CLI/admission, and workflow/contract scopes. Every atomic finding received a
separate TP/FP adjudication before any production decision.

### Adjudication

| Scope | Finding | Verdict | Resolution |
|---|---|---|---|
| Workflow contract | Finalizer exit capture adjacency was not pinned | TP | The contract now proves the last Finalizer argument is immediately followed by `finalizer_exit=$?`. |
| Workflow contract | Success, non-PR, and projection gate order was not pinned | TP | The contract now proves strict ordered indices through all three gates. |
| Workflow contract | PR number could be removed from only the projection invocation | TP | Assertions now isolate the projection block and require its complete identity argument. |
| Workflow contract | `continue-on-error: true` was not excluded | TP | The finalization/projection step must have no `continue-on-error` key. |
| Policy | `unsupported` supersession should be rejected | FP for production | Normative text rejects explicit `superseded`; exact event identity remains the approved binding when platform proof is unavailable. |
| Policy | Approved `unsupported` behavior lacked a regression | TP test gap | `test_bootstrap_projection_allows_unavailable_platform_proof` now pins the intended positive path. |
| CLI | Local Git replacement refs require extra hardening | FP | Replacement refs require control of trusted local Git metadata, outside the clean hosted-runner and trusted-operator boundary. |
| CLI | Summary-path inode aliases require extra hardening | FP | The workflow supplies a runner-managed path; a local operator able to create aliases already has direct record-mutation capability. |

The first CLI adjudicator returned no findings without the required explicit
per-item classification. A separate independent boundary adjudicator therefore
re-ran both decisions and returned explicit FP verdicts; the incomplete result
was not used as evidence.

After the fixes and clarifications, all three original reviewers re-read their
owned scopes and returned no findings.

### Post-review validation

- Complete affected modules: **131 passed**.
- Focused policy bootstrap scenarios, including unavailable platform proof:
  **7 passed**.
- Workflow contract module: **18 passed**.
- Ruff check and format check: passed.
- Focused Pyrefly: **0 errors**.
- Actionlint and `git diff --check`: passed.

The earlier generated-test count is superseded: the final generated surface is
eight test functions collecting 16 focused cases.

<!-- END APPEND: 2026-08-21T020500Z-pr552-ci-bootstrap-review-closure -->

<!-- BEGIN APPEND: 2026-08-21T021300Z-pr552-ci-bootstrap-final-gate -->

## PR #552 bootstrap projection final gate

STATUS: COMPLETE

The final managed HK run covered the seven delivered implementation, workflow,
test, and status files after all review repairs:

- Workflow Delivery v3 control suite: **3234 passed**.
- Workflow-release control suite: **1257 passed**.
- Actionlint, Ruff check/format, Markdown formatting, spelling,
  EditorConfig, and the other selected repository hooks passed.
- `git diff --check` passed.

All three original reviewers returned no findings in the terminal follow-up
round. The implementation is ready for its bounded commit; PR publication and
live check observation remain separate steps.

<!-- END APPEND: 2026-08-21T021300Z-pr552-ci-bootstrap-final-gate -->

<!-- BEGIN APPEND: 2026-08-21T030000Z-pr552-ci-bootstrap-remote-proof -->

## PR #552 bootstrap projection remote proof

STATUS: COMPLETE

Published head `9b7b7d2c` passed every PR check. Workflow Delivery v3 run
`32440545037` and its shadow Finalizer job completed successfully; associated
general CI run `32440545005` and CodeQL run `32440545090` also passed.

The retained run artifacts contain a blocked Plan with 576 changed paths, 283
exclusively unclassified-path diagnostics, four empty lane results, no
Evidence, and no artifact digests. Replaying those exact Plan and lane-result
records produced:

- Finalizer exit `1`;
- terminal result `failure`;
- failure class `incomplete-model-plan`;
- next action `fix-model-plan-and-rerun`;
- authority `non-authoritative`;
- supersession state `unsupported`; and
- zero admitted Evidence and artifact digests.

Replaying the projection against exact base `7f8f41c2`, head `9b7b7d2c`, and
tested merge `0b31d95e` succeeded and emitted the explicit
`Pre-coexistence bootstrap projection` note stating that the canonical
Decision remains failure. This proves the green check is the approved
conclusion projection rather than a Decision rewrite.

<!-- END APPEND: 2026-08-21T030000Z-pr552-ci-bootstrap-remote-proof -->

<!-- BEGIN APPEND: 2026-08-26-wdv3-acceptance-proof-repair -->

## Workflow Delivery v3 Acceptance Proof Repair Status

| Requirement | Evidence |
| --- | --- |
| Normal HTTP 201 proof propagation | `test_normal_runner_propagates_proxy_http_201_exchange_proof` |
| Proof-bound normal completion | `test_normal_create_propagates_request_bound_http_201_exchange_proof` |
| Proof-free create remains incomplete | `test_normal_create_without_http_exchange_proof_remains_incomplete` |
| Existing ambiguity reconciliation retained | `test_lost_response_with_complete_identity_reconciles_after_ambiguity` |
| Optional diagnostic is bounded and credential-free | `test_runner_failure_diagnostics_are_structured_bounded_and_redacted` |
| Diagnostic remains non-authoritative | `test_protocol_confirmed_governance_does_not_require_runner_diagnostic`, `test_protocol_confirmed_governance_diagnostic_exit_is_non_authoritative` |
| Historical evidence remains replayable | `test_historical_created_evidence_without_proof_remains_admissible` |
| Proof and readback fail closed | `test_protocol_confirmed_rejects_substituted_exchange_facts`, `test_protocol_confirmed_result_requires_exact_complete_readback` |
| Adapter/Governance startedness remains consistent | `test_normal_protocol_confirmed_requires_every_authoritative_condition`, `test_protocol_confirmed_readback_incomplete_requires_startedness` |

Validation under the repository-locked Mise environment:

- focused acceptance and Governance tests: `543 passed`;
- complete Workflow Delivery v3 suite: `3686 passed`;
- no test was skipped or deselected.

Pseudo-mutation analysis found no in-scope survivor: proof omission or
substitution, proof-free success, startedness inversion, readback fail-open,
diagnostic elevation, and diagnostic/proof cross-binding removal are killed.

Assertion-quality analysis found 14 test functions with meaningful equality,
Boolean, None, exception, negative, comparison, state/side-effect, collection,
and structural assertions. There are no assertion-free, trivial-only,
self-referential, or tautological tests.

PR #596 review identified that a valid protocol proof could retain protocol
authority when admitted runner facts reported that action execution or mutation
had not started. The Adapter now requires both admitted startedness facts before
retaining protocol authority; otherwise it emits the existing fail-closed
runner classification without proof or protocol diagnostic. The focused
543-test suite passed after this correction, and independent review reported no
findings.

No network, workflow, Environment, package, coordinate, or Live operation was
performed.

<!-- END APPEND: 2026-08-26-wdv3-acceptance-proof-repair -->

<!-- BEGIN APPEND: 2026-08-26-wdv3-acceptance-retry-3-fallback -->

## Retry-3 fallback test phase status

**Status: SUCCESS for the requested retry-3 phase; unrelated harness blockers
recorded.**

Created 23 collected test cases across five files. The focused command passed
`25 passed, 679 deselected` (23 new cases plus two selected historical
preservation cases). Targeted discovery increased from 681 to 704 tests
(`+23`). Ruff format check reported `5 files already formatted`; Ruff check
reported `All checks passed!`.

The complete five-file run reached `702 passed, 2 failed`. Both failures are
pre-existing environment/toolchain fixture checks:
`test_acceptance_capture_records_nonsecret_toolchain_metadata` and
`test_acceptance_request_fixture_is_reproducible` observed installed
Node/npm `v24.14.0`/`11.9.0` instead of their fixed
`v24.19.0`/`11.17.0` expectations. They are outside the retry-3 behavior and
were not weakened. Root default collection saw 3737 tests but stopped with 13
unrelated collection errors in other workspace projects; the targeted package
collection discovered all 704 relevant tests.

Requirement-to-test evidence:

| Requirement | Evidence |
| --- | --- |
| `.9`/`.10`/`.11`/`.12`, tags 9-12; `.1`-.8 preserved | `test_retry_3_suite_resolves_exact_coordinates_and_preserves_history` |
| Retry-3 suite execution and npm runner binding | `test_retry_3_suite_executes_with_exact_base_coordinate_and_tag`, `test_retry_3_npm_runner_uses_exact_lost_response_coordinate` |
| Cross-profile Adapter proof rejection | `test_retry_3_proof_rejects_cross_profile_coordinate_and_tag` |
| Exact retry-3 path, Environment, digest, zero rejected dispatch | `test_retry_3_profile_admits_exact_zero_sentinel_rejected_dispatch`, `test_retry_3_dispatch_and_profile_literals_are_exact` |
| Governance cross-profile and coordinate/tag closure | `test_retry_3_profile_rejects_cross_profile_substitution`, `test_retry_3_profile_rejects_scenario_coordinate_or_tag_mismatch` |
| Retry-1/retry-2 admission and literal historical digests | `test_retry_3_profile_preserves_retry_1_and_retry_2_admission` |
| Five-job DAG and first-attempt guards | `test_retry_3_has_exact_five_job_first_attempt_dag` |
| Zero target rejects before review/mutation | `test_retry_3_zero_sentinel_fails_before_review_or_mutation`, `test_retry_3_terminal_script_emits_canonical_rejected_dispatch` |
| Packages-write only for probes | `test_retry_3_permissions_limit_packages_write_to_probe_jobs` |
| Node/npm versions and full action pins | `test_retry_3_toolchain_and_action_revisions_are_fully_pinned` |
| Terminal always capture and optional diagnostic reconstruction with canonical digest equality | `test_retry_3_terminal_capture_is_always_and_reconstructs_diagnostics` |
| CODEOWNERS and no Live/Release route | `test_retry_3_is_owned_and_contains_no_live_or_release_route` |
| Retry-3 sole temporary workflow with exact jobs | `test_retry_3_is_the_only_temporary_acceptance_workflow_preserved` |
| Original/retry-2 absent; normal Buddy disabled | `test_retry_3_temporary_acceptance_coexists_with_disabled_normal_buddy` |

Pseudo-mutation review found the requested omissions killed: profile suffix or
tag drift, historical profile replacement, digest/path/Environment
substitution, zero fail-open, job/guard removal, permissions expansion,
unpinned toolchains/actions, terminal `always()` removal, omitted diagnostic
reconstruction, canonical digest comparison removal, ownership removal, and
temporary workflow coexistence all change asserted observables.

Assertion-quality review found no assertion-free, trivial-only,
self-referential, or tautological generated test. The set includes equality,
collection/structural, exception/negative, ordering, subprocess side-effect,
AST-binding, and canonical-byte assertions.

No network/live operation, staging, commit, or push was performed.

<!-- END APPEND: 2026-08-26-wdv3-acceptance-retry-3-fallback -->

<!-- BEGIN APPEND: 2026-08-26-wdv3-acceptance-retry-3-audit-closure -->

## Retry-3 high-confidence audit closure

Closed all requested retry-3 gaps without network or live operations. The
terminal workflow Python is now executed against canonical complete suite
records and proves exact preservation of the proof, all four runner-diagnostic
fields, canonical record digest, probe result, and mutation classification.
Dispatch input objects, per-job guards, permissions, and ordered action uses
are exact; fixed-input validation covers a nonzero finalized target and each
independent mismatch, with the complete guard order asserted.

Adapter and Governance assertions now pin every retry-3 coordinate/tag pair,
the zero target profile, workflow, Environment, recovery Environment,
confirmation digest, and historical `.1`-`.8` profiles and literal record
digests. Pseudo-mutation review confirms the targeted `None`, wrong-key/value,
omitted-diagnostic, guard-removal/reordering, permission/action multiplicity,
and coordinate/tag/profile substitutions are killed. Assertion review found
no assertion-free, trivial-only, or self-referential additions.

Validation:

- Focused five-file pytest: `712 passed in 30.49s` (`+8` collected cases over
  the prior 704-test baseline).
- Ruff check: `All checks passed!`.
- Ruff format check: all five files already formatted.
- No staging, commit, push, workflow dispatch, Environment, package, or remote
  operation was performed.

<!-- END APPEND: 2026-08-26-wdv3-acceptance-retry-3-audit-closure -->

<!-- BEGIN APPEND: 2026-08-26-wdv3-acceptance-retry-3-final-gap-closure -->

## Retry-3 final gap closure

The final workflow contract pins exact concurrency, immutable checkout refs,
credential persistence, complete probe step inventories, the sole
token-bearing mutation commands, probe output wiring, terminal dependency
bindings, and the terminal artifact identity and error-on-missing policy. The
focused five-file suite passes `714 tests` under the repository-locked Mise
environment.

Independent adjudication classified the proposed self-contained fallback for a
terminal job checkout or tool-bootstrap failure as false-positive and
out-of-scope. The normative contract requires terminal fan-in across upstream
dependency failures, not fabrication of canonical Governance evidence without
the reviewed source and admission implementation. Missing evidence remains an
explicit failed platform outcome and blocks acceptance.

No network request, workflow dispatch, Environment operation, package
mutation, staging, commit, or push was performed.

<!-- END APPEND: 2026-08-26-wdv3-acceptance-retry-3-final-gap-closure -->

<!-- BEGIN APPEND: 2026-08-26-wdv3-acceptance-retry-3-review-closure -->

## Retry-3 preparation review closure

The final tests/documentation review produced four findings. Independent
adjudication classified three as true positives and one as false positive:

- The zero-sentinel contract inspected the guard without executing it: true
  positive. The test now executes the validation shell with the zero target
  and proves failure before Environment review or either mutation job.
- Trigger and per-job Environment assertions were incomplete: true positive.
  The contract now pins the exact trigger allowlist and exact job-to-Environment
  mapping.
- The local CODEOWNERS assertion only checked line presence: false positive.
  The existing commit-9 ownership contract already evaluates ordered,
  last-match effective ownership for governed workflow surfaces.
- The overview described Live activation as separately authorized: true
  positive. It now states that Live remains unauthorized and requires separate
  authorization.

The same reviewer re-examined the repaired scope and reported no findings.
Final validation under the repository-locked Mise environment:

- focused five-file pytest: `715 passed in 31.34s`;
- complete Workflow Delivery v3 pytest: `3720 passed in 490.61s`;
- Prettier and markdownlint for the corrected overview: passed;
- `git diff --check`: passed.

No network request, workflow dispatch, Environment operation, package
mutation, staging, commit, push, or PR operation was performed.

<!-- END APPEND: 2026-08-26-wdv3-acceptance-retry-3-review-closure -->
