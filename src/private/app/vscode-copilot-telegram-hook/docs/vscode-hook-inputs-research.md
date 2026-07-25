# VS Code Copilot Hook Inputs Research

## Provenance

- Kind: derived technical research note.
- Derived from:
    - the VS Code reference set preserved in
      [`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md)
    - [`h-003-human-confirmation-2026-03-13-addendum.md`](./h-003-human-confirmation-2026-03-13-addendum.md)
    - [`h-005-human-verification-2026-03-14-hook-input-field-names.md`](./h-005-human-verification-2026-03-14-hook-input-field-names.md)
    - [`h-008-human-confirmation-2026-03-16-stop-blocking-summary-flow.md`](./h-008-human-confirmation-2026-03-16-stop-blocking-summary-flow.md)
    - [`h-009-human-confirmation-2026-07-25-root-lifecycle-notifications.md`](./h-009-human-confirmation-2026-07-25-root-lifecycle-notifications.md)
    - the current official VS Code hook overview and hook reference
    - non-normative repository context reviewed for comparison
- Scope note: H-002 and H-003 may refine project interpretation, but the
  external research basis for this note is the user-provided VS Code reference
  set in H-001.
- Purpose: explain what the documented hook inputs do and do not provide for
  this project without treating current repository implementation choices as
  normative.

This note summarizes the parts of the official VS Code documentation that are most relevant to this project's hook design.

Its goal is to answer a practical question for this repository:

> Do the documented hook input fields already contain enough information to correlate a Copilot session and its end-of-turn notification, or does the project still need an explicit correlation handoff mechanism?

This document is intentionally concise and project-focused so that future work does not require rereading the full upstream documentation each time.

## Scope

This research focuses on the hook events and input fields that are directly relevant to this project:

- common hook input fields,
- `SessionStart` input,
- `UserPromptSubmit` input,
- `PreToolUse` input,
- `SubagentStart` and `SubagentStop` lifecycle inputs,
- `Stop` input,
- what hook input already gives us for correlation,
- what the custom instructions documentation does and does not document,
- what that means for requirement framing and design choices.

## Primary Sources

### Human-authored source inputs

- [`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md)

### Official VS Code documentation inherited from H-001

