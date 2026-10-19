# Workflow Delivery

Workflow Delivery is the repository's internal C# application for change
validation and release delivery. It uses native ecosystem tools for repository
facts, versions, builds, packages and consumers. CI selects and checks an exact
candidate; Release independently qualifies one complete request.

The [project portal](docs/README.md) routes requirements, architecture, concrete
integration contracts and retained evidence. This page owns the implemented
operator interface. It grants no release, experiment or access authorization.

## Commands and Transfers

Use the repository-pinned SDK from the repository root:

```powershell
dotnet build src/private/app/workflow-delivery/WorkflowDelivery.csproj
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll `
  ci comparison pull_request native-event.json <tested-commit>
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll `
  ci plan request.json
```

The application emits JSON on stdout; diagnostics use stderr. CI returns `0`
for a valid plan or satisfied result, `1` for an unsatisfied result, and `2` for
invalid input, failed planning or unsupported usage. Release preserves actual
command outcomes independently of business satisfaction. Redirect stdout when
a receiving process needs a file.

Use the checked-in ordinary workflows and the finite typed
[CI callers](CI/Callers/CiCallerCli.cs) to compose native preparation and requests. The
[CLI](Program.cs), source-generated transfer serialization and
[Release CLI](Release/ReleaseCli.cs) define current internal command signatures
and types; internal formats have no compatibility promise. A request must supply
its required frozen native facts and owned directories, rather than a latest
artifact, ambient fallback or second dependency declaration.

| Interface                                                                | Purpose                                                                                           |
| ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------- |
| `ci comparison`, `ci control-inputs`                                     | Resolve the event comparison and actual control-test resource consumers                           |
| `ci caller-control prepare`, `ci caller-dotnet/python/ruby/node`         | Compose existing CI requests, original plans and exact transfers                                  |
| `ci native-preparation-plan`                                             | Select the original Linux managed preparation scenarios                                           |
| `ci plan`, `ci plan-node`, `ci plan-node-group`                          | Select declared work from native paired facts and reasons                                         |
| `ci prepare-python-group`, `ci plan-python-group`, `ci run-python-group` | Prepare, select and execute the original Python plan                                              |
| `ci prepare-ruby-group`, `ci plan-ruby-group`, `ci run-ruby-group`       | Prepare, select and execute Ruby checks and transfer native Node contributions                    |
| `ci run-dotnet`                                                          | Execute native managed work                                                                       |
| `ci run-node`, `ci run-python-package`, `ci result`                      | Execute selected work or collect its candidate-bound results                                      |
| `release prepare-*`, destination observation/description                 | Independently qualify originals and describe exact, absent, partial, conflicting or unknown state |
| `release job-inputs ruby/python/npm/nuget <operation>`                   | Prepare existing ordinary Release caller inputs and transfers                                     |
| `native-message-schemas <directory>`                                     | Regenerate static descriptions of maintained native helper messages                               |
| Destination publication and completion commands                          | Invoke the native publisher, require separate registry readback and report truthful results       |

The [static native helper contracts](Native/contracts/) come from actual serializer
metadata. Managed tests check regeneration; native helper tests use the static
schemas directly. Native identity and business checks remain separate.

Ordinary Release callers use typed `job-inputs` operations for `request`, `originals`,
`receive <phase>` and `state`. `receive` is the one check of the downloaded
preparation in each later job: it binds its subject to the request and applies the
ecosystem's complete preparation check before creating the phase output directory.
Every `job-inputs` entry rejects a live run attempt other than 1, so partial
reruns of later jobs stop too. Native tool-config operations
remain ecosystem-specific. npm Official simulation uses `simulation-request` in
its single job. Each `complete-*` command takes preparation, initial observation,
final observation and publication (`-` only when omitted) paths. Missing
required publication information remains unknown even when final bytes match.

### Source Layout

| Directory       | Contents                                                                        |
| --------------- | ------------------------------------------------------------------------------- |
| `CI/`           | Contracts, impact planning, per-ecosystem group execution and result collection |
| `CI/Callers/`   | Typed CI callers composing each ecosystem's requests and transfers              |
| `Repository/`   | Native fact readers and assemblers, CI scope and Git materialization            |
| `Release/`      | Release requests, preparation, job inputs, state and completion                 |
| `Destinations/` | Registry observation, publication and the shared registry HTTP client           |
| `Ecosystems/`   | Shared package build, inspection and clean consumption used by CI and Release   |
| `Platform/`     | Git, native processes, bounded paths and the .NET CLI                           |
| `Native/`       | Python and Ruby helper scripts and their generated message schemas              |

## Implemented Scope

