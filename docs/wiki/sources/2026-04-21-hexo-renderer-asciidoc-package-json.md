# hexo-renderer-asciidoc Package Metadata

## Summary

`src/public/lib/hexo-renderer-asciidoc/package.json` is a publishable public
Node.js package with explicit packaging metadata and publish-time scripts.

## Key Points

- The package is not marked `private`.
- It defines `files`, `exports`, repository metadata, and peer dependencies.
- `prepack` and `prepublishOnly` prepare a distributable package and build the
  output.

## Important Claims

- This is the clearest current npm-style package release candidate in the repo.
- Node.js package publication already has enough local metadata to support both
  GitHub Packages and npmjs-style channels later.

## Related Pages

- [Repository Release Landscape](../analyses/repository-release-landscape.md)
- [Root pnpm Workspace](./2026-04-21-root-package-json-pnpm-workspace.md)

## Open Questions

- Should buddy releases go to GitHub Packages only, or also to npm dist-tags?

## Source Location

- `src/public/lib/hexo-renderer-asciidoc/package.json`
