# nbgv-python Metadata

## Summary

`src/public/lib/nbgv-python/pyproject.toml` is the clearest public Python
packaging metadata example in the repo, but it is not yet a current-scope
PyPI-ready release candidate because it remains a bootstrap-special case: the
normal current-scope contract expects build-system-integrated NBGV, so this
package needs a separate bootstrap path before it can become PyPI-ready under
that contract.

## Key Points

- It declares a stable project name and a checked-in static version.
- It includes repository, issues, documentation, and changelog URLs.
- It exposes both a console script and a Hatch entry point.
- It is not marked `Private :: Do Not Upload`.

## Important Claims

- The repository already has at least one Python project with clearly public
  package metadata, but the current repo state still leaves Python rollout work
  because this project is still a bootstrap-special case before it becomes
  current-scope PyPI-ready.
- `nbgv-python` should not be framed as the immediate first PyPI rollout
  candidate under the normal current-scope rules; it first needs a separate
  bootstrap path, after which it can be reconsidered alongside the rest of the
  PyPI-targeted contract.

## Related Pages

- [Repository Release Landscape](../analyses/repository-release-landscape.md)
- [Root Python Workspace](./2026-04-21-root-pyproject-python-workspace.md)

## Settled Note

- Python `buddy` is GitHub Release-only; Python package publication, when
  declared for `official`, uses PyPI; TestPyPI is not part of the current
  baseline.

## Source Location

- `src/public/lib/nbgv-python/pyproject.toml`
