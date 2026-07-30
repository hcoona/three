# Unified Azure DevOps Credential Provider Project Breakdown

Status: **Draft execution baseline**

## Audience and Use

This document is written for AI agents that plan, implement, review, or validate
work on the unified Azure DevOps credential provider. Treat it as an execution
breakdown, not as a product specification. The source requirements and designs
remain authoritative for product behavior:

- `requirements.md`
- `research.md`
- `high-level-design.md`
- `mid-level-design.md`

Agents must not infer unsupported behavior. When a requirement is gated by source
inspection or prototype evidence, keep the work behind that gate until the gate
explicitly closes.

## Execution Principles

1. Preserve the product boundary: this project provides credential acquisition,
   refresh, configuration, diagnostics, and host-tool adapters. It does not
   implement a package manager, Git transport, feed proxy, Azure DevOps client
   replacement, SSH manager, or CI orchestration system.
2. Keep one shared credential core. Do not duplicate token acquisition, cache
   policy, redaction, or identity-selection logic inside ecosystem adapters.
3. Keep adapters thin. Adapters parse host-tool input, call the shared contracts,
   and emit protocol-valid output only.
4. Route every persistent configuration mutation through the configuration
   manager. Adapters emit declarative change plans; they do not write Git, NuGet,
   Python, npm, Yarn, PATH, or temporary CI configuration directly.
5. Treat protocol stdout as format-constrained. Human diagnostics, banners,
   prompts, and update notices must not appear on protocol stdout.
6. Treat Windows as a first-class platform throughout the sequence, not as a late
   hardening pass.
7. Prefer source inspection and reproducible experiments over assumptions.

## Owner Roles

| Role          | Responsibility                                                                                                                        |
| ------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| PL            | Project lead. Owns program gates, scope decisions, re-scope decisions, and final release acceptance.                                  |
| ARCH          | Architecture lead. Owns contracts, shared-core boundaries, adapter-host boundaries, and technical gate acceptance.                    |
| ID            | Identity and security lead. Owns identity-flow policy, AzureAuth or direct MSAL decisioning, secure-cache policy, and security gates. |
| CONFIG        | Configuration lead. Owns the configuration manager, change-plan semantics, persistent write policy, and removal behavior.             |
| PLATFORM      | Platform and release lead. Owns build, packaging, installer behavior, OS matrix, discovery probes, and release mechanics.             |
| ADAPTER-GIT   | Git ecosystem lead. Owns Git credential helper behavior and Azure Repos Git integration.                                              |
| ADAPTER-NUGET | NuGet ecosystem lead. Owns NuGet plugin behavior and Azure Artifacts NuGet integration.                                               |
| ADAPTER-PY    | Python ecosystem lead. Owns Python keyring backend, helper, shim, and Python tool discovery.                                          |
| ADAPTER-NPM   | npm ecosystem lead. Owns npm, pnpm, and conditional Yarn behavior.                                                                    |
| QA            | Validation lead. Owns cross-ecosystem acceptance, hardening, and release test signoff.                                                |

## Gate Rules for AI Agents

Gates are execution boundaries. If a mandatory gate fails, stop dependent work and
enter the re-scope gate. Do not continue by assuming a favorable outcome.

The system of record for gate decisions is the project tracker selected in Phase 0. Each decision record must include the gate name, owner, date, evidence links,
decision, affected requirements, follow-up actions, and whether implementation
may proceed.

AzureAuth is not a program-wide mandatory gate. If AzureAuth fails suitability,
continue through the identity-provider abstraction with the direct MSAL path
unless the project lead and identity lead explicitly re-scope the program.

Yarn write support is a product requirement. Until the Yarn configuration update
gate closes, Yarn support is limited to read-only diagnostics that report
registry declarations and explicitly state that credential writes are blocked by
an open gate. Shipping without Yarn write support requires Phase 1R to record an
approved requirement change.

Package integrity validation and package signing are separate release decisions.
Integrity validation is mandatory for release readiness. Signing is required only
when Phase 0 selects a signing policy and the required infrastructure is
available.

## Phase Breakdown