- [Agent hooks in Visual Studio Code (Preview)](https://code.visualstudio.com/docs/agent-customization/hooks)
- [Hooks reference](https://code.visualstudio.com/docs/agents/reference/hooks-reference)

### Later human clarification and verification

- [`h-003-human-confirmation-2026-03-13-addendum.md`](./h-003-human-confirmation-2026-03-13-addendum.md)
- [`h-005-human-verification-2026-03-14-hook-input-field-names.md`](./h-005-human-verification-2026-03-14-hook-input-field-names.md)
- [`h-008-human-confirmation-2026-03-16-stop-blocking-summary-flow.md`](./h-008-human-confirmation-2026-03-16-stop-blocking-summary-flow.md)
- [`h-009-human-confirmation-2026-07-25-root-lifecycle-notifications.md`](./h-009-human-confirmation-2026-07-25-root-lifecycle-notifications.md)

These VS Code references are part of the user-provided reference set preserved
in [`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md).

### Repository context reviewed (non-normative)

- [`../Commands/HookCommandService.cs`](../Commands/HookCommandService.cs)
- [`../State/WorkspaceStateStore.cs`](../State/WorkspaceStateStore.cs)

## Documented Common Hook Input Fields

The VS Code hooks documentation states that **every hook receives a JSON object on standard input** with these common fields:

| Field             | Meaning in the official docs        | Why it matters to this project                                  |
| ----------------- | ----------------------------------- | --------------------------------------------------------------- |
| `timestamp`       | Time when the hook event occurred   | Can be used for logging, ordering, and run correlation          |
| `cwd`             | Current workspace path              | Distinguishes workspaces and gives a stable state root          |
| `session_id`      | Agent session identifier            | The main documented session-level correlation key               |
| `hook_event_name` | Current hook event name             | Distinguishes `SessionStart`, `PreToolUse`, `Stop`, and others  |
| `transcript_path` | Path to the session transcript JSON | Potentially useful for traceability and turn/session inspection |

### Current project implementation note

The current official documentation and the observed runtime recorded in
[`h-005-human-verification-2026-03-14-hook-input-field-names.md`](./h-005-human-verification-2026-03-14-hook-input-field-names.md):
both use `session_id` and `hook_event_name`. The earlier camelCase discrepancy
is no longer present in the current official common-input table.

### Key observation

For hook-to-hook correlation alone, the documented common input already includes
**session-level identity** (`session_id`) and **workspace identity** (`cwd`).

That means a design that only needs to correlate one hook invocation with another hook invocation may already have enough documented information without inventing an extra identifier first.

## Documented Project-Relevant Event Inputs

### `SessionStart`

The VS Code hooks documentation states that `SessionStart` receives the common fields plus:

| Field    | Meaning                                               |
| -------- | ----------------------------------------------------- |
| `source` | How the session was started; currently always `"new"` |

#### Project relevance

For this project, `SessionStart` does **not** provide much extra correlation
data beyond the common fields. The main useful values are still `session_id`,
`cwd`, `timestamp`, and `transcript_path`.

The official hooks documentation also states that `SessionStart` output can inject `additionalContext` into the agent conversation.

That means `SessionStart` is not only a place where a hook can observe the session start; it is also a documented place where a hook can hand information to the model.

### `UserPromptSubmit`

The VS Code hooks documentation states that `UserPromptSubmit` receives the
common fields plus:

| Field    | Meaning                                  |
| -------- | ---------------------------------------- |
| `prompt` | The text the user submitted for the turn |

#### Project relevance

For this project, `UserPromptSubmit` is the documented hook event that fires
for each user prompt and therefore marks the start of a new chat turn.

That makes it the most natural documented place to advance any internal
turn-scoped state such as a repository-defined `turn_id`.

### `PreToolUse`

The current hook reference states that `PreToolUse` receives:

| Field         | Meaning                                  |
| ------------- | ---------------------------------------- |
| `tool_name`   | Name of the tool about to be invoked     |
| `tool_input`  | Structured input that will be sent to it |
| `tool_use_id` | Identifier for that individual tool call |

#### Project relevance

Observed Copilot events identify the user-input tool as `ask_user` and place the
user-facing question in `tool_input.message`. Therefore, a root `PreToolUse`
hook can notify at the moment Copilot requires an answer without parsing the
unstable transcript format. The durable duplicate-suppression key can be
derived from `tool_use_id`.

### `SubagentStart` and `SubagentStop`

The current hook reference documents dedicated subagent lifecycle events with
`agent_id` and `agent_type`; `SubagentStop` also receives
`stop_hook_active`. The general hook overview also states that hooks operate
across local, background, and cloud agent types.

#### Project relevance

Subagent completion is now a distinct documented lifecycle concept and is not a
root user-notification opportunity. Runtime evidence supplied with H-009 also
shows nested sessions whose `session_id` is a tool-call identifier. The
implementation therefore suppresses those observed tool-call session
identifiers on every managed notification hook, including `Stop` and
`PreToolUse`.

### `Stop`

The VS Code hooks documentation states that `Stop` receives the common fields plus:

| Field              | Meaning                                                                   |
| ------------------ | ------------------------------------------------------------------------- |
| `stop_hook_active` | `true` if the agent is already continuing because of a previous stop hook |

#### Project relevance

For this project, `Stop` also gets the same common correlation fields, especially:

- `session_id`
- `cwd`
- `timestamp`
- `transcript_path`

That means the `Stop` hook already knows which session it belongs to according
to the official documentation. The project must still decide whether that
session is the root user-facing session before notifying.

The official hooks documentation also states that `Stop` can return
`hookSpecificOutput` with:

- `hookEventName: "Stop"`,
- `decision: "block"`, and
- a required `reason`.

The same documentation states that `stop_hook_active` becomes `true` when the
agent is already continuing because of a previous stop hook.

### Practical consequence for this project

This documented `Stop` behavior provides a direct hook-to-model feedback path
that can be used to validate a summary file at stop time, tell the model what
is wrong, and require regeneration without relying on a separately installed
custom-instruction artifact.

## What the Hook Input Already Gives This Project

Based on the official hook input contract, the hook runtime already has documented access to enough information to do all of the following:

1. identify the current workspace (`cwd`),
2. identify the current Copilot session (`session_id`),
3. observe when a new user prompt starts a turn (`UserPromptSubmit`),
4. tell which lifecycle event is running (`hook_event_name`),
5. record when the event happened (`timestamp`), and
6. reference the transcript (`transcript_path`),
7. identify a pending tool invocation (`tool_name` and `tool_use_id`), and
8. distinguish explicit subagent lifecycle events (`agent_id` and `agent_type`).

## Locality and execution-model clarification

The official hooks documentation explicitly says that hooks are designed to
work across **local agents**, **background agents**, and **cloud agents**. The
same document also states that OS-specific hook commands are selected based on
the **extension host platform**, which may differ from the user's local
operating system in remote development scenarios.

### Practical meaning for this project

The documented workspace and user hook file locations are local configuration
locations, but the upstream documentation does not define hooks as a purely
local execution model.

Therefore, the safer project interpretation is:

- the current supported product target can still remain user-level installation
  for the user's Windows and WSL Linux setups; but
- broader remote or non-local execution semantics should be treated as upstream
  platform constraints, not as proof that the product must widen its formal
  support scope before design begins; and
- user-level notification hooks must explicitly suppress nested or tool-call
  sessions because hook execution is not limited to the root agent.

This reframes the earlier concern from an unresolved pre-design requirement gap
into a platform constraint that later design work should tolerate where
practical.

## What the Hook Input Does **Not** Automatically Solve

The harder part for this project is not merely correlating **hook invocation A** with **hook invocation B**.

The harder part is deciding what to do when the current turn's summary file is
missing or invalid when the agent tries to stop.

### Why this matters

The summary still needs correlation data. The documented `Stop` blocking path is
a platform capability, but the current default design supersedes H-008's
blocking direction: pending handoffs may defer notification while unresolved,
and the `Stop` hook sends a non-blocking degraded fallback only when no pending
handoff can satisfy the `Stop`.

### Practical implication

Even though the hook runtime itself already has documented session information,
the design still needs a summary handoff protocol and validation rules. What
changes is that the project no longer needs a separate always-on instruction
layer to keep the summary file up to date throughout the whole conversation.

## Project-Specific Implications

### Conclusion 1: hook session identity already exists

If the problem were only:

- "identify the current session in the hook runtime", or
- "correlate `SessionStart` and `Stop` hook invocations"

then the documented fields `session_id` and `cwd` may already be enough.

In that narrower design, a separate session initialization step is **not obviously required** by the official docs.

### Conclusion 2: non-blocking `Stop` fallback replaces default blocking recovery

If the design requires:

- the agent to write a summary file for the current turn, and
- the `Stop` hook to decide what to do when that file is missing or invalid,

then the official `Stop` output contract provides enough lifecycle context to
validate the handoff and send a degraded fallback notification without blocking
the agent.

This means the project can move summary handoff validation into the `Stop` hook
itself instead of relying on a separately installed instruction file to keep the
summary synchronized throughout the whole conversation. Blocking regeneration is
not the current default behavior; it is only a possible future strict/debug mode.

### Conclusion 3: repository-defined correlation identifiers are optional

The official hook input contract documents `session_id`, not any
repository-defined `turn_id` field.

So a repository-defined turn identifier can still be useful, but it should be described as an implementation choice or an internal protocol unless the project explicitly decides to make it part of the product contract.

### Conclusion 4: user attention can be observed without transcript parsing

The documented `PreToolUse` fields, together with the observed `ask_user` tool
shape, provide a direct attention trigger. This is preferable to inferring a
waiting state from a later `Stop` event.

### Conclusion 5: root filtering is a product requirement

Dedicated subagent lifecycle events clarify the platform model, but user hooks
can execute for nested agent activity. The notification implementation must
therefore reject identified nested tool-call sessions before creating state or
sending Telegram messages.

## What This Means for the Functional Requirements

## Recommended requirement framing

The functional requirement should describe the **goal**, not the current implementation tactic.

A better functional requirement is closer to this:

> The solution shall provide a reliable way to correlate a turn's summary with the correct Copilot session and the correct Telegram notification within a workspace.

That is a functional requirement because it describes a capability the system must have.

### What should probably **not** be treated as a top-level functional requirement

The following is probably too implementation-specific to stand alone as a top-level functional requirement:

> The solution shall initialize session state at `SessionStart`.

That wording describes one implementation strategy, not the underlying business need.

### Better classification

A design that writes or seeds correlation state at `SessionStart` is better understood as one of these:

- an implementation decision,
- a derived system design requirement, or
- an internal correlation protocol.

## Project Design Options After This Research

Based on the surveyed documentation and the later supersession of H-008 default
blocking, this project has at least two plausible directions. The current
default direction is non-blocking degraded fallback.

### Option A — Superseded for default: use `Stop` blocking as summary recovery

Use the documented `Stop` hook blocking output to validate the current turn's
summary file, explain validation failures, and require regeneration before the
agent is allowed to finish.

This option is superseded for default behavior. It may only be revisited as a
future strict/debug mode.

#### Pros

- directly supported by the official `Stop` output contract,
- summary instructions are delivered exactly when needed,
- avoids relying on long-lived instruction persistence across long chats.

#### Cons

- requires bounded retry logic to avoid indefinite continuation,
- still needs a concrete summary-file schema and validator.

### Option B — Current default: use assignments plus non-blocking Stop fallback

Use hook-emitted notification assignments and explicit runtime state to
correlate summaries. At `Stop`, validate the assigned summary if present; send a
normal notification when valid. Pending assigned summaries may defer without a
degraded fallback; send degraded fallback only when no pending handoff can
satisfy the `Stop`.

#### Pros

- explicit and easy to inspect,
- works even if the agent has no documented direct access to hook stdin fields.

#### Cons

- more moving parts,
- requires duplicate suppression and durable delivery coordination.

This is the current default product direction.

## Bottom Line

Based on the official docs surveyed here:

1. **Yes**, VS Code hook input already includes enough documented information to identify the current session inside hook scripts.
2. **Yes**, the official `Stop` output contract provides a documented way to block stopping, but default notification behavior in this project must not use it for missing or invalid summaries.
3. **Yes**, `PreToolUse` supplies the tool name, structured input, and a stable
   tool-call identifier needed for an `ask_user` attention notification.
4. **Yes**, current VS Code documents explicit subagent lifecycle events, and
   project runtime evidence demonstrates why root-session filtering is still
   required.
5. Therefore, the current preferred project direction is:

- suppress identified subagent and tool-call sessions before any notification
  state is created;
- maintain per-turn summary state in workspace runtime files;
- validate that file at `Stop` time;
- send a normal notification only for a root `Stop` when the summary is valid;
- send a durable, duplicate-suppressed attention notification for root
  `ask_user` tool calls;
- defer when a pending handoff may still satisfy the `Stop`, otherwise send a
  non-blocking degraded fallback notification when the summary is missing or
  invalid;
- reserve `decision: "block"` only for future strict/debug recovery scope, not default behavior.
