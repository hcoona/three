# Workflow Delivery v3 Snapshot Admission Research

## Request

Implement the final adjudicated TP finding for Workflow Delivery v3
implementation commit 3: `RepositoryModelSnapshot` admission must enforce the
exact immutable runtime closure before digest/readiness use, and Live
Eligibility must reject list-backed or otherwise mutated Snapshots. The final
implementation also closes the independent-review finding for
`release_policy_path`, whose exact `str` type must be established before
comparison.

The user required **Single pass** Research -> Plan -> Implement, preservation of
unrelated working-tree changes, and retained `.testagent/` artifacts.

## Instructions and Conventions Read

- `AGENTS.md`
  - Read `docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md` before
    Workflow Delivery v3 work.
  - Use English.
  - Do not use v1/v2 as normative sources for v3.
- `docs/AGENTS.md`
  - Applies to the handoff page only; do not modify immutable source/raw docs.
- `docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md`
  - Commit 3 includes exact-target Repository Model compilation and exact-target
    Live Eligibility Decision.
- Root Python conventions:
  - `pyproject.toml` uses pytest `--import-mode=importlib`.
  - Tests are pytest-style with bare `assert`, `pytest.raises`, dataclass
    `replace`, and parameterized contract matrices.
- `code-testing-extensions` was unavailable before this run; repository Python
  conventions and representative pytest files were used instead.

## Bounded Target Inventory

### Production source

1. `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/repository/compiler.py`
   - `RepositoryModelSnapshot`
   - `CompilationContext`
   - `CompiledReleaseUnit`
   - `CompiledBuild`
   - `CompiledOutput`
   - `CompiledQualitySelection`
   - `validate_first_slice_repository_model_snapshot`
   - Snapshot tuple fields actually defined:
     - `provider_result_digests`
     - `project_nodes`
     - `release_units`
     - `quality`
     - `reverse_index`
     - `unresolved`
     - nested `ProjectNode.workspace_dependencies`
     - nested `CompiledReleaseUnit.builds`
     - nested `CompiledBuild.outputs`
     - nested `CompiledBuild.required_native_projections`
     - nested `CompiledQualitySelection.required`
     - nested `CompiledQualitySelection.advisory`
     - nested reverse-index entries and build-id tuples
   - Snapshot bounded scalar fields:
     - `manifest_digest`
     - `release_policy_path`
     - `ready`
   - `release_policy_path` is part of the bounded scalar closure and must be
     exact `str` before any equality comparison/use.
   - Snapshot record fields actually defined:
     - `context`
     - `ProjectNode`
     - `CompiledReleaseUnit`
     - `CompiledBuild`
     - `CompiledOutput`
     - `CompiledQualitySelection`
     - `NbgvFacts`

2. `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/eligibility.py`
   - `_validate_live_context`
   - `evaluate_live_eligibility`
   - Must validate Snapshot admission before any `snapshot.snapshot_digest`,
     `snapshot.ready`, or `snapshot.unresolved` use.

### Tests

`src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit3_contract_boundaries.py`

Existing representative patterns in this file:

- `_snapshot()` builds the canonical first-slice `RepositoryModelSnapshot`.
- Contract tests use `replace(...)`, `cast("Any", ...)`, parameterization, and
  concrete post-assertions that the valid baseline remains admitted.
- Positive controls pin canonical digest and concrete first-slice identities.

No later v3 scope, workflow files, descriptors, sibling packages, or unrelated
docs were inventoried for implementation.

## Requirement Checklist

