# AzureAuth Product Records and Acceptance

## Audience and Use

This is the project record interface for authors and reviewers of the unified
Azure DevOps credential provider. The C# application and
[Python backend and shim](../python/README.md) are components of one product.
The existing document root owns their shared product knowledge.

Repository work authorization and work carriers follow the
[repository record policy](../../../../../docs/governance/record-system.md)
and the accepted [Delivery Wave](../../../../../docs/delivery-wave.md).
The product gates below are additional prerequisites, not work grants. A phase
number, an old implementation permission, or an internal validation result does
not authorize implementation, authentication experiments, or publication.

## Product and Contract Records

| Concern                                                   | Record and use                                                                                                             |
| --------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- |
| Product boundary and required behavior                    | [Requirements](requirements.md), including ecosystem scope and explicit deferrals.                                         |
| Shared core and host boundaries                           | [High-level design](high-level-design.md) and [mid-level design](mid-level-design.md).                                     |
| Host-tool source analysis                                 | [Research](research.md), with its declared source and experiment limitations.                                              |
| Product acceptance, release train, integrity, and signing | [Phase 0 decisions](phase-0-decisions.md).                                                                                 |
| V1 contracts and the helper protocol                      | [Contract freeze](phase-2-contract-freeze.md); preserve the separate credential-contract and `keyring-helper-v2` versions. |
| V2 acquisition requests                                   | [Acquisition contract V2](phase-v5b-acquisition-contract-v2.md); this does not replace the V1 wire contract.               |
| Provider configuration, binding, and persistence          | [WP2](phase-wp2-artifact-trust-enrollment.md).                                                                             |
| AzureAuth process and platform behavior                   | [WP3](phase-wp3-azureauth-process-provider.md) and [production composition](phase-wp6-production-composition.md).          |
| Credential forms and exchange                             | [Token materialization](phase-wp4-token-materialization.md).                                                               |
| Azure Pipelines tokens and PAT deferral                   | [WP5](phase-wp5-azure-pipelines-system-access-token.md).                                                                   |
| npm, pnpm, and Yarn ownership and lifecycle               | [WP7](phase-wp7-registry-credential-lifecycle.md).                                                                         |
| Internal deployment and its evidence limits               | [Deployment validation bundle](phase-wp16-deployment-validation-bundle.md).                                                |

These records retain their declared evidence levels. The requirements and design
baselines do not turn every described platform or contract option into released
support. Read explicit scope decisions and recorded supersession alongside them;
implementation alone does not resolve a conflicting product claim.

## Domain Gates

