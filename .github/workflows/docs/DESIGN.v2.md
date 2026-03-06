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

This design intentionally uses the workflow files from the protected control-plane branch set rather than historical workflow files from the tagged commit. For `official.yml`, that protected control-plane branch set is the default branch `main` plus eligible protected maintenance branches `release/<project-name>/v<release-line>`. In other words, source code is released from the tagged commit, but release orchestration remains centralized in the current workflow definitions from that protected branch set. Because this repository has not started implementation yet, the design chooses this centralized control-plane model explicitly instead of preserving compatibility with older workflow revisions.

Trusted control-plane code follows the same rule. For `official.yml`, the caller workflow, every reusable workflow, every composite action, and every helper script that performs privileged release gating or publishing must come from the same protected control-plane branch set rather than the tagged source commit. Tagged source is built, tested, and statically analyzed as release payload input. Repository-owned source configuration intentionally evaluated as part of that payload, such as `hk.pkl`, follows the tagged source commit rather than the protected control-plane branch set.

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
| Read environment metadata           | `actions: read`        |
| Push tags                           | `contents: write`      |
| Create GitHub Release               | `contents: write`      |
| GitHub Packages (any feed)          | `packages: write`      |
| OIDC publish to official registries | `id-token: write`      |

All four official registries (NuGet.org, PyPI, npmjs, RubyGems.org) support OIDC Trusted Publishing. GPR feeds use `GITHUB_TOKEN` with `packages: write` instead.

> **Note:** With `permissions: {}` at workflow level, jobs that run `actions/checkout` or read GitHub release metadata must explicitly declare at least `permissions: { contents: read }`. Jobs that read GitHub environment metadata, protection rules, or deployment branch policies must declare at least `permissions: { actions: read }`. Build jobs included — without the required scope, the zero-permission `GITHUB_TOKEN` cannot clone the repository or read the environment metadata that release gating depends on.

**Concurrency policy:** Each entry workflow defines a `concurrency:` group to prevent resource races:

- `ci.yml`: `group: ci-${{ github.ref }}`, `cancel-in-progress: true`
- `buddy.yml`: `group: buddy-${{ inputs.project-name }}`, `cancel-in-progress: false`
- `official.yml`: `group: official-${{ inputs.tag-name }}`, `cancel-in-progress: false`

With `cancel-in-progress: false`, an in-progress run is preserved. GitHub Actions may still replace an older queued run with a newer queued run for the same concurrency group, so operators should not stack multiple fresh dispatches for the same official tag or buddy project and assume each queued run will execute.

**Job timeouts:** Every job must declare `timeout-minutes`, and workflow linting enforced through `hk`/`actionlint` should fail if any job omits it. Recommended defaults: resolution and static-analysis jobs `15`, Ubuntu build jobs `30`, Windows C# build jobs `45`, publish jobs `15`, and lightweight tag-management jobs `10`. Some YAML snippets below omit `timeout-minutes` only for brevity; concrete workflow files must still declare it.

**Action pinning:** All actions, including GitHub-maintained actions under the `actions/` namespace, must be pinned to full commit SHA. Use Renovate or Dependabot to manage updates:

```yaml
uses: dorny/paths-filter@de90cc6ed7cd597cb74b84a7e832ce805e3c7b15 # v3.0.2
```

The repository's dependency-update automation must cover `.github/workflows/**` so pinned SHAs are refreshed intentionally rather than drifting indefinitely.

## 2. `ci.yml` — PR Validation (Targeted Concurrency, Shift-Left)

**Trigger:** `on: pull_request`

CI does not build everything on every PR. It uses path filtering (`dorny/paths-filter`, SHA-pinned) to run only the affected language test suites.

**Jobs:**

1. **`static-analysis`**: Runs `jdx/hk` (`hk check --all`) on an Ubuntu runner. HK auto-detects file types from its configuration (`hk.pkl`), serving as the first gate for formatting and linting failures.

