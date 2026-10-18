# Python Native Planning Source Assessment

## Purpose and Scope

This source assessment supported the Python CI integration, delivered in
[PR #1114](https://github.com/hcoona/three/pull/1114) under the
[integration contract](../middle-level-design.md#native-fact-integration).
It identifies the native facts available at the repository's former UV 0.12.23
pin and a specific missing relation. It is not an accepted reader, complete
Python graph, implementation choice or complete runtime qualification. The later
[primitive observation](#primitive-native-observation) retains its separate scope.
The [supplement retirement recheck](#embedded-uv-supplement-retirement) binds the
UV 0.13.0 pin of that time and supersedes the library candidate as the selected design.

The original source pin is UV commit
[`46b84fd0bfec23b72f29e8e2185ba68a65052f48`][uv-pin]. At assessment time the committed
[`mise.toml`][repository-uv-pin] and [`mise.lock`][repository-uv-lock] selected
0.12.23; the [UV 0.13.1 pin recheck](#uv-0131-pin-recheck) covers the later pin. The source conclusions below use pinned public source and repository
files. No workspace evaluation, resolver, installation, backend hook or build
was executed for the source assessment; the later observation is separate evidence.

## Available Native Facts

UV's [`workspace metadata` documentation][metadata-doc] describes a structured
resolution graph with workspace members, native source coordinates, dependencies,
markers, extras and groups. The [serializer][metadata-source] emits member
names, absolute paths and node IDs, plus structured source kinds. IDs are opaque
join keys; parsing their readable spelling would invent another source decoder.

The current [exporter construction][metadata-construction] derives workspace
member paths and reachability from packages recorded in the native lock. The
[command][metadata-command] can use lockfile-based workspace discovery when frozen.
These are source findings, not observed output or proof of complete source inputs.
The synchronized lock at each exact endpoint remains a planning prerequisite.

Both `workspace metadata` and [`tree --format json`][tree-warning] are
experimental at this pin. The metadata documentation labels the
[schema][metadata-preview] `preview`; UV's [metadata warning][metadata-warning]
says the interface can change without warning. An exact version/schema consumer
is a candidate, but pinning
cannot supply missing facts or promise future compatibility.

The native [lock flags][uv-lock-flags] distinguish `--frozen`, which consumes the
existing lock without checking freshness, from `--locked`, which asserts freshness
through UV's [lock operation][metadata-lock-operation]. The accepted repository lock
synchronization prerequisite can support a frozen consumer of each exact endpoint;
it cannot justify borrowing another checkout's lock. `--sync` permits environment
installation and adds module ownership. Installed module ownership is not the
source graph of all isolated build environments.

## Build-Only Dependency Gap

The omission is explicit in native source, rather than an application parsing
failure. The [metadata exporter][metadata-source] says build nodes are stubbed
and unused, initializes `build_system` to `None`, and supplies an empty dependency
slice for Build nodes. The metadata documentation lists build nodes as future
work. A prospective example in the source does not establish emitted edges.

The repository has a concrete source counterexample. The Python smoke
[`pyproject.toml`][smoke-manifest] declares `nbgv-python` in
`build-system.requires`, an empty ordinary dependency list, and a native
workspace source for that build requirement. The [sample manifest][sample-manifest] declares
both the build requirement and an ordinary dependency on that package.

The resulting inference is narrow: an ordinary-dependency reverse closure can
exclude a smoke build affected by a local build-requirement/plugin change; the sample's ordinary
edge can conceal that omission. This is not an observed failed UV invocation or
CI run. Metadata alone therefore lacks a necessary premise for the required
Python impact closure.

## Native Library Candidate and Its Limits

This section retains the original candidate assessment. The
[planning-fact contract](../integration/python.md#native-python-planning-facts)
no longer selects it; see the [retirement recheck](#embedded-uv-supplement-retirement).

UV [documents][build-sources] that its build frontend respects `tool.uv.sources`
for build requirements. PyPA `build` does not provide those UV source semantics.
Using `--no-sources` changes the operation and cannot qualify the source-aware
planning path.

UV publicly exports
[`BuildRequires::from_project_maybe_workspace`][build-lowering], used by its own
build frontend. It delegates workspace discovery, source/index lowering and
markers to native UV code. Workspace sources produce typed directory coordinates,
which a finite adapter could join to native project roots. This is source
association, not complete build dependency resolution, exact version-satisfaction
proof or arbitrary file-read discovery.

The passive extraction interface has limits. UV's
[build-system extractor][uv-extractor] is private. Public
[build setup][uv-build-setup] [resolves and installs][uv-build-setup-effects]
build requirements under build isolation; it is not a passive graph query.
PyPA `build` offers the public
[`ProjectBuilder.build_system_requires`][pypa-build] property for declared/default
requirements. The planning contract bounds its composition with UV's native
parsing/lowering to an explicit declared shape. The source finding alone does not
qualify error and identity behavior; arbitrary default contexts remain unestablished.
Dynamic PEP 517 hooks execute backend code and do not promise a complete input
inventory.

UV [labels][uv-versioning] its component crates internal and unstable. Public Rust
exports do not establish a supported external integration or compatibility
promise. The pinned [workspace manifest][uv-cargo] declares Rust 1.97.0, and
[`uv-distribution`][uv-distribution-cargo] declares version 0.0.90. A supplement adds a real Rust preparation dependency alongside Python
requirement extraction. These source findings alone do not establish compilation,
runtime availability or platform feasibility.

This candidate does not yet establish an unavoidable product tradeoff. The
accepted HLD permits native-language library helpers; the owner's PNPM-specific
CLI choice does not impose a Python CLI-only requirement. The planning contract selects a finite composition from this evidence. Its
complete integration and support/maintenance cost remain unqualified. Source
findings alone must not claim complete relations have already
been recovered, relax completeness, add duplicate declarations, or implement UV
source semantics in the application.

## Embedded UV Supplement Retirement

The owner approved retiring the embedded Rust supplement. This recheck evaluates
each fact it supplied against the UV pin of that time, tag `0.13.0` at commit
[`4ecd0eff2a81e3ffdcd7cfee10d08936e4747c17`][uv-013]. Source findings below cite
that commit; runtime observations used the mise-pinned UV 0.13.0 binary on Linux
x86_64 with CPython 3.14.3, against repository commit
`e87ddd5e12fb0b1314e87af840294ad2407b3521`. These are local, credential-free
observations, not a native experiment campaign or caller qualification.

Source findings:

- The metadata exporter still [stubs build nodes][uv-013-build-stub] and
  [emits no Build dependencies][uv-013-build-empty]. No UV CLI exposes
  build-requirement lowering, so the relation still needs a passive extraction.
- UV [lowering][uv-013-lowering] takes a requirement's project
  `tool.uv.sources` entry before the workspace entry. A workspace-member
  requirement without a source, or with a non-workspace source, is an error;
  a `{ workspace = true }` source lowers to the member. The supplement already
  rejected non-workspace source shapes before calling this code; it still
  admitted workspace sources with additional fields.
- [Settings discovery][uv-013-from-directory] reads a directory's `uv.toml` before
  its `pyproject.toml`, and the [documented][uv-013-config-files] search walks to
  the nearest parent. [`UV_NO_SYSTEM_CONFIG`][uv-013-system] disables system
  configuration.
- UV [documents][uv-013-default-groups] that it includes the `dev` group by
  default unless `tool.uv.default-groups` changes it or `--no-dev` excludes it.
  The passive guard already rejects that setting and the legacy group settings.

Inference: under the existing guards, a canonical name join from PyPA-parsed
build requirements to metadata members, admitted only when the effective
source is absent or exactly `{ workspace = true }`, yields the same
associations as the former lowering for every admitted shape and fails on the
rest. The admission is a strict subset of the former one. It is an application rule, not UV lowering. Treating every marker as
active yields a finite union that can only add consumers. The operation's
`requires-python` check is already enforced by `uv sync --frozen --python` during
preparation.

Runtime observation: on the repository commit above, the former reader with the
supplement built from its committed lock, and the replacement reader, produced
byte-identical projections: 15 projects, 664 input bindings, the same four
`nbgv-python` build associations, 15 full-selection checks and identical
single-path selections for all 394 bound inputs. Of 40 marked metadata edges,
none reaches a local workspace node. The implementing pull request records the
summary and output digest; the throwaway probe and JSON were not retained. The
committed `GuardedNameJoinMatchesRecordedNativeLowering` replay test compares
the join with the retained PR #1063 lowering.

Limits: future marker-gated local edges are over-selected rather than
platform-exact. Admitting other source shapes, default-group settings or a
project `uv.toml` requires a new explicit rule. This recheck does not establish
Windows behavior or different-commit renamed producers; the paired union
consumes the same association records. The join premises are guarded instead of
rechecked. `PythonNativeContractTests.MetadataAndPassiveExtractionKeepTheGuardedJoinPremises`
runs the locked UV, CPython, PyPA `build`, `packaging`, Hatchling and
`nbgv-python` on a committed fixture workspace through the application readers.
It asserts that metadata gives a build-only member no edge, gives an empty or
marker-disjoint group no node and stays inside the reader's admitted shape, and
that passive extraction and the guarded join yield exactly the build association.
CI runs it in the Linux native-preparation job whenever `uv.lock`, `mise.toml`,
`mise.lock`, `.config/uv/uv.toml` or `.python-version` changes. A pin change
that alters one of these facts fails CI, and the gap returns to the owner.
`GuardedNameJoinMatchesRecordedNativeLowering` remains a replay of the retained
PR #1063 lowering.

## UV 0.13.1 Pin Recheck

The repository pin changed from UV 0.13.0 to 0.13.1 in
[PR #1238](https://github.com/hcoona/three/pull/1238). Tag `0.13.0` resolves to
commit [`4ecd0eff2a81e3ffdcd7cfee10d08936e4747c17`][uv-013] and tag `0.13.1` to commit
[`62faed3351cfee2121b3c28b937b1f4b93233a93`][uv-0131]. This recheck is a source comparison between those
commits plus a replay test; no UV 0.13.1 binary, workspace or native query was
executed for it.

| Cited source at 0.13.0                                                    | 0.13.0 → 0.13.1 | Reviewed verdict                                          |
| ------------------------------------------------------------------------- | --------------- | --------------------------------------------------------- |
| `crates/uv-lock/src/lock/export/metadata.rs` (build stub and empty Build) | Identical       | Build nodes are still stubbed with no Build dependencies  |
| `crates/uv-distribution/src/metadata/lowering.rs` (source lowering)       | Identical       | Lowering precedence and workspace-source errors unchanged |
| `crates/uv-settings/src/lib.rs` (directory and system settings)           | Identical       | Settings discovery and `UV_NO_SYSTEM_CONFIG` unchanged    |
| `docs/concepts/configuration-files.md`                                    | Identical       | Documented settings search unchanged                      |
| `docs/concepts/projects/dependencies.md` (default groups)                 | Identical       | Default `dev` group behavior unchanged                    |

The metadata command (`crates/uv-workspace-commands/src/metadata.rs`), its
documentation (`docs/reference/internals/metadata.md`), the build frontend
(`crates/uv-build-frontend/src/lib.rs`) and the workspace group sources
(`crates/uv-workspace/src/dependency_groups.rs`,
`crates/uv-workspace/src/workspace.rs`) are also identical between the two
commits.

The committed `GuardedNameJoinMatchesRecordedNativeLowering` test passed at
this recheck's commit (1 test). It replays the retained PR #1063 lowering
through the application's join; it does not run UV 0.13.1 and is not a native
observation of UV 0.13.1 behavior.

Conclusion: the source basis for the admitted guarded join is unchanged at UV
0.13.1, so the join remains admitted on that basis. This recheck establishes no
runtime, Windows, publisher or live-publication behavior and does not requalify
the UV 0.12.23 observations. Its independent evidence review is recorded in the
governing pull request.

## Pytest Configuration Source Basis

Immutable pytest 9.1.1 source at
[`cf470ec0bf7eb89cd97dd56df4859eae5db46447`][pytest-source] supplies the candidate
configuration query used by the
[quality-owner contract](../integration/python.md#python-quality-owners-and-input-only-members).
Public `pytest.main(..., plugins=[...])` calls the first-result
`pytest_cmdline_main` hook after native configuration parsing. A supplied plugin
can return a result before the default configure/collect/run lifecycle while
reading `Config.getini`, `rootpath` and `inipath`. The
[configuration source][pytest-config], [hook contract][pytest-hook] and
[default main lifecycle][pytest-main] establish these source-level boundaries.

Native parsing can already import explicit plugins and alter Python paths before
that hook. Disabling autoload and conftest discovery does not disable explicit
plugin processing; inherited `PYTEST_ADDOPTS` and `PYTEST_PLUGINS` also affect the
request. Native target selection expands configured globs and can fall back to
the invocation directory when configured targets do not resolve. Reading declared
`testpaths` alone therefore does not establish arbitrary effective execution scope.
The initial design admits only explicit committed targets without those unsupported
necessary behaviors; it does not port the private selection algorithm.

These are public-source findings and a bounded integration inference, not a helper
runtime observation. No pytest query/helper was executed for this assessment.
`Native/Python/test_pytest_configuration_contract.py` now runs the helper under
the locked pytest: the early hook returns the consumed fields without importing a
conftest or collecting a test, ambient `PYTEST_ADDOPTS`/`PYTEST_PLUGINS` do not
reach it, a `pythonpath` is reported for rejection, and a missing required plugin
fails the query.
Actual complete fact/caller qualification still needs its separately accepted
concrete protocol and complete operation/ownership/check mapping.

## PyPA Dependency Pin Recheck

Accepted repository [helper preparation][pypa-helper-pin] now selects PyPA
`build` 1.6.1. Inspection of the [locked wheel source][pypa-build-wheel] with
SHA256 `ecd351a4be9d35a9eaaba244a7687143c9c7d4aea6ac964e7e7ddab20cbcf4e7`
finds that `build/_builder.py` still reads native TOML and constructs its hook caller
in `ProjectBuilder.__init__` (lines 177–221); `build_system_requires` (lines 249–256)
returns the native declared requirement strings as a set without invoking a backend.
The wheel was inspected as source bytes, not imported, installed or executed.

This supports the narrow property-consumption source contract for the admitted
explicit Hatchling/no-backend-path shape. It does not qualify actual composition
with 1.6.1 or the current UV lock revision. The primitive observation below remains
bound to its original tools, including PyPA 1.4.1; its stopped campaign and expired
reserves are unchanged. A future complete collector/caller protocol must bind the
actual accepted dependencies and lock format instead of upgrading the old observation.

## Primitive Native Observation

The independently accepted [native outcome](https://github.com/hcoona/three/pull/1063#issuecomment-6045152503)
and [retained originals](https://github.com/hcoona/three/pull/1063#issuecomment-6045108404)
qualify the pinned ordered C# reader, passive extraction and UV Rust supplement
on Linux 6.6.157.1-1.azl3/x86_64/glibc 2.38, CPython 3.14.3, UV 0.12.23 and
Rust 1.97.0. The evidence archive SHA256 is
`f8952378e7b7341d20ae6f12ae8657f67150ac965a3ed4eadc85189d2b11ce25`.
The protocol manifest SHA256 is
`83e68edfa26e634ed8d3b0d504ddb2d67bc92f4c8d81d61fb69f6a880cd22a06`.
The actual composition preserves build-only and ordinary-plus-build associations,
local build chains/extras, native marker/interpreter/group answers and finite
negative diagnostic outcomes. The two actual repository endpoints deliberately
materialize the same reviewed tree. Synthetic ordinary relations are controls,
not observed native ordinary edges.

This observation does not establish complete Workflow ownership/activity/impact
assembly, different-commit deletion/rename, check execution, package consumers,
caller cutover, Windows, publication, future compatibility, zero egress or
universal containment. The first independently accepted success stopped the
campaign; both unused diagnostic reserves expired. Retained replay fixtures
exercise application adaptation without reopening that campaign. Any necessary
later native experiment needs its own accepted concrete protocol and effects
bounds; these results do not authorize it.

## Later Evidence and Recheck

Before a complete runnable caller was selected, the remaining questions were
complete endpoint operation/activity and runnable ownership assembly, integration
of the qualified generated/version producer inputs into actual check consumers,
Python check/prerequisite/execution mapping and caller cutover.
Undeclared dynamic inputs retain the implementation plan's unsupported boundary.
No generic helper framework is implied.

The [current-tool collector observation](python-native-graph-qualification.md#completed-ordinary-and-no-dev-observation)
qualifies exact complete ordinary/no-dev graph composition, separately from
this source assessment and the primitive observation. Its failed lifetimes,
corrections and final cleanup remain retained; the first accepted success stopped
all remaining captures. The separate
[planning-input observation](python-planning-enrichment-qualification.md#completed-planning-input-observation)
qualifies native pytest, committed NBGV/quality inputs and producer input mapping
on those exact Linux subjects. It reused retained graph answers without another
UV query; its campaign and cleanup are complete, and both unused diagnostics
stopped. Complete runnable caller facts, package preparation/execution and
caller cutover were pending at that time. They are now owned by the Python integration contract's
[Python planning facts](../integration/python.md#native-python-planning-facts),
[CI group](../integration/python.md#native-python-ci-group) and
[package execution](../integration/python.md#native-python-package-execution)
contracts, with their own qualification.

If further native execution is needed, its exact remaining subject first needs
a concrete accepted isolated protocol. Reuse the retained primitive evidence
for its qualified boundary; do not repeat it as a complete-reader test.

The metadata, passive-extraction and identity facts these findings rely on are
guarded by `PythonNativeContractTests` at the locked pins. The pytest query is guarded by
`Native/Python/test_pytest_configuration_contract.py`, which runs the locked
pytest. A change to the Python planning facts or caller that relies on other
native behavior still evaluates it in that change. Keep immutable source
findings separate from later operation observations and support claims.

[uv-pin]: https://github.com/astral-sh/uv/tree/46b84fd0bfec23b72f29e8e2185ba68a65052f48
[metadata-doc]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/docs/reference/internals/metadata.md#L3-L35
[metadata-source]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-lock/src/lock/export/metadata.rs
[metadata-construction]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-lock/src/lock/export/metadata.rs#L1255-L1305
[metadata-command]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv/src/commands/workspace/metadata.rs#L77-L107
[smoke-manifest]: https://github.com/hcoona/three/blob/48539dd209a5247b86b7c7e28bfce405e15a0c7c/src/public/lib/hcoona-release-smoke-python/pyproject.toml
[sample-manifest]: https://github.com/hcoona/three/blob/48539dd209a5247b86b7c7e28bfce405e15a0c7c/src/sample/nbgv-hatch-demo/pyproject.toml
[build-sources]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/docs/concepts/projects/dependencies.md#L800-L838
[build-lowering]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-distribution/src/metadata/build_requires.rs#L18-L168
[pypa-build]: https://github.com/pypa/build/blob/bd889569a1b9e2e7722682378c2d0cec469166a2/src/build/_builder.py#L199-L226
[uv-versioning]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/docs/reference/policies/versioning.md#L14-L28
[repository-uv-pin]: https://github.com/hcoona/three/blob/5eb196692be8b45298a506c71f521e19f9a9d226/mise.toml#L25-L27
[repository-uv-lock]: https://github.com/hcoona/three/blob/5eb196692be8b45298a506c71f521e19f9a9d226/mise.lock#L815-L817
[tree-warning]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv/src/commands/project/tree.rs#L84-L90
[uv-extractor]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-build-frontend/src/lib.rs#L661-L678
[uv-build-setup]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-build-frontend/src/lib.rs#L289-L335
[uv-build-setup-effects]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-build-frontend/src/lib.rs#L391-L417
[uv-cargo]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/Cargo.toml#L10-L16
[uv-distribution-cargo]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-distribution/Cargo.toml#L1-L6
[metadata-preview]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/docs/reference/internals/metadata.md#L160-L170
[metadata-warning]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv/src/commands/workspace/metadata.rs#L66-L73
[uv-lock-flags]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-cli/src/lib.rs#L7879-L7892
[metadata-lock-operation]: https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv/src/commands/workspace/metadata.rs#L99-L174
[uv-013]: https://github.com/astral-sh/uv/tree/4ecd0eff2a81e3ffdcd7cfee10d08936e4747c17
[uv-013-build-stub]: https://github.com/astral-sh/uv/blob/4ecd0eff2a81e3ffdcd7cfee10d08936e4747c17/crates/uv-lock/src/lock/export/metadata.rs#L249
[uv-013-build-empty]: https://github.com/astral-sh/uv/blob/4ecd0eff2a81e3ffdcd7cfee10d08936e4747c17/crates/uv-lock/src/lock/export/metadata.rs#L646
[uv-013-lowering]: https://github.com/astral-sh/uv/blob/4ecd0eff2a81e3ffdcd7cfee10d08936e4747c17/crates/uv-distribution/src/metadata/lowering.rs#L61-L150
[uv-013-from-directory]: https://github.com/astral-sh/uv/blob/4ecd0eff2a81e3ffdcd7cfee10d08936e4747c17/crates/uv-settings/src/lib.rs#L130-L132
[uv-013-config-files]: https://github.com/astral-sh/uv/blob/4ecd0eff2a81e3ffdcd7cfee10d08936e4747c17/docs/concepts/configuration-files.md#L1-L44
[uv-013-system]: https://github.com/astral-sh/uv/blob/4ecd0eff2a81e3ffdcd7cfee10d08936e4747c17/crates/uv-settings/src/lib.rs#L83-L86
[uv-013-default-groups]: https://github.com/astral-sh/uv/blob/4ecd0eff2a81e3ffdcd7cfee10d08936e4747c17/docs/concepts/projects/dependencies.md#L738-L757
[pytest-source]: https://github.com/pytest-dev/pytest/tree/cf470ec0bf7eb89cd97dd56df4859eae5db46447
[pytest-config]: https://github.com/pytest-dev/pytest/blob/cf470ec0bf7eb89cd97dd56df4859eae5db46447/src/_pytest/config/__init__.py
[pytest-hook]: https://github.com/pytest-dev/pytest/blob/cf470ec0bf7eb89cd97dd56df4859eae5db46447/src/_pytest/hookspec.py
[pytest-main]: https://github.com/pytest-dev/pytest/blob/cf470ec0bf7eb89cd97dd56df4859eae5db46447/src/_pytest/main.py
[pypa-helper-pin]: https://github.com/hcoona/three/blob/8d2ed8ed86ebea8df509a0a2d78b0671ecb7a1b8/pyproject.toml#L21
[pypa-build-wheel]: https://files.pythonhosted.org/packages/ad/9b/9fb3585dabcd73a1b2a6267f63f62649347c9e6d072c9fde365b105abb2c/build-1.6.1-py3-none-any.whl
[uv-0131]: https://github.com/astral-sh/uv/tree/62faed3351cfee2121b3c28b937b1f4b93233a93
