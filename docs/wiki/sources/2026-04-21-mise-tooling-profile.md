# mise Tooling Profile

## Summary

`mise.toml` declares the shared toolchain and environment variables for the
monorepo.

## Key Points

- It provisions runtimes and package managers for .NET, Python, Node.js, Ruby,
  Go, and PowerShell.
- `HK_PROFILE` is set to `small,medium`.
- No buddy or official release profile is defined here.

## Important Claims

- Tooling is already centralized for a future multi-language release pipeline.
- The current environment contract covers validation profiles, not release
  profiles.

## Related Pages

- [Repository Release Landscape](../analyses/repository-release-landscape.md)
- [HK Hook Profiles](./2026-04-21-hk-hook-profiles.md)

## Open Questions

- Should release profile selection become part of the repo-wide environment
  contract later?

## Source Location

- `mise.toml`
