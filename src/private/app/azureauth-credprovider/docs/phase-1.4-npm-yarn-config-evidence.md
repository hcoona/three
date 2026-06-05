# Phase 1.4 npm, pnpm, and Yarn Configuration Evidence Gate

Status: **Accepted with write-policy constraints**

Date: **2026-06-05**

Decision ID: **phase-1.4-npm-yarn-config-evidence**

Gate name: **Phase 1.4 npm, pnpm, and Yarn configuration update gate**

Owner: **ADAPTER-NPM and CONFIG**

## Gate Status and Decision

| Field                      | Decision                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Gate status                | Passed for npm, pnpm, and Yarn Berry configuration write planning, including scoped registry credential target selection.                                                                                                                                                                                                                                                                                                                                    |
| Decision                   | Implement npm-compatible credential writes as configuration-manager-owned change plans. npm and pnpm use `.npmrc` entries scoped by registry URL. Yarn Berry uses `.yarnrc.yml` `npmRegistries` auth entries while reading registry declarations from `npmRegistryServer` and `npmScopes`.                                                                                                                                                                   |
| Evidence scope             | Local reference package inspection covers `@microsoft/artifacts-npm-credprovider` 1.1.3 and `@microsoft/artifacts-credprovider-wrapper` 1.1.4 package metadata, declarations, README guidance, and bundled JavaScript behavior snippets as supporting references with auditable registry shasums. Disposable probes cover npm 11.9.0, pnpm 10.34.1, and Yarn 4.9.2 config resolution and default plus scoped write targets with fake credential values only. |
| Implementation may proceed | Yes for Phase 12 npm/pnpm change-plan generation and Phase 13B Yarn change-plan generation, including scoped registry credential targets, subject to the credential write target and atomicity policies in this record. The adapter must not write files directly; persistent and temporary files must be applied by the configuration manager.                                                                                                              |
| Phase 1R routing           | Not entered. Yarn write support is accepted for user-level and CI temporary scopes. If later platform validation disproves Yarn consumption of configuration-manager-owned `.yarnrc.yml` targets, Yarn writes must stop and enter Phase 1R unless the requirement is explicitly changed.                                                                                                                                                                     |

## Reference Package Snapshot

The local npm references are extracted packages rather than Git working trees.
`git -C` did not find a repository at either package path, so the extracted
packages are not authoritative source snapshots. They are accepted only as
supporting references for package shape and bundled runtime behavior; the gate
pass relies on the disposable npm, pnpm, and Yarn package-manager probes below.

Package provenance that was auditable from package metadata and registry
metadata:

| Package                                     | Local path                                         | Version | Registry source                                                                                   | Shasum                                     | Integrity                                                                                         |
| ------------------------------------------- | -------------------------------------------------- | ------- | ------------------------------------------------------------------------------------------------- | ------------------------------------------ | ------------------------------------------------------------------------------------------------- |
| `@microsoft/artifacts-npm-credprovider`     | `/workspace/public/artifacts-npm-credprovider`     | 1.1.3   | `https://pkgs.dev.azure.com/artifacts-public/PublicTools/_packaging/AzureArtifacts/npm/registry/` | `3754f2e8e5ccd43d66bd2b4ac9840d3468f9c470` | `sha512-WNcO7LUxr1W3Hu1K7HE+l94ob6ya+6/UKbycVRu+RUGELbH9AO01WOYeakp+rjINp05u/icMhj6aXFG6cQ9fjA==` |
| `@microsoft/artifacts-credprovider-wrapper` | `/workspace/public/artifacts-credprovider-wrapper` | 1.1.4   | `https://pkgs.dev.azure.com/artifacts-public/PublicTools/_packaging/AzureArtifacts/npm/registry/` | `71153852540c03f261c607b048c1ea931bd7bd19` | `sha512-uMrQO/u0WgftQy9hCeRS6ycITz2bkCZ2ps+QG+IVnrKZuTwsxvJ6X2+X0PdkBUELQg8a+uCHZXBAor9dP7Dm0g==` |

Exact metadata fetch commands:

```bash
REGISTRY=https://pkgs.dev.azure.com/artifacts-public/PublicTools/_packaging/AzureArtifacts/npm/registry/
npm view --registry "$REGISTRY" @microsoft/artifacts-npm-credprovider@1.1.3 dist --json
npm view --registry "$REGISTRY" @microsoft/artifacts-credprovider-wrapper@1.1.4 dist --json
npm pack --dry-run --json --registry "$REGISTRY" @microsoft/artifacts-npm-credprovider@1.1.3
npm pack --dry-run --json --registry "$REGISTRY" @microsoft/artifacts-credprovider-wrapper@1.1.4
```

Results:

```json
{
  "shasum": "3754f2e8e5ccd43d66bd2b4ac9840d3468f9c470",
  "tarball": "https://pkgs.dev.azure.com/artifacts-public/PublicTools/_packaging/AzureArtifacts/npm/registry/@microsoft/artifacts-npm-credprovider/-/artifacts-npm-credprovider-1.1.3.tgz"
}
{
  "shasum": "71153852540c03f261c607b048c1ea931bd7bd19",
  "tarball": "https://pkgs.dev.azure.com/artifacts-public/PublicTools/_packaging/AzureArtifacts/npm/registry/@microsoft/artifacts-credprovider-wrapper/-/artifacts-credprovider-wrapper-1.1.4.tgz"
}
[
  {
    "filename": "microsoft-artifacts-npm-credprovider-1.1.3.tgz",
    "integrity": "sha512-WNcO7LUxr1W3Hu1K7HE+l94ob6ya+6/UKbycVRu+RUGELbH9AO01WOYeakp+rjINp05u/icMhj6aXFG6cQ9fjA==",
    "shasum": "3754f2e8e5ccd43d66bd2b4ac9840d3468f9c470"
  }
]
[
  {
    "filename": "microsoft-artifacts-credprovider-wrapper-1.1.4.tgz",
    "integrity": "sha512-uMrQO/u0WgftQy9hCeRS6ycITz2bkCZ2ps+QG+IVnrKZuTwsxvJ6X2+X0PdkBUELQg8a+uCHZXBAor9dP7Dm0g==",
    "shasum": "71153852540c03f261c607b048c1ea931bd7bd19"
  }
]
```

