---
name: copilot-notify-summary
description: Keep .copilot/notify-summary.json updated so the VS Code GitHub Copilot Telegram notification hook can send a task summary when the session stops.
applyTo: '**'
---

# Notification summary handoff

If the current workspace contains `.copilot/notify-session.json`, maintain
`.copilot/notify-summary.json` before you finish the current task.

- Read `.copilot/notify-session.json` and copy its `run_id` into
  `.copilot/notify-summary.json`.
- Overwrite `.copilot/notify-summary.json` with valid JSON. Do not append logs,
  prose, or Markdown.
- Set `updated_at` to the current UTC time in the
  `yyyy-MM-ddTHH:mm:ss.fffZ` format.
- Always write English text.
- Never include secrets, tokens, credentials, or private keys.
- Keep `summary` to one concise sentence, ideally under 240 characters.
- Use arrays for `details`, `changed_files`, and `next_steps`. Use `[]` when
  there is nothing to report.
- Use `status: "info"` for read-only or explanation-only tasks.

Use this JSON shape:

```json
{
    "version": 1,
    "run_id": "<value from .copilot/notify-session.json>",
    "updated_at": "2026-03-11T12:34:56.789Z",
    "status": "success",
    "summary": "Implemented a VS Code GitHub Copilot hook that sends Telegram notifications with a task summary.",
    "details": [
        "Installed user-level hooks and instructions for summary handoff.",
        "Updated the Telegram notification runtime to read the generated summary file."
    ],
    "changed_files": [
        "src/private/app/vscode-copilot-telegram-hook/Install-UserCopilotHook.ps1",
        "src/private/app/vscode-copilot-telegram-hook/instructions/copilot-notify-summary.instructions.md"
    ],
    "next_steps": ["Validate the user-level setup in a second workspace."]
}
```
