# Review: GitHub Workflows Design (v2)

<!-- markdownlint-disable MD036 -->

**Overall Verdict:** The architecture's hub-and-spoke pattern, runner separation by ecosystem, channel isolation between CI/buddy/official, and OIDC direction are all well-reasoned design decisions appropriate for a polyglot monorepo. However, **two blocking technical errors** make the design unimplementable as written, and several operational gaps must be resolved before implementation begins.

---

## Blocking Issues (Must Fix Before Implementation)

### B-1 — "Dynamic Language Dispatch" Is a GitHub Actions Platform Error

**Section 3 (`build-and-pack`)**

The design states `build-and-pack` will "dynamically call the corresponding Reusable Workflow based on language." In GitHub Actions, the `uses:` key is resolved **statically at parse time** — it cannot be a runtime expression. A single job cannot select a reusable workflow path at runtime using `${{ steps.ctx.outputs.language }}` or any expression.

**Correct pattern:** Three separate static conditional jobs:

```yaml
build-csharp:
    needs: [resolve-context, static-analysis]
    if: needs.resolve-context.outputs.language == 'csharp'
    uses: ./.github/workflows/_build-test-csharp.yml

build-python:
    needs: [resolve-context, static-analysis]
    if: needs.resolve-context.outputs.language == 'python'
    uses: ./.github/workflows/_build-test-python.yml

build-jsts:
    needs: [resolve-context, static-analysis]
    if: needs.resolve-context.outputs.language == 'jsts'
    uses: ./.github/workflows/_build-test-jsts.yml
```

### B-2 — The `buddy.yml` → `official.yml` Promotion Path Is Mechanically Broken

**Section 3 + Section 4**

`buddy.yml` creates tag `release/<project>/v1.2.3` via `GITHUB_TOKEN`. `official.yml` listens for `on: push: tags: release/*/v*`. The claim that `GITHUB_TOKEN`-pushed tags will not trigger `official.yml` is **technically correct** (confirmed by GitHub docs: "events triggered by the GITHUB_TOKEN will not create a new workflow run"). However, because the tag `release/<project>/v1.2.3` already exists in Git after `buddy.yml` runs, the PAT-based promotion step alluded to in Section 4 will fail — you cannot re-push an existing tag without `--force`, which is unsafe for release tags.

The design's own "absolutist" language ("绝对不会 / absolutely never") is editorially imprecise but technically correct for the stated scenario. The real problem is that the promotion model is mechanically broken.

**Fix direction:** Use a distinct tag namespace for buddy builds (e.g., `buddy/<project>/v<version>`) and reserve `release/<project>/v<version>` exclusively for official releases. The official trigger pattern then unambiguously targets only official-intent tag pushes.

---

## High Priority Issues (Should Fix in Design)

### H-1 — `official.yml` Parallel Publish Is Non-Recoverable After Partial Failure

**Section 4 (`publish-official`)**

NuGet.org, PyPI, and npmjs permanently reject republishing an identical version. The design specifies a parallel matrix strategy with no `fail-fast: false` guidance, no skip-if-already-published logic, and no recovery playbook. If the matrix partially succeeds (e.g., NuGet publishes, then PyPI fails due to a transient OIDC timeout), the workflow cannot be re-run — the NuGet job will fail with "version already exists." The release version is permanently stuck in a partially-published state.

**Fix:** Explicitly reference the existing `eng/scripts/publish_*_idempotent.sh` scripts as the implementation contract. Each publish step must treat "version already exists at target registry" as a success exit code.

### H-2 — No Tag Protection Ruleset for the `release/*/v*` Namespace

**Section 4 trigger**

Any repository collaborator with write access can push a `release/<project>/v99.0.0` tag from their local Git client and trigger a full production publish run. The design states official releases require "a PAT with specific permissions," but provides no enforcement mechanism.

**Fix:** Document a required GitHub repository ruleset restricting creation of `release/**` tags to maintainer accounts or a release-bot service account. This is a mandatory pre-deployment prerequisite, not a recommendation.

### H-3 — `publish-unofficial` Missing `needs: build-and-pack`

**Section 3, Job 4**

The design shows `publish-unofficial` referencing `needs.resolve-context.outputs.targets` but only explicitly states a dependency on `resolve-context`. Without `needs: [resolve-context, build-and-pack]`, GitHub Actions may schedule `publish-unofficial` before artifacts exist, causing guaranteed runtime failure at artifact download.

### H-4 — No `permissions:` Model Documented

**Design-wide**

No `permissions:` blocks are specified for any workflow or job. Without explicit declarations, every job inherits the repository's default GITHUB_TOKEN scope (which may include broad write permissions depending on org settings).

Key required declarations:

- `contents: write` on the `create-traceability-tag` job (required to push tags)
- `packages: write` on any GitHub Packages publish job
- `id-token: write` scoped only to OIDC publish jobs in `official.yml`
- `permissions: {}` at workflow level for all entry workflows

