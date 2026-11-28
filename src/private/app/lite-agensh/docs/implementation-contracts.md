# Implementation Contracts

This record defines the concrete CLI, YAML configuration, SQLite schema, MCP
surface, and Copilot SDK bindings refining [`specs/`](../specs/) and
[architecture](architecture.md) to satisfy [requirements](requirements.md).

## Application and dependencies

Build one `LiteAgensh.csproj` executable (`LiteAgensh` namespace,
`$(CurrentTargetFramework)`, `CopilotSkipCliDownload=true`) using root CPM and
lockfiles:

- `GitHub.Copilot.SDK`: official typed runtime, session, permission, soft-limit,
  and cumulative-usage client. Normal reviewed CPM/Renovate upgrades are
  permitted without version allowlists; saved-session recovery verifies
  unchanged SDK identity.
- `Microsoft.Data.Sqlite`: WAL persistence with parameterized SQL and finite
  busy timeouts.
- `ModelContextProtocol`: low-level stdio `McpServer` (`ListToolsHandler`,
  `CallToolHandler`) bound at launch to `(statePath, actor, channelToken)`.
- `YamlDotNet`: strict single-document YAML configuration loader.

Launch one owned installed Copilot CLI per active epoch in headless mode
(`--no-auto-update`, loopback `RuntimeConnection.ForUri` token). Record the
observed non-empty runtime version as provenance without imposing CLI version
gates, and verify required typed session/limit/usage APIs before dispatch.

## Unified CLI surface

Non-MCP commands emit `{ok: boolean, code: string, message: string, data: object}`
and exit `0` iff `ok == true`:

- `init --config <RUN_YAML>`: validates YAML + Markdown `goalFile`, initializes
  `statePath/run.sqlite`, snapshots configuration and goal, and allocates
  isolated Git refs/worktrees without model calls.
- `preflight --state <STATE_DIR>`: checks scope-containment readiness, Git/state
  integrity, and non-model SDK session/cwd/model/limit readback on a settled
  temporary host.
- `run --state <STATE_DIR>`: unified `ActivateEpoch` entry point
  (`EpochLifecycle.tla`)—starts an `Idle` run, continues a `Paused` run with
  `needs_bootstrap = 1` and durable state summary, or reconnects an interrupted
  epoch (preserving `needs_bootstrap` while advancing `ABS(chan_token) + 1` on
  channel re-attachment) once all `effect_scopes` are `Stopped` (refusing
  `Ambiguous` scopes or unrecoverable `Unmeasured` turns).
- `pause --state <STATE_DIR>`: records an operator `Paused` stop signal in
  SQLite without requiring `coordinator.lock`.
- `status --state <STATE_DIR>`: read-only projection of `run_epoch`, `phase`,
  `stop_reason`, `meter`, `effect_scopes`, `peers`, and `targetRef`.
- `finalize --state <STATE_DIR>`: reconciles any stopped interrupted epoch and
  executes `finalChecks` idempotently under an owned `verify` scope without
  overwriting `stop_reason`.
- `cleanup --state <STATE_DIR>`: requires `Quiescent` finalized state and all
  `effect_scopes` `Stopped`, removing only run-owned worktrees, refs, and
  `statePath`.
- `tool --state <STATE_DIR> --name <TOOL> --json <JSON_OBJECT>`: invokes a
  domain collaboration operation as actor `owner`.
- `mcp --state <STATE_DIR> --actor <PEER_ID> --channel <TOKEN>`: internal stdio
  MCP helper process spawned by `runtime` for one peer channel attachment;
  atomically verifies `epochs.phase = 'Open' AND
effect_scopes['runtime'].state = 'Active' AND session_meters.exec_state = 'Idle' AND
COALESCE(effect_scopes['channel:<peer>'].state, 'Stopped') = 'Stopped' AND
peers.chan_token = TOKEN` to register `(pid, start_ticks, 'Active')` as
  `channel:<peer>` under `parent_id = 'runtime'` in `Main` before serving
  requests (leaving `'Stopped'` clearing exclusively to Coordinator settlement
  and post-exit recovery), and uses immutable launch-bound `--actor` + positive
  `--channel <K>` (`turn_snap >= 0 AND chan_token = K`) as a cooperative
  concurrency/ABA staleness fence across reconnects and turn aborts
  (`LA-REQ-008`, `LA-REQ-016`).

