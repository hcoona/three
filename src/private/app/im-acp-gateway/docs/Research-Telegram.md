# Research — Telegram for a Session-Aware IM-ACP Gateway

## Provenance

- Kind: design-oriented research note for an alternative IM channel.
- Requested outcome: document the early-research findings for implementing an
  Instant Messaging (IM) to Agent Client Protocol (ACP) gateway through
  Telegram, with session management as a first-class capability.
- Current project direction at the time of writing:
    - long-term: support multiple IM channels,
    - long-term: support multiple agent backends,
    - current agent starting point: **GitHub Copilot CLI** through ACP,
    - current IM investigation started with **Personal WeChat via iLink**,
    - Telegram research is being extended because the first live WeChat iLink
      verification run did not complete a successful login.
- Key references reviewed for this note:
    - [Telegram Bot Platform overview](https://core.telegram.org/bots)
    - [Telegram Bot API reference](https://core.telegram.org/bots/api)
    - [Telegram `getUpdates`](https://core.telegram.org/bots/api#getupdates)
    - [Telegram `setWebhook`](https://core.telegram.org/bots/api#setwebhook)
    - [Telegram `sendMessage`](https://core.telegram.org/bots/api#sendmessage)
    - [Telegram `editMessageText`](https://core.telegram.org/bots/api#editmessagetext)
    - [Telegram `sendChatAction`](https://core.telegram.org/bots/api#sendchataction)
    - [Telegram `CallbackQuery`](https://core.telegram.org/bots/api#callbackquery)
    - [Telegram `ReplyParameters`](https://core.telegram.org/bots/api#replyparameters)
    - [Telegram `sendMessageDraft` (newer Bot API capability)](https://core.telegram.org/bots/api#sendmessagedraft)
    - [Telegram local Bot API server](https://core.telegram.org/bots/api#using-a-local-bot-api-server)
    - [Telegram Bot API changelog](https://core.telegram.org/bots/api-changelog)
    - [Telegram Bot FAQ](https://core.telegram.org/bots/faq)
    - [Telegram privacy mode](https://core.telegram.org/bots/features#privacy-mode)
    - [GitHub Copilot CLI ACP server reference](https://docs.github.com/en/copilot/reference/copilot-cli-reference/acp-server)
    - [Agent Client Protocol overview](https://agentclientprotocol.com/protocol/overview)
    - existing repository research notes:
        - `src/private/app/im-acp-gateway/docs/Research.md`
        - `src/private/app/im-acp-gateway/docs/EarlyResearchVerification.md`
        - `src/private/app/im-acp-gateway/docs/PersonalWeChatVerificationLog.md`

This document is a design-oriented research note for the Telegram track. It
records feasibility, reference material, implementation options, and
recommended technical direction while still stopping short of product
requirements or a final implementation contract.

## Why this document exists

The current repository research already established a strong gateway direction:
the gateway core should own session state, IM adapters should stay isolated, and
Copilot CLI should be integrated through ACP rather than through a one-shot CLI
wrapper.

However, the current WeChat path is still transport-risk-heavy:

- the first live iLink verification run reached real QR bootstrap but did not
  complete a successful login,
- reply-routing evidence for WeChat is promising but still partially inferred
  from public package artifacts rather than fully validated on the target setup,
  and
- the Personal WeChat route currently depends on a more specialized integration
  path than Telegram's official Bot API model.

Because of that, Telegram deserves a separate early-research note rather than a
small appendix inside the existing WeChat-focused document.

## Executive summary

Telegram Bot API is a **strong candidate** for the first operational IM adapter
for the IM-ACP Gateway, even if WeChat remains strategically important.

The most important conclusions are:

1. Telegram is easier to evaluate than the current Personal WeChat iLink path
   because it offers an official HTTPS Bot API, official webhook and long
   polling delivery paths, and a stable bot identity model.
2. Telegram supports the current early requirements well:
    - user sends commands from IM,
    - gateway routes to Copilot CLI through ACP,
    - results are delivered back to IM,
    - multiple sessions can be continued by replying to bot messages.
3. Telegram improves the interaction model for approvals, cancellation, and
   progress feedback because it supports:
    - native reply metadata,
    - per-chat message identifiers,
    - inline buttons and callback queries,
    - message editing,
    - chat actions such as typing indicators.
4. Telegram does **not** remove the need for a gateway-owned session control
   plane. It changes the IM adapter and improves UX primitives, but the gateway
   core still needs durable session mapping, permission state, turn tracking,
   and recovery logic.
5. Telegram also changes the product shape in an important way: this is an
   **official bot account** model, not a personal-account bridge. That reduces
   transport uncertainty, but it may feel different from the eventual Personal
   WeChat target experience.

In short:

> Telegram is likely the easier and safer platform for proving the
> session-aware IM-ACP gateway concept, but the gateway still has to be built as
> a real session router rather than a chat relay.

## Telegram findings that matter to this project

### Official platform and transport model

Telegram bots are official bot accounts connected to a developer-controlled
backend server through the Bot API over HTTPS. The Bot API supports both:

- **long polling** through `getUpdates`, and
- **webhooks** through `setWebhook`.

This matters because the first gateway implementation can choose a transport
path based on deployment simplicity:

- use long polling for the smallest local proof of concept,
- use webhooks for a cleaner deployed service,
- keep the adapter contract the same above that transport choice.

Unlike the current Personal WeChat iLink path, Telegram does not require QR
login bootstrap for the bot runtime itself.

### Inbound event primitives

Telegram delivers structured `Update` objects and can report, among other
things:

- incoming messages,
- edited messages,
- callback queries,
- business account related updates,
- reactions and other richer interaction types.

For the gateway, the most important early primitives are:

- a normal text message from the user,
- a reply to a prior bot message,
- a callback query from an approval or stop button,
- enough chat and sender identifiers to map the event into a gateway session.

### Outbound interaction primitives

Telegram provides stronger official UI primitives than the current WeChat iLink
research path:

- `sendMessage` for normal text delivery,
- reply-aware sending through reply parameters,
- inline keyboards with callback buttons,
- message editing,
- `sendChatAction` for typing or activity indicators.

This is especially useful for IM-side approval and cancellation flows. Instead
of relying only on text commands such as `/approve` and `/deny`, the gateway can
surface compact buttons while still keeping text commands as a fallback.

### Identity and correlation primitives

Telegram provides several correlation-friendly concepts directly:

- `chat.id` for the chat or conversation container,
- `from.id` for the sender,
- `message_id` for a specific message in a chat,
- reply metadata that points back to a prior message,
- `message_thread_id` for topic-aware chats.

These are exactly the kinds of fields a session-aware gateway needs to map:

`replied_message_id -> gateway_session_id -> ACP session`

Compared with the current WeChat research, this is a major simplification. The
WeChat path appears to rely on a combination of quoted-message data,
`context_token`, and other message fields that still need fuller runtime
validation on the intended setup.

### Threads and topic-like conversation splits

Telegram has explicit support for threaded or topic-aware conversations in some
chat types. The official bot documentation also highlights threaded
conversations as useful for AI chatbots, and the Bot API changelog documents
support for:

- `message_thread_id`,
- topic-related fields in `Message`,
- sending and chat-action targeting to a specific topic,
- even private-chat topics in newer versions.

This does **not** mean the gateway should make Telegram topics the primary
session model. However, it does mean Telegram offers a future path for richer
multi-session UX inside one Telegram chat if that ever becomes desirable.

For early research and MVP thinking, reply-based routing is still simpler and
lower-risk than topic-per-session routing.

### Streaming and progress delivery

Official Telegram materials now mention `sendMessageDraft`, including the Bot
API changelog and the bot-platform overview page, which describe it as a way to
stream partial responses while they are being generated. Telegram also supports
`sendChatAction` for transient typing or activity indicators.

That suggests three possible progress strategies:

1. use `sendChatAction` for minimal "thinking" feedback,
2. use coalesced message edits for stable visible progress,
3. later validate `sendMessageDraft` in the target bot setup for richer live
   streaming.

The safest early-research conclusion is:

- Telegram appears better suited than the current WeChat path for progress UX,
  but
- the gateway should still treat aggressive token-by-token streaming as an
  optional delivery policy, not as a default assumption.

## Similarities with the current WeChat iLink track

Telegram and the current Personal WeChat iLink track still share the most
important product truth:

> the IM adapter is not the session system.

For both channels, the gateway still needs to own:

- gateway session identifiers,
- mapping from inbound and outbound IM messages to turns,
- mapping from gateway sessions to ACP sessions,
- active turn state,
- pending permission state,
- persistence and restart recovery.

Other important similarities:

- both channels can deliver inbound messages and outbound replies,
- both channels need some form of reply-aware routing or explicit session
  command fallback,
- both channels need anti-flooding logic for long agent outputs,
- both channels must be isolated behind a clean adapter interface.

So Telegram is not a reason to redesign the gateway core. It is a reason to
build a better channel adapter and to reduce transport uncertainty.

## Important differences from the current WeChat iLink track

### 1. Officialness and evidence quality

Telegram Bot API is an official first-party platform contract with public
documentation and official lifecycle guidance.

By contrast, the current Personal WeChat research depends on a combination of:

- public package artifacts,
- OpenClaw-related materials,
- published protocol descriptions, and
- live validation that has not yet fully succeeded on the intended setup.

This makes Telegram a stronger candidate for early feasibility spikes.

### 2. Bot account model versus personal account bridge

Telegram uses a bot account. Users must initiate the conversation by messaging
the bot or adding it to a group. Bots cannot start a conversation with a user
first.

This is different from the aspirational Personal WeChat experience, which aims
to feel closer to a user talking through a personal messaging account.

Impact:

- Telegram reduces transport risk,
- but Telegram may not fully answer product questions that are unique to a
  personal-account bridge model.

### 3. Reply correlation is more direct

Telegram natively supports:

- replies to prior messages,
- stable per-chat message identifiers,
- callback-query references to bot-generated UI.

The current WeChat findings indicate reply routing is plausible, but the actual
runtime dependency chain appears more indirect and still needs more validation.

Impact:

- Telegram is more favorable for early multi-session routing validation.

### 4. Approval and cancellation UX is stronger

Telegram's inline keyboards and callback queries are a major UX advantage for:

- approve,
- deny,
- stop,
- retry,
- session selection shortcuts.

WeChat can still support these through text replies or other channel-specific
patterns, but Telegram offers a cleaner official primitive for it.

### 5. Message update UX is stronger

Telegram supports message editing. That means the gateway can:

- send one "pending approval" message,
- update it to "approved" or "denied",
- replace noisy progress with a compact status summary,
- reduce chat clutter during long-running turns.

This is a meaningful advantage over a send-only interaction model.

### 6. Group behavior has privacy-mode caveats

Telegram bots in groups are subject to privacy-mode behavior unless privacy mode
is disabled or the bot has broader privileges. Official FAQ guidance makes clear
that privacy-enabled bots only receive a subset of group messages, though
replies to the bot remain especially important and available.

Impact:

- group-chat support is possible,
- but the first gateway version should focus on direct bot chats unless group
  behavior is explicitly needed.

### 7. Topics are promising but should not be the MVP dependency

Telegram's newer topic support may eventually help represent multiple sessions
more visibly inside Telegram. However:

- topic mode in private chats is a newer feature,
- the bot platform documentation notes that enabling private-chat threaded mode
  is subject to an additional fee for Telegram Star purchases,
- reply-based routing already solves the current requirement more simply.

Impact:

- topics are a useful optional future enhancement,
- they should not be the foundational assumption for the first version.

## Impact on the current early requirements

### Requirement 1: send instructions from IM to Copilot CLI and return results

Telegram supports this cleanly.

The minimum path is straightforward:

1. receive a Telegram message,
2. normalize it into a gateway inbound event,
3. resolve or create a gateway session,
4. send the prompt to Copilot CLI through ACP,
5. deliver progress and final output back through Telegram.

Compared with the WeChat track, Telegram appears easier for this requirement
because:

- inbound delivery is official and well documented,
- outbound delivery is official and well documented,
- no QR-login runtime dependency is involved,
- typing/progress and message-update patterns are richer.

### Requirement 2: manage multiple sessions and route by replying to a message

Telegram also supports this well.

The most natural early Telegram routing rule is:

1. if the user replies to a bot message, route to the linked gateway session,
2. otherwise, if the user uses an explicit session command, honor that command,
3. otherwise, use the user's current active session or create a new one.

Telegram helps here because:

- reply metadata is first-class,
- message IDs are stable within the chat,
- inline buttons can help expose session actions without requiring the user to
  remember command syntax,
- topics may become an optional enhancement later.

However, the gateway should still persist explicit mappings such as:

- inbound Telegram message ID,
- outbound Telegram message ID,
- gateway session ID,
- turn ID,
- pending permission or callback state.

The adapter should not assume Telegram will manage sessions for the product.

## Recommended implementation methods for a Telegram-backed gateway

### Method 1: standard Bot API with long polling

This is the simplest local proof-of-concept path.

Advantages:

- easy local startup,
- no public webhook endpoint required,
- quick validation of replies, buttons, and session mapping.

Use when:

- doing early local experiments,
- testing Copilot CLI ACP integration together with Telegram routing,
- validating reply and callback semantics before deployment hardening.

### Method 2: standard Bot API with webhooks

This is the cleaner deployed-service path.

Advantages:

- less polling overhead,
- more natural fit for a long-running gateway service,
- better operational shape for a server deployment.

Use when:

- the gateway becomes a real always-on service,
- stable HTTPS hosting is available,
- deployment simplicity matters more than local-only experimentation.

### Method 3: local Telegram Bot API server

Telegram documents a local Bot API server option that enables features such as:

- larger file handling,
- local-path file upload,
- more flexible webhook host and port options,
- larger webhook connection counts.

For this project, the local Bot API server is **not** required for the first
text-first gateway version. It is only worth considering if later work needs:

- larger media payloads,
- more deployment control,
- higher throughput,
- file-heavy workflows.

### Method 4: business-account related Telegram features

Telegram documents business-related bot integrations, including business account
update types and bot connections to business accounts.

This is interesting because it partially narrows the experience gap between
"official bot account" and "chatting through a business-owned account context".
However, it is not required for the current early requirements and would add
scope and identity complexity too early.

### Method 5: Mini Apps and richer UI

Telegram Mini Apps are powerful, but they are outside the early need. The
current product idea is fundamentally chat-first:

- message in,
- session-aware routing,
- approval/cancel/progress,
- result back out.

Mini Apps may later help with dashboards or session browsers, but they should
not be part of the initial IM-ACP gateway proof.

## Major functional capabilities worth targeting on Telegram

### Session creation and continuation

- create a new gateway session from a Telegram message,
- continue a session by replying to a bot message,
- continue or switch sessions via explicit commands such as `/new`,
  `/sessions`, `/use`, and `/stop`,
- optionally expose these actions through inline buttons.

### Turn execution and progress feedback

- send prompts to Copilot CLI through ACP,
- display transient activity through `sendChatAction`,
- coalesce incremental updates into Telegram-safe progress messages,
- deliver a final answer clearly and durably.

### Permission mediation

- display approval requests in a compact Telegram message,
- use inline buttons for approve or deny,
- keep text-command fallbacks,
- persist the callback or command decision before responding to ACP.

### Cancellation

- allow `/stop` as a universal fallback,
- optionally expose a stop button,
- map the selected action to ACP `session/cancel`,
- mark the turn state durably before or at the same time as the visible IM
  acknowledgement.

### Session-aware reply correlation

- store outbound bot message IDs,
- map them back to gateway sessions and turns,
- use Telegram reply metadata to continue the intended session,
- fall back to explicit session commands if no reply target is present.

### Formatting and delivery hygiene

- avoid flooding the chat with raw token chunks,
- edit or replace status messages when practical,
- split long final outputs safely,
- include compact session labels where useful.

### Persistence and restart recovery

- persist update processing progress,
- persist session mappings,
- persist pending approvals,
- rebuild active-session pointers after restart,
- reconcile incomplete turns on startup.

## Key technologies and platform features

### 1. Telegram Bot API over HTTPS

This is the primary Telegram integration surface and should be treated as the
default adapter contract.

### 2. Webhook or long-polling intake

The adapter should support one of these first and be structured so that either
intake mode can sit below the same normalized inbound event pipeline.

### 3. Reply metadata and message identifiers

These are essential for session-aware routing and are among Telegram's biggest
advantages for this project.

### 4. Inline keyboards and callback queries

These are the best Telegram-native primitives for approval, cancel, retry, and
session action UX.

### 5. Message editing

This allows better status management and less chat spam during long-running ACP
turns.

### 6. `sendChatAction`

This is the simplest Telegram-native progress signal and should likely be used
before any more ambitious streaming display mode.

### 7. Optional `sendMessageDraft`

This is promising for richer live-response UX, but it should be treated as an
optional feature pending actual runtime validation in the target setup.

### 8. Optional local Bot API server

Useful only if the project later needs file-heavy workflows or special
deployment control.

### 9. ACP integration remains unchanged

None of the Telegram findings change the agent side recommendation:

- keep Copilot CLI behind ACP,
- negotiate capabilities at runtime,
- let the gateway own product-level session semantics.

## Risks and constraints

### 1. Telegram does not eliminate session-management complexity

Risk:

- the cleaner IM adapter could tempt the implementation toward a thin relay.

Mitigation:

- keep the same gateway-owned session registry and turn orchestrator discipline
  already established in the main research note.

### 2. Bot-account semantics differ from personal-account semantics

Risk:

- Telegram validation success may not fully answer product questions that are
  unique to a personal-account IM channel.

Mitigation:

- treat Telegram as a strong validation path for gateway architecture and UX,
  not as proof that every future IM channel will behave identically.

### 3. Group privacy mode can surprise the product

Risk:

- the bot may not receive all group messages in privacy-enabled mode.

Mitigation:

- keep the first version focused on direct chats,
- treat group support as explicit later scope,
- rely on replies and commands when group scope is introduced.

### 4. Rate limits and anti-spam behavior still matter

Risk:

- noisy streaming or aggressive status updates can hit rate limits or degrade UX.

Mitigation:

- coalesce chunks,
- prefer message edits or compact summaries,
- separate transient status from final answer.

### 5. Callback-based UX still needs durable state

Risk:

- if approval or cancel buttons are used without durable persistence, a restart
  can orphan the visible UI state.

Mitigation:

- persist permission and callback correlation records before or while exposing
  visible actions to the user.

### 6. Topics and advanced private-thread features may add cost or scope

Risk:

- building around Telegram-specific advanced features too early can distort the
  gateway's channel-agnostic core.

Mitigation:

- treat topics, private threaded mode, and richer Telegram-only features as
  optional follow-up work after the core session gateway is proven.

## Early research conclusion

Telegram Bot API is currently the most promising next IM research track for this
repository's IM-ACP Gateway work.

It is promising because it combines:

- official API documentation,
- stable inbound and outbound primitives,
- strong reply correlation support,
- better approval and cancellation UX primitives,
- better progress-display options than the current WeChat evidence base.

At the same time, the right product conclusion remains conservative:

- Telegram improves the **channel adapter** a lot,
- Telegram improves the **interaction UX** noticeably,
- Telegram does **not** change the need for a durable gateway session core.

## Recommendation for the next early-research slice

If the repository continues the discovery flow before moving into requirements
analysis, the thinnest useful Telegram spike is:

1. create a simple Telegram bot,
2. use long polling first,
3. support text prompts only,
4. map reply-to-bot-message to gateway session,
5. expose `/new`, `/sessions`, `/stop`, approve, and deny,
6. use callback buttons for approve and stop,
7. use `sendChatAction` plus coalesced status delivery,
8. route turns to Copilot CLI through ACP,
9. persist all message, session, turn, and callback mappings locally.

The most important thing to validate in that spike is not Telegram transport by
itself. It is whether the full combination of:

- Telegram reply routing,
- Telegram approval or stop UX,
- gateway session persistence, and
- Copilot CLI ACP turn lifecycle

works cleanly in one end-to-end slice.

That would give the project stronger confidence for the next stage than
continuing to reason only from documentation.

This proposed Telegram spike should be understood as a way to satisfy the
substance of the current "thin end-to-end spike" validation goal from
`EarlyResearchVerification.md`: prove that reply routing and IM-side approvals
work on a real stack. However, it does **not** replace the separate exit
criterion that the Personal WeChat path must still be verified on the intended
setup if WeChat remains in scope.
