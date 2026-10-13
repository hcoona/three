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

Use the checked-in ordinary workflows and `eng/scripts/run_*_ci_group.py`
callers to compose their native preparation and typed requests. The
[CLI](Program.cs), source-generated transfer serialization and
[Release CLI](Release/ReleaseCli.cs) define current internal command signatures
and types; internal formats have no compatibility promise. A request must supply
its required frozen native facts and owned directories, rather than a latest
artifact, ambient fallback or second dependency declaration.

| Interface                                                                | Purpose                                                                                           |
| ------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------- |
| `ci comparison`, `ci control-inputs`                                     | Resolve the event comparison and actual control-test resource consumers                           |
| `ci plan`, `ci plan-node`, `ci plan-node-group`                          | Select declared work from native paired facts and reasons                                         |
| `ci prepare-python-group`, `ci plan-python-group`, `ci run-python-group` | Prepare, select and execute the original Python plan                                              |
| `ci prepare-ruby-group`, `ci plan-ruby-group`, `ci run-ruby-group`       | Prepare, select and execute Ruby checks and transfer native Node contributions                    |
| `ci plan-dotnet-group`, `ci run-dotnet`                                  | Select and execute native managed work                                                            |
| `ci run-node`, `ci run-python-package`, `ci result`                      | Execute selected work or collect its candidate-bound results                                      |
| `release prepare-*`, destination observation/description                 | Independently qualify originals and describe exact, absent, partial, conflicting or unknown state |
| Destination publication, consumption and completion commands             | Invoke the native publisher, require separate consumers and report truthful business results      |

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
recollection or parallel declaration.

### Native .NET Group Interface

The existing Windows .NET job receives the exact original plan and complete
candidate control artifact, then executes and collects without replanning.
Native MSBuild owns evaluated projects, imports, resources and references;
NBGV owns version projections. The NBGV auxiliary cache project is the accepted
bounded native exception, not a business release unit or custom version cache.
The current managed recipe's runner/configuration limits follow the
[concrete integration contract](docs/migration-strategy.md#native-net-ci-group).

### Native Python CI Caller

UV workspace/metadata and the pinned native supplement supply Python facts.
Preparation materializes committed endpoints, freezes native tool and build
inputs and performs one paired selection. Root CI executes the original mixed
plan and retains its native preparation, commands and results. The native
Python and Ruby helper suites remain actual consumers. Legacy V3-only adapter
preparation and static-reference scanning are removed.

For explicit full validation of committed HEAD:

```powershell
mise run test:python
```

This uses the same native group and does not validate uncommitted source by
substituting a worktree snapshot.

### Native Ruby CI Caller

Unprivileged RubyGems/Bundler and RSpec interfaces own gemspec evaluation,
installed identities and test result interpretation. Root CI receives exact
scope/control transfers, prepares each committed lock context and executes the
original selected RSpec/gem checks. Source gems and installed dependency
identities remain distinct. Unknown dynamic input relationships are unsupported.

## Release

Preparation, project code and clean consumers have no publication capability.
Control is built and transferred before product execution. An isolated publisher
receives bound qualified originals and uses the actual destination's native
permission policy. Effective Environment configuration owns approval; the
application does not mirror reviewers or re-prove platform approval.

The publisher reobserves exact state and performs only still-needed supported
native invocations. Failure, ambiguity or interruption stops later application
mutation. Native transport retries retain their documented bounds; the
application adds no retry or administrative repair loop. Exact byte/state and
required consumer verification establish satisfaction separately from upload
acknowledgement. An absent result cannot prove publication never started.

Ordinary recovery is a fresh request at the original source/control revision,
with rebuilt qualification, fresh observation and current native authorization.
It cannot combine repaired latest control with an old target, reuse approval,
rewrite a terminal failure or perform deletion/yank/restoration. Registry
semantics govern external writers; Actions concurrency is not a global lock.

### Official npm Dry Run

The existing Official simulation workflow calls `prepare-npm`, `observe-npmjs`
and `describe-npmjs`. It uses NBGV and locked PNPM to build/test/pack the complete
original archive and perform clean installed API consumption, then reads bounded
public npmjs state. Development refs can exercise this process without write
capability. Its description is hypothetical and never claims live completion.

### npm Buddy Release

The existing Buddy workflow accepts allowed development refs and transfers
selected source/control, originals and qualification by immutable artifact IDs.
GitHub package association and exact archive state are checked natively. The
actual publisher gate precedes the write-capable invocation; a separate scoped
native consumer and common result component establish completion. Tags remain
routing metadata, not original-package authority.

### NuGet Buddy Release

The existing entry and reusable caller use native MSBuild/NBGV Build/Pack,
original nupkg inspection and a clean local consumer. Bounded observation uses
maintained NuGet interpretation; the publisher uses the official native upload
invocation. Fresh remote restore and original-byte comparison run in a separate
read-only context before marker/API execution. A successful upload alone cannot
satisfy the request.

The Windows native scenario job retains original archives, DLLs, separate native
portable PDBs, debug paths, TRX attachments and binlogs. It requires all seven
current preparation, source/timestamp sensitivity and fresh-recovery cases to be
discovered, executed and passed. `IncludeSymbols=false` does not promise a PDB
inside the package. Fixture relocation requires this source's ordinary Windows
qualification; earlier native observations do not qualify changed source.

### Ordinary Python Release

The existing four-job caller independently qualifies the complete wheel/sdist
set, including Git-free source-archive consumption. Native public-index and
installed metadata interpretation retain selected-index origins and original
hashes. UV owns publication. A supported fresh request may upload only missing
files after exact subset observation; no atomic two-file publication is promised.
Uncertain reads, unsupported partial state or conflicting files block mutation.
Consumers and completion remain required when state is already exact.

### Ordinary Ruby Release

The existing caller qualifies one original gem through native gemspec evaluation,
archive inspection and clean installed API consumption. GitHub Packages and
RubyGems retain their separate observation, authorization and publisher
contracts. The native publisher invocation, command outcome and required
read-only consumer remain distinct from business satisfaction. Closed bootstrap
and native campaigns are not alternate operational entries.

## Validation and Evidence Limits

The application has one managed test project and native helper suites; useful
root workflow contracts live under `tests/eng/`. From the repository root:

```powershell
dotnet test --project tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj
```

The project's native MTP default excludes `NuGetNativeScenarios`; their existing
Windows CI job owns the seven required cases. To run those cases locally, override
the default with `--filter 'TestCategory=NuGetNativeScenarios'
--minimum-expected-tests 7`. An explicit filter replaces the default, so an
unrestricted local run uses `--filter 'FullyQualifiedName~WorkflowDelivery'`.

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
