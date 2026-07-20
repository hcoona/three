# WP4 — Token Claim Consistency and Credential Materialization

## Boundary

WP4 validates and materializes tokens obtained by the optional WP3 AzureAuth
process provider. It is not composed into the CLI or protocol adapters yet. It
does not add CI `SYSTEM_ACCESSTOKEN`, PAT compatibility, persistent registry
state, refresh, or live acceptance. Frozen v1 request and wire contracts are
unchanged.

## Claim-consistency validation

An AzureAuth access token is trusted as output from the authorized process, but
its JWT text and claims are untrusted input. `AzureDevOpsJwtClaimConsistencyValidator`
performs **claim-consistency validation, not authentication**. It never validates
the JWT signature and must not be used for tokens from an untrusted transport.

The validator requires:

- exactly three non-empty, unpadded base64url segments;
- decoded header at most 8 KiB and payload at most 64 KiB;
- strict UTF-8 and JSON objects, depth at most 16, without comments, trailing
  commas, or duplicate property names (including nested objects);
- string `aud` exactly `499b84ac-1321-427f-aa17-267ca6975798`;
- string `tid` matching the tenant enforced by the AzureAuth binding;
- integral Unix-second `iat`, `nbf`, and `exp` claims in the platform-supported
  date range;
- coherent dates and a five-minute clock skew for future `iat`/`nbf` and recent
  expiry;
- an `exp` no more than 24 hours after either `iat` or validation time.

Missing, malformed, inconsistent, expired, or excessively future claims fail
closed. Account claims never establish an account binding; WP3 leaves account
unknown because its invocation does not enforce an account. The acquired token
records issued, not-before, expiry, enforced tenant, deployment provenance, and
the claim-consistency method. Its secret remains redacted.

This 24-hour ceiling is deliberately conservative. Microsoft documents a
60–90 minute default Entra access-token lifetime and a configurable maximum of
`23:59:59`; CAE's separate 24–28 hour behavior is not established by the pinned
AzureAuth/Azure DevOps contract used here. The ceiling prevents malformed
year-9999-style claims from becoming long-lived credentials without claiming
signature authentication.

## Credential-form policy

No credential kind falls back to another kind.

| Ecosystem          | Accepted requested form                                       | Materialized form                                         | Exchange |
| ------------------ | ------------------------------------------------------------- | --------------------------------------------------------- | -------- |
| Git                | `BasicPassword`                                               | username `AzureDevOps`; access token as password          | No       |
| Git                | `BearerToken`, other forms                                    | Disabled                                                  | No       |
| NuGet              | `NuGetPluginCredential`                                       | username `VssSessionToken`; SPS session token as password | SPS      |
| Python keyring/pip | `BasicPassword`                                               | username `AzureDevOps`; access token as password          | No       |
| npm                | `NpmAuthToken`                                                | token field                                               | No       |
| pnpm               | `NpmAuthToken`                                                | token field                                               | No       |
| Yarn               | `NpmAuthToken`                                                | token field                                               | No       |
| Any ecosystem      | `PatCompatibility`, CI system token, mismatched form/audience | Disabled                                                  | No       |

This reflects the adapter evidence: Git, NuGet, and Python expose
username/password surfaces; npm/pnpm `.npmrc` and Yarn `npmAuthToken` expose
token surfaces. A disabled decision occurs before token materialization or any
network call. The pinned `VstsCredentialProvider.cs` uses the exact
`VssSessionToken` username for an exchanged SPS token; direct Git and Python
forms retain their established `AzureDevOps` username.

## Evidence-backed SPS exchange

Only NuGet's `NuGetPluginCredential` row may exchange. The implementation follows
the inspected Microsoft artifacts credential provider flow:

1. `GET` the canonical HTTPS Azure Artifacts feed without authorization.
2. Treat an absent `X-VSS-AuthorizationEndpoint` as “exchange not advertised”
   and disable SPS exchange. If the header is present, require exactly one
   syntactically valid and trusted endpoint; malformed or untrusted values fail.
3. Accept only these production endpoint bases:
    - `https://vssps.dev.azure.com/{organization}/`
    - `https://vssps.visualstudio.com/`
    - `https://{organization}.vssps.visualstudio.com/`
4. `POST` only the exact appended path
   `/_apis/Token/SessionTokens?tokenType=SelfDescribing&api-version=5.0-preview.1`
   (with the organization prefix retained for `vssps.dev.azure.com`).
5. Send JSON `displayName`, scope
   `vso.packaging_write vso.drop_write`, and a requested validity no later than
   four hours or the source-token expiry.
6. Re-read the injected UTC clock after endpoint discovery, immediately before
   POST, and after parsing the response. At each boundary the relevant earliest
   expiry must retain more than the documented five-minute skew. The response
   `validTo` must not exceed the requested validity, source-token expiry, or
   this implementation's fixed four-hour session duration.

The source token appears only in the POST `Authorization: Bearer` header. It is
never in the URI, body, result text, exception, or diagnostics. Redirects,
non-HTTPS endpoints, user information, non-default ports, unexpected
host/path/query shapes, multiple discovery headers, and other ecosystems are
rejected without posting.

Production transport uses no redirects, cookies, proxy, or default credentials.
Calls use `SendAsync` with caller cancellation, a 30-second bounded timeout, and
a 64 KiB streamed response limit. Supported timeout overrides are 10
milliseconds through five minutes; supported response limits are 256 bytes
through 1 MiB. Constructor validation rejects values outside those hard bounds
before network activity or large allocation. Injected `HttpClient` transport
supports unit tests; WP4 performs no live calls.

The response parser accepts only the documented case-sensitive
`displayName`, `scope`, `validTo`, and `token` JSON fields, rejects duplicates
and type aliases, and extracts only non-empty `token` plus a UTC `validTo`.
Exchange validation then applies the response and fresh-time bounds above.
HTTP, JSON, size, expiry, cancellation, and timeout failures return stable
secret-free codes.

## Expiry and secret semantics

Direct materialization propagates source `exp`. Exchanged materialization
reports the earlier of source `exp` and SPS `validTo`, always as UTC. WP4 exposes
expiry only; a later package owns refresh. Materialization rejects expired or
not-yet-valid tokens before exchange. SPS success additionally requires the
final earliest expiry to remain strictly beyond the five-minute safety skew at
response completion, so an exchange never returns an already stale credential.

Secret-bearing records redact `ToString()`. Secrets are not cache keys or
metadata. No cache or registry lifecycle is introduced.

## Sources

- [`phase-1.1-nuget-evidence.md`](phase-1.1-nuget-evidence.md)
- [`phase-1.3-python-backend-helper-evidence.md`](phase-1.3-python-backend-helper-evidence.md)
- [`phase-1.4-npm-yarn-config-evidence.md`](phase-1.4-npm-yarn-config-evidence.md)
- [`phase-1.5-git-discovery-evidence.md`](phase-1.5-git-discovery-evidence.md)
- [`phase-wp3-azureauth-process-provider.md`](phase-wp3-azureauth-process-provider.md)
- Microsoft artifacts credential provider commit
  `9c3840be1c97594708331b1797b0a2d9dce480b3`,
  `IAuthUtil.cs`, `VstsCredentialProvider.cs`, `VstsSessionTokenClient.cs`, and
  `VstsSessionTokenFromBearerTokenProvider.cs`
- [Microsoft identity platform configurable token lifetimes][token-lifetimes]

[token-lifetimes]: https://learn.microsoft.com/en-us/entra/identity-platform/configurable-token-lifetimes
