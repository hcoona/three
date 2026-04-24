# Wiki Log

This file is the append-only chronological record of wiki activity.

## [2026-04-21] bootstrap | Initialize docs wiki scaffold

- Created the initial `docs/` LLM Wiki structure.
- Added the agent contract, wiki index, overview page, and category guides.
- Reserved `docs/sources/` for immutable sources and `docs/raw/` for immutable
  supporting assets.

## [2026-04-21] query | Audit repository release landscape

- Surveyed the monorepo's C#, Python, and JS/TS projects for release intent and
  publishability signals.
- Added source digests for the root workspace manifests, release-policy
  scripts, representative package manifests, and current C# app publish flows.
- Wrote a repository-wide release analysis covering buddy and official profile
  gaps, OIDC direction, and C# app migration targets.

## [2026-04-21] query | Capture workflow release requirements baseline

- Recorded requirement-phase clarifications from the workflow release
  discussion.
- Tightened the release analysis to require OIDC for all currently known
  supported targets.
- Added a dedicated requirements baseline analysis documenting descriptor
  gating, explicit profiles, unified binaries, and target-specific packaging.

## [2026-04-21] query | Review requirements-phase boundary

- Compared the workflow-release initiative against a standard
  requirements-analysis checklist.
- Identified which current discussion items belong to design rather than
  requirements.
- Added a gap review listing the missing requirement items that should be
  settled before requirements sign-off.

## [2026-04-21] query | Freeze approval and initiation rules

- Recorded the role-based approval model for `buddy` and `official`.
- Captured the self-approval exception for `admin` on `official`.
- Updated the requirements baseline to prioritize manual `workflow_dispatch`
  initiation for the first delivery scope.

## [2026-04-21] query | Freeze rerun and dry-run rules

- Recorded whole-release rerun as a first-delivery-scope business requirement.
- Explicitly left single-target retry out of the first delivery scope and tied
  replay handling to skip detection plus idempotent behavior.
- Added dry-run or validation-only execution as a first-delivery-scope
  requirement.

## [2026-04-21] query | Freeze partial-success handling

- Allowed first-delivery-scope releases to preserve partial success instead of
  forcing transactional rollback.
- Recorded manual remediation as acceptable for the first delivery scope.
- Narrowed the remaining failure-policy gaps to cancellation, supersession, and
  any exceptional rollback cases.

## [2026-04-21] query | Freeze version identity and target-scope principles

- Recorded commit-centric version identity and the rule that `official` is the
  freezing state for a version.
- Allowed explicit `buddy FORCE` before `official`, while keeping target-platform
  constraints authoritative.
- Recorded that the first delivery scope must cover multiple target classes from
  the start.

## [2026-04-21] query | Freeze target-family and transform rules

- Distinguished ecosystem-specific target families instead of using the generic
  term package registry.
- Recorded that the same final target family may still require different
  packaging paths for different project kinds.
- Added target-specific transformation examples such as GitHub Packages scope
  changes versus npmjs canonical package names.

## [2026-04-22] query | Freeze target semantics and acceptance baseline

- Recorded GitHub Release as mandatory for any non-zero-target profile, while
  leaving zero-target profiles legal, with fixed `buddy` = pre-release and
  `official` = release semantics when GitHub Release is declared.
- Clarified that package targets remain project-declared even when GitHub
  Packages supports the ecosystem, and captured the Python exception because
  GitHub Packages does not expose a PyPI registry.
- Clarified that `buddy` and `official` may not share the same package registry
  under the same published package name.
- Clarified that GitHub Packages uses secretless `GITHUB_TOKEN` publication
  rather than OIDC trusted publishing.
- Tightened canonical-build semantics so one build may emit binary and
  package/installer outputs for the same variant without target-by-target
  recompilation.
- Added real-project acceptance requirements, including real `official`,
  promotion, and direct-`official` proof.
- Expanded acceptance to require explicit proof of multi-project dispatch,
  dry-run, rerun including immutable-target partial-success replay, cancellation,
  supersession, approval boundaries including self-approval rules, and real
  GitHub Packages publication when that target is in scope.
- Added lifecycle rules for manual cancellation, same-project same-profile
  supersession, and shared visible handling across `buddy` and `official`.
- Excluded extra manual-remediation closure mechanics from workflow scope and
  kept release initiation manual while assigning Git tag creation to the
  workflow itself.
