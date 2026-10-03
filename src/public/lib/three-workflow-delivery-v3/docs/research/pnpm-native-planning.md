# PNPM Native Planning Interfaces

## Scope and Premise

This source assessment supports the replacement application's
[native fact integration](../migration-strategy.md#native-facts-and-first-supported-shapes).
It compares interfaces at the repository's pinned PNPM 12.8.1. It does not audit
the former V3 implementation or assert runtime coverage. The
[implementation plan](../migration-strategy.md#native-facts-and-first-supported-shapes)
records the owner's CLI selection. Maintainers and integration reviewers use this assessment to avoid choosing
an incomplete API or introducing a second PNPM resolver.

The owner confirmed that valid, synchronized repository locks are an existing
premise. The [MLD](../middle-level-design.md#minimum-facts) owns that analysis
contract. Missing-target diagnostics in a manifest-only graph are consequently
not a reason to create another lock-validation gate. Revision selection and
installed-state selection remain different concerns.

## Immutable Source Basis

All source findings below use PNPM release `v12.8.1`, commit
`57e7b9b76ac3a5ef67461093071b2264335a1a01`.

The release [README][release-readme] identifies the v12 CLI as Rust and the
`pnpm11/` TypeScript line as maintained for bug fixes. Findings about a v11
JavaScript project-graph package cannot establish v12 CLI behavior. In particular,
the v12 [workspace graph implementation][workspace-graph] handles `link:` paths
and npm aliases; this differs from the earlier v11 interface assessment.

The public [npm version metadata][napi-metadata] inspected on 2026-10-03 identifies
`@pnpm/napi@12.8.1`, its [distribution archive][napi-archive] and optional platform
packages at the same version. Registry integrity is
`sha512-T1TczlkTX3Cd6Vj3AhHWGiIJfrUjgkmPwCSoYXzvd4Yu/ngFIJ1XRlCQWrjZDDZSr+myZ8chRkQ31mUTah94VA==`.
This establishes published availability, not successful installation or execution
on any platform. No binding was installed or exercised for this assessment.

The [binding README][napi-readme] describes the package as the Rust engine's
programmatic API, with Bit as a reference consumer. It explicitly exposes native
lockfile operations to avoid a second implementation of the engine-owned format.
It also retains ordinary JavaScript packages for type definitions and small pure
helpers. The Rust rewrite does not make every maintained JavaScript helper an
inappropriate dependency.

## Source Findings

| Interface                                 | Useful contract                                                                                                            | Limit for Workflow analysis                                                                                          |
| ----------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- |
| `readLockfile({dir, kind: 'wanted'})`     | Reads the committed lock with the Rust parser; returns the file shape, including importers, package metadata and snapshots | Parsing is not workspace discovery or a resolved project graph                                                       |
| `filterLockfileByImporters`               | Native package-snapshot closure for selected importers and dependency groups                                               | Other importer entries remain unchanged; `link:` edges end that package-graph walk                                   |
| `getDependents`                           | Native reverse trees used by `pnpm why`                                                                                    | Prefers the current installed lock; workspace leaf nodes have name/version/field but no directory identity           |
| `readConfig`                              | Native configuration cascade                                                                                               | Published projection has no workspace-membership patterns and includes credentials; never serialize the whole result |
| `parseBareSpecifier`                      | Splits alias and bare specifier                                                                                            | The pinned implementation leaves `fetchSpec` and `type` unset; it is not a local-path resolver                       |
| Recursive `list` with JSON and depth `-1` | Native workspace discovery and project directory identities                                                                | Provides membership, not dependency edges                                                                            |
| `list --lockfile-only --json`             | Wanted-lock dependency listing with native paths and dependency groups                                                     | JSON is a presentation of the graph; it does not expose every source-identity field                                  |
| `list --only-projects`                    | Follows linked importer nodes and recognizes publish directories                                                           | Its project-edge filter excludes package-snapshot nodes, including local directory dependencies represented that way |

The [typed binding contract][napi-types] defines the public inputs and outputs.
The following implementation passages establish the material details:

- [Lockfile binding][napi-lockfile] reads the wanted file explicitly and returns
  the engine's file-shaped JSON. [Native filtering][lock-filter] narrows package
  snapshots and metadata, carries other importers through, and stops on links.
- [Dependents binding][napi-dependents] calls `LoadedState::load(..., false)`;
  [loaded state][loaded-state] interprets that flag as current-lock preference
  with wanted-lock fallback. The binding exposes no wanted-only option.
- [Specifier binding][napi-specifier] returns alias/bare-specifier fields with
  no resolved path or type. Field names in the declaration alone do not imply
  those facts are populated.
- [Recursive listing][list-recursive] discovers projects before listing them.
  [Listing][list-command] propagates `--lockfile-only` and implements depth `-1`
  without walking dependencies. [Structured output][list-json] supplies each
  root's path and each dependency's native path.
- [Inspection graph][inspection-graph] treats a resolved package key as a package
  node; `only_projects` keeps importer targets instead. [Package information][pkg-info]
  locates package nodes in the virtual store and omits directory resolution from
  the `resolved` field. Such a path is not the source directory of an injected
  workspace dependency. The native lock's [directory resolution][resolution]
  preserves the source directory separately.

The [workspace][workspace-crate] and [workspace-graph][graph-crate] Rust crates are
marked `publish = false` at this release. Their internal public Rust functions
are source evidence, not an established published embedding contract.

## Candidate Composition and Tradeoffs

The owner selected the supported PNPM CLI for the C# application's Node
integration after considering the external helper and embedded runtime. Calling the CLI satisfies the same native
ownership boundary as calling an ecosystem library. There is no general preference
for libraries when the host cannot use them directly.

The [binding loader][napi-loader] runs in Node.js and loads a platform-specific
native addon. An external Node helper would add a runtime requirement, package
preparation and addon distribution while retaining a child process and
structured-result transfer. That external-helper arrangement provides no
in-process library benefit to the C# caller. The CLI is already a required PNPM
tool. A helper is justified only by a concrete required capability or a
demonstrated reduction in integration code that outweighs those dependencies;
neither has been established by this assessment. The embedded arrangement below
has different benefits and costs.

This is an architectural recommendation with moderate confidence, not an accepted
complete extraction recipe. The NAPI binding alone currently lacks exposed
workspace discovery. The `list` JSON view can hide local directory source
identities behind virtual-store paths; that limitation does not establish that
every supported CLI interface lacks the required facts. Assess the actual CLI
commands and output contracts before adding a binding to compensate. Maintained
PNPM helpers remain candidates for specific capabilities, and Workflow must not
reimplement specifier grammar, semver matching or dependency resolution.

Before implementing an integration, specify which native result supplies each
required fact and verify the identity joins. Use meaningful integration cases
with valid PNPM-produced locks: ordinary workspace links, aliases, `link:` and
`file:` directory inputs, injected workspace instances, peer variants, publish
directories, cycles, and changed/deleted relations across two revisions. Include
an installed state from another revision to check wanted-lock selection; that
case does not question validity of the committed lock. Test application adaptation
and impact results rather than independently proving PNPM's resolver.

## Embedded Node Assessment

The assessed `Microsoft.JavaScript.NodeApi` arrangement is distinct from an
external Node helper. The owner subsequently selected CLI integration and did
not select this arrangement. Microsoft's
[README][nodeapi-readme] explicitly supports loading JavaScript packages and
calling their APIs in a .NET process. Source inspection at
`microsoft/node-api-dotnet@958b8c3c04dff5414781a88f069062c6528f7427` establishes
the following contracts. Public NuGet package `Microsoft.JavaScript.NodeApi`
`0.9.27` identifies that same source commit in its
[package metadata][nodeapi-package]. No embedded runtime or PNPM addon was
executed for this assessment.

- [Embedding guidance][nodeapi-embedding] describes loading `libnode`, importing
  installed packages and running their APIs on the JavaScript thread. This can
  remove the helper process and stdout/JSON transport; C# still adapts JavaScript
  values into application facts. The
  [thread-runtime implementation][nodeapi-thread-runtime] supplies CommonJS and
  asynchronous module imports. Loading this exact PNPM native addon and its
  promises remains an integration assumption, not an observed result.
- The README marks the project Public Preview; the embedding guide separately
  marks embedding experimental. Node-API addon compatibility does not establish
  embedding-ABI compatibility. The guide requires shared `libnode` with its
  embedding API and matching package versions; an ordinary installed `node`
  executable is not that shared library. The project's
  [package pins][nodeapi-package-pins] select `Microsoft.JavaScript.LibNode`
  `20.1800.203`. The newer public
  [LibNode `20.1800.215` package][libnode-package] still identifies the Node 20
  line and distributes platform-specific packages. Neither newer package
  compatibility nor availability of a suitable Node 24 embedding binary has
  been established here. The repository already pins Node 24 in `mise.toml`.
- The guide's referenced [Node.js embedding PR][node-embedding-pr] was inspected
  on 2026-10-03: closed and unmerged. That mutable status is not evidence that
  every other embedding route is unavailable. The selected Microsoft source
  still imports its own embedding API; its broad Node-API compatibility statement
  must not be extended to arbitrary `libnode` builds.
- [Platform lifetime][nodeapi-platform] permits one platform per process and
  prevents reinitialization after disposal. Multiple environments are supported,
  but values have thread affinity and reference lifetimes. Embedding therefore
  adds runtime lifecycle and value-marshalling responsibilities and shares native
  process failure with the C# host. No startup, throughput or memory advantage
  has been measured for this short-lived planner.

The proposed workspace-discovery branch also needs a concrete PNPM interface
selection. Public `@pnpm/workspace.find-packages@1000.0.65` is available; its
[published archive][workspace-find-archive], with integrity
`sha512-76n06rXBgK9FaLuexbvah0cQzLxKCK/5SwwCJ1i9xLDV7anZTPkSiJQpGTmUlDr9hbQPXOG1nSorV031hjG7QA==`,
exposes discovery with caller-supplied `patterns` (`lib/index.js` and
`lib/index.d.ts`). It does not itself read workspace membership configuration.
At the pinned PNPM 12 release, the maintained JavaScript
[workspace reader][workspace-reader] is instead
`@pnpm/workspace.projects-reader@1101.1.1`; its
[package contract][workspace-reader-package] requires Node `>=22.13`, also
declared in the public [version metadata][workspace-reader-metadata]. The observed
Node 20 LibNode packages do not satisfy that engine declaration. Selecting an
older helper, obtaining a newer embedding runtime, or changing the discovery
interface would require a separate, explicit pairing assessment; do not ignore
the declared engine requirement. Workspace configuration must still come from
a PNPM-owned reader rather than application YAML/glob interpretation.

Its source-backed benefit is real in-process access; a usable runtime/package
pairing and sufficient fact coverage were not established. The owner chose the
CLI to avoid relying on embedded LibNode. The binding's install, network
resolution and peer APIs do not require the planner to reinstall or re-resolve
an already valid lock. Changing the calling mechanism does not remove the PNPM
interface limits listed above or supply missing project edges automatically.

## CLI Fact Extraction Candidate

The CLI can supply more than a rendered installed dependency tree. At the pinned
release, [recursive selection][recursive-selection] builds PNPM's own project
graph and applies directory/dependency selectors before rendering the selected
projects. The [project view][project-graph-view] includes peer, development,
optional and production dependency groups. This is a native project graph,
not a graph reconstructed by Workflow from dependency specifiers.

The concrete candidate is:

1. Run `pnpm list --recursive --include-workspace-root --lockfile-only --json
--depth -1` in the selected revision's workspace to discover source projects.
2. For each discovered directory, run the same command with its native directory
   selector and dependency expansion, such as `--filter '{./packages/app}...'`.
   The returned project paths supply PNPM's dependency closure, including the
   initiating project. Workflow removes that project from its adapted relation
   set and preserves every other returned project identity. Do not infer an edge
   by matching package names or parsing `workspace:`, `file:`, aliases or semver.
3. Retain the two revisions' relations separately and combine them under the MLD
   impact algorithm. A native transitive closure can be consumed as dependency
   reachability; it must not be described as an exported direct-edge graph.
4. Use `list --only-projects --lockfile-only --json` as a second native view only
   when validation identifies a concrete need. Its package-snapshot omission is
   still relevant; it is not a complete graph merely because it returns JSON.

Source inspection supports this candidate, not complete coverage. In particular,
the [graph's directory lookup][workspace-graph] matches workspace project roots;
it does not automatically map every local dependency on a nested publish
directory to its owning source project. The linked-project listing has a
[separate publish-directory mapping][list-linked-projects]. Verify the actual
command composition before implementing or asserting supported coverage. Native
directory-selector glob/legacy behavior must not accidentally select a subtree
when the caller intended one project. The application owns its selectors and
returned identity checks, while PNPM owns interpreting them.

The reader's narrow layout queries and explicit selector setting also have source
support at the pinned commit. [`config get --json`](https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/config/values.rs#L261)
renders an unset key as JSON null. The
[CLI override parser](https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/config_overrides.rs)
accepts `--config.legacy-dir-filtering=false`; configuration
[loading](https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/config/src/loading.rs#L242)
applies the generic CLI setting values over workspace settings. This is source
evidence for the invocation, not a new runtime observation. The retained native
observations below did not execute these layout queries or that explicit override.

The global full listing is a presentation tree, not independently complete for
every initiating consumer. PNPM
[shares a materialization cache](https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/deps-inspection/src/build.rs#L73)
across selected projects and
[elides repeated subtrees](https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/deps-inspection/src/get_tree.rs#L294).
This can hide the second consumer of a nested non-workspace local input. Use an
exact singleton directory selector without ellipsis for each project's full
`Infinity` query; keep the separate native dependency-closure query. Within one
consumer's tree, the original expanded occurrence still supplies that consumer's
reachable inputs. The
[selector parser](https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/workspace-projects-filter/src/parse_project_selector.rs#L53)
and [exact glob matching](https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/workspace-projects-filter/src/glob.rs#L50)
support this composition. These are source findings and a corrective inference;
the retained observations did not execute singleton full queries or exercise
nonempty deduplicated subtrees.

## Bounded Local CLI Validation Protocol

This protocol supports the decision whether the candidate above is sufficient
and the later credential-free adapter tests. It is executable only after this
protocol is accepted through repository delivery under the contraction Wave.
It grants no external experiment, registry publication or hosted dispatch.

- **Subject and environment:** the repository-pinned PNPM 12.8.1 CLI on the
  active Linux host. Record the exact executable/tool versions, fixture inputs,
  commands, exit codes and JSON outputs. Windows feasibility remains the owner's
  accepted assumption; this protocol does not add a Windows experiment.
- **Isolation:** use at most two disposable workspace copies, independent of the
  repository checkout. Use empty fixture-local npmrc auth input and isolated
  store/state directories. Fixtures contain only local packages and no scripts,
  hooks or credentials. Do not modify the repository locks or installed state.
- **Preparation:** create a finite fixture graph with a transitive consumer and
  unrelated project; ordinary workspace links, workspace and npm aliases, local
  `link:` and `file:` directories, injected workspace instances, local peer
  inputs, cycles and publish directories. Generate valid synchronized locks
  with native `pnpm install --lockfile-only --offline --ignore-scripts`; use only
  local inputs. At most four preparation commands are permitted. A registry/cache
  dependency, invalid lock or unsupported fixture ends that case; do not repair
  it with a handwritten lock or online resolution.
- **Queries and expected observations:** at most 80 read-only list/filter/SBOM
  queries, each bounded to 30 seconds; preparation commands to 120 seconds.
  Observe native workspace identities and consumers for each required local
  input, keeping unrelated projects excluded. Compare two valid revisions with
  removed references or deleted projects. A matching empty selection is evidence
  only for that query and case. No current-directory or full-run fallback.
- **Installed-state case:** if a materialized state is needed to check wanted-lock
  selection, use at most one offline, script-disabled install from a valid base
  fixture before making a second valid revision. Subsequent queries still use
  wanted-lock mode. This is a state-selection case, not a new lock validity gate.
- **Stop and outcome:** stop dependent implementation if a required source
  identity/relation is omitted and no supported CLI composition exposes it.
  Record the exact counterexample, command/output and affected requirement;
  return the gap to the owner. Do not compensate with a custom resolver or a
  Node/LibNode integration. Tool crashes, malformed output, unexpected external
  effects or exhausted command/time bounds also stop the affected case.
- **Cleanup and limits:** retain sanitized inputs and observations in the
  governing work carrier, remove only generated disposable fixture state, and
  leave source caches available for review. No installation scripts, external
  registry reads, package publication, authentication, access/configuration or
  host-trust changes. Local success covers the tested CLI composition and
  fixture shapes; it does not certify PNPM or all monorepo structures.

## Local CLI Observations

The protocol accepted in [PR #1006](https://github.com/hcoona/three/pull/1006),
commit `057a7a0f420f0d9adbce236bd6c808a2ed783cd0`, was exercised with PNPM
12.8.1 on Linux x86_64 on 2026-10-03. Two disposable workspaces, four preparation
commands and 57 read-only queries were used. The fixture commands used no registry reads, scripts, hooks,
credentials or hosted operations. The governing observation PR retains
the sanitized fixtures, native-generated locks, runner, command outputs and
environment identity; these are local integration observations, not a PNPM
support certification or an accepted native reader.

The first workspace's optional `packageManager` directive caused an offline
package-manager metadata cache miss before any graph query. That case stopped.
The second workspace used the already pinned executable without that directive;
its base and candidate locks were generated natively. One script-disabled offline
install supplied base installed state before candidate manifests and the wanted
lock changed. The four-command preparation ceiling includes the stopped case.

| Fixture shape                                                                              | Observed native result                                                                                                                                       |
| ------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Ordinary workspace links, workspace/npm aliases, root-directory `link:` and `file:` inputs | Directory-selector dependency closure included the source projects                                                                                           |
| Transitive consumers, cycles, nested projects and a path containing a space                | Closure included the expected projects and excluded unrelated projects; the parent selector did not select its nested child with `legacyDirFiltering: false` |
| Removed reference and deleted project                                                      | Candidate closure dropped the old relation; the retained base closure still identified the old consumer                                                      |
| Candidate wanted lock with base installed state                                            | Wanted-lock queries returned candidate relations rather than substituting installed base relations                                                           |
| Workspace reference to a project with `publishConfig.directory`                            | Closure included the source project                                                                                                                          |
| Explicit `link:` reference to that publish subdirectory                                    | Filter closure omitted the source project; complementary `list --only-projects` returned its source-root path                                                |
| Explicit `file:` reference to that publish subdirectory                                    | Filter closure omitted the source project; `list --only-projects` omitted the package-snapshot edge                                                          |

The local-peer declaration appeared in filter closure. The fixture declaring
`dependenciesMeta.injected: true` also appeared there, but its generated lock
still represented the dependency as a link. This is not evidence for a
materialized injected snapshot or peer-variant coverage. Those shapes remain
unobserved; no additional preparation is permitted under this execution's budget.

For the nested `file:` case, full list JSON retained a package coordinate such as
`version: "file:packages/publisher/dist"` and a virtual-store installation path.
CycloneDX retained the same coordinate as a component version and package URL.
Neither queried view returned a separate source-directory field. This is not a
claim that the CLI exposes no useful information: the coordinate is visible.
However, a complete source join cannot be claimed from these observations or
by matching package names. Follow-up source inspection corrects the initial
version-overwrite concern: the pinned [lock writer][directory-version] explicitly
excludes directory resolutions from metadata `version`. For these directory
records, [package information][pkg-info] therefore renders the package-key
coordinate. This correction is source evidence, not another runtime observation.

The selected filter/list composition therefore does not yet establish complete
coverage for every required local-input shape. The owner's CLI choice remains
unchanged. The owner directed further CLI research without duplicate declarations
or unnecessary complexity. The candidate below uses the existing native
publish-directory configuration; this known relationship does not need a second
Workflow declaration. The MLD's extra-input contract remains for relationships
actually unavailable from native metadata.

The source assessment and local composition have not established an unavoidable
gap in every supported CLI interface.
If a required source identity or relation cannot be obtained through supported
native interfaces, follow the [HLD boundary](../high-level-design.md#native-integrations)
and return that concrete gap to the owner. Do not fill it with custom parsing or
weaken selective CI to a full-run fallback.

## Directory Coordinate Adaptation Candidate

Additional immutable source findings at the same PNPM release establish:

- The [local resolver][local-coordinate] normalizes copied directory coordinates
  relative to the lockfile directory, uses forward slashes, and can preserve an
  explicitly absolute input when configured. The
  [directory resolution writer][local-resolution] retains that same directory.
- The [lock writer][directory-version] omits metadata `version` for directory
  resolutions. [Package information][pkg-info] then uses the package-key version;
  [its formatter][coordinate-format] emits `file:<path>` without the peer suffix.
  The virtual-store installation path need not be decoded.
- Package information emits `resolved` for tarball resolutions and omits it for
  directory resolutions. Absence alone is not a directory type discriminator:
  other native resolution kinds also omit it.
- Native [`pkg get`][pkg-command] can read `publishConfig.directory` through
  PNPM's manifest reader. A non-recursive query in each discovered source
  directory preserves that directory's identity. The recursive report is keyed
  by package name and is unsuitable as a general directory join.

The revised candidate keeps the native filter reachability above, including its
peer dimensions, and complements it with full wanted-lock `list` JSON. Use
`--depth Infinity` for that complementary view so a resolved local input below
another package is not dropped. Obtain existing publish-directory configuration
with `pnpm --dir <discovered-directory> pkg get publishConfig.directory --json`.
An absent directory field produces no output; other publish settings do not add
an output-directory association.

For each discovered source directory and its declared publish directory, form
the pinned native coordinate encoding, such as
`file:packages/publisher/dist`, and compare the whole value with returned
`version` fields that have no `resolved` field. Native link paths can likewise be
compared with those known directories. Preserve exact source-directory identity,
reject ambiguous ownership and retain the initiating project's relation to each
matched producer. Combine these relations with native filter reachability and
the base/candidate impact algorithm. Do not use package names, installation-path
decoding, directory-prefix guesses or an unmatched field's prefix to infer a
producer. A local tarball is an archive input, not its same-named source project.

This is output adaptation: PNPM has already selected and resolved the dependency.
Workflow associates its returned identity with known project/output identities.
It does not parse manifest dependency specifiers, implement aliases or ranges,
read lock YAML, or resolve a missing dependency. Ordinary repository path handling
and encoding a known identity do not constitute a second dependency resolver.
The HLD's native ownership boundary remains in force. Unmatched relevant inputs
and ambiguous ownership still cannot justify exclusion.

The follow-up below observes these identity joins for the pinned CLI and local
fixture shapes. It supports this composition without a duplicate declaration;
it does not accept a native reader or arbitrary extraction coverage. Existing
observations and immutable source findings retain their separate evidence levels.

## Bounded Directory Join Follow-up Protocol

This is a distinct follow-up to the exhausted original preparation budget, not
permission to repeat or refill that execution. It becomes executable only when
accepted through repository delivery under the contraction Wave. Its consumer
is the decision whether the directory adaptation above avoids a second manually
maintained declaration while retaining meaningful impact selection.

- **Subject and environment:** PNPM 12.8.1 on the active Linux x86_64 host. Record
  executable identity/hash, environment, accepted protocol revision, exact
  commands, elapsed times, exit codes, fixture inputs and JSON output. Windows
  remains an accepted assumption and is not tested here.
- **Isolation and inputs:** at most two disposable workspace copies outside the
  checkout, starting from the retained local fixture inputs of PR #1007. At most
  32 local package manifests, including output/peer fixture manifests, and one
  locally assembled tarball. No scripts, hooks, credentials or registry inputs.
  Use empty fixture-local npmrc auth input and isolated store/state directories;
  leave repository locks and installed state unchanged.
- **Preparation:** at most three native preparation commands, each bounded to
  120 seconds: two script-disabled offline lock generations for base/candidate
  and, only if needed, one script-disabled offline base install. Use native
  `injectWorkspacePackages` configuration to seek actual directory snapshots;
  include two local peer-provider contexts, a publish-directory alias, a space
  in an output path and a local tarball. Change/remove a publish-directory
  relation in the candidate. Fixture file creation and tarball assembly use only
  the finite local inputs. A missing cache/registry input or invalid lock stops
  the affected case; do not repair it online or edit generated locks.
- **Queries and checks:** at most 60 read-only `list`/filter/`pkg get` queries,
  each bounded to 30 seconds. Compare native metadata and directory coordinates
  with known source/output identities, retaining unrelated projects as negative
  controls. Check actual injected/peer shapes from generated output rather than
  equating a manifest declaration with runtime coverage. Observe the composed
  producer/consumer relation in each revision and retention of removed base
  relations. Keep native source findings distinct from experiment observations;
  source inspection suffices for interfaces that clearly cannot answer the join.
- **Stop and limits:** unexpected external effects, malformed output, crashes,
  ambiguous joins or exhausted bounds stop the affected execution. A required
  relation still unavailable after the supported composition blocks dependent
  implementation and returns the concrete gap to the owner. No custom resolver,
  Node/LibNode helper, duplicate declaration or silent full-run fallback follows.
- **Evidence and cleanup:** retain sanitized inputs, native locks, runner,
  commands, outputs and comparison results in the governing PR before removing
  only generated disposable state. Leave source caches available for review.
  No external experiment, publication, hosted dispatch, authentication,
  access/configuration or host-trust changes. Conclusions cover only the pinned
  CLI composition and observed shapes; peer/injection cases that do not actually
  materialize remain unobserved rather than inferred successes.

## Directory Join Observations

The follow-up protocol was accepted in
[PR #1008](https://github.com/hcoona/three/pull/1008), commit
`73d47a43edf76749e78a8b92702e15d6d5555411`, and executed on 2026-10-03 with
the same PNPM 12.8.1 executable as the first observation set. Two isolated local
workspaces used 30 base manifests, one local tarball, two offline/script-disabled
lock generations and 38 read-only queries. No install, registry access, scripts,
hooks or credentials were needed. The delivery PR retains sanitized inputs,
native locks, runner, exact commands/outputs, environment/hash and comparisons;
generated state is removed only after durable retention.

The observation runner combines native filter reachability with full wanted-lock
list JSON and exact known-directory/coordinate matches. Four source directories
per revision are queried for publish metadata in this finite fixture; only the
publisher declares a directory. This is a local composition probe, not the C#
reader or a claim that every project's metadata was evaluated by that reader.

| Scenario                                                           | Observed result                                                                                                                                                                             |
| ------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `file:` dependency on a publish directory with a space in its path | Native `version` retains the directory coordinate; exact association selects the source publisher, which native filter closure alone omits                                                  |
| Publish-directory alias and explicit `link:` input                 | Both associate with the source publisher; no name or manifest-specifier join is needed                                                                                                      |
| Existing native publish metadata                                   | Direct `pkg get publishConfig.directory --json` returns `dist output` in base and `build output` in candidate; an access-only `publishConfig` yields no output                              |
| New injected peer library in two local provider contexts           | Native locks contain two directory snapshots; list returns two distinct installation identities with the same `file:packages/peer-lib` coordinate, both associating with the source library |
| Same-named local tarball                                           | List returns ordinary version `1.0.0` and `resolved: file:fixtures/publisher.tgz`; it is not associated with the source publisher                                                           |
| Transitive consumer and unrelated control                          | The composed base relation includes the publisher below a consumer that filter closure alone omits; the unrelated project has no local source relation                                      |
| Removed file relation and changed publish directory                | Candidate drops the removed relation, updated alias/link targets still associate with the producer, and the base/candidate union retains the old consumer relation                          |

All 18 recorded adaptation comparisons matched their expected values; there were
no ambiguous directory joins. This does not certify the native resolver or
complete input coverage. The two new peer-library snapshots are actual wanted-lock
observations, not installed-package qualification. Reused core entries remained
links despite injection declarations, so they do not demonstrate conversion of
an existing link into an injected snapshot. The pre-existing workspace publisher
link also retained its old directory spelling while native filter reachability
continued to identify the source publisher. Do not infer link regeneration or
installation success from these observations.

The resulting recommendation has high confidence for the observed shared-root
lock layout: implement native filter/list composition with existing publish
metadata and exact identity association. No second PNPM dependency/output
declaration, helper runtime or custom resolver is needed for these cases. The
remaining reader work is ordinary application integration, revision/root handling,
ownership ambiguity and unsupported relevant inputs. Dedicated lock layouts,
absolute-path preservation and other unobserved shapes must not inherit this
runtime coverage. The existing rule remains: unresolved required scope blocks
planning rather than broadening execution silently.

Reevaluate the selected CLI composition when the repository's PNPM pin changes
or concrete integration exposes a missing fact. Recheck embedded-runtime facts
only if the owner reopens that selection. The integration author checks the
public contracts and affected scenarios; the next native-reader implementation
or dependency-upgrade review is the fallback review point.

[release-readme]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/README.md
[workspace-graph]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/workspace-projects-graph/src/create_projects_graph.rs
[napi-metadata]: https://registry.npmjs.org/@pnpm/napi/12.8.1
[napi-archive]: https://registry.npmjs.org/@pnpm/napi/-/napi-12.8.1.tgz
[napi-readme]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/npm/napi/README.md
[napi-loader]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/npm/napi/index.js
[napi-types]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/npm/napi/index.d.ts
[napi-lockfile]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/napi/src/lockfile.rs
[lock-filter]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/lockfile/src/filter_by_importers.rs
[napi-dependents]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/napi/src/dependents.rs
[loaded-state]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/deps-inspection/src/build/loaded_state.rs
[napi-specifier]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/napi/src/specifier.rs
[list-recursive]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/list/recursive.rs
[list-command]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/list.rs
[list-json]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/list/render/structured.rs
[inspection-graph]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/deps-inspection/src/graph.rs
[pkg-info]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/deps-inspection/src/pkg_info.rs
[resolution]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/lockfile/src/resolution.rs
[workspace-crate]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/workspace/Cargo.toml
[graph-crate]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/workspace-projects-graph/Cargo.toml
[nodeapi-readme]: https://github.com/microsoft/node-api-dotnet/blob/958b8c3c04dff5414781a88f069062c6528f7427/README.md
[nodeapi-embedding]: https://github.com/microsoft/node-api-dotnet/blob/958b8c3c04dff5414781a88f069062c6528f7427/docs/scenarios/dotnet-js.md
[nodeapi-thread-runtime]: https://github.com/microsoft/node-api-dotnet/blob/958b8c3c04dff5414781a88f069062c6528f7427/src/NodeApi/Runtime/NodeEmbeddingThreadRuntime.cs
[nodeapi-platform]: https://github.com/microsoft/node-api-dotnet/blob/958b8c3c04dff5414781a88f069062c6528f7427/src/NodeApi/Runtime/NodeEmbeddingPlatform.cs
[nodeapi-package-pins]: https://github.com/microsoft/node-api-dotnet/blob/958b8c3c04dff5414781a88f069062c6528f7427/Directory.Packages.props
[nodeapi-package]: https://api.nuget.org/v3-flatcontainer/microsoft.javascript.nodeapi/0.9.27/microsoft.javascript.nodeapi.0.9.27.nupkg
[libnode-package]: https://api.nuget.org/v3-flatcontainer/microsoft.javascript.libnode/20.1800.215/microsoft.javascript.libnode.20.1800.215.nupkg
[node-embedding-pr]: https://github.com/nodejs/node/pull/54660
[workspace-find-archive]: https://registry.npmjs.org/@pnpm/workspace.find-packages/-/workspace.find-packages-1000.0.65.tgz
[workspace-reader]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm11/workspace/projects-reader/src/index.ts
[workspace-reader-package]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm11/workspace/projects-reader/package.json
[workspace-reader-metadata]: https://registry.npmjs.org/@pnpm/workspace.projects-reader/1101.1.1
[recursive-selection]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/recursive.rs
[project-graph-view]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/workspace/src/projects_graph_view.rs
[list-linked-projects]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/list/linked_projects.rs
[directory-version]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/package-manager/src/dependencies_graph_to_lockfile/packages.rs#L215-L236
[local-coordinate]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/resolving-local-resolver/src/parse_bare_specifier.rs#L123-L161
[local-resolution]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/resolving-local-resolver/src/local_resolver.rs#L216-L269
[coordinate-format]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/lockfile/src/pkg_ver_peer.rs#L7-L40
[pkg-command]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/pkg.rs#L162-L238
