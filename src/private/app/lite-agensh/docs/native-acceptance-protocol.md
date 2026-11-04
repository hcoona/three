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
Recheck installed versions, the official ACP/session/credit contracts and
required executable flags before relying on them. Changed interfaces pause
dependent work pending reviewed invocation/validation updates.

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
- Allocate and persist native session reservations before startup, with the
  sum bounded by the run and remaining campaign allowances. Pass native
  `--max-ai-credits` for each allocation; do not reset limits or start a fresh
  session to evade a spent allowance. Reconcile native-reported usage before
  releasing reservations or admitting further native work.
- These are the official native **soft** credit limits. The owner accepts
  possible overshoot by an in-flight model response before the runtime stops
  the next call. Record actual overshoot; never label this a mathematical
  hard ceiling, aggregate token cutoff or exact prepaid reservation.
- If native ACP limit applicability or final usage cannot be established,
  retain state/reservations and stop additional native model calls. Do not
  infer credit cost from tokens, wall-clock time or account balance.
- Per-run wall-clock fuse: 45 minutes, at most three workers. Stop on the
  first applicable configured deadline/credit limit, owner stop or unresolvable
  ownership/protocol failure. These are accident fuses, not development quotas.

The finite cumulative credit allowance and finite per-run duration/worker
limits bound repeated execution without a speculative global run-count limit.
Credential-free tests and non-model contract preflight do not spend a fictitious
credit amount; retain actual native reporting even when zero.

## Non-model interface discovery

The [first invocation counterexample][startup-counterexample] shows that
Copilot 1.0.105 rejects `--acp` together with `--usage-output-file` before
initialization. An advertised flag is not evidence that it works in every mode.
This observation does not establish that all official ACP usage paths are
unavailable.

To investigate that question, an exact recorded product candidate may omit the
incompatible usage-output flag **only for non-model discovery**. Retain the
native credit-limit flag, absolute fixture cwd, normal existing configuration,
owned process tracking and private diagnostic/output paths. Start at most one
native process per probe, with a three-minute probe deadline inside the existing
run fuse. No reasoning prompt, tool action or model-enabled turn is allowed.

After initialization and session creation, capture the advertised command
snapshot. At most four requests may invoke `/usage`, `/context`,
`/session info` and `/limits`, each at most once and only when its base command
is advertised. The official ACP documentation identifies the first three as
informational/direct operations; installed 1.0.105 `copilot help commands`
describes `/limits` as "View or edit session limits." The no-argument invocation
is view-only: never send `set`, `unset`, `reset` or another mutation. Do not send
an unadvertised command or infer that arbitrary slash text is non-model.

Record whether native output actually exposes the applied AI-credit limit and
session-cumulative credits, its format/unit/scope and any missing fields.
Capture the native output locally without treating a host-generated wrapper
as a native JSON schema. Do not read account quota/balance, credentials or other
sessions. If output unexpectedly contains account data, withhold those values
from inspection/public evidence and stop the affected command path.

Prior unknown reservations remain retained and fully charged against both
allowances. They block all model work, but not another strictly non-model probe
whose additional reservation fits the remaining run/campaign allowances.
Use the same durable campaign ledger; never reset it or release an unknown
reservation to enable a probe. No credit amount is inferred from the absence
of a model prompt. EOF/settlement ends the probe; missing native reports remain
explicitly unknown.

These observations may support a separately reviewed concrete usage/report
adapter and invocation update. They do not admit model dispatch, relax the
applied-limit/final-usage gate, establish crash accounting or constitute product
acceptance. Paid execution remains stopped until the accepted concrete contract
and its evidence establish the required native behavior.

[startup-counterexample]: https://github.com/hcoona/three/pull/1530#issuecomment-6622441256

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

1. **Non-model preflight:** start a native ACP process with a reserved finite
   credit limit and usage-output path; initialize and negotiate; create a
   session with absolute fixture cwd and the local MCP helper. Record advertised
   capabilities/commands. Use only a documented advertised informational
   operation to verify applied limits/usage; unknown slash text is not sent as
   a presumed non-model command. Establish the exact native usage-report fields
   and version before interpreting credits. If any required limit/report
   cannot be verified, stop before a model prompt. The bounded non-model
   discovery variant above may investigate an incompatible output-file flag;
   it never substitutes an unverified report for successful preflight.
2. **Cooperation:** run two native workers on their distinct tasks. Require
   actual local MCP task/claim/message/board operations, peer communication,
   candidate-bound review, checked integration and a retained passing local
   delivery. Preserve the original dirty checkout/instructions. Capture the
   expected results and exact candidate/final/check identities, not an
   assessment based only on agents saying they succeeded.
3. **Pause/cancel/fresh resume:** in a separate run or still-allocated segment,
   arrange a bounded active task, request pause while work is active, and
   verify owned process settlement and retained work. Explicitly resume and
   verify fresh native session associations plus durable state summary, then
   perform final checks. Unknown cancellation/child state blocks replacement.
4. **Crash reconnect:** use a separately allocated segment and terminate only
   the known owned coordinator process to simulate interruption. Inspect and
   settle surviving owned children before replacement. Verify exact saved
   session load when advertised; otherwise record the labeled fresh-summary
   recovery and absence of lost durable work. Do not call unsupported methods.
5. **Reconcile:** quiesce the run and collect final native usage reports.
   Atomically reconcile the run/campaign ledger. Compare observed behavior
   against the required validation scenarios and publish only sanitized
   outcomes. Failed or incomplete runs remain failed/incomplete, not deleted
   successes. Repetition consumes the same cumulative allowance.

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
