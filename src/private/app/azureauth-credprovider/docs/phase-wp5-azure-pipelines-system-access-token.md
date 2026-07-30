# WP5 — Opaque Azure Pipelines Credential and PAT Deferral

## Boundary

WP5 adds policy and materialization for a caller-provided Azure Pipelines
`SYSTEM_ACCESSTOKEN`. It does not compose the provider into every production
adapter, add registry refresh, add WSL artifacts, mutate a live runner, or claim
live acceptance. Frozen v1 wire shapes and enum values remain unchanged.

## Source model

`AzurePipelinesSystemAccessToken` is an opaque, secret-bearing type. It is not
`IdentityMaterial`, an AzureAuth/Entra acquired JWT, an account or tenant
identity, or PAT compatibility. The implementation does not parse JWT segments
or claims. Input must be nonblank, at most 16 KiB, and contain no control
characters. Its value and all materialized password/bearer fields redact from
`ToString()` and errors.

The token has unknown expiry and is bounded by the Azure Pipelines job. Results
therefore report `JobScopedUnknownExpiry`, no fabricated UTC expiry, no account,
no tenant, and no cache key. The service has no provider, exchange, cache, or
global token state.

## Request matrix

Both v1 and v2 require `Get`, `AzurePipelinesSystemAccessToken`,
`InteractivePolicy.Never`, `NonPersistentCi`, no account/tenant hints, an
explicit CI context, provider exactly `AzurePipelines`, declared token input,
and `AllowsPersistentWrites=false`. The resource, audience, and credential kind
must satisfy the frozen v1 contract.

V1 has no acquisition-mode field. V2 requires explicit
`AcquisitionMode.SilentOnly` for opaque provided input;
`AcquisitionMode.Unspecified` and `InteractionAllowed` are invalid in CI.
Missing or blank input is
`CredentialUnavailable`; it never falls back to interaction, PAT, AzureAuth, or
another identity provider.

## Protocol mapping

Unsupported rows fail before producing secret output. No row exchanges or
reinterprets the input as a PAT.

| Ecosystem             | Frozen requested form   | Protocol material                                                            | Fixed username                        |
| --------------------- | ----------------------- | ---------------------------------------------------------------------------- | ------------------------------------- |
| Git Azure Repos       | `BearerToken`           | Bearer result; the frozen Git adapter converts it to Basic token-as-password | `AzureDevOps` at the adapter boundary |
| NuGet Azure Artifacts | `NuGetPluginCredential` | disabled: no direct opaque-token evidence                                    | none                                  |
| Python keyring/pip    | `BasicPassword`         | disabled: no direct opaque-token evidence                                    | none                                  |
| npm                   | `NpmAuthToken`          | temporary registry `_authToken`                                              | none                                  |
| pnpm                  | `NpmAuthToken`          | temporary registry `_authToken`                                              | none                                  |
| Yarn                  | `NpmAuthToken`          | temporary `npmAuthToken`                                                     | none                                  |

The NuGet evidence documents an SPS exchange that produces
`VssSessionToken`; it does not establish that a caller-provided
`SYSTEM_ACCESSTOKEN` can be relabeled as that session token. The Python evidence
establishes its password protocol, not direct acceptance of this opaque token.
Both mappings fail closed before materialization.

## Temporary lifecycle

npm, pnpm, and Yarn reuse the existing `ConfigurationChangePlan` abstractions:

- `SYSTEM_JOBID` (or the equivalent explicit service option) is required and
  validated as a bounded safe ASCII identifier before CI materialization;
- each job uses `<product-temporary-root>/ci-jobs/<job-id>` (or the explicitly
  supplied equivalent root) for temporary files and ownership manifests;
- scope is `CiTemporary`;
- only token values have `IsSecretValue=true`;
- plans declare credential material and a product-owned temporary container;
- activation redirects npm/pnpm user config or Yarn `HOME`;
- ownership manifests contain selectors and ownership facts, but neither the
  token nor a token-derived hash;
- unconfigure, cleanup, and logout remove only the selected job's generated
  temporary state;
- no user-global manifest, persistent credential cache, or persistent cache key
  receives the token.

WP5 tests exercise the deterministic existing in-memory configuration scaffold.
Production runner composition and live filesystem acceptance belong to later
work.

## PAT compatibility

The frozen `PatCompatibility` enum and v1 wire spelling remain readable.
`PatCompatibilityPolicy` and the central credential core mark new production
acquisition/materialization as `Deferred` with code
`PatCompatibilityDeferred`. CLI status, doctor, login, and release evidence
report it as deferred/disabled, never accepted. The CLI consumes but never
prints an explicit placeholder value. There is no
AzureAuth `ado token`/`ado pat` invocation, environment PAT fallback, PAT cache,
silent conversion, account binding, or invented identity.

## Evidence

- [`phase-2-contract-freeze.md`](phase-2-contract-freeze.md): Git bearer
  adaptation and frozen CI policy.
- [`phase-1.1-nuget-evidence.md`](phase-1.1-nuget-evidence.md): NuGet Basic and
  `VssSessionToken` protocol form.
- [`phase-1.3-python-backend-helper-evidence.md`](phase-1.3-python-backend-helper-evidence.md):
  keyring password/credential protocol.
- [`phase-1.4-npm-yarn-config-evidence.md`](phase-1.4-npm-yarn-config-evidence.md):
  `_authToken`, `npmAuthToken`, activation, and temporary configuration.
- [`phase-wp4-token-materialization.md`](phase-wp4-token-materialization.md):
  accepted direct Azure Artifacts token-as-password forms.