Commands used to identify the local snapshot:

```bash
git -C /workspace/public/artifacts-npm-credprovider --no-pager rev-parse HEAD
git -C /workspace/public/artifacts-credprovider-wrapper --no-pager rev-parse HEAD
```

Results:

```text
artifacts-npm-credprovider: not a Git repository
artifacts-credprovider-wrapper: not a Git repository
```

Metadata and declarations inspected:

- `/workspace/public/artifacts-npm-credprovider/package.json` lines 1-28:
  package name, version 1.1.3, source homepage, CLI bin, and dependency on
  `@microsoft/artifacts-credprovider-wrapper` `^1.1.4`.
- `/workspace/public/artifacts-npm-credprovider/README.md` lines 28-75:
  npm and pnpm project setup, `pnpm:devPreinstall`, workspace discovery, and
  `--config-file` guidance for `.npmrc` or `.yarnrc.yml`.
- `/workspace/public/artifacts-npm-credprovider/lib/fileProvider.d.ts` lines 11-23:
  reference provider shape with workspace file path, user file path, registry
  discovery, auth-entry discovery, and registry-entry write methods.
- `/workspace/public/artifacts-npm-credprovider/lib/npmrc/npmrcFileProvider.d.ts`
  lines 2-23: `.npmrc` provider methods for user file preparation, registry
  discovery, auth-entry discovery, and registry-entry writes.
- `/workspace/public/artifacts-npm-credprovider/lib/yarnrc/yarnrcFileProvider.d.ts`
  lines 2-14: `.yarnrc.yml` provider methods and registry discovery from
  `npmRegistryServer` and `npmScopes`.
- `/workspace/public/artifacts-credprovider-wrapper/package.json` lines 1-13 and
  43-60: wrapper package version, bin shape, main module, and postinstall
  behavior.

Bundled JavaScript snippets were inspected because the extracted package does
not include TypeScript source. The minified bundle confirms these behaviors:

- The base file provider computes `userFilePath` as `HOME` or `USERPROFILE`
  plus the workspace file name and computes `workspaceFilePath` from the
  workspace root unless an explicit config file path is supplied.
- The npm provider overrides `userFilePath` with `NPM_CONFIG_USERCONFIG` when
  present, reads registry declarations from workspace `.npmrc` first, falls back
  to user `.npmrc`, and writes registry-scoped `_authToken` entries to the user
  file.
- The Yarn provider reads registry declarations from workspace `.yarnrc.yml`
  `npmRegistryServer` and `npmScopes`, falls back to user `.yarnrc.yml`, and
  writes `npmRegistries` entries with `npmAlwaysAuth` and `npmAuthToken` to the
  user file.
- The orchestrator constructs both npm and Yarn file providers and writes only
  after validation determines credentials need refresh.

## Disposable Prototype Environment

All disposable probes were run from the repository root:
`/workspace/three-workspaces/azureauth-credprovider`.

Variableized fixture root used below:

```bash
ROOT="$PWD"
SCRATCH="$ROOT/.copilot-scratch/npm-yarn-config-probe"
```

Actual fixture paths:

| Fixture                          | Path                            |
| -------------------------------- | ------------------------------- |
| Probe project                    | `$SCRATCH/project`              |
| Probe project npm config         | `$SCRATCH/project/.npmrc`       |
| Probe project Yarn config        | `$SCRATCH/project/.yarnrc.yml`  |
| Probe user home                  | `$SCRATCH/home`                 |
| Probe user npm config            | `$SCRATCH/home/.npmrc`          |
| Probe user Yarn config           | `$SCRATCH/home/.yarnrc.yml`     |
| Probe CI npm userconfig          | `$SCRATCH/npmrc-ci`             |
| Probe CI Yarn home               | `$SCRATCH/ci-home`              |
| Probe CI Yarn home config        | `$SCRATCH/ci-home/.yarnrc.yml`  |
| Probe `YARN_RC_FILENAME` file    | `$SCRATCH/temporary.yarnrc.yml` |
| Isolated `YARN_RC_FILENAME` home | `$SCRATCH/fresh-home`           |
| Probe Corepack cache             | `$SCRATCH/corepack`             |

Fixture creation commands:

```bash
rm -rf "$SCRATCH"
mkdir -p "$SCRATCH/project" "$SCRATCH/home" "$SCRATCH/corepack" "$SCRATCH/ci-home"
cat > "$SCRATCH/project/package.json" <<'EOF'
{"name":"phase-14-probe","packageManager":"yarn@4.9.2"}
EOF
cat > "$SCRATCH/project/.npmrc" <<'EOF'
registry=https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/
@scope:registry=https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/
always-auth=true
EOF
cat > "$SCRATCH/home/.npmrc" <<'EOF'
//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken=fake-token
EOF
cat > "$SCRATCH/project/.yarnrc.yml" <<'EOF'
npmRegistryServer: 'https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/'
npmScopes:
  scope:
    npmRegistryServer: 'https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/'
EOF
cat > "$SCRATCH/home/.yarnrc.yml" <<'EOF'
npmRegistries:
  'https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/':
    npmAlwaysAuth: true
    npmAuthToken: fake-token
EOF
```

Tool version transcript:

<!-- markdownlint-disable MD013 -->

```text
$ node -v
v24.14.0
exit=0
$ npm -v
11.9.0
exit=0
$ pnpm -v
10.34.1
exit=0
$ corepack --version
0.34.6
exit=0
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/home" corepack yarn@4.9.2 --version
4.9.2
exit=0
```

Reference package Git probe transcript:

```text
$ git -C /workspace/public/artifacts-npm-credprovider --no-pager rev-parse HEAD
fatal: not a git repository (or any parent up to mount point /)
Stopping at filesystem boundary (GIT_DISCOVERY_ACROSS_FILESYSTEM not set).
exit=128
$ git -C /workspace/public/artifacts-credprovider-wrapper --no-pager rev-parse HEAD
fatal: not a git repository (or any parent up to mount point /)
Stopping at filesystem boundary (GIT_DISCOVERY_ACROSS_FILESYSTEM not set).
exit=128
```

## npm and pnpm Evidence

### User-Level `.npmrc` Resolution

