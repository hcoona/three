# Workflow Delivery v3 Integration and Retirement Contract

## Purpose and Baseline

This contract connects the [HLD](high-level-design.md) and
[middle-level design](middle-level-design.md) to the internal application's native
integrations, ordinary workflow callers and retirement boundaries. Implementers
and reviewers use it to preserve complete journeys across actual process,
permission and artifact transfers. It is not an execution or publication grant.
The contracts below govern current integrations and changes to them; their
preparation and acceptance conditions are not a remaining migration backlog.
The [Wave](../../../../../docs/delivery-wave.md) owns work authorization;
[agent handoff](agent-handoff.md) retains operation and evidence limits.

The former caller inventory and migration order remain at their
[immutable source][former-plan]. Historical producer identities and their exact
implementation, fixture and outcome readers are routed by
[historical evidence readers](validation/historical-readers.md). Current source
acceptance does not requalify those originals or reopen their closed campaigns.

## Owned Projects and Final Locations

| Owner                                                               | Responsibility                                                                                                             |
| ------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| `src/private/app/workflow-delivery/WorkflowDelivery.csproj`         | Internal CI and Release entry point, native readers, selection, execution, package and destination operations              |
| `tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj` | Managed scenario, algorithm, transfer and native integration tests                                                         |
| `tests/private/app/workflow-delivery/Native/{Python,Ruby}/`         | Tests of the corresponding native-language helpers                                                                         |
| `tests/private/app/workflow-delivery/fixtures/products/`            | Four ordinary npm, NuGet, Python and Ruby package inputs, with native manifests, versions and quality/release declarations |
| `src/private/app/workflow-delivery/docs/`                           | Current requirements/design/contracts and retained research/evidence readers                                               |
| `src/public/lib/nbgv-python/` and `src/sample/nbgv-hatch-demo/`     | Independent public NBGV component and sample, retaining their own consumers and authorities                                |

Module names in the MLD are source organization, not additional applications.
Native product fixtures retain their package coordinates but have no old/new
byte or version continuity promise. Their placement must preserve deliberate
native version bases, quality ancestry, workspace membership and build imports.
Root traversal retains normal native project semantics; moving a fixture does
not justify a separate discovery or scheduler service.

## Native Helper and Caller Boundaries

Complete .NET, Python and Ruby CI group execution owns its original plan's outcome.
CLI exit and process receivers use that bound outcome, retaining comparison,
candidate, scope, raw results and diagnostics. Receivers reject missing or malformed
outcomes rather than launching the same control to reevaluate its unchanged results.
Pre-execution plan validation and Node's join across independent runtime results
remain distinct.

The application owns ordinary Release job inputs, request subject binding,
original transfer, phase configuration and required write-state output. Existing
Ruby/Python/npm/NuGet workflows invoke `release job-inputs <ecosystem> <operation>` through
their already distributed candidate control. They do not carry a separate
language-specific Release caller implementation. Actions retains job ordering,
Environment, credentials, concurrency and immutable artifact transfer. npm's
single-job Official simulation creates a consumed typed request. Same-run
immutable artifact IDs carry the producer run; each later job checks the received
preparation once against the request subject rather than restating run and
attempt in separate producer records. Live ref eligibility has one definition,
applied where the request is formed and again by the privileged publisher.
npm completion consumes the retained initial observation to
distinguish legitimate no-write completion from missing required publication
information. NuGet callers forward native process termination; typed completion
owns consumer/result interpretation, without a shell enum mirror.

Ruby helpers retain RubyGems/Bundler evaluation, native source reads and concrete
installation/API behavior. Python helpers retain native library bindings and the
pinned UV supplement; .NET and Node use their supported native process interfaces.
A helper is justified by a required library interface, not by the language label.
Adding a distinct product can require a concrete recipe; no generic plugin or
automatic adoption capability is implied by these four implemented products.

[Native wire schemas](../Native/contracts/) are generated from the actual
application serializer metadata by `native-message-schemas <directory>`.
They describe retained Ruby fact/source/consumer messages with independent
native consumers. The existing Python supplement request is tested by its
managed producer and Rust consumer; no unused schema export is retained. Managed tests require regeneration equivalence; native Ruby
tests consume the static descriptions without building the application. These
are shape descriptions, not a schema registry, runtime admission policy,
upstream PNPM/MSBuild format mirror or compatibility lifecycle. Semantic native
identity, path, version, producer and command checks remain required. Existing
native command-argument interfaces need no invented JSON envelope.

## Runtime and CLI Consumers

The [application README](../README.md) owns current commands and supported
options. The internal CLI has no historical Python command or record aliases.

| Consumer                      | Native input/output boundary                                                                                        |
| ----------------------------- | ------------------------------------------------------------------------------------------------------------------- |
| Root CI planning              | Exact event candidate/comparison, native paired facts, quality declarations and selected required work              |
| Ecosystem CI group wrappers   | Prepared current control, complete selected checks/variants and candidate-bound command/results                     |
| Ordinary Release preparation  | Selected revision, one release unit/channel/destination/mode, complete original package set and clean qualification |
| Observation and publisher     | Bound preparation/original files, native destination state, upload-once command facts and required verification     |
| Separate consumer/result jobs | No-write native consumption and business completion, retaining command abnormalities and unresolved effects         |
| Historical auditors           | Original producer-qualified Git source and retained evidence; no current campaign/replay entry point                |

Native PNPM, MSBuild/NuGet, UV/Python and RubyGems/Bundler own ecosystem semantics.
The application does not retain the old public Python control package, exclusive
provider/authority/consumer projects, dependency scanner, runtime admission files
or closed campaign operators as compatibility services.

## Workflow and Action Cutover Groups

| Caller                                                     | Required atomic boundary                                                                                                                                                                            |
| ---------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `.github/workflows/ci.yml` and the CI scope caller         | Actual native ownership, control preparation, group selection, complete results and retained originals                                                                                              |
| Native Node, .NET, Python and Ruby group wrappers          | Paired endpoint facts, selected work, effective quality, original tool outputs and terminal process facts                                                                                           |
| Windows NuGet preparation job and reproducibility workflow | Three preparation rows in CI and four sensitivity/recovery rows on dispatch or `main` changes, executed and passed, with original packages, separate PDBs, native diagnostics and failure retention |
| Ordinary npm, NuGet, Python and Ruby release workflows     | Selected control/product revision, prepared artifact bindings, native authorization, upload-once behavior and separate no-write verification/results                                                |
| Remaining platform-specific CI and independent projects    | Preserve their own ownership, runners, preparations and required checks                                                                                                                             |

The Windows NuGet jobs' success cannot substitute for the complete ordinary CI
gate. General C# checks use Windows; Python and Ruby callers use their supported
Linux runner. Fixture and local successes do not establish live destination,
authentication or publisher configuration. Closed bootstrap/acceptance workflows
and exclusive setup/operation-record actions have no current execution consumer.
Their actual historical producers remain recoverable through pinned readers.

## Root Tooling, Declarations and Record Readers

