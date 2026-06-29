# Phase 2 Contract Freeze

Status: **Implemented contract baseline**

Date: **2026-06-06**

Decision ID: **phase-2-contract-freeze**

Gate name: **Phase 2 Contract freeze**

Owner: **ARCH**

## Scope

This record freezes the Phase 2 shared contracts implemented in
`src/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Contracts`.
It covers only the Phase 2 package: credential request/result shapes, typed
errors, canonical resource identity, cache-key schema, configuration change
plans, doctor checks, adapter-host result mapping, and `keyring-helper-v2`.

This record does not implement real credential acquisition, persistent
product-owned derived credential caching, configuration application, adapter
protocol parsers, or final packaging.

## Frozen Contract Surface

| Contract                    | Version | Frozen behavior                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| --------------------------- | ------: | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Credential request/result   |       1 | Ecosystem, operation, canonical resource, audience, credential kind, identity flow, interaction policy, cache policy, CI context, result status, credential fields, account, tenant, cache key, and typed errors. Secret-bearing record `ToString()` output is redacted.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                            |
| Canonical resource identity |       1 | Azure DevOps host, organization, optional project, feed, repository, and HTTPS service endpoint. The endpoint host, organization, and supported path components for project, feed, and repository must match the canonical identity because the endpoint is not a separate cache partition dimension.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| Cache-key schema            |       1 | `azdo-cache-v1` plus ecosystem, host, organization, optional project, optional feed, optional repository, service identity, account, tenant, audience, and credential kind. Phase 2 Git defaults to host-and-organization partitioning with project, feed, and repository omitted; package adapters include feed identity. Service identity is a required canonical lower-case partition; mixed-case values are rejected rather than silently folded. Cache keys are produced only for accepted version 1 MVP requests. Consumers fail closed on unsupported majors, malformed v1 shapes, missing or extra dimensions, empty components, non-canonical partition encoding, unsupported resource identity partitions, reserved resource markers as identity values, non-canonical service identity, unspecified or unknown required enums, and future persistent-cache requests.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| ConfigurationChangePlan     |       1 | Declarative, owner-scoped changes only. Create, update, refresh, remove, atomic change-set, rollback state, manifest metadata, global scope, and CI temporary declaration-preservation semantics are explicit. Adapters do not apply writes directly. `WorkspaceReadOnly` plans carry no `ConfigurationChange` writes. Secret-bearing changes are flagged, require `containsCredentialMaterial=true`, and are redacted in `ToString()`. Value-writing npm `.npmrc` `_authToken` selectors and Yarn `.yarnrc.yml` `npmAuthToken` selectors are intrinsically secret by target/key semantics: they must set `isSecretValue=true`, require `containsCredentialMaterial=true`, reject CR or LF regardless of the supplied secret flag, and are redacted even on invalid instances. Phase 2 Yarn bearer-token plans emit `npmAuthToken` and `npmAlwaysAuth`. Yarn `npmAuthIdent` is unsupported in this product scenario: any `ConfigurationChangePlan` entry or ownership manifest entry targeting Yarn `npmAuthIdent` is rejected regardless of operation, and it is not generated for write, remove, or ownership cleanup plans. Doctor/plan-gate diagnostics still detect project-local Yarn `npmAuthIdent` as a forbidden same-registry shadowing or conflict source.                                                                                                                                                                                                                                                                                                                                                                               |
| DoctorCheck                 |       1 | Pass, warning, fail, skipped, unsupported, deferred, and `notApplicable` statuses plus observed value, expected value, correlation ID, remediation, and safe details are explicit.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| Adapter-host result mapping |       1 | Success writes protocol stdout only when the result contract major is supported, no error is attached, protocol-specific required credential material is present, and the adapter protocol has a stdout protocol for the operation. Git credential-helper `get` success accepts complete Basic material or bearer material; bearer material is adapted to Basic stdout with fixed username `AzureDevOps` and token-as-password, with no CR or LF in emitted fields. Git credential-helper `store`/`erase` success carries no credential material and writes no protocol stdout. Git results fail closed if Basic and bearer secret material are both present. When a Git `get` result carries a cache key, the cache-key credential kind must match the material: `bearerToken` keys require bearer material and permit the fixed Basic adaptation, while `basicPassword` and `patCompatibility` keys require username plus password material. `PythonKeyringBackend` is import/API-mode and does not write protocol stdout; stdout belongs only to the adapter-host v1 `KeyringHelper` protocol, whose helper argv uses `keyring-helper-v2`. `NpmConfiguration` success requires bearer material but writes no protocol stdout because npm-compatible configuration is surfaced by `ConfigurationChangePlan`. NuGet plugin success requires complete Basic material: username plus password, with no CR or LF in either field. Unsupported majors, error-bearing success, and missing or wrong-kind success material map to exit 64 and no protocol stdout. Failure classes suppress protocol stdout and use safe diagnostic stderr when required. |
| keyring-helper-v2           |       2 | Fixed non-shell `python-keyring` command arguments, unsupported contract-major rejection, including old major 1, invalid command/mode/service URI rejection, exit codes, stdout/stderr behavior, non-optional helper integrity requirements, and redacted helper response `ToString()`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             |

