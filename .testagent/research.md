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

---

# Workflow Delivery v3 Commit 4 Research

This section appends commit-4 research without replacing retained history.

## Request and Strategy

Use one sequential Research -> Plan -> Implement pass for the bounded first
slice: project tests, canonical in-tarball Package Target Witness, and isolated
Node Build/Quality Adapters. The approved LLD sections "Node Build Adapter",
"Quality Adapters", and "Build and Artifact Scenarios" are normative. CI
planning, release identities, workflows, destination observation/publication,
and commit 5+ are excluded.

## Language and Existing Conventions

- Control implementation: Python 3.13, frozen slotted dataclasses, RFC 8785
  canonical JSON, pytest parameter matrices, real temporary-directory
  integration tests, and subprocess runner injection.
- First-slice project: Node 24 ESM. It had no test script or test file at the
  start of commit 4; repository-native `node:test` avoids a new dependency.
- `code-testing-extensions` was unavailable as stated by the user. Existing
  repository conventions are sufficient; no example was loaded.

## Bounded Target Inventory

- Existing project inputs: smoke-package `package.json`, `src/index.js`,
  `scripts/build.mjs`, and README.
- Existing control inputs: `canonical.py` and static Build/Quality definitions
  in `catalogs.py`.
- New production target: an isolated Node adapter module under
  `three_workflow_delivery_v3/adapters/`.
- New tests: one Node first-slice project test and one Python adapter
  scenario/integration module.

## Explicit Requirement Checklist

| ID | Requirement |
|---|---|
| C4-R1 | Add meaningful first-slice project tests and a package `test` script. |
| C4-R2 | Canonical witness binds exact target, Release Unit, canonical/native NBGV facts, Build Definition, catalog/control digests, purpose, and schema; excludes run/Attempt IDs. |
| C4-R3 | Build receives a frozen non-placeholder `npmPackageVersion`; never invokes NBGV or uses ambient manifest fallback. |
| C4-R4 | Build uses a fresh staging tree outside checkout and copies only declared inputs. |
| C4-R5 | Staged and packed manifests preserve intended `files` entries and add exactly `workflow-delivery/provenance.json`; missing/dropped/duplicate/extra entries fail. |
| C4-R6 | Invoke deterministic build directly and `npm pack --ignore-scripts`; validate tar basename, entries, identity, version, scripts, and witness bytes. |
| C4-R7 | Emit immutable tarball bytes plus exact entry/lifecycle manifest, SHA-256, SHA-512, and witness provenance. |
| C4-R8 | Artifact contents rejects altered, missing, misplaced, sidecar-only, noncanonical witness and undeclared tar entries. |
| C4-R9 | Install/import uses a clean consumer, disables scripts, imports `smokeMessage`, checks its concrete value, and verifies installed witness bytes. |
| C4-R10 | Project-build and project-test quality run without publication credentials; build is staged. |
| C4-R11 | Same target built twice yields identical tarball bytes. |
| C4-R12 | Source checkout remains byte-identical after success and representative failures; no restore/reset/clean behavior exists. |
| C4-R13 | Strict negative matrices cover traversal, malformed witness/version/allowlist, tar substitutions, and failed commands. |
| C4-R14 | Scope remains commit 4 only; preserve unrelated `specialized_processor.py` and all unrelated changes. |
| C4-R15 | Run narrow Node/Python tests, full package/root validation, and mandatory gap/assertion gate. |

## Exact Validation Commands

- `pnpm --dir src/public/lib/hcoona-release-smoke-npm test`
- `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
- `uv run --python 3.13 pytest -q`
- `uv run --python 3.13 pyrefly check`
- `uv build --package three-workflow-delivery-v3`
- `pnpm run build`
- `dotnet build dirs.proj --no-incremental`

## Commit-4 Independent Review Follow-up

The follow-up remained inside commit-4 adapter/test scope. Three review gaps
were added to the acceptance inventory:

| ID | Requirement |
|---|---|
| C4-R16 | Artifact qualification must parse canonical witness bytes as the exact Package Target Witness schema and reject arbitrary canonical JSON, wrong schema, and wrong bindings. |
| C4-R17 | Build and artifact qualification boundaries must pin the normative first-slice npm identity `@hcoona/hcoona-release-smoke-npm`. |
| C4-R18 | Source input validation must resolve declared inputs, verify regular files, and prove they remain inside the source checkout before reading bytes, copying, or invoking runners. |

Focused validation for this follow-up:

- `pnpm --dir src/public/lib/hcoona-release-smoke-npm test`
- `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- `uv run --python 3.13 ruff format --check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- `uv run --python 3.13 pyrefly check`

## Commit-4 Normative Hardening Follow-up

This append-only follow-up records the seven user-supplied findings verified
against the approved LLD's "Node Build Adapter", "Quality Adapters", "Build and
Artifact Scenarios", and dependency-ordered commit 4. Commit 5+ planning,
workflows, Evidence, and publication remain excluded.

| ID | Requirement |
|---|---|
| C4-R19 | Every target-controlled build, test, pack, install, and import command receives a minimal credential-free environment with an isolated `HOME`, npm user config, and cache rather than an ambient-environment copy with a short denylist. |
| C4-R20 | Lifecycle evidence binds every exact manifest script, including valid npm hooks such as `dependencies`, `preprepare`, and `postprepare`; qualification rejects any script change. |
| C4-R21 | Install/import pins the exact first-slice `smokeMessage` value inside the Adapter; callers cannot select the expected value, and the negative test mutates artifact export bytes. |
| C4-R22 | Tar qualification enforces exact member closure. Directory members are not ignored; because the first-slice allowlist declares only regular package files, every explicit directory member fails. |
| C4-R23 | The Build Request freezes and verifies PNPM in addition to Node and npm. Adapter identity/version is an internal first-slice constant and cannot be forged by a caller. |
| C4-R24 | Preservation tests snapshot every fixture-relevant project file outside dependency installations and cover injected build, pack, project-test, and install failures. |
| C4-R25 | Run the requested Node, narrow Adapter, full v3, Ruff, Biome, Pyrefly, and package-build validations; append exact results and the corrected full-v3 count while proving `specialized_processor.py` remains byte-identical. |

Normative interpretation notes:

- The LLD requires target execution without publication credentials and says
  the Adapter never mutates or restores the source checkout. A minimal
  allowlisted process environment and isolated npm state make that boundary
  fail closed instead of attempting to enumerate credential variable names.
- The LLD requires an exact lifecycle-script manifest. Binding all `scripts`
  entries is the closed representation and avoids an incomplete lifecycle-name
  allowlist.
- The LLD's exact tar entry allowlist contains regular package files only.
  Explicit directory headers are therefore undeclared members rather than
  ignorable metadata.
- The LLD explicitly freezes Node, PNPM, npm, and Adapter versions. The current
  repository resolves PNPM `11.17.0` for the first-slice workspace.

## Commit-4 Artifact Build/Pack Environment Review Follow-up

This independent-review follow-up remains within C4-R19 and the commit-4
Adapter test boundary. The approved LLD requires the Build Adapter to invoke
the deterministic build directly, run `npm pack --ignore-scripts`, isolate
staging outside the checkout, and freeze `SOURCE_DATE_EPOCH`, locale, and
timezone. Target-controlled execution must not inherit publication credentials.

The existing environment regression invoked project build, project test, and
install/import qualification, but it reused a prebuilt fixture and omitted
`build_node_package`. It therefore observed neither the artifact-build command
nor `npm pack`.

| ID | Requirement |
|---|---|
| C4-R26 | Invoke `build_node_package` while `_run` is observed; require both artifact build and `npm pack --ignore-scripts` to use the same isolated build home, exact minimal safe environment, frozen epoch/locale/timezone, isolated npm user config/cache, and no ambient secrets. |

<!-- BEGIN RUN: adjudicated-workflow-delivery-v3-commit4-focused-tests-2026-08-10 -->

---

# Adjudicated Workflow Delivery v3 Commit 4 Focused Test Research

This is an append-only research run. It does not replace the retained commit-3
or earlier commit-4 history above.

## Project Overview and Boundary

- **Workspace**: `/workspace/three-workspaces/design-workflows`
- **Languages/frameworks**: Python 3.13 with pytest; Node 24 ESM with
  `node:test`.
- **Requested boundary**: the current commit-4 Node Build/Quality Adapter and
  first-slice smoke-package test changes only.
- **Production/config targets**:
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py`
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py`
  - `src/public/lib/hcoona-release-smoke-npm/package.json`
- **Test targets**:
  - `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
  - `src/public/lib/hcoona-release-smoke-npm/test/index.test.js`