### H-5 — `_publish-target.yml` Violates the Single Responsibility Principle

**Section 1**

A single reusable workflow handling GitHub Packages, GitHub Releases, NuGet.org, PyPI, and npm simultaneously requires all publish credentials at once, uses incompatible auth mechanisms per target (GITHUB_TOKEN vs. OIDC vs. API keys), and different tooling per registry. As the number of supported registries grows, this file will become a fragile conditional maze.

**Fix:** Split into per-ecosystem publish workflows: `_publish-github.yml`, `_publish-nuget.yml`, `_publish-pypi.yml`, `_publish-npm.yml`. The dynamic matrix calls the appropriate one. Each receives only the permissions and secrets it needs.

### H-6 — `environment: production` Must Be a Hard Prerequisite, Not a Recommendation

**Section 4 (`publish-official`)**

The design phrases `environment: production` as "安全建议 (security recommendation)." If this environment does not exist in repository settings when the workflow first runs, GitHub auto-creates it with zero protection rules — the human approval gate silently does not exist. The OIDC and manual approval protection described in the design is illusory without this being pre-configured.

**Fix:** Document this as a mandatory pre-deployment setup step. Add a preflight job in `official.yml` that asserts the environment's protection rules are active via the GitHub API.

> **Note:** The two-job mandatory split for OIDC (separate gate + publish jobs) cited in some reviews is a best practice for separation of concerns — it is NOT a hard technical requirement. Having `environment:` and `id-token: write` on the same job is technically valid.

---

## Medium Priority Issues (Should Address)

### M-1 — CI Path Filters Have Significant Blind Spots

**Section 2 (`detect-changes`)**

The current filter patterns cover primary source files but miss toolchain and infrastructure changes that directly affect build correctness:

| Category       | Missing Patterns                                        |
| -------------- | ------------------------------------------------------- |
| All languages  | `mise.toml`, `hk.pkl`                                   |
| C#             | `NuGet.Config`, `**/*.targets`, `**/packages.lock.json` |
| JS/TS          | `biome.jsonc`, `tsconfig*.json`, `pnpm-lock.yaml`       |
| Infrastructure | `.github/workflows/**`, `eng/scripts/**`                |

A PR that pins a new .NET SDK version in `global.json` passes CI if no `.cs` files changed. A PR modifying `biome.jsonc` skips the JS/TS validation entirely.

### M-2 — Skipped-All Pattern Will Block Required Status Checks

**Section 2**

When a PR modifies only documentation or configuration outside the filter patterns, all three test jobs are skipped (via `if:` condition). GitHub required status checks treat a skipped job as not-passing, permanently blocking merged PRs.

**Fix:** Add a final `ci-passed` gate job:

```yaml
ci-passed:
    if: always()
    needs: [test-csharp, test-python, test-jsts]
    runs-on: ubuntu-latest
    steps:
        - name: Assert all required checks passed or were skipped
          run: |
              results='${{ toJson(needs) }}'
              echo "$results" | jq -e 'to_entries | map(.value.result == "success" or .value.result == "skipped") | all'
```

### M-3 — No Concurrency Groups Defined

**Design-wide**

Without `concurrency:` blocks, two simultaneous `buddy.yml` triggers for the same project will race on publish and tag creation. Multiple open PRs will queue redundant CI runs.

Recommended policy:

- `ci.yml`: `group: ci-${{ github.ref }}`, `cancel-in-progress: true`
- `buddy.yml`: `group: buddy-${{ inputs.project-name }}`, `cancel-in-progress: false`
- `official.yml`: `group: official-${{ github.ref_name }}`, `cancel-in-progress: false`

### M-4 — Dynamic Matrix Output Has No Schema or Allowlist Validation

**Section 3 (`publish-unofficial`)**

`strategy.matrix.target: ${{ fromJson(needs.resolve-context.outputs.targets) }}` passes script-generated JSON directly into the matrix. An empty array `[]` silently produces zero jobs while `create-traceability-tag` still runs, creating a tag for a build that published nothing. Unknown target values could direct publishes to unintended channels.

**Fix:** In `resolve-context`, validate against an explicit allowlist before setting output:

```python
VALID_TARGETS = frozenset({"gpr", "github_release", "nuget", "pypi", "npmjs"})
assert all(t in VALID_TARGETS for t in targets), f"Unknown target in {targets}"
assert len(targets) > 0, "No publish targets resolved"
```

### M-5 — Reusable Workflow Secret Inheritance Not Addressed

**Section 1**

Secrets are not automatically inherited by called reusable workflows. Callers must explicitly pass `secrets: inherit` or name each secret individually. Any registry credential needed inside `_build-test-*.yml` workflows (NuGet feed tokens, signing keys, etc.) will silently be unavailable at runtime if this is not specified.

### M-6 — Third-Party Actions Not SHA-Pinned

**Section 2 (`dorny/paths-filter`)**

Referencing third-party actions by mutable tags (e.g., `dorny/paths-filter@v3`) creates a supply chain vulnerability. A compromised upstream account can silently replace the tag with malicious code.

