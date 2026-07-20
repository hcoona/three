# Phase V5-B: Versioned Acquisition Contract V2

Status: **Contract surface defined — runtime scaffold only**

Date: **2026-07-20**

Decision ID: **phase-v5b-acquisition-contract-v2**

Gate name: **V5-B Versioned acquisition contract V2**

Owner: **ARCH**

## Scope

This record introduces a separate public v2 credential request root for future
acquisition-mode work. It does **not** change current provider execution,
cache behavior, or the direct-MSAL MVP direction.

This record supersedes the acquisition-mode proposal only for the v2 contract
surface. The frozen v1 baseline in `phase-2-contract-freeze.md` remains
authoritative and unchanged for all `contractMajor: 1` behavior.

## Why v2 instead of v1 additive change

`AcquisitionMode` is a normative security-policy field. A same-major additive
change on the public v1 request would allow a newer producer to emit a field
that older v1 consumers silently ignore. That is not acceptable for this
contract family. Therefore the acquisition-mode surface must live on a new root
with `contractMajor: 2`.

## Contract Roots

### V1 root (unchanged)

- Public type: `CredentialRequest`
- `contractMajor`: `1`
- Contract ID: `azureauth-credprovider-credential-contract-v1`
- No `acquisitionMode` field exists on the v1 wire shape.
- Current `CredentialCoreService` and provider/cache scaffold continue to accept
  only this v1 root in work-package-1.

### V2 root (new scaffold)

- Public type: `CredentialRequestV2`
- `contractMajor`: `2`
- Contract ID: `azureauth-credprovider-credential-contract-v2`
- Carries the same request fields as v1 plus a **required**
  `acquisitionMode` field.
- Keeps `accountHint` and `tenantHint` optional, but when present they must not
  contain C0 or C1 control characters. The strict v2 facade rejects such values
  on both serialize and deserialize. The frozen v1 root remains unchanged.
- Uses only the dedicated strict public JSON facade
  (`CredentialRequestV2Json.Serialize(...)` / `CredentialRequestV2Json.Deserialize(...)`).
  Direct generic `System.Text.Json.JsonSerializer.Serialize(...)` /
  `Deserialize<CredentialRequestV2>(...)` is intentionally unsupported and
  throws. The source-generated serializer context is an internal implementation
  detail.
- Is a contract surface and validation scaffold only in work-package-1. It is
  **not** routed through `CredentialCoreService`, providers, or cache code yet.

## AcquisitionMode values

- `Unspecified` (`unspecified`): Explicit bridge value preserving the full
  frozen v1 request shape and operation surface, not only the MVP-accepted
  subset. Interaction behavior still comes from `IdentityFlow` plus
  `InteractivePolicy`, so states such as
  `futurePersistentCacheRequested` and non-`get` operations remain
  representable here even though current v1 runtime behavior still fail-closes
  some of them.
- `SilentOnly` (`silentOnly`): Categorically forbids active interaction.
  Non-default acquisition modes are valid only on `operation: get`. In
  work-package-1 this mode still has **no usable current acquisition source**
  and must fail closed for every current `get` flow.
- `InteractionAllowed` (`interactionAllowed`): Contract-compatible only for
  explicit human `get` requests using browser/device-code flows when
  `InteractivePolicy` is `HostToolAllows` or `UserAllowed`. No fallback is
  introduced.

## Compatibility rules

1. **V1 stays exact.** `CredentialRequest` remains the public v1 root with no
   `acquisitionMode` member, no v1 JSON emission of that field, and no v1 core
   routing changes. Protocol-valid frozen v1 states, including the full frozen
   operation surface (`get`, `store`, `erase`, `refresh`, `configure`,
   `doctor`), stay distinct from MVP acceptance.
2. **V2 is opt-in by major.** A caller that wants acquisition-mode semantics
   must use `CredentialRequestV2` with `contractMajor: 2`.
3. **Older v1 consumers do not silently honor v2 policy.** A v2 payload is
   separated by type and major version, so the current core rejects it rather
   than treating `acquisitionMode` as an ignorable v1 extra field. Same-major
   additive-field compatibility remains limited to genuinely ignorable optional
   additions on the v1 root and must still reject any proposed name that is not
   an auditable ASCII identifier (ASCII letters and digits with a letter
   first), that collides after ASCII case normalization with the frozen v1 wire
   members (`contractMajor`, `ecosystem`, `operation`, `resource`,
   `serviceIdentity`, `accountHint`, `tenantHint`, `requestedAudience`,
   `credentialKind`, `identityFlow`, `interactivePolicy`, `cachePolicy`,
   `ciContext`, `extensionData`) and normative or security-policy fields such
   as `acquisitionMode`. The context-free
   `RequiresMajorVersionChange("add-optional-field")` check fails closed
   because it cannot know the root or field name; callers must use explicit
   root/major plus field-aware compatibility review.
4. **Strict v2 JSON and additive-field policy.** Only
   `CredentialRequestV2Json.Serialize(...)` and
   `CredentialRequestV2Json.Deserialize(...)` are supported public JSON entry
   points for the v2 root. Direct generic `System.Text.Json` on
   `CredentialRequestV2` is intentionally unsupported and throws. This strict
   path rejects unknown or misspelled properties, property-name case aliases,
   duplicate properties, unknown enum values, numeric enum values, and wrong
   or missing `contractMajor`. Because the v2 root rejects unknown members, it
   also rejects all same-major additive fields.
5. **Non-default modes are get-only.** `SilentOnly` and
   `InteractionAllowed` are acquisition-specific modes and are rejected for
   `store`, `erase`, `refresh`, `configure`, and `doctor`.
6. **`SilentOnly` fails closed in WP1.** Even on `get`, browser, device code,
   PAT compatibility, CI/system-access-token, and every other current identity
   flow remain incompatible because no source-proved silent cache/broker
   capability exists yet.
7. **`InteractionAllowed` is narrow.** Only explicit human `get` requests using
   `interactiveBrowser` or `deviceCode` with `hostToolAllows` or `userAllowed`
   are contract-compatible. CI-mode and non-human flows are not. This check
   does **not** collapse `cachePolicy` to the MVP-accepted subset; frozen typed
   cache states such as `futurePersistentCacheRequested` remain representable
   and may fail closed later when runtime wiring exists.
8. **No fallback.** Neither `SilentOnly` nor `InteractionAllowed` creates an
   implicit retry chain or silent downgrade.
9. **No runtime routing in WP1.** Even contract-compatible v2 requests remain a
   scaffold only until later work wires a source-proved runtime path.

## What Is NOT Changed

- No AzureAuth runtime or broker/cache silent path is added here.
- No current provider is marked `SilentOnly`-compatible.
- No fallback from `SilentOnly` to interactive acquisition is introduced.
- No v2 request reaches the current core/provider/cache pipeline.
- The Phase 1.2 direct-MSAL decision remains authoritative.

## Referenced Documents

- `phase-2-contract-freeze.md` — v1 frozen baseline (authoritative for v1)
- `phase-v5a-wsl-azureauth-backend-governance.md` — Optional AzureAuth governance
- `phase-1a-identity-flow-selection.md` — Identity-flow matrix
- `phase-1.2-azureauth-suitability.md` — Direct-MSAL decision
