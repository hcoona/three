# nbgv-python Hatch Integration Design

This scoped design owns the adapter's configuration, version-projection,
templating, and diagnostic rationale. Library contributors produce and maintain
it with adapter changes; implementers and reviewers use it to distinguish NBGV
metadata from Python packaging values and to interpret existing tests. The
[overview](overview.md) owns component boundaries and records the original
design provenance and unresolved API expectations. The [README](../../README.md)
remains the package reader's configuration reference; this record supplies the
internal rationale and evidence limits that a usage example cannot carry.
Implementation changes require their own accepted Delivery Wave grant.

## Functional Scope

The original design combines a reusable metadata API, a forwarding console
script, a Hatch `VersionSourceInterface` plugin, and diagnostics/configuration
for missing tools in automation. Those concerns remain together through the
shared runner. The adapter projects NBGV output into a Python package version;
it does not calculate Git version height or set the repository version itself.
`NbgvVersionSource.set_version()` raises `NotImplementedError`.

## Module Breakdown

The [component map](overview.md#key-components) identifies the current modules
corresponding to the original design. `config.py` separates configuration
parsing from `hatch_plugin.py`; normalization, template construction, and file
writing remain reusable outside Hatch. This retains the `versioningit`-inspired
boundary between adapter and core described in the original plan without
promising compatibility with that package.

## Data Contracts

- [Configuration](../../README.md#hatch-integration) remains under
  `[tool.hatch.version.nbgv]`. The original command override, `SemVer2` default,
  project-root default, optional epoch, and NBGV/PEP 440 tuple modes are retained.
  [config.py](../../src/nbgv_python/config.py) also parses `write`, rejects
  unknown keys, resolves relative project and output paths against the Hatch
  root, and accepts absolute paths. The original `null` defaults mean Python
  `None`; TOML consumers omit those keys. The top-level numeric epoch and the
  tuple's optional boolean epoch switch are distinct settings.
- [GitVersion](overview.md#data-contracts) preserves the CLI metadata view.
  The plugin obtains both aliases and original names via
  `as_dict(include_raw=True)`. The selected raw value remains template
  `version`; `normalized_version` is the packaging projection, including a
  configured epoch. Hatch's returned `version` is the normalized value.
  This distinction preserves the documented [2.0.0 compatibility change](../../CHANGELOG.md#200---2025-12-18).
  There is an unresolved implementation qualification: `build_template_fields()`
  merges metadata after creating these fields, so a colliding metadata key can
  replace a generated value. Existing fixtures do not settle that collision
  behavior. The documented raw/normalized contract is retained; no precedence
  change is approved by this migration.
- [versioning.py](../../src/nbgv_python/versioning.py) first tries
  `packaging.version.Version`, then its SemVer fallback. Known prerelease
  labels map to PEP 440 forms such as `beta.1` to `b1`. Unrecognized labels use
  `.dev` and local metadata to retain prerelease ordering; for example the
  existing test expects `1.2.3-gabcdef` to become `1.2.3.dev0+gabcdef`.
  Complex unrecognized tags can raise `NbgvVersionNormalizationError` rather
  than silently become a release. This follows the changelog's ordering
  rationale, not a guarantee that every SemVer string is representable.
- [Template fields](../../README.md#template-fields) include raw CLI metadata
  plus `version`, `normalized_version`, and a Python tuple expression.
  The default tuple uses `VersionMajor`, `VersionMinor`, `BuildNumber`, and
  `PrereleaseVersionNoLeadingHyphen`; empty and null values are skipped, but a
  missing field raises an error. PEP 440 mode derives components from the
  normalized version instead. Quoting, normalized prerelease, and epoch
  options preserve the existing configuration and tests. Singleton tuples
  keep the trailing comma; booleans render as Python literals, as recorded
  in the 2.0.0 changelog.
- [File writing](../../README.md#writing-version-files) is already implemented
  in [writer.py](../../src/nbgv_python/writer.py). It applies `str.format`,
  creates parent directories, writes with the requested encoding (UTF-8 by
  default), and appends a newline. Default Python templates prefer
  `normalized_version`, falling back to `version`; text or extension-less
  targets use `version`, while other suffixes need an explicit template.
  Writing occurs inside `get_version_data()`, so version inspection may write
  the file even without a separate build hook. Distribution inclusion remains
  the consuming project's responsibility.

## Error Handling Strategy

[errors.py](../../src/nbgv_python/errors.py) retains the original plan's typed
failures: `NbgvNotFoundError` includes tool-installation/override guidance,
`NbgvCommandError` records the command and exit status, and `NbgvJsonError`
retains malformed output. Captured version queries retain stdout and stderr;
forwarding inherits terminal streams, so its error need not carry captured
stderr. `cli.main()` logs command context, propagates a non-zero CLI status,
and returns 127 for `NbgvNotFoundError`.

The missing-tool design claim is broader than the current resolver: explicit
overrides are parsed without checking that the executable exists, and the
`dotnet` fallback is selected without checking for an installed `nbgv` tool.
[runner.py](../../src/nbgv_python/runner.py) wraps `CalledProcessError`, not
every `OSError`. A missing override executable can therefore escape as an OS
error. Whether all missing-tool paths should share `NbgvNotFoundError` remains
unresolved; the migration neither changes error behavior nor withdraws the
original diagnostic intent.

The Hatch adapter wraps `NbgvError` from discovery, retrieval, and normalization
as plain `RuntimeError` with the original cause. This allows Hatchling to
reconstruct the error while retaining diagnostics, the rationale behind the
[unreleased changelog entry](../../CHANGELOG.md#unreleased). Configuration
parsing, missing selected fields, tuple construction, and writer failures have
their own error paths; this wrapper does not promise to convert every error.

## Testing Plan and Existing Evidence

The original plan chose temporary Python CLI scripts so wrapper checks need
not depend on a .NET installation. Existing tests retain that separation:

| Concern                       | Existing evidence and limit                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |
| ----------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Command discovery             | [test_command.py](../../tests/test_command.py) exercises string/list token parsing, environment precedence, the .NET fallback, and total lookup failure. It does not establish that every discovered executable can run.                                                                                                                                                                                                                                                                        |
| JSON and mapping              | [test_runner.py](../../tests/test_runner.py) parses a successful stub response and checks the absolute project argument; [test_models.py](../../tests/test_models.py) checks aliases including `NuGetPackageVersion` and `SemVer2`, missing keys, and mapping iteration. The old plan's malformed-JSON parsing coverage is not present as a direct runner test.                                                                                                                                 |
| Adapter configuration         | [test_config.py](../../tests/test_config.py) checks defaults, command/directory parsing, unknown keys, template/write settings, and invalid epochs. [test_hatch_plugin.py](../../tests/test_hatch_plugin.py) directly instantiates the plugin with synthetic runners to exercise field selection, normalization, epoch application, writing, and Hatch-compatible errors. Its invalid-JSON runner raises an already constructed `NbgvJsonError`; that is adapter evidence, not parser evidence. |
| Version and template behavior | [test_versioning.py](../../tests/test_versioning.py), [test_templating.py](../../tests/test_templating.py), and [test_writer.py](../../tests/test_writer.py) exercise prerelease conversion/rejection, tuple modes, quote/epoch options, boolean and singleton rendering, and template defaults.                                                                                                                                                                                                |
| Console forwarding            | [test_cli.py](../../tests/test_cli.py) calls `cli.main()` with stubs and checks exit codes and diagnostic logging. The plan's proposed `CliRunner`-style or `python -m nbgv_python.cli` invocation is not an existing subprocess-entry-point test.                                                                                                                                                                                                                                              |

These sources support bounded implementation claims, not an end-to-end NBGV,
Hatch plugin discovery, operating-system support, or publication claim. The
unimplemented parts of the original testing plan remain unverified; this
record does not authorize new tests or runtime experiments. The separate
sample owns its usage and limitations and is not the validation authority for
the library.