Native package recipes currently cover the four real products under
[`tests/private/app/workflow-delivery/fixtures/products/`](../../../../tests/private/app/workflow-delivery/fixtures/products/).
Their ordinary coordinates are unchanged. They have no special trust, admission
policy or source witness. Moving a fixture changes its native source/version
inputs; it promises neither old/new byte equality nor republication.

The public [nbgv-python](../../../public/lib/nbgv-python/README.md) component
and its sample remain independent projects. The former public Python control
package, scanner, helper applications, dynamic admission files and closed
campaign entries are retired. Original campaign audit readers use
[producer-qualified historical source](docs/validation/historical-readers.md).

### CI Selection and Quality

Root [`ci.yml`](../../../../.github/workflows/ci.yml) owns ordinary CI.
Comparisons use actual immutable Git objects. Native base/candidate facts,
project declarations, shared inputs and genuine resource consumers select work
and explain its reasons. Unknown necessary ownership or failed extraction
blocks planning; it does not silently select everything. Explicit full
validation remains available.

Projects select supported quality presets through their existing declarations.
All selected required checks and supported variants must return satisfied
results. Missing, skipped, cancelled or incompatible required outcomes cannot
pass. Native tools own build ordering and caches; Actions owns job execution and
concurrency. The application supplies no general graph scheduler or cache layer.

### Native Node CI Caller

The supported PNPM CLI supplies workspace and resolved dependency facts. Source
projects and publish directories retain their distinct identities; Workflow
combines native endpoint facts without parsing or resolving dependencies itself.
Root CI runs the adopted package and selected retained Node matrix. Ruby's
successful same-run typed contribution is received once by Node, with no native
recollection or parallel declaration. Planning needs synchronized frozen native
locks and tool preparation, without endpoint product installation. Execution
installs selected product dependencies. The existing workflow matrix and native
mise selection own runtime variants and the configured packaging context.

### Native .NET Group Interface

