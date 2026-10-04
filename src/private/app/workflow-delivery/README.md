# Workflow Delivery

This private application implements the replacement Workflow V3 core. The
[project records](../../../public/lib/three-workflow-delivery-v3/docs/README.md),
[middle-level contracts](../../../public/lib/three-workflow-delivery-v3/docs/middle-level-design.md)
and [migration plan](../../../public/lib/three-workflow-delivery-v3/docs/migration-strategy.md)
remain in their current namespace until the old Python application's last
consumer retires. This entry describes the implemented interface, not a second
architecture authority.

## Current Scope

The application selects CI checks from supplied, resolved repository facts and
collects candidate-bound check results. Its internal PNPM graph/script, Git and
NBGV input readers supply inputs to repository analysis. A quality-selection reader resolves existing
project declarations, and a concrete Node component expands its registered project
checks. Complete native fact assembly, Git event resolution, artifact/check
expansion, check execution and Release commands remain
pending. Existing workflows still use their current implementations.

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
Complete ownership/shared-input expansion, quality/version facts, base/candidate
assembly and caller cutover are still pending. The root project is retained as
`.` here; this graph is not directly accepted as the CI core's project facts.

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

This input mapping does not calculate version height or canonical package versions.
The pinned native legacy `version.txt` reader can retain loose-object handles until
managed finalization. Keep the source object store alive through the reading process;
context disposal does not promise immediate repository deletion. Test-fixture
finalization is not a production cleanup protocol.
Full projection still requires the clean exact-target/ref/tag/history context in
the [native NBGV evidence](../../../public/lib/three-workflow-delivery-v3/docs/research/nbgv-native-inputs.md).
Complete fact assembly, other quality presets and workflow cutover remain pending.

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

[`NodeInputConsumers`](Repository/NodeInputConsumers.cs) adapts supplied native
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

This is supplied-fact adaptation only. Root tooling consumers, shared toolchain
and workspace configuration coverage, complete fact assembly, event materialization
and caller cutover remain pending. The mapper does not infer unaffected paths or
certify a scope's completeness.

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
the registered test contract does not consume a build output. Artifact variants,
other presets, quality-input consumer mapping and complete fact assembly remain
pending. This component creates no new project adoption or workflow caller.

## Commands and Transfers

Build with the repository-pinned SDK. From the repository root:

```powershell
dotnet build src/private/app/workflow-delivery/WorkflowDelivery.csproj
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci plan request.json
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci result plan.json results.json
```

Commands emit one JSON value on stdout. Exit code `0` means a valid plan or
satisfied CI result; `1` means an unsatisfied result (including incompatible
result subjects) with its JSON explanation; `2` means malformed input, a planning
failure or unsupported usage, with a diagnostic on stderr. Redirect stdout
when a receiving process needs a file. In-process consumers call the same typed
operations without serializing their inputs.

The source-generated JSON contracts use camel-case property names, required
constructor fields and named check statuses. Unknown fields and missing or null
required values fail. These internal formats have no historical compatibility
promise. The concrete types are in [Contracts.cs](CI/Contracts.cs).

The collected outcome retains the plan's `comparison`, `candidate` and declared
`scope`, including when no checks are selected. Its success applies to that
subject and supplied coverage only.

| Input                 | Required content and producer responsibility                                                                                                                                |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Planning request      | `basis`, `candidate`, `changedPaths`, `full`; the caller resolves the event's actual comparison/tested revisions and includes both rename paths and deleted paths           |
| Each revision's facts | `revision`, `scope`, `projects`, `sharedInputs`, `unaffectedPaths`, `errors`; native readers establish completeness for this declared scope                                 |
| Project               | Stable `id`, repository-relative `directory`, build `dependencies`, distinct `qualityConsumers`, nullable `releaseUnit` and resolved `qualityPreset`, and concrete `checks` |
| Shared input          | Exact repository-relative `path` and project `consumers`, including actual version/configuration/lock inputs                                                                |
| Unaffected path       | Exact path the producer established has no consumers in this scope; no wildcard or unknown-path fallback                                                                    |
| Check                 | `key` (`target`, `check`, `variant`), actual `dimensions`, `runner`, `required`, and prerequisite keys; variant names identify resolved dimension combinations              |
| Result array          | Each entry carries the exact `candidate`, check `key` and `status`: `Passed`, `Failed`, `Cancelled`, `TimedOut` or `Skipped`                                                |

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

Identical check keys deduplicate only when runner, dimensions and prerequisites
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
