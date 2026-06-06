# Phase 1A MVP Identity-Flow Selection

Status: **Accepted**

Date: **2026-06-06**

Decision ID: **phase-1a-identity-flow-selection**

Gate name: **Phase 1A MVP identity-flow selection**

Owner: **PL with ID**

## Scope

This record closes the Phase 1A identity-flow selection gate for the MVP. It
selects the identity flows that Phase 2 contracts, Phase 6 credential-core work,
and Phase 14.1 authentication orchestration may implement or expose for MVP.

This record does not implement source code, choose final command names, add
service-principal support, add managed-identity support, add workload identity
federation support, or approve any product-owned persistent derived credential
cache.

## Decision Summary

| Field                      | Decision                                                                                                                                                                                                                                          |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Evidence links             | `phase-0-decisions.md`, `project-breakdown.md`, `phase-1.2-azureauth-suitability.md`, `phase-1.6-secure-cache-evidence.md`, `phase-1r-secure-cache-rescope.md`, `requirements.md`, `high-level-design.md`, and `mid-level-design.md`.             |
| Decision                   | Accept interactive browser, device code, narrow explicit PAT compatibility, and Azure Pipelines system access token for MVP. Defer service principal, managed identity, and workload identity federation while preserving contract extensibility. |
| Secure-cache position      | All accepted flows must respect the Phase 1R secure-cache re-scope: no product-owned persistent derived credential cache by default, no plaintext fallback, and no silent persistence.                                                            |
| Implementation may proceed | Yes. Phase 2, Phase 6, and Phase 14.1 may proceed only with the accepted MVP matrix and must treat deferred flows as future extensibility, not MVP support.                                                                                       |

## MVP Identity-Flow Matrix

| Identity flow                       | MVP decision                       | Required MVP behavior                                                                                                                                                                                                                    |
| ----------------------------------- | ---------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Interactive browser                 | **Accepted**                       | Supported for interactive developer commands when host-tool policy permits interaction. Must use the selected identity provider behind the abstraction and must not appear on protocol stdout except through host-tool-approved prompts. |
| Device code                         | **Accepted**                       | Supported for interactive developer commands and constrained environments where browser interaction is unavailable or user-selected. Must be explicit and must not run when the host protocol or non-interactive policy forbids prompts. |
| PAT compatibility                   | **Accepted narrowly; opt-in only** | Explicit compatibility path only. The product must not mint PATs, must not store PATs as product-owned persistent derived credentials, and must never silently fall back to PATs from failed Microsoft Entra flows.                      |
| Service principal                   | **Deferred**                       | Preserve request/result and policy extensibility, but do not expose or claim service-principal MVP support.                                                                                                                              |
| Managed identity                    | **Deferred**                       | Preserve request/result and policy extensibility, but do not expose or claim managed-identity MVP support.                                                                                                                               |
| Workload identity federation        | **Deferred**                       | Preserve contract shape for future secretless CI, but do not expose or claim workload identity federation MVP support.                                                                                                                   |
| Azure Pipelines system access token | **Accepted**                       | Explicit non-persistent Azure Pipelines CI path only. Use the system token only when the command is in explicit CI mode and the token is provided by the pipeline environment for the requested Azure DevOps resource.                   |

Deferred flows are not MVP support. Documentation, help text, `doctor`, and
release statements must not describe service principal, managed identity, or
workload identity federation as supported until a later accepted decision and
implementation evidence add them.

## Rationale

Interactive browser and device code are accepted because they satisfy the MVP
developer-login needs identified in the requirements and design baseline while
remaining compatible with a direct MSAL identity provider behind the shared-core
abstraction selected by Phase 1.2.

PAT compatibility is accepted only as a narrow escape hatch for existing
workflows. The product requirements prefer short-lived or identity-derived
credentials, and the mid-level design forbids silent PAT fallback. Therefore PAT
compatibility must be explicitly selected, must be visible in policy and
diagnostics, and must not create or manage PAT lifecycle.

Azure Pipelines system access token is accepted because it provides an MVP CI
path without requiring new long-lived secrets. It is accepted only as explicit,
non-persistent CI behavior. Absence of the pipeline token must fail closed with
redacted diagnostics rather than falling back to interactive auth, desktop cache
discovery, device code, or PAT.

