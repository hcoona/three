# Workflow Delivery Node and npm Integration Contract

This integration contract owns the PNPM native facts, Node CI group, npm Release
caller and npm destination conditions. It refines the
[MLD](../middle-level-design.md), whose [integration
mapping](../middle-level-design.md#integration-mapping) states the shared scope
and authority of the integration contracts.

## Native PNPM Facts

The owner selected the supported PNPM CLI for the application's Node
integration. Do not add a Node helper or embedded LibNode for that integration.
The [PNPM 12 assessment](../research/pnpm-native-planning.md) supplies source
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

## Native Node CI Group

This concrete Node integration contract refines the MLD's selection and transfer
contracts. Source, local and hosted evidence retain their distinct qualification
boundaries.
The group adopts the npm fixture's existing quality/package obligations. Other
native workspace members retain their existing Node executor. Native PNPM impact
is computed over the complete supported workspace before this ownership split.
An unadopted member needs no replacement preset merely to retain its checks.

### Endpoint Responsibility and Selection

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
The [Python quality and caller mapping](python.md#python-quality-and-caller-mapping) and
[native Python CI group](python.md#native-python-ci-group) own both-endpoint native facts,
the single original mixed plan and candidate execution. Preserve the retained
other jobs' candidate-execution semantics. A basis-only owner explains a
removed input; it does not execute a deleted target. Preserve both general .NET
and AzureAuth owners when applicable and the native
[control-test resource reasons](dotnet.md#control-test-input-routing).

Retired pytest preparation gates and the former Ruby pytest carrier do not give
the control application, managed test tree or Ruby fixture blanket Python
responsibility. Their actual native managed/Ruby and record consumers explain
those coordinates. Existing Python helper and caller bindings still select real
Python consumers, including the passive extraction helper, even when another native owner
also consumes the input. A missing native responsibility or unresolved Python
claim remains a planning failure.

Record responsibility reads only `bindings[].{id,state,path}` from the endpoint's
[record-family catalog](../../../../../../docs/governance/record-families.yaml) with
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
record-system responsibility. These scripts, and the matcher the checker
imports, run under their mise tasks rather than as Python group targets, so they
receive no Python caller responsibility. This finite route neither invokes the
checker nor adds CI selections; it does not infer owners for other local
controls, tests or engineering paths. Without the discovered task, these paths
fall back to Python caller responsibility, which the Python group cannot
resolve, so planning still fails closed. A present coordinate with neither responsibility nor a native
consumer remains a planning failure, not a run-everything fallback. Actual
product/control consumption remains independently native.

Source-conformance responsibility for HK configuration comes from the
endpoint's native import graph of root `hk.pkl`: the scope job prepares the
locked mise `pkl` at each endpoint, and `pkl analyze imports` with the
checkout as its root directory names every repository-local module it loads.
Remote package and standard-library modules are not repository coordinates.
The graph is read only when the endpoint commits `hk.pkl`; a failed,
malformed or out-of-checkout read fails planning instead of dropping
responsibility. Directory names and a repository-local Pkl parser do not infer
this responsibility.

Five finite rules cover repository tooling configuration. `renovate.json`,
`.github/renovate/**` and `.github/workflows/renovate.yml` receive owner
`renovate`, target `.github/workflows/renovate.yml` and rule
`native-renovate-configuration`. A present coordinate whose repository-relative
path occurs as a complete path token in the endpoint's committed root
`hk.pkl` or `renovate.json` receives owner `conformance`, target
`.github/workflows/ci.yml#conformance` and rule `literal-tooling-reference`.
A complete token is not preceded or followed by a path character, so
`PklProject` does not match `PklProject.deps.json`. This is a literal
reference, not proof that a step reads the file, and it parses no shell
commands: locked HK 2.6.0 `check --plan --json` reports step status, reasons
and file counts but no per-step files. Imported HK modules keep their native
import route. A present direct child of the root `.github/workflows/` ending in
`.yml` or `.yaml` receives owner `conformance`, target
`.github/workflows/ci.yml#conformance` and rule `native-hk-workflow-lint` when
the endpoint commits root `hk.pkl` and `ci.yml`. HK's `actionlint` step checks
these files; its glob covers workflow YAML under any `.github/workflows/`
directory, and this rule is a fixed copy for the root directory only. A present `*.sh` or
`*.bash` file that the shared `shellcheck`/`shfmt` exclusions do not match
(`**/.github/**`, `**/.specify/**`, `**/generated/**` and the root-only
`.agents/**`) receives owner `conformance`, target
`.github/workflows/ci.yml#conformance` and rule `native-hk-shell-lint` under the
same `hk.pkl` and `ci.yml` condition. This is a fixed copy of those steps' shared
glob and exclusions. The rule adds to, and does not replace, a script's existing owner. A committed `*.py`
file whose first line is `# /// script` with a later closing `# ///` line (PEP
723 inline script metadata) has its `native-python-caller-responsibility`
reason replaced, never added to, by owner `conformance`, target
`.github/workflows/ci.yml#conformance` and rule `native-hk-python-lint` when the
file is not `conftest.py` or under `tests/`, nothing but its `.py` suffix would
give it a Python reason (no retained-job or Python input list, `tests/eng`,
AzureAuth input, enclosing non-root `pyproject.toml` or record task), the shared
`ruff`/`ruff_format` exclusions that can match a Python file (`**/.github/**`,
`**/.specify/**`, `**/generated/**`, the root-only `.agents/**` and
`src/private/lib/scan-restoration/skills/**`) do not match it, and the endpoint
commits `hk.pkl` and `ci.yml`. Other owners are kept. No rule selects a
retained job or adds Python execution.

The last three rules read one committed copy of their HK step names, globs and
exclusions, `Repository/resources/hk-scope-copies.json`, embedded in the control
application. The HK conformance step `hk-scope-drift`
(`eng/scripts/hk_scope_drift.py`, profile `small`, also run by `--all`)
evaluates root `hk.pkl` with the locked Pkl and fails unless each named `check`
step stays in a profile `ci.yml` runs, its glob still matches the copied
suffixes, and its exclusions whose final segment can name such a file equal the
copied set. Changes to `hk.pkl`, `src/private/lib/hk/**`, the copy, `ci.yml` or
the script select that step.

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

### Native Control-CLI Inputs

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
the [.NET contract](dotnet.md#control-test-resource-consumer).
The complete root graph additionally uses CI and locked-restore globals, so it
cannot replace that query without a reviewed context change. Existing endpoint
root restore supplies prerequisites; do not separately restore the test project.
Produce each complete graph once in its own context, reusing its in-memory facts
only when revision, checkout and operation inputs coincide. This is ordinary
invocation reuse, not an artifact cache or replay contract.

### Workflow, Runtime Variants and Results

Add one static Ubuntu group job after the existing Windows scope job and the
Ruby group. Their uploads expose same-run immutable artifact IDs. The group
downloads the scope selection, the complete candidate control transfer and
Ruby's typed contribution by those IDs, relying on the native download digest
check. Its caller binds the scope comparison to the checked-out candidate before
planning and binds the resulting plan to that comparison. Add no signature,
admission mirror or producer download/readmission loop. Plan every change,
independently of the old coarse Node Boolean. The job runs the transferred
candidate control directly; it does not build the CLI again.

The retained Node matrix depends one-way on scope and this group. Successful
planning supplies explicit retained-member selections/reasons and applicability;
failed/missing group planning cannot become a skip. Exclude exactly the group-owned
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

The Node job prepares the committed dotnet, Node and PNPM tools and invokes the
C# Node caller (`ci caller-node`) directly. Native automatic
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

### Fixture Ownership and Changed-Source Qualification

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

## npm Release and Destination Completion

The MLD's [post-publication completion](../middle-level-design.md#post-publication-completion)
contract applies to these destinations; this table owns their concrete
conditions and observation.

| Existing destination/use  | Native completion conditions to preserve                                                                                      | Post-publication observation                                                                                                   |
| ------------------------- | ----------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| GitHub Packages npm Buddy | Scoped package/version, intended repository association/access and original tarball; native non-overwrite/duplicate semantics | Package control, version manifest naming the exact version, downloaded tarball size/SHA-256 equal to the original              |
| npmjs Official simulation | Exact public package/version metadata and original tarball or authoritative absence; this caller selects no routing tag       | Report read/qualification/hypothetical result only; no live publication caller or completion claim is inferred from simulation |

The [npm Buddy integration](../../README.md#npm-buddy-release)
owns complete selected preparation,
native package-control/exact original-byte reads, Environment on the actual
publisher, one standard native upload, separate no-write registry readback and
complete common result. Its source-only validation does not qualify a new
hosted Live invocation. Historical acceptance readers retain their actual pinned
native producer source; they do not authorize replay.

## Concrete npm Release Integration

This section records the implemented npm Release behavior; it adds no
obligation beyond the code it cites. It neither reopens the completed npm
proving work nor authorizes publication, dispatch, authentication or access
changes. Changes follow the
[shared review core](../middle-level-design.md#release-integration-review).

- **Endpoints.** GitHub Packages Buddy observes package control at
  `https://api.github.com/users/hcoona/packages/npm/hcoona-release-smoke-npm` and
  metadata and tarballs at `https://npm.pkg.github.com`
  ([GithubNpmObservation `Registry`/`Api`](../../Destinations/GithubNpmObservation.cs)).
  It accepts only HTTPS port 443 on those hosts and the GitHub package storage
  hosts ([`ValidateUri`](../../Destinations/GithubNpmObservation.cs)). The
  npmjs Official simulation reads only `https://registry.npmjs.org`
  ([NpmjsObservation `ObserveAsync`](../../Destinations/NpmjsObservation.cs),
  [`ValidateUri`](../../Destinations/NpmjsObservation.cs)).
- **Observation bounds.** Each observation has a 60-second operation deadline
  and a 20-second deadline per request including its body, with metadata bodies
  limited to 1,000,000 bytes
  ([GithubNpmObservation `ObserveAsync`](../../Destinations/GithubNpmObservation.cs),
  [`Metadata`](../../Destinations/GithubNpmObservation.cs),
  [`ReadAsync`](../../Destinations/GithubNpmObservation.cs);
  [NpmjsObservation `ObserveAsync`](../../Destinations/NpmjsObservation.cs),
  [metadata read](../../Destinations/NpmjsObservation.cs),
  [`ReadAsync`](../../Destinations/NpmjsObservation.cs)). The GitHub reader attaches
  its Bearer credential only to `api.github.com` and `npm.pkg.github.com`
  ([`ReadAsync`](../../Destinations/GithubNpmObservation.cs)); npmjs reads
  are public. Both use the shared registry client and redirect contract in the
  [MLD](../middle-level-design.md#registry-observation-and-retention).
- **Preparation tools.** Node and PNPM follow the root
  [locked tool selection](common.md#locked-tool-selection)
  ([NpmPreparation](../../Release/NpmPreparation.cs);
  [NpmPreparationTests.cs](../../../../../../tests/private/app/workflow-delivery/Release/NpmPreparationTests.cs)).
- **Publication.** After a fresh observation finds the version absent and the
  original unchanged, the publisher runs one
  `npm publish <original> --registry https://npm.pkg.github.com --tag buddy-sha-<revision> --ignore-scripts --fetch-retries=0`
  with a 120-second deadline and no inherited environment, then observes again.
  Exact state needs no write, other states block it, and an abnormal native
  outcome permits reads but never another upload
  ([GithubNpmPublication `PublishAsync`](../../Destinations/GithubNpmPublication.cs)).
- **Permissions.** In the
  [Buddy workflow](../../../../../../.github/workflows/workflow-delivery-v3-buddy-smoke.yml),
  only the `publish` job, behind the `workflow-delivery-v3-buddy-approval`
  Environment, has `packages: write`; the observation and verification jobs have
  `packages: read`. Its concurrency group is the registry/package pair without
  cancellation. The
  [simulation workflow](../../../../../../.github/workflows/workflow-delivery-v3-official-simulate.yml)
  has `contents: read` only.
- **Non-overwrite.** GitHub Packages' native non-overwrite and duplicate
  semantics are relied on as stated in the completion table above; the
  application adds no reservation or replacement.
- **Validation cases.** Controlled tests cover single publication under current
  native state, an abnormal upload with observed business state and a
  substituted original
  ([GithubNpmPublicationTests.cs](../../../../../../tests/private/app/workflow-delivery/Release/GithubNpmPublicationTests.cs));
  exact identity and original bytes, longer or different bytes, absence,
  incomplete metadata, oversized responses, deadlines and cancellation
  ([GithubNpmObservationTests.cs](../../../../../../tests/private/app/workflow-delivery/Release/GithubNpmObservationTests.cs),
  [NpmjsObservationTests.cs](../../../../../../tests/private/app/workflow-delivery/Release/NpmjsObservationTests.cs)).
- **Non-claims.** These checks do not establish current token reach, publisher
  or Environment configuration, live registry behavior or a new publication.
  The npmjs simulation makes no live completion claim.
