# Implementation Contracts

This record completes the concrete boundaries required before implementation
under the accepted [architecture](architecture.md) and
[requirements](requirements.md). Implementers consume these contracts when
working independently; the owner maintains them when integration changes.
The capability/source inventory is not a second API contract.

## Application and dependencies

Use one `LiteAgensh.csproj` executable under the project root, namespace
`LiteAgensh`, target `$(CurrentTargetFramework)`. Use root SDK/CPM/NBGV rules.
Select `Microsoft.Data.Sqlite` 10.0.14 and `ModelContextProtocol` 2.3.1 through
CPM; retain existing `Microsoft.Extensions.Hosting` selection. Select
`GitHub.Copilot.SDK` 1.0.28. Its packaged `CopilotCliVersion` default remains
1.0.105; explicitly override that property to 1.0.107 in this application and
record the selected value in its existing assembly metadata. Reuse the
installed CLI 1.0.107 and require exact typed runtime-status equality with that
selected value; do not accept a minimum version, range or arbitrary newer patch.
This application selection does not change or repackage the SDK. Set
`CopilotSkipCliDownload=true` and retain disabled automatic acquisition/update.
Its normal transitive AI abstractions do not
authorize another provider or reasoning layer. Do not copy SDK source or
implement an application HTTP server. The official MCP package supplies stdio framing,
initialization, tool discovery and invocation. Use its low-level tool handlers
with explicit input schemas so operation payloads and notice envelopes remain
application-owned.

