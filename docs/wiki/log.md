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

## [2026-04-25] design | Record design layering and handoff scope

- Added a dedicated analysis page that frames the current workflow-release design
  corpus in upper-layer, middle-layer, and lower-layer terms for implementation
  handoff.
- Recorded that upper-layer design is effectively closed, while middle-layer
  design still needs a small set of seam contracts to be frozen before the
  handoff can be treated as fully complete.
- Named the current pre-handoff seam items explicitly: selected-commit
  materialization, prior build-receipt durability and lookup, planner-time
  remote-observation auth, `official` `maintain+` trigger enforcement, and the
  closed current-scope artifact-typing vocabularies.
- Updated the wiki overview and index so they no longer imply that all
  cross-layer design seams are already closed.

## [2026-04-25] design | Close middle-layer handoff seams

- Froze current-scope manual dispatch commit selection as branch/tag entry in the
  GitHub UI followed by control-plane pinning to the resolved commit SHA for all
  later planning, build, tag, and publish stages.
- Froze planner-time remote observation to use public reads where possible and
  otherwise only least-privilege `GITHUB_TOKEN` reads for GitHub-hosted surfaces,
  excluding publish credentials and approval-gated environment secrets.
- Froze `official` triggering authorization as an explicit early control-plane
  repository-permission check that fails closed unless the actor has at least
  `maintain`, while keeping later protected-environment approval as a separate
  gate.
- Froze prior build-receipt durability expectations to the platform's default
  GitHub Actions artifact retention window, with immutable proof reuse
  guaranteed only while the relevant records remain unexpired.
- Updated the workflow-boundary, architecture, plan-shape, layering, and
  overview pages so middle-layer design is now treated as closed in current
  scope.

## [2026-04-25] design | Record pre-lower-layer handoff guardrails

- Reviewed the middle-layer design against the waterfall handoff boundary and
  recorded that no blocking upper-layer or middle-layer gap remains before
  lower-layer design.
- Added a pre-lower-layer handoff review that keeps the remaining work at the
  implementation-traceability level rather than reopening architecture:
  acceptance traceability, planner diagnostic-code handling, dry-run build
  policy, and receipt lookup or artifact layout.
- Synced the wiki overview and index so the remaining open items are framed as
  low-level handoff guardrails within the frozen contracts.

## [2026-04-25] design | Add workflow-release low-level design

- Added the lower-layer implementation handoff page for workflow release,
  covering stable workflow filenames for trusted publishing, entry inputs,
  dry-run plus validation-build behavior, planner diagnostic-code registration,
  JSON handoff files, artifact and immutable-proof naming, registry adapter
  obligations, GitHub permission boundaries, tag orchestration, and acceptance
  traceability.
- Recorded the Human-in-the-Loop decision that dry-run defaults to no build while
  an explicit `validation-build` input may run validation-only build units.
- Updated the design-layering page, wiki overview, and index so the lower-layer
  handoff is discoverable and no current-scope lower-layer guardrail remains
  unresolved.

## [2026-04-27] design | Specify first-delivery PyPI live publish

- Tightened the lower-layer workflow handoff so first-delivery `pypi/pypi`
  official publication is selected by `external-oidc-entry-workflow`, hosted by
  `.github/workflows/official.yml`, and still uses the standard
  `publish-request.json` / `publish-result.json` contracts.
- Made the PyPI external setup checklist concrete for project or pending Trusted
  Publisher setup: repository `hcoona/three`, workflow filename
  `official.yml`, environment `release`, and no reusable workflow as the
  configured PyPI publisher.
- Added acceptance traceability requiring real PyPI official publish evidence in
  first delivery and clarified that normal PyPI readiness, credential,
  conformance, or upload failures are not topology-block diagnostics.

## [2026-04-27] design | Align OIDC and PyPI release docs

- Synced the requirements review, design direction, layering, overview, and
  index pages with the first-class OIDC publish topology model.
- Clarified that first delivery includes live `official` PyPI publication through
  the entry-workflow-bound path.
- Kept deferred PyPI multi-wheel or cross-variant support separate from the
  current live PyPI one-wheel-plus-optional-sdist path.

## [2026-04-27] cleanup | Finalize OIDC and PyPI documentation links

- Standardized summary wording around the `publish-topology` value
  `external-oidc-entry-workflow`, entry-hosted publish scheduling, and live PyPI
  first-delivery scope.
- Added cross-links between the OIDC topology research, low-level design, wiki
  overview, index, and deferred PyPI multi-wheel issue record.
- Reconfirmed that future PyPI multi-wheel or cross-variant wheel support remains
  out of current scope and separate from current live PyPI support.

## [2026-05-08] query | Record PyPI OIDC canary outcome

- Updated the workflow release low-level design with the failed PyPI OIDC canary
  evidence from run 25522559257.
- Classified the run as workflow failure but positive official PyPI OIDC
  publish-path evidence, and documented the official public-ref guard plus
  hcoona-release-smoke canary override policy.

## [2026-05-08] query | Defer official PyPI success acceptance

- Recorded that official Python smoke full-success PyPI acceptance is deferred
  until all other validation is complete and the workflow changes have merged to
  `main`.
- Clarified that prior official break-glass development-ref runs are positive
  OIDC path evidence only because PyPI rejected local-version identifiers.
- Noted that the buddy Python smoke has passed and that the final official PyPI
  success run must use a proper NBGV public release ref after merge.

## [2026-05-08] query | Dedicated Release-Smoke Projects

Updated the workflow-release design notes to move live acceptance away from the legacy generic `hcoona-release-smoke` package and onto dedicated `hcoona-release-smoke-*` projects for GitHub Release, NuGet, npm, PyPI, RubyGems, and GitHub Packages.

## [2026-05-09] query | Update smoke GitHub Packages buddy policy

- Recorded that GitHub Packages targets now deliberately allow same-name buddy
  and official smoke package identities.
- Updated the workflow release design notes for NuGet, npm, RubyGems, and the
  dedicated GitHub Packages smoke package buddy publication paths.

## [2026-05-10] design | Simplify GitHub Packages 404 observation

- Recorded that GitHub Packages package API 404 during planner-time remote
  observation is normalized to `absent`.
- Clarified that the publish executor remains authoritative for permission and
  conflict failures, while non-404 observation errors still fail hard.

## [2026-05-10] design | Document npm dual-artifact projection

- Updated the descriptor schema to allow artifact-level npm
  `projection.package-name` and the duplicate npm artifact tuple exception when
  projected package names are distinct.
- Updated the plan shape to freeze artifact-level npm projection, per-artifact
  npm final distribution filenames, projected npm tarballs in build bundles, and
  identity-verification-only npm publication.

## [2026-05-11] query | Extend workflow release smoke coverage

- Updated workflow-release schema and low-level acceptance notes for dedicated
  GitHub Release smoke coverage of .NET executable, Inno Setup installer, and
  WXT browser zip artifacts.
- Recorded that Python application smoke coverage remains intentionally skipped.

## [2026-05-14] query | Probe CI validation artifact enumeration

- Added a Group 1 workflow-release CI affected-validation platform experiment
  record for artifact enumeration, instance counting, run-attempt separation,
  and fixed physical artifact names.
- Recorded GitHub Actions run `25885824704` and its rerun observations,
  including the fact that run-scoped artifact enumeration still returns old
  attempt artifacts.

## [2026-05-14] query | Record CI validation producer identity experiment

- Added the Group 2 GitHub Actions platform experiment for producer and job
  identity, matrix rerun behavior, and writer-observation feasibility.
- Linked the findings to the Group 1 artifact enumeration spike and recorded
  LLD impacts for trusted receipt writers.

## [2026-05-14] query | Record CI validation no-plan failure experiment

- Ran the Group 3 no-authoritative-plan GitHub Actions probe through the
  `Release Buddy` dispatch entry.
- Added a durable experiment page covering missing plan artifacts, readable
  diagnostics, downstream skipped jobs, always-running reporting, and required
  aggregate-check implications for CI affected validation.

## [2026-05-14] query | Summarize CI validation platform spikes

- Added the Group 4 platform-spike summary for workflow-release CI
  affected-validation readiness.
- Synthesized the Group 1 artifact enumeration, Group 2 producer identity, and
  Group 3 no-authoritative-plan experiment records without triggering new
  workflow runs.
- Recorded the implementation-readiness recommendation, validated platform
  assumptions, design constraints, remaining risks, and OA scope-out items.

## [2026-05-23] query | Rebaseline G5 CI batch admission

- Updated the CI affected-validation LLD to remove G5 reliance on producer-side
  batch observation sidecars for live CI gating.
- Clarified that G5 batch bundle admission is internal validation-grade evidence,
  while the G4 trusted-observation seam remains available for future trusted
  observer topologies.

## [2026-05-23] query | Remove self-attested CI batch observations

- Removed the public caller-writable batch observation writer and observation
  manifest consumer path from the CI control script.
- Clarified that future trusted observations must come from a genuine trusted
  observer, not producer-side sidecar artifacts.

## [2026-05-24] query | Clarify G5 aggregate evidence schemas

- Corrected the CI affected-validation LLD dependency-result outcome enum and
  clarified `admitted-for-gating` as evidence admission rather than success.
- Documented downloader-observed unexpected-artifact names and strengthened the
  aggregate metadata trust boundary language.

## [2026-05-24] query | CI validation artifact boundary updates

Updated the CI affected-validation LLD to clarify that matrix execution-batch jobs do not enumerate the artifact API, final aggregation performs live namespace checks, and snapshot artifact names are only allowed when plan-required.

## [2026-05-24] query | Remove selector writer-observation refs

- Clarified that legacy selector-assignment compatibility binds receipt refs and
  writer IDs directly, without requiring, producing, or validating
  writer-observation refs.

## [2026-05-24] query | Attempt-visible CI artifact namespace

- Updated the CI validation artifact enumeration analysis and LLD to describe
  current G5 attempt-visible physical artifact names and current-attempt-only
  namespace reconciliation.

## [2026-05-24] query | Clean G5 legacy surface removal

- Updated the CI affected-validation LLD to state that clean G5 has no selector-assignment, standalone-receipt, receipt-manifest, or writer-observation compatibility contract.

## [2026-05-25] query | Simplify aggregate summary artifact contract

- Clarified that aggregate-summary self-artifact problems discovered after upload
  are post-upload workflow gate diagnostics, not public aggregate summary JSON
  reasons, failure kinds, or final evidence details.

## [2026-05-25] query | Bind release-shaped validation evidence

- Documented `projection-authority` in the aggregate evidence manifest schema.
- Clarified that release-shaped public batch evidence rejects unbound reused receipts and carries per-obligation blocking/skipped detail.

## [2026-05-25] query | Align projection authority schema

- Updated the CI affected-validation LLD so aggregate `projection-authority` documents the implemented mode, validation tree, affected range, request, scheduled-full, and projection digest shape.

## [2026-05-25] query | Clarify CI artifact physical names

- Updated the CI validation artifact enumeration analysis to supersede the
  digest-only physical-name proposal with current attempt-scoped
  `three-ci-validation-{run-id}-{run-attempt}-{sha256(logical-ref)}` guidance.

## [2026-05-25] query | Rebaseline matrix dependency downloads

- Updated the CI affected-validation LLD to describe artifact-ID/API singleton
  downloads and downloader-observed `artifact-metadata.json` for matrix
  inter-batch dependency evidence.

## [2026-05-25] query | Reconcile CI diagnostic detail contract

- Updated the CI affected-validation LLD to remove stale `invalid-plan`
  diagnostic details for request-boundary and execution-batch-manifest failures,
  aligning the documentation with the implemented public registry.

## [2026-05-25] query | Align execution manifest diagnostics

- Updated the CI affected-validation LLD and acceptance fixture wording so
  invalid execution-batch manifests use `required-input-artifact-failure` with
  `inadmissible-batch-evidence` details instead of `invalid-plan` details.

## [2026-05-25] query | Tighten release descriptor identity

- Updated the CI affected-validation LLD so release-shaped descriptor identity
  must be a non-empty string, and clarified execution-batch manifest mismatch
  details under `inadmissible-batch-evidence`.

## [2026-05-25] query | Bind dependency upstream identity

- Updated the CI affected-validation LLD dependency-result schema with trusted upstream artifact and admitted-candidate identity fields.
- Clarified when upstream identity fields may be null or omitted and when authoritative upstream evidence requires non-empty matching IDs.

## [2026-05-26] query | Remove reused receipt authority

- Updated the CI affected-validation LLD to state that public CI batch evidence
  has no reused-receipt authority and release-shaped public source proof is
  admitted only through `evidence-source: no-publish-validation` output.

## [2026-05-26] query | Record runner-family CI topology

- Updated the CI affected-validation low-level design to replace fixed
  execution-batch layers with bounded runner-family orchestrators.
- Clarified exact artifact handling for same-family and cross-runner
  dependencies without adding receipt or writer-observation authority.

## [2026-05-26] query | Tighten CI runner-family orchestrator contract

- Updated the CI affected-validation LLD for runner-family orchestrator slots,
  truthful bundle writer identity, budget-counted batch evidence bundles,
  cross-runner dependency waiting, and cache-spoof fail-closed behavior.

## [2026-05-26] query | Correct CI physical budget semantics

- Updated the CI affected-validation LLD to count physical runner-family
  orchestrator jobs separately from logical execution batches.
- Clarified runner-family orchestrator slot dependency admission and live
  artifact-ID downloads for cross-runner batch bundles.

## [2026-05-26] query | Clarify CI writer identity checks

- Updated the CI affected-validation LLD to distinguish legacy logical batch
  writer fields from physical runner-family orchestrator job and slot evidence.
- Recorded that observed writer identity is recomputed from observed workflow,
  job, and matrix, with orchestrator writers using an empty physical matrix.

## [2026-05-26] query | Tighten CI orchestrator slot schema

- Clarified that orchestrator writer evidence requires a non-empty
  `observed-orchestrator-slot-index`, while `null` is valid only for
  legacy/direct job-context writers.

## [2026-05-26] query | Finalized CI artifact run-attempt admission

- Updated the CI affected-validation LLD for design C: orchestrator artifact-ID state plus attempt-scoped names are the trusted degraded-platform proof when GitHub omits per-artifact `run_attempt`.
- Clarified that a present mismatched artifact attempt fails closed, while an absent attempt may pass only with artifact ID/name/run, namespace, metadata, and payload binding; malicious current control-plane behavior remains out of artifact-admission scope.

## [2026-05-26] query | Clarify CI validation admission trust boundary

- Clarified that CI artifact admission trusts checked-in workflow/control-plane
  code as reviewed code.
