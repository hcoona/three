# Authentication Engine Threat Model

## Scope

This threat model applies to the delegated public-client authentication architecture and
the [concrete Windows Slice design](../designs/windows-ado-authentication.md). The Slice
is implemented; its controls are effective only to the extent the
[validation strategy](../validation/strategy.md#windows-slice-requirement-to-evidence-ledger)
records evidence for them, and design review alone does not mark them effective.

The goal is to protect authentication intent, token material, account metadata, cache
state, machine protocols, and user interaction within a normal developer workstation
threat model.

The project does not claim to protect secrets after the current operating-system user
session, administrator, kernel, or authentication broker is fully compromised.

## Security Design Tradeoffs

Evaluate a mitigation against a concrete scenario, protected asset, credible attacker or
failure, and the trust boundary it crosses. Compare the risk reduction with implementation
complexity, ongoing maintenance, and operational cost. Prefer the smallest mechanism that
satisfies the accepted security requirements; speculative attack chains outside this
workstation threat model do not justify unlimited application hardening.

Within the stated threat model, rely on the documented protections of the operating
system, maintained authentication libraries, broker, and platform-secure storage. Review
the application's configuration and use of those contracts rather than building a second
implementation of their security guarantees. Missing or contradictory capability
evidence remains an integration question, not a reason to assume a stronger guarantee.

If the required trust or safety conditions cannot be established, fail closed at the
affected boundary using the existing result taxonomy. Bound recovery and cleanup to
state the engine owns and the effects it is authorized to perform. Do not broaden access,
repair unrelated account state, or invent additional services to handle an extreme case.
This preserves the requirements' permitted cache-miss recovery and validated success
with a persistence warning; those outcomes do not expose an unvalidated token.

Cost is not permission to weaken an accepted requirement. If a required journey cannot
be realized under these assumptions at reasonable cost, present the limitation and
tradeoff for repository-owner disposition. A broader threat model, reduced security
guarantee, or expanded product boundary needs an explicit accepted decision.

## Protected Assets

- Access tokens, refresh-token cache material, authorization codes, and device codes.
- Request and provider-observed account emails, internal provider account metadata, and
  token/resource-tenant identity.
- Host identifiers: Windows security and logon-session identifiers, the machine name,
  and local paths that can contain a user name.
- Client application and authority configuration.
- Interaction-policy decisions and acquisition-stage order.
- Secure reusable-state integrity, recovery, and persistence-warning accuracy.
- Machine-readable stdout and diagnostic stderr boundaries.
- Release artifacts, dependencies, update metadata, and imported upstream source.

## Trust Boundaries

The [request lifecycle view](../architecture/request-lifecycle.md#request-context-and-trust)
allocates application and dependency responsibilities across these boundaries. This
threat model owns their security assumptions and mitigation tradeoffs.

- Calling process to CLI protocol boundary.
- Explicit caller-managed local Profile file to the immutable request configuration.
- CLI process to MSAL and native broker.
- Process to browser, device-code terminal, or other interactive surface.
- WSL calling process to the explicitly selected Windows CLI through interoperability.
- Process to platform secure storage.
- Process to network identity and resource endpoints.
- Build and release environment to distributed artifacts.
- Fork source to Microsoft-owned application registrations and upstream code.

## Credible Threats and Governing Authorities

| Threat | Governing authority |
| --- | --- |
| An explicitly interactive request is silently downgraded to engine silent acquisition, its result is mistaken for proof of renewed credentials or visible UI, or it bypasses account ambiguity and strict result validation. | [`V2-REQ-013`](../product/requirements/strategy-interaction-and-host.md#v2-req-013-deterministic-acquisition-order), [`V2-REQ-014`](../product/requirements/strategy-interaction-and-host.md#v2-req-014-explicit-per-request-interaction-permission), [`V2-REQ-020`](../product/requirements/strategy-interaction-and-host.md#v2-req-020-pre-resolved-account-for-silent-acquisition), and [`V2-REQ-022`](../product/requirements/strategy-interaction-and-host.md#v2-req-022-strict-result-identity) |
| A cached or operating-system default account silently overrides the requested account, or a same-email ambiguity is hidden by a persistent binding. | [`V2-REQ-012`](../product/requirements/request-identity-and-authority.md#v2-req-012-required-email-account-constraint), [`V2-REQ-020`](../product/requirements/strategy-interaction-and-host.md#v2-req-020-pre-resolved-account-for-silent-acquisition), and [`V2-REQ-022`](../product/requirements/strategy-interaction-and-host.md#v2-req-022-strict-result-identity) |
| A nominally silent request opens authentication or secure-state unlock UI. | [`V2-REQ-014`](../product/requirements/strategy-interaction-and-host.md#v2-req-014-explicit-per-request-interaction-permission), [`V2-REQ-021`](../product/requirements/strategy-interaction-and-host.md#v2-req-021-no-interaction-guarantee), and [`V2-REQ-025`](../product/requirements/strategy-interaction-and-host.md#v2-req-025-no-orphaned-work) |
| Ambiguous failure causes unsafe fallback to another identity or mechanism. | [`V2-REQ-023`](../product/requirements/strategy-interaction-and-host.md#v2-req-023-classified-fallback), [`V2-REQ-024`](../product/requirements/strategy-interaction-and-host.md#v2-req-024-request-bounded-claims-handling), and [`V2-REQ-032`](../product/requirements/result-and-process-protocol.md#v2-req-032-caller-action-failure-taxonomy) |
| A token or code leaks through arguments, logs, telemetry, crash output, or protocol noise. | [`V2-REQ-034`](../product/requirements/result-and-process-protocol.md#v2-req-034-output-discipline) and [`V2-REQ-035`](../product/requirements/result-and-process-protocol.md#v2-req-035-authentication-material-containment) |
| An email, an email-derived hash, a stable provider account or tenant identifier, a Windows security or logon-session identifier, the machine name, or a user-bearing local path creates a diagnostic, exception or telemetry identity trail. | [`V2-REQ-036`](../product/requirements/result-and-process-protocol.md#v2-req-036-account-and-host-identifier-privacy) |
| Another local user reads or modifies reusable state, unsafe state is consumed, or storage silently falls back to plaintext. | [`V2-REQ-040`](../product/requirements/cache-security-and-operational-identity.md#v2-req-040-secure-authentication-state) and [`V2-REQ-041`](../product/requirements/cache-security-and-operational-identity.md#v2-req-041-safe-reusable-state-recovery-and-concurrency) |
| Reuse across consumers confuses account, tenant, client, resource, or security contexts, or OS sign-in is mistaken for permission to use an opaque default. | [`V2-REQ-041`](../product/requirements/cache-security-and-operational-identity.md#v2-req-041-safe-reusable-state-recovery-and-concurrency), [`V2-REQ-020`](../product/requirements/strategy-interaction-and-host.md#v2-req-020-pre-resolved-account-for-silent-acquisition), and [`V2-REQ-022`](../product/requirements/strategy-interaction-and-host.md#v2-req-022-strict-result-identity) |
| Authentication work outlives cancellation or timeout. | [`V2-REQ-015`](../product/requirements/strategy-interaction-and-host.md#v2-req-015-common-deadline) and [`V2-REQ-025`](../product/requirements/strategy-interaction-and-host.md#v2-req-025-no-orphaned-work) |
| A WSL-launched provider prompt cannot be presented, has no owner, or appears on the wrong desktop. The hidden owned parent is deliberately never shown and is not itself the threat. | [`V2-REQ-016`](../product/requirements/strategy-interaction-and-host.md#v2-req-016-self-contained-interactive-authentication) and [`V2-REQ-021`](../product/requirements/strategy-interaction-and-host.md#v2-req-021-no-interaction-guarantee) |
| A Microsoft-owned client ID is mistaken for an owned credential or support contract. | [`V2-REQ-042`](../product/requirements/cache-security-and-operational-identity.md#v2-req-042-client-registration-as-configuration) and decision [`0003`](../decisions/0003-treat-client-registration-as-an-external-dependency.md) |
| A caller directs discovery or authentication to an attacker-controlled authority. | [`V2-REQ-011A`](../product/requirements/request-identity-and-authority.md#v2-req-011a-trusted-authority) |
| Home-tenant identity is mistaken for the requested resource tenant, or token parsing fabricates scope satisfaction. | [`V2-REQ-019`](../product/requirements/request-identity-and-authority.md#v2-req-019-tenant-selection), [`V2-REQ-027`](../product/requirements/request-identity-and-authority.md#v2-req-027-scope-satisfaction), and [`V2-REQ-033`](../product/requirements/result-and-process-protocol.md#v2-req-033-opaque-access-tokens) |
| Telemetry sends without explicit configuration or changes the authentication outcome. | [`V2-REQ-043`](../product/requirements/cache-security-and-operational-identity.md#v2-req-043-no-upstream-telemetry-reuse) and [`V2-REQ-046`](../product/requirements/cache-security-and-operational-identity.md#v2-req-046-optional-telemetry-semantics) |
| A compromised or substituted input to the product build (a NuGet package, the .NET SDK, or a GitHub Action in a build or test job) or an over-privileged CI token alters what is built or tested. | [`V2-REQ-053`](../product/requirements/quality-build-and-validation.md#v2-req-053-public-build-chain) and the existing mitigations: [`check.yml`](../../.github/workflows/check.yml) pins every action to a full commit SHA, grants only `contents: read` and checks out without persisted credentials; the [release workflow](../../.github/workflows/release.yml) also pins every action, starts only by hand and, in its `main-v2` copy, refuses other refs (the release review checks the attestation's source ref and workflow), checks out without persisted credentials, restores no tool cache, and splits its token by job (build: `contents: read`; attestation: `id-token: write` and `attestations: write`; draft: `contents: write`), attests both zips' build provenance, and only drafts a prerelease that the repository owner publishes after review; every restore runs in locked mode against the committed `packages.lock.json` files with the NuGet.org-only source mapping in [`nuget.config`](../../nuget.config); and [`mise.lock`](../../mise.lock) pins the SDK archive checksums. Repository check tooling is outside this product threat model; [contributor tooling](../../CONTRIBUTING.md#net-and-agent-tooling) governs it and how these pins change. |
| Dependency or upstream changes alter broker or cache behavior. | [Dependency Upgrade Matrix](../validation/strategy.md#dependency-upgrade-matrix) and [Release Gates](../validation/strategy.md#release-gates) |
| Unofficial artifacts overwrite or impersonate upstream AzureAuth. | [`V2-REQ-003`](../product/requirements/product-boundary.md#v2-req-003-unofficial-product-identity), [`V2-REQ-044`](../product/requirements/cache-security-and-operational-identity.md#v2-req-044-independent-distribution-identity), and decision [`0005`](../decisions/0005-establish-independent-operational-identity.md) |

## Security Policy References

Cache and operational-identity behavior is normative only in
[`cache-security-and-operational-identity.md`](../product/requirements/cache-security-and-operational-identity.md).
Protocol and diagnostic behavior is normative only in
[`result-and-process-protocol.md`](../product/requirements/result-and-process-protocol.md).
This threat model records why those requirements are security-relevant; it does not
restate them as an independent policy.

The explicit email in a command-line request is visible to local process inspection;
account and host identifier privacy is a diagnostic, exception and telemetry boundary,
not a promise to conceal process arguments or the result the caller requested.
Non-enumerated same-email accounts and provider alias metadata remain
visibility limits, not justification for ambient-account fallback. The
[secure-store desk finding](../research/v1-public-contract-baseline.md#recheck-006-secure-store-availability)
is reported evidence, not proof that V2 recovery works on a platform.

## Security Validation Priorities

### Concrete Windows Slice

The selected design adds a nonsecret Profile-file input and specializes the existing
caller/CLI boundary with optional stdin-lifetime cancellation. The caller deliberately
selects the executable and local Windows Profile path. The engine reads one bounded
snapshot, validates the trusted cloud/client/tenant/integration constraints, and does not
search for configuration, follow a configuration URL, or import upstream state. Ordinary
Windows file permissions protect configuration within the stated same-user trust model;
signing or encrypting a public Profile file would not protect against the excluded
compromised current user and is not required.

WAM supplies reusable-state protection and integrity through its API. The Slice has no
app-owned token file, cache callback, cross-process lock, account binding, or repair
operation. Missing durable-write confirmation is represented accurately as a success
warning. No new persistence service or independent proof of broker internals is needed.

The hidden owned Windows parent exists only for permitted interaction, is never shown,
and has no controls; its window procedure only turns `WM_CLOSE` into cancellation and
handles destruction. WAM controls its own credential/MFA/consent UI and its cancel. The
engine supplies the HWND, supplies the resolved account or the requested-email login
hint only for `interactive-if-needed` (`force-interactive` supplies neither, so the
user may pick another account; a hint would not bind the result either), validates
the final result, rejects any account other than the
requested email, forwards cancellation, and invalidates late completion. Provider prompt
choice is not an application security control. Unknown or terminal failures cannot
activate a PAT, alternate registration, browser, or account fallback.

Before real provider initialization, the [local host admission design](../designs/windows-ado-authentication.md#local-windows-host-admission)
checks only the current process/thread, its own logon, local session, station and desktop.
Unknown required state fails closed; checks do not change identity, inspect other users,
unlock the desktop or repair services. Treat own-logon metadata as transient sensitive
input: retain only the fields needed for local comparisons and expose no native details
through results, diagnostics or telemetry. Volatile conditions are rechecked at provider
and owned-UI effect boundaries under the original deadline. These snapshots do not
certify continuing WAM availability or exhaustive alternate-launch provenance. The
later protocol must declare the actual metadata access and account effects separately.

The selected Native AOT design replaces the managed desktop host with finite static Win32
interop inside the same process. Explicit ABI/layout, callback lifetime, creating-thread
window destruction and nonblocking cancellation are review obligations; the unmanaged
boundary must not leak exceptions or accept a late success. The hidden owned parent
holds no text controls and collects no credentials. The pre-interaction stderr indication
carries only fixed text and the admitted Profile name and owner, which reject control and
Unicode format characters. Process DLL search must exclude
the current directory and ambient `PATH`, retaining only the application directory and
System32 before provider initialization. Validate the actual broker loader and every
required native asset under those restrictions; Native AOT alone supplies no dependency
integrity or DLL-loading guarantee. A conflict makes the affected target unavailable.

The native TMT model's process, UI, broker, caller, Profile and data flows do not change:
these are internal host and deployment mitigations of its existing execution, tampering,
disclosure and availability threats. The
[Native AOT assessment](../research/experiments/windows-native-aot.md#windows-native-aot-assessment)
supports the bounded loader/allocation/observable-cleanup premise; it does not mark
product controls effective. Complete-application, Profile and security acceptance gates
remain applicable.

The optional lifetime pipe gives a cooperating caller a concrete cancellation signal.
Its contract depends on ordinary writer-handle closure, not Linux-to-Windows signal
translation. Without that channel, the original deadline bounds a lost caller. A blocked
output pipe or uncooperative dependency may require process termination. Unless V2 has
observed the complete result written, the process exits with a non-typed status and the
caller must recognize that as transport failure; once V2 observes the complete result
written, its typed exit status survives a later forced termination. A one-second local shutdown
allowance cannot resume authentication or extend its deadline. This is the bounded
fail-closed response to the actual transport limitation, without an additional daemon.

Only the validated result contains a token. Fixed stderr indications and optional local
telemetry use an allowlist, have bounded buffering, and carry no email or authentication
material, and none of the account or host identifiers that
[`V2-REQ-036`](../product/requirements/result-and-process-protocol.md#v2-req-036-account-and-host-identifier-privacy)
excludes. The opt-in token-shape observation under
[`V2-REQ-038`](../product/requirements/result-and-process-protocol.md#v2-req-038-local-token-shape-validation-observation)
runs only after the complete result is written, never after a failed write, and emits
fixed categories plus the provider-reported expiration and remaining lifetime only. Network telemetry
is not selected. The externally owned Visual Studio registration remains an explicit
dependency limit under the
[external Client Profile gate](../product/compatibility-and-migration.md#externally-owned-client-profile-gate).
The Slice scenario matrix tests these application boundaries.

The unsigned [portable package](../designs/windows-ado-authentication.md#portable-package-and-version)
crosses the build-and-release boundary. Its executable and broker runtime are not
Authenticode-signed, so a user can establish their identity only by comparing the
archive with its published SHA-256 checksum before extracting it into a directory they
control. The package installs nothing outside that directory, adds no `PATH` entry and
no `azureauth` shim, and its names differ from every reserved upstream identity, so it
cannot overwrite or be invoked as upstream AzureAuth. The bundled Visual Studio
compatibility Profile is inert until a caller passes its path. Signing is tracked in
[Issue #505](https://github.com/hcoona/microsoft-authentication-cli/issues/505).

The normative release evidence requirements are defined by
[`validation/strategy.md`](../validation/strategy.md). Security review prioritizes its
strict-account, interaction, cancellation, cache, WSL, authority, output, dependency, and
artifact-isolation scenarios because they exercise the threats above.

Research using existing account or broker state follows the
[experiment policy](../research/experiment-safety.md#environment-and-effects). Its accepted
protocol bounds observations, ordinary authentication updates, operator interaction, and
safe retention or cleanup. Using an existing environment neither changes the product
threat model nor authorizes access to unrelated state.

## TMT Analysis and Disposition

The checked-in [native model](authentication-engine.tm7) is the executable data-flow
input and candidate-disposition record for
[Microsoft Threat Modeling Tool](https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool).
This Markdown record owns the security assumptions, interpretation, and requirement
routing. The native model does not create a second product policy.

The model covers one CLI process containing the engine and MSAL, its caller, explicit
caller-managed Profile file, OS broker, user-interaction facility, platform-secure
reusable state, identity service, local diagnostic sink, and optional telemetry endpoint.
It has two boundary containers and 17 directed flows. MSAL is an in-process dependency;
a broker is an external OS facility.
The workstation boundary and CLI process/API boundary distinguish network trust from
local process and dependency responsibilities. They do not assert that every local
same-user process is a separately protected OS security principal. The secure-state role
does not select a V2 cache format or imply access to a broker's private store.

In the [selected WSL deployment](../architecture/overview.md#deployment-wsl-caller-and-windows-cli),
the existing caller is in WSL and the CLI process, broker, and eligible state are on
Windows. The caller/CLI flows model the request, optional lifetime pipe, and token
crossing that boundary; the Profile-to-engine flow supplies the selected configuration
snapshot. No forwarding process is introduced. WSL interoperability is
an operating-system dependency inside the workstation trust base. Executable selection,
output confidentiality, Windows UI ownership, and bounded process completion remain
relevant threats. The model records the design boundaries; it does not establish that an
implemented cross-host path is secure.

The generic interaction, state, and telemetry roles retain the wider architecture's
trust boundaries. In the concrete Windows Slice they specialize to WAM-owned interaction
and state, and local stderr telemetry; browser/device-code and network telemetry remain
unselected. They do not add components to the selected deployment. Resource access,
Git/package protocols, and credential translation remain with the caller. Build/release
threats remain in the narrative model above because they have a separate lifecycle from
this runtime diagram. No real account, tenant, token, or authentication observation is
stored in the model.

### Tool and Reproduction

The current reproduction basis is installed Windows TMT **7.3.51110.1** with the embedded
**SDL TM Knowledge Base (Core) 4.1.0.11**, which comes from Microsoft's
[public default template at `0ece9c7`](https://github.com/microsoft/threat-modeling-templates/blob/0ece9c71b6f3710b10d497bd1ef63e57805e7c3e/default.tb7).
With that tool, the model loads without issues and generates **96 candidates**: 27 denial
of service, 20 elevation of privilege, 20 spoofing, 17 repudiation, 7 information
disclosure and 5 tampering. Their stored states are 63 `Mitigated`, 11
`NeedsInvestigation`, 22 `NotApplicable` and none `NotStarted`. Analysis View displays
the stored value `NeedsInvestigation` as *Needs Investigation*, the status name in the
[TMT getting-started guide](https://learn.microsoft.com/en-us/azure/security/develop/threat-modeling-tool-getting-started#analyzing-threats).
These counts come from the 2026-10-19 desktop run in
[PR #531](https://github.com/hcoona/microsoft-authentication-cli/pull/531), which drove
the installed tool through Windows UI Automation on a copy of the model as changed by
that pull request; every displayed ID, state and justification matched the committed
XML. In that run the repository owner performed one desktop `Save As` to a temporary
copy, which was not committed. TMT 7.3.51110.1 kept every threat ID, state and
justification but rewrote the file: it dropped the license comment and indentation,
reordered properties, wrote the duplicated state information as nil, renumbered
serialization IDs and moved one connector. A tool save therefore needs its full diff
reviewed before it replaces the committed text. Analysis is a design review aid, not an authentication experiment,
implementation test, or proof of platform security.

Prefer a supported command-line analyzer when available. The official getting-started
and feature documentation consulted for this version did not identify one. The native API
workflow therefore uses 32-bit Windows PowerShell in STA mode to load the installed TMT
model, view-model, and local-storage assemblies, initialize WPF, and open a separate
model copy with `ObjectModel(LocalFile, false)`; `GenerateThreats()` and
`ProcessModelImmediately(FullModel)` then produce and reconcile the candidates. This is a
version-specific workflow, not a supported public CLI contract. A pull request that
changes the model records its validation method, input hash, tool and template versions,
and observed counts.

Desktop reproduction remains possible by opening `authentication-engine.tm7` and switching
to Analysis View. The file embeds its template; importing an Azure template or regenerating
from Markdown is unnecessary. Review candidate justification and status in the native file
alongside the requirements linked above. Keep machine-specific author metadata out of
committed dispositions. Template upgrades may change the candidate set and require review;
zero findings is not an acceptance target. A Windows installation or analyzer job is not
required for every unrelated CI change.

### Candidate Dispositions

All 96 candidates have a scenario-specific or threat-family justification in the native
file. The dispositions rest on the security re-review against the implemented Slice at
`main-v2` `563c262` and its follow-up against `main-v2` `69d9a72`, which applied the
[real-host results](../research/experiments/real-host-validation.md#results): **63 are
`Mitigated`, 11 are `NeedsInvestigation` and 22 are `NotApplicable`**. `src` at
`69d9a72` is identical to `563c262` and to the real-host subject `8d9be27`. The later
forced-interaction change and its revert, decided by the repository owner on 2026-10-16
(quoted in [PR #501](https://github.com/hcoona/microsoft-authentication-cli/pull/501)),
changed no disposition; the fourth real-host subject `3ae2554` contains that revert, and
its unfiltered Windows scenario run is the S1-1 run cited below. Scenarios added after
that run are cited with the CI job that executes them. The `WindowsHostTiming` scenarios
run in no CI job; their current evidence is the HT-1 run on the same host at `main-v2`
`9f426ed`, which the
[validation strategy](../validation/strategy.md#recorded-host-timing-runs) records as
synthetic host-timing evidence for that commit only. The design records the forced-interaction
[known limitations](../designs/windows-ado-authentication.md#validation-and-explicit-unsupported-cases),
and the real-host record owns the observations.

- **`Mitigated`** requires an implemented control with executed evidence: the Linux CI
  run of `tests/Authentication.Scenarios` (job `linux-scenarios`; 378 cases at `563c262`); the
  unfiltered `tests/Authentication.Windows.Scenarios` run on the designated Windows host
  (S1-1 at subject `3ae2554`: 186 passed, 0 failed, 0 skipped, including
  `WindowsHostTiming`) or the published Native
  AOT import and loading checks, both in the
  [real-host results](../research/experiments/real-host-validation.md#results); the
  [HT-1 run](../validation/strategy.md#recorded-host-timing-runs) of the same project on
  that host at `9f426ed`; or a
  recorded real-host observation in those results or the
  [Windows Slice research](../research/experiments/windows-slice-validation.md#selected-role-windows-and-wsl-acceptance-observations).
  Each native justification names the control and those scenarios or observations.
  The Windows scenarios also run in the GitHub Actions job `windows-scenarios` (Windows
  Scenario Tests), which excludes `WindowsHostTiming` and the published-executable cases.
  Many Windows scenarios run the real classification code against substitutes: a
  synthetic `IWindowsHostObservations`, a substitute `IMsalSessionFactory`, a substitute
  loader-restriction delegate, or, in `LocalProviderAdmissionScenarios`, substituted host
  admission and provider initializer. Such a scenario establishes how the engine
  classifies the condition, not that the native control detects it. `NativeObservationScenarios`
  runs some native reads and controls directly; where no scenario does, the native
  success path is established only by real-host calls that passed it. Justifications
  state which applies.
- **`NeedsInvestigation`** now covers only candidates whose remaining evidence depends
  on a release gate that the
  [validation strategy](../validation/strategy.md#slice-closure-versus-release-readiness)
  keeps open, tracked by
  [Issue #482](https://github.com/hcoona/microsoft-authentication-cli/issues/482):
  independent distribution identity, real broker failure or concurrency, or real-WAM
  correlation-echo confirmation (CORE-01), which the strategy carries as a candidate left
  `NeedsInvestigation`. Each justification names that gate. No candidate disposition
  depends on still-pending Windows scenario execution.
- **`NotApplicable`** adds the two optional telemetry-endpoint candidates (IDs 85 and 87):
  the Slice exports no network telemetry. Selecting network export must re-disposition
  them.

**Evidence provenance.** The cited real-host observations ran on earlier accepted builds,
not on `563c262`. The research records own each observation and its binding:

| Observation | Build |
| --- | --- |
| Native personal R1/R6 and work R7/R8 acquisition with fresh-process reuse; WSL success and reuse; initially closed lifetime pipe | Selected candidate built before `2d79f3a` (runs on or before 2026-10-09; the native pairs are recorded in the [Issue #108](https://github.com/hcoona/microsoft-authentication-cli/issues/108) body revision of 2026-10-09T18:24:55Z in its edit history, and the WSL calls in the [PR #420](https://github.com/hcoona/microsoft-authentication-cli/pull/420) and [PR #421](https://github.com/hcoona/microsoft-authentication-cli/pull/421) completion comments); no committed record names its exact source or executable, so these observations are supplementary only |
| Typed WSL timeout; timed lifetime-pipe EOF; caller death without a lifetime pipe | Corrected candidate from source `2d79f3a` (Native AOT `f6a04588…`); caller death ran on the unchanged candidate at `cef5e13`, whose `src` equals `2d79f3a` |
| EOF during actual WAM; human WAM close | Source `af05460` (Native AOT `4a393afd…`) |
| Hidden-parent WAM cancellation; locked-state no-UI rejection | Tree identical to `68c5175` (Native AOT `b9ecea4f…`) |

Why they still bear on `563c262`: `WindowsLifetimePipe.cs`, `WindowsStandardHandles.cs`
and `WindowsProfileSource.cs` are unchanged across every observed build, and
`RequestLifetime.cs` is unchanged since the `2d79f3a` dispatch fix, after which the
timeout, EOF, caller-death and WAM-cancellation observations ran. Later changes to
`RequestCoordinator.cs` and `ResultProjection.cs` (explicit account selection, provider
failure categories and the single failure filter, correlation binding, the tenant-conflict
reason) narrow acceptance and are exercised by the named executed Core scenarios. Changes
to `WindowsProcess.cs` (result written before diagnostics, help text, notice path) are
executed by the Windows `ProcessScenarios` in the S1-1 run. Later changes to
`WindowsProcess.cs` (local diagnostics drained within the shutdown bound, the typed exit
status kept once the result is written, no completion diagnostics after a failed write)
postdate S1-1. HT-1 executes the Windows scenarios on that source at `9f426ed`,
including the `WindowsHostTiming` watchdog, closure and undrained-pipe bounds that no CI
job runs. Earlier success transport is
supplementary: token containment rests on the executed Core and Windows scenarios. The
[real-host results](../research/experiments/real-host-validation.md#results) add deadline
and lifetime-pipe EOF observations under the hidden parent on subject `8d9be27`, for the
roles and callers they list. `RequestLifetime.cs` and `WindowsLifetimePipe.cs` are
unchanged from `8d9be27` through the current source. The `WindowsProcess.cs` changes
after `3ae2554`, the last real-host subject, rest on HT-1 as above; no real-host call
ran on source after `3ae2554`.

`Mitigated` means the named control is implemented and exercised by that evidence
within the declared threat model; it is not a support claim, release readiness or proof
of platform security. Trusted dependencies (OS process isolation, WAM, MSAL TLS and
OAuth handling) remain trusted rather than verified.

| Candidate group | Disposition after the 2026-10-16 follow-up |
| --- | --- |
| Caller, identity-service and broker-completion spoofing; input validation (IDs 2, 3, 20, 33, 34, 43, 47, 48) | `Mitigated`: closed request/Profile syntax, cloud and tenant admission and strict email/tenant/scope/operation validation, executed in Core scenarios; the production correlation binding is executed in the Windows S1-1 run. Construction of the fixed Public Cloud authority is executed in the S1-1 run by `MsalConstructionScenarios`, which build the real MSAL application and assert its authority, and is exercised by every recorded real-host acquisition. |
| Process spoofing (IDs 1, 18, 32, 46, 65, 73) | `NeedsInvestigation`: operation binding is executed and the distribution identities are selected under decision 0005, but the distribution-identity release gate stays open because the signing identity is unsigned until [Issue #505](https://github.com/hcoona/microsoft-authentication-cli/issues/505); executable identity rests only on the published SHA-256 checksum and the directory the user extracts into. |
| Broker and interaction-surface spoofing (IDs 15, 19, 29) | `Mitigated`: `MsalCompositionScenarios` classify loader and initialization failures with a substitute loader delegate and session factory; `NativeObservationScenarios.RestrictedDllSearchExcludesTheWorkingDirectory` runs the real `WindowsLoader.RestrictSearch` in a child process, and `MsalConstructionScenarios.UnavailableBrokerStopsSessionCreationWithItsFixedCategory` runs the real session factory with only the `IsBrokerAvailable` answer replaced; `MsalAdapterScenarios.RealNonBrokerInteractiveCallReachesTheRejectingWebUi` drives a real non-broker MSAL interactive call against a synthetic transport into `RejectingWebUi`; the last three were added after S1-1 and run in `windows-scenarios`; the hidden parent is a real Win32 window in `OwnedHostScenarios`. On the real host, the published Native AOT application imports only Windows DLLs, and every real call loaded the broker from the application directory with System32 imports and reported no `dll_search` or `broker_initialization` indication, which observes the native success path of both controls. Forced interaction is WAM-owned under the hidden parent with strict final validation; its provider-owned UI behavior is a design known limitation ([real-host results](../research/experiments/real-host-validation.md#results)). Deployment without development-machine dependencies remains a separate Native AOT release gate. |
| Data-flow disclosure; token and diagnostic destinations (IDs 5, 12, 22, 36, 50, 69, 76, 82) | `Mitigated`: no credential input, no application token store, token only in a validated success, allowlisted diagnostics; executed in Core scenarios with synthetic markers and recorded real-host transport. |
| Interrupted flows (IDs 7, 14, 17, 24, 31, 38, 45, 52, 58, 60, 62, 64, 71, 78, 84, 93) | `Mitigated`: original deadline, single terminal latch and no retry/fallback in Core scenarios, plus recorded EOF-during-WAM, human-cancellation and timeout observations ([real-host results](../research/experiments/real-host-validation.md#results)) and the earlier initially closed lifetime pipe ([Windows Slice record](../research/experiments/windows-slice-validation.md#selected-account-scenarios)) and caller-death ([Windows Slice record](../research/experiments/windows-slice-validation.md#remaining-wsl-lifetime-and-action-cases)) observations, bound to the builds in the evidence-provenance table above. ID 14, result delivery, now rests on the Windows `ProcessScenarios` executed in S1-1 and, for the current `WindowsProcess.cs`, in HT-1. A provider window left after non-human termination is a [known limitation](#known-limitation-provider-window-after-non-human-termination), not an unmitigated interruption. |
| Process crash/stop and Profile-read bounds (IDs 6, 23, 37, 51, 77, 92) | `Mitigated`: typed outcomes and Profile bounds are executed in Core, and the process watchdog, broken or undrained result pipe and Profile size limit are executed in the Windows S1-1 run. For the current `WindowsProcess.cs`, the watchdog and the broken and undrained result-pipe bounds are executed in HT-1 at `9f426ed`. |
| Impersonation (IDs 8, 25, 39, 53) | `Mitigated`: no privileged helper exists. The impersonation and logon-kind classification is executed by `WindowsHostAdmissionScenarios` against synthetic `IWindowsHostObservations` (S1-1 and `windows-scenarios`). The native thread-identity read is executed in `windows-scenarios` by `NativeObservationScenarios.ThreadIdentityDistinguishesImpersonationOnTheCallingThread`, with and without `ImpersonateSelf`. The native logon-kind, session and desktop reads have only their success path (an ordinary interactive logon) observed, on the real host by every call that passed admission. |
| Remote code execution (IDs 9, 26, 40, 54, 80, 95) | `Mitigated`: request and Profile input (IDs 9, 95) in Core; bounded provider error-body and broker-status parsing (IDs 26, 40, 54, 80) in the Windows S1-1 run. |
| Execution-flow changes (IDs 10, 27, 41, 55, 81, 96) | `Mitigated`: explicit policy state, one safe provider-failure filter and Profile fields that cannot select policy, executed in Core scenarios. |
| OAuth response CSRF (IDs 42, 56) | `NeedsInvestigation`: correlation binding is executed in Core and the MSAL composition scenarios in the Windows S1-1 run, and every validated real success passed the fail-closed correlation check; broker-level echo remains an inference, so real-WAM confirmation (CORE-01) stays open under the [release gates](../validation/strategy.md#slice-closure-versus-release-readiness). No parallel OAuth verifier. |
| Reusable-state spoofing, persistence status and resources (IDs 66, 68, 70, 74) | `Mitigated`: no application-owned store, persistence always reported as unconfirmed, no coordination or retry outside the deadline; recorded fresh-process reuse. |
| Store corruption and inaccessibility (IDs 67, 72, 79) | `NeedsInvestigation`: the classification of an unavailable broker is executed with synthetic initialization results: `LocalProviderAdmissionScenarios` substitutes host admission and the provider initializer, and `MsalCompositionScenarios` substitutes the session factory. `MsalConstructionScenarios.UnavailableBrokerStopsSessionCreationWithItsFixedCategory` runs the real session factory with only the `IsBrokerAvailable` answer replaced (added after S1-1; `windows-scenarios`); real broker failure, disappearance and concurrency stay open under the [release gates](../validation/strategy.md#slice-closure-versus-release-readiness). |
| Profile source and reads (IDs 89, 94) | `Mitigated`: admission of a reported failure is executed in Core, and source substitution and the translation of real file-store failures in the Windows file-snapshot scenarios of the S1-1 run. |
| Optional telemetry endpoint (IDs 85, 87) | `NotApplicable`: the Slice exports no network telemetry. |
| Generic nonrepudiation candidates (`R6`/`R7`, 16 items) | Not applicable: the engine promises typed results and sanitized diagnostics, not signed delivery receipts or an identity-rich audit ledger. Persistence-status accuracy remains applicable under the separate storage candidate. |
| CSRF on a CLI request or broker API result (IDs 11 and 28) | Not applicable to these non-browser-cookie endpoints. Their input-validation and spoofing concerns remain applicable under other candidates. |
| Profile disclosure through a spoofed receiver or weak store ACL (IDs 88 and 91) | Not applicable to confidentiality of deliberately nonsecret registration metadata. Profile integrity, executable selection, and authenticated output remain covered by applicable spoofing and tampering candidates. |

Generic template attribute defaults are question prompts, not observations that a
platform lacks TLS, memory protection, or secure storage. Dispositions evaluate the
actual flow and the declared dependency trust rather than mechanically adopting each
suggested control.

### Known Limitation: Provider Window After Non-Human Termination

After a product timeout or a lifetime-pipe cancellation while WAM is shown, a WAM window
can remain after the CLI process ends (finding F2 in the
[real-host results](../research/experiments/real-host-validation.md#results), which own
the observations and their scope).

That window belongs to the provider. The product forwards cancellation through MSAL,
destroys its hidden parent and invalidates the pending request; it does not wait for, or
guarantee, closure of a provider surface. This matches the externally owned surface
clause of [`V2-REQ-025`](../product/requirements/strategy-interaction-and-host.md#v2-req-025-no-orphaned-work)
and the [design's cancellation semantics](../designs/windows-ado-authentication.md#uml-ui-ownership-and-cancellation-sequence).
The user closes the window manually.

Result correctness is unaffected. The terminal outcome is latched and the typed
`Timeout` or `Cancelled` result is committed before the process ends, so no token is
delivered for that request, and a late provider completion cannot resume it. Whether
acting in the leftover window changes provider-managed session state was not observed;
the requirement does not promise to revoke independently completed provider changes. By
repository-owner decision, recorded as a non-goal in
[PR #493](https://github.com/hcoona/microsoft-authentication-cli/pull/493) (commit
`fe6fbc8`) without a verbatim owner quotation, no window-closing mechanism will be added;
this is a known limitation, not an open candidate.

### Scenario Review Beyond STRIDE Generation

The template does not fully express strict full-email selection, no-interaction
permission, or validated-success/persistence-warning semantics. Review those directly
against the primary journey and the
[UML lifecycle](../architecture/request-lifecycle.md), including:

- a corporate OS default differing from the requested personal account;
- a silent request encountering authentication or secure-store unlock UI;
- a provider result with missing or mismatched identity metadata;
- a valid result with unconfirmed persistence, without waiting indefinitely or leaving
  V2-controlled work running;
- a late completion after cancellation or timeout;
- consumer changes that preserve authentication context and should reuse eligible state.

The [V1-to-V2 delta assessment](../research/v1-public-contract-baseline.md#architecture-reuse-and-remaining-deltas)
provides the dependency baseline for these checks. TMT cannot establish the specific
personal-account/Azure DevOps registration eligibility, first-use account visibility, or
real host cancellation behavior. Those focused integration questions retain their
existing evidence routes; the model does not enlarge them into a general hardening or
experiment program.
