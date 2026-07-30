# WP4 — Token Metadata and Credential Materialization

## Opaque AzureAuth token boundary

AzureAuth is invoked with bounded `--output token`. Apart from the ordinary
single trailing line ending, stdout must contain exactly one non-empty token
with no whitespace or control character. Process output and diagnostics remain
redacted.

The product does not authenticate or establish trust by decoding unsigned JWT
claims. It reads only `exp`, when present, as untrusted expiry metadata needed
for credential lifetime handling. Audience, tenant, issued-at, and not-before
claims are not local security gates. Tenant selection comes from the binding
and the `--tenant` argument.

## Credential-form policy

No credential kind falls back to another kind.

| Ecosystem          | Accepted requested form | Materialized form                                         | Exchange |
| ------------------ | ----------------------- | --------------------------------------------------------- | -------- |
| Git                | `BasicPassword`         | username `AzureDevOps`; access token as password          | No       |
| NuGet              | `NuGetPluginCredential` | username `VssSessionToken`; SPS session token as password | SPS      |
| Python keyring/pip | `BasicPassword`         | username `AzureDevOps`; access token as password          | No       |
| npm/pnpm/Yarn      | `NpmAuthToken`          | token field                                               | No       |
| Other combinations | unsupported             | none                                                      | No       |

## SPS exchange

NuGet session-token exchange retains its bounded HTTPS discovery and POST flow.
The source bearer token is sent only in the Authorization header. SPS validates
that token and its `validTo` response is authoritative for the exchanged
credential; untrusted source `exp` metadata does not constrain the SPS result.

Direct materialization reports source `exp` when available. Exchanged
materialization reports SPS `validTo`. Secret-bearing objects redact
`ToString()` and never expose token contents in diagnostics.
