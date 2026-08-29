# Workflow Delivery v3 Snapshot Admission Test Plan

## 2026-08-28 Workflow Delivery v3 Retry-4 Acceptance Preparation Plan

### Strategy and edit boundary

Use one sequential **Research -> Plan -> Implement** pass. The result is
intentionally RED: test collection and test-side quality gates must pass, but
execution must fail solely because the fourth production profiles and the
retry-4 workflow do not yet exist.

Allowed implementation paths:

1. `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`
2. `src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py`
3. `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_retry_4_workflow.py`
4. `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
5. `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py`

The `.testagent` artifacts are also allowed. Do not edit production Python,
the CLI, manifests, locks, Governance configuration, or `.github/workflows`.
Do not create, restore, or mutate a workflow. Add no skip, `xfail`,
conditional escape, or weakened assertion.

### Phase 1 - Adapter identity, resolution, runner, suite, and proof

Modify only
`tests/adapters/test_commit10_acceptance_probes.py`.

Before adding retry-4 expectations, move the two valid-form unregistered
negative fixtures from `.13` to `.17`:

- keep
  `test_retry_2_suite_resolves_only_the_reviewed_coordinate_block`;
- keep
  `test_acceptance_probe_requires_the_fixed_coordinate_and_explicit_tag`;
- alter only fixture coordinates/tags and preserve the negative assertions.

Add these collection-safe tests:

1. `test_retry_4_adapter_profiles_have_stable_historical_order_and_unique_base_coordinates`
   - exactly `.1`, `.5`, `.9`, `.13`, stable and unique.
2. `test_retry_4_adapter_profiles_preserve_scenario_order_and_qualified_identity_uniqueness`
   - exact five-scenario order for every profile;
   - unique profile-qualified identity;
   - only intentional absent/exact reuse inside a profile.
3. `test_retry_4_adapter_coordinate_tag_pairs_are_exact_and_globally_unique`
   - exactly 16 coordinate/tag pairs across four blocks;
   - exact retry-4 `.13`, `.13`, `.14`, `.15`, `.16` mapping and tags.
4. `test_retry_4_fixed_acceptance_resolvers_return_exact_scenarios_and_coordinates`
   - direct fourth-profile resolution and `.17` rejection.
5. `test_retry_4_npm_runner_invokes_all_five_exact_coordinates`
   - record exact five runner coordinates in order using the existing fake.
6. `test_retry_4_fixed_acceptance_suite_routes_exact_bindings_through_controlled_fakes`
   - observe exact package/base/scenario/coordinate/tag routing and suite
     result.
7. `test_retry_4_validated_proof_accepts_exact_coordinate_tag_bindings`
   - matched retry-4 proof.
8. `test_retry_4_validated_proof_rejects_historical_substitutions_in_both_directions`
   - retry-4/historical coordinate-tag substitutions both ways.

Run Adapter collection first, then the two retained negative tests, then
`-k retry_4`. Collection and the retained tests must pass. Retry-4 execution
must fail only as `E-ADAPTER-PROFILE-ABSENT`; correct any collection,
fixture, fake, or assertion-construction defect without touching source.

Maps: A1, A3-A12.

### Phase 2 - Governance preparation and placeholder-finalized shapes

Modify only
`tests/governance/test_commit10_acceptance_evidence.py`.

Add test-local retry-4 document helpers and:

1. `test_retry_4_governance_profiles_have_stable_historical_order_and_unique_base_coordinates`
2. `test_retry_4_governance_profile_binds_exact_workflow_environment_confirmation_digest_and_scenarios`
3. `test_retry_4_governance_admits_exact_zero_target_rejected_dispatch`
4. `test_retry_4_governance_rejects_non_exact_zero_targets`
   - 39 zeroes, 41 zeroes, non-ASCII zeroes, and nonzero hex.
5. `test_retry_4_zero_target_rejects_review_probe_record_artifact_reviewer_or_mutation_claims`
   - one precise forbidden observable per parameter case.
6. `test_retry_4_finalized_placeholder_round_trips_canonically_with_exact_bindings`
   - temporarily patch the expected fourth Governance profile with a clearly
     named test-only nonzero 40-hex target;
   - never use the work-base or provenance SHA as authority.
7. `test_retry_4_governance_rejects_cross_profile_field_substitutions`
   - workflow, Environment, recovery Environment, digest, target, coordinate,
     and tag in both applicable directions.
8. `test_retry_4_governance_preserves_historical_profiles_digests_and_replay_evidence`
   - exact retry-1 through retry-3 tuples, admission, suite digests, and replay
     evidence.

The exact zero preparation case must assert validation failure; skipped
review/probes; empty records; absent artifact and reviewer; and incomplete
mutation classification. Run collection, historical-preservation selections,
then `-k retry_4`. Classify only absence of the fourth profile as
`E-GOVERNANCE-PROFILE-ABSENT`.

Maps: A2-A3 and G1-G10.

### Phase 3 - Dedicated retry-4 workflow contract

Add
`tests/contracts/test_commit10_acceptance_retry_4_workflow.py`.
Re-author the assertions against current authority. Historical retry-3 tests
may supply mechanisms only. Load the absent workflow lazily inside test
bodies, making absence an ordinary assertion failure rather than a collection
or fixture error.

Add:

1. `test_retry_4_workflow_uses_exact_temporary_path_stem_and_environment_identity`
2. `test_retry_4_workflow_declares_exact_five_jobs_in_order`
3. `test_retry_4_workflow_applies_first_attempt_guards_and_terminal_always_capture`
4. `test_retry_4_workflow_scopes_environment_and_packages_write_permissions_to_exact_jobs`
5. `test_retry_4_workflow_zero_target_stops_before_review_and_write_capable_probes`
6. `test_retry_4_workflow_test_only_nonzero_placeholder_satisfies_finalized_guard_shape`
7. `test_retry_4_workflow_dispatch_identity_confirmation_digest_and_concurrency_are_exact`
8. `test_retry_4_workflow_pins_current_actions_toolchains_checkout_and_probe_wiring`
9. `test_retry_4_workflow_wires_terminal_governance_evidence_exactly`
10. `test_retry_4_workflow_rejects_wrong_dispatch_inputs`
11. `test_retry_4_workflow_exposes_no_live_release_bypass_force_or_generalized_triggers`

The tests must require exactly:

- `validate-fixed-inputs`;
- `acceptance-review`;
- `probe-absent-create-readback`;
- `probe-exact-and-conflict`;
- `capture-governance-evidence`.

Require first-attempt guards, terminal `always()` capture, Environment only on
review, and `packages: write` only on the two probe jobs. Execute the bounded
guard script in controlled test environments to prove that exactly forty
zeroes fail before review or either mutation-capable probe, while a clearly
test-only nonzero target demonstrates the eventual finalized shape.

Run collection, then the file. Collection must pass; all execution failures
must be `E-WORKFLOW-ABSENT`.

Maps: W1-W7.

### Phase 4 - Preparation topology and retirement exception

Modify only the terminal bounded topology tests in:

- `tests/contracts/test_buddy_workflows.py`;
- `tests/contracts/test_commit11_legacy_buddy_retirement.py`.

Use exact, non-generalized tests:

1. `test_retry_4_is_the_only_required_temporary_acceptance_workflow_during_preparation`
2. `test_retry_4_preparation_keeps_normal_buddy_disabled_and_live_enabled_false`
3. `test_retry_4_is_the_only_temporary_workflow_allowed_by_legacy_buddy_retirement`
4. `test_legacy_buddy_retirement_rejects_every_other_temporary_and_legacy_workflow`

Require the retry-4 path, reject every additional temporary identity, and
explicitly preserve absence of original, retry-2, and retry-3. Keep normal
Buddy disabled and `live_enabled: false`.

Run collection, then the three contract paths with
`-k 'retry_4 or temporary_acceptance'`. Only workflow absence may fail, as
`E-WORKFLOW-ABSENT`; all negative topology cases must pass.

Maps: T1-T2.

### Phase 5 - Quality and expected-RED validation

After all test edits:

1. Invoke `test-gap-analysis` against the two production targets and all five
   bounded test files. Resolve only test-side gaps; production absence remains
   intentionally out of scope.
2. Invoke `assertion-quality` against all five test files. Replace any
   truthiness-only, self-derived, tautological, broad-exception, or
   single-observable assertion.
3. Re-open the final tests and map every checklist item to concrete
   assertions.
4. Run five-file collection. It must pass.
5. Run existing non-retry-4/non-temporary selections. They must pass.
6. Run Ruff check and format check on all five paths.
7. Run `uv build --package three-workflow-delivery-v3`.
8. Run `git --no-pager diff --check`, scope audit, and explicitly verify the
   retry-4 workflow remains absent.
9. Run the combined five-file
   `-k 'retry_4 or temporary_acceptance'` command last. It must exit nonzero
   only with:
   - `E-ADAPTER-PROFILE-ABSENT`;
   - `E-GOVERNANCE-PROFILE-ABSENT`;
   - `E-WORKFLOW-ABSENT`.
10. Record exact commands, exit codes, failing node IDs, observed messages,
    classifications, unexpected defects, and the no-production/no-external
    mutation audit in `.testagent/status.md`.

Maps: S1-S4.

### Requirement traceability

| Requirement | Planned evidence |
|---|---|
| A1 | Adapter stable-order/unique-base test |
| A2 | Governance stable-order/unique-base test |
| A3 | Both stable-order/unique-base tests |
| A4-A5 | Adapter scenario-order/qualified-identity test |
| A6-A7 | Adapter exact/global coordinate-tag test |
| A8 | Adapter resolver test |
| A9 | Adapter npm-runner test |
| A10 | Adapter controlled-suite test |
| A11 | Adapter exact-proof and bidirectional-substitution tests |
| A12 | Two retained Adapter negative tests using `.17` |
| G1-G3 | Governance exact binding test |
| G4-G6 | Governance exact/non-exact zero and forbidden-observable tests |
| G7-G8 | Governance placeholder-finalized round-trip test |
| G9 | Governance cross-profile substitution test |
| G10 | Governance historical-preservation test and non-retry-4 baseline |
| W1-W7 | Dedicated workflow module, one exact test per mechanism above |
| T1 | Exact Buddy topology and retirement exception tests |
| T2 | Disabled-Buddy/`live_enabled: false` test |
| S1-S2 | Diff/scope/no-workflow/no-external-mutation audit in status |
| S3 | Collection, unrelated baseline, and final expected-RED records |
| S4 | Per-phase failure ledger in status |

### Completion criteria

- Each allowed test path is owned by exactly one phase.
- All A/G/W/T/S checklist items have concrete evidence.
- Collection, retained historical behavior, lint, format, build, and diff
  checks pass.
- Mandatory test-gap and assertion-quality reviews have no unresolved
  test-side findings.
- The combined run is RED only for absent retry-4 profiles/workflow.
- Zero skips/xfails, zero production edits, zero workflow edits, and zero
  external mutations.

## 2026-08-13 Commit 8 Governance Observation Error Taxonomy Plan

### Strategy

**Single pass, full sequential RPI, test-only.** Research is complete above in
`.testagent/research.md`. Implementation must not modify production or build
manifests. If the current production taxonomy is incomplete, retain the tests
and report exact expected failures.

### Phase 1 - Observation taxonomy tests

Create one focused pytest module that:

1. proves exact Boolean unprotected state is the only source-protection
   condition mapped to definitive `GovernanceRejectionError`;
2. covers successfully fetched canonical/schema failures;
3. covers binding, lifetime, inventory, attestation-semantic, and digest
   inconsistency failures;
4. pins disabled/expired/changed to the existing freshness rejection type;
5. proves source/time configuration, malformed identities, and transport/API
   failures do not become Governance rejection.

Maps: G1-G7.

### Phase 2 - Concrete GitHub protection client tests

Extend the canonical GitHub REST client test file with:

1. an authoritative 404/unprotected false case;
2. permission and 5xx unknown cases;
3. network unknown;
4. malformed protection response unknown;
5. protocol/base64/JSON content-read failures that remain transport errors.

Maps: G7-G8.

### Phase 3 - Publisher/finalizer evidence

Run the existing exact-state publisher and post-marker fallback tests. Add a
test-only case only if the new definitive observation failures are not already
carried by the publisher test harness.

Maps: G9-G10.

### Phase 4 - Verification and quality gates

1. Run the narrowest focused tests immediately.
2. Run publisher/live fallback selections.
3. Run the full v3 package test suite with a fresh build.
4. Run Pyrefly, Ruff check, Ruff format check, package build, and diff check.
5. Invoke `test-gap-analysis` and `assertion-quality` after final test edits;
   address test-only findings and record exact results in `status.md`.

### Requirement-to-test plan

| Requirement | Planned evidence |
|---|---|
| G1 | `test_unprotected_ref_is_definitive_governance_rejection` |
| G2 | `test_fetched_invalid_canonical_or_schema_content_is_definitive_rejection` |
| G3 | `test_fetched_invalid_governance_semantics_are_definitive_rejection`; `test_fetched_content_digest_inconsistency_is_definitive_rejection` |
| G4 | `test_disabled_expired_and_changed_governance_remain_freshness_rejections` |
| G5 | `test_local_source_and_time_configuration_errors_are_not_governance_rejections` |
| G6 | `test_malformed_remote_identities_are_not_governance_rejections` |
| G7 | `test_transport_failures_are_not_governance_rejections`; concrete GitHub REST error tests |
| G8 | `test_ref_protection_404_is_authoritative_false`; `test_ref_protection_unknowns_raise` |
| G9 | existing publisher exact terminal-state and CLI persistence tests |
| G10 | existing post-marker malformed/missing-state fallback tests |

## 2026-08-13 Commit 8 History Admission Findings 10-13 Plan

1. Keep discovery target-filtering before artifact/job enumeration so
   well-formed different-target runs are ignored without side effects.
2. Add an explicit recognized historical schema allowlist in `release/live.py`.
   Treat non-JSON, unknown JSON schemas, and multi-file downloads as unrelated
   history; fail closed for recognized malformed, cross-Execution,
   cross-target, or cross-purpose payloads.
3. Replace arbitrary first-job selection with unique finalizer/publisher phase
   validation. Use finalizer success as the exact context-owned phase fact,
   fail duplicates/missing finalizer, and fail publisher-started-without-
   finalizer states conservatively.
4. For same-run prior attempts, enumerate current-run artifacts, require a
   separate run-level proof that the earlier attempt exists, and keep artifact
   payload run-attempt/reusable-workflow claims diagnostic-only.
5. Add focused pytest cases mapping H10-H13, including negative assertions that
   no unsupported artifact-to-job, artifact-to-attempt, or reusable-workflow
   provenance fields are emitted.

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

## 2026-08-12 Workflow Delivery v3 Commit 7 Plan Addendum

1. Preserve the existing 12-job Official simulation topology and only replace
   commit-6 observation/action boundary payloads with commit-7 observation-set
   and hypothetical-actions-report payloads.
2. Add strict immutable transport records in `release/simulation.py` rather
   than introducing live Release lineage.
3. Wire CLI:
   - `release observe-npmjs` loads current Snapshot/Decision/Adapter context
     and optional Release Artifact transport by explicit artifact ID/digest.
   - Non-success qualification emits an empty observation set without invoking
     the npmjs observer.
   - `materialize-hypothetical-actions` recomputes absent-only actions and
     otherwise emits an empty report.
   - `finalize-simulation` re-admits observation/action reports, recomputes the
     expected outcome/actions, rejects substitution, and returns success only
     for terminal success.
4. Update workflow transport and static tests for exact ID/digest raw artifact
   names, no permissions beyond `contents: read`, no credentials/auth/mutation,
   and real npmjs observation only in `observe-npmjs`.
5. Run focused release/workflow/adapters tests, then Ruff/Pyrefly and broader
   v3 validation as practical.
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

---

# Workflow Delivery v3 Commit 4 Test Plan

This append-only section preserves the completed commit-3 plan above.

## Strategy

**Single pass, sequential.** Commit 4 spans the Node smoke project and one
cohesive Python adapter boundary.

## Phase 1 - First-Slice Project Tests

- Add a `test` script using Node's built-in test runner.
- Test the public `smokeMessage` export for its exact stable value and repeated
  calls.
- Run the project test immediately.

Maps: C4-R1.

## Phase 2 - Canonical Witness and Isolated Build Adapter

- Add frozen witness/build/result records.
- Enforce canonical witness schema, field types, purpose, digest forms, and
  absence of execution identities.
- Reject placeholder/ambient versions and unsafe/duplicate declared paths.
- Stage declared inputs outside checkout, write frozen version and witness,
  preserve/add the exact package allowlist, invoke the build file directly,
  and run `npm pack --ignore-scripts`.
- Validate the tarball and compute both hashes.

Maps: C4-R2 through C4-R7, C4-R11, C4-R12.

## Phase 3 - Isolated Quality Adapters

- Add staged project-build and credential-scrubbed project-test adapters.
- Add artifact-contents validation.
- Add clean-consumer install/import validation with scripts disabled.

Maps: C4-R8 through C4-R10.

## Phase 4 - Scenario and Strict Negative Tests

- Positive end-to-end build, determinism, qualification, install/import, hash,
  and source-preservation scenarios.
- Parameterized witness/version/path/allowlist rejection.
- Parameterized tar identity/version/files/scripts/witness/entry rejection.
- Failed build/pack/test/install scenarios prove source preservation.

Maps: C4-R2 through C4-R13.

## Phase 5 - Validation and Gate

- Run narrow tests first, then full package and workspace validation.
- Run `test-gap-analysis` and `assertion-quality`; resolve in-scope findings.
- Recheck status/diff and confirm unrelated `specialized_processor.py` remains
  untouched.

Maps: C4-R14 and C4-R15.

## Commit-4 Independent Review Follow-up Plan

This append-only follow-up addresses only the three commit-4 review findings.

1. Harden `qualify_npm_artifact_contents` by parsing expectation and packed
   witness bytes as the exact Package Target Witness schema, including schema
   name, closed key sets, typed NBGV bindings, first-slice Release Unit, build
   definition, and canonical byte equality.
2. Add first-slice npm identity checks at both build manifest preparation and
   artifact qualification expectation validation; keep packed manifest identity
   validation unchanged as the artifact-content boundary.
3. Replace separate manifest/copy path handling with one safe declared-input
   resolution pass reused by source manifesting and staging copy, and call it
   before runner/toolchain execution.
4. Add focused negatives for canonical `{}`, wrong witness schema, wrong
   witness binding, non-first-slice package identity in build and qualification,
   and an outside-root symlink rejected before read/copy/runner execution.
5. Re-run the narrow Node test, narrow adapter pytest, Ruff check/format check,
   and Pyrefly.

## Commit-4 Normative Hardening Follow-up Plan

This append-only phase remains within the approved commit-4 Adapter and test
boundary.

1. Replace ambient environment copying with a minimal environment constructor
   that creates isolated `HOME`, npm user config, and npm cache state. Reuse it
   for staged build/pack, staged project tests, clean-consumer install, and
   import.
2. Bind all manifest scripts as lifecycle evidence and add hooks outside the
   former partial allowlist to the regression suite.
3. Remove caller-selected install/import output expectations, pin the exact
   first-slice value internally, and mutate packed `dist/index.js` for the
   negative scenario.
4. Reject every non-regular tar member, including explicit directory entries,
   before comparing the exact file allowlist.
5. Add frozen PNPM to `BuildRequest`, runtime verification, and result
   provenance. Remove caller control over Adapter identity/version and emit the
   pinned internal value.
6. Stage project tests outside the checkout. Replace the four-input snapshot
   helper with a complete fixture-project snapshot excluding installed
   dependencies, and add injected build, pack, test, and install failure cases.
7. Run tests immediately after the implementation, then the full requested
   validation set and mandatory pseudo-mutation/assertion-depth gate. Append
   exact results to `status.md` without rewriting prior history.

Requirement mapping:

| Requirement | Planned evidence |
|---|---|
| C4-R19 | `test_target_controlled_commands_use_minimal_isolated_environments` and staged project-test assertions. |
| C4-R20 | `test_lifecycle_evidence_binds_every_manifest_script`. |
| C4-R21 | `test_install_import_rejects_mutated_artifact_export` plus the positive install/import test with no expected-value argument. |
| C4-R22 | `test_artifact_contents_rejects_explicit_directory_member`. |
| C4-R23 | `test_build_rejects_runtime_toolchain_mismatch` PNPM case and `test_adapter_identity_is_pinned_and_not_request_forgeable`. |
| C4-R24 | Complete `_source_snapshot` assertions in success paths and `test_failure_paths_preserve_complete_source_checkout`. |
| C4-R25 | Final command/result table and before/after SHA-256 evidence in `status.md`. |

## Commit-4 Artifact Build/Pack Environment Review Follow-up Plan

1. Extend
   `test_target_controlled_commands_use_minimal_isolated_environments` to call
   `build_node_package` under the `_run` monkeypatch.
2. Require the observed command sequence to include direct artifact build and
   `npm pack --ignore-scripts`.
3. Assert exact environment key closure for every observed target-controlled
   command. For artifact/project builds and pack, additionally require the
   frozen `SOURCE_DATE_EPOCH`.
4. Pin isolated npm config/cache/XDG paths, locale, timezone, ambient-secret
   exclusion, distinct execution homes, and the shared artifact build/pack
   environment.
5. Run the narrow Adapter test, full v3 tests, Ruff check/format, Pyrefly, and
   v3 package build. Biome is not required because no JavaScript or JSON file
   is changed.

Requirement mapping:

| Requirement | Planned evidence |
|---|---|
| C4-R26 | `test_target_controlled_commands_use_minimal_isolated_environments`; exact validation results appended to `status.md`. |

<!-- BEGIN RUN: adjudicated-workflow-delivery-v3-commit4-focused-tests-plan-2026-08-10 -->

---

# Adjudicated Workflow Delivery v3 Commit 4 Focused Test Implementation Plan

## Overview

Use a targeted, sequential pytest pass for C4-R27 through C4-R32. All test
changes are confined to the existing commit-4 test file
`src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`.
The already-substantial smoke test
`src/public/lib/hcoona-release-smoke-npm/test/index.test.js` remains unchanged
and is run only as a regression check.

No production/config file may be edited. If current Adapter code does not
provide a required behavior, add and retain the focused failing test, do not
weaken or skip it, and append the exact blocker to `.testagent/status.md`.
Snapshot/request binding and future Snapshot, Evidence, Finalizer, Planner,
workflow, publication, and destination contracts remain out of scope.

The unrelated
`src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`
is excluded from every phase and command.

## Commands

- **Scoped Node regression**:
  `pnpm --dir src/public/lib/hcoona-release-smoke-npm test`
- **Focused pytest cycle**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'reads_declared_inputs_once or suffix_smuggling or concatenated_tar_archive or malformed_or_premature_streams or runtime_request_is_minimal_frozen_and_exported or quality_adapters_probe_frozen_runtime_before_operations or subprocess_sequence_is_complete_and_forbids_nbgv_or_restoration_commands or adapter_public_api_exports_closed_types_and_functions or target_controlled_commands_use_minimal_isolated_environments'`
- **Full target test file**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- **Discovery**: `uv run --python 3.13 pytest --collect-only -q`
- **Workspace Node regression**: `pnpm test` (record the known unrelated
  `hexo-renderer-asciidoc` PNPM-version failure separately if it recurs)
- **Lint**:
  `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- **Format check**:
  `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- **Type check**: `uv run --python 3.13 pyrefly check`
- **Package build**: `uv build --package three-workflow-delivery-v3`

## Phase Summary

| Phase | Focus | Test-file changes | Est. tests |
|---|---|---:|---:|
| 1 | Single-read immutable source binding | 1 new; 2 retained regressions | 3 |
| 2 | Complete gzip/tar stream rejection | 3 new; 4 retained regressions | 7 |
| 3 | Closed runtime request, probes, environment, and API | 3 new; 1 strengthened | 4 |
| 4 | Complete unfiltered subprocess evidence | 1 new | 1 |
| 5 | Relevant regression gate and append-only evidence | no test edits | command gate |

---

## Phase 1: Bind Manifest and Staging to One Immutable Read

### Overview

Establish C4-R27 first because source capture is a leaf behavior and does not
depend on the runtime-request or tar-stream changes.

### Files to Test

#### `node.py` source capture and staging

- **Source (read-only)**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py`
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- **Test Module**: existing `test_node`

**Test to add**:

1. `test_build_reads_declared_inputs_once_and_reuses_immutable_bytes`
   - Build the existing valid temporary smoke-package fixture with the explicit
     declared files `src/index.js`, `README.md`, `scripts/build.mjs`, and
     `package.json`.
   - Set `src/index.js` initially to captured bytes representing the stable
     `smokeMessage` export. Instrument `Path.read_bytes` for every resolved
     declared path; after the first `src/index.js` read returns, replace only
     the checkout file with distinct `mutated-after-capture` bytes.
   - Record the immutable `bytes` returned for each declared path and the
     staging root observed by the injected runner.
   - Assert each resolved declared path has a read count of exactly `1`.
   - Assert every `source_input_manifest` path is the declared relative path and
     every SHA-256 equals `sha256(captured_bytes).hexdigest()`, especially
     `src/index.js`; no digest may match the later mutation.
   - Assert staged `src/index.js` observed by the runner equals the captured
     bytes, not the mutated checkout bytes.
   - Assert packed `package/dist/index.js` contains the output derived from the
     captured source bytes and not `mutated-after-capture`.
   - Assert the checkout retains the deliberate mutation, proving the Adapter
     neither rereads nor restores source state.

**Existing tests to retain and run**:

2. `test_build_rejects_outside_root_symlink_before_read_copy_or_runner`
   - Input: a declared source symlink resolving outside the temporary package
     root.
   - Assert rejection occurs before any source read, staging copy, or runner
     observation.

3. `test_build_is_deterministic_and_preserves_source_checkout`
   - Input: two builds of the same valid declared-source set.
   - Assert byte-identical tarballs, matching exact-byte hashes/manifests, and
     no Adapter mutation of the source checkout.

### Narrow Command

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'build_reads_declared_inputs_once_and_reuses_immutable_bytes or build_rejects_outside_root_symlink_before_read_copy_or_runner or build_is_deterministic_and_preserves_source_checkout'`

### Blocker Rule

If the new test exposes the known second read through
`_source_input_manifest`/`_copy_declared_inputs`, retain the failure and record
the observed read count plus manifest/staged-byte mismatch. Do not edit those
production functions.

### Success Criteria

- [ ] The new test is collected under its exact name.
- [ ] One immutable byte capture is asserted for every declared file.
- [ ] Symlink rejection and deterministic checkout-preservation regressions
      remain intact.
- [ ] Any missing production behavior is recorded as a focused blocker.

---

## Phase 2: Fail Closed on the Complete npm Tar Byte Stream

### Overview

Cover C4-R28 at the parser boundary with in-memory archives. Valid artifact
hashes and sizes remain bound to the exact input bytes; tests must not
normalize, decompress/recompress, or silently ignore suffix data.

### Files to Test

#### `node.py` tarball parsing and artifact qualification

- **Source (read-only)**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py`
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- **Test Module**: existing `test_node`

**Tests to add**:

1. `test_artifact_contents_rejects_suffix_smuggling`
   - Parameter 1: the existing exact valid npm `.tgz` bytes followed by raw
     `b"RAW-SUFFIX"`.
   - Parameter 2: the same valid `.tgz` followed by a second gzip member whose
     tar payload contains regular entry `package/smuggled.txt` with bytes
     `b"second-member"`.
   - Write each complete byte sequence as the qualification input.
   - Assert `qualify_npm_artifact_contents` raises `ValueError` for both cases;
     neither the raw suffix nor second member may be ignored.

2. `test_artifact_contents_rejects_concatenated_tar_archive`
   - Input: one gzip stream whose decompressed payload is the valid npm tar
     archive concatenated with a second tar archive containing
     `package/smuggled.txt` as a regular file with bytes
     `b"second-archive"`.
   - Assert `ValueError`; parsing must not stop successfully at the first tar
     end marker.

3. `test_artifact_contents_rejects_malformed_or_premature_streams`
   - Parameterize exact invalid inputs: `b"not-a-gzip-stream"`, the valid
     `.tgz` with its final eight-byte gzip trailer removed, and a valid `.tgz`
     truncated halfway through its compressed bytes.
   - Assert `ValueError` for every input and no partial artifact manifest is
     returned.

**Existing tests to retain, strengthen only if an exact-byte assertion is
missing, and run**:

4. `test_artifact_contents_accepts_exact_tarball`
   - Input: the unchanged valid deterministic npm tarball.
   - Assert success, `size == len(input_tgz_bytes)`, and
     `sha256 == hashlib.sha256(input_tgz_bytes).hexdigest()`.

5. `test_artifact_contents_rejects_strict_negative_matrix`
   - Retain the undeclared regular-member case, concretely an archive containing
     `package/undeclared.txt`; assert `ValueError`.

6. `test_artifact_contents_rejects_explicit_directory_member`
   - Input: an otherwise valid archive with an explicit directory tar member.
   - Assert `ValueError`, preserving rejection of non-regular members.

7. `test_build_is_deterministic_and_preserves_source_checkout`
   - Assert repeated valid builds produce byte-for-byte identical `.tgz`
     inputs and matching exact-input size/SHA-256 results.

### Narrow Command

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'artifact_contents_rejects_suffix_smuggling or artifact_contents_rejects_concatenated_tar_archive or artifact_contents_rejects_malformed_or_premature_streams or artifact_contents_accepts_exact_tarball or artifact_contents_rejects_strict_negative_matrix or artifact_contents_rejects_explicit_directory_member or build_is_deterministic_and_preserves_source_checkout'`

### Blocker Rule

If `_read_tarball` accepts a raw suffix, another gzip member/archive, or a
missing trailer, retain each failing parameter and record its exact byte
construction and observed acceptance. Do not edit `_read_tarball` or artifact
qualification code.

### Success Criteria

- [ ] All three new parser tests are collected.
- [ ] Raw suffix, second gzip member, second tar archive, malformed gzip, and
      both premature-stream inputs are independently asserted.
- [ ] Valid exact-byte size/hash and deterministic-byte regressions remain.
- [ ] Existing undeclared-entry and non-regular-member rejection remains.

---

## Phase 3: Freeze the Quality Runtime and Public API

### Overview

Cover C4-R29 and C4-R32 together because runtime validation, quality-operation
signatures, package exports, and environment closure must agree. Tests should
use module-level `getattr` checks so a missing `RuntimeRequest` fails as a
focused assertion rather than aborting pytest collection.

### Files to Test

#### `node.py` runtime/environment and `adapters/__init__.py` exports

- **Sources (read-only)**:
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py`
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py`
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- **Test Module**: existing `test_node`

**Tests to add**:

1. `test_runtime_request_is_minimal_frozen_and_exported`
   - Obtain `RuntimeRequest` from the node Adapter module and instantiate
     `RuntimeRequest(node_version="v24.4.1", npm_version="11.4.2")`.
   - Assert `dataclasses.fields` is exactly
     `("node_version", "npm_version")`, both field values have exact type
     `str`, `__dict__` is absent, and assignment raises
     `dataclasses.FrozenInstanceError`.
   - Assert no PNPM, Snapshot, Evidence, Planner, run, or Attempt field exists.
   - Assert the package-level `adapters.RuntimeRequest` is the identical class.

2. `test_quality_adapters_probe_frozen_runtime_before_operations`
   - Success input: the request above, a valid staged project, and a valid
     tarball/expectation. Make the injected runner return `v24.4.1` for
     `node --version` and `11.4.2` for `npm --version`.
   - Assert project-test command order is exactly `node --version`,
     `npm --version`, then `npm test --ignore-scripts`.
   - Assert install/import command order is exactly `node --version`,
     `npm --version`,
     `npm install --ignore-scripts --no-audit --no-fund --package-lock=false
     <consumer/package.tgz>`, then the fixed Node import command.
   - Parameterize mismatch inputs: Node reports `v24.4.0`, and npm reports
     `11.4.1`. Assert `ValueError` and assert no `npm test`, `npm install`, or
     import command runs after the mismatching probe.
   - Parameterize invalid requests:
     `RuntimeRequest(node_version="", npm_version="11.4.2")`,
     `RuntimeRequest(node_version="v24.4.1", npm_version="")`, and
     `types.SimpleNamespace(node_version="v24.4.1",
     npm_version="11.4.2")`.
   - Assert empty fields raise `ValueError`, the surrogate raises `TypeError`,
     and `_run` receives no observation for all invalid requests.

3. `test_adapter_public_api_exports_closed_types_and_functions`
   - Assert package-level identity with the node module for
     `PackageTargetWitness`, `BuildRequest`, `ArtifactExpectation`,
     `ArtifactManifest`, `BuildResult`, `InstallImportResult`,
     `RuntimeRequest`, `build_node_package`, `run_node_project_build`,
     `run_node_project_tests`, `qualify_npm_artifact_contents`, and
     `qualify_npm_install_import`.
   - Assert `inspect.signature(run_node_project_tests)` has parameters exactly
     `("project_root", "request")`.
   - Assert `inspect.signature(qualify_npm_install_import)` has parameters
     exactly `("tarball", "expectation", "request")`.
   - Assert each `request` annotation resolves to the identical
     `RuntimeRequest` class.
   - Assert the RuntimeRequest fields remain exactly Node/npm and package
     exports introduce no name containing `Snapshot`, `Evidence`, `Finalizer`,
     or `Planner`.

**Existing test to strengthen**:

4. `test_target_controlled_commands_use_minimal_isolated_environments`
   - Keep the existing ambient-credential and exact environment-key closure
     inputs for build, pack, project build/test, install, and import.
   - For every observed target-controlled command, assert
     `NPM_CONFIG_GLOBALCONFIG` is present, points inside that operation's
     isolated state, exists before the command, and has exact bytes `b""`.
   - Assert it is not inherited from an ambient npm global-config value and no
     credential-bearing ambient key enters any subprocess environment.
   - Preserve the existing isolated `HOME`, npm user config/cache, XDG,
     locale/timezone, and `SOURCE_DATE_EPOCH` assertions.

### Narrow Command

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'runtime_request_is_minimal_frozen_and_exported or quality_adapters_probe_frozen_runtime_before_operations or adapter_public_api_exports_closed_types_and_functions or target_controlled_commands_use_minimal_isolated_environments'`

### Blocker Rule

Retain focused failures if `RuntimeRequest`/its export is absent, signatures
still omit `request`, probes do not precede operations, version mismatches do
not stop execution, or `NPM_CONFIG_GLOBALCONFIG` is absent/not empty. Record
the missing symbol, actual signature/sequence/environment key set, and exact
failing parameter. Do not add or export production records.

### Success Criteria

- [ ] RuntimeRequest's exact two-field frozen/slotted contract is asserted.
- [ ] Empty and surrogate requests fail before all operations.
- [ ] Node/npm match and mismatch probe behavior is covered for both quality
      adapters.
- [ ] Global npm config is isolated, empty, and part of the closed environment.
- [ ] Public identities and updated signatures are asserted without introducing
      future Snapshot/Evidence/Planner contracts.

---

## Phase 4: Assert the Complete Adapter Subprocess Sequence

### Overview

Cover C4-R30 only after Phase 3 defines the expected quality-runtime probes.
Observe `_run` without command filtering.

### Files to Test

#### `node.py` command evidence seam

- **Source (read-only)**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py`
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- **Test Module**: existing `test_node`

**Test to add**:

1. `test_subprocess_sequence_is_complete_and_forbids_nbgv_or_restoration_commands`
   - Invoke, in order, one artifact build, one project build, one project test,
     and one install/import qualification using valid temporary package,
     tarball/expectation, BuildRequest tool versions, and
     `RuntimeRequest(node_version="v24.4.1", npm_version="11.4.2")`.
   - Capture every `_run` argv tuple before making any assertions; do not
     filter probes or dynamic-path commands.
   - Assert exactly 16 observations in this order:
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
     15. `npm install --ignore-scripts --no-audit --no-fund
         --package-lock=false <consumer/package.tgz>`
     16. `node --input-type=module -e <fixed smokeMessage import script>`
   - Assert command 5's destination is the observed isolated build-output
     directory, command 15's tarball is the observed consumer copy, and command
     16's script equals the fixed import literal already asserted by
     `test_install_import_uses_tarball_and_verifies_export_and_witness`.
   - Assert each operation follows its required probes by direct index
     comparison, not by filtered subsequences.
   - Lowercase and join each argv only for the forbidden-command scan. Assert no
     command contains `nbgv-version.mjs` or `stamp`, no argv token is
     lifecycle `reset`, and no command begins with `git checkout`,
     `git restore`, `git reset`, or `git clean`.

