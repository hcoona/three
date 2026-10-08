# Native Python Package Preparation and Consumption

## Question and Evidence Level

Use native ecosystem contracts to prepare the existing adopted Python package,
build its complete wheel/sdist set and consume the original distributions
without a second dependency declaration or application implementation of UV.
This is pinned public-source evidence and a narrow design inference. No package
sync, backend import, NBGV computation, build, install or consumer has executed
for this assessment. The [MLD](../middle-level-design.md#native-python-package-execution)
owns the selected integration; a separate concrete protected protocol and actual
observation remain prerequisites for qualification.

## Native UV Responsibility

All UV findings use revision
`46b84fd0bfec23b72f29e8e2185ba68a65052f48` (the existing 0.12.23 tool basis).
The complete retained source and independent desk interpretations remain in the
delivery carrier; immutable public locations below recover each passage.

- [Native sync](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv/src/commands/project/sync.rs)
  consumes the frozen lock-derived resolution, applies noneditable overrides at
  lines 1030–1043 and native installation/hash planning thereafter.
- [CLI declarations](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-cli/src/lib.rs)
  at lines 3861–3871 document `--no-editable` for project/workspace dependencies.
  [Native editable override](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv/src/commands/editable.rs)
  changes applicable local-directory distributions through UV's own resolution.
- [Build frontend](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv/src/commands/build_frontend.rs)
  lines 746–755 gate native `build-dependency-check` preview. Lines 1028–1097
  check declared requirements before backend import, then backend-reported
  requirements, with transitive native installed metadata checks. Both sdist
  and wheel paths invoke that check at lines 1216–1219 and 1329–1332.
- [Backend interface](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-build-frontend/src/lib.rs)
  lines 543–566 and 1104–1209 retain native declared requirements and
  `get_requires_for_build` behavior. This is backend execution, not passive
  project inspection.
- [Installed requirement checks](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-installer/src/site_packages.rs)
  lines 476–620 use native markers, extras and transitive metadata. Build
  checking uses permissive compatible-version satisfaction and does not prove
  strict source type or original wheel hashes. Frozen native preparation
  remains responsible for the selected installation.

The existing root `dev` group declares Hatchling and quality tools;
`workflow-delivery-python` declares `build` and workspace `nbgv-python`. The
maintained plugin has its own static version and Hatchling backend. Reusing
these declarations avoids a backend roster or lock parser. The candidate
two-stage frozen preparation excludes local products first, then installs the
build/plugin group with shared build mode and noneditable distributions.
Noneditable installation is needed to avoid an implicit editable source-checkout
input during Git-free consumption. Neither source finding proves the actual
installed footprint, source independence or full backend suitability.

Native [build documentation](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/docs/concepts/projects/build.md)
assigns interpreter/frontend behavior to UV and filenames/payload to the backend.
Default native build produces sdist then wheel from that sdist; explicit both-format
selection can build both from source. Native archive input, package selection,
prepared interpreter and output directory are available. These contracts do not
make an arbitrary isolated backend resolution lock-frozen.

## Native Hatchling Version Reuse

Hatchling 1.32.4 uses immutable revision
`c4abc454f908bb4fc2a92452a82cb33dac14abf3`.
[Metadata core](https://github.com/pypa/hatch/blob/c4abc454f908bb4fc2a92452a82cb33dac14abf3/backend/src/hatchling/metadata/core.py)
lines 83–111 reads native `PKG-INFO`, applies its core metadata and removes
corresponding dynamic declarations in memory. Lines 258–268 use a present
project/core version before consulting its dynamic source.
[Metadata specification](https://github.com/pypa/hatch/blob/c4abc454f908bb4fc2a92452a82cb33dac14abf3/backend/src/hatchling/metadata/spec.py)
lines 72–92 maps the native Version header.

For the adopted package, a normal Hatchling sdist carrying `PKG-INFO` should
therefore retain its version during a Git-free wheel build. This is an inference
to observe, not tested consumption or general support for metadata hooks and
generated-version variants. Preserve the maintained plugin/backend and original
archive; do not rewrite static project metadata or substitute a computed version.

## Qualification and Recheck Boundary

The original wheel/sdist must expose matching native coordinate/version and
complete declared payload, with actual clean wheel installation/import and
Git-free original-sdist wheel build/install/import. Product API requirements
belong to its registered quality implementation. No V3 witness, mandatory full
commit field, universal file roster or physical tar encoding is required.
Package-name spelling is not an import-name contract.

Review changed UV/Hatchling/NBGV versions, native backend/source shapes, shared
preparation or consumer requirements when they change, and at the next package
integration review. The integration author evaluates affected native contracts;
independent engineering/evidence review checks the conclusion before relying on
it. Missing necessary native capability stops for owner disposition. Exact
helper/tool/runtime/Git identities, finite command/diagnostic/time/state bounds,
public-only dependency/cache effects, owned cleanup and original evidence must
be accepted in the concrete protocol before execution. Completed graph and
enrichment campaigns remain stopped. Windows remains the owner's feasibility
assumption. No registry publication, dispatch or credentials/access/Environment
operation follows from this design evidence.