**Fix:** Pin to full commit SHA and use Renovate/Dependabot to manage updates:

```yaml
uses: dorny/paths-filter@de90cc6ed7cd597cb74b84a7e832ce805e3c7b15 # v3.0.2
```

---

## Additional Design Gaps

### G-1 — `official.yml` Has No Defined Trigger Mechanism

**Section 4**

The design states `official.yml` is triggered by "a tag pushed by a PAT or dedicated deployment script" but never defines what creates that tag. There is no `promote.yml` workflow, no documented manual procedure, and no Git command specified. As written, `official.yml` is a dead workflow with no concrete trigger path.

### G-2 — `_publish-target.yml` Interface Is Entirely Undefined

**Section 1**

The reusable workflow central to both `buddy.yml` and `official.yml` has no documented inputs, outputs, or secrets contract. Implementors have no spec to build against.

### G-3 — NBGV Multi-Project Version Resolution Not Addressed

**Section 3 (`resolve-context`)**

The monorepo contains multiple `version.json` files at different directory levels. NBGV resolves version by walking up the directory tree from the project root. The design does not address:

- How the correct `version.json` is located per project
- Whether `fetch-depth: 0` is enforced (required for NBGV height calculation)
- What happens when the resolved version has already been published (especially on retry)

### G-4 — CI Path Filtering Does Not Scale to Per-Project Granularity

**Section 2**

The current language-level filters (`**/*.cs` triggers all C# builds) will create significant bottlenecks as the monorepo grows. A change to one C# utility project triggers tests across every C# project. The design should note this scaling cliff and plan for a future evolution to project-level granularity using affected-project detection from `eng/scripts/find_*_project_path.py`.

---

## Notable False Positives (Dismissed)

The following claims appeared in intermediate review passes but are **not genuine issues**:

| Claim                                                                                | Verdict                                                                                                                                                                                                                     |
| ------------------------------------------------------------------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| "`GITHUB_TOKEN` absolutely never triggers tag-based workflows" is wrong              | **False positive.** GitHub docs explicitly confirm: "events triggered by the GITHUB_TOKEN will not create a new workflow run." This applies to all `push` events including tag pushes. The behavior is correctly described. |
| `environment` + `id-token: write` on the same job requires a mandatory two-job split | **False positive.** There is no hard OIDC requirement mandating separate gate/publish jobs. The two-job pattern is a best practice, not a platform constraint.                                                              |
| Artifacts from concurrent `buddy.yml` runs corrupt each other                        | **False positive.** GitHub artifact storage is scoped per workflow run; concurrent runs operate in completely separate namespaces.                                                                                          |

---

## Document Quality

The design document is written almost entirely in Chinese. Per the repository's `AGENTS.md`:

> "You must use ENGLISH rather than CHINESE for all code, comments, commit messages, documentation in this repository."

The document must be translated to English before it can be committed to the codebase.

---

## Summary Table

| ID  | Finding                                                                                              | Severity       |
| --- | ---------------------------------------------------------------------------------------------------- | -------------- |
| B-1 | Dynamic `uses:` dispatch is a GitHub Actions platform error — impossible to implement                | **Blocking**   |
| B-2 | buddy→official promotion path is mechanically broken (tag already exists at official trigger moment) | **Blocking**   |
| H-1 | Official publish matrix has no idempotency / recovery path after partial failure                     | High           |
| H-2 | No tag protection ruleset for `release/*/v*` — any write-access user can trigger production          | High           |
| H-3 | `publish-unofficial` missing `needs: build-and-pack`                                                 | High           |
| H-4 | No `permissions:` model documented for any workflow                                                  | High           |
| H-5 | `_publish-target.yml` monolithic design (SRP violation, will become unmaintainable)                  | High           |
| H-6 | `environment: production` framed as optional recommendation, not a hard prerequisite                 | High           |
| M-1 | CI path filter blind spots (mise.toml, hk.pkl, biome.jsonc, eng/scripts/, etc.)                      | Medium         |
| M-2 | Skipped-all pattern will block PRs when required status checks are configured                        | Medium         |
| M-3 | No concurrency groups — race conditions on simultaneous buddy triggers                               | Medium         |
| M-4 | Dynamic matrix output has no schema validation or allowlist                                          | Medium         |
| M-5 | Reusable workflow secret inheritance not addressed                                                   | Medium         |
| M-6 | Third-party actions not SHA-pinned (supply chain risk)                                               | Medium         |
| G-1 | `official.yml` has no defined trigger mechanism (no promote workflow specified)                      | Gap            |
| G-2 | `_publish-target.yml` interface (inputs/secrets/outputs) is entirely undefined                       | Gap            |
| G-3 | NBGV multi-project version resolution behavior not addressed                                         | Gap            |
| G-4 | Language-level CI path filtering does not scale past ~10 projects per language                       | Gap            |
| Doc | Design document is in Chinese, violating AGENTS.md                                                   | Administrative |
