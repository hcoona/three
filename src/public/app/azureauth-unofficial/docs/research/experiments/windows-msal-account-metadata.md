# Windows MSAL Account, Token, and Git Discovery Evidence

This is a completed experiment record for
[Issue #35](https://github.com/hcoona/microsoft-authentication-cli/issues/35), retained as
evidence cited by current records. It authorizes no execution. The Delivery Wave grant it
ran under was completed and removed. The full protocol, operator procedure, package
pins and hashes, launcher and probe recipes, attempt accounting, source pins and complete
execution history remain at the
[pinned revision](https://github.com/hcoona/microsoft-authentication-cli/blob/242cd292a10ee5626e783f42349e570c2e5a19a1/docs/research/experiments/windows-msal-account-metadata.md).
The probe source is retained at the
[same revision](https://github.com/hcoona/microsoft-authentication-cli/tree/242cd292a10ee5626e783f42349e570c2e5a19a1/tools/probes/windows-msal).
The [experiment policy](../experiment-safety.md) governs any future experiment; another
run requires a new accepted protocol and authorization.

The investigation asked whether a designated personal Microsoft account is visible
through MSAL's explicit Windows account discovery, whether a unique exact email match
permits silent and interactive acquisition for the Azure DevOps scope, which result
fields are available, whether a later process reuses existing broker state, and whether
the token is accepted by one owner-designated Azure DevOps repository's read-only Git
discovery endpoint. This record also keeps the public-source findings that consume
these observations: the [probe basis](#windows-msal-probe-basis), the
[Client Profile and tenant mapping assessment](#client-profile-and-tenant-mapping-assessment)
and the [Azure Artifacts token-path assessment](#azure-artifacts-token-forms-and-nuget-paths),
moved from the [V1 baseline](../v1-public-contract-baseline.md) under
[Issue #509](https://github.com/hcoona/microsoft-authentication-cli/issues/509) with their
evidence types and dates unchanged. This record is neither a V2 implementation nor a
selected Client Profile, host architecture or support promise.

## Current Disposition: MSA Acquisition and Reuse Observed

**Runtime observation:** The GCM-informed configuration completed preparation, attended
interactive acquisition and its conditional fresh-process silent follow-up. Both account
actions returned the exact requested email and a nonempty, unexpired token. Both
authenticated Git discovery requests returned HTTP 200 with the expected content type and
fixed service prefix. During interaction, the operator identified WAM and manually
switched the apparent Security Key selection to Windows Hello PIN. The
[completed observation](#gcm-informed-msa-acquisition-and-silent-reuse) records the
details.

**Limits:** This establishes the bounded requested-personal-account path on the
designated existing Windows host and Git target with this exact configuration. It does
not isolate the cause of the earlier broker error, establish fresh-state or
cross-consumer behavior, select a Client Profile, or establish intended external
registration reuse or broader support. The sequence is complete and its limits are
exhausted.

## Probe Configuration

The research probe was a small `net8.0-windows` x64 Windows Forms executable that ran one
operator-started operation per process. Each operation used a new in-memory MSAL cache,
with no MSAL Extensions or application cache file, and the existing OS broker state.

| Input | Value |
| --- | --- |
| Windows host | Owner-designated Windows 11 25H2 x64 host in an interactive desktop session with a corporate-account Windows session, as reported by the owner; join and compliance state were not measured |
| Dependencies | `Microsoft.Identity.Client` and `.Broker` 4.83.1; `Microsoft.Identity.Client.NativeInterop` 0.20.3, fetched as public packages and restored from a local feed |
| Client | Microsoft-owned Visual Studio client `872cd9fa-d31f-45e0-9eab-6e460a02d1f1`, an experiment input only |
| Scope | `499b84ac-1321-427f-aa17-267ca6975798/.default` |
| Broker options | Windows WAM; `ListOperatingSystemAccounts = true`; a real owned parent window |
| Selected account | One owner-designated personal Microsoft account, matched by exact email; never recorded |
| Resource | One owner-designated `https://dev.azure.com` Git remote; never recorded |

Earlier sequences used the `https://login.microsoftonline.com/common` authority without
MSA passthrough. The final GCM-informed sequence used the `organizations` authority with
`MsaPassthrough = true`. For its silent follow-up only, when the uniquely matched
account's home tenant was the public MSA home tenant, the probe applied MSAL
`WithTenantId` with the public Microsoft MSA transfer tenant
`f8cdef31-a31e-4b4a-93e4-5f571e91255a`. It recorded only whether that rule applied.

Account selection passed only a unique exact email match to MSAL `WithAccount`. With no
match, the unchanged logic used `WithLoginHint`. The returned email was checked against
the request. There was no OS-default sentinel, alias inference, account fallback,
browser fallback or device-code flow.

The probe's Git discovery was a single `GET <remote>/info/refs?service=git-upload-pack`.
It used a Bearer token only after an exact returned-email match and a nonempty, unexpired
token. Redirects, cookies and default credentials were disabled. Only HTTP status,
challenge presence and a Boolean comparison of the first 34 response bytes against the
fixed `001e# service=git-upload-pack\n0000` announcement were retained. This follows
Microsoft's
[Azure Repos authentication guidance](https://learn.microsoft.com/en-us/azure/devops/repos/git/auth-overview?view=azure-devops)
and Git's
[v2.51.0 HTTP protocol](https://github.com/git/git/blob/v2.51.0/Documentation/gitprotocol-http.adoc).
Recognized discovery is bounded evidence of token acceptance for that target, not a
complete clone, fetch or push.

Sanitized results kept fixed booleans, bucketed account counts and, for failures, only
the MSAL error code, `IsRetryable`, service status, UI-required classification and the
named broker status and code. No token bytes, account or tenant identifiers, remote URL,
raw scopes, exception text, headers, bodies or broker diagnostics were retained.

## Execution History

The first preparation failed when Windows restore could not reach the public NuGet
index; the cause was not established. Public packages were then fetched through WSL into
a local feed. A Windows PowerShell 5.1
process exit-code defect in the launcher stopped one more preparation. Later preparations
built the probe and passed a synthetic self-check that made no account or resource call.
The account sequences below are the evidence current records cite. Earlier subjects
retain their own evidence limits.

### Account Visibility and Unresolved Acquisition Failure

**Runtime observation, 2026-09-10 UTC:** Three actions each reported broker
availability, multiple visible accounts, exactly one exact email match and no
missing-email indication. An anonymous Git discovery request observed HTTP 302 with a
`WWW-Authenticate` header and no recognized Git advertisement. Silent acquisition for
the matched account returned `interaction-required`. The permitted interactive action
returned the probe's coarse `provider-rejected` category, its mapping for
`MsalServiceException`, without an `AuthenticationResult`. No authenticated discovery
request or reuse follow-up ran.

**Limits:** Exact account discovery worked in this existing state. The coarse category
does not identify the underlying service error, configuration issue, account eligibility
or operator action, and finer fields were not captured. Do not infer categorical MSA
rejection or a registration-support conclusion. Successful result metadata,
authenticated discovery and later reuse remained unobserved. Detailed operator UI steps
were unavailable.

### Diagnostic Subject Preparation and Acquisition

**Runtime observation, 2026-09-10 UTC:** A diagnostic subject added the structured
failure fields above and passed its synthetic self-check, including exclusion of
synthetic private markers. One interactive action then reported broker availability,
multiple visible accounts, no missing-email indication and zero exact matches, so the
unchanged logic used `WithLoginHint` rather than `WithAccount`. It reached the probe's
360-second process deadline as `process-timeout` without an `AuthenticationResult`
or authenticated resource request. The structured failure recorded kind `client`,
`authentication_canceled`, broker status `UserCanceled`, broker code 0, no service
status and `IsRetryable = false`.

**Inference and limits:** The
[pinned MSAL mapping](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/WamAdapters.cs#L69-L74)
converts `UserCanceled` to a client cancellation, which is consistent with the deadline
cancellation. It does not establish manual cancellation, remote rejection or why
acquisition had not completed. The cause of the changed exact-match count is unknown,
and this action does not explain the first sequence's failure.

### Attended Preparation and Structured Broker Failure

**Runtime observation, 2026-09-11 UTC:** After preparation, the agent announced
readiness and waited for the owner's explicit readiness response. One interactive action
then found one exact match among multiple visible accounts and used `WithAccount`. About
20.6 seconds after launch, it returned `acquisition-failed` without an
`AuthenticationResult` or resource request. The structured failure recorded kind
`service-or-broker`, `unknown_broker_error`, broker status `Unexpected`, broker code
`2147786073` (`0x80049D59`), service status 0 and `IsRetryable = false`. The owner
saw the probe form, apparent automatic filling and submission, and then a brief surface
that appeared to be WAM before the windows closed.

**Limits:** Service status 0 is the reported default, not an observed HTTP response. The
[pinned MSAL default mapping](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/WamAdapters.cs#L116-L120)
accounts for the generic category; it does not identify the cause of this broker code or
establish remote rejection. Matching the first sequence's category does not prove the
same cause. The owner was uncertain about the brief surface's identity, and no sign-in,
MFA or consent step was reported.

### GCM-Informed MSA Acquisition and Silent Reuse

**Runtime observation, 2026-09-11 UTC:** After preparation and the explicit readiness
handoff, the interactive action used the `organizations` authority and
`MsaPassthrough = true` with the same client, scope, exact account checks and owned
parent. After its strict result and discovery checks passed, the conditional silent
action ran in a fresh process with a new in-memory cache, resolved the unique account
afresh and applied the MSA transfer-tenant rule.

| Observation | Interactive | Silent |
| --- | --- | --- |
| Broker available; visible accounts; exact matches | true; multiple; one | true; multiple; one |
| Acquisition started; provider result returned | true; true | true; true |
| Token present; unexpired | true; true | true; true |
| Exact returned email | true | true |
| Tenant present; equals public MSA home tenant | true; false | true; false |
| Silent MSA transfer rule applied | false | true |
| Scope metadata present; requested `.default` reported | true; true | true; true |
| Structured failure | null | null |
| Authenticated resource request; HTTP status; challenge | true; 200; false | true; 200; false |
| Discovery content type and fixed prefix matched | true | true |

The interactive and silent processes completed about 48.5 and 29.2 seconds after their
start records. The operator identified WAM, reported that Security Key appeared initially
selected and manually chose Windows Hello PIN, followed by apparent success. No factor
value was collected.

**Inference and limits:** This exact existing-state configuration returned the requested
personal account's token and recognized authenticated Git discovery, then repeated those
postconditions through fresh-process silent acquisition without an application cache
file. It does not isolate a single option's causal effect, explain the earlier
`0x80049D59`, or prove that `common` is unsupported. It also does not establish a
clean first-use path, cross-consumer interoperability, a complete Git operation or
intended external reuse of the Microsoft-owned registration. The tenant equality flag
does not reveal the token tenant. No operator UI observation exists for the silent
action, so it is not an independently observed no-UI guarantee.

## Windows MSAL Probe Basis

The [bounded Windows protocol](https://github.com/hcoona/microsoft-authentication-cli/blob/242cd292a10ee5626e783f42349e570c2e5a19a1/docs/research/experiments/windows-msal-account-metadata.md) governed
account discovery, selected-account acquisition, result metadata, conditional broker reuse,
and discovery at the single owner-designated Azure DevOps Git repository. It used V1's
managed/native dependency versions in a small research probe. The first sequence stopped
without a token; the diagnostic sequence later timed out with structured cancellation
fields, also without a token. Authentication configuration and selection logic were
unchanged, but the observed exact-match count changed. A subsequent explicitly attended
action returned an unexpected broker failure, also without a token. These observations
do not establish resource authorization, registration eligibility, or a Profile/support
commitment, and no corporate-account comparison is performed. A subsequent GCM-informed
sequence succeeded in both interactive acquisition and fresh-process silent reuse, with
exact returned email and recognized authenticated Git discovery. Its bounded current
conclusion is recorded below; earlier failures retain their original evidence limits.

The [completed evidence record](#execution-history)
summarizes the initial public-index and launcher failures, followed by successful Windows
compilation and synthetic self-check of the metadata-only subject and the amended
Git-discovery subject, then the diagnostic subject with its error-output self-check.
The pinned protocol retains their distinct source and artifact identities. Preparation used
public packages transferred through WSL to a Windows local feed and did not change the
native Windows account or token boundary.

The first account sequence found one exact match for the designated account among
multiple visible accounts and an anonymous HTTP 302 response with an authentication
challenge header. Silent acquisition required interaction; the permitted interactive
action returned the probe's `provider-rejected` category without an authentication result.
The sequence stopped without an authenticated Git request or reuse follow-up. The error
category does not identify the cause or establish categorical MSA incompatibility.
That first sequence left successful result metadata, token acceptance, fresh-state
behavior, and later reuse unobserved; the [evidence record](#account-visibility-and-unresolved-acquisition-failure)
owns the observations and limitations.

The later [diagnostic action](#diagnostic-subject-preparation-and-acquisition)
reported zero exact matches and used the existing login-hint branch, rather than the
previous matched-account branch. It reached the process deadline with
`authentication_canceled` / `UserCanceled`, no authentication result, and no authenticated
resource request. The cancellation category does not establish manual cancellation or
explain the previous service-exception category. The cause of the changed account
visibility and detailed operator UI steps remain unknown. That sequence stopped; no
reuse follow-up ran, and the evidence does not select a Profile or establish support.

The subsequent [attended sequence](#attended-preparation-and-structured-broker-failure)
completed preparation and the explicit readiness handoff, then again found one exact
match and used the matched-account branch. It returned `unknown_broker_error`, broker
status `Unexpected`, and code `2147786073` (`0x80049D59`) about 20.6 seconds after launch,
with no authentication result or resource request. The owner reported the input form,
apparent automatic fill/submission, and a brief possible WAM surface before the windows
closed. The brief surface was not conclusively identified and no detailed sign-in/MFA/
consent steps were reported. This adds structured failure and UI observations, not an
explanation of the code, proof of the earlier failure's cause, or Profile eligibility.
That sequence stopped. The later successful configuration and its limits are described below.

**Source interpretation, checked 2026-09-11 UTC:** The same pinned
[`WamAdapters` default branch](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/WamAdapters.cs#L116-L120)
returns a service exception with `UnknownBrokerError` for statuses outside its explicit
cases. This supports the reported generic category for `Unexpected`, not a specific
cause or identity-service rejection. Reported service status zero is not an observed
HTTP response. The two failed matched-account actions need not share a cause; the first
one lacks the finer fields needed to compare them.

**Source interpretation, retrieved 2026-09-10 UTC:** The pinned MSAL
[`WamAdapters` exception mapping](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/WamAdapters.cs#L58-L120)
uses `MsalServiceException` for configuration/API-contract failures, network or temporary
server failures, and unknown broker failures. Its
[runtime-response wrapper](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/WamAdapters.cs#L379-L385)
also wraps other response-processing exceptions in that type. The probe's literal
`provider-rejected` label therefore denotes an acquisition-failure category, not proof
that a remote identity service rejected the account. No finer error code/status was
retained; these source possibilities do not identify which failure occurred in this run.

The same pinned API documents [`ErrorCode`](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/MsalException.cs#L129-L154)
as a protocol code for exception handling, [`IsRetryable` and broker keys](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/MsalException.cs#L30-L61),
service [`StatusCode`](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/MsalServiceException.cs#L149-L163),
and [`UiRequiredExceptionClassification`](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/MsalUiRequiredException.cs#L66-L89).
The [`broker decorator`](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/WamAdapters.cs#L214-L225)
populates separate status-name and numeric-code entries alongside private context and
telemetry. The diagnostic subject selects only the protocol/enum/numeric fields. These
contracts justify retaining useful error classification without raw exception/property-bag
export. A retry hint does not authorize another attempt, and a reported status remains
bounded evidence rather than proof of every underlying failure cause. The earlier run's
missing fields cannot be reconstructed from this source finding.

**Pinned source findings, reviewed on 2026-09-10 UTC:** MSAL 4.83.1's
[`BrokerOptions`](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/ApiConfig/BrokerOptions.cs#L81-L87)
exposes `ListOperatingSystemAccounts`. The
[`runtime broker`](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/RuntimeBroker.cs#L571-L632)
returns an empty list when this option is off and otherwise calls discovery, filters the
cloud environment, and converts the returned accounts. V1 0.9.6's
[`Windows broker configuration`](https://github.com/AzureAD/microsoft-authentication-cli/blob/8ef1b8b00782bf20a51de078289819a79c3cba70/src/MSALWrapper/AuthFlow/Broker.cs#L232-L242)
does not enable that option. This is an available dependency capability, not evidence
that a particular existing personal account will be visible or have an exact email.

MSAL's
[`broker availability query`](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/PublicClientApplication.cs#L80-L98)
and
[`custom web UI callback`](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/Extensibility/ICustomWebUI.cs#L30-L52)
let the probe require WAM and decline browser fallback. The callback implements no OAuth
exchange. The earlier probe left the hidden first-party `MsaPassthrough` option off,
as in V1's Windows configuration. The following comparison changes that experiment
input without upgrading MSAL.

### GCM MSA Configuration Comparison

**Public source findings, retrieved 2026-09-11 UTC:** GCM **v2.9.1**, commit
[`6760f0ef069c994aa2bb1d703fb374986ee82a3e`](https://github.com/git-ecosystem/git-credential-manager/tree/6760f0ef069c994aa2bb1d703fb374986ee82a3e),
provides a concrete Azure Repos MSA configuration reference. This is a pinned source
comparison, not a claim about the installed GCM version or its execution on this host.

| Concern | GCM source finding | Probe comparison |
| --- | --- | --- |
| Client and scope | [Azure DevOps constants](https://github.com/git-ecosystem/git-credential-manager/blob/6760f0ef069c994aa2bb1d703fb374986ee82a3e/src/shared/Microsoft.AzureRepos/AzureDevOpsConstants.cs#L11-L16) specify the Visual Studio client and Azure DevOps `.default` scope. | The earlier and amended probe use these same public values. The client remains Microsoft-owned. |
| MSA passthrough | [Azure Repos token acquisition](https://github.com/git-ecosystem/git-credential-manager/blob/6760f0ef069c994aa2bb1d703fb374986ee82a3e/src/shared/Microsoft.AzureRepos/AzureReposHostProvider.cs#L347-L353) passes `msaPt: true`; the [Windows broker builder](https://github.com/git-ecosystem/git-credential-manager/blob/6760f0ef069c994aa2bb1d703fb374986ee82a3e/src/shared/Core/Authentication/MicrosoftAuthentication.cs#L633-L643) forwards it to `BrokerOptions.MsaPassthrough`. | The earlier probe used the default false; the amended subject enables it. |
| Authority | [Authority discovery](https://github.com/git-ecosystem/git-credential-manager/blob/6760f0ef069c994aa2bb1d703fb374986ee82a3e/src/shared/Microsoft.AzureRepos/AzureDevOpsRestApi.cs#L30-L88) prefers the service's Bearer authority, then its resource-tenant header. An empty resource-tenant GUID selects `organizations` for an MSA-backed organization; absent information falls back to `common`. | The earlier probe fixed `common`. The amended subject fixes `organizations` as an MSA candidate, without claiming that this private target has an empty resource tenant or reproducing GCM's discovery. |
| Silent transfer tenant | [Selected-account silent acquisition](https://github.com/git-ecosystem/git-credential-manager/blob/6760f0ef069c994aa2bb1d703fb374986ee82a3e/src/shared/Core/Authentication/MicrosoftAuthentication.cs#L533-L554) applies `WithTenantId` for an MSA home account; the [public constants](https://github.com/git-ecosystem/git-credential-manager/blob/6760f0ef069c994aa2bb1d703fb374986ee82a3e/src/shared/Core/Constants.cs#L21-L32) identify the home and Microsoft transfer tenants. | The conditional silent follow-up applies the same tenant rule to its unique exact account and records only whether it applied. |

The probe's pinned MSAL 4.83.1 already exposes
[`MsaPassthrough`](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/ApiConfig/BrokerOptions.cs#L74-L79),
documented in public source as a legacy first-party option. Its
[`WAM adapter`](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/WamAdapters.cs#L153-L164)
passes the consumer-passthrough request property to the native runtime. This source
contract supports a bounded experiment with the already declared Microsoft client; it
does not establish eligibility for arbitrary registrations or intended external reuse.
GCM v2.9.1 [pins](https://github.com/git-ecosystem/git-credential-manager/blob/6760f0ef069c994aa2bb1d703fb374986ee82a3e/Directory.Packages.props)
MSAL 4.84.2. The probe keeps 4.83.1, its owned parent, exact account checks, and lack of an
application cache or browser fallback. It does not reproduce GCM's full runtime or policy.

**Hypothesis and consequence:** The configuration difference could explain why the earlier
probe did not reach a usable MSA path. The completed comparison below demonstrates a
successful configuration; it does not isolate one option's causal effect or prove the
cause of `0x80049D59`. The
[evidence record](#current-disposition-msa-acquisition-and-reuse-observed)
owns the actual results; execution limits and readiness remained in the
[pinned protocol](https://github.com/hcoona/microsoft-authentication-cli/blob/242cd292a10ee5626e783f42349e570c2e5a19a1/docs/research/experiments/windows-msal-account-metadata.md). No GCM helper, cache, credential,
organization API, or extra discovery request was used. No Profile or platform is selected.

### Client Profile and Tenant Mapping Assessment

**Scope and provenance:** Public desk inspection on **2026-09-11 UTC** addresses the
tenant-mapping and external-registration questions from the high-level architecture.
The MSAL sources remain pinned to **4.83.1** at
`d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f`; the GCM comparison remains **v2.9.1** at
`6760f0ef069c994aa2bb1d703fb374986ee82a3e`. No dependency was built or executed, and no
account, cache, authentication, or resource request was part of this assessment.

**Source findings:**

- Microsoft's [authority configuration guidance](https://learn.microsoft.com/en-us/entra/identity-platform/msal-client-application-configuration#authority)
  describes `common` as admitting work/school and personal accounts, and ordinary
  `organizations` as admitting work/school accounts. The effective audience is constrained
  by both the code configuration and the application registration. The same retrieval
  of [Azure DevOps guidance](https://learn.microsoft.com/en-us/azure/devops/integrate/get-started/authentication/entra-oauth?view=azure-devops#tips-for-building-and-migrating)
  still states that ordinary Entra applications do not natively support MSA users for
  the Azure DevOps resource. Neither statement is a new observation of the legacy client.
- MSAL's [`BrokerOptions.MsaPassthrough`](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/ApiConfig/BrokerOptions.cs#L74-L79)
  and its [public API documentation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.identity.client.brokeroptions.msapassthrough?view=msal-dotnet-latest)
  describe a legacy option available only to Microsoft first-party applications and
  recommend avoiding it where possible. The WAM adapter forwards the option to the
  runtime, as recorded above. This is a specific registration capability, not a way to
  add MSA support to an arbitrary application registration.
- MSAL's [`AuthorityInfo` request resolution](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/AppConfig/AuthorityInfo.cs#L505-L583)
  can resolve a tenantless authority using the selected account's home tenant. Its
  request-override branch preserves an explicit non-tenantless authority and has a
  separate `organizations`/MSA-passthrough case.
  [`WithTenantId`](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/ApiConfig/AbstractAcquireTokenParameterBuilder.cs#L248-L276)
  overrides the request tenant while preserving the authority host and validation
  setting. These contracts distinguish configured authority, request override, and
  resolved authority; they do not make every `common` request use a literal `/common`
  endpoint throughout acquisition.
- GCM's [Azure DevOps authority discovery](https://github.com/git-ecosystem/git-credential-manager/blob/6760f0ef069c994aa2bb1d703fb374986ee82a3e/src/shared/Microsoft.AzureRepos/AzureDevOpsRestApi.cs#L36-L88)
  explicitly uses `organizations` for its MSA-passthrough service path. Its
  [silent acquisition workaround](https://github.com/git-ecosystem/git-credential-manager/blob/6760f0ef069c994aa2bb1d703fb374986ee82a3e/src/shared/Core/Authentication/MicrosoftAuthentication.cs#L533-L554)
  selects public transfer tenant `f8cdef31-a31e-4b4a-93e4-5f571e91255a` when passthrough
  is enabled and the selected account has MSA home tenant
  `9188040d-6c67-4c5b-b112-36a304b66dad`. That branch has no separate guard for V2's
  explicit caller resource-tenant constraint. Copying it indiscriminately could replace
  the exact tenant the V2 caller requested.
- GCM links the workaround to public [MSAL Issue #3077](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/3077).
  At retrieval, the Issue was closed with `state_reason = not_planned`, five comments,
  and `updated_at = 2025-06-09T10:50:04Z`. The report concerns MSAL 4.37.0 on macOS
  and a first-party application; it identifies the transfer-tenant workaround as Public
  Cloud only. A [maintainer comment](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/3077#issuecomment-1507353678)
  confirms the workaround's then-current necessity. Issue closure is not evidence that
  the behavior was fixed in the pinned Windows dependency. Private work-item links were
  not followed, and account details and raw diagnostics are not reproduced here.

**External reuse evidence and limits:** AzureAuth's [public README](https://github.com/AzureAD/microsoft-authentication-cli/blob/8ef1b8b00782bf20a51de078289819a79c3cba70/README.md#L10-L12)
describes a CLI returning tokens for public-client applications and acting as an Azure
DevOps credential provider. Its Visual Studio identifier and GCM's use of the same
identifier are pinned above. These establish published tool use of the Microsoft-owned
registration. They do not identify a Microsoft support or continued-availability
commitment for this fork, grant ownership of the registration, or show that independently
registered clients have the same legacy capability. The API's first-party limitation
also does not, by itself, establish a blanket prohibition on all third-party tool use
of that public identifier.

**Architecture inference:** Under `V2-REQ-019`, normalized `common` leaves the result
tenant unconstrained within the selected Profile's eligible audience; an explicit or
fixed tenant remains an exact constraint. A separately reviewed legacy provider mapping
can therefore retain `common` intent while using the demonstrated `organizations` and
MSA-transfer routes. It must not run the transfer substitution for an explicit or fixed
resource tenant, and final result metadata remains authoritative. This is the allocation
in the [client-identity view](../../architecture/client-application-identity.md#provider-mapping),
not a new runtime result or a generic equivalence between `common` and `organizations`.

Decision `0003` already defines the unofficial external-compatibility posture. The
[external dependency boundary](../../architecture/client-application-identity.md#external-dependency-boundary)
now records the actual public reuse evidence and support limits instead of treating a
support guarantee as something established by token success. Profile distribution and
acceptance still require the existing host, authority, redirect/broker, consent, audit,
branding, state-partitioning, explicit-selection, and failure evidence. The successful
probe did not retain the actual result-tenant value and does not validate V2's exact-tenant
branch or prove that the result tenant equals a routing constant.

**Recheck disposition:** RECHECK-007's `client-profile` trigger fires for this assessment.
Its named Azure DevOps source was refreshed above; the accepted Windows observation
remains the bounded account-type evidence. The architecture maps tenant intent and states
the legacy external-dependency boundary without enabling or distributing a Profile.
All seven registry entries were evaluated. RECHECK-001 and RECHECK-002 retain their
accepted dispositions: interaction permission and strict account/result constraints are
unchanged. RECHECK-003, RECHECK-004, and RECHECK-005 do not fire because no new WSL,
system-browser, or Linux-broker path is selected. RECHECK-006's accepted state-ownership
disposition is unchanged. There is no Wave change, release, or new experiment. Mutable
Issue status is dated context for the pinned source finding, not an ongoing claim of a
fix or a separate prerequisite to applying the selected dependency's contract.

### Observed MSA Token, Git Discovery, and Silent Reuse

**Runtime observation, 2026-09-11 UTC:** Under the
[accepted PR #52 protocol](#gcm-informed-msa-acquisition-and-silent-reuse),
preparation attempt 12 passed, followed by attended interactive attempt 13 and conditional
fresh-process silent attempt 14. Both account actions found one exact match among multiple
visible accounts and returned the exact requested email, a nonempty unexpired token,
and recognized HTTP 200 authenticated Git discovery for the single designated target.
The interactive operator identified WAM and manually changed the apparent Security Key
selection to Windows Hello PIN. The silent action applied the public MSA transfer-tenant
rule to the newly resolved real account, without an application cache file. Both owned
probe and input-automation processes exited; the sequence is complete and stopped.

**Bounded conclusion:** Requested-personal-account metadata, usable token acquisition,
and later-process silent reuse work in this declared existing Windows scenario with the
GCM-informed configuration. The mechanism's feasibility for that scenario need not remain
an unobserved premise. Existing state, a single host/target, and the paired configuration
limit attribution and generalization. No clean first-use, alias coverage, cross-consumer
interoperability, actual returned-tenant identity, full Git operation, or broader support
is inferred. RECHECK-007 now has bounded token/resource behavior evidence. The
[client-profile assessment](#client-profile-and-tenant-mapping-assessment) records the
separate public reuse and support boundary; the concrete Profile-selection gate remains
open.

### Host and Registration Recheck for the Probe

The 2026-09-10 UTC desk refresh inspected the public sources below before proposing
native Windows execution initiated from WSL. This evaluates the applicable research
boundary; it selects no V2 WSL or Linux-broker implementation and transports no token.

| Concern and source | Retrieved state and bounded outcome |
| --- | --- |
| Windows [WAM guidance](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam) | The page identifies supported Windows versions, a required parent window, account picking for mixed authority audiences, and possible browser fallback. The probe owns a Windows Forms parent, requires broker availability, and declines browser fallback. These are preparation choices, not observed host behavior. |
| RECHECK-003: [Issue #460](https://github.com/AzureAD/microsoft-authentication-cli/issues/460) | Open; `updated_at = 2026-05-13T17:25:51Z`. Its body proposes a Windows helper, trust/version/transport boundaries, and missing-helper/fallback handling. It is a proposal, not evidence of an upstream implemented bridge. This probe uses an exact local Windows executable and returns only flags. |
| RECHECK-003/005: [current WSL guidance](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/linux-dotnet-sdk-wsl) | Documents native Linux broker packages, dependencies, and an unlocked keychain. It does not supply the Windows-helper protocol proposed by #460. This experiment uses native Windows WAM; it does not install or invoke that Linux broker. |
| RECHECK-005: [PR #462](https://github.com/AzureAD/microsoft-authentication-cli/pull/462) | Open and unmerged; `updated_at = 2026-08-14T10:00:41Z`. The inspected diff adds Linux broker routing, a Linux redirect, and OS-account listing, while retaining an OS-default sentinel fallback. Its reported Ubuntu test is public author-reported experience, not a V2 support result or this experiment's implementation. |
| RECHECK-007: [Azure DevOps guidance](https://learn.microsoft.com/en-us/azure/devops/integrate/get-started/authentication/entra-oauth?view=azure-devops) | The dated guidance concerns ordinary Entra applications. Earlier account sequences returned no token; the subsequent [GCM-informed sequence](#gcm-informed-msa-acquisition-and-silent-reuse) returned exact-account tokens and recognized authenticated discovery through both attended interaction and fresh-process silent reuse. This resolves the token/resource feasibility question for the declared existing Windows configuration. It does not identify the earlier failure cause or establish intended external registration reuse; no Profile is selected. |

RECHECK-001, RECHECK-002, and RECHECK-006 retain their bounded desk dispositions; this
protocol does not amend product interaction, account, or cache requirements. RECHECK-004
is not activated as a browser workstream: the probe declines that mechanism. Later
platform/Profile choices and fired triggers retain their full recorded outcomes.

## Azure Artifacts Token Forms and NuGet Paths

**Immutable source findings, retrieved 2026-09-11 UTC:** Azure Artifacts Credential Provider at
[`bca6c32fdb9611aea25819147ef4508f730aa5fb`](https://github.com/microsoft/artifacts-credprovider/tree/bca6c32fdb9611aea25819147ef4508f730aa5fb)
defines the Azure DevOps MSAL scope as
`499b84ac-1321-427f-aa17-267ca6975798/.default` in
[`MsalConstants.cs`](https://github.com/microsoft/artifacts-credprovider/blob/bca6c32fdb9611aea25819147ef4508f730aa5fb/src/Authentication/MsalConstants.cs#L9-L10).
This is the same resource/scope used by
[GCM v2.9.1](https://github.com/git-ecosystem/git-credential-manager/blob/6760f0ef069c994aa2bb1d703fb374986ee82a3e/src/shared/Microsoft.AzureRepos/AzureDevOpsConstants.cs).

Its NuGet integration has both direct-token and exchange paths. The exchange is the
default, not a requirement of the NuGet plugin protocol. The
[`VstsCredentialProvider` branch](https://github.com/microsoft/artifacts-credprovider/blob/bca6c32fdb9611aea25819147ef4508f730aa5fb/CredentialProvider.Microsoft/CredentialProviders/Vsts/VstsCredentialProvider.cs#L164-L188)
returns the acquired access token directly when
`ARTIFACTS_CREDENTIALPROVIDER_RETURN_ENTRA_TOKENS=true`.
[`EnvUtil`](https://github.com/microsoft/artifacts-credprovider/blob/bca6c32fdb9611aea25819147ef4508f730aa5fb/CredentialProvider.Microsoft/Util/EnvUtil.cs#L171-L174)
defaults that option to false. The same branch and option exist in released v2.0.4 at
[`14855bba1b20482623697fe9497cc5398d5077cd`](https://github.com/microsoft/artifacts-credprovider/blob/14855bba1b20482623697fe9497cc5398d5077cd/CredentialProvider.Microsoft/CredentialProviders/Vsts/VstsCredentialProvider.cs#L164-L188).
This is source evidence for an available path, not a new execution result.

| Path or token form | Source-defined behavior | Lifetime and state distinction |
| --- | --- | --- |
| Direct access token, called an Entra token by the plugin | Return MSAL's access token as `Password`, username `EntraToken`, and authentication type `Basic`; skip the session-token endpoint. | Identity-platform expiry applies. The handler disables its session-token cache for this mode; MSAL caching remains available. |
| Exchanged `SelfDescribing` token | Use the access token to request an Azure DevOps-issued JWT session token, then return it to NuGet as a Basic credential. | The provider normally requests four hours and caps its requested duration at 24 hours. Actual validity comes from the service; a rejected requested expiry can be retried with service-defined validity. The provider has a separate session-token cache. |
| Exchanged `Compact` token | Use the access token to request an Azure DevOps PAT. | The provider normally requests 90 days, subject to service policy. PATs are outside the selected V2 Slice. |

The cache distinction is explicit in
[`GetAuthenticationCredentialsRequestHandler`](https://github.com/microsoft/artifacts-credprovider/blob/bca6c32fdb9611aea25819147ef4508f730aa5fb/CredentialProvider.Microsoft/RequestHandlers/GetAuthenticationCredentialsRequestHandler.cs#L126-L142).
The exchange provider
[selects `Compact` by default after interactive acquisition and `SelfDescribing` otherwise](https://github.com/microsoft/artifacts-credprovider/blob/bca6c32fdb9611aea25819147ef4508f730aa5fb/CredentialProvider.Microsoft/CredentialProviders/Vsts/VstsSessionTokenFromBearerTokenProvider.cs#L33-L61),
unless its token-type option overrides that choice. Thus, leaving direct-token mode off
does not establish a PAT-free path. These are upstream defaults, not V2 behavior.

[`VstsSessionTokenClient`](https://github.com/microsoft/artifacts-credprovider/blob/bca6c32fdb9611aea25819147ef4508f730aa5fb/CredentialProvider.Microsoft/CredentialProviders/Vsts/VstsSessionTokenClient.cs#L60-L149)
uses bearer authorization to call `/_apis/Token/SessionTokens`, requesting
`vso.packaging_write vso.drop_write`. Those are downstream service-credential scopes,
not an additional MSAL resource or an engine operation. A session token has its own
issuance and validity; it is not the MSAL refresh token. The name `SelfDescribing`
identifies the ADO JWT form, not a general distinction between JWT and OAuth tokens.
Source descriptions of scope and duration alone do not establish that one path is
universally safer. Existing token-opacity and secure-state requirements remain in force;
none of these upstream caches or fallback behaviors is imported into V2.

### Personal Accounts and Evidence Limits

**Source finding:** The pinned provider's
[`AzureArtifacts` builder](https://github.com/microsoft/artifacts-credprovider/blob/bca6c32fdb9611aea25819147ef4508f730aa5fb/src/Authentication/AzureArtifacts.cs#L18-L70)
enables broker MSA passthrough, and
[`MsalExtensions`](https://github.com/microsoft/artifacts-credprovider/blob/bca6c32fdb9611aea25819147ef4508f730aa5fb/src/Authentication/MsalExtensions.cs#L32-L53)
handles MSA accounts and the transfer-tenant workaround. The subsequent direct-token
branch tests the configuration option, not the account type. The plugin's `EntraToken`
label is not an organizational-account selector. Its production application registration
also differs from the Visual Studio registration used in the accepted Windows probe;
the source does not enable that newer registration as a V2 Profile.

**Inference:** Personal-account selection does not by itself require a SelfDescribing
exchange. Both branches first need a usable MSAL access token. This conclusion depends
on an eligible client/authority path, including the documented legacy MSA compatibility
boundary; it does not establish personal-account support for arbitrary Entra applications.

| Question | Current evidence and limit |
| --- | --- |
| Can the selected personal-account compatibility path acquire an access token and use it directly for ADO Git? | The [accepted Windows observation](#observed-msa-token-git-discovery-and-silent-reuse) records exact-account token acquisition, successful authenticated Git discovery, and later-process silent reuse without PAT or session-token exchange. It is bounded to that existing host/account-state/target scenario. |
| Does the official NuGet provider expose direct access-token presentation? | Yes, in the pinned source and released-source revision above. It passes the token through a Basic credential response and does not branch on personal versus work account. |
| Has this project's personal-account token been used through NuGet Basic authentication against an Artifacts feed? | No such observation exists. Git bearer-token success and the shared resource/scope do not themselves demonstrate this downstream path or a package operation. |
| Has SelfDescribing exchange been shown necessary for personal accounts? | No. Neither the inspected branch nor the accepted observation establishes that requirement. Absence of a NuGet/feed observation must not be converted into either a forced-exchange rule or a claim of validated direct-feed access. |

**Architectural inference:** Azure DevOps Git and Azure Artifacts can use the same engine
capability for explicitly requested personal or work accounts. Artifacts authentication
does not need a separate deferred engine mechanism. Compatible account, Profile, tenant,
resource, and scope contexts can reuse eligible state; identical resource IDs alone do
not make different account or tenant requests interchangeable. Git and package adapters
retain service authorization, protocol handling, and any derived-credential lifecycle
under [decision 0004](../../decisions/0004-keep-the-authentication-engine-separate-from-consumers.md).
This conclusion includes Artifacts token acquisition in the same authentication scope;
it does not claim that the CLI implements NuGet credential exchange or that a token alone
grants repository/feed access. The [architecture boundary](../../architecture/overview.md#system-boundary)
owns the selected direct-access-token design;
[V2-REQ-005](../../product/requirements/product-boundary.md#v2-req-005-no-personal-access-tokens)
owns the PAT prohibition. NuGet presentation and
feed behavior remain explicitly scoped [validation obligations](../../validation/strategy.md#platform-matrix).

**Refinement recheck assessment:** All seven registry entries were evaluated. This
correction selects no new Profile, account contract, interaction policy, cache design,
or host path, and changes neither the Wave nor a release; no new trigger fires.
RECHECK-001/002/006 retain their accepted interaction/account/state dispositions;
RECHECK-003/004/005 retain the accepted WSL and unselected Linux/browser boundaries;
RECHECK-007 retains the current client/tenant assessment and bounded MSA evidence.
The immutable source findings above do not claim a fresh mutable-source status or a
new platform-support result.

This assessment executed no application, authentication, cache access, or resource request.
It adds no corporate-account or feed observation and does not extend the completed
personal-account probe's capacity or evidence.
