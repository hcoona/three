# Workflow Delivery v3 Implementation and Retirement Plan

## Purpose and Baseline

This plan turns the accepted [HLD](./high-level-design.md) and
[middle-level design](./middle-level-design.md) into bounded implementation and
caller transitions for [#984](https://github.com/hcoona/three/issues/984),
[#985](https://github.com/hcoona/three/issues/985) and
[#986](https://github.com/hcoona/three/issues/986). Implementers and reviewers use
it to identify what changes together and what establishes each completed journey.
It is a migration contract, not a progress ledger or an implementation claim.

The inventory describes tracked source at
[`67f294cfd864a6376c14bee4a9f2ed0b92af9552`][baseline]. Paths below are relative
to the repository root unless a table gives a prefix. Refresh the affected caller
search before implementation; additions to source do not become unclassified
exceptions. Source inspection establishes existing wiring, not live integration
acceptance. The [Wave](../../../../../docs/delivery-wave.md) and
[handoff](./agent-handoff.md) retain effects and evidence limits.

The [former migration plan][former-plan] remains available for existing runtime
and historical-evidence readers. Its campaign ordering, admission records and
old-format contracts do not prescribe this replacement. References in historical
records retain that immutable version.

## Owned Projects and Final Locations

Start with `src/private/app/workflow-delivery/WorkflowDelivery.csproj` and
`tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj`. Module names
in the MLD are namespaces/directories, not more projects. Implement directly
there; do not relocate the Python framework as an intermediate step.

| Existing project or surface                                                            | Treatment and actual consumer                                                                                                    | Atomic completion boundary                                                                                                                        |
| -------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- |
| `src/public/lib/three-workflow-delivery-v3/` Python package                            | Replace orchestration with the private C# application; existing workflows/CLI and self-tests are its consumers                   | Remove package, script entry point and Python test root after their last active callers switch or retire                                          |
| `src/private/app/workflow-delivery-v3-dotnet-provider/`                                | Merge native MSBuild fact extraction into the application's .NET module; repository analysis needs it                            | Switch provider callers and native fact transfer together; preserve unprivileged evaluation                                                       |
| `src/private/app/workflow-delivery-v3-nuget-consumer/` and its test project            | Merge native package consumption and useful protocol scenarios into the application/test project                                 | Preserve separate unprivileged consumer execution; project consolidation does not grant publisher credentials                                     |
| `src/private/app/workflow-delivery-v3-nuget-authority/` and its test project           | Retire with the bounded smoke-reference scanner; these are its native metadata authority and tests                               | Remove scanner imports, preparation, HK/CI/bootstrap callers, tool-only dependencies and these projects together                                  |
| Four `src/public/lib/hcoona-release-smoke-{npm,github-packages,python,ruby}/` projects | Keep real native package scenarios as `tests/private/app/workflow-delivery/fixtures/products/` children with the same leaf names | Move each product with its workflow, manifest/workspace membership, version inputs, quality declaration and consumers; retain package coordinates |
| `src/public/lib/nbgv-python/` and `src/sample/nbgv-hatch-demo/`                        | Keep public versioning component and its independent sample/backend consumers                                                    | Remove only the old control package's dependency edge; do not absorb the public component into the CLI                                            |
| V3 `docs/` and project README                                                          | Move current authorities and retained evidence to the final private application at final Python retirement                       | Update portals, `AGENTS.md`, family bindings and all current relative readers together; preserve immutable historical links                       |

Native fixture projects are product/consumer inputs, not extra control applications.
A package move may change native version/history inputs; it does not promise old/new
byte equality or authorize republishing an existing version. Inspect `dirs.proj`
traversal, workspace membership and root configuration inheritance when placing
fixtures; explicit fixture execution must not accidentally become an unrelated
root build/test target. No generic fixture discovery service is added.

## Runtime and CLI Consumers

The runtime prefix in this section is
`src/public/lib/three-workflow-delivery-v3/src/three_workflow_delivery_v3/`.
The installed `three-workflow-delivery-v3` entry point is `cli:main`; Python and
Ruby hosted workflows also invoke modules directly. Internal CLI/schema breaking
changes are allowed, so every row switches its callers rather than adding aliases.

| Existing source/entry family                                                                                                                | Keep, merge or retire                                                                                                | Replacement consumer/operation                                                                                      |
| ------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| `repository/`, `catalogs.py`; `repository provide-node`, `provide-dotnet`, `compile`, `validate-authoring`, `catalog`                       | Merge useful native readers/declarations; retire Provider/Model admission and digest catalog authority               | Repository analysis used by CI planning and Release preparation                                                     |
| `ci/`; `ci candidate`, `admit-payload`, `plan`, `node-adapter`, `lane-result`, `finalize`, `project-bootstrap-shadow`                       | Replace fixed slice admission/record closure with finite selection and required-result accounting                    | `ci plan`, `ci run`, `ci result`; remove bootstrap projection when its shadow caller ends                           |
| `adapters/{node,dotnet,python,ruby,ruby_project}.py`, native npm/Ruby helpers                                                               | Merge native build, inspect and consume operations; retain only helpers still needed to call native interfaces       | Shared ecosystem operations, not one process/project per conceptual stage                                           |
| `adapters/{github_packages,npmjs,nuget_github_packages,pypi,python_observation,ruby_registry,rubygems}.py` and bounded HTTP/process helpers | Merge native destination semantics and sanitization; retire smoke witnesses and policy-profile mirrors               | Release observation, upload-once and required verification                                                          |
| `release/` planning, qualification, observation, publication and finalizers; `release` and `release nuget` command trees                    | Replace record choreography with common Release state handling                                                       | `release prepare`, `release observe`, `release publish`, `release result`                                           |
| `python_cli.py`, `ruby_cli.py`, `ruby_operation_cli.py`                                                                                     | Merge ordinary build/quality/publication work into common commands; retire per-language hosted record pipelines      | Migrate the corresponding complete workflow caller, including its required consumer                                 |
| `records/`, `canonical.py`; `bind`, `export`, `admit-*`, `form-*`, `*-proof`, `marker`, terminal record transport                           | Retire recursive proof/admission machinery; retain only actual serialization and package-byte checks                 | Typed boundary payloads and native artifact identity; no historical-format reader in the new core                   |
| `governance/`, `release/*governance*`, `governance_git.py`; Governance/approval/sentinel commands                                           | Retire dynamic file admission, path-history revocation and platform approval re-proofs                               | Native publisher Environment and destination authorization configuration                                            |
| `release/static_reference_*.py`                                                                                                             | Retire with its entire scanner caller group                                                                          | No Release admission replacement; ordinary project dependency rules remain project/CI concerns                      |
| `acceptance/`, Ruby operation/configuration/ledger modules and their platform helpers                                                       | Retire closed campaign execution, capture and configuration entry points after separating reusable native mechanisms | Git-pinned evidence readers only; ordinary Release does not inherit bootstrap, dispatch, account or repair commands |

Direct acceptance entry points include `acceptance`/`acceptance.__main__`, npm
operator/probe/suite modules, NuGet preparation/probe/profile/operator/suite and
consumer modules, and Python bootstrap/native modules. The entire `acceptance/`
namespace has the same retirement rule; parser count is not the scope boundary.
Some ordinary workflows import its mechanisms today, so extract those native
operations before deleting it. Likewise, `ruby_operation_cli` mixes ordinary
publication with bootstrap/ledger duties; it is not retained wholesale.

## Workflow and Action Cutover Groups

All workflow paths below have prefix `.github/workflows/`; all old V3 workflow
filenames begin `workflow-delivery-v3-`. Keep one authoritative owner per migrated
journey. A temporary comparison run has no required-check or Release authority.
Retirement of a campaign caller never grants another operation.

| Existing workflow files                                                                              | Target treatment and order                                                                                                                                                   |
| ---------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `workflow-delivery-v3-ci.yml`                                                                        | Move Node fixture CI planning/execution/result into the common CI integration after native selection and package/check coverage pass; retire the slice shadow projection     |
| `workflow-delivery-v3-python-smoke.yml`                                                              | Separate its PR CI and manual Release responsibilities; switch PR work with the Python CI group, then Release preparation/publication as a complete later group              |
| `workflow-delivery-v3-ruby-ci.yml`                                                                   | Switch Ruby CI with its native facts/build/consumer group; retire duplicate PR triggers                                                                                      |
| `workflow-delivery-v3-official-simulate.yml`                                                         | Replace simulation record stages with common development dry run and npmjs observation; this is not an existing npmjs live-publication caller                                |
| `workflow-delivery-v3-buddy-smoke.yml`, `workflow-delivery-v3-live-attempt.yml`                      | Replace npm Buddy entry and reusable live implementation together, after common Release preparation and destination/result contracts                                         |
| `workflow-delivery-v3-nuget-buddy-smoke.yml`, `workflow-delivery-v3-nuget-live-attempt.yml`          | Replace NuGet entry and reusable live implementation with the consolidated native provider/consumer; preserve actual Windows and unprivileged-consumer boundaries            |
| `workflow-delivery-v3-ruby-smoke.yml`                                                                | Replace ordinary Ruby Release flow without carrying over operation claims, ledger phases or configuration continuation                                                       |
| `workflow-delivery-v3-native-npm-acceptance.yml`, `workflow-delivery-v3-native-nuget-acceptance.yml` | Retire closed native campaign dispatch surfaces; retain original source/run references in evidence                                                                           |
| `workflow-delivery-v3-nuget-fixtures.yml`, `workflow-delivery-v3-nuget-profile.yml`                  | Retire campaign provisioning/profile callers; keep only native fixtures/read mechanisms with ordinary consumers                                                              |
| `workflow-delivery-v3-bootstrap-python.yml`, `workflow-delivery-v3-ruby-bootstrap.yml`               | Retire completed bootstrap callers; preserve original provenance and spent operation limits                                                                                  |
| `ci.yml`                                                                                             | Keep repository-wide conformance and one control-application test owner; replace its old V3 Python/authority and NuGet reproducibility invocations with the new scoped tests |

The common CI integration is owned by `ci.yml`; use a reusable workflow only when
its actual runner/job boundary requires it. Do not create a second repository CI
owner. Each initial migration covers a declared product/check group, while the
existing owner remains for unmigrated groups. Cross-group consumers must be
accounted for before a selected group switches; unsupported closure blocks the
switch. Do not claim repository-wide graph coverage from a successful smoke group.

Under `.github/actions/`, retire
`workflow-delivery-v3-{python-record,ruby-record,ruby-operation-record}` with their
last producing/receiving workflow steps. Their upload/download/bind cycle has no
replacement action. Simplify or inline `workflow-delivery-v3-{python-setup,ruby-setup}`
where pinned native setup is still needed; remove Python control-package installation
without removing the product ecosystem toolchain.

Keep existing live workflow/Environment identities while they have destination
trust consumers. A renamed OIDC-bound workflow, changed publisher registration or
Environment setting is a separate configuration effect, not an incidental source
rename. This plan neither activates new live callers nor reopens spent campaigns.
Source implementation and credential-free validation may proceed under the Wave;
any required live acceptance or configuration transition needs its own grant.

## Root Tooling, Declarations and Record Readers

| Concrete surface                                                                                                                                                                            | Required treatment with its last affected caller                                                                                                                                                                                                                         |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `eng/scripts/workflow_delivery_v3_{static_reference,prepare_static_reference}.py`, `workflow_delivery_v3_static_reference_node.mjs`                                                         | Delete with scanner modules, the NuGet authority and their tests; remove `prepare:static-reference-authorities`/`check:static-reference-worktree`, dependent bootstrap/test preparation and both scanner HK steps                                                        |
| `eng/scripts/workflow_delivery_v3_hk.py`, `hk_exec.py`, `ci_scope.py`, `run_python_tests.py` and `tests/eng/` consumers                                                                     | Keep generic path selection, watchdog and Python workspace functions; remove V3 coupling with the relevant caller. Update `ci_scope.py` for the new application/tests and retired fixture paths; do not delete this independent root-CI owner as if it were the V3 graph |
| `eng/scripts/workflow_delivery_v3_native_npm.mjs`, `workflow_delivery_v3_run_created_epoch.py`                                                                                              | Keep native npm operations only if the new adapter calls them; retire run-created-time admission support after its actual users disappear                                                                                                                                |
| `eng/scripts/sync_python_version.py`, `.python-version`, `.config/uv/uv.toml`                                                                                                               | Keep repository Python/UV projections; remove only generated `_python_runtime.py` when the new adapter reads the native locked inputs                                                                                                                                    |
| `eng/scripts/sync_python_build_backend.py`, `eng/workflow-delivery/v3/python-build-constraints.txt`, generated `_python_build_backend.py`, `repository/python_backend.py`                   | Replace the script's old-core import and smoke-specific generated constants with native locked build-input handling in the Python integration; retain frozen build constraints where that native build/consumer still needs them                                         |
| `eng/workflow-delivery/v3/policies/hcoona-release-smoke-*.yml`                                                                                                                              | Retain channel/destination and actual quality selection in ordinary unit/adapter configuration; remove Governance paths, expiry and duplicated native identities; do not copy the old policy schema                                                                      |
| Four products' `workflow-delivery.release-unit.yml`, `workflow-delivery.quality.yml`, manifests and `version.json`                                                                          | Preserve explicit unit/quality ownership; update native entry paths and genuine shared version inputs with each fixture move; registered project-specific presets remain legitimate                                                                                      |
| `.github/workflow-delivery/{governance,configuration,requests,bootstrap,native}/`                                                                                                           | Retire runtime admission/closed operation documents when corresponding callers retire. Preserve current historical readers at pinned source; do not transplant them into the new app                                                                                     |
| `mise.toml`, `hk.pkl`, `pyproject.toml`, `uv.lock`, `package.json`, `pnpm-workspace.yaml`, `pnpm-lock.yaml`, `Directory.Packages.props`, native project locks, `dirs.proj`, `renovate.json` | Update task ownership, workspace membership, scanner-only dependencies, new CLI/test/native fixture inputs and dependency-update rules together; delete a dependency only after its last actual consumer is removed                                                      |
| `.editorconfig`, `.gitattributes`, `.gitignore`, `biome.jsonc`, `.github/CODEOWNERS`                                                                                                        | Reconcile old path selectors and generated/output ownership with each moved or retired surface                                                                                                                                                                           |
| `AGENTS.md`, `CONTRIBUTING.md`, `docs/README.md`, `docs/engineering/{hk-execution,workspaces}.md`, `docs/governance/record-families.yaml`, project READMEs/handoffs                         | Route actual commands, owners and current authorities to final paths atomically; preserve historical evidence and regenerate affected generated interfaces through their existing source                                                                                 |

The JSON/YAML directories above are not all merely dead files today: ordinary
workflows and historical evidence still read them. Deletion follows their caller
transition, while their admission model is already excluded from new design.
Do not delete version/lock/build constraints merely because a nearby trust record
is retired. Do not retain the old Python package just to supply generated constants.

## Concrete Integration Contracts

### Native Facts and First Supported Shapes

| Ecosystem | Native inputs and required initial coverage                                                                                                                    | Unsupported relevant shape                                                                                                    |
| --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| Node      | PNPM workspace membership and local dependency edges; package manifests/scripts; shared lock/toolchain consumers; NBGV project inputs; declared package output | Unknown workspace protocol or dynamic dependency/input relation that native metadata and explicit declarations cannot resolve |
| .NET      | Evaluated MSBuild projects/imports and `ProjectReference` per selected configuration/TFM/RID; CPM/lock inputs; declared package outputs and supporting tests   | Missing evaluation runner, unresolved conditional/import inputs or an unmodeled relevant dimension                            |
| Python    | UV workspace/project and local dependency facts; `pyproject.toml`, native lock/build-system inputs; wheel/sdist outputs and Git-free sdist build requirements  | Undeclared dynamic backend/source inputs or native metadata insufficient for affected closure                                 |
| Ruby      | Evaluated gemspec/native dependency facts, locked build/check inputs and explicit repository-local relations unavailable from native metadata                  | Dynamic gemspec behavior whose input/ownership closure is not bounded                                                         |

Analyze base and candidate as specified by the MLD. Native evaluation is product
execution and runs unprivileged. Initial graph scenarios must include real local
references and transitive consumers, not just the current reference-free smoke
projects. The fixtures do not define the limit of the repository model. Shared
lockfile changes may select all actual consumers; unknown scope never selects a
full-run fallback. New production-product onboarding remains outside this Wave.

Use Ubuntu runners for the initial Node/Python/Ruby groups and Windows for general
.NET evaluation/build/consumption, with explicit product variants taking precedence.
Tool versions come from `global.json`, `mise.toml`/`mise.lock` and native locks;
do not introduce a second toolchain authority in this plan.

### CLI and Boundary Payloads

The executable is `workflow-delivery`; these are internal command families with
JSON files only at actual process/job boundaries. Field spelling can be refined
with the first implementation, but producers and consumers must change together.
Do not build an envelope registry or independent schema version service.

| Command and caller                                          | Required inputs                                                                                         | Output consumed at the next real boundary                                                                                                          |
| ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `repository facts` on a required native runner              | Candidate or comparison revision, ecosystem, evaluation roots and dimensions                            | Revision, project ownership, local/shared-input relations, native dimensions, explicit coverage/errors                                             |
| `ci plan` in root CI planning                               | Candidate/comparison identities, changed paths and necessary native facts, supported group              | Selected project/target/check/dimensions, reasons, required/advisory flags, runner, prerequisites and unresolved scope                             |
| `ci run` in a selected executor                             | Selected work, candidate and required native inputs                                                     | Check/target/dimensions, actual status, native command result and output identities where consumed                                                 |
| `ci result` in collection                                   | Expected work and available candidate-bound results/native job conclusions                              | Required-check result and missing/failed/unknown reasons; no success from absent work                                                              |
| `release prepare` before write capability                   | Source/control revision, unit, channel, destination, live/dry-run mode                                  | Complete qualified original files with native coordinates/version, qualification results, applicable completion checks and maximum intended writes |
| `release observe` without product execution                 | Prepared request/files and scoped read access where needed                                              | Per-file absent/exact/conflict/unknown, native metadata/visibility, remaining required checks and proposed missing writes                          |
| `release publish` in the gated publisher                    | Prepared selected-control artifact, qualified input identities, maximum writes and native authorization | Fresh observation, actual attempted writes/raw command outcomes and bounded readback; stop further writes after ambiguity                          |
| `release verify` in unprivileged verification/consumer jobs | Prepared request/files, destination read access where required and selected native consumer checks      | Required remote observations and consumer results for the complete intended set                                                                    |
| `release result` in collection                              | Request, publisher/native execution facts, readback and applicable consumer results                     | Satisfied/unsatisfied/unknown with command abnormality and unresolved effects kept distinct                                                        |

In-process callers use the same typed operations without serializing each row.
Artifact references crossing jobs carry immutable artifact ID, producer/run and
integrity bindings; qualified file entries carry relative path, size and digest
for package comparison. These checks bind actual consumers, not approvals or a
chain of internal facts. A receiving consumer validates the actual download.

Dry run executes qualification and supported reads, reports hypothetical writes
and missing read authorization, and never enters a write gate. Official live ref
eligibility remains explicit; Buddy and development dry run may use development
refs. Do not preserve the old smoke-only `main` checks as the channel contract.

### Destination Completion and Remaining Native Limits

All destinations require complete native identity/version, required metadata and
visibility, original-byte comparison and local clean consumption. The following
properties determine additional remote consumer work. Exact-state and newly
published requests have the same applicable completion conditions.

| Existing destination/use         | Native completion conditions to preserve                                                                                                                          | Consumer integration                                                                                                                                                                             |
| -------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| GitHub Packages npm Buddy        | Scoped package/version, intended repository association/access and original tarball; native non-overwrite/duplicate semantics                                     | Clean registry-qualified npm install/import where scoped registry/authentication routing is part of the request; local tarball installation cannot establish that routing                        |
| npmjs Official simulation        | Public package/version metadata, applicable tag projection and original tarball or authoritative absence                                                          | Report read/qualification/hypothetical result only; no new live publication caller or remote-install claim is inferred from simulation                                                           |
| GitHub Packages NuGet Buddy      | Normalized package/version, intended owner/feed/repository and visibility, exact `.nupkg`                                                                         | Clean restore/build/invoke against the selected feed with isolated caches; preserve source mapping/read credentials needed to prove resolution                                                   |
| TestPyPI Buddy and PyPI Official | Complete intended wheel/sdist set, normalized version, index file/metadata identity, both original files and required visibility; supported exact-subset recovery | Keep native wheel and Git-free sdist clean consumption. Add index-based remote resolution when needed to prove file selection/index routing; readback-by-URL alone cannot satisfy that condition |
| GitHub Packages Ruby Buddy       | Owner feed, native gem name/version/platform, index visibility and exact original `.gem`; safe signed storage redirect behavior                                   | Clean feed-qualified install/require where authenticated owner-feed resolution is required; do not use local `.gem` installation as its evidence                                                 |
| RubyGems.org Official            | Native gem name/version/platform, native index/API metadata, visibility and exact original `.gem`                                                                 | Keep local install/require; require remote native consumption for an explicitly selected index/resolution condition rather than to repeat package-content validation                             |

Before changing each Release caller, name its concrete required consumer checks
and native limits in the integration change. Existing applicable consumer work
cannot be removed by classifying it as redundant without establishing which
property the remaining checks cover. Historical audits keep their original tests.
The table distinguishes the current mechanisms and required properties; it does
not assert new platform guarantees or itself enable any remote operation.

Use the existing bounded native read mechanisms as extraction inputs, not the old
Governance expiry/profile digests. Each integration closes endpoint/redirect and
credential handling, request/body limits, read deadline/poll bound, package-size
bounds and cancellation before it is callable. No missing lower-layer guarantee
may be replaced by an application reservation or probe ledger. A failed upload
still permits bounded read-only evaluation; it never permits blind resend or the
next file write after ambiguity.

For initial workflow wiring, request 45-day retention for required prepared CLI,
qualified packages and request/results, matching the existing workflow setting.
This is a workflow default, not a new policy expiry or platform-retention proof.
If required inputs expire or the effective platform limit cannot cover the chosen
operation, that operation cannot continue. Optional diagnostics may expire sooner.
A fresh original-revision recovery rebuilds; it does not need a permanent ledger.
Keep registry/package serialization over required verification, including remote
consumer jobs, with native concurrency and without automatic live cancellation.

## Behavior and Evidence Compatibility

Tests move by retained behavior, not by old class or chronology-named file. Paths
in the second column are existing scenario sources under the V3 `tests/` tree;
there is no requirement to port their old record fixtures or exact assertion form.
Native helper tests under `tests/private/app/workflow-delivery-v3-*` supply the
corresponding real integration scenarios.

| Required journey or failure                 | Existing scenario/evidence input                                                                          | Replacement acceptance                                                                                                                                       |
| ------------------------------------------- | --------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Meaningful CI selection                     | `repository/`, `ci/`, `tests/eng/test_ci_scope.py` at repository root                                     | Base/candidate ownership, local/transitive edges, shared inputs, deletion/rename and unknown scope; add graph scenarios that old smoke-only providers reject |
| Project quality and complete variants       | `ci/test_{rules,planner,scenarios,python,ruby}.py`                                                        | Required/advisory selection, explicit preset adoption, all selected variants and failed/missing/skipped results                                              |
| Native version and reproducibility          | `adapters/test_{node,dotnet,python,ruby}.py`, `release/test_nuget_native_recovery.py`                     | Correct native projections and same-input original bytes; no mandatory single NBGV invocation or cross-commit equality                                       |
| Complete packages and clean consumers       | Adapter and `release/test_*qualification.py` scenarios; native consumer tests                             | Native content/dependency rules and isolated complete-set consumption without witness/file-layout generalization                                             |
| Development dry run and channel eligibility | `release/test_eligibility.py`, simulation/live scenarios                                                  | Ordinary development refs for Buddy/dry run, eligible Official live refs, no dry-run write capability                                                        |
| Approval and input isolation                | Existing live qualification/publication and workflow scenarios                                            | Prepared intended control code, actual artifact binding and no product execution with publication capability; no replay of platform approval                 |
| Initially exact, absent, subset, conflict   | `release/test_exact_satisfied.py`, destination/observation/readback and Python partial-recovery scenarios | Same complete-state predicate; no approval when no write; only supported missing-file completion in a fresh request                                          |
| Lost response or upload error               | `release/test_{publication,python_publication,ruby_execution}.py`                                         | Add complete/incomplete readback cases; a handled command error may yield observed satisfaction, but stops all later writes                                  |
| Cancellation, missing result and recovery   | Attempt/finalizer/native recovery scenarios                                                               | Native failure remains visible, missing data does not prove non-start, fresh original-revision build/current authorization, terminal history unchanged       |
| Real transfers and retention                | Workflow/transport tests and retained validation records                                                  | Missing/substituted/expired required files block the operation; optional telemetry does not undo established satisfaction                                    |

No old passing suite proves the new graph or observation-based completion logic.
Use focused algorithm tests, journey scenarios and actual boundary integrations
as assigned by the MLD. Source comparison is useful only for retained semantics;
retired proof contracts are expected to differ. Windows remains the accepted
assumption, with ordinary changed-code validation rather than a new feasibility
gate. Local tests and fixture success are not new live destination acceptance.

## Dependency-Ordered Delivery

1. Introduce the final private CLI/test projects and native facts/selection core.
   Close the concrete fields above with the first caller, establish local-reference
   and quality scenarios, and give the application tests one root CI owner.
2. Switch complete CI groups in Node, .NET, Python and Ruby as their native facts,
   full selected variants and package/check contracts are implemented. Move each
   corresponding fixture atomically. Preserve outside-group CI ownership and
   reject unresolved cross-group closure; no fixed lane port or full-run fallback.
3. Implement complete common Release preparation and development dry run before
   privileged callers. Consolidate native provider/consumer helpers and the Python
   locked-build-input consumers with those integrations.
4. Switch ordinary destination callers one complete permission/transfer/result
   boundary at a time. Preserve native trust identities; make required consumer
   conditions, read limits and retention concrete before the change. Keep external
   configuration/live acceptance outside this source-only grant.
5. Retire closed campaign callers and obsolete admission/scanner groups with their
   last active consumer, extracting shared native behavior first. Campaign source
   needed by evidence readers is pinned in Git rather than ported as a second tool.
6. Remove remaining Python entrypoints, dependencies and workspace/test/task roots;
   move the project authority/evidence root directly to the final private location.
   Reconcile all root selectors, record bindings and documentation readers, then
   rerun affected native scenarios, HK, record checks and independent reviews.

The last two items may accompany earlier groups when their dependencies are
already closed. Do not preserve a retired framework merely to enforce this list's
numbering. Each delivery PR names the actual caller group, switched authority,
retained old consumers, validation and unresolved native effects. The contraction
is complete only when the Wave's implementation and retirement outcome is met;
accepting this plan alone does not complete #985, #986 or the Wave.

[baseline]: https://github.com/hcoona/three/tree/67f294cfd864a6376c14bee4a9f2ed0b92af9552
[former-plan]: https://github.com/hcoona/three/blob/67f294cfd864a6376c14bee4a9f2ed0b92af9552/src/public/lib/three-workflow-delivery-v3/docs/migration-strategy.md
