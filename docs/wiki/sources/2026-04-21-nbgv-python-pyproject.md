# nbgv-python Metadata

## Summary

`src/public/lib/nbgv-python/pyproject.toml` is the clearest public Python
library release candidate in the repo.

## Key Points

- It declares a stable project name and version.
- It includes repository, issues, documentation, and changelog URLs.
- It exposes both a console script and a Hatch entry point.
- It is not marked `Private :: Do Not Upload`.

## Important Claims

- The repository already has at least one Python project that looks ready for
  public package publication.
- Python public-release logic should start with this package before considering
  public-path apps that are still private in metadata.

## Related Pages

- [Repository Release Landscape](../analyses/repository-release-landscape.md)
- [Root Python Workspace](./2026-04-21-root-pyproject-python-workspace.md)

## Open Questions

- Should buddy releases for Python packages publish to TestPyPI or remain
  GitHub Release-only?

## Source Location

- `src/public/lib/nbgv-python/pyproject.toml`
