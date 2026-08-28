# Workflow Delivery v3 Snapshot Admission Research

## 2026-08-28 Workflow Delivery v3 Retry-4 Acceptance Preparation Research

### Scope and authority

- Strategy: **single-pass Research -> Plan -> Implement**, tests-first and
  deliberately RED.
- Workspace:
  `/workspace/three-workspaces/design-workflows`.
- Branch: `workflow-delivery-v3-acceptance-retry-4`.
- Authoritative base and current HEAD:
  `bcf47e2d817b718adf96a67ef0506d220b74f2bf`.
- The current v3 handoff was read first:
  `docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md`.
- The current workspace is authoritative. Historical retry-3 files may be
  consulted only as mechanism references. They must not be restored.
- `bf1748971f2717a8877852590c5436b4160a4fbf` is implementation provenance
  only; neither it nor the work-base SHA is an acceptable reviewed target.
- This phase may edit only tests, test-local fixtures/contracts, and these
  `.testagent` records. It must not edit production Python registries or add
  the workflow.

### Language and repository conventions

- Python 3.13, pytest 9.1.1, a UV workspace, and Hatchling.
- Package manifest:
  `src/public/lib/three-workflow-delivery-v3/pyproject.toml`.
- Root pytest configuration uses importlib import mode and includes the v3
  tests in `testpaths`.
- Existing tests use plain `test_*` functions, bare concrete assertions,
  descriptive parameter IDs, `pytest.raises(..., match=...)`, exact
  tuple/dictionary equality, canonical JSON bytes, `tmp_path`,
  `monkeypatch`, controlled fake transports/runners, and `yaml.safe_load`.
- Missing retry-4 production symbols and the absent workflow must be accessed
  from test bodies so collection remains green and execution is behaviorally
  RED.
- The `code-testing-extensions` entry point was unavailable; the local base
  extension at
  `.agents/skills/code-testing-extensions/extensions/python.md` was read.
  Its normal green-suite rule is superseded here by the explicit tests-first
  request to preserve expected RED failures.

### Approved retry-4 identity and binding contract

- Package: `@hcoona/hcoona-release-smoke-npm`.
- Stable scenario order and exact bindings:
  1. `absent-create-readback` ->
     `0.0.0-wdv3-acceptance.13`, `wdv3-acceptance-13`;
  2. `exact` -> `0.0.0-wdv3-acceptance.13`,
     `wdv3-acceptance-13`;
  3. `identical-race` -> `0.0.0-wdv3-acceptance.14`,
     `wdv3-acceptance-14`;
  4. `differing-race` -> `0.0.0-wdv3-acceptance.15`,
     `wdv3-acceptance-15`;
  5. `lost-response` -> `0.0.0-wdv3-acceptance.16`,
     `wdv3-acceptance-16`.
- Fourth base coordinate:
  `@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13`.
- Workflow path:
  `.github/workflows/workflow-delivery-v3-buddy-smoke-acceptance-retry-4.yml`.
- Workflow stem and Environment:
  `workflow-delivery-v3-buddy-smoke-acceptance-retry-4`.
- Confirmation:
  `I_ACCEPT_DISPOSABLE_GITHUB_PACKAGES_PROBES_RETRY_4`.
- Confirmation digest:
  `sha256:b6f94d3c13c98b0714404959dd878230f8302ee849038a536f5a18cc3a85c7ec`.
- Preparation target:
  `0000000000000000000000000000000000000000`, exactly forty ASCII
  zeroes.
- `.1` through `.12` and every historical identity remain consumed.
- `live_enabled` remains `false`.

### Bounded target inventory

Test files to modify:

1. `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`
   - fourth Adapter profile order/uniqueness;
   - resolution and exact coordinates;
   - suite behavior using existing fakes;
   - `_AcceptanceNpmRunner` coordinates;
   - matched proof and bidirectional cross-profile rejection;
   - move two `.13` negative fixtures to clearly unregistered `.17`.
2. `src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py`
   - fourth Governance profile order/uniqueness;
   - exact zero-sentinel preparation admission;
   - fail-closed zero shape;
   - placeholder-only finalized target shape;
   - exact workflow, Environment, confirmation, digest, coordinate, and tag
     bindings;
   - cross-profile rejection and historical evidence preservation.
3. `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
   - permit and require exactly the retry-4 temporary workflow during
     preparation while normal Buddy remains disabled.
4. `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py`
   - permit exactly retry-4 and continue rejecting every other temporary or
     legacy workflow.

Test file to add:

5. `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_retry_4_workflow.py`
   - dedicated retry-4 workflow contract re-authored from current authority
     and historical retry-3 mechanisms.

Read-only behavior targets:

- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py`
  (`_ACCEPTANCE_SUITE_PROFILES`,
  `_ACCEPTANCE_COORDINATE_TAG_PAIRS`,
  `fixed_acceptance_scenario_specs`, `fixed_acceptance_coordinates`,
  `ValidatedAcceptanceRequestProof`, and `run_fixed_acceptance_suite`).
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/governance.py`
  (`_GovernanceAcceptanceProfile`,
  `_GOVERNANCE_ACCEPTANCE_PROFILES`, `_acceptance_profile`,
  `_require_zero_target_rejected_dispatch`, and
  `admit_governance_acceptance_evidence`).
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
  (`_AcceptanceNpmRunner` and the existing fixed-suite CLI route); this is a
  consumer only and requires no CLI change.
- The absent retry-4 workflow path above; production workflow creation is
  forbidden in this phase.
- `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`;
  `live_enabled: false` is read-only.

### Existing fixture conflict

The now-approved `.13` coordinate occurs in two current Adapter negative
fixtures and must move without weakening their assertions:

1. `test_retry_2_suite_resolves_only_the_reviewed_coordinate_block`, near
   line 242: replace the unreviewed base `.13` with `.17`.
2. `test_acceptance_probe_requires_the_fixed_coordinate_and_explicit_tag`,
   near lines 2529-2530: replace coordinate/tag `.13` with
   `.17`/`wdv3-acceptance-17`.

`.17` is syntactically valid and clearly outside all four registered blocks.

### Requirement checklist

- [ ] A1: Adapter has exactly four profiles in stable historical order
  `.1`, `.5`, `.9`, `.13`.
- [ ] A2: Governance has exactly four profiles in the same stable order.
- [ ] A3: All four base identities are unique.
- [ ] A4: Every profile retains exact five-scenario order.
- [ ] A5: Profile-qualified base/scenario/coordinate/tag identities are
  unique, with only the intentional absent/exact reuse inside each profile.
- [ ] A6: The four four-version blocks yield 16 unique accepted
  coordinate/tag pairs.
- [ ] A7: Retry-4 uses exactly `.13`/`.13`/`.14`/`.15`/`.16` and tags
  `-13`/`-13`/`-14`/`-15`/`-16`.
- [ ] A8: `fixed_acceptance_scenario_specs` and
  `fixed_acceptance_coordinates` resolve the fourth profile.
- [ ] A9: `_AcceptanceNpmRunner` uses all five exact retry-4 coordinates.
- [ ] A10: `run_fixed_acceptance_suite` routes the exact retry-4
  base/coordinate/tag values through controlled fakes.
- [ ] A11: A matched retry-4 proof is accepted and retry-4/historical
  coordinate-tag substitutions are rejected in both directions.
- [ ] A12: Both formerly negative `.13` fixtures move to `.17` while their
  negative assertions remain unchanged.
- [ ] G1: Governance binds the exact retry-4 workflow path and Environment.
- [ ] G2: Governance binds the exact confirmation literal and digest.
- [ ] G3: Governance binds the exact `.13` through `.16` scenario
  coordinates/tags.
- [ ] G4: Preparation target is exactly forty ASCII zeroes.
- [ ] G5: Only an exact zero-target rejected-dispatch shape is admitted:
  validation failed; review and both probes skipped; no probe records,
  artifact, or reviewer; mutation classification incomplete.
- [ ] G6: Zero-target documents implying review, retained scenarios, or
  possible mutation are rejected.
- [ ] G7: Eventual finalized shape is represented only with a clearly named
  test-local 40-hex placeholder reviewed target and temporary registry patch;
  the placeholder is not authority.
- [ ] G8: The placeholder finalized shape round-trips canonically with exact
  retry-4 scenario/proof bindings where representable.
- [ ] G9: Cross-profile workflow path, Environment, recovery Environment,
  digest, target, coordinate, and tag substitutions are rejected.
- [ ] G10: Retry-1 through retry-3 admission, profile tuples, suite digests,
  and replay evidence remain unchanged.
- [ ] W1: A dedicated retry-4 static workflow contract is modeled on
  historical retry-3 mechanics but re-authored against current source.
- [ ] W2: It requires exactly five jobs:
  `validate-fixed-inputs`, `acceptance-review`,
  `probe-absent-create-readback`, `probe-exact-and-conflict`, and
  `capture-governance-evidence`.
- [ ] W3: It requires first-attempt guards, terminal `always()` capture,
  Environment only on review, and `packages: write` only on the two probe
  jobs.
- [ ] W4: The forty-zero target fails in validation before review or either
  write-capable probe can run.
- [ ] W5: A test-only nonzero placeholder demonstrates the finalized guard
  shape without assigning an actual final target.
- [ ] W6: Exact dispatch inputs, confirmation/digest, current pinned actions
  and toolchains, concurrency, checkout, probe, and terminal evidence wiring
  are fixed.
- [ ] W7: Wrong inputs fail closed and Live, Release, bypass, force,
  schedule, push, `workflow_call`, and generalized routes are absent.
- [ ] T1: Topology and retirement permit exactly retry-4 during preparation;
  original, retry-2, and retry-3 temporary workflow sources remain absent.
- [ ] T2: Disabled normal Buddy and `live_enabled: false` remain required.
- [ ] S1: No production registry, workflow, CLI schema, generalized profile
  framework, generic architecture, manifest, lock, or external state change.
- [ ] S2: No Live, bypass, external workflow/Environment/package/ref
  mutation, or historical file restoration.
- [ ] S3: Test collection stays green; execution is RED only because fourth
  profiles and workflow behavior are absent.
- [ ] S4: Narrow runs record exact node IDs and classify expected missing
  production/workflow behavior separately from accidental test defects.

### Exact commands

Run from the repository root.

Collection:

```text
uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q \
  src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py \
  src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_retry_4_workflow.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py
```

Narrow Adapter, Governance, and workflow/topology RED runs:

```text
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py \
  -k retry_4

uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py \
  -k retry_4

uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_retry_4_workflow.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py \
  -k 'retry_4 or temporary_acceptance'
```

Combined RED run:

```text
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py \
  src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_retry_4_workflow.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py \
  -k 'retry_4 or temporary_acceptance'
```

Quality checks:

```text
uv run --python 3.13 ruff check <the five bounded test paths>
uv run --python 3.13 ruff format --check <the five bounded test paths>
uv build --package three-workflow-delivery-v3
git --no-pager diff --check
```

The final scoped execution must remain RED. Collection, lint, formatting,
package build, and unrelated baseline behavior must not fail.

## 2026-08-13 Commit 8 Governance Observation Error Taxonomy Research

### Scope and strategy

This is a focused **single-pass Research -> Plan -> Implement** test-only run
covering the current uncommitted commit-8 Governance observation work. The
working tree is authoritative. No production, workflow, manifest, or existing
test deletion is permitted.

Subagent delegation was requested but unavailable because the current agent
was already at the maximum delegation depth. The same sequential pipeline is
therefore recorded and executed inline.

### Instructions and language guidance

- Read `AGENTS.md` and the normative v3 handoff before research.
- Read `.agents/skills/code-testing-agent/unit-test-generation.prompt.md`
  before implementation.
- `code-testing-extensions` was attempted by the parent and is unavailable.
- Repository conventions establish Python 3.13, pytest, importlib import mode,
  `pytest.mark.parametrize`, concrete bare assertions, injected fake
  transports/clients, Ruff, Pyrefly, and UV workspace commands.
- Existing canonical tests are extended where possible:
  `tests/release/test_eligibility.py`,
  `tests/platform/test_github.py`, and
  `tests/adapters/test_commit8_publish_governance_recheck.py`.

### Bounded target inventory

Production behavior inspected, but not editable:

- `release/eligibility.py`
  - `observe_governance_source`
  - `parse_governance_attestation`
  - `require_fresh_governance_identity`
  - `GovernanceFreshnessRejectionError`
- `platform/github.py`
  - `GitHubRestClient.is_ref_protected`
  - REST/JSON/base64 error normalization
- `adapters/github_packages.py`
  - publisher Governance recheck and exact terminal result
- `cli.py` and existing commit-8 live-scenario tests
  - missing/malformed generic post-marker fallback

Test targets:

- new focused Governance observation taxonomy tests under `tests/release/`;
- focused additions to `tests/platform/test_github.py`;
- existing publisher and live-finalizer tests retained as evidence for the
  exact definitive and generic fallback states.

### Requirement checklist

| ID | Requirement | Research evidence / intended test |
|---|---|---|
| G1 | Definitive `GovernanceRejectionError` for authoritatively observed unprotected refs. | Direct observation with exact Boolean `False`; assert exact error class identity/name and no ref/blob read. |
| G2 | Definitive `GovernanceRejectionError` for successfully fetched invalid canonical JSON/schema. | Parameterized canonical/noncanonical/schema-invalid fetched bytes. |
| G3 | Definitive `GovernanceRejectionError` for successfully fetched invalid policy-package binding, lifetime, inventory, attestation semantics, or content digest inconsistency. | Parameterized semantic documents plus a digest-mismatch seam test. |
| G4 | Existing disabled, expired, and changed outcomes remain typed. | Direct `require_fresh_governance_identity` matrix asserting exact `GovernanceFreshnessRejectionError`. |
| G5 | Local source/time configuration errors are not `GovernanceRejectionError`. | Wrong fixed source and naive observation time, with zero remote calls. |
| G6 | Malformed/non-Boolean commit, blob, and API identities are not `GovernanceRejectionError`. | Parameterized malformed protection/commit/blob identities. |
| G7 | Transport/network/HTTP/permission/protocol/base64/JSON failures are not `GovernanceRejectionError`. | Raising source client plus concrete GitHub REST failure tests. |
| G8 | GitHub protection client distinguishes authoritative false from permission/5xx/network/malformed unknown. | 404 false control; permission, 5xx, network, and malformed response must raise. |
| G9 | Publisher definitive rejection persists exact failed/no-side-effect state and invokes zero runners. | Existing `test_publish_second_governance_read_returns_terminal_no_side_effect` and `test_publish_cli_persists_governance_terminal_state_before_nonzero`, extended only if new observation cases expose a gap. |
| G10 | Publisher generic fallback remains incomplete/possibly-mutated. | Existing `test_post_marker_governance_terminal_state_lookalikes_are_possibly_mutated` and generic publish CLI branch. |

### Current behavior gap

The current source has `GovernanceFreshnessRejectionError`, but no
`GovernanceRejectionError`. `observe_governance_source` currently maps an
authoritative unprotected ref and all fetched attestation validation failures
to generic `ValueError`. `GitHubRestClient.is_ref_protected` catches every
`GitHubRestError` and converts it to `False`, collapsing permission, 5xx,
network, and malformed-API unknowns into an authoritative rejection. Because
production edits are forbidden, focused tests are expected to preserve these
gaps as exact failures until the production observation taxonomy is added.

### Exact validation commands

1. Narrow Governance taxonomy and platform tests:
   `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q <focused paths>`
2. Existing publisher/live fallback evidence:
   `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q <publisher/live paths> -k <focused expression>`
3. Full package tests:
   `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
4. Type/lint/format:
   `uv run --python 3.13 pyrefly check <focused tests>` and
   `uv run --python 3.13 ruff check/format --check <focused tests>`
5. Workspace build validation:
   `uv build --package three-workflow-delivery-v3`
6. `git --no-pager diff --check`

## 2026-08-13 Workflow Delivery v3 Commit 8 History Admission Findings 10-13

### Request

Implement only user findings 10-13 for commit-8 history discovery/admission,
preserving existing edits, not editing workflow YAML, and not changing
`platform/github.py` unless impossible.

### Instructions and LLD Sections Read

- `AGENTS.md` and `docs/AGENTS.md`.
- `docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md`.
- `docs/wiki/analyses/workflow-delivery/v3/hcoona-release-smoke-npm-lld.md`
  sections on canonical records/bindings, Live Buddy history admission, and
  Bootstrap and History Scenarios.

### Bounded Target Inventory

- Production: `release/live.py` history discovery/admission helpers only.
- Tests: `tests/release/test_commit8_history_admission.py`, plus a focused
  existing commit-8 live history scenario fixture update needed to keep the
  stricter discovery contract passing.
- No workflow YAML or `platform/github.py` edits.

### Acceptance Checklist

| ID | Requirement |
|---|---|
| H10 | Filter different-target well-formed runs without querying artifacts/jobs. |
| H11 | Admit only explicit historical schemas; skip unrelated JSON, non-JSON, and multi-file artifacts; fail recognized malformed/conflicting payloads. |
| H12 | Require strict execution/target/live-purpose lineage and exact phase facts from unique finalizer/publisher job identities without artifact-to-job or reusable-workflow provenance claims. |
| H13 | Enumerate same-run artifacts for prior attempts, prove earlier attempts separately from artifacts, and fail closed when proof is missing. |

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

## 2026-08-12 Workflow Delivery v3 Commit 7 CLI/Workflow Integration Addendum

### Request

Implement commit-7 Official simulation npmjs observation and CLI/workflow
transport, replacing commit-6 observation-unavailable/action-boundary records.
No subagents, commit, push, live Publication Snapshot, authorization,
capability, Receipt, mutation, GitHub Packages, Buddy, or commit 8 scope.

### Bounded Target Inventory

- `release/simulation.py`: physical cross-job observation/action bundles and
  summary rendering.
- `release/finalizer.py`: commit-7 observation classification and hypothetical
  action outcome mapping.
- `adapters/npmjs.py`: credential-free stdlib npmjs observer (already present
  in working tree and consumed by CLI).
- `cli.py`: `release observe-npmjs`,
  `materialize-hypothetical-actions`, and `finalize-simulation` boundaries.
- `.github/workflows/workflow-delivery-v3-official-simulate.yml`: existing
  12-job Official simulation DAG, updated in-place.
- Tests under `tests/release`, `tests/adapters`, and
  `tests/contracts/test_official_simulation_workflow.py`.

### Acceptance Checklist

| ID | Requirement |
|---|---|
| C7-1 | Replace commit-6 unavailable/empty boundary transport with canonical observation-set/action-report bundles. |
| C7-2 | Bind simulation identity, purpose, target, producer, current run/attempt, Snapshot/Decision digests, and exact observation/action digests; include no PublicationSnapshot/live lineage. |
| C7-3 | Add `release observe-npmjs` using credential-free stdlib adapter and skip network for non-successful qualification. |
| C7-4 | Materialize absent actions and empty reports for exact/non-ready/non-success. |
| C7-5 | Finalizer recomputes expected actions/outcome, rejects substitutions, and maps success/non-success exits correctly. |
| C7-6 | Workflow keeps the approved 12-job names/DAG and `permissions: contents: read`; no secrets, npm token/auth, id-token, packages permission, Environment, or mutation. |
| C7-7 | Workflow downloads/transports Snapshot, Decision, Adapter context, Release Artifact, observation set, and action report by explicit IDs/digests/raw names. |
| C7-8 | Static tests understand `archive: false` physical basenames and commit-7 names. |
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

<!-- BEGIN APPEND: workflow-delivery-v3-commit7-observer-core-research -->

# Workflow Delivery v3 Commit 7 Observer Core Research

This append-only section records the commit-7 observer-core target inventory and
acceptance checklist.

## Instructions and sources read

- `AGENTS.md`: v3 work must read the v3 handoff first; v1/v2 are not normative.
- `docs/AGENTS.md`: docs wiki rules and immutable source/raw boundaries.
- `agent-handoff.md`: commit 7 is the npmjs observation boundary; no live
  activation, publication mutation, Authorization, Capability, Receipt,
  workflow YAML, or CLI expansion.
- `release-delivery-mld.md` remote observation/simulation mapping: absent and
  exact succeed; unknown is incomplete/rerun; unprovable is incomplete/fix
  capability; partial/conflicting fail for reconciliation.
- `hcoona-release-smoke-npm-lld.md` npmjs adapter sections: exact first-slice
  coordinate only, public registry, SHA-512 over raw `.tgz`, in-package target
  witness, `dist.integrity` auxiliary only, and no routing tag for npmjs.

## Bounded target inventory

- Production:
  - `three_workflow_delivery_v3/adapters/npmjs.py`
  - `three_workflow_delivery_v3/adapters/__init__.py`
  - `three_workflow_delivery_v3/records/release.py`
  - `three_workflow_delivery_v3/records/release_transport.py`
  - `three_workflow_delivery_v3/release/finalizer.py`
- Tests:
  - `tests/adapters/test_npmjs.py`
  - `tests/release/test_commit7_observation.py`
  - `tests/adapters/test_node.py` export assertion update.

## Acceptance checklist

| ID | Requirement |
|---|---|
| C7-R1 | Add credential-free injectable npmjs HTTP observation adapter with no network in tests. |
| C7-R2 | Enforce exact first-slice coordinate `@hcoona/hcoona-release-smoke-npm` plus frozen native version and registry.npmjs.org URL policy. |
| C7-R3 | Classify exact 404 as absent; 401/403/other hard 4xx/malformed/off-policy as unprovable; timeout/network/429/5xx/truncation as unknown. |
| C7-R4 | Require 200 metadata and tarball responses to use identity content encoding and bounded byte reads; reject off-host tarball URLs and redirects. |
| C7-R5 | Download complete raw `.tgz`, compute SHA-512, validate package identity/version and canonical in-package target witness against the qualified basis. |
| C7-R6 | Treat byte-identical SHA-512 plus exact witness as exact; differing complete valid bytes or different target witness as conflicting; digest-only evidence is not exact. |
| C7-R7 | Evolve ProjectionObservation minimally for purpose, target, and producer binding and add strict transport deserialization/admission. |
| C7-R8 | Finalizer admits real observations, materializes hypothetical actions only for absent/exact, maps all observation outcomes, and preserves failed/incomplete qualification without observation. |
| C7-R9 | Keep synthetic observation support private test support only; no public success shortcut. |
| C7-R10 | Do not edit workflow YAML or CLI and do not add credentials, live identity, Authorization, Capability, Receipt, mutation, GitHub Packages, Buddy tag, services, or commit-8 code. |

## Source-to-test mapping

| Requirement | Test evidence |
|---|---|
| C7-R1 | `ScriptedTransport`; `test_npmjs_observer_does_not_fetch_after_failed_qualification`. |
| C7-R2 | `test_npmjs_observer_rejects_wrong_coordinate_before_network`; malformed/wrong metadata test. |
| C7-R3 | `test_npmjs_observer_classifies_exact_404_as_absent`; hard-4xx/retryable/timeout tests. |
| C7-R4 | redirect/nonidentity and size/truncation tests. |
| C7-R5 | `test_npmjs_observer_accepts_exact_bytes_and_witness`. |
| C7-R6 | byte conflict, witness conflict, and integrity-only tests. |
| C7-R7 | `test_projection_observation_crosses_transport_with_current_bindings`; purpose/target substitution test. |
| C7-R8 | `test_finalize_simulation_maps_commit7_observation_outcomes`; `test_materialize_hypothetical_actions_accepts_only_absent_and_exact`; failed qualification test. |
| C7-R9 | Existing commit-6 synthetic private tests retained and passing. |
| C7-R10 | Changed-file review and no workflow/CLI files modified in this phase. |

<!-- END APPEND: workflow-delivery-v3-commit7-observer-core-research -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-research-2026-08-13 -->

# Workflow Delivery v3 Commit 8 Research

## Bounded scope

Commit 8 adds the disabled live Buddy boundary only: history admission, live
Attempt binding, GitHub Packages observation/publication, reviewer summary,
approval and Authorization, immediate Governance freshness admission,
capability-group results, Receipts, and live finalization. It does not add
CODEOWNERS work, acceptance bootstrap, legacy retirement, activation, live
Official publication, OIDC, PATs, Apps, services, or compatibility routes.

## Existing implementation inventory

- Canonical JSON, strict digest primitives, artifact identities, caller-selected
  current/history authority, Release Intent, Buddy Execution Identity, Release
  Attempt Identity, Qualification Snapshot/Decision, Publication Action,
  guarded Publication Snapshot formation, Repository Model, and Live
  Eligibility Decision already exist.
- Commit-7 strict bundle and transport patterns are reusable.
- Static GitHub Packages destination and `github/packages-write-v1` capability
  definitions already exist.
- No live history client, GitHub Packages Adapter, Authorization, Capability
  Admission Decision, Action Result, Receipt, capability-group bundle, Attempt
  Outcome, live CLI chain, or v3 Buddy workflow exists.

## Confirmed implementation decision

The approval job remains `permissions: {}` and credential-free. It anonymously
fetches exact selected target SHA from the public `hcoona/three` Git repository,
verifies detached `HEAD`, and invokes the same-revision Authorization formatter.
It does not use `GITHUB_TOKEN`, Actions artifact credentials,
`actions/checkout`, a moving ref, or fallback revision.

## Acceptance checklist

1. Strict current-attempt canonical records and substitution rejection.
2. Caller-selected, exhaustive, history-only admission with separate platform
   run/job facts and no unsupported artifact-to-attempt claims.
3. Successful approval is the only Authorization source; Environment denial is
   diagnostic-only and grants no Capability.
4. Capability admission validates the exact authorized closure and repeats the
   fixed-source Governance read immediately before scheduling publication.
5. GitHub Packages observation classifies absent, exact, partial, conflicting,
   unknown, and unprovable across REST, npm metadata, tarball, witness, and tag.
6. Publication is one create-only compound `npm publish --tag
   buddy-sha-<target>` operation with no standalone tag mutation or overwrite.
7. Complete coordinate-plus-tag keys remain authoritative while GitHub
   concurrency conservatively groups physical destination plus normalized
   package.
8. Receipt formation requires durable exact response/readback proof; a lost
   response or Receipt is incomplete and possibly mutated.
9. Exactly one capability-group result bundle covers the exact active action
   set; missing, duplicate, extra, or conflicting members block finalization.
10. Pre-observed exact state still requires approval but creates no action,
    package-write Capability, bundle, or Receipt.
11. Platform termination before capability proves no side effect; termination
    after capability may have started is possibly mutated.
12. Whole-release replay rejects prior-attempt current authority and mixed
    failed-job reruns.
13. Caller-held Execution concurrency and publisher resource concurrency are
    distinct and `cancel-in-progress: false`.
14. Workflow permissions are exact and negative; no PAT, OIDC, inherited
    secrets, name-selected artifact transport, or write permission outside the
    publisher.
15. The protected activation state remains disabled and no later rollout scope
    is introduced.

<!-- END APPEND: workflow-delivery-v3-commit8-research-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-phase2-research-2026-08-13 -->

# Workflow Delivery v3 Commit 8 Phase 2 Test Research

## Bounded target inventory

- Primary future target:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py`.
- Primary test target:
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_github_packages.py`.
- Directly relevant existing contracts:
  `records/release.py`, `release/live.py`, `adapters/npmjs.py`,
  `tests/adapters/test_npmjs.py`, and the two commit-8 Release test files.
- Export tests are in scope only if needed to pin the Adapter's public surface.
  Workflow YAML, activation, production publication, and unrelated adapters are
  outside scope.

The requested `code-testing-extensions` skill is unavailable and was already
attempted by the parent. Python conventions are therefore inferred from the
repository's pytest suites: function tests, frozen in-memory fakes, exact tuple
and document equality, `pytest.mark.parametrize` for closed classifications,
anchored exception assertions, and no plugin dependence across test
directories.

## Current implementation boundary

The commit-8 live records and pure admission/finalization helpers exist.
There is currently no `adapters/github_packages.py` implementation or public
GitHub Packages Adapter export. Phase 2 is consequently a test-first contract
phase: the focused suite must collect all scenario names but is expected to
report the missing Adapter API as the production blocker until the next
production phase.

## Requirement checklist

