# nbgv-python Metadata

## Summary

`src/public/lib/nbgv-python/pyproject.toml` is the clearest current-scope
special-support Python packaging metadata example in the repo. Unlike the
normal Python contract, which expects build-system-integrated NBGV, this one
named exception uses the checked-in `pyproject.toml` `[project].version` as its
authoritative release version.

## Key Points

- It declares a stable project name and a checked-in static version.
- It includes repository, issues, documentation, and changelog URLs.
- It exposes both a console script and a Hatch entry point.
- It is not marked `Private :: Do Not Upload`.

## Important Claims

- The repository already has normal Python NBGV integration examples, but
  `nbgv-python` remains a named special-support exception rather than evidence
  that manifest-version fallback is generally allowed.
- `nbgv-python` is in first-delivery scope under that explicit exception path:
  planner authority comes from the selected commit's checked-in
  `pyproject.toml` version, and later build and publish stages must preserve
  that same frozen project-scoped version identity.

## Related Pages

- [Repository Release Landscape](../analyses/repository-release-landscape.md)
- [Root Python Workspace](./2026-04-21-root-pyproject-python-workspace.md)

## Settled Note

- Python `buddy` is GitHub Release-only; Python package publication, when
  declared for `official`, uses PyPI; TestPyPI is not part of the current
  baseline.

## Source Location

- `src/public/lib/nbgv-python/pyproject.toml`
