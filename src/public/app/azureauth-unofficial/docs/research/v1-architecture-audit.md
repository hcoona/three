# AzureAuth V1 Architecture Audit

## Status and Scope

This audit records public evidence supporting the v2 direction. It evaluates AzureAuth
v1 as a deterministic, multi-account, machine-facing authentication substrate rather than
only as a human-operated token CLI.

Audited references:

- `main` at
  [`de20930c34b3b86c8a0ed7bbdeeca3f662dae918`](https://github.com/AzureAD/microsoft-authentication-cli/commit/de20930c34b3b86c8a0ed7bbdeeca3f662dae918)
- tag
  [`0.9.5`](https://github.com/AzureAD/microsoft-authentication-cli/tree/0.9.5)
- tag
  [`0.9.6`](https://github.com/AzureAD/microsoft-authentication-cli/tree/0.9.6)
- public issues and Microsoft documentation cited below

This record contains no internal project evidence or unpublished downstream observations.

## Summary

AzureAuth v1 has useful projects, interfaces, MSAL mechanisms, cache integration, ADO
specialization, and cross-platform packaging. Its central weakness is not absence of
layers; it is an under-specified request, policy, and result model.

The v1 design is reasonable for a human asking a CLI to obtain a token using a broad set
of acceptable fallbacks. It is not a sufficient foundation for a caller requiring exact
identity, explicit interaction policy, ordered acquisition, one deadline, host-aware UI,
and typed machine failures.

Assessment for that target use case: **2.3/5**.

| Area | Score | Publicly observable reason |
| --- | ---: | --- |
| Responsibility boundaries | 3/5 | Projects and test seams exist, but command, policy, storage, and presentation remain coupled. |
| Caller-intent preservation | 2/5 | Client, tenant, and scopes pass through; order, interaction, deadline, and host intent do not. |
| Account correctness | 1/5 | Domain suffix filtering, lossy ambiguity, OS-account widening, and no result postcondition. |
| Flow architecture | 2/5 | Deterministic fixed sequence, but broker combines silent and interactive policy stages. |
| Platform and host model | 2/5 | Broad platform support, but no explicit WSL model and weak UI-parent discovery. |
| Cache and security | 3/5 | Secure-first integration with an implicit headless plaintext fallback. |
| Failure and output contract | 2/5 | Token output is separate from logs, but failures and result metadata are collapsed. |
| Testability | 3/5 | Useful interfaces and unit tests, without release-gating real broker and WSL matrices. |
| Release and maintenance | 2/5 | Self-contained pinned artifacts, but stale release records and weak dependency canaries. |

## Finding 1: Flow Order and Interaction Policy Are Conflated

`--mode` values are combined as flags by
[`AuthMode.Combine`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/AuthMode.cs#L124-L133).
The caller's order is therefore lost.

[`AuthFlowFactory`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/AuthFlow/AuthFlowFactory.cs#L38-L87)
then imposes a fixed cache, IWA, broker, web, and device sequence.

[`Broker.GetTokenInnerAsync`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/AuthFlow/Broker.cs#L78-L116)
attempts silent acquisition and then interactive WAM inside one flow. The caller cannot
request broker-aware silent acquisition while prohibiting the interactive half of that
same mechanism.

This is the architectural basis of the missing strict no-interaction contract discussed
in [issue #464](https://github.com/AzureAD/microsoft-authentication-cli/issues/464).

## Finding 2: Account Selection Is a Preference, Not a Postcondition

The CLI documents `--domain` as a preferred-domain filter, not a strict selector.

[`PCAWrapper.TryToGetCachedAccountAsync`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/PCAWrapper.cs#L150-L180):

- filters usernames with a raw suffix comparison;
- returns the same `null` outcome for no account, no match, or multiple matches;
- does not produce a typed ambiguity result.

On Windows,
[`Broker.ResolveAccountAsync`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/AuthFlow/Broker.cs#L119-L142)
widens that `null` outcome to `PublicClientApplication.OperatingSystemAccount`.

The claims retry overload in
[`PCAWrapper`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/PCAWrapper.cs#L111-L134)
applies claims without preserving an account argument.

There is no final account postcondition. This is the central subject of
[issue #465](https://github.com/AzureAD/microsoft-authentication-cli/issues/465).

## Finding 3: The Result Boundary Discards Required Facts

[`PCAWrapper.TokenResultOrNull`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/PCAWrapper.cs#L209-L216)
reduces `AuthenticationResult` to the access token and correlation ID. The provider
account, tenant, scopes, token type, and other acquisition metadata are not preserved as
authoritative result fields.

[`PublicClientAuth`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/PublicClientAuth.cs#L54-L81)
further reduces the public outcome to token or `null`.

[`CommandAad`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Commands/CommandAad.cs#L384-L424)
maps most failures to exit code `1`. A machine caller cannot distinguish account absence,
interaction required, user cancellation, mechanism unavailability, network failure,
timeout, cache failure, or internal failure.

Microsoft recommends treating access tokens as opaque:
[Access tokens in the Microsoft identity platform](https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens).
Strict account selection must therefore be enforced at the MSAL result boundary rather
than reconstructed from token claims.

## Finding 4: Host Context and WSL Are Under-Specified

On Windows, broker UI ownership is derived from `GetConsoleWindow()` and
`GetAncestor(...)` in
[`Broker`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/AuthFlow/Broker.cs#L193-L240).
There is no caller-supplied parent-window contract or typed result for an unusable host.

Microsoft documents that `GetConsoleWindow` can return `NULL` and that pseudoconsole
hosting can expose a non-displayed compatibility window:
[GetConsoleWindow](https://learn.microsoft.com/en-us/windows/console/getconsolewindow).

The source has no distinct WSL host model. A Linux binary is treated as Linux; a Windows
binary launched through WSL interop still receives no explicit Linux-caller-to-Windows-UI
context. See the public WSL request in
[issue #460](https://github.com/AzureAD/microsoft-authentication-cli/issues/460).

Current MSAL versions also document a native Linux broker route for WSL, with significant
WSL, package, and keyring prerequisites:
[Using MSAL.NET with WSL](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/linux-dotnet-sdk-wsl).
V2 must evaluate that route explicitly rather than inherit an accidental host model.

## Finding 5: Timeout and Cancellation Do Not Form One Deadline

The named mutex provides useful prompt serialization, but lock wait and authentication do
not share one caller-defined deadline.

[`Locked.Execute`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/Locked.cs#L43-L84)
uses synchronous waiting.

[`TaskExecutor.CompleteWithin`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/TaskExecutor.cs#L27-L54)
can return after requesting cancellation without proving that the underlying operation
has stopped.

[`AuthFlowExecutor`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/AuthFlow/AuthFlowExecutor.cs#L98-L144)
has separate polling and timeout behavior. The public contract cannot guarantee that a
timed-out request leaves no prompt or background operation.

## Finding 6: Cache Policy Contains an Implicit Security Decision

[`PCACache`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/PCACache.cs#L49-L105)
uses MSAL Extensions and attempts secure platform persistence.

On headless Linux, failure to use the secure store can select an unprotected file cache.
Permission hardening is best effort rather than a caller-visible policy decision. See
[`PCACache`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/PCACache.cs#L85-L95)
and
[`LinuxHelper`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/MSALWrapper/LinuxHelper.cs#L40-L85).

Issue [#398](https://github.com/AzureAD/microsoft-authentication-cli/issues/398)
records reported secure-store availability problems, not an independently reproduced
result. Its [2026-09-09 desk outcome](v1-public-contract-baseline.md#recheck-006-secure-store-availability)
owns the dated source status and limits. The current V2 policy is governed by
[`V2-REQ-040`](../product/requirements/cache-security-and-operational-identity.md#v2-req-040-secure-authentication-state)
and [`V2-REQ-041`](../product/requirements/cache-security-and-operational-identity.md#v2-req-041-safe-reusable-state-recovery-and-concurrency),
not by V1 fallback behavior.

## Finding 7: Real Broker and Dependency Regressions Need Release Canaries

The source provides useful interfaces and unit tests, but no public release-gating matrix
was found for real WAM, macOS broker, browser, device-code, WSL, multiple-account, claims,
and cancellation states.

Between 0.9.5 and 0.9.6, public project references show a substantial MSAL update and a
native interop dependency change:

- [`0.9.5 MSALWrapper.csproj`](https://github.com/AzureAD/microsoft-authentication-cli/blob/21258ff3a2cbb01d6891243114a55abe9ae3587e/src/MSALWrapper/MSALWrapper.csproj)
- [`0.9.6 MSALWrapper.csproj`](https://github.com/AzureAD/microsoft-authentication-cli/blob/8ef1b8b00782bf20a51de078289819a79c3cba70/src/MSALWrapper/MSALWrapper.csproj)

The existing broker source already retries interactive acquisition after
`MsalUiRequiredException`. A changed dependency could therefore expose duplicate
interaction behavior that older versions did not trigger. This is a hypothesis, not a
proven public root cause.

No public issue or source fix was found that establishes a complete Windows repeated-
interaction regression and resolution for 0.9.6. V2 must reproduce dependency-sensitive
behavior directly before drawing a stronger conclusion.

## Finding 8: The Upstream Build Is Not Yet a Public V2 Foundation

The audited upstream
[`nuget.config`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/nuget.config)
clears package sources and selects an authenticated Office Azure Artifacts feed.

[`AzureAuth.csproj`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/AzureAuth.csproj)
references `Microsoft.Office.Lasso`.

**Source finding - build input.** The source declares `Microsoft.Office.Lasso`
`2024.10.23.1` and selects a credentialed Office package feed. The
[Lasso reference manifest](https://github.com/hcoona/microsoft-authentication-cli/blob/2d2c15f907218ea5390d1ed8876fa2433ecc92ae/docs/research/public-build-lasso-reference-manifest.json),
retired from the current tree and kept at that revision, inventories all
261 references at the audited commit: 133 references in 13 production files and 128
references in seven test files.

**Runtime observation.** In the accepted
[WSL2 Linux x64 experiment](https://github.com/hcoona/microsoft-authentication-cli/blob/2d2c15f907218ea5390d1ed8876fa2433ecc92ae/docs/research/experiments/public-build-wsl2-linux-x64-dotnet-8-0-424.json),
retired from the current tree with its source baseline and retained assets and kept at
that revision,
the isolated public-only restore used NuGet.org and exited `1` with `NU1101` for
`Microsoft.Office.Lasso` in `AzureAuth.csproj` and `AzureAuth.Test.csproj`. Its seven
dependent build, filtered-test, and non-publishing package commands were blocked. The
AdoPat, MSALWrapper, and TestHelper dependency assets were valid, while the other five
target projections remain invalid with unknown transitive scope. The source-faithful
restore also exited `1`, but its output was suppressed, so no cause is assigned.

**Bounded inference.** The recorded NuGet.org-only attempt did not establish an entirely
public restore, build, test, or package path for the audited solution in the recorded
WSL2 Linux x64 environment. Restore failed, so its dependent commands were blocked rather
than executed. Other anonymous public package sources were not tested, so this does not
establish that no public source could supply Lasso or that the downstream commands would
fail after a successful restore. It also does not establish permanent NuGet.org
availability, service reliability, or behavior on native Linux, WSL1, Windows, macOS, or
Linux arm64.

### Apparent Lasso Responsibilities

The 83 executable production references in the manifest divide without overlap into the
following source-observable responsibilities. The remaining 50 production entries are
23 namespace imports and 27 documentation references that route to the same surfaces.
For complete manifest coverage, executable entries map by the symbol-family column below.
Documentation entries map by the symbol or limitation they name. In
`AuthFlowResultExtensions`, class-level `lasso-ref-134` maps to both the telemetry event
model and telemetry delivery, method-level `lasso-ref-137` maps to the event model, and
method-level `lasso-ref-150` maps to delivery. For inventory routing, a namespace import
maps only to same-file symbols in the families assigned to that exact namespace:
`Microsoft.Office.Lasso` routes `LassoMcMaster*` and `LassoOptions*`, `.Extensions`
routes `ILogger.LogSuccess`, `.Interfaces` routes `IEnv*` and `ITelemetryService*`, and
`.Telemetry` routes `EventData*`, `CommandExecuteEventData*`, `TelemetryOutput*`,
`TelemetryConfig*`, and `TelemetryDeviceID*`. When a specific import maps to none of
those symbols, that import is namespace-only residue, even if the file uses another
Lasso namespace. This routing describes the public-source inventory, not Lasso's
unpublished API contract. Test entries use the same mapping as corroboration rather than
as a separate runtime responsibility.

| Apparent responsibility | References | Manifest symbol family | Source-established surface | Bounded candidate, not a decision |
| --- | ---: | --- | --- | --- |
| CLI hosting and execution | 3 | `LassoMcMaster*` | [`LassoMcMaster`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Program.cs#L117-L137) wraps an already-created McMaster application and standard service collection. | Retain the McMaster command model behind direct public or fork-owned host glue; validate activation, binding, exit, disposal, and output behavior before claiming equivalence. |
| Environment access | 18 | `IEnv*` | `IEnv.Get` supplies nullable strings; callers own pipeline detection, interaction policy, parsing, and precedence in [`IEnvExtensions`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/IEnvExtensions.cs#L19-L70) and [`PublicClientAuth`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/PublicClientAuth.cs#L22-L52). | Use a minimal fork-owned lookup seam, or direct process-environment access where no test seam is needed; remove ADO-specific reads if that downstream concern remains outside the authentication engine. |
| Telemetry event model | 33 | `EventData*`; `CommandExecuteEventData*` | `EventData` and `CommandExecuteEventData` carry mutable properties, measures, and exceptions across command and authentication paths; [`AuthFlowResultExtensions`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/AuthFlowResultExtensions.cs#L23-L64) performs the visible conversion. | Replace the Lasso event chain with a bounded fork-owned telemetry boundary that remains separate from authentication results and satisfies `V2-REQ-035` and `V2-REQ-046`. |
| Telemetry delivery | 8 | `ITelemetryService*` | `ITelemetryService.SendEvent` is the visible delivery operation in [`AuthFlowResultExtensions`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/AuthFlowResultExtensions.cs#L67-L86); some injected service references are unused by their command. | Delete unused injections and define an optional fork-owned export boundary with explicit configuration, bounded failure, and finite lifecycle behavior. |
| Telemetry and host configuration | 17 | `TelemetryOutput*`; `TelemetryConfig*`; `LassoOptions*` | [`Program`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Program.cs#L62-L121) selects the backend, ingestion token, privacy flags, command events, asynchronous behavior, collected environment names, and stderr threshold. Upstream documents telemetry as [off by default](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/README.md#L96-L109). | Drop the upstream ingestion configuration and identity. Retain the optional telemetry behavior required by `V2-REQ-046`; select its implementation in later architecture work. The existing [`OpenTelemetry.Api` declaration](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/AzureAuth.csproj#L28-L31) does not establish backend or behavioral equivalence. |
| Telemetry device identity | 2 | `TelemetryDeviceID*` | `TelemetryDeviceID.GetAsync` and `Delete` expose an identifier and reset command in [`CommandInfo`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Commands/CommandInfo.cs#L17-L41) and [`CommandInfoResetDeviceID`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Commands/Info/CommandInfoResetDeviceID.cs#L15-L30). | Remove both with telemetry, or define a fork-owned lifecycle and storage policy if an identifier is later justified. |
| Logging extension | 2 | `ILogger.LogSuccess` | `ILogger.LogSuccess` reports token and device-reset success in [`CommandAad`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Commands/CommandAad.cs#L402-L415) and [`CommandInfoResetDeviceID`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Commands/Info/CommandInfoResetDeviceID.cs#L25-L30). | Use ordinary `ILogger` behavior or a one-line fork-owned extension if distinct success formatting remains necessary. |

Three namespace imports have no associated behavioral symbol and are direct removal
candidates: both Lasso imports in
[`CommandAzureAuth`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Commands/CommandAzureAuth.cs#L6-L10)
and the Lasso extensions import in
[`CommandInfo`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth/Commands/CommandInfo.cs#L7-L12).
The test references corroborate the visible environment, event-property, measure, and
delivery call shapes in
[`IEnvExtensionsTest`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth.Test/IEnvExtensionsTest.cs#L30-L112),
[`AuthFlowResultExtensionsTest`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth.Test/AuthFlowResultExtensionsTest.cs#L34-L140),
and
[`PublicClientAuthTest`](https://github.com/AzureAD/microsoft-authentication-cli/blob/de20930c34b3b86c8a0ed7bbdeeca3f662dae918/src/AzureAuth.Test/PublicClientAuthTest.cs#L75-L141);
they do not establish Lasso's private implementation semantics.

The current public evidence does not establish Lasso's service activation, event
serialization, transport, flushing, persistence, privacy enforcement, or logging
semantics. `V2-REQ-046` requires an optional telemetry capability, so removing that
capability is no longer a candidate; the Lasso implementation and upstream device
identity remain non-reusable. A direct McMaster host plus narrow environment, diagnostic,
logging, and telemetry adapters is a hypothesis only: it has not been implemented or
validated and is not an architecture selection.

V2 implementation must not depend on private Microsoft build infrastructure. Before any
AzureAuth implementation is reused, a separately authorized decision must remove,
replace, or isolate the Lasso dependency behind an entirely public build path.

## Structural Debt Versus Isolated Defects

| Area | Classification |
| --- | --- |
| Ordered strategy and interaction policy | Structural: mechanism flags substitute for a policy model. |
| Strict account selection | Structural, with additional suffix-match and claims-retry defects. |
| Typed result and failure semantics | Structural public-contract gap. |
| Parent-window and WSL behavior | Missing host-context abstraction plus implementation defects. |
| Deadline and cancellation | Structural execution-contract gap. |
| Linux cache fallback | Embedded policy decision requiring explicit product ownership. |
| Individual duplicate cache attempts or environment checks | Repairable implementation defects. |

## V1 Component Disposition

| V1 component | V2 disposition |
| --- | --- |
| `AuthMode` and `Combine()` | Replace. |
| `AuthParameters` | Replace with a complete request contract. |
| `PublicClientAuth.Token` | Replace with asynchronous typed orchestration. |
| `AuthFlowFactory` and `AuthFlowExecutor` | Replace. |
| Composite `Broker` | Split; selectively reuse mechanism code. |
| `CachedAuth`, `Web`, and `DeviceCode` | Reuse concepts behind v2 mechanism contracts. |
| IWA | Open decision requiring a supported-scenario justification. |
| `IPCAWrapper` and `PCAWrapper` | Reuse the seam; replace the result shape and claims behavior. |
| `PCACache` | Reuse platform knowledge; replace policy, namespace, and fallback semantics. |
| `TaskExecutor` | Replace. |
| Named lock | Reuse only the shared-state coordination concept with asynchronous deadline-aware behavior. |
| `TokenResult` and manual JSON | Replace with a versioned protocol. |
| Attempt diagnostics | Reuse the local observability concept with fork-owned identity and redaction. |
| ADO PAT components | Defer to a separate product-specific decision. |
| Upstream build and release plumbing | Replace with publicly operable, fork-owned infrastructure. |

## Reusable Assets

The audit supports selective reuse of:

- MSAL and broker integration concepts;
- `IPCAWrapper`-style test seams, expanded to preserve full results;
- system-browser and device-code mechanisms;
- broker-aware silent acquisition;
- secure platform-storage integration after policy review;
- cross-process shared-state coordination after deadline redesign;
- ADO PAT components if a later product decision retains them;
- self-contained platform packaging knowledge.

## Conclusion

The v1 orchestration core should not define v2. Reusing mechanism-level code remains
economical, but every reused component must sit behind v2 identity, interaction, host,
deadline, cache, and result contracts.

Confidence is high for static source findings and public history, medium for inferred
runtime consequences, and intentionally limited for dependency-regression hypotheses
that lack a public reproduction.