The workspace `.npmrc` declared the registry URLs, and the user `.npmrc` held
only the fake token. npm hides protected auth keys in JSON output, so the JSON
snapshot below proves registry selection but is not used as the npm token
presence proof. pnpm returns the fake token value.

```text
$ HOME="$SCRATCH/home" npm --prefix "$SCRATCH/project" config get registry
npm warn Unknown project config "always-auth". This will stop working in the next major version of npm.
https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/
exit=0
$ HOME="$SCRATCH/home" npm --prefix "$SCRATCH/project" config get @scope:registry
npm warn Unknown project config "always-auth". This will stop working in the next major version of npm.
https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/
exit=0
$ HOME="$SCRATCH/home" npm --prefix "$SCRATCH/project" config list --json | node -e 'const fs=require("node:fs"); const c=JSON.parse(fs.readFileSync(0,"utf8")); console.log(JSON.stringify({registry:c.registry,scopeRegistry:c["@scope:registry"],authToken:c["//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken"] ?? "<hidden-or-absent>"}, null, 2));'
npm warn Unknown project config "always-auth". This will stop working in the next major version of npm.
{
  "registry": "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/",
  "scopeRegistry": "https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/",
  "authToken": "<hidden-or-absent>"
}
exit=0
$ HOME="$SCRATCH/home" pnpm --dir "$SCRATCH/project" config list --json | node -e 'const fs=require("node:fs"); const c=JSON.parse(fs.readFileSync(0,"utf8")); console.log(JSON.stringify({registry:c.registry,scopeRegistry:c["@scope:registry"],authToken:c["//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken"] ?? "<hidden-or-absent>"}, null, 2));'
{
  "registry": "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/",
  "scopeRegistry": "https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/",
  "authToken": "fake-token"
}
exit=0
```

A follow-up npm-only discriminating probe used fake tokens and `npm config list
--long` with a selected `--userconfig`. npm redacts protected auth values as
`(protected)` when the scoped key is present, and the same filtered query returns
no line when the key is absent. This distinguishes token-present from
token-absent without exposing credential material or performing a package
operation.

```bash
cat > "$SCRATCH/present.npmrc" <<'EOF'
//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken=fake-discriminating-token
EOF
: > "$SCRATCH/absent.npmrc"
```

Transcript:

```text
$ HOME="$SCRATCH/home" npm --prefix "$SCRATCH/project" --userconfig "$SCRATCH/present.npmrc" config list --long | grep -F '//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken'
//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken = (protected)
grep exit=0
$ HOME="$SCRATCH/home" npm --prefix "$SCRATCH/project" --userconfig "$SCRATCH/absent.npmrc" config list --long | grep -F '//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken'
grep exit=1
```

### Scoped npm and pnpm Credential Target Selection

A scoped target probe used the existing project declaration
`@scope:registry=https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/`
and added both default-registry and scoped-registry fake tokens to the selected
user config. A negative home contained only the default-registry token. The
outputs redact token values and prove that scoped credentials are matched by the
scoped registry URL, not by the default registry credential.

Additional setup:

```bash
mkdir -p "$SCRATCH/home-negative"
cat > "$SCRATCH/home/.npmrc" <<'EOF'
//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken=fake-default-token
//pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/:_authToken=fake-scoped-token
EOF
cat > "$SCRATCH/home-negative/.npmrc" <<'EOF'
//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken=fake-default-token
EOF
```

Transcript:

```text
$ HOME="$SCRATCH/home" npm --prefix "$SCRATCH/project" config get @scope:registry
https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/
exit=0
$ HOME="$SCRATCH/home" npm --prefix "$SCRATCH/project" config list --long | grep -F "//pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/:_authToken"
//pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/:_authToken = (protected)
grep exit=0
$ HOME="$SCRATCH/home" npm --prefix "$SCRATCH/project" config list --long | grep -F "//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken"
//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken = (protected)
grep exit=0
$ HOME="$SCRATCH/home-negative" npm --prefix "$SCRATCH/project" config list --long | grep -F "//pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/:_authToken"
grep exit=1
$ HOME="$SCRATCH/home" pnpm --dir "$SCRATCH/project" config list --json | node -e 'const fs=require("node:fs"); const c=JSON.parse(fs.readFileSync(0,"utf8")); const scoped="//pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/:_authToken"; const def="//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken"; console.log(JSON.stringify({scopeRegistry:c["@scope:registry"],scopedAuthToken:c[scoped] ? "<present>" : "<absent>",defaultAuthToken:c[def] ? "<present>" : "<absent>"}, null, 2));'
{
  "scopeRegistry": "https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/",
  "scopedAuthToken": "<present>",
  "defaultAuthToken": "<present>"
}
pipe exit=0 0
$ HOME="$SCRATCH/home-negative" pnpm --dir "$SCRATCH/project" config list --json | node -e 'const fs=require("node:fs"); const c=JSON.parse(fs.readFileSync(0,"utf8")); const scoped="//pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/:_authToken"; const def="//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken"; console.log(JSON.stringify({scopeRegistry:c["@scope:registry"],scopedAuthToken:c[scoped] ? "<present>" : "<absent>",defaultAuthToken:c[def] ? "<present>" : "<absent>"}, null, 2));'
{
  "scopeRegistry": "https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/",
  "scopedAuthToken": "<absent>",
  "defaultAuthToken": "<present>"
}
pipe exit=0 0
```

A separate compatibility probe showed npm can write username, `_password`, and
email keys to a selected `--userconfig` file, but npm 11 treats `always-auth` as
an unknown option and refuses `npm config set ...:always-auth`.

```text
$ npm config set --userconfig "$SCRATCH/compat.npmrc" //pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:username user
exit=0
$ npm config set --userconfig "$SCRATCH/compat.npmrc" //pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_password ZmFrZQ==
exit=0
$ npm config set --userconfig "$SCRATCH/compat.npmrc" //pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:email user@example.invalid
exit=0
$ npm config set --userconfig "$SCRATCH/compat.npmrc" //pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:always-auth true
npm error `always-auth` is not a valid npm option
npm error A complete log of this run can be found in: /home/shuaizhang/.npm/_logs/2026-06-05T21_07_35_975Z-debug-0.log
exit=1
```

