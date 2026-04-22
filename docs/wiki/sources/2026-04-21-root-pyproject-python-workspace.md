# Root Python Workspace

## Summary

The root `pyproject.toml` defines the uv workspace membership for Python
projects and marks the root manifest itself as private.

## Key Points

- Workspace members span `lab`, `private/app`, `public/app`, `public/lib`, and
  `sample`.
- The root project uses `classifiers = ["Private :: Do Not Upload"]`.
- `src/public/lib/nbgv-python` is the only clearly public Python library in the
  workspace.
- `src/public/app/markdown-hybrid-search-mcp` lives under `public/app` but is
  also marked `Private :: Do Not Upload`.

## Important Claims

- Python release decisions cannot be inferred from visibility alone; package
  metadata can override the path-based expectation.
- The workspace membership file is the best repo-wide inventory source for
  Python projects.

## Related Pages

- [Repository Release Landscape](../analyses/repository-release-landscape.md)
- [nbgv-python Metadata](./2026-04-21-nbgv-python-pyproject.md)
- [markdown-hybrid-search-mcp Metadata](./2026-04-21-markdown-hybrid-search-mcp-pyproject.md)

## Open Questions

- Should `markdown-hybrid-search-mcp` remain private despite living under
  `src/public/app/`?

## Source Location

- `pyproject.toml`