2. **`detect-changes`**: Uses `dorny/paths-filter` to classify modified files:
    - `csharp`: `['**/*.cs', '**/*.csproj', 'global.json', 'Directory.*.props', 'NuGet.Config', '**/*.targets', '**/packages.lock.json']`
    - `python`: `['**/*.py', 'pyproject.toml', 'uv.lock']`
    - `jsts`: `['**/*.ts', '**/*.js', 'package.json', 'pnpm-workspace.yaml', 'pnpm-lock.yaml', 'biome.jsonc', 'tsconfig*.json']`
    - `ruby`: `['**/*.rb', '**/*.gemspec', 'Gemfile', 'Gemfile.lock']`
    - `infra`: `['.github/workflows/**', '.github/CODEOWNERS', 'eng/scripts/**', '**/release.json', 'mise.toml', 'mise.lock', 'hk.pkl']`

    When `infra` changes are detected, all language test suites are triggered regardless of other filters.

    > **Scaling note:** The current filters operate at language level (`**/*.cs` triggers all C# builds). As the monorepo grows past ~10 projects per language, this should evolve to per-project granularity using affected-project detection from `eng/scripts/find_project_path.py`.

3. **`test-csharp` / `test-python` / `test-jsts` / `test-ruby`** (run in parallel):
    - `needs: [detect-changes, static-analysis]`
    - `permissions: { contents: read }`
    - Conditional: e.g. `if: needs.detect-changes.outputs.csharp == 'true' || needs.detect-changes.outputs.infra == 'true'`
    - Each calls its corresponding reusable workflow. C# uses `windows-latest`; the others use `ubuntu-latest`.

4. **`ci-passed`** (final gate job):
    - `if: always()`
    - `needs: [detect-changes, static-analysis, test-csharp, test-python, test-jsts, test-ruby]`
    - Asserts all required checks either passed or were legitimately skipped. Including both `detect-changes` and `static-analysis` in `needs` ensures their failures block the gate — if `detect-changes` fails, all `test-*` jobs are auto-skipped with `result: "skipped"`, and without `detect-changes` in `needs`, `ci-passed` would see only `"success"` and `"skipped"` results and falsely pass. This prevents the "skipped-all blocks required status checks" problem.

    ```yaml
    ci-passed:
        if: always()
        needs: [detect-changes, static-analysis, test-csharp, test-python, test-jsts, test-ruby]
        runs-on: ubuntu-latest
        steps:
            - name: Assert all required checks passed or were skipped
              env:
                  NEEDS_JSON: ${{ toJson(needs) }}
              run: |
                  echo "$NEEDS_JSON" | jq -e '
                                        (."detect-changes".result == "success")
                                        and (."static-analysis".result == "success")
                                        and ([
                                            ."test-csharp".result,
                                            ."test-python".result,
                                            ."test-jsts".result,
                                            ."test-ruby".result
                                        ] | all(. == "success" or . == "skipped"))'
    ```

## 3. `buddy.yml` — Unofficial Release (Static Conditional Publish, Tag Isolation)

**Trigger:** `on: workflow_dispatch` only (no automated triggers).

**Inputs:**

| Input          | Type      | Required | Description                                                                                                          |
| -------------- | --------- | -------- | -------------------------------------------------------------------------------------------------------------------- |
| `project-name` | `string`  | Yes      | Project identity to release                                                                                          |
| `force`        | `boolean` | No       | Allow buddy to replace non-matching GitHub pre-release assets and repoint buddy traceability tags (default: `false`) |

All workflow inputs must be mapped to intermediate environment variables before use in shell scripts (e.g., `env: PROJECT_NAME: ${{ inputs.project-name }}`; use `"$PROJECT_NAME"` in bash, never `${{ inputs.project-name }}` directly in `run:` blocks).

`force=true` is a **privileged** path. In this design revision, that privilege is recorded as policy rather than enforced by a separate workflow-level approval gate. This is an explicit pre-implementation risk acceptance: buddy overwrite authority is currently controlled by repository write access and release-operator discipline, and the workflow itself does not add a distinct protected-environment boundary yet.

Buddy is intentionally allowed to release from development branches. It does **not** require ancestry to `main` or to a maintenance release branch.

Even within the same language, different projects may have different packaging strategies (EXE, NuGet, wheel, etc.). The workflow resolves publish targets dynamically from project configuration.

**Jobs:**

1. **`resolve-context`**:
    - `permissions: { contents: read }`
    - **Runner and tooling:** Runs on `ubuntu-latest`. Requires `mise install` to bootstrap Python (for `eng/scripts/find_project_path.py`) and the .NET SDK (for NBGV via the `nbgv-python` adapter). The `mise.toml` and `mise.lock` at the repo root pin tool versions and, where supported by the selected MISE backends, the exact download digests. The job should restore a tool cache keyed by both files before invoking `mise install`.
    - **Input validation:** As the first step (before any checkout or git operation), validate `project-name` against the character class `[A-Za-z0-9][A-Za-z0-9._-]*`, and additionally reject `..` and trailing `.`. Reject invalid names with a clear error. This is stricter than the current helper script because leading option-like names are intentionally out of scope for releaseable project identities and the name must remain compatible with Git ref naming.
    - **Source ref policy:** Buddy intentionally permits dispatch from non-default branches. No ancestry check against `main` or any release branch is performed in this workflow.
    - Runs `eng/scripts/find_project_path.py` to determine the project path and the workflow language. `project-name` is case-sensitive and must resolve to exactly one project in the repository. The resolution step must emit exactly one of `{csharp, python, jsts, ruby}` for `language`; no match, ambiguous match, unsupported language, or resolver error is a hard failure.
    - **NBGV resolution:** The checkout must use `fetch-depth: 0` so NBGV can compute version height from git history. Read-only checkouts in this job must also use `persist-credentials: false`. All jobs that use NBGV or rely on git-history-derived metadata must also checkout with full history. The script locates the correct `version.json` by searching upward from the project directory. Version validation is performed programmatically using the existing scripts: `eng/scripts/validate_semver2_version.py` (for NuGet and npm), `eng/scripts/validate_rubygems_version.py` (for the repository's supported RubyGems-compatible subset), or `eng/scripts/validate_pep440_version.py` (for Python/PyPI).
    - Reads the project's release configuration (see **Section 5: Release Configuration Contract**) and emits a JSON array of publish targets. Targets use the format `ecosystem:destination` (e.g. `["nuget:gpr", "github:release"]`).
    - **Strictly validates** `release.json` exactly as specified in **Section 5** before any channel filtering occurs.
    - **Language-target validation:** Before channel filtering, validate every declared target against the resolved project language. `csharp` projects may declare only `nuget:*` and `github:*`; `jsts` projects may declare only `npm:*` and `github:*`; `python` projects may declare only `pypi:official` and `github:*`; `ruby` projects may declare only `rubygems:*` and `github:*`. Cross-ecosystem target declarations are hard failures.
    - After that validation succeeds, `buddy.yml` filters to the unofficial target set `{nuget:gpr, npm:gpr, rubygems:gpr, github:release}` and fails if the filtered set is empty. Targets that belong to the official channel are filtered out only **after** strict validation succeeds. Unknown or duplicate target values are hard failures. In this design, Python has no unofficial registry target; a Python project that wants a buddy preview must declare `github:release`.
    - **GitHub Packages immutability in workflow scope:** GitHub supports deleting and restoring package versions with elevated package-admin capabilities, but this design does not request delete or admin package permissions and does not support delete-and-republish recovery. Within this workflow design, GPR package versions are treated as immutable release identities.
    - **Overwrite guard:** Before proceeding, check whether a non-pre-release GitHub Release already exists for this project and version. If it does, fail immediately — stable releases must not be overwritten by buddy. Buddy GitHub pre-release overwrite and idempotency decisions are enforced later inside `_publish-github.yml` with remote asset identity checks and the caller's `force` input. Separately, if a buddy traceability tag under `refs/tags/buddy/<project-name>/v<version>` exists pointing to a different commit, allow overwrite only when `inputs.force` is `true`.
    - **Outputs:** `language`, `project-name`, `project-path`, `version`, `targets` (JSON array of filtered unofficial targets).
    - **On failure**, the script must print: the resolved project path, the contents of `release.json` if found, and the specific validation rule that was violated.

2. **`static-analysis`**:
    - `needs: [resolve-context]`
    - `permissions: { contents: read }`
    - Checks out the source ref for this workflow run before enumerating files. Read-only checkout must use `persist-credentials: false`.
    - Runs `hk check --files <file-list>` scoped to the resolved project path. The file list is generated by enumerating all files under `<project-path>/` (e.g., via `find` or `fd`) and passed directly to HK as normal file arguments. HK applies its configured linter rules based on file extensions and glob patterns defined in `hk.pkl`.

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

    Because GitHub Actions resolves `uses:` statically at parse time, and each reusable workflow call publishes to **exactly one** destination, publish jobs are split per ecosystem-destination pair. Each job has its own `if:` guard using `fromJson()` for exact array membership (not substring matching):

    ```yaml
    publish-nuget-gpr:
        needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            packages: write
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-context.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            contains(fromJson(needs.resolve-context.outputs.targets), 'nuget:gpr')
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
            contains(fromJson(needs.resolve-context.outputs.targets), 'npm:gpr')
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
            contains(fromJson(needs.resolve-context.outputs.targets), 'rubygems:gpr')
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
            contains(fromJson(needs.resolve-context.outputs.targets), 'github:release')
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
    - Including `static-analysis` directly in each publish job's `needs` is required for correctness: auto-skipped build jobs report `result: "skipped"`, not `"failure"`, so without `static-analysis` as a direct dependency the publish job could still evaluate its `if:` guard after a lint failure and degrade into an `artifact not found` failure.
    - For GPR targets, auth uses `GITHUB_TOKEN` with `packages: write`. No OIDC is needed.
    - All buddy publish jobs use `secrets: {}`. No repository, organization, or environment secrets are forwarded by default.
    - Each publish step uses idempotent publish logic. Duplicate-version outcomes (`409`, `422`, or tool-equivalent "already exists" responses) count as success only after the workflow verifies that the already-published remote artifact set matches the local artifact set and expected digests. Authentication failures, authorization failures, malformed artifacts, and upstream `5xx` errors remain hard failures. This design intentionally does not retry upstream `5xx` failures inside a single run; operator recovery happens by re-running the workflow.

5. **`create-traceability-tag`**:
    - `needs: [resolve-context, static-analysis, build-csharp, build-python, build-jsts, build-ruby, publish-nuget-gpr, publish-npm-gpr, publish-rubygems-gpr, publish-github-release]`
    - `if: always() && !cancelled() && !failure()`
    - `permissions: { contents: write }`
    - Assembles and pushes a lightweight Git tag: `buddy/<project-name>/v<version>`.
    - **Tag overwrite logic:** If the tag does not exist, create it. If it exists and points to the same commit, succeed as no-op. If it exists but points to a different commit: when `inputs.force` is `true`, force-update the tag; otherwise fail with a clear error message.
    - Checks out the source ref with credential persistence enabled before running `git push`, so `GITHUB_TOKEN` is available to the git remote.
    - Uses `${{ secrets.GITHUB_TOKEN }}` to run `git push origin <tag>`. Because `official.yml` is `workflow_dispatch`-only, pushing this tag does not trigger official release automation. Using `GITHUB_TOKEN` remains preferred defense-in-depth if a future maintainer later adds push-based triggers.
    - Buddy traceability tags are intentionally isolated from the official release-identity namespace. They are informational only and are never accepted as `official.yml` input.

## 4. `official.yml` — Production Release

**Important:** `buddy.yml` and `official.yml` are **independent release channels**, not a sequential promotion pipeline. Buddy publishes to unofficial registries and optional GitHub pre-releases via `github:release`; official publishes to production registries and optional stable GitHub Releases via `github:official`. A buddy run is NOT a prerequisite for an official run — either can be triggered independently for registry delivery and for GitHub Release delivery.

**Trigger:** `on: workflow_dispatch` only (no `push: tags:` trigger — `workflow_dispatch` is sufficient and avoids the bootstrapping-window risk where a tag trigger is live before the tag protection ruleset is verified).

```yaml
on:
    workflow_dispatch:
        inputs:
            tag-name:
                description: 'Release tag (e.g. release/my-project/v1.2.3)'
                required: true
                type: string
```

All workflow inputs must be mapped to intermediate environment variables before use in shell scripts (same pattern as `buddy.yml`).

**Caller ref policy:** In `workflow_dispatch`, the branch selected in the GitHub UI determines which revision of `official.yml`, its reusable workflows, and its trusted helper code executes. Under this design, that caller ref must be one of the protected control-plane branches only: `main` or an eligible protected maintenance branch `release/<project-name>/v<release-line>`. The caller ref is therefore constrained to the same protected branch set as the trusted control-plane code, while the release payload source remains the supplied tag.

Jobs that need both trusted control-plane helper code and tagged-source payload input should keep the caller-ref workspace available for those helper scripts and check out the tagged source into a separate working path. This preserves the centralized control-plane model without forcing source-owned payload configuration such as `hk.pkl` back onto the protected branch set.

**Tag checkout mechanism:** `resolve-tag` is a two-phase validation job. Before checkout, it validates the structural shape of `inputs.tag-name` (`release/<project-name>/v<version>`), the safe character set of `project-name`, and a conservative pre-checkout character class for `version`. The `project-name` character class is `[A-Za-z0-9][A-Za-z0-9._-]*`, with `..` and trailing `.` rejected explicitly. The `version` character class is `[A-Za-z0-9][A-Za-z0-9._+-]*`; PEP 440 epoch markers (`!`) are intentionally unsupported in this repository's release-tag format. After that structural validation, the job keeps the caller-ref workspace available for trusted helper scripts and checks out the tagged release payload into a separate working path using `ref: refs/tags/${{ inputs.tag-name }}` with `fetch-depth: 0` (not the branch HEAD selected in the dispatch UI). Read-only checkouts in this workflow must use `persist-credentials: false`. Once the tagged commit is available in that separate working path, the workflow resolves `language` and `project-path`, then runs the ecosystem-specific semantic version validator (`eng/scripts/validate_semver2_version.py`, `eng/scripts/validate_rubygems_version.py`, or `eng/scripts/validate_pep440_version.py`) against the extracted version string. Finally, it asserts that the tagged workspace `HEAD` matches `git rev-parse refs/tags/<tag>^{commit}` to handle both lightweight and annotated tags correctly, and that the tagged commit is reachable from either `origin/main` or the corresponding protected maintenance branch `origin/release/<project-name>/v<release-line>`. For ancestry purposes, `<release-line>` is derived from the version's base release segment only: SemVer uses the core `MAJOR.MINOR.PATCH` portion before any `-prerelease` or `+build` suffix, and PEP 440 uses the normalized release segment zero-padded to at least three numeric components before any pre/dev/post/local suffixes are removed. The final numeric segment of that base release segment is replaced with `x` (for example `1.2.3 -> v1.2.x`, `1.2.3-rc.1 -> v1.2.x`, `1.2.3rc1 -> v1.2.x`, `1.1 -> v1.1.x`, `1.2.3.4 -> v1.2.3.x`). This ordering avoids a pre-checkout language-decision loop while still preventing accidental releases from the wrong commit while permitting hotfix releases from protected maintenance branches.

**Maintenance branch policy:** A maintenance branch exists only for release lines that release engineering explicitly supports. It is created by release engineering from the first official release on that line, or immediately before the first hotfix on that line, using the exact name `release/<project-name>/v<release-line>`. Before that branch is used for any official release, it must receive the same protection profile as `main`: required PR review, required `ci-passed`, no direct pushes, and no force-pushes. If a tag resolves to a non-default release line and the matching maintenance branch does not exist, `official.yml` must fail with a clear error that prints the exact expected branch name and instructs the operator to either create and protect that maintenance branch or re-tag from `main`. Retired release lines are no longer eligible for official publication.

**Prerequisites (must be configured before first run):**

- **Branch protection** on the default branch, and on every maintenance release branch used for official hotfixes, must require PR review approval and the `ci-passed` required status check before merging, and must disallow direct pushes and force-pushes. Without this, direct pushes bypass `ci.yml` entirely, allowing unreviewed code to be released.
- **Tag protection** must use GitHub rulesets, or an equivalent mechanism that restricts both tag creation and tag updates, on `refs/tags/release/**` so that only release operators can create or modify official release tags. Legacy protection that only blocks deletion or force-push is insufficient for this design.
- **Buddy traceability tag protection** must likewise restrict both creation and updates on `refs/tags/buddy/**` outside the workflow path. Buddy traceability tags remain informational only and are intentionally outside the official release-identity namespace, but protecting them prevents pre-seeding and accidental traceability poisoning.
- **`environment: production`** must exist in GitHub repository settings with protection rules that include required reviewers and `prevent_self_review = true` **before** the workflow is ever triggered. If this environment does not pre-exist, GitHub auto-creates it with **zero** protection rules and the human approval gate silently does not exist.
- **`environment: production` deployment branches:** The environment's deployment branch policy must allow only the protected control-plane branch set: `main` and eligible protected maintenance branches `release/<project-name>/v<release-line>`. No other branch may enter the production environment approval flow.
- **Workflow file ownership:** `.github/CODEOWNERS`, `official.yml`, every `_build-test-*.yml`, every `_publish-*.yml`, `eng/scripts/**`, `mise.toml`, `mise.lock`, and any other trusted control-plane helper code used by official release jobs must be protected by `CODEOWNERS` review from a dedicated release-engineering group on every branch in the protected control-plane branch set. Protected control-plane branches must also require code-owner review in their branch protection or ruleset configuration. `job_workflow_ref` constrains which workflow file can mint publish credentials, but it does not prove the content hash of that file.
- **OIDC trust policies:** Each external registry must be configured with the strongest claim set it supports, without assuming portable wildcard future-branch trust. The authoritative branch restriction is the GitHub `environment: production` deployment branch policy. Registry-side trust must at minimum bind the repository, the **called reusable publish workflow** path, and `environment = "production"`. Where a target registry also supports branch-ref or caller-workflow discrimination, require those claims as well and enumerate each allowed protected control-plane branch ref explicitly. This prevents other workflows in the same repository from minting valid production publish tokens by reusing a different publish implementation.
- **OIDC change management:** Because Trusted Publisher configuration is coupled to workflow file path and the allowed protected control-plane branch set, any rename of a protected control-plane branch, any addition or retirement of an allowed protected maintenance branch ref, or any move/rename of `_publish-*.yml` must be accompanied by registry-side configuration updates before the next release. The repository must also keep a checked-in OIDC trust inventory (for example `.github/oidc-trust-inventory.json`) that lists the allowed caller branches and reusable publish workflow paths, and CI must fail any control-plane change that updates those refs or paths without updating the inventory in the same change.

**Jobs:**

1. **`preflight-check`**:
    - Runs before `resolve-tag`.
    - `permissions: { actions: read }`
    - Verifies that `environment: production` already exists, includes at least one required-reviewer protection rule, has `prevent_self_review` enabled, and restricts deployment branches to the official protected control-plane branch set.
    - Uses the GitHub Environments API response directly: the check must look for a `protection_rules` entry with `type == "required_reviewers"` and a non-empty reviewer list, must verify `prevent_self_review == true`, and must verify that the deployment branch policy allows only `main` plus the registered protected maintenance branches in the official protected control-plane branch set. The check is global to that branch set; it does not attempt to infer the current release line dynamically. A wait timer or branch policy alone is not sufficient.
    - Treats every GitHub API error as a hard failure. Specifically: `404` means the environment is missing; `200` without a qualifying `required_reviewers` rule, with `prevent_self_review` disabled, or with an overly broad deployment branch policy means the environment is misconfigured; every other non-`200` response blocks the workflow as an environment-verification failure.
    - Fails hard if the environment is missing or unprotected. This turns the documented prerequisite into an executable guardrail.
    - All GitHub API calls in this job must set an explicit client timeout so the guard fails fast rather than consuming the full job timeout on a hung response.

2. **`resolve-tag`**:
    - `needs: [preflight-check]`
    - `permissions: { contents: read }`
    - **Structural validation (first step, before checkout):** Extract `project-name` and `version` from `${{ inputs.tag-name }}`. Validate the tag shape `release/<project-name>/v<version>`, the `[A-Za-z0-9][A-Za-z0-9._-]*` character class for `project-name`, reject `..` and trailing `.` in `project-name`, and validate the conservative pre-checkout character class `[A-Za-z0-9][A-Za-z0-9._+-]*` for `version`. PEP 440 epoch markers (`!`) are intentionally rejected in release tags. Do not select an ecosystem-specific semantic version validator yet.
    - **Runner and tooling:** Runs on `ubuntu-latest`. Like `resolve-context` in `buddy.yml`, version resolution uses the `nbgv-python` adapter and does not require a Windows runner even for C# projects. The job should restore a tool cache keyed by `mise.toml` and `mise.lock` before invoking `mise install`.
    - **Workspace layout:** Keep the caller-ref workspace checked out at the dispatch-selected protected control-plane branch so trusted helper scripts remain available there.
    - **Tagged payload checkout:** Check out the tagged source into a separate working path (for example `.release-source/`) using `ref: refs/tags/${{ inputs.tag-name }}` with `fetch-depth: 0` and `persist-credentials: false`. `release.json`, `hk.pkl`, and all build inputs are read from that tagged workspace, while privileged helper scripts continue to come from the caller-ref workspace.
    - Runs the trusted `eng/scripts/find_project_path.py` from the caller-ref workspace against the tagged workspace to resolve `language` and `project-path` from `project-name`. `project-name` is case-sensitive and must resolve to exactly one project in the repository. The resolution step must emit exactly one of `{csharp, python, jsts, ruby}` for `language`; no match, ambiguous match, unsupported language, or resolver error is a hard failure.
    - **OIDC inventory preflight:** Before any publish jobs become eligible, verify that the current caller branch and reusable publish workflow paths are present in the checked-in OIDC trust inventory (for example `.github/oidc-trust-inventory.json`). This catches repository-side trust drift before any production approval is consumed. Because registry-side trust settings are not queried portably, matching registry updates are still a mandatory operational step.
    - **Semantic version validation (after checkout):** Validate the extracted `version` using the trusted validator scripts from the caller-ref workspace: `eng/scripts/validate_semver2_version.py` (NuGet and npm), `eng/scripts/validate_rubygems_version.py` (the repository's supported RubyGems-compatible subset), or `eng/scripts/validate_pep440_version.py` (Python), chosen after the project language is known.
    - **Official source ancestry:** After semantic validation, assert that the tagged commit is reachable from either `origin/main` or the protected maintenance branch `origin/release/<project-name>/v<release-line>`, where `<release-line>` is derived from the version's base release segment exactly as defined in the tag checkout mechanism above. This is what allows official releases, including prerelease-suffixed versions, from previous supported release lines while still rejecting feature-branch-only commits.
    - Reads `release.json` from the tagged workspace, validates it exactly as specified in **Section 5**, applies the same language-target validation rule as `buddy.yml`, then filters to the official target set `{nuget:official, npm:official, pypi:official, rubygems:official, github:official}` and fails if the filtered set is empty.

    - **Overwrite guard:** If `github:official` is among the resolved targets, check GitHub Releases state for the supplied tag. If no GitHub Release exists, proceed — this is the normal first official run. Official GitHub Releases must use a deterministic release title `<project-name> v<version>`. The guard must paginate existing non-pre-release GitHub Releases, match that deterministic title, and fail immediately if the same title already exists under a different tag or commit. If a pre-release GitHub Release exists for the same tag, allow `_publish-github.yml` to replace it with a stable release using the current local build output. If a non-pre-release GitHub Release already exists for the same tag, defer the idempotent/no-op decision to `_publish-github.yml`, which must verify remote asset identity before reporting success.
    - **Outputs:** `tag-name`, `language`, `project-name`, `project-path`, `version`, `targets` (JSON array of filtered official targets).

3. **`static-analysis`**:
    - `needs: [resolve-tag]`
    - `permissions: { contents: read }`
    - Checks out the tagged source ref before enumerating files. Read-only checkout must use `persist-credentials: false`.
    - Runs `hk check --files <file-list>` scoped to the resolved project path. The file list is generated by enumerating all files under `<project-path>/` (e.g., via `find` or `fd`) and passed directly to HK as normal file arguments. For official release gating, HK configuration intentionally follows the tagged source ref.

4. **`clean-build`** (`build-csharp` / `build-python` / `build-jsts` / `build-ruby`):
    - For supply chain security, no prior artifacts are reused. A fresh build and test run is performed from the exact commit the tag points to. The checkout must use `fetch-depth: 0` for NBGV resolution.
    - Uses the same four static conditional build jobs pattern as `buddy.yml`, with `permissions: { contents: read }`, `secrets: {}`, and the required `with:` inputs wired from `needs.resolve-tag.outputs.project-path`, `needs.resolve-tag.outputs.project-name`, and `checkout-ref: refs/tags/${{ needs.resolve-tag.outputs.tag-name }}`. Each build job depends on both `resolve-tag` and `static-analysis`. Only the language-matching build job executes; the others are skipped.

5. **Publish jobs** (static conditional, one job per official ecosystem-destination pair):
    - Uses the same per-destination split pattern as `buddy.yml`, but official targets now include `github:official` in addition to the production package registries.
    - `needs: [resolve-tag, static-analysis, build-csharp, build-python, build-jsts, build-ruby]`
    - `environment: production` — **mandatory**, not optional. This enables human approval gates and OIDC token issuance. Each destination still triggers its own approval step. This trades operator convenience for per-destination isolation of approvals and tokens. If reviewer fatigue becomes material later, migrate to a single reviewed gate plus destination-specific non-reviewed environments.
    - Package-registry publish jobs use `permissions: { id-token: write }` for OIDC Trusted Publishing. `publish-github-official` uses `permissions: { contents: write }`.
    - Because `official.yml` may run only from the protected control-plane branch set, no separate runtime assertion is required here to distinguish the caller branch from the trusted control-plane source. The production environment branch policy and branch protections carry that responsibility.
    - All official publish jobs use `secrets: {}`. OIDC and the automatic `GITHUB_TOKEN` are the default mechanisms; no blanket secret inheritance is allowed.
    - Each publish step uses idempotent publish logic from the protected control-plane branch set. Duplicate-version outcomes (`409`, `422`, or tool-equivalent "already exists" responses) count as success only after the workflow verifies that the already-published remote artifact set matches the local artifact set and expected digests. Authentication failures, authorization failures, malformed artifacts, and upstream `5xx` errors remain hard failures. This design intentionally does not retry upstream `5xx` failures inside a single run; operator recovery happens by re-running the workflow.

    ```yaml
    publish-nuget-official:
        needs: [resolve-tag, static-analysis, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            id-token: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-tag.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            contains(fromJson(needs.resolve-tag.outputs.targets), 'nuget:official')
        uses: ./.github/workflows/_publish-nuget.yml
        with:
            artifact-name: build-output-${{ needs.resolve-tag.outputs.project-name }}
            version: ${{ needs.resolve-tag.outputs.version }}
            feed-url: https://api.nuget.org/v3/index.json
        secrets: {}

    publish-npm-official:
        needs: [resolve-tag, static-analysis, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            id-token: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-tag.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            contains(fromJson(needs.resolve-tag.outputs.targets), 'npm:official')
        uses: ./.github/workflows/_publish-npm.yml
        with:
            artifact-name: build-output-${{ needs.resolve-tag.outputs.project-name }}
            version: ${{ needs.resolve-tag.outputs.version }}
            registry: https://registry.npmjs.org
        secrets: {}

    publish-pypi-official:
        needs: [resolve-tag, static-analysis, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            id-token: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-tag.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            contains(fromJson(needs.resolve-tag.outputs.targets), 'pypi:official')
        uses: ./.github/workflows/_publish-pypi.yml
        with:
            artifact-name: build-output-${{ needs.resolve-tag.outputs.project-name }}
            version: ${{ needs.resolve-tag.outputs.version }}
        secrets: {}

    publish-rubygems-official:
        needs: [resolve-tag, static-analysis, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            id-token: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-tag.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            contains(fromJson(needs.resolve-tag.outputs.targets), 'rubygems:official')
        uses: ./.github/workflows/_publish-rubygems.yml
        with:
            artifact-name: build-output-${{ needs.resolve-tag.outputs.project-name }}
            version: ${{ needs.resolve-tag.outputs.version }}
            host: https://rubygems.org
        secrets: {}

    publish-github-official:
        needs: [resolve-tag, static-analysis, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            contents: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            needs.resolve-tag.result == 'success' &&
            needs.static-analysis.result == 'success' &&
            contains(fromJson(needs.resolve-tag.outputs.targets), 'github:official')
        uses: ./.github/workflows/_publish-github.yml
        with:
            artifact-name: build-output-${{ needs.resolve-tag.outputs.project-name }}
            project-name: ${{ needs.resolve-tag.outputs.project-name }}
            version: ${{ needs.resolve-tag.outputs.version }}
            tag-name: ${{ needs.resolve-tag.outputs.tag-name }}
            prerelease: false
        secrets: {}
    ```

## 5. Release Configuration Contract

Each project that can be released must have a release configuration file at `<project-root>/release.json`. The `resolve-context` (buddy) and `resolve-tag` (official) jobs read this file to determine publish targets.

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
- No fields other than `schemaVersion` and `targets` are allowed.
- A workflow may filter out valid targets that belong to the opposite release channel, but only **after** validation succeeds.
- After channel filtering, the invoking workflow must still have at least one applicable target.
- In this design, Python has no unofficial registry target. A Python project that wants a buddy preview must include `github:release`.
- Removing a previously used target takes effect immediately because backward-compatibility shims are intentionally out of scope before implementation starts. For example, removing `github:official` stops GitHub Release reconciliation on subsequent official runs and leaves any existing stable release unchanged until operators update it manually.
- Unsupported future schema versions are hard failures with operator guidance. Because implementation has not started, schema upgrades are coordinated changes rather than backward-compatible migrations.

**Project resolution contract:**

- `project-name` is case-sensitive and must identify exactly one project in the repository.
- Project resolution must emit both `project-path` and `language`.
- `language` must be exactly one of `csharp`, `python`, `jsts`, or `ruby`.
- No match, ambiguous match, unsupported language, or resolver error is a hard failure.

**Lookup behavior:** The script searches for `release.json` starting from the project directory (resolved by `eng/scripts/find_project_path.py`). If the file is absent, the workflow fails with a clear error — there is no default target set. On failure, the script must print: the resolved project path, the contents of `release.json` if found, and the specific validation rule that was violated.

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
- They must use the same shell input-hardening rule as entry workflows: map `inputs.*` to `env:` first, then reference quoted shell variables inside `run:` steps.
- Official release workflows may execute only trusted control-plane helper code sourced from the protected control-plane branch set. Tagged source checkout is for release payload build/test input, not for privileged publish logic.
- They must treat artifact validation failures, auth failures, and upstream service failures as hard failures unless a specific duplicate-version case is explicitly documented as idempotent.

### Build-Test Workflows

All four build-test workflows share the same input/output structure:

| Input          | Type     | Required | Description                                                  |
| -------------- | -------- | -------- | ------------------------------------------------------------ |
| `checkout-ref` | `string` | No       | Git ref or commit SHA that the build workflow must check out |
| `project-path` | `string` | Yes      | Path to the project directory within the repo                |
| `project-name` | `string` | Yes      | Project name (used for artifact naming)                      |

| Output          | Type     | Description                                                     |
| --------------- | -------- | --------------------------------------------------------------- |
| `artifact-name` | `string` | Name of the uploaded CI Artifact: `build-output-<project-name>` |

**Required caller permissions:** `contents: read`

**Checkout behavior:** Build-test workflows perform their own checkout and must use `fetch-depth: 0` internally so NBGV and other git-history-derived metadata resolve correctly. These read-only checkouts must also use `persist-credentials: false`. When `checkout-ref` is provided, the reusable workflow must check out exactly that ref; official callers use this to force the tagged release payload, while buddy callers may pass the dispatch commit SHA explicitly.

**Secrets:** `secrets: {}` — build-test workflows require no secrets. Callers must not pass `secrets: inherit` to avoid exposing publish credentials to build/test execution.

**Artifact convention:** Each build workflow uploads its output to CI Artifacts with the name `build-output-<project-name>`. Publish workflows download by this exact name. The artifact layout per ecosystem:

| Ecosystem | Expected artifact contents                                                                                                                                                       |
| --------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| NuGet     | One or more `.nupkg` files; matching `.snupkg` symbol packages may also be included and should be pushed alongside the corresponding `.nupkg` when the destination supports them |
| npm       | One `.tgz` tarball (output of `npm pack` / `pnpm pack`)                                                                                                                          |
| PyPI      | One `.whl` and one `.tar.gz` (wheel + sdist)                                                                                                                                     |
| RubyGems  | One `.gem` file                                                                                                                                                                  |
| GitHub    | All files in the artifact (uploaded as release assets)                                                                                                                           |

Every build artifact must also contain a manifest file at the artifact root (for example `artifact-manifest.json`) that lists each published file and its SHA-256 digest. Publish workflows must verify the downloaded files against that manifest before any publish step runs.

**Reproducibility requirement:** Build workflows must configure their packaging tools so reruns from the same source commit and lockfiles produce the same package-file identities. Where a package format embeds timestamps, file ordering, or host-specific metadata by default, the reusable build workflow must normalize those fields before publishing artifacts.

**Artifact retention:** CI artifacts are an ephemeral hand-off mechanism, not permanent release storage. Recommended defaults: `retention-days: 7` for PR and buddy runs, `retention-days: 14` for official runs.

### Publish Workflows

All publish workflows share a common set of inputs, with ecosystem-specific additions:

| Input           | Type     | Required | Description                                    |
| --------------- | -------- | -------- | ---------------------------------------------- |
| `artifact-name` | `string` | Yes      | CI Artifact name to download (from build step) |
| `version`       | `string` | Yes      | Package version string                         |

**Ecosystem-specific inputs:**

| Workflow                | Input          | Type      | Description                                                                                   |
| ----------------------- | -------------- | --------- | --------------------------------------------------------------------------------------------- |
| `_publish-nuget.yml`    | `feed-url`     | `string`  | NuGet feed URL (GPR or NuGet.org)                                                             |
| `_publish-npm.yml`      | `registry`     | `string`  | npm registry URL (GPR or npmjs)                                                               |
| `_publish-pypi.yml`     | (none extra)   |           | Always publishes to PyPI via OIDC                                                             |
| `_publish-rubygems.yml` | `host`         | `string`  | RubyGems host URL (GPR or RubyGems.org)                                                       |
| `_publish-github.yml`   | `project-name` | `string`  | Project name, used for deterministic GitHub Release titles and diagnostics                    |
| `_publish-github.yml`   | `tag-name`     | `string`  | Git tag for the GitHub Release                                                                |
| `_publish-github.yml`   | `prerelease`   | `boolean` | Whether to mark the release as pre-release                                                    |
| `_publish-github.yml`   | `force`        | `boolean` | Buddy-only optional flag controlling replacement of a non-matching pre-release GitHub Release |

**Required caller permissions:**

| Workflow                | Required caller `permissions`                |
| ----------------------- | -------------------------------------------- |
| `_publish-nuget.yml`    | `packages: write` (GPR) or `id-token: write` |
| `_publish-npm.yml`      | `packages: write` (GPR) or `id-token: write` |
| `_publish-pypi.yml`     | `id-token: write`                            |
| `_publish-rubygems.yml` | `packages: write` (GPR) or `id-token: write` |
| `_publish-github.yml`   | `contents: write`                            |

**Secrets:** `secrets: {}` by default. If a future publish target requires an explicit credential, the caller must pass only that named secret. `secrets: inherit` is prohibited.

**Artifact validation:** Before publishing, each reusable publish workflow must verify that the expected files exist at the artifact root and fail on empty artifacts, missing required files, or ambiguous layouts. Duplicate-version outcomes count as idempotent success only when the remote artifact set matches the local artifact set and expected digests. `_publish-github.yml` must verify that at least one top-level file exists in the downloaded artifact and must fail if release assets are nested under subdirectories instead of flattened at the artifact root.

For GPR targets, publish workflows must treat package versions as immutable within workflow execution. Even though GitHub supports package deletion and restoration with elevated package-admin capabilities, these reusable publish workflows do not request those permissions and must never delete package versions as part of a retry or recovery path.

**GitHub publish force semantics:** `_publish-github.yml` accepts `force` only for the buddy pre-release path. The workflow input must declare `default: false`, buddy callers may pass it explicitly, and official callers do not pass it. `force` never relaxes official stable-release protections.

**GitHub release identity metadata:** `_publish-github.yml` must use deterministic release titles. For official stable releases, the title must be `<project-name> v<version>`. `resolve-tag` relies on that invariant when scanning existing stable releases for same-version identity conflicts across tags.

**Publish result signaling:** Each reusable publish workflow must emit an explicit notice indicating whether the run performed a new publish or completed as an idempotent no-op.

## 7. Overwrite and Idempotency Policy

Both `buddy.yml` and `official.yml` check for existing artifacts before proceeding. The policy differs by channel:

### Buddy (Unofficial)

| Condition                                                                                                           | Behavior                                                                               |
| ------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| Non-pre-release GitHub Release exists                                                                               | **Hard fail** — stable releases must not be overwritten by buddy                       |
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
| No GitHub Release exists for the supplied tag                                                      | **Proceed** — normal first official run                                                        |
| Non-pre-release GitHub Release exists for the supplied tag with matching remote artifact identity  | **Success** (idempotent no-op)                                                                 |
| Non-pre-release GitHub Release exists for the supplied tag with different remote artifact identity | **Hard fail** — release assets must not silently diverge from the local build output           |
| Non-pre-release GitHub Release exists for the same version but different tag/commit                | **Hard fail** — stable releases must not be rebound to a different release identity            |
| Pre-release GitHub Release exists for the supplied tag                                             | **Replace with stable release** using the current local build output for that tag              |
| Package version already exists at official registry with matching remote artifact identity         | **Success** (idempotent publish scripts)                                                       |
| Package version already exists at official registry with different remote artifact identity        | **Hard fail** — cut a new version; official registry versions are immutable release identities |
| Authn/authz failure or upstream `5xx` at official registry                                         | **Hard fail** — not idempotent                                                                 |

### Recovery Playbook

If a workflow run fails partway through (e.g., nuget:gpr succeeds but npm:gpr fails):

1. If `preflight-check` fails, treat it as an environment-setup issue rather than a code defect: configure `environment: production` with at least one required reviewer, then trigger a new run.
2. If execution fails before any publish job starts (for example `resolve-context`, `resolve-tag`, `static-analysis`, or build failure), fix the repository or configuration issue and trigger a fresh workflow dispatch. No remote release state has been mutated yet.
3. Distinguish between **Re-run jobs** and a fresh **workflow_dispatch**. Before choosing a rerun path, check that the original run's artifacts still exist in the GitHub Actions run UI or API. After artifact retention expires, or if the artifacts are already gone, do not use GitHub's Re-run button; trigger a new workflow dispatch so the build artifacts are regenerated from source.
4. If the failure is transient (network issue, auth outage, upstream `5xx`, or a declined per-destination approval that will now be approved), trigger the same workflow again with the same inputs. Matching already-published artifacts must settle as idempotent no-ops.
5. If the failure is caused by malformed build output or a remote artifact identity mismatch, stop retrying the same release identity. For buddy, `force=true` applies only to GitHub pre-release asset replacement and buddy traceability tag re-pointing; it does **not** apply to GPR package versions. If a GPR package version already exists with different artifact identity, cut a new version rather than deleting and republishing. For official, cut a new release version/tag from `main` or the matching protected maintenance branch through the normal release-operator path rather than trying to salvage the existing immutable production version.
6. If buddy publish steps succeeded but `create-traceability-tag` failed, trigger the same buddy workflow inputs rather than creating the tag manually. Matching publish targets should settle as idempotent no-ops, and the rerun should complete the missing tag.
7. If official publish jobs partially succeeded because some destinations were approved and others were declined, trigger the same official workflow again with the same tag. Already-published destinations must settle as idempotent no-ops, and the remaining destinations will request fresh approval.
8. If a queued buddy run has become stale because an earlier run already published or moved the project forward, cancel the queued run rather than letting it fail late on overwrite checks.
9. If official publish jobs fail with authentication or authorization errors immediately after a new maintenance branch, trusted workflow path, or protected control-plane branch change was introduced, verify the registry-side Trusted Publisher configuration and the checked-in OIDC trust inventory before retrying. Treat this as control-plane configuration drift, not as a package-content defect.

## 8. Build Provenance

Until full provenance attestation is implemented, build jobs must emit the artifact manifest and digests described in Section 6, and publish jobs must verify that manifest before any upload. As the long-term design, `official.yml`'s build jobs should also add a post-build attestation step using `actions/attest-build-provenance` or ecosystem-native equivalents (PyPI via PEP 740/sigstore, npmjs via `npm attest`, NuGet.org via package signing). OIDC Trusted Publishing proves workflow identity at publish time; provenance attestation embeds that proof into the artifact itself, enabling offline verification by consumers.

## Summary of Key Design Properties

1. **PR speed maximized**: A JS-only PR never waits for the Windows C# build queue.
2. **Channel isolation**: `buddy.yml` publishes only to unofficial registries plus optional GitHub pre-releases (`github:release`). `official.yml` publishes only to production registries plus optional stable GitHub Releases (`github:official`). Neither channel requires the other to run first for registry delivery.
3. **Static conditional dispatch**: Because `uses:` paths must be static, both build and publish jobs use conditional `if:` guards instead of dynamic matrix dispatch to reusable workflows. Each ecosystem-destination pair has its own dedicated job.
4. **Tag isolation**: Official release identity uses `release/<project-name>/v<version>`. Buddy traceability uses `buddy/<project-name>/v<version>`. The unofficial channel no longer writes into the official release-identity namespace.
5. **Overwrite-safe with force escape hatch**: Buddy guards against overwriting stable releases and uses a privileged `force=true` path only where the design explicitly permits replacing a non-matching pre-release or re-pointing the unofficial traceability tag. In this revision, that privilege is accepted as policy-controlled rather than workflow-enforced. Official publishes are idempotent for the same release identity only when remote artifact identity matches the local build output, and they never rebind a stable release to a different tag or commit.
6. **Least-privilege security**: Workflow-level `permissions: {}` with per-job escalation; build and publish jobs default to `secrets: {}`; shell input hardening applies to reusable workflows as well as entry workflows; privileged official publish logic stays on the protected control-plane branch set (`main` plus eligible protected `release/*` branches); OIDC for production registries binds repository, `environment = "production"`, and the called reusable publish workflow path, with branch-ref and caller-workflow claims added where the target registry supports them; `environment: production` with mandatory required-reviewer gates and deployment branch restrictions remains the authoritative branch scope; protected official source branches; protected official `release/**` tags; isolated unofficial `buddy/**` tags.
