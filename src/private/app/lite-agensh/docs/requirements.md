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
persistent coordination state and native worker processes. Its implementation
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
Pause/cancellation shall stop further dispatch, request native cancellation and
settle owned processes before reporting a quiescent state. Timeouts or ambiguous
children shall remain visible and block replacement/cleanup, not establish
success. Crash reconnect attempts the exact saved session when supported;
explicit resume starts fresh native sessions with durable project-state
summaries. These paths shall not be conflated.

### LA-REQ-014: Budgets, liveness and stop meaning

Support a finite wall-clock budget, operator stop, lifecycle timing/reminders
and bounded liveness recovery. Native connection silence alone shall not establish that a
tool or process is dead. Task closure and budget expiry are stop reasons, not
proof that final checks passed. Do not claim an aggregate token-request cutoff
or exact spend metering without a verified native interface.

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
