# Refactoring Plan for Release Orchestrator

## 1. Background

The current `.github/workflows/release-orchestrate.yml` is a monolithic file handling polyglot release pipelines (Python, Node/WXT, Ruby). It has become difficult to maintain due to:

- **Excessive Line Count:** Combining multiple languages into one workflow.
- **Job Duplication:** To bypass GitHub Actions' static `needs:` graph limitations, many jobs are duplicated (e.g., `*-with-registry` vs `*-no-registry`, `*-enabled` vs `*`).
- **Low Extensibility:** Adding a new language (like C# or Rust) requires modifying up to 7 synchronized points, which is highly error-prone.

## 2. Target Architecture (Hub-and-Spoke)

We will adopt a **Hub-and-Spoke model** with **Dynamic Environments**.

### 2.1 The Hub (`release-orchestrate.yml`)

- **Responsibilities:** Acts strictly as the dispatcher and policy engine.
- **Logic:** Handles `resolve`, validates channel policies (`guard`), prepares release notes, and dynamically computes deployment environments.
- **Routing:** Contains static routing jobs (`if: language == 'python'`) to call language-specific Spoke workflows.
- **Finalizer:** Centralizes the final GitHub Release creation by aggregating artifacts from Spokes.

### 2.2 The Spokes (`release-orchestrate-<lang>.yml`)

- **Responsibilities:** Language-specific execution (build, attestation, registry publish).
- **Routing contract** (minimal; spokes additionally receive language-specific inputs from the Hub routing job):

    ```yaml
    inputs:
        package_dir: { type: string, required: true }
        target_environment: { type: string, required: true }
        channel_profile: { type: string, required: true }  # 'official' | 'buddy' | 'custom'
        publish_mode: { type: string, required: true }  # 'publish' | 'build-only'
        # TODO(Step 4): node-npm publish_mode is currently binary — 'publish' fires when *any*
        # node-npm registry flag is true. The node-npm spoke will need GPR-only / npmjs-only /
        # both disambiguation. See the Step 4 design decision note below.
    ```

    > **Note:** This is the minimal routing contract from the Hub. Each Spoke also receives
    > language-specific inputs (`version`, `project`, `tag_name`, `target`, `artifact_prefix`,
    > tool version pins, etc.) forwarded by the routing job from `resolve` outputs.
    > The `project_variant` hub output is **NOT** forwarded to spokes: each spoke is
    > inherently single-language; `project_variant` is used only for hub-side `publish_mode`
    > derivation and step-summary diagnostics.

    > **Pending Step 4 (node-npm):** `publish_mode='publish'` is currently binary (fires when
    > *any* node-npm registry flag is true). The node-npm spoke will need to distinguish
    > GPR-only / npmjs-only / both. Resolve before implementing Step 4 — see the Step 4 design
    > decision note below. Until then, treat `publish_mode` as binary in all spokes.

- **Job Deduplication:** Uses the dynamically injected `target_environment` to trigger native GitHub Environment deployment gates. Only one publish job is needed per Spoke, entirely eliminating `*-with-registry`/`*-no-registry` pairs.

  > **OIDC two-job pattern (mandatory):** GitHub Actions `environment:` simultaneously controls
  > approval gates and the `environment` sub-claim baked into the OIDC token. These cannot be
  > separated on a single job. Every Spoke MUST implement a two-job split:
  > 1. **Gate job** — `environment: ${{ inputs.target_environment }}`: holds the per-channel
  >    human-approval gate; requests no OIDC token (`id-token: write` absent).
  > 2. **Publish job** — `needs: [gate]`, `environment: pypi` (or `npmjs`, `rubygems` —
  >    hardcoded to match the registry's Trusted Publisher registration): the OIDC `environment`
  >    claim must match the registration exactly or the registry hard-rejects the token.
  >    Never assign `target_environment` to a job that requests `id-token: write`.

  > **GPR vs OIDC registries (custom channels):** GPR (GitHub Packages Registry) authenticates
  > via `github.token` — it does not use OIDC and does not require a separate publish environment.
  > `publish_node_gpr=true` or `publish_ruby_gpr=true` on a custom allowlisted channel will set
  > `publish_mode=publish`, causing the spoke to run its gate job and request `target_environment`
  > approval. If the custom environment (e.g. `release-staging`) lacks required-reviewer rules,
  > GitHub auto-creates it with no protection, and the publish proceeds without human approval.
  > This is intentional: GPR is treated as a lower-trust registry for custom channels. If you
  > require approval gates for GPR publishes on custom channels, pre-create the environment with
  > required reviewers in repository Settings → Environments.

---

## 3. Implementation Steps

We will execute this refactoring iteratively across 8 steps to minimize risk:

### Step 1: Freeze current workflow baseline [COMPLETED]

- Ensure the current `release-orchestrate.yml` is acting as a stable, testable baseline before structural changes begin.

### Step 2: Extract central policy jobs [COMPLETED]

- Prepare the Hub structure.
- Define dynamic environment outputs (Official vs Buddy) directly in the Context/Policy resolution jobs.

#### Breaking changes in Step 2

- **Stricter `channel_allowlist` regex.** The pre-Step-2 pattern was `^[a-z0-9_-]+$`; the new pattern is `^[a-z0-9]([a-z0-9]|[_-][a-z0-9])*$`. This rejects consecutive hyphens/underscores, leading/trailing separators, and mixed sequences. Migration:
  - `my--channel` → `my-channel` (collapse consecutive hyphens)
  - `my__channel` → `my-channel` (collapse consecutive underscores)
  - `-beta` → `beta` (remove leading separator)
  - `alpha-` → `alpha` (remove trailing separator)
  - `a_-b` → `a-b` (normalize mixed sequence)

  The rationale is to make the allowlist-to-`target_environment` mapping injective: the hub context job collapses consecutive dashes via `sed 's/-{2,}/-/g'`, so `my--channel` and `my-channel` would previously both map to the same `release-my-channel` environment, creating a near-miss collision.

### Step 3: Create Python spoke workflow

- Migrate `build-python`, `attest-python`, and PyPI publishing.
- Apply dynamic environment variables and standard boolean flags to simplify the Spoke logic.

> **Prerequisite — GitHub Environments pre-creation (OPS REQUIRED BEFORE DEPLOYMENT):**
> Before the Python spoke is deployed, the following GitHub Environments **MUST** be manually
> pre-created in repository **Settings → Environments** with required-reviewer protection rules:
>
> | Environment name   | Required reviewers | Purpose |
> |---|---|---|
> | `release-official` | Prod team          | Approval gate for official channel spoke jobs |
> | `release-buddy`    | Dev team           | Approval gate for buddy channel spoke jobs |
>
> **Warning:** GitHub silently auto-creates missing environments with **no protection rules**
> when a workflow first references them via `environment:`. This would cause the human-approval
> gate to be absent on the first real publish run. Do not deploy the spoke without confirming
> the environments exist and have required reviewers configured.
>
> Custom channel environments (e.g., `release-staging`) may be created on-demand per team
> policy, but must also have appropriate protection rules before being used in production.

### Step 4: Create Node/WXT spoke workflow

- Migrate PNPM packing, GitHub Packages (GPR) publish, and npmjs mapping.
- Consolidate WXT extensions into the same Spoke or an inherited process to reduce Node pipeline duplication.
- **Design decision required before this step:** `publish_mode='publish'` from the Hub is binary
  (fires when *any* node-npm registry flag is true). The node-npm spoke must distinguish three
  states: GPR-only, npmjs-only, both. Resolve by either (a) passing `publish_node_gpr` and
  `publish_node_npmjs` as additional explicit spoke inputs alongside `publish_mode` (contract
  stays `'publish' | 'build-only'`), or (b) extending `publish_mode` to encode the registry
  set (e.g. `'publish-gpr'`, `'publish-npmjs'`, `'publish-both'`) — note option (b) requires
  updating the routing contract documentation in Section 2.2 above.

### Step 5: Create Ruby spoke workflow

- Migrate Ruby gem build, GPR upload, and RubyGems.org publish.
- Adopt the standardized Inputs/Outputs contract.

### Step 6: Wire hub static routing

- Wire the Hub (`release-orchestrate.yml`) to call `.github/workflows/release-orchestrate-python.yml`, etc., via `uses:` blocks equipped with static `if: project_kind == '...'` expressions.
- Clear out the migrated language-specific blocks in the Hub.

### Step 7: Centralize GitHub release finalizer

- Combine all the scattered `release-*-with/-no-registry` jobs from the Hub into a single `create-github-release` job.
- Configure it to download all built artifacts from parallel Spoke runs and attach them globally.

### Step 8: Validate official and buddy channels

- Run integration tests (Dry-Run / Manual Trigger) for both `official` and `buddy` channels.
- Ensure the NBGV version generation, Artifact outputs, and publish guardrails behave identically to the legacy design.
