# Ruby Native Release Integration

## Question and Evidence Level

Use maintained RubyGems interfaces for the existing GitHub Packages Buddy and
RubyGems.org Official destinations. This is pinned public-source research and
bounded integration inference. It does not qualify current feed configuration,
deployed download routes, authentication or a newly executed Release request.
The completed Ruby campaigns retain their actual original evidence and spent
operation boundaries.

The [replacement MLD](../middle-level-design.md#destination-interface) separates
native publication, fresh complete observation and required consumption. A
native command result alone does not establish the business result, and the
application does not resend an ambiguous publication.

## Native Publication and Credential Profiles

RubyGems 4.0.20 is pinned to
`7f2502dc32b4e10a099c398ad93d2d2534873205`. Its [PushCommand][push] uses
native package metadata and the explicit host to send the original gem. Native
[GemcutterUtilities][gemcutter] prioritizes `GEM_HOST_API_KEY` over stored keys
and skips sign-in when a key is already available. Those public interfaces can
carry the supported platform-issued credential without an application upload
client or private-method patch.

[GitHub's Ruby registry documentation][github-ruby], pinned at
`be38ec5d78e24172587e61b3c6ff40ace1865c71`, describes native `gem push` to the
owner host using `Bearer TOKEN`. The environment-key interface provides a
candidate credential composition for the same native command. Documentation
does not prove this repository's effective permission or current package access.

RubyGems.org server source at
`58d5e43b55906ed8503a670d465d1bba22d0db98` provides the selected trusted-token
profile. [Token exchange][exchange] creates a trusted-publisher-owned key with
push scope and a fifteen-minute expiry. [ApiKey][api-key] distinguishes that
key from a User-owned key; its MFA predicate is false for the trusted-publisher
key. The [controller authorization][base] therefore does not require account
MFA for that profile. Account-key creation or scope updates separately require
account authentication. These findings support using the native publisher with
the exact platform-issued profile; they do not promise that every generic
RubyGems auxiliary request is impossible. Native failures remain failures.

The maintained [credential Action][action] is pinned to
`8674aee7f9f9055a8b771794d6f1e66b5fad36a9`. Its trusted-publisher path owns
GitHub OIDC acquisition and token exchange. Its [configuration][configuration]
exports the token, registers native masking and writes a mode-0600 native
credential file under the user's gem directory. There is no source basis for
claiming environment-only credentials or automatic revocation/credential-file
cleanup from that Action. A concrete integration must explicitly account for
that temporary native file in owned, unretained job state. Do not implement
another OIDC exchange or invent a token-revocation certificate.

## Native Read and Consumer Interfaces

Public [SourceList][sources] and `Gem.sources=` can configure the selected source
in memory without writing gemrc. [Source][source] owns native index decoding,
specification fetch and original gem download. [DependencyInstaller][installer]
accepts explicit installation/cache directories, remote selection and an exact
native requirement. A small Ruby adapter can configure those public interfaces
and serialize portable results; it does not need a second resolver, index parser
or downloader.

[SpecFetcher][spec-fetcher] has an important error boundary:
`available_specs(:complete)` may replace a failed prerelease inventory with an
empty list, and `detect` discards returned errors. Those convenience results
cannot independently prove prerelease absence. Separate successful public
`Source.load_specs(:released)` and `Source.load_specs(:prerelease)` calls provide
a candidate strict inventory basis. Match native name, requirement and platform
only after both succeed; unavailable or contradictory facts remain unknown.

These APIs load complete source inventories. This is native protocol delegation,
not a per-package HTTP or inflated-index byte-limit guarantee. The concrete
contract must distinguish whole-process deadlines, bounded serialized answers
and original-package limits from native internal transfers. Public source alone
does not establish that either deployed destination exposes every required
inventory route.

Native source/download methods can reuse cached bytes or choose a cache fallback.
Use fresh owned state, require the actual native original path to remain within
that state and compare its complete size/digest. Installed version equality is
insufficient. Required remote consumption must bind the selected source, native
installed name/version/platform and cached original gem before a separate
credential-free child imports product code. The existing dependency-free
`ruby`-platform fixture under `tests/private/app/workflow-delivery/fixtures/products`
is the concrete extraction basis, rather than a new public library project.

## Redirect and Output Limits

[Request][request] derives Basic authorization from the current request URI.
[RemoteFetcher][fetcher] follows a finite HTTPS redirect sequence, rejects
HTTPS-to-non-HTTPS continuation and reapplies global additional headers. With
source credentials only in the in-memory owner URI and no global authorization
header, an ordinary absolute signed Location without user information receives
no original source Basic credentials. This is a source-bound inference for that
redirect shape, not an arbitrary-origin guarantee or deployed-feed observation.
Do not carry forward a custom one-hop redirect protocol as universal policy.

Source cache paths omit user information, but that does not prove all native
diagnostics or files are credential-free. Keep credential-bearing URIs out of
arguments, ordinary request records and retained output. Sanitize native results
before persistence; omit unsafe diagnostics while retaining actual termination,
exit code, duration and effect uncertainty. Runner masking alone does not
sanitize result artifacts. Close configuration, home/cache ownership, stdin,
process deadlines and output bounds in the concrete contract before cutover.

## Native Timestamp and Installation Order

The same pinned [RubyGems source][rubygems], line 150, defines
`DEFAULT_SOURCE_DATE_EPOCH` as the fixed value `315619200`.
`source_date_epoch_string` uses the explicit environment value or that constant.
The [Package][package] builder selects this native epoch and uses it for gzip;
[TarWriter][tar-writer] uses it for default entry timestamps. Native
[Specification][specification] derives its default date from the same epoch.
The current fixture sets no explicit date. A nearby source comment about a
current-run value does not turn this revision's fixed default into the wall clock.
Exclude inherited overrides and use the native behavior; do not add an
application timestamp engine. These are source findings, not byte-identity
proof across runtimes, compression, file modes or arbitrary gemspec inputs.

Public [Installer][installer-file] `at(path, options)` builds the maintained
package/installer and performs native checks, extraction, extension/hook
handling and installed cache/spec registration. For the selected dependency-free
fixture, native Source inventory/spec/download can first establish remote
routing. Verify the actual downloaded original before a credential-free child
installs that fetched gem with native validation intact. This is distinct from
substituting the prebuilt expected original. Post-install checks cannot establish
that unqualified remote code was safe to install with a read token.

Public [ConfigFile][config-file] `initialize` reads configuration files before
its `--norc` merge check. That option ignores their configured values and retains
native platform defaults; it is not a guarantee those files are never read.
The [concrete contract](../integration/ruby.md#concrete-ruby-release-integration)
selects owned state and native API composition within those limits.

## Pin Guard and Remaining Qualification

The original source responses, retrieval requests, hashes and independent source
assessments remain in the delivery work carrier. They establish the stated
source behavior only. Concrete integration acceptance and credential-free local
tests/ordinary CI must precede implementation/caller delivery claims. New live
feed, authentication, publisher configuration or publication work requires its
own bounded authorization; this research reopens no completed campaign.

The locked Ruby and its bundled RubyGems are guarded by contract tests instead of a
recheck. `RubyNativePublisherContractTests` runs the production
`RubyNativePublisher` with the locked `gem` against a loopback HTTPS gem host. The
host uses fake keys and a disposable CA whose leaf names `127.0.0.1` and
`localhost`, and every proxy points at a closed port, so no request leaves the
host. It asserts the following:

- Buddy pushes exactly one POST of the original bytes to `/hcoona/api/v1/gems` with
  `Bearer <key>`, and Official to `/api/v1/gems` with the raw key; both exit 0.
- A 503, the registry's existing-version 409, a dropped connection, a same-realm
  redirect and an other-realm redirect each end the command as a failure after
  exactly one POST, and no request reaches another realm.

`RubySourceReaderContractTests` runs the production `RubySourceReader` and the
real `source.rb` with the locked RubyGems against a loopback HTTPS gem source.
The source serves gems built by the locked `gem` and indexes written with
RubyGems' own Marshal format. The only seam routes the selected host to loopback
and keeps its path and user information; every `Gem::Source` method runs
unchanged. It asserts:

- the exact ordered requests: `specs.4.8.gz`, `prerelease_specs.4.8.gz`,
  `quick/Marshal.4.8/<full>.gemspec.rz`, then `gems/<full>.gem`, under the
  `/hcoona` owner path for Buddy (the download is requested from the host root);
- Basic authentication with the encoded read user and token on every Buddy
  request and none for Official;
- a present answer, released or prerelease, with the original's size, SHA-256
  and the `<download>/cache/<file>.gem` path;
- absent after only the two inventory reads when neither lists the version;
- unknown, stopping at the failed request, when either inventory answers 404 or
  500, the specification 500 or the original 404.

RubyGems reuses an original already in its download cache without a request, so
the reader's fresh-scratch requirement is load-bearing; the cache row asserts both
the native reuse and the reader's refusal of used scratch. This contract found an
application defect: `source.rb` never loaded `rubygems/remote_fetcher`, which
`Gem::Source` calls but does not autoload, so every real read answered unknown.
The helper now requires it. The hosted Ruby smoke runs recorded in
[normal-live evidence](../validation/ruby-normal-live-evidence.md) (2026-10-02)
predate this helper (added 2026-10-12); no hosted Ruby smoke run has used it.

The native helper's RSpec carrier asserts the exact specification and archive
field shape, the locked Ruby identity, a remote `file://` Git source answered
from the lock as its URI and revision, and that every committed `Gemfile.lock`
names a Bundler the helper supports and the carrier tests. The Linux
native-preparation job runs the publisher contracts whenever `mise.toml`,
`mise.lock` or the managed sources change; the RSpec carrier runs when its
helpers, schemas or any committed `Gemfile.lock` change.

The credential Action, OIDC token exchange, GitHub Packages and RubyGems.org
acceptance and index format, token scope and registry routing (including the
GitHub Packages storage redirect) remain non-claims, bound to the recorded runs.
The carrier's `gem install bundler --version` preparation and the RSpec JSON
report shape are exercised only by the hosted Ruby group job and are not pin
contracts; the Action is a dispatch-only workflow pin and gets static
validation only. Windows remains the owner's assumption. A missing native
capability blocks the affected path rather than expanding the application into a
generic credential, resolver or trust service.

[push]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/commands/push_command.rb
[gemcutter]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/gemcutter_utilities.rb
[github-ruby]: https://github.com/github/docs/blob/be38ec5d78e24172587e61b3c6ff40ace1865c71/content/packages/working-with-a-github-packages-registry/working-with-the-rubygems-registry.md
[exchange]: https://github.com/rubygems/rubygems.org/blob/58d5e43b55906ed8503a670d465d1bba22d0db98/app/controllers/api/v1/oidc/trusted_publisher_controller.rb
[api-key]: https://github.com/rubygems/rubygems.org/blob/58d5e43b55906ed8503a670d465d1bba22d0db98/app/models/api_key.rb
[base]: https://github.com/rubygems/rubygems.org/blob/58d5e43b55906ed8503a670d465d1bba22d0db98/app/controllers/api/base_controller.rb
[action]: https://github.com/rubygems/configure-rubygems-credentials/blob/8674aee7f9f9055a8b771794d6f1e66b5fad36a9/src/main.ts
[configuration]: https://github.com/rubygems/configure-rubygems-credentials/blob/8674aee7f9f9055a8b771794d6f1e66b5fad36a9/src/configure-api-token.ts
[sources]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/source_list.rb
[source]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/source.rb
[installer]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/dependency_installer.rb
[spec-fetcher]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/spec_fetcher.rb
[request]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/request.rb
[fetcher]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/remote_fetcher.rb
[rubygems]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems.rb
[package]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/package.rb
[tar-writer]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/package/tar_writer.rb
[specification]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/specification.rb
[installer-file]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/installer.rb
[config-file]: https://github.com/rubygems/rubygems/blob/7f2502dc32b4e10a099c398ad93d2d2534873205/lib/rubygems/config_file.rb
