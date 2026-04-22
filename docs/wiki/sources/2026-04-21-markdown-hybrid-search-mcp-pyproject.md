# markdown-hybrid-search-mcp Metadata

## Summary

`src/public/app/markdown-hybrid-search-mcp/pyproject.toml` describes a public
path Python app that is still explicitly private in package metadata.

## Key Points

- The project lives under `src/public/app/`.
- It uses Hatchling as the build backend.
- It declares `classifiers = ["Private :: Do Not Upload"]`.

## Important Claims

- Path-based visibility and package-metadata visibility are currently in
  tension for this app.
- Any future release matrix needs an explicit publishability field rather than
  inferring everything from the directory tree.

## Related Pages

- [Repository Release Landscape](../analyses/repository-release-landscape.md)
- [Root Python Workspace](./2026-04-21-root-pyproject-python-workspace.md)

## Open Questions

- Is this app intended for public binary release, public package release, or
  internal use only?

## Source Location

- `src/public/app/markdown-hybrid-search-mcp/pyproject.toml`
