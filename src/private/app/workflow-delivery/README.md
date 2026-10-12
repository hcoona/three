# Workflow Delivery

This private application implements the replacement Workflow V3 core. The
[project records](../../../public/lib/three-workflow-delivery-v3/docs/README.md),
[middle-level contracts](../../../public/lib/three-workflow-delivery-v3/docs/middle-level-design.md)
and [migration plan](../../../public/lib/three-workflow-delivery-v3/docs/migration-strategy.md)
remain in their current namespace until the old Python application's last
consumer retires. This entry describes the implemented interface, not a second
architecture authority.

## Official npm Dry Run

`release prepare-npm <request.json>` independently qualifies one declared npm
Release Unit from the exact selected checkout, without a CI plan. Its finite
adopted recipe uses native NBGV, locked mise Node24/PNPM, build/test/pack,
bounded content inspection and fresh offline installed API consumption. The
caller prepares frozen script-disabled dependencies and builds the selected
control before product execution. Preparation returns complete original file
identities, required checks and actual native command outcomes; failure remains
nonzero and never exposes a qualified file set.

`release observe-npmjs <preparation.json> <original-directory>` checks the actual
original files and reads exact public npmjs metadata and tarball bytes. Reads
use one 60-second operation deadline, at most 20 seconds per request including
bodies, five redirects, the public HTTPS origin only, identity encoding and
bounded responses. Complete metadata 404 is absent; transient or incomplete
reads are unknown; unsupported metadata/origins are unprovable; different
complete bytes conflict. No authentication, tag projection or remote witness
parsing participates in this caller.

`release describe-npmjs <preparation.json> <observation.json>` reports whether
the dry run can describe native creation or already exact state. It never
reports live completion. The existing Official simulation workflow runs these
commands without publication capability and retains original files/results for
45 days. Its source contract and controlled tests establish application behavior;
they do not establish a new live npmjs publication or hosted dispatch result.

The common Release state component preserves raw upload outcomes separately
from complete business state. Initially exact state and readback after an
abnormal upload use the same completion predicate; abnormal uploads stop later
writes. Other destination callers retain their implementations until their
complete permission, transfer and required-consumer cutover.

## npm Buddy Release

The npm Buddy workflow uses one selected same-repository source/control revision,
including development branches. Preparation resolves the existing adopted product
with native NBGV and locked PNPM, builds/tests/packs its complete original tarball
and performs clean local consumption independently of CI. Prepared control is
uploaded before product execution, separately from original files and results.
The receiving jobs consume immutable artifact IDs and validate the selected
producer and request. Required artifacts request 45-day retention.

`release observe-github-npm <preparation.json> <originals-directory>` verifies
the existing public GitHub package's native owner/repository association, exact
version metadata and original bytes. Only complete authoritative version absence
can permit creation; an absent package container cannot. Reads have a 60-second
operation limit, 20-second complete-response limit, five redirects, 1 MB metadata
and 25 MB tarball bounds. Credentials go only to GitHub API/registry origins and
are stripped across storage redirects. Results omit signed URLs and credentials.

`release publish-github-npm <preparation.json> <originals-directory>
<fresh-runtime> <toolchain-bin>` reobserves after native Environment approval on
the actual publisher job. It removes unnecessary writes, then invokes the existing
qualified Node 24.19.0/npm 11.17.0 standard publish once with lifecycle scripts and
fetch retries disabled. Its 120-second native outcome remains separate from
business completion. No witness, approval mirror, Governance expiry or durable
mutation marker is used. Product evaluation/consumption has no write capability.

`release consume-github-npm <preparation.json> <observation.json>
<originals-directory> <fresh-runtime> <toolchain-bin>` performs a required native
scoped exact-version npm install and adopted API import in a separate read-only
job. Fresh configuration/cache excludes ambient npm state. It consumes npm's
generated lockfile result and original SRI rather than implementing resolution.
Its selected tarball URL is compared by digest to avoid retaining signed URLs;
original byte identity remains SHA256/size. Installation and import have
300-second and 30-second limits. Native metadata/originals are read again afterward.

`release complete-github-npm <preparation.json> <observation.json>
<consumer.json> <publication.json|->` requires complete native state and remote
consumption on both initially exact and postupload paths. A handled abnormal
upload can satisfy only through complete observation/checks, preserving its actual
failure. Missing results cannot prove non-start. Native workflow concurrency
covers the fixed physical package through verification without canceling active
publication. `release describe-github-npm` reports dry-run hypothetical writes
without live completion or a write gate.

The ordinary entry and former reusable live workflow are replaced together.
Historical acceptance and other ordinary ecosystem consumers still use their
retained Python helpers. Source/controlled tests do not establish a new hosted
Buddy run, live publication or reopened campaign.

## Native NuGet Publisher Component

[`GithubNuGetPublisher`](Destinations/GithubNuGetPublisher.cs) validates the
qualified original package and invokes the official NuGet
`PackageUpdateResource.PushAsync` once. NuGet constructs the upload and owns its
bounded retries; the application adds no multipart encoder or resend loop.
Only the existing Buddy package and exact GitHub Packages endpoint are supported.
Dry runs, incomplete required checks and changed original identity or bytes fail
before transport. Original files must remain exclusively owned and unchanged
through the native invocation, which opens the validated file itself.

