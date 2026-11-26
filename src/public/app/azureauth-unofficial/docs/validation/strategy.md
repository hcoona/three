# Validation Strategy

This record defines the evidence required before v2 can claim support. The Windows Slice
is implemented and its [Slice ledger](#windows-slice-requirement-to-evidence-ledger)
records its evidence; Slice closure is not release readiness. No release currently
satisfies the [release gates](#release-gates).

Scenario tests provide the main coverage of business behavior. Focused unit and contract
tests protect core rules and public behavior, while targeted real-environment tests cover
properties that simulation cannot establish.

## Test Design and Evidence Selection

Organize most business-behavior coverage around the user journeys and the account,
interaction, reuse, and failure scenarios below. Exercise the application boundary with
controlled dependency substitutes where practical, and assert caller-visible outcomes
and required side effects. Scenario tests need not contact a service, open real UI, or
run the entire deployed system.

Use focused unit tests for core algorithms and functions whose correctness warrants
precise protection, such as strict account matching, scope satisfaction, terminal versus
retryable classification, and deadline transitions. Protect serialized public behavior
with contract tests. Avoid locking ordinary orchestration code to private method calls,
mock call counts, class layouts, or incidental internal ordering. Required ordering, such
as selected-account silent acquisition before interaction, remains observable behavior to
test. A behavior-preserving refactor should not require rewriting business expectations.

The layers and matrices below describe coverage responsibilities, not a requirement to
repeat every case at every test level. Choose the least costly level that establishes
the property. Rely on documented dependency contracts for behavior delegated to that
dependency; test the engine's use of the contract and the integration assumptions that
could change an architectural choice. Do not attempt to re-prove an operating system or
provider through an exhaustive mock suite.

Use public desk evidence when it answers a decision-relevant question. Real broker,
browser, secure-store, and WSL behavior still require the applicable bounded observations
before support is claimed; simulated success cannot supply that evidence. This strategy
does not waive the primary-journey gate, required rechecks, experiment authorization, or
accepted protocols. Evidence depth should reflect the decision, credible failure, and
cost, rather than an arbitrary unit-test count or coverage target.

For architecture work, start from V1's existing integrations and the
[V1-to-V2 delta assessment](../research/v1-public-contract-baseline.md#architecture-reuse-and-remaining-deltas).
Use relevant public runtime experience with its reported scope, alongside source and
dependency contracts; local repetition is not a prerequisite for learning from an
existing product. Focus new observations on a changed assumption that can alter the
decision. This avoids repeating established mechanism checks while preserving the
V2-specific primary-journey and release evidence below.

Owner-operated observations on existing machines are valid evidence when collected
under an accepted [experiment protocol](../research/experiment-safety.md#experiment-authorization).
Record relevant prior state, manual steps, actual host, attempt history, and retention
limits. An existing-state result must not be described as a clean first-use result;
manual assistance cannot substitute for a capability the product itself must provide.

## Primary Journey Gate

The [personal Azure DevOps Git journey](../product/user-stories.md#primary-journey-personal-azure-devops-git-access)
is a first-release blocker: it blocks the first supported, non-prerelease release.
Unsupported `0.1.0-alpha` prereleases are exempt by
[owner decision](https://github.com/hcoona/microsoft-authentication-cli/pull/549#issuecomment-6510282012).
Before that release, bounded evidence must demonstrate that an
external adapter can request the selected personal Microsoft account's Azure DevOps
access token without silently receiving the corporate default account, and that usable
state enables strict selected-account silent reuse across invocations.

The proof concerns the engine's authentication boundary under
[V2-REQ-005](../product/requirements/product-boundary.md#v2-req-005-no-personal-access-tokens),
including independence from PAT prerequisites or fallback. Git protocol and repository
remote parsing remain outside this boundary. The adapter supplies a Client Profile, full
email,
Azure DevOps scope, interaction permission, and protocol version. It is not evidence
that WAM must implement this path or that any profile or platform is already supported.

The unresolved obligations include:

- a suitable external client registration and bounded MSA/Azure DevOps behavior under the
  [external-client-profile gate](../product/compatibility-and-migration.md#externally-owned-client-profile-gate)
  and `RECHECK-007`;
- real provider-account enumeration and authoritative email metadata sufficient for
  pre-resolution and final validation, including alias and same-email visibility limits;
- repeated silent reuse without an identity-opaque operating-system default;
- secure persistence, safe recovery, and accurate persistence warnings for the exact
  supported combination.

The [bounded Windows probe](../research/experiments/windows-msal-account-metadata.md#observed-msa-token-git-discovery-and-silent-reuse)
now demonstrates unique exact-account resolution, matching token-result metadata,
authenticated Git discovery, and one fresh-process silent reuse in existing state.
This is mechanism evidence for that configuration, not completion of the engine/adapter
release gate, first-use/alias coverage, cross-consumer reuse, or persistence-failure tests.
No experiment is required merely to state desired behavior; execution requires separately accepted authorization
and a protocol under [experiment safety](../research/experiment-safety.md).
This strategy records proof obligations, not an executable experiment protocol.

## Validation Layers

The [architecture allocation](../architecture/overview.md#user-goal-allocation) and
[request lifecycle views](../architecture/request-lifecycle.md) identify the application
boundaries and paths exercised by these scenarios. Their diagrams are design views, not
evidence that a mechanism or platform passes the corresponding tests.

### Contract Tests

- Explicit command-line request versioning; unsupported majors and invalid arguments
  rejected before authentication.
- One request and one terminal outcome per native authentication process.
- Stable versioned structured stdout for success and failure, including invalid requests;
  prompts, usage text, and diagnostics separated; no alternate authentication output mode.
- Required fields, invalid combinations, and unknown-enum behavior. The explicit
  `force-interactive` value is additive; existing interaction meanings and result/exit
  shapes remain unchanged. An older executable rejects the new value without an
  automatic downgrade by the caller.
- Explicit Client Profile selection, including missing selection with exactly one
  available profile; no implicit/default selection or inline full client configuration.
- Equal interpretation and validation of pre-distributed and user-provided profiles.
- Explicit email, scopes, and interaction permission; no environment-supplied intent,
  scope/resource presets, separate resource input, or request-level mechanism order.
- Fixed profile and trust conflicts rejected; only applicable product deadline and tenant
  defaults applied.
- Zero exit status for success, including persistence warnings, and one stable nonzero
  value for every normally emitted typed failure, consistent with the payload. Once V2
  observes the complete result written, a later cleanup, watchdog or diagnostic failure
  keeps that status; any other status means no complete result was guaranteed.
- Complete success metadata under
  [`V2-REQ-031`](../product/requirements/result-and-process-protocol.md#v2-req-031-complete-success-metadata),
  one opaque access token, and no public stable account ID or reusable credential artifact.
- Exactly the caller-action outcomes under
  [`V2-REQ-032`](../product/requirements/result-and-process-protocol.md#v2-req-032-caller-action-failure-taxonomy);
  consent and transient origins confined to safe reason details.
- Synthetic token/code and email redaction, including stable email-derived hashes,
  nested exceptions, diagnostics, and telemetry; permitted request/success email channels
  distinguished from prohibited diagnostic propagation. The same applies to the other
  account and host identifiers under
  [`V2-REQ-036`](../product/requirements/result-and-process-protocol.md#v2-req-036-account-and-host-identifier-privacy).
- Unchanged adapter fixtures across engine updates for every still-supported protocol
  major under
  [`V2-REQ-037`](../product/requirements/result-and-process-protocol.md#v2-req-037-supported-protocol-compatibility).
  A breaking change requires a different major, not a reinterpretation of an old one.

### Policy Tests

- [V2-REQ-005](../product/requirements/product-boundary.md#v2-req-005-no-personal-access-tokens)
  applied to personal- and work-account scenarios: successful delegated acquisition
  needs no PAT input or bootstrap and produces no PAT credential or PAT persistence.
- After permitted delegated paths are exhausted or a terminal failure occurs, return the
  applicable existing outcome without a PAT prompt, exchange, or fallback. Cover silent-only
  exhaustion, denial, unavailable mechanisms, and invalid configuration. Request/Profile
  settings and inherited provider defaults, including a Compact-token default, cannot
  enable PAT behavior. Verify provider-operation semantics and observable effects;
  do not classify credential types by parsing or scanning opaque token contents.
- The no-PAT rows are satisfied by the absence of any PAT path: no request option,
  Profile field, provider configuration or code path solicits, accepts, creates,
  exchanges, persists or returns a PAT, confirmed by source review, plus contract cases
  that reject PAT-like options (for example `--pat` or a PAT-valued Profile field) as
  unknown input with `invalid_request` before provider work. No separate PAT scenario
  per failure mode is required while no PAT path exists.
- Versioned product order, profile compatibility filtering without reordering, and
  mandatory silent-first acquisition after unique real-account pre-resolution for the
  two ordinary modes. Explicit forced interaction instead skips silent acquisition and
  requests the provider interactive path with supported account selection after the same strict resolution.
- Forced interaction with usable state, zero matches, and duplicate exact matches:
  retain strict selection/validation and at most one interactive call that supplies
  neither the discovered account nor a login hint. A different account chosen in the
  provider picker fails final validation. No fabricated
  silent failure, engine silent call/fallback, cache clearing, sign-out or second attempt.
  An unsupported forced sign-in route returns unavailable. Provider session
  reuse inside the interactive call is allowed; its result is not visible-UI evidence.
- Host capability discovery never changing caller intent.
- No authentication or state-unlock interaction under every no-interaction request shape.
- Terminal versus retryable failure classification.
- Caller cancellation, user denial, strict identity mismatch, and reported-success
  validation failure remaining terminal.
- Request-local provider claims handling preserving every original constraint; no public
  resource/CAE continuation or `cp1` advertisement.
- Full-string case-insensitive email matching, without alias/domain inference, Account
  Kind, stable-ID selection, or hidden first-account binding.
- No match permitting only policy-allowed interaction; multiple visible matches remaining
  ambiguous without a wrong-account silent attempt.
- For `interactive-if-needed`, the interactive call carries the resolved account or the
  requested email as login hint when supported; for `force-interactive` it carries
  neither. Final authoritative validation applies regardless of hint use and rejects any
  other returned account.
- Fixed single-tenant policy, eligible multitenant/MSA `common` default, compatible exact
  token/resource-tenant GUID, B2B home/resource-tenant distinction, and no exact-to-common
  fallback or email-domain inference.
- The [provider mapping](../architecture/client-application-identity.md#provider-mapping)
  preserving normalized intent: ordinary `common` requests do not gain legacy passthrough;
  the eligible legacy compatibility path can use its documented routing while preserving
  `common` and strict identity checks. For an MSA account with an explicit resource tenant,
  both silent and interactive operations retain that exact tenant, including across
  permitted fallback. Wrong-tenant results fail validation; routing metadata never
  substitutes for actual result metadata. Fixed-tenant conflicts fail before acquisition.
- Dynamic permission coverage with provider extras allowed; `/.default` resource/result
  association without literal returned-scope matching or access-token parsing; rejection
  of mixed dynamic permissions and `/.default`.
- One finite total deadline across resolution, locks, state, authentication, retries,
  fallback, validation, and persistence; bounded caller override, no profile default or
  timer reset.
- Host combinations that require an external UI owner or raw platform handle remaining
  unavailable under the native CLI contract.
- Unusable state treated as a miss and never consumed; recovery preserving request
  constraints, interaction permission, and the original deadline.
- Compatible OS sign-in state considered on first engine use without a prior V2 sign-in
  or V2-created cache; strict pre-resolution and terminal validation still required.
- A change of consumer, package ecosystem, or working repository alone not causing
  another sign-in when valid, safe state can satisfy the otherwise compatible request.
- Validated result delivery not waiting for persistence; success carries a
  machine-readable warning when safe persistence fails or is not confirmed complete.
  No plaintext fallback or caller/profile cache modes.
- Network telemetry remaining disabled until explicitly configured, and export or flush
  failure leaving the authentication result and process status unchanged within a finite
  shutdown bound.

### Mechanism Tests

- Selected-account silent acquisition.
- The exact accepted Profile/host's ordinary, legacy-passthrough, and explicit-tenant
  paths where applicable. Existing legacy `organizations`/transfer observations do not
  establish direct `/common` passthrough, exact-tenant MSA behavior, or arbitrary client
  registration eligibility. Use dependency contracts first and bounded observations only
  for remaining integration uncertainty; no all-platform cross-product matrix is implied.
- Rejection of identity-opaque operating-system-account silent acquisition.
- Broker interactive acquisition.
- System-browser acquisition.
- Device-code acquisition.
- Secure state read/write, unreadable/undecryptable/corrupt/incompatible state recovery,
  and persistence-failure separation from acquisition success.
- Concurrent access to shared cache state preserving locking and update integrity.

These are future mechanism evidence obligations, not a selected implementation list.
The first version has no Logout, Cache Clear, Force Refresh, or Account List tests as
supported operations; contract tests instead verify that they are not exposed.
Cross-process interaction single-flight is not an acceptance condition.

### Real Environment Tests

Real broker and host behavior must be exercised on supported systems. Mocked MSAL builders
cannot prove UI ownership, account picker behavior, keyring integration, or cancellation.
Broker tests must stay within the environment and account-state boundaries of the
accepted Wave and experiment protocol under
[`../research/experiment-safety.md`](../research/experiment-safety.md); cache-directory
separation alone does not isolate OS accounts.

## Account-State Matrix

| State | Required observations |
| --- | --- |
| Empty application cache | Resolve visible real provider accounts before any silent attempt; absence cannot trigger ambient silent acquisition. |
| First V2 use with compatible OS sign-in state and no V2-created cache | Consider the OS state without requiring prior V2 sign-in; resolve the unique requested real account before silent acquisition and validate every success postcondition. |
| First V2 use with only opaque or incompatible OS state | Do not substitute the OS default or assume resource authorization; preserve account resolution, typed outcomes, and interaction permission. |
| One exact visible account with usable state | Ordinary modes attempt that account silently first; forced interaction requests account selection through the interactive path without engine silent acquisition. Success reports provider-observed email, not a stable ID. |
| Multiple visible accounts with one full-email match | Only the unique matching account is eligible for silent acquisition. |
| No matching account | No silent fallback to another account; interaction-required handling follows request permission. |
| Corporate OS default differs from requested personal email | Default is not substituted; identity-opaque OS-account acquisition is never used. |
| Multiple visible accounts with the same email | Account ambiguity is terminal; no hidden binding or Account Kind disambiguation. |
| Aliases, missing provider email, or non-enumerated accounts | Different aliases do not match; unverifiable success fails; document visibility limits without claiming detection of hidden duplicates. |
| Guest and home-tenant representations | Exact selector checks the token/resource tenant, not the account home tenant. |
| Microsoft account and work account | Behavior is recorded per client application/resource, without an independent public Account Kind postcondition. |
| Usable state on a subsequent invocation | Strict pre-resolution recurs; the explicit interaction mode determines engine silent reuse versus explicitly interactive acquisition without relaxing identity or storage policy. |

### Account-Context Variants

The following are variants of the [requested-account goal](../product/user-stories.md#account-context-variants),
not separate stories or an automatic supported-platform or first-release matrix:

| Computer account context | Requested account | Evidence focus |
| --- | --- | --- |
| Company account | Company account | Select the requested email, not an assumed company default. |
| Company account | Personal account | Preserve the primary wrong-default scenario and its first-release gate. |
| Non-company account | Company account | Do not substitute the computer's non-company default. |
| Non-company account | Personal account | Do not assume that the selected email is the computer's default personal identity. |

For any combination selected for support, record default-account relation, target-account
state, and device-management, join, and compliance context independently. Exercise first
engine use with OS state separately from later engine-state reuse. Account labels alone
are not evidence of device state or service eligibility.

## Cross-Consumer Reuse Scenarios

These scenarios validate
[`V2-REQ-041`](../product/requirements/cache-security-and-operational-identity.md#v2-req-041-safe-reusable-state-recovery-and-concurrency)
for [package consumers](../product/user-stories.md#reuse-authentication-across-package-ecosystems-and-repositories)
and other compatible callers. They do not select a cache architecture or credential
translation path.

| Scenario | Required evidence |
| --- | --- |
| Sequential compatible calls from different package ecosystems or working repositories with valid, safe reusable state | Changing only the consumer context does not cause another user sign-in. Each result independently satisfies strict account, tenant, profile, resource, and scope constraints. |
| Concurrent compatible calls with reusable state already available | State remains consistent and updates retain integrity; consumer differences alone do not cause another sign-in. Lock waiting and acquisition stay within each request's original deadline. |
| Concurrent calls when usable state is absent or insufficient | Each call obeys its interaction permission, classified fallback, and deadline. Do not require a cross-process single prompt or identical token bytes. |
| Changed account, tenant, profile/cloud, resource/scopes, or security context | Prior success in another context is not sufficient for reuse or result validity. Verify the current request's constraints rather than widening them to consume state. |
| Missing, locked, corrupt, incompatible, or unpersisted state | Unsafe state is not consumed, no plaintext fallback occurs, and recovery preserves interaction permission and deadline; validated success with a persistence warning does not promise state will be reusable later. |

Observe engine requests, validated results, user-facing interaction, and safe state
outcomes rather than treating equal token bytes as proof of reuse. Package-ecosystem
credential materialization, session-credential exchange, and derived-credential lifecycle
are downstream concerns, not engine acceptance criteria. Public implementation examples
do not establish cross-consumer interoperability.

## Generic Caller Scenarios

The [direct-service](../product/user-stories.md#direct-protected-service-access) and
[integrated-tool](../product/user-stories.md#authentication-integrated-into-a-remote-tool-workflow)
goals consume the existing delegated public-client and process boundary, not new engine
protocols. Planned contract and policy fixtures cover:

- a direct caller supplying an explicit profile, selected email, intended resource
  scopes, interaction permission, and protocol version, receiving only a validated
  structured access-token result and authoritative metadata;
- an external tool integration making that same engine request and handling the typed
  result without an engine-owned service connection or consumer-protocol implementation;
- wrong or missing identity, incompatible target/profile, insufficient authoritative
  scope metadata, and prohibited interaction producing the existing constrained failure
  behavior rather than an unvalidated token or broader fallback;
- repeated compatible requests exercising the reuse scenarios above, with token
  application and any consumer-specific credential translation left downstream.

Mocks can validate this engine boundary without contacting a protected service or
implementing an adapter. Actual profile/resource/account eligibility and real-platform
reuse require separately authorized bounded evidence before support is claimed. These
goals do not imply service authorization for every user identity, personal-account
support for every resource, an MCP engine protocol, or a new first-release support matrix.

## Interaction Matrix

For every supported mechanism:

- no-interaction request;
- interaction allowed after silent miss;
- explicitly interactive acquisition despite usable state; record provider UI separately
  and preserve unavailable observations as incomplete;
- user cancellation;
- user denial;
- consent required;
- provider-local claims challenge;
- prompt timeout;
- interactive-surface owner or completion channel unavailable;
- external browser left open after request termination.

No-interaction tests must detect broker, browser, device-code, terminal, and state-unlock
surfaces, not merely an absent token. Cleanup observations distinguish controlled UI from
external surfaces that cannot be closed, prove that a late response cannot resume an
ended request, and do not claim reversal of independently completed provider sessions.

## Platform Matrix

| Platform or host | Required decision and evidence |
| --- | --- |
| Windows native | WAM availability, selected account, v2-owned interaction context, cancellation, cache, and multi-account behavior. |
| WSL directly invoking the Windows CLI | Caller-selected executable, interoperability availability, unchanged CLI contract, Windows configuration and path interpretation, account selection, UI ownership, cancellation/disconnect, timeout, and confidential result transport. |
| WSL with native Linux broker, outside the selected deployment | A separate selection would require WSL version, broker package, native dependencies, keyring state, account UI, and failure-mode evidence. |
| Linux headful | System browser, callback, secure store, and cancellation. |
| Linux headless | Device code, no-browser behavior, secure-store absence, product-owned secure-state policy, and cross-invocation reuse for any claimed repeated-noninteractive capability. |
| macOS | System browser, Keychain, and broker behavior if declared supported. |

This is an evidence-planning matrix, not a selection of supported platforms or mechanisms.
The first supported release may choose a smaller matrix while satisfying the primary
journey gate. Unsupported combinations must be explicit and fail safely.

The [selected WSL deployment](../architecture/overview.md#deployment-wsl-caller-and-windows-cli)
requires scenario coverage for both a requested personal account accessing a personal
Azure DevOps repository and a requested work account accessing a company repository.
Exercise a different available/default account, exact resource-tenant constraints,
silent reuse, interaction forbidden/allowed, and wrong-account or wrong-tenant rejection.
Verify ordinary CLI output reaches the WSL caller without prompt or diagnostic mixing.
Missing executables and disabled interoperability remain launch failures; there is no
automatic Linux fallback. Cancellation and caller disconnection must be assessed at the
Windows process boundary, including the CLI's own finite request deadline.

Azure Artifacts consumes the same Azure DevOps token-acquisition capability under the
[public source assessment](../research/experiments/windows-msal-account-metadata.md#azure-artifacts-token-forms-and-nuget-paths).
Cross-consumer scenarios cover reuse with compatible account, Profile, tenant, and scopes,
and separation when any relevant context differs. Downstream integration evidence must
identify whether the adapter presents the access token directly or uses a separately
selected derived-credential path; NuGet does not universally require an exchange.
For the selected direct-token design, a claim of NuGet/Artifacts support needs the actual
account, Profile, feed, and credential-presentation path, including Basic authentication
where used. The accepted personal-account Git discovery result does not satisfy that
downstream obligation. [V2-REQ-005](../product/requirements/product-boundary.md#v2-req-005-no-personal-access-tokens)
applies throughout; a failed direct request does not authorize the upstream default
exchange or a new engine mechanism.
Feed permissions and package-manager protocols remain downstream integration evidence;
token acquisition alone does not demonstrate a successful restore or package operation.

## Failure and Resilience Matrix

- Network unavailable before and during each mechanism.
- Proxy and TLS failures.
- Authority, tenant, scope, or client misconfiguration.
- Explicit-request conflicts with enforced-profile or trust constraints.
- Unsupported, noncanonical, or untrusted authority hosts.
- Broker unavailable or unsupported.
- Browser launch or callback failure.
- Locked, missing, corrupt, undecryptable, incompatible, or permission-invalid state.
- Secure storage unavailable under the product state policy, without plaintext fallback;
  validated token plus persistence failure, safe recovery, and retained deadline.
- For a timely validated candidate, compare confirmed safe persistence, known failure,
  and unavailable confirmation: only the first omits the persistence warning. Classify
  evidence already available from the provider/state integration without additional
  I/O, acquisition, or a watcher wait at delivery. Provider success or cache timing alone
  must not fabricate confirmation. Broker reuse does not require an engine-owned copy.
- Provider failure before a candidate result is returned: classify the acquisition
  outcome without emitting a partial token or relabeling it as success with a warning.
- Acquisition and all required success validation complete with persistence still
  pending and no deadline expiry: return success, the validated token, a persistence
  warning, and zero exit status without waiting for persistence or the deadline.
- Incidental deadline expiry after acquisition and all required success validation
  completed within budget but before result delivery: accept either validated success
  or timeout under `V2-REQ-015`, without requiring a particular completion ordering.
  Success carries the token, zero exit status, and a persistence warning if safe
  persistence failed or is unconfirmed; timeout carries no token and the common nonzero
  exit status. Neither outcome extends the deadline or leaves V2-controlled persistence
  work running after the request ends.
- Acquisition or required success validation incomplete at deadline expiry: return
  timeout without a token and with the common nonzero exit status; late results cannot
  resume the ended request.
- Process cancellation during lock wait and each mechanism, including while persistence
  is pending after validation; persistence status cannot override cancellation.
- Dependency exception not recognized by the policy layer.
- Process output overflow, malformed output, or diagnostic contamination.

Operating-system kill, incomplete output, and malformed output must be distinguished by
consumer validation from a normal typed failure; they do not justify fabricating a
complete result. The
[2026-09-09 secure-store desk outcome](../research/v1-public-contract-baseline.md#recheck-006-secure-store-availability)
is not evidence that the V2 failure/recovery matrix passes.

## Windows Slice Design Acceptance

The [concrete Windows design](../designs/windows-ado-authentication.md) and
[protocol/Profile schemas](../../contracts/v1/request.schema.json) select the bounded
design for the two Azure DevOps account journeys. This section owns its scenario evidence
basis. A schema-valid example is not an executed CLI request, a passing authentication
scenario, or Profile/support acceptance.

Before design acceptance, validate every schema against its metaschema, validate embedded
synthetic examples with JSON Schema 2020-12 and format checking, and exercise negative
shape cases: missing explicit intent, unsupported versions/enums, duplicate scopes,
deadline limits, conflicting legacy Profile identity/tenant policy, and credentials in a
failure result. Check additive result-field tolerance separately from strict input
readers. Review the semantic cases below alongside those checks; JSON Schema does not
prove resource equivalence, tenant precedence, provider behavior, or lifecycle correctness.

| Scenario | Required observable outcome | Evidence before the corresponding claim |
| --- | --- | --- |
| Personal request with a different corporate OS default | Unique matching real account is used silently first; permitted interactive completion must still return the exact requested email. No PAT bootstrap or default-account request. | Orchestration scenario plus bounded Windows personal-account integration before Profile/support acceptance. Existing .NET 8 evidence is limited to its recorded setup. |
| Work request for company Azure DevOps | Requested work email and, when supplied, exact resource tenant are retained through silent/interactive acquisition and final validation. | Orchestration scenario; bounded work-account integration before claiming that combination. Company repository access is a separate downstream test. |
| First use and later invocation | Discovery considers eligible broker accounts without a prior V2 cache. Later compatible invocation attempts selected-account silent reuse. | Scenario doubles for ordering; real first-use/existing-state Windows evidence for availability and UI behavior. |
| Git followed by Artifacts acquisition | Changing the downstream consumer alone does not force another sign-in. Different account/client/tenant/resource contexts are not substituted. | Two-process broker integration; actual Git and feed/package operations remain downstream evidence. |
| No match, duplicate match, and alias-only visibility | No match follows permission policy; duplicate exact matches stop as ambiguity; an alias or missing email does not pass identity validation. | Scenario tests of calls and outcomes; unit tests only for the pure matching rule. |
| Tenant and scope normalization | Fixed or explicit tenant wins over legacy MSA routing. `common` uses the selected integration's mapping. Mixed resources/default-plus-dynamic scopes fail admission; dynamic coverage uses provider metadata. | Table-driven core tests plus scenario tests that preserve constraints through both calls. |
| Wrong/missing provider metadata | No token reaches stdout after wrong email, exact-tenant mismatch, missing or all-zero actual tenant, insufficient scope coverage, or a mismatched operation/client context. | Candidate-validation scenarios and core validation tests; real result metadata coverage before activation. |
| Noninteractive request in locked or expired state | No owned UI, broker interaction, browser, device-code instruction, or unlock prompt is initiated. Return the applicable interaction/unavailability outcome. | Scenario call assertions and attended real-platform no-UI observation; do not infer this solely from API names. |
| Broker unavailable or disappears between checks | Return `mechanism_unavailable`; rejecting custom web UI prevents automatic browser fallback. | Provider/host scenario, then bounded Windows failure test before support. |
| Local Windows host admission rejects or cannot observe required state | Return the existing unavailable outcome before real provider initialization, with no account discovery or owned UI. Cover rejected OS/client/architecture, thread impersonation or query failure, service/batch/new-credentials logon, station-user mismatch, disconnected/unobservable session and non-input desktop; retain a valid ordinary-user path. | Scenario fixtures at the local-observation/provider boundary; inspect actual native ABI and bounded resource ownership. Real-host observation of own-logon metadata access follows the experiment policy. |
| Cancellation or host eligibility changes around provider/UI effects | Original cancellation/deadline wins; failed volatile rechecks prevent the next effect and close any owned parent. Broker false or recognized initialization failure permits no discovery or browser fallback. | Controlled scenarios for cancellation after a local check, loss of session/input before interaction and initialization failure. Bind checks to the actual request/UI threads in source review; real session rejection claims need their own bounded observations. |
| User cancellation, denial, or another claims challenge | Stop within the same request; no alternate account, tenant, registration, mechanism, PAT, or repeated interaction. Only current silent-challenge data may reach the permitted interactive call. | Scenario outcomes and provider-call trace; bounded UI cancellation/denial evidence. |
| Explicit `force-interactive`, including when silent reuse would succeed | Perform strict account discovery, no silent acquisition, and one provider interactive operation through the sign-in/account-selection route supplying neither the discovered account nor a login hint, using the same Profile/tenant/scopes and hidden parent. Whether WAM presents interactive UI is provider behavior recorded among the design's known limitations, with finding F1. A different account chosen in the picker fails final validation. A successful result reports the invoked path as `interactive`, not proof of visible UI or renewed credentials; unavailable semantics, cancellation/denial and identity failure stay terminal. No cache management or changed token-byte/persistence guarantee. | Contract examples and controlled policy/adapter scenarios, pinned MSAL/Broker/native source review, and actual WAM/UI/cancellation/EOF evidence before support. Existing silent-success evidence does not establish this mode. |
| Lifetime pipe closes before discovery or during WAM | Cancel without late success, destroy the hidden parent, end the process, and expose no token. Reject console/file stdin when the pipe flag is selected. | Deterministic lifecycle scenario and actual WSL-to-Windows pipe integration, including writer-handle inheritance. |
| Linux caller dies without the optional lifetime pipe | Do not assert Linux signal delivery to Windows. Windows work ends through detected transport failure or the original finite deadline. | Bounded WSL process-lifetime test; no immediate-disconnect support claim for this combination. |
| Deadline during Profile reading, discovery, silent, interaction, or validation | Original deadline is never restarted. Incomplete validation yields timeout; late candidates cannot resume. | Clock-driven scenarios and bounded real host tests for dependency cancellation and process exit. |
| Blocked stdout/stderr or uncooperative completion | The shutdown watchdog ends the one process within the defined local shutdown allowance. Incomplete output is a transport failure, never a complete typed result. A result already written completely keeps its typed exit status when cleanup, drain or diagnostics force that termination. | Controlled process/pipe scenario before shipping; no tests using real credentials are needed for this transport condition. |
| Validated token with no broker persistence receipt | Return success with `persistence_unconfirmed`, no readback, extra acquisition, write, or persistence wait. Next invocation may still reuse usable state. | Scenario and real reuse evidence; do not infer durable-write confirmation from provider success. |
| Concurrent invocations | Broker integrity is preserved through supported APIs; no app-owned cache or locking is introduced. Duplicate prompts are not suppressed by contract. | Concurrent Windows integration before that support claim; trust broker internals within their documented boundary. |
| Explicit Profile file and compatibility candidate | Same parser for user-provided and eventually provisioned files; no implicit selection, hot reread, external URL, inherited configuration, or upstream cache import. Show external ownership without claiming Microsoft endorsement. Profile name and owner reject control and Unicode format characters. | Admission scenarios, contract examples, and later bounded consent/audit/branding validation under the external Profile gate. |
| Unofficial identity presentation | CLI help states that the project is unofficial; before an interactive call, a best-effort bounded stderr indication names the unofficial project and the Profile's name and owner; the fixed broker title is set. No per-use confirmation. | Help text asserted in a Windows process scenario; stderr indication asserted in Windows scenarios; configured broker title asserted in a Windows construction scenario (executed in the unfiltered Windows scenario run of the [real-host results](../research/experiments/real-host-validation.md#results)); caller display of stderr and WAM title presentation are not claimed. |
| Output and optional local telemetry | One complete result with matching 0/1 status, or recognizable transport failure. Only validated success exposes a token; stderr has no token/email/identifier/provider text. Local telemetry opt-in, overflow, or sink failure cannot alter authentication. | Contract and orchestration scenarios with synthetic secret markers; no production credential fixture. |
| Local OpenTelemetry-compatible observation | Owned request/phase activities have explicit parentage; phase starts are observable before an awaited operation finishes. Only allowlisted local observations are emitted. Ambient activity, outgoing HTTP headers, ordinary result/exit, cancellation and process bounds are preserved. | Controlled listeners and safe-sink scenarios with synthetic private markers, listener/sink failure and overflow, parentage and ambient-context checks; exact Windows artifact/lifetime checks before dependent real use. Telemetry is not an independent proof of process exit, visible UI, normal stdout or actual WSL credential transport. |
| Token-shape validation observation under [`V2-REQ-038`](../product/requirements/result-and-process-protocol.md#v2-req-038-local-token-shape-validation-observation) | Runs only with `--telemetry stderr` and only after the complete success result is written to stdout; a failed result write is followed by no observation and no completion indication. Emits fixed categories plus provider-reported expiration and remaining lifetime only; opaque, malformed, duplicate-member or oversized tokens cannot change success or expose token material. | Code-ordered delivery (stdout before diagnostics, source review); Windows scenarios assert opt-in field presence/absence (executed in the unfiltered Windows scenario run of the [real-host results](../research/experiments/real-host-validation.md#results)); Core scenarios cover opaque/duplicate/oversized interpretation. |

The [research evidence for the Windows Slice](../research/experiments/windows-slice-validation.md#windows-slice-dependency-and-host-contracts)
owns each observation, its exact candidate/request binding and its limits. This
strategy owns only the mapping below from scenarios to that evidence; it does not
restate the observations.

### Windows Slice Requirement-to-Evidence Ledger

An independent contextual review at `main-v2` `e789c64` mapped each scenario above
to accepted evidence; the 2026-10-15 review remediation (Issue #474) refined rows whose
evidence covers only part of the scenario, and the 2026-10-17 remediation
([Issue #509](https://github.com/hcoona/microsoft-authentication-cli/issues/509)) added
requirement IDs, test names and where each test executes. "Partially satisfied" names the
observed sub-condition and the sub-condition that remains unobserved.

Evidence locations: **Linux CI** is the GitHub Actions job `linux-scenarios` (Linux
Scenario Tests), which runs `tests/Authentication.Scenarios`. **Windows CI** is the job
`windows-scenarios` (Windows Scenario Tests), which runs
`tests/Authentication.Windows.Scenarios` on `windows-latest` excluding
`WindowsHostTiming` and the published-executable cases (see
[Continuous Integration](#continuous-integration-and-host-timing-evidence)). **S1-1** is
the recorded unfiltered Windows scenario run on the designated host at subject `3ae2554`
(186 of 186 passed, including `WindowsHostTiming`), and **S1-2** its run against the
published executable, both in the
[real-host results](../research/experiments/real-host-validation.md#results). **HT-1** is
the [2026-10-18 host-timing run](#recorded-host-timing-runs) of the same project on that
host at `main-v2` `9f426ed`. No CI job runs `WindowsHostTiming` scenarios, so they cite
S1-1 and HT-1 only; other tests added after `3ae2554` cite Windows CI. **Real
host** names a real-provider call recorded in those results or in the
[Windows Slice record](../research/experiments/windows-slice-validation.md). Each
controlled entry states whether it exercises the real control or a substitute.

| Scenario | Requirements | Status | Evidence and exact sub-condition |
| --- | --- | --- | --- |
| Personal request with a different corporate OS default | V2-REQ-012, 013, 020, 022 | Partially satisfied | Real host: native and WSL personal acquisition and fresh-process reuse. Core `SelectedAccountScenarios.PersonalAccountReusesItsOwnStateWhenAWorkAccountIsVisible` (Linux CI; substitute provider). Not observed: the scenario's premise. The real-host records do not record the host's OS default account or its relation to the requested personal account, as the [account-context variants](#account-context-variants) require, so these calls do not establish that a different corporate OS default was present. |
| Work request for company Azure DevOps | V2-REQ-012, 019, 022 | Partially satisfied | Real host: work acquisition and reuse with `common`. Core `SelectedAccountScenarios.WorkAccountKeepsTheRequestedResourceTenantWhenAPersonalAccountIsVisible` (Linux CI; substitute provider). An explicit work-tenant GUID was not observed and is not claimed by [owner decision](https://github.com/hcoona/microsoft-authentication-cli/issues/108#issuecomment-6182996452). |
| First use and later invocation | V2-REQ-020, 041 | Partially satisfied | Real host: existing-state first engine use and fresh-process silent reuse. Clean-state first use was not observed and is not claimed by [owner decision](https://github.com/hcoona/microsoft-authentication-cli/issues/108#issuecomment-6182996452). |
| Git followed by Artifacts acquisition | V2-REQ-002, 041 | Partially satisfied | Real host: fresh-process reuse with the same request. A change of downstream consumer was not itself exercised; Git and feed operations remain downstream evidence. |
| Validated token with no broker persistence receipt | V2-REQ-031, 041 | Satisfied | Real host: `persistence_unconfirmed` success followed by fresh-process reuse. Core `ResultContractScenarios.SuccessfulRequestPreservesMetadataAndEmitsOnlyPublicFields` (Linux CI). |
| Explicit `force-interactive` | V2-REQ-013, 014, 016, 020, 022, 023 | Partially satisfied | Real host: on `3ae2554`, forced calls for both roles from native and WSL callers entered WAM's interactive path and passed strict validation; F1 and the provider-owned UI decision are [design known limitations](../designs/windows-ado-authentication.md#validation-and-explicit-unsupported-cases). Core `InteractionScenarios.ForcedInteractionSkipsSuccessfulSilentReuseAndHandsTheDiscoveredAccountToTheProvider` and `InteractionScenarios.InteractiveSuccessStillMustSatisfyTheOriginalRequest` (Linux CI; substitute provider). Windows `MsalCompositionScenarios.ForcedOperationCarriesNeitherAccountNorLoginHint` and `RealInteractiveBuilderRejectsAForcedIdentity` (Windows CI and S1-1; real MSAL builder, substitute session). Not observed: real-WAM rejection of a different picked account. |
| User cancellation, denial, or another claims challenge | V2-REQ-023, 024, 032 | Partially satisfied | Real host: human WAM cancellation. Windows `MsalAdapterScenarios.ProviderUserCancellationRemainsCancelled`, `AccessDeniedWinsOverUiRequiredAndRetryHint` and `SilentClaimsReachOneContinuationAndSecondChallengeStops` (Windows CI and S1-1; synthetic MSAL exceptions). Consent denial was not observed and is not claimed by [owner decision](https://github.com/hcoona/microsoft-authentication-cli/issues/108#issuecomment-6182996452); claims continuation has controlled evidence only. |
| Lifetime pipe closes before discovery or during WAM | V2-REQ-025, 032 | Partially satisfied | Real host: initially closed pipe and EOF during actual WAM. Windows `ProcessScenarios.AlreadyClosedLifetimePipeCancelsBeforeAuthentication` (Windows CI and S1-1; real process and pipe, substitute provider), `WriterClosureRejectsLateSuccessAndEndsTheProcess` (S1-1 and HT-1, `WindowsHostTiming`; real process and pipe, substitute provider) and `FlaggedRegularFileStopsBeforeProfileAndProvider` (Windows CI and S1-1; a regular file as stdin with the lifetime flag stops before Profile reading and the provider). `OwnedHostScenarios.CancellationDuringCreationRejectsLateParent` (Windows CI and S1-1; real Win32 window) observes the hidden parent destroyed after caller cancellation. Not observed: destruction of the hidden parent after lifetime-pipe closure, because the process scenarios' host creates no parent, and rejection of console stdin. |
| Linux caller dies without the optional lifetime pipe | V2-REQ-015, 025 | Satisfied | Real host: finite scoped Windows completion after caller-only SIGTERM. |
| Deadline during Profile reading, discovery, silent, interaction, or validation | V2-REQ-015, 025 | Partially satisfied | Real host: a typed WSL timeout at the ordinary deadline, and for the personal role only a 30-second deadline expiring while WAM was shown (native and WSL); work-role deadline cases were not executed by [owner decision](https://github.com/hcoona/microsoft-authentication-cli/pull/491#issuecomment-6208285632), and role independence is an inference. Core `RequestLifetimeScenarios.AdmissionAndProviderTimeShareTheOriginalDeadline` and `ApplicationScenarios.CallerTimeoutAppliesDuringProfileReading` (Linux CI; substitute clock and provider). Windows `ProcessScenarios.DeadlineEndsUncooperativeWorkWithinTheProcessBound` (S1-1 and HT-1, `WindowsHostTiming`; substitute provider). Deadlines during discovery or validation have controlled evidence only: Core `RequestLifetimeScenarios.CancellationOrDeadlineEndsTheRequestWhileDependencyRemainsPending` (discovery phase; its open and interactive phases add controlled coverage) and `RequestLifetimeScenarios.DelayedTimerCannotAcceptAResultValidatedAfterTheDeadline` (Linux CI; substitute clock and provider). |
| Noninteractive request in locked or expired state | V2-REQ-014, 021 | Partially satisfied | Real host: a disconnected Remote Desktop session was rejected at local admission with no UI ([locked-state observation](../research/experiments/windows-slice-validation.md#locked-state-non-interactive-observation)). Windows `WindowsHostAdmissionScenarios.InactiveOrUnobservableSessionPreventsInitialization` (Windows CI and S1-1; synthetic host observations). The native session read is not run by any scenario; its success path is observed only by real-host calls. Expired-credential broker handling was not observed and is not claimed. |
| Hidden owned parent | V2-REQ-016, 025 | Partially satisfied | Real host: the [hidden-parent observation](../research/experiments/windows-slice-validation.md#hidden-owned-parent-interactive-observation) found no exposed product window on a build whose hidden parent still had child controls; the later forced calls ran with the current childless parent and reported no unexpected window, without a UI Automation check. Windows `OwnedHostScenarios.ReadyParentIsHiddenChildlessAndNoticeCarriesAdmittedMetadata` (Windows CI and S1-1; real Win32 window, substitute provider). |
| Unofficial identity indication | V2-REQ-003 | Partially satisfied | Windows `ProcessScenarios.RootHelpCompletesWithoutAuthentication` (Windows CI and S1-1 on the managed scenario executable; S1-2 on the published executable), `OwnedProcessScenarios.DefaultHostWritesTheExactNoticeWithoutTelemetry` (Windows CI and S1-1; substitute provider) and `MsalConstructionScenarios` broker-title assertions (Windows CI and S1-1; real MSAL construction). Real host: the notice appeared before every forced call. Its visibility to users depends on the caller and is not claimed; WAM title presentation and focus are not acceptance obligations by [owner decision](https://github.com/hcoona/microsoft-authentication-cli/issues/108#issuecomment-6182996452). |
| No match, duplicate match, alias-only visibility; tenant and scope normalization; wrong/missing provider metadata; local host admission; cancellation and eligibility races; blocked output; output and telemetry allowlists; local OpenTelemetry observation | V2-REQ-012, 016, 019, 022, 027, 030, 034, 035, 046 | Satisfied by controlled evidence | Core `SelectedAccountScenarios`, `CandidateValidationScenarios`, `RequestSyntaxTests`, `ProfileSyntaxTests`, `ResultContractScenarios`, `RequestLifetimeScenarios` and `TelemetryScenarios` (Linux CI; substitute provider and host). Windows `WindowsHostAdmissionScenarios` (Windows CI and S1-1; real admission against synthetic `IWindowsHostObservations`), `LocalProviderAdmissionScenarios` (Windows CI and S1-1; substituted host admission and provider initializer), `MsalCompositionScenarios` (Windows CI and S1-1; substitute `IMsalSessionFactory` and loader-restriction delegate), `ProcessScenarios.BrokenResultReaderEndsWithTransportFailure` (Windows CI and S1-1; real process and pipes, substitute provider), and `ProcessScenarios.UndrainedResultPipeCannotKeepTheProcessAlive` and `BlockedDiagnosticsDoNotChangeTheAuthenticationResultOrKeepTheProcessAlive` (S1-1 and HT-1, `WindowsHostTiming`; real process and pipes, substitute provider). Native controls run directly in Windows CI only: `NativeObservationScenarios.ThreadIdentityDistinguishesImpersonationOnTheCallingThread` (real thread-identity read, with and without `ImpersonateSelf`), `RestrictedDllSearchExcludesTheWorkingDirectory` (real `WindowsLoader.RestrictSearch` in a child process) and `PlatformObservationReportsThisProcess` (real platform read), and `MsalConstructionScenarios.UnavailableBrokerStopsSessionCreationWithItsFixedCategory` (real session factory, only the `IsBrokerAvailable` answer replaced). The native logon-kind, session and desktop reads are run by no scenario; their success path is observed only by real-host calls passing admission. |
| Token-shape validation observation | V2-REQ-038 | Partially satisfied | Core `TelemetryScenarios.ExpectedClaimsInTheirExpectedTypesAreReportedPresent`, `InterpretationDoesNotChangeOpaqueTokenSuccessOrExposePayload` and `InvalidOrUnexpectedClaimsHaveOnlyFixedDiagnosticInterpretation` (Linux CI). Windows `OwnedProcessScenarios.DefaultHostQueuesTheNoticeBeforeTheCompletionIndication` (field present) and `DefaultHostWritesTheExactNoticeWithoutTelemetry` (field absent) (Windows CI and S1-1; synthetic token). Stdout-before-diagnostics ordering is source review; `ProcessScenarios.BrokenResultReaderEndsWithTransportFailure` asserts that a failed write emits neither. Real-token field values were excluded. |
| Broker unavailable or disappears; concurrent invocations | V2-REQ-032, 041 | Outside Slice closure | Retain their "before support" claim gates. |
| Explicit Profile file and compatibility candidate | V2-REQ-018, 042 | Outside Slice closure | Consent, audit and branding remain under the external Profile gate. The prerelease package bundles the candidate as an explicit opt-in file only under decision 0003's owner exception; Core `BundledProfileTests` (Linux CI) assert that the bundled file equals the schema candidate and that the product reader accepts it. |
| Minimal deployment without development-machine dependencies | V2-REQ-055 | Outside Slice closure | Retains the deployment support claim gate. |

The local host predicates do not prove continuing eligibility, every alternate-launch
origin or WAM health. Do not force service, account, desktop or logon-state changes
merely to manufacture rejection evidence.

### Continuous Integration and Host-Timing Evidence

Every CI job below restores from public package sources with the committed lock files in
locked mode and uses the pinned SDK. CI runs these jobs alongside the schema and record
checks:

- **Linux Scenario Tests** restores, builds and runs the platform-neutral
  `tests/Authentication.Scenarios` project on Linux.
- **Windows Build on Linux** restores and builds `Windows.slnx` on Linux with Windows
  targeting enabled. It compiles the Windows adapter, the CLI and the Windows scenarios
  but runs none of them.
- **Windows Scenario Tests** restores and builds `Windows.slnx` on a GitHub-hosted
  `windows-latest` runner and runs `tests/Authentication.Windows.Scenarios`, excluding
  the `WindowsHostTiming` category. It never passes the published-executable modes
  (`--native-cli-executable` and `--native-profile-cases`), so no published Native AOT
  executable runs. The scenarios use synthetic providers and HTTP handlers and touch no
  account, WAM UI or broker sign-in.

The Windows scenarios' wall-clock process assertions, such as the one-second shutdown
allowance and process-exit bounds, carry the `WindowsHostTiming` category. They are
Windows host-timing evidence: they establish timing only on the host where they ran. No
CI job runs them, and the `windows-latest` job makes no host-timing claim. The
[release workflow](../designs/windows-ado-authentication.md#release-workflow) runs the same
scenarios with the same filter before packaging and makes the same limited claim. A passing
Windows CI run establishes the synthetic Windows behavior on that runner image only; it
is not real-platform, broker or published-executable evidence.
Every Windows scenario run, in CI or on a host, uses a new empty temporary directory as
`TEMP` and `TMP`: the scenarios keep their files and junctions there as
[development validation](../research/experiment-safety.md#development-validation)
permits, and the run's owner disposes of it.
The scenario test projects are framework-dependent and run on the runtime bundled with
the pinned SDK, with roll-forward disabled. The Windows process scenarios launch the CLI
from its self-contained `win-x64` build output through that SDK's `dotnet` host; these
records make no claim about which runtime copy that launch loads.

A failed Windows host-timing assertion is not yet a product defect. Rerun the failed
scenario once on the same host and record the host load (processor and memory
utilization and other active workloads) around both runs. The validation reviewer, who
must be independent of the change author, decides whether the recorded load explains the
exceeded bound. Classify the failure as a product defect only when the rerun also fails
and that reviewer finds the recorded load does not explain it; otherwise record it as a
host-timing observation of that host.

#### Recorded Host-Timing Runs

**HT-1** ran on 2026-10-18 on the designated Windows host as development validation
under the Delivery Wave entry "W3: Validation Evidence, Tests and Hygiene (2026-10-18)"
([Issue #523](https://github.com/hcoona/microsoft-authentication-cli/issues/523), item E1).
It used a `git archive` copy of `main-v2` `9f426ed`, SDK 10.0.402 and its bundled runtime
10.0.13. A locked-mode restore of the same tree under WSL fetched the packages from
NuGet.org into a fresh package folder, and its NuGet audit passed. The Windows
locked-mode restore then resolved every package from that folder alone. It disabled
NuGet audit for that step only, because this host could not reach the audit source.
After a Release build, each scenario run used a new empty temporary directory:

- `TestCategory=WindowsHostTiming`: 13 passed, 0 failed, 0 skipped. No rerun was needed.
  Processor load was 14% before the run and 11% after it.
- `TestCategory!=WindowsHostTiming`: 179 passed, 0 failed, 0 skipped.

HT-1 is synthetic host-timing evidence for `9f426ed` on that host only. It touched no
account, WAM UI or broker sign-in and makes no real-platform, Native AOT or support claim.

### Slice Closure Versus Release Readiness

Closing the Windows Slice means its scenarios have the evidence above. It is not release
readiness. The following [release gates](#release-gates) remain open: the primary journey
gate for a release, real broker failure and concurrency evidence, external Profile
consent/audit/branding acceptance and Profile activation/distribution, minimal
deployment without development-machine dependencies, independent distribution and
installation identity under decision 0005 (selected for the portable package except its
signing identity, which stays unsigned until
[Issue #505](https://github.com/hcoona/microsoft-authentication-cli/issues/505)), resolution of the native threat-model
candidates that the [re-review](../security/threat-model.md#candidate-dispositions)
left `NeedsInvestigation`, and downstream service operations.

The [portable package](../designs/windows-ado-authentication.md#portable-package-and-version)
selects the distribution identities in the
[registry](../governance/operational-identities.yaml) and bundles the compatibility
candidate under decision 0003's prerelease exception. It closes none of these gates:
the packaging script's import and `--version` checks run on the build host, which has
development tools installed, so they are not deployment evidence; a locally built
archive is not public-restore evidence; and a real-host test of that archive supports
only claims bounded by its accepted protocol. Core `ProductVersionTests` (Linux CI) and
Windows `ProcessScenarios.RootVersionCompletesWithoutAuthentication` (Windows CI)
cover the version forms.
The [host test of the locally built archive](../research/experiments/portable-package-host-test.md#results)
passed every case it reached for both roles from native and WSL callers in existing state
on the designated host; interaction-required was reached only for the personal native
caller, and one forced-interactive call timed out with its prompt unidentified. It is
that bounded evidence only. Publishing `0.1.0-alpha` prereleases from the release workflow
closes none of these gates either.

The Slice explicitly excludes browser/device code, Linux forwarding/native brokers,
non-Public Cloud, service/impersonated/disconnected Windows sessions, ARM64, Profile
activation/distribution, service protocols, derived-credential exchange, and network
telemetry export. Requirements outside this bounded design retain their existing gates;
no complete-product or platform-support acceptance follows from this Slice alone.

## Native AOT Publishing

For [V2-REQ-055](../product/requirements/quality-build-and-validation.md#v2-req-055-native-aot-publishing),
the design records a disposition for every in-scope executable and accepted runtime
identifier: a supported candidate Native AOT path with its exact prerequisites, an
evidence-backed exception with a specified alternative publishing mode and reassessment
condition, or an unresolved choice that cannot be called implementation-ready. Review
public toolchain/dependency contracts and supported alternatives before accepting an
exception. Requirement acceptance does not establish compatibility of the current
UI/MSAL host; the [concrete disposition](../designs/windows-ado-authentication.md#native-aot-target-disposition)
selects Native AOT for the .NET 10 Windows x64 Win32 host. No non-AOT exception is
accepted. That selection is separate from the later evidence required for support.

The [Native AOT assessment](../research/experiments/windows-native-aot.md#windows-native-aot-assessment)
owns the synthetic observations that closed the bounded obligations below for their
exact pinned source, toolchain, artifacts and host; it does not validate the complete
application.

| Synthetic obligation | Status |
| --- | --- |
| Resolved provider assets and provenance | Satisfied for the synthetic graph; clean network restore remains unproved. |
| Complete AOT/trim diagnostics and publish completion | Satisfied; product build-helper and symbol behavior remain separate. |
| Native dependency disposition | Partially satisfied: synthetic loading under restricted search observed; the [real-host results](../research/experiments/real-host-validation.md#results) record the published application's imports and broker loading from the application directory with System32 imports on the development host; deployment without development tools remains open. |
| Observable native cleanup | Partially satisfied: observable disposal without exceptions; opaque native deallocation and shutdown status not claimed. |
| Genuine wrong architecture | Satisfied. |
| Missing and decoy inputs | Satisfied for the recorded artifact. |

For the implemented application, verify the resolved graph and every required native
asset under application-directory/System32 search before claiming deployment support.
Do not use arbitrary working directories, ambient `PATH`, runtime extraction or an
undeclared developer-machine dependency to make the selected path work. Retain exact
Windows C++ compiler/SDK/linker, .NET inputs and publishing properties in the applicable
build evidence. A discovered blocker needs a reviewed correction or requirement-compliant
exception; it cannot silently change the selected mode or weaken platform checks.

Review and render the C4 deployment and UML request, UI-cancellation and terminal-state
sources against their normative contracts. Later application tests must cover HWND
readiness, creation failure, callback lifetime/ABI, creating-thread destruction, stalled
UI dispatch, the hidden parent never becoming visible, the bounded pre-interaction
indication, and cancellation racing provider completion. Windows scenarios assert that
the hidden parent has no child controls and never becomes visible. One bounded
interactive observation confirms hidden-parent WAM presentation and human cancellation. Verify strict Profile parsing and result allowlists on the published binary,
including duplicate/unknown JSON fields and synthetic secret markers. Use the existing
scenario matrix for account, tenant, interaction, reuse and lifetime semantics rather
than treating an AOT publish as their acceptance.

Before claiming a publishing mode is supported, retain evidence for the exact source,
SDK, runtime identifier, native compiler, dependency versions, and publish configuration.
Verify that the artifact uses the selected mode, includes every required native/runtime
asset, and starts on its declared host without undeclared development-machine packages.
Inspect AOT/trimming diagnostics and justify each remaining suppression against a real
reachable-path obligation. A successful publish or absence of warnings alone does not
prove runtime compatibility.

Exercise the applicable existing contract and scenario basis on the published artifact,
including serialization, native broker loading, strict account/result validation,
silent and permitted interactive paths, hidden parent, cancellation, deadline, and process
termination. Select the least costly evidence level that establishes each property;
real WAM/UI behavior still needs bounded Windows evidence. Apply these obligations to
an accepted non-AOT exception as well. Do not add redundant copies of the scenario matrix
or weaken the primary-journey, Profile, security, and release gates.

Before a performance comparison runs, define a representative workload, host and
account-state assumptions, the selected non-AOT baseline, sample/attempt limits, and
decision-relevant acceptance criteria. Compare process startup separately from network
or user-interaction time, memory, total deployed size including native assets, and
build/diagnostic costs using equivalent functionality and configuration. Record the
measurement method and variability; claim only observed benefits. A desired benefit or
an AOT setting alone is not a measured improvement. This validation basis is not an
execution protocol; experiments require their own accepted authorization and protocol.

The Windows comparison baseline is the same Win32 host, .NET/runtime, RID, provider pins,
request/serialization behavior and native assets, published self-contained with JIT,
without trimming or ReadyToRun. It is a comparison input, not an accepted exception.
Separate synthetic help/invalid-request startup from silent and interactive network/UI
time. Before running a comparison, its accepted protocol must specify numerical criteria
appropriate to those workloads, warm/cold cache conditions, sample limits and variability;
this desk review invents no measurement or benefit threshold after seeing results.

## Dependency Upgrade Matrix

Each MSAL, native broker, cache, and platform dependency upgrade, including native
interop, must be independently testable, pinnable, and reversible. An evaluation must
isolate the target upgrade and any directly required adaptation from unrelated product
or dependency changes.

At minimum:

1. establish a known baseline source and explicitly pinned dependency set;
2. change one target dependency category per evaluation;
3. verify that both the baseline and proposed versions can be selected through explicit
   pins and tested independently;
4. exercise the applicable Windows, WSL, multiple-account, claims, cancellation, cache,
   secure-storage, and host states;
5. record whether a failure belongs to v2 policy, MSAL, the native broker, cache, platform
   integration, or host
   configuration;
6. restore the prior pin, rerun the applicable matrix, and retain the verified rollback
   path.

The 0.9.5 to 0.9.6 public dependency change is an initial case study, not a presumed
root cause.

## Release Gates

A platform or mechanism is supported only when:

- the first-release primary journey gate is satisfied for the release;
- its required contract, policy, and real-environment tests pass;
- no-interaction and strict-identity postconditions are directly observed;
- cancellation leaves no v2-owned task, listener, lock, or controllable prompt running;
  externally owned browser sessions can no longer complete the pending request;
- output and diagnostics obey authentication-material and email-channel containment;
- secure-state recovery, incompatible-state handling, concurrency, and persistence-warning
  behavior are documented and validated;
- a claimed headless-Linux repeated-noninteractive capability demonstrates compliant
  cross-invocation authentication-state reuse;
- the exact client application and dependency versions are recorded;
- the selected publishing mode meets the [Native AOT validation basis](#native-aot-publishing),
  including any accepted target-specific exception;
- installation and update behavior cannot collide with upstream AzureAuth.
