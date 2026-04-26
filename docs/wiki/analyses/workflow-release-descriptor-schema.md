# Workflow Release Descriptor Schema

## Purpose

This page defines the author-time release files that feed the frozen planner-
centric architecture. It settles descriptor placement, discovery, YAML syntax,
ownership boundaries, target-instance catalog references, and validation split.
It does not define planner output objects, workflow job layouts, or executor
APIs.

## Design Summary

The release system uses exactly two author-time file kinds:

| File kind               | Owner              | Fixed location rule                | Purpose                                                                                                                                                         |
| ----------------------- | ------------------ | ---------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| project descriptor      | individual project | `src/**/three.release.yml`         | Declares that one project participates in workflow release, what canonical variants and artifacts it owns, and which shared target instances each profile uses. |
| target-instance catalog | repository         | `eng/release/target-instances.yml` | Declares the shared publication destinations, destination contracts, and static target capabilities that projects may reference.                                |

Both file kinds use YAML. The current schema version is `three.release/v1alpha1`.

## File Placement and Discovery

### Project descriptor placement

In `v1alpha1`, a releasable project owns one file named `three.release.yml`
in its release root, and that release root must be under `src/`.
The parent directory of that file is the release root for discovery, field-scoped
relative-path resolution, and project ownership.
A checked-in file with that name anywhere else in the repo is an authoring error,
not an ignored future opt-in.

That rule is intentionally project-root oriented rather than manifest-root
oriented. It matches the actual monorepo cases where the release unit is a
wrapper directory above the build manifest, such as:

| Release root                             | Descriptor path                                           | Primary manifest example                                       |
| ---------------------------------------- | --------------------------------------------------------- | -------------------------------------------------------------- |
| `src/public/lib/Hjg.Pngcs/`              | `src/public/lib/Hjg.Pngcs/three.release.yml`              | `Hjg.Pngcs.csproj`                                             |
| `src/public/lib/nbgv-python/`            | `src/public/lib/nbgv-python/three.release.yml`            | `pyproject.toml`                                               |
| `src/public/lib/hexo-renderer-asciidoc/` | `src/public/lib/hexo-renderer-asciidoc/three.release.yml` | `package.json`                                                 |
| `src/public/lib/asciidoctor-latexmath/`  | `src/public/lib/asciidoctor-latexmath/three.release.yml`  | `asciidoctor-latexmath.gemspec`                                |
| `src/public/app/ImageOcclusionEditor/`   | `src/public/app/ImageOcclusionEditor/three.release.yml`   | `ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj` |

### Project descriptor discovery

Discovery is deterministic:

1. enumerate all git-tracked files named `three.release.yml` anywhere in the
   repository;
2. reject the repository state if any such file is outside `src/`, including
   under `tests/`;
3. from the remaining `src/**/three.release.yml` set, reject any descriptor whose
   path is outside the signed-off first-delivery scope;
4. parse and statically validate every remaining descriptor before planning any
   release request;
5. sort the valid project set by `project.id` for downstream deterministic use.

The first-delivery scope currently allows descriptors only under:

- `src/public/`;
- `src/private/app/qidian-novel-downloader/`;
- `src/private/app/vscode-copilot-telegram-hook/`.

A checked-in descriptor outside that scope is an authoring error, not a hidden
future opt-in.