| ID | Requirement | Research finding |
|---|---|---|
| R1 | Enforce exact immutable runtime closure types throughout Snapshot admission. | Current admission had some tuple/type checks but not a recursive closure check for every Snapshot record/tuple position. |
| R2 | Require exact tuple for top-level `project_nodes` and `release_units`. | Add explicit admission checks before length/indexing. |
| R3 | Require exact tuple for every tuple-typed nested collection, including `builds`, `outputs`, `dependencies`, `variants`, `capabilities`/`facts` wherever applicable. | Actual Snapshot has `workspace_dependencies`, `builds`, `outputs`, `required_native_projections`, `quality.required`, `quality.advisory`, reverse-index tuples, plus top-level digest/unresolved tuples. No `variants` field is defined in the current Snapshot source. |
| R4 | Require exact expected frozen dataclass record types, rejecting duck objects, subclasses, mappings, lists, and surrogates at top-level/nested record positions. | `RepositoryModelSnapshot`, `CompilationContext`, `ProjectNode`, compiled record types, and `NbgvFacts` need exact-type admission before field use. |
| R5 | Admission validation must occur before any digest or readiness use. | `_validate_live_context` compared `context.repository_model_digest` to `snapshot.snapshot_digest` and consulted readiness before final admission; reorder is required. |
| R6 | Live Eligibility must reject mutated or list-backed snapshots. | Need a digest-equivalent list-backed TOCTOU regression through `_validate_live_context`. |
| R7 | Add comprehensive negative tests for top-level and nested list/type substitutions. | Add parameterized list and tuple-subclass matrices plus record-surrogate matrix. |
| R8 | Add a TOCTOU regression proving mutation cannot cross admission. | Start with a valid admitted Snapshot and live-context digest, install a side-effecting `release_policy_path` surrogate, and prove Live Eligibility rejects before surrogate comparison can mutate `release_units`. |
| R9 | Preserve canonical JSON serialization for valid tuples. | Add positive canonical JSON array and digest control for the valid Snapshot. |
| R10 | Scope only the final adjudicated TP finding in Workflow Delivery v3 commit 3. | Only `compiler.py`, `eligibility.py`, the commit-3 contract-boundary test file, and `.testagent` artifacts are implementation targets. |
| R11 | Update `.testagent` artifacts. | This file, `plan.md`, and `status.md` are retained and updated for this run. |
| R12 | Run narrow tests, then package/managed HK, root importlib pytest, Pyrefly, Ruff, build, locks, git diff/check. | Exact commands are listed below and results are recorded in `status.md`. |

## Source-to-Test Mapping

| Production behavior | Test evidence |
|---|---|
| Top-level exact tuple admission for `project_nodes` and `release_units` | `test_repository_model_admission_rejects_top_level_tuple_surrogates` |
| Nested tuple admission for digest, dependency, build/output, capability/projection, quality, reverse-index, and unresolved tuples | `test_repository_model_snapshot_admission_rejects_nested_tuple_substitutions` |
| Exact top-level Snapshot runtime type at admission and Live Eligibility | `test_snapshot_admission_and_live_eligibility_reject_top_level_surrogates` |
| Exact record runtime types for Snapshot record positions | `test_repository_model_snapshot_admission_rejects_record_surrogates` |
| Live Eligibility validates before digest serialization | `test_live_eligibility_validates_snapshot_admission_before_digest_use` |
| Digest-equivalent list-backed mutation is blocked | `test_live_eligibility_rejects_digest_equivalent_list_backed_snapshot` |
| `release_policy_path` is exact `str` before equality can mutate the Snapshot | `test_live_eligibility_blocks_toctou_mutation_during_snapshot_admission` |
| Valid tuples still serialize as canonical JSON arrays | `test_repository_model_valid_tuples_keep_canonical_json_arrays` |

Expected generated case count after the final review regression: 64.

## Independent-Review Finding and Fix

The independent review found a surviving TOCTOU gap: admission compared
`snapshot.release_policy_path` to `FIRST_SLICE_POLICY_PATH` without first
requiring exact `str`, so a user-defined `__ne__` could mutate the already
validated `release_units` tuple to a digest-equivalent list and restore the path.
The focused fix records `release_policy_path` in the bounded scalar closure and
adds an exact type guard before the comparison. R8 now maps to a dedicated
adversarial test that proves the comparison payload is not executed.

## Exact Validation Commands Discovered/Used

- Narrow generated tests:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit3_contract_boundaries.py -k 'repository_model_admission_rejects_top_level_tuple_surrogates or repository_model_snapshot_admission or snapshot_admission_and_live_eligibility_reject_top_level_surrogates or live_eligibility_validates_snapshot_admission_before_digest_use or live_eligibility_rejects_digest_equivalent_list_backed_snapshot or live_eligibility_blocks_toctou_mutation_during_snapshot_admission or repository_model_valid_tuples_keep_canonical_json_arrays'`
- Full commit-3 contract file:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit3_contract_boundaries.py`
- Pyrefly:
  `uv run --python 3.13 pyrefly check`
- Ruff:
  `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/repository/compiler.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit3_contract_boundaries.py`
  `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/repository/compiler.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit3_contract_boundaries.py`