- Froze replay handling as automatic-only and confirmed there are no exceptional
  first-delivery cases that require automatic rollback.
- Froze the descriptor business-field set and confirmed that the first delivery
  scope will not add target families beyond GitHub Release, NuGet, PyPI, npm,
  and RubyGems.

## [2026-04-22] query | Downgrade supersession to optional native cancellation

- Confirmed from GitHub Actions documentation that runs are not auto-cancelled
  by default; cancellation of in-progress runs requires explicit `concurrency`
  configuration with `cancel-in-progress`.
- Replaced the repo-defined supersession requirement with a narrower rule:
  duplicate-run cancellation is optional and may rely on native GitHub Actions
  concurrency controls when convenient for the same workflow entry point and the
  same commit.
- Froze that this optional duplicate definition is based only on workflow entry
  point plus commit, not on selected project subset or other release inputs.
- This correction supersedes earlier exploratory log wording that treated
  supersession as a stronger requirement.
- Removed supersession proof from the acceptance baseline while keeping manual
  cancellation, rerun, approval-boundary, and immutable-target replay evidence.
- Froze that one workflow-dispatch run may target multiple projects, while
  `buddy` and `official` remain separate workflow entry points.

## [2026-04-22] design | Enter workflow design phase

- Marked the requirements phase as signed off and the initiative as formally
  entering design.
- Added a top-level design-direction page instead of jumping straight into
  descriptor syntax or workflow internals.
- Framed the first architecture-level discussion around whether the system
  should be planner-centric or workflow-centric, with lower-level design
  deliberately deferred until that direction is agreed.

## [2026-04-22] design | Freeze architecture-layer model

- Confirmed planner-centric architecture with GitHub workflows as control plane
  and a repo-owned planner that emits a fully materialized declarative plan.
- Recorded the agreed envelope/graph split and the normalized core entities:
  variant, artifact, publish node, and target-instance snapshot.
- Froze the artifact-side rules around variant-centric production, artifact
  identity, mandatory logical artifact roles, and layered artifact kinds.
- Froze the publish-side rules around target family vs target instance,
  protocol-shaped destination contracts, static target-instance capabilities,
  and explicit target-side projection.
- Recorded project ownership and graph cardinality rules, including multi-target
  artifact reuse and shared target instances across projects.
- Clarified the split between control-plane run envelope and plan envelope, and
  tightened the shared target-instance catalog model so each target instance
  belongs to exactly one family and exactly one destination contract.
- Clarified that shared target instances remain opt-in catalog entries rather
  than repo-default targets, and that execution consumes the planner's frozen
  target-instance snapshots rather than re-reading the catalog.
- Clarified that GitHub Packages is represented through host-specific target
  instances rather than as its own target family, and tightened artifact
  identity wording to use kind family plus concrete kind.
- Explicitly deferred descriptor schema and concrete plan object shape to the
  next design layer.

## [2026-04-23] design | Define descriptor schema and file syntax

- Added a descriptor-schema design page for the author-time release layer.
- Fixed the file inventory to one shared `eng/release/target-instances.yml`
  catalog plus one project-owned `src/**/three.release.yml` descriptor per
  releasable project.
- Defined deterministic descriptor discovery, project and catalog ownership
  boundaries, `family/instance-id` catalog references, and the split between
  file-schema, static repo, and planner-time validation.
- Reduced the top-level open questions to the remaining planner-output design
  work and linked the new page from the wiki overview and index.

## [2026-04-23] design | Tighten shared target-instance catalog schema

- Made the shared `eng/release/target-instances.yml` catalog schema normative at
  the family-specific level instead of leaving `contract` and `destination`
  effectively free-form.
- Froze the current-scope `contract` vocabulary and one-to-one family
  compatibility for `github-release`, `nuget`, `pypi`, `npm`, and `rubygems`.
- Added closed family-specific `destination` shapes and static validation rules,
  including the host-specific owner requirement for GitHub Packages NuGet and
  npm instances.

## [2026-04-23] design | Close projection and contract-compatibility gaps

- Narrowed current-scope project `projection` authoring from an open value object
  to closed family-specific rules: GitHub Release `asset-labels`, npm
  `package-name`, and projection omission for the other current target families.
- Added current-scope contract-to-artifact compatibility rules so static
  validation can deterministically check allowed role/kind tuples and aggregate
  cardinality for `github-release-assets`, `nuget-publish`, `pypi-publish`,
  `npm-publish`, and `rubygems-publish`.