## Declarative YAML configuration

`init --config` accepts strict single-document YAML (`.yaml`/`.yml`, guided by
`schemas/run.schema.json`):

- **Required:** `repositoryPath`, `statePath`, `goalFile` (non-empty Markdown
  file; relative paths resolve against the YAML file's directory).
- **Optional:** `baseRevision` (`"HEAD"`), `workerCount` (`2`, `>= 1`),
  `workerStartIntervalSeconds` (`0`, `>= 0`), `durationSeconds` (`1800`, `>= 1`),
  `idleWakeSeconds` (`600`, `>= 1`), `drainSeconds` (`30`, `>= 1`),
  `nativeExecutable` (`"copilot"`), `nativeArguments` (`[]`, optional reasoning
  effort), `nativeModel` (`null`), `nativePermissionPolicy` (`"deny" | "trusted"`,
  default `"deny"`), `runAiCreditAllowance` (`10000`, `>= 30`),
  `candidateChecks` (`[]`), `integrationChecks` (`[]`), `finalChecks` (`[]`),
  `requirePeerReview` (`true`), `protectedPaths` (`[]`), and `createRepository`
  (`false`).
- Reject unknown or duplicate keys, inline `goal`, null items, and invalid types
  before creating `statePath`. Update `schemas/run.schema.json` and
  `examples/run.yaml` atomically with `RunConfiguration` in the implementation
  slice.
- Each check is an executable with an argument vector (default empty) and a
  positive `timeoutSeconds` (default `120`), run in the stage's check worktree.
  Do not interpolate a command string or resolve the executable relative to
  the YAML file. `protectedPaths` patterns refer to Git repository paths.

### Initialization and command admission

`init` takes exclusive ownership of a new, dedicated `statePath` outside the
user checkout. It writes the run ID, resolved configuration, goal text, and
intended run-owned paths/ref names before adding worktrees. Resolve
`baseRevision` to a **commit OID** once. If `repositoryPath` does not exist,
only explicit `createRepository: true` may initialize it; a new repository
needs an initial commit for the default `HEAD`. Never seed or repair an
existing repository implicitly. A repository created for the operator is not
a cleanup target.

Create the run target, each peer's branch/worktree, and a detached check
worktree at that OID. Publish readiness in one SQLite commit with the root
goal task, configured peer identities, initial cursors, and `Exact(0)`. An
initializing run is not `Idle`: no other command can activate it. If creation
fails, report the owned resources and undo only resources known to have been
created by this invocation. An interrupted or unrecognizable directory is
reported as incomplete, never adopted, overwritten, or recursively cleaned.
Only `init` may create a database; all other commands open existing state
and reject an unknown schema or storage error.

`preflight`, `run`, `finalize`, and `cleanup` take `coordinator.lock`
exclusively (a second controller gets `busy`). Preflight borrows stopped
scopes only in `Idle` or unfinalized `Quiescent(Paused)`; its temporary sessions
do not become saved peers or alter the run meter. It checks the configured
working directory, model, permission policy, session isolation, usage/limit
interfaces and scoped teardown without sending a prompt. Failure to settle
the probe remains visible in `effect_scopes`, not a successful preflight.
`status` samples one SQLite snapshot and labels the separately resolved target
OID; it neither reconciles a `committing` row nor promises a cross-store
snapshot. `pause` records a pending signal only for an active `Open` or
`Draining` epoch; repeated requests for that epoch coalesce. A successful
response means **recorded**, not **all peers stopped**.

## SQLite schema refinement

One `run.sqlite` database projects the four-plane abstract state without
duplicate stream/entity storage:

1. **Epoch, hierarchical scopes, and sum-type meter (`EpochLifecycle.tla`):**
    - `meta(key PRIMARY KEY, value)` — `schema_version`, `run_id`, initialization/cleanup
      state, owned workspace locations, `base_commit`, `configuration_json`,
      goal text, sum-type `meter` (`>= 0` exact nano-AIU sum, or
      `'-1'` for `Indeterminate`), and `final_verdict` (`'Unverified' | 'Passed'
| 'Failed'`) with the corresponding final check attempt and target OID.
    - `epochs(epoch PRIMARY KEY, phase, stop_reason, started_at, deadline_at, ended_at)`.
    - `effect_scopes(scope_id PRIMARY KEY, parent_id, kind, pid, start_ticks, state)` —
      Coordinator-spawned roots (`runtime`, `verify`: pre-PID write-ahead
      `'Ambiguous'`, promoted to `'Active'` with `(pid, start_ticks)` under
      Windows `KILL_ON_JOB_CLOSE` or a POSIX `setsid` group) and
      `runtime`-spawned `channel:<peer>` helpers (upserted directly as
      `'Active'` with `(pid, start_ticks)` under `parent_id = 'runtime'` in
      `mcp Main`, never pre-written without a PID); settled bottom-up via
      persisted `(pid, start_ticks)` exit (plus `kill(-pid, 0) == ESRCH` on
      cooperative POSIX roots), while PID-less pre-PID `'Ambiguous'` root rows
      fail closed.
    - `session_meters(session_id PRIMARY KEY, peer, sdk_identity, cumulative_nano_aiu, exec_state)` —
      one baseline per original session, including retired sessions;
      `exec_state IN ('Idle', 'Running', 'Unmeasured')` (`peerExec`); settling
      an epoch into `Quiescent` with any non-`'Idle'` row sets `meta['meter'] =
'-1'` (`Indeterminate`) and `stop_reason = 'FaultedClosed'`.
    - `control_signals(id PRIMARY KEY, epoch, reason, created_at, consumed_at)`.
2. **Single causal log and co-axial peer cursors (`CausalStream.tla`):**
    - `causal_log(seq PRIMARY KEY AUTOINCREMENT, author, target, immediate, dedup_key, kind, summary, detail, meta_json, expires_at, replaces_seq, created_at)` —
      strictly append-only single source of truth for discussion messages
      (`kind = 'message'`, `dedup_key NULL`), task/proposal comments
      (`kind = 'comment'`, `dedup_key NULL`), shared-context entries (`kind IN
('FACT','OBSERVED','FAIL','CLAIM','PATCH_SUMMARY')`, `immediate = 1`,
      `dedup_key NULL`, optional `replaces_seq REFERENCES causal_log(seq)`), and
      coalesced state notifications (`dedup_key = 'task:<id>' | 'proposal:<id>'`,
      `immediate = 0`). At turn start `T`, `UnreadTurnIdx` selects visible rows
      in `(base_cursor..T]` with `(seq > imm_cursor OR immediate = 0)` where
      `dedup_key IS NULL` or `seq` equals `MAX(seq)` for that `dedup_key` across
      all visible rows up to `T` (including `immediate = 1` rows `<= imm_cursor`).
    - `peers(peer PRIMARY KEY, needs_bootstrap, chan_token, base_cursor, imm_cursor, turn_snap, inflight_imm, saved_session_id)` —
      `0 <= base_cursor <= imm_cursor`, signed `chan_token` (`+k` bound, `-k`
      unbound), and `turn_snap` (`-1` idle, `>= 0` active snapshot `T`).
    - Mutable entity state: `tasks(id PRIMARY KEY, title, body, state, author, closed_by, updated_at)`.
3. **Two-phase workspace proposals (`WorkspaceCas.tla`):**
    - `proposals(id PRIMARY KEY, author, branch, head_oid, title, body, status, requested, approval_review_id, last_outcome, updated_at)` —
      `status IN ('open', 'committing', 'integrated')`, `requested IN (0, 1)`.
    - `proposal_reviews(id PRIMARY KEY, proposal_id, reviewer, head_oid, decision, body, created_at)`.
    - `verification_runs(id PRIMARY KEY, attempt_id, proposal_id, stage, commit_oid, tree_oid, command_json, status, exit_code, log_path, created_at)` —
      one result per attempted command; final attempts have no proposal.

## Minimal native MCP tool catalog

Tools execute under immutable launch-bound `(run, actor, launchChannel)`. Every
successful peer MCP mutation or observation commits inside an `IMMEDIATE` transaction
requiring `peers.turn_snap >= 0 AND peers.chan_token == launchChannel`,
rejecting stale or unbound helpers without mutating rows or cursors
(`CausalStream.tla` / `AgenshOrganization.tla`). Git-derived results may need
side-effect-free preparation **before** that transaction. Only settlement
invalidates the whole cohort on abort/fault; only a subsequent attachment
reserves `ABS(chan_token) + 1`. Valid active calls
piggyback visible `immediate = 1` rows in `(inflight_imm..MAX(seq)]` and advance
`inflight_imm` atomically iff that visible slice is non-empty.

| Tool              | Operations / parameters                                                                                                                                         | Result payload                                                                                 |
| ----------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `org_status`      | `{}`                                                                                                                                                            | `{goal, baseCommit, targetRef, phase, members, activeClaims}`                                  |
| `task_mutate`     | `action: "create"\|"update"\|"comment"\|"close"`, `id?`, `title?`, `body?` (`close` on root goal task signals `GoalConverged` candidate)                        | Task object `{id, title, body, state, author, closedBy, comments, claimants}`                  |
| `task_query`      | `id?: integer, state?: "open"\|"closed", query?: string, cursor?: integer, limit?: integer`                                                                     | Single task or `{items, nextCursor}`                                                           |
| `message_post`    | `to: "team"\|<actor>, body: string, replyTo?: integer, urgent?: boolean` (urgent direct messages are immediate)                                                 | `{seq, author, to, threadSeq, urgent, createdAt}`                                              |
| `message_query`   | `channel?: "team"\|<actor>, threadSeq?: integer, query?: string, cursor?: integer, limit?: integer`                                                             | `{items, nextCursor}`                                                                          |
| `context_publish` | `type: "FACT"\|"OBSERVED"\|"FAIL"\|"CLAIM"\|"PATCH_SUMMARY", summary: string, detail?: string, workLabel?: string, ttlSeconds?: integer, replacesSeq?: integer` | `{seq, author, type, summary, workLabel, expiresAt}` (appends `causal_log` with `immediate=1`) |
| `context_query`   | `mode: "recent"\|"search"\|"expand", query?: string, seqs?: integer[], cursor?: integer, limit?: integer` (queries `causal_log` directly)                       | `{items, nextCursor}`                                                                          |
| `proposal_mutate` | `action: "open"\|"update"\|"comment"\|"review"\|"request_integrate"`, `id?`, `title?`, `body?`, `head?: string`, `decision?: "approve"\|"revoke"`               | Proposal or review object (`request_integrate` sets `requested = 1` for any peer integrator)   |
| `proposal_query`  | `id?: integer, status?: "open"\|"committing"\|"integrated", cursor?: integer, limit?: integer`                                                                  | Proposal detail (including `diff`, `reviews`, `verificationRuns`, `lastOutcome`) or page       |

### Collaboration calls and projections

The shared handler receives either the launch-bound peer or the CLI's `owner`;
JSON cannot choose an identity. Owner reads do not need a turn. Owner writes
participate in `Open` or `Draining`, without a peer cursor, and cannot admit a
peer, assign a task, or advance the Git target. All callers get the committed
result; validation, stale-fence/head, and busy errors commit **nothing**.
Return the result and the peer's ordered immediate slice only after commit.
A lost reply is not proof of rollback: do not automatically replay mutations;
inspect the entity/log. An uncertain peer tool exchange must not be certified
by successful turn completion; the abort path preserves its durable events
but rewinds provisional delivery. The helper serves one call/response at a
time per channel, not across the whole run. Reusing the token on the next
successful turn relies on the SDK's terminal turn boundary covering all
previous tool exchanges; stdio and a positive token alone do not prove this.

- **Tasks.** All participants may create, update or close an open task and
  comment on a task; there is no assignee lock. Create/update/close appends a
  keyed state event; comments are immutable, non-keyed rows linked by task
  ID. Closing the root task and queuing `GoalConverged` are one transaction;
  repeated close does not emit a second signal. Claims do not change task
  state or authorize a transition.
- **Messages.** A team post is readable by everyone; direct messages are
  readable only by sender and recipient, including `owner` when a party.
  A reply references a visible message in the same conversation and inherits
  its root `threadSeq`. For automatic turn/piggyback delivery, exclude the
  caller's own rows (`VisibleTo`); explicit queries may return the caller's
  own posts. Publishing an urgent team message does not interrupt all peers.
- **Shared context.** Store summary and optional detail once. `recent` returns
  bounded summaries, hiding expired claims and superseded entries; `expand`
  returns requested visible entries by exact sequence, including detail and
  expiry/replacement status. Only a new entry by the same author may replace
  their current entry of the same type; this never edits the old row.
  A `CLAIM` requires a `workLabel` (use `task:<id>` for a task) and positive
  TTL, defaulting to `idleWakeSeconds` when omitted. Active means not replaced
  and `expires_at >` the query's sampled UTC time. Two active claims on the
  same label are both returned. These are query projections: expiry and
  replacement do not discard raw `NoKey` notifications from the causal stream.
- **Search and paging.** Context `search` uses SQLite FTS5 on summary/detail:
  quoted phrases for literals, FTS5 boolean expressions for combinations;
  invalid syntax is an input error, not an empty match. An external-content
  index over context rows is maintained in the publishing transaction; history
  includes expired/replaced rows. `recent` considers the newest 100 current
  entries. Task/message text filters are literal matches. For lists the
  integer cursor is exclusive: tasks/proposals ascend by ID, messages/context
  descend by sequence. Apply visibility and filters before the page limit
  (default 20, maximum 100); return the last key only if more match. Each
  page is a fresh read, not a held snapshot across calls. These limits never
  clip `UnreadTurnIdx` or acknowledge an undelivered immediate slice.

### Proposal identity, reviews and versioned reads

`open` takes the author's run-owned branch tip, not a caller-selected ref or
the base commit; reject an already proposed head. `update` can replace the
head only on the author's open proposal. An explicit `head` on either action
must match the tip OID. A head change clears `requested` and the effective
approval; old reviews and checks remain historical. A text-only edit does not
silently pick up a newer branch tip. Comments are non-keyed log entries. A
review names the **exact observed head OID**. In the review transaction a
non-author peer can set (`approve`) or clear (`revoke`) `approval_review_id`
only if `status = 'open' AND head_oid = H`. Once precommit wins, reject
either decision without writing a review, event, or cursor. This is one
current approval bit, not votes or a quorum. Owner reviews are
attributed feedback and never satisfy or revoke the peer gate.

Both `review` and `request_integrate` require `head`. Any peer can request
integration for the named open head if it is not already in the target
history; a duplicate pending request reports the existing request. The
receipt acknowledges the durable request, not evaluation or landing.

Only three peer operations need a Git ref held at a particular OID: open,
replace a head, and request integration. The helper forwards these over a
run-local pipe to the coordinator with its immutable launch identity. The
coordinator executes the **entire** fenced collaboration transaction,
including the immediate slice; the helper only returns that reply. All other
collaboration operations need no such round trip. There is no durable RPC
queue or replay after a lost reply.

The coordinator resolves the OIDs and predicates outside SQLite, then holds
a Git `update-ref --stdin` prepared **verify** of the relevant refs at those
OIDs while the short SQLite transaction rechecks the proposal, actor, and
channel. Release the guard **after** SQLite ends. A moved ref returns `stale`.
All prepared Git processes are children of the coordinator **outside** the
killable `runtime` and `verify` trees. Only the coordinator holds the stdin
writer: helper loss cannot kill Git while it holds a ref, and coordinator
loss closes the pipe so Git can finish a sent command or abort on EOF. On
settlement release outstanding guards before retiring the coordinator. This
does not exempt any peer, helper, or configured check from OS containment.

`proposal_query` lists are SQLite-only. Detail returns a **versioned review
diff**, not "the current diff" across two stores:

1. Read the proposal ID/head `H` without side effects. Resolve the run target
   once to `T` in Git. Require one merge base `B` of `T,H`; compute the patch
   `git diff --no-ext-diff --no-textconv B H` on those immutable OIDs.
2. In the final SQLite transaction, check the caller fence (peer only) and
   that the proposal still names `H`. Read status, reviews, verification
   attempts and last outcome there, and commit the ordinary peer piggyback.
3. Return `headOid: H` and `diff: {targetOid: T, baseOid: B, headOid: H, patch}`.
   If the head changed, return `stale` with no cursor movement; the caller
   can issue a new query. A later target advance does not relabel the result.
   No unique base or unreadable Git data is an explicit error, never an empty
   patch. `B..H` is for the reviewer; only `T` versus the **actual merged
   tree** decides whether integration touches a protected path.

## Session attachment and accounting

The controller owns **one** loopback runtime/client in an epoch. Each admitted
peer has its own SDK session, working directory, permission handler, and
stdio helper. The run-wide allowance is also the maximum configured native
soft cap for an individual session's current accounting window; these are
not reserved shares. The controller checks the **aggregate** before every
dispatch. A native limit prompt cancels the blocked request; it never
increases the cap or changes enterprise policy. Concurrent turns may overshoot.

1. **Open without dispatch.** On a new owned runtime, create a session or
   cold-resume the exact `saved_session_id`, with `ContinuePendingWork = false`
   and `AllowTranscriptRecovery = false`. The LiteAgensh MCP server is **only**
   added ephemerally to a live session, never configured for automatic start
   during create/resume. Do not send a prompt or continue pending work.
   Verify the original SDK artifact identity for resume, returned session ID,
   exclusive idle attachment, effective worktree/model, policy and cap. A
   metadata lookup can establish existence, not usage or successful resume.
   If the host cannot establish these boundaries, do not dispatch.
2. **Reconcile before binding.** On crash activation, prior `Running` rows
   become `Unmeasured`; cursors are not acknowledged. For each such peer read
   qualified cumulative usage on the **opened original session**. In the
   reservation transaction, apply the delta below and make execution `Idle`.
   If still `Open` and below allowance, reserve `K = ABS(chan_token) + 1`,
   binding the session ID and SDK identity to that peer. If the reading
   reaches the allowance, settle instead of issuing a token. Do not dispatch
   any peer while predecessor spend remains unmeasured. An unavailable
   original session/reading for `Unmeasured` means `Indeterminate`, not a
   replacement session. For an `Idle` peer whose saved session cannot be
   loaded, one fresh-session fallback is safe **before** starting a helper:
   retain the old meter row, bind a new session and set `needs_bootstrap`.
3. **Start once; then send.** After reservation commits, use session-scoped
   `Mcp.StartServerAsync` with the stdio config for `(statePath, peer, K)`.
   Wait for server readiness **and** the matching `mcp Main` registration.
   A failed or uncertain start fences the cohort and settles the host; do not
   retry that launch with `K` or start a replacement helper on the same host.
   Only `ComposedBeginTurn` may now record `Running` and the snapshot before
   sending the prompt. Normal turn completion does not recycle the channel.
4. **Account at the turn boundary.** Accept completion only for the
   controller's outstanding send on that session and token, after the SDK
   turn and its tool exchanges have ended. Consume that completion once;
   never treat a delayed callback, a tool reply, client disposal, or a
   cancellation request as a terminal usage observation. A qualified reading
   is session-wide integer `TotalNanoAiu`, nonnegative and nonregressing,
   with `AiCreditsStatus.Complete` and an established end to the dispatched
   work. `Complete` describes _observed_ calls; it is not itself that end.

For session baseline `b` and qualified reading `u`, one guarded SQLite
transaction writes `cumulative_nano_aiu = u`, adds `u - b` to the exact run
total, and changes the execution state. Start a newly created, never-dispatched
session at zero; never zero a resumed session's baseline. Compare in nano-AIU
to `runAiCreditAllowance * 10^9`; do not round per turn or clamp the sum.
On successful completion commit the same peer's cursors and bootstrap in
that transaction. Thus two peers' completions add both deltas, and applying
the same reading again cannot double charge. A crash before commit leaves
`Running` and the old baseline, even if the send or the accounting read
already returned. A crash after commit leaves both progress and spend.

On abort, account a qualified delta **without** committing cursors. On entry
to `Settling`, prohibit further sends and fence every helper in one commit.
On a successful completion crossing the limit in `Open`, that commit includes
the completing peer's cursors; a completion in `Draining` keeps `TimeLimit`.
For other in-flight sessions stop work and obtain a terminal reading while
the original session is still reachable, with a bounded wait; then tear down
the shared root. Do not wait indefinitely on SDK disposal. If the terminal
boundary or the amount for a dispatch is unknowable, record `Unmeasured`,
`Indeterminate` and `FaultedClosed`. An exact meter is never reconstructed
from a transcript, estimates, or a metadata lookup. The distinction between
the SDK surface and native evidence is recorded in [interface evidence](upstream-migration.md#relied-on-external-interface-findings-and-rechecks).

## Stop, verification and cleanup

**Time and signals.** The first activation persists a UTC deadline from
`durationSeconds` for the whole run; reconnect and paused continuation do not
grant another budget. Paused wall time counts. Use elapsed timers to wake the
live controller, rechecking persisted state before admitting more work.
On expiry enter `Draining` with a deadline of at most `drainSeconds`; admit no
new sessions, turns or evaluations. Existing turns and an already-started
evaluation may complete. End the drain when they finish or the drain deadline
is reached. A pending pause or root-close signal can cut the drain short.
Consume the oldest applicable signal in the same commit as its phase change;
on reconnect honor unconsumed signals from the interrupted epoch before
dispatch. The first accepted stop stands in `Settling`, except that unknown
spend or a persistent execution fault forces `FaultedClosed`. A clean,
measured turn timeout aborts to `Paused` in `Open` unless the aggregate limit
was reached; it does not drop work or exhaust a message retry counter.

**Checks.** Execute each stage's configured commands in order, against a
clean detached checkout of the intended immutable commit: candidate `H`,
integration `C`, final the sampled target `F`. Keep generated files from one
stage out of the next. Run trusted commands with their argument vectors,
bounded timeouts and captured output under `verify`; checks do not supply a
replacement commit. Store stage, attempt, input commit/tree, command, status,
actual exit code when available, and local log path. A nonzero exit, timeout,
or launch failure cannot pass; stop that attempt on the first failure. An
empty command list passes that stage without inventing evidence. Never reuse
partial, earlier-head, or other-stage success.

One `verify` **runner** stays alive for the entire admitted evaluation. Before
switching its checkout from `H` to `C`, establish that the previous command and
its descendants have exited, not that the runner itself has exited. If that
cannot be established, abort the evaluation; do not switch trees. The same
runner may execute the remaining stage during `Draining`, within the existing
deadline. Settle the runner and its whole scope before publishing a successful
precommit or final verdict; never launch another `verify` root during drain.

**Final result.** Under the coordinator lock, reconcile outstanding CAS first.
`finalize` on a stopped interrupted epoch settles it as `FaultedClosed`, not
an opportunity to resume the conversation. From `Quiescent` start a final
attempt with `Unverified`; run `finalChecks` against `F`. Recheck `F` under
a prepared Git verify guard and commit the attempt ID, OID and verdict
together. `Passed` requires unchanged `F`, every final command passing,
all scopes stopped, all execution states `Idle`, an exact meter and a
non-faulted stop reason. If `F` moved before publication, return `stale` and
leave `Unverified` for a new attempt on the new tip. A completed failing attempt records `Failed`;
an indeterminate Git/storage/containment operation cannot publish success.
If interrupted before publication, discard the attempt as a source of verdict
and rerun the full final stage after scopes settle. Preserve the epoch's stop
reason. Repeated `finalize` after publication returns the stored verdict
for its OID without rerunning commands; it never certifies a different tip.

**Cleanup.** Require the stored `Passed` or `Failed` result, `Quiescent`, no
predecessor scopes, and exclusive ownership. Record cleanup intent before
removing anything, including the expected ref OIDs in that intent; reject new
run/tool/finalize operations once it is set.
Remove only registered worktrees and run-namespaced refs belonging to this
run, using Git worktree operations and expected-old ref deletes. Unexpected
ownership or a changed final target is an error, not permission to force a
delete. Remove external workspace resources first. Detach the dedicated
state directory from its public name last (same-parent rename after closing
SQLite); reclaim that retired directory only. Key the exclusive command lock
and retired name by the canonical state path, outside the removable directory.
`init` cannot reuse the path while retirement is pending. After interruption,
`cleanup` continues from the ownership record, treating already removed
resources as absent; after rename it checks only that exact retired location,
not neighboring runs. Retain its identity marker until its contents are gone. The
user checkout, repository, unrelated refs and SDK's own session store are not
cleanup targets.
