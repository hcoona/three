# Workflow Delivery Python Integration Contract

This integration contract owns the Python native facts, CI group, package
execution, Release caller and destination conditions. It refines the
[MLD](../middle-level-design.md), whose [integration
mapping](../middle-level-design.md#integration-mapping) states the shared scope
and authority of the integration contracts.

## Native Python Planning Facts

`Native/Python` uses the repository-pinned UV metadata and synchronization CLI,
passive PyPA build-requirement extraction and the bounded build association
below. The [source assessment](../research/python-native-planning.md) retains the
evidence and names the native contract tests that guard pin changes. UV owns
resolution, the resolved graph, workspace membership and `requires-python`
enforcement; PyPA owns build-requirement strings and PEP 508 parsing; NBGV owns
its semantics. Standard tomllib supplies physical key/shape guards only.
There is no embedded UV library, settings
interpreter, resolver or compatibility promise. A nonempty flattened
dependency group whose every requirement has a marker disjoint from the
workspace Python range can lack a metadata node; that valid UV
shape remains an unsupported
[group-transfer gap](../research/python-native-graph-qualification.md#failed-captures-and-counterexamples)
with its recorded review obligation.

For each exact basis/candidate checkout, bind revision, workspace root,
interpreter/tool identity and operation dimensions, then invoke
`uv workspace metadata --frozen --python <interpreter>` at the workspace root
without sync/active/script mode. The committed lock is consumed under the
existing synchronized-lock guarantee; frozen mode does not revalidate it.
Consume the pinned preview schema's member paths/names, structured sources,
opaque graph IDs, ordinary edges, group/extra entry IDs and `requires_python`.
Never decode IDs or infer owners from URLs, prefixes or caches.

The request uses the default index with sources enabled. A manifest in a native
context or above the workspace root may configure `tool.uv` only with
`workspace` and `sources`; any other setting, or a project `uv.toml` at or
above the workspace root, is unsupported rather than ignored. The child process
is credential-free, with owned HOME/XDG/cache/temp roots, an explicit
interpreter, downloads disabled, `UV_NO_SYSTEM_CONFIG` set, no inherited
UV/PIP/index/credential settings and an owned absent `UV_PROJECT_ENVIRONMENT`.
Unexpected environment facts fail the request. Bind project configuration and
version-file inputs; do not port private CLI precedence or suppress
configuration/sources.

UV metadata stubs build nodes, so build requirements come from
`ProjectBuilder.build_system_requires` for named package contexts with explicit
Hatchling build tables and no backend-path/package override, parsed with PyPA
`packaging.requirements.Requirement`; direct URL requirements are rejected. A
requirement joins a workspace member by canonical name only when its
`tool.uv.sources` entry (project table, else workspace root table) is absent or
exactly `{ workspace = true }`. Every other source shape, a member requirement
without that source and a non-member requirement with it fail planning.

A
registry requirement or an unsourced self-reference adds no local input; a
workspace-sourced self-reference keeps its requested extras. This is
the bounded HLD exception, a strict subset of UV lowering. Repeat the
association for every necessary local build-requirement source; Workflow
computes transitive/reverse closure and cycles. Association does not prove
version satisfaction or build success. Tableless native quality/group contexts
remain; forced package builds, default backends and undeclared dynamic inputs
are unsupported. Malformed manifests remain errors.

Treat every edge and build-requirement marker as active: the finite union over
interpreters and platforms can only add consumers. Groups follow UV's default
`dev` group unless no-dev is requested; the guard rejects `default-groups`,
`dev-dependencies` and `dependency-groups`. Root pytest preparation includes
root plus selected quality owners; no-dev operations and build-requirement
ordinary legs select no groups. Operation extras start empty; build-requirement
extras select producer extra entry IDs. Unknown necessary group/extra selection and nonempty native conflicts fail
planning. Preserve
endpoint, interpreter/platform, groups/extras and check/build variant before
markerless `ProjectFacts.Dependencies`, and union both endpoints so removed or
renamed producers retain consumers. `uv sync --frozen --python <interpreter>`
during preparation enforces `requires-python` for the operation interpreter.

Adapt into the existing [implementation types](../middle-level-design.md#implementation-type-names)
`RepositoryFacts`/`ProjectFacts`/`SharedInput`/`CheckSpec` and
impact closure, keeping quality consumers separate from build inputs and root ownership
explicit. Managed
NBGV and passive plugin coordinates supply project-scoped version/generated
input consumers. Missing or ambiguous ownership, native errors or unmodeled
necessary scope fail planning; there is no reduced completeness, parser
fallback or all-run.

## Python Quality Owners and Input-Only Members

Terms such as endpoint, paired and input-only member are defined in the
[HLD domain values](../high-level-design.md#domain-values).

Native workspace membership does not imply a build or test obligation. Keep the
complete native producer graph, including tableless members (a `pyproject.toml`
without a `[build-system]` table, which has no package build) and unadopted members. Actual
retained pytest targets and adopted package checks define runnable quality owners.
Resolve nearest source ownership over all native members before restricting the
result to runnable owners; an input-only nested member is not its ancestor's source.

The paired assembler associates endpoint-native identities and unions resolved
ordinary, build and quality relations from both comparison revisions before
computing producer-to-runnable-owner closure. Do not compose only endpoint-local
closures after removing intermediate producers. For example, a basis relation
`A -> B` and candidate relation `B -> C`, expressed as producer-to-consumer, must
select surviving runnable `C` for a changed `A` input even when `B` has no checks.
Keep each endpoint's committed path association and initiating path/revision reason;
resolve executable work against surviving candidate owners. Do not infer native
identity matches from package names or opaque graph ID spelling.

Paired input associations reach runnable owners through the
[paired direct-selection reasons](../middle-level-design.md#paired-direct-selection-reasons).
Only actual endpoint runnable owners become `ProjectFacts` with nonempty resolved
checks; input-only members retain their graph role without synthetic builds,
empty-check policy, candidate-only basis proxies or a new core origin.

Read root pytest configuration through its public `pytest.main(..., plugins=[...])`
and `pytest_cmdline_main` hook, using native `Config.getini`, `rootpath` and `inipath`.
The initial request admits the current nonempty explicit committed `testpaths` and
the existing import-mode option. Reject necessary unsupported glob, implicit
invocation-directory fallback, escaping target, custom plugin or option behavior;
do not copy pytest's configuration precedence or private target-selection algorithm.
Configuration parsing can import explicit plugins and alter Python paths before
the main hook: the query runs unprivileged with owned state, no inherited
`PYTEST_ADDOPTS`/`PYTEST_PLUGINS`, disabled plugin autoload and conftest discovery.
Those switches alone do not prove absence of explicit plugin processing. This
finite native query supplies configuration facts, not successful test collection
or execution. Its source basis is recorded in the
[Python planning assessment](../research/python-native-planning.md#pytest-configuration-source-basis).

Associate configured targets under native members with their actual quality owners;
standalone repository tests are concrete target consumers, not invented workspace
members. Preparation uses root plus selected quality-member contexts and native
operation groups. Input analysis does not require building every workspace package.
Retain adopted package checks, complete outputs and their own prerequisites. The
[caller mapping](#python-quality-and-caller-mapping) owns the
concrete engineering relations that native package tools cannot discover.

## Python Planning Integration

The private application's auxiliary code follows the planning-fact contract.
Planning extraction is separate from Python preparation/build/check execution. It selects source
impact from native facts; it neither installs local build requirements nor proves successful
wheel/sdist consumers. The [source assessment](../research/python-native-planning.md) supplies its evidence
basis; this design owns the selection.

Retain the current root pytest testpaths, selected/full frozen package-preparation obligations,
tableless quality owners and root shared inputs. Root
pytest does not require building all workspace packages. Preserve the smoke quality declaration's
wheel and sdist outputs, distribution-content checks, wheel install/import and Git-free sdist
build/install/import, with their actual prerequisites and variants. Do not duplicate inventories
or freeze current counts as architecture. Maintain project-scoped NBGV/plugin/template/generated
consumers even where a generated file is absent or current staged execution removes a plugin.

The current caller uses native lock/default/source facts and the accepted
execution, check/prerequisite and transfer mapping. Retired declaration/name
heuristics, generated constraints and adapter no-config/no-sources choices are
not fallback semantics. Changes update producers and consumers atomically and
qualify the affected facts/callers. Publication and spent campaigns remain
excluded from CI qualification.

The [primitive native observation](../research/python-native-planning.md#primitive-native-observation)
is independently accepted and stopped; both unused diagnostic reserves expired.
It qualifies the pinned helper composition on its recorded Linux host, not complete
fact assembly or caller integration. Do not repeat that campaign; a later native
experiment needs its own accepted protocol.

### Python Quality and Caller Mapping

The [paired producer-to-owner projection](#python-quality-owners-and-input-only-members)
serves Python fact assembly and the current callers. Root pytest configuration remains
the sole test-target declaration; native UV membership and dependency answers
remain the package graph. Read both revisions and preserve the following concrete
consumers in the current native group. This table defines ownership and retained
behavior, not another package or test-target roster.

| Current consumer relation                               | Association and retained behavior                                                                                                                                                                                                                                                                                                                                            |
| ------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Native member source and local dependency consumers     | Resolve ownership over all native members, union both revisions' ordinary/build/group/quality relations, then map to actual configured member test or adopted package owners. Preserve removed edges and input-only intermediaries.                                                                                                                                          |
| Root pytest `testpaths` and direct target inputs        | Query native pytest configuration and bind each explicit committed target to its endpoint. Changes within a configured target select its complete check; standalone repository targets have exact input consumers and no blanket root ownership.                                                                                                                             |
| `CiScope.AllInputs` and `PythonInputs`                  | Retain root CI/toolchain, native workspace/lock/configuration, test configuration and Python preparation-script consumers for the actual retained Python target set. Keep source-input reasons and distinguish helper preparation from product build inputs.                                                                                                                 |
| Passive Python helper tests                             | Retain native helper source and maintained `nbgv-python` source as inputs to the configured passive test target. This does not turn the helper into a publishable project.                                                                                                                                                                                                   |
| General CI shell and current control tests              | Retain exact native helper, engineering script, workflow and configuration inputs for the root test owner. The general CI shell contract consumes `tests/eng/ci_workflow_shell.py`; no old public-core test or adapter relation remains.                                                                                                                                     |
| Azure credential-provider Python tests and `AzureInput` | Retain source, .NET interoperability, engineering script, maintained plugin and Python configuration consumers. Keep its independent special-job ownership.                                                                                                                                                                                                                  |
| Maintained NBGV plugin tests                            | Retain required .NET tooling/configuration inputs as well as native member-source consumers. Native plugin coordinates and managed NBGV input discovery supply version/template/generated consumers, including absent generated files.                                                                                                                                       |
| `tests/eng` source associations                         | Retain the Python runtime/version projection, HK wrapper, tool-version reader and native shell/workflow consumers; every root workflow and `global.json` select the committed tool-version guard. Retain the exact typo-configuration and retired-Buddy guard consumers. Derive target membership from native pytest configuration and retain cross-target script relations. |
| Adopted Python distribution checks                      | Preserve wheel/sdist build outputs, distribution contents, wheel install/import and Git-free sdist build/install/import, with their complete required variants and actual prerequisites. Keep these obligations separate from root pytest preparation.                                                                                                                       |

Prepare root plus selected quality-member package contexts with native UV group
answers, retaining the current frozen selected/full preparation behavior. Do not
install unrelated workspace tools or force input-only members to build. Executors
receive the original candidate-bound selected work and required results, without
recomputing impact or changing native target selection. No legacy selection or
preparation fallback remains. The [concrete native caller](../../README.md#native-python-ci-caller)
uses the finite C# Python caller for root CI and committed local full; Windows scope
retains only comparison, other-job responsibility and the independent NuGet
native preparation mapping. The ordinary fixture's PR
checks use its adopted native distribution preset; manual Release and historical
readers have separate owners.

The group inside the private app follows the
[native Python CI group contract](#native-python-ci-group).
The Windows root scope caller transfers the exact event comparison and explicit
full/incremental mode plus complete candidate control outputs. The existing Linux
Python job materializes both endpoints, collects their native facts for its actual
Linux product interpreter, invokes shared selection once, then runs the finite
pytest and distribution partitions and collects against that original mixed plan.
Native analysis is independent of retired Windows Python selection outputs;
an explainable empty affected scope performs no product preparation or execution.
Reuse the current process/artifact transfer pattern; no additional workflow, job,
public library or target roster is needed.

Root CI, explicit local full preparation and the ordinary fixture's PR checks
share this native consumer group. Affected changes require tests with controlled fixtures
and ordinary PR CI for the complete group. Independent special jobs and Release
callers retain their own responsibilities. Historical evidence readers use their
actual pinned producers, without retired selection, preparation or constraint
runtime consumers.

The local Linux full entry uses a native launcher to build committed control before
the same typed Python caller executes explicit full scope. No retired wrapper is a
fallback or compatibility path.

Ruby helper behavior is owned by the quality-adopted bundle-only RSpec carrier.
Its Gemfile and locks declare native test dependencies, including both actual
Bundler contexts; no caller installation roster or Python test host remains.
Managed contract tests and real C# native integration retain counterpart coverage.
The existing one Python and one Ruby Linux managed preparation scenarios execute
directly in the native-preparation CI job. It binds the original comparison,
preserves selected triggers, qualifies rows through the native test filter,
per-module `--minimum-expected-tests` and exit code, retains reports and
original packages, and contributes to required Validate completion. Retired pytest
gates and their wrapper-only tests have no continuing consumer.

Tests with controlled fixtures must include a mixed-endpoint `A -> B -> C` closure through
input-only `B`, nested native ownership, changed/deleted producer edges, a known
input with no consumers versus unresolved scope, tableless quality owners,
native pytest endpoint/configuration/target errors and cancellation, root shared
inputs, selected package/group preparation, and every required distribution/result
transfer. The existing core nonempty selected-check contract remains intact.
Complete native collection/query qualification requires its own accepted exact
protocol using the actual current tools and lock format; the stopped primitive
campaign is not reused or repeated. Ordinary PR CI remains available for its
authorized caller-integration qualification after contract acceptance. A change
to Python selection or caller code is qualified, after contract acceptance, by
ordinary PR CI together with the tests with controlled fixtures above; only a new
native collection or query experiment needs its own accepted protocol.

The shared core owns the
[paired direct-reason transfer](../middle-level-design.md#paired-direct-selection-reasons)
used by Python fact assembly. Keep both endpoint project sets actual; no
candidate-only owner proxy is inserted into basis facts. Controlled integration
must cover a basis-only input reaching candidate-only `C`, deleted quality owners,
candidate Release Unit siblings, all selected variants/checks/prerequisites and
initiating path/revision reasons. Reject foreign seed owners/revisions, reasons
outside the changed-path set and unrelated unresolved changes; preserve explicit
full mode and the existing nonempty selected-check/origin guards. Do not duplicate
shared relation/unit expansion inside Python or call `PlanSelected` with an
externally completed selection that skipped that expansion.

### Native Python Package Execution Integration

The private app implements the [native package contract](#native-python-package-execution)
using its accepted native source basis. C# owns
finite check expansion and orchestration; a minimal Python helper may call
maintained packaging, native installed metadata and standard archive interfaces
where needed. It does not parse UV lock/dependency/backend semantics. Preserve
the adopted project's payload/API expectations in its registered quality
implementation, separate from ordinary native distribution validity.

Reuse the committed root development/build-tool groups and maintained
`nbgv-python` plugin, with the [phase-specific native frozen preparation](#native-python-package-execution)
and build-dependency checking. Preserve declared workspace editability through
original distribution creation; reconcile the same tools to noneditable native
installation before Git-free original-archive consumption. Bind the repository-pinned official NBGV command
through the plugin's existing command selection, exact runtime and full relevant
Git inputs. Native backend builds may compute version again; agreement, rather
than a single invocation, is required. No generated backend roster, alternate
version injection, smoke witness or static-project rewrite is introduced.

Tests with controlled fixtures cover complete declared outputs and version agreement,
failed/missing required checks, native preparation/build failure, installed
consumer origin, Git-free sdist invocation, cancellation and original result
transfer. Native qualification additionally observes actual selected frozen
installation, noneditable plugin independence, shared backend requirements,
wheel/sdist contents and both clean consumers using one concrete protected finite
protocol with a diagnostic buffer. Stop at the first accepted success. Source
findings alone do not establish those results.

Native preparation and the current Python caller own their locked build inputs.
Retired control-package generators and adapter constants have no remaining
consumer. Package qualification and paired planning remain distinct required
journeys; neither substitutes for the other.

## Native Python CI Group

The existing Linux Python job selects and executes one original candidate-bound plan for
configured pytest targets and adopted distribution checks. This group composes
the native graph collector, pytest configuration/target binding, repository input
associations and paired fact assembler already defined above. It adds no project,
dependency, test-target or backend declaration. The
[caller mapping](#python-quality-and-caller-mapping)
retains the concrete repository and special-job consumers.
Ordinary hosted qualification of this group is carried by
[PR #1114](https://github.com/hcoona/three/pull/1114).

The Windows scope job transfers the exact event comparison and explicit
full/incremental mode, together with the complete candidate control distribution.
The existing Linux Python job performs the first Python selection: it materializes
both exact endpoints and collects their native facts for its actual Linux product
interpreter and supported operation dimensions. Windows interpreter facts
cannot substitute for that Linux context. The selected control distribution
and native query tools must run in that context; unavailable necessary capability
fails analysis rather than introducing platform emulation.

Planning binds each materialized endpoint to its own native membership,
configuration, interpreter/platform, operation groups and committed inputs.
Prepare a separately owned query-tool environment for each endpoint with native
`uv sync --frozen --only-group dev --only-group workflow-delivery-python --no-editable`,
noneditable workspace dependency installation and default build isolation. Use
that endpoint's committed root groups, lock and source configuration, and the
bound Linux interpreter. UV owns the selected tool-group closure and any native
backend preparation; no separate library or backend roster is maintained. The
declared plugin may be built as a query-tool dependency without making unrelated
workspace members product or quality owners. Passive query execution remains
distinct from this tool installation. Keep the installed helper environment
separate from product/test/package environments and from the owned absent
`UV_PROJECT_ENVIRONMENT` required by native metadata queries.

Supply the candidate control version's `Native/Python` helper sources from the
transferred candidate control sources. Bind helper source and runtime identities
to both endpoint queries; each query retains its own endpoint configuration,
lock, groups and actual Linux interpreter facts. Preserve the configuration
preflight and owned credential-free query state. Necessary tool preparation
failure, timeout or cancellation stops analysis.
The current native Python callers own this preparation. Affected preparation or
caller changes require qualification of their actual source and environment;
stopped graph/enrichment observations alone do not establish installation or
caller success.

Reuse the accepted native metadata, passive extraction, build-association join and pytest query;
candidate facts cannot substitute for missing basis facts. Bind actual quality
adoption and release declarations before complete paired selection. Existing
shared-core closure selects surviving checks and supplies initiating path/revision
reasons. Explicit full mode states the supported group it covers; an incremental
analysis failure cannot become a full run.

Begin that analysis for every valid comparison, independently of legacy Windows
Python applicability, target, package or special-input heuristics. Successful
incremental analysis may establish that no checks are affected: report the
explainable no-work scope and perform no product/test/package preparation or
execution. Query-tool preparation remains separate. Missing facts, failed
transfer, cancellation or timeout cannot become successful non-applicability.
For selected work, keep the existing nonempty selected-check requirement.

The Linux job invokes shared selection once, then passes its original plan to
the executor in the same job. At that boundary, preserve candidate-native
membership
and pytest target bindings, tool/operation context and complete candidate control
distribution. Validate their agreement with the receiving exact candidate before
product dispatch. Native member names come from metadata, and target/member
coordinates come from the existing native target binding. This request validation
must not resolve dependencies, choose targets again or introduce a trust record.
Selected V3 consumers prepare mise's native configuration trust in their owned
HOME before HK/Pkl queries. Mise owns that local tool state.

The executor partitions that plan into native-retained `python/pytest-v1`
checks and the registered Python distribution preset. Validate both supported
partitions and their complete prerequisites before either executes. Keep one
required result per original check and collect against the original mixed plan;
neither executor may remove or substitute the other partition. Empty partitions
need no preparation. This follows the existing .NET group composition rather
than introducing a task scheduler.

For a nonempty pytest partition, prepare a separate test environment with native
`uv sync --frozen`, selecting the root context plus every selected quality-member
context. Resolve names from supplied native membership, retain the interpreter
and native default-group operation, and let UV own installation and dependency
semantics. Standalone repository targets require the root context; member targets
also require their bound member. Dependency producers do not become automatic
quality owners. Do not substitute the distribution-tool environment or force
unrelated input-only members to build.

Execute each original selected target with
`uv run --no-project --python <prepared-environment> python -m pytest`, retaining
the supported native configuration/options. Set `UV_PROJECT_ENVIRONMENT` only
for preparation and remove it from product execution so nested project-native
UV calls do not retarget the prepared environment. Product preparation and
execution retain ordinary Python import behavior; query-only `PYTHONSAFEPATH`
is removed at that boundary while native query isolation remains unchanged.
One invocation per target supplies its distinct required outcome while sharing
preparation. A preparation failure, timeout or cancellation leaves every affected
required target unsatisfied; it cannot produce an empty successful result. Each
affected target's failure text names the shared `uv sync` preparation stage
with its bounded native diagnostic rather than a pytest failure. Keep
independent package obligations visible even when pytest fails. The package
partition retains its separate phase-specific preparation, original archives and
four required distribution checks under the contract below.

The root CI group and local full runner use the same native Python group and
preparation contract. Affected producer/consumer changes preserve selected/full
preparation and complete checks together. Tests with controlled fixtures cover root-only
and mixed member preparation, native name/target agreement, explicit full versus
incremental selection, complete partition/result transfer and failures/cancellation.
Ordinary authorized PR CI qualifies the complete affected caller group before
delivery. No local package experiment, historical campaign, Release or publication
authority follows from this integration contract.

## Native Python Package Execution

Use the private C# application to expand adopted package checks, invoke native
operations and join their outcomes through existing `CheckSpec`, `PackageTarget`
and result contracts. UV owns frozen group/workspace selection, phase-specific native installation,
PEP 517 backend requirements and build dispatch. The maintained
`nbgv-python` plugin owns official NBGV invocation; `nbgv-python`'s own version
comes from Hatchling's static `[project].version`. Do not port these semantics,
maintain a second backend-requirement declaration, rewrite project version
metadata or inject a replacement version engine. The
[source assessment](../research/python-native-packaging.md) distinguishes these
selected boundaries from unqualified runtime behavior.

For the current adopted Python distribution contract, prepare an owned native
project environment from the existing root `dev` and `workflow-delivery-python`
groups and synchronized lock. Use one frozen sync selecting both groups with
UV's default build isolation and the workspace's declared editability. UV owns
declared and backend-reported auxiliary build requirements, including editable
tool preparation. Do not precede it with a separate root-dev bootstrap, disable
that native requirement preparation, maintain a backend roster or manually
orchestrate requirement hooks.

Keep the selected installed tools through
completion of the original wheel/sdist set: native source-build requirements
retain workspace source and editability semantics. Exact sync removes omitted
tools, so selection includes the development backend tools as well as the
build/plugin group. The root-dev footprint is existing infrastructure,
not an application-maintained minimal backend roster. Actual selection and
installation must establish that unrelated workspace products are not built.
This package-tool preparation does not replace root pytest's selected/full
member preparation obligations.

Frozen group selection owns the installed tools; it does not promise that
auxiliary isolated backend resolution is lock-frozen. Preserve the distinction
and any relevant native builder/version input in qualification evidence. Product
builds below use the prepared selected tools and shared native build mode.
Neither a frozen sync nor the lock alone establishes same-input byte
reproducibility or Release/recovery qualification.

Build the complete declared wheel/sdist set with native `uv build`, the selected
package and prepared interpreter. Enable the pinned native
`build-dependency-check` preview with shared build mode; it checks declared,
backend-reported and transitive requirements. Missing native prerequisites fail;
there is no skip flag or application fallback. Frozen preparation remains the
selected installation authority. Native Directory requirements still enforce
editability equality under permissive checking; compatible-version checks do not
prove original-archive identity. A changed tool or necessary
backend shape requires the affected native-contract review before use.

Consume normal native distribution metadata through maintained packaging and
Python library interfaces. Preserve necessary archive path, size and ambiguous
member checks without requiring a universal literal payload, tar encoding or
V3 witness. Distribution spelling does not determine its import module. The
registered project quality implementation retains actual payload and installed
API expectations; those expectations are not universal package-format rules.

Wheel consumption installs the original wheel in a fresh owned environment.
Before Git-free sdist consumption, reconcile the same owned package-tool
environment with one frozen native sync selecting both groups, shared build mode
and `--no-editable`. Require successful reconciliation and unchanged original
archive bytes before continuing; failure, cancellation or timeout fails the sdist
check and prevents its downstream consumer operations. This does not erase an
independently completed original-wheel result.

Git-free sdist consumption builds the original archive file from a neutral
directory outside Git using the reconciled noneditable backend/plugin
distributions, then installs its resulting wheel into another clean environment.
Pass the original archive file, rather than an extracted directory, to UV: its
native archive path uses ordinary declared PEP 517 requirements without the
source-directory workspace lowering. This source-supported distinction remains
subject to complete native qualification. Neither consumer
implicitly imports the source checkout.

Native installed metadata, import origin
and the applicable project API check must agree with the intended native version
and outputs; command exit alone cannot satisfy them. A source finding about
native Hatchling metadata reuse alone does not establish Git-free runtime
qualification. The adopted composition retains its exact original-wheel and
Git-free original-sdist [package observations](../research/python-native-package-qualification.md);
current caller qualification and affected changes remain scoped to their actual
source, tools and environment. This establishes no universal backend/version
support. Do not rewrite `pyproject.toml` to force success.

Keep complete build, contents, wheel install/import and Git-free sdist
build/install/import obligations individually visible even when one native
build serves multiple checks. Native fact selection remains separate from
execution. The exact tools/helper/harness, environment/download/cache effects,
finite command/diagnostic/time/state bounds, stop and owned cleanup must receive
independent review and protected protocol acceptance before native qualification.
Earlier graph/enrichment campaigns supply no package allowance. Missing necessary
native capability stops for owner disposition without reduced check coverage.

## Concrete Python Release Integration

This contract selects the registered `hcoona-release-smoke-python` and
`nbgv-python` release units, their declared wheel/sdist builds and the adopted
Python distribution quality preset. TestPyPI is Buddy and PyPI is Official;
Official accepts only the smoke unit. The product-neutral caller
`workflow-delivery-v3-python-release.yml` takes the unit as a dispatch input and
binds it through concurrency, preparation, observation and publication. It adds
no destination, publication authority or native experiment. Source findings follow the
[native publisher assessment](../research/python-native-publisher.md); closed
campaigns remain historical evidence rather than executable callers.

Preparation reads the exact committed release declaration, project entry and
quality selection using existing Git/declaration readers. Unsupported builds,
outputs, quality recipes or required tests remain unavailable. Resolve native
metadata through the maintained backend and `nbgv-python`, except that
`nbgv-python` itself has Hatchling's static `[project].version` and no NBGV
projection or public-release fact; use the selected
CPython/UV and the synchronized root lock. [Locked tool selection](common.md#locked-tool-selection)
accepts whichever CPython and UV the root lock selects. Live Official requires the native public-release
projection; ref eligibility follows the [Official channel scope](common.md#official-channel-scope).
The Buddy Environment `workflow-delivery-v3-python-testpypi` allows
every branch (`**`) and keeps its required reviewer; the Official
Environment remains limited to `main`. Keep one
source/control revision throughout; do not inject versions or a target witness.

This project's committed NBGV `publicReleaseRefSpec` is `^refs/heads/`.
Shared CI and Release preparation retain its existing base, prerelease height
and maintained `SemVer2` normalization. This native public-version mode shapes
a version; it supplies no
Official authorization. Keep the Official selected-ref check independent.
Provide faithful selected native ref/commit context on detached materializations;
missing required history/context or a live local-version projection blocks that
request. Do not strip suffixes, choose another field or override only Release's
version. Same-version/different-byte conflicts retain their normal outcome.

CI package execution runs native tools in an owned environment that does not
inherit the runner's ambient Actions/ref context, so builds see the same NBGV
inputs as planning. Without ref context NBGV projects a non-public version that
carries a `+g<commit>` local segment. Release preparation supplies the selected
ref, so NBGV projects the public version. CI qualifies package construction, not
the Release version string.

Changes to this configuration or shared preparation require qualification of
the exact changed source and affected CI/Release version and identity boundaries
before delivery; they supply no new native experiment or live-operation authority.

Shared `Ecosystems/PythonSmokePackageOperations` owns the explicitly registered
smoke product recipe and native package operations
inside the private application. `CI/PythonPackageExecution` and
`Release/PythonPreparation` consume those operations while retaining distinct
plan/check and request/qualification decisions. Release does not consume `CiPlan`
or `CheckSpec`. The common mechanisms cover native
frozen tool preparation, metadata/output inspection, complete wheel/sdist build,
original size/digest checks and clean consumers.

Follow the
[existing native package contract](#native-python-package-execution):
one frozen sync selecting root `dev` and `workflow-delivery-python`, native
backend-requirement preparation, shared product build with native dependency
checking, and noneditable backend/plugin reconciliation before Git-free sdist
consumption. Do not maintain another backend roster, dependency evaluator or
version engine. Native packaging interprets filenames and versions; Python
library interfaces interpret archive and installed metadata.

Complete preparation records native inputs/version, build, contents, original
wheel consumer, Git-free original sdist consumer and original-byte results,
plus any applicable declared tests. Preserve each reached command's actual
termination and failed/missing/cancelled results. Bind portable native name,
version, filenames, declared output roles, sizes and digests to the request;
checkout-specific paths are not remote identity. The smoke project has no
runtime dependencies. `nbgv-python` declares exactly `packaging>=24.0`; each
clean consumer installs that closure from the committed root `uv.lock`
(`uv export --frozen` then a hash-checked `uv pip install --no-deps`) before
installing the original alone, and checks its import origin and exported API.
Its preparation also runs the adopted pytest suite
(`src/public/lib/nbgv-python/tests`) as the required `tests` check, so CI
results are not substituted for Release qualification. Another dependency
shape needs its concrete integration.

Use explicit closed destinations: TestPyPI Simple Index
`https://test.pypi.org/simple/`, upload `https://test.pypi.org/legacy/` and file
host `test-files.pythonhosted.org`; PyPI Simple Index
`https://pypi.org/simple/`, upload `https://upload.pypi.org/legacy/` and file
host `files.pythonhosted.org`. Observe publicly, without OIDC or write credentials.
Read the project's PEP 691 JSON index, then interpret selected-version filenames
through maintained packaging and compare actual original bytes. Require the
complete declared pair, native coordinate, visibility and file metadata. A
missing project or missing expected file can be absent; contradictory metadata,
unexpected selected-version files, conflicting bytes or unavailable required
facts cannot permit mutation. Registry active-file non-overwrite is the native
dependency; no atomic two-file release or lifetime-global retention is promised.

Bound each observation phase to 90 seconds, with one application index read and at
most one application original read per prepared file, each body-inclusive read to 30 seconds and
index JSON to 2 MiB/4,096 entries. Use verified HTTPS through the shared
registry HTTP client, without insecure fallback. Accept index file URLs only on
the selected public storage host with no user information or fragment.
Observation performs no visibility polling. Retain necessary sanitized
observations rather than a replayable HTTP trust ledger.

Only the actual publisher receives the existing destination Environment and
native `id-token: write`. Invoke the repository-selected UV publisher once for
one explicit still-needed original, with `--no-config`, the selected
`--publish-url`, `--trusted-publishing always`, `--keyring-provider disabled`
and `--no-attestations`; no index alias, wildcard or alternative credentials.
Do not supply native `--check-url`; application complete verification stays separate.
Native UV owns multipart, OIDC/short-lived token exchange, response
interpretation, bounded retry and supported redirect behavior. The application
provides an owned external working directory, fresh home/config/cache state,
closed stdin, an environment containing only required platform/tool variables
and a 180-second whole-process deadline. Supply the native GitHub OIDC request
variables only to this publisher. Keep credentials out of arguments, request
records and persisted native configuration. Token invalidation remains a native
best-effort operation; record no application revocation certificate.

Publication output needs a credential-aware boundary before any persistence.
Forward complete native `add-mask` controls to the runner through stderr, keeping
CLI stdout reserved for its JSON result. Do not forward arbitrary child workflow
commands or retain token-bearing control lines. Persist only sanitized native
diagnostics; omit unsafe text with an explicit diagnostic limitation. Preserve
actual termination, exit, elapsed time and unknown effects on every error,
output-limit, cancellation and timeout path. Runner masking does not replace
result-file sanitation. Never fabricate native success to hide output handling.

Refresh the complete observation immediately before mutation. Remove writes
that became unnecessary; conflict or unknown state stops them. Invoke missing
wheel before missing sdist, retaining already exact originals. Rely on the
platform: an acknowledged native upload means the index committed that file,
while the public index read is eventually consistent with no read-after-write
guarantee, so a read soon after the upload can still miss it. Continue to the
next missing file after acknowledged native success, without an intermediate
readback or application polling. A returned failure/ambiguity or interruption
stops all further application writes even if a subsequent read confirms that file.
Bounded complete verification may still satisfy the active request; retain the
actual command abnormality and do not attribute insertion. A fresh same-revision
request can consider supported exact-subset completion after rebuilding and
qualifying its own pair. No automatic resend, rerun recovery or compensation.

A separate no-write job observes the selected index again for either initial
exact state or new publication. Each intended file must be listed for the exact
version with matching index hashes, not yanked, and its downloaded bytes must
equal the qualified original. No remote UV install or import runs after
publication; local wheel and Git-free sdist consumption before publication
carries qualification. This observation does not claim index routing or
resolver file selection. Missing or unknown observation blocks completion.
A file whose upload this request acknowledged but which the observation still
shows absent is unknown rather than unsatisfied: the acknowledged upload is not
yet visible. A later fresh request confirms it by observing exact state and
needs no write. Conflicting bytes remain unsatisfied.

The ordinary Python entry and actual Release caller compose qualified components.
They use prepare, observe, gated publish and separate
verify/result jobs on the supported Linux runner. Build/retain complete control
before product execution. Transfer control, preparation and original artifacts
by same-run immutable artifact ID with the native download digest check;
`job-inputs python receive <phase>` binds the received preparation's
source/ref, unit/channel/destination/mode subject to the request. Keep the
existing 45-day retention setting. Serialize by physical
registry and normalized package through required verification. Dry run uses
qualification/read/hypothetical operations and never `uv publish --dry-run`.
Exact-state and dry-run paths enter no write gate. Common result evaluation
retains attempted effects, actual command outcomes, observed state and business
satisfaction separately. Historical readers retain their actual consumers;
there is no runtime-schema compatibility alias.

Changes follow the [shared review core](../middle-level-design.md#release-integration-review). Validate
native command/configuration and output boundaries, original
preparation/consumption, partial-state order, failure-after-effect,
cancellation, missing transfers and complete results with controlled contract
tests and credential-free local builds/tests. Run complete affected suites,
HK/hooks and exact record checks. Ordinary Linux CI must qualify the final
component/caller subject before delivery. These checks do not prove current OIDC
configuration, live registry behavior or a new publication and do not reopen
the stopped Python package or publication campaigns.

### Python Destination Completion

The MLD's [post-publication completion](../middle-level-design.md#post-publication-completion)
contract applies to these destinations; this table owns their concrete
conditions and observation.

| Existing destination/use         | Native completion conditions to preserve                                                                                                                          | Post-publication observation                                                                                        |
| -------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| TestPyPI Buddy and PyPI Official | Complete intended wheel/sdist set, normalized version, index file/metadata identity, both original files and required visibility; supported exact-subset recovery | Each intended file listed in the selected index with matching hashes and not yanked, and its downloaded bytes equal |
