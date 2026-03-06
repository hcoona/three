# GitHub Workflows Design (v2.1)

This document describes the GitHub Actions workflow architecture for the `three` monorepo.

## 1. Architecture Overview (Hub-and-Spoke Pattern)

To avoid duplicating build and deploy logic across three entry workflows, the design adopts reusable workflows as the execution layer.

**Entry layer (Entry Workflows):** `ci.yml`, `buddy.yml`, `official.yml`

**Execution layer (Reusable Workflows under `.github/workflows/`):**

- `_build-test-csharp.yml` — runs on `windows-latest`
- `_build-test-python.yml` — runs on `ubuntu-latest`
- `_build-test-jsts.yml` — runs on `ubuntu-latest`
- `_publish-nuget.yml` — publishes `.nupkg` to GPR NuGet feed **or** NuGet.org (parameterized by `feed-url`)
- `_publish-npm.yml` — publishes npm tarball to GPR npm feed **or** npmjs (parameterized by `registry`)
- `_publish-pypi.yml` — publishes wheel/sdist to PyPI
- `_publish-rubygems.yml` — publishes gem to GPR RubyGems feed **or** RubyGems.org (parameterized by `host`)
- `_publish-github-release.yml` — publishes downloadable assets to GitHub Releases

The split axis is **ecosystem (tooling)**, not destination. Publishing a NuGet package to GPR vs NuGet.org uses the same tool (`dotnet nuget push`) with a different `--source` URL; the same applies to npm, RubyGems, etc. Each reusable workflow encapsulates one tool and one package format, accepting the destination as an input parameter. The caller (buddy or official) controls which destination and auth method to use.

Callers must pass `secrets: inherit` (or name each secret individually) when invoking reusable workflows. Note that `secrets: inherit` only forwards **repository/organization secrets** — it does NOT affect `permissions` (OIDC tokens, `GITHUB_TOKEN` scopes, etc.). Permissions are inherited automatically: a reusable workflow receives the caller job's `permissions` grants as long as the reusable workflow itself does **not** declare its own `permissions` block. This is what allows the same `_publish-nuget.yml` to operate under `packages: write` when called from `buddy.yml` and under `id-token: write` when called from `official.yml`.

**Permissions model:** Every entry workflow declares `permissions: {}` at workflow level. Individual jobs then request only the scopes they need (principle of least privilege). Key scopes:

| Job kind                            | Required `permissions` |
| ----------------------------------- | ---------------------- |
| Push tags                           | `contents: write`      |
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
    - `python`: `['**/*.py', 'pyproject.toml', 'uv.lock', 'mise.toml']`
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
    - `needs: [test-csharp, test-python, test-jsts]`
    - Asserts all required checks either passed or were legitimately skipped. This prevents the "skipped-all blocks required status checks" problem when a PR modifies only docs or non-code files.

    ```yaml
    ci-passed:
        if: always()
        needs: [test-csharp, test-python, test-jsts]
        runs-on: ubuntu-latest
        steps:
            - name: Assert all required checks passed or were skipped
              run: |
                  results='${{ toJson(needs) }}'
                  echo "$results" | jq -e '
                    to_entries
                    | map(.value.result == "success" or .value.result == "skipped")
                    | all'
    ```

## 3. `buddy.yml` — Unofficial Release (Dynamic Matrix, Tag Isolation)

**Trigger:** `on: workflow_dispatch`
**Input:** User selects `project-name`.

Even within the same language, different projects may have different packaging strategies (EXE, NuGet, wheel, etc.). The workflow resolves publish targets dynamically from project configuration.

**Jobs:**

