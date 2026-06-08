# Telegram Bot Verifier POC

This package is a simple proof of concept for the Telegram verification items in
`src/private/app/im-acp-gateway/docs/EarlyResearchVerification-Telegram.md`.

It focuses on validating the real Telegram bot path by exercising:

- bot token setup,
- inbound update polling with offset persistence,
- outbound text replies,
- inspection of reply-correlation metadata such as `chat.id`, `from.id`,
  `message_id`, and reply targets,
- message editing,
- chat-action progress signals,
- inline-button and callback-query handling,
- a thin Telegram-to-Copilot-ACP bridge.

## Why this POC exists

`Research-Telegram.md` and `EarlyResearchVerification-Telegram.md` record strong
evidence that Telegram is a promising IM path, but early research still needs a
real-stack check on the intended setup. This POC narrows the scope to the
Telegram primitives needed for that validation.

## Commands

From the package directory:

```bash
pnpm run build
```

Show the CLI help:

```bash
pnpm run start -- help
```

Validate the bot token and save local state:

```bash
pnpm run start -- setup --bot-token YOUR_BOT_TOKEN
```

Optionally save a default chat id or use a custom API base URL:

```bash
pnpm run start -- setup \
  --bot-token YOUR_BOT_TOKEN \
  --chat-id 123456789 \
  --api-base-url https://api.telegram.org
```

Monitor updates and auto-reply with a simple echo:

```bash
pnpm run start -- monitor --reply-prefix "POC echo: "
```

Inspect only without replying:

```bash
pnpm run start -- monitor --no-reply
```

Send a manual text message:

```bash
pnpm run start -- send --chat-id 123456789 --text "hello from the POC"
```

Send a chat action such as typing:

```bash
pnpm run start -- send-action --chat-id 123456789 --action typing
```

Send an inline-button approval demo prompt:

```bash
pnpm run start -- approval-demo --chat-id 123456789
```

Edit a bot-authored message:

```bash
pnpm run start -- edit \
  --chat-id 123456789 \
  --message-id 42 \
  --text "updated text"
```

Show the current saved state without printing the full token:

```bash
pnpm run start -- show-state
```

Run the thin Telegram-to-Copilot bridge:

```bash
pnpm run start -- bridge --cwd /absolute/path/to/your/workspace
```

In bridge mode:

- a normal text message is routed to Copilot ACP,
- the final Copilot text is sent back to Telegram,
- `/new` resets the active Copilot session for the chat,
- `/stop` cancels the active Copilot turn.

## Local state

By default the CLI stores state in:

```text
~/.local/share/im-acp-gateway/telegram-bot-verifier
```

The state file stores:

- the Telegram `botToken`,
- the effective `apiBaseUrl`,
- an optional default chat id,
- the last observed ACP session identifier and stop reason,
- the last observed `update_id`,
- the latest observed chat and callback metadata,
- timestamps useful for restart validation.

## Practical validation checklist

Use the POC to validate the Telegram track:

1. Run `setup` and confirm the token is valid.
2. Send `/start` to the bot in Telegram.
3. Run `monitor` and confirm the POC receives the private-chat message.
4. Confirm the POC sends a text reply back successfully.
5. Inspect the logged update summary and raw payload to confirm whether
   `chat.id`, `from.id`, `message_id`, `reply_to_message`, and topic-related
   fields are present and stable enough for later session correlation.
6. Run `approval-demo` and click a button in Telegram to confirm
   `callback_query` handling works.
7. Run `send-action` and confirm the activity indicator is useful.
8. Run `edit` and confirm bot-authored message updates work.
9. Run `bridge`, send a normal Telegram text message, and confirm Copilot's
   reply comes back to Telegram.
10. Reply to a Copilot-generated bot message and confirm the same logical bridge
    session continues.
11. Run `/stop` during a long-running Copilot turn if you want to validate
    Telegram-to-ACP cancellation behavior.

## Limitations

- The POC is intentionally Telegram-focused and does not implement the gateway
  architecture from `Research.md`.
- The bot token is stored locally in plain text for simplicity, so use a
  dedicated state directory and treat it as sensitive.
- Webhooks are not implemented in this first cut; the POC uses `getUpdates`
  because it is the simplest local validation path.
- The bridge currently auto-cancels ACP permission requests instead of surfacing
  a full Telegram approval workflow for real tool permissions.