| Phase | Owner                    | Work package                                                                                                                                                                                                                                                                                                                                                                                                                                                                                         | Exit criteria                                                                                                                                                                                      |
| ----- | ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0     | PL                       | Program gate definition. Confirm product-name placeholder policy, MVP acceptance criteria, target platforms, release train, package integrity policy, signing policy, tracker location, decision-record format, and gate governance.                                                                                                                                                                                                                                                                 | Tracker records the decisions. No repository planning document is required.                                                                                                                        |
| 1.1   | ADAPTER-NUGET            | NuGet evidence gate. Prototype source-confirmed plugin launch, handshake, authentication message flow, and runtime packaging constraints.                                                                                                                                                                                                                                                                                                                                                            | Pass/fail decision is recorded with evidence. Mandatory failure enters Phase 1R.                                                                                                                   |
| 1.2   | ID                       | AzureAuth suitability gate. Evaluate AzureAuth as an optional identity substrate for required audiences, non-interactive behavior, cache reuse, logging, installation, and adapter isolation.                                                                                                                                                                                                                                                                                                        | Decision selects AzureAuth or direct MSAL path. AzureAuth failure does not block shared-core work.                                                                                                 |
| 1.3   | ADAPTER-PY               | Python backend-helper evidence gate. Prototype backend discovery, fixed external helper invocation, installed layout, and release-package signing or provenance options.                                                                                                                                                                                                                                                                                                                             | Pass/fail decision is recorded. Release packaging cannot lock until invocation and package-distribution expectations are accepted.                                                                 |
| 1.4   | ADAPTER-NPM and CONFIG   | npm, pnpm, and Yarn configuration update gate. Prototype config resolution and update behavior for user-level and CI temporary scopes, including Yarn Berry keys and consumed write targets.                                                                                                                                                                                                                                                                                                         | Pass/fail decision is recorded before Phases 12 and 13 implement credential write plans. Yarn write failure enters Phase 1R unless the requirement is explicitly changed.                          |
| 1.5   | ADAPTER-GIT and PLATFORM | Git GUI and PATH discovery gate. Validate helper discovery through Git for Windows, PATH-sensitive shells, and at least one GUI-launched Git scenario.                                                                                                                                                                                                                                                                                                                                               | Pass/fail decision is recorded. Mandatory failure enters Phase 1R or narrows supported installation modes.                                                                                         |
| 1.6   | ID                       | Secure-cache behavior gate. Verify platform secure-store behavior, failure modes, and no-plaintext fallback policy on target platforms.                                                                                                                                                                                                                                                                                                                                                              | Pass/fail decision is recorded before persistent credential cache behavior is locked.                                                                                                              |
| 1A    | ID and PL                | MVP identity-flow selection. Explicitly accept, defer, or remove interactive browser, device code, PAT compatibility, service principal, managed identity, workload identity federation, and Azure Pipelines system access token for MVP.                                                                                                                                                                                                                                                            | Approved identity-flow matrix exists before contract freeze.                                                                                                                                       |
| 1R    | PL and affected leads    | Re-scope gate for failed mandatory evidence. Use failed spike evidence and affected requirements as inputs.                                                                                                                                                                                                                                                                                                                                                                                          | Revised scope or sequence is accepted in the tracker. Restart requires an explicit accept, defer, or remove decision before Phase 2.                                                               |
| 2     | ARCH                     | Contract freeze. Define credential request, credential result, typed errors, canonical resource identity, cache-key schema, `ConfigurationChangePlan`, `DoctorCheck`, adapter-host result mapping, and the `keyring-helper-v2` request/response protocol.                                                                                                                                                                                                                                            | Versioned contract tests pass with fakes. Compatibility rules are explicit.                                                                                                                        |
| 3     | PLATFORM                 | Foundation platform. Create build and test skeletons, packaging skeleton, cross-platform process and filesystem abstractions, redaction, logging, correlation primitives, and Windows path-with-spaces tests. The packaging skeleton produces deterministic internal, non-release, unsigned foundation archives for the Contracts and Platform outputs only; it records SHA-256 file integrity and safe provenance metadata without machine-local paths and keeps build OS distinct from target RID. | CI produces artifacts on the target OS matrix and runs contract and foundation tests.                                                                                                              |
| 4     | CONFIG                   | Configuration manager. Implement declarative change plans, selector ownership sidecars, dry-run equivalence, conflict handling, scoped persistent writes, and precise removal. Enforce no adapter direct writes.                                                                                                                                                                                                                                                                                     | Golden tests cover Git, NuGet, Python, and npm plan application and removal. Yarn selectors are covered in read-only mode, with conditional write/remove extension points if the Yarn gate passes. |
| 5A    | ARCH and PLATFORM        | Minimal adapter-host scaffold. Implement explicit known entry-point routing, stdout/stderr discipline, and fatal-error mapping against frozen contracts.                                                                                                                                                                                                                                                                                                                                             | Adapter and child-process tests prove protocol stdout discipline and mapped exit behavior.                                                                                                         |
| 6     | ID and ARCH              | Credential core. Implement identity-provider abstraction with a fake provider first, secure-cache adapter, identity-flow policy matrix from Phase 1A, token-exchange boundary, and cache partitioning.                                                                                                                                                                                                                                                                                               | Core tests prove cache partitioning, policy enforcement, redaction, fake-provider behavior, and selected AzureAuth or direct MSAL path readiness.                                                  |
| 5B    | PLATFORM                 | Installer and discovery planning. Define artifact-placement conventions and basic side-effect-free discovery probes. Do not materialize fake installation artifacts or package final ecosystem adapters in this phase.                                                                                                                                                                                                                                                                               | Placement projections and basic discovery behavior are documented and tested. Final installer behavior remains owned by ecosystem phases and Phase 15.                                             |
| 7     | PL and PLATFORM          | CLI shell. Implement command structure, help text, status shell, dry-run rendering, error presentation, and CI mode selection. Do not implement full configure or login orchestration yet.                                                                                                                                                                                                                                                                                                           | Snapshot and golden CLI tests pass.                                                                                                                                                                |
| 8     | ARCH                     | Architecture vertical slice. Connect CLI shell, configuration manager, fake credential core, adapter host, and fake Git protocol path.                                                                                                                                                                                                                                                                                                                                                               | Configure, dry-run, doctor, and unconfigure round trip without real credentials.                                                                                                                   |
| 9     | ADAPTER-GIT              | Git adapter. Implement Git credential helper `get`, `store`, and `erase`; `dev.azure.com` `useHttpPath`; Git discovery doctor checks; and Windows GUI/PATH acceptance.                                                                                                                                                                                                                                                                                                                               | Git protocol, discovery, path-forwarding, and Windows acceptance tests pass.                                                                                                                       |
| 10    | ADAPTER-NUGET            | NuGet adapter. Implement plugin entry point, source-confirmed handshake and authentication protocol, packaging decision from the gate, non-interactive behavior, and NuGet doctor checks.                                                                                                                                                                                                                                                                                                            | NuGet plugin protocol, packaging, non-interactive, and doctor tests pass.                                                                                                                          |
| 11    | ADAPTER-PY               | Python adapter. Implement Python keyring backend, `keyring-helper-v2` fixed external helper, keyring CLI shim, virtual environment, pipx, tox, and uv discovery, configured absolute-path and executable checks, exact no-credential versus hard-failure mapping, and Python doctor checks.                                                                                                                                                                                                          | Import-mode, subprocess-mode, helper contract, configured-path validation, environment discovery, and error-mapping tests pass.                                                                    |
| 12    | ADAPTER-NPM and CONFIG   | npm and pnpm adapter. Implement parser and change-plan generation only after Phase 1.4 closes. The configuration manager applies writes. Support user-level default policy, CI temporary configuration requests, and npm/pnpm doctor checks.                                                                                                                                                                                                                                                         | npm and pnpm parser, change-plan, configuration-manager apply/remove, CI temporary-state, and doctor tests pass.                                                                                   |
| 13A   | ADAPTER-NPM              | Yarn read-only diagnostics. Implement Yarn registry discovery diagnostics, gate-status reporting, and unsupported-write messaging while the Yarn write gate is open.                                                                                                                                                                                                                                                                                                                                 | Doctor reports Yarn registry state and clearly reports that writes are blocked until Phase 1.4 closes.                                                                                             |
| 13B   | ADAPTER-NPM and CONFIG   | Yarn write support. Only after Phase 1.4 passes, implement exact Yarn change-plan emission, configuration-manager application and removal metadata, and validation.                                                                                                                                                                                                                                                                                                                                  | Yarn write support passes validated apply/remove tests. If Phase 1.4 fails, this phase is replaced by Phase 1R scope decision.                                                                     |
| 14.1  | PL and ID                | Authentication orchestration. Implement login, logout, account selection, identity-flow UX, and CI identity bootstrap using the Phase 1A matrix.                                                                                                                                                                                                                                                                                                                                                     | Auth command flows pass with fake provider and selected real integration targets.                                                                                                                  |
| 14.2  | PL and CONFIG            | Configuration orchestration. Implement configure and unconfigure flows across ecosystems through configuration-manager plans.                                                                                                                                                                                                                                                                                                                                                                        | Configure/unconfigure flows pass for Git, NuGet, Python, npm/pnpm, and Yarn when enabled.                                                                                                          |
| 14.3  | PL and QA                | Doctor aggregation and cleanup UX. Implement cross-ecosystem doctor aggregation, remediation output, cleanup commands, and CI guidance.                                                                                                                                                                                                                                                                                                                                                              | Aggregated doctor and cleanup flows pass against fake and selected real integration targets.                                                                                                       |
| 15    | QA and PLATFORM          | End-to-end validation. Run cross-ecosystem tests, Windows-first acceptance, secret redaction audit, installer and uninstaller validation, path-with-spaces tests, GUI Git validation, and headless CI validation.                                                                                                                                                                                                                                                                                    | Release-candidate acceptance matrix passes.                                                                                                                                                        |
| 16    | PL, PLATFORM, and QA     | Release readiness. Run package publishing dry run, mandatory package integrity validation, signing only if Phase 0 requires it, support runbooks, operational diagnostics, and final acceptance checklist.                                                                                                                                                                                                                                                                                           | Final checklist closes and release approval is recorded.                                                                                                                                           |

