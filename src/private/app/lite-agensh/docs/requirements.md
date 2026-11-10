# LiteAgensh Requirements

These are required behaviors for the initial local product, not claims about
an existing implementation. The [README](../README.md) supplies user context;
[architecture](architecture.md) owns realization and
[validation](validation.md) owns the required evidence.

The migration outcome is a bounded single-repository, trusted single-user
product using Copilot first. Its full upstream treatment is recorded in the
[migration map](upstream-migration.md), including deliberate omissions.

## Runtime and collaboration

### LA-REQ-001: Local single-user host

The product shall operate on one machine with one coordinator per run, local
persistent coordination state and owned native runtimes hosting worker sessions. Its implementation
shall be C#. Initial real-platform acceptance shall target Windows and the
repository-selected .NET toolchain; no other operating-system support is
implied. It shall not require hosted Git/chat/database services or Docker.

### LA-REQ-002: Native agent responsibility

The initial backend shall be Copilot through its official .NET SDK and native
runtime interface. Reasoning, editing, shell tools, model calls and compaction
remain native-agent responsibilities. The host shall verify required
capabilities and applied settings at the documented boundary, and report
unavailable required capabilities rather than patching runtime internals or
silently substituting another backend. ACP compatibility, AHP hosting and a
second model/tool loop are not required.

Each admitted peer retains its own conversation, working directory, local MCP
actor, applied settings, finite credit window and cumulative usage. Reusing an
owned runtime must not merge these identities or serialize all peers behind
one conversation. A session-scoped operation must not dispose another peer's
healthy session; loss of the shared runtime stops its affected cohort.

### LA-REQ-003: Recoverable run identity and state

The product shall retain the exact repository/base, run, worker, branch/worktree,
native-session and owned-process associations, configuration, lifecycle segments
and end reason. Tasks, messages, board revisions, queued delivery, reviews and
check identities shall survive a coordinator restart. An ambiguous surviving
writer shall block replacement until ownership is resolved.

### LA-REQ-004: Peer work selection

Agents shall coordinate through tasks, team/direct/threaded messages, shared
findings and local changes. Preserve peer work selection and advisory expiring
claims: multiple agents may claim a task. Do not turn claims into exclusive
locks or introduce a central intelligent scheduler. When planning is enabled,
wake the initial planner before peers and release peers on plan integration or
the configured planning timeout.

Support an operator-configurable interval between ordinary worker admissions.
An omitted or zero interval preserves simultaneous dispatch eligibility; a
positive interval admits the first worker immediately and subsequent workers
gradually, without preassigning their work. Admitted peers continue cooperating
while later peers wait. Stops remain responsive during ramp-up, and workers
not yet admitted shall not acquire native sessions that require usage settlement.

### LA-REQ-005: Local communication and owner control

The product shall support task creation/update/comment/closure, message
post/read/search/thread access, known-member lookup and operator messages,
review/approval decisions and lifecycle requests. Local identities are
run-scoped; no Gitea/Mattermost account or artificial direct-channel creation
step is required. An owner question need not block all other agents' work.

### LA-REQ-006: Typed shared board