The existing Windows .NET job receives the exact original plan and complete
candidate control artifact, then executes and collects without replanning.
Native MSBuild owns evaluated projects, imports, resources and references;
NBGV owns version projections. The NBGV auxiliary cache project is the accepted
bounded native exception, not a business release unit or custom version cache.
The current managed recipe's runner/configuration limits follow the
[concrete integration contract](docs/integration/dotnet.md#native-net-ci-group).

### Native Python CI Caller

UV workspace metadata and PyPA passive extraction supply Python facts.
Preparation materializes committed endpoints, freezes native tool and build
inputs and performs one paired selection. Root CI executes the original mixed
plan and retains its native preparation, commands and results. The native
Python helper suite remains a native Python consumer; Ruby helper behavior belongs
to its [native RSpec carrier](../../../../tests/private/app/workflow-delivery/Native/Ruby/README.md).

For explicit full validation of committed HEAD:

```powershell
mise run test:python
```

This uses the same native group and does not validate uncommitted source by
substituting a worktree snapshot.
The current local full entry supports Linux. Its native launcher builds the exact
committed control once; `ci caller-local-python` composes the explicit full group.
Static tools and dependencies come from MISE and native manifests/locks. The
launcher accepts `--repository` and `--directory`;
state must be fresh and outside the source checkout. It prints the source and
retained result location before execution.

### Native Ruby CI Caller

Unprivileged RubyGems/Bundler and RSpec interfaces own gemspec evaluation,
installed identities and test result interpretation. Root CI receives exact
scope/control transfers, plans from each committed lock context without installing
registry dependencies, and executes the original selected RSpec/gem checks, which
install their own frozen bundles. Source gems and installed dependency
identities remain distinct. Unknown dynamic input relationships are unsupported.

## Release

Preparation, project code and local clean consumers have no publication capability.
Control is built and transferred before product execution. An isolated publisher
receives bound qualified originals and uses the actual destination's native
permission policy. Effective Environment configuration owns approval; the
application does not mirror reviewers or re-prove platform approval.

Completion, retry and recovery semantics are owned by the
[requirements](docs/requirements.md#observation-retry-and-recovery), the MLD's
[decision table](docs/middle-level-design.md#decision-table) and
[recovery contract](docs/middle-level-design.md#recovery), and the
per-destination completion conditions in each
[integration contract](docs/middle-level-design.md#integration-mapping).

### Official npm Dry Run

The existing Official simulation workflow calls `prepare-npm`, `observe-npmjs`
and `describe-npmjs`. It uses NBGV and locked PNPM to build/test/pack the complete
original archive and perform clean installed API consumption, then reads bounded
public npmjs state. Development refs can exercise this process without write
capability. Its description is hypothetical and never claims live completion.

### npm Buddy Release

The existing Buddy workflow accepts any dispatched ref without an application
ref-class check (see the [Official channel
scope](docs/integration/common.md#official-channel-scope)) and transfers
selected source/control, originals and qualification by immutable artifact IDs.
GitHub package association and exact archive state are checked natively. The
actual publisher gate precedes the write-capable invocation; a separate no-write
registry readback and common result component establish completion. Tags remain
routing metadata, not original-package authority.

### NuGet Buddy Release

The existing entry and reusable caller use native MSBuild/NBGV Build/Pack,
original nupkg inspection and a clean local consumer. Bounded observation uses
maintained NuGet interpretation; the publisher uses the official native upload
invocation. A fresh registry observation and original-byte comparison run in a
separate read-only verification job. A successful upload alone cannot satisfy
the request.

The Windows `NuGet native preparation (Windows)` CI job and the
[NuGet reproducibility workflow](../../../../.github/workflows/workflow-delivery-v3-nuget-reproducibility.yml)
run the `NuGetNativePreparation` and `NuGetNativeScenarios` tests. Their
triggers, evidence carrier, failure ownership and retained diagnostics are
owned by the [native .NET CI group contract](docs/integration/dotnet.md#planning-execution-and-collection).

### Ordinary Python Release

The existing four-job caller independently qualifies the complete wheel/sdist
set, including Git-free source-archive consumption. Native public-index and
installed metadata interpretation retain selected-index origins and original
hashes. UV owns publication. A supported fresh request may upload only missing
files after exact subset observation; no atomic two-file publication is promised.
Uncertain reads, unsupported partial state or conflicting files block mutation.
Final index readback and completion remain required when state is already exact.

### Ordinary Ruby Release

The existing caller qualifies one original gem through native gemspec evaluation,
archive inspection and clean installed API consumption. GitHub Packages and
RubyGems retain their separate observation, authorization and publisher
contracts. The native publisher invocation, command outcome and required
read-only source readback remain distinct from business satisfaction. Closed bootstrap
and native campaigns are not alternate operational entries.

## Validation and Evidence Limits

The application has one managed test project and native helper suites. Root
workflow contracts live under `tests/eng/`; the managed project also runs actual
Release workflow step bodies under their native shells
(`BashWorkflowCallerTests`, `NuGetWorkflowCallerTests`). From the repository
root:

```powershell
dotnet test --project tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj
```

The project's native MTP default excludes `NuGetNativePreparation` and
`NuGetNativeScenarios`; the Windows jobs named above own them. It also excludes
`PythonNativeContract`, which the Linux native-preparation job runs under
`mise exec --locked dotnet python uv` with
`--filter 'TestCategory=PythonNativeContract'`, and `PnpmNativeContract`, which
the same job runs under `mise exec --locked dotnet node pnpm`. To run them locally,
override the default with `--filter 'TestCategory=NuGetNativePreparation'
--minimum-expected-tests 2` or `--filter 'TestCategory=NuGetNativeScenarios'
--minimum-expected-tests 3`. An explicit filter replaces the default, so an
unrestricted local run uses `--filter 'FullyQualifiedName~WorkflowDelivery'`.

The managed test assembly points `TMPDIR`, `TMP` and `TEMP` at one short
`wdt-<pid>-<random>` directory for the run and tries to delete it at assembly cleanup;
a later run removes such directories whose process has exited, including ones
left by killed runs or a failed cleanup.
Ruby helper specs use Ruby's `Dir.tmpdir`, which follows the `TMPDIR` that the
group runner already supplies. `mise run test:python` keeps its results under
`${XDG_CACHE_HOME:-~/.cache}/three/python-full/` unless `--directory` is
given, and prints the command that removes them, including after a failure.
With `--directory`, that command removes only the given directory.

Use complete affected scenario/contract tests, HK, hooks and independent review
for changes. Real transfers, supported native tool interfaces and required
ordinary runner qualification need their actual evidence. Controlled/local
success does not qualify a deployed feed, OIDC, account policy or publication.
Routine release need not duplicate-build; same-input reproducibility and
cross-commit byte equality are different claims.

Artifacts must survive their actual approval/operation window and be selected
by immutable producer identity. Expired or missing required originals block the
operation; a display name or latest-artifact search is not a replacement.
Historical observations retain their original revision, attempt, scope,
counterexamples and failures. No live campaign, postmerge test, authentication,
Environment, publisher-registration or access change follows from source
acceptance. The [Wave](../../../../docs/delivery-wave.md) owns current work
authorization; further substantive work requires an accepted entry.