- **Read-only paired project source/fixture dependencies**:
  `src/index.js`, `README.md`, `scripts/build.mjs`, and `package.json` in the
  smoke package.
- **Explicit exclusion**:
  `src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`.
  Its unrelated user diff was not touched; its observed SHA-256 is
  `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`.
- **Later-scope exclusion**: Snapshot/request binding belongs to commits 5-6.
  Do not add CI Planner, Evidence/Finalizer, Release identity, two-snapshot
  planning, workflow, publication, or destination contracts in this run.

Git status identifies exactly the three untracked Adapter paths, the untracked
Node test, and the modified smoke `package.json` above as commit-4 code/test
work. `.testagent/{research,plan,status}.md` are retained artifacts. The
one-time pairing output supplied by the coordinator was consumed, not rerun; its
relevant deterministic record pairs `adapters/node.py` with
`tests/adapters/test_node.py`.

## Dependency Graph

- **Leaf**: smoke `src/index.js` (`smokeMessage`); byte/environment/tar helpers
  in `node.py` depend only on the standard library.
- **Mid-layer**: frozen records `PackageTargetWitness`, `BuildRequest`,
  `ArtifactExpectation`, `ArtifactManifest`, `BuildResult`, and
  `InstallImportResult`; witness handling depends on existing `NbgvFacts`,
  canonical JSON helpers, and first-slice descriptor constants.
- **Top-layer**: `build_node_package`, `run_node_project_build`,
  `run_node_project_tests`, `qualify_npm_artifact_contents`, and
  `qualify_npm_install_import`.
- **API layer**: `adapters/__init__.py` re-exports the public records and
  operations.

All targets are highly testable through temporary trees, in-memory tarballs,
and the existing injectable `_run` subprocess seam.

## Existing Source-to-Test Pairs and Conventions

| Source/config | Existing test | Classification |
|---|---|---|
| smoke `src/index.js` and `package.json` test script | smoke `test/index.test.js` | Substantial for the single public export: `node:test`, strict equality, and repeated-call stability. |
| `adapters/node.py` | `tests/adapters/test_node.py` | Partial for this adjudication: 58 previously recorded cases cover core success/negative scenarios, but the stream, single-read, quality-runtime, and complete-command requirements below remain uncovered. |
| `adapters/__init__.py` | none directly | Untested package-level export coherence; current pytest imports from `adapters.node`. |

Pytest conventions are fixtures, `tmp_path`, `monkeypatch`, frozen-dataclass
`replace`, parameter matrices, `pytest.raises`, injected
`subprocess.CompletedProcess`, concrete byte/hash/manifest assertions, and
source snapshots. Node uses `node:test` plus `node:assert/strict`.

## Adjudicated Acceptance Checklist and Evidence Inventory

| ID | Acceptance requirement | Current evidence/gap | Required focused evidence |
|---|---|---|---|
| C4-R27 | Safely resolve each declared source file, read it exactly once into immutable `bytes`, derive `source_input_manifest` from those same bytes, and write those same bytes to staging. | `_safe_input_sources` checks resolved regular files, but `_source_input_manifest` calls `read_bytes()` and `_copy_declared_inputs` later calls `shutil.copyfile`, so source content is read twice and can change between hashing and staging. | `test_build_reads_declared_inputs_once_and_reuses_immutable_bytes`: count one source read per declared path; mutate a temporary source after its captured read; assert manifest SHA-256 matches captured bytes and packed `dist/index.js`/staged behavior uses the captured bytes. Retain the outside-root symlink rejection. No Snapshot/request fields. |
| C4-R28 | Parse the complete npm tar byte stream; reject raw suffixes, multiple gzip members/archives, malformed or premature streams, undeclared entries, and non-regular members while retaining valid deterministic npm tarballs. | `_read_tarball` delegates to `tarfile.open(..., "r:gz")` without proving compressed-stream closure. A bounded probe showed current acceptance of raw trailing bytes, a second gzip member, a second tar archive, and a tarball missing its gzip trailer; malformed non-gzip bytes were rejected. Existing tests already cover exact valid tarballs, deterministic bytes, extra regular entries, and an explicit directory. | Add `test_artifact_contents_rejects_suffix_smuggling` (raw suffix and second gzip member), `test_artifact_contents_rejects_concatenated_tar_archive`, and `test_artifact_contents_rejects_malformed_or_premature_streams`. Assert `ValueError`, no suffix/second archive entry is ignored, and preserve `test_artifact_contents_accepts_exact_tarball`, strict undeclared-member cases, directory rejection, and byte-for-byte determinism/hash assertions. Hash and size must remain over the exact validated input bytes, not normalized/recompressed bytes. |
| C4-R29 | Use a credential-free minimal environment with `NPM_CONFIG_GLOBALCONFIG` pinned to an isolated empty config; use a minimal closed typed request freezing Node/npm for project-test and install/import; verify probes before quality subprocesses. | The environment has isolated user config/cache but no global-config pin. `run_node_project_tests(Path)` and `qualify_npm_install_import(tarball, expectation)` have no typed runtime input and perform no version probes. | Add frozen slotted exact-type `RuntimeRequest(node_version: str, npm_version: str)`. Pass it to project-test and install/import; reject empty/surrogate requests before operations; run and compare `node --version` and `npm --version` before `npm test`, `npm install`, or import. Extend environment assertions with an existing isolated empty global config. Do not add PNPM, Snapshot, Evidence, Planner, run, or Attempt fields to this minimal request. |
| C4-R30 | Observe the complete unfiltered Adapter subprocess sequence, including probes, and detect NBGV stamp/reset or source-restoration commands. | The existing environment test filters observations down to six target commands, so omitted/reordered probes can survive. Package lifecycle metadata legitimately contains NBGV stamp/reset scripts, but Adapter execution must never invoke them. | Add `test_subprocess_sequence_is_complete_and_forbids_nbgv_or_restoration_commands` (or remove the filter and strengthen the existing test). Assert the exact 16-command sequence below, operation-after-probe ordering, no command containing `nbgv-version.mjs`, `stamp`, or lifecycle `reset`, and no `git checkout`, `git restore`, `git reset`, or `git clean`. |
| C4-R31 | Keep `.testagent` research/plan/status evidence append-only and map acceptance to implementation/results. | Prior run sections are retained. This run appends only this delimited research section; plan/status were not edited. | A later implementation pass must append matching C4-R27 through C4-R32 sections to `plan.md` and `status.md`, including test names, exact command results, parser probes, command sequence, and unchanged unrelated-file evidence; never rewrite old results. |
| C4-R32 | Keep public exports, frozen types, function signatures, and tests coherent. | `adapters/__init__.py` exports all current public Adapter records/functions, but tests import the module directly and do not verify package exports. | Export `RuntimeRequest`; add `test_adapter_public_api_exports_closed_types_and_functions`. Assert package-level identity of exports, exact `RuntimeRequest` fields, frozen/slotted behavior, and updated project-test/install-import signatures. Assert no Snapshot/Evidence/Planner contract enters this API. |

