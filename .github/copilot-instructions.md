# Notification summary handoff

Keep `.copilot/notify-summary.json` updated so the VS Code `Stop` hook can send a short Telegram notification after the current agent run finishes.

- The `SessionStart` hook creates `.copilot/notify-session.json` and a placeholder `.copilot/notify-summary.json`.
- Before you present a final answer for the current task, read `.copilot/notify-session.json` and overwrite `.copilot/notify-summary.json` with valid JSON for the current run.
- Copy the current `run_id` from `.copilot/notify-session.json` into `.copilot/notify-summary.json`.
- Always write English text and never include secrets, tokens, credentials, or private keys.
- Keep `summary` to one concise sentence, ideally under 240 characters.
- Use arrays for `details`, `changed_files`, and `next_steps`. Use `[]` when there is nothing to report.
- For read-only or explanation-only tasks, use `status: "info"`.
- Replace the file contents instead of appending logs or prose.

Use this JSON shape:

```json
{
  "version": 1,
  "run_id": "<value from .copilot/notify-session.json>",
  "updated_at": "2026-03-11T12:34:56.789Z",
  "status": "success",
  "summary": "Implemented Telegram notifications that include a Copilot-generated task summary.",
  "details": [
    "Initialized per-run summary state in .copilot/.",
    "Updated the Telegram hook to include summary fields when available."
  ],
  "changed_files": [
    "src/private/app/vscode-copilot-telegram-hook/scripts/telegram-notify.ps1",
    "src/private/app/vscode-copilot-telegram-hook/Install-UserCopilotHook.ps1"
  ],
  "next_steps": [
    "Test the hooks with multiple prompts in the same chat session."
  ]
}
```
