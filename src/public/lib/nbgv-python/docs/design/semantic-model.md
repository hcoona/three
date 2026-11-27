# NBGV-to-Python Domain Semantic Specification

This candidate specification models the normative ecosystem standards, pinned
upstream tool behaviors, and repository consumer contracts governing a
Nerdbank.GitVersioning (`nbgv`) integration for Python packaging. The package
[README](../../README.md),
[architecture overview](../architecture/overview.md), and
[Hatch integration record](../architecture/hatch-integration.md) remain the
current implementation authorities; this specification evaluates target domain
semantics without claiming current code conformance or altering accepted release
commitments.

## Normative Standards and Pinned Tool Provenance

- **Normative specifications:** [SemVer 2.0.0](https://semver.org/spec/v2.0.0.html),
  [PEP 440](https://peps.python.org/pep-0440/),
  [PEP 503](https://peps.python.org/pep-0503/),
  [PEP 517](https://peps.python.org/pep-0517/),
  [PEP 621](https://peps.python.org/pep-0621/), and
  [PEP 660](https://peps.python.org/pep-0660/).
- **Pinned upstream tool sources and local CLI observations:**
    - `Nerdbank.GitVersioning` `3.10.94`
      ([`dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb`](https://github.com/dotnet/Nerdbank.GitVersioning/tree/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb),
      pinned in [`.config/dotnet-tools.json`](../../../../../../.config/dotnet-tools.json)):
      [`VersionOracle.cs`](https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/VersionOracle.cs)
      and
      [`ManagedGitContext.cs`](https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/Managed/ManagedGitContext.cs);
    - `hatchling` `1.33.0`
      ([`hatchling-v1.33.0`](https://github.com/pypa/hatch/tree/hatchling-v1.33.0/backend/src/hatchling),
      locked in [`uv.lock`](../../../../../../uv.lock)):
      `hatchling/metadata/core.py` (`ProjectMetadata.core_raw_metadata`),
      `hatchling/builders/plugin/interface.py` (`BuilderInterface.get_build_hooks`
      and `recurse_included_files`), and `hatchling/builders/wheel.py`
      (`WheelBuilder.build_editable_detection` and `get_forced_inclusion_map`);
    - `packaging` `26.3`
      ([`26.3`](https://github.com/pypa/packaging/blob/26.3/src/packaging/version.py),
      locked in [`uv.lock`](../../../../../../uv.lock)):
      `packaging/version.py` (`_cmpkey` and `Version`);
    - Repository-locked `tlaplus` `v1.7.4` (`tla2tools.jar`, `TLC 2.19`, pinned in
      [`mise.toml`](../../../../../../mise.toml)) and internal Workflow Delivery
      consumers
      ([`PythonPreparation.cs`](../../../../../private/app/workflow-delivery/Release/PythonPreparation.cs),
      [`packages.py`](../../../../../private/app/workflow-delivery/Native/Python/packages.py),
      and [`passive.py`](../../../../../private/app/workflow-delivery/Native/Python/passive.py)).

## External Contracts and Responsibility Boundaries

| Boundary                      | External contract and physical behavior                                                                                                                                                                                                                                                                                                                              | Domain obligation                                                                                                                                                                                                                                                 |
| ----------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **NBGV `VersionOracle` JSON** | Emits `VersionFileFound` (`false` with exit `0` when `version.json` is absent or invalid!), `VersionHeight`, `VersionHeightOffset`, integer `VersionHeightPosition` (`2=Build`, `3=Revision`, `4=Prerelease`, `null`), `VersionOptions.GitCommitIdPrefix` (default `"g"`), prefixed `SemVer2`/`NuGetPackageVersion`, and commit-first `InfoVersion` (`+<sha>.meta`). | Require `VersionFileFound = true`, non-negative effective height, and an alphabetic commit prefix for non-public builds; consume structured JSON fields rather than hardcoding `"-g"` regexes; encode `effH` at most once across release, prerelease, and `.dev`. |
| **PEP 440 & PyPA indexes**    | Defines `[N!]N(.N)*[{a\|b\|rc}N][.postN][.devN][+local]` (`packaging.version._cmpkey`), strips trailing release zeros only during comparison (retaining `(1, 2, 0)` in `.release`), normalizes all-digit `+local` segments to integers (`> str`), and forbids `+local` on PyPI and TestPyPI.                                                                         | Preserve strict prerelease polarity (`< final`) and unconditional promotion monotonicity (`nonPublic < public`); expose static `pyproject.toml` channel policy (`local`, `public-index` Buddy, `official-release`).                                               |
| **PEP 517/621/660 & Hatch**   | Hatchling activates a `BuildHook` _only_ via `[tool.hatch.build.hooks.<name>]`, passes `version = "standard"` to both checkout and unpacked-`sdist` wheel builds, treats `self.root/PKG-INFO` as the `sdist` boundary (skipping `VersionSource`), and copies `force_include` into editable wheels when `force_include_editable` is empty.                            | Use `self.root/PKG-INFO` as the `sdist` vs NBGV source mode switch (supporting both monorepo subpackages and `sdist` archives unpacked inside an outer Git worktree), fail closed on Git-less snapshots, and isolate editables.                                   |
| **Subprocess & `.NET` CLI**   | `dotnet` resolves `global.json` and `.config/dotnet-tools.json` upward from subprocess `cwd` (regardless of whether `dotnet tool run nbgv` came from auto-discovery or an explicit/env override), while OS `chdir(cwd)` runs before `execve`.                                                                                                                        | Decouple configuration source from execution mechanism, resolve relative executable overrides against their configuration/shell anchor before shifting `cwd`, and separate `cwd` from `--project`.                                                                |

## Domain Invariants and Impossibility Boundaries

### 1. Version Projection and Order Algebra ([`NbgvPep440Rules.tla`](models/NbgvPep440Rules.tla), [`VersionProjection.tla`](models/VersionProjection.tla))

1. **Strict weak order and canonical isomorphism (`StrictWeakOrderLaw`,
   `CanonicalOrderIsomorphismLaw`, `PreReleasePolarityLaw`):** Over the
   injective canonical vocabulary (`none`, `alpha.0`, `alpha.H`, `beta.H`,
   `beta.1`, `rc.H`, including `effH = 0` from `VersionHeight = 0` or negative
   `versionHeightOffset`), SemVer 2.0 and PEP 440 are strictly order-isomorphic.
   Every prerelease projects strictly below its final release (including two-part
   `1.2-beta.{height}`, where NBGV pads `SemVer2` to `1.2.0-beta.H` and PEP 440
   equates `1.2.0` with `SimpleVersion = 1.2`). Configuring `epoch = 0` is
   extensionally identical to omitting `epoch`.
2. **Pre-release pigeonhole and compound-tag boundary
   (`PermissiveFoldingPreservesSemVerOrder`):** PEP 440 provides only one
   `(tag, num)` prerelease pair (`tag \in {"a", "b", "rc"}`) and one `.dev`
   integer. Admitting numberless `-alpha` alongside `-alpha.0` collapses
   `1.2.3-alpha < 1.2.3-alpha.0` onto `1.2.3a0`; folding `-pre` / `-preview`
   onto `rc` inverts `.NET`'s `preview.2 -> rc.1` progression
   (`1.2.3rc2 > 1.2.3rc1`); and mapping compound `.NET` `-beta.1.{height}` to
   `1.2.3b1.dev{height}` inverts order against `1.2.3-beta.1`
   (`SemVer: beta.1 < beta.1.0`, `PEP 440: b1.dev0 < b1`). Non-injective aliases and unlabelled multi-integer prereleases must fail
   closed unless explicitly structured as `beta.1.dev.{height}` (where
   `dev.{height}` satisfies `1.2.3.devH < 1.2.3a0` and `beta.1.dev.{height}`
   satisfies `1.2.3b1.devH < 1.2.3b1` under PEP 440's pre-phase `.dev` ordering
   rather than SemVer's prefix/ASCII order,
   [`PermissivePreFoldingCounterexample.cfg`](models/PermissivePreFoldingCounterexample.cfg)).
3. **Unconditional promotion monotonicity and `.dev`-slot saturation
   (`UnconditionalPromotionMonotonicityLaw`, `AdmissionAndChannelLaw`):** Across
   both `local` and `public-index` (Buddy / TestPyPI, where
   `PublicRelease = false` is admitted with `local = <<>>`), every admitted
   non-public build sorts _strictly below_ the promoted public release of the
   same commit (`nonPublic(H) < public(H)`), and when effective `Build` (`2`) or
   `Prerelease` (`4`) height advances (`eff1 < eff2`, noting NBGV clamps
   `VersionHeight = 0` to `0` on `Build`/`Revision`), `public(eff1) < nonPublic(eff2)`
   holds as well.
   When a base recipe already occupies PEP 440's bottom `.dev` slot
   (`1.2.3-dev.{height}` or `1.2.3-beta.1.dev.{height}`), for that fixed
   `(epoch, release, pre)` base no valid PEP 440 suffix without lowering
   `dev < H` sorts strictly below `.devH` (`1.2.3.devH` or `1.2.3b1.devH`,
   [`DevSlotSaturationCounterexample.cfg`](models/DevSlotSaturationCounterexample.cfg));
   rather than weakening promotion monotonicity via `1.2.3.devH+g<sha> > 1.2.3.devH`,
   non-public builds of `.dev`-saturated recipes fail closed (requiring projects
   that intentionally emit `.devH` series—such as `nbgv-hatch-demo`—to match
   `publicReleaseRefSpec` or switch base recipes to `alpha`/`beta`/`rc`).
   Explicit `--metadata` (`BuildMetadata`) is admitted in `local` mode (placed
   after any commit tag, `+<prefix><sha>.<meta>`) and rejected on `public-index`
   and `official-release` channels. Because isolated CI/Release callers (such as
   Workflow Delivery's `PythonPreparation`) run `uv build` without forwarding
   ad-hoc policy environment variables and require `local_version == null` on
   non-DryRun Buddy runs, channel policy (`local` vs `public-index`) must be
   declarable statically in `pyproject.toml`.
4. **Public-index parallel-commit trilemma and `Revision`-height monotonicity
   (`RevisionHeightMonotonicityLaw`, `PublicIndexDistinguishesParallelCommits`):**
   On `PosRevision` (`3`) three-part recipes (`1.2.3`, `1.2.3-beta.1`), using
   `effH = VersionHeight + VersionHeightOffset` in `.dev<effH>` preserves causal
   commit ordering across both `local` and `public-index` channels even when a
   later commit's short hash is ASCII-smaller (`"0481029" < "ccf1759"`). Under
   `public-index` (`local = <<>>`), parallel non-public branches at equal
   `(core, prerelease, effH)` necessarily share the same public PEP 440 version
   ([`PublicIndexParallelCommitCounterexample.cfg`](models/PublicIndexParallelCommitCounterexample.cfg)).

### 2. Multi-Field Fidelity, Dictionary Precedence, and Tuple Reconstruction ([`MetadataAndTemplates.tla`](models/MetadataAndTemplates.tla))

1. **Multi-field and prefix semantics (`FieldProjectionFidelityLaw`,
   `ZeroEpochExtensionalityLaw`):** Missing or schema-invalid `version.json`
   files cause `nbgv` to exit `0` with `VersionFileFound = false` (`0.0.0-g...`)
   and must fail closed. Only `SemVer2` (`NpmPackageVersion`) and
   `NuGetPackageVersion` carry NBGV's alphabetic
   `VersionOptions.GitCommitIdPrefix` (default `"g"`, or custom `"u"`):
   `SemVer1` omits the prefix, `AssemblyInformationalVersion` does not
   distinguish non-public from public builds and is rejected when
   `PublicRelease = false`, and lossy core-only selectors (`SimpleVersion`,
   `Version`) strip prerelease tags as well as commit lowering and are admitted
   only when `PublicRelease = true` and `PrereleaseVersionNoLeadingHyphen` is
   empty (preserving strict prerelease polarity across every admitted selector).
   `NuGetPackageVersion`
   retains `--metadata` (`+meta`) when `nuGetPackageVersion.semVer = 2`
   (dropping it only under `semVer = 1`), unpads SemVer 1 numeric tokens
   (`0002 -> 2`), and pads to four release components
   (`EnsureNonNegativeComponents` + `ToStringSafe(4)`, e.g. `1.2.0.0` or
   `1.2.3.2`) whenever `precision = "revision"`, emitting `.dev0` on non-public
   builds rather than encoding height twice. When `VersionHeightPosition` is
   `null` (four-part `1.2.3.4`) or `VersionHeight = 0` on `Build`/`Revision`,
   NBGV ignores `VersionHeightOffset` (`posHeight = 0`). Conversely, when
   `VersionHeight > 0` on `Build`/`Revision` yields
   `VersionHeight + VersionHeightOffset < 0` (where `-1` still exits `0` in NBGV
   via `System.Version`'s `-1` unspecified sentinel, collapsing `1.2` onto
   `1.2.0` or leaving `posHeight = -1` on `1.2.3`) or when `{height}` macro
   substitution goes negative (`1.2.3-beta.-1`), projection must fail closed.
   `AssemblyInformationalVersion` uses four release segments _iff_
   `VersionHeightPosition = 3` and three-part `semCore` otherwise (emitting
   `1.2.0-beta.H+<sha>` with `.release = (1, 2, 0)` on `1.2-beta.{height}`, and
   `1.2.3+<sha>` on `1.2.3.4`), placing the unprefixed commit hash _before_
   explicit `--metadata` (`+481029.meta`, where PEP 440 normalizes the leading
   all-digit segment to integer `481029`).
2. **Dictionary merge order, consumer keys, and tuple fidelity
   (`TemplateAndTupleLaw`):** Base payload aliases must populate the mapping
   _before_ binding synthetic `"version"` (`AliasesFirstDict` vs refuted
   `AliasesLastDict`, where snake_case `"Version" -> "version"` clobbers the
   selected field), while retaining full 40-character `"GitCommitId"` and
   `"PublicRelease"` in `metadata` for downstream consumers
   (`workflow-delivery/Native/Python/packages.py`). In `pep440` tuple mode,
   tuple elements reconstruct `(epoch, release, pre, dev, "+", local)`
   component-by-component (including `(1, 2, 0)` and normalized commit-first
   local metadata); in `nbgv` mode, if retained, `normalized-prerelease`
   normalizes `PrereleaseVersionNoLeadingHyphen` orthogonally from its own
   tokens rather than coupling to `selectedField`, and JSON types pass through
   without coercing all-digit `"0481029"` to `int`. If consumers do not need
   legacy `nbgv` tuple mode or multi-field `version-field` selection, those
   configuration axes are candidates for scope contraction in the authorized
   rewrite Wave.

## Implementation Delegation Boundary

The TLA+ modules represent SemVer tokens, PEP 440 components, and tuples as
typed AST records (`IntV`, `BoolV`, `StrV`) and integer-key projections strictly
to enable finite-state exhaustive checking. The Python rewrite must _not_
translate those AST operators into hand-rolled version parsers: consume NBGV's
structured JSON for domain admission and minimal candidate assembly, then
delegate canonical formatting, validation, comparison, and PEP 440 segment/tuple
decomposition directly to `packaging.version.Version` (covering Python `repr()`
literal escaping and runtime-import-free generated files via focused unit
tests).

### 3. Hatchling Hook Activation, Topology-Safe Provenance, and Editable Isolation ([`BuildLifecycle.tla`](models/BuildLifecycle.tla))

1. **Explicit hook requirement and consumer migration
   (`ReadOnlyAndGitFreeSdistLaw`, `AlternativeBoundaryLaw`):** Hatchling's
   `BuilderInterface.get_build_hooks` loads hooks solely from
   `[tool.hatch.build.hooks.<name>]`; configuring
   `[tool.hatch.version] source = "nbgv"` alone never invokes a build hook and
   drops `.gitignore`-excluded generated files. A redesigned plugin must
   register `hatch_register_build_hook`, bind generated version files to
   `[tool.hatch.build.hooks.nbgv]`, keep `VersionSource` read-only via a shared
   metadata evaluator, reject deprecated `[tool.hatch.version.nbgv.write]`
   inside `VersionSource` during source-checkout evaluation (while noting that
   an already-built `sdist` with `PKG-INFO` and no hook entry never invokes
   either plugin path), and atomically update
   `workflow-delivery/Native/Python/passive.py` in the same authorized Delivery
   Wave.
2. **Monorepo vs outer-Git unpacked-`sdist` provenance
   (`ProvenanceAndArchiveIntegrityLaw`, `DirectVsSdistWheelEquivalence`,
   `AlternativeBoundaryLaw`):** Monorepo subpackages
   (`src/sample/nbgv-hatch-demo`) have no `.git` at `self.root` (relying on
   ancestor `.git`), whereas an `sdist` unpacked inside a scratch directory of
   an outer Git worktree (`<repo>/.tmp/pkg-1.2/`) _does_ have an unrelated
   ancestor `.git` alongside `self.root/PKG-INFO`. `AlternativeBoundaryLaw`
   algebraically refutes requiring `self.root/.git` (which breaks monorepo
   subpackages), rejecting `hasAncestorGit /\ hasRootPkgInfo` (which breaks
   outer-Git unpacked `sdist` builds), and trusting stale disk files without
   `PKG-INFO` (which ships stale versions from Git-less snapshots). Matching and
   trusting Hatchling's native `ProjectMetadata` boundary without compensating
   `.git` heuristics (preserving `prepare_metadata_for_build_wheel` and
   `build_wheel` symmetry), `self.root/PKG-INFO` presence selects Sdist Mode
   (requiring the pre-materialized file via `force_include` without touching
   `.git` or `nbgv`), and `self.root/PKG-INFO` absence selects NBGV Source Mode.
3. **Editable `site-packages` isolation and PEP 660 metadata boundary
   (`EditableNoSitePackagesShadowLaw`, `AlternativeBoundaryLaw`):** When
   `force_include_editable` is empty, Hatchling's
   `WheelBuilder.get_forced_inclusion_map` copies `build_data["force_include"]`
   entries into `site-packages/`, shadowing live working-tree files in `.pth` /
   `dev-mode-dirs` editable installs. Pinned Hatchling's `recurse_forced_files`
   also raises `FileNotFoundError` if a static or hook `force-include` entry
   points to an absent file (`ValidConfigs` excludes forcing `_version.py` when
   `writesFile = false`). Unless static wheel-target `force-include`
   or another hook's `build_data["force_include_editable"]` targets the
   generated file, editable builds (`version = "editable"`) must materialize
   `_version.py` in the working tree via `artifacts` only (`siteCopyVer = 0`).
   Advancing Git HEAD without re-installing editable necessarily leaves
   `.dist-info/METADATA` at the prior commit while source builds refresh
   `_version.py`—an inherent PEP 660 static-metadata boundary
   ([`Pep660EditableDriftCounterexample.cfg`](models/Pep660EditableDriftCounterexample.cfg)).

### 4. Execution Mechanism vs Configuration Source ([`CommandExecution.tla`](models/CommandExecution.tla))

Configuration origin (`explicit`, `env`, `auto`) is orthogonal to execution
mechanism (`standalone-*` vs `dotnet-tool-run`): passing `dotnet tool run nbgv`
via `command` or `NBGV_PYTHON_COMMAND` still requires `SubprocCwd` to lie under
a restored `.config/dotnet-tools.json` and compatible `global.json` SDK
(`MechanismUniformityLaw`). `FailureAndShortcutLaw` algebraically refutes both
inheriting `callerCwd` (which misses the repo's `.NET` manifest/SDK when called
from outside) and shifting `cwd` without anchoring relative overrides
(`./tools/nbgv`) against `configRoot` / `callerCwd`. Without reimplementing
`.NET`'s manifest parser in Python, binding `cwd = targetProjectDir` (with
absolute `--project`) satisfies every monorepo query regardless of `callerCwd`
(`MonorepoGetVersionSuccessLaw`), while querying an external Git repository
lacking a discoverable `.config/dotnet-tools.json` via `dotnet-tool-run` hits
`.NET`'s local-tool boundary and requires a standalone/global `nbgv` binary
([`CrossRepoLocalToolBoundaryCounterexample.cfg`](models/CrossRepoLocalToolBoundaryCounterexample.cfg)).

## Reproducing the Bounded Model Checks

Every model defines pure external mechanics and target contracts without
fault-injection `CONSTANT` switches: tempting implementation shortcuts are
refuted algebraically inside the normal runs, while five separate configurations
on the unmodified `Spec` produce deliberate witnesses (`TLC exit 12`) for
inherent domain impossibility or platform boundaries.

```sh
python eng/scripts/tla.py sany src/public/lib/nbgv-python/docs/design/models/*.tla
mise run tla:tlc -- -workers auto -config src/public/lib/nbgv-python/docs/design/models/VersionProjection.cfg src/public/lib/nbgv-python/docs/design/models/VersionProjection.tla
```

| Model and configuration                                                                            | Finite boundary and checked behavior                                                                                                                                                           | Distinct states |
| -------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------: |
| [`VersionProjection`](models/VersionProjection.tla) ([cfg](models/VersionProjection.cfg))          | Single-point `(shape, offset \in -2..1, recipe, height \in 0..2, pub, commit)` with relational quantifiers: strict weak order, channels, `posH = -1` / `effH = 0` bounds, promotion, revision. |           2,160 |
| [`MetadataAndTemplates`](models/MetadataAndTemplates.tla) ([cfg](models/MetadataAndTemplates.cfg)) | Seven field selectors, `GitCommitIdPrefix`, `VersionFileFound = false`, 4-part NuGet `revision`, commit-first `InfoVersion`, `pep440`/orthogonal `nbgv` tuples, and alias/coercion refutation. |           7,560 |
| [`BuildLifecycle`](models/BuildLifecycle.tla) ([cfg](models/BuildLifecycle.cfg))                   | Bundled `ValidConfigs` across `repo-root`/`monorepo-subdir`, outer-Git unpacked `sdist`, `PKG-INFO` symmetry, missing-forced-file failure, editable isolation, and 6 hook refutations.         |          60,800 |
| [`CommandExecution`](models/CommandExecution.tla) ([cfg](models/CommandExecution.cfg))             | Orthogonal `configSource` vs `EffectiveMechanism` across monorepo/external/non-Git/missing directories, `chdir`-before-`execve` failure taxonomy, and `callerCwd`/relative-exec refutations.   |           7,560 |

| Deliberate boundary configuration                                                                     | Module              | Violated conjecture (`TLC exit 12`) and established domain boundary                                                |
| ----------------------------------------------------------------------------------------------------- | ------------------- | ------------------------------------------------------------------------------------------------------------------ |
| [`PublicIndexParallelCommitCounterexample.cfg`](models/PublicIndexParallelCommitCounterexample.cfg)   | `VersionProjection` | `PublicIndexDistinguishesParallelCommits`: `public-index` (`local = <<>>`) equates equal-height branches.          |
| [`PermissivePreFoldingCounterexample.cfg`](models/PermissivePreFoldingCounterexample.cfg)             | `VersionProjection` | `PermissiveFoldingPreservesSemVerOrder`: `-alpha`/`-alpha.0`, `preview`/`rc`, and `-beta.1.H` invert SemVer.       |
| [`DevSlotSaturationCounterexample.cfg`](models/DevSlotSaturationCounterexample.cfg)                   | `VersionProjection` | `ExistsPep440SlotBelowDevH`: at fixed `(release, pre)`, no suffix with `dev = None` or `>= H` sorts below `.devH`. |
| [`Pep660EditableDriftCounterexample.cfg`](models/Pep660EditableDriftCounterexample.cfg)               | `BuildLifecycle`    | `EditableMetadataTracksCommitWithoutReinstall`: PEP 660 freezes `.dist-info/METADATA` across `HEAD` commits.       |
| [`CrossRepoLocalToolBoundaryCounterexample.cfg`](models/CrossRepoLocalToolBoundaryCounterexample.cfg) | `CommandExecution`  | `CrossRepoWithoutTargetManifestSucceeds`: `dotnet tool run` from `targetProjectDir` needs a target manifest.       |
