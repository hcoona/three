# PNPM Native Planning Interfaces

## Scope and Premise

This assessment supplies source evidence for the application's
[native fact integration](../design/repository-and-selection.md#native-fact-integration).
The [Node integration contract](../integration/node.md#native-pnpm-facts)
owns the selected CLI integration; this record does not select a second resolver,
helper runtime or dependency/output declaration.

Valid synchronized repository locks are an existing analysis premise. Reading
the selected revision's wanted lock is distinct from reading installed state;
neither requires another lock-validation gate.

Original source findings below use PNPM 12.8.1, commit
`57e7b9b76ac3a5ef67461093071b2264335a1a01`. The
[historical assessment][historical-assessment] retains the unselected NAPI,
external-helper and embedded-Node alternatives, original package/source
identities, finite CLI protocols, results and version-specific rechecks.
The Rust v12 CLI and maintained v11 JavaScript packages are distinct source
bases; the latter do not establish v12 CLI behavior. No alternative is a current
fallback, and historical execution budgets remain exhausted.

## Native CLI Source Findings

| Interface                                 | Useful contract                                             | Limit for Workflow analysis                                         |
| ----------------------------------------- | ----------------------------------------------------------- | ------------------------------------------------------------------- |
| Recursive `list` with JSON and depth `-1` | Native workspace discovery and project directory identities | Membership, not dependency edges                                    |
| Native directory/dependency selector      | PNPM's project reachability                                 | A transitive closure, not an exported direct-edge graph             |
| `list --lockfile-only --json`             | Wanted-lock dependencies, paths and groups                  | Presentation output does not expose every source-identity field     |
| `list --only-projects`                    | Linked importer and publish-directory association           | Omits package-snapshot edges, including some local directory inputs |

[Recursive listing][list-recursive] discovers projects before listing them;
[the command][list-command] propagates `--lockfile-only` and implements depth
`-1` without dependency traversal. [Structured output][list-json] supplies each
root's path and each dependency's native path. The
[inspection graph][inspection-graph] distinguishes package-snapshot nodes from
importer targets. [Package information][pkg-info] can expose a virtual-store
installation path rather than an injected workspace source directory; the
native [directory resolution][resolution] retains that source separately.

## CLI Fact Extraction

[Recursive selection][recursive-selection] builds PNPM's project graph and
applies directory/dependency selectors before rendering. Its
[project view][project-graph-view] includes peer, development, optional and
production groups. The source-supported composition is:

1. Discover source projects with `pnpm list --recursive --include-workspace-root
--lockfile-only --json --depth -1`.
2. Query each discovered directory with the same native command and an exact
   dependency selector such as `--filter '{./packages/app}...'`. Remove only
   the initiating project from the adapted relation set; preserve every other
   returned project identity. Do not decode specifiers, aliases or semver.
3. Retain each revision's reachability separately and combine them under the
   MLD impact algorithm. Native transitive closure is not a direct-edge graph.
4. Complement this with singleton full wanted-lock queries and the
   [directory association](#directory-coordinate-adaptation-candidate) below;
   neither filter closure nor `--only-projects` alone establishes completeness.

The [graph's directory lookup][workspace-graph] matches project roots, not
every nested publish directory. The [linked-project view][list-linked-projects]
has its own publish-directory association. Workflow owns exact selectors and
returned identity checks; PNPM owns their interpretation. Native selector
glob/legacy behavior must not turn one intended directory into a subtree.

The narrow layout queries and explicit selector setting have pinned source
support. [`config get --json`][config-values] renders an unset key as JSON null;
the [override parser][config-overrides] accepts
`--config.legacy-dir-filtering=false`, and [configuration loading][config-loading]
applies CLI values over workspace settings. These are source findings; the old
12.8.1 observations did not exercise those queries or that explicit override.

The global full listing is not independently complete for every consumer:
PNPM [shares a materialization cache][materialization-cache] and
[elides repeated subtrees][subtree-elision]. Use an exact singleton directory
selector without ellipsis for each project's full `Infinity` query, separately
from the dependency closure. Within one consumer's tree, the expanded occurrence
supplies its reachable inputs. The [selector parser][selector-parser] and
[exact glob matching][selector-glob] support this composition. The original
observations did not exercise singleton full queries or nonempty deduplicated
subtrees.

Wanted-lock selection does not make every output group revision-bound.
The [builder][unsaved-builder] also scans installed modules for extraneous
packages; the [renderer][unsaved-renderer] emits `unsavedDependencies`.
Ignore that entire group when reading saved facts; retain production,
development and optional dependencies. A synchronized lock does not imply an
empty or synchronized installation.

`Infinity` has a native safety limit. The [inspection limit][inspection-limit]
and [materializer][materializer-limit] permit level 256 but omit its children
without a truncation flag. Reject that indistinguishable boundary. Two JSON
containers per dependency level inside the array/project pair make a maximum
JSON depth of 512 accept complete level 255 and reject level 256. These are
pinned source findings and adapter inferences, not unbounded graph support.
The [pin guard](#pin-guard) exercises this boundary at the locked PNPM.

## Directory Coordinate Adaptation Candidate

The source basis for the selected association is:

- The [local resolver][local-coordinate] normalizes copied directory coordinates
  relative to the lockfile directory, uses forward slashes and can preserve an
  explicitly absolute input. The [resolution writer][local-resolution] retains
  the same directory.
- The [lock writer][directory-version] omits metadata `version` for directory
  resolutions. [Package information][pkg-info] uses the package-key version;
  [formatting][coordinate-format] emits `file:<path>` without the peer suffix.
- Package information emits `resolved` for tarballs and omits it for directories.
  Other resolution kinds also omit it, so absence alone does not prove directory
  identity.
- Native [`pkg get`][pkg-command] reads `publishConfig.directory`. Query each
  discovered source directory non-recursively; recursive name-keyed results do
  not preserve a general directory identity.

Complement native filter reachability with singleton full wanted-lock list JSON.
Read existing publish metadata with
`pnpm --dir <discovered-directory> pkg get publishConfig.directory --json`.
An absent field returns no output; other publish settings add no output-directory
association.

For each known source/publish directory, form its pinned native coordinate
encoding, for example `file:packages/publisher/dist`, and compare the entire
value with returned `version` fields without `resolved`. Compare native link
paths with those same known directories. Preserve exact source identity,
initiating consumer and both revisions' relations; reject ambiguity.
Do not infer producers from package names, installation paths, prefixes or
unmatched coordinate spelling. A local tarball is an archive input, not its
same-named source project.

PNPM has already selected and resolved the dependency; this is output identity
association, not manifest/lock parsing, semver evaluation or resolution.
Unmatched necessary inputs and ambiguous ownership still block planning.
The original [directory-join observation][directory-observation] supports only
its tested shared-root lock shapes: actual wanted-lock peer snapshots are not
installed-package qualification, and reused links do not establish injection
conversion or regeneration. Dedicated layouts, absolute-path preservation and
other unobserved shapes do not inherit that coverage.

## Native Script Metadata

At the original pin, [`pkg.rs`, lines 175–236][pkg-command] reads native manifests
and selects fields: a single field with `--json` returns its value, or no output
when absent. Recursive results are keyed by package name and do not supply a
general directory join. These are source findings, not runtime observations.
The same file at PNPM 12.8.2 commit
`b952ea2c7dfa0c50dbddfeadad6f57e4a86a7fce` was byte-identical, SHA-256
`0555e7feb6067d7425a6fc40763806168597e9b41fc4e580b2424c63dd80a6be`.

Use `pnpm --dir <already-discovered-directory> pkg get scripts --json` for
registered native check presence. Script text remains opaque; execution uses
`pnpm run`. Existing quality declarations select registered contracts, without
shell dependency parsing, a duplicate script declaration or YAML command language.

## Historical Qualification and Source Rechecks

The [original CLI observations][cli-observation], [directory-join follow-up][directory-observation]
and [actual C# reader/script observation][reader-observation] remain bound to
their separate 12.8.1/12.8.2 subjects. The actual reader used copied production
source with unchanged native locks; its 169 queries included four separate
script queries, not script execution inside the graph reader. The complete
protocols retain original inputs, commands, failures, hashes, effects, cleanup,
independent reviews and spent ceilings. The record snapshot is not their actual
producer. These observations do not qualify current tools, complete ownership,
caller integration, Windows, publication or new repetitions.

The [12.8.2][recheck-1282], [12.9.0][recheck-1290] and
[12.11.2][recheck-12112] assessments retain exact source commits, retrieved
inventories, comparisons and original PR carriers. They are bounded source
findings and integration inferences, not complete upstream equivalence.
The 12.11.2 local fixture runs lacked accepted pre-execution protocol and remain
unadmitted corroboration; source conclusions do not depend on admitting them.

Two continuing source limits matter to current consumers:

- At 12.9.0, an ordinary failure of implicit pre-run installation could warn and
  still run scripts. Its success therefore did not prove dependency freshness.
  The adopted executor disables that gate; explicit frozen installation remains
  a separately checked prerequisite.
- At 12.11.2, an `updateConfig` hook could narrow an unfiltered membership query.
  Current layout guards reject that unsupported configuration, rather than
  treating the old repository's absence of hooks as a permanent guarantee.

The PNPM pin recheck trigger is retired in favor of the native guards below.
Reopening an unselected embedded-runtime arrangement requires its own source
assessment; the retained alternatives do not qualify it.

## Current Layout Limits

`PnpmGraphReader` reads `pnpmfile`, `config-dependencies`, `node-linker` and
`global-pnpmfile` through native config queries. Planning requires the hook and
configuration-dependency settings to be unset, and the linker unset or
`isolated`; `PnpmGraphReaderTests` covers each rejection.

A default pnpmfile can load while `config get pnpmfile` is null.
`NodeRepositoryReader` therefore rejects `.npmrc` or hook files at the workspace
root, even if PNPM reports no other member, or at a member ancestor.
The PNPM 12.14.0 [finder][hook-finder] probes `.pnpmfile.mjs` and
`.pnpmfile.cjs`; the retained undotted `pnpmfile.cjs` is also rejected although
12.14.0 does not load it. `NodeRepositoryReaderTests` covers each name.
`PnpmGraphContractTests.EveryDefaultHookFilePnpmLoadsIsRejectedByTheReader`
runs the pinned PNPM with narrowing hooks and requires every loaded name to be
rejected. Adopting a hook or another linker returns to the owner as a reader
change, not a merge-time recheck.

## Pin Guard

The PNPM and Node pins are guarded by contract tests in the
`PnpmNativeContract` category. The Linux native-preparation job runs them under
`mise exec --locked dotnet node pnpm` whenever `mise.toml`, `mise.lock`,
`ci.yml`, the root `package.json`, `pnpm-workspace.yaml`, `pnpm-lock.yaml`, an
`.npmrc` or hook file, or the managed workflow-delivery sources and fixtures change.
Every PNPM command runs with a constructed environment: no inherited npm or PNPM
configuration, a fresh home and a closed proxy and registry. It must exit on its
own. PNPM 12.14.0 still writes zero-byte operation-lock files under
`/tmp/pnpm-store-operation-locks-<uid>` even when `TMPDIR` is set; they hold no
inputs or results and stay on the ephemeral runner.

`PnpmGraphContractTests` runs the application's `PnpmGraphReader` and
`PnpmScriptReader` on the committed `fixtures/pnpm-contract` workspace. Its lock
was generated by the pinned PNPM with `install --lockfile-only --offline --ignore-scripts`.
The workspace has a link, a workspace alias, optional and dev edges, a transitive
consumer, a cycle, a nested member, an injected peer, `file:` and `link:` inputs to a
publish directory with a space in its path, a local tarball, a non-workspace
directory, a `link:` input to a directory outside every member and an unrelated
member. The tests assert:

- the exact projection the planner consumes: every member, name, dependency set,
  publish directory and local input with its kind and consumer;
- the exact directory-selector closure, including that a nested member is not
  selected by its parent, both by default and with
  `PNPM_CONFIG_LEGACY_DIR_FILTERING=true`: the reader's
  `--config.legacy-dir-filtering=false` override keeps the closure and the whole
  projection unchanged, and the projection fails without it;
- the script metadata object and its absence;
- that the committed settings plan, while a dedicated lock, a configured
  pnpmfile, a configuration dependency, the hoisted linker and a global pnpmfile
  each stop planning;
- which default hook files the pinned PNPM loads;
- that after an offline frozen install, a manifest edge removal written only to
  the wanted lock is what the reader returns, while the installed tree still holds
  the removed relation, so planning never reads installed state;
- the depth boundary, with a generated single-member `file:` chain: at 255 levels
  the reader keeps every input including the deepest, and at 256 levels, where
  PNPM stops without a truncation marker, the reader fails with a JSON depth error.

NAPI and embedded-runtime findings stay unselected. Hoisted or global virtual
stores, dedicated lock layouts and multiple peer contexts are rejected or
unobserved shapes. Windows remains the owner's assumption.

## Native Execution Evidence and Pin Guard

The [closed Node execution observation](https://github.com/hcoona/three/blob/e90543cf4b9c5b069053221f7b9d38ee6035ea11/src/private/app/workflow-delivery/docs/research/node-ci-native-execution.md)
qualifies the adopted npm fixture's five-check recipe in two executions of one
same-candidate plan on its recorded Linux/tool/lock context. It does not qualify
other presets, hosted event/transfer/caller integration, whole-repository
coverage or Windows. The owner's [Windows assumption](../high-level-design.md#design-assumptions)
is separate. The original protocol, source/caller identities, complete archive,
failed initial generation, effects limits and independent reviews remain in
that historical record and its original PR carriers. Its finite allowance and
unused reserve are stopped; no execution, cleanup or publication follows.

The observation stays bound to Node 24.21.0, PNPM 12.8.2 and npm 11.19.0.
`NpmPackContractTests` guards the consumed execution facts at the locked Node
and PNPM in the Linux native-preparation job, with the selection and isolation
above. Tests use the production command builders on the committed
`fixtures/npm-contract` workspace and assert:

- the Node and PNPM version readback used by preparation;
- the runtime reader's acceptance of the real
  `mise ls --json --current --locked` selection, with locked Node as packaging
  variant and locked PNPM as plan dimension, and rejection for another root;
- build/test scripts execute with
  `--config.verify-deps-before-run=false` without PNPM's implicit install;
- `pack --out` writes exactly its requested path after successful prepack,
  writes nothing after failed prepack, injects the root license and retains
  the payload required by `NodePackageArchive`;
- multi-field `pkg get --json` returns flat dotted keys for the recipe's
  `engines.runtime` rejection and Release's `publishConfig.directory` read;
- isolated offline installation imports the original archive, while a missing
  archive or skipped install fails.

`RepositoryPinContractsTests.LockedNodeRuntimeHasItsPackagingMatrixEntry` keeps
the locked Node major in the `node-tests` matrix. npm is bundled with the Node
selected by the dispatch-only Buddy workflow rather than separately pinned.
These checks do not establish npm publication, registry authentication or
GitHub Packages behavior beyond the separately recorded runs.

[historical-assessment]: https://github.com/hcoona/three/blob/938bb34c458da1ce5c94e93fa4f4d3f9ade2e058/src/private/app/workflow-delivery/docs/research/pnpm-native-planning.md
[cli-observation]: https://github.com/hcoona/three/blob/938bb34c458da1ce5c94e93fa4f4d3f9ade2e058/src/private/app/workflow-delivery/docs/research/pnpm-native-planning.md#local-cli-observations
[directory-observation]: https://github.com/hcoona/three/blob/938bb34c458da1ce5c94e93fa4f4d3f9ade2e058/src/private/app/workflow-delivery/docs/research/pnpm-native-planning.md#directory-join-observations
[reader-observation]: https://github.com/hcoona/three/blob/938bb34c458da1ce5c94e93fa4f4d3f9ade2e058/src/private/app/workflow-delivery/docs/research/pnpm-native-planning.md#actual-reader-and-script-metadata-observation
[recheck-1282]: https://github.com/hcoona/three/blob/938bb34c458da1ce5c94e93fa4f4d3f9ade2e058/src/private/app/workflow-delivery/docs/research/pnpm-native-planning.md#pnpm-1282-pin-recheck
[recheck-1290]: https://github.com/hcoona/three/blob/938bb34c458da1ce5c94e93fa4f4d3f9ade2e058/src/private/app/workflow-delivery/docs/research/pnpm-native-planning.md#pnpm-1290-pin-recheck
[recheck-12112]: https://github.com/hcoona/three/blob/938bb34c458da1ce5c94e93fa4f4d3f9ade2e058/src/private/app/workflow-delivery/docs/research/pnpm-native-planning.md#pnpm-12112-pin-recheck
[workspace-graph]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/workspace-projects-graph/src/create_projects_graph.rs
[list-recursive]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/list/recursive.rs
[list-command]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/list.rs
[list-json]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/list/render/structured.rs
[inspection-graph]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/deps-inspection/src/graph.rs
[pkg-info]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/deps-inspection/src/pkg_info.rs
[resolution]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/lockfile/src/resolution.rs
[recursive-selection]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/recursive.rs
[project-graph-view]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/workspace/src/projects_graph_view.rs
[list-linked-projects]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/list/linked_projects.rs
[config-values]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/config/values.rs#L261
[config-overrides]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/config_overrides.rs
[config-loading]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/config/src/loading.rs#L242
[materialization-cache]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/deps-inspection/src/build.rs#L73
[subtree-elision]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/deps-inspection/src/get_tree.rs#L294
[selector-parser]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/workspace-projects-filter/src/parse_project_selector.rs#L53
[selector-glob]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/workspace-projects-filter/src/glob.rs#L50
[unsaved-builder]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/deps-inspection/src/build.rs#L139
[unsaved-renderer]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/list/render/structured.rs#L134
[inspection-limit]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/deps-inspection/src/lib.rs#L34
[materializer-limit]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/deps-inspection/src/get_tree.rs#L115
[directory-version]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/package-manager/src/dependencies_graph_to_lockfile/packages.rs#L215-L236
[local-coordinate]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/resolving-local-resolver/src/parse_bare_specifier.rs#L123-L161
[local-resolution]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/resolving-local-resolver/src/local_resolver.rs#L216-L269
[coordinate-format]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/lockfile/src/pkg_ver_peer.rs#L7-L40
[pkg-command]: https://github.com/pnpm/pnpm/blob/57e7b9b76ac3a5ef67461093071b2264335a1a01/pnpm/crates/cli/src/cli_args/pkg.rs#L162-L238
[hook-finder]: https://github.com/pnpm/pnpm/blob/fe4a0c232384289de82be3d502896ca7e070050c/pnpm/crates/hooks/src/finder.rs#L17-L27
