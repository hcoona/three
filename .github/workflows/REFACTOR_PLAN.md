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
- **Interface:** Adheres to a strict Data Contract (standardized `inputs` and `outputs`).

    ```yaml
    inputs:
        project_path: { type: string, required: true }
        target_environment: { type: string, required: true }
        publish_registry_mode: { type: boolean, required: true }
    ```

- **Job Deduplication:** Uses the dynamically injected `target_environment` to trigger native GitHub Environment deployment gates. Only one publish job is needed per Spoke, entirely eliminating `*-with-registry`/`*-no-registry` pairs.

---

## 3. Implementation Steps

We will execute this refactoring iteratively across 8 steps to minimize risk:

### Step 1: Freeze current workflow baseline [COMPLETED]

- Ensure the current `release-orchestrate.yml` is acting as a stable, testable baseline before structural changes begin.

### Step 2: Extract central policy jobs

- Prepare the Hub structure.
- Define dynamic environment outputs (Official vs Buddy) directly in the Context/Policy resolution jobs.

### Step 3: Create Python spoke workflow

- Migrate `build-python`, `attest-python`, and PyPI publishing.
- Apply dynamic environment variables and standard boolean flags to simplify the Spoke logic.

### Step 4: Create Node/WXT spoke workflow

- Migrate PNPM packing, GitHub Packages (GPR) publish, and npmjs mapping.
- Consolidate WXT extensions into the same Spoke or an inherited process to reduce Node pipeline duplication.

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
