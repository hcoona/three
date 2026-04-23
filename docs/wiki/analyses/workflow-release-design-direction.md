# Workflow Release Design Direction

## Purpose

This page starts the design phase for workflow-based release support. It does
not define descriptor fields, YAML structure, or executor details yet. It only
frames the top-level architecture choices that should be settled before deeper
design.

## Design Inputs

The signed-off requirements baseline fixes these constraints:

- release participation is descriptor-gated;
- scope covers all public projects plus two named private apps;
- `buddy` and `official` are separate workflow entry points;
- one run may target multiple projects;
- targets are project-declared rather than repo-defaulted;
- package and installer paths may vary by project kind;
- binary production for one variant must stay canonical;
- publication rules, approval rules, rerun rules, and acceptance rules are
  already frozen as business constraints.

## Top-Level Architecture Options

### Option A: Workflow-Centric Orchestration

Put most planning logic directly inside GitHub Actions workflows. The workflow
would discover descriptors, evaluate rules, choose jobs, and call target-
specific publish steps itself.

#### Strengths

- Lowest number of moving parts.
- More of the release logic is visible directly in workflow YAML.

#### Weaknesses

- Harder to keep descriptor evaluation, validation, and planning testable.
- Complex branching logic becomes spread across YAML expressions and shell
  scripts.
- Higher risk that `buddy` and `official` drift apart over time.

### Option B: Planner-Centric Architecture

Use thin GitHub workflow entry points that delegate release planning to a repo-
owned planner layer. That planner reads descriptors, validates requirements,
and computes a normalized release plan. The control plane then orchestrates
execution against that plan through reusable execution units.

#### Strengths

- Keeps business-rule evaluation in one testable place.
- Makes `buddy` and `official` share the same planning model while still
  allowing different approvals and publication behavior.
- Fits descriptor-gated participation and multi-project request handling
  naturally.
- Gives a stable seam between future descriptor evolution and GitHub workflow
  execution details.

#### Weaknesses

- Adds a repo-owned planning component that must be designed and maintained.
- Requires deliberate boundaries between planner output and executor input.

### Option C: Ecosystem-Silo Architecture

Split the design primarily by language ecosystem. A top wrapper would route to
separate C#, Python, JS/TS, and Ruby release paths, each with its own planning
and execution shape.

#### Strengths

- Can align closely with existing ecosystem-specific scripts.
- May feel straightforward for the first few representative projects.

#### Weaknesses

- Works against the signed-off requirement that multiple ecosystems still share
  one release model with common approval, rerun, version, and target rules.
- Higher risk of duplicated lifecycle logic and inconsistent operator
  experience.
- Makes cross-ecosystem multi-project release handling harder.

## Recommended Direction

The recommended top-level design is **Option B: Planner-Centric Architecture**.

At the highest level, the system should have three layers:

1. **Control plane**: GitHub workflow entry points for `buddy` and `official`,
   plus approval and concurrency integration.
2. **Planning layer**: repo-owned logic that loads descriptors, validates the
   request, expands the selected project set, and emits a normalized release
   plan.
3. **Execution layer**: reusable executors for canonical build, packaging, and
   target publication.

This direction best matches the signed-off requirements because the hardest part
of the problem is not "how to run a publish command" but "how to interpret
descriptor-owned release policy consistently across many projects, profiles, and
targets."

## Why This Direction Fits the Current Requirements

- **Descriptor gating** already implies a planning step.
- **Multi-project dispatch** implies request normalization before execution.
- **Project-declared targets** imply a need for per-project plan expansion.
- **Canonical build semantics** imply that execution should consume a prior
  planned build graph rather than letting each target decide its own build.
- **Controlled requirement changes** imply we should keep business-rule
  interpretation centralized and inspectable.

## Deliberately Deferred to the Next Design Layer

These questions are intentionally left for the next layer of design:

- descriptor file format and schema syntax;
- exact planner output shape;
- exact reusable-workflow and job boundaries;
- exact executor interfaces for NuGet, PyPI, npm, RubyGems, GitHub Release, and
  installer production;
- exact tagging algorithm and approval-job structure.

## Outcome of This Design Step

The architecture discussion has now resolved this page's original top-level
question in favor of a **planner-centric architecture with thin workflow entry
points**.

The detailed architecture-layer decisions have been captured in
[Workflow Release Architecture Model](./workflow-release-architecture-model.md).
