# GitHub Workflows Design (v2.8)

This document describes the GitHub Actions workflow architecture for the `three` monorepo.

> **Scope constraint:** Each project maps to exactly one language ecosystem. Multi-language projects (e.g., a C# library with a companion npm package) are out of scope and must be split into separate project directories with separate `release.json` files.

> **Release-unit constraint:** Each `buddy.yml` or `official.yml` run releases exactly one project. Coordinated multi-project release orchestration is out of scope for this design.

## 1. Architecture Overview (Shared Execution Layer)

To avoid duplicating build and deploy logic across three entry workflows, the design adopts a shared execution layer made of reusable workflows plus reviewed local composite actions. Each entry workflow independently invokes the checked-in execution paths it needs — there is no single dispatching hub.

**Entry layer (Entry Workflows):** `ci.yml`, `buddy.yml`, `official.yml`

**Execution layer (Reusable Workflows under `.github/workflows/`):**

- `_build-test-csharp.yml` — runs on `windows-latest`
- `_build-test-python.yml` — runs on `ubuntu-latest`
- `_build-test-jsts.yml` — runs on `ubuntu-latest`
- `_build-test-ruby.yml` — runs on `ubuntu-latest`
- `_publish-nuget.yml` — publishes `.nupkg` to the GitHub Packages NuGet feed for buddy runs
- `_publish-npm.yml` — publishes npm tarballs to the GitHub Packages npm registry for buddy runs
- `_publish-rubygems.yml` — publishes gems to the GitHub Packages RubyGems-compatible host for buddy runs

The split axis is still **ecosystem (tooling)**, not destination, but official publication is now one explicit exception. GitHub OIDC binds external trust to the workflow identity that actually mints the token, and the public provider contracts reviewed for March 2026 document GitHub Actions trusted publishing for `npmjs`, `PyPI`, `RubyGems.org`, and `NuGet.org`, but they still differ in selector fields, login helpers, and documented unsupported compositions. To remove that ambiguity entirely, every official external publish job (`nuget:official`, `npm:official`, `pypi:official`, and `rubygems:official`) and the official GitHub Release publish job (`github:official`) run directly in `official.yml`. Reviewed local composite actions or helper scripts may still encapsulate step sequences inside those direct jobs, including the final publish or mutation command, because they execute under the same `official.yml` job identity after the job-level environment and permission gates are established. What is forbidden is reusable-workflow indirection or any other separate workflow hop for official publication. Each publish job still mutates **exactly one** destination.

For `official.yml`, the protected control-plane branch set is the default branch `main` plus eligible protected maintenance branches `release/<project-name>/v<release-line>`, where `<release-line>` is the numeric release line such as `1.2.x` without a leading `v`. The branch selected in the `workflow_dispatch` UI, or the branch ref supplied through the REST API, supplies both the trusted workflow/control-plane code and the release payload source for that run. Tag refs and every other non-branch ref are unsupported in this design and must hard-fail before checkout. Official release tags are derived and created by the workflow itself from the selected protected source ref after validation succeeds; they are not external workflow inputs.

Trusted control-plane **code** follows the same rule with one stricter exception for privileged monitoring reads. For `official.yml`, the caller workflow, every reusable workflow, every composite action, and every helper script that performs privileged release gating or publishing come from the same dispatch-selected protected source branch once the run leaves `preflight-check`. The checked-in control-plane **state** is separate: `.github/official-caller-refs.json`, `.github/official-dispatch-authorizers.json`, `.github/publish-trust-inventory.json`, `.github/provenance-signer-map.json`, and `.github/release-recovery-ledger.jsonl` are authoritative only on `refs/heads/main`. Official runs must therefore freeze one control-plane snapshot from `main` in `authorize-dispatcher`, pass that immutable SHA into `preflight-check`, re-emit it unchanged from `preflight-check`, and use that same snapshot consistently for branch eligibility, dispatcher authorization, trust-inventory validation, signer-map validation, and any helper code executed while `control-plane-monitoring` credentials are live. Recovery-ledger schema validation is enforced in CI when the ledger changes; official-time behavior instead consumes the reviewed recovery helpers and runbook semantics defined later in this design. During `preflight-check`, the dispatch-selected protected source branch is treated as data-only input rather than as a trusted source of helper code.

**Secrets:**

- **Build-test workflows** have no secret requirements. Callers must pass `secrets: {}` and must not pass any non-empty `secrets:` map. This blocks caller secret forwarding and limits the blast radius if a compromised dependency or malicious test reads the environment during build/test execution. It does **not** disable the job `GITHUB_TOKEN`; that token is controlled only by `permissions`.
- **Reusable publish workflows** must also be called with `secrets: {}` and must not receive any non-empty `secrets:` map. Direct official publish jobs are ordinary jobs rather than reusable-workflow calls, so they do not declare a job-level `secrets:` map; instead they rely on job `permissions`, OIDC, and environment-scoped credentials. Blanket `secrets: inherit` is prohibited in this design.

Permissions are inherited automatically: a reusable workflow receives the caller job's `permissions` grants as long as the reusable workflow itself does **not** declare its own `permissions` block. This is what allows the same `_publish-nuget.yml` to operate under `packages: write` when called from `buddy.yml`, while all official publication happens directly in `official.yml` and buddy publish workflows stay reusable.

> **Important constraint:** Reusable workflows must NOT declare their own `permissions:` block. If they do, only the scopes explicitly declared there remain eligible, and each of those scopes is still capped by the caller job's grant. Undeclared scopes become `none` even if the caller granted them. For example, if a reusable workflow declares `permissions: { id-token: write }` and the caller grants only `packages: write`, the minted token will have both `id-token: none` and `packages: none`, causing silent runtime failures. Keep all `permissions:` declarations in the entry workflows only.
>
> This rule must be lint-enforced in repository policy. In addition to `actionlint`, the repository must run a custom `hk` validation that matches `^\.github/workflows/_.*\.yml$` and fails if any such reusable workflow declares either a workflow-level or job-level `permissions:` block. This rule has no exceptions in this design.

> **Important constraint:** Shell input hardening applies to both entry workflows and reusable workflows. No `run:` step may interpolate `${{ inputs.* }}`, `${{ github.event.inputs.* }}`, `${{ github.* }}`, `${{ needs.*.outputs.* }}`, `${{ env.* }}` values derived from those contexts, or any other untrusted expression directly into shell source. In practice, shell source must not contain `${{ ... }}` expansions for untrusted values at all. All such values must first be mapped under `env:` and then referenced only as quoted shell variables such as `"$PROJECT_NAME"`.

> **Important constraint:** Mapping untrusted values through `env:` is necessary but not sufficient. Shell steps must also ban `eval`, `bash -c`, PowerShell `Invoke-Expression`, and other dynamic command construction with untrusted data; must pass any untrusted positional value after an explicit `--` end-of-options separator when the target CLI supports one; must write `GITHUB_ENV`, `GITHUB_OUTPUT`, `GITHUB_PATH`, and `GITHUB_STEP_SUMMARY` only through helpers that reject embedded newlines and delimiter injection; must not derive here-doc delimiters or workflow-command file syntax from untrusted values; must not enable shell tracing around credential-handling steps; and must not print untrusted values to stdout or stderr in legacy workflow-command form. Repository policy must lint these sinks in addition to checking for direct `${{ ... }}` interpolation in `run:` blocks. Where Bash is used in trusted shell steps, the workflow or composite action must declare `shell: bash` explicitly so the reviewed hardening contract is not left to runner defaults. The allowed workflow-command-file writers are not implicit: `hk.pkl` must define an explicit reviewed allowlist of helper entrypoints, and any direct command-file write outside that allowlist is a hard failure. Those helper entrypoints must live only under trusted control-plane paths already covered by `CODEOWNERS` and the `infra` inventory.

> **Important constraint:** The same shell-hardening rule also applies to local composite actions under `.github/actions/**`. Any value received through a composite action's `with:` inputs is still untrusted at the point where that composite action consumes it. Composite-action `run:` steps must therefore map those values through `env:` before use, must avoid direct `${{ inputs.* }}` interpolation in shell source, and must be covered by the same repository-policy linting for unsafe workflow-command-file writes and dynamic shell execution.

**Protected environments:** Official releases use one protected GitHub environment per mutable destination plus two dedicated write environments and one non-human-gated monitoring environment. The publish environments are `production-nuget-<project-name>`, `production-npm-<project-name>`, `production-pypi-<project-name>`, `production-rubygems-<project-name>`, and `production-github-<project-name>`. Each official publish job must enter only its own exact target environment; sharing one environment across multiple trusted-publisher jobs is unsupported because it would collapse the provider-facing trust boundary and let one compromised job mint OIDC tokens for unrelated audiences. `production-tag-write-<project-name>` is a separate tag-reservation environment used only by `create-release-tag`; it carries the release-tag writer App private key and must never be referenced by registry, provenance, or GitHub Release publish jobs. `production-evidence-write-<project-name>` is a separate evidence-persistence environment used only by `require-provenance`; it carries the release-evidence writer App private key and must never be referenced by registry, GitHub Release, or tag-reservation jobs. `control-plane-monitoring` is a repository-scoped environment used only by `preflight-check` and the allowlisted monitoring workflows that need audit credentials and outbound monitor secrets; the ruleset-auditor App remains operationally read-only for this design, but GitHub's current Rulesets API requires the minimum write-level ruleset permission to read `bypass_actors`, so that credential must be isolated there explicitly.

The target-specific publish environments plus the tag-write and evidence-write environments must each use the same exact deployment branch set for the project, the same required-reviewer protection profile, and no environment wait timer. `control-plane-monitoring` is intentionally different: it must have **no** required reviewers and no wait timer, because GitHub Environments block all jobs that reference an environment until one configured reviewer approves them, and scheduled or `workflow_run`-triggered monitors therefore cannot function if they depend on a human-gated environment. `control-plane-monitoring` must instead rely on exact deployment-branch policy, zero registry credentials, and repository policy that limits which workflows may reference it. Intentional release holds are operational decisions outside the workflow, not durable checked-in exception state, so approval-latency monitoring in this design measures only human review latency. GitHub Environments still treat required reviewers as a one-of-N approval pool rather than a dual-approval gate, so the at-least-two-reviewer requirement in this design is reviewer-pool redundancy, not a claim that GitHub will require two approvals. Every protected environment in this design must also define an environment-scoped variable `CONTROL_PLANE_ENVIRONMENT_ROLE` whose exact value identifies its intended role: `monitoring`, `tag-write`, `evidence-write`, `publish-nuget`, `publish-npm`, `publish-pypi`, `publish-rubygems`, or `publish-github`. Every environment-gated job must verify the expected value before any GitHub App token mint, OIDC request, or external mutation. This is a mandatory fail-closed guard against GitHub's documented behavior of auto-creating a referenced missing environment without protection rules or secrets.

**Permissions model:** Every entry workflow declares `permissions: {}` at workflow level. Individual jobs then request only the scopes they need (principle of least privilege). Key scopes:

| Job kind                                         | Required `permissions` |
| ------------------------------------------------ | ---------------------- |
| Read repository metadata / releases              | `contents: read`       |
| Read pull request file lists                     | `pull-requests: read`  |
| Read-only checkout and trusted helper code       | `contents: read`       |
| Push protected official release tags             | `contents: read` on the job `GITHUB_TOKEN`; the actual protected-tag write uses a dedicated GitHub App installation token |
| Create or update official GitHub Releases        | `contents: write`      |
| Publish to GitHub Packages                       | `packages: write`      |
| Generate official attestations in the isolated language-matching attestation job | `contents: read`, `id-token: write`, `attestations: write` |
| Verify build attestations and persist durable evidence in `require-provenance` | `contents: read` |
| Trusted-publisher publish to supported official registries | `contents: read`, `id-token: write` |

Official registry auth is intentionally split by documented provider capability, but all four external production registries use GitHub Actions trusted publishing in this design. `npmjs`, `PyPI`, and `RubyGems.org` use their documented trusted-publisher flows directly in `official.yml`. `NuGet.org` uses GitHub Actions trusted publishing through `NuGet/login@v1` or a reviewed successor that mints a short-lived API key inside the approved environment; no long-lived `NUGET_API_KEY` secret is part of this design. npmjs may auto-generate provenance for eligible public packages when trusted publishing is used. GPR feeds use `GITHUB_TOKEN` with `packages: write` instead.

Because GitHub attestations are bound to the job that generates them, this design isolates attestation generation into a dedicated language-matching attestation job that performs no dependency installation, package restore, or test execution. The release-mode build job therefore runs without OIDC, produces only the manifest-selected artifacts plus deterministic verification inputs, and uploads them as immutable workflow artifacts. The attestation job downloads that artifact set, validates it, mints the OIDC-backed attestation bundles, and uploads a provenance sidecar artifact. `require-provenance` then downloads both artifacts, verifies the bundles against the expected attestation-job identity, and writes the durable evidence record. `require-provenance` must not mint a second attestation set for already-built artifacts.

GitHub Releases management does not expose a narrower `releases: write` permission in GitHub Actions. `contents: write` is therefore the minimum available scope for the direct `publish-github-official` job in this design.

That `contents: write` grant is repository-scoped rather than project-scoped. The target-specific `production-github-<project-name>` environments in this design provide approval and review isolation, not a separate GitHub Release permission namespace.

The GitHub App installation tokens used by `create-release-tag` and `require-provenance` are repository-scoped rather than ref-scoped. Ref-level isolation for `refs/tags/release/**` and `refs/heads/release-evidence` therefore comes from repository rulesets plus the dedicated protected environments, not from a narrower App permission shape.

Because GitHub's write credentials are repository-scoped primitives in this design, project-scoped environments must not be described or treated as independent write namespaces. Instead, every job that mints `contents: write` or a repository-scoped GitHub App write token must do so only immediately before one reviewed project-scoped mutation helper step, must pass exact expected values such as `project-name`, release-tag prefix, evidence path prefix, release tag, and deterministic GitHub Release title into that helper, and must execute no other checkout-sourced code, arbitrary shell expansion, or second mutation path while the credential is live.

> **Note:** With `permissions: {}` at workflow level, jobs that run `actions/checkout` or read GitHub release metadata must explicitly declare at least `permissions: { contents: read }`. In this design, `preflight-check`, build jobs, gate jobs, and any other job that performs a read-only checkout must therefore request `contents: read`. GitHub environment and ruleset metadata are still read through the dedicated audit App installation tokens rather than through the job `GITHUB_TOKEN`, because GitHub Actions does not document an `environments` workflow-permission key and the ruleset-auditor contract remains App-based.

**Repository protection model:** This design uses GitHub repository rulesets for protected branches and protected tags. Legacy branch-protection endpoints and compatibility shims are out of scope before implementation starts. Workflow preflight and policy validation query the Environments API plus the Repository Rulesets API only.

**Concurrency policy:** Each entry workflow defines a `concurrency:` group to prevent resource races:

- `ci.yml`: `group: ci-${{ github.ref }}`, `cancel-in-progress: true`
- `buddy.yml`: `group: buddy::${{ github.ref }}::${{ inputs.project-name }}`, `cancel-in-progress: false`
- `official.yml`: `group: official::${{ github.ref }}::${{ inputs.project-name }}`, `cancel-in-progress: false`

The `::` separator is intentional because Git ref names cannot contain `:` under `git check-ref-format`, and valid `project-name` values in this design also exclude `:`; this prevents ambiguous concatenation such as `feat` + `a-b` colliding with `feat-a` + `b`. GitHub Actions still compares concurrency groups case-insensitively, so releasable `project-name` values must also be unique under ASCII lowercase normalization across the repository. With `cancel-in-progress: false`, an in-progress run is preserved. GitHub Actions will replace any existing pending run with the newest queued run for the same concurrency group, so these release concurrency groups are admission locks rather than durable FIFO queues. Queue depth greater than one for the same group is unsupported. Before issuing any fresh dispatch for a concurrency group that already has queued or in-progress runs, operators must inspect the existing runs, then either cancel stale queued runs or wait for the in-progress run to settle rather than assuming the queue will preserve intent. The repository must also run a dedicated release-admission monitor that opens or updates an incident whenever a same-group run is queued behind another live run, or when a queued run in that group disappears or is cancelled without a matching operator-issued cancellation event.

Because workflow-level `concurrency.group` is computed before any job can normalize `github.ref`, this design also forbids mixed-case release workflow source refs. The official protected control-plane branch set is already lowercase-only by construction. Buddy dispatches must likewise use lowercase branch names only; mixed-case buddy source refs are unsupported because case-distinct refs would alias the same concurrency group.

The separator guarantee applies only after `project-name` validation. Because `buddy.yml` and `official.yml` compute their concurrency keys before `resolve-context` runs, an authorized dispatcher can still intentionally collide with another queued run by reusing the same valid `project-name`. Concurrency is therefore an operational coordination control, not a security boundary. A dispatch from a not-yet-eligible branch or otherwise invalid configuration still occupies its concurrency group until the run settles; operators must cancel the known-bad run before redispatching the corrected configuration for the same group. The reviewed dispatcher allowlist described later is therefore a secret-consumption and privileged-progress gate, not a native pre-dispatch admission gate: a non-allowlisted repository writer can still create and transiently occupy an `official.yml` concurrency slot until `authorize-dispatcher` fails.

For `official.yml`, the concurrency key intentionally serializes per `(ref, project-name)` pair rather than across all protected branches. This means the same project may still have concurrent official runs from different protected branches such as `main` and `release/<project-name>/v<release-line>`. That is acceptable only when those runs are expected to produce distinct release identities. Operators should not intentionally launch concurrent official runs for the same project from different protected branches unless that distinction is understood; GitHub Release identity conflict checks remain authoritative if those runs overlap.

**Job timeouts:** Every job must declare `timeout-minutes`, and workflow linting enforced through `hk`/`actionlint` should fail if any job omits it. Recommended defaults: `preflight-check`, resolution jobs, and static-analysis jobs `15`; Ubuntu build jobs `30`; Windows C# build jobs `45` because hosted Windows runners have materially higher startup and .NET restore/build/test overhead than Ubuntu runners; isolated attestation jobs `15`; `require-provenance` `45` because it now downloads the build artifact plus provenance sidecar artifact, verifies the attestation set, performs bounded evidence-branch append retries with immutable read-back verification, and must emit phase checkpoints for timeout diagnosis; publish jobs `25` except `publish-pypi-official` `35`; `confirm-publish-state` `45` because it performs bounded remote rechecks in parallel across the selected destinations, may spend up to 20 minutes on PyPI convergence while keeping 10-minute budgets for the other targets, and still needs explicit margin for runner startup, checkout, locked-tool bootstrap, checkpoint persistence, and output serialization; lightweight tag-management jobs `10`; and terminal gate jobs `ci-passed` and `release-complete` `10` because those jobs must perform a read-only checkout and bootstrap `jq` from the locked `mise` toolchain before running their jq assertions. Some YAML snippets below omit `timeout-minutes` only for brevity; concrete workflow files must still declare it.

**Action pinning:** All external actions, including GitHub-maintained actions under the `actions/` namespace, must be pinned to full commit SHA. Any `docker://` image reference must be pinned to an immutable digest such as `@sha256:...`. Local composite actions under `.github/actions/**` are sourced from the checked-out protected workspace, must be explicitly covered by `CODEOWNERS`, and are governed by the same branch protection and `CODEOWNERS` review as the caller workflow rather than by a separate pin. Use Renovate or Dependabot to manage external action updates:

```yaml
uses: dorny/paths-filter@de90cc6ed7cd597cb74b84a7e832ce805e3c7b15 # v3.0.2
```

The repository's dependency-update automation must cover `.github/workflows/**` so pinned SHAs are refreshed intentionally rather than drifting indefinitely.

Repository policy must run both `actionlint` and `zizmor` in strict mode through `hk`. `actionlint` covers workflow syntax and common semantics. `zizmor` is the authoritative enforcement layer for full-SHA action pinning, digest-pinned `docker://` references, prohibiting `secrets: inherit`, and rejecting `on: pull_request_target`. `on: workflow_run` remains prohibited everywhere except the exact reviewed workflow files `.github/workflows/official-run-health-monitor.yml` and `.github/workflows/control-plane-post-tag-failure.yml`; no other workflow may declare that trigger. The only workflows that may reference `control-plane-monitoring` are `.github/workflows/official.yml`, `.github/workflows/control-plane-drift-monitor.yml`, `.github/workflows/official-run-health-monitor.yml`, `.github/workflows/control-plane-post-tag-failure.yml`, `.github/workflows/open-incident-freshness-monitor.yml`, `.github/workflows/release-operational-audit.yml`, and `.github/workflows/governance-and-runbook-freshness.yml`. Each allowlisted monitoring workflow must hold zero registry credentials, may request no repository write scope beyond the minimum needed to open or update recovery issues, may call the GitHub Releases API only for read-only confirmation and audit queries, must declare an explicit `workflows:` allowlist plus an exact `branches:` filter and a runtime exact-ref check against the caller-ref registry frozen from `main` when it uses `workflow_run`, must self-validate `github.workflow_ref` against its own exact reviewed workflow path before reading any environment secret or minting any GitHub App token, and must not download, execute, or restore artifacts, caches, or code from the triggering run. `.github/workflows/official.yml` is the only non-monitoring workflow allowed to reference `control-plane-monitoring`, and only for `preflight-check`. The only artifact-download exception in this design is `.github/workflows/control-plane-post-tag-failure.yml`, which may download only the immutable `tag-reservation-result-<project-name>` artifact from the triggering `official.yml` run as data-only input for recovery correlation; it must not download any other artifact, cache, or code from the triggering run, and it must treat that artifact as untrusted data rather than executable content. Privileged workflows in this design must also reject `issue_comment`, `pull_request_review`, `pull_request_review_comment`, and `pull_request_review_thread` triggers unless a future design adds a dedicated actor-validation model for them. `hk.pkl` must also define the repository-specific policy checks that `actionlint` and `zizmor` do not provide: no reusable-workflow `permissions:` blocks, no unsafe shell sinks or unreviewed workflow-command-file writers in `.github/workflows/**`, `.github/actions/**`, and `eng/scripts/**`, no self-hosted runner labels on any job that requests `id-token: write`, reads `control-plane-monitoring`, mutates a registry, mints repository-scoped GitHub App write credentials, or mutates protected tags, durable evidence refs, or GitHub Release state, exact `infra` inventory coverage, exact workflow self-checks before any job reads `control-plane-monitoring` secrets, and a digest-pinned MISE backend allowlist.

**Tool lock enforcement:** `mise.lock` is required repository state, not optional convenience metadata. `hk check --all` must fail when the repository root lacks `mise.lock`, and any intentional toolchain update must regenerate the lockfile with `mise lock` in the same change that modifies `mise.toml`. Gate jobs that rely on `jq`, including `ci-passed`, `confirm-publish-state`, and both `release-complete` jobs, and provenance jobs that rely on `gh`, including `require-provenance`, must obtain those tools from the repository-managed `mise` toolchain rather than from the runner image, so both `jq` and `gh` are part of the reviewed locked toolchain for this design. Those jobs must therefore perform a read-only checkout, restore a tool cache keyed by `mise.toml` and `mise.lock`, and run `mise install` before the relevant gate step. Zero-permission jq gates are unsupported. The locked `jq` version must be at least `1.6`, and the locked `gh` version must be a reviewed release that exposes the verified JSON fields consumed by Section 8; runner-native `gh` is unsupported for official provenance verification. The reviewed MISE backend policy must also be machine-enforced in `hk.pkl`: only backends whose lockfile entries include immutable upstream digests are allowed in official build or publish paths. Version-string-only backends are unsupported in official build or publish paths.

## 2. `ci.yml` — PR Validation (Targeted Concurrency, Shift-Left)

**Trigger:** `on: pull_request`

Because `pull_request` runs evaluate the PR merge commit, local workflow files, composite actions, and helper scripts execute from the PR-provided tree. The repository must therefore require explicit approval before workflows from forked pull requests may run. Without that repository setting, `ci-passed` is not a trusted release-gating signal for fork PRs.

For private repositories, this design does not rely on forked `pull_request` workflow execution at all. GitHub does not pass repository or organization secrets to fork-triggered `pull_request` runs, so the metadata-App path used elsewhere in this design is unavailable there. External contributions to a private repository must therefore be mirrored into same-repository branches by a maintainer before this workflow may run.

Because GitHub documents the repository Actions settings needed for fork-PR approval and pull-request write-token policy behind Administration-read APIs, this design does **not** attempt to re-verify those live repository settings from untrusted `pull_request` runs. Those settings are instead repository prerequisites audited by control-plane monitoring from trusted credentials on `main`; `ci.yml` validates checked-in control-plane files and affected test scope only.

CI does not build everything on every PR. It uses path filtering (`dorny/paths-filter`, SHA-pinned) to run only the affected language test suites.

**Jobs:**

1. **`static-analysis`**:
    - `permissions: { contents: read }`
    - Runs `jdx/hk` (`hk check --all`) on an Ubuntu runner. HK auto-detects file types from its configuration (`hk.pkl`), serving as the first gate for formatting and linting failures.
    - The same whole-repo analysis must also enforce the single-language project scope for releasable projects: repository policy must hard-fail if any candidate releasable project root resolves to more than one workflow language.
    - This job is intentionally unconditional whole-repo analysis and must not acquire an `if:` guard without a matching `ci-passed` contract change. If HK wall time ever becomes a material bottleneck, replace it with an explicit path-aware design rather than silently making the job skippable.

2. **`detect-changes`**: Uses `dorny/paths-filter` to classify modified files:
    - `permissions: { pull-requests: read }`
    - `csharp`: `['**/*.cs', '**/*.csproj', '**/*.fsproj', '**/*.vbproj', 'global.json', 'Directory.*.props', 'nuget.config', '**/NuGet.Config', '**/*.targets', '**/packages.lock.json']`
    - `python`: `['**/*.py', 'pyproject.toml', 'uv.lock']`
    - `jsts`: `['**/*.ts', '**/*.js', '**/*.mjs', '**/*.cjs', 'package.json', 'pnpm-workspace.yaml', 'pnpm-lock.yaml', 'biome.jsonc', 'tsconfig*.json']`
    - `ruby`: `['**/*.rb', '**/*.gemspec', 'Gemfile', 'Gemfile.lock']`
    - `infra`: `['.github/workflows/**', '.github/actions/**', '.github/CODEOWNERS', '.github/official-caller-refs.json', '.github/official-dispatch-authorizers.json', '.github/publish-trust-inventory.json', '.github/provenance-signer-map.json', '.github/release-recovery-ledger.jsonl', 'eng/scripts/**', '**/release.json', '**/version.json', 'mise.toml', 'mise.lock', 'hk.pkl', 'PklProject', 'PklProject.deps.json', 'global.json', 'nuget.config', '**/NuGet.Config', 'Directory.*.props', '**/*.targets', 'package.json', 'pyproject.toml', 'biome.jsonc', 'pnpm-workspace.yaml', 'pnpm-lock.yaml', '.npmrc', '**/.npmrc', 'uv.lock', 'Gemfile.lock', '**/packages.lock.json', 'Directory.Packages.props']`

    When `infra` changes are detected, all language test suites are triggered regardless of other filters.

    The `infra` filter is the authoritative CI-maintained inventory of trust-bearing control-plane files for release validation. Any reviewed change that adds, removes, or renames trusted helper code or shared dependency-control files consumed by official build or release jobs must update this filter in the same PR; there is no separate compatibility fallback to a broader implicit catch-all.

    Repository policy must machine-enforce that invariant rather than relying on reviewer memory alone. In addition to `dorny/paths-filter`, `hk check --all` must run a control-plane inventory check that recomputes the expected `infra` filter members from the trusted-file classes in this design and hard-fails on any missing entry, stale entry, or trusted file change that would not set `infra = true`.

    > **Scaling note:** The current filters operate at language level (`**/*.cs` triggers all C# builds). As the monorepo grows past ~10 projects per language, this should evolve to per-project granularity using affected-project detection from `eng/scripts/find_project_path.py`.

3. **`trusted-release-inventory`**:
    - `needs: [detect-changes]`
    - `permissions: { contents: read }`
    - Conditional: `if: needs.detect-changes.outputs.infra == 'true'`
    - Checks out the PR merge commit as data-only input and also checks out `refs/heads/main` as the trusted validator source, both with `persist-credentials: false`, and runs the repository-side drift check for `.github/publish-trust-inventory.json`. Any helper script, schema validator, or jq/Python logic executed by this job must come from the trusted `main` checkout; the PR checkout provides the candidate post-change control-plane data being validated. This job must recompute the expected post-change trust-bearing state from the PR checkout's candidate control-plane files and validated `release.json` files, using the trusted validator logic from `main` and deriving each project's exact `allowedCallerRefs` from the candidate `.github/official-caller-refs.json` as `refs/heads/main` plus only those maintenance refs that exactly match `refs/heads/release/<project-name>/v<major>.<minor>.x`. It must compare the exact normalized values of `schemaVersion`, `entryWorkflowPath`, and the per-project `language`, `currentMainReleaseLine`, derived `allowedCallerRefs`, `targets[*].publishExecutionPath`, `targets[*].environment`, `targets[*].authMechanism`, optional `targets[*].trustedPublisherSelector`, optional `targets[*].documentedOidcAudience`, and optional `targets[*].oidcAudienceEndpoint` fields. The same job must also cross-validate that every official target declared in each releasable project's candidate `release.json` has exactly one matching inventory target entry and that the candidate inventory does not contain any orphan official target absent from the project's validated candidate `release.json`. Any missing inventory update, stale mapping, malformed schema, mismatched current-main-release-line, mismatched environment mapping, mismatched auth mechanism, mismatched trusted-publisher selector, mismatched fixed-audience mapping, mismatched audience-discovery-endpoint mapping, release-config mismatch, or `schemaVersion != 2` is a hard failure.
    - The same job must also validate `.github/CODEOWNERS` whenever that file or any trust-bearing inventory file changes. Validation must prove that every trust-bearing path class named in this design remains covered by the dedicated release-engineering owners and that those patterns still cover `.github/workflows/**`, `.github/actions/**`, the checked-in trust inventories, and every reviewed helper path consumed by official release control-plane code.
    - The same job must also validate `.github/official-dispatch-authorizers.json` whenever that file changes, including `schemaVersion`, the strict top-level key whitelist, non-empty unique `users`, and exact-login string normalization rules.
    - The same job must also validate `.github/provenance-signer-map.json` against the Section 8 schema whenever that file changes, including `schemaVersion`, the exact top-level keys, the closed language set, the required `.github/workflows/official.yml` workflow path, and one exact attestation-job mapping for each enabled official language.
    - The same job must also validate `.github/release-recovery-ledger.jsonl` against the Section 7 schema whenever that file changes, including `schemaVersion`, strict key whitelists, `incidentId` UUID requirements, required `revision` monotonicity per `incidentId`, `auditId` UUID requirements, required `revision` monotonicity per `auditId`, required `releaseLine`, required `evidenceUrl`, the required incident and audit governance fields (`owner`, `severity`, `acknowledgedAt`, and `nextReviewAt` where applicable), closed sets, conditional `runAttempt`, `workflowRunUrl`, `artifactEvidenceUrl`, `closedAt`, `automationId`, `scriptVersion`, and `credentialId` rules, canonical target ordering, the `selectedTargets` / `publishedTargets` / `unpublishedTargets` partition invariants, the `deprecatedTargets`, `delistedTargets`, `removedTargets`, and `retainedTargets` subset invariants, the disposition-specific distinguishing rules for `open-before-publish`, `open-partial-publish`, `recovered`, `abandoned-before-publish`, `abandoned-after-partial-publish`, `burned`, `partially-withdrawn`, and `fully-withdrawn`, exhaustive target accounting for terminal published states, and the presence-and-absence rules for the hold-window evidence fields used in destructive stable-release recovery.

4. **`test-csharp` / `test-python` / `test-jsts` / `test-ruby`** (run in parallel):
    - `needs: [detect-changes, static-analysis]`
    - `permissions: { contents: read }`
    - Conditional: e.g. `if: needs.detect-changes.outputs.csharp == 'true' || needs.detect-changes.outputs.infra == 'true'`
    - Each calls its corresponding reusable workflow in `build-scope: ci` mode. In this mode the caller omits `project-path` and `project-name`, the reusable workflow executes the language-wide CI suite for the current checkout, and `require-provenance` must remain `false`. C# uses `windows-latest`; the others use `ubuntu-latest`.

5. **`ci-passed`** (final gate job):
    - `if: always()`
    - `permissions: { contents: read }`
    - `needs: [detect-changes, static-analysis, trusted-release-inventory, test-csharp, test-python, test-jsts, test-ruby]`
    - Before the jq assertion runs, this job must check out the repository read-only with `persist-credentials: false`, restore the `mise` tool cache keyed by `mise.toml` plus `mise.lock`, and run `mise install` so the jq gate uses the reviewed locked toolchain rather than the runner image.
    - Asserts all required checks either passed or were legitimately skipped. Including `detect-changes`, `static-analysis`, and `trusted-release-inventory` in `needs` ensures their failures block the gate — if `detect-changes` fails, all downstream conditional jobs are auto-skipped with `result: "skipped"`, and without `detect-changes` in `needs`, `ci-passed` would see only `"success"` and `"skipped"` results and falsely pass. The gate must also re-derive which language suites were required from `detect-changes.outputs` so a drifted `if:` condition on a `test-*` job cannot silently convert a required suite into a tolerated skip, and it must hard-fail if any expected `detect-changes` output key is missing or not one of `"true"` / `"false"`.
    - `detect-changes` and `static-analysis` are intentionally unconditional. Neither may acquire an `if:` guard without a matching `ci-passed` contract change.
    - The normative jq skeleton is logical rather than a literal requirement to pass the entire `needs` object through one environment variable. Implementations must project `needs` down to the compact set of fields the gate actually consumes before handing JSON to `jq`, so runner environment-size limits cannot silently become a correctness bug in larger monorepos.

    ```yaml
    ci-passed:
        if: always()
        permissions:
            contents: read
        needs: [detect-changes, static-analysis, trusted-release-inventory, test-csharp, test-python, test-jsts, test-ruby]
        runs-on: ubuntu-latest
        steps:
            - name: Assert all required checks passed or were legitimately skipped
              env:
                  GATE_INPUT_JSON: ${{ steps.collect-gate-input.outputs.gate-input-json }}
              run: |
                  jq -n -e '
                      (env.GATE_INPUT_JSON | fromjson) as $n
                      | ($n["detect-changes"].outputs) as $dc
                      | ($n["detect-changes"].result == "success")
                      and ($n["static-analysis"].result == "success")
                      and (["csharp", "python", "jsts", "ruby", "infra"]
                          | all(. as $key | ($dc[$key] == "true" or $dc[$key] == "false")))
                      and (if ($dc.infra == "true")
                          then $n["trusted-release-inventory"].result == "success"
                          else $n["trusted-release-inventory"].result == "skipped"
                          end)
                      and (if ($dc.csharp == "true" or $dc.infra == "true")
                          then $n["test-csharp"].result == "success"
                          else $n["test-csharp"].result == "skipped"
                          end)
                      and (if ($dc.python == "true" or $dc.infra == "true")
                          then $n["test-python"].result == "success"
                          else $n["test-python"].result == "skipped"
                          end)
                      and (if ($dc.jsts == "true" or $dc.infra == "true")
                          then $n["test-jsts"].result == "success"
                          else $n["test-jsts"].result == "skipped"
                          end)
                      and (if ($dc.ruby == "true" or $dc.infra == "true")
                          then $n["test-ruby"].result == "success"
                          else $n["test-ruby"].result == "skipped"
                          end)'
    ```

## 3. `buddy.yml` — Unofficial Release (Static Conditional Publish)

**Trigger:** `on: workflow_dispatch` only (no automated triggers).

**Inputs:**

| Input          | Type      | Required | Description                 |
| -------------- | --------- | -------- | --------------------------- |
| `project-name` | `string`  | Yes      | Project identity to release |

All workflow inputs must be mapped to intermediate environment variables before use in shell scripts (e.g., `env: PROJECT_NAME: ${{ inputs.project-name }}`; use `"$PROJECT_NAME"` in bash, never `${{ inputs.project-name }}` directly in `run:` blocks).

Same-repository `pull_request` runs from collaborators with repository write access remain inside the trusted-collaborator boundary for read-only CI only. Those runs must never mint production OIDC credentials, must never enter any `production-*` environment, and must never receive GitHub App write tokens.

Buddy is intentionally allowed to release from development branches. It does **not** require ancestry to `main` or to a maintenance release branch.

Even within the same language, different projects may have different packaging strategies (EXE, NuGet, wheel, etc.). The workflow resolves publish targets dynamically from project configuration.

**Jobs:**

1. **`resolve-context`**:
    - `permissions: { contents: read }`
    - **Runner and tooling:** Runs on `ubuntu-latest`. Requires `mise install` to bootstrap Python (for `eng/scripts/find_project_path.py`) and the .NET SDK. The `nbgv-python` adapter is sourced from the current checked-out repository workspace, not from an external package index, so trusted version resolution tracks the selected source ref rather than an out-of-band registry artifact. The `mise.toml` and `mise.lock` at the repo root pin tool versions and, where supported by the selected MISE backends, the exact download digests. Every tool used in an official build or publish path must use a digest-pinning backend; version-string-only backends are unsupported in this design. The job must hard-fail if `mise.lock` is absent, and should restore a tool cache keyed by both files before invoking `mise install`.
    - **Input validation:** As the first step (before any checkout or git operation), validate `project-name` with a full-string match against the character class `[a-z0-9][a-z0-9._-]*`, require length `1..100`, reject any occurrence of the substring `..`, reject trailing `.`, and reject any name that ends with `.lock`. Reject invalid names with a clear error. Releasable project identities in this design are ASCII-lowercase only so branch names, environment names, tag names, and concurrency keys all use one canonical spelling.
    - **Source ref policy:** Buddy intentionally permits dispatch from non-default branches, but only branch refs are supported. `workflow_dispatch` requests that name a tag or any other non-branch ref are hard failures before checkout. No ancestry check against `main` or any release branch is performed in this workflow.
    - Runs `eng/scripts/find_project_path.py` to determine the project path and the workflow language. `project-name` is ASCII-lowercase and must resolve to exactly one releasable project in the repository whose leaf directory name uses that same canonical lowercase spelling. The resolution step must emit exactly one of `{csharp, python, jsts, ruby}` for `language`; no match, ambiguous match, unsupported language, or resolver error is a hard failure.
    - **NBGV resolution:** The checkout must use `fetch-depth: 0` so NBGV can compute version height from git history. Read-only checkouts in this job must also use `persist-credentials: false`. All jobs that use NBGV or rely on git-history-derived metadata must also checkout with full history. The script locates the correct `version.json` by searching upward from the project directory. In this design, inheriting the nearest ancestor `version.json` when the project root does not have its own local file is intentional, but the governing file must still resolve uniquely. "Resolve deterministically" therefore means: on a full-history checkout of the selected ref, `nbgv-python` finds exactly one governing `version.json`, emits exactly one normalized version string, and that string passes the language-specific validator. Missing history, ambiguous governing configuration, or validator failure are all non-deterministic failures. `eng/scripts/validate_pep440_version.py` must reject non-canonical normalized PEP 440 strings, all epoch markers (`!`), and all local version identifiers (`+...`); `eng/scripts/validate_semver2_version.py` must reject official release versions that contain SemVer build metadata (`+...`) rather than leaving equal-precedence handling to downstream npm rules. If `nbgv-python` cannot resolve the version deterministically, the job must hard-fail; there is no fallback or manual override path in this design. Version validation is performed programmatically using the existing scripts: `eng/scripts/validate_semver2_version.py` (for NuGet and npm), `eng/scripts/validate_rubygems_version.py` (for the repository's supported RubyGems-compatible subset), or `eng/scripts/validate_pep440_version.py --channel buddy` (for Python/PyPI). The Python validator must receive that explicit channel flag; default-mode inference is unsupported. The NBGV-resolved value becomes the workflow output `version` and the single buddy release identity for that run. Because buddy intentionally allows dispatch from development branches, the computed buddy version may differ across branches or after additional commits change git history height; that is expected behavior rather than a recovery-path bug.
    - Reads the project's release configuration (see **Section 5: Release Configuration Contract**) and emits a JSON array of publish targets. Targets use the format `ecosystem:destination` (e.g. `["nuget:gpr", "nuget:official"]`).
    - **Strictly validates** `release.json` exactly as specified in **Section 5** before any channel filtering occurs.
    - **Language-target validation:** Before channel filtering, validate every declared target against the resolved project language. `csharp` projects may declare only `nuget:*` and `github:official`; `jsts` projects may declare only `npm:*` and `github:official`; `python` projects may declare only `pypi:official` and `github:official`; `ruby` projects may declare only `rubygems:*` and `github:official`. Cross-ecosystem target declarations are hard failures.
    - After that validation succeeds, `buddy.yml` filters to the unofficial target set `{nuget:gpr, npm:gpr, rubygems:gpr}`. Targets that belong to the official channel are filtered out only **after** strict validation succeeds. Unknown or duplicate target values are hard failures. Before the generic empty-filtered-set failure is evaluated, a resolved `python` project must fail with an explicit language-specific error stating that Python currently has no unofficial publish channel in this design. Non-Python projects use the generic empty-filtered-set failure.
    - **GitHub Packages immutability in workflow scope:** GitHub supports deleting and restoring package versions with elevated package-admin capabilities, but this design does not request delete or admin package permissions and does not support delete-and-republish recovery. Within this workflow design, GPR package versions are treated as immutable release identities.
    - **Outputs:** `language`, `project-name`, `project-path`, `version`, `targets` (compact JSON array of filtered unofficial targets in canonical unofficial-target order `nuget:gpr`, `npm:gpr`, `rubygems:gpr`).
    - **On failure**, the script must print: the resolved project path, the contents of `release.json` if found, and the specific validation rule that was violated.

2. **`static-analysis`**:
    - `needs: [resolve-context]`
    - `permissions: { contents: read }`
    - Checks out the source ref for this workflow run before enumerating files. Read-only checkout must use `persist-credentials: false`.
    - Restores the `mise` tool cache keyed by `mise.toml` and `mise.lock`, runs `mise install`, then runs `hk check <project-path>` scoped to the resolved project path. HK receives the project path directly and discovers applicable files under that path according to `hk.pkl`; this design does not pre-enumerate file names in shell.
    - This scoped analysis is an intentional trust boundary: `buddy.yml` does not perform whole-repo control-plane validation of the selected development branch. Unofficial-channel safety relies on GitHub Packages immutability, artifact validation, and per-target idempotency checks rather than on official-channel control-plane guarantees.

3. **`build-csharp` / `build-python` / `build-jsts` / `build-ruby`** (static conditional jobs):
    - `needs: [resolve-context, static-analysis]`
    - `permissions: { contents: read }`
    - Because GitHub Actions resolves `uses:` statically at parse time, a single job cannot dynamically select a reusable workflow at runtime. Instead, four separate jobs are defined with conditional execution:

    ```yaml
    build-csharp:
        needs: [resolve-context, static-analysis]
        permissions:
            contents: read
        if: needs.resolve-context.outputs.language == 'csharp'
        uses: ./.github/workflows/_build-test-csharp.yml
        with:
            build-scope: release
            checkout-ref: ${{ github.sha }}
            project-path: ${{ needs.resolve-context.outputs.project-path }}
            project-name: ${{ needs.resolve-context.outputs.project-name }}
            require-provenance: false
        secrets: {}

    build-python:
        needs: [resolve-context, static-analysis]
        permissions:
            contents: read
        if: needs.resolve-context.outputs.language == 'python'
        uses: ./.github/workflows/_build-test-python.yml
        with:
            build-scope: release
            checkout-ref: ${{ github.sha }}
            project-path: ${{ needs.resolve-context.outputs.project-path }}
            project-name: ${{ needs.resolve-context.outputs.project-name }}
            require-provenance: false
        secrets: {}

    build-jsts:
        needs: [resolve-context, static-analysis]
        permissions:
            contents: read
        if: needs.resolve-context.outputs.language == 'jsts'
        uses: ./.github/workflows/_build-test-jsts.yml
        with:
            build-scope: release
            checkout-ref: ${{ github.sha }}
            project-path: ${{ needs.resolve-context.outputs.project-path }}
            project-name: ${{ needs.resolve-context.outputs.project-name }}
            require-provenance: false
        secrets: {}

    build-ruby:
        needs: [resolve-context, static-analysis]
        permissions:
            contents: read
        if: needs.resolve-context.outputs.language == 'ruby'
        uses: ./.github/workflows/_build-test-ruby.yml
        with:
            build-scope: release
            checkout-ref: ${{ github.sha }}
            project-path: ${{ needs.resolve-context.outputs.project-path }}
            project-name: ${{ needs.resolve-context.outputs.project-name }}
            require-provenance: false
        secrets: {}
    ```

    Only one of these four jobs will actually execute. Build artifacts (`.nupkg`, `.whl`, `.exe`, `.gem`, etc.) are uploaded to CI Artifacts using a deterministic name: `build-output-<project-name>` (e.g. `build-output-my-library`). Artifacts are built fresh within this workflow run; no artifacts from prior runs are downloaded. Build workflows must produce reproducible package outputs for the same source commit and locked toolchain so reruns can satisfy remote-identity idempotency checks.

4. **Publish jobs** (static conditional, one job per ecosystem-destination pair):

    Because GitHub Actions resolves `uses:` statically at parse time, and each reusable workflow call publishes to **exactly one** destination, publish jobs are split per ecosystem-destination pair. Each job has its own `if:` guard using `fromJson()` for exact array membership (not substring matching). That guard must assert `resolve-context.result == 'success'`, `static-analysis.result == 'success'`, exact target membership, and that the single language-matching build job finished with `result == 'success'` while the three non-matching build jobs finished with `result == 'skipped'`; buddy publish jobs must not rely on downstream `release-complete` alone to prove the required build succeeded. `always() && !cancelled() && !failure()` remains necessary so selected publish jobs are not suppressed merely because the unrelated build jobs were skipped:

    Adding a new supported language requires updating every buddy publish-job `if:` guard that maps filtered targets to the single language-matching build result, the `require-provenance` build predicate in `official.yml`, and the `buildJobs` maps embedded in both `release-complete` jq gates. This N-by-M update surface is a GitHub Actions platform constraint caused by static `uses:` resolution, not a runtime-dispatch choice. Code generation may reduce authoring toil, but it does not remove the underlying runtime structure; the change still must be updated atomically with any language expansion.

    ```yaml
    publish-nuget-gpr:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            contents: read
            packages: write
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.resolve-context.outputs.language == 'csharp' &&
            needs.build-csharp.result == 'success' &&
            needs.build-python.result == 'skipped' &&
            needs.build-jsts.result == 'skipped' &&
            needs.build-ruby.result == 'skipped' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'nuget:gpr')
        uses: ./.github/workflows/_publish-nuget.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            checkout-ref: ${{ github.sha }}
            caller-workflow-path: .github/workflows/buddy.yml
            version: ${{ needs.resolve-context.outputs.version }}
            feed-url: https://nuget.pkg.github.com/hcoona/index.json
        secrets: {}

    publish-npm-gpr:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            contents: read
            packages: write
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.resolve-context.outputs.language == 'jsts' &&
            needs.build-csharp.result == 'skipped' &&
            needs.build-python.result == 'skipped' &&
            needs.build-jsts.result == 'success' &&
            needs.build-ruby.result == 'skipped' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'npm:gpr')
        uses: ./.github/workflows/_publish-npm.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            checkout-ref: ${{ github.sha }}
            caller-workflow-path: .github/workflows/buddy.yml
            version: ${{ needs.resolve-context.outputs.version }}
            registry: https://npm.pkg.github.com
            dist-tags: '["buddy"]'
        secrets: {}

    publish-rubygems-gpr:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            contents: read
            packages: write
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.resolve-context.outputs.language == 'ruby' &&
            needs.build-csharp.result == 'skipped' &&
            needs.build-python.result == 'skipped' &&
            needs.build-jsts.result == 'skipped' &&
            needs.build-ruby.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'rubygems:gpr')
        uses: ./.github/workflows/_publish-rubygems.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            checkout-ref: ${{ github.sha }}
            caller-workflow-path: .github/workflows/buddy.yml
            version: ${{ needs.resolve-context.outputs.version }}
            host: https://rubygems.pkg.github.com/hcoona
        secrets: {}
    ```

    - The `if: always() && !cancelled() && !failure()` guard ensures the publish jobs run despite the three skipped build jobs in the `needs` chain. This condition is safe because skipped jobs are treated as neither failure nor cancellation.
    - Including `static-analysis` directly in each publish job's `needs` keeps the gate explicit and allows the job to assert `needs.static-analysis.result == 'success'` directly rather than relying on transitive failure propagation alone.
    - For GPR targets, auth uses `GITHUB_TOKEN` with `packages: write`. No OIDC is needed.
    - All buddy publish jobs use `secrets: {}`. No repository, organization, or environment secrets are forwarded by default.
    - Each publish step uses idempotent publish logic. Duplicate-version outcomes (`409`, `422`, or tool-equivalent "already exists" responses) count as success only after the workflow verifies that the already-published remote artifact set matches the local artifact set and expected digests. Buddy npm publish is tarball-only and must always pass `--ignore-scripts`; publishing from an extracted package directory is unsupported. Authentication failures, authorization failures, malformed artifacts, and upstream `5xx` or transport failures remain hard failures after a bounded in-run retry policy. Every buddy publish path in this design must retry transient network or upstream `5xx` failures at least three times with exponential backoff before surfacing failure, but must not spin indefinitely or cross the job timeout budget.

5. **`release-complete`**:
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, publish-nuget-gpr, publish-npm-gpr, publish-rubygems-gpr]`
    - `if: always()`
    - `permissions: { contents: read }`
    - Before the jq assertion runs, this job must check out the repository read-only with `persist-credentials: false`, restore the `mise` tool cache keyed by `mise.toml` plus `mise.lock`, and run `mise install` so the jq gate uses the reviewed locked toolchain rather than the runner image.
    - Performs the terminal correctness check for buddy. It must first assert that `resolve-context.result == "success"` and `static-analysis.result == "success"`. It must then parse `targets` as JSON, assert that the filtered target set is non-empty, map that set to the exact publish jobs `{nuget:gpr -> publish-nuget-gpr, npm:gpr -> publish-npm-gpr, rubygems:gpr -> publish-rubygems-gpr}`, and assert that every selected target finished with `result == "success"` and a valid `publish-result` output in `{new-publish, no-op}`. For `npm:gpr`, `no-op` is valid only when the publish helper also reports `applied-dist-tags == ["buddy"]`, because the buddy tag family for npm is intentionally single-valued in this design.
    - It must also assert that every non-selected publish job finished with `result == "skipped"`.
    - It must also assert that the single language-matching build job finished with `result == "success"`; the three non-matching build jobs must be `result == "skipped"`.
    - The normative jq skeleton is logical rather than a literal requirement to pass the entire `needs` object through one environment variable. Implementations must project `needs` down to the compact set of fields the gate actually consumes before handing JSON to `jq`, so runner environment-size limits cannot silently become a correctness bug in larger monorepos.

    ```yaml
    - name: Assert buddy release completeness
      env:
          GATE_INPUT_JSON: ${{ steps.collect-gate-input.outputs.gate-input-json }}
      run: |
          jq -n -e '
              (env.GATE_INPUT_JSON | fromjson) as $n
              | {
                  publishJobs: {
                      "nuget:gpr": "publish-nuget-gpr",
                      "npm:gpr": "publish-npm-gpr",
                      "rubygems:gpr": "publish-rubygems-gpr"
                  },
                  buildJobs: {
                      "csharp": "build-csharp",
                      "python": "build-python",
                      "jsts": "build-jsts",
                      "ruby": "build-ruby"
                  }
              } as $map
              | ($n["resolve-context"].result == "success")
              and ($n["static-analysis"].result == "success")
              and (
                  ($n["resolve-context"].outputs.targets) as $targets_json
                  | ($targets_json | type) == "string"
                  and ($targets_json != "")
                  and (
                      ($targets_json | fromjson) as $targets
                      | ($targets | type) == "array"
                      and ($targets | length) > 0
                      and ($targets | all(. as $target | $map.publishJobs[$target] != null))
                      and ($targets | all(. as $target | $n[$map.publishJobs[$target]].result == "success"))
                      and ($targets | all(. as $target | ($n[$map.publishJobs[$target]].outputs["publish-result"] == "new-publish" or $n[$map.publishJobs[$target]].outputs["publish-result"] == "no-op")))
                      and (if (($targets | index("npm:gpr")) != null)
                          then $n["publish-npm-gpr"].outputs["applied-dist-tags"] == "[\"buddy\"]"
                          else true
                          end)
                      and (([$map.publishJobs[]] - ($targets | map($map.publishJobs[.])))
                          | all(. as $job | $n[$job].result == "skipped"))
                  )
              )
              and (
                  ($n["resolve-context"].outputs.language) as $lang
                  | ($map.buildJobs[$lang] != null)
                  and ($n[$map.buildJobs[$lang]].result == "success")
                  and (([$map.buildJobs[]] - [$map.buildJobs[$lang]])
                      | all(. as $job | $n[$job].result == "skipped"))
              )'
    ```
    - Any mismatch between the selected target set and actual publish-job outcomes is a hard failure. This closes the silent-green path where a publish job is skipped because of wiring drift rather than because the target was absent.

**Language expansion checklist:** Adding a new supported language is an atomic control-plane change. The same reviewed PR must update at minimum: (1) the reusable build workflow inventory in Section 1 and the new reusable build workflow reference itself, (2) `eng/scripts/find_project_path.py` and any other resolver logic that discovers the workflow language from repository contents, (3) the language-specific version validator script plus the validator-selection logic in both `buddy.yml` and `official.yml` `resolve-context`, including any explicit release-channel flags such as Python's `--channel buddy` / `--channel official`, (4) the `detect-changes` filters, the new `test-<language>` job, and the `ci-passed` contract in `ci.yml`, (5) every buddy publish-job `if:` guard and `needs:` list, (6) every official publish-job block and its `needs:` wiring, (7) the official `require-provenance` and `create-release-tag` gates, including the language-to-expected-signer mapping used by `require-provenance`, (8) the `buildJobs` / `publishJobs` maps and `needs:` lists in both `release-complete` jq skeletons, (9) `confirm-publish-state.needs:` and its live-confirmation mapping for every new build or publish job introduced by that language, (10) the language-aware target-validation and channel-filtering rules in both `resolve-context` jobs, (11) the Section 5 language-to-target matrix and any Section 7 canonical target ordering or ledger closed sets affected by new official targets, (12) the Section 6 reusable-workflow I/O contract tables and provenance-evidence contract, (13) the publish-trust-inventory mappings and CI comparison scope for any new official targets, (14) `.github/CODEOWNERS` coverage for every newly introduced trusted workflow, helper path, and control-plane file, and (15) the workflow-boundary policy plus reusable-workflow caller assertions for every new publish path. Partial updates are unsupported.

## 4. `official.yml` — Production Release

**Important:** `buddy.yml` and `official.yml` are **independent release channels**, not a sequential promotion pipeline. Buddy publishes only to unofficial package registries. Official publishes to production registries and optional GitHub Releases via `github:official`; the GitHub Release is stable or prerelease according to the resolved version. A buddy run is NOT a prerequisite for an official run.

**Trigger:** `on: workflow_dispatch` only (no `push: tags:` trigger — `workflow_dispatch` is sufficient and avoids the bootstrapping-window risk where a tag trigger is live before the tag protection ruleset is verified).

Although the `workflow_dispatch` REST API can accept either a branch or a tag ref, this design supports only branch refs. Any dispatch that names a tag or any other non-branch ref is malformed and must fail before checkout.

```yaml
on:
    workflow_dispatch:
        inputs:
            project-name:
                description: 'Project identity to release'
                required: true
                type: string
```

All workflow inputs must be mapped to intermediate environment variables before use in shell scripts (same pattern as `buddy.yml`).

**Caller ref policy:** In `workflow_dispatch`, the branch selected in the GitHub UI, or the branch ref supplied through the REST API, determines which revision of `official.yml`, its reusable workflows, its trusted helper code, and its release payload source executes. Under this design, that caller ref must be one of the protected control-plane branches only: `main` or an eligible protected maintenance branch `release/<project-name>/v<release-line>`, where `<release-line>` is the numeric series such as `1.2.x`. Tag refs are unsupported even if the API accepts them generically.

**Authorized dispatcher policy:** Because GitHub does not provide a documented native actor allowlist for `workflow_dispatch`, `official.yml` must also enforce a reviewed repository-side dispatcher allowlist before any privileged environment secret is consumed. The workflow's first job must resolve one immutable `main` control-plane snapshot with only `contents: read`, read `.github/official-dispatch-authorizers.json` from that frozen snapshot, and hard-fail unless the human account that started the current attempt is listed there. On reruns, the workflow must also fail if GitHub surfaces a distinct rerun initiator and that rerun initiator is not listed. Membership in a broad repository role such as `write` or `admin` is not sufficient by itself to authorize official dispatch.

**Release identity mechanism:** `official.yml` does not accept a pre-existing release tag as input. Instead, it resolves the project version from the dispatch-selected protected source ref, derives the official release tag `release/<project-name>/v<version>` from that result, and creates that protected release tag inside the workflow only after approval has been granted in the project's dedicated protected tag-write environment `production-tag-write-<project-name>`. This keeps official and buddy symmetric as `workflow_dispatch` entry points while preserving a dedicated immutable official release-identity namespace.

**Branch and version mechanism:** Official release eligibility is decided from the dispatch-selected protected source ref plus the authoritative caller-ref registry and publish-trust inventory frozen from `main` in `preflight-check`. The workflow resolves `project-name`, `language`, `project-path`, and NBGV version from that ref, validates the version semantically, derives the release line from the resolved version, and then enforces only two branch-shape rules. If the caller ref is `refs/heads/main`, it must be present in the frozen `.github/official-caller-refs.json` entry set, the matching project inventory entry must still include `refs/heads/main`, and that inventory entry's `currentMainReleaseLine` must exactly match the derived release line. If the caller ref is a maintenance branch, it must be exactly `refs/heads/release/<project-name>/v<release-line>` and that exact ref must be present in both the frozen caller-ref registry and the derived project-scoped `allowedCallerRefs` entry. The workflow does not compare the selected release line against `main`'s moving version state, and maintenance-branch eligibility is therefore an explicit control-plane decision rather than a side effect of when `main` happened to move to a different line. Only after those checks succeed may the workflow derive and create the protected official release tag `release/<project-name>/v<version>`.

Official releases may publish valid prerelease versions from the protected control-plane branch set. Prerelease status does not relax branch eligibility, protection requirements, or release-tag derivation; it only changes npm dist-tag selection under the explicit community-convention rules defined below.

**Maintenance branch policy:** A maintenance branch exists only for release lines that release engineering explicitly supports. It is created by release engineering from the first official release on that line, or immediately before the first hotfix on that line, using the exact name `release/<project-name>/v<release-line>`. Before that branch is used for any official release, it must receive the same protection profile as `main`: required PR review, required `ci-passed`, no direct pushes, and no force-pushes. If a dispatch-selected version resolves to any release line whose exact maintenance branch does not exist and whose caller ref is not `refs/heads/main`, `official.yml` must fail with a clear error that prints the exact expected branch name and instructs the operator to either create and protect that maintenance branch or dispatch from the correct protected branch for that release line. Retired release lines are no longer eligible for official publication. This applies to `refs/heads/main` as well: each project's checked-in publish trust inventory must declare one exact normalized `currentMainReleaseLine`, and official runs from `main` must hard-fail unless the derived release line matches that field.

**Release-line derivation:** This design uses one release-line rule across all supported ecosystems. The input to this rule must already be a canonical normalized version string; validators are part of the contract rather than an implementation detail. First, keep only the leading numeric release segment of the normalized version string and discard everything from the first prerelease, postrelease, devrelease, local, or repository-specific suffix onward. Concretely: for SemVer-style versions, discard everything from the first `-`; official release versions with build metadata (`+...`) are unsupported and must have been rejected earlier by validation; for PEP 440-style versions, keep only the leading release segment before any `a`, `b`, `rc`, `.post`, or `.dev` suffix, and reject both epoch markers (`!`) and local version identifiers (`+...`) before this rule is applied on the official release path; for the repository's Ruby subset, keep only the leading `MAJOR.MINOR.PATCH` numeric segment before any dotted suffix containing letters. Then read at most the first two numeric components, zero-pad a missing minor component to `0`, and render the release line as `<major>.<minor>.x` without a leading `v`. Any third and later numeric components are ignored for release-line selection. Branch names, tags, and other identifiers that require a `v` prefix add that literal `v` separately. Examples: `1 -> 1.0.x`, `1.1 -> 1.1.x`, `1.2.3` (SemVer) `-> 1.2.x`, `1.2.3rc1` (PEP 440) `-> 1.2.x`, `1.2.0-dev.1` (SemVer) `-> 1.2.x`, `1.2.post1` (PEP 440) `-> 1.2.x`.

**Maintenance branch onboarding order:** Because implementation has not started yet, the onboarding procedure is defined strictly rather than retrofitted for backward compatibility. Branch eligibility is enforced on the GitHub side through the protected project-scoped environments and the repository-scoped `control-plane-monitoring` environment, and on the repository side through the control-plane files frozen from `refs/heads/main`. Registry-side trusted-publisher configuration is tracked separately in the inventory but does not receive per-branch updates because this design does not rely on provider-side exact branch or tag binding. GitHub-side environment policy and `main`'s checked-in caller-ref state cannot be updated atomically, so onboarding is a bounded control-plane transition rather than an instantaneous convergence event. Because `preflight-check` also requires `control-plane-monitoring` to match `main` exactly, that transition is a repository-wide official-release hold rather than a project-local one: no `official.yml` run from any branch is supported until convergence completes. The safe order is therefore: (1) create the maintenance branch from the exact commit that should continue that line and apply the full branch protection profile plus required code-owner review; (2) update the deployment branch policy of `control-plane-monitoring`, every required target-specific publish environment for the project, `production-tag-write-<project-name>`, and `production-evidence-write-<project-name>`, and apply any registry-side auth configuration change that is required because of repository identity, workflow selector path, auth mechanism, environment-name changes, or documented audience contract changes; (3) land the reviewed `main` change that updates `.github/official-caller-refs.json`, the matching project entry in `.github/publish-trust-inventory.json`, and any other trust-bearing file affected by the new supported line; and (4) wait for the drift monitor to observe the converged state before lifting the repository-wide official-release hold. During this bounded transition, the drift monitor must open or update a `high-nonpage` issue immediately and must escalate to `page` only if the mismatch persists for more than 4 hours or overlaps any queued, requested, waiting, or in-progress `official.yml` run. Official dispatch from any branch is unsupported until the GitHub-side branch protections, the protected environments, and `main`'s checked-in caller-ref plus trust inventory all agree.

**Maintenance branch retirement order:** Retirement is the inverse control-plane change and must also be strict. GitHub-side environment policy and `main`'s checked-in caller-ref state cannot be updated atomically, so retirement is also a bounded control-plane transition. Because `preflight-check` also requires `control-plane-monitoring` to match `main` exactly, that transition is a repository-wide official-release hold rather than a project-local one: no `official.yml` run from any branch is supported until convergence completes. The safe order is therefore: (1) run the reviewed retirement-drain helper under `eng/scripts/` to enumerate all relevant `official.yml` runs for that exact branch, determine whether any of those runs has already published one or more official destinations, and settle any already-partially-published identity before continuing; (2) wait until no queued, waiting-for-approval, requested, or in-progress `official.yml` run remains for that exact branch; (3) land the reviewed `main` change that removes that caller ref from `.github/official-caller-refs.json` and removes that same ref from the matching project's `allowedCallerRefs` set in `.github/publish-trust-inventory.json`, while preserving the project's remaining trust entry for any still-supported refs and targets; (4) remove that exact branch name from `control-plane-monitoring`, every affected target-specific publish environment, `production-tag-write-<project-name>`, and `production-evidence-write-<project-name>` so no new official run can be admitted from that line; and (5) optionally delete the Git branch after both GitHub-side and checked-in state converge. During this bounded transition, the drift monitor must open or update a `high-nonpage` issue immediately and must escalate to `page` only if the mismatch persists for more than 4 hours or overlaps any queued, requested, waiting, or in-progress `official.yml` run. If the operator cannot complete those steps in one session, the only supported choices are to finish the retirement immediately or restore the pre-retirement state; the design does not keep a durable planned-window exception on `main`.

**Emergency-cleanup path:** This path is a named runbook, not an implicit operator judgment call. It is not a general-purpose GitHub Actions workflow exception in this design. The only supported automation entrypoint is the reviewed operator-run helper `eng/scripts/official_emergency_cleanup.py`, executed manually from a trusted workstation on `refs/heads/main` by a human who currently holds the dedicated release-engineering emergency-cleanup authority and the required dual-control approval for the incident or recovery ticket. The steps are strict: (1) keep the affected release line, or the whole repository when the repair is repository-scoped, outside any unsafe environment state by removing the relevant branch from `control-plane-monitoring` and every affected publish, tag-write, and evidence-write environment if that has not already happened; (2) use `eng/scripts/official_emergency_cleanup.py` to capture live tag, GitHub Release, registry, and durable-evidence state and then either open or update the tracked incident with a candidate ledger payload or open a reviewed PR carrying that ledger update before making destructive changes; only during a P0 or P1 break-glass incident may the helper land the minimal direct ledger entry permitted by the later ledger-governance rules; (3) cancel or drain in-scope official runs; (4) repair the protected tag namespace or the `release-evidence` branch if needed; (5) settle any partially published identity to a terminal ledger state; and (6) either restore the release line to a converged supported state or complete the retirement. The design does not require a standing two-person break-glass staffing model before cleanup can begin, so a single operator may perform evidence capture, issue filing, run draining, and other non-bypass preparatory steps when necessary. However, any destructive branch, tag, or protected evidence-branch mutation that uses bypass authority must still satisfy the later contemporaneous dual-control rule before execution. If staffing constraints delayed preparatory cleanup before that second approver became available, the operator must record the rationale in the ledger and land a reviewed cleanup PR by 17:00 UTC on the next Monday-Friday business day.

**Prerequisites (must be configured before first run):**

- **Repository rulesets only:** This design uses GitHub repository rulesets, not legacy branch protection, for protected branches and protected tags. Rulesets configuration must be in place before the first workflow run; no backward-compatibility path for classic branch-protection endpoints is supported.
- **Branch rulesets** on the default branch, and on every maintenance release branch used for official hotfixes, must require at least two human PR approvals, required code-owner review, and the `ci-passed` required status check before merging, and must disallow direct pushes and force-pushes. Their bypass actors must be limited to the dedicated release-engineering emergency-cleanup group only; broad repository roles such as `admin` or `write`, and the GitHub Actions app, are not allowed as bypass actors. The emergency-cleanup group is break-glass only: it must be JIT-managed, limited to at most three named humans, and must remain operationally separate from normal production review where staffing permits. If staffing does not permit fully disjoint reviewer pools, every actual bypass action must still require contemporaneous dual control, and the approving second human must be distinct from the operator and from any human who approved the affected production environment gate or control-plane PR for that release identity. Those membership-shape constraints are governance requirements rather than something `preflight-check` can prove mechanically with its read-only metadata App. Every actual use of branch or tag bypass authority must require contemporaneous dual control: one operator performs the action, and a second authorized human separately approves the incident or recovery ticket before the action is executed. Without this, direct pushes bypass `ci.yml` entirely, allowing unreviewed code to be released. Any PR that changes `.github/official-caller-refs.json`, `.github/publish-trust-inventory.json`, checked-in publish execution paths, or any other trust-bearing control-plane file must therefore receive at least two human approvals, one of which must satisfy the CODEOWNERS requirement.
    Release engineering must perform a reviewed attestation at least every 30 days and after any membership change that the emergency-cleanup group still meets those size and separation-of-duty constraints. That attestation must be recorded in `.github/release-recovery-ledger.jsonl` as an `audit` record with `scope = "emergency-cleanup-governance"` and an `evidenceUrl` pointing to the reviewed issue, PR, or audit-log permalink that documents the attestation. For this requirement, a recorded membership change means a GitHub audit-log event that adds or removes a member from the dedicated emergency-cleanup group. The repository's alerting-only control-plane audit automation must use that audit-log event stream as the authoritative trigger and must alert when the newest such attestation is older than 30 days or missing after a recorded membership change.
- **Official release tag rulesets** must restrict both tag creation and tag updates on `refs/tags/release/**`. Because GitHub repository rulesets cannot scope bypass by workflow file path, the configuration must use a dedicated release-tag writer GitHub App as the only automation bypass actor for normal workflow execution and a dedicated release-engineering emergency-cleanup group as the only human bypass actor for manual recovery. The GitHub Actions app that backs `GITHUB_TOKEN` must **not** be a bypass actor on `refs/tags/release/**`. Ruleset bypass is not create-only: those bypass actors can create, update, and delete protected release tags, so the revocation runbook and audit monitoring must explicitly cover unexpected tag deletions as well as writes.
- **Release-evidence branch rulesets** must protect `refs/heads/release-evidence` against direct pushes, force-push, and branch deletion. The only automation bypass actor for that branch is the dedicated release-evidence writer GitHub App used by `require-provenance`; the only human bypass actor is the dedicated release-engineering emergency-cleanup group.
- **Release-evidence anchor tags:** The repository must maintain one protected project-scoped anchor tag per releasable project at `refs/tags/control-plane/release-evidence-head/<project-name>` as the authoritative last-known-good anchor for that project's latest verified position on `refs/heads/release-evidence`. Only the release-evidence writer GitHub App and the dedicated release-engineering emergency-cleanup group may update or delete those tags. `require-provenance` must advance only the selected project's anchor tag, and only after the evidence commit has been pushed and read-back verification has succeeded.
- **Release-evidence branch bootstrap:** Before the first production run for a project, release engineering must ensure that `refs/heads/release-evidence` already exists with an initial reviewed commit and must create `refs/tags/control-plane/release-evidence-head/<project-name>` pointing to a reviewed reachable commit on that branch. Ruleset configuration alone does not create the branch or the project-scoped anchor tag, and `require-provenance` must hard-fail if either is missing for the selected project.
- **Project-scoped publish environments:** For every releasable project, the target-specific publish environments that its language can use must exist in GitHub repository settings before the workflow is ever triggered: `production-nuget-<project-name>`, `production-npm-<project-name>`, `production-pypi-<project-name>`, `production-rubygems-<project-name>`, and `production-github-<project-name>` as applicable. Each such environment must include required reviewers, `prevent_self_review = true`, `Allow administrators to bypass configured protection rules = false`, no configured wait timer, and `CONTROL_PLANE_ENVIRONMENT_ROLE` set to the exact value `publish-nuget`, `publish-npm`, `publish-pypi`, `publish-rubygems`, or `publish-github` as appropriate. Because GitHub Environments require only one approval from the configured reviewer set, the design's at-least-two-reviewer rule is reviewer-pool redundancy rather than a claim of dual approval. If one of these environments does not pre-exist, GitHub auto-creates it with **zero** protection rules and the human approval gate silently does not exist; the in-job `CONTROL_PLANE_ENVIRONMENT_ROLE` check is therefore mandatory.
- **Project-scoped tag-write environments:** For every releasable project, `environment: production-tag-write-<project-name>` must also exist before the workflow is ever triggered. It must use the same required-reviewer protection profile, the same disabled-admin-bypass setting, no configured wait timer, the same deployment branch policy as the project's target-specific publish environments, and `CONTROL_PLANE_ENVIRONMENT_ROLE = tag-write`, and it is reserved exclusively for `create-release-tag` and the release-tag writer App private key.
- **Project-scoped evidence-write environments:** For every releasable project, `environment: production-evidence-write-<project-name>` must also exist before the workflow is ever triggered. It must use the same required-reviewer protection profile, the same disabled-admin-bypass setting, no configured wait timer, the same deployment branch policy as the project's target-specific publish environments, and `CONTROL_PLANE_ENVIRONMENT_ROLE = evidence-write`, and it is reserved exclusively for `require-provenance` and the release-evidence writer App private key.
- **Repository-scoped control-plane monitoring environment:** `environment: control-plane-monitoring` must exist before the first official run. It is reserved exclusively for `preflight-check` and the privileged monitoring workflows that need the environment-reader or ruleset-auditor GitHub App credentials plus outbound monitor secrets. It must have no required reviewers, no configured wait timer, administrator bypass disabled, deployment branches limited to the protected control-plane branch set from `main`'s authoritative caller-ref registry, and `CONTROL_PLANE_ENVIRONMENT_ROLE = monitoring`. Any onboarding, retirement, rollback, or emergency-cleanup step that changes that branch set must update `control-plane-monitoring` in the same GitHub-side change batch as the affected publish/tag/evidence environments. If this environment is missing, deleted, or recreated incorrectly, official dispatch and privileged monitoring are unsupported until release engineering recreates it with the exact documented settings, restores its secrets, and records the restoration as a `control-plane-monitoring` audit.
- **Project-scoped deployment branches:** Every target-specific publish environment, plus `production-tag-write-<project-name>` and `production-evidence-write-<project-name>`, must allow only the protected control-plane branch set for that project: `main` and eligible protected maintenance branches `release/<project-name>/v<release-line>`. Wildcard entries such as `release/**` are not allowed. No environment in this design may configure a deployment tag policy; only exact branch names are allowed. No other branch may enter that project's protected official-release flow.
- **Workflow file ownership:** `.github/CODEOWNERS`, `.github/workflows/**`, `.github/actions/**`, `.github/official-caller-refs.json`, `.github/official-dispatch-authorizers.json`, `.github/publish-trust-inventory.json`, `.github/provenance-signer-map.json`, `.github/release-recovery-ledger.jsonl`, `eng/scripts/**`, `**/release.json`, `**/version.json`, `hk.pkl`, `PklProject`, `PklProject.deps.json`, `mise.toml`, `mise.lock`, `global.json`, `biome.jsonc`, `pnpm-lock.yaml`, `uv.lock`, `Gemfile.lock`, `Directory.Packages.props`, and every other trusted control-plane helper code or shared dependency-control file consumed by official build or release jobs must be protected by `CODEOWNERS` review from a dedicated release-engineering group on every branch in the protected control-plane branch set. Every such file must also be represented in the `detect-changes` `infra` inventory in `ci.yml`; there is no separate implicit trust-bearing file class outside that reviewed inventory. Protected control-plane branches must also require code-owner review in their rulesets configuration.
- **Workflow boundary policy:** Local reusable publish workflows are not authorization boundaries by themselves. Repository policy must therefore hard-fail if any workflow other than `.github/workflows/buddy.yml` calls a local reusable publish workflow under `.github/workflows/_publish-*.yml`. Only `.github/workflows/official.yml` may reference a target-specific publish environment, request `id-token: write` for external publication, or reference the release-tag writer or release-evidence writer App credential. No GitHub Actions workflow other than `.github/workflows/official.yml` may request `contents: write` or mutate GitHub Release objects; emergency cleanup uses only the reviewed operator-run helper `eng/scripts/official_emergency_cleanup.py` from Section 7 rather than a separate privileged workflow. Control-plane monitoring workflows may reference only `control-plane-monitoring`, may request only the minimum read or issue-writing scopes needed for alerting, may call the GitHub Releases API only for read-only confirmation and audit queries, and must never mutate packages, releases, tags, or branches. Every workflow that references `control-plane-monitoring` must self-validate its exact `github.workflow_ref` before reading any environment secret or minting any GitHub App token. `official.yml` must publish GitHub Releases directly and must not call a local reusable publish workflow for `github:official`. `.github/workflows/buddy.yml` may call only `_publish-nuget.yml`, `_publish-npm.yml`, and `_publish-rubygems.yml` with the documented GitHub Packages endpoints and `packages: write`; any other same-repository publish caller is a hard failure. For private repositories, GitHub's reusable-workflow access policy must be configured so no external repository can call these local publish workflows. Regardless of repository visibility, each local reusable publish workflow must still self-check runtime repository identity and an explicit `caller-workflow-path` input supplied by the caller before any checkout, credential mint, or registry mutation. Because GitHub does not expose a documented trusted runtime caller-workflow-path context inside reusable workflows, that input check is a reviewed wiring guard rather than an independent authorization boundary; repository policy plus GitHub's reusable-workflow access policy remain the actual boundary.
- **GitHub App credentials and monitor secrets:** Before first use, release engineering must provision four GitHub Apps and store their private keys in the narrowest possible GitHub secret scopes: an environment-reader App for `preflight-check` and control-plane monitors, a separate ruleset-auditor App for authoritative Repository Rulesets reads including `bypass_actors`, a release-tag writer App whose private key is stored only in the corresponding `production-tag-write-<project-name>` environment for `create-release-tag`, and a release-evidence writer App used only by `require-provenance` to push to `refs/heads/release-evidence`. The environment-reader and ruleset-auditor App private keys, plus any external dead-man's-switch credentials, must be stored only in `control-plane-monitoring`; target-specific publish environments must never carry either audit credential or either write credential. These Apps must request only the repository permissions required for their single purpose. The required scopes are strict: the environment-reader App must have `actions: read` plus the minimum documented read scope GitHub requires for environment, deployment-policy, and repository Actions settings reads; the ruleset-auditor App must have the minimum GitHub App permission set that returns `bypass_actors` in the Repository Rulesets API, and because GitHub's public docs currently require write access to the ruleset for that field, this design isolates that read-equivalent but permission-elevated credential behind the separate `control-plane-monitoring` environment rather than exposing it to every workflow job. Any workflow that mints the ruleset-auditor token must do so only after all trusted and data-only checkouts are complete, must execute no further branch-controlled helper code while that token is live, and must revoke it immediately after the required ruleset reads finish. The release-tag writer App must have `contents: write` and no other repository scopes; the release-evidence writer App must have `contents: write` and no other repository scopes. Organization administrators remain the trusted root for these GitHub-hosted secrets; this design does not attempt to defend against org-admin compromise. All private keys and monitor secrets must rotate at least every 90 days and immediately on suspected compromise, and every credential rotation or trusted-publisher selector change must produce a reviewed ledger update that includes `credentialId`, the affected `projectName` and `releaseLine` when project-scoped, the operator rationale, and the verification evidence URL. Any GitHub App installation token minted at runtime must be masked immediately after issuance, before any other use, and should be explicitly revoked at job end on a best-effort basis in addition to relying on its native expiry.
- **Repository administration monitoring:** Changes to production environments, rulesets, bypass actors, GitHub App installations, or other release-control-plane administration state must emit near-real-time alerts. Audit logs remain required, but they are not the only detection mechanism in this design.
- **Alert routing contract:** Every control-plane monitor in this design must declare one of three reviewed routes: `page`, `high-nonpage`, or `tracked-follow-up`. `page` alerts route to the release-engineering primary on-call destination plus a durable team-visible channel, require acknowledgment within 15 minutes, re-page every 30 minutes, and escalate to engineering management at 60 minutes. `high-nonpage` alerts require acknowledgment within 4 hours and must open or update a tracked issue in the same operation. `tracked-follow-up` alerts must open or update a tracked issue by the next business day. DMS-provider alerts themselves use the same route as the monitor they protect.
- **Live control-plane drift detection:** In addition to near-real-time administration alerts, the repository must run a dedicated scheduled control-plane drift monitor at least every 30 minutes. This monitor is a named control-plane monitor with its own dual external dead-man's-switch heartbeats and must query `control-plane-monitoring`, every live target-specific publish environment, `production-tag-write-<project-name>`, and `production-evidence-write-<project-name>` deployment branch policy, verify that none of those environments has a configured wait timer, verify that no environment in scope contains any deployment tag-policy entry, compare the exact live branch set and publish-environment mapping to `main`'s checked-in `.github/official-caller-refs.json` plus `.github/publish-trust-inventory.json`, verify that `refs/heads/release-evidence` exists and is protected, verify the expected branch and tag ruleset profile including every project-scoped evidence-anchor tag protection, verify the authoritative `bypass_actors` set through the ruleset-auditor App, verify that the required GitHub App installations are present, and verify that the repository Actions settings which control fork-PR approval and pull-request write-token policy remain in the required secure state. For `control-plane-monitoring`, presence and bypass-disabled status are insufficient: its exact deployment branch set must also match `main`'s authoritative caller-ref registry. Any mismatch between live GitHub state and `main`'s checked-in caller-ref registry or publish trust inventory is a control-plane incident even if no release is currently running. The drift monitor must route new mismatches as `high-nonpage` immediately, open or update a tracked issue in the same operation, and escalate the same mismatch to `page` only if it persists for more than 4 hours or overlaps any queued, requested, waiting, or in-progress `official.yml` run for the affected project.
- **Official registry auth policy:** `npmjs`, `PyPI`, `RubyGems.org`, and `NuGet.org` all use documented GitHub Actions trusted publishing in this design. The authoritative branch restriction for every official target remains the GitHub deployment-branch policy of the corresponding protected environments. Exact provider-side binding to branch, tag, or commit SHA is not assumed for any registry. Repository-side trust inventory is necessary but not sufficient for registry auth readiness, but this design intentionally does **not** add a standalone pre-approval canary workflow for production credentials because the reviewed provider contracts do not guarantee a portable non-mutating proof of readiness. Instead, the design relies on reviewed inventory, direct entry-workflow execution, periodic control-plane drift auditing, and target-local just-in-time mutation checks inside `official.yml`.
- **Trusted-publisher selector matrix:** GitHub's documented OIDC claims include `workflow_ref` and, for reusable workflows, `job_workflow_ref`; GitHub does not document a stable cross-provider `caller_workflow_ref` abstraction. `npmjs` publicly documents trusted publishing with repository owner, repository name, workflow filename, optional environment, and fixed OIDC audience `npm:registry.npmjs.org`. PyPI publicly documents trusted publishing with repository identity and workflow filename, but its publish client performs runtime audience discovery for the selected index host and the upstream `pypa/gh-action-pypi-publish` action does not support trusted publishing when wrapped by a composite action or reusable workflow, so this design invokes that action directly from `official.yml`. `RubyGems.org` documents reusable-workflow support, but does not publicly document a required audience value, and this design still standardizes official trusted publishing on the entry workflow so every external registry binding points to the same reviewed workflow path `.github/workflows/official.yml`. `NuGet.org` publicly documents GitHub Actions trusted publishing through `NuGet/login@v1` or a reviewed successor that exchanges the OIDC token for a short-lived API key. Exact branch, tag, or commit-SHA binding is not assumed documented in any provider UI and therefore is not part of this design.
- **Trusted-publisher isolation note:** No registry in scope publicly documents stronger isolation between multiple jobs that run inside the same entry workflow and the same protected environment. This design therefore does **not** share one protected environment across official trusted-publisher jobs. Each trusted-publisher-backed target gets its own target-specific environment so the GitHub environment claim, approval queue, and repository-side trust inventory stay target-local.
- **Trusted-publisher and secret change management:** Because external registry auth is coupled to repository identity, trusted-publisher workflow selector path, target-specific production environment name, target auth mechanism, and, where applicable, a fixed documented audience literal or documented runtime discovery behavior in this design, any repository move/rename that changes identity, any change in target auth mechanism, any rename of a target-specific production environment, any change in a fixed documented registry audience, any change in an audience-discovery endpoint, or any move/rename of a trusted-publisher-backed workflow selector path must be accompanied by the corresponding external configuration update before the next release. Changes to the allowed protected control-plane branch set are GitHub-side operations only in this design: they must update the deployment branch policy on the relevant protected environments, the authoritative checked-in caller-ref registry on `main` at `.github/official-caller-refs.json`, and the matching project entry in `main`'s checked-in publish trust inventory, but they do not require registry-side branch-specific trust edits. The checked-in publish trust inventory must be updated in the same reviewed PR for every official target whose trusted selector path, environment, auth mechanism, fixed documented audience literal, or audience-discovery endpoint changed, including `github:official` where no trusted-publisher audience field exists. CI enforces repository-side drift by running the explicit `trusted-release-inventory` job in `ci.yml`, which must compare the post-change trust-bearing state rather than merely checking whether both the inventory file and another control-plane file were edited. The comparison scope is exactly `entryWorkflowPath`, the project-scoped `allowedCallerRefs` derived from `.github/official-caller-refs.json` as `refs/heads/main` plus only those maintenance refs that match the current project name, and the per-project `language`, `targets[*].publishExecutionPath`, `targets[*].environment`, `targets[*].authMechanism`, optional `targets[*].trustedPublisherSelector`, optional `targets[*].documentedOidcAudience`, and optional `targets[*].oidcAudienceEndpoint` fields. Order-only differences in `allowedCallerRefs` are not meaningful, but every added, removed, renamed, or remapped caller ref, publish workflow path, environment name, auth mechanism, trusted-publisher selector, fixed audience literal, or audience-discovery endpoint is a hard mismatch. CI must fail any control-plane change for which those post-change values do not exactly match the checked-in inventory, whether or not `.github/publish-trust-inventory.json` itself changed.

**Authoritative official caller-ref registry:** The checked-in authoritative repository-side source of active official caller refs is `.github/official-caller-refs.json` on `refs/heads/main`. This file is not a convenience cache; it is a required control-plane contract that records the normalized fully qualified refs that may dispatch `official.yml`. Official runs from maintenance branches must freeze and consult `main`'s copy rather than any branch-local copy. It uses `schemaVersion: 1` and the exact schema:

```json
{
    "schemaVersion": 1,
    "refs": ["refs/heads/main", "refs/heads/release/example-project/v1.2.x"]
}
```

No top-level keys other than `schemaVersion` and `refs` are allowed. `refs` must be a non-empty array of unique fully qualified Git refs, must contain `refs/heads/main` exactly once, and every non-`main` entry must be an exact fully qualified maintenance branch ref of the form `refs/heads/release/<project-name>/v<major>.<minor>.x`. Tag refs and every other branch shape are invalid in this file. GitHub-side deployment branch policy, the checked-in publish trust inventory, and runtime official caller-ref validation all derive from `main`'s copy of this file rather than from ad hoc branch enumeration or branch-local mirrors.

**Official dispatcher allowlist schema:** The checked-in dispatcher allowlist is also authoritative only on `refs/heads/main` and defines who may start or rerun `official.yml`:

```json
{
    "schemaVersion": 1,
    "users": ["release-engineer-a", "release-engineer-b"]
}
```

No top-level keys other than `schemaVersion` and `users` are allowed. `users` must be a non-empty array of unique GitHub login strings, each matching exactly the repository login spelling GitHub reports at runtime. Team slugs, role names, and wildcard patterns are invalid in this file. `official.yml` must treat this file as the only reviewed actor allowlist for manual official dispatch and rerun admission.

There is no checked-in planned-change-window file in this design. Onboarding, retirement, and emergency cleanup are coordinated operational runbooks that must either reach a converged end state or restore the previous converged state inside one operator session, without leaving durable exception state on `main`. Drift detection still treats any live mismatch between GitHub-side protection and `main`'s checked-in control-plane state as an incident, but onboarding and retirement are handled as bounded high-nonpage control-plane transitions that page only when the mismatch persists beyond the documented budget or overlaps an active official run.

**Publish trust inventory schema:** The checked-in inventory is part of the trusted control plane and is authoritative only on `refs/heads/main`. It uses `schemaVersion: 2` and records the entry workflow plus a per-project trust model. Buddy targets are intentionally excluded because they publish with `GITHUB_TOKEN` to GitHub Packages and do not have external registry-side trust state to drift:

```json
{
    "schemaVersion": 2,
    "entryWorkflowPath": ".github/workflows/official.yml",
    "projects": {
        "example-project": {
            "language": "csharp",
            "currentMainReleaseLine": "1.3.x",
            "allowedCallerRefs": ["refs/heads/main", "refs/heads/release/example-project/v1.2.x"],
            "targets": {
                "nuget:official": {
                    "publishExecutionPath": ".github/workflows/official.yml",
                    "environment": "production-nuget-example-project",
                    "authMechanism": "trusted-publisher",
                    "documentedOidcAudience": "https://www.nuget.org",
                    "trustedPublisherSelector": {
                        "providerContract": "entry-workflow-ui",
                        "repositoryOwner": "hcoona",
                        "repositoryName": "three",
                        "workflowPath": ".github/workflows/official.yml",
                        "environment": "production-nuget-example-project"
                    }
                },
                "github:official": {
                    "publishExecutionPath": ".github/workflows/official.yml",
                    "environment": "production-github-example-project",
                    "authMechanism": "github-token"
                }
            }
        }
    }
}
```

The inventory uses fully qualified Git refs under each project's `allowedCallerRefs`, one exact normalized `currentMainReleaseLine`, repository-relative workflow paths, a target-specific environment mapping, an explicit target-to-auth mapping, a target-to-selector mapping for trusted-publisher-backed targets, and either an optional fixed audience literal or an optional audience-discovery endpoint, depending on the provider contract. A project's `allowedCallerRefs` is not an independent source of truth: it must exactly mirror the project-scoped subset derived from `.github/official-caller-refs.json` on `main`, namely `refs/heads/main` plus only those maintenance refs whose path exactly matches `refs/heads/release/<project-name>/v<major>.<minor>.x`. `currentMainReleaseLine` is the authoritative checked-in declaration of which normalized release line `refs/heads/main` is currently allowed to publish for that project, and retired lines must be removed from that field before their retirement is considered complete.

No top-level keys other than `schemaVersion`, `entryWorkflowPath`, and `projects` are allowed. The validator must enforce this as a strict top-level key whitelist equivalent to JSON Schema with `additionalProperties: false`. `projects` must be a non-empty object keyed by exact project name. Every project object must contain exactly `language`, `currentMainReleaseLine`, `allowedCallerRefs`, and `targets`. `language` must use the closed set `{csharp, python, jsts, ruby}`. `currentMainReleaseLine` must be one normalized `<major>.<minor>.x` string. `allowedCallerRefs` must be a non-empty array of unique fully qualified refs. `targets` must be a non-empty object whose keys are drawn only from the official target set legal for that project's `language`. Every target object must contain exactly `publishExecutionPath`, `environment`, and `authMechanism`, plus `trustedPublisherSelector` when `authMechanism = trusted-publisher`, `documentedOidcAudience` only when the provider publicly documents a fixed audience literal, and `oidcAudienceEndpoint` only when the provider requires runtime audience discovery. Every selector object must contain exactly `providerContract`, `repositoryOwner`, `repositoryName`, `workflowPath`, and `environment`. `providerContract` must use the closed set `{entry-workflow-ui}` in this design. As of v2.8 the fixed-audience set is `{nuget:official, npm:official}`; the audience-discovery set is `{pypi:official}`; `RubyGems.org` public docs still do not publish a required fixed audience value for this design.

The checked-in inventory is an in-repository drift detector and audit trail, not an independent cryptographic proof of registry-side trust state. An actor who can merge arbitrary changes into `main` can change both the workflow code and the inventory together. Its purpose is to make trust changes reviewable and to catch accidental repository-side drift before production approval is consumed.

**Jobs:**

Every official job that reads `control-plane-monitoring`, enters a protected production environment, mints a repository-scoped GitHub App write token, writes durable evidence, mutates a protected release tag, or mutates GitHub Release state must run on a GitHub-hosted runner. Self-hosted runners are unsupported for `preflight-check`, `require-provenance`, `create-release-tag`, and all official publish jobs.

1. **`authorize-dispatcher`**:
    - Runs before `preflight-check`.
    - `permissions: { contents: read }`
    - This job is intentionally unconditional and must not acquire an `if:` guard.
    - It must validate `project-name` with the same syntax rule later used by `resolve-context`, resolve and emit a single immutable `main-control-plane-sha` by reading `refs/heads/main` exactly once, perform a read-only checkout of that exact `main-control-plane-sha` with `persist-credentials: false`, load `.github/official-dispatch-authorizers.json` from that frozen checkout, validate its strict schema, and hard-fail unless `github.actor` is listed.
    - If GitHub surfaces a distinct rerun initiator for the current attempt, that rerun initiator must also be listed. An authorized original dispatcher does not authorize a different human to rerun the same official flow later.
    - This job must not reference any environment and must not mint any GitHub App token. Its purpose is to ensure that `preflight-check` cannot consume `control-plane-monitoring` secrets for an unapproved dispatcher.

2. **`preflight-check`**:
    - Runs before `resolve-context`.
    - `needs: [authorize-dispatcher]`
    - `permissions: { contents: read }`
    - This job is intentionally unconditional and must not acquire an `if:` guard.
    - This job must run inside `environment: control-plane-monitoring`. Its first in-environment step must validate `CONTROL_PLANE_ENVIRONMENT_ROLE = monitoring` and must self-check that `github.workflow_ref` identifies `.github/workflows/official.yml` at the current ref before it reads any environment secret or mints any GitHub App token. Only after that validation succeeds may it mint two dedicated GitHub App installation tokens just in time: an environment-reader token for GitHub Environments and deployment branch policy reads, and a separate ruleset-auditor token for authoritative Repository Rulesets reads including `bypass_actors`. A long-lived PAT is not the normal path. The GitHub App private keys for these audit tokens and the external monitor secrets must live only in `control-plane-monitoring`, not in organization-level or repository-level general workflow secrets. Treat the default `GITHUB_TOKEN` as insufficient for this job; weakening or skipping the verification is unsupported. Every minted installation token must be masked before first use.
    - After the runtime self-check above succeeds and before any audit token is minted, this job must reuse `needs.authorize-dispatcher.outputs.main-control-plane-sha` as the only authoritative frozen `main` snapshot and must re-emit that exact value unchanged as `preflight-check.outputs.main-control-plane-sha` for downstream jobs. Every later read of checked-in control-plane files and every helper executed while `control-plane-monitoring` credentials remain live must be pinned to that exact SHA; `preflight-check` must not re-read moving `main` state later in the run.
    - Before its first environment query, this job must perform two read-only checkouts: a frozen checkout of `main` at `needs.authorize-dispatcher.outputs.main-control-plane-sha` for trusted control-plane helpers and control-plane files, plus a separate checkout of the dispatch-selected protected source ref for data-only inspection. It must validate `project-name` with the same syntax rule as `resolve-context`, require the current caller ref to start with `refs/heads/`, resolve `project-path` and `language`, read the project's `release.json`, filter to the official target set, and derive the exact required environment names for that project: one target-specific publish environment per selected official target, plus `production-tag-write-<project-name>` and `production-evidence-write-<project-name>`. Any helper code used before the job leaves `control-plane-monitoring` must come from the frozen `main` checkout, not from the dispatch-selected source checkout, and no executable content from the dispatch-selected checkout may run while the `control-plane-monitoring` credentials are still live.
    - Verifies that every derived required environment already exists; that every target-specific publish environment plus the tag-write and evidence-write environments includes a required-reviewer protection rule with at least two reviewers total, `prevent_self_review` enabled, administrator bypass disabled, no configured wait timer, and the exact deployment branch set from `main`'s caller-ref registry for that project; and that the publish, tag-write, and evidence-write environments expose the same reviewer pool and the same branch-policy set. `control-plane-monitoring` is validated separately and must have no required-reviewer rule, no wait timer, and deployment branches exactly equal the full repository-wide protected control-plane branch set frozen from `.github/official-caller-refs.json`, not merely the selected project's subset. Any mismatch in that repository-wide set blocks every `official.yml` run and is therefore the mechanism that enforces the repository-wide hold during onboarding or retirement transitions.
    - Uses the GitHub Environments API response directly: the check must look for a `protection_rules` entry with `type == "required_reviewers"` and a reviewer list whose total length is at least `2`, must verify `prevent_self_review == true`, must verify that administrator bypass is disabled, must verify that no `wait_timer` protection rule is present, and must verify that the deployment branch policy on the publish, tag-write, and evidence-write environments contains exactly the expected branch names for `main` plus the registered protected maintenance branches for that project, with no missing entry and no extra entry. The same API pass must verify that `control-plane-monitoring` contains exactly the repository-wide protected control-plane branch set frozen from `main`, with no missing entry and no extra entry. Wildcard or pattern-based entries such as `release/**`, and every deployment tag-policy entry, are hard failures. Because the API returns short branch names rather than fully qualified refs, this job must normalize the expected caller refs by stripping the `refs/heads/` prefix before comparison. Required reviewers and exact branch policy are both mandatory; a wait timer or branch policy alone is not sufficient. This job must fail closed on any live environment branch-set mismatch before later production approvals are consumed, while `resolve-context` separately verifies that the checked-in publish trust inventory and checked-in caller-ref registry agree with each other. `refs/heads/release-evidence` is outside that environment-derived branch set by design.
    - Uses the GitHub Repository Rulesets API only. It must verify that active branch rulesets protect `main`, every non-`main` branch currently allowed by the relevant target-specific publish, tag-write, evidence-write, or `control-plane-monitoring` deployment policies for that project, and the exact branch `refs/heads/release-evidence` with the required profile for each ref. The protected release branches must carry the same required PR review, required code-owner review, required status check `ci-passed`, no direct pushes, and no force-pushes, and their bypass actors must be limited to the dedicated release-engineering emergency-cleanup group rather than broad repository roles or the GitHub Actions app. `refs/heads/release-evidence` must be protected against direct pushes, force-push, and deletion and must allow bypass only for the dedicated release-evidence writer App plus the dedicated release-engineering emergency-cleanup group. The job must also verify that an active tag ruleset protects `refs/tags/release/**` against unauthorized creation and updates, that a second active tag ruleset or prefix-targeted ruleset protects `refs/tags/control-plane/release-evidence-head/<project-name>`, and that their bypass actors are limited to the dedicated release-tag writer App for `refs/tags/release/**` plus the dedicated release-evidence writer App for the selected project's evidence-anchor tag, with the dedicated release-engineering emergency-cleanup group as the only human bypass actor for both.
    - Before any approval is consumed, this job must also verify through the refs API that `refs/heads/release-evidence` and `refs/tags/control-plane/release-evidence-head/<project-name>` already exist for the selected project. Ruleset presence alone is insufficient because rulesets do not create those refs.
    - `preflight-check` must not claim to machine-enforce the emergency-cleanup group's maximum size or reviewer-overlap policy; those are governance checks performed out of band because neither the environment-reader App nor the ruleset-auditor App enumerates team membership.
    - Treats every GitHub API error as a hard failure. Specifically: `404` from environment endpoints means one or more required target-specific publish environments or the tag/evidence-write environments are missing; a successful API response that lacks the required reviewers rule where one is required, has `prevent_self_review` disabled, has a wildcard deployment branch policy, lacks the required branch or tag rulesets, or applies a weaker ruleset profile than `main` means the protected environment set is misconfigured; every other non-`200` response blocks the workflow as an environment-verification failure.
    - Fails hard if the environment is missing or unprotected. This turns the documented prerequisite into an executable guardrail.
    - All GitHub API calls in this job must set an explicit client timeout of no more than 30 seconds per request so the guard fails fast rather than consuming the full job timeout on a hung response.
    - This check is still an audit-before-use guard, not a transactional lock. If an administrator weakens or deletes environment protection after `preflight-check` passes but before a later job reaches its environment gate, the authoritative fail-closed path is the later GitHub environment evaluation plus that job's required `CONTROL_PLANE_ENVIRONMENT_ROLE` check; a deleted environment must therefore stop the run by appearing as an uninitialized auto-created environment rather than by silently proceeding. The same residual TOCTOU window exists for tag rulesets: `preflight-check` validates the live ruleset configuration at job start, while `create-release-tag` is still subject to whatever tag ruleset is live at push time. Those residual windows are accepted and must be controlled through CODEOWNERS, repository audit logs, and change discipline around production protection settings.

3. **`resolve-context`**:
    - `needs: [preflight-check]`
    - `permissions: { contents: read }`
    - **Input validation (first step, before checkout):** Validate `project-name` with a full-string match against `[a-z0-9][a-z0-9._-]*`, require length `1..100`, reject any occurrence of `..`, reject trailing `.`, and reject any name that ends with `.lock`. Reject invalid names with a clear error. Releasable project identities in this design are ASCII-lowercase only.
    - **Runner and tooling:** Runs on `ubuntu-latest`. Like `resolve-context` in `buddy.yml`, version resolution uses the repository-local `nbgv-python` adapter from the checked-out source ref and does not require a Windows runner even for C# projects. The job must hard-fail if `mise.lock` is absent, and should restore a tool cache keyed by `mise.toml` and `mise.lock` before invoking `mise install`. If the lockfile needs regeneration, that is an out-of-band repository change performed with `mise lock`, not a workflow fallback. Every tool used in an official build or publish path must use a digest-pinning backend; version-string-only backends are unsupported in this design. If `nbgv-python` cannot resolve the version deterministically, the job must hard-fail; there is no fallback or manual override path in this design.
    - **Source checkout:** Check out the dispatch-selected protected source ref for this workflow run with `fetch-depth: 0` and `persist-credentials: false`. In `official.yml`, that source workspace is both the trusted control-plane checkout and the release payload input.
    - Runs `eng/scripts/find_project_path.py` to resolve `language` and `project-path` from `project-name`. `project-name` is ASCII-lowercase and must resolve to exactly one releasable project in the repository whose leaf directory name uses that same canonical lowercase spelling. The resolution step must emit exactly one of `{csharp, python, jsts, ruby}` for `language`; no match, ambiguous match, unsupported language, or resolver error is a hard failure.
    - **NBGV resolution and semantic validation:** Resolve the version with `nbgv-python`, hard-fail if that resolution is non-deterministic, and use the resolved value as the workflow output `version`. Here, "non-deterministic" has the same meaning as in `buddy.yml`: no unique governing `version.json`, no unique normalized version string from the selected full-history checkout, or validator rejection of the resolved string. Inheriting the nearest ancestor `version.json` when the project root does not have its own local file is intentional, but the governing file must still resolve uniquely. Then validate that resolved version using `eng/scripts/validate_semver2_version.py` (NuGet and npm), `eng/scripts/validate_rubygems_version.py` (the repository's supported RubyGems-compatible subset), or `eng/scripts/validate_pep440_version.py --channel official` (Python), chosen after the project language is known. The Python validator must receive that explicit channel flag; default-mode inference is unsupported. `eng/scripts/validate_pep440_version.py --channel official` must reject non-canonical normalized PEP 440 strings, all epoch markers (`!`), all local version identifiers (`+...`), and all `.devN` development-release forms on the official release path. `.postN` post-releases remain valid on the official release path and derive the same `<release-line>` as their base release. `eng/scripts/validate_semver2_version.py` must reject official release versions that contain SemVer build metadata (`+...`).
    - **Official branch-line validation:** Derive `<release-line>` using the release-line derivation rule above. This job must reuse `needs.preflight-check.outputs.main-control-plane-sha` as its only authoritative `main` snapshot and must not recapture or recompute against moving `origin/main` state. It must require the current caller ref to start with `refs/heads/`, then read `.github/official-caller-refs.json` from that frozen snapshot and require the current caller ref to be listed there. If the current caller ref is `refs/heads/main`, the branch-shape check succeeds only provisionally at this stage: `refs/heads/main` must be present in the frozen caller-ref registry, and the later publish trust inventory preflight must additionally prove that the matching project inventory entry exists, still includes `refs/heads/main`, and declares `currentMainReleaseLine` exactly equal to the derived `<release-line>`. If the current caller ref is not `refs/heads/main`, it must be exactly `refs/heads/release/<project-name>/v<release-line>`, and that exact ref must be present in both the frozen caller-ref registry and the derived project-scoped `allowedCallerRefs` entry. The workflow does not infer branch eligibility by comparing against `main`'s current release line; it uses the checked-in caller-ref registry plus the checked-in per-project `currentMainReleaseLine` field.
    - Reads `release.json` from the selected source workspace, validates it exactly as specified in **Section 5**, applies the same language-target validation rule as `buddy.yml`, then filters to the official target set `{nuget:official, npm:official, pypi:official, rubygems:official, github:official}` and fails if the filtered set is empty.
    - **Official npm dist-tag derivation:** If `npm:official` is present in the filtered target set, derive `npm-dist-tags` deterministically as a compact ordered JSON array from the validated caller ref, release line, and whether the resolved version is stable or prerelease. Stable releases from `refs/heads/main` use `["latest"]`. Stable releases from a maintenance branch `refs/heads/release/<project-name>/v<release-line>` use `["release-v<major>.<minor>"]` and must never append `latest`. Prerelease versions remain eligible for official publication, but they must never claim `latest`: lowercase the first prerelease identifier and use that entire identifier as the prerelease channel token. That token must match `^[a-uw-z][a-z0-9]*$`; numeric-leading identifiers, numeric-only identifiers, identifiers containing separators such as `-`, `.`, or `_`, and identifiers beginning with `v` are unsupported for `npm:official` and must hard-fail in `resolve-context`. `resolve-context` must enforce the `!startsWith('v')` rule explicitly even if a future regex refactor changes the character class. The derived prerelease channel token must then be validated structurally and contextually before any maintenance-line prefix is added: it must not equal `latest`, must not equal `buddy`, must not equal `release`, and must not begin with `release-v`. Prerelease releases from `main` therefore use tags such as `rc`, `beta`, `alpha`, `preview`, or `next`; prerelease releases from maintenance branches use `["release-v<major>.<minor>-<channel>"]`. `resolve-context` must perform this structural validation before emitting `npm-dist-tags`, and the direct `publish-npm-official` job in `official.yml` must validate the emitted tag array against runtime `github.ref` and `release-line` before any registry mutation.
    - **Publish trust inventory preflight:** After the official target set is resolved, read `.github/official-caller-refs.json` and `.github/publish-trust-inventory.json` from the same frozen `main` control-plane snapshot, validate both schemas, verify that `entryWorkflowPath` is exactly `.github/workflows/official.yml`, verify that the current caller ref is present in `.github/official-caller-refs.json`, look up the exact `projects[<project-name>]` entry, verify that its `language` equals the resolved project language, verify that its `currentMainReleaseLine` is present and exactly equals the normalized derived `<release-line>` whenever the current caller ref is `refs/heads/main`, derive the expected project-scoped `allowedCallerRefs` as `refs/heads/main` plus only those maintenance refs in `.github/official-caller-refs.json` that exactly match `refs/heads/release/<project-name>/v<major>.<minor>.x`, verify that the inventory entry matches that derived subset exactly, verify that its target keys exactly match the filtered official targets declared by the validated `release.json`, and verify that every filtered official target maps to an exact per-target object that defines the expected publish execution path, target-specific environment name, auth mode, optional trusted-publisher selector contract, and, when applicable, an optional fixed OIDC audience literal or optional audience-discovery endpoint. Official auth modes are `trusted-publisher` for `nuget:official`, `npm:official`, `pypi:official`, and `rubygems:official`, and `github-token` for `github:official`. When `npm:official` is selected, `resolve-context` must also copy that target's inventory `documentedOidcAudience` value into `npm-oidc-audience` and currently require it to equal `npm:registry.npmjs.org`; `publish-npm-official` must reject any runtime mismatch against that emitted value before registry mutation. For `nuget:official`, any documented audience value remains audit and drift state only because the runtime OIDC exchange is delegated to `NuGet/login@v1` or its reviewed successor rather than to a caller-supplied audience input. This catches repository-side trust drift before any production approval is consumed. Because registry-side trust settings are not queried portably, matching external configuration updates are still a mandatory operational step.
    - **Official release tag derivation and overwrite guard:** Derive `tag-name = release/<project-name>/v<version>`. This guard must query the remote protected tag namespace via `git ls-remote --tags` or the GitHub refs API rather than relying on a local tag list from checkout. When `git ls-remote --tags` is used, annotated tags must be compared by their peeled `refs/tags/<tag>^{}` commit SHA rather than by the raw tag-object SHA at `refs/tags/<tag>`. If that protected official release tag already exists and points to a different commit, fail immediately. If it already exists and points to the current commit, treat the tag reservation as an idempotent no-op. If `github:official` is among the resolved targets, check GitHub Releases state for that derived tag. If no GitHub Release exists for that derived tag, proceed — this is the normal first official run. Official GitHub Releases must use a deterministic release title `<project-name> v<version>`. The guard must scan GitHub Releases to completion with `per_page=100`, following pagination across non-pre-release releases including drafts, and must hard-fail on API, authentication, authorization, rate-limit, transport, or response-shape errors. An interrupted, truncated, or otherwise incomplete scan is `unknown`, not `not found`. Match that deterministic stable title across the completed stable-release set and fail immediately if the same title already exists under a different tag or commit. A draft or published stable release with that deterministic title is part of the same stable identity space; the design does not treat drafts as a separate namespace. If a pre-release GitHub Release exists for the same derived tag, the later direct `publish-github-official` job may promote it to a stable release only after remote asset identity checks compare every manifest-selected `github-release-asset`, including the required `SHA256SUMS` entry when GitHub Release assets are present, and confirm that every already-present remote asset matches the current local build output; a divergent same-tag pre-release is a hard failure. If a non-pre-release GitHub Release already exists for the same derived tag, defer the idempotent/no-op decision to the later direct `publish-github-official` job, which must verify remote asset identity before reporting success. If `github:official` is not in the resolved target set but stable GitHub Releases already exist for `release/<project-name>/v*`, the workflow may emit a non-blocking warning to the step summary reminding operators that those Releases are now manual state.
    - **Outputs:** `tag-name`, `language`, `project-name`, `project-path`, `version`, `release-line`, `targets` (compact JSON array of filtered official targets in canonical official-target order `nuget:official`, `npm:official`, `pypi:official`, `rubygems:official`, `github:official`), `npm-dist-tags` (compact JSON array when `npm:official` is selected), and `npm-oidc-audience` (the frozen inventory `documentedOidcAudience` value for `npm:official`, currently `npm:registry.npmjs.org`).

3. **`static-analysis`**:
    - `needs: [resolve-context]`
    - `permissions: { contents: read }`
    - Checks out the source ref for this workflow run before enumerating files. Read-only checkout must use `persist-credentials: false`.
    - Restores the `mise` tool cache keyed by `mise.toml` and `mise.lock`, runs `mise install`, then runs `hk check <project-path>` scoped to the resolved project path. HK receives the project path directly and discovers applicable files under that path according to `hk.pkl`; this design does not pre-enumerate file names in shell.

4. **`clean-build`** (`build-csharp` / `build-python` / `build-jsts` / `build-ruby`):
    - For supply chain security, no prior artifacts are reused. A fresh build and test run is performed from the exact dispatch-selected commit for this workflow run. The checkout must use `fetch-depth: 0` for NBGV resolution.
    - Uses the same four static conditional build jobs pattern as `buddy.yml`. In `build-scope: release` mode, the single language-matching build job must request only `permissions: { contents: read }`, use `secrets: {}`, and receive the required `with:` inputs wired from `build-scope: release`, `needs.resolve-context.outputs.project-path`, `needs.resolve-context.outputs.project-name`, `checkout-ref: ${{ github.sha }}`, and `require-provenance: true`. Each build job depends on both `resolve-context` and `static-analysis`. Only the language-matching build job executes; the others are skipped. The matching release-mode build job must produce the exact manifest-selected publication files plus deterministic build-side verification inputs, but it must not mint OIDC tokens, generate attestation bundles, or execute any privileged publish logic.

5. **`attest-build-output`** (`attest-csharp` / `attest-python` / `attest-jsts` / `attest-ruby`):
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby]`
    - Uses the same four static conditional job pattern as the build stage. Only the language-matching attestation job executes; the others are skipped.
    - Each attestation job must request `permissions: { contents: read, id-token: write, attestations: write }`, use `secrets: {}`, perform a read-only checkout of the trusted control-plane code for `github.sha`, download the single language-matching build artifact, validate `artifact-manifest.json` and `build-verification-input.json`, and generate the attestation bundle set for the exact manifest-selected publication files.
    - The attestation job must not perform dependency installation, package restore, test execution, or any shell evaluation of branch-controlled package metadata beyond manifest validation. Its only mutable external effect is generating GitHub artifact attestations for the already-built files.
    - The attestation job must upload a second deterministic artifact named `provenance-output-<project-name>` that contains exactly `attestation-manifest.json` and the `attestations/` directory. Same-run overwrites use `overwrite: true`; cross-run reuse is unsupported.

6. **`require-provenance`**:
    - `needs: [preflight-check, resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, attest-csharp, attest-python, attest-jsts, attest-ruby]`
    - `permissions: { contents: read }`
    - `environment: production-evidence-write-<project-name>` — mandatory. Durable evidence persistence must occur only inside the dedicated evidence-write environment.
    - `if: always() && !cancelled() && !failure() && needs.resolve-context.result == 'success' && needs.static-analysis.result == 'success' && ((needs.resolve-context.outputs.language == 'csharp' && needs.build-csharp.result == 'success' && needs.attest-csharp.result == 'success' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped' && needs.attest-python.result == 'skipped' && needs.attest-jsts.result == 'skipped' && needs.attest-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'python' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'success' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped' && needs.attest-csharp.result == 'skipped' && needs.attest-python.result == 'success' && needs.attest-jsts.result == 'skipped' && needs.attest-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'jsts' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'success' && needs.build-ruby.result == 'skipped' && needs.attest-csharp.result == 'skipped' && needs.attest-python.result == 'skipped' && needs.attest-jsts.result == 'success' && needs.attest-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'ruby' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'success' && needs.attest-csharp.result == 'skipped' && needs.attest-python.result == 'skipped' && needs.attest-jsts.result == 'skipped' && needs.attest-ruby.result == 'success'))`
    - This job must not mint the environment-reader or ruleset-auditor GitHub App tokens and must not perform live control-plane API rechecks. The authoritative live gate for this job is the GitHub environment evaluation plus its own fail-closed environment-role check. Its first in-environment step must validate `CONTROL_PLANE_ENVIRONMENT_ROLE = evidence-write` before it mints the release-evidence writer App token or mutates durable evidence. A missing or wrong value is a hard failure and specifically covers the deleted-and-auto-recreated-environment path.
    - This job does not use a repository-wide concurrency lock. Instead, after verification succeeds it must push to `refs/heads/release-evidence` with a bounded fetch/rebase/retry loop so concurrent official releases for different projects can append disjoint immutable paths without serializing the entire repository. It downloads the single language-matching build artifact plus `provenance-output-<project-name>`, validates `build-verification-input.json`, recomputes the SHA-256 of the downloaded `artifact-manifest.json`, validates the pre-generated `attestation-manifest.json`, verifies the deterministic `attestations/` layout, runs attestation verification against the attestation-job-generated bundles, and verifies binding to the exact attestation-job identity, repository identity, source commit, project identity, version, and build artifact manifest for this release attempt. The validation is exact: every manifest entry selected for publication must have exactly one attestation-manifest entry and exactly one matching bundle file, and no extra attestation-manifest entry or extra bundle file is allowed.
    - Mints a dedicated release-evidence writer GitHub App installation token and uses that token, not the job `GITHUB_TOKEN`, for the durable evidence push. The minted token must be masked immediately after issuance, before any other use, and should be explicitly revoked at job end on a best-effort basis.
    - `require-provenance` is the sole producer of the final durable `artifact-evidence.json`. The build and attestation jobs must not pre-populate any `verified*` field. After validation, this job must write an immutable evidence directory to the protected durable evidence branch at `.github/release-evidence/<project-name>/<version>/<source-commit>/runs/<github.run_id>-attempt-<github.run_attempt>/` containing exactly `artifact-evidence.json`, the copied `artifact-manifest.json`, the copied `build-verification-input.json`, the copied `attestation-manifest.json`, and the verified `attestations/` bundle files generated by the attestation job. Same-path overwrites are unsupported. After the evidence commit is pushed successfully, this job must also advance `refs/tags/control-plane/release-evidence-head/<project-name>` to the new `release-evidence` tip using compare-and-swap semantics that verify the expected previous anchor target before update, retry a bounded number of times on contention, and fail closed if the anchor cannot be advanced safely. The read-back verification must use the Git blobs API's content-addressed blob endpoints by blob SHA, not GitHub web URLs of the form `/blob/<blob-sha>/...`, and must verify byte equality for `artifact-evidence.json`, `artifact-manifest.json`, `build-verification-input.json`, and `attestation-manifest.json` before succeeding. This write path is mandatory, not best-effort. `artifactEvidenceUrl` for any later recovery ledger entry must point to the immutable Git blobs API URL of the committed `artifact-evidence.json`. A missing durable evidence branch, missing project-scoped anchor tag, evidence-writer credential failure, write failure, anchor-update failure, attempted overwrite, or byte-mismatch on the read-back verification is a hard failure.
    - Emits `artifact-evidence-url`, `artifact-manifest-sha256`, and `release-evidence-head-sha` as workflow outputs so later recovery and audit steps can reference the durable evidence record, exact verified manifest digest, and reconciled evidence-branch head directly.
    - This job is the machine-enforced production gate for Section 8. Until provenance support exists for every enabled official publish path, `require-provenance` must fail closed and keep `create-release-tag` plus all official publish jobs ineligible.

7. **`create-release-tag`**:
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance]`
    - `if: always() && !cancelled() && !failure() && needs.resolve-context.result == 'success' && needs.static-analysis.result == 'success' && needs.require-provenance.result == 'success'`
    - `permissions: { contents: read }`
    - `environment: production-tag-write-<project-name>` — mandatory. Tag reservation must occur only inside the dedicated tag-write environment; package-registry and GitHub Release publish jobs must never enter that environment.
    - The job name and approval context shown to reviewers must include the project name, resolved version, caller ref, `github.run_id`, and at least a 12-character source-commit prefix so concurrent approval requests for the same project are distinguishable.
    - Mints a dedicated release-tag writer GitHub App installation token from a secret in `production-tag-write-<project-name>` and uses that token for the tag push. The job `GITHUB_TOKEN` is never the bypass actor for `refs/tags/release/**`. The minted token must be masked immediately after issuance, before any other use, and should be revoked explicitly at job end on a best-effort basis.
    - This job must not mint the environment-reader or ruleset-auditor GitHub App tokens and must not perform live control-plane API rechecks. The authoritative live gate for this job is the GitHub environment evaluation plus its own fail-closed environment-role check. Its first in-environment step must validate `CONTROL_PLANE_ENVIRONMENT_ROLE = tag-write` before it mints the release-tag writer App token or mutates any tag. A missing or wrong value is a hard failure and specifically covers the deleted-and-auto-recreated-environment path.
    - Creates the protected official release-identity tag `release/<project-name>/v<version>` at the current workflow commit after approval and before any official publish job becomes eligible. Reserving the official identity before per-destination publish is still intentional in this design; recovery rules for abandoned or partially used reservations are defined in Section 7.
    - **Tag creation logic:** If the tag does not exist, create it. If it already exists and points to the same commit, succeed as an idempotent no-op. If it exists but points to a different commit, fail immediately. There is no force path for official release tags. The existence check must query the remote protected tag namespace via `git ls-remote --tags` or the GitHub refs API; when `git ls-remote --tags` is used, annotated tags must be compared by their peeled `refs/tags/<tag>^{}` commit SHA rather than by the raw tag-object SHA. A local `git tag -l` view from checkout is insufficient and must not be the sole source of truth. If the create attempt returns a duplicate or conflict response such as HTTP `422`, the job must re-query the remote tag state immediately and treat the result as `no-op` only when the just-observed remote tag now points to the same source commit; otherwise it is a hard failure.
    - Checks out the current source ref read-only, then configures git explicitly to push with the minted GitHub App installation token. The job must not persist the default `GITHUB_TOKEN` as a write-capable remote credential.
    - Emits a machine-readable workflow output `tag-result` whose value is exactly `created` or `no-op`, uploads an immutable same-run artifact `tag-reservation-result-<project-name>` containing a compact JSON record with `projectName`, `version`, `tagName`, `sourceCommit`, and `tagResult`, and appends a one-line summary to `$GITHUB_STEP_SUMMARY` describing the reserved release identity, caller ref, source commit, and tag outcome.

8. **Publish jobs** (static conditional, one job per official ecosystem-destination pair):
    - Uses the same per-destination split structure as `buddy.yml`, but official targets now include `github:official` in addition to the production package registries. Unlike buddy, official publish jobs do not need to restate the full language-matching build predicate in each `if:` guard because `require-provenance` already enforces the exact single-build-success pattern for the resolved language, and `create-release-tag` plus all official publish jobs are blocked on `require-provenance` success.
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag]`
    - `environment:` is **mandatory**, not optional, and must be target-specific: `publish-nuget-official` uses `production-nuget-<project-name>`, `publish-npm-official` uses `production-npm-<project-name>`, `publish-pypi-official` uses `production-pypi-<project-name>`, `publish-rubygems-official` uses `production-rubygems-<project-name>`, and `publish-github-official` uses `production-github-<project-name>`. This enables human approval gates and target-local OIDC environment claims.
    - Every environment-gated publish job name and first summary line must include the target name, project name, resolved version, caller ref, `github.run_id`, and at least a 12-character source-commit prefix so concurrent approval requests for the same project are distinguishable in the approval UI and in notifications.
    - `publish-nuget-official`, `publish-npm-official`, `publish-pypi-official`, and `publish-rubygems-official` use `permissions: { contents: read, id-token: write }` for trusted publishing. All five official publish jobs run directly inside `official.yml`; `publish-github-official` uses `permissions: { contents: write }`, which already satisfies read access. Reviewed local composite actions may still perform shared artifact validation, helper logic, or the final target mutation command, provided they are invoked as steps inside those direct `official.yml` jobs after the job-level environment and permission gates are already in force. Reusable-workflow indirection for official publication remains forbidden. In particular, `publish-pypi-official` must invoke `pypa/gh-action-pypi-publish` directly from `official.yml`; a local composite action may prepare artifact layout or remote prechecks, but must not wrap the upstream trusted-publishing step.
    - Every selected official publish path must expose a terminal-gate-visible `publish-result` output. Direct jobs must map their step or composite-action outputs to job outputs explicitly, and `publish-npm-official` must also map `applied-dist-tags`. `release-complete` consumes those job outputs; step summaries alone are insufficient.
    - Because `official.yml` may run only from the protected control-plane branch set, no separate runtime assertion is required here to distinguish the caller branch from the trusted control-plane source. The target-specific production environment branch policy and branch protections carry that responsibility.
    - Direct official publish jobs do not declare a job-level `secrets:` map. They rely on explicit job `permissions`, OIDC, and target-specific protected environments; blanket secret inheritance remains prohibited.
    - Official publish jobs must not mint the environment-reader or ruleset-auditor GitHub App tokens and must not perform live control-plane API rechecks. The authoritative live gate for these jobs is the GitHub environment evaluation plus their own fail-closed environment-role check. Each selected publish job's first in-environment step must validate the exact expected `CONTROL_PLANE_ENVIRONMENT_ROLE` value before it requests OIDC or mutates any remote target. A missing or wrong value is a hard failure and specifically covers the deleted-and-auto-recreated-environment path.
    - Before any registry mutation, each official publish job must recompute the SHA-256 of the downloaded `artifact-manifest.json` and verify that it exactly matches both the local `build-verification-input.json` value and `require-provenance.outputs.artifact-manifest-sha256`. Self-consistency of the downloaded artifact alone is insufficient.
    - Each publish step uses idempotent publish logic from the protected control-plane branch set. Duplicate-version outcomes (`409`, `422`, or tool-equivalent "already exists" responses) count as success only after the workflow verifies the target-specific remote identity contract. For `nuget:official`, raw downloaded `.nupkg` bytes are explicitly **not** a valid idempotency proof because NuGet.org repository-signs packages after upload; idempotency must instead be based on exact package ID/version presence, optional `.snupkg` presence, and the same reserved release identity. If the `.nupkg` is already present but the expected `.snupkg` is still missing remotely, the rerun performs the missing symbol upload and must emit `publish-result = new-publish`; `publish-result = no-op` is allowed only when both the primary package and the expected symbol-package state are already fully reconciled. For `pypi:official`, a state where only the wheel or only the sdist exists is an explicit `partial` incident state, not a success and not a generic `not found`. For npm, RubyGems, and GitHub Releases, the workflow verifies the already-published remote artifact set against the local artifact set and the target-specific identity rules below. Authentication failures, authorization failures, malformed artifacts, and upstream `5xx` or transport failures remain hard failures after a bounded in-run retry policy. Every publish path in this design must retry transient network or upstream `5xx` failures at least three times with exponential backoff before surfacing failure, but must not spin indefinitely or cross the job timeout budget.

    ```yaml
    publish-nuget-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag]
        outputs:
            publish-result: ${{ steps.publish.outputs.publish-result }}
        permissions:
            contents: read
            id-token: write
        environment:
            name: production-nuget-${{ needs.resolve-context.outputs.project-name }}
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.require-provenance.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'nuget:official')
        steps:
            - name: Verify environment role
              shell: bash
              env:
                  CONTROL_PLANE_ENVIRONMENT_ROLE: ${{ vars.CONTROL_PLANE_ENVIRONMENT_ROLE }}
              run: |
                  [[ "$CONTROL_PLANE_ENVIRONMENT_ROLE" == "publish-nuget" ]] || { echo "expected CONTROL_PLANE_ENVIRONMENT_ROLE=publish-nuget"; exit 1; }
            - name: Checkout trusted control-plane code
              uses: actions/checkout@<sha>
              with:
                  ref: ${{ github.sha }}
                  fetch-depth: 1
                  persist-credentials: false
            - id: publish
              name: Publish to NuGet.org via trusted publishing
              uses: ./.github/actions/publish-nuget-official
              with:
                  artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
                  artifact-manifest-sha256: ${{ needs.require-provenance.outputs.artifact-manifest-sha256 }}
                  version: ${{ needs.resolve-context.outputs.version }}

    publish-npm-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag]
        outputs:
            publish-result: ${{ steps.publish.outputs.publish-result }}
            applied-dist-tags: ${{ steps.publish.outputs.applied-dist-tags }}
        permissions:
            contents: read
            id-token: write
        environment:
            name: production-npm-${{ needs.resolve-context.outputs.project-name }}
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.require-provenance.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'npm:official')
        steps:
            - name: Verify environment role
              shell: bash
              env:
                  CONTROL_PLANE_ENVIRONMENT_ROLE: ${{ vars.CONTROL_PLANE_ENVIRONMENT_ROLE }}
              run: |
                  [[ "$CONTROL_PLANE_ENVIRONMENT_ROLE" == "publish-npm" ]] || { echo "expected CONTROL_PLANE_ENVIRONMENT_ROLE=publish-npm"; exit 1; }
            - name: Checkout trusted control-plane code
              uses: actions/checkout@<sha>
              with:
                  ref: ${{ github.sha }}
                  fetch-depth: 1
                  persist-credentials: false
            - id: publish
              name: Publish to npmjs via trusted publishing
              uses: ./.github/actions/publish-npm-official
              with:
                  artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
                  artifact-manifest-sha256: ${{ needs.require-provenance.outputs.artifact-manifest-sha256 }}
                  version: ${{ needs.resolve-context.outputs.version }}
                  release-line: ${{ needs.resolve-context.outputs.release-line }}
                  dist-tags: ${{ needs.resolve-context.outputs.npm-dist-tags }}
                  oidc-audience: ${{ needs.resolve-context.outputs.npm-oidc-audience }}

    publish-pypi-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag]
        outputs:
            publish-result: ${{ steps.finalize.outputs.publish-result }}
        permissions:
            contents: read
            id-token: write
        environment:
            name: production-pypi-${{ needs.resolve-context.outputs.project-name }}
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.require-provenance.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'pypi:official')
        steps:
            - name: Verify environment role
              shell: bash
              env:
                  CONTROL_PLANE_ENVIRONMENT_ROLE: ${{ vars.CONTROL_PLANE_ENVIRONMENT_ROLE }}
              run: |
                  [[ "$CONTROL_PLANE_ENVIRONMENT_ROLE" == "publish-pypi" ]] || { echo "expected CONTROL_PLANE_ENVIRONMENT_ROLE=publish-pypi"; exit 1; }
            - name: Checkout trusted control-plane code
              uses: actions/checkout@<sha>
              with:
                  ref: ${{ github.sha }}
                  fetch-depth: 1
                  persist-credentials: false
            - id: prepare
              name: Validate PyPI artifact layout and remote preconditions
              uses: ./.github/actions/prepare-pypi-official
              with:
                  artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
                  artifact-manifest-sha256: ${{ needs.require-provenance.outputs.artifact-manifest-sha256 }}
                  version: ${{ needs.resolve-context.outputs.version }}
            - id: publish
              if: steps.prepare.outputs.publish-result == 'new-publish'
              name: Publish to PyPI via trusted publishing
              uses: pypa/gh-action-pypi-publish@<sha>
              with:
                  packages-dir: ${{ steps.prepare.outputs.packages-dir }}
            - id: finalize
              name: Finalize PyPI publish result
              uses: ./.github/actions/finalize-pypi-official
              with:
                  prepared-result: ${{ steps.prepare.outputs.publish-result }}
                  publish-step-outcome: ${{ steps.publish.outcome || 'skipped' }}

    publish-rubygems-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag]
        outputs:
            publish-result: ${{ steps.publish.outputs.publish-result }}
        permissions:
            contents: read
            id-token: write
        environment:
            name: production-rubygems-${{ needs.resolve-context.outputs.project-name }}
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.require-provenance.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'rubygems:official')
        steps:
            - name: Verify environment role
              shell: bash
              env:
                  CONTROL_PLANE_ENVIRONMENT_ROLE: ${{ vars.CONTROL_PLANE_ENVIRONMENT_ROLE }}
              run: |
                  [[ "$CONTROL_PLANE_ENVIRONMENT_ROLE" == "publish-rubygems" ]] || { echo "expected CONTROL_PLANE_ENVIRONMENT_ROLE=publish-rubygems"; exit 1; }
            - name: Checkout trusted control-plane code
              uses: actions/checkout@<sha>
              with:
                  ref: ${{ github.sha }}
                  fetch-depth: 1
                  persist-credentials: false
            - id: publish
              name: Publish to RubyGems.org via trusted publishing
              uses: ./.github/actions/publish-rubygems-official
              with:
                  artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
                  artifact-manifest-sha256: ${{ needs.require-provenance.outputs.artifact-manifest-sha256 }}
                  version: ${{ needs.resolve-context.outputs.version }}

    publish-github-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag]
        outputs:
            publish-result: ${{ steps.publish.outputs.publish-result }}
        permissions:
            contents: write
        environment:
            name: production-github-${{ needs.resolve-context.outputs.project-name }}
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.require-provenance.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'github:official')
        steps:
            - name: Verify environment role
              shell: bash
              env:
                  CONTROL_PLANE_ENVIRONMENT_ROLE: ${{ vars.CONTROL_PLANE_ENVIRONMENT_ROLE }}
              run: |
                  [[ "$CONTROL_PLANE_ENVIRONMENT_ROLE" == "publish-github" ]] || { echo "expected CONTROL_PLANE_ENVIRONMENT_ROLE=publish-github"; exit 1; }
            - name: Checkout trusted control-plane code
              uses: actions/checkout@<sha>
              with:
                  ref: ${{ github.sha }}
                  fetch-depth: 1
                  persist-credentials: false
            - id: publish
              name: Publish GitHub Release assets
              uses: ./.github/actions/publish-github-official
              with:
                  artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
                  artifact-manifest-sha256: ${{ needs.require-provenance.outputs.artifact-manifest-sha256 }}
                  project-name: ${{ needs.resolve-context.outputs.project-name }}
                  version: ${{ needs.resolve-context.outputs.version }}
                  tag-name: ${{ needs.resolve-context.outputs.tag-name }}
    ```

9. **`confirm-publish-state`**:
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag, publish-nuget-official, publish-npm-official, publish-pypi-official, publish-rubygems-official, publish-github-official]`
    - `if: always() && !cancelled() && needs.resolve-context.result == 'success' && needs.static-analysis.result == 'success' && needs.require-provenance.result == 'success' && needs.create-release-tag.result == 'success'`
    - `permissions: { contents: read }`
    - Before any confirmation logic runs, this job must check out the repository read-only with `persist-credentials: false`, restore the `mise` tool cache keyed by `mise.toml` plus `mise.lock`, and run `mise install` so any jq-based normalization uses the reviewed locked toolchain rather than the runner image.
    - Re-queries every selected official target from `fromJson(needs.resolve-context.outputs.targets)` against its live remote system and emits `confirmed-published-targets` as a compact ordered JSON array in canonical official-target order, serialized exactly as compact JSON equivalent to `jq -c`. It must also emit `confirmation-state` with the closed set `{complete, partial-timeout, partial-upstream-failure}`. These semantic states are workflow outputs rather than job conclusions: when this job completes its confirmation loop and emits outputs, it must finish with job `result == success` for all three values so downstream gates and recovery logic can read the outputs. Only a hard timeout, runner crash, or other infrastructure failure that prevents reliable output emission should make the job itself fail. Because job outputs are not reliable after a hard timeout, this job must also persist the latest checkpoint as both a machine-readable `confirm-publish-state-checkpoint.json` artifact (same-run overwrite allowed) and a one-line JSON checkpoint in `$GITHUB_STEP_SUMMARY` whenever another target becomes confirmed. `confirmed-published-targets` is authoritative only on full job success; the checkpoint artifact plus summary are recovery hints and never substitute for the required full confirmation. Result collection may happen in parallel, but final serialization must sort explicitly into canonical official-target order before emission; completion order must never leak into the output array. This job is the authoritative workflow-side source of truth for `publishedTargets`; machine-readable publish outputs and step summaries are diagnostic hints only. It must still run after a selected publish job fails so the workflow captures the best available live remote state for partial-publish recovery; in that case `confirmation-state` must settle to `partial-upstream-failure` unless every selected target was nevertheless confirmed.
    - Remote confirmation must use bounded retry with backoff to absorb registry eventual consistency. A single immediate `not found` response after a publish attempt is `unknown`, not authoritative absence. The job must confirm selected targets in parallel; a serial confirmation loop is unsupported. Each selected target must be retried at least three times with increasing delays, use an initial delay of at least 30 seconds, a backoff multiplier of at least 2x, and a per-request timeout of at most 30 seconds. The maximum confirmation budget is 10 minutes per selected target except `pypi:official`, which receives 20 minutes to accommodate slower index convergence.
    - For `nuget:official`, confirmation must query the flat-container or equivalent package-content endpoint for the exact package/version identity and, when a symbol package is expected, must also confirm `.snupkg` presence through the symbol-capable endpoint used by the publish logic. Search-index visibility is advisory only and must not be the blocking confirmation signal.
    - For `pypi:official`, confirmation must query `/pypi/<project>/<version>/json`, enumerate `urls[*].filename`, and require at least one `.whl` plus at least one `.tar.gz` file before confirming publication. Version existence alone is insufficient.
    - For `npm:official`, this job must also verify the full ordered `dist-tags` array emitted by `resolve-context`. The registry-side dist-tag payload is an object keyed by tag name, so this job must first transform that object into the deterministic ordered array requested by `resolve-context` before comparing arrays; raw object-vs-array equality is invalid.
    - For `rubygems:official`, confirmation must query the RubyGems versions API or an equivalent version-specific endpoint for the exact gem name/version identity and confirm that the released `.gem` payload metadata matches the manifest-selected artifact set; website search visibility is advisory only.
    - For `github:official`, confirmation must query the GitHub Releases API for the exact reserved tag, verify that the release title remains `<project-name> v<version>`, verify the expected prerelease/stable state, require the exact manifest-selected GitHub Release asset set including `SHA256SUMS`, reject any unexpected extra asset, and verify that every remote asset's digest matches both the local manifest-selected artifact and the durable evidence referenced by `require-provenance.outputs.artifact-evidence-url` before confirming publication.

10. **`release-complete`**:
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag, publish-nuget-official, publish-npm-official, publish-pypi-official, publish-rubygems-official, publish-github-official, confirm-publish-state]`
    - `if: always()`
    - `permissions: { contents: read }`
    - Before the jq assertion runs, this job must check out the repository read-only with `persist-credentials: false`, restore the `mise` tool cache keyed by `mise.toml` plus `mise.lock`, and run `mise install` so the jq gate uses the reviewed locked toolchain rather than the runner image.
    - Performs the terminal correctness check for official releases. It must first assert that `resolve-context.result == "success"`, `static-analysis.result == "success"`, `require-provenance.result == "success"`, `create-release-tag.result == "success"`, and `confirm-publish-state.result == "success"`. It must also assert that `create-release-tag.outputs.tag-result` is present and equal to `created` or `no-op`, that `require-provenance.outputs.artifact-evidence-url` is present and non-empty, and that `confirm-publish-state.outputs.confirmation-state == "complete"`. It must then parse `targets` as JSON, assert that the filtered target set is non-empty, map that set to the exact publish jobs `{nuget:official -> publish-nuget-official, npm:official -> publish-npm-official, pypi:official -> publish-pypi-official, rubygems:official -> publish-rubygems-official, github:official -> publish-github-official}`, and assert that every selected target finished with `result == "success"` and a valid `publish-result` output in `{new-publish, no-op}`.
    - It must also assert that every non-selected publish job finished with `result == "skipped"`.
    - It must also assert that `confirm-publish-state.outputs.confirmed-published-targets` parses as JSON and exactly equals the selected target array in canonical order.
    - If `npm:official` is selected, it must also assert that both `publish-npm-official.outputs.applied-dist-tags` and `resolve-context.outputs.npm-dist-tags` are present, parse as JSON arrays, and are exactly equal in canonical order.
    - It must also assert that the single language-matching build job finished with `result == "success"`; the three non-matching build jobs must be `result == "skipped"`.
    - The normative jq skeleton is logical rather than a literal requirement to pass the entire `needs` object through one environment variable. Implementations must project `needs` down to the compact set of fields the gate actually consumes before handing JSON to `jq`, so runner environment-size limits cannot silently become a correctness bug in larger monorepos.

    ```yaml
    - name: Assert official release completeness
      env:
          GATE_INPUT_JSON: ${{ steps.collect-gate-input.outputs.gate-input-json }}
      run: |
          jq -n -e '
              (env.GATE_INPUT_JSON | fromjson) as $n
              | {
                  publishJobs: {
                      "nuget:official": "publish-nuget-official",
                      "npm:official": "publish-npm-official",
                      "pypi:official": "publish-pypi-official",
                      "rubygems:official": "publish-rubygems-official",
                      "github:official": "publish-github-official"
                  },
                  buildJobs: {
                      "csharp": "build-csharp",
                      "python": "build-python",
                      "jsts": "build-jsts",
                      "ruby": "build-ruby"
                  }
              } as $map
              | ($n["resolve-context"].result == "success")
              and ($n["static-analysis"].result == "success")
              and ($n["require-provenance"].result == "success")
              and ($n["create-release-tag"].result == "success")
              and ($n["confirm-publish-state"].result == "success")
              and (($n["create-release-tag"].outputs["tag-result"] == "created") or ($n["create-release-tag"].outputs["tag-result"] == "no-op"))
              and (($n["require-provenance"].outputs["artifact-evidence-url"] | type) == "string")
              and ($n["require-provenance"].outputs["artifact-evidence-url"] != "")
              and ($n["confirm-publish-state"].outputs["confirmation-state"] == "complete")
              and (
                  ($n["resolve-context"].outputs.targets) as $targets_json
                  | ($targets_json | type) == "string"
                  and ($targets_json != "")
                  and (
                      ($targets_json | fromjson) as $targets
                      | ($n["confirm-publish-state"].outputs["confirmed-published-targets"] | fromjson) as $confirmed_targets
                      | ($targets | type) == "array"
                      and ($targets | length) > 0
                      and ($confirmed_targets | type) == "array"
                      and ($targets | all(. as $target | $map.publishJobs[$target] != null))
                      and ($targets | all(. as $target | $n[$map.publishJobs[$target]].result == "success"))
                      and ($targets | all(. as $target | ($n[$map.publishJobs[$target]].outputs["publish-result"] == "new-publish" or $n[$map.publishJobs[$target]].outputs["publish-result"] == "no-op")))
                      and ($confirmed_targets == $targets)
                      and (if (($targets | index("npm:official")) != null)
                          then (($n["publish-npm-official"].outputs["applied-dist-tags"] | fromjson) == ($n["resolve-context"].outputs["npm-dist-tags"] | fromjson))
                          else true
                          end)
                      and (([$map.publishJobs[]] - ($targets | map($map.publishJobs[.])))
                          | all(. as $job | $n[$job].result == "skipped"))
                  )
              )
              and (
                  ($n["resolve-context"].outputs.language) as $lang
                  | ($map.buildJobs[$lang] != null)
                  and ($n[$map.buildJobs[$lang]].result == "success")
                  and (([$map.buildJobs[]] - [$map.buildJobs[$lang]])
                      | all(. as $job | $n[$job].result == "skipped"))
              )'
    ```
    - The workflow is not complete until this job succeeds. This closes the silent-green path where a publish job is skipped because of wiring drift rather than because the target was absent.

## 5. Release Configuration Contract

Each project that can be released must have a release configuration file at `<project-root>/release.json`. The resolver jobs in `buddy.yml` and `official.yml` read this file to determine publish targets.

**Schema:**

```json
{
    "schemaVersion": 1,
    "targets": ["nuget:gpr", "nuget:official", "github:official"]
}
```

**Fields:**

| Field           | Type       | Required | Description                                                 |
| --------------- | ---------- | -------- | ----------------------------------------------------------- |
| `schemaVersion` | `number`   | Yes      | Release configuration schema version. Current value: `1`.   |
| `targets`       | `string[]` | Yes      | Array of publish targets in `ecosystem:destination` format. |

**Validation rules:**

- `release.json` must be valid JSON.
- `schemaVersion` must be present and equal to `1`.
- `targets` must be a non-empty array of unique strings.
- Every target must be one of the explicitly supported values in the table below. Unknown values are hard failures.
- Every target must also be compatible with the resolved project language according to the language-target matrix below. Cross-ecosystem target declarations are hard failures.
- No fields other than `schemaVersion` and `targets` are allowed. The validator must enforce this as a strict top-level key whitelist, equivalent to JSON Schema with `additionalProperties: false`.
- A workflow may filter out valid targets that belong to the opposite release channel, but only **after** validation succeeds.
- After channel filtering, the invoking workflow must still have at least one applicable target.
- In this design, Python currently has no unofficial target in `buddy.yml`.
- Removing a previously used target takes effect immediately because backward-compatibility shims are intentionally out of scope before implementation starts. For example, removing `github:official` stops GitHub Release reconciliation on subsequent official runs and leaves any existing GitHub Release on that identity as manual state. The reviewed PR that removes a target must include a target-specific decommission checklist: for `github:official`, enumerate every existing GitHub Release on that identity, decide archival vs deletion, update consumer documentation, and record any follow-up issue; for production package targets, state whether the existing published package remains live, is deprecated, or is withdrawn, and record that decision in the recovery ledger when applicable. Subsequent official runs should emit a non-blocking warning when such legacy state still exists.
- Unsupported future schema versions are hard failures with operator guidance. Because implementation has not started, schema upgrades are coordinated changes rather than backward-compatible migrations.
- RubyGems versions use the repository's explicit subset policy: `MAJOR.MINOR.PATCH[.suffix...]`, no leading `v`, no `-` or `+`, suffix segments limited to `[0-9A-Za-z]+`, and every suffix segment must independently contain at least one letter. Numeric-only suffix chains such as `1.2.3.1` are rejected, and mixed chains such as `1.2.3.1.rc1` are also rejected because the segment `1` is digit-only.

**Project resolution contract:**

- `project-name` must identify exactly one releasable project in the repository, must be `1..100` characters long, must match `[a-z0-9][a-z0-9._-]*`, and must reject any occurrence of `..`, any trailing `.`, and any `.lock` suffix for ref safety.
- Releasable project identities are canonical ASCII-lowercase names. Repository policy must hard-fail if any releasable project root basename is not already in that canonical lowercase form.
- Releasable `project-name` values must be unique under ASCII lowercase normalization across the repository so workflow concurrency keys cannot alias distinct projects.
- Repository policy must include a CI validation that scans all candidate project roots and hard-fails if two candidate roots collide under ASCII lowercase normalization. For this validation, a candidate project root is any directory whose basename is exactly the candidate `project-name` and whose contents resolve to exactly one workflow language in `{csharp, python, jsts, ruby}`, regardless of whether its `release.json` is missing or invalid.
- Repository policy must also include a CI validation that scans releasable project roots and hard-fails if any such root resolves to more than one workflow language, enforcing the single-language project scope before any release workflow is invoked.
- Project resolution is performed from the repository root by exact leaf-directory-name match: a candidate project root is a directory whose basename is exactly the canonical lowercase `project-name`.
- Language detection is manifest-driven, not file-extension-driven. For a candidate project root, the resolver must inspect only ecosystem-defining manifests inside that root: `*.csproj`, `*.fsproj`, or `*.vbproj` imply `csharp`; `pyproject.toml` implies `python`; `package.json` implies `jsts`; `*.gemspec` or `Gemfile` implies `ruby`. Helper scripts, test helpers, CI glue, and stray source files in other languages do not contribute to language detection.
- A candidate project root resolves successfully only when the manifest-driven scan finds at least one manifest from exactly one of those four ecosystem sets. Zero matching ecosystem sets is `no match`. More than one matching ecosystem set is `multi-language` and is a hard failure. There is no heuristic tie-breaker and no "primary language" fallback.
- Candidate discovery must not case-fold names, apply substring matching, or apply heuristic tie-breakers. If multiple candidate project roots with the same basename are discovered, the result is ambiguous and the workflow must hard-fail.
- Project resolution must emit both `project-path` and `language`.
- `language` must be exactly one of `csharp`, `python`, `jsts`, or `ruby`.
- No match, ambiguous match, unsupported language, or resolver error is a hard failure.

**Lookup behavior:** The script reads `release.json` only from the resolved project root at `<project-root>/release.json`. There is no upward search, inheritance, or fallback target set. If that exact file is absent, the workflow fails with a clear error. On failure, the script must print: the resolved project path, the contents of `release.json` if found, and the specific validation rule that was violated.

**Valid target values:**

| Target              | Channel    | Processed by   | Description                                                       |
| ------------------- | ---------- | -------------- | ----------------------------------------------------------------- |
| `nuget:gpr`         | Unofficial | `buddy.yml`    | Publish `.nupkg` to GitHub Packages NuGet feed                    |
| `nuget:official`    | Official   | `official.yml` | Publish `.nupkg` to NuGet.org                                     |
| `npm:gpr`           | Unofficial | `buddy.yml`    | Publish npm tarball to GitHub Packages npm feed                   |
| `npm:official`      | Official   | `official.yml` | Publish npm tarball to npmjs                                      |
| `pypi:official`     | Official   | `official.yml` | Publish wheel/sdist to PyPI                                       |
| `rubygems:gpr`      | Unofficial | `buddy.yml`    | Publish gem to GitHub Packages RubyGems feed                      |
| `rubygems:official` | Official   | `official.yml` | Publish gem to RubyGems.org                                       |
| `github:official`   | Official   | `official.yml` | Create or update a GitHub Release with downloadable assets, using prerelease or stable state derived from the resolved version |

**Language-target compatibility matrix:**

| Resolved `language` | Allowed targets             |
| ------------------- | --------------------------- |
| `csharp`            | `nuget:*`, `github:official`       |
| `jsts`              | `npm:*`, `github:official`         |
| `python`            | `pypi:official`, `github:official` |
| `ruby`              | `rubygems:*`, `github:official`    |

`buddy.yml` filters to unofficial targets only. `official.yml` filters to official targets only. A `release.json` may declare targets from both channels, but opposite-channel filtering happens only after strict validation; unknown targets are hard failures.

## 6. Reusable Workflow I/O Contracts

### Global Reusable Workflow Rules

All reusable workflows share these constraints:

- They must NOT declare their own `permissions:` blocks. Caller jobs own permission grants.
- They must use the same shell input-hardening rule as entry workflows: map `${{ inputs.* }}`, `${{ github.* }}`, `${{ needs.*.outputs.* }}`, `${{ env.* }}` values derived from untrusted contexts, and any other untrusted context expression to `env:` first, then reference quoted shell variables inside `run:` steps.
- Official release workflows may execute only trusted control-plane helper code sourced from the dispatch-selected protected control-plane branch. Official release tags are workflow-created outputs, not alternative sources of privileged code.
- They must treat artifact validation failures, auth failures, and upstream service failures as hard failures unless a specific duplicate-version case is explicitly documented as idempotent.

Local composite actions under `.github/actions/**` follow the same shell-hardening contract. Any composite action that accepts caller-controlled values through `with:` inputs must remap those values through `env:` before its own `run:` steps consume them, and repository policy must lint those composite actions for the same shell-sink rules enforced on workflows.

### Build-Test Workflows

All four build-test workflows share the same input/output structure:

| Input          | Type     | Required | Description                                                                                                          |
| -------------- | -------- | -------- | -------------------------------------------------------------------------------------------------------------------- |
| `build-scope`  | `string` | Yes      | Must be exactly `ci` or `release`; `ci` runs the language-wide CI suite for the current checkout, `release` builds one resolved release project |
| `checkout-ref` | `string` | No       | Git ref or commit SHA that the build workflow must check out; defaults to the caller job's `github.sha` when omitted |
| `project-path` | `string` | `release` only | Path to the project directory within the repo; must be absent when `build-scope = ci` |
| `project-name` | `string` | `release` only | Project name used for artifact naming; must be absent when `build-scope = ci` |
| `require-provenance` | `boolean` | No | When `true`, the release-mode build workflow must emit the deterministic build-side metadata consumed later by `require-provenance`, including `build-verification-input.json`; the attestation bundles and `attestation-manifest.json` are generated later by the isolated attestation job; defaults to `false` and is valid only when `build-scope = release` |

| Output          | Type     | Description                                                     |
| --------------- | -------- | --------------------------------------------------------------- |
| `artifact-name` | `string` | Name of the uploaded CI Artifact: `build-output-<project-name>` when `build-scope = release`, else empty |

**Required caller permissions:** `contents: read`

**Mode contract:** `ci.yml` is the only caller that uses `build-scope: ci`, and in that mode the reusable workflow must run the language-wide CI suite for the current checkout, must not upload release artifacts, and must reject `require-provenance: true`. `buddy.yml` and `official.yml` must use `build-scope: release` and must provide both `project-path` and `project-name`.

**Checkout behavior:** Build-test workflows perform their own checkout and must use `fetch-depth: 0` internally so NBGV and other git-history-derived metadata resolve correctly. These read-only checkouts must also use `persist-credentials: false`. When `checkout-ref` is provided, the reusable workflow must check out exactly that ref; when it is omitted, the reusable workflow must check out the caller job's `github.sha`. Buddy and official callers may pass the dispatch commit SHA explicitly for clarity, but the default behavior already targets the current workflow commit.

**Secrets:** `secrets: {}` is mandatory. Build-test workflows require no secrets, callers must not pass any non-empty `secrets:` map, and `secrets: inherit` is prohibited to avoid exposing publish credentials to build/test execution.

**Dependency lock enforcement:** Build-test workflows must treat project dependency lockfiles as mandatory control-plane inputs, not advisory metadata. The reusable workflow for the resolved ecosystem must hard-fail if the required lockfile is absent and must install in strict locked mode: C# uses restore lock enforcement such as `dotnet restore --locked-mode` (or an equivalent reviewed MSBuild property form); JavaScript/TypeScript uses `pnpm install --frozen-lockfile`; Python uses `uv sync --locked`; Ruby uses Bundler frozen mode against `Gemfile.lock`. A build that would regenerate or ignore a lockfile is invalid for this design.

**Artifact convention:** Each build workflow uploads its output to CI Artifacts with the name `build-output-<project-name>`. Publish workflows and `require-provenance` download by this exact name. Because rerun-based recovery is a first-class path in Section 7 and artifact names are deterministic within a run, every `actions/upload-artifact` invocation in build workflows must set `overwrite: true` for the main build artifact. The artifact layout per ecosystem:

| Ecosystem | Expected artifact contents                                                                                                                                                       |
| --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| NuGet     | One or more `.nupkg` files, and optionally matching `.snupkg` symbol packages, whose manifest `publishRoles` include `package`                                                 |
| npm       | One `.tgz` tarball (output of `npm pack` / `pnpm pack`) whose manifest `publishRoles` include `package`                                                                         |
| PyPI      | One `.whl` and one `.tar.gz` (wheel + sdist) whose manifest `publishRoles` include `package`                                                                                    |
| RubyGems  | One `.gem` file whose manifest `publishRoles` include `package`                                                                                                                  |
| GitHub    | Any top-level file whose manifest `publishRoles` include `github-release-asset`, plus a required top-level `SHA256SUMS` file when any non-`SHA256SUMS` file is selected for GitHub Release publication; files may also carry `package` when the same artifact should be published to both surfaces |

Every build artifact must also contain a manifest file at the artifact root named exactly `artifact-manifest.json` that lists each published file, its SHA-256 digest, and the publish roles for which that file is intended. When any non-`SHA256SUMS` manifest entry carries `github-release-asset`, the build workflow must also generate a top-level file named exactly `SHA256SUMS`, add it to the manifest as a normal published file, and treat it as part of the final GitHub Release asset set rather than as publish-time derived metadata. When `require-provenance = true`, the same artifact must also contain `build-verification-input.json`. `artifact-manifest.json` and `build-verification-input.json` are internal workflow metadata and must not be uploaded as GitHub Release assets. Publish workflows and `require-provenance` must verify the downloaded files against that manifest before any publish or provenance-verification step runs. The manifest schema is fixed and shared across ecosystems:

```json
{
    "schemaVersion": 1,
    "files": [
        {
            "path": "artifact-file-name.ext",
            "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "publishRoles": ["package", "github-release-asset"]
        }
    ]
}
```

`schemaVersion` must equal `1`. `files` must be a non-empty array. Each `path` must be a relative path to a top-level artifact file, must not contain `/` or `\`, must not equal `.` or `..`, and must not contain any ASCII control character (`U+0000` through `U+001F` or `U+007F`); each `sha256` must match exactly `[0-9a-f]{64}`, and each `publishRoles` value must be a non-empty array of unique strings from the closed set `{package, github-release-asset}`. Every publish workflow must reject nested paths, dot-segment paths, or control-character file names at manifest-validation time rather than surfacing a later file-not-found error. Package-registry publish workflows operate only on manifest entries whose `publishRoles` include `package`. The direct `publish-github-official` job operates only on manifest entries whose `publishRoles` include `github-release-asset` and must upload that manifest-selected set byte-for-byte without generating additional release assets. A file may carry both roles when the same artifact should be published both as a package and as a GitHub Release asset. If any non-`SHA256SUMS` manifest entry includes `github-release-asset`, exactly one manifest entry must use `path = "SHA256SUMS"` and `publishRoles = ["github-release-asset"]`; if no non-`SHA256SUMS` manifest entry includes `github-release-asset`, the reserved `SHA256SUMS` path must be absent. The build workflow, not `publish-github-official`, must generate `SHA256SUMS` from the sorted set of all other manifest entries whose `publishRoles` include `github-release-asset`, using GNU coreutils `sha256sum` output format `<64-hex-digest><two spaces><filename>`, bytewise ascending file-name order with `LC_ALL=C` semantics, LF-only line endings, and GNU-compatible filename escaping. `SHA256SUMS` must exclude itself from its own input set.

The validator must enforce this schema strictly. No top-level keys other than `schemaVersion` and `files` are allowed, and no file-entry keys other than `path`, `sha256`, and `publishRoles` are allowed.

When `require-provenance = true`, the build workflow must place `build-verification-input.json` in the main build artifact next to `artifact-manifest.json`. The attestation bundles are generated later by the isolated attestation job and uploaded in the separate deterministic artifact `provenance-output-<project-name>`. `artifact-evidence.json` is not part of the build-produced artifact; it is produced only later by `require-provenance` after verification succeeds. `build-verification-input.json` is internal control-plane metadata rather than a GitHub Release asset and uses a fixed exact schema:

```json
{
    "schemaVersion": 1,
    "projectName": "example-project",
    "version": "1.2.3",
    "sourceCommit": "0123456789abcdef0123456789abcdef01234567",
    "artifactName": "build-output-example-project",
    "artifactManifestSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
    "declaredBuildWorkflowRef": "hcoona/three/.github/workflows/_build-test-jsts.yml@refs/heads/main",
    "declaredBuildWorkflowSha": "89abcdef0123456789abcdef0123456789abcdef"
}
```

No top-level keys other than those shown above are allowed. `schemaVersion` must equal `1`. `artifactManifestSha256` must match exactly `[0-9a-f]{64}`. `declaredBuildWorkflowRef` and `declaredBuildWorkflowSha` are deterministic build-side declarations carried forward for operator diagnostics and same-identity rebuild comparison only; they are not verifier-owned identity claims and provenance trust must not depend on them. This file records only deterministic build-side facts and must never contain any `verified*` claim or any run-unique URL.

The isolated attestation job must generate `attestation-manifest.json` in `provenance-output-<project-name>` using this exact schema:

```json
{
    "schemaVersion": 1,
    "attestations": [
        {
            "path": "artifact-file-name.ext",
            "bundlePath": "attestations/artifact-file-name.ext.sigstore.json",
            "subjectSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        }
    ]
}
```

No top-level keys other than `schemaVersion` and `attestations` are allowed. Each attestation entry must contain exactly `path`, `bundlePath`, and `subjectSha256`. `path` must reference a manifest entry path exactly once. `bundlePath` must be unique, must stay under `attestations/`, and must point to an existing bundle file generated by the attestation job. `subjectSha256` must equal the corresponding digest from `artifact-manifest.json`. The attestation manifest and bundle layout are exact: every manifest entry selected for publication must appear once, no manifest entry may appear twice, and no extra attestation entry or extra bundle file is allowed. When GitHub Release assets are present, this exact published set includes the build-generated `SHA256SUMS` entry because it is part of the final uploaded asset set, not a publish-time side effect.

After verification succeeds, `require-provenance` must write the final durable `artifact-evidence.json` and the copied verification materials to the evidence branch. The final `artifact-evidence.json` uses this exact schema:

```json
{
    "schemaVersion": 1,
    "projectName": "example-project",
    "version": "1.2.3",
    "sourceCommit": "0123456789abcdef0123456789abcdef01234567",
    "workflowRunUrl": "https://github.com/hcoona/three/actions/runs/1234567890",
    "workflowRunAttempt": 1,
    "artifactName": "build-output-example-project",
    "artifactManifestSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
    "artifactManifestBlobApiUrl": "https://api.github.com/repos/hcoona/three/git/blobs/<blob-sha>",
    "buildVerificationInputSha256": "76543210fedcba9876543210fedcba9876543210fedcba9876543210fedcba98",
    "buildVerificationInputBlobApiUrl": "https://api.github.com/repos/hcoona/three/git/blobs/<blob-sha>",
    "attestationManifestSha256": "89abcdef0123456789abcdef0123456789abcdef0123456789abcdef01234567",
    "attestationManifestBlobApiUrl": "https://api.github.com/repos/hcoona/three/git/blobs/<blob-sha>",
    "attestationType": "github-artifact-attestation",
    "verificationTool": "gh attestation verify",
    "verifiedRepository": "hcoona/three",
    "verifiedRef": "refs/heads/main",
    "verifiedSourceSha": "0123456789abcdef0123456789abcdef01234567",
    "verifiedAttestationJobWorkflowRef": "hcoona/three/.github/workflows/official.yml@refs/heads/main",
    "verifiedAttestationWorkflowSha": "89abcdef0123456789abcdef0123456789abcdef",
    "verifiedRepositoryOwner": "hcoona",
    "verifiedBundles": [
        {
            "path": "artifact-file-name.ext",
            "bundleSha256": "fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210",
            "bundleBlobApiUrl": "https://api.github.com/repos/hcoona/three/git/blobs/<blob-sha>",
            "verifiedSubjectSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "verifiedTlogEntryIds": ["1234567890"]
        }
    ]
}
```

No top-level keys other than those shown above, plus optional `verifiedEnvironment` when the verifier surfaced that claim, are allowed. `verifiedBundles` must use the same canonical order as `artifact-manifest.json`. `bundleSha256`, `verifiedSubjectSha256`, `artifactManifestSha256`, `buildVerificationInputSha256`, and `attestationManifestSha256` must each match exactly `[0-9a-f]{64}`. `verifiedRepository`, `verifiedRef`, `verifiedSourceSha`, `verifiedAttestationJobWorkflowRef`, `verifiedAttestationWorkflowSha`, and `verifiedRepositoryOwner` must come from the verifier's trusted output and runtime context, not from the attestation statement predicate and not from any build-supplied file. In this design the attestation jobs are direct jobs in `.github/workflows/official.yml`, so `verifiedAttestationJobWorkflowRef` identifies `official.yml` at the current ref and the language-specific single-attestation-job contract is enforced separately by the selected target set, attestation manifest, and workflow wiring rather than by distinct signer workflow paths. `verifiedEnvironment` is optional and must be absent rather than `null` when the verifier does not surface that claim. The `*BlobApiUrl` fields are immutable Git blobs API URLs by blob SHA rather than GitHub web UI links. The later `require-provenance` job must emit the workflow output `artifact-evidence-url`; the recovery ledger field `artifactEvidenceUrl` then records that same immutable Git blobs API URL for `artifact-evidence.json`. An expiring GitHub Actions artifact URL is never a valid durable evidence reference.

**Verifier signer mapping contract:** The verifier-owned attestation signer mapping for direct official attestation jobs must be checked in at `.github/provenance-signer-map.json` and covered by `CODEOWNERS` plus the `infra` inventory. It uses this exact schema:

```json
{
    "schemaVersion": 1,
    "languages": {
        "csharp": {
            "workflowPath": ".github/workflows/official.yml",
            "attestationJob": "attest-csharp"
        },
        "python": {
            "workflowPath": ".github/workflows/official.yml",
            "attestationJob": "attest-python"
        },
        "jsts": {
            "workflowPath": ".github/workflows/official.yml",
            "attestationJob": "attest-jsts"
        },
        "ruby": {
            "workflowPath": ".github/workflows/official.yml",
            "attestationJob": "attest-ruby"
        }
    }
}
```

No top-level keys other than `schemaVersion` and `languages` are allowed. `schemaVersion` must equal `1`. `languages` must contain exactly the closed language set `{csharp, python, jsts, ruby}`. Each language object must contain exactly `workflowPath` and `attestationJob`; `workflowPath` must equal `.github/workflows/official.yml`, and `attestationJob` must equal the single direct attestation job that is allowed to sign manifest-selected subjects for that language. `require-provenance` must load this mapping from the frozen `main` control-plane snapshot identified by `needs.preflight-check.outputs.main-control-plane-sha`, must require that the resolved `language` maps to exactly one successful attestation job while the other attestation jobs are skipped, and must fail closed on any mismatch. Repository policy must validate the file schema and fail any language-expansion change that does not update this mapping atomically.

**Reproducibility requirement:** Build workflows must configure their packaging tools so reruns from the same source commit and lockfiles produce the same package-file identities. Where a package format embeds timestamps, file ordering, or host-specific metadata by default, the reusable build workflow must normalize those fields before publishing artifacts.

**Artifact retention:** CI artifacts are an ephemeral hand-off mechanism, not permanent release storage. Recommended defaults: `retention-days: 7` for PR and buddy runs, `retention-days: 90` for official runs. The longer official retention window is intentional recovery budget for partial publishes, long-lived approval waits, and post-incident evidence comparison, and it deliberately exceeds GitHub's documented 30-day workflow-rerun limit by a full additional 60 days. It does not eliminate the dead-end case where artifacts expire and the protected branch has since moved, so Section 7 still defines that as a separate recovery boundary.

### Reusable Publish Workflows

Official publish jobs (`publish-nuget-official`, `publish-npm-official`, `publish-pypi-official`, `publish-rubygems-official`, and `publish-github-official`) are ordinary jobs in `official.yml`, not reusable workflows. They may call reviewed local composite actions as steps, but they must not hand official publication off to another workflow. The reusable publish contracts below apply only to buddy GPR publishes.

### Direct Official Composite Actions

The direct official trusted-publisher jobs in `official.yml` may share reviewed logic through local composite actions under `.github/actions/**`, including the final mutation step for targets other than PyPI, because those actions still execute inside the same direct `official.yml` job identity after environment approval and job permissions are established. Those composite actions are part of the control-plane contract and must expose stable machine-readable outputs because `release-complete` consumes job outputs, not step summaries.

| Composite action | Required inputs | Required outputs |
| ---------------- | --------------- | ---------------- |
| `.github/actions/publish-nuget-official` | `artifact-name`, `artifact-manifest-sha256`, `version` | `publish-result` |
| `.github/actions/publish-npm-official` | `artifact-name`, `artifact-manifest-sha256`, `version`, `release-line`, `dist-tags`, `oidc-audience` | `publish-result`, `applied-dist-tags` |
| `.github/actions/prepare-pypi-official` | `artifact-name`, `artifact-manifest-sha256`, `version` | `publish-result`, `packages-dir` |
| `.github/actions/finalize-pypi-official` | `prepared-result`, `publish-step-outcome` | `publish-result` |
| `.github/actions/publish-rubygems-official` | `artifact-name`, `artifact-manifest-sha256`, `version` | `publish-result` |
| `.github/actions/publish-github-official` | `artifact-name`, `artifact-manifest-sha256`, `project-name`, `version`, `tag-name` | `publish-result` |

- `publish-result` must be exactly `new-publish` or `no-op`.
- `applied-dist-tags` must be emitted only by `.github/actions/publish-npm-official` and must be the exact compact canonical JSON array that the composite action validated and applied or confirmed remotely.
- `packages-dir` must be emitted only by `.github/actions/prepare-pypi-official` and must point to the validated artifact directory that `pypa/gh-action-pypi-publish` should upload when `publish-result = new-publish`. When remote identity already proves a full idempotent success, `prepare-pypi-official` must emit `publish-result = no-op` and `official.yml` must skip the upstream PyPI publish step entirely.
- `.github/actions/finalize-pypi-official` must map the prechecked `prepare-pypi-official` result plus the direct upstream publish-step outcome into the final job-visible `publish-result`, so `publish-pypi-official` never reports success merely because the precheck decided publication should be attempted.
- `artifact-manifest-sha256` is the verified digest emitted by `require-provenance` and is mandatory for every local direct official publish helper that reads the downloaded build artifact. Each such helper must recompute the digest of the downloaded `artifact-manifest.json` and fail before any mutation unless it matches both the downloaded `build-verification-input.json` and this caller-supplied digest.
- `.github/actions/publish-npm-official` must publish exactly one pre-built tarball selected from the reviewed artifact manifest and must always invoke npm with `--ignore-scripts`; publishing from an extracted package directory is unsupported in this design.
- `publish-pypi-official` must invoke `pypa/gh-action-pypi-publish` directly from `official.yml`. Any local helper action may validate artifact layout or remote preconditions, but it must not wrap the trusted-publishing step itself; if such a helper reads the build artifact, it must receive and enforce the same verified `artifact-manifest-sha256` contract before the upstream publish step runs.
- These composite actions must not perform their own checkout. The caller job in `official.yml` owns the read-only checkout of the dispatch-selected protected control-plane branch and must pass any additional validated values explicitly through `with:` inputs.
- These composite actions are not authorization boundaries by themselves. Their safety depends on `official.yml` staying on the protected control-plane branch set, on the target-specific publish environment gate, on `CODEOWNERS`, and on the same shell-hardening and workflow-command-file restrictions that apply elsewhere in this design.
- The caller job must map the composite-action step outputs to job outputs explicitly. A direct trusted-publisher job that omits that mapping is malformed for this design even if the composite action itself writes the expected step outputs.

All publish workflows share a common set of inputs, with ecosystem-specific additions:

| Input           | Type     | Required | Description                                    |
| --------------- | -------- | -------- | ---------------------------------------------- |
| `artifact-name` | `string` | Yes      | CI Artifact name to download (from build step) |
| `checkout-ref`  | `string` | Yes      | Exact caller commit SHA that the publish workflow must check out before running trusted helper code |
| `caller-workflow-path` | `string` | Yes | Repository-relative path of the reviewed caller workflow, used as a wiring guard before any credential mint or registry mutation |
| `version`       | `string` | Yes      | Package version string                         |

**Ecosystem-specific inputs:**

| Workflow                | Input          | Type      | Required | Description                                                                                                    |
| ----------------------- | -------------- | --------- | -------- | -------------------------------------------------------------------------------------------------------------- |
| `_publish-nuget.yml`    | `feed-url`     | `string`  | Yes      | NuGet feed URL (GitHub Packages only in this design; must match the reviewed allowlist exactly)               |
| `_publish-npm.yml`      | `registry`     | `string`  | Yes      | npm registry URL (GitHub Packages only in this design)                                                         |
| `_publish-npm.yml`      | `dist-tags`    | `string`  | Yes      | Ordered JSON array of explicit npm dist-tags to write (`["buddy"]` only in this design)                     |
| `_publish-rubygems.yml` | `host`         | `string`  | Yes      | RubyGems host URL (GitHub Packages only in this design)                                                        |

**Common outputs:**

| Output           | Type     | Description                                                                                     |
| ---------------- | -------- | ----------------------------------------------------------------------------------------------- |
| `publish-result` | `string` | Machine-readable publish outcome. Must be exactly `new-publish` or `no-op` for selected jobs. |

**Ecosystem-specific outputs:**

| Workflow             | Output             | Type     | Description |
| -------------------- | ------------------ | -------- | ----------- |
| `_publish-npm.yml`   | `applied-dist-tags` | `string` | Compact ordered JSON array of the exact npm dist-tags that the workflow validated and applied or confirmed as already present |

**Required caller permissions:**

| Workflow                | Required caller `permissions`                |
| ----------------------- | -------------------------------------------- |
| `_publish-nuget.yml`    | `contents: read` plus `packages: write` |
| `_publish-npm.yml`      | `contents: read` plus `packages: write` |
| `_publish-rubygems.yml` | `contents: read` plus `packages: write` |

**Secrets:** `secrets: {}` is mandatory. Reusable publish workflows must never rely on trusted publishing in this design, callers must not pass any non-empty `secrets:` map, and `secrets: inherit` is prohibited.

**JSON output serialization:** Any reusable workflow output in this design whose contract says "JSON array" must emit compact canonical JSON equivalent to `jq -c`, not pretty-printed JSON with discretionary whitespace.

**Checkout behavior:** Publish workflows that execute trusted helper code from the protected control-plane branch must perform their own read-only checkout with `persist-credentials: false` and must check out exactly the supplied `checkout-ref`. Buddy callers must pass `${{ github.sha }}` explicitly; omission is unsupported in this design. Package-registry publish workflows therefore require caller `contents: read` in addition to their registry-specific write scope.

**GitHub Release runner contract:** `publish-github-official` must run on `ubuntu-latest`. Windows runners are unsupported for this job because the release-asset validation and upload path in this design is standardized on POSIX tooling, and this job must validate the build-generated GNU-format `SHA256SUMS` file byte-for-byte without rewriting it.

Local reusable publish workflows are not authorization boundaries by themselves. They rely on `CODEOWNERS`, repository-policy linting that restricts same-repo callers, and the repository's reusable-workflow access policy to keep buddy publish credentials from being minted by unauthorized workflows in the same repository. Target-specific `production-*` environments are part of the direct official-job boundary in `official.yml`, not part of the buddy reusable-workflow boundary. Each local reusable publish workflow must still hard-fail before any checkout, credential mint, or registry mutation unless the runtime repository owner/name matches the checked-in trusted contract and the caller-supplied `caller-workflow-path` input matches the reviewed expected caller path for that target. Because GitHub does not expose a documented trusted runtime caller-workflow-path context inside reusable workflows, this input check is a reviewed wiring guard rather than a standalone authorization boundary. External repository callers are unsupported even if GitHub visibility settings would otherwise allow the call.

**Artifact validation:** Before publishing, each reusable publish workflow must verify that the expected files exist at the artifact root and fail on empty artifacts, missing required files, or ambiguous layouts. Duplicate-version outcomes count as idempotent success only when the remote artifact set matches the local artifact set and the target-specific identity rules documented here. Package-registry publish workflows must validate only manifest entries whose `publishRoles` include `package`. The direct `publish-github-official` job must validate only manifest entries whose `publishRoles` include `github-release-asset`, must verify that at least one such top-level non-manifest file exists in the downloaded artifact, and must fail if release assets are nested under subdirectories instead of flattened at the artifact root. Validation must also reject symlinks, absolute paths, or any archive entry that escapes the artifact root when extracted. After any successful registry mutation or duplicate-version response, the publish path must re-query the remote state with bounded retry and backoff before declaring success; an immediate read that still reports `not found` is `unknown`, not authoritative absence.

For GitHub Releases, remote identity comparison is role-filtered and metadata-aware: the expected asset set is exactly the manifest entries whose `publishRoles` include `github-release-asset`, including the required `SHA256SUMS` entry when GitHub Release publication is selected; internal workflow metadata such as `artifact-manifest.json` is never part of that comparison. A rerun may repair a strict remote subset state when every already-present remote asset, including `SHA256SUMS`, matches the current local digest set and only expected assets are missing; any mismatched existing asset digest is a hard failure.

For GPR targets, publish workflows must treat package versions as immutable within workflow execution. Even though GitHub supports package deletion and restoration with elevated package-admin capabilities, these reusable publish workflows do not request those permissions and must never delete package versions as part of a retry or recovery path.

**GitHub publish tag contract:** `publish-github-official` is official-only in this design. It must hard-fail unless `tag-name == 'release/<project-name>/v<version>'` and the release title is exactly `<project-name> v<version>`.

For GitHub Release consumers, the build artifact contract above requires a deterministic public checksum asset named exactly `SHA256SUMS` whenever any GitHub Release asset is present. `publish-github-official` must treat that file as an ordinary manifest-selected `github-release-asset`, upload it byte-for-byte without regeneration or normalization, and include it in all remote identity checks. `artifact-manifest.json` remains internal workflow metadata; `SHA256SUMS` is the consumer-facing integrity aid that complements, rather than replaces, the attestation-based provenance gate. Because many downstream consumers do not implement GNU escape parsing correctly, compliant release assets in this design must never require escaped control characters in `SHA256SUMS`; the manifest-level control-character filename ban is therefore part of the checksum contract, not a separate optional hygiene rule.

**npm dist-tag policy:** `_publish-npm.yml` is buddy-only in this design. It must use the explicit `dist-tags` input on every publish and must hard-fail before any registry mutation if that input is missing, empty, not valid JSON, or structurally invalid for its own inputs and target registry. Buddy npm publish is tarball-only: the workflow must publish exactly one pre-built tarball from the reviewed artifact manifest and must always pass `--ignore-scripts`; publishing from an extracted package directory is unsupported. For GitHub Packages publishes, the contract is simple: `dist-tags` must be exactly `["buddy"]`, `release-line` must be absent, and the workflow must never write `latest`.

`_publish-npm.yml` must treat tarball idempotency and dist-tag idempotency as separate checks. When the package version already exists and the remote tarball identity matches the local artifact set, the workflow must still query the current owner of the required `buddy` tag and attach it if missing. The reserved-family tags for buddy npm publish are exactly `buddy`, `latest`, and every tag that begins with `release-v`; any existing reserved-family tag other than the required `buddy` tag that points to the same package version, or any conflicting owner for `buddy`, is a hard failure because it makes the buddy publish ambiguous with official-line semantics. `_publish-npm.yml` must emit the exact validated ordered JSON array through the `applied-dist-tags` output.

**GitHub release identity metadata and scan completion:** `publish-github-official` must use deterministic release titles. For official releases, the title must be `<project-name> v<version>`. `publish-github-official` must derive prerelease state from the validated version string: prerelease versions must create or preserve GitHub Releases with `prerelease: true` and must not become the repository's Latest Release, while stable versions must create or preserve releases with `prerelease: false`. Any GitHub Release scan used by this design, whether in `resolve-context` or in `publish-github-official`, must follow pagination until the relevant result set is exhausted and must hard-fail on API, authentication, authorization, rate-limit, transport, or response-shape errors. An interrupted, truncated, or otherwise incomplete scan is `unknown`, not `not found`; overwrite, no-op, and conflict decisions must never be made from a partial page. `publish-github-official` must repeat the deterministic title conflict scan immediately before it mutates any GitHub Release record or release asset, and must hard-fail if the same deterministic release title exists under a different tag or commit than the current official release identity.

**Publish result signaling:** Each reusable publish workflow must emit a workflow output `publish-result` whose value is exactly `new-publish` or `no-op`. It must also append a machine-readable one-line summary to `$GITHUB_STEP_SUMMARY`, for example a single-line JSON object containing at least `target`, `version`, `publishResult`, `callerRef`, and `workflowRunUrl`, for human diagnostics. npm publish summaries must additionally include the validated `appliedDistTags`. Step summaries are not the authoritative source of truth for `publishedTargets`; the authoritative workflow-side source is the later `confirm-publish-state` job's live remote inspection. `release-complete` must aggregate the selected-target outcomes into its own step summary and must treat a missing or malformed `publish-result` output for a selected target as a hard failure. `publish-result` is orthogonal to the workflow job result: a selected publish target that proves remote identity matches local output must still finish with job `result == "success"` even when `publish-result == "no-op"`; `result == "skipped"` is reserved for non-selected targets only.

## 7. Overwrite and Idempotency Policy

Both `buddy.yml` and `official.yml` check for existing artifacts before proceeding. The policy differs by channel:

### Buddy (Unofficial)

| Condition                                                                                                           | Behavior                                                                               |
| ------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| Package version already exists at GPR with matching remote artifact identity                                        | **Success** (idempotent publish scripts)                                               |
| Package version already exists at GPR with different remote artifact identity                                       | **Hard fail** — cut a new version; workflow does not delete and republish GPR versions |
| Authn/authz failure or upstream `5xx` at GPR                                                                        | **Hard fail** — not idempotent                                                         |

### Official (Production)

GitHub Release rows apply only when `github:official` is present in the resolved target list.

| Condition                                                                                          | Behavior                                                                                       |
| -------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| Derived official release tag does not exist yet                                                    | **Proceed** — normal first official run                                                        |
| Derived official release tag exists and points to the same commit                                  | **Proceed** (idempotent tag reservation no-op)                                                 |
| Derived official release tag exists and points to a different commit                               | **Hard fail** — official release identity must not be rebound to a different commit            |
| No GitHub Release exists for the derived official tag                                              | **Proceed** — normal first official run                                                        |
| Current resolved version is stable and a non-pre-release GitHub Release exists for the derived official tag with matching remote artifact identity  | **Success** (idempotent no-op)                                                        |
| Current resolved version is stable and a non-pre-release GitHub Release exists for the derived official tag with different remote artifact identity | **Hard fail** — release assets must not silently diverge from the local build output    |
| Non-pre-release GitHub Release exists for the same deterministic stable title `<project-name> v<version>` but different tag/commit | **Hard fail** — stable releases in that stable identity space must not be rebound to a different release identity |
| Current resolved version is stable and a pre-release GitHub Release exists for the derived official tag with matching remote artifact identity | **Replace with stable release** using the current local build output for that tag            |
| Current resolved version is stable and a pre-release GitHub Release exists for the derived official tag with different remote artifact identity | **Hard fail** — official stable promotion must not overwrite a divergent same-tag pre-release |
| Current resolved version is prerelease and a pre-release GitHub Release exists for the derived official tag with matching remote artifact identity | **Success** (idempotent no-op) — preserve the existing prerelease state |
| Current resolved version is prerelease and a pre-release GitHub Release exists for the derived official tag with different remote artifact identity | **Hard fail** — prerelease assets must not silently diverge from the local build output |
| Current resolved version is prerelease and a non-pre-release GitHub Release already exists for the derived official tag | **Hard fail** — a prerelease run must not downgrade or reinterpret an already-stable GitHub Release |
| Package version already exists at official registry with matching remote artifact identity         | **Success** (idempotent publish scripts; for npm, requested dist-tag state must also pass the explicit policy below, and every such success still requires bounded remote re-check for eventual consistency) |
| Package version already exists at official registry with different remote artifact identity        | **Hard fail** — cut a new version; official registry versions are immutable release identities |
| Authn/authz failure or upstream `5xx` at official registry after bounded in-run retry             | **Hard fail** — not idempotent                                                                 |

For official npm publishes, remote tarball identity alone is not sufficient for idempotent success. The direct `publish-npm-official` job in `official.yml` must also evaluate every requested entry in the ordered `dist-tags` array. A requested tag that already points to the same version is `no-op`; a missing requested tag may be attached to that same version; a requested tag that points to a different version may be advanced only when the current run's version has higher SemVer precedence in that same deterministic tag family. Any retarget that would move `latest`, a prerelease channel tag, or a maintenance-line tag backward is a hard failure.

For official NuGet publishes, idempotent success must account for symbol packages explicitly. If the primary `.nupkg` already exists with matching package identity and the local artifact set also includes an expected `.snupkg` that is still absent remotely, a rerun may perform a supplementary symbol-package upload only; the target is not a full `no-op` until the expected symbol state is reconciled or intentionally declared absent by policy.

For official RubyGems publishes, a yanked version is never treated as idempotently published. Public RubyGems documentation does not define same-version republish after `gem yank` as a stable supported recovery path, so this design treats a yanked `rubygems:official` identity as burned for same-version workflow recovery and requires a new version or an explicit terminal withdrawal decision.

### Recovery Playbook

If a workflow run fails partway through (for example `nuget:gpr` succeeds but `npm:gpr` fails), use the first matching recovery path below and do not mix strategies:

1. If `preflight-check` fails, treat it as an environment or control-plane configuration issue rather than a source-code defect: fix whichever preflight invariant failed, including the required reviewers on every selected target-specific publish environment plus `production-tag-write-<project-name>` and `production-evidence-write-<project-name>`, the exact deployment branch set on `control-plane-monitoring`, `prevent_self_review = true` where required, exact deployment branch names only, the required maintenance-branch protection profile, active branch/tag rulesets, and Rulesets API read-credential sufficiency, then trigger a new run.
2. If execution fails before any publish job starts in **buddy** (for example `resolve-context`, `static-analysis`, or build failure), fix the repository or configuration issue and trigger a fresh buddy workflow dispatch. No remote release state has been mutated yet.
3. If execution fails before any publish job starts in **official** and `resolve-context` never finished successfully, fix the repository or configuration issue and trigger a fresh official workflow dispatch from the intended protected branch. If the failure happened during publish trust inventory preflight, reconcile `.github/publish-trust-inventory.json`, the selected caller ref, the expected checked-in publish execution paths, and the expected target auth mechanisms on that control-plane branch before retrying. No remote official release state has been mutated yet.
4. If `resolve-context` succeeded but `static-analysis`, a build job, or `require-provenance` later failed in **official** before `create-release-tag` succeeded, fix the source on the appropriate protected control-plane branch or supported maintenance branch and trigger a fresh official workflow dispatch. No official release tag or remote publish state has been mutated yet. If `require-provenance` may have written or partially advanced durable evidence, inspect both `refs/heads/release-evidence` and `refs/tags/control-plane/release-evidence-head/<project-name>` before retrying. When the branch tip and the selected project's anchor both point to the same newly verified evidence commit for that run, the failure remains a normal pre-tag retry case. If the branch tip, the project-scoped anchor tag, or the expected durable evidence directory diverged or was only partially advanced, stop normal retry and hand off to the emergency-cleanup path before any further official attempt.
5. Distinguish between **Re-run jobs** and a fresh **workflow_dispatch**. Before choosing a rerun path, first verify in the GitHub Actions run UI or API that the original run's artifacts still exist, then check the documented GitHub lifetime boundaries on the original run: GitHub Actions rerun availability is 30 days from the initial run, GitHub's documented gate approval time is 30 days for environment approvals, and artifact retention follows the configured retention window. These timers are independent. A retained artifact does not keep an expired run rerunnable, and a still-rerunnable run does not guarantee that operators should continue waiting indefinitely on pending approvals. GitHub reruns also execute the original workflow snapshot from the original run's ref; they do not pick up later fixes to workflow files, reusable workflows, helper scripts, or composite publish actions on the branch. After run expiry or approval expiry, do not use GitHub's Re-run button. Any same-identity rebuild attempt after run expiry or artifact expiry must still satisfy step 10's durable-evidence rule before a fresh dispatch is allowed. If an approval has been pending long enough that operators no longer trust the run context, audit the official tag and publish state first, then choose recovery under the later rules below.
6. If the failure is transient (network issue, auth outage, or upstream `5xx`), prefer **Re-run failed jobs** on the same workflow run so the original commit, derived version, and derived official tag remain unchanged, but only when every remaining selected target is in a failed state. If any required selected job is `cancelled`, `skipped`, `requested`, `waiting`, or waiting for approval, use **Re-run all jobs** instead. Reviewer decline, operator cancellation, and any other path that settles as cancellation rather than a normal failed-job subset therefore require **Re-run all jobs**. On any rerun, every job gated by `production-evidence-write-<project-name>`, `production-tag-write-<project-name>`, or a target-specific publish environment re-enters the approval queue as a fresh request; no prior approval carries forward. This rerun guidance applies only while the original run is still rerunnable under step 5's lifetime limits and while no workflow-code fix is required. Deterministic artifact uploads must use `overwrite: true`, so reruns are expected to replace same-run artifact names cleanly rather than fail on name collisions. If workflow logic, reusable workflow wiring, trusted helper code, or the checked-in trust inventory had to be fixed, use a fresh dispatch instead. Before any fresh buddy or official dispatch for the same project/ref concurrency group, inspect that group's queued and in-progress runs and cancel stale queued runs rather than assuming the queue will preserve operator intent. A fresh official workflow dispatch is valid only when the selected protected branch still points to the same commit as the original run; otherwise it is a new release attempt and must be treated as such. Matching already-published artifacts must settle as idempotent no-ops. Each official run must surface the current wait age of every pending `production-evidence-write-<project-name>`, `production-tag-write-<project-name>`, and target-specific publish-environment approval, plus the run-rerun, gate-approval, and artifact-retention deadlines, in its step summary so operators do not have to reconstruct those timers manually.
7. If official publish jobs partially succeeded because some destinations were approved and others were declined or failed transiently, rerun the same official workflow run whenever possible and only while step 5 still permits reruns. Already-published destinations must settle as idempotent no-ops, and the remaining destinations will request fresh approval. If the partial-failure path includes a declined environment approval or any other approval path that settled as cancellation, use **Re-run all jobs** rather than **Re-run failed jobs**, following step 6's cancellation-path rule. If the official tag was already created but all later approvals were declined or the run was cancelled, rerun the same workflow run or dispatch the same protected branch again while it still points to the same commit; the tag reservation must settle as an idempotent no-op. For npm specifically, reruns must reuse the same deterministic tag array derived from the version channel and release line: stable mainline releases keep `["latest"]`, prerelease mainline releases keep their prerelease channel tag array, stable maintenance-line releases keep `["release-v<major>.<minor>"]`, and maintenance-line prerelease releases keep `["release-v<major>.<minor>-<channel>"]`. Reruns must never move any npm dist-tag backward. Do not retire or decommission that source branch until the partial-publish state has either been completed successfully or explicitly declared burned.
8. If an official tag reservation is no longer wanted after approvals were declined or after a maintenance-branch retirement cancelled the run, release engineering must resolve that explicitly rather than leaving an orphaned tag behind. Before deleting the tag, first determine the actual remote publish state by live inspection, not by job summaries alone. If `publishedTargets = []`, write an incident ledger entry whose `unpublishedTargets` and any still-unproven `indeterminateTargets` form a complete disjoint partition of the original selected official targets in canonical order, and use `disposition = "open-before-publish"` while the identity remains under investigation or completion remains under consideration. Targets belong in `unpublishedTargets` only when live inspection proved they were not published; targets with still-unproven state belong in `indeterminateTargets` instead. `disposition = "abandoned-before-publish"` is allowed only after live inspection proves `indeterminateTargets = []` and operators intentionally stop before any official target was published. If some but not all official targets were already published, write either `disposition = "open-partial-publish"` when completion is still under consideration or `disposition = "abandoned-after-partial-publish"` when operators intentionally stop completing the remaining targets. The only supported abandon path is then reviewed deletion of `release/<project-name>/v<version>` through the dedicated emergency-cleanup helper `eng/scripts/official_emergency_cleanup.py`, using the authorized `refs/tags/release/**` bypass actor, followed by a fresh official release attempt from a later intended-release commit on an active protected branch so the workflow derives a different release identity. Tag deletion is only cleanup; it must never be used to silently rebind the same deterministic release identity to a different commit or artifact set. Silent abandonment of orphaned official tags is unsupported.
9. If official `resolve-context` fails because a non-pre-release GitHub Release, including a draft, already occupies the deterministic stable title `<project-name> v<version>` under a different tag or commit, stop rerunning immediately. A draft stable release is part of the same stable identity space as a published stable release and blocks the new official attempt by design. Release engineering must either preserve that existing stable identity and cut a different version from a corrected commit, or explicitly delete the conflicting release identity before rerunning. Deleting a conflicting **draft** stable release is a normal recovery action. Deleting a conflicting **published stable** release requires an explicit consumer-impact review first, because that action removes a publicly visible production artifact set. That review must be tracked in a dedicated incident or follow-up issue, must be approved by at least two humans with one approver distinct from the operator requesting deletion, must evaluate consumer impact such as download volume and known dependents, and must define any target-side deprecate, unlist, yank, or remove action that should happen before deletion. The same review must explicitly assess every already-published target for that conflicting release identity and decide whether each one remains live, is deprecated, is delisted, or is removed before the GitHub Release deletion proceeds. Those decided target-side actions must be completed and reflected in `.github/release-recovery-ledger.jsonl` before deleting the conflicting published stable GitHub Release or rerunning the workflow; use `deprecatedTargets` for targets that remain installable with an explicit warning, `delistedTargets` for already-published NuGet targets that were unlisted from normal discovery but remain directly installable, `removedTargets` for targets that were removed from normal availability, and `retainedTargets` for reviewed targets intentionally left live. Once the conflicting published stable GitHub Release is actually deleted, `github:official` must appear in `removedTargets` on the closing terminal ledger update unless the identity remains open for further investigation. The review must impose a minimum 48-hour hold period before deleting the published stable release. That hold begins when the dedicated incident or follow-up issue is opened, and the ledger entry for this path must record `holdStartedAt`, `eligibleDeleteAt`, and `consumerImpactEvidenceUrl` before the deletion proceeds. `consumerImpactEvidenceUrl` must point to timestamped provider evidence captured no more than 15 minutes before the decision. If any already-published target lacks a trustworthy download metric, the deletion remains blocked until equivalent reviewed consumer-impact evidence exists. Any associated `release/<project-name>/v<version>` tag must be reconciled using the same authorized tag-deletion mechanism from step 8, but the ledger disposition for this path must match the actual already-published state rather than reusing `abandoned-before-publish`. Renaming the GitHub Release title to sidestep the deterministic-title guard is unsupported.
10. If artifacts expired for an official run but the selected protected branch still points to the same commit, trigger a fresh official workflow dispatch from that same branch only when durable evidence from the original run still exists and can be compared against the rebuilt output. That durable evidence is the immutable evidence directory anchored by `artifactEvidenceUrl`, including the committed `artifact-evidence.json`, `artifact-manifest.json`, `build-verification-input.json`, `attestation-manifest.json`, and `attestations/` bundle files copied from the build, attestation, and verification jobs; an expiring GitHub Actions artifact URL is not sufficient. The comparison algorithm is deterministic-plus-equivalence, not byte identity of Sigstore bundles: the rebuilt run must reproduce the same `artifact-manifest.json` bytes and the same `build-verification-input.json` bytes, and the rebuilt `artifact-evidence.json` must prove the same verified repository, verified ref, verified source SHA, verified attestation-job workflow ref, verified attestation workflow SHA, verified repository owner, optional verified environment, and canonical per-subject digests recorded in the original durable evidence directory. The rebuilt `attestation-manifest.json` and bundle files may differ in run-scoped metadata such as bundle digests, bundle blob URLs, and transparency-log entry IDs, but every rebuilt bundle must still verify successfully and bind the same manifest-selected artifact subjects as the original durable evidence. Any deterministic-file mismatch, claim mismatch, subject-digest mismatch, verification failure, or missing durable evidence burns that identity rather than authorizing a same-identity rebuild. This is the authoritative rule for the 31-90 day window where the run may be expired but official artifacts still exist. If artifacts expired and the protected branch has already moved to a different commit, stop trying to complete the old partially published identity. Treat that earlier version as burned or partially withdrawn, apply the target-specific withdrawal action defined below to any already-published artifacts whose continued availability is no longer acceptable, record those actions in the ledger's `deprecatedTargets`, `delistedTargets`, `removedTargets`, and `retainedTargets` fields as applicable, fix the source on the correct branch, and continue with the next version derived from the corrected commit.
11. If `confirm-publish-state` exhausts its retry budget without proving the complete selected target set, or if it completes with `confirmation-state` in `{partial-timeout, partial-upstream-failure}`, stop using blind reruns as the default response. First preserve the latest `confirm-publish-state-checkpoint.json` artifact and one-line JSON checkpoint emitted into `$GITHUB_STEP_SUMMARY` as recovery hints, then reconstruct live remote state for every selected target, the official tag, and the GitHub Release using the same canonical target order and evidence rules as `confirm-publish-state`, then record or update the incident in `.github/release-recovery-ledger.jsonl` before deciding whether the identity is still recoverable. Manual reconstruction is not free-form: use the same registry APIs the workflow-side checks use, namely the NuGet flat-container or package-content endpoints for `nuget:official`, `npm view <package>@<version> --json` or the npm registry metadata API for `npm:official`, the PyPI JSON API `/pypi/<project>/<version>/json` for `pypi:official`, the RubyGems versions API for `rubygems:official`, and the GitHub Releases API for `github:official`. Evidence must be recorded as a timestamped permalink or captured response URL in the ledger. If provider availability prevents a trustworthy conclusion for one or more selected targets, record those targets in `indeterminateTargets` rather than forcing them into `unpublishedTargets`. If live inspection shows every selected destination actually converged and the failure was only bounded-read eventual consistency, fix the retry budget or polling logic on the protected control-plane branch before the next run. If live inspection shows real divergence or an incomplete publish, continue under the later recovery rules based on that observed state rather than on the failed job conclusion alone.
12. If `release-complete` fails because a selected publish job was skipped, an unexpected non-selected job ran, the canonical target ordering or JSON shape is wrong, or any other target-to-job mapping assertion failed, stop rerunning immediately. Treat that as workflow wiring drift or other control-plane code defect, fix the workflow via the normal protected-branch review path, and only then dispatch again. If any official destination already published before this failure was detected, record or update the incident in `.github/release-recovery-ledger.jsonl` before considering the response operationally closed. When reconstructing `publishedTargets`, use live remote inspection equivalent to `confirm-publish-state`; machine-readable publish outputs and step summaries are diagnostic hints only. If `confirm-publish-state` completed successfully with `confirmation-state = complete` before `release-complete` failed, its `confirmed-published-targets` output already satisfies the live-inspection requirement for `publishedTargets`; a second manual inspection is required only if later evidence suggests that the confirmation budget itself was insufficient. `publishedTargets` means destinations where the official artifact is now fully present at the registry, regardless of whether that presence came from `new-publish` in this run or `no-op` from a prior run.
13. If the failure is caused by malformed build output or a remote artifact identity mismatch, stop retrying the same release identity. For buddy, if a GPR package version already exists with different artifact identity, cut a new version rather than deleting and republishing. For official, if the immutable official release tag was already created for that failed attempt, do not retarget it. Fix the source on the correct protected branch and run official again so the workflow derives a new release identity from the corrected commit. If the corrected commit still resolves to the same version and the old `release/<project-name>/v<version>` tag already points to the failed commit, do not keep retrying blindly: record and, if needed, clean up the burned identity through step 8's authorized tag-deletion path, but do not treat tag deletion as authorization to reuse that same version on a different commit. The supported retry path after this class of failure is to bump the version on the protected branch so the next official dispatch derives a different release identity.
14. If official publish jobs fail with authentication or authorization errors immediately after a new maintenance branch, trusted workflow path, protected control-plane branch change, or trusted-publisher configuration change was introduced, diagnose the mismatch direction explicitly. If repository-side publish trust inventory preflight fails first, fix `.github/publish-trust-inventory.json` or roll back the external auth configuration change before retrying. If publish trust inventory preflight succeeds but publish still fails at the registry, verify and restore the external auth configuration for the expected selector model: for npmjs verify the calling entry workflow path, environment, fixed audience `npm:registry.npmjs.org`, and the deterministic dist-tag choice; for PyPI verify the configured entry workflow path, environment, and the provider-host runtime audience-discovery behavior expected by `pypa/gh-action-pypi-publish`; for RubyGems.org verify the configured entry workflow path and environment; for NuGet.org verify the configured entry workflow path, environment, and the documented `NuGet/login@v1` trusted-publishing binding. Treat both cases as control-plane configuration drift, not as a package-content defect. The repository must maintain a registry-specific rollback runbook for NuGet.org, npmjs, PyPI, and RubyGems.org that records the exact UI path, API call, or support-escalation path used to remove, rotate, or restore the relevant trusted-publisher selector for each registry.
15. If `create-release-tag` pushed the official tag and the runner crashed before the job result was recorded, rerun the same workflow run. Tag creation must settle as an idempotent no-op, after which the remaining official publish jobs can continue through the normal approval and idempotency flow.

The repository must maintain a durable recovery ledger at `.github/release-recovery-ledger.jsonl`, outside ephemeral workflow logs, for every burned, deprecated, delisted, removed, partially withdrawn, or partially published official release identity and for every required periodic tag audit. Each line must be a standalone JSON object with `schemaVersion: 1` and `recordType` in `{incident, audit}`. `incident` records must contain `schemaVersion`, `recordType`, `incidentId`, `revision`, `recordedAt`, `projectName`, `releaseLine`, `version`, `reservedTag`, `sourceCommit`, `evidenceUrl`, `attemptScope`, `disposition`, `owner`, `severity`, `acknowledgedAt`, `operatorRationale`, `selectedTargets`, `publishedTargets`, `unpublishedTargets`, `tagState`, and `githubReleaseState`, and optional `nextReviewAt`, `workflowRunUrl`, `runAttempt`, `credentialId`, `indeterminateTargets`, `deprecatedTargets`, `delistedTargets`, `removedTargets`, `retainedTargets`, `followUpIssue`, `followUpStatus`, `closedAt`, `artifactEvidenceUrl`, `holdStartedAt`, `eligibleDeleteAt`, `consumerImpactEvidenceUrl`. `audit` records must contain `schemaVersion`, `recordType`, `auditId`, `revision`, `recordedAt`, `evidenceUrl`, `attemptScope`, `scope`, `result`, `owner`, `severity`, `operatorRationale`, and optional `nextReviewAt`, `workflowRunUrl`, `runAttempt`, `credentialId`, `followUpIssue`, `followUpStatus`, `closedAt`, `automationId`, and `scriptVersion`. `evidenceUrl` is the canonical evidence reference and must point either to a workflow run URL or to a non-workflow evidence permalink such as an issue, PR, or audit-log entry. `incidentId` and `auditId` must each be UUIDv4 strings. `revision` must start at `1` for the first line of a given incident or audit and increase by exactly `1` on each later line that reuses the same identifier; unrelated incidents and audits must use new IDs. `releaseLine` is required on every incident record and must use the same normalized `<major>.<minor>.x` form used elsewhere in this design. `attemptScope` must use the closed set `{single-run-attempt, no-single-run-attempt}`. `workflowRunUrl` and `runAttempt` are required when `attemptScope = single-run-attempt` and must both be absent when `attemptScope = no-single-run-attempt`. `disposition` must use the closed set `{open-before-publish, open-partial-publish, recovered, burned, abandoned-before-publish, abandoned-after-partial-publish, partially-withdrawn, fully-withdrawn}`. `owner` must be a durable team or group slug, `severity` must use the closed set `{sev0, sev1, sev2, sev3}`, `acknowledgedAt` is required on incident records as soon as the incident is opened, and `nextReviewAt` is required on every open incident and every audit record that still requires follow-up. `credentialId`, when present, must identify the rotated secret, trusted-publisher selector, or App credential involved in the incident or audit. `selectedTargets`, `publishedTargets`, `unpublishedTargets`, `indeterminateTargets`, `deprecatedTargets`, `delistedTargets`, `removedTargets`, and `retainedTargets` must each be ordered JSON arrays of unique values drawn from the official target set `{nuget:official, npm:official, pypi:official, rubygems:official, github:official}` using that exact canonical order. For every incident record, `selectedTargets` must be non-empty, `publishedTargets`, `unpublishedTargets`, and `indeterminateTargets` must be pairwise disjoint, and `publishedTargets ∪ unpublishedTargets ∪ indeterminateTargets` must equal `selectedTargets`; partial ledgers that omit selected targets or silently collapse provider-unavailable reads into `unpublishedTargets` are invalid. Non-empty `indeterminateTargets` means live inspection could not yet prove either published or unpublished state for those targets, typically because a provider was unavailable or returned insufficient trustworthy evidence, and therefore the incident must remain open. `deprecatedTargets`, `delistedTargets`, `removedTargets`, and `retainedTargets`, when present, must each be subsets of `publishedTargets` and must be pairwise disjoint. `delistedTargets` is reserved for already-published NuGet targets that were unlisted from normal discovery but remain directly installable, so any non-NuGet value in that field is invalid. For every terminal incident with `publishedTargets != []`, exhaustive target accounting is mandatory: `retainedTargets ∪ deprecatedTargets ∪ delistedTargets ∪ removedTargets` must equal `publishedTargets`. `open-before-publish` requires `publishedTargets = []` and `unpublishedTargets ∪ indeterminateTargets = selectedTargets`. `open-partial-publish` requires `publishedTargets` to be non-empty and at least one target to remain either in `unpublishedTargets` or `indeterminateTargets`. `recovered` requires `unpublishedTargets = []`, `indeterminateTargets = []`, `retainedTargets = publishedTargets`, and `deprecatedTargets`, `delistedTargets`, plus `removedTargets` to be absent. `abandoned-before-publish` is reserved for identities with `publishedTargets = []`, `indeterminateTargets = []`, and `unpublishedTargets = selectedTargets`. `abandoned-after-partial-publish` is reserved for identities where `publishedTargets` is non-empty, `indeterminateTargets = []`, and operators intentionally stopped completing the remaining `unpublishedTargets`. `partially-withdrawn` is reserved for a previously published identity with `unpublishedTargets = []`, `indeterminateTargets = []`, `publishedTargets` non-empty, at least one retained published target, and at least one published target later deprecated, delisted, or removed. `fully-withdrawn` is reserved for a previously published identity whose remaining live targets were all later either deprecated, delisted, or removed from normal consumer availability; it requires `unpublishedTargets = []`, `indeterminateTargets = []`, `publishedTargets` to be non-empty, `retainedTargets` to be absent, and `deprecatedTargets ∪ delistedTargets ∪ removedTargets` to equal `publishedTargets`. `burned` is reserved for identities that cannot be completed safely as the same release identity because the source, artifact, control-plane, or retention boundary has been invalidated; it may include a mix of `deprecatedTargets`, `delistedTargets`, `removedTargets`, and `retainedTargets`, but when `publishedTargets != []` the exhaustive target-accounting rule still applies so the final state of every published target is explicit. `burned` requires `indeterminateTargets = []`; if live state is still unresolved for one or more targets, the incident must remain in an open disposition until those targets are classified. `artifactEvidenceUrl` is required whenever the incident occurs after `require-provenance` has durably produced the final repository-controlled `artifact-evidence.json`, must point to that durable copy rather than to an expiring CI artifact URL, and is mandatory before any same-identity rebuild is attempted after artifact expiry. Incidents that occur after build artifact creation but before successful durable evidence creation may omit `artifactEvidenceUrl`, but those incidents are not eligible for a same-identity rebuild unless a later record adds durable evidence. `tagState` must use the closed set `{not-created, created-at-source-commit, created-at-different-commit, manually-deleted}`. `githubReleaseState` must use the closed set `{absent, draft-prerelease-same-identity, published-prerelease-same-identity, draft-stable-same-identity, published-stable-same-identity, conflicting-other-identity, manually-deleted}`. `audit.scope` must use the closed set `{full-release-tag-namespace, project-release-line, single-release-identity, emergency-cleanup-governance, control-plane-monitoring}`. `audit.result` must use the closed set `{clean, discrepancy-found, reconciled-during-audit}`. `followUpStatus` must use the closed set `{not-required, required-open, resolved}`. `closedAt` must be absent for open incidents with `disposition` in `{open-before-publish, open-partial-publish}`, and required for every closed incident state; `null` is not a valid value. Once recorded on an incident or audit line, `closedAt` is immutable and any later correction must appear as a new line rather than rewriting history. For `audit` records, `closedAt` must be absent when `followUpStatus = required-open` and required when `followUpStatus` is either `resolved` or `not-required`. `automationId` and `scriptVersion` are required whenever an `audit` record is prepared automatically by workflow automation and must be absent for manually authored audit records; they form an inseparable pair, so one without the other is invalid. `holdStartedAt`, `eligibleDeleteAt`, and `consumerImpactEvidenceUrl` are required when the 48-hour hold in recovery step 9 applies and must be absent otherwise. `recovered`, `burned`, `abandoned-before-publish`, `abandoned-after-partial-publish`, `partially-withdrawn`, and `fully-withdrawn` are terminal dispositions; a closed incident record must never transition back to an open disposition, and any later correction must be recorded as a new ledger line that reuses the same `incidentId` and references the earlier incident via `operatorRationale` and `followUpIssue` when applicable. The validator must enforce a strict top-level key whitelist for both `incident` and `audit` records equivalent to JSON Schema with `additionalProperties: false`. The ledger is trusted control-plane state: routine ledger updates land only through reviewed PRs on `refs/heads/main` under `CODEOWNERS` review. Automation must not push `.github/release-recovery-ledger.jsonl` directly; instead it may open or update tracked issues, attach candidate JSON payloads, or open reviewed PRs for humans to merge. Because the schema is intentionally strict, the repository must also provide reviewed helpers under `eng/scripts/` to create and validate ledger incident and audit records, to render candidate PR payloads, and to perform reviewed orphan-tag deletion through the authorized bypass path; ad hoc raw JSONL editing or raw `git push :refs/tags/...` commands are unsupported outside the emergency-cleanup path. During a P0 or P1 incident, the dedicated emergency-cleanup group may use a break-glass bypass to land a minimal incident or audit ledger update directly on `main`, but that entry must include an `operatorRationale` explaining the bypass, must modify only `.github/release-recovery-ledger.jsonl`, and must be followed by a reviewed cleanup PR on the protected control-plane branch set by 17:00 UTC on the next Monday-Friday business day that either preserves the exact emergency record or replaces it with a reviewed equivalent without losing history. Control-plane monitoring audit automation must separately alert if that reviewed cleanup PR deadline is missed. Every incident that burns, partially withdraws, fully withdraws, or partially publishes an official release identity must add or update a ledger entry before the incident is considered operationally closed.

The repository must maintain a target-specific withdrawal runbook and treat it as part of the reviewed recovery design. At minimum: NuGet.org withdrawal means unlist only, must be documented as still directly installable by exact version, and must be recorded in `delistedTargets` rather than `removedTargets`; npmjs withdrawal means `npm unpublish` only within npm's currently documented supported window, with the explicit acknowledgement that an unpublished `name@version` tuple cannot be reused later and that removing all versions of a package name imposes npm's documented 24-hour package-name publish block, otherwise `npm deprecate` with explicit messaging and no attempt to force a same-version republish; PyPI withdrawal defaults to yank, while delete is allowed only under explicit second-human approval plus explicit acknowledgement that the deleted filename/version can never be re-uploaded later and therefore burns that exact PyPI identity; RubyGems.org withdrawal means `gem yank` with the explicit note that historical mirrors or direct-fetch paths may still retain the artifact and that same-version republish after yank is unsupported by this design because public RubyGems documentation does not guarantee it; GitHub Release withdrawal for `github:official` means deleting the release object itself and then explicitly reconciling the associated `release/<project-name>/v<version>` tag through step 8, with any associated terminal state recorded in `removedTargets` once deletion actually occurs. Disabling the tag ruleset manually in the GitHub UI to perform that cleanup is unsupported; the only allowed break-glass path is the reviewed emergency helper `eng/scripts/official_emergency_cleanup.py`, using the pre-authorized bypass actor, and recording the action in the ledger. The ledger meaning is exact: `deprecatedTargets` is for targets that remain installable but carry deprecation or equivalent warning state, `delistedTargets` is for already-published NuGet targets hidden from normal discovery but still directly installable, `removedTargets` is for targets removed from normal consumer availability, and `retainedTargets` is for targets intentionally left live. When a partial official release is followed by PyPI deletion or any other irreversible per-target burn, the same reviewed ledger update must explicitly decide for every surviving already-published non-PyPI target whether it stays live, is deprecated, is delisted, or is removed before any new-version dispatch proceeds; silent mixed-state carry-forward is unsupported. When all previously published targets have been either deprecated, delisted, or removed and none remain retained, the incident must settle as `fully-withdrawn`. Any recovery path that says "withdraw" or "remove from normal availability" must follow this target-specific runbook rather than relying on a vague strongest-available-action phrase. The withdrawal runbook and the registry-auth rollback runbook from step 14 must both be re-attested by release engineering at least every 90 days, with the attestation evidence recorded either in the protected control-plane branch history or in the recovery ledger as an `audit` record.

Operators must also treat GitHub's lifetime limits as first-class recovery boundaries. GitHub's documented 30-day workflow-rerun limit, GitHub's documented 30-day gate approval time for environment approvals, and the recommended 90-day official artifact retention are distinct timers with different failure modes even when two of them currently share the same numeric value. If a run itself expires while approvals are still pending, audit the resulting `release/**` tag state against completed `release-complete` runs before choosing recovery, even if artifacts are still retained. If artifacts later expire as well, the original run is no longer recoverable and the same-identity rebuild rule from step 10 becomes the only supported path from the still-unchanged protected branch. The repository's privileged monitoring workflow-file allowlist is `{.github/workflows/control-plane-drift-monitor.yml, .github/workflows/official-run-health-monitor.yml, .github/workflows/control-plane-post-tag-failure.yml, .github/workflows/open-incident-freshness-monitor.yml, .github/workflows/release-operational-audit.yml, .github/workflows/governance-and-runbook-freshness.yml}`; together with `.github/workflows/official.yml`, those are the only workflows allowed to reference `control-plane-monitoring`. Only `.github/workflows/official-run-health-monitor.yml` and `.github/workflows/control-plane-post-tag-failure.yml` may use `workflow_run`; the other allowlisted monitor workflows are schedule-driven. The repository must maintain five control-plane monitors in addition to the reviewed ledger updates. First, a scheduled control-plane drift monitor at `.github/workflows/control-plane-drift-monitor.yml` must run at least every 30 minutes, use the `high-nonpage` route, verify live deployment policies against `main`'s authoritative checked-in control-plane inventories, verify that the `control-plane-monitoring` environment remains present, bypass-disabled, configured with the exact deployment branch set from `main`'s caller-ref registry, and still carries `CONTROL_PLANE_ENVIRONMENT_ROLE = monitoring`, verify the project-scoped evidence-anchor tag protections, and ping its external heartbeats on success. The same drift monitor must escalate the active mismatch to the `page` route only when it persists for more than 4 hours or overlaps any queued, requested, waiting, or in-progress `official.yml` run for the affected project. Second, an official-run health monitor at `.github/workflows/official-run-health-monitor.yml` must run at least every 5 minutes, use the `high-nonpage` route, combine approval-age monitoring, same-group queued-run loss detection, and post-tag non-success detection, and open or update the tracked recovery issue from live official target state rather than from wait state alone; an event-driven `workflow_run` path may feed this monitor, but a scheduled backstop is still required. For any official release identity that selected `github:official` and completed within the prior 24 hours, this same monitor must also compare the live GitHub Release asset set against durable evidence and raise `high-nonpage` if any manifest-selected asset or the release title/prerelease state drifts. `.github/workflows/control-plane-post-tag-failure.yml` is a narrowly scoped helper workflow for that same health-monitoring function: it may react to `official.yml` via `workflow_run`, may download only `tag-reservation-result-<project-name>` as untrusted data, and may not perform any registry, tag, or release mutation. Third, an open-incident freshness monitor at `.github/workflows/open-incident-freshness-monitor.yml` must run at least every 6 hours, re-query every open `incident` ledger entry with `disposition` in `{open-before-publish, open-partial-publish}`, compare live GitHub Release and registry state against the stored `publishedTargets`, `unpublishedTargets`, `indeterminateTargets`, `tagState`, and `githubReleaseState`, verify that `owner`, `severity`, and `nextReviewAt` remain populated, and enforce severity-aware review cadence rather than a single aging ladder. Fourth, a 7-day operational audit at `.github/workflows/release-operational-audit.yml` must use the `high-nonpage` route when it finds discrepancies, enumerate protected `release/**` tags, enumerate GitHub Releases whose deterministic title matches `<project-name> v<version>`, confirm each one corresponds either to a completed official release or to an explicitly tracked burned, partially withdrawn, or fully withdrawn identity under the recovery policy, and, whenever `github:official` applies, compare the live GitHub Release asset set and prerelease/stable state against durable evidence rather than checking only for existence. That audit must record its outcome through a reviewed ledger update. The same audit obligation also triggers immediately after run expiry, reviewed manual orphan-tag deletion, burned-identity declaration, or any approval incident escalated beyond the normal waiting budget. Fifth, a daily governance-and-runbook freshness monitor at `.github/workflows/governance-and-runbook-freshness.yml` must use the `tracked-follow-up` route by default, escalate to `high-nonpage` when any attestation is already stale, verify that the newest emergency-cleanup governance attestation is not older than 30 days and that the newest withdrawal-runbook and registry-auth-rollback-runbook re-attestations are not older than 90 days, and ping its external heartbeats on success. The external dead-man's-switch contract is strict: use authenticated HTTPS heartbeats, configure dual independent checks for every `page` and `high-nonpage` monitor, configure at least one independent check plus a repository-side age watchdog for every `tracked-follow-up` monitor, and alert after the first missed interval for each configured check. Those thresholds intentionally allow one missed scheduled execution plus normal scheduler jitter before paging, rather than paging on the first slightly late run. In addition, at least once every 7 days an out-of-band alert-delivery canary must prove that the heartbeat providers can still deliver notifications to the intended route; heartbeat acceptance alone is insufficient. A second repository-side watchdog may alert on the age of the newest successful audit run, but it is only a supplement to the external heartbeat, not a replacement. Automation may prepare candidate `audit` ledger payloads, attach them to tracked issues, or open reviewed PRs, but no monitor may push `.github/release-recovery-ledger.jsonl` directly outside the break-glass path. Any `discrepancy-found` audit result that is not reconciled during the same audit must create or link a tracked follow-up issue before the audit is considered complete, must set `followUpStatus = required-open`, and must either be reconciled or escalated to the release-engineering owners within 24 hours. A later ledger entry may mark `followUpStatus = resolved` only after the discrepancy has been reconciled and the closing audit evidence is recorded.

## 8. Build Provenance

No official production release may go live until full provenance attestation is implemented for every official publish path enabled by this design. This is a machine-enforced workflow rule, not a documentation-only policy: `official.yml` includes the `require-provenance` gate described in Section 4, and `create-release-tag` plus every official publish job remain ineligible unless that gate succeeds.

Until that gate is satisfied for every enabled official target, the interim manifest-only state remains suitable only for pre-production rollout work and dry runs, and `require-provenance` must fail closed for production use. Full provenance attestation is considered implemented only when the protected control-plane branch contains checked-in attestation steps for every supported official publish path in this design, the language-matching official build job runs in release mode and emits the required build-side provenance inputs without holding attestation-signing privileges, the isolated language-matching attestation job runs with `id-token: write` plus `attestations: write`, consumes the immutable build output, generates the required GitHub attestation bundles for the manifest-selected subjects, and uploads `attestation-manifest.json` plus the `attestations/` bundle directory as the provenance sidecar artifact; the verifier-side signer mapping for every enabled language and target is checked in and validated by repository policy; `require-provenance` downloads the build output plus the attestation sidecar, validates them successfully, writes the final `artifact-evidence.json` and related durable evidence blobs to the protected durable evidence branch `refs/heads/release-evidence`, verifies the committed blobs by permalink, emits `artifact-evidence-url`, and repository policy fails any official workflow change that would allow a selected official target to publish without that successful verifier-owned evidence output. The `release-evidence` branch is part of the control plane: it must be protected against direct pushes, force-push, and deletion, accept writes only from the reviewed automation path plus the emergency-cleanup group, and retain immutable historical evidence paths under `.github/release-evidence/<project-name>/<version>/<source-commit>/runs/<workflow-run-id>-attempt-<workflow-run-attempt>/`.

The provenance contract itself is exact. The isolated attestation job, not the release-mode build job and not `require-provenance`, must produce GitHub artifact attestations for each published artifact using `actions/attest-build-provenance` or a reviewed successor that emits an equivalent DSSE-wrapped in-toto provenance statement. The attestation job must also generate `attestation-manifest.json` so the verifier consumes a deterministic list of expected bundle files and subject digests rather than inferring them from directory layout. `require-provenance` must then verify those attestation-job-generated bundles with `gh attestation verify` or an equivalent reviewed verifier that uses GitHub's documented trust root, and it must hard-fail unless verification proves all of the following claims for the exact manifest-selected artifact set: repository identity, repository owner identity, triggering ref, source commit SHA, the attestation-job `job_workflow_ref`, the workflow file SHA, and, when a GitHub environment was used and the verifier surfaces it, the environment name. For GitHub Releases, that exact manifest-selected published set includes the build-generated `SHA256SUMS` asset because it is uploaded as part of the final release surface. With `gh attestation verify`, satisfying that contract is a two-step protocol rather than a flag-only check: first run the verifier with explicit signer and repository constraints plus an explicit per-invocation timeout of at most 30 seconds, then parse the verified JSON output and attestation statement to assert the exact source SHA, `job_workflow_ref`, workflow SHA, and optional environment claim. A plain flag-only invocation is insufficient for this design because not all required claims are surfaced as dedicated CLI switches. Verification must also prove that every attested subject digest matches the corresponding `artifact-manifest.json` entry, and the total attestation-verification budget must fail closed within 10 minutes for the selected artifact set. This design treats immutable build output plus later isolated attestation generation and verification inside a separate trusted gate job as a GitHub-backed provenance gate, not as an isolated post-build signer, and therefore makes no SLSA L3 claim. Ecosystem-native proofs such as PyPI publish attestations or npm provenance may be emitted in addition to the GitHub attestation set, but they are supplemental evidence rather than replacements for this core gate.

`artifact-evidence.json` is the durable normalized record of that verification result. The evidence record must therefore capture the exact verified repository, ref, source SHA, attestation-job `job_workflow_ref`, workflow SHA, repository owner identity, verifier tool, attestation type, and optional verified environment, as defined in Section 6. A missing claim, mismatched digest, unverifiable signer, unreviewed successor verifier, or durable-copy write failure is a hard failure. OIDC trusted publishing proves workflow identity at publish time; provenance attestation binds that identity, the source ref, and the exact build output into evidence that consumers and operators can verify later.

## Summary of Key Design Properties

1. **PR speed maximized**: A JS-only PR never waits for the Windows C# build queue.
2. **Channel isolation**: `buddy.yml` publishes only to unofficial package registries. `official.yml` publishes only to production registries plus optional GitHub Releases (`github:official`), with prerelease or stable state derived from the resolved version. Neither channel requires the other to run first for registry delivery.
3. **Static conditional dispatch**: Because `uses:` paths must be static, both build and publish jobs use conditional `if:` guards instead of dynamic matrix dispatch to reusable workflows. Each ecosystem-destination pair has its own dedicated job.
4. **Tag isolation**: Official release identity uses `release/<project-name>/v<version>`. Buddy no longer writes repository tags or GitHub Releases in this repository, which keeps unofficial branch-selected workflow code out of the official release-asset namespace.
5. **Overwrite-safe release identities**: Buddy treats GPR package versions as immutable and never overwrites them. Official publishes are idempotent for the same release identity only when remote artifact identity matches the local build output, and they never rebind a stable release to a different tag or commit.
6. **Least-privilege security**: Workflow-level `permissions: {}` with per-job escalation; build jobs and reusable publish workflows must be called with `secrets: {}` and never with a non-empty `secrets:` map, while direct official publish jobs rely on explicit job permissions plus target-specific protected environments instead of a job-level `secrets:` map; shell input hardening applies to reusable workflows as well as entry workflows and forbids untrusted `${{ ... }}` expansions inside `run:` blocks while also banning unsafe workflow-command-file writes and dynamic shell execution; privileged official publish logic stays on the protected control-plane branch set (`main` plus eligible protected `release/*` branches); package-registry publish workflows receive `contents: read` plus only their registry-specific auth path; all four external production registries use the strongest currently documented GitHub Actions trusted-publishing selector model their providers support, including `NuGet/login@v1` for `NuGet.org`; the dedicated `production-tag-write-*` and `production-evidence-write-*` environments isolate the release-tag writer and release-evidence writer credentials from publish jobs; the protected GitHub deployment-branch policy remains the authoritative branch restriction for all official registries; project-scoped protected environments with mandatory required-reviewer gates, exact deployment branch names, and repository-ruleset verification remain the authoritative branch scope; protected `.github/workflows/**`, official source branches, and official `release/**` tags.
7. **Terminal completeness checks**: `buddy.yml` and `official.yml` both end with a `release-complete` gate. For official releases, that gate depends on a separate `confirm-publish-state` job that re-queries live remote state with retry and backoff before declaring the selected target set complete. A green workflow run without that terminal proof is not considered complete.
8. **Durable recovery evidence and proactive operations**: Official provenance is not complete until `require-provenance` writes an immutable evidence record to the protected `release-evidence` branch, approval waiting states are monitored before the 30-day deadline, the 6-hour open-incident freshness monitor revalidates open ledger entries against live remote state, and the 7-day operational audit plus the monitor suite are themselves watched by external dead-man's-switch heartbeats.
