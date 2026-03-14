---
name: copilot-notify-summary
description: >-
    Keep the session-scoped notification summary file updated so the VS Code
    GitHub Copilot Telegram notification hook can send a task summary when the
    current chat turn stops.
applyTo: '**'
---

<!-- managed-by: hcoona-vscode-copilot-telegram-hook -->

# Notification summary handoff

If the hook context tells you that notification summary handoff is enabled for
the current workspace, maintain the referenced session-scoped
`notify-summary.json` file before you finish the current task.

- Read the referenced session-scoped `notify-turn.json` file and copy its
  `session_id` and `turn_id` into the matching `notify-summary.json` file.
- Overwrite the referenced session-scoped `notify-summary.json` file with valid
  JSON. Do not append logs, prose, or Markdown.
- Set `updated_at` to the current UTC time in the
  `yyyy-MM-ddTHH:mm:ss.fffZ` format.
- Write the `summary` field as concise human-readable text. Prefer Chinese on a
  best-effort basis.
- Never include secrets, tokens, credentials, or private keys.
- Keep `summary` to one concise sentence, ideally under 240 characters.
- Use arrays for `details`, `changed_files`, and `next_steps`. Use `[]` when
  there is nothing to report.
- If `details` or `next_steps` contain human-readable text, prefer Chinese so
  the notification stays readable end-to-end.
- Use `status: "info"` for read-only or explanation-only tasks.

The hook context should provide the exact session-scoped paths, for example
under `.copilot/sessions/<session_id>/`.

Use this JSON shape:

```json
{
    "version": 1,
    "session_id": "<value from notify-turn.json>",
    "turn_id": "<value from notify-turn.json>",
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
