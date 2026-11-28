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
effect_scopes['runtime'].state = 'Active' AND
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

## SQLite schema refinement

One `run.sqlite` database projects the four-plane abstract state without
duplicate stream/entity storage:

1. **Epoch, hierarchical scopes, and sum-type meter (`EpochLifecycle.tla`):**
    - `meta(key PRIMARY KEY, value)` — `schema_version`, `run_id`, `base_commit`,
      `configuration_json`, sum-type `meter` (`>= 0` exact nano-AIU sum, or
      `'-1'` for `Indeterminate`), and `final_verdict` (`'Unverified' | 'Passed'
| 'Failed'`).
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
    - `session_meters(session_id PRIMARY KEY, peer, cumulative_credits, exec_state)` —
      `exec_state IN ('Idle', 'Running', 'Unmeasured')` (`peerExec`); settling
      an epoch into `Quiescent` with any non-`'Idle'` row sets `meta['meter'] =
'-1'` (`Indeterminate`) and `stop_reason = 'FaultedClosed'`.
    - `control_signals(id PRIMARY KEY, reason, created_at, consumed_at)`.
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
    - `proposals(id PRIMARY KEY, author, branch, head_oid, title, body, status, requested, last_outcome, updated_at)` —
      `status IN ('open', 'committing', 'integrated')`, `requested IN (0, 1)`.
    - `proposal_reviews(id PRIMARY KEY, proposal_id, reviewer, head_oid, decision, body, created_at)`.
    - `verification_runs(id PRIMARY KEY, proposal_id, stage, commit_oid, tree_oid, command_json, status, exit_code, log_path, created_at)`.

## Minimal native MCP tool catalog

Tools execute under immutable launch-bound `(run, actor, launchChannel)`. Every
peer MCP mutation and observation executes inside an `IMMEDIATE` transaction
requiring `peers.turn_snap >= 0 AND peers.chan_token == launchChannel`,
rejecting stale or unbound helpers without mutating rows or cursors
(`CausalStream.tla` / `AgenshOrganization.tla`). Controller-handled aborts and
meter faults immediately set `turn_snap = -1, inflight_imm = imm_cursor` and
either advance `chan_token = chan_token + 1` or negate `chan_token =
-ABS(chan_token)`; after an abrupt coordinator crash (`SIGKILL`), surviving
orphan helpers stop with their OS `effect_scopes`, and `UnbindChannels` clears
ephemeral cursors with zero writes to `(base_cursor, imm_cursor)` before
`BindOrRecoverChannel` advances `ABS(chan_token) + 1`. Valid active calls
piggyback visible `immediate = 1` rows in `(inflight_imm..MAX(seq)]` and advance
`inflight_imm` atomically iff that visible slice is non-empty.

| Tool              | Operations / parameters                                                                                                                                         | Result payload                                                                                 |
| ----------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `org_status`      | `{}`                                                                                                                                                            | `{goal, baseCommit, targetRef, phase, members, activeClaims}`                                  |
| `task_mutate`     | `action: "create"\|"update"\|"comment"\|"close"`, `id?`, `title?`, `body?` (`close` on root goal task signals `GoalConverged` candidate)                        | Task object `{id, title, body, state, author, closedBy, comments, claimants}`                  |
| `task_query`      | `id?: integer, state?: "open"\|"closed", query?: string, cursor?: integer, limit?: integer`                                                                     | Single task or `{items, nextCursor}`                                                           |
| `message_post`    | `to: "team"\|<peerId>, body: string, replyTo?: integer, urgent?: boolean` (appends `causal_log` with `immediate = 1` iff `urgent && to!=team`)                  | `{seq, author, to, threadSeq, urgent, createdAt}`                                              |
| `message_query`   | `channel?: "team"\|<peerId>, threadSeq?: integer, query?: string, cursor?: integer, limit?: integer` (visibility-scoped over `causal_log`)                      | `{items, nextCursor}`                                                                          |
| `context_publish` | `type: "FACT"\|"OBSERVED"\|"FAIL"\|"CLAIM"\|"PATCH_SUMMARY", summary: string, detail?: string, workLabel?: string, ttlSeconds?: integer, replacesSeq?: integer` | `{seq, author, type, summary, workLabel, expiresAt}` (appends `causal_log` with `immediate=1`) |
| `context_query`   | `mode: "recent"\|"search"\|"expand", query?: string, seqs?: integer[], cursor?: integer, limit?: integer` (queries `causal_log` directly)                       | `{items, nextCursor}`                                                                          |
| `proposal_mutate` | `action: "open"\|"update"\|"comment"\|"review"\|"request_integrate"`, `id?`, `title?`, `body?`, `head?: string`, `decision?: string`                            | Proposal or review object (`request_integrate` sets `requested = 1` for any peer integrator)   |
| `proposal_query`  | `id?: integer, status?: "open"\|"committing"\|"integrated", cursor?: integer, limit?: integer`                                                                  | Proposal detail (including `diff`, `reviews`, `verificationRuns`, `lastOutcome`) or page       |
