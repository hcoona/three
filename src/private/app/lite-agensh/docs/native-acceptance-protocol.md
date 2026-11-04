# Windows Native Acceptance Protocol

This protocol is consumed by the acceptance executor and independent
research-evidence reviewer. It owns the exact native observation boundary that
the broader [validation basis](validation.md) deliberately does not authorize.
The owner maintains it when the subject or effects change. It is not a passed
acceptance report.

## Subject and prerequisites

Subject: the C# LiteAgensh executable built from the exact candidate of the
implementation PR linked to [Issue #1526](https://github.com/hcoona/three/issues/1526).
Before execution, record that immutable commit/build hash, applicable check
results and accepted design/contract identities in the PR. No unspecified
latest checkout substitutes for the recorded candidate.

Use the owner's Windows host, root-selected .NET SDK, installed Git and Copilot
CLI. Initial metadata is .NET 10.0.403, Git 2.53.0.windows.1 and Copilot 1.0.105.
Use official `GitHub.Copilot.SDK` 1.0.28, whose released package pins CLI
1.0.105. Recheck installed versions, the pinned SDK/session/credit contracts and
required executable flags before relying on them. Changed interfaces pause
dependent work pending reviewed invocation/validation updates. Disable SDK
runtime acquisition and native auto-update; do not install another CLI.

The accepted Wave must still grant LiteAgensh work. Implementation prerequisites
and this protocol must already be accepted, with independent research-evidence
review. The owner explicitly approved using existing GitHub Copilot account
credits under the following native soft-limit semantics; no current account
balance, personal usage or authentication artifact belongs in public evidence.

## Owner-approved effects and budgets

- Use only existing GitHub Copilot authentication, entitlement and default
  model configuration. No BYOK/provider substitution, new subscription,
  billing change, account/credential inspection or access change.
- A single acceptance run has a total allowance of **10,000 AI Credits** across
  all its workers and fresh sessions. The acceptance campaign's cumulative
  allowance is **150,000 AI Credits**. The owner places no separate run-count
  ceiling. Preserve one durable cumulative ledger across repetitions/restarts.
- Allocate and persist native session reservations before paid dispatch, with the
  sum bounded by the run and remaining campaign allowances. Pass native
  SDK `SessionLimits.MaxAiCredits` for each allocation and read it back; do not
  reset limits or start a fresh session to evade a spent allowance. Reconcile
  complete native-reported usage before releasing reservations or admitting
  further paid work. Bind reservation/session/worker identity atomically.
- These are the official native **soft** credit limits. The owner accepts
  possible overshoot by an in-flight model response before the runtime stops
  the next call. Record actual overshoot; never label this a mathematical
  hard ceiling, aggregate token cutoff or exact prepaid reservation.
- If native limit applicability or final usage cannot be established,
  retain state/reservations and stop additional native model calls. Do not
  infer credit cost from tokens, wall-clock time or account balance.
- Per-run wall-clock fuse: 45 minutes, at most three workers. Stop on the
  first applicable configured deadline/credit limit, owner stop or unresolvable
  ownership/protocol failure. These are accident fuses, not development quotas.

The finite cumulative credit allowance and finite per-run duration/worker
limits bound repeated execution without a speculative global run-count limit.
Credential-free tests and non-model contract preflight do not spend a fictitious
credit amount. A strictly non-model SDK connection/readback may execute without
a paid reservation, including while an old unknown reservation blocks model
calls. It may create an explicit non-dispatch session with a finite native
limit, but must never send a prompt or continue pending work. An empty-session
subtotal is not measured zero-cost usage.

## Environment and fixture

Create a new persistent, uniquely named acceptance directory outside user
repositories and temporary memory filesystems. Record its local location only
in private execution state. Create a self-authored Git repository with:

- A minimal .NET console fixture and two independent source files with simple
  failing behavior, plus a declared SDK-only check command asserting both
  corrected results. No external package/model or private code dependency.
- Tracked target `AGENTS.md` containing the fixture's local instructions,
  a dirty tracked owner note and an untracked owner file. Capture hashes and
  original ref/index/status before LiteAgensh initialization.
- Distinct worker tasks for the two files, a shared root goal, peer review,
  candidate/integration/final checks, and run configuration with explicit
  local state/check/native permission choices.

Only the fixture, exact product candidate and owned local run state may be read
by native acceptance workers. Native backend context may use its existing
normal runtime configuration, but the executor does not expose a private
repository or raw session diagnostics as public evidence.

## Execution and expected observations

1. **Non-model preflight:** launch and register an installed native headless
   process with automatic updates disabled. Use its documented auto-assigned
   loopback endpoint and a process-local connection token; connect through SDK
   `RuntimeConnection.ForUri`. Do not log the token or bind remotely. Verify
   runtime identity; create an explicit persistent non-dispatch session with
   absolute fixture cwd and a finite SDK session limit. Read native metadata
   and cumulative metrics; verify session/cwd/applied limit, retain
   completeness, and do not infer free usage from an empty session. Configure
   the real local MCP helper for the worker-boundary check. No `Send`/prompt,
   pending-work continuation, slash command, account/quota query or provider
   substitution is allowed in this phase. Disconnect and settle owned
   processes. Record the exact build/runtime/SDK/configuration receipt.
2. **Initial billing calibration:** only after paid ledger admission and the
   preceding actual limit readback, create a separate explicit session with
   SDK `AvailableTools=[]`, no custom/MCP tools, a 30-credit native soft cap
   and a two-minute turn deadline. Reserve it within the same run/campaign
   allowances and verify that session's actual identity/cwd/30-credit cap
   before sending one short request to reply `READY`. This is a
   paid observation, not an informational command. Require complete
   cumulative native credit metrics before ordinary dispatch. If any usage
   is incomplete, unexpected tool execution occurs or required readback
   changes, abort and settle; retain any uncertain reservation. Do not
   repeat a failed calibration with unknown cost. A successful receipt binds
   the actual implementation build, SDK/runtime and accounting contract;
   changed relied-on inputs require a new bounded receipt.
3. **Cooperation:** run two native workers on their distinct tasks. Require
   actual local MCP task/claim/message/board operations, peer communication,
   candidate-bound review, checked integration and a retained passing local
   delivery. Preserve the original dirty checkout/instructions. Capture the
   expected results and exact candidate/final/check identities, not an
   assessment based only on agents saying they succeeded.
4. **Pause/cancel/fresh resume:** in a separate run or still-allocated segment,
   arrange a bounded active task, request pause while work is active, and
   verify owned process settlement and retained work. Explicitly resume and
   verify fresh native session associations plus durable state summary, then
   perform final checks. Unknown cancellation/child state blocks replacement.
5. **Crash reconnect:** use a separately allocated segment and terminate only
   the known owned coordinator process to simulate interruption. Inspect and
   settle surviving owned children before replacement. Verify exact saved
   session resume with `ContinuePendingWork=false` and
   `AllowTranscriptRecovery=false`, then verify identity/cwd, remaining native
   limit and nonregressing complete cumulative usage before another send.
   Otherwise record the labeled fresh-summary recovery only when budget and
   ownership gates permit it; unknown cost is not permission to start fresh.
6. **Reconcile:** quiesce the run and collect complete SDK usage metrics.
   Atomically reconcile the run/campaign ledger. Compare observed behavior
   against the required validation scenarios and publish only sanitized
   outcomes. Failed or incomplete runs remain failed/incomplete, not deleted
   successes. Repetition consumes the same cumulative allowance.

Use `TotalNanoAiu / 1,000,000,000` for native-reported AI Credits and retain
`AiCreditsStatus`; only `Complete` permits final reconciliation. Neither
`Partial`, `Unavailable`, missing status nor numeric zero proves free usage.
Charge cumulative deltas once, not the sum of live events, checkpoints and
snapshots. Read metrics after completed ordinary turns and on quiescence;
stop further paid dispatch on incomplete accounting or unexpected limit changes.
On exact resume, never regrant the original cap: apply at most the saved
reservation's remaining allowance and verify its cumulative baseline.

First establish platform-dependent behavior through these native observations;
use existing controlled scenario/unit/contract tests for exhaustive board,
retry, stale-head, conflict, quality-timer and failure classification cases.
Do not repeat all controlled cases against a paid model merely for symmetry.

## Stop, cleanup and evidence carrier

On timeout, spent/unknown allowance, unverified native policy, unexpected
external effect, ambiguous surviving writer or owner stop: stop dispatch,
request cancellation, settle known processes where possible and preserve
all work/evidence. Do not start a replacement or another paid call to repair
unknown budget/ownership.

An existing unknown reservation remains unknown and fully retained. This
protocol does not authorize resetting the campaign, treating startup failure
as measured zero, releasing that reservation, or bypassing the global paid-work
gate. Any proposed conservative carry-forward requires an explicit
repository-owner disposition before dependent paid execution; non-model SDK
inspection and credential-free implementation remain separate.

Keep logs, SQLite/journals, fixture worktrees and native session associations
local for diagnosis. Cleanup is separately explicit, limited to named owned
resources after outcomes/ownership are inspected. No broad directory/process
deletion and no automatic push/publication occurs.

The implementation/acceptance PR retains exact identities, environment,
commands/results, sanitized observations and counterexamples, actual reported
credits/overshoot, known limitations, independent domain/evidence review,
finding triage and owner acceptance. Never upload tokens, authentication
artifacts, current account usage/balance, private paths or raw agent transcripts.
Passing establishes only the tested Windows Copilot-backed boundary.