Preserve `FACT`, `OBSERVED`, `FAIL`, `CLAIM` and `PATCH_SUMMARY`, the upstream
deterministic text-admission rules, expiry, replacement/invalidation, boolean
history search and detail expansion. Regular gists have a 100-character cap,
patch summaries 300, claims default to one hour, and the recent view retains at
most 2,000 entries. Persist admitted history separately from the recent view.
Text admission shall not be represented as execution of an agent's reported
checks. These constants come from the pinned
[board source](upstream-migration.md#source-locations).

### LA-REQ-007: Durable serialized delivery

Serialize turns per worker while permitting different workers to progress.
Persist recipient deduplication and pending/sending/sent/failed/retrying state,
acknowledge a batch after its completed turn, and preserve the upstream
five-attempt bound and retry schedule. Interrupted sending batches may return
to pending; recovery does not guarantee exactly-once agent actions. Domain
changes and their required events shall be committed together.

### LA-REQ-008: Notices at real tool boundaries

Direct/urgent messages and fresh board gists shall be available in the results
of local infrastructure MCP operations, using snapshots, notice leases,
recipient cursors and shown-notice reconciliation. Reading a snapshot, showing
a notice and completing a turn are distinct events. This is not an arbitrary
interrupt during a native command or model reasoning.

### LA-REQ-009: Truthful local MCP surface

Map every upstream allowlisted collaboration tool to a local capability or an
explicit removal, as listed in the [complete tool map](upstream-migration.md#collaboration-mcp-map).
Tool names and descriptions shall describe actual local tasks, changes,
checks, members, messages and board operations. Neither upstream tool names
nor wire compatibility is required. Prompts and tool descriptions shall migrate
together; do not claim hosted repository/team discovery that is absent.

## Git, validation and lifecycle

### LA-REQ-010: Preserve the user's checkout

Bind a run to an explicit local base revision. Use a dedicated integration
branch/worktree and separate worker branches/worktrees without changing the
user's original checkout, index, untracked work or global Git configuration.
Keep target-repository instructions; generated cooperation instructions shall
not overwrite a tracked target `AGENTS.md`. Empty-project initialization shall
be explicit rather than an accidental prerequisite for an existing repository.

### LA-REQ-011: Candidate-bound integration

Changes and reviews shall identify the exact candidate commit. The coordinator
shall serialize integration, validate the actual proposed merged tree, and
check that the integration head and reviewed candidate have not moved before
updating the integration branch. A conflict or stale review/check shall preserve
work and return a visible non-success outcome, not force a merge.

### LA-REQ-012: Separate quality and final acceptance

Keep candidate, integration-branch and final-delivery checks distinct. Persist
the declared command, checked commit/tree, result and retained log location.
Preserve peer/protected-path review rules through the local integration path.
Port the upstream broken-main policy accurately: fix-main restriction, team
alert after 15 minutes and restriction lift after 60 minutes. That lift is not
a passing check and never bypasses required candidate/final acceptance.

### LA-REQ-013: Cancellation and recovery

Support run, status, pause, resume, finalize and explicit cleanup operations.
Support a separately bounded, explicitly targeted accounting-only reconciliation
of retained pre-dispatch state without dispatching or reopening native sessions.
It shall not restart or relabel an ended unresolved lifecycle.
Pause/cancellation shall stop further dispatch, request native cancellation and
settle owned processes before reporting a quiescent state. Timeouts or ambiguous
children shall remain visible and block replacement/cleanup, not establish
success. Crash reconnect attempts the exact saved session when supported;
explicit resume starts fresh native sessions with durable project-state
summaries. These paths shall not be conflated.

Normal work-budget expiry shall first stop admission of new sessions, turns
and spending windows. Already-dispatched requests may finish within the
separately bounded deadline drain defined by the
[lifecycle contract](implementation-contracts.md#bounded-deadline-drain),
including their existing acknowledgement and usage verification. Expiry of
that bound requests cancellation and retains existing uncertainty on missing
terminal or accounting evidence. Operator/caller cancellation, ownership or
accounting ambiguity, and other safety failures shall not wait for this grace.
Draining shall not send a finalization prompt or manufacture a completed review.

For an ended failed run whose lifecycle remains unresolved, explicit `reconnect`
shall also support bounded, non-dispatch recovery of associated native usage.
Resolve ownership and verify exact saved sessions before measured settlement;
do not reopen lifecycle segments, create worker loops, replay delivery or send
prompts. Return a distinct settlement-only result and preserve the original
failed outcome, report and preflight/calibration receipts. This branch shall
finish normally without depending on a later paid-admission gate rejection.
It does not authorize resuming work or waive receipts for subsequent paid use.
The [credit contract](implementation-contracts.md#closed-failed-run-reconnect)
owns selection, idempotence and rejection conditions.

When sessions share a runtime, stop dispatch and settle their session operations
before stopping that runtime once. Final accounting and a quiescent outcome
require settlement of the owned runtime, not merely disposal of one session.
Unknown ownership or usage blocks replacement; reuse is not permission to
weaken the existing recovery or accounting guarantees.

Distinguish verified cancellation before first dispatch from provider-measured
reconciliation. The [credit contract](implementation-contracts.md#verified-pre-dispatch-cancellation)
owns the fresh-session proof and failure conditions. Preserve raw usage and
missing completeness; an eligible cancellation shall not fabricate complete
zero billing. Missing proof or ambiguous ownership remains unresolved.

### LA-REQ-014: Budgets, liveness and stop meaning

Support a finite wall-clock budget, operator stop, lifecycle timing/reminders
and bounded liveness recovery. Native connection silence alone shall not establish that a
tool or process is dead. Task closure and budget expiry are stop reasons, not
proof that final checks passed. Do not claim an aggregate token-request cutoff
or exact spend metering without a verified native interface.

Distinguish the work-admission deadline from the fixed, finite deadline-drain
bound and subsequent existing process/accounting settlement. Startup and
waiting consume the work budget; drain never restarts it. Work completed while
draining remains inside the same run/campaign credit allowances and native
limits. Retain the stop reason and whether drain completed or exhausted its
bound; neither outcome alone establishes successful task delivery.

Support an explicitly selected shared AI-credit allocation mode. Workers draw
from one finite run allowance and the same continuing campaign allowance,
not permanent equal per-worker shares. A local spending window may be renewed
only against available shared credit; exhausting that window alone shall not
stop the team while the shared pool can fund further work. Waiting workers
shall not lock fixed shares of the pool. Preserve cancellation, complete
cumulative usage, unknown-usage stops and the native soft-limit evidence
boundary. Existing configurations retain their per-session allocation mode.

Unused authorization from a verified pre-dispatch cancellation may return to
the same finite pool only after session and common-runtime settlement. It shall
not reset cumulative spend, grant an extra allowance or release unrelated
historical unknowns.

### LA-REQ-015: Retained local delivery

Finalization shall retain the local delivery branch, partial work, SQLite state
and native-session associations, and report base/final commits, check outcomes
and unresolved work. A failed final check shall be reported as failed delivery,
not successful acceptance. Cleanup shall be separately requested, explicit and
limited to owned resources after outcome inspection. Retained same-host files
are not a portable sealed backup.

### LA-REQ-016: Trusted-host boundary and attribution

The operator supplies a trusted repository, check commands and existing native
backend configuration. Worktrees are edit separation, not process/network or
credential isolation. Local review gates are cooperative rather than protection
against arbitrary same-user actions. The product shall not provision, copy,
redistribute or rewrite model credentials, override enterprise policy, or
automatically publish local diagnostics. Retain the upstream copyright and MIT
notice for derived material, and separate source findings from runtime evidence.

### LA-REQ-017: Human-editable run configuration

The operator shall be able to initialize a run from YAML configuration and a
separate Markdown goal, with JSON Schema guidance for ordinary VS Code YAML
editing. Existing JSON configurations shall remain compatible. Relative
repository, state, campaign-ledger and goal-file paths in YAML shall resolve
against the configuration file, not the launching terminal. Existing JSON
path semantics shall remain unchanged.

Validate input before creating run state or admitting paid work. Report
malformed, duplicate, unknown, missing or incorrectly typed configuration
fields and missing or empty goal files as configuration failures. Persist the
resolved configuration and goal at initialization; later input-file edits
shall not silently change a running or resumed run. Editor integration shall
be optional and shall not require a custom LiteAgensh VS Code extension.

### LA-REQ-018: Automatic local SQLite contention evidence

When application-owned SQLite access fails with a busy or locked result, retain
bounded local diagnostic evidence automatically, without requiring a later
operator to reproduce the failure. Identify the failed operation, connection,
process lifetime, transaction phase, elapsed time and primary/extended result
codes, together with available recent application-owned transaction activity.
Preserve the original operation failure and distinguish observed activity from
an inferred competing lock holder; missing coverage remains explicit.

Diagnostics shall not depend on another write to the contended database,
include SQL parameters, goals, messages or credentials, or inspect unrelated
host processes. Diagnostic persistence failure shall remain visible without
replacing the original database failure. Retaining evidence does not authorize
automatic retry, recovery, ledger disposition, diagnostic upload or paid work.

## Explicitly unsupported in this outcome

Other agent backends; multi-host coordination; untrusted/mutually hostile
workers; hosted Git/chat administration or arbitrary repository/team discovery;
full chat edits/deletes/reactions; automatic remote push/PR delivery; a model
HTTP gateway, private credential shielding or uniform cross-backend
token/truncation guarantees; portable sealed archives; history-hiding or
held-out/private-evaluator access guarantees; ProgramBench reproduction.

These are deliberate scope boundaries, not promises of equivalent replacements.
Adding one requires an owner scope decision and accepted authorization, not
merely extending an adapter.
