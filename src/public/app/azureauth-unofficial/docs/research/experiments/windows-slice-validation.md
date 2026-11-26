# Windows Slice Implementation Validation Evidence

This is a completed experiment record for
[Issue #108](https://github.com/hcoona/microsoft-authentication-cli/issues/108), retained as
evidence cited by current records. It authorizes no execution. The Delivery Wave grant it
ran under was completed and removed. The full protocol, call procedures, recipes,
allocations and accounting, source pins and execution history remain at the
[pinned revision](https://github.com/hcoona/microsoft-authentication-cli/blob/242cd292a10ee5626e783f42349e570c2e5a19a1/docs/research/experiments/windows-slice-validation.md).
Earlier controlled scenario results remain at the
[pre-cleanup revision](https://github.com/hcoona/microsoft-authentication-cli/blob/06ab8855e4fc4c3144852f4cadeb12086531c388/docs/research/experiments/windows-slice-validation.md).
The [experiment policy](../experiment-safety.md) governs any future experiment; another
run requires a new accepted protocol and authorization.

The [dated observation records](#dated-observation-records) below retain the exact
candidate hashes, accepted call revisions and accounting behind the summaries that
precede them. The [public-source basis](#windows-slice-dependency-and-host-contracts)
and the [recheck evaluations](#recheck-evaluations) for the Slice are also kept here.
The [validation strategy](../../validation/strategy.md) owns the mapping from scenarios
to evidence, and the [Windows design](../../designs/windows-ado-authentication.md) owns
the parent selection.

## Common Observation Basis

All real calls used the designated Windows 11 x64 interactive user and, where stated, the
designated WSL2 Linux x64 caller. They used the existing owner-designated personal and
work roles, the experimental `visual-studio-legacy-wam` Profile, the Azure DevOps default
scope and existing broker state. Existing state is not clean first use, and ordinary
provider-managed session updates were permitted and retained.

- **Evidence channels.** Three channels stayed separate. Standard local OpenTelemetry
  stage events established observed request phases. Ordinary process handles and actual
  tool returns established process completion. Operator reports established visible WAM
  and human actions. Phase events and process exit do not prove visible UI, window
  attribution or provider-wide closure.
- **Result validation.** Token-bearing stdout was checked only on Windows, in memory, by
  the existing Windows-only result validator. It returned fixed protocol, outcome, route,
  identity and metadata conclusions. No token, result, account or tenant identifier,
  identifier hash, provider text or raw diagnostic entered Linux beyond bounded caller
  memory and pipes, agent output or Git.
- **Typed results.** A typed outcome required complete streams, a matching product exit
  and Windows-local validation. Product exit 2 or incomplete transport is a transport
  failure, not a typed result.
- **Limits.** These are existing-state observations on one host. They establish no
  durable persistence receipt, clean first use, another host's behavior, Profile
  distribution or support.

## Selected-Account Scenarios

**Method:** Real-account calls invoked the existing product directly as one ordinary
foreground Windows process per request. Observation used standard local telemetry and
Windows-local result validation, without a retained host, launcher, controller or custom
observer. Interaction was never forced to manufacture evidence: a validated
`InteractionRequired` ended a silent-first probe, and only a separately admitted
interactive call followed it.

**Runtime observation:** Native personal and work requests each obtained a validated
selected-account success and a fresh-process silent reuse (scenarios R1/R6 and R7/R8).
Actual WSL calls through ordinary interop then established complete result transport,
silent reuse and an initially closed lifetime pipe returning `Cancelled/None` with
exit 1 and no token. These ran on or before 2026-10-09 on a selected candidate built
before `2d79f3a`. The native pairs are recorded in the
[Issue #108](https://github.com/hcoona/microsoft-authentication-cli/issues/108) body
revision of 2026-10-09T18:24:55Z, kept in its edit history; the WSL calls are recorded
in the [PR #420](https://github.com/hcoona/microsoft-authentication-cli/pull/420) and
[PR #421](https://github.com/hcoona/microsoft-authentication-cli/pull/421) completion
comments. No committed record names their exact source or executable.

**Limits:** A silent route, no-interaction permission, telemetry or process exit alone
does not prove that no UI appeared. Reuse is fresh-process reuse of existing state, not a
durable-write receipt.

## Actual WSL Deadline Assessment

**Runtime observation:** Repeated one-second noninteractive WSL requests did not produce
a typed `Timeout`. They returned `MechanismUnavailable` with the fixed
`host_session_inactive` indication, or exit-2 transport failures. A diagnostic call
recorded a fixed `timeout` request completion after about one second while account
discovery had no observed completion, with product exit 2. A later personal
`interactive-if-needed` request on the corrected candidate returned validated
`Timeout/None`, exit 1, at its ordinary 120-second deadline. That observation
establishes an actual typed WSL timeout under the original deadline.

**Limits:** Caller intervals are not exact Windows entry or exit times. Phase events do
not establish which dependency was pending at expiry, the watchdog path or the cause of
an exit-2 failure. `host_session_inactive` establishes failure of the session's
Active-state gate, not its exact substate or physical cause. The operator did not watch
continuously during the timeout, so that call does not establish user cancellation or
visible WAM.

## Console Parent Feasibility

**Question:** Does an already installed PowerShell 7 process, launched from WSL, give a
console child an existing visible root parent that could serve as the WAM parent?

**Method:** Two credential-free foreground calls each ran once. WSL invoked Windows
PowerShell 5.1 x64 directly, and WSL invoked the installed PowerShell 7, which invoked the
same Windows PowerShell subject. A fixed script used `GetConsoleWindow`,
`GetAncestor(GA_ROOTOWNER)`, `IsWindow` and `IsWindowVisible` and emitted only eight
fixed Booleans. No authentication, account, Profile, cache, network or window creation was
involved.

**Runtime observation:** Neither launch shape gave the surrogate a console HWND or a
root-owner HWND. Both reports completed with exit 0 and empty stderr.

**Inference and limits:** Ordinary shell wrapping is not an established existing-console
parent source for this launch context. Windows PowerShell is a surrogate: the result is
not exact Native AOT product evidence, does not cover every WSL terminal, and does not
show that WAM requires a visible application window.

## Hidden Owned-Parent Candidate

**Question:** The [Windows design](../../designs/windows-ado-authentication.md) considered
keeping the existing owned top-level HWND hidden. Can that be done without a larger host
rewrite?

**Method:** An isolated experimental source omitted only the native show call in the owned
request host and set MSAL `BrokerOptions.Title` to
`Unofficial hcoona/microsoft-authentication-cli`. It kept the owned STA, message pump,
admission rechecks, cancellation latch and creating-thread cleanup.

**Synthetic observation:** Four existing credential-free Windows scenarios passed for the
hidden parent: readiness with immutable-Profile and thread checks, cancellation during
creation, internal close without caller cancellation, and completion waiting for actual
UI-thread exit.

**Runtime observation:** The first personal silent assessment returned
`MechanismUnavailable` without a retained reason. A diagnostic call identified
`host_session_inactive`. After the ordinary desktop connection action, a fresh
comparison returned `Success/Silent` and fresh-process `Success/Silent`, so the
conditional interactive case was skipped.

**Limits:** These results establish synthetic own-window lifecycle behavior and selected
request transport and reuse for the candidate. They neither validate nor reject
hidden-parent WAM presentation; the later
[interactive observation](#hidden-owned-parent-interactive-observation) addresses it.

## Windows Request Lifetime Dispatch Validation

**Synthetic observation:** After the request-lifetime dispatch correction, the corrected
visible-parent product was rebuilt and published as Windows x64 Native AOT. Twelve
existing credential-free Windows process scenarios passed: normal result despite broken
diagnostics, initially closed lifetime pipe, writer closure rejecting late success,
uncooperative deadline, six owned-host cancellation, fault, UI-thread and callback drain
cases, and two default HTTP cancellation and disposal cases.

**Limits:** Controlled providers and synthetic Profiles were used. These cases do not
establish real WAM cancellation, an actual WSL deadline, the cause of a real exit-2
failure or whole-Slice acceptance.

## Remaining WSL Lifetime and Action Cases

**Runtime observation:** On the selected visible-parent candidate, the work role
established three named cases:

- **Caller death without a lifetime pipe.** Caller-only SIGTERM ended the Linux caller
  with exit 143. An independently retained Windows process handle then observed native
  exit within the original bound. This establishes finite scoped Windows completion after
  caller death, not immediate signal delivery, a typed result or UI closure.
- **EOF during actual WAM.** After the operator confirmed an actual WAM surface, the
  caller closed its sole lifetime writer while the product was live. The result was
  validated `Cancelled/None`, exit 1, with no token. EOF did not close external WAM.
- **Human WAM cancellation.** The operator closed WAM, and the validated result was
  `Cancelled/None`, exit 1, with no token. This is WAM cancellation, not consent denial
  or owned-parent Cancel behavior.

Earlier attempts ended as alternatives, such as silent success, `host_session_inactive`,
timed-pipe cancellation without observed WAM, or failed observation. They remain
historical evidence at the pinned revision and do not satisfy these cases. One forced
interactive diagnostic returned fixed `error.type: broker_contract`, which established
the failure boundary rather than the offending parameter. One EOF attempt reached actual
WAM but failed acceptance when the result validator exited without conclusions; its
pinned runtime was then found absent, and the validator alone was bound to the installed
runtime without changing the product.

## Selected Visible-Parent UIA Observation

**Method:** A work-role WSL `force-interactive` call ran with a short PowerShell 5.1
STA script. Using standard Windows UI Automation, the script bound exactly one product
process and its one owned top-level window. It read only that window and its descendants
and could invoke the owned Cancel only when the parent and Cancel were enabled and on
screen. A prior manual-report attempt had ended in a timeout without physical evidence.

**Runtime observation:** The owned parent was found and its title matched. While WAM was
shown, the parent was disabled as WAM's modal owner, focus was not in the product, and
the owned Cancel was not actionable, so the script took no action. The operator cancelled
on WAM's own surface. The result was validated `Cancelled/None`, exit 1, with complete
transport and completion. The [owner's public report](https://github.com/hcoona/microsoft-authentication-cli/issues/108#issuecomment-6176150339)
adds that a private logical-DPI rendering showed the title, Profile, registration
owner, provider-control notice and Cancel without clipping, and that the window was
DPI-unaware at 96 DPI on a 192-DPI monitor.

**Inference and limits:** The notice told users to choose an owned Cancel that WAM
disabled while its prompt was shown. This one observation informed the owner's parent
selection decision. Owned Cancel, notice layout through UIA, keyboard navigation, focus
between the parent and WAM, physical readability and assistive-technology behavior were
not accepted or observed.

## Hidden Owned-Parent Interactive Observation

**Method:** A work-role WSL `force-interactive` call ran the merged hidden-parent
product under the accepted protocol at `843c4f5` (Native AOT executable SHA-256
`b9ecea4f0ec76f1ea94cb1a5826e0cfd48d6256d9b156b6715c1a125f4cfd1cc`, source tree
identical to `main-v2` at `68c5175`, SDK 10.0.402). A read-only UI Automation check bound the product process and reported only
whether a product-owned top-level window was exposed. It read nothing from WAM and took
no action. The operator received the public unofficial project, Profile and owner context
beforehand.

**Runtime observation:** No product-owned top-level window was exposed, and the operator
saw no product window. WAM appeared, and the operator cancelled it on WAM's own surface a
few seconds later. The validated result was `Cancelled/None` with no token and product
exit 1, all created processes ended normally, and no residual window remained. The exact
pre-interaction notice line was written to stderr.

**Limits:** UI Automation shows that no product window was exposed, not that none
existed. The stderr notice shows delivery to the caller, not that a user saw it. The
operator did not report the WAM title or focus. Hidden-parent lifetime-pipe EOF and
deadline were not observed; accepted lifetime evidence is reused.

## Locked-State Non-Interactive Observation

**Method:** On this Remote Desktop host, a disconnected session is the locked state. It
shows the lock screen and has no input desktop. A Boolean-only check confirmed that
exactly one session of the current user was disconnected. The call ran under the
accepted protocol at `3d7e29e`. A work-role WSL
`non-interactive-only` call then ran the hidden-parent candidate without a lifetime
pipe.

**Runtime observation:** The product returned validated `MechanismUnavailable/None` with
no token, the fixed `host_session_inactive` indication and exit 1, after only Profile
reading and account discovery and with no interactive stage. After reconnecting, the
operator saw no WAM, product window or unlock prompt.

**Limits:** On this host, a noninteractive request from a disconnected session is
rejected at local admission without a product, WAM or unlock surface. Broker behavior
under a connected lock screen or an expired credential was not observed.

## WSL Direct Invocation

**Public source findings, retrieved 2026-09-11 UTC:** Microsoft's
[WSL interoperability documentation](https://learn.microsoft.com/en-us/windows/wsl/filesystems#run-windows-tools-from-linux)
documents direct execution of Windows `.exe` files from WSL, running as the active Windows
user, with pipes and redirection. Arguments reach the Windows binary unmodified; file
arguments therefore need Windows-understood paths. Interoperability can be disabled.
These documented abstractions support using the ordinary Windows CLI as the selected
WSL caller's authentication engine. They do not establish application cancellation,
result encoding, Profile eligibility, or UI behavior for a future implementation.

The WSL decision fires RECHECK-003 and RECHECK-005. The registry evaluation also records
the retained outcomes for the other entries:

| Recheck | Source state and decision impact |
| --- | --- |
| RECHECK-003 | [Issue #460](https://github.com/AzureAD/microsoft-authentication-cli/issues/460) remains open with no comments, updated `2026-05-13T17:25:51Z`. It proposes Linux-side detection and a Windows helper, including trust, version, transport, and missing-helper failures; it does not supply an implemented bridge or require V2 to adopt one. Direct executable invocation keeps Windows-side authentication ownership without introducing that proposed forwarding layer. |
| RECHECK-003/005 | [MSAL.NET WSL guidance](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/linux-dotnet-sdk-wsl) describes Linux `BrokerOptions`, .NET 8, native libraries, and an unlocked `libsecret` keyring; it identifies WSL 2.4.13 or later for the WAM account-control dialog. Those are prerequisites of its Linux application path, not a new Linux dependency of the selected Windows executable. |
| RECHECK-005 | [PR #462](https://github.com/AzureAD/microsoft-authentication-cli/pull/462) remains open and unmerged in the retrieved public page. Its patch ends at `dea657fda153a45ffe782755f952a2890e42db13`, adding Linux broker availability/routing, a native-client redirect, OS-account listing, and an OS-default sentinel fallback. It supplies no Windows CLI forwarding protocol or V2 strict-account guarantee. Its Linux path remains outside this deployment. |
| RECHECK-001/002 | Issues [#464](https://github.com/AzureAD/microsoft-authentication-cli/issues/464) and [#465](https://github.com/AzureAD/microsoft-authentication-cli/issues/465) remain open with no comments, updated `2026-08-17T21:12:04Z` and `2026-08-27T22:02:13Z`. No new documented silent-only or strict-account contract changes the accepted V2 requirements. |
| RECHECK-004 | Issues [#459](https://github.com/AzureAD/microsoft-authentication-cli/issues/459) and [#461](https://github.com/AzureAD/microsoft-authentication-cli/issues/461) remain open, updated `2026-05-14T00:13:01Z` and `2026-05-14T00:12:30Z`. Browser/callback support is not selected by direct invocation; the existing bounded disposition remains applicable. |
| RECHECK-006 | [Issue #398](https://github.com/AzureAD/microsoft-authentication-cli/issues/398) remains open with no comments, updated `2024-08-13T16:18:59Z`. No cache fallback or secure-store support claim changes. |
| RECHECK-007 | The same-day [Client Profile and tenant assessment](windows-msal-account-metadata.md#client-profile-and-tenant-mapping-assessment) remains applicable. Neither direct invocation nor another Azure DevOps consumer enables a Profile or enlarges the observed account/host coverage. |

### Bounded WSL Transport Recheck

**Public source findings, retrieved 2026-10-09 UTC:** The bounded direct-invocation
batch reevaluates RECHECK-003/005. [Issue #460](https://github.com/AzureAD/microsoft-authentication-cli/issues/460)
is still open, updated `2026-05-13T17:25:51Z`. [PR #462](https://github.com/AzureAD/microsoft-authentication-cli/pull/462)
was merged on `2026-09-17T19:33:15Z`; this supersedes the earlier retrieved
open/unmerged status, without rewriting the fixed patch findings. Its Linux broker
path and the current [MSAL.NET WSL guidance](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/linux-dotnet-sdk-wsl)
do not themselves establish Windows CLI result transport, strict selected-account
behavior or this fork's WSL acceptance. The guidance still describes Linux broker
packages, native dependencies and an unlocked keyring. The selected deployment
remains direct Windows executable invocation with Windows WAM; it adopts no Linux
broker, keyring, forwarding service or new dependency.

The registry evaluation retains RECHECK-001/002's interaction/account constraints,
RECHECK-004's unselected browser path, RECHECK-006's state ownership and RECHECK-007's
experimental Profile limits. RECHECK-008's exact candidate/dependency basis and
RECHECK-009's provider-failure mapping are unchanged. This recheck authorizes no
execution, enables no Profile and makes no runtime, release or support claim.

## Windows Slice Dependency and Host Contracts

**Public-source findings, retrieved 2026-09-11 UTC:** These inputs supplement the
existing MSAL/probe baseline for the concrete Windows Slice. They do not report a new
build, authentication attempt, account observation, or support result.

- The pinned [.NET 10 release metadata](https://github.com/dotnet/core/blob/3b2b11b56f0dfa0f4c273fed44efbc32342ea475/release-notes/10.0/releases.json)
  records the September 8 release, SDK 10.0.401, runtime/Windows Desktop 10.0.12, public
  Windows x64 artifacts, and the November 14, 2028 lifecycle end. The
  [Windows Forms overview](https://github.com/dotnet/docs-desktop/blob/7cf14527a627b58f7c0e7ec01464d417495fd8b4/dotnet-desktop-guide/winforms/overview/index.md)
  describes the maintained Windows UI framework. These published contracts support a
  .NET 10 Windows host design; the existing .NET 8 experiment did not execute that host.
- MSAL 4.83.1's [Broker project](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/Microsoft.Identity.Client.Broker.csproj)
  targets .NET Standard 2.0 and references the MSAL client and NativeInterop. This supports
  consuming the existing authentication dependency baseline from a newer compatible
  managed runtime; it is not an end-to-end compatibility test.
- The concrete [ClientApplicationBase](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/ClientApplicationBase.cs#L48-L64)
  exposes `GetAccountsAsync(CancellationToken)`. Its interface's older overload does not
  express that parameter. The pinned [RuntimeBroker discovery path](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/RuntimeBroker.cs#L571-L632)
  passes the request cancellation token to native discovery; the selected-account silent
  and interactive paths in the same file also pass it to native token operations. This
  supports correctly wiring cancellation, not an assertion that all external work is
  synchronously rolled back when it is requested.
- The pinned [WAM guide](https://github.com/MicrosoftDocs/microsoft-authentication-library-dotnet/blob/1cb0d817cf6e13e59667137a7765c70a7b1f6fb8/msal-dotnet-articles/acquiring-tokens/desktop-mobile/wam.md)
  describes WAM-managed token maintenance, parent HWND requirements, the broker redirect
  URI, automatic browser fallback, and the requirement for an active interactive Windows
  user session. Windows services and impersonation are not eligible substitutes. The
  [existing probe basis](windows-msal-account-metadata.md#windows-msal-probe-basis) identifies the availability API and
  rejecting custom-web-UI seam used to prevent a browser fallback. The availability API
  is obsolete but remains present in the pinned version; it is not a new public contract
  that V2 owns.
- The pinned [AuthenticationResult](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/AuthenticationResult.cs#L257-L337)
  exposes account, actual token tenant, expiry, token type, scopes, and correlation ID,
  but no literal final authority endpoint URI. A canonical result authority must be
  described as a representation of the validated single-cloud context and actual tenant,
  not misreported as a captured endpoint or filled from the initial routing alias.
- Windows [anonymous-pipe operations](https://github.com/MicrosoftDocs/win32/blob/b46b3638e4691f4f451590e1f861db27649849bf/desktop-src/ipc/anonymous-pipe-operations.md)
  document EOF/error when all writers close, blocking writes when the buffer fills, no
  overlapped anonymous-pipe operations, and handle release at process termination. The
  [WSL interoperation guidance](https://github.com/MicrosoftDocs/WSL/blob/7b28cc1ee9b8ff672ada5e1c6c326d3573d703e5/WSL/filesystems.md)
  describes Windows executable invocation and pipeline interoperation. Neither is a
  guarantee that a Linux signal or parent death directly cancels Windows authentication.

**Design inference:** A single .NET Windows process can use explicit cancellation-aware
MSAL calls, an owned parent window, an optional caller-lifetime pipe, and a finite process
shutdown boundary without introducing a bridge or repairing OS internals. Broker-managed
reuse avoids an additional app-owned store and its namespace/locking obligations. The
absence of a public durable-write receipt still requires the accepted persistence warning.
Concrete semantics are owned by the [Windows design](../../designs/windows-ado-authentication.md),
not by these source findings. Synthetic and real-platform validation remain separate.

### Console Parent Versus a Separate Desktop Surface

**Public-source findings, retrieved 2026-10-10 UTC:** The current Microsoft
[WAM guide](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam#parent-window-handles)
requires an explicit parent HWND. Its console example calls
`GetConsoleWindow()` followed by `GetAncestor(..., GA_ROOTOWNER)`; it does not
create an application window or require Windows Forms, WPF, or WinUI. The same
guide's integration best practices recommend giving the user context before a
user-triggered authentication request, drawing a UI or window, and trying silent
acquisition first. The sample's parent selection and that user-experience
recommendation are distinct; neither proves that a separate visible V2 window is
the only adequate presentation for an explicitly invoked CLI.

The [GetConsoleWindow contract](https://learn.microsoft.com/en-us/windows/console/getconsolewindow#remarks)
states that a pseudoconsole supplies a message-queue handle whose associated
window is not displayed locally. A nonzero handle therefore does not establish
a visible terminal parent. [GetAncestor](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getancestor)
with `GA_ROOTOWNER = 3` walks parent and owner relationships; it is not a
promise that the result is visible or that an intermediate shell creates a
window. [IsWindowVisible](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iswindowvisible)
reports the `WS_VISIBLE` chain, not unobscured presentation, focus, or WAM
eligibility. [IsWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-iswindow)
is an instantaneous existence observation; a borrowed handle can be destroyed
or recycled afterward.

**Hypothesis:** An already installed `pwsh.exe` may give a WSL-launched child
a usable existing console parent without a separate V2 desktop surface. A shell
process parent is not itself a window owner, and inherited pseudoconsole state
may leave this hypothesis false. The
[bounded console-parent comparison](#console-parent-feasibility)
tests only that prerequisite. A positive visibility observation still requires
exact-product WAM, context, cancellation, and lifetime evidence before replacing
the currently selected host. No new provider, Profile, browser fallback,
registration, account state, dependency version, or Native AOT publishing mode
is selected by this assessment.

**Runtime prerequisite comparison, 2026-10-10 UTC:** Under the accepted
[Console Parent Feasibility protocol](#console-parent-feasibility)
at commit `39ff59bf20cd5f68d465570766f99eb287b69745`, the designated WSL2
caller ran each ordinary launch once, using Windows PowerShell 5.1 x64 as the
same credential-free console surrogate:

| Launch | Console HWND present | Root-owner HWND present | Exit / stderr |
| --- | --- | --- | --- |
| WSL directly to Windows PowerShell | No | No | 0 / empty |
| WSL to installed PowerShell 7, then the same Windows PowerShell | No | No | 0 / empty |

Both complete reports had `ObservationFailed=false`; all seven handle,
existence, visible-style and equality flags were false. The transient source
was SHA-256 `896057bbf4a086a0f95cdeaefd5df0f646393c74f3363b2c767017c1167b50ee`.
The optional fixed installed `pwsh.exe` was a 301,368-byte AMD64 console image,
SHA-256 `bfb46af89433268872ddb43d1ca7a3f433452ee91ed356a9786940f90118e285`,
independently admitted only for this metadata role under the workstation trust
model. Its release version, signature, released-artifact provenance and
historical continuity were not established by the bounded correspondence read.

Neither call invoked V2, MSAL or WAM, created an application window, read account,
Profile or cache state, or needed manual interaction. Account roles, authority,
scopes and provider versions are nonapplicable. Telemetry was opted out and
PowerShell update checks disabled before startup; no network operation was
requested. Both ordinary requested roots returned normally, the outer shell
returned the child's exit code, all safe streams were complete, and no forced
termination or retry occurred. Safe captures are retained locally; no account or
installation state was changed by the recipe. Preserve the existing ordinary
interop console-host lifecycle basis rather than claim an exhaustive OS-process
survey. The whole conservative three-synthetic-start allocation and one metadata
and one collection pass remain spent; real-account consumption was zero.

**Inference and limit:** In this sampled launch context, an intermediate ordinary
PowerShell process did not supply the surrogate with a console HWND. Wrapping
alone therefore does not establish the proposed existing-console parent source.
This is not exact Native AOT product evidence, a claim about every WSL terminal,
or evidence that WAM requires a visible application window. It does not select a
replacement parent, validate hidden-parent WAM behavior, or invalidate prior
accepted authentication results.

## Dated Observation Records

These records were moved unchanged from the V1 baseline under
[Issue #509](https://github.com/hcoona/microsoft-authentication-cli/issues/509). They keep
the exact protocol revisions, candidate hashes and accounting of each dated observation;
the sections above state their methods and bounded conclusions.

### Hidden Owned-Parent Candidate Observations

**Bounded runtime observations, 2026-10-10 UTC:** The accepted
[candidate protocol](#hidden-owned-parent-candidate)
at `effe583b3a8cecd7ae64280866b7383756cf75e1` admitted isolated source
[`8e3ac5c76964b94ba27a9c636bd224a981a4104d`](https://github.com/hcoona/microsoft-authentication-cli/commit/8e3ac5c76964b94ba27a9c636bd224a981a4104d),
tree `8acdf7d5a26346ac1d24417fe78cc89d39e3a008`. It omits the existing parent
show call, sets the explicit unofficial MSAL broker title and adapts the existing
visibility assertion. It does not replace the selected candidate.

On the designated Windows 11 x64/WSL2 host, the admitted installed .NET SDK
10.0.401/runtime 10.0.12, MSAL/Broker 4.83.1, NativeInterop 0.20.3, MSVC
14.51.36231 and Windows SDK 10.0.26100.0 completed two offline restores, the
existing test build and one Native AOT publication. All four phases exited 0
without forced termination. Source pins, warm public cache/locks, compiler exits,
PE checks and bounded artifact readbacks establish the provenance chain; no
clean-restore or deployment claim follows. The executable is 8,922,624 bytes,
SHA-256 `7e3409563adb6ef94020dfb085058ac513a5999720fb64a01f060e0c4cd40c65`;
the native runtime hash remains unchanged.

Exactly four existing credential-free Windows cases passed with complete standard
TRX and normal runner completion: hidden owned-parent readiness with immutable
Profile/STA checks, cancellation during creation, internal close without caller
cancellation, and completion waiting for actual UI-thread exit. These establish
synthetic own-window/lifecycle behavior, not WAM or human UX.

The selected personal silent-only request then returned a valid
`MechanismUnavailable/None`, product exit 1, complete stdout/stderr and normal
completion without stopping a Linux relay. All six public inputs and Windows-local
selection preflight passed; token/result validation remained Windows-local after
bounded confidential WSL memory/pipe transport. The fixed phase and mechanism
indication were not retained by that caller, so the blocking stage and cause are
unknown. No interactive permission, prompt or configuration/cache repair was
requested. Existing account/cache state was not assumed clean; any ordinary
provider-managed effects remain unconfirmed. No private payload was retained.

The whole preparation allocation `2/2/1/4` and real comparison reservation
`3/3/3/1` remain spent, including skipped interaction and reuse. Metadata and safe
collection each reserved three preparation passes and three real passes. Failed
history, unused charges and `noExperimentLive=false` remain preserved. The
[Issue #108 evidence](https://github.com/hcoona/microsoft-authentication-cli/issues/108#issuecomment-6093678279)
and independent reviews retain source/call/actual correspondence.

**Bounded diagnostic and fresh-comparison observations, 2026-10-10 and
2026-10-11 UTC:** The accepted correction protocol at
`05e4f1c214d24ce3bfa9da0dc33a2f21e7eb0b50` admitted one separately charged
silent diagnostic using the same candidate and selected request. It returned a
valid `MechanismUnavailable/None`, product exit 1, complete streams and normal
completion. Existing fixed diagnostics reported `host_session_inactive` with
`profile_read` and `account_discovery` phase names. This establishes failure of
the calling Windows session's Active-state gate, not the exact WTS substate or
physical cause. The caller's result-receipt timestamp was corrected to occur
immediately after transport and before diagnostic projection; independent finding
triage and exact-source admission accepted that correction.

After the required ordinary Windows desktop connection/usable-state action, the
one admitted fresh comparison ran an initial silent assessment and a fresh-process
silent reuse. Both returned `Success/Silent`, product and foreground exit 0,
complete stdout/stderr, passing normal-result and selected-account metadata
checks, six verified public inputs, and preflight exit 0 with empty stderr.
They completed in 6,635 ms and 2,558 ms, respectively, with normal owned-root
completion and no stopped Linux relay. Fixed observations included
`profile_read`, `account_discovery`, `silent_acquisition` and
`candidate_validation`; no mechanism-unavailable indication was observed.
The first success skipped the conditional interactive case without refund.
No WAM UX observation follows from either noninteractive success.

The source, candidate, Profile, selected personal request, common authority,
Azure DevOps `.default` scope, Windows-local private configuration and validator
remained unchanged. Results traversed only the accepted bounded confidential WSL
memory/pipes before Windows-only validation; no private payload or raw diagnostics
were retained. No prompt was forced, and no account/cache/configuration repair or
service request occurred. Persistence remains explicitly unconfirmed; the second
success establishes fresh-process reuse rather than a durable-write receipt.
Independent actual acceptance passed before reuse and after the final call.

The diagnostic reservation `1/1/1/0` and fresh-comparison reservation `3/3/3/1`
remain spent in addition to the initial failed comparison. Each added actual-call
slot reserved one metadata and one safe-collection pass, including skipped
interaction. Current cumulative technical consumption is `58/227/18/807` and
real consumption is `47/47/47/13`, with `49/49/49/39` real units remaining.
Metadata consumption is 87 passes and safe collection is 508 passes. The closed
comparison preserves all failures, unused charges and historical
`noExperimentLive=false`; it admits no further call under this recipe.

**Limit:** These observations neither validate nor reject hidden-parent WAM behavior.
The later [interactive observation](#hidden-owned-parent-interactive-observation)
covers presentation and human cancellation; by
[owner decision](https://github.com/hcoona/microsoft-authentication-cli/issues/108#issuecomment-6182996452)
WAM title and focus are not required. Notice delivery to users and the remaining real lifetime
obligations stay open. Reuse sufficient accepted native,
work and prior WSL evidence within its original limits.

### Actual WSL Deadline Assessment Observation

**Bounded runtime observation, 2026-10-11 UTC:** The accepted
[deadline protocol](#actual-wsl-deadline-assessment)
at `d2eecc84236ce28a394314a79af0265cd923637d` admitted one noninteractive
personal-account request with a one-second timeout on the unchanged selected
Native AOT candidate, not the hidden-parent variant. The selected Profile,
common authority, Azure DevOps default scope, existing private Windows
configuration and Windows-only validator were unchanged.

The actual request returned valid `MechanismUnavailable/None`, product and
foreground exit 1, complete streams, six verified public inputs, preflight exit 0
with empty stderr, and normal owned-root completion without stopping a Linux relay.
The fixed mechanism indication was `host_session_inactive`; observed phase names
were `profile_read` and `account_discovery`. The complete foreground call took
2,304 ms, including preflight and validation; this is not a measured product
deadline interval. It establishes the calling session's Active-state obstruction,
not its exact WTS substate or physical cause. No dependency-pending-at-expiry,
authentication success or actual timeout evidence was obtained.

Independent actual acceptance passed. The full `1/1/1/0` real reservation, one
metadata pass and one safe-collection pass remain spent. Cumulative real
consumption is `48/48/48/13`, with `48/48/48/39` remaining; technical consumption
remains `58/227/18/807`. Metadata consumption is 88 passes and safe collection is
509 passes. The failed assessment is closed without refund or automatic retry;
historical failures and `noExperimentLive=false` remain preserved. No private
payload, raw diagnostics, state repair, forced interaction or network manipulation
was retained or performed.

**Limit:** This valid failure does not complete actual deadline, caller-death,
EOF-during-WAM, hidden-parent UX or whole-Slice acceptance. Any new assessment
requires its accepted protocol and current operating-basis gates; reuse sufficient
native, work and WSL success evidence.

**Bounded reassessment observation, 2026-10-11 UTC:** After the separately admitted
ordinary-desktop state action, the same request ran once under the supplement at
`a9aa414ca305c8efd8b57f3454e0a1a04340f384`. Preflight returned exit 0 with empty
stderr and six verified public inputs. The product returned exit 2; both streams
completed and all created roots ended normally without stopping a Linux relay.
Result validation failed, so no validated outcome, route or typed timeout was
obtained. Fixed phase observations recorded `profile_read` and `account_discovery`,
with no mechanism indication. They establish neither pending WAM work nor the exact
exit-2 path. The 4,472 ms whole foreground duration is not the product entry interval.

Independent actual and scoped-completion review accepted this failure. The supplement
ended with its full charge retained and no retry or refund. Real consumption is
`49/49/49/13`, remaining `47/47/47/39`; technical consumption remains `58/227/18/807`.
Metadata consumption is 89 passes and safe collection 510 passes. Historical failures,
reservations and `noExperimentLive=false` remain preserved. No raw result, private
payload or diagnostics were retained. Reuse existing sufficient success evidence;
actual deadline and the other incomplete lifetime/UI claims remain open.

### Windows Dispatch Correction Validation

**Bounded controlled observation, 2026-10-11 UTC:** The accepted source
`2d79f3a07408cffe024ce9d888d1cca272f5c926`, tree
`7e6b8531f83900d211780ac1856561958b1e46cd`, dispatches the request body so a
synchronous dependency prefix cannot withhold the selected terminal task from
its caller. The prior four-case red observation and 72-case green observation
establish that controlled source behavior; they do not identify the real exit-2
failure cause.

Under the [Windows dispatch protocol](#windows-request-lifetime-dispatch-validation)
at `7f1c8de8d05d6a55e2fe51ed9cbd120e1cdf33ca`, two offline restores, the existing
Windows scenario build and Native AOT publication all returned exit 0. The four
fixed compiler reports indicated normal created-root completion without forced
termination. Exact EXE, PDB and runtime content, EOF and non-ctime identity checks
joined retained copies to the accepted source/toolchain/dependency basis. The EXE
is Windows x64 PE32+ without a CLR directory, SHA-256
`f6a045888160ce1734396392e2bb27d1eb6800bf7a39886868a2d54eab4e00d9`.

The 12 existing process cases all passed through the standard runner and TRX:
normal result despite broken diagnostics, initially closed input, writer closure,
uncooperative deadline, six owned-host cancellation/fault/thread/callback/drain
cases, and two default HTTP ownership/disposal cases. The runner returned exit 0
with normal created-root completion and no forced termination. Source assertions
bind their child exit, capture and applicable drain observations; runner return
alone is not global closure. Independent compiler/artifact and final actual
reviews passed before admitting the candidate for separately authorized real calls.

Accounting1004 closes the batch without a debit or refund. Technical consumption
is `61/231/19/819`, remaining `39/169/41/381`; real remains `49/49/49/13`.
Metadata consumption is 92 passes and collection 513. All historical flags, failed
and unused reservations and `noExperimentLive=false` remain preserved. No private
input, account effects or real authentication were part of this controlled batch.

**Limit:** These synthetic cases establish their controlled behaviors, not actual
WAM cancellation, a real typed WSL timeout, human UI evidence, caller death or full
Slice acceptance. Reuse the accepted native personal/work and actual WSL successes
within their original limits. The isolated hidden-parent variant remains unpromoted.

**Actual followup, 2026-10-11 UTC:** Under the one-shot deadline protocol at
`b4ff21ffcda1177a4027f4108f83b01d2e2a6f5a`, the corrected candidate returned
valid `MechanismUnavailable/None`, exit 1, with fixed `host_session_inactive`.
Six public inputs and preflight exit 0 with empty stderr passed; both streams and
created roots completed normally without stopping a Linux relay. The 987 ms caller
launch-to-full-streams interval and 2,421 ms complete foreground interval establish
neither the product entry clock nor dependency work pending at expiry. No typed
Timeout was obtained, and the exact WTS substate or physical cause is unknown.

Independent actual acceptance passed. Accounting1006 closes only this failed
assessment without a debit or refund: real `50/50/50/13`, remaining `46/46/46/39`;
technical unchanged `61/231/19/819`, metadata 93 and collection 514. All historical
flags and charges remain. The one-shot recipe is exhausted; no retry is running.
The prior controlled successes and actual lifetime/UI evidence limits still apply.

**Same-request reassessment, 2026-10-11 UTC:** The accepted supplement at
`0d6981cda4fa7f17d571b6c610dfcfceb3a166f2` ran once after its required desktop
state action, on the same candidate and request. Six public inputs and preflight
exit 0 with empty stderr passed. Product exit 2 and both complete streams were
observed; all created roots closed normally without stopping a Linux relay.
Windows-only result validation accepted no typed outcome. This is a transport
failure, not a typed Timeout. Fixed phase observations included `profile_read`
and `account_discovery`; no mechanism indication was observed. The 2,116 ms
caller launch-to-full-streams interval and 5,954 ms complete foreground interval
do not identify the product entry clock, watchdog path, dependency pending at
expiry or the internal exit-2 cause.

Independent actual acceptance passed. Accounting1008 closes only this assessment
without a debit or refund: real `51/51/51/13`, remaining `45/45/45/39`; technical
unchanged `61/231/19/819`, metadata 94 and collection 515. All historical flags
and charges remain. The supplement is exhausted, with no automatic retry.


**Observation correction, 2026-10-11 UTC:** The one admitted diagnostic call at
`a405d92bd288a589bfd7a16ba78c735f6ec8df46` preserved existing fixed telemetry.
Profile reading started at 2 ms and completed at 6 ms; account discovery started
at 6 ms, with no completion event observed. A fixed request-completion event
reported `timeout` at 1,010 ms. Product stdout contained 54 bytes; its exit was 2,
and Windows-only validation accepted no typed result. Both streams and created
roots completed normally without stopping a Linux relay. Preflight verified six
public inputs, exit 0 and empty stderr. No raw payload was retained or inspected.

The caller launch-to-full-streams interval was 2,092 ms; whole foreground time was
4,417 ms. These intervals are not exact Windows entry/exit measurements. The
fixed timeout-completion observation does not identify the watchdog path or a
specific unfinished dependency or cleanup. Missing optional phase completion is
not proof that the operation never finished. The accepted design permits exit 2
if required operation/callback/host/pipe/HTTP drain cannot finish within its original
bound, including after result writing; this source rule is not a diagnosis of
the actual exit. Complete typed Timeout acceptance remains open.

Independent actual acceptance passed. Accounting1010 closes only this diagnostic
failure without a debit or refund: real `52/52/52/13`, remaining `44/44/44/39`;
technical unchanged `61/231/19/819`, metadata 95 and collection 516. Historical
flags and full charges remain. The diagnostic recipe is exhausted. Another
identical one-second call has no established additional evidence value.


**Normal baseline followup, 2026-10-11 UTC:** One noninteractive 120-second
personal WSL request at `491067629530465d523138014d736b8f35ef17c8` returned
validated `InteractionRequired/None`, exit 1. Preflight verified six public inputs,
exit 0 and empty stderr. Both streams and created roots completed normally without
stopping a Linux relay. Fixed telemetry recorded Profile completion at 5 ms,
account discovery from 5 to 4,430 ms and request completion at 4,431 ms. The
4,537 ms caller interval is not an exact Windows entry/exit measurement.

This result establishes the completed request path on the corrected candidate,
not a successful token, a cache-state cause, visible UI or general support. No
interaction was allowed. Independent actual acceptance passed. Accounting1012
closes only this failed baseline without a debit/refund: real `53/53/53/13`,
remaining `43/43/43/39`; technical unchanged `61/231/19/819`, metadata 96 and
collection 517. All historical flags and full charges remain. The recipe ended;
interaction requires separate exact admission and fresh actual readiness.

**Interactive cancellation attempt, 2026-10-11 UTC:** After fresh operator
readiness, the one admitted personal WSL `interactive-if-needed` call at
`69af581f74230b93bc2d27997cb4640bb7e6f4da` used the same corrected candidate,
selected request, Profile and Windows-only validator, retaining silent-first
behavior. It returned validated `Timeout/None`, exit 1, rather than Cancelled.
Preflight verified six public inputs, exit 0 and empty stderr. Both streams and
created roots completed normally without stopping a Linux relay; no raw result,
private selector or provider diagnostic was retained.

Standard local telemetry reported request completion as `timeout` at 120,006 ms.
Account discovery ran from 6 to 2,059 ms, interaction opening from 2,059 to
2,072 ms and interactive acquisition from 2,072 to 120,036 ms; interaction
closure completed at 120,036 ms. The caller launch-to-full-streams interval was
120,157 ms and the complete foreground interval 122,485 ms. These caller intervals
are not exact Windows entry/exit measurements. The valid typed result establishes
an actual WSL timeout under the ordinary 120-second deadline, without another
one-second assessment. Phase observations alone do not establish visible WAM UI
or a particular provider-internal cause.

The operator did not continuously watch and saw no WAM window on returning to
check. Earlier appearance, an explicit cancel action and disappearance timing
were not observed. The user-cancellation claim therefore remains incomplete.
This one-shot recipe ended with its alternative result, without retry or refund.
Independent actual acceptance passed. Accounting1014 closes only this call and
retains its full precharge: real `54/54/54/14`, remaining `42/42/42/38`;
technical unchanged `61/231/19/819`, metadata 97 and collection 518. Historical
flags and charges remain. Caller death, EOF during actual WAM, denial, applicable
hidden-parent UX and full Slice acceptance remain separate obligations.

**Interaction-pipe EOF, 2026-10-12 UTC:** After fresh operator readiness, the
single admitted personal WSL `interactive-if-needed` call at
`87676fc6ae822da655ff30e41cff7372eb587fae` reused the same corrected candidate,
selected request, Profile, Windows-only validator and bounded transport. The
existing sole lifetime writer closed while the product was running, 15,022 ms
after the caller's product launch. The result was validated `Cancelled/None`,
product exit 1, with no token. Both streams and created roots completed normally
without stopping a Linux relay. Preflight verified six public inputs, exit 0
and empty stderr. No raw result, private selector or provider diagnostic was
retained.

Standard local telemetry reported `cancelled` completion at 14,938 ms. Account
discovery ran from 7 to 4,493 ms, interaction opening from 4,493 to 4,512 ms and
interactive acquisition from 4,512 to 14,971 ms; interaction closure completed
at 14,971 ms. Caller launch-to-full-streams was 15,105 ms and the complete
foreground interval was 17,220 ms. Caller and telemetry clocks have separate
origins; these values do not establish exact Windows entry/exit or UI timing.

The operator observed only a Win32 application window and did not observe a WAM
window. Window attribution, explicit user action and later window state were
not reported. Phase events do not prove visible WAM or its shutdown. The timed
pipe-cancellation case passed; EOF during actual WAM remains incomplete. The
one-shot recipe ended without retry or refund.

Independent actual acceptance passed. Accounting1016 closes only this call and
retains the whole1015 precharge: real `55/55/55/15`, remaining `41/41/41/37`;
technical unchanged `61/231/19/819`, metadata 98 and collection 519. All historical
flags and charges remain. User cancellation, caller death, denial, applicable
hidden-parent UX and full Slice acceptance remain separate obligations.

**Caller-death assessment, 2026-10-12 UTC:** The single admitted personal WSL
noninteractive call at `fa52b2ab35bd80b9bde04386e835d44fcdf40bb9` omitted the lifetime
flag and used null stdin. It reused the accepted candidate, request, Profile,
private configuration and Windows-only validator. The product returned before
caller termination: valid `MechanismUnavailable/None`, product exit 1, validator
exit 0 and `ProtocolValid=true`. Both streams and created roots completed normally,
without stopping a Linux relay. Preflight verified six public inputs, exit 0 and
empty stderr. The complete foreground call exited 0 because a validated early
alternative is retained as itself; the caller-death case did not pass.

The fixed diagnostic was `host_session_inactive`. Standard telemetry recorded
Profile completion at 7 ms, account discovery from 7 to 10 ms, and request
`mechanism_unavailable` completion at 10 ms. These phase names do not establish
provider discovery or acquisition. Exact session state and the cause of the
rejection remain unknown. Caller launch-to-full-streams was 117 ms and total
caller elapsed time was 2,747 ms, with separate origins from product telemetry.
No caller signal, native-wait submission or operator action occurred. No UI,
Linux-caller-death or independently held native-exit evidence was established.

The one-shot recipe ended without retry or refund. Its whole1017 precharge remains:
real `56/56/56/15`, remaining `40/40/40/37`; technical unchanged `61/231/19/819`,
metadata 100 and collection 520. All historical flags, failures and charges remain.
The fixed host rejection requires an actual connected/unlocked desktop action
before a separately admitted fresh assessment. Caller death, actual WAM EOF,
human cancellation/denial, applicable hidden-parent UX and full Slice acceptance
remain incomplete.

**Fresh caller-death assessment, 2026-10-12 UTC:** After the required desktop
state action and fresh readiness, the sole fresh noninteractive assessment at
`1e1813e0620eeed65a44b8756209ceb655b85cd2` reused the same accepted candidate,
request, inputs and validator. It returned valid `InteractionRequired/None`,
product exit 1, validator exit 0 and `ProtocolValid=true`. Both streams and created
roots completed normally without stopping a Linux relay. Preflight again verified
six public inputs, exit 0 and empty stderr. Caller launch-to-full-streams was
2,639 ms and total caller elapsed time was 4,896 ms. Standard telemetry recorded
Profile completion at 7 ms, account discovery from 7 to 2,534 ms and request
`interaction_required` completion at 2,534 ms. These origins differ; phase names
do not establish provider acquisition, the account-state cause or exact native
entry/exit timing. The prior fixed host rejection was not observed in this call.

The separate stock PowerShell observer bound a native instance and emitted its
fixed Ready fact. The live orchestration parser removed CSI terminal escapes but
omitted the OSC title prefix, so it failed to recognize that fact on the same
line. The retained safe output establishes emission only. No receipt clock
sample, pidfd, caller signal or `CallerEnded` marker followed. The caller's
original foreground call exited 0 normally. The observer reached its finite
marker deadline and exited 1 with `ObservationFailed=true`; it reported no native
exit. The known original Linux relay was absent at launch plus 17,346 ms. That
scoped absence is separate from native-handle exit evidence. The original handles
are terminal, but caller-death ordering and held-native completion remain unproved.

The sole fresh supplement is exhausted without retry or refund. Whole1019 retains
real `57/57/57/15`, remaining `39/39/39/37`; technical unchanged `61/231/19/819`,
metadata 102 and collection 521. Historical flags, failures and charges remain.
No interaction was allowed and no UI evidence was obtained. Caller death, actual
WAM EOF, human cancellation/denial, applicable hidden-parent UX and full Slice
acceptance remain incomplete. This observation admits no further execution.


**Ready-framing correction assessment, 2026-10-12 UTC:** The separately admitted
noninteractive assessment at `240d5a6b865b02f50dd0d8a8b638371a7306fb09` correctly
decoded the stock observer's fixed Ready fact after removing CSI/OSC framing.
The initial 750-ms tool return contained framing only; the next empty-input
poll took approximately five seconds. The receipt-to-Windows-Ready difference
was 5,292 ms, so the required clock bracket failed. Linux wall/monotonic
consistency and the original Ready window passed, but neither waives that
bracket. No pidfd, signal or `CallerEnded` marker was submitted.

The caller returned valid `InteractionRequired/None`, product exit 1, validator
exit 0 and `ProtocolValid=true`. Both streams and created roots completed normally
without stopping a Linux relay. Preflight verified six public inputs, exit 0 and
empty stderr. Caller launch-to-full-streams was 3,492 ms and total elapsed time
was 5,870 ms. Standard telemetry recorded Profile completion at 6 ms, account
discovery from 6 to 3,389 ms and request `interaction_required` at 3,390 ms.
These separate origins do not establish exact native entry/exit or a pending
provider at receipt. No interaction was allowed and no UI evidence was obtained.

The observer bound a native instance, then reached its finite marker deadline
and exited 1 with `ObservationFailed=true` and no native exit observation.
The caller's original exited 0; the known relay was absent at original launch
plus 17,646 ms. These are scoped observations, not held-native completion or
caller-death evidence. The sole correction recipe is exhausted without retry
or refund. Whole1021 retains real `58/58/58/15`, remaining `38/38/38/37`,
technical unchanged `61/231/19/819`, metadata 104 and collection 522. Historical
flags, failures and charges remain. Stop repeating fast noninteractive calls;
remaining caller-death and WAM interaction observations need their own admitted
pending-case basis. Actual WAM EOF, human cancellation/denial, applicable
hidden-parent UX and full Slice acceptance remain incomplete.

**Interactive-permission caller-death assessment, 2026-10-12 UTC:** The sole
personal WSL assessment at `234061777bfb1f9153f8b0489181999239e28fc8` reused
the accepted visible-parent candidate, selected inputs and null stdin without a
lifetime-pipe option. It permitted ordinary interaction while retaining silent-first
behavior. Preflight verified six public inputs, exit 0 and empty stderr. The
stock observer's first 2,000-ms return contained one exact Ready fact after
CSI/OSC decoding. Both clock-bracket inequalities, Linux wall/monotonic
consistency and the original Ready window passed.

The caller-only pidfd signal succeeded. The original Linux caller then exited
143 under default SIGTERM disposition, without a normal final result, before
`CallerEnded` was submitted to the same observer. This establishes the scoped
Linux caller termination and submission order. The observer's PTY echoed the
public marker, but its final facts reported `CallerEndedMarker=false`,
`LiveAfterCallerEnded=false`, `NativeExitObserved=false` and
`ObservationFailed=true` at `CallerEndedMarker`; its original exited 1.
Submission and echo do not establish native receipt. The exact reader failure
cause and native exit remain unknown. The echo had no trailing newline, so the
aggregate line decoder did not extract the final JSON; the final original
return separately retains those fixed facts without upgrading live ordering.

One read of the known original Linux relay observed absence at launch plus
39,571 ms, within the original 130-second interval. This is separate from
held-native exit evidence. The one-shot assessment is exhausted without retry
or refund. Whole1023 retains real `59/59/59/16`, remaining `37/37/37/36`,
technical unchanged `61/231/19/819`, metadata 106 and collection 523; all
historical flags and charges remain. No typed authentication result was obtained
after caller death. Full caller-death acceptance, actual WAM EOF, human
cancellation/denial, applicable hidden-parent UX and full Slice acceptance
remain incomplete. UI observations require a separate actual operator report.

The operator reported no window observed before leaving and no window present
after returning. The intervening interval was unattended. This establishes only
those observations, not absence throughout the request or provider termination.

**Console marker and subsequent caller-death observations, 2026-10-12 UTC:**
At accepted target `50e11b24512ecbc0bbb3982e8d99af7164c7f5a6`, one
credential-free stock PowerShell check confirmed exact `CallerEnded` receipt
with CR termination and exited 0. Its elapsed time was 1,497 ms. This establishes
the current console delivery path, not the prior LF failure cause. Independent
actual acceptance passed; accounting1026 closes only that check without refund.

The subsequent separately charged WSL assessment again bound the native instance.
Both original clock qualifications passed; caller-only pidfd SIGTERM succeeded,
and the original Linux caller exited 143 before confirmed CR marker receipt.
The observer then exited 1 at `LiveAfterCallerEnded`, reporting no native exit.
Its source rejects either an already exited subject or elapsed budget expiry
before reading the retained handle's exit code. The result does not distinguish
those alternatives or establish the native exit code/time. The final relay
observation was attempted at launch plus 132,603 ms; its 130-second guard
prevented a process-state read. Current relay completion remains unobserved.

The sole assessment is exhausted, with no retry or refund. Whole1027 retains
real `60/60/60/17`, remaining `36/36/36/35`, technical `61/231/19/820`,
metadata 109 and collection 525, including all historical uncertainties and
flags. These facts establish caller termination and marker receipt only;
full caller-death acceptance and the remaining WAM/human/UX obligations stay
open. Independent scoped actual acceptance passed; accounting1028 closes only
this failed assessment without debit or refund. The observation correction
removes the console handshake and post-marker liveness gate; it does not retroactively repair this missing exit.

**Completion observation verification, 2026-10-12 UTC:** Under accepted target
`b8df232675e4dbcabefe3387e95a46f8713a3763` (PR #445), the three credential-free
stock checks each passed once without a source change or retry. The retained
handle reported exit 7 for an already exited child and exit 9 after waiting for
a live child. A zero-budget wait correctly reported completion unavailable;
that same child subsequently exited naturally within its original test bound.
All three original calls exited 0 and all owned subjects were observed ended,
without forced cleanup, account access or an authentication product start.

The checks establish the completion block's three paths on this host. Native
product binding and real WAM/UI behavior are not established by these stock
subjects. The existing standard-telemetry caller projection has independent
source admission; no product instrumentation changed. Keep these observation
mechanics fixed for the remaining acceptance scenarios. Independent actual and
scoped-completion acceptance passed; accounting1030 closes only this three-check
batch, retaining its whole1029 charge without refund. Historical failed exits,
missing relay observations and all charges remain unchanged.

**Remaining-batch caller-death alternative, 2026-10-12 UTC:** At accepted
`7961a6c3f0e02d5a71dc3e66476dcd4576c324d5` (PR #446), the sole initial
caller-death request completed before any signal or native observer. It returned
valid MechanismUnavailable/None, product exit 1 and validator exit 0 with
ProtocolValid=true. Both streams and all created roots completed normally;
caller original269aa6 exited 0. Preflight verified the same six public inputs,
exit 0 and empty stderr. Product launch-to-full-streams was 126 ms.

The existing fixed indication was host_session_inactive. Live standard phase
facts recorded Profile completion at 6 ms, the account-discovery wrapper from
6 to 11 ms and mechanism_unavailable completion at 11 ms. These stage names
do not establish an actual provider discovery call. The exact session state
and rejection cause remain unobserved. No WAM UI or caller termination is proved.
The observation method captured this completed alternative without a source
change; it is not another observer failure or a passed caller-death case.

The initial caller-death slot is exhausted without refund. Whole1031 conservatively
retains real63/63/63/20, remaining33/33/33/32; technical61/231/19/823,
metadata114 and collection529. The two required human-action slots remain
unsubmitted. A separately admitted fresh caller-death request requires the
actual same-machine desktop reactivation and retains the original failure,
charges, historical globalfalse and all missing evidence.

**Desktop-reactivation continuation alternative, 2026-10-13 UTC:** At accepted
`2efa86b24297c19419e60cdf148bf0b5e8e8687e` (PR #447), the separately charged
personal request returned validated Success/Silent, product and validator exit 0,
ProtocolValid/MetadataValid/Passed=true, complete streams and normal completion
of all created roots. Product launch-to-full-streams was 3,954 ms. Account
discovery took 2,823 ms and silent acquisition 1,007 ms; the reported phase path
did not enter interaction. Actual visible-window state and the precise earlier
host-session rejection cause remain unobserved.

The first tool return after 3.002 seconds was empty while the same original was
still running. Startup arrived later, about 11.35 seconds after caller creation;
the same original26614 then completed normally, terminal 0 at chunk9df728.
The no-startup alternative had already withheld caller termination. No signal,
native observer, clock sample or guarded relay read ran. This observed silent
success does not satisfy caller death, WAM EOF or human cancellation, and the
initial empty return was not itself product completion.

Independent actual/scoped-completion review passed. Accounting1034 closes only
this continuation, retaining real64/64/64/21, remaining32/32/32/31,
technical61/231/19/823, metadata116 and collection530 without refund. The two
original human slots remain unsubmitted. Observation sources and product code
are unchanged; the result does not supply missing historical exit or UI evidence.

**Work-role WAM EOF alternative, 2026-10-13 UTC:** At accepted
`eb513ab82e26420441a8b6aed27b1f326d600826` (PR #448), the original precharged
WamEof slot returned Success/Silent, product and validator exit 0,
ProtocolValid/MetadataValid=true, complete streams and normal created-root
completion. Passed=false records the expected Cancelled/None mismatch; it does
not invalidate this successful authentication result. Product launch-to-full-streams
was 3,704 ms; discovery took 2,609 ms and silent acquisition 963 ms.

The phase path did not enter interaction. Actual UI remains unobserved; no
operator WAM confirmation preceded completion, no SIGUSR1 was sent and the
writer was not closed during live product execution. The same original12118
ended normally, terminal 0 at chunk339d62. No native observer, clock sample or
relay query ran. Observation mechanics and product code remain unchanged.

Independent actual/scoped-completion review passed. Accounting1036 closes only
that case without another debit or refund; consumption remains
real64/64/64/21, remaining32/32/32/31, technical61/231/19/823, metadata116 and
collection530. The prescribed human route stopped: HumanCancellation remains
unsubmitted and the earlier conditional caller-death proposal remains inert
and uncharged. This result does not establish WAM EOF, human cancellation or
caller death, and does not complete WSL or whole-Slice acceptance.

### Selected-Role Windows and WSL Acceptance Observations

**Runtime observations accepted through October 14, 2026 UTC, for Issue #108:**
The named remaining WSL cases now have independently accepted evidence at the
scopes below. Earlier alternatives, transport failures, missed observations and
charges remain historical evidence; these later results do not repair them.

The environment remains the designated Windows 11 x64 interactive user and
WSL2 Linux x64 caller, with existing selected personal/work accounts and broker
state. Reuse the accepted native personal R1/R6 and work R7/R8 acquisition and
fresh-process reuse, actual WSL success/reuse, initially closed lifetime pipe,
typed Timeout and timed Cancelled observations under their original candidate
and request bindings. Existing state does not establish clean first use or
durable persistence. Actual Git/feed operations and Profile distribution remain
outside this authentication acceptance.

The October 14 work calls use the accepted
[account-selection implementation](https://github.com/hcoona/microsoft-authentication-cli/pull/457),
source `af054606fcd6f8436b78872a3c7234539c2238cf`, accepted at
`a9e877c0bcb1c32560ebde43dcbc45a445dda71a`. Its independently accepted Windows x64
Native AOT executable has SHA-256
`4a393afdd9b537beaadddef2164e625973be882e220a94e10e629c81d1052478`.
The publication basis remains SDK 10.0.401/runtime 10.0.12, MSAL/Broker 4.83.1
and NativeInterop 0.20.3. Reuse the accepted 191 controlled cases, four unchanged
Windows construction cases and native source/artifact/dependency correspondence.
The selected implementation still shows its owned Win32 parent; it is separate
from the unpromoted hidden-parent variant.

The [accepted protocol revision](https://github.com/hcoona/microsoft-authentication-cli/blob/e0992a988fac8b5957260e8b6a167cc086a615f6/docs/research/experiments/windows-slice-validation.md)
binds the existing explicit public-client Profile, Public Cloud, selected work
role, `common` authority and admitted Azure DevOps scopes. Each October 14 call
uses `force-interactive`: strict discovery, no engine silent acquisition, one
supported WAM sign-in/account-selection operation and strict final validation.
Ordinary provider-managed session/protected-cache updates are permitted and
intentionally retained; cancellation does not promise to reverse them. No
account/cache repair, administrator consent or authenticated resource operation
is part of these cases.

Only the existing Windows-only result validator now launches with
`--fx-version 10.0.13`, under the
[accepted runtime correction](https://github.com/hcoona/microsoft-authentication-cli/pull/458).
Current public host/runtime correspondence, six public input checks and two
fixed public validation cases passed independent acceptance before real use:
valid Cancelled was accepted and duplicate outcome was rejected. This is not a
product runtime change, installation or validation of the earlier failed EOF
whose validator exited 150. That earlier case remains failed and spent.

| Actual case and public acceptance | Observed result and bounded conclusion |
| --- | --- |
| [Independent Work caller death, October 13](https://github.com/hcoona/microsoft-authentication-cli/issues/108#issuecomment-6135958075), under PR #449 at `cef5e13b05eb994468f3098e0b129459bba5133e` | Without the optional lifetime pipe, caller-only SIGTERM ended Linux caller with exit 143. The same independently retained Windows process handle observed native exit 1; the known relay was absent in the single guarded read at launch plus 2,674.661449 ms, within the original bound. This establishes finite scoped Windows completion after caller death, not immediate Windows signal delivery, a typed result, token absence or UI closure. |
| [Work EOF during actual WAM, October 14](https://github.com/hcoona/microsoft-authentication-cli/issues/108#issuecomment-6160780098) | After separate fresh readiness, the operator confirmed actual WAM before the matched held-pidfd SIGUSR1 at caller plus 40,002 ms. The sole writer closed while the product was live at 40,047 ms. Product exit 1, complete streams, validator exit 0 and valid Cancelled/None passed, with normal scoped completion and no forced termination. The operator made no login/cancel/close action before EOF and later closed remaining WAM normally. This later cleanup is distinct from human-cancellation acceptance. |
| [Work human WAM close, October 14](https://github.com/hcoona/microsoft-authentication-cli/issues/108#issuecomment-6161244691) | After separate fresh readiness, the operator saw Win32 and WAM, closed WAM with its X, then observed WAM followed by Win32 disappear. Product exit 1, complete streams, validator exit 0 and valid Cancelled/None passed, with normal scoped completion. The sole writer stayed open until completion; no signal, live EOF, caller death or forced termination substituted for the human action. This establishes actual WAM cancellation, not consent denial or the owned parent's Cancel/caption/Escape behavior. |

For both October 14 calls, Windows-local validation established
`Passed=true`, `ProtocolValid=true` and `TypedCancellationChecksPassed=true`.
Token-bearing stdout used only the admitted bounded confidential caller memory
and ordinary pipes, then the existing Windows-only validator. Retained evidence
contains fixed conclusions, process facts and allowlisted local telemetry, not
raw results, selectors, identifier hashes, tokens or provider diagnostics.
The frozen caller's `ExpectedCasePassed=false` and `UiEvidence=Unavailable`
remain unchanged: actual scenario/UI judgment is supplied by the separately
accepted result checks and operator observations. Telemetry alone proves neither
visible WAM nor process completion. Operator observations do not attribute an
HWND or establish broker-global termination. EOF did not close external WAM;
normal owned-process completion is not a promise to terminate shared broker UI.

Independent actual/scoped reviews accepted caller death and EOF (reviewer A)
and human close (reviewer B), with no material findings. Accounting1072 closes
the two-call batch and marks the one forwarded human reservation consumed.
There is no new debit/refund or rewriting of its historical submission state.
Consumption remains technical `71/242/22/825`, real `70/70/65/27`, metadata 138
and collection 546; real capacity remaining is `26/26/31/25`. All earlier failed
charges, historical lifetime uncertainties and global `noExperimentLive=false`
remain retained. No authentication is live and no readiness is retained for
another call.

**Acceptance limits:** These observations complete the three named WSL
lifetime/action cases for their admitted candidates. They do not themselves
close the whole Slice. The validation strategy's
[Slice ledger](../../validation/strategy.md#windows-slice-requirement-to-evidence-ledger)
owns the mapping from scenarios to these observations.
The selected visible-parent implementation has structural owned-HWND/thread,
branding, cancellation and teardown evidence from its existing controlled
scenarios, but physical accessibility/DPI/focus and notice suitability remain
unobserved. Attended locked/expired-state no-UI behavior, real broker
unavailability/disappearance, concurrency and deployment without undeclared
development-machine dependencies retain their respective claim-specific gates.
The visible-parent calls do not validate hidden-parent notice/title/focus or
lifetimes; hidden-parent adoption is not a prerequisite for accepting this batch.
Profile activation/distribution, downstream service operations, installation,
release and general support remain separate. This evidence update admits no
new experiment or observation mechanism.

## Recheck Evaluations

### Slice Design Recheck Assessment (2026-09-11)

All seven registry entries were evaluated on **2026-09-11 UTC** for the concrete host,
account, interaction, state, and candidate-Profile design. Public Issues/pages were
retrieved; issue descriptions are reports or proposals, not observed platform facts.

| Entry | Current outcome and design impact |
| --- | --- |
| RECHECK-001 | Issue 464 remains open with no comments or documented upstream no-interaction contract. The selected design retains distinct discovery/silent APIs and explicit interaction permission. |
| RECHECK-002 | Issue 465 remains open with no comments or new strict-selection/result guarantee. Preserve whole-email resolution and authoritative final validation. |
| RECHECK-003 | Issue 460 remains an open Windows-helper proposal with no comments. Current WSL documentation describes a Linux broker/keyring setup, not transparent Windows CLI cancellation. Keep explicit direct Windows invocation and specify its lifetime boundary. |
| RECHECK-004 | Issues 459 and 461 remain open; their two-comment discussions retain the Azure CLI workaround rather than a changed native browser/callback contract. Browser/device-code support is not selected. |
| RECHECK-005 | PR 462 remains open and unmerged. Its proposed Linux broker integration and current Linux/WSL prerequisites do not change the selected Windows process design. No Linux broker support claim follows. |
| RECHECK-006 | Issue 398 remains open with no comments or new secure-store behavior. The Windows Slice uses WAM-owned state; Linux libsecret limitations do not justify plaintext fallback or an app-owned repair mechanism. |
| RECHECK-007 | Current Azure DevOps guidance still says Entra apps do not natively support MSA users for this resource. The already recorded legacy-client/passthrough observation remains bounded positive evidence; candidate definition neither enables that Profile nor generalizes to arbitrary registrations or feeds. |

These outcomes preserve the existing source/observation distinctions. The runtime release
and source commits above are immutable design baselines, not promises that they remain the
latest versions. Upgrade and release review must refresh the applicable dependency facts.

### Registry Evaluation for the Review Remediation (2026-10-17)

All ten registry entries were evaluated on **2026-10-17 UTC**. Two events require it:
the merged Delivery Wave change that authorized the
[review remediation](https://github.com/hcoona/microsoft-authentication-cli/issues/509),
and the repository owner's 2026-10-16 decision to revert forced interaction to supply
neither a discovered account nor a login hint. That decision is an `interaction-policy`
decision, so it fired RECHECK-001 and RECHECK-010; no evaluation was recorded when it was
made, and this entry records it. The remediation also restates V2-REQ-014, V2-REQ-016 and
V2-REQ-020 as behavior only and amends V2-REQ-042.

GitHub Issue and pull-request state was retrieved anonymously from the public REST API.
Microsoft Learn state was retrieved from the public page metadata (`updated_at` and
source `git_commit_id`) and the page text. Issue descriptions remain reports or
proposals, not observed platform facts.

| Entry | Retrieved public state | Outcome |
| --- | --- | --- |
| RECHECK-001 | [Issue #464](https://github.com/AzureAD/microsoft-authentication-cli/issues/464): open, zero comments, updated `2026-08-17T21:12:04Z`. | Fired. Upstream still documents no no-interaction contract. The revert and the behavior-only requirement text do not rely on an upstream feature; explicit interaction permission and the no-interaction guarantee are unchanged. |
| RECHECK-002 | [Issue #465](https://github.com/AzureAD/microsoft-authentication-cli/issues/465): open, zero comments, updated `2026-08-27T22:02:13Z`. | Not fired. Strict account discovery and final validation are unchanged; a forced call without a hint still fails on any other returned account. |
| RECHECK-003 | [Issue #460](https://github.com/AzureAD/microsoft-authentication-cli/issues/460): open, zero comments, updated `2026-05-13T17:25:51Z`. [MSAL.NET WSL guidance](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/linux-dotnet-sdk-wsl): updated `2026-03-31T12:00:00Z`, source commit `5311d7a`; it still describes a Linux broker with an unlocked keyring and WSL 2.4.13 or later. | Not fired. Direct Windows invocation is unchanged; no Linux broker or forwarding layer is selected. |
| RECHECK-004 | Issues [#459](https://github.com/AzureAD/microsoft-authentication-cli/issues/459) and [#461](https://github.com/AzureAD/microsoft-authentication-cli/issues/461): open, two comments each, updated `2026-05-14T00:13:01Z` and `2026-05-14T00:12:30Z`. | Not fired. No browser or callback mechanism is selected. |
| RECHECK-005 | [PR #462](https://github.com/AzureAD/microsoft-authentication-cli/pull/462): closed and merged `2026-09-17T19:33:15Z`, last updated `2026-09-17T20:35:25Z`, re-retrieved on 2026-10-17; unchanged since the [bounded WSL transport recheck](#bounded-wsl-transport-recheck). The shared MSAL.NET WSL guidance is in the RECHECK-003 row. | Not fired. Its Linux broker path is not selected. |
| RECHECK-006 | [Issue #398](https://github.com/AzureAD/microsoft-authentication-cli/issues/398): open, zero comments, updated `2024-08-13T16:18:59Z`. | Not fired. WAM-owned state and the persistence warning are unchanged. |
| RECHECK-007 | [Azure DevOps Entra OAuth guidance](https://learn.microsoft.com/en-us/azure/devops/integrate/get-started/authentication/entra-oauth): updated `2026-05-08T17:05:00Z`, source commit `f7bd73f`, the revision recorded on 2026-09-04; it still states that Entra apps do not natively support MSA users for Azure DevOps. | Not fired. The V2-REQ-042 amendment permits an integration-specific identity pin that validates a Profile-supplied client ID; it selects, enables and distributes no Profile, and the external Profile gate is unchanged. |
| RECHECK-008 | [Native AOT guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/): updated `2026-07-27T16:18:00Z`, source commit `45d2254`; it still lists Windows x64 and Visual Studio 2022 or later with Desktop development with C++. [Trimming incompatibilities](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities): updated `2025-12-03T09:19:00Z`, source commit `020cd4a`; Windows Forms and WPF trimming remain unsupported. MSAL issues [5226](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/5226) and [5248](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/5248): closed as completed, four comments each, updated `2025-08-11T22:15:10Z` and `2025-09-11T21:47:37Z`. | Not fired. The publishing mode, target and dependency pins are unchanged, and no exception is accepted or renewed. |
| RECHECK-009 | [Entra error-code reference](https://learn.microsoft.com/en-us/entra/identity-platform/reference-error-codes): updated `2026-06-15T17:40:00Z`, source commit `a4be4ac`; it still lists AADSTS65004 as declined consent and warns that error codes can change. | Not fired. The provider-failure mapping is unchanged. |
| RECHECK-010 | [WAM API reference](https://learn.microsoft.com/en-us/entra/identity-platform/reference-entra-id-wam-api): updated `2026-06-15T17:40:00Z`, source commit `a4be4ac`; it still documents the `prompt` request property (`no_select` or `select_account`) and `LoginHint`. The pinned MSAL and Broker 4.83.1 and NativeInterop 0.20.3 are unchanged. | Fired. The documented prompt values are a request contract; the [Windows design](../../designs/windows-ado-authentication.md#forced-interactive-authentication) records that the pinned transport does not carry `Prompt`, so forced interaction controls only whether an account or hint is supplied. The revert rests on the [real-host observations](real-host-validation.md#results), not on prompt metadata. Whether WAM presents UI remains provider behavior recorded as a design known limitation, and an interactive result is not treated as renewed credentials. |

No entry changes a current conclusion. The remediation changes no dependency, SDK,
runtime, Profile, platform or release, and authorizes no experiment.

### Registry Evaluation for the Toolchain Alignment (2026-10-18)

The [toolchain alignment](https://github.com/hcoona/microsoft-authentication-cli/pull/520)
moved the runtime pin from 10.0.12 to the 10.0.13 runtime bundled with SDK 10.0.402. Its
pull request recorded a desk-only RECHECK-008 evaluation, using sources retrieved on
2026-10-17. This record makes the evaluation of all ten registry entries on
**2026-10-18 UTC**. Only RECHECK-008 needed an assessment, and only on the desk. Its publishing
mode, target, SDK, MSAL, Broker and NativeInterop pins are unchanged; only the runtime
patch and its ILCompiler and ILLink packs moved.

The [.NET 10.0.13 release notes](https://github.com/dotnet/core/blob/50e60dcd5ba76509fe472278ba9fe299a411efa6/release-notes/10.0/10.0.13/10.0.13.md)
(dated 2026-10-13) list SDK 10.0.402 and the 10.0.13 ILCompiler and ILLink packages, with
no NativeAOT- or ILCompiler-specific breaking change or known issue. The
[.NET 10 known issues](https://github.com/dotnet/core/blob/50e60dcd5ba76509fe472278ba9fe299a411efa6/release-notes/10.0/known-issues.md)
list no NativeAOT or ILCompiler issue for 10.0.13. The Native AOT disposition stands.
Existing Native AOT and real-host evidence stays bound to the configuration its records
name and is not evidence for 10.0.13. The other nine entries did not fire: the change
makes no interaction, account, WSL, browser, broker, cache, Profile or failure-mapping
decision and is not a release.

### Registry Evaluation for the SDK 10.0.403 Move (2026-10-29)

The Delivery Wave entry "Windows Portable Package, Local Build and Test" (item 6) moves
the pinned SDK from 10.0.402 to 10.0.403 and the runtime from 10.0.13 to its bundled
10.0.14, with the ILCompiler and ILLink packs. This record evaluates all ten registry
entries on **2026-10-29 UTC**. Only RECHECK-008 needed an assessment, and only on the
desk. The publishing mode, target, MSAL, Broker and NativeInterop pins are unchanged.

The [.NET 10.0.14 release notes](https://github.com/dotnet/core/blob/60871330fce2b3da383461be027ebdf3ce30f8fb/release-notes/10.0/10.0.14/10.0.14.md)
(an out-of-band security release dated 2026-10-25) list SDK 10.0.403 and the 10.0.14
ILCompiler and ILLink packages; their notable changes concern the Brotli decoder and
source-built Linux tarball ownership, with no NativeAOT- or ILCompiler-specific change.
The [.NET 10 known issues](https://github.com/dotnet/core/blob/3860c130e11c7feb24511e6ac3dd1694b25efda1/release-notes/10.0/known-issues.md)
list no NativeAOT or ILCompiler issue for 10.0.14. The Native AOT disposition stands.
Existing Native AOT and real-host evidence stays bound to the configuration its records
name and is not evidence for 10.0.13 or 10.0.14. The other nine entries did not fire for
this change: it makes no interaction, account, WSL, browser, broker, cache, Profile or
failure-mapping decision and is not a release.

### Registry Evaluation for the Bundled Profile (2026-10-29)

The [portable package](../../designs/windows-ado-authentication.md#portable-package-and-version)
bundles the Visual Studio legacy compatibility Profile as an explicit opt-in file under
the repository owner's prerelease exception in decision
[0003](../../decisions/0003-treat-client-registration-as-an-external-dependency.md).
That fires RECHECK-007's `client-profile` trigger. This record evaluates it on
**2026-10-29 UTC**.

The [Azure DevOps Entra OAuth guidance](https://learn.microsoft.com/en-us/azure/devops/integrate/get-started/authentication/entra-oauth)
was last updated `2026-05-08T17:05:00Z` (source commit `f7bd73f`), the revision the
[Slice evaluations above](#recheck-evaluations) already recorded. It still says an
ordinary Microsoft Entra application cannot natively support Microsoft-account users for
Azure DevOps. The bounded account-type evidence for this registration stays the
[Windows probe](windows-msal-account-metadata.md#observed-msa-token-git-discovery-and-silent-reuse)
and the [real-host results](real-host-validation.md#results), both in existing account
state. No conclusion changes: the bundled file uses the existing candidate unchanged,
nothing selects it by default, and the external Client Profile gate stays open. The
owner's exception is a risk decision, not new evidence. The other nine entries did not
fire for this change.
