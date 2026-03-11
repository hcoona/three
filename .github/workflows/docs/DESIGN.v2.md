# GitHub Workflows Design (v2.6)

This document describes the GitHub Actions workflow architecture for the `three` monorepo.

> **Scope constraint:** Each project maps to exactly one language ecosystem. Multi-language projects (e.g., a C# library with a companion npm package) are out of scope and must be split into separate project directories with separate `release.json` files.

> **Release-unit constraint:** Each `buddy.yml` or `official.yml` run releases exactly one project. Coordinated multi-project release orchestration is out of scope for this design.

## 1. Architecture Overview (Shared Execution Layer)

To avoid duplicating build and deploy logic across three entry workflows, the design adopts reusable workflows as the shared execution layer. Each entry workflow independently invokes the same set of reusable workflows — there is no single dispatching hub.

**Entry layer (Entry Workflows):** `ci.yml`, `buddy.yml`, `official.yml`

**Execution layer (Reusable Workflows under `.github/workflows/`):**

- `_build-test-csharp.yml` — runs on `windows-latest`
- `_build-test-python.yml` — runs on `ubuntu-latest`
- `_build-test-jsts.yml` — runs on `ubuntu-latest`
- `_build-test-ruby.yml` — runs on `ubuntu-latest`
- `_publish-nuget.yml` — publishes `.nupkg` to a single NuGet feed (parameterized by `feed-url`)
- `_publish-npm.yml` — publishes npm tarball to a single npm registry (parameterized by `registry`)
- `_publish-pypi.yml` — publishes wheel/sdist to PyPI (official only; GitHub Packages does not offer a PyPI-compatible feed)
- `_publish-rubygems.yml` — publishes gem to a single RubyGems host (parameterized by `host`)
- `_publish-github.yml` — runs on `ubuntu-latest` and publishes downloadable assets to stable GitHub Releases for the official channel

The split axis is **ecosystem (tooling)**, not destination. Publishing a NuGet package to GPR vs NuGet.org uses the same tool (`dotnet nuget push`) with a different `--source` URL; the same applies to npm, RubyGems, etc. Each reusable workflow encapsulates one tool and one package format, accepting the destination as an input parameter. Each call publishes to **exactly one** destination — publishing to both GPR and an official registry requires two separate caller jobs (see Section 3, job 4). The caller (buddy or official) controls which destination and auth method to use.

Two intentional exceptions are still ecosystem-shaped rather than contradictory to that rule. `_publish-pypi.yml` has no unofficial counterpart because GitHub Packages does not expose a PyPI-compatible feed, and `_publish-github.yml` is destination-specific because GitHub Releases is not a package-registry tool surface.

For `official.yml`, the protected control-plane branch set is the default branch `main` plus eligible protected maintenance branches `release/<project-name>/v<release-line>`, where `<release-line>` is the numeric release line such as `1.2.x` without a leading `v`. The branch selected in the `workflow_dispatch` UI supplies both the trusted workflow/control-plane code and the release payload source for that run. Official release tags are derived and created by the workflow itself from the selected protected source ref after validation succeeds; they are not external workflow inputs.

Trusted control-plane code follows the same rule. For `official.yml`, the caller workflow, every reusable workflow, every composite action, and every helper script that performs privileged release gating or publishing come from the same dispatch-selected protected control-plane branch. Because official runs are allowed only from that protected branch set, there is no separate historical tagged-source workspace in this design.

**Secrets:**

- **Build-test workflows** have no secret requirements. Callers should pass secrets explicitly: `secrets: {}` (empty). This limits the blast radius if a compromised dependency or malicious test reads the environment during build/test execution.
- **Publish workflows** should also default to `secrets: {}`. Prefer the automatic `GITHUB_TOKEN`, caller-granted `permissions`, and trusted publishing where the target supports it. When a destination requires an explicit credential, the caller must pass only that named secret; blanket `secrets: inherit` is prohibited in this design.

Permissions are inherited automatically: a reusable workflow receives the caller job's `permissions` grants as long as the reusable workflow itself does **not** declare its own `permissions` block. This is what allows the same `_publish-nuget.yml` to operate under `packages: write` when called from `buddy.yml`, while official NuGet publication instead supplies the explicit `NUGET_API_KEY` secret from `production-<project-name>` and requires only `contents: read`.

> **Important constraint:** Reusable workflows must NOT declare their own `permissions:` block. If they do, only the scopes explicitly declared there remain eligible, and each of those scopes is still capped by the caller job's grant. Undeclared scopes become `none` even if the caller granted them. For example, if a reusable workflow declares `permissions: { id-token: write }` and the caller grants only `packages: write`, the minted token will have both `id-token: none` and `packages: none`, causing silent runtime failures. Keep all `permissions:` declarations in the entry workflows only.
>
> This rule must be lint-enforced in repository policy. In addition to `actionlint`, the repository must run a custom `hk` validation that fails if any reusable workflow under `.github/workflows/_*.yml` declares either a workflow-level or job-level `permissions:` block.

> **Important constraint:** Shell input hardening applies to both entry workflows and reusable workflows. No `run:` step may interpolate `${{ inputs.* }}`, `${{ github.event.inputs.* }}`, `${{ github.* }}`, `${{ needs.*.outputs.* }}`, `${{ env.* }}` values derived from those contexts, or any other untrusted expression directly into shell source. In practice, shell source must not contain `${{ ... }}` expansions for untrusted values at all. All such values must first be mapped under `env:` and then referenced only as quoted shell variables such as `"$PROJECT_NAME"`.

> **Important constraint:** Mapping untrusted values through `env:` is necessary but not sufficient. Shell steps must also ban `eval`, `bash -c`, PowerShell `Invoke-Expression`, and other dynamic command construction with untrusted data; must write `GITHUB_ENV`, `GITHUB_OUTPUT`, `GITHUB_PATH`, and `GITHUB_STEP_SUMMARY` only through helpers that reject embedded newlines and delimiter injection; and must not derive here-doc delimiters or workflow-command file syntax from untrusted values. Repository policy must lint these sinks in addition to checking for direct `${{ ... }}` interpolation in `run:` blocks. The allowed workflow-command-file writers are not implicit: `hk.pkl` must define an explicit reviewed allowlist of helper entrypoints, and any direct command-file write outside that allowlist is a hard failure. Those helper entrypoints must live only under trusted control-plane paths already covered by `CODEOWNERS` and the `infra` inventory.

> **Important constraint:** The same shell-hardening rule also applies to local composite actions under `.github/actions/**`. Any value received through a composite action's `with:` inputs is still untrusted at the point where that composite action consumes it. Composite-action `run:` steps must therefore map those values through `env:` before use, must avoid direct `${{ inputs.* }}` interpolation in shell source, and must be covered by the same repository-policy linting for unsafe workflow-command-file writes and dynamic shell execution.

**Project-scoped production environments:** Official releases use three protected GitHub environments per releasable project. `production-<project-name>` is the approval gate and OIDC environment claim for official publish jobs. `production-tag-write-<project-name>` is a separate tag-reservation environment used only by `create-release-tag`; it carries the release-tag writer App private key and must never be referenced by package-registry, provenance, or GitHub Release publish jobs. `production-evidence-write-<project-name>` is a separate evidence-persistence environment used only by `require-provenance`; it carries the release-evidence writer App private key and must never be referenced by package-registry, GitHub Release, or tag-reservation jobs. All three environments must use the same exact deployment branch set and the same required-reviewer protection profile. This keeps both write credentials out of publish jobs while preserving explicit per-project branch boundaries.

**Permissions model:** Every entry workflow declares `permissions: {}` at workflow level. Individual jobs then request only the scopes they need (principle of least privilege). Key scopes:

| Job kind                                         | Required `permissions` |
| ------------------------------------------------ | ---------------------- |
| Read repository metadata / releases              | `contents: read`       |
| Read pull request file lists                     | `pull-requests: read`  |
| Read-only checkout and trusted helper code       | `contents: read`       |
| Push protected official release tags             | `contents: read` on the job `GITHUB_TOKEN`; the actual protected-tag write uses a dedicated GitHub App installation token |
| Create or update official GitHub Releases        | `contents: write`      |
| Publish to GitHub Packages                       | `packages: write`      |
| Publish to NuGet.org with explicit API key       | `contents: read`       |
| Generate build attestations                      | `id-token: write`, `attestations: write` |
| Trusted-publisher publish to official registries | `id-token: write`      |

Official registry auth is intentionally split by documented provider capability. `npmjs`, `PyPI`, and `RubyGems.org` use GitHub Actions trusted publishing. `NuGet.org` does not currently provide a documented equivalent in this design, so official NuGet publication uses a narrowly scoped API key secret stored only in the project-scoped `production-<project-name>` environment. That NuGet.org key scope must be limited at the provider side to the exact package ID or project-specific package pattern for that single project; repository-wide or organization-wide NuGet push keys are unsupported. npmjs may auto-generate provenance for eligible public packages when trusted publishing is used. GPR feeds use `GITHUB_TOKEN` with `packages: write` instead.

Because GitHub's documented attestation generation examples require both `id-token: write` and `attestations: write`, every official `clean-build` caller job that passes `require-provenance: true` must request all three scopes `contents: read`, `id-token: write`, and `attestations: write`. `contents: read` alone is insufficient for Section 8's provenance gate.

GitHub Releases management does not expose a narrower `releases: write` permission in GitHub Actions. `contents: write` is therefore the minimum available scope for `_publish-github.yml` in this design.

The GitHub App installation tokens used by `create-release-tag` and `require-provenance` are repository-scoped rather than ref-scoped. Ref-level isolation for `refs/tags/release/**` and `refs/heads/release-evidence` therefore comes from repository rulesets plus the dedicated protected environments, not from a narrower App permission shape.

> **Note:** With `permissions: {}` at workflow level, jobs that run `actions/checkout` or read GitHub release metadata must explicitly declare at least `permissions: { contents: read }`. In this design, no job reads GitHub environment metadata through the job `GITHUB_TOKEN`; `preflight-check` and CI policy checks mint a dedicated read-only GitHub App installation token instead and may therefore keep `permissions: {}` unless future changes add other repository reads. Build jobs included — without the required scope, the zero-permission `GITHUB_TOKEN` cannot clone the repository or read the repository metadata that release gating depends on. If a future job reads environment metadata through the job `GITHUB_TOKEN`, that specific job must request `permissions: { environments: read }` explicitly.

**Repository protection model:** This design uses GitHub repository rulesets for protected branches and protected tags. Legacy branch-protection endpoints and compatibility shims are out of scope before implementation starts. Workflow preflight and policy validation query the Environments API plus the Repository Rulesets API only.

**Concurrency policy:** Each entry workflow defines a `concurrency:` group to prevent resource races:

- `ci.yml`: `group: ci-${{ github.ref }}`, `cancel-in-progress: true`
- `buddy.yml`: `group: buddy::${{ github.ref }}::${{ inputs.project-name }}`, `cancel-in-progress: false`
- `official.yml`: `group: official::${{ github.ref }}::${{ inputs.project-name }}`, `cancel-in-progress: false`

The `::` separator is intentional because it cannot appear in either `github.ref` or a valid `project-name`, which prevents ambiguous concatenation such as `feat` + `a-b` colliding with `feat-a` + `b`. GitHub Actions still compares concurrency groups case-insensitively, so releasable `project-name` values must also be unique under ASCII lowercase normalization across the repository. With `cancel-in-progress: false`, an in-progress run is preserved. GitHub Actions may still replace an older queued run with a newer queued run for the same concurrency group, so operators should not stack multiple fresh dispatches for the same buddy project/ref combination or the same official project/ref combination and assume each queued run will execute. Before issuing any fresh dispatch for a concurrency group that already has queued or in-progress runs, operators must inspect the existing runs, then either cancel stale queued runs or wait for the in-progress run to settle rather than assuming the queue will preserve intent.

Because workflow-level `concurrency.group` is computed before any job can normalize `github.ref`, this design also forbids mixed-case release workflow source refs. The official protected control-plane branch set is already lowercase-only by construction. Buddy dispatches must likewise use lowercase branch names only; mixed-case buddy source refs are unsupported because case-distinct refs would alias the same concurrency group.

The separator guarantee applies only after `project-name` validation. Because `buddy.yml` and `official.yml` compute their concurrency keys before `resolve-context` runs, an authorized dispatcher can still intentionally collide with another queued run by reusing the same valid `project-name`. Concurrency is therefore an operational coordination control, not a security boundary. A dispatch from a not-yet-eligible branch or otherwise invalid configuration still occupies its concurrency group until the run settles; operators must cancel the known-bad run before redispatching the corrected configuration for the same group.

For `official.yml`, the concurrency key intentionally serializes per `(ref, project-name)` pair rather than across all protected branches. This means the same project may still have concurrent official runs from different protected branches such as `main` and `release/<project-name>/v<release-line>`. That is acceptable only when those runs are expected to produce distinct release identities. Operators should not intentionally launch concurrent official runs for the same project from different protected branches unless that distinction is understood; GitHub Release identity conflict checks remain authoritative if those runs overlap.

**Job timeouts:** Every job must declare `timeout-minutes`, and workflow linting enforced through `hk`/`actionlint` should fail if any job omits it. Recommended defaults: `preflight-check`, resolution jobs, and static-analysis jobs `15`; Ubuntu build jobs `30`; Windows C# build jobs `45` because hosted Windows runners have materially higher startup and .NET restore/build/test overhead than Ubuntu runners; publish jobs `15`; `confirm-publish-state` `15` because it performs bounded remote rechecks in parallel across the selected destinations and the design does not support a serial fallback; lightweight tag-management jobs `10`; and terminal gate jobs `ci-passed` and `release-complete` `10`. Some YAML snippets below omit `timeout-minutes` only for brevity; concrete workflow files must still declare it.

**Action pinning:** All external actions, including GitHub-maintained actions under the `actions/` namespace, must be pinned to full commit SHA. Any `docker://` image reference must be pinned to an immutable digest such as `@sha256:...`. Local composite actions under `.github/actions/**` are sourced from the checked-out protected workspace, must be explicitly covered by `CODEOWNERS`, and are governed by the same branch protection and `CODEOWNERS` review as the caller workflow rather than by a separate pin. Use Renovate or Dependabot to manage external action updates:

```yaml
uses: dorny/paths-filter@de90cc6ed7cd597cb74b84a7e832ce805e3c7b15 # v3.0.2
```

The repository's dependency-update automation must cover `.github/workflows/**` so pinned SHAs are refreshed intentionally rather than drifting indefinitely.

Repository policy must run both `actionlint` and `zizmor` in strict mode through `hk`. `actionlint` covers workflow syntax and common semantics. `zizmor` is the authoritative enforcement layer for full-SHA action pinning, digest-pinned `docker://` references, prohibiting `secrets: inherit`, and rejecting `on: pull_request_target` or `on: workflow_run` in workflows covered by this design. `hk.pkl` must also define the repository-specific policy checks that `actionlint` and `zizmor` do not provide: no reusable-workflow `permissions:` blocks, no unsafe shell sinks or unreviewed workflow-command-file writers in `.github/workflows/**`, `.github/actions/**`, and `eng/scripts/**`, exact `infra` inventory coverage, and the MISE backend allowlist plus reviewed-exception registry described below.

**Tool lock enforcement:** `mise.lock` is required repository state, not optional convenience metadata. `hk check --all` must fail when the repository root lacks `mise.lock`, and any intentional toolchain update must regenerate the lockfile with `mise lock` in the same change that modifies `mise.toml`. Gate jobs that rely on `jq`, including `ci-passed`, `confirm-publish-state`, and both `release-complete` jobs, must obtain `jq` from the repository-managed `mise` toolchain rather than from the runner image, so `jq` is part of the reviewed locked toolchain for this design. The locked `jq` version must be at least `1.6`. The reviewed MISE backend policy must also be machine-enforced in `hk.pkl`: by default, only backends whose lockfile entries include immutable upstream digests are allowed in official build or publish paths. Any version-string-only backend must be listed explicitly in a reviewed exception registry that records the exact tool name, backend name, rationale, owner, and review date; undeclared exceptions are hard failures.

## 2. `ci.yml` — PR Validation (Targeted Concurrency, Shift-Left)

**Trigger:** `on: pull_request`

Because `pull_request` runs evaluate the PR merge commit, local workflow files, composite actions, and helper scripts execute from the PR-provided tree. The repository must therefore require explicit approval before workflows from forked pull requests may run. Without that repository setting, `ci-passed` is not a trusted release-gating signal for fork PRs.

CI does not build everything on every PR. It uses path filtering (`dorny/paths-filter`, SHA-pinned) to run only the affected language test suites.

**Jobs:**

1. **`static-analysis`**:
    - `permissions: { contents: read }`
    - Runs `jdx/hk` (`hk check --all`) on an Ubuntu runner. HK auto-detects file types from its configuration (`hk.pkl`), serving as the first gate for formatting and linting failures.
    - The same whole-repo analysis must also enforce the single-language project scope for releasable projects: repository policy must hard-fail if any candidate releasable project root resolves to more than one workflow language.
    - This job is intentionally unconditional whole-repo analysis and must not acquire an `if:` guard without a matching `ci-passed` contract change. If HK wall time ever becomes a material bottleneck, replace it with an explicit path-aware design rather than silently making the job skippable.

2. **`detect-changes`**: Uses `dorny/paths-filter` to classify modified files:
    - `permissions: { pull-requests: read }`
    - `csharp`: `['**/*.cs', '**/*.csproj', 'global.json', 'Directory.*.props', 'nuget.config', '**/NuGet.Config', '**/*.targets', '**/packages.lock.json']`
    - `python`: `['**/*.py', 'pyproject.toml', 'uv.lock']`
    - `jsts`: `['**/*.ts', '**/*.js', '**/*.mjs', '**/*.cjs', 'package.json', 'pnpm-workspace.yaml', 'pnpm-lock.yaml', 'biome.jsonc', 'tsconfig*.json']`
    - `ruby`: `['**/*.rb', '**/*.gemspec', 'Gemfile', 'Gemfile.lock']`
    - `infra`: `['.github/workflows/**', '.github/actions/**', '.github/CODEOWNERS', '.github/official-caller-refs.json', '.github/publish-trust-inventory.json', '.github/planned-change-windows.json', '.github/release-recovery-ledger.jsonl', 'eng/scripts/**', '**/release.json', '**/version.json', 'mise.toml', 'mise.lock', 'hk.pkl', 'PklProject', 'PklProject.deps.json', 'global.json', 'nuget.config', '**/NuGet.Config', 'Directory.*.props', '**/*.targets', 'package.json', 'pyproject.toml', 'biome.jsonc', 'pnpm-workspace.yaml', 'pnpm-lock.yaml', '.npmrc', '**/.npmrc', 'uv.lock', 'Gemfile.lock', '**/packages.lock.json', 'Directory.Packages.props']`

    When `infra` changes are detected, all language test suites are triggered regardless of other filters.

    The `infra` filter is the authoritative CI-maintained inventory of trust-bearing control-plane files for release validation. Any reviewed change that adds, removes, or renames trusted helper code or shared dependency-control files consumed by official build or release jobs must update this filter in the same PR; there is no separate compatibility fallback to a broader implicit catch-all.

    Repository policy must machine-enforce that invariant rather than relying on reviewer memory alone. In addition to `dorny/paths-filter`, `hk check --all` must run a control-plane inventory check that recomputes the expected `infra` filter members from the trusted-file classes in this design and hard-fails on any missing entry, stale entry, or trusted file change that would not set `infra = true`.

    > **Scaling note:** The current filters operate at language level (`**/*.cs` triggers all C# builds). As the monorepo grows past ~10 projects per language, this should evolve to per-project granularity using affected-project detection from `eng/scripts/find_project_path.py`.

3. **`repo-policy-check`**:
    - `permissions: {}`
    - Runs without checkout. For public repositories, this job may use unauthenticated REST reads or the default `GITHUB_TOKEN` to validate the existence and basic metadata of repository environments and rulesets. For private repositories, or for any check that needs metadata unavailable to public reads, it must mint the same dedicated read-only GitHub App installation token shape used by `preflight-check`.
    - Verifies that repository Actions settings require explicit approval before workflows from fork pull requests may run, and specifically requires the strongest available mode equivalent to "all outside collaborators". Modes limited to first-time contributors or first-time GitHub users are hard failures because they allow returning external contributors to bypass approval.
    - The check must validate the full API response schema, not just the HTTP status code. Unknown enums, missing fields, empty bodies, or any response-shape drift are hard failures.
    - Any minted App token must be masked before first use.

4. **`trusted-release-inventory`**:
    - `needs: [detect-changes]`
    - `permissions: { contents: read }`
    - Conditional: `if: needs.detect-changes.outputs.infra == 'true'`
    - Checks out the PR merge commit with `persist-credentials: false` and runs the repository-side drift check for `.github/publish-trust-inventory.json`. This job must recompute the post-change trust-bearing state from the checked-in control-plane files, using `.github/official-caller-refs.json` as the authoritative repository-side source for the normalized fully qualified `allowedCallerRefs` set, and compare the exact normalized values of `schemaVersion`, `entryWorkflowPath`, `allowedCallerRefs`, `publishWorkflowPaths`, `targetEnvironments`, `targetAuthMechanisms`, `targetTrustedPublisherSelectors`, and `targetOidcAudiences`. Any missing inventory update, stale mapping, malformed schema, mismatched environment mapping, mismatched auth mechanism, mismatched trusted-publisher selector, mismatched OIDC audience mapping, or `schemaVersion != 1` is a hard failure.
    - The same job must also validate `.github/planned-change-windows.json` against the Section 4 schema whenever that file changes, including `schemaVersion`, the strict key whitelists, unique `windowId` enforcement, operation/status closed sets, RFC 3339 UTC timestamps, immutable `openedAt`, required `phaseStartedAt`, per-operation and per-status maximum window duration, conditional `linkedIncidentId`, `closedAt`, and `cooldownUntil` rules, and the mandatory 48-hour cooldown before the same `{operation, projectName, releaseLine}` tuple may open a new active window.
    - The same job must also validate `.github/release-recovery-ledger.jsonl` against the Section 7 schema whenever that file changes, including `schemaVersion`, strict key whitelists, `incidentId` UUID requirements, closed sets, conditional `runAttempt`, `workflowRunUrl`, `evidenceUrl`, `closedAt`, `automationId`, and `scriptVersion` rules, canonical target ordering, the `selectedTargets` / `publishedTargets` / `pendingTargets` partition invariants, the `withdrawnTargets` subset invariants, the disposition-specific non-empty-set rules for `open-before-publish`, `open-partial-publish`, `recovered`, `abandoned-after-partial-publish`, and `fully-withdrawn`, and the presence-and-absence rules for the hold-window evidence fields used in destructive stable-release recovery.

5. **`test-csharp` / `test-python` / `test-jsts` / `test-ruby`** (run in parallel):
    - `needs: [detect-changes, static-analysis]`
    - `permissions: { contents: read }`
    - Conditional: e.g. `if: needs.detect-changes.outputs.csharp == 'true' || needs.detect-changes.outputs.infra == 'true'`
    - Each calls its corresponding reusable workflow in `build-scope: ci` mode. In this mode the caller omits `project-path` and `project-name`, the reusable workflow executes the language-wide CI suite for the current checkout, and `require-provenance` must remain `false`. C# uses `windows-latest`; the others use `ubuntu-latest`.

6. **`ci-passed`** (final gate job):
    - `if: always()`
    - `permissions: {}`
    - `needs: [detect-changes, static-analysis, repo-policy-check, trusted-release-inventory, test-csharp, test-python, test-jsts, test-ruby]`
    - Asserts all required checks either passed or were legitimately skipped. Including `detect-changes`, `static-analysis`, and `trusted-release-inventory` in `needs` ensures their failures block the gate — if `detect-changes` fails, all downstream conditional jobs are auto-skipped with `result: "skipped"`, and without `detect-changes` in `needs`, `ci-passed` would see only `"success"` and `"skipped"` results and falsely pass. The gate must also re-derive which language suites were required from `detect-changes.outputs` so a drifted `if:` condition on a `test-*` job cannot silently convert a required suite into a tolerated skip, and it must hard-fail if any expected `detect-changes` output key is missing or not one of `"true"` / `"false"`.
    - `detect-changes`, `static-analysis`, and `repo-policy-check` are intentionally unconditional. None of them may acquire an `if:` guard without a matching `ci-passed` contract change.

    ```yaml
    ci-passed:
        if: always()
        permissions: {}
        needs: [detect-changes, static-analysis, repo-policy-check, trusted-release-inventory, test-csharp, test-python, test-jsts, test-ruby]
        runs-on: ubuntu-latest
        steps:
            - name: Assert all required checks passed or were legitimately skipped
              env:
                  NEEDS_JSON: ${{ toJson(needs) }}
              run: |
                  jq -n -e '
                      (env.NEEDS_JSON | fromjson) as $n
                      | ($n["detect-changes"].outputs) as $dc
                      | ($n["detect-changes"].result == "success")
                      and ($n["static-analysis"].result == "success")
                      and ($n["repo-policy-check"].result == "success")
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
    - **Runner and tooling:** Runs on `ubuntu-latest`. Requires `mise install` to bootstrap Python (for `eng/scripts/find_project_path.py`) and the .NET SDK. The `nbgv-python` adapter is sourced from the current checked-out repository workspace, not from an external package index, so trusted version resolution tracks the selected source ref rather than an out-of-band registry artifact. The `mise.toml` and `mise.lock` at the repo root pin tool versions and, where supported by the selected MISE backends, the exact download digests. Any tool used in an official build or publish path must use a digest-pinning backend unless the exact tool/backend pair is listed in the reviewed `hk.pkl` exception registry with rationale and owner; silent version-string-only backends are unsupported for the official release path. The job must hard-fail if `mise.lock` is absent, and should restore a tool cache keyed by both files before invoking `mise install`.
    - **Input validation:** As the first step (before any checkout or git operation), validate `project-name` with a full-string match against the character class `[A-Za-z0-9][A-Za-z0-9._-]*`, reject any occurrence of the substring `..`, reject trailing `.`, and reject any name that ends with `.lock`. Reject invalid names with a clear error. This is stricter than the current helper script because leading option-like names are intentionally out of scope for releaseable project identities and the name must remain compatible with Git ref naming and `git check-ref-format`.
    - **Source ref policy:** Buddy intentionally permits dispatch from non-default branches. No ancestry check against `main` or any release branch is performed in this workflow.
    - Runs `eng/scripts/find_project_path.py` to determine the project path and the workflow language. `project-name` is case-sensitive and must resolve to exactly one project in the repository. The resolution step must emit exactly one of `{csharp, python, jsts, ruby}` for `language`; no match, ambiguous match, unsupported language, or resolver error is a hard failure.
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
    - Runs `hk check <project-path>` scoped to the resolved project path. HK receives the project path directly and discovers applicable files under that path according to `hk.pkl`; this design does not pre-enumerate file names in shell.
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
            version: ${{ needs.resolve-context.outputs.version }}
            host: https://rubygems.pkg.github.com/hcoona
        secrets: {}
    ```

    - The `if: always() && !cancelled() && !failure()` guard ensures the publish jobs run despite the three skipped build jobs in the `needs` chain. This condition is safe because skipped jobs are treated as neither failure nor cancellation.
    - Including `static-analysis` directly in each publish job's `needs` keeps the gate explicit and allows the job to assert `needs.static-analysis.result == 'success'` directly rather than relying on transitive failure propagation alone.
    - For GPR targets, auth uses `GITHUB_TOKEN` with `packages: write`. No OIDC is needed.
    - All buddy publish jobs use `secrets: {}`. No repository, organization, or environment secrets are forwarded by default.
    - Each publish step uses idempotent publish logic. Duplicate-version outcomes (`409`, `422`, or tool-equivalent "already exists" responses) count as success only after the workflow verifies that the already-published remote artifact set matches the local artifact set and expected digests. Authentication failures, authorization failures, malformed artifacts, and upstream `5xx` errors remain hard failures. This design intentionally does not retry upstream `5xx` failures inside a single run; operator recovery happens by re-running the workflow.

5. **`release-complete`**:
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, publish-nuget-gpr, publish-npm-gpr, publish-rubygems-gpr]`
    - `if: always()`
    - `permissions: {}`
    - Performs the terminal correctness check for buddy. It must first assert that `resolve-context.result == "success"` and `static-analysis.result == "success"`. It must then parse `targets` as JSON, assert that the filtered target set is non-empty, map that set to the exact publish jobs `{nuget:gpr -> publish-nuget-gpr, npm:gpr -> publish-npm-gpr, rubygems:gpr -> publish-rubygems-gpr}`, and assert that every selected target finished with `result == "success"` and a valid `publish-result` output in `{new-publish, no-op}`.
    - It must also assert that every non-selected publish job finished with `result == "skipped"`.
    - It must also assert that the single language-matching build job finished with `result == "success"`; the three non-matching build jobs must be `result == "skipped"`.
    - The normative jq skeleton is:

    ```yaml
    - name: Assert buddy release completeness
      env:
          NEEDS_JSON: ${{ toJson(needs) }}
      run: |
          jq -n -e '
              (env.NEEDS_JSON | fromjson) as $n
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

**Language expansion checklist:** Adding a new supported language is an atomic control-plane change. The same reviewed PR must update at minimum: (1) the reusable build workflow inventory in Section 1 and the new reusable build workflow reference itself, (2) `eng/scripts/find_project_path.py` and any other resolver logic that discovers the workflow language from repository contents, (3) the language-specific version validator script plus the validator-selection logic in both `buddy.yml` and `official.yml` `resolve-context`, including any explicit release-channel flags such as Python's `--channel buddy` / `--channel official`, (4) the `detect-changes` filters, the new `test-<language>` job, and the `ci-passed` contract in `ci.yml`, (5) every buddy publish-job `if:` guard and `needs:` list, (6) every official publish-job block and its `needs:` wiring, (7) the official `require-provenance` and `create-release-tag` gates, (8) the `buildJobs` / `publishJobs` maps and `needs:` lists in both `release-complete` jq skeletons, (9) `confirm-publish-state.needs:` and its live-confirmation mapping for every new build or publish job introduced by that language, (10) the language-aware target-validation and channel-filtering rules in both `resolve-context` jobs, (11) the Section 5 language-to-target matrix and any Section 7 canonical target ordering or ledger closed sets affected by new official targets, (12) the Section 6 reusable-workflow I/O contract tables and provenance-evidence contract, and (13) the publish-trust-inventory mappings and CI comparison scope for any new official targets. Partial updates are unsupported.

## 4. `official.yml` — Production Release

**Important:** `buddy.yml` and `official.yml` are **independent release channels**, not a sequential promotion pipeline. Buddy publishes only to unofficial package registries. Official publishes to production registries and optional stable GitHub Releases via `github:official`. A buddy run is NOT a prerequisite for an official run.

**Trigger:** `on: workflow_dispatch` only (no `push: tags:` trigger — `workflow_dispatch` is sufficient and avoids the bootstrapping-window risk where a tag trigger is live before the tag protection ruleset is verified).

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

**Caller ref policy:** In `workflow_dispatch`, the branch selected in the GitHub UI determines which revision of `official.yml`, its reusable workflows, its trusted helper code, and its release payload source executes. Under this design, that caller ref must be one of the protected control-plane branches only: `main` or an eligible protected maintenance branch `release/<project-name>/v<release-line>`, where `<release-line>` is the numeric series such as `1.2.x`.

**Release identity mechanism:** `official.yml` does not accept a pre-existing release tag as input. Instead, it resolves the project version from the dispatch-selected protected source ref, derives the official release tag `release/<project-name>/v<version>` from that result, and creates that protected release tag inside the workflow only after approval has been granted in the project's dedicated protected tag-write environment `production-tag-write-<project-name>`. This keeps official and buddy symmetric as `workflow_dispatch` entry points while preserving a dedicated immutable official release-identity namespace.

**Branch and version mechanism:** Official release eligibility is decided from the dispatch-selected protected source ref itself. The workflow resolves `project-name`, `language`, `project-path`, and NBGV version from that ref, validates the version semantically, derives the release line from the resolved version, captures a single comparison snapshot of `origin/main` at the start of `resolve-context`, and then checks that the selected protected branch matches that release line against that frozen snapshot. If the resolved release line matches the frozen `main` release line, the caller ref must be `main`. If it differs, the caller ref must be the exact protected maintenance branch `release/<project-name>/v<release-line>`. A maintenance branch is not eligible for official release until `main` has already been bumped to a different release line; creating `release/<project-name>/v<release-line>` before `main` moves off that same line is unsupported and must fail with operator guidance to bump `main` first. Only after those checks succeed may the workflow derive and create the protected official release tag `release/<project-name>/v<version>`. Because `workflow_dispatch` fixes the release payload commit at dispatch time while the frozen `origin/main` comparison snapshot is captured when `resolve-context` starts, a later release-line-changing push to `main` may cause a safe false failure rather than a silent misrelease; operators must redispatch rather than overriding that mismatch.

Official releases may publish valid prerelease versions from the protected control-plane branch set. Prerelease status does not relax branch eligibility, protection requirements, or release-tag derivation; it only changes npm dist-tag selection under the explicit community-convention rules defined below.

**Maintenance branch policy:** A maintenance branch exists only for release lines that release engineering explicitly supports. It is created by release engineering from the first official release on that line, or immediately before the first hotfix on that line, using the exact name `release/<project-name>/v<release-line>`. Before that branch is used for any official release, it must receive the same protection profile as `main`: required PR review, required `ci-passed`, no direct pushes, and no force-pushes. The `main` release line is the base release line computed from the frozen `origin/main` comparison snapshot captured at the start of `resolve-context` for that run. If a dispatch-selected version resolves to any different release line and the matching maintenance branch does not exist, `official.yml` must fail with a clear error that prints the exact expected branch name and instructs the operator to either create and protect that maintenance branch or dispatch from the correct protected branch for that release line. Retired release lines are no longer eligible for official publication.

**Release-line derivation:** This design uses one release-line rule across all supported ecosystems. The input to this rule must already be a canonical normalized version string; validators are part of the contract rather than an implementation detail. First, keep only the leading numeric release segment of the normalized version string and discard everything from the first prerelease, postrelease, devrelease, local, or repository-specific suffix onward. Concretely: for SemVer-style versions, discard everything from the first `-`; official release versions with build metadata (`+...`) are unsupported and must have been rejected earlier by validation; for PEP 440-style versions, keep only the leading release segment before any `a`, `b`, `rc`, `.post`, or `.dev` suffix, and reject both epoch markers (`!`) and local version identifiers (`+...`) before this rule is applied on the official release path; for the repository's Ruby subset, keep only the leading `MAJOR.MINOR.PATCH` numeric segment before any dotted suffix containing letters. Then read at most the first two numeric components, zero-pad a missing minor component to `0`, and render the release line as `<major>.<minor>.x` without a leading `v`. Any third and later numeric components are ignored for release-line selection. Branch names, tags, and other identifiers that require a `v` prefix add that literal `v` separately. Examples: `1 -> 1.0.x`, `1.1 -> 1.1.x`, `1.2.3` (SemVer) `-> 1.2.x`, `1.2.3rc1` (PEP 440) `-> 1.2.x`, `1.2.0-dev.1` (SemVer) `-> 1.2.x`, `1.2.post1` (PEP 440) `-> 1.2.x`.

**Maintenance branch onboarding order:** Because implementation has not started yet, the onboarding procedure is defined strictly rather than retrofitted for backward compatibility. Branch eligibility is enforced on the GitHub side through the protected project-scoped environments `production-<project-name>`, `production-tag-write-<project-name>`, and `production-evidence-write-<project-name>` plus the checked-in caller-ref registry and publish trust inventory. Registry-side trusted-publisher configuration is tracked separately in the inventory but does not receive per-branch updates because this design does not rely on provider-side exact branch or tag binding. The safe order is therefore: (1) land a reviewed change on `main` that bumps the governing version files so the frozen `main` release line moves off the maintenance line that will remain supported, (2) create the maintenance branch from the exact commit that should continue that older line, (3) apply the full protection profile and required code-owner review, (4) create a machine-readable planned-change window entry in `.github/planned-change-windows.json` with `operation = onboarding`, `status = active`, an exact `windowId`, `projectName`, `releaseLine`, immutable `openedAt`, `phaseStartedAt`, `expiresAt`, and `openedBy`, where `phaseStartedAt == openedAt` on creation and `expiresAt` is no more than 24 hours later, (5) add that exact branch name to the deployment branch policy of all three protected environments, (6) merge the matching `.github/official-caller-refs.json` and `.github/publish-trust-inventory.json` updates onto every branch in the protected control-plane branch set so the normalized caller-ref registry and inventory mapping remain identical across that set, (7) update any registry-side auth configuration that changed because of repository identity, trusted-publisher workflow selector path, auth mechanism, or environment-name changes, (8) update the same planned-change window to `status = cooldown`, set `closedAt` to the moment GitHub-side and repository-side trust returned to a matching state, and set `cooldownUntil` to at least 48 hours later, and only then (9) allow `official.yml` dispatches from that branch while the cooldown record remains for audit and anti-roll-extension enforcement. The branch created in step 2 may inherit the prior caller-ref registry snapshot from its source branch, but that inherited snapshot is not sufficient for official release until step 6 completes. Between steps 5 and 6, live GitHub policy and checked-in repository state are intentionally out of sync for a bounded planned-change window: `preflight-check` may pass, while `resolve-context`'s publish-trust-inventory preflight must still fail from that new branch until step 6 lands. Drift-detection automation must treat that state as planned only while a non-expired matching active window exists in `.github/planned-change-windows.json`; once `expiresAt` is reached, the mismatch is an incident even if the rollout is still in progress. If onboarding is aborted after step 5 before any step 6 merge lands, release engineering must first remove the branch from all three protected environments, then update the same window to `status = cooldown` with `closedAt` and `cooldownUntil`, and only then conclude the rollback. If onboarding is aborted after step 6 has landed on only a subset of the protected control-plane branch set, release engineering must first remove the branch from all three protected environments, then revert the checked-in caller-ref registry and inventory changes on every already-updated branch until the entire branch set is byte-identical again, and only then transition the same window to `status = cooldown`. Any other ordering is unsupported.

**Maintenance branch retirement order:** Retirement is the inverse control-plane change and must also be strict. The safe order is therefore: (0) enumerate all non-completed `official.yml` runs for that exact branch by separate queries for each non-completed status class, at minimum `queued`, `in_progress`, `waiting`, `requested`, and `action_required`, and determine whether any of those runs has already published one or more official destinations; (1) create or update a planned-change window entry in `.github/planned-change-windows.json` with `operation = retirement`, `status = active`, an exact `windowId`, `projectName`, `releaseLine`, immutable `openedAt`, `phaseStartedAt`, `expiresAt`, and `openedBy`, where `phaseStartedAt == openedAt` on creation and `expiresAt` is no more than 8 hours later; (2) remove that exact branch name from all three protected environments to close the approval-entry gate for new official runs; (2.5) perform active propagation polling for up to 5 minutes with at least 30-second intervals, then re-check that no new run entered the same `official::<ref>::<project-name>` concurrency groups before the policy change became effective; (3) drain the remaining already-admitted runs by either completing any run that already published one or more official destinations or explicitly treating that release identity as burned under Section 7, and by cancelling runs that have not yet published any official destination; (3.5) perform a second active polling pass for up to 5 minutes with at least 30-second intervals and re-check that the branch is quiescent with no queued, waiting-for-approval, action-required, requested, or in-progress `official.yml` runs and no still-open ledger entry for that release line; and only then (4) merge the matching `.github/official-caller-refs.json` and `.github/publish-trust-inventory.json` removals onto every remaining branch in the protected control-plane branch set so their normalized caller-ref registry and inventory remain identical, (5) update the same planned-change window to `status = cooldown`, set `closedAt` to the moment GitHub-side and repository-side trust returned to a matching state, set `cooldownUntil` to at least 48 hours later, and only then consider the retirement complete. Any other ordering is unsupported. The interval between steps 2 and 4 is an intentional planned-change mismatch window: live GitHub policy has already removed the branch from all three protected environments, while the checked-in caller-ref registry and inventory may still mention it until step 4 lands. Drift-detection automation must treat that state as planned only while a non-expired matching active or quarantine window exists in `.github/planned-change-windows.json`; once `expiresAt` is reached, the mismatch is an incident even if retirement is still in progress. Once step 2 succeeds, the deployment-policy removal remains in force for the rest of that retirement attempt; returning to step 0 or step 3 does not restore the branch to any protected environment. If step 2.5 or step 3.5 finds a new run, retirement must stop immediately, keep the branch removed from all three protected environments, and restart at step 0 using the same removed-policy state. If step 3.5 finds a still-open incident whose `publishedTargets` is non-empty, normal retirement must stop and hand off immediately to the emergency-cleanup path; partial official publication must reach a terminal ledger state while the branch remains outside all three protected environments, and quarantine is not a valid holding state for that case. If step 3.5 fails only because a matching `open-before-publish` incident remains open, retirement may update the same planned-change window to `status = quarantine`, keep `openedAt` unchanged, set `phaseStartedAt` to the quarantine start timestamp, set `expiresAt` to no more than 7 days later, set `linkedIncidentId` to that exact open incident's `incidentId`, and pause for at most 7 days while that entry is resolved. If the entry is still open after 7 days, stop normal retirement, open or update an operational incident ticket, keep the branch outside all three protected environments, transition the same planned-change window to `status = cooldown` with `closedAt` set to the escalation timestamp and `cooldownUntil` at least 48 hours later, and require release engineering to settle the ledger entry to a terminal state before retirement may resume from step 0 under a new active window after that cooldown expires. That quarantine pause does not count as a drain-attempt increment unless a new run is also detected. One restart from step 0 counts as exactly one drain attempt even if both rechecks observed new runs in the same pass, and the drain-attempt counter resets only after a full pass through step 3.5 finds no new run and no still-open ledger entry for that release line. If new runs still reappear after three consecutive drain attempts, stop normal retirement, open or update an operational incident ticket, update the corresponding ledger entry if any official release identity is already partially published or burned, and use the dedicated emergency-cleanup path defined below to keep the branch removed from all three protected environments, cancel any newly queued or waiting runs, and then restart from step 0 without restoring the policy in between. If retirement must be rolled back, it must first restore the matching `.github/official-caller-refs.json` and `.github/publish-trust-inventory.json` entries on the protected control-plane branch set, then restore the deployment-policy entries on all three protected environments, and only then transition the same planned-change window to `status = cooldown` with `closedAt` and `cooldownUntil`, so GitHub-side and repository-side trust state return to a matching configuration before any official release resumes. If step 4 was only partially merged across the protected control-plane branch set, rollback must first re-add the removed caller-ref and publish-trust entries on the already-updated branches until the set is byte-identical again, and only then restore the protected-environment deployment-policy entries. Git branch deletion is optional and may happen only after step 4 succeeds; if the branch is retained for archival purposes, it must remain outside all three protected environments, outside `.github/official-caller-refs.json`, and outside `.github/publish-trust-inventory.json`.

**Emergency-cleanup path:** This path is a named runbook, not an implicit operator judgment call. It exists only for retirement deadlock, break-glass control-plane repair, or post-failure stabilization after partial official publication. The steps are strict: (1) keep the affected branch removed from `production-<project-name>`, `production-tag-write-<project-name>`, and `production-evidence-write-<project-name>` for the full duration of the cleanup attempt; (2) cancel every queued, requested, waiting, or approval-pending `official.yml` run for that branch via the GitHub Actions API or `gh run cancel`; (3) for every `in_progress` run, first determine whether any official target has already been published and, if so, create or update the matching ledger incident before any manual cancellation; (4) if branch, tag, or `release-evidence` bypass is required, one operator performs the change and a second authorized human must separately approve the dedicated incident or recovery ticket before the action executes; (5) the JIT membership grant for the emergency-cleanup group must expire no later than 2 hours after issuance; (6) if stabilization is not achieved within 30 minutes, escalate to release-engineering incident management; (7) when this path is entered from retirement because an `open-partial-publish` incident exists, cleanup must settle that incident to a terminal state such as `burned`, `abandoned-after-partial-publish`, or `fully-withdrawn` while the branch remains outside all three protected environments; reopening the retired branch inside protected environments merely to finish remaining targets is unsupported; (8) when the branch is quiescent, restart the normal retirement or recovery procedure from its first numbered step without re-adding the removed deployment-policy entries in between. Every emergency-cleanup action must be recorded either as a ledger incident or as an `audit` record with `scope = "emergency-cleanup-governance"`. The required reviewed cleanup PR deadline means 17:00 UTC on the next Monday-Friday business day after the break-glass action.

**Prerequisites (must be configured before first run):**

- **Repository rulesets only:** This design uses GitHub repository rulesets, not legacy branch protection, for protected branches and protected tags. Rulesets configuration must be in place before the first workflow run; no backward-compatibility path for classic branch-protection endpoints is supported.
- **Branch rulesets** on the default branch, and on every maintenance release branch used for official hotfixes, must require at least two human PR approvals, required code-owner review, and the `ci-passed` required status check before merging, and must disallow direct pushes and force-pushes. Their bypass actors must be limited to the dedicated release-engineering emergency-cleanup group only; broad repository roles such as `admin` or `write`, and the GitHub Actions app, are not allowed as bypass actors. The emergency-cleanup group is break-glass only: it must be JIT-managed, limited to at most three named humans, and must not overlap with the required-reviewer lists of any `production-*` environment. Those membership-shape constraints are governance requirements rather than something `preflight-check` can prove mechanically with its read-only metadata App. Every actual use of branch or tag bypass authority must require contemporaneous dual control: one operator performs the action, and a second authorized human separately approves the incident or recovery ticket before the action is executed. Without this, direct pushes bypass `ci.yml` entirely, allowing unreviewed code to be released. Any PR that changes `.github/official-caller-refs.json`, `.github/publish-trust-inventory.json`, reusable publish workflow paths, or any other trust-bearing control-plane file must therefore receive at least two human approvals, one of which must satisfy the CODEOWNERS requirement.
    Release engineering must perform a reviewed attestation at least every 30 days and after any membership change that the emergency-cleanup group still meets those size and reviewer-separation constraints. That attestation must be recorded in `.github/release-recovery-ledger.jsonl` as an `audit` record with `scope = "emergency-cleanup-governance"` and an `evidenceUrl` pointing to the reviewed issue, PR, or audit-log permalink that documents the attestation. For this requirement, a recorded membership change means a GitHub audit-log event that adds or removes a member from the dedicated emergency-cleanup group. The repository's alerting-only control-plane audit automation must use that audit-log event stream as the authoritative trigger and must alert when the newest such attestation is older than 30 days or missing after a recorded membership change.
- **Official release tag rulesets** must restrict both tag creation and tag updates on `refs/tags/release/**`. Because GitHub repository rulesets cannot scope bypass by workflow file path, the configuration must use a dedicated release-tag writer GitHub App as the only automation bypass actor for normal workflow execution and a dedicated release-engineering emergency-cleanup group as the only human bypass actor for manual recovery. The GitHub Actions app that backs `GITHUB_TOKEN` must **not** be a bypass actor on `refs/tags/release/**`. Ruleset bypass is not create-only: those bypass actors can create, update, and delete protected release tags, so the revocation runbook and audit monitoring must explicitly cover unexpected tag deletions as well as writes.
- **Release-evidence branch rulesets** must protect `refs/heads/release-evidence` against force-push and branch deletion. The only automation bypass actor for that branch is the dedicated release-evidence writer GitHub App used by `require-provenance`; the only human bypass actor is the dedicated release-engineering emergency-cleanup group.
- **Release-evidence branch bootstrap:** Before the first production run, release engineering must create `refs/heads/release-evidence` with an initial reviewed commit and then apply the required branch ruleset. Ruleset configuration alone does not create the branch, and `require-provenance` must hard-fail if that branch is missing.
- **Project-scoped publish environments:** For every releasable project, `environment: production-<project-name>` must exist in GitHub repository settings with protection rules that include required reviewers and `prevent_self_review = true` **before** the workflow is ever triggered. If that environment does not pre-exist, GitHub auto-creates it with **zero** protection rules and the human approval gate silently does not exist.
- **Project-scoped tag-write environments:** For every releasable project, `environment: production-tag-write-<project-name>` must also exist before the workflow is ever triggered. It must use the same required-reviewer protection profile and deployment branch policy as `production-<project-name>`, and it is reserved exclusively for `create-release-tag` and the release-tag writer App private key.
- **Project-scoped evidence-write environments:** For every releasable project, `environment: production-evidence-write-<project-name>` must also exist before the workflow is ever triggered. It must use the same required-reviewer protection profile and deployment branch policy as `production-<project-name>`, and it is reserved exclusively for `require-provenance` and the release-evidence writer App private key.
- **Project-scoped deployment branches:** Every `production-<project-name>`, `production-tag-write-<project-name>`, and `production-evidence-write-<project-name>` environment's deployment branch policy must allow only the protected control-plane branch set for that project: `main` and eligible protected maintenance branches `release/<project-name>/v<release-line>`. Wildcard entries such as `release/**` are not allowed. No other branch may enter that project's protected official-release flow.
- **Workflow file ownership:** `.github/CODEOWNERS`, `.github/workflows/**`, `.github/actions/**`, `.github/official-caller-refs.json`, `.github/publish-trust-inventory.json`, `.github/planned-change-windows.json`, `.github/release-recovery-ledger.jsonl`, `eng/scripts/**`, `**/release.json`, `**/version.json`, `hk.pkl`, `PklProject`, `PklProject.deps.json`, `mise.toml`, `mise.lock`, `global.json`, `biome.jsonc`, `pnpm-lock.yaml`, `uv.lock`, `Gemfile.lock`, `Directory.Packages.props`, and every other trusted control-plane helper code or shared dependency-control file consumed by official build or release jobs must be protected by `CODEOWNERS` review from a dedicated release-engineering group on every branch in the protected control-plane branch set. Every such file must also be represented in the `detect-changes` `infra` inventory in `ci.yml`; there is no separate implicit trust-bearing file class outside that reviewed inventory. Protected control-plane branches must also require code-owner review in their rulesets configuration.
- **Workflow boundary policy:** Local reusable publish workflows are not authorization boundaries by themselves. Repository policy must therefore hard-fail if any workflow other than `.github/workflows/buddy.yml` or `.github/workflows/official.yml` calls a local reusable publish workflow under `.github/workflows/_publish-*.yml`. Only `.github/workflows/official.yml` may reference an environment whose name matches `production-*`, request `id-token: write`, call `_publish-github.yml`, or reference the release-evidence writer App credential. `.github/workflows/buddy.yml` may call only `_publish-nuget.yml`, `_publish-npm.yml`, and `_publish-rubygems.yml` with the documented GitHub Packages endpoints and `packages: write`; any other same-repository publish caller is a hard failure.
- **GitHub App credentials and explicit registry secrets:** Before first use, release engineering must provision three GitHub Apps and store their private keys in the narrowest possible GitHub secret scopes: a read-only control-plane metadata App for `preflight-check` and CI trust validation, a release-tag writer App whose private key is stored only in the corresponding `production-tag-write-<project-name>` environment for `create-release-tag`, and a release-evidence writer App used only by `require-provenance` to push to `refs/heads/release-evidence`. `production-<project-name>` must never carry either write credential. These Apps must request only the repository permissions required for their single purpose. The required scopes are strict: the metadata App must have `metadata: read`, `administration: read`, and `environments: read` with no write scopes; the release-tag writer App must have `contents: write` and no other repository scopes; the release-evidence writer App must have `contents: write` and no other repository scopes. Because NuGet.org does not have documented trusted publishing in this design, each releasable project that publishes to `nuget:official` must also carry exactly one explicit `NUGET_API_KEY` secret in `production-<project-name>`, and that secret must be passed only to `publish-nuget-official`. The provider-side key scope must be limited to the exact package ID or project-specific package pattern for that one project; cross-project NuGet.org push keys are unsupported. Organization administrators remain the trusted root for these GitHub-hosted secrets; this design does not attempt to defend against org-admin compromise. All private keys and explicit registry secrets must rotate at least every 90 days and immediately on suspected compromise, and the revocation runbook must define how to revoke the old key or API key, install the replacement, validate the tag-write path, evidence-write path, or NuGet publish path as applicable, review GitHub audit logs for abuse, and audit the protected `refs/tags/release/**` namespace plus the `release-evidence` branch for unexpected mutation. Any GitHub App installation token minted at runtime must be masked immediately after issuance, before any other use, and should be explicitly revoked at job end on a best-effort basis in addition to relying on its native expiry.
- **Repository administration monitoring:** Changes to production environments, rulesets, bypass actors, GitHub App installations, or other release-control-plane administration state must emit near-real-time alerts. Audit logs remain required, but they are not the only detection mechanism in this design.
- **Live control-plane drift detection:** In addition to near-real-time administration alerts, the repository must run a scheduled or push-triggered control-plane drift check that queries every live `production-<project-name>`, `production-tag-write-<project-name>`, and `production-evidence-write-<project-name>` deployment branch policy and compares the exact short-branch set to `.github/official-caller-refs.json`. Outside a non-expired matching `active` or `quarantine` record in `.github/planned-change-windows.json`, any mismatch between live GitHub state and the checked-in caller-ref registry is a control-plane incident even if no release is currently running. `cooldown` records document recent changes but do not authorize live drift. Drift automation must also fail if `.github/planned-change-windows.json` contains an expired window that has not been removed.
- **Official registry auth policy:** Only `npmjs`, `PyPI`, and `RubyGems.org` use trusted publishing in this design. `NuGet.org` uses an explicit environment secret because a documented trusted-publishing path is not available here. The authoritative branch restriction for every official target remains the GitHub deployment-branch policy of the corresponding protected environments. Exact provider-side binding to branch, tag, or commit SHA is not assumed for any registry.
- **Trusted-publisher selector matrix:** `npmjs` binds the repository, the calling workflow filename `.github/workflows/official.yml`, the project-scoped production environment, and the documented OIDC audience `npm:registry.npmjs.org`; it does not use the called reusable publish workflow as the trust anchor. `PyPI` publicly documents the repository, workflow filename, and OIDC audience `pypi`, but does not explicitly document reusable-workflow path handling; this design nevertheless models `pypi:official` with the called reusable workflow `.github/workflows/_publish-pypi.yml` to match GitHub's `job_workflow_ref` behavior, and pre-implementation validation against a disposable test publisher is mandatory before `pypi:official` is enabled for production use. `RubyGems.org` documents reusable-workflow binding through the called reusable publish workflow `.github/workflows/_publish-rubygems.yml` plus the project-scoped production environment, and this design follows that documented model. Exact branch, tag, or commit-SHA binding is not assumed documented in any provider UI and therefore is not part of this design.
- **Shared trusted-publisher boundary note:** No registry in scope publicly documents stronger isolation between multiple jobs that run inside the same entry workflow and the same protected environment. This design therefore treats all official trusted-publisher jobs that execute inside `production-<project-name>` as one shared external trust boundary. The compensating control is that those jobs, their reusable workflows, and their helper code all remain on the protected control-plane branch set under mandatory review; the design does not claim registry-side per-job separation that the providers do not document.
- **Trusted-publisher and secret change management:** Because external registry auth is coupled to repository identity, trusted-publisher workflow selector path, project-scoped production environment name, target auth mechanism, and any documented provider audience in this design, any repository move/rename that changes identity, any change in target auth mechanism, any rename of `production-<project-name>`, any change in a documented registry audience, or any move/rename of a trusted-publisher-backed workflow selector path must be accompanied by the corresponding external configuration update before the next release. Changes to the allowed protected control-plane branch set are GitHub-side operations only in this design: they must update the deployment branch policy on the relevant protected environments, the authoritative checked-in caller-ref registry at `.github/official-caller-refs.json`, and the checked-in publish trust inventory, but they do not require registry-side branch-specific trust edits. Every active branch in the protected control-plane branch set must carry byte-identical normalized copies of `.github/official-caller-refs.json`, and the `allowedCallerRefs` array in `.github/publish-trust-inventory.json` must exactly mirror that file after normalization. The checked-in publish trust inventory must be updated in the same reviewed PR for every official target whose trusted selector path, environment, auth mechanism, or documented provider audience changed, including `github:official` and `nuget:official` where no trusted-publisher audience field exists. CI enforces repository-side drift by running the explicit `trusted-release-inventory` job in `ci.yml`, which must compare the post-change trust-bearing state rather than merely checking whether both the inventory file and another control-plane file were edited. The comparison scope is exactly `entryWorkflowPath`, the deduplicated set of fully qualified `allowedCallerRefs` derived from `.github/official-caller-refs.json`, the `publishWorkflowPaths` mapping, the `targetEnvironments` mapping, the `targetAuthMechanisms` mapping, the `targetTrustedPublisherSelectors` mapping, and the `targetOidcAudiences` mapping. Order-only differences in `allowedCallerRefs` are not meaningful, but every added, removed, renamed, or remapped caller ref, publish workflow path, environment name, auth mechanism, trusted-publisher selector, or documented audience is a hard mismatch. CI must fail any control-plane change for which those post-change values do not exactly match the checked-in inventory, whether or not `.github/publish-trust-inventory.json` itself changed.

**Authoritative official caller-ref registry:** The checked-in authoritative repository-side source of active official caller refs is `.github/official-caller-refs.json`. This file is not a convenience cache; it is a required control-plane contract that records the normalized fully qualified refs that may dispatch `official.yml`. It uses `schemaVersion: 1` and the exact schema:

```json
{
    "schemaVersion": 1,
    "refs": ["refs/heads/main", "refs/heads/release/example-project/v1.2.x"]
}
```

No top-level keys other than `schemaVersion` and `refs` are allowed. `refs` must be a non-empty array of unique fully qualified Git refs. Every active branch in the protected control-plane branch set must carry the same normalized file contents before official release resumes. GitHub-side deployment branch policy, the checked-in publish trust inventory, and runtime official caller-ref validation all derive from this file rather than from ad hoc branch enumeration.

**Planned-change windows schema:** The checked-in authoritative repository-side source of active and recently closed onboarding and retirement control-plane windows is `.github/planned-change-windows.json`. This file is not free-form operator scratch state; it is a strict control-plane contract used by CI validation and drift-detection automation. It uses `schemaVersion: 1` and the exact schema:

```json
{
    "schemaVersion": 1,
    "windows": [
        {
            "windowId": "example-project-v1.2.x-retirement-20260311T120000Z",
            "operation": "retirement",
            "status": "active",
            "projectName": "example-project",
            "releaseLine": "1.2.x",
            "openedAt": "2026-03-11T12:00:00Z",
            "phaseStartedAt": "2026-03-11T12:00:00Z",
            "expiresAt": "2026-03-11T16:00:00Z",
            "openedBy": "release-engineering"
        }
    ]
}
```

No top-level keys other than `schemaVersion` and `windows` are allowed. `windows` must be an array of unique objects where `windowId` is unique. Every window object must contain exactly `windowId`, `operation`, `status`, `projectName`, `releaseLine`, immutable `openedAt`, `phaseStartedAt`, `expiresAt`, and `openedBy`, plus optional `linkedIncidentId`, `closedAt`, `cooldownUntil`, and `notes`. `operation` must use the closed set `{onboarding, retirement}`. `status` must use the closed set `{active, quarantine, cooldown}`. `openedAt`, `phaseStartedAt`, `expiresAt`, `closedAt`, and `cooldownUntil` must be RFC 3339 UTC timestamps. `projectName` and `releaseLine` must each match the same validation rules used elsewhere in this design. `openedAt` records when the window tuple was first opened and must never be rewritten; `phaseStartedAt` records when the current status phase began. When `status = active` and `operation = onboarding`, `expiresAt - phaseStartedAt` must be no more than 24 hours and `linkedIncidentId`, `closedAt`, and `cooldownUntil` must be absent. When `status = active` and `operation = retirement`, `expiresAt - phaseStartedAt` must be no more than 8 hours and `linkedIncidentId`, `closedAt`, and `cooldownUntil` must be absent. When `status = quarantine`, `operation` must be `retirement`, `expiresAt - phaseStartedAt` must be no more than 7 days, `linkedIncidentId` is required and must equal the exact open incident record's `incidentId`, and `closedAt` plus `cooldownUntil` must be absent. A transition from `active` to `quarantine` must keep `openedAt` unchanged, must rewrite `phaseStartedAt` and `expiresAt` to the quarantine phase's start and end timestamps in the same reviewed change, and is valid only for an `open-before-publish` incident. When `status = cooldown`, the window no longer authorizes drift, `closedAt` and `cooldownUntil` are required, `cooldownUntil` must be at least 48 hours after `closedAt`, and the record must be removed promptly once `cooldownUntil` passes. At most one non-expired window may exist for the same `{operation, projectName, releaseLine}` tuple, and a new `active` window for that tuple is forbidden until the prior record's cooldown has expired and been removed.

**Publish trust inventory schema:** The checked-in inventory is part of the trusted control plane and must be read from the current protected source workspace for the run. It uses `schemaVersion: 1` and records the entry workflow, exact caller refs, the publish workflow path for each official target, the expected environment name for each target, the expected auth mechanism for each target, the provider-specific trusted-publisher selector contract for each trusted-publisher-backed target, and the exact documented OIDC audience for each target whose provider documents one. Buddy targets are intentionally excluded because they publish with `GITHUB_TOKEN` to GitHub Packages and do not have external registry-side trust state to drift:

```json
{
    "schemaVersion": 1,
    "entryWorkflowPath": ".github/workflows/official.yml",
    "allowedCallerRefs": ["refs/heads/main", "refs/heads/release/example-project/v1.2.x"],
    "publishWorkflowPaths": {
        "nuget:official": ".github/workflows/_publish-nuget.yml",
        "npm:official": ".github/workflows/_publish-npm.yml",
        "pypi:official": ".github/workflows/_publish-pypi.yml",
        "rubygems:official": ".github/workflows/_publish-rubygems.yml",
        "github:official": ".github/workflows/_publish-github.yml"
    },
    "targetEnvironments": {
        "nuget:official": "production-example-project",
        "npm:official": "production-example-project",
        "pypi:official": "production-example-project",
        "rubygems:official": "production-example-project",
        "github:official": "production-example-project"
    },
    "targetAuthMechanisms": {
        "nuget:official": "environment-secret",
        "npm:official": "trusted-publisher",
        "pypi:official": "trusted-publisher",
        "rubygems:official": "trusted-publisher",
        "github:official": "github-token"
    },
    "targetTrustedPublisherSelectors": {
        "npm:official": {
            "selectorModel": "entry-workflow",
            "workflowPath": ".github/workflows/official.yml",
            "environment": "production-example-project"
        },
        "pypi:official": {
            "selectorModel": "reusable-workflow",
            "workflowPath": ".github/workflows/_publish-pypi.yml",
            "environment": "production-example-project"
        },
        "rubygems:official": {
            "selectorModel": "reusable-workflow",
            "workflowPath": ".github/workflows/_publish-rubygems.yml",
            "environment": "production-example-project"
        }
    },
    "targetOidcAudiences": {
        "npm:official": "npm:registry.npmjs.org",
        "pypi:official": "pypi"
    }
}
```

The inventory uses fully qualified Git refs under `allowedCallerRefs`, repository-relative workflow paths, a target-to-environment mapping, an explicit target-to-auth mapping, a target-to-selector mapping for trusted-publisher-backed targets, and a target-to-documented-audience mapping for the subset of targets whose provider publicly documents an audience. `allowedCallerRefs` is not an independent source of truth: it must exactly mirror the normalized `refs` array from `.github/official-caller-refs.json`.

No top-level keys other than `schemaVersion`, `entryWorkflowPath`, `allowedCallerRefs`, `publishWorkflowPaths`, `targetEnvironments`, `targetAuthMechanisms`, `targetTrustedPublisherSelectors`, and `targetOidcAudiences` are allowed. The validator must enforce this as a strict top-level key whitelist equivalent to JSON Schema with `additionalProperties: false`. `publishWorkflowPaths`, `targetEnvironments`, and `targetAuthMechanisms` must contain exactly the five official target keys. `targetTrustedPublisherSelectors` must contain exactly the currently trusted-publisher-backed targets `{npm:official, pypi:official, rubygems:official}` and no others. Every selector object must contain exactly `selectorModel`, `workflowPath`, and `environment`; `selectorModel` must use the closed set `{entry-workflow, reusable-workflow}`. `targetOidcAudiences` must contain exactly the targets whose provider publicly documents a required audience in this design; as of v2.6 that set is `{npm:official, pypi:official}`.

The checked-in inventory is an in-repository drift detector and audit trail, not an independent cryptographic proof of registry-side trust state. An actor who can merge arbitrary changes into the protected control-plane branch set can change both the workflow code and the inventory together. Its purpose is to make trust changes reviewable and to catch accidental repository-side drift before production approval is consumed.

**Jobs:**

1. **`preflight-check`**:
    - Runs before `resolve-context`.
    - `permissions: {}`
    - This job is intentionally unconditional and must not acquire an `if:` guard.
    - This job must not perform repository checkout. It must mint a dedicated read-only GitHub App installation token just in time and use that token for both GitHub Environments API reads and GitHub Repository Rulesets API reads. A long-lived PAT is not the normal path. The GitHub App private key must be stored as an organization-level Actions secret scoped only to this repository, not as a repository-level secret. If a temporary fallback secret is ever required before the GitHub App path exists, it must be treated as an emergency-only exception, kept out of repository-level secrets, and removed once the GitHub App path is in place. Treat the default `GITHUB_TOKEN` as insufficient for this job; weakening or skipping the verification is unsupported. The minted installation token must be masked before first use.
    - Before its first environment query, this job must validate `project-name` with the same syntax rule as `resolve-context`, derive `production-<project-name>`, `production-tag-write-<project-name>`, and `production-evidence-write-<project-name>`, and treat those derived environment names as the only valid protected environments for the run.
    - Verifies that all three derived environments already exist, each includes at least one required-reviewer protection rule, each has `prevent_self_review` enabled, and each restricts deployment branches to the official protected control-plane branch set for that project.
    - Uses the GitHub Environments API response directly: the check must look for a `protection_rules` entry with `type == "required_reviewers"` and a non-empty reviewer list, must verify `prevent_self_review == true`, and must verify that the deployment branch policy on all three environments contains only exact branch names for `main` plus the registered protected maintenance branches for that project. Wildcard or pattern-based entries such as `release/**` are hard failures. Because the API returns short branch names rather than fully qualified refs, this job must normalize the expected caller refs by stripping the `refs/heads/` prefix before comparison. A wait timer or branch policy alone is not sufficient. This job verifies the protection quality of every branch already listed in those deployment policies; completeness of the allowed caller-ref set is enforced separately by the publish trust inventory preflight in `resolve-context`, and `refs/heads/release-evidence` is outside that environment-derived branch set by design.
    - Uses the GitHub Repository Rulesets API only. It must verify that active branch rulesets protect `main`, every non-`main` branch currently allowed by the production environment, and the exact branch `refs/heads/release-evidence` with the required profile for each ref. The protected release branches must carry the same required PR review, required code-owner review, required status check `ci-passed`, no direct pushes, and no force-pushes, and their bypass actors must be limited to the dedicated release-engineering emergency-cleanup group rather than broad repository roles or the GitHub Actions app. `refs/heads/release-evidence` must be protected against force-push and deletion and must allow bypass only for the dedicated release-evidence writer App plus the dedicated release-engineering emergency-cleanup group. The job must also verify that an active tag ruleset protects `refs/tags/release/**` against unauthorized creation and updates, and that its bypass actors are limited to the dedicated release-tag writer App plus the dedicated release-engineering emergency-cleanup group.
    - `preflight-check` must not claim to machine-enforce the emergency-cleanup group's maximum size or reviewer-overlap policy; those are governance checks performed out of band because the metadata App intentionally does not enumerate team membership.
    - Treats every GitHub API error as a hard failure. Specifically: `404` from environment endpoints means one or both derived project-scoped environments are missing; a successful API response that lacks the required reviewers rule, has `prevent_self_review` disabled, has a wildcard deployment branch policy, lacks the required branch or tag rulesets, or applies a weaker ruleset profile than `main` means the protected environment set is misconfigured; every other non-`200` response blocks the workflow as an environment-verification failure.
    - Fails hard if the environment is missing or unprotected. This turns the documented prerequisite into an executable guardrail.
    - All GitHub API calls in this job must set an explicit client timeout of no more than 30 seconds per request so the guard fails fast rather than consuming the full job timeout on a hung response.
    - This check is still an audit-before-use guard, not a transactional lock. If an administrator weakens or deletes environment protection after `preflight-check` passes but before a publish job reaches `production-<project-name>`, the later GitHub environment evaluation remains authoritative. The same residual TOCTOU window exists for tag rulesets: `preflight-check` validates the live ruleset configuration at job start, while `create-release-tag` is still subject to whatever tag ruleset is live at push time. Those residual windows are accepted and must be controlled through CODEOWNERS, repository audit logs, and change discipline around production protection settings.

2. **`resolve-context`**:
    - `needs: [preflight-check]`
    - `permissions: { contents: read }`
    - **Input validation (first step, before checkout):** Validate `project-name` with a full-string match against `[A-Za-z0-9][A-Za-z0-9._-]*`, reject any occurrence of `..`, reject trailing `.`, and reject any name that ends with `.lock`. Reject invalid names with a clear error.
    - **Runner and tooling:** Runs on `ubuntu-latest`. Like `resolve-context` in `buddy.yml`, version resolution uses the repository-local `nbgv-python` adapter from the checked-out source ref and does not require a Windows runner even for C# projects. The job must hard-fail if `mise.lock` is absent, and should restore a tool cache keyed by `mise.toml` and `mise.lock` before invoking `mise install`. If the lockfile needs regeneration, that is an out-of-band repository change performed with `mise lock`, not a workflow fallback. Any tool used in an official build or publish path must use a digest-pinning backend unless the exact tool/backend pair is listed in the reviewed `hk.pkl` exception registry with rationale and owner. If `nbgv-python` cannot resolve the version deterministically, the job must hard-fail; there is no fallback or manual override path in this design.
    - **Source checkout:** Check out the dispatch-selected protected source ref for this workflow run with `fetch-depth: 0` and `persist-credentials: false`. In `official.yml`, that source workspace is both the trusted control-plane checkout and the release payload input.
    - Runs `eng/scripts/find_project_path.py` to resolve `language` and `project-path` from `project-name`. `project-name` is case-sensitive and must resolve to exactly one project in the repository. The resolution step must emit exactly one of `{csharp, python, jsts, ruby}` for `language`; no match, ambiguous match, unsupported language, or resolver error is a hard failure.
    - **NBGV resolution and semantic validation:** Resolve the version with `nbgv-python`, hard-fail if that resolution is non-deterministic, and use the resolved value as the workflow output `version`. Here, "non-deterministic" has the same meaning as in `buddy.yml`: no unique governing `version.json`, no unique normalized version string from the selected full-history checkout, or validator rejection of the resolved string. Inheriting the nearest ancestor `version.json` when the project root does not have its own local file is intentional, but the governing file must still resolve uniquely. Then validate that resolved version using `eng/scripts/validate_semver2_version.py` (NuGet and npm), `eng/scripts/validate_rubygems_version.py` (the repository's supported RubyGems-compatible subset), or `eng/scripts/validate_pep440_version.py --channel official` (Python), chosen after the project language is known. The Python validator must receive that explicit channel flag; default-mode inference is unsupported. `eng/scripts/validate_pep440_version.py --channel official` must reject non-canonical normalized PEP 440 strings, all epoch markers (`!`), all local version identifiers (`+...`), and all `.devN` development-release forms on the official release path. `.postN` post-releases remain valid on the official release path and derive the same `<release-line>` as their base release. `eng/scripts/validate_semver2_version.py` must reject official release versions that contain SemVer build metadata (`+...`).
    - **Official branch-line validation:** Derive `<release-line>` using the release-line derivation rule above. At the start of this job, capture a single comparison SHA for `origin/main` and compute the `main` release line from that frozen snapshot only; do not recompute against a moving `origin/main` later in the run. The comparison must read the same resolved project's exact governing version file path from that frozen snapshot, using the governing path that `nbgv-python` resolved on the dispatch checkout rather than assuming `<project-path>/version.json`. If that resolved governing path is absent from the frozen `origin/main` snapshot, fail with operator guidance that distinguishes between "the project path is not yet on `main`" and "the governing version file for that project is missing from the frozen `main` snapshot"; the workflow must never guess a mainline release line. If the resolved `<release-line>` matches that frozen `main` release line, the current caller ref must be `refs/heads/main`. If it differs, the current caller ref must be exactly `refs/heads/release/<project-name>/v<release-line>`, and that protected maintenance branch must already exist. If the current caller ref is a maintenance branch but the frozen `main` release line still equals `<release-line>`, hard-fail with operator guidance that `main` must be bumped to a newer release line before this maintenance branch may carry official releases. The workflow must not accept a non-`main` release line from `main`, and must not accept a `main` release line from a maintenance branch.
    - Reads `release.json` from the selected source workspace, validates it exactly as specified in **Section 5**, applies the same language-target validation rule as `buddy.yml`, then filters to the official target set `{nuget:official, npm:official, pypi:official, rubygems:official, github:official}` and fails if the filtered set is empty.
    - **Official npm dist-tag derivation:** If `npm:official` is present in the filtered target set, derive `npm-dist-tags` deterministically as a compact ordered JSON array from the validated caller ref, release line, and whether the resolved version is stable or prerelease. Stable releases from `refs/heads/main` use `["latest"]`. Stable releases from a maintenance branch `refs/heads/release/<project-name>/v<release-line>` use `["release-v<major>.<minor>"]` and must never append `latest`. Prerelease versions remain eligible for official publication, but they must never claim `latest`: lowercase the first prerelease identifier and use that entire identifier as the prerelease channel token. That token must match `^[a-uw-z][a-z0-9]*$`; numeric-leading identifiers, numeric-only identifiers, identifiers containing separators such as `-`, `.`, or `_`, and identifiers beginning with `v` are unsupported for `npm:official` and must hard-fail in `resolve-context`. `resolve-context` must enforce the `!startsWith('v')` rule explicitly even if a future regex refactor changes the character class. The derived prerelease channel token must then be validated structurally and contextually before any maintenance-line prefix is added: it must not equal `latest`, must not equal `buddy`, must not equal `release`, and must not begin with `release-v`. Prerelease releases from `main` therefore use tags such as `rc`, `beta`, `alpha`, `preview`, or `next`; prerelease releases from maintenance branches use `["release-v<major>.<minor>-<channel>"]`. `resolve-context` must perform this structural validation before emitting `npm-dist-tags`, and `_publish-npm.yml` must derive the caller ref from runtime `github.ref` and validate each requested npmjs tag against that runtime ref and `release-line` before any registry mutation.
    - **Publish trust inventory preflight:** After the official target set is resolved, read `.github/official-caller-refs.json` and `.github/publish-trust-inventory.json` from the current protected caller ref, validate both schemas, verify that `entryWorkflowPath` is exactly `.github/workflows/official.yml`, verify that the current caller ref is present in `.github/official-caller-refs.json`, verify that `allowedCallerRefs` exactly mirrors the normalized `refs` array from `.github/official-caller-refs.json`, verify that every filtered official target maps to the expected reusable publish workflow path via `publishWorkflowPaths`, verify that every filtered official target maps to the expected project-scoped environment name via `targetEnvironments`, verify that every filtered official target maps to the expected auth mode via `targetAuthMechanisms` (`environment-secret` for `nuget:official`, `trusted-publisher` for `npm:official`, `pypi:official`, and `rubygems:official`, `github-token` for `github:official`), verify that every filtered trusted-publisher-backed target maps to the expected selector contract via `targetTrustedPublisherSelectors`, and verify that every filtered target whose provider documents an OIDC audience maps to the expected value via `targetOidcAudiences`. This catches repository-side trust drift before any production approval is consumed. Because registry-side trust settings are not queried portably, matching external configuration updates are still a mandatory operational step.
    - **Official release tag derivation and overwrite guard:** Derive `tag-name = release/<project-name>/v<version>`. This guard must query the remote protected tag namespace via `git ls-remote --tags` or the GitHub refs API rather than relying on a local tag list from checkout. When `git ls-remote --tags` is used, annotated tags must be compared by their peeled `refs/tags/<tag>^{}` commit SHA rather than by the raw tag-object SHA at `refs/tags/<tag>`. If that protected official release tag already exists and points to a different commit, fail immediately. If it already exists and points to the current commit, treat the tag reservation as an idempotent no-op. If `github:official` is among the resolved targets, check GitHub Releases state for that derived tag. If no GitHub Release exists for that derived tag, proceed — this is the normal first official run. Official GitHub Releases must use a deterministic release title `<project-name> v<version>`. The guard must scan GitHub Releases to completion, following pagination across non-pre-release releases including drafts, and must hard-fail on API, authentication, authorization, rate-limit, transport, or response-shape errors. An interrupted, truncated, or otherwise incomplete scan is `unknown`, not `not found`. Match that deterministic stable title across the completed stable-release set and fail immediately if the same title already exists under a different tag or commit. A draft or published stable release with that deterministic title is part of the same stable identity space; the design does not treat drafts as a separate namespace. If a pre-release GitHub Release exists for the same derived tag, `_publish-github.yml` may promote it to a stable release only after remote asset identity checks compare only `github-release-asset` manifest entries, exclude workflow-derived assets such as `SHA256SUMS`, and confirm that every already-present remote asset matches the current local build output; a divergent same-tag pre-release is a hard failure. If a non-pre-release GitHub Release already exists for the same derived tag, defer the idempotent/no-op decision to `_publish-github.yml`, which must verify remote asset identity before reporting success. If `github:official` is not in the resolved target set but stable GitHub Releases already exist for `release/<project-name>/v*`, the workflow may emit a non-blocking warning to the step summary reminding operators that those Releases are now manual state.
    - **Outputs:** `tag-name`, `language`, `project-name`, `project-path`, `version`, `release-line`, `targets` (compact JSON array of filtered official targets in canonical official-target order `nuget:official`, `npm:official`, `pypi:official`, `rubygems:official`, `github:official`), `npm-dist-tags` (compact JSON array when `npm:official` is selected).

3. **`static-analysis`**:
    - `needs: [resolve-context]`
    - `permissions: { contents: read }`
    - Checks out the source ref for this workflow run before enumerating files. Read-only checkout must use `persist-credentials: false`.
    - Runs `hk check <project-path>` scoped to the resolved project path. HK receives the project path directly and discovers applicable files under that path according to `hk.pkl`; this design does not pre-enumerate file names in shell.

4. **`clean-build`** (`build-csharp` / `build-python` / `build-jsts` / `build-ruby`):
    - For supply chain security, no prior artifacts are reused. A fresh build and test run is performed from the exact dispatch-selected commit for this workflow run. The checkout must use `fetch-depth: 0` for NBGV resolution.
    - Uses the same four static conditional build jobs pattern as `buddy.yml`, with `permissions: { contents: read, id-token: write, attestations: write }`, `secrets: {}`, and the required `with:` inputs wired from `build-scope: release`, `needs.resolve-context.outputs.project-path`, `needs.resolve-context.outputs.project-name`, `checkout-ref: ${{ github.sha }}`, and `require-provenance: true`. Each build job depends on both `resolve-context` and `static-analysis`. Only the language-matching build job executes; the others are skipped.

5. **`require-provenance`**:
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby]`
    - `permissions: { contents: read }`
    - `environment: production-evidence-write-<project-name>` — mandatory. Durable evidence persistence must occur only inside the dedicated evidence-write environment.
    - `if: always() && !cancelled() && !failure() && needs.resolve-context.result == 'success' && needs.static-analysis.result == 'success' && ((needs.resolve-context.outputs.language == 'csharp' && needs.build-csharp.result == 'success' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'python' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'success' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'jsts' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'success' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'ruby' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'success'))`
    - Downloads the provenance artifact emitted by the single language-matching build job, validates that the attestation set is present, validates the accompanying durable evidence record, and verifies binding to the exact workflow run, repository identity, source commit, project identity, version, and build artifact manifest for this release attempt.
    - Mints a dedicated release-evidence writer GitHub App installation token and uses that token, not the job `GITHUB_TOKEN`, for the durable evidence push. The minted token must be masked immediately after issuance, before any other use, and should be explicitly revoked at job end on a best-effort basis.
    - After validation, writes `artifact-evidence.json` to the protected durable evidence branch `refs/heads/release-evidence` at the exact path `.github/release-evidence/<project-name>/<version>/<source-commit>/runs/<github.run_id>-attempt-<github.run_attempt>/artifact-evidence.json`, but must first hard-fail if that exact path already exists. Same-path overwrites are unsupported. The read-back verification must use the committed blob's content-addressed Git blob permalink by blob SHA, not a mutable branch/path URL, and must verify byte equality before succeeding. This write path is mandatory, not best-effort. `artifactManifestEvidenceUrl` for any later recovery ledger entry must point to that immutable blob permalink. A missing durable evidence branch, evidence-writer credential failure, write failure, attempted overwrite, or byte-mismatch on the read-back verification is a hard failure.
    - Emits `artifact-manifest-evidence-url` as a workflow output so later recovery and audit steps can reference the durable evidence record directly.
    - This job is the machine-enforced production gate for Section 8. Until provenance support exists for every enabled official publish path, `require-provenance` must fail closed and keep `create-release-tag` plus all official publish jobs ineligible.

6. **`create-release-tag`**:
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance]`
    - `if: always() && !cancelled() && !failure() && needs.resolve-context.result == 'success' && needs.static-analysis.result == 'success' && needs.require-provenance.result == 'success'`
    - `permissions: { contents: read }`
    - `environment: production-tag-write-<project-name>` — mandatory. Tag reservation must occur only inside the dedicated tag-write environment; package-registry and GitHub Release publish jobs must never enter that environment.
    - The job name and approval context shown to reviewers must include the project name, resolved version, caller ref, `github.run_id`, and at least a 12-character source-commit prefix so concurrent approval requests for the same project are distinguishable.
    - Mints a dedicated release-tag writer GitHub App installation token from a secret in `production-tag-write-<project-name>` and uses that token for the tag push. The job `GITHUB_TOKEN` is never the bypass actor for `refs/tags/release/**`. The minted token must be masked immediately after issuance, before any other use, and should be revoked explicitly at job end on a best-effort basis.
    - Creates the protected official release-identity tag `release/<project-name>/v<version>` at the current workflow commit after approval and before any official publish job becomes eligible. Reserving the official identity before per-destination publish is still intentional in this design; recovery rules for abandoned or partially used reservations are defined in Section 7.
    - **Tag creation logic:** If the tag does not exist, create it. If it already exists and points to the same commit, succeed as an idempotent no-op. If it exists but points to a different commit, fail immediately. There is no force path for official release tags. The existence check must query the remote protected tag namespace via `git ls-remote --tags` or the GitHub refs API; when `git ls-remote --tags` is used, annotated tags must be compared by their peeled `refs/tags/<tag>^{}` commit SHA rather than by the raw tag-object SHA. A local `git tag -l` view from checkout is insufficient and must not be the sole source of truth.
    - Checks out the current source ref read-only, then configures git explicitly to push with the minted GitHub App installation token. The job must not persist the default `GITHUB_TOKEN` as a write-capable remote credential.
    - Emits a machine-readable workflow output `tag-result` whose value is exactly `created` or `no-op`, and appends a one-line summary to `$GITHUB_STEP_SUMMARY` describing the reserved release identity, caller ref, source commit, and tag outcome.

7. **Publish jobs** (static conditional, one job per official ecosystem-destination pair):
    - Uses the same per-destination split structure as `buddy.yml`, but official targets now include `github:official` in addition to the production package registries. Unlike buddy, official publish jobs do not need to restate the full language-matching build predicate in each `if:` guard because `create-release-tag` is already gated on resolver success, static-analysis success, the exact single-build-success pattern for the resolved language, and the Section 8 provenance gate.
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag]`
    - `environment: production-<project-name>` — **mandatory**, not optional. This enables human approval gates and OIDC token issuance. Each destination still triggers its own approval step. This trades operator convenience for per-destination isolation of approvals and tokens. If reviewer fatigue becomes material later, migrate to a single reviewed gate plus destination-specific non-reviewed environments.
    - Every environment-gated publish job name and first summary line must include the target name, project name, resolved version, caller ref, `github.run_id`, and at least a 12-character source-commit prefix so concurrent approval requests for the same project are distinguishable in the approval UI and in notifications.
    - `publish-npm-official`, `publish-pypi-official`, and `publish-rubygems-official` use `permissions: { contents: read, id-token: write }` for trusted publishing so the reusable workflow can check out trusted helper code from the protected control-plane branch. `publish-nuget-official` uses `permissions: { contents: read }` plus the single explicit `NUGET_API_KEY` secret from `production-<project-name>`. `publish-github-official` uses `permissions: { contents: write }`, which already satisfies read access.
    - Because `official.yml` may run only from the protected control-plane branch set, no separate runtime assertion is required here to distinguish the caller branch from the trusted control-plane source. The production environment branch policy and branch protections carry that responsibility.
    - All official publish jobs use `secrets: {}` except `publish-nuget-official`, which passes only the named `NUGET_API_KEY` secret. Blanket secret inheritance is not allowed.
    - Each publish step uses idempotent publish logic from the protected control-plane branch set. Duplicate-version outcomes (`409`, `422`, or tool-equivalent "already exists" responses) count as success only after the workflow verifies that the already-published remote artifact set matches the local artifact set and expected digests. Authentication failures, authorization failures, malformed artifacts, and upstream `5xx` errors remain hard failures. This design intentionally does not retry upstream `5xx` failures inside a single run; operator recovery happens by re-running the workflow.

    ```yaml
    publish-nuget-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag]
        permissions:
            contents: read
        environment:
            name: production-${{ needs.resolve-context.outputs.project-name }}
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'nuget:official')
        uses: ./.github/workflows/_publish-nuget.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            checkout-ref: ${{ github.sha }}
            version: ${{ needs.resolve-context.outputs.version }}
            feed-url: https://api.nuget.org/v3/index.json
        secrets:
            NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}

    publish-npm-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag]
        permissions:
            contents: read
            id-token: write
        environment:
            name: production-${{ needs.resolve-context.outputs.project-name }}
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'npm:official')
        uses: ./.github/workflows/_publish-npm.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            checkout-ref: ${{ github.sha }}
            version: ${{ needs.resolve-context.outputs.version }}
            registry: https://registry.npmjs.org
            release-line: ${{ needs.resolve-context.outputs.release-line }}
            dist-tags: ${{ needs.resolve-context.outputs.npm-dist-tags }}
        secrets: {}

    publish-pypi-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag]
        permissions:
            contents: read
            id-token: write
        environment:
            name: production-${{ needs.resolve-context.outputs.project-name }}
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'pypi:official')
        uses: ./.github/workflows/_publish-pypi.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            checkout-ref: ${{ github.sha }}
            version: ${{ needs.resolve-context.outputs.version }}
        secrets: {}

    publish-rubygems-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag]
        permissions:
            contents: read
            id-token: write
        environment:
            name: production-${{ needs.resolve-context.outputs.project-name }}
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'rubygems:official')
        uses: ./.github/workflows/_publish-rubygems.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            checkout-ref: ${{ github.sha }}
            version: ${{ needs.resolve-context.outputs.version }}
            host: https://rubygems.org
        secrets: {}

    publish-github-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag]
        permissions:
            contents: write
        environment:
            name: production-${{ needs.resolve-context.outputs.project-name }}
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'github:official')
        uses: ./.github/workflows/_publish-github.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            checkout-ref: ${{ github.sha }}
            project-name: ${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
            tag-name: ${{ needs.resolve-context.outputs.tag-name }}
        secrets: {}
    ```

8. **`confirm-publish-state`**:
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag, publish-nuget-official, publish-npm-official, publish-pypi-official, publish-rubygems-official, publish-github-official]`
    - `if: always() && !cancelled() && !failure() && needs.resolve-context.result == 'success' && needs.static-analysis.result == 'success' && needs.require-provenance.result == 'success' && needs.create-release-tag.result == 'success'`
    - `permissions: { contents: read }`
    - Re-queries every selected official target against its live remote system and emits `confirmed-published-targets` as a compact ordered JSON array in canonical official-target order, serialized exactly as compact JSON equivalent to `jq -c`. It must also emit `partial-confirmed-published-targets` in the same serialization format, updating that output monotonically as confirmations succeed so operators retain a machine-readable checkpoint if the job later times out. `confirmed-published-targets` is authoritative only on full job success; `partial-confirmed-published-targets` is a recovery hint and never substitutes for the required full confirmation. This job is the authoritative workflow-side source of truth for `publishedTargets`; machine-readable publish outputs and step summaries are diagnostic hints only. This job intentionally does not run once an upstream selected publish job has already failed; in that case, Section 7's manual live-state reconstruction path is authoritative.
    - Remote confirmation must use bounded retry with backoff to absorb registry eventual consistency. A single immediate `not found` response after a publish attempt is `unknown`, not authoritative absence. The job must confirm selected targets in parallel; a serial confirmation loop is unsupported. Each selected target must be retried at least three times with increasing delays, use an initial delay of at least 30 seconds, a backoff multiplier of at least 2x, a per-request timeout of at most 30 seconds, and a maximum confirmation budget of 10 minutes per selected target before concluding that a version, dist-tag, release asset, or GitHub Release state is missing.
    - For `npm:official`, this job must also verify the full ordered `dist-tags` array emitted by `resolve-context`.

9. **`release-complete`**:
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, require-provenance, create-release-tag, publish-nuget-official, publish-npm-official, publish-pypi-official, publish-rubygems-official, publish-github-official, confirm-publish-state]`
    - `if: always()`
    - `permissions: {}`
    - Performs the terminal correctness check for official releases. It must first assert that `resolve-context.result == "success"`, `static-analysis.result == "success"`, `require-provenance.result == "success"`, `create-release-tag.result == "success"`, and `confirm-publish-state.result == "success"`. It must also assert that `create-release-tag.outputs.tag-result` is present and equal to `created` or `no-op`. It must then parse `targets` as JSON, assert that the filtered target set is non-empty, map that set to the exact publish jobs `{nuget:official -> publish-nuget-official, npm:official -> publish-npm-official, pypi:official -> publish-pypi-official, rubygems:official -> publish-rubygems-official, github:official -> publish-github-official}`, and assert that every selected target finished with `result == "success"` and a valid `publish-result` output in `{new-publish, no-op}`.
    - It must also assert that every non-selected publish job finished with `result == "skipped"`.
    - It must also assert that `confirm-publish-state.outputs.confirmed-published-targets` exactly equals the selected target array in canonical order.
    - If `npm:official` is selected, it must also assert that `publish-npm-official.outputs.applied-dist-tags == resolve-context.outputs.npm-dist-tags`.
    - It must also assert that the single language-matching build job finished with `result == "success"`; the three non-matching build jobs must be `result == "skipped"`.
    - The normative jq skeleton is:

    ```yaml
    - name: Assert official release completeness
      env:
          NEEDS_JSON: ${{ toJson(needs) }}
      run: |
          jq -n -e '
              (env.NEEDS_JSON | fromjson) as $n
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
                      and ($n["confirm-publish-state"].outputs["confirmed-published-targets"] == ($targets | tojson))
                      and (if (($targets | index("npm:official")) != null)
                          then $n["publish-npm-official"].outputs["applied-dist-tags"] == $n["resolve-context"].outputs["npm-dist-tags"]
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
- Removing a previously used target takes effect immediately because backward-compatibility shims are intentionally out of scope before implementation starts. For example, removing `github:official` stops GitHub Release reconciliation on subsequent official runs and leaves any existing stable release as manual state. The reviewed PR that removes a target must include a target-specific decommission checklist: for `github:official`, enumerate every existing stable Release on that identity, decide archival vs deletion, update consumer documentation, and record any follow-up issue; for production package targets, state whether the existing published package remains live, is deprecated, or is withdrawn, and record that decision in the recovery ledger when applicable. Subsequent official runs should emit a non-blocking warning when such legacy state still exists.
- Unsupported future schema versions are hard failures with operator guidance. Because implementation has not started, schema upgrades are coordinated changes rather than backward-compatible migrations.
- RubyGems versions use the repository's explicit subset policy: `MAJOR.MINOR.PATCH[.suffix...]`, no leading `v`, no `-` or `+`, suffix segments limited to `[0-9A-Za-z]+`, and every suffix segment must independently contain at least one letter. Numeric-only suffix chains such as `1.2.3.1` are rejected, and mixed chains such as `1.2.3.1.rc1` are also rejected because the segment `1` is digit-only.

**Project resolution contract:**

- `project-name` is case-sensitive, must identify exactly one project in the repository, and must reject any occurrence of `..`, any trailing `.`, and any `.lock` suffix for ref safety.
- Releasable `project-name` values must be unique under ASCII lowercase normalization across the repository so workflow concurrency keys cannot alias distinct projects.
- Repository policy must include a CI validation that scans all candidate project roots and hard-fails if two candidate roots collide under ASCII lowercase normalization. For this validation, a candidate project root is any directory whose basename is exactly the candidate `project-name` and whose contents resolve to exactly one workflow language in `{csharp, python, jsts, ruby}`, regardless of whether its `release.json` is missing or invalid.
- Repository policy must also include a CI validation that scans releasable project roots and hard-fails if any such root resolves to more than one workflow language, enforcing the single-language project scope before any release workflow is invoked.
- Project resolution is performed from the repository root by exact leaf-directory-name match: a candidate project root is a directory whose basename is exactly `project-name` and whose contents resolve to exactly one workflow language in `{csharp, python, jsts, ruby}`.
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
| `github:official`   | Official   | `official.yml` | Create or update a stable GitHub Release with downloadable assets |

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
| `require-provenance` | `boolean` | No | When `true`, the build workflow must emit the official provenance artifact and fail if the required attestation set cannot be produced; defaults to `false` and is valid only when `build-scope = release` |

| Output          | Type     | Description                                                     |
| --------------- | -------- | --------------------------------------------------------------- |
| `artifact-name` | `string` | Name of the uploaded CI Artifact: `build-output-<project-name>` when `build-scope = release`, else empty |
| `provenance-artifact-name` | `string` | Name of the uploaded CI Artifact containing official provenance material: `build-provenance-<project-name>` when `build-scope = release` and `require-provenance = true`, else empty |

**Required caller permissions:** `contents: read`; callers that set `require-provenance: true` must additionally request `id-token: write` and `attestations: write`

**Mode contract:** `ci.yml` is the only caller that uses `build-scope: ci`, and in that mode the reusable workflow must run the language-wide CI suite for the current checkout, must not upload release artifacts, and must reject `require-provenance: true`. `buddy.yml` and `official.yml` must use `build-scope: release` and must provide both `project-path` and `project-name`.

**Checkout behavior:** Build-test workflows perform their own checkout and must use `fetch-depth: 0` internally so NBGV and other git-history-derived metadata resolve correctly. These read-only checkouts must also use `persist-credentials: false`. When `checkout-ref` is provided, the reusable workflow must check out exactly that ref; when it is omitted, the reusable workflow must check out the caller job's `github.sha`. Buddy and official callers may pass the dispatch commit SHA explicitly for clarity, but the default behavior already targets the current workflow commit.

**Secrets:** `secrets: {}` — build-test workflows require no secrets. Callers must not pass `secrets: inherit` to avoid exposing publish credentials to build/test execution.

**Dependency lock enforcement:** Build-test workflows must treat project dependency lockfiles as mandatory control-plane inputs, not advisory metadata. The reusable workflow for the resolved ecosystem must hard-fail if the required lockfile is absent and must install in strict locked mode: C# uses restore lock enforcement such as `dotnet restore --locked-mode` (or an equivalent reviewed MSBuild property form); JavaScript/TypeScript uses `pnpm install --frozen-lockfile`; Python uses `uv sync --locked`; Ruby uses Bundler frozen mode against `Gemfile.lock`. A build that would regenerate or ignore a lockfile is invalid for this design.

**Artifact convention:** Each build workflow uploads its output to CI Artifacts with the name `build-output-<project-name>`. Publish workflows download by this exact name. Because rerun-based recovery is a first-class path in Section 7 and artifact names are deterministic within a run, every `actions/upload-artifact` invocation in build workflows must set `overwrite: true` for both the main build artifact and the provenance artifact. The artifact layout per ecosystem:

| Ecosystem | Expected artifact contents                                                                                                                                                       |
| --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| NuGet     | One or more `.nupkg` files, and optionally matching `.snupkg` symbol packages, whose manifest `publishRoles` include `package`                                                 |
| npm       | One `.tgz` tarball (output of `npm pack` / `pnpm pack`) whose manifest `publishRoles` include `package`                                                                         |
| PyPI      | One `.whl` and one `.tar.gz` (wheel + sdist) whose manifest `publishRoles` include `package`                                                                                    |
| RubyGems  | One `.gem` file whose manifest `publishRoles` include `package`                                                                                                                  |
| GitHub    | Any top-level file whose manifest `publishRoles` include `github-release-asset`; files may also carry `package` when the same artifact should be published to both surfaces     |

Every build artifact must also contain a manifest file at the artifact root named exactly `artifact-manifest.json` that lists each published file, its SHA-256 digest, and the publish roles for which that file is intended. That manifest is internal workflow metadata and must not be uploaded as a GitHub Release asset. Publish workflows must verify the downloaded files against that manifest before any publish step runs. The manifest schema is fixed and shared across ecosystems:

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

`schemaVersion` must equal `1`. `files` must be a non-empty array. Each `path` must be a relative path to a top-level artifact file, must not contain `/` or `\`, and must not contain any ASCII control character (`U+0000` through `U+001F` or `U+007F`); each `sha256` must match exactly `[0-9a-f]{64}`, and each `publishRoles` value must be a non-empty array of unique strings from the closed set `{package, github-release-asset}`. Every publish workflow must reject nested paths or control-character file names at manifest-validation time rather than surfacing a later file-not-found error. Package-registry publish workflows operate only on manifest entries whose `publishRoles` include `package`. `_publish-github.yml` operates only on manifest entries whose `publishRoles` include `github-release-asset`. A file may carry both roles when the same artifact should be published both as a package and as a GitHub Release asset.

The validator must enforce this schema strictly. No top-level keys other than `schemaVersion` and `files` are allowed, and no file-entry keys other than `path`, `sha256`, and `publishRoles` are allowed.

When `require-provenance = true`, the build workflow must also upload a second CI artifact named `build-provenance-<project-name>` and expose that name via `provenance-artifact-name`. That artifact must contain the full attestation set plus a durable identity record named exactly `artifact-evidence.json`. `artifact-evidence.json` is internal control-plane metadata rather than a GitHub Release asset and uses a fixed exact schema:

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
    "attestationType": "github-artifact-attestation",
    "verificationTool": "gh attestation verify",
    "verifiedRepository": "hcoona/three",
    "verifiedRef": "refs/heads/main",
    "verifiedSourceSha": "0123456789abcdef0123456789abcdef01234567",
    "verifiedJobWorkflowRef": "hcoona/three/.github/workflows/_build-test-jsts.yml@refs/heads/main",
    "verifiedWorkflowSha": "89abcdef0123456789abcdef0123456789abcdef",
    "verifiedRepositoryOwner": "hcoona"
}
```

No top-level keys other than those shown above, plus optional `verifiedEnvironment` when the attested build job executed inside a GitHub environment and the verifier surfaced that claim, are allowed. `schemaVersion` must equal `1`. `workflowRunAttempt` must equal the positive integer value of `github.run_attempt` for that evidence-producing run. `artifactManifestSha256` must match exactly `[0-9a-f]{64}`. `verifiedRepository` must be the canonical `owner/repo` string for the run. `verifiedRef`, `verifiedSourceSha`, `verifiedJobWorkflowRef`, `verifiedWorkflowSha`, and `verifiedRepositoryOwner` must match the attestation verification result exactly. `verifiedEnvironment` is optional and must be absent rather than `null` when the attested build job did not execute inside a GitHub environment or when the verifier does not surface that claim. The later `require-provenance` job must copy that evidence record to the protected durable evidence branch described in Section 8 and must emit the workflow output `artifact-manifest-evidence-url`; the recovery ledger field `artifactManifestEvidenceUrl` then records that same immutable permalink. An expiring GitHub Actions artifact URL is never a valid durable evidence reference.

**Reproducibility requirement:** Build workflows must configure their packaging tools so reruns from the same source commit and lockfiles produce the same package-file identities. Where a package format embeds timestamps, file ordering, or host-specific metadata by default, the reusable build workflow must normalize those fields before publishing artifacts.

**Artifact retention:** CI artifacts are an ephemeral hand-off mechanism, not permanent release storage. Recommended defaults: `retention-days: 7` for PR and buddy runs, `retention-days: 90` for official runs. The longer official retention window is intentional recovery budget for partial publishes, long-lived approval waits, and post-incident evidence comparison, and it deliberately exceeds GitHub's documented 30-day workflow-rerun limit by a full additional 60 days. It does not eliminate the dead-end case where artifacts expire and the protected branch has since moved, so Section 7 still defines that as a separate recovery boundary.

### Publish Workflows

All publish workflows share a common set of inputs, with ecosystem-specific additions:

| Input           | Type     | Required | Description                                    |
| --------------- | -------- | -------- | ---------------------------------------------- |
| `artifact-name` | `string` | Yes      | CI Artifact name to download (from build step) |
| `checkout-ref`  | `string` | Yes      | Exact caller commit SHA that the publish workflow must check out before running trusted helper code |
| `version`       | `string` | Yes      | Package version string                         |

**Ecosystem-specific inputs:**

| Workflow                | Input          | Type      | Required | Description                                                                                                    |
| ----------------------- | -------------- | --------- | -------- | -------------------------------------------------------------------------------------------------------------- |
| `_publish-nuget.yml`    | `feed-url`     | `string`  | Yes      | NuGet feed URL (GPR or NuGet.org)                                                                              |
| `_publish-npm.yml`      | `registry`     | `string`  | Yes      | npm registry URL (GPR or npmjs)                                                                                |
| `_publish-npm.yml`      | `release-line` | `string`  | npmjs only | Resolved release line such as `1.2.x` used to validate maintenance-line tag semantics |
| `_publish-npm.yml`      | `dist-tags`    | `string`  | Yes      | Ordered JSON array of explicit npm dist-tags to write (`["buddy"]`, `["latest"]`, `["release-v1.2"]`, or `["release-v1.2-rc"]`) |
| `_publish-pypi.yml`     | (none extra)   |           |          | Always publishes to PyPI via trusted publishing                                                                |
| `_publish-rubygems.yml` | `host`         | `string`  | Yes      | RubyGems host URL (GPR or RubyGems.org)                                                                        |
| `_publish-github.yml`   | `project-name` | `string`  | Yes      | Project name, used for deterministic GitHub Release titles and diagnostics                                     |
| `_publish-github.yml`   | `tag-name`     | `string`  | Yes      | Git tag for the GitHub Release                                                                                 |

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
| `_publish-nuget.yml`    | `contents: read` plus `packages: write` (GPR) or explicit `NUGET_API_KEY` secret (NuGet.org) |
| `_publish-npm.yml`      | `contents: read` plus `packages: write` (GPR) or `id-token: write` |
| `_publish-pypi.yml`     | `contents: read` plus `id-token: write`                            |
| `_publish-rubygems.yml` | `contents: read` plus `packages: write` (GPR) or `id-token: write` |
| `_publish-github.yml`   | `contents: write`                            |

**Secrets:** `secrets: {}` by default. `publish-nuget-official` is the one exception in this design and must receive exactly one named secret `NUGET_API_KEY` from `production-<project-name>`. Every other publish path uses either `GITHUB_TOKEN` or trusted publishing. `secrets: inherit` is prohibited.

**JSON output serialization:** Any reusable workflow output in this design whose contract says "JSON array" must emit compact canonical JSON equivalent to `jq -c`, not pretty-printed JSON with discretionary whitespace.

**Checkout behavior:** Publish workflows that execute trusted helper code from the protected control-plane branch must perform their own read-only checkout with `persist-credentials: false` and must check out exactly the supplied `checkout-ref`. Buddy and official callers must pass `${{ github.sha }}` explicitly; omission is unsupported in this design. Package-registry publish workflows therefore require caller `contents: read` in addition to their registry-specific write scope. `_publish-github.yml` already satisfies that requirement through `contents: write`.

**GitHub Release runner contract:** `_publish-github.yml` must run on `ubuntu-latest`. Windows runners are unsupported for this workflow because the public `SHA256SUMS` asset in this design is byte-exact GNU `sha256sum` output with `LC_ALL=C` sorting and LF-only line endings.

Local reusable publish workflows are not authorization boundaries by themselves. They rely on `CODEOWNERS`, the protected control-plane branches, repository-policy linting that restricts same-repo callers, and project-scoped `production-*` environments to keep publisher credentials from being minted by unauthorized workflows in the same repository.

**Artifact validation:** Before publishing, each reusable publish workflow must verify that the expected files exist at the artifact root and fail on empty artifacts, missing required files, or ambiguous layouts. Duplicate-version outcomes count as idempotent success only when the remote artifact set matches the local artifact set and expected digests. Package-registry publish workflows must validate only manifest entries whose `publishRoles` include `package`. `_publish-github.yml` must validate only manifest entries whose `publishRoles` include `github-release-asset`, must verify that at least one such top-level non-manifest file exists in the downloaded artifact, and must fail if release assets are nested under subdirectories instead of flattened at the artifact root. Validation must also reject symlinks, absolute paths, or any archive entry that escapes the artifact root when extracted. After any successful registry mutation or duplicate-version response, the reusable workflow must re-query the remote state with bounded retry and backoff before declaring success; an immediate read that still reports `not found` is `unknown`, not authoritative absence.

For GitHub Releases, remote identity comparison is role-filtered and metadata-aware: the expected asset set is exactly the manifest entries whose `publishRoles` include `github-release-asset`; internal workflow metadata such as `artifact-manifest.json` is never part of that comparison; workflow-derived consumer assets such as `SHA256SUMS` are excluded from the expected manifest set and ignored when comparing remote assets to local build output. A rerun may repair a strict remote subset state when every already-present remote asset matches the current local digest and only expected assets are missing; any mismatched existing asset digest is a hard failure.

For GPR targets, publish workflows must treat package versions as immutable within workflow execution. Even though GitHub supports package deletion and restoration with elevated package-admin capabilities, these reusable publish workflows do not request those permissions and must never delete package versions as part of a retry or recovery path.

**GitHub publish tag contract:** `_publish-github.yml` is official-only in this design. It must hard-fail unless `tag-name == 'release/<project-name>/v<version>'` and the release title is exactly `<project-name> v<version>`. Because GitHub cannot restrict same-repository callers of a local reusable workflow by workflow path, `_publish-github.yml` is not an authorization boundary by itself; its safety relies on `CODEOWNERS` over `.github/workflows/**`, the protected `release/**` tag namespace, and the deterministic tag/title contract.

For GitHub Release consumers, `_publish-github.yml` must also derive a deterministic public checksum asset such as `SHA256SUMS` from the subset of `artifact-manifest.json` entries whose `publishRoles` include `github-release-asset`, and upload it alongside the release assets. `artifact-manifest.json` remains internal workflow metadata; the public checksum asset is a consumer-facing integrity aid that complements, rather than replaces, the attestation-based provenance gate. The checksum file format must be the GNU coreutils `sha256sum` format `<64-hex-digest><two spaces><filename>` so consumers can validate it with standard tooling. Generation is part of the contract: the input file set must be sorted by bytewise ascending file name (`LC_ALL=C` semantics), emitted with LF line endings only, escape file names exactly as GNU `sha256sum` does, and exclude `SHA256SUMS` itself plus any non-`github-release-asset` entry from the checksum input set. Because many downstream consumers do not implement GNU escape parsing correctly, compliant release assets in this design must never require escaped control characters in `SHA256SUMS`; the manifest-level control-character filename ban is therefore part of the checksum contract, not a separate optional hygiene rule.

**npm dist-tag policy:** `_publish-npm.yml` must use the explicit `dist-tags` input on every publish and must hard-fail before any registry mutation if that input is missing, empty, not valid JSON, or structurally invalid for its own inputs and target registry. The reusable workflow must distinguish GPR from npmjs via `registry`. For GPR publishes, the contract is simple: `dist-tags` must be exactly `["buddy"]`, and `release-line` is not required. For npmjs publishes, `release-line` is required and the reusable workflow must derive the caller ref from the runtime `github.ref` context rather than from any caller-supplied input, then validate the ordered array against that derived ref, `version`, `release-line`, and `registry`, not just against registry shape. It must enforce that npmjs publishes do not use `buddy`, reject reserved channel tokens, and apply the forward-only SemVer precedence checks below. Full derivation of the correct npmjs tag array from caller ref, release line, and prerelease channel remains the responsibility of `resolve-context` in `official.yml`, which passes the already-derived deterministic `dist-tags` into `_publish-npm.yml`; `_publish-npm.yml` must then verify that those tags are semantically consistent with the runtime `github.ref` and the supplied `release-line`. Buddy publishes to GPR must use `["buddy"]` and must never write `latest`. Official stable npmjs publishes from `main` must use `["latest"]`. Official prerelease npmjs publishes from `main` must use the prerelease channel tag derived from that validated first prerelease identifier and must never write `latest`. Official stable npmjs publishes from maintenance branches must use `["release-v<major>.<minor>"]`. Official prerelease npmjs publishes from maintenance branches must use `["release-v<major>.<minor>-<channel>"]` and must never append `latest`. For the extracted prerelease channel token, values such as `latest`, `buddy`, `release`, any token beginning with `release-v`, and any token beginning with `v` are invalid. For the final tag array, values beginning with `release-v` are valid only when produced by the maintenance-branch derivation rules above. For the forward-only rules below, the deterministic tag family is defined by the exact requested tag form: `latest` is the stable-mainline family; a bare prerelease channel such as `rc` or `beta` is the mainline prerelease family for that exact channel token; `release-v<major>.<minor>` is the maintenance stable family for that exact release line; and `release-v<major>.<minor>-<channel>` is the maintenance prerelease family for that exact release line and exact channel token. Publication chronology is not a substitute for semantic version comparison. SemVer precedence comparisons in `_publish-npm.yml` must use a standards-compliant SemVer comparator such as the canonical `semver` library rather than lexicographic comparison or ad hoc parsing.

`_publish-npm.yml` must treat tarball idempotency and dist-tag idempotency as separate checks. When the package version already exists and the remote tarball identity matches the local artifact set, the workflow must still query the current owner of every requested tag in `dist-tags`. If a requested tag already points to the same version, preserve it and continue. If a requested tag is absent, attach it to the same version. If a requested tag points to a different version, the workflow may move that tag forward only when the current run's version has higher SemVer precedence within that same deterministic tag family; it must never move `latest`, a prerelease channel tag, or a maintenance-line tag backward or sideways to an older or unrelated version. Any attempted rewind is a hard failure, not a silent rewrite. `_publish-npm.yml` must emit the exact validated ordered JSON array through the `applied-dist-tags` output, and official `release-complete` must compare that output to the deterministic tag array derived in `resolve-context`.

**GitHub release identity metadata and scan completion:** `_publish-github.yml` must use deterministic release titles. For official stable releases, the title must be `<project-name> v<version>`. Any GitHub Release scan used by this design, whether in `resolve-context` or in `_publish-github.yml`, must follow pagination until the relevant result set is exhausted and must hard-fail on API, authentication, authorization, rate-limit, transport, or response-shape errors. An interrupted, truncated, or otherwise incomplete scan is `unknown`, not `not found`; overwrite, no-op, and conflict decisions must never be made from a partial page. `_publish-github.yml` must repeat the deterministic stable-title conflict scan immediately before it mutates any GitHub Release record or release asset, and must hard-fail if the same deterministic stable title exists under a different tag or commit than the current official release identity.

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
| Non-pre-release GitHub Release exists for the derived official tag with matching remote artifact identity  | **Success** (idempotent no-op)                                                        |
| Non-pre-release GitHub Release exists for the derived official tag with different remote artifact identity | **Hard fail** — release assets must not silently diverge from the local build output    |
| Non-pre-release GitHub Release exists for the same deterministic stable title `<project-name> v<version>` but different tag/commit | **Hard fail** — stable releases in that stable identity space must not be rebound to a different release identity |
| Pre-release GitHub Release exists for the derived official tag with matching remote artifact identity | **Replace with stable release** using the current local build output for that tag            |
| Pre-release GitHub Release exists for the derived official tag with different remote artifact identity | **Hard fail** — official stable promotion must not overwrite a divergent same-tag pre-release |
| Package version already exists at official registry with matching remote artifact identity         | **Success** (idempotent publish scripts; for npm, requested dist-tag state must also pass the explicit policy below, and every such success still requires bounded remote re-check for eventual consistency) |
| Package version already exists at official registry with different remote artifact identity        | **Hard fail** — cut a new version; official registry versions are immutable release identities |
| Authn/authz failure or upstream `5xx` at official registry                                         | **Hard fail** — not idempotent                                                                 |

For official npm publishes, remote tarball identity alone is not sufficient for idempotent success. `_publish-npm.yml` must also evaluate every requested entry in the ordered `dist-tags` array. A requested tag that already points to the same version is `no-op`; a missing requested tag may be attached to that same version; a requested tag that points to a different version may be advanced only when the current run's version has higher SemVer precedence in that same deterministic tag family. Any retarget that would move `latest`, a prerelease channel tag, or a maintenance-line tag backward is a hard failure.

### Recovery Playbook

If a workflow run fails partway through (for example `nuget:gpr` succeeds but `npm:gpr` fails), use the first matching recovery path below and do not mix strategies:

1. If `preflight-check` fails, treat it as an environment or control-plane configuration issue rather than a source-code defect: fix whichever preflight invariant failed, including the required reviewers on `production-<project-name>`, `prevent_self_review = true`, exact deployment branch names only, the required maintenance-branch protection profile, active branch/tag rulesets, and Rulesets API read-credential sufficiency, then trigger a new run.
2. If execution fails before any publish job starts in **buddy** (for example `resolve-context`, `static-analysis`, or build failure), fix the repository or configuration issue and trigger a fresh buddy workflow dispatch. No remote release state has been mutated yet.
3. If execution fails before any publish job starts in **official** and `resolve-context` never finished successfully, fix the repository or configuration issue and trigger a fresh official workflow dispatch from the intended protected branch. If the failure happened during publish trust inventory preflight, reconcile `.github/publish-trust-inventory.json`, the selected caller ref, the expected reusable publish workflow paths, and the expected target auth mechanisms on that control-plane branch before retrying. No remote official release state has been mutated yet.
4. If `resolve-context` succeeded but `static-analysis` or a build job later failed in **official** before `create-release-tag` succeeded, fix the source on the appropriate protected control-plane branch or supported maintenance branch and trigger a fresh official workflow dispatch. No official release tag or remote publish state has been mutated yet.
5. Distinguish between **Re-run jobs** and a fresh **workflow_dispatch**. Before choosing a rerun path, first verify in the GitHub Actions run UI or API that the original run's artifacts still exist, then check the two documented GitHub lifetime boundaries on the original run: GitHub Actions rerun availability is 30 days from the initial run, and artifact retention follows the configured retention window. These timers are independent. A retained artifact does not keep an expired run rerunnable, and a still-rerunnable run does not guarantee that operators should continue waiting indefinitely on pending approvals. GitHub reruns also execute the original workflow snapshot from the original run's ref; they do not pick up later fixes to workflow files, reusable workflows, or helper scripts on the branch. GitHub publicly documents environment wait-timer bounds but does not publicly document a separate approval-request expiry, so this design treats approval waiting time as an operational threshold tracked against the enclosing run's 30-day rerun deadline rather than as an independent GitHub expiry clock. After run expiry, do not use GitHub's Re-run button. Any same-identity rebuild attempt after run expiry or artifact expiry must still satisfy step 10's durable-evidence rule before a fresh dispatch is allowed. If an approval has been pending long enough that operators no longer trust the run context, audit the official tag and publish state first, then choose recovery under the later rules below.
6. If the failure is transient (network issue, auth outage, or upstream `5xx`), prefer **Re-run failed jobs** on the same workflow run so the original commit, derived version, and derived official tag remain unchanged, but only when every remaining selected target is in a failed state. If any required selected job is `cancelled`, `skipped`, `requested`, `waiting`, or waiting for approval, use **Re-run all jobs** instead. Reviewer decline, operator cancellation, and any other path that settles as cancellation rather than a normal failed-job subset therefore require **Re-run all jobs**. On any rerun, every job gated by `production-evidence-write-<project-name>`, `production-tag-write-<project-name>`, or `production-<project-name>` re-enters the approval queue as a fresh request; no prior approval carries forward. This rerun guidance applies only while the original run is still rerunnable under step 5's lifetime limits and while no workflow-code fix is required. Deterministic artifact uploads must use `overwrite: true`, so reruns are expected to replace same-run artifact names cleanly rather than fail on name collisions. If workflow logic, reusable workflow wiring, trusted helper code, or the checked-in trust inventory had to be fixed, use a fresh dispatch instead. Before any fresh buddy or official dispatch for the same project/ref concurrency group, inspect that group's queued and in-progress runs and cancel stale queued runs rather than assuming the queue will preserve operator intent. A fresh official workflow dispatch is valid only when the selected protected branch still points to the same commit as the original run; otherwise it is a new release attempt and must be treated as such. Matching already-published artifacts must settle as idempotent no-ops. Each official run must surface the current wait age of every pending `production-evidence-write-<project-name>`, `production-tag-write-<project-name>`, and `production-<project-name>` approval, plus the run-rerun and artifact-retention deadlines, in its step summary so operators do not have to reconstruct those timers manually.
7. If official publish jobs partially succeeded because some destinations were approved and others were declined or failed transiently, rerun the same official workflow run whenever possible and only while step 5 still permits reruns. Already-published destinations must settle as idempotent no-ops, and the remaining destinations will request fresh approval. If the partial-failure path includes a declined environment approval or any other approval path that settled as cancellation, use **Re-run all jobs** rather than **Re-run failed jobs**, following step 6's cancellation-path rule. If the official tag was already created but all later approvals were declined or the run was cancelled, rerun the same workflow run or dispatch the same protected branch again while it still points to the same commit; the tag reservation must settle as an idempotent no-op. For npm specifically, reruns must reuse the same deterministic tag array derived from the version channel and release line: stable mainline releases keep `["latest"]`, prerelease mainline releases keep their prerelease channel tag array, stable maintenance-line releases keep `["release-v<major>.<minor>"]`, and maintenance-line prerelease releases keep `["release-v<major>.<minor>-<channel>"]`. Reruns must never move any npm dist-tag backward. Do not retire or decommission that source branch until the partial-publish state has either been completed successfully or explicitly declared burned.
8. If an official tag reservation is no longer wanted after approvals were declined or after a maintenance-branch retirement cancelled the run, release engineering must resolve that explicitly rather than leaving an orphaned tag behind. Before deleting the tag, first determine the actual remote publish state by live inspection, not by job summaries alone. If `publishedTargets = []`, write an incident ledger entry with `pendingTargets` set to the original selected official targets in canonical order and use `disposition = "open-before-publish"` when the identity is still under investigation or completion remains under consideration, or `disposition = "abandoned-before-publish"` when operators intentionally stop before any official target was published. If some but not all official targets were already published, write either `disposition = "open-partial-publish"` when completion is still under consideration or `disposition = "abandoned-after-partial-publish"` when operators intentionally stop completing the remaining targets. The only supported abandon path is then manual deletion of `release/<project-name>/v<version>` by a member of the dedicated release-engineering group explicitly configured as a bypass actor on the active `refs/tags/release/**` tag ruleset, followed by a fresh official release attempt from a later intended-release commit on an active protected branch so the workflow derives a different release identity. Silent abandonment of orphaned official tags is unsupported.
9. If official `resolve-context` fails because a non-pre-release GitHub Release, including a draft, already occupies the deterministic stable title `<project-name> v<version>` under a different tag or commit, stop rerunning immediately. A draft stable release is part of the same stable identity space as a published stable release and blocks the new official attempt by design. Release engineering must either preserve that existing stable identity and cut a different version from a corrected commit, or explicitly delete the conflicting release identity before rerunning. Deleting a conflicting **draft** stable release is a normal recovery action. Deleting a conflicting **published stable** release requires an explicit consumer-impact review first, because that action removes a publicly visible production artifact set. That review must be tracked in a dedicated incident or follow-up issue, must be approved by at least two humans with one approver distinct from the operator requesting deletion, must evaluate consumer impact such as download volume and known dependents, and must define any registry-side yank, unlist, or deprecate action that should happen before deletion. The same review must explicitly assess every already-published registry target for that conflicting release identity and decide whether each one remains live, is deprecated, or is withdrawn before the GitHub Release deletion proceeds. Those decided registry-side actions must be completed and reflected in `.github/release-recovery-ledger.jsonl` before deleting the conflicting published stable GitHub Release or rerunning the workflow; use `withdrawnTargets` for targets that were removed from normal availability and `operatorRationale` for reviewed targets intentionally left live. Unless the release is younger than one hour and has zero observed downloads, the review must impose a minimum 48-hour hold period before deleting the published stable release. That hold begins when the dedicated incident or follow-up issue is opened, and the ledger entry for this path must record `holdStartedAt`, `eligibleDeleteAt`, and `consumerImpactEvidenceUrl` before the deletion proceeds. If the younger-than-one-hour and zero-download exception is used, the ledger entry must keep `holdStartedAt` and `eligibleDeleteAt` absent, must still record `consumerImpactEvidenceUrl`, and must also record `holdExceptionBasis` with the reviewed evidence. In either case, any associated `release/<project-name>/v<version>` tag must be reconciled using the same authorized tag-deletion mechanism from step 8, but the ledger disposition for this path must match the actual already-published state rather than reusing `abandoned-before-publish`. Renaming the GitHub Release title to sidestep the deterministic-title guard is unsupported.
10. If artifacts expired for an official run but the selected protected branch still points to the same commit, trigger a fresh official workflow dispatch from that same branch only when a durable evidence record from the original run still exists and can be compared against the rebuilt output. That durable evidence record is the repository-controlled immutable copy referenced by `artifactManifestEvidenceUrl`; an expiring GitHub Actions artifact URL is not sufficient. This is the authoritative rule for the 31-90 day window where the run may be expired but official artifacts still exist. If that evidence is unavailable, treat the identity as burned rather than rebuilding blindly. If artifacts expired and the protected branch has already moved to a different commit, stop trying to complete the old partially published identity. Treat that earlier version as burned or partially released, apply the registry-specific withdrawal action defined below to any already-published artifacts whose continued availability is no longer acceptable, record those actions in the ledger's `withdrawnTargets` field, fix the source on the correct branch, and continue with the next version derived from the corrected commit.
11. If `confirm-publish-state` exhausts its retry budget without proving the complete selected target set, or if it never started because an upstream selected publish job already failed, stop using blind reruns as the default response. First record any emitted `partial-confirmed-published-targets` output as a recovery hint, then reconstruct live remote state for every selected target, the official tag, and the GitHub Release using the same canonical target order and evidence rules as `confirm-publish-state`, then record or update the incident in `.github/release-recovery-ledger.jsonl` before deciding whether the identity is still recoverable. Manual reconstruction is not free-form: use the same registry APIs the workflow-side checks use, namely the NuGet search or package-content API for `nuget:official`, `npm view <package>@<version> --json` or the npm registry metadata API for `npm:official`, the PyPI JSON API `/pypi/<project>/<version>/json` for `pypi:official`, the RubyGems versions API for `rubygems:official`, and the GitHub Releases API for `github:official`. Evidence must be recorded as a timestamped permalink or captured response URL in the ledger. If live inspection shows every selected destination actually converged and the failure was only bounded-read eventual consistency, fix the retry budget or polling logic on the protected control-plane branch before the next run. If live inspection shows real divergence or an incomplete publish, continue under the later recovery rules based on that observed state rather than on the failed job conclusion alone.
12. If `release-complete` fails because a selected publish job was skipped, an unexpected non-selected job ran, the canonical target ordering or JSON shape is wrong, or any other target-to-job mapping assertion failed, stop rerunning immediately. Treat that as workflow wiring drift or other control-plane code defect, fix the workflow via the normal protected-branch review path, and only then dispatch again. If any official destination already published before this failure was detected, record or update the incident in `.github/release-recovery-ledger.jsonl` before considering the response operationally closed. When reconstructing `publishedTargets`, use live remote inspection equivalent to `confirm-publish-state`; machine-readable publish outputs and step summaries are diagnostic hints only. If `confirm-publish-state` completed successfully before `release-complete` failed, its `confirmed-published-targets` output already satisfies the live-inspection requirement for `publishedTargets`; a second manual inspection is required only if later evidence suggests that the confirmation budget itself was insufficient. `publishedTargets` means destinations where the official artifact is now fully present at the registry, regardless of whether that presence came from `new-publish` in this run or `no-op` from a prior run.
13. If the failure is caused by malformed build output or a remote artifact identity mismatch, stop retrying the same release identity. For buddy, if a GPR package version already exists with different artifact identity, cut a new version rather than deleting and republishing. For official, if the immutable official release tag was already created for that failed attempt, do not retarget it. Fix the source on the correct protected branch and run official again so the workflow derives a new release identity from the corrected commit. If the corrected commit still resolves to the same version and the old `release/<project-name>/v<version>` tag already points to the failed commit, do not keep retrying blindly: either bump the version on the protected branch or explicitly abandon the burned identity through step 8's authorized tag-deletion path before the next official dispatch.
14. If official publish jobs fail with authentication or authorization errors immediately after a new maintenance branch, trusted workflow path, protected control-plane branch change, or explicit registry-secret rotation was introduced, diagnose the mismatch direction explicitly. If repository-side publish trust inventory preflight fails first, fix `.github/publish-trust-inventory.json` or roll back the external auth configuration change before retrying. If publish trust inventory preflight succeeds but publish still fails at the registry, verify and restore the external auth configuration for the expected selector or secret model: for npmjs verify the calling workflow path, environment, documented audience `npm:registry.npmjs.org`, and the deterministic dist-tag choice; for PyPI verify the configured workflow path, environment, and documented audience `pypi`, while treating reusable-workflow binding as a pre-implementation validation dependency rather than an externally guaranteed fact; for RubyGems.org verify the configured workflow path and environment; for NuGet.org verify the environment secret name, scope, and active API key. Treat both cases as control-plane configuration drift, not as a package-content defect. The repository must maintain a registry-specific rollback runbook for NuGet.org, npmjs, PyPI, and RubyGems.org that records the exact UI path, API call, or support-escalation path used to remove, rotate, or restore the relevant trusted-publisher selector or explicit credential for each registry.
15. If `create-release-tag` pushed the official tag and the runner crashed before the job result was recorded, rerun the same workflow run. Tag creation must settle as an idempotent no-op, after which the remaining official publish jobs can continue through the normal approval and idempotency flow.

The repository must maintain a durable recovery ledger at `.github/release-recovery-ledger.jsonl`, outside ephemeral workflow logs, for every burned, withdrawn, or partially published official release identity and for every required periodic tag audit. Each line must be a standalone JSON object with `schemaVersion: 1` and `recordType` in `{incident, audit}`. `incident` records must contain `schemaVersion`, `recordType`, `incidentId`, `recordedAt`, `projectName`, `version`, `reservedTag`, `sourceCommit`, `evidenceUrl`, `attemptScope`, `disposition`, `operatorRationale`, `selectedTargets`, `publishedTargets`, `pendingTargets`, `tagState`, `githubReleaseState`, and optional `workflowRunUrl`, `runAttempt`, `withdrawnTargets`, `followUpIssue`, `followUpStatus`, `closedAt`, `artifactManifestEvidenceUrl`, `holdStartedAt`, `eligibleDeleteAt`, `consumerImpactEvidenceUrl`, `holdExceptionBasis`. `audit` records must contain `schemaVersion`, `recordType`, `recordedAt`, `evidenceUrl`, `attemptScope`, `scope`, `result`, `operatorRationale`, and optional `runAttempt`, `followUpIssue`, `followUpStatus`, `closedAt`, `automationId`, and `scriptVersion`. `evidenceUrl` is the canonical evidence reference and must point either to a workflow run URL or to a non-workflow evidence permalink such as an issue, PR, or audit-log entry. `incidentId` must be a UUIDv4 string. Any later ledger line that corrects, closes, or otherwise continues the same incident must reuse the same `incidentId`; unrelated incidents must use new IDs. `attemptScope` must use the closed set `{single-run-attempt, no-single-run-attempt}`. `workflowRunUrl` and `runAttempt` are required when `attemptScope = single-run-attempt` and must both be absent when `attemptScope = no-single-run-attempt`. `disposition` must use the closed set `{open-before-publish, open-partial-publish, recovered, burned, abandoned-before-publish, abandoned-after-partial-publish, fully-withdrawn}`. `selectedTargets`, `publishedTargets`, `pendingTargets`, and `withdrawnTargets` must each be ordered JSON arrays of unique values drawn from the official target set `{nuget:official, npm:official, pypi:official, rubygems:official, github:official}` using that exact canonical order. For every incident record, `selectedTargets` must be non-empty, `publishedTargets` and `pendingTargets` must be disjoint, and `publishedTargets ∪ pendingTargets` must equal `selectedTargets`; partial ledgers that omit skipped-or-still-pending selected targets are invalid. `open-before-publish` requires `publishedTargets = []` and `pendingTargets = selectedTargets`. `open-partial-publish` requires both `publishedTargets` and `pendingTargets` to be non-empty. `recovered` requires `pendingTargets = []`. `abandoned-before-publish` is reserved for identities with `publishedTargets = []` and `pendingTargets = selectedTargets`. `abandoned-after-partial-publish` is reserved for identities where both `publishedTargets` and `pendingTargets` are non-empty and operators intentionally stopped completing the remaining targets. `fully-withdrawn` is reserved for a previously published identity whose remaining live targets were all later removed from normal consumer availability; it requires `pendingTargets = []`, `publishedTargets` to be non-empty, and `withdrawnTargets` to equal `publishedTargets`. When present, `withdrawnTargets` must be a subset of `publishedTargets` and records the targets that were later withdrawn, unlisted, yanked, deprecated, or otherwise removed from normal consumer availability after publication; it does not require proof of permanent deletion except where a registry-specific rule makes deletion irreversible. `artifactManifestEvidenceUrl` is required whenever the incident occurs after an official build artifact was produced, must point to the durable repository-controlled copy of the official build evidence record rather than to an expiring CI artifact URL, and is mandatory before any same-identity rebuild is attempted after artifact expiry. `tagState` must use the closed set `{not-created, created-at-source-commit, created-at-different-commit, manually-deleted}`. `githubReleaseState` must use the closed set `{absent, draft-prerelease-same-identity, published-prerelease-same-identity, draft-stable-same-identity, published-stable-same-identity, conflicting-other-identity, manually-deleted}`. `audit.scope` must use the closed set `{full-release-tag-namespace, project-release-line, single-release-identity, emergency-cleanup-governance}`. `audit.result` must use the closed set `{clean, discrepancy-found, reconciled-during-audit}`. `followUpStatus` must use the closed set `{not-required, required-open, resolved}`. `closedAt` must be absent for open incidents with `disposition` in `{open-before-publish, open-partial-publish}`, and required for every closed incident state; `null` is not a valid value. For `audit` records, `closedAt` must be absent when `followUpStatus = required-open` and required when `followUpStatus` is either `resolved` or `not-required`. `automationId` and `scriptVersion` are required whenever an `audit` record is appended automatically by workflow automation and must be absent for manually authored audit records; they form an inseparable pair, so one without the other is invalid. `holdStartedAt` and `eligibleDeleteAt` are required when the 48-hour hold in recovery step 9 applies and must be absent otherwise. `consumerImpactEvidenceUrl` is required on both the hold and hold-waiver paths because reviewed consumer-impact evidence is mandatory either way. `holdExceptionBasis` is required when that hold is waived under the younger-than-one-hour and zero-download exception, and must be absent when the hold actually applies. `burned` is reserved for identities that cannot be completed safely as the same release identity because the source, artifact, control-plane, or retention boundary has been invalidated, even if some published artifacts were later withdrawn. `recovered`, `burned`, `abandoned-before-publish`, `abandoned-after-partial-publish`, and `fully-withdrawn` are terminal dispositions; a closed incident record must never transition back to an open disposition, and any later correction must be recorded as a new ledger line that reuses the same `incidentId` and references the earlier incident via `operatorRationale` and `followUpIssue` when applicable. The validator must enforce a strict top-level key whitelist for both `incident` and `audit` records equivalent to JSON Schema with `additionalProperties: false`. The ledger is trusted control-plane state: updates normally go through the protected control-plane branch set under `CODEOWNERS` review. During a P0 or P1 incident, the dedicated emergency-cleanup group may use a break-glass bypass to land a minimal incident or audit ledger update directly, but that entry must include an `operatorRationale` explaining the bypass, must modify only `.github/release-recovery-ledger.jsonl`, and must be followed by a reviewed cleanup PR on the protected control-plane branch set by 17:00 UTC on the next Monday-Friday business day that either preserves the exact emergency record or replaces it with a reviewed equivalent without losing history. Control-plane audit automation must separately alert if that reviewed cleanup PR deadline is missed. Every incident that burns, fully withdraws, or partially publishes an official release identity must add or update a ledger entry before the incident is considered operationally closed.

The repository must maintain a registry-specific withdrawal runbook and treat it as part of the reviewed recovery design. At minimum: NuGet.org withdrawal means unlist only and must be documented as still directly installable by exact version; npmjs withdrawal means `npm unpublish` only within the registry's supported window, otherwise `npm deprecate` with explicit messaging; PyPI withdrawal means either yank or delete, with delete allowed only under explicit acknowledgement that the deleted filename/version can never be re-uploaded later and therefore burns that exact PyPI identity; RubyGems.org withdrawal means `gem yank` with the explicit note that historical mirrors or direct-fetch paths may still retain the artifact. When a partial official release is followed by PyPI deletion or any other irreversible per-target burn, the same reviewed ledger update must explicitly decide for every surviving already-published non-PyPI target whether it stays live or is also withdrawn before any new-version dispatch proceeds; silent mixed-state carry-forward is unsupported. When all previously published targets have been withdrawn, the incident must settle as `fully-withdrawn`. Any recovery path that says "withdraw" or "remove from normal availability" must follow this registry-specific runbook rather than relying on a vague strongest-available-action phrase. The withdrawal runbook and the registry-auth rollback runbook from step 14 must both be re-attested by release engineering at least every 90 days, with the attestation evidence recorded either in the protected control-plane branch history or in the recovery ledger as an `audit` record.

Operators must also treat GitHub's lifetime limits as first-class recovery boundaries. GitHub's documented 30-day workflow-rerun limit and the recommended 90-day official artifact retention are distinct timers with different failure modes. GitHub publicly documents environment wait-timer bounds but does not publicly document a separate approval-request expiry, so this design tracks long-lived pending approvals against the enclosing run's rerun deadline instead of inventing an independent GitHub expiry. If a run itself expires while approvals are still pending, audit the resulting `release/**` tag state against completed `release-complete` runs before choosing recovery, even if artifacts are still retained. If artifacts later expire as well, the original run is no longer recoverable and a fresh dispatch from the same still-unchanged protected branch is the only supported rebuild path. The repository must maintain four control-plane monitors in addition to the reviewed ledger updates. First, a scheduled approval-age monitor must run at least every 6 hours, enumerate official runs still waiting on `production-evidence-write-*`, `production-tag-write-*`, or `production-*` approvals, alert at minimum when the enclosing run's rerun deadline is within 7 days, 3 days, and 1 day, plus within the monitor's current polling interval (at most 6 hours), and ping an external dead-man's-switch heartbeat on success so the repository can detect when the monitor itself is disabled or stalled. Second, an event-driven post-tag failure monitor must react whenever an official run reaches `create-release-tag` success with `tag-result` in `{created, no-op}` for the expected `release/<project-name>/v<version>` identity and later settles in a non-success conclusion, then open or update the tracked recovery issue and require a ledger reconciliation check instead of relying on operators to notice the failed run manually. Third, a daily open-incident freshness monitor must re-query every open `incident` ledger entry with `disposition` in `{open-before-publish, open-partial-publish}`, compare live GitHub Release and registry state against the stored `publishedTargets`, `pendingTargets`, `tagState`, and `githubReleaseState`, escalate any such incident once its age reaches 14 days, and also alert on any `discrepancy-found` audit follow-up that remains unresolved for more than 24 hours. Fourth, a 7-day operational audit must enumerate protected `release/**` tags, enumerate GitHub Releases whose deterministic stable title matches `<project-name> v<version>`, confirm each one corresponds either to a completed official release or to an explicitly tracked burned or fully withdrawn identity under the recovery policy, and record that audit outcome in `.github/release-recovery-ledger.jsonl`. That 7-day audit must also enumerate every open `incident` ledger entry with `disposition` in `{open-before-publish, open-partial-publish}`, re-query the live GitHub Release state plus every selected remote registry target, and compare those live results against the stored `publishedTargets`, `pendingTargets`, `tagState`, and `githubReleaseState`; point-in-time ledger snapshots are not sufficient for open incidents. `full-release-tag-namespace` audits are the default periodic sweep and must enumerate every protected `release/**` tag. `project-release-line` audits are for scoped follow-up on a single protected branch or release line after a localized incident. `single-release-identity` audits are for one deterministic tag or version identity during active recovery. `emergency-cleanup-governance` audits are governance attestations for the break-glass group and do not enumerate release tags. The audit requirement must be enforced by a scheduled control-plane audit workflow, and that workflow must ping an external dead-man's-switch heartbeat on success so the repository can alert when the audit workflow itself fails, is disabled, or otherwise stops running. The external dead-man's-switch contract is strict: use a provider that accepts authenticated HTTPS heartbeats, configure one check for the approval-age monitor with an expected interval of 12 hours, one check for the daily open-incident freshness monitor with an expected interval of 36 hours, and one check for the 7-day audit with an expected interval of 8 days, and alert after the first missed interval for each. A second repository-side watchdog may alert on the age of the newest successful audit run, but it is only a supplement to the external heartbeat, not a replacement. This automation may automatically append `audit` ledger records when either `result = clean` with `followUpStatus = not-required`, or `result = discrepancy-found` with `followUpStatus = required-open` and a linked follow-up issue created or referenced in the same operation; reconciliations, break-glass usage, and incident-state changes still require a reviewed ledger update. Any `discrepancy-found` audit result that is not reconciled during the same audit must create or link a tracked follow-up issue before the audit is considered complete, must set `followUpStatus = required-open`, and must either be reconciled or escalated to the release-engineering owners within 24 hours. A later ledger entry may mark `followUpStatus = resolved` only after the discrepancy has been reconciled and the closing audit evidence is recorded.

## 8. Build Provenance

No official production release may go live until full provenance attestation is implemented for every official publish path enabled by this design. This is a machine-enforced workflow rule, not a documentation-only policy: `official.yml` includes the `require-provenance` gate described in Section 4, and `create-release-tag` plus every official publish job remain ineligible unless that gate succeeds.

Until that gate is satisfied for every enabled official target, the interim manifest-only state remains suitable only for pre-production rollout work and dry runs, and `require-provenance` must fail closed for production use. Full provenance attestation is considered implemented only when the protected control-plane branch contains checked-in attestation steps for every supported official publish path in this design, the language-matching official build job emits the required provenance artifact and durable evidence record, `require-provenance` validates those materials successfully, copies `artifact-evidence.json` to the protected durable evidence branch `refs/heads/release-evidence`, verifies the committed blob by permalink, emits `artifact-manifest-evidence-url`, and repository policy fails any official workflow change that would allow a selected official target to publish without that successful attestation and durable-copy output. The `release-evidence` branch is part of the control plane: it must be protected against force-push, accept writes only from the reviewed automation path plus the emergency-cleanup group, and retain immutable historical evidence paths under `.github/release-evidence/<project-name>/<version>/<source-commit>/runs/<workflow-run-id>-attempt-<workflow-run-attempt>/`.

The provenance contract itself is exact. Every official build job must produce GitHub artifact attestations for each published artifact using `actions/attest-build-provenance` or a reviewed successor that emits an equivalent DSSE-wrapped in-toto provenance statement. `require-provenance` must verify those attestations with `gh attestation verify` or an equivalent reviewed verifier that uses GitHub's documented trust root, and it must hard-fail unless verification proves all of the following claims for the exact manifest-selected artifact set: repository identity, repository owner identity, triggering ref, source commit SHA, the build-job `job_workflow_ref`, the workflow file SHA, and, when a GitHub environment was used and the verifier surfaces it, the environment name. With `gh attestation verify`, satisfying that contract is a two-step protocol rather than a flag-only check: first run the verifier with explicit signer and repository constraints plus an explicit per-invocation timeout of at most 30 seconds, then parse the verified JSON output and attestation statement to assert the exact source SHA, `job_workflow_ref`, workflow SHA, and optional environment claim. A plain flag-only invocation is insufficient for this design because not all required claims are surfaced as dedicated CLI switches. Verification must also prove that every attested subject digest matches the corresponding `artifact-manifest.json` entry, and the total attestation-verification budget must fail closed within 10 minutes for the selected artifact set. This design treats same-job attestation generation plus later verification as a GitHub-backed provenance gate, not as an isolated post-build signer, and therefore makes no SLSA L3 claim. Ecosystem-native proofs such as PyPI publish attestations or npm provenance may be emitted in addition to the GitHub attestation set, but they are supplemental evidence rather than replacements for this core gate.

`artifact-evidence.json` is the durable normalized record of that verification result. The evidence record must therefore capture the exact verified repository, ref, source SHA, `job_workflow_ref`, workflow SHA, repository owner identity, verifier tool, attestation type, and optional verified environment, as defined in Section 6. A missing claim, mismatched digest, unverifiable signer, unreviewed successor verifier, or durable-copy write failure is a hard failure. OIDC trusted publishing proves workflow identity at publish time; provenance attestation binds that identity, the source ref, and the exact build output into evidence that consumers and operators can verify later.

## Summary of Key Design Properties

1. **PR speed maximized**: A JS-only PR never waits for the Windows C# build queue.
2. **Channel isolation**: `buddy.yml` publishes only to unofficial package registries. `official.yml` publishes only to production registries plus optional stable GitHub Releases (`github:official`). Neither channel requires the other to run first for registry delivery.
3. **Static conditional dispatch**: Because `uses:` paths must be static, both build and publish jobs use conditional `if:` guards instead of dynamic matrix dispatch to reusable workflows. Each ecosystem-destination pair has its own dedicated job.
4. **Tag isolation**: Official release identity uses `release/<project-name>/v<version>`. Buddy no longer writes repository tags or GitHub Releases in this repository, which keeps unofficial branch-selected workflow code out of the official release-asset namespace.
5. **Overwrite-safe release identities**: Buddy treats GPR package versions as immutable and never overwrites them. Official publishes are idempotent for the same release identity only when remote artifact identity matches the local build output, and they never rebind a stable release to a different tag or commit.
6. **Least-privilege security**: Workflow-level `permissions: {}` with per-job escalation; build and publish jobs default to `secrets: {}` except for the explicit `NUGET_API_KEY` secret path; shell input hardening applies to reusable workflows as well as entry workflows and forbids untrusted `${{ ... }}` expansions inside `run:` blocks while also banning unsafe workflow-command-file writes and dynamic shell execution; privileged official publish logic stays on the protected control-plane branch set (`main` plus eligible protected `release/*` branches); package-registry publish workflows receive `contents: read` plus only their registry-specific auth path; trusted publishing for `npmjs`, `PyPI`, and `RubyGems.org` binds the strongest currently documented selector set each target supports, while `NuGet.org` uses an explicit environment secret; the dedicated `production-tag-write-*` and `production-evidence-write-*` environments isolate the release-tag writer and release-evidence writer credentials from publish jobs; the protected GitHub deployment-branch policy remains the authoritative branch restriction for all official registries; project-scoped protected environments with mandatory required-reviewer gates, exact deployment branch names, and repository-ruleset verification remain the authoritative branch scope; protected `.github/workflows/**`, official source branches, and official `release/**` tags.
7. **Terminal completeness checks**: `buddy.yml` and `official.yml` both end with a `release-complete` gate. For official releases, that gate depends on a separate `confirm-publish-state` job that re-queries live remote state with retry and backoff before declaring the selected target set complete. A green workflow run without that terminal proof is not considered complete.
8. **Durable recovery evidence and proactive operations**: Official provenance is not complete until `require-provenance` writes an immutable evidence record to the protected `release-evidence` branch, approval waiting states are monitored before the 30-day deadline, and the 7-day audit both revalidates open ledger entries against live remote state and is itself watched by an external dead-man's-switch heartbeat.