## Dependency Order

Use this dependency order unless Phase 1R changes scope:

```text
0
  -> 1.1, 1.2, 1.3, 1.4, 1.5, 1.6
      -> 1A
      -> 1R only on mandatory gate failure
  -> 2
  -> 3
  -> 4
  -> 5A
  -> 6
  -> 5B
  -> 7
  -> 8
  -> 9, 10, 11, 12
  -> 13A for Yarn read-only diagnostics
  -> 13B only when Phase 1.4 closes with Yarn write approval
  -> 14.1, 14.2, 14.3
  -> 15
  -> 16
```

Phases 9 through 12 can run in parallel after Phase 8 if their gate dependencies
are closed and their adapter leads coordinate shared contract changes through
ARCH. Phase 13A can run before Yarn writes are approved. Phase 13B is blocked by
Phase 1.4 and cannot be replaced by read-only diagnostics unless Phase 1R records
an approved requirement change.

## Agent Work Rules

When an AI agent takes a phase:

1. Read the authoritative design documents before changing code.
2. Confirm all upstream gates and dependencies are closed.
3. Produce or update tests that prove the phase exit criteria.
4. Keep changes inside the phase boundary unless the project lead approves a
   scope change.
5. Do not move persistent configuration writes into adapters.
6. Do not add direct identity-flow support outside the identity-provider
   abstraction.
