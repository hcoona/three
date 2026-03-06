# GitHub Workflows Design (v2.2)

This document describes the GitHub Actions workflow architecture for the `three` monorepo.

## 1. Architecture Overview (Shared Execution Layer)

To avoid duplicating build and deploy logic across three entry workflows, the design adopts reusable workflows as the shared execution layer. Each entry workflow independently invokes the same set of reusable workflows — there is no single dispatching hub.

**Entry layer (Entry Workflows):** `ci.yml`, `buddy.yml`, `official.yml`

**Execution layer (Reusable Workflows under `.github/workflows/`):**

- `_build-test-csharp.yml` — runs on `windows-latest`
- `_build-test-python.yml` — runs on `ubuntu-latest`
- `_build-test-jsts.yml` — runs on `ubuntu-latest`
- `_publish-nuget.yml` — publishes `.nupkg` to GPR NuGet feed **or** NuGet.org (parameterized by `feed-url`)
- `_publish-npm.yml` — publishes npm tarball to GPR npm feed **or** npmjs (parameterized by `registry`)
- `_publish-pypi.yml` — publishes wheel/sdist to PyPI (official only; GitHub Packages does not offer a PyPI-compatible feed)
- `_publish-rubygems.yml` — publishes gem to GPR RubyGems feed **or** RubyGems.org (parameterized by `host`)
- `_publish-github.yml` — publishes downloadable assets to GitHub Releases

The split axis is **ecosystem (tooling)**, not destination. Publishing a NuGet package to GPR vs NuGet.org uses the same tool (`dotnet nuget push`) with a different `--source` URL; the same applies to npm, RubyGems, etc. Each reusable workflow encapsulates one tool and one package format, accepting the destination as an input parameter. The caller (buddy or official) controls which destination and auth method to use.

Callers must pass `secrets: inherit` (or name each secret individually) when invoking reusable workflows. Note that `secrets: inherit` forwards **repository secrets, organization secrets, and environment secrets accessible to the calling job** (i.e., if the calling job specifies `environment: production`, environment-scoped secrets are included). It does NOT affect `permissions` (OIDC tokens, `GITHUB_TOKEN` scopes, etc.).

Permissions are inherited automatically: a reusable workflow receives the caller job's `permissions` grants as long as the reusable workflow itself does **not** declare its own `permissions` block. This is what allows the same `_publish-nuget.yml` to operate under `packages: write` when called from `buddy.yml` and under `id-token: write` when called from `official.yml`.

> **Important constraint:** Reusable workflows must NOT declare their own `permissions:` block. If they do, the effective token is silently capped at the intersection of the declared scopes and the caller's grants. For example, if a reusable workflow declares `permissions: { id-token: write }` but the caller only grants `packages: write`, the minted token will have `id-token: none`, causing silent runtime failures. Keep all `permissions:` declarations in the entry workflows only.

**Permissions model:** Every entry workflow declares `permissions: {}` at workflow level. Individual jobs then request only the scopes they need (principle of least privilege). Key scopes:

| Job kind                            | Required `permissions` |
| ----------------------------------- | ---------------------- |
| Push tags                           | `contents: write`      |
| Create GitHub Release               | `contents: write`      |
| GitHub Packages (any feed)          | `packages: write`      |
| OIDC publish to official registries | `id-token: write`      |
| Read-only checkout                  | `contents: read`       |

All four official registries (NuGet.org, PyPI, npmjs, RubyGems.org) support OIDC Trusted Publishing. GPR feeds use `GITHUB_TOKEN` with `packages: write` instead.

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

1. **`static-analysis`**: Runs `jdx/hk` (`hk check`) on an Ubuntu runner. HK auto-detects file types, serving as the first gate for formatting and linting failures.

