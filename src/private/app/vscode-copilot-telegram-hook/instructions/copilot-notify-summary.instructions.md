---
name: copilot-notify-summary
description: >-
    Keep .copilot/notify-summary.json updated so the VS Code GitHub Copilot
    Telegram notification hook can send a task summary when the current chat turn
    stops.
applyTo: '**'
---

<!-- managed-by: hcoona-vscode-copilot-telegram-hook -->

# Notification summary handoff

If the current workspace contains `.copilot/notify-session.json`, maintain
`.copilot/notify-summary.json` before you finish the current task.

- Read `.copilot/notify-session.json` and copy its `run_id` into
  `.copilot/notify-summary.json`.
- Overwrite `.copilot/notify-summary.json` with valid JSON. Do not append logs,
  prose, or Markdown.
- Set `updated_at` to the current UTC time in the
  `yyyy-MM-ddTHH:mm:ss.fffZ` format.
- Write the `summary` field in concise Chinese.
- Never include secrets, tokens, credentials, or private keys.
- Keep `summary` to one concise sentence, ideally under 240 characters.
- Use arrays for `details`, `changed_files`, and `next_steps`. Use `[]` when
  there is nothing to report.
- If `details` or `next_steps` contain human-readable text, prefer Chinese so
  the notification stays readable end-to-end.
- Use `status: "info"` for read-only or explanation-only tasks.

Use this JSON shape:

```json
{
    "version": 1,
    "run_id": "<value from .copilot/notify-session.json>",
    "updated_at": "2026-03-11T12:34:56.789Z",
    "status": "success",
    "summary": "已实现一个 VS Code GitHub Copilot Telegram 通知钩子，并补齐当前任务的摘要交接。",
    "details": ["已安装用户级 hooks 与 instructions。", "通知运行时现在会读取当前任务写出的摘要文件。"],
    "changed_files": [
        "src/private/app/vscode-copilot-telegram-hook/VSCodeCopilotTelegramHook.csproj",
        "src/private/app/vscode-copilot-telegram-hook/instructions/template.md"
    ],
    "next_steps": ["在另一处工作区验证用户级安装是否生效。"]
}
```
