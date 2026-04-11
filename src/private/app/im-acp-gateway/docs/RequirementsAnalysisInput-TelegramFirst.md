# Formal Requirements Analysis Input — Telegram-First IM-ACP Gateway

## Provenance

- Kind: formal requirements analysis input document.
- Purpose: organize discovery outputs, scope boundaries, open questions, and
  checklist prompts for the formal requirements analysis stage.
- Status: active input for the next project stage.
- Primary source documents:
    - `src/private/app/im-acp-gateway/docs/Research.md`
    - `src/private/app/im-acp-gateway/docs/Research-Telegram.md`
    - `src/private/app/im-acp-gateway/docs/EarlyResearchVerification-Telegram.md`
    - `src/private/app/im-acp-gateway/docs/TelegramBotVerificationLog.md`

This document is intentionally positioned between discovery and design.

It is **not**:

- the final requirements specification,
- an architecture decision record,
- an implementation plan,
- or a decision log that settles the open product questions.

Its job is to help the requirements phase start from a disciplined, traceable,
and scope-aware input set.

## How to read this document

This document uses four different kinds of material. They should not be mixed:

### 1. Discovery evidence

These are points already supported by research or verification runs.

### 2. Working scope assumptions

These are temporary boundaries or planning assumptions for the requirements
phase. They help focus the discussion, but they are not automatically permanent
product commitments.

### 3. Requirement analysis prompts

These are areas that the formal requirements phase must examine and write down
clearly.

### 4. Open decision questions

These are questions that should stay unanswered in this document until the
formal requirements phase resolves them explicitly.

## Stage boundary and current working scope

The project is entering formal requirements analysis for the following narrowed
near-term slice:

- Telegram-first,
- direct-chat-only,
- WeChat deferred,
- GitHub Copilot CLI via ACP as the current backend under investigation,
- personal-use-oriented initial scope.

This stage transition applies to the **Telegram-first MVP slice only**.

It does **not** mean that the broader original WeChat-inclusive product vision
has already completed all discovery requirements.

## Why entering formal requirements analysis is now reasonable

For the narrowed Telegram-first scope, the remaining unknowns are now mostly
about expected product behavior and boundary choices rather than about basic
platform feasibility.

The Telegram discovery track already provides practical evidence for the core
questions that were holding the project in early research:

- Telegram private-chat transport works on the intended setup,
- inbound and outbound text messaging works,
- reply metadata is usable for session continuation,
- inline-button callback handling works,
- one thin Telegram-to-Copilot-ACP bridge worked end to end.

The main unresolved areas are now things that belong in requirements analysis:

- exact session rules,
- command surface,
- approval behavior,
- cancellation behavior,
- progress-delivery expectations,
- deployment expectations,
- recovery expectations.

## Discovery evidence that can be treated as established input

For the Telegram-first slice, the following points are already supported well
enough to serve as requirements-analysis input.

### Telegram-side evidence

- a Telegram bot can receive inbound private-chat messages,
- a Telegram bot can send outbound text messages,
- a Telegram reply to a bot-authored message exposes usable reply metadata,
- inline buttons and callback queries work in practice,
- message updates and chat-action style feedback are available.

### Thin end-to-end evidence

- a Telegram message can be routed into Copilot CLI through ACP,
- a Copilot response can be returned to Telegram,
- a user can reply to a bridge-generated bot message and continue the same
  logical conversation shape,
- the bridge can preserve at least minimal session continuity state.

### Discovery caveats that still matter

These are **not** blockers to entering requirements analysis, but they should
remain visible:

- permission mediation over Telegram is still thin,
- cancellation proof is not yet strong for a deliberately long-running turn,
- webhook deployment behavior is not yet the validated primary path,
- Telegram groups are still deferred rather than validated.

## Working scope assumptions for the requirements phase

Use the following assumptions to focus the requirements work unless that phase
changes them explicitly.

### Product and user context assumptions

