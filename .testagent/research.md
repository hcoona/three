# Test Generation Research

## Bounded Target Inventory

- Production: `src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform/TokenMaterialization/AzureDevOpsSpsTokenExchange.cs`
- Tests: `tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Platform.Tests/TokenMaterializationWp4Tests.cs`
- Framework: xUnit v3 on Microsoft Testing Platform, .NET SDK 10
- Existing doubles: `RecordingHandler`, `FixedTimeProvider`, and `AdvancingTimeProvider`

## Existing Conventions

- Use deterministic `HttpMessageHandler` transcripts instead of network calls.
- Pass `TestContext.Current.CancellationToken` to asynchronous operations.
- Assert concrete status/code, token/expiry, URI, authorization, and JSON body shape.
- Parameterize endpoint allow/deny cases.
- Preserve HTTPS, default-port, user-info/query/fragment, organization/path, redirect, response-size/schema, and default TLS protections.

## Upstream Reference

Microsoft's `VstsSessionTokenClient` retries one initial HTTP 400 once against the same SPS endpoint with a nullable `validTo`, then treats the service response as authoritative. Its allowlist includes both `vssps.dev.azure.com` and `.vssps.dev.azure.com`. This implementation keeps the repository's stricter endpoint/path and response validation around those interoperability behaviors.

## Acceptance Checklist

- [x] Initial HTTP 400 retries exactly once against the same endpoint.
- [x] Retry body omits or nulls `validTo`.
- [x] Fresh parseable service expiry beyond the requested timestamp is accepted exactly.
- [x] Exact and boundary-suffix official SPS hosts are accepted; lookalikes are rejected.
- [x] Default production handler uses runtime/OS proxy discovery while redirects remain disabled.
- [x] Default TLS validation and all existing endpoint protections remain intact.
- [x] Focused tests compile and pass.
