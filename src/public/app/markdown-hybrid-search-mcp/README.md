# Markdown Hybrid Search MCP

This package will provide an MCP server that:

- scans one or more folders (provided via CLI args) for Markdown files;
- builds process-owned ephemeral indexes at startup (vector + full-text);
- exposes hybrid retrieval tools over MCP.

Implementation is intentionally left blank at this stage; see the
[design and source inputs](docs/README.md). Index lifetime follows the
[supplied v1.3 answers](.AGENTS/DESIGN/CLARIFY_REVIEW_v1_3.md); this summary
describes design intent, not implemented behavior.
