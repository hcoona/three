# Early Research Verification — Telegram IM-ACP Gateway

## Provenance

- Kind: early discovery verification note for the Telegram track.
- Purpose: record the remaining real-stack validation work that should be
  completed before Telegram is treated as a credible IM path for the
  session-aware IM-ACP Gateway.
- Scope assumptions at the time of writing:
    - personal use only,
    - single user,
    - single Telegram bot,
    - private chat is the first Telegram topology to validate,
    - GitHub Copilot CLI via ACP is the initial agent backend.
- Related source documents:
    - `src/private/app/im-acp-gateway/docs/Research-Telegram.md`
    - `src/private/app/im-acp-gateway/docs/EarlyResearchVerification.md`

This document is intentionally narrow. It does **not** define product
requirements, architecture, or implementation details. It only records the
Telegram-specific discovery-stage validations that remain worth doing on the
real stack.

## Why this document exists

Telegram documentation is much stronger and more official than the current
Personal WeChat iLink evidence base. That lowers transport uncertainty, but it
does not remove the need for runtime validation.

There are still several questions that are best answered by a thin POC rather
than by documentation alone:

- do Telegram replies expose the exact metadata needed for durable session
  routing in the intended setup,
- do inline buttons and callback queries work cleanly enough for approval and
  stop UX,
- does the Telegram interaction model remain understandable once Copilot CLI ACP
  is involved,
- and does the end-to-end flow still work when all three concerns stay separate:
  Telegram, gateway state, and Copilot ACP.

If WeChat remains in scope, this Telegram verification work is **supplementary**.
It can strengthen the general feasibility case and can satisfy the substance of
the thin end-to-end spike goal, but it does **not** replace the separate need to
validate the Personal WeChat path.

## Current status after the 2026-03-24 Telegram runs

The Telegram verification track now has direct runtime evidence for the first
three discovery items:

- Item 1 is validated for the intended private-chat setup:
  real inbound and outbound private-chat traffic, reply metadata,
  `editMessageText`, and `sendChatAction` were all observed successfully.
- Item 2 is materially validated:
  inline buttons, `callback_query`, message editing after a decision, and text
  fallback commands were all exercised on the real Telegram stack.
- Item 3 is now validated as a thin feasibility spike:
  a Telegram message was routed to Copilot CLI through ACP, the Copilot result
  came back to Telegram, and reply-based continuation was exercised over the
  live bridge.

The main Telegram-specific discovery gaps that remain are narrower:

- the current bridge auto-cancels ACP permission requests instead of surfacing a
  full Telegram approval UX for real tool permissions,
- the `/stop` path is wired and was exercised, but a deliberately long-running
  ACP turn was not held open to produce stronger mid-turn cancellation evidence,
- Telegram group behavior remains a separate scope question and is still open.

## Remaining early research verification items

## 1. Validate the real Telegram bot path on the intended setup

### Goal

Confirm that a real Telegram bot can handle the basic transport and message
correlation primitives the gateway depends on in a private chat.

### What to verify

- Bot creation and token provisioning succeed.
- The bot can receive inbound private-chat messages reliably.
- The bot can send outbound text replies reliably.
- A private-chat reply to a prior bot message exposes usable metadata such as:
    - `chat.id`,
    - `from.id`,
    - `message_id`,
    - reply target metadata.
- The selected intake mode behaves as expected:
    - `getUpdates` with offsets, or
    - webhook delivery with stable request handling.
- `editMessageText` works for bot-authored status updates.
- `sendChatAction` is visible enough to be useful for progress feedback.
- If available in the target setup, `sendMessageDraft` can be probed as an
  optional richer-streaming capability.

### Why this is still early research

This is still basic platform validation. The question is not how the gateway
should behave in all cases. The question is whether Telegram exposes the
transport and correlation primitives the product idea depends on.

### Minimum success signal

At least one real private-chat loop is observed end to end:

1. a user sends a Telegram message,
2. the bot receives it,
3. the bot replies successfully,
4. a reply to the bot message carries usable correlation metadata, and
5. at least one status-oriented feature such as message editing or
   `sendChatAction` is observed successfully.

## 2. Validate Telegram approval and cancellation primitives

### Goal

Confirm that Telegram's native interaction primitives are good enough for IM-side
approval and stop flows.