## Identity and Cache Policy

The MVP identity flows represented as accepted are interactive browser, device
code, explicit PAT compatibility, and Azure Pipelines system access token.
Service principal, managed identity, and workload identity federation are
represented but deferred for MVP.

PAT compatibility is explicit only. The contract does not permit silent fallback
from Microsoft Entra flows to PAT, device code, desktop cache discovery, or
Azure Pipelines token.

Product-owned persistent derived credential caching is disabled for MVP. The
cache-key schema is frozen for partitioning and future extension, but the MVP
contract supports no-cache, cache-disabled, non-persistent CI, and
cache-unavailable behavior without requiring persistent writes. Requests for the
future product-owned persistent derived credential cache are represented for
future compatibility but are rejected by MVP request acceptance. No plaintext
fallback is permitted.

Service identity is a required cache partition dimension and must be supplied in
canonical lower-case form with no leading or trailing whitespace. The contract
does not define service identity as case-insensitive: `ProdApp` and `prodapp`
must never be silently collapsed into the same cache key. Non-canonical service
identity values are rejected before cache-key creation and when validating
deserialized cache keys.

Cache-key partition components are encoded with the canonical padded Base64
encoder emitted by the contract. Equivalent unpadded or Base64URL spellings are
not accepted because they create multiple wire keys for the same partition.

Interactive browser and device code flows require an interaction policy that
allows user interaction through the host tool or user consent; `never` blocks
those flows. Azure Pipelines system access token flow is accepted only when CI
mode is explicit, the CI provider marker is exactly `AzurePipelines`, the system
access token is declared as present, the cache policy is `nonPersistentCi`, and
the request declares no persistent writes; only Git system-token requests require
the `bearerToken` request credential kind.
Git `bearerToken` results, including Azure Pipelines system access tokens, are
returned to the Git credential-helper protocol as Basic credential material using
the fixed username `AzureDevOps` and the token as the password. Mixed Git
results that contain bearer material plus any username or password material fail
closed, and Git cache-key credential kinds must remain coherent with the result
material before protocol stdout is emitted.