- one primary operator is enough for the first version,
- Telegram private chat is the only in-scope conversation topology,
- WeChat is deferred rather than rejected permanently,
- the first version should stay text-first.

### Operational assumptions

- one Telegram bot is enough for the first version,
- one gateway deployment operated by the same person is a reasonable first
  planning assumption,
- the current backend under investigation is Copilot CLI via ACP,
- the current development path may start with locally managed state.

These points are working assumptions for analysis, not permanent commitments.

## Product goal statement to refine during requirements analysis

The working product goal is:

> enable a user to operate GitHub Copilot CLI from a Telegram private chat while
> keeping session continuity understandable, controllable, and durable.

This goal still needs to be refined into explicit requirements, workflows,
constraints, and acceptance criteria.

## Requirement analysis areas to cover

The formal requirements phase should address at least the following areas.

## 1. User and scope definition

- who the intended operator is,
- who is explicitly out of scope,
- whether there is exactly one user or a single trusted operator model,
- what direct-chat-only means in user-facing behavior,
- what WeChat deferred means operationally and documentation-wise.

## 2. Session behavior

- how a new session starts,
- how the active session is identified,
- how a reply continues a session,
- what happens when a non-reply message arrives,
- how a user inspects or switches sessions,
- how stale or ambiguous session mappings are handled.

## 3. Prompt and turn behavior

- what kinds of Telegram inputs are accepted in the first version,
- how turns are started,
- what visible states a turn can enter,
- what progress feedback is expected,
- what marks a turn as complete, failed, stopped, or abandoned.

## 4. Approval behavior

- when approval is required,
- what an approval prompt must show,
- how a user can approve or deny,
- what happens when no response arrives,
- how approval state is kept unambiguous for the user.

## 5. Cancellation behavior

- how a user requests cancellation,
- what visible acknowledgement is required,
- how cancellation interacts with in-progress turns,
- what the user should see if cancellation succeeds late or fails,
- how stopped turns differ from failed turns in the product behavior.

## 6. Output and progress behavior

- how much intermediate progress is appropriate in Telegram,
- whether status should be edited or sent as additional messages,
- how long outputs are split or summarized,
- how errors are surfaced clearly,
- whether session labels are needed in visible responses.

## 7. Persistence and recovery behavior

- what information must survive restart,
- what information may be reconstructed,
- how incomplete turns are recovered,
- how stale approvals are resolved after restart,
- how duplicate or repeated delivery is prevented or explained.

## 8. Operator visibility and supportability

- what logs are required,
- what identifiers must be traceable across Telegram, gateway, and ACP,
- what minimal diagnostics an operator needs for failed or stuck turns,
- what manual recovery actions are expected in the first version.

## Cross-cutting requirement categories that must be added

The following categories were previously underrepresented and should be treated
as first-class inputs for the requirements phase.

### Security, privacy, and trust boundaries

The requirements phase should explicitly ask:

- how bot tokens are stored and rotated,
- how local session and transcript data is protected,
- what trust model exists between operator, bot, and local machine,
- what user-visible actions require explicit confirmation,
- what auditability is required for approval and stop actions.

This section should define requirement questions, not silently choose a storage
or secret-management implementation.

### Failure handling, idempotency, and resilience

The requirements phase should explicitly ask:

- how duplicate Telegram updates are handled,
- how out-of-order updates are handled,
- how stale callback actions are handled,
- how Telegram API failures are surfaced,
- how ACP timeout or failure is surfaced,
- what the user should experience when part of a turn succeeds and part fails.

### Data lifecycle

The requirements phase should explicitly ask:

- what data must be stored,
- how long it should be kept,
- whether the operator needs export or backup behavior,
- whether deletion or cleanup behavior is required,
- what data is considered sensitive.

### Non-functional expectations

The requirements phase should explicitly ask:

- what reliability level is expected for the first version,
- what response-latency expectations exist,
- what restart-recovery expectation is acceptable,
- what message-loss or duplication tolerance is acceptable,
- what rate-limit tolerance is acceptable.

