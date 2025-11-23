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
