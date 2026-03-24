# WeChat iLink Verifier POC

This package is a simple proof of concept for the first verification item in
`src/private/app/im-acp-gateway/docs/EarlyResearchVerification.md`.

It focuses on validating the real Personal WeChat transport path by exercising:

- QR-code login,
- local login-state persistence,
- inbound long polling,
- outbound text replies,
- inspection of reply-correlation metadata such as `context_token`,
  `message_id`, `session_id`, and quoted-message data.

## Why this POC exists

`Research.md` records strong public evidence for the iLink / ClawBot path, but
the first early-research question still requires a real-stack check on the
intended setup. This POC narrows the scope to the transport primitives needed
for that validation.

## Commands

From the package directory:

```bash
pnpm run build
```

Show the CLI help:

```bash
pnpm run start -- help
```

Start QR login:

```bash
pnpm run start -- login
```

Override the state directory or base URL if needed:

```bash
pnpm run start -- login \
  --state-dir ~/.local/share/im-acp-gateway/wechat-ilink-verifier \
  --base-url https://ilinkai.weixin.qq.com
```

Monitor messages and auto-reply with a simple echo:

```bash
pnpm run start -- monitor --reply-prefix "POC echo: "
```

Inspect only without replying:

```bash
pnpm run start -- monitor --no-reply
```

Send a manual text message when you already know the target user and
`context_token`:

```bash
pnpm run start -- send \
  --to-user-id some-user@im.wechat \
  --context-token your-context-token \
  --text "hello from the POC"
```

Show the current saved state without printing the full token:

```bash
pnpm run start -- show-state
```

## Local state

By default the CLI stores state in:

```text
~/.local/share/im-acp-gateway/wechat-ilink-verifier
```

The state file stores:

- the `bot_token`,
- the effective `baseUrl`,
- the last `get_updates_buf` cursor,
- the latest QR-code status metadata,
- timestamps useful for restart validation.

QR-code artifacts are saved under the same state directory so the user can open
or inspect them outside the CLI if the response includes image content.

## Validation checklist

Use the POC to validate the first early-research item:

1. Run `login` and confirm QR-code login succeeds.
2. Restart the process and run `show-state` to confirm login state persisted.
3. Run `monitor`, send a real text message from Personal WeChat, and confirm the
   POC receives it.
4. Confirm the POC sends a text reply back successfully.
5. Inspect the logged payload to confirm whether `context_token`,
   `message_id`, `session_id`, item-level IDs, and quoted-message data are
   present and stable enough for later session correlation.

## Limitations

- The POC is intentionally transport-focused and does not implement the gateway
  architecture from `Research.md`.
- The token is stored locally in plain text for simplicity, so use a dedicated
  state directory and treat it as sensitive.
- Media upload and typing indicators are out of scope for this verification
  pass.
