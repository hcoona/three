# Ruby Smoke Source Evidence

The V3 maintainer maintains this record for Ruby requirements/design reviewers
and later implementation/admission reviewers under [Issue #954][issue]. It owns
source findings and their limits, not runtime support, package ownership or
operation permission. Sources were inspected on 2026-09-30 after the Ruby
design Wave was accepted. No Ruby package build, registry probe, authentication
change or publication was performed in this research.

## Existing Project and Destination Support

**Repository observation:** accepted tree `18b5da1d53beaae7e83da93273cb61035a46d23d`
has npm, NuGet and Python smoke products but no Ruby smoke gem or Ruby-specific
V3 design. Its only product `.gemspec` belongs to `asciidoctor-latexmath`, which
has separate consumers and requirements. Old Ruby release workflows do not
establish V3 contracts. The owner selected creation of a minimal Ruby smoke
when none exists; namespace availability and remote ownership remain unverified.

**Public-source finding:** [GitHub's RubyGems registry guide][github] explicitly
supports publishing and installing gems at
`https://rubygems.pkg.github.com/NAMESPACE`. It documents repository association
through the `github_repo` metadata field, Actions `GITHUB_TOKEN` publication
for associated packages, and authenticated package installation. New packages
default to private visibility; linking and inheritance affect effective access.
This supports the owner's selected destination order: GitHub Packages first,
then RubyGems.org. It does not establish this package's existence, grant,
visibility, current authentication or exact-byte readback behavior.

## Version Projection

**Pinned-source findings:** NBGV 3.10.94's [VersionOracle][nbgv] forms `SemVer2`
from the three-part version, prerelease and configured build metadata. Its
`SemVer2BuildMetadata` and `GitCommitIdShortForNonPublicPrereleaseTag` append the
non-public commit identifier to the prerelease; the stale XML comment mentioning
a `+COMMITID` suffix is not the implementation. Explicit build metadata can
still introduce `+`.

RubyGems 4.0.20's [Gem::Version][version], `VERSION_PATTERN` and `initialize`,
accepts a narrower native version syntax and normalizes hyphens to `.pre.`.
It rejects `+` build metadata. Thus `Gem::Version.new("0.1.0-beta.7").to_s`
is specified by that source as `0.1.0.pre.beta.7`; this is a source-derived
example, not an executed test. Native version equality may collapse spellings
such as trailing zero segments. Exact strings alone are not sufficient to
classify remote-coordinate conflicts.

**Design implication:** a bounded Provider can freeze raw `SemVer2` and its
native RubyGems normalization, rejecting unsupported input rather than stripping
metadata or deriving a version from a workflow run. No evidence supports a
built-in NBGV Ruby projection or a need for a new general-purpose versioning
package. Full-history, exact-target NBGV remains the upstream version authority.

## Original-Package Reproducibility

**Pinned-source findings:** RubyGems 4.0.20 provides native mechanisms:

- [Gem::Package][package] initializes package build time from
  `Gem.source_date_epoch`, assigns that time to gzip headers and writes
  metadata, payload and checksums without an external repacking step.
- [Gem::Package::TarWriter][tar] uses `Gem.source_date_epoch` for tar member
  timestamps. File modes and payload still matter.
- [Gem::Specification][specification], `files`, `normalize`, `date` and
  serialization methods, sorts file collections and obtains the default
  specification date from the same source epoch.

**Inference:** a pure-Ruby smoke with a fixed file closure, stable metadata,
normalized staged modes, exact native toolchain/compression inputs and a
target-commit-derived `SOURCE_DATE_EPOCH` can use these native mechanisms as
the proposed reproducible-build path. This is not a claim that two builds have
matched. Future acceptance must compare original `.gem` bytes from independent
clean roots and reject differences; normalized/repacked archives cannot provide
that evidence. There is no cross-platform, cross-toolchain or signed-package
equality claim.

## Publication and Observation Interfaces

**Pinned-source findings:** the [RubyGems.org API guide][api] documents binary
gem upload through `POST /api/v1/gems`, version inventories through
`GET /api/v1/versions/<name>.json`, and exact-version/platform lookup through
`GET /api/v2/rubygems/<name>/versions/<version>.json?platform=ruby`. The latter
returns `yanked`, native identity, SHA and a gem URL. Metadata SHA is an
additional check, not a replacement for downloading and comparing the original
archive and witness.

RubyGems.org's pinned [Pusher][pusher], in version admission around lines
115-150, includes an indexed-existing-record success path returning HTTP 200
and separate rejection of existing version/platform/ABI coordinates. The lookup
includes hashes, size and pusher/API-key fields: this does **not** promise that
every byte-identical request under a fresh credential succeeds. Nor does a
successful response prove that this request inserted a new version. The pinned
repository is public implementation evidence, not proof of the exact deployed
service revision, all concurrency schedules or universal future behavior.

The [yank guide][yank] states that yanking removes availability and the gem file.
Neither destination supplies indefinite retention through this research.
GitHub's publication/installation guide does not itself specify a complete
atomic non-overwrite or concurrent-creation contract. npm/NuGet observations
cannot fill that Ruby-specific evidence gap.

**Pinned-source finding:** RubyGems 4.0.20's [GemcutterUtilities][gemcutter],
`rubygems_api_request`, can repeat a request after an MFA challenge or API-key
scope update. Therefore an ordinary `gem push` invocation is not, by its name
alone, evidence of V3's one-shot upload contract. Select and validate an explicit
bounded transport before implementation; no successful duplicate response or
CLI skip mode may repair a failed Attempt.

**Pinned-client findings:** RubyGems 4.0.20's [Source reader][native-source],
`load_specs`, selects `specs.4.8.gz` and `prerelease_specs.4.8.gz` relative to
the registry base and uses `Gem::SafeMarshal` for decoded native tuples. Its
[RemoteFetcher][fetcher] constructs `gems/<native-filename>` for downloads.
These native-client paths provide the GitHub adapter's initial protocol basis
alongside GitHub's documented native-client support. They do not prove actual
GitHub responses or redirect origins; activation must bind the supported
profile and retain actual reads. Use the maintained safe native reader in an
unprivileged process, not unrestricted Marshal deserialization in control.

The pinned [credential action's exchange implementation][credential-action]
posts JSON `jwt` to `/api/v1/oidc/trusted_publisher/exchange_token` after obtaining
a GitHub assertion for the selected audience. Reusing this source contract does
not attest a configured publisher or permit logging credential response bodies.

## RubyGems.org Trusted Publishing

**Pinned-source findings:** the [Trusted Publishing guide][trusted] documents
existing-gem and pending-gem GitHub publishers. Registration binds repository,
workflow and optionally an Environment; a pending publisher converts after
first successful push and adds its account as owner. Cross-repository reusable
workflows have separate workflow-repository fields. These configuration facts
must be inspected for the actual future tuple; another ecosystem's publisher
registration is not transferable.

The service's pinned [token exchange controller][exchange] produces a
`push_rubygem` key with a 15-minute lifetime after issuer, signature, claims
and publisher checks. Documentation describes gem-scoped short-lived access.
This finding supplies neither an account confirmation nor a configured publisher.
The owner separately confirmed control of account `Raffle2282` in Issue #954;
no configured pending publisher or package ownership is claimed.
First-project creation requires its own later bounded authority and evidence.

## Rechecks and Evidence Limits

Before implementation or admission relies on mutable destination support,
authentication, publisher behavior or registry APIs, the implementer rechecks
the official guide and the selected native versions; an independent evidence
reviewer assesses material changes. Changed toolchain/profile, service behavior,
ownership or publication tuple triggers the affected recheck. The next Wave
change selecting Ruby implementation or operation is the fallback review event.
Record results in its Issue/PR and update this record if a current conclusion
changes. Pinned source identity does not expire, but applicability can change.

Requirements own the [explicitly owner-selected Ruby platform reliance](../requirements.md#ruby-smoke-slice)
and future evidence gates. This decision does not convert the source findings
into deployed observations. This record alone neither waives a gate nor
authorizes probes.

[issue]: https://github.com/hcoona/three/issues/954
[github]: https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-rubygems-registry
[nbgv]: https://github.com/dotnet/Nerdbank.GitVersioning/blob/dea9a6c17cd9bd2dab3a87f2d1f9098735c820cb/src/NerdBank.GitVersioning/VersionOracle.cs#L452
[version]: https://github.com/ruby/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/version.rb#L159
[package]: https://github.com/ruby/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/package.rb
[tar]: https://github.com/ruby/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/package/tar_writer.rb
[specification]: https://github.com/ruby/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/specification.rb
[api]: https://github.com/rubygems/guides/blob/299c071b2e7aa1612d94f4bb17018acc179fcd67/rubygems-org-api.md
[pusher]: https://github.com/rubygems/rubygems.org/blob/36e040d0c8a3282959bff078894a5d458f991472/app/models/pusher.rb#L115
[yank]: https://github.com/rubygems/guides/blob/299c071b2e7aa1612d94f4bb17018acc179fcd67/removing-a-published-gem.md
[gemcutter]: https://github.com/ruby/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/gemcutter_utilities.rb#L91
[trusted]: https://github.com/rubygems/guides/blob/299c071b2e7aa1612d94f4bb17018acc179fcd67/trusted-publishing.md
[exchange]: https://github.com/rubygems/rubygems.org/blob/36e040d0c8a3282959bff078894a5d458f991472/app/controllers/api/v1/oidc/trusted_publisher_controller.rb
[native-source]: https://github.com/ruby/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/source.rb#L166
[fetcher]: https://github.com/ruby/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/remote_fetcher.rb#L149
[credential-action]: https://github.com/rubygems/configure-rubygems-credentials/blob/8674aee7f9f9055a8b771794d6f1e66b5fad36a9/src/oidc/trustedPublisher.ts
