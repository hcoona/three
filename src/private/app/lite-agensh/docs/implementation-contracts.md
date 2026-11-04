# Implementation Contracts

This record completes the concrete boundaries required before implementation
under the accepted [architecture](architecture.md) and
[requirements](requirements.md). Implementers consume these contracts when
working independently; the owner maintains them when integration changes.
The capability/source inventory is not a second API contract.

## Application and dependencies

Use one `LiteAgensh.csproj` executable under the project root, namespace
`LiteAgensh`, target `$(CurrentTargetFramework)`. Use root SDK/CPM/NBGV rules.
Select `Microsoft.Data.Sqlite` 10.0.14 and `ModelContextProtocol` 2.3.0 through
CPM; retain existing `Microsoft.Extensions.Hosting` selection. Select
`GitHub.Copilot.SDK` 1.0.28, whose packaged runtime pin is CLI 1.0.105.
Set `CopilotSkipCliDownload=true` and reuse the installed executable without
automatic acquisition/update. Its normal transitive AI abstractions do not
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

Create sessions with an explicit unique `SessionId`, `WorkingDirectory`,
local `McpServers`, `ExcludedTools` including native `task`, and finite
`SessionLimits.MaxAiCredits`. Leave default model/authentication selection
unchanged unless the operator's product configuration explicitly selects a
supported model; acceptance uses the owner's existing default.
Read `session.Rpc.Metadata.SnapshotAsync()` and verify the actual session,
working directory and applied limit before dispatch. Missing/mismatched
metadata or an unexpected limit change stops the affected path.
Permission callbacks honor the explicit trusted-run policy for known request
kinds; unknown actions or enforced native policy are not silently overridden.

On crash, settle/resolve known processes before resuming the exact saved session.
Set `ContinuePendingWork=false` and `AllowTranscriptRecovery=false`; read back
identity, working directory, limits and cumulative usage before another send.
Do not silently recover a damaged transcript or continue pending native actions.
Explicit resume always creates fresh sessions with a durable summary. Queue
recovery may replay work; do not promise exactly-once actions.
Pause/finalize retain state; explicit cleanup never removes unrelated refs,
worktrees, user checkout, credentials or global config.

## Credit contract and evidence limits

Use SDK `SessionLimitsConfig.MaxAiCredits`, not the incompatible ACP/usage-file
flag combination. It is a native soft limit on the current accounting window,
not a hard prepaid ceiling. Never automatically add, unset or increase a limit
in response to exhaustion. Request cancellation and report actual overshoot.

Persist campaign/run allowances and session reservations before paid dispatch.
Bind each reservation to the exact run, worker and persistent session in one
transaction. Fresh sessions consume new reservations. Exact continuation
retains previously charged usage and applies no more than the remaining
allowance as the native window cap; never reset to the original allocation.

`session.Rpc.Usage.GetMetricsAsync()` supplies `TotalNanoAiu` and
`AiCreditsStatus`. Interpret credits as reported nano-AIU divided by
1,000,000,000. Require finite nonnegative totals and `Complete` before treating
a subtotal as final or releasing a reservation. `Partial`, `Unavailable` or an
absent status cannot establish zero cost. Retain the native subtotal and its
status separately from the conservative reserved charge.

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

Cancel through `AbortAsync`, collect complete metrics when available, quiesce
handlers, dispose sessions/client without permanent deletion, and settle the
host-owned runtime. Local task cancellation or silence does not settle it.
If final usage, ownership or limit applicability is unknown, retain the
reservation and stop paid work. Scripted metrics are not native billing proof.
