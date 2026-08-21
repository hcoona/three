# Root pnpm Workspace

## Summary

The root `package.json` defines the repository-level pnpm workspace scripts and
is itself marked private.

## Key Points

- The root package is not publishable.
- Workspace scripts fan out `build`, `test`, `lint`, and `format` to child
  packages.
- No root release or publish script exists for JavaScript packages.

## Important Claims

- JS/TS package release automation has not yet been centralized at the root.
- Release behavior must currently be inferred from child `package.json` files or
  future workflows.

## Related Pages

- [hexo-renderer-asciidoc Package Metadata](./2026-04-21-hexo-renderer-asciidoc-package-json.md)
- [steam-account-history-to-csv Package Metadata](./2026-04-21-steam-account-history-to-csv-package-json.md)

## Open Questions

- Will buddy and official publishing be orchestrated from workflows only, or
  also via root package scripts?

## Source Location

- `package.json`
