# Real-Host Validation Evidence

This is a completed experiment record for
[Issue #482](https://github.com/hcoona/microsoft-authentication-cli/issues/482), retained as
evidence cited by current records. It authorizes no execution. It ran under the Delivery
Wave entry "Real-Host Validation and Threat-Model Re-Review (2026-10-15)", whose text
remains in
[`docs/delivery-wave.md` at commit `4c4b023`](https://github.com/hcoona/microsoft-authentication-cli/blob/4c4b023a3574a7d07f6bc2d24c2e06d0ee531f14/docs/delivery-wave.md);
[PR #507](https://github.com/hcoona/microsoft-authentication-cli/pull/507) removed that
entry on 2026-10-17. The accepted protocol and its amendments, step commands, launcher
rules, per-subject build records, file hashes, verbatim ledger rows and execution
history remain at the
[pinned revision](https://github.com/hcoona/microsoft-authentication-cli/blob/3a3ad6be41090b2b8f8fc34f12a6d21e8ea3c610/docs/research/experiments/real-host-validation.md).
The [experiment policy](../experiment-safety.md) governs any future experiment; another
run requires a new accepted protocol and authorization.

The [validation strategy](../../validation/strategy.md#windows-slice-requirement-to-evidence-ledger)
maps scenarios to this evidence, the
[Windows design](../../designs/windows-ado-authentication.md) owns behavior and known
limitations, and the [threat model](../../security/threat-model.md#candidate-dispositions)
owns candidate dispositions. This record keeps the observation method, the sanitized
results, their bounded conclusions and limitations, and the deviation dispositions.

## Method

**Protocol.** The protocol was accepted by its merge into `main-v2` at `83a68a4`
([PR #485](https://github.com/hcoona/microsoft-authentication-cli/pull/485)). Reviewed
amendments rebound execution to each corrected subject and shared one call ledger and
one set of Wave limits. Each subject was materialized with `git archive` into a fresh
directory, restored in locked mode, built, published as Windows x64 Native AOT, and run
through the Windows scenario project before any real call.

| Subject | Role in the experiment | Calls | Protocol revision bound |
| --- | --- | --- | --- |
| `c9ac862` | First subject; stopped at the scenario run | #1, #2 (artifact checks only) | `c9ac862` |
| `8d9be27` | Second subject; full case plan | #3 to #24 | `8d9be27` |
| `0eb257a` | Third subject; forced call with the requested email as login hint ([PR #492](https://github.com/hcoona/microsoft-authentication-cli/pull/492)) | #25 to #29 | `0eb257a` (#25 to #27), `268ea4b` (#28, #29) |
| `389f85a` | Experiment-only variant binding the discovered account, never merged into `main-v2` | #30 to #34 | `97a8d76` |
| `3ae2554` | Fourth subject; forced call reverted to neither account nor hint ([PR #501](https://github.com/hcoona/microsoft-authentication-cli/pull/501)) | #35 to #40 | `3ae2554` |

**Steps.** B1 restore, B2 build, B3 Native AOT publish, B4 result-validator build; native
artifact checks (`help` and `authenticate --protocol 0` from an empty directory with
`PATH=C:\Windows\System32`, charged as N units); S1, the unfiltered
`tests/Authentication.Windows.Scenarios` run including `WindowsHostTiming` (S1-1), a
two-test filtered run against the published executable (S1-2) and the native Profile
cases (S1-3). Each S1 run used its own new empty `TEMP`. The only probe change was the
scenario host's runtimeconfig framework version, 10.0.12 to 10.0.13, because only runtime
10.0.13 was installed. No outer Job object was used.

**Cases.** Every call used `authenticate --protocol 1` with the Profile, the role's
email, the Azure DevOps default scope, `--tenant common` and `--telemetry stderr`; WSL
calls also passed `--cancel-on-stdin-close` with a launcher-held lifetime pipe. Units:
I is interactive-capable, N is `non-interactive-only`.

| Case | Interaction and action | Expected outcome/route, exit |
| --- | --- | --- |
| P7 | Native first call, `non-interactive-only` | `InteractionRequired/None`, 1 |
| P1 | `interactive-if-needed` | `Success`, 0 |
| P2 | Fresh process after P1, `non-interactive-only` | `Success/Silent`, 0 |
| P3 | `force-interactive`; owner chooses the designated account | `Success/Interactive`, 0 |
| P4 | `force-interactive`; owner cancels on the WAM surface | `Cancelled/None`, 1 |
| P5 | WSL `force-interactive`; launcher closes the lifetime writer after WAM is shown | `Cancelled/None`, 1 |
| P6 | `force-interactive --timeout-seconds 30`; owner does not act | `Timeout/None`, 1 |

**Evidence channels.** A call passed only with a Windows-local result-validator pass,
matching product exit and, for success, valid metadata and `persistence_unconfirmed`.
Token-bearing stdout stayed in bounded caller memory and pipes and was validated only on
Windows. The allowlist admitted the validator projection, exits, wall durations, fixed
telemetry stage, status, outcome, `error.type` and mechanism-indication values, whether
the pre-interaction notice appeared, and owner yes/no answers. After the F1
confirmation amendment the owner answered Q1 (did WAM present its interactive UI?),
Q2 (did the designated account complete sign-in?), Q3 (personal role: did the second
provider page of F1 appear?), Q4 (did a consent prompt or unexpected window appear?) and
Q5 (did the provider default to a credential method the operator had to cancel?), with a
fixed credential-method vocabulary after a Q5 yes.

## Experiment Record Fields

- **Environment.** Windows 11 Enterprise x64 build 26300 (10.0.26300.9711) in an active
  Remote Desktop session of the designated user; WSL 3.0.3.0 with Ubuntu 26.04 under
  WSL 2. Native calls were initiated from PowerShell 7.6.7 and WSL calls from bash; both
  ran the same published executable, and account state, WAM and UI belonged to the
  Windows session in every case.
- **Versions.** SDK 10.0.402 with runtime 10.0.13, MSVC 14.51.36231, Windows SDK
  10.0.26100.0; MSAL and Broker 4.83.1, NativeInterop 0.20.3, as locked by the subject.
- **Restore.** B1 used the owner's machine-local restore exemption
  ([PR #491](https://github.com/hcoona/microsoft-authentication-cli/pull/491#issuecomment-6208285632),
  [PR #494](https://github.com/hcoona/microsoft-authentication-cli/pull/494#issuecomment-6222226699)),
  not the protocol's nuget.org-only rule. That exemption was a
  [deviation](#deviations-and-dispositions) outside the Wave's authorization. Lock
  closure was verified for 26 of 26 lock-file packages; three implicit 10.0.12 runtime
  packs were not lock-verified. This is not a public-restore result and bounds the
  provenance of every published artifact.
- **Account state.** Existing personal (Microsoft account) and work (organizational)
  roles with unknown prior broker state; each later subject carried the earlier
  subjects' ordinary updates. Selectors stayed in a local file and were never recorded.
  This is not clean first use.
- **Client and request.** The `visual-studio-legacy-wam` example Profile (SHA-256
  `b7b26fb3bcedeca62087dc3818e858dd9184ca37dbc9eb0abcbd310a839a2c75`), Public Cloud,
  `common` for both roles (the `organizations` authority with MSA passthrough for this
  Profile), the Azure DevOps default scope, and the interaction policy of each case.
- **Files and state access.** Profile, launchers and ledger were in a dedicated local
  directory; the product has no application-owned cache and reached WAM state only
  through supported APIs. Ordinary WAM session and cache updates were permitted and
  retained.
- **Telemetry, network and sensitive output.** Toolchain telemetry opt-out variables were
  set. Product telemetry went only to stderr and was filtered to the allowlist. No token,
  email, tenant, provider text or screenshot entered the committed record. Network
  effects were token acquisition against Microsoft Entra and the exempted restore.
- **Published artifacts.** Every subject's `Authentication.Cli.exe` imported only
  Windows DLLs and API sets, and `msalruntime.dll` (SHA-256
  `9DF30B54B7AF974A072B1D55FEE3590A5562C77EBC46F47016F0DD5199CD0C79`) was unchanged
  across subjects. The pinned revision records every file's size and hash.

## Results

All results are runtime observations on this host and existing account state unless
labeled otherwise. "Personal" and "work" name roles; "native" and "WSL" name callers.

**Scenario runs.** S1-1 passed 183 of 183 on `c9ac862` and `8d9be27`, 185 of 185 on
`0eb257a`, and 186 of 186 on `389f85a` and `3ae2554`, each with no failure, skip,
surviving process or latched safety stop. S1-2 passed exactly two tests and S1-3 exited 0
on every subject except `c9ac862` (see [Deviations](#deviations-and-dispositions)).

**Artifact checks and native loading.** `help` exited 0 with the unofficial first line
and `authenticate --protocol 0` exited 1 with `invalid_request` on every subject, except
that #25's first-line check was affected by a checker defect. Every real call reached a
typed provider outcome without a `dll_search` or `broker_initialization` indication, so
`msalruntime.dll` loaded from the application directory with its System32 imports on
this host.

**Second subject `8d9be27` (#5 to #24).**

- **P7 not reached** for both roles (#5, #17): existing state already permitted silent
  use; not repeated and no state changed to reach it.
- **Silent success and fresh-process reuse** for both roles from both callers (P1 and P2:
  #6, #7, #11, #12, #18, #19, #22, #23), all `Success/Silent`; no window appeared where
  the owner was asked (#6, #11, #18, #22).
- **Forced interaction** (P3: #8, #13, #20, #24): `Success/Interactive`, WAM shown,
  designated account chosen, no consent prompt, strict validation passed.
- **Human WAM cancellation** (P4: #9, #14, #21): `Cancelled/None`, no leftover window.
- **Lifetime-pipe EOF while WAM was shown** (P5: #15, personal WSL): the product ended
  `Cancelled/None` about one second after the writer closed.
- **Deadline while WAM was shown** (P6: #10, #16, personal native and WSL):
  `Timeout/None` at about 30 seconds, within the 40-second bound.
- **Finding F1.** For the personal role (#8 native, #13 WSL), WAM showed a second
  provider page that did not offer the personal account for direct selection, and the
  operator entered the account manually; the work role (#20, #24) did not reach it. Cause
  not established; a relation to the forced call passing neither account nor login hint
  into the `organizations` MSA-passthrough flow is an **inference**.
- **Finding F2.** A WAM window remained after the product ended in all three non-human
  terminations: product timeout (#10, #16) and lifetime-pipe EOF (#15). The operator
  closed it. Human cancellation left no window.
- **Broker title and notice.** The owner answered yes to the configured broker title
  appearing on the WAM account picker. The pre-interaction stderr notice appeared in
  every forced call and in no silent-route call.
- **Telemetry.** Only fixed stage events; no `error.type` or mechanism indication.

**Third subject `0eb257a`, login hint (#27 to #29).**

- #27 personal native and #28 personal WSL were accepted: Q1 and Q2 yes, Q3 no (F1 did
  not appear), Q4 no. For #28, Q5 was yes: the provider defaulted to `security key`
  and the operator chose `Windows Hello`; #27 has the owner's equivalent categorical
  description (candidate finding F3, an operator observation; cause not established).
- #29 work native was **not accepted**: Q1 no. The product returned
  `Success/Interactive` with a passing validator after a 1,551 ms
  `interactive_acquisition` stage, so WAM completed the forced call without presenting
  its interactive UI. All real calls stopped. That the hint caused the skip is an
  **inference**.

**Variant `389f85a`, discovered account bound (#32 to #34; Wave outcome (3) evidence
only, not `main-v2` validation).** Personal native, personal WSL and work native each
returned `Success/Interactive` with a passing validator, and for each Q1 was no and Q4
was no; the `interactive_acquisition` stage lasted 1,229, 233 and 1,062 ms. The variant
did not meet the owner's first criterion in any observed combination; the F1 and F3
criteria could not be observed. Why the broker completed without UI is not established.

**Fourth subject `3ae2554`, reverted (#37 to #40).** Personal native (#37), work native
(#38), personal WSL (#39) and work WSL (#40) were all accepted: Q1 and Q2 yes, Q4 no,
Q5 no, with passing validators. F1 appeared for the personal role (#37, #39). The #29
defect did not recur in the work native combination. No F3 conclusion is drawn.

**CORE-01 (inference).** Every validated success passed the coordinator's fail-closed
check that the provider-returned correlation ID equals the ID it sent, so the MSAL
result returned the sent ID. The pinned MSAL source builds that value from its own
request context, so broker-level echo is not established. No ID was recorded.

**Not observed.** Work-role WSL WAM cancellation and lifetime-pipe EOF, the work-role
deadline (native and WSL), work-role WSL on the third subject and the variant, fresh-state
first use, consent denial, an explicit work-tenant GUID, deadline expiry before WAM,
concurrent calls and any other host.

## Bounded Conclusions

- On this host and existing state, the published Native AOT product loaded its broker
  under application-directory/System32 search and completed silent, reused, forced,
  cancelled, EOF and deadline requests with the expected typed outcomes for the observed
  role and caller combinations.
- Forced interaction supplying neither account nor login hint presented WAM's interactive
  UI in every observed combination on two subjects; supplying the login hint (one of
  three combinations) or binding the account (all three) let WAM complete a forced call
  without UI. These observations informed the repository owner's 2026-10-16 decision to
  revert, quoted in [PR #501](https://github.com/hcoona/microsoft-authentication-cli/pull/501); they are not a guarantee that WAM presents UI on other hosts or states.
- F1 and F2 are provider behaviors observed for the stated combinations; their causes
  are not established.
- Existing-state success does not establish clean first use, another host, deployment
  without the development tools installed here, or Profile, platform or release support.

## Deviations and Dispositions

- **Restore exemption.** The Wave entry permitted only public package restore, but B1
  used a machine-local, credential-bearing restore configuration under owner
  dispositions given in pull-request comments. A comment cannot grant work authorization
  ([experiment policy](../experiment-safety.md#source-and-build)), so every B1 restore
  ran outside the Wave's authorization. The owner's dispositions in those pull requests
  admit the resulting artifacts as evidence, bounded as stated under Restore above.
- **First subject stop.** S1-2 ran no tests because a local wrapper split the filter
  argument (operator error), and S1-3 failed on an outdated test assertion, corrected by
  [PR #489](https://github.com/hcoona/microsoft-authentication-cli/pull/489). No real call
  ran and no first-subject artifact is relied on.
- **Readiness timing.** For #13, #18 and #20 the fresh readiness answer was given before
  the immediately preceding N call; each I call started 3 to 8 seconds later. No effect
  on outcomes was observed.
- **Provider text in traces.** Free-form operator descriptions of provider surfaces,
  including provider UI wording, were relayed into agent traces after #8 and quoted into
  the local ledger after #13. The stop condition fired and was not honored; calls #9 to
  #24 ran past it. The text held no token or identifier, was removed from the ledger and
  never committed. The owner's
  [disposition](https://github.com/hcoona/microsoft-authentication-cli/pull/491#issuecomment-6208392178)
  accepts #9 to #24 as evidence.
- **Cases not executed.** Work-role P6 (native and WSL) and work-role WSL P4 and P5 were
  skipped by [owner decision](https://github.com/hcoona/microsoft-authentication-cli/pull/491#issuecomment-6208285632)
  with no capacity charged; the protocol had no skip route. That they add no
  role-dependent behavior is an **inference**.
- **Third subject.** A first B1 invocation failed in a local wrapper before any restore;
  B4 first ran with the wrong restore configuration and was repeated as specified. For
  #27 the executor asked Q4 before the protocol defined it, carrying it over from the
  earlier operator-observation practice; the F3 amendment then made Q4 part of the
  protocol. The
  owner described the credential-method steps after #27, so the executor stopped under
  the yes/no-only rule; the owner dispositioned that description as non-sensitive and
  the F3 amendment added Q4, Q5 and a fixed vocabulary. The ledger lacked a Profile
  hash, so calls stopped before #28 until the owner's
  [ruling](https://github.com/hcoona/microsoft-authentication-cli/pull/497#issuecomment-6224331392)
  set it to the recorded value. #25's first-line check used the wrong expected prefix;
  S1-2 separately asserts the help text against the same executable.
- **Retention.** The third subject's directory was retained after #29 at the coordinator's
  direction for comparison and deleted after the variant results reached the local ledger,
  an interpretation of the protocol's deletion trigger. The variant directory took the
  `-1` suffix as a conservative reading of the naming rule.

## Attempt History and Cleanup

The Wave limits were 24 I and 40 N, raised to 48 I and 64 N by the F3 amendment. Every
attempt was appended to the local ledger before it started; failed starts were counted;
no call overlapped, overran its bound, was repeated or left a product process.

| Subject | I used | N used |
| --- | --- | --- |
| `c9ac862` | 0 | 2 |
| `8d9be27` | 14 | 8 |
| `0eb257a` | 3 | 2 |
| `389f85a` | 3 | 2 |
| `3ae2554` | 4 | 2 |
| **Total** | **24** | **16** |

At the end of the Wave, 24 I and 48 N remained unused. No account, consent, cache or
remote state changed beyond ordinary token acquisition. Each subject's directory was
deleted after its hashes, scenario counts and ledger rows were recorded. On 2026-10-17
the local launchers, role file, Profile and ledger were deleted. On 2026-10-18 the
coordinator removed the experiment-owned machine-local NuGet configuration file from the
experiment directory under the owner's risk decision in the
[Delivery Wave](https://github.com/hcoona/microsoft-authentication-cli/blob/0f5716e1078c6c302d374676524db1b216b42633/docs/delivery-wave.md#w2-governance-record-authority-and-experiment-policy-2026-10-18).
The user-wide credential-provider token cache is pre-existing user state shared with
unrelated work; it was neither read nor removed. WAM sessions and caches are retained;
deleting local files reverses no provider-side change.