Use the official Copilot SDK's typed session, event, permission, cancellation,
metadata and usage APIs. Replace the handwritten ACP client and configurable
JSON-pointer credit descriptor; do not maintain two native adapters.
Experimental SDK APIs receive narrow adapter-scoped acknowledgement, not
global analyzer suppression. The pinned source and released-package evidence
are in the [migration map](upstream-migration.md#native-interface-selection).

Tests use the root `MSTest.Sdk` and a project reference from
`tests/private/app/lite-agensh/LiteAgensh.Tests.csproj`. Existing traversal and
CI selection remain authoritative. No independent toolchain is introduced.

## Configuration input

`init --config` selects YAML for `.yaml` and `.yml` files, with
case-insensitive extension matching. Other files retain the existing JSON
loader; `.json` is its documented filename convention. The input file is an
initialization source only; subsequent lifecycle commands use the persisted
run configuration.

- JSON retains the existing camel-case `RunConfiguration` fields, required
  inline `goal`, strict unknown-field rejection and existing absolute
  repository/state/ledger requirements. Do not reinterpret its paths.
- YAML accepts the same configuration fields except that required
  `goalFile` replaces `goal`. Read that Markdown file as the complete goal;
  reject a missing or whitespace-only path or body. Reject inline `goal`
  rather than inventing precedence between two goal authorities.
- Resolve relative YAML `repositoryPath`, `statePath`,
  `campaignLedgerPath` and `goalFile` against the absolute input file's
  directory. Preserve absolute paths. Do not resolve executable names,
  arguments, protected-path patterns or `baseRevision` as input-file paths.
- Use the root CPM selection of `YamlDotNet`. Accept one YAML mapping
  document, reject duplicate keys, unknown fields and wrong value types,
  then apply existing `RunConfiguration.Validate()` invariants before any
  run SQLite state is created. Do not introduce a configuration-provider
  framework.
- Materialize the loaded Markdown body as `RunConfiguration.Goal` and
  persist the resolved existing configuration shape. Do not add a
  goal-file dependency to run recovery or reread it on resume/reconnect.
  Correcting an input after initialization does not silently reconfigure
  that run.
- Publish `schemas/run.schema.json` for the YAML input. Cover accepted
  field names, required fields, types, descriptions, enum choices,
  meaningful numeric bounds and strict additional-property rejection.
  Keep it consistent with the typed input contract and supply an example
  YAML/Markdown pair with a normal `yaml-language-server` schema
  association. Editor structure checks do not replace runtime filesystem
  or cross-field validation. A consumer copying the example elsewhere
  adjusts its schema reference and paths; no custom VS Code extension
  or downloaded schema is required to run LiteAgensh.

Input errors produce the existing non-success CLI operation envelope before
models start. Existing budget, ownership, permission, preflight and billing
calibration gates remain unchanged by the input format.

`creditAllocation` accepts `per-session` or `shared` in JSON and YAML, defaulting
to `per-session` for existing inputs and retained runs. Persist it with the
resolved configuration and receipt identity. Reject other values. Shared mode
changes how the finite allowances are allocated, not their magnitude or the
native soft-limit semantics; it has no independent per-worker budget field.

## Worker admission ramp-up

`workerStartIntervalSeconds` is a nonnegative integer in both JSON and YAML,
defaulting to zero for existing configurations and persisted runs. It is
persisted with the resolved configuration and participates in the existing
preflight/calibration identity. Schema guidance and examples use the same name.

- Zero preserves immediate eligibility without an artificial interval.
  Per-session credit allocation retains the legacy all-session startup path.
  Shared allocation admits incrementally: ready peers run while later
  admissions may wait for backing, rather than blocking all loops behind
  a whole-team session-start barrier.
- With a positive interval, admit the first ordinary worker without an
  artificial delay. Start its native session, queue its kickoff and run its
  loop while the coordinator continues handling controls and peer work.
  Start each following native worker no sooner than the configured interval
  after the preceding admission completes. Do not catch up missed intervals
  by bursting several admissions.
- In per-session mode, retain the existing fair allocation policy. In shared
  mode, a waiting worker may have a durable preassigned identity but holds no
  spending grant until actual admission. Neither mode starts a waiting
  worker's native session, process lease or model dispatch. Release only
  reservations known to remain unstarted; do not infer free usage for a
  native session that was started.
- Deadline, cancellation, control requests and admitted-worker failures remain
  effective during ramp-up. The finite segment deadline is not extended by
  startup or waiting. Stops can leave fewer than the configured workers
  admitted; report the actual count rather than asserting full-team execution.
- With planning enabled, admit the planner first, begin its planning window
  when its session is ready, and admit later peers only after existing plan
  integration or planning-timeout release, while respecting the start interval.
- Apply the interval anew to each lifecycle segment after existing recovery
  ownership and accounting checks. Do not silently replace an unavailable exact
  reconnect identity or change explicit fresh-summary resume semantics.

This is a configurable constant interval, not the upstream experiment's full
two-stage deployment schedule. The [paper, Appendix C][agensh-paper], uses
30 seconds during its first hour and 3 seconds thereafter to reduce scope
contention; a local 32-peer run can select 30 without introducing another
deployment service. This source rationale does not establish a measured local
quality or cost improvement.

[agensh-paper]: https://arxiv.org/html/2609.26781v1

## Shared platform boundary

The following public application contracts permit independent units without a
generic plugin framework. Additional internal types do not change this API.

- `RunConfiguration` contains absolute repository/state directory, goal,
  explicit base revision, worker count, finite duration, optional planning
  interval, native executable/model/options, candidate/integration/final check
  commands, peer-review/protected-path settings, native permission policy and
  run AI-credit allowance. Worker IDs are `worker1` through `workerN`; the
  operator ID is `owner`.
- `CheckCommand` contains executable, argument list and finite timeout. Never
  concatenate executable/arguments into an implicit shell. A shell command is
  represented explicitly as the declared shell and arguments.
- `RunDatabase` opens a local SQLite database below the state directory,
  enables WAL/foreign keys and finite busy timeouts, validates schema version,
  and offers `Open()` returning a fresh connection. Persist configuration in
  the database. Domain owners create their tables transactionally; a higher
  incompatible schema is rejected without recreating it.
- `ProcessRunner.RunAsync` runs a declared executable/argument list in an
  explicit cwd with redirected output, a finite deadline and caller token.
  Return exit/stdout/stderr/timeout classification. Cancellation settles owned
  children before returning; do not kill processes by name.
- `OperationResult` is represented as JSON with `ok`, stable `code`, `message`
  and `data`. Expected failures return a non-success result; cancellation
  remains cancellation. Invalid JSON/fields, missing identity, not-found,
  conflict/stale-candidate, review/check failure and unavailable capability
  must be distinguishable. Numeric IDs/cursors are local monotonically
  increasing integers; timestamps are UTC. No tool parameter changes the
  helper's bound actor or run.

## Local operations and storage ownership

`CollaborationOperations` owns task/message/board/event/notice tables and
provides `Invoke(actor, name, arguments)` returning an operation result. It also
provides recipient event claim/complete/fail/recovery and shared-context reads
for the coordinator. Mutations insert their required events in the same
transaction. Persist board history and recent-view state separately.

`GitOperations` owns change/review/check/integration-request tables. It exposes
`InvokeAsync(actor, name, arguments, token)`, initialization of run/worker
worktrees, worker-path lookup, processing of pending integration requests,
integration health evaluation and final delivery/check evaluation. The
coordinator is the sole executor of Git integration and checks.

`Coordinator` owns lifecycle/worker/session/process/credit/control tables and
serialized worker loops. CLI/MCP calls enqueue lifecycle/integration requests;
only the coordinator records their completed outcomes. One lifetime-held
exclusive ownership handle protects the run; resolve surviving children before
recovery. Shared operation dispatch routes to collaboration/Git owners rather
than duplicating business rules in CLI and MCP handlers.

The internal native dependency has a worker contract for session/reservation
identity, diagnostics, start, prompt, credit verification, stop and disposal,
plus a factory for worker creation and settled-session usage recovery.
Production uses the existing real `NativeWorker`/SDK `ForUri` implementation.
Keep the public coordinator constructor unchanged; an internal constructor may
accept a factory builder supplied with the actual run database, process runner
and credit ledger. Association validation and ledger reconciliation remain
coordinator responsibilities. Recovery returns usage only after settlement;
an unsettled outcome cannot release a reservation.

Supplying that factory automatically selects controlled dependency evidence,
including separate controlled preflight/calibration receipt keys and evidence
classification in results/bindings. Native SDK admission rejects controlled
receipts. There is no user-selectable backend, bypass of real credit admission,
alternate SDK transport or generic extension framework. The
[architecture](architecture.md#controlled-native-dependency-in-scenarios) owns
the current scenario rationale and boundary.

Use ordinary SQLite rows, with JSON payload columns where structured bodies
need them, not mutable flat-file coordination. SQL is parameterized. Core board
matching/admission and retry/scheduling policies are translated from the pinned
source, including limits, expiry/invalidation and source backoff. The source
notice accompanies derived policy implementation.

## Tool input and result contracts

All tools use the bound actor/run. Optional read limits default to a bounded
page; return a cursor when more data exists. Reject nonpositive/out-of-range
limits and malformed IDs. Query text is literal except the board's explicitly
supported source boolean grammar.

| Name                                               | Input and operation                                                                                                   | Data on success                                                                                  |
| -------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| `run_info`                                         | No arguments                                                                                                          | Goal, base, lifecycle, local members/configured paths and supported capabilities; no credentials |
| `agent_info`, `agent_list`                         | Optional known ID/name filter                                                                                         | Known member(s), not service accounts                                                            |
| `task_write`                                       | `action`: create/update/comment/close; create title/body, other actions task `id`; updates explicit supported fields  | Task identity/body/state/comments; root task closure records actor                               |
| `task_read`, `task_list`                           | Read `id`; list state/cursor/limit                                                                                    | Task(s) and advisory claim-derived assignees                                                     |
| `work_search`                                      | `query`, optional cursor/limit                                                                                        | Matching local tasks/changes with kind retained                                                  |
| `message_send`                                     | Text and team/direct recipient, optional reply/thread/mentions                                                        | Message ID, channel/thread and time                                                              |
| `message_read`, `message_thread`, `message_search` | Channel/cursor, thread ID, or query; optional limit                                                                   | Visible messages and continuation cursor                                                         |
| `channel_read`, `channel_list`                     | Optional local ID/name                                                                                                | Local metadata/channels visible to actor                                                         |
| `board_publish`                                    | Type, gist content, optional detail/work label and claim TTL                                                          | Admitted revision ID or deterministic admission failure                                          |
| `board_recent`                                     | Optional limit                                                                                                        | Recent live view, not all history                                                                |
| `board_search`                                     | Source boolean/literal query, optional cursor/limit                                                                   | Matching history revisions                                                                       |
| `board_expand`                                     | Source detail labels                                                                                                  | Detail expansion and unknown/truncated label information                                         |
| `change_write`                                     | `action`: submit/update/comment/integrate; branch/head/title/task IDs as applicable; existing change `id` for updates | Captured candidate/change or queued integration request ID, never a fabricated completed merge   |
| `change_read`, `change_list`                       | `id`, or state/cursor/limit                                                                                           | Candidate, actual diff, comments/reviews/checks and state                                        |
| `change_review`                                    | Change ID, exact head, approve/request-changes/comment, body                                                          | Candidate-bound review; author cannot satisfy own peer approval                                  |
| `check_read`                                       | Check ID or change ID                                                                                                 | Exact checked commit/tree, command, status and retained log reference                            |

Malformed payloads do not partially commit mutations. Workers can read run/team
history and their direct messages, not another worker's direct messages through
ordinary tools. The operator can inspect all local state. Same-user database
access remains outside tamper-resistant security claims.

### Concrete operation payloads

The table above summarizes capabilities; the field contracts below govern their
JSON boundary. Object keys use the exact camel-case spellings shown here.
An unmarked field is required; `?` means optional, not nullable. IDs/cursors are
positive JSON integers, except a starting cursor of zero. Member IDs, channel
IDs, Git refs/hashes and board labels are strings. Text is a string; required
title/body/query/content fields must not be empty. Reject unknown action names,
unknown fields and invalid enum/type combinations before committing. An omitted
array defaults to empty on creation. A partial update preserves every omitted
field, including arrays; only an explicit empty array clears existing links.
Pagination uses `cursor?: integer = 0` and
`limit?: integer = 50`, with limits 1 through 200; ascending ID pages return
`{items: [...], nextCursor: integer|null}`. No filter means all visible matching
states, not an implicit state transition.

| Tool/action              | Exact request fields                                                                                                                     | Successful `data`                                                                                      |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| `run_info`               | `{}`                                                                                                                                     | `{goal, baseCommit, lifecycle, members, repositoryPath, statePath, capabilities}`                      |
| `agent_info`             | `id: string`                                                                                                                             | `{id, worktreePath, lifecycle}`                                                                        |
| `agent_list`             | `id?: string`                                                                                                                            | `{items: [{id, worktreePath, lifecycle}]}`                                                             |
| `task_write/create`      | `action: "create", title: string, body: string`                                                                                          | Task object                                                                                            |
| `task_write/update`      | `action: "update", id: integer, title?: string, body?: string`; at least one updated field                                               | Task object                                                                                            |
| `task_write/comment`     | `action: "comment", id: integer, body: string`                                                                                           | Task object including appended comment                                                                 |
| `task_write/close`       | `action: "close", id: integer`                                                                                                           | Task object with closing actor; repeated closure is unchanged success                                  |
| `task_read`              | `id: integer`                                                                                                                            | Task object                                                                                            |
| `task_list`              | `state?: "open"\|"closed"`, pagination                                                                                                   | Paginated tasks                                                                                        |
| `work_search`            | `query: string`, pagination                                                                                                              | Paginated `{kind: "task"\|"change", id, title, state}`                                                 |
| `message_send`           | `recipient: string, body: string, threadId?: integer, mentions?: string[], urgent?: boolean = false`                                     | Message object                                                                                         |
| `message_read`           | `channelId: string`, pagination                                                                                                          | Paginated messages                                                                                     |
| `message_thread`         | `threadId: integer`, pagination                                                                                                          | Paginated messages in the visible root thread, including its root                                      |
| `message_search`         | `query: string`, pagination                                                                                                              | Paginated visible messages                                                                             |
| `channel_read`           | `id: string`                                                                                                                             | `{id, members: string[], kind: "team"\|"direct"}`                                                      |
| `channel_list`           | `{}`                                                                                                                                     | `{items: [channel objects]}`                                                                           |
| `board_publish`          | `type: "FACT"\|"OBSERVED"\|"FAIL"\|"CLAIM"\|"PATCH_SUMMARY", content: string, detail?: string, workLabel?: string, ttlSeconds?: integer` | `{id, label, type, content, detail, workLabel, expiresAt, createdAt}`                                  |
| `board_recent`           | `limit?: integer = 50`                                                                                                                   | `{items: [board revision objects]}`                                                                    |
| `board_search`           | `query: string`, pagination                                                                                                              | Paginated board revisions                                                                              |
| `board_expand`           | `labels: string[]`                                                                                                                       | `{items: [board revision objects], unknownLabels: string[], truncated: boolean}`                       |
| `change_write/submit`    | `action: "submit", title: string, body: string, taskIds?: integer[]`                                                                     | Change object, capturing the bound worker branch's current head                                        |
| `change_write/update`    | `action: "update", id: integer, title?: string, body?: string, taskIds?: integer[], head?: string`; at least one updated field           | Change object; a new head must be the current bound worker branch head and invalidates prior approvals |
| `change_write/comment`   | `action: "comment", id: integer, body: string`                                                                                           | Change object including appended comment                                                               |
| `change_write/integrate` | `action: "integrate", id: integer, head: string`                                                                                         | `{requestId, changeId, head, state: "pending"}`                                                        |
| `change_read`            | `id: integer`                                                                                                                            | Change object plus `diff: string` and `integrationRequests`                                            |
| `change_list`            | `state?: "open"\|"integrated"`, pagination                                                                                               | Paginated change objects                                                                               |
| `change_review`          | `id: integer, head: string, decision: "approve"\|"request-changes"\|"comment", body: string`                                             | `{id, changeId, actor, head, decision, body, createdAt}`                                               |
| `check_read`             | Exactly one of `id: integer` or `changeId: integer`                                                                                      | `{items: [check objects]}`                                                                             |

A task object contains `{id, title, body, state, author, closedBy: string|null,
createdAt, updatedAt, comments: [{id, actor, body, createdAt}],
assignees: string[]}`. Assignees are derived from live advisory claims; task
writes cannot create an exclusive assignee. A change object contains
`{id, author, branch, head, title, body, taskIds: integer[], state, createdAt,
updatedAt, comments: [{id, actor, body, createdAt}], reviews: [review objects],
checks: [check objects]}`. A check object contains
`{id, changeId: integer|null, kind: "candidate"|"integration"|"final", commit,
tree, command, status, exitCode: integer|null, logPath, createdAt}`; command
uses the `CheckCommand` shape, and status is `passed`, `failed`, `cancelled`
or `timed-out`. All times are ISO-8601 UTC strings; absent optional stored
values are explicitly null in results. Revision labels and admission/TTL/
detail-expansion semantics remain the pinned board policy, not new text rules.

`change_read.integrationRequests` retains
`[{requestId, changeId, actor, head, state, resultCode: string|null,
message: string|null, integratedCommit: string|null, createdAt, updatedAt}]`.
Request states are `pending`, `running`, `succeeded` and `failed`. Terminal
results include their success/failure code and explanation even when no check
could start, for example a conflict or stale head. Only a successful integration
has an `integratedCommit`. Reads therefore distinguish pending work from failed
work without adding a separate operation or equating a queued request to a merge.

`message_send.recipient` is either `"team"` or one existing member ID, never a
caller-supplied arbitrary channel. The team channel ID is `"team"`. A direct
channel ID is `"dm:"` followed by the two member IDs in ordinal order separated
by `":"`; it needs no separate creation operation. A new message without
`threadId` becomes its own thread root. A supplied `threadId` must name a
visible root in the derived destination channel; otherwise reject the send.
Mentions must be known members visible in that channel and do not widen
visibility. A message object contains
`{id, actor, channelId, recipient, body, threadId, mentions: string[], urgent,
createdAt}`. Replies and roots share the channel's visibility. Requests for an
invisible channel/thread fail without returning its messages.

Only a change's author may update it or submit that worker's branch; reviewers
can comment and record their own candidate-bound decision. All referenced tasks
must exist. Integration requests require the exact recorded head and are
deduplicated while pending. The operator uses its explicit approval/control
path rather than pretending to own a worker branch. `board_publish.ttlSeconds`
is accepted only for claims and must be positive; omission uses the source
default. `board_expand` accepts source detail labels, not arbitrary paths.
Expected error codes are `invalid_arguments`, `unknown_actor`, `not_found`,
`not_visible`, `conflict`, `stale_candidate`, `review_required`, `check_failed`,
and `unavailable`; unhandled defects are not silently converted to success.

Each collaboration tool result can include a separate `notices` envelope.
Acquire a finite recipient lease and snapshot, render eligible messages/gists,
record shown notices/cursors only for that envelope, and reconcile queued
events with displayed notices. Reading an ordinary history page does not
silently acknowledge an unrelated batch. Full turn acknowledgement still
requires native turn completion.

## Git and lifecycle contracts

Capture exact base/head/tree identities, use dedicated run/worker refs and
worktrees, and preserve the original checkout. Construct and check the merged
candidate before advancing integration; revalidate heads and review identity.
Persist conflicts/failures and logs without losing worker commits. Peer
review and protected-path owner approval are candidate-bound. HealthGate's
15/60-minute source policy does not bypass candidate/final checks.

The CLI supports `init`, `run`, `status`, `pause`, `resume`, `reconnect`,
`finalize`, `cleanup`, local operator tool invocation and an internal `mcp`
mode. Configuration uses the [explicit input contract](#configuration-input);
status/delivery are machine-readable.
Help documents actual syntax. Init creates a root task and identities without
starting models. Optional planning wakes worker1 before peers. Closure by the
operator and configured fixed-length behavior are classified separately from
successful final checks.

Native startup launches the installed executable with documented headless
hosting and no-auto-update settings. Register the actual process before using
its loopback readiness endpoint; use a process-local connection token and
`RuntimeConnection.ForUri` rather than an SDK-spawned process whose PID the
application cannot durably identify. Never bind a non-loopback listener or log
the connection token. SDK connection disposal does not settle this externally
owned process; the host must do that explicitly.

The production native-worker factory owns one runtime and one SDK client for
an operation's admitted peers. Initialize them once even if session creation
overlaps. A session adapter owns only its session, callbacks and turn state;
disposing it must not close that shared client. The factory's explicit
operation-settlement boundary disposes the client and owned runtime after
the workers stop. A later operation obtains a new host only after the previous
one is settled. Do not silently fall back to one CLI per worker.

Create sessions with an explicit unique `SessionId`, `WorkingDirectory`,
local `McpServers`, `ExcludedTools` including native `task`, and finite
`SessionLimits.MaxAiCredits`. Leave default model/authentication selection
unchanged unless the operator's product configuration explicitly selects a
supported model; the closed development acceptance used the owner's existing
default. The separately accepted user-audit protocol owns its model selection.
Read `session.Rpc.Metadata.SnapshotAsync()` and verify the actual session,
working directory, explicitly requested model and applied limit before dispatch.
An unavailable requested model is not permission to fall back. Missing/mismatched
metadata or an unexpected limit change stops the affected path.
Permission callbacks honor the explicit trusted-run policy for known request
kinds; unknown actions or enforced native policy are not silently overridden.

Persist the session-to-runtime association alongside the existing session and
process identities before dispatch. Multiple sessions may name the same exact
runtime lease, but retain distinct worker/reservation/session bindings.
Credit-ledger SDK-owner rows continue identifying the owning coordinator,
not separate backend processes. Keep session IDs on all typed RPC operations:
metadata, options, UI renewal, usage and abort. One session's event or permission
callback must not be applied to another session.

Cold peers still have no native session or grant. A running backend for an
admitted peer is not evidence that every configured worker has been admitted.
The host process uses an explicit owned startup directory; peer tool execution
uses the separately configured and verified session working directory.

On crash, settle/resolve known processes before resuming the exact saved session.
Set `ContinuePendingWork=false` and `AllowTranscriptRecovery=false`; read back
identity, working directory, limits and cumulative usage before another send.
Do not silently recover a damaged transcript or continue pending native actions.
Explicit resume always creates fresh sessions with a durable summary. Queue
recovery may replay work; do not promise exactly-once actions.
Pause/finalize retain state; explicit cleanup never removes unrelated refs,
worktrees, user checkout, credentials or global config.

### Native prompt lifecycle

A new native session receives the shared goal, cooperation/lifecycle guidance,
bounded durable task/board state and change summary with its first event batch.
Avoid duplicating the goal and cooperation text inside that bootstrap's
shared-context prefix; the root task remains a real task, not a second goal
authority.

After a completed bootstrap turn, persist confirmation keyed by exact native
session identity before batch acknowledgement. Further turns in that same
session carry pending events and the canonical fresh unread board prefix.
Do not repeat static instructions, goal, members or unchanged full task/change
snapshots; peers can use the existing read tools for current state. Preserve
event ordering, full backlog access, direct/urgent visibility, notice
deduplication and post-turn acknowledgement. Do not suppress CLAIM events or
change idle/wake scheduling as part of this context reduction.

Exact reconnect uses the saved session-bound confirmation, not the first
iteration of a new worker loop. A fresh explicit-resume or load-fallback session
receives a new durable bootstrap. If bootstrap completion was not confirmed,
it may replay with interrupted work, including for older retained sessions
without that marker. A failed or canceled send cannot create confirmation.
This does not change pending-work/transcript-recovery flags, reservations,
model selection, native compaction ownership or failure classification.

## Credit contract and evidence limits

Use SDK `SessionLimitsConfig.MaxAiCredits`, not the incompatible ACP/usage-file
flag combination. It is a native soft limit on the current accounting window,
not a hard prepaid ceiling. Per-session mode never automatically adds, unsets
or increases a limit in response to exhaustion. Shared mode may renew a finite
window only as specified below, without increasing the run/campaign allowances.
Never unset a paid session's cap to keep it working. Report actual overshoot.

Persist campaign/run allowances and session reservations before paid dispatch.
Bind each reservation to the exact run, worker and persistent session in one
transaction. Fresh sessions consume new reservations. Exact continuation
retains previously charged usage and permits no more additional capacity than
the remaining backed allowance. Express the native cap in its accounting-window
coordinate, not as that remaining difference; never reset cumulative usage.
Per-session mode retains its original lifetime allocation.

### Shared spending windows

The campaign ledger remains the single accounting authority. Persist allocation
mode and grant state with exact run/worker/session associations; retain
idempotent cumulative reports and reject incompatible state before dispatch.
Keep confirmed spend distinct from outstanding authorization. Within each
immediate SQLite transaction, require confirmed spend plus backed outstanding
grants not to exceed either finite allowance before approving another grant.
Actual in-flight soft-cap overshoot is retained even when it crosses a bound.

For ordinary shared workers, grant at most 100 credits from the available
pool and require at least 30 for a new grant. Grant only when a worker is
actually admitted or needs a turn/renewal, not up front for all configured
workers. Calibration keeps its separate once-only 30-credit native soft cap
and its spend counts against the same finite allowances.

Before a new paid send, acquire backing, apply a finite current-window cap
through `session.Rpc.Options.UpdateAsync`, and verify metadata and the
unchanged cumulative baseline. During the supported exhausted-limit flow,
record complete cumulative usage, obtain additional backing and use
`session.Rpc.Ui.HandlePendingSessionLimitsExhaustedAsync` with an explicit
finite `Set` response. Verify the applied cap and retained baseline; never
use `Unset` or an unbacked `Add`. Unsupported or inconsistent behavior fails
closed rather than silently reverting to fixed shares or replacing sessions.

Establish the native accounting-window origin on creation and preserve it on
exact resume. Subtract that origin when translating a cumulative authorized
ceiling; a newly read or booked cumulative total is not itself a new origin.
Changing a cap does not authorize resetting that baseline. A completed SDK user turn may close
its grant only after known native background compaction has settled and a fresh,
complete, nonregressing cumulative reading is established as specified below.
A cap still present in an idle native session does not authorize another send:
the host must obtain and verify a newly backed window first. A failed or
canceled send cannot release backing merely because its local await ended.

Preserve grant ownership through cancellation and crash settlement. An exact
reconnect may acquire another backed window after previous usage and ownership
are established; it never restores a spent original allocation. Fresh-summary
resume uses fresh session identities and the same remaining shared pool.
Preserve overshoot observations when closing or renewing a grant.

If enough actual credit remains but is committed to in-flight calls, wait
without dispatching until backing is available or existing cancellation,
deadline, unknown-usage or exhaustion conditions apply. Do not equate
temporarily unavailable backing with spent credit.

The typed mutable limit, metadata, complete-usage and exhausted-limit APIs are
available in the pinned SDK package. The public
[session-limits guide][sdk-session-limits] describes per-session current-window
soft caps, post-response enforcement and exhausted-limit events; it does not
provide a native pool across independent sessions. Typed/public source
availability is not installed-runtime verification. The applicable operational
protocol owns that observation.

[sdk-session-limits]: https://github.com/github/copilot-sdk/blob/99d90edf504f824c77dcdf0faa978f4b35848a87/docs/features/session-limits.md

### Usage evidence and settlement

`session.Rpc.Usage.GetMetricsAsync()` supplies `TotalNanoAiu` and
`AiCreditsStatus`. Interpret credits as reported nano-AIU divided by
1,000,000,000. Require finite nonnegative totals and `Complete` before treating
a subtotal as final or releasing backing for a dispatched session. `Partial`, `Unavailable` or an
absent status cannot establish zero cost. Retain the native subtotal and its
status separately from the conservative reserved charge.

`Complete` covers the amounts for observed calls; it does not assert that a
background compaction call has finished. Observe the exact session's compaction
start/terminal events without replacing native compaction. Retain backing while
that work is pending. After its terminal notification has been processed,
request fresh typed metadata and cumulative metrics under a session-local
settlement fence. A work-state change during the read invalidates it. A
notification, a local idle flag, stable repeated numbers or a fixed delay alone
cannot establish complete final usage. Missing, failed or contradictory
settlement evidence must not release backing or finalize an earlier reading.
Use bounded waits governed by the existing operation/cancellation and settlement
deadlines; timeout leaves uncertainty rather than inventing a final subtotal.

Live `assistant.usage` events can provide prompt stop signals but are ephemeral.
Durable usage checkpoints and cumulative snapshots support recovery; a numeric
checkpoint alone is not completeness proof. Charge cumulative deltas from a
persisted known baseline, transactionally and idempotently; never sum events,
checkpoints and cumulative snapshots as independent charges. Reject regressing
or unassociated usage. Premium-request `cost`, token counts, context occupancy,
model-price estimates and account quota are not substitutes.

A newly created, never-prompted session need not report completed-call billing.
Non-model preflight establishes applied limits and identity, not measured free
usage. The [native protocol](native-acceptance-protocol.md) separately bounds
the initial tool-disabled billing calibration, which must obtain complete
native usage before ordinary work. An outstanding unknown reservation still
blocks all new paid work: this interface change neither releases it nor
authorizes conservative carry-forward without repository-owner disposition.
The owner's explicitly authorized one-time audit ledger transition follows
the [user-audit protocol](user-audit-protocol.md#continuing-accounting-and-finite-effects);
it preserves historical unknowns rather than reconciling or releasing them.
It is not a general exception for uncertainty within the active campaign.

Cancel through each session's `AbortAsync`, settle known background compaction,
then collect fresh complete metrics when available. Fence its handlers and
close that session without permanent deletion. Preserve its backing while the
shared operation is still settling. If work changes after a reading is staged,
refresh it while the owned session remains readable or retain uncertainty;
closing the common host cannot repair a stale reading.
After all worker stop attempts, close the shared client and settle the
host-owned runtime once, even when a peer stop failed. Only then finalize
the staged complete readings; do not release reservations early because one
session was disposed. A failed barrier leaves affected reservations uncertain
and blocks replacement. Retain all stop failures rather than hiding them
behind an otherwise successful host exit.

Keep the existing special case for an exact-load failure that opened no
session and dispatched nothing, with an unchanged persisted complete baseline.
That case may settle its old reservation without tearing down healthy peers.
Non-dispatch usage recovery uses a separately bounded temporary runtime and
returns its reading only after that runtime settles.

A changed cumulative value outside an open shared window remains a live
admission failure. The existing exact-session non-dispatch recovery may instead
book a settled final increase for an uncertain, dispatched reservation only
when all of these conditions hold:

- The run is exclusively owned and old owned process identities are resolved.
  Reservation/run/worker/session and saved SDK associations match; exact load
  keeps pending-work continuation and transcript recovery disabled.
- Actual session identity, working directory, requested model and applicable
  finite limit are verified. Known background work settles before the fresh
  complete cumulative reading; callbacks, session and temporary runtime then
  settle without invalidating it.
- The final reading is finite, nonnegative and nonregressing in both cumulative
  spend and reported requests. It does not revise an already reconciled total.
  Partial, unavailable, unassociated or ambiguous evidence remains unresolved.
- The ledger receives evidence from that completed recovery boundary, not a
  caller's finalization flag alone. Preserve the raw reading and atomically
  record the cumulative delta once, including any soft-cap overshoot.
- No new grant, dispatch, allowance, baseline reset or cancellation is created.
  Other unknown reservations and all paid-admission/preflight/calibration
  gates remain effective. Repeating an established disposition is idempotent.

This is the existing reconnect recovery's measured-usage rule, not a broader
meaning for the offline `reconcile` cancellation command. It adds no public
command, permits no manual ledger edit and does not waive build or receipt
identity checks for subsequent paid work.

### SDK-native recovery boundary

For accounting-only recovery of a dispatched uncertain reservation, original
routine native termination may establish the predecessor's known-work boundary
without successful delivery of its original SDK send. This is the owner's
native-reported accounting evidence level, not a certificate that every
historical cancelled request returned or every provider invoice is final.
An earlier cancelled compaction can retain `attemptSettled=false` at its
cancellation decision; the separately verified later native termination is
the accepted predecessor boundary. Keep that failure and the original fence
unchanged. A newly idle instance, abandoned pending work, process exit,
stable amounts or `Complete` alone cannot replace this provenance.
Live turn/compaction fencing and the unopened exact-load failure exception
above retain their existing semantics.

Before activating the saved session, use the temporary owned client's official
`Rpc.Sessions.ReadPersistedEventsAsync` for that exact local session and storage
context. Bind the original lifetime to its durable session/start, segment and
stop associations; do not choose a global latest session or accept a later
recovery shutdown as the original endpoint. Read one pinned journal snapshot
sequentially to `CursorStatus=ok` and `HasMore=false`, using each continuation
cursor once. Require its matching original `SessionShutdownEvent` with
`ShutdownType=routine`, finite nonnegative public `TotalNanoAiu` and valid
public accounting watermarks. Preserve the original event identity/time,
amount and frontier as recovery provenance. Watermarks subsume preceding
receipts, not future or unobserved calls.

Bound the entire history read by the existing native-probe deadline, not a
new deadline for each page; consume one snapshot at a time. A short page is
not completion. Expired, unavailable, unsupported, incomplete or contradictory
history leaves uncertainty. Do not fall back to raw CLI files, hidden snapshot
properties, reflection, another journal generation or session replacement.
The public shutdown amount is a consistency lower bound, never a saved amount
to book. The SDK's internal `AccountingSnapshot` is not a production interface.

Then exact-load with pending continuation and transcript recovery disabled,
retain all identity/model/SDK/runtime/finite-cap/origin/backing checks, and read
fresh supported aggregate metrics under the existing session-local fence.
Require `Complete`, finite nonnegative cumulative spend and valid requests,
nonregression against the associated persisted spend/request baseline, and
spend at least covering the original public shutdown amount in the same native
coordinate. Check typed local `Metadata.IsProcessingAsync` and
`Metadata.ActivityAsync` around the usage read: processing or active work
invalidates it. These are new-boundary work checks, not predecessor financial
certificates; nonlocal or unsupported results cannot prove inactivity.
Known new background work, callbacks, session and temporary host must settle
without invalidating the fresh reading before the existing atomic delta
booking. No new grant, prompt, baseline reset or original-fence clearing occurs.

These primitives are documented in the selected SDK 1.0.28's
[public generated RPC contract][sdk-recovery-rpc] and
[event contract][sdk-recovery-events]: persisted snapshot paging, current-local
activity, observed-call completeness and public shutdown accounting projections.
The public source ref is distinct from the package's runtime build stamp;
source/package correspondence is not installed CLI effectiveness.
Recheck the selected package and CLI interface before dependent native use.
The owning protocol and validation gates still require separate implementation
acceptance and an exact prospective operation; this contract is not a successful
recovery or permission to book the retained diagnostic snapshots.

[sdk-recovery-rpc]: https://github.com/github/copilot-sdk/blob/fa108e71ee3025663e0dac20f2a071055d3a6b14/dotnet/src/Generated/Rpc.cs
[sdk-recovery-events]: https://github.com/github/copilot-sdk/blob/fa108e71ee3025663e0dac20f2a071055d3a6b14/dotnet/src/Generated/SessionEvents.cs

### Exact recovery limit coordinates

For exact non-dispatch usage recovery, distinguish the existing cumulative
authorized ceiling `A`, booked cumulative actual `S`, remaining backed capacity
`H = A - S`, and native accounting-window origin `B` in the same cumulative
coordinate. Require finite positive `H` before opening a session. The absolute
native ceiling representing that authorization is `A - B`, not `H`. Apply the
same supported finite value to resume configuration and limit readback. This
translation creates no grant and does not change `A`, `S` or either baseline.

The bounded retained-state profile supports a fresh session with established
zero origin and an unchanged, previously verified native ceiling equal to `A`.
Reuse that ceiling; do not substitute raw `A` merely because backing remains.
Establish compatibility from the unique fresh reservation/session/attempt
associations, zero starting association baseline, and matching original typed
zero-use/zero-request observation in the write-ahead dispatch-contract start
receipt. Bind its run, reservation, session, working directory, model, SDK and
runtime lineage to the retained session evidence. Require the latest verified
applied limit to equal the existing authorized ceiling and reject conflicting
or intervening origin/reset evidence. A numeric zero association baseline or
an SDK version alone does not establish this profile.

The start observation establishes this application's coordinate provenance,
not provider-final zero billing or a cancellation disposition. Retain its raw
usage status. Do not reuse the offline cancellation eligibility rules to
release a dispatched reservation. A missing, ambiguous, nonzero-origin or
incompatible receipt, including a lagging applied-limit observation, leaves
recovery unavailable; do not synthesize a receipt, renew backing, reset a
window or guess a higher limit from a native error. A supported ceiling may
still be rejected by the installed runtime, in which case retain uncertainty.

For example, `A = 300`, `S = 200` and established `B = 0` leave `H = 100`,
but the unchanged verified absolute ceiling is `300`, not a new 300-credit
grant. Rounded already-used diagnostics do not establish complete usage or
authorize another ceiling. This profile does not claim that every SDK
accounting window has zero origin, that another retained lineage is supported,
or that an installed-native recovery has succeeded. Those claims retain their
separate evidence and operational invocation gates.

#### Retained CLI 1.0.105 to selected CLI 1.0.107

In addition to an original session matching the selected runtime, admit only
the explicit ordered recovery bridge from original CLI 1.0.105 to selected
CLI 1.0.107 with the unchanged SDK 1.0.28 package. This is not a generic
cross-version, SDK-upgrade or session-migration mechanism. Preserve the
original start, latest session, runtime and lease evidence unchanged; their
original runtime versions must still agree. All unique associations, raw
zero-origin proof, working directory, model, exact SDK identity, original
runtime/lease bindings, latest applied ceiling `A` and positive backing `H`
remain required.

The temporary recovery host must report the application's exactly selected
1.0.107 runtime. Its live session must retain the original session identity,
working directory, model and exact SDK identity, and read back the unchanged
absolute ceiling. Compare its runtime version with the selected current pin,
not the historical receipt's version. Do not rewrite that receipt, either
baseline or the original cap to make the versions match.

Reject any other ordered version pair, a changed SDK identity, conflicting
original lineage, unavailable settings or incomplete/regressing live metrics.
Require the same fresh usage fence and session/host settlement before booking
the measured cumulative delta. Neither the owner's version selection nor a
controlled bridge scenario proves installed-native session loadability,
preserved accounting-window behavior or complete billing. Native rejection
retains uncertainty; no new prompt, grant, fallback or automatic retry follows.

### Closed failed-run reconnect

The existing `reconnect --state <owned-state>` entrypoint has a distinct
settlement-only branch for a run with at least one lifecycle segment, all
segments ended and retained lifecycle `unresolved`. Select it under the run's
exclusive ownership handle before the unfinished-crash-segment gate. No other
closed lifecycle becomes eligible, and an unfinished segment keeps the normal
crash reconnect path. Do not edit lifecycle or segment state to select a path.

Resolve old owned process identities and validate durable run/campaign,
reservation/worker/session and SDK associations. Recover each unresolved
associated reservation through the existing exact-session non-dispatch
factory boundary and measured-usage rules above, including the
[SDK-native predecessor boundary](#sdk-native-recovery-boundary) for dispatched
uncertainty. Use the actual durable
uncertain reservation, disabled pending-work continuation and transcript
recovery, isolated tools, positive remaining backing and the verified finite
native ceiling under the [coordinate contract](#exact-recovery-limit-coordinates). Failed exact
load has no fresh-session fallback in this branch. Missing association,
unsupported cap, live/ambiguous ownership or incomplete or regressing evidence
retains uncertainty; no forced release or fabricated cancellation is allowed.

Return a distinct settlement-only result after all selected recovery attempts
and their owned temporary runtimes settle. A successful result means this
run's retained reservations have supported accounting dispositions, not that
the original failed run succeeded. If another reservation cannot settle,
report failure and retain its uncertainty; already completed atomic
dispositions are not rolled back or charged again. Other campaign unknowns
remain untouched and still block paid use.

Do not reserve another allowance, acquire a shared window, start a worker
loop, send a prompt, replay delivery, process Git checks, export a report,
create/reopen/end a segment or change the retained lifecycle/failure records.
Keep original preflight/calibration receipts unchanged. Return normally from
this branch without invoking paid admission or requiring new paid receipts:
this is an explicitly selected non-dispatch operation, not a way to exploit
an expected later gate failure. Subsequent paid operations retain every
ordinary admission and exact build/configuration receipt requirement.

Return the operation's own outcome and remaining run/campaign uncertainty
separately from the retained failed-run outcome. A repeat with no unresolved
run reservations is a successful no-op without native session creation,
new reports or repeated charges. Record new native observations and ledger
dispositions through the existing evidence carriers, never by rewriting the
original failure. This adds no CLI option, incident command or recovery
framework and does not change offline pre-dispatch `reconcile`.

Local task cancellation or silence does not settle an operation.
If dispatched usage, ownership or limit applicability is unknown, retain the
reservation and stop paid work. Scripted metrics are not native billing proof.

### Verified pre-dispatch cancellation

A fresh opened session may have no completed-call billing because it was never
dispatched. Permit a distinct cancellation of its remaining authorization only
with all proof below. This is an accepted application-path evidence level under
the trusted same-user boundary, not provider-final zero cost or tamper resistance.
Hashes preserve captured evidence; they do not prove that nobody edited it
before capture. An incomplete native reading alone never permits cancellation.

- The reservation, run, worker, fresh session, SDK usage row, native attempt,
  observed metadata and owned runtime associations match uniquely. Reject reused
  or exact-resume sessions, missing associations and unsupported source lineage.
- Establish the applicable dispatch contract: intent is durably committed before
  every SDK send and never reset. Current intent and maximum reported requests
  must both be zero. A defaulted counter or SDK version alone is not provenance.
  Retained compatible source/build evidence may establish legacy lineage;
  unknown compatibility remains uncertain.
- Preserve matching raw numeric zero usage and zero native requests, with
  observed identity, locality, cwd, model and finite limit validation. Missing
  numeric data, nonfinite or nonzero totals, positive requests, `Partial`,
  `Unavailable` or contradictory readings reject cancellation. Absent status
  remains absent; it is not rewritten to `Complete`.
- Fence admission and quiesce session turns and handlers, close the session and
  settle its owned common runtime. A stop or runtime failure rejects the affected
  disposition. Disposal, silence, timestamps and a recorded exit flag alone do
  not establish this. Retained recovery additionally checks the exact old
  coordinator-owner/runtime identities; a live or uninspectable identity fails.

Apply the disposition inside an immediate campaign-ledger transaction that
rechecks the matching current reservation, intent, requests and raw usage.
Atomically record its distinct cancellation reason and proof provenance,
including source contract, associations, raw readings and settlement evidence,
and close its remaining authorization. Preserve measured actual usage as
unestablished; do not insert a synthetic zero credit report or mark it reconciled.
The resulting terminal state cannot authorize another send. Repetition is
idempotent only for the same consistent disposition; racing or contradictory
state fails closed. Unrelated reservations, allowances and cumulative reports
remain unchanged. Keep existing unstarted-release and unopened exact-load
complete-baseline cases separate.

Expose only the targeted accounting-only `reconcile --state <owned-state>
--reservation <id>` operation for retained cases. Hold exclusive run ownership,
verify its unchanged campaign binding and source lineage, and check live state
rather than replacing it with a backup. When legacy state lacks a provenance
marker, `--evidence <owned-proof>` supplies retained lineage and evidence
references, not trusted success booleans or a budget reset. Independently check
its identity, compatible source and referenced observations against owned state.
Return the cancellation classification, raw usage/status and remaining
uncertainty separately from measured reconciliation. Do not open native sessions,
query accounts, dispatch, continue pending work, run checks, consume delivery
events or alter the old run's lifecycle, segments, leases or report.

This path does not infer free usage for all empty sessions or release arbitrary
historical unknowns. An owning operational protocol must authorize the retained
target. Actual sends retain the existing complete-usage guards. A successful
cancellation settles local authorization only; it is not native billing
effectiveness, a completed audit or permission to bypass future preflight.