Service principal, managed identity, and workload identity federation remain
important future flows, especially for secretless automation. They are deferred
because Phase 1A MVP support is limited to flows approved by the
human-in-the-loop matrix. Contracts must keep room for these flows so later
phases can add them without adapter rewrites.

## Security and Cache Implications

1. Accepted flows must inherit the Phase 1R secure-cache re-scope:
   product-owned persistent derived host-tool credentials are non-MVP and
   disabled by default.
2. No accepted flow may silently persist credential material as plaintext when a
   secure store is unavailable, locked, denied, unsupported, or unverified.
3. Identity-provider or MSAL cache behavior is separate from product-owned
   derived credential caching. It is allowed only when policy-compliant for the
   selected provider, platform, and mode.
4. PAT compatibility must never mint, renew, rotate, or store PATs. It may only
   consume explicitly supplied PAT material through an opt-in compatibility path
   and log-safe, redacted handling.
5. Azure Pipelines system access token support must be non-persistent by
   default. It may flow through protocol responses or approved temporary CI
   configuration only when the relevant phase records the target, scope,
   credential kind, ownership metadata, cleanup behavior, and redaction tests.
6. Protocol adapters must continue to emit only protocol-valid stdout. Human
   diagnostics, flow-selection guidance, and CI warnings must use diagnostic
   channels outside protocol stdout.

## Affected Requirements and Designs

| Source                               | Impact                                                                                                                                                                                                                                                  |
| ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `phase-0-decisions.md`               | Closes the mandatory Phase 1A identity-flow selection gate before contract freeze. The MVP acceptance criteria remain unchanged.                                                                                                                        |
| `project-breakdown.md`               | Phase 2, Phase 6, and Phase 14.1 may proceed only with this matrix. Direct identity-flow support must remain behind the identity-provider abstraction.                                                                                                  |
| `phase-1.2-azureauth-suitability.md` | The direct MSAL path remains the MVP identity-provider direction. AzureAuth remains optional future helper evidence only and is not required for any accepted MVP flow.                                                                                 |
| `phase-1.6-secure-cache-evidence.md` | The no-plaintext fallback policy applies to every accepted identity flow. This record does not unlock persistent secure-cache defaults.                                                                                                                 |
| `phase-1r-secure-cache-rescope.md`   | Identity-flow contracts and implementation must model non-persistent product-owned derived credentials by default. Accepted flows cannot reintroduce default product-owned persistent derived credential cache behavior.                                |
| `requirements.md`                    | Resolves the open MVP identity-flow question by accepting interactive browser, device code, explicit PAT compatibility, and Azure Pipelines system access token while deferring service identity flows.                                                 |
| `high-level-design.md`               | Keeps identity acquisition in the shared core and preserves future non-interactive service identity hooks without claiming MVP support.                                                                                                                 |
| `mid-level-design.md`                | Refines the identity-flow policy table: accepted MVP interactive developer flows are browser and device code; accepted MVP CI flow is Azure Pipelines system access token; PAT remains explicit opt-in only; other CI identity candidates are deferred. |

## Follow-Up Actions

| Required work                                                                                         | Owner persona(s) | Dependency effect                                                                                                                                                        | Target phase |
| ----------------------------------------------------------------------------------------------------- | ---------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------ |
| Freeze request, policy, result, and typed-error contracts for accepted and deferred flow states.      | ARCH, ID, PL     | Phase 2 may close only if contracts distinguish supported, deferred, disabled, unavailable, interaction-blocked, and explicit PAT-compatibility outcomes.                | Phase 2      |
| Add fake-provider tests for the accepted matrix and negative tests for silent fallback.               | ID, ARCH, QA     | Phase 6 may close only when accepted flows route through the identity-provider abstraction and unsupported/deferred flows fail closed without adapter-specific bypasses. | Phase 6      |
| Define explicit CLI and CI UX for browser, device code, PAT compatibility, and Azure Pipelines token. | PL, ID           | Phase 14.1 may expose only accepted flows and must render deferred flows as unsupported or future, not partially available.                                              | Phase 14.1   |
| Preserve future contract shape for service principal, managed identity, and workload federation.      | ARCH, ID         | Future support can be added without changing adapter protocols, but MVP adapters must not call these paths.                                                              | Phase 2+     |
| Carry the non-persistence and no-plaintext policy into docs, help text, doctor, and release notes.    | PL, ID, QA       | Phase 15 and Phase 16 must block claims that MVP stores derived credentials by default or supports deferred flows.                                                       | Phases 15-16 |

