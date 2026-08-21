# README Monorepo Layout

## Summary

The root `README.md` defines the repository as a polyglot monorepo and explains
the canonical `src/` and `tests/` layout used for active projects.

## Key Points

- `src/` is split into `lab/`, `private/`, `public/`, and `sample/`.
- The root pnpm workspace currently includes two public packages and three
  private proof-of-concept packages.
- The root uv workspace is authoritative for Python project membership.
- The root `dirs.proj` is the active .NET traversal entry point.

## Important Claims

- The repo is intended to share CI, release, and security policies across
  languages.
- Active projects have already been normalized into the canonical root layout.

## Related Pages

- [Root Python Workspace](./2026-04-21-root-pyproject-python-workspace.md)
- [Root pnpm Workspace](./2026-04-21-root-package-json-pnpm-workspace.md)

## Open Questions

- Which public projects will get first-class automated release workflows first?

## Source Location

- `README.md`