- Recorded that orchestrator artifact-ID state manifests are validation-grade
  evidence, not release immutable proof.

## [2026-05-26] query | R8 CI artifact-id state manifest fixes

Updated the CI affected-validation LLD to describe published runner-family artifact-id state manifests as CI-internal trust anchors and reframed the 12-minute performance goal as an observable target rather than a hard correctness ceiling.

## [2026-05-26] query | R9 CI runner-family simplification

- Updated the CI affected-validation LLD to make Windows and Ubuntu orchestrators independent and to forbid cross-family validation batch dependencies.
- Removed the previous peer-family artifact-ID state manifest handoff from the documented dependency-admission model; aggregate remains a workflow-needs fan-in.

## [2026-05-27] query | Tighten CI aggregate downloader admission

- Updated the CI affected-validation LLD to require downloader-produced internal batch admissions before aggregate can verify batch candidates.
- Replaced stale cross-runner dependency text with same-family-only runner-family admission and cross-family fail-closed topology.

## [2026-05-27] query | Rebaseline Release-Shaped CI Batch Compatibility

- Updated the CI affected-validation LLD so release-shaped batch compatibility is
  based on the current validation-only executor profile rather than exact artifact
  or receipt payload shape.
- Clarified that exact artifact refs, descriptor identity, receipt logical role,
  profile labels, artifact shapes, and obligation IDs remain per-selector equality
  checks.

## [2026-06-07] query | Record CI validation implementation plan

- Added a durable implementation-plan handoff for workflow-release CI affected
  validation.
- Captured the difference between the original LLD implementation groups and the
  later governance priority groups.
- Recorded the mandatory independent-review protocol, Group 1
  release-validation authority completion evidence, and future Group 2
  optimization constraints.

## [2026-06-07] query | Add CI validation Group 2 execution plan

- Expanded the workflow-release CI affected-validation implementation plan with
  a waterfall Group 2 execution plan.
- Added current-topology baseline admission rules, bottleneck-analysis phases,
  design decision gates, serialized implementation rules, and acceptance
  evidence requirements for topology/runtime optimization.

## [2026-06-08] query | Record CI validation Group 2 acceptance

- Updated the workflow-release CI affected-validation implementation plan so
  Group 2 and final review are no longer described as future work.
- Added hosted acceptance evidence for run `27111512179`, including job
  durations, topology and artifact caps, aggregate verdict, repair commits, and
  earlier failed hosted evidence.
- Recorded the 35m18s wall-clock timing miss against the 12-minute target and
  kept it visible as a follow-up optimization item dominated by Windows hosted
  validation runtime.

## [2026-06-08] query | Repair CI validation acceptance docs

- Updated the CI affected-validation LLD runner-family schema and mapping text
  to include the accepted macOS orchestrator family.
- Added concise Group 2 review and triage evidence to the hosted acceptance
  package, including raw-clean implementation and group-interface review cycles.

## [2026-06-08] query | Repair CI validation review evidence wording

- Reconciled the CI affected-validation LLD top-level DAG with bounded
  cross-family readiness/admission semantics.
- Replaced stale two-family aggregation wording with runner-family-agnostic
  wording.
- Expanded the Group 2 acceptance evidence into a compact auditable review and
  triage table covering implementation, interface, scheduler, pyrefly,
  documentation, and final global review streams.

## [2026-06-08] query | Record CI validation final global closure

- Updated the CI affected-validation implementation plan to record two
  consecutive raw-clean final global overview rounds after commit `9f1791d`.
- Clarified that this closure-recording update is docs-only, locally
  hook-validated, and covered by the post-hosted docs-only rerun waiver.
- Recorded remaining non-blocking follow-ups: hosted runtime optimization,
  clean A/B runtime baseline only for future runtime-target claims, and clear
  aggregate-summary versus aggregate-job timing evidence.

## [2026-06-08] query | Record CI validation cache follow-up

- Updated the CI affected-validation implementation plan with the first focused
  runtime follow-up: NuGet lockfile caching for NBGV-backed CI validation jobs.
- Preserved the distinction between this workflow optimization, required hosted
  behavior validation, and any future clean A/B runtime-target claim.

## [2026-06-08] query | Correct CI validation follow-up scope

- Clarified that the CI affected-validation follow-up has two parts:
  `actions/setup-dotnet` NuGet lockfile caching and the NBGV/control-plane
  full-checkout repair.
- Recorded that hosted run `27117479657` failed under shallow checkout and that
  `materialize-execution-batches` and `aggregate-evidence` need
  `fetch-depth: 0`.
- Kept hosted rerun validation explicit and did not claim hosted success or
  runtime-target compliance.

## [2026-06-08] query | Correct CI validation source-restoration scope

- Corrected the pending hosted rerun scope to cover the combined state:
  `actions/setup-dotnet` NuGet cache optimization, NBGV/control-plane
  full-checkout repair, and restoration of the 213 legitimate `src/**` files
  accidentally deleted by commit `4fde7f1`.
- Clarified that the prior post-hosted docs-only rerun waiver does not apply to
  this combined workflow/source restoration change.
- Kept hosted validation pending and did not claim hosted success, measured
  speedup, or runtime-target compliance.

## [2026-06-08] query | Correct CI validation planner follow-up scope

- Expanded the pending hosted rerun scope to include staged planner behavior
  changes: runtime output relocation from `.three-workflow-release-planner` to
  `.copilot/three-workflow-release-planner` and removal of root `biome.json` and
  `biome.jsonc` from materialized planner checkouts.
- Restated the full combined follow-up scope as NuGet cache optimization,
  NBGV/control-plane full-checkout repair after hosted run `27117479657`,
  restoration of the 213 legitimate `src/**` files deleted by `4fde7f1`, and the
  two planner behavior changes.
- Clarified that the prior docs-only rerun waiver does not apply to this
  combined workflow/source/planner behavior change, while keeping hosted rerun
  validation pending with no hosted-success, speedup, or runtime-target claim.

## [2026-06-08] query | Correct CI validation config follow-up scope

- Updated the pending combined hosted rerun scope to include staged root config
  hygiene: exact `.editorconfig-checker.json` exclusions for
  `src/private/app/supermemo-mcp/references/Gdip_All.ahk` and
  `src/private/app/supermemo-mcp/references/supermemo_18.ahk`, the narrow
  `.typos.toml` exclusion for
  `src/private/app/supermemo-mcp/references/Gdip_All.ahk`, `biome.jsonc` ignores
  for `.copilot` and `.three-workflow-release-planner`, and the `.gitignore`
  rule for root `/.copilot/` local Copilot agent/tool runtime state, covering
  `/.copilot/three-workflow-release-planner/`.
- Clarified that these config updates are validation/runtime-state hygiene for
  the combined workflow/source/planner/config follow-up, not a runtime-target
  improvement claim.

## [2026-06-08] query | Correct CI validation test follow-up scope

- Expanded the pending combined hosted rerun scope to include staged
  `tests/test_workflow_release_control.py` workflow-release-control test updates
  for `actions/setup-dotnet` NuGet lockfile cache settings and
  NBGV/control-plane full-checkout assertions.
- Kept hosted validation pending and did not claim hosted success, measured
  speedup, or runtime-target compliance.

## [2026-06-08] query | Record combined CI validation hosted success

- Updated the CI affected-validation implementation plan to record hosted
  success for combined follow-up commit `4dd8d8e` via run `27164117754` at HEAD
  `4dd8d8eae0083416066bc803e06bdfd5d471e5a0`.
- Preserved earlier baseline run `27111512179` as separate historical Group 2
  evidence and dispositioned failed run `27117479657` as the shallow
  checkout/NBGV failure repaired by restoring control-plane `fetch-depth: 0`.
- Recorded actual timing for run `27164117754`: 38m20s wall-clock,
  34m08s Windows orchestrator, and 2m37s `aggregate-evidence`; the 12-minute
  runtime target remains unmet and non-blocking.

## [2026-06-08] query | Record combined CI validation audit closure

- Added durable closure records for the combined
  code/source/planner/config/test/doc follow-up ending in commit `4dd8d8e` and
  the hosted-success docs update ending in commit `a41ae36`.
- Recorded the serialized `gpt-55-coder` implementation path,
  `gpt-55-reviewer` adversarial review rounds, independent TP/FP triage, TP
  repair categories, known FP dispositions, and two raw-clean implementation and
  group-interface rounds for each closure record.
- Preserved hosted success evidence for run `27164117754`, prior historical
  evidence `27111512179`, and the remaining non-blocking runtime follow-up
  wording while clarifying that only the current final global two-clean loop
  remains before final closure.

## [2026-06-09] query | Release OIDC split topology alignment

- Updated release topology wiki pages to supersede the earlier PyPI/npmjs
  entry-workflow trusted-publisher guidance.
- Recorded that PyPI, npmjs, and RubyGems.org token-minting jobs run in
  `.github/workflows/release-orchestrate.yml` with environments `pypi`,
  `npmjs`, and `rubygems`, and aligned attestation wording with
  `actions/attest-build-provenance` jobs before GitHub Release upload.

## [2026-06-19] query | Tighten release entry guards

- Updated the workflow-release low-level design to require exact reusable caller
  identity checks for reserved `official` and `buddy` channels.
- Recorded checked-in buddy target authorization: non-empty buddy targets must
  be reachable from a policy-authorized branch ref for the selected project and
  channel.

## [2026-06-26] query | Align hosted CI evidence docs

- Recorded that `.github/workflows/docs/DESIGN.v2.md` remains required hosted
  validation evidence for the next commit.
- Kept unrelated workflow-doc scratch files out of scope while preserving the
  hosted CI evidence alignment updates in the wiki index and overview.

## [2026-06-26] query | Record batch timing evidence contract

- Updated the CI affected-validation low-level design to describe timing-bearing
  batch evidence bundles as `v1alpha2`.
- Recorded selector, command, and runner-family orchestrator dependency-selection
  timing evidence requirements for hosted analysis.

## [2026-07-10] query | Establish delivery architecture glossary

- Added a working ideal-architecture glossary for the peer CI Qualification and
  Release Delivery systems.
- Recorded the Shared Foundation and Delivery Governance boundaries.
- Confirmed Component, Release Unit, and Qualification Target as the core object
  model.
- Recorded that Release rebuilds its artifacts and independently reruns required
  quality checks instead of reusing pull request artifacts or CI results.

## [2026-07-10] query | Confirm authority promotion model

- Added the Trusted Decision Kernel, Authority Epoch, Candidate Authority, and
  Atomic Authority Promotion terms.
- Set complete pull-request testing and atomic activation on merge as the
  default authority-upgrade path.
- Limited mandatory post-merge shadowing to governance or platform behavior
  that cannot be adequately exercised before merge.

## [2026-07-10] query | Confirm runtime trust zones

- Defined separate Decision, Build and Qualification, and Side-Effect runtime
  zones.
- Prohibited publication capability in environments that execute target code.
- Prohibited target-code execution in environments that receive publication
  capability.

## [2026-07-10] query | Decouple CI and Release runtime state

- Confirmed that CI Qualification and Release Delivery do not consume each
  other's plans, evidence, artifacts, status checks, or decisions.
- Kept domain identities, quality definitions, build specifications, ecosystem
  capabilities, and provenance primitives as shared alignment mechanisms.
- Assigned release target eligibility to Delivery Governance rather than CI
  runtime state.

## [2026-07-10] query | Align CI and Release build definitions

- Defined a shared Build Definition with system-owned immutable Build Requests.
- Required CI to build every publishable variant of an affected Release Unit.
- Kept CI and Release artifacts, revisions, version identities, evidence, and
  decisions separate.

## [2026-07-10] query | Define Buddy as preview delivery

- Defined Buddy as a distributable, non-authoritative preview release channel.
- Required isolated preview destinations, identities, and capabilities.
- Kept Buddy on the shared Release planning and execution path while forbidding
  promotion of Buddy artifacts or evidence to Official.

## [2026-07-10] query | Define Official authoritative delivery

- Limited live Official publication to revisions reachable from a
  Governance-configured authoritative branch.
- Bound Official authorization to a frozen Release Plan and artifact digests.
- Allowed non-authoritative branches to exercise Official dry-run behavior
  without production publication capability.

## [2026-07-10] query | Define immutable release history and remediation

- Defined immutable Official and Buddy release identities and idempotent replay
  expectations.
- Added Break-Glass Remediation as a separately authorized operational process
  rather than a normal Release Intent force flag.
- Required expected-state checks, destination capability enforcement, scoped
  remediation capability, and append-only before-and-after audit records.

## [2026-07-30] query | Confirm CI and Release identity binding

- Bound pull-request qualification to the current base, head, and tested merge
  commit SHAs, with corresponding merge-queue and push identities.
- Bound Release execution to the target commit, trusted authority commit,
  Release Unit, frozen plan digest, artifact digests, and plan-specific
  authorization.
- Kept branches, tags, and workflow run identifiers as indexes rather than
  authoritative source identities.

## [2026-07-30] query | Require closed qualification targets

- Required CI to close changed paths over Components, Release Units, variants,
  global configuration, and control-plane obligations before execution.
- Required Release to close build dependencies, variants, quality obligations,
  compatibility obligations, and destinations before execution.
- Made unresolved scope explicitly blocking rather than implicitly excluded or
  delegated to executors.

## [2026-07-30] query | Freeze plan semantics at execution

- Allowed executors to resolve locked dependencies, enumerate selected tests,
  locate declared outputs, inspect remote state, and adapt runner paths.
- Prohibited executors from changing Components, Release Units, variants,
  obligations, versions, artifacts, destinations, or authorization.
- Required runtime conflicts with the Plan or Build Definition to fail rather
  than trigger replanning.

## [2026-07-30] query | Define lightweight evidence admission

- Required exact correlation of execution results to candidate or target
  commits, authority, plans, obligations, producers, attempts, runners, and
  artifact digests.
- Kept Evidence Admission lightweight by prohibiting command re-execution or
  duplicate quality-result interpretation in the Decision Zone.
- Reserved additional digest and provenance verification for high-risk
  side-effect boundaries.

## [2026-07-30] query | Define structural success conditions

- Added explicit Plan readiness, obligation disposition, and obligation outcome
  states.
- Required every mandatory obligation to have admitted successful Evidence.
- Made skipped, cancelled, timed-out, missing, unknown, and conflicting states
  non-successful by construction.
- Limited diagnostics to explanation rather than verdict derivation.

## [2026-07-30] query | Make final decisions append-only

- Defined immutable Final Decision records bound to identity, authority, plans,
  Evidence Sets, obligation outcomes, and verdicts.
