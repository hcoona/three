# Workflow Delivery v3 Snapshot Admission Test Plan

## Strategy

**Single pass.** The request spans one bounded production feature and its
contract tests, but it is limited to the Workflow Delivery v3 commit-3 Snapshot
admission TP finding.

## Phase 1 — Research and Admission Design

1. Read repository instructions and v3 handoff.
2. Inventory only:
   - `repository/compiler.py`
   - `release/eligibility.py`
   - `tests/contracts/test_commit3_contract_boundaries.py`
3. Enumerate actual Snapshot tuple and record fields.
4. Design exact-type validation helpers that reject:
   - list-backed tuple positions;
   - tuple subclasses;
   - record subclasses;
   - duck objects;
   - mappings;
   - lists.

## Phase 2 — Production Implementation

### `repository/compiler.py`

Planned changes:

- Add exact tuple helper used by `validate_first_slice_repository_model_snapshot`.
- Validate top-level tuple fields before length/indexing:
  `provider_result_digests`, `project_nodes`, `release_units`, `quality`,
  `reverse_index`, `unresolved`.
- Validate nested tuple fields:
  `ProjectNode.workspace_dependencies` via existing `validate_project_node`,
  `CompiledReleaseUnit.builds`, `CompiledBuild.outputs`,
  `CompiledBuild.required_native_projections`,
  `CompiledQualitySelection.required`,
  `CompiledQualitySelection.advisory`, reverse-index entries and build IDs.
- Add exact compiled-record validators for `CompiledReleaseUnit`,
  `CompiledBuild`, `CompiledOutput`, and `CompiledQualitySelection`.
- Continue using `validate_compilation_context`, `validate_project_node`, and
  `validate_nbgv_facts` for exact `CompilationContext`, `ProjectNode`, and
  `NbgvFacts` runtime types.
- Require `snapshot.release_policy_path` to be an exact nonempty `str` before
  comparing it with `FIRST_SLICE_POLICY_PATH`.

### `release/eligibility.py`

Planned changes:

- In `_validate_live_context`, call
  `validate_first_slice_repository_model_snapshot(snapshot)` before comparing
  `snapshot.snapshot_digest` or reading readiness/unresolved state.
- Keep binding comparison after admission.
- Exercise that boundary through the R5, R6, and R8 Live Eligibility
  regressions.

## Phase 3 — Tests

Target:
`src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit3_contract_boundaries.py`

Planned generated tests:

1. `test_repository_model_admission_rejects_top_level_tuple_surrogates`
   - `project_nodes` and `release_units`
   - list and tuple-subclass substitutes
2. `test_repository_model_snapshot_admission_rejects_nested_tuple_substitutions`
   - `provider_result_digests`
   - `project_nodes.workspace_dependencies`
   - `release_units.builds`
   - `release_units.builds.outputs`
   - `release_units.builds.required_native_projections`
   - `quality`
   - `quality.required`
   - `quality.advisory`
   - `reverse_index`
   - `reverse_index.entry`
   - `reverse_index.build_ids`
   - `unresolved`
   - list and tuple-subclass substitutes
3. `test_repository_model_snapshot_admission_rejects_record_surrogates`
   - `context`
   - `project`
   - `release-unit`
   - `build`
   - `output`
   - `quality`
   - `nbgv`
   - subclass, duck object, mapping, and list substitutes
4. `test_snapshot_admission_and_live_eligibility_reject_top_level_surrogates`
   - top-level `RepositoryModelSnapshot`
   - subclass, duck object, mapping, and list substitutes
   - direct admission and Live Eligibility boundaries
5. `test_live_eligibility_validates_snapshot_admission_before_digest_use`
   - trap object raises if digest serialization reads it before admission
6. `test_live_eligibility_rejects_digest_equivalent_list_backed_snapshot`
   - proves JSON digest can remain unchanged while admission rejects list-backed
     state
7. `test_live_eligibility_blocks_toctou_mutation_during_snapshot_admission`
   - starts with a valid admitted Snapshot and live-context digest, installs a
     side-effecting `release_policy_path` surrogate, and proves admission
     rejects before the surrogate comparison mutates `release_units`
8. `test_repository_model_valid_tuples_keep_canonical_json_arrays`
   - concrete JSON array structure and canonical digest positive control

Expected generated case count: 64.

## Requirement-to-Test Plan

| Requirement | Planned evidence |
|---|---|
| R1 | All generated negative tests plus production helper validators in `compiler.py`. |
| R2 | `test_repository_model_admission_rejects_top_level_tuple_surrogates`. |
| R3 | `test_repository_model_snapshot_admission_rejects_nested_tuple_substitutions`; note no current Snapshot `variants` field. |
| R4 | `test_snapshot_admission_and_live_eligibility_reject_top_level_surrogates` and `test_repository_model_snapshot_admission_rejects_record_surrogates`. |
| R5 | `test_live_eligibility_validates_snapshot_admission_before_digest_use`. |
| R6 | `test_live_eligibility_rejects_digest_equivalent_list_backed_snapshot`. |
| R7 | Tuple and record surrogate matrices. |
| R8 | `test_live_eligibility_blocks_toctou_mutation_during_snapshot_admission`. |
| R9 | `test_repository_model_valid_tuples_keep_canonical_json_arrays`. |
| R10 | Changed-file list limited to bounded production/test files plus `.testagent`. |
| R11 | `.testagent/research.md`, `.testagent/plan.md`, `.testagent/status.md`. |
| R12 | Validation command table in `status.md`. |

## Validation Plan

Run in order:

1. Narrow generated-test selection, including the new R8 test.
2. Full commit-3 contract file.
3. Pyrefly.
4. Ruff check for the changed Python files.
5. Ruff format check for the changed Python files.
6. Git diff/status checks.
7. Pre-completion pseudo-mutation, assertion-depth, and literal
    prompt-scenario coverage review.
