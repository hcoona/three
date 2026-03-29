# Telegram Topic Session Bridge POC

This package is an independent proof of concept for the `im-acp-gateway`
Telegram topic-session model.

It uses a Telegram forum-enabled supergroup where:

- the General Topic acts as the control plane,
- `/new --cwd ...` starts a new Copilot ACP session,
- each created Telegram topic maps to one gateway session and one ACP session,
- `/list` and `/kill` are handled in the General Topic,
- session-topic messages continue the mapped ACP session,
- permission prompts are surfaced in the session topic unless bridge-managed
  allow-all mode is enabled for that topic.

## Commands

From the package directory:

```bash
pnpm run build
pnpm run start -- help
```

Setup the bot and General Topic routing target:

```bash
pnpm run start -- setup \
  --bot-token YOUR_BOT_TOKEN \
  --chat-id YOUR_SUPERGROUP_CHAT_ID
```

Inspect raw updates to discover topic-related fields:

```bash
pnpm run start -- monitor
```

Run the bridge:

```bash
pnpm run start -- bridge
```

Show persisted state:

```bash
pnpm run start -- show-state
```

## General Topic commands

Send these in the configured General Topic:

- `/new --cwd /absolute/path optional prompt text`
- `/takeover --session-id ACP_SESSION_ID --cwd /absolute/path optional topic label`
- `/list`
- `/kill <gateway-session-id|acp-session-id|topic-thread-id>`
- `/help`

The bridge replies to each General Topic command so the request and result stay
threaded together in Telegram.

Telegram clients sometimes autocorrect `--cwd` into a long-dash variant such as
`—cwd`. This POC accepts that Telegram-shaped variant for `/new`.

## Session Topic commands

Send these inside a mapped session topic:

- plain text to continue the Copilot session,
- `/yolo` or `/allow-all` enables bridge-managed allow-all approval mode for
  this topic,
- `/yolo off` disables bridge-managed allow-all approval mode,
- `/yolo show` shows the current bridge approval mode,
- slash commands not reserved by the bridge, such as `/status`, are forwarded
  to Copilot,
- bare `/new` and `/resume` are reserved by the bridge to avoid changing
  Copilot context without changing the topic mapping,
- `/copilot <text>` to force raw text to Copilot, for example
  `/copilot /yolo inspect this repo`,
- `/stop` to cancel the active turn,
- `/approve [approval-id]` to approve the latest or a specific pending approval,
- `/deny [approval-id]` to deny the latest or a specific pending approval,
- `/help` to show the session-topic command summary.

## PoC behavior notes

- `workingDirectory` is validated before any ACP session or Telegram topic is
  created.
- Bridge replies and Copilot output are rendered with Telegram-safe
  MarkdownV2.
- Bridge-managed `/yolo` is implemented by automatically selecting ACP
  permission options for the topic, preferring `allow_always` and then
  `allow_once`.
- Raw `/copilot /yolo` is only forwarded as Copilot input text; it does not
  change the bridge permission policy by itself.
- On bridge restart, connected topics attempt to restore their persisted ACP
  sessions through `session/load` before continuing.
- `takeover` validates an existing ACP session with `session/load` before
  creating a topic and linking it into gateway state.
- Permission cards show the concrete tool title, kind, summarized input, and
  the actual ACP selection options exposed by Copilot.
- The bridge does not forward low-level `tool_call` or `tool_call_update`
  chatter into Telegram topics; topics stay focused on explicit Copilot dialog
  plus permission prompts.
- Telegram API rate limits (`HTTP 429`) are retried using Telegram's
  `retry_after` hint before the bridge gives up.
- The General Topic is addressed by omitting `message_thread_id`; it does not
  require a separately configured topic id.
- If Telegram delivers a topic-close event, the bridge cancels any active turn
  and detaches the topic-to-session mapping.
- `/kill` detaches the mapping and cancels any active turn, but it does not
  close the Telegram topic automatically.
- State is stored as local JSON under
  `~/.local/share/im-acp-gateway/telegram-topic-session-bridge` by default.
