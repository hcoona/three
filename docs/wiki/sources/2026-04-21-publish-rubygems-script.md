# RubyGems Trusted Publishing Script

## Summary

`eng/scripts/publish_rubygems_org_idempotent.sh` publishes Ruby gems with
rerun-safe digest checks and explicitly expects trusted publishing or OIDC
credential setup from its caller.

## Key Points

- The script does not configure credentials itself.
- It tells callers to use `rubygems/configure-rubygems-credentials` with
  trusted publishing.
- It validates whether a version already exists and whether the remote digest
  matches the local artifact.

## Important Claims

- The repo already has an explicit passwordless publishing pattern in one
  language stack.
- Similar credential handling should be mirrored for future NuGet and npm/PyPI
  publication wherever possible.

## Related Pages

- [Repository Release Landscape](../analyses/repository-release-landscape.md)
- [Release Publish-Target Policy Script](./2026-04-21-release-policy-publish-targets-script.md)

## Open Questions

- Which registries besides RubyGems should adopt the same rerun-safe pattern?

## Source Location

- `eng/scripts/publish_rubygems_org_idempotent.sh`
