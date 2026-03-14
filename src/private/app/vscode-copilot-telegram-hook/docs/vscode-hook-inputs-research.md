# VS Code Copilot Hook Inputs Research

## Provenance

- Kind: derived technical research note.
- Derived from:
    - the VS Code reference set preserved in
      [`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md)
    - non-normative repository context reviewed for comparison
- Scope note: H-002 may refine project interpretation, but the external
  research basis for this note is the user-provided VS Code reference set in
  H-001.
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
- `Stop` input,
- what hook input already gives us for correlation,
- what the custom instructions documentation does and does not document,
- what that means for requirement framing and design choices.

## Primary Sources

### Human-authored source inputs

- [`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md)
- [`h-002-human-confirmation-2026-03-13.md`](./h-002-human-confirmation-2026-03-13.md)

### Official VS Code documentation inherited from H-001

- [Agent hooks in Visual Studio Code (Preview)](https://code.visualstudio.com/docs/copilot/customization/hooks)
- [Use custom instructions in VS Code](https://code.visualstudio.com/docs/copilot/customization/custom-instructions)

These VS Code references are part of the user-provided reference set preserved
in [`h-001-original-requirement-brief.md`](./h-001-original-requirement-brief.md).

### Repository context reviewed (non-normative)

- [`../scripts/copilot-summary-state.ps1`](../scripts/copilot-summary-state.ps1)
- [`../scripts/telegram-notify.ps1`](../scripts/telegram-notify.ps1)
- [`../instructions/copilot-notify-summary.instructions.md`](../instructions/copilot-notify-summary.instructions.md)

## Documented Common Hook Input Fields

The VS Code hooks documentation states that **every hook receives a JSON object on standard input** with these common fields:

| Field             | Meaning in the official docs        | Why it matters to this project                                  |
| ----------------- | ----------------------------------- | --------------------------------------------------------------- |
| `timestamp`       | Time when the hook event occurred   | Can be used for logging, ordering, and run correlation          |
| `cwd`             | Current workspace path              | Distinguishes workspaces and gives a stable state root          |
| `sessionId`       | Agent session identifier            | The main documented session-level correlation key               |
| `hookEventName`   | Current hook event name             | Distinguishes `SessionStart`, `Stop`, and others                |
| `transcript_path` | Path to the session transcript JSON | Potentially useful for traceability and turn/session inspection |

### Key observation

For hook-to-hook correlation alone, the documented common input already includes **session-level identity** (`sessionId`) and **workspace identity** (`cwd`).

That means a design that only needs to correlate one hook invocation with another hook invocation may already have enough documented information without inventing an extra identifier first.

## Documented Project-Relevant Event Inputs

### `SessionStart`

The VS Code hooks documentation states that `SessionStart` receives the common fields plus:

| Field    | Meaning                                               |
| -------- | ----------------------------------------------------- |
| `source` | How the session was started; currently always `"new"` |

#### Project relevance

For this project, `SessionStart` does **not** provide much extra correlation data beyond the common fields. The main useful values are still `sessionId`, `cwd`, `timestamp`, and `transcript_path`.

The official hooks documentation also states that `SessionStart` output can inject `additionalContext` into the agent conversation.

That means `SessionStart` is not only a place where a hook can observe the session start; it is also a documented place where a hook can hand information to the model.

### `Stop`

The VS Code hooks documentation states that `Stop` receives the common fields plus:

| Field              | Meaning                                                                   |
| ------------------ | ------------------------------------------------------------------------- |
| `stop_hook_active` | `true` if the agent is already continuing because of a previous stop hook |

#### Project relevance

For this project, `Stop` also gets the same common correlation fields, especially:

- `sessionId`
- `cwd`
- `timestamp`
- `transcript_path`

That means the `Stop` hook already knows which session it belongs to according to the official documentation.

## What the Hook Input Already Gives This Project

Based on the official hook input contract, the hook runtime already has documented access to enough information to do all of the following:

1. identify the current workspace (`cwd`),
2. identify the current Copilot session (`sessionId`),
3. tell which lifecycle event is running (`hookEventName`),
4. record when the event happened (`timestamp`), and
5. reference the transcript (`transcript_path`).

## What the Hook Input Does **Not** Automatically Solve

The harder part for this project is not merely correlating **hook invocation A** with **hook invocation B**.

The harder part is correlating:

1. information available to hook scripts, with
2. a summary later written by the Copilot agent under custom instructions.

### Why this matters

A design for this project may ask the agent to maintain a machine-readable summary during the turn and may ask a later `Stop` hook to read it.

However, the custom instructions documentation describes instructions as Markdown guidance that is automatically included in chat requests. The surveyed documentation does **not** document a built-in mechanism by which an instructions file can directly read hook standard input fields such as:

- `sessionId`
- `timestamp`
- `transcript_path`
- `hookEventName`

### Practical implication

Even though the hook runtime itself already has documented session information, the project still appears to need **some explicit handoff or shared-correlation mechanism** if the agent-written summary must be reliably tied to the same session or tracked result.

## Project-Specific Implications

### Conclusion 1: hook session identity already exists

If the problem were only:

- "identify the current session in the hook runtime", or
- "correlate `SessionStart` and `Stop` hook invocations"

then the documented fields `sessionId` and `cwd` may already be enough.

In that narrower design, a separate session initialization step is **not obviously required** by the official docs.

### Conclusion 2: a handoff mechanism still seems necessary for agent-authored summaries

If the design requires:

- the agent to write a summary file during the turn, and
- the `Stop` hook to send the summary later,

then a handoff problem still exists unless the agent is given a stable correlation value through some documented path.

The official docs do document one such path: `SessionStart` can inject `additionalContext` into the conversation. So the handoff does **not** have to be file-based.

One possible design is to seed shared correlation state at `SessionStart` and reuse it later during summary generation and delivery.

That is a valid design, but it is only **one possible design**, not a fact imposed directly by the hook input contract.

### Conclusion 3: repository-defined correlation identifiers are optional

The official hook input contract documents `sessionId`, not any repository-defined `run_id` field.

So a repository-defined correlation identifier can still be useful, but it should be described as an implementation choice or an internal protocol unless the project explicitly decides to make it part of the product contract.

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

Based on the surveyed documentation, this project has at least three plausible directions:

### Option A — Use `SessionStart` to seed explicit correlation state

Use `SessionStart` to create explicit shared correlation state and let the agent reuse that state later.

#### Pros

- explicit and easy to inspect,
- works even if the agent has no documented direct access to hook stdin fields.

#### Cons

- more moving parts,
- may look like implementation detail when written as a product requirement.

### Option B — Keep the capability requirement, but weaken the implementation wording

Keep a functional requirement about correlation, but avoid saying it must happen specifically by session-state initialization.

#### Pros

- cleaner requirements language,
- still compatible with designs that seed explicit correlation state,
- leaves room for redesign later.

#### Cons

- less concrete unless accompanied by architecture notes.

### Option C — Redesign around documented hook input only

Attempt a design that uses `sessionId`, `cwd`, and `transcript_path` directly and reduces or removes explicit seeded correlation state, potentially combined with `SessionStart` `additionalContext` for hook-to-model handoff.

#### Pros

- closer to the documented hook contract,
- potentially simpler hook-to-hook correlation.

#### Cons

- may still leave the agent-summary handoff problem unsolved,
- would need a different documented way for the agent-written summary to align with the hook runtime.

## Bottom Line

Based on the official docs surveyed here:

1. **Yes**, VS Code hook input already includes enough documented information to identify the current session inside hook scripts.
2. **No**, that does **not automatically prove** that this project can remove all explicit correlation handoff.
3. The remaining uncertainty is not about hook input between hooks; it is about the boundary between **hook runtime data** and **agent-authored summary data**.
4. Therefore, the safe functional requirement is:
    - the system must be able to correlate summary data with the correct session and Telegram notification;
    - the specific `SessionStart` initialization mechanism should not be treated as the only valid product requirement unless the project chooses to standardize it.