The service endpoint is acquisition metadata, not an independent cache-key
partition. To avoid acquiring for one endpoint while caching under another
canonical resource identity, accepted requests and cache-key creation require an
absolute HTTPS endpoint on the default HTTPS port and without user info, query,
or fragment. The endpoint host must match the canonical Azure DevOps host and be
one of the supported Azure DevOps or Azure Artifacts host forms: `dev.azure.com`,
`pkgs.dev.azure.com`, `{org}.visualstudio.com`, or
`{org}.pkgs.visualstudio.com`. For modern hosts, the first path segment must
match the canonical organization. Legacy
`visualstudio.com` hosts carry the organization in the hostname and remain
supported when that hostname organization matches the canonical organization. For
supported Azure DevOps path shapes such as `{org}`, `{org}/{project}/_git/{repository}`,
`{org}/{project}/_packaging/{feed}`, `{org}/_packaging/{feed}`,
`{project}/_git/{repository}`, `{project}/_packaging/{feed}`,
`_packaging/{feed}`, and legacy `DefaultCollection` variants, endpoint project,
feed, and repository path components must exactly align with the corresponding
canonical fields. An absent endpoint component matches only an absent canonical
field, not a more-specific canonical cache identity. Feed endpoints may include
only the explicitly recognized suffixes `nuget/v3/index.json`, `npm`,
`npm/registry`, and `pypi/simple`; exactly one terminal slash after supported
package endpoint suffixes such as `npm` or `npm/registry` is accepted for
validation only after `_packaging/{feed}` appears at the expected position. The
contract stores the original `serviceEndpoint` supplied by the producer rather
than canonicalizing terminal slashes.
Unsupported or ambiguous resource paths, including `_git` or `_packaging`
markers in unrecognized positions, Git repositories named `npm` with a terminal
slash, unsupported feed suffixes, empty boundary segments such as `npm//`, and
extra path segments after a recognized suffix, fail closed instead of being
treated as wildcard cache identity matches. Encoded or decoded path separators
inside endpoint identity components, such as organization, project, feed, or
repository names, are rejected before matching so values such as
`feed%2Fother`, `project%2Fother`, or `repo%2Fother` cannot alias another
resource path.

Accepted package requests additionally bind the endpoint suffix to the requested
ecosystem. NuGet requests require `nuget/v3/index.json`; Python requests require
`pypi/simple`; npm, pnpm, and Yarn requests require `npm` or `npm/registry`.
Feed-root endpoints remain valid canonical resources but are not accepted package
credential requests for Phase 2.

Accepted Git requests may carry validated Azure Repos project/repository
resource identity when the feed dimension is absent and the service endpoint is
Azure Repos-compatible. The default Phase 2 Git cache key still omits project,
feed, and repository partitions and is scoped to host plus organization.

## Compatibility Rules

1. Version fields are major versions. A consumer supports a contract only when the
   producer major version equals the supported major version.
2. Optional additive fields are compatible within the same major version when an
   older consumer can ignore them safely.
3. Removing, renaming, changing field type, changing requiredness, changing enum
   representation, or changing the meaning of a field requires a new major
   version.
4. Changing required protocol stdout, stderr, or exit behavior requires a new
   major version.
5. Weakening security policy, cache partitioning, no-plaintext behavior,
   non-persistence defaults, PAT opt-in behavior, or redaction requirements
   requires a new major version and a new accepted security decision.
6. Unknown or explicit `unspecified` enum values are not silently accepted as
   success. Required credential-request enum fields, including credential kind
   and identity flow, fail closed when they are `unspecified` or unknown. Adapter
   protocols are validated before status mapping; unknown protocols always map to
   configuration error exit `64` without protocol stdout. Adapters must map
   unknown, unsupported, disabled, or deferred values to typed failures or doctor
   statuses.
7. String change-kind compatibility checks fail closed. Only explicitly listed
   compatible strings, such as `add-optional-field`, may avoid a major version
   change; unknown non-empty strings and the `unspecified` compatibility enum
   value require a major version change.
8. Extension data must contain only safe, non-secret metadata unless a field is
   explicitly marked as secret-bearing in the containing contract.

## Wire Shape and Redaction

JSON wire contracts use source-generated metadata, web-style camel-case property
names, and camel-case string enum values through
`ContractJson.CreateSerializerOptions()`. Numeric enum encoding is rejected and is
not the Phase 2 wire shape. Versioned payloads must carry `contractMajor`; a
missing version is rejected instead of being treated as explicit version 1.
Contract tests serialize, deserialize, and negatively validate representative
request, result, configuration, doctor, adapter-host, keyring-helper, and helper
integrity contracts to pin the shape, including when reflection-based JSON
metadata is disabled.

The Phase 2 configuration enum surface is frozen with the following camel-case
wire values:

| Enum                                   | Members and wire values                                                                                                                                                                                                                                                                             |
| -------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `ConfigurationChangeOperation`         | `Unspecified`/`unspecified`, `Set`/`set`, `Remove`/`remove`, `EnsureFile`/`ensureFile`, `InstallAdapter`/`installAdapter`, `RemoveAdapter`/`removeAdapter`, `Create`/`create`, `Update`/`update`, `Refresh`/`refresh`                                                                               |
| `ConfigurationScope`                   | `Unspecified`/`unspecified`, `User`/`user`, `WorkspaceReadOnly`/`workspaceReadOnly`, `ExplicitPath`/`explicitPath`, `CiTemporary`/`ciTemporary`, `Global`/`global`                                                                                                                                  |
| `ConfigurationTargetKind`              | `Unspecified`/`unspecified`, `GitConfig`/`gitConfig`, `NuGetPluginLayout`/`nuGetPluginLayout`, `PythonKeyringBackend`/`pythonKeyringBackend`, `KeyringShim`/`keyringShim`, `Npmrc`/`npmrc`, `Yarnrc`/`yarnrc`, `CiTemporaryFile`/`ciTemporaryFile`                                                  |
| `ConfigurationDeclarationPreservation` | `Unspecified`/`unspecified`, `NotApplicable`/`notApplicable`, `AuthOnlyWhenDeclarationsRemainVisible`/`authOnlyWhenDeclarationsRemainVisible`, `CopyHiddenDeclarationsToTemporaryConfig`/`copyHiddenDeclarationsToTemporaryConfig`, `CompleteMergedTemporaryConfig`/`completeMergedTemporaryConfig` |
| `ConfigurationTemporaryContainerKind`  | `Unspecified`/`unspecified`, `None`/`none`, `NpmrcFile`/`npmrcFile`, `TemporaryHome`/`temporaryHome`, `YarnRcFile`/`yarnRcFile`                                                                                                                                                                     |
| `ConfigurationAtomicityPolicy`         | `Unspecified`/`unspecified`, `AtomicChangeSetRequired`/`atomicChangeSetRequired`                                                                                                                                                                                                                    |
| `ConfigurationRollbackPolicy`          | `Unspecified`/`unspecified`, `Required`/`required`                                                                                                                                                                                                                                                  |
| `ConfigurationPlanState`               | `Unspecified`/`unspecified`, `Planned`/`planned`, `Applied`/`applied`, `RolledBack`/`rolledBack`, `Failed`/`failed`                                                                                                                                                                                 |
| `ConfigurationManifestCommitPolicy`    | `Unspecified`/`unspecified`, `CommitAfterDurableChanges`/`commitAfterDurableChanges`                                                                                                                                                                                                                |

Secret-bearing contracts (`CredentialResult`, `ConfigurationChange`, and
`KeyringHelperResponse`) must not expose plaintext secret values through
synthesized record `ToString()` output. JSON protocol payloads may still carry
credential material only in fields explicitly defined for protocol transfer.
For `ConfigurationChange`, value-writing npm-compatible auth-token selectors
(`Npmrc` `_authToken` and `Yarnrc` `npmAuthToken`) are treated as secret by
selector semantics for validation and redaction even if a producer supplies
`isSecretValue=false`. Phase 2 Yarn bearer-token plans do not emit
`npmAuthIdent`; Yarn `npmAuthIdent` plan and ownership manifest entries are
unsupported and rejected regardless of operation.

## Configuration Change Plan Semantics

`ConfigurationChangePlan` is a declarative plan, not an implementation of file
writes. Version 1 explicitly carries:

- a stable `changeSetId` for one logical registry credential update;
- `create`, `update`, `refresh`, and `remove` operations;
- npm and pnpm `_authToken` entries derived from the accepted npm registry
  endpoint host and path, not from a hard-coded package host. For example,
  `https://dev.azure.com/{org}/{project}/_packaging/{feed}/npm/registry`,
  `https://pkgs.dev.azure.com/{org}/_packaging/{feed}/npm`, and
  `https://{org}.pkgs.visualstudio.com/_packaging/{feed}/npm/registry` map to
  `//<host>/<path>/:_authToken` after removing one terminal slash from accepted
  npm registry endpoints;
