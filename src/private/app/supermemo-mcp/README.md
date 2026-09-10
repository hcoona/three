# SuperMemo MCP

This package provides MCP to enable LLMs interactive with SuperMemo 18/19.

## Top-Level Windows

The most important 2 windows are:

1. `ClassName: TElWind`: Main content window.
2. `ClassName: TContents`: Knowledge tree window.

## IE Control Chain

It seems that the `Internet Explorer_Server` is always loading from a temporary file rather than the real backend file.

It seems that the modifications will be cached in `Shell DocObject View` rather than `Internet Explorer_Server`.

1. `TElWind`
2. `TScrollBox`
3. `Shell Embedding`
4. `Shell DocObject View`
5. `Internet Explorer_Server`

## Operational Knowledge and Sources

The existing knowledge base and script documentation serve the Windows
automation investigation:

- [Element data and clipboard encoding](knowledge_base/intro.md) includes a
  captured element example.
- [Window classes](knowledge_base/windows.md) and
  [menu ID notes](knowledge_base/context_menu_id.md) retain the observed names
  and IDs, including incomplete investigation notes.
- [Automation log](knowledge_base/@AutomationLog.txt) retains the source
  observations; it is not a requirements specification.
- [Menu monitor usage](scripts/README.md) describes the diagnostic script and
  credits its AutoHotkey reference.
- [Menu enumeration discussion](scripts/Note1.md) retains the attributed
  human/assistant exchange as investigation input. Its suggestions are not
  verified Windows behavior merely because the discussion is stored here.

Project authors maintain the derived operational guidance; implementers use
the source observations to evaluate automation behavior without rewriting the
captured inputs as product promises.
