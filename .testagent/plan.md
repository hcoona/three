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
