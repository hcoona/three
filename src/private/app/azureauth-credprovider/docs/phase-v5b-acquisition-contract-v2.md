# Phase V5-B: Versioned Acquisition Contract V2

Status: **Contract surface defined — runtime integration in progress**

Date: **2026-07-20**

Decision ID: **phase-v5b-acquisition-contract-v2**

Gate name: **V5-B Versioned acquisition contract V2**

Owner: **ARCH**

## Scope

This record introduces a separate public v2 credential request root for
acquisition-mode work. The frozen v1 baseline in `phase-2-contract-freeze.md`
remains authoritative and unchanged for all `contractMajor: 1` behavior.

## Contract roots

### V1 root

- Public type: `CredentialRequest`
- `contractMajor`: `1`
- Contract ID: `azureauth-credprovider-credential-contract-v1`
- No `acquisitionMode` field exists on the v1 wire shape.

### V2 root

- Public type: `CredentialRequestV2`
- `contractMajor`: `2`
- Contract ID: `azureauth-credprovider-credential-contract-v2`
- Carries the v1 request fields plus required `acquisitionMode`.
- Keeps `accountHint` and `tenantHint` optional and rejects control characters
  when they are present.

Both roots use ordinary source-generated `System.Text.Json` metadata through
`ContractJson.CreateSerializerOptions()`. Enums write their existing camel-case
string names and reject numeric values. Framework-compatible enum casing,
unknown-property, and duplicate-property behavior is not replaced with a
second JSON implementation. `CredentialRequestV2Json` remains a convenience
facade that applies v2 semantic validation around the same serializer options.

## AcquisitionMode values

- `Unspecified` (`unspecified`) is the enum default and is not a valid v2
  acquisition request. A v2 caller must choose an explicit mode.
- `SilentOnly` (`silentOnly`) forbids active identity acquisition. It is valid
  for `operation: get` with `InteractivePolicy.Never`, including the explicit
  Azure Pipelines policy where a separate service consumes a caller-provided
  opaque `SYSTEM_ACCESSTOKEN`.
- `InteractionAllowed` (`interactionAllowed`) permits explicit human-facing
  browser or device-code flows when `InteractivePolicy` also permits them.

Current WP5 and package-configuration Azure Pipelines paths construct v2
requests with `SilentOnly` before consuming the caller-provided opaque token.

## Compatibility rules

1. V1 remains unchanged and does not gain `acquisitionMode`.
2. V2 is opt-in by its separate type and major.
3. A v2 request must specify `SilentOnly` or `InteractionAllowed`.
4. Non-default acquisition modes are valid only for `get`.
5. `InteractionAllowed` remains limited to explicit human browser or
   device-code flows outside CI.
6. `SilentOnly` never falls back to interaction.
7. Contract evolution is reviewed as normal API/schema design. The runtime no
   longer exposes a separate compatibility-oracle API for hypothetical field
   changes.

## What is not changed

- No silent cache or broker source is introduced.
- No fallback from silent to interactive acquisition is introduced.
- No v1 wire or runtime behavior is changed.
- No Azure Pipelines token-service behavior is changed in this package; its
  integration package must adopt the explicit `SilentOnly` contract mode.

## Referenced documents

- `phase-2-contract-freeze.md`
- `phase-v5a-wsl-azureauth-backend-governance.md`
- `phase-1a-identity-flow-selection.md`
- `phase-1.2-azureauth-suitability.md`
