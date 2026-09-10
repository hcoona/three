# Unified Azure DevOps Credential Provider Phase 0 Decisions

Status: **Phase 0 decision baseline**

Date: **2026-06-05**

## Scope

This record closes only the Phase 0 program-definition work package from `project-breakdown.md`. It records baseline program decisions for product naming, MVP acceptance, target platforms, release train, package integrity, signing, tracker location, decision-record format, and gate governance.

This record does not implement source code, freeze adapter contracts, choose identity-flow support, choose AzureAuth versus direct MSAL, lock package layouts, or close technical gates that require source inspection or prototypes.

Read this dated baseline with the [current product records and domain gates](project-breakdown.md).
Later identity and platform decisions retain their explicit scopes; the original
unselected alternatives below do not override those decisions. Generic work
coordination follows the repository policy linked below. The
[original Phase 0 record][original-phase-0] remains recoverable in Git.

## Decision Summary

| Area              | Decision                                                                                                                                            |
| ----------------- | --------------------------------------------------------------------------------------------------------------------------------------------------- |
| Product name      | Use substitution placeholders until the final product name is selected.                                                                             |
| MVP acceptance    | Accept an MVP only when one shared credential product satisfies the listed cross-ecosystem acceptance criteria.                                     |
| Target platforms  | Treat Windows as first-class and keep Linux and macOS as release validation targets, while this Phase 0 group uses local-run-first validation only. |
| Release train     | Ship one coordinated product train, not independent ecosystem products.                                                                             |
| Package integrity | Require artifact integrity evidence before release readiness closes.                                                                                |
| Package signing   | Require signing where approved signing infrastructure exists; record explicit release waivers otherwise.                                            |
| Tracker           | Keep the product decisions here; use repository work carriers for coordination and gate-review evidence.                                            |
| Decision format   | Use the domain gate-decision fields below, with repository policy governing work authorization.                                                     |
| Gate governance   | Mandatory evidence-gate failures stop dependent work and enter Phase 1R.                                                                            |

## Product-Name Placeholder Policy

The final product name and executable names are not selected in Phase 0. Design and planning documents must continue to use substitution placeholders such as `<primary-cli>` and `<helper-name>` when referring to unresolved command names.

Placeholder text is not literal command text. Any example that includes a placeholder must either be clearly illustrative or state the value must be replaced by the final product or helper name.

Machine-facing names remain similarly provisional until the relevant technical gates and packaging decisions close. In particular, `git-credential-<helper-name>` is the required Git helper shape, but `<helper-name>` is not the final helper name.

## MVP Acceptance Criteria

The MVP is acceptable only when all of the following are true:

1. The product remains one Azure DevOps and Azure Artifacts credential provider with one shared credential core.
2. The human-facing CLI supports the accepted MVP surface for login, logout, status, configure, unconfigure, and doctor workflows.
3. Git, NuGet, Python, and npm-compatible integrations satisfy their host-tool discovery and protocol boundaries through thin adapters.
4. Protocol adapters emit only protocol-valid stdout and route human diagnostics away from protocol stdout.
5. Credential acquisition, refresh, cache partitioning, redaction, identity policy, and CI behavior are centralized in the shared core or its approved abstractions.
6. Persistent configuration writes flow through the configuration manager and record ownership metadata for safe removal.
7. Secrets are redacted from stdout, stderr, logs, traces, dry-run output, and error messages.
8. Repository-local credential writes are not performed by default.
9. CI mode is explicit, non-interactive by default, log-safe, and non-persistent unless policy explicitly allows a scoped write.
10. Windows, Linux, and macOS validation for the accepted release target baseline passes before release acceptance; this Phase 0 group only requires applicable local validation in the environment available to the operator.
11. Mandatory Phase 1 evidence gates are closed or Phase 1R records an approved requirement or sequence change.
12. Package integrity evidence is recorded for release artifacts before release readiness closes.
13. Signing evidence or an approved signing waiver is recorded according to the signing policy.

The MVP identity-flow matrix is not selected by this record. Phase 1A must explicitly accept, defer, or remove each candidate identity flow before contract freeze.

## Target Platform Baseline

Windows is the primary first-class platform for design, validation, and release readiness. MVP validation must cover Git for Windows, PowerShell, `.exe` and `.cmd` behavior, paths with spaces, Visual Studio/MSBuild/NuGet.exe where applicable, .NET SDK restore, and Windows secure credential storage.