## Exact Target Symbols

Production symbols requiring focused change/evidence:

- Source capture/staging: `_safe_input_sources`,
  `_safe_declared_input_sources`, `_source_input_manifest`,
  `_copy_declared_inputs`, `build_node_package`, and
  `run_node_project_build`.
- Complete stream validation: `_read_tarball` and
  `qualify_npm_artifact_contents`.
- Environment/runtime: `_credential_free_environment`, a new public
  `RuntimeRequest`, a Node/npm probe validator, `run_node_project_tests`, and
  `qualify_npm_install_import`.
- Command evidence seam: `_run`.
- Public API: `adapters/__init__.py::__all__` and imports.

Existing regression tests to retain include
`test_build_rejects_outside_root_symlink_before_read_copy_or_runner`,
`test_build_is_deterministic_and_preserves_source_checkout`,
`test_target_controlled_commands_use_minimal_isolated_environments`,
`test_artifact_contents_accepts_exact_tarball`,
`test_artifact_contents_rejects_strict_negative_matrix`,
`test_artifact_contents_rejects_explicit_directory_member`, and
`test_install_import_uses_tarball_and_verifies_export_and_witness`.

## Complete Expected Subprocess Sequence

After C4-R29, the unfiltered `_run` observations for one build, one project
build, one project-test, and one install/import qualification must be:

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
16. `node --input-type=module -e <fixed smokeMessage import script>`

The dynamic temporary paths and fixed import script are asserted separately;
no command is filtered out before sequence comparison. NBGV stamp/reset strings
remain allowed only as packed lifecycle evidence, never as executed commands.

## Exact Commands

- **Node scoped**:
  `pnpm --dir src/public/lib/hcoona-release-smoke-npm test`
- **Python scoped fix cycle**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'reads_declared_inputs_once or suffix_smuggling or concatenated_tar_archive or malformed_or_premature_streams or runtime_request_is_minimal_frozen_and_exported or quality_adapters_probe_frozen_runtime_before_operations or subprocess_sequence_is_complete_and_forbids_nbgv_or_restoration_commands or adapter_public_api_exports_closed_types_and_functions or target_controlled_commands_use_minimal_isolated_environments'`
- **Python full target file**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`
- **Harness-equivalent Python discovery from repository root**:
  `uv run --python 3.13 pytest --collect-only -q`
- **Harness-equivalent Node execution from repository root**:
  `pnpm test` (the retained status notes an unrelated existing
  `hexo-renderer-asciidoc` PNPM-version failure; use the scoped Node command
  during fix cycles).
- **Lint/type/package checks**:
  `uv run --python 3.13 ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`;
  `uv run --python 3.13 ruff format --check` with the same paths;
  `uv run --python 3.13 pyrefly check`;
  `uv build --package three-workflow-delivery-v3`.

## Recommendation Order

1. Capture validated source bytes once and reuse them for both digesting and
   staging.
2. Make gzip/tar stream closure fail closed, then add suffix/concatenation and
   premature-stream regressions.
3. Add/export the minimal `RuntimeRequest`, isolated empty global npm config,
   and pre-operation Node/npm probes.
4. Strengthen command evidence to the full unfiltered sequence and forbidden
   command scan.
5. Run the narrow commands, then append—not rewrite—plan/status evidence.

<!-- END RUN: adjudicated-workflow-delivery-v3-commit4-focused-tests-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-commit4-umask-padding-regressions-2026-08-10 -->

# Commit-4 Cross-Umask and Tar-Padding Regression Research

This append-only addendum is bounded to the two adjudicated blocking
Workflow Delivery v3 commit-4 regressions. Existing Adapter pytest conventions
in `tests/adapters/test_node.py` are authoritative: real isolated npm builds,
`tmp_path`/`monkeypatch`, exact byte/hash assertions, in-memory tar mutation,
and `pytest.raises`. No production source is an implementation target.

## Bounded inventory

- Test target:
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`.
- Read-only behavior target:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py`,
  specifically `build_node_package`, `_strict_gzip_payload`, `_read_tarball`,
  and `qualify_npm_artifact_contents`.
- Read-only smoke manifest:
  `src/public/lib/hcoona-release-smoke-npm/package.json`; its authoritative
  source version is exactly `0.0.0-placeholder`.
- Explicit exclusion:
  `src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`
  and every other production/test area.

## Requirement checklist and current gap

| ID | Requirement | Required evidence |
|---|---|---|
| C4-R33 | Build the same isolated npm package under process umasks `022` and `077`; restore the prior umask after each attempt and in an outer `finally`; require byte-identical tarballs, equal SHA-256/SHA-512 manifest hashes bound to exact bytes, normalized staged directory modes `0755`, normalized staged and packed regular-file modes `0644`, and no executable packed file. | `test_build_is_deterministic_across_process_umasks_and_normalizes_modes`; assert the source smoke version remains exactly `0.0.0-placeholder` before either build. |
| C4-R34 | Reject nonzero alignment padding immediately after an ordinary non-final tar member, independently of the all-zero final tar trailer. | `test_artifact_contents_rejects_nonzero_member_alignment_padding`; mutate one byte between `package/dist/index.js` data and the next header, prove ordinary contents still parse identically and the final two-block-or-larger trailer remains zero, then require `ValueError`. |
| C4-R35 | Preserve scope and append-only evidence. | Edit only the existing Adapter pytest plus appended `.testagent/{research,plan,status}.md` sections; do not edit production, smoke sources, or the excluded specialized processor. |