### Narrow Command

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'subprocess_sequence_is_complete_and_forbids_nbgv_or_restoration_commands'`

### Blocker Rule

If the sequence is not exactly 16 commands because production lacks probes or
executes an extra lifecycle/restoration operation, retain the full unfiltered
failure and append actual versus expected argv tuples to status. Do not filter
the test or edit Adapter execution.

### Success Criteria

- [ ] One exact 16-command assertion covers all four Adapter operations.
- [ ] Dynamic destination, tarball, and import-script arguments are asserted
      separately.
- [ ] Probe ordering and all forbidden NBGV/restoration commands are asserted.

---

## Phase 5: Relevant Regression Gate and Append-Only Evidence

### Overview

Cover C4-R31 by validating the intended files and appending evidence. This phase
does not edit tests or production.

### Commands, in Order

1. Run the focused pytest cycle from **Commands**.
2. Run the full target test file.
3. Run the scoped Node regression.
4. Run repository-root Python collection.
5. Run Ruff check and Ruff format check with the exact three paths above.
6. Run Pyrefly.
7. Run the v3 package build.
8. Run root `pnpm test`; isolate the documented unrelated
   `hexo-renderer-asciidoc` failure from commit-4 results if it recurs.

### Append-Only Status Evidence

Append, never replace, one section to `.testagent/status.md` delimited as:

`<!-- BEGIN RUN: adjudicated-workflow-delivery-v3-commit4-focused-tests-status-2026-08-10 -->`

through the matching `END RUN` marker. Include:

- A C4-R27 through C4-R32 table mapping every ID to the exact test names in
  this plan and its pass/fail result.
- For C4-R27, per-declared-path read counts, captured/staged/manifest digest
  comparison, and retained symlink/determinism results.
- For C4-R28, one row per raw suffix, second gzip member, concatenated tar,
  malformed gzip, missing trailer, and halfway truncation probe, plus exact
  valid-input size/SHA-256 evidence.
- For C4-R29, exact RuntimeRequest fields, invalid-request outcomes,
  probe-before-operation sequences, and isolated empty global-config evidence.
- For C4-R30, the complete unfiltered 16-command argv sequence and the
  forbidden-command scan result.
- For C4-R32, package/module identity and exact signature results.
- Every command above verbatim with exit code, passed/failed/collected counts,
  and concise relevant output. A missing production behavior is recorded as
  `BLOCKED` with the exact failing test, input, expected assertion, actual
  result, and responsible production symbol; the focused test remains present
  and unskipped.
- A scoped changed-file list proving test edits were limited to
  `tests/adapters/test_node.py`, with `.testagent/plan.md` and
  `.testagent/status.md` changed only by appended run sections. Record that
  smoke `test/index.test.js` was regression-only and unchanged.
- Preserve the research-recorded unrelated-file baseline SHA-256
  `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`
  and state that the excluded `specialized_processor.py` was never opened,
  imported, tested, or edited in this plan/implementation run. Do not inspect
  or recompute it.

### Final Acceptance Mapping

| Requirement | Exact planned evidence |
|---|---|
| C4-R27 | `test_build_reads_declared_inputs_once_and_reuses_immutable_bytes`; retained outside-root symlink and deterministic-build tests. |
| C4-R28 | Three new complete-stream tests; retained exact-tarball, undeclared-member, directory-member, and deterministic-byte tests. |
| C4-R29 | `test_runtime_request_is_minimal_frozen_and_exported`, `test_quality_adapters_probe_frozen_runtime_before_operations`, and strengthened isolated-environment test. |
| C4-R30 | `test_subprocess_sequence_is_complete_and_forbids_nbgv_or_restoration_commands`. |
| C4-R31 | This append-only plan run and the specified append-only status run with exact commands, parser probes, command sequence, blockers, and scoped preservation evidence. |
| C4-R32 | `test_adapter_public_api_exports_closed_types_and_functions` plus RuntimeRequest closure/export assertions. |

### Success Criteria

- [ ] Test changes are limited to the intended existing pytest file.
- [ ] No production/config or smoke Node test file is edited.
- [ ] Every relevant command result or precise production blocker is appended
      to status.
- [ ] Prior research, plan, and status history remains byte-for-byte retained
      above the new delimiters.
- [ ] Specialized processor and future Snapshot/Evidence/Planner contracts
      remain untouched and out of scope.

<!-- END RUN: adjudicated-workflow-delivery-v3-commit4-focused-tests-plan-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-commit4-umask-padding-regressions-plan-2026-08-10 -->

# Commit-4 Cross-Umask and Tar-Padding Regression Plan

Only
`src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
receives test code. Production and smoke-package files remain read-only.

## Phase 1: Cross-umask npm pack determinism

Add
`test_build_is_deterministic_across_process_umasks_and_normalizes_modes`.

1. Pin the authoritative smoke `package.json` source version to the concrete
   value `0.0.0-placeholder`.
2. Wrap process umask changes in a context manager with `finally` restoration,
   plus an outer restoration guard, so a failure cannot leak `022` or `077`
   into another pytest.
3. Observe the real isolated staging tree immediately before real
   `npm pack --ignore-scripts`; record stage/build/declared/witness directory
   modes and README/build-output/build-script/manifest/source/witness file
   modes.
4. Run one real build under `022` and one under `077`.
5. Compare tarball bytes; SHA-256 and SHA-512 fields and exact-byte bindings;
   staged modes; packed member names/types/modes; executable-bit absence; and
   per-build umask restoration in one complete evidence object.
6. Require directories `0755`, regular files `0644`, and no intentional
   executable in this first slice.

If current production varies by umask, retain the focused failing assertion and
record both exact tar hashes/modes in status. Do not chmod production paths.

## Phase 2: Ordinary-member alignment padding

Add
`test_artifact_contents_rejects_nonzero_member_alignment_padding`.

1. Decompress the known-valid npm tarball and select ordinary, non-final
   `package/dist/index.js`.
2. Prove it has real alignment padding ending at the next member header.
3. Change exactly the first padding byte to `0xA5`.
4. Prove the final trailer remains at least two zero blocks and ordinary
   member contents still parse identically.
5. Require `qualify_npm_artifact_contents` to raise
   `ValueError("invalid npm tarball")`.

If the mutation is accepted, retain the test and identify the missing
per-member padding check in `_read_tarball`; do not edit production parsing.

## Phase 3: Narrow validation and append-only status

Run:

1. collection for the two exact tests;
2. the exact two-test pytest selection;
3. the nearest retained Adapter regressions only;
4. Ruff check and format check for the target pytest;
5. `git diff --check`.

Append exact outcomes, evidence, blockers, and changed files to
`.testagent/status.md`. Do not run unrelated workspace tests or modify/delete
retained artifact history.

## Requirement mapping

| Requirement | Planned evidence |
|---|---|
| C4-R33 | `test_build_is_deterministic_across_process_umasks_and_normalizes_modes` |
| C4-R34 | `test_artifact_contents_rejects_nonzero_member_alignment_padding` |
| C4-R35 | Exact changed-file list, narrow commands/results, and this append-only plan plus the matching status addendum |

<!-- END RUN: workflow-delivery-v3-commit4-umask-padding-regressions-plan-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-commit4-production-fixes-plan-2026-08-10 -->

# Commit-4 Production Fix Completion Plan

- [x] Normalize only the isolated npm-pack staging tree after build and before
      pack: directories `0755`, regular files `0644`, no symlink following,
      and no process umask mutation.
- [x] Validate each tar member's uncompressed alignment padding before the
      existing final trailer validation.
- [x] Run the two adjudicated regressions and the full Node Adapter pytest file.
- [x] Run scoped Ruff check, scoped Ruff format check, repository Pyrefly, and
      `git diff --check`.
- [x] Verify the smoke version remains `0.0.0-placeholder` and the excluded
      specialized processor retains its recorded byte hash.
- [x] Append completion evidence without rewriting prior `.testagent` history.

<!-- END RUN: workflow-delivery-v3-commit4-production-fixes-plan-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-hidden-physical-tar-padding-regressions-plan-2026-08-10 -->

# Hidden Physical Tar-Extension Padding Regression Plan

## Phase 1 — Physical extension fixture

- [ ] Extend only the canonical
      `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`.
- [ ] Parameterize the three directly confirmed physical variants:
      GNU long-name `L`, per-file PAX extended `x`, and PAX global `g`.
- [ ] Insert each valid physical extension immediately before the existing
      `package/dist/index.js` header so the logical member closure remains
      exactly the accepted npm package closure.
- [ ] Derive the physical record data size from its tar header and mutate
      exactly the first byte of its otherwise-zero 512-byte alignment padding.

## Phase 2 — Non-vacuous strict rejection

- [ ] Prove the inserted physical typeflag and non-empty zero padding before
      mutation.
- [ ] Prove `tarfile.getmembers()` hides the physical record while still
      returning the exact original logical member names.
- [ ] Prove the malformed archive extracts the exact original regular-file
      contents and retains an independently all-zero final trailer.
- [ ] Require the exact strict diagnostic
      `ValueError("invalid npm tarball")` from
      `qualify_npm_artifact_contents`.
- [ ] Leave any currently failing cases collected and unmarked; never
      skip/xfail or edit production.

## Phase 3 — Focused completion validation

- [ ] Run only the generated parameterized pytest node:
      `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding`.
- [ ] Invoke `test-gap-analysis` and `assertion-quality` against the final
      source/test pair if available; address test-only findings and append
      exact outcomes to status.
- [ ] Verify the smoke version is `0.0.0-placeholder`, the protected processor
      retains baseline SHA-256
      `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`,
      the Adapter production source retains baseline SHA-256
      `e1fd61081b7d7221476bbcc9971c62288dc0f21740c1627743a0b16beb322a62`,
      and no commit/VCS mutation occurred.
- [ ] Append exact results to `.testagent/status.md` without replacing prior
      evidence.

## Requirement mapping

| Requirement | Planned evidence |
|---|---|
| C4-R36 | `test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding[gnu-long-name]` |
| C4-R37 | `test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding[pax-extended]` |
| C4-R38 | `test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding[pax-global]`, completing PAX `x`/`g` coverage |
| C4-R39 | Every generated case proves hidden logical parsing and asserts the exact strict exception |
| C4-R40 | Exact header, padding, mutation, unchanged-content, and final-trailer assertions |
| C4-R41 | Canonical existing Adapter pytest file and retained helper/fixture patterns |
| C4-R42 | Protected-file post-run SHA-256 evidence |
| C4-R43 | Smoke manifest post-run version evidence |
| C4-R44 | Focused command, changed-file inventory, append-only artifacts, and final workspace status |

<!-- END RUN: workflow-delivery-v3-hidden-physical-tar-padding-regressions-plan-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-hidden-physical-tar-padding-refinement-plan-2026-08-10 -->

# Hidden Physical Tar-Extension Fixture Refinement Plan

- [x] For GNU `L`, mutate the first parser-ignored padding byte and the final
      padding byte.
- [x] For PAX `x` and `g`, retain the required first zero terminator, then
      mutate the second padding byte and the final padding byte.
- [x] Keep each mutation one-byte-only and independently prove unchanged
      logical names, contents, and final trailer before requiring the strict
      diagnostic.
- [x] Re-run only the exact generated parameterized pytest node after this
      refinement.

<!-- END RUN: workflow-delivery-v3-hidden-physical-tar-padding-refinement-plan-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-physical-tar-padding-fix-plan-2026-08-10 -->

# Physical Tar Padding Fix Plan

- [x] Add a raw 512-byte physical-record walker before logical tar extraction.
- [x] Validate zero alignment padding for every accepted physical header,
      including GNU long-name and PAX local/global extension records.
- [x] Require at least two all-zero trailer blocks and reject concatenated or
      nonzero trailing payload.
- [x] Reject PAX `size` and GNU sparse layouts that can create competing
      physical traversal interpretations.
- [x] Preserve the existing logical regular-file uniqueness, extraction,
      allowlist, manifest, witness, and member-padding checks.
- [x] Add focused GNU/PAX hidden-padding and PAX-size shrink/expand smuggling
      regressions.
- [x] Run focused and full Adapter pytest, Ruff check/format, Pyrefly,
      `git diff --check`, smoke-version verification, and protected-file hash
      verification.
- [x] Perform pseudo-mutation and assertion-depth review of the generated
      regressions.

<!-- END RUN: workflow-delivery-v3-physical-tar-padding-fix-plan-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-pax-physical-closure-regressions-plan-2026-08-10 -->

# Adjudicated PAX Physical-Closure Regression Plan

## Direct implementation

- [x] Reuse the existing physical PAX extension fixture and mutate its header
      checksum/size so `NUL` plus `0xA5` belongs to the declared payload.
- [x] Add focused local (`x`) and global (`g`) parameter cases in the canonical
      Adapter pytest file.
- [x] For each case, assert the exact physical type and declared size, the
      in-payload suffix bytes, separately zero TAR alignment padding, unchanged
      logical member names/content, and anchored qualification rejection.
- [x] Leave production source, the specialized processor, smoke package
      manifests/placeholders, and all unrelated work untouched.
- [x] Run only the exact generated pytest node. If it fails because
      qualification does not raise, retain the assertion and record the parent
      production-fix blocker.
- [x] Run focused Ruff check/format validation.
- [x] Perform pseudo-mutation, assertion-depth, and prompt-scenario review
      against the final tests; record unavailable extension tooling rather than
      weakening the review.
- [x] Append research, plan, and status completion evidence without replacing
      prior sections; do not commit or run restoration commands.

## Requirement mapping

| Requirement | Planned evidence |
|---|---|
| C4-R45 | `test_artifact_contents_rejects_nonzero_suffix_inside_declared_pax_payload[pax-local]` |
| C4-R46 | `test_artifact_contents_rejects_nonzero_suffix_inside_declared_pax_payload[pax-global]` |
| C4-R47 | Dedicated suffix-range equality plus independent zero alignment-padding assertion |
| C4-R48 | Existing Adapter fixture/helper/assertion conventions in `test_node.py` |
| C4-R49 | Exact test-node command in research/status |
| C4-R50 | Matching append-only research/plan/status sections |
| C4-R51 | Identical protected-file SHA-256 in status |
| C4-R52 | No commit command |
| C4-R53 | Unchanged `0.0.0-placeholder` verification |
| C4-R54 | Tests/evidence-only scope and explicit expected production blocker |
| C4-R55 | No checkout/restore/reset/clean or missing-file reconstruction |

<!-- END RUN: workflow-delivery-v3-pax-physical-closure-regressions-plan-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-robust-first-slice-tar-profile-plan-2026-08-10 -->

# Robust First-Slice TAR Physical Profile Plan

## Direct implementation

1. Add raw-header helpers only to the canonical Adapter pytest file:
   header checksum normalization, one-field mutation, extension insertion, and
   a well-formed zero-sized special-header constructor.
2. Add
   `test_artifact_contents_accepts_actual_frozen_npm_pack_ustar_profile` to pin
   the four real npm headers, exact type/magic/version, canonical string,
   numeric/checksum, unused-field, padding, trailer, manifest, and hash facts.
3. Add focused rejection matrices for:
   - GNU long-name `L` and long-link `K`;
   - PAX local `x`, global `g`, and Solaris extended `X`;
   - all known remaining nonordinary/special TAR types;
   - GNU/v7/wrong-version magic/version substitutions;
   - nonzero suffixes after NUL in `name`, `linkname`, `uname`, `gname`, and
     `prefix`;
   - closed unused UID/GID/reserved/device substitutions;
   - equal-value alternate or parser-elided mode/size/mtime/checksum/device
     encodings.
4. Invert the old extension premise: valid zero-padded GNU/PAX extension
   records must now be rejected. Remove the obsolete extension-padding,
   in-PAX-suffix, and PAX-size-ambiguity tests and their dead helpers.
5. Preserve all unrelated tests, including gzip/full-consumption, ordinary
   member padding, final trailer, allowlist, manifest, witness, exact hash,
   deterministic build, and quality/runtime tests.
6. Run the exact focused selection immediately. Do not weaken, skip, or xfail
   failures caused by the read-only production validator.
7. Run the complete canonical Adapter test file, scoped Ruff check/format, the
   v3 package build, `git diff --check`, smoke-version verification, and scoped
   changed-file inspection.
8. Invoke `test-gap-analysis` and `assertion-quality`, complete literal
   prompt-scenario mapping, strengthen test-only gaps, and append exact results
   to `.testagent/status.md`.

## Requirement-to-test mapping

| Requirement | Planned evidence |
|---|---|
| TAR-R1 | `test_artifact_contents_accepts_actual_frozen_npm_pack_ustar_profile` and every negative physical-profile test. |
| TAR-R2 | `test_artifact_contents_rejects_gnu_long_name_or_long_link_header`, `test_artifact_contents_rejects_pax_physical_header`, and `test_artifact_contents_rejects_every_nonordinary_tar_type`. |
| TAR-R3 | `test_artifact_contents_rejects_noncanonical_ustar_magic_or_version`. |
| TAR-R4 | Positive exact name padding plus `test_artifact_contents_rejects_nonzero_suffix_after_nul_in_fixed_string_field`. |
| TAR-R5 | Positive exact unused-field bytes, the string suffix matrix, and `test_artifact_contents_rejects_noncanonical_unused_header_field`. |
| TAR-R6 | `test_artifact_contents_rejects_noncanonical_numeric_header_encoding`. |
| TAR-R7 | Retained strict stream/padding/trailer/allowlist/manifest/witness/hash tests. |
| TAR-R8 | GNU/PAX rejection tests; removal of the former valid-extension qualification call. |
| TAR-R9 | Exact GNU `L`/`K`, PAX `x`/`g`/`X`, and five fixed-string parameter IDs. |
| TAR-R10 | Focused diff showing only obsolete extension-validator tests/helpers removed. |
| TAR-R11 | Final scoped diff/status; no production path in the intentional edit list. |
| TAR-R12 | Exact command/result and blocker table in append-only status evidence. |

## Validation commands