### Acceptance scenarios

The requirements phase should explicitly define scenario-level outcomes rather
than only abstract capability lists.

At minimum, it should describe expected behavior for:

- a new session started from a plain message,
- a session continued by replying to a bot message,
- an explicit session switch,
- an approval request that is approved,
- an approval request that is denied,
- a turn stopped by the user,
- a restart during an in-flight turn,
- a stale callback or stale reply target.

## Items explicitly out of near-term scope

The following items should remain deferred unless the requirements phase
intentionally re-opens them:

- Personal WeChat support,
- Telegram group support,
- media-heavy workflows,
- Mini Apps,
- multiple agent backends,
- multi-user access control,
- multi-node deployment,
- advanced long-term approval policies such as approve-for-session or
  approve-forever.

## Open decision questions for the requirements phase

This document intentionally does **not** answer these questions. They are here
so the formal requirements phase can decide them deliberately.

### Scope and user-model questions

- Is the first version truly single-user, or single-trusted-operator?
- Should operator-only assumptions appear in the product requirements or only in
  deployment guidance?

### Session-model questions

- How many concurrent sessions should the first version support per user?
- Should there be exactly one active session pointer per chat?
- What should happen when a non-reply message arrives while multiple sessions
  already exist?

### Command-surface questions

- Which commands are mandatory in the first version?
- Which controls should also be exposed through inline buttons?
- Which controls are always available, and which only appear contextually?

### Approval questions

- What minimum information must an approval prompt show?
- Should unanswered approvals default to deny, expire silently, or remain pending
  until operator action?
- What should happen if the backend-side request is no longer valid by the time
  the user responds?

### Cancellation questions

- Is a text command alone enough, or is a visible stop button also required?
- What should the user see if the stop request arrives after the turn already
  finished?
- What product distinction should exist between cancelled, failed, and timed-out
  turns?

### Progress and delivery questions

- How much intermediate progress should be shown in Telegram?
- When should the system edit an existing status message instead of sending a new
  one?
- What output truncation, chunking, or summarization rules are acceptable?

### Persistence and deployment questions

- What data must be durable in the first version?
- Is local-only state acceptable for the first shipped version, or only for the
  development phase?
- Is long polling acceptable for the first shipped version, or only for local
  validation?
- When, if ever, should webhook delivery become a formal requirement?

## Suggested structure for the formal requirements document

The next-stage requirements specification will likely be easier to write if it
separates:

1. product context and scope,
2. actors and operating assumptions,
3. evidence-backed constraints and assumptions,
4. functional requirements,
5. non-functional requirements,
6. security and privacy requirements,
7. resilience and recovery requirements,
8. out-of-scope items,
9. acceptance scenarios,
10. unresolved decisions.

## Immediate input checklist for the requirements phase

Use this checklist when preparing or reviewing the formal requirements document.

### Scope and actors

- confirm the user and operator model,
- confirm the direct-chat-only boundary,
- confirm explicit out-of-scope items,
- confirm the intended first-version deployment context.

### Functional behavior

- confirm the first-version session model,
- confirm the first-version command surface,
- confirm approval behaviors,
- confirm cancellation behaviors,
- confirm progress and final-output behaviors,
- confirm recovery-visible behaviors after restart or partial failure.

### Cross-cutting requirements

- confirm security and privacy expectations,
- confirm failure-handling and idempotency expectations,
- confirm data-lifecycle expectations,
- confirm non-functional expectations,
- confirm operator visibility expectations.

### Validation readiness

- confirm scenario-level acceptance criteria for the first usable version,
- confirm which assumptions remain temporary,
- confirm which decisions are intentionally deferred beyond this phase.

## Working conclusion

For the Telegram-first, direct-chat-only slice, the project now has enough
discovery evidence to move into formal requirements analysis.

The purpose of the next stage is **not** to preserve every current assumption.
It is to turn the current evidence, boundaries, and unanswered questions into a
clear and reviewable requirements specification.
