# Test Implementation Plan

| Requirement | Planned evidence |
|---|---|
| One relaxed retry | Add a GET/POST/POST transcript test and a repeated-400 call-ceiling test. Assert identical endpoint/authorization and missing-or-null retry `validTo`. |
| Authoritative expiry | Remove the obsolete five-hour rejection row, retain stale/too-close cases, and add a success test for a five-hour service expiry while confirming the request still asks for four hours. |
| Official hosts | Add accepted cases for exact, app, and regional `.vssps.dev.azure.com` hosts plus rejected prefix/suffix lookalikes and unsafe URI forms. |
| Proxy/redirect/TLS | Add an internal production-handler factory seam and assert system proxy discovery, disabled redirects/cookies, and no custom TLS callback. |

## Validation Plan

1. Run the filtered `TokenMaterializationWp4Tests` class.
2. Run `dotnet format --verify-no-changes` for the test project.
3. Build the focused test project and root traversal project.
4. Run `git diff --check`.
5. Re-open the focused tests and confirm every requirement maps to an exact test name in `status.md`.
