# GitHub Workflows Design (v2.4)

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
- `_publish-github.yml` — publishes downloadable assets to GitHub Releases

The split axis is **ecosystem (tooling)**, not destination. Publishing a NuGet package to GPR vs NuGet.org uses the same tool (`dotnet nuget push`) with a different `--source` URL; the same applies to npm, RubyGems, etc. Each reusable workflow encapsulates one tool and one package format, accepting the destination as an input parameter. Each call publishes to **exactly one** destination — publishing to both GPR and an official registry requires two separate caller jobs (see Section 3, step 4). The caller (buddy or official) controls which destination and auth method to use.

For `official.yml`, the protected control-plane branch set is the default branch `main` plus eligible protected maintenance branches `release/<project-name>/v<release-line>`. The branch selected in the `workflow_dispatch` UI supplies both the trusted workflow/control-plane code and the release payload source for that run. Official release tags are derived and created by the workflow itself from the selected protected source ref after validation succeeds; they are not external workflow inputs.

Trusted control-plane code follows the same rule. For `official.yml`, the caller workflow, every reusable workflow, every composite action, and every helper script that performs privileged release gating or publishing come from the same dispatch-selected protected control-plane branch. Because official runs are allowed only from that protected branch set, there is no separate historical tagged-source workspace in this design.

**Secrets:**

- **Build-test workflows** have no secret requirements. Callers should pass secrets explicitly: `secrets: {}` (empty). This limits the blast radius if a compromised dependency or malicious test reads the environment during build/test execution.
- **Publish workflows** should also default to `secrets: {}`. Prefer the automatic `GITHUB_TOKEN`, caller-granted `permissions`, and OIDC Trusted Publishing. If a future destination cannot use those mechanisms and needs an explicit credential, the caller must pass only that named secret; blanket `secrets: inherit` is prohibited in this design.

Permissions are inherited automatically: a reusable workflow receives the caller job's `permissions` grants as long as the reusable workflow itself does **not** declare its own `permissions` block. This is what allows the same `_publish-nuget.yml` to operate under `packages: write` when called from `buddy.yml` and under `id-token: write` when called from `official.yml`.

> **Important constraint:** Reusable workflows must NOT declare their own `permissions:` block. If they do, the effective token is silently capped at the intersection of the declared scopes and the caller's grants. For example, if a reusable workflow declares `permissions: { id-token: write }` but the caller only grants `packages: write`, the minted token will have `id-token: none`, causing silent runtime failures. Keep all `permissions:` declarations in the entry workflows only.
>
> This rule must be lint-enforced in repository policy. In addition to `actionlint`, the repository must run a custom `hk` validation that fails if any reusable workflow under `.github/workflows/_*.yml` declares either a workflow-level or job-level `permissions:` block.

> **Important constraint:** Shell input hardening applies to both entry workflows and reusable workflows. No `run:` step may interpolate `${{ inputs.* }}`, `${{ github.event.inputs.* }}`, `${{ github.* }}`, `${{ needs.*.outputs.* }}`, or any other untrusted expression directly into shell source. All such values must first be mapped under `env:` and then referenced as quoted shell variables.

**Permissions model:** Every entry workflow declares `permissions: {}` at workflow level. Individual jobs then request only the scopes they need (principle of least privilege). Key scopes:

| Job kind                            | Required `permissions` |
| ----------------------------------- | ---------------------- |
| Read repository metadata / releases | `contents: read`       |
| Read-only checkout                  | `contents: read`       |
| Read environment metadata           | `environments: read`   |
| Push tags                           | `contents: write`      |
| Create GitHub Release               | `contents: write`      |
| GitHub Packages (any feed)          | `packages: write`      |
| OIDC publish to official registries | `id-token: write`      |

All four official registries (NuGet.org, PyPI, npmjs, RubyGems.org) support OIDC Trusted Publishing. GPR feeds use `GITHUB_TOKEN` with `packages: write` instead.

> **Note:** With `permissions: {}` at workflow level, jobs that run `actions/checkout` or read GitHub release metadata must explicitly declare at least `permissions: { contents: read }`. Jobs that read GitHub environment metadata through the job `GITHUB_TOKEN` must declare at least `permissions: { environments: read }`. In this design, `preflight-check` does not rely on the job `GITHUB_TOKEN` for either environment metadata or ruleset metadata; it mints a dedicated read-only GitHub App installation token and may therefore keep `permissions: {}` unless future changes add other repository reads. Build jobs included — without the required scope, the zero-permission `GITHUB_TOKEN` cannot clone the repository or read the repository metadata that release gating depends on.

**Repository protection model:** This design uses GitHub repository rulesets for protected branches and protected tags. Legacy branch-protection endpoints and compatibility shims are out of scope before implementation starts. Workflow preflight and policy validation query the Environments API plus the Repository Rulesets API only.

**Concurrency policy:** Each entry workflow defines a `concurrency:` group to prevent resource races:

- `ci.yml`: `group: ci-${{ github.ref }}`, `cancel-in-progress: true`
- `buddy.yml`: `group: buddy::${{ github.ref }}::${{ inputs.project-name }}`, `cancel-in-progress: false`
- `official.yml`: `group: official::${{ github.ref }}::${{ inputs.project-name }}`, `cancel-in-progress: false`

The `::` separator is intentional because it cannot appear in either `github.ref` or a valid `project-name`, which prevents ambiguous concatenation such as `feat` + `a-b` colliding with `feat-a` + `b`. GitHub Actions still compares concurrency groups case-insensitively, so releasable `project-name` values must also be unique under ASCII lowercase normalization across the repository. With `cancel-in-progress: false`, an in-progress run is preserved. GitHub Actions may still replace an older queued run with a newer queued run for the same concurrency group, so operators should not stack multiple fresh dispatches for the same buddy project/ref combination or the same official project/ref combination and assume each queued run will execute. Before dispatching buddy with `force=true`, the operator must inspect the existing runs for that same buddy project/ref concurrency group and confirm whether another run is already queued or in progress. If one exists, the operator must either cancel it or wait for it to settle before issuing the `force=true` dispatch; a later `force=true` dispatch can replace an older queued run but will not change an already in-progress run. For buddy specifically, queued-run replacement can also discard the operator intent behind an earlier `force=true` dispatch on the same project/ref combination, so `force=true` must be treated as a one-run decision rather than something the queue preserves.

**Job timeouts:** Every job must declare `timeout-minutes`, and workflow linting enforced through `hk`/`actionlint` should fail if any job omits it. Recommended defaults: `preflight-check`, resolution jobs, and static-analysis jobs `15`; Ubuntu build jobs `30`; Windows C# build jobs `45` because hosted Windows runners have materially higher startup and .NET restore/build/test overhead than Ubuntu runners; publish jobs `15`; lightweight tag-management jobs `10`; and terminal gate jobs `ci-passed` and `release-complete` `10`. Some YAML snippets below omit `timeout-minutes` only for brevity; concrete workflow files must still declare it.

**Action pinning:** All external actions, including GitHub-maintained actions under the `actions/` namespace, must be pinned to full commit SHA. Local composite actions under `.github/actions/**` are sourced from the checked-out protected workspace, must be explicitly covered by `CODEOWNERS`, and are governed by the same branch protection and `CODEOWNERS` review as the caller workflow rather than by a separate pin. Use Renovate or Dependabot to manage external action updates:

```yaml
uses: dorny/paths-filter@de90cc6ed7cd597cb74b84a7e832ce805e3c7b15 # v3.0.2
```

The repository's dependency-update automation must cover `.github/workflows/**` so pinned SHAs are refreshed intentionally rather than drifting indefinitely.

**Tool lock enforcement:** `mise.lock` is required repository state, not optional convenience metadata. `hk check --all` must fail when the repository root lacks `mise.lock`, and any intentional toolchain update must regenerate the lockfile with `mise lock` in the same change that modifies `mise.toml`.

## 2. `ci.yml` — PR Validation (Targeted Concurrency, Shift-Left)

**Trigger:** `on: pull_request`

CI does not build everything on every PR. It uses path filtering (`dorny/paths-filter`, SHA-pinned) to run only the affected language test suites.

**Jobs:**

1. **`static-analysis`**: Runs `jdx/hk` (`hk check --all`) on an Ubuntu runner. HK auto-detects file types from its configuration (`hk.pkl`), serving as the first gate for formatting and linting failures.

