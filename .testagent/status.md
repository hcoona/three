# Test Agent Status

## Scope

- Production target: `AzureDevOpsSpsTokenExchange.cs`
- Test target: `TokenMaterializationWp4Tests.cs`
- Framework: xUnit v3 on Microsoft Testing Platform with .NET SDK 10.0.300

## Implemented Evidence

| Requirement | Test evidence |
|---|---|
| Retry an initial HTTP 400 exactly once without `validTo` | `SpsExchangeRetriesTokenDurationPolicyBadRequestOnceWithoutValidTo`; `SpsExchangeStopsAfterSecondTokenDurationPolicyBadRequest` |
| Accept a fresh service-authoritative expiry beyond the requested value | `SpsExchangeAcceptsServiceAuthoritativeExpiryBeyondRequestedLifetime`; retained stale/too-close expiry theory |
| Accept official `.vssps.dev.azure.com` hosts and reject lookalikes | `SpsSessionEndpointAcceptsOfficialAzureDevOpsHosts`; `SpsSessionEndpointRejectsLookalikeOrUnsafeUris` |
| Use the system proxy while preserving redirect and TLS defaults | `CreateProductionHttpHandlerUsesSystemProxyAndDisablesRedirectsCookiesAndCustomTlsValidation` |

## Validation

- Changed-file `dotnet format --verify-no-changes`: passed for production and test files.
- Focused test-project build: passed with 0 warnings and 0 errors.
- Filtered `TokenMaterializationWp4Tests`: 75 passed, 0 failed, 0 skipped.
- Root `dirs.proj` build: passed with 0 warnings and 0 errors.
- `git diff --check`: passed.

## Quality Review

- Pseudo-mutation review found no focused requirement gap: the tests would fail if the retry were removed or repeated, `validTo` remained usable on retry, the endpoint changed, the expiry upper bound returned, suffix matching lost its label boundary, or proxy/redirect/TLS properties changed.
- Assertion review found no assertion-free, trivial-only, or tautological focused tests. Tests assert returned material, exact request transcripts, body shape, host decisions, normalized endpoints, and handler security properties.
- End-to-end proxy routing is intentionally not tested because it would require process-global environment mutation and a local proxy/TLS setup. Handler properties provide the deterministic production configuration boundary.

## Remaining Work

- None.
