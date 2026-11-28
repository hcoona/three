# LiteAgensh Requirements

These are required behaviors for the single-machine product, not claims about
an existing implementation. The [README](../README.md) supplies user context;
[`specs/`](../specs/) and [architecture](architecture.md) own the abstract
coordination model and its downward refinement; [validation](validation.md) owns
required evidence; and [conceptual provenance](upstream-migration.md) records
how the model relates to [_Agensh_](https://arxiv.org/html/2609.26781v1).

## Organization and observation plane

### LA-REQ-001: Local single-user host

The product shall coordinate equal coding peers on one machine using local
persistent state, local Git worktrees, and owned native agent runtimes. Its
implementation shall be C# targeting the repository-selected .NET toolchain,
with initial real-platform acceptance on Windows and no hosted Git, chat,
database, or container server requirement.

### LA-REQ-002: Native single-agent loop delegation

Each peer's reasoning, file/shell tool execution, model transport, and context
compaction shall remain delegated to the underlying Copilot runtime via its
official .NET SDK. Admitted peers within an active epoch may share a parent
runtime scope while retaining isolated conversations, working directories,
actor-bound MCP channels, soft credit caps, and session-level usage readings.
Verified missing capabilities or violated session boundaries shall fail closed
without runtime patching or silent backend substitution.

### LA-REQ-003: Fenced epoch ownership and recoverable state

Run identity, resolved configuration, base artifact state, causal log entries,
peer cursors, workspace proposals, and hierarchical effect-scope states shall
persist locally across controller restarts (`EpochLifecycle.tla`). Exclusive
controller epochs shall be fenced monotonically: a new epoch, final verification
pass, or resource cleanup shall never start while any predecessor effect scope
(parent `runtime` host, child `channel:<peer>` MCP helper process, or `verify`
check runner) remains `Active` or `Ambiguous`. Storage errors shall fail closed
with explicit error diagnostics without silently recreating state.

### LA-REQ-004: Self-organized equal-peer work selection

Peers shall self-organize without a central task scheduler, privileged planner
stage, or exclusive task locks: multiple peers may publish advisory expiring
claims and negotiate overlaps directly. An optional nonnegative
`workerStartIntervalSeconds` setting staggers peer admissions into an open epoch
without preassigning roles or opening sessions for unadmitted peers.

### LA-REQ-005: Asynchronous communication and operator participation

The product shall provide shared task tracking, organization-wide announcements,
targeted urgent direct messages, and operator messages/reviews/stop requests
under run-scoped identities (`owner` and `worker1..workerN`). Operator questions
or messages shall not block unrelated peers from progressing.

### LA-REQ-006: Typed shared context

Peers shall publish concise typed shared-context entries (`FACT`, `OBSERVED`,
`FAIL`, `CLAIM`, and `PATCH_SUMMARY`) with optional expandable detail, advisory
claim expiry, replacement/invalidation, bounded recent-window projection, and
boolean/literal full-history search. Concrete length/window tuning bounds belong
to configuration/policy defaults rather than altering stream invariants, and
published text is never treated as an executed verification check.

### LA-REQ-007: Single causal log and co-axial cursor projection

Deferred notifications and immediate observations (urgent direct messages and
peer shared-context entries) shall share one global append-only causal log
consumed through two co-axial per-peer durable cursors
`0 <= baseCursor <= immCursor <= |log|` (`CausalStream.tla`), preserving global
causal order across normal and immediate events without duplicate stream/entity
tables. At turn start `T = |log|`, `UnreadTurnIdx` over `(baseCursor..T]`
excludes `imm` items in `(baseCursor..immCursor]` already piggybacked in a prior
turn, preserves every visible non-keyed item (`NoKey`) verbatim, and folds keyed
state updates (`task:<id>`, `proposal:<id>`) across `(baseCursor..T]` (including
newer `imm` updates `<= immCursor`) to the latest visible sequence index. Turn
aborts—from timeout, `pause`, drain expiry, soft cost cutoff, or crash
recovery—discard only ephemeral active-turn cursors (`turnSnap := -1`,
`inflightImm := immCursor`) with zero persistent cursor writes, zero attempt
penalties, and zero "dead-letter" drops; persistent execution faults trip
`FaultedClosed`.

### LA-REQ-008: Algebraic channel fencing and bootstrap state

Local collaboration MCP tool calls and returns during an active turn shall
piggyback newly appended immediate `log` items `(inflightImm..|log|]` and
advance `inflightImm` atomically, fenced by `InTurn(peer)` (`turnSnap >= 0`) and
the peer's signed monotonic channel token `chan` (`+k` when bound at attachment
`k`, `-k` when unbound after `k`) so stale pre-reconnect or post-abort helpers
cannot mutate state or advance cursors (`ABA` safety) and completed turns
commit `(baseCursor := turnSnap, immCursor := inflightImm)` without spawning
duplicate turns. Conversational bootstrap state (`needsBootstrap`) remains
independent of channel recycling: single-turn timeouts and crash reconnects
advance `|chan| + 1` while preserving `needsBootstrap` and saved conversational
memory, whereas `Paused` continuation or safe fresh-session fallback sets
`needsBootstrap := true` until a turn commits (`AgenshOrganization.tla`).

### LA-REQ-009: Minimal native collaboration MCP surface

Expose a concise, actor-bound local stdio MCP surface directly representing the
organization's core primitives—organization status, tasks, messages, shared
context, and workspace proposals—rather than cloning hosted Gitea or Mattermost
REST endpoints. Tool inputs shall never override the helper's bound run, actor,
or channel-epoch fence token.

## Workspace integration, lifecycle, and guardrails