- the intended success surface for npm, pnpm, and Yarn configuration. These
  package managers do not consume credential-provider stdout, so
  `NpmConfiguration` success validates bearer material but leaves protocol stdout
  empty;
- Yarn Berry `.yarnrc.yml` entries for user-level and CI-temporary
  bearer-token plans separately target `npmRegistries[registry].npmAuthToken`
  and `npmRegistries[registry].npmAlwaysAuth`, where `registry` is the same
  terminal-slash-normalized accepted npm registry endpoint used for npm and pnpm
  selector derivation;
- an atomic change-set policy requiring all sibling entries and metadata to
  become durable before success;
- a rollback policy and plan state for planned, applied, rolled-back, or failed
  outcomes;
- manifest metadata with product owner, entry selector, and product version.
  Update, refresh, remove, and remove-adapter operations carry previous
  owned-entry metadata on each `ConfigurationChange`. Value-writing operations
  (`set`, `create`, `update`, and `refresh`) require a non-null `value`;
  remove-style and other non-value operations must carry `value: null`.
  Value-writing line/config-file targets, including `GitConfig`, `Npmrc`, and
  `Yarnrc`, reject any value containing CR or LF;
- user, global, explicit-path, workspace-read-only, and CI-temporary scopes;
  workspace-read-only authorizes inspection and diagnostics only, not
  `ConfigurationChange` writes;
- a consistent credential-material flag. Any change with `isSecretValue=true`,
  and any value-writing `Npmrc` `_authToken` or `Yarnrc` `npmAuthToken` change
  by selector semantics, makes `containsCredentialMaterial=true` mandatory, and
  inconsistent deserialized plans fail closed. Phase 2 bearer-token plans do not
  emit `npmAuthIdent`; Yarn `npmAuthIdent` changes are unsupported and rejected
  regardless of operation;
- CI temporary container metadata and declaration-preservation mode so auth-only
  temporary config is allowed only when declarations remain visible, otherwise
  hidden declarations must be copied or a complete merged temporary config must
  be emitted. Yarn Berry CI-temporary activation is frozen as a
  configuration-manager-owned temporary `HOME` directory containing
  `.yarnrc.yml`; the immediate child `.yarnrc.yml` file path is the target of
  Yarn changes, and the temporary directory is the activation container.
  CI-temporary containers always use whole-container cleanup on rollback and
  removal; `deleteContainerOnRollback: false` and
  `deleteContainerOnRemoval: false` are rejected for every CI-temporary plan;
- CI temporary target binding. `ProductOwnedPath` and `TargetPathOrName` must be
  fully qualified canonical paths without `.` or `..` path segments and must not
  be POSIX, Windows drive, or UNC share filesystem roots. Windows extended path
  prefixes (`\\?\`, `\\.\`, `//?/`, and `//./`, including extended UNC forms
  such as `//?/UNC/server/share`) are rejected fail-closed for Phase 2.
  `NpmrcFile` changes target exactly the declared product-owned temporary file
  and use `Npmrc` changes. Npmrc file activation metadata is required and carries
  a `platform` marker. On Windows, activation sets only the canonical
  `NPM_CONFIG_USERCONFIG` variable to the product-owned `.npmrc` path because
  `NPM_CONFIG_USERCONFIG` and `npm_config_userconfig` collide in the
  case-insensitive environment block. On POSIX, activation sets both
  `NPM_CONFIG_USERCONFIG` and `npm_config_userconfig` to the product-owned
  `.npmrc` path. Both modes require an explicit empty `clearVariables` list so
  npm and pnpm consume the temporary file without relying on host-default user
  config discovery. Yarn Berry CI-temporary changes use only
  `TemporaryHome` activation: the declared product-owned temporary home is the
  activation container, and the only accepted `Yarnrc` target is its immediate
  child `.yarnrc.yml`.
  Standalone `YarnRcFile` activation is not accepted in the Phase 2 CI-temporary
  contract. Repository-local and user-level paths outside the declared
  product-owned container are rejected;