## Acceptance Criteria

### Phase 2 Contract Freeze

Phase 2 may close only when these pass/fail artifacts are linked from Phase 2
evidence:

- [ ] Credential request contracts can express `interactive-browser`,
      `device-code`, `pat-compatibility`, and `azure-pipelines-system-token` as
      accepted MVP flow selections.
- [ ] Contracts can express `service-principal`, `managed-identity`, and
      `workload-identity-federation` as deferred or unsupported without changing
      adapter protocols later.
- [ ] Result and error contracts distinguish `InteractionRequired`,
      `InteractionBlocked`, `CredentialUnavailable`, `FlowDeferred`,
      `FlowDisabled`, `UnsupportedFlow`, `CacheUnavailable`, and explicit
      PAT-compatibility policy failures or equivalent typed outcomes.
- [ ] Cache-policy contracts represent no-cache, cache-disabled,
      cache-unavailable, non-persistent CI, and future persistent-cache extension
      states without requiring product-owned persistent derived credential
      writes.
- [ ] Contract tests prove adapters cannot silently fall back from Microsoft
      Entra flows to PAT, device code, desktop cache discovery, or Azure
      Pipelines token.

### Phase 6 Credential Core

Phase 6 may close only when these pass/fail artifacts are linked from Phase 6
evidence:

- [ ] Fake-provider tests cover successful interactive browser, device code,
      explicit PAT compatibility, and Azure Pipelines system access token
      outcomes.
- [ ] Negative tests prove service principal, managed identity, and workload
      identity federation return explicit deferred or unsupported outcomes in
      MVP builds.
- [ ] CI tests prove Azure Pipelines system access token behavior is explicit,
      non-interactive, non-persistent by default, log-safe, and fail-closed when
      the token or required Azure Pipelines context is absent.
- [ ] PAT tests prove PAT material is never minted, stored, logged, used as a
      silent fallback, or accepted without an explicit compatibility selection.
- [ ] Cache and filesystem/configuration snapshots prove default accepted flows
      do not create product-owned persistent derived credential cache entries or
      plaintext fallback files.
- [ ] Direct MSAL readiness evidence is separate from product-owned derived
      credential cache evidence and cannot rely on an unprotected fallback to
      satisfy product policy.

### Phase 14.1 Authentication Orchestration

Phase 14.1 may close only when these pass/fail artifacts are linked from Phase
14.1 evidence:

- [ ] CLI login and account-selection flows support interactive browser and
      device code with safe prompt, timeout, cancellation, and redacted error
      behavior.
- [ ] Authentication UX exposes PAT compatibility only through an explicit
      opt-in command or option that states the risk and non-persistence policy.
- [ ] CI bootstrap supports Azure Pipelines system access token only in explicit
      CI mode and never persists it by default.
- [ ] Help text, `doctor`, status, and error messages report service principal,
      managed identity, and workload identity federation as deferred or
      unsupported for MVP.
- [ ] Protocol adapters receive only structured credential-core outcomes and do
      not implement identity-flow selection or fallback locally.
- [ ] End-to-end fake-provider flows prove accepted authentication paths produce
      protocol-safe results for dependent adapter scenarios without diagnostics
      on protocol stdout.

## Residual Risks

- MVP CI support is intentionally narrow. Non-Azure-Pipelines CI systems will
  need future service principal, managed identity, workload identity federation,
  or another approved non-interactive flow.
- PAT compatibility may be misunderstood as full PAT lifecycle support. CLI,
  docs, and diagnostics must consistently state that the product does not mint
  or store PATs.
- Non-persistent derived credentials may cause more frequent authentication or
  token exchange. Phase 6 and Phase 14.1 must handle this without adding
  undocumented persistence.

## Validation

| Command                                                                                                       | Exit status |
| ------------------------------------------------------------------------------------------------------------- | ----------- |
| `pnpm exec prettier --write src/private/app/azureauth-credprovider/docs/phase-1a-identity-flow-selection.md`  | 0           |
| `pnpm exec prettier --check src/private/app/azureauth-credprovider/docs/phase-1a-identity-flow-selection.md`  | 0           |
| `pnpm exec markdownlint-cli2 src/private/app/azureauth-credprovider/docs/phase-1a-identity-flow-selection.md` | 0           |