### LA-REQ-010: Immutable base checkout and isolated worktrees

Bind each run to an explicit local base revision (`baseRef`), allocating a
dedicated target integration ref, check worktree, and isolated per-peer branches
and worktrees (`WorkspaceCas.tla`). Never mutate the user's original checkout,
index, untracked files, global Git config, or tracked `AGENTS.md`. Creating a
new target repository requires explicit `createRepository: true`.

### LA-REQ-011: Two-phase cross-store CAS and emergent peer integration

Peers may author multi-commit branches, merge the latest target ref locally into
their peer branch (`PeerSyncMerge`) to resolve divergence while preserving local
branch history, open multiple proposals, and act as authors or emergent Peer
Integrators to request integration of an `open` proposal (`WorkspaceCas.tla`).
Conflicting (delegating multi-base and three-way merge conflict detection to
Git), unapproved, or check-failing requests shall clear `requested := false`
without losing peer commits or spinning in a livelock. Cross-store integration
shall execute as an explicit two-phase protocol—metadata pre-commit
(`committing`) $\to$ atomic Git target-ref CAS to the verified merged commit
$\to$ metadata completion (`integrated` or `open`)—completing in-flight
`committing` CAS steps cleanly across `Draining`/`Settling` and reconciling
mid-step crashes solely from target-ref ancestry (`ancestors[targetRef]`).

### LA-REQ-012: State-diff review gates and distinct check stages

Keep candidate, integration, and final verification checks distinct with
retained command, tree/commit, exit code, and log evidence. Non-author peer
approval of the exact candidate head shall be enforced whenever
`requirePeerReview` is true or the per-path net state difference
`ProtectedDiff(snapTarget, mergedTree)` touches configured `protectedPaths`. No
time-based broken-main bypass timer is included.

### LA-REQ-013: Unified epoch activation, bounded drain, and bottom-up teardown

Expose a unified lifecycle CLI (`init`, `preflight`, `run`, `status`, `pause`,
`finalize`, `cleanup`), where `run` implements `ActivateEpoch`
(`EpochLifecycle.tla`): starting an `Idle` run, resuming a `Paused` run with
`needsBootstrap := true` and a durable state summary, or reconnecting an
interrupted epoch (preserving `needsBootstrap` while advancing `|chan| + 1` on
channel re-attachment) once all predecessor effect scopes are `Stopped`.
Fresh-session fallback on reconnect is permitted only when `peerExec !=
'Unmeasured'`; otherwise—or whenever an epoch settles into `Quiescent` with any
non-`Idle` `peerExec`—the sum-type meter transitions to `Indeterminate`
(`FaultedClosed`).

Normal time-budget expiry transitions `Open -> Draining` (closing new channel
and turn admission while granting a bounded drain window for in-flight turns and
in-flight workspace evaluations) before `Settling`; `Paused` or `FaultedClosed`
transitions directly to `Settling`. Parent `runtime` scopes shall never be
marked `Stopped` until all registered child `channel:<peer>` helper scopes under
them have `Stopped`.

### LA-REQ-014: Algebraic epistemic meter and stop reasons

Model and enforce per-run wall-clock (`durationSeconds`), autonomous idle
wakeups, and soft AI-credit guardrails (`runAiCreditAllowance`) over a sum-type
epistemic meter `Exact(cost) | Indeterminate` (`EpochLifecycle.tla`),
distinguishing terminal stop reasons `GoalConverged`, `Paused`, `TimeLimit`,
`SoftCostLimit`, and `FaultedClosed`. Native SDK soft caps may overshoot in
flight; unreadable, incomplete, or regressing usage on a dispatched turn
transitions `meter` to `Indeterminate` (`FaultedClosed`) without fabricating
zero spend or requiring offline reconciliation commands.

### LA-REQ-015: Crash-idempotent final verification and explicit cleanup

`finalize` shall reconcile any stopped interrupted epoch record and execute
declared `finalChecks` under an owned verification scope without clobbering a
settled `stopReason`, remaining idempotent if interrupted and re-run
(`EpochLifecycle.tla`). A `Passed` final verdict requires settled scopes,
`Exact` metering, a non-faulted stop reason, and passing `finalChecks`; state
and the local target ref remain retained until explicit `cleanup` after
finalization.

### LA-REQ-016: Trusted-host boundary and attribution

Agent sessions and configured checks run under the operator's OS account with
explicit `nativePermissionPolicy` (`deny` or `trusted`); worktrees isolate edits
rather than OS permissions or credentials. Never copy or proxy model secrets,
override enterprise policy, or upload local state. Retain the Microsoft MIT
notice in [`THIRD-PARTY-NOTICES.txt`](../THIRD-PARTY-NOTICES.txt).

### LA-REQ-017: Declarative YAML and Markdown configuration

`init --config` shall accept a strict single-document YAML configuration
(`.yaml`/`.yml`, validated by `schemas/run.schema.json`) paired with a non-empty
Markdown `goalFile`, resolving relative `repositoryPath`, `statePath`, and
`goalFile` paths against the YAML directory and snapshotting the resolved
configuration into run state before creating worktrees or starting runtimes.

### LA-REQ-018: Retired - current authority: LA-REQ-003

## Explicitly unsupported in this outcome

Legacy inline JSON configuration; cross-run credit ledgers, paid calibration, or
offline pre-dispatch cancellation commands; mutable per-recipient event queues
with retry-exhaustion message drops; privileged planner stages or broken-main
timers; Gitea/Mattermost compatibility shims; alternate agent backends;
multi-host or untrusted-sandbox execution; and ProgramBench reproduction.