- Required reruns and late Evidence to create new Decisions.
- Treated GitHub required checks as projections of the latest authoritative
  Decision rather than the durable audit record.

## [2026-07-30] query | Bind publication to scoped capabilities

- Required Delivery Governance to grant publication authority externally from
  qualification and planning.
- Bound short-lived Capabilities to channels, commits, plans, artifacts,
  destinations, actions, validity windows, and attempts.
- Isolated CI, Buddy, Official, dry-run, and Break-Glass Remediation authority.

## [2026-07-30] query | Define whole-release replay and reconciliation

- Selected whole-release replay instead of GitHub failed-job resumption.
- Required each replay to rerun planning, build, qualification, authorization
  checks, and reporting while skipping only remotely satisfied side effects.
- Defined partial publication as an append-only per-destination Saga requiring
  reconciliation rather than automatic rollback.
- Recorded bit-for-bit reproducible Release builds as a Release Unit business
  contract without adding duplicate-build certification to the delivery system.

## [2026-07-30] query | Normalize remote-state observation

- Made destination observation a normal planning step for every Release Attempt,
  including the first attempt and whole-release replay.
- Limited reconciliation to partial, unknown, conflicting, or unprovable remote
  state.
- Removed any requirement for a dedicated post-cancellation reconciliation
  workflow.

## [2026-07-30] query | Define CI and Release concurrency

- Allowed CI to cancel superseded candidate runs.
- Serialized Release by Official canonical identity or Buddy preview identity
  without cancelling in-progress execution.
- Required Remediation to share the affected Release and destination locks.
- Rejected or coalesced duplicate pending requests instead of relying on an
  unbounded native GitHub concurrency queue.

## [2026-07-30] query | Classify caches as non-authoritative

- Classified package, tool, and build caches as performance mechanisms rather
  than correctness dependencies.
- Allowed tools to use cache whenever available without requiring an explicit
  cache-disabled execution mode.
- Required continuous cache unavailability to leave scope, Evidence, and verdict
  semantics unchanged.

## [2026-07-30] query | Require just-in-time publication capability

- Allowed build and qualification to complete without publication credentials.
- Requested OIDC or equivalent publication capability only when a Side-Effect
  action needs it, without a separate availability probe.
- Prohibited fallback to long-lived tokens, alternate environments, or alternate
  workflow identities when capability acquisition fails.

## [2026-07-30] query | Separate audit records from telemetry

- Classified Plans, Evidence, artifact identities, Decisions, and Receipts as
  authoritative delivery records.
- Required a Release Attempt to stop before further side effects when a
  publication Receipt cannot be persisted.
- Allowed optional metrics, diagnostics, dashboards, and notifications to fail
  without changing correctness.

## [2026-07-30] query | Adopt platform-native record retention

- Recorded the public-repository GitHub Actions retention ceiling and the
  workflows' current 30-day operational retention.
- Assigned longer-lived identity and provenance to Git tags, registries, GitHub
  Releases when selected, and GitHub Artifact Attestations.
- Deferred any external Durable Release Ledger or universal GitHub Release audit
  anchor until a concrete compliance requirement exists.

## [2026-07-30] query | Confirm ordinary CI latency SLO

- Retained a P95 12-minute Final Decision objective for ordinary pull requests.
- Measured broad authority, policy, toolchain, and multi-Release-Unit changes
  separately.
- Prohibited latency optimization from reducing required obligations, variant
  coverage, or Evidence Admission.

## [2026-07-30] query | Define shared delivery extension points

- Defined Repository Model Provider, Build Adapter, Quality Adapter, and
  Destination Adapter boundaries.
- Kept CI Qualification and Release Delivery as independent aggregate roots.
- Limited Trusted Decision Kernel changes to new cross-system authority
  semantics rather than ordinary ecosystem or destination additions.

## [2026-07-30] query | Make decision explanation contractual

- Required CI Decisions to explain path, Component, Release Unit, obligation,
  variant, Evidence, and verdict relationships.
- Required Release reports to explain identity, artifact, destination,
  observation, action, Receipt, authority, and recovery relationships.
- Kept human GitHub summaries and machine-readable reports as projections of one
  model.

## [2026-07-30] query | Synthesize workflow delivery target architecture

- Added the confirmed ideal top-level architecture for peer CI Qualification and
  Release Delivery systems.
- Integrated Delivery Governance, Trusted Decision Kernel, runtime trust zones,
  shared adapters, independent aggregate roots, Buddy and Official semantics,
  whole-release replay, and platform-aware retention.
- Defined one logical Release Plan lineage with immutable Qualification and
  Publication snapshots.

## [2026-07-30] query | Version workflow delivery documentation

- Established explicit v1, v2, and v3 architecture entry points.
- Moved active target architecture and glossary under the normative `v3/`
  directory.
- Archived v2 by immutable commit instead of copying superseded normative design
  into the clean v3 line.
- Added rules for porting platform observations and mechanism assets while
  rewriting requirements, design, runbooks, and implementation plans.

## [2026-07-30] query | Separate v3 requirements from high-level design

- Replaced the mixed target architecture page with a requirements baseline and
  a separate high-level design.
- Added stable requirement identifiers and explicit requirements-stage exit
  criteria.
- Preserved the confirmed peer-system architecture, shared foundation,
  governance boundary, trust zones, Release Plan lineage, and recovery model in
  the HLD.
- Reorganized the next design stage into bounded-context MLDs followed by a
  brief LLD for the first vertical slice.

## [2026-07-30] query | Confirm workflow delivery v3 requirements

- Interactively reviewed and accepted all 67 requirements across mission,
  system boundaries, CI, Release, channels, authority, trust isolation,
  Evidence, recovery, concurrency, retention, quality attributes, and
  non-goals.
- Promoted the requirements page from draft to the confirmed normative
  baseline.
- Closed the requirements gate for entry into middle-layer design.

## [2026-07-30] query | Remove the unnecessary Component domain layer

- Tested the Component abstraction against concrete .NET, global build input,
  and cross-ecosystem dependency scenarios.
- Found that Project Nodes, dependency and path-impact facts, Build Definitions,
  and Release Units already express the required CI and Release behavior.
- Removed Component as a core domain object and retained Project Nodes as
  discovered technical facts rather than authored governance objects.
- Added the architecture rule that a domain abstraction requires independent
  behavior, identity, lifecycle, or policy responsibility.

## [2026-07-30] query | Define the Repository Model and Release Unit MLD

- Defined colocated Release Unit discovery from the target Git tree without
  porting v2 Project/Profile semantics.
- Made ecosystem-native Project Nodes and dependency graphs technical facts
  rather than user-authored business objects.
- Derived Release Unit build closure from Build Definition entry points instead
  of manually maintained project membership.
- Established NBGV as the sole canonical version authority.
- Kept intermediate-output reuse and cross-artifact binary consistency as
  acceptance-tested Build Adapter invariants rather than authoring policy.

## [2026-07-30] query | Simplify Decision Kernel authority

- Removed the independent Authority Epoch, Candidate Authority, and dual-revision
  promotion model.
- Bound CI decision code to the tested candidate revision and Release decision
  code to the exact protected target revision.
- Made GitHub owner review, protected refs, environments, and OIDC the external
  authority boundary.
- Accepted that a Release control-code fix creates a new target/version; an old
  target cannot use newer control code through ordinary replay.
- Removed initial record schema versioning because all machine consumers are
  within the same revision and attempt.

## [2026-07-30] query | Remove the top-level Decision Kernel layer

- Found that the same-revision Kernel had no independent trust, deployment,
  identity, lifecycle, or policy responsibility.
- Moved planning, Evidence Admission, and finalization into the CI and Release
  bounded contexts that own those decisions.
- Retained canonicalization, digest, strict record validation, and exact
  Evidence binding as Shared Foundation mechanisms.
- Replaced the Kernel MLD stage with Governance Integration and Shared Decision
  Primitives.

## [2026-07-30] query | Define Governance Integration MLD

- Made GitHub Rulesets, CODEOWNERS, Environments, workflow permissions, OIDC,
  and destination trust the native authority sources without a repository
  mirror.
- Restricted publication authority to destination-specific side-effect jobs and
  isolated Buddy from Official identities and destinations.
- Prohibited runtime re-adjudication of platform reviews and governance state.
- Defined Agent-guided rollout inspection, human-required gates, and limited
  safe smoke scenarios instead of a permanent automated governance audit
  system.
- Deferred Shared Foundation extraction until CI and Release MLDs establish
  concrete shared mechanisms.

## [2026-08-03] query | Define CI Qualification MLD

- Separated root-authoritative HK source-tree conformance from model-driven
  affected-system qualification.
- Made HK one opaque required composite obligation and removed any dependency
  on HK plan or per-step machine output.
- Defined ecosystem-specific, semantically versioned quality presets and custom
  project policy with nearest-ancestor ecosystem selection.
- Closed typed reverse Project Node impact, supporting test targets,
  provider-native dimensions, affected Release Unit variants, obligation DAGs,
  identity-preserving batching, and candidate-scoped Evidence.
- Restricted the authoritative Finalizer to required obligations and assigned
  advisory results to a separate non-authoritative Reporter.
- Defined explicit incremental and full-validation modes, supersession,
  fail-closed planning, and the ordinary pull-request P95 12-minute cohort.
- Corrected the HK source page after HK 1.38.0 verification showed that
  `--plan --json` is not a reliable machine interface.

## [2026-08-04] query | Define Release Delivery MLD

- Defined channel-specific Release identity, append-only whole-release
  Attempts, and a separate dry-run simulation lifecycle.
- Required every channel to build the complete Release Unit artifact variant
  set and execute an independent all-required Release quality policy.
- Split each Attempt into immutable Qualification and Publication snapshots,
  with channel approval captured by an Authorization Record bound to the exact
  Publication Snapshot digest.
- Assigned logical projections to Release Units, mechanics to Destination
  Adapters, and publication Capability to Delivery Governance.
- Defined projection-atomic read-only observation, parallel independent
  capability groups, ordered fail-stop actions within each group, and
  per-action Receipts.
- Defined `in-progress`, `replayable`, `reconciliation-required`, and
  `completed` Release Execution states with whole-release replay and separate
  Break-Glass Remediation.
- Replaced the earlier strong Release-lock terminology with GitHub execution
  serialization and documented the governed single-writer assumption and its
  residual external-writer risk.
- Required final artifact bytes to be frozen before publication and excluded
  byte-changing signing or notarization from the initial scope.
- Incorporated independent architecture and platform review by moving external
  provenance after channel approval, requiring approval for exact-satisfied
  no-op Releases, separating stable Release Identity from Attempt bindings,
  correcting platform Capability scope claims, and requiring per-action Receipt
  persistence before later mutations.
- Closed final review findings by removing the remaining selected-variant
  wording and requiring post-approval expected-state revalidation immediately
  before Break-Glass Remediation mutation.
- Defined a non-live Buddy simulation projection identity so dry-run version
  projection is deterministic without reserving or colliding with live Buddy
  identity.
- Aligned the HLD to per-mutation Receipts and clarified that protected-target
  control code is required for live Release, while dry-run uses its exact
  selected simulation revision without live authority.

## [2026-08-04] query | Define Shared Foundation MLD

- Defined Shared Foundation as a logical mechanism layer without an aggregate
  root, independent runtime service, scheduler, authorization, universal record
  model, or Finalizer.
- Separated read-only Providers from closed-invocation Build and Quality
  Adapters and introduced pure versus target-evaluating Provider trust modes.
- Assigned the shared Repository Model Compiler, static Definition catalogs,
  mechanical outcomes, execution classes, generic clients, Artifact References,
  and internal provenance primitives to Foundation.
- Required CI and Release to wrap Mechanical Results into independently owned
  and admitted Evidence, Observation Records, and Receipts.
- Allowed transparent cross-context cache reuse while requiring Release to
  rematerialize outputs, recompute digests, and create new Release provenance.
- Moved destination projection, action, Receipt, replay, and remediation
  semantics to Release Delivery while retaining only generic client primitives
  in Foundation.
- Kept same-revision internal records unversioned and introduced explicit
  versioning only for intentional cross-revision exchange contracts such as
  reconciliation requests consumed by current remediation code.
- Incorporated independent review by unifying Provider output as Fact Bundle,
  removing stale destination and Evidence ownership wording, documenting
  accepted Provider semantic-completeness obligations, restricting
  cross-context compiler caches by writer trust and provenance, and binding
  authenticated clients to allowlisted capability origins and operations.
- Closed final review gaps by separating in-process Provider Results from
  isolated Fact Bundle transport, requiring a closed Provider Request Manifest,
  expanding owner-review coverage to executable mechanisms and catalogs, and
  admitting protected producer plus original Release lineage before
  cross-revision remediation.

## [2026-08-04] query | Establish Workflow Delivery v3 AI handoff

- Added a root Agent-instruction router to the tracked v3 handoff.
- Recorded that requirements, HLD, and all five MLDs are confirmed and corrected
  their stale draft labels.
- Froze `hcoona-release-smoke-npm` as the first vertical slice with CI
  Qualification, live Buddy publication to GitHub Packages, and Official npmjs
  dry-run.
- Captured the waterfall gates, interactive decision protocol, architecture
  guardrails, scope and security discipline, commit sizing, scenario-oriented
  testing, HK validation, and multi-agent review with independent TP/FP triage.
- Required future phase and slice changes to update the handoff and navigation
  pages without turning the handoff into a second normative architecture.

## [2026-08-05] query | Reconfirm Buddy and Official release identity model

- Reopened and reconfirmed the Release MLD identity decision before LLD.
- Made frozen native NBGV ecosystem versions, including npm
  `npmPackageVersion`, the unchanged published product versions.
- Defined Buddy and Official isolation through complete channel, destination,
  package-coordinate, and Capability boundaries rather than Intent-derived
  version components.
- Kept distinct admitted requests as separate Intent records while routing the
  same Buddy channel, Release Unit, and target into new independent Attempts
  within one Release Execution.
- Made Buddy Release Execution Identity channel, Release Unit, and immutable
  target. Different targets create separate Executions and serialize overlapping
  live actions on complete Adapter-declared mutable-resource keys.
- Distinguished Official Product Identity, Release Execution Identity, and
  independently derived live-action resource keys. Package actions include
  channel, destination, package, and version plus any additional
  Adapter-required keys.
- Defined Official Product Identity as channel, Release Unit, and canonical NBGV
  version, with Official Release Execution Identity adding immutable target.
  Different targets may share Product Identity but remain separate Executions.
  Official ecosystem publication and dry-run use the exact frozen native NBGV
  projection unchanged.