| ID | Separate required behavior |
|---|---|
| C8P2-R1 | Exact escaped REST and npm endpoints, complete pagination, fixed headers, and positive timeout/body/page bounds. |
| C8P2-R2 | Wrong live basis, coordinate, qualification, or artifact binding fails before any transport call. |
| C8P2-R3 | Independently exercise all six classifications: absent, exact-satisfied, partial, conflicting, unknown, unprovable. |
| C8P2-R4 | Exact state requires byte-identical tarball, matching in-package witness, and exact target-derived tag mapping. |
| C8P2-R5 | REST version state and npm metadata/tag disagreement never becomes exact or absent. |
| C8P2-R6 | Tokens never enter records/diagnostics; redirects retain Authorization only on the exact approved origin. |
| C8P2-R7 | Publish invokes exact create-only npm argv, uses a private temporary npm config, and removes it on success and failure. |
| C8P2-R8 | Force, overwrite, unpublish, delete, restore, standalone dist-tag mutation, PAT, and OIDC operations are forbidden. |
| C8P2-R9 | Created, create-conflict, lost-response, identical-race, and differing-race outcomes have distinct fail-closed semantics. |
| C8P2-R10 | Receipt and response identities reject action, snapshot, Attempt, artifact, coordinate, tag, and response substitution. |
| C8P2-R11 | Complete coordinate-plus-tag keys are retained while different versions/tags share the conservative normalized package group. |
| C8P2-R12 | Failed qualification/no action/no capability and other preconditions prevent network or publish execution. |
| C8P2-R13 | Only fakes are used; no real network, npm publish, workflow YAML, activation, or production mutation is introduced. |

## Source-to-test pairing

The new Adapter suite will use a recording HTTP fake and recording command
runner. Each fake raises on an unscripted call, making no-network/no-publish
claims independently observable. The suite will name the expected public API
and keep missing implementation failures explicit rather than skipping tests.

<!-- END APPEND: workflow-delivery-v3-commit8-phase2-research-2026-08-13 -->

## Commit 8 Phase 2 Final Review Addendum

The mandatory prompt-scenario and pseudo-mutation review found that the first
draft passed the desired classification into the classifier and constructed
publication results directly from commit-8 records. Those tests could not pin
Adapter decision logic. The focused contract now supplies independent REST,
npm, tarball, witness, and tag facts for all six classifications, and routes
created/conflict/lost-response plus identical/differing race facts through the
expected Adapter classifier.

The expected public surface inventory was also completed for every directly
called helper. Exact-state coverage now independently mutates remote bytes,
the in-package witness, and the target tag. Receipt review now checks action,
Snapshot, artifact, coordinate, tag, Attempt, and response substitutions.

`test-analysis-extensions`, like `code-testing-extensions`, is unavailable.
The final reviews therefore applied the pytest rules from the repository
directly. No production GitHub Packages Adapter exists, so Adapter mutation
points remain an explicit production blocker rather than executable coverage.

<!-- END APPEND: workflow-delivery-v3-commit8-phase2-final-research-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-phase34-tests-2026-08-13 -->

# Workflow Delivery v3 Commit 8 Phase 3/4 Test Research

## Bounded target inventory

- Required test targets:
  `tests/release/test_commit8_live_scenarios.py` and
  `tests/contracts/test_buddy_workflows.py` within the
  `three-workflow-delivery-v3` package.
- Directly required extension:
  `tests/test_cli.py` for the closed live command and terminal-status mapping.