- Yarn Berry CI-temporary activation environment. On Windows, activation must
  set/override `USERPROFILE` and `HOME` to the product-owned temporary home and
  clear `HOMEDRIVE` and `HOMEPATH` so Yarn/Node resolve one temporary home. On
  Linux and macOS, activation keeps POSIX semantics by setting only `HOME` to the
  product-owned temporary home and clearing no Windows home variables. The
  `clearVariables` field is required on the JSON wire; POSIX plans preserve
  valid empty-list semantics only by carrying `clearVariables: []` explicitly;
- Yarn Berry CI-temporary plans must detect same-registry project-local
  `.yarnrc.yml` auth entries that would shadow the temporary `HOME`
  `npmRegistries[registry].npmAuthToken`. This includes project-local
  `npmRegistries[registry].npmAuthToken` entries, same-registry
  `npmRegistries[registry].npmAlwaysAuth: false` entries that override the
  required temporary `npmAlwaysAuth: true`, and project-local
  `npmScopes[*]` entries whose normalized `npmRegistryServer` matches a planned
  registry and that contain `npmAuthToken`, `npmAuthIdent`, or
  `npmAlwaysAuth: false`, because Yarn prefers scope auth over registry auth.
  Project-local same-registry `npmAuthIdent` is a forbidden shadowing or
  conflict signal in doctor/plan-gate diagnostics even though product plans do
  not write, remove, own, or clean up `npmAuthIdent`. A
  detected shadowing entry is a failing
  `DoctorCheck` diagnostic and blocks the CI temporary plan. Removing or
  migrating the project-local auth requires `ExplicitPath` or another
  write-authorizing scope; `WorkspaceReadOnly` may only report the conflict.
  Generated Yarn write plans use `npmAuthToken` plus `npmAlwaysAuth`;
  `npmAuthIdent` is unsupported and is not a product cleanup target.

## keyring-helper-v2 Contract

The backend invokes the helper with an argv list, not through a shell:

```text
python-keyring get --protocol-version 2 --service <service> [--username <user>] --mode password|creds
```

The command name is fixed to `python-keyring`. Requests that specify an unsupported `contractMajor`, another command, omit the
fixed command, specify `unspecified`/unknown modes, or provide a missing,
relative, non-HTTPS, non-default-port, unsupported-host, unsupported-path,
userinfo-bearing, query-bearing, fragment-bearing, or malformed service URI are
protocol violations; they fail closed with exit `64`, empty stdout, and redacted
stderr instead of being treated as password mode or leaking credentials through
argv. The service URI is keyring-specific and narrower than canonical resource
identity validation: it must be an Azure Artifacts Python feed endpoint on
`dev.azure.com`, `pkgs.dev.azure.com`, `{org}.visualstudio.com`, or
`{org}.pkgs.visualstudio.com`. Modern hosts require a non-empty organization
path segment; legacy `visualstudio.com` hosts carry the organization in the
hostname. Supported shapes require a non-empty feed component, an optional
non-empty project component, and a path ending exactly in
`_packaging/{feed}/pypi/simple` with at most one terminal slash. Frozen legacy
accepted shapes include
`https://{org}.visualstudio.com/DefaultCollection/{project}/_packaging/{feed}/pypi/simple/`
and
`https://{org}.pkgs.visualstudio.com/DefaultCollection/_packaging/{feed}/pypi/simple/`.
Org-only, Git, npm, NuGet, feed-root, and other canonical Azure DevOps endpoint
shapes are rejected before helper argv construction or response stdout mapping.
Encoded or decoded path separators inside the service URI identity components
are rejected before helper argv construction.

Stdout rules:

- `password` mode success writes only the password plus a trailing newline.
- `creds` mode success writes username, newline, password, newline.
- protocol stdout newlines are explicit LF (`\n`) on every OS.
- no-credential, unsupported contract majors, mapped failures, and all explicit
  failures write no protocol stdout.
