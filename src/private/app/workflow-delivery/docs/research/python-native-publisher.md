# Python Native Publisher Integration

## Question and Evidence Level

Use the maintained UV publisher for the existing TestPyPI Buddy and PyPI Official
destinations, keeping application qualification, observation and business
completion separate from native publication. This is pinned public-source
research and bounded integration inference. It does not qualify a newly executed
Release request, current publisher configuration or deployed runner behavior.
The closed Python campaigns keep their original source, client and run identities;
their operation slots remain spent.

The [MLD destination contract](../middle-level-design.md#destination-interface)
permits one application invocation per still-needed original, including supported
bounded native retries. A returned failure, ambiguity or interruption stops
subsequent application writes. This is not transport-level exactly-once delivery.

## Pinned Native Sources

UV 0.12.24 resolves to
`5411378eb76dc1ea1ad90aeb10e84e997e5bbd96`. The source findings below concern that
revision, rather than an inferred copy of its publication implementation. The
[UV 0.13.1 recheck](#uv-01224-to-0131-recheck) compares them with the later pin.

| Source                                                                  | Finding                                                                                                                                                                                             |
| ----------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [Publish command][command], `publish` and `publish_files`               | Prepares distributions and credentials before the dry-run file loop; native live processing stops at the first returned failure.                                                                    |
| [Native publisher][publisher], `PublishSession` and upload handling     | Owns distribution preparation, multipart upload, supported bounded retry, existing-file checking and same-realm redirect handling.                                                                  |
| [CLI arguments][cli], `PublishArgs`, lines 7362–7496                    | Exposes explicit file paths, publish/check URLs, trusted-publishing selection, disabled keyring and no-attestation options. Native existing-file checking is optional.                              |
| [Trusted-publishing provider][trusted], `get_token`, lines 147–168      | Gets the audience from the selected registry, delegates ambient OIDC discovery and exchanges a short-lived publication token. On GitHub Actions, emits the native `::add-mask::` command on stdout. |
| [PyPI service][pypi] and [command finalization][command], lines 166–186 | Native finalization attempts token invalidation. A successful exit does not prove that invalidation succeeded or that a token was revoked.                                                          |

UV's maintained `ambient-id` 0.0.11 dependency owns GitHub provider discovery.
The original public crate has SHA-256
`c1daa54020e05aa0b163ee10434fff35a0f18d28a1cafa142bd1290e1abe630e`, matching
the UV Cargo.lock entry. Its `src/github.rs` selects GitHub when
`GITHUB_ACTIONS` is exactly `true` and consumes
`ACTIONS_ID_TOKEN_REQUEST_URL` and `ACTIONS_ID_TOKEN_REQUEST_TOKEN`. The retained
source was extracted without execution. Do not implement a second OIDC client,
audience mirror or token-verification service.

The original public source responses, retrieval requests, source hashes and
independent assessments remain in the delivery work carrier. They establish
source behavior, not this repository's effective native authorization.

## Development-Branch Native Version Projection

The current Python project's non-inheriting `version.json` matches only
`refs/heads/main` for public-release mode and selects `0.1.0-beta.{height}`.
Its backend selects maintained `nbgv-python` `SemVer2` normalization. Current
configuration does not establish development-branch index compatibility.

Pinned NBGV 3.10.94 source at
`dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb` provides the supported boundary:
[VersionOptions][version-options], lines 490–507, defines committed
`publicReleaseRefSpec`; [VersionOracle][version-oracle], lines 106–119 and
539–552, matches those refs and appends the non-public Git suffix only outside
public-release mode. The maintained plugin converts extra prerelease segments
such as `.gSHORT` into a PEP 440 local part. This is an inference from native
and plugin source, not a new branch query or package-build observation.

Pinned Warehouse source at
`f43a0f79dbaf40e4888110ad5b1d5e0ad87121b6` rejects local versions in
[metadata validation][warehouse-metadata], lines 176–184; the
[upload entry][warehouse-upload], lines 624–662, returns invalid metadata as
HTTP Bad Request. It establishes the source constraint, rather than a newly
observed deployed rejection.

Use committed native public-version ref configuration for the selected Buddy
branches, preserving base/prerelease/height and the same CI/Release projection.
Public-version mode is not Official channel authorization. Detached checkout
queries still need faithful selected native ref/commit context; extra local
metadata remains inadmissible for live publication. Do not strip a suffix in
the workflow/application or change global plugin normalization. Equal public
versions across commits remain possible and conflicting originals remain
conflicts. The concrete contract selects the ref set; later component/native
qualification must establish the actual projection and original consumers.

## Remote Native Consumption Boundary

The same pinned UV revision provides native exact-version/archive-hash consumption.
[`HashCheckingArgs`][consumer-cli] requires pinned hashed requirements;
[`pip::install`][consumer-install] creates the trusted hash strategy and passes it
through native resolution and installation. [`HashStrategy`][consumer-hash]
requires a matching trusted archive digest.
[`Preparer`][consumer-preparer] rejects a selected archive that does not satisfy
that policy. [`SourceDistributionBuilder`][consumer-sdist] validates the original
sdist digest before running its build.

Use one generated exact-version/SHA-256 requirement, one selected index, the
required wheel/sdist format flag, no config, no inherited index/source/credential
inputs, no populated product cache and an initially absent product. Successful
native installation under those inputs acknowledges native hash enforcement;
the existing isolated helper then checks installed metadata, origin and API.
The sdist's noneditable tools still use the same frozen native recipe. This is
source-backed integration inference, not a new index or install observation.

UV's experimental installation JSON reports name, version and change action;
it does not carry original archive hashes or provenance. Do not turn that JSON
into another durable trust record. Preserve actual command outcomes and required
selection/hash inputs instead. Missing or failed native acknowledgement blocks
consumption; a local-original installation cannot establish remote routing.
The [concrete contract](../integration/python.md#concrete-python-release-integration)
retains component/caller qualification and separate live authorization.
`PythonNativeContractTests` guards the consumed UV contracts at the locked pin: the root-lock
export is hash-pinned and registry-only and includes the locked `packaging`, and
the exact install flags stop on a missing or different hash and install a
matching one. Installation from pypi.org needs the network and stays a
non-claim, covered only by the dispatch-only smoke workflow.

## Credential and Output Boundary

Application dry run uses qualification, public observation and hypothetical
operations without publication authority. It must not invoke `uv publish
--dry-run`: credential collection occurs before UV reaches its dry-run file
loop. Do not acquire OIDC merely to validate a development release request.

The existing generic native-process result captures stdout and stderr. Blindly
serializing that result for trusted publication would persist the exchanged
token from UV's masking command. Runner log masking does not sanitize an
uploaded result file.

Pinned Actions runner source
`67f01c276e0a91d967ba499ce4cdc9a95efa0262` provides the relevant source contract:

- [ScriptHandler][script], lines 332–336, connects both stdout and stderr to
  `OutputManager`.
- [OutputManager][output], lines 77–90, processes workflow commands before
  ordinary logging.
- [AddMaskCommandExtension][mask], lines 420–450, suppresses command echo and
  registers the supplied secret with the native masker.

Forwarding a complete native masking command through application stderr can
therefore preserve a strict JSON stdout payload. This is an inference from the
pinned runner source, not a deployed-runner observation. Forward only the
supported masking command; do not replay arbitrary child workflow commands.
Never persist token-bearing control lines, credential environment values or
unsanitized publication diagnostics, including partial failure/cancellation
output. Omit native diagnostic text when safe retention cannot be established,
while preserving actual termination, exit code, elapsed time and effect
uncertainty. Omission is a diagnostic limitation, not a fabricated successful
command outcome.

## Integration Consequences and Limits

Invoke UV for one explicit qualified original, without wildcard expansion,
application resend or a custom upload protocol. Required complete-state
verification remains independent of UV's native existing-file check. A native
command can succeed without creating a new file; a failed command can coexist
with satisfied observed state. Neither outcome attributes unique insertion.

Wheel/sdist ordering and supported exact-subset recovery follow the
[MLD](../middle-level-design.md#decision-table). Fresh recovery rebuilds and
qualifies its own complete set from the original revision. Native package
identity and provenance remain owned by packaging, the backend and NBGV; no
application witness or source-code bundle is added.

The [concrete Python contract](../integration/python.md#concrete-python-release-integration)
owns selected flags, process/configuration isolation, destination reads,
consumers, deadlines and workflow transfer. Controlled integration tests and
ordinary CI qualify only their stated scope. Any live validation, publisher
configuration change or publication requires separate bounded authorization.

`PythonNativeContractTests.TrustedPublishingUploadsOnceAndForwardsTheTokenMask`
runs the locked UV with the publisher's exact command against a loopback HTTPS
registry and token service with fake identities, and a closed proxy for any
non-loopback request. It asserts that the flags are accepted and that the exchange
is audience, OIDC token request, mint, exactly one upload of the selected original
bytes with the minted token, then token invalidation. It also asserts that the
forwarded runner output is exactly the minted token's `::add-mask::` line.
`NativeUploadHandlingStaysBoundedAndFailsClosed` guards the native upload handling
the publisher relies on instead of an application retry. A 503, a 500 and the
registry's `400 File already exists` answer each end the command as a failure
after one upload. A connection reset is retried a bounded four uploads in total
before failing. A same-realm redirect is followed with the token, and a redirect
to another realm fails without sending it there. The existing-file row covers
UV's handling of a registry rejection; the optional pre-upload
`--check-url` check is not selected and is not guarded. The publisher passes one
file per invocation, so stopping at the first failure across several files is
outside the consumed contract. Warehouse token minting and acceptance, the real
GitHub OIDC provider and runner masking remain non-claims, bound to the recorded
runs. A change to runner command
handling is evaluated by its author. Missing capability evidence blocks the
changed path rather than expanding the application into another publisher.

## UV 0.12.24 to 0.13.1 Recheck

The selected UV changed from 0.12.24 to 0.13.0 in
[PR #1155](https://github.com/hcoona/three/pull/1155) and to 0.13.1 in
[PR #1238](https://github.com/hcoona/three/pull/1238). Tag `0.13.1` resolves to
commit [`62faed3351cfee2121b3c28b937b1f4b93233a93`][uv-0131]. This recheck compares the cited files between
`5411378eb76dc1ea1ad90aeb10e84e997e5bbd96` and that commit; no UV 0.13.1 binary, publisher or index was
executed for it.

| Cited source                                                                   | 0.12.24 → 0.13.1                         | Reviewed verdict                                                                                                                                        |
| ------------------------------------------------------------------------------ | ---------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `crates/uv-publish/src/trusted_publishing.rs` and `trusted_publishing/pypi.rs` | Identical                                | Audience, OIDC exchange, masking and token invalidation findings unchanged                                                                              |
| `crates/uv-cli/src/lib.rs` `PublishArgs`                                       | Identical struct; file changed elsewhere | Publish arguments unchanged. `HashCheckingArgs` gains only a documentation pointer to the new opt-in `--require-build-hashes`                           |
| `crates/uv-publish-commands/src/lib.rs`                                        | Changed (2 lines)                        | Passes `TarBackend::from_env()` to the session; the retry policy, file loop and finalization are unchanged                                              |
| `crates/uv-publish/src/lib.rs`                                                 | Changed                                  | See the `TarBackend` verdict below; upload, existing-file checking and redirect handling are unchanged                                                  |
| `crates/uv-pip-commands/src/install.rs`                                        | Changed                                  | Build-dependency hash checking becomes a separate, resolved mode; runtime `HashStrategy::from_requirements` for pinned hashed requirements is unchanged |
| `crates/uv-types/src/hash.rs`, `crates/uv-installer/src/preparer.rs`           | Identical                                | Hash strategy and archive rejection unchanged                                                                                                           |
| `crates/uv-distribution/src/source/mod.rs`                                     | Changed                                  | Build-settings cache sharding moves before the metadata cache lookup; every "check that the hashes match" guard before a build remains                  |

UV's `ambient-id` dependency remains 0.0.11 in both lockfiles.

`TarBackend` verdict: at 0.12.24 the publisher read an sdist's `PKG-INFO` with the
tar-codec reader only under a preview feature and otherwise with `tokio-tar`. At
0.13.1 `TarBackend::from_env()` selects the tar-codec reader by default and
`tokio-tar` only when `UV_LEGACY_TAR_BACKEND` is true. Both readers keep the
same single top-level `PKG-INFO` requirement and its tests. The change affects
how UV reads upload metadata from the qualified sdist, not which file is
uploaded, the one-invocation boundary, retries or the application's independent
original-byte verification. No finding above depends on the tar reader.

Conclusion: the source basis of this record is unchanged at UV 0.13.1 for the
publisher, trusted-publishing and native consumption findings. This recheck
establishes no publisher, index, runtime, Windows or live-publication behavior.
Its dependency-change review is recorded in the governing pull request.

[command]: https://github.com/astral-sh/uv/blob/5411378eb76dc1ea1ad90aeb10e84e997e5bbd96/crates/uv-publish-commands/src/lib.rs
[publisher]: https://github.com/astral-sh/uv/blob/5411378eb76dc1ea1ad90aeb10e84e997e5bbd96/crates/uv-publish/src/lib.rs
[cli]: https://github.com/astral-sh/uv/blob/5411378eb76dc1ea1ad90aeb10e84e997e5bbd96/crates/uv-cli/src/lib.rs#L7362
[trusted]: https://github.com/astral-sh/uv/blob/5411378eb76dc1ea1ad90aeb10e84e997e5bbd96/crates/uv-publish/src/trusted_publishing.rs#L147
[pypi]: https://github.com/astral-sh/uv/blob/5411378eb76dc1ea1ad90aeb10e84e997e5bbd96/crates/uv-publish/src/trusted_publishing/pypi.rs
[script]: https://github.com/actions/runner/blob/67f01c276e0a91d967ba499ce4cdc9a95efa0262/src/Runner.Worker/Handlers/ScriptHandler.cs#L332
[output]: https://github.com/actions/runner/blob/67f01c276e0a91d967ba499ce4cdc9a95efa0262/src/Runner.Worker/Handlers/OutputManager.cs#L77
[mask]: https://github.com/actions/runner/blob/67f01c276e0a91d967ba499ce4cdc9a95efa0262/src/Runner.Worker/ActionCommandManager.cs#L420
[version-options]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/VersionOptions.cs#L490
[version-oracle]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/VersionOracle.cs#L106
[warehouse-metadata]: https://github.com/pypi/warehouse/blob/f43a0f79dbaf40e4888110ad5b1d5e0ad87121b6/warehouse/forklift/metadata.py#L176
[warehouse-upload]: https://github.com/pypi/warehouse/blob/f43a0f79dbaf40e4888110ad5b1d5e0ad87121b6/warehouse/forklift/legacy.py#L624
[consumer-cli]: https://github.com/astral-sh/uv/blob/5411378eb76dc1ea1ad90aeb10e84e997e5bbd96/crates/uv-cli/src/lib.rs#L6792
[consumer-install]: https://github.com/astral-sh/uv/blob/5411378eb76dc1ea1ad90aeb10e84e997e5bbd96/crates/uv-pip-commands/src/install.rs
[consumer-hash]: https://github.com/astral-sh/uv/blob/5411378eb76dc1ea1ad90aeb10e84e997e5bbd96/crates/uv-types/src/hash.rs
[consumer-preparer]: https://github.com/astral-sh/uv/blob/5411378eb76dc1ea1ad90aeb10e84e997e5bbd96/crates/uv-installer/src/preparer.rs#L174
[consumer-sdist]: https://github.com/astral-sh/uv/blob/5411378eb76dc1ea1ad90aeb10e84e997e5bbd96/crates/uv-distribution/src/source/mod.rs#L665
[uv-0131]: https://github.com/astral-sh/uv/tree/62faed3351cfee2121b3c28b937b1f4b93233a93
