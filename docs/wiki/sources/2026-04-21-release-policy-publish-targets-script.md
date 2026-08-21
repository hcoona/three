# Release Publish-Target Policy Script

## Summary

`eng/scripts/release_orchestrate_policy_publish_targets.sh` encodes channel- and
language-aware publish-target policy for release orchestration.

## Key Points

- The script already understands `official` and `buddy` channels.
- It validates publish targets for Node.js, Python, Ruby, and WXT projects.
- It warns that caller workflows must grant `packages: write` when publishing to
  GitHub Packages.
- It does not yet define any C# or NuGet-specific publish-target policy.

## Important Claims

- A release policy layer already exists even though the workflow layer is still
  missing in this checkout.
- Future C# support should be added here rather than invented ad hoc in each
  workflow.

## Related Pages

- [RubyGems Trusted Publishing Script](./2026-04-21-publish-rubygems-script.md)

## Open Questions

- How should NuGet.org and GitHub Packages rules map onto buddy and official
  channels for C# projects?

## Source Location

- `eng/scripts/release_orchestrate_policy_publish_targets.sh`
