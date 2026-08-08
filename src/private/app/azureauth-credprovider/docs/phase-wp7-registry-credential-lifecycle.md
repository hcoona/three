# Work package 7: registry credential lifecycle

WP7 manages Azure Artifacts credentials for npm, pnpm, and Yarn. User commands acquire a
credential through the configured provider. Azure Pipelines commands use `SYSTEM_ACCESSTOKEN`
directly. Credential text is written only to the selected package-manager configuration and is
never printed, logged, or stored in an ownership sidecar.

## Threat model

The implementation assumes:

- a normal local filesystem;
- an uncompromised operating system and user account;
- cooperative commands running as the same user; and
- the standard behavior of .NET filesystem APIs and OS file permissions.

It does not attempt to defend against malicious same-user races, hostile symbolic-link or reparse
point manipulation, inode or volume identity attacks, exotic filesystem semantics, or
crash/power-loss transactional guarantees across several files. Those defenses added substantial
proof, rollback, and recovery machinery without addressing a realistic product scenario.

Safety retained by the design is directly connected to ordinary use:

- secrets are absent from command output, plans, sidecars, and hashes;
- secret-bearing files use owner-only permissions where Unix modes apply;
- relative configuration and state paths are rejected, preventing accidental repository-local
  credential writes;
- unrelated user declarations, comments, newline style, and UTF-8 BOMs are preserved;
- removal targets only selectors recorded in a recognized ownership sidecar; and
- malformed or unrecognized ownership is left untouched.

## Paths and ownership

Credentials are written to the effective user configuration:

- npm and pnpm share `NPM_CONFIG_USERCONFIG`, `npm_config_userconfig`, or `~/.npmrc`;
- conflicting upper- and lowercase npm overrides are rejected; and
- Yarn 4+ uses an absolute `YARN_RC_FILENAME` when configured. A relative value affects
  project discovery, but AzureAuth user writes target `$HOME/.yarnrc.yml` according to Yarn 4
  user-configuration semantics.

npm and pnpm are one ownership group with one sidecar and one ordinary mutation lock. Yarn has a
separate ownership group. A recognized sidecar identifies the product, scope, resource, target,
and exact selectors. Lifecycle metadata contains only `issuedAt`, `expiresAt`, and
`refreshBefore`.

The lock serializes cooperative operations for an ownership group. Each changed file and sidecar
is written atomically through the existing filesystem abstraction. There is deliberately no
cross-file transaction, preclaim protocol, retained ownership proof, compare-and-swap hash,
rollback snapshot, adoption pass, or indeterminate-commit recovery.

## Mutation model

The configuration manager performs a small sequence:

1. validate and normalize the plan;
2. acquire the ownership-group lock;
3. load and recognize the sidecar, if present;
4. validate all requested selector changes;
5. edit each target using its exact selector;
6. atomically write changed files; and
7. atomically write or delete the resulting sidecar.

Existing selectors without recognized ownership are conflicts. Once ownership is recognized,
ordinary drift is reconciled: changed or missing owned selectors are updated or recreated during
`configure` and `refresh`. Removal does not compare the current secret value and preserves all
unrelated configuration.

The npmrc, Git config, and Yarn writers parse only the selectors the product owns. They retain
unrelated content, comments, BOM, and newline style. The Yarn writer supports the product's
generated `npmRegistryServer`, exact-registry `npmAuthToken`, and `npmAlwaysAuth` shape and rejects
an `npmAuthIdent` conflict.

## Lifecycle and commands

Lifecycle states are `fresh`, `refresh-recommended`, `expired`, `invalid`, and `missing`. User
credentials with a known expiry follow the normal refresh window. A user credential with unknown
expiry remains usable but is `refresh-recommended`; a job-scoped `SYSTEM_ACCESSTOKEN` with unknown
expiry is `fresh` for that job scope.

- `configure` is a no-op only when recognized ownership, expected selectors, physical state, and
  lifecycle are current. It checks secret selector presence, not secret equality.
- `refresh` reacquires a credential and reconciles recognized state.
- `status` and `doctor` report non-secret state and expiry. `refresh-recommended` is a warning and
  remains successful; expired, invalid, and incomplete state fail doctor.
- `unconfigure` and `logout` remove exact owned selectors and then remove the sidecar.
- `cleanup` processes npm/pnpm shared state once and removes recognized CI temporary state.

Malformed lifecycle metadata can be replaced during configure or refresh when ownership is still
recognized. Malformed or unrecognized ownership is reported as incomplete and left untouched.

## CI temporary state

CI plans include registry routing, a job-scoped temporary target, and activation output. npm and
pnpm activation selects the temporary user config. Yarn activation selects a temporary home.
Dry runs produce the same non-secret plan without acquiring credentials or writing files.

Cleanup removes a known product-owned temporary container after its owned files are removed. A
manifestless container is removed only when empty; nonempty unknown content is preserved and
reported as incomplete.

## Live package-manager invocation evidence

On 2026-07-30, the production apphost from commit `31e60f70` was exercised in WSL with an
isolated product configuration root, home, package-manager configuration, Corepack cache, and
working directory. It configured the public Azure Artifacts `PublicTools` npm registry and ran:

```text
azureauth-credprovider configure npm --registry-url <public-feed>
npm view @microsoft/artifacts-npm-credprovider version --registry <public-feed>

azureauth-credprovider configure pnpm --registry-url <public-feed>
pnpm view @microsoft/artifacts-npm-credprovider version --registry <public-feed>

azureauth-credprovider configure yarn --registry-url <public-feed>
corepack yarn@4.9.2 npm info @microsoft/artifacts-npm-credprovider --fields version --json
```

npm `11.9.0`, pnpm `11.17.0`, and Yarn `4.9.2` each resolved package version `1.1.3`. The
temporary Yarn project declared its non-secret scoped registry route; the product-owned user
configuration supplied the exact-registry auth selectors. The public feed is readable without
authentication, so this evidence validates real configured invocation paths but does not claim
private-feed authorization.

Product `unconfigure npm` removed the shared npm/pnpm selector, `unconfigure yarn` removed both
Yarn selectors, and identity unconfiguration removed the isolated binding and provider records.
Checks confirmed that no auth selector or ownership sidecar remained before the temporary root was
deleted. No token, account, or tenant identifier is recorded in this evidence.

## Maintenance tradeoff

This model favors understandable selector ownership and predictable cleanup over speculative
filesystem proofs. Failures propagate directly. A failure after one atomic file write may leave a
partially completed multi-file operation, which a later configure, refresh, unconfigure, or
cleanup reconciles using the recognized sidecar and current files. This limitation is explicit
and is preferable to maintaining a custom transaction system for unsupported threat scenarios.
