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
- status-oriented features such as chat actions and message updates.

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

## Open status summary

As of this document revision:

- one live Telegram validation run has been completed,
- private-chat transport has been validated successfully,
- reply-correlation metadata has been observed successfully,
- inline-button callback handling has been observed successfully,
- Telegram-to-ACP end-to-end validation remains open.
