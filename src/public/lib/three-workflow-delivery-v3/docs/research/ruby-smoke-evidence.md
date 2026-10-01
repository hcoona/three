# Ruby Smoke Source Evidence

The V3 maintainer maintains this record for Ruby requirements/design reviewers
and later implementation/admission reviewers under [Issue #954][issue]. It owns
source findings and their limits, not runtime support, package ownership or
operation permission. Initial sources were inspected on 2026-09-30 after the
Ruby design Wave was accepted, without native or service execution. Later runtime
observations below identify their separate accepted operation and diagnostic
protocols; they do not convert source findings into universal platform guarantees.

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

## GitHub Download Source Recheck

**Current public-source findings:** on 2026-10-01, four bounded unauthenticated
HTTPS GETs [independently rechecked][source-recheck] the
[GitHub Ruby guide][github], package permissions
and pinned RubyGems 4.0.20 RemoteFetcher and Request sources. All returned HTTP
200 without redirect following or retries. The guides still document
conditional authenticated installation and package access; neither inspected
guide specifies the observed Azure origin/path/query, redirect count or direct
download status. This is a bounded absence finding about those sources, not a
promise that no other documentation exists. The separate [postcreation evidence carrier][postcreation-review]
addresses actual package facts; these source findings do not establish
package access or readiness.

**Immutable client-source findings:** pinned RemoteFetcher `fetch_http`, lines
212-238, follows 301/302/303/307 with a `depth > 10` guard and rejects missing
Location or an HTTPS downgrade. It reapplies configured headers at each hop;
pinned [Request][native-request] derives Basic authentication from the current
URI and has bad-response/connection-reset retry paths. These mechanisms do not
supply a one-hop, credential-free, no-retry contract. The rechecked RemoteFetcher
body matches the original retained source. Retained public-body SHA256 identities
bind this recheck:

| Source                     | SHA256                                                             |
| -------------------------- | ------------------------------------------------------------------ |
| GitHub Ruby guide          | `91df72a573e1674977d06b0ee8cdd320c6c23579abab5cb63566c6f18514fd34` |
| GitHub package permissions | `c60274f632dadf283e659ec1096a3217a9159c0ed7bb09115b1cfd63fc724761` |
| Pinned RemoteFetcher       | `e901acecd1bf018fcd469a6560a92d3e59c271073a4cc0cc1472b8058b188b5a` |
| Pinned Request             | `f1bc300d45bcd83692417f9ceb7604863ca6c311a7d8e1e3d657332ba96460af` |

Client support is neither GitHub server implementation evidence nor proof of
storage status, original byte equality, current grants or service immutability.

## GitHub Download Observation

**Runtime observation:** [bootstrap run 36924998743][bootstrap02], attempt 1,
used protected source `aa925d7b8cf0fa5ca64ec09ee3e309309b8834d5` and uploaded
`hcoona-release-smoke-ruby` version `0.1.0.pre.beta.20` with HTTP 200. Its first
visibility observation returned two index HTTP-200 responses followed by an exact
gem HTTP 302. The zero-redirect implementation classified this as unknown; the
Result and terminal outcome remain failed. No remote-consumer success or normal
completion follows from upload acceptance. Both GitHub bootstrap slots are spent.
The [independent terminal review][bootstrap02-review] retains that failure.

The original 302 HTML contained a short-lived signed storage capability. No
Location header was retained, so the HTML was not evidence of an observed header.
Historical originals remain private evidence; no body, encoded body, signed URL
or capability value is reproduced here. This exposed both an unclosed GitHub
read contract and a response-retention defect, independently classified as true
positives in the [screened independent disposition][redirect-triage].

**Separate runtime observation:** an independently reviewed, single authenticated
GET diagnostic at 2026-10-01T21:10:56.625605Z through
2026-10-01T21:10:56.873600Z returned HTTP 302 and observed Location origin
`https://rubygemsregistryv2prod.blob.core.windows.net`, with the exact
owner/package/version/UUID path and a query. The diagnostic retained only safe
status, booleans and cryptographic identities; no Location or body, and it
followed no redirect. Its source digest is
`sha256:2a2e729bb5adb52b27d131c355796faa15acd311f0f882070d216bdb6d6fc25d`.

**Design implication and limit:** a single exact-origin/path, credential-free
continuation can be designed within the existing 32-send ceiling, with safe
redirect evidence and fresh normal gates. This observation establishes neither
storage HTTP 200 nor whole-gem equality, effective Actions permission, permanent
host/path stability or service immutability. The [operation protocol][redirect]
owns the bounded route and protected implementation gate; source presence does
not accept a real download. Changed origin/path/status behavior stops for scoped
revalidation rather than broadening the allowlist.

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
and publisher checks, returning HTTP 201 (`:created`). The pinned credential
action [response schema][credential-response] reads `rubygems_api_key`, `name`,
`scopes`, optional `gem.name` and offset-aware `expires_at`. Pending publication
may omit the gem field; normal admission requires the selected gem binding.
These are source-derived response contracts, not a live exchange observation.
Documentation describes gem-scoped short-lived access.
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
[credential-response]: https://github.com/rubygems/configure-rubygems-credentials/blob/8674aee7f9f9055a8b771794d6f1e66b5fad36a9/src/oidc/responses.ts
[bootstrap02]: https://github.com/hcoona/three/actions/runs/36924998743
[bootstrap02-review]: https://github.com/hcoona/three/issues/954#issuecomment-5940605940
[redirect]: ../ruby-operation-protocol.md#github-gem-download-continuation
[redirect-triage]: https://github.com/hcoona/three/issues/954#issuecomment-5940719795
[postcreation-review]: https://github.com/hcoona/three/issues/954#issuecomment-5940722853
[native-request]: https://github.com/ruby/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/request.rb#L142
[source-recheck]: https://github.com/hcoona/three/issues/954#issuecomment-5940809720