## Expected blockers

- The current staging writer relies on the ambient process umask and does not
  normalize staged directories/files before `npm pack`; a `077` build may
  therefore produce `0600` packed files and different compressed bytes/hashes.
- `_read_tarball` validates only content length and the zero final trailer
  after the maximum member data end. It does not inspect each ordinary
  member's alignment padding, so a nonzero byte before a later header may be
  accepted.

## Narrow validation commands

1. `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py -k 'deterministic_across_process_umasks or nonzero_member_alignment_padding'`
2. The nearest retained regressions for deterministic build, exact tar
   acceptance, explicit-directory rejection, and complete-stream rejection.
3. `uv run --python 3.13 ruff check` and `ruff format --check` on the target
   pytest file only.

<!-- END RUN: workflow-delivery-v3-commit4-umask-padding-regressions-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-commit4-production-fixes-research-2026-08-10 -->

# Commit-4 Production Fix Completion Research

- The exact npm-pack staging closure is the isolated `staging_root` passed as
  the `npm pack` working directory. `_normalize_staging_modes` now runs after
  `node scripts/build.mjs` and immediately before `npm pack`, sets only that
  root and its descendant directories to `0755`, and sets only descendant
  regular files to `0644`. It rejects symlinks and other filesystem node types
  rather than following or normalizing anything outside the closure.
- The mode fix does not read or change the process umask. The generated build
  output, rewritten staged manifest, and generated witness are all present
  before normalization.
- `_read_tarball` now validates the alignment region after every parsed regular
  member and rejects any nonzero padding byte before applying the existing
  final all-zero trailer rule.
- The smoke source manifest remains exactly `0.0.0-placeholder`.
- The excluded `specialized_processor.py` remained byte-for-byte equal to the
  recorded SHA-256
  `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`.

<!-- END RUN: workflow-delivery-v3-commit4-production-fixes-research-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-hidden-physical-tar-padding-regressions-research-2026-08-10 -->

# Hidden Physical Tar-Extension Padding Regression Research

## Strategy and bounded target inventory

This is a focused Research → Plan → Implement pytest pass. The current
uncommitted tree is authoritative. The only test-code target is the canonical
Adapter file:

- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`

Read-only behavior and preservation targets are:

- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py`,
  specifically `_strict_gzip_payload`, `_read_tarball`, and
  `qualify_npm_artifact_contents`;
- `src/public/lib/hcoona-release-smoke-npm/package.json`;
- `src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`.

The Adapter source and test trees were already untracked user work at the
baseline and are preserved as authoritative. The three `.testagent` files and
the smoke manifest were already modified. The protected processor had a
pre-existing one-line diff and baseline SHA-256
`91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`;
it is excluded from all edits.

## Existing pytest conventions and behavior gap

- Canonical Adapter regressions use the module-scoped real `built_result`,
  decompress and mutate tar bytes in memory, prove that Python `tarfile` still
  exposes the same logical regular-file closure, and assert the concrete
  `ValueError("invalid npm tarball")` diagnostic from
  `qualify_npm_artifact_contents`.
- The retained ordinary-member regression mutates logical-member alignment
  padding using offsets returned by `archive.getmembers()`.
- `_read_tarball` likewise iterates only `archive.getmembers()`. Python hides
  GNU long-name (`L`), per-file PAX extended (`x`), and PAX global (`g`)
  physical records while returning their following logical members. Therefore
  the current logical-member padding loop does not inspect the extension
  record's own data-to-512-byte alignment region.
- Repository/Python `tarfile` support was directly probed without examples.
  All three feasible named physical variants create a 1,024-byte prefix with
  non-empty zero alignment padding: GNU long-name `L` (22-byte record), local
  PAX `x` (30-byte record), and global PAX `g` (44-byte record).
- `code-testing-extensions` was attempted before research and was unavailable,
  so the existing pytest file is the sole style authority.

## Acceptance checklist

| ID | Requirement | Required evidence |
|---|---|---|
| C4-R36 | Exercise GNU long-name physical extension padding smuggling. | A focused `gnu-long-name` parameter case inserts a real `L` record before an existing expected member and mutates only its alignment padding. |
| C4-R37 | Exercise per-file PAX physical extension padding smuggling. | A focused `pax-extended` parameter case inserts a real `x` path record that resolves to the same expected logical name. |
| C4-R38 | Exercise every other feasible named PAX variant. | A focused `pax-global` parameter case inserts a real `g` record with harmless global metadata; together `x` and `g` cover both supported PAX physical record types. |
| C4-R39 | Strict mode rejects nonzero physical-record padding hidden by `getmembers()`. | Each case proves the physical type is absent from the logical members and requires `ValueError("invalid npm tarball")`. |
| C4-R40 | The regression is concrete and non-vacuous. | Assert exact typeflag, record size/padding bounds, original zero padding, one-byte `0xA5` mutation, unchanged logical names/contents, zero final trailer, and exact failure diagnostic. |
| C4-R41 | Follow canonical Adapter pytest conventions. | Append one parameterized regression to `tests/adapters/test_node.py`, reusing `built_result`, in-memory gzip/tar mutation, `_tar_entries`, and `pytest.raises`. |
| C4-R42 | Preserve the protected processor byte-for-byte. | Post-run SHA-256 must remain `91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`. |
| C4-R43 | Keep the smoke version exact. | Post-run JSON version remains `0.0.0-placeholder`; do not edit the manifest. |
| C4-R44 | Preserve user work, avoid production edits/commit, append evidence only, and run only the focused new tests. | Changed-file and command evidence in status; no VCS mutation command and no broad pytest selection. |