- Defined Attempt identity as Release Execution Identity plus `github.run_id` and
  `github.run_attempt`; originating Intent and request identity are required
  immutable bindings, not additional identity components.
- Clarified that replay preserves Product and Execution identities while
  compiling a new request-local Repository Model Snapshot and creating new
  Attempt-specific Qualification and Publication snapshots.
- Required observation to compare each logical projection with snapshot-bound
  desired projection state rather than Product or Execution Identity. Exact
  state produces an approved no-op; conflicting, differently owned, or
  unprovable package state fails closed without importing prior Attempt records.
- Clarified that a coordinate is an external address rather than an Intent
  reservation. No retained lineage plus destination absence is legitimate
  initial-publication state, and pre-mutation failure burns nothing.
- Required registry Adapters to prove atomic non-overwriting creation and
  durable exact-state observation. An incapable GitHub Packages destination is
  unsupported or blocked; no tag witness, binding index, application lock, or
  permanent ledger substitutes for the destination contract.
- Required every admitted, non-coalesced request to create a distinct Attempt.
  A pending dispatch replaced or coalesced before execution is not admitted and
  creates no Attempt.
- Defined external package coordinate strictly as channel, destination, package,
  and version and excludes Release Unit and target. Package serialization
  includes that exact coordinate plus any additional Adapter-required keys.
- Removed External Package Coordinate and projection-set digest from Buddy
  business identity. Native NBGV version and the complete deterministic
  destination projection set are Plan and Snapshot bindings derived from
  target-controlled definitions.
- Required every live Destination Adapter mutating action to declare complete
  deterministic mutable-resource keys. Publication Snapshots and action
  manifests bind the keys, overlapping actions serialize, package actions
  include exact External Package Coordinate, non-package keys are
  Adapter-defined, and remediation reuses exactly the complete frozen original
  action key set without deriving it from Product or Execution Identity.
- Made target-bound canonical NBGV facts and required native ecosystem
  projections authoritative Repository Model outputs transported through
  Provider Results and Fact Bundles. Plans and Build Requests select and freeze
  the exact projection; Build Adapters apply and verify it without NBGV
  recomputation, alternative derivation, or fallback.
- Required every candidate run attempt to branch to live Release or release
  simulation before live eligibility, identity lookup, coalescing, or admission.
  Each branch compiles exactly one same-revision, purpose-bound request-local
  Repository Model Snapshot and reuses it throughout the resulting live Attempt
  or simulation pass. A replay or other new run attempt compiles a new Snapshot
  even when request identity, run ID, and target remain unchanged. Cross-purpose
  and prior-attempt artifacts are rejected.
- Defined separately namespaced, request-scoped Simulation Identity and
  purpose-discriminated Snapshot bindings. Simulation may emit hypothetical
  requirements, actions, and an outcome but cannot contain or acquire live
  Product, Execution, or Attempt identity, Authorization Record, capability,
  Receipt, or mutation. The simulation-purpose Repository Model Snapshot binds
  validated request/run, target, channel, Release Unit, version, producer, and
  control facts before the Planner derives Simulation Identity; it never binds
  that future Identity.
- Assigned complete Official dry-run planning exclusively to Release simulation;
  CI retains only artifact-shape and validation-only work without publication
  authority.
- Required successful approval before creating the Authorization Record consumed
  by capability groups. Denied, canceled, or timed-out approval instead produces
  platform-derived terminal Approval Outcome Evidence and no Capability. If
  neither a valid Authorization Record nor admissible terminal Approval Outcome
  Evidence exists, approval state is unknown and the outcome is
  approval-contract failure.
- Restricted Qualification to declaring Capability requirements. Only an
  authorized side-effect capability group may request destination Capability
  after validating the Authorization Record and exact Publication
  Snapshot/action bindings.
- Pre-admission compilation closes descriptors, technical graph, Build
  Definitions, modeled variants/outputs, canonical/native NBGV facts, and
  build/artifact scope; failure creates no Attempt. Live Attempt planning
  selects and freezes native projections and deterministic publication basis in
  the Qualification Snapshot. Actual actions, inputs, complete mutation key
  sets, groups, capabilities, and Receipt contracts freeze in the Publication
  Snapshot after build, qualification, and observation.
- Clarified that Buddy Execution Identity ignores version even though
  pre-admission compilation computes `npmPackageVersion`. Attempt planning
  selects and freezes that authoritative Snapshot fact before deriving package
  coordinates and projections.
- Required remote observation to record artifact digests and stop when
  destination state conflicts with the current Attempt's snapshot-bound desired
  projection state rather than an identity-level comparison.
- Rejected a permanent global Official Product Identity-to-target ledger.
  Different target-specific Executions serialize on destination resource keys,
  and durable destination state determines absent, exact, or conflict.

## [2026-08-06] query | Accept bounded first-slice Buddy publisher risk

- Reopened and reconfirmed the live Buddy trust decision before LLD for
  `hcoona-release-smoke-npm` only.
- Allowed any same-repository ref selected by `workflow_dispatch` to supply the
  exact same-revision workflow, control, Planner, Finalizer, and publisher code
  without protected-ref or CODEOWNERS eligibility.
- Kept exact Publication Snapshot creation and dedicated protected Buddy
  Environment approval mandatory before package-write Capability exists.
- Bound the approved target-revision side-effect job to short-lived
  `GITHUB_TOKEN` with minimum `packages: write`, no PAT fallback, and no
  `id-token: write`.
- Recorded that Environment approval is human trust elevation rather than
  cryptographic or independent semantic validation and that no independent
  protected publisher constrains malicious target code after approval.
- Accepted bounded risk of arbitrary or malicious bytes, reachable namespace or
  version squatting, registry clutter/cost, and package-operation abuse within
  the token's repository/package permissions.
- Required the exact disposable smoke package and isolated GitHub Packages
  destination, separate Buddy Environment, minimum access, reviewer-visible
  target/ref/coordinate/artifact/lifecycle/action details, self-review prevention
  where available, no normal consumers, and Break-Glass delete/restore handling.
- Preserved strict Official protected-ref, owner-review, Environment, and
  destination-trust requirements. CI governance is unchanged, and future Buddy
  destinations do not inherit this exception.
- Kept activation conditional on proving that GitHub prevents selected-ref code
  from obtaining `packages: write` outside the dedicated Environment-gated job;
  otherwise the slice is blocked.

## [2026-08-06] query | Finalize first-slice repository-writer publisher TCB

- Superseded the earlier impossible Environment permission-ceiling assumption.
  GitHub Environment remains mandatory for the normal live Buddy workflow after
  exact Publication Snapshot creation, but does not prevent a malicious
  repository writer from authoring alternate jobs with `packages: write`.
- Placed every repository actor with Write, Maintain, or Admin access inside the
  bounded first-slice Buddy trusted publisher TCB. External/fork contributors
  and actors without repository write remain outside it and cannot normally
  dispatch the live path.
- Kept same-revision execution from any selected same-repository ref. The normal
  capability job alone requests short-lived `GITHUB_TOKEN` with minimum
  `packages: write`, no PAT, and no `id-token: write`.
- Defined Environment approval as protection against mistakes, accidental
  publication, and ordinary process violations rather than a malicious-writer
  security boundary or independent semantic validation.
- Allowed optional workflow-execution protections only as defense in depth, not
  as a required dependency or per-job permission ceiling.
- Forbade planned and ordinary delete, restore, permission, visibility, and
  admin actions while acknowledging latent repository/package admin authority
  as accepted trusted-writer misuse risk.
- Added repository-writer membership revalidation. If any
  Write/Maintain/Admin actor is not trusted to publish, the live slice blocks
  until that actor's access is reduced below those roles or an independently
  enforced publisher boundary makes package-write Capability and destination
  access unavailable to writer-authored workflows. Ref narrowing, Environment
  branch restrictions, CODEOWNERS, and workflow-execution protections alone
  are insufficient remediation.
- Preserved the disposable package, no-consumer policy, Official isolation,
  no-PAT/no-OIDC constraints, and non-inheritance by future Buddy destinations
  or production packages.

## [2026-08-06] query | Draft first-slice Workflow Delivery v3 LLD

- Added the brief `hcoona-release-smoke-npm` first-slice LLD as a draft awaiting
  explicit user approval.
- Defined clean v3 module and workflow decomposition, strict run-attempt and
  purpose bindings, isolated frozen-version npm packing, SHA-512 remote exact
  proof, first-slice policy authoring, failure/replay behavior, acceptance
  scenarios, and dependency-ordered implementation commits.
- Revised first-slice CI to shadow pull-request and non-authoritative manual
  `slice-validation`, retaining v1 as required CI and deferring canonical v3
  full validation.
- Required one concurrency-scoped caller to hold Release Execution identity
  while invoking the same-revision reusable Attempt through finalization, plus
  exact capability-group result bundles.
- Replaced the invented approval timeout with truthful platform handling:
  first-slice GitHub rejection lacks authoritative attempt-bound denial Evidence
  and remains unknown/replayable incomplete, while approval-pending cancellation
  or expiry may end the run without a downstream record or Finalizer. When no
  capability group started, that platform termination proves no side effect;
  possible capability execution requires reobservation.
- Defined caller/callee reusable-workflow permission ceilings, separate
  tarball-content and install/import Evidence in one physical qualification
  lane, and an immutable digest-bound reviewer-summary artifact linked through
  the approval deployment URL and Authorization Record.
- Added root-HK path-triggered v3 control-package pytest with unconditional
  manual slice execution, while keeping it inside SourceTreeConformance.
- Added history-only same-Execution run/artifact admission before current
  Attempt binding, complete Actions API pagination, ID-only artifact transport,
  and expiry fallback to current absent/exact observation or reconciliation.
- Made `approval-finalizer` the credential-free capability admission gate before
  publisher scheduling, retained publisher revalidation as defense in depth,
  and treated Deployment Review data as diagnostic-only.
- Expanded root-HK v3 pytest triggers to the exact control tree, first-slice
  descriptors/policy, all v3 workflow consumers, direct Python workspace/lock
  inputs, and HK configuration/helpers; added policy-only coverage.
- Required Renovate-managed full 40-character action pins with version comments
  and the current Renovate-selected Node-24-compatible action major, without
  fixing an `upload-artifact` major in the LLD.
- Defined caller-selected `current-authority` and `execution-history` admission
  modes, strict current/history bindings, and payload inability to choose mode.
- Added permanent root-HK disposable-package consumer policy, CODEOWNERS
  final-match coverage/tests, and change-triggered plus 90-day human
  writer-TCB/package-grant re-attestation with operator-driven live disablement
  pending acceptance.
- Added Release-owned exact-target pre-Attempt Live Eligibility Decisions and
  fixed-source protected-ref human writer/access attestations. Runtime validates
  ref/commit/blob/content provenance, schema, bindings, expiry, and live-enable
  state without claiming unavailable writer or GitHub Packages grant
  enumeration; human change response plus at-most-90-day expiry bounds
  staleness.
- Added canonical in-tarball npm target witness requirements and remote
  extraction/validation so coordinate, ownership, immutable target, and bytes
  are all required for exact state.
- Relaxed history attribution to platform-exposed artifact/run facts with
  separately queried job/run phase data; payload producer/attempt/workflow claims
  are diagnostic and strict historical provenance remains unsupported without
  separately approved attestations/OIDC.
- Allowed same-run earlier-attempt artifacts as history-only diagnostics when
  platform run-attempt existence and artifact integrity/correlation validate,
  without claiming artifact-to-attempt or artifact-to-job provenance.
- Added explicit target-specific npm routing tags
  (`buddy-sha-<40-lowercase-target-sha>`), compound version-and-tag mutation
  keys/Receipts, exact tag observation, and acceptance probes for syntax,
  races, and GitHub Packages combined behavior. The in-package witness remains
  provenance authority.
- Expanded v3-control triggers to CODEOWNERS, all descriptor operations, v3
  control/catalog/tests, governed workflows/actions/scripts, HK surfaces, and
  root Python workspace inputs.
- Expanded the direct cutover commit to remove/rewrite legacy Buddy tests and
  node IDs, preserve Official/CI coverage, add no-route negatives, update active
  topology/rollout docs and `MEMORY.md`, and require root HK success.
- Recorded the final direct cutover: the implementation PR lands v3 disabled,
  retires both `buddy.yml` and `release-buddy.yml` without a compatibility
  route, drains old executions, verifies old-ref rejection, runs and removes the
  acceptance bootstrap, and then enables only the smoke slice. Failed acceptance
  leaves legacy Buddy retired; restoration requires a separate user-approved
  rollback PR, and a brief Buddy outage is expected.
- Retained bounded token-reach inspection to known and safely enumerated assets.
- Kept live Buddy disabled until disposable-package destination tests and human
  Governance inspection pass, and kept all coding blocked until LLD approval.
- Corrected Governance freshness so the pre-Attempt Live Eligibility Decision
  remains mandatory but cannot survive a later live disablement, attestation
  expiry, fixed-source change, or invalidation. `approval-finalizer` now
  re-reads live state and the fixed source immediately before Capability
  Admission, requires provenance/content identity with the admitted Decision,
  and forces a new Attempt after Governance restoration; publisher repetition
  remains defense in depth only.
