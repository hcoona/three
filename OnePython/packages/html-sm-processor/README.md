# HTML for SuperMemo Processor

This project will process HTML file for SuperMemo.

1. Convert SVG into PNG.
2. Render math into PNG.
    1. `<span class="math">`: Inlined math.
    2. `<div class="math">`: Standalone math, need to extract text excluding `<span>` inside.
3. Remove 3 other tabs (specialized for d2l.ai): `<div id="mxnet-1-0">`, `<div id="tensorflow-1-2">` and `<div id="paddle-1-3">`.

## Note

`cairosvg` on Windows requires `cairo` DLL file in `PATH`.