- success with missing password, missing required username in `creds` mode, or
  other malformed success material is a protocol violation: exit `64`, empty
  stdout, redacted stderr.
- success with an attached typed error is not stdout-writable; it fails closed
  with exit `64`, empty stdout, and redacted stderr.

Stderr rules:

- no-credential writes no stderr.
- interaction, authorization, cache, integrity, protocol, and fatal failures may
  write only redacted diagnostic text.

JSON response rules:

- `KeyringHelperResponse` is a versioned source-generated contract root.
- Responses must carry `contractMajor: 2`; missing versions are rejected.

Exit rules:

- `0`: success.
- `1`: no credential.
- `2`: interaction required or blocked.
- `3`: unauthorized.
- `64`: configuration or unsupported contract error.
- `65`: helper integrity failure.
- `69`: cache unavailable.
- `70`: fatal failure.

Integrity metadata must include product ID, absolute helper path, SHA-256 digest,
and explicit platform integrity policy fields. Requiring `platform` is the
breaking change that advances the keyring helper contract major from 1 to 2. The
contract policy API validates only declared policy, path syntax, and platform
matching; it does not inspect the filesystem and does not prove helper existence,
file digest, symlink/reparse state, mode bits, owner, or parent-chain safety.

Linux strong policy is represented by `platform: linux`, `required` owner
validation, `rejectSymlinks`, and `sha256Required`. Windows and macOS use the
accepted weak policy represented by `platform: windows` or `platform: macOs`,
`deferredNotAvailable` owner validation, `bestEffortRejectSymlinks`, and
`sha256RequiredWeakPath`. The Phase 2 contract must not require native Windows or
macOS owner validation, race-free no-follow identity, Authenticode, or
signing/notarization validation now. Other trusted runtime platforms are
unsupported and fail closed at contract-policy validation.

`EnsureContractPolicyValid`/`IsContractPolicyValid` bind integrity metadata to
the current or supplied trusted runtime platform and remain contract-policy checks
only. `EnsureStructurallyValid`/`IsStructurallyValid` check self-declared metadata
shape and declared-platform path syntax only. Before any helper execution, the
caller must still take and revalidate a filesystem snapshot that proves regular
file existence, expected SHA-256 content, symlink/reparse policy, executable mode,
ownership requirements, and parent-chain safety for the declared platform.
JSON wire payloads must carry the platform, owner-validation, symlink-policy,
and digest-policy fields explicitly; omitted integrity policy metadata is
rejected instead of defaulting to secure values.

## Evidence and Sign-off Linkage

Phase 2 contract tests close the Phase 1A identity-flow checklist by freezing
the accepted and deferred flow states, proving `servicePrincipal`,
`managedIdentity`, and `workloadIdentityFederation` remain deferred, and proving
no silent fallback from Microsoft Entra flows to PAT, device code, desktop cache
discovery, or Azure Pipelines system token is allowed. Explicit PAT compatibility
is usable only when the PAT request itself is an accepted MVP request, so
explicit CI cannot advertise PAT compatibility as usable.

Phase 2 contract tests close the Phase 1R secure-cache checklist by freezing
`noCache`, `productPersistentCacheDisabled`, `nonPersistentCi`, and
`futurePersistentCacheRequested` states without requiring product-owned
persistent writes. Accepted Azure Pipelines system-token requests require the
`AzurePipelines` provider marker, declared system token, `nonPersistentCi`, and
`allowsPersistentWrites=false`. Requests for future product-owned persistent
derived credential caching remain rejected, and persistent-cache reintroduction
remains blocked until the later Phase 1R acceptance conditions or a superseding
record pass.

## Validation

Versioned contract tests are in
`tests/private/app/azureauth-credprovider/Hcoona.AzureAuth.CredProvider.Contracts.Tests`.
They use fakes and value fixtures only; no real credentials, host-tool writes, or
persistent cache writes are performed.