Direct file-plan writes can still preserve or emit registry-scoped
`always-auth` when a target package manager requires it, but the accepted default
change plan is registry-scoped `_authToken` because that is what the local
Microsoft reference provider writes.

### CI Temporary `.npmrc` Scope

CI temporary npm and pnpm probes selected a product-owned userconfig file:

```bash
cat > "$SCRATCH/npmrc-ci" <<'EOF'
//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken=fake-ci-token
EOF
```

Transcript:

```text
$ NPM_CONFIG_USERCONFIG="$SCRATCH/npmrc-ci" npm --prefix "$SCRATCH/project" config list --json | node -e 'const fs=require("node:fs"); const c=JSON.parse(fs.readFileSync(0,"utf8")); console.log(JSON.stringify({registry:c.registry,scopeRegistry:c["@scope:registry"],userconfig:c.userconfig,authToken:c["//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken"] ?? "<hidden-or-absent>"}, null, 2));'
npm warn Unknown project config "always-auth". This will stop working in the next major version of npm.
{
  "registry": "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/",
  "scopeRegistry": "https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/",
  "userconfig": "/workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/npm-yarn-config-probe/npmrc-ci",
  "authToken": "<hidden-or-absent>"
}
exit=0
$ NPM_CONFIG_USERCONFIG="$SCRATCH/npmrc-ci" pnpm --dir "$SCRATCH/project" config list --json | node -e 'const fs=require("node:fs"); const c=JSON.parse(fs.readFileSync(0,"utf8")); console.log(JSON.stringify({registry:c.registry,scopeRegistry:c["@scope:registry"],userconfig:c.userconfig,authToken:c["//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/:_authToken"] ?? "<hidden-or-absent>"}, null, 2));'
{
  "registry": "https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/",
  "scopeRegistry": "https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/",
  "userconfig": "/workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/npm-yarn-config-probe/npmrc-ci",
  "authToken": "fake-ci-token"
}
exit=0
```

The CI probe above proves the accepted auth-only temporary `.npmrc` case only
when registry declarations remain visible from project/workspace configuration.
A replacement `NPM_CONFIG_USERCONFIG` hides the original user-level `.npmrc`.
Therefore, if discovery found the registry declaration only in the original user
config, CONFIG must either include/copy the needed declaration into the temporary
`.npmrc` or reject an auth-only CI plan.

User-config-source fixture:

```bash
mkdir -p "$SCRATCH/project-useronly" "$SCRATCH/home-useronly"
cat > "$SCRATCH/project-useronly/package.json" <<'EOF'
{"name":"phase-14-useronly","packageManager":"yarn@4.9.2"}
EOF
cat > "$SCRATCH/home-useronly/.npmrc" <<'EOF'
registry=https://pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/
@scope:registry=https://pkgs.dev.azure.com/org/user/_packaging/userscoped/npm/registry/
//pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/:_authToken=fake-user-token
EOF
cat > "$SCRATCH/auth-only.npmrc" <<'EOF'
//pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/:_authToken=fake-ci-token
EOF
cat > "$SCRATCH/declaration-and-auth.npmrc" <<'EOF'
registry=https://pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/
@scope:registry=https://pkgs.dev.azure.com/org/user/_packaging/userscoped/npm/registry/
//pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/:_authToken=fake-ci-token
EOF
```

Transcript:

```text
$ NPM_CONFIG_USERCONFIG="$SCRATCH/auth-only.npmrc" HOME="$SCRATCH/home-useronly" npm --prefix "$SCRATCH/project-useronly" config list --json | node -e 'const fs=require("node:fs"); const c=JSON.parse(fs.readFileSync(0,"utf8")); console.log(JSON.stringify({registry:c.registry,scopeRegistry:c["@scope:registry"] ?? "<absent>",userconfig:c.userconfig,authToken:c["//pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/:_authToken"] ?? "<hidden-or-absent>"}, null, 2));'
{
  "registry": "https://registry.npmjs.org/",
  "scopeRegistry": "<absent>",
  "userconfig": "/workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/npm-yarn-config-probe/auth-only.npmrc",
  "authToken": "<hidden-or-absent>"
}
exit=0
$ NPM_CONFIG_USERCONFIG="$SCRATCH/declaration-and-auth.npmrc" HOME="$SCRATCH/home-useronly" npm --prefix "$SCRATCH/project-useronly" config list --json | node -e 'const fs=require("node:fs"); const c=JSON.parse(fs.readFileSync(0,"utf8")); console.log(JSON.stringify({registry:c.registry,scopeRegistry:c["@scope:registry"] ?? "<absent>",userconfig:c.userconfig,authToken:c["//pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/:_authToken"] ?? "<hidden-or-absent>"}, null, 2));'
{
  "registry": "https://pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/",
  "scopeRegistry": "https://pkgs.dev.azure.com/org/user/_packaging/userscoped/npm/registry/",
  "userconfig": "/workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/npm-yarn-config-probe/declaration-and-auth.npmrc",
  "authToken": "<hidden-or-absent>"
}
exit=0
$ NPM_CONFIG_USERCONFIG="$SCRATCH/auth-only.npmrc" HOME="$SCRATCH/home-useronly" pnpm --dir "$SCRATCH/project-useronly" config list --json | node -e 'const fs=require("node:fs"); const c=JSON.parse(fs.readFileSync(0,"utf8")); console.log(JSON.stringify({registry:c.registry,scopeRegistry:c["@scope:registry"] ?? "<absent>",userconfig:c.userconfig,authToken:c["//pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/:_authToken"] ?? "<hidden-or-absent>"}, null, 2));'
{
  "registry": "https://registry.npmjs.org/",
  "scopeRegistry": "<absent>",
  "userconfig": "/workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/npm-yarn-config-probe/auth-only.npmrc",
  "authToken": "fake-ci-token"
}
exit=0
$ NPM_CONFIG_USERCONFIG="$SCRATCH/declaration-and-auth.npmrc" HOME="$SCRATCH/home-useronly" pnpm --dir "$SCRATCH/project-useronly" config list --json | node -e 'const fs=require("node:fs"); const c=JSON.parse(fs.readFileSync(0,"utf8")); console.log(JSON.stringify({registry:c.registry,scopeRegistry:c["@scope:registry"] ?? "<absent>",userconfig:c.userconfig,authToken:c["//pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/:_authToken"] ?? "<hidden-or-absent>"}, null, 2));'
{
  "registry": "https://pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/",
  "scopeRegistry": "https://pkgs.dev.azure.com/org/user/_packaging/userscoped/npm/registry/",
  "userconfig": "/workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/npm-yarn-config-probe/declaration-and-auth.npmrc",
  "authToken": "fake-ci-token"
}
exit=0
```

