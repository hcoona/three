# Refactoring Plan for Release Orchestrator

> **Archived and superseded:** This pre-v3 design is retained only for
> historical context. The legacy `buddy.yml` and `release-buddy.yml` routes are
> retired. Do not use this document to recreate either route; current Buddy
> delivery is owned by Workflow Delivery v3.

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
        channel_profile: { type: string, required: true } # 'official' | 'buddy' | 'custom'
        publish_mode: { type: string, required: true } # 'publish' | 'build-only'
        # node-npm spokes additionally receive (per Step 4 Option a decision):
        #   publish_node_gpr:   { type: boolean } # forwarded from hub routing job
        #   publish_node_npmjs: { type: boolean } # forwarded from hub routing job
        # See Step 4 design decision in §3 for rationale.
    ```

    > **Note:** This is the minimal routing contract from the Hub. Each Spoke also receives
    > language-specific inputs (`version`, `project`, `tag_name`, `target`, `artifact_prefix`,
    > tool version pins, etc.) forwarded by the routing job from `resolve` outputs.
    > The `project_variant` hub output is **NOT** forwarded to spokes: each spoke is
    > inherently single-language; `project_variant` is used only for hub-side `publish_mode`
    > derivation and step-summary diagnostics.
    >
    > **Pending Step 4 (node-npm):** `publish_mode='publish'` is currently binary (fires when
    > _any_ node-npm registry flag is true). The node-npm spoke will need to distinguish
    > GPR-only / npmjs-only / both. Resolve before implementing Step 4 — see the Step 4 design
    > decision note below. Until then, treat `publish_mode` as binary in all spokes.

- **Job Deduplication:** Uses the dynamically injected `target_environment` to trigger native GitHub Environment deployment gates. Only one publish job is needed per Spoke, entirely eliminating `*-with-registry`/`*-no-registry` pairs.

    > **OIDC two-job pattern (mandatory):** GitHub Actions `environment:` simultaneously controls
    > approval gates and the `environment` sub-claim baked into the OIDC token. These cannot be
    > separated on a single job. Every Spoke MUST implement a two-job split:
    >
    > 1. **Gate job** — `environment: ${{ inputs.target_environment }}`: holds the per-channel
    >    human-approval gate; requests no OIDC token (`id-token: write` absent).
    > 2. **Publish job** — `needs: [gate]`, `environment: pypi` (or `npmjs`, `rubygems` —
    >    hardcoded to match the registry's Trusted Publisher registration): the OIDC `environment`
    >    claim must match the registration exactly or the registry hard-rejects the token.
    >    Never assign `target_environment` to a job that requests `id-token: write`.
    >
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

### Step 2: Extract central policy jobs [COMPLETED — breaking validator changes]

- Prepare the Hub structure.
- Define dynamic environment outputs (Official vs Buddy) directly in the Context/Policy resolution jobs.

#### Breaking changes in Step 2

- **Stricter `channel_allowlist` regex.** The pre-Step-2 pattern was `^[a-z0-9_-]+$`; the new pattern is `^[a-z0-9]([a-z0-9]|[_-][a-z0-9])*$`. This rejects consecutive hyphens/underscores, leading/trailing separators, and mixed sequences. Migration:
    - `my--channel` → must be manually renamed to `my-channel` in `channel_allowlist` (the new regex rejects this allowlist entry; direct dispatch input `channel: my--channel` is _also_ rejected by policy — the format check in the `*)` case arm rejects it before the allowlist lookup since `my--channel` violates the consecutive-separator rule; the hub's sed collapse of consecutive dashes is a defensive local-testing path only)
    - `my__channel` → `my_channel` or `my-channel` (consecutive underscores are rejected and are **not** auto-collapsed; rename manually)
    - `-beta` → `beta` (remove leading separator)
    - `alpha-` → `alpha` (remove trailing hyphen separator)
    - `alpha_` → `alpha` (remove trailing underscore separator)
    - `a_-b` → `a-b` (normalize mixed sequence)

    The rationale is to make the allowlist-to-`target_environment` mapping injective: the hub context job collapses consecutive dashes via `sed 's/-{2,}/-/g'`, so `my--channel` and `my-channel` would previously both map to the same `release-my-channel` environment, creating a near-miss collision. Note: consecutive underscores are intentionally **not** collapsed by the sanitization sed pipeline, which is why `my__channel` must be renamed rather than auto-migrated.

- **New reserved channel names: `x-official` and `x-buddy`.** These values are now rejected both as direct `channel:` inputs and as `channel_allowlist` entries. They are reserved as internal sanitization escape slugs used by `resolve-hub-context` to prevent near-miss inputs (e.g., `official-`) from impersonating the `release-official`/`release-buddy` protected environments. Under the old regex (`^[a-z0-9_-]+$`), these names were syntactically valid. Migration: rename to any other slug that satisfies the allowlist regex (e.g., `ext-off`, `ext-official`).

- **New pre-case validation guards on `channel:` input.** Three new guards now reject invalid `channel:` inputs earlier, with targeted error messages. Previously these inputs were all rejected, but via the allowlist/case path with generic messages. The rejection _outcome_ for any valid caller is unchanged; only the error message and exit point differ for edge-case inputs:
    1. Empty `channel:` input: rejected immediately with "Channel must not be empty."
    2. `channel:` containing whitespace (leading, trailing, or internal): rejected with a whitespace-specific message.
    3. `channel:` containing uppercase characters: rejected with an uppercase-specific message (including a suggested lowercase rename).

### Step 3: Create Python spoke workflow

- Migrate `build-python`, `attest-python`, and PyPI publishing.
- Apply dynamic environment variables and standard boolean flags to simplify the Spoke logic.

> **Prerequisite — GitHub Environments pre-creation (OPS REQUIRED BEFORE DEPLOYMENT):**
> Before the Python spoke is deployed, the following GitHub Environments **MUST** be manually
> pre-created in repository **Settings → Environments** with required-reviewer protection rules:
>
> | Environment name   | Required reviewers | Purpose                                       |
> | ------------------ | ------------------ | --------------------------------------------- |
> | `release-official` | Prod team          | Approval gate for official channel spoke jobs |
> | `release-buddy`    | Dev team           | Approval gate for buddy channel spoke jobs    |
>
> **Warning:** GitHub silently auto-creates missing environments with **no protection rules**
> when a workflow first references them via `environment:`. This would cause the human-approval
> gate to be absent on the first real publish run. Do not deploy the spoke without confirming
> the environments exist and have required reviewers configured.
>
> **Merge gate for the Step 3 PR (required before merge):**
>
> - [ ] Verify `release-official` and `release-buddy` already exist in Settings → Environments.
> - [ ] Verify both environments have required-reviewer rules configured.
> - [ ] Confirm `release-resolve.yml`'s `project_kind` output values are a strict subset of
>       the variants handled in `resolve-hub-context`'s `project_variant` mapping
>       (`python` | `node` | `ruby`). Any new language added to `release-resolve.yml` MUST
>       be added to the variant mapping in `resolve-hub-context` in the same PR, or the
>       release workflow will fail the `resolve-hub-context` job on every run.
>       Note: `project_kind='node'` maps to **two** variants (`node-npm` when `is_wxt!=true`,
>       `node-wxt` when `is_wxt==true`). A new language with sub-variants must add
>       corresponding branches to **both** the `project_variant` mapping **and** the
>       `publish_mode` case block in `resolve-hub-context` in the same PR.
>
> Custom channel environments (e.g., `release-staging`) may be created on-demand per team
> policy, but must also have appropriate protection rules before being used in production.

### Step 4: Create Node/WXT spoke workflow

- Migrate PNPM packing, GitHub Packages (GPR) publish, and npmjs mapping.
- Consolidate WXT extensions into the same Spoke or an inherited process to reduce Node pipeline duplication.
- **Design decision (resolved — Option a selected):** `publish_mode='publish'` from the Hub is binary
  (fires when _any_ node-npm registry flag is true). The node-npm spoke must distinguish three
  states: GPR-only, npmjs-only, both. **Selected approach: Option (a)** — pass `publish_node_gpr`
  and `publish_node_npmjs` as additional explicit spoke inputs alongside `publish_mode`. The
  `publish_mode` contract remains `'publish' | 'build-only'` as documented in Section 2.2.
  Option (b) (extending `publish_mode` to encode registry set, e.g. `'publish-gpr'`,
  `'publish-npmjs'`, `'publish-both'`) is rejected: it would require updating the routing contract
  in Section 2.2 and all existing consumers simultaneously.

### Step 5: Create Ruby spoke workflow

- Migrate Ruby gem build, GPR upload, and RubyGems.org publish.
- Adopt the standardized Inputs/Outputs contract.

### Step 6: Wire hub static routing

- Wire the Hub (`release-orchestrate.yml`) to call `.github/workflows/release-orchestrate-python.yml`, etc., via `uses:` blocks equipped with static `if: project_kind == '...'` expressions.
- Clear out the migrated language-specific blocks in the Hub.
- **`project_kind` sourcing:** Hub routing jobs MUST source `project_kind` from
  `needs.resolve.outputs.project_kind` directly. Do NOT source it from
  `needs.resolve-hub-context.outputs` — `project_variant` is hub-internal and is intentionally
  absent from `resolve-hub-context` outputs (see the job comment in `release-orchestrate.yml`).
  The routing `if:` expressions should read:
  - Python: `if: needs.resolve.outputs.project_kind == 'python'`
  - Ruby: `if: needs.resolve.outputs.project_kind == 'ruby'`
  - Node/npm: `if: needs.resolve.outputs.project_kind == 'node' && needs.resolve.outputs.is_wxt != 'true'`
  - Node/WXT: `if: needs.resolve.outputs.project_kind == 'node' && needs.resolve.outputs.is_wxt == 'true'`

  Note: Node projects split into two distinct spokes based on `is_wxt`. Do NOT route all `project_kind == 'node'` projects to a single spoke — WXT and npm have separate build and publish pipelines. The `is_wxt` output from `release-resolve.yml` MUST always be an explicit `'true'` or `'false'` string for every `project_kind == 'node'` run; if `is_wxt` is ever absent or empty, `!= 'true'` will match it and silently route a WXT project to the node-npm spoke.
- **`prepare-release-notes` dependency:** Routing jobs that pass release-notes artifacts to a
  spoke MUST add `prepare-release-notes` to their own `needs:` directly (it is absent from
  `resolve-hub-context/needs:`; see `SYNC[routing-jobs]` comment at the job definition).

### Step 7: Centralize GitHub release finalizer

- Combine all the scattered `release-*-with/-no-registry` jobs from the Hub into a single `create-github-release` job.
- Configure it to download all built artifacts from parallel Spoke runs and attach them globally.

### Step 8: Validate official and buddy channels

- Run integration tests (Dry-Run / Manual Trigger) for both `official` and `buddy` channels.
- Ensure the NBGV version generation, Artifact outputs, and publish guardrails behave identically to the legacy design.
