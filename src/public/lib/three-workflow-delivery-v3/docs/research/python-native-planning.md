# Python Native Planning Source Assessment

## Purpose and Scope

This source assessment supports the next Python CI integration under the
[implementation plan](../migration-strategy.md#native-facts-and-first-supported-shapes).
It identifies the native facts available at the repository's UV 0.12.23 pin and a
specific missing relation. It is not an accepted reader, complete Python graph,
implementation choice or complete runtime qualification. The later
[primitive observation](#primitive-native-observation) retains its separate scope.

The source pin is UV commit
[`46b84fd0bfec23b72f29e8e2185ba68a65052f48`][uv-pin]. The committed
[`mise.toml`][repository-uv-pin] and [`mise.lock`][repository-uv-lock] select
0.12.23. The source conclusions below use pinned public source and repository
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

The [planning-fact contract](../middle-level-design.md#native-python-planning-facts)
owns the selected integration and admitted shape. These source findings supply
its evidence basis; they do not qualify its implementation.

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

## Pytest Configuration Source Basis

Immutable pytest 9.1.1 source at
[`cf470ec0bf7eb89cd97dd56df4859eae5db46447`][pytest-source] supplies the candidate
configuration query used by the
[quality-owner contract](../middle-level-design.md#python-quality-owners-and-input-only-members).
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

## Next Evidence and Recheck

Before selecting a complete runnable caller, the remaining questions are
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
caller cutover remain pending.

If further native execution is needed, its exact remaining subject first needs
a concrete accepted isolated protocol. Reuse the retained primitive evidence
for its qualified boundary; do not repeat it as a complete-reader test.

Recheck these source findings when the repository UV pin changes or the next
Python integration review relies on them. Its author checks the affected native
interfaces and omissions; an independent evidence reviewer evaluates the result.
The next Python integration review is the fallback event. Keep immutable source
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
[pytest-source]: https://github.com/pytest-dev/pytest/tree/cf470ec0bf7eb89cd97dd56df4859eae5db46447
[pytest-config]: https://github.com/pytest-dev/pytest/blob/cf470ec0bf7eb89cd97dd56df4859eae5db46447/src/_pytest/config/__init__.py
[pytest-hook]: https://github.com/pytest-dev/pytest/blob/cf470ec0bf7eb89cd97dd56df4859eae5db46447/src/_pytest/hookspec.py
[pytest-main]: https://github.com/pytest-dev/pytest/blob/cf470ec0bf7eb89cd97dd56df4859eae5db46447/src/_pytest/main.py
[pypa-helper-pin]: https://github.com/hcoona/three/blob/8d2ed8ed86ebea8df509a0a2d78b0671ecb7a1b8/pyproject.toml#L21
[pypa-build-wheel]: https://files.pythonhosted.org/packages/ad/9b/9fb3585dabcd73a1b2a6267f63f62649347c9e6d072c9fde365b105abb2c/build-1.6.1-py3-none-any.whl