A mandatory evidence-gate failure stops dependent work until the accountable
owner records an explicit re-scope decision. The
[Phase 0 gate rules and decision fields](phase-0-decisions.md#gate-governance)
retain the product's evidence and acceptance requirements.

| Concern                                | Gate, evidence, and current decision to consult                                                                                                                                                                                                                                                                                 |
| -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| NuGet protocol and packaging           | [Phase 1.1](phase-1.1-nuget-evidence.md), [V1 contracts](phase-2-contract-freeze.md), and the [NuGet requirements](requirements.md#nuget). The current MVP netcore boundary does not imply netfx acceptance.                                                                                                                    |
| Python discovery and release packaging | [Phase 1.3](phase-1.3-python-backend-helper-evidence.md), [Python requirements](requirements.md#python), and [deployment boundaries](phase-wp16-deployment-validation-bundle.md#explicit-boundaries). Import and subprocess consumers remain distinct; release packaging requires accepted discovery and distribution evidence. |
| npm, pnpm, and Yarn writes             | [Phase 1.4](phase-1.4-npm-yarn-config-evidence.md) and [WP7](phase-wp7-registry-credential-lifecycle.md). Yarn write support remains required; omitting it needs an approved requirement change. The original conditional gate does not erase the later recorded write evidence.                                                |
| Git installation modes                 | [Phase 1.5 evidence](phase-1.5-git-discovery-evidence.md) and the [Git re-scope](phase-1r-git-discovery-rescope.md). Preserve the distinction between accepted shell discovery and deferred Windows/GUI modes.                                                                                                                  |
| Persistent derived credentials         | [Secure-cache re-scope](phase-1r-secure-cache-rescope.md), including its separate native Linux provider-cache decision. Product-owned persistent derived credentials remain disabled; provider-owned cache behavior is a different concern.                                                                                     |
| Identity provider and flows            | [WP3](phase-wp3-azureauth-process-provider.md), [WP5](phase-wp5-azure-pipelines-system-access-token.md), and [WP6](phase-wp6-production-composition.md). AzureAuth 0.9.5 is the selected path; changing provider or version requires an explicit accepted decision.                                                             |

The [verification matrix](mid-level-design.md#verification-matrix) and
[design-change gates](mid-level-design.md#design-risks-and-versioned-follow-up)
locate the validation basis for shared-core, adapter, configuration, security,
and diagnostic changes. Host-tool shape tests precede real authentication.
The [contract freeze](phase-2-contract-freeze.md) owns versioned contract tests
and compatibility rules; it does not claim every represented identity flow is
implemented.

## Internal Foundation Artifacts

The foundation packaging boundary from Phase 3 remains distinct from the
complete WP16 deployment bundle. Its deterministic, internal, unsigned,
non-release archives contain only Contracts and Platform outputs. They record
SHA-256 file integrity and safe provenance without machine-local paths, and
keep build OS distinct from target RID. The existing
[foundation artifact script](../../../../../eng/scripts/azureauth-credprovider/New-FoundationArtifact.ps1)
and its [validation consumer](../../../../../tests/private/app/azureauth-credprovider/New-FoundationArtifact.Tests.ps1)
retain that boundary. Foundation scaffolds and placement projections do not
establish a final installer or ecosystem-adapter package.

## Release Acceptance

The original Phase 15 and Phase 16 acceptance responsibilities remain product
gates:

- QA and PLATFORM own the release-candidate acceptance matrix: cross-ecosystem
  tests, Windows-first acceptance, secret redaction audit, installer and
  uninstaller validation, paths with spaces, GUI Git, and headless CI. Apply
  the recorded ecosystem re-scopes when determining the supported release
  surface; deterministic or fake-provider rows are not live-platform evidence.
- PL, PLATFORM, and QA own the final checklist: package publishing dry run,
  mandatory package integrity validation, signing or explicit waivers under
  [Phase 0](phase-0-decisions.md#package-signing-policy), support runbooks,
  operational diagnostics, and recorded release approval.

The [Phase 0 release baseline](phase-0-decisions.md#release-train-baseline)
requires one coordinated product train and final acceptance before public
release. The [WP16 bundle](phase-wp16-deployment-validation-bundle.md) is
internal, unsigned, and non-release; it does not close release-installer or
publication gates. Its explicit evidence boundaries remain in that record.

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

These are domain responsibilities used by the retained decisions. They do not
create separate products, independent work-authorization authorities, or an
assignment of people to roles.

## Review Checklist

Use this checklist when reviewing the affected product contracts:

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

The [product boundary and non-goals](requirements.md#product-boundary) and
[security design](mid-level-design.md#security-design) remain the sources for
scope, configuration-write, identity-abstraction, and protocol constraints.

## Earlier Evidence and Decision Lineage

Retain the original phase records at their existing paths because current
contracts, re-scope decisions, and diagnostics refer to their evidence or IDs.
Their dates, commands, observations, limitations, and declared supersession
remain part of that evidence; they are not a current task queue.

- [Phase 1.1](phase-1.1-nuget-evidence.md),
  [Phase 1.3](phase-1.3-python-backend-helper-evidence.md), and
  [Phase 1.4](phase-1.4-npm-yarn-config-evidence.md) support protocol and
  configuration conclusions used by the current contracts and WP5.
- [Phase 1.5](phase-1.5-git-discovery-evidence.md) supports the
  [Git installation re-scope](phase-1r-git-discovery-rescope.md).
- [Phase 1.6](phase-1.6-secure-cache-evidence.md) retains the blocked platform
  evidence used by the [secure-cache re-scope](phase-1r-secure-cache-rescope.md).
- [Phase 1.2](phase-1.2-azureauth-suitability.md),
  [Phase 1A](phase-1a-identity-flow-selection.md), and
  [V5-A](phase-v5a-wsl-azureauth-backend-governance.md) retain earlier identity
  decisions and the V2 contract's design context. Their own supersession notices
  route current behavior to WP2, WP3, WP5, and WP6. They do not reactivate Direct
  MSAL, PAT support, or the earlier optional-backend plan.

The [original execution breakdown][original-breakdown] preserves the phase
names, original exit criteria, and dependency sequence cited by those records.
Git retains that execution history. This interface keeps its existing path so
those consumers can reach the product concerns without acquiring another
planning authority.

[original-breakdown]: https://github.com/hcoona/three/blob/b673ee27aab8553a4ee259bcaca87a197178463c/src/private/app/azureauth-credprovider/docs/project-breakdown.md