### What to verify

- Inline keyboard buttons render correctly in the target client.
- Button clicks produce usable `callback_query` updates.
- Callback payloads are sufficient to resolve the intended pending action when
  combined with gateway state.
- The original approval or stop prompt can be updated cleanly after the user
  acts, for example through message editing.
- Text-command fallback still works for:
    - `/approve`,
    - `/deny`,
    - `/stop`.
- One cancellation interaction can be completed without leaving ambiguous state
  in the chat.

### Why this is still early research

This is still runtime verification of Telegram-side primitives. It is not yet a
product design decision about the final approval UX.

### Minimum success signal

One visible Telegram approval or stop interaction succeeds:

1. the bot presents an approval or stop prompt,
2. the user acts through a button or text command,
3. the callback or command is correlated correctly,
4. the chat state is updated clearly,
5. the resulting action can be relayed to the gateway side without ambiguity.

## 3. Run one thin end-to-end Telegram-to-ACP spike

### Goal

Validate the narrowest real slice that proves Telegram can participate in the
session-aware IM-ACP gateway concept.

### What to verify

- A Telegram message can be routed to Copilot CLI through ACP.
- A Copilot response can be delivered back to Telegram.
- A Telegram reply to the bot message can be mapped back to the intended logical
  conversation.
- The gateway can preserve enough state to continue the correct Copilot session.
- An approval or stop interaction can be surfaced and completed successfully.
- The flow still works when Telegram, gateway state, and ACP are treated as
  separate runtime concerns.

### Why this is still early research

This is a feasibility spike, not a product design. The point is not to lock a
final schema, storage model, or UI. The point is to remove uncertainty about
whether session routing and approval handling are operationally believable on
the real Telegram stack.

### Minimum success signal

One thin scenario succeeds:

1. a user sends a Telegram message,
2. the system routes it to Copilot CLI through ACP,
3. the user replies to the bot message to continue the same conversation,
4. an approval or stop interaction is surfaced over Telegram,
5. the conversation continues on the intended session after that interaction.

The first thin spike now demonstrates items 1 through 3 directly. The remaining
follow-up here is mostly about deepening confidence in permission and
cancellation behavior rather than re-proving basic feasibility.

## 4. Validate Telegram group behavior only if groups are near-term scope

### Goal

Determine whether Telegram group chats should be considered near-term scope or
intentionally deferred.

### What to verify

- Privacy mode behavior for commands, replies, and mentions is understood in
  practice.
- Replies to the bot in groups are received as expected.
- Commands explicitly addressed to the bot are received as expected.
- If topics are in scope, `message_thread_id` behavior is understood well enough
  to decide whether it helps or complicates the first version.

### Why this is still early research

This is still boundary validation. It answers whether group-chat support is
worth treating as near-term scope, not how the final group product should work.

### Minimum success signal

A clear scope decision is produced:

- either direct-chat-only is confirmed as the right early boundary,
- or group support is validated enough to justify near-term inclusion.

## Practical validation checklist

Use this as the shortest sensible execution order for a Telegram POC:

1. create a Telegram bot and store its token securely,
2. choose `getUpdates` first unless a webhook is already easy to host,
3. verify `/start` and a normal private-chat text message,
4. verify outbound text reply,
5. inspect the received message and reply metadata,
6. verify one `editMessageText` update,
7. verify one `sendChatAction` status signal,
8. verify one inline button and `callback_query` round trip,
9. verify text fallback for `/approve`, `/deny`, and `/stop`,
10. route one Telegram message to Copilot CLI through ACP,
11. reply to the bot message and confirm the same logical session is continued,
12. decide explicitly whether Telegram groups are in or out of near-term scope.

## Exit criterion for the Telegram verification track

Telegram can be treated as a credible IM path for the next project stage when:

- the private-chat bot path has been validated on the intended setup,
- Telegram-side approval or stop primitives have been validated in practice,
- one thin Telegram-to-ACP spike has demonstrated session continuity and
  approval-or-stop feasibility, and
- group scope has either been intentionally deferred or validated enough to
  justify near-term inclusion.

This strengthens the overall early-research picture for the gateway. However, if
WeChat remains a target channel, this document does **not** supersede the
WeChat-specific verification requirements recorded elsewhere.