## Exact focused validation command

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_nonzero_hidden_physical_extension_padding`

If current production accepts any malformed archive, retain the ordinary
failing regression without skip/xfail and record each parameter result. No
production fix is permitted in this pass.

<!-- END RUN: workflow-delivery-v3-hidden-physical-tar-padding-regressions-research-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-hidden-physical-tar-padding-refinement-research-2026-08-10 -->

# Hidden Physical Tar-Extension Fixture Refinement

The first focused execution confirmed an important PAX parser boundary.
Python's PAX reader treats the first zero padding byte as the end-of-record
sentinel; changing that byte makes `tarfile` itself raise `ReadError` and does
not exercise the adapter gap. Keeping that first byte zero while changing the
second padding byte, or changing the final padding byte, remains physically
noncanonical but is hidden by `getmembers()` for both local `x` and global `g`
records. GNU long-name `L` records hide nonzero padding at both the first and
final padding positions.

The final regression therefore checks two padding boundaries per physical
variant: the first byte ignored by the corresponding parser and the final byte
before the following 512-byte block. This both preserves a valid smuggling
fixture and detects partial or off-by-one physical-padding scans.

<!-- END RUN: workflow-delivery-v3-hidden-physical-tar-padding-refinement-research-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-physical-tar-padding-fix-research-2026-08-10 -->

# Physical Tar Padding Fix Research

The logical `tarfile.getmembers()` list cannot be the authority for strict
physical-stream padding because it hides GNU long-name and PAX extension
records. The production fix therefore walks the uncompressed payload one
physical 512-byte header at a time, parses each header independently, validates
the exact data-to-block alignment range, and requires an all-zero trailer of at
least two blocks before the existing logical allowlist/content pass runs.

Independent review identified that PAX `size` metadata creates an alternate
physical traversal interpretation. Both shrinking and expanding overrides can
make one parser treat extension-shaped blocks as member data while another
parser treats them as headers. The strict accepted npm subset therefore rejects
PAX `size` and GNU sparse metadata/types rather than permitting ambiguous
physical layouts. Harmless GNU long-name, local PAX path, and global PAX
metadata records remain accepted when their physical padding is zero.

The retained logical-member padding check remains as defense in depth and
preserves the pre-existing regular-file content validation. Focused regressions
cover:

- hidden GNU `L`, local PAX `x`, and global PAX `g` padding at both the first
  parser-ignored and final alignment byte;
- acceptance of each corresponding valid zero-padded extension archive;
- rejection of both shrinking and expanding PAX-size traversal ambiguity;
- unchanged logical allowlist/content observations for the smuggled archives.

The protected specialized processor baseline SHA-256 is
`91e2be3d2f647bd279ebc3f6c65a25f4223d9ab3235282ec96a2afeb39841429`.
The smoke manifest remains `0.0.0-placeholder`.

<!-- END RUN: workflow-delivery-v3-physical-tar-padding-fix-research-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-pax-physical-closure-regressions-research-2026-08-10 -->

# Adjudicated PAX Physical-Closure Regression Research

## Strategy and bounded inventory

Direct strategy is appropriate: the request adds two focused pytest cases for
one validator behavior in the existing Node Adapter test file. Production is a
read-only target because the parent agent owns the validator fix.

- Read-only production target:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py`,
  specifically `_validate_pax_payload` as called by
  `_validate_physical_tar_stream`.