2. **`detect-changes`**: Uses `dorny/paths-filter` to classify modified files:
    - `csharp`: `['**/*.cs', '**/*.csproj', 'global.json', 'Directory.*.props', 'nuget.config', '**/NuGet.Config', '**/*.targets', '**/packages.lock.json']`
    - `python`: `['**/*.py', 'pyproject.toml', 'uv.lock']`
    - `jsts`: `['**/*.ts', '**/*.js', 'package.json', 'pnpm-workspace.yaml', 'pnpm-lock.yaml', 'biome.jsonc', 'tsconfig*.json']`
    - `ruby`: `['**/*.rb', '**/*.gemspec', 'Gemfile', 'Gemfile.lock']`
    - `infra`: `['.github/workflows/**', '.github/actions/**', '.github/CODEOWNERS', '.github/publish-trust-inventory.json', '.github/release-recovery-ledger.jsonl', 'eng/scripts/**', '**/release.json', '**/version.json', 'mise.toml', 'mise.lock', 'hk.pkl', 'PklProject', 'PklProject.deps.json']`

    When `infra` changes are detected, all language test suites are triggered regardless of other filters.

    > **Scaling note:** The current filters operate at language level (`**/*.cs` triggers all C# builds). As the monorepo grows past ~10 projects per language, this should evolve to per-project granularity using affected-project detection from `eng/scripts/find_project_path.py`.

3. **`trusted-release-inventory`**:
    - `needs: [detect-changes, static-analysis]`
    - `permissions: { contents: read }`
    - Conditional: `if: needs.detect-changes.outputs.infra == 'true'`
    - Checks out the PR head with `persist-credentials: false` and runs the repository-side drift check for `.github/publish-trust-inventory.json`. This job must recompute the post-change trust-bearing state from the checked-in control-plane files and compare the exact normalized values of `entryWorkflowPath`, the deduplicated fully qualified `allowedCallerRefs` set, `publishWorkflowPaths`, and `targetAuthMechanisms`. Any missing inventory update, stale mapping, malformed schema, or mismatched auth mechanism is a hard failure.

4. **`test-csharp` / `test-python` / `test-jsts` / `test-ruby`** (run in parallel):
    - `needs: [detect-changes, static-analysis]`
    - `permissions: { contents: read }`
    - Conditional: e.g. `if: needs.detect-changes.outputs.csharp == 'true' || needs.detect-changes.outputs.infra == 'true'`
    - Each calls its corresponding reusable workflow. C# uses `windows-latest`; the others use `ubuntu-latest`.

5. **`ci-passed`** (final gate job):
    - `if: always()`
    - `needs: [detect-changes, static-analysis, trusted-release-inventory, test-csharp, test-python, test-jsts, test-ruby]`
    - Asserts all required checks either passed or were legitimately skipped. Including `detect-changes`, `static-analysis`, and `trusted-release-inventory` in `needs` ensures their failures block the gate — if `detect-changes` fails, all downstream conditional jobs are auto-skipped with `result: "skipped"`, and without `detect-changes` in `needs`, `ci-passed` would see only `"success"` and `"skipped"` results and falsely pass. The gate must also re-derive which language suites were required from `detect-changes.outputs` so a drifted `if:` condition on a `test-*` job cannot silently convert a required suite into a tolerated skip.

    ```yaml
    ci-passed:
        if: always()
        needs: [detect-changes, static-analysis, trusted-release-inventory, test-csharp, test-python, test-jsts, test-ruby]
        runs-on: ubuntu-latest
        steps:
              - name: Assert all required checks passed or were legitimately skipped
              env:
                  NEEDS_JSON: ${{ toJson(needs) }}
              run: |
                  echo "$NEEDS_JSON" | jq -e '
                                        (."detect-changes".result == "success")
                                        and (."static-analysis".result == "success")
                                    and (if (."detect-changes".outputs.infra == "true")
                                        then ."trusted-release-inventory".result == "success"
                                        else ."trusted-release-inventory".result == "skipped"
                                        end)
                                    and (if (."detect-changes".outputs.csharp == "true" or ."detect-changes".outputs.infra == "true")
                                        then ."test-csharp".result == "success"
                                        else ."test-csharp".result == "skipped"
                                        end)
                                    and (if (."detect-changes".outputs.python == "true" or ."detect-changes".outputs.infra == "true")
                                        then ."test-python".result == "success"
                                        else ."test-python".result == "skipped"
                                        end)
                                    and (if (."detect-changes".outputs.jsts == "true" or ."detect-changes".outputs.infra == "true")
                                        then ."test-jsts".result == "success"
                                        else ."test-jsts".result == "skipped"
                                        end)
                                    and (if (."detect-changes".outputs.ruby == "true" or ."detect-changes".outputs.infra == "true")
                                        then ."test-ruby".result == "success"
                                        else ."test-ruby".result == "skipped"
                                        end)'
    ```

## 3. `buddy.yml` — Unofficial Release (Static Conditional Publish, Tag Isolation)

**Trigger:** `on: workflow_dispatch` only (no automated triggers).

**Inputs:**

| Input          | Type      | Required | Description                                                                                                          |
| -------------- | --------- | -------- | -------------------------------------------------------------------------------------------------------------------- |
| `project-name` | `string`  | Yes      | Project identity to release                                                                                          |
| `force`        | `boolean` | No       | Allow buddy to replace non-matching GitHub pre-release assets and repoint buddy traceability tags (default: `false`) |

All workflow inputs must be mapped to intermediate environment variables before use in shell scripts (e.g., `env: PROJECT_NAME: ${{ inputs.project-name }}`; use `"$PROJECT_NAME"` in bash, never `${{ inputs.project-name }}` directly in `run:` blocks).

`force=true` is a **privileged** path. In this design revision, that privilege is recorded as policy rather than enforced by a separate workflow-level approval gate. This is an explicit pre-implementation risk acceptance: buddy overwrite authority is currently controlled by repository write access and release-operator discipline, and the workflow itself does not add a distinct protected-environment boundary yet. Because buddy may be dispatched from development branches and uses the workflow definitions from the selected branch, repository write access to a branch that can dispatch buddy is also sufficient to alter unofficial publish logic for that run. That risk is intentionally accepted for the unofficial channel only and does not extend to `official.yml`.

Buddy is intentionally allowed to release from development branches. It does **not** require ancestry to `main` or to a maintenance release branch.

Even within the same language, different projects may have different packaging strategies (EXE, NuGet, wheel, etc.). The workflow resolves publish targets dynamically from project configuration.

**Jobs:**

1. **`resolve-context`**:
    - `permissions: { contents: read }`
    - **Runner and tooling:** Runs on `ubuntu-latest`. Requires `mise install` to bootstrap Python (for `eng/scripts/find_project_path.py`) and the .NET SDK. The `nbgv-python` adapter is sourced from the current checked-out repository workspace, not from an external package index, so trusted version resolution tracks the selected source ref rather than an out-of-band registry artifact. The `mise.toml` and `mise.lock` at the repo root pin tool versions and, where supported by the selected MISE backends, the exact download digests. The job must hard-fail if `mise.lock` is absent, and should restore a tool cache keyed by both files before invoking `mise install`.
    - **Input validation:** As the first step (before any checkout or git operation), validate `project-name` with a full-string match against the character class `[A-Za-z0-9][A-Za-z0-9._-]*`, reject any occurrence of the substring `..`, reject trailing `.`, and reject any name that ends with `.lock`. Reject invalid names with a clear error. This is stricter than the current helper script because leading option-like names are intentionally out of scope for releaseable project identities and the name must remain compatible with Git ref naming and `git check-ref-format`.
    - **Source ref policy:** Buddy intentionally permits dispatch from non-default branches. No ancestry check against `main` or any release branch is performed in this workflow.
    - Runs `eng/scripts/find_project_path.py` to determine the project path and the workflow language. `project-name` is case-sensitive and must resolve to exactly one project in the repository. The resolution step must emit exactly one of `{csharp, python, jsts, ruby}` for `language`; no match, ambiguous match, unsupported language, or resolver error is a hard failure.
    - **NBGV resolution:** The checkout must use `fetch-depth: 0` so NBGV can compute version height from git history. Read-only checkouts in this job must also use `persist-credentials: false`. All jobs that use NBGV or rely on git-history-derived metadata must also checkout with full history. The script locates the correct `version.json` by searching upward from the project directory. In this design, "resolve deterministically" means: on a full-history checkout of the selected ref, `nbgv-python` finds exactly one governing `version.json`, emits exactly one normalized version string, and that string passes the language-specific validator. Missing history, ambiguous governing configuration, or validator failure are all non-deterministic failures. If `nbgv-python` cannot resolve the version deterministically, the job must hard-fail; there is no fallback or manual override path in this design. Version validation is performed programmatically using the existing scripts: `eng/scripts/validate_semver2_version.py` (for NuGet and npm), `eng/scripts/validate_rubygems_version.py` (for the repository's supported RubyGems-compatible subset), or `eng/scripts/validate_pep440_version.py` (for Python/PyPI). The NBGV-resolved value becomes the workflow output `version` and the single buddy release identity for that run. Because buddy intentionally allows dispatch from development branches, the computed buddy version may differ across branches or after additional commits change git history height; that is expected behavior rather than a recovery-path bug.
    - Reads the project's release configuration (see **Section 5: Release Configuration Contract**) and emits a JSON array of publish targets. Targets use the format `ecosystem:destination` (e.g. `["nuget:gpr", "github:release"]`).
    - **Strictly validates** `release.json` exactly as specified in **Section 5** before any channel filtering occurs.
    - **Language-target validation:** Before channel filtering, validate every declared target against the resolved project language. `csharp` projects may declare only `nuget:*` and `github:*`; `jsts` projects may declare only `npm:*` and `github:*`; `python` projects may declare only `pypi:official` and `github:*`; `ruby` projects may declare only `rubygems:*` and `github:*`. Cross-ecosystem target declarations are hard failures.
    - After that validation succeeds, `buddy.yml` filters to the unofficial target set `{nuget:gpr, npm:gpr, rubygems:gpr, github:release}` and fails if the filtered set is empty. Targets that belong to the official channel are filtered out only **after** strict validation succeeds. Unknown or duplicate target values are hard failures. In this design, Python has no unofficial registry target; a Python project that wants a buddy preview must declare `github:release`. That preview is a downloadable GitHub pre-release asset, not a pip-installable package index.
    - **GitHub Packages immutability in workflow scope:** GitHub supports deleting and restoring package versions with elevated package-admin capabilities, but this design does not request delete or admin package permissions and does not support delete-and-republish recovery. Within this workflow design, GPR package versions are treated as immutable release identities.
    - **Overwrite guard:** Before proceeding, paginate GitHub Releases where `prerelease == false`, including drafts, and look up the deterministic stable-release title `<project-name> v<version>`. If that stable title already exists under any tag or commit, fail immediately — a draft or published stable release with that deterministic title is part of the same stable identity space, and buddy must not overwrite or shadow it. Buddy GitHub pre-release overwrite and idempotency decisions are enforced later inside `_publish-github.yml` with remote asset identity checks, the deterministic pre-release title `<project-name> v<version> (pre-release)`, and the caller's `force` input. Separately, if a buddy traceability tag under `refs/tags/buddy/<project-name>/v<version>` exists pointing to a different commit, allow overwrite only when `inputs.force` is `true`.
    - **Outputs:** `language`, `project-name`, `project-path`, `version`, `targets` (JSON array of filtered unofficial targets).
    - **On failure**, the script must print: the resolved project path, the contents of `release.json` if found, and the specific validation rule that was violated.

2. **`static-analysis`**:
    - `needs: [resolve-context]`
    - `permissions: { contents: read }`
    - Checks out the source ref for this workflow run before enumerating files. Read-only checkout must use `persist-credentials: false`.
    - Runs `hk check <project-path>` scoped to the resolved project path. HK receives the project path directly and discovers applicable files under that path according to `hk.pkl`; this design does not pre-enumerate file names in shell.

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
            checkout-ref: ${{ github.sha }}
            project-path: ${{ needs.resolve-context.outputs.project-path }}
            project-name: ${{ needs.resolve-context.outputs.project-name }}
        secrets: {}

    build-python:
        needs: [resolve-context, static-analysis]
        permissions:
            contents: read
        if: needs.resolve-context.outputs.language == 'python'
        uses: ./.github/workflows/_build-test-python.yml
        with:
            checkout-ref: ${{ github.sha }}
            project-path: ${{ needs.resolve-context.outputs.project-path }}
            project-name: ${{ needs.resolve-context.outputs.project-name }}
        secrets: {}

    build-jsts:
        needs: [resolve-context, static-analysis]
        permissions:
            contents: read
        if: needs.resolve-context.outputs.language == 'jsts'
        uses: ./.github/workflows/_build-test-jsts.yml
        with:
            checkout-ref: ${{ github.sha }}
            project-path: ${{ needs.resolve-context.outputs.project-path }}
            project-name: ${{ needs.resolve-context.outputs.project-name }}
        secrets: {}

    build-ruby:
        needs: [resolve-context, static-analysis]
        permissions:
            contents: read
        if: needs.resolve-context.outputs.language == 'ruby'
        uses: ./.github/workflows/_build-test-ruby.yml
        with:
            checkout-ref: ${{ github.sha }}
            project-path: ${{ needs.resolve-context.outputs.project-path }}
            project-name: ${{ needs.resolve-context.outputs.project-name }}
        secrets: {}
    ```

    Only one of these four jobs will actually execute. Build artifacts (`.nupkg`, `.whl`, `.exe`, `.gem`, etc.) are uploaded to CI Artifacts using a deterministic name: `build-output-<project-name>` (e.g. `build-output-my-library`). Artifacts are built fresh within this workflow run; no artifacts from prior runs are downloaded. Build workflows must produce reproducible package outputs for the same source commit and locked toolchain so reruns can satisfy remote-identity idempotency checks.

4. **Publish jobs** (static conditional, one job per ecosystem-destination pair):

    Because GitHub Actions resolves `uses:` statically at parse time, and each reusable workflow call publishes to **exactly one** destination, publish jobs are split per ecosystem-destination pair. Each job has its own `if:` guard using `fromJson()` for exact array membership (not substring matching). That guard must assert `resolve-context.result == 'success'`, `static-analysis.result == 'success'`, exact target membership, and that the single language-matching build job finished with `result == 'success'` while the three non-matching build jobs finished with `result == 'skipped'`; buddy publish jobs must not rely on downstream `release-complete` alone to prove the required build succeeded. `always() && !cancelled() && !failure()` remains necessary so selected publish jobs are not suppressed merely because the unrelated build jobs were skipped:

    ```yaml
    publish-nuget-gpr:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            packages: write
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            ((needs.resolve-context.outputs.language == 'csharp' && needs.build-csharp.result == 'success' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'python' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'success' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'jsts' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'success' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'ruby' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'success')) &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'nuget:gpr')
        uses: ./.github/workflows/_publish-nuget.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
            feed-url: https://nuget.pkg.github.com/hcoona/index.json
        secrets: {}

    publish-npm-gpr:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            packages: write
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            ((needs.resolve-context.outputs.language == 'csharp' && needs.build-csharp.result == 'success' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'python' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'success' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'jsts' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'success' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'ruby' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'success')) &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'npm:gpr')
        uses: ./.github/workflows/_publish-npm.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
            registry: https://npm.pkg.github.com
        secrets: {}

    publish-rubygems-gpr:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            packages: write
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            ((needs.resolve-context.outputs.language == 'csharp' && needs.build-csharp.result == 'success' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'python' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'success' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'jsts' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'success' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'ruby' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'success')) &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'rubygems:gpr')
        uses: ./.github/workflows/_publish-rubygems.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
            host: https://rubygems.pkg.github.com/hcoona
        secrets: {}

    publish-github-release:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            contents: write
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            ((needs.resolve-context.outputs.language == 'csharp' && needs.build-csharp.result == 'success' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'python' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'success' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'jsts' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'success' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'ruby' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'success')) &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'github:release')
        uses: ./.github/workflows/_publish-github.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            project-name: ${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
            tag-name: buddy/${{ needs.resolve-context.outputs.project-name }}/v${{ needs.resolve-context.outputs.version }}
            prerelease: true
            force: ${{ inputs.force }}
        secrets: {}
    ```

    - The `if: always() && !cancelled() && !failure()` guard ensures the publish jobs run despite the three skipped build jobs in the `needs` chain. This condition is safe because skipped jobs are treated as neither failure nor cancellation.
    - Including `static-analysis` directly in each publish job's `needs` keeps the gate explicit and allows the job to assert `needs.static-analysis.result == 'success'` directly rather than relying on transitive failure propagation alone.
    - For GPR targets, auth uses `GITHUB_TOKEN` with `packages: write`. No OIDC is needed.
    - All buddy publish jobs use `secrets: {}`. No repository, organization, or environment secrets are forwarded by default.
    - Each publish step uses idempotent publish logic. Duplicate-version outcomes (`409`, `422`, or tool-equivalent "already exists" responses) count as success only after the workflow verifies that the already-published remote artifact set matches the local artifact set and expected digests. Authentication failures, authorization failures, malformed artifacts, and upstream `5xx` errors remain hard failures. This design intentionally does not retry upstream `5xx` failures inside a single run; operator recovery happens by re-running the workflow.

5. **`release-complete`**:
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, publish-nuget-gpr, publish-npm-gpr, publish-rubygems-gpr, publish-github-release]`
    - `if: always()`
    - `permissions: {}`
    - Performs the terminal correctness check for buddy. It must first assert that `resolve-context.result == "success"` and `static-analysis.result == "success"`. It must then parse `targets` as JSON, assert that the filtered target set is non-empty, map that set to the exact publish jobs `{nuget:gpr -> publish-nuget-gpr, npm:gpr -> publish-npm-gpr, rubygems:gpr -> publish-rubygems-gpr, github:release -> publish-github-release}`, and assert that every selected target finished with `result == "success"` and a valid `publish-result` output in `{new-publish, no-op}`.
    - It must also assert that every non-selected publish job finished with `result == "skipped"`.
    - It must also assert that the single language-matching build job finished with `result == "success"`; the three non-matching build jobs must be `result == "skipped"`.
    - The normative jq skeleton is:

    ```yaml
    - name: Assert buddy release completeness
      env:
          NEEDS_JSON: ${{ toJson(needs) }}
      run: |
          echo "$NEEDS_JSON" | jq -e '
              . as $n
              | {
                  publishJobs: {
                      "nuget:gpr": "publish-nuget-gpr",
                      "npm:gpr": "publish-npm-gpr",
                      "rubygems:gpr": "publish-rubygems-gpr",
                      "github:release": "publish-github-release"
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
                  (($n["resolve-context"].outputs.targets // "[]") | fromjson) as $targets
                  | ($targets | length) > 0
                  and ($targets | all(. as $target | $map.publishJobs[$target] != null))
                  and ($targets | all(. as $target | $n[$map.publishJobs[$target]].result == "success"))
                  and ($targets | all(. as $target | ($n[$map.publishJobs[$target]].outputs["publish-result"] == "new-publish" or $n[$map.publishJobs[$target]].outputs["publish-result"] == "no-op")))
                  and (([$map.publishJobs[]] - ($targets | map($map.publishJobs[.])))
                      | all(. as $job | $n[$job].result == "skipped"))
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

6. **`create-traceability-tag`**:
    - `needs: [resolve-context, release-complete]`
    - `if: needs.release-complete.result == 'success'`
    - `permissions: { contents: read }`
    - Assembles and pushes a lightweight Git tag: `buddy/<project-name>/v<version>`.
    - Before pushing the tag, this job must mint a dedicated GitHub App installation token for the buddy tag writer App from the repository-scoped organization secret configured for this purpose, then perform a preflight-style GitHub Repository Rulesets API check for `refs/tags/buddy/**`. The check must verify that an active tag ruleset restricts both tag creation and tag updates in that namespace, and that bypass actors are limited to the dedicated buddy-tag writer App plus the dedicated release-engineering emergency-cleanup group rather than the GitHub Actions app or broad human repository roles. Treat missing rulesets, weaker protection, or any GitHub API error as a hard failure, and do not attempt the tag push when the buddy tag namespace cannot be verified as protected.
    - **Tag overwrite logic:** If the tag does not exist, create it. If it exists and points to the same commit, succeed as no-op. If it exists but points to a different commit: when `inputs.force` is `true`, force-update the tag; otherwise fail with a clear error message. Whenever a force-update is actually performed, append a human-readable record to `$GITHUB_STEP_SUMMARY` that states the old commit, the new commit, and the operator-selected `force=true` path.
    - Checks out the source ref read-only, then configures git explicitly to push with the minted buddy-tag writer App installation token. The job must not persist the default `GITHUB_TOKEN` as a write-capable remote credential.
    - Uses the minted buddy-tag writer App token for `git push origin <tag>`; no inline `${{ ... }}` expression is embedded in shell source. Because the buddy namespace `buddy/**` is disjoint from the official release namespace `release/**`, pushing this tag does not enter the official release-identity path. `official.yml` is additionally `workflow_dispatch`-only, which removes push-trigger ambiguity entirely.
    - Buddy traceability tags are intentionally isolated from the official release-identity namespace. They are informational only and are never accepted as `official.yml` input.

## 4. `official.yml` — Production Release

**Important:** `buddy.yml` and `official.yml` are **independent release channels**, not a sequential promotion pipeline. Buddy publishes to unofficial registries and optional GitHub pre-releases via `github:release`; official publishes to production registries and optional stable GitHub Releases via `github:official`. A buddy run is NOT a prerequisite for an official run — either can be triggered independently for registry delivery and for GitHub Release delivery.

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

**Caller ref policy:** In `workflow_dispatch`, the branch selected in the GitHub UI determines which revision of `official.yml`, its reusable workflows, its trusted helper code, and its release payload source executes. Under this design, that caller ref must be one of the protected control-plane branches only: `main` or an eligible protected maintenance branch `release/<project-name>/v<release-line>`.

**Release identity mechanism:** `official.yml` does not accept a pre-existing release tag as input. Instead, it resolves the project version from the dispatch-selected protected source ref, derives the official release tag `release/<project-name>/v<version>` from that result, and creates that protected release tag inside the workflow only after `environment: production` approval has been granted to the dedicated tag-reservation job. This keeps official and buddy symmetric as `workflow_dispatch` entry points while preserving a dedicated immutable official release-identity namespace.

**Branch and version mechanism:** Official release eligibility is decided from the dispatch-selected protected source ref itself. The workflow resolves `project-name`, `language`, `project-path`, and NBGV version from that ref, validates the version semantically, derives the release line from the resolved version, captures a single comparison snapshot of `origin/main` at the start of `resolve-context`, and then checks that the selected protected branch matches that release line against that frozen snapshot. If the resolved release line matches the frozen `main` release line, the caller ref must be `main`. If it differs, the caller ref must be the exact protected maintenance branch `release/<project-name>/v<release-line>`. Only after those checks succeed may the workflow derive and create the protected official release tag `release/<project-name>/v<version>`.

**Maintenance branch policy:** A maintenance branch exists only for release lines that release engineering explicitly supports. It is created by release engineering from the first official release on that line, or immediately before the first hotfix on that line, using the exact name `release/<project-name>/v<release-line>`. Before that branch is used for any official release, it must receive the same protection profile as `main`: required PR review, required `ci-passed`, no direct pushes, and no force-pushes. The `main` release line is the base release line computed from the frozen `origin/main` comparison snapshot captured at the start of `resolve-context` for that run. If a dispatch-selected version resolves to any different release line and the matching maintenance branch does not exist, `official.yml` must fail with a clear error that prints the exact expected branch name and instructs the operator to either create and protect that maintenance branch or dispatch from the correct protected branch for that release line. Retired release lines are no longer eligible for official publication.

**Release-line derivation:** This design uses one release-line rule across all supported ecosystems. First, keep only the leading numeric release segment of the normalized version string and discard everything from the first prerelease, postrelease, devrelease, build, local, or repository-specific suffix onward. Concretely: for SemVer-style versions, discard everything from the first `-` or `+`; for PEP 440-style versions, keep only the leading release segment before any `a`, `b`, `rc`, `.post`, `.dev`, or `+local` suffix; for the repository's Ruby subset, keep only the leading `MAJOR.MINOR.PATCH` numeric segment before any dotted suffix containing letters. Then read at most the first two numeric components, zero-pad a missing minor component to `0`, and render the line as `v<major>.<minor>.x`. Any third and later numeric components are ignored for release-line selection. Examples: `1.1 -> v1.1.x`, `1.2.3 -> v1.2.x`, `1.2.3.4 -> v1.2.x`, `1.2.3rc1 -> v1.2.x`, `1.2.3+meta -> v1.2.x`, `1.2.0-dev.1 -> v1.2.x`, `1.2.post1 -> v1.2.x`. PEP 440 epoch markers (`!`) remain unsupported in release tag versions.

**Maintenance branch onboarding order:** Because implementation has not started yet, the onboarding procedure is defined strictly rather than retrofitted for backward compatibility. The safe order is: (1) create the maintenance branch, (2) apply the full protection profile and required code-owner review, (3) add that exact branch name to the `environment: production` deployment branch policy, (4a) add the matching registry-side OIDC trust entry, (4b) merge the matching `.github/publish-trust-inventory.json` change onto the protected control-plane branch set, and only then (5) dispatch `official.yml` from that branch. Step 4 is not complete until both step 4a and step 4b have succeeded. If step 4a fails, step 3 must be rolled back immediately. If step 4b fails after step 4a succeeded, or if onboarding is otherwise aborted after registry-side trust was added, release engineering must first remove the registry-side OIDC trust entry added in step 4a and only then roll back step 3 so the branch is not left in the production deployment policy or at the registry without matching trust state. Any other ordering is unsupported.

**Maintenance branch retirement order:** Retirement is the inverse control-plane change and must also be strict. The safe order is: (0) confirm there are no queued, waiting-for-approval, action-required, pending, or in-progress `official.yml` runs from that branch and drain them correctly, (1) remove that exact branch name from the `environment: production` deployment branch policy, (2) merge the matching `.github/publish-trust-inventory.json` removal onto the protected control-plane branch set, and only then (3) remove the matching registry-side OIDC trust entry. Any other ordering is unsupported. Operationally, step 0 is not complete until release engineering has enumerated `official.yml` runs for that exact branch by separate queries for each non-completed status class, at minimum `queued`, `in_progress`, `waiting`, `pending`, and `action_required`, rather than relying on a single capped list, and then either (a) completed any run that already published one or more official destinations, or explicitly treated that release identity as burned under Section 7 before continuing, or (b) cancelled runs that have not yet published any official destination. Retirement must not proceed while any run remains queued, waiting for approval, action-required, pending, or in progress. If step 3 fails after step 2 has succeeded, retirement is not operationally complete. The branch remains retired on the GitHub side and no new official run may be dispatched from it. Release engineering must continue remediation until the registry-side OIDC trust entry is removed; if retirement must instead be rolled back, it must first restore the matching `.github/publish-trust-inventory.json` entry on the protected control-plane branch set and only then restore the `environment: production` deployment branch policy entry, so registry and GitHub trust state return to a matching configuration before any official release resumes. Git branch deletion is optional and may happen only after step 3 succeeds; if the branch is retained for archival purposes, it must remain outside the production deployment policy and outside `.github/publish-trust-inventory.json`.

**Prerequisites (must be configured before first run):**

- **Repository rulesets only:** This design uses GitHub repository rulesets, not legacy branch protection, for protected branches and protected tags. Rulesets configuration must be in place before the first workflow run; no backward-compatibility path for classic branch-protection endpoints is supported.
- **Branch rulesets** on the default branch, and on every maintenance release branch used for official hotfixes, must require PR review approval, required code-owner review, and the `ci-passed` required status check before merging, and must disallow direct pushes and force-pushes. Without this, direct pushes bypass `ci.yml` entirely, allowing unreviewed code to be released.
- **Official release tag rulesets** must restrict both tag creation and tag updates on `refs/tags/release/**`. Because GitHub repository rulesets cannot scope bypass by workflow file path, the configuration must use a dedicated release-tag writer GitHub App as the only automation bypass actor for normal workflow execution and a dedicated release-engineering emergency-cleanup group as the only human bypass actor for manual recovery. The GitHub Actions app that backs `GITHUB_TOKEN` must **not** be a bypass actor on `refs/tags/release/**`.
- **Buddy traceability tag rulesets** must likewise restrict both creation and updates on `refs/tags/buddy/**` outside the workflow path. Buddy traceability tags remain informational only and are intentionally outside the official release-identity namespace, but protecting them prevents pre-seeding and accidental traceability poisoning. Their bypass actors must be limited to a dedicated buddy-tag writer GitHub App for workflow automation plus the dedicated release-engineering emergency-cleanup group for manual recovery; the GitHub Actions app must **not** be a bypass actor.
- **`environment: production`** must exist in GitHub repository settings with protection rules that include required reviewers and `prevent_self_review = true` **before** the workflow is ever triggered. If this environment does not pre-exist, GitHub auto-creates it with **zero** protection rules and the human approval gate silently does not exist.
- **`environment: production` deployment branches:** The environment's deployment branch policy must allow only the protected control-plane branch set: `main` and eligible protected maintenance branches `release/<project-name>/v<release-line>`. Wildcard entries such as `release/**` are not allowed. No other branch may enter the production environment approval flow.
- **Workflow file ownership:** `.github/CODEOWNERS`, `.github/workflows/**`, `.github/actions/**`, `.github/publish-trust-inventory.json`, `.github/release-recovery-ledger.jsonl`, `eng/scripts/**`, `**/release.json`, `**/version.json`, `hk.pkl`, `PklProject`, `PklProject.deps.json`, `mise.toml`, `mise.lock`, and any other trusted control-plane helper code used by official release jobs must be protected by `CODEOWNERS` review from a dedicated release-engineering group on every branch in the protected control-plane branch set. Protected control-plane branches must also require code-owner review in their rulesets configuration. `job_workflow_ref` constrains which workflow file can mint publish credentials, but it does not prove the content hash of that file.
- **GitHub App credentials:** Before first use, release engineering must provision three GitHub Apps and store their private keys in the narrowest possible GitHub secret scopes: a read-only control-plane metadata App for `preflight-check` and CI trust validation, a release-tag writer App whose private key is stored as an `environment: production` secret for `create-release-tag`, and a buddy-tag writer App scoped only to the repository for `create-traceability-tag`. These Apps must request only the repository permissions required for their single purpose.
- **OIDC trust policies:** Each external registry must be configured with the strongest claim set it supports, without assuming portable wildcard future-branch trust. The authoritative branch restriction is the GitHub `environment: production` deployment branch policy. Registry-side trust must at minimum bind the repository, the **called reusable publish workflow** path, and `environment = "production"`. Exact caller-ref binding must be required where the target registry supports it; where it does not, branch restriction relies entirely on the protected GitHub environment branch policy.
- **OIDC claim support matrix:** Use the following support assumptions until the design is revised. `NuGet.org`: require repository + called publish workflow path + `environment = "production"`, and also require exact caller-ref enumeration. `PyPI`: require repository + called publish workflow path + `environment = "production"`; no portable exact caller-ref binding is assumed. `npmjs`: require repository + called publish workflow path + `environment = "production"`; no portable exact caller-ref binding is assumed. `RubyGems.org`: require repository + called publish workflow path + `environment = "production"`; no portable exact caller-ref binding is assumed. For PyPI, npmjs, and RubyGems.org, exact production-branch restriction therefore comes from the GitHub deployment-branch policy, not from a registry-side branch claim.
- **Trusted publish change management:** Because Trusted Publisher configuration is coupled to workflow file path and the allowed protected control-plane branch set, any rename of a protected control-plane branch, any addition or retirement of an allowed protected maintenance branch ref, any change in target auth mechanism, or any move/rename of an OIDC-backed publish workflow (`_publish-nuget.yml`, `_publish-npm.yml`, `_publish-pypi.yml`, `_publish-rubygems.yml`) must be accompanied by registry-side configuration updates before the next release. The checked-in publish trust inventory at `.github/publish-trust-inventory.json` must be updated in the same reviewed PR for every official target whose trusted workflow path, caller-ref set, or auth mechanism changed, including `github:official`. CI enforces repository-side drift by running the explicit `trusted-release-inventory` job in `ci.yml`, which must compare the post-change trust-bearing state rather than merely checking whether both the inventory file and another control-plane file were edited. The comparison scope is exactly `entryWorkflowPath`, the deduplicated set of fully qualified `allowedCallerRefs`, the `publishWorkflowPaths` mapping, and the `targetAuthMechanisms` mapping for every official target. Order-only differences in `allowedCallerRefs` are not meaningful, but every added, removed, renamed, or remapped caller ref, publish workflow path, or auth mechanism is a hard mismatch. CI must fail any control-plane change for which those post-change values do not exactly match the checked-in inventory, whether or not `.github/publish-trust-inventory.json` itself changed.

**Publish trust inventory schema:** The checked-in inventory is part of the trusted control plane and must be read from the current protected source workspace for the run. It uses `schemaVersion: 1` and records the entry workflow, exact caller refs, the publish workflow path for each official target, and the expected auth mechanism for each target:

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
    "targetAuthMechanisms": {
        "nuget:official": "oidc",
        "npm:official": "oidc",
        "pypi:official": "oidc",
        "rubygems:official": "oidc",
        "github:official": "github-token"
    }
}
```

The inventory uses fully qualified Git refs under `allowedCallerRefs`, repository-relative workflow paths, an explicit target-to-workflow mapping, and an explicit target-to-auth mapping so `resolve-context` can validate exactly which reusable publish workflows and auth paths are trusted for the filtered official target set.

The checked-in inventory is an in-repository drift detector and audit trail, not an independent cryptographic proof of registry-side trust state. An actor who can merge arbitrary changes into the protected control-plane branch set can change both the workflow code and the inventory together. Its purpose is to make trust changes reviewable and to catch accidental repository-side drift before production approval is consumed.

**Jobs:**

1. **`preflight-check`**:
    - Runs before `resolve-context`.
    - `permissions: {}`
    - This job must not perform repository checkout. It must mint a dedicated read-only GitHub App installation token just in time and use that token for both GitHub Environments API reads and GitHub Repository Rulesets API reads. A long-lived PAT is not the normal path. The GitHub App private key must be stored as an organization-level Actions secret scoped only to this repository, not as a repository-level secret. If a temporary fallback secret is ever required before the GitHub App path exists, it must be treated as an emergency-only exception, kept out of repository-level secrets, and removed once the GitHub App path is in place. Treat the default `GITHUB_TOKEN` as insufficient for this job; weakening or skipping the verification is unsupported.
    - Verifies that `environment: production` already exists, includes at least one required-reviewer protection rule, has `prevent_self_review` enabled, and restricts deployment branches to the official protected control-plane branch set.
    - Uses the GitHub Environments API response directly: the check must look for a `protection_rules` entry with `type == "required_reviewers"` and a non-empty reviewer list, must verify `prevent_self_review == true`, and must verify that the deployment branch policy contains only exact branch names for `main` plus the registered protected maintenance branches in the official protected control-plane branch set. Wildcard or pattern-based entries such as `release/**` are hard failures. Because the API returns short branch names rather than fully qualified refs, this job must normalize the expected caller refs by stripping the `refs/heads/` prefix before comparison. A wait timer or branch policy alone is not sufficient. This job verifies the protection quality of every branch already listed in that deployment policy; completeness of the allowed caller-ref set is enforced separately by the publish trust inventory preflight in `resolve-context`.
    - Uses the GitHub Repository Rulesets API only. It must verify that active branch rulesets protect `main` and every non-`main` branch currently allowed by the production environment with the same required PR review, required code-owner review, required status check `ci-passed`, no direct pushes, and no force-pushes. It must also verify that active tag rulesets protect both `refs/tags/release/**` and `refs/tags/buddy/**` against unauthorized creation and updates, and that their bypass actors are limited respectively to the dedicated release-tag writer App or buddy-tag writer App plus the dedicated release-engineering emergency-cleanup group.
    - Treats every GitHub API error as a hard failure. Specifically: `404` from environment endpoints means the environment is missing; a successful API response that lacks the required reviewers rule, has `prevent_self_review` disabled, has a wildcard deployment branch policy, lacks the required branch or tag rulesets, or applies a weaker ruleset profile than `main` means the environment is misconfigured; every other non-`200` response blocks the workflow as an environment-verification failure.
    - Fails hard if the environment is missing or unprotected. This turns the documented prerequisite into an executable guardrail.
    - All GitHub API calls in this job must set an explicit client timeout so the guard fails fast rather than consuming the full job timeout on a hung response.
    - This check is still an audit-before-use guard, not a transactional lock. If an administrator weakens or deletes environment protection after `preflight-check` passes but before a publish job reaches `environment: production`, the later GitHub environment evaluation remains authoritative. The same residual TOCTOU window exists for tag rulesets: `preflight-check` validates the live ruleset configuration at job start, while `create-release-tag` is still subject to whatever tag ruleset is live at push time. Those residual windows are accepted and must be controlled through CODEOWNERS, repository audit logs, and change discipline around production protection settings.

2. **`resolve-context`**:
    - `needs: [preflight-check]`
    - `permissions: { contents: read }`
    - **Input validation (first step, before checkout):** Validate `project-name` with a full-string match against `[A-Za-z0-9][A-Za-z0-9._-]*`, reject any occurrence of `..`, reject trailing `.`, and reject any name that ends with `.lock`. Reject invalid names with a clear error.
    - **Runner and tooling:** Runs on `ubuntu-latest`. Like `resolve-context` in `buddy.yml`, version resolution uses the repository-local `nbgv-python` adapter from the checked-out source ref and does not require a Windows runner even for C# projects. The job must hard-fail if `mise.lock` is absent, and should restore a tool cache keyed by `mise.toml` and `mise.lock` before invoking `mise install`. If the lockfile needs regeneration, that is an out-of-band repository change performed with `mise lock`, not a workflow fallback. If `nbgv-python` cannot resolve the version deterministically, the job must hard-fail; there is no fallback or manual override path in this design.
    - **Source checkout:** Check out the dispatch-selected protected source ref for this workflow run with `fetch-depth: 0` and `persist-credentials: false`. In `official.yml`, that source workspace is both the trusted control-plane checkout and the release payload input.
    - Runs `eng/scripts/find_project_path.py` to resolve `language` and `project-path` from `project-name`. `project-name` is case-sensitive and must resolve to exactly one project in the repository. The resolution step must emit exactly one of `{csharp, python, jsts, ruby}` for `language`; no match, ambiguous match, unsupported language, or resolver error is a hard failure.
    - **NBGV resolution and semantic validation:** Resolve the version with `nbgv-python`, hard-fail if that resolution is non-deterministic, and use the resolved value as the workflow output `version`. Here, "non-deterministic" has the same meaning as in `buddy.yml`: no unique governing `version.json`, no unique normalized version string from the selected full-history checkout, or validator rejection of the resolved string. Then validate that resolved version using `eng/scripts/validate_semver2_version.py` (NuGet and npm), `eng/scripts/validate_rubygems_version.py` (the repository's supported RubyGems-compatible subset), or `eng/scripts/validate_pep440_version.py` (Python), chosen after the project language is known.
    - **Official branch-line validation:** Derive `<release-line>` using the release-line derivation rule above. At the start of this job, capture a single comparison SHA for `origin/main` and compute the `main` release line from that frozen snapshot only; do not recompute against a moving `origin/main` later in the run. If the resolved `<release-line>` matches that frozen `main` release line, the current caller ref must be `refs/heads/main`. If it differs, the current caller ref must be exactly `refs/heads/release/<project-name>/v<release-line>`, and that protected maintenance branch must already exist. The workflow must not accept a non-`main` release line from `main`, and must not accept a `main` release line from a maintenance branch.
    - Reads `release.json` from the selected source workspace, validates it exactly as specified in **Section 5**, applies the same language-target validation rule as `buddy.yml`, then filters to the official target set `{nuget:official, npm:official, pypi:official, rubygems:official, github:official}` and fails if the filtered set is empty.
    - **Publish trust inventory preflight:** After the official target set is resolved, read `.github/publish-trust-inventory.json` from the current protected caller ref, validate `schemaVersion == 1`, verify that `entryWorkflowPath` is exactly `.github/workflows/official.yml`, verify that the current caller ref is present in `allowedCallerRefs`, verify that every filtered official target maps to the expected reusable publish workflow path via `publishWorkflowPaths`, and verify that every filtered official target maps to the expected auth mode via `targetAuthMechanisms` (`oidc` for production registries, `github-token` for `github:official`). This catches repository-side trust drift before any production approval is consumed. Because registry-side trust settings are not queried portably, matching registry updates are still a mandatory operational step.
    - **Official release tag derivation and overwrite guard:** Derive `tag-name = release/<project-name>/v<version>`. If that protected official release tag already exists and points to a different commit, fail immediately. If it already exists and points to the current commit, treat the tag reservation as an idempotent no-op. If `github:official` is among the resolved targets, check GitHub Releases state for that derived tag. If no GitHub Release exists for that derived tag, proceed — this is the normal first official run. Official GitHub Releases must use a deterministic release title `<project-name> v<version>`. The guard must scan GitHub Releases to completion, following pagination across non-pre-release releases including drafts, and must hard-fail on API, authentication, authorization, rate-limit, transport, or response-shape errors. An interrupted, truncated, or otherwise incomplete scan is `unknown`, not `not found`. Match that deterministic stable title across the completed stable-release set and fail immediately if the same title already exists under a different tag or commit. A draft or published stable release with that deterministic title is part of the same stable identity space; the design does not treat drafts as a separate namespace. If a pre-release GitHub Release exists for the same derived tag, `_publish-github.yml` may promote it to a stable release only after remote asset identity checks confirm that the existing pre-release assets match the current local build output; a divergent same-tag pre-release is a hard failure. If a non-pre-release GitHub Release already exists for the same derived tag, defer the idempotent/no-op decision to `_publish-github.yml`, which must verify remote asset identity before reporting success.
    - **Outputs:** `tag-name`, `language`, `project-name`, `project-path`, `version`, `targets` (JSON array of filtered official targets).

3. **`static-analysis`**:
    - `needs: [resolve-context]`
    - `permissions: { contents: read }`
    - Checks out the source ref for this workflow run before enumerating files. Read-only checkout must use `persist-credentials: false`.
    - Runs `hk check <project-path>` scoped to the resolved project path. HK receives the project path directly and discovers applicable files under that path according to `hk.pkl`; this design does not pre-enumerate file names in shell.

4. **`clean-build`** (`build-csharp` / `build-python` / `build-jsts` / `build-ruby`):
    - For supply chain security, no prior artifacts are reused. A fresh build and test run is performed from the exact dispatch-selected commit for this workflow run. The checkout must use `fetch-depth: 0` for NBGV resolution.
    - Uses the same four static conditional build jobs pattern as `buddy.yml`, with `permissions: { contents: read }`, `secrets: {}`, and the required `with:` inputs wired from `needs.resolve-context.outputs.project-path`, `needs.resolve-context.outputs.project-name`, and `checkout-ref: ${{ github.sha }}`. Each build job depends on both `resolve-context` and `static-analysis`. Only the language-matching build job executes; the others are skipped.

5. **`create-release-tag`**:
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby]`
    - `if: always() && !cancelled() && !failure() && needs.resolve-context.result == 'success' && needs.static-analysis.result == 'success' && ((needs.resolve-context.outputs.language == 'csharp' && needs.build-csharp.result == 'success' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'python' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'success' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'jsts' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'success' && needs.build-ruby.result == 'skipped') || (needs.resolve-context.outputs.language == 'ruby' && needs.build-csharp.result == 'skipped' && needs.build-python.result == 'skipped' && needs.build-jsts.result == 'skipped' && needs.build-ruby.result == 'success'))`
    - `permissions: { contents: read }`
    - `environment: production` — mandatory. Tag reservation must occur only after the production approval gate is satisfied.
    - Mints a dedicated release-tag writer GitHub App installation token from an `environment: production` secret and uses that token for the tag push. The job `GITHUB_TOKEN` is never the bypass actor for `refs/tags/release/**`.
    - Creates the protected official release-identity tag `release/<project-name>/v<version>` at the current workflow commit after approval and before any official publish job becomes eligible. Reserving the official identity before per-destination publish is still intentional in this design; recovery rules for abandoned or partially used reservations are defined in Section 7.
    - **Tag creation logic:** If the tag does not exist, create it. If it already exists and points to the same commit, succeed as an idempotent no-op. If it exists but points to a different commit, fail immediately. There is no force path for official release tags.
    - Checks out the current source ref read-only, then configures git explicitly to push with the minted GitHub App installation token. The job must not persist the default `GITHUB_TOKEN` as a write-capable remote credential.

6. **Publish jobs** (static conditional, one job per official ecosystem-destination pair):
    - Uses the same per-destination split structure as `buddy.yml`, but official targets now include `github:official` in addition to the production package registries. Unlike buddy, official publish jobs do not need to restate the full language-matching build predicate in each `if:` guard because `create-release-tag` is already gated on resolver success, static-analysis success, and the exact single-build-success pattern for the resolved language.
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, create-release-tag]`
    - `environment: production` — **mandatory**, not optional. This enables human approval gates and OIDC token issuance. Each destination still triggers its own approval step. This trades operator convenience for per-destination isolation of approvals and tokens. If reviewer fatigue becomes material later, migrate to a single reviewed gate plus destination-specific non-reviewed environments.
    - Package-registry publish jobs use `permissions: { id-token: write }` for OIDC Trusted Publishing. `publish-github-official` uses `permissions: { contents: write }`.
    - Because `official.yml` may run only from the protected control-plane branch set, no separate runtime assertion is required here to distinguish the caller branch from the trusted control-plane source. The production environment branch policy and branch protections carry that responsibility.
    - All official publish jobs use `secrets: {}`. OIDC and the automatic `GITHUB_TOKEN` are the default mechanisms; no blanket secret inheritance is allowed.
    - Each publish step uses idempotent publish logic from the protected control-plane branch set. Duplicate-version outcomes (`409`, `422`, or tool-equivalent "already exists" responses) count as success only after the workflow verifies that the already-published remote artifact set matches the local artifact set and expected digests. Authentication failures, authorization failures, malformed artifacts, and upstream `5xx` errors remain hard failures. This design intentionally does not retry upstream `5xx` failures inside a single run; operator recovery happens by re-running the workflow.

    ```yaml
    publish-nuget-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, create-release-tag]
        permissions:
            id-token: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'nuget:official')
        uses: ./.github/workflows/_publish-nuget.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
            feed-url: https://api.nuget.org/v3/index.json
        secrets: {}

    publish-npm-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, create-release-tag]
        permissions:
            id-token: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'npm:official')
        uses: ./.github/workflows/_publish-npm.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
            registry: https://registry.npmjs.org
        secrets: {}

    publish-pypi-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, create-release-tag]
        permissions:
            id-token: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'pypi:official')
        uses: ./.github/workflows/_publish-pypi.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
        secrets: {}

    publish-rubygems-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, create-release-tag]
        permissions:
            id-token: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'rubygems:official')
        uses: ./.github/workflows/_publish-rubygems.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
            host: https://rubygems.org
        secrets: {}

    publish-github-official:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, create-release-tag]
        permissions:
            contents: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            needs.create-release-tag.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets || '[]'), 'github:official')
        uses: ./.github/workflows/_publish-github.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            project-name: ${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
            tag-name: ${{ needs.resolve-context.outputs.tag-name }}
            prerelease: false
        secrets: {}
    ```

7. **`release-complete`**:
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, create-release-tag, publish-nuget-official, publish-npm-official, publish-pypi-official, publish-rubygems-official, publish-github-official]`
    - `if: always()`
    - `permissions: {}`
    - Performs the terminal correctness check for official releases. It must first assert that `resolve-context.result == "success"`, `static-analysis.result == "success"`, and `create-release-tag.result == "success"`. It must then parse `targets` as JSON, assert that the filtered target set is non-empty, map that set to the exact publish jobs `{nuget:official -> publish-nuget-official, npm:official -> publish-npm-official, pypi:official -> publish-pypi-official, rubygems:official -> publish-rubygems-official, github:official -> publish-github-official}`, and assert that every selected target finished with `result == "success"` and a valid `publish-result` output in `{new-publish, no-op}`.
    - It must also assert that every non-selected publish job finished with `result == "skipped"`.
    - It must also assert that the single language-matching build job finished with `result == "success"`; the three non-matching build jobs must be `result == "skipped"`.
    - The normative jq skeleton is:

    ```yaml
    - name: Assert official release completeness
      env:
          NEEDS_JSON: ${{ toJson(needs) }}
      run: |
          echo "$NEEDS_JSON" | jq -e '
              . as $n
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
              and ($n["create-release-tag"].result == "success")
              and (
                  (($n["resolve-context"].outputs.targets // "[]") | fromjson) as $targets
                  | ($targets | length) > 0
                  and ($targets | all(. as $target | $map.publishJobs[$target] != null))
                  and ($targets | all(. as $target | $n[$map.publishJobs[$target]].result == "success"))
                  and ($targets | all(. as $target | ($n[$map.publishJobs[$target]].outputs["publish-result"] == "new-publish" or $n[$map.publishJobs[$target]].outputs["publish-result"] == "no-op")))
                  and (([$map.publishJobs[]] - ($targets | map($map.publishJobs[.])))
                      | all(. as $job | $n[$job].result == "skipped"))
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
    "targets": ["nuget:gpr", "nuget:official", "github:release", "github:official"]
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
- In this design, Python has no unofficial registry target. A Python project that wants a buddy preview must include `github:release`. That preview is not a package index and therefore is not directly installable through normal `pip install` flows.
- Removing a previously used target takes effect immediately because backward-compatibility shims are intentionally out of scope before implementation starts. For example, removing `github:official` stops GitHub Release reconciliation on subsequent official runs and leaves any existing stable release unchanged until operators update it manually.
- Unsupported future schema versions are hard failures with operator guidance. Because implementation has not started, schema upgrades are coordinated changes rather than backward-compatible migrations.
- RubyGems versions use the repository's explicit subset policy: `MAJOR.MINOR.PATCH[.suffix...]`, no leading `v`, no `-` or `+`, suffix segments limited to `[0-9A-Za-z]+`, and any suffix must contain at least one letter. Numeric-only suffix chains such as `1.2.3.1` are rejected.

**Project resolution contract:**

- `project-name` is case-sensitive, must identify exactly one project in the repository, and must reject any occurrence of `..`, any trailing `.`, and any `.lock` suffix for ref safety.
- Releasable `project-name` values must be unique under ASCII lowercase normalization across the repository so workflow concurrency keys cannot alias distinct projects.
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
| `github:release`    | Unofficial | `buddy.yml`    | Create or update a GitHub pre-release with downloadable assets    |
| `github:official`   | Official   | `official.yml` | Create or update a stable GitHub Release with downloadable assets |

**Language-target compatibility matrix:**

| Resolved `language` | Allowed targets             |
| ------------------- | --------------------------- |
| `csharp`            | `nuget:*`, `github:*`       |
| `jsts`              | `npm:*`, `github:*`         |
| `python`            | `pypi:official`, `github:*` |
| `ruby`              | `rubygems:*`, `github:*`    |

`buddy.yml` filters to unofficial targets only. `official.yml` filters to official targets only. A `release.json` may declare targets from both channels, but opposite-channel filtering happens only after strict validation; unknown targets are hard failures.

## 6. Reusable Workflow I/O Contracts

### Global Reusable Workflow Rules

All reusable workflows share these constraints:

- They must NOT declare their own `permissions:` blocks. Caller jobs own permission grants.
- They must use the same shell input-hardening rule as entry workflows: map `${{ inputs.* }}`, `${{ github.* }}`, `${{ needs.*.outputs.* }}`, and any other untrusted context expression to `env:` first, then reference quoted shell variables inside `run:` steps.
- Official release workflows may execute only trusted control-plane helper code sourced from the dispatch-selected protected control-plane branch. Official release tags are workflow-created outputs, not alternative sources of privileged code.
- They must treat artifact validation failures, auth failures, and upstream service failures as hard failures unless a specific duplicate-version case is explicitly documented as idempotent.

### Build-Test Workflows

All four build-test workflows share the same input/output structure:

| Input          | Type     | Required | Description                                                                                                          |
| -------------- | -------- | -------- | -------------------------------------------------------------------------------------------------------------------- |
| `checkout-ref` | `string` | No       | Git ref or commit SHA that the build workflow must check out; defaults to the caller job's `github.sha` when omitted |
| `project-path` | `string` | Yes      | Path to the project directory within the repo                                                                        |
| `project-name` | `string` | Yes      | Project name (used for artifact naming)                                                                              |

| Output          | Type     | Description                                                     |
| --------------- | -------- | --------------------------------------------------------------- |
| `artifact-name` | `string` | Name of the uploaded CI Artifact: `build-output-<project-name>` |

**Required caller permissions:** `contents: read`

**Checkout behavior:** Build-test workflows perform their own checkout and must use `fetch-depth: 0` internally so NBGV and other git-history-derived metadata resolve correctly. These read-only checkouts must also use `persist-credentials: false`. When `checkout-ref` is provided, the reusable workflow must check out exactly that ref; when it is omitted, the reusable workflow must check out the caller job's `github.sha`. Buddy and official callers may pass the dispatch commit SHA explicitly for clarity, but the default behavior already targets the current workflow commit.

**Secrets:** `secrets: {}` — build-test workflows require no secrets. Callers must not pass `secrets: inherit` to avoid exposing publish credentials to build/test execution.

**Artifact convention:** Each build workflow uploads its output to CI Artifacts with the name `build-output-<project-name>`. Publish workflows download by this exact name. The artifact layout per ecosystem:

| Ecosystem | Expected artifact contents                                                                                                                                                       |
| --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| NuGet     | One or more `.nupkg` files; matching `.snupkg` symbol packages may also be included and should be pushed alongside the corresponding `.nupkg` when the destination supports them |
| npm       | One `.tgz` tarball (output of `npm pack` / `pnpm pack`)                                                                                                                          |
| PyPI      | One `.whl` and one `.tar.gz` (wheel + sdist)                                                                                                                                     |
| RubyGems  | One `.gem` file                                                                                                                                                                  |
| GitHub    | All top-level files in the artifact except `artifact-manifest.json` (uploaded as release assets)                                                                                 |

Every build artifact must also contain a manifest file at the artifact root named exactly `artifact-manifest.json` that lists each published file and its SHA-256 digest. That manifest is internal workflow metadata and must not be uploaded as a GitHub Release asset. Publish workflows must verify the downloaded files against that manifest before any publish step runs. The manifest schema is fixed and shared across ecosystems:

```json
{
    "schemaVersion": 1,
    "files": [
        {
            "path": "artifact-file-name.ext",
            "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
        }
    ]
}
```

`schemaVersion` must equal `1`. `files` must be a non-empty array. Each `path` must be a relative path to a top-level artifact file, and each `sha256` must be a lowercase hexadecimal SHA-256 digest for that exact file.

**Reproducibility requirement:** Build workflows must configure their packaging tools so reruns from the same source commit and lockfiles produce the same package-file identities. Where a package format embeds timestamps, file ordering, or host-specific metadata by default, the reusable build workflow must normalize those fields before publishing artifacts.

**Artifact retention:** CI artifacts are an ephemeral hand-off mechanism, not permanent release storage. Recommended defaults: `retention-days: 7` for PR and buddy runs, `retention-days: 45` for official runs. The longer official retention window is intentional recovery budget for partial publishes and approval delays, and it deliberately exceeds GitHub's 30-day deployment-approval expiry so operators still have a buffer after an approval timeout. It does not eliminate the dead-end case where artifacts expire and the protected branch has since moved, so Section 7 still defines that as a separate recovery boundary.

### Publish Workflows

All publish workflows share a common set of inputs, with ecosystem-specific additions:

| Input           | Type     | Required | Description                                    |
| --------------- | -------- | -------- | ---------------------------------------------- |
| `artifact-name` | `string` | Yes      | CI Artifact name to download (from build step) |
| `version`       | `string` | Yes      | Package version string                         |

**Ecosystem-specific inputs:**

| Workflow                | Input          | Type      | Description                                                                                                    |
| ----------------------- | -------------- | --------- | -------------------------------------------------------------------------------------------------------------- |
| `_publish-nuget.yml`    | `feed-url`     | `string`  | NuGet feed URL (GPR or NuGet.org)                                                                              |
| `_publish-npm.yml`      | `registry`     | `string`  | npm registry URL (GPR or npmjs)                                                                                |
| `_publish-pypi.yml`     | (none extra)   |           | Always publishes to PyPI via OIDC                                                                              |
| `_publish-rubygems.yml` | `host`         | `string`  | RubyGems host URL (GPR or RubyGems.org)                                                                        |
| `_publish-github.yml`   | `project-name` | `string`  | Project name, used for deterministic GitHub Release titles and diagnostics                                     |
| `_publish-github.yml`   | `tag-name`     | `string`  | Git tag for the GitHub Release                                                                                 |
| `_publish-github.yml`   | `source-commit-sha` | `string` | Exact source commit SHA that the GitHub Release API must use as `target_commitish` when the tag does not yet exist |
| `_publish-github.yml`   | `prerelease`   | `boolean` | Whether to mark the release as pre-release                                                                     |
| `_publish-github.yml`   | `force`        | `boolean` | Buddy-only optional flag controlling replacement of a non-matching pre-release GitHub Release; default `false` |

**Required caller permissions:**

| Workflow                | Required caller `permissions`                |
| ----------------------- | -------------------------------------------- |
| `_publish-nuget.yml`    | `packages: write` (GPR) or `id-token: write` |
| `_publish-npm.yml`      | `packages: write` (GPR) or `id-token: write` |
| `_publish-pypi.yml`     | `id-token: write`                            |
| `_publish-rubygems.yml` | `packages: write` (GPR) or `id-token: write` |
| `_publish-github.yml`   | `contents: write`                            |

**Secrets:** `secrets: {}` by default. If a future publish target requires an explicit credential, the caller must pass only that named secret. `secrets: inherit` is prohibited.

**Artifact validation:** Before publishing, each reusable publish workflow must verify that the expected files exist at the artifact root and fail on empty artifacts, missing required files, or ambiguous layouts. Duplicate-version outcomes count as idempotent success only when the remote artifact set matches the local artifact set and expected digests. `_publish-github.yml` must verify that at least one top-level non-manifest file exists in the downloaded artifact, must fail if release assets are nested under subdirectories instead of flattened at the artifact root, and must pass `target_commitish = source-commit-sha` whenever it calls the GitHub Releases API to create a release for a tag that does not yet exist.

For GPR targets, publish workflows must treat package versions as immutable within workflow execution. Even though GitHub supports package deletion and restoration with elevated package-admin capabilities, these reusable publish workflows do not request those permissions and must never delete package versions as part of a retry or recovery path.

**GitHub publish force semantics and tag contract:** `_publish-github.yml` accepts `force` only for the buddy pre-release path. The workflow input must declare `default: false`, buddy callers may pass it explicitly, and official callers do not pass it. Regardless of `force`, `_publish-github.yml` must hard-fail unless `(prerelease == true && tag-name == 'buddy/<project-name>/v<version>' && release title == '<project-name> v<version> (pre-release)')` or `(prerelease == false && tag-name == 'release/<project-name>/v<version>' && release title == '<project-name> v<version>')`. It must also hard-fail if it receives `force == true` together with `prerelease == false`. `force` never relaxes official stable-release protections. Whenever `_publish-github.yml` actually overwrites a buddy pre-release under `force=true`, it must append a human-readable record to `$GITHUB_STEP_SUMMARY` that states the old remote identity, the new local identity, and the operator-selected force path.

**npm dist-tag policy:** `_publish-npm.yml` must use an explicit dist-tag on every publish. Buddy publishes to GPR must use a non-stable dist-tag such as `buddy` and must never write `latest`. Official npmjs publishes may write `latest`, but `_publish-npm.yml` must not move the stable `latest` dist-tag backward in SemVer ordering. If an older official version is being published after a newer stable version already owns `latest`, the workflow must hard-fail instead of retagging `latest`. Publication chronology is not a substitute for semantic version comparison.

**GitHub release identity metadata and scan completion:** `_publish-github.yml` must use deterministic release titles. For official stable releases, the title must be `<project-name> v<version>`. For buddy pre-releases, the title must be `<project-name> v<version> (pre-release)`. Any GitHub Release scan used by this design, whether in `resolve-context` or in `_publish-github.yml`, must follow pagination until the relevant result set is exhausted and must hard-fail on API, authentication, authorization, rate-limit, transport, or response-shape errors. An interrupted, truncated, or otherwise incomplete scan is `unknown`, not `not found`; overwrite, no-op, and conflict decisions must never be made from a partial page. Because `buddy.yml` and `official.yml` are independent channels and may run concurrently, `_publish-github.yml` must repeat the deterministic stable-title conflict scan immediately before it mutates any GitHub Release record or release asset. On the buddy pre-release path, that last-minute scan must examine non-pre-release GitHub Releases, including drafts, for the deterministic stable title `<project-name> v<version>` and must hard-fail if that stable title exists. On the official stable path, that last-minute scan must likewise hard-fail if the same deterministic stable title exists under a different tag or commit than the current official release identity. `force=true` does not bypass any stable-title guard.

**Publish result signaling:** Each reusable publish workflow must emit a workflow output `publish-result` whose value is exactly `new-publish` or `no-op`. It must also append a one-line summary to `$GITHUB_STEP_SUMMARY` that records the target, the resolved release identity, and whether the outcome was `new-publish` or `no-op`. `release-complete` must aggregate those selected-target outcomes into its own step summary and must treat a missing or malformed `publish-result` output for a selected target as a hard failure. `publish-result` is orthogonal to the workflow job result: a selected publish target that proves remote identity matches local output must still finish with job `result == "success"` even when `publish-result == "no-op"`; `result == "skipped"` is reserved for non-selected targets only.

## 7. Overwrite and Idempotency Policy

Both `buddy.yml` and `official.yml` check for existing artifacts before proceeding. The policy differs by channel:

### Buddy (Unofficial)

| Condition                                                                                                           | Behavior                                                                               |
| ------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| Non-pre-release GitHub Release exists for the same version-specific stable title `<project-name> v<version>` (including drafts) | **Hard fail** — a draft or published stable release in that stable identity space must not be overwritten or shadowed by buddy |
| Pre-release GitHub Release exists for the supplied buddy tag with matching remote artifact identity                 | **Success** (idempotent no-op)                                                         |
| Pre-release GitHub Release exists for the supplied buddy tag with different remote artifact identity, `force=false` | **Fail** with guidance to re-run with `force=true`                                     |
| Pre-release GitHub Release exists for the supplied buddy tag with different remote artifact identity, `force=true`  | **Overwrite** allowed                                                                  |
| Traceability tag exists, same commit                                                                                | **No-op** (idempotent)                                                                 |
| Traceability tag exists, different commit, `force=false`                                                            | **Fail** with clear error                                                              |
| Traceability tag exists, different commit, `force=true`                                                             | **Force-update** tag                                                                   |
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
| Package version already exists at official registry with matching remote artifact identity         | **Success** (idempotent publish scripts)                                                       |
| Package version already exists at official registry with different remote artifact identity        | **Hard fail** — cut a new version; official registry versions are immutable release identities |
| Authn/authz failure or upstream `5xx` at official registry                                         | **Hard fail** — not idempotent                                                                 |

### Recovery Playbook

If a workflow run fails partway through (for example `nuget:gpr` succeeds but `npm:gpr` fails), use the first matching recovery path below and do not mix strategies:

1. If `preflight-check` fails, treat it as an environment or control-plane configuration issue rather than a source-code defect: fix whichever preflight invariant failed, including `environment: production` required reviewers, `prevent_self_review = true`, exact deployment branch names only, the required maintenance-branch protection profile, active branch/tag rulesets, and Rulesets API read-credential sufficiency, then trigger a new run.
2. If execution fails before any publish job starts in **buddy** (for example `resolve-context`, `static-analysis`, or build failure), fix the repository or configuration issue and trigger a fresh buddy workflow dispatch. No remote release state has been mutated yet.
3. If execution fails before any publish job starts in **official** and `resolve-context` never finished successfully, fix the repository or configuration issue and trigger a fresh official workflow dispatch from the intended protected branch. If the failure happened during publish trust inventory preflight, reconcile `.github/publish-trust-inventory.json`, the selected caller ref, the expected reusable publish workflow paths, and the expected target auth mechanisms on that control-plane branch before retrying. No remote official release state has been mutated yet.
4. If `resolve-context` succeeded but `static-analysis` or a build job later failed in **official** before `create-release-tag` succeeded, fix the source on the appropriate protected control-plane branch or supported maintenance branch and trigger a fresh official workflow dispatch. No official release tag or remote publish state has been mutated yet.
5. Distinguish between **Re-run jobs** and a fresh **workflow_dispatch**. Before choosing a rerun path, first verify in the GitHub Actions run UI or API that the original run's artifacts still exist, then check three separate GitHub lifetime boundaries on the original run: GitHub Actions' maximum workflow-run lifetime (approximately 35 days), GitHub's 30-day deployment-approval expiry, and the configured artifact retention window. These timers are independent. A retained artifact does not keep an expired run rerunnable, and a non-expired run does not revive an approval that already expired. After run expiry or artifact expiry, do not use GitHub's Re-run button; trigger a new workflow dispatch so the build artifacts are regenerated from source. After approval expiry, audit the official tag and publish state first, then choose recovery under the later rules below.
6. If the failure is transient (network issue, auth outage, or upstream `5xx`), prefer **Re-run failed jobs** on the same workflow run so the original commit, derived version, and derived official tag remain unchanged. If the failure path was a reviewer decline or approval expiry at an environment approval gate, use **Re-run all jobs** for that workflow run, because those paths settle as cancellation rather than a normal failed-job subset. A fresh official workflow dispatch is valid only when the selected protected branch still points to the same commit as the original run; otherwise it is a new release attempt and must be treated as such. Matching already-published artifacts must settle as idempotent no-ops.
7. If official publish jobs partially succeeded because some destinations were approved and others were declined or failed transiently, rerun the same official workflow run whenever possible. Already-published destinations must settle as idempotent no-ops, and the remaining destinations will request fresh approval. If the official tag was already created but all later approvals were declined or the run was cancelled, rerun the same workflow run or dispatch the same protected branch again while it still points to the same commit; the tag reservation must settle as an idempotent no-op. Do not retire or decommission that source branch until the partial-publish state has either been completed successfully or explicitly declared burned.
8. If an official tag reservation is no longer wanted after approvals were declined or after a maintenance-branch retirement cancelled the run, release engineering must resolve that explicitly rather than leaving an orphaned tag behind. The only supported abandon path is manual deletion of `release/<project-name>/v<version>` by a member of the dedicated release-engineering group explicitly configured as a bypass actor on the active `refs/tags/release/**` tag ruleset, followed by a fresh official release attempt from a later intended-release commit on an active protected branch so the workflow derives a different release identity. Silent abandonment of orphaned official tags is unsupported.
9. If official `resolve-context` fails because a non-pre-release GitHub Release, including a draft, already occupies the deterministic stable title `<project-name> v<version>` under a different tag or commit, stop rerunning immediately. A draft stable release is part of the same stable identity space as a published stable release and blocks the new official attempt by design. Release engineering must either preserve that existing stable identity and cut a different version from a corrected commit, or manually delete the conflicting draft or stable release and reconcile any associated `release/<project-name>/v<version>` tag through the authorized bypass path before rerunning. Renaming the GitHub Release title to sidestep the deterministic-title guard is unsupported.
10. If artifacts expired for an official run but the selected protected branch still points to the same commit, trigger a fresh official workflow dispatch from that same branch and let the rebuilt artifacts prove the same release identity. If artifacts expired and the protected branch has already moved to a different commit, stop trying to complete the old partially published identity. Treat that earlier version as burned or partially released, fix the source on the correct branch, and continue with the next version derived from the corrected commit.
11. If `release-complete` fails because a selected publish job was skipped, an unexpected non-selected job ran, or any other target-to-job mapping assertion failed, stop rerunning immediately. Treat that as workflow wiring drift or other control-plane code defect, fix the workflow via the normal protected-branch review path, and only then dispatch again.
12. If the failure is caused by malformed build output or a remote artifact identity mismatch, stop retrying the same release identity. For buddy, `force=true` applies only to GitHub pre-release asset replacement and buddy traceability tag re-pointing; it does **not** apply to GPR package versions. If a GPR package version already exists with different artifact identity, cut a new version rather than deleting and republishing. For official, if the immutable official release tag was already created for that failed attempt, do not retarget it. Fix the source on the correct protected branch and run official again so the workflow derives a new release identity from the corrected commit.
13. If buddy publish steps succeeded but `create-traceability-tag` failed, first retry the same buddy workflow inputs rather than creating the tag manually. Matching publish targets should settle as idempotent no-ops, and the rerun should complete the missing tag.
14. If that buddy rerun still fails because the traceability tag already exists on a different commit, choose explicitly between the only two supported recovery paths: rerun with `force=true` to replace the buddy pre-release assets and re-point the buddy traceability tag, or manually resolve the conflicting buddy tag state first and then rerun with `force=false`. `force=true` changes both the buddy release assets and the buddy traceability tag and may affect downstream automation that follows that tag.
15. If a queued buddy run has become stale because an earlier run already published or moved the project forward, cancel the queued run rather than letting it fail late on overwrite checks. This is especially important when a newer queued run displaced an older `force=true` intent for the same concurrency group.
16. If official publish jobs fail with authentication or authorization errors immediately after a new maintenance branch, trusted workflow path, or protected control-plane branch change was introduced, diagnose the mismatch direction explicitly. If repository-side publish trust inventory preflight fails first, fix `.github/publish-trust-inventory.json` or roll back the registry-side trust change before retrying. If publish trust inventory preflight succeeds but publish still fails at the registry, verify and restore the registry-side Trusted Publisher configuration for the checked-in caller refs, publish workflow path, and expected auth mechanism. Treat both cases as control-plane configuration drift, not as a package-content defect.
17. If `create-release-tag` pushed the official tag and the runner crashed before the job result was recorded, rerun the same workflow run. Tag creation must settle as an idempotent no-op, after which the remaining official publish jobs can continue through the normal approval and idempotency flow.

The repository must maintain a durable recovery ledger at `.github/release-recovery-ledger.jsonl`, outside ephemeral workflow logs, for every burned or partially published official release identity. Each line must be a standalone JSON object with `schemaVersion: 1`, `recordedAt`, `projectName`, `version`, `reservedTag`, `sourceCommit`, `workflowRunUrl`, `disposition`, `operatorRationale`, and optional `followUpIssue`. The ledger is trusted control-plane state: updates must go through the protected control-plane branch set under `CODEOWNERS` review. Every incident that burns or partially publishes an official release identity must add or update a ledger entry before the incident is considered operationally closed.

Operators must also treat GitHub's lifetime limits as first-class recovery boundaries. GitHub's 30-day deployment-approval expiry, GitHub Actions' maximum workflow-run lifetime (approximately 35 days), and the recommended 45-day official artifact retention are distinct timers with different failure modes. If an approval expires or the run itself expires, audit the resulting `release/**` tag state against completed `release-complete` runs before choosing recovery, even if artifacts are still retained. If artifacts later expire as well, the original run is no longer recoverable and a fresh dispatch from the same still-unchanged protected branch is the only supported rebuild path. The repository must maintain an operational audit that, at least once every 7 days and after every approval-expiry incident, enumerates protected `release/**` tags, confirms each one corresponds either to a completed official release or to an explicitly tracked burned identity under the recovery policy, and records that audit outcome in `.github/release-recovery-ledger.jsonl`.

## 8. Build Provenance

Until full provenance attestation is implemented, build jobs must emit the artifact manifest and digests described in Section 6, and publish jobs must verify that manifest before any upload.

Full provenance attestation is considered implemented only when the protected control-plane branch contains checked-in attestation steps for every supported official publish path in this design and repository policy fails any official workflow change that would allow a selected official target to publish without a successful attestation output. After that condition is met, every official build job must produce provenance for each published artifact using the platform-native mechanism for that ecosystem, and every official publish path must fail if the required attestation is missing, failed, or does not bind the exact published artifact set to the workflow run, source commit, and repository identity. GitHub-hosted artifacts should use `actions/attest-build-provenance` or its supported successor; ecosystem-native mechanisms such as PyPI Trusted Publishing provenance, `npm attest`, and any adopted NuGet provenance or signing path may satisfy this requirement if they provide equivalent artifact-to-run binding. OIDC Trusted Publishing proves workflow identity at publish time; provenance attestation embeds that proof into the released artifact set so consumers can verify it offline.

## Summary of Key Design Properties

1. **PR speed maximized**: A JS-only PR never waits for the Windows C# build queue.
2. **Channel isolation**: `buddy.yml` publishes only to unofficial registries plus optional GitHub pre-releases (`github:release`). `official.yml` publishes only to production registries plus optional stable GitHub Releases (`github:official`). Neither channel requires the other to run first for registry delivery.
3. **Static conditional dispatch**: Because `uses:` paths must be static, both build and publish jobs use conditional `if:` guards instead of dynamic matrix dispatch to reusable workflows. Each ecosystem-destination pair has its own dedicated job.
4. **Tag isolation**: Official release identity uses `release/<project-name>/v<version>`. Buddy traceability uses `buddy/<project-name>/v<version>`. The unofficial channel no longer writes into the official release-identity namespace.
5. **Overwrite-safe with force escape hatch**: Buddy guards against overwriting stable releases and uses a privileged `force=true` path only where the design explicitly permits replacing a non-matching pre-release or re-pointing the unofficial traceability tag. In this revision, that privilege is accepted as policy-controlled rather than workflow-enforced. Official publishes are idempotent for the same release identity only when remote artifact identity matches the local build output, and they never rebind a stable release to a different tag or commit.
6. **Least-privilege security**: Workflow-level `permissions: {}` with per-job escalation; build and publish jobs default to `secrets: {}`; shell input hardening applies to reusable workflows as well as entry workflows; privileged official publish logic stays on the protected control-plane branch set (`main` plus eligible protected `release/*` branches); OIDC for production registries binds the strongest claim set each target supports, with `environment = "production"` and the called reusable publish workflow path as the baseline, exact caller-ref added where supported, and no wildcard branch trust; PyPI, npmjs, and RubyGems.org rely on the protected GitHub deployment-branch policy for branch restriction; `environment: production` with mandatory required-reviewer gates, exact deployment branch names, and repository-ruleset verification remains the authoritative branch scope; protected `.github/workflows/**`, official source branches, official `release/**` tags, and isolated unofficial `buddy/**` tags.
7. **Terminal completeness checks**: `buddy.yml` and `official.yml` both end with a `release-complete` gate that proves the selected target set is non-empty, every selected publish target actually succeeded, only the non-selected publish jobs were skipped, and the single language-matching build succeeded. A green workflow run without that terminal proof is not considered complete.
