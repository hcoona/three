# Early Research Verification — IM-ACP Gateway

## Provenance

- Kind: early discovery verification note.
- Purpose: record the remaining validation work that should be completed before
  moving fully from early research into requirements analysis.
- Scope assumptions at the time of writing:
    - personal use only,
    - single user,
    - single WeChat account,
    - single device,
    - Personal WeChat via iLink / ClawBot is the primary IM track,
    - GitHub Copilot CLI via ACP is the initial agent backend.

This document is intentionally narrow. It does **not** define product
requirements, architecture, or implementation details. It only records the
remaining discovery-stage validations.

## Why this document exists

Most of the current work has already moved beyond pure discovery and into
requirements-analysis territory. However, three items still need direct
validation on the real stack before the project can confidently leave the early
research stage.

## Remaining early research verification items

## 1. Validate the real Personal WeChat path on the intended setup

### Goal

Confirm that the actual Personal WeChat path works in the target single-user,
single-device environment, rather than relying only on public package artifacts
and protocol descriptions.

### What to verify

- QR-code login succeeds.
- Login state or token persistence behaves as expected across process restart.
- Inbound text messages are received reliably.
- Outbound text replies are sent successfully.
- Real reply actions expose the metadata needed for session correlation, such as
  `context_token`, quoted-message details, message identifiers, or other stable
  reference fields.

### Why this is still early research

This is not yet about product behavior or system design. It is still basic
environment and transport validation: does the real Personal WeChat path expose
the primitives that the product idea depends on?

### Minimum success signal

At least one real conversation loop is observed end to end:

1. a user sends a message from Personal WeChat,
2. the transport receives it,
3. a reply is sent back successfully, and
4. a real WeChat reply action can be inspected for usable correlation metadata.

### Validation run log

Detailed attempt-by-attempt results for this item are tracked separately in:

- `src/private/app/im-acp-gateway/docs/PersonalWeChatVerificationLog.md`

That log should be extended for second, third, and later verification rounds so
this document can remain focused on the research-stage questions and exit
criteria.

## 2. Validate actual Copilot CLI ACP behavior beyond `initialize`

### Goal

Confirm the runtime behavior of the installed Copilot CLI ACP server version,
instead of relying only on protocol documentation and capability advertisement.

### What to verify

- `session/new` works in the installed environment.
- `session/load` works when `loadSession` is advertised.
- `session/list` works when `sessionCapabilities.list` is advertised.
- Streaming updates arrive as expected during a prompt turn.
- Permission request and response flow works in practice.
- Cancellation behavior works in practice.
- Useful correlation data can be captured from real prompt turns.

### Why this is still early research

This is runtime validation of external dependency behavior. It is still about
what the platform actually does, not yet about what the gateway should do with
it.

### Minimum success signal

A lightweight ACP probe can:

1. create a session,
2. send a prompt,
3. observe streaming updates,
4. confirm load/list behavior on the installed version, and
5. confirm at least one permission or cancellation path if the environment
   triggers it.

## 3. Run one thin end-to-end spike for reply routing and IM-side approvals

### Goal

Validate the narrowest possible real-world slice that proves the product concept
is operationally believable before formal requirements analysis continues.

### What to verify

- A WeChat reply can be mapped back to the intended logical conversation.
- The gateway can preserve enough state to continue the correct Copilot session.
- An IM-side approval interaction can be surfaced and completed successfully.
- The flow still works when the gateway, Copilot CLI, and IM transport are
  treated as separate runtime concerns.

### Why this is still early research

This is a feasibility spike, not a product design. The point is not to choose a
final schema, UX, or persistence model. The point is to remove uncertainty about
whether reply routing and approval handling are actually workable on the real
stack.

### Minimum success signal

One thin scenario succeeds:

1. a user sends a message,
2. the system routes it to Copilot CLI through ACP,
3. the user replies to the bot message to continue the same conversation,
4. an approval interaction is surfaced over IM, and
5. the conversation continues on the intended session after the approval step.

## Exit criterion for the early research stage

The project can move cleanly into requirements analysis when:

- the Personal WeChat path has been verified on the intended setup,
- Copilot CLI ACP runtime behavior has been validated beyond `initialize`, and
- one thin end-to-end spike has demonstrated reply routing and IM-side approval
  feasibility.

At that point, the remaining questions are primarily about requirement
priorities, acceptable behavior, and system boundaries rather than about basic
external-platform feasibility.
