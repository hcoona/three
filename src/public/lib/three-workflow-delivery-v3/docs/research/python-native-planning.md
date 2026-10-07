# Python Native Planning Source Assessment

## Purpose and Scope

This source assessment supports the next Python CI integration under the
[implementation plan](../migration-strategy.md#native-facts-and-first-supported-shapes).
It identifies the native facts available at the repository's UV 0.12.23 pin and a
specific missing relation. It is not an accepted reader, complete Python graph,
implementation choice or runtime qualification.

The source pin is UV commit
[`46b84fd0bfec23b72f29e8e2185ba68a65052f48`][uv-pin]. Repository `mise.toml`
selects 0.12.23; the inspected locked tool reports that version. Bare PATH's
0.12.19 is not the repository authority. No workspace evaluation, resolver,
installation, backend hook or build was executed for this assessment. Inspection
used public source, existing repository files and tool version/help output.

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

Both `workspace metadata` and `tree --format json` are experimental at this pin.
The metadata schema reports `preview`; UV warns that these interfaces can change
without warning. An exact version/schema consumer is a candidate, but pinning
cannot supply missing facts or promise future compatibility.

`--frozen` consumes the existing lock without checking freshness. `--locked`
asserts freshness through UV's lock operation. The accepted repository lock
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
workspace source for the backend. The [sample manifest][sample-manifest] declares
both the build requirement and an ordinary dependency on that package.

The resulting inference is narrow: an ordinary-dependency reverse closure can
exclude a smoke build affected by a local backend change; the sample's ordinary
edge can conceal that omission. This is not an observed failed UV invocation or
CI run. Metadata alone therefore lacks a necessary premise for the required
Python impact closure.

## Native Library Candidate and Its Limits

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

The extraction boundary is still unresolved. UV's passive build-system extractor
is private. Public build setup performs resolution and installation; it is not a
passive graph query. PyPA `build` offers the public
[`ProjectBuilder.build_system_requires`][pypa-build] property for declared/default
requirements. Combining that property with UV's native parsing and lowering is a
candidate, with defaults and error behavior still needing contract review.
Dynamic PEP 517 hooks execute backend code and do not promise a complete input
inventory.

UV [labels][uv-versioning] its component crates internal and unstable. Public Rust
exports do not establish a supported external integration or compatibility
promise. The inspected source declares Rust 1.97.0 and component version 0.0.90.
A supplement would add a real Rust preparation dependency alongside Python
requirement extraction. Neither compilation, runtime availability nor platform
feasibility has been established here.

This candidate does not yet establish an unavoidable product tradeoff. The
accepted HLD permits native-language library helpers; the owner's PNPM-specific
CLI choice does not impose a Python CLI-only requirement. The next design review
can compare the concrete composition and its support/maintenance cost with a
sufficient upstream interface. It must not claim the missing relation has already
been recovered, relax completeness, add duplicate declarations, or implement UV
source semantics in the application.

## Next Evidence and Recheck

Before selecting a complete reader, the remaining questions are requirement
extraction alignment, native build-source association, build-only and transitive
relations at both endpoints, operation dimensions/markers, backend configuration
and generated/version input ownership, and helper preparation feasibility.
Undeclared dynamic inputs retain the implementation plan's unsupported boundary.
No generic helper framework is implied.

If native execution is needed, its chosen interface first needs a concrete,
accepted isolated protocol. A build-only consumer, an ordinary-plus-build control,
a local backend chain, missing-member/source errors and conditional native sources
are useful candidate scenarios. This assessment does not authorize those calls.

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
