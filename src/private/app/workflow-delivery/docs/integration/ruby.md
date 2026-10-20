# Workflow Delivery Ruby Integration Contract

This integration contract owns the Ruby native facts, CI group, Release caller
and destination conditions. It refines the [MLD](../middle-level-design.md),
whose [integration mapping](../middle-level-design.md#integration-mapping)
states the shared scope and authority of the integration contracts.

## Native Ruby Planning Facts

The Ruby reader consumes unprivileged RubyGems and Bundler answers for each
materialized endpoint. RubyGems owns evaluated source gem metadata; Bundler owns
Gemfile/lock equivalence, group/platform selection, source identities and complete
resolved dependency relations. Workflow does not parse these native declarations
or resolve package versions. A small Ruby adapter may serialize native public
answers to the managed process; it is not a second resolver or runtime service.

A source gem, a Bundler environment and an installed dependency have distinct
identities. Existing repository discovery identifies committed source gemspecs
and actual bundle consumers. Source gemspec evaluation can be required even when
its adjacent Gemfile does not load that gemspec. Each answer includes its original
source location and native package coordinate. A failed or nil gemspec evaluation
fails collection; it cannot silently remove a repository producer.

Each bundle answer includes its original Gemfile/lock, activated native version
and operation context, actual evaluated Gemfile inputs, selected direct and
transitive dependencies, and native source identities. Keep registry dependency
facts only as needed to recover repository-local relations; installed gems do not
become repository projects. Activate the locked Bundler version and verify it.
Planning reads frozen Bundler's locked resolution for the requested groups and
current platform, and evaluates local Path/Git gemspecs, without installing
registry dependencies. An incomplete locked resolution fails collection. Only a
selected check installs its frozen bundles. A native
fallback to another library cannot claim the requested lock context. The reader
must obtain complete native answers for both existing lock-selected versions
before their callers switch; this is finite supported integration, not a generic
library compatibility layer.

Join native local Path source identity and active local Git override identity to
repository producers by their original source paths. Installed cache locations
and names alone cannot establish these relations. Preserve native transitive
edges and cross-ecosystem bundle consumers. A bundle-only consumer does not gain
an invented gemspec or package identity. Missing required local owners, native
incomplete dependency answers, or an unsupported relevant source shape fail
collection instead of choosing full mode or maintaining a second input roster.

Input ownership covers committed project inputs, native evaluated Gemfiles and
locks, concrete project-owned gemspec helpers, and existing tool/NBGV/quality
inputs. Package file membership describes packaged content, not a test-input
inventory. Unknown dynamic Ruby IO or external input cannot be proven closed by
loaded-feature enumeration; such a required shape remains unsupported. Native
facts from both endpoints join the existing paired impact selection, including
removed local producers and their previous consumers, through the
[paired direct-selection reasons](../middle-level-design.md#paired-direct-selection-reasons).

## Native Ruby CI Group

Plan complete adopted Ruby checks from the paired facts and quality declarations.
Reuse the existing selected-work and original-result contracts. Preserve whole
project RSpec invocation and its native configuration and external tools; Workflow
does not choose examples. Request the supported native JSON formatter/output,
retaining original examples, failure/pending counts and outside-example errors.
Native command termination and complete result output remain separate facts.
Missing/truncated output, incomplete examples or a failing native command cannot
become a successful check. Apply project quality requirements to pending results;
a universal extra quality policy is not introduced here.

The registered `ruby/project-rspec-v1` check invokes that whole native RSpec
recipe and is required by the `ruby/rspec-v1` preset. Latexmath adopts this preset
in its source-local `workflow-delivery.quality.yml`, preserving its required suite
and external tools. Check registration, the adopted preset and actual selection
remain prerequisites for planning and caller qualification. Structural discovery does not supply a default
quality selection. The smoke gem retains its separate content/clean-require
preset; Hexo remains a bundle-only dependency consumer without an invented Ruby
package or check owner.

An explicitly quality-adopted bundle-only RSpec carrier can own a native suite
without a synthetic gemspec or publishable unit. The Ruby helper carrier consumes
its specs, native helpers, static wire descriptions and concrete gem fixtures.
Native Bundler locks own all test dependencies and both Bundler contexts. C# tests
its producer/reader/result side plus actual integration; pytest does not drive Ruby
helper behavior. Selected Latexmath checks alone require its external TeX tools.

An adopted gem distribution obligation does not require a Gemfile or spec
folder. Use native gem build, RubyGems metadata/content inspection and the declared
clean installed require consumer. Preserve original package associations and
native outputs for result collection. Source version projection remains with
NBGV and the project's native build integration. No custom complete-commit field,
provenance admission chain or Release state machine is added to CI.

The caller transfers exact endpoint/plan/control context and executes every
selected variant against the original plan without replanning. A selected RSpec
suite first installs each frozen bundle it governs. Failed preparation,
cancellation and absent required results retain failure. Explainable empty
selection performs no product preparation. Root CI owns RSpec and adopted gem
consumers as one complete Ruby group; their duplicate automated PR trigger is
retired. Affected caller changes require ordinary hosted transfer/execution/result
qualification. Manual Release and historical evidence consumers retain their
distinct owning contracts. This contract does not reopen any completed Ruby campaign or authorize
publication, dispatch or credential/configuration changes.

## Native Ruby CI Integration

Accept the [native Ruby planning facts](#native-ruby-planning-facts)
and [CI group contract](#native-ruby-ci-group) before
changing the reader and caller group. Use the private application's
native command/paired-selection/result interfaces. A small Ruby helper can
consume activated RubyGems/Bundler APIs through the already required Ruby runtime;
C# consumes structured answers and does not implement Gemfile/lock syntax,
version resolution or group/platform selection.

The current source gem consumers are `asciidoctor-latexmath` and the adopted
Ruby smoke gem; Hexo separately consumes a bundle environment without its own
gemspec. Latexmath's Gemfile does not load its source gemspec, whose project-owned
version projection invokes NBGV. Its gemspec file list omits tests. Preserve these
separate producer/check/input responsibilities; no generated package roster or
package-file-based RSpec selection is justified. Quality adoption fits the
existing YAML preset composition rather than a new policy lifecycle.

The required `ruby/project-rspec-v1` check and `ruby/rspec-v1` preset use
Latexmath's source-local quality selection. This explicitly adopts the existing whole-project RSpec
obligation; it does not strengthen its policy or infer quality from directory
structure. Retain the smoke gem's separate adopted distribution checks and Hexo's
bundle-only input responsibility. The native recipe and actual YAML selection
change with their corresponding reader, executor and caller.

Public Bundler source exposes native evaluated Gemfile inputs and dependency
closure, but the two committed locks select different versions and public method
shapes. Native Path source and active local Git override identity also differ.
Qualify the exact activated interfaces, frozen input invariance, nested Gemfiles,
direct/transitive local sources, removed/missing owners and existing cross-group
consumers when changing the dependent integration. Do not call private APIs, copy native
selection logic or replace a missing answer with installed-name/path inference.
Unbounded relevant dynamic Ruby inputs remain unsupported.

For RSpec, use supported native JSON formatter/output CLI options with the whole
original project/configuration. The formatter class itself is private; do not
construct or subclass it, select examples in Workflow, or add a report converter.
Preserve command termination, original examples, pending/failure and
outside-example results. Gem build/inspect/clean require obligations remain even
when the source has no Gemfile/spec directory.

The complete native Ruby group qualified combined planning, preparation,
execution and original result transfer in ordinary [PR #1123](https://github.com/hcoona/three/pull/1123) CI.
Root `ruby-group` now owns PR checks; the duplicate root job and shadow PR
workflow are retired with the fixture relocation. Node receives the original
paired Ruby facts from the same successful native group. Manual Release uses its
ordinary caller; historical readers use their pinned producers. Public source establishes interface candidates,
not current native qualification; source findings and bounded validation limits
must be accepted before observations. Further work requires the current Wave
and applicable concrete authorization; this contract does not reopen any stopped
Ruby campaign or supply publication, dispatch, authentication or configuration authority.

The native source basis is public and pinned: Bundler
[2.4.20 Definition](https://github.com/ruby/rubygems/blob/de20c7e7b7d7cc7a9ef088684a2c742d5a651ebf/bundler/lib/bundler/definition.rb)
and [2.7.2 Definition](https://github.com/ruby/rubygems/blob/b463ced1459e03bf871116fff3a09c5d287806c2/bundler/lib/bundler/definition.rb)
expose `gemfiles` and requested dependency/resolve answers; their respective
[2.4 SpecSet](https://github.com/ruby/rubygems/blob/de20c7e7b7d7cc7a9ef088684a2c742d5a651ebf/bundler/lib/bundler/spec_set.rb)
and [2.7 SpecSet](https://github.com/ruby/rubygems/blob/b463ced1459e03bf871116fff3a09c5d287806c2/bundler/lib/bundler/spec_set.rb)
implement different public `for` signatures.

Bundler 2.7's
[Path](https://github.com/ruby/rubygems/blob/b463ced1459e03bf871116fff3a09c5d287806c2/bundler/lib/bundler/source/path.rb)
and [Git](https://github.com/ruby/rubygems/blob/b463ced1459e03bf871116fff3a09c5d287806c2/bundler/lib/bundler/source/git.rb)
separate original Path locations from active local Git paths. RSpec 3.13.6's
[JSON formatter feature](https://github.com/rspec/rspec/blob/4d8e9c3699dbc821b4603c3c01b6e81c85e3784c/rspec-core/features/formatters/json_formatter.feature)
and [option parser](https://github.com/rspec/rspec/blob/4d8e9c3699dbc821b4603c3c01b6e81c85e3784c/rspec-core/lib/rspec/core/option_parser.rb)
document native `--format`/`--out` use; the
[formatter implementation](https://github.com/rspec/rspec/blob/4d8e9c3699dbc821b4603c3c01b6e81c85e3784c/rspec-core/lib/rspec/core/formatters/json_formatter.rb)
marks its class private. These source findings do not demonstrate
complete current-project graph identity, activated-version or execution behavior.

## Concrete Ruby Release Integration

This contract selects one of two statically registered units through the
`unit` input of `.github/workflows/workflow-delivery-v3-ruby-release.yml`: the
`hcoona-release-smoke-ruby` fixture under
`tests/private/app/workflow-delivery/fixtures/products` and the
`asciidoctor-latexmath` product under `src/public/lib/asciidoctor-latexmath`,
each with its declared single gem and adopted quality selection. GitHub Packages at
`https://rubygems.pkg.github.com/hcoona` is Buddy; `https://rubygems.org` is
Official. The [native assessment](../research/ruby-native-publisher.md) supplies
the source basis. This adds no product, destination, publication authority or
native experiment and reopens no completed campaign.

Read the exact committed declaration, native gemspec and quality selection
through existing repository readers. Support only each registered unit's
`ruby`-platform gem with no extensions: the dependency-free smoke gem and the
latexmath gem with its declared `asciidoctor` runtime dependency. Another
dependency/build/quality shape needs a concrete adapter, rather than a generic
resolver or task framework. Unit and ref eligibility follow the
[Official channel scope](common.md#official-channel-scope).
The Buddy Environment `workflow-delivery-v3-ruby-github-packages`
allows every branch (`**`) and keeps its required reviewer; the Official
Environment remains limited to `main`. Keep one faithful
source/control revision and selected native ref context; missing history or
unsupported native facts block the request. Use the existing NBGV `SemVer2` and
maintained RubyGems version projection, without an application suffix stripper,
publication-only version override or injected commit witness.

`Ecosystems/RubyReleaseUnits` statically registers the two units.
`Ecosystems/RubySmokePackageOperations` and
`Ecosystems/RubyLatexmathPackageOperations` own their concrete product recipes,
build, native archive inspection and original local consumption inside the
private application.
`CI/RubyPackageExecution` and `Release/RubyPreparation` consume those operations
while retaining distinct plan/check and request/qualification decisions. Release
does not consume `CiPlan` or `CheckSpec`
([implementation types](../middle-level-design.md#implementation-type-names)). The common mechanisms use the selected
Ruby/RubyGems/NBGV tools, closed environment and native builder. The request also
carries the caller's mise data directory and mise executable. The closed `PATH`
includes that executable's directory because the mise-installed Ruby's own
RubyGems plugin runs `mise reshim` after installs; redirected home and XDG state
keep that effect in scratch. The prepared Ruby runtime must report
the root [locked tool selection](common.md#locked-tool-selection) for `ruby`. Exclude inherited `SOURCE_DATE_EPOCH` for this
fixture and use RubyGems' fixed native default; do not introduce a timestamp or
archive engine. Native default timestamps do not promise byte identity across
different tools, compression, file modes or source inputs.

Qualification requires native name/version/platform, declared contents, the
declared runtime dependencies (none for smoke) and no extensions, GitHub
repository metadata, complete original size/digest, original local installation
and a clean installed API/origin check, plus any adopted required tests. The
latexmath consumer installs its runtime dependencies with the Bundler version
and frozen resolution of the committed product `Gemfile.lock`, without
development gems, then installs the original locally; RubyGems/Bundler own that
resolution. Its clean `require` uses RubyGems' native default path, so gems
bundled with the selected Ruby remain loadable. Latexmath's adopted
`ruby/rspec-v1` suite is its required `tests` check: the whole native RSpec
invocation runs from the exact checkout with the complete frozen locked bundle in
separate owned gem state, and the preparation job installs the suite's CI system
tools. No CI verdict substitutes for it. Retain actual reached command termination and
failed/missing/cancelled checks. Portable native identity and original bytes bind
the request; checkout paths and a version string alone do not. A fresh recovery
rebuilds and qualifies its own original at the same revision. A conflicting
same-version original remains a conflict.

Observation uses a small adapter over public RubyGems APIs. Configure only the
selected `Gem::Source` in memory through `SourceList`/`Gem.sources=`. Call native
`Source.load_specs(:released)` and `Source.load_specs(:prerelease)` separately
and require both to succeed; do not use convenience results that silently omit
failed prerelease reads. Match native name/requirement/platform, fetch the native
specification and download the original through that selected native Source.
Require consistent native identity, owned fresh cache/download paths and actual
original size/digest. Missing tuples after successful complete reads can be
absent; transport/authentication/index/spec failures or contradictory facts are
unknown. An exact original can be reused; different bytes forbid publication.

Each observation uses a fresh external process/home/spec cache, a 90-second
whole-process deadline, at most 64 selected tuples and a 256-KiB JSON answer.
These are application process/result limits; the selected original has no
application size limit and is hashed from the owned download file.
RubyGems owns full inventory transfers, decompression and native request retries.
Do not claim an underlying HTTP/inflated-index byte ceiling or replace native
fetching to simulate one. Observation performs no visibility polling. Retain
sanitized business observations, not a replayable transport trust ledger.

GitHub read credentials belong only to the read adapter's environment and
in-memory owner Source URI. Use no global authorization header, credentials in
arguments or persisted native source configuration. Native RubyGems owns its
finite HTTPS continuation: an ordinary signed storage Location without the
source URI's user information receives no original Basic credentials. Preserve
the assessment's stated redirect shape and limits; add no custom one-hop
downloader or arbitrary-origin guarantee. RubyGems.org observation is public.

Only an actual publisher receives the existing destination Environment and
write capability. Buddy uses repository `GITHUB_TOKEN` with `packages: write`,
passed to the native command as `GEM_HOST_API_KEY` with the documented Bearer
composition. Official uses the maintained pinned credential Action's
trusted-publisher path with `id-token: write`, explicit RubyGems.org server and
audience, no account credential, static key or role fallback. That Action owns
OIDC, native masking and token exchange, including its mode-0600 native
credential-file write. Give it fresh owned job home/state; exclude that state
from retained artifacts and clean only owned state. Do not claim automatic key
revocation or recreate platform authorization/MFA/scope management.

Refresh observation immediately before mutation. If state became exact, omit
the write; conflict or unknown state stops it. Invoke maintained `gem push` once
for the explicit qualified original and selected host, under a 180-second
whole-process deadline, closed stdin/non-TTY, native `--norc`, fresh working
home/cache and an allowlisted environment. RubyGems owns package interpretation,
upload, supported retry and native response handling. Do not patch private
sign-in, OTP or scope methods. Native generic auxiliary requests remain subject
to the documented selected token/platform profile; no account mutation is
authorized. No application resend, compensation or rerun recovery follows a
failure or interruption. Complete fresh observation may still satisfy the
request; preserve the actual command abnormality and unknown
effects without attributing unique insertion.

Suppress unsafe publisher/read diagnostics before persistence, including
credential-bearing URIs and partial failure/cancellation output. Preserve actual
exit, termination, duration and uncertainty. Native runner masking does not
sanitize result files. Read adapters serialize only portable sanitized native
answers; missing/invalid/oversized output stays unknown. Configure native
`ConfigFile(['--norc'])` when calling public Ruby APIs: it ignores user/system
configuration values while retaining native platform defaults, rather than
claiming those files are never read. Own `HOME`, gem/spec/cache/temp roots and
exclude inherited proxy, Gem/Bundler credentials, `RUBYOPT` and `RUBYLIB`.
Load only prepared maintained tools; product code is absent from the publisher.

A separate no-write job observes the selected source again on both initial
exact state and new publication. Native selected-source inventory/spec/download
must produce the exact name/version/platform and a fresh remote original whose
size and SHA-256 equal the qualified original. No remote install or require runs
after publication; local install/require before publication carries
qualification. This observation does not claim client source routing or
dependency resolution. Missing or unknown observation blocks completion.

The ordinary Ruby entry and actual caller compose qualified components.
They use prepare, observe, gated publish and separate verify/result
jobs on the supported Linux runner. Transfer complete same-revision control
before product execution. Transfer preparation and originals by same-run
immutable artifact ID with the native download digest check;
`job-inputs ruby receive <phase>` binds the received preparation's
source/ref, unit/channel/destination/mode subject to the request. Use the
existing 45-day retention and physical registry/package native concurrency
through verification. Dry run performs qualification/read/hypothetical work
with no publication credential acquisition or `gem push`. Exact/dry-run paths
enter no write gate. The common result separates attempted effects, actual
command outcomes, observed state and required business satisfaction.

Changes follow the [shared review core](../middle-level-design.md#release-integration-review). Controlled
credential-free component tests and ordinary Linux CI must qualify native
command/configuration/output boundaries, original build/inspection/consumption,
failure-after-effect, cancellation, missing transfers and complete results on
the final subject. Run complete affected suites and repository HK/hooks/record
checks. These checks do not establish current token/publisher configuration or
a new live feed/publication. Historical readers and spent campaigns remain
intact; no runtime schema compatibility alias is added.

### Ruby Destination Completion

The MLD's [post-publication completion](../middle-level-design.md#post-publication-completion)
contract applies to these destinations; this table owns their concrete
conditions and observation.

| Existing destination/use   | Native completion conditions to preserve                                                                                        | Post-publication observation                                                                                            |
| -------------------------- | ------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| GitHub Packages Ruby Buddy | Owner feed, native gem name/version/platform, index visibility and exact original `.gem`; safe signed storage redirect behavior | Native selected-source specification for name/version/platform and downloaded `.gem` size/SHA-256 equal to the original |
| RubyGems.org Official      | Native gem name/version/platform, native index/API metadata, visibility and exact original `.gem`                               | Native selected-source specification for name/version/platform and downloaded `.gem` size/SHA-256 equal to the original |