The owned standard HTTP handler disables redirects, ambient credentials, cookies
and proxies. Basic authentication and the native API key go to the exact upload
origin. Parent cancellation reaches native sends and response-body operations;
the component awaits native completion rather than abandoning an active upload.
The native timeout is 120 seconds. Its typed result preserves command success,
parent cancellation, sanitized failure type and duration separately from business
completion. Controlled tests exercise the actual official client through supplied
transport. The [NuGet Buddy caller](#nuget-buddy-release) integrates this component
with the ordinary Release CLI and workflow. Controlled component tests establish
no live publication, remote-consumer or platform qualification.

## Native Ruby Primitives

[`Native/Ruby/facts.rb`](Native/Ruby/facts.rb) serializes evaluated RubyGems
source/archive metadata and frozen Bundler dependency answers. The process must
activate the lock-selected Bundler before the bundle operation; the helper checks
actual activation against the native locked version. Source evaluation requires
prepared project inputs. Path and active local Git answers retain native source
locations; registry and runtime metadata dependencies are not repository owners.
The helper does not parse Gemfiles/locks or resolve dependencies.

[`RubyNativeFactsReader`](Repository/RubyNativeFactsReader.cs) consumes the
structured source-gem, bundle and archive answers as separate values. It binds
the requested paths and activated/locked Bundler version, preserving native
Gemfile inputs, dependency requirements, content lists and source locations.
Original local sources and installed directories remain distinct.
The separate lock-context answer reads Bundler's public locked metadata without
executing the Gemfile. Its installed probe version and locked selected version
remain distinct; it neither establishes frozen equivalence nor supplies resolved
facts. Complete bundle collection still requires the actual locked library and
frozen operation. The version answer preserves the supplied NBGV SemVer2, uses
RubyGems for the existing metadata-stripping projection and Ruby for its source
literal; missing native versions cannot become placeholders.
Complete caller ownership, paired planning and product execution retain the
pending boundary below.

[`RubyNativeFactCollector`](Repository/RubyNativeFactCollector.cs) invokes that
helper through the existing native process interface with caller-prepared tools,
environment and scratch space. Bundle queries activate the selected Bundler and
set the original Gemfile and frozen mode. Native failure and cancellation cannot
supply successful facts; the typed reader still binds each returned answer.
This entry does not prepare products, discover projects or resolve dependencies.

[`RubyDependencyProjection`](Repository/RubyDependencyProjection.cs) joins supplied
native local source/gemspec coordinates to source producers and follows resolved
runtime edges, including registry intermediates, for transitive impact. Bundle
consumers retain their native Gemfile/lock inputs and local producers separately;
a bundle-only consumer does not gain a Ruby package or check. Installed cache
paths and matching registry names do not establish local ownership.

[`RubyRepositoryInventory`](Repository/RubyRepositoryInventory.cs) discovers
committed source gemspecs and Gemfile or `gems.rb` entry points without
interpreting their declarations. It requires one native answer for every
discovered entry point, including a source gemspec not loaded by its adjacent
bundle. Multiple source gemspecs or competing bundle entry points in one directory
remain unsupported rather than being selected by Workflow.

[`RubyInputConsumers`](Repository/RubyInputConsumers.cs) binds supplied native
answers to committed project inputs, native NBGV inputs and effective quality
declarations. Project input scope includes tests outside package membership and
uses the nearest nested source owner. Native evaluated Gemfile/lock inputs must
be committed. Bundle-only consumers retain their original inputs and local
producers separately for their actual ecosystem caller; they do not become Ruby
check owners. Root source-gem ownership remains unsupported. These supplied-fact
components do not establish arbitrary dynamic Ruby IO closure. The concrete
collector still needs bounded project preparation, supported input-scope
validation, paired selection and cross-group caller composition.

[`RubySourceCollector`](Repository/RubySourceCollector.cs) queries prepared NBGV
in each committed source project's original directory and collects evaluated
source metadata. Its finite integrations are Latexmath's existing native NBGV
gemspec and the smoke gem's generated native version source. The latter is
materialized only in the caller-owned disposable checkout before evaluation;
committed generated version inputs are rejected. Native name/version must match
the supported integration's projection. This is metadata-query input preparation,
not selected build/install/check preparation; it adds no provenance witness and
does not establish arbitrary dynamic Ruby input closure. Controlled command/Git
scenarios cover the composition, not actual product qualification. Complete
paired collection, supported input-scope validation and caller ownership remain
pending.

[`RubyBundlePreparation`](Repository/RubyBundlePreparation.cs) installs Bundler
through native gem commands, asks the probe library for each original lock's
selected version and invokes that Bundler's frozen install. It requires the
committed native lock and preserves actual preparation commands and failures in
memory. The caller owns its tools, environment and disposable installation state;
source metadata preparation must precede a bundle that loads generated source
inputs. Empty bundle inventory installs nothing. Controlled preparation coverage
does not qualify actual dependency installation or complete caller composition.

[`RubyRepositoryReader`](Repository/RubyRepositoryReader.cs) composes actual native
source and bundle collection for a caller-bound endpoint with prepared tools and
dependencies. Bundler's native lock context selects the activated library; the
full frozen answer remains required. Original source paths, native NBGV inputs,
quality ancestors and actual adopted gem declarations remain distinct. Sources
without quality adoption stay input-only producers. Supported adoption expands
whole-project RSpec or the gem's complete distribution checks. Concrete root CI,
imported caller and shared tool inputs target those actual quality owners through
operation-input associations, without changing native producer membership. This
reader does not install dependencies, certify dynamic IO closure or wire paired callers;
controlled Git/command scenarios do not qualify native products or transfers.

[`RubyFactsAssembler`](Repository/RubyFactsAssembler.cs) joins supplied native
producer relations across both committed endpoints before projecting actual
quality owners. Input-only source gems remain dependency intermediaries, including
cycles and removed producers. Selection reasons retain each initiating path and
revision; source moves keep their separate coordinates. Nearest nested source
ownership prevents implicit parent quality adoption. Unknown input scope fails
in full mode too; a caller-certified empty operation input remains distinct.
The shared core expands complete selected obligations and collects results.
Bundle-only consumers remain separate for later actual Node composition. This
controlled paired assembly neither collects native endpoints nor qualifies the
complete Ruby caller.

[`run_ruby_ci_group.py`](../../../../eng/scripts/run_ruby_ci_group.py) receives
native scope/control artifacts, materializes both exact endpoints and transfers
one preparation and paired plan into complete original-result collection.
The paired readback retains both native fact endpoints once; Node planning can
receive their typed contribution through its existing caller. An empty selection
collects the original empty plan without product preparation. These caller seams
have controlled, local Git and isolated native caller coverage. Root CI now
also receives the complete candidate control artifact in `ruby-group`, then
prepares, plans and executes the original Ruby group. Ordinary [PR #1123](https://github.com/hcoona/three/pull/1123) CI qualified
the complete group. Root Ruby and shadow PR duplicates are retired with the
adopted fixture relocation. Node receives the original paired fact artifact
from the same run; selected Hexo jobs use native locked Bundler preparation.

[`RubyNodeInputConsumers`](Repository/RubyNodeInputConsumers.cs) joins supplied
Ruby facts to PNPM projects at the same Git endpoint. Native bundle directories
identify their Node consumers; Bundler's resolved local producers carry source,
version and quality inputs into existing Node operation inputs. The original
paired Node planner retains removed-source reasons and PNPM reverse consumers.
Ruby-owned bundles keep their ecosystem; an unmapped bundle-only consumer fails.
Controlled planning coverage does not qualify actual caller preparation or native
products. Root CI transfers the same-run original fact artifact to Node.

[`RubyRSpecResult`](CI/RubyRSpecResult.cs) consumes complete public CLI JSON,
retains original examples/messages and counts, and keeps them separate from native
command termination. Missing/incomplete results, failed commands, failed examples
and errors outside examples cannot satisfy a required check. Pending examples
remain available to adopted project quality policy.

[`RubyProjectChecks`](Repository/RubyProjectChecks.cs) expands an explicit
`ruby/rspec-v1` selection to the required whole-project `ruby/project-rspec-v1`
check. Latexmath's source-local quality declaration adopts its existing RSpec
obligation. [`RubyRSpecExecution`](CI/RubyRSpecExecution.cs) uses caller-prepared
Ruby, locked Bundler, Gemfile and environment to invoke the original entire suite
with public JSON formatter/output options. It preserves the native command and
original report separately, rejects absent or incomplete output and cleans its
owned result file. Controlled tests cover this recipe; they do not qualify the
complete product or caller.

[`RubyGroupReader`](Repository/RubyGroupReader.cs) binds exact prepared endpoints
and transferred scope to one native paired selection, retaining the original
candidate sources, bundles and release units for execution. Other retained jobs
can establish non-Ruby responsibility; their declarations cannot suppress actual
Ruby input consumption. Controlled collection/transfer tests do not qualify
native tool preparation, product execution or the complete hosted caller.

[`RubyGroupPreparation`](Repository/RubyGroupPreparation.cs) prepares the candidate
control manifest's NBGV tool through native .NET installation and gives both
committed endpoints separate disposable home, gem, bundle and query state. It
binds the exact Git/scope inputs before installation, prepares source version
inputs before frozen Bundler evaluation and returns the existing paired-reader
request with original native commands. Caller-supplied tool paths and the probe
version do not become another tool declaration. Controlled command/Git scenarios
cover this boundary; actual dependencies and hosted caller qualification remain
pending.

[`RubyPackageExecution`](CI/RubyPackageExecution.cs) implements the adopted gem's
native build, archive inspection and clean local installed-require checks against
the original candidate plan. Source version projection remains native; RubyGems
owns archive semantics and installation. It retains the original gem and actual
commands/results, rejects missing or substituted outputs and skips consumers
when prerequisites fail. Its clean consumer has separate GEM_HOME/GEM_PATH and
no checkout Ruby load path. Controlled execution does not qualify the actual
product or complete workflow transfer; caller integration remains pending.

The concrete build, native archive inspection and clean local installation now
live in [`RubyPackageOperations`](Ecosystems/RubyPackageOperations.cs). The shared
operations use native fact/tool values and an original archive identity, with no
CI plan, check or Release completion object. CI retains candidate/check validation,
prerequisites and actual command/result recording. Release preparation and caller
cutover remain separate pending work under the accepted
[concrete Ruby contract](../../../public/lib/three-workflow-delivery-v3/docs/migration-strategy.md#concrete-ruby-release-integration).

The `ci prepare-ruby-group`, `ci plan-ruby-group` and `ci run-ruby-group`
commands transfer these existing preparation, paired selection and execution
objects through the strict JSON interface. Execution exit status is collected
against the original plan and actual results; a supplied success outcome cannot
hide a missing required result. Controlled CLI transfer does not qualify actual
native preparation, product execution or root/shadow caller cutover.

[`RubyGroupExecution`](CI/RubyGroupExecution.cs) combines the original selected
RSpec and gem obligations against exact prepared candidate facts. It binds the
checkout and validates both finite recipes before execution, invokes whole-project
RSpec with its original native bundle, and passes the unchanged gem checks to the
package component. Independent failures remain visible; cancellation retains all
original required keys. Empty selection performs no product preparation. Native
RSpec reports and gem commands/outputs remain separate; complete hosted caller
and actual product qualification remain pending.

[`RubyPackageChecks`](Repository/RubyPackageChecks.cs) expands the existing
`ruby/hcoona-release-smoke-ruby-v1` recipe from its actual release-unit declaration
and evaluated native source identity. Its required gem build, content inspection
and clean installed require checks keep original output associations and explicit
prerequisites. A Gemfile or spec directory is not a distribution prerequisite.
This finite recipe currently accepts the adopted dependency-free, platform-neutral
smoke gem; other product recipes require their own adaptation. Expansion is
controlled contract behavior, not native package execution or caller adoption.

The isolated native contract tests are a root pytest target:
[`test_facts.py`](../../../../tests/private/app/workflow-delivery/Native/Ruby/test_facts.py).
The Python CI caller prepares the two Bundler environments only when this target
is selected; its Ruby runner setup and isolated native tool installation precede
the original selected-plan execution. For explicit local invocation, prepare Ruby
and both supported locked Bundler libraries, then supply
`WORKFLOW_DELIVERY_TEST_RUBY` with the Ruby executable and
`WORKFLOW_DELIVERY_TEST_RUBY_GEM_HOMES` with a JSON mapping from `2.4.20` and
`2.7.2` to their prepared Gem homes. Invoke this file explicitly with pytest;
it creates native-locked local fixtures and uses no personal Bundler configuration.
The RSpec scenarios use the prepared `2.4.20` environment with RSpec `3.13.6`.
Their original public CLI outputs also supply the managed parser fixtures.

These components do not yet implement complete native paired Ruby planning, project preparation
or complete workflow execution. Existing root/shadow Ruby callers,
manual Release and historical evidence readers remain with their migration groups.
The complete Latexmath suite still requires the existing runner's declared
conversion tools; fixture success is not full product/caller qualification.

## Prepared Python Endpoint Collection

`PythonRepositoryReader` composes an exact materialized endpoint's existing native
graph and pytest readers, committed quality/release declarations, managed NBGV
input associations and maintained backend package identity. Native membership
remains the complete producer graph; only configured pytest targets and adopted
distribution checks become runnable owners. Missing native facts or incomplete
quality adoption fail collection. `PythonGroupPlanning` joins both endpoint
answers and repository input reasons through the existing paired assembler and
shared impact planner, retaining explicit full mode and explainable empty scope.

`ci plan-python-group <request.json>` binds the existing transferred scope to both
exact checkouts, requires one actual interpreter and one supplied control helper set,
and collects each endpoint before selection. Other retained job/record
responsibilities resolve unrelated repository coordinates without supplying Python
targets or suppressing native Python effects. Legacy Python heuristics cannot
declare unknown scope unaffected. The readback carries the original plan and
candidate-native membership, configuration, targets and operation for execution.

`ci prepare-python-group <request.json>` binds both supplied checkouts to their requested
Git revisions before preparation, then prepares the candidate control sources with
native manifest-selected Rust and locked Cargo compilation, and installs
each endpoint's frozen, noneditable query-tool groups after native NBGV tool restore.
Its owned credential-free contexts keep installed helper environments separate from
absent metadata/activity environments, preserve the prepared NBGV context for later
queries, and supply the existing group request without selecting or executing products.
Prepared query launches replace inherited environment settings with the owned
context at execution, including after a transfer. Required native preparation
failures and cancellation stop completion. Failed preparation retains original
command observations through the caller failure channel; the CLI writes them to
owned local diagnostics and reports an attributed failure without exposing raw
streams or a completed request.

Controlled Git/command scenarios cover collection and supplied-command preparation.
The concrete caller below composes these interfaces. Controlled validation does
not establish its actual Linux installation, compilation or complete hosted
execution; ordinary PR CI owns that qualification. Historical native campaigns,
Release and publication remain separate.

## Native Python CI Caller

Root CI's existing Linux Python job receives the exact scope and candidate control
artifact IDs with native producer/lifetime checks and Actions download integrity.
`eng/scripts/run_python_ci_group.py materialize` creates fresh Git worktrees at the
transferred basis/candidate before maintained setup actions select both endpoints'
NBGV SDKs. The source archive retains the complete candidate `Native/Python` tree;
the managed control distribution is transferred unchanged.

The `plan` phase composes `ci prepare-python-group` and one
`ci plan-python-group`. The `execute` phase passes that original mixed plan and
candidate-native readback to `ci run-python-group`, retaining every result and
its original outcome. Failed preparation, missing transfer/output and cancellation
cannot become no-work. Explainable empty selection performs no product or
auxiliary preparation. Only actually selected retained V3/native helper targets
prepare their existing native adapters. Selected V3 or AzureAuth bundle tests
receive the installed PowerShell directory in their product PATH; unrelated work
does not require that tool. AzureAuth apphosts retain configured native .NET
runtime roots, and V3 HK consumers retain the configured native HK profile.
Absent configuration keeps native defaults. Owned query, NBGV and product
environments remain distinct; selected HK/Pkl use explicitly prepared mise tool data.
Selected V3 execution disables native mise task and exec automatic installation;
its child queries consume the prepared adapters without installing unrelated tools.
Selected V3 preparation calls native `mise trust` for the candidate configuration
in that same owned HOME before its native queries. Mise owns the resulting local
configuration state.

Windows scope no longer parses Python dependency declarations or pytest targets,
nor emits Python package/test selection. Its committed path responsibility tells
other ecosystem groups about Python coordinates; it cannot suppress Linux Python
analysis. The separate `nuget_reproducibility` output retains the ordinary Windows
managed recovery check. Azure and other special jobs retain their own consumers.

`mise run test:python` uses the same native group in explicit full mode on Linux.
It names the committed HEAD and retained external result directory, rejects
tracked edits and unsupported legacy pytest arguments, and builds the control
application from a fresh checkout of that exact commit. It tests that committed
subject, including adopted distribution checks. Use `--directory` to retain a
chosen external result carrier and `--mise-data-directory` to specify installed
native mise adapter data. Windows native caller support is not claimed here.

Root CI replaces the smoke workflow's PR entry; that workflow retains only its
separately gated manual Release path, qualification-failure readback and current
historical evidence consumers. This caller change does not supply publication,
dispatch or stopped-campaign authority. Complete native caller qualification
requires ordinary authorized PR CI; controlled Git/process tests supply boundary
coverage only.

## Current Scope

The application selects CI checks and collects candidate-bound check results.
The supplied-fact command and Node revision-to-plan command share one impact
algorithm and comparison/responsibility binder. The core accepts exact committed
ownership and resolved preset/native-retained check origins. The native .NET group
command joins both endpoints, completes selected checks and collects their original
plan through the existing executors. Root CI consumes this interface through its
existing Windows scope and .NET execution jobs, qualified for the current Debug
operation in [PR #1054](https://github.com/hcoona/three/pull/1054). The Node command reads exact
caller-prepared checkouts, native
PNPM relations, NBGV inputs and existing application declarations, then expands
selected project and package checks. A concrete Node executor runs the adopted
quality recipe and retains every check result and original package output.
The adopted fixture has a bounded Linux
[native build/package/consumer observation](../../../public/lib/three-workflow-delivery-v3/docs/research/node-ci-native-execution.md).
The GitHub event comparison command reads native payloads and resolves comparison
endpoints under the MLD's finite mapping. Root CI now uses this comparison and
native control-test resource inputs in its existing Windows scope job. The
existing .NET job remains the sole general .NET test executor. The static Ubuntu
Node group joins complete endpoint inputs, prepares the candidate control CLI,
executes the adopted fixture in separate Node 22/24 checkouts and collects both
result sets against their original parent plan. The retained Node matrix consumes
only its selected native members. The configured Node 24 variant preserves its
committed locked installation. The existing moving Node 22 variant uses native
unconfigured installation with locked mode disabled for that invocation only;
subsequent execution retains the configured SDK/PNPM locks and bounded native
automatic-install policy. Other quality presets and Release commands
remain pending. Ordinary hosted qualification in
[PR #1042](https://github.com/hcoona/three/pull/1042) covers this group's immutable
scope transfer, all seven adopted obligations and selected retained members on
Node 22/24. The exclusive npm shadow workflow, record/planner family and bootstrap
projection are retired; Python, Ruby and Release retain their current callers.

The finite `ci_scope.record_control_owners` helper derives local record-checker
and regression-script responsibility from each committed control entry and
native `mise tasks info` output in its exact endpoint checkout. It retains those
authority coordinates and advisory local semantics without executing either
task or adding CI selection. The group caller joins these endpoint reasons with
the received scope; native product/control consumption remains separate from
responsibility.

New ecosystem integrations follow the HLD's
[native integration boundary](../../../public/lib/three-workflow-delivery-v3/docs/high-level-design.md#native-integrations).
PNPM integration invokes the repository-selected CLI directly. The implemented
selection/result core consumes resolved facts and does not
interpret ecosystem manifests, dependency specifiers or version inheritance.

The internal impact-selection operation also accepts caller-bound paired
`SelectionReason` roots under the MLD's
[quality-owner transfer contract](../../../public/lib/three-workflow-delivery-v3/docs/middle-level-design.md#python-quality-owners-and-input-only-members).
It validates changed paths, initiating revisions and actual union owners before
the ordinary relation, Release Unit, check and prerequisite closure. This seam
does not change the supplied-fact command format or establish native caller
qualification. The controlled Python assembly below consumes this seam.

The Python passive extractor returns declared build strings and maintained
plugin coordinates. The pinned Rust supplement and ordered configuration
preflight have controlled unit boundaries. Group answers use the native current
or exact singleton manifest, native flattened declarations and native
default/no-dev selection. Root or other member groups cannot become another owner's
defaults. Selected groups with empty native requirements need no metadata node;
selected empty groups carrying Python constraints stop with an attributed diagnostic
until a constraint-preserving transfer exists. Nonempty metadata associations remain
strict. The
[current-tool observation](../../../public/lib/three-workflow-delivery-v3/docs/research/python-native-graph-qualification.md#completed-ordinary-and-no-dev-observation)
qualifies the exact complete ordinary/no-dev graph collector composition on its
pinned Linux subjects. Its campaign and owned cleanup are complete; remaining
captures are stopped. Metadata adaptation and operation dependency projection
retain native IDs, dimensions and build-source associations. The separate
[planning-input observation](../../../public/lib/three-workflow-delivery-v3/docs/research/python-planning-enrichment-qualification.md#completed-planning-input-observation)
qualifies native pytest, committed NBGV/quality inputs and producer input mapping
on the exact retained Linux subjects. The [native Python CI caller](#native-python-ci-caller)
implements the complete mapping; ordinary hosted qualification remains required under the
[planning contract](../../../public/lib/three-workflow-delivery-v3/docs/middle-level-design.md#native-python-planning-facts).

Native integrations must supply complete ownership and reverse-consumer facts for
their declared scope, including base and candidate relations, selected quality
presets and all required variants. They must report incomplete evaluation or
unsupported relevant shapes in `errors`. An empty error array is a producer's
result, not an admission certificate or proof that a reader is implemented.
The scope label must agree across revisions; it is not a coverage inference.

### Python Release Preparation Component

[`PythonPreparation`](Release/PythonPreparation.cs) qualifies the selected complete
wheel/sdist declaration independently of CI impact selection. It reads committed
release and quality inputs, queries locked native MISE tool selection, restores
the committed NBGV tool and uses shared frozen UV/backend preparation. The caller
supplies MISE's existing installation directory as an execution resource; it is
not another tool-version declaration. Product builds and consumers use fresh
external state with no publication credentials.

Hatchling, `nbgv-python` and maintained packaging supply version, public-release,
commit, dependency and interpreter facts. Live Buddy accepts branch refs for
TestPyPI; live Official requires `refs/heads/main`, PyPI and the native public
projection. Dry run can qualify another ref and retains its native version.
The selected project's branch-wide NBGV public-version mode shapes versions for
CI and Release; it does not authorize Official publication.

Preparation requires build, contents, original wheel consumption, Git-free
original sdist consumption and unchanged originals. It retains actual reached
command outcomes and complete failed/cancelled/timed-out/skipped checks. Files
carry portable native coordinates, declared output identities, sizes and digests.
Controlled scenarios and a native Linux local fixture exercise this component;
they do not establish feed, OIDC, publication or hosted caller behavior.
The existing Linux Python project-test path selects
`tests/eng/test_python_release_preparation_gate.py` from its consumed inputs.
That target invokes the native scenario using committed Python/UV tools and its
selected MISE resource, rejects zero/skipped results, and retains original TRX,
preparation and distribution attachments through the Python job. It is a
component qualification route; ordinary caller qualification remains separate.
Ordinary Release caller integration requires its separate qualification under the [accepted contract](../../../public/lib/three-workflow-delivery-v3/docs/migration-strategy.md#concrete-python-release-integration).

### Python Package Execution Component

`ci run-python-package <plan.json> <request.json>` executes the selected adopted
package partition. The original plan retains complete build, contents, wheel
install/import and Git-free sdist build/install/import as four required checks.
The request supplies caller-bound native members, the prepared checkout, fresh
external scratch and native tool/helper coordinates. The executor validates the
committed release declaration and selected check contract before native effects.

[`PythonPackageOperations`](Ecosystems/PythonPackageOperations.cs) supplies frozen
tool preparation, native identity/build, original distribution inspection and both
clean consumers inside the existing application. It consumes no CI plan, check
or Release outcome. CI retains committed obligation validation and adapts each
reached native command and result to its original four-check plan. The
[ordinary Python Release caller](#ordinary-python-release) integrates these
components under the
[concrete contract](../../../public/lib/three-workflow-delivery-v3/docs/migration-strategy.md#concrete-python-release-integration).

UV prepares the existing frozen root groups with native workspace editability
through creation of both originals and owns PEP 517 dispatch with native
build-dependency checking. Before Git-free sdist consumption, one frozen sync
reconciles the same tools environment to noneditable backend/plugin installation.
Conversion failure, cancellation or timeout stops sdist consumption and preserves
the completed original-wheel result; changed original bytes stop the consumer.
[`packages.py`](Native/Python/packages.py) consumes maintained backend metadata,
normal distribution metadata and standard archive readers; its registered
quality check owns the adopted product's payload and installed API. It introduces
no dependency inventory, backend resolver, version conversion or V3 witness.
The serialized result retains original wheel/sdist file paths, declared outputs,
lengths and SHA256 identities. Callers retain the original artifact files for
downstream transfer and consumption.
Command failure, timeout, cancellation and prerequisite skips remain visible.

Controlled scenarios cover phase preparation, failure/cancellation and CLI transfer.
The [completed corrected Linux package observation](../../../public/lib/three-workflow-delivery-v3/docs/research/python-native-package-qualification.md#completed-corrected-linux-package-observation)
independently qualifies the exact four-check integration, including original wheel
and Git-free original-sdist consumers. It retains the audit's separately classified
filename-discovery method exception. The package campaign is stopped: one lifetime
spent, both unused package slots expired. Historical failures remain in that evidence
record; they grant no replay. The [native Python CI caller](#native-python-ci-caller)
implements root pytest and adopted package execution together, with separate
ordinary hosted qualification. Release retains its own qualification and cutover requirements.

### Finite Python CI Execution Component

`ci run-python-group <plan.json> <request.json>` receives the original mixed plan
and native membership, pytest configuration/target bindings and operation context.
The request contains `native` (checkout, fresh external scratch, UV/interpreter,
members, configuration, targets, operation, deadline and environment) and
`packageHelper`. Both supported check partitions are validated against the exact
candidate before either dispatches. It adds no target roster or dependency resolver.
For selected work the JSON receiver reuses `uv workspace metadata --frozen` once,
with a credential-free, offline query environment and an absent owned project
environment. It checks the complete native name/directory mapping before product
preparation, then supplies that native answer to both partitions. Opaque IDs stay
in the receiving native context; no cross-checkout spelling guarantee is assumed.

[`PythonPytestExecution`](CI/PythonPytestExecution.cs) prepares a separate frozen
UV environment for root plus selected native members, retaining native default
groups and editability. Each selected target runs through native pytest with the
bound configuration, using `uv run --no-project --python <prepared-environment>`.
The project-environment selector is preparation-only; product execution removes
it and query-only safe-path settings so nested UV calls own their project
environments and product imports retain native Python behavior.
Preparation failure leaves all targets unsatisfied; independent
target failures and cancellation retain complete required results.
[`PythonGroupExecution`](CI/PythonGroupExecution.cs) keeps separate pytest/package
scratch, preserves package obligations after pytest failure, and collects both
partitions against the original plan. Empty partitions perform no product preparation.
The archive consumer's Git-free scratch boundary is checked before either dispatch.

Controlled Git/command-transfer scenarios cover selection, preparation, partitions,
results and cancellation. They do not qualify actual root pytest, complete paired
planning/callers, hosted Python CI, Windows or Release. The
[native Python CI caller](#native-python-ci-caller) implements the replacement
routing; the complete consumer group must be qualified together under the
[native Python CI group contract](../../../public/lib/three-workflow-delivery-v3/docs/middle-level-design.md#native-python-ci-group).

### Python Passive Extraction

[`passive.py`](Native/Python/passive.py) accepts one project-directory argument
and returns one complete JSON response: resolved `directory`, nullable
`build_requirements` and nullable `nbgv` coordinates (`working_directory` and
`generated_file`). A missing build table leaves the build requirements absent;
quality/group contexts do not receive a synthetic backend. Explicit named
Hatchling tables without backend paths are admitted. Structural guards reject
additional UV settings before extraction; source/default resolution remains a
later native responsibility.

PyPA `ProjectBuilder.build_system_requires` supplies unchanged strings;
`nbgv_python.config.PluginConfig.from_mapping` supplies plugin coordinates.
There are no requirement/default/source parsers or backend/generation calls.
Failures produce a nonzero exit without partial facts. Physical request guards
explain the unsupported shape using fixed application messages; native failures
report the exception type without forwarding raw messages. The caller must bind
the actual directory and native request before using these outputs for planning.
Scoped native warning capture prevents source-valued warning diagnostics from
escaping that response boundary while preserving native success/error outcomes.

The root `workflow-delivery-python` dependency group prepares pinned PyPA build
and the existing workspace plugin through the ordinary UV lock. This is helper
tool preparation, separate from project build environments. The
[unit tests](../../../../tests/private/app/workflow-delivery/Native/Python/test_passive.py)
substitute the native property/config boundaries; root pytest owns them and
selects them for helper or maintained-plugin source changes. They do not qualify
actual PyPA extraction, native lowering, complete ownership/activity/closure or
runtime effects. The retained native observations qualify their exact subjects;
the [native Python CI caller](#native-python-ci-caller) implements planning and execution
with ordinary hosted qualification required. Release remains separate.

`passive.py --configuration-only <directory>...` applies the same physical UV
table/key guard to supplied project and ancestor contexts. It returns their
actual `configuration_inputs` paths only after the complete batch succeeds.
It invokes neither PyPA extraction nor the NBGV plugin for those contexts;
UV still owns settings and workspace/source interpretation.

### Python Native Supplement

The [Rust helper](Native/Python/uv/Cargo.toml) uses UV's exact source pin for
requirement parsing/build-source lowering, native workspace identity, group
defaults, interpreter markers and Python constraints. It admits native
configuration before conversion and rejects necessary unsupported sources before
fetch-capable lowering. It does not resolve/install build environments, execute
backends or compute Workflow's impact closure. Its JSON request contains
workspace/interpreter/cache coordinates, passive project build strings, group
operations and opaque marker/constraint IDs. Failed queries return a fixed
attributed diagnostic without partial facts or raw native errors.

[`PythonNativeSupplementReader`](Repository/PythonNativeSupplementReader.cs)
collects the same project/root-ancestor preflight contexts, runs the
configuration-only Python call and checks its complete returned input set before
writing the transient native request and invoking Rust. Failure, cancellation or
an incomplete preflight result prevents the Rust call. Both stages use the
caller's supplied native environment through the existing bounded process
runner. The caller supplies exact checkouts/tools, a fresh external scratch
directory and owned credential-free configuration/cache/temp roots; a dictionary
or input-path response does not prove that isolation. The caller retains and
cleans its scratch state. The Rust entry requires this ordered composition;
direct invocation does not supply the physical preflight.

The helper's [native toolchain file](Native/Python/uv/rust-toolchain.toml) selects
the compiler; `Cargo.toml` declares its minimum version and exact UV pin, while
the helper-owned lock fixes transitive dependencies. From its directory, run
`rustup show`, `cargo build --locked --all-targets`, `cargo test --locked`,
`cargo fmt --all -- --check` and `cargo clippy --locked --all-targets -- -D warnings`.
The existing Ubuntu Python CI job performs those source checks when the passive
helper test root is selected. Pure parsing/admission/transport tests and
controlled C# command responses do not qualify actual native workspace,
interpreter, lowering, metadata or effects. The separately retained primitive
observation qualifies the pinned ordered helper composition on its Linux host.
The separately accepted current-tool observation qualifies complete native graph
collection on its exact subjects. The [native Python CI caller](#native-python-ci-caller)
implements the planning command and complete caller mapping; actual ordinary
hosted qualification is still required before accepted cutover.

`PythonMetadataReader` consumes the pinned preview schema, structured sources,
opaque resolution IDs and ordinary/group/extra edges. It rejects a different
workspace, environment/script output, conflicts and unresolved graph identities.
`PythonDependencyProjection` joins lowered local build paths to those native
members and consumes native marker/group answers for a finite operation union.
Build-plugin ordinary/extra legs exclude development groups. Cycles terminate
without hiding reachable producers. `PythonPlanningActivities` collects unchanged
marker expressions with their exact extra scopes and binds the graph to supplied
native group operations and interpreter constraints. `PythonNativeActivities`
associates answers by request ID, preserving false activity and empty group sets.
Group names describe selected nonempty native declarations awaiting strict graph
association; UV flattens includes before the helper tests requirement-vector
emptiness. A nonempty declaration entirely excluded by native Python applicability
can still have no metadata node. That existing transfer shape remains explicitly
unsupported; missing nodes are not silently dropped. The
[qualification record](../../../public/lib/three-workflow-delivery-v3/docs/research/python-native-graph-qualification.md#final-diagnostic-and-empty-group-correction)
retains the source finding and its next integration review trigger.
The projection unions actual operation answers without reconstructing defaults.
Zero-package operations retain their directory scope; a single selected package
retains its native member group ownership and the original base context. Multiple
selected packages remain unsupported until their native ownership is adapted.
`PythonPassiveReader` retains PyPA build strings and the maintained NBGV plugin's
working-directory and generated-file coordinates, including an absent generated
file. It does not interpret requirements or plugin configuration.
These controlled components do not invoke the complete collector, assign committed
ownership, expand checks or replace callers.

### Controlled Native Python Graph Collection

[`PythonNativeGraphReader`](Repository/PythonNativeGraphReader.cs) composes metadata,
passive extraction, ordered supplementation and activity projection for one caller-prepared
endpoint. Metadata supplies the complete member roster; root and tableless contexts remain
visible. A synchronous caller-owned factory receives that same metadata answer once and
constructs actual operation dimensions from native member/root contexts. UV retains group
and default semantics; no second metadata query or maintained member roster is needed.
Passive NBGV working-directory/generated-file coordinates remain available to the later
committed-input binding. The collector returns native dependency facts, operation
identities, interpreter and configuration inputs; it does not create runnable checks.

The bootstrap supplement lowers build strings before the activity request can include their
unchanged marker expressions. A second ordered supplement answers the complete native
marker/group/constraint request in distinct fresh scratch. Native tools own semantics;
Workflow joins their resolved coordinates. No additional parser, project declaration,
cache or persistent graph record is introduced. Failure or cancellation prevents a partial
return. The caller owns checkout/tool preparation, credential-free process environment,
configuration/cache/temp roots and cleanup. Controlled command-substitute tests qualify
the controlled composition only. The separately accepted
[current-tool observation](../../../public/lib/three-workflow-delivery-v3/docs/research/python-native-graph-qualification.md#completed-ordinary-and-no-dev-observation)
qualifies actual native collection on its exact subjects. The separate planning-input
observation qualifies committed version/quality input binding on those subjects.
Adopted package expansion and preparation/execution are implemented through the
[package execution component](#python-package-execution-component); the
[native Python CI caller](#native-python-ci-caller) composes ordinary CI.
Release remains separate. Completed graph, enrichment and primitive campaigns
supply no execution allowance.

### Controlled Python Input Consumer Mapping

[`PythonInputConsumers`](Repository/PythonInputConsumers.cs) maps supplied native
configuration, passive plugin, managed NBGV and effective quality answers to native
producer coordinates. Root configuration reaches every producer; a shared NBGV
working directory can serve several producers without another member declaration
or native query. Committed source inputs must be regular Git files; absent native
configuration candidates, generated-file coordinates and quality declaration
candidates remain available for paired added/deleted input reasons. Quality lookup
uses the existing declaration search boundary without interpreting presets.

Controlled mapping and paired projection tests cover input-only producers,
mixed-endpoint dependency closure and original committed reasons. The mapper does
not assign checks, qualify actual native queries, prepare environments or establish
complete Python caller input coverage. Caller-specific lock/toolchain/preparation
and root/standalone pytest associations remain explicit caller inputs. Necessary
outside-endpoint configuration or plugin coordinates are unsupported.

### Controlled Python Repository Input Associations

[`PythonRepositoryInputs`](Repository/PythonRepositoryInputs.cs) associates supplied
native pytest targets and adopted package check owners with repository engineering
inputs. It preserves shared CI/Python preparation, passive/plugin, retained V3, Azure
interoperability, NBGV tooling and exact engineering/legacy contract consumers.
It reuses native target binding, preserves absent shared configuration candidates
and emits deterministic owner relations without creating producers or runnable
targets. Unknown source scope remains unresolved.

`Pair` evaluates the same finite input relations against both actual endpoint
owner sets and emits committed initiating path/revision reasons. Supply its complete
result to `PythonFactsAssembler.Assemble` before classifying unresolved scope or
unaffected paths. A deleted input can reach a candidate-only standalone check;
owners remain in their actual endpoint facts and the shared core retires absent
candidate work. An omitted paired result is not complete repository association.

Controlled mapping and paired selection tests retain endpoint-specific input
reasons, surviving check owners and complete candidate check variants. This does
not query native tools, implement dependency semantics, expand presets or replace
the Azure independent job. The current-tool graph observation is separately
accepted, as is the separate native pytest/committed-input composition. Complete
caller preparation, distribution and execution obligations, transfer and atomic
caller retirement remain pending.

### Native Pytest Configuration Component

[`pytest_configuration.py`](Native/Python/pytest_configuration.py) uses public
`pytest.main`, an early `pytest_cmdline_main` hook and native configuration fields.
It clears inherited pytest addopts/plugins and disables autoload, conftest and
cache-provider behavior. Configuration processing can still load explicit plugins
before the hook; these controls do not qualify arbitrary configuration effects.
`PythonPytestConfigurationReader` consumes the response through `NativeCommand`,
requiring its endpoint root, explicit configuration input, nonempty unique normalized
target declarations and the supported import-mode option. Necessary additional
Python search paths or required plugins remain unsupported. It does not parse
configuration files or copy pytest's private target-selection algorithm.
Controlled producer and reader tests substitute the native query/process boundary.
[`PythonPytestTargets`](Repository/PythonPytestTargets.cs) binds those supplied native
targets to complete committed file inputs and the nearest native member. Standalone
targets keep their own coordinates without becoming root workspace members. A
target spanning descendant member roots is unsupported rather than assigned a
guessed owner. Controlled binding tests establish these supplied-fact behaviors.
These controlled components do not qualify actual behavior. The separate
planning-input observation qualifies the exact native query and target/input
composition; test collection/execution, complete runnable caller facts and cutover
remain subject to the complete Python caller contracts.

### PNPM Graph Component

[`PnpmGraphReader`](Repository/PnpmGraphReader.cs) reads wanted-lock workspace
membership, native dependency closures and every project's publish-directory
metadata. Full wanted-lock trees use exact singleton project queries so PNPM's
cross-project output deduplication cannot hide another consumer's nested inputs.
It joins resolved source/output directory identities, preserving
reachability rather than promising direct manifest edges. It does not parse
manifests, lockfiles, dependency declarations or installation-path encodings.
Native workspace package names associate projects across revisions and directory
moves; nameless members retain their native directory identity. Names do not
establish dependency source ownership. Unmatched local directories and
tarballs remain inputs with explicit consumers, including when names match a
workspace project.

Only saved production, development and optional dependency groups contribute
facts. The native `unsavedDependencies` group reflects extraneous installed state
and is ignored. PNPM 12.8.1 silently cuts off trees at dependency level 256;
the JSON depth bound accepts its complete level-255 shape and rejects the
indistinguishable level-256 boundary rather than returning partial facts.

The supported layout is a shared lockfile at the requested workspace root, with
projects and local inputs inside that root. Unrepresentable directory selectors,
ambiguous ownership, nonsingleton full responses and failed native queries stop the
read. Each process has a 30-second deadline and a 33,554,432-character limit per
output stream; cancellation or failure requests native process-tree termination
and waits up to five seconds for the owned root to exit. This is not an exit
barrier for every descendant. Cleanup failure remains a failure. Raw native
diagnostics do not enter application errors.

This component consumes the repository's accepted synchronized-lock guarantee;
it does not add lock admission. The
[native research](../../../public/lib/three-workflow-delivery-v3/docs/research/pnpm-native-planning.md#directory-coordinate-adaptation-candidate)
retains the CLI evidence limits. The
[actual application observation](../../../public/lib/three-workflow-delivery-v3/docs/research/pnpm-native-planning.md#actual-reader-and-script-metadata-observation)
qualifies both retained fixture revisions through this reader on Linux with
PNPM 12.8.2. The probe separately observed native present/absent script metadata
through direct process calls. Script discovery is a separate component below.
Ordinary reader tests also use controlled queries and retained-output replay.
The assembler below supplies concrete shared inputs, quality/version facts and
base/candidate association. The group caller consumes this composition. The graph retains
root `.`; the assembler treats it as a tooling/input role rather than a CI project.

### Native Script and Version Inputs

[`PnpmScriptReader`](Repository/PnpmScriptReader.cs) queries `pkg get scripts --json`
for an already discovered source directory, including root `.`. It returns script
names and opaque string values. Absent output or an empty map means no scripts;
malformed, nonstring or duplicate values fail the read. Registered quality checks
will select names and execute them through `pnpm run`. This reader never parses
or executes the command text, and adds no script declaration.

[`NbgvInputReader`](Repository/NbgvInputReader.cs) consumes an exact `GitRevision`
and project directory. The existing global NBGV package pin supplies the official
managed assembly and its bundled portable Newtonsoft.Json dependency through
the same imported task path. Explicit copied references preserve that dependency
when a Windows-built control distribution runs the Python NBGV reader on Linux;
this does not qualify SDK-dependent MSBuild paths across operating systems.
A read-only native context selects committed effective options, including when
HEAD and working-copy options differ. Native filters and repository case behavior
select inventory paths; absent or exclusion-only filters use NBGV's implicit
inclusion. Native descendant matching retains relevant gitlink coordinates
without traversing submodule contents. Later fact assembly must still establish
supported ownership and input coverage for those native entry kinds.

The result keeps the source commit, consumer directory, existing selected paths
and ancestor `version.txt`/`version.json` search candidates, including absent
candidates. Existing configuration paths remain inputs even when value overrides
or path exclusions hide them. These candidates describe the pinned native search
boundary, not a reconstructed inheritance tree or exact read trace. The caller
reads both revisions and joins added/deleted configurations and removed-project
consumers. Missing required projects, options or revisions fail; no default or
full-run fallback is supplied.

Input mapping does not calculate version height or package versions. The separate
`NpmVersion` operation uses the official `VersionOracle.NpmPackageVersion` on the
selected clean HEAD, with its actual tags/history and native cloud/ref context.
The package plan retains that expected projection; later native build and packed
metadata must agree with it.
The pinned native legacy `version.txt` reader can retain loose-object handles until
managed finalization. Keep the source object store alive through the reading process;
context disposal does not promise immediate repository deletion. Test-fixture
finalization is not a production cleanup protocol.
Full projection requires the clean exact-target/ref/tag/history context in
the [native NBGV evidence](../../../public/lib/three-workflow-delivery-v3/docs/research/nbgv-native-inputs.md).
The linked native observation qualifies the adopted fixture composition only.
Other quality presets and workflow cutover remain pending.

### Git Revision and Path Component

[`GitReader`](Repository/GitReader.cs) resolves caller-selected references to native
full commit IDs, reads recursive committed entries with their modes/object types,
and compares two endpoints to return changed paths. Both deleted and added rename
paths remain visible. The event caller chooses the comparison; this reader does
not substitute a merge base or inspect dirty/untracked worktree files.

All queries disable lazy fetching and replacement refs. Inventory and comparison
return root-relative paths even when invoked in a subdirectory or under conflicting
diff settings. Symlink and gitlink metadata remain distinct; submodule contents
are not traversed. Missing required revisions/tree objects fail the operation.
There is no object-completeness or history audit.

The shared process helper decodes stdout as strict UTF-8 with BOM detection
disabled, preserving an initial U+FEFF filename and rejecting invalid bytes.
NUL framing preserves whitespace, tabs, newlines, case and Unicode spelling.
Later CI fact assembly still owns path representability and ownership rules.
Queries retain the shared process deadline, output and cleanup bounds above.
The [native contract evidence](../../../public/lib/three-workflow-delivery-v3/docs/research/git-native-facts.md)
and real isolated Git/process tests cover this component. It is not complete
repository facts, event resolution, revision checkout management or caller cutover.

### MSBuild Resource Input Component

[`MsBuildResourceReader`](Repository/MsBuildResourceReader.cs) reads evaluated
`EmbeddedResource` items for a caller-specified committed project and concrete
configuration/TFM/RID in an exact `GitMaterialization`. It invokes `dotnet msbuild`
with supported evaluation-only JSON switches and disables automatic response
files. The caller supplies the selected SDK and any locked preparation required
by project evaluation; this reader does not restore or build.

Native project identity and effective dimensions must match the request. Native
`FullPath` values become in-root committed regular-file inputs with their original
Git spelling. Missing or unsupported files and malformed/conflicting native
results fail. Unspecified RID and a valid empty resource array are allowed;
duplicate appearances of a resource retain one input relation. Dimension values
must be single literal values without property-list separators or escape syntax.
Read each revision separately so a removed or moved resource retains its prior
consumer. Evaluation belongs in an unprivileged context and can run project
property functions even without targets.

The [native interface evidence](../../../public/lib/three-workflow-delivery-v3/docs/research/msbuild-native-items.md)
and controlled-result/actual CLI tests cover this narrow projection. It does not
discover project membership, imports, references, other item/target inputs or
required dimensions. It does not qualify complete .NET facts or CI group cutover.

### Native .NET Graph Task

[`DotNetGraphTask`](Repository/DotNetGraphTask.cs) is an `ITask` entry supplied by
the existing application. A completely built application is loaded with
`UsingTask` inside ordinary endpoint-selected `dotnet msbuild`. The typed request
supplies the exact revision, absolute endpoint/traversal paths, Debug CI locked
operation globals, Git-derived committed paths and a new private response path.
Caller TFM/RID selector names are rejected case-insensitively before discovery;
native-propagated TFM/RID globals remain in each node's complete identity.
MSBuild constructs the `ProjectGraph`; wrapper properties do not become graph
globals. Project paths plus complete global maps identify native nodes. The
response retains business/traversal outer/inner nodes, configuration/TFM/RID and
native reference edges, including analyzer references. Endpoint-dependent
business semantic globals fail.

[`DotNetNbgvCaching`](Repository/DotNetNbgvCaching.cs) adapts only the pinned
NBGV caching helper. An evaluated `NBGV_InnerProject` reference and the consumer's
own restored NBGV 3.10.94 provider must identify the exact package helper; every
incoming relation must be supported and the helper cannot have outgoing project
references. MSBuild keeps the complete local graph and default caching mode.
The business response omits helper identities and tool-only edges while retaining
its main file, actual imports and supported evaluated inputs for each native
consumer. Repository producer validation also applies to helper imports.
Internal `Release` configuration and local helper globals never become checks;
receiving MSBuild/NBGV constructs its own helper context. Arbitrary marked or
external projects gain no exemption.

[`DotNetOwnership`](Repository/DotNetOwnership.cs) projects ordinary committed
paths into the nearest managed project directory; projects sharing that directory
retain their own ownership. The selected SDK's `MSBuildGlob` matches effective
`DefaultItemExcludes` and `DefaultExcludesInProjectFolder`. Escaped list boundaries
are preserved before official unescaping. Illegal file specifications and encoded
literal wildcards remain unsupported instead of broadening ownership. No SDK
defaults are reconstructed. Each variant retains its own owned paths for later
union by the fact assembler.

The response separately retains main projects, native imports and supported
evaluated source/resource/additional/analyzer/XAML items with their full paths,
roles, evaluation stage and consuming native node. Explicit input consumers remain
independent of ordinary ownership, including an embedded resource excluded by a
default file rule. Environmental and generated paths remain physical native facts
with invocation-local provider associations; they are not committed source inputs.
Four nonempty evaluated scalar properties retain explicit file consumers:
`ApplicationIcon`, `ApplicationManifest`, `Win32Manifest` and `Win32Resource`.
Native Build still owns defaults, target-time assignments and generated outputs.

[`DotNetConfigurationInputs`](Repository/DotNetConfigurationInputs.cs) consumes
prepared managed-project assets through the selected SDK's official NuGet model.
Supported format, populated locked restore metadata, exact physical project and
the committed endpoint `nuget.config` contributor are required. Actual NuGet
configuration contributors and the official effective lock path retain each
native consumer; assets retain a generated restore role. An outer node without
an evaluated assets path consumes the same project's referenced native inner
nodes. The root Traversal SDK project consumes its own prepared NuGet model,
generated imports and configuration contributors. Restored global packages must
agree with their evaluated exact pins; inactive declarations supply no provider.
NuGet owns dependency applicability. Repository configuration does
not create a committed traversal lock; `TraversalPackage` therefore identifies
its actual restored package closure separately from managed `LockedPackage`
providers. No descendant inventory or invented lock supplies that context.
Other traversal shapes do not acquire a managed restore requirement. Projection
performs no restore or repair.

[`DotNetNativeInputs`](Repository/DotNetNativeInputs.cs) uses native toolset paths,
successful public SDK results captured through the graph's project-instance factory,
and validated locked-library directories to classify supported observed inputs.
Official `NuGet.Commands.BuildAssetsUtils` supplies the two generated evaluation
import paths. Microsoft's Artifacts SDK has four finite sibling build imports;
its arbitrary custom hooks gain no provider authority. Package-cache or `obj`
residence alone does not classify an input. Committed files bind first.

The workload autoimport locator has one finite native association. For successful
`Microsoft.NET.SDK.WorkloadAutoImportPropsLocator` results, an actual import of
the returned `Sdk/AutoImport.props` associates its immediate parent pack directory
with that SDK provider. Primary and additional returned paths are considered
separately. The [SDK 10.0.401 workload resolver][workload-resolver] returns the
fixed `packRoot/Sdk` child; the [maintained autoimport template][workload-autoimport]
imports sibling `targets` inputs. This is source evidence for the association,
not qualification of the hosted pack's exact bytes. Keep the native locator name
and native SDK version field, which may be empty; they do not identify an exact
pack version. Generic SDKs, the workload manifest locator, unmatched imports and
hooks outside that pack receive no expanded provider boundary. Observed eligible
inputs still require existing files and retain committed-file precedence.

The separate control-build binlog boundary retains each successful import's
immediate parent, unexpanded import text and native evaluation identity. An
existing absolute `Sdk/AutoImport.props` imported by the selected SDK's
`Microsoft.NET.Sdk.ImportWorkloads.props` with literal `AutoImport.props` seeds
a finite pack association. Only observed imports connected to that seed within
the same evaluation and pack become exact dependency files. Disconnected,
missing or escaping files gain no membership; ordinary task/role restrictions
and committed-file precedence remain. This consumes the [SDK import contract][workload-import]
and [native import event fields][native-import-event], without reconstructing
SDK results, enumerating pack contents or claiming an exact workload version.

Control-build restore consumes the official model's exact configuration
contributors under the existing locked operation binding. Require absolute
existing files before normalization. Committed contributors remain repository
sources; outside-checkout contributors are invocation-local restore-environment
facts. The [native metadata][restore-configurations] names the configurations
used by NuGet; Workflow neither parses their contents nor reconstructs hierarchy.
This role grants no generic dependency membership, directory trust or exact pin,
and does not admit the same file as an unrelated import, task or compiler input.

[restore-configurations]: https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/nuget-client/src/NuGet.Core/NuGet.ProjectModel/ProjectRestoreMetadata.cs#L68
[workload-resolver]: https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/sdk/src/Resolvers/Microsoft.NET.Sdk.WorkloadMSBuildSdkResolver/CachingWorkloadResolver.cs#L115
[workload-import]: https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/sdk/src/Tasks/Microsoft.NET.Build.Tasks/targets/Microsoft.NET.Sdk.ImportWorkloads.props#L14
[native-import-event]: https://github.com/dotnet/dotnet/blob/e34a38d2ae1fc26406a317517196e55c68ff83ab/src/msbuild/src/Build/Evaluation/Evaluator.cs#L2247
[workload-autoimport]: https://github.com/dotnet/macios/blob/354c2a645855dfdf774cc5e098cd214764f475b5/dotnet/targets/AutoImport.template.props#L1

Roslyn's evaluated `PotentialEditorConfigFiles` and `GlobalAnalyzerConfigFiles`
retain absent linked-source candidates and respect native discovery switches.
Evaluated CPM paths and finite SDK/Directory.Build/CPM/NuGet/lock search candidates
remain separate input roles. Candidates cover possible additions; the application
does not resolve configuration hierarchy or reconstruct Roslyn's search.

Each node's `TestCapability` reads evaluated `IsTestingPlatformApplication`
first: true selects MTP. Only otherwise is `IsTestProject` needed to select
VSTest; false or absent selects no test capability. Malformed required nonempty
values fail with revision, endpoint, complete node identity and property role.
Capability is metadata; runnable output still needs Build/inner GetTargetPath.

[`DotNetRepositoryTargets`](Repository/DotNetRepositoryTargets.cs) reads the native
effective target dictionary and definition origins. Concrete repository
origin/name pairs identify the supported operation adapters. An unsupported
committed repository producer fails with native coordinates instead of assuming
its I/O. Uncommitted SDK/package/generated definitions remain native; physical residence
under the endpoint does not establish committed repository ownership. Native
shadowing selects the winning definition. Existing project/import consumer rows
retain definition impact, without an extra target record, body parser, condition
evaluator, SDK task allowlist or hash admission policy. Native conditions and
effects are unchanged.

[`DotNetRepositoryReader`](Repository/DotNetRepositoryReader.cs) invokes the task
from a caller-prepared exact checkout with the complete application distribution
and caller-owned scratch paths. It binds physical coordinates to original Git
spelling, validates full node/edge/input identities and rejects necessary unsupported
source. Finite absent in-endpoint configuration candidates retain possible-addition
consumers; existing untracked source is not absence. The native effective
`GitVersionBaseDirectory` binds NBGV's committed version-input subject separately
from the product directory; business version inputs union across native variants.
Traversal's unused self-version metadata does not create product consumers.
Its actual project, configuration, provider and helper inputs retain their native
consumers. Business projects may still use a root version subject.
External or unsupported
repository-relative version contexts fail. The physical base remains invocation
local and is not a replayed check global. Nearest committed quality YAML still
uses the product directory and retains adoption or unadopted native intent.

[`DotNetFactsAssembler`](Repository/DotNetFactsAssembler.cs) aggregates native
variants into distinct committed project IDs only for `ImpactPlanner.SelectProjects`.
Traversal discovery retains real consumers without root ownership. Exact ownership
and explicit inputs remain separate, and both-endpoint relation union preserves
removed consumers. Caller-supplied positive responsibility answers cover unrelated
paths; unknown paths fail even in full mode. Every candidate native node remains
available for later completion. Selection-stage checks stay empty.

Isolated native-process and exact-Git selection tests exercise this bounded projection.
The group interface below composes this reader with both-endpoint native Node and
control consumption. The root caller qualifies complete repository preparation,
selected execution and original-plan transfer for its Windows Debug operation.
Additional producer adapters require their own concrete operation scope.

### Native .NET Selected Checks and Execution

[`DotNetFactsAssembler.Complete`](Repository/DotNetFactsAssembler.cs) expands only
selected surviving candidate nonouter identities through the existing planner.
[`DotNetChecks`](Repository/DotNetChecks.cs) retains the complete original escaped
global map, native dimensions and `NativeRetained` origin. Each identity requires
Build; evaluated MTP or VSTest capability also requires Test after that Build.
None is Build-only. Selected adopted package quality fails completion until its
separate concrete NuGet package adapter is available; it cannot become an
unadopted native recipe.

The internal asynchronous `CompleteAsync` operation resolves the registered
`dotnet/hcoona-release-smoke-github-packages-v1` contract and its existing
release-unit declaration before native version hooks. Release-unit membership
and declaration consumers participate at both endpoints before impact selection.
The native NBGV target supplies expected package and assembly projections;
Build/Pack may calculate them again without frozen version injection.
[`DotNetPackageExecution`](CI/DotNetPackageExecution.cs) consumes only a complete
adopted package partition, separately from the retained native executor. Its
fixed required phases are native Pack/output items, NuGet/.NET content inspection,
and a fresh local native restore/build/marker consumer. Results retain each
prerequisite and the complete original archive's unit/build/output, path, size
and SHA-256. NuGet owns identities, version ranges, assets and installed paths.
The concrete archive inspector, local consumer and MSBuild result projection
live in `Ecosystems`; they consume no CI plan or check specification. CI retains
its orchestration and quality selection. The control and tests use the coherent
official NuGet 7.9.0 package group, with package-resolved Newtonsoft.Json and
ProtectedData; Microsoft.Build and official NBGV remain SDK/native references.
This shared foundation does not implement or qualify NuGet Release publication.
The per-command environment option isolates consumer caches/configuration/home
without changing the application's ambient environment.

The group executor below exposes this package partition without introducing a
separate package command or workflow. The product's native project supplies a
fixed deterministic archive timestamp only
when neither `DeterministicTimestamp` nor `SOURCE_DATE_EPOCH` is supplied.
Explicit native inputs retain precedence. Native build infrastructure owns
that metadata; the application does not normalize or repack archives. Local
qualification materializes current product project bytes in a clean isolated
Git fixture, retaining the source/fixture identities and original archives.

[`DotNetExecution`](CI/DotNetExecution.cs) consumes the original complete plan,
an exact candidate checkout and fresh external scratch. The caller owns locked
preparation, SDK selection and native output lifetime. One phase runs native
Build/GetTargetPath. A separate Test phase repeats native Build, then runs
GetTargetPath and `InvokeTestingPlatform` or `VSTest` in that same process.
MSBuild owns references, analyzers and target state; the test platform owns its
host, arguments, environment, settings and verdict. The selected SDK's
`CommandLineBuilder` quotes each original property value without re-escaping it.
Unrepresentable globals fail before product commands.

Each phase reads its required native target-result JSON and regular-file
GetTargetPath coordinate from fresh scratch. Diagnostic stdout is not a result.
A failed Test, repeated Build or required receipt cannot erase an earlier passed
Build. Native cancellation, deadlines, skipped prerequisites and missing results
retain the existing check/collector semantics. Individual intentionally skipped
tests and no-tests policies remain native. A genuine empty plan performs no
product Build/Test commands. No restore, application launcher, task scheduler or
general cache is added.

Isolated native integration tests exercise original globals, references/analyzers,
inner multi-TFM MTP with apphost and hostless execution, VSTest settings, redirected
outputs, intentional skips, no-tests policies and active cancellation/deadlines.
These tests cover their concrete fixture composition. The CLI below composes retained
native checks and the adopted NuGet recipe. The root caller below supplies the
separate whole-group and ordinary Windows artifact/result qualification.

### NuGet Destination Observation

[`GithubNuGetObservation`](Destinations/GithubNuGetObservation.cs) consumes a
qualified single-package Buddy request. Official NuGet interprets the service
index, normalized versions and archive identity. The application restricts the
selected owner endpoints, requires public package/repository association,
collects bounded supporting GitHub metadata and native version inventory, and
compares the original archive size and SHA-256. Unavailable, incomplete or
conflicting facts cannot authorize creation.

An observation permits at most 80 HTTP reads in 90 seconds. Each body-inclusive
read has 30 seconds; metadata is limited to 2 MiB and archives to 25 MiB. The
owned client disables automatic redirects, cookies, proxy and default
credentials. NuGet reads use origin-bound Basic authentication; GitHub metadata
uses Bearer authentication. Only the original archive can follow one supported
HTTPS storage redirect, without credentials. Signed locations remain in memory;
diagnostics retain only the storage origin and location digest. Cancellation and
missing responses retain unknown state. Controlled handler scenarios qualify
this component's behavior, not live feed responses or publication. The
[NuGet Buddy caller](#nuget-buddy-release) composes native observation, publication
and required remote consumption without promoting controlled results to live-feed
qualification.

### Native .NET Group Interface

[`DotNetGroupReader`](Repository/DotNetGroupReader.cs) reads both exact .NET endpoints,
native PNPM ownership and candidate/basis control consumption before the existing
`ScopeInputs.Bind`. Complete native input consumers supply .NET and Node responsibility
rows; retained project/test/record owners remain separate. PNPM reads the synchronized
wanted lock through its CLI without workspace installation or product lifecycles.
Unknown required ownership and native relation errors fail even in full mode.
Both-endpoint changes select surviving candidate projects and all required variants.

`ci plan-dotnet-group` emits the original `CiPlan`, joined endpoint responsibility and
candidate control-build context. Its request supplies both materializations, native
graph response destinations, control build logs and fresh completion scratch; it
adds no project, dependency or version declaration. Confirmed basis control-owner
absence allows a null basis log, while both native group endpoints remain required.
Explicit full may reuse one comparison endpoint without inventing basis absence.

[`DotNetGroupExecution`](CI/DotNetGroupExecution.cs) resolves both selected contracts
before either executor can run product commands. It partitions the unchanged original
plan into `NativeRetained` and the registered NuGet preset, reuses their existing
executors, and combines their results against the complete parent. A failed or missing
partition cannot become successful nonselection. `ci run-dotnet` exposes that operation;
`ci result` independently collects its result array against the same original plan.

Package plans retain the committed project entry point and original native semantic
expectations. Each checkout validates its own native `MSBuildProjectFullPath`; the
producer's absolute path does not cross the plan boundary. Receiver Pack still checks
its local project identity and recomputed package/version values against the original
expectations before qualifying archives and clean consumers.

[`run_dotnet_ci_group.py`](../../../../eng/scripts/run_dotnet_ci_group.py) builds the
complete candidate control output once, prepares each endpoint with its selected SDK
and locked unfiltered traversal restore, and finalizes scope only after native planning.
The existing Windows `scope`/`dotnet-tests` caller transfers the original plan
and complete distribution through a native artifact ID. The receiver validates the
actual producer/lifetime, prepares the candidate and executes without rebuilding control
or replanning. Empty work still crosses execution and collection; missing planning,
transfer or required results fail. No second test launcher, scheduler or cache is added.
The whole sequential execution has a finite one-hour caller budget; each native
phase retains its fifteen-minute deadline. Preparation and collection keep their
existing command deadlines. Reaching either execution limit remains failure.
The required `Build & Test (.NET 10)` owner remains the existing Windows job. Its
ordinary changed-code qualification is recorded in
[PR #1054](https://github.com/hcoona/three/pull/1054); this scope does not migrate Release.

[`ControlTestInputs`](Repository/ControlTestInputs.cs) exposes one concrete root-CI
consumer through `ci control-inputs`: the control test project's embedded resources,
including the npm fixture version file. Git confirms the exact comparison,
candidate owner and optional base absence. Each present endpoint is a caller-prepared
exact materialization. One native query per distinct endpoint uses Debug without
TFM/RID overrides; the effective TFM must be nonempty and plural TFM/RID declarations
must be empty. Unsupported required shapes fail.

[`prepare_ci_control_inputs.py`](../../../../eng/scripts/prepare_ci_control_inputs.py)
resolves the native event once, materializes both endpoints outside the source root
and restores their test-project reference closures in locked Debug context using
each endpoint's pinned SDK. The same job builds the complete candidate application
once. Its finite response carries comparison, project, revision, presence, dimension
and resource inputs to the existing Python selector before any final scope output.
Both resource sets contribute the .NET owner and path/project/revision reasons;
missing or conflicting facts fail rather than suppressing checks. The final
`ci-scope` artifact also carries `endpoint_owners`: each exact basis/candidate
revision has one row per changed path, with committed presence/mode and separate
owner/target/rule/source reasons. It reads committed endpoint path/mode and
manifest-path inventory for other-job responsibility, reuses retained selectors
and native resource facts, and matches current record bindings with the record
checker's shared pure path matcher. The [native Python CI caller](#native-python-ci-caller)
owns both endpoints' Python workspace, dependency and pytest facts, selection and
candidate execution; scope transfers the exact comparison and full-mode context
without parsing or selecting Python work. Frozen root-dev preparation supplies the existing locked PyYAML
parser; it does not run the contextual record checker in the scope job.

Responsibility explains retained project/test/record routing; it does not prove
product nonconsumption. Absent coordinates and unknown ownership have no positive
reasons. The retained Node manifest heuristic supplies no authoritative Node
owner, and unconditional source conformance supplies no per-path owner. Native
PNPM ownership and complete product/control consumption come from the native
group readers. Basis-only reasons do not execute removed targets. The Node group
consumes the final `ci-scope` artifact. The .NET caller
transfers its original plan separately with the complete control distribution,
under the Windows qualification described above.

### Input Consumer Mapping

[`NodeInputConsumers`](Repository/NodeInputConsumers.cs) adapts a complete supplied
native project roster independently of its optional NBGV answers. It joins native
version/configuration inputs, effective quality selections and resolved local
inputs into the existing shared-input values. Directory identities name consumers;
the NBGV answers must match the committed revision. Absent native configuration
candidates and quality-search candidates remain inputs. Quality search stops at
its actual winning ancestor, without reevaluating YAML or native inheritance.

Local directories expand against the committed inventory with path-segment
boundaries; exact file inputs retain their supplied consumers. Missing required
sources, unknown consumers, unrepresentable coordinates and unsupported local
symlink/gitlink coverage fail. Native version paths keep their original committed
spelling. Results deduplicate consumers and sort deterministically. Each revision
is mapped independently for the planner's base/candidate union.

This is supplied-fact adaptation. The concrete reader/assembler below adds its
operation inputs. The mapper does not infer unaffected paths or certify a
scope's completeness.

### Quality Selection Component

[`QualitySelectionReader`](Repository/QualitySelectionReader.cs) reads the existing
`workflow-delivery.quality.yml` from a caller-supplied materialized revision. The
nearest ancestor containing the requested ecosystem wins; unrelated ecosystem
entries allow the search to continue. Parent presets are never merged. The result
is the preset identifier and repository-relative declaration path, or explicit
absence. Root `.` is a valid reader input without changing the CI core's project
directory contract.

YamlDotNet handles YAML syntax and aliases. The reader validates the application's
single-document `schema`/`ecosystems`/`preset` contract and fails on malformed
declarations or failed reads. It does not resolve registered preset semantics or
invent checks. The caller binds the materialized revision and expands the selected
preset into concrete work; this component alone does not complete CI facts or
switch any workflow.

[`NodeQualityChecks`](Repository/NodeQualityChecks.cs) expands the existing
`node/hcoona-release-smoke-npm-v1` selection into its required project build and
test checks. Discovered source directories identify targets; script bodies remain
opaque, with PNPM owning execution. Unknown or absent selections and missing
required script names fail. Root metadata cannot become a runnable project.
The concrete checks use the default variant and Ubuntu runner with no prerequisite;
the registered test contract does not consume a build output. The package component
below resolves the current declared build/output contract. Other presets and
variants remain unavailable; this component creates no new project adoption.

### Node Revision-to-Plan Transaction

[`NodePlanning`](Repository/NodePlanning.cs) accepts two distinct caller-prepared
checkout roots and event-selected references. Git resolves both endpoints and
their changed paths without substituting a merge base. Each checkout must have
the requested HEAD, available required objects and clean tracked content.
The caller owns checkout creation, tool/dependency preparation, controlled native
configuration, publication-free execution and lifetime; the application does not
clone, fetch, repair or maintain a checkout/proof service. Untracked/ignored
application declarations are absent, not inputs from another revision.

[`NodeRepositoryReader`](Repository/NodeRepositoryReader.cs) reads the complete
native PNPM roster and joins existing quality/release declarations from committed
regular files. Its script input contracts cover the six current Node source roots;
an additional project needs a concrete input contract. These contracts do not
declare native membership or interpret script bodies. Unversioned POC membership
does not require an invented NBGV answer. The three existing versioned products
retain native committed configuration/filter inputs, including absent candidates.

The supported setup consumes root `package.json`, workspace YAML, shared lock and
the repository's mise tool files. The fixture's native pack consumes matching
regular root license files. Hexo's existing license-copy script consumes `LICENSE`,
`COPYING`, `COPYING.LESSER` and the linking exception; Steam's build consumes the
two COPYING files and regular direct members of `LICENSES/`. Hexo and the three
POCs consume root Biome configuration and its `.gitignore`/`.ignore` candidates;
Hexo's Prettier also consumes root EditorConfig. Steam has neither style role.
These are concrete
operation inputs, not a universal native configuration or arbitrary script trace.
Added/deleted inputs remain visible through both endpoint inventories. Additional
ancestor npmrc/pnpmfile configuration and uncommitted native version options are
unsupported by this input contract. Native root local relations without a justified
project projection, required symlinks/gitlinks and unknown changed paths fail.

[`NodeFactsAssembler`](Repository/NodeFactsAssembler.cs) preserves native source,
publish and local-input identities, associates unit members through entry paths,
and selects against the union of both endpoints. It does not infer unaffected
paths from inventory subtraction. Only selected surviving candidate projects
resolve execution checks. Unrelated absent quality does not block a selective
change; a genuine shared-input selection of an unadopted project fails. Full mode
also fails unavailable contracts. The scope label describes this resolved input
boundary and supplies no whole-repository or cross-ecosystem completeness proof.

[`NodePackageChecks`](Repository/NodePackageChecks.cs) preserves every selected
unit build and output association. The current `node/npm-package-v1` contract uses
the default Ubuntu variant and one `primary-package` npm tarball per build,
without an alternate publish-directory build. Each build has required pack,
contents and installed-consumer checks. Pack depends on its actual project build;
contents and consumer each depend on pack. The project source test has no build
prerequisite. Additional unsupported outputs, definitions, mixed builds or
ambiguous/multiple unit membership fail rather than retaining the first member.
Each artifact check transfers its unit/declaration/build/definition, source/entry,
expected native version and complete outputs in `package`.

Planning does not execute pack. The
[native observation](../../../public/lib/three-workflow-delivery-v3/docs/research/node-ci-native-execution.md)
qualifies the executor's adopted fixture composition on Linux; hosted event, transfer and
group integration still precede caller cutover. The fixture's
native NBGV filters include its source and shared package/workspace/lock, toolchain
and license inputs. Five other Node projects have no adopted new quality
preset, and unmigrated/cross-group callers retain their current owners.

### Node Check Execution

[`NodeExecution`](CI/NodeExecution.cs) consumes a complete runtime-expanded parent
plan, an explicit `node22` or `node24` context, and one caller-prepared checkout
plus fresh external scratch directory. It validates
the supported required quality, runner, variant, package associations and prerequisites
before running work, then binds the exact clean tracked starting checkout.
Normal native build/stamping changes after that bind are permitted. Candidate
identities must equal the full commit returned by Git; aliases fail. An empty
plan still binds its starting checkout and requires no runtime reads or product
commands. Nonempty execution reads native `node --version` and `pnpm --version`
before product work: Node 24 and PNPM must match the plan's exact configured
identities; Node 22 must match its supported matrix selector. The result retains
the actual concrete versions and native readbacks. Invalid or unsuccessful
starting runtime reads fail the invocation before product work. Checkout
and scratch paths cannot have linked ancestors, and scratch paths cannot contain
PNPM's `%s` or `%v` output markers. The caller supplies tools,
locked dependencies,
correct history/ref/cloud context and an unprivileged environment with isolated
configuration, credentials, caches and runtime preloads; request fields do not
prove those conditions. The caller owns checkout/scratch retention and cleanup.

The current `node/hcoona-release-smoke-npm-v1` recipe invokes native project
build and independent source test, then every declared package build through
`pnpm pack --out`. Pack requires its actual successful source build. Contents
and installed consumption each require successful pack and remain independent
checks. Failed prerequisites produce skipped results; cancellation, deadlines,
command failure and invalid outputs remain unsuccessful. There is no arbitrary
task graph scheduler or interpretation of script bodies. Source build/test run
in the selected runtime; packaging, contents and installed consumption run only
in Node 24. Both source runtimes remain required in the complete parent plan.
The caller supplies distinct native source/dependency materializations for each
runtime; external scratch alone does not supply that isolation.

[`NodeRuntimeReader`](Repository/NodeRuntimeInputs.cs) derives runtime dimensions
from the committed root CI matrix and native mise tool identities. It maintains
no second runtime declaration. `ci plan-node-group` joins its projection with
base/candidate selections and the executor obligations; `ci plan-node` remains
the supplied-operation selection interface.

Build and source-test invocations use PNPM's native
`--config.verify-deps-before-run=false` setting. The caller explicitly prepares
locked dependencies; execution does not automatically inspect freshness or
reinstall the workspace after ordinary version stamping. This is the maintained
[PNPM contract](https://github.com/pnpm/pnpm/blob/b952ea2c7dfa0c50dbddfeadad6f57e4a86a7fce/pnpm/crates/cli/src/cli_args/verify_deps.rs#L78),
not a script-effects or network boundary. Selected scripts can still invoke
installation themselves, and the caller controls ambient native configuration.
The native `pnpm_config_verify_deps_before_run` recursion variable can override
the CLI setting. The recipe's contents/consumer checks and explicit native
consumer install remain separate operations.

Each successful native pack retains its complete original tarball under a fresh
build directory, with unit/build/output association, byte size and SHA-256.
A nonzero pack cannot qualify a leftover file. Maintained `GZipStream` and
`TarReader` inspect the archive without extracting archive-controlled paths.
The bounds are 32 MiB compressed, 128 MiB expanded, 32 MiB per member, 1,024
members and 1 MiB each for the required regular manifest and ESM payload.
Only the packed manifest is written to a fixed metadata filename. Native
`pnpm pkg get` supplies typed metadata; Workflow compares the recipe's package
identity, planned native version, module entry and supported install shape.

This recipe allows development dependencies and native pack lifecycles. Populated
runtime/optional/peer/bundled requirements, runtime acquisition, install lifecycles
and native-build members need their own concrete contract and fail this recipe.
Other archive members have no universal allowlist. A fresh external consumer,
store, state and empty authentication file feed the complete original tarball to
native offline, scripts-disabled installation; PNPM creates its consumer manifest.
Node imports the public package name and checks `smokeMessage()`. Source tests or
source imports cannot replace that result. Offline mode and an empty auth file
alone do not establish the caller's configuration or credential isolation.

The process helper records actual termination, exit code when available, bounded
strict UTF-8 stdout/stderr, duration and failure diagnostics. Build/test/pack/install
have five-minute deadlines; metadata and Node assertions have thirty seconds.
Each stream retains at most 33,554,432 characters, with the existing five-second
owned-root cleanup bound. The result keeps every selected runtime key, command observation,
original output descriptor and execution failure. The existing collector decides
whether that runtime's required checks are satisfied. Final collection combines
both runtime result sets against the complete original parent; one runtime's
success cannot replace the other's missing or unsuccessful result. Controlled
tests establish these
application contracts. The linked bounded native observation separately
qualifies this fixture's Linux composition and effects; it does not qualify other
presets or establish caller cutover.

## Commands and Transfers

Build with the repository-pinned SDK. From the repository root:

```powershell
dotnet build src/private/app/workflow-delivery/WorkflowDelivery.csproj
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll `
  ci comparison pull_request native-event.json <tested-commit>
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci control-inputs control-request.json
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci plan request.json
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci plan-node node-request.json
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci plan-node-group group-request.json
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci plan-dotnet-group dotnet-group-request.json
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci run-dotnet dotnet-plan.json dotnet-request.json
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci run-node plan.json execution-request.json
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci result plan.json results.json
```

Commands emit one JSON value on stdout. Exit code `0` means a valid plan or
satisfied CI result; `1` means an unsatisfied result (including incompatible
result subjects) with its JSON explanation; `2` means malformed input, a planning
failure, invalid execution input or unsupported usage, with a diagnostic on stderr. Redirect stdout
when a receiving process needs a file. In-process consumers call the same typed
operations without serializing their inputs.

`ci comparison <event-name> <native-event.json> <tested-candidate>` consumes the
native Actions `GITHUB_EVENT_PATH` payload and tested SHA without a second event
declaration. It emits only `basis`, `candidate` and `full` for caller construction
of the existing Node request below. The finite
[GitHub event mapping](../../../public/lib/three-workflow-delivery-v3/docs/middle-level-design.md#github-event-comparison)
owns supported comparisons. Required malformed fields or conflicting targets
return input error; unrelated native fields are ignored. Native Git subsequently
resolves actual objects. This command does not fetch events, prepare checkouts,
inspect ancestry or switch workflow/group ownership.

`ci control-inputs <request.json>` consumes `comparison` (`basis`, `candidate`,
`full`), `repository`, nullable `basisDirectory` and `candidateDirectory`. The
comparison contains full Git object IDs, and the repository HEAD must be the
candidate. A null basis directory means confirmed owner absence, not failed
preparation. Explicit full retains equal comparison IDs and may reuse one endpoint.
The response contains `comparison`, `basis` and `candidate`; each endpoint carries
`revision`, the fixed test `project`, `present`, nullable `dimension` and `inputs`.
This internal response feeds scope selection and supplies no publication authority.

The Node request supplies materializations, not a second fact declaration:

```json
{
    "basis": { "directory": "C:/work/three-basis", "reference": "<comparison-commit>" },
    "candidate": { "directory": "C:/work/three-candidate", "reference": "<tested-commit>" },
    "full": false
}
```

Both required commits must be available in the candidate object store for the
native comparison. The checkouts retain their own correct native ref/tag/history
context. A JSON request does not supply tool/runtime or configuration isolation.

`ci plan-node-group` adds `scopePath`, nullable `basisBuildLog` and required
`candidateBuildLog` to those endpoint fields. It requires separate exact checkouts
even for explicit full. A missing basis log is valid only for confirmed absence of
the committed control project; build or read failure is never owner absence.
The response carries `adopted`, `retained`, `adoptedDirectory`, native `runtime`
variants and both native control-input projections. It identifies the fixture by
its workspace package name across directory moves.

`eng/scripts/run_node_ci_group.py group` receives the exact scope artifact and
native producer/lifetime metadata, prepares both endpoints and fresh control
builds, then executes and collects the complete runtime plan before emitting
retained-member outputs. The `retained` entry delegates those members' build/test
and selected Hexo probes to PNPM. These are concrete root-CI callers, not another
task scheduler. Failed or missing group collection cannot become nonselection.

The execution request adds no project, script, dependency or version declaration:

```json
{
    "checkout": "C:/work/three-candidate",
    "scratch": "C:/work/node-check-scratch",
    "runtime": "node24"
}
```

`ci run-node` emits its candidate-bound execution result, including unsuccessful
checks and runtime identity, with exit code `0` or `1` for its selected runtime
according to the existing collector. Contract or
starting-materialization errors return `2` before product execution. Its `results`
arrays from both runtimes are combined and passed to `ci result` with the original
parent plan; in-process callers use the same typed values.

The source-generated JSON contracts use camel-case property names, required
constructor fields and named check statuses. Unknown fields and missing or null
required values fail. These internal formats have no historical compatibility
promise. The concrete types are in [Contracts.cs](CI/Contracts.cs).

The collected outcome retains the plan's `comparison`, `candidate` and declared
`scope`, including when no checks are selected. Its success applies to that
subject and supplied coverage only.

| Input                 | Required content and producer responsibility                                                                                                                                                                  |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Planning request      | `basis`, `candidate`, `changedPaths`, `full`; the caller resolves the event's actual comparison/tested revisions and includes both rename paths and deleted paths                                             |
| Each revision's facts | `revision`, `scope`, `projects`, `sharedInputs`, `unaffectedPaths`, `errors`; native readers establish completeness for this declared scope                                                                   |
| Project               | Stable `id`, repository-relative `directory`, optional exact `ownedPaths`, build `dependencies`, distinct `qualityConsumers`, nullable `releaseUnit`/`qualityPreset`, resolved `origin` and concrete `checks` |
| Shared input          | Exact repository-relative `path` and project `consumers`, including actual version/configuration/lock inputs                                                                                                  |
| Unaffected path       | Exact path the producer established has no consumers in this scope; no wildcard or unknown-path fallback                                                                                                      |
| Check                 | `key` (`target`, `check`, `variant`), actual `dimensions`, `runner`, `required`, prerequisite keys and nullable artifact `package` subject; variant names identify resolved dimension combinations            |
| Planned check         | Concrete `work`, actual selected `qualityPresets`, selection `reasons` and nonempty `origins` (`Preset` and/or `NativeRetained`)                                                                              |
| Result array          | Each entry carries the exact `candidate`, check `key` and `status`: `Passed`, `Failed`, `Cancelled`, `TimedOut` or `Skipped`                                                                                  |

Paths use Git's case-sensitive forward-slash spelling, without a leading slash,
empty components or traversal. Null `ownedPaths` retains nearest-directory
ownership; a populated array supplies exact owned coordinates, with no directory
fallback. All exact owners and explicit shared-input consumers contribute even
when an ordinary directory/exact owner excludes the path. A nearer project still
prevents implicit ownership by a legacy parent directory. Readers populate these
facts; the core does not evaluate native excludes or reconstruct glob rules.
Root-level inputs and inputs outside project directories need explicit consumers;
they do not become implicit dependencies of every project. Readers must resolve
native imports, workspace references, extra inputs and quality declarations before
constructing these values; the core does not parse build-system expressions.

Selection follows the union of base/candidate reverse dependencies and quality
consumers. Projects in an affected Release Unit are included with all their
candidate checks/variants. Deleted projects contribute relationships and reasons
but cannot become runnable targets. Known unaffected changes may produce an empty
plan. Unknown paths and incomplete facts fail even in explicit full mode.

Identical check keys deduplicate only when runner, dimensions, package subject and prerequisites
agree. Required status combines the selected contracts. Required checks promote
their prerequisite closure to required; missing or cyclic prerequisites fail.
These prerequisites order checks, not individual native compilation nodes. Plans
retain actual quality presets, selected origins and all initiating
path/revision/project reasons. `Preset` requires a resolved preset;
`NativeRetained` requires nonempty resolved checks and no claimed preset adoption.
It never substitutes for a missing adopted contract. Node producers retain preset
selection. The internal .NET completion operation above produces native-retained
checks; the supplied-fact core alone does not establish native capability.

[`ScopeInputs`](Repository/ScopeInputs.cs) binds native comparison coordinates and
committed endpoint responsibility to actual consumers for both revisions. Node
callers and the native .NET group reuse that shared operation. The rename adds
no second scope parser or compatibility alias.

The collector requires every selected required result at the candidate revision.
Missing, failed, cancelled, timed-out, skipped and conflicting required results
prevent satisfaction. Repeated identical results are harmless; unexpected subjects
or unknown statuses fail. Advisory outcomes remain visible without failing the
required result. A native cancellation that prevents collection is not a success.
The eventual workflow must preserve that native conclusion; this command cannot
observe jobs that its caller did not report.

### NuGet Release Preparation Component

[`NuGetPreparation`](Release/NuGetPreparation.cs) prepares the existing declared
GitHub Packages product for Buddy and development dry runs without a CI plan.
It reads exact committed release and quality declarations, uses the selected
SDK and locked Release restore, then compares native GetBuildVersion, Build and
Pack version projections. Restore lets the native project declare its frameworks;
native cloud/ref inputs are bound to the selected source commit and ref rather
than inherited from the caller. NBGV retains native public-release evaluation;
its variable output stays in preparation-owned scratch.
Subsequent queries and operations use the single supported `net10.0`/no-RID
variant. Multiple variants, unknown quality recipes and unavailable required tests
cannot become a smaller successful release.

The shared `Ecosystems` operations receive the complete original Pack output,
inspect native package and assembly metadata, and restore/build/invoke a fresh
local consumer. Preparation retains every required phase's outcome, command
termination and original file size/digest. Its consumer expectations omit physical
checkout paths. Native NBGV caching and reproducibility remain authoritative;
preparation maps the complete source checkout to a stable native compiler path,
including generated sources and PDB paths. It injects neither frozen versions nor
a witness. Controlled failures
and isolated native preparation tests qualify this component only. The ordinary
caller below composes it with destination observation, publication and required
remote consumption; component qualification does not establish live feed behavior.

### Native NuGet CI Scenarios

The existing `nuget-reproducibility` Windows job selects `NuGetNativeScenarios`
in the managed test owner. Its
[native preparation scenarios](../../../../tests/private/app/workflow-delivery/Release/NuGetPreparationNativeTests.cs)
compare independently prepared originals across different checkout roots,
source mtimes and dependency caches, including native DLL/PDB bytes and debug
paths. Source and explicit timestamp controls establish sensitivity; the
timestamp control keeps entry payloads equal while archive metadata changes.
Portable PDBs are inspected separately because the selected product does not
package symbols.

Fresh exact/conflicting observations use independently rebuilt originals and
the real native consumer with controlled HTTP. The first missing-publication
result remains Unknown; exact recovery requires clean consumption without a
publisher invocation, and conflicting bytes block completion.

The [scope selector](../../../../eng/scripts/ci_scope.py) follows the current
application, test owner, product, inherited managed build inputs and native
control resources. Old Python-package/helper inputs do not select this job.
The caller restores pinned product dependencies before the tests and requires
one TRX with every selected case executed and passed. Actual results,
attachments, original packages and binlogs retain the existing
`nuget-reproducibility-windows` artifact, `artifacts/nuget-reproducibility`
directory and 30-day lifetime, including failures. Local Linux qualification
does not establish Windows execution, a deployed feed or publication; ordinary
final-source Windows CI remains required before delivery.

### NuGet Buddy Release

The ordinary NuGet entry and reusable Release workflow use four concrete jobs:
prepare, observe, publish and verify/result. The containing entry owns native
package concurrency through verification. Preparation builds and transfers the
complete same-revision control distribution before running product code. Receiving
jobs download immutable artifact IDs with native digest rejection and bind the
actual run, attempt, source commit, selected ref, unit and dry-run mode. Complete
preparation, original packages and actual business results use 45-day retention.

The private interface is:

```text
release prepare-nuget REQUEST
release observe-github-nuget PREPARATION ORIGINALS
release describe-github-nuget PREPARATION OBSERVATION
release publish-github-nuget PREPARATION ORIGINALS
release consume-github-nuget PREPARATION OBSERVATION ORIGINALS SCRATCH
release complete-github-nuget PREPARATION INITIAL FINAL CONSUMER PUBLISHER_OR_-
```

`ORIGINALS` is an absolute receiving directory containing the original relative
package path. Native size, digest and package identity checks precede network
activity. `SCRATCH` is a fresh external directory. Transferred native expectations
omit checkout-specific preparation paths. JSON inputs reject missing, null,
unknown and duplicate properties through the existing typed transfer boundary.

Buddy development refs and dry runs are supported. Dry run qualifies complete
preparation and describes supported missing writes without entering an Environment,
invoking publication or claiming live completion. Live reruns remain unavailable.
The existing `workflow-delivery-v3-buddy-approval` Environment applies only to the
actual publisher. That job runs transferred control without rebuilding or executing
product code. Publication refreshes native state; exact removes the write, absent
permits one official NuGet invocation, and conflict or unknown blocks it.

An official upload returns its actual `NuGetPushResult`, preserving handled
timeout, cancellation or failure rather than inventing a process result. Handled
upload abnormality permits bounded independent readback and required native remote
restore/build/marker consumption. The separate consumer job has read permission;
product child processes receive cleared environments without that capability.
Captured command results survive later handled consumer failure.

Completion uses initial and final observations and the required consumer result.
Initial exact legitimately requires no publisher result. Initial absent requires
an actual publisher result; missing evidence leaves mutation effects and business
completion unknown. A complete refreshed-exact publisher result explicitly records
zero invocation. Complete fresh verification may satisfy the request after a handled
upload failure while retaining that abnormality. An unexpected task crash remains a
platform failure and cannot be hidden by blanket job continuation. Controlled tests
and ordinary CI do not authorize dispatch, live feeds, publication or campaign replay.

### Release Unit Declaration Component

[`ReleaseUnitDeclarationReader`](Repository/ReleaseUnitDeclarationReader.cs) parses
the existing `workflow-delivery.release-unit.yml` application schema with YamlDotNet.
It retains the supplied source path and every ordered build, entry point and output.
Entry coordinates remain relative to the declaration directory. Structural errors,
empty required sets and duplicate build or unit-wide output identities fail.
The caller obtains exact committed content; a supplied string/path does not establish
revision identity. The Node transaction associates native entries and the current
registered package definition; other definitions/variants remain unavailable and
must reject unresolved necessary facts.

### Paired Python Producer-to-Owner Component

[`PythonOwnerProjection`](Repository/PythonOwnerProjection.cs) consumes the complete
resolved native ordinary/build/group graph and supplied actual quality bindings at
both committed endpoints. It unions producer relations before following paths to
quality owners, retaining input-only intermediaries, cycles, nested nearest-member
ownership and each initiating path/revision. Repository directory coordinates join
the endpoint graphs; names and opaque native IDs do not associate moves. Removed
and added coordinates retain their independent endpoint input associations.

Standalone targets and root/shared inputs require exact supplied consumers. A known
native input with no reachable quality owner yields an explicit empty owner set;
missing scope, dangling relations, unknown owners and unsupported committed entry
kinds fail. The component returns owner associations, including basis-only owners,
without selecting deleted checks or changing the core's origin/check guards.

This is a supplied-fact projection, not a second target declaration or native
collector. Callers must bind complete native graphs and derive actual quality
bindings from native pytest configuration/adopted checks. The controlled assembly
below binds supplied targets and actual checks. The
[native Python CI caller](#native-python-ci-caller) implements paired integration;
ordinary native group qualification remains required. Controlled projection tests
execute no native Python helper.

### Controlled Paired Python Fact Assembly

[`PythonFactsAssembler`](Repository/PythonFactsAssembler.cs) composes supplied native
endpoint graphs, committed Git entries, pytest configuration and fully resolved
package checks. `PythonPytestTargets` binds actual configured targets; the assembler
retains the supplied runner, variant, native dimensions and supported import mode.
It creates no independent target roster, dependency parser or synthetic quality
owner for input-only members.

Package owners retain complete supplied checks, presets, Release Unit relations
and explicit inputs outside their member directories. Exact committed ownership
uses nearest membership across all native producers, including nested input-only
members. All actual quality owners and available core relations/prerequisites are
validated before any path is certified, including unselected and basis-only owners.
Possible missing explicit inputs remain in native projection, while ordinary core
input coordinates include only committed endpoint paths.

The existing producer projection completes paired closure and preserves each
initiating path/revision. Only complete empty closure at every present endpoint
certifies an unaffected path. The shared core receives the original changed paths,
full-mode flag and paired reasons; it owns candidate survival, relation/unit
expansion and complete checks/prerequisites. Controlled integration tests establish
this supplied-fact composition. They execute no native Python helper and do not
qualify complete native collection, package preset expansion, preparation/execution
or caller cutover.

## Validation and CI Ownership

The single test project is
[`WorkflowDelivery.Tests.csproj`](../../../../tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj).
The existing `dirs.proj` traversal and root `ci.yml` .NET test job discover it;
`ci_scope.py` discovers the new native project roots. No second test workflow or
new job owner is introduced. Run the bounded suite with:

```powershell
dotnet test --project tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj
```

Tests cover selection, result completeness, the JSON CLI boundary, PNPM identity
and opaque script adaptation, committed NBGV input mapping, quality declaration
traversal and native process failure/cancellation. NBGV tests use isolated local Git
objects; PNPM metadata tests use controlled native responses. Process tests use the
repository's PowerShell tool; PNPM replay tests execute no native PNPM query.
These tests do not qualify complete native fact collectors. Each
native integration still needs the migration plan's concrete coverage and caller
validation before switching a workflow.

### NuGet remote consumer component

`DotNetRemoteConsumer` generates a native restore graph for an external exact-version
`net10.0` consumer and invokes official NuGet `RestoreRunner`. An owned source-local
HTTP resource supplies read credentials only to the selected origin, with bounded
body reads and one credential-free archive redirect. Native NuGet owns resolution,
retries and extraction; the application compares actual LockFile inputs and installed
original bytes before native Build/GetTargetPath and marker invocation.

The caller must execute this component in a separate no-write process/job with fresh
external package, HTTP, plugin, profile, CLI and temporary directories. Product child
commands use cleared explicit environments without the read capability. Source-local
cache settings do not replace that process/job boundary. The ordinary NuGet caller
above supplies the separate receiving job; this component's controlled qualification
does not establish live-feed behavior or publication authorization.

### Python public index observation component

`PythonIndexObservation` consumes a completely qualified original wheel/sdist pair
and caller-prepared `PythonIndexTools` (maintained interpreter, committed helper
and owned empty scratch). It reads the selected public TestPyPI or PyPI Simple
Index without credentials. The helper's `index-facts` operation uses maintained
`packaging` for canonical names, version equality, filenames and file metadata;
C# does not implement a Python version or dependency engine.

The component distinguishes complete exact state, absent originals, exact subsets,
conflicting metadata/bytes and unavailable facts. It accepts originals only on
the selected HTTPS storage host, does not follow redirects, and compares actual
size/digest with the qualified originals. One observation performs at most one
index and two original reads, below the contract's nine-read ceiling, within the
90-second phase and body-inclusive 30-second request limits. Index and archive
bodies remain bounded by the
[concrete integration contract](../../../public/lib/three-workflow-delivery-v3/docs/migration-strategy.md#concrete-python-release-integration).

Native facts run in an isolated Python process with no inherited environment or
user-site/import path. Temporary index input is removed after use. Returned
diagnostics contain bounded stage/status and sanitized termination information;
they do not retain arbitrary URLs, index bodies or raw native diagnostics. A
failed/cancelled/timed-out phase cannot permit writes even if earlier files were
observed exact.

Controlled HTTP/native-process tests and local maintained-packaging/isolated CLI
tests establish these component boundaries. Ordinary final-source CI is still
required before delivery; these tests do not establish deployed index behavior,
publication, remote consumer or ordinary Release caller qualification. The
[native remote consumer](#native-python-remote-consumer-component) and
[ordinary Python Release caller](#ordinary-python-release) describe their
implemented integration and remaining qualification; native publisher
qualification is described below.

### Native Python publisher component

`PythonNativePublisher` invokes the selected native UV once for one qualified
original from the complete wheel/sdist pair. It accepts the existing TestPyPI
Buddy or PyPI Official coordinates; live Official requires the selected main ref,
while Buddy accepts branch refs. Dry run never enters this component. The caller
owns fresh complete observation, missing-file order, intermediate readback and
separate remote verification; this component creates no retry or recovery loop.

UV owns multipart, OIDC, token exchange and supported native retries/redirects.
The invocation selects an explicit upload URL, mandatory trusted publishing,
disabled keyring, no config or attestations and one literal original path. It
checks the transferred original pair, uses caller-prepared UV with fresh external
home/config/cache/temp directories, inherits no environment, receives only the
required native GitHub context and uses a 180-second process deadline. The shared
native executor closes stdin before awaiting the process; an input closure failure
retains its actual failure and possible effects.

Only complete native `add-mask` controls reach the caller's runner stderr writer.
All native diagnostic text remains transient and is omitted from persisted
results, including partial/error output. Results preserve actual termination,
exit and duration, an explicit diagnostic limitation and any masking-forwarding
failure. A masking failure does not invent a different native outcome; native
success alone does not establish business completion or attribute insertion.

Controlled command/output tests and a local native EOF test qualify these
component boundaries. They do not invoke UV publication, acquire credentials or
establish deployed OIDC/index/publisher behavior. Remote consumers and ordinary
caller integration retain their separate qualification
under the [concrete Python contract](../../../public/lib/three-workflow-delivery-v3/docs/migration-strategy.md#concrete-python-release-integration).

### Native Python remote consumer component

`PythonRemoteConsumer` consumes one selected format from a completely qualified
original wheel/sdist pair in a separate caller-owned no-write job. Native UV
resolves the exact version from only the selected TestPyPI or PyPI index and
enforces the original archive SHA-256. Wheel and sdist require their respective
native format flags; no local-original path, alternate index or populated cache
can supply remote acknowledgement.

Wheel uses a fresh external venv. Sdist reuses `PythonPackageOperations`' frozen
root tool preparation and noneditable backend/plugin conversion, then builds and
installs in Git-free external state. Tool preparation has no publication
capability. The isolated native helper requires the product to be absent before
installation and checks installed metadata, import origin and the registered API
afterward. Native UV owns dependency/build checks, download, archive hash
validation and installation; this component maintains no backend roster,
resolver, downloader or target witness.

Commands inherit no environment and receive only prepared native tool paths and
owned state. The component enforces a ten-minute whole-phase deadline and bounded
native commands, stops after any unsuccessful or cancelled operation, and retains
actual reached termination/exit/duration beside the required consumer outcome.
Arbitrary native diagnostics are omitted; installation change JSON is not archive
provenance. Failure or missing helper facts cannot qualify consumption.

Controlled command/fact scenarios qualify these boundaries; they do not execute
live index consumption or establish deployed destination behavior. Ordinary caller
cutover and live qualification retain their separate gates under the
[concrete Python integration](../../../public/lib/three-workflow-delivery-v3/docs/migration-strategy.md#concrete-python-release-integration).

### Ordinary Python Release

The ordinary Python CLI and `workflow-delivery-v3-python-smoke.yml` compose the
accepted preparation, public observer, native UV publisher and remote consumers.
The workflow selects TestPyPI Buddy or PyPI Official, defaults to dry run and
uses four Linux jobs: complete preparation, public observation, conditional
publication and separate required verification/result. This replaces the
former ordinary Python CLI and per-stage record action. Historical campaign
readers and their native setup retain their actual consumers.

Build complete source/control and export the frozen root `dev` tool recipe before
product execution. The native export omits workspace/local projects and retains
registry hashes; receiving jobs install wheels through UV into fresh isolated
tool environments. There is no second dependency roster or transferred venv.
Control, preparation/originals, observation, publication and final verification
cross only actual job boundaries, using immutable artifact IDs, platform digest
validation, actual producer/source/ref/mode binding and 45-day retention.

Dry run qualifies the pair and describes supported missing writes. An initially
exact live pair requires both remote consumers but no publisher. Only the
actual publisher receives the existing destination Environment and OIDC write
capability; it executes transferred control and tools without product builds.
Native workflow concurrency serializes the selected physical registry/package
through required verification. These settings do not establish current native
Environment or trusted-publisher registration eligibility.

The publisher refreshes the complete state and can only remove initially
reviewed missing writes. Wheel precedes sdist. Acknowledged native success and
exact intermediate readback are required before another write; abnormality,
mask-forwarding failure, cancellation or unsafe state stops later writes.
Separate no-write verification runs after handled publication failures and
requires both exact-bound format consumers plus complete public readback.
Business satisfaction preserves every returned invocation's actual outcome;
missing publication data after initial absence leaves effects unknown.

The same typed CLI is available to local callers:

```text
release prepare-python <request.json>
release prepare-python-index-tools <tool-request.json>
release observe-python <preparation.json> <original-directory> <index-tools.json>
release describe-python <preparation.json> <observation.json>
release publish-python <preparation.json> <original-directory> <initial-observation.json> <index-tools.json> <publisher-tools.json>
release consume-python <preparation.json> <original-directory> <python-wheel|python-sdist> <package-tools.json>
release complete-python <preparation.json> <initial-observation.json> <final-observation.json> \
  <wheel-consumer.json|-> <sdist-consumer.json|-> <publication.json|->
```

Inputs use the source-generated typed contracts; duplicate/unknown fields and
missing required values are rejected. Original directories and native tool paths
are absolute; all components retain their own fresh external-state requirements.
CLI stdout carries JSON results, while publisher masking controls use stderr.
Command failure is data for separate completion, never an invented upload success.
Local controlled scenarios do not establish hosted caller qualification, deployed
index/OIDC behavior or publication authority. The ordinary final-source Linux CI
gate and independent evidence reconciliation remain required before delivery.