Decision for npm and pnpm:

- User-level writes target the effective user `.npmrc`, preferably selected by
  the configuration manager rather than by adapter-side file I/O.
- CI temporary writes target a product-owned temporary `.npmrc` and the caller
  receives environment instructions such as `NPM_CONFIG_USERCONFIG=<path>` for
  npm and pnpm processes. Auth-only temporary `.npmrc` plans are accepted only
  when required registry declarations remain visible in project/workspace
  configuration. If the required declaration was discovered from the original
  user config that `NPM_CONFIG_USERCONFIG` would hide, the temporary `.npmrc`
  must include/copy that declaration alongside auth entries.
- Registry declarations remain in project/workspace `.npmrc` files when they are
  already repository configuration; credential entries are written outside
  repository-local configuration by default.
- Scoped credential writes target the auth selector derived from the scoped
  registry URL, such as
  `//pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/:_authToken` for
  `@scope:registry`, and must not fall back to the default registry token.
- The configuration manager may support explicit project-scoped writes only as a
  separate opt-in mode with conflict detection and ownership metadata.

## Yarn Berry Evidence

### Registry Keys Read by Yarn

The project `.yarnrc.yml` declared `npmRegistryServer` and `npmScopes` registry
URLs. Yarn 4.9.2 read those declarations from the project file while using the
separate fake-token user home.

```text
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/home" corepack yarn@4.9.2 --cwd "$SCRATCH/project" config get npmRegistryServer
https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/
exit=0
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/home" corepack yarn@4.9.2 --cwd "$SCRATCH/project" config get npmScopes --json
{"scope":{"npmAlwaysAuth":false,"npmAuthIdent":null,"npmAuthToken":null,"npmAuditRegistry":null,"npmPublishRegistry":null,"npmRegistryServer":"https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/"}}
exit=0
```

### User-Level Auth Target Consumed by Yarn

The user-level Yarn probe wrote auth under `npmRegistries` in
`$SCRATCH/home/.yarnrc.yml`. The evidence gate covers Yarn config resolution and
write targets only; it does not claim a real package install, publish, or feed
operation.

```text
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/home" corepack yarn@4.9.2 --cwd "$SCRATCH/project" config get npmRegistries --json
{"https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry":{"npmAlwaysAuth":true,"npmAuthIdent":null,"npmAuthToken":"********"}}
exit=0
```

A protocol-relative key also resolved:

```bash
cat > "$SCRATCH/home/.yarnrc.yml" <<'EOF'
npmRegistries:
  '//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/':
    npmAlwaysAuth: true
    npmAuthToken: fake-slash-token
EOF
```

Transcript:

```text
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/home" corepack yarn@4.9.2 --cwd "$SCRATCH/project" config get npmRegistries --json
{"//pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry":{"npmAlwaysAuth":true,"npmAuthIdent":null,"npmAuthToken":"********"}}
exit=0
```

This matches the local reference provider, which stores registry keys as `//`
plus the normalized registry host/path.

### Scoped Yarn Berry Auth Target

A scoped Yarn target probe used the project `npmScopes.scope.npmRegistryServer`
declaration and user-level `npmRegistries` entries for both default and scoped
registries. The negative home contained only the default registry entry. Yarn
redacts token values as `********`; the evidence proves the scoped auth target is
the matching `npmRegistries` key for the scoped registry URL.

Additional setup:

```bash
cat > "$SCRATCH/home/.yarnrc.yml" <<'EOF'
npmRegistries:
  'https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/':
    npmAlwaysAuth: true
    npmAuthToken: fake-default-token
  'https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/':
    npmAlwaysAuth: true
    npmAuthToken: fake-scoped-token
EOF
cat > "$SCRATCH/home-negative/.yarnrc.yml" <<'EOF'
npmRegistries:
  'https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/':
    npmAlwaysAuth: true
    npmAuthToken: fake-default-token
EOF
```

Transcript:

```text
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/home" corepack yarn@4.9.2 --cwd "$SCRATCH/project" config get npmScopes --json
{"scope":{"npmAlwaysAuth":false,"npmAuthIdent":null,"npmAuthToken":null,"npmAuditRegistry":null,"npmPublishRegistry":null,"npmRegistryServer":"https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry/"}}
exit=0
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/home" corepack yarn@4.9.2 --cwd "$SCRATCH/project" config get npmRegistries --json
{"https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry":{"npmAlwaysAuth":true,"npmAuthIdent":null,"npmAuthToken":"********"},"https://pkgs.dev.azure.com/org/_packaging/scoped/npm/registry":{"npmAlwaysAuth":true,"npmAuthIdent":null,"npmAuthToken":"********"}}
exit=0
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/home-negative" corepack yarn@4.9.2 --cwd "$SCRATCH/project" config get npmRegistries --json
{"https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry":{"npmAlwaysAuth":true,"npmAuthIdent":null,"npmAuthToken":"********"}}
exit=0
```

Decision: for Yarn Berry scoped packages, CONFIG writes `npmAuthToken` and
`npmAlwaysAuth` under the `npmRegistries` key matching
`npmScopes.<scope>.npmRegistryServer`. The default registry credential is not an
acceptable substitute for a scoped registry credential target.

### CI Temporary Yarn Scope

Temporary `HOME` setup:

```bash
cat > "$SCRATCH/ci-home/.yarnrc.yml" <<'EOF'
npmRegistries:
  'https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/':
    npmAlwaysAuth: true
    npmAuthToken: fake-ci-token
EOF
```

Transcript:

```text
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/ci-home" corepack yarn@4.9.2 --cwd "$SCRATCH/project" config get npmRegistryServer
https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/
exit=0
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/ci-home" corepack yarn@4.9.2 --cwd "$SCRATCH/project" config get npmRegistries --json
{"https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry":{"npmAlwaysAuth":true,"npmAuthIdent":null,"npmAuthToken":"********"}}
exit=0
```

