# GitHub Workflows Design (v2.3)

This document describes the GitHub Actions workflow architecture for the `three` monorepo.

> **Scope constraint:** Each project maps to exactly one language ecosystem. Multi-language projects (e.g., a C# library with a companion npm package) are out of scope and must be split into separate project directories with separate `release.json` files.

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

**Secrets:**

- **Build-test workflows** have no secret requirements. Callers should pass secrets explicitly: `secrets: {}` (empty). This limits the blast radius if a compromised dependency or malicious test reads the environment during build/test execution.
- **Publish workflows** require `secrets: inherit` from the caller. Note that `secrets: inherit` forwards **repository secrets, organization secrets, and environment secrets accessible to the calling job** (i.e., if the calling job specifies `environment: production`, environment-scoped secrets are included). It does NOT affect `permissions` (OIDC tokens, `GITHUB_TOKEN` scopes, etc.).

Permissions are inherited automatically: a reusable workflow receives the caller job's `permissions` grants as long as the reusable workflow itself does **not** declare its own `permissions` block. This is what allows the same `_publish-nuget.yml` to operate under `packages: write` when called from `buddy.yml` and under `id-token: write` when called from `official.yml`.

> **Important constraint:** Reusable workflows must NOT declare their own `permissions:` block. If they do, the effective token is silently capped at the intersection of the declared scopes and the caller's grants. For example, if a reusable workflow declares `permissions: { id-token: write }` but the caller only grants `packages: write`, the minted token will have `id-token: none`, causing silent runtime failures. Keep all `permissions:` declarations in the entry workflows only.

**Permissions model:** Every entry workflow declares `permissions: {}` at workflow level. Individual jobs then request only the scopes they need (principle of least privilege). Key scopes:

| Job kind                            | Required `permissions` |
| ----------------------------------- | ---------------------- |
| Read-only checkout                  | `contents: read`       |
| Push tags                           | `contents: write`      |
| Create GitHub Release               | `contents: write`      |
| GitHub Packages (any feed)          | `packages: write`      |
| OIDC publish to official registries | `id-token: write`      |

All four official registries (NuGet.org, PyPI, npmjs, RubyGems.org) support OIDC Trusted Publishing. GPR feeds use `GITHUB_TOKEN` with `packages: write` instead.

> **Note:** With `permissions: {}` at workflow level, every job that runs `actions/checkout` must explicitly declare at least `permissions: { contents: read }`. Build jobs included — without this, the zero-permission `GITHUB_TOKEN` cannot clone the repository (private or internal repos fail immediately; public repos may also fail depending on runner configuration).

**Concurrency policy:** Each entry workflow defines a `concurrency:` group to prevent resource races:

- `ci.yml`: `group: ci-${{ github.ref }}`, `cancel-in-progress: true`
- `buddy.yml`: `group: buddy-${{ inputs.project-name }}`, `cancel-in-progress: false`
- `official.yml`: `group: official-${{ inputs.tag-name }}`, `cancel-in-progress: false`

**Third-party actions:** All third-party actions must be pinned to full commit SHA. Use Renovate or Dependabot to manage updates:

```yaml
uses: dorny/paths-filter@de90cc6ed7cd597cb74b84a7e832ce805e3c7b15 # v3.0.2
```

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
    - `infra`: `['.github/workflows/**', 'eng/scripts/**', 'mise.toml', 'hk.pkl']`

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
                    to_entries
                    | map(.value.result == "success" or .value.result == "skipped")
                    | all'
    ```

## 3. `buddy.yml` — Unofficial Release (Static Conditional Publish, Tag Isolation)

**Trigger:** `on: workflow_dispatch` only (no automated triggers).

**Inputs:**

| Input          | Type      | Required | Description                                                |
| -------------- | --------- | -------- | ---------------------------------------------------------- |
| `project-name` | `string`  | Yes      | Project identity to release                                |
| `force`        | `boolean` | No       | Allow overwriting pre-release artifacts (default: `false`) |

All workflow inputs must be mapped to intermediate environment variables before use in shell scripts (e.g., `env: PROJECT_NAME: ${{ inputs.project-name }}`; use `"$PROJECT_NAME"` in bash, never `${{ inputs.project-name }}` directly in `run:` blocks).

Even within the same language, different projects may have different packaging strategies (EXE, NuGet, wheel, etc.). The workflow resolves publish targets dynamically from project configuration.

**Jobs:**

1. **`resolve-context`**:
    - **Runner and tooling:** Runs on `ubuntu-latest`. Requires `mise install` to bootstrap Python (for `eng/scripts/find_project_path.py`) and the .NET SDK (for NBGV via the `nbgv-python` adapter). The `mise.toml` at the repo root pins all tool versions.
    - **Input validation:** As the first step (before any checkout or git operation), validate `project-name` against the character class `[A-Za-z0-9._-]+`. Reject invalid names with a clear error. This is the same validation already performed by `eng/scripts/find_project_path.py`.
    - Runs `eng/scripts/find_project_path.py` to determine: language (from `project_kind` output), project path (from `package_dir` output).
    - **NBGV resolution:** The checkout must use `fetch-depth: 0` so NBGV can compute version height from git history. All jobs that use NBGV or rely on git-history-derived metadata must also checkout with full history. The script locates the correct `version.json` by searching upward from the project directory. Version validation is performed programmatically using the existing scripts: `eng/scripts/validate_semver2_version.py` (for NuGet, npm, RubyGems ecosystems) or `eng/scripts/validate_pep440_version.py` (for Python/PyPI).
    - Reads the project's release configuration (see **Section 5: Release Configuration Contract**) and emits a JSON array of publish targets. Targets use the format `ecosystem:destination` (e.g. `["nuget:gpr", "github:release"]`).
    - **Validates the output** against the unofficial-only allowlist before setting the job output:

    ```python
    UNOFFICIAL_TARGETS = frozenset({
        "nuget:gpr", "npm:gpr", "rubygems:gpr", "github:release",
    })
    buddy_targets = [t for t in targets if t in UNOFFICIAL_TARGETS]
    assert len(buddy_targets) > 0, (
        f"No unofficial publish targets found. "
        f"release.json targets={targets}, allowed={UNOFFICIAL_TARGETS}"
    )
    ```

    Official-only targets (`nuget:official`, `npm:official`, `pypi:official`, `rubygems:official`) in `release.json` are silently filtered out — they are only processed by `official.yml`. This ensures clean channel separation: buddy never attempts to publish to production registries.
    - **Overwrite guard:** Before proceeding, check whether a non-pre-release GitHub Release already exists for this project and version. If it does, fail immediately — stable releases must not be overwritten. If only a pre-release GitHub Release exists, allow overwrite only when `inputs.force` is `true`; otherwise fail with a clear message. Similarly, if a traceability tag exists pointing to a different commit, allow overwrite only when `inputs.force` is `true`.
    - **Outputs:** `language`, `project-name`, `project-path`, `version`, `targets` (JSON array of filtered unofficial targets).
    - **On failure**, the script must print: the resolved project path, the contents of `release.json` if found, and the specific validation rule that was violated.

2. **`static-analysis`**:
    - `needs: [resolve-context]`
    - Runs `hk check --files <file-list>` scoped to the resolved project path. The file list is generated by enumerating all files under `<project-path>/` (e.g., via `find` or `fd`). HK applies its configured linter rules based on file extensions and glob patterns defined in `hk.pkl`.

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
        secrets: {}

    build-python:
        needs: [resolve-context, static-analysis]
        permissions:
            contents: read
        if: needs.resolve-context.outputs.language == 'python'
        uses: ./.github/workflows/_build-test-python.yml
        secrets: {}

    build-jsts:
        needs: [resolve-context, static-analysis]
        permissions:
            contents: read
        if: needs.resolve-context.outputs.language == 'jsts'
        uses: ./.github/workflows/_build-test-jsts.yml
        secrets: {}

    build-ruby:
        needs: [resolve-context, static-analysis]
        permissions:
            contents: read
        if: needs.resolve-context.outputs.language == 'ruby'
        uses: ./.github/workflows/_build-test-ruby.yml
        secrets: {}
    ```

    Only one of these four jobs will actually execute. Build artifacts (`.nupkg`, `.whl`, `.exe`, `.gem`, etc.) are uploaded to CI Artifacts using a deterministic name: `build-output-<project-name>` (e.g. `build-output-my-library`). Artifacts are built fresh within this workflow run; no artifacts from prior runs are downloaded.

4. **Publish jobs** (static conditional, one job per ecosystem-destination pair):

    Because GitHub Actions resolves `uses:` statically at parse time, and each reusable workflow call publishes to **exactly one** destination, publish jobs are split per ecosystem-destination pair. Each job has its own `if:` guard using `fromJson()` for exact array membership (not substring matching):

    ```yaml
    publish-nuget-gpr:
        needs: [resolve-context, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            packages: write
        if: |
            always() && !cancelled() && !failure() &&
            contains(fromJson(needs.resolve-context.outputs.targets), 'nuget:gpr')
        uses: ./.github/workflows/_publish-nuget.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
            feed-url: https://nuget.pkg.github.com/hcoona/index.json
        secrets: inherit

    publish-npm-gpr:
        needs: [resolve-context, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            packages: write
        if: |
            always() && !cancelled() && !failure() &&
            contains(fromJson(needs.resolve-context.outputs.targets), 'npm:gpr')
        uses: ./.github/workflows/_publish-npm.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
            registry: https://npm.pkg.github.com
        secrets: inherit

    publish-rubygems-gpr:
        needs: [resolve-context, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            packages: write
        if: |
            always() && !cancelled() && !failure() &&
            contains(fromJson(needs.resolve-context.outputs.targets), 'rubygems:gpr')
        uses: ./.github/workflows/_publish-rubygems.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
            host: https://rubygems.pkg.github.com/hcoona
        secrets: inherit

    publish-github-release:
        needs: [resolve-context, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            contents: write
        if: |
            always() && !cancelled() && !failure() &&
            contains(fromJson(needs.resolve-context.outputs.targets), 'github:release')
        uses: ./.github/workflows/_publish-github.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            version: ${{ needs.resolve-context.outputs.version }}
            tag-name: release/${{ needs.resolve-context.outputs.project-name }}/v${{ needs.resolve-context.outputs.version }}
            prerelease: true
        secrets: inherit
    ```

    - The `if: always() && !cancelled() && !failure()` guard ensures the publish jobs run despite the three skipped build jobs in the `needs` chain. This condition is safe because skipped jobs are treated as neither failure nor cancellation.
    - For GPR targets, auth uses `GITHUB_TOKEN` with `packages: write`. No OIDC is needed.
    - Each publish step uses the idempotent scripts (`eng/scripts/publish_*_idempotent.sh`) that treat "version already exists" as a success exit code.

5. **`create-traceability-tag`**:
    - `needs: [resolve-context, publish-nuget-gpr, publish-npm-gpr, publish-rubygems-gpr, publish-github-release]`
    - `if: always() && !cancelled() && !failure()`
    - `permissions: { contents: write }`
    - Assembles and pushes a lightweight Git tag: `release/<project-name>/v<version>`.
    - **Tag overwrite logic:** If the tag does not exist, create it. If it exists and points to the same commit, succeed as no-op. If it exists but points to a different commit: when `inputs.force` is `true`, force-update the tag; otherwise fail with a clear error message.
    - Uses `${{ secrets.GITHUB_TOKEN }}` to run `git push origin <tag>`. Per GitHub docs, events triggered by `GITHUB_TOKEN` will **not** create new workflow runs (anti-recursion mechanism). This ensures the traceability tag records the source commit without triggering `official.yml`.

## 4. `official.yml` — Production Release

**Important:** `buddy.yml` and `official.yml` are **independent release channels**, not a sequential promotion pipeline. Buddy publishes to unofficial registries (GitHub Packages, GitHub Releases as pre-release); official publishes to production registries (NuGet.org, PyPI, npmjs, RubyGems.org). A buddy run is NOT a prerequisite for an official run — either can be triggered independently.

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

**Tag checkout mechanism:** Validation must occur as the first step in `resolve-tag`, before any `actions/checkout` or git operations. The tag name is validated using `eng/scripts/validate_semver2_version.py` (or the appropriate per-ecosystem validator) on the version segment extracted from the tag. After validation, the checkout step uses `ref: refs/tags/${{ inputs.tag-name }}` (not the branch HEAD selected in the dispatch UI). An assertion step then verifies the checked-out `HEAD` commit SHA matches the dereferenced tag target SHA (using `git rev-parse HEAD` vs `git rev-parse refs/tags/<tag>^{commit}` to handle both lightweight and annotated tags correctly). This prevents accidental releases from the wrong commit.

**Prerequisites (must be configured before first run):**

- **Branch protection** on the default branch must require: PR review approval and the `ci-passed` required status check before merging. Without this, direct pushes bypass `ci.yml` entirely, allowing unreviewed code to be released.
- **`environment: production`** must exist in GitHub repository settings with protection rules (required reviewers, deployment branches, etc.) **before** the workflow is ever triggered. If this environment does not pre-exist, GitHub auto-creates it with **zero** protection rules and the human approval gate silently does not exist.
- **OIDC trust policies:** Each external registry's Trusted Publisher configuration must enforce both:
    1. The `environment = "production"` claim in its OIDC subject/claims filter.
    2. The `job_workflow_ref` claim matching `.github/workflows/official.yml@refs/heads/main` (or per-registry equivalent claim name). This prevents other workflows in the same repository (that also declare `environment: production` for unrelated purposes) from minting valid production publish tokens.

**Jobs:**

1. **`resolve-tag`**:
    - **Input validation (first step, before checkout):** Extract `project-name` and `version` from `${{ inputs.tag-name }}`. Validate `project-name` against `[A-Za-z0-9._-]+` and the version string using the appropriate version validator script (`eng/scripts/validate_semver2_version.py` or `eng/scripts/validate_pep440_version.py`, determined by project language). Reject malformed tags with a hard failure.
    - **Runner and tooling:** Runs on `ubuntu-latest`. Like `resolve-context` in `buddy.yml`, version resolution uses the `nbgv-python` adapter and does not require a Windows runner even for C# projects.
    - Runs `eng/scripts/find_project_path.py` to resolve `language` and `project-path` from `project-name`.
    - Reads `release.json` and filters to official-only targets:

    ```python
    OFFICIAL_TARGETS = frozenset({
        "nuget:official", "npm:official", "pypi:official", "rubygems:official",
    })
    official_targets = [t for t in targets if t in OFFICIAL_TARGETS]
    assert len(official_targets) > 0, (
        f"No official publish targets found. "
        f"release.json targets={targets}, allowed={OFFICIAL_TARGETS}"
    )
    ```

    - **Overwrite guard:** Check whether a non-pre-release GitHub Release already exists for this version. If it does, fail immediately — stable releases must not be overwritten. Then check whether the traceability tag exists and points to a different commit than the tag being released; if so, fail immediately. If only a pre-release GitHub Release exists, allow overwrite (the official release promotes it to stable).
    - **Outputs:** `language`, `project-name`, `project-path`, `version`, `targets` (JSON array of filtered official targets).

2. **`clean-build`** (`build-csharp` / `build-python` / `build-jsts` / `build-ruby`):
    - For supply chain security, no prior artifacts are reused. A fresh build and test run is performed from the exact commit the tag points to. The checkout must use `fetch-depth: 0` for NBGV resolution.
    - Uses the same four static conditional build jobs pattern as `buddy.yml`, with `permissions: { contents: read }` and `secrets: {}`. Only the language-matching build job executes; the others are skipped.

3. **Publish jobs** (static conditional, one job per official ecosystem-destination pair):
    - Uses the same per-destination split pattern as `buddy.yml`'s publish jobs, but only official targets are present.
    - `needs: [resolve-tag, build-csharp, build-python, build-jsts, build-ruby]`
    - `environment: production` — **mandatory**, not optional. This enables human approval gates and OIDC token issuance. Note: when multiple publish jobs each declare `environment: production`, each triggers its own independent approval gate. This is intentional — approvers must confirm each destination individually.
    - `permissions: { id-token: write }` — scoped only to these jobs for OIDC Trusted Publishing (NuGet.org, PyPI, npmjs, RubyGems.org all support OIDC).
    - Each publish job calls the same per-ecosystem `_publish-{ecosystem}.yml` workflow as `buddy.yml`, but with the official destination and OIDC auth instead of GPR + `GITHUB_TOKEN`.
    - Each publish step uses the idempotent scripts (`eng/scripts/publish_*_idempotent.sh`). If a version already exists at the target registry, the step exits successfully. This ensures the workflow can be safely re-run after partial failures.

    ```yaml
    publish-nuget-official:
        needs: [resolve-tag, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            id-token: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            contains(fromJson(needs.resolve-tag.outputs.targets), 'nuget:official')
        uses: ./.github/workflows/_publish-nuget.yml
        with:
            artifact-name: build-output-${{ needs.resolve-tag.outputs.project-name }}
            version: ${{ needs.resolve-tag.outputs.version }}
            feed-url: https://api.nuget.org/v3/index.json
        secrets: inherit

    publish-npm-official:
        needs: [resolve-tag, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            id-token: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            contains(fromJson(needs.resolve-tag.outputs.targets), 'npm:official')
        uses: ./.github/workflows/_publish-npm.yml
        with:
            artifact-name: build-output-${{ needs.resolve-tag.outputs.project-name }}
            version: ${{ needs.resolve-tag.outputs.version }}
            registry: https://registry.npmjs.org
        secrets: inherit

    publish-pypi-official:
        needs: [resolve-tag, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            id-token: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            contains(fromJson(needs.resolve-tag.outputs.targets), 'pypi:official')
        uses: ./.github/workflows/_publish-pypi.yml
        with:
            artifact-name: build-output-${{ needs.resolve-tag.outputs.project-name }}
            version: ${{ needs.resolve-tag.outputs.version }}
        secrets: inherit

    publish-rubygems-official:
        needs: [resolve-tag, build-csharp, build-python, build-jsts, build-ruby]
        permissions:
            id-token: write
        environment: production
        if: |
            always() && !cancelled() && !failure() &&
            contains(fromJson(needs.resolve-tag.outputs.targets), 'rubygems:official')
        uses: ./.github/workflows/_publish-rubygems.yml
        with:
            artifact-name: build-output-${{ needs.resolve-tag.outputs.project-name }}
            version: ${{ needs.resolve-tag.outputs.version }}
            host: https://rubygems.org
        secrets: inherit
    ```

4. **`promote-github-release`** (optional):
    - `needs: [resolve-tag, publish-nuget-official, publish-npm-official, publish-pypi-official, publish-rubygems-official]`
    - `if: always() && !cancelled() && !failure()`
    - `permissions: { contents: write }`
    - If a pre-release GitHub Release exists for this tag (created by a prior buddy run), promote it to a stable release (clear the `prerelease` flag). If no GitHub Release exists, this step is a no-op.

## 5. Release Configuration Contract

Each project that can be released must have a release configuration file at `<project-root>/release.json`. The `resolve-context` (buddy) and `resolve-tag` (official) jobs read this file to determine publish targets.

**Schema:**

```json
{
    "targets": ["nuget:gpr", "nuget:official", "github:release"]
}
```

**Fields:**

| Field     | Type       | Required | Description                                                 |
| --------- | ---------- | -------- | ----------------------------------------------------------- |
| `targets` | `string[]` | Yes      | Array of publish targets in `ecosystem:destination` format. |

**Lookup behavior:** The script searches for `release.json` starting from the project directory (resolved by `eng/scripts/find_project_path.py`). If the file is absent, the workflow fails with a clear error — there is no default target set. On failure, the script must print: the resolved project path, the contents of `release.json` if found, and the specific validation rule that was violated.

**Valid target values:**

| Target              | Channel    | Processed by   | Description                                     |
| ------------------- | ---------- | -------------- | ----------------------------------------------- |
| `nuget:gpr`         | Unofficial | `buddy.yml`    | Publish `.nupkg` to GitHub Packages NuGet feed  |
| `nuget:official`    | Official   | `official.yml` | Publish `.nupkg` to NuGet.org                   |
| `npm:gpr`           | Unofficial | `buddy.yml`    | Publish npm tarball to GitHub Packages npm feed |
| `npm:official`      | Official   | `official.yml` | Publish npm tarball to npmjs                    |
| `pypi:official`     | Official   | `official.yml` | Publish wheel/sdist to PyPI                     |
| `rubygems:gpr`      | Unofficial | `buddy.yml`    | Publish gem to GitHub Packages RubyGems feed    |
| `rubygems:official` | Official   | `official.yml` | Publish gem to RubyGems.org                     |
| `github:release`    | Unofficial | `buddy.yml`    | Upload assets to GitHub Releases (pre-release)  |

`buddy.yml` filters to unofficial targets only. `official.yml` filters to official targets only. A `release.json` may declare targets from both channels — each workflow ignores the targets it does not handle.

## 6. Reusable Workflow I/O Contracts

### Build-Test Workflows

All four build-test workflows share the same input/output structure:

| Input          | Type     | Required | Description                                   |
| -------------- | -------- | -------- | --------------------------------------------- |
| `project-path` | `string` | Yes      | Path to the project directory within the repo |
| `project-name` | `string` | Yes      | Project name (used for artifact naming)       |

| Output          | Type     | Description                                                     |
| --------------- | -------- | --------------------------------------------------------------- |
| `artifact-name` | `string` | Name of the uploaded CI Artifact: `build-output-<project-name>` |

**Secrets:** `secrets: {}` — build-test workflows require no secrets. Callers must not pass `secrets: inherit` to avoid exposing publish credentials to build/test execution.

**Artifact convention:** Each build workflow uploads its output to CI Artifacts with the name `build-output-<project-name>`. Publish workflows download by this exact name. The artifact layout per ecosystem:

| Ecosystem | Expected artifact contents                              |
| --------- | ------------------------------------------------------- |
| NuGet     | One or more `.nupkg` files                              |
| npm       | One `.tgz` tarball (output of `npm pack` / `pnpm pack`) |
| PyPI      | One `.whl` and one `.tar.gz` (wheel + sdist)            |
| RubyGems  | One `.gem` file                                         |
| GitHub    | All files in the artifact (uploaded as release assets)  |

### Publish Workflows

All publish workflows share a common set of inputs, with ecosystem-specific additions:

| Input           | Type     | Required | Description                                    |
| --------------- | -------- | -------- | ---------------------------------------------- |
| `artifact-name` | `string` | Yes      | CI Artifact name to download (from build step) |
| `version`       | `string` | Yes      | Package version string                         |

**Ecosystem-specific inputs:**

| Workflow                | Input        | Type      | Description                                |
| ----------------------- | ------------ | --------- | ------------------------------------------ |
| `_publish-nuget.yml`    | `feed-url`   | `string`  | NuGet feed URL (GPR or NuGet.org)          |
| `_publish-npm.yml`      | `registry`   | `string`  | npm registry URL (GPR or npmjs)            |
| `_publish-pypi.yml`     | (none extra) |           | Always publishes to PyPI via OIDC          |
| `_publish-rubygems.yml` | `host`       | `string`  | RubyGems host URL (GPR or RubyGems.org)    |
| `_publish-github.yml`   | `tag-name`   | `string`  | Git tag for the GitHub Release             |
| `_publish-github.yml`   | `prerelease` | `boolean` | Whether to mark the release as pre-release |

**Secrets:** `secrets: inherit` from the caller. Publish workflows must NOT declare their own `permissions:` block (see Section 1 constraint).

## 7. Overwrite and Idempotency Policy

Both `buddy.yml` and `official.yml` check for existing artifacts before proceeding. The policy differs by channel:

### Buddy (Unofficial)

| Condition                                                | Behavior                                                         |
| -------------------------------------------------------- | ---------------------------------------------------------------- |
| Non-pre-release GitHub Release exists                    | **Hard fail** — stable releases must not be overwritten by buddy |
| Pre-release GitHub Release exists, `force=false`         | **Fail** with guidance to re-run with `force=true`               |
| Pre-release GitHub Release exists, `force=true`          | **Overwrite** allowed                                            |
| Traceability tag exists, same commit                     | **No-op** (idempotent)                                           |
| Traceability tag exists, different commit, `force=false` | **Fail** with clear error                                        |
| Traceability tag exists, different commit, `force=true`  | **Force-update** tag                                             |
| Package version already exists at GPR                    | **Success** (idempotent publish scripts)                         |

### Official (Production)

| Condition                                           | Behavior                                                             |
| --------------------------------------------------- | -------------------------------------------------------------------- |
| Non-pre-release GitHub Release exists               | **Hard fail** — stable releases must not be overwritten              |
| Traceability tag exists, different commit           | **Hard fail** — tag SHA mismatch indicates an integrity problem      |
| Traceability tag exists, same commit                | **No-op** (idempotent)                                               |
| Pre-release GitHub Release exists                   | **Allow overwrite** (promote to stable via `promote-github-release`) |
| Package version already exists at official registry | **Success** (idempotent publish scripts)                             |

### Recovery Playbook

If a workflow run fails partway through (e.g., nuget:gpr succeeds but npm:gpr fails):

1. Fix the root cause of the failure (network issue, auth, etc.).
2. Re-trigger the same workflow with the same inputs. The idempotent publish scripts treat "already exists" as success, so completed steps are no-ops on re-run.
3. For buddy with the same version but different commit, re-run with `force=true`. For official, this scenario is an integrity failure and requires investigation.

## 8. Build Provenance (Future Enhancement)

Published packages currently carry no build provenance attestation. As a future enhancement, `official.yml`'s build jobs should add a post-build attestation step using `actions/attest-build-provenance` or ecosystem-native equivalents (PyPI via PEP 740/sigstore, npmjs via `npm attest`, NuGet.org via package signing). OIDC Trusted Publishing proves workflow identity at publish time; provenance attestation embeds that proof into the artifact itself, enabling offline verification by consumers.

## Summary of Key Design Properties

1. **PR speed maximized**: A JS-only PR never waits for the Windows C# build queue.
2. **Channel isolation**: `buddy.yml` publishes only to unofficial registries (GPR feeds, GitHub Releases as pre-release). `official.yml` publishes only to production registries (NuGet.org, PyPI, npmjs, RubyGems.org). Neither channel can reach the other's destinations. Traceability tags created by buddy use `GITHUB_TOKEN`'s anti-recursion property.
3. **Static conditional dispatch**: Because `uses:` paths must be static, both build and publish jobs use conditional `if:` guards instead of dynamic matrix dispatch to reusable workflows. Each ecosystem-destination pair has its own dedicated job.
4. **Overwrite-safe with force escape hatch**: Buddy guards against overwriting stable releases and requires explicit `force=true` to overwrite pre-release artifacts. Official guards are stricter — stable overwrites and tag SHA mismatches always fail.
5. **Least-privilege security**: Workflow-level `permissions: {}` with per-job escalation; build jobs receive `secrets: {}` (no publish credentials); OIDC for production registries with `environment = "production"` and `job_workflow_ref` claim enforcement at the registry level; `environment: production` with mandatory approval gates; branch protection on default branch.