7. Do not treat a prototype result as production behavior until the owning lead
   accepts the gate.
8. Report blocked work as blocked rather than implementing speculative fallback
   behavior.

## Review Checklist

Use this checklist for phase reviews:

- The work preserves one shared credential core.
- Protocol adapters remain thin and protocol-stdout safe.
- Persistent configuration writes go through the configuration manager.
- Secret values are redacted in stdout, stderr, logs, traces, dry-run output, and
  errors.
- Cache keys include ecosystem, host, organization, project when relevant, feed
  when relevant, service identity, account, tenant, token audience, and
  credential kind.
- Unsupported hosts return adapter-appropriate no-credential behavior, while CLI
  management commands fail explicitly.
- CI behavior is explicit, non-interactive by default, and non-persistent unless
  policy allows otherwise.
- Windows path, quoting, `.exe`, `.cmd`, Git for Windows, Visual Studio/MSBuild,
  and path-with-spaces behavior are tested before release hardening.

## Out-of-Scope Guardrails

Agents must not add these capabilities as part of this project unless the project
lead re-scopes the program:

- Git remote transport implementation.
- Package manager implementation.
- Azure Artifacts feed hosting or proxying.
- Azure DevOps client replacement.
- SSH key management or Azure Repos SSH authentication.
- Transparent credentials for arbitrary non-Azure registries.
- Repository-local credential writes by default.
- Four independent credential products with separate token acquisition logic.