`YARN_RC_FILENAME` setup used an isolated fresh home with no pre-existing
`.yarnrc.yml`, so the result maps only to the selected file behavior:

```bash
mkdir -p "$SCRATCH/fresh-home"
cat > "$SCRATCH/temporary.yarnrc.yml" <<'EOF'
npmRegistries:
  'https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry/':
    npmAlwaysAuth: true
    npmAuthToken: fake-rc-token
EOF
```

Transcript:

```text
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/fresh-home" YARN_RC_FILENAME="$SCRATCH/temporary.yarnrc.yml" corepack yarn@4.9.2 --cwd "$SCRATCH/project" config get npmRegistryServer
https://registry.yarnpkg.com
exit=0
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/fresh-home" YARN_RC_FILENAME="$SCRATCH/temporary.yarnrc.yml" corepack yarn@4.9.2 --cwd "$SCRATCH/project" config get npmRegistries --json
{"https://pkgs.dev.azure.com/org/proj/_packaging/feed/npm/registry":{"npmAlwaysAuth":true,"npmAuthIdent":null,"npmAuthToken":"********"}}
exit=0
```

The isolated `YARN_RC_FILENAME` probe consumed auth from the selected file, with
no contamination from prior user-home state. It also changed rc filename lookup
enough that the normal project `.yarnrc.yml` registry declaration was bypassed
and Yarn fell back to `https://registry.yarnpkg.com`.

A temporary `HOME` also hides declarations that exist only in the original user
home. For CI plans where the source registry declaration is user-level rather
than project/workspace-level, CONFIG must emit a complete temporary
`.yarnrc.yml` with both declarations and auth:

```bash
mkdir -p "$SCRATCH/ci-home-complete"
cat > "$SCRATCH/ci-home-complete/.yarnrc.yml" <<'EOF'
npmRegistryServer: 'https://pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/'
npmScopes:
  scope:
    npmRegistryServer: 'https://pkgs.dev.azure.com/org/user/_packaging/userscoped/npm/registry/'
npmRegistries:
  'https://pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/':
    npmAlwaysAuth: true
    npmAuthToken: fake-ci-token
EOF
```

Transcript:

```text
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/ci-home-complete" corepack yarn@4.9.2 --cwd "$SCRATCH/project-useronly" config get npmRegistryServer
https://pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry/
exit=0
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/ci-home-complete" corepack yarn@4.9.2 --cwd "$SCRATCH/project-useronly" config get npmScopes --json
{"scope":{"npmAlwaysAuth":false,"npmAuthIdent":null,"npmAuthToken":null,"npmAuditRegistry":null,"npmPublishRegistry":null,"npmRegistryServer":"https://pkgs.dev.azure.com/org/user/_packaging/userscoped/npm/registry/"}}
exit=0
$ COREPACK_HOME="$SCRATCH/corepack" HOME="$SCRATCH/ci-home-complete" corepack yarn@4.9.2 --cwd "$SCRATCH/project-useronly" config get npmRegistries --json
{"https://pkgs.dev.azure.com/org/user/_packaging/userfeed/npm/registry":{"npmAlwaysAuth":true,"npmAuthIdent":null,"npmAuthToken":"********"}}
exit=0
```

<!-- markdownlint-enable MD013 -->

Decision for Yarn:

- User-level writes target the effective user `.yarnrc.yml` auth entries under
  `npmRegistries`.
- CI temporary writes should prefer a product-owned temporary home directory
  containing `.yarnrc.yml`, with the package-manager process launched under that
  temporary `HOME`, because it preserves normal project `.yarnrc.yml` discovery.
  Auth-only temporary home config is accepted only when required declarations
  remain visible in project/workspace `.yarnrc.yml`; otherwise the temporary
  `.yarnrc.yml` must include/copy the user-sourced declarations with auth.
- `YARN_RC_FILENAME` is not the default CI write target. It may be supported only
  if the configuration manager emits a complete merged temporary rc file that
  includes both registry declarations and auth entries.
- Repository-local `.yarnrc.yml` files are read for registry declarations but are
  not default credential write targets.
- Phase 1.4 does not require proof of a real Yarn package operation or final YAML
  writer comment-preservation behavior. Those remain implementation validation
  follow-ups, not Phase 1.4 gate defects.

## Credential Write Target Policy

| Ecosystem | Default persistent target                               | CI temporary target                                                                                                                            | Default repository-local credential writes |
| --------- | ------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------ |
| npm       | User `.npmrc`, or selected userconfig managed by CONFIG | Product-owned temporary `.npmrc` selected via `NPM_CONFIG_USERCONFIG`; include declarations if they would otherwise be hidden                  | No                                         |
| pnpm      | Same `.npmrc` model as npm                              | Same temporary `.npmrc` selected via `NPM_CONFIG_USERCONFIG`; include declarations if they would otherwise be hidden                           | No                                         |
| Yarn      | User `.yarnrc.yml` `npmRegistries` entries              | Product-owned temporary `HOME/.yarnrc.yml`; merged `YARN_RC_FILENAME` only if explicitly implemented; include hidden user-sourced declarations | No                                         |

All credential-bearing plans must set `containsCredentialMaterial=true` and must
be denied by default for repository-local targets. Dry-run output, doctor output,
and ownership manifests must not contain token values.

Atomicity policy:

- Multi-entry credential writes for one logical registry target must be represented
  as a single change set with one transaction boundary. Examples include Yarn
  `npmAuthToken` plus `npmAlwaysAuth`, and npm/pnpm compatibility groups such as
  `_authToken` plus explicitly selected `always-auth`, `username`, `_password`, or
  `email`.
- CONFIG may satisfy the transaction boundary either by applying all sibling
  entries through an atomic single-file replacement, or by using an equivalent
  plan-group mechanism that verifies all before-state hashes before the first
  mutation and commits all sibling ownership metadata together after the file
  update succeeds.
- A change set is successful only after every sibling entry and its ownership
  metadata or manifest record has been committed. If any sibling entry or metadata
  commit fails, the whole change set fails; no partial success may be reported to
  the adapter, caller, doctor output, or ownership manifest.