- Fixed the immutable first-slice attestation source contract to repository
  `hcoona/three`, ref `refs/heads/main`, and path
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`, carried
  by the concrete Release policy and bound by eligibility/provenance tests
  without payload self-reference to Git provenance.
- Corrected physical artifact naming to be deterministic and unique across the
  complete workflow run by including `github.run_attempt` directly or in the
  deterministic hash preimage, while preserving `overwrite: false`, ID-only
  admission, and same-run prior-attempt history-only use.

## [2026-08-06] lint | Apply adjudicated first-slice documentation fixes

- Replaced the non-fresh repository-variable rollout switch with the required
  boolean `live_enabled` field in the policy-fixed protected Governance
  attestation. Pre-Attempt eligibility and immediate pre-Capability admission
  now use `contents: read` to freshly resolve and read the protected source,
  bind and compare enabled state plus commit/blob/content provenance, and force
  a new Attempt after disablement, expiry, change, or invalidation.
- Kept publisher repetition optional defense in depth, added no PAT, App,
  service, OIDC, repository variable, or additional token permission, and
  documented the truthful protected-review/merge/read latency of human
  disablement and re-attestation.
- Added explicit final-match CODEOWNERS coverage and ownership-contract tests
  for
  `/.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`.
- Corrected direct-cutover wording so unchanged preservation covers v2 and v1
  Official/CI assets only. Legacy Buddy workflows, Buddy-specific
  tests/matrices, and Buddy docs are retired or rewritten before destination
  acceptance; an intentional Buddy outage is allowed, and acceptance failure
  leaves all Buddy publication disabled.
- Kept the first-slice LLD in Draft and made no source, workflow, test, or
  activation change.

## [2026-08-06] lint | Correct npm witness packaging and live permissions

- Required isolated npm staging to preserve the source manifest while
  deterministically adding `workflow-delivery/provenance.json` to the staged
  `files` allowlist alongside existing intended package files.
- Required artifact qualification to inspect the packed tarball, find the
  witness at exact entry `package/workflow-delivery/provenance.json`, and match
  its canonical bytes to the frozen witness input without changing frozen NBGV
  version semantics.
- Removed workflow-wide package write from the normal live dispatcher/caller
  contract. `packages: write` now appears only on the `run-live-attempt`
  `uses`-only caller job as the reusable-workflow ceiling and on the called
  Environment-referencing publisher job as effective capability; all other jobs
  remain explicitly least-privilege, and the callee cannot elevate beyond the
  caller.
- Kept approval and capability admission credential-free, retained the LLD in
  Draft, and made no implementation, test, workflow, activation, or commit
  change.

## [2026-08-06] lint | Tighten permissions, bootstrap retries, and NBGV history

- Restricted `evaluate-live-eligibility` to exactly `contents: read`, confined
  effective Actions-history read to admission and explicit package read to the
  observer, and aligned permission contracts and negative tests.
- Required every one-time GitHub Packages probe and evidence-capture job to fail
  closed unless `github.run_attempt == 1`; retry now requires a new reviewed
  workflow invocation and disposable coordinate/version.
- Required the NBGV Provider to remain pinned to the exact target while fetching
  complete ancestry and tags through `fetch-depth: 0` or an equivalent
  guarantee, reject shallow or incomplete history before compiling facts, and
  added contract/control test expectations.
- Kept the first-slice LLD in Draft and made no implementation, workflow, test,
  activation, or commit change.

## [2026-08-06] lint | Correct package serialization and bootstrap evidence

- Replaced the first-slice complete-resource-set hash as the actual GitHub
  concurrency group with a conservative physical-destination-plus-npm-package
  projection. It intentionally serializes different versions and
  target-derived tags because GitHub supports equality groups rather than
  arbitrary set-overlap locks.
- Preserved the complete frozen External Package Coordinate plus routing-tag key
  set in Publication Snapshots, action bindings, Receipts, validation,
  remediation, and future abstract overlap semantics without weakening
  `WD-CON-004`.
- Required terminal bootstrap evidence capture to run on the first attempt with
  `always() && github.run_attempt == 1`, retain dependency failures and
  ambiguous mutation evidence, and classify incomplete or unknown state for
  reconciliation while every probe and evidence job still rejects reruns.
- Kept the first-slice LLD in Draft and made no implementation, workflow, test,
  activation, or commit change.

## [2026-08-06] query | Approve the first-slice LLD

- Recorded explicit user approval of the
  `hcoona-release-smoke-npm` Workflow Delivery v3 first-slice LLD after the
  GPT-5.6 Sol closure review reported no findings.
- Advanced the checkpoint from design approval to dependency-ordered
  implementation while retaining separate acceptance and live-activation
  gates.
- Kept GitHub Packages atomic behavior as an activation acceptance
  classification rather than an open architecture decision.

## [2026-08-10] query | Deliver Workflow Delivery v3 build and quality adapters

- Recorded completion of first-slice implementation commit 4.
- Added the canonical in-tarball target witness, isolated Node Build and
  Quality Adapters, deterministic npm packaging, and strict artifact closure.
- Advanced the next approved dependency boundary to commit 5 while retaining
  disabled live activation.

## [2026-08-12] query | Deliver Workflow Delivery v3 shadow CI

- Recorded completion of first-slice implementation commit 5.
- Added exact CI planning, lane Evidence, non-authoritative finalization,
  shadow/manual CI, and the permanent smoke-package consumer-policy gate.
- Advanced the next approved dependency boundary to commit 6 while retaining
  disabled live activation.

## [2026-08-12] lint | Close Workflow Delivery v3 shadow CI review

- Closed five GPT-5.6 Sol review rounds with independent TP/FP adjudication and
  no findings in the final CI, policy, or holistic passes.
- Added typed retained npm artifact Evidence, blocked semantic-model
  finalization, trusted pull-request SLO classification, and bounded workflow
  and composite-action consumer discovery.
- Passed 1,838 v3 tests, the managed v3 HK gate, 3,873 root Python tests, and
  the applicable static, build, and lock gates while keeping live activation
  disabled.

## [2026-08-12] query | Deliver Workflow Delivery v3 Official simulation

- Recorded completion of first-slice implementation commit 6.
- Added Release identities, complete Release policy closure, exact
  four-obligation qualification, guarded two-snapshot contracts, strict
  Release transport, and the 12-job Official simulation workflow.
- Closed independent contract, qualification, workflow, and holistic reviews
  after TP/FP adjudication and fixes.
- Passed 1,924 v3 tests, the managed v3 HK gate, 3,959 root Python tests, and
  the applicable static, build, and lock gates.
- Advanced the next dependency boundary to commit 7 npmjs observation while
  keeping live activation disabled.

## [2026-08-13] query | Close Workflow Delivery v3 commit 7

- Completed credential-free exact-version npmjs observation for Official
  simulation with bounded HTTP/tar processing and retained canonical facts.
- Closed six independently adjudicated review findings and obtained clean
  follow-up reports from all four original GPT-5.6 Sol reviewers.
- Recorded passing v3, managed HK, root Python, static, Pkl, build, and lock
  validation evidence; commit 8 remains separately gated.

## [2026-08-14] query | Close Workflow Delivery v3 commit 8

- Completed the disabled live Buddy GitHub Packages boundary, including
  history admission, approval and Capability controls, publication, Receipts,
  finalization, and caller/reusable workflows.
- Closed six rounds of independently adjudicated findings and obtained explicit
  no-finding closure from all five original GPT-5.6 Sol reviewers.
- Passed 2,253 v3 tests, the managed v3 HK gate, 4,288 root Python tests, and
  all applicable static, Pkl, build, and lock gates.
- Advanced the next separately authorized boundary to commit 9 CODEOWNERS
  final-match coverage while keeping live activation disabled.

## [2026-08-14] query | Close Workflow Delivery v3 commit 9

- Added exact final-match `@hcoona` ownership for all approved Workflow
  Delivery v3 governance surfaces, including the absent protected Governance
  path and future descriptor/workflow/action layouts.
- Added real CODEOWNERS and HK history contracts for missing/overridden rules,
  exact owners, add/modify/delete/rename behavior, and arbitrary-ref Buddy
  separation.
- Closed all independently adjudicated review findings and passed 2,294 v3
  tests, the managed v3 HK gate, and 4,329 root Python tests.
- Advanced the next separately authorized boundary to commit 10 acceptance
  bootstrap while keeping live activation disabled.

## [2026-08-14] query | Implement Workflow Delivery v3 commit 10

- Added the protected disabled Governance attestation, strict Governance
  Acceptance Evidence, fixed-coordinate GitHub Packages probe contracts, and
  optional scoped read-only reviewer recovery.
- Added the temporary five-job destination-acceptance workflow with an
  immutable 40-zero target sentinel, first-attempt guards, dedicated
  Environment review, least-privilege probe jobs, and terminal reconciliation
  evidence retained for 45 days.
- Recorded that reviewer identity is unavailable in job context, recovery is
  diagnostic-only and retention-dependent, normal live remains disabled, and
  legacy Buddy retirement, real probes, target finalization, and activation are
  still later protected work.

## [2026-08-15] test | Close commit 10 local acceptance integration

- Bound acceptance classification to the exact validated npm request and
  mocked-upstream response proof captured locally with Node 24.14.0/npm 11.9.0,
  including exact credential replacement and redaction.
- Closed the single monotonic deadline, fail-closed runner-fact matrix, and
  non-zero complete-evidence SHA rules while retaining incomplete sentinel
  behavior.
- Kept capture and validation loopback-only, performed no remote Environment
  configuration, dispatch, external publication, or package mutation, and
  validated all four acceptance files together.

## [2026-08-15] query | Finalize Workflow Delivery v3 commit 10 locally

- Closed all independently adjudicated acceptance-bootstrap findings across
  evidence, workflow, probe runtime, Governance, and holistic review angles.
- Passed 578 commit-10 tests, 2,888 v3 tests through both direct and managed HK
  execution, 4,923 root Python tests, PNPM tests/builds, Python and .NET builds,
  and all applicable static and consumer-policy gates.
- Kept the acceptance workflow fail-closed behind the 40-zero target sentinel.

## [2026-08-15] query | Implement Workflow Delivery v3 commit 11 locally

- Retired legacy `.github/workflows/buddy.yml` and
  `.github/workflows/release-buddy.yml` with no `legacy-buddy.yml`, dispatch, or
  caller-compatibility route while preserving v1 Official/CI, v2 docs, generic
  profiles, and normal v3 Buddy workflows.
- Rewrote active workflow-release tests, matrix rows, caller completeness,
  bootstrap inventory, actionlint overrides, and active docs around the
  retained Official caller and retired legacy Buddy entries.
- Updated the v3 handoff to record commit 10 as delivered/pushed at `e69675be`
  and commit 11 as implemented locally until the parent commit; the next
  operational boundary remains post-merge commit 12.
  Protected Environment setup, reviewer configuration, target-SHA
  finalization, dispatch, package mutation, and activation remain pending.

## [2026-08-15] query | Close commit 11 TP adjudication findings

- Tightened reserved Buddy route handling so legacy `channel=buddy` callers and
  reserved `official`/`buddy` allowlist entries fail closed instead of falling
  through as custom channels.
- Removed impossible Official GitHub Packages live-publication evidence from
  Node and Ruby GitHub Release acceptance rows and dropped the now-unreferenced
  gate definition while preserving public registry and GitHub Release evidence.
- Preserved reusable/profile Buddy-domain regression coverage without restoring
  retired exact entry names, and clarified active docs as Official-only with
  post-merge commit-12 live-activation prerequisites.

## [2026-08-15] query | Finalize Workflow Delivery v3 commit 11 locally

- Marked the preserved v2 workflow design as archived and superseded without
  erasing its historical mechanism record.
- Closed all topology, evidence, reusable-domain, and documentation findings;
  the final 28-case retirement contract and 2,916-test managed v3 gate pass.
- Kept commit 11 uncommitted and performed no post-merge dispatch freeze,
  workflow disablement, run cancellation, acceptance execution, package
  mutation, target finalization, or activation.

## [2026-08-18] query | Define publication preparation interruption

- Recorded the confirmed live-Attempt boundary after successful Qualification
  and before a durable Publication Snapshot exists.
- Defined the `publication-preparation` incomplete Outcome, direct platform
  fact admission, durable-Snapshot boundary, single Finalizer ownership,
  retained operational diagnostics, and fail-after-retention behavior.
- Updated the v3 handoff through durable qualification terminalization while
  keeping live activation, acceptance dispatch, and package mutation disabled.

## [2026-08-18] query | Close publication preparation interruption

- Implemented and reviewed the canonical Outcome, exact Qualification replay,
  direct platform-fact truth table, durable Snapshot boundary, retained
  diagnostics, and fail-after-retention workflow behavior.
- Corrected Snapshot upload-failure admission, cancellation truth-table
  contradictions, separate Snapshot/reviewer artifact documentation, and sole
  Release Finalizer ownership.
- Advanced the next implementation boundary to Buddy caller-held concurrency
  around Release Execution identity while keeping live activation disabled.

## [2026-08-19] query | Close publication preparation review repairs

- Admitted GitHub's cancellation-owned `cancelled` result for an unstarted
  publisher only when no Snapshot or downstream lineage exists, without also
  classifying it as post-Snapshot platform termination.
- Executed the actual workflow classifier and retention shells in scenario
  tests, locked Snapshot transport ordering and lineage, completed the
  `AttemptOutcome` negative matrix, and added the post-binding reviewer
  artifact link to the completed job summary.
- Reconciled the Release MLD, smoke LLD, and handoff; the full v3 package passes
  3,068 tests with `GIT_LFS_SKIP_SMUDGE=1`, and the complete hook gate passes.

## [2026-08-19] query | Close cancellation finalization review

- Made Release Finalizer checkout, tool setup, and exact artifact acquisition
  cancellation-admitting so retained inputs remain available when GitHub runs
  cancellation finalization.
- Preserved failed and incomplete Qualification Outcomes when an unstarted
  publisher is reported `cancelled`, and rejected partial downstream
  `finalize-live` transport groups before record loading.
- Replaced the stale singular Publication Control Bundle requirement with the
  implemented logical closure of separately retained, explicitly selected
  artifacts and canonical cross-bindings.
- Closed pseudo-mutation and assertion-quality gaps; the full v3 package passes
  3,157 tests with `GIT_LFS_SKIP_SMUDGE=1`, and the complete hook gate passes.

## [2026-08-19] query | Close final cancellation runtime gaps

- Added a job-level inherited-cancellation witness and guarded mandatory
  artifact downloads against empty IDs.
- Completed independent propagation, unsuccessful-Qualification lineage, and
  one-hot CLI platform-fact forwarding coverage.
- Committed the runtime/test closure at `646060e5`; the full v3 package passes
  3,181 tests with `GIT_LFS_SKIP_SMUDGE=1`.

## [2026-08-19] query | Close adjudicated cancellation test gaps

- Independently classified two final test-review findings as true positives
  and the commit-state documentation finding as a false positive.
- Locked exact producer-specific Qualification digest forwarding and the
  cancellation combination where Observation succeeded before materialization
  was skipped and the publisher was cancelled.
- Committed the test closure at `1daf3202`; the full v3 package passes 3,182
  tests with `GIT_LFS_SKIP_SMUDGE=1`, and the workspace hook gate passes.

## [2026-08-19] query | Close cancellation re-review test gaps

- Independently classified the two test re-review findings as true positives;
  the runtime, workflow, and design/evidence re-reviews reported no findings.
- Locked every retained Qualification artifact ID and name producer and
  executed the cancellation state through durable Attempt and GitHub job
  summaries.
- Committed the re-review test closure at `b5c4b38e`; the full v3 package passes
  3,183 tests with `GIT_LFS_SKIP_SMUDGE=1`, and the workspace hook gate passes.

## [2026-08-19] query | Complete Buddy execution concurrency

- Moved the caller-held concurrency key from request-specific shell hashing to
  canonical Buddy Release Execution identity: channel, Release Unit, and
  immutable target.
- Preserved request ID and workflow-run identity as Attempt transport, kept
  compilation and eligibility before coalescing, and held the group across the
  complete reusable live Attempt with `cancel-in-progress: false`.
- Committed the repair at `3a2df043`; the full v3 package passes 3,189 tests,
  the workspace hook gate passes, and all four independent reviewers report no
  findings after adjudication and repair.
- Kept normal live delivery, acceptance probes, sentinel finalization, and
  package mutation disabled.

## [2026-08-19] query | Complete Workflow Delivery v3 PR preparation

- Integrated `origin/main` at `3cc079ee` through non-rewriting merge commit
  `e4dfea3d`, preserving the deliberate legacy CI-job retirement while
  regenerating the merged PNPM and UV locks.
- Independently adjudicated and repaired the standalone Hexo override, active
  CI PNPM pins, v3 runtime toolchain expectations, and merged CI digest; all
  four merge re-reviewers then reported no findings.
- Made the post-merge lint boundary explicit at `f3eb3b81` by pinning Ruff
  0.14.4 until a separately scoped Ruff 0.16 migration; all three tooling
  re-reviewers reported no findings.
- Passed the complete workspace gate and authoritative 574-file
  `origin/main..HEAD` gate, including 1,257 workflow-release tests and 3,189 v3
  tests.
- Kept the branch local: no push, PR creation, acceptance probe, sentinel
  finalization, live activation, publication, or package mutation occurred.

## [2026-08-20] query | Open Workflow Delivery v3 implementation PR

- Pushed `dev/shuaizhang/design-workflows` non-force after refreshing both
  remote `main` and the existing branch ref.
- Opened [PR #552](https://github.com/hcoona/three/pull/552) against `main` with
  the direct-cutover warning, validation evidence, disabled-live state, and
  required post-merge drain steps.
- Left the PR unmerged. Checks and review remain the active boundary, and merge
  requires separate explicit authorization with the operator ready for the
  immediate intentional Buddy outage and legacy drain.
- Performed no acceptance probe, sentinel finalization, live activation,
  publication, or package mutation.

## [2026-08-20] query | Close Workflow Delivery v3 PR validation

- Repaired the Provider Git LFS boundary and all 20 PR #552 CodeQL findings in
  focused commits through `116b84d4`, without dismissing or suppressing an
  alert; independent reviewers reported no remaining findings.
- Integrated `origin/main` at `191abc82` through non-rewriting merge commit
  `4fac140d`, then passed the complete workspace gate and authoritative
  573-file committed-range gate, including 1,257 workflow-release tests and
  3,218 v3 tests.
- Pushed `4fac140d`; general CI and CodeQL passed in remote run
  [`32346356010`](https://github.com/hcoona/three/actions/runs/32346356010).
  The non-authoritative shadow finalizer produced the expected fail-closed
  `incomplete-model-plan` decision with `fix-model-plan-and-rerun`.
- Left PR #552 unmerged and performed no acceptance probe, sentinel
  finalization, live activation, publication, or package mutation. Human review
  and separate merge authorization remain required before the intentional
  Buddy outage and legacy drain.

## [2026-08-21] query | Confirm v3 pre-coexistence CI bootstrap

- Confirmed that PR #552 is red only because its canonical non-authoritative
  shadow Decision blocks on 283 paths outside the incomplete first-slice
  Repository Model; general CI and CodeQL remain green.
- Approved a one-time, self-disabling bootstrap check-conclusion projection
  when the exact pull-request base commit lacks the canonical v3 CI workflow.
  The canonical `incomplete-model-plan` Decision remains failure and the
  projection accepts only exclusively unclassified changed-path diagnostics.
- Kept project-test and other lane failures, mixed diagnostics, manual
  validation, superseded candidates, malformed records, and post-coexistence
  pull requests fail-closed.

## [2026-08-21] query | Implement v3 pre-coexistence CI bootstrap

- Committed the bounded bootstrap design at `7c457b7c` and implementation at
  `f0535989`; the canonical `incomplete-model-plan` Decision and Summary remain
  unchanged while only the enclosing non-authoritative pull-request check may
  project success.
- Bound the projection to exact canonical record re-admission, exact
  pull-request event identity, exclusively unclassified changed-path
  diagnostics, no selected work or Evidence, and an exact base-tree proof that
  the canonical v3 CI workflow is absent. The marker self-disables the
  exception after merge.
- Passed the 14-file committed-range managed gate with 3,234 v3 tests and
  1,257 workflow-release control tests. Three independent review scopes
  completed TP/FP adjudication and returned no findings on the terminal
  re-review.
- Left PR #552 unmerged and performed no acceptance probe, sentinel
  finalization, live activation, publication, or package mutation. Current-head
  PR check observation and human review remain next.

## [2026-08-21] query | Integrate latest main before bootstrap publication

- Refreshed `origin/main` after the bootstrap closure and found two
  dependency-only commits updating Biome to 2.5.9 and Asciidoctor to 4.0.10;
  the canonical v3 CI workflow remains absent from the exact new base tree.
- Integrated `origin/main` at `7f8f41c2` through non-rewriting merge commit
  `30b793be` without conflicts or changes to the bootstrap implementation.
- Verified the merged PNPM lock with frozen installation and supply-chain
  policy checks and verified the UV lock. PR #552 remains unmerged, and no live
  or package mutation operation ran.

## [2026-08-21] query | Validate v3 bootstrap projection remotely

- Published head `9b7b7d2c` non-force and observed every PR check pass,
  including Workflow Delivery v3 run
  [`32440545037`](https://github.com/hcoona/three/actions/runs/32440545037),
  general CI run
  [`32440545005`](https://github.com/hcoona/three/actions/runs/32440545005),
  and CodeQL run
  [`32440545090`](https://github.com/hcoona/three/actions/runs/32440545090).
- Downloaded the exact Plan and four lane-result artifacts. Their replay
  retained Finalizer exit `1`, `incomplete-model-plan`,
  `fix-model-plan-and-rerun`, 283 exclusively unclassified-path diagnostics,
  and zero admitted Evidence or artifacts.
- Replayed the projection against the exact base, head, and tested merge and
  observed its explicit note that the canonical Decision remains failure.
  Left PR #552 unmerged and performed no acceptance probe, sentinel
  finalization, live activation, publication, or package mutation.

## [2026-08-21] lint | Clean Workflow Delivery v3 merge scope

- Restored production v1 CI exactly from base `7f8f41c2` and restored the v1
  Official/reusable release stack with only the approved fail-closed legacy
  Buddy retirement delta.
- Removed the inherited pre-v3 control plane, obsolete smoke projects, legacy
  descriptors, related scripts/tests/fixtures, and superseded design history
  while retaining the direct v3 package, first-slice npm project, Governance,
  CODEOWNERS/HK integration, and dedicated v3 workflows.
- Regenerated UV, PNPM, and Mise locks for the retained scope. This entry does
  not claim RC-001 final validation closure, and no live or package mutation
  operation ran.

## [2026-08-23] lint | Refine Workflow Delivery v3 requirement layering

- Reframed WD-REL-003 around observable purpose isolation, current-attempt and
  exact-target binding, complete repository facts, anti-replay behavior, and
  fail-closed NBGV history proof.
- Removed Repository Model Snapshot, Fact Bundle, GitHub field, Provider,
  checkout, and `fetch-depth` mechanisms from the requirement while retaining
  their existing HLD and MLD ownership.
- Preserved the requirement ID, security outcomes, lifecycle boundary, and
  implementation behavior.

## [2026-08-23] lint | Correct Workflow Delivery v3 current boundaries

- Replaced obsolete pre-LLD overview steps with the current review, merge,
  cutover, acceptance, and separately approved activation boundaries.
- No merge, sentinel finalization, acceptance run, package mutation, or Live
  activation was authorized or performed.

## [2026-08-23] lint | Close Workflow Delivery v3 implementation review

- Recorded Phase 8 closure of RC-039, RC-002, RC-005, RC-006, and RC-001 at
  exact behavior commit `e9d812b2`, tree `b36b7926`, base `62ffb59b`, and
  tested merge `34dc8fe6`.
- Retained the superseded candidate ledger and the final green General CI,
  CodeQL, and dedicated v3 runs; admitted all nine immutable v3 artifacts and
  reproduced the canonical Decision and Summary byte-for-byte.
- Kept this closure documentation-only. PR #552 remains open and unmerged, and
  no acceptance, package mutation, Live activation, Ruleset change, force
  push, or history rewrite was authorized or performed.

## [2026-08-23] lint | Close Workflow Delivery v3 PR review follow-up

- Adjudicated the remaining PR comments into ten true positives and four false
  positives, repaired only the true positives, then closed two additional
  holistic test-integration gaps. Fresh same-scope and holistic rereviews
  report no findings. A later comment claiming that
  `PurePosixPath.full_match()` is unavailable was independently adjudicated as
  a fifth false positive against the package's Python 3.13 minimum and the
  dedicated workflow's Python 3.13 runtime. Every review thread is resolved
  with evidence. The prior RC-001 evidence reviewer could not be resumed, so a
  fresh independent evidence and documentation reviewer assessed the exact
  closure and reported no findings.
- Recorded current behavior commit `9f97ef09`, tree `69bec461`, exact base
  `62ffb59b`, and tested merge `59ad1ef2`. General CI run `32669623270`,
  CodeQL run `32669623284`, and dedicated v3 run `32669623261` all pass at
  attempt 1.
- Verified every retained artifact against its GitHub byte count and SHA-256
  digest, admitted all nine v3 payloads, and reproduced the canonical Decision
  and Summary byte-for-byte at the recorded 171-second clock. The Decision
  remains the expected non-authoritative `incomplete-model-plan` failure while
  the exact bootstrap projection succeeds.
- Retained the prior `e9d812b2` evidence as superseded rather than rewriting
  it. PR #552 remains open and unmerged, and no acceptance, package mutation,
  Live activation, Ruleset change, force push, or history rewrite was
  authorized or performed.

## [2026-08-24] query | Begin Workflow Delivery v3 destination acceptance

- Recorded PR #552 merge `5a84bebd` and completed the immediate disabled Buddy
  cutover. Both legacy workflow identities are `disabled_manually`, no
  nonterminal executions remain, both files are absent from `main`, and real
  old-ref dispatch requests receive disabled-workflow rejection.
- Authorized destination acceptance without normal Live activation. Protected
  finalization binds the one-time workflow to implementation merge `5a84bebd`
  while the Governance attestation remains `live_enabled: false`.
- Created the temporary reviewer-protected acceptance Environment before
  finalization, with `hcoona` as required reviewer, self-review permitted for
  the single-operator topology, and a custom `main` deployment branch policy.
- Retained transition evidence: run `32693641797` terminated with zero jobs,
  and run `32693679161` executed only the read-only default-branch refusal
  stub. Neither entered a publication path.

## [2026-08-24] query | Fail closed after incomplete destination acceptance

- Merged protected finalization PR #573 as `d36e5a68` with the one-time
  acceptance target fixed to implementation merge `5a84bebd`.
- Attempt-1 run `32769435970` passed fixed-input validation and protected
  review. Its absent/create/readback probe observed absent pre-state, started
  the fixed publish mutation, and exactly read back
  `0.0.0-wdv3-acceptance.1`, but classified the suite incomplete because the
  runner did not prove a controlled response.
- Terminal Governance evidence correctly failed closed when the runner's
  pre-request-failure response contradicted canonical mutation startedness.
  The exact/race/lost-response probe did not run.
- Immediately converted the temporary workflow to `disabled_manually` and
  removed and verified absence of the acceptance Environment. Protected cleanup
  removes the workflow file, after which workflow and Environment APIs must
  verify the workflow identity, any temporary bypass, and the Environment
  absent before package/evidence reconciliation. The failed invocation, review,
  and coordinate are not reusable; no retry is currently authorized, and the
  protected attestation remains `live_enabled: false`.

## [2026-08-24] query | Reconcile incomplete destination acceptance

- Merged cleanup PR #575 as `274d81fd`, restored workflow ID `340952168` to
  `disabled_manually` after GitHub marked the removed file `deleted`, and
  proved real old-ref dispatch rejection with HTTP 422. The workflow file,
  temporary transition ref, acceptance Environment, and nonterminal runs are
  absent.
- Authenticated package and npm-registry inspection confirmed version
  `0.0.0-wdv3-acceptance.1` and tag `wdv3-acceptance-1` remain exact. The
  repository association is `hcoona/three`; SHA-1 is
  `033bb682b846c7fc3a6c325d5455ad79087b50d5`; SHA-512 is
  `b32fa3ccdd477af2d83f554392d8fa2499db713bc651bef39fca16263752dcb8af51513a5a459fbc13dbbd75c1607ee35cbd74af1d608998feced5c1d21c4e37`;
  and the tarball manifest and witness bind the fixed package, version, purpose,
  and target `5a84bebd`.
- Root cause is a fail-closed result-label defect: a generic runner failure
  after recorded mutation startedness was labeled
  `runner-failed-before-mutation`, so terminal Governance admission correctly
  rejected the contradiction. The staged, unmerged repair preserves incomplete
  classification and does not convert reconciled exact destination state into
  acceptance success. No retry or Live activation is authorized.

## [2026-08-25] query | Prepare isolated destination-acceptance retry

- Recorded the explicit authorization for one fresh destination-acceptance
  retry while keeping normal Live activation unauthorized and
  `live_enabled: false`.
- Added a closed retry-2 acceptance profile with fixed `.5`-`.8` versions and
  tags while preserving the historical `.1`-`.4` profile for strict evidence
  replay. Profile selection binds the exact workflow path, Environment,
  confirmation digest, target, and scenario coordinate/tag inventory.
- Restored the reviewed five-job bootstrap under the fresh
  `workflow-delivery-v3-buddy-smoke-acceptance-retry-2.yml` identity and fresh
  Environment name. The implementation stage retains a 40-zero target sentinel
  and cannot pass fixed-input validation before a separate protected
  finalization binds the implementation merge SHA.
- Remote provisioning of the fresh GitHub Environment remains pending.
- The eventual attempt remains first-attempt-only and requires independent
  platform verification. Terminalization and evidence capture precede
  disabling the numeric workflow identity and deleting the Environment;
  protected cleanup then removes the temporary surfaces and re-disables the
  saved identity if GitHub transitions it to `deleted`.

## [2026-08-25] query | Finalize isolated destination-acceptance retry

- Merged implementation PR #582 as
  `b031e5e0bd98a95943a03a1529b64e856e1a8aa1`.
- Authenticated preflight confirmed the `.5`-`.8` versions and tags are absent.
- Provisioned Environment
  `workflow-delivery-v3-buddy-smoke-acceptance-retry-2` with sole required
  reviewer `hcoona`, self-review permitted, and sole deployment branch `main`.
  Workflow ID `341728447` has no runs.
- Bound the protected-finalization workflow, Governance profile, and contract
  to the implementation merge SHA. The finalization must merge before the one
  authorized attempt-1 dispatch. Normal Live remains disabled and
  unauthorized.

## [2026-08-25] query | Capture unsuccessful acceptance retry

- Merged protected finalization PR #583 as
  `953c1db0712f6ff4d41b7e6a35767d71a2b19c4d` and dispatched exactly one
  attempt-1 run, `32805739095`, from `main`.
- Environment reviewer `hcoona` approved the fixed target, `.5` base
  coordinate, and retry-2 confirmation. The first probe observed absent
  pre-state, started mutation, and observed exact post-state.
- The runner did not prove a controlled outcome and retained
  `runner-failed-after-mutation-start`. The first probe failed its completeness
  guard, the second probe was skipped, and terminal Governance evidence
  remained unknown/incomplete. The invocation did not establish destination
  acceptance.
- Raw artifacts `9548188898`, `9548197128`, and `9548202666` match their
  GitHub-recorded byte counts and SHA-256 digests.
- Disabled workflow ID `341728447`, deleted Environment ID `20531285468`, and
  verified all five jobs terminal. Protected source cleanup is staged.
  Authenticated package reconciliation remains blocked until cleanup merges,
  the deleted identity is re-disabled if necessary, and post-merge workflow,
  Environment, and temporary-ref absence plus old-ref rejection are proved.
  The `.5`-`.8` block is consumed, and normal Live remains disabled and
  unauthorized.

## [2026-08-25] query | Record acceptance-cleanup platform blocker

- Merged protected cleanup PR #584 as
  `fd2f056b756067bd759b7a6004fe5c2fdbebe47e`; the retry workflow,
  Environment, and temporary transition ref are absent.
- GitHub changed workflow ID `341728447` from `disabled_manually` to `deleted`
  after source removal and rejected direct enable and disable requests with
  HTTP 403.
- The required old-ref rejection request unexpectedly returned HTTP 204 and
  created cleanup probe run `32809578776`. The request used the non-`main`
  transition ref, so fixed-input validation would fail before Environment
  review or mutation. The accepted dispatch reactivated the workflow identity,
  which was immediately returned to `disabled_manually`.
- The cleanup probe remains queued with zero jobs and zero pending deployments.
  Normal cancel and force-cancel return HTTP 500. User-authorized deletion
  returns HTTP 403 while the run is queued. Its complete API metadata and
  SHA-256 digests are retained outside the repository.
- Do not dispatch, rerun, approve, or recreate a transition ref. Authenticated
  package reconciliation remains blocked until GitHub terminalizes or removes
  the stuck run. Normal Live remains disabled and unauthorized.

## [2026-08-26] query | Close acceptance-cleanup platform blocker

- Verified that GitHub Support terminalized cleanup probe run `32809578776` as
  `completed` / `cancelled` at `2026-08-26T01:45:46Z`, with zero jobs and zero
  pending deployments.
- Reconfirmed workflow ID `341728447` as `disabled_manually` and the workflow
  source, acceptance Environment, and transition ref as absent.
- Authenticated exact-coordinate reconciliation confirmed version
  `0.0.0-wdv3-acceptance.5`, tag `wdv3-acceptance-5`, repository association
  `hcoona/three`, tarball SHA-1
  `e2c5d9ffb5f94e3ae807d0a4022d99c3e5711050`, SHA-512
  `080c3d828a30d73d1febc3b6773015fafb529cf3a2be81fe597e83a83a589d32c1be62e933fb38ac4a77f9cb561c6399d3b2e6fe9179b3e4aed93087007140f2`,
  and target witness `b031e5e0bd98a95943a03a1529b64e856e1a8aa1`.
- Closed Platform-Orphan implementation PR #590 unmerged because natural
  terminalization removed the blocker before protected consumption. No
  candidate ran, and no exception authority or result entered `main`.
- Preserved retry-2 acceptance as unsuccessful, prohibited reuse of the
  `.5`-`.8` block, and kept normal Live disabled and unauthorized.

## [2026-08-26] query | Prepare acceptance protocol-proof repair

- Prepared repair-only PR #596 at implementation commit `44b3915e`.
- Changed normal acceptance completion to require the proxy-validated,
  request-bound HTTP 201 proof, admitted execution and mutation startedness,
  and exact post-readback. Proof-free create remains incomplete.
- Kept optional closed runner diagnostics non-authoritative and preserved
  historical proof-free `created` and lost-response evidence replay.
- Passed 543 focused acceptance and Governance tests, 3,686 complete v3 tests,
  Python quality gates, staged pre-commit under the repository-locked Mise
  environment, and multi-reviewer review with independent adjudication.
- Did not create or authorize a third acceptance workflow, invocation,
  Environment, coordinate block, package operation, Release lineage, or Live
  activation. Both prior acceptance invocations remain unsuccessful.

## [2026-08-26] lint | Close PR 596 startedness review

- Independently adjudicated as true-positive the PR review finding that valid
  protocol proof could retain authority despite missing admitted execution or
  mutation startedness.
- Required both admitted startedness facts before the Adapter retains protocol
  authority. Other cases now use the existing fail-closed runner
  classification without proof or protocol diagnostic, matching Governance
  admission.
- Passed the focused 543-test acceptance and Governance suite after the
  correction, and independent review reported no findings.
- Preserved the repair-only boundary: no acceptance invocation, package
  operation, or Live activation was performed or authorized.

## [2026-08-26] query | Merge acceptance protocol-proof repair

- Merged repair-only PR #596 by squash as
  `e69195eda71e7c8dd184bb29b28fd90102e71fa1` after resolving the independently
  confirmed startedness finding.
- Confirmed the final head passed the focused 543-test suite, complete
  3,686-test v3 suite, staged gate, GitHub CI, CodeQL, v3 shadow CI, and Copilot
  rereview without a new finding.
- Kept both prior acceptance invocations unsuccessful. The merge does not
  authorize a third acceptance workflow, invocation, Environment, coordinate
  block, package operation, Release lineage, or Live activation.

## [2026-08-26] query | Prepare destination acceptance retry 3

- Recorded explicit user authorization for the full retry-3 sequence while
  limiting the current phase to preparation.
- Added the fixed retry-3 workflow and Governance profile with the 40-zero
  target sentinel, Environment
  `workflow-delivery-v3-buddy-smoke-acceptance-retry-3`, and `.9`-`.12`
  scenario/tag block (`wdv3-acceptance-9` through `wdv3-acceptance-12`).
- Recorded read-only preflight: `.9`-`.12`, acceptance refs, and acceptance
  Environments were absent; retired retry-2 workflow ID `341728447` was
  disabled with no nonterminal runs.
- Did not provision an Environment, dispatch a workflow, mutate a package, or
  create a ref. Both historical attempts remain unsuccessful,
  `live_enabled` remains false, and Live activation still requires separate
  authorization.

## [2026-08-27] lint | Align retry-3 acceptance toolchain authority

- Corrected the first-slice LLD after PR #598 review identified that its
  acceptance-toolchain paragraph still described the historical Node
  24.14.0/npm 11.9.0 capture as current execution authority.
- Recorded merged dependency update `d3114d77` (#568) as advancing the current
  acceptance boundary and retry-3 workflow to the separately captured Node
  24.19.0/npm 11.17.0 request.
- Preserved the original capture as historical replay evidence and made no
  Environment, dispatch, package, ref, finalization, or Live operation.

## [2026-08-27] query | Finalize destination acceptance retry 3

- Merged preparation PR #598 as
  `a61f9a4e44458bfd7bc7bfd96f6db848ce047c0c`.
- Fresh authenticated preflight confirmed the `.9`-`.12` versions and
  acceptance refs are absent.
- Provisioned Environment
  `workflow-delivery-v3-buddy-smoke-acceptance-retry-3` as ID `20680097388`
  with sole required reviewer `hcoona`, self-review permitted, and sole
  deployment branch `main`.
- Bound the protected-finalization workflow, Governance profile, and contract
  to the preparation merge SHA. Finalization must merge before the one
  authorized attempt-1 dispatch. Normal Live remains disabled and
  unauthorized.

## [2026-08-27] query | Capture unsuccessful acceptance retry 3

- Merged protected finalization PR #599 as
  `af9212288e83c3b792cfd9b3ab86f3b8e2b7f533` and dispatched exactly one
  attempt-1 run, `33032171094`, from `main`.
- Environment reviewer `hcoona` approved the fixed target, `.9` base
  coordinate, and retry-3 confirmation. The first probe observed absent
  pre-state, started mutation, and exactly read back `.9`.
- The runner again did not prove a controlled outcome. The first probe failed
  its completeness guard, the `.10`-`.12` probe was skipped, and terminal
  Governance evidence classified the run unknown. Acceptance was not
  established and no retry is authorized.
- Raw artifacts `9630646147`, `9630653496`, and `9630658559` match their
  GitHub-recorded SHA-256 digests. Authenticated package reconciliation
  confirms tag `wdv3-acceptance-9`, tarball SHA-1
  `316c789500aed5fccf535b90c02d501b0e262755`, SHA-512
  `905b839a258d8b6171f2ad8b6e945d317ed03e68fc8efd11bc8a0e8e240c3ee16a5a54e28c743e55aa1f4ca36d0086ec824d696e72c0c30f5c16b7cae2d8bff1`,
  and target witness `a61f9a4e44458bfd7bc7bfd96f6db848ce047c0c`; `.10`-`.12`
  remain absent.
- Disabled workflow ID `343371046`, deleted Environment ID `20680097388`, and
  confirmed acceptance refs absent. Protected source cleanup is staged. The
  `.9`-`.12` block is consumed, and normal Live remains disabled and
  unauthorized.

## [2026-08-27] query | Close destination acceptance retry 3

- Merged protected cleanup PR #600 as
  `916ea33802dda17a24e3b4971aae74b55ed4c7f5`.
- Confirmed the temporary workflow source and workflow-only contract are
  absent from `main`; numeric workflow ID `343371046` is `deleted`.
- Reconfirmed Environment ID `20680097388` and acceptance refs absent, run
  `33032171094` terminal at attempt 1, exact `.9` retained, and `.10`-`.12`
  absent. No post-deletion dispatch was attempted.
- Closed retry 3 as unsuccessful without retry. All three destination-
  acceptance attempts remain unsuccessful, `live_enabled` remains false, and
  Live activation remains unauthorized.

## [2026-08-28] query | Record acceptance upstream-diagnostic repair

- Recorded retry-3 documentation closure PR #601 at
  `ad70a879de36b750bff7793dde470b6712ea515e`.
- Recorded repair PR #603 at
  `bf1748971f2717a8877852590c5436b4160a4fbf`. The repair retains only closed,
  credential-free, request-bound upstream status or transport diagnostics,
  propagates them through the runner and Adapter, and admits them in Governance
  without granting completion authority.
- Recorded the expected-one concurrency repair: request cardinality and
  request-fact append form one atomic reservation, so a simultaneous duplicate
  is rejected locally before a second upstream write.
- Recorded 3,782 passing v3 tests plus focused Pyrefly, unstaged and staged HK,
  independent review/adjudication, required checks, and CodeQL.
- Confirmed that no destination-acceptance invocation followed #603 before
  this documentation update. The repair does not retroactively change any of
  the three unsuccessful attempts; `.1`-`.12` and all associated identities
  remain consumed.
- Recorded the continuing acceptance-only authorization boundary: a fourth
  profile may proceed tests-first with wholly new coordinates and identities,
  one package-mutation attempt at a time, and mandatory reconciliation for
  ambiguity. Normal Live activation and `live_enabled: true` remain excluded.

## [2026-08-28] query | Prepare destination acceptance retry 4

- Added the closed retry-4 Adapter and Governance profile using base `.13`,
  absent/exact `.13`, identical-race `.14`, differing-race `.15`, and
  lost-response `.16`, with tags `wdv3-acceptance-13` through
  `wdv3-acceptance-16`.
- Added the temporary retry-4 workflow and fixed Environment identity with the
  40-zero target sentinel and confirmation
  `I_ACCEPT_DISPOSABLE_GITHUB_PACKAGES_PROBES_RETRY_4`. Validation fails before
  Environment review or either package-write probe while the sentinel remains.
- Recorded 3,836 passing v3 tests, local HK gates, and multi-reviewer review
  closure after independent TP/FP adjudication and repair.
- Kept the phase preparation-only: no Environment, acceptance dispatch,
  package or tag mutation, or acceptance ref was created. After preparation
  merges without bypass, external state must be revalidated before Environment
  creation and a separate finalization PR pins the reviewed target to the
  preparation merge SHA.
- Preserved all three historical attempts as unsuccessful, `.1`-`.12` as
  consumed, `live_enabled: false`, and normal Live activation as unauthorized.

## [2026-08-28] query | Close retry-4 rejected-dispatch evidence gap

- Added
  `test_retry_4_terminal_program_preserves_fixed_identity_after_rejected_dispatch`
  to execute the temporary workflow's exact terminal Python after fixed-input
  validation rejects deliberately wrong dispatch values.
- Proved that canonical terminal evidence remains bound to the fixed zero
  target, `.13` package coordinate, retry-4 confirmation digest, workflow
  identity, failed validation, skipped downstream jobs, and incomplete
  mutation classification.
- Isolated the subprocess from ambient credentials, proxies, `PYTHONPATH`, and
  user startup state with Python isolated mode, an isolated home, and a minimal
  allowlisted environment.
- Recorded 3,837 passing v3 tests and a successful unstaged HK gate. The
  follow-up remains preparation-only: no Environment, acceptance ref,
  workflow dispatch, package, tag, or Live mutation occurred.

## [2026-08-28] query | Finalize destination acceptance retry 4

- Rebase-merged preparation PR #608 without bypass as
  `835b81be1ff0ba7aa0ec23c9a7b518d4ade3dfaa`; post-merge CI and CodeQL passed.
- Fresh authenticated preflight confirmed exact principal, owner, `main`,
  ruleset, checks, open-PR drift, runs, deployments, refs, workflow identity,
  and absent `.13`-`.16` package versions and retry-4 npm tags.
- Provisioned Environment
  `workflow-delivery-v3-buddy-smoke-acceptance-retry-4` as ID `20772100445`
  with sole required reviewer `hcoona`, self-review permitted, and sole
  deployment branch `main`.
- Bound the protected-finalization workflow, Governance profile, and contracts
  to the preparation merge SHA. Finalization must merge without bypass before
  the single authorized attempt-1 dispatch, and another fresh exact preflight
  must pass before any acceptance ref or dispatch.
- No workflow dispatch, deployment, acceptance ref, package, tag, or Live
  mutation occurred.

## [2026-08-28] query | Capture unsuccessful destination acceptance retry 4

- Merged protected finalization PR #609 without bypass as
  `f3d53177a75bec9952fe39ffa547533d1a0992ef`, then passed a fresh exact
  preflight and dispatched exactly one attempt-1 run, `33165777024`, from
  `main`.
- Environment reviewer `hcoona` approved the fixed preparation target and
  `.13` base coordinate. The first probe observed absent pre-state, recorded
  action execution and mutation startedness, received request-bound upstream
  HTTP 200, and exactly read back `.13`.
- The proof contract required HTTP 201, so no validated request proof formed.
  The first probe remained incomplete, the `.14`-`.16` probe was skipped, and
  terminal Governance evidence classified the run unknown. Acceptance was not
  established and the run was not retried.
- Artifacts `9683508663`, `9683519655`, and `9683526452` match their
  GitHub-recorded SHA-256 digests. Authenticated reconciliation confirms tag
  `wdv3-acceptance-13`, tarball SHA-1
  `7f088ba1708310ef0dba5814da3ad4cf57d49062`, SHA-512
  `aafe86f3b48a7affc6c160f81bd81d69692fc3789149a7a01e620acd05052d0c7c0e87b7f552b19fc2192a90b6af1201b265cc2475ac28288cc1ab70bfbe7c71`,
  and target witness `835b81be1ff0ba7aa0ec23c9a7b518d4ade3dfaa`;
  `.14`-`.16` remain absent.
- Disabled workflow ID `344468231`, deleted Environment ID `20772100445`,
  and confirmed acceptance refs absent. Protected source cleanup is staged.
  The `.13`-`.16` block is consumed, and normal Live remains disabled and
  unauthorized.

## [2026-08-28] query | Close destination acceptance retry 4

- Merged protected cleanup PR #610 without bypass as
  `4e7e7ef6ffe08de2695d51ec5c477d82da4ff226`; post-merge Continuous
  Integration run `33170502138` and CodeQL run `33170502133` passed.
- Confirmed the temporary workflow source and workflow-only contract are
  absent from `main`; workflow ID `344468231` is `deleted` and still has only
  failed attempt-1 run `33165777024`.
- Reconfirmed Environment ID `20772100445` and acceptance refs absent, exact
  `.13` and its tag retained, `.14`-`.16` and their tags absent, and historical
  deployment `6140043006` terminal `inactive` after Environment deletion. No
  post-deletion dispatch occurred.
- Closed retry 4 as unsuccessful without rerun. The cleanup-before-repair gate
  is satisfied at `main@4e7e7ef6`; normal Live remains disabled and
  unauthorized.

## [2026-08-28] query | Repair GitHub Packages HTTP 200 acceptance proof

- Added tests-first coverage for exact HTTP 200 and HTTP 201 proof formation,
  normal and lost-response completion, status-preserving proof round-trip,
  response identity, Governance cross-binding, and CLI persistence. HTTP 202
  and HTTP 204 remain rejected.
- Changed the acceptance proxy, Adapter, and Governance admission boundary to
  accept only the closed provider-specific status set `{200, 201}` after the
  npm publish request has passed every existing coordinate, tag, tarball,
  witness, path, framing, cardinality, and credential-redaction check.
- Preserved the actual upstream status in proof serialization and response
  identity. New HTTP 200 diagnostics must remain request-bound; the narrow
  historical unbound HTTP 201-with-matching-proof form remains replayable.
- Pinned the exact terminal retry-4 Governance artifact as a regression
  fixture. It still admits as `unknown`: HTTP 200 plus exact `.13` readback
  cannot replace the missing validated request proof, and the `.14`-`.16`
  probe remains skipped. This repair does not rerun, reuse, or retroactively
  upgrade retry 4 and does not change normal Live authority.

## [2026-08-28] query | Close GitHub Packages HTTP 200 proof repair

- Rebase-merged protected repair PR #612 without bypass as
  `aed58191ce37defba8f7a7e44def03396c2c6824` after every required check,
  CodeQL analysis, and Workflow Delivery v3 shadow lane passed.
- Confirmed post-merge Continuous Integration run `33190125517` and CodeQL run
  `33190125529` passed on the exact merge.
- Fresh authenticated read-only reconciliation found no post-merge acceptance
  invocation: deleted workflow ID `344468231` still has only failed attempt-1
  run `33165777024`, the temporary Environment remains absent, and package
  versions remain limited to `.1`, `.5`, `.9`, and `.13`.
- Closed the response-status repair gate without changing retry-4's unknown
  result or normal Live authority. Any retry-5 profile must begin from a fresh
  fetch of `main@aed58191` or a later reviewed successor and use wholly new
  execution identities.

## [2026-08-28] query | Clarify retry-5 work-base authority

- Corrected the preceding closure entry's abbreviated work-base wording.
  Retry-5 must start from freshly fetched and revalidated `origin/main` at
  `aed58191ce37defba8f7a7e44def03396c2c6824`, or at a later reviewed, merged
  successor that contains it. A fetched SHA, local branch, attempt ref, or
  reviewed-but-unmerged head is not a valid work base.

## [2026-08-28] query | Prepare destination acceptance retry 5

- Recorded work-base clarification PR #613 at
  `8e6baf24ca476b449b5c97c21f14f3776e668b90` and successful post-merge
  Continuous Integration run `33194078923`. Retry-5 preparation started from
  a fresh fetch and revalidation of that exact `origin/main`.
- Added the closed retry-5 Adapter and Governance profile plus temporary
  manual workflow for absent/exact `.17`, identical-race `.18`,
  differing-race `.19`, and lost-response `.20`, with exact corresponding tags
  and a forty-zero preparation target. Fixed-input validation rejects before
  Environment review or either package-write probe while that target remains.
- Preserved canonical terminal suite records across monotone job/upload
  downgrade, nullified incomplete artifact bindings, normalized native bare
  upload digests, and retained request-bound runner diagnostics. Proof
  authority remains exactly HTTP 200 or HTTP 201.
- Kept the phase preparation-only. Read-only preflight found `.17`-`.20` and
  their tags unused, but no retry-5 Environment, dispatch, deployment,
  acceptance ref, package, tag, or Live mutation occurred; those coordinates
  remain unexecuted and unconsumed.
- Required a protected preparation merge before creating a new protected
  Environment or opening a separate finalization PR bound to the exact
  preparation merge SHA. Every later phase must freshly fetch and revalidate
  `origin/main` containing the preceding protected merge. Normal Live and
  `live_enabled: true` remain unauthorized.

## [2026-08-28] query | Clarify retry-5 post-preparation sequencing

- Clarified the preceding retry-5 preparation entry: after the protected
  preparation merge, fresh external-state revalidation must pass before either
  creating the retry-5 Environment or opening the separate protected
  finalization PR bound to that merge SHA. This external-state gate is distinct
  from the fresh `origin/main` work-base validation required for each later
  phase, which must contain the immediately preceding protected merge.

## [2026-08-28] query | Refresh retry-5 preparation work base

- A fresh pre-delivery fetch found dependency-only PRs #614 and #615 merged
  after the initial retry-5 base, advancing `origin/main` to
  `c33ea9da5456ca0e915e39134ec111714ddc4ec8`.
- Their four changed paths do not overlap the retry-5 preparation. Rebased the
  two preparation commits onto that reviewed successor without conflict.
- Continuous Integration run `33202438870` and CodeQL run `33202438791`
  passed on exact `main@c33ea9da5456ca0e915e39134ec111714ddc4ec8`.
- The all-zero preparation target, `.17`-`.20` coordinates and tags, workflow,
  Environment, and confirmation identities remain unchanged. No Environment,
  dispatch, deployment, package, tag, retry-5 acceptance ref, or Live mutation
  occurred.

## [2026-08-29] implementation | Finalize retry-5 protected target

- Protected preparation PR #616 rebase-merged without bypass as
  `66154d0bb351a0c9c13d16292ce003d7eee65077`. Post-merge Continuous
  Integration run `33223036097` and CodeQL run `33223036123` passed.
- Fresh authenticated revalidation found the retry-5 workflow active with zero
  runs, zero deployments, no acceptance refs, and `.17`-`.20` plus
  `wdv3-acceptance-17` through `wdv3-acceptance-20` unused.
- Created Environment
  `workflow-delivery-v3-buddy-smoke-acceptance-retry-5` as ID `20815831035`,
  with sole reviewer `hcoona` / `712433`, self-review permitted, and sole
  custom branch policy `main`.
- The bounded finalization candidate replaces only the two workflow target
  literals and retry-5 Governance target with exact preparation merge
  `66154d0bb351a0c9c13d16292ce003d7eee65077`. No dispatch, deployment,
  acceptance ref, package, tag, or Live mutation occurred.

## [2026-08-29] query | Capture successful destination acceptance retry 5

- Protected finalization PR #618 rebase-merged without bypass as
  `73bf1ecf395bc6d646d3e689e3c9e7fd580948ef`; post-merge Continuous
  Integration run `33265013602` and CodeQL run `33265013646` passed.
- Fresh exact preflight preceded the sole attempt-1 dispatch. Run
  `33265777858` executed from `main@73bf1ecf` against preparation target
  `66154d0b`. Deployment `6158274629` received approval from `hcoona`;
  independent recovery identifies deployment-review ID `100993530`.
- All five jobs succeeded. Artifacts `9718601879`, `9718607290`,
  `9718615519`, and `9718619450` match their GitHub-recorded SHA-256 values,
  and terminal Governance evidence independently re-admits as `complete`.
- Authenticated package reconciliation confirms exact `.17`-`.20` versions,
  tags, tarball SHA-1/SHA-512 values, repository association, and target
  witnesses. The fixed scenarios prove absent/create/readback, exact
  no-mutation, identical race, differing race, and lost-response behavior;
  both real create exchanges retain request-bound HTTP 200 proof.
- Disabled workflow ID `345015706`, deleted Environment ID `20815831035`, and
  confirmed deployment `6158274629` `inactive` with no pending deployments.
  The cleanup candidate removes the temporary source and workflow-only
  contract and restores direct zero-temporary-workflow assertions. Protected
  cleanup merge and post-merge reconciliation remain required.
- Destination acceptance is successful. The run and `.17`-`.20` block must
  never be retried or reused. Normal Live remains disabled and unauthorized.

## [2026-08-29] query | Close successful destination acceptance retry 5

- Cleanup PR #621 rebase-merged without bypass as
  `791544371eb3d1aff7376dbd14ae05ed074ff927`; post-merge Continuous
  Integration run `33268353682` and CodeQL run `33268353678` passed.
- Fresh `main` inspection confirms the retry-5 workflow source and
  workflow-only contract absent. Workflow ID `345015706` is `deleted` and
  retains exactly one successful attempt-1 run `33265777858`; no
  post-deletion dispatch occurred.
- Reconfirmed Environment ID `20815831035` and acceptance refs absent,
  deployment `6158274629` `inactive`, package repository association
  `hcoona/three`, and exact `.17`-`.20` versions and tags retained.
- Closed retry-5 destination acceptance and cleanup as successful. The run and
  consumed coordinate block must never be retried or reused. Historical
  Adapter/Governance replay authority remains. Normal Live remains disabled
  and requires a separate production decision.

## [2026-08-29] query | Design normal Live activation

- Confirmed design-only scope for normal Buddy publication of
  `@hcoona/hcoona-release-smoke-npm`; no implementation, Environment,
  Governance, workflow dispatch, approval, package, retry, or Break-Glass
  mutation is authorized.
- Read-only inventory at `main@7e04c5c2` found normal caller/callee workflow IDs
  `340952169` and `340952170` active, both permanent normal Environments absent,
  sole direct collaborator and accepted writer `hcoona`, and
  `live_enabled: false`.
- Recorded the user-selected single-maintainer exception: sole reviewer
  `hcoona` may self-approve with `prevent_self_review: false`. This is explicit
  operator self-confirmation, not independent review or a security boundary.
- Defined future protected delivery as disabled readiness repair, explicit
  Environment provisioning/readback, preparation evidence while false,
  separate activation, frozen `main` and dispatch, exact-SHA rollout preflight,
  one correlated attempt-1 run, explicit approval, and complete terminal
  reconciliation.
- Required Environment markers only as first-step configuration sentinels;
  native settings remain externally inspected authority. Closed GitHub Packages
  production behavior as create-only conflict semantics and preserved
  append-only failure/reconciliation rules.

## [2026-08-30] query | Implement normal Live readiness repair

- Recorded the user's separate authorization for readiness repair only after
  PR #623 merged the design as `cda7e2d6`; later Environment, preparation,
  activation, dispatch, approval, package, retry, legacy, and Break-Glass
  phases remain unauthorized.
- Added distinct approval and capability Environment marker checks as the
  literal first steps of their Environment jobs. The checks use exact
  case-sensitive Bash comparison, no `continue-on-error`, and explicit
  downstream marker-success gates.
- Preserved publisher failure evidence while preventing Receipt/result
  artifact work after marker failure; the sole unconditional propagation step
  is non-mutating and fails explicitly for any non-success marker outcome.
- Removed the unused approval-finalizer output that mislabeled the Intent
  artifact ID as an Attempt artifact ID, with no consumer replacement.
- Kept protected Governance false and made no Environment, workflow-run,
  deployment, package, tag, legacy, or Break-Glass mutation.

## [2026-08-30] query | Define normal Live Environment identity profiles

- Reopened the Environment naming question before external provisioning and
  found that the MLDs deferred exact names without defining resource reuse or
  cardinality.
- Confirmed the repository's single-maintainer operating model: pull-request-
  only contributors remain outside the writer TCB, while a later
  Write/Maintain/Admin grant is a Governance change.
- Defined policy-scoped reuse: Buddy approval identity follows the exact
  reviewer/Governance profile, and capability identity follows the exact
  destination, credential, permission, access, and native-protection profile
  with reviewer policy fixed to `none`; a reviewer-bearing destination requires
  a new architecture decision.
- Mapped the first slice to `workflow-delivery-v3-buddy-approval` and
  `workflow-delivery-v3-buddy-github-packages`. Sharing never transfers
  package eligibility, an approval, Authorization, Capability, or
  Attempt/package lineage; GitHub's Environment-level deployment history is
  shared.
- Kept `live_enabled: false`; no workflow, source, test, Environment,
  deployment, package, tag, legacy, or Break-Glass mutation occurred.
