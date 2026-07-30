# Work package 7: registry credential lifecycle

WP7 manages npm, pnpm, and Yarn credentials for Azure Artifacts. User commands acquire
credentials through the configured provider with browser interaction allowed. Azure Pipelines
commands use `SYSTEM_ACCESSTOKEN` directly and do not load the provider. Token text is never
printed.

## Configuration and ownership

Credentials are written to the package manager's effective user configuration:

- npm and pnpm share `NPM_CONFIG_USERCONFIG`, `npm_config_userconfig`, or `~/.npmrc`;
- differing upper- and lowercase npm overrides are rejected;
- Yarn uses `YARN_RC_FILENAME` under the effective home, or `.yarnrc.yml`.

npm and pnpm form one ownership group and use one canonical sidecar. Yarn has its own sidecar.
The existing ownership manifest identifies the product, scope, target, resource, and exact
selectors. Lifecycle metadata in `SafeMetadata` contains only `issuedAt`, `expiresAt`, and
`refreshBefore`.

Existing unrelated declarations, comments, and files are preserved. CI plans include registry
routing, a product-owned temporary target, and activation output. npm and pnpm activation selects
the temporary user config; Yarn activation selects the temporary home. Dry runs render the same
plan without acquiring credentials or writing files.

## Lifecycle and commands

Lifecycle states are `fresh`, `refresh-recommended`, `expired`, `invalid`, and `missing`. User
credentials require an expiry. A missing expiry is valid only for a job-scoped
`SYSTEM_ACCESSTOKEN`.

`configure` is a no-op only when the canonical manifest is recognized, its resource, target, and
selectors match the current plan, the target exists, and lifecycle timestamps are fresh. It does
not compare secret values or hash target contents. `refresh` always reacquires and applies.

When a valid owned configuration moves to another path or resource, the old exact selectors are
removed through the standard configuration manager before the new plan is applied. Unrecognized
state fails clearly rather than being adopted or repaired. `status` and `doctor` report lifecycle
state; `doctor` also reports the non-secret expiry timestamp.

`unconfigure`, `cleanup`, and `logout` remove the exact selectors from a recognized manifest,
preserve unrelated content, and then remove the sidecar. Malformed lifecycle timestamps do not
prevent removal when the manifest ownership itself is valid. Malformed or unrecognized manifests
are reported as incomplete and left untouched.

For a known product-owned CI container with no manifest, cleanup removes it only when it is empty.
A nonempty manifestless container is incomplete and remains untouched. npm and pnpm shared state
is processed once during aggregate cleanup.

Writes and removals use the configuration manager's normal atomic mutation behavior. The design
assumes a normal local filesystem and cooperative same-user commands; it does not add
cross-process locking, crash recovery, legacy sidecar migration, or speculative malformed-state
recovery.