- For persistent user/global files, partial failure must restore the file to the
  verified prior state for every sibling entry, including deletion of entries that
  did not exist before the change set. If restoration cannot be verified, CONFIG
  must leave the operation failed and report recovery guidance without recording
  ownership of the partially applied entries.
- For product-owned CI temporary containers, partial failure or cancellation deletes
  the entire temporary `.npmrc`, temporary `HOME`, or explicitly merged temporary
  `YARN_RC_FILENAME` container. Because these containers are disposable and
  product-owned, whole-container cleanup is preferred over per-entry repair.
- Explicit project-scoped writes, when enabled, must use the same change-set
  atomicity and manifest-commit semantics as user/global writes, in addition to the
  opt-in, conflict-detection, and ownership-metadata requirements above.

Rollback and removal policy:

- User/global plans must distinguish `create`, `update`, and `refresh` for each
  owned credential entry. A `create` records that no prior owned entry existed;
  rollback removes only that newly-created owned entry.
- An `update` or `refresh` must include prior product-owned entry metadata, or a
  before-state hash plus selector/value metadata sufficient for CONFIG to verify
  the target still matches the planned before state and restore the previous
  product-owned value. Rollback must not delete a pre-existing owned credential
  that was updated or refreshed.
- User/global rollback and removal must preserve registry declarations, comments
  where practical, unrelated credentials, unowned package-manager settings, and
  pre-existing product-owned entries that are not part of the failed operation.
- For product-owned CI temporary containers, rollback/removal deletes the whole
  temporary `.npmrc` file or temporary `HOME` directory created by the
  configuration manager. These files and directories are product-owned disposable
  containers, so whole-file or whole-directory deletion is the intended cleanup
  semantic.
- For an explicitly implemented merged `YARN_RC_FILENAME` CI file, cleanup
  deletes that product-owned merged temporary file rather than editing entries in
  place.

## Configuration-Manager Change-Plan Implications

Phase 12 and Phase 13B should implement adapter output as declarative plans, not
file writes:

```text
ConfigurationChangeSet
  changeSetId: stable id for one logical registry credential update
  ecosystem: npm | pnpm | yarn
  targetScope: user | ci-temporary | explicit-project
  targetPathOrConfigKey: selected .npmrc or .yarnrc.yml path
  entries:
    - entrySelector: registry URL plus auth key selector
      operation: create | update | refresh | remove
      intendedCanonicalValue: redacted in diagnostics when credential-bearing
      priorOwnedEntry:
        required for update/refresh; includes owner id, selector, redacted
        previous value metadata, and before-state hash sufficient to verify and
        restore
  conflictPolicy: fail on non-owned conflicting credential entries by default
  atomicApplyPolicy:
    verify all sibling before states before mutation;
    apply by atomic single-file replacement or equivalent CONFIG transaction;
    commit ownership manifest only after every sibling entry is durable
  rollbackPolicy:
    user/global create removes only the newly-created owned entry;
    user/global update/refresh restores the previous product-owned value after
    before-state verification;
    product-owned CI temporary deletes the temporary file or directory;
    partial failure rolls back the whole change set, never individual success
  ciTemporaryDeclarationPolicy:
    auth-only temporary config is allowed only when registry declarations remain
    visible from project/workspace config; otherwise copy/include the required
    user-sourced declarations in the product-owned temporary config
  containsCredentialMaterial: true for auth entries
  expiresAt: required for CI temporary plans when the CI job duration is known
```

Canonical selectors:

- npm/pnpm registry auth: `npmrc:<scope>: //<normalized-registry>/:_authToken`.
- npm/pnpm optional compatibility fields, only when explicitly selected:
  `username`, `_password`, `email`, and registry-scoped `always-auth`.
- Yarn registry auth:
  `.yarnrc.yml:npmRegistries["//<normalized-registry>"].npmAuthToken` and
  `.npmAlwaysAuth`.
- Yarn registry declarations are discovered from `.yarnrc.yml` `npmRegistryServer`
  and `npmScopes.<scope>.npmRegistryServer`; they are not overwritten during a
  credential refresh.

## Affected Requirements and Designs

- `requirements.md`: npm, pnpm, and Yarn requirements 1, 2, 3, and 5 are
  evidence-supported only for the scoped user-level and CI temporary
  configuration behavior above, including the rule that CI temporary auth-only
  config is valid only when registry declarations remain visible or are copied
  into the temporary config. Requirement 4, covering lifecycle/bootstrap
  invocation, is not validated by Phase 1.4 and remains a follow-up later-phase
  concern. The no-repository-local-secret requirement remains constrained by
  configuration-manager-owned write plans and explicit opt-in for project-local
  targets.
- `high-level-design.md`: The npm adapter discovery and user-level default write
  model are evidence-supported for npm and pnpm, including scoped registry
  credential target selection, and Yarn Berry write support is unblocked for
  configuration-manager-owned plans instead of read-only diagnostics only.
- `mid-level-design.md`: npm adapter configuration inputs, request mapping, write
  policy, doctor checks, and configuration-manager ownership semantics are
  evidence-supported with npm/pnpm scoped auth selectors, the Yarn
  `npmScopes`-to-`npmRegistries` scoped write selector, and rollback constraints
  recorded here. Lifecycle-script guidance is outside the Phase 1.4 evidence
  scope and must be validated by the concrete downstream owners before acceptance
  depends on it: ADAPTER-NPM Phase 12 for npm/pnpm adapter invocation checks,
  ADAPTER-NPM Phase 13B for Yarn-enabled adapter/write-path coverage, and QA
  Phase 15 for end-to-end hardening.
- `project-breakdown.md`: Phase 1.4 exit criterion is satisfied with a pass
  decision before dependent implementation phases. Phase 12 may proceed with npm
  and pnpm parser/change-plan generation, including scoped registry credential
  targets; Phase 13B may proceed with Yarn change-plan generation and
  configuration-manager apply/remove metadata, including scoped `npmScopes`
  credential targets. Phase 13A remains valid for read-only Yarn diagnostics, but
  it no longer blocks Yarn write planning after this decision.

## Pass/Fail Decision Before Phases 12 and 13

Phase 1.4 passes with constraints:

