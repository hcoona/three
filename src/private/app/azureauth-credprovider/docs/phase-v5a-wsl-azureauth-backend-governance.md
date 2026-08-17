# Phase V5-A: WSL AzureAuth Backend Governance

Status: **Superseded historical governance record**

Date: **2026-07-20**

The implemented design is documented by
`phase-wp2-artifact-trust-enrollment.md`,
`phase-wp3-azureauth-process-provider.md`, and
`phase-wp6-production-composition.md`. Those later records supersede this
document's direct-MSAL direction and its statement that no AzureAuth runtime
exists. This file remains only as decision history.

Decision ID: **phase-v5a-wsl-azureauth-backend-governance**

Gate name: **V5-A WSL-to-Windows AzureAuth backend governance gate**

Owner: **ID**

## Scope

This record is an additive English design and governance record for a
_potential future optional backend_: WSL-native Linux host tools calling the
Linux credential provider, which in turn calls a Windows-side `AzureAuth.exe`
process through a WSL-to-Windows interop channel.

This record:

- Does **not** change or supersede Phase 1.2's direct-MSAL direction.
- Does **not** add any AzureAuth runtime code.
- Does **not** provide evidence that AzureAuth is safe, supported, or suitable.
- Does **not** constitute acceptance for Windows-native Git for Windows,
  Visual Studio, or NuGet.exe tooling.
- Does **not** convert any current fake Phase 15 acceptance matrix rows into
  live evidence.

## Relationship to Phase 1.2

Phase 1.2 (`phase-1.2-azureauth-suitability.md`) closed with the decision to
use the **direct MSAL path** behind the identity-provider abstraction. That
decision remains authoritative and unchanged.

The optional WSL-to-Windows AzureAuth path described here is a **future
extensibility hypothesis only**. It is placed behind the existing
identity-provider abstraction so that the direct-MSAL MVP path is never
blocked or replaced by this work.

## Architecture Hypothesis

```text
WSL Linux host tool (git, pip, npm, ...)
    ↓  credential-helper / keyring / plugin protocol
Linux credential provider (this product, running in WSL)
    ↓  future process spawn through a validated absolute AzureAuth.exe path
       using a source-proved silent-only invocation surface
Windows AzureAuth.exe (Microsoft-published optional helper)
    ↓  token response on stdout
Linux credential provider parses token, applies redaction and policy
```

This path is **distinct** from:

- Windows-native Git for Windows (Git-Credential-Manager) credential flows.
- Windows Visual Studio authentication.
- Windows NuGet.exe credential provider protocol.

None of those constitute acceptance evidence for this product or for this path.

## Gate Requirements Before This Path Can Ship

The following gates must all pass before any AzureAuth backend code is
considered for enabling or release. None of these gates are currently open or
have evidence:

- `v5a-azureauth-process-isolation`: Process spawn, stdout/stderr discipline,
  stdin isolation, and redaction wrappers confirmed. Status: **Not started**.
- `v5a-azureauth-noninteractive-policy`: Future silent-only invocation surface
  confirmed sufficient for `SilentOnly` through the validated absolute
  executable path. Status: **Not started**.
- `v5a-azureauth-cache-security`: AzureAuth cache behavior on headless Linux
  confirmed safe or isolated from product cache. Status: **Not started**.
- `v5a-azureauth-artifact-verification`: AzureAuth binary integrity and
  provenance verification approach confirmed. Status: **Not started**.
- `v5a-azureauth-installation-channel`: Installation, update, and discovery
  approach confirmed and does not couple product release. Status:
  **Not started**.
- `v5a-azureauth-wsl-interop-evidence`: WSL-to-Windows executable invocation
  confirmed for target WSL and Windows versions. Status: **Not started**.

Until all gates pass, the AzureAuth backend is:

- **Disabled** in production code.
- **Unshippable** in any release artifact.
- **Not validated** by any Phase 15 matrix row.

## Phase 15 Matrix Note

The current Phase 15 acceptance matrix rows reference fake providers and
deterministic local tests only. None of those rows constitute evidence for the
AzureAuth optional backend. The AzureAuth backend gate is tracked separately as
`optional-azureauth-wsl-backend` in the Phase 15 matrix
(see `ReleaseHardeningPhase15VerticalSliceService.cs`), and it must remain in
`DeferredOptionalFeature` status until the gates above pass.

## Referenced Documents

- `phase-1.2-azureauth-suitability.md` — Phase 1.2 direct-MSAL decision (authoritative)
- `phase-1a-identity-flow-selection.md` — MVP identity-flow matrix
- `phase-2-contract-freeze.md` — Frozen v1 contract baseline
- `project-breakdown.md` — Program phase definitions and gate governance
- `phase-v5b-acquisition-contract-v2.md` — V2 acquisition contract introducing AcquisitionMode