- Test target:
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`.
- Evidence targets: `.testagent/research.md`, `.testagent/plan.md`, and
  `.testagent/status.md`, preserving all prior sections.
- Explicitly excluded:
  `src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`,
  package/version manifests, and all production files.

The unavailable `code-testing-extensions` reference was not retried. Existing
Python 3.13 pytest conventions in `test_node.py` are the language/style
authority: `built_result`, in-memory gzip/tar mutation, bare concrete
assertions, parameter IDs, and anchored `pytest.raises`.

## Defect and fixture boundary

`_validate_pax_payload` stops when `content[position] == 0`, so it can accept a
valid length-prefixed PAX record followed, inside the physical header's
declared payload size, by `NUL` and a nonzero byte. Python's logical tar reader
still hides the local/global PAX physical record and preserves the original
regular-file entry closure. The regression must therefore prove all three
physical facts independently:

1. the header size includes the two-byte `NUL`/nonzero suffix;
2. the suffix lies inside that declared payload; and
3. the subsequent TAR block-alignment padding remains entirely zero.

## Acceptance checklist

| ID | Requirement | Evidence target |
|---|---|---|
| C4-R45 | Reject a local PAX payload containing `NUL` plus a nonzero declared suffix while its logical entries remain preserved. | `test_artifact_contents_rejects_nonzero_suffix_inside_declared_pax_payload[pax-local]` |
| C4-R46 | Reject the equivalent global PAX payload. | `test_artifact_contents_rejects_nonzero_suffix_inside_declared_pax_payload[pax-global]` |
| C4-R47 | Keep declared-payload validation separate from TAR alignment-padding validation. | The test asserts `NUL`/`0xA5` before `payload_end` and separately asserts all bytes in `[payload_end, padding_end)` are zero. |
| C4-R48 | Use existing Adapter pytest conventions. | Existing `built_result`, `_physical_extension_prefix`, `_tar_header_with_size`, `_tar_entries`, gzip/tar parsing, and anchored `pytest.raises` patterns. |
| C4-R49 | Run the narrowest relevant pytest command. | Exact parameterized test-node command below. |
| C4-R50 | Append/update all three `.testagent` artifacts without removing prior evidence. | This delimited section and matching plan/status sections. |
| C4-R51 | Preserve unrelated `specialized_processor.py` exactly. | Before/final SHA-256 comparison in status. |
| C4-R52 | Do not commit. | No commit command is part of the plan or execution. |
| C4-R53 | Do not change package/version placeholders. | Smoke package version verification in status. |
| C4-R54 | Edit only tests/evidence and retain strict assertions if the parent production fix is pending. | Changed-scope/status evidence and expected `DID NOT RAISE` result. |
| C4-R55 | Treat the current workspace as authoritative and never restore missing files. | No restore/reset/checkout/clean command is used. |

## Narrow validation command

`uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py::test_artifact_contents_rejects_nonzero_suffix_inside_declared_pax_payload`

<!-- END RUN: workflow-delivery-v3-pax-physical-closure-regressions-research-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-robust-first-slice-tar-profile-research-2026-08-10 -->

# Robust First-Slice TAR Physical Profile Research

## Strategy and bounded inventory

**Direct strategy.** This adjudication changes only the canonical focused
pytest file and append-only test-agent evidence. Production is read-only.

- Canonical test target:
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`.
- Read-only behavior target:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py`,
  specifically `_strict_gzip_payload`, `_validate_physical_tar_stream`,
  `_read_tarball`, and `qualify_npm_artifact_contents`.
- Behavioral fixture: the real `built_result` fixture, which runs the frozen
  Node/npm build and `npm pack --ignore-scripts`.
- Explicit exclusion:
  `src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`
  and every production/configuration file.

Existing Python 3.13 pytest conventions in `test_node.py` are authoritative.
The parent already attempted `code-testing-extensions`; it is unavailable and
was not retried.

## Actual frozen npm-pack physical evidence

The real frozen build produced a 983-byte gzip member with SHA-256
`0e615dbe7cf23a5192d9565518ff741784a0092df23d3433bee9b4eb52c818dd`.
Its uncompressed TAR contains four physical headers, in order:

1. `package/dist/index.js`
2. `package/package.json`
3. `package/workflow-delivery/provenance.json`
4. `package/README.md`

Every header is an ordinary regular-file type `b"0"` with exact
`magic=b"ustar\0"` and `version=b"00"`. Names are NUL-filled to 100 bytes.
`linkname`, `uname`, `gname`, `prefix`, and bytes 500-511 are all NUL.
`uid` and `gid` are all NUL. Device fields are exactly `b"000000 \0"`.
Mode is exactly `b"000644 \0"`, size and mtime use ten octal digits followed
by space/NUL, checksum uses six octal digits followed by space/NUL, and the
archive ends with exactly two zero blocks.

Read-only probes confirmed that the current validator accepts semantically
equivalent headers with GNU/v7 magic, wrong USTAR version, nonzero bytes after
the first NUL in fixed strings, alternate octal terminators/widths, closed
field substitutions, and valid GNU/PAX extension records. These are the
adjudicated gaps.

## Explicit requirement checklist

| ID | Requirement | Concrete evidence target |
|---|---|---|
| TAR-R1 | Qualification accepts only ordinary regular-file USTAR headers emitted by the frozen npm-pack artifact. | `test_artifact_contents_accepts_actual_frozen_npm_pack_ustar_profile` plus all negative profile tests. |
| TAR-R2 | Reject GNU `L`/`K`, PAX `x`/`g`/`X`, hard/symbolic links, directories, sparse records, and every other known special/nonordinary type. | Dedicated GNU/PAX tests plus `test_artifact_contents_rejects_every_nonordinary_tar_type`. |
| TAR-R3 | Validate each raw 512-byte header with exact supported USTAR magic/version. | `test_artifact_contents_rejects_noncanonical_ustar_magic_or_version`. |
| TAR-R4 | Require canonical NUL filling in every relevant fixed string field. | Positive frozen-profile test and `test_artifact_contents_rejects_nonzero_suffix_after_nul_in_fixed_string_field`. |
| TAR-R5 | Close unused `linkname`, `uname`, `gname`, `prefix`, reserved, UID/GID, and device fields exactly as emitted by npm. | Positive frozen-profile assertions, fixed-string suffix matrix, and `test_artifact_contents_rejects_noncanonical_unused_header_field`. |
| TAR-R6 | Require npm-compatible canonical octal/checksum encodings and reject parser-elided/alternate equal-value bytes. | `test_artifact_contents_rejects_noncanonical_numeric_header_encoding`. |
| TAR-R7 | Preserve strict gzip/full-consumption, member padding, trailer, allowlist, manifest, witness, and hash coverage. | Retained exact-tarball, suffix-smuggling, concatenated archive, malformed/premature stream, member-padding, strict matrix, witness, and deterministic hash tests. |
| TAR-R8 | Replace/invert artificial GNU/PAX acceptance. | New dedicated GNU/PAX tests require valid zero-padded extension archives to fail; no extension archive is positively qualified. |
| TAR-R9 | Add focused GNU long-name/long-link, all PAX physical types, and nonzero suffix-after-NUL cases for every relevant fixed string field. | Exact parameter IDs in the GNU, PAX, and five-field suffix matrices. |
| TAR-R10 | Remove dead extension-validator tests without deleting/overwriting the canonical file. | Remove only the three obsolete physical-extension padding/PAX payload/PAX-size test functions and their now-unused helpers from `test_node.py`. |
| TAR-R11 | Do not modify `specialized_processor.py` or any production file. | Final changed-file/status evidence. |
| TAR-R12 | Run the narrow relevant tests and report exact changes and blockers. | Exact focused pytest, full target-file pytest, Ruff, package build, preservation, and status results. |

## Existing conventions and retained regression closure

- Real artifact fixture: module-scoped `built_result`.
- Physical mutation: decompress, edit a raw header, recompute a valid TAR
  checksum, recompress with `mtime=0`, prove logical entries are unchanged,
  then assert anchored qualification rejection.
- Extension fixtures: insert well-formed GNU/PAX physical records that Python
  hides while retaining the exact original logical file closure.
- Existing strict gzip, complete-stream, member-alignment, final-trailer,
  logical allowlist, package-manifest, witness, hash, and deterministic-byte
  tests remain untouched.

<!-- END RUN: workflow-delivery-v3-robust-first-slice-tar-profile-research-2026-08-10 -->

<!-- BEGIN RUN: workflow-delivery-v3-robust-tar-gate-remediation-research-2026-08-10 -->

## Gate-remediation research: mandatory pre-completion review gaps

The working tree remains authoritative. This remediation phase is test-only
and limited to:

- `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_node.py`;
- append-only additions to `.testagent/research.md`,
  `.testagent/plan.md`, and `.testagent/status.md`.

No production/config/package path is in scope, including
`src/private/app/html-sm-processor/src/html_sm_processor/specialized_processor.py`
and
`src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/node.py`.

### Frozen physical artifact inventory

The actual frozen npm pack artifact is still pinned by
`test_artifact_contents_accepts_actual_frozen_npm_pack_ustar_profile`:

| Index | Physical member |
|---:|---|
| 0 | `package/dist/index.js` |
| 1 | `package/package.json` |
| 2 | `package/workflow-delivery/provenance.json` |
| 3 | `package/README.md` |

The new later-member tests operate on indexes 1, 2, and 3 so a validator that
checks only physical member 0 cannot satisfy the negative matrix. The helper
`_tarball_with_member_header_fields` edits a selected raw 512-byte header,
recomputes that member checksum, and recompresses with `mtime=0`. The helper
`_tar_member_observables` records concrete parser identity and structural
observables before qualification:

- member index and name;
- parsed mode, uid, gid, size, mtime, type, linkname, uname, gname, devmajor,
  and devminor;
- raw header/data offsets.

Every added negative test asserts those observables and logical extracted
entries remain unchanged before requiring `qualify_npm_artifact_contents` to
reject at the same raw qualification boundary.

### Review gaps mapped to focused evidence

| Gap | Added research finding | Concrete test target |
|---|---|---|
| Later-member raw profile | Existing mutations were first-header only. Representative equal-value mode terminator and USTAR magic mutations are meaningful on all three later frozen members. | `test_artifact_contents_rejects_later_member_ustar_profile_mutations`: 3 member indexes x 2 profile mutations = 6 cases. |
| Fixed-string hidden suffix | Existing suffix cases only wrote the final suffix byte after the first NUL. A validator that probes one suffix byte could survive. | `test_artifact_contents_rejects_nonzero_suffix_after_nul_in_fixed_string_field`: 5 fields x 2 positions = 10 cases, covering immediate-after-NUL and final suffix bytes for `name`, `linkname`, `uname`, `gname`, and `prefix`. |
| Canonical numeric encodings | The existing 19-case matrix missed equal-value canonical-length `NUL+space`/space terminators for several fields, UID/GID equal-value forms, immediate hidden bytes in zero numeric fields, and checksum space termination. | `test_artifact_contents_rejects_noncanonical_numeric_header_encoding`: 34 cases total, adding 15 meaningful cases without changing the positive frozen npm output assertion. |

### Inline pre-completion review

`test-analysis-extensions` is unavailable in this workspace, so the Python
pytest review was performed inline after invoking `test-gap-analysis` and
`assertion-quality`.

Pseudo-mutation review result:

- The first-member-only physical-header mutation survived before this
  remediation; the 6 later-member cases now kill it.
- A fixed-string validator that checks only one suffix byte survived before
  this remediation; the 10-case two-position matrix now kills it.
- Numeric validators that accept equal-value alternate terminators or only scan
  a final hidden byte survived before this remediation; the 34-case matrix now
  targets the missing forms.

Assertion-quality review result:

- The added tests are not assertion-free or trivial-only.
- They combine exact byte equality/inequality, structural parser observables,
  logical-entry equivalence, concrete member identity/index checks, and
  negative exception assertions.
- No further test-only gap was found within the allowed edit scope.

<!-- END RUN: workflow-delivery-v3-robust-tar-gate-remediation-research-2026-08-10 -->
<!-- BEGIN APPEND: workflow-delivery-v3-robust-tar-final-gate-research-2026-08-10 -->

## Final gate follow-up: fixed-string interior suffix

The independent pseudo-mutation review found that immediate-after-NUL and
final-byte mutations alone did not pin down an interior suffix byte. The
bounded target remains the canonical Adapter TAR test. Each relevant fixed
string field (`name`, `linkname`, `uname`, `gname`, `prefix`) now also mutates
one middle byte after the first NUL while preserving parser-visible member
observables and logical entries.

<!-- END APPEND: workflow-delivery-v3-robust-tar-final-gate-research-2026-08-10 -->

<!-- BEGIN APPEND: workflow-delivery-v3-robust-tar-adjudication-closure-research-2026-08-10 -->

## Final adjudication closure research

The final pseudo-mutation pass identified two narrower ways an incomplete raw
validator could survive the earlier matrices:

1. validate only a subset of profile categories after physical member zero;
2. require a NUL terminator in unused string fields without requiring the
   observed npm value itself to remain empty, or inspect only one reserved-byte
   position.

The bounded test file now closes both gaps. Every later frozen npm member is
mutated across mode, magic, version, name padding, linkname padding, reserved
bytes, old-regular type, and checksum termination. The unused-field matrix now
also rejects nonempty `linkname`, `uname`, `gname`, and `prefix` values and
nonzero first, middle, and final reserved bytes. The existing 15-case
immediate/middle/final suffix matrix remains the literal coverage for all five
relevant fixed string fields.

No additional production or test target entered scope. The final focused
inventory remains the one canonical Adapter pytest file plus append-only
`.testagent` evidence.

<!-- END APPEND: workflow-delivery-v3-robust-tar-adjudication-closure-research-2026-08-10 -->

<!-- BEGIN APPEND: workflow-delivery-v3-robust-tar-production-implementation-research-2026-08-10 -->

## Robust TAR production implementation research

The implementation scope adds the previously blocked production validator in
`adapters/node.py` and keeps the adjudicated tests as the executable oracle.
The validator can reject the unsupported physical profile without interpreting
extension records: every nonzero 512-byte header must itself be the frozen npm
ordinary-file USTAR form before `tarfile` receives the payload.

The supported raw header profile is:

- exact `magic=b"ustar\0"`, `version=b"00"`, and regular type `b"0"`;
- a nonempty name followed by an all-NUL suffix;
- exact mode `b"000644 \0"`;
- all-NUL UID/GID, linkname, uname, gname, and prefix fields;
- exact zero device encodings `b"000000 \0"` and an all-NUL reserved field;
- ten octal digits plus space/NUL for size and mtime;
- six octal digits plus space/NUL for checksum, bound to the raw header sum.

Physical traversal uses the raw canonical size, validates each member's zero
alignment padding, and retains the existing all-zero final trailer closure.
Only after the entire physical stream passes does semantic `tarfile` parsing
enforce the logical regular-file closure, allowlist, manifest, witness, and
content reads.

The old PAX payload parser and its constants are dead under this closed
profile and are removed. Test-only synthetic regular archives are normalized
to the same npm-compatible header form so retained manifest/witness/build
scenarios continue testing their intended semantic boundary. The explicit
directory scenario now expects raw-profile rejection.

Pseudo-mutation review found the focused matrices kill removal or weakening of
the type, magic/version, later-member, fixed-string suffix, unused-field,
octal terminator/width, checksum terminator, and base-256 guards. Raw checksum
arithmetic is also checked before semantic parsing; removing only that check
would be externally masked by `tarfile` checksum validation but would violate
the required validation order. `test-analysis-extensions` was requested and
is unavailable, so Python/pytest conventions were applied directly.

<!-- END APPEND: workflow-delivery-v3-robust-tar-production-implementation-research-2026-08-10 -->

<!-- BEGIN APPEND: workflow-delivery-v3-robust-tar-checksum-order-review-2026-08-10 -->

## Raw checksum-order review closure

Independent adversarial review identified that canonical-but-incorrect
checksum bytes were rejected by production before semantic parsing but were not
directly pinned by a test. The focused suite now mutates only the stored
checksum value while retaining the six-octal-digit space/NUL encoding, replaces
`tarfile.open` with a fail-fast sentinel, and proves raw validation rejects
without invoking semantic TAR parsing.

<!-- END APPEND: workflow-delivery-v3-robust-tar-checksum-order-review-2026-08-10 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit5-ci-research-2026-08-12 -->

## Workflow Delivery v3 Commit 5 Research

The bounded target is the approved first-slice CI dependency boundary:
candidate formation, Repository Model-backed planning, static lane Evidence,
required non-authoritative finalization, the shadow/manual workflow, and the
permanent smoke-package consumer-policy gate. Release identities, simulation,
live publication, CODEOWNERS, acceptance, and activation remain excluded.

Acceptance requires exact current-candidate bindings, blocked Plans for
incomplete models or unavailable comparisons, root-HK-only empty affected
scope, complete four-lane first-slice scope, lane-specific Evidence, missing
work as incomplete, deterministic summaries, ordinary-PR SLO facts, exact
workflow permissions/topology, and digest-bound consumer exceptions.

<!-- END APPEND: workflow-delivery-v3-commit5-ci-research-2026-08-12 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit6-release-core-research -->

## Workflow Delivery v3 Commit 6 Release Core Research

### Bounded inventory

- Production:
  `records/artifacts.py`, `records/release.py`, `repository/compiler.py`,
  `release/identity.py`, `release/planner.py`, `release/qualification.py`,
  `release/finalizer.py`, and their package exports.
- Tests/fixtures:
  `tests/release/test_commit6_contracts.py`,
  `tests/release/test_commit6_qualification.py`, shared release fixtures, and
  canonical Intent/Repository Model fixture bytes and digests.
- Existing collaborators:
  RFC 8785 canonical parsing/digests, first-slice Repository Model and Release
  policy records, Node build/project-test/tarball/install adapters, and static
  Build/Quality/Destination definitions.

### Acceptance checklist

1. Frozen/slotted Release, simulation, artifact, Evidence, Decision,
   observation, Publication Snapshot, and Simulation Outcome contracts.
2. Exact canonical Repository Model transport deserialization and current
   simulation-purpose admission; forged admission wrappers fail closed.
3. Official workflow-dispatch simulation Intent and post-admission Simulation
   Identity/Binding with no live Attempt derivation.
4. One npm build/variant/output and npmjs projection; four required obligations
   with a closed DAG and both tarball checks depending on build.
5. Adapter-wrapped qualification with current-attempt artifact transport,
   SHA-256/SHA-512, manifest, witness, source-input, toolchain, and provenance
   binding.
6. Complete success plus failure-continuation, missing, duplicate,
   cross-purpose, prior-attempt, and substituted Evidence behavior.
7. Guarded Publication Snapshot, synthetic absent/exact observation planning,
   and truthful unsupported-observation simulation finalization.
8. No workflow, remote npm interpretation, live authority, Receipt,
   authorization, mutation, HK, catalog, docs, or consumer-policy changes.

### Test conventions

The package uses pytest, exact dataclass construction, `replace` for adversarial
bindings, monkeypatched adapter boundaries, canonical byte fixtures, and
scenario fixtures. The focused commit-6 suite maps the checklist to concrete
contract and qualification scenarios rather than network-dependent tests.

<!-- END APPEND: workflow-delivery-v3-commit6-release-core-research -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit6-core-correction-research -->

## Workflow Delivery v3 Commit 6 Core Correction Research

### Bounded target inventory

- `records/release.py`: complete Request/Intent binding, Qualification Snapshot
  repository binding, Release Artifact context.
- `release/identity.py`: rerun-stable Request ID and run-attempt-bound
  Simulation derivation.
- `repository/compiler.py`: immutable compiled Release policy in every ready
  Repository Model Snapshot, strict JSON admission, and incomplete-model
  behavior.
- `release/planner.py`: admitted-Snapshot-only policy consumption.
- `release/qualification.py`: mechanical build execution separated from
  post-upload artifact/Evidence formation.
- Direct consumers in `ci/planner.py`, package exports, and focused
  repository/CI/release tests and fixtures.

The static pairing analyzer scanned the package once: 25 of 26 Python sources
were paired. The correction targets are already paired with
`tests/repository/test_compiler.py`, `tests/ci/test_planner.py`,
`tests/ci/test_scenarios.py`, and the two commit-6 release test files. This is a
static symbol-reference heuristic, not line or branch coverage evidence.

### Acceptance checklist

1. Intent includes workflow ref and run attempt; request ID stays stable across
   rerun attempts while Intent and Simulation bindings change.
2. Ready Repository Models contain exact path/unit/governance and Buddy plus
   Official quality/projection policy closure and digest. Incomplete models
   carry no compiled policy.
3. Release planning accepts no external `ReleasePolicy` and consumes only the
   admitted Snapshot closure.
4. Build mechanics require no upload metadata. Successful post-upload
   formation consumes the original mechanics/tarball once; failed mechanics
   produce failed Evidence without transport.
5. Release Artifact admission binds repository, exact GitHub Actions artifact
   URL, producer/run/attempt, and deterministic purpose/role/attempt name.
6. No CLI, workflow, docs, HK, remote observation, or live authority changes.

<!-- END APPEND: workflow-delivery-v3-commit6-core-correction-research -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit6-cli-workflow-research -->

# Workflow Delivery v3 Commit 6 CLI and Official Simulation Research

This append-only phase extends the current uncommitted Release core through
cross-job CLI transport and the Official simulation workflow. Commit-7 remote
observation, live authority, publication mutation, Receipt, authorization, and
capability implementation remain excluded.

## Bounded target inventory

- Release transport:
  `records/release.py`, `records/release_transport.py`, and admitted
  `RepositoryModelSnapshot` parsing in `repository/compiler.py`.
- CLI and mechanics:
  `cli.py`, `release/simulation.py`, and `release/workflow.py`.
- Workflow:
  `.github/workflows/workflow-delivery-v3-official-simulate.yml`.
- Focused tests:
  `tests/release/test_commit6_transport_cli.py`,
  `tests/contracts/test_official_simulation_workflow.py`, and the commit-6
  command-availability assertions in `tests/test_cli.py`.
- HK already governs `.github/workflows/workflow-delivery-v3-*.yml`; no HK
  production change is required.

## Acceptance checklist

| ID | Requirement |
|---|---|
| C6T-1 | Closed canonical deserialization and caller-authoritative current bindings for Intent, admitted model, Binding, Snapshot, Artifact, Evidence, Decision, and Outcome. |
| C6T-2 | CLI record chain covers normalization, model admission/compilation, identity, planning/context, mechanics, post-upload formation, exact-four finalization, unavailable observation, empty actions, and non-success Outcome. |
| C6T-3 | Request ID is rerun-stable while records are attempt-bound; stale, cross-purpose, noncanonical, producer, digest, and type substitutions fail. |
| C6T-4 | Official workflow has the exact 12-job DAG, dispatch surface, permissions, concurrency, runner, deadlines, and commit-5 action pins. |
| C6T-5 | Artifact transport is raw, 45-day, non-overwriting, hidden-file inclusive, ID-only, digest checked, and deterministic. |
| C6T-6 | Build runs once before tarball upload; Release Artifact and build Evidence bind upload outputs afterward. |
| C6T-7 | Qualification closes exactly four Evidence obligations, including independent project-test and two npm artifact Evidence identities. |
| C6T-8 | Observation performs no registry/network work, actions stay empty, no PublicationSnapshot is emitted, successful qualification ends incomplete/unsupported, and failed or incomplete qualification is preserved. |
| C6T-9 | Existing CI behavior remains unchanged and commit-7+ CLI commands remain unavailable. |
| C6T-10 | Validate focused/full v3 tests, Ruff, Pyrefly, actionlint, Pkl/diff checks, and record exact blockers. |

<!-- END APPEND: workflow-delivery-v3-commit6-cli-workflow-research -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit6-raw-name-correction-research -->

# Workflow Delivery v3 Commit 6 Raw Artifact Name Correction Research

Pinned `actions/upload-artifact` v7 with `archive: false` uses the basename of
the first uploaded file as the physical artifact name and does not use the
configured `name` value. The Official simulation workflow therefore cannot
upload fixed basenames such as `release-intent.json` while claiming a
digest-bound configured name.

## Correction inventory

- Seventeen raw uploads require exact physical basenames:
  Intent, Provider Result, Repository Model, Simulation Binding,
  Qualification Snapshot, Adapter context, tarball, Release Artifact, four
  Evidence records, Qualification Decision, observation boundary, actions
  boundary, Simulation Outcome, and human summary.
- Every downstream CLI path must reference the propagated physical basename
  after ID-only raw download.
- `release_artifact_transport_name` must include `.tgz`; the Plan output,
  build path, upload path, CLI metadata, ReleaseArtifact admission, and npm
  qualification paths must use that exact string without appending a suffix.
- The workflow already exposes semantic digests before upload, so JSON/Markdown
  outputs can be moved to purpose/role/run/attempt/digest names immediately
  before upload.

## Acceptance checklist

1. For every `archive: false` upload, `basename(path)` equals the configured
   name and is the intended physical identity.
2. Every physical name visibly binds purpose, role, run attempt, and digest;
   roles keep names unique within one run.
3. Every consumed raw artifact propagates its basename through direct job
   outputs and uses it in downstream CLI arguments.
4. Tarball identity includes `.tgz` exactly once from Plan through
   ReleaseArtifact validation and qualification.
5. A negative contract test records upload-artifact v7's configured-name
   behavior.
6. No docs, consumer policy, live release, or observation implementation
   changes are made.

<!-- END APPEND: workflow-delivery-v3-commit6-raw-name-correction-research -->