1. Phase 12 may implement npm and pnpm registry discovery and change-plan
   generation for user-level and CI temporary `.npmrc` credential writes,
   including scoped registry auth selectors, only if CI temporary plans preserve
   required registry declarations by relying on visible project/workspace config
   or by copying user-sourced declarations into the temporary `.npmrc`.
2. Phase 13A remains valid for read-only Yarn diagnostics.
3. Phase 13B may implement Yarn write support for config-manager-owned change
   plans because Yarn Berry user-level, scoped `npmScopes` to `npmRegistries`,
   and CI temporary write targets were accepted by the config-resolution probes
   above, subject to the same CI temporary declaration-preservation rule for
   user-sourced declarations.
4. Any implementation that writes credentials directly from the adapter, writes
   credentials into repository-local config by default, cannot distinguish
   create/update/refresh rollback state, reports success before all sibling
   credential entries and ownership metadata are committed, permits partial
   multi-entry credential success, or relies on `YARN_RC_FILENAME` without emitting
   a complete merged temporary rc file, or emits auth-only CI temporary config
   while hiding the only registry declaration source is out of scope and must stop
   for CONFIG review.
5. The pass does not claim real-feed package operations or final YAML writer
   preservation. Those are residual follow-ups outside the Phase 1.4 gate.

## Validation and Checks

Reference package metadata and tool version commands are shown in the transcripts
above. Each npm, pnpm, and Yarn observed result maps to one command invocation
and recorded exit status.

Markdown validation:

```bash
pnpm exec prettier --check src/private/app/azureauth-credprovider/docs/phase-1.4-npm-yarn-config-evidence.md
pnpm exec markdownlint-cli2 src/private/app/azureauth-credprovider/docs/phase-1.4-npm-yarn-config-evidence.md
```

Results after this update:

```text
pnpm exec prettier --check ...: exit 0
pnpm exec markdownlint-cli2 ...: exit 0
```

## Residual Risks and Follow-Ups

1. The local Microsoft npm reference packages are extracted packages, not Git
   checkouts. Registry and dry-run pack metadata provided tarball URLs, shasums,
   and integrity strings, but the extracted paths remain non-authoritative
   supporting references. Phase 12 should prefer source-level inspection if the
   corresponding source commit becomes available.
    - Owner: **ADAPTER-NPM**
    - Dependency impact: Does not block Phase 12 or Phase 13B because disposable
      package-manager probes are the gate evidence. If source commits become
      available before implementation lock, Phase 12 should refresh parser and
      compatibility assumptions before final acceptance.
2. npm 11 warns that `always-auth` is unknown. Treat `always-auth` as an optional
   compatibility field, not a mandatory npm 11 write, unless later package-manager
   validation proves it is required for a selected target.
    - Owner: **ADAPTER-NPM and CONFIG**
    - Dependency impact: Phase 12 and Phase 13B may proceed only with
      explicitly-selected compatibility-field plans. CONFIG must keep
      `always-auth` out of mandatory default writes until new evidence changes the
      selector policy.
3. The probes did not perform real Azure Artifacts package install or publish
   operations, including scoped package operations. Later adapter validation may
   cover fake-feed and selected real-feed behavior with redaction, but that
   absence is not a Phase 1.4 defect.
    - Owner: **QA and ADAPTER-NPM**
    - Dependency impact: Does not block Phase 12 or Phase 13B change-plan
      implementation. It becomes a validation input for adapter acceptance,
      cross-ecosystem QA, and any selected real-feed release checks.
4. Requirement 4 lifecycle/bootstrap invocation remains deferred. Phase 1.4
   validates configuration discovery and write planning only; it does not prove
   that npm, pnpm, or Yarn lifecycle hooks/bootstrap flows invoke the adapter.
    - Owner: **ADAPTER-NPM and QA**
    - Dependency impact: Does not block Phase 12 or Phase 13B configuration
      change-plan generation. ADAPTER-NPM Phase 12 must own npm/pnpm
      adapter-side lifecycle/bootstrap invocation checks as part of adapter
      acceptance, using configuration-manager-owned outputs as dependencies.
      ADAPTER-NPM Phase 13B owns the corresponding Yarn-enabled adapter/write-path
      coverage when Yarn writes are enabled. Phase 14.2 covers configure and
      unconfigure orchestration through CONFIG-owned plans only; it closes a
      requirement 4 gap only if that orchestration explicitly invokes the related
      bootstrap flow. QA Phase 15 must close end-to-end npm, pnpm, and Yarn
      invocation-path acceptance without repository-local credential writes before
      requirement 4 is complete.
5. CI temporary declaration preservation needs implementation-level tests for
   both cases: auth-only temporary config when declarations remain visible in
   project/workspace files, and complete temporary config when declarations were
   discovered only from the original user config.
    - Owner: **CONFIG, ADAPTER-NPM, and QA**
    - Dependency impact: Does not block Phase 12 or Phase 13B plan generation
      because the constraint is recorded in this gate, but any implementation that
      cannot prove declaration preservation must stop before acceptance.
6. Windows path, PowerShell environment propagation, and path-with-spaces behavior
   remain release-validation items. This gate only validates local Linux behavior.
    - Owner: **PLATFORM, QA, ADAPTER-NPM, and CONFIG**
    - Dependency impact: Does not block Phase 12 or Phase 13B design-time plan
      generation, but implementation tests must preserve Windows-safe path and
      environment handling before release hardening and Phase 15 signoff.
7. YAML comment preservation was not proven. The configuration manager should use
   a parser/writer strategy selected by CONFIG and should test surgical updates
   against representative `.yarnrc.yml` files before broad rollout; this is not a
   Phase 1.4 gate defect.
    - Owner: **CONFIG and ADAPTER-NPM**
    - Dependency impact: Phase 13B may proceed, but broad Yarn write rollout depends
      on configuration-manager tests that prove surgical `.yarnrc.yml` updates,
      rollback, and unrelated-setting preservation.
8. If a future Yarn version changes `npmRegistries` key normalization or config
   file discovery, Phase 13B must stop and either update this decision with new
   evidence or enter Phase 1R.
    - Owner: **ADAPTER-NPM, CONFIG, and PL**
    - Dependency impact: Directly gates Phase 13B only if new Yarn evidence
      contradicts this record. In that case, dependent Yarn write work must stop
      until the decision is updated or Phase 1R changes scope.