1. `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'actual_frozen_npm_pack_ustar_profile or gnu_long_name_or_long_link_header or pax_physical_header or every_nonordinary_tar_type or noncanonical_ustar_magic_or_version or nonzero_suffix_after_nul_in_fixed_string_field or noncanonical_unused_header_field or noncanonical_numeric_header_encoding'`
2. `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
3. `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
4. `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
5. `uv build --package three-workflow-delivery-v3`
6. `git --no-pager diff --check`

<!-- END RUN: workflow-delivery-v3-robust-first-slice-tar-profile-plan-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-robust-tar-gate-remediation-plan-2026-08-10 -->

## Gate-remediation plan: pre-completion review gaps

Scope is test-only and additive for the adjudicated robust first-slice TAR
physical profile. Production remains intentionally read-only.

## Phase A — Later-member raw profile coverage

1. Add `_tarball_with_member_header_fields` so existing first-header mutations
   can target any frozen physical member without changing production code.
2. Add `_tar_member_observables` to assert concrete member identity/index and
   parser-visible logical/structural observables before qualification rejects.
3. Add
   `test_artifact_contents_rejects_later_member_ustar_profile_mutations` with
   all later indexes:
   - index 1, `package/package.json`;
   - index 2, `package/workflow-delivery/provenance.json`;
   - index 3, `package/README.md`.
4. Parameterize representative exact-profile violations on each later member:
   - `mode-alt-terminator`: `b"000644\0 "`;
   - `noncanonical-magic`: `b"ustar "`.

Planned case count: 6.

## Phase B — Fixed string hidden-suffix breadth

Expand
`test_artifact_contents_rejects_nonzero_suffix_after_nul_in_fixed_string_field`
to cover both:

- nonzero byte immediately after the first NUL;
- nonzero final suffix byte.

Apply both positions to every relevant fixed string field:
`name`, `linkname`, `uname`, `gname`, and `prefix`.

Planned case count: 10 total (5 added immediate-after-NUL cases plus the
retained final-position cases).

## Phase C — Canonical octal/checksum encoding breadth

Keep the positive frozen npm pack profile unchanged. Extend the 19-case numeric
matrix to 34 total cases by adding meaningful equal-value alternate forms:

- `mode`: canonical-length `NUL+space` and space termination;
- `uid`/`gid`: canonical-length `NUL+space`, space termination, and immediate
  hidden nonzero byte after the first NUL in the all-zero field;
- `size`/`mtime`: canonical-length `NUL+space` and space termination;
- `devmajor`/`devminor`: canonical-length space termination in addition to the
  existing alternate terminator and hidden suffix;
- checksum: canonical-length space termination in addition to the existing
  alternate terminator and hidden suffix.

Do not duplicate parser-equivalent cases already present in
`test_artifact_contents_rejects_noncanonical_unused_header_field`.

## Phase D — Validation and reporting

Run:

1. Narrow robust-profile pytest selection.
2. Full canonical pytest file.
3. Full node adapter pytest file to preserve unrelated tests and show expected
   production-profile blockers.
4. Ruff check and format check for the edited test file.
5. Safe scoped package build using an out-of-tree `/tmp` output directory and
   no restore/sync behavior.
6. Append exact case counts, commands/results, requirement evidence,
   pre-completion review results, and blockers to `.testagent/status.md`.

<!-- END RUN: workflow-delivery-v3-robust-tar-gate-remediation-plan-2026-08-10 -->
<!-- BEGIN APPEND: workflow-delivery-v3-robust-tar-final-gate-plan-2026-08-10 -->

## Final gate follow-up plan

1. Expand the canonical fixed-string suffix matrix with a middle-position case
   for all five relevant fields.
2. Retain concrete byte, parser-observable, logical-entry, and anchored raw
   qualification assertions.
3. Re-run the focused robust-profile selection, full Adapter test file, Ruff,
   scoped package build, and final pseudo-mutation/assertion review.

<!-- END APPEND: workflow-delivery-v3-robust-tar-final-gate-plan-2026-08-10 -->

<!-- BEGIN APPEND: workflow-delivery-v3-robust-tar-adjudication-closure-plan-2026-08-10 -->

## Final adjudication closure plan

1. Expand the later-member matrix from two to eight profile mutations for each
   of the three non-first frozen npm members.
2. Expand unused-field closure with nonempty fixed-string values and
   first/middle/final reserved-byte mutations.
3. Preserve the nine focused test functions, all unrelated Adapter tests, and
   every production file.
4. Re-run focused collection, the focused robust-profile selection, the full
   Adapter test file, Ruff check/format, `git diff --check`, pseudo-mutation
   review, assertion-depth review, and prompt-scenario mapping.
5. Keep strict red assertions when read-only production still accepts a
   noncanonical physical header or rejects it only through a later logical
   check with a non-physical-profile error.

<!-- END APPEND: workflow-delivery-v3-robust-tar-adjudication-closure-plan-2026-08-10 -->

<!-- BEGIN APPEND: workflow-delivery-v3-robust-tar-production-implementation-plan-2026-08-10 -->

## Robust TAR production implementation plan

1. Replace extension-aware physical traversal with a closed raw USTAR
   regular-file header validator.
2. Validate exact magic/version/type, canonical name NUL filling, exact unused
   fields, canonical numeric encodings, and checksum bytes/value before
   semantic TAR parsing.
3. Retain strict gzip consumption, raw size-based traversal, member padding,
   trailer closure, and all downstream logical qualification.
4. Remove the dead PAX payload validator and constants.
5. Keep synthetic regular TAR fixtures on the supported npm header profile and
   update the directory rejection boundary.
6. Preserve the unrelated specialized processor and the npm package version
   placeholder.
7. Run the scoped Node test, full Adapter pytest, Ruff check/format, repository
   Pyrefly, and `git diff --check`; append exact outcomes without committing.

<!-- END APPEND: workflow-delivery-v3-robust-tar-production-implementation-plan-2026-08-10 -->

<!-- BEGIN APPEND: workflow-delivery-v3-robust-tar-checksum-order-review-plan-2026-08-10 -->

## Raw checksum-order review follow-up

Add one canonical-encoding/wrong-value checksum test that installs a semantic
parser sentinel, then rerun the full Adapter and required validation commands.

<!-- END APPEND: workflow-delivery-v3-robust-tar-checksum-order-review-plan-2026-08-10 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit5-ci-plan-2026-08-12 -->

## Workflow Delivery v3 Commit 5 Test Plan

1. Pin Candidate, Plan, Evidence, lane-result, and Decision admission to the
   exact current purpose, target, producer, run, and attempt.
2. Cover blocked, repository-only, complete first-slice, manual, empty-diff,
   failure, missing-work, and broad-control SLO scenarios.
3. Verify the workflow's events, permissions, concurrency, exact DAG, pinned
   actions, artifact bindings, static lanes, and always-run stable Finalizer.
4. Cover every cataloged dependency surface, exact exception, near miss,
   Windows form, malformed failure, deterministic output, and HK trigger parity.
5. Run focused tests, full v3 tests, repository gates, pseudo-mutation review,
   assertion-depth review, and independent GPT-5.6 Sol review.

<!-- END APPEND: workflow-delivery-v3-commit5-ci-plan-2026-08-12 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit6-release-core-plan -->

## Workflow Delivery v3 Commit 6 Release Core Test Plan

1. Add reusable artifact transport/content identities and strict immutable
   Release records with canonical documents and digests.
2. Add exact Repository Model JSON deserialization/admission and reject
   malformed, unknown, tampered, prior-context, and forged admitted wrappers.
3. Normalize the fixed Official simulation Intent, derive Simulation Binding
   only from admitted current inputs, and plan the exact first-slice npm
   Qualification Snapshot.
4. Wrap the four Node Adapter operations, form exact current-attempt Evidence
   and Release Artifact records, and finalize complete/failure/missing/
   duplicate/substituted sets.
5. Add synthetic absent/exact observation contracts, absent-only hypothetical
   actions, guarded live Publication Snapshot materialization, and the
   commit-6 unsupported-observation Simulation Outcome.
6. Validate with the two focused commit-6 files, Repository Model compiler
   tests, the full package, Ruff check/format, Pyrefly, and diff integrity.
7. Review every explicit requirement against named tests; keep remote
   observation, live eligibility/Attempt creation, workflows, authority, and
   mutation out of scope.

<!-- END APPEND: workflow-delivery-v3-commit6-release-core-plan -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit6-core-correction-plan -->

## Workflow Delivery v3 Commit 6 Core Correction Plan

1. Extend Intent schema/normalization and add same-request rerun-attempt
   contract tests plus updated canonical fixtures.
2. Add strict compiled policy value types, compiler conversion, canonical
   deserialization, ready/incomplete validation, and update all Snapshot
   builders and CI path/model checks.
3. Remove the Planner policy parameter and prove planning uses the admitted
   Snapshot policy even when repository authoring is unavailable or changed.
4. Add a frozen successful mechanical build result. Change build execution to
   return mechanics or failed Evidence, then add post-upload formation of the
   Release Artifact and satisfied build Evidence.
5. Add repository-aware deterministic transport validation and tests for
   post-mechanics binding, no rebuild, same tarball bytes, prior attempt,
   substituted name, and substituted URL.
6. Run focused commit-6, repository compiler, CI planner/scenarios, full
   package, Ruff check/format, Pyrefly, and diff checks. Perform inline
   test-gap/assertion review because subagents are prohibited.

<!-- END APPEND: workflow-delivery-v3-commit6-core-correction-plan -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit6-cli-workflow-plan -->

# Workflow Delivery v3 Commit 6 CLI and Workflow Test Plan

1. Complete closed recursive parsing for every transported Release record and
   validate purpose, workflow run, run attempt, target, and producer from
   caller-selected current bindings.
2. Implement the bounded `release` CLI surface and thin workflow mechanics,
   preserving Provider single execution and separating mechanical build bytes
   from post-upload Artifact/Evidence formation.
3. Implement the exact Official simulation DAG with propagated immutable
   artifact IDs/digests through direct dependencies only.
4. Add `test_every_transported_commit6_release_record_round_trips_closed_schema`
   and
   `test_release_transport_rejects_canonical_binding_and_substitution_attacks`
   for C6T-1 and C6T-3.
5. Add
   `test_release_cli_transports_current_attempt_through_commit6_stop_line`,
   `test_release_cli_request_id_is_rerun_stable_but_transport_is_attempt_bound`,
   and
   `test_simulation_finalizer_preserves_non_successful_qualification`
   for C6T-2, C6T-3, C6T-7, and C6T-8.
6. Add the Official workflow contract tests for exact dispatch,
   permissions/concurrency, DAG/deadlines, pins/checkouts, raw artifact
   transport, build ordering, exact-four qualification, and the commit-6 stop
   line.
7. Update CLI availability tests so all commit-6 commands expose help while
   publication and observation commands outside this scope remain rejected.
8. Run focused tests first, then the full v3 package if practical. Run Ruff
   check/format, Pyrefly, actionlint, Pkl evaluation/diff, and Git integrity.
9. Perform inline test-gap and assertion-quality review because subagents are
   prohibited; record the requirement-to-test evidence in `status.md`.

<!-- END APPEND: workflow-delivery-v3-commit6-cli-workflow-plan -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit6-raw-name-correction-plan -->

# Workflow Delivery v3 Commit 6 Raw Artifact Name Correction Plan

1. Change `release_artifact_transport_name` to return the complete `.tgz`
   physical basename.
2. After each digest-producing CLI/mechanics step, move the output to its exact
   purpose/role/run/attempt/digest basename and expose that basename as a step
   and job output.
3. Make every raw upload's `name` and `path` select the same basename
   expression. Keep `archive: false`, explicit IDs, and digest checking.
4. Propagate consumed basenames across the exact direct-dependency DAG and
   replace every fixed `.wdv3/input/*.json` reference.
5. Remove all tarball `.tgz` suffix appending from workflow paths and pass the
   exact Plan-selected basename to post-upload metadata and qualification.
6. Add workflow regression tests for v7 raw naming, every upload's physical
   identity, downstream fixed-name absence, tarball path/metadata equality,
   and the missing-suffix ReleaseArtifact rejection.
7. Run focused workflow/CLI/release tests, full v3 tests, actionlint, Ruff,
   Pyrefly, and diff integrity checks.

<!-- END APPEND: workflow-delivery-v3-commit6-raw-name-correction-plan -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit7-observer-core-plan -->

# Workflow Delivery v3 Commit 7 Observer Core Plan

## Phase 1 - npmjs adapter

- Add `adapters/npmjs.py` with an injectable `HttpTransport`.
- Implement stdlib urllib transport only; send no Authorization, cookies, npm
  configuration, credentials, OIDC, or publish-capable identity.
- Bound metadata/tarball bytes and timeout; classify HTTP, network, malformed,
  off-policy, content-encoding, redirect, size, and truncation cases.
- Reuse safe Node tarball/witness validation helpers for remote `.tgz`
  identity and witness checks.

Maps: C7-R1 through C7-R6.

## Phase 2 - record transport and finalizer

- Extend `ProjectionObservation` with strict purpose, target, and producer
  bindings.
- Add closed `ProjectionObservation` deserialization and current-binding
  admission in `release_transport`.
- Keep missing hosted observations at the commit-6 unsupported boundary until
  CLI/workflow integration; admit real observations when supplied.
- Recompute hypothetical actions in `finalize_simulation`, map all observation
  outcomes, and reject the private synthetic seam as a success shortcut.

Maps: C7-R7 through C7-R9.

## Phase 3 - tests and validation

- Add `tests/adapters/test_npmjs.py` for public-registry observation
  classification and no-network behavior.
- Add `tests/release/test_commit7_observation.py` for transported observation
  bindings and finalizer outcome mapping.
- Update adapter public export assertions.
- Run focused tests, full v3 tests where practical, Ruff, Pyrefly, format, and
  diff checks; record blockers.

Maps: C7-R1 through C7-R10.

<!-- END APPEND: workflow-delivery-v3-commit7-observer-core-plan -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-plan-2026-08-13 -->

# Workflow Delivery v3 Commit 8 Plan

1. **Contracts and history**
   - Add live Attempt binding, historical records, exhaustive admission
     snapshot, Authorization, Capability Admission Decision, Action Result,
     Receipt, capability-group bundle, and Attempt Outcome.
   - Add strict transport, caller-selected authority, exact current-attempt
     admission, and substitution tests.
2. **GitHub Packages Adapter**
   - Add bounded injectable REST/npm observation, exact tarball/witness/tag
     classification, create-only publication, post-publish readback, complete
     resource keys, conservative lock projection, response identity, and
     Receipt formation.
3. **Authorization and finalization**
   - Add immutable reviewer summary, anonymous exact-SHA Authorization
     formation, immediate Governance freshness admission, publisher-side
     repetition, exact group-bundle admission, platform termination handling,
     and whole-release finalization.
4. **Workflow integration**
   - Add the caller and reusable Attempt workflows with caller-held Execution
     concurrency, exact permission ceilings, Environment boundaries, ID-only
     artifact transport, 45-day retention, and disabled activation.
5. **Scenario tests**
   - Add commit-8 contract, history, live scenario, GitHub Packages Adapter,
     workflow topology, permission-negative, race, Receipt-loss, replay, and
     termination tests.
6. **Review and validation**
   - Run focused/full tests and managed hooks, then independent multi-angle
     GPT-5.6 Sol review with atomic TP/FP adjudication and clean follow-up
     closure before final repository validation.

<!-- END APPEND: workflow-delivery-v3-commit8-plan-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-phase2-plan-2026-08-13 -->

# Workflow Delivery v3 Commit 8 Phase 2 Test Plan

## Phase 1 — Adapter contract surface

Add `tests/adapters/test_github_packages.py` without editing production. Keep
Adapter loading dynamic so pytest can collect the complete test inventory when
the test-first production API is absent. Pin the expected public surface before
scenario execution.

Maps: C8P2-R1 through C8P2-R13.

## Phase 2 — Observation scenarios

Use a strict recording HTTP fake and focused helpers to cover:

- `test_github_packages_requests_exact_escaped_endpoints_headers_and_pages`;
- `test_github_packages_rejects_wrong_basis_before_transport`;
- `test_github_packages_classifies_all_six_closed_states`;
- `test_github_packages_exact_requires_tar_witness_and_target_tag`;
- `test_github_packages_rest_npm_inconsistency_is_blocking`; and
- `test_github_packages_redacts_token_and_rejects_cross_origin_redirect`.

Assert complete request tuples, page bounds, byte bounds, fixed API/media
headers, call counts, concrete classification values, and absent secret text in
all returned facts and diagnostics.

Maps: C8P2-R1 through C8P2-R6 and C8P2-R12 through C8P2-R13.

## Phase 3 — Publication and race scenarios

Use a recording command fake and private temporary directory to cover:

- `test_publish_uses_exact_argv_private_config_and_cleans_up`;
- `test_publish_cleans_config_after_runner_failure`;
- `test_publish_never_uses_forbidden_operations_or_credentials`;
- `test_publish_created_conflict_and_lost_response_are_distinct`;
- `test_publish_identical_and_differing_races_fail_closed`;
- `test_publish_rejects_receipt_and_response_substitution`; and
- `test_publish_preconditions_block_runner_and_network`.

Assert the entire argv and environment, exact temporary config mode/content,
cleanup in success and exception paths, empty fake call logs on precondition
failure, concrete Action Result/Receipt fields, and exact mutation uncertainty.

Maps: C8P2-R7 through C8P2-R10 and C8P2-R12 through C8P2-R13.

## Phase 4 — Existing key/group contracts and exports

Cover
`test_complete_keys_remain_distinct_while_grouping_is_conservative` using two
Buddy projections. Add an export test only if a real Adapter implementation
exists and requires registration; do not edit exports merely to make a
test-first availability check green.

Maps: C8P2-R11.

## Phase 5 — Validation and gap review

Run the focused Adapter file with pytest and Ruff. Invoke
`test-gap-analysis` and `assertion-quality`; fix test gaps with `apply_patch`.
Record a missing production Adapter as a blocker, never as a skip, and do not
add workflow or production behavior.

<!-- END APPEND: workflow-delivery-v3-commit8-phase2-plan-2026-08-13 -->

## Commit 8 Phase 2 Final Review Plan Addendum

1. Replace caller-supplied classification echoes with independent remote-fact
   matrices for all six closed states.
2. Make exact-state tests independently vary tarball SHA-512, witness digest,
   and target tag.
3. Route created/conflict/lost-response and identical/differing race scenarios
   through `classify_publish_result` instead of constructing expected records.
4. Expand Receipt validation across action, Snapshot, artifact, coordinate,
   tag, Attempt, and response identity.
5. Re-run focused pytest, existing commit-8 regressions, Ruff, formatting,
   pseudo-mutation review, and assertion-depth review. Retain the missing
   Adapter as the only focused-suite blocker.

<!-- END APPEND: workflow-delivery-v3-commit8-phase2-final-plan-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-phase34-tests-2026-08-13 -->

# Workflow Delivery v3 Commit 8 Phase 3/4 Test Plan

## Phase 3 — Live authorization and finalization scenarios

Add `tests/release/test_commit8_live_scenarios.py` with strict injected fakes
for exhaustive history and anonymous exact-SHA fetch. Pin reviewer byte/digest
bindings, freshness substitutions/restoration, diagnostic-only denial, exact
no-op, Receipt loss, pre/post Capability termination, and mixed-attempt replay.

Maps: C8P34-R2 through C8P34-R10 and C8P34-R13.

## Phase 4 — CLI and Buddy workflow contracts

Add `tests/contracts/test_buddy_workflows.py` for the caller/callee DAG,
permission ceilings, two distinct concurrency boundaries, Environments,
full-SHA action pins, raw artifact ID transport, retention, disabled state, and
later-scope negatives. Extend `tests/test_cli.py` only for the six closed live
commands, their required transport options, and the exact terminal status map.
Do not edit HK because its existing path globs already select both surfaces.

Maps: C8P34-R1 and C8P34-R11 through C8P34-R13.

## Validation and review

Run the two added files first, the focused CLI cases, existing commit-8
regressions, Ruff check/format-check, package build, Pyrefly, full package
pytest, and `git diff --check`. Invoke `test-gap-analysis` and
`assertion-quality`; record unavailable extension guidance and distinguish
test defects from missing phase-3/4 production.

<!-- END APPEND: workflow-delivery-v3-commit8-phase34-tests-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-acceptance34-audit-2026-08-14 -->
## Commit 8 final acceptance-audit plan

1. Preserve the 165-test baseline and map all 34 rows to exact assertions or
   explicit static workflow/scope evidence.
2. Add only two missing scenarios to `test_commit8_live_scenarios.py`:
   successful approval forms exact Authorization without scheduling Capability,
   and Capability admission closes the exact non-empty action/artifact/key/group
   set.
3. Run the complete focused suite and capture the final discovery delta.
4. Invoke pseudo-mutation and assertion-quality review; apply Python/pytest
   rules inline if their extension helper remains unavailable.
5. Validate Ruff check/format-check, Pyrefly, actionlint on both workflows,
   and non-destructive Git diff/status.
6. Append the full `Requirement | Evidence` map to status. No production edit
   is planned unless a strengthened test proves a real defect.

<!-- END APPEND: workflow-delivery-v3-commit8-acceptance34-audit-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-redacted-auth-review-2026-08-13 -->
## Commit 8 redacted Authorization review plan

1. Re-read Workflow Delivery v3 handoff and the two reported modules/tests.
2. Verify whether the apparent `******` values are retained evidence markers or
   live transport values by inspecting the exact source/runtime header
   expressions.
3. If a live defect is confirmed, add the smallest focused contract/scenario
   tests first and then patch only the credential/evidence split.
4. If no live defect is confirmed, make no production change and record the
   exact counter-evidence instead.
5. Run the requested focused validation gate: six-file pytest, Ruff
   check/format-check, Pyrefly, actionlint on both Buddy workflows, and
   `git diff --check`.

Production-fix section: not applicable. The live headers already use bearer
credentials and retained evidence already uses the redacted marker.

<!-- END APPEND: workflow-delivery-v3-commit8-redacted-auth-review-2026-08-13 -->
## 2026-08-13 Workflow Delivery v3 commit-8 workflow-fix test plan

1. Extend `contracts/test_buddy_workflows.py` with one auditable contract test
   per workflow requirement: exact result comparison, no-op routing, platform
   facts, receipt-first transport, failed bundle formation, final artifacts,
   offline formatting, job/check-run correlation, freshness evidence ordering,
   missing-Authorization routing, topology/retention/error propagation, and
   status artifact binding.
2. Extend `test_cli.py` with a CLI surface test requiring platform termination
   flags plus outcome, summary, step-summary, and GitHub-output status channels.
3. Reuse the existing live scenario tests
   `test_exact_noop_still_requires_authorization_and_emits_no_capability`,
   `test_platform_termination_mapping_is_capability_phase_exact`,
   `test_missing_authorization_without_denial_is_unknown_replayable_contract`,
   and finalizer closure tests as core behavioral evidence.
4. Run the focused four-file pytest command. If workflow contracts fail, do not
   modify production; preserve the failures and report the exact blocker.
5. Invoke `test-gap-analysis` and `assertion-quality`, then append results to
   `.testagent/status.md`.

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-fourth-round-governance-recheck-2026-08-13 -->
## Commit 8 fourth-round Governance publish recheck plan

### Phase 1 — Publish-path boundary regressions

Create
`tests/adapters/test_commit8_publish_governance_recheck.py` with canonical
Governance bytes and strict recording fakes. Require the publish API to accept
an injected fixed-source client and observation instant. Verify marker
admission → protected fixed-source resolve/read → `runner.run` ordering;
parameterize disabled, expired, provenance-changed, and content-changed second
observations and assert zero runner calls; assert the unchanged observation
reaches the runner exactly once. These tests map checklist items 1, 2, 3, 5,
7, and 10.

### Phase 2 — Live finalization regression

Append one explicit scenario to
`tests/release/test_commit8_live_scenarios.py` showing that failure of the
post-marker Governance recheck supplies platform-terminated plus
capability-may-have-started facts and finalizes
incomplete/possibly-mutated/post-capability-termination with
`reobserve-and-replay`. This maps checklist item 4.

### Validation and preservation

Run the focused new adapter file first, then the focused adapter/live pair,
Ruff check and format-check for changed tests, Pyrefly for the workspace, and
the full Workflow Delivery v3 pytest suite with a fresh environment/build.
Invoke `test-gap-analysis` and `assertion-quality` against the final test set;
if unavailable, retain the tests and record the equivalent concrete
pseudo-mutation/assertion review. Do not modify production, workflows, the
artifact-attempt FP finding, existing tests, or version control state. Append
status only; do not truncate or delete `.testagent` artifacts.
<!-- END APPEND: workflow-delivery-v3-commit8-fourth-round-governance-recheck-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-fifth-round-governance-terminal-state-2026-08-13 -->
# Test Implementation Plan — Commit 8 Fifth-Round Governance Terminal State

## Overview

Use a targeted strategy because all bounded production files already have
substantial surrounding coverage but are partial for this defect. Implement
the publisher-boundary terminal result first, then its CLI persistence and
strict formation, then finalization semantics. Keep the canonical diagnostic
literal `publisher-governance-recheck-blocked` exact throughout. Do not change
workflows, records unless a test proves the existing schema insufficient,
documentation, status, or unrelated tests/source.

## Commands

- **Build**:
  `uv build --package three-workflow-delivery-v3`
- **Scoped test gate**:
  `uv run --python 3.13 pytest src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit8_publish_governance_recheck.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py`
- **Harness collection**:
  `uv run --python 3.13 pytest --collect-only -q`
- **Lint**:
  `uv run ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/live.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit8_publish_governance_recheck.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py`

## Phase Summary

| Phase | Focus | Production file | Test files | Est. tests |
|---|---|---|---|---:|
| 1 | Publisher terminal result and runner boundary | `adapters/github_packages.py` | `tests/adapters/test_commit8_publish_governance_recheck.py` | 6 parameter cases + 1 success |
| 2 | Durable CLI transport and strict state admission | `cli.py` | `tests/test_cli.py`, `tests/release/test_commit8_live_scenarios.py` | 2 direct tests + 10–14 parameter cases |
| 3 | Attempt finalization | `release/live.py` | `tests/release/test_commit8_live_scenarios.py` | 2 new tests + 1 retained regression |
| 4 | Focused validation only | None | All three bounded test files | 4 commands |

---

## Phase 1: Return a Durable Publisher-Boundary Failure

### Overview

Convert only failure of the second fixed-source Governance read, after marker
admission and before npm execution, into a deferred terminal result. Do not
broaden exception handling to marker admission, malformed observation time,
runner, transport, or readback failures.

### Files to Test

#### `github_packages.py`

- **Source**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py`
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit8_publish_governance_recheck.py`
- **Test Module**: `test_commit8_publish_governance_recheck`
- **Methods/types**:
  `publish_github_packages_action`,
  `DeferredPublicationExecutionResult`

**Planned tests**:

1. `test_publish_second_governance_read_returns_terminal_no_side_effect`
   - Replace the current raised-exception expectation and parameterize
     `disabled`, `expired`, `resolved-commit-changed`, `blob-oid-changed`, and
     `content-changed`.
   - Assert the return type is `DeferredPublicationExecutionResult`.
   - Assert the complete terminal facts:
     `classification.outcome == "failed"`,
     `classification.mutation_disposition == "no-side-effect"`,
     `classification.receipt_digest is None`,
     `diagnostic_reference == "publisher-governance-recheck-blocked"`,
     `response_identity_digest is None`, `receipt is None`, and
     `observation is None`.
   - Assert `runner.calls == 0`.
   - Assert the complete event list is marker admission, protected-ref check,
     resolve, and blob read, with no `runner.run` event.

2. `test_publish_unchanged_second_governance_read_runs_exactly_once`
   - Retain the existing success-boundary regression.
   - Assert `runner.calls == 1`, the first four events are marker admission and
     the exact fixed-source reread, and event five is `runner.run`.

### Narrow Command

`uv run --python 3.13 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit8_publish_governance_recheck.py`

### Success Criteria

- [ ] Every changed second-read case returns the same exact terminal result.
- [ ] No changed second-read case invokes the runner.
- [ ] An unchanged second read still invokes the runner once.
- [ ] Unrelated publisher exceptions are not converted by a broad catch.

---

## Phase 2: Persist and Strictly Admit the Terminal State

### Overview

Persist the exact deferred failure before the publish command returns nonzero,
then admit only that exact proof and the existing exact `create-conflict`
proof during Action Result/bundle formation. Every malformed or lookalike
post-marker state must continue to fail closed.

### Files to Test

#### `cli.py`

- **Source**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
- **Test Files**:
  - `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
  - `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py`
- **Test Modules**: `test_cli`, `test_commit8_live_scenarios`
- **Methods**:
  `_release_publish_github_packages_command`,
  `_release_form_github_packages_result_command`

**Planned tests**:

1. `test_publish_cli_persists_governance_terminal_state_before_nonzero`
   in `tests/test_cli.py`
   - Stub the existing record loaders and publisher so the publisher returns
     the Phase 1 deferred result.
   - Capture `_write_output` ordering.
   - Assert status `1`.
   - Assert no Receipt output is written.
   - Assert the execution-state output is the complete document with the
     expected schema/action/action-digest/lock-group/control, `failed`,
     `no-side-effect`, null response/Receipt digests, and exact
     `publisher-governance-recheck-blocked` diagnostic.
   - Assert state persistence and `_record_outputs` occur before command
     return, and the recorded digest equals `canonical_sha256(state)`.

2. `test_post_marker_no_side_effect_terminal_state_allowlist_forms_failed_bundle`
   in `tests/release/test_commit8_live_scenarios.py`
   - Parameterize exact diagnostics
     `publisher-governance-recheck-blocked` and `create-conflict`.
   - Invoke `_release_form_github_packages_result_command` with a valid
     marker-bound state.
   - Assert status `1`.
   - Assert the Action Result preserves `outcome == "failed"`,
     `mutation-disposition == "no-side-effect"`, the selected exact
     diagnostic, null response/Receipt fields, and exact action bindings.
   - Assert the bundle contains that Action Result and has
     `completion-state == "failed"`.

3. `test_post_marker_governance_terminal_state_lookalikes_are_possibly_mutated`
   in `tests/release/test_commit8_live_scenarios.py`
   - Parameterize wrong case, prefix, suffix, similar Governance wording,
     `outcome="incomplete"`, `outcome="success"`, and mutation dispositions
     other than `no-side-effect`.
   - Assert every case returns status `1` and is replaced, not preserved:
     Action Result `incomplete`/`possibly-mutated`, null response/Receipt
     fields,
     `terminal-state-missing-or-malformed-after-start`, and bundle
     `completion-state == "incomplete"`.

4. `test_start_marker_without_valid_terminal_state_is_possibly_mutated`
   in `tests/release/test_commit8_live_scenarios.py`
   - Retain the current missing, truncated, and substituted cases.
   - Add unreadable (`Path.read_bytes` raises `OSError`), malformed JSON, and
     structurally valid generic-diagnostic cases.
   - Assert the same complete conservative fallback as test 3 for every case.

### Narrow Commands

1. `uv run --python 3.13 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py -k publish_cli_persists_governance_terminal_state_before_nonzero`
2. `uv run --python 3.13 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py -k 'post_marker_no_side_effect_terminal_state_allowlist or post_marker_governance_terminal_state_lookalikes or start_marker_without_valid_terminal_state'`

### Success Criteria

- [ ] The publisher CLI durably writes the exact failure before returning `1`.
- [ ] Exactly two post-marker failed/no-side-effect diagnostics are admitted.
- [ ] One-field diagnostic/outcome/disposition lookalikes fail closed.
- [ ] Missing and malformed states retain the existing conservative result.

---

## Phase 3: Distinguish the Durable Governance Block in Finalization

### Overview

Map the exact durable publisher Governance block to current-Attempt failure and
a new Attempt. Preserve replay for ordinary failed bundles and
reobserve-and-replay for generic post-marker uncertainty.

### Files to Test

#### `live.py`

- **Source**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/live.py`
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py`
- **Test Module**: `test_commit8_live_scenarios`
- **Method**: `finalize_attempt_outcome`

**Planned tests**:

1. `test_publisher_governance_blocked_bundle_requires_new_attempt`
   - Form an exactly bound authorizing Capability Decision and failed bundle
     whose sole Action Result is `failed`/`no-side-effect` with
     `publisher-governance-recheck-blocked` and no Receipt.
   - Assert the complete `AttemptOutcome` classification:
     `terminal_phase == "capability-blocked"`, `result == "failure"`,
     `uncertainty is False`, `possibly_mutated is False`, and
     `next_action == "new-attempt"`.
   - Assert the outcome retains the exact attempt, qualification,
     publication, authorization, capability-decision, and bundle digests, and
     has no Receipt digests.

2. `test_non_governance_failed_bundle_remains_replayable`
   - Parameterize at least exact `create-conflict` and an ordinary failed
     no-side-effect diagnostic.
   - Assert `terminal_phase == "finalized"`, `result == "failure"`,
     `uncertainty is False`, `possibly_mutated is False`, and
     `next_action == "replay"`.

3. `test_after_marker_governance_failure_requires_reobservation`
   - Retain the existing generic platform-termination regression.
   - Assert `post-capability-termination`,
     `incomplete-possibly-mutated`, `possibly_mutated is True`, and
     `reobserve-and-replay`; add `uncertainty is True` to close the full
     classification.

### Narrow Command

`uv run --python 3.13 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py -k 'publisher_governance_blocked_bundle or non_governance_failed_bundle or after_marker_governance_failure'`

### Success Criteria

- [ ] Only the exact durable Governance-blocked bundle requires a new Attempt.
- [ ] Ordinary failed bundles remain replayable.
- [ ] Missing/generic post-marker evidence remains uncertain and possibly
      mutated.

---

## Phase 4: Focused Validation

Run in this order, stopping at the first failure and fixing only the bounded
phase responsible:

1. Scoped three-file pytest gate from **Commands**.
2. Package build from **Commands**.
3. Ruff command from **Commands**.
4. Root harness collection command from **Commands**.

Do not run broad production rewrites or edit workflows/docs/status. The only
planning-turn edit is this appended section in `.testagent/plan.md`.

## Acceptance Checklist Mapping

| # | Requirement | Exact planned evidence and assertions |
|---:|---|---|
| 1 | Disabled second read | `tests/adapters/test_commit8_publish_governance_recheck.py::test_publish_second_governance_read_returns_terminal_no_side_effect[disabled]`: exact failed/no-side-effect diagnostic and zero runner calls. |
| 2 | Expired second read | Same test `[expired]`: same complete terminal result and zero runner calls. |
| 3 | Commit/blob provenance mismatch | Same test `[resolved-commit-changed]` and `[blob-oid-changed]`: same result, exact reread events, zero runner calls. |
| 4 | Content mismatch | Same test `[content-changed]`: same result and zero runner calls. |
| 5 | Unchanged Governance runs npm once | `test_publish_unchanged_second_governance_read_runs_exactly_once`: exact event prefix and one `runner.run`. |
| 6 | Publish CLI persists before nonzero | `tests/test_cli.py::test_publish_cli_persists_governance_terminal_state_before_nonzero`: complete JSON, canonical recorded digest, no Receipt, status `1`. |
| 7 | Exact diagnostic forms result/bundle | `test_post_marker_no_side_effect_terminal_state_allowlist_forms_failed_bundle[publisher-governance-recheck-blocked]`: preserved failed/no-side-effect Action Result and failed bundle. |
| 8 | `create-conflict` remains the only other proof | Same test `[create-conflict]`, plus all lookalike cases below fail closed. |
| 9 | Reject case/prefix/suffix/similar/outcome/disposition lookalikes | `test_post_marker_governance_terminal_state_lookalikes_are_possibly_mutated`: exact incomplete/possibly-mutated fallback and incomplete bundle for every parameter. |
| 10 | Missing/unreadable/malformed/truncated/substituted/generic state stays conservative | Extended `test_start_marker_without_valid_terminal_state_is_possibly_mutated`: exact fallback diagnostic and incomplete bundle. |
| 11 | Exact durable block requires a new Attempt | `test_publisher_governance_blocked_bundle_requires_new_attempt`: capability-blocked/failure/non-uncertain/not-mutated/new-attempt plus exact digest closure. |
| 12 | Preserve generic replay and uncertainty semantics | `test_non_governance_failed_bundle_remains_replayable` and retained `test_after_marker_governance_failure_requires_reobservation`: replay versus reobserve-and-replay assertions. |
| 13 | Tests, collection, build, and lint pass | Phase 4 commands, run sequentially. |
| 14 | Preserve unrelated edits and scope | Production, workflows, and docs/status remain untouched; implementation changes only the three bounded test files and `.testagent` artifacts. |

## Final Success Criteria

- [ ] Every behavioral checklist row has the named test and stated assertions.
- [ ] All three bounded source behaviors are covered by tests without editing
      production.
- [ ] Existing `records/release.py` contracts remain unchanged unless a
      concrete constructor failure blocks the planned tests.
- [ ] All Phase 4 commands pass.
- [ ] No workflow, documentation, status, or unrelated tracked file changes.
<!-- END APPEND: workflow-delivery-v3-commit8-fifth-round-governance-terminal-state-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-codeowners-tests-2026-08-14 -->
# Workflow Delivery v3 Commit 9 CODEOWNERS Test Plan

## Strategy

Single bounded test module:
`src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py`.
Implement phases sequentially and do not edit production.

## Phase 1: Ordered CODEOWNERS evaluator and inventory

- Parse non-comment CODEOWNERS rules in order.
- Match the repository's anchored patterns with GitHub-compatible `*`, `**`,
  and basename behavior needed by the governed rules.
- Resolve ownership from the final matching rule.
- Discover tracked governed files by category, plus the exact intentionally
  absent Governance path.
- Assert every requested category is represented where actual files exist.

Planned test:

- `test_every_governed_v3_surface_resolves_finally_to_hcoona`

Checklist mapping: 1, 2, 7.

## Phase 2: Descriptor discovery and negative mutation contracts

- Add synthesized nested release-unit and quality descriptor paths to the
  discovered inventory and prove both are checked.
- Remove a required rule from a complete synthetic CODEOWNERS document and
  assert evaluation reports the uncovered path.
- Append a later overriding rule and assert final-match evaluation reports the
  wrong final owner.

Planned tests:

- `test_new_descriptor_paths_are_discovered_and_owned`
- `test_missing_required_pattern_fails_coverage`
- `test_later_overriding_pattern_fails_final_match_coverage`

Checklist mapping: 3, 4, 5, 7.

## Phase 3: Buddy runtime decoupling

- Exercise arbitrary valid branch and tag refs through the existing runtime
  selected-ref validator.
- Assert the eligibility module has no CODEOWNERS input or lookup coupling.

Planned test:

- `test_arbitrary_ref_buddy_runtime_eligibility_is_not_codeowners_gated`
  (parameterized for branch and tag refs)

Checklist mapping: 6, 7.

## Phase 4: Validation and review

- Run the focused module only.
- Invoke `test-gap-analysis` and `assertion-quality`; fix actionable findings.
- Append results to `.testagent/status.md`.
- Run `git diff --check` and inspect `git diff --name-only` against the hard
  edit boundary.

Checklist mapping: 8.

## Expected result before production commit-9 patterns

Synthetic negative/final-match, descriptor, and runtime-decoupling tests should
pass. The repository-wide positive test should fail on the delivered
pre-commit-9 CODEOWNERS file, providing the intended test-first evidence
without violating the production edit boundary.
<!-- END APPEND: workflow-delivery-v3-commit9-codeowners-tests-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-independently-adjudicated-tp-fixes-2026-08-14 -->
# Workflow Delivery v3 Commit 9 Independently Adjudicated TP Fix Plan

## Overview

Use a targeted, two-file test-only strategy because the bounded suite is
currently green and the adjudicated gaps are confined to ownership-oracle
integrity and real-HK/public-Buddy cross-validation. Edit only:

- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py`
- `src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`

`.github/CODEOWNERS`, `hk.pkl`, production/runtime code, workflows, activation,
acceptance, legacy files, and all other tests remain unchanged. The earlier
expected-red statement is historical and must not guide implementation.

## Commands

- **Focused tests**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
- **Ruff check**:
  `uv run ruff check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
- **Ruff format check**:
  `uv run ruff format --check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
- **Boundary checks**: `git diff --check` and `git diff --name-only`
- **Required reviews**: invoke `test-gap-analysis` and `assertion-quality`
  against the final two-file diff, address only findings within those files,
  then rerun all gates above.

## Phase Summary

| Phase | Focus | Files | Est. Tests |
|---|---|---:|---:|
| 1 | Actual CODEOWNERS oracle and mutation strength | 1 | 5-7 |
| 2 | Real HK history cross-validation | 1 | 7-9 |
| 3 | Public CLI and actual Buddy workflow contract | 1 | 2-3 |
| 4 | Focused validation and review | 2 | Gates |

---

## Phase 1: Actual CODEOWNERS Final-Match Contract

### File to Test

- **Source oracle**: `.github/CODEOWNERS` (read-only)
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py`
- **Test Module**: `test_commit9_codeowners`

### Helper and Inventory Changes

1. Retain `CodeOwnersRule`, `_parse_rules`, `_pattern_expression`,
   `_final_owners`, `_coverage_failures`, `_workspace_paths`,
   `_descriptor_paths`, and `_governed_categories`.
2. Remove `COMPLETE_REQUIRED_RULES` and `_complete_rules`. Parse the actual
   `.github/CODEOWNERS` once into an ordered module fixture/constant and pass
   only those rules, or explicit mutations of those rules, to the evaluator.
3. Add one reusable `_governed_surface_inventory()` that combines:
   - actual tracked v3 workflows, approved actions, direct scripts,
     descriptors, package/control/catalog/tests, and
     `eng/workflow-delivery/v3/**`;
   - `.github/CODEOWNERS`, the exact absent Governance path, `hk.pkl`,
     `src/private/lib/hk/**`, `eng/scripts/hk_exec.py`,
     `eng/scripts/workflow_delivery_v3_hk.py`, `pyproject.toml`, and `uv.lock`;
   - shallow and nested future instances of both descriptor basenames;
   - a future v3 workflow, one future path in each approved action layout, and
     a future direct `eng/scripts/workflow_delivery_v3*.py` path.
4. Expose that inventory for `test_hk_trigger.py` rather than maintaining a
   second surface list.
5. Make the success predicate exact:
   `_final_owners(rules, path) == ("@hcoona",)`. Earlier matches, no match,
   replacement owners, and `("@hcoona", "@co-owner")` all fail.

### Planned Tests

1. `test_actual_codeowners_final_owner_is_exact_for_every_current_and_future_v3_surface`
   - Parse the real file in source order and evaluate every shared inventory
     path, including the absent Governance document.
   - Assert each governed category is nonempty.
   - Assert `_coverage_failures(...) == ()` and, per path, the exact final
     tuple is `("@hcoona",)`.
   - Explicitly assert the synthetic shallow/nested descriptor, workflow,
     both action-layout, and direct-script paths are present so accidental
     inventory omission cannot make the coverage assertion vacuous.

2. `test_removing_each_actual_governing_rule_exposes_its_exact_surface`
   - Parameterize the actual final rules governing workflow, both action
     layouts, direct script, both descriptor basenames, package/control,
     `eng/workflow-delivery/v3`, Governance, HK/support/helper, root Python
     inputs, and CODEOWNERS self-ownership.
   - For each case remove that rule from the actual parsed sequence, evaluate
     its named current/future exemplar, and assert the exact resulting owner
     tuple (normally `()`, or the explicitly recorded earlier-rule tuple).
   - Assert `_coverage_failures` reports exactly that `(path, final_owners)`
     failure. Do not reconstruct or append any expected production rule.

3. `test_later_replacement_owner_override_fails_exact_final_match`
   - Append an exact-path `@replacement-owner` rule to the actual rules.
   - Assert final owners equal `("@replacement-owner",)` and the sole coverage
     failure identifies that path and tuple.

4. `test_later_hcoona_coowner_override_fails_exact_final_match`
   - Append an exact-path `@hcoona @co-owner` rule.
   - Assert final owners equal `("@hcoona", "@co-owner")`, not success, and
     assert the exact single failure.

5. Retain parser-focused ordering/glob tests only where they validate syntax
   used by the real rules; rewrite any test that relies on
   `_complete_rules()` to mutate the actual parsed rule sequence instead.

### Success Criteria

- [ ] Actual ordered CODEOWNERS is the sole positive ownership oracle.
- [ ] Every current, absent-required, and synthetic future surface ends with
      exactly one owner, `@hcoona`.
- [ ] Removing broad or v3-specific rules and appending later overrides fails
      with the exact affected path and final tuple.

---

## Phase 2: Real HK Cross-Validation Across Git History

### File to Test

- **Source controls**: `hk.pkl` and
  `eng/scripts/workflow_delivery_v3_hk.py` (read-only)
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
- **Test Module**: `test_hk_trigger`

### Helper Changes

1. Reuse `HistoryChange`, `_initialize_repository`, `_apply_change`, `_commit`,
   `_helper_changed_paths`, `_helper_step_plan`, `_step_from_plan`, `_git`,
   `_write`, and the actual `_hk_executable`; do not add a glob/matcher
   substitute.
2. Import the Phase 1 shared surface inventory and convert it into one
   deterministic representative set containing all required categories.
3. Add `_batched_history_changes(kind, surfaces)` to produce one batch for
   each of `add`, `modify`, `delete`, `rename-out`, and `rename-in`.
4. Add `_restore_execution_copies(...)` that caches valid `hk.pkl` and helper
   bytes before mutation and restores safe **unstaged, uncommitted** copies
   after the requested delete/rename commit. Invoke the helper and HK only
   after restoration, while passing the unchanged committed base/head SHAs.
   This operational restoration must not alter the matcher, changed-path
   result, or asserted history range.

### Planned Tests

1. `test_real_v3_control_pytest_selects_every_codeowners_surface_for_history_kind`
   parameterized with `add`, `modify`, and `delete`
   - Create one temporary Git repository per kind, apply all representative
     surfaces as one batch, and make one history commit.
   - Assert `_helper_changed_paths(base, head)` equals the exact sorted batch.
   - Obtain the actual plan from `hk.pkl`, select `STEP_NAME`, and assert the
     real `v3-control-pytest` step is present with
     `fileCount == len(representative_surfaces)`.
   - For deletion of active HK/helper files, restore execution copies only
     after the commit and prove the committed range still reports deletion.

2. `test_real_v3_control_pytest_selects_governed_side_of_batched_rename`
   parameterized with `rename-out` and `rename-in`
   - Use one repository/run per rename kind and batch every representative
     surface.
   - Rename-out maps governed old paths to unrelated new paths; rename-in maps
     unrelated old paths to governed new paths.
   - Assert the range helper reports both old and new names for every rename.
   - Assert the actual HK plan includes the governed side and has the exact
     `fileCount == len(representative_surfaces)`, rather than counting both
     rename names or omitting the governed side.
   - Restore safe HK/helper execution copies after committing any rename that
     moves them; never execute a missing, deleted, or malformed control file.

3. Retain `test_unrelated_product_source_does_not_select_v3_control_pytest`
   (or its existing equivalent)
   - Assert an unrelated product source does not select `STEP_NAME`.

4. Retain the existing `--all` slice-validation test
   - Assert the actual HK plan still selects the v3 test slice with its exact
     current plan structure.

### Inventory Assertions

Before each five-kind matrix, assert representatives include:
`.github/CODEOWNERS`, package/control/catalog/test paths, both descriptors,
current and future workflows/actions/direct scripts, Governance, `hk.pkl`,
`src/private/lib/hk/**`, `eng/scripts/hk_exec.py`,
`eng/scripts/workflow_delivery_v3_hk.py`, `pyproject.toml`, and `uv.lock`.
This ties HK evidence to the same CODEOWNERS surfaces and prevents silent
category loss.

### Success Criteria

- [ ] Exactly five batched temporary-repository histories exercise all
      surfaces: add, modify, delete, rename-out, and rename-in.
- [ ] Changed paths come from the real range helper and selection comes from
      the actual `hk.pkl` plan.
- [ ] Operational restoration keeps HK/helper execution safe without changing
      committed-range assertions or weakening matching.
- [ ] Negative unrelated-source and `--all` contracts remain intact.

---

## Phase 3: Public Arbitrary-Ref Buddy Boundaries

### File to Test

- **Production/workflow sources**:
  `three_workflow_delivery_v3.cli.main` and
  `.github/workflows/workflow-delivery-v3-buddy-smoke.yml` (read-only)
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py`
- **Test Module**: `test_commit9_codeowners`

### Planned Tests

1. `test_public_cli_normalizes_arbitrary_buddy_branch_and_tag_without_codeowners_gate`
   parameterized with an arbitrary valid branch ref and tag ref
   - Call public `cli.main(["release", "normalize-live-request", ...])`.
   - Assert status `0` and parse the canonical emitted intent.
   - Assert exactly:
     `workflow_ref == selected_ref`, `selected_ref` equals the supplied ref,
     `workflow_sha == target`, `target` equals the supplied target,
     `event_kind == "workflow_dispatch"`, `channel == "buddy"`,
     `mode == "live"`, and `purpose == "live-release"`.
   - Install local fail-fast network sentinels for the call and assert none is
     reached. Invoke no CODEOWNERS option and assert branch/tag values are
     neither narrowed to a protected branch nor rewritten.

2. `test_actual_buddy_workflow_passes_github_ref_as_selected_ref_without_ownership_gate`
   - Parse the actual Buddy caller YAML and locate the exact
     `Normalize fixed live request` step, reusing the established
     `_document`, `_step`, and `_run` contract helpers.
   - Assert its command invokes `release normalize-live-request`.
   - Assert it passes the literal
     `--selected-ref "${GITHUB_REF}"` and emits/preserves `${GITHUB_REF}` as
     selected-ref.
   - Assert the step contains no hard-coded branch, CODEOWNERS argument/read,
     ownership gate, GitHub API query, or network ownership lookup.

### Success Criteria

- [ ] Public CLI canonical intent preserves arbitrary branch and tag refs.
- [ ] No ownership or network dependency participates in normalization.
- [ ] The actual workflow connects `GITHUB_REF` to `--selected-ref` exactly.

---

## Phase 4: Validation and Mandatory Reviews

Run sequentially, fixing only the two bounded test files:

1. Focused pytest for both files.
2. Ruff check for both files.
3. Ruff format check for both files.
4. Invoke `test-gap-analysis`; close any missing adjudicated behavior without
   broadening scope.
5. Invoke `assertion-quality`; replace shallow/tautological checks with exact
   path, owner tuple, plan, count, canonical intent, or workflow-command
   assertions.
6. Rerun focused pytest and both Ruff gates after review changes.
7. Run `git diff --check`.
8. Inspect `git diff --name-only`; confirm implementation touched only the two
   pytest files and that this planning turn appended only this plan addendum.

## Checklist-to-Test Mapping

| Requirement | Concrete evidence |
|---|---|
| Actual CODEOWNERS only; no synthetic completion | Remove `COMPLETE_REQUIRED_RULES`/`_complete_rules`; `test_actual_codeowners_final_owner_is_exact_for_every_current_and_future_v3_surface` parses the real ordered file. |
| Future descriptors/workflow/actions/script | Same positive test asserts every named synthetic surface is in the shared inventory and has final owners `("@hcoona",)`. |
| Exact final owner | Positive test plus replacement/co-owner override tests reject every tuple other than `("@hcoona",)`. |
| Broad/v3-specific rule removal | `test_removing_each_actual_governing_rule_exposes_its_exact_surface` asserts exact exemplar and fallback final tuple for every actual relevant rule. |
| Later final-match overrides | `test_later_replacement_owner_override_fails_exact_final_match` and `test_later_hcoona_coowner_override_fails_exact_final_match`. |
| CODEOWNERS/HK shared surfaces | `_governed_surface_inventory()` is consumed by the real-HK history matrix. |
| Add/modify/delete batching | `test_real_v3_control_pytest_selects_every_codeowners_surface_for_history_kind` uses one temp repository/run per kind and exact `fileCount`. |
| Rename-out/rename-in batching | `test_real_v3_control_pytest_selects_governed_side_of_batched_rename` asserts both helper names and one governed-side count. |
| Safe HK/helper operation | `_restore_execution_copies` restores uncommitted execution bytes after the history commit while helper/HK assertions stay on base-to-head. |
| Negative and `--all` HK behavior | Retained unrelated-product and slice-validation tests use the actual plan. |
| Public branch/tag canonical intent | `test_public_cli_normalizes_arbitrary_buddy_branch_and_tag_without_codeowners_gate` calls `cli.main`, asserts status/canonical fields, and trips on network access. |
| Actual workflow `GITHUB_REF` contract | `test_actual_buddy_workflow_passes_github_ref_as_selected_ref_without_ownership_gate` asserts exact command wiring and absence of CODEOWNERS/network gates. |
| Scope and quality | Focused pytest, Ruff check/format check, both mandatory reviews, `git diff --check`, and `git diff --name-only`. |

## Final Success Criteria

- [ ] All checklist rows have exact named tests, helpers, and assertions.
- [ ] The two-file focused suite remains green.
- [ ] No fake CODEOWNERS completion and no fake HK matcher remains.
- [ ] `.github/CODEOWNERS` and all production/runtime/workflow/activation/
      acceptance/legacy files are preserved.
- [ ] No file other than this append-only plan artifact is changed during this
      planning turn.
<!-- END APPEND: workflow-delivery-v3-commit9-independently-adjudicated-tp-fixes-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-tp-final-plan-correction-2026-08-14 -->
## Commit 9 TP final plan correction

Final execution is limited to the two owned pytest files. Public CLI and Buddy
workflow requirements are tested from `test_commit9_codeowners.py` by importing
the public CLI and reusing the established read-only workflow contract helpers;
no additional test file is edited.

Representative replacement-owner and co-owner overrides are parameterized over
every approved synthetic descriptor/workflow/action surface and both actual
direct-script paths. The shared inventory is then exercised through the actual
HK plan in five batched Git histories: add, modify, delete, rename-in, and
rename-out.
<!-- END APPEND: workflow-delivery-v3-commit9-tp-final-plan-correction-2026-08-14 -->

<!-- BEGIN APPEND: current-commit-10-single-pass-test-plan-2026-08-15T021630Z -->

# Current Commit-10 Single-Pass Test Implementation Plan

## Overview

Use a targeted, test-only strategy for requirement checklist items 1-8 in the
latest research section. Implement repository-contract leaves first, then
CLI/process-boundary regressions, then workflow topology contracts. Edit only
the four existing pytest files named below; do not edit production, workflow
YAML, `.testagent/research.md`, or `.testagent/status.md`. Tests remain
meaningful failures where the current implementation does not yet satisfy the
contract.

## Commands

- **Build**: `uv build --package three-workflow-delivery-v3`
- **Scoped tests**: `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
- **Full package**: `python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
- **Lint**: `uv run ruff check <changed-test-paths>`
- **Format**: `uv run ruff format --check <changed-test-paths>`
- **Workspace validation**: `uv run --python 3.13 pytest --collect-only -q`; `uv build --package three-workflow-delivery-v3`; `git diff --check`; `git diff --name-only`

## Phase Summary

| Phase | Focus | Files | Est. tests |
|---|---|---:|---:|
| 1 | Repository hygiene contracts | 1 | 8-12 |
| 2 | Cleanup, credentials, and deadlines | 2 | 12-18 |
| 3 | Workflow gates and toolchain | 1 | 8-12 |
| 4 | Integrated validation | 4 | Existing suite + new regressions |

---

## Phase 1: Repository Hygiene Leaves

### Overview

Pin filesystem and repository-policy behavior without subprocess, network, or
workflow dependencies. These tests establish exact fixtures and historical
exceptions used by later phases.

### Files to Test

#### 1. `test_hk_trigger.py`

- **Test File**: `src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
- **Test Module**: `test_hk_trigger`

**Tests and assertions**:

1. `test_acceptance_fixture_gitignore_negations_are_exact_and_narrow`
   - Maps requirement **1**.
   - Assert `git check-ignore` does not ignore exactly
     `tests/fixtures/acceptance/npm-publish-request/package.tgz`,
     `package/dist/acceptance-witness.json`, and
     `package/dist/index.js`.
   - Assert representative sibling `other.tgz` and unrelated `dist/index.js`
     remain ignored, proving there is no broad `!*.tgz` or `!dist/**` rule.

2. `test_acceptance_fixture_required_files_are_visible_to_git`
   - Maps requirement **1**.
   - Assert `capture.json`, `package.tgz`, and both required package `dist`
     files are present in `git ls-files --cached --others
     --exclude-standard`; assert the expected four-path closure exactly.

3. `test_testagent_markdown_exclusion_remains_local_to_two_steps`
   - Maps requirement **7** and retains the existing convention.
   - Assert only the two mutating/checking Markdown HK steps exclude
     `.testagent/**`; all other HK selectors remain unchanged.

4. `test_testagent_plan_update_is_append_only_against_head`
   - Maps requirement **7**.
   - Read HEAD and working-tree plan bytes without mutation; assert the
     working bytes start with the complete HEAD bytes and contain the unique
     current section marker exactly once.

5. `test_legacy_pngchunk_ztxt_ba_line_and_typos_exception_are_exact`
   - Maps requirement **8**.
   - Assert line 46 preserves the exact historical two-letter identifier.
   - Assert `.typos.toml` names the exact legacy Pngcs file path.

6. `test_typos_legacy_identifier_exceptions_are_file_specific`
   - Maps requirement **8**.
   - Assert no wildcard Pngcs/generated-code exclusion and no global
     identifier exemption exists; exact nearby legacy file entries are
     permitted.

### Narrow Command

`PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider -q src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py -k 'acceptance_fixture or testagent or legacy_pngchunk or typos_legacy'`

### Success Criteria

- [ ] Requirements 1, 7, and 8 have exact positive and negative assertions.
- [ ] No repository/configuration file is edited by the tests.
- [ ] The narrow tests collect and run locally.

---

## Phase 2: Process Cleanup, Readback Credentials, and Suite Deadlines

### Overview

Exercise the mid/top-layer acceptance seams with fake processes, clocks, and
transports. No test may invoke external npm publication or GitHub Packages.

### Files to Test

#### 1. `test_commit10_acceptance_probes.py`

- **Test File**: `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`
- **Test Module**: `test_commit10_acceptance_probes`

**Tests and assertions**:

1. `test_cleanup_signals_every_started_process_before_reaping`
   - Maps requirement **2**.
   - Use multiple stubborn fake processes; assert every `kill` event precedes
     the first `wait` event and every started PID is signaled once.

2. `test_cleanup_reaps_all_processes_with_one_absolute_deadline`
   - Maps requirement **2**.
   - Assert decreasing remaining timeout values derive from one absolute
     monotonic deadline; one expired reap cannot prevent attempts to reap the
     remaining processes.

3. `test_timeout_classification_is_immutable_after_late_process_completion`
   - Maps requirement **2**.
   - After timeout classification, complete fakes late; assert contender
     results, winner, request proof, and mutation facts remain byte-for-byte
     equal to the timeout snapshot.

4. `test_partial_startup_cleanup_signals_only_started_processes`
   - Maps requirement **2**.
   - Assert no signal/wait is attempted for an unstarted contender and all
     started contenders still follow signal-all-then-reap ordering.

5. `test_authenticated_readback_uses_dedicated_ephemeral_npm_config`
   - Maps requirement **3**.
   - Assert `npm view` receives a fresh config path with mode `0600`, exact
     GitHub registry settings, and only the dedicated token.
   - Assert the token and config content are absent from argv, retained
     output, diagnostics, and inherited environment.

6. `test_authenticated_readback_config_is_deleted_on_success`
   - Maps requirement **3**; assert the config exists during the fake call and
     is absent afterward.

7. `test_authenticated_readback_config_is_deleted_on_failure`
   - Maps requirement **3**; inject command/parse failure and assert identical
     cleanup and redaction.

8. `test_publish_proxy_config_never_contains_dedicated_readback_token`
   - Maps requirement **3**.
   - Assert the loopback proxy config contains only the dummy proxy token and
     the readback config never contains that dummy token.

9. `test_acceptance_suite_uses_one_absolute_deadline_across_scenarios`
   - Maps requirement **5**.
   - Parameterize both suite paths; assert observation, spawn, proxy, waits,
     readback, and cleanup receive monotonically decreasing budgets from one
     deadline, never a reset full timeout.

#### 2. `test_cli.py`

- **Test File**: `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
- **Test Module**: `test_cli`

**Tests and assertions**:

1. `test_acceptance_absent_create_readback_default_timeout_is_120_seconds`
   - Maps requirement **5**; omit the option and assert the parsed/effective
     timeout is exactly `120.0`.

2. `test_acceptance_exact_and_conflict_default_timeout_is_at_least_300_seconds`
   - Maps requirement **5**; omit the option and assert effective timeout is
     at least `300.0`.

3. `test_acceptance_explicit_timeout_overrides_suite_default`
   - Maps requirement **5**; parameterize both suites and assert an explicit
     value is passed unchanged to the single deadline constructor.

4. `test_acceptance_cli_does_not_reset_deadline_between_scenarios`
   - Maps requirement **5**; assert one constructor call and decreasing
     remaining budgets across all four scenarios.

### Narrow Commands

- `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py -k 'cleanup_signals or cleanup_reaps or timeout_classification_is_immutable or partial_startup_cleanup or authenticated_readback or publish_proxy_config or one_absolute_deadline'`
- `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py -k 'acceptance and timeout'`

### Success Criteria

- [ ] Requirements 2, 3, and 5 are covered at process, filesystem, argv,
      environment, retained-output, and clock boundaries.
- [ ] Tests use only injected fakes/loopback seams.
- [ ] Current production gaps remain explicit failures, not skips or xfails.

---

## Phase 3: Workflow Classification and Toolchain Contracts

### Overview

Parse the actual acceptance workflow and pin exact job/step topology. This
phase depends on the behavior contracts from phase 2 but performs no workflow
dispatch.

### Files to Test

#### 1. `test_commit10_acceptance_workflow.py`

- **Test File**: `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py`
- **Test Module**: `test_commit10_acceptance_workflow`

**Tests and assertions**:

1. `test_probe_jobs_record_upload_then_classify`
   - Maps requirement **4**.
   - For each write-probe job, assert exact step ordering: produce immutable
     record, `always()` upload, then classification gate.

2. `test_probe_classification_gate_runs_after_failed_record_upload_attempt`
   - Maps requirement **4**.
   - Assert gate conditions reference record and upload outcomes and cannot run
     before the upload attempt.

3. `test_first_probe_failure_prevents_second_mutation_job`
   - Maps requirement **4**.
   - Assert the second mutation job has a `needs` dependency and success gate
     on the first probe; no `always()` bypass may start the second mutation.

4. `test_terminal_job_fans_in_all_probe_results_and_outputs`
   - Maps requirement **4**.
   - Assert exact terminal guard
     `always() && github.run_attempt == 1`, and consumption of every dependency
     result, record output, and artifact ID/digest output, including failed or
     skipped jobs.

5. `test_terminal_evidence_upload_is_always_attempted`
   - Maps requirement **4**.
   - Assert evidence formation handles failed/skipped dependencies and its
     upload step uses `always()` after formation.

6. `test_package_writing_jobs_pin_exact_node_and_npm_versions`
   - Maps requirement **6**.
   - For both jobs, assert a full-40-character-SHA `actions/setup-node` use,
     exact Node `24.14.0`, explicit npm `11.9.0` installation, and exact
     `node --version`/`npm --version` checks before mutation.

7. `test_package_writing_setup_is_credential_free`
   - Maps requirement **6**.
   - Assert checkout uses `persist-credentials: false`; setup-node has no
     registry/token configuration; no dedicated token exists before the
     acceptance process step.

8. `test_dedicated_token_enters_only_acceptance_process_boundary`
   - Maps requirements **3** and **6**.
   - Assert the token is scoped only to the acceptance command step and is
     absent from setup, install, version-check, upload, and terminal steps.

### Narrow Command

`PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py -k 'record_upload_then_classify or classification_gate or prevents_second_mutation or terminal_job_fans_in or evidence_upload_is_always or exact_node_and_npm or setup_is_credential_free or token_enters_only'`

### Success Criteria

- [ ] Requirements 4 and 6 are mapped to exact workflow paths and conditions.
- [ ] Existing action-pin and first-attempt assertions remain intact.
- [ ] No workflow or remote state is changed.

---

## Phase 4: Integrated Test-Only Validation

### Overview

Run all four bounded test files together, then repository-convention checks.
Do not edit production or tests outside the bounded inventory in response to
failures; report failures as implementation blockers for the subsequent code
phase.

### Requirement-to-Test Matrix

| Requirement | Test files | Exact evidence |
|---|---|---|
| 1 | `tests/test_hk_trigger.py` | Exact fixture visibility and unrelated-ignore controls |
| 2 | `tests/adapters/test_commit10_acceptance_probes.py` | Signal-all ordering, shared reap deadline, immutable timeout state |
| 3 | Adapter probe + workflow contract tests | 0600 ephemeral config, token separation/redaction, narrow workflow token scope |
| 4 | `tests/contracts/test_commit10_acceptance_workflow.py` | Record/upload/gate order, sequential failure gate, terminal fan-in/upload |
| 5 | Adapter probe + CLI tests | 120/300 suite defaults, explicit override, one absolute deadline |
| 6 | Workflow contract tests | Full-SHA setup-node, Node 24.14.0, npm 11.9.0, credential-free setup |
| 7 | `tests/test_hk_trigger.py` | Existing local HK exclusion plus HEAD-prefix/unique-append assertion |
| 8 | `tests/test_hk_trigger.py` | Exact historical source line and file-specific typos exclusions |

### Validation Sequence

1. Scoped collection:
   `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
2. Scoped execution using the exact **Scoped tests** command above.
3. Narrow lint and format checks on the four test files.
4. Harness discovery:
   `uv run --python 3.13 pytest --collect-only -q`
5. Full package:
   `python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
6. Package build:
   `uv build --package three-workflow-delivery-v3`
7. Inspection-only hygiene:
   `git diff --check`;
   `git diff --name-only`;
   `git diff --numstat HEAD -- .testagent/plan.md` and require zero deletions.

### Success Criteria

- [ ] Every checklist item 1-8 maps to at least one exact named regression.
- [ ] Every test asserts concrete state, ordering, bytes, paths, versions, or
      conditions rather than truthiness alone.
- [ ] Only the four bounded pytest files are proposed implementation edits.
- [ ] No external network, package publication, remote configuration, or
      workflow dispatch occurs.
- [ ] Historical `.testagent/plan.md` bytes remain an exact HEAD prefix and
      this uniquely labeled section is append-only.

<!-- END APPEND: current-commit-10-single-pass-test-plan-2026-08-15T021630Z -->


## 2026-08-15 Commit 11 Calibration Plan Addendum

1. Replace overbroad Buddy assertions with exact AST/YAML inventories:
   retired Buddy-only test names, exact retired acceptance rows, exact removed
   live gate, exact matrix/gate retired node IDs, and exact active matrix
   evidence paths.
2. Preserve mixed and Official/CI evidence explicitly: keep the mixed R41 test
   outside `RETIRED_BUDDY_TEST_NAMES`, require named Official/CI tests, and
   require explicit Official/CI acceptance-gate node IDs.
3. Add executable/static script contracts: caller completeness must execute with
   only `official.yml`, bootstrap governance exact paths must drop only legacy
   entry workflows while preserving other entries, and actionlint path overrides
   must drop only deleted Buddy entry paths while retaining official/orchestrate
   overrides.
4. Calibrate active-doc checks to optional exact filename references only:
   references to `buddy.yml` or `release-buddy.yml` require retirement context
   when present; documents with only generic Buddy prose pass.
5. Validate with collect-only, the narrow contract module, Ruff check, and Ruff
   format check; record expected-red failures as bounded commit-11 gaps.
<!-- BEGIN APPEND: commit11-calibration-mixed-node-correction-2026-08-15 -->
## Commit 11 Calibration Mixed-Node Correction Plan

1. Keep the exact ten Buddy-only function inventory separate.
2. Require the old mixed R41 Buddy/completion pin name to be retired or split.
3. Check every exact retired matrix test node ID across the complete row set.
4. Re-run collect-only, narrow expected-red pytest, and Ruff validation.
<!-- END APPEND: commit11-calibration-mixed-node-correction-2026-08-15 -->
<!-- BEGIN APPEND: current-2026-08-17-bounded-regression-plan -->

# Test Implementation Plan

## Overview

Use a targeted three-phase regression strategy. Coverage is partial for the two
Python findings and substantial-but-missing-one-assertion for the workflow
finding. Add tests only to the existing canonical pytest modules:

- `src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py`
- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`

The repeated `github.py` target is partitioned by symbol: Phase 1 owns
`read_blob`; Phase 2 owns `_open`/`_request` only as needed by
`download_artifact`. Phase 3 owns the caller/callee YAML contract. Guard suites
for CLI forwarding and history admission are validation-only and must not be
edited unless a newly discovered, in-scope test gap requires it.

No phase may change live adapter context, Node selection, the package-owner
endpoint, artifact raw-mode/name/ID semantics, concurrency, or history
admission semantics.

## Commands

Run from the repository root.

- **Build**:
  `uv build --package three-workflow-delivery-v3`
- **Direct tests**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Package tests**:
  `python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
- **Workspace tests**:
  `uv run --python 3.13 pytest -q`
- **Collection check**:
  `uv run --python 3.13 pytest --collect-only -q`
- **Lint**:
  `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/platform/github.py src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Format check**:
  `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/platform/github.py src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`

## Phase Summary

| Phase | Focus | Source surfaces | Planned pytest additions |
|---|---|---:|---:|
| 1 | Strict Base64 after CR/LF-only unwrapping | `github.py::read_blob` | 1 success matrix plus 1 malformed matrix/boundary group |
| 2 | One credential-free artifact 302 follow | `github.py::_open`, `_request`, `download_artifact` | 7 focused tests/groups |
| 3 | Caller-path history lookup through reusable callee | Both Buddy workflow YAML files | 1 composed contract regression |

---

## Phase 1: GitHub Contents CR/LF-Unwrapped Strict Base64

### Overview

First pin the leaf-level JSON decoding behavior. The tests must distinguish
GitHub-permitted line wrapping from all other whitespace and malformed Base64.
They should use the existing injected-response pattern and make no network
request.

### Files to Test

#### `github.py::GitHubRestClient.read_blob`

- **Source**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/platform/github.py`
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py`
- **Test Module**: module-level pytest tests; retain the existing style

**Proposed tests**

1. `test_read_blob_accepts_cr_lf_wrapped_base64`
   - Parameter IDs: `cr`, `lf`, `crlf`.
   - Build a GitHub-shaped JSON response containing:
     - a fixed 40-character `sha`;
     - `encoding: "base64"`;
     - the Base64 encoding of fixed nontrivial bytes such as
       `b"policy\nbytes\x00"`, split at a fixed interior point with the selected
       line separator.
   - Assert the returned `GovernanceBlob` has exactly the supplied OID and
     original bytes.
   - Include CR/LF at more than one wrap point in at least one case so the test
     does not merely permit a trailing newline.

2. Extend the canonical parameterized malformed-contents test (or add
   `test_read_blob_rejects_non_cr_lf_or_malformed_base64` if extension would
   obscure it).
   - Named cases:
     - `invalid-alphabet`: an interior `*`;
     - `space`: otherwise-valid content with an interior ASCII space;
     - `tab`: otherwise-valid content with an interior tab;
     - `missing-padding`: e.g. `Zg=`;
     - `excess-padding`: e.g. `Zg===`.
   - Include a malformed case also containing CRLF, proving removal of CR/LF
     does not relax alphabet or padding validation.
   - Every case must raise `GitHubRestError`, not leak `binascii.Error`,
     `ValueError`, or another implementation exception.

3. Keep malformed JSON and wrong/missing protocol fields in the same canonical
   failure group.
   - Preserve the existing malformed-JSON and non-`base64` encoding cases.
   - Add a non-string `content` case only if it is not already covered.
   - Assert the stable `GitHubRestError` boundary rather than overfitting the
     complete message.

### Smallest Permitted Production Change

In `read_blob`, remove literal `"\r"` and `"\n"` characters from the JSON
`content` immediately before the existing strict
`base64.b64decode(..., validate=True)` call. Do not use `strip`, generic
whitespace removal, `split`, or `validate=False`. Preserve the current
`GitHubRestError` translation. Do not introduce a separate validator unless
the explicit excess-padding regression proves the strict decoder insufficient;
if it does, add only a local canonical-padding check.

### Sequential Implementation

1. **Step 1 — Add and run the Phase 1 regressions first.** Confirm the wrapped
   success matrix fails for the expected reason while existing malformed cases
   remain fail-closed.
2. **Step 2 — Apply only the CR/LF normalization above**, then rerun the exact
   Phase 1 nodes and the full platform module.

### Narrow Validation

```text
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py::test_read_blob_accepts_cr_lf_wrapped_base64 \
  src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py::test_read_blob_rejects_non_cr_lf_or_malformed_base64

uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py
```

### Success Criteria

- [ ] CR, LF, and CRLF-wrapped GitHub Contents payloads decode exactly.
- [ ] Spaces, tabs, invalid alphabet, and malformed/excess padding fail.
- [ ] All malformed payloads remain behind `GitHubRestError`.
- [ ] No behavior outside `read_blob` changes.

---

## Phase 2: One Credential-Stripped Artifact Archive Redirect

### Overview

Exercise the real `_open` path by monkeypatching
`urllib.request.build_opener`; do not use the constructor's byte-returning
callback for these tests. The sole new transport allowance is one initial
artifact-archive `302` from the authenticated GitHub API URL to one absolute,
off-origin HTTPS URL. The second request must be freshly constructed without
credentials, and no third request is allowed.

### Files to Test

#### `github.py::_open`, `_request`, and `download_artifact`

- **Source**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/platform/github.py`
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py`
- **Test Module**: module-level pytest tests

Keep any sequenced recording opener, context-managed byte response, redirect
error factory, and single-member ZIP builder local to this test module. Reuse
the existing `Message`/`HTTPError`/`BytesIO` conventions.

**Proposed tests**

1. `test_download_artifact_follows_one_off_origin_https_302_without_credentials`
   - Configure a sentinel token that is easy to search in headers and a
     non-default timeout such as `7.25`.
   - First opener call:
     `https://api.github.com/repos/octo/example/actions/artifacts/17/zip`;
     raise `HTTPError(302)` with
     `Location: https://objects.example.invalid/signed/archive.zip?...`.
   - Second opener call returns a valid ZIP containing exactly one member,
     `history.json`, with fixed bytes such as `b'{"runs":[]}'`.
   - Assert:
     - exactly two opener calls;
     - the first request contains the normal `Authorization` header and token;
     - the second request has no `Authorization` header;
     - neither any second-request header name nor value contains the sentinel
       token/credential;
     - both calls receive timeout `7.25`;
     - the returned artifact bytes equal the one member's bytes exactly.

2. `test_download_artifact_rejects_unsafe_or_non_off_origin_location_before_follow_up`
   - Parameterize `http://...`, `ftp://...`, a scheme-relative URL, a relative
     URL, and an `https://api.github.com/...` same-origin target.
   - The initial response is `302` with that `Location`.
   - Assert `GitHubRestError` and exactly one opener call for every case.

3. `test_list_runs_does_not_use_the_artifact_redirect_exception`
   - Make a normal JSON API operation such as `list_runs` receive an
     off-origin HTTPS `302`.
   - Assert `GitHubRestError`, one opener call, and no request to the location.
   - This is the generic-policy guard; do not test the exception through a
     generic `_request` call alone.

4. `test_download_artifact_rejects_non_302_initial_redirect`
   - Parameterize `301`, `303`, `307`, and `308`, each with an otherwise valid
     off-origin HTTPS location.
   - Assert `GitHubRestError` and one opener call.

5. `test_download_artifact_rejects_any_redirect_from_the_blob_without_a_third_request`
   - The first API response is the permitted `302`.
   - Parameterize a second redirect back to the API origin, to the same blob
     URL, and to another HTTPS blob URL; cover `302` and at least one other 3xx
     status.
   - Assert exactly two calls, no credentials on call two, `GitHubRestError`,
     and no third call. This pins the cycle/limit and extra-hop behavior.

6. `test_download_artifact_rejects_302_without_location`
   - Raise an initial `302` with no `Location`.
   - Assert `GitHubRestError` and one opener call.

7. Add two focused preservation groups:
   - `test_download_artifact_redirect_preserves_http_and_network_errors`
     parameterizes an initial non-redirect HTTP error, a blob HTTP error, and
     an `OSError` at each call stage. Assert `GitHubRestError`, retained HTTP
     status where applicable, and stage-appropriate call counts.
   - `test_download_artifact_redirect_preserves_archive_validation`
     parameterizes malformed ZIP bytes, an empty ZIP, and a multi-member ZIP
     returned after the permitted redirect. Assert `GitHubRestError`; the
     one-member success is covered by test 1.

The full existing platform module remains the canonical guard for positive
timeout validation and all pre-existing HTTP/protocol cases. The exact payload
assertion and timeout assertions above ensure the redirect branch neither
truncates output nor drops timeout forwarding. Do not add an artificial
response-byte cap: research found none in this client.

### Smallest Permitted Production Change

Add a private, default-off artifact-redirect mode through only the minimum
`download_artifact` → `_request` → `_open` call chain.

When that mode is active:

1. Validate and issue the original GitHub API request exactly as today, with
   authentication.
2. Permit only an initial `302` with a present, absolute URL whose scheme is
   exactly HTTPS and whose origin differs from `api.github.com`.
3. Build a fresh second `urllib.request.Request`; do not copy the first
   request's headers. Carry no `Authorization` header and no token-bearing
   header.
4. Issue that request once with the same timeout.
5. Treat every redirect from the second request as an error without recursion
   or a third open.

The default mode must retain generic fail-closed URL validation. Preserve
existing exception translation, ZIP cardinality, artifact identity/raw
semantics, and timeout behavior. Do not add host allowlists, response limits,
retry behavior, or a general redirect policy.

### Sequential Implementation

3. **Step 3 — Add all Phase 2 tests against the unchanged client.** Verify the
   valid off-origin artifact case fails while generic and unsafe cases already
   remain closed.
4. **Step 4 — Add the private artifact-only redirect path**, then run the
   redirect node group followed by all of `test_github.py`.

### Narrow Validation

```text
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py \
  -k "download_artifact or list_runs_does_not_use_the_artifact_redirect_exception"

uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py
```

### Success Criteria

- [ ] The initial artifact request is authenticated.
- [ ] Exactly one off-origin HTTPS request is possible and is credential-free.
- [ ] Generic, unsafe, non-302, repeated, cyclic, and extra redirects fail.
- [ ] Missing location, HTTP/network errors, timeout forwarding, and ZIP
      cardinality retain their existing behavior.
- [ ] No unrelated size limit or artifact semantic is introduced.

---

## Phase 3: Caller Workflow History Lookup Across the Reusable Callee

### Overview

Add one composed YAML regression that starts at the Buddy caller, follows its
reusable-workflow edge, locates the callee's history command, and verifies that
the command queries caller history. Keep the existing exact job/DAG and
artifact contract tests as the canonical topology guards.

### Files to Test

#### Buddy caller and reusable live-attempt callee

- **Sources**:
  - `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`
  - `.github/workflows/workflow-delivery-v3-live-attempt.yml`
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Helpers**: `CALLER`, `CALLEE`, `_document`, `_step`, `_run`,
  `EXPECTED_JOBS`

**Proposed test**

`test_history_discovery_uses_caller_path_through_reusable_live_attempt_topology`

1. Load both documents with `_document`.
2. Assert the caller's exact job set using the existing `EXPECTED_JOBS`
   convention, including `run-live-attempt`.
3. Assert
   `caller["jobs"]["run-live-attempt"]["uses"]` is exactly
   `./.github/workflows/workflow-delivery-v3-live-attempt.yml`.
4. Assert the callee's exact twelve-job set with the existing
   `EXPECTED_JOBS` convention. Do not create a competing topology list.
5. Use `_step` to select `Discover exhaustive retained execution history`
   from the callee's `admit` job and `_run` to inspect its command.
6. Assert the command is `discover-execution-history` and supplies exactly one
   `--workflow-path` value:
   `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`.
7. Assert the command does not contain
   `.github/workflows/workflow-delivery-v3-live-attempt.yml`.

The existing tests in this module must continue to enforce the exact caller
and callee DAG, artifact-ID downloads, raw upload settings, names, retention,
permissions, and error propagation. Do not duplicate those detailed
assertions in the new test.

### Smallest Permitted Production Change

Change only the callee history command's `--workflow-path` argument from
`.github/workflows/workflow-delivery-v3-live-attempt.yml` to
`.github/workflows/workflow-delivery-v3-buddy-smoke.yml`.

Do not edit the caller workflow, `release/live.py`, `release/identity.py`,
`cli.py`, or any admission logic. A failure in a guard test is a signal to
shrink/revert the implementation, not permission to broaden it.

### Sequential Implementation

5. **Step 5 — Add the composed workflow regression first.** Confirm its only
   failure is the callee-path assertion and that existing topology tests pass.
6. **Step 6 — Make the one-line callee YAML correction**, then run the contract
   module and the unchanged identity/CLI/history-admission guards.

### Narrow Validation

```text
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py

uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py \
  src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_history_admission.py \
  src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py \
  src/public/lib/three-workflow-delivery-v3/tests/test_cli.py
```

### Success Criteria

- [ ] The caller still invokes the reusable live-attempt workflow.
- [ ] The callee command queries only the caller workflow path.
- [ ] Exact job/DAG and artifact topology contracts remain green.
- [ ] Identity, CLI forwarding, and admission behavior remain unchanged.

---

## Final Requirement Cross-Check

| ID | Research checklist item | Concrete test/guard |
|---|---|---|
| B64-1 | Accept CR, LF, and CRLF wrapping | `test_read_blob_accepts_cr_lf_wrapped_base64[cr/lf/crlf]` |
| B64-2 | Remove only CR/LF before strict decode | Wrapped success test plus `space` and `tab` rejection cases |
| B64-3 | Reject alphabet, whitespace, and padding defects | `test_read_blob_rejects_non_cr_lf_or_malformed_base64` named cases |
| B64-4 | GitHub-shaped success and malformed responses | Both Phase 1 groups use `sha`/`encoding`/`content` JSON |
| B64-5 | Preserve `GitHubRestError` and protocol/JSON handling | Canonical malformed response group and full `test_github.py` |
| REDIR-1 | One artifact-only API 302 to off-origin HTTPS | Single-redirect success test |
| REDIR-2 | Initial API request authenticated | First-call header assertion in the success test |
| REDIR-3 | Blob request has no credential and occurs once | Second-call all-header scan and exact call count |
| REDIR-4 | Generic API redirects remain closed | `test_list_runs_does_not_use_the_artifact_redirect_exception` |
| REDIR-5 | Unsafe/non-HTTPS target rejected pre-request | Unsafe/non-off-origin location matrix |
| REDIR-6 | Second, extra, and non-302 redirects rejected | Initial-status matrix and no-third-request matrix |
| REDIR-7 | Preserve location/cycle/timeout/status/network/ZIP checks | Missing-location, no-third-request, transport-error, archive-validation groups; full platform module |
| REDIR-8 | Preserve size/time/error behavior without inventing a cap | Exact returned bytes and both timeout arguments; full platform module; explicit no-cap scope gate |
| FLOW-1 | Query Buddy caller workflow | New composed workflow regression's positive path assertion |
| FLOW-2 | Do not query reusable callee | Same regression's negative callee-path assertion |
| FLOW-3 | Tie caller, reusable edge, callee command, and path | All four are asserted in the single composed regression |
| FLOW-4 | Retain exact job/DAG and artifact topology | `EXPECTED_JOBS` assertions plus the complete existing Buddy contract module |
| FLOW-5 | Do not broaden history/admission semantics | Unchanged commit8 identity, CLI, and history-admission guard suites |
| EXCL-1 | No live adapter context change | Diff-scope check; no adapter production/test edit planned |
| EXCL-2 | No Node version change | Diff-scope check; existing Buddy contract module remains green |
| EXCL-3 | No package owner endpoint change | Diff-scope check; no package adapter edit planned |
| EXCL-4 | No artifact raw/name/ID change | Existing Buddy artifact contract assertions plus platform artifact tests |
| EXCL-5 | No concurrency change | Diff-scope check and complete Buddy contract module |
| EXCL-6 | Production changes minimal/direct | Expected production diff is one local Base64 normalization, one private artifact redirect path, and one YAML argument |

Any cross-check row lacking a passing named test/guard blocks completion and
returns implementation to its owning phase.

## Step 7: Mandatory Review Gate and Final Validation

Step 7 is mandatory after all three phases and before marking verification
complete.

1. Run the `test-gap-analysis` skill against the bounded implementation diff
   and this acceptance matrix. Resolve only gaps tied to B64, REDIR, or FLOW
   rows.
2. Run the `assertion-quality` skill on the two changed pytest modules. Reject
   assertion-free, truthiness-only, self-referential, or call-count-only tests;
   the critical tests must assert exact bytes, paths, URLs, headers, statuses,
   and request counts.
3. Perform prompt-scenario mapping: update/check every row above against actual
   pytest node IDs and parameter IDs. There must be no unmapped prompt
   scenario, and exclusions must have no out-of-scope diff.
4. Review the final diff. Permitted files are only the two direct pytest files
   as needed, `platform/github.py`, the callee YAML path line, and these
   `.testagent` state files. The caller YAML should remain unchanged unless a
   test-only fixture reference is strictly required; no guard-layer production
   file may change.
5. Run final relevant pytest validation:

```text
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py \
  src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_history_admission.py \
  src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py \
  src/public/lib/three-workflow-delivery-v3/tests/test_cli.py

python eng/scripts/hk_exec.py --timeout-seconds 720 \
  uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests

uv run --python 3.13 pytest --collect-only -q
uv run --python 3.13 pytest -q
```

6. Run the bounded lint, format, and build commands from **Commands**. Any
   failure must be fixed within the owning bounded phase; do not expand scope.

### Final Completion Criteria

- [ ] All requirement-matrix rows map to passing tests or explicit immutable
      scope guards.
- [ ] `test-gap-analysis`, `assertion-quality`, and prompt-scenario mapping
      complete with no unresolved finding.
- [ ] Direct, guard, package, collection, and workspace pytest validations
      pass.
- [ ] Bounded lint, format, and build pass.
- [ ] No excluded surface or semantic is changed.

<!-- END APPEND: current-2026-08-17-bounded-regression-plan -->

<!-- BEGIN APPEND: 2026-08-18-v3-artifact-transport-plan -->

## v3 artifact transport test-first plan

1. Add failing YAML scenarios in
   `test_buddy_workflows.py`:
   - `test_reviewer_archive_is_decompressed_with_transport_and_payload_bindings`
   - `test_authorization_raw_upload_materializes_exact_attempt_basename`
   - `test_mutation_marker_raw_upload_and_consumers_use_attempt_basename`
   - `test_authority_record_multidownload_is_comma_delimited_flat_merged_raw`
2. Repair only the matching workflow transport:
   - archive/decompress the three-file reviewer artifact;
   - rename/materialize Authorization and mutation-marker raw files to their
     propagated artifact basenames and update local consumers;
   - use exact comma-separated multi-ID inputs with merged flat raw downloads.
3. Preserve all other raw uploads, history admission, package endpoint, live
   adapter context, Node version, concurrency, and GitHub platform client.
4. Validate the canonical Buddy contracts plus the existing multi-file history
   archive scenarios, then run Ruff, Pyrefly, actionlint, and
   `git diff --check`.
5. Perform pseudo-mutation and assertion-depth review. Completion requires each
   listed defect mutation to be killed by an exact assertion and no
   assertion-free or trivial-only generated test.

<!-- END APPEND: 2026-08-18-v3-artifact-transport-plan -->

<!-- BEGIN APPEND: 2026-08-18-bounded-wdv3-artifact-transport-sequential-plan -->

# Sequential Implementation Plan — Bounded Workflow Delivery v3 Artifact-Transport Regression

## Overview

Use a targeted, test-first strategy for the newest bounded research inventory:
one directly untested loader and one partially covered workflow contract. Treat
the current uncommitted workflow and contract-test changes as authoritative.
Extend or append tests in the existing pytest modules; do not delete, replace,
or weaken existing scenarios.

The implementation sequence is:

1. Add direct loader regressions, observe the bounded failures, and make only
   the transport-digest normalization fix in `cli.py`.
2. Strengthen the existing live-attempt workflow scenarios for single-file raw
   uploads, producer ordering, and complete reviewer/Authorization chains.
3. Run bounded discovery, tests, linters, affected-package validation, and
   root collection without repairing unrelated failures.
4. Review bounded coverage and checklist evidence, run the mandatory
   `test-gap-analysis` and `assertion-quality` skills against the final tests,
   and append findings and handoff evidence to `.testagent/status.md`.

No sibling package, other Workflow Delivery v3 defect, governance behavior, or
unrelated workflow belongs to this plan.

## Authoritative Target Inventory

| Phase | Role | Exact path |
|---|---|---|
| 1 | Narrow production target | `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py` (`_load_mutation_marker`) |
| 1 | Canonical loader tests | `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` |
| 2 | Workflow contract tests | `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` |
| 2 | Primary asserted fixture | `.github/workflows/workflow-delivery-v3-live-attempt.yml` |
| 4 | Append-only evidence | `.testagent/status.md` |

The existing changes in
`.github/workflows/workflow-delivery-v3-live-attempt.yml` and
`tests/contracts/test_buddy_workflows.py` must be preserved. The workflow is an
asserted artifact, not an automatic edit target: change it later only if a new
bounded assertion proves that one of the listed transport requirements is
still unsatisfied.

These context fixtures remain unchanged:

- `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`
- `.github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml`

## Commands

- **Build**: No separate build command was identified for this bounded Python
  scope. The affected-package HK-equivalent test gate is the project-level
  validation gate.
- **Bounded discovery**:

```bash
uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q \
  src/public/lib/three-workflow-delivery-v3/tests/test_cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py
```

- **Scoped fix-cycle tests**:

```bash
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/test_cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py \
  -k 'load_mutation_marker or reviewer_archive_is_decompressed_with_transport_and_payload_bindings or authorization_raw_upload_materializes_exact_attempt_basename or mutation_marker_raw_upload_and_consumers_use_attempt_basename or authority_record_multidownload_is_comma_delimited_flat_merged_raw or user_item11_publisher_preflight_and_start_marker_are_separate'
```

- **Complete bounded modules**:

```bash
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/test_cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py
```

- **Affected-package gate**:

```bash
python eng/scripts/hk_exec.py --timeout-seconds 720 \
  uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests
```

- **Root harness discovery**:

```bash
uv run --python 3.13 pytest --collect-only -q
```

- **Lint and format**:

```bash
uv run --python 3.13 ruff check --force-exclude -- \
  src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/test_cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py

uv run --python 3.13 ruff format --check --force-exclude -- \
  src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/test_cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py

python eng/scripts/hk_actionlint.py \
  .github/workflows/workflow-delivery-v3-live-attempt.yml
```

- **Bounded whitespace validation**:

```bash
git --no-pager diff --check -- \
  .github/workflows/workflow-delivery-v3-live-attempt.yml \
  src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/test_cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py \
  .testagent/status.md
```

## Phase Summary

| Phase | Focus | Files | Estimated test evidence |
|---|---|---:|---:|
| 1 | Loader transport normalization and malformed rejection | 2 | 3 test functions; at least 11 collected cases |
| 2 | Raw-upload and end-to-end workflow contracts | 2 | 5 existing named scenarios strengthened |
| 3 | Narrow and workspace-relevant validation | 0 new | Collection, scoped, module, package, lint, and workflow gates |
| 4 | Coverage/checklist audit and append-only evidence | 1 | 2 mandatory skill reviews plus six requirement mappings |

---

## Phase 1: Canonical Mutation-Marker Transport Regression

### Overview

Test the leaf digest-normalization behavior through
`_load_mutation_marker` before changing production. Every case must use an
otherwise valid marker document so that the transport value is the only
variable. Preserve the existing positive artifact-ID check, marker schema,
marker-body canonical digest validation, and publication-attempt/action/lock/
preflight substitutions.

### Files

#### `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`

- Append direct module-level pytest scenarios in the existing style.
- Use `tmp_path` and canonical JSON bytes as in the representative CLI test.
- Call `cli._load_mutation_marker` directly and retain `# noqa: SLF001`.
- Do not alter or remove the existing monkeypatched passthrough scenario.

#### `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`

- Limit the production edit to `_load_mutation_marker`.
- Reuse `_normalized_digest` for the upload transport digest and carry its
  canonical `sha256:<64-lowercase-hex>` result forward.
- Preserve the loader's existing malformed-transport error contract and every
  marker-body binding check.
- Do not conflate the upload action's transport digest with the marker
  document's distinct canonical digest.

### Tests to Add

#### 1. `test_load_mutation_marker_accepts_upload_artifact_v7_bare_digest`

Arrange a valid marker, a positive artifact ID, and the native
`actions/upload-artifact` v7 digest `"a" * 64`.

Assert all of the following:

- the bare value is accepted;
- the returned transport binding is canonicalized to
  `"sha256:" + ("a" * 64)`;
- the positive artifact ID is retained;
- the returned value is the expected
  `MutationMayHaveStartedMarker`;
- attempt, first-materialized-action, lock-group, and preflight bindings still
  equal the valid marker inputs;
- the separate marker-body digest remains its own validated binding.

This is the primary regression and must fail against the pre-fix
`startswith("sha256:")` implementation.

#### 2. `test_load_mutation_marker_accepts_canonical_prefixed_digest`

Use the same otherwise valid marker and artifact ID with
`"sha256:" + ("a" * 64)`.

Assert that:

- prefixed compatibility remains intact;
- the returned transport digest is unchanged and canonical;
- all nontransport marker bindings match the same expected object as the bare
  case.

#### 3. `test_load_mutation_marker_rejects_malformed_artifact_transport`

Parameterize the transport input and artifact ID with descriptive IDs:

| Parameter ID | Artifact ID | Digest |
|---|---:|---|
| `short-bare` | positive | 63 lowercase hex characters |
| `long-bare` | positive | 65 lowercase hex characters |
| `uppercase-bare` | positive | 64 uppercase hex characters |
| `nonhex-bare` | positive | 63 valid characters plus `g` |
| `empty-digest` | positive | empty string |
| `prefix-only` | positive | `sha256:` |
| `malformed-prefixed-nonhex` | positive | `sha256:not-a-digest` |
| `zero-artifact-id` | `0` | valid 64-character bare digest |
| `negative-artifact-id` | negative | valid 64-character bare digest |

For every row:

- retain a valid marker body to isolate transport validation;
- assert the loader raises its existing malformed-transport exception with
  the exact message `mutation-start marker transport is malformed`;
- assert no partially populated marker is returned.

The `malformed-prefixed-nonhex` row specifically kills the current false
acceptance, while the bare short/long/uppercase/nonhex rows prove that only
exactly 64 lowercase hexadecimal characters are accepted without a prefix.

### Sequential Implementation Steps

1. Append the three tests and collect their node IDs.
2. Run the scoped command with `-k load_mutation_marker`.
3. Record the expected pre-fix failures: native bare rejection and malformed
   prefixed acceptance. Do not change unrelated CLI behavior.
4. Replace the prefix-only transport check with positive-ID validation plus
   `_normalized_digest` normalization, translating invalid transport input to
   the existing loader error contract as necessary.
5. Rerun all three loader tests, then the complete `test_cli.py` module.

### Success Criteria

- [ ] Exactly 64 lowercase bare hex is accepted and canonicalized.
- [ ] Already canonical prefixed input remains accepted.
- [ ] Every named malformed/nonpositive parameter is rejected.
- [ ] Distinct marker-body validation and all existing substitutions remain
      unchanged.
- [ ] No production file other than `cli.py` is edited for this defect.

---

## Phase 2: Workflow Raw-Upload and Complete-Chain Contracts

### Overview

Strengthen the existing scenario-heavy contracts against the authoritative
live-attempt YAML. Keep using `yaml.safe_load` and the existing
`_document`/`_steps`/`_step`/`_run` helpers, exact mappings and expressions,
explicit order comparisons, negative assertions, and named parameter IDs.
Extend the named scenarios below rather than replacing them or adding
unrelated topology coverage.

### Files

#### `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`

Strengthen `_raw_artifact_name` (or its narrowly equivalent helper) so every
asserted `archive: false` upload proves:

1. `archive` is exactly `false`;
2. `path` resolves to exactly one nonempty line/entry;
3. that entry does not end in `/`;
4. it contains none of the glob metacharacters `*`, `?`, or `[`;
5. its physical basename exactly equals the artifact `name`.

Do not merely compare basenames before checking entry cardinality and selector
shape. Reuse this helper at every raw upload already covered by the named
Authorization, mutation-marker, and user-item-11 scenarios.

#### `.github/workflows/workflow-delivery-v3-live-attempt.yml`

First run the stronger tests against the current authoritative workflow, which
already contains the researched archive/decompression, attempt-basename,
Base64, and comma-delimited raw-download changes. Preserve those changes.
Only if a bounded assertion exposes a remaining checklist defect may the
matching live-attempt step be adjusted. Do not edit either Buddy smoke
workflow.

### Existing Scenarios to Strengthen

#### 1. `test_reviewer_archive_is_decompressed_with_transport_and_payload_bindings`

Add exact assertions for this producer/transport order:

1. `Materialize immutable publication and reviewer payload`
2. `Materialize exact publication basenames`
3. archived `Upload reviewer artifact`
4. `Bind reviewer artifact transport to exact payloads`

Then assert every reviewer formatter hop:

- the bound reviewer file is the file Base64-encoded by the producer;
- that Base64 value is exposed as
  `materialize-publication.outputs.reviewer-formatter-input-base64`;
- the approval job output and environment preserve the exact expression;
- the authorization formatter receives/decodes that same value;
- the approval-finalizer receives/decodes that same value;
- the formatter/finalizer consume the expected reviewer payload path rather
  than a directory, alternate file, or recomputed value.

Retain the existing reviewer archive/decompression and payload-binding
assertions. A mutation that moves upload before either producer, drops any
output hop, or changes the decoded input must fail this scenario.

#### 2. `test_authorization_raw_upload_materializes_exact_attempt_basename`

Assert the complete Authorization producer and consumer chain:

- the authorization formatter initially writes
  `.wdv3/authorization.json`;
- it derives the SHA-256 attempt basename and moves the file to
  `.wdv3/${name}`;
- Base64 is read from that same renamed file, not the original generic path;
- formatter step outputs expose both exact `name` and Base64 values;
- approval job outputs preserve those exact step-output expressions;
- `approval-finalizer` explicitly needs `approval`;
- its decode/materialization step writes the same basename and occurs before
  the raw upload step;
- the raw upload passes the strengthened one-literal-file helper;
- approval-finalizer job outputs preserve the exact basename, upload artifact
  ID, and upload artifact digest expressions;
- exactly the three downstream consumer sites in the publisher and
  release-finalizer paths use that basename and transport metadata under
  `.wdv3/input`, with no fallback to a generic `authorization.json`.

Use explicit step indices for producer-before-upload ordering and exact
whole-mapping/expression assertions for every step-output/job-output hop.

#### 3. `test_mutation_marker_raw_upload_and_consumers_use_attempt_basename`

Assert that:

- the producer writes the attempt-specific literal marker path;
- the producer index is lower than the raw upload index;
- the upload path passes the one-literal-file helper and its basename equals
  the artifact name;
- upload step ID and digest outputs are preserved exactly;
- the publish command and capability-bundle consumer use the attempt basename
  and the same transport ID/digest;
- no generic marker path, multiline selector, directory, or glob is accepted.

#### 4. `test_authority_record_multidownload_is_comma_delimited_flat_merged_raw`

Retain and make exact the named-scenario whole mappings for:

- comma-delimited multi-artifact IDs;
- flat destination paths;
- merged multiple downloads;
- raw/nonarchive handling.

This scenario protects the existing authority-record transport repair while
the stronger raw-file contract is introduced; do not broaden it into unrelated
topology behavior.

#### 5. `test_user_item11_publisher_preflight_and_start_marker_are_separate`

Retain the distinction between publisher preflight and mutation-start marker.
Apply the strengthened raw-file helper wherever this scenario inspects a raw
upload, and assert the two basenames/paths remain separate rather than aliases.

### Sequential Implementation Steps

1. Strengthen the shared raw-upload helper before adding chain assertions.
2. Extend the five existing scenarios in the order listed above.
3. Run each scenario by exact `-k` name while editing, then run the complete
   scoped fix-cycle command.
4. Prefer test-only changes when the authoritative YAML already satisfies the
   assertions.
5. If and only if one assertion reveals a bounded workflow mismatch, update
   the corresponding live-attempt producer/output/consumer expression and
   rerun all five scenarios. Preserve all unrelated YAML bytes and semantics.

### Success Criteria

- [ ] Every covered raw upload is one resolved literal file, never a
      directory, glob, or multiline selector.
- [ ] Reviewer and Authorization producers precede their uploads.
- [ ] The reviewer formatter Base64 chain is asserted at every producer,
      output, environment, decode, and consumer hop.
- [ ] The Authorization basename/Base64 chain is asserted through formatter
      steps, approval outputs, finalizer decode/upload, finalizer outputs, and
      all three downstream consumers.
- [ ] Mutation-marker and authority-record transport regressions remain
      protected.
- [ ] Existing tests are preserved and only extended/appended.
- [ ] Context-only Buddy workflows remain unchanged.

---

## Phase 3: Narrow Gates and Final Relevant Workspace Validation

### Overview

Validate in widening order. A failure outside the bounded files is report-only
unless the bounded diff directly caused it; do not expand the implementation
inventory to make unrelated package or workspace failures green.

### Validation Sequence

1. Run bounded `--collect-only` for `test_cli.py` and
   `test_buddy_workflows.py`. Record all three new loader test names and every
   malformed parameter ID.
2. Run the scoped fix-cycle command containing the loader and five named
   workflow scenarios.
3. Run both complete bounded test modules.
4. Run Ruff check and Ruff format check on exactly `cli.py`, `test_cli.py`,
   and `test_buddy_workflows.py`.
5. Run `hk_actionlint.py` on exactly
   `.github/workflows/workflow-delivery-v3-live-attempt.yml`.
6. Run bounded `git diff --check`.
7. Run the affected-package HK-equivalent test gate for all
   `three-workflow-delivery-v3` tests.
8. Run root pytest collection as the harness-equivalent workspace discovery
   check.
9. After any bounded correction, rerun its owning narrow command and then all
   subsequent gates.

For each command, retain the exact command text, exit status, and
pass/fail/deselected/collected counts for the final status append. Record an
unrelated failure as bounded report-only evidence rather than editing another
file.

### Bounded Coverage and Checklist Review

Perform an explicit scenario-to-node review rather than inventing an
unresearched coverage command:

- `_load_mutation_marker`, previously direct-untested, must map to both
  acceptance tests and every malformed parameter node.
- Each raw upload assertion must be reachable from at least one of the named
  workflow scenarios.
- Each reviewer and Authorization data-flow arrow in Phase 2 must map to an
  exact assertion in a collected test.
- Check the mutation-kill matrix:
  - reject a return to prefix-only transport validation;
  - reject acceptance of `sha256:not-a-digest`;
  - reject a second raw path line, trailing slash, or `*`/`?`/`[` selector;
  - reject moving a producer after its upload;
  - reject removal or rewiring of any reviewer Base64 hop;
  - reject removal or rewiring of any Authorization name/Base64/ID/digest hop.
- Confirm the two context-only workflows and all other v3 files have no
  implementation diff.

### Success Criteria

- [ ] Bounded collection and scoped tests pass with all expected node IDs.
- [ ] Both complete bounded modules pass.
- [ ] Ruff, Ruff format, actionlint, and diff checks pass.
- [ ] The affected-package gate and root collection pass, or any unrelated
      failure is reported without scope expansion.
- [ ] Every bounded target and defect mutation has concrete test evidence.

---

## Phase 4: Mandatory Quality Reviews and Append-Only Handoff Evidence

### Overview

Run quality analysis only after the tests and bounded implementation are final.
Any in-scope finding returns to its owning phase; after correction, rerun all
affected validation and both reviews.

### Mandatory Reviews

1. Invoke `test-gap-analysis` against:
   - the final bounded implementation diff;
   - the newest research inventory;
   - acceptance checklist items 1–6;
   - the collected loader parameter IDs and five named workflow scenarios.
2. Resolve only findings tied to the loader transport or listed workflow
   chains. Report unrelated suggestions without broadening scope.
3. Invoke `assertion-quality` on the final changed tests in:
   - `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`;
   - `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`.
4. Reject assertion-free, truthiness-only, self-referential, or
   call-count-only evidence. Critical assertions must compare exact digest
   values, IDs, paths, basenames, mappings, expressions, ordering, outputs,
   and consumer inputs.
5. If either review causes an in-scope test edit, rerun Phase 3 and then rerun
   both skills until no bounded finding remains.

### Append-Only Status Artifact

Append one clearly delimited, timestamped section to the existing
`.testagent/status.md`; never truncate, replace, or rewrite prior status
content. Include these independent blocks:

1. **Changed files**
   - list every final modified production, test, workflow, plan/status path;
   - distinguish pre-existing authoritative workflow/test changes from edits
     made during implementation;
   - state that both context-only Buddy workflows remained unchanged.
2. **Exact tests**
   - list the three loader test names;
   - list every malformed parameter ID;
   - list the five strengthened workflow scenario names.
3. **Commands/results**
   - copy each final command;
   - include exit status and pass/deselect/collection counts;
   - label any unrelated failure as report-only.
4. **Quality findings**
   - append the `test-gap-analysis` findings/dispositions;
   - append the `assertion-quality` findings/dispositions;
   - identify any resulting bounded edit and revalidation.
5. **Requirement-to-evidence mapping**
   - map each checklist item 1–6 separately to exact test assertions,
     artifacts, and validation/review results.

The final response must mirror the research handoff template with explicit
**Changed files**, **Exact tests**, **Commands/results**, and
**Requirement-to-evidence mapping** sections.

### Success Criteria

- [ ] Both mandatory skills run against the final changed tests.
- [ ] No bounded gap or shallow-assertion finding remains unresolved.
- [ ] Status evidence is appended, not rewritten.
- [ ] The handoff distinguishes prior user changes from implementation edits.
- [ ] All six checklist items have independent evidence.

---

## Requirement-to-Implementation Mapping

| Checklist item | Concrete planned tests/assertions | Planned artifact/evidence |
|---:|---|---|
| 1 | The two exact acceptance tests prove native bare normalization and prefixed compatibility; the parametrized rejection test covers short, long, uppercase, nonhex, empty, prefix-only, malformed-prefixed, zero-ID, and negative-ID cases using a valid marker body. | Narrow `_normalized_digest` reuse in `cli.py`; exact collected nodes and results in the Phase 4 status append. |
| 2 | The strengthened raw helper proves one nonempty literal file and rejects directory/glob/multiline shapes. The five exact workflow scenarios prove producer ordering, the complete reviewer formatter chain, and the complete Authorization basename/Base64/name/ID/digest chain through all consumers. | Assertions in `test_buddy_workflows.py` against the authoritative `workflow-delivery-v3-live-attempt.yml`; scenario results and chain mapping in status. |
| 3 | All tests use existing module-level pytest style, `tmp_path`/canonical bytes for CLI cases, YAML helpers and exact mappings for contracts, descriptive names, explicit ordering/negative assertions, and named parameter IDs. Existing tests are extended/appended only. | Final diff review plus `assertion-quality` disposition. |
| 4 | Run bounded collection/scoped tests, both full modules, Ruff check/format, actionlint, diff check, the affected-package gate, and root collection in the documented order. Do not repair unrelated failures. | Exact commands, statuses, and counts appended to status; unrelated failures explicitly marked report-only. |
| 5 | Run `test-gap-analysis` and `assertion-quality` only after tests are final; resolve bounded findings and rerun validation/reviews. | Clearly delimited append-only findings and dispositions in `.testagent/status.md`. |
| 6 | Produce four explicit handoff blocks listing changed files and provenance, exact tests/parameter IDs, commands/results, and a one-to-one requirement evidence map. | Final status section and final implementation response. |

## Final File Budget

Expected implementation edits are limited to:

- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
- `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- `.testagent/status.md` (append only)

Preserve the already modified
`.github/workflows/workflow-delivery-v3-live-attempt.yml`; edit it only for a
remaining bounded failure proven by Phase 2. Do not modify either context-only
Buddy workflow or any other production/test surface.

<!-- END APPEND: 2026-08-18-bounded-wdv3-artifact-transport-sequential-plan -->


<!-- APPENDED PUBLICATION-PREPARATION PLAN: preserved in full from the planning phase -->

---

# Test Implementation Plan

## Overview

Use a targeted, tests-first strategy for the bounded Workflow Delivery v3
publication-preparation regression. The authority is the current tree plus
`1e742b29..HEAD`, as summarized in `.testagent/research.md`.

Implementation is split into three sequential delegation phases: leaf record
invariants, executable classifier/publisher scenarios, then workflow lifecycle
and retention behavior. One `code-testing-implementer` owns one phase at a
time; the next phase must not begin until the prior phase's narrow command is
green and `.testagent/status.md` records the handoff.

No new test project, dependency, Python model of the Bash classifier, remote
workflow run, or real publication is needed.

## Implementation Guardrails

- Add or strengthen tests before touching production/workflow code. A new
  characterization may already be green; never force a production edit.
- Preserve every existing scenario and assertion. Consolidating a duplicated
  Python truth table into execution of the real shell is allowed only when no
  row or semantic assertion is lost.
- Extract and execute the exact workflow `run` strings. Double only the `uv`
  CLI process boundary.
- Make only a failing-test-driven, surgical change in the bounded workflow or
  `AttemptOutcome.__post_init__`. Do not refactor `release/live.py`, `cli.py`,
  or unrelated code without a new failing test that requires it.
- Never restore missing files or run version-control-mutating operations
  (`checkout`, `reset`, `clean`, `stash`, `commit`, or rebase).
- Update `.testagent/status.md` at phase start, after the tests-first run,
  after the green run, and at handoff. Record phase, exact changed paths,
  test/case names, command and result, production change or “none,” blockers,
  and next phase.
- Do not run phases concurrently. Stop on a blocker or unexplained baseline
  regression.

## Commands

- **Build**:
  `uv build --package three-workflow-delivery-v3`
- **Record test**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`
- **Workflow test**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Bounded integration**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
- **Full affected package**:
  `python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
- **Discovery**:
  `uv run --python 3.13 pytest --collect-only -q`
- **Workflow lint**:
  `actionlint .github/workflows/workflow-delivery-v3-live-attempt.yml`
- **Python lint/format**:
  `uv run --python 3.13 ruff check --force-exclude -- <changed-python-paths>`
  and
  `uv run --python 3.13 ruff format --check --force-exclude -- <changed-python-paths>`
- **Type check**:
  `uv run --python 3.13 pyrefly check <changed-python-paths>`
- **Affected-file HK gate**:
  `hk check --check <changed-paths>`

## Phase Summary

| Phase | Focus | Primary files | Estimated additions |
|---|---|---|---:|
| 1 | Direct `AttemptOutcome` negatives | 1 test file; conditional 1 source file | 1 parametrized test, 6 new rows |
| 2 | Exact classifier and publisher shell | 1 test file; conditional workflow edit | 3 tests, about 21 scenario rows |
| 3 | Snapshot lifecycle, reviewer link, retention/postamble | 1 test file; conditional workflow edit | 6 focused tests |

---

## Phase 1: Publication-Preparation Record Invariants

### Overview

Start at the dependency-graph leaf. Directly vary the real immutable
`AttemptOutcome`; do not involve the domain finalizer, CLI, workflow, or mocks.

### Files to Test

#### `AttemptOutcome`

- **Source**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py`
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`
- **Test Function**:
  `test_commit8_records_reject_independent_binding_substitutions`
- **Exact publication-preparation parameter IDs**:
  `uncertainty`, `authorization-digest`, `publication-snapshot-digest`,
  `capability-admission-digests`, `capability-group-bundle-digests`,
  `receipt-digests`, `result`, `possibly-mutated`, and `next-action`
- **Method**: `AttemptOutcome.__post_init__`

**Scenarios and assertions**

Use the existing canonical publication-preparation Outcome and
`dataclasses.replace`, changing exactly one field per readable parameter ID:

1. Retain the existing rows:
   - `uncertainty`
   - `authorization-digest`
   - `publication-snapshot-digest`
2. Add the missing rows:
   - `capability-admission-digests`
   - `capability-group-bundle-digests`
   - `receipt-digests`
   - `result`
   - `possibly-mutated`
   - `next-action`

For digest collections, use one valid digest; for `result` and `next_action`,
use an otherwise valid noncanonical value already represented by package test
fixtures. This ensures the publication-preparation invariant—not generic field
validation—is what fails.

Every row must assert:

```text
pytest.raises(ValueError, match=r"(?i)publication[- ]preparation")
```

Do not remove the existing admitted canonical case. If the existing three rows
currently live under another test name, extend that table in place and retain
its name rather than duplicating those rows; record the resulting exact
collected name in `.testagent/status.md`.

### Allowed Production Response

No source change is expected. If and only if a new row fails, tighten the
existing publication-preparation branch in `AttemptOutcome.__post_init__` for
that field. Do not alter unrelated Outcome variants or validation helpers.

### Verification and Handoff

1. Run the **Record test** command.
2. Before Phase 2, protect the already-substantial domain/CLI layers:

   `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`

3. Update `.testagent/status.md` with all nine row IDs and both results.

### Success Criteria

- [ ] All nine one-field substitutions directly construct/replace the real record.
- [ ] Every negative fails at the publication-preparation invariant.
- [ ] Existing positive, domain-finalizer, and CLI tests remain green.
- [ ] No production edit was made unless a new test demonstrated the need.

---

## Phase 2: Execute the Classifier and Publisher Truth Table

### Overview

Add one reusable Bash harness in the existing workflow contract test. Replace
the duplicated Python truth calculation with scenario facts rendered into the
exact `Finalize Attempt Outcome` `run` string.

### Files to Test

#### `release-finalizer` / `Finalize Attempt Outcome`

- **Source**:
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Job/Step**: `release-finalizer` / `Finalize Attempt Outcome`

### Harness Contract

Using the existing `_document`, `_steps`, `_step`, and `_run` conventions:

1. Load YAML with `yaml.safe_load` and extract the exact current `run` value.
2. Render every `${{ ... }}` from one explicit fact map and assert no `${{`
   remains.
3. Set `GITHUB_OUTPUT`, `GITHUB_STEP_SUMMARY`, `GITHUB_RUN_ID`,
   `GITHUB_RUN_ATTEMPT`, and `WDV3_PACKAGE` under `tmp_path`.
4. Put a small executable `uv` double first on `PATH`. It records argv, writes
   requested files/outputs when configured, and returns a configured status.
5. Execute with
   `("bash", "--noprofile", "--norc", "-euo", "pipefail", "-c", run)`,
   `cwd=tmp_path`, and never `shell=True`.
6. The harness must not reproduce a classifier predicate in Python.

### Exact Tests

#### 1. `test_publication_preparation_classifier_executes_workflow_shell`

Parametrize these exact IDs. Qualification is successful, publisher is
`skipped`, Snapshot transport and all downstream lineage are absent:

| ID | Workflow cancelled | Observation | Materialization |
|---|---:|---|---|
| `observation-failure__materialization-skipped` | no | failure | skipped |
| `observation-cancelled__materialization-cancelled` | no | cancelled | cancelled |
| `observation-success__snapshot-upload-failure` | no | success | failure |
| `observation-success__materialization-cancelled` | no | success | cancelled |
| `workflow-cancelled__observation-skipped__materialization-skipped` | yes | skipped | skipped |
| `workflow-cancelled__observation-success__materialization-skipped` | yes | success | skipped |

For each row assert successful shell completion with a status-zero CLI double,
one CLI invocation, exactly one
`--publication-preparation-interrupted`, no `--platform-terminated`, and no
Snapshot or downstream-lineage sentinel in argv.

#### 2. `test_publication_preparation_classifier_rejects_invalid_workflow_facts`

Parametrize:

| ID | Invalid fact | Required diagnostic token |
|---|---|---|
| `unexplained-observation-skip` | no workflow cancellation; Observation success; materialization skipped | `Observation`/`skipped` |
| `materialization-success-without-durable-snapshot` | materialization success; both transport outputs absent | `Snapshot` |
| `snapshot-artifact-id-without-upload-digest` | artifact ID only | `Snapshot`/`transport` |
| `snapshot-upload-digest-without-artifact-id` | upload digest only | `Snapshot`/`transport` |
| `publisher-success` | otherwise admitted preparation facts; publisher success | `publisher` |
| `publisher-failure` | otherwise admitted preparation facts; publisher failure | `publisher` |

Each row must assert a nonzero shell status, the scenario-specific diagnostic
in captured output, and that the CLI argv record was never created.

#### 3. `test_publisher_result_truth_table_executes_workflow_shell`

Parametrize:

| ID | Facts | Expected |
|---|---|---|
| `whole-run-cancelled-unstarted` | workflow cancelled; Observation/materialization skipped; publisher cancelled; no transport/lineage | preparation flag only |
| `cancelled-without-workflow-ownership` | same, but workflow not cancelled | reject before CLI |
| `cancelled-with-forwarded-snapshot` | admitted cancellation facts plus forwarded Snapshot | reject before CLI |
| `cancelled-with-authorization` | plus Authorization | reject before CLI |
| `cancelled-with-capability-admission` | plus Capability Admission | reject before CLI |
| `cancelled-with-mutation-marker` | plus mutation marker | reject before CLI |
| `cancelled-with-result-bundle` | plus result bundle | reject before CLI |
| `cancelled-with-receipt` | plus Receipt | reject before CLI |
| `post-snapshot-cancelled` | ordinary run; durable Snapshot transport/payload; publisher cancelled | platform-termination flag, Snapshot args, no preparation flag |

For the admitted unstarted row, assert
`--publication-preparation-interrupted` occurs once and
`--platform-terminated` is absent. For every rejected row, assert nonzero,
a diagnostic naming the conflicting ownership/lineage fact, and no CLI call.
For `post-snapshot-cancelled`, assert one CLI call, the exact durable Snapshot
sentinels in argv, `--platform-terminated` present, and
`--publication-preparation-interrupted` absent.

The existing Python-only preparation/publisher truth table must be folded into
these shell-driven rows without losing coverage; do not leave a second model
that calculates the expected classifier result.

### Allowed Production Response

The expected failing case is `whole-run-cancelled-unstarted`. Make the smallest
workflow-only change that:

- admits publisher result `cancelled` only when workflow cancellation owns the
  interruption, Snapshot transport is absent, and every downstream/mutation
  fact is absent; and
- prevents that same admitted row from also adding `--platform-terminated`.

Do not broaden ordinary publisher cancellation admission. No Python production
change is planned.

### Verification and Handoff

Run the **Workflow test** command and update `.testagent/status.md` with every
case ID, the initial failing rows, the exact YAML change, and the final result.

### Success Criteria

- [ ] The exact workflow shell—not copied Python logic—decides every row.
- [ ] All admitted preparation combinations and both partial transport states are covered.
- [ ] Every downstream lineage fact is rejected independently.
- [ ] Unstarted cancellation and post-Snapshot cancellation map to distinct flags.
- [ ] The workflow test file is green before Phase 3 starts.

---

## Phase 3: Snapshot Lifecycle, Reviewer Diagnostics, and Retention

### Overview

Build on Phase 2's harness to lock direct Snapshot identity, preserve a durable
Snapshot through a later materialization-job failure, link immutable reviewer
diagnostics, and execute the finalizer postamble and propagation shell.

### Files to Test

- **Source**:
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`
- **Test File**:
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Jobs**: `materialize-publication`, `release-finalizer`

### Exact Tests

#### 1. `test_publication_snapshot_lifecycle_and_transport_identity_are_exact`

Assert the filtered semantic step-ID order is exactly:

```text
materialize -> names -> upload-snapshot -> upload-reviewer -> bind
```

Assert the materialization job's existing transport outputs equal, exactly:

- `${{ steps.upload-snapshot.outputs.artifact-id }}`
- `${{ steps.upload-snapshot.outputs.artifact-digest }}`

Assert the separate canonical Snapshot payload-digest output still references
`steps.materialize`, not either upload step. Check expressions, not YAML
formatting or unrelated step adjacency.

#### 2. `test_release_finalizer_downloads_snapshot_directly_from_materialization`

Assert `release-finalizer.needs` directly contains `materialize-publication`;
the Snapshot download input and finalizer Snapshot/payload-digest expressions
reference `needs.materialize-publication.outputs`. Assert none of those
expressions reference approval forwarding.

#### 3. `test_durable_snapshot_survives_later_reviewer_failure`

Execute the exact finalizer shell with successful Qualification and
Observation, `materialize-publication=failure`, publisher skipped, and valid
Snapshot artifact-ID, upload-digest, payload-digest, and path sentinels. This
single GitHub fact shape represents either a later reviewer upload or binding
failure, so do not duplicate an indistinguishable row.

Assert one CLI call, all Snapshot payload arguments/sentinels preserved,
`--publication-preparation-interrupted` absent, and
`--platform-terminated` absent.

#### 4. `test_completed_materialization_summary_links_immutable_reviewer_artifact`

Assert the completed-summary/link step is after `upload-reviewer` and `bind`,
and runs only after the reviewer upload and exact payload binding succeed. Its
exact shell must use `${{ steps.upload-reviewer.outputs.artifact-url }}` and
write the URL to `GITHUB_STEP_SUMMARY`.

Execute that exact summary/link shell with a reviewer payload and URL sentinel.
Compare `reviewer-summary.md` bytes before and after; they must be identical.
Assert the job summary contains both the reviewer text and URL, while the
reviewer payload never contains the URL. Also assert no workflow shell redirects
into or rewrites `reviewer-summary.md`.

#### 5. `test_incomplete_preparation_retains_diagnostics_before_job_failure`

Execute the exact `Finalize Attempt Outcome` shell for an admitted preparation
scenario. Configure the `uv` double to:

- capture real CLI argv;
- write the requested Outcome and Attempt-summary files;
- emit a known final `artifact-name`; and
- return the real incomplete status `1`.

Assert:

- the finalizer shell preserves control for retention rather than failing
  before the upload step;
- argv includes the preparation flag;
- Outcome and Attempt-summary files still exist;
- both the retained Attempt summary and `GITHUB_STEP_SUMMARY` contain the
  publication-preparation interruption diagnostic;
- parsed `GITHUB_OUTPUT["artifact-name"]` is the configured sentinel; and
- parsed `GITHUB_OUTPUT["status"] == "1"`.

#### 6. `test_propagation_fails_after_successful_retention`

Retain and strengthen the structural protection by asserting:

```text
Finalize Attempt Outcome
  < Upload final Attempt Outcome and summary
  < Propagate finalization status
```

Assert the upload step has `if: always()`. Then render and execute the exact
`Propagate finalization status` shell with finalization status `1` and a
successful simulated retention upload. Assert its process exits nonzero only
after that successful-retention state is supplied.

The upload action itself remains a structural boundary; do not attempt to run
`actions/upload-artifact` locally.

### Allowed Production Response

The expected workflow change is limited to completed reviewer-summary
rendering:

- remove the pre-upload summary write from `names`;
- after `upload-reviewer` and `bind` succeed, read the immutable reviewer
  summary into `GITHUB_STEP_SUMMARY` and append its artifact URL; and
- keep this step after `bind`.

Lifecycle/output/direct-Snapshot and postamble tests are expected to be
test-only locks. Change those workflow sections only if an exact executable
test demonstrates a defect. Do not edit Python production code for this phase.

### Verification and Handoff

1. Run the **Workflow test** command.
2. Run the **Bounded integration** command.
3. Update `.testagent/status.md` with all six exact test names, commands,
   outcomes, changed paths, and any remaining blocker.

### Success Criteria

- [ ] Snapshot upload order and all three output identities are locked.
- [ ] Release finalization consumes materialization outputs directly.
- [ ] A durable Snapshot survives a later reviewer/binding failure.
- [ ] The completed summary links the artifact without mutating its payload.
- [ ] Status `1` retains diagnostics/files before propagation fails the job.
- [ ] All five bounded integration files pass.

---

## Requirement Traceability

| Research checklist item | Exact test(s) / gate | Concrete proof |
|---|---|---|
| 1: execute actual classifier shell | `test_publication_preparation_classifier_executes_workflow_shell`; `test_publication_preparation_classifier_rejects_invalid_workflow_facts`; `test_publisher_result_truth_table_executes_workflow_shell` | YAML-extracted `run`, complete expression rendering, Bash argv execution, CLI call/no-call assertions |
| 1: admitted Observation/materialization combinations, including upload failure and workflow cancellation | `test_publication_preparation_classifier_executes_workflow_shell` and its six named IDs | Preparation flag only, no Snapshot/lineage/platform flag |
| 1: unexplained skips, missing durable Snapshot, partial transport, and non-admitted publisher results | `test_publication_preparation_classifier_rejects_invalid_workflow_facts` and its six named IDs | Nonzero plus specific diagnostic and no CLI invocation |
| 1: Snapshot transport presence/absence and all downstream lineage facts | Phase 2 absent/partial rows; all six `cancelled-with-*` rows; Phase 3 durable-Snapshot test | Both absent, both present, each one-sided partial, and each lineage fact independently exercised |
| 1: narrowly admitted unstarted cancelled publisher | `test_publisher_result_truth_table_executes_workflow_shell[whole-run-cancelled-unstarted]` | Preparation flag exactly once; no platform-termination flag |
| 2: lifecycle and output identity | `test_publication_snapshot_lifecycle_and_transport_identity_are_exact` | Exact semantic order and exact upload/materialize output sources |
| 2: finalizer uses direct materialization Snapshot | `test_release_finalizer_downloads_snapshot_directly_from_materialization` | Direct `needs` and expression source; no approval forwarding |
| 2: later reviewer/binding failure preserves Snapshot | `test_durable_snapshot_survives_later_reviewer_failure` | Captured Snapshot argv; no preparation flag |
| 3: all direct `AttemptOutcome` negatives | `test_commit8_records_reject_independent_binding_substitutions` with IDs `uncertainty`, `authorization-digest`, `publication-snapshot-digest`, `capability-admission-digests`, `capability-group-bundle-digests`, `receipt-digests`, `result`, `possibly-mutated`, and `next-action` | Nine one-field `dataclasses.replace` rows; real invariant exception; no mock |
| 4: execute finalizer postamble and retain diagnostics/files | `test_incomplete_preparation_retains_diagnostics_before_job_failure` | CLI status `1`, both summaries, output metadata, and surviving files |
| 4: always-upload ordering and later failure | `test_propagation_fails_after_successful_retention` | Structural `if: always()`/ordering plus exact propagation-shell nonzero exit |
| 5: reviewer artifact URL and immutable payload | `test_completed_materialization_summary_links_immutable_reviewer_artifact` | Success-only order, artifact URL in job summary, byte-identical reviewer file |
| 6: publisher truth table | `test_publisher_result_truth_table_executes_workflow_shell` | Whole-run admission, no-ownership rejection, six lineage rejections, ordinary post-Snapshot platform termination |
| 7: tests first/minimal changes | Per-phase tests-first run and Allowed Production Response sections | Red/green evidence and production-change rationale in `status.md` |
| 7: no restoration or VCS mutation | Implementation Guardrails | Any requested mutation is an immediate blocker |
| 7: research/plan/status handoff | This plan plus required phase updates to `.testagent/status.md` | Status updated at start, tests-first run, green run, and handoff |
| 7: narrow then complete validation | Per-phase verification and Final Validation Gate | Narrow file first; bounded/full/lint/build/HK last |

## Conditional Blocker: Normative Wording

Research records an MLD/LLD statement that the publisher “must be skipped,”
but does not provide exact documentation paths. Do not search or edit unrelated
documentation speculatively. If required handoff/docs rules make reconciliation
mandatory, pause after the failing test identifies the narrow exception,
record the blocker in `.testagent/status.md`, consult only the required
authoritative docs, and make the smallest wording change describing GitHub's
`cancelled` spelling for an unstarted publisher. It must not broaden general
publisher cancellation.

## Final Validation Gate

After all three implementation phases are green:

1. Run the **Bounded integration** command.
2. Run the **Full affected package** command.
3. Run the **Discovery** command.
4. Run the **Build** command.
5. Run `actionlint .github/workflows/workflow-delivery-v3-live-attempt.yml`.
6. For the expected changed Python files, run:

   `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`

   `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`

   `uv run --python 3.13 pyrefly check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`

   Append `records/release.py` only if Phase 1 actually changed it.
7. For the expected changed paths, run:

   `hk check --check .github/workflows/workflow-delivery-v3-live-attempt.yml src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`

   Add only other paths actually changed.
8. Run the mandatory post-implementation quality gate:
   - invoke `test-gap-analysis` for this bounded request and resolve every
     unmapped/high-priority gap;
   - invoke `assertion-quality` on the changed tests and resolve shallow,
     tautological, self-referential, or assertion-free findings; and
   - perform the prompt-scenario gate by checking every row in the Requirement
     Traceability table and every named scenario ID against collected tests and
     assertions.
9. Re-run the narrow affected test after any gate-driven edit, then repeat all
   affected final validations.
10. Record command results and all three quality-gate outcomes in
    `.testagent/status.md`. Any unresolved checklist row, quality finding,
    normative-doc blocker, or validation failure blocks completion.

<!-- BEGIN APPEND: 2026-08-19-wdv3-four-accepted-repairs-plan-4a38b286 -->

# Workflow Delivery v3 Four Accepted Repairs — Test Implementation Plan

## Overview

Use a targeted, single-pass, tests-first sequence against `HEAD` `4a38b286`.
Only the partial workflow/CLI surfaces identified by the bounded research are
in scope. Reuse the exact-workflow-shell helpers already in
`test_buddy_workflows.py`; do not add another renderer, fact model, or Bash
harness. Publication Control Closure documentation is out of scope.

Expected production edits are limited to:

- `.github/workflows/workflow-delivery-v3-live-attempt.yml`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`

Use the canonical tests:

- `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- `src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py`
- `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`

Do not change `records/release.py`, `records/release_transport.py`, or
`test_commit6_transport_cli.py` unless the CLI-local typed helper cannot remain
local; if one changes, retain and run the closed-transport regression.

## Commands

- **Build**: `uv build --package three-workflow-delivery-v3`
- **Bounded tests**: `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit6_transport_cli.py`
- **Lint/type**: use the exact Ruff, Pyrefly, and actionlint commands in the
  Final Validation Gate below.

## Prompt-Scenario and State Gate

Before Phase 1, map every row below to a collected test and append the mapping
to `.testagent/status.md`; no production edit precedes its failing test.

| Requirement | Exact planned test/scenario |
|---|---|
| Five required and ten optional acquisition guards | `test_release_finalizer_prerequisite_actions_are_cancellation_admitting` with the 15 Phase 1 IDs |
| Existing acquisition assertions | `test_unsuccessful_live_qualification_retains_a_publication_free_outcome`; `test_release_finalizer_downloads_snapshot_directly_from_materialization` |
| No workflow ownership | `test_publisher_result_truth_table_executes_workflow_shell[cancelled-without-workflow-ownership]` |
| Qualification-only cancellation | `test_cancelled_unsuccessful_qualification_uses_exact_qualification_only_argv[failure]` and `[incomplete]` |
| Ordinary post-Snapshot cancellation | `test_publisher_result_truth_table_executes_workflow_shell[post-snapshot-cancelled]` |
| Contradictory/downstream cancellation | The same truth-table function's six existing `cancelled-with-*` IDs and existing partial-Snapshot IDs, retained verbatim |
| Five all-or-none optional groups | `test_finalize_live_rejects_each_partial_optional_transport_group` with the 20 Phase 3 IDs |
| All 20 parser options | `test_cli_exposes_strict_commit8_live_transport_commands` (single existing scenario, extended in place) |
| Optional Qualification Evidence unchanged | Entire `test_live_qualification_boundary.py` regression; no `_optional_evidence` or parser-semantic edit |
| Append-only artifacts | `test_hk_trigger.py` plus the three-byte-prefix command in the Final Validation Gate |

At entry and after every phase, run:

`python -c 'from pathlib import Path; import subprocess; paths=(".testagent/research.md",".testagent/plan.md",".testagent/status.md"); assert all(Path(path).read_bytes().startswith(subprocess.check_output(("git","show",f"HEAD:{path}"))) for path in paths)'`

Keep research and plan unchanged during implementation. Any status update must
be a concise, uniquely delimited EOF append. Do not commit, stage, reset, or
otherwise mutate VCS state.

## Phase Summary

| Phase | Focus | Files | Estimated scenarios |
|---|---|---:|---:|
| 1 | Cancellation-admitting acquisition guards | 2 | 15 parameter rows + 2 updated contracts |
| 2 | Publisher ownership and cancellation argv | 2 | 3 focused rows + existing negative matrix |
| 3 | Typed finalize-live optional-group preflight | 3 | 20 direct CLI rows + parser contract |
| 4 | Narrow-to-final validation and state proof | 0 | All required command gates |

---

## Phase 1: Release-Finalizer Acquisition Guards

### Files

- **Source**: `.github/workflows/workflow-delivery-v3-live-attempt.yml`
  (`release-finalizer` acquisition steps)
- **Test**:
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Test module**: module-level workflow contracts; no test class

### Tests First

Add
`test_release_finalizer_prerequisite_actions_are_cancellation_admitting` as
one parameterized structural contract. Its exact IDs are:

- Required / exact `if: always()`: `checkout-target`, `install-uv`,
  `attempt-binding`, `qualification-snapshot`, `qualification-decision`.
- Optional / exact `if: always() && <existing artifact-id expression> != ''`:
  `build`, `project-test`, `artifact-contents`, `install-import`,
  `release-artifact`, `publication-snapshot`, `authorization`,
  `capability-admission-decision`, `capability-result-bundle`, `receipt`.

Each parameter must identify the actual step by name and compare the complete
YAML `if` string, not merely search for `always()`. Update the old-condition
assertions in:

- `test_unsuccessful_live_qualification_retains_a_publication_free_outcome`
- `test_release_finalizer_downloads_snapshot_directly_from_materialization`

**Red command** (after test edits, before workflow edits):

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py -k 'release_finalizer_prerequisite_actions_are_cancellation_admitting or unsuccessful_live_qualification_retains_a_publication_free_outcome or release_finalizer_downloads_snapshot_directly_from_materialization'`

### Production Response

Add `if: always()` to exactly the five required acquisition actions. Prefix
each of the ten existing optional artifact-ID predicates with `always() &&`;
preserve each current artifact expression, action version, `with` block, and
step order. Marketplace actions remain structural-only and are not executed
locally.

**Green command**: rerun the Red command, then:

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`

### Success Criteria

- [ ] All 15 exact conditions pass.
- [ ] Existing publication-free and direct-Snapshot contracts use the new conditions.
- [ ] No workflow behavior outside acquisition conditions changes.

---

## Phase 2: Ownership Rejection and Qualification-Only Cancellation

### Files

- **Source**: `.github/workflows/workflow-delivery-v3-live-attempt.yml`
  (`Finalize Attempt Outcome` shell only)
- **Test**:
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`

### Tests First

1. Correct
   `test_publisher_result_truth_table_executes_workflow_shell[cancelled-without-workflow-ownership]`
   to use Observation=`failure`, materialization=`skipped`,
   publisher=`cancelled`, and workflow cancellation=`false`. Assert the
   publisher-ownership diagnostic and zero captured CLI invocations.
2. Add
   `test_cancelled_unsuccessful_qualification_uses_exact_qualification_only_argv`
   with IDs `failure` and `incomplete`. Both rows use
   Observation/materialization=`skipped`, publisher=`cancelled`, workflow
   cancellation=`true`, and no Snapshot, Authorization, capability, bundle,
   Receipt, or mutation-marker lineage. Compare the complete captured argv to
   a literal expected argv made only from the canonical Attempt binding,
   Qualification Snapshot/Decision replay arguments, and current Outcome and
   summary outputs. Do not derive the expectation by filtering actual argv.
   Neither row may contain `--publication-preparation-interrupted` or
   `--platform-terminated`.
3. Keep
   `test_publisher_result_truth_table_executes_workflow_shell[post-snapshot-cancelled]`
   asserting exactly one `--platform-terminated` and no preparation flag.
   Run the whole parameterization so its six existing `cancelled-with-*`
   lineage IDs, partial-Snapshot IDs, and contradictory cases remain rejected
   with no CLI call. Do not rename those established IDs.

All shell execution must continue through `_phase2_finalizer_facts`,
`_phase2_render_finalizer_run`, `_phase2_execute_finalizer_shell`, and
`_phase2_assert_successful_finalizer`.

**Red command** (after test edits, before shell edits):

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py -k 'publisher_result_truth_table_executes_workflow_shell or cancelled_unsuccessful_qualification_uses_exact_qualification_only_argv'`

### Production Response

In the existing finalizer shell, classify an owned whole-workflow cancellation
after exact failed/incomplete Qualification and before any downstream lineage
as qualification-only: append neither semantic flag. Keep publication
preparation interruption classification and ordinary post-Snapshot
`--platform-terminated` classification unchanged. Do not weaken publisher
ownership, partial transport, contradictory fact, or downstream-lineage
rejections, and do not change `finalize_attempt_outcome` or `AttemptOutcome`.

**Green command**: rerun the Red command, then the complete workflow-contract
command from Phase 1.

### Success Criteria

- [ ] The no-ownership row reaches publisher ownership rejection.
- [ ] `failure` and `incomplete` match complete qualification-only argv.
- [ ] Post-Snapshot cancellation remains platform termination.
- [ ] Every established ownership/lineage/partial-Snapshot negative remains closed.

---

## Phase 3: Typed All-or-None Finalize-Live Preflight

### Files

- **Source**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
- **Behavior tests**:
  `src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py`
- **Parser test**:
  `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`

### Tests First

Add `test_finalize_live_rejects_each_partial_optional_transport_group` using
valid exact mandatory Qualification replay. The exact 20 IDs are:

- `publication-snapshot-missing-path`,
  `publication-snapshot-missing-record-digest`,
  `publication-snapshot-missing-artifact-id`,
  `publication-snapshot-missing-artifact-digest`
- `authorization-missing-path`,
  `authorization-missing-record-digest`,
  `authorization-missing-artifact-id`,
  `authorization-missing-artifact-digest`
- `capability-decision-missing-path`,
  `capability-decision-missing-record-digest`,
  `capability-decision-missing-artifact-id`,
  `capability-decision-missing-artifact-digest`
- `capability-group-bundle-missing-path`,
  `capability-group-bundle-missing-record-digest`,
  `capability-group-bundle-missing-artifact-id`,
  `capability-group-bundle-missing-artifact-digest`
- `receipt-missing-path`, `receipt-missing-record-digest`,
  `receipt-missing-artifact-id`, `receipt-missing-artifact-digest`

For each row, supply exactly three of that group's four members, leave the
other optional groups absent, dispatch through the real parser, and assert
status `1`, stderr naming that group and saying all four members must be all
present or all absent, and non-creation of both Attempt Outcome and summary.

Extend `test_cli_exposes_strict_commit8_live_transport_commands` in place to
lock these exact option families for each of `publication-snapshot`,
`authorization`, `capability-decision`, `capability-group-bundle`, and
`receipt`: `--<group>-path`, `--<group>-record-digest`,
`--<group>-artifact-id`, and `--<group>-artifact-digest`. This is a
single-case existing test with no parameter ID.

**Red command** (after test edits, before CLI edits):

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py -k 'finalize_live_rejects_each_partial_optional_transport_group or cli_exposes_strict_commit8_live_transport_commands'`

### Production Response

Add one CLI-local typed helper,
`_optional_uploaded_record_transport`, taking group name plus
`Path | None`, record digest, artifact ID, and artifact digest, and returning
`tuple[Path, str, str, str] | None`. It must:

1. return `None` when all four values are absent;
2. return the fully narrowed tuple when all four are present; and
3. otherwise raise a group-specific `ValueError` stating that all four values
   must be all present or all absent.

Call it for `publication_snapshot`, `authorization`, `capability_decision`,
`capability_group_bundle`, and `receipt` at the start of
`_release_finalize_live_command`, before `_load_attempt_binding` or any other
record load. Consume the returned typed states in the existing loaders. Add no
`cast`, broad catch, or new exception mapping; retain `main`'s existing
`ValueError` handling. Do not apply the helper to Qualification Evidence or
change `_optional_evidence`.

**Green command**: rerun the Red command, then:

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py`

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`

### Success Criteria

- [ ] All 20 one-member-omitted cases fail clearly before Outcome/summary creation.
- [ ] All-present and all-absent transports retain existing behavior.
- [ ] Optional Qualification Evidence semantics remain unchanged.
- [ ] The implementation is typed without casts or broad catches.

---

## Phase 4: Final Validation and Append-Only Proof

There is no production response and no expected red test in this phase. If a
command fails, stop, repair only its owning earlier phase tests-first, and
restart this ordered gate. The prompt-scenario gate passes only when collected
tests show every exact new ID above, the no-ownership and post-Snapshot IDs,
and all pre-existing `cancelled-with-*`/partial-Snapshot IDs.

Run, in order:

1. **Narrow workflow contract**
   `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
2. **Live Qualification CLI**
   `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py`
3. **CLI parser/behavior**
   `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
4. **Closed transport regression**
   `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit6_transport_cli.py`
5. **Bounded combined tests**
   `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit6_transport_cli.py`
6. **Discovery / prompt-scenario collection gate**
   `uv run --python 3.13 pytest --collect-only -q`
7. **Full affected package**
   `GIT_LFS_SKIP_SMUDGE=1 python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
8. **Build**
   `uv build --package three-workflow-delivery-v3`
9. **Ruff check**
   `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release_transport.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
10. **Ruff format check**
    `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release_transport.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
11. **Pyrefly**
    `uv run --python 3.13 pyrefly check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release_transport.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
12. **actionlint**
    `actionlint .github/workflows/workflow-delivery-v3-live-attempt.yml`
13. **Append-only artifact tests**
    `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
14. **All three append-only byte prefixes**
    `python -c 'from pathlib import Path; import subprocess; paths=(".testagent/research.md",".testagent/plan.md",".testagent/status.md"); assert all(Path(path).read_bytes().startswith(subprocess.check_output(("git","show",f"HEAD:{path}"))) for path in paths)'`
15. **Whitespace/diff integrity**
    `git diff --check`

Completion requires every command green, every prompt scenario collected and
asserted, only bounded files changed, append-only state intact, and no commit
or Publication Control Closure documentation edit.

<!-- END APPEND: 2026-08-19-wdv3-four-accepted-repairs-plan-4a38b286 -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-four-repairs-quality-gate-plan-correction -->

## Quality-gate correction

- Phase 3 uses canonical `--<group>` / `--<group>-digest` options; the planned
  alias/parser-surface addition is superseded as unnecessary.
- Every partial-group row also proves `_load_attempt_binding` is not called.
- Phase 2 additionally maps
  `test_unsuccessful_qualification_cancellation_is_not_clean_with_contradictions`
  IDs `without-workflow-ownership`, `with-observation-work`,
  `with-materialization-work`, `with-publication-snapshot`,
  `with-orphaned-snapshot-upload-digest`, `with-forwarded-snapshot`,
  `with-authorization`, `with-capability-admission`, `with-mutation-marker`,
  `with-result-bundle`, and `with-receipt`.

<!-- END APPEND: 2026-08-19-wdv3-four-repairs-quality-gate-plan-correction -->
<!-- BEGIN APPEND: 2026-08-19-wdv3-six-final-review-repairs-plan -->

## Workflow Delivery v3 six final-review repairs plan

1. **Workflow contracts first**
   - Replace the obsolete recorder test with
     `test_workflow_cancellation_witness_has_exact_job_contract`.
   - Update the exact fact map/scenario overrides and three prerequisite rows.
   - Parameterize `test_propagation_fails_after_successful_retention` over
     all-success and the three independent failure inputs.
2. **Minimal workflow implementation**
   - Add only the witness job/dependency/output/fallback and the three nonempty
     mandatory-download guards.
   - Run the complete workflow contract file and actionlint.
3. **Domain scenarios**
   - Add `failure`/`incomplete` × nine-operand cases to
     `test_commit8_live_scenarios.py`, each calling real
     `finalize_attempt_outcome` and checking the exact rejection.
4. **CLI/live boundary**
   - Add one parser-to-handler test using valid transported typed records.
     Capture only `finalize_attempt_outcome`; assert all downstream values,
     constructed Receipt transport fields, platform facts, output, and summary.
5. **Documentation**
   - Update only `release-delivery-mld.md` and the v3 `README.md`; verify the
     smoke LLD remains byte-identical.
6. **Validation and review**
   - Run the six focused pytest files, Ruff check/format, Pyrefly, actionlint,
     both documentation hooks, append-only/HK artifact tests, builds, and
     `git diff --check`.
   - After focused success, run the full v3 test package with
     `GIT_LFS_SKIP_SMUDGE=1`.
   - Apply pseudo-mutation, assertion-depth, and prompt-scenario gates to the
     final tests; strengthen any surviving requirement before reporting.

| Requirement | Planned evidence |
|---|---|
| Job cancellation witness | Exact job contract plus real recorder/fallback shell |
| Mandatory downloads | `attempt-binding`, `qualification-snapshot`, `qualification-decision` rows |
| Unsuccessful guard | 18 named real-domain cases |
| CLI forwarding | One real parser/loader/handler boundary test |
| Propagation | Four executable shell cases |
| Documentation/scope | Two-document diff and unchanged-smoke-LLD guard |

<!-- END APPEND: 2026-08-19-wdv3-six-final-review-repairs-plan -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-two-adjudicated-test-gaps-plan -->

## Workflow Delivery v3 two adjudicated test gaps plan

1. Extend
   `test_unsuccessful_live_qualification_retains_a_publication_free_outcome`
   with an exact map of every digest output to its real producer expression.
2. Strengthen
   `test_incomplete_preparation_retains_diagnostics_before_job_failure` with
   distinct per-record record/upload digest sentinels and an exact record-argv
   assertion against the extracted workflow shell.
3. Add the
   `whole-run-cancelled-after-successful-observation` row to
   `test_publisher_result_truth_table_executes_workflow_shell`; rely on its
   existing exact semantic-flag and no-lineage assertions.
4. Run precisely those three pytest nodes, then invoke `test-gap-analysis` and
   `assertion-quality` on the bounded workflow/test pair. Fix only confirmed
   findings and rerun affected nodes.
5. Append validation and review results to `.testagent/status.md`; verify
   append-only prefixes and diff integrity without running package/HK or
   repository-wide gates.

| Requirement | Planned evidence |
|---|---|
| Producer-specific digest wiring | Exact parsed-workflow digest-output map plus distinct executable argv |
| Independent cancellation combination | `test_publisher_result_truth_table_executes_workflow_shell[whole-run-cancelled-after-successful-observation]` |
| Surgical scope | Test-only diff and EOF-only `.testagent` appends |
| Quality gate | Recorded pseudo-mutation and assertion-quality outcomes |

<!-- END APPEND: 2026-08-19-wdv3-two-adjudicated-test-gaps-plan -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-final-rereview-two-test-gaps-plan -->

## Workflow Delivery v3 final re-review test-gap plan

1. Consolidate the existing partial Qualification Finalizer ID assertions into
   one exact parsed-workflow map for all eight retained record transports,
   covering both `artifact-id` and `artifact-name` producers while preserving
   the existing exact digest map.
2. Expose the retained Attempt summary and GitHub Step Summary paths from the
   existing real-shell execution helper.
3. Add one scenario-first test for successful Qualification, cancellation
   witness `true`, Observation `success`, materialization `skipped`, publisher
   `cancelled`, and absent Snapshot/downstream lineage. Assert the exact
   retained diagnostics on both summary surfaces and the sole admitted
   interruption semantic.
4. Run the two affected pytest nodes and Ruff check/format on the changed test
   file.
5. Invoke bounded `test-gap-analysis` and `assertion-quality`, repair only
   in-scope findings, rerun affected validation, and append results to
   `.testagent/status.md`.
6. Prove all three `.testagent` files retain their complete `HEAD` byte
   prefixes; run scoped diff, scope, and whitespace checks.

| Requirement | Planned evidence |
|---|---|
| Exact retained transport producers | `test_unsuccessful_live_qualification_retains_a_publication_free_outcome` |
| Retained cancellation diagnostics | `test_successful_observation_cancellation_retains_exact_job_diagnostics` |
| Surgical scope | Scoped `git diff` plus changed-path allowlist |
| Quality and append-only gates | Skill outcomes, Ruff/pytest results, and byte-prefix validation |

<!-- END APPEND: 2026-08-19-wdv3-final-rereview-two-test-gaps-plan -->

<!-- BEGIN APPEND: 2026-08-19T200717Z-wdv3-buddy-caller-held-release-execution-concurrency-repair-plan -->

## Workflow Delivery v3 Buddy caller-held Release Execution concurrency repair implementation plan (2026-08-19T20:07:17Z)

### Overview

This is a four-phase, scenario-first plan for the bounded Buddy caller repair.
It characterizes the existing domain identity first, then adds the real CLI
producer, then rewires and pins the caller transport, and finally runs only
bounded validation. The only planned production edits are:

1. `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
2. `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`

`release/identity.py`, `records/release.py`, and `canonical.py` are
preservation dependencies, not production edit targets. No reusable-workflow,
ledger, lock, tag, service, credential, package, manifest, or lockfile change
is planned.

### Commands and scope

- **Build/package gate**: intentionally not planned; do not run `uv build`,
  full-package/HK, acceptance, publication, or repository-wide gates.
- **Per-increment tests**: use the exact node commands in each phase.
- **Bounded regression**:

  ```bash
  uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py
  ```

- **Pyrefly**:

  ```bash
  uv run --python 3.13 pyrefly check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/identity.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/canonical.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py
  ```

- **Ruff check**:

  ```bash
  uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/identity.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/canonical.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py
  ```

- **Ruff format check**:

  ```bash
  uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/identity.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/canonical.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py
  ```

- **Caller actionlint**:

  ```bash
  python eng/scripts/hk_actionlint.py .github/workflows/workflow-delivery-v3-buddy-smoke.yml
  ```

### Phase summary

| Phase | Focus | Owned source targets | Planned test work |
|---|---|---:|---:|
| 1 | Domain identity, equality, separation, and irrelevant facts | 3 read-only dependencies | 3 new scenarios; retain 1 strict scenario |
| 2 | Real successful CLI key production | 1 Python production file | 2 new CLI scenarios |
| 3 | Exact caller forwarding and whole-Attempt concurrency | 1 workflow production file | Strengthen 1 canonical workflow scenario |
| 4 | Bounded regression, static checks, append-only evidence | 0 | Re-run 6 named scenarios plus 4-file regression |

---

### Phase 1: Characterize the canonical Buddy identity and caller-group domain

#### Why first

Establish the immutable domain contract without changing production. These
tests make the later CLI implementation consume the existing identity and
canonical digest mechanisms rather than inventing a key abstraction.

#### Files

- **Sources, read-only**:
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/identity.py`
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py`
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/canonical.py`
- **Canonical test file**:
  `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`
- **Existing regression file, unchanged**:
  `src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py`

#### Test-first scenarios

1. Retain
   `test_buddy_request_normalization_and_execution_derivation_are_strict`.
   Keep its strict normalization/error coverage and exact channel, Release
   Unit, and target assertions.
2. Add
   `test_buddy_execution_identity_document_and_concurrency_key_are_exact`.
   Use the existing fixed normalized Buddy intent and assert:
   - `derive_buddy_execution_identity(intent).to_document()` equals the
     literal four-member document: the existing schema literal plus
     `channel: buddy`, `release-unit: hcoona-release-smoke-npm`, and the fixed
     40-lowercase-hex target SHA;
   - no version, package coordinate, destination adapter, destination
     projection, request, or run member exists;
   - `canonical_sha256(the_exact_document)` equals a committed literal
     `sha256:<64-lowercase-hex>` regression value;
   - removing only `sha256:` with `removeprefix("sha256:")` equals the
     committed literal 64-lowercase-hex caller key.
3. Add
   `test_three_same_target_dispatches_share_one_caller_group_for_github_coalescing`.
   Model three dispatch contexts with the same normalized immutable target and
   differing request ID, workflow run ID, run attempt, ref, and actor facts.
   Derive all three through
   `derive_buddy_execution_identity(...).to_document()` and
   `canonical_sha256(...)`, then assert the exact same
   `wdv3-execution-<64hex>` group for all three. Treat this solely as the
   smallest reliable local proof of group equality: the first may run, at
   most one same-group run may remain pending, and a later same-group dispatch
   may replace that pending run without canceling the running one. Do not
   encode start order or fairness.
4. Add
   `test_different_buddy_targets_derive_different_execution_concurrency_keys`.
   Change only the valid immutable 40-lowercase-hex target SHA; assert
   different exact identity documents, prefixed canonical digests,
   unprefixed 64-hex keys, and `wdv3-execution-...` groups.

#### Exact increment command

Run immediately after adding the scenarios:

```bash
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py::test_buddy_request_normalization_and_execution_derivation_are_strict src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py::test_buddy_execution_identity_document_and_concurrency_key_are_exact src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py::test_three_same_target_dispatches_share_one_caller_group_for_github_coalescing src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py::test_different_buddy_targets_derive_different_execution_concurrency_keys
```

These characterization tests should pass without a production edit. If they
do not, stop rather than changing the canonical helpers: that would contradict
the confirmed bounded findings.

#### Success criteria

- [ ] Exact identity document and literal digest/key are pinned.
- [ ] Same-target equality and different-target inequality are independent.
- [ ] Every enumerated irrelevant request/run/version/destination fact is
      excluded structurally or varied without changing the key.
- [ ] No identity, record, or canonical production file changes.

---

### Phase 2: Add the real compile-live-model key producer

#### Files

- **Source**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
- **Canonical test file**:
  `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`

#### Test-first scenarios

1. Add
   `test_compile_live_model_emits_canonical_buddy_execution_concurrency_key`.
   Invoke the real parser path with
   `release compile-live-model --github-output <path>`, using the adjacent
   canonical temporary repository/provider fixture pattern. Assert:
   - request-local compilation succeeds before output is inspected;
   - existing Repository Model output lines remain exact;
   - the output file additionally contains exactly
     `execution-concurrency-key=<the Phase 1 literal 64-lowercase-hex key>`;
   - the value has no `sha256:` prefix;
   - it equals the key from the fixed normalized intent, despite compiled
     version/package/destination fields;
   - the Provider is not rerun, preserving the established boundary.
2. Add
   `test_compile_live_model_does_not_emit_execution_concurrency_key_when_compilation_fails`.
   Drive the real command through an established request-local compilation
   failure and assert no `execution-concurrency-key=` record is written. Keep
   the assertion limited to this output and existing failure behavior; do not
   introduce a new fake compiler or provider abstraction.

Run both nodes before production editing and retain the expected failure
evidence for the missing success output. Then make the smallest production
change and rerun the same nodes.

#### Exact production edit

In `_release_compile_live_model_command`, only after the existing
request-local Repository Model compilation has succeeded:

1. Reuse its normalized admitted `intent`; do not re-normalize, recompile, or
   rerun the Provider.
2. Compute exactly:

   ```python
   canonical_sha256(
       derive_buddy_execution_identity(intent).to_document()
   ).removeprefix("sha256:")
   ```

3. Add that value as `execution-concurrency-key` to the existing
   `--github-output` emission alongside the existing Repository Model
   outputs.

Do not add a helper, hash abstraction, lock, ledger, tag, destination salt, or
fallback. Do not derive from the compiled Snapshot, request ID, GitHub run
facts, canonical/native version, coordinate, adapter, or projection.

#### Exact increment command

```bash
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py::test_compile_live_model_emits_canonical_buddy_execution_concurrency_key src/public/lib/three-workflow-delivery-v3/tests/test_cli.py::test_compile_live_model_does_not_emit_execution_concurrency_key_when_compilation_fails
```

#### Success criteria

- [ ] The real successful CLI path emits the exact unprefixed 64-hex key.
- [ ] Failed request-local compilation does not emit the key.
- [ ] Existing Repository Model output and Provider behavior remain exact.
- [ ] `cli.py` is the only production file changed in this phase.

---

### Phase 3: Move caller concurrency to the forwarded domain key

#### Files

- **Source**:
  `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`
- **Canonical test file**:
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Reusable workflow context, read-only**:
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`

#### Test-first scenario

Strengthen the existing
`test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact`; do not add
a string-only duplicate. Its parsed-YAML assertions must pin:

1. The existing five-job DAG and order:
   request normalization and request-local `compile-model`, then
   `evaluate-live-eligibility`, then reusable admission at
   `run-live-attempt`.
2. `compile-model.outputs.execution-concurrency-key` is exactly
   `${{ steps.compile.outputs.execution-concurrency-key }}`, where `compile`
   is the real `release compile-live-model --github-output` step.
3. `evaluate-live-eligibility` forwards exactly
   `${{ needs.compile-model.outputs.execution-concurrency-key }}` as its own
   `execution-concurrency-key` output.
4. `run-live-attempt.concurrency.group` is exactly
   `wdv3-execution-${{ needs.evaluate-live-eligibility.outputs.execution-concurrency-key }}`.
5. `run-live-attempt.concurrency.cancel-in-progress` is the YAML boolean
   `false`.
6. The caller compile shell contains no request-specific key construction:
   no `printf`/`sha256sum` key pipeline and no use of request ID,
   `GITHUB_SHA`, workflow run ID, or run attempt to compute or emit
   `execution-concurrency-key`.
7. `run-live-attempt` remains the `uses`-only reusable caller job, with no
   `runs-on` or `steps`; concurrency therefore owns the complete same-revision
   reusable Attempt from admission through finalization.
8. The existing live-disabled/eligibility `if` gate, permissions, action pins,
   artifacts, inputs, secrets, and unrelated DAG mappings remain exact.

Run the node before editing and retain its expected failure against the old
shell producer. Then make the workflow-only edit and rerun it.

#### Exact production edit

1. In `compile-model`, keep the real
   `release compile-live-model --github-output` invocation and expose its step
   output as the job's `execution-concurrency-key`.
2. Delete only the shell lines that hash
   `request-id:GITHUB_SHA:buddy` and emit that request-specific key.
3. Preserve or minimally correct the exact three-hop forwarding:
   `steps.compile.outputs.execution-concurrency-key` ->
   `needs.compile-model.outputs.execution-concurrency-key` ->
   `needs.evaluate-live-eligibility.outputs.execution-concurrency-key`.
4. Keep caller group
   `wdv3-execution-${{ needs.evaluate-live-eligibility.outputs.execution-concurrency-key }}`
   and `cancel-in-progress: false` on `run-live-attempt`.
5. Do not change `.github/workflows/workflow-delivery-v3-live-attempt.yml`.

#### Exact increment commands

```bash
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py::test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact
python eng/scripts/hk_actionlint.py .github/workflows/workflow-delivery-v3-buddy-smoke.yml
```

#### Success criteria

- [ ] Producer and both forwarding hops are exact parsed-YAML assertions.
- [ ] Request/run facts are absent from shell key computation.
- [ ] Concurrency remains on the whole reusable caller job with no
      cancellation.
- [ ] Compilation/eligibility/live-disabled failures remain pre-Attempt.
- [ ] No callee or unrelated workflow behavior changes.

---

### Phase 4: Bounded regression and append-only evidence

#### Validation order

1. Re-run the Phase 1, Phase 2, and Phase 3 node commands independently so
   each increment remains diagnosable.
2. Run the bounded four-file regression, Pyrefly, Ruff check, Ruff format
   check, and caller-only actionlint commands listed above.
3. Review only the bounded diff and preserve every pre-existing path/change.
4. Append a uniquely delimited, timestamped implementation-results section to
   `.testagent/status.md`. This is evidence recording, not sentinel
   finalization. Map all 15 checklist items to exact test results or the
   hosted-GitHub blocker.

#### Append-only checks

Before implementation edits, capture the authoritative prefixes:

```bash
cp .testagent/research.md /tmp/wdv3-buddy-concurrency-implementation-research-prefix.md
cp .testagent/plan.md /tmp/wdv3-buddy-concurrency-implementation-plan-prefix.md
cp .testagent/status.md /tmp/wdv3-buddy-concurrency-implementation-status-prefix.md
```

After the status append, prove all captured content remains a byte prefix:

```bash
python -c 'from pathlib import Path; prefix=Path("/tmp/wdv3-buddy-concurrency-implementation-research-prefix.md").read_bytes(); current=Path(".testagent/research.md").read_bytes(); assert current.startswith(prefix), "research.md prefix changed"'
python -c 'from pathlib import Path; prefix=Path("/tmp/wdv3-buddy-concurrency-implementation-plan-prefix.md").read_bytes(); current=Path(".testagent/plan.md").read_bytes(); assert current.startswith(prefix), "plan.md prefix changed"'
python -c 'from pathlib import Path; prefix=Path("/tmp/wdv3-buddy-concurrency-implementation-status-prefix.md").read_bytes(); current=Path(".testagent/status.md").read_bytes(); assert current.startswith(prefix), "status.md prefix changed"'
python -c 'from pathlib import Path; import hashlib; prefix=Path(".testagent/research.md").read_bytes()[:247073]; assert len(prefix)==247073 and hashlib.sha256(prefix).hexdigest()=="64ab82657e5865817d91df5db3b3f5be6899f4aa05fe7496b9b5ef83cab7e5c2"'
```

#### Diff checks

```bash
git --no-pager diff --check -- .github/workflows/workflow-delivery-v3-buddy-smoke.yml src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/identity.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/canonical.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py .testagent/research.md .testagent/plan.md .testagent/status.md
git --no-pager diff -- .github/workflows/workflow-delivery-v3-buddy-smoke.yml src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/identity.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/canonical.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py .testagent/research.md .testagent/plan.md .testagent/status.md
```

Do not run the historical source-pairing analyzer again. Do not run full
package/HK, harness-wide discovery, acceptance probes, sentinel finalization,
publication, package mutation, commit, push, or PR operations.

---

### Independent requirement-to-edit-and-evidence map

| # | Exact planned production edit | Exact canonical test/evidence file | Named planned test/scenario or blocker |
|---:|---|---|---|
| 1 | `cli.py::_release_compile_live_model_command` derives from the normalized Buddy intent; no identity-helper edit. | `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py` | `test_buddy_execution_identity_document_and_concurrency_key_are_exact` pins `buddy`, `hcoona-release-smoke-npm`, and valid 40-lowercase-hex target. |
| 2 | Caller YAML uses the domain key with `cancel-in-progress: false`; no scheduler implementation is added. | `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py` and `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | `test_three_same_target_dispatches_share_one_caller_group_for_github_coalescing` plus `test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact`. **Blocker:** hosted replacement/order cannot be locally emulated; assert only equal group, one-running/one-pending documented semantics, and no running cancellation. |
| 3 | Concurrency stays on `run-live-attempt`; no Attempt/ledger code changes. | `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | `test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact` proves a pending caller has not invoked the reusable job. **Blocker:** local tests cannot observe GitHub replacing a hosted pending run; status must describe surviving callers as the only callers that can create Attempts, without claiming order. |
| 4 | `cli.py` applies the same composition to the target-bearing intent; workflow forwards it unchanged. | `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py` | `test_different_buddy_targets_derive_different_execution_concurrency_keys`. |
| 5 | Remove the request-specific shell hash; never add request/run/version/coordinate/destination salts to `cli.py`. | `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`, `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`, and `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | Exact-document test, three-dispatch irrelevant-facts test, real CLI key test, and workflow negative-shell assertions independently cover all excluded facts. |
| 6 | Place CLI key derivation/output after successful request-local compilation; preserve caller DAG and eligibility gate. | `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` and `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | `test_compile_live_model_does_not_emit_execution_concurrency_key_when_compilation_fails` and the strengthened DAG scenario. Eligibility failure remains pre-Attempt by the exact unchanged gate. |
| 7 | Keep `concurrency` on the `run-live-attempt` reusable `uses` job; no callee edit. | `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | `test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact` pins no `runs-on`/`steps` and the whole reusable-job boundary. |
| 8 | Keep YAML boolean `cancel-in-progress: false`. | `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | `test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact`. |
| 9 | Add exactly `canonical_sha256(derive_buddy_execution_identity(intent).to_document()).removeprefix("sha256:")` in `cli.py`; add no helper. | `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py` and `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | Exact literal document/digest test and `test_compile_live_model_emits_canonical_buddy_execution_concurrency_key`. |
| 10 | Emit `execution-concurrency-key` through the existing successful `--github-output` writer. | `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | `test_compile_live_model_emits_canonical_buddy_execution_concurrency_key` checks the real fixture and exact output file. |
| 11 | Delete only the caller shell hash; expose compile step output and forward through compile/evaluate/run to exact `wdv3-execution-${{ ... }}` group. | `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | `test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact` pins producer, two forwarding expressions, final group, and shell negatives. |
| 12 | No production edit for a ledger/lock/tag/service/credential/destination lock/general abstraction. | Bounded diff over the two production targets; no executable repository-wide absence test is appropriate. | **Explicit blocker/constraint evidence:** scoped diff review and the existing workflow contract can prove no such mechanism was added in the changed files, but cannot prove global absence beyond the authoritative bounded workspace. |
| 13 | Preserve all non-key CLI behavior and all non-key caller YAML; do not edit the reusable workflow. | `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` and `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | Exact full output assertions plus strengthened existing DAG/live-disabled/permissions/pins/artifact/reusable-boundary scenario; bounded four-file regression catches unrelated changes. |
| 14 | The two production edits are made only after their scenario-first red tests; no other production file changes. | All three changed canonical test files plus unchanged `src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py` | The five new named tests and one strengthened workflow test cover equality, inequality, exact identity/hash, CLI output, forwarding, shell absence, and whole-job concurrency. |
| 15 | This section is the uniquely delimited EOF plan append; implementation later appends only a uniquely delimited status section. | `.testagent/plan.md`, `.testagent/status.md`, and `.testagent/research.md` prefix commands | `append-only-prefix-evidence` scenario records exact command results and every checklist row. This is report evidence, not a production test or sentinel finalization. |

### Blocker boundary

There is no blocker to the local production/test repair. The sole evidence
boundary is GitHub-hosted concurrency scheduling: local tests cannot dispatch
or prove replacement timing, order, or fairness. They can and must prove the
three same-target requests have one exact caller group, different targets do
not, `cancel-in-progress` is false, and the reusable Attempt is invoked only
after caller concurrency admission.

<!-- END APPEND: 2026-08-19T200717Z-wdv3-buddy-caller-held-release-execution-concurrency-repair-plan -->

<!-- BEGIN APPEND: 2026-08-20T014646Z-wdv3-node-provider-lfs-regression-plan -->

## Plan: focused Node Provider LFS-smudge regression

### Phase 1 — Add one scenario-first parameterized regression

Edit only
`src/public/lib/three-workflow-delivery-v3/tests/repository/test_node_provider.py`.

Add
`test_internal_exact_target_git_materialization_skips_lfs_smudge_in_closed_environment`
with these named cases:

1. `lfs-budget-exhausted`
   - ambient `GIT_LFS_SKIP_SMUDGE` is absent;
   - the internal `git checkout --detach <target>` subprocess behaves like an
     exhausted LFS filter and fails unless its explicit environment contains
     `GIT_LFS_SKIP_SMUDGE=1`;
   - on success, assert exact target/NBGV binding, complete history and tags,
     exact authoritative local remote/refspec, non-persisted credentials,
     preserved closed environment controls, no global Git configuration, and
     temporary-repository cleanup.
2. `ordinary-checkout-failure`
   - after verifying suppression is present, inject an unrelated
     `CalledProcessError` at the same internal checkout boundary;
   - assert the Provider propagates its `ValueError` with the original error as
     cause, runs no PNPM/NBGV metadata command, and cleans the temporary
     repository.

Use only a safe environment projection in recorded assertions so unrelated
ambient values or credentials cannot appear in diagnostics.

### Phase 2 — Narrow validation and red-test diagnosis

1. Run the generated parameterized test without ambient LFS suppression.
2. If it fails because the internal Git subprocess receives no explicit
   environment, retain the non-skipped regression and record that exact
   production blocker. Do not weaken the assertion or modify production.
3. Run Ruff check, Ruff format check, and `git diff --check` on the bounded
   test/state files.
4. Re-open the generated test and map every checklist item to concrete
   assertions.
5. Run `test-gap-analysis` and `assertion-quality` against the final source/test
   pair. Record any accepted finding as the same test-only production blocker;
   do not broaden scope.
6. Append final results to `.testagent/status.md`.

### Requirement-to-evidence map

| Requirement | Planned evidence |
|---|---|
| Internal target Git gets exact LFS suppression | Generated test, safe subprocess-environment projection, and checkout gate |
| Closed/minimal environment preserved | Generated test compares all `_OFFLINE_ENVIRONMENT` controls |
| Exact target and NBGV binding | Generated success case checks checkout target/head and `gitCommitId` |
| Complete history and tags | Generated success case checks non-shallow ancestry/tags and exact tag fetch |
| Authoritative remote | Generated success case checks `origin`, local URI, and exact refspec |
| Credentials not persisted | Generated success case checks exact `False` evidence |
| Failures propagate | Generated ordinary-failure case checks wrapper message, cause type, and metadata non-execution |
| Local/no global weakening | Generated test checks local URI, no network command, and no `git config` invocation |
| Test-only bounded change | Final bounded diff |
| Append-only state | Captured prefix comparisons for all three files |

### Stop condition

Stop after the focused test and bounded validation. Do not edit
`node_provider.py`, workflows, manifests, locks, or global Git/LFS settings.
The delivered production omission is a reportable blocker, not permission to
implement the production fix.

<!-- END APPEND: 2026-08-20T014646Z-wdv3-node-provider-lfs-regression-plan -->

<!-- BEGIN APPEND: 2026-08-20T042859Z-pr552-codeql-closure-regression-plan -->

## PR #552 CodeQL-Closure Test Implementation Plan

### Overview

Use a **targeted, scenario-first** strategy because every bounded surface is
partially covered or has a contradictory stale test. Implement only tests and
minimal test-local helpers in the four canonical test modules below. Production
Python, workflow YAML, CodeQL configuration, and existing source inventory are
read-only. Do not skip, xfail, suppress, or repair the intentional red
regressions.

Node Provider LFS repair `2c0c1c24`, its source, and its tests are expressly out
of scope. The current workspace is authoritative; do not restore missing or
orphaned source.

### Edit boundary

Permitted implementation edits:

1. `src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py`
2. `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`
3. `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
4. `tests/test_workflow_release_control.py`
5. one uniquely delimited EOF append to `.testagent/status.md`

This plan append is the only permitted `.testagent/plan.md` change. Keep
`.testagent/research.md` unchanged during implementation. Do not edit or delete:

- `eng/scripts/workflow_delivery_v3_consumer_policy.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py`
- any `.github/workflows/*` file, including
  `release-build-variant.yml`
- CodeQL configuration, alert state, manifests, locks, or unrelated tests

Before Phase 1, capture the authoritative state:

```bash
cp .testagent/research.md /tmp/pr552-codeql-research-prefix.md
cp .testagent/plan.md /tmp/pr552-codeql-plan-prefix.md
cp .testagent/status.md /tmp/pr552-codeql-status-prefix.md
git status --short
```

Retain the initial status as the ownership boundary; never revert unrelated
workspace changes.

### Commands

- **Build**: not required for this tests/docs-only scope.
- **Consumer tests**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py -k 'token and (unterminated or escaped)'`
- **Proxy and fake-transport tests**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py -k 'closure_bound or absolute_form or upstream_response_header or authenticated_github_package_version_metadata'`
- **Workflow contracts**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py -k 'exact_target_checkouts or only_dispatch_same_commit or target_sha_stays_bound'`
- **Release topology**:
  `uv run --python 3.13 pytest -q tests/test_workflow_release_control.py -k 'release_build_variant'`
- **Scoped v3 collection**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Root collection**:
  `uv run --python 3.13 pytest --collect-only -q`
- **Explicit root release collection**:
  `uv run --python 3.13 pytest --collect-only -q tests/test_workflow_release_control.py -k 'release_build_variant'`
- **Lint**:
  `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py tests/test_workflow_release_control.py`
- **Format**:
  `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py tests/test_workflow_release_control.py`
- **Optional focused type check**:
  `uv run --python 3.13 pyrefly check src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py tests/test_workflow_release_control.py`
- **Three-alert predicate check**:
  `rg -n '"api\.github\.com" in url|"api\.github\.com" in call\[0\]' src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`
  must return no matches.
- **Whitespace**: `git --no-pager diff --check`

Do not replace these commands with the recursive pnpm suite, full package/HK
execution, live acceptance probes, or any networked workflow run.

### Expected current-workspace classification

| Focused command | Expected green evidence | Expected intentional red evidence |
|---|---|---|
| Consumer | four escaped-valid controls | two small structural quote cases and four child-process large-payload cases |
| Proxy/fake | exact API/lookalike fake, absolute-form rejection, legal response relay | closure-bound method/path case and four CR/LF header cases |
| Workflow | exact inventory, sole caller, caller chain, callee binding/publication equality | 11 checkout-ref parameter cases, each reporting `${{ inputs.target-sha }}` instead of `${{ github.sha }}` |
| Release | two active-workflow no-reference cases | orphan-file absence case |
| Collection/Ruff/format/predicate check | green | none |

The intentional reds must remain ordinary collected failures, not
`skip`/`xfail`. A different failure shape is diagnostic and must be recorded
verbatim rather than normalized away.

### Phase Summary

| Phase | Focus | Assigned test file | Estimated selected nodes |
|---|---|---|---:|
| 1 | `_TOKEN` overlap, bounded ReDoS, escaped controls | `tests/ci/test_consumer_policy.py` | 10 |
| 2 | exact fake URL matching and acceptance proxy boundary | `tests/adapters/test_commit10_acceptance_probes.py` | 8 |
| 3 | same-commit caller/callee and all live checkouts | `tests/contracts/test_buddy_workflows.py` | 15 |
| 4 | orphan release-workflow topology | `tests/test_workflow_release_control.py` | 3 |
| 5 | bounded validation, reviews, append-only evidence | no test-file edits | 0 |

Each target test file belongs to exactly one phase. Finish and run each phase's
focused command before starting the next.

---

## Phase 1: Consumer `_TOKEN` overlap and bounded tokenization

### Overview

Establish the leaf/core regression first. The small direct invariant gives a
fast structural failure, while every expensive payload runs in a separately
reaped child process. No mock is required.

### File to Test

#### `workflow_delivery_v3_consumer_policy.py`

- **Source (read-only)**:
  `eng/scripts/workflow_delivery_v3_consumer_policy.py`
- **Test file**:
  `src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py`
- **Test module**: `test_consumer_policy`
- **Symbols**: `_TOKEN.fullmatch`, `_manager_references`, `_lockfile`

### Minimal test-local helper

Add `_run_tokenization_probe_in_child(route, payload)`:

- invoke `sys.executable` without `shell=True`;
- pass `POLICY_IMPLEMENTATION_PATH`, route, and payload to a small child
  importer;
- route `command-argument` calls
  `_manager_references("npm install " + payload)`;
- route `bun-lock` calls `_lockfile("bun.lock", payload.encode())`;
- emit one exact JSON boolean indicating whether a consumer was found;
- use `subprocess.run(..., timeout=1.0, check=False, capture_output=True,
  text=True)`;
- convert `TimeoutExpired` into `pytest.fail` containing the route and quote
  ID; rely on `subprocess.run` to kill and wait for the child;
- assert exact return code and stdout so child import/runtime errors cannot be
  mistaken for a safe result.

Never evaluate the 64-pair payload in the pytest process.

### Tests

1. `test_unterminated_quoted_token_is_not_reclassified_as_ordinary`
   - **Parameters**:
     - `double`: payload is an opening `"` followed by `\a`, with no close.
     - `single`: payload is an opening `'` followed by `\a`, with no close.
   - **Assertion**:
     `policy._TOKEN.fullmatch(payload) is None`.
   - This is a functional token-boundary invariant only. Do not assert the
     regex text, branch order, or scanner implementation.
   - **Expected now**: red for both IDs because the ordinary fallback accepts
     the quote-bearing token.

2. `test_unterminated_escaped_quoted_tokenization_completes_without_consumer_match`
   - **Parameter IDs**:
     `command-argument-double`, `command-argument-single`,
     `bun-lock-double`, `bun-lock-single`.
   - **Payload**: selected opening quote + exactly 64 `\a` pairs, with no
     closing quote or consumer package token.
   - **Mocks/fakes**: child process only; no monkeypatch of the policy.
   - **Assertions**:
     each child exits normally inside one second, returns exact JSON `false`,
     and emits no false consumer match. Timeout or nonzero exit is a focused
     failure naming the case.
   - **Expected now**: red for all four IDs, normally by timeout.

3. `test_escaped_quoted_token_preserves_consumer_match`
   - **Parameter IDs**: the same four route/quote combinations.
   - **Inputs**:
     - double-quoted decoy: `"ordinary \" quoted content"`;
     - single-quoted decoy: `'ordinary \' quoted content'`;
     - append the exact canonical consumer token already used by the adjacent
       command and `bun.lock` positive tests (the repository's
       `@hcoona/hcoona-release-smoke-npm` token);
     - command form remains rooted at `npm install`; lock form is UTF-8 bytes
       passed with path `bun.lock`.
   - **Assertions**:
     command cases return `True`; Bun cases' returned reference set contains
     the exact canonical token. The escaped quote and embedded whitespace must
     not consume or hide the following valid token.
   - **Expected now**: green for all four IDs.

### Phase 1 success criteria

- [ ] Large risky inputs only execute in child processes.
- [ ] Four route/quote combinations have descriptive, independent node IDs.
- [ ] Both quote forms have fast direct fallback evidence.
- [ ] Four well-formed escaped controls preserve existing detection behavior.
- [ ] Focused command is run and its exact red/green nodes retained.

---

## Phase 2: Exact fake GitHub URL matching and acceptance proxy closure

### Overview

First repair only the three alerted predicates in the existing test fake.
Then add behavioral proxy scenarios using loopback HTTP and a monkeypatched
HTTPS boundary. No request may reach an external network.

### File to Test

#### Acceptance transport and proxy boundaries

- **Sources (read-only)**:
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py`
- **Test file**:
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`
- **Test module**: `test_commit10_acceptance_probes`
- **Symbols**:
  `AcceptanceMutationProxy`, `ValidatedAcceptanceRequestProof`,
  `_AcceptanceNpmTransport.observe`

### Minimal test-only helpers

1. Add one narrowly named exact-origin predicate, for example
   `_is_exact_github_api_url(url)`:
   - parse once with `urllib.parse.urlsplit(url)`;
   - return only
     `parts.scheme == "https" and parts.netloc == "api.github.com"`.
   - Do not use substring, suffix, hostname-lookalike, regex, or suppression
     logic.

2. Add or extend a loopback request helper that returns the local response's
   exact status, complete headers, and body. Always close the loopback
   connection and proxy in `finally`/context cleanup.

3. Keep fake upstream objects at the documented
   `Connection`/`Response` shape. Every proxy test below must monkeypatch
   `cli_module.http.client.HTTPSConnection`; constructors and requests must be
   recorded.

### Tests

1. Modify only
   `test_acceptance_observation_requires_authenticated_github_package_version_metadata`
   - Replace its two `MetadataTransport.get` API-response branch predicates and
     its `api_calls` filter with the exact parsed-origin predicate.
   - Change the fake tarball URL to
     `https://api.github.com.example.invalid/tar.tgz`.
   - Keep existing exact API metadata URLs and authentication assertions.
   - Assert the lookalike URL returns the tarball bytes, appears in the full
     call log, and does **not** appear in `api_calls`; every retained API call
     parses to exact HTTPS + exact `api.github.com`.
   - **Expected now after this test-only edit**: green.

2. `test_acceptance_proxy_uses_closure_bound_method_and_path_after_handler_mutation`
   - **Inputs**:
     `expected_method="PUT"`,
     canonical fixed scoped-package path
     `/@hcoona%2fhcoona-release-smoke-npm`, and
     `_adversarial_publish_body(...)`.
   - Capture the live handler by wrapping
     `proxy._server.RequestHandlerClass._forward` and retaining `self` before
     delegating.
   - In fake `HTTPSConnection.__init__`, after qualification and before
     `.request`, mutate that retained handler to
     `command="DELETE"` and
     `path="https://api.github.com.example.invalid/attacker"`.
   - Record fake `.request(method, path, body, headers)`.
   - **Assertions**:
     the only upstream request uses exactly `PUT` and the fixed canonical path,
     never either mutated value; body/auth remain the already-qualified values.
   - **Expected now**: red because production forwards mutable handler fields.

3. `test_acceptance_proxy_rejects_absolute_form_target_before_upstream`
   - Send a loopback `PUT` whose request target is the absolute form
     `https://npm.pkg.github.com/@hcoona%2fhcoona-release-smoke-npm`.
   - The proxy still expects the canonical origin-form path.
   - Install a fail-on-construction fake `HTTPSConnection`.
   - **Assertions**:
     preserve the exact local rejection status already used by the adjacent
     method/path rejection matrix; connection-constructor count and request
     count are zero; `proxy.proof is None`; and
     `proxy.processed.is_set() is False`.
   - **Expected now**: green.

4. `test_acceptance_proxy_relays_legal_upstream_response_headers_status_and_body`
   - Configure `drop_accepted_response=False`.
   - Fake response:
     status `201`, body `b'{"ok":true}'`, headers
     `("Content-Type", "application/json")` and
     `("X-GitHub-Request-Id", "request-123")`.
   - **Assertions**:
     the loopback client receives exact status, exact body, and both legal
     non-hop-by-hop headers; proof records exact upstream response evidence and
     `processed` is set only for this accepted exchange.
   - **Expected now**: green.

5. `test_acceptance_proxy_rejects_illegal_upstream_response_header`
   - **Parameter IDs and fake headers**:
     - `header-name-cr`: `("X-Bad\rName", "value")`
     - `header-name-lf`: `("X-Bad\nName", "value")`
     - `header-value-cr`: `("X-Bad", "before\rafter")`
     - `header-value-lf`: `("X-Bad", "before\nafter")`
   - Each fake response otherwise uses status `201`, a legal body, and
     `drop_accepted_response=False`.
   - **Assertions for every ID**:
     local response status is exactly `502`; no illegal header is accepted or
     relayed; `proxy.proof is None`; and
     `proxy.processed.is_set() is False`.
   - **Expected now**: red for all four IDs because proof/processed currently
     precede header validation and CR/LF is not rejected.

### Phase 2 success criteria

- [ ] Exactly two fake response branches and one call filter use `urlsplit`.
- [ ] The lookalike host is positive tarball evidence and negative API evidence.
- [ ] Every proxy scenario monkeypatches `HTTPSConnection`.
- [ ] Only loopback traffic occurs; no npm invocation or external request.
- [ ] Legal relay and four independent illegal-header cases assert deep state.
- [ ] Focused command and the zero-match predicate check are run.

---

## Phase 3: Canonical same-commit workflow contracts

### Overview

Parse the authoritative workflows with existing YAML helpers. Do not mock or
snapshot raw files. Separate inventory from per-job ref checks so all 11
omissions receive distinct node IDs.

### File to Test

#### Buddy caller and live-attempt workflow

- **Contracts (read-only)**:
  - `.github/workflows/workflow-delivery-v3-live-attempt.yml`
  - `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`
- **Test file**:
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Test module**: `test_buddy_workflows`
- **Helpers**:
  `_document`, `_needs`, `_steps`, `_step`, `_run`

### Tests

1. `test_live_attempt_exact_target_checkouts_inventory_is_complete`
   - Parse every step named `Check out exact selected target`.
   - Assert exactly one such step in each and only each of:
     `admit`, `plan-qualification`, `build-tarball`, `project-test`,
     `npm-artifact-qualification`, `qualification-finalizer`,
     `observe-github-packages`, `materialize-publication`,
     `approval-finalizer`, `publish-github-packages`, and
     `release-finalizer`.
   - Assert the publisher job remains protected by environment
     `workflow-delivery-v3-buddy-smoke-github-packages`.
   - **Expected now**: green.

2. `test_live_attempt_exact_target_checkouts_use_github_sha`
   - Parameterize the exact 11-job tuple above, with each job name as its node
     ID.
   - For each job, locate the unique named checkout and assert
     `step["with"]["ref"] == "${{ github.sha }}"`.
   - The `publish-github-packages` parameter is mandatory and must not be
     split into weaker publisher-only logic.
   - **Expected now**: 11 red nodes; actual value is
     `${{ inputs.target-sha }}` in every case.

3. `test_buddy_is_only_dispatch_same_commit_local_live_attempt_caller`
   - Parse both `*.yml` and `*.yaml` under `.github/workflows`.
   - Collect jobs whose `uses` is exactly
     `./.github/workflows/workflow-delivery-v3-live-attempt.yml`.
   - **Assertions**:
     the exact singleton is
     `(workflow-delivery-v3-buddy-smoke.yml, run-live-attempt)`; the caller's
     trigger map has only `workflow_dispatch` (using
     `document.get("on", document.get(True))`); and its local `uses` string is
     unchanged.
   - **Expected now**: green.

4. `test_buddy_target_sha_stays_bound_from_github_sha_to_live_attempt`
   - Assert the request shell emits exact `target-sha=${GITHUB_SHA}` and the
     step receives the workflow's `github.sha` as `GITHUB_SHA`.
   - Assert exact edge equality, without recomputation or fallback, through:
     `steps.request.outputs.target-sha` -> request job output -> discovery job
     output -> compile job output -> eligibility job output ->
     `run-live-attempt.with.target-sha`.
   - Also assert each of the caller's four pre-Attempt
     `Check out exact selected target` steps has
     `with.ref == "${{ github.sha }}"`.
   - **Expected now**: green.

5. `test_live_attempt_target_sha_stays_bound_before_attempt_creation_and_publication`
   - Assert `admit.outputs.target-sha` is exactly
     `${{ inputs.target-sha }}`.
   - Assert the Release Attempt binding command consumes that same input, and
     its step occurs before the `Upload Release Attempt binding` step.
   - Assert publication consumes the same admitted/input target, with no
     alternate SHA expression, before/at the
     `publish-github-packages` publication boundary.
   - Assert ordering by step indexes and exact parsed expressions, not broad
     text position.
   - **Expected now**: green.

### Phase 3 success criteria

- [ ] Inventory proves exactly 11 named checkout owners.
- [ ] Eleven parameterized ref nodes expose all current omissions.
- [ ] Environment-protected publisher is covered by inventory and ref checks.
- [ ] Sole local caller and dispatch-only trigger are independently proven.
- [ ] Caller and callee target equality/order are independently proven.
- [ ] No workflow YAML is edited.

---

## Phase 4: Negative release-variant topology

### Overview

Replace only the stale positive orphan-workflow test. Keep file absence
separate from active-workflow no-reference evidence so the existing orphan
produces one intentional red without hiding the green topology controls.

### File to Test

#### Release workflow topology

- **Contracts (read-only)**:
  - `.github/workflows/release-build-variant.yml`
  - `.github/workflows/official.yml`
  - `.github/workflows/release-orchestrate.yml`
- **Test file**: `tests/test_workflow_release_control.py`
- **Test module**: `test_workflow_release_control`
- **Helpers**: `_workflow`, `_workflow_yaml`

### Tests

1. Delete only the stale test function
   `test_release_build_variant_runs_control_from_trusted_checkout`.
   Do not alter adjacent release-control tests.

2. Add `test_release_build_variant_workflow_is_absent`
   - Resolve `_workflow("release-build-variant.yml")`.
   - Assert the path does not exist; do not parse, delete, rename, or ignore
     the orphan.
   - **Expected now**: red while the file exists.

3. Add `test_release_build_variant_has_no_active_workflow_reference`
   - Parameterize exact IDs `official.yml` and `release-orchestrate.yml`.
   - For each, assert the active workflow itself exists, read it through
     `_workflow_yaml(name)`, and assert
     `release-build-variant.yml` is absent from its local workflow references.
   - Retain the active topology distinction: `official.yml` delegates to
     `release-orchestrate.yml`; neither delegates to the orphan.
   - **Expected now**: green for both IDs.

### Phase 4 success criteria

- [ ] Exactly one stale positive function is removed.
- [ ] Absence and no-reference facts are separate tests.
- [ ] The orphan file remains untouched and yields one ordinary red failure.
- [ ] Both active workflows yield independently named green nodes.

---

## Phase 5: Validation, quality review, and append-only status

### Validation order

1. Re-run each phase's exact focused pytest command independently.
2. Run scoped v3 collection, root collection, and explicit release collection.
3. Run Ruff check, Ruff format check, the three-alert zero-match query, and
   `git --no-pager diff --check`.
4. Optionally run the exact focused Pyrefly command; report it as optional, not
   as a substitute for Ruff.
5. Inspect only the bounded diff. Confirm no production Python, workflow YAML,
   CodeQL configuration, or Node Provider LFS path was changed.
6. Run the `test-gap-analysis` review against the four canonical
   source/contract-to-test pairs. Require it to check route × quote coverage,
   post-qualification mutation, all four CR/LF positions, all 11 checkout
   jobs, and independent release topology facts.
7. Run the `assertion-quality` review against the four changed test modules.
   Require exact values/call counts/state assertions; reject truthiness-only,
   self-referential, source-string, or assertion-free scenarios.
8. Perform a final prompt-coverage review using the independent C1.1-C7.3 map
   below. Confirm every parameter ID is collected and every expected red is
   attributable to the documented production/workflow/file omission.
9. Append one uniquely delimited timestamped PR #552 result section to
   `.testagent/status.md`; do not edit earlier bytes.
10. Stop. Do not repair the red blockers.

### Exact expected node classification

| Proposed node(s) | Current expectation |
|---|---|
| `test_unterminated_quoted_token_is_not_reclassified_as_ordinary[double|single]` | red |
| `test_unterminated_escaped_quoted_tokenization_completes_without_consumer_match[command-argument-double|command-argument-single|bun-lock-double|bun-lock-single]` | red |
| `test_escaped_quoted_token_preserves_consumer_match[...]` (4 IDs) | green |
| `test_acceptance_observation_requires_authenticated_github_package_version_metadata` | green |
| `test_acceptance_proxy_uses_closure_bound_method_and_path_after_handler_mutation` | red |
| `test_acceptance_proxy_rejects_absolute_form_target_before_upstream` | green |
| `test_acceptance_proxy_relays_legal_upstream_response_headers_status_and_body` | green |
| `test_acceptance_proxy_rejects_illegal_upstream_response_header[...]` (4 IDs) | red |
| `test_live_attempt_exact_target_checkouts_inventory_is_complete` | green |
| `test_live_attempt_exact_target_checkouts_use_github_sha[...]` (11 job IDs) | red |
| `test_buddy_is_only_dispatch_same_commit_local_live_attempt_caller` | green |
| both `target_sha_stays_bound` tests | green |
| `test_release_build_variant_workflow_is_absent` | red |
| `test_release_build_variant_has_no_active_workflow_reference[official.yml|release-orchestrate.yml]` | green |

### Append-only status content

The status append must record:

- exact commands, pass/fail counts, durations, and full failing node IDs;
- timeout versus structural-fallback evidence for each consumer case;
- mutable method/path and each CR/LF header failure;
- all 11 checkout job IDs and their actual `${{ inputs.target-sha }}` value;
- the existing orphan path failure;
- green control nodes, including lookalike exclusion, absolute-form rejection,
  legal relay, caller/callee SHA binding, and both active no-reference cases;
- Ruff/format/collection/predicate-check results;
- `test-gap-analysis`, `assertion-quality`, and manual prompt-review findings;
- confirmation that no real upstream network/npm call occurred;
- confirmation that only the three fake substring predicates changed in the
  fake matcher;
- confirmation of no production/workflow/LFS/config/suppression/dismissal
  change.

After the append, prove the implementation did not alter research or this plan
and preserved the status prefix:

```bash
python -c 'from pathlib import Path; assert Path(".testagent/research.md").read_bytes() == Path("/tmp/pr552-codeql-research-prefix.md").read_bytes(), "research.md changed"'
python -c 'from pathlib import Path; assert Path(".testagent/plan.md").read_bytes() == Path("/tmp/pr552-codeql-plan-prefix.md").read_bytes(), "plan.md changed"'
python -c 'from pathlib import Path; prefix=Path("/tmp/pr552-codeql-status-prefix.md").read_bytes(); current=Path(".testagent/status.md").read_bytes(); assert current.startswith(prefix) and len(current) > len(prefix), "status.md was not append-only"'
```

---

## Independent requirement-to-test/evidence map

| ID | Exact file and proposed test/evidence | Inputs and mocks/fakes | Required assertions | Expected now |
|---|---|---|---|---|
| C1.1 | `tests/ci/test_consumer_policy.py::test_unterminated_escaped_quoted_tokenization_completes_without_consumer_match` | 64-pair payload; `sys.executable` child; 1.0-second timeout | Child is killed/reaped on timeout; normal completion is required and false consumer result is exact | red |
| C1.2 | Same test, IDs `command-argument-double`, `command-argument-single`, `bun-lock-double`, `bun-lock-single` | `_manager_references("npm install " + payload)` or `_lockfile("bun.lock", payload.encode())` | All four IDs collect independently and identify route/quote in failures | red ×4 |
| C1.3 | Same four nodes | Opening quote + 64 `\a` pairs, no close/token; no policy mock | Completion within bound and exact no-match; nonzero child exit cannot pass | red ×4 |
| C1.4 | `tests/ci/test_consumer_policy.py::test_escaped_quoted_token_preserves_consumer_match` (same four IDs) | Closed escaped single/double decoy with whitespace followed by canonical package token; no mocks | Command result is true; Bun result contains the exact token | green ×4 |
| C1.5 | `tests/ci/test_consumer_policy.py::test_unterminated_quoted_token_is_not_reclassified_as_ordinary[double|single]` | Small `"\a` / `'\a` inputs; direct `_TOKEN.fullmatch` | Both full matches are `None`; no regex-string assertion | red ×2 |
| C2.1 | `tests/adapters/test_commit10_acceptance_probes.py::test_acceptance_proxy_uses_closure_bound_method_and_path_after_handler_mutation` | Expected `PUT` + fixed package path; captured handler mutated to `DELETE` + attacker absolute path; fake HTTPS connection | Sole fake upstream call receives closure-bound method/path and never mutated fields | red |
| C2.2 | `...::test_acceptance_proxy_rejects_absolute_form_target_before_upstream` | Absolute-form HTTPS target over loopback; fail-on-construction HTTPS fake | Exact local rejection, zero constructors/requests, no proof, processed false | green |
| C2.3 | `...::test_acceptance_proxy_relays_legal_upstream_response_headers_status_and_body` | Fake 201, JSON body, `Content-Type`, request-ID; `drop_accepted_response=False` | Exact local status/body/legal headers plus accepted proof/processed state | green |
| C2.4 | `...::test_acceptance_proxy_rejects_illegal_upstream_response_header` IDs `header-name-cr`, `header-name-lf`, `header-value-cr`, `header-value-lf` | Four fake `getheaders()` results; fake 201 | Four independently collected cases cover CR/LF × name/value | red ×4 |
| C2.5 | Same four illegal-header nodes | As above | Exact local 502, `proof is None`, `processed.is_set() is False`, no illegal relay | red ×4 |
| C2.6 | All four Phase 2 proxy tests | Monkeypatched `cli_module.http.client.HTTPSConnection`; loopback client only | Constructor/request logs prove no external network; resources close | mixed by scenario |
| C3.1 | Existing `...::test_acceptance_observation_requires_authenticated_github_package_version_metadata` | Exact-origin helper used at only two response branches and one `api_calls` filter | Those three substring predicates are replaced and zero-match `rg` succeeds | green |
| C3.2 | Same existing test | `urlsplit`; exact API URLs and `https://api.github.com.example.invalid/tar.tgz` | Scheme equals `https`, netloc equals `api.github.com`; lookalike serves tarball but is excluded from API calls | green |
| C3.3 | Same test plus bounded diff/predicate evidence | Test fake only; no production transport mock change or suppression | Existing metadata/auth behavior passes; no alerted predicate remains; production diff is empty | green |
| C4.1 | `tests/contracts/test_buddy_workflows.py::test_live_attempt_exact_target_checkouts_inventory_is_complete` and parameterized `test_live_attempt_exact_target_checkouts_use_github_sha` | Parsed live workflow; exact 11-job tuple; no mocks | Exact inventory and each unique checkout ref equals `${{ github.sha }}` | inventory green, refs red ×11 |
| C4.2 | `...::test_buddy_is_only_dispatch_same_commit_local_live_attempt_caller` | Parsed `*.yml`/`*.yaml`; exact local callee `uses` | Singleton Buddy/run-live-attempt caller and workflow_dispatch-only trigger | green |
| C4.3 | `...::test_buddy_target_sha_stays_bound_from_github_sha_to_live_attempt` | Parsed caller outputs/run/env; no mocks | `GITHUB_SHA` emission and unchanged request -> discovery -> compile -> eligibility -> callee equality | green |
| C4.4 | `...::test_live_attempt_target_sha_stays_bound_before_attempt_creation_and_publication` | Parsed admit outputs, binding/upload step order, publisher expression | Input equality at admit/bind/publication and binding before upload/publication | green |
| C4.5 | Checkout inventory and per-job ref tests, especially ID `publish-github-packages` | Publisher environment and named checkout | Exact protected environment plus same `${{ github.sha }}` requirement | environment green, ref red |
| C4.6 | `test_live_attempt_exact_target_checkouts_use_github_sha` with 11 job IDs | Current parsed refs | Ordinary failure output identifies every `${{ inputs.target-sha }}` omission; no xfail/workflow edit | red ×11 |
| C5.1 | `tests/test_workflow_release_control.py`: remove only `test_release_build_variant_runs_control_from_trusted_checkout`; add the two tests below | No mocks | Adjacent tests and helpers remain unchanged | bounded edit |
| C5.2 | `...::test_release_build_variant_workflow_is_absent` | `_workflow("release-build-variant.yml")` | Exact path does not exist | red |
| C5.3 | `...::test_release_build_variant_has_no_active_workflow_reference[official.yml|release-orchestrate.yml]` | Existing active YAML text | Each active file exists and independently lacks the orphan reference; official still delegates to orchestrator | green ×2 |
| C5.4 | Absence node plus bounded diff | Existing orphan is read-only | Failure is retained and YAML is neither deleted nor modified | red |
| C6.1 | All four bounded test files; final scoped diff and status evidence | No production/workflow edits, suppression, config, dismissal, or source restore | Only allowed tests/helpers and append-only status are implementation-owned changes | green process gate |
| C7.1 | Four exact focused commands, collection, Ruff/format, predicate query, diff check | Current workspace with intentional omissions | Exact red/green counts and failing node IDs are retained; lint/collection remain green | mixed tests, green tooling |
| C7.2 | `.testagent/status.md` uniquely delimited EOF append | Captured prefix plus exact command/review results | Prior bytes preserved; all blockers and self-review items recorded | green process gate |
| C7.3 | Phase 5 stop condition | No repair step after evidence collection | Stop after tests, lint, reviews, and status append; no production/workflow/orphan repair | enforced |

### Prompt coverage check

| User scenario | Covered by |
|---|---|
| 1. `_TOKEN` structural/large/escaped routes | Phase 1; C1.1-C1.5 |
| 2. proxy closure, absolute form, relay, CR/LF, no network | Phase 2 proxy tests; C2.1-C2.6 |
| 3. exactly three fake GitHub matcher repairs | Phase 2 existing metadata test; C3.1-C3.3 |
| 4. all v3 checkouts, publisher, sole caller, target binding | Phase 3; C4.1-C4.6 |
| 5. absent orphan and no active references | Phase 4; C5.1-C5.4 |
| 6. exact commands, red/green report, quality reviews, append-only status | Phase 5; C6.1-C7.3 |

### Final stop condition

Stop with the intentional regressions red and fully reported. Do not modify
production Python, workflow YAML, the orphan file, CodeQL state, or Node
Provider LFS work to obtain a green suite.

<!-- END APPEND: 2026-08-20T042859Z-pr552-codeql-closure-regression-plan -->
<!-- BEGIN APPEND: 2026-08-21-wdv3-precoexistence-bootstrap-plan -->

# Workflow Delivery v3 Pre-Coexistence Bootstrap Test Plan

## Phase 1: Pure policy scenarios

Add scenario-first tests in `tests/ci/test_scenarios.py`:

1. A representative 283-unclassified-path blocked Plan remains a canonical
   failure Decision but qualifies for projection when the exact base marker is
   absent.
2. The predicate self-disables when the base marker is present.
3. A matrix rejects manual validation, project-test failure, supersession,
   event identity drift, and mixed/nonexact diagnostics.

## Phase 2: CLI and exact Git boundary

Add tests in `tests/test_cli.py`:

1. A real Git base without the marker reports absence, and a later commit with
   the marker reports presence.
2. `ci project-bootstrap-shadow` re-admits canonical Plan, Decision, and
   summary; appends a bootstrap note; and leaves record bytes unchanged.
3. Missing/malformed/noncanonical Decision or summary, invalid/nonexistent
   base identity, marker presence, and ineligible Decision return nonzero.

## Phase 3: Workflow projection contract

Add tests in `tests/contracts/test_ci_workflow.py` proving:

1. `ci finalize` still runs and its status is captured.
2. Canonical success returns directly.
3. Manual failure returns the Finalizer status unchanged.
4. Pull-request failure invokes only `ci project-bootstrap-shadow` with exact
   base/head/tested-merge/request bindings.
5. The command has no PR, branch, or SHA literal and no release capability.
6. The no-Decision fallback remains last and terminal.

## Phase 4: Production response

- Add the pure projection predicate beside CI Finalizer policy.
- Add exact base commit/path detection and the projection CLI command.
- Update only the shadow Finalizer workflow shell around the existing
  Finalizer invocation.
- Preserve all canonical Decision and summary formation.

## Validation and quality gates

1. Run the focused red/green tests for the three changed modules.
2. Run complete CI workflow contracts, scenarios, CLI tests, and the complete
   Workflow Delivery v3 package suite.
3. Run Ruff, Ruff format, Pyrefly, actionlint, package build, targeted HK, and
   the complete committed-range HK gate.
4. Invoke `test-gap-analysis` and `assertion-quality`; repair every confirmed
   finding.
5. Run independent multi-angle implementation reviews, adjudicate each atomic
   finding independently, and repeat until reviewers report no findings.
6. Update `.testagent/status.md` with exact test names and results.

<!-- END APPEND: 2026-08-21-wdv3-precoexistence-bootstrap-plan -->

<!-- BEGIN APPEND: 2026-08-26-wdv3-acceptance-proof-repair -->

## Workflow Delivery v3 Acceptance Proof Repair Plan

1. Add failing offline regressions for normal proof propagation, proof-free
   incompleteness, exact readback, and optional diagnostic closure.
2. Propagate the validated 201 proof from the normal single-create runner path.
3. Form `protocol-confirmed` only from proof, admitted execution and mutation
   startedness, and exact post-readback.
4. Add an optional closed runner diagnostic without making it completeness or
   activation authority.
5. Extend Governance admission compatibly: old proof-free `created` evidence
   remains replayable; new proof-bound facts are cross-checked when present.
6. Run focused and complete v3 tests, Python quality gates, HK, test-quality
   analysis, and independent review.

The plan excludes any third acceptance invocation or live operation.

<!-- END APPEND: 2026-08-26-wdv3-acceptance-proof-repair -->

<!-- BEGIN APPEND: 2026-08-26-wdv3-acceptance-retry-3-fallback -->

## Retry-3 fallback test phase plan

1. Update only obsolete “first unreviewed coordinate” negatives from `.9` to
   `.13`; retain valid cross-coordinate substitution negatives.
2. Add Adapter tests:
   - exact profile/history:
     `test_retry_3_suite_resolves_exact_coordinates_and_preserves_history`;
   - suite execution:
     `test_retry_3_suite_executes_with_exact_base_coordinate_and_tag`;
   - npm runner:
     `test_retry_3_npm_runner_uses_exact_lost_response_coordinate`;
   - cross-profile proof:
     `test_retry_3_proof_rejects_cross_profile_coordinate_and_tag`.
3. Add Governance `_retry_3_document` and tests:
   - exact rejected dispatch:
     `test_retry_3_profile_admits_exact_zero_sentinel_rejected_dispatch`;
   - profile closure:
     `test_retry_3_profile_rejects_cross_profile_substitution`;
   - coordinate/tag closure:
     `test_retry_3_profile_rejects_scenario_coordinate_or_tag_mismatch`;
   - historical admission/digests:
     `test_retry_3_profile_preserves_retry_1_and_retry_2_admission`.
4. Add the compact workflow contract:
   - literals: `test_retry_3_dispatch_and_profile_literals_are_exact`;
   - DAG/attempt/Environment:
     `test_retry_3_has_exact_five_job_first_attempt_dag`;
   - permissions: `test_retry_3_permissions_limit_packages_write_to_probe_jobs`;
   - pins/toolchain:
     `test_retry_3_toolchain_and_action_revisions_are_fully_pinned`;
   - terminal diagnostic/digest structure:
     `test_retry_3_terminal_capture_is_always_and_reconstructs_diagnostics`;
   - zero fail-before:
     `test_retry_3_zero_sentinel_fails_before_review_or_mutation`;
   - controlled terminal execution:
     `test_retry_3_terminal_script_emits_canonical_rejected_dispatch`;
   - ownership/no route:
     `test_retry_3_is_owned_and_contains_no_live_or_release_route`.
5. Add retirement/topology tests:
   `test_retry_3_is_the_only_temporary_acceptance_workflow_preserved` and
   `test_retry_3_temporary_acceptance_coexists_with_disabled_normal_buddy`.
6. Run the focused pytest selection, all five relevant files, targeted Ruff
   check/format, and root/targeted discovery. Record environmental blockers
   without weakening historical tests.

<!-- END APPEND: 2026-08-26-wdv3-acceptance-retry-3-fallback -->

<!-- BEGIN APPEND: 2026-08-27-wdv3-acceptance-upstream-diagnostic-characterization-plan -->

## Workflow Delivery v3 upstream-diagnostic tests-first plan

### Single implementation phase

Modify only the three allowlisted tests. Keep production unchanged and use
offline loopback/fake seams only.

1. **Proxy post-response facts (checklist 1).** Add
   `test_proxy_retains_status_and_request_digest_when_201_response_validation_fails`
   with ids `oversized-body`, `unsafe-response-header`, and
   `response-read-os-error`. Assert the exact status/request diagnostic,
   request digest equality, proof absence, processed-event absence, and no
   fabricated proof or processed result in request facts.
2. **Proxy transport closure/redaction (checklist 2).** Add
   `test_proxy_pre_response_transport_failure_retains_redacted_category_and_request_digest_without_status`
   with ids `timeout-error`, `os-error`, and `http-exception`. Assert one exact
   category, no status, exact request binding, exact keys, and absence of raw
   exception message, body, headers, token, stdout, and stderr from serialized
   retained state.
3. **Runner propagation and omission (checklist 3).** Add
   `test_runner_propagates_closed_upstream_diagnostic_for_returned_and_raised_failures`
   and
   `test_runner_omits_upstream_diagnostic_when_proxy_supplies_no_admitted_fact`,
   each with ids `returned-failure`, `raised-timeout`, `raised-os-error`, and
   `raised-classification-error`. Assert exact returned field or raised
   attribute and preserve concrete startedness; assert true omission when the
   proxy supplies no admitted fact.
4. **Adapter matrix and non-authority (checklist 4).** Add
   `test_acceptance_probe_preserves_non_authoritative_upstream_diagnostic_matrix_with_incomplete_readback`
   with ids `status-200`, `status-201`, `status-202`, `status-409`,
   `status-500`, `transport-timeout`, `transport-os-error`, and
   `transport-http-exception`. Assert the exact four-key mapped diagnostic
   plus unchanged action/mutation startedness, failure result, incomplete
   classification, exact post-readback/content reconciliation, concrete
   diagnostics, and proof absence.
5. **Authority controls (checklist 5).** Do not edit or replace the existing
   tests. Rerun
   `test_normal_create_propagates_request_bound_http_201_exchange_proof`,
   `test_exact_preexisting_state_never_invokes_the_mutation_runner`,
   `test_identical_conflict_race_is_exact_without_blind_repair`,
   `test_differing_conflict_race_is_conflicting_without_overwrite`, and
   `test_protocol_confirmed_governance_requires_validated_request_proof` as
   part of both narrow commands.
6. **Governance admission/closure (checklist 6).** Add
   `test_governance_admits_and_round_trips_canonical_upstream_diagnostic`
   with the eight Adapter matrix ids;
   `test_governance_rejects_malformed_or_unbound_upstream_diagnostic` with ids
   `status-below-range`, `status-above-range`, `status-bool`,
   `status-without-request`, `transport-without-request`,
   `status-and-transport`, `request-without-status-or-transport`,
   `unknown-transport-category`, `malformed-request-digest`, and
   `unknown-field`; and
   `test_governance_proof_required_completion_rejects_diagnostic_only_authority`
   with ids `protocol-confirmed-complete` and
   `protocol-confirmed-readback-incomplete`. Use unchecked digest refresh for
   the tests-first/malformed documents, exact admission round-trip assertions
   for canonical forms, and explicit proof-required rejection assertions.
7. **Request cardinality (checklist 7).** Add
   `test_acceptance_proxy_one_request_retains_at_most_one_request_bound_diagnostic`
   with ids `status-100`, `status-409`, `status-599`, and
   `pre-response-transport`, and
   `test_acceptance_proxy_two_request_race_never_exposes_a_singleton_upstream_diagnostic`.
   Bind the one-request diagnostic to the sole request digest; on the
   loopback-only two-request race assert two distinct request facts and no
   aggregate singleton diagnostic.
8. Run the exact offline collect-only command. Fix only collection, syntax,
   import, or fixture defects within the allowlist.
9. Run the exact offline narrow test command. Separate intended missing
   production-behavior failures from any harness or assertion-construction
   defects; fix only the latter.
10. Run `test-gap-analysis` and `assertion-quality` on the final bounded test
    additions, perform the prompt-scenario mapping review, inspect the diff,
    prove only the six allowlisted files changed, and append exact counts and
    classifications to `.testagent/status.md`.

<!-- END APPEND: 2026-08-27-wdv3-acceptance-upstream-diagnostic-characterization-plan -->

<!-- BEGIN APPEND: 2026-08-27-wdv3-acceptance-upstream-diagnostic-production-plan -->

## Workflow Delivery v3 upstream-diagnostic production repair plan

1. Correct the tests-first compatibility matrix before production changes:
   preserve requestless historical local exceptions, use `HTTPException` for
   the unbound transport negative, and add request-bound
   `RuntimeError`/`ValueError` rejection plus historical Governance replay.
2. In the proxy, retain the first request-bound status or transport fact,
   expose only a copy, and never aggregate one singleton diagnostic across a
   two-request race.
3. In the runner, propagate the optional admitted proxy diagnostic on returned
   failures and raised timeout, `OSError`, and classification failures. Wait
   only for the remaining absolute deadline when a request was observed but
   the handler has not yet reached its terminal diagnostic point.
4. In the Adapter, admit only closed raw request-bound diagnostics, preserve
   local historical fallback, bind protocol-confirmed diagnostics to proof,
   and reject raw/proof conflicts in the lost-response complete path.
5. In Governance, admit the closed compatibility union, reject empty and
   contradictory arms, preserve historical replay, and keep both completion
   and protocol-confirmed classification proof-gated.
6. Add regressions before repairing independently reviewed findings:
   delayed terminal publication, atomic first-fact retention, empty/unbound
   raw rejection, lost-response proof/raw conflict, empty Governance
   rejection, and proofless protocol-confirmed rejection.
7. Validate the three bounded files together, run Ruff format/check and
   Pyrefly on all six Python files, then run the complete v3 test suite.
8. Run the repository HK gate, stage only the nine bounded files, run the
   staged gate, and perform fresh independent split-scope review of:
   - CLI proxy/runner concurrency and propagation;
   - Adapter/Governance admission, replay, proof binding, and non-authority.
9. Independently adjudicate every new finding and iterate until both review
   scopes report no findings. Only then create the bounded commit and continue
   the authorized push/PR/CI flow.

The plan does not authorize Live activation, another destination acceptance
attempt, or reuse of consumed retry-3 coordinates `.9` through `.12`.

<!-- END APPEND: 2026-08-27-wdv3-acceptance-upstream-diagnostic-production-plan -->

<!-- BEGIN APPEND: 2026-08-27-wdv3-acceptance-proxy-cardinality-race-plan -->

## Tests-first plan for expected-one proxy cardinality

1. Append
   `test_acceptance_proxy_expected_one_rejects_simultaneous_identical_qualified_request_before_upstream`
   to the existing acceptance-probe test module.
2. Build one canonical publish body and synchronize two loopback client
   threads after strict body qualification.
3. Install a list-compatible test barrier at the current cardinality snapshot
   seam so both unsynchronized handlers deterministically observe zero
   retained facts. Give the barrier a bounded broken-barrier path so a future
   serialized check-and-append implementation can admit one handler and then
   reject the other.
4. Replace only the upstream HTTPS connection with an in-process fake
   returning HTTP 201 and recording forwarded bodies.
5. Assert one forwarded request, statuses `[201, 409]`, one request fact bound
   to the shared request digest, and an absent or single exact request-bound
   HTTP-201 diagnostic.
6. Run only:
   `PYTHONDONTWRITEBYTECODE=1 uv run --offline --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py::test_acceptance_proxy_expected_one_rejects_simultaneous_identical_qualified_request_before_upstream`.
7. Preserve the expected red result, append the exact evidence to
   `.testagent/status.md`, and make no production repair.

<!-- END APPEND: 2026-08-27-wdv3-acceptance-proxy-cardinality-race-plan -->

<!-- BEGIN APPEND: 2026-08-27-wdv3-acceptance-proxy-cardinality-race-fix-plan -->

## Production plan for expected-one proxy cardinality

1. Add a dedicated request-reservation lock to
   `AcceptanceMutationProxy`.
2. Preserve immutable expected-tarball membership validation outside the
   lock.
3. Compute the request and tarball digests once, then hold the lock only
   across matching reservation count and `request_facts.append`.
4. Reject a consumed matching reservation with local HTTP 409 only after
   releasing the lock.
5. Keep the two-request barrier, upstream HTTPS request, response handling,
   proof construction, and diagnostic publication outside the reservation
   lock.
6. Run the exact new regression, all acceptance-proxy tests, the three
   upstream-diagnostic modules, Ruff check/format, focused Pyrefly over the
   six bounded Python files, and the complete v3 package suite.
7. Run the unstaged repository HK gate, then perform fresh independent review
   and per-finding adjudication. Iterate tests-first if any true positive
   remains.
8. Stage only the five follow-up files, run the staged HK gate, commit, push,
   request Copilot rereview, resolve the addressed thread, and merge only
   after every required check and review thread is clear.

No step authorizes Live activation, a destination acceptance invocation, or
reuse of consumed acceptance coordinates.

<!-- END APPEND: 2026-08-27-wdv3-acceptance-proxy-cardinality-race-fix-plan -->

<!-- BEGIN APPEND: 2026-08-27-wdv3-acceptance-proxy-cardinality-test-refinement-plan -->

## Review-driven atomicity test refinement

1. Replace the cardinality-iteration barrier with an overridden
   `request_facts.append` barrier.
2. Keep the bounded broken-barrier fallback so the correct implementation
   does not deadlock while holding the reservation lock.
3. Re-run the exact regression, all acceptance-proxy tests, the three bounded
   modules, Ruff, focused Pyrefly, and the complete v3 suite.
4. Perform pseudo-mutation review for lock removal, count-only locking,
   `>=` boundary weakening, duplicate append/forwarding, wrong local status,
   and overbroad lock scope.
5. Repeat independent production and test/evidence review. Independently
   adjudicate and repair any new true positive before the final repository
   gates.

<!-- END APPEND: 2026-08-27-wdv3-acceptance-proxy-cardinality-test-refinement-plan -->

<!-- BEGIN APPEND: 2026-08-28-pr608-retry-4-terminal-fixed-identity-plan -->

## PR #608 retry-4 terminal fixed-identity plan

1. Add exactly
   `test_retry_4_terminal_program_preserves_fixed_identity_after_rejected_dispatch`
   to the existing retry-4 workflow contract module.
2. Extract `_terminal_python(document)`, run it with `sys.executable`, and
   write `WDV3_FILE` beneath `tmp_path`. Supply failed validation, skipped
   review/probe dependencies, empty optional outputs, wrong dispatch values,
   fixed retry-4 constants, first attempt, positive run ID, exact repository
   and ref, and a valid nonzero workflow SHA.
3. Admit the written bytes with
   `admit_governance_acceptance_evidence`; assert exact fixed identity,
   dependency results, incomplete classification, absent reviewer/artifacts/
   scenarios, and run attempt. The wrong target makes the test reject the
   `INPUT_TARGET_SHA`-after-failure mutant.
4. Run the exact new node, the whole retry-4 contract module, Ruff check, Ruff
   format check, and focused Pyrefly. Then run scoped `test-gap-analysis` and
   `assertion-quality`, re-run affected validation if strengthened, and audit
   the four-file allowlist and append-only notes.

<!-- END APPEND: 2026-08-28-pr608-retry-4-terminal-fixed-identity-plan -->

<!-- BEGIN APPEND: 2026-08-28-wdv3-acceptance-retry-4-finalization-plan -->

## Retry-4 protected-finalization plan

1. Change only target-pinned workflow and Governance contracts to require
   preparation merge
   `835b81be1ff0ba7aa0ec23c9a7b518d4ade3dfaa`.
2. Preserve executable rejection of zero and wrong dispatch inputs, exact
   rejected-dispatch terminal identity, complete real-registry Governance
   admission, historical profile replay, and cross-profile rejection.
3. Run the two focused contract modules and classify the intentional RED
   failures. They must arise only because the workflow default/environment
   and registered Governance target remain the preparation zero sentinel.
4. Implement only those three production target bindings, update current
   authoritative state documents, and run focused then complete validation.
5. Run repository gates and multi-reviewer OCR review with atomic independent
   TP/FP adjudication before commit, push, and the non-bypassed finalization
   PR.

<!-- END APPEND: 2026-08-28-wdv3-acceptance-retry-4-finalization-plan -->

<!-- BEGIN APPEND: 2026-08-28-wdv3-acceptance-retry-4-cleanup-plan -->

## Consumed retry-4 destination-acceptance cleanup plan

### Phase 1: Restore post-consumption absence contracts

1. Delete exactly
   `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_retry_4_workflow.py`;
   add no replacement workflow-only contract.
2. In `test_buddy_workflows.py`, restore
   `test_temporary_acceptance_workflows_are_absent_with_disabled_normal_buddy`
   so the exact `.yml`/`.yaml` temporary acceptance inventory is empty,
   including retry-4, while normal Buddy remains disabled and
   `live_enabled is False`.
3. In `test_commit11_legacy_buddy_retirement.py`, restore
   `test_temporary_acceptance_workflows_are_retired` so every matching
   temporary destination-acceptance workflow source is absent.
4. Preserve append-only `.testagent` history and append exact validation
   outcomes to status. Leave `docs/wiki/log.md`, workflow YAML, CODEOWNERS,
   production code, Governance profiles/evidence tests, authoritative wiki
   documents, `live_enabled`, and normal Live authority unchanged.
5. Run:

   `uv run pytest src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py::test_temporary_acceptance_workflows_are_absent_with_disabled_normal_buddy src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py::test_temporary_acceptance_workflows_are_retired`

   Because the parent-owned retry-4 YAML intentionally remains, focused
   failures are expected only when they identify that source. Do not skip or
   weaken the contracts and do not edit the workflow to make them pass. After
   the parent deletes it, the same nodes must pass. Record exact results and
   do not commit.

<!-- END APPEND: 2026-08-28-wdv3-acceptance-retry-4-cleanup-plan -->

<!-- BEGIN APPEND: 2026-08-28-wdv3-http-200-proof-repair-plan -->

## GitHub Packages npm HTTP 200 proof repair plan

### Phase 1: Add focused RED contracts

1. In `test_commit10_acceptance_probes.py`:
   - parameterize the exact proxy proof test over 200 and 201;
   - change the qualifying upstream matrix so 200 and 201 both produce proof
     and dropped-response processing;
   - retain 202/204 and other non-accepted statuses in the no-proof matrix;
   - replace the old 200 rejection contract with exact `{200, 201}`
     construction coverage and explicit 202/204 rejection;
   - assert 200/201 response identity differs and retains the actual status.
2. In `test_acceptance_exchange_proof_repair.py`:
   - parameterize normal runner propagation, normal protocol completion, and
     lost-response reconciliation over 200 and 201;
   - add closed-document round-trip coverage for both statuses;
   - keep unbound 200 diagnostics rejected;
   - add an exact-readback case proving a request-bound 200 diagnostic without
     proof remains incomplete.
3. In `test_commit10_acceptance_evidence.py`:
   - admit a request-bound 200 proof with a matching 200 diagnostic;
   - reject 200/201 status mismatch and unbound 200 adjacent to a 200 proof;
   - retain the existing historical unbound 201-with-proof and proof-free
     historical digest tests;
   - add a retry-4-shaped first-scenario regression proving HTTP 200
     diagnostic plus exact readback without proof remains incomplete and
     cannot create complete suite evidence.
4. Run only the changed nodes/modules. Confirm failures are limited to the
   production 201 gates and 201 normalization; do not weaken assertions or
   edit production during this phase.

Checklist mapping:

| Requirement | Planned evidence |
| --- | --- |
| H200-1 | proof accepted-status and rejected-status parameterized tests |
| H200-2 | proof round-trip and response-identity distinction tests |
| H200-3 | proxy exact-request 200/201 proof tests |
| H200-4 | proxy 202/204 diagnostic-only tests |
| H200-5 | normal and lost-response 200/201 Adapter completion tests |
| H200-6 | diagnostic-only exact-readback incomplete test |
| H200-7 | Governance matching and substitution tests |
| H200-8 | Governance unbound-200 rejection plus existing unbound-201 replay |
| H200-9 | retry-4-shaped no-proof non-retroactivity test |
| H200-10 | existing historical digest/profile suites |

### Phase 2: Implement the minimal production repair

1. In `adapters/github_packages.py`, define the closed authoritative publish
   status set `{200, 201}`. Use it for proof construction/admission,
   protocol-confirmed diagnostics, and normal/lost-response proof validation.
   Preserve the document status during rehydration.
2. In `cli.py`, form a proof only when the strictly validated upstream
   response status is in `{200, 201}`. Retain all existing size, header,
   request-cardinality, credential-redaction, and diagnostic behavior.
3. In `records/governance.py`, independently admit proof and
   protocol-confirmed diagnostic statuses `{200, 201}`. Add an explicit
   historical compatibility guard that permits an unbound status only when it
   is 201; require request binding for 200.
4. Make no schema, result-token, profile, workflow, coordinate, Live,
   authorization, or retry-history changes.

### Phase 3: Close documentation and test-agent state

1. Update the LLD to define the exact `{200, 201}` proof-status contract,
   actual-status retention, non-authoritative diagnostics, and 201-only
   historical unbound compatibility.
2. Update the v3 README, handoff, and overview with the bounded repair state
   while retaining retry-4 as unsuccessful and consumed.
3. Append a new `docs/wiki/log.md` entry; never rewrite prior HTTP 201 history.
4. Append RED/GREEN/review results to `.testagent/status.md`.

### Phase 4: Validate and review

1. Run focused Adapter, proxy, Governance, and CLI selections.
2. Run the complete Workflow Delivery v3 suite, Ruff check/format check,
   Pyrefly, documentation lint, and repository HK gates.
3. Run `test-gap-analysis` and `assertion-quality`; fix only findings inside
   the bounded repair contract.
4. Run multi-reviewer Open Code Review delegation, independently adjudicate
   every finding as TP/FP, apply scoped fixes, and repeat until no findings.
5. Commit and open a protected PR. Merge without bypass only after required
   checks pass, then perform fresh post-merge reconciliation and documentation
   closure before any retry-5 profile work.

<!-- END APPEND: 2026-08-28-wdv3-http-200-proof-repair-plan -->

<!-- BEGIN APPEND: 2026-08-28-wdv3-acceptance-retry-5-plan -->

## 2026-08-28 Workflow Delivery v3 retry-5 acceptance preparation plan

### Phase 1 - Governance profile and admission (current)

1. Preserve the current four-profile/historical baseline, then append tests in
   `tests/governance/test_commit10_acceptance_evidence.py` for exactly five
   ordered unique bases `.1`, `.5`, `.9`, `.13`, `.17` and exact retry-5
   workflow, Environment, confirmation digest, target, and ordered scenario
   bindings. Maps R1-R2.
2. Reuse current document builders to pin the exact forty-zero
   rejected-dispatch round-trip and reject validation/review/probe/fact/
   reviewer/artifact/mutation and identity substitutions. Maps R3.
3. Install only a clearly named hypothetical nonzero 40-hex retry-5 target
   through `monkeypatch`; prove complete 200 and 201 documents preserve exact
   bindings, canonical round-trip, and secondary observables, while 202 and
   204 reject. Maps R4.
4. Parameterize bidirectional retry-5/historical substitutions for every
   supported workflow, Environment, recovery Environment, digest, target,
   coordinate, tag, request, tarball, and response binding. Preserve the
   existing historical replay matrix. Maps R5.
5. Run the required narrow selection before source registration and record its
   expected missing-profile failure. Then add only the fifth
   `_GovernanceAcceptanceProfile` in
   `src/three_workflow_delivery_v3/records/governance.py`; do not change
   admission/status logic. Run the whole Governance file green. Maps R1-R5.
6. Run scoped Ruff check, Ruff format-check, and Pyrefly on the two Phase-1
   files, plus a read-only diff audit proving the CLI and live Governance JSON
   are untouched. Append every command/count/result to status. Maps R8.

### Phase 2 - Adapter registration and contracts (later)

Add the exact fifth Adapter profile and focused tests for ordered/unique
profiles, exact scenario resolution, runner and suite routing, proof identity,
and bidirectional cross-profile substitutions. Preserve all four historical
profiles and accepted statuses exactly `{200, 201}`. Maps R1-R2 and R5-R6.

### Phase 3 - workflow and topology (later)

Add only
`.github/workflows/workflow-delivery-v3-buddy-smoke-acceptance-retry-5.yml`
with exact retry-5 identity, forty-zero preparation target, five-job guarded
topology, scoped Environment/package permissions, and terminal evidence.
Update bounded workflow/topology and retirement contracts so retry-5 is the
sole temporary preparation workflow; keep normal Live disabled and introduce
no dispatch, ref, tag, package, or external mutation. Run narrow pytest plus
YAML/static validation. Maps R1 and R7.

### Required closure

After all three phases, run the relevant narrow/full bounded tests, Ruff,
Pyrefly, YAML/topology checks, and read-only scope checks. Invoke
`test-gap-analysis` and `assertion-quality`, remediate in-scope findings, and
record exact results. Maps R8. No phase may use the invalid 41-character SHA
or place a nonzero reviewed target in production.

<!-- END APPEND: 2026-08-28-wdv3-acceptance-retry-5-plan -->

<!-- BEGIN APPEND: 2026-08-29-wdv3-acceptance-retry-5-finalization-plan -->

## 2026-08-29 Workflow Delivery v3 retry-5 finalization test plan

### Phase 1 - Workflow contract finalization

Minimally adapt
`tests/contracts/test_commit10_acceptance_retry_5_workflow.py`:

1. Replace the synthetic finalized target expectation with immutable PR #616
   target `66154d0bb351a0c9c13d16292ce003d7eee65077`.
2. Require both workflow target literals to equal that SHA, intentionally
   exposing the two unchanged zero production bindings.
3. Reuse the fixed-input guard cases to accept exact target and reject zero,
   wrong, and hypothetical later-finalization targets before review/mutation.
4. Preserve and assert the fixed target identity emitted for rejected
   dispatches where the contract applies.
5. Remove the local Governance profile-finalization monkeypatch from complete
   terminal cases so HTTP 200/201 evidence reaches the real registry.
6. Keep all broad workflow topology, artifact, permission, and security
   coverage unchanged.

### Phase 2 - Governance finalization

Minimally adapt
`tests/governance/test_commit10_acceptance_evidence.py`:

1. Require the real retry-5 registry profile to bind the immutable target,
   intentionally exposing the unchanged zero profile binding.
2. Build finalized complete evidence with the exact target and delete the
   test-local registry replacement seam.
3. Reuse existing HTTP 200/201 round-trip tests through the real registry and
   existing 202/204 rejection tests.
4. Preserve the exact historical zero-sentinel rejected-dispatch admission.
5. Preserve historical profile replay and bidirectional cross-profile
   substitution rejection.
6. Add or refine a narrow case rejecting a hypothetical later finalization
   commit as the retry-5 target.

### Phase 3 - Focused RED validation and quality gate

1. Run only:
   `uv run --python 3.13 --package three-workflow-delivery-v3 python -m pytest src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_retry_5_workflow.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py`
2. Fix only test construction/assertion defects within the two test modules.
   Stop when every remaining failure is caused solely by one or more of:
   workflow dispatch default zero, workflow
   `WDV3_ACCEPTANCE_TARGET_SHA` zero, or Governance retry-5 `target_sha`
   zero. Do not skip/xfail and do not repair production.
3. Run `test-gap-analysis`, `assertion-quality`, and explicit prompt-scenario
   mapping against the final tests; strengthen only in-scope tests if needed.
4. Append the exact command, collection/pass/fail counts, failure identities
   and root causes, quality findings, and requirement mapping to
   `.testagent/status.md`.
5. Verify changed paths are limited to the five allowlisted files and that the
   pre-run byte prefixes of all three `.testagent` files remain unchanged.

### Requirement-to-evidence map

| Requirement | Planned evidence |
| --- | --- |
| Immutable PR #616 target; later commit invalid | Workflow identity/guard tests and Governance hypothetical-target rejection |
| Authoritative preflight; no repeated mutation | Read-only run record in status |
| Exactly three zero production bindings untouched | Focused RED failure classification and final diff audit |
| Both workflow literals and Governance target pinned | Existing identity/profile tests updated to exact SHA |
| Exact guard accepted; zero/wrong rejected pre-mutation | Existing fixed-input guard parameter matrix |
| Fixed rejected-dispatch identity | Existing terminal rejected-dispatch test |
| Real-registry HTTP 200/201 admission | Existing complete terminal and Governance round-trip tests with monkeypatch seam removed |
| HTTP 202/204 rejected | Existing proof-status parameterized rejection tests |
| Zero-sentinel rejected dispatch retained | Existing Governance preparation/rejected-dispatch round trip |
| Cross-profile and hypothetical substitutions rejected | Existing bidirectional profile matrix plus exact later-SHA case |
| Minimal reuse | Diff limited to existing preparation tests; no duplicate module |
| Quality and bounded RED closure | Exact command plus gap/assertion/scenario review in status |

<!-- END APPEND: 2026-08-29-wdv3-acceptance-retry-5-finalization-plan -->

<!-- BEGIN APPEND: 2026-08-29-wdv3-acceptance-retry-5-finalization-execution -->

## Retry-5 finalization plan execution

- [x] Establish the exact protected preparation merge and external-state
  preconditions before Environment creation.
- [x] Create and read back the single protected retry-5 Environment.
- [x] Produce the bounded tests-first RED against exactly three zero target
  bindings.
- [x] Bind those three production literals to
  `66154d0bb351a0c9c13d16292ce003d7eee65077`.
- [x] Preserve zero-sentinel rejected-dispatch admission, exact `{200, 201}`
  authority, cross-profile isolation, the existing DAG/permissions, and
  disabled normal Live.
- [x] Run focused and bounded GREEN validation plus test-gap,
  assertion-quality, and overdesign review.
- [ ] Complete full-suite and branch-range gates.
- [ ] Deliver and merge the protected finalization PR without bypass.
- [ ] Revalidate the exact finalization merge before the sole attempt-1
  dispatch.

<!-- END APPEND: 2026-08-29-wdv3-acceptance-retry-5-finalization-execution -->

<!-- BEGIN APPEND: 2026-08-29-wdv3-acceptance-retry-5-finalization-validation -->

## Retry-5 finalization validation progress

- [x] Complete v3 suite: **4,446 / 4,446** passed.
- [ ] Complete independent code/test and authority/document reviews.
- [ ] Complete staged and branch-range HK gates.
- [ ] Deliver and merge the protected finalization PR without bypass.
- [ ] Revalidate exact finalization merge and unused execution identities
  before the sole attempt-1 dispatch.

<!-- END APPEND: 2026-08-29-wdv3-acceptance-retry-5-finalization-validation -->

<!-- BEGIN APPEND: 2026-08-29-wdv3-acceptance-retry-5-cleanup-plan -->

## Workflow Delivery v3 retry-5 cleanup: single bounded phase

1. [x] Replace the Buddy retry-5 preparation/allowance pair with
   `test_temporary_acceptance_workflows_are_absent_with_disabled_normal_buddy`;
   require no matching `.yml`/`.yaml`, exact manual/reusable triggers, no
   schedule/push or `live_enabled: true`, no embedded retry-5 workflow name,
   and Governance `live_enabled is False`.
2. [x] Replace the retirement preparation exception with
   `test_temporary_acceptance_workflows_are_retired`; require both an empty
   inventory and `_legacy_buddy_routes(WORKFLOWS) == ()`. Add retry-5 itself
   to the direct parametrized absence matrix and remove the exception helper.
3. [x] Run both modified topology nodes together before source deletion.
   Result: expected RED, `2 failed in 0.57s`; both failures named only
   `.github/workflows/workflow-delivery-v3-buddy-smoke-acceptance-retry-5.yml`.
4. [x] Only after that RED, delete exactly the retry-5 workflow source and
   `tests/contracts/test_commit10_acceptance_retry_5_workflow.py`.
5. [x] Rerun the identical two-node command. Result: GREEN,
   `2 passed in 0.59s`.
6. [x] Perform bounded static mutation/assertion review and inspect final
   status/diff. Do not run a broad suite/build or mutate external resources.

Exact RED/GREEN command:

`uv run pytest src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py::test_temporary_acceptance_workflows_are_absent_with_disabled_normal_buddy src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py::test_temporary_acceptance_workflows_are_retired`

Scope is limited to the two contract edits, two authorized deletions, and
append-only research/plan/status entries. Production Adapter/Governance code,
historical retry-5 replay tests, authoritative docs/wiki files, Git refs, and
external resources remain unchanged.

<!-- END APPEND: 2026-08-29-wdv3-acceptance-retry-5-cleanup-plan -->