1. **`resolve-context`**:
    - Runs a script (reusing `eng/scripts/find_*_project_path`) to determine: language, project path, and version (via NBGV).
    - **NBGV resolution:** The checkout must use `fetch-depth: 0` so NBGV can compute version height from git history. The script locates the correct `version.json` by searching upward from the project directory. If the resolved version has already been published, the script should warn but not fail (the idempotent publish scripts handle this downstream).
    - Reads the project's release configuration and emits a JSON array of publish targets. Targets use the format `ecosystem:destination` (e.g. `["nuget:gpr", "nuget:official", "github_release"]`).
    - **Validates the output** against an explicit allowlist before setting the job output:

    ```python
    VALID_TARGETS = frozenset({
        "nuget:gpr", "nuget:official",
        "npm:gpr", "npm:official",
        "pypi:official",
        "rubygems:gpr", "rubygems:official",
        "github_release",
    })
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

    Only one of this three jobs will actually execute. Build artifacts (`.nupkg`, `.whl`, `.exe`, etc.) are uploaded to CI Artifacts.

4. **`publish-unofficial`** (dynamic matrix):
    - `needs: [resolve-context, build-csharp, build-python, build-jsts]`
    - `if: always() && !cancelled() && !failure()` (to handle the two skipped build jobs)
    - `strategy.matrix.target: ${{ fromJson(needs.resolve-context.outputs.targets) }}`
    - Each matrix leg parses the `ecosystem:destination` target value, calls the corresponding `_publish-{ecosystem}.yml` reusable workflow, and passes the destination (GPR feed URL vs official registry) as an input parameter.
    - For GPR targets, auth uses `GITHUB_TOKEN` with `packages: write`. No OIDC is needed.
    - Each publish step uses the idempotent scripts (`eng/scripts/publish_*_idempotent.sh`) that treat "version already exists" as a success exit code.

5. **`create-traceability-tag`**:
    - `needs: publish-unofficial`
    - `permissions: contents: write`
    - Assembles and pushes a Git tag: `release/<project-name>/v<version>`.
    - Uses `${{ secrets.GITHUB_TOKEN }}` to run `git push origin <tag>`. Per GitHub docs, events triggered by `GITHUB_TOKEN` will **not** create new workflow runs (anti-recursion mechanism). This ensures the traceability tag records the source commit without triggering `official.yml`.

## 4. `official.yml` — Production Release

**Important:** `buddy.yml` and `official.yml` are **independent release channels**, not a sequential promotion pipeline. Buddy publishes to unofficial registries (GitHub Packages, GitHub Releases); official publishes to production registries (NuGet.org, PyPI, npmjs). A buddy run is NOT a prerequisite for an official run — either can be triggered independently.

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

`workflow_dispatch` is the **primary trigger**. The operator specifies the exact tag to release. The workflow must explicitly `git checkout` the commit referenced by that tag — not the branch HEAD selected in the dispatch UI.

> **Optional secondary trigger:** `on: push: tags: 'release/*/v*'` may be retained as an automated trigger. If so, a GitHub repository ruleset **must** restrict creation of `release/**` tags to maintainer accounts or a release-bot service account (see "Prerequisites" below).

**Prerequisites (must be configured before first run):**

- **`environment: production`** must exist in GitHub repository settings with protection rules (required reviewers, deployment branches, etc.) **before** the workflow is ever triggered. If this environment does not pre-exist, GitHub auto-creates it with **zero** protection rules and the human approval gate silently does not exist.
- If `push: tags:` is used, a **tag protection ruleset** must restrict the `release/**` namespace to authorized accounts only.

**Jobs:**

1. **`resolve-tag`**: Parses `${{ inputs.tag-name }}` (or `${{ github.ref_name }}` when triggered by tag push) to extract `project-name` and `version`. Determines which official registries to publish to.

2. **`clean-build`**:
    - For supply chain security, no prior artifacts are reused. A fresh build and test run is performed from the exact commit the tag points to.
    - Uses the same three static conditional build jobs pattern as `buddy.yml` (`build-csharp` / `build-python` / `build-jsts`), calling the corresponding reusable workflow.

3. **`publish-official`** (dynamic matrix):
    - `needs: [resolve-tag, clean-build]`
    - `environment: production` — **mandatory**, not optional. This enables human approval gates and OIDC token issuance.
    - `permissions: id-token: write` — scoped only to this job for OIDC Trusted Publishing (NuGet.org, PyPI, npmjs, RubyGems.org all support OIDC).
    - `strategy.fail-fast: false` — a failure in one registry must not cancel others.
    - Each matrix leg calls the same per-ecosystem `_publish-{ecosystem}.yml` workflow as `buddy.yml`, but with the official destination and OIDC auth instead of GPR + `GITHUB_TOKEN`.
    - Each publish step uses the idempotent scripts (`eng/scripts/publish_*_idempotent.sh`). If a version already exists at the target registry, the step exits successfully. This ensures the workflow can be safely re-run after partial failures.

## Summary of Key Design Properties

1. **PR speed maximized**: A JS-only PR never waits for the Windows C# build queue.
2. **Channel isolation with traceability**: `buddy.yml` tags the source commit for unofficial releases using `GITHUB_TOKEN`'s anti-recursion property, while `official.yml` runs independently via `workflow_dispatch`.
3. **Highly decoupled**: Multi-target publishing (exe / nupkg coexistence) is handled by dynamic JSON matrix — whether a project has 1 or 3 publish targets, they fan out to parallel jobs automatically.
4. **Idempotent and recoverable**: All publish steps treat "already exists" as success, enabling safe re-runs after partial failures.
5. **Least-privilege security**: Workflow-level `permissions: {}` with per-job escalation, OIDC for production registries, `environment: production` with mandatory approval gates.
