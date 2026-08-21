# HK Hook Profiles

## Summary

`hk.pkl` configures repository validation steps and currently distinguishes only
`small` and `medium` profiles.

## Key Points

- Markdown, JS/TS, Python, PowerShell, TOML, and .NET validation are all wired
  through HK steps.
- `.github/workflows/**` is excluded from general linting, while `actionlint`
  is scoped specifically to workflow files when they exist.
- There is no buddy or official profile concept in HK today.

## Important Claims

- The repository already has a strong validation gate, but not a release gate.
- Release orchestration will likely live outside HK or in additional scripts.

## Related Pages

- [mise Tooling Profile](./2026-04-21-mise-tooling-profile.md)

## Open Questions

- Should HK eventually validate release manifests or release matrix metadata?

## Source Location

- `hk.pkl`