- Updated the architecture and summary pages to reflect that current-scope
  descriptor-side projection semantics are now defined, while plan serialization
  and executor behavior remain deferred.

## [2026-04-23] design | Resolve descriptor path-base and discovery gaps

- Reconciled the descriptor schema's path wording so fixed repo locations remain
  repo-root-relative, while project-descriptor path fields are explicitly
  release-root-relative.
- Tightened file-schema validation to require normalized relative paths per
  field-defined base and clarified that `v1alpha1` has no repo-relative
  project-descriptor YAML path fields.
- Changed descriptor discovery to scan all checked-in `three.release.yml` files,
  reject any that are outside `src/` including under `tests/`, and only then
  continue with first-delivery-scope filtering and normal static validation.

## [2026-04-23] design | Close descriptor identity and source-existence gaps

- Tightened the descriptor schema so `variants[].id` is explicitly unique within
  each project descriptor, matching project-local variant identity.
- Tightened author-time static validation so `source.primary-manifest` and every
  `source.auxiliary-inputs[]` entry must resolve to an existing checked-in file
  under the descriptor's release root.
- Synced the wiki overview and index summaries to reflect only those descriptor-
  layer validation rules.

## [2026-04-23] design | Relax RubyGems catalog host constraint for GitHub Packages

- Corrected the descriptor-side shared target-instance catalog schema so the
  `rubygems` family accepts either `rubygems.org` or host-specific GitHub
  Packages RubyGems destinations, with `owner` forbidden for `rubygems.org` and
  required for `rubygems.pkg.github.com`.
- Synced the schema and overview wording so GitHub Packages remains modeled only
  as host-specific target instances inside the existing `nuget`, `npm`, and
  `rubygems` families, while PyPI remains the only explicitly unsupported
  GitHub Packages registry family in current scope.

## [2026-04-23] design | Close descriptor artifact-identity gap

- Tightened the project-descriptor artifact schema so `artifact.id` remains a
  descriptor-local reference handle rather than the frozen semantic identity.
- Added a normative rule that within one variant the `role` / `kind-family` /
  `concrete-kind` tuple must be unique, matching the frozen architecture tuple
  (`project.id`, variant semantic identity, kind-family, concrete-kind, logical-artifact-role).
- Synced the overview and index summaries to reflect only that descriptor-layer
  validation change.

## [2026-04-23] design | Close descriptor variant-identity gap

- Tightened the project-descriptor variant schema so `variants[].id` remains a
  project-local authoring handle rather than the frozen semantic variant
  identity.
- Added a normative author-time validation rule that compares each variant's
  full `dimensions` key/value set as semantic identity and rejects duplicates
  within one descriptor even when ids differ.
- Synced the overview and index summaries to reflect only that descriptor-layer
  validation change.

## [2026-04-23] design | Sync artifact identity with variant semantic identity

- Corrected the descriptor-layer artifact identity rule so it uses project id, the enclosing variant dimensions map, and the artifact role or kind tuple rather than the local variant handle.
- Clarified that both variant ids and artifact ids remain local authoring handles whose rename alone does not change semantic artifact identity.
- Applied the matching minimal architecture terminology sync so the Group 1 docs no longer imply handle-based artifact identity.

## [2026-04-23] design | Close descriptor capability and manifest-mapping gaps

- Added a closed current-scope mapping from `project.ecosystem` to allowed
  `source.primary-manifest` types, grounded in the in-scope .NET, Python,
  Node, and Ruby projects already present in the repository.
- Tightened the shared target-instance catalog so current-scope capability tuples
  are fixed by family plus destination host instead of being left underconstrained
  at author time.
- Made the package-registry coexistence rule deterministic at static-validation
  time by requiring distinct resolved package identities across `buddy` and
  `official` for every current-scope package target instance.

## [2026-04-23] design | Generalize raw executable concrete kind

- Replaced the misleading descriptor and architecture references to
  `cli-binary` with one general `executable` concrete kind for raw runnable
  outputs.
- Clarified that this single executable kind intentionally covers both CLI
  executables and desktop GUI executables such as .NET `WinExe` outputs.
- Updated the representative WinUI app descriptor example to use
  `concrete-kind: executable`.

## [2026-04-23] design | Define planner output and plan shape

