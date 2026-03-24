# Telegram Bot Verification Log — IM-ACP Gateway

## Provenance

- Kind: iterative validation log for the Telegram track.
- Purpose: record real-stack validation attempts for the Telegram bot path on
  the intended setup without overloading the Telegram research and verification
  notes.
- Related source documents:
    - `src/private/app/im-acp-gateway/docs/Research-Telegram.md`
    - `src/private/app/im-acp-gateway/docs/EarlyResearchVerification-Telegram.md`

This document is intended to grow over multiple rounds. Each run should record
what was attempted, what was observed, what remains unknown, and what should be
done next.

## Scope of this log

This log currently covers the first two Telegram early-research verification
items and their overlap with thin-spike preparation:

- private-chat bot setup and token validation,
- inbound message polling through `getUpdates`,
- outbound text delivery,
- reply-correlation metadata on real Telegram traffic,
- Telegram approval and callback primitives,
- status-oriented features such as chat actions and message updates,
- the first thin Telegram-to-Copilot-ACP bridge run.

It is not the place for gateway design decisions or broader product planning.

## Validation artifact used so far

The current validation helper is the dedicated POC at:

- `src/private/app/im-acp-gateway/poc/telegram-bot-verifier`

That POC talks directly to the Telegram Bot API and is designed to validate
Telegram-side transport and interaction primitives before any broader gateway
assumptions are treated as confirmed.

## Run log

### Run 1 — 2026-03-24

#### Goal

Start the first real-stack verification attempt for the Telegram bot path and
confirm whether the basic private-chat transport, reply correlation, and
approval-button flow work on the intended setup.

#### Process

1. a dedicated Telegram verifier POC was created under
   `src/private/app/im-acp-gateway/poc/telegram-bot-verifier`,
2. the POC passed its local `test`, `typecheck`, `lint`, and CLI-help checks,
3. the runtime credentials were loaded locally from
   `src/private/app/im-acp-gateway/.env` using `TG_BOT_TOKEN` and `TG_CHAT_ID`,
   without committing or printing the secret values into repository files,
4. the POC `setup` command validated the bot token successfully through a real
   `getMe` call and persisted local state under
   `~/.local/share/im-acp-gateway/telegram-bot-verifier/state.json`,
5. the POC `send-action` command was used to send a `typing` chat action to the
   configured private chat,
6. the POC `send` command sent a real bot message asking the user to reply so
   reply metadata could be inspected,
7. the POC `approval-demo` command sent a real inline-button prompt with
   `Approve`, `Deny`, and `Stop` buttons,
8. the POC `monitor` command started live polling through `getUpdates`,
9. the user replied with the text `Acknowledge` to the bot message,
10. the user clicked the `Approve` button on the inline-button prompt,
11. the POC captured both updates, logged their summaries and raw payloads, and
    automatically:
    - answered the callback query,
    - edited the approval-demo message,
    - sent an echo reply to the user's reply message.

#### Observed result

- the Telegram bot token and private-chat path are real and working on the
  intended setup,
- outbound bot messages are delivered successfully,
- the `typing` chat action was visible enough for the user to notice,
- reply-aware routing data is clearly available on real traffic:
    - the user's message `328` was received as a normal `message` update,
    - `reply_to_message.message_id` correctly pointed back to the bot's earlier
      message `326`,
- approval-button handling is clearly available on real traffic:
    - the user's button click arrived as a `callback_query` update,
    - the callback payload was
      `demo:approve:364ce6d0`,
    - the callback was correlated to bot message `327`,
- the user confirmed the visible UX looked correct:
    - the reply was received,
    - the bot responded with `POC echo: Acknowledge`,
    - the `Approve` button behaved as expected.

#### Still unverified after this run

- webhook-mode intake on the intended deployment setup,
- Telegram group behavior and privacy-mode implications in practice,
- richer streaming behavior such as `sendMessageDraft`,
- Telegram-to-Copilot-ACP end-to-end routing,
- whether the same clean behavior holds once the gateway persists richer
  session, turn, and permission state instead of the current Telegram-only POC.

#### Current conclusion

This run materially strengthens confidence in the Telegram track.

The first two Telegram validation areas are now supported by direct runtime
evidence on the intended setup:

- the private-chat bot transport path works,
- reply metadata is usable for session correlation,
- inline-button approval handling works,
- callback-query data is usable for pending-action correlation.

This does **not** yet satisfy the full Telegram thin-spike goal, because the
Telegram POC has not yet been wired to Copilot CLI through ACP. However, the
Telegram-side uncertainty is now meaningfully reduced.

#### Recommended next action

Keep the current Telegram verifier as the transport-side probe and build the
next thin slice on top of it:

1. route one Telegram message to Copilot CLI through ACP,
2. return the Copilot response to Telegram,
3. preserve the reply-to-message correlation for session continuation,
4. surface one real approval or stop interaction while ACP is involved.

### Run 2 — 2026-03-24

#### Goal

Run the thinnest real Telegram-to-Copilot-ACP slice and verify that reply-based
session continuation works on the intended setup.

#### Process

1. the Telegram verifier POC was extended with a `bridge` command and a thin
   ACP client that:
   - starts `copilot --acp --stdio`,
   - creates one active Copilot session per Telegram chat,
   - routes a normal Telegram text message into `session/prompt`,
   - returns the final Copilot text response to Telegram,
   - supports `/new` for session reset and `/stop` for turn cancellation,
   - auto-cancels ACP permission requests in this first cut,
2. the updated POC passed local `typecheck`, `test`, and `lint`,
3. a local ACP smoke test was run directly against Copilot CLI:
   - `initialize`,
   - `session/new`,
   - `session/prompt`,
   - and a no-tools prompt completed with `stopReason = end_turn`,
4. the live `bridge` command was started against the repository workspace,
5. the bot sent an operator message asking the user to:
   - send a fresh message,
   - reply to a Copilot-generated bot message,
   - optionally try `/stop`,
6. the user sent `Hello`,
7. the user then replied `What can you do?` to bot message `332`,
8. the user then replied `Summarize current repo` to bot message `334`,
9. the user then sent `/stop` replying to bot message `337`,
10. the bridge process logged each inbound update summary while the local state
    file recorded the latest ACP session identifier and stop reason.

#### Observed result

- the thin end-to-end path worked on the real stack:
  Telegram private chat -> verifier bridge -> Copilot ACP -> Telegram private
  chat,
- the first user message arrived as a normal Telegram `message` update without a
  reply target, which is the expected shape for starting a new logical session,
- reply-based continuation worked in practice:
  - inbound message `333` carried `reply_to_message.message_id = 332`,
  - inbound message `335` carried `reply_to_message.message_id = 334`,
  - both replies were directed at earlier bridge-generated bot messages,
- the bridge persisted ACP-side continuity data:
  - the saved local state now includes `lastAcpSessionId`,
  - the latest completed prompt recorded `lastAcpStopReason = end_turn`,
- the `/stop` command path was exercised from Telegram text input and reached the
  bridge successfully,
- the user confirmed the visible behavior looked correct.

#### Still unverified after this run

- webhook-mode intake on the intended deployment setup,
- Telegram group behavior and privacy-mode implications in practice,
- richer streaming behavior such as `sendMessageDraft`,
- full ACP permission mediation over Telegram instead of the current
  auto-cancel behavior,
- forced cancellation of a visibly long-running in-progress ACP turn; this run
  exercised the `/stop` command path, but did not intentionally hold a long
  turn open to observe mid-turn interruption.

#### Current conclusion

This run satisfies the main thin-spike question for the Telegram track.

The project now has direct runtime evidence that:

- a Telegram private-chat message can be routed into Copilot CLI through ACP,
- the Copilot result can be sent back to Telegram,
- a user can reply to a bridge-generated bot message and continue the same
  logical conversation shape,
- the bridge can persist minimal ACP session continuity data alongside Telegram
  intake state.

The remaining uncertainty has shifted away from basic feasibility and toward
follow-up UX depth:

- richer permission mediation,
- stronger in-flight cancellation proof,
- and whether group or webhook behavior matters for near-term scope.

## Open status summary

As of this document revision:

- two live Telegram validation runs have been completed,
- private-chat transport has been validated successfully,
- reply-correlation metadata has been observed successfully,
- inline-button callback handling has been observed successfully,
- Telegram-to-ACP thin-spike validation has been observed successfully,
- ACP permission mediation over Telegram remains intentionally thin,
- group behavior remains open.
