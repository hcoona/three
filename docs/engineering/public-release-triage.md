# Public Release Triage and Inventory Gate

This record defines the monthly release-diff triage policy and mechanical
directory-inventory invariant for public deliverables under `src/public/**`. It
implements [GOV-002](../governance/governance-system.md#gov-002-one-canonical-authority),
[GOV-003](../governance/governance-system.md#gov-003-minimum-sufficient-mechanism),
[GOV-004](../governance/governance-system.md#gov-004-preserve-human-judgment),
[GOV-005](../governance/governance-system.md#gov-005-controls-implement-policy),
[GOV-007](../governance/governance-system.md#gov-007-trigger-bound-deferral), and
[GOV-008](../governance/governance-system.md#gov-008-merge-defines-current-repository-state).

Workflow Delivery retains sole authority over [Official channel scope](../../src/private/app/workflow-delivery/docs/integration/common.md#official-channel-scope),
release qualification, and destination completion
([WD-REL-009](../../src/private/app/workflow-delivery/docs/requirements.md#release-delivery)).
This triage mechanism advises the repository owner on accumulated changes; it
never dispatches releases or acquires publication credentials.

## Single-Source Discovery and Public Directory Exhaustiveness

`eng/scripts/release_triage.py` discovers public release units directly from
`src/public/**/workflow-delivery.release-unit.yml` and their declared
`entry-point` manifests using official YAML, JSON, TOML, and XML parsers. No
separate list of triaged units is maintained.

To prevent newly added public projects from silently omitting release and triage
coverage, the mechanical HK step `public-release-inventory`
(`eng/scripts/release_triage.py --check-inventory`, registered as control
`public-release-inventory` in [`controls.yaml`](../governance/controls.yaml))
enforces two deterministic invariants:

1. **First-level directory exhaustiveness:** every immediate child directory of
   `src/public/app/` and `src/public/lib/` either commits
   `workflow-delivery.release-unit.yml` or is explicitly listed in the
   non-release allowlist:
    - `src/public/app/azureauth-unofficial` — Windows alpha portable-plus-symbols
      CLI not yet onboarded to Workflow Delivery v3 GitHub Release recipes.
    - `src/public/lib/Memoization.Generators` — Roslyn source-generator build
      component packed inside `Memoization`.
    - `src/public/lib/Nanopass.Generators` — Roslyn source-generator and
      analyzer companion packed inside `Nanopass`.
      Stale allowlist entries (missing directory or a directory that now commits
      `workflow-delivery.release-unit.yml`) fail validation.
2. **Fail-closed recipe classification:** every discovered
   `src/public/**/workflow-delivery.release-unit.yml` must resolve cleanly to a
   supported triage tier and credential-free baseline reader from its existing
   `builds[].definition` and `entry-point` manifest (for `ruby/gem-v1`, the gem
   coordinate is `release-unit` per `RubyReleaseUnits`—verified at build time by
   native Ruby CI—and `--check-inventory` checks only that the declared
   `.gemspec` exists as non-empty UTF-8 text without parsing or executing Ruby).
   Unknown build definitions or unreadable manifests fail validation at
   pull-request time.

## Triage Tiers and Advisory Scope Boundary

Blanket calendar republishing across unbundled libraries adds version churn and
credential exposure without changing downstream resolved dependency graphs.
Public units are therefore evaluated in two tiers.

Because triage is a periodic advisory reminder rather than a release
qualification gate (which Workflow Delivery owns), change detection
intentionally scopes watched inputs to each unit's own directory, its
`ProjectReference` source companions, and primary dependency or runtime pins
(`global.json` for NativeAOT apps, the unit importer entry in `pnpm-lock.yaml`
for browser extensions, and `Directory.Packages.props` for NuGet libraries).
Repository-wide MSBuild props, root legal or workspace files, and deep
transitive lockfile graphs are intentionally out of scope.

### Tier 1: Bundled and Exact-Locked Deliverables

Applies to units whose shipped artifacts embed runtime binaries, bundled assets,
or exact runtime dependency pins:

- `dotnet/github-release-assets-v1` (`image-occlusion-editor`): self-contained
  NativeAOT Windows installer and portable archive. Surfaces on non-doc/non-test
  changes under the unit directory (including `packages.lock.json` and
  `Update-LocalSvgEditor.ps1`), referenced companion projects
  (`ProjectReference`), or root `global.json` compiler/runtime updates.
- `node/browser-extension-v1` (`steam-account-history-to-csv`): packaged Chrome,
  Firefox, and Edge extension archives. Surfaces on non-doc/non-test product,
  patch, script, `LICENSE`, WXT configuration, packaging-dependency (`wxt`,
  `@wxt-dev/auto-icons`, `engines`), or `pnpm-lock.yaml` importer changes, or
  whenever any of the three stores lacks a verified current baseline.
- `node/npm-package-v1` when `package.json` pins an exact runtime dependency
  version (`hexo-renderer-asciidoc`, which pins `@asciidoctor/core`): surfaces
  on non-doc/non-test source, build/pack/version script, or runtime/peer/export
  manifest changes while ignoring `devDependencies`-only and lint/test script
  updates.

### Tier 2: Unbundled Source Libraries

Applies to `dotnet/nuget-package-v1`, `python/distribution-set-v1`,
`ruby/gem-v1`, and `node/npm-package-v1` without exact runtime pins. Surfaces
only when:

- library source or packaging files (including referenced analyzer/generator
  companion projects such as `src/public/lib/Memoization.Generators`, while
  excluding docs, tests, and lockfiles) changed;
- declared runtime dependency lower bounds or target-framework/engine contracts
  changed (`src/public/lib/Directory.Packages.props` or root
  `Directory.Packages.props` entries referenced by the `.csproj`,
  `pyproject.toml` `[project]`/`[build-system]`/`[tool.hatch]` build fields,
  `*.gemspec` runtime fields, or `package.json` runtime/peer dependencies); or
- a merged pull request carrying the `security` label (within the 100 most
  recently updated merged security PRs) touched the unit.

## Credential-Free Official Baselines

Triage resolves published Official baselines without publication, Environment,
or browser-store credentials:

- **GitHub Releases (`dotnet/github-release-assets-v1`):** latest non-draft,
  non-prerelease GitHub Release matching `release/<unit>/v*` and its tag commit.
- **Browser stores (`node/browser-extension-v1`):** per-store GitHub Deployments
  in `workflow-delivery-v3-chrome-web-store`, `workflow-delivery-v3-firefox-amo`,
  and `workflow-delivery-v3-edge-addons` whose latest deployment status is
  `success` and whose linked `workflow-delivery-v3-browser-store-release.yml`
  workflow run completed with `conclusion == "success"` (`deployments: read` and
  `actions: read`, establishing that `verify-result` satisfied `WD-REL-009`). A
  store without such a verified run is reported as `unverified` rather than
  inferred from historical GitHub tags.
- **Public package registries (`nuget.org`, `registry.npmjs.org`, `pypi.org`,
  `rubygems.org`):** unauthenticated registry metadata aligned with each unit's
  configured `version.json` release/prerelease line or static `pyproject.toml`
  version (including npm prerelease dist-tags matching `NpmjsPublication.DistTag`
  and PyPI published `.dev` releases), correlating NuGet `.nuspec` repository
  commits, npm `gitHead`, or repository `release/<unit>/v*` tags to local Git
  commits when present, and falling back to commits after the published
  version's registry timestamp otherwise.

## Scheduled Mechanism and Ephemeral Ticket Lifecycle

Per [GOV-007](../governance/governance-system.md#gov-007-trigger-bound-deferral):

- **Trigger:** `.github/workflows/release-triage.yml` runs monthly (`23 7 1 * *`
  UTC) and on manual `workflow_dispatch` against the repository's default
  branch (`main`).
- **Evaluator:** `eng/scripts/release_triage.py`, running with job-scoped
  `actions: read`, `contents: read`, `deployments: read`, and `issues: write`
  on the default `GITHUB_TOKEN` (`github-actions[bot]`).
- **Action when triggered:** maintains at most one open Issue labeled
  `release-triage`:
    - **No pending units:** exits silently when no open `release-triage` Issue
      exists, or posts a closing summary comment and closes an open
      `release-triage` Issue once all pending units clear.
    - **Pending units present:** embeds a per-unit state map
      (`<!-- release-triage-state: {...} -->`) recording each pending unit's
      current delivery-affecting input head. When no open Issue exists and every
      pending unit matches the most recently closed `release-triage` Issue's state
      map (owner reviewed and deferred), the run stays silent until a new
      delivery-affecting commit lands. When new untriaged state appears, the
      workflow creates a `release-triage` Issue assigned to `hcoona` with an
      `@hcoona` mention—or appends an `@hcoona` comment carrying the refreshed
      report and state map on an existing open Issue—so GitHub delivers native
      Participating notifications without external notification secrets or
      rewriting existing Issue bodies.
- **Owner disposition and fallback review:** the repository owner decides
  whether to dispatch `workflow-delivery-v3-*-release.yml` (dry-run then live
  with Environment approval) or close the ticket to defer until further changes
  accumulate. Merges changing `src/public/**/workflow-delivery.release-unit.yml`
  or [`docs/delivery-wave.md`](../delivery-wave.md) serve as the fallback
  applicability review.