- Added a dedicated plan-shape design page that normatively defines the authoritative `three.release.plan/v1alpha1` top-level object as `api-version`, `kind`, `envelope`, and `graph`.
- Fixed the envelope/graph split at the concrete field level, including selected-project source snapshots in the envelope and normalized variants, artifacts, publish nodes, and target-instance snapshots in the graph.
- Made the plan self-sufficient for descriptor-owned and catalog-owned release intent by freezing resolved catalog contract, destination, and capability data into target-instance snapshots and by normalizing descriptor-handle references to plan ids.
- Updated the descriptor-schema backlinks plus the wiki overview and index so the remaining design questions now move down to workflow and executor layers instead of planner output shape.

## [2026-04-23] design | Close plan-id determinism and rerun-skip boundary

- Added deterministic canonical ID generation rules for `plan-id`, `variant-id`,
  `artifact-id`, and `publish-node-id`, including canonical JSON hashing inputs
  and lexicographic serialized map ordering.
- Added planner-authored per-publish-node `publish-disposition` so immutable-
  target rerun skip decisions live in the frozen plan while raw remote
  observations remain outside target-instance snapshots.
- Synced the architecture, descriptor-schema, overview, and index pages so the
  doc set no longer says the exact plan shape is deferred.

## [2026-04-23] design | Close resolved publish identity plan boundary

- Clarified that each selected publish node serializes planner-resolved external publication identity in the plan: current-scope `release-tag` for GitHub Release or `package-name` plus `version` for package registries.
- Kept raw remote observations outside the plan while stating that immutable-target replay checks refer to the serialized publish identity plus the derived `publish-disposition`, not to re-derived manifest or commit state.
- Synced the descriptor-schema, overview, and index summaries so the plan boundary is now explicit about what is frozen versus what remains out of plan.

## [2026-04-23] design | Define workflow and executor boundaries

- Added a dedicated workflow-and-executor-boundaries design page for the control-
  plane layer on top of `three.release.plan/v1alpha1`.
- Froze the control-plane entry-point and reusable-boundary model as `buddy` and
  `official` entry workflows over one shared orchestration workflow, with one
  build unit per `variant-id` and one publish unit per `publish-node-id`.
- Froze job-to-job handoff boundaries so build units emit per-variant bundles and
  build receipts keyed by plan `artifact-id`, publish units consume only their
  referenced artifacts and target-instance snapshot, and immutable-target skip
  receipts remain control-plane-authored rather than executor-authored.
- Froze control-plane ownership of approvals, concurrency, dry-run gating,
  tagging, runtime wiring, artifact transport, orchestration, and final
  reporting, and explicitly kept descriptor loading, planning, target selection,
  and publish-identity derivation out of executors.
- Updated the architecture, plan-shape, overview, and index pages so this design
  layer is discoverable and the top-level open questions now collapse to
  implementation work.
- Validated the touched markdown files with `pnpm exec prettier --write` and
  `pnpm exec markdownlint-cli2`.

## [2026-04-23] design | Sync replay-satisfaction log wording

- Corrected the chronology so planner-owned `publish-disposition: skip-satisfied` is the generic replay-satisfaction outcome rather than immutable-target-only rerun wording.
- Recorded that this satisfied-skip outcome covers both immutable targets that already satisfy publish intent and same-tag GitHub Release reruns whose existing release already matches the full frozen publish intent: `desired-publish-state.release-state`, authoritative asset set, and asset labels.
- This supersedes earlier log phrasing that framed replay skip behavior mainly as immutable-target rerun handling.

## [2026-04-24] design | Freeze project-scoped release identity semantics

- Updated the requirements baseline, architecture model, plan shape, workflow and executor boundaries, descriptor schema, overview, and index to freeze project-selection semantics for omitted, empty, explicit, and normalized project-id sets.
- Froze current-scope version identity as project-scoped NBGV output and GitHub Release identity as the existing `release/<project-slug>/v<version>` tag shape, using descriptor-owned `project.id` as the current-scope project slug without adding a second tag field.
- Clarified that same-commit multi-project releases map to distinct GitHub Release objects when project slugs differ, and that `official-frozen` is created only by successful official GitHub Release publication for that same project-scoped tag.
- Corrected the workflow boundary so tag orchestration now handles each distinct selected project-scoped release tag rather than implying one shared repository tag per run.