- Production targets named by the contracts:
  `release/live.py`, `cli.py`,
  `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`, and
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`.
- Existing commit-8 records, finalizer, history, and GitHub Packages Adapter
  tests remain authoritative supporting coverage.
- HK already selects `src/public/lib/three-workflow-delivery-v3/**` and all
  `.github/workflows/workflow-delivery-v3-*.yml`; no HK edit is required.

The parent had already attempted `code-testing-extensions`, and it was
unavailable. The mandatory review also attempted `test-analysis-extensions`;
it was unavailable. Python pytest conventions were inferred from the existing
package tests: strict in-memory fakes, concrete/deep equality, exact exception
messages, parameterized closed sets, and no network or publication.

## Requirement checklist

| ID | Separate required behavior |
|---|---|
| C8P34-R1 | Closed live CLI commands, canonical artifact/file options, and exact success/fail-closed exit status map. |
| C8P34-R2 | Injectable history traversal exhausts run/artifact/job pages, downloads only by artifact ID, and blocks duplicate, rate-limited, denied, or truncated discovery before Attempt creation. |
| C8P34-R3 | Reviewer Snapshot and Markdown bytes remain byte-identical and bind payload digest, upload digest, and immutable artifact ID. |
| C8P34-R4 | Approval fetch accepts only an exact lowercase 40-character SHA, anonymously fetches the public repository, verifies exact detached HEAD, and uses no real network in tests. |
| C8P34-R5 | Every Governance freshness substitution blocks the current Attempt; restoration requires a different Attempt. |
| C8P34-R6 | Exact no-op still requires Authorization and emits no Capability or Receipt. |
| C8P34-R7 | Deployment-review rejection is diagnostic-only and cannot schedule Capability. |
| C8P34-R8 | Lost Receipt after possible mutation remains incomplete and requires reobservation. |
| C8P34-R9 | Platform termination maps differently before and after Capability may start. |
| C8P34-R10 | Whole-release replay rejects mixed-attempt capability records. |
| C8P34-R11 | Caller/callee DAG, least privilege, caller-held Execution concurrency, publisher resource concurrency, Environments, action pins, ID-only raw artifact transport, and 45-day retention are exact. |
| C8P34-R12 | Live activation remains disabled; acceptance/bootstrap, PAT/OIDC, publication bypasses, and all later scope remain absent. |
| C8P34-R13 | Tests use only injected fakes and static YAML reads; no GitHub API, network, package publication, or activation occurs. |

## Current production boundary

The pure record/finalizer and GitHub Packages Adapter portions exist. The five
phase-3 live orchestration APIs, six commit-8 CLI commands/status constant, and
both v3 Buddy workflow files do not exist in the authoritative tree. The new
tests therefore intentionally report these as production blockers while the
five record/finalizer scenarios execute and pass.

<!-- END APPEND: workflow-delivery-v3-commit8-phase34-tests-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-acceptance34-audit-2026-08-14 -->
## Commit 8 final 34-item acceptance audit research

- Scope stayed bounded to the six requested commit-8 test files, their
  production modules, both Buddy workflows, and the v3 design documents.
- `code-testing-extensions` was unavailable. Python 3.13 pytest conventions
  were inferred from existing tests: concrete bare assertions, strict frozen
  records/canonical transport, parameterized substitutions, and injected fakes.
- No standalone `c8-test-map` file or literal was found in `.testagent/`,
  `artifacts/`, or report JSON. The map below is reconstructed from the LLD,
  retained C8P34 evidence, and the user's independently auditable requirements.
- Baseline focused discovery was **165 tests**. Two genuine gaps remained:
  successful Authorization formation and exact non-empty Capability closure.

| ID | Requirement |
|---|---|
| C8-A01 | Strict records |
| C8-A02 | Binding substitutions rejected |
| C8-A03 | Exact current-Attempt authority |
| C8-A04 | Exhaustive caller-selected history |
| C8-A05 | Same-run prior-Attempt verification |
| C8-A06 | Unsupported provenance remains diagnostic |
| C8-A07 | Approval success only |
| C8-A08 | Denial diagnostic-only |
| C8-A09 | Authorization/denial-Evidence mutual exclusion |
| C8-A10 | Exact non-empty Capability closure |
| C8-A11 | Immediate Governance freshness |
| C8-A12 | Restoration requires a new Attempt |
| C8-A13 | Anonymous exact-SHA fetch |
| C8-A14 | Explicit workflow permissions |
| C8-A15 | Reusable caller ceiling |
| C8-A16 | Absent permits create-only action |
| C8-A17 | Approved exact no-op |
| C8-A18 | Partial cannot mutate |
| C8-A19 | Conflict cannot mutate |
| C8-A20 | Unknown cannot mutate |
| C8-A21 | Unprovable cannot mutate |
| C8-A22 | Identical race fails closed |
| C8-A23 | Differing race fails closed |
| C8-A24 | Lost response/Receipt and bindings |
| C8-A25 | Exact capability-group equality |
| C8-A26 | Pre-Capability termination |
| C8-A27 | Post-Capability termination |
| C8-A28 | Whole-release replay |
| C8-A29 | Mixed failed-job rejection |
| C8-A30 | Complete keys versus conservative projection |
| C8-A31 | Caller-held Execution concurrency |
| C8-A32 | ID-only transport and exact reviewer bytes |
| C8-A33 | Activation disabled |
| C8-A34 | No acceptance/bootstrap or commit-9+ scope |

<!-- END APPEND: workflow-delivery-v3-commit8-acceptance34-audit-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-redacted-auth-review-2026-08-13 -->
## Commit 8 redacted Authorization review research

Scope: assess the two reported live-credential findings in
`adapters/github_packages.py` and `platform/github.py` without restoring,
cleaning, reverting, resetting, stashing, checking out, committing, or
overwriting the existing uncommitted commit-8 work.

Intended design from the commit-8 handoff/LLD/tests:

- live transport headers must carry the actual in-process bearer credential;
- retained request facts, diagnostics, records, and evidence must keep only the
  redacted `******` marker;
- redirect handling may retain Authorization only on same-origin redirects and
  strips it across approved cross-origin tarball redirects;
- first-slice live GitHub REST reads and GitHub Packages reads are injected
  transports/clients, so the credential boundary is tested at header formation
  and request construction.

Counter-evidence found:

1. `github_api_headers(token)` and `_github_transport_headers(token)` contain
   `("Authorization", f"Bearer {token}")`; `_npm_transport_headers(token)`
   contains the same bearer header. The nearby `_retained_github_headers()`
   separately returns `("Authorization", _REDACTED)` and is used for retained
   `ObservationRequestFacts`.
2. `GitHubRestClient._request()` constructs the urllib request with
   `"Authorization": f"Bearer {self._token}"`.
3. Existing commit-8 tests already assert the bearer-header contract for
   `github_api_headers`, same-origin redirect retention, cross-origin redirect
   stripping, and diagnostic redaction. Tool output renders bearer-token
   expressions as `******`, so the review finding is explained by output
   redaction rather than by source/runtime behavior.

Conclusion: both findings are invalid live-defect reports. No production fix
or new generated scenario was required.

<!-- END APPEND: workflow-delivery-v3-commit8-redacted-auth-review-2026-08-13 -->
## 2026-08-13 Workflow Delivery v3 commit-8 workflow-fix contract research

### Bounded inventory and conventions

- Scope: `.github/workflows/workflow-delivery-v3-live-attempt.yml`,
  `tests/contracts/test_buddy_workflows.py`, `tests/release/test_commit8_live_scenarios.py`,
  `tests/release/test_commit8_contracts.py`, and `tests/test_cli.py`.
- Pytest functions use concrete structural equality and parse workflow YAML with
  `yaml.safe_load`; workflow tests inspect exact jobs, expressions, step order,
  commands, permissions, retention, and failure behavior.
- Production, commit-9+, activation, acceptance, CODEOWNERS, and legacy
  retirement are excluded.
- Focused command:
  `uv run --python 3.13 pytest src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`.

### Acceptance checklist

1. Exact `success` capability comparison.
2. Authorized pre-observed no-op skips publication and still finalizes.
3. Platform termination and capability-start facts reach finalization.
4. Receipt upload precedes bundle formation and supplies real ID/digest.
5. A failed capability still yields exactly one retained group bundle.
6. Final Attempt Outcome and Markdown summary are retained.
7. Approval formatter is offline and invokes no pip.
8. Authorization binds a real correlated job/check-run identity.
9. Governance-freshness blocked evidence is uploaded before nonzero propagation.
10. Missing Authorization still reaches the approval finalizer path.
11. Exact DAG, step order, 45-day retention, and error propagation.
12. CLI/workflow status evidence outputs are explicit and transport-bound.

### Current implementation finding

The current workflow violates multiple checklist items (including the
`admitted` comparison, pip installation, placeholder IDs, default skipped
finalizer paths, and absent final status uploads). Per user direction, tests
must remain accurate and failing; production changes are a blocker.

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-fourth-round-governance-recheck-2026-08-13 -->
## Commit 8 fourth-round Governance publish recheck research

### Bounded target inventory and conventions

- Production target:
  `src/three_workflow_delivery_v3/adapters/github_packages.py`, specifically
  `preflight_github_packages_action`, `_admit_mutation_marker`, and
  `publish_github_packages_action`.
- Existing live/finalization evidence:
  `tests/release/test_commit8_live_scenarios.py`.
- New focused adapter regressions belong under `tests/adapters/` and follow the
  existing pytest style: injected recording fakes, concrete call counts,
  parameterized substitutions, exact diagnostics, and no network/process use.
- Existing fixed-source reader contract is
  `release.eligibility.GovernanceSourceClient`; its three operations are
  protected-ref verification, exact-ref resolution, and exact-commit blob read.
- Focused command:
  `uv run --python 3.13 pytest src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit8_publish_governance_recheck.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py`.
- Full package command:
  `uv run --python 3.13 pytest src/public/lib/three-workflow-delivery-v3/tests`.

### Acceptance checklist

1. A successful preflight is not sufficient: publish performs a second fresh
   fixed-source Governance read after mutation-marker admission and directly
   before `runner.run`.
2. Disabled, expired, provenance-changed, and content-changed second reads each
   invoke the runner zero times.
3. An unchanged second read invokes the runner exactly once.
4. A live after-marker Governance failure finalizes as
   incomplete/possibly-mutated with reobserve required.
5. The recheck is exercised through `publish_github_packages_action`, not only
   through preflight or capability admission.
6. The adjudicated artifact-attempt finding remains an FP and is untouched.
7. Existing pytest conventions and deterministic in-memory fakes are used.
8. Current uncommitted edits remain authoritative; no tracked content is
   restored, deleted, reverted, overwritten, or committed.
9. Research, plan, and status artifacts receive append-only bounded sections.
10. If the publish API lacks the second-reader seam, regressions remain
    meaningful and failing rather than accepting weaker preflight-only checks.

### Current production finding

`publish_github_packages_action` admits the durable marker and then proceeds
to token/config preparation and `runner.run`. It accepts no Governance client
or observation instant and performs no fresh Governance read in the publish
path. The existing freshness substitution test covers capability admission
only. The adjudicated fourth-round TP is therefore reproducible as a missing
publish-path boundary; test-first regressions are expected to fail until
production supplies that API and behavior.
<!-- END APPEND: workflow-delivery-v3-commit8-fourth-round-governance-recheck-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit8-fifth-round-governance-terminal-state-2026-08-13 -->
## Test Generation Research

### Project Overview

- **Path**:
  `src/public/lib/three-workflow-delivery-v3`
- **Language**: Python 3.13+
- **Framework**: package CLI and immutable release records
- **Test Framework**: pytest 8 through the root uv workspace
- **Issue boundary**: commit-8 fifth-round handling of the publisher's second
  Governance read after mutation-marker admission and before `runner.run`.
  Workflow YAML, documentation/status, plans, unrelated commit-8 behavior, and
  all production/test edits are outside this research-only turn.
- The coordinator already attempted `code-testing-extensions`; it was
  unavailable. Harness discovery below therefore follows the repository's root
  `pyproject.toml` pytest configuration directly.

### Dependency Graph

- **Leaf/supporting contracts**:
  `release.eligibility.require_fresh_governance_identity`,
  `records.release.ActionResult`, and
  `records.release.CapabilityGroupResultBundle`. The record types already admit
  failed/no-side-effect diagnostics and need no speculative schema expansion.
- **Mid-layer target**:
  `adapters.github_packages.publish_github_packages_action`; it admits the
  marker, performs the fixed-source reread, and owns the zero-runner terminal
  result.
- **Top-layer targets**:
  `cli._release_publish_github_packages_command` persists the deferred state;
  `cli._release_form_github_packages_result_command` validates it and forms the
  Action Result/bundle; `release.live.finalize_attempt_outcome` maps the exact
  Governance-blocked result to current-Attempt failure/new-Attempt semantics.

### Build & Test Commands

- **Build**:
  `uv build --package three-workflow-delivery-v3`
- **Test (scoped — fix cycles)**:
  `uv run --python 3.13 pytest src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit8_publish_governance_recheck.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py`
- **Test (harness-equivalent — discovery check, from repository root)**:
  `uv run --python 3.13 pytest --collect-only -q`
- **Lint**:
  `uv run ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/live.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit8_publish_governance_recheck.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py`

### Scope

- **Production targets**:
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py`
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/live.py`
- **Supporting contract inspected, not a presumed edit target**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py`.
- **Direct test targets**:
  - `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit8_publish_governance_recheck.py`
  - `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
  - `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py`
- **Representative existing tests**:
  `tests/adapters/test_commit8_publish_governance_recheck.py` and
  `tests/release/test_commit8_live_scenarios.py`.

### Files to Test

#### High Priority

| File | Classes/Functions | Testability | Estimated Coverage | Notes |
|---|---|---|---|---|
| `adapters/github_packages.py` | `publish_github_packages_action`, `DeferredPublicationExecutionResult` | High | Partial | Existing tests prove reread ordering and runner-zero only by expecting a raised `ValueError`; they do not prove a persisted terminal result. |
| `cli.py` | `_release_publish_github_packages_command`, `_release_form_github_packages_result_command` | High | Partial | Publisher currently writes state only after a normal deferred return. Formation allows post-marker no-side-effect only for exact `create-conflict`; malformed states fall back conservatively. |
| `release/live.py` | `finalize_attempt_outcome` | High | Partial | Existing blocked Capability Decisions produce `capability-blocked`/`failure`/`new-attempt`, while ordinary failed bundles currently produce finalized failure/replay. |

#### Medium Priority

| File | Classes/Functions | Testability | Estimated Coverage | Notes |
|---|---|---|---|---|
| `records/release.py` | `ActionResult`, `CapabilityGroupResultBundle`, `AttemptOutcome` | High | Substantial | Existing closed values can express the required result; test transport/validation only if implementation changes these contracts. |
| `release/eligibility.py` | `require_fresh_governance_identity` | High | Substantial | Fixed-source reread is already centralized; its generic exception must be converted only at the publisher boundary, not broadly reinterpreted. |

#### Low Priority / Skip

| File | Reason |
|---|---|
| Workflows and docs/status/plan | Explicitly excluded; preserve current uncommitted edits. |
| Other adapters, history, and artifact-attempt code | Unrelated to the fifth-round terminal-state defect. |

### Existing Tests & Coverage Classification

- `adapters/github_packages.py` →
  `tests/adapters/test_commit8_publish_governance_recheck.py` and
  `tests/adapters/test_github_packages.py`: **partial for this issue**.
  Disabled, expired, commit/blob, and content changes prove zero runner calls,
  but failure escapes instead of returning the required exact terminal state.
- `cli.py` → `tests/test_cli.py` and
  `tests/release/test_commit8_live_scenarios.py`: **partial**. Generic
  missing/truncated/substituted post-marker state and Receipt persistence are
  covered; exact publisher Governance-state persistence and strict lookalike
  rejection are not.
- `release/live.py` → `tests/release/test_commit8_live_scenarios.py`:
  **partial**. Capability-admission Governance blocks already require a new
  Attempt, and generic post-marker termination is possibly mutated, but a
  durable publisher Governance-blocked bundle is not distinguished from an
  ordinary failed bundle.
- The required `find-untested-sources` run classified all four inspected
  production files as paired with tests. This is a static identifier/import
  heuristic, not line or branch coverage evidence.

### Existing Test Projects

- **Project file**:
  `src/public/lib/three-workflow-delivery-v3/pyproject.toml`
- **Root harness**: root `pyproject.toml` includes this package's `tests` path
  and supplies pytest through the uv dev dependency group.
- **Target source project**: `three-workflow-delivery-v3`
- **Test files in this bounded issue**: the three direct test targets listed
  above; no sibling package test inventory was performed.

### Testing Patterns

- Use pytest parameterization for disabled, expired, provenance mismatch, and
  content mismatch.
- Reuse the recording Governance client and runner. Assert the complete event
  prefix: marker admission, protected-ref check, resolve, blob read, and no
  `runner.run`.
- Assert complete deferred-state documents and persisted JSON, not only
  exception text or individual fields.
- Drive result/bundle formation with exact state documents and one-field
  lookalikes. Assert both Action Result and bundle outputs.
- Keep exact strings centralized. The new diagnostic should be one canonical
  publisher-boundary value (recommended literal:
  `publisher-governance-recheck-blocked`), distinct from the existing
  pre-capability diagnostics and from `create-conflict`.
- Use the existing `_closure`/qualified-simulation fixtures for final outcome
  assertions; no network or subprocess execution is needed.

### Recommendations

1. First make the adapter convert only a failed second Governance reread into a
   deferred `failed`/`no-side-effect` result with no observation, Receipt, or
   response identity and the exact new diagnostic. Do not catch unrelated
   marker, config, token, runner, or readback failures.
2. Persist that exact deferred terminal state in the publish CLI even though
   the command exits nonzero.
3. Extend result/bundle formation's post-marker no-side-effect allowlist to
   exactly the new diagnostic and the existing proven `create-conflict`.
   Near matches, wrong case, prefixes/suffixes, wrong outcome/disposition, and
   generic missing/malformed state must remain
   incomplete/possibly-mutated.
4. Teach finalization that this exact durable diagnostic is Governance-blocked:
   current Attempt `failure`, no uncertainty/possible mutation, and
   `next_action == "new-attempt"`. Do not alter ordinary failed bundle replay or
   generic post-marker uncertainty semantics.
5. Preserve all current edits. This turn must stop after this research artifact
   update; no production, test, docs/status, or plan file is to be edited.

### Acceptance Checklist

- [ ] Disabled second read: runner count zero and exact deferred
      failed/no-side-effect Governance diagnostic.
- [ ] Expired second read: same exact terminal state and runner count zero.
- [ ] Resolved-commit/blob provenance mismatch: same exact terminal state and
      runner count zero.
- [ ] Content mismatch: same exact terminal state and runner count zero.
- [ ] Unchanged Governance still reaches `runner.run` exactly once.
- [ ] Publish CLI persists the exact terminal state before returning nonzero.
- [ ] Result and bundle formation accept the exact new diagnostic.
- [ ] Existing exact `create-conflict` remains the only other admitted
      post-marker failed/no-side-effect proof.
- [ ] Case changes, prefixes, suffixes, similar Governance strings, and
      outcome/disposition substitutions are rejected as lookalikes.
- [ ] Missing, unreadable, malformed, truncated, substituted, or generic
      post-marker state remains incomplete/possibly-mutated with
      `terminal-state-missing-or-malformed-after-start`.
- [ ] The exact durable Governance block finalizes the current Attempt as
      `capability-blocked`, `failure`, non-uncertain, not possibly mutated, and
      `new-attempt`.
- [ ] Generic failed bundles retain replay semantics; generic post-marker
      termination retains incomplete/possibly-mutated and
      reobserve-and-replay semantics.
- [ ] Scoped tests, harness collection, build, and lint commands above pass.
- [ ] No production, tests, docs/status, plan, or unrelated tracked state is
      changed during this research-only turn.
<!-- END APPEND: workflow-delivery-v3-commit8-fifth-round-governance-terminal-state-2026-08-13 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-codeowners-tests-2026-08-14 -->
# Workflow Delivery v3 Commit 9 CODEOWNERS Test Research

## Bounded objective

Add tests only for GitHub CODEOWNERS final-match coverage. The delivered
workspace is authoritative. Production CODEOWNERS changes, activation,
acceptance, legacy workflow work, and all unrelated files are excluded.

## Required-reading findings

- The handoff identifies commit 9 as complete final-match coverage for every
  governed v3 surface and preserves arbitrary-ref Buddy eligibility.
- The LLD CODEOWNERS section requires `@hcoona` ownership for the v3 package,
  `eng/workflow-delivery/v3/**`, both descriptor basenames, the exact protected
  Governance path, HK configuration, and root Python workspace inputs. Existing
  workflow, action, script, and CODEOWNERS ownership remains authoritative.
- GitHub CODEOWNERS uses the last matching pattern; tests therefore must parse
  ordered rules and inspect the final match rather than accept any earlier
  `@hcoona` match.
- The protected Governance document is intentionally absent and must still be
  tested by its exact repository-relative path.

## Bounded target inventory

- Existing ownership source: `.github/CODEOWNERS`.
- Actual governed paths are discovered from tracked files for:
  `src/public/lib/three-workflow-delivery-v3/**`,
  `eng/workflow-delivery/v3/**`, both descriptor basenames, v3 workflows,
  v3 actions when present, and directly invoked v3 scripts.
- Explicit governed paths include `.github/CODEOWNERS`,
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`,
  `hk.pkl`, `src/private/lib/hk/**`, `eng/scripts/hk_exec.py`,
  `pyproject.toml`, and `uv.lock`.
- Current descriptors are the release-unit and quality files under
  `src/public/lib/hcoona-release-smoke-npm/`; discovery must also accept
  synthesized descriptors in new nested `src/**` locations.
- Existing Python tests use pytest functions, `Path`, concrete equality,
  parameterization where useful, and root-relative repository contracts.
- Existing live eligibility tests already use an arbitrary feature ref. The
  commit-9 test must prove ownership data is not an input to that runtime
  decision, without changing production.

## Acceptance checklist

1. Every actual governed path in every requested category resolves finally to
   `@hcoona`.
2. The exact absent protected Governance path resolves finally to `@hcoona`.
3. Newly synthesized release-unit and quality descriptor paths are discovered
   and checked.
4. Removing required coverage causes the contract evaluator to fail.
5. Appending a later non-`@hcoona` override causes the contract evaluator to
   fail.
6. Arbitrary-ref Buddy live eligibility remains accepted and CODEOWNERS is not
   consulted or coupled into runtime eligibility.
7. Tests are local-only and have no network or GitHub API dependency.
8. Changes remain within one or more pytest files under the v3 tests directory
   and append-only updates to the three existing `.testagent` files.

## Validation commands

- Focused: `uv run --python 3.13 pytest src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py`
- Boundary: `git diff --name-only` and `git diff --check`

## Current expected production state

The delivered `.github/CODEOWNERS` contains the pre-commit-9 workflow, action,
script, and self-ownership rules but not the new package, descriptor,
Governance, HK, or root-workspace patterns. The repository-wide positive
contract is therefore expected to fail until the separate production portion
of commit 9 lands; the synthetic parser/failure and runtime-decoupling tests
can still pass.
<!-- END APPEND: workflow-delivery-v3-commit9-codeowners-tests-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-independently-adjudicated-tp-fixes-2026-08-14 -->
# Commit 9 Independently Adjudicated TP Fix Research Addendum

## Boundary and current state

- **Strict implementation inventory**:
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py`,
  `src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`,
  `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`, and
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`.
  Production sources, workflows, `hk.pkl`, and `.github/CODEOWNERS` are
  read-only for these test fixes.
- The actual `.github/CODEOWNERS` now contains the nine commit-9 rules from the
  LLD after the existing broad workflow/action/script/self rules. The earlier
  expected-red statement is superseded: the bounded four-file suite is
  currently green (**142 passed**).
- Python 3.13 and pytest are authoritative. Tests use module-level repository
  paths, plain functions, `tmp_path`, `pytest.mark.parametrize`, exact
  structural equality, real local Git repositories, and parsed YAML. No
  network, GitHub API, or production mutation is needed.
- The requested `code-testing-extensions` skill is unavailable. Harness
  discovery was therefore taken from the actual `hk.pkl`
  `v3-control-pytest` step and package/root pytest configuration.

## Exact existing symbols and helpers

- CODEOWNERS contracts: `CodeOwnersRule`, `_parse_rules`,
  `_pattern_expression`, `_final_owners`, `_coverage_failures`,
  `_workspace_paths`, `_descriptor_paths`, `_governed_categories`, and
  `_complete_rules`.
- Real-HK fixture support: `HistoryChange`, `GOVERNED_PATHS`, `HK_CONFIG`,
  `HK_SUPPORT`, `HK_RANGE_HELPER`, `STEP_NAME`, `_run`, `_git`, `_write`,
  `_commit`, `_initialize_repository`, `_hk_executable`, `_step_from_plan`,
  `_helper_changed_paths`, `_helper_step_plan`, and `_apply_change`.
- Public CLI boundary: `three_workflow_delivery_v3.cli.main`,
  `_release_normalize_live_request_command`, and
  `normalize_buddy_live_intent`. The resulting `ReleaseIntent` preserves
  `workflow_ref == selected_ref`, `selected_ref`, `workflow_sha == target`,
  `target`, `event_kind == "workflow_dispatch"`, `channel == "buddy"`,
  `mode == "live"`, and `purpose == "live-release"`.
- Workflow boundary: `CALLER`, `_document`, `_step`, and `_run` in
  `test_buddy_workflows.py`; the actual request step is `Normalize fixed live
  request` in `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`.

## Adjudicated requirement checklist

### A. Actual CODEOWNERS as the sole ownership oracle

- [ ] Parse `.github/CODEOWNERS` once in ordered form and evaluate every
      current and synthetic path against those actual rules. Remove
      `COMPLETE_REQUIRED_RULES` and `_complete_rules`; tests must never append
      an expected production rule document to make a future path pass.
- [ ] Add synthetic future descriptors at shallow and nested `src/**`
      locations for both fixed basenames:
      `workflow-delivery.release-unit.yml` and
      `workflow-delivery.quality.yml`.
- [ ] Add a representative future v3 workflow, both approved action layouts
      (`.github/actions/workflow-delivery-v3-*/**` and
      `.github/actions/workflow-delivery-v3/**`), and a direct
      `eng/scripts/workflow_delivery_v3*.py` path. Keep actual tracked v3
      workflows, direct scripts, descriptors, package/control, HK, root Python
      inputs, CODEOWNERS, and the exact intentionally absent Governance path in
      the same governed inventory.
- [ ] Require the **exact** final tuple `("@hcoona",)` for every path; neither
      an earlier match nor `@hcoona` plus a co-owner is sufficient.
- [ ] Mutation tests must derive from the actual parsed rules. Remove each
      relevant actual broad rule in turn (workflow, action, direct-script,
      descriptor, and applicable v3-specific rules) and assert the exact
      affected future/current path and resulting final owners. Append later
      replacement-owner and co-owner overrides and assert final-match failure.
      This prevents a mutation from being masked by production rules copied
      into test constants.

### B. CODEOWNERS-to-real-HK cross-validation

- [ ] Build one shared current-plus-synthetic v3 surface inventory, then prove
      every CODEOWNERS-governed surface is selected by the **actual**
      `v3-control-pytest` plan from the actual `hk.pkl`. Do not implement a
      second glob matcher as an HK substitute.
- [ ] Exercise real Git add, modify, delete, rename-out, and rename-in history
      through `_initialize_repository`, `_apply_change`, `_commit`,
      `_helper_changed_paths`, and `_helper_step_plan`.
- [ ] Batch all representative surfaces once per history kind (five temporary
      repositories/runs), rather than one HK process per path. For renames,
      assert the helper reports both old and new names while the real HK plan
      includes the governed side with the exact expected `fileCount`.
- [ ] Include `.github/CODEOWNERS`, package/control/catalog/test paths,
      descriptors, current and future workflow/action/direct-script paths,
      Governance, `hk.pkl`, `src/private/lib/hk/**`,
      `eng/scripts/hk_exec.py`, `eng/scripts/workflow_delivery_v3_hk.py`,
      `pyproject.toml`, and `uv.lock`.
- [ ] Keep the execution copies of `hk.pkl` and
      `eng/scripts/workflow_delivery_v3_hk.py` usable when their governed
      history cases delete or rename those paths. Commit the requested history,
      then restore safe uncommitted execution copies before invoking the
      range helper/HK; the asserted range must remain the committed
      base-to-head range. Never execute a deleted helper or malformed/deleted
      active HK configuration.
- [ ] Retain the negative unrelated-product-source case and `--all`
      slice-validation contract.

### C. Arbitrary-ref Buddy contract at public boundaries

- [ ] Parameterize arbitrary valid branch and tag refs and call public
      `cli.main(["release", "normalize-live-request", ...])`, not the private
      eligibility validator. Assert return code `0`, canonical output, and the
      exact preserved Intent fields listed above.
- [ ] Prove the command performs no network operation and has no CODEOWNERS
      argument/gate. The test should fail if normalization narrows the selected
      branch/tag to a protected ref or rewrites canonical intent.
- [ ] Parse the actual Buddy caller workflow and require its normalization step
      to invoke `release normalize-live-request`, pass
      `--selected-ref "${GITHUB_REF}"`, and preserve `${GITHUB_REF}` in the
      emitted selected-ref output. Reject a hard-coded branch, CODEOWNERS gate,
      or network-based ownership lookup.

### D. Append-only and validation

- [ ] Preserve all pre-existing working-tree changes. Test implementation may
      edit only the four bounded pytest files above; test-agent state remains
      append-only.
- [ ] Keep all tests deterministic and local. Do not add package dependencies,
      a fake HK matcher, acceptance/activation work, legacy retirement, or
      production changes.
- [ ] Finish with `git diff --check`, inspect `git diff --name-only`, and verify
      this pre-existing research prefix is unchanged.

## Priority and testability

| Priority | Target | Testability | Current gap |
|---|---|---|---|
| High | `test_commit9_codeowners.py` | High | Uses expected-rule synthesis; future workflow/action/script coverage and broad-rule mutations are incomplete. |
| High | `test_hk_trigger.py` | High, local integration | Real HK exists, but cross-validation is not shared with CODEOWNERS and all surfaces are not covered across all five history kinds. |
| Medium | `test_cli.py` | High | No public `cli.main` branch/tag normalization contract. |
| Medium | `test_buddy_workflows.py` | High | No exact `GITHUB_REF` to `--selected-ref` request-step contract. |

The tests are integration/contract leaves around local files and subprocesses;
no mocks of in-scope types are required. `cli.main` is a mid-layer boundary,
but its normalization path is deterministic and can be exercised directly
without mocking network services.

## Commands

- **Scoped fix cycle**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Harness-equivalent discovery/check from repository root**:
  `python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
- **Build**: `uv build --package three-workflow-delivery-v3`
- **Lint/format**:
  `uv run ruff check src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit9_codeowners.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
  and the corresponding `ruff format --check` command.
- **Boundary**: `git diff --check` and `git diff --name-only`.
<!-- END APPEND: workflow-delivery-v3-commit9-independently-adjudicated-tp-fixes-2026-08-14 -->

<!-- BEGIN APPEND: workflow-delivery-v3-commit9-tp-final-scope-correction-2026-08-14 -->
## Commit 9 TP final scope correction

The earlier expected-red evidence is historical. The final implementation owns
only `test_commit9_codeowners.py`, `test_hk_trigger.py`, and append-only
commit-9 addenda in the three `.testagent` files. `test_cli.py`,
`test_buddy_workflows.py`, `.github/CODEOWNERS`, `hk.pkl`, and all production,
workflow, activation, acceptance, and legacy files remain unedited by this
implementation.

The approved synthetic inventory contains both descriptor basenames, one
future v3 workflow, and both future action layouts. Direct-script evidence uses
the two actual registered `eng/scripts/workflow_delivery_v3_*.py` paths. Actual
parsed CODEOWNERS rules, exact final-owner tuples, representative later
overrides, and the real HK plan are the only positive oracles.
<!-- END APPEND: workflow-delivery-v3-commit9-tp-final-scope-correction-2026-08-14 -->

# Commit-10 adversarial regression research

## Scope and authority

- Python/pytest repository conventions; the current working tree is authoritative.
- Test-only changes are permitted. Production, workflows, docs, status files, and existing expectations are out of scope.
- Bounded targets are the five existing commit-10 files:
  - `tests/contracts/test_commit10_acceptance_workflow.py`
  - `tests/adapters/test_commit10_acceptance_probes.py`
  - `tests/governance/test_commit10_acceptance_evidence.py`
  - `tests/governance/test_commit10_inspection.py`
  - `tests/governance/test_commit10_attestation.py`
- `find-untested-sources` polyglot analyzer was run exactly once with Python and `--include-tested`. Its static heuristic found 158 source files, 65 test files, 125 paired sources, 33 unpaired sources, and one orphan test. Conclusions here remain bounded to the five files above; static pairing is not line/branch coverage.

## Existing conventions

- pytest functions with explicit concrete assertions and parameterization.
- Injected fake transports/runners for network/process boundaries.
- YAML contract tests parse the actual workflow and inspect exact job/step structure.
- Canonical evidence tests build closed documents and assert admission or precise rejection.
- Reviewer tests use a recording GraphQL runner and assert exact cursor arguments.

## Bounded implementation inventory

- Acceptance workflow: confirmation input/env/validation, protected Environment placement, terminal embedded Python reconstruction.
- Acceptance probe adapter/CLI: process result facts, two-contender race semantics, lost-response runner boundary, authenticated package metadata observation.
- Governance evidence: action fact semantics and authentic incomplete/unknown scenario preservation.
- Reviewer inspection: outer deployment-review pagination and per-review nested EnvironmentConnection pagination.
- Protected attestation: no new behavior requested; preserve unchanged unless a requirement needs it.

## Requirement checklist

1. Exact confirmation literal `I_ACCEPT_DISPOSABLE_GITHUB_PACKAGES_PROBES`; no inert default.
2. Exactly one protected Environment job (`acceptance-review`); no Environment on probes/terminal; zero-SHA failure cannot cause terminal Environment review.
3. Executable terminal capture accepts successful dependencies/probes with missing review artifact ID and writes incomplete evidence.
4. Terminal reconstruction preserves per-scenario incomplete/unknown classifications instead of constructing every scenario as complete.
5. Evidence `action.executed`/`mutation-started` comes from explicit runner facts: pre-start false/false, lost and timeout true/true, exact false/false.
6. Differing race is complete only for one created plus one explicit conflict in either ordering; readback must equal the actual winning payload; all other outcomes rejected.
7. Lost-response runner crosses a deterministic forwarded/processed boundary before dropping response; immediate kill after `Popen` is forbidden; use an injected fake process/proxy seam.
8. Acceptance observation uses authenticated GitHub Packages REST package/version metadata for absence and owner/repository/version authority; npm E404 alone is insufficient; exact tarball claims require `repository.full_name == hcoona/three` and matching version metadata.
9. Reviewer traverses every review edge and independently paginates one review's nested environments; workflow has exactly one Environment-bearing job; timeout stays unknown.
10. Docs remain out of scope.

## Validation commands

- Ruff on changed commit-10 tests.
- `pytest --collect-only` for the package tests.
- Scoped pytest for changed commit-10 files.
- Full package/workspace pytest validation as appropriate without edits outside tests.
- Final `test-gap-analysis` and `assertion-quality`.

<!-- BEGIN APPEND: workflow-delivery-v3-acceptance-request-proof-research-2026-08-15 -->
# Test Generation Research

## Project Overview

- **Path**: `/workspace/three-workspaces/design-workflows`
- **Bounded package**:
  `src/public/lib/three-workflow-delivery-v3`
- **Language**: Python 3.13+ production and pytest tests, with a local npm
  subprocess boundary. The active shell reports Python 3.14.3, Node 24.14.0,
  and npm 11.9.0; `mise.toml` pins Node 24 but does not separately pin npm.
- **Framework**: Hatchling package, uv workspace.
- **Test Framework**: pytest 8+; Ruff for Python lint/format.
- **Extension note**: the parent invoked `code-testing-extensions`, but it was
  unavailable. No language example was read. The repository's own pytest
  conventions are sufficient.
- **Required prompt read**:
  `.agents/skills/code-testing-agent/unit-test-generation.prompt.md`.
- **Authority**: current tracked and untracked workspace content is
  authoritative. No remote Environment was configured, no workflow was
  dispatched, no external service was called, and git was not mutated.

## Dependency Graph

- **Leaf types / records**:
  `records/governance.py` (`GovernanceAcceptanceEvidence`,
  `GovernanceProbeFact`, reviewer/recovery/workflow/dependency records and
  admission helpers). These depend on canonical JSON primitives, not the CLI
  or adapter.
- **Mid-layer adapter**:
  `adapters/github_packages.py` fixed-coordinate records, request-proof
  validation, scenario classification, and suite aggregation. It depends on
  canonical hashing and npm-artifact inspection.
- **Top layer / process boundary**:
  `cli.py` acceptance tarball construction, npm execution, loopback mutation
  proxy, GitHub Packages observation transport, CLI commands, and output
  persistence. It depends on both the adapter and governance records.
- **External boundaries to fake or bind locally**: npm subprocess,
  loopback HTTP server, and the proxy's upstream HTTPS connection. Tests must
  never contact GitHub Packages; monkeypatch the upstream connection.

## Build & Test Commands

- **Build**:
  `uv build --package three-workflow-delivery-v3`
- **Test (scoped — fix cycles)**:
  `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
- **Scoped discovery/count**: the same four files with `--collect-only -q`;
  current authoritative collection is **490 tests**.
- **Test (harness-equivalent — discovery/full-package check)**:
  `python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
- **Full-package discovery/count**:
  `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider --collect-only -q src/public/lib/three-workflow-delivery-v3/tests`;
  current authoritative collection is **2775 tests**.
- **Lint**:
  `uv run --python 3.13 ruff check --no-cache src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/governance.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
  and the corresponding `ruff format --check` command.
- **Final local hygiene**: `git diff --check` and `git diff --name-only`.
  These inspect only; do not restore, stage, or commit files.

## Scope

- **Boundary**: only local acceptance regressions and fixtures, the acceptance
  CLI/proxy, GitHub Packages adapter, governance records/exports, and the
  minimum status/docs needed to describe closure.
- **Production targets**:
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py`
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/__init__.py`
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/governance.py`
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/__init__.py`
- **Acceptance tests / fixtures**:
  - `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`
  - `src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py`
  - `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py`
  - `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
  - Add a bounded fixture directory such as
    `tests/fixtures/acceptance/npm-publish-request/` only if needed for the
    captured manifest, tarball, raw request, and capture metadata.
- **Read-only acceptance inputs**:
  `.github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml` and
  `src/public/lib/hcoona-release-smoke-npm/package.json`.
- **Docs/status targets**:
  `docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md`,
  `docs/wiki/analyses/workflow-delivery/v3/hcoona-release-smoke-npm-lld.md`,
  `docs/wiki/log.md`, and an append-only newest closure in
  `.testagent/status.md`.
- **Excluded**: remote Environment/reviewer configuration, dispatch, external
  HTTP, package publication, activation, unrelated release/CI modules,
  dependency changes, git restoration/staging/commit, and sibling projects.
- **Representative existing tests**:
  `tests/adapters/test_commit10_acceptance_probes.py` and
  `tests/governance/test_commit10_acceptance_evidence.py`.

## Files to Test

### High Priority

| File | Classes/Functions | Testability | Estimated Coverage | Notes |
|---|---|---:|---|---|
| `cli.py` | `AcceptanceMutationProxy`, `_AcceptanceNpmRunner`, acceptance request validation/building, deadline handling | High | Partial | Existing proxy tests feed a hand-built `_adversarial_publish_body`; they do not prove compatibility with the raw request emitted by the active/repository-pinned npm client. |
| `adapters/github_packages.py` | `_valid_lost_response_proof`, `run_fixed_coordinate_acceptance_probe` and a new validated request/upstream proof record | High | Partial | `_expected_acceptance_request_digest` reconstructs a synthetic body instead of consuming proof of the bytes actually validated and forwarded. |
| `records/governance.py` | `_sha`, `admit_governance_acceptance_evidence` | High | Partial | Syntax admits forty zeroes; complete evidence needs a semantic non-zero SHA rule. |
| `test_commit10_acceptance_probes.py` | real npm loopback capture, strict proxy/proof/deadline/runner regressions | High | Partial | Strong existing local seams; missing real-client request fixture and missing/partial fact matrix. |

### Medium Priority

| File | Classes/Functions | Testability | Estimated Coverage | Notes |
|---|---|---:|---|---|
| `test_commit10_acceptance_evidence.py` | complete-evidence SHA admission | High | Partial | Add zero target/workflow SHA negatives while retaining zero only in rejected/incomplete workflow paths where contractually intended. |
| `test_commit10_acceptance_workflow.py` | terminal complete/incomplete evidence paths | High | Partial | Ensure workflow-generated complete evidence cannot carry the zero sentinel. |
| `test_cli.py` | CLI command/output contract | High | Partial | Add only CLI-level proof/export/output regressions not already covered by adapter acceptance tests. |
| adapter/record `__init__.py` exports | proof record/API exports | High | Partial | Update only if the proof object is intentionally public across the CLI/adapter boundary. |

### Low Priority / Skip

| File | Reason |
|---|---|
| Other `release/`, `ci/`, `repository/`, and platform modules | Outside the bounded acceptance request/proof correction. |
| Existing package source under `hcoona-release-smoke-npm` | Use a disposable copied/generated package for capture; do not stamp or modify the tracked smoke package. |
| Remote workflow/Environment state | Explicitly prohibited and unnecessary for local loopback tests. |

## Existing Tests & Coverage Classification

- `adapters/github_packages.py` ↔
  `tests/adapters/test_commit10_acceptance_probes.py`,
  `tests/adapters/test_github_packages.py`, and many broader contract tests:
  **partial for this request**. Existing tests validate a schema-faithful but
  synthetic JSON body and bind response facts; they do not capture npm's exact
  emitted request.
- `cli.py` ↔ `tests/test_cli.py`,
  `tests/adapters/test_commit10_acceptance_probes.py`, and acceptance workflow
  contracts: **partial for this request**.
- `records/governance.py` ↔
  `tests/governance/test_commit10_acceptance_evidence.py` and workflow
  contracts: **partial for this request**; complete zero-SHA rejection is
  absent.
- `adapters/__init__.py` ↔ adapter/release tests: **substantial generally**,
  but any new proof export is untested until added.
- `records/__init__.py` ↔ admission/release tests: **substantial generally**,
  but any new proof/export surface is untested until added.
- Static pairing is only a parse/identifier heuristic, not line or branch
  coverage.

## Existing Test Projects

- **Project file**:
  `src/public/lib/three-workflow-delivery-v3/pyproject.toml`
- **Target source project**: the same Hatchling package.
- **Test files**: pytest files under
  `src/public/lib/three-workflow-delivery-v3/tests`; bounded files are listed
  above. There is no separate test manifest.

## Static Pairing Result

`find-untested-sources` was run exactly once on the bounded package with
`--lang python --include-tested`: **38 source files, 39 test files, 36 paired,
2 unpaired, 0 orphan tests**. The only unpaired files are declaration-free
`ci/__init__.py` and `repository/__init__.py`, both outside this request.
Relevant target source files are paired. The analyzer attributes broad
identifier-overlap test sets, so the focused pairs above are the useful
implementation map.

## Testing Patterns

- Plain pytest functions, parameterized negative matrices, concrete exact
  documents, and precise diagnostics.
- Injected fake runners/transports and monkeypatched
  `http.client.HTTPSConnection`; loopback is permitted, external network is
  not.
- Canonical JSON and exact SHA-256/SHA-512 assertions; inspect tar members and
  witness content, not merely status codes.
- Contract tests parse the actual workflow and execute bounded embedded
  terminal-capture code locally.
- Keep dummy credentials loopback-scoped and assert neither dummy nor upstream
  token appears in retained proof, diagnostics, request fixture, or forwarded
  headers.

## Auditable Implementation Checklist

1. [ ] **Locally capture repository-pinned/active exact npm publish HTTP
   request against bounded loopback using a disposable package matching
   `@hcoona/hcoona-release-smoke-npm` and acceptance manifest/tarball, with
   dummy loopback-scoped token and schema-faithful fixtures/tests.** Record
   Node/npm versions and argv in non-secret capture metadata. Use the actual
   emitted method, escaped path, headers, and body bytes as the fixture oracle;
   do not call an external registry or modify/stamp the tracked package.
2. [ ] **Strict proxy validation of actual CouchDB payload and token
   replacement.** Validate package/version/tag closure, attachment bytes and
   hashes, acceptance witness, method/path/content framing, and the incoming
   dummy loopback authorization. Strip it, inject only the upstream token into
   the mocked upstream request, and prove neither token leaks into evidence.
3. [ ] **Remove synthetic raw request reconstruction in
   `adapters/github_packages.py` and use a validated request/upstream proof
   object.** Eliminate `_expected_acceptance_request_digest` as an authority.
   The proxy/request validator should form an immutable proof from actual raw
   bytes; adapter classification should admit that proof and bind request
   digest, tarball digest, coordinate/tag, upstream status, selected headers,
   response digest, and response-identity digest.
4. [ ] **Shared monotonic deadline.** Create one operation deadline and pass
   its remaining budget through pre-observation, npm process creation/waits,
   proxy barrier/read/upstream exchange, post-observation, and cleanup. No
   nested component may reset a full timeout.
5. [ ] **Malformed missing/partial runner proof facts.** Add a matrix for no
   facts, executed-only, started-only, wrong types, contradictory
   `started=True/executed=False`, pre-start failure, local post-spawn failure,
   proxy-observed timeout, and fully validated proof. Missing/partial facts
   must not default to `True/True` or produce complete evidence.
6. [ ] **Governance complete evidence rejects zero SHA.** Reject forty-zero
   `target-sha` and complete-evidence workflow SHA independently while
   preserving admissible failure/incomplete capture semantics required for
   rejected dispatches.
7. [ ] **Append newest `.testagent/status.md` closure.** Do not overwrite or
   rewrite history. Append exact files, regression names, commands, pass/fail
   counts, npm/Node capture versions, fixture provenance, external-call
   prohibition evidence, and remaining blockers.
8. [ ] **Add regressions and run requested/relevant narrow/full validations
   and report counts.** Run Ruff, scoped collection/test, full package
   collection/harness-equivalent test, build, and diff hygiene. Report
   collected/passed/failed/skipped counts exactly; current pre-change
   collection baselines are 490 scoped and 2775 full-package tests.

## Recommendations

1. Capture the real npm request first; freeze only non-secret, deterministic
   body/header facts and the exact disposable tarball/manifest needed to
   reproduce validation.
2. Introduce the immutable validated request/upstream proof seam before
   changing adapter classification. This removes duplicated body builders in
   `cli.py`, `github_packages.py`, and tests as competing authorities.
3. Thread one monotonic deadline object/value through CLI, proxy, and adapter,
   then add adversarial clock tests.
4. Close runner-fact and zero-SHA matrices, then update workflow/CLI contracts.
5. Update only the bounded docs and append the status closure after all local
   validations. Never configure or dispatch acceptance remotely.
<!-- END APPEND: workflow-delivery-v3-acceptance-request-proof-research-2026-08-15 -->

<!-- BEGIN APPEND: current-commit-10-regression-scope-2026-08-15T021318Z -->
# Current Commit-10 Regression Scope Research (2026-08-15T02:13:18Z)

## Project Overview

- **Path**: `/workspace/three-workspaces/design-workflows`
- **Boundary**: the uncommitted Workflow Delivery v3 commit-10 acceptance
  implementation only. Do not inventory or modify sibling packages.
- **Language/framework**: Python 3.13, Hatchling/UV workspace, pytest with
  `--import-mode=importlib`; YAML workflow contract tests use PyYAML.
- **Authority read**: `AGENTS.md`, the v3 handoff,
  `.agents/skills/code-testing-agent/unit-test-generation.prompt.md`, and the
  Python testing extension. The `code-testing-extensions` skill was not
  invocable, so its checked-in `extensions/python.md` was read directly.
- **Research-only constraints**: production and tests are inspection-only;
  `.testagent/plan.md` and `.testagent/status.md` remain untouched. This
  uniquely labeled section is appended without changing the prior research
  prefix.

## Bounded Target Inventory and Dependencies

### High priority

| Target | Public/internal surface | Dependency layer | Existing coverage |
|---|---|---|---|
| `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py` | `_AcceptanceNpmRunner._cleanup_processes`, `_AcceptanceNpmTransport.observe`, `_acceptance_subprocess_environment`, acceptance parser/deadline wiring | Top layer: subprocesses, temporary files, proxy, HTTP transport, monotonic clock | Partial |
| `.github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml` | two write-probe jobs, uploads, terminal fan-in | Top layer: Actions jobs and acceptance CLI | Partial |
| `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | process cleanup, credential/config boundary, shared deadlines | Direct fakes for processes, clock, transport; no external network | Partial |
| `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py` | workflow topology, action pins, gates, terminal evidence | Leaf contract parser over repository YAML | Partial |

### Medium priority

| Target | Reason |
|---|---|
| `.gitignore` | Exact fixture files under `tests/fixtures/acceptance/npm-publish-request/` are currently hidden by global `*.tgz` and `dist/` rules; only narrow path negations are admissible. |
| `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | Existing CLI deadline test pins the obsolete `7.0` default and should pair with parser-level suite-default regressions. |
| `src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py` | Existing repository contract for the narrow `.testagent/**` markdown-only exclusion; appropriate home for any explicit append-only/typos convention check. |
| `.typos.toml` and `src/public/lib/Hjg.Pngcs/Chunks/PngChunkZTXT.cs` | Read-only historical convention for requirement 8. |

### Low priority / skip

- `adapters/github_packages.py`, governance records, and governance inspection
  are already paired and are outside these newly requested cleanup,
  credential, workflow-gate, deadline, and repository-contract regressions
  unless a signature must be imported by a focused test.
- Do not change docs, remote Environment/reviewer state, or dispatch workflows.
- Do not restore ignored fixtures by broad `dist/**`, `*.tgz`, or fixture-tree
  negations.

### Dependency graph

- **Leaves**: workflow YAML contract parsing; `.gitignore`, `hk.pkl`, and
  `.typos.toml` text contracts; fake process/clock/transport objects.
- **Mid layer**: `_acceptance_subprocess_environment`,
  `_AcceptanceNpmTransport`, `_AcceptanceNpmRunner`.
- **Top layer**: acceptance CLI suite orchestration and the two workflow probe
  jobs followed by terminal evidence fan-in.
- Mock/fake subprocess, filesystem mode checks, monotonic time, and HTTP
  transport. Never call GitHub Packages or publish externally.

## Requirement Checklist

1. **Ignored acceptance fixtures and narrow negations**: `capture.json` is
   visible, but `package.tgz` is ignored by `.gitignore:538` (`*.tgz`) and
   `package/dist/acceptance-witness.json` plus `package/dist/index.js` are
   ignored by the global `dist/` convention. Add contract coverage for exact
   file-level negations only and prove unrelated `.tgz`/`dist` artifacts remain
   ignored.
2. **Cleanup ordering and immutable timeout state**:
   `_cleanup_processes` currently kills and reaps one process at a time and
   returns on the first expired reap. The regression must require a first pass
   that signals/kills every started process, then a bounded reap pass using one
   shared absolute deadline. Once timeout is classified, no contender result,
   winner, proof, or mutation state may be changed by late process completion.
   Extend the all-started/partial-startup fake-process convention with multiple
   stubborn processes and ordered call assertions.
3. **Authenticated npm readback credential boundary**:
   `_AcceptanceNpmTransport.observe` currently invokes `npm view` with the
   tokenless shared config created at CLI lines 458-466. Require a fresh 0600
   temporary npm config containing only the dedicated token plus minimum
   GitHub-registry settings, used only for `npm view`/readback, absent from argv,
   logs, retained output, and inherited environment, and deleted on success and
   failure. Keep `_AcceptanceNpmRunner.run_scenario`'s loopback proxy config
   separate; it may contain only the dummy proxy token and must never contain
   the dedicated token.
4. **Workflow classification gates and terminal evidence**:
   each probe needs record production, an `always()` immutable upload, then an
   explicit classification gate after both record and upload. A failing first
   probe must prevent the second mutation job through `needs`; the terminal job
   must retain its exact `always() && github.run_attempt == 1` guard, consume
   all dependency results/record/artifact outputs, form evidence even for
   failed/skipped jobs, and always attempt its evidence upload.
5. **Suite deadlines**: parser default is currently `7.0`. Regressions must pin
   omitted `--timeout-seconds` to exactly 120 seconds for
   `absent-create-readback` and at least 300 seconds for the four-scenario
   `exact-and-conflict` suite. An explicit CLI timeout remains authoritative.
   Both suite paths must form and preserve one shared internal absolute
   deadline rather than resetting a budget per scenario/proxy/wait/reap.
6. **Exact Node/npm toolchain and credential boundary**:
   both package-writing jobs currently have no setup-node step. Require a
   full-SHA-pinned `actions/setup-node` step selecting exact Node `24.14.0`,
   explicit installation and verification of npm `11.9.0`, and checks of both
   exact versions before mutation. Setup/checkouts remain credential-free
   (`persist-credentials: false`, no registry token on setup); the dedicated
   token enters only the acceptance process boundary described in item 3.
7. **`.testagent` append-only contracts**: repository convention already
   excludes `.testagent/**` only from the two mutating/checking Markdown steps:
   `hk.pkl:68,72,82`, enforced by
   `test_testagent_markdown_exclusion_is_local_to_two_markdown_steps`.
   Add any requested contract without broadening that exclusion; it must check
   the intended append-only/zero-deletion rule rather than rewriting historical
   artifacts.
8. **Historical two-letter identifier and narrow typos exception**: preserve
   the exact legacy declaration at
   `src/public/lib/Hjg.Pngcs/Chunks/PngChunkZTXT.cs:46`. `.typos.toml` already
   names that exact legacy file in `extend-exclude`; preserve the file-specific
   exception and reject a wildcard/general identifier exemption. The same legacy
   identifier appears in nearby Pngcs chunk files, also represented by exact
   file entries rather than a broad generated-code exception.

## Source-to-Test Pairs and Coverage Classification

- `cli.py` ↔ `tests/adapters/test_commit10_acceptance_probes.py` and
  `tests/test_cli.py`: **partial**. Shared-deadline and single-process cleanup
  tests exist, but signal-all-before-reap, immutable post-timeout state,
  dedicated ephemeral readback config, and suite-specific defaults do not.
- Acceptance workflow ↔
  `tests/contracts/test_commit10_acceptance_workflow.py`: **partial**. It
  already pins full action SHAs, first-attempt guards, sequential jobs, output
  bindings, and terminal fan-in, but not setup-node/npm versions or post-upload
  classification gates.
- `.gitignore` ↔ new focused repository contract in the existing commit-10
  contract file (or `test_hk_trigger.py` if kept as root hygiene):
  **untested for these fixtures**.
- `hk.pkl` ↔ `tests/test_hk_trigger.py`: **substantial for the existing narrow
  Markdown exclusion**, partial for an explicit append-only history contract.
- `.typos.toml`/historical Pngcs line ↔ root hygiene contract:
  **represented by exact current configuration, but no focused regression was
  found**.

Static pairing was run once on the bounded package with
`--lang python --include-tested`: 38 source files, 39 tests, 36 paired, 2
unpaired declaration-free `__init__.py` files, and 0 orphan tests. Relevant
`cli.py`, `adapters/github_packages.py`, governance inspection, and governance
records are paired. This is a static identifier/import heuristic, not line or
branch coverage.

## Existing Test Conventions

- Plain pytest functions; `pytest.mark.parametrize` for state matrices; exact
  dict/list/string assertions rather than truthiness.
- Injected fakes and `monkeypatch` for Popen, clocks, subprocess, and transport.
- Workflow tests parse the actual YAML and execute bounded embedded Python only
  when needed; all action revisions are checked as 40-character SHAs.
- Security assertions inspect argv, environment, file mode/lifetime, retained
  bytes, and logs independently. Dummy proxy and dedicated upstream
  credentials must be distinguishable sentinel values.
- Representative tests:
  `tests/adapters/test_commit10_acceptance_probes.py` and
  `tests/contracts/test_commit10_acceptance_workflow.py`.

## Exact Build and Test Commands

- **Build (narrow package)**:
  `uv build --package three-workflow-delivery-v3`
- **Test (scoped fix cycles)**:
  `PYTHONDONTWRITEBYTECODE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -p no:cacheprovider -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
- **Scoped discovery**: use the same command with `--collect-only -q`.
- **Test (harness-equivalent full package from repository root)**:
  `python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
- **Harness discovery check**:
  `uv run --python 3.13 pytest --collect-only -q`
- **Lint/format check (narrow)**:
  `uv run ruff check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit10_acceptance_workflow.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
  and the corresponding `uv run ruff format --check ...`.
- **Full repository build requested by unit-test-generation guidance**:
  `uv build --package three-workflow-delivery-v3` for this bounded Python
  package; do not expand to unrelated C#/Node sibling builds.
- **Final hygiene**:
  `git diff --check` and
  `git diff --numstat HEAD -- .testagent/research.md` (research must report
  zero deletions).

## Recommendations

1. Add repository-contract regressions first for fixture visibility,
   `.testagent` locality, and the historical identifier exception.
2. Add adversarial cleanup and credential-lifetime tests with independent
   sentinels, then suite-default/shared-deadline parser tests.
3. Add workflow topology tests for exact tool versions, full SHA pinning,
   record/upload/gate order, first-failure skip, and unconditional terminal
   evidence upload.
4. Do not dispatch, configure a remote Environment, expose credentials, alter
   status/plan artifacts, or modify production during test generation.
<!-- END APPEND: current-commit-10-regression-scope-2026-08-15T021318Z -->


## 2026-08-15 Commit 11 Calibration Research Addendum

- Calibrated the legacy Buddy retirement contracts away from blanket `buddy`
  token rejection and toward explicit inventories. Generic Buddy text, v2/history
  documentation, and future v3 Buddy concepts are no longer rejected by this
  contract unless they keep an exact retired v1 entry filename or exact retired
  node/path inventory.
- Exact retired v1 test inventory is the ten Buddy-only functions currently in
  `tests/test_workflow_release_control.py`; the mixed
  `test_acceptance_gate_pins_r41_release_completion_and_buddy_regressions`
  remains separate and is not part of `RETIRED_BUDDY_TEST_NAMES`.
- Acceptance-matrix calibration now targets exact retired row IDs
  `buddy-to-official-promotion` and
  `buddy-force-rejected-after-official-freeze`, exact removed live gate
  `buddy-github-packages-live-publication`, exact retired matrix/gate node IDs,
  and exact active evidence paths `.github/workflows/buddy.yml` /
  `.github/workflows/release-buddy.yml`.
- Added script/config/doc contract inventory research for
  `release_orchestrate_lint_caller_completeness.sh`,
  `workflow_release_control.py`, `workflow_release_acceptance_gate.py`,
  `.github/actionlint.yaml`, `src/public/lib/hexo-renderer-asciidoc/README.md`,
  and `workflow-release-workflow-executor-boundaries.md`.
<!-- BEGIN APPEND: commit11-calibration-mixed-node-correction-2026-08-15 -->
## Commit 11 Calibration Mixed-Node Correction

The mixed R41 acceptance-pin function is an exact retired mixed Buddy node,
not preserved Official/CI evidence. The calibrated contract therefore keeps
the ten Buddy-only names in `RETIRED_BUDDY_TEST_NAMES` and separately requires
`test_acceptance_gate_pins_r41_release_completion_and_buddy_regressions` to be
absent through `RETIRED_MIXED_BUDDY_TEST_NAMES`. Matrix node-ID retirement is
evaluated across all rows; exact legacy workflow paths remain limited to
active-row evidence checks.
<!-- END APPEND: commit11-calibration-mixed-node-correction-2026-08-15 -->
<!-- BEGIN APPEND: current-2026-08-17-bounded-regression-research -->

# Test Generation Research

## Acceptance / Requirement Checklist

### 1. GitHub Contents JSON Base64
- [ ] Accept GitHub Contents API objects whose `content` is Base64 wrapped with
  `\r`, `\n`, or CRLF.
- [ ] Remove **only** CR and LF before calling strict Base64 validation/decoding.
- [ ] Continue rejecting invalid alphabet characters, non-CR/LF whitespace, and
  malformed or excess padding.
- [ ] Cover a GitHub-shaped successful response (`sha`, `encoding: "base64"`,
  wrapped `content`) and malformed alphabet, padding, and whitespace responses.
- [ ] Preserve the existing `GitHubRestError` boundary and malformed
  JSON/protocol handling.

### 2. Artifact archive redirect
- [ ] Permit only the artifact archive download flow to follow exactly one
  authenticated GitHub API `302` to a temporary off-origin HTTPS blob URL.
- [ ] Prove the initial `api.github.com` request is authenticated.
- [ ] Prove the off-origin request has no `Authorization` header, contains no
  GitHub token/credential in any header, and is issued at most once.
- [ ] Keep generic GitHub API redirects fail-closed; the exception must not
  become a general redirect policy.
- [ ] Reject non-HTTPS/unsafe redirect targets before a follow-up request.
- [ ] Reject a second redirect, an extra hop, and non-`302` off-origin redirect
  behavior as appropriate.
- [ ] Preserve missing-location, cycle/limit, timeout propagation, non-redirect
  HTTP status, network error, malformed ZIP, and one-file archive checks.
- [ ] Preserve all existing size/time/error checks. The current bounded client
  has timeout and archive-cardinality/error checks, but no explicit response
  byte cap was found in `platform/github.py`; do not invent or weaken unrelated
  limits during this repair.

### 3. Live history caller/callee topology
- [ ] The live history command must query
  `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`.
- [ ] It must not query the reusable callee
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`.
- [ ] Add one regression that ties together: caller workflow, reusable-callee
  invocation, callee history command, and the caller path supplied to that
  command.
- [ ] Retain the exact reusable-attempt job set/DAG and artifact/job topology
  checks already enforced by the contract and history-admission tests.
- [ ] Do not broaden run, artifact, job, attempt, or admission semantics.

### Explicit exclusions and change discipline
- [ ] Do not touch live adapter context.
- [ ] Do not touch Node version selection.
- [ ] Do not touch the package owner endpoint.
- [ ] Do not change artifact raw-mode, name, or ID semantics.
- [ ] Do not change concurrency.
- [ ] Any later production change must be minimal and directly required by
  these regressions.

## Project Overview
- **Workspace**: `/workspace/three-workspaces/design-workflows`
- **Bounded package**:
  `src/public/lib/three-workflow-delivery-v3`
- **Language**: Python 3.13+; two YAML workflow surfaces are contract-tested
  from Python
- **Packaging**: uv workspace, Hatchling build backend
- **Test framework**: pytest 8.3+
- **Relevant libraries**: stdlib `base64`, `urllib`, `zipfile`; PyYAML in
  workflow contract tests

## Scope
- **Boundary**: only the three adjudicated findings above in the delivery-v3
  package and its two Buddy workflow files.
- **Direct Python target**:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/platform/github.py`
- **Direct workflow targets**:
  `.github/workflows/workflow-delivery-v3-buddy-smoke.yml` and
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`
- **Direct regression files**:
  `src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py` and
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Representative existing tests**: the two direct regression files above.

## Bounded Target Inventory and Exact Pairing

| Source surface | Relevant symbols/section | Exact test pair | Existing coverage |
|---|---|---|---|
| `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/platform/github.py` | `GitHubRestClient._open`, `_validate_api_url`, `_request`, `download_artifact`, `read_blob`, `list_runs` | `src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py` | **Partial**: pagination, authentication, API-origin rejection, HTTP errors, and malformed governance content exist; wrapped Base64 and artifact redirect/download behavior do not. |
| `.github/workflows/workflow-delivery-v3-buddy-smoke.yml` | `run-live-attempt` reusable-workflow edge | `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | **Substantial overall, partial for finding**: caller DAG and callee `uses` edge are exact. |
| `.github/workflows/workflow-delivery-v3-live-attempt.yml` | `admit` → `Discover exhaustive retained execution history` | `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | **Partial for finding**: job/artifact contracts exist, but no assertion covers the `--workflow-path` value. |

### Relevant guard pairs, not direct edit targets
- `src/.../release/live.py` ↔
  `tests/release/test_commit8_history_admission.py`: substantial coverage of
  exhaustive runs, artifact downloads, exact attempt jobs, finalizer/publisher
  selection, and fail-closed admission. Preserve this behavior.
- `src/.../release/identity.py` ↔
  `tests/release/test_commit8_contracts.py`: the existing
  `BUDDY_LIVE_WORKFLOW_PATH` already equals the required caller path.
- `src/.../cli.py` ↔ `tests/test_cli.py`: the CLI parser requires
  `--workflow-path`, and `_release_discover_history_command` forwards it
  unchanged into `GitHubRestClient`. No CLI semantic change is indicated.

## Static Source-to-Test Analysis
- Executed exactly once against the narrow package root:
  `python .agents/skills/find-untested-sources/scripts/find_untested_sources.py src/public/lib/three-workflow-delivery-v3 --lang python --include-tested`
- Package result: 38 Python source files, 40 test files, 36 statically paired
  source files, 2 unpaired source files, and 0 orphan tests.
- Relevant analyzer result: `platform/github.py` is classified **tested** with
  31 declarations and includes `tests/platform/test_github.py` as a covering
  test. Relevant collaborators `cli.py`, `release/identity.py`, and
  `release/live.py` are also classified tested.
- Analyzer-relative paths above are prefixed with
  `src/public/lib/three-workflow-delivery-v3/` elsewhere in this document.
- **Caveat**: this is a static import/identifier-overlap heuristic, not line or
  branch coverage. It can over-pair files through shared identifiers and cannot
  demonstrate the missing redirect or Base64 branches.

## Current Behavior and Test Gaps

### Contents Base64
- `read_blob` currently passes the JSON `content` directly to
  `base64.b64decode(..., validate=True)`, so API-permitted CR/LF wrapping is
  rejected.
- The existing parameterized failure test covers invalid alphabet (`"***"`),
  malformed JSON, and a wrong `encoding`, but has no successful REST-backed
  `read_blob` case and no padding/non-CR-LF-whitespace cases.
- A focused regression can build valid bytes, Base64-encode them, wrap the
  encoded text with CRLF in a GitHub-shaped JSON object, and assert exact
  `GovernanceBlob` OID/content. Parameterized malformed inputs should establish
  that spaces/tabs are not normalized and padding remains strict.

### Artifact redirect
- `_open` installs a no-auto-redirect handler and manually processes redirect
  `HTTPError`s. It currently validates every hop as `api.github.com` and copies
  all request headers to the next hop.
- Therefore a real off-origin artifact `302` currently fails, while merely
  relaxing URL validation would leak the authenticated header.
- `download_artifact` uses the same generic `_request` path as JSON and
  governance calls. The exception must be scoped to the exact artifact archive
  request, not `_request` globally.
- To exercise real redirect logic, tests must monkeypatch
  `urllib.request.build_opener`; passing the constructor's byte-returning
  `opener` bypasses `_open`.
- Reuse the existing local fake pattern: an opener object records URL, headers,
  and timeout; raises `urllib.error.HTTPError(302)` with a `Message` containing
  `Location`; then returns a context-managed byte response. Assert two calls for
  success and one call for rejected targets/generic redirects.
- Existing behavior to retain includes positive timeout validation and timeout
  forwarding, non-redirect HTTP status preservation on `GitHubRestError`,
  `OSError` translation, missing location/cycle/limit rejection, and
  `download_artifact` malformed/multi-file ZIP handling.

### Caller/callee history query
- The caller correctly invokes
  `./.github/workflows/workflow-delivery-v3-live-attempt.yml`.
- The callee currently passes its own path to
  `discover-execution-history`; this is the adjudicated one-line topology bug.
- `release/identity.py` already binds Buddy intents to the caller path, providing
  an existing invariant for the required value.
- Existing workflow tests already enforce the exact five caller jobs, twelve
  callee jobs, least privileges, artifact ID downloads, raw upload settings,
  retention, and error propagation. The new assertion should compose the
  existing `_document`, `_step`, and `_run` helpers rather than duplicate YAML
  parsing or alter history admission.

## Dependency Graph and Collaborator Seams
- **Leaf/directly testable**:
  - `GitHubRestClient` through an injected callback for ordinary responses and
    a monkeypatched stdlib opener for redirect behavior.
  - Workflow YAML through PyYAML and path-based contract helpers.
- **Mid-layer, preserve only**:
  - `cli._release_discover_history_command`, which forwards the workflow path.
  - `release.identity.BUDDY_LIVE_WORKFLOW_PATH`, already the caller path.
- **Top-layer, preserve only**:
  - `release.live.discover_execution_history`, which consumes
    `GitHubActionsHistoryClient` and validates retained run/artifact/exact-job
    topology.
- **External seams**: `urllib.request.build_opener`,
  `urllib.error.HTTPError`, response context/read behavior, `base64`, `zipfile`,
  and `yaml.safe_load`.

## Existing Pytest Conventions and Helpers to Reuse
- `tests/platform/test_github.py` uses nested fake openers, request inspection,
  `pytest.MonkeyPatch`, `pytest.mark.parametrize`, named `pytest.param` cases,
  and `pytest.raises(..., match=...)`; tests make no real network calls.
- Its existing `ErrorOpener` pattern uses `email.message.Message` and
  `io.BytesIO` to construct realistic `HTTPError` responses.
- `tests/contracts/test_buddy_workflows.py` provides `CALLER`, `CALLEE`,
  `_document`, `_steps`, `_step`, `_run`, and `EXPECTED_JOBS`; use these for the
  caller/callee regression.
- The only package `conftest.py` is release-specific and supplies no relevant
  platform/workflow fixture. Keep new helpers local to the direct test module.
- Assertions favor exact URLs, call sequences, job sets, headers, bytes, and
  error messages over broad truthiness checks.
- `tests/adapters/test_github_packages.py` is a useful convention reference for
  recording two redirect calls and proving the second call has no
  `Authorization`; do not couple the GitHub REST client to that adapter.

## Files to Test

### High Priority
| File | Testability | Coverage classification | Reason |
|---|---|---|---|
| `src/.../platform/github.py` | High | Partial | Both Base64 and credential-safe redirect gaps are isolated behind injectable stdlib seams. |
| `.github/workflows/workflow-delivery-v3-live-attempt.yml` | High | Partial for finding | Contains the wrong history workflow argument. |
| `.github/workflows/workflow-delivery-v3-buddy-smoke.yml` | High | Substantial | Supplies the caller/callee side of the topology regression. |

### Low Priority / Preserve
| File | Reason |
|---|---|
| `src/.../release/live.py` | Existing admission tests are substantial; changing it would broaden semantics. |
| `src/.../cli.py` | Correctly forwards the supplied workflow path. |
| `src/.../release/identity.py` | Already declares the required caller path. |
| Live adapter context, Node/version, package-owner endpoint, artifact raw/name/ID, concurrency surfaces | Explicitly excluded. |

## Build and Test Commands
Run from the repository root.

- **Build (bounded package)**:
  `uv build --package three-workflow-delivery-v3`
- **Test (scoped fix cycles)**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Test (package validation / repository HK equivalent)**:
  `python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
- **Test (workspace-level Python validation from root configuration)**:
  `uv run --python 3.13 pytest -q`
- **Test (harness-equivalent discovery check)**:
  `uv run --python 3.13 pytest --collect-only -q`
- **Lint (bounded Python files)**:
  `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/platform/github.py src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Format check (bounded Python files)**:
  `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/platform/github.py src/public/lib/three-workflow-delivery-v3/tests/platform/test_github.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`

## Recommendations
1. Add the wrapped/malformed Base64 matrix in `test_github.py`.
2. Add artifact-only redirect success and rejection tests in the same file,
   explicitly asserting call count, URL, timeout, and absent credentials.
3. Add one composed caller/callee history-path test in
   `test_buddy_workflows.py`.
4. Keep `release/live.py` admission tests green without changing their
   semantics, then run the package and root discovery commands.
5. Treat the absence of an explicit response-byte cap in the current client as
   a research note, not permission to add unrelated transport behavior.

<!-- END APPEND: current-2026-08-17-bounded-regression-research -->

<!-- BEGIN APPEND: 2026-08-18-v3-artifact-transport-research -->

## v3 artifact transport repair

### Bounded target inventory

- Production target:
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`.
- Canonical YAML contract target:
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`.
- Existing preservation coverage:
  `tests/platform/test_github.py` rejects multi-file history archives, and
  `tests/release/test_commit8_history_admission.py` skips unrelated multi-file
  artifacts while retaining fail-closed validation for recognized records.

### Existing conventions

- Parse workflows with `yaml.safe_load` and use `_document`, `_steps`, `_step`,
  and `_run` for exact scenario assertions.
- Model upload-artifact v7 raw physical naming with
  `PurePosixPath(path).name`, matching the Official simulation contract helper.
- Assert complete action `with` mappings where delimiter, decompression, and
  merged download layout are all contract-significant.
- Preserve ID-only artifact transport, attempt-specific names, exact upload
  ID/digest propagation, 45-day retention, and all unrelated workflow
  topology and permissions.

### Acceptance checklist

- [x] Reviewer directory uses archived upload and decompressed download while
      retaining upload digest plus snapshot and summary payload digest binding.
- [x] Authorization and mutation-marker raw files use the exact configured
      attempt-specific artifact basename at upload and every local read.
- [x] Already-correct raw uploads remain basename-equal and unchanged.
- [x] Publisher closure and finalization authority downloads use comma-delimited
      IDs, `merge-multiple: true`, `skip-decompress: true`, and flat
      `.wdv3/input`.
- [x] Multi-file history admission behavior remains unchanged and fail-closed.

<!-- END APPEND: 2026-08-18-v3-artifact-transport-research -->

<!-- BEGIN APPEND: 2026-08-18-wdv3-artifact-transport-regression-research -->

# Bounded Workflow Delivery v3 Artifact-Transport Regression Research

This append covers the request timestamped 2026-08-18T03:56:07Z. Earlier
content is preserved as history and is not completion evidence for the
checklist below.

## Project Overview

- **Path**: `/workspace/three-workspaces/design-workflows`
- **Package**:
  `src/public/lib/three-workflow-delivery-v3`
- **Language/runtime**: Python 3.13 via the root uv workspace
- **Test framework**: pytest 8; root configuration uses
  `--import-mode=importlib`
- **Workflow contract parser**: PyYAML
- **Linters relevant to this scope**: Ruff and the repository
  `eng/scripts/hk_actionlint.py` wrapper
- `code-testing-extensions` is unavailable as stated by the requester;
  conventions below come only from the two representative tests named here.

## Strict Scope Boundary and Target Inventory

| Role | Exact path/symbol | Classification |
|---|---|---|
| Production target | `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py::_load_mutation_marker` | High priority; directly defective |
| Canonical test module | `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | The module imports `cli` but has no direct loader test |
| Contract target | `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | High priority; current artifact assertions are partial for the new checklist |
| Primary workflow fixture | `.github/workflows/workflow-delivery-v3-live-attempt.yml` | Direct source for all artifact-transport scenarios |
| Referenced caller fixture | `.github/workflows/workflow-delivery-v3-buddy-smoke.yml` | Context-only topology fixture; no artifact change indicated |
| Referenced existence fixture | `.github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml` | Existence-only assertion in the contract module; no content change indicated |

No sibling package, other v3 defect, governance JSON behavior, or unrelated
workflow is part of this research.

## Source-to-Test and Fixture Pairing

- `cli.py::_load_mutation_marker` ↔ `tests/test_cli.py`.
  The only current `_load_mutation_marker` reference in that test module is a
  monkeypatched passthrough in
  `test_publish_cli_persists_governance_terminal_state_before_nonzero`; it does
  not execute or validate the loader. The symbol is therefore **untested
  directly**, while its containing module is broadly paired.
- `.github/workflows/workflow-delivery-v3-live-attempt.yml` ↔
  `tests/contracts/test_buddy_workflows.py`, especially:
  - `test_reviewer_archive_is_decompressed_with_transport_and_payload_bindings`
  - `test_authorization_raw_upload_materializes_exact_attempt_basename`
  - `test_mutation_marker_raw_upload_and_consumers_use_attempt_basename`
  - `test_authority_record_multidownload_is_comma_delimited_flat_merged_raw`
  - `test_user_item11_publisher_preflight_and_start_marker_are_separate`
  These are **partial** for the requested stronger single-file, ordering, and
  complete-chain evidence.
- `.github/workflows/workflow-delivery-v3-buddy-smoke.yml` ↔ topology/history
  scenarios in the same contract module. It is not implicated in the loader
  defect or raw-upload data path.
- `.github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml` ↔ the
  existence assertion in
  `test_buddy_workflow_files_are_the_disabled_commit8_pair_only`; it is
  context-only for this task.

The required `find-untested-sources` attempt was restricted to a temporary
symlink-only tree containing only `cli.py`, `test_cli.py`, and
`test_buddy_workflows.py`. The analyzer did not follow that layout and returned
zero parsed source/test files, so it was inconclusive. The pairings above come
from bounded direct imports and workflow path constants. They are static
pairings, not line or branch coverage evidence.

## Relevant Dependencies and Behavior

### Loader dependency graph

- **Leaf helper**: `cli._normalized_digest` removes one optional `sha256:`
  prefix, requires exactly 64 characters from `[0-9a-f]`, and returns the
  canonical prefixed form.
- **Target loader**: `_load_mutation_marker` reads JSON from `Path`, checks a
  positive artifact ID, validates schema and canonical marker digest, and binds
  the marker to the publication attempt, first materialized action, lock group,
  and preflight digest.
- **Returned type**:
  `adapters.github_packages.MutationMayHaveStartedMarker`.
- The upload action's transport digest and the marker document's canonical
  digest are distinct bindings. The narrow fix should validate/normalize the
  transport value without replacing the existing marker-body validation.

Current code uses:

```python
if artifact_id <= 0 or not artifact_digest.startswith("sha256:"):
```

Consequences confirmed by a bounded runtime probe:

- native bare `"a" * 64` → rejected with
  `mutation-start marker transport is malformed`;
- malformed `"sha256:not-a-digest"` → accepted when the marker body is valid.

The narrow production repair is to reuse `_normalized_digest` for transport
validation while preserving the positive-ID and all marker substitution
checks. No broader transport redesign is indicated.

### Workflow data-flow graph

- **Reviewer chain**:
  `Materialize immutable publication and reviewer payload` →
  `Materialize exact publication basenames` →
  archived `Upload reviewer artifact` →
  `Bind reviewer artifact transport to exact payloads` →
  `materialize-publication.outputs.reviewer-formatter-input-base64` →
  approval job output/environment →
  authorization formatter and approval-finalizer consumers.
- **Authorization chain**:
  authorization formatter writes `.wdv3/authorization.json` →
  SHA-256-derived attempt basename →
  move to `.wdv3/${name}` →
  Base64 from that same renamed file →
  approval step outputs →
  approval job outputs →
  approval-finalizer decode to the same basename →
  raw upload →
  approval-finalizer name/ID/digest outputs →
  publisher and release-finalizer consumers under `.wdv3/input`.
- **Mutation-marker chain**:
  marker producer writes the attempt-specific literal path →
  raw upload of that path →
  upload step ID/digest outputs →
  publish command and capability-bundle consumer.
- For `archive: false`, a contract helper must establish one literal,
  non-directory selector: one nonempty path entry, no trailing slash, and no
  glob metacharacters, with the physical basename equal to the artifact name.
  Producer-order assertions provide the complementary evidence that the
  literal path is materialized as a file before upload.

## Current Worktree Observations

- The bounded status at research time was:
  - modified:
    `.github/workflows/workflow-delivery-v3-live-attempt.yml`;
  - modified:
    `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`;
  - unmodified:
    `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`;
  - unmodified:
    `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`;
  - already modified before this append: `.testagent/research.md`.
- The authoritative workflow changes already archive/decompress the reviewer
  directory, rename and Base64-encode Authorization from its attempt basename,
  use an attempt basename for the mutation marker, and use comma-delimited,
  merged raw authority downloads.
- Current contract coverage is green but not yet sufficient:
  `_raw_artifact_name` checks `archive: false` and basename equality only. It
  does not reject multiline paths, globs, a trailing-slash directory, or prove
  reviewer/Authorization producer ordering and every Base64/output hop.
- Bounded current results:
  - selected artifact contract scenarios: **6 passed, 27 deselected**;
  - Ruff check on the three bounded Python files: **passed**;
  - Ruff format check on the three bounded Python files: **passed**;
  - actionlint on the live-attempt workflow: **passed**;
  - bounded `git diff --check`: **passed**.

## Representative Existing Pytest Conventions

Only these in-repository examples were used:

1. `tests/test_cli.py::test_ci_payload_admission_requires_upload_digest_and_canonical_bytes`
   uses `tmp_path`, canonical bytes, direct CLI behavior, and positive/negative
   assertions.
2. `tests/contracts/test_buddy_workflows.py::test_authority_record_multidownload_is_comma_delimited_flat_merged_raw`
   uses named parametrized scenarios and exact whole-mapping assertions.

Continue the local style: descriptive scenario names, real repository YAML
loaded with `yaml.safe_load`, `_document`/`_steps`/`_step`/`_run` helpers,
explicit ordering, exact expressions/mappings, negative assertions, and
`pytest.mark.parametrize(..., ids=...)` for malformed cases. A direct private
call in `test_cli.py` should retain `# noqa: SLF001`.

## Narrow Test Worklist

### High priority

| Target | Testability | Existing classification | Required evidence |
|---|---|---|---|
| `cli.py::_load_mutation_marker` | High | Untested directly | Accept a 64-lowercase-hex bare v7 digest; retain valid prefixed compatibility; reject short, long, uppercase, non-hex, empty/prefix-only values and nonpositive IDs while using an otherwise valid marker |
| `test_buddy_workflows.py` against `live-attempt.yml` | High | Partial | One literal resolved file per raw upload; producer-before-upload ordering; complete reviewer formatter and Authorization basename/Base64/output/consumer chains |

Suggested exact loader test names:

- `test_load_mutation_marker_accepts_upload_artifact_v7_bare_digest`
- `test_load_mutation_marker_accepts_canonical_prefixed_digest`
- `test_load_mutation_marker_rejects_malformed_artifact_transport`

Strengthen the existing named contract scenarios rather than adding unrelated
topology tests. In particular:

- model a raw path as exactly one nonempty line, reject `*`, `?`, `[`, and a
  trailing `/`, then require physical basename = artifact name;
- assert reviewer step order:
  materialize → names → reviewer upload → bind;
- assert the bound reviewer file is Base64-produced, exposed from the
  materializer, passed through approval job outputs/environments, decoded, and
  supplied to the formatter/finalizer;
- assert `approval-finalizer` needs `approval`, the authorization formatter
  step emits name/Base64 from the renamed file, approval job outputs preserve
  them, finalizer decode precedes upload, finalizer outputs preserve
  name/ID/digest, and all three downstream consumers use that basename and
  transport metadata.

### Low priority / no change

| Path | Reason |
|---|---|
| `.github/workflows/workflow-delivery-v3-buddy-smoke.yml` | Referenced topology context only |
| `.github/workflows/workflow-delivery-v3-buddy-smoke-acceptance.yml` | Existence-only context |
| Other v3 files/tests | Explicitly outside the requested boundary |

## Exact Commands

Run all commands from the repository root.

### Discovery

Bounded collection during implementation:

```bash
uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q \
  src/public/lib/three-workflow-delivery-v3/tests/test_cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py
```

Harness-equivalent root discovery check:

```bash
uv run --python 3.13 pytest --collect-only -q
```

### Scoped fix-cycle tests

```bash
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/test_cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py \
  -k 'load_mutation_marker or reviewer_archive_is_decompressed_with_transport_and_payload_bindings or authorization_raw_upload_materializes_exact_attempt_basename or mutation_marker_raw_upload_and_consumers_use_attempt_basename or authority_record_multidownload_is_comma_delimited_flat_merged_raw or user_item11_publisher_preflight_and_start_marker_are_separate'
```

### Bounded lint and workflow validation

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

### Final relevant validation

First execute both complete bounded test modules:

```bash
uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests/test_cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py
```

Then run the affected-project HK-equivalent test gate:

```bash
python eng/scripts/hk_exec.py --timeout-seconds 720 \
  uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q \
  src/public/lib/three-workflow-delivery-v3/tests
```

Finish with root harness discovery, the lint commands above, and:

```bash
git --no-pager diff --check -- \
  .github/workflows/workflow-delivery-v3-live-attempt.yml \
  src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/test_cli.py \
  src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py \
  .testagent/status.md
```

The full affected-package gate is validation, not permission to repair or
inventory an unrelated failure; report any out-of-scope failure without
broadening the patch.

## Acceptance Checklist — Preserve as Separate Items

1. [ ] `_load_mutation_marker` must accept `actions/upload-artifact` v7's
   native bare `artifact-digest`: exactly 64 lowercase hex characters,
   normalize/validate it, and continue rejecting malformed values. A narrowly
   necessary production fix is allowed later.
2. [ ] Strengthen raw-upload contracts in
   `tests/contracts/test_buddy_workflows.py` to prove exactly one resolved file
   is uploaded (not directory/glob), reviewer and authorization producers
   precede uploads, and the reviewer formatter plus authorization
   basename/Base64 chains stay complete across steps, job outputs, and
   consumers.
3. [ ] Follow existing scenario-heavy pytest conventions.
4. [ ] Identify narrow test/lint commands and final relevant workspace
   validation commands without broadening fixes to unrelated failures.
5. [ ] Final changed tests must undergo `test-gap-analysis` and
   `assertion-quality`, with findings appended to `.testagent/status.md`.
6. [ ] Final handoff must identify changed files, exact test names,
   commands/results, and requirement-to-evidence mapping.

Item 4 is researched above; it remains a final execution obligation. Item 5
must occur only after the tests are final. Invoke both named skills against the
final changed tests and append clearly delimited findings to the existing
status file—never rewrite it.

## Final Handoff Evidence Template

The implementation handoff must contain four explicit blocks:

1. **Changed files** — every modified production, test, workflow, and
   `.testagent/status.md` path, distinguishing pre-existing authoritative
   changes from changes made in the implementation pass.
2. **Exact tests** — all added/strengthened test names, including parameter
   IDs for the malformed transport matrix.
3. **Commands/results** — command text, exit status, pass/deselect counts, and
   any bounded report-only failure.
4. **Requirement-to-evidence mapping** — checklist items 1–6 mapped to the
   source line/behavior, exact test assertion(s), validation result, and the
   appended test-gap/assertion-quality status sections.

<!-- END APPEND: 2026-08-18-wdv3-artifact-transport-regression-research -->


<!-- APPENDED PUBLICATION-PREPARATION RESEARCH: preserved in full from the research phase -->

---

# Test Generation Research

## Project Overview

- **Path**: `/workspace/three-workspaces/design-workflows`
- **Bounded package**:
  `src/public/lib/three-workflow-delivery-v3`
- **Language**: Python 3.13 plus GitHub Actions YAML/Bash
- **Framework**: Workflow Delivery v3 control package and one reusable GitHub
  Actions workflow
- **Test framework**: pytest 8.x; PyYAML is used for workflow contract tests
- **Authority**: current tracked tree and `1e742b29..HEAD`. The tracked worktree
  was clean when researched.
- **Baseline checked**:
  - the five relevant test files pass: `232 passed in 11.60s`;
  - `actionlint .github/workflows/workflow-delivery-v3-live-attempt.yml`
    passes.

The existing green tests do not establish the requested regressions: several
tests inspect shell text or duplicate its truth table in Python rather than
executing the workflow shell.

## Explicit Requirement Checklist

1. **Execute the actual publication-preparation classifier shell**
   - Load the current workflow YAML and execute the exact `run` value of
     `release-finalizer` / `Finalize Attempt Outcome`; do not copy its
     classifier into Python.
   - Cover successful Qualification followed by the admitted Observation and
     materialization combinations:
     - Observation `failure|cancelled`, materialization `skipped|cancelled`;
     - Observation `success`, materialization `failure|cancelled` (including
       Snapshot upload failure);
     - workflow cancellation with Observation/materialization
       `skipped/skipped` or `success/skipped`.
   - Reject unexplained skips, Observation/materialization success without a
     durable Snapshot, partial Snapshot transport, non-admitted publisher
     results, and each downstream lineage fact.
   - Cover Snapshot ID/upload digest presence and absence, forwarded Snapshot,
     Authorization, Capability Admission, mutation marker, result bundle, and
     Receipt.
   - Admit an **unstarted** publisher whose GitHub result is `cancelled` only
     when `workflow_cancelled=true`, Snapshot transport is absent, and all
     downstream/mutation lineage is absent. It must be translated as
     publication-preparation interruption, not also as post-Snapshot platform
     termination.

2. **Lock Publication Snapshot lifecycle ordering and output identity**
   - Assert the exact lifecycle:
     `materialize -> names -> upload-snapshot -> upload-reviewer -> bind`.
   - Assert materialization job transport outputs use
     `steps.upload-snapshot.outputs.artifact-id` and
     `steps.upload-snapshot.outputs.artifact-digest`; the canonical Snapshot
     payload digest remains sourced from `steps.materialize`.
   - Assert release-finalizer downloads and passes the Snapshot directly from
     `needs.materialize-publication`, not from approval forwarding.
   - Execute a later reviewer upload/binding failure scenario with a durable
     Snapshot and prove the shell preserves Snapshot arguments and does not add
     `--publication-preparation-interrupted`.

3. **Directly cover every publication-preparation `AttemptOutcome` negative**
   - Retain current direct substitutions for `uncertainty`,
     `authorization_digest`, and `publication_snapshot_digest`.
   - Add one direct substitution each for:
     `capability_admission_digests`,
     `capability_group_bundle_digests`, `receipt_digests`, `result`,
     `possibly_mutated`, and `next_action`.
   - Each case must construct/replace the real `AttemptOutcome` and fail with
     the publication-preparation invariant; do not test through a mock.

4. **Execute the release-finalizer postamble**
   - Use the repository Bash harness pattern to execute the real finalizer
     shell with a CLI boundary double that writes Outcome/summary files and
     returns the real incomplete status (`1`).
   - Prove the retained Attempt summary and GitHub Step Summary contain the
     publication-preparation diagnostics, the final artifact name/status are
     emitted, and files still exist after the nonzero finalize status.
   - Keep the structural assertion that the `if: always()` upload is between
     finalize and propagation.
   - Execute the exact `Propagate finalization status` shell with a successful
     retention upload and prove the job exits nonzero afterward.

5. **Link the uploaded reviewer artifact from the completed job summary**
   - Add a workflow contract assertion that the reviewer artifact URL is
     appended only after `upload-reviewer` and exact payload binding succeed.
   - The summary/link step must follow `bind`; a binding failure must not
     present an unbound reviewer payload as a completed review surface.
   - Assert the immutable `reviewer-summary.md` is only read; the URL is written
     to `GITHUB_STEP_SUMMARY`, never appended to or rewritten into the reviewer
     payload.

6. **Update publisher-result truth-table coverage**
   - Replace or fold the current Python-only publisher-result calculation into
     the executable shell scenarios.
   - Include the accepted whole-run cancellation case, rejection without
     workflow-cancellation ownership, rejection with any lineage, and ordinary
     post-Snapshot publisher cancellation mapping to platform termination.

7. **Non-behavioral constraints**
   - Add tests first. Make only the smallest workflow/production change exposed
     by those tests; no unrelated refactors, gratuitous tests, or formatting
     locks.
   - Never restore missing source or mutate version-control state.
   - Keep `.testagent/research.md`, `.testagent/plan.md`, and
     `.testagent/status.md` current across the full Research -> Plan ->
     Implement pipeline. This phase updates only `research.md`.
   - Run a narrow test after each implementation phase, then the complete
     affected package and applicable workflow/HK validation as practical.

## Relevant Diff Facts (`1e742b29..HEAD`)

The range contains five commits:

- `62ac4bb2`: records the publication-preparation interruption design;
- `fca9862d`: adds the `AttemptOutcome`, live finalizer, and CLI contract;
- `8377343b`: adds direct workflow facts, diagnostics, retention, and
  fail-after-retention behavior;
- `14b40c75`: closes review findings in workflow and tests;
- `5f8449d7` (`HEAD`): reconciles the v3 documentation.

Relevant current behavior:

- `records/release.py::AttemptOutcome.__post_init__` admits a Snapshot-free
  `publication-preparation` Outcome only as `incomplete`, uncertain,
  not-possibly-mutated, with no later records and `new-attempt`.
- `release/live.py::finalize_attempt_outcome` accepts the semantic
  `publication_preparation_interrupted` fact, rejects contradictory domain
  records/facts, and forms that exact Outcome.
- `cli.py::_release_finalize_live_command` exposes/forwards
  `--publication-preparation-interrupted`, writes Outcome and summary before
  returning status `1` for `incomplete`.
- The workflow now gives `release-finalizer` direct `needs` access to
  Observation and materialization, sources Snapshot transport directly from
  materialization, records workflow cancellation, classifies interruption,
  appends diagnostics, uploads final files with `if: always()`, then propagates
  failure.

Observed regression gaps in the current tree:

- `test_publication_preparation_interruption_truth_table_is_exact` duplicates
  the classifier in Python and only checks two shell substrings.
- Diagnostics and propagation tests are text/order assertions; no finalizer
  shell postamble is executed.
- Lifecycle tests omit `upload-snapshot` from their ordering chain.
- No executable case proves Snapshot lineage survives a later reviewer failure.
- The publication-preparation record substitution table covers only
  uncertainty, Authorization, and Snapshot among the requested core fields.
- The current shell requires publisher result `skipped` unconditionally and
  later maps every `cancelled` publisher to `--platform-terminated`; this cannot
  represent the newly approved unstarted/whole-run cancellation case.
- `Materialize exact publication basenames` currently copies the reviewer
  summary to `GITHUB_STEP_SUMMARY` **before** either upload. No later step
  appends `steps.upload-reviewer.outputs.artifact-url`.

The current MLD/LLD text says the publisher “must be skipped.” The request
explicitly approves GitHub result `cancelled` only as the platform spelling of
an unstarted publisher under whole-workflow cancellation. Implementation must
keep that exception narrow; a minimal normative wording reconciliation may be
needed, but this is not permission to broaden cancellation admission.

## Scope

- **Boundary**: publication preparation, reviewer transport/summary, and the
  sole live release-finalizer in
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`, plus the exact
  Python Outcome/finalizer/CLI contracts it invokes.
- **Production/workflow targets**:
  - `.github/workflows/workflow-delivery-v3-live-attempt.yml`
    - job `materialize-publication`;
    - job `release-finalizer`, especially steps `Finalize Attempt Outcome`,
      `Upload final Attempt Outcome and summary`, and
      `Propagate finalization status`.
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py`
    - `AttemptOutcome`.
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/live.py`
    - `finalize_attempt_outcome`.
  - `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`
    - `_release_finalize_live_command`, parser flag, and
      `LIVE_OUTCOME_EXIT_STATUS`.
- **Test targets**:
  - `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
  - `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`
  - existing integration protection in
    `tests/release/test_commit8_live_scenarios.py`,
    `tests/release/test_live_qualification_boundary.py`, and
    `tests/test_cli.py`.
- **Representative existing tests**:
  - `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
  - `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py`
- **Excluded**: sibling projects, unrelated v1/v2 workflows, remote GitHub
  execution, real publication, and unrelated release adapters. The Python
  `release/finalizer.py` materialization algorithms are unchanged and are not a
  target for this shell/workflow regression.

## Dependency Graph

- **Leaf/core record**:
  `AttemptOutcome` (exact field validation and canonical document; depends on
  existing `ReleaseAttemptIdentity` and validation helpers).
- **Mid-layer domain finalizer**:
  `finalize_attempt_outcome` (consumes Qualification Decision, optional
  Snapshot/Authorization/Capability/bundle/Receipt records and forms
  `AttemptOutcome`).
- **Top-layer adapter**:
  `_release_finalize_live_command` (loads exact retained records, calls the
  domain finalizer, writes Outcome/summary/output metadata, maps result to exit
  status).
- **Workflow layer**:
  `materialize-publication` supplies durable Snapshot/reviewer transport;
  `release-finalizer` translates direct GitHub job/output facts into CLI flags,
  retains diagnostics, and propagates failure.

Tests should therefore proceed record invariant -> domain/CLI preservation ->
actual workflow shell. No mocking is needed for `AttemptOutcome`; the shell
harness should double only the already-tested CLI process boundary.

## Exact Source-to-Test Pairs

| Source/workflow target | Exact relevant test pair(s) | Current classification |
|---|---|---|
| `.github/workflows/workflow-delivery-v3-live-attempt.yml` / `materialize-publication`, `release-finalizer` | `tests/contracts/test_buddy_workflows.py` | **Partial**: strong static topology checks, but classifier/postamble are not executed and reviewer URL is absent |
| `records/release.py::AttemptOutcome` | `tests/release/test_commit8_contracts.py` | **Partial** for this slice: canonical case exists; six requested direct negative fields are missing |
| `release/live.py::finalize_attempt_outcome` | `tests/release/test_commit8_live_scenarios.py` | **Substantial** semantic coverage: exact Outcome and contradictory records/facts are covered |
| `cli.py::_release_finalize_live_command` | `tests/release/test_live_qualification_boundary.py`, `tests/test_cli.py` | **Substantial**: real CLI transport, incomplete exit status, and parser flag are covered |

The required `find-untested-sources` tree-sitter analyzer was run once at the
bounded package root with `--lang python --include-tested`: 38 source files and
41 tests were parsed; all four Python target files are statically paired with
tests. This is a parse-only identifier/import heuristic, not line or branch
coverage evidence. YAML-to-contract-test pairing is manual because the analyzer
does not pair workflow YAML.

## Files to Test

### High Priority

| File | Classes/functions/jobs | Testability | Estimated coverage | Notes |
|---|---|---:|---|---|
| `.github/workflows/workflow-delivery-v3-live-attempt.yml` | `materialize-publication`, `release-finalizer` shell | High | Partial | Primary regression surface; execute extracted Bash with a CLI boundary double |
| `tests/contracts/test_buddy_workflows.py` | workflow topology and shell scenarios | High | Partial | Replace duplicated truth tables; add reusable renderer/executor |
| `records/release.py` | `AttemptOutcome.__post_init__` | High | Partial for requested invariant | Direct dataclass substitutions, no mocks |
| `tests/release/test_commit8_contracts.py` | publication-preparation substitution matrix | High | Partial | Add only the six missing field cases |

### Medium Priority

| File | Classes/functions | Testability | Estimated coverage | Notes |
|---|---|---:|---|---|
| `release/live.py` | `finalize_attempt_outcome` | High | Substantial | Retain existing scenario protection; production change is not currently indicated |
| `cli.py` | live finalization command/parser/status map | High | Substantial | Existing integration proves incomplete writes files then returns `1` |
| `tests/release/test_commit8_live_scenarios.py` | publication-preparation domain scenarios | High | Substantial | Run after workflow changes; add only if an uncovered domain behavior emerges |
| `tests/release/test_live_qualification_boundary.py` and `tests/test_cli.py` | CLI transport/parser | High | Substantial | Regression validation, not first-choice edit targets |

### Low Priority / Skip

| File | Reason |
|---|---|
| `src/.../release/finalizer.py` | Publication Snapshot materialization logic is unchanged; the requested “release-finalizer shell” is the workflow job |
| Other Workflow Delivery projects/workflows | Outside the bounded v3 live-attempt request |
| v1/v2 sources | Non-normative and explicitly out of scope |

## Existing Tests & Coverage Classification

- Workflow pairing is partial: exact action pins, permissions, output strings,
  artifact settings, and many orderings are checked, but the new classifier and
  failure postamble are only inspected as text.
- `AttemptOutcome` pairing is partial for this request: the positive
  publication-preparation shape and three negative substitutions exist.
- Domain live finalization is substantial: successful/unsuccessful
  Qualification, required Snapshot, preparation interruption, all contradictory
  record categories, exact Boolean facts, platform termination, and receipt
  closure already have scenarios.
- CLI coverage is substantial: a real successful Qualification can be
  terminalized as publication-preparation, the retained Outcome is admitted
  back through transport, and the command returns nonzero for incomplete.
- No numeric coverage percentage is claimed because no coverage report was
  produced.

## Existing Test Project

- **Project file**:
  `src/public/lib/three-workflow-delivery-v3/pyproject.toml`
- **Target source project**: same Python package under `src/`
- **Test root**:
  `src/public/lib/three-workflow-delivery-v3/tests`
- **Runner/config**: root `pyproject.toml` supplies pytest
  `--import-mode=importlib`; the package dev group supplies pytest.

## Scenario-First Testing Patterns

- Use descriptive `test_<scenario>_<expected_behavior>` functions, no test
  classes.
- Use `pytest.mark.parametrize` with named tuple columns and readable case IDs
  for platform-state scenarios.
- Reuse `_document`, `_steps`, `_step`, and `_run`; assert semantic job/step
  identity and security/lifecycle boundaries rather than incidental YAML
  formatting.
- Use bare pytest assertions and `pytest.raises(..., match=...)`.
- For core records, use immutable `dataclasses.replace` to vary one field at a
  time.
- Do not add a second Python model of shell behavior. Consolidate the existing
  preparation and publisher truth tables into shell-driven scenarios where
  practical.

Suggested scenario-oriented names:

- `test_publication_preparation_classifier_executes_workflow_shell`
- `test_durable_snapshot_survives_later_reviewer_failure`
- `test_publication_preparation_outcome_rejects_each_forbidden_fact`
- `test_incomplete_preparation_retains_diagnostics_before_job_failure`
- `test_completed_materialization_summary_links_immutable_reviewer_artifact`

## Actual Shell Execution Harness Pattern

Follow the established pattern in
`tests/contracts/test_commit10_acceptance_workflow.py`:

1. Parse the workflow with `yaml.safe_load` and extract the exact step `run`
   string.
2. Render every `${{ ... }}` occurrence from one explicit scenario fact map and
   assert no unresolved expression remains.
3. Use `tmp_path`; set `GITHUB_OUTPUT`, `GITHUB_STEP_SUMMARY`,
   `GITHUB_RUN_ID`, `GITHUB_RUN_ATTEMPT`, and `WDV3_PACKAGE`.
4. Put a tiny executable `uv` boundary double first on `PATH`. It should record
   the received CLI arguments, write the requested Outcome and summary paths,
   optionally emit CLI outputs, and return the configured status. Do not mock
   or rewrite the classifier.
5. Execute with an argv tuple, not `shell=True`:
   `bash --noprofile --norc -euo pipefail -c <exact-run>`, with `cwd=tmp_path`.
6. For admitted cases, inspect captured CLI argv for the exact semantic flags.
   For rejected cases, assert nonzero status, the specific workflow diagnostic,
   and that the CLI double was not invoked.
7. Execute the exact propagation step separately with rendered step outcomes
   to prove retention succeeds before final nonzero job status.

Minimum shell scenario set:

| Cancellation | Observation | Materialization | Publisher | Snapshot/lineage | Expected |
|---:|---|---|---|---|---|
| no | failure | skipped | skipped | absent | preparation flag |
| no | cancelled | cancelled | skipped | absent | preparation flag |
| no | success | failure | skipped | absent (upload failed) | preparation flag |
| no | success | cancelled | skipped | absent | preparation flag |
| yes | skipped | skipped | skipped | absent | preparation flag |
| yes | success | skipped | skipped | absent | preparation flag |
| yes | skipped | skipped | cancelled, unstarted | absent | preparation flag only |
| no | skipped | skipped | cancelled | absent | reject |
| no | success | skipped | skipped | absent | reject unexplained skip |
| no | success | success | skipped | absent | reject missing durable Snapshot |
| any admitted row | same | same | same | digest without ID | reject partial transport |
| any admitted row | same | same | success/failure | absent | reject publisher result |
| any admitted row | same | same | skipped/cancelled | each downstream lineage field in turn | reject |
| no | success | failure | skipped | durable Snapshot ID/digest | preserve Snapshot path; no preparation flag |
| no | any post-Snapshot state | cancelled | durable Snapshot | ordinary platform-termination mapping |

This set covers the approved cancellation rule without a combinatorial
cross-product or gratuitous cases.

## Build & Test Commands

- **Build**:
  `uv build --package three-workflow-delivery-v3`
- **Test (workflow fix cycle)**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Test (record fix cycle)**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`
- **Test (bounded integration)**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_live_scenarios.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
- **Test (harness-equivalent discovery check, repo root)**:
  `uv run --python 3.13 pytest --collect-only -q`
- **Final affected package**:
  `python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
- **Workflow lint**:
  `actionlint .github/workflows/workflow-delivery-v3-live-attempt.yml`
- **Python lint/format**:
  `uv run --python 3.13 ruff check --force-exclude -- <changed-python-paths>`
  and
  `uv run --python 3.13 ruff format --check --force-exclude -- <changed-python-paths>`
- **Type check**:
  `uv run --python 3.13 pyrefly check <changed-python-paths>`
- **Affected-file HK gate**:
  `hk check --check .github/workflows/workflow-delivery-v3-live-attempt.yml src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py`

Use only paths actually changed when running Ruff/Pyrefly/HK. If Python
production remains unchanged, do not include it merely to enlarge validation.

## Likely Gaps / Blockers

- **Likely required workflow change**: narrowly admit `publish_result=cancelled`
  for the approved whole-run/unstarted case and prevent that same fact from also
  adding `--platform-terminated` to a preparation Outcome.
- **Likely required workflow change**: move completed-summary rendering after
  reviewer upload and append the returned artifact URL without mutating
  `reviewer-summary.md`.
- **Likely test-only changes**: exact Snapshot upload ordering/output identity,
  later reviewer failure lineage, all missing `AttemptOutcome` substitutions,
  and executable postamble coverage.
- **Normative wording mismatch**: current MLD/LLD says “publisher must be
  skipped”; the approved exception must be documented or explicitly treated as
  GitHub's cancelled result for an unstarted job. It is bounded enough to test
  now, but must not become general cancellation admission.
- **Harness boundary**: upload-artifact itself cannot be run locally. Test its
  `if: always()`, ordering, path, and outputs structurally; execute both adjacent
  shell steps with simulated action outputs.
- **No dependency blocker**: Bash, PyYAML, pytest, and stdlib subprocess/path
  tools already exist. No new package or generalized workflow abstraction is
  justified.

## Recommendations

1. Add the direct `AttemptOutcome` substitutions first.
2. Add one reusable exact-workflow-shell harness and replace duplicated
   preparation/publisher calculations with scenario rows.
3. Add lifecycle/output/summary contracts, including the durable-Snapshot later
   failure scenario.
4. Let the failing tests drive only the two likely YAML corrections.
5. Run the narrow file after each step, then bounded integration, full package,
   discovery, actionlint, and the affected HK gate.

<!-- BEGIN APPEND: 2026-08-19-wdv3-four-accepted-repairs-research-4a38b286 -->

# Workflow Delivery v3 Four-Repair Research

## Authority and Boundary

- **Authority**: `HEAD` `4a38b286a5ff`; `git status --short` was empty at
  research start. The current files, not earlier handoff results, define the
  baseline.
- **Project**: `src/public/lib/three-workflow-delivery-v3`; Python 3.13,
  uv, pytest, PyYAML workflow contracts, Bash, and GitHub Actions YAML.
- **Production boundary**:
  `.github/workflows/workflow-delivery-v3-live-attempt.yml` job
  `release-finalizer`, and
  `three_workflow_delivery_v3/cli.py::_release_finalize_live_command` plus its
  directly used uploaded-record/parser helpers.
- **Test boundary**:
  `tests/contracts/test_buddy_workflows.py`,
  `tests/release/test_live_qualification_boundary.py`, and
  `tests/test_cli.py`; retain
  `tests/release/test_commit6_transport_cli.py` only as the closed-transport
  regression if a shared transport typing helper changes.
- **Configuration boundary**: root and package `pyproject.toml` plus
  `.github/actionlint.yaml`; `tests/test_hk_trigger.py` supplies the existing
  `.testagent`/HK artifact contracts.
- **Excluded**: sibling projects, other workflows, real Marketplace-action
  execution, GitHub API/publication probes, v1/v2, generalized transport
  refactors, and Publication Control Closure documentation reconciliation.

The unavailable `code-testing-extensions` skill was replaced by the already
consulted Python extension at
`.agents/skills/code-testing-extensions/extensions/python.md`. The required
one-time `find-untested-sources` attempt against this bounded Python package
stopped before analysis because `tree-sitter-language-pack` is not installed.
The manual static pairings below are therefore a limitation, not a blocker;
do not rerun the analyzer or install the package.

## Bounded Inventory and Source-to-Test Pairs

| Target | Exact symbols/steps | Canonical tests | Current coverage |
|---|---|---|---|
| `.github/workflows/workflow-delivery-v3-live-attempt.yml` | `release-finalizer`; acquisition steps; `Record workflow cancellation`; `Finalize Attempt Outcome` | `test_buddy_workflows.py` | Partial: exact shell harness exists, but prerequisite `if` contracts and unsuccessful-Qualification cancellation rows are missing |
| `three_workflow_delivery_v3/cli.py` | `_release_finalize_live_command`, `_add_uploaded_record_arguments`, `_load_release_record`, `_load_publication_snapshot`, `_load_authorization`, `_load_capability_decision` | `test_live_qualification_boundary.py`; `test_cli.py` | Partial: all-absent and some all-present transports work; no 5×4 partial-group matrix |
| `records/release.py::admit_release_record` and `records/release_transport.py::release_record_from_document` | Closed typed record admission used by `_load_release_record` | `test_commit6_transport_cli.py::test_every_transported_commit6_release_record_round_trips_closed_schema` | Substantial runtime coverage; change only if needed for a narrowly typed helper |
| Root/package `pyproject.toml`, `.github/actionlint.yaml` | pytest discovery, Ruff, Pyrefly, actionlint | Commands below | Configuration only |

No domain change is indicated in `release/live.py::finalize_attempt_outcome` or
`records/release.py::AttemptOutcome`: both already support qualification-only
failed/incomplete outcomes and ordinary post-Snapshot platform termination.

## Current Behavior and Exact Gaps

### 1. Release-finalizer acquisition

The following required acquisition steps currently omit `if: always()`:

- `Check out exact selected target`
- `Install uv`
- `Download Release Attempt binding by artifact ID`
- `Download Qualification Snapshot by artifact ID`
- `Download Qualification Decision by artifact ID`

The ten optional downloads currently use only `<artifact-id> != ''`: build,
project-test, artifact-contents, install-import, Release Artifact, Publication
Snapshot, Authorization, Capability Admission Decision, capability result
bundle, and Receipt. They need `always() && <artifact-id> != ''`.

`test_unsuccessful_live_qualification_retains_a_publication_free_outcome` and
`test_release_finalizer_downloads_snapshot_directly_from_materialization`
currently lock the old optional conditions. Update those assertions and add one
focused structural contract that enumerates every prerequisite action and its
exact condition. Do not execute `actions/checkout`, `setup-uv`, or
`download-artifact` locally.

### 2. Cancellation and publisher ownership

Reuse the canonical exact-workflow-shell harness in
`test_buddy_workflows.py`:

- `_phase2_finalizer_facts`
- `_phase2_render_finalizer_run`
- `_phase2_execute_finalizer_shell`
- `_phase2_assert_successful_finalizer`

Do not create a second renderer, fact model, or Bash harness.

Current gaps:

- `test_publisher_result_truth_table_executes_workflow_shell[
  cancelled-without-workflow-ownership]` uses Observation=`skipped`, so it
  fails at interruption admission instead of publisher ownership. Use the
  default Observation=`failure`, materialization=`skipped`,
  publisher=`cancelled`, and workflow cancellation=`false`; assert the
  publisher-ownership rejection and no CLI invocation.
- The shell adds `--platform-terminated` for every cancelled publisher not
  classified as publication preparation. Thus exact Qualification
  `failure`/`incomplete` followed by whole-workflow cancellation is
  misclassified even when Snapshot, Authorization, capability, bundle,
  Receipt, and mutation-marker lineage are all absent.
- Add executable `failure` and `incomplete` rows with
  Observation/materialization=`skipped`, publisher=`cancelled`, workflow
  cancellation=`true`, and no downstream lineage. Compare the complete
  captured CLI argv, not only flag membership: it must contain only current
  Attempt/Qualification replay inputs and outputs, with neither
  `--publication-preparation-interrupted` nor `--platform-terminated`.
- Preserve `post-snapshot-cancelled` as `--platform-terminated`. Preserve the
  six existing `cancelled-with-*` lineage rejections, partial-Snapshot
  rejections, and a no-workflow-ownership negative.

Suggested focused test:
`test_cancelled_unsuccessful_qualification_uses_exact_qualification_only_argv`
with IDs `failure` and `incomplete`.

### 3. Finalize-live optional transport groups

`_add_uploaded_record_arguments(..., required=False)` makes each member
independently optional. `_release_finalize_live_command` currently decides
presence from only the path, and validates groups only while loading them,
after mandatory records and Qualification replay. A missing path can silently
discard supplied metadata; other missing members produce incidental loader
errors rather than one clear group error.

Before calling `_load_attempt_binding`, validate each of these five groups:

1. `publication_snapshot`
2. `authorization`
3. `capability_decision`
4. `capability_group_bundle`
5. `receipt`

For each group, path, record digest, artifact ID, and artifact digest must be
all present or all absent. Reuse one explicitly typed helper taking those four
optional values and returning presence/typed transport state. It should raise
`ValueError` naming the group, add no `cast`, and add no broad exception
handler; `main` already maps `ValueError` to stderr/status `1`.

Do not change `_optional_evidence` or optional Qualification Evidence behavior
unless the same helper can be reused without changing its established
semantics.

Add a direct CLI matrix against valid exact Qualification replay:
`test_finalize_live_rejects_each_partial_optional_transport_group`, with 20
readable IDs (`5 groups × missing path|record-digest|artifact-id|
artifact-digest`). Every row must assert status nonzero, the group-specific
error, and absence of the Attempt Outcome (and summary). Extend
`test_cli_exposes_strict_commit8_live_transport_commands` or add a focused
help test to lock all 20 parser option names.

## Existing Conventions

- Tests are module-level scenario functions with bare assertions and readable
  `pytest.mark.parametrize` IDs.
- Workflow contracts parse the authoritative YAML through `_document`,
  `_steps`, `_step`, and `_run`.
- Shell tests render every `${{ ... }}` expression, execute the extracted
  `run` body with `bash --noprofile --norc -euo pipefail -c`, and double only
  the `uv`/CLI process boundary.
- Direct CLI behavior uses canonical bytes, `_uploaded_arguments`, `tmp_path`,
  real parser dispatch, `capsys`, exact nonzero status, and filesystem
  non-creation assertions.
- Use American English and scenario-first assertions; avoid formatting locks,
  unrelated refactors, casts, and broad catches.

## Acceptance Checklist

1. **Prerequisite acquisition**: all five required acquisition actions use
   `if: always()`; all ten optional downloads use
   `always() && <artifact-id> != ''`; one focused structural contract locks
   every step; Marketplace actions remain structural-only locally.
2. **Cancellation/publisher semantics**: correct the no-ownership row so it
   reaches publisher rejection; exact `failure` and `incomplete`
   Qualification cancellation use qualification-only argv with neither
   semantic flag; post-Snapshot cancellation remains platform termination;
   contradictory and downstream lineage stays rejected.
3. **Optional groups**: preflight all five four-member groups before any record
   load; partial state raises clear `ValueError` before Outcome writing; use
   one typed helper without casts/broad catches; cover all 20 missing-member
   cases; preserve optional Qualification Evidence behavior.
4. **Validation**: run the narrow workflow contract, live Qualification CLI,
   parser/behavior tests, Ruff check/format, Pyrefly, actionlint, append-only
   artifact checks, discovery, and `git diff --check`. Publication Control
   Closure documentation reconciliation remains out of scope.
5. **State discipline**: keep `.testagent/research.md`,
   `.testagent/plan.md`, and `.testagent/status.md` byte-prefix append-only
   against `4a38b286`; append only concise unique sections; do not commit.
6. **Implementation discipline**: scenario-first, American English, and
   surgical production changes only.

## Exact Validation Commands

- **Build**:
  `uv build --package three-workflow-delivery-v3`
- **Workflow contract**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Live Qualification CLI**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py`
- **CLI parser/behavior**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
- **Closed transport regression**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/release/test_commit6_transport_cli.py`
- **Bounded combined tests**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit6_transport_cli.py`
- **Harness-equivalent discovery from repository root**:
  `uv run --python 3.13 pytest --collect-only -q`
- **Full affected package**:
  `GIT_LFS_SKIP_SMUDGE=1 python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
- **Ruff check**:
  `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release_transport.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
- **Ruff format check**:
  `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release_transport.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
- **Pyrefly**:
  `uv run --python 3.13 pyrefly check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release_transport.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/release/test_live_qualification_boundary.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py`
- **Workflow lint**:
  `actionlint .github/workflows/workflow-delivery-v3-live-attempt.yml`
- **Existing append-only/HK artifact tests**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_hk_trigger.py`
- **All three byte-prefixes**:
  `python -c 'from pathlib import Path; import subprocess; paths=(".testagent/research.md",".testagent/plan.md",".testagent/status.md"); assert all(Path(path).read_bytes().startswith(subprocess.check_output(("git","show",f"HEAD:{path}"))) for path in paths)'`
- **Whitespace/diff integrity**:
  `git diff --check`

<!-- END APPEND: 2026-08-19-wdv3-four-accepted-repairs-research-4a38b286 -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-four-repairs-research-correction -->

## Phase 3 canonical-option correction

`finalize-live` already names the path and record-digest members
`--<group>` and `--<group>-digest`. The final repair preserves those canonical
options; it adds no `-path`/`-record-digest` aliases. The 20 direct CLI rows use
the existing options and lock preflight-before-record-loading through the
existing `_load_attempt_binding` seam. Production uses the typed
`_validate_optional_uploaded_record_transport` validator and otherwise leaves
the established all-present loaders unchanged.

<!-- END APPEND: 2026-08-19-wdv3-four-repairs-research-correction -->
<!-- BEGIN APPEND: 2026-08-19-wdv3-six-final-review-repairs-research -->

## Workflow Delivery v3 six final-review repairs

### Scope and authority

- Read `docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md`,
  `docs/AGENTS.md`, and the bounded workflow/source/test files below.
- Treat the current tree as authoritative; do not commit or mutate package,
  acceptance, activation, sentinel, smoke-LLD, or artifact-REST behavior.
- `code-testing-extensions` was unavailable. Existing Python/pytest, parsed
  YAML, and extracted-shell conventions are sufficient.

### Acceptance checklist

1. Replace the finalizer step cancellation recorder with a no-permission
   `workflow-cancellation` job: exact `if: cancelled()`, six authoritative
   dependencies, exposed output, exact recorder shell, finalizer dependency,
   and skipped-job `false` fallback.
2. Guard the three mandatory finalizer downloads with `always()` plus a
   nonempty artifact ID; preserve optional predicates.
3. Execute both unsuccessful Qualification results against each of nine
   independent publication/platform operands through real
   `finalize_attempt_outcome`.
4. Exercise real `finalize-live` parser, record loading, Receipt transport
   construction, and forwarding of five downstream record groups plus all
   three platform facts.
5. Execute the retained propagation shell for all-success and each independent
   failure input.
6. Repair the two current documents without changing the historical smoke LLD
   or weakening activation/probe/sentinel/package-mutation prohibitions.
7. Run focused tests, quality/workflow/docs/artifact gates, then the full v3
   package with `GIT_LFS_SKIP_SMUDGE=1`.

### Bounded inventory and conventions

| Target | Existing convention / required pairing |
|---|---|
| `.github/workflows/workflow-delivery-v3-live-attempt.yml` | Parsed YAML and authoritative extracted Bash in `tests/contracts/test_buddy_workflows.py` |
| `release/live.py::finalize_attempt_outcome` | Scenario-first typed records in `tests/release/test_commit8_live_scenarios.py` |
| `cli.py::_release_finalize_live_command` | Real parser/loaders with only the domain call captured in `tests/release/test_live_qualification_boundary.py` |
| `release-delivery-mld.md`, v3 `README.md` | Current normative terminology/checkpoint; smoke LLD is read-only |
| `.testagent/{research,plan,status}.md` | Preserve the complete `HEAD` byte prefix and append only |

The workflow's six authoritative fact owners are `admit`,
`qualification-finalizer`, `observe-github-packages`,
`materialize-publication`, `approval-finalizer`, and
`publish-github-packages`. The unsuccessful domain guard already owns all nine
operands; missing coverage is independent execution, not production logic.
The CLI already loads/forwards the requested values; its missing evidence is a
single real handler-boundary test.

<!-- END APPEND: 2026-08-19-wdv3-six-final-review-repairs-research -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-two-adjudicated-test-gaps-research -->

## Workflow Delivery v3 two adjudicated test gaps

### Scope and strategy

- **Strategy:** Direct. The bounded change strengthens one existing pytest
  workflow-contract file and does not require production changes.
- **Authority:** current
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`, parsed and executed
  by
  `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`.
- Preserve the current tree, all existing tests, and all package/workflow
  behavior. Do not run publication, acceptance, activation, sentinel, package
  mutation, or repository-wide validation.

### Requirement checklist and bounded inventory

1. Assert the exact producer expression for every digest-valued output of the
   `qualification-finalizer` job, so swapping record or upload digest producers
   is observable from the real parsed workflow.
2. In an existing executable successful-Qualification finalizer scenario, use
   distinct record-digest and upload-digest sentinels for each retained
   Qualification/Release record and assert the exact emitted record argv.
3. Execute successful Qualification with workflow cancellation witnessed,
   Observation `success`, materialization `skipped`, publisher `cancelled`, and
   no Publication Snapshot or downstream lineage. Require success and exactly
   `--publication-preparation-interrupted` among semantic platform/cancellation
   flags.
4. Keep the change test-only apart from EOF appends to `.testagent`; run only
   the three directly affected pytest nodes.
5. Run bounded pseudo-mutation and assertion-depth review, repair any true
   positive in this scope, and append the outcome to `.testagent/status.md`.

The existing test conventions are parsed-YAML source contracts plus execution
of the exact extracted Bash with a recording `uv` boundary double. The relevant
pair is only the live-attempt workflow and `test_buddy_workflows.py`; no
unrelated source inventory or coverage scan is needed.

<!-- END APPEND: 2026-08-19-wdv3-two-adjudicated-test-gaps-research -->

<!-- BEGIN APPEND: 2026-08-19-wdv3-final-rereview-two-test-gaps-research -->

## Workflow Delivery v3 final re-review test gaps

### Scope and strategy

- **Strategy:** Direct. Both independently adjudicated true positives belong
  to one existing pytest workflow-contract file.
- **Authority:** the parsed and executable Release Finalizer shell in
  `.github/workflows/workflow-delivery-v3-live-attempt.yml`.
- `code-testing-extensions` was unavailable. The existing Python/pytest,
  parsed-YAML, and extracted-shell conventions remain authoritative.
- Production behavior, dependencies, publication/acceptance/activation,
  sentinel state, package state, and repository history are out of scope.

### Requirement checklist and bounded inventory

1. Lock the exact `artifact-id` and `artifact-name` producer expressions for
   retained build Evidence, project-test Evidence, artifact-contents Evidence,
   install-import Evidence, Qualification Snapshot, Adapter Context, Release
   Artifact, and Qualification Decision transports. In particular,
   `release-artifact-artifact-id` must come from the Release Artifact output,
   not the build Evidence output.
2. Execute successful Qualification with cancellation witnessed, Observation
   `success`, materialization `skipped`, publisher `cancelled`, and no
   Publication Snapshot/downstream lineage through the real workflow shell.
   Require publication-preparation interruption semantics and exact
   materialization/publisher facts in both retained summary surfaces.
3. Preserve all existing behavior and tests; add no combinatorial matrix or
   syntax-only substitute for the executable retention scenario.
4. Run the two affected pytest nodes, Ruff check/format, append-only prefix
   validation, and diff checks. The parent agent owns the full package/HK gate.
5. Run bounded pseudo-mutation and assertion-quality review, fix any in-scope
   true positive, and append the final evidence to `.testagent/status.md`.

The bounded source-to-test pair is only
`.github/workflows/workflow-delivery-v3-live-attempt.yml` and
`src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`.
The current helper already executes the exact YAML shell with a recording CLI
boundary double; it only needs to expose the two generated summary paths.

<!-- END APPEND: 2026-08-19-wdv3-final-rereview-two-test-gaps-research -->

<!-- BEGIN APPEND: 2026-08-19T20:01:25Z-wdv3-buddy-caller-held-release-execution-concurrency-repair-research -->

## Workflow Delivery v3 Buddy caller-held Release Execution concurrency repair research (2026-08-19T20:01:25Z)

### Project overview and bounded authority

- **Workspace**: `/workspace/three-workspaces/design-workflows`
- **Boundary**: only
  `.github/workflows/workflow-delivery-v3-buddy-smoke.yml`, the real
  `compile-live-model` CLI path, the existing Buddy identity/canonical digest
  helpers that path must use, their canonical tests, and directly relevant
  Buddy workflow/business-scenario tests.
- **Language/framework**: Python 3.13 in a UV workspace, Hatchling package
  `three-workflow-delivery-v3`, pytest 8 with
  `--import-mode=importlib`, PyYAML workflow contract tests, RFC 8785
  canonical JSON, and GitHub Actions YAML.
- **Instructions read first**: root `AGENTS.md`, `docs/AGENTS.md`, the complete
  applicable v3 handoff context, `requirements.md` for WD-REL-001 and
  WD-CON-002/003/006, HLD **Concurrency Design**, Release MLD request-local
  compilation/identity/duplicate-request/coalescing sections and scenarios,
  the smoke LLD caller DAG/concurrency section, and
  `.agents/skills/code-testing-agent/unit-test-generation.prompt.md`.
  No `AGENTS.local.md` exists in the workspace.
- The parent-reported `code-testing-extensions` skill is unavailable. The
  checked-in base Python extension at
  `.agents/skills/code-testing-extensions/extensions/python.md` exists and was
  read directly; no language example was read.
- This is research only. Production, workflows, tests, package manifests,
  locks, `.testagent/plan.md`, and `.testagent/status.md` were not changed.

### Confirmed requirement checklist

Keep these as separate implementation/evidence items:

1. [ ] Buddy Release Execution Identity is exactly channel `buddy`, Release
   Unit `hcoona-release-smoke-npm`, and immutable 40-lowercase-hex target SHA.
2. [ ] For three same-identity dispatches, model GitHub's documented
   one-running/one-pending behavior: the first may run, the second is pending,
   and a later same-group dispatch replaces the pending one without canceling
   the running one. Do not claim ordering/fairness beyond GitHub's documented
   concurrency behavior.
3. [ ] Only dispatches that survive caller coalescing and reach reusable
   admission create Attempts. The replaced pending caller never invokes the
   reusable workflow and creates no Attempt; each surviving admitted request
   creates its own Attempt in the same Execution.
4. [ ] Different immutable target SHAs derive different Execution identities
   and deterministic groups.
5. [ ] Request ID, workflow run ID, run attempt, canonical/native version,
   External Package Coordinate, Destination Adapter, and destination
   projection cannot enter or alter the Execution concurrency key.
6. [ ] Request normalization, request-local Repository Model compilation, and
   live eligibility remain before caller concurrency. Compilation or
   eligibility failure remains pre-Attempt.
7. [ ] `run-live-attempt`, the `uses`-only caller job, owns the concurrency
   group while the same-revision reusable Attempt runs from admission through
   finalization.
8. [ ] `cancel-in-progress` remains exactly `false`.
9. [ ] Derive the key as
   `canonical_sha256(derive_buddy_execution_identity(intent).to_document())`
   with only the leading `sha256:` removed. Use the repository's existing
   helpers; add no hash/key abstraction.
10. [ ] A successful real `release compile-live-model --github-output ...`
    invocation emits the 64-lowercase-hex `execution-concurrency-key` in
    addition to existing Repository Model outputs.
11. [ ] Remove the request-specific shell hash from the caller. Forward only
    the CLI output through `compile-model` ->
    `evaluate-live-eligibility` -> `run-live-attempt`, whose group remains
    `wdv3-execution-${{ ...execution-concurrency-key }}`.
12. [ ] Add no ledger, application lock, tag witness, service, credential,
    destination lock, or generalized abstraction.
13. [ ] Preserve the current live-disabled gate, permissions, action pins,
    artifact transport, DAG, reusable workflow behavior, and every unrelated
    behavior/test.
14. [ ] Tests are scenario-first and mutation-sensitive: exact same-target
    equality, different-target inequality, exact identity document/hash,
    successful real CLI output, exact workflow producer/forwarding chain,
    absence of the shell/request hash, and whole-reusable-job concurrency.
15. [ ] Later Plan/Implement phases append uniquely delimited sections to
    `.testagent/plan.md` and `.testagent/status.md`. Status must map each item
    above to named tests/files and record exact command results, discovery,
    lint/format/actionlint, append-prefix, and diff evidence. Do not rewrite
    prior report content.

GitHub concurrency is a best-effort equality-group execution mechanism, not a
distributed correctness lock. Local tests can pin identity/group derivation and
caller topology; they cannot emulate or strengthen GitHub's hosted scheduling
contract.

### Bounded target inventory

| Priority | Path / symbol | Testability | Current classification | Relevant dependency/observation |
|---|---|---:|---|---|
| High | `.github/workflows/workflow-delivery-v3-buddy-smoke.yml` (`compile-model`, `evaluate-live-eligibility`, `run-live-attempt`) | High | Partial | The DAG and `cancel-in-progress: false` are tested, but the exact key producer and forwarding chain are not. The compile shell currently hashes `request-id:GITHUB_SHA:buddy`, so same-target manual requests receive different groups. |
| High | `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py::_release_compile_live_model_command` | High | Untested directly | Successful compilation currently records only Repository Model digest outputs. It already imports `derive_buddy_execution_identity` and `canonical_sha256`. |
| High | `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | High | Missing live-compile key scenario | The adjacent simulation compile test supplies the canonical temp-repository/provider pattern and proves the Provider is not rerun. |
| High | `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py::test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact` | High | Partial | Parses the real caller YAML and already pins the five-job DAG, reusable `uses` job, group prefix, and no cancellation. Strengthen exact expressions and negative shell assertions. |
| Medium | `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/identity.py::derive_buddy_execution_identity` | High | Partial | Strictly validates the normalized first-slice live Intent and returns only channel, Release Unit, and target. |
| Medium | `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py::BuddyExecutionIdentity` / `to_document` | High | Partial | Frozen/slotted ordered value object; `to_document()` emits the canonical schema plus only `channel`, `release-unit`, and `target`. |
| Medium | `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py::test_buddy_request_normalization_and_execution_derivation_are_strict` | High | Partial | Existing direct Buddy derivation scenario; suitable for same-target/different-target and excluded-input identity assertions. |
| Low / dependency only | `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/canonical.py::canonical_sha256` | High | Substantial | Existing canonical RFC 8785 SHA-256 implementation returns `sha256:<64 lowercase hex>`; no production change is indicated. |
| Low / preserve | `src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py` | High | Substantial | Golden digest, insertion-order stability, prefix, and lowercase shape are already pinned. |

The reusable workflow
`.github/workflows/workflow-delivery-v3-live-attempt.yml` is read-only topology
context: the caller's `uses` job spans its complete execution. No callee change
is required or in scope.

### Dependency graph for targets only

- **Leaf mechanisms**:
  `canonical.py::canonical_sha256`; and
  `records/release.py::BuddyExecutionIdentity.to_document` plus its existing
  exact-value validation.
- **Mid layer**:
  `release/identity.py::derive_buddy_execution_identity(ReleaseIntent)`.
- **Top layer**:
  `cli.py::_release_compile_live_model_command`, which has the admitted
  normalized Intent and successful compiled Snapshot; then caller workflow
  output forwarding and the `run-live-attempt` concurrency group.
- No mock is needed for leaf identity/hash behavior. The CLI test should reuse
  existing canonical local fixtures and monkeypatch only the already
  established Provider-rerun boundary; workflow tests statically parse the
  authoritative YAML and make no external call.

### Canonical source-to-test pairing

| Source | Direct canonical test pair | Evidence/gap |
|---|---|---|
| `.github/workflows/workflow-delivery-v3-buddy-smoke.yml` | `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | Partial parsed-YAML contract; exact key source and three-hop forwarding are missing. |
| `.../three_workflow_delivery_v3/cli.py::_release_compile_live_model_command` | `src/public/lib/three-workflow-delivery-v3/tests/test_cli.py` | No direct `compile-live-model` success/output test; only the analogous simulation compiler is covered. |
| `.../release/identity.py::derive_buddy_execution_identity` and `.../records/release.py::BuddyExecutionIdentity` | `src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py` | One strict normalized-Intent derivation test; key invariance and target separation are missing. |
| `.../canonical.py::canonical_sha256` | `src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py` | Substantial direct golden/shape/order coverage; reuse rather than duplicate. |

The polyglot analyzer was run exactly once, at the narrowest package root, with
`--include-tested`:

```bash
python /workspace/three-workspaces/design-workflows/.agents/skills/find-untested-sources/scripts/find_untested_sources.py \
  /workspace/three-workspaces/design-workflows/src/public/lib/three-workflow-delivery-v3 \
  --lang python --include-tested
```

Result: 38 Python source files, 41 test files, 36 heuristically paired sources,
2 unpaired zero-declaration `__init__.py` files, and 0 orphan tests. All four
Python source files above appeared in `tested_sources`; in particular,
`identity.py` was paired to six files and `cli.py` to many broad integration
references. **Do not rerun this analyzer in later phases.** Its result is a
static parse/identifier heuristic, not line or branch coverage; broad
identifier overlap materially overstates coverage of the specific
`compile-live-model` handler.

### Existing test conventions and recommended scenarios

- Tests are module-level pytest scenarios with descriptive
  `test_<behavior>` names, bare exact assertions, `tmp_path`,
  `monkeypatch`, canonical bytes, and readable parameter IDs.
- Workflow contracts load the real YAML with `yaml.safe_load` and use
  `_document`, `_steps`, `_step`, `_run`, and exact whole-expression/mapping
  assertions. Keep the existing test and strengthen it instead of adding a
  string-only duplicate.
- CLI behavior is exercised through `cli_module.main([...])`, real parser
  dispatch, canonical temp files, and exact GitHub output file contents.
- Recommended scenario evidence:
  - strengthen
    `test_buddy_caller_dag_concurrency_and_reusable_boundary_are_exact` to pin
    the exact compile output, eligibility forwarding, final group expression,
    absence of `printf`/`sha256sum`/request-specific key construction, and the
    `uses`-only whole-Attempt boundary;
  - add a direct CLI scenario such as
    `test_compile_live_model_emits_canonical_buddy_execution_concurrency_key`;
  - expand or pair the strict Buddy identity scenario with three same-target
    Intents carrying different request/run/attempt/ref/actor facts and one
    different target. Require exact same-target key equality, different-target
    inequality, and the exact four-member canonical identity document
    (`schema` plus the three identity fields). Version, coordinate, and
    destination are structurally absent, not test-supplied key salts.
- Do not invoke GitHub, dispatch a workflow, publish a package, probe
  acceptance, alter Governance, or depend on timing.

### Exact commands

Run from the repository root. The static analyzer command above is historical
evidence and must not be run a second time.

- **Build**:
  `uv build --package three-workflow-delivery-v3`
- **Scoped fix-cycle tests**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py`
- **Affected-package test gate**:
  `GIT_LFS_SKIP_SMUDGE=1 python eng/scripts/hk_exec.py --timeout-seconds 720 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests`
- **Harness-equivalent discovery from repository root, no test path**:
  `uv run --python 3.13 pytest --collect-only -q`
- **Pyrefly**:
  `uv run --python 3.13 pyrefly check src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/identity.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/canonical.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py`
- **Ruff lint**:
  `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/identity.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/canonical.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py`
- **Ruff format check**:
  `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/identity.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/canonical.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py`
- **Actionlint through the repository HK wrapper**:
  `python eng/scripts/hk_actionlint.py .github/workflows/workflow-delivery-v3-buddy-smoke.yml`
- **Capture the full research prefix before Plan/Implement append work**:
  `cp .testagent/research.md /tmp/wdv3-buddy-concurrency-research-prefix.md`
- **Validate that captured append-only prefix afterward**:
  `python -c 'from pathlib import Path; prefix=Path("/tmp/wdv3-buddy-concurrency-research-prefix.md").read_bytes(); current=Path(".testagent/research.md").read_bytes(); assert current.startswith(prefix), "research.md prefix changed"'`
- **Validate the pre-research prefix retained by this append**:
  `python -c 'from pathlib import Path; import hashlib; prefix=Path(".testagent/research.md").read_bytes()[:247073]; assert len(prefix)==247073 and hashlib.sha256(prefix).hexdigest()=="64ab82657e5865817d91df5db3b3f5be6899f4aa05fe7496b9b5ef83cab7e5c2"'`
- **Whitespace/diff check for the complete bounded run**:
  `git --no-pager diff --check -- .github/workflows/workflow-delivery-v3-buddy-smoke.yml src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/identity.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/canonical.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py .testagent/research.md .testagent/plan.md .testagent/status.md`
- **Review only the bounded diff**:
  `git --no-pager diff -- .github/workflows/workflow-delivery-v3-buddy-smoke.yml src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/release/identity.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/release.py src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/canonical.py src/public/lib/three-workflow-delivery-v3/tests/test_cli.py src/public/lib/three-workflow-delivery-v3/tests/release/test_commit8_contracts.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py src/public/lib/three-workflow-delivery-v3/tests/test_canonical.py .testagent/research.md .testagent/plan.md .testagent/status.md`

### Existing dirty-worktree considerations

- Both `git status --short --untracked-files=all` and porcelain-v2 status were
  empty before this research append; there were no pre-existing dirty files to
  preserve or attribute.
- The only expected research-phase change is this tracked
  `.testagent/research.md` EOF append. Any other dirty path in a later phase is
  either that phase's bounded work or an external concurrent change; inspect
  and preserve it. Never restore, reconstruct, clean, or delete tracked files.
- Do not mutate `pyproject.toml`, `uv.lock`, package manifests, or lockfiles,
  and do not install packages for this repair.

### Risks and blockers

- The checked-in Python extension was available, but the extension skill
  itself remains unavailable; established pytest/YAML conventions supply the
  needed guidance.
- Static pairing reports `cli.py` as tested because many tests mention its
  declarations. That does not cover this handler; a successful real
  `compile-live-model --github-output` regression is mandatory.
- GitHub's hosted queue replacement cannot be integration-tested locally.
  Keep tests and status language to exact group equality, placement,
  `cancel-in-progress: false`, and the documented at-most-one-running/
  one-pending behavior; do not promise start order.
- A YAML syntax pass cannot prove the business identity. The CLI and pure
  identity scenarios must independently pin canonical bytes/hash inputs, while
  the workflow contract pins transport and caller ownership.
- The current downstream output forwarding is already mostly present; the
  defect is the shell-generated request-specific value. Avoid unrelated DAG,
  callee, eligibility, permission, or artifact changes.

<!-- END APPEND: 2026-08-19T20:01:25Z-wdv3-buddy-caller-held-release-execution-concurrency-repair-research -->

<!-- BEGIN APPEND: 2026-08-20T014646Z-wdv3-node-provider-lfs-regression-research -->

## Workflow Delivery v3 Node Provider LFS-smudge regression

### Scope and strategy

- **Strategy:** Direct. The request is bounded to one Python Provider and its
  canonical pytest module.
- **Production target:**
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/repository/node_provider.py`
- **Canonical test target:**
  `src/public/lib/three-workflow-delivery-v3/tests/repository/test_node_provider.py`
- No workflow, manifest, lockfile, global Git configuration, or unrelated
  source is in scope.

### Remote failure evidence supplied by the request

- PR: `#552`
- Workflow run: `32322124132`
- Job: `96286306051`
- Boundary: `Discover Node facts` / `Produce exact Provider Result`
- The workflow checkout succeeded. The Provider subsequently created its own
  exact-target repository, and `git checkout --detach <target>` attempted to
  smudge `src/private/app/OxfordDictExtractor/wordlist.tsv.zip`.
- GitHub LFS budget exhaustion made that internal checkout fail because the
  Provider did not supply `GIT_LFS_SKIP_SMUDGE=1`.

### Bounded implementation findings

- `_isolated_exact_target_repository` runs a local
  `git clone --no-local --no-checkout --no-tags`, restores the authoritative
  `origin`, then runs `git checkout --detach <target>`.
- `_run_command` delegates to `subprocess.run` without an explicit
  environment. Therefore the Provider cannot currently guarantee LFS smudge
  suppression when the ambient workflow environment omits it.
- `verify_exact_checkout` independently proves exact HEAD, non-shallow and
  complete ancestry/objects, fetches the exact authoritative tag refspec, and
  records credential non-persistence and the authoritative remote.
- Existing real-local-repository scenarios already establish the canonical
  fixture style: local-only remotes, a closed noninteractive Git/PNPM
  environment, detached exact targets, complete history and annotated plus
  lightweight tags, temporary-repository cleanup, and propagated command
  failures.
- The checked-in Python extension was read. The extension skill entry point
  was unavailable, so the base extension was read directly. This project uses
  pytest through the root UV workspace.

### Acceptance checklist

1. With ambient `GIT_LFS_SKIP_SMUDGE` absent, every Git subprocess operating
   on or creating the Provider's internal target repository receives the exact
   value `GIT_LFS_SKIP_SMUDGE=1`.
2. Adding suppression preserves every existing closed/offline Git and PNPM
   environment control, including blank credential helper, disabled prompts,
   local file transport, and denied HTTP/HTTPS/SSH transports.
3. The successful scenario remains bound to the exact requested detached
   target and exact NBGV commit.
4. Complete non-shallow history/object validation remains required.
5. Complete authoritative tags and the exact tag refspec remain required.
6. The internal repository retains the authoritative local `origin` URL.
7. Checkout evidence continues to report non-persisted credentials.
8. A distinct internal checkout failure remains a propagated `ValueError`
   caused by the original `CalledProcessError`; it is not swallowed, skipped,
   or allowed to reach PNPM/NBGV.
9. The regression uses only local repositories and subprocess observation. It
   does not call a network service, alter global Git/LFS configuration, weaken
   checkout validation, or depend on timing.
10. Production and workflow files remain unchanged for this test-only request.
    If the new scenario is red, report the precise remaining production
    failure rather than masking it with skip/xfail.
11. `.testagent/research.md`, `.testagent/plan.md`, and
    `.testagent/status.md` retain their captured byte prefixes.

### Planned test mechanism

- Reuse `_real_local_nbgv_repository` so the scenario exercises the public
  `provide_node_repository_facts` entry point with real local Git, PNPM, and
  installed NBGV behavior.
- Remove ambient `GIT_LFS_SKIP_SMUDGE`, then wrap `subprocess.run` only after
  fixture construction. Record a safe projection of the internal Git
  subprocess environment.
- Model the exhausted-LFS boundary at the exact internal detached-checkout
  subprocess: it fails unless that subprocess receives
  `GIT_LFS_SKIP_SMUDGE=1`.
- Parameterize the same scenario with an unrelated injected checkout failure
  to prove ordinary Git failures still propagate and block metadata
  evaluation.

### Commands

- Baseline canonical scenario:
  `GIT_LFS_SKIP_SMUDGE=1 uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/repository/test_node_provider.py::test_isolated_exact_target_materialization_preserves_source_and_cleans_up`
  — `5 passed`.
- Narrow generated regression:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/repository/test_node_provider.py::test_internal_exact_target_git_materialization_skips_lfs_smudge_in_closed_environment`
- Bounded lint/format:
  `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/repository/test_node_provider.py`
  and
  `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/repository/test_node_provider.py`
- Append-only prefix validation uses the three
  `/tmp/wdv3-node-lfs-*-prefix.md` snapshots captured before this append.

### Known pre-implementation blocker

The requested positive regression is expected to fail against the delivered
production tree: `_run_command` does not pass any explicit subprocess
environment, so the internal checkout cannot receive Provider-owned LFS
suppression when the ambient variable is absent. The user explicitly bounded
this run to regression tests and requested the remaining production failure;
no production repair is planned.

<!-- END APPEND: 2026-08-20T014646Z-wdv3-node-provider-lfs-regression-research -->

<!-- BEGIN APPEND: 2026-08-20T024000Z-wdv3-node-provider-lfs-regression-research-clarification -->

### Materialization-command clarification

The final regression applies the explicit environment assertion to
`git checkout --detach <target>`, the command that materializes the worktree
and triggered the observed LFS smudge. It separately pins the preceding clone
to `--no-checkout`; therefore that clone cannot become an unguarded
materializing command without failing the regression. Other internal Git
commands remain recorded and asserted for exact-target, history, tags, remote,
network, and global-configuration invariants, but are not over-constrained to
an explicit environment when they cannot smudge worktree content.

<!-- END APPEND: 2026-08-20T024000Z-wdv3-node-provider-lfs-regression-research-clarification -->

<!-- BEGIN APPEND: 2026-08-20T042004Z-pr552-codeql-closure-regression-research -->

## 2026-08-20 PR #552 CodeQL-Closure Regression-Test Research

### Project overview

- **Path**: `/workspace/three-workspaces/design-workflows`
- **Language**: Python 3.13 plus GitHub Actions YAML contracts
- **Test framework**: pytest 8.3.4+, PyYAML-backed structural contract tests
- **Configuration**: root `pyproject.toml` sets
  `--import-mode=importlib`; package `three-workflow-delivery-v3` exposes its
  pytest dependency and console entry point.
- **Authority**: the current workspace is authoritative. Missing source must
  not be restored.
- **Explicit exclusion**: Node Provider LFS repair commit `2c0c1c24` and all
  related source/tests are out of scope.

The mandatory Python/pytest guidance was already loaded. The supplied one-time
polyglot pairing result classified
`eng/scripts/workflow_delivery_v3_consumer_policy.py` as paired, with
`src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py`
among its covering tests. This is only a static identifier/import heuristic,
not line or branch coverage. Discovery was not rerun.

### Scope and edit boundary

Research is bounded to PR #552's test-only CodeQL-closure regressions. During
this research pass, no production Python, workflow YAML, or test was modified.
The only permitted research edit is this append to
`.testagent/research.md`.

Later test generation is limited to:

1. `src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py`
2. `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`
3. `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
4. `tests/test_workflow_release_control.py`
5. append-only test status documentation requested below

No production repair, workflow edit/deletion, suppression, configuration
change, alert dismissal, or semantic weakening is part of test generation.

### Requirement checklist

Each row is independently verifiable.

| ID | Requirement / verification |
|---|---|
| C1.1 | Add the bounded core-algorithm ReDoS regression in canonical `test_consumer_policy.py`; execute risky tokenization in a child process with a hard timeout so vulnerable code fails fast rather than hanging pytest. |
| C1.2 | Parameterize large unterminated payloads over double quote and single quote, and over command-argument tokenization and `bun.lock` tokenization (four independently identified cases). |
| C1.3 | Use escaped-plus-ordinary content that reaches the vulnerable `_TOKEN` branch overlap; require completion with no false consumer match. |
| C1.4 | Preserve well-formed escaped quoted-token behavior through both tokenization routes. |
| C1.5 | If a structural assertion is added, keep it to the narrow functional invariant that an unterminated quoted token cannot be reclassified as an ordinary token; do not pin the whole regex or scanner implementation. |
| C2.1 | In `test_commit10_acceptance_probes.py`, prove a qualified upstream HTTPS request receives closure-bound `expected_method` and `expected_path`, never mutable handler `self.command` or `self.path`. |
| C2.2 | Add an absolute-form request-target case and prove it is rejected locally before any upstream connection/request. |
| C2.3 | Prove a legal upstream response status, body, and non-hop-by-hop headers are relayed locally. |
| C2.4 | Independently cover CR and LF in an upstream response header name and in a header value (four cases). |
| C2.5 | Every illegal upstream-header case must yield local HTTP 502, `proxy.proof is None`, and `proxy.processed.is_set()` false. |
| C2.6 | Monkeypatch `cli_module.http.client.HTTPSConnection` in every proxy regression; no real upstream network is allowed. Loopback HTTP to the proxy is permitted. |
| C3.1 | Replace exactly the three test-only GitHub API substring classifications in the acceptance metadata fake: its two response branches and the `api_calls` filter. |
| C3.2 | Classification must use `urllib.parse.urlsplit(url)` with exact `scheme == "https"` and exact `netloc == "api.github.com"`; a host such as `api.github.com.example.invalid` must not count. |
| C3.3 | Eliminate the three test-only alerts without changing production transport behavior or adding a suppression. |
| C4.1 | In canonical v3 workflow tests, enumerate every `Check out exact selected target` step in `workflow-delivery-v3-live-attempt.yml` and require `with.ref == "${{ github.sha }}"`. |
| C4.2 | Prove `workflow-delivery-v3-buddy-smoke.yml` remains the sole `workflow_dispatch` workflow with a local call to the live-attempt workflow. |
| C4.3 | Prove the caller derives `target-sha` from `GITHUB_SHA`, forwards it unchanged through request/discovery/compile/eligibility outputs, and passes that value to the callee. |
| C4.4 | Before Release Attempt binding/upload and during publication, prove `inputs.target-sha` remains the same caller-bound target; preserve the ordering that binding precedes Attempt publication. |
| C4.5 | Include the Environment-protected `publish-github-packages` checkout in the same exact-ref assertion. |
| C4.6 | The checkout regression is intentionally red now: all 11 live-attempt exact-target checkouts currently use `${{ inputs.target-sha }}`. |
| C5.1 | Remove/replace only stale positive test `test_release_build_variant_runs_control_from_trusted_checkout`. |
| C5.2 | Add a negative regression that `.github/workflows/release-build-variant.yml` is absent. |
| C5.3 | Independently prove active `official.yml` and `release-orchestrate.yml` do not reference `release-build-variant.yml`. |
| C5.4 | The absence regression is intentionally red while the orphan file remains present; do not delete the YAML during test generation. |
| C6.1 | Tests/docs only: no suppressions, config changes, dismissals, production changes, workflow changes, or weakened assertions. |
| C7.1 | Run the narrow tests and lint after generation, retaining the intended red blockers rather than fixing production. |
| C7.2 | Append exact failing node IDs/omissions and a self-review to `.testagent/status.md`; do not rewrite its prior content. |
| C7.3 | Stop after test generation, red-blocker demonstration, lint, and status append. |

### Bounded target inventory

| Path | Role | Selected surface |
|---|---|---|
| `eng/scripts/workflow_delivery_v3_consumer_policy.py` | production, read-only | `_TOKEN`, `_lockfile`, `_arguments_reference`, `_manager_references`, scanner entry |
| `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py` | production, read-only | `AcceptanceMutationProxy`, `_LostResponseProxy`, `_AcceptanceNpmTransport` only |
| `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py` | production helper, read-only | `ValidatedAcceptanceRequestProof.from_validated_exchange` only |
| `src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py` | canonical test target | tokenization regressions |
| `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py` | canonical test target | proxy regressions and the three affected fake classifications |
| `.github/workflows/workflow-delivery-v3-live-attempt.yml` | contract source, read-only | exact-target checkouts, Attempt binding, publisher |
| `.github/workflows/workflow-delivery-v3-buddy-smoke.yml` | caller, read-only | sole local dispatch call and target-SHA flow |
| `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py` | canonical workflow test target | caller/callee topology and checkout contracts |
| `src/public/lib/three-workflow-delivery-v3/tests/contracts/test_commit11_legacy_buddy_retirement.py` | read-only convention | existing workflow inventory/trigger helpers |
| `.github/workflows/release-build-variant.yml` | stale contract source, read-only | expected-absent blocker |
| `.github/workflows/official.yml` | active entry, read-only | delegates to `release-orchestrate.yml` |
| `.github/workflows/release-orchestrate.yml` | active orchestrator, read-only | calls language-specific build workflows; no variant reference |
| `tests/test_workflow_release_control.py` | canonical release test target | replace stale positive structural test |
| `pyproject.toml`, package `pyproject.toml`, `package.json` | config, read-only | exact pytest/Ruff/command context |
| `.testagent/*.md` | append-only state | prior content preserved |

No other source tree, provider repair, or CodeQL configuration is in scope.

### Canonical source-to-test pairs

| Source/contract | Canonical test |
|---|---|
| `eng/scripts/workflow_delivery_v3_consumer_policy.py` | `.../tests/ci/test_consumer_policy.py` |
| `three_workflow_delivery_v3/cli.py` proxy and metadata seams; proof helper in `adapters/github_packages.py` | `.../tests/adapters/test_commit10_acceptance_probes.py` |
| `workflow-delivery-v3-live-attempt.yml` plus sole caller `workflow-delivery-v3-buddy-smoke.yml` | `.../tests/contracts/test_buddy_workflows.py` |
| `release-build-variant.yml` plus active `official.yml` -> `release-orchestrate.yml` topology | `tests/test_workflow_release_control.py` |

### Dependency graph

- **Leaf/core**:
  - `_TOKEN` tokenizes both `_arguments_reference` input and `bun.lock`.
  - Test-local exact GitHub API URL classification depends only on
    `urllib.parse.urlsplit`.
  - Parsed workflow documents and path existence checks have no in-scope
    production dependency.
- **Mid-layer**:
  - `_arguments_reference(arguments, command)` is reached through
    `_manager_references(code)`.
  - `_lockfile(path, content)` reaches `_TOKEN` for `bun.lock`.
  - `_AcceptanceNpmTransport.observe` uses the injected
    `_AcceptanceHttpTransport`.
  - `AcceptanceMutationProxy` validates a publish body, forwards through
    `HTTPSConnection`, and forms `ValidatedAcceptanceRequestProof`.
- **Top-layer/topology**:
  - `scan_consumer_policy` reaches command and lockfile scanners.
  - Buddy request -> discovery -> compile -> eligibility -> local reusable
    live Attempt.
  - Active release entry `official.yml` -> `release-orchestrate.yml` ->
    language-specific reusable build workflows. The generic variant workflow
    has no incoming active reference.

Leaf-first implementation should update the fake classifier and token tests
first, then proxy tests, then workflow/release topology contracts. Mock only
the HTTPS boundary; do not mock the pure token or YAML logic.

### Exact symbols and signatures

Consumer policy:

```python
_TOKEN: re.Pattern[str]
_lockfile(path: str, content: bytes) -> set[str]
_arguments_reference(arguments: str, command: str) -> bool
_manager_references(code: str) -> bool
scan_consumer_policy(repository_root: Path) -> ConsumerPolicyResult
```

Existing canonical helpers:

```python
_write(repository: Path, path: str, content: bytes | str) -> None
_repository(tmp_path: Path) -> tuple[Path, str]
_assert_consumer(repository: Path, path: str) -> None
```

Proxy/transport boundaries:

```python
AcceptanceMutationProxy(
    *,
    timeout_seconds: float,
    token: str,
    incoming_dummy_token: str | None = None,
    expected_method: str,
    expected_path: str,
    expected_version: str = "0.0.0-wdv3-acceptance.4",
    expected_tag: str = "wdv3-acceptance-4",
    expected_tarballs: tuple[bytes, ...] = (),
    expected_target_sha: str | None = None,
    expected_requests: int = 1,
    drop_accepted_response: bool = True,
    deadline: float | None = None,
) -> None

_LostResponseProxy(AcceptanceMutationProxy)

_AcceptanceNpmTransport(
    npm_config: Path,
    *,
    token: str,
    target_sha: str,
) -> None

_AcceptanceNpmTransport.observe(
    package_coordinate: str,
    tag: str,
    *,
    timeout_seconds: float,
    max_response_bytes: int,
    deadline: float | None = None,
) -> dict[str, object]

_AcceptanceHttpTransport.get(
    url: str,
    *,
    headers: tuple[tuple[str, str], ...],
    timeout: float,
    max_bytes: int,
) -> GitHubPackagesHttpResponse

ValidatedAcceptanceRequestProof.from_validated_exchange(
    *,
    raw_request: bytes,
    tarball: bytes,
    package_coordinate: str,
    tag: str,
    upstream_status: int,
    selected_headers: dict[str, str],
    response_body: bytes,
) -> ValidatedAcceptanceRequestProof
```

Injected fake HTTPS objects must retain the existing shape:

```python
Connection(host: str, *, timeout: float)
Connection.request(
    method: str,
    path: str,
    *,
    body: bytes,
    headers: dict[str, str],
) -> None
Connection.getresponse() -> Response
Connection.close() -> None
Response.status: int
Response.read(size: int) -> bytes
Response.getheaders() -> list[tuple[str, str]]
```

Existing YAML helpers are `_document(path)`, `_needs(job)`, `_steps(job)`,
`_step(job, name)`, and `_run(step)`. Release helpers are `_workflow(name)` and
`_workflow_yaml(name)`. Continue handling PyYAML's YAML-1.1 `on` key with
`document.get("on", document.get(True))`.

### Current behavior and intentional blockers

1. `_TOKEN` is currently
   `"(?:\\.|[^"])*"|'(?:\\.|[^'])*'|[^\s]+`. Backslashes overlap the escaped
   and ordinary quoted branches, and quotes remain admissible to the unquoted
   fallback. A bounded isolated probe showed all four
   route/quote combinations exceeded 0.75 seconds at 25 `\a` pairs, while 20
   pairs completed in roughly 0.27-0.31 seconds. Never execute the large
   vulnerable payload in the pytest process.
2. `AcceptanceMutationProxy.Handler._forward` qualifies against closure values
   but calls `HTTPSConnection.request(self.command, self.path, ...)`.
   It also forms proof and sets `processed` before relaying upstream headers,
   with no CR/LF rejection.
3. The three test-only substring classifications are the two
   `MetadataTransport.get` branches and the `api_calls` list filter in
   `test_acceptance_observation_requires_authenticated_github_package_version_metadata`.
4. The live-attempt workflow has 11 exact-target checkout steps, in jobs
   `admit`, `plan-qualification`, `build-tarball`, `project-test`,
   `npm-artifact-qualification`, `qualification-finalizer`,
   `observe-github-packages`, `materialize-publication`,
   `approval-finalizer`, `publish-github-packages`, and
   `release-finalizer`. Every one currently uses
   `${{ inputs.target-sha }}`. The publisher is protected by environment
   `workflow-delivery-v3-buddy-smoke-github-packages`.
5. The sole local caller found is
   `workflow-delivery-v3-buddy-smoke.yml`; it is `workflow_dispatch` only.
   Its four pre-Attempt checkouts already use `${{ github.sha }}`. It emits
   `target-sha=${GITHUB_SHA}`, forwards that output through all four jobs, and
   passes the eligibility output to the callee. Callee Attempt binding and
   publication both use `${{ inputs.target-sha }}`.
6. `release-build-variant.yml` currently exists. Neither active
   `official.yml` nor active `release-orchestrate.yml` references it.
   `official.yml` calls `release-orchestrate.yml`; the orchestrator calls the
   concrete dotnet, Python, WXT, Node, and Ruby build workflows.

### Planned regression mechanics

#### Consumer tokenization

- Add a small test-local subprocess helper using `sys.executable`,
  `POLICY_IMPLEMENTATION_PATH`, and `subprocess.run(..., timeout=...)`.
- Use a payload substantially above the observed threshold, such as an opening
  quote followed by 64 `\a` pairs and no closing quote. Run each
  `route x quote` case in its own child.
- Command route: invoke `_manager_references("npm install " + payload)`.
  Bun route: invoke `_lockfile("bun.lock", payload.encode())`.
- Require normal completion and a false result. Convert `TimeoutExpired` to a
  focused assertion failure naming route and quote.
- Add positive controls with closed double/single quoted tokens containing an
  escaped quote and whitespace, followed by the exact package token. Exercise
  both routes and retain exact package detection.
- A narrow additional assertion may require `_TOKEN.fullmatch` to reject a
  small unterminated quoted token. Do not assert the complete regex string.

#### Acceptance proxy

- Reuse `_adversarial_publish_body`, loopback `HTTPConnection`, and nested fake
  upstream classes.
- For the closure test, capture the live nested handler through
  `proxy._server.RequestHandlerClass`, then have fake
  `HTTPSConnection.__init__` mutate that handler's `command` and `path` after
  qualification but before `.request`. Record `.request` arguments and require
  the original closure-bound `PUT` and fixed package path. This is behavioral,
  not a source-string assertion.
- Extend the existing method/path rejection matrix with an absolute-form URL;
  assert zero upstream calls, no proof, and no processed signal.
- Add a response-returning loopback helper that retains local status, headers,
  and body. With `drop_accepted_response=False`, prove safe upstream headers
  and body relay.
- Parameterize `"\r"`/`"\n"` across header name/value. For each fake 201
  response, require local 502 and no proof/processed signal.
- All upstream connections remain monkeypatched. Do not invoke npm or any real
  network for these regressions.

#### Exact GitHub API fake classification

- Parse each URL once with `urllib.parse.urlsplit`.
- Define API classification solely as exact HTTPS scheme plus exact
  `api.github.com` netloc; use it in both fake response branches and the
  post-call filter.
- Make the fake tarball URL a lookalike such as
  `https://api.github.com.example.invalid/tar.tgz`; assert it is served as the
  tarball and excluded from `api_calls`.
- After editing, the three substring predicates must be absent. URL literals
  elsewhere are not alerts and are not in this repair scope.

#### Workflow contracts

- Collect exact-target checkouts by job and require the expected 11-job set.
  Require the same checkout settings with `ref: ${{ github.sha }}` for each,
  explicitly including the Environment-protected publisher.
- Locate all local callers of the exact callee path across workflow YAML,
  require the singleton Buddy caller, then assert its exact dispatch trigger
  and local `uses` form.
- Assert the caller target chain:
  `steps.request.outputs.target-sha` -> request output -> discovery output ->
  compile output -> eligibility output -> callee `with.target-sha`, rooted in
  `GITHUB_SHA`.
- In the callee, assert `admit.outputs.target-sha ==
  "${{ inputs.target-sha }}"`, bind uses that target before Attempt upload, and
  the publisher uses the same target. Do not broaden assertions to unrelated
  workflow implementation text.

#### Release topology

- Replace the stale positive function with:
  1. one absence test for `release-build-variant.yml`; and
  2. a parameterized active-orchestrator no-reference test for `official.yml`
     and `release-orchestrate.yml`.
- Keep absence and no-reference checks separate so both facts remain
  independently observable while the absence case is intentionally red.

### Existing tests and coverage classification

| Target | Existing evidence | Classification for this scope |
|---|---|---|
| Consumer tokenization | command-family and basic `bun.lock` positives | **Partial**: no unterminated escaped payload, bounded-time, or branch-overlap regression |
| Acceptance proxy | method/path mismatch, auth injection, publish-body qualification, status/proof tests | **Partial**: no post-qualification handler mutation, absolute-form case, or response-header CR/LF failure contract |
| Metadata fake | exact metadata/auth happy path | **Partial**: three substring predicates and no lookalike-host control |
| Buddy/live workflow | extensive DAG, permissions, environment, caller-path, and action-pin contracts | **Partial**: no all-checkout reusable-workflow revision assertion or complete target-flow equality test |
| Release variant topology | positive structural test of the orphan workflow | **Contradictory/stale**: no expected-absence test; active orchestrators already have no reference |

No numeric coverage percentage is claimed.

### Existing conventions and helpers

- pytest bare assertions, `pytest.mark.parametrize` with descriptive IDs, and
  focused `pytest.raises`.
- Dynamic policy import via `_load_policy`; temporary Git repositories via
  `_repository` and `_write`.
- Nested fake response/connection classes and `monkeypatch.setattr` at the
  `HTTPSConnection` seam.
- Loopback-only HTTP for proxy tests; no external network.
- Immutable proof assertions include status, body/header identity, `proof`,
  and `processed`, not merely truthiness.
- YAML tests parse contracts and use `_step`/`_run` instead of broad raw-text
  snapshots.
- Release workflow tests use explicit active workflow names rather than
  inventorying unrelated workflows.

### Build, test, discovery, and lint commands

- **Build**: not required for this test/docs-only scope.
- **Consumer regressions**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py -k 'token and (unterminated or escaped)'`
- **Proxy plus fake regressions**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py -k 'closure_bound or absolute_form or upstream_response_header or authenticated_github_package_version_metadata'`
- **Workflow contracts**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py -k 'exact_target_checkouts or only_dispatch_same_commit or target_sha_stays_bound'`
- **Release topology**:
  `uv run --python 3.13 pytest -q tests/test_workflow_release_control.py -k 'release_build_variant'`
- **Scoped v3 collection**:
  `uv run --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py`
- **Harness-equivalent root discovery**:
  `uv run --python 3.13 pytest --collect-only -q`
  Root `testpaths` includes the v3 package tests but excludes
  `tests/test_workflow_release_control.py`; therefore also run
  `uv run --python 3.13 pytest --collect-only -q tests/test_workflow_release_control.py -k 'release_build_variant'`
  to verify the root release node explicitly.
- **Ruff**:
  `uv run --python 3.13 ruff check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py tests/test_workflow_release_control.py`
- **Format check**:
  `uv run --python 3.13 ruff format --check --force-exclude -- src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py tests/test_workflow_release_control.py`
- **Optional focused type check**:
  `uv run --python 3.13 pyrefly check src/public/lib/three-workflow-delivery-v3/tests/ci/test_consumer_policy.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/contracts/test_buddy_workflows.py tests/test_workflow_release_control.py`
- **Whitespace**: `git --no-pager diff --check`
- **Three-alert predicate check**:
  `rg -n '"api\.github\.com" in url|"api\.github\.com" in call\[0\]' src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`
  must return no matches.

`package.json`'s recursive pnpm test/lint scripts are not relevant to these
Python/YAML contract tests.

### Expected red blockers and required status append

After generation, retain and report the exact failures caused by:

1. vulnerable `_TOKEN` timeout/reclassification;
2. mutable handler method/path reaching upstream and unsafe response-header
   acceptance;
3. 11 live-attempt checkout refs still using `inputs.target-sha`; and
4. existing `release-build-variant.yml`.

Passing controls should include exact URL lookalike classification, absolute
request rejection, legal response-header relay, caller target binding, and
active-orchestrator non-reference.

Append to `.testagent/status.md`:

- exact commands, pass/fail counts, and failing node IDs;
- each intentional production/workflow/file omission;
- confirmation of no real network and no new suppression/config/dismissal;
- confirmation that only the three fake predicates were changed;
- self-review for child-process cleanup, timeout boundedness, assertion depth,
  exact scheme/netloc matching, all 11 checkout jobs including publisher, and
  independent release absence/reference assertions.

Then stop. Do not make the intentionally red tests pass by changing production
Python, workflow YAML, deleting the stale workflow, or restoring any source.

<!-- END APPEND: 2026-08-20T042004Z-pr552-codeql-closure-regression-research -->
<!-- BEGIN APPEND: 2026-08-21-wdv3-precoexistence-bootstrap-research -->

# Workflow Delivery v3 Pre-Coexistence Bootstrap Projection Research

## Bounded target inventory

- `.github/workflows/workflow-delivery-v3-ci.yml`: the shadow Finalizer command
  currently propagates every canonical negative Decision as job failure.
- `three_workflow_delivery_v3.ci.finalizer`: owns typed non-authoritative CI
  Decision policy and is the narrow location for a pure bootstrap predicate.
- `three_workflow_delivery_v3.cli`: already admits canonical Plans and lane
  results and provides Git-bound workflow commands.
- `tests/contracts/test_ci_workflow.py`: locks workflow topology, Finalizer
  transport, and the missing-Decision terminal fallback.
- `tests/ci/test_scenarios.py`: implements literal first-slice LLD scenarios.
- `tests/test_cli.py`: has real temporary-Git fixtures and canonical CI command
  tests.

## Existing conventions

- Scenario tests exercise complete typed Plans, lane results, and Decisions.
- Workflow tests compare exact YAML conditions and command fragments.
- CLI tests use canonical JSON, real local Git repositories where Git behavior
  matters, and `main(argv)` return codes without broad exception handling.
- A blocked Plan emits no selected obligations or admitted Evidence and closes
  as `failure` / `incomplete-model-plan` /
  `fix-model-plan-and-rerun`.
- Project-test failure is a distinct canonical lane failure and must remain
  red.

## Acceptance checklist

- [ ] Preserve Finalizer Decision and summary bytes and its nonzero exit.
- [ ] Allow projection only for pull requests whose exact base commit lacks
      `.github/workflows/workflow-delivery-v3-ci.yml`.
- [ ] Bind exact event base, head, tested merge, and request number.
- [ ] Require a canonical failure Decision with
      `incomplete-model-plan` / `fix-model-plan-and-rerun`.
- [ ] Require zero selected obligations, Evidence, artifacts, and selected
      first-slice scope.
- [ ] Require one or more diagnostics, all exactly
      `changed path is unclassified: <changed-path>`.
- [ ] Reject a base that already contains the marker, supersession, manual
      validation, lane failure, incomplete/success Decisions, mixed
      diagnostics, malformed or missing records, and Git/transport failures.
- [ ] Never hardcode PR #552, a branch, or a commit SHA.
- [ ] Append an explicit bootstrap note without changing canonical records.
- [ ] Keep the existing missing-Decision fallback terminal and last.
- [ ] Perform no acceptance, activation, publication, package mutation, or
      merge action.

## Implementation boundary

Use a pure typed predicate plus a separate `ci project-bootstrap-shadow`
command. The workflow captures `ci finalize` status and invokes the projection
only after a pull-request failure. Manual failure returns the original status.
The projection command re-admits the Plan, Decision, and summary and performs
the exact base-tree marker probe. It does not change Finalizer behavior.

<!-- END APPEND: 2026-08-21-wdv3-precoexistence-bootstrap-research -->

<!-- BEGIN APPEND: 2026-08-26-wdv3-acceptance-proof-repair -->

## Workflow Delivery v3 Acceptance Proof Repair

### Acceptance checklist

1. Propagate the proxy-validated normal-path HTTP 201 proof.
2. Require proof, admitted startedness, and exact post-readback for new normal
   completion; package existence alone is insufficient.
3. Keep proof-free create and mismatched or missing readback fail-closed.
4. Preserve existing lost-response reconciliation and historical evidence.
5. Retain only optional, bounded, credential-free runner diagnostics.
6. Do not add a retry profile, coordinate, workflow, Environment, package
   operation, or Live authority.

### Bounded inventory

- `cli.py`: the proxy formed a validated HTTP 201 proof, but the normal runner
  path discarded it and reduced the result to npm process output.
- `adapters/github_packages.py`: normal proof-free `created` plus exact
  readback was complete; lost-response already required proof and exact
  readback.
- `records/governance.py`: historical proof-free evidence had to remain
  replayable while admitting the new proof-bound outcome.
- Existing transport and tarball inspection already validate the exact
  manifest, repository, tag, bytes, and target witness. The Adapter must trust
  that lower-level boundary rather than duplicate it.

<!-- END APPEND: 2026-08-26-wdv3-acceptance-proof-repair -->

<!-- BEGIN APPEND: 2026-08-26-wdv3-acceptance-retry-3-fallback -->

## Retry-3 fallback test phase research

Bounded targets were the retry-profile additions in
`adapters/github_packages.py` and `records/governance.py`, the new retry-3
workflow, and the five requested test modules. Existing pytest/YAML contracts
use exact deep equality, controlled subprocess environments, parsed workflow
documents, and fail-closed exception assertions.

Acceptance inventory:

- Adapter profile: base/absent/exact `.9`, identical `.10`, differing `.11`,
  lost `.12`, with tags 9-12; legacy `.1`-.8 must remain unchanged.
- Governance profile: retry-3 workflow path and Environment, zero target,
  confirmation digest
  `sha256:33e59948941f5f1111d5017ab80dd33c90dd2ac8d1a17203e7f7382a8c5b2c72`,
  rejected-dispatch-only admission, and cross-profile/coordinate closure.
- Workflow: exact fixed inputs, five-job first-attempt DAG, fail-before-review
  zero sentinel, packages-write only in probe jobs, Node 24.19.0/npm 11.17.0,
  full action pins, terminal `always()` capture, optional
  `AcceptanceRunnerDiagnostic` reconstruction with canonical suite-digest
  equality, CODEOWNERS, and no Live/Release route.
- Retirement/topology: retry-3 is the sole temporary acceptance workflow;
  original/retry-2 remain absent and normal Buddy remains disabled.
- Scope: tests and append-only `.testagent` history only. No production,
  workflow, docs, CODEOWNERS, or `hk.pkl` edits were made by this phase.

The targeted pre-change discovery baseline was 681 tests across the four
existing requested files. Adding the new contract file and focused extensions
produced 704 discovered tests, a delta of 23.

<!-- END APPEND: 2026-08-26-wdv3-acceptance-retry-3-fallback -->

<!-- BEGIN APPEND: 2026-08-27-wdv3-acceptance-upstream-diagnostic-characterization-research -->

## Workflow Delivery v3 acceptance upstream-diagnostic characterization

### Boundary and strategy

This is one bounded, tests-first Python/pytest characterization pass. Production
behavior was read only from:

- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`;
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/adapters/github_packages.py`;
- `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/records/governance.py`.

The only test targets are:

- `tests/adapters/test_acceptance_exchange_proof_repair.py`;
- `tests/adapters/test_commit10_acceptance_probes.py`;
- `tests/governance/test_commit10_acceptance_evidence.py`.

The `code-testing-extensions` skill was requested once by the coordinator but
is not installed in this workspace. The established pytest conventions in the
three bounded test files therefore govern. No v1/v2 source, unrelated test,
manifest, workflow, Environment, package, Release, Live path, network service,
or dead-session event log is part of this research.

### Seven-item requirement checklist

1. [ ] The proxy captures the immutable, credential-free request digest and an
   exact HTTP status in the inclusive range 100..599 immediately after
   `getresponse()`. A 201 fact survives a later oversized-body, unsafe-header,
   or `response.read()` failure without fabricating a validated proof or
   processed authority.
2. [ ] A pre-response transport failure retains the request digest and exactly
   one closed transport category but no status. No exception message, request
   body, request/response header, token, stdout, or stderr may survive.
3. [ ] `_AcceptanceNpmRunner` propagates an optional closed upstream diagnostic
   on a returned failure and on raised timeout, `OSError`, and classification
   error paths; it omits the field/attribute when no admitted upstream fact is
   available.
4. [ ] The Adapter maps status diagnostics for 200, 201, 202, 409, and 500 plus
   a transport diagnostic while preserving action executedness, mutation
   startedness, result, mutation classification, and exact readback
   reconciliation. Diagnostic-only evidence remains incomplete and
   non-authoritative without a validated request proof.
5. [ ] Existing authority controls are rerun unchanged: validated normal 201
   completeness, exact preexisting no-mutation, identical-race exactness,
   differing-race conflict without overwrite, and Governance proof-required
   protocol confirmation.
6. [ ] Governance admits and exactly round-trips only canonical
   non-authoritative status/request and transport/request forms; it rejects
   malformed, unbound, or contradictory forms, and diagnostic-only facts
   cannot satisfy proof-required completion.
7. [ ] A diagnostic is scoped to a single request in the
   absent-create-readback/lost-response paths. One request retains at most one
   request-bound diagnostic; a two-request race never exposes one aggregate
   singleton diagnostic as authority for both requests.

### Current bounded behavior

- `AcceptanceMutationProxy` validates a request and appends a request fact
  before forwarding. It computes the request digest before
  `HTTPSConnection.request()`, but after `getresponse()` it reads and validates
  the response before retaining any standalone status/category diagnostic.
  Only a fully validated status-201 exchange creates
  `ValidatedAcceptanceRequestProof`. Oversized response bodies, unsafe response
  headers, `read()` errors, and pre-response transport exceptions therefore
  lose the requested bounded upstream facts. The common
  `OSError`/`TimeoutError`/`HTTPException` catch also loses their closed
  distinction.
- `_AcceptanceNpmRunner.run_scenario()` returns proof-derived facts only on the
  two proof paths. Generic returned failures and the timeout, `OSError`, and
  `_classify()` exception paths do not copy an optional proxy diagnostic into
  the returned document or raised exception.
- `AcceptanceRunnerDiagnostic` serializes exactly
  `exit-classification`, `upstream-status`, `exception-category`, and
  `request-correlation-digest`. Its present constraints allow status 201 only,
  and disallow a request-bound transport category. `_acceptance_runner_diagnostic`
  derives facts only from a validated proof or the local runner exception;
  it does not map an optional upstream diagnostic.
- Governance mirrors the proof-bound model: status is restricted to 201;
  a request/status fact must bind a proof; and an exception category cannot be
  request-bound. Proof-required completion already remains gated by proof,
  admitted startedness, and exact readback and must not be weakened.

### Existing conventions and reusable seams

- Tests use `pytest.mark.parametrize` with explicit, descriptive `ids`.
- Proxy tests replace `http.client.HTTPSConnection` with local fake
  Response/Connection classes and send only to the loopback proxy through
  `_request_proxy_publish`; no external URL is contacted.
- `_acceptance_tarball` and `_adversarial_publish_body` create a valid fixed
  package request, while fake Process/Proxy seams drive
  `_AcceptanceNpmRunner` deterministically.
- Adapter tests use `ScriptedTransport`, `ScriptedRunner`, `_absent`,
  `_exact_readback`, `_proof`, and `_run_probe`.
- Governance tests follow
  `_document -> mutate -> refresh record digest -> _admit -> exact
  to_document round-trip`. `_refresh_probe_record_digest_unchecked` is used
  when intentionally malformed or not-yet-admitted diagnostic values cannot
  be constructed through the current Adapter dataclass.
- Assertions use exact dictionaries/key sets, concrete state/results,
  startedness, proof absence, digest binding, and full serialized secret
  absence rather than existence-only checks.

### Source-to-test map and intended cases

- `cli.py::AcceptanceMutationProxy`:
  `test_proxy_retains_status_and_request_digest_when_201_response_validation_fails`
  (`oversized-body`, `unsafe-response-header`, `response-read-os-error`);
  `test_proxy_pre_response_transport_failure_retains_redacted_category_and_request_digest_without_status`
  (`timeout-error`, `os-error`, `http-exception`);
  `test_acceptance_proxy_one_request_retains_at_most_one_request_bound_diagnostic`
  (`status-100`, `status-409`, `status-599`,
  `pre-response-transport`);
  `test_acceptance_proxy_two_request_race_never_exposes_a_singleton_upstream_diagnostic`.
- `cli.py::_AcceptanceNpmRunner`:
  `test_runner_propagates_closed_upstream_diagnostic_for_returned_and_raised_failures`
  (`returned-failure`, `raised-timeout`, `raised-os-error`,
  `raised-classification-error`) and
  `test_runner_omits_upstream_diagnostic_when_proxy_supplies_no_admitted_fact`
  with the same path ids.
- `adapters/github_packages.py`:
  `test_acceptance_probe_preserves_non_authoritative_upstream_diagnostic_matrix_with_incomplete_readback`
  (`status-200`, `status-201`, `status-202`, `status-409`, `status-500`,
  `transport-http-exception`).
- `records/governance.py`:
  `test_governance_admits_and_round_trips_canonical_upstream_diagnostic`
  with the Adapter matrix ids;
  `test_governance_rejects_malformed_or_unbound_upstream_diagnostic`
  (`status-below-range`, `status-above-range`, `status-bool`,
  `status-without-request`, `transport-without-request`,
  `status-and-transport`, `request-without-status-or-transport`,
  `unknown-transport-category`, `malformed-request-digest`,
  `unknown-field`);
  `test_governance_proof_required_completion_rejects_diagnostic_only_authority`
  (`protocol-confirmed-complete`,
  `protocol-confirmed-readback-incomplete`).

The narrow run also reruns unchanged
`test_normal_create_propagates_request_bound_http_201_exchange_proof`,
`test_exact_preexisting_state_never_invokes_the_mutation_runner`,
`test_identical_conflict_race_is_exact_without_blind_repair`,
`test_differing_conflict_race_is_conflicting_without_overwrite`, and
`test_protocol_confirmed_governance_requires_validated_request_proof`.

### Validation contract

From the repository root, and only offline:

1. `uv run --offline --python 3.13 --package three-workflow-delivery-v3 pytest --collect-only -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_acceptance_exchange_proof_repair.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py`
2. `uv run --offline --python 3.13 --package three-workflow-delivery-v3 pytest -q src/public/lib/three-workflow-delivery-v3/tests/adapters/test_acceptance_exchange_proof_repair.py src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py src/public/lib/three-workflow-delivery-v3/tests/governance/test_commit10_acceptance_evidence.py`

Collection/import/syntax/fixture defects are harness failures and must be fixed
inside the three test files. Assertion failures or missing expected diagnostic
attributes/fields caused by the unchanged production model are intended
tests-first product-behavior reds and must not be skipped or repaired in
production during this phase.

<!-- END APPEND: 2026-08-27-wdv3-acceptance-upstream-diagnostic-characterization-research -->

<!-- BEGIN APPEND: 2026-08-27-wdv3-acceptance-upstream-diagnostic-production-research -->

## Workflow Delivery v3 upstream-diagnostic production repair research

### Bounded production scope

The tests-first characterization was followed by a production repair limited
to:

- `src/three_workflow_delivery_v3/cli.py`;
- `src/three_workflow_delivery_v3/adapters/github_packages.py`;
- `src/three_workflow_delivery_v3/records/governance.py`;
- the same three characterization test modules.

Paths above are relative to the v3 package root. No workflow, Environment,
release route, Live activation, external network operation, or additional
acceptance invocation is part of this repair.

### Compatibility model

The admitted diagnostic model is a closed compatibility union:

| Arm | Upstream status | Exception category | Request correlation |
|---|---:|---|---|
| Historical local runner | `None` | `TimeoutError`, `OSError`, `RuntimeError`, or `ValueError` | `None` |
| Request-bound transport | `None` | `TimeoutError`, `OSError`, or `HTTPException` | Required |
| Request-bound response | `100..599` | `None` | Required |

Additional closure rules:

- an all-null diagnostic is invalid;
- status and exception category cannot coexist;
- request-only diagnostics are invalid;
- unbound `HTTPException` is invalid;
- request-bound `RuntimeError` and `ValueError` are invalid;
- diagnostics are observability only and do not create completion authority;
- `exit-classification == "protocol-confirmed"` independently requires an
  admitted validated proof;
- historical status-only replay remains possible only where an adjacent
  admitted proof supplies the exact request/status binding.

### False-positive adjudication

The initial negative matrix treated requestless `TimeoutError` as invalid.
That expectation was adjudicated false: historical local runner diagnostics
already use requestless `TimeoutError` and `OSError`, and removing that arm
would break stored evidence replay. The unbound transport negative now uses
`HTTPException`, while request-bound `RuntimeError` and `ValueError` provide
the complementary invalid-category cases.

### Implementation findings

- The proxy must publish the first retained diagnostic atomically. A plain
  check-then-set can let concurrent handlers overwrite the singleton fact.
- A runner timeout can race a qualified handler that has observed a request
  but has not yet published its terminal status/transport fact. The runner
  therefore needs a bounded wait on a terminal event using the existing
  absolute operation deadline, not a new timeout.
- Raw Adapter diagnostics must not suppress local fallback unless they form a
  complete request-bound status or transport arm.
- A protocol-confirmed raw diagnostic must bind the admitted proof exactly.
  The lost-response complete path must reject any raw status/request facts
  that contradict the proof.
- Governance must reject the empty arm and require proof for a
  protocol-confirmed diagnostic even when the surrounding response result is
  not itself protocol-confirmed.

These findings preserve the original non-authority boundary: retained upstream
facts improve diagnosis but cannot prove package mutation completion.

<!-- END APPEND: 2026-08-27-wdv3-acceptance-upstream-diagnostic-production-research -->

<!-- BEGIN APPEND: 2026-08-27-wdv3-upstream-diagnostic-review-adjudication -->

## Independent review adjudication addendum

Fresh split-scope review produced four findings; four independent adjudicators
classified all four as true positives.

- A two-request proxy can never publish a meaningful aggregate singleton
  diagnostic. Its terminal event must therefore be set without waiting for a
  handler, or the runner can consume the shared deadline waiting for a fact
  that is intentionally impossible.
- A malformed raw upstream diagnostic remains fail-closed when it is the only
  source. When an independently admitted local runner exception exists, the
  malformed raw value must not suppress that historical local fallback.
- A request-correlation digest proves that the proxy observed a qualified
  request. Request-bound status or transport diagnostics therefore require
  both `action.executed` and `mutation-started`, represented by
  `runner-failed-after-mutation-start` for non-protocol failures.
- The Adapter diagnostic constructor must reject the all-null arm so every
  constructible serialized diagnostic remains within the Governance-admitted
  union. Absence is represented by no diagnostic object, not an empty object.

These refinements do not strengthen diagnostic facts into authority. They
close contradictions and replay failures within the existing observability
model.

<!-- END APPEND: 2026-08-27-wdv3-upstream-diagnostic-review-adjudication -->

<!-- BEGIN APPEND: 2026-08-27-wdv3-upstream-diagnostic-final-closure-research -->

## Final diagnostic-closure refinements

Later review iterations established four additional closure requirements:

- a validated response proof cannot coexist with a transport exception
  diagnostic for the same request;
- retry-2 and retry-3 complete lost-response evidence must bind the proof's
  request tarball SHA-512 to exact post-readback bytes, while retry-1 remains
  an explicit historical replay exception;
- the only constructible unbound status arm is historical status `201`;
  unbound non-201 statuses cannot be admitted in any Governance context;
- optional raw diagnostics use key omission, not explicit null. Every present
  returned value is validated even when malformed action facts create a
  synthetic runner error. Genuine raised exceptions with unadmitted
  startedness retain their prior diagnostic-omission behavior.

Together with the earlier union, these rules make Adapter construction,
returned-document admission, and Governance replay closed without converting
diagnostics into proof.

<!-- END APPEND: 2026-08-27-wdv3-upstream-diagnostic-final-closure-research -->

<!-- BEGIN APPEND: 2026-08-27-wdv3-acceptance-proxy-cardinality-race-research -->

## Acceptance proxy expected-one cardinality race

### Bounded target inventory

- Branch and starting HEAD were verified as
  `workflow-delivery-v3-acceptance-upstream-diagnostics` at
  `a52308c43c105b49f6a161325dbdf9f3d21086fa`, with a clean working tree.
- Production target:
  `src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/cli.py`,
  `AcceptanceMutationProxy._forward`.
- Sole test target:
  `src/public/lib/three-workflow-delivery-v3/tests/adapters/test_commit10_acceptance_probes.py`.
- Required operating guidance was read from the applicable `AGENTS.md` files
  and `docs/wiki/analyses/workflow-delivery/v3/agent-handoff.md`.

### Existing convention and defect

The module already uses `threading.Barrier`, two loopback HTTP client threads,
`_request_proxy_publish`, `_acceptance_tarball`,
`_adversarial_publish_body`, and a monkeypatched
`http.client.HTTPSConnection` fake for deterministic proxy races without an
external destination.

For `expected_requests=1`, request qualification counts matching retained
facts and then appends the new fact without one synchronization boundary.
Two identical qualified handlers can therefore both observe cardinality zero,
both append, and both reach the upstream fake.

### Acceptance checklist

1. Append one deterministic pytest regression to the existing canonical test
   module; preserve all prior tests.
2. Send two simultaneous identical, fully qualified requests to a proxy
   configured with `expected_requests=1`.
3. Use only the existing loopback proxy and a monkeypatched local upstream
   fake; perform no Live activation, real destination request, coordinate
   consumption/reuse, or external network access.
4. Force both current unsynchronized checks to observe the same empty
   snapshot with barriers, while allowing a future serialized critical
   section to proceed through a bounded broken-barrier fallback.
5. Assert exactly one upstream request, response statuses exactly `201` and
   local `409`, exactly one retained request fact, and no more than one
   correctly request-bound upstream diagnostic.
6. Run only the new pytest node, offline and without pytest or bytecode cache,
   and retain the intended race assertion failure as required tests-first red
   evidence.
7. Do not modify production code or repair the confirmed defect.

<!-- END APPEND: 2026-08-27-wdv3-acceptance-proxy-cardinality-race-research -->

<!-- BEGIN APPEND: 2026-08-27-wdv3-acceptance-proxy-cardinality-race-fix-research -->

## Acceptance proxy expected-one cardinality repair

An independent adjudicator classified the PR review finding as a true
positive with confidence 9/10. `ThreadingHTTPServer` can run two handlers
concurrently, while the prior matching-fact count and
`request_facts.append` formed an unsynchronized check-then-act boundary. The
GIL makes an individual list append safe but does not make that compound
cardinality invariant atomic.

The smallest compatible repair is a dedicated request-reservation lock held
only while counting already reserved matching tarballs and appending the new
request fact. Immutable tarball membership validation remains outside the
lock, and local rejection, the intentional two-request barrier, upstream
network I/O, response processing, proof construction, and diagnostic
publication all remain outside it. This preserves the existing
`expected_requests=2` race semantics while ensuring that a one-request proxy
cannot forward two simultaneous identical qualified requests.

The repair does not change diagnostic authority, historical replay, accepted
proof shapes, or any acceptance coordinate. It performs no Live activation
or destination operation.

<!-- END APPEND: 2026-08-27-wdv3-acceptance-proxy-cardinality-race-fix-research -->

<!-- BEGIN APPEND: 2026-08-27-wdv3-acceptance-proxy-cardinality-test-refinement-research -->

## Expected-one regression atomicity refinement

Fresh production review reported no findings. The independent test/evidence
review found that synchronizing list iteration could allow a count-only lock
with an outside-lock append to pass: the first handler could time out while
holding the count lock, release it, append, and let the second handler observe
the fact even though a preemption between release and append would retain the
real race.

An independent adjudicator classified this finding as a true positive with
confidence 10/10. The deterministic seam therefore moves from list iteration
to `request_facts.append`:

- without a lock, both handlers count zero and meet at append;
- with a count-only lock, the first releases the lock before waiting in
  append, so the second also counts zero and releases both appends;
- with the correct count-and-append critical section, the first waits while
  holding the lock, times out boundedly, appends, and the second then observes
  the consumed reservation.

The existing assertions consequently kill both the original unlocked
implementation and the count-only-lock mutant while retaining the correct
implementation.

<!-- END APPEND: 2026-08-27-wdv3-acceptance-proxy-cardinality-test-refinement-research -->

<!-- BEGIN APPEND: 2026-08-28-pr608-retry-4-terminal-fixed-identity-research -->

## PR #608 retry-4 terminal fixed-identity gap

At authoritative HEAD `127131db0f1f06817ace20d0249cf7dffa0d84e9`, the
retry-4 workflow contract statically inspects the embedded terminal Python but
does not execute its rejected-dispatch branch. The bounded gap is one
subprocess test in
`tests/contracts/test_commit10_acceptance_retry_4_workflow.py`.

Existing conventions provide `_terminal_python`, `sys.executable` subprocess
execution, `tmp_path`, bare exact assertions, and strict canonical admission.
The workflow/test constants fix the rejected identity to forty zeroes,
`@hcoona/hcoona-release-smoke-npm@0.0.0-wdv3-acceptance.13`,
`I_ACCEPT_DISPOSABLE_GITHUB_PACKAGES_PROBES_RETRY_4`, and digest
`sha256:b6f94d3c13c98b0714404959dd878230f8302ee849038a536f5a18cc3a85c7ec`.

Acceptance checklist: execute the exact extracted program with failed
validation, skipped downstream jobs, empty optional outputs, wrong dispatch
inputs, and fixed `WDV3_ACCEPTANCE_*` values; require successful canonical
write and admission; assert the complete rejected-dispatch identity and empty
mutation surfaces. This kills substitution of `INPUT_TARGET_SHA` in the
validation-failure branch. No production, workflow, or normative-document edit
is in scope.

<!-- END APPEND: 2026-08-28-pr608-retry-4-terminal-fixed-identity-research -->

<!-- BEGIN APPEND: 2026-08-28-wdv3-acceptance-retry-4-finalization-research -->

## Retry-4 protected-finalization target research

Preparation PR #608 rebase-merged as
`835b81be1ff0ba7aa0ec23c9a7b518d4ade3dfaa`. Fresh authenticated preflight
confirmed that exact `main`, green post-merge checks, no nonterminal Actions
runs, active retry-4 workflow identity with zero runs, absent acceptance refs,
absent Environment, and absent `.13` through `.16` package versions and npm
tags. Environment `workflow-delivery-v3-buddy-smoke-acceptance-retry-4` was
then created as ID `20772100445` with sole reviewer `hcoona` / `712433`,
`prevent_self_review: false`, and one custom branch policy for `main`; readback
still showed zero workflow runs and deployments.

The immutable reviewed target is the preparation merge SHA, not a finalization
commit. The bounded tests-first scope is:

- the retry-4 workflow contract, which must bind both workflow target
  literals to the preparation merge, execute the fixed guard against the real
  finalized constant, reject zero and wrong dispatch values, and retain fixed
  terminal identity after rejected validation;
- the Governance contract, which must bind the registered retry-4 profile to
  the preparation merge, preserve zero-sentinel rejected-dispatch admission,
  admit complete finalized evidence through the real registry, and keep
  cross-profile substitutions closed.

The only expected production gaps are the two zero workflow target literals
and the zero retry-4 Governance profile target. Historical retry-1 through
retry-3 profiles, `.13` through `.16` coordinates/tags, confirmation digest,
workflow/Environment identity, permissions, DAG, first-attempt guard, and
normal Live exclusion remain unchanged.

<!-- END APPEND: 2026-08-28-wdv3-acceptance-retry-4-finalization-research -->
