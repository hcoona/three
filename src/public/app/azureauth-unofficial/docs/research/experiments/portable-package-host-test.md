# Portable Package Host Test Evidence

This is a completed experiment record for
[Issue #506](https://github.com/hcoona/microsoft-authentication-cli/issues/506), retained as
evidence cited by current records. It authorizes no execution. It ran under the Delivery
Wave entry "Windows Portable Package, Local Build and Test", whose text remains in
[`docs/delivery-wave.md` at commit `a7ece22`](https://github.com/hcoona/microsoft-authentication-cli/blob/a7ece22222480d4938a48a2ade803f3728ed97ff/docs/delivery-wave.md);
[PR #549](https://github.com/hcoona/microsoft-authentication-cli/pull/549) removed that
entry on 2026-10-29. The accepted protocol below is kept as the method of the recorded
results; the [experiment policy](../experiment-safety.md) governs any future experiment,
and another run requires a new accepted protocol and authorization.

The question is whether the archive that `packaging/windows/package.ps1` builds, once
extracted into a fresh directory, authenticates the existing personal and work roles on
the designated host from native Windows and WSL callers with the bundled Profile.

## Subject and Inputs

- **Source.** A full clone of this repository at a recorded `main-v2` commit whose copy of
  this file is unchanged, under a fresh root `C:\Temp\azureauth-pkgtest-YYYYMMDD[-n]`
  (`<root>` below), in `<root>\src`. A local `main-v2` branch is reset to that commit and
  checked out, so `HEAD` is on `refs/heads/main-v2`; the worktree is clean. Record the full
  commit hash in the ledger before the first build. No `PublicRelease` override is used.
- **Toolchain.** .NET SDK 10.0.403 with runtime 10.0.14, installed by `mise install --locked`
  on both sides and always called by its absolute install path with `DOTNET_ROOT` set to
  that directory: on Windows
  `%LOCALAPPDATA%\mise\installs\http-dotnet-development\10.0.403\dotnet.exe`, and in WSL
  `~/.local/share/mise/installs/http-dotnet-development/10.0.403/dotnet`. For B1 and B2 the
  Windows SDK directory is also first in `PATH`, because `package.ps1` and the validator
  build call `dotnet` by name; the host also has a system SDK 10.0.403 that would
  otherwise satisfy `global.json`. Before R1 and B1, record on each side the resolved
  `dotnet` (`Get-Command dotnet` on Windows), `dotnet --version` and the base path from
  `dotnet --info`; stop if any is not under that mise install directory. The protocol
  runs no `mise exec` or `mise trust` and writes no mise state. The other tools are the
  installed Visual Studio 18 C++ tools (MSVC 14.51.36231) for Native AOT and `dumpbin`,
  PowerShell 7 as the native caller, and bash in the host's WSL 2 distribution as the WSL
  caller. Record each version before R1.
- **Dependencies.** MSAL and Broker 4.83.1, NativeInterop 0.20.3, Nerdbank.GitVersioning
  3.10.94 and the rest of the committed `packages.lock.json` files.
- **Profile.** `profiles\visual-studio-legacy-wam.json` from the extracted archive, passed by
  its absolute Windows path. Every request uses Public Cloud, `--tenant common` and the
  Azure DevOps default scope `499b84ac-1321-427f-aa17-267ca6975798/.default`.

## Restore Route

The Windows host cannot complete a TLS connection to `api.nuget.org`, so the Wave's Windows
restore route applies. `NUGET_PACKAGES` and `NUGET_HTTP_CACHE_PATH` point to new empty
directories `<root>\packages` and `<root>\http-cache` on both sides.

- **R1, WSL fill.** In WSL, from `<root>\src` through its `/mnt/c` path at the same commit:
  `dotnet restore Windows.slnx --locked-mode --configfile nuget.config -p:EnableWindowsTargeting=true`
  with NuGet audit at its default, bounded at 900 s. Record the exit code and the
  warning and error counts. Any audit warning or error, lock mismatch or nonzero exit stops the experiment.
- **R2, Windows.** `package.ps1 -DisableNuGetAudit` restores from that folder alone, in locked
  mode. Audit is off for this step only.

The committed lock files verify every package they list. The implicit runtime packs
(`Microsoft.NETCore.App.Runtime.win-x64`, `Microsoft.NETCore.App.Host.win-x64`,
`Microsoft.NETCore.App.Runtime.NativeAOT.win-x64`) are not lock-verified; record this as an
evidence limit. This is not a public-restore claim.

## Build and Synthetic Steps

These steps are development validation and are not counted against the Wave limits.
They run sequentially with `DOTNET_CLI_TELEMETRY_OPTOUT=1` and `DOTNET_NOLOGO=1`.

| Step | Action | Bound |
| --- | --- | --- |
| B1 package | From `<root>\src`: `pwsh packaging\windows\package.ps1 -DisableNuGetAudit -OutputDirectory <root>\out`, without `-AllowNonPublicRelease` | 1,800 s |
| B2 validator | `dotnet build tools\result-validation\cli -c Release -o <root>\validator` | 300 s |
| B3 extract | Verify both `.sha256` files; record both hashes and the archive's file list; `Expand-Archive` the package zip into the new empty `<root>\app` | 60 s |
| B4 artifact checks | See below | 60 s |

B1 runs every packaging check, including the public-release check; a failure stops the
experiment. B1's `--version` check is the first run of the executable's bytes: record
whether Windows blocked or warned on it, including a block that makes B1 fail. After B1 and B2, stop the SDK build servers and list processes whose image is
under `<root>`; any survivor other than a stopped build server stops the experiment.

**B4 artifact checks.** From a new empty directory with `PATH` set to System32 only, run
`<root>\app\azureauth-unofficial.exe --version` (expect exit 0 and
`azureauth-unofficial <version>+<commit>` for the recorded commit) and `--help` (expect exit 0
and the unofficial first line). This is the first run from the extracted directory;
record whether Windows blocked or warned on it. Record the current Smart App Control mode
with `citool.exe -lp`, read-only, as Microsoft's
[Smart App Control test guide](https://learn.microsoft.com/windows/apps/develop/smart-app-control/test-your-app-with-smart-app-control#verify-smart-app-controls-current-mode)
describes: only the Smart App Control policy's friendly name and enforced flag. If
CiTool cannot run without elevation, record the registry value
`VerifiedAndReputablePolicyState` instead, labeled as the configured value rather than the
verified mode. The archive was built locally and never downloaded, so it has no Mark of
the Web; neither observation says anything about a downloaded archive.

## Environment and Account State

- **Host.** The owner-designated Windows 11 x64 host and its designated interactive user,
  with WSL 2 under that user as the second caller. Both callers run
  `<root>\app\azureauth-unofficial.exe`; account state, WAM and any UI belong to the Windows
  user session in every case.
- **Session.** The owner keeps an active, connected desktop session for every real call,
  and gives a fresh readiness answer immediately before each interactive-capable call,
  after all preparation for it is complete.
- **Roles.** `personal` (a personal Microsoft account) and `work` (an organizational
  account), both existing on the host with unknown prior broker state. Their emails exist
  only in `%LOCALAPPDATA%\azureauth-pkgtest\roles.json`.
- **Launchers.** Two local launchers in that directory, PowerShell 7 for native and bash
  for WSL, read `roles.json`, pass the values and the Windows-form Profile path, and
  print only the evidence allowlist. They are not committed, never echo arguments, and
  run with tracing disabled.
- **Effects.** Token acquisition for the two existing accounts; WAM sessions and caches
  receive ordinary updates and are retained. No account addition or removal, sign-out,
  consent change, cache clearing or resource request.

## Real Calls

Every call is `authenticate --protocol 1 --profile <profile> --account-email <role email>`
`--scope <ADO default> --tenant common --telemetry stderr` with the interaction below and the
default 120 s deadline. WSL calls also pass `--cancel-on-stdin-close` with a dedicated pipe
whose only writer the launcher holds until completion. Units follow the Wave: **I** is
interactive-capable, **N** is `non-interactive-only`. The external bound is 130 s; at the
bound the native launcher ends its own product process, and the WSL launcher closes the
lifetime writer. After every call the launcher confirms from Windows that no process with
image `<root>\app\azureauth-unofficial.exe` remains; calls never overlap.

| Case | Interaction and action | Expected (outcome/route, exit) | Unit |
| --- | --- | --- | --- |
| A | `non-interactive-only`, the first call for the role and caller | `Success/Silent`, 0 (silent success); or `InteractionRequired/None`, 1 (interaction required) | N |
| B | Only if A required interaction: `interactive-if-needed`; the owner completes any account choice, credential or MFA prompt | `Success`, Silent or Interactive, 0 | I |
| C | Fresh process: `non-interactive-only` | `Success/Silent`, 0 (fresh-process silent reuse) | N |
| D | `force-interactive`; the owner chooses the designated account | `Success/Interactive`, 0 | I |

**Order.** Personal native, personal WSL, work native, work WSL, each running A, B when
needed, C and D. A result of `Success/Silent` in A records the interaction-required case as
**not reached** for that role and caller; no state is changed to reach it. A consent or
permission prompt in any case is cancelled by the owner and recorded as an unexpected
outcome. The plan uses at most 8 N and 8 I of the Wave's 60 N and 40 I.

**Circuit breaker.** No automatic retry. A case may run again only after a recorded
diagnosis of its failure. Two consecutive unexpected outcomes of the same case stop that
case until an independent review of the sanitized evidence. Any unexpected effect, stop
condition or deviation stops all real calls, as the experiment policy requires.

## Validation and Evidence

The validator is `<root>\validator\ResultValidation.dll` from B2, started after the product
exits by the absolute path of the Windows SDK's `dotnet.exe` named under Toolchain, from both
callers; record its runtime version (10.0.14). Its arguments are the product exit code, the
receipt time in round-trip `O` format, the role email, `common`, `non-interactive-only` or
`interactive-if-needed` (for every interactive-capable case), the expected `Outcome` and
`Route` (`None` accepts any route) and the scope. For case A the launcher chooses the
expectation from the product exit code alone: exit 0 expects `Success Silent`, exit 1
expects `InteractionRequired None`. The launcher never parses stdout.

The caller holds product stdout only in bounded process memory (at most 1 MiB) and writes
it into the validator's stdin; it never reaches a file, terminal or log. A WSL success
returns the token to the trusted Linux caller as the owner risk decision accepts, and the
WSL launcher passes it to the Windows validator through interop in the same way. A call
passes only with validator exit 0, `Passed` and `ProtocolValid` true, a matching product
exit, and for a success also `MetadataValid` and `PersistenceUnconfirmed` true and
`PersistenceFailed` false, as the design's success contract requires.

**Evidence allowlist.** Only these are recorded: the validator projection and exit;
product exit; wall durations; from `--telemetry stderr` the `event`, `stage`, `status`,
`outcome`, `error.type` and `durationMilliseconds` values and any fixed
`Mechanism unavailable at <category>` indication; whether the pre-interaction notice line
appeared; and owner yes/no answers (WAM shown, account chosen, no unexpected window).
Trace IDs, token-shape fields, provider text, screenshots and crash dumps are excluded.
No crash dump is collected. A product crash is recorded by its exit code only and is a
stop condition. Any Windows Error Reporting report is operating-system state that the
experiment does not read, collect or delete.

**Ledger.** Before each counted call starts, append a row to
`%LOCALAPPDATA%\azureauth-pkgtest\ledger.md` with case, role class, caller, unit, start time
(UTC) and remaining I and N after charging the call; fill the outcome after the call. The
rows are copied unchanged into [Results](#results). If the ledger cannot be read or
reconciled, stop. Protocol amendments and corrected archives share these counts.

## Stop Conditions

Stop all real calls and preserve state when: a token, email or raw provider text would
enter captured output, a trace or a record; a prompt appears outside the case's
interaction or in another session; the owner cannot identify or close a prompt; a call
exceeds its external bound or its process cannot be confirmed ended; the subject commit,
archive hash, Profile or validator differs from the recorded values; R1 reports an audit
warning or error, or a restore or lock mismatch occurs; the product process crashes;
remaining capacity is unknown;
or any account, cache, installation or remote effect outside this protocol is observed.

## Retention and Cleanup

The package zip and the symbols zip, with their `.sha256` files, stay in `<root>\out` until
the results pull request merges, as the identity of the tested subject; they are never
uploaded or published. After that merge, stop the SDK build servers and delete `<root>`
(source, packages, HTTP cache, outputs, extraction directory and validator). Keep the
local launchers, `roles.json` and ledger until the Wave entry ends, then delete them.
WAM sessions and caches are retained; deleting local files reverses no provider-side
change.

## Corrected Archives

A defect fix follows Wave item 5. Before further real calls, a reviewed amendment to this
file names the new `main-v2` commit; the new subject repeats R1 and B1 to B4 in a new
`<root>` and continues the same ledger and limits. Earlier evidence stays attributed to its
own commit and archive hashes.

## Results

Executed on 2026-10-29 under this protocol as merged at
`6f7fadbafa9af6b9cd565cbee92ff89f07030294` ([PR #547](https://github.com/hcoona/microsoft-authentication-cli/pull/547)).
All results are runtime observations on this host and its existing account state unless
labeled otherwise. "Personal" and "work" name roles; "native" and "WSL" name callers.

### Context

- **Subject.** `main-v2` at `6f7fadbafa9af6b9cd565cbee92ff89f07030294`, which is this
  protocol's merge commit, cloned in full and checked out as a local `main-v2` branch
  (`HEAD` on `refs/heads/main-v2`, clean, not shallow) under
  `C:\Temp\azureauth-pkgtest-20261029`. Product version
  `0.1.0-alpha.18+6f7fadbafa`, a public-release build.
- **Host.** Windows 11 x64 build 10.0.26300.9711, the designated user's active Remote
  Desktop session; WSL 3.0.4.0 (kernel 6.18.54.1-1) with Ubuntu 26.04.1 LTS under WSL 2. Account
  state, WAM and UI belonged to the Windows session in every call.
- **Toolchain.** SDK 10.0.403 with runtime 10.0.14 from `mise install --locked` on both
  sides. Before R1 and B1 each side's resolved `dotnet` and its `dotnet --info` base path
  were under the mise install directory. Visual Studio 18.11.2 with MSVC 14.51.36231;
  PowerShell 7.6.9; bash 5.3.9. No `mise exec` or `mise trust` ran.
- **Dependencies.** MSAL and Broker 4.83.1, NativeInterop 0.20.3, Nerdbank.GitVersioning
  3.10.94, as locked by the subject.
- **Profile and request.** The archive's `profiles\visual-studio-legacy-wam.json`
  (Visual Studio legacy compatibility Profile, Public Cloud, multitenant), `--tenant common`,
  which for this Profile resolves to the `organizations` authority with MSA passthrough,
  scope `499b84ac-1321-427f-aa17-267ca6975798/.default`, `--telemetry stderr`; WSL calls
  also used `--cancel-on-stdin-close` with a launcher-held pipe.
- **Initial account state.** Existing personal (Microsoft account) and work
  (organizational) accounts with unknown prior broker state. The first personal call
  required interaction; every first work call was silent.
- **Application files and access.** The product's only file input was the Profile; it
  has no own cache or configuration. WAM sessions and caches received ordinary updates
  and are retained.
- **Telemetry and sensitive output.** Toolchain telemetry opt-out variables were set.
  Product stdout stayed in launcher memory and went only to the validator's stdin.
  Launchers printed only the evidence allowlist; emails stayed in the local
  `roles.json`. No token, email, tenant, provider text, screenshot or crash dump entered
  any output, trace or record.
- **Expected UI.** Case A and C: none. Case B: whatever WAM needs for the discovered
  account. Case D: WAM's interactive account selection.

### Restore and Build

- **R1.** 19:19:35 UTC, locked WSL restore into fresh folders with NuGet audit at its
  default: exit 0, 0 warnings, 0 errors, 1 min 36 s. The audit fetched the nuget.org
  vulnerability index. The three implicit runtime packs are not lock-verified.
- **B1.** 19:21:44 to 19:22:31 UTC, `package.ps1 -DisableNuGetAudit` without
  `-AllowNonPublicRelease`: exit 0. Public release true; `azureauth-unofficial.exe` imports
  17 and `msalruntime.dll` 50 System32 DLLs and API sets; the version resource and the
  staged `--version` matched. This `--version` was the first run of the executable's
  bytes; Windows did not block or warn.
- **B2.** Validator build exit 0, 0 warnings. After B1 and B2 the build servers were
  stopped and no process remained under the root.
- **B3.** Both `.sha256` files verified.

| Artifact | Bytes | SHA-256 |
| --- | --- | --- |
| `azureauth-unofficial-0.1.0-alpha.18-win-x64.zip` | 5,168,952 | `68c2dc524c576d0b442d06cc7f13d8ee423f28ae32509d678538cb329c2d6f95` |
| `azureauth-unofficial-0.1.0-alpha.18-win-x64-symbols.zip` | 7,973,908 | `2b0674224fec000d71e2d9f4fefd5225eaa62d8ee1d280eddf34c11e4f14e7b9` |
| `azureauth-unofficial.exe` (extracted) | 8,933,888 | `fc791de5bec10e0db2d1dfa1b879673cd9009b2b3f37920f78380d87e4a46d30` |
| `msalruntime.dll` (extracted) | 2,949,656 | `9df30b54b7af974a072b1d55fee3590a5562c77ebc46f47016f0dd5199cd0c79` |

  The archive held exactly the six allowlisted files; `msalruntime.dll` is the same
  file the earlier [real-host results](real-host-validation.md#method) recorded.
- **B4.** From an empty directory with `PATH` limited to System32, the extracted
  `--version` printed `azureauth-unofficial 0.1.0-alpha.18+6f7fadbafa` and `--help` printed
  the unofficial first lines, both exit 0. This first run from the extracted directory was
  not blocked or warned. The extracted executable had no `Zone.Identifier` stream.
  `citool.exe -lp` failed without elevation (`0x80070005`); the configured
  `VerifiedAndReputablePolicyState` value is 0 (Off), a configured value, not the verified
  mode.

### Calls

Units: I is interactive-capable, N is `non-interactive-only`. Every passing call had validator
exit 0 with `Passed` and `ProtocolValid` true; every success also had `MetadataValid` and
`PersistenceUnconfirmed` true and `PersistenceFailed` false. After every call whose launcher
completed (all but #1, #7 and #8) the process check found no product process; a check
after #8 and before #9 also found none. In those calls the pre-interaction notice line
appeared in every interactive call and in no other, and no `Mechanism unavailable`
indication appeared. Before each interactive call the owner gave a fresh readiness
answer in chat.

| # | Case | Role | Caller | Unit | Start (UTC) | Outcome and exits | I left | N left |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | A | personal | native | N | 2026-10-29T19:32:59Z | Not captured: launcher defect after the call (see Deviations) | 40 | 59 |
| 2 | A | personal | native | N | 2026-10-29T19:33:56Z | InteractionRequired/None, exit 1, pass | 40 | 58 |
| 3 | B | personal | native | I | 2026-10-29T19:39:04Z | Success/Interactive, exit 0, pass; interactive stage 21.2 s | 39 | 58 |
| 4 | C | personal | native | N | 2026-10-29T19:39:32Z | Success/Silent, exit 0, pass | 39 | 57 |
| 5 | D | personal | native | I | 2026-10-29T19:40:57Z | Timeout/None, exit 1, FAIL (see Deviations) | 38 | 57 |
| 6 | D | personal | native | I | 2026-10-29T19:44:40Z | Success/Interactive, exit 0, pass; interactive stage 24.2 s | 37 | 57 |
| 7 | A | personal | WSL | N | 2026-10-29T19:45:39Z | Not captured: launcher defect after the call | 37 | 56 |
| 8 | C | personal | WSL | N | 2026-10-29T19:45:41Z | Not captured: launcher defect after the call | 37 | 55 |
| 9 | A | personal | WSL | N | 2026-10-29T19:46:46Z | Success/Silent, exit 0, pass | 37 | 54 |
| 10 | C | personal | WSL | N | 2026-10-29T19:46:49Z | Success/Silent, exit 0, pass | 37 | 53 |
| 11 | D | personal | WSL | I | 2026-10-29T20:01:05Z | Success/Interactive, exit 0, pass; interactive stage 25.4 s | 36 | 53 |
| 12 | A | work | native | N | 2026-10-29T20:01:42Z | Success/Silent, exit 0, pass | 36 | 52 |
| 13 | C | work | native | N | 2026-10-29T20:01:53Z | Success/Silent, exit 0, pass | 36 | 51 |
| 14 | A | work | WSL | N | 2026-10-29T20:01:54Z | Success/Silent, exit 0, pass | 36 | 50 |
| 15 | C | work | WSL | N | 2026-10-29T20:01:56Z | Success/Silent, exit 0, pass | 36 | 49 |
| 16 | D | work | native | I | 2026-10-29T20:02:54Z | Success/Interactive, exit 0, pass; interactive stage 5.3 s | 35 | 49 |
| 17 | D | work | WSL | I | 2026-10-29T20:03:19Z | Success/Interactive, exit 0, pass; interactive stage 4.5 s | 34 | 49 |

**Operator observations.** #6, #11, #16, #17: WAM shown, requested account chosen, no
unexpected window. #3: no WAM account picker; a Windows Hello PIN prompt appeared with
the personal account preselected, and the owner entered the PIN. This description goes
beyond the protocol's yes/no answers; the
[repository owner admitted it](https://github.com/hcoona/microsoft-authentication-cli/pull/548#issuecomment-6507561844)
as an additional operator observation. No consent prompt appeared in any call.

**Coverage.** Silent success and fresh-process silent reuse passed for both roles and
callers. `non-interactive-only` with interaction required passed for personal native (#2);
it was **not reached** for personal WSL (state from #3 already permitted silent use) or
for either work caller (existing state), and no state was changed to reach it.
`force-interactive` success passed for both roles and callers.

### Deviations and Dispositions

Classified against this protocol and the experiment policy's
[Stop Conditions and Deviations](../experiment-safety.md#stop-conditions-and-deviations).
The repository owner's disposition is recorded in
[PR #548](https://github.com/hcoona/microsoft-authentication-cli/pull/548#issuecomment-6507561844).

- **#1, launcher failure (in-protocol failed attempt).** The native launcher failed
  after the product call, on a ledger write that its PowerShell host did not bind, so the
  outcome and the after-call process check were not captured. The call was charged
  before it started and was non-interactive, so no UI was possible. Product stdout had
  already been released. The author fixed the write (using `[IO.File]::WriteAllText`)
  and wrote the diagnosis into the local ledger row for #1 before starting #2. The
  outcome is excluded from the evidence.
- **#7 and #8, launcher failure (deviation).** The WSL launcher exited after each
  product call when an empty `grep` in the stderr projection ended the script under
  `set -e`; outcomes and after-call process checks were not captured. #8 started 2 s
  after #7 in the same shell loop, without a recorded diagnosis, and case C ran while
  case A's outcome was unknown, so the "B when needed" rule could not be applied. A
  process check before #9 found no product process. After a recorded diagnosis the
  `grep` was made to tolerate no match, a synthetic test confirmed the projection, and A
  and C ran once more (#9, #10). Their outcomes are excluded from the evidence.
- **#14 and #15 before #16 (deviation).** Work WSL cases A and C ran before work native
  case D, against the protocol's per-role-and-caller order. All were separate processes
  in existing state.
- **#5, timeout (stop condition: prompt not identified).** `force-interactive` ended at
  the 120 s deadline with a typed `Timeout`. The owner had not noticed any prompt and
  could not say whether WAM showed none or showed one behind other windows, so the
  protocol's "cannot identify a prompt" stop condition applied. No product process
  remained, and the owner later reported seeing no remaining WAM window. As a manual
  step outside the protocol, the owner minimized all other windows before the case ran
  once more (#6), which passed. Continuing with #6 to #17 after this stop condition, and
  the owner's manual minimizing of other windows before #6, are deviations. Inference: an unseen WAM surface behind other windows
  would explain #5, but whether WAM presented UI in #5 is undetermined. #5 does not bear
  on the Windows design's known-limitation sentence, which is scoped to the earlier
  real-host record.
- **Smart App Control mode (in protocol).** `citool.exe -lp` needed elevation, so the
  protocol's labeled registry fallback was used.

**Disposition.** The repository owner admitted calls #2 to #17 as evidence despite
these deviations and the #5 stop condition, and ended the experiment: no further real
call runs under this protocol without a merged amendment. No account, cache,
installation or remote effect outside the protocol was observed.

### Conclusions

- On this host and existing state, the locally built portable archive, extracted into
  a fresh directory, authenticated both roles from native and WSL callers with the
  bundled Visual Studio Profile in every case reached, with strict result validation and
  no product process left behind; one forced-interactive call timed out with its prompt
  unidentified. Inference: the Native AOT executable built with runtime 10.0.14 loaded
  the broker from its own directory, because every captured call reached a typed provider
  outcome without a `dll_search` or `broker_initialization` indication.
- This is existing-state evidence for one host. It does not establish clean first use,
  another host, a downloaded archive (Mark of the Web, SmartScreen or Smart App Control
  enforcement), deployment without development tools, or any support claim.

### Recheck Evaluations (2026-10-29)

- **RECHECK-007.** The Azure DevOps Entra OAuth guidance is unchanged (updated
  `2026-05-08T17:05:00Z`, `f7bd73f`). These calls add bounded existing-state evidence that
  the bundled Profile works for a personal and a work account; the external Client Profile
  gate stays open. No conclusion changes.
- **RECHECK-008.** The Native AOT guide (`45d2254`), trimming incompatibilities
  (`020cd4a`) and MSAL issues #5226 and #5248 (closed) are unchanged. The published
  application with the 10.0.14 runtime packs loaded its broker and completed every
  outcome above. The Native AOT disposition stands.
- **RECHECK-003 and RECHECK-005.** Evaluated with the protocol in
  [PR #547](https://github.com/hcoona/microsoft-authentication-cli/pull/547); unchanged.

### Retention

After [PR #548](https://github.com/hcoona/microsoft-authentication-cli/pull/548) merged on
2026-10-29, the SDK build servers were stopped and `C:\Temp\azureauth-pkgtest-20261029` (source,
packages, HTTP cache, both zips, extraction directory and validator) was deleted, together
with the local launchers, `roles.json`, ledger and the WSL launcher's temporary
directories. WAM sessions and caches were retained. The experiment ended with 34 I and
49 N unused.
