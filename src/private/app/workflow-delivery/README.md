# Workflow Delivery

This private application implements the replacement Workflow V3 core. The
[project records](../../../public/lib/three-workflow-delivery-v3/docs/README.md),
[middle-level contracts](../../../public/lib/three-workflow-delivery-v3/docs/middle-level-design.md)
and [migration plan](../../../public/lib/three-workflow-delivery-v3/docs/migration-strategy.md)
remain in their current namespace until the old Python application's last
consumer retires. This entry describes the implemented interface, not a second
architecture authority.

## Current Scope

The application selects CI checks and collects candidate-bound check results.
The supplied-fact command and Node revision-to-plan command share one impact
algorithm. The Node command reads exact caller-prepared checkouts, native
PNPM relations, NBGV inputs and existing application declarations, then expands
selected project and package checks. A concrete Node executor runs the adopted
quality recipe and retains every check result and original package output.
The adopted fixture has a bounded Linux
[native build/package/consumer observation](../../../public/lib/three-workflow-delivery-v3/docs/research/node-ci-native-execution.md).
Git event mapping and Release commands remain pending.
Existing workflows still use their current implementations.

New ecosystem integrations follow the HLD's
[native integration boundary](../../../public/lib/three-workflow-delivery-v3/docs/high-level-design.md#native-integrations).
PNPM integration invokes the repository-selected CLI directly. The implemented
selection/result core consumes resolved facts and does not
interpret ecosystem manifests, dependency specifiers or version inheritance.

Native integrations must supply complete ownership and reverse-consumer facts for
their declared scope, including base and candidate relations, selected quality
presets and all required variants. They must report incomplete evaluation or
unsupported relevant shapes in `errors`. An empty error array is a producer's
result, not an admission certificate or proof that a reader is implemented.
The scope label must agree across revisions; it is not a coverage inference.

### PNPM Graph Component

[`PnpmGraphReader`](Repository/PnpmGraphReader.cs) reads wanted-lock workspace
membership, native dependency closures and every project's publish-directory
metadata. Full wanted-lock trees use exact singleton project queries so PNPM's
cross-project output deduplication cannot hide another consumer's nested inputs.
It joins resolved source/output directory identities, preserving
reachability rather than promising direct manifest edges. It does not parse
manifests, lockfiles, dependency declarations or installation-path encodings.
Package names do not establish source identity. Unmatched local directories and
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
base/candidate association. Caller cutover is still pending. The graph retains
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
managed assembly through its imported task path and normal assembly resolution.
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
qualifies the executor's adopted fixture composition on Linux; event, transfer and
group integration still precede caller cutover. The fixture's
native NBGV filters include its source and shared package/workspace/lock, toolchain
and license inputs. Five other Node projects have no adopted new quality
preset, and unmigrated/cross-group callers retain their current owners.

### Node Check Execution

[`NodeExecution`](CI/NodeExecution.cs) consumes that complete plan and one
caller-prepared checkout plus fresh external scratch directory. It validates
the supported required quality, runner, variant, package associations and prerequisites
before running work, then binds the exact clean tracked starting checkout.
Normal native build/stamping changes after that bind are permitted. Candidate
identities must equal the full commit returned by Git; aliases fail. An empty
plan still binds its starting checkout and executes no product commands. Checkout
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
task graph scheduler or interpretation of script bodies.

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
owned-root cleanup bound. The result keeps every planned key, command observation,
original output descriptor and execution failure. The existing collector decides
whether all required checks are satisfied. Controlled tests establish these
application contracts. The linked bounded native observation separately
qualifies this fixture's Linux composition and effects; it does not qualify other
presets or establish caller cutover.

## Commands and Transfers

Build with the repository-pinned SDK. From the repository root:

```powershell
dotnet build src/private/app/workflow-delivery/WorkflowDelivery.csproj
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll `
  ci comparison pull_request native-event.json <tested-commit>
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci plan request.json
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci plan-node node-request.json
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

The execution request adds no project, script, dependency or version declaration:

```json
{
    "checkout": "C:/work/three-candidate",
    "scratch": "C:/work/node-check-scratch"
}
```

`ci run-node` emits its candidate-bound execution result, including unsuccessful
checks, with exit code `0` or `1` according to the existing collector. Contract or
starting-materialization errors return `2` before product execution. Its `results`
array can be passed to `ci result`; in-process callers use the same typed values.

The source-generated JSON contracts use camel-case property names, required
constructor fields and named check statuses. Unknown fields and missing or null
required values fail. These internal formats have no historical compatibility
promise. The concrete types are in [Contracts.cs](CI/Contracts.cs).

The collected outcome retains the plan's `comparison`, `candidate` and declared
`scope`, including when no checks are selected. Its success applies to that
subject and supplied coverage only.

| Input                 | Required content and producer responsibility                                                                                                                                                       |
| --------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Planning request      | `basis`, `candidate`, `changedPaths`, `full`; the caller resolves the event's actual comparison/tested revisions and includes both rename paths and deleted paths                                  |
| Each revision's facts | `revision`, `scope`, `projects`, `sharedInputs`, `unaffectedPaths`, `errors`; native readers establish completeness for this declared scope                                                        |
| Project               | Stable `id`, repository-relative `directory`, build `dependencies`, distinct `qualityConsumers`, nullable `releaseUnit` and resolved `qualityPreset`, and concrete `checks`                        |
| Shared input          | Exact repository-relative `path` and project `consumers`, including actual version/configuration/lock inputs                                                                                       |
| Unaffected path       | Exact path the producer established has no consumers in this scope; no wildcard or unknown-path fallback                                                                                           |
| Check                 | `key` (`target`, `check`, `variant`), actual `dimensions`, `runner`, `required`, prerequisite keys and nullable artifact `package` subject; variant names identify resolved dimension combinations |
| Result array          | Each entry carries the exact `candidate`, check `key` and `status`: `Passed`, `Failed`, `Cancelled`, `TimedOut` or `Skipped`                                                                       |

Paths use Git's case-sensitive forward-slash spelling, without a leading slash,
empty components or traversal. A project directory owns its subtree, with the
nearest project owning nested paths. Shared-input consumers are added separately.
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
retain quality presets and all initiating path/revision/project reasons.

The collector requires every selected required result at the candidate revision.
Missing, failed, cancelled, timed-out, skipped and conflicting required results
prevent satisfaction. Repeated identical results are harmless; unexpected subjects
or unknown statuses fail. Advisory outcomes remain visible without failing the
required result. A native cancellation that prevents collection is not a success.
The eventual workflow must preserve that native conclusion; this command cannot
observe jobs that its caller did not report.

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
