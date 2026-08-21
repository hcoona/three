# steam-account-history-to-csv Package Metadata

## Summary

`src/public/lib/steam-account-history-to-csv/package.json` is a public-path WXT
browser-extension project that is still marked private.

## Key Points

- The package sets `"private": true`.
- Its scripts focus on `wxt build`, `wxt zip`, and version stamping rather than
  npm publication.
- The project behaves more like an extension artifact producer than an npm
  package.

## Important Claims

- Not every project under `src/public/lib/` is intended for registry publishing.
- Browser-extension packaging may need a separate release path from npm package
  publication.

## Related Pages

- [Release Publish-Target Policy Script](./2026-04-21-release-policy-publish-targets-script.md)

## Open Questions

- Should buddy and official releases distribute extension archives only, or also
  use extension stores later?

## Source Location

- `src/public/lib/steam-account-history-to-csv/package.json`
