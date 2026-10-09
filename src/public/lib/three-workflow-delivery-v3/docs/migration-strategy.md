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
| npm `ci/{evidence,finalizer,planner,rules}.py`, `records/ci.py` and old `ci` command tree                                                   | Retired with the qualified root Node group, exclusive tests/goldens and bootstrap projection                         | Private application selection, execution and collection; no old schema aliases                                      |
| Shared `ci/path_admission.py`, Python/Ruby CI modules                                                                                       | Retain for their current Python/Ruby callers until those groups switch                                               | Migrate each complete ecosystem group with its actual consumers                                                     |
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
| `workflow-delivery-v3-ci.yml`                                                                        | Retired after ordinary hosted root Node group transfer/execution and retained-member qualification; root `ci.yml` owns those checks                                          |
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

For each new integration, first identify its maintained native library or
supported tool interface, the inputs/configuration it evaluates, the facts and
errors it exposes, and the required facts still unavailable. Review that concrete
boundary before implementation. Follow the HLD's
[native integration boundary](./high-level-design.md#native-integrations), including
native-language helpers and owner disposition when a necessary capability is
missing. Internal implementation and transfer formats have no compatibility
obligation. Rewrite producers and consumers together instead of adding aliases
or porting ecosystem parsing and resolution rules into C#.

The owner selected the supported PNPM CLI for the replacement application's Node
integration. Do not add a Node helper or embedded LibNode for that integration.
The [PNPM 12 assessment](./research/pnpm-native-planning.md) supplies source
evidence and the bounded local validation protocol for this choice. Selecting
the CLI does not accept an incomplete fact extraction recipe or native reader.
Use PNPM's workspace selection, structured resolved output and native
publish-directory metadata to obtain source project relations; reuse existing
declarations rather than maintaining a second dependency/output manifest. The
assessment's bounded directory-join follow-up tests that composition before a
reader is accepted. Workflow combines the base/candidate results and computes
impact with the other ecosystems and declarations. If a necessary fact cannot
be obtained, return the concrete gap to the owner rather than introducing
dependency parsing or changing the selected integration silently.

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

### Python Planning Integration and Later Caller Cutover

Accept the finite planning-fact contract before implementing its private-app auxiliary code.
Planning extraction is separate from Python preparation/build/check execution. It selects source
impact from native facts; it neither installs local build requirements nor proves successful
wheel/sdist consumers. The [source assessment](./research/python-native-planning.md) supplies its evidence
basis; this design owns the selection.

Retain the current root pytest testpaths, selected/full frozen package-preparation obligations,
tableless quality owners and root shared inputs through the existing implementation plan. Root
pytest does not require building all workspace packages. Preserve the smoke quality declaration's
wheel and sdist outputs, distribution-content checks, wheel install/import and Git-free sdist
build/install/import, with their actual prerequisites and variants. Do not duplicate inventories
or freeze current counts as architecture. Maintain project-scoped NBGV/plugin/template/generated
consumers even where a generated file is absent or current staged execution removes a plugin.

Current ci_scope.py declaration/name heuristics, generated constraints and retained adapter
no-config/no-sources choices identify execution consumers, not replacement native lock/default/
source semantics. Retire producers and consumers atomically only after accepting the execution,
check/prerequisite and transfer mapping and qualifying the implemented facts/callers. Release,
publication and spent campaigns remain excluded.

The [primitive native observation](./research/python-native-planning.md#primitive-native-observation)
is independently accepted and stopped; both unused diagnostic reserves expired.
It qualifies the pinned helper composition on its recorded Linux host, not complete
fact assembly or caller integration. Do not repeat that campaign.

Before later native qualification of complete fact assembly and callers, bind reviewed
helper/tool/field/source identities and concrete runtime preparation, then accept a separate
exact experiment protocol covering commands, isolation,
configuration/environment/cache/download effects, finite time/state, observations, stop and owned
cleanup/evidence. No later executable helper hash or protocol is selected here.
Keep a finite diagnostic buffer and stop at the first independently accepted success
or the exhausted/unsafe bounds. Repetitions within the accepted protocol need no
repeated owner approval.

#### Python Quality and Caller Mapping

Implement the [paired producer-to-owner projection](./middle-level-design.md#python-quality-owners-and-input-only-members)
before Python fact assembly or caller cutover. Root pytest configuration remains
the sole test-target declaration; native UV membership and dependency answers
remain the package graph. Read both revisions and preserve the following concrete
consumers currently expressed by `eng/scripts/ci_scope.py`. This table defines
migration responsibilities, not another package or test-target roster.

| Current consumer relation                                 | Replacement association and retained behavior                                                                                                                                                                                                                                           |
| --------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Native member source and local dependency consumers       | Resolve ownership over all native members, union both revisions' ordinary/build/group/quality relations, then map to actual configured member test or adopted package owners. Preserve removed edges and input-only intermediaries.                                                     |
| Root pytest `testpaths` and direct target inputs          | Query native pytest configuration and bind each explicit committed target to its endpoint. Changes within a configured target select its complete check; standalone repository targets have exact input consumers and no blanket root ownership.                                        |
| `ALL_INPUTS` and `PYTHON_INPUTS`                          | Retain root CI/toolchain, native workspace/lock/configuration, test configuration and Python preparation-script consumers for the actual retained Python target set. Keep source-input reasons and distinguish helper preparation from product build inputs.                            |
| Passive Python helper tests                               | Retain native helper source and maintained `nbgv-python` source as inputs to the configured passive test target. This does not turn the helper into a publishable project.                                                                                                              |
| Retained V3 Python tests and `_v3_input`                  | Preserve the existing concrete workflow, adapter, fixture, native helper, HK and configuration input consumers until the corresponding tests/callers retire. These relations provide test coverage, never Release authority.                                                            |
| Azure credential-provider Python tests and `_azure_input` | Retain source, .NET interoperability, engineering script, maintained plugin and Python configuration consumers. Keep its independent special-job ownership.                                                                                                                             |
| Maintained NBGV plugin tests                              | Retain required .NET tooling/configuration inputs as well as native member-source consumers. Native plugin coordinates and managed NBGV input discovery supply version/template/generated consumers, including absent generated files.                                                  |
| `tests/eng` source associations                           | Retain selector, Python runtime/version projection, HK wrapper and Node/.NET execution-script consumers; retain the exact typo-configuration and legacy-release-contract consumers. Derive target membership from native pytest configuration and retain cross-target script relations. |
| Adopted Python distribution checks                        | Preserve wheel/sdist build outputs, distribution contents, wheel install/import and Git-free sdist build/install/import, with their complete required variants and actual prerequisites. Keep these obligations separate from root pytest preparation.                                  |

Prepare root plus selected quality-member package contexts with native UV group
answers, retaining the current frozen selected/full preparation behavior. Do not
install unrelated workspace tools or force input-only members to build. Executors
receive the original candidate-bound selected work and required results, without
recomputing impact or changing native target selection. Keep the existing
legacy selection/preparation consumers until the replacement group is accepted
together. The [concrete native caller](../../../../private/app/workflow-delivery/README.md#native-python-ci-caller)
uses `run_python_ci_group.py` for root CI and committed local full; Windows scope
retains only comparison, other-job responsibility and the independent NuGet
reproducibility mapping. Smoke PR checks move to the adopted native distribution
preset while manual Release and current evidence readers remain.

Implement that group inside the existing private app using the
[native Python CI group contract](./middle-level-design.md#native-python-ci-group).
The Windows root scope caller transfers the exact event comparison and explicit
full/incremental mode plus complete candidate control outputs. The existing Linux
Python job materializes both endpoints, collects their native facts for its actual
Linux product interpreter, invokes shared selection once, then runs the finite
pytest and distribution partitions and collects against that original mixed plan.
Start native analysis independently of legacy Windows Python selection outputs;
an explainable empty affected scope performs no product preparation or execution.
Reuse the current process/artifact transfer pattern; no additional workflow, job,
public library or target roster is needed.

Switch root CI, explicit local full preparation and smoke PR checks as one
reviewed consumer group after controlled validation and ordinary PR CI qualify
their complete replacement. Preserve independent special-job ownership and
Release callers. Retirement then removes only the replaced Python selection,
preparation and constraint producers/consumers; retained ecosystem or historical
evidence readers continue to use their actual authorities.

Controlled validation must include a mixed-endpoint `A -> B -> C` closure through
input-only `B`, nested native ownership, changed/deleted producer edges, a known
input with no consumers versus unresolved scope, tableless quality owners,
native pytest endpoint/configuration/target errors and cancellation, root shared
inputs, selected package/group preparation, and every required distribution/result
transfer. The existing core nonempty selected-check contract remains intact.
Complete native collection/query qualification requires its own accepted exact
protocol using the actual current tools and lock format; the stopped primitive
campaign is not reused or repeated. Ordinary PR CI remains available for its
authorized caller-integration qualification after contract acceptance.

Implement the MLD's paired direct-reason transfer into the existing shared core
before full Python fact assembly. Keep both endpoint project sets actual; no
candidate-only owner proxy is inserted into basis facts. Controlled integration
must cover a basis-only input reaching candidate-only `C`, deleted quality owners,
candidate Release Unit siblings, all selected variants/checks/prerequisites and
initiating path/revision reasons. Reject foreign seed owners/revisions, reasons
outside the changed-path set and unrelated unresolved changes; preserve explicit
full mode and the existing nonempty selected-check/origin guards. Do not duplicate
shared relation/unit expansion inside Python or call `PlanSelected` with an
externally completed selection that skipped that expansion.

#### Native Python Package Execution Integration

Implement the [native package contract](./middle-level-design.md#native-python-package-execution)
in the existing private app after accepting its native source basis. C# owns
finite check expansion and orchestration; a minimal Python helper may call
maintained packaging, native installed metadata and standard archive interfaces
where needed. It does not parse UV lock/dependency/backend semantics. Preserve
the adopted project's payload/API expectations in its registered quality
implementation, separate from ordinary native distribution validity.

Reuse the committed root development/build-tool groups and maintained
`nbgv-python` plugin, with the MLD's phase-specific native frozen preparation
and build-dependency checking. Preserve declared workspace editability through
original distribution creation; reconcile the same tools to noneditable native
installation before Git-free original-archive consumption. Bind the repository-pinned official NBGV command
through the plugin's existing command selection, exact runtime and full relevant
Git inputs. Native backend builds may compute version again; agreement, rather
than a single invocation, is required. No generated backend roster, alternate
version injection, smoke witness or static-project rewrite is introduced.

Controlled validation covers complete declared outputs and version agreement,
failed/missing required checks, native preparation/build failure, installed
consumer origin, Git-free sdist invocation, cancellation and original result
transfer. Native qualification additionally observes actual selected frozen
installation, noneditable plugin independence, shared backend requirements,
wheel/sdist contents and both clean consumers using one concrete protected finite
protocol with a diagnostic buffer. Stop at the first accepted success. Source
findings alone do not establish those results.

Keep existing build-constraint generators, adapter constants, root pytest
preparation and Python workflow callers until their complete replacement
consumers are accepted and can retire atomically. Package implementation alone
does not qualify paired caller/cutover or migrate Release.

### Native .NET CI Group

This group replaces general .NET CI selection and execution using the current
Windows Debug operation. It does not migrate Release or test frameworks. Root CI
consumes the implemented group under ordinary changed-code Windows qualification
in [PR #1054](https://github.com/hcoona/three/pull/1054). The following contract and
eight qualification gates continue to govern changes to this operation.

#### Native Process and Endpoint Facts

Build the candidate private application completely before task use. Its native
locked restore, build and output-path query specify `dotnet` as the mise tool
argument, preserving configured SDK choices while restricting automatic missing-tool
installation to that control dependency. Other configured tools may remain in
the native environment; this is not isolation or a zero-network guarantee. Its
assembly supplies one public `ITask` entry loaded through `UsingTask` in an ordinary
endpoint-selected `dotnet msbuild` process. Use native `ProjectGraph` with explicit
entry/global properties. MSBuild owns SDK resolution, task/dependency loading,
evaluation and reference propagation. Do not introduce MSBuildLocator, a custom
ProjectCollection host/resolver, a helper application or an XML/item evaluator.
An unavailable native capability stops this operation.

Prepare each exact endpoint with its own `global.json` SDK and locked `dirs.proj`
restore using `Configuration=Debug`, `ContinuousIntegrationBuild=true` and
`RestoreLockedMode=true`. Candidate assets cannot substitute for basis facts;
there is no unlocked repair. Discover through unfiltered `dirs.proj`. Projection
task/import/output properties are wrapper inputs, not semantic graph globals.

The typed task request carries endpoint revision/root, traversal entry, operation
globals and private response destination. Its response carries native nodes,
edges, input-consumer rows, test capabilities and unavailable facts. Nodes retain
project path, complete globals, outer/inner distinction and native configuration,
TFM and RID. Node identity is project path plus sorted complete globals. Edges
refer to those identities, including native analyzer references. Input rows retain
path, role, observation stage and consuming nodes. Required unavailable facts
name the endpoint, project/node when known, role and reason; they are errors.

The CLI binds physical native paths to exact endpoint roots and committed Git
spelling. SDK/package/generated paths retain their environmental/producer roles.
The native workload autoimport locator associates a returned `Sdk` directory's
immediate parent pack only when that result actually imports its `AutoImport.props`.
Consume primary and additional paths without an installed-pack inventory or a
second workload resolver. Other SDK roots and unknown external hooks do not gain
that expansion; native provider identity/version do not claim an exact pack pin.
Control-build collection separately consumes the successful import's immediate
parent, unexpanded text and native evaluation identity. The selected SDK's literal
autoimport seeds exact file membership for observed connected imports in that
same evaluation and pack; no directory inventory or reconstructed SDK result
supplies missing membership.
Reject unsupported semantic globals containing endpoint-dependent coordinates;
do not invent path rewriting or AssemblyName/relocation aliases. Aggregate native
relations into business project-path IDs for the shared impact planner, retaining
full native identity for execution. Both-endpoint relation union handles removed
projects/references and rename coordinates; selected surviving projects expand
all supported candidate inner variants.

Retain NBGV's default `MSBuildTargetCaching`. The native
`NBGV_InnerProject` reference and the consuming project's validated restored NBGV
package pin identify its `build/PrivateP2PCaching.proj` helper. Adapt this concrete
tool dependency separately from business projects: leave the native graph
unchanged, omit the helper's internal identity and edges from portable business
facts, and preserve its observed input responsibilities for each business
consumer. Arbitrary external projects and unsupported helper overrides gain no
exception. The receiving native operation reconstructs local helper paths.
The [pinned NBGV source findings](./research/nbgv-native-inputs.md#native-msbuild-caching)
explain this boundary; source support does not qualify hosted execution.

#### Finite Input and Ownership Coverage

Evaluation supplies main projects/imports, native source/resource/AdditionalFiles/
Analyzer/XAML/manifest items and relevant icon/manifest properties, native graph
references and test capabilities, and Roslyn's existing/potential analyzer
configuration inputs. Locked restore supplies assets, CPM/lock and configuration
facts. Read `ProjectAssetsFile` through official NuGet `LockFileFormat` and consume
`PackageSpec.RestoreMetadata.ConfigFilePaths`. Require supported format, readable
populated native metadata, exact project identity and the current committed
`nuget.config` contributor. An empty model collection alone cannot establish
serialized presence. Do not parse configuration hierarchy or add a second JSON
reader. Native NBGV supplies actual configuration/version inputs; generated `obj`
outputs retain their native producers instead of becoming committed diff paths.

The root Traversal SDK project uses its own actual NuGet assets and generated
imports, with effective restored global-package pins checked against native assets.
Inactive declarations supply no provider; NuGet owns applicability.
Its package providers describe the restored closure without claiming a committed
traversal lock; the managed-project lock requirement is unchanged. Do not borrow
descendant assets or add a lock solely to fit a business-project validator.
Native version-input subjects bind effective `GitVersionBaseDirectory` to the
endpoint's committed directory and union business consumers across variants.
Traversal's unused self-version metadata does not add semantic product inputs.
Its genuine project/configuration/provider/helper inputs keep their native
reachable consumers. Business projects may still consume a root version subject.
These physical subjects are not transferred check globals. Quality selection remains at the
product directory. Unsupported external/repository-relative contexts fail.
Build and native inner `GetTargetPath` supply runnable output at their proper
stage; passive evaluation does not promise target-produced values.

The adapter populates the MLD's exact `OwnedPaths` from committed endpoint entries
in ordinary native project regions, narrowed by evaluated `DefaultItemExcludes`
and `DefaultItemExcludesInProjectFolder`. Match each native file spec through
selected-SDK `Microsoft.Build.Globbing.MSBuildGlob.Parse`/`IsMatch`, preserving
native scalar-list boundaries, escaping and path semantics. Illegal or ambiguous
necessary values fail instead of broadening ownership. Explicit input-consumer
facts independently add their consumers; union across native nodes rather than
intersecting variant excludes. Do not simulate SDK item rules.

The current test project's `fixtures/products/**` exclusion separates ordinary
Node fixture edits from its .NET parent, while its explicit embedded npm
`version.json` retains that exact .NET consumer. WebHdfs assigns `ConfigFilePath`
inside `PreBuild`; its in-project copy input is covered by ordinary committed
ownership, including additions/deletions. Keep native Copy unchanged, without a
passive-property promise or default-None assumption. External/shared producer
inputs need concrete native facts/adapters.

Native `ProjectInstance.Targets` and `ProjectTargetInstance.Location` identify
effective repository target definitions and their origins, not arbitrary task
I/O. The current finite producer scope includes Pack-only `SetPackageReleaseNotes`,
official tool references in `ReferenceOfficialNbgv`, WebHdfs `PreBuild`, the
traversal's non-Windows restore customization and conditional
`ValidateWorkflowDeliveryFrozenInputs`. Preserve their native conditions/effects;
ordinary Windows CI does not activate the non-Windows or frozen-release operation.
New or unrecognized repository producers need a reviewed adapter/operation scope;
report unsupported origin/name. This is no SDK task allowlist, hash admission
policy, universal tracer or claim about every locked package's hidden I/O.

#### Planning, Execution and Collection

Extend existing Windows `scope`; do not add a planning job for runner availability.
Reuse its prepared candidate application and native event/comparison handoff.
Obtain both endpoint facts before .NET applicability, and reuse the shared
comparison/responsibility binder and impact planner rather than duplicating them.
The old coarse .NET Boolean is not selected-plan authority.

Resolve adopted presets and explicit native-retained checks under the MLD's
[quality-origin contract](./middle-level-design.md#declarations-and-quality-autonomy).
Emit the original .NET `CiPlan` as a distinct artifact for the existing
`dotnet-tests` execution/collection consumer, with native artifact ID and resolved
applicability. Failed/missing/cancelled planning cannot become a successful skip.
Successful empty work remains valid; required CI composition still accounts for
the planning owner and .NET execution/result check.

Initially build selected native projects/variants individually with the original
operation globals; MSBuild owns references/order. Repeated reference work is an
acceptable cost. Traversal filtering is an optional later optimization requiring
its own native hook/variant qualification, not an application scheduler. Evaluated
`IsTestingPlatformApplication` selects MTP; otherwise `IsTestProject` selects
VSTest using native inner `GetTargetPath` output. Do not infer framework paths or
capability from package names. Every selected supported variant contributes its
required checks, collected against the original plan and candidate. Exit zero
cannot satisfy absent, skipped or failed obligations.

Retain the old command as sole executor during qualification if needed; it cannot
satisfy replacement-plan results. Before atomic cutover, qualify task/dependency
loading; graph identity/dimensions/references; finite committed/generated input
ownership; official configuration facts; native test capability/output; selected
execution; both-endpoint mapping; and changed-code Windows plan transfer/results.
Cover actual local/transitive/analyzer references, exclusions plus explicit
resources, additions/deletions/renames, changed variants/edges and unavailable
facts. Windows feasibility remains the owner's assumption, not another experiment.
Retire the old provider/caller only after its actual remaining consumers switch.
No publication/proving dispatch, authentication, access or Environment change
follows from this group.

### Control-Test Input Routing

Before the complete Node group switches, connect the MLD's
[control-test resource consumer](./middle-level-design.md#control-test-resource-consumer)
to root CI. Run native evaluation and final selection in the existing `scope`
job on Windows, keeping its job name, outputs, downstream guards and final
`ci-scope` artifact. Keep `Build & Test (.NET 10)` as the sole general .NET test
executor. This co-location needs neither an extra resource producer job nor a
resource/CLI artifact transfer; only the concrete receiving process consumes the
comparison/resource response.

Prepare the selected candidate control application once using its pinned SDK
and locked native dependencies. Invoke that complete same-runner build output
with its selected .NET runtime; keep required assemblies, dependency metadata and
runtime configuration together without creating a portable archive. Resolve the
native event comparison once with `ci comparison`, then resolve its actual Git
objects. Confirm the named test project's committed entry, materialize exact
endpoints outside the candidate source root, install each endpoint's selected
`global.json` SDK, and perform necessary locked restore of that test project and
its native reference closure under Debug before the single evaluation query.
Candidate preparation never substitutes for unavailable base inputs. Native
preparation/evaluation is unprivileged; the scope job runs no second test suite.
Confirmed base absence requires successful inventory inspection; the candidate
owner must remain present. Identical full-mode endpoints may reuse one evaluation.

The existing scope job explicitly prepares locked dotnet, Node and PNPM tools
before native environment export, with `MISE_EXEC_AUTO_INSTALL=0` scoped to that
job and inherited by its control commands and native children. Before each
endpoint's first dotnet operation, explicitly prepare that endpoint's own locked
dotnet, Node and PNPM declarations. Keep Actions-selected `global.json` SDKs and
native DOTNET_ROOT semantics; candidate tool preparation cannot stand in for a
different basis declaration. Missing tools or native verification still fail.
This bounds implicit installation, not tool visibility or all network effects.

Change the Python selector's logical Git-coordinate operations to `PurePosixPath`;
retain `Path` for filesystem access. Use strict UTF-8 for Git, JSON and text at
the process boundary, and an explicit compatible workflow shell rather than
inheriting Windows defaults for the current Bash block. Consume the required
native response before emitting final applicability and reasons. Preserve all
other source inventories/selectors and the existing successful-scope guards.
Missing response, malformed/conflicting identities or native errors fail the
scope job; they cannot appear as non-applicable downstream work.

Validate fixture-version-only and Debug-conditional resource changes selecting
the existing .NET owner; base/candidate removed, moved and added resources;
confirmed base absence versus native failure; default and unsupported dimensions;
missing/malformed response and candidate mismatch; unrelated Node nonselection;
and coherent explicit full. Reuse existing native item-semantics tests and run
existing selector scenarios, including Unicode and inherited/deleted/renamed
inputs. Ordinary changed-code hosted CI must exercise Windows preparation, final
selection and required-check behavior. A component or design result alone does
not establish routing or complete-group acceptance. Retained Python consumers,
all selected Node variants and explicit migrated execution ownership remain
separate prerequisites of the later group cutover.

### First Node Execution Group

This is the concrete integration contract for the first root-CI execution
cutover. It refines the MLD's selection and transfer contracts; it does not claim
that the new caller or input projections have been implemented or qualified.
The group adopts the npm fixture's existing quality/package obligations. Other
native workspace members retain their existing Node executor. Native PNPM impact
is computed over the complete supported workspace before this ownership split.
An unadopted member needs no replacement preset merely to retain its checks.

#### Endpoint Responsibility and Selection

Extend the existing `ci-scope` JSON with `endpoint_owners`, alongside its existing
retained other-job selections. Each `basis`/`candidate` endpoint carries its exact
`revision` and one `paths` row for every changed coordinate. A row contains
`path`, committed `present`, nullable Git `mode`, and `reasons`; each positive
reason contains `owner`, concrete `target`, `rule`, and source-file coordinates
in `sources`. Confirmed absence has `present=false`, `mode=null` and no reasons.
Multiple legitimate owners remain separate reasons. Native Git supplies entry
presence/mode and committed configuration bytes; missing facts are not absence.
Only changed coordinates are transferred, not another ownership database.

Use the retained selector's manifest-path inventory, native resource and
special-job associations to produce these endpoint facts. Python coordinates
provide responsibility for other ecosystem consumers without parsing Python
workspace, dependency or test configuration or selecting Python execution.
The [Python quality and caller mapping](#python-quality-and-caller-mapping) and
[native Python CI group](./middle-level-design.md#native-python-ci-group) own both-endpoint native facts,
the single original mixed plan and candidate execution. Preserve the retained
other jobs' candidate-execution semantics. A basis-only owner explains a
removed input; it does not execute a deleted target. Preserve both general .NET
and AzureAuth owners when applicable and the native control-test resource reasons
already integrated above.

Record responsibility uses the accepted endpoint's record-family bindings and
the existing component-aware matcher from `check_repository_records.py`. Extract
that small pure matcher for both consumers rather than import the entire checker
or introduce another glob dialect. Prepare the existing root dev dependency
group through frozen UV synchronization for its locked PyYAML parser; introduce
no second parser/version/lock declaration. Catalog parsing and matching do not
run the full contextual record checker in every scope job. Unconditional source
conformance is not a positive owner for an arbitrary path.

The existing `repository-record-validation` implementation and its maintained
`records:test` regression entry may supply positive endpoint responsibility from
that endpoint's current [control entry](../../../../../docs/governance/controls.yaml),
explicit `mise.toml` tasks and [checker contract](../../../../../docs/governance/checker-contract.md).
Retain the committed source coordinates and local/advisory execution semantics.
This finite route neither invokes the checker nor adds CI selections; it does
not infer owners for other local controls, tests or engineering paths. Actual
product/control consumption remains independently native.

The Ubuntu group joins native PNPM membership and reasons before final owner
validation. The Windows selector's `src/` package-manifest heuristic cannot
establish or reject native Node responsibility. Manifest/catalog reads used to
discover an owner do not themselves make those files product execution inputs.
Responsibility and actual product/control consumption remain distinct.

Compute the complete supported PNPM, product NBGV, declaration and control-input
relations at both endpoints, then select their reverse consumers and surviving
candidate work. A changed coordinate may be excluded from the fixture group only
when every relevant present endpoint has positive responsibility and neither
endpoint has a group consumer. Unknown ownership, unsupported relevant entries,
unresolved necessary inputs or failed native reads fail planning. Do not infer
nonselection from arbitrary Git subtraction, filename extensions or HK success.
Explicit full selects all supported adopted checks and retained Node owners; it
still rejects unresolved required facts. An empty plan retains its exact subject.

#### Native Control-CLI Inputs

The supported control operation is unprivileged Ubuntu Debug locked restore/build
of `src/private/app/workflow-delivery/WorkflowDelivery.csproj`, followed by Node
planning/execution. Use the endpoint's committed SDK with one effective TFM, no
RID override or project references, and ordinary build properties recorded with
the operation. Record project, SDK, configuration, TFM, working directory,
output/intermediate paths and relevant CI/global properties. The candidate CLI
must build successfully even when no product check is selected. A distinct
basis gets its own exact materialization and native context. Confirmed absence
of the basis CLI is supported; a failed restore/build/read is not absence.

Read the ordinary build's binary log through the selected SDK's maintained
`BinaryLogReplayEventSource`, reusing only the predecessor's native replay
mechanism inside the one private application. Use structured import, task,
parameter and completion events, with `MSBuildLogVerboseTaskParameters=true` and
task-input logging enabled. Native `-getItem/-getProperty` queries supplement the
SDK's configuration candidate lists and operation context. Do not add a custom
compiler snapshot, `ProjectCollection` host, SDK XML evaluator, compiler command
parser, localized-text fallback or extra helper project.

Correlate `TaskStarted`, `TaskInput` parameters and completion through native
build/node/project/target/task contexts. Accept inputs only for the actual CLI
operation and supported producers. Preserve the native parameter role, item
identity and required metadata; `IList` alone does not define a universal path
schema. The finite mapping is:

| Native role                        | Projection and supported boundary                                                                                                                                                                                                                                                                                                                                                                               |
| ---------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Csc file lists                     | `Sources`, `Resources`, `References`, `AdditionalFiles`, `Analyzers`, `AnalyzerConfigFiles`, `EmbeddedFiles`, `AddModules` and `LinkResources`; include repository inputs and classify generated/external inputs below. Output assembly, documentation, PDB and generated-output directory parameters are outputs.                                                                                              |
| Csc scalar/indirect files          | Recognize `ApplicationConfiguration`, `CodeAnalysisRuleSet`, `KeyFile`, `ResponseFiles`, `Win32Icon`, `Win32Manifest`, `Win32Resource` and `SourceLink`. The first operation supports generated SourceLink through its native producer/configuration; other nonempty forms remain unavailable until their native/explicit input adapter is implemented. A response filename alone cannot close indirect reads.  |
| Standard generated compiler inputs | Associate SDK global-usings, assembly/TFM attributes, generated analyzer configuration and NBGV managed version source with their native producer inputs/outputs and contributing configuration. Preserve generated identity; do not rewrite an `obj` filename into a guessed source path. A generated resource or other generator outside this finite operation fails until its producer/input adapter exists. |
| Runtime inputs                     | Include native source-backed copy inputs and resolved implementation dependencies used by running the CLI, not just Csc reference assemblies. Bind source files to native Copy input identities/metadata and their output roles. The current locked CopyOnWrite task implementation is part of the supported operation; task name alone does not establish implementation semantics.                            |
| Project/configuration inputs       | Include the main project, contributing native imports, SDK/CPM/locked restore configuration and concrete group caller/preparation files. Native imports do not include the main project automatically.                                                                                                                                                                                                          |
| Optional configuration candidates  | Query native `PotentialEditorConfigFiles` and `GlobalAnalyzerConfigFiles`, retaining relevant absent coordinates. The finite operation also includes standard root/ancestor SDK, NuGet, Directory.Build and Directory.Packages configuration search coordinates. These are supported search candidates, not a claim of arbitrary missing-import discovery.                                                      |

Normalize repository paths in their native project/task context to the exact
endpoint's committed spelling. Only supported regular Git files become source
inputs. A generated file must join a supported native producer/configuration
relation. Files outside the checkout must join the selected SDK or locked restore
package/tool identity or be an exact configuration contributor reported by that
operation's native locked restore. Require absolute existing contributor files
before normalization; committed files retain source precedence. Outside-checkout
contributors remain invocation-local restore-environment facts, without exact
pins or portable Git coordinates. This configuration-role association grants no
membership to imports, task implementations or compiler inputs. Unknown external
files still fail. Do not invent source paths from output basenames or treat all
external files as pinned dependencies.

This is a maintained adapter for the concrete CLI build, not a filesystem trace
or an arbitrary-MSBuild completeness promise. Review additions to custom tasks,
imports, generators and indirect inputs against this boundary; detectable
unsupported tasks/parameters fail at runtime. Native events cannot discover a
custom task's arbitrary unreported reads. Such behavior requires an explicit
adapter before adoption, rather than an application monitoring or trust service.

Require successful restore/build, complete successful replay, successful relevant
task completion and an actual required Csc invocation. Fresh endpoint output and
intermediate state prevents an incremental hit from standing in for input
coverage. Design-time/skipped compiler execution, truncated/malformed logs,
missing required fields or conflicting contexts fail. Optional empty parameters
remain distinguishable from required missing events. A supported SDK pair must
have ordinary integration evidence for event emission and replay/dependency
loading; an incompatible basis/candidate pair fails explicitly. Do not infer
exact-runtime support from a newer public API page.

Unused CLI self AssemblyVersion/InformationalVersion and source-link provenance
do not create product-consumption edges solely because the repository commit
changes. This narrow rule requires no current runtime consumer of that metadata;
genuine CLI source/configuration/dependency/preparation inputs remain semantic.
Product NBGV version inputs remain native product inputs. Always retain the exact
candidate control-build provenance and native failure behavior.

#### Workflow, Runtime Variants and Results

Add one static Ubuntu group job after the existing Windows scope job. The scope
upload exposes its actual immutable artifact ID. The group downloads that ID and
validates the actual producer/run, comparison, complete changed-coordinate set
and endpoint facts before planning. Use native transfer integrity; add no
signature, admission mirror or producer download/readmission loop. Plan every
change, independently of the old coarse Node Boolean. Build and use the candidate
CLI in this job; there is no separate CLI artifact without another consumer.

The retained Node matrix depends one-way on scope and this group. Successful
planning supplies explicit retained-member selections/reasons and applicability;
failed/missing group planning cannot become a skip. Exclude exactly the migrated
fixture using PNPM's native negative directory filter for recursive build/test,
while preserving full frozen installation, every other workspace member, both
runtime variants, if-present test behavior and the Hexo typecheck/packed probes.
Resolve any real generated-output prerequisite before crossing this boundary.
Do not execute an adopted check again through the retained aggregate owner.

Preserve these seven required obligations:

| Runtime | Required checks and actual prerequisites                                                                               |
| ------- | ---------------------------------------------------------------------------------------------------------------------- |
| Node 22 | Build and source test; source test has no build prerequisite.                                                          |
| Node 24 | Build and source test; npm package requires build, and package contents/clean installed consumer require that package. |

Resolve runtime variants from root CI's existing `node-version` matrix and the
committed mise toolchain authority, retaining actual native version readback.
Do not maintain a second runtime list. Finite variant keys carry those dimensions
through planning, execution and results; packaging remains Node 24 only. Each
runtime receives the complete parent plan plus its selected runtime context.
The collector evaluates both result sets against that original plan, with no
success substituted for another runtime's missing/failed/canceled/timed-out/
skipped/conflicting required result.

Prepare separate exact candidate materializations, native dependency state and
external scratch for the two runtimes. Node 22 build can stamp its tracked
manifest without postpack reset; fresh scratch alone does not isolate source.
Use native checkouts, not application cleanup/recovery logic. An empty product
plan requires no product runtime execution. Root CI retains overall result
ownership; the group and retained executor remain required when applicable.

#### Atomic Fixture Move and Acceptance

Move the native member to
`tests/private/app/workflow-delivery/fixtures/products/hcoona-release-smoke-npm`
with unchanged package coordinate, PNPM membership/importer, relative quality/
unit declarations and native version/operation inputs. Associate old basis and
new candidate coordinates through native identity; retain no duplicate fixture
or mutable compatibility alias. Update the replacement C# Node repository reader
(`NodeRepositoryReader.cs`) and its fixture-sensitive tests, current MSBuild
resources/tests, Python source-copy/catalog/adapter consumers, active legacy
Release source-path readers and workflow environments atomically. Preserve the
reader's actual script, version, legal and configuration input roles across the
native old-basis/new-candidate association. Preserve original historical bytes
and Git-pinned evidence identities. No Release execution follows from this move.
If an active reader cannot migrate correctly, defer both move and dependent
caller cutover together.

Before cutover, validate transitive/removed/native local inputs, dependency-edge
changes, source moves/deletions, selected unadopted members, explicit full/empty
plans, genuine shared/control inputs and unrelated owned Node/C#/Python/record
nonselection. Validate real target-added NBGV references, later analyzer-config
inputs, generated/pinned input classification, supported copy roles, task context
correlation, missing/failed/skipped compiler facts and unsupported indirect reads.
Verify the exact SDK logging/replay dependency assumptions in ordinary authorized
implementation tests, then the actual changed-code hosted transfer, retained
filter and all seven obligations. These are implementation acceptance conditions,
not a claim of existing native qualification or permission to revive the stopped
Node campaign. A necessary native capability gap returns to the owner.

### CLI and Boundary Payloads

The executable is `workflow-delivery`; these are internal command families with
JSON files only at actual process/job boundaries. Field spelling can be refined
with the first implementation, but producers and consumers must change together.
Do not build an envelope registry or independent schema version service.

| Command and caller                                          | Required inputs                                                                                         | Output consumed at the next real boundary                                                                                                          |
| ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ci comparison` in root CI planning                         | Native event name/payload and platform-tested candidate                                                 | Comparison basis, candidate and explicit full flag for native checkout/plan preparation                                                            |
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
The finite [GitHub comparison mapping](./middle-level-design.md#github-event-comparison)
reads native payload fields directly rather than adding another event declaration.
Its implementation alone does not switch root CI or establish group coverage.
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