The signed-off first-delivery project set is narrower than these allowed root
patterns and is recorded in
[Workflow Release Low-Level Design](./workflow-release-low-level-design.md#first-delivery-author-time-input-project-set).
The extra allowed private app root remains valid schema scope for future
descriptor migration, but it is not part of the first generated author-time input
batch unless that low-level project set is updated.

### Project descriptor uniqueness rules

Static validation must reject all of the following:

- more than one `three.release.yml` in the same release root;
- two descriptors with the same `project.id`;
- two descriptors whose roots are ancestor and descendant of each other;
- any git-tracked descriptor under `tests/` or otherwise outside `src/`.

### Shared target catalog placement

The shared catalog lives at a single fixed path:
`eng/release/target-instances.yml`.

There is no catalog discovery step in `v1alpha1`. The fixed path keeps the
shared target-instance authority obvious and keeps project references stable.

## Common YAML Syntax

All release authoring files use these syntax rules:

- UTF-8 with LF line endings;
- YAML 1.2 mappings and sequences only;
- spaces, not tabs;
- relative normalized forward-slash paths for path-valued fields, with the base
  defined per field;
- explicit schema header fields at the top of every file.

The common header is:

```yaml
api-version: three.release/v1alpha1
kind: ...
```

Allowed `kind` values in this design layer are:

- `project`
- `target-instance-catalog`

### Path base rules

`v1alpha1` does not use one repo-wide base for every path mention.
Instead, each path-valued field or fixed-location rule declares its own base:

- fixed repository locations and discovery patterns in this document are repo-
  root-relative, such as `src/**/three.release.yml` and
  `eng/release/target-instances.yml`;
- `source.primary-manifest` and every entry in `source.auxiliary-inputs` are
  relative to the descriptor's release root, with author-time static validation
  required to resolve them to checked-in files under that root;
- absolute paths, backslashes, empty paths, and any `.` or `..` path segment are
  invalid.

There are no repo-relative project-descriptor path-valued YAML fields in the
current `v1alpha1` schema. Any future repo-relative field must say so
explicitly.

## Project Descriptor Schema

### Top-level shape

A project descriptor uses this top-level layout:

```yaml
api-version: three.release/v1alpha1
kind: project

project:
    id: ...
    display-name: ...
    ecosystem: ...
    release-kind: ...

source:
    primary-manifest: ...
    auxiliary-inputs: []

variants:
    - id: ...
      dimensions: {}
      artifacts:
          - id: ...
            role: ...
            kind-family: ...
            concrete-kind: ...
            produced-from: []

profiles:
    buddy:
        targets: []
    official:
        targets: []
```

### `project` section

`project` is the stable project-owned identity block.

Required fields:

- `id`: repo-unique stable selection key used by later workflow inputs and
  planning;
- `display-name`: human-facing project name;
- `ecosystem`: one of `dotnet`, `python`, `node`, or `ruby`;
- `release-kind`: one of `lib`, `app`, `tool`, `extension`, or `generator`.

`project.id` is descriptor-owned, not path-derived, because later workflow
selection must stay stable even if a directory is renamed. In current scope,
that same stable `project.id` also serves as the project slug for planner-
derived GitHub Release tags, so the design does not introduce a second tag-slug
field.

### `source` section

`source` identifies the project-owned release inputs inside the release root.

Required fields:

- `primary-manifest`: path to the main language manifest or build entry point,
  relative to the release root, and required to resolve at author time to one
  checked-in file under that root.

Optional fields:

- `auxiliary-inputs`: additional repo files that materially affect release
  semantics for this project, such as packaging scripts or installer metadata.
  Each entry is relative to the release root and must likewise resolve at author
  time to one checked-in file under that root.
- `version-authority`: current-scope authoritative version-source contract for
  this project. When omitted, the default is `build-system-nbgv`.

Validation rules:

- `source.primary-manifest` must resolve to an existing checked-in file under the
  descriptor's release root;
- every `source.auxiliary-inputs[]` entry must resolve to an existing checked-in
  file under the descriptor's release root;
- `source.version-authority`, when present, must be one of the closed current-
  scope values `build-system-nbgv` or `nbgv-python-pyproject-version`;
- `source.version-authority: nbgv-python-pyproject-version` is valid only when
  `project.id = nbgv-python`, `project.ecosystem = python`, and
  `source.primary-manifest = pyproject.toml`.

`source.primary-manifest` is also ecosystem-constrained in the current scope.
Static validation must apply this closed mapping:

| `project.ecosystem` | Allowed `source.primary-manifest` type | Current-scope repo examples                                                                                                                            |
| ------------------- | -------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `dotnet`            | a checked-in `.csproj` file            | `Hjg.Pngcs.csproj`, `ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj`, `QidianNovelDownloader.csproj`, `VSCodeCopilotTelegramHook.csproj` |
| `python`            | the checked-in file `pyproject.toml`   | `pyproject.toml` in `nbgv-python` and `markdown-hybrid-search-mcp`                                                                                     |
| `node`              | the checked-in file `package.json`     | `hexo-renderer-asciidoc/package.json`                                                                                                                  |
| `ruby`              | a checked-in `.gemspec` file           | `asciidoctor-latexmath.gemspec`                                                                                                                        |

Validation must reject any descriptor whose `project.ecosystem` and
`source.primary-manifest` resolve to a different manifest type than this table.
`v1alpha1` does not allow alternative current-scope primary-manifest forms such
as `setup.py`, `Gemfile`, `package-lock.json`, or non-`.csproj` MSBuild entry
files.

Examples grounded in the repo:

- `Hjg.Pngcs` points at `Hjg.Pngcs.csproj`;
- `nbgv-python` points at `pyproject.toml`;
- `hexo-renderer-asciidoc` points at `package.json`;
- `asciidoctor-latexmath` points at `asciidoctor-latexmath.gemspec`;
- `ImageOcclusionEditor` points at its nested WinUI `.csproj` and lists the
  lock file, installer payload metadata, template files, icon, and packaging
  scripts that materially affect release semantics as auxiliary inputs.

### `variants` section

`variants` is the author-time declaration of canonical build families.

Each variant requires:

- `id`: project-local stable variant handle, unique within the project
  descriptor;
- `dimensions`: a string map that declares only dimensions that change the
  canonical output family;
- `artifacts`: the artifact declarations owned by that variant.

`dimensions` is intentionally open-keyed in `v1alpha1`. The descriptor, not the
planner, decides which dimensions are semantically meaningful for that project.
Typical current-scope examples include `os`, `rid`, or another explicit
production flavor. Within one `project.id`, the full `dimensions` key/value set
is the variant's semantic identity. `variants[].id` remains only a project-local
authoring handle for stable references and diagnostics; it does not override or
replace the semantic identity derived from `dimensions`.

Validation rules:

- variant ids are unique within the project descriptor;
- within one project descriptor, static validation must reject two variants
  whose `dimensions` maps contain the same complete key/value set, even if their
  `id` values differ;
- variant semantic equality is based on the parsed `dimensions` mapping content,
  so key order in YAML does not create a distinct variant.

### `artifacts` section

Each artifact declaration is an author-time artifact intent that later maps to a
planned artifact object.

Required fields:

- `id`: descriptor-local stable artifact handle used by `produced-from`, profile
  target `artifacts`, and other same-descriptor references;
- `role`: logical artifact role such as `primary-package`, `symbols`,
  `primary-binary`, `installer`, or `release-metadata`;
- `kind-family`: one of the frozen architecture families such as `package`,
  `binary`, `installer`, `archive`, or `metadata`;
- `concrete-kind`: current-scope examples include `nuget`, `snupkg`, `wheel`,
  `sdist`, `npm-package`, `rubygem`, `executable`, or `inno-setup`.

For raw runnable outputs under `kind-family: binary`,
`concrete-kind: executable` is the single general executable kind in
`v1alpha1`. It covers both CLI executables and desktop GUI executables
such as .NET `Exe` and `WinExe` outputs; the schema does not split those
into separate concrete kinds.

Optional fields:

- `produced-from`: zero or more sibling artifact ids from the same variant when
  the artifact is a post-build transform rather than a direct canonical output.

`artifact.id` is not the frozen semantic artifact identity. Within one
descriptor, an artifact's semantic identity is the enclosing `project.id`,
the enclosing variant's full semantic identity from its parsed `dimensions`
key/value set, and the artifact `role`, `kind-family`, and `concrete-kind`.
Neither `artifact.id` nor `variants[].id` participates in semantic artifact
identity; both remain only local authoring handles for references and
diagnostics.

Validation rules:

- artifact ids are unique within the project descriptor;
- within one variant, static validation must reject two artifacts with the same
  `role` / `kind-family` / `concrete-kind` tuple, even if their `id` values
  differ;
- because duplicate semantic variants are already rejected by the `dimensions`
  rule, that within-one-variant tuple check is the descriptor-layer
  enforcement of semantic artifact-identity uniqueness within one `project.id`;
- `produced-from` may reference only artifact ids declared by the same variant;
- multi-variant bundle artifacts are out of scope in `v1alpha1`.

This keeps author-time descriptors aligned with the frozen architecture:
semantic artifact identity is stable across `variants[].id` or `artifact.id`
handle renames, while the descriptor still uses those local handles for
references.

### `profiles` section

`profiles` is the project-owned publish-intent block.

Rules:

- both `buddy` and `official` are required keys;
- each profile contains a `targets` list;
- the list may be empty;
- if a profile lists any non-`github-release` target, that same profile must
  also list exactly one `github-release` target.

Each target usage entry has this shape:

```yaml
- uses: family/instance-id
  artifacts: [artifact-id, ...]
  projection: {}
```

Required fields:

- `uses`: reference to one shared catalog target instance in the form
  `family/instance-id`;
- `artifacts`: one or more artifact ids from this descriptor that the publish
  intent consumes.

Optional fields:

- `projection`: project-owned target-side naming or presentation data when the
  resolved target family permits projection in `v1alpha1`.

`projection` is not free-form in the current scope. Static validation resolves
`uses` to a catalog target family and then applies the following closed schema
with no extra keys:

| Resolved family  | Allowed `projection` shape      | Rules                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| ---------------- | ------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `github-release` | optional `asset-labels` mapping | `asset-labels` maps artifact ids from this same target usage entry to non-empty display labels. Every mapping key must also appear in `artifacts`. The mapping is presentation-only and does not change artifact identity or package names.                                                                                                                                                                                                                                                                                 |
| `nuget`          | absent                          | `projection` must be omitted.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `pypi`           | absent                          | `projection` must be omitted.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |
| `npm`            | optional `package-name` string  | The target usage must reference exactly one artifact, and that artifact must have `kind-family: package` plus `concrete-kind: npm-package`. The resolved published package name is `projection.package-name` when present, otherwise the manifest-owned `package.json` `name`. For `npm.pkg.github.com`, the resolved name must be a valid scoped npm package name, and that resolved scope must exactly equal the referenced catalog `destination.owner`. For `registry.npmjs.org`, it must be any valid npm package name. |
| `rubygems`       | absent                          | `projection` must be omitted.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                               |

This intentionally narrows current-scope projection to the only signed-off
author-time cases that need explicit descriptor data: GitHub Release asset
labels and npm published package-name override for cases such as GitHub
Packages scope rewrite.

Within one profile, the same `uses` reference may appear at most once, and one
target usage entry may not repeat the same artifact id in `artifacts`.

## Shared Target-Instance Catalog Schema

### Top-level shape

The repo-owned catalog uses this top-level layout:

```yaml
api-version: three.release/v1alpha1
kind: target-instance-catalog

families:
    github-release:
        instances:
            - id: public
              contract: github-release-assets
              destination: {}
              capabilities: {}
```

`families` is keyed by the frozen target-family vocabulary:

- `github-release`
- `nuget`
- `pypi`
- `npm`
- `rubygems`

Each family contains zero or more `instances`.

### Catalog-owned data

Each target instance is shared, repo-owned, and referenced by many projects.
Each instance requires:

- `id`: family-local stable instance key;
- `contract`: destination contract id from the closed current-scope vocabulary;
- `destination`: a closed family-specific static destination object;
- `capabilities`: the static capability declaration used by planning.

Current-scope catalog examples are expected to include:

- `github-release/public`
- `nuget/nuget-org`
- `nuget/github-packages`
- `pypi/pypi`
- `npm/npmjs`
- `npm/github-packages`
- `rubygems/rubygems-org`
- `rubygems/github-packages`

There is intentionally no `pypi/github-packages` instance because GitHub
Packages does not expose a Python package registry. In the current repository
scope, GitHub Packages target instances exist only inside the `nuget`, `npm`,
and `rubygems` families.

### `contract`

`contract` is not free-form in `v1alpha1`. Static validation must accept only the
following vocabulary and family pairing:

| Family           | Allowed `contract` value |
| ---------------- | ------------------------ |
| `github-release` | `github-release-assets`  |
| `nuget`          | `nuget-publish`          |
| `pypi`           | `pypi-publish`           |
| `npm`            | `npm-publish`            |
| `rubygems`       | `rubygems-publish`       |

Validation must reject all of the following:

- any `contract` value outside that table;
- any catalog entry whose `family` and `contract` do not match that table;
- any attempt to model GitHub Packages as its own contract or family in the
  shared catalog.

This makes current-scope family-to-contract compatibility deterministic at
author time while preserving the architecture rule that GitHub Packages NuGet,
npm, and RubyGems hosts remain target instances inside the `nuget`, `npm`, and
`rubygems` families.

### `destination`

`destination` is catalog-owned static locator data for the target instance.
Projects never rewrite `destination`; they only opt in by reference.

`destination` is also not free-form in `v1alpha1`. Each family uses a closed
object shape with no extra keys:

| Family           | Required shape                                 | Rules                                                                                                                                                                                             |
| ---------------- | ---------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `github-release` | `host`, `owner`, `repo`                        | `host` must be the literal `github`. `owner` and `repo` must be non-empty GitHub slug strings. In the current repository scope, the only valid destination is `owner: hcoona` plus `repo: three`. |
| `nuget`          | `host` plus optional `owner` depending on host | `host` must be either `nuget.org` or `nuget.pkg.github.com`. `owner` is forbidden for `nuget.org` and required for `nuget.pkg.github.com`.                                                        |
| `pypi`           | `host`                                         | `host` must be the literal `pypi.org`. No other keys are allowed.                                                                                                                                 |
| `npm`            | `host` plus optional `owner` depending on host | `host` must be either `registry.npmjs.org` or `npm.pkg.github.com`. `owner` is forbidden for `registry.npmjs.org` and required for `npm.pkg.github.com`.                                          |
| `rubygems`       | `host` plus optional `owner` depending on host | `host` must be either `rubygems.org` or `rubygems.pkg.github.com`. `owner` is forbidden for `rubygems.org` and required for `rubygems.pkg.github.com`.                                            |

Validation must reject unknown destination keys, host values outside the allowed
family-specific set, and any missing or forbidden `owner` field under the host
rules above. For current scope, RubyGems publication to GitHub Packages must be
modeled as a host-specific `rubygems` target instance rather than as a separate
family or contract.

### `capabilities`

`capabilities` is the static planner-readable rule block for the target
instance. The minimum required keys are the frozen architecture capability
dimensions:

- `mutability`
- `name-uniqueness-scope`
- `version-uniqueness-rule`
- `profile-coexistence-rule`
- `credential-posture`

The current `v1alpha1` value vocabulary is:

| Capability key             | Allowed values                                                  |
| -------------------------- | --------------------------------------------------------------- |
| `mutability`               | `immutable`, `mutable-prerelease`, `replaceable`                |
| `name-uniqueness-scope`    | `release-tag`, `package-name`, `package-name-with-owner`        |
| `version-uniqueness-rule`  | `tag`, `version`, `package-name-plus-version`                   |
| `profile-coexistence-rule` | `same-name-allowed`, `requires-distinct-name`, `not-applicable` |
| `credential-posture`       | `oidc`, `github-token`                                          |

These values are catalog-owned. Project descriptors may not override them.

In current-scope `v1alpha1`, the capability tuple is also constrained by the
resolved family plus destination host. Static validation must require exactly
the following assignments:

| Family           | Destination discriminator                      | Required capabilities                                                                                                                                                                                                   |
| ---------------- | ---------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `github-release` | `host: github`, `owner: hcoona`, `repo: three` | `mutability: mutable-prerelease`; `name-uniqueness-scope: release-tag`; `version-uniqueness-rule: tag`; `profile-coexistence-rule: not-applicable`; `credential-posture: github-token`                                  |
| `nuget`          | `host: nuget.org`                              | `mutability: immutable`; `name-uniqueness-scope: package-name`; `version-uniqueness-rule: package-name-plus-version`; `profile-coexistence-rule: requires-distinct-name`; `credential-posture: oidc`                    |
| `nuget`          | `host: nuget.pkg.github.com`                   | `mutability: immutable`; `name-uniqueness-scope: package-name-with-owner`; `version-uniqueness-rule: package-name-plus-version`; `profile-coexistence-rule: requires-distinct-name`; `credential-posture: github-token` |
| `pypi`           | `host: pypi.org`                               | `mutability: immutable`; `name-uniqueness-scope: package-name`; `version-uniqueness-rule: package-name-plus-version`; `profile-coexistence-rule: requires-distinct-name`; `credential-posture: oidc`                    |
| `npm`            | `host: registry.npmjs.org`                     | `mutability: immutable`; `name-uniqueness-scope: package-name`; `version-uniqueness-rule: package-name-plus-version`; `profile-coexistence-rule: requires-distinct-name`; `credential-posture: oidc`                    |
| `npm`            | `host: npm.pkg.github.com`                     | `mutability: immutable`; `name-uniqueness-scope: package-name-with-owner`; `version-uniqueness-rule: package-name-plus-version`; `profile-coexistence-rule: requires-distinct-name`; `credential-posture: github-token` |
| `rubygems`       | `host: rubygems.org`                           | `mutability: immutable`; `name-uniqueness-scope: package-name`; `version-uniqueness-rule: package-name-plus-version`; `profile-coexistence-rule: requires-distinct-name`; `credential-posture: oidc`                    |
| `rubygems`       | `host: rubygems.pkg.github.com`                | `mutability: immutable`; `name-uniqueness-scope: package-name-with-owner`; `version-uniqueness-rule: package-name-plus-version`; `profile-coexistence-rule: requires-distinct-name`; `credential-posture: github-token` |

This closes the current-scope author-time rules that are otherwise only implied
by the frozen baseline:

- within package families, only GitHub Packages hosts may use
  `credential-posture: github-token`;
- all other current-scope package-registry hosts must use
  `credential-posture: oidc`;
- all current-scope package-registry hosts must use
  `profile-coexistence-rule: requires-distinct-name`, so same-registry same-name
  `buddy` and `official` publication stays forbidden at author time;
- `same-name-allowed` and `replaceable` remain out of scope for all current-scope
  package-registry target instances.

For that coexistence check, author-time static validation must compute the
resolved published package identity from the target family, destination host,
optional destination owner, and the published package name after applying any
allowed descriptor-side projection. Two target usages from the same project but
different profiles may not resolve to the same package-registry identity tuple,
even if they reference different catalog `instance-id` values.

## Ownership Boundaries

### Project-owned data

The project descriptor owns:

- participation in workflow release at all;
- stable `project.id` and display name;
- ecosystem and release kind;
- primary manifest path and auxiliary release inputs;
- canonical variants and artifact intents;
- profile-to-target bindings;
- target-side projection details for that project.

### Shared catalog-owned data

The shared target catalog owns:

- target family membership;
- target-instance identity inside each family;
- destination contract id;
- static destination locator data;
- static target capabilities and credential posture.

### Existing manifest-owned data

Language manifests remain authoritative for intrinsic package metadata unless a
project descriptor explicitly declares a target-side projection override. This
includes current-scope data such as:

- NuGet `PackageId` and package metadata in `.csproj` files;
- Python project name and build metadata in `pyproject.toml`;
- npm package name and publishable file set in `package.json`;
- Ruby gem name and metadata in `.gemspec`.

That split avoids duplicating package identity data in the release descriptor
while still letting the descriptor express deliberate target-side differences,
such as an npm scope rewrite for GitHub Packages.

Current-scope package-registry identity is resolved from those manifests through
this closed table before the planner emits `resolved-publish-identity`:

| Target family | Authoritative package-name source                                                               | Current-scope fallback and normalization rule                                                                                                                                                                                                                                                                        |
| ------------- | ----------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `nuget`       | The evaluated `PackageId` MSBuild property from the selected project's primary `.csproj`.       | No descriptor override and no MSBuild `AssemblyName` or directory-name fallback are allowed for release planning. If `PackageId` is absent or evaluates empty, a `nuget-publish` target is invalid. The planner preserves the evaluated spelling in the plan, while NuGet identity comparisons are case-insensitive. |
| `pypi`        | `[project].name` in the selected project's `pyproject.toml`.                                    | No descriptor override is allowed. The planner serializes the PyPI / PEP 503 normalized name: lowercase, then replace each maximal run of `.`, `-`, or `_` with one `-`.                                                                                                                                             |
| `npm`         | `projection.package-name` when declared for that target usage; otherwise `package.json` `name`. | The resolved value must be a valid publishable npm package name. Current-scope selected package names must be lowercase; scoped GitHub Packages names must keep a scope matching the catalog destination owner. The planner preserves the resolved spelling.                                                         |
| `rubygems`    | The selected `.gemspec`'s evaluated `Gem::Specification.name`.                                  | No descriptor override is allowed. Current-scope gem names must be lowercase and are serialized with their evaluated spelling.                                                                                                                                                                                       |

For all package-registry families, `resolved-publish-identity.version` comes from
the planner-frozen project `resolved-version`, not from a target-specific
descriptor field. Family-specific version normalization is used only for remote
identity and publish-time conformance checks; it does not introduce a second
author-time version source.

### Repo-layout-owned data

The monorepo layout remains authoritative for directory scope such as
`src/public/` versus the two explicitly in-scope private app roots. The project
descriptor does not repeat those path facts.

## Catalog Reference Model

A project descriptor references a shared target instance with a `uses` value of
`family/instance-id`.

The reference resolves as follows:

1. split the string at the single `/` separator;
2. look up the target family in `eng/release/target-instances.yml`;
3. find exactly one instance with the referenced `id` inside that family;
4. inherit that instance `contract`, `destination`, and `capabilities` into
   later planning.

A project target usage entry may add `projection`, but it may not redefine:

- target family;
- destination contract;
- destination locator data;
- target capabilities;
- credential posture.

This preserves the frozen architecture rule that projects own target usage, not
shared destination definitions.

## Current-Scope Contract-to-Artifact Compatibility

After resolving `uses` to a catalog target instance, author-time static
validation must check the referenced `artifacts` set against that instance's
resolved `contract`. Compatibility is defined by the current-scope
`role` / `kind-family` / `concrete-kind` tuple rules and aggregate cardinality
rules below.

| `contract`              | Allowed artifact tuples                                                                                                                                                                                                                                                     | Aggregate rules                                                                                                                                                                                                                                                                              |
| ----------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `github-release-assets` | `primary-package/package/nuget`, `symbols/package/snupkg`, `primary-package/package/wheel`, `primary-package/package/sdist`, `primary-package/package/npm-package`, `primary-package/package/rubygem`, `primary-binary/binary/executable`, `installer/installer/inno-setup` | One or more artifacts are required. Any mix of the allowed tuples may appear. Artifacts may come from one or more variants of the same project.                                                                                                                                              |
| `nuget-publish`         | `primary-package/package/nuget`, `symbols/package/snupkg`                                                                                                                                                                                                                   | Exactly one `primary-package/package/nuget` artifact is required. Zero or one `symbols/package/snupkg` artifact is allowed. All referenced artifacts must come from the same variant.                                                                                                        |
| `pypi-publish`          | `primary-package/package/wheel`, `primary-package/package/sdist`                                                                                                                                                                                                            | Exactly one `wheel` artifact is required. Zero or one `sdist` artifact is allowed. All referenced artifacts must come from the same variant. Planner-time PyPI admissibility still requires that the resolved output be exactly one pure-Python `py3-none-any` wheel plus an optional sdist. |
| `npm-publish`           | `primary-package/package/npm-package`                                                                                                                                                                                                                                       | Exactly one artifact is required, and it must come from exactly one variant.                                                                                                                                                                                                                 |
| `rubygems-publish`      | `primary-package/package/rubygem`                                                                                                                                                                                                                                           | Exactly one artifact is required, and it must come from exactly one variant.                                                                                                                                                                                                                 |

Validation must reject any target usage whose artifact set violates either the
allowed tuple set or the aggregate rules of its resolved contract. This keeps
current-scope contract compatibility authorable and statically checkable
without defining executor behavior. Planner-time admissibility then applies any
request-dependent or build-output-dependent gates, including the narrowed PyPI
pure-Python `py3-none-any` requirement.

For current .NET package projects, the repo-wide MSBuild configuration emits a
portable `.snupkg` alongside `.nupkg` when a packable library is packed. Project
descriptors for such package variants should therefore declare both the primary
NuGet artifact and the symbol artifact. GitHub Release target usages may carry
both package files as release assets. GitHub Packages NuGet buddy target usages
should also publish both artifacts rather than treating the buddy package target
as `.nupkg`-only. NuGet.org target usages should likewise reference both
artifacts when the release includes NuGet symbol publication, because `.snupkg`
is the modern separate symbol-package format. That in turn requires the
planner's NuGet.org adapter to implement and test symbol-package remote
observation before first live NuGet.org publication for those descriptors.

## Validation Boundary

The validation model is intentionally split into three layers.

### 1. File-schema validation

Single-file schema validation checks only the file being read.

Examples:

- YAML parses successfully;
- `api-version` and `kind` are valid;
- required sections exist;
- field types and enum values are valid;
- paths are relative and normalized for their field-defined base;
- within each variant, no two artifact entries share the same `role` /
  `kind-family` / `concrete-kind` tuple;
- `uses` has the required `family/instance-id` syntax;
- each catalog `contract` value is from the allowed current-scope vocabulary and
  is compatible with its enclosing family;
- each catalog `destination` object matches the closed family-specific shape and
  host rules;
- each catalog `capabilities` object matches the closed current-scope
  family-plus-host assignment table rather than merely using valid enum values.

### 2. Author-time static repo validation

Static validation may read all descriptors, the shared catalog, and the
project source files referenced by descriptors, but it still does not depend on a
specific release request or external registry state.

Examples:

- every discovered `project.id` is unique;
- every descriptor's `variants[].id` set is unique within that descriptor;
- every descriptor's variant semantic identity set is unique within that
  descriptor, so two variants cannot carry the same complete `dimensions` map
  under different ids;
- within each descriptor variant, the full semantic artifact identity tuple
  (`project.id`, enclosing variant's full `dimensions` map, `kind-family`,
  `concrete-kind`, `role`) is unique, so distinct `artifact.id` values
  cannot describe the same artifact;
- no git-tracked `three.release.yml` exists outside `src/`;
- descriptor roots are in scope and not nested;
- every `source.primary-manifest` resolves to an existing checked-in file under
  its descriptor's release root;
- every `source.auxiliary-inputs[]` entry resolves to an existing checked-in file
  under its descriptor's release root;
- every descriptor other than `nbgv-python` either omits
  `source.version-authority` or sets it to `build-system-nbgv`;
- `source.version-authority: nbgv-python-pyproject-version` appears only on the
  `nbgv-python` descriptor and therefore cannot become a broad manifest-version
  fallback for other Python projects;
- every catalog family has unique instance ids;
- every `uses` reference resolves to a catalog target instance;
- `artifacts` references resolve to declared artifact ids;
- non-zero-target profiles also declare `github-release`;
- any discovered `buddy` target usage that resolves to `pypi-publish` is
  statically invalid in current scope, because Python `buddy` remains
  GitHub Release-only while PyPI package publication is `official`-only;
- each resolved `projection` object matches the closed family-specific schema and
  host rules for its referenced target instance, including the rule that an
  `npm.pkg.github.com` target resolves to an npm package scope equal to the
  referenced catalog `destination.owner`;
- each target usage `artifacts` set satisfies the resolved contract's allowed
  tuple set and aggregate rules;
- the descriptor ecosystem matches the referenced primary manifest type from the
  closed current-scope mapping (`dotnet` -> `.csproj`, `python` ->
  `pyproject.toml`, `node` -> `package.json`, `ruby` -> `.gemspec`);
- every selected package-registry target can resolve its package name through the
  closed current-scope table in
  [Existing manifest-owned data](#existing-manifest-owned-data), including the
  current-scope rule that NuGet package publication requires an explicit
  non-empty `PackageId` rather than relying on MSBuild's pack default;
- cross-profile package-registry coexistence is evaluated from the resolved
  package-registry identity tuple (family, destination.host, destination.owner?,
  published-name) after combining descriptor data with manifest-owned default
  names and any npm `projection.package-name` override;
- because every current-scope package-registry target instance uses
  `profile-coexistence-rule: requires-distinct-name`, static validation must
  reject any one-project `buddy`/`official` pair that resolves to the same
  package-registry identity tuple.

This is the right layer for CI linting of checked-in authoring files.

### 3. Planner-time validation

Planner-time validation starts only after the author-time inputs are already
schema-valid and statically consistent. If any discovered in-scope release
descriptor is invalid at either earlier layer, planning must fail before any
release request is planned rather than silently dropping that project and
continuing.

Planner-time validation handles request-dependent or external-state-dependent
questions, such as:

- whether omitted or empty `requested-project-ids` selects the whole
  discovered in-scope releasable set, or an explicit non-empty set fully
  resolves; otherwise planning fails;
- whether the request selected `buddy` or `official`, because that request
  profile affects downstream publish decisions;
- the authoritative normalized planner-facing request contract for current
  scope: `profile`, `commit-sha`, normalized `requested-project-ids`, and
  normalized `request-flags.force`;
- project-scoped version identity for each selected project from its
  descriptor-declared authoritative version source: in current scope, that
  means build-system-integrated NBGV for every project except the single
  `nbgv-python` special-support path, which resolves version from the selected
  commit's checked-in `pyproject.toml` `[project].version`; manifest-owned
  static versioning is not a general fallback for other projects;
- whether each selected `official` publish intent that resolves to
  `pypi-publish` is admissible under the narrowed current-scope PyPI path:
  before accepting that node, the planner must mechanically verify that the
  selected project's checked-in `pyproject.toml` uses the `hatchling.build`
  backend, that the project's version at the selected commit resolves either
  through build-system-integrated NBGV or through the explicit
  `nbgv-python-pyproject-version` special-support path, and that the
  authoritative planner-time PyPI output resolution for that node yields
  exactly one `py3-none-any` wheel plus an optional sdist from one variant;
  otherwise planning must reject that PyPI node/project rather than infer or
  permit any alternate current-scope path;
- target-family-specific resolved publish identity derived from that
  project-scoped version identity plus the selected projection and manifest
  inputs, including GitHub Release tag derivation as
  `release/<project.id>/v<version>` with no extra tag split;
- `buddy FORCE` versus immutable-target rules;
- rerun skip decisions based on already-published remote state;
- external uniqueness conflicts that require checking the destination.

Planner-time validation does not own approval satisfaction, duplicate-run
cancellation, or other control-plane sequencing concerns.

Planner-time validation also resolves the external publication identity for
each selected publish intent and freezes that derived identity into the plan
(current-scope: `release-tag` for GitHub Release, or `package-name` plus
`version` for package registries). The normalized planner request is also the
whole-release rerun-equivalence basis at the plan layer, so changing
`request-flags.force` changes the request identity even when the selected
projects and commit stay the same. When immutable-target remote checks affect a
selected publication intent, planner-time validation produces the derived
per-publish-node outcome in the plan: closed current-scope `publish-disposition`
values plus, for live publish nodes, the planner-frozen publish mode. The raw
remote observations themselves are not author-time schema data and are not
frozen into target-instance snapshots or into observation records in the plan.

## Mapping Into the Frozen Architecture

This schema layer is the author-time input counterpart of the architecture page:

| Author-time declaration                           | Later architecture object                               |
| ------------------------------------------------- | ------------------------------------------------------- |
| project descriptor `project.id` plus release root | project ownership anchor in the plan envelope and graph |
| `variants[]` entry                                | `variant`                                               |
| `artifacts[]` entry                               | `artifact`                                              |
| profile target usage entry                        | `publish-node`                                          |
| catalog target instance                           | `target-instance-snapshot` source                       |

This gives Group 2 a complete author-time input model. The exact authoritative
planner output shape is now defined in
[Workflow Release Plan Shape](./workflow-release-plan-shape.md).

## Representative YAML Examples

### Shared catalog excerpt

```yaml
api-version: three.release/v1alpha1
kind: target-instance-catalog

families:
    github-release:
        instances:
            - id: public
              contract: github-release-assets
              destination:
                  host: github
                  owner: hcoona
                  repo: three
              capabilities:
                  mutability: mutable-prerelease
                  name-uniqueness-scope: release-tag
                  version-uniqueness-rule: tag
                  profile-coexistence-rule: not-applicable
                  credential-posture: github-token
    nuget:
        instances:
            - id: nuget-org
              contract: nuget-publish
              destination:
                  host: nuget.org
              capabilities:
                  mutability: immutable
                  name-uniqueness-scope: package-name
                  version-uniqueness-rule: package-name-plus-version
                  profile-coexistence-rule: requires-distinct-name
                  credential-posture: oidc
```

### Public .NET library descriptor excerpt

```yaml
api-version: three.release/v1alpha1
kind: project

project:
    id: hjg-pngcs
    display-name: Hjg.Pngcs
    ecosystem: dotnet
    release-kind: lib

source:
    primary-manifest: Hjg.Pngcs.csproj

variants:
    - id: package
      dimensions: {}
      artifacts:
          - id: nuget
            role: primary-package
            kind-family: package
            concrete-kind: nuget
          - id: snupkg
            role: symbols
            kind-family: package
            concrete-kind: snupkg

profiles:
    buddy:
        targets:
            - uses: github-release/public
              artifacts: [nuget, snupkg]
            - uses: nuget/github-packages
              artifacts: [nuget, snupkg]
    official:
        targets:
            - uses: github-release/public
              artifacts: [nuget, snupkg]
            - uses: nuget/nuget-org
              artifacts: [nuget, snupkg]
```

### Public .NET app descriptor excerpt

```yaml
api-version: three.release/v1alpha1
kind: project

project:
    id: image-occlusion-editor
    display-name: ImageOcclusionEditor
    ecosystem: dotnet
    release-kind: app

source:
    primary-manifest: ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj
    auxiliary-inputs:
        - ImageOcclusionEditorWinUI3/app.manifest
        - ImageOcclusionEditorWinUI3/packages.lock.json
        - LICENSE
        - LICENSE.GPL3.txt
        - LICENSE.MIT.txt
        - README.md
        - Resources/Template_IIOT.txt
        - Resources/Template_IIOTT.txt
        - THIRD-PARTY-NOTICES.TXT
        - imageocclusioneditor.ico
        - script/Build-InnoInstaller.ps1
        - script/Helpers.ps1
        - script/New-ThirdPartyNotices.ps1
        - script/Publish-ImageOcclusionEditor.ps1
        - script/Setup.iss

variants:
    - id: win-x64
      dimensions:
          os: windows
          rid: win-x64
      artifacts:
          - id: app-binary
            role: primary-binary
            kind-family: binary
            concrete-kind: executable
          - id: installer
            role: installer
            kind-family: installer
            concrete-kind: inno-setup
            produced-from: [app-binary]

profiles:
    buddy:
        targets:
            - uses: github-release/public
              artifacts: [app-binary, installer]
    official:
        targets:
            - uses: github-release/public
              artifacts: [app-binary, installer]
```

### Public Node package descriptor excerpt

```yaml
api-version: three.release/v1alpha1
kind: project

project:
    id: hexo-renderer-asciidoc
    display-name: hexo-renderer-asciidoc
    ecosystem: node
    release-kind: lib

source:
    primary-manifest: package.json

variants:
    - id: package
      dimensions: {}
      artifacts:
          - id: npm-package
            role: primary-package
            kind-family: package
            concrete-kind: npm-package

profiles:
    buddy:
        targets:
            - uses: github-release/public
              artifacts: [npm-package]
              projection:
                  asset-labels:
                      npm-package: hexo-renderer-asciidoc npm package
            - uses: npm/github-packages
              artifacts: [npm-package]
              projection:
                  package-name: '@hcoona/hexo-renderer-asciidoc'
    official:
        targets:
            - uses: github-release/public
              artifacts: [npm-package]
            - uses: npm/npmjs
              artifacts: [npm-package]
```

## Related Pages

- [Workflow Release Design Direction](./workflow-release-design-direction.md)
- [Workflow Release Architecture Model](./workflow-release-architecture-model.md)
- [Workflow Release Plan Shape](./workflow-release-plan-shape.md)
- [Repository Release Landscape](./repository-release-landscape.md)
- [Workflow Release Requirements Baseline](./workflow-release-requirements-baseline.md)