2. **`detect-changes`**: Uses `dorny/paths-filter` to classify modified files:
    - `csharp`: `['**/*.cs', '**/*.csproj', 'global.json', 'Directory.*.props', 'NuGet.Config', '**/*.targets', '**/packages.lock.json']`
    - `python`: `['**/*.py', 'pyproject.toml', 'uv.lock']`
    - `jsts`: `['**/*.ts', '**/*.js', 'package.json', 'pnpm-workspace.yaml', 'pnpm-lock.yaml', 'biome.jsonc', 'tsconfig*.json']`
    - `infra`: `['.github/workflows/**', 'eng/scripts/**', 'mise.toml', 'hk.pkl']`

    When `infra` changes are detected, all three language test suites are triggered regardless of other filters.

    > **Scaling note:** The current filters operate at language level (`**/*.cs` triggers all C# builds). As the monorepo grows past ~10 projects per language, this should evolve to per-project granularity using affected-project detection from `eng/scripts/find_*_project_path.py`.

3. **`test-csharp` / `test-python` / `test-jsts`** (run in parallel):
    - `needs: [detect-changes, static-analysis]`
    - Conditional: e.g. `if: needs.detect-changes.outputs.csharp == 'true' || needs.detect-changes.outputs.infra == 'true'`
    - Each calls its corresponding reusable workflow. C# uses `windows-latest`; the others use `ubuntu-latest`.

4. **`ci-passed`** (final gate job):
    - `if: always()`
    - `needs: [static-analysis, test-csharp, test-python, test-jsts]`
    - Asserts all required checks either passed or were legitimately skipped. Including `static-analysis` in `needs` ensures a static-analysis failure blocks the gate (otherwise, the `test-*` jobs would be skipped and the gate would pass). This prevents the "skipped-all blocks required status checks" problem when a PR modifies only docs or non-code files.

    ```yaml
    ci-passed:
        if: always()
        needs: [static-analysis, test-csharp, test-python, test-jsts]
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

**Trigger:** `on: workflow_dispatch`
**Input:** User selects `project-name`. All workflow inputs must be mapped to intermediate environment variables before use in shell scripts (e.g., `env: PROJECT_NAME: ${{ inputs.project-name }}`; use `"$PROJECT_NAME"` in bash, never `${{ inputs.project-name }}` directly in `run:` blocks).

Even within the same language, different projects may have different packaging strategies (EXE, NuGet, wheel, etc.). The workflow resolves publish targets dynamically from project configuration.

**Jobs:**

1. **`resolve-context`**:
    - **Runner and tooling:** Runs on `ubuntu-latest`. Requires `mise install` to bootstrap Python (for `eng/scripts/find_*_project_path.py`) and the .NET SDK (for NBGV via the `nbgv-python` adapter). The `mise.toml` at the repo root pins all tool versions.
    - Runs a script (reusing `eng/scripts/find_*_project_path`) to determine: language, project path, and version (via NBGV).
    - **NBGV resolution:** The checkout must use `fetch-depth: 0` so NBGV can compute version height from git history. The script locates the correct `version.json` by searching upward from the project directory. If the resolved version has already been published, the script should warn but not fail (the idempotent publish scripts handle this downstream).
    - Reads the project's release configuration (see **Section 5: Release Configuration Contract**) and emits a JSON array of publish targets. Targets use the format `ecosystem:destination` (e.g. `["nuget:gpr", "nuget:official", "github:release"]`).
    - **Validates the output** against an explicit allowlist before setting the job output. The allowlist is partitioned into unofficial-only and official-only targets; `buddy.yml` uses the full set while `official.yml` filters to `OFFICIAL_TARGETS` only:

    ```python
    UNOFFICIAL_ONLY_TARGETS = frozenset({
        "nuget:gpr", "npm:gpr", "rubygems:gpr", "github:release",
    })
    OFFICIAL_TARGETS = frozenset({
        "nuget:official", "npm:official", "pypi:official", "rubygems:official",
    })
    VALID_TARGETS = UNOFFICIAL_ONLY_TARGETS | OFFICIAL_TARGETS
    assert all(t in VALID_TARGETS for t in targets), f"Unknown target in {targets}"
    assert len(targets) > 0, "No publish targets resolved"
    ```

2. **`static-analysis`**: Runs `hk check` scoped to the resolved project path.

3. **`build-csharp` / `build-python` / `build-jsts`** (static conditional jobs):
    - `needs: [resolve-context, static-analysis]`
    - Because GitHub Actions resolves `uses:` statically at parse time, a single job cannot dynamically select a reusable workflow at runtime. Instead, three separate jobs are defined with conditional execution:

    ```yaml
    build-csharp:
        needs: [resolve-context, static-analysis]
        if: needs.resolve-context.outputs.language == 'csharp'
        uses: ./.github/workflows/_build-test-csharp.yml
        secrets: inherit

    build-python:
        needs: [resolve-context, static-analysis]
        if: needs.resolve-context.outputs.language == 'python'
        uses: ./.github/workflows/_build-test-python.yml
        secrets: inherit

    build-jsts:
        needs: [resolve-context, static-analysis]
        if: needs.resolve-context.outputs.language == 'jsts'
        uses: ./.github/workflows/_build-test-jsts.yml
        secrets: inherit
    ```

    Only one of these three jobs will actually execute. Build artifacts (`.nupkg`, `.whl`, `.exe`, etc.) are uploaded to CI Artifacts using a deterministic name: `build-output-<project-name>` (e.g. `build-output-my-library`). Artifacts are built fresh within this workflow run; no artifacts from prior runs are downloaded.

4. **`publish-nuget` / `publish-npm` / `publish-pypi` / `publish-rubygems` / `publish-github`** (static conditional publish jobs):
    - Because GitHub Actions resolves `uses:` statically at parse time, publish jobs use the same static conditional pattern as build jobs. Each publish job is conditioned on whether its ecosystem appears in the resolved targets:

    ```yaml
    publish-nuget:
        needs: [resolve-context, build-csharp, build-python, build-jsts]
        if: |
            always() && !cancelled() && !failure() &&
            (contains(needs.resolve-context.outputs.targets, 'nuget:gpr') ||
             contains(needs.resolve-context.outputs.targets, 'nuget:official'))
        uses: ./.github/workflows/_publish-nuget.yml
        with:
            artifact-name: build-output-${{ needs.resolve-context.outputs.project-name }}
            # destination and feed-url derived from targets
        secrets: inherit

    publish-npm:
        needs: [resolve-context, build-csharp, build-python, build-jsts]
        if: |
            always() && !cancelled() && !failure() &&
            (contains(needs.resolve-context.outputs.targets, 'npm:gpr') ||
             contains(needs.resolve-context.outputs.targets, 'npm:official'))
        uses: ./.github/workflows/_publish-npm.yml
        secrets: inherit

    # ... same pattern for publish-pypi, publish-rubygems, publish-github
    ```

    - The `if: always() && !cancelled() && !failure()` guard ensures the publish jobs run despite the two skipped build jobs in the `needs` chain. This condition is safe because skipped jobs are treated as neither failure nor cancellation.
    - For GPR targets, auth uses `GITHUB_TOKEN` with `packages: write`. No OIDC is needed.
    - Each publish step uses the idempotent scripts (`eng/scripts/publish_*_idempotent.sh`) that treat "version already exists" as a success exit code.

5. **`create-traceability-tag`**:
    - `needs: [publish-nuget, publish-npm, publish-pypi, publish-rubygems, publish-github]`
    - `if: always() && !cancelled() && !failure()`
    - `permissions: contents: write`
    - Assembles and pushes a Git tag: `release/<project-name>/v<version>`.
    - **Idempotent tag logic:** Before pushing, check if the tag already exists. If absent, create it. If it exists and points to the same commit, succeed as no-op. If it exists but points to a different commit, fail with a clear error message.
    - Uses `${{ secrets.GITHUB_TOKEN }}` to run `git push origin <tag>`. Per GitHub docs, events triggered by `GITHUB_TOKEN` will **not** create new workflow runs (anti-recursion mechanism). This ensures the traceability tag records the source commit without triggering `official.yml`.

## 4. `official.yml` — Production Release

**Important:** `buddy.yml` and `official.yml` are **independent release channels**, not a sequential promotion pipeline. Buddy publishes to unofficial registries (GitHub Packages, GitHub Releases); official publishes to production registries (NuGet.org, PyPI, npmjs, RubyGems.org). A buddy run is NOT a prerequisite for an official run — either can be triggered independently.

**Trigger:**

```yaml
on:
    workflow_dispatch:
        inputs:
            tag-name:
                description: 'Release tag (e.g. release/my-project/v1.2.3)'
                required: true
                type: string
```

`workflow_dispatch` is the **primary trigger**. The operator specifies the exact tag to release. All workflow inputs must be mapped to intermediate environment variables before use in shell scripts (same pattern as `buddy.yml`).

**Tag checkout mechanism:** The checkout step must use `ref: refs/tags/${{ inputs.tag-name }}` (not the branch HEAD selected in the dispatch UI). After checkout, an assertion step must verify the checked-out `HEAD` SHA matches the dereferenced tag target SHA (to handle annotated tags correctly). This prevents accidental releases from the wrong commit.

> **Optional secondary trigger:** `on: push: tags: 'release/*/v*'` may be retained as an automated trigger. If so, a GitHub repository ruleset **must** restrict creation of `release/**` tags to maintainer accounts or a release-bot service account (see "Prerequisites" below).

**Prerequisites (must be configured before first run):**

- **Branch protection** on the default branch must require: PR review approval and the `ci-passed` required status check before merging. Without this, direct pushes bypass `ci.yml` entirely, allowing unreviewed code to be released.
- **`environment: production`** must exist in GitHub repository settings with protection rules (required reviewers, deployment branches, etc.) **before** the workflow is ever triggered. If this environment does not pre-exist, GitHub auto-creates it with **zero** protection rules and the human approval gate silently does not exist.
- **OIDC trust policies:** Each external registry's Trusted Publisher configuration must enforce the `environment = "production"` claim in its OIDC subject/claims filter. Without this, any workflow in the repository with `id-token: write` could publish to production, bypassing the approval gate.
- If `push: tags:` is used, a **tag protection ruleset** must restrict the `release/**` namespace to authorized accounts only.

**Jobs:**

1. **`resolve-tag`**:
    - Parses `${{ inputs.tag-name }}` (or `${{ github.ref_name }}` when triggered by tag push) to extract `project-name` and `version`.
    - **Input validation:** The tag must match a strict format pattern (e.g., `^release/[a-zA-Z0-9._-]+/v[0-9]+\.[0-9]+\.[0-9]+`). Reject malformed tags with a hard failure.
    - Determines which official registries to publish to by filtering the project's release config targets to `OFFICIAL_TARGETS` only (see Section 3, `resolve-context`).

2. **`clean-build`**:
    - For supply chain security, no prior artifacts are reused. A fresh build and test run is performed from the exact commit the tag points to.
    - Uses the same three static conditional build jobs pattern as `buddy.yml` (`build-csharp` / `build-python` / `build-jsts`), calling the corresponding reusable workflow.

3. **`publish-nuget` / `publish-npm` / `publish-pypi` / `publish-rubygems`** (static conditional publish jobs):
    - Uses the same static conditional pattern as `buddy.yml`'s publish jobs (see Section 3, step 4), but filtered to `OFFICIAL_TARGETS` only — `github:release` is excluded since GitHub Releases are an unofficial channel.
    - `needs: [resolve-tag, build-csharp, build-python, build-jsts]`
    - `environment: production` — **mandatory**, not optional. This enables human approval gates and OIDC token issuance. Note: when multiple publish jobs each declare `environment: production`, each triggers its own independent approval gate. This is intentional — approvers must confirm each destination individually.
    - `permissions: id-token: write` — scoped only to these jobs for OIDC Trusted Publishing (NuGet.org, PyPI, npmjs, RubyGems.org all support OIDC).
    - Each publish job calls the same per-ecosystem `_publish-{ecosystem}.yml` workflow as `buddy.yml`, but with the official destination and OIDC auth instead of GPR + `GITHUB_TOKEN`.
    - Each publish step uses the idempotent scripts (`eng/scripts/publish_*_idempotent.sh`). If a version already exists at the target registry, the step exits successfully. This ensures the workflow can be safely re-run after partial failures.

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

**Lookup behavior:** The script searches for `release.json` starting from the project directory (resolved by `eng/scripts/find_*_project_path.py`). If the file is absent, the workflow fails with a clear error — there is no default target set.

**Valid target values:**

| Target              | Channel    | Description                                     |
| ------------------- | ---------- | ----------------------------------------------- |
| `nuget:gpr`         | Unofficial | Publish `.nupkg` to GitHub Packages NuGet feed  |
| `nuget:official`    | Official   | Publish `.nupkg` to NuGet.org                   |
| `npm:gpr`           | Unofficial | Publish npm tarball to GitHub Packages npm feed |
| `npm:official`      | Official   | Publish npm tarball to npmjs                    |
| `pypi:official`     | Official   | Publish wheel/sdist to PyPI                     |
| `rubygems:gpr`      | Unofficial | Publish gem to GitHub Packages RubyGems feed    |
| `rubygems:official` | Official   | Publish gem to RubyGems.org                     |
| `github:release`    | Unofficial | Upload assets to GitHub Releases                |

`buddy.yml` processes all targets. `official.yml` filters to official-channel targets only.

## 6. Reusable Workflow I/O Contracts

### Build-Test Workflows

All three build-test workflows share the same input/output structure:

| Input          | Type     | Required | Description                                   |
| -------------- | -------- | -------- | --------------------------------------------- |
| `project-path` | `string` | Yes      | Path to the project directory within the repo |
| `project-name` | `string` | Yes      | Project name (used for artifact naming)       |

| Output          | Type     | Description                                                     |
| --------------- | -------- | --------------------------------------------------------------- |
| `artifact-name` | `string` | Name of the uploaded CI Artifact: `build-output-<project-name>` |

**Secrets:** `secrets: inherit` from the caller. No additional secrets required.

**Artifact convention:** Each build workflow uploads its output to CI Artifacts with the name `build-output-<project-name>`. Publish workflows download by this exact name.

### Publish Workflows

All publish workflows share a common set of inputs, with ecosystem-specific additions:

| Input           | Type     | Required | Description                                    |
| --------------- | -------- | -------- | ---------------------------------------------- |
| `artifact-name` | `string` | Yes      | CI Artifact name to download (from build step) |
| `version`       | `string` | Yes      | Package version string                         |

**Ecosystem-specific inputs:**

| Workflow                | Input        | Type     | Description                             |
| ----------------------- | ------------ | -------- | --------------------------------------- |
| `_publish-nuget.yml`    | `feed-url`   | `string` | NuGet feed URL (GPR or NuGet.org)       |
| `_publish-npm.yml`      | `registry`   | `string` | npm registry URL (GPR or npmjs)         |
| `_publish-pypi.yml`     | (none extra) |          | Always publishes to PyPI via OIDC       |
| `_publish-rubygems.yml` | `host`       | `string` | RubyGems host URL (GPR or RubyGems.org) |
| `_publish-github.yml`   | `tag-name`   | `string` | Git tag for the GitHub Release          |

**Secrets:** `secrets: inherit` from the caller. Publish workflows must NOT declare their own `permissions:` block (see Section 1 constraint).

## Summary of Key Design Properties

1. **PR speed maximized**: A JS-only PR never waits for the Windows C# build queue.
2. **Channel isolation with traceability**: `buddy.yml` tags the source commit for unofficial releases using `GITHUB_TOKEN`'s anti-recursion property, while `official.yml` runs independently via `workflow_dispatch`. `github:release` targets are unofficial-only; official publish jobs are filtered to production registries.
3. **Static conditional dispatch**: Because `uses:` paths must be static, both build and publish jobs use conditional `if:` guards instead of dynamic matrix dispatch to reusable workflows. Each ecosystem has a dedicated job that activates only when relevant targets are present.
4. **Idempotent and recoverable**: All publish steps treat "already exists" as success, enabling safe re-runs after partial failures. Tag creation uses three-way idempotency (absent → create, same commit → no-op, different commit → fail).
5. **Least-privilege security**: Workflow-level `permissions: {}` with per-job escalation, OIDC for production registries with `environment = "production"` claim enforcement at the registry level, `environment: production` with mandatory approval gates, branch protection on default branch.