Linux and macOS are supported developer and CI platforms. MVP validation must cover shell-independent helper discovery where practical, executable permission checks, headless CI behavior, and secure-store availability or fail-closed behavior.

For the current Phase 0 implementation and validation workflow, acceptance is local-run-first. Operators and subagents must run the relevant validation that is available in their local environment and record unavailable remote platform coverage as deferred release evidence, not as a Phase 0 group failure. This does not narrow the product target baseline below.

The release baseline validation matrix is:

| Platform family | MVP baseline                                                                                                                  |
| --------------- | ----------------------------------------------------------------------------------------------------------------------------- |
| Windows         | x64 Windows 11 24H2 developer environment and x64 Windows Server 2022 or 2025 CI/server environment.                          |
| Linux           | Ubuntu 24.04 LTS x64 CI/developer environment.                                                                                |
| macOS           | macOS 15 arm64 developer environment when an arm64 artifact is produced; otherwise macOS 15 x64 validation for x64 artifacts. |

Packaging-specific runtime identifiers, minimum OS versions, and artifact split remain gated technical decisions for later phases.

## Release Train Baseline

The project uses one coordinated release train for the unified product. Ecosystem adapters may have separate package artifacts only where host tools require different package shapes, but they do not become independent credential products or independent release trains.

Release readiness requires the applicable [product gates and release acceptance](project-breakdown.md#release-acceptance). Public release approval cannot occur until Phase 16 closes. Earlier artifacts may be used for internal validation only when their scope, unsupported status, and open gates are explicit.

A release candidate must include the shared core, the human CLI, accepted adapter surfaces, diagnostics, configuration ownership behavior, package integrity evidence, and signing evidence or waivers required by this record.

## Package Integrity Policy

Package integrity validation is mandatory for release readiness. Every release-candidate artifact must have recorded integrity evidence before Phase 16 can close.

At minimum, integrity evidence must include:

1. The artifact identity, version, platform or package ecosystem, and build source.
2. A cryptographic digest such as SHA-256 for each produced artifact.
3. Provenance or build metadata sufficient to trace the artifact to the approved source revision and build job.
4. Verification that installer, wrapper, or bootstrap flows do not consume unverified remote payloads.
5. Release checklist evidence that package-manager-published artifacts match the recorded build outputs.

Stronger provenance, SBOM, reproducible-build, or package-manager-native verification mechanisms may be added in later phases without weakening this baseline.

## Package Signing Policy

Package signing is required for release artifacts when approved signing infrastructure exists for the artifact type and target channel. Signing is especially expected for Windows executables, installers, shims, and any package ecosystem where the project has an approved signing or trusted-publishing path.

If signing infrastructure is unavailable for an artifact type at release time, Phase 16 must record an explicit signing waiver with affected artifacts, reason, risk, compensating integrity evidence, and approval. A signing waiver does not waive package integrity validation.

Prototype, spike, and internal validation artifacts must be clearly marked as non-release artifacts when they are unsigned or lack full release integrity evidence.

## Tracker Location and System of Record

This document remains the product's Phase 0 decision baseline. Durable product
requirements, contracts, decisions, and evidence stay in this project document
root, with [project-breakdown.md](project-breakdown.md) routing their consumers.

The [repository record policy](../../../../../docs/governance/record-system.md)
owns Issue/PR coordination, review carriers, and record lifecycle. The accepted
[Delivery Wave](../../../../../docs/delivery-wave.md) owns positive work
authorization. Gate-review evidence in a work carrier links to the affected
project decision and preserves the domain evidence fields below; neither a
tracker entry nor a product gate result grants additional work.

## Decision-Record Format

Each future gate or scope decision must include these fields:

| Field                      | Required content                                                                                      |
| -------------------------- | ----------------------------------------------------------------------------------------------------- |
| Decision ID                | Stable identifier, such as `phase-1.1-nuget-evidence`.                                                |
| Gate name                  | The product gate name; original phase names remain recoverable through `project-breakdown.md`.        |
| Owner                      | Accountable role, such as PL, ARCH, ID, CONFIG, PLATFORM, or adapter lead.                            |
| Date                       | Decision date.                                                                                        |
| Status                     | Proposed, accepted, rejected, superseded, or blocked.                                                 |
| Evidence links             | Source-inspection notes, prototype results, tests, logs, or external tracker links.                   |
| Decision                   | The accepted outcome, including pass, fail, accept, defer, remove, or re-scope.                       |
| Affected requirements      | Requirements, design sections, or phase exit criteria affected by the decision.                       |
| Follow-up actions          | Required work, owners, and dependency effects.                                                        |
| Implementation may proceed | Whether the domain prerequisite is satisfied; separate accepted Wave authorization is still required. |

Decision records must distinguish evidence-backed conclusions from provisional recommendations.

## Gate Governance

Gates are execution boundaries. A mandatory gate must close before dependent implementation proceeds. Closing a gate requires the owning role to record the decision with evidence in the system of record.

Phase 1 gate classification is:

| Gate                                              | Classification                                            | Required Phase 1 decision                                                                                                                                                                                                                          |
| ------------------------------------------------- | --------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1.1 NuGet evidence gate                           | Mandatory                                                 | Record pass/fail evidence for plugin launch, handshake, authentication message flow, and runtime packaging constraints. Failure enters Phase 1R.                                                                                                   |
| 1.2 AzureAuth suitability gate                    | Optional substrate                                        | Select AzureAuth or direct MSAL. AzureAuth failure does not block shared-core work; continue through the identity-provider abstraction with direct MSAL unless PL and ID explicitly re-scope.                                                      |
| 1.3 Python backend-helper evidence gate           | Mandatory before release packaging lock                   | Record pass/fail evidence for backend discovery, fixed helper invocation, installed layout, and release-packaging expectations. Release packaging cannot lock until those expectations are accepted.                                               |
| 1.4 npm, pnpm, and Yarn configuration update gate | Mandatory for npm/pnpm writes and conditional Yarn writes | Record pass/fail evidence for config resolution and update behavior for user-level and CI temporary scopes. Phases 12 and 13B are blocked until this gate closes. Yarn write failure enters Phase 1R unless the requirement is explicitly changed. |
| 1.5 Git GUI and PATH discovery gate               | Mandatory for supported Git installation modes            | Record pass/fail evidence for Git for Windows, PATH-sensitive shells, and at least one GUI-launched Git scenario. Failure enters Phase 1R or narrows supported installation modes through an explicit decision.                                    |
| 1.6 Secure-cache behavior gate                    | Mandatory before persistent cache behavior locks          | Record pass/fail evidence for platform secure-store behavior, failure modes, and no-plaintext fallback policy on target platforms.                                                                                                                 |
| 1A MVP identity-flow selection                    | Mandatory before contract freeze                          | Explicitly accept, defer, or remove interactive browser, device code, PAT compatibility, service principal, managed identity, workload identity federation, and Azure Pipelines system access token for MVP.                                       |
| 1R Re-scope gate                                  | Conditional                                               | Runs only for failed mandatory evidence or required sequence/scope changes. Record an explicit accept, defer, remove, requirement-change, or sequence-change decision before dependent Phase 2 or later work proceeds.                             |

If a mandatory evidence gate fails, dependent implementation stops and the failure enters Phase 1R. Phase 1R must use the failed evidence, affected requirements, and owner recommendations to record one of these outcomes before dependent Phase 2 or later work proceeds:

1. Accept revised scope.
2. Defer the requirement with explicit release impact.
3. Remove or change the requirement.
4. Change the implementation sequence and define a new gate.

AzureAuth suitability remains an optional substrate gate. If AzureAuth fails suitability, the project continues through the identity-provider abstraction with direct MSAL unless PL and ID explicitly re-scope the program.

Yarn write support remains a product requirement. Until the npm, pnpm, and Yarn configuration update gate closes, Yarn support is limited to read-only diagnostics and explicit unsupported-write messaging. Shipping without Yarn write support requires Phase 1R to record an approved requirement change.

Package integrity and package signing are separate release decisions. Integrity validation is mandatory for release readiness. Signing follows the signing policy in this record and any recorded waivers.

## Open Technical Gates Not Closed by This Record

This record does not close any gate that requires source inspection, protocol prototypes, package-layout prototypes, secure-cache validation, or host-tool discovery experiments. The following remain open until their owning phases record evidence-backed decisions:

- NuGet plugin launch, handshake, authentication message flow, and runtime packaging constraints.
- AzureAuth suitability versus direct MSAL.
- Python backend-helper discovery, fixed invocation, installed layout, and release packaging.
- npm, pnpm, and Yarn configuration resolution and write behavior.
- Git for Windows, GUI Git, and PATH-sensitive helper discovery.
- Platform secure-store behavior and no-plaintext fallback policy.
- MVP identity-flow selection.
- Core deployment boundary and final artifact layout.

[original-phase-0]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/private/app/azureauth-credprovider/docs/phase-0-decisions.md
