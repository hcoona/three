# Native Python Package Preparation and Consumption

## Question and Evidence Level

Use native ecosystem contracts to prepare the existing adopted Python package,
build its complete wheel/sdist set and consume the original distributions
without a second dependency declaration or application implementation of UV.
The native-interface findings below are pinned public-source evidence and narrow
design inference. The separate
[completed corrected Linux observation](python-native-package-qualification.md#completed-corrected-linux-package-observation)
qualifies the exact four-check integration, retaining the separately classified
audit-method exception. Historical failures remain in that protocol/evidence record.
The [MLD](../middle-level-design.md#native-python-package-execution) owns the selected
integration; actual root pytest, complete paired callers/Python CI and Release retain
separate qualification requirements.

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
  checking uses permissive compatible-version satisfaction, but Directory
  requirements retain source-specific checks.
  [Directory satisfaction](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-installer/src/satisfies.rs#L363-L396)
  requires equal requested/installed editability before path/cache checks.
  Neither behavior proves original wheel hashes. Frozen native preparation
  remains responsible for the selected installation.

The existing root `dev` group declares Hatchling and quality tools;
`workflow-delivery-python` declares `build` and workspace `nbgv-python`. The
maintained plugin has its own static version and Hatchling backend. Reusing
these declarations avoids a backend roster or lock parser. The candidate
earlier bootstrap excluded local products first, then selected both existing
groups with shared build mode and native workspace editability for source building.
The [first native preparation failure](python-native-package-qualification.md#first-native-preparation-failure)
shows that selecting only the build/plugin group removes the previously installed
backend tools. Shared build mode does not preserve an omitted group.
The final failed observation established that noneditable sync before checked
source building does not satisfy this workspace requirement.
[Build requirement extraction](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-build-frontend/src/lib.rs#L770-L787)
lowers native workspace requirements with editability enabled;
[workspace lowering](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-distribution/src/metadata/lowering.rs#L816-L858)
uses the existing declaration's value or the native editable default. Installed
noneditable metadata and this source trace support an installation-mode mismatch
at confidence 9/10; the exact runtime comparison branch was not traced. No UV bug
or operator defect is established.

After complete original archives exist, one additional frozen both-groups sync
with shared build mode and `--no-editable` reconciles the same owned tools before
Git-free sdist consumption. Noneditable installation avoids an implicit editable
source-checkout input in that consumer. The proposed source phase and conversion
retain native dependency checks and the same declarations; neither source finding
proves actual installation, removal of source links or clean consumer behavior.

Native [build documentation](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/docs/concepts/projects/build.md)
assigns interpreter/frontend behavior to UV and filenames/payload to the backend.
Default native build produces sdist then wheel from that sdist; explicit both-format
selection can build both from source. Native archive input, package selection,
prepared interpreter and output directory are available. These contracts do not
make an arbitrary isolated backend resolution lock-frozen.

## Native Editable Tool Preparation

The separately audited second corrected-campaign lifetime failed while preparing
editable `nbgv-python`: Hatchling could not import `editables`. Product build,
contents and both original consumers were not reached. Independent triage
classified `EDITABLE-PREPARATION-001` as a true positive, confidence 10/10 for the
observed failure and 9/10 for the integration cause. The
[actual observation](python-native-package-qualification.md#second-lifetime-editable-preparation-failure)
retains the exact subject, failed results and remaining allowance.

At pinned Hatchling `c4abc454f908bb4fc2a92452a82cb33dac14abf3`, the public
[editable requirement hook](https://github.com/pypa/hatch/blob/c4abc454f908bb4fc2a92452a82cb33dac14abf3/backend/src/hatchling/build.py#L61-L69)
returns configured requirements plus `EDITABLES_REQUIREMENT`. The
[editable builder](https://github.com/pypa/hatch/blob/c4abc454f908bb4fc2a92452a82cb33dac14abf3/backend/src/hatchling/builders/wheel.py#L543-L550)
imports that module. This is a maintained dynamic requirement channel; the
generic UV hint about a likely missing static declaration does not establish a
plugin manifest defect.

Pinned UV's [build environment preparation](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-build-frontend/src/lib.rs#L373-L510)
assumes a supplied environment in shared mode. Isolated preparation queries the
backend and resolves/installs its additional requirements. The separately exposed
requirement-query method remains available to shared-mode callers; this source
finding does not claim that no such caller can query requirements. The actual
capture observes a failed import, without tracing every hook invocation.

The source-supported correction uses one frozen native sync of the existing
`dev` and `workflow-delivery-python` groups with default isolation and declared
workspace editability. It removes the redundant dev bootstrap and forced shared
editable installation. UV supplies auxiliary requirements; Workflow adds no
backend roster, plugin/root declaration amendment or manual hook orchestration.
Auxiliary isolated backend requirements are native-resolved and are not promised
lock-frozen. Final installed tools still come from the selected locked groups;
relevant observed builder/version inputs remain part of qualification evidence.
The complete original product build retains prepared tools, shared mode and
native build-dependency checking. Same-tools noneditable reconciliation and
Git-free original-archive consumption remain unchanged.

This is source evidence and a correction inference, not successful runtime
qualification. Accept the affected contract before implementation, then bind and
review a fresh exact subject/protocol/readiness before using the remaining
package lifetime. Existing owned public-only installation/build/cache effects,
credentials/TLS restrictions and finite bounds apply. No additional owner decision
was identified for this bounded candidate; changed effects, reduced guarantees or
exhausted allowance still require owner disposition.

## Native Original-Archive Build Identity

For the pinned UV source, archive-file input selects `WheelFromSdist` in
[build-plan selection](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv/src/commands/build_frontend.rs#L1579-L1596).
[Archive extraction](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv/src/commands/build_frontend.rs#L980-L1024)
provides the backend tree, while
[build dispatch](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv/src/commands/build_frontend.rs#L1313-L1332)
passes the original archive file as `install_path`. The
[dispatcher](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-dispatch/src/lib.rs#L524-L600)
retains that distinction.

[Build requirement lowering](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-distribution/src/metadata/build_requires.rs#L41-L81)
uses the install identity for project discovery.
[Discovery](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-workspace/src/workspace.rs#L1665-L1710)
cannot read a child pyproject from an archive file; it returns no project and
lowering uses ordinary PEP 517 metadata, without the checkout's workspace
sources. Backend-reported requirements retain the file identity at
[lines 1206–1229](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-build-frontend/src/lib.rs#L1206-L1229).
Native dependency checking remains enabled. Invoking an extracted directory would
change this basis and is outside the selected consumer contract.

Source-directory default sdist-to-wheel building retains its original directory
identity at
[lines 845–907](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv/src/commands/build_frontend.rs#L845-L907).
Keep workspace-compatible tools through both original outputs before conversion.
The separate archive-file route supports the phase design at source level; it is
not observed package qualification or proof of source-independent consumer imports.

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

## Native Output Directory Metadata

Pinned UV `46b84fd0bfec23b72f29e8e2185ba68a65052f48` documents
[`--no-create-gitignore`](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv-cli/src/lib.rs#L3101-L3109).
Its [output-directory preparation](https://github.com/astral-sh/uv/blob/46b84fd0bfec23b72f29e8e2185ba68a65052f48/crates/uv/src/commands/build_frontend.rs#L1362-L1377)
otherwise creates a `.gitignore` containing `*`. This is native directory metadata,
not a distribution or an extra product output.

The [third-lifetime actual audit](https://github.com/hcoona/three/pull/1104#issuecomment-6073057562)
observed successful corrected tool preparation, identity and checked product build,
then output adoption failed with `InvalidSdistFilename`. Both native archives and
the output marker remain in failed state. The
[separate classification](https://github.com/hcoona/three/pull/1104#issuecomment-6073153756)
confirmed the output-adoption mismatch at confidence 9/10. The exact offending path
and producer are supported inferences; no per-file creation trace was recorded.
Parsing every directory entry and requiring exactly two total entries both conflict
with that retained directory. The second guard was not reached after helper failure.

The faithful invocation correction requests `--no-create-gitignore` on both the
source-product build and the original-sdist consumer wheel build, using fresh owned
output directories. Preserve strict enumeration, the complete original wheel/sdist
set, native filename/metadata agreement, original bytes, shared dependency checking
and both independent consumers. Do not filter auxiliary files, maintain a sidecar
roster or delete output metadata after a build. The flag does not remove an existing
marker and supplies no repair or reuse authority for the failed lifetime.

Controlled validation must cover both build paths with the documented native marker
default represented by the command substitute, plus rejection of extra or missing
outputs and retention of completed independent results on later failure. Source and
controlled coverage do not establish actual corrected output adoption or consumers.
Historical package lifetimes remain spent. The
[fresh package reserve](python-native-package-qualification.md#fresh-output-option-package-reserve)
records the approved buffer and unchanged first subject. The
[completed corrected Linux observation](python-native-package-qualification.md#completed-corrected-linux-package-observation)
now qualifies exact native output adoption and both original consumers, with the
separately classified audit-method exception retained. One package lifetime was spent
and both unused slots expired. Existing callers, root pytest and Release retain
separate qualification requirements.