| Surface                                                                        | Retained responsibility                                                                                                |
| ------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------------------------- |
| Product release/quality declarations, native manifests, versions and locks     | Explicit native ownership, complete output/check scope and genuine shared inputs                                       |
| `mise.toml`, `hk.pkl`, runtime projections and dependency-update configuration | Current tool preparation and conformance; no old control-package generators, scanner tasks or parser-only dependencies |
| Root workspace/build manifests and locks                                       | Actual current projects and native fixture inheritance; native tools update their own locks                            |
| Root selectors, attributes, ignore rules and CODEOWNERS                        | Current source/configuration paths and generated/output ownership                                                      |
| Agent instructions, portals, family bindings and project interfaces            | One current application authority; fixture carrier paths remain distinct from owning namespace                         |

Retire an exclusive dependency or helper only with its last current caller.
Native version/build inputs and the public NBGV component remain when their real
consumers need them. Historical evidence uses original source identities rather
than a transplanted admission, approval-proof or replay implementation.

## Concrete Integration Contracts

### Native Facts and First Supported Shapes

For each new integration, first identify its maintained native library or
supported tool interface, the inputs/configuration it evaluates, the facts and
errors it exposes, and the required facts still unavailable. Review that concrete
boundary before implementation. Follow the HLD's
[native integration boundary](high-level-design.md#native-integrations), including
native-language helpers and owner disposition when a necessary capability is
missing. Internal implementation and transfer formats have no compatibility
obligation. Rewrite producers and consumers together instead of adding aliases
or porting ecosystem parsing and resolution rules into C#.

The owner selected the supported PNPM CLI for the application's Node
integration. Do not add a Node helper or embedded LibNode for that integration.
The [PNPM 12 assessment](research/pnpm-native-planning.md) supplies source
evidence and the bounded local validation protocol for this choice. Selecting
the CLI does not accept an incomplete fact extraction recipe or native reader.
Use PNPM's workspace selection, structured resolved output and native
publish-directory metadata to obtain source project relations; reuse existing
declarations rather than maintaining a second dependency/output manifest. The
assessment's completed bounded directory-join follow-up supplies its recorded
composition evidence. Changes to the reader require their own qualification.
Workflow combines the base/candidate results and computes
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

Use Ubuntu runners for the Node/Python/Ruby groups and Windows for general
.NET evaluation/build/consumption, with explicit product variants taking precedence.
Tool versions come from `global.json`, `mise.toml`/`mise.lock` and native locks;
do not introduce a second toolchain authority in this plan.

### Python Planning Integration and Later Caller Cutover

The private application's auxiliary code follows the finite planning-fact contract.
Planning extraction is separate from Python preparation/build/check execution. It selects source
impact from native facts; it neither installs local build requirements nor proves successful
wheel/sdist consumers. The [source assessment](research/python-native-planning.md) supplies its evidence
basis; this design owns the selection.

Retain the current root pytest testpaths, selected/full frozen package-preparation obligations,
tableless quality owners and root shared inputs through the existing implementation plan. Root
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

The [primitive native observation](research/python-native-planning.md#primitive-native-observation)
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

The [paired producer-to-owner projection](middle-level-design.md#python-quality-owners-and-input-only-members)
serves Python fact assembly and the current callers. Root pytest configuration remains
the sole test-target declaration; native UV membership and dependency answers
remain the package graph. Read both revisions and preserve the following concrete
consumers in the current native group. This table defines ownership and retained
behavior, not another package or test-target roster.

| Current consumer relation                                 | Replacement association and retained behavior                                                                                                                                                                                                                                |
| --------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Native member source and local dependency consumers       | Resolve ownership over all native members, union both revisions' ordinary/build/group/quality relations, then map to actual configured member test or adopted package owners. Preserve removed edges and input-only intermediaries.                                          |
| Root pytest `testpaths` and direct target inputs          | Query native pytest configuration and bind each explicit committed target to its endpoint. Changes within a configured target select its complete check; standalone repository targets have exact input consumers and no blanket root ownership.                             |
| `ALL_INPUTS` and `PYTHON_INPUTS`                          | Retain root CI/toolchain, native workspace/lock/configuration, test configuration and Python preparation-script consumers for the actual retained Python target set. Keep source-input reasons and distinguish helper preparation from product build inputs.                 |
| Passive Python helper tests                               | Retain native helper source and maintained `nbgv-python` source as inputs to the configured passive test target. This does not turn the helper into a publishable project.                                                                                                   |
| General CI shell and current control tests                | Retain exact native helper, engineering script, workflow and configuration inputs for the root test owner. The general CI shell contract consumes `tests/eng/ci_workflow_shell.py`; no old public-core test or adapter relation remains.                                     |
| Azure credential-provider Python tests and `_azure_input` | Retain source, .NET interoperability, engineering script, maintained plugin and Python configuration consumers. Keep its independent special-job ownership.                                                                                                                  |
| Maintained NBGV plugin tests                              | Retain required .NET tooling/configuration inputs as well as native member-source consumers. Native plugin coordinates and managed NBGV input discovery supply version/template/generated consumers, including absent generated files.                                       |
| `tests/eng` source associations                           | Retain the Python runtime/version projection, HK wrapper and native shell/workflow consumers; retain the exact typo-configuration and legacy-release-contract consumers. Derive target membership from native pytest configuration and retain cross-target script relations. |
| Adopted Python distribution checks                        | Preserve wheel/sdist build outputs, distribution contents, wheel install/import and Git-free sdist build/install/import, with their complete required variants and actual prerequisites. Keep these obligations separate from root pytest preparation.                       |

Prepare root plus selected quality-member package contexts with native UV group
answers, retaining the current frozen selected/full preparation behavior. Do not
install unrelated workspace tools or force input-only members to build. Executors
receive the original candidate-bound selected work and required results, without
recomputing impact or changing native target selection. No legacy selection or
preparation fallback remains. The [concrete native caller](../README.md#native-python-ci-caller)
uses the finite C# Python caller for root CI and committed local full; Windows scope
retains only comparison, other-job responsibility and the independent NuGet
native preparation mapping. The ordinary fixture's PR
checks use its adopted native distribution preset; manual Release and historical
readers have separate owners.

The group inside the private app follows the
[native Python CI group contract](middle-level-design.md#native-python-ci-group).
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
share this native consumer group. Affected changes require controlled validation
and ordinary PR CI for the complete group. Independent special jobs and Release
callers retain their own responsibilities. Historical evidence readers use their
actual pinned producers, without retired selection, preparation or constraint
runtime consumers.

CI caller composition for all four implemented ecosystems belongs to the existing
C# application. Native Actions/shell bootstrap builds and transfers the complete
control distribution once and prepares the declared native tools. The scope
caller selects retained jobs and changed-coordinate responsibility in the same
process as the .NET plan, consuming the separate default Debug control-resource
response; it does not reproduce native planners.
The local Linux full entry uses a native launcher to build committed control before
the same typed Python caller executes explicit full scope. No retired wrapper is a
fallback or compatibility path.

Ruby helper behavior is owned by the quality-adopted bundle-only RSpec carrier.
Its Gemfile and locks declare native test dependencies, including both actual
Bundler contexts; no caller installation roster or Python test host remains.
Managed contract tests and real C# native integration retain counterpart coverage.
The existing one Python and three Ruby Linux managed preparation scenarios execute
directly in the native-preparation CI job. It binds the original comparison,
preserves selected triggers and exact passed-row qualification, retains reports and
original packages, and contributes to required Validate completion. Retired pytest
gates and their wrapper-only tests have no continuing consumer.

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

The shared core owns the MLD's paired direct-reason transfer used by Python fact
assembly. Keep both endpoint project sets actual; no
candidate-only owner proxy is inserted into basis facts. Controlled integration
must cover a basis-only input reaching candidate-only `C`, deleted quality owners,
candidate Release Unit siblings, all selected variants/checks/prerequisites and
initiating path/revision reasons. Reject foreign seed owners/revisions, reasons
outside the changed-path set and unrelated unresolved changes; preserve explicit
full mode and the existing nonempty selected-check/origin guards. Do not duplicate
shared relation/unit expansion inside Python or call `PlanSelected` with an
externally completed selection that skipped that expansion.

#### Native Python Package Execution Integration

The private app implements the [native package contract](middle-level-design.md#native-python-package-execution)
using its accepted native source basis. C# owns
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

Native preparation and the current Python caller own their locked build inputs.
Retired control-package generators and adapter constants have no remaining
consumer. Package qualification and paired planning remain distinct required
journeys; neither substitutes for the other.

### Native Ruby CI Integration

Accept the [native Ruby planning facts](middle-level-design.md#native-ruby-planning-facts)
and [CI group contract](middle-level-design.md#native-ruby-ci-group) before
changing the reader and caller group. Use the private application's
native command/paired-selection/result interfaces. A small Ruby helper can
consume activated RubyGems/Bundler APIs through the already required Ruby runtime;
C# consumes structured answers and does not implement Gemfile/lock syntax,
version resolution or group/platform selection.

The current source gem consumers are `asciidoctor-latexmath` and the adopted
Ruby smoke gem; Hexo separately consumes a bundle environment without its own
gemspec. Latexmath's Gemfile does not load its source gemspec, whose project-owned
version projection invokes NBGV. Its gemspec file list omits tests. Preserve these
separate producer/check/input responsibilities; no generated package roster or
package-file-based RSpec selection is justified. Quality adoption fits the
existing YAML preset composition rather than a new policy lifecycle.

The required `ruby/project-rspec-v1` check and `ruby/rspec-v1` preset use
Latexmath's source-local quality selection. This explicitly adopts the existing whole-project RSpec
obligation; it does not strengthen its policy or infer quality from directory
structure. Retain the smoke gem's separate adopted distribution checks and Hexo's
bundle-only input responsibility. The native recipe and actual YAML selection
change with their corresponding reader, executor and caller.

Public Bundler source exposes native evaluated Gemfile inputs and dependency
closure, but the two committed locks select different versions and public method
shapes. Native Path source and active local Git override identity also differ.
Qualify the exact activated interfaces, frozen input invariance, nested Gemfiles,
direct/transitive local sources, removed/missing owners and existing cross-group
consumers when changing the dependent integration. Do not call private APIs, copy native
selection logic or replace a missing answer with installed-name/path inference.
Unbounded relevant dynamic Ruby inputs remain unsupported.

For RSpec, use supported native JSON formatter/output CLI options with the whole
original project/configuration. The formatter class itself is private; do not
construct or subclass it, select examples in Workflow, or add a report converter.
Preserve command termination, original examples, pending/failure and
outside-example results. Gem build/inspect/clean require obligations remain even
when the source has no Gemfile/spec directory.

The complete native Ruby group qualified combined planning, preparation,
execution and original result transfer in ordinary [PR #1123](https://github.com/hcoona/three/pull/1123) CI.
Root `ruby-group` now owns PR checks; the duplicate root job and shadow PR
workflow are retired with the fixture relocation. Node receives the original
paired Ruby facts from the same successful native group. Manual Release uses its
ordinary caller; historical readers use their pinned producers. Public source establishes interface candidates,
not current native qualification; source findings and bounded validation limits
must be accepted before observations. Further work requires the current Wave
and applicable concrete authorization; this contract does not reopen any stopped
Ruby campaign or supply publication, dispatch, authentication or configuration authority.

The native source basis is public and pinned: Bundler
[2.4.20 Definition](https://github.com/ruby/rubygems/blob/de20c7e7b7d7cc7a9ef088684a2c742d5a651ebf/bundler/lib/bundler/definition.rb)
and [2.7.2 Definition](https://github.com/ruby/rubygems/blob/b463ced1459e03bf871116fff3a09c5d287806c2/bundler/lib/bundler/definition.rb)
expose `gemfiles` and requested dependency/resolve answers; their respective
[2.4 SpecSet](https://github.com/ruby/rubygems/blob/de20c7e7b7d7cc7a9ef088684a2c742d5a651ebf/bundler/lib/bundler/spec_set.rb)
and [2.7 SpecSet](https://github.com/ruby/rubygems/blob/b463ced1459e03bf871116fff3a09c5d287806c2/bundler/lib/bundler/spec_set.rb)
implement different public `for` signatures. Bundler 2.7's
[Path](https://github.com/ruby/rubygems/blob/b463ced1459e03bf871116fff3a09c5d287806c2/bundler/lib/bundler/source/path.rb)
and [Git](https://github.com/ruby/rubygems/blob/b463ced1459e03bf871116fff3a09c5d287806c2/bundler/lib/bundler/source/git.rb)
separate original Path locations from active local Git paths. RSpec 3.13.6's
[JSON formatter feature](https://github.com/rspec/rspec/blob/4d8e9c3699dbc821b4603c3c01b6e81c85e3784c/rspec-core/features/formatters/json_formatter.feature)
and [option parser](https://github.com/rspec/rspec/blob/4d8e9c3699dbc821b4603c3c01b6e81c85e3784c/rspec-core/lib/rspec/core/option_parser.rb)
document native `--format`/`--out` use; the
[formatter implementation](https://github.com/rspec/rspec/blob/4d8e9c3699dbc821b4603c3c01b6e81c85e3784c/rspec-core/lib/rspec/core/formatters/json_formatter.rb)
marks its class private. These source findings do not demonstrate
complete current-project graph identity, activated-version or execution behavior.

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
The [pinned NBGV source findings](research/nbgv-native-inputs.md#native-msbuild-caching)
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
traversal's non-Windows restore customization. Preserve their native
conditions/effects; ordinary Windows CI does not activate the non-Windows
restore operation.
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
[quality-origin contract](middle-level-design.md#declarations-and-quality-autonomy).
Emit the original .NET `CiPlan` as a distinct artifact for the existing
`dotnet-tests` execution/collection consumer, with native artifact ID and resolved
applicability. Failed/missing/cancelled planning cannot become a successful skip.
Successful empty work remains valid; required CI composition still accounts for
the planning owner and .NET execution/result check.

Submit selected native projects/variants together to the selected SDK's native
`ProjectGraph` and `BuildManager` graph-build interface, with the original
operation globals for each entry. The SDK-hosted task disables its nested
manager's in-process node and uses native node lifetimes and result codes.
MSBuild owns references, ordering, parallelism and its invocation-local target
cache. A thin task in the existing application projects native per-entry target
outputs; it does not resolve dependencies or implement a scheduler. Retain one
batch command observation and bind its outputs to the original selected entries.
Preserve successful independent builds when another entry fails. Missing native
outputs, interruption or failed extraction cannot establish a passed build.
Tests follow their own satisfied build obligations. Retain Build in each test
invocation where native build-time properties or environment are required; do
not substitute inferred paths or a test-binary launcher. Evaluated
`IsTestingPlatformApplication` selects MTP; otherwise `IsTestProject` selects
VSTest using native inner `GetTargetPath` output. Do not infer framework paths or
capability from package names. Every selected supported variant contributes its
required checks, collected against the original plan and candidate. Exit zero
cannot satisfy absent, skipped or failed obligations.

The Workflow Delivery test project's native MTP configuration excludes the
`NuGetNativePreparation` and `NuGetNativeScenarios` categories by default.
The Windows CI preparation job owns the three preparation rows; its
applicability follows the existing .NET/Workflow scope selection and its result
consumer remains required. Same-input .NET
reproducibility was established once ([Issue #948](https://github.com/hcoona/three/issues/948),
[PR #952](https://github.com/hcoona/three/pull/952)); by owner decision it is build-infrastructure behavior, not
per-PR evidence. The Windows reproducibility workflow runs the four
sensitivity/recovery rows on owner dispatch and on `main` pushes changing the
SDK, shared MSBuild/NuGet/NBGV/Git inputs, NuGet Release/package adapter source
or the fixture product; its run ID, source commit and 30-day artifact are the
evidence carrier. The triggering PR's author, then the repository owner, owns a
failed `main` run. Skipped intermediate `main` commits are acceptable because
the next run covers the newer state; dispatch can target a specific commit.
Both override the default through MTP's ordinary command-line filter. Explicit
local filters also override the default; this is project-owned test
configuration, not a Workflow filter interpreter.
Native graph-test fixtures may batch restore their disposable projects through
an ordinary solution. Seed fixture locks and still validate the locked restore;
do not share mutable fixture state or introduce a Workflow restore cache.

The current native plan and executor own the complete group; retired commands
cannot satisfy its results. Affected changes qualify task/dependency
loading; graph identity/dimensions/references; finite committed/generated input
ownership; official configuration facts; native test capability/output; selected
execution; both-endpoint mapping; and changed-code Windows plan transfer/results.
Cover actual local/transitive/analyzer references, exclusions plus explicit
resources, additions/deletions/renames, changed variants/edges and unavailable
facts. Windows feasibility remains the owner's assumption, not another experiment.
No retired provider or caller is a runtime fallback.
No publication/proving dispatch, authentication, access or Environment change
follows from this group.

### Control-Test Input Routing

Root CI connects the MLD's
[control-test resource consumer](middle-level-design.md#control-test-resource-consumer)
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

The scope caller uses repository-relative POSIX Git coordinates and strict UTF-8
at the process boundary. It reads the required native response before emitting
final applicability and reasons. Preserve all
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
separate qualification responsibilities of the complete group.

### First Node Execution Group

This concrete Node integration contract refines the MLD's selection and transfer
contracts. Source, local and hosted evidence retain their distinct qualification
boundaries.
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

The scope caller produces these endpoint facts once from manifest-path inventory,
native resource and special-job associations. Receivers bind the comparison and
Git entries to their own native comparison; they do not re-admit the same job's
reasons. Python coordinates
provide responsibility for other ecosystem consumers without parsing Python
workspace, dependency or test configuration or selecting Python execution.
The [Python quality and caller mapping](#python-quality-and-caller-mapping) and
[native Python CI group](middle-level-design.md#native-python-ci-group) own both-endpoint native facts,
the single original mixed plan and candidate execution. Preserve the retained
other jobs' candidate-execution semantics. A basis-only owner explains a
removed input; it does not execute a deleted target. Preserve both general .NET
and AzureAuth owners when applicable and the native control-test resource reasons
already integrated above.

Retired pytest preparation gates and the former Ruby pytest carrier do not give
the control application, managed test tree or Ruby fixture blanket Python
responsibility. Their actual native managed/Ruby and record consumers explain
those coordinates. Existing Python helper and caller bindings still select real
Python consumers, including the Rust supplement, even when another native owner
also consumes the input. A missing native responsibility or unresolved Python
claim remains a planning failure.

Record responsibility reads only `bindings[].{id,state,path}` from the endpoint's
[record-family catalog](../../../../../docs/governance/record-families.yaml) with
the application's YAML parser. It performs no schema validation; the record
checker remains the catalog authority, and a binding-format change must update
both readers. Current binding paths use the checker's component-aware star
grammar from `eng/scripts/repository_path_patterns.py`; the application matches
them with `Microsoft.Extensions.FileSystemGlobbing`, whose `*` and `**/`
semantics agree for every current binding and are pinned by tests. Catalog reads
do not run the contextual record checker. Unconditional source conformance is
not a positive owner for an arbitrary path.

The endpoint's native `mise tasks ls --json` metadata names the `records:check`
and `records:test` scripts, which, with the checker's shared matcher, receive
record-system responsibility. This finite route neither invokes the checker nor
adds CI selections; it does not infer owners for other local controls, tests or
engineering paths. A present coordinate with neither responsibility nor a native
consumer remains a planning failure, not a run-everything fallback. Actual
product/control consumption remains independently native.

The Node group joins native PNPM membership and reasons before final owner
validation. Manifest/catalog reads used to
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

Build and distribute the exact candidate control before product execution. The
candidate must build successfully even when no product checks are selected.
A distinct basis requires its exact checkout and locked native restore, but no
fresh basis compilation or compiler-task log replay. Confirmed absence of the
basis CLI is supported; failed preparation or evaluation is not absence.

Use the endpoint-selected native MSBuild graph to bind the control project's
ordinary owned source region, evaluated file items, explicit reference HintPaths,
imports, locked restore and configuration candidates. Node's independent caller
uses the managed control project as its graph entry; the .NET group consumes the
control node from its existing complete root graph. The supported control has one
inner dimension and no ordinary project dependencies. A changed relevant shape
requires a concrete adapter rather than a task interpreter or broad fallback.

Explicit native reference HintPaths retain committed files outside the control
region and use existing native SDK/package provider bindings for external files.
Unadapted response files, additional assembly search paths/modules, linked
resources and indirect key inputs fail explicitly. Native repository-target
validation retains concrete supported producers. The official target-produced
NBGV DLL is a native tool exception, with its existing package configuration and
cache helper contract; Workflow does not discover a business dependency from
that tool DLL or recreate its caching mechanism.

Only the concrete inert control `docs/` subtree and control `README.md` are
excluded from the ordinary native item region. Actual embedded resources, copy
items, package documentation, helper sources, declarations, configuration and
product version inputs retain their consumers. An owner relationship alone does
not make all project records executable inputs. The root record/conformance
checks still own affected architecture and documentation records.

Unused CLI self-version/source-link provenance does not create product edges
solely because a repository commit changes. Retain genuine source, configuration,
imports, helper and caller consumers, paired deletion/rename facts, complete
selected checks and exact candidate/control bindings. Binlogs may remain native
diagnostics; they are not a required input-fact carrier.

Control-test resources retain their separate default Debug native query under
the [MLD contract](middle-level-design.md#control-test-resource-consumer).
The complete root graph additionally uses CI and locked-restore globals, so it
cannot replace that query without a reviewed context change. Existing endpoint
root restore supplies prerequisites; do not separately restore the test project.
Produce each complete graph once in its own context, reusing its in-memory facts
only when revision, checkout and operation inputs coincide. This is ordinary
invocation reuse, not an artifact cache or replay contract.

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

Preserve complete build and source-test obligations for every declared runtime;
source tests have no build prerequisite. The configured packaging runtime also
requires npm pack after build, followed by contents and clean installed consumer
checks. The current two-entry matrix expands to seven obligations.

Resolve variants from root CI's existing `node-version` matrix. The committed
mise Node tool selection determines the packaging runtime; it must occur in that
matrix. Support a finite nonempty unique list of major `.x` selectors, with no
general matrix include/exclude or dynamic task graph. Keep exact native Node and
PNPM readback for the configured packaging context and major readback for moving
matrix contexts. No application-owned second runtime roster or largest-major
heuristic is permitted. Each runtime receives the original complete plan and
its selected context. Collect every original required key without substituting
another runtime's success for missing or abnormal results.

Planning obtains lockfile-only native PNPM facts and metadata without installing
product dependencies at either endpoint. Selected execution retains its frozen
product installation. Tool preparation remains a separate prerequisite.

The Node job prepares the committed dotnet, Node, PNPM, UV and Python tools and
starts its frozen caller with explicit UV/Python selection. Native automatic
installation is disabled within this job and the local group's child processes.
Each endpoint explicitly installs its own locked dotnet, Node and PNPM before
use; native version, SDK and verification semantics remain unchanged. This
bounds missing-tool installation, not visibility of other installed tools or
network effects. Preserve the separate retained Node matrix setup.

The configured packaging variant installs through its committed locked context.
Other declared moving matrix selectors have no committed tool lock; only their
finite native unconfigured installation disables locked mode. Subsequent native
execution retains the configured SDK/PNPM locks and automatic-install restriction.

Prepare separate exact candidate materializations, native dependency state and
external scratch for each declared runtime. A build can stamp its tracked
manifest without postpack reset; fresh scratch alone does not isolate source.
Use native checkouts, not application cleanup/recovery logic. An empty product
plan requires no product runtime execution. Root CI retains overall result
ownership; the group and retained executor remain required when applicable.

#### Fixture Ownership and Changed-Source Qualification

The native member lives at
`tests/private/app/workflow-delivery/fixtures/products/hcoona-release-smoke-npm`
with its ordinary package coordinate, PNPM membership/importer, relative quality/
unit declarations and native version/operation inputs. The C# Node repository
reader (`NodeRepositoryReader.cs`), fixture-sensitive tests, MSBuild resources,
current ordinary Release callers and workflow environments use that location.
Changes update actual producers and consumers atomically, preserving script,
version, legal and configuration input roles and native endpoint identity.
There is no duplicate fixture, old-basis mapping or mutable compatibility alias.
Historical readers retain original bytes and Git-pinned producer identities.
A source-path change does not authorize Release execution; unresolved current
consumer mapping blocks the dependent change.

For affected changes, validate transitive/removed/native local inputs, dependency-edge
changes, source moves/deletions, selected unadopted members, explicit full/empty
plans, genuine shared/control inputs and unrelated owned Node/C#/Python/record
nonselection. Validate native NBGV helper/configuration classification, evaluated Reference HintPaths,
compiler file properties, analyzer/configuration inputs, generated/pinned input
classification and supported copy roles. Unsupported indirect control inputs must
fail explicitly. Qualify the retained endpoint graph and separate Debug resource
query under their actual SDK globals, including additions/deletions, unavailable
facts and cross-checkout original-plan execution. Then validate the changed-code
hosted transfer, retained filter and all seven obligations. These are changed-source
qualification conditions, not transferable acceptance from an earlier source or
permission to revive the stopped Node campaign. A necessary native capability gap
returns to the owner.

### CLI and Boundary Payloads

The executable is `workflow-delivery`; these are internal command families with
JSON files only at actual process/job boundaries. Changes to field spelling
update producers and consumers together.
Do not build an envelope registry or independent schema version service.

| Command and caller                                          | Required inputs                                                                                         | Output consumed at the next real boundary                                                                                                                            |
| ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ci comparison` in root CI planning                         | Native event name/payload and platform-tested candidate                                                 | Comparison basis, candidate and explicit full flag for native checkout/plan preparation                                                                              |
| `repository facts` on a required native runner              | Candidate or comparison revision, ecosystem, evaluation roots and dimensions                            | Revision, project ownership, local/shared-input relations, native dimensions, explicit coverage/errors                                                               |
| `ci plan` in root CI planning                               | Candidate/comparison identities, changed paths and necessary native facts, supported group              | Selected project/target/check/dimensions, reasons, required/advisory flags, runner, prerequisites and unresolved scope                                               |
| `ci run` in a selected executor                             | Selected work, candidate and required native inputs                                                     | Check/target/dimensions, actual status, native command result and output identities where consumed                                                                   |
| `ci result` in collection                                   | Expected work and available candidate-bound results/native job conclusions                              | Required-check result and missing/failed/unknown reasons; no success from absent work                                                                                |
| `release prepare` before write capability                   | Source/control revision, unit, channel, destination, live/dry-run mode                                  | Complete qualified original files with native coordinates/version, qualification results, applicable completion checks and maximum intended writes                   |
| `release observe` without product execution                 | Prepared request/files and scoped read access where needed                                              | Per-file absent/exact/conflict/unknown, native metadata/visibility, remaining required checks and proposed missing writes                                            |
| `release publish` in the gated publisher                    | Prepared selected-control artifact, qualified input identities, maximum writes and native authorization | Fresh observation, one native invocation per needed operation, raw command outcomes and bounded readback; stop application writes after invocation failure/ambiguity |
| `release verify` in unprivileged verification/consumer jobs | Prepared request/files, destination read access where required and selected native consumer checks      | Required remote observations and consumer results for the complete intended set                                                                                      |
| `release result` in collection                              | Request, publisher/native execution facts, readback and applicable consumer results                     | Satisfied/unsatisfied/unknown with command abnormality and unresolved effects kept distinct                                                                          |

In-process callers use the same typed operations without serializing each row.
The finite [GitHub comparison mapping](middle-level-design.md#github-event-comparison)
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
| npmjs Official simulation        | Exact public package/version metadata and original tarball or authoritative absence; this caller selects no routing tag                                           | Report read/qualification/hypothetical result only; no new live publication caller or remote-install claim is inferred from simulation                                                           |
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

The [npm Buddy integration](../README.md#npm-buddy-release)
owns complete selected preparation,
native package-control/exact original-byte reads, Environment on the actual
publisher, one standard native upload, separate scoped native install/import
and complete common result. Its source-only validation does not qualify a new
hosted Live invocation. Historical acceptance readers retain their actual pinned
native producer source; they do not authorize replay.

Use the existing bounded native read mechanisms as extraction inputs, not the old
Governance expiry/profile digests. Each integration closes endpoint/redirect and
credential handling, request/body limits, read deadline/poll bound, package-size
bounds and cancellation before it is callable. No missing lower-layer guarantee
may be replaced by an application reservation or probe ledger. A failed upload
still permits bounded read-only evaluation; it never permits application resend or
the next file write after the native invocation reports failure or ambiguity.
Built-in bounded native retries remain within that one invocation, under the
[MLD destination contract](middle-level-design.md#destination-interface).
The [NuGet source basis](research/nuget-native-publisher.md) closes the selected
publisher retry boundary without supplying new runtime evidence.

Ordinary workflows request 45-day retention for required prepared CLI,
qualified packages and request/results, matching the existing workflow setting.
This is a workflow default, not a new policy expiry or platform-retention proof.
If required inputs expire or the effective platform limit cannot cover the chosen
operation, that operation cannot continue. Optional diagnostics may expire sooner.
A fresh original-revision recovery rebuilds; it does not need a permanent ledger.
Keep registry/package serialization over required verification, including remote
consumer jobs, with native concurrency and without automatic live cancellation.

### Concrete NuGet Buddy Integration

This contract selects the existing declared
`Hcoona.ReleaseSmoke.GithubPackages` product, one `net10.0` `.nupkg`, no symbols
or runtime dependencies, and GitHub Packages NuGet Buddy with development dry
runs. It adds no product, destination or native-operation authorization.

Preparation reads the exact committed release declaration, entry and quality
selection through the existing Git materialization and declaration readers.
Resolve the supported `dotnet/nuget-package-v1` build and primary package output;
unknown quality recipes, variants or required tests remain unavailable. Use the
normal selected SDK, locked restore, `Release`/`net10.0`/no RID and
`ContinuousIntegrationBuild`. Native `GetBuildVersion`, Build and
`Pack,_GetOutputItemsFromPack` use those same dimensions; compare their native
package and assembly version projections. Native reproducibility configuration
remains authoritative. Inject neither frozen versions nor a package witness.

Share concrete archive inspection, original size/digest comparison and clean
local consumption in `Ecosystems`; CI adapts its own plan/results to them.
Release does not consume `CiPlan` or `CheckSpec`. Complete preparation requires
native inputs/version, build, pack, contents, local consumer and original-byte
results, plus any actual applicable declared tests. Retain command termination
and missing/failed/cancelled results. Transfer native package properties needed
by the consumer, omitting checkout-specific physical paths.

Use the coherent official packaged NuGet 7.9.0 group: Commands, Protocol,
Configuration, ProjectModel, Packaging, Versioning and Frameworks. Replace their
SDK assembly references together; retain the selected SDK's Microsoft.Build
references separately. Resolve Newtonsoft.Json through the official package
graph rather than a second NBGV task-directory copy source; retain official NBGV.
The [publisher source basis](research/nuget-native-publisher.md#client-distribution-selection)
explains this selection. Verify resolved package and output assembly identities,
NBGV binding, native restore and consumer execution before accepting the
implementation. Source inspection and installed package availability establish
neither runtime compatibility nor live publication. Keep this client untrimmed
and JIT; no AOT claim is made.

Observe the selected `https://nuget.pkg.github.com/hcoona/index.json` through an
owned standard HTTP handler with no automatic redirects, cookies, proxies or
default credentials. Official `ServiceIndexResourceV3` interprets the service
index; require unique HTTPS owner-origin PackageBaseAddress and PackagePublish
endpoints without user information, query or fragment. Native NuGet identity
comparison owns normalized package/version equality. Require the selected
GitHub package's NuGet type, normalized name, public visibility and
`hcoona/three` repository association, then complete native version metadata and
original archive comparison. API inventory is supporting metadata with bounded
pagination, not an independent admission authority.

Bound each observation to 90 seconds and 80 HTTP reads, each body-inclusive read
to 30 seconds, metadata bodies to 2 MiB and the original archive to 25 MiB. Permit
one actual 301/302 archive redirect under the retained native storage contract:
public HTTPS DNS destination without user information, fragment or a nondefault
port, no credentials to storage and no further redirects. Signed URLs stay in
memory; diagnostic output uses sanitized origin and location digest. Compare
original size and digest. Exact, absent, conflicting and unknown observations
use the same predicate before and after publication. Unknown control, identity
or original bytes block writes; absence permits creation only with established
native package control.

The publisher constructs supported `HttpSource` and `HttpHandlerResourceV3`
resources for the selected publish endpoint, using native `NullThrottle`. Invoke
official `PackageUpdateResource.PushAsync` once for one qualified regular file,
with no symbols, `allowSnupkg=false`, `skipDuplicate=false`,
`noServiceEndpoint=true`, a 120-second native timeout and insecure connections
disabled. Official NuGet owns multipart construction and its default bounded
retries. Use sanitized typed failures and no credential-bearing native logger.
The standard handler supplies Basic authentication only to the selected NuGet
origin; the API-key delegate supplies the same scoped native capability.
GitHub metadata API reads use Bearer authentication separately. Do not inherit
credential plugins or prompts, override the testing retry handler, introduce an
application resend loop or infer finer package isolation than the platform grants.

Link parent cancellation to standard handler sends and body consumption, and
await native invocation completion after cancellation. Stopping the await does
not stop writes. Preserve actual timeout/cancellation/failure and uncertainty;
process termination may interrupt the invocation, after which only bounded reads
may run. Refresh observation immediately before publication: exact removes the
write, absent permits it, conflict or unknown stops it. A handled invocation
failure still permits bounded readback but no further application write.

Separate remote consumption uses a no-write job, exact native VersionRange,
`net10.0`, `UseAppHost=false`, no inherited Directory.Build/CPM/fallback or extra
sources, fresh external package/HTTP/plugin/profile/CLI directories and an
explicit cleared NuGet.Config mapping the selected package to the selected feed.
Generate the native restore graph and use official RestoreRunner with
DependencyGraphSpecRequestProvider and a supported source-local HTTP resource
provider. The owned read handler supplies origin-bound Basic authentication,
strips credentials at the one archive redirect and bounds downloads. NuGet owns
dependency interpretation. Check actual LockFile identity, source, configuration,
package folder and installed original size/digest, then native Build/GetTargetPath
and the marker API invocation. No witness, started ledger or graph proof record
is required.

Required completion covers destination control, coordinate, original bytes and
remote restore/build/invoke. Common result evaluation may report satisfaction
after a handled upload error only with complete fresh observations and consumer
results, preserving the command abnormality. Missing or malformed subject, file,
digest or required result cannot satisfy the request.

The ordinary NuGet Buddy entry and reusable live caller use four concrete jobs:
prepare, observe, publish and verify/result. Use the existing
Environment on the actual publisher and native package concurrency through
verification. Bind transferred control, preparation and original artifacts to
immutable artifact ID/digest, producer/run and source subject, with 45-day
retention. Product code runs outside the publication context. Development Buddy
refs and dry runs are supported; dry runs enter no write gate and make no live
claim. Retained closed-campaign readers keep their actual historical consumers;
there is no compatibility alias for old runtime schemas.

Changes preserve this contract and its governing native-invocation boundary,
with independent review before implementation. Validate application journeys, authentication separation, cancellation,
redirects, transfer/result completeness and official-client binding with controlled
contract tests and real credential-free local native package/consumer tests.
Complete affected managed/Python suites, HK, records, independent engineering/OCR
and record/research reviews, and ordinary Windows CI before caller delivery.
These checks do not prove new hosted Release/feed behavior or grant publication,
campaign replay, configuration changes or postmerge tests.

### Concrete Python Release Integration

This contract selects the existing `hcoona-release-smoke-python` release unit,
its declared wheel/sdist build and adopted Python distribution quality preset.
TestPyPI is Buddy and PyPI is Official. It adds no product, destination,
publication authority or native experiment. Source findings follow the
[native publisher assessment](research/python-native-publisher.md); closed
campaigns remain historical evidence rather than executable callers.

Preparation reads the exact committed release declaration, project entry and
quality selection using existing Git/declaration readers. Unsupported builds,
outputs, quality recipes or required tests remain unavailable. Resolve native
metadata through the maintained backend and `nbgv-python`; use the selected
CPython/UV and the synchronized root lock. Live Official requires the selected
official ref and the native public-release projection. This existing Official
caller selects `refs/heads/main`; the native Environment may constrain it
further. Buddy accepts repository branch refs; dry run does not require live ref
eligibility. The Buddy Environment `workflow-delivery-v3-python-testpypi` allows
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
original size/digest checks and clean consumers. Follow the
[existing native package contract](middle-level-design.md#native-python-package-execution):
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
checkout-specific paths are not remote identity. The selected project has no
runtime dependencies; another dependency shape needs its concrete integration.

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

Bound each observation phase to 90 seconds and nine HTTP reads, each
body-inclusive read to 30 seconds, index JSON to 2 MiB/4,096 entries and each
original to 8 MiB. Use verified HTTPS, no automatic redirects, cookies,
proxy/default credentials or insecure fallback. Accept file URLs only on the
selected public storage host with no user information or fragment. Bounded
visibility polling is read-only and remains inside those phase limits. Retain
necessary sanitized observations rather than a replayable HTTP trust ledger.

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
wheel before missing sdist, retaining already exact originals. Continue to the
next missing file only after acknowledged native success and required exact
intermediate readback. A returned failure/ambiguity or interruption stops all
further application writes even if a subsequent read confirms that file.
Bounded complete verification may still satisfy the active request; retain the
actual command abnormality and do not attribute insertion. A fresh same-revision
request can consider supported exact-subset completion after rebuilding and
qualifying its own pair. No automatic resend, rerun recovery or compensation.

Required remote consumers run in a separate no-write job for either initial
exact state or new publication. Resolve each format from only the selected
index with native UV and a generated exact-version/hash requirement. Force
wheel or sdist respectively; reject local-file/source substitution, fallback
indexes and populated caches. The original archive hash binds native resolution
to the qualified output; installed version equality alone is insufficient.
Use fresh external environments with no checkout import path. Prepare the
sdist's noneditable backend/plugin tools through the same frozen native recipe
outside publication capability, then consume in a Git-free directory with
shared build mode. Build prerequisites and the smoke's destination remain
separate native inputs. Check installed native metadata, import origin and
the selected project's API. Missing native selection/hash evidence or required
consumer results blocks completion; local-original install cannot establish
remote index routing. No witness comparison is required.

The ordinary Python entry and actual Release caller compose qualified components.
They use prepare, observe, gated publish and separate
verify/result jobs on the supported Linux runner. Build/retain complete control
before product execution; bind immutable transferred control, preparation and
original artifacts to source/ref, unit/channel/destination/mode and actual
producer/run, with the existing 45-day retention setting. Serialize by physical
registry and normalized package through required verification. Dry run uses
qualification/read/hypothetical operations and never `uv publish --dry-run`.
Exact-state and dry-run paths enter no write gate. Common result evaluation
retains attempted effects, actual command outcomes, observed state and business
satisfaction separately. Historical readers retain their actual consumers;
there is no runtime-schema compatibility alias.

Before implementing affected changes, review this contract with independent
engineering/OCR and record/research review. Validate native command/configuration
and output boundaries, original preparation/consumption, partial-state order,
failure-after-effect, cancellation, missing transfers and complete results with
controlled contract tests and credential-free local builds/tests. Run complete
affected suites, HK/hooks and exact record checks. Ordinary Linux CI must qualify
the final component/caller subject before delivery. These checks do not prove
current OIDC configuration, live registry behavior or a new publication and do
not reopen the stopped Python package or publication campaigns.

### Concrete Ruby Release Integration

This contract selects the existing `hcoona-release-smoke-ruby` unit under
`tests/private/app/workflow-delivery/fixtures/products`, its declared single gem
and adopted quality selection. GitHub Packages at
`https://rubygems.pkg.github.com/hcoona` is Buddy; `https://rubygems.org` is
Official. The [native assessment](research/ruby-native-publisher.md) supplies
the source basis. This adds no product, destination, publication authority or
native experiment and reopens no completed campaign.

Read the exact committed declaration, native gemspec and quality selection
through existing repository readers. Support the declared dependency-free
`ruby`-platform gem with no extensions. Another dependency/build/quality shape
needs a concrete adapter, rather than a generic resolver or task framework.
Buddy accepts repository branch refs; this Official caller selects
`refs/heads/main`, with any further eligibility enforced by the native
Environment. The Buddy Environment `workflow-delivery-v3-ruby-github-packages`
allows every branch (`**`) and keeps its required reviewer; the Official
Environment remains limited to `main`. Dry run does not require live ref
eligibility. Keep one faithful
source/control revision and selected native ref context; missing history or
unsupported native facts block the request. Use the existing NBGV `SemVer2` and
maintained RubyGems version projection, without an application suffix stripper,
publication-only version override or injected commit witness.

Shared `Ecosystems/RubySmokePackageOperations` owns the explicitly registered
smoke product recipe, concrete build, native archive
inspection and original local consumption inside the private application.
`CI/RubyPackageExecution` and `Release/RubyPreparation` consume those operations
while retaining distinct plan/check and request/qualification decisions. Release
does not consume `CiPlan` or `CheckSpec`. The common mechanisms use the selected
Ruby/RubyGems/NBGV tools, closed environment and native builder. Exclude inherited `SOURCE_DATE_EPOCH` for this
fixture and use RubyGems' fixed native default; do not introduce a timestamp or
archive engine. Native default timestamps do not promise byte identity across
different tools, compression, file modes or source inputs.

Qualification requires native name/version/platform, declared contents, no
runtime dependencies/extensions, GitHub repository metadata, complete original
size/digest, original local installation and a clean installed API/origin check,
plus any adopted required tests. Retain actual reached command termination and
failed/missing/cancelled checks. Portable native identity and original bytes bind
the request; checkout paths and a version string alone do not. A fresh recovery
rebuilds and qualifies its own original at the same revision. A conflicting
same-version original remains a conflict.

Observation uses a small adapter over public RubyGems APIs. Configure only the
selected `Gem::Source` in memory through `SourceList`/`Gem.sources=`. Call native
`Source.load_specs(:released)` and `Source.load_specs(:prerelease)` separately
and require both to succeed; do not use convenience results that silently omit
failed prerelease reads. Match native name/requirement/platform, fetch the native
specification and download the original through that selected native Source.
Require consistent native identity, owned fresh cache/download paths and actual
original size/digest. Missing tuples after successful complete reads can be
absent; transport/authentication/index/spec failures or contradictory facts are
unknown. An exact original can be reused; different bytes forbid publication.

Each observation uses a fresh external process/home/spec cache, a 90-second
whole-process deadline, at most 64 selected tuples, a 256-KiB JSON answer and an
8-MiB selected original. These are application process/result/original limits;
RubyGems owns full inventory transfers, decompression and native request retries.
Do not claim an underlying HTTP/inflated-index byte ceiling or replace native
fetching to simulate one. Allow bounded read-only visibility polling only within
the phase deadline. Retain sanitized business observations, not a replayable
transport trust ledger.

GitHub read credentials belong only to the read adapter's environment and
in-memory owner Source URI. Use no global authorization header, credentials in
arguments or persisted native source configuration. Native RubyGems owns its
finite HTTPS continuation: an ordinary signed storage Location without the
source URI's user information receives no original Basic credentials. Preserve
the assessment's stated redirect shape and limits; add no custom one-hop
downloader or arbitrary-origin guarantee. RubyGems.org observation is public.

Only an actual publisher receives the existing destination Environment and
write capability. Buddy uses repository `GITHUB_TOKEN` with `packages: write`,
passed to the native command as `GEM_HOST_API_KEY` with the documented Bearer
composition. Official uses the maintained pinned credential Action's
trusted-publisher path with `id-token: write`, explicit RubyGems.org server and
audience, no account credential, static key or role fallback. That Action owns
OIDC, native masking and token exchange, including its mode-0600 native
credential-file write. Give it fresh owned job home/state; exclude that state
from retained artifacts and clean only owned state. Do not claim automatic key
revocation or recreate platform authorization/MFA/scope management.

Refresh observation immediately before mutation. If state became exact, omit
the write; conflict or unknown state stops it. Invoke maintained `gem push` once
for the explicit qualified original and selected host, under a 180-second
whole-process deadline, closed stdin/non-TTY, native `--norc`, fresh working
home/cache and an allowlisted environment. RubyGems owns package interpretation,
upload, supported retry and native response handling. Do not patch private
sign-in, OTP or scope methods. Native generic auxiliary requests remain subject
to the documented selected token/platform profile; no account mutation is
authorized. No application resend, compensation or rerun recovery follows a
failure or interruption. Complete fresh observation and required consumers may
still satisfy the request; preserve the actual command abnormality and unknown
effects without attributing unique insertion.

Suppress unsafe publisher/read diagnostics before persistence, including
credential-bearing URIs and partial failure/cancellation output. Preserve actual
exit, termination, duration and uncertainty. Native runner masking does not
sanitize result files. Read adapters serialize only portable sanitized native
answers; missing/invalid/oversized output stays unknown. Configure native
`ConfigFile(['--norc'])` when calling public Ruby APIs: it ignores user/system
configuration values while retaining native platform defaults, rather than
claiming those files are never read. Own `HOME`, gem/spec/cache/temp roots and
exclude inherited proxy, Gem/Bundler credentials, `RUBYOPT` and `RUBYLIB`.
Load only prepared maintained tools; product code is absent from the publisher.

Required remote consumption runs in a separate no-write job on both initial
exact state and new publication. Native selected-source inventory/spec/download
must produce a fresh remote original; a prebuilt local-original install is not
remote routing proof. End the read-credential process, validate the actual
remote original digest/identity/contents and dependency/extension constraints,
then install that fetched original in a credential-free child using public
`Gem::Installer.at` with fresh `install_dir`, `only_install_dir: true`,
`force: false`, `ignore_dependencies: false` and no development dependencies.
Keep native dependency/runtime/platform validation. Compare native installed
name/version/platform and cached original digest, then require the selected API
and import origin in a clean credential-free child outside the checkout.
Post-install version/digest checks alone cannot justify installing unqualified
remote code with a read token. This sequence delegates source routing,
downloading, archive semantics and installation to RubyGems; it implements no
second resolver. Missing routing, original or consumer results block completion.

The ordinary Ruby entry and actual caller compose qualified components.
They use prepare, observe, gated publish and separate verify/result
jobs on the supported Linux runner. Transfer complete same-revision control
before product execution and bind immutable preparation/original transfers to
source/ref, unit/channel/destination/mode and actual producer/run. Use the
existing 45-day retention and physical registry/package native concurrency
through verification. Dry run performs qualification/read/hypothetical work
with no publication credential acquisition or `gem push`. Exact/dry-run paths
enter no write gate. The common result separates attempted effects, actual
command outcomes, observed state and required business satisfaction.

Before implementing affected changes, review this contract with independent
engineering/OCR and record/research review. Controlled credential-free component
tests and ordinary Linux CI must qualify native command/configuration/output
boundaries, original build/inspection/consumption, hash-before-install routing,
failure-after-effect, cancellation, missing transfers and complete results on
the final subject. Run complete affected suites and repository HK/hooks/record
checks. These checks do not establish current token/publisher configuration or
a new live feed/publication. Historical readers and spent campaigns remain
intact; no runtime schema compatibility alias is added.

## Behavior and Evidence Compatibility

Tests preserve retained behavior rather than old class, record-fixture or
chronology-named file layouts. Paths in the second column identify historical
scenario sources at the [former implementation][baseline], not current test
entry points. The current managed/native test owners establish replacement
behavior; historical passing suites do not qualify it.

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

## Current Integration Boundaries

The final private CLI/test projects own native facts, meaningful impact selection,
complete selected CI checks and common Release behavior. Node, .NET, Python and
Ruby groups use their native contracts and original candidate-bound plans.
Ordinary destination callers retain their actual permission, transfer, consumer
and result boundaries. The fixtures, native helpers, root tooling and project
records use their current private locations. Independently useful public
components retain their own consumers.

Closed campaign callers, the public Python control package, obsolete admission
and scanner groups, and old-basis compatibility mappings are retired. Historical
evidence readers use actual pinned producer source rather than a runtime fallback.
Later changes reconcile actual callers, selectors, workspace/version inputs,
record routing and complete affected validation together. Their source acceptance
does not qualify new live destination behavior or reopen a stopped campaign;
work and operation authorization remain with the accepted Wave and domain gates.

[baseline]: https://github.com/hcoona/three/tree/67f294cfd864a6376c14bee4a9f2ed0b92af9552
[former-plan]: https://github.com/hcoona/three/blob/67f294cfd864a6376c14bee4a9f2ed0b92af9552/src/public/lib/three-workflow-delivery-v3/docs/migration-strategy.md
