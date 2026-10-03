# Ruby Smoke Brief LLD

> **Implementation baseline:** This record describes the pre-contraction
> implementation. Its authority for existing code and retained evidence, and the
> requirements-first order for replacement work, are defined by the
> [requirements transition](./requirements.md#requirements-and-implementation-transition).
> It is not yet a design demonstrating conformity to the contraction requirements.

## Status and Ownership

This is the Ruby slice design authority under [Issue #954][issue]. The
[implementation status](../README.md#ruby-build-and-qualification) records
completed publication, campaign retirement and retained evidence limits. This
design is not package ownership, a configured publisher or publication evidence.
The V3 maintainer maintains this carrier for implementation and acceptance
reviewers. The [source record](./research/ruby-smoke-evidence.md) distinguishes
public findings from observations and remaining feasibility gates. The [requirements](./requirements.md#ruby-smoke-slice) and accepted
[Wave](../../../../../docs/delivery-wave.md) own scope and stage gates.
The owner delegated the complete journey; this document alone grants no effects.

## Smoke Product and Frozen Version

The selected new product is `hcoona-release-smoke-ruby` under
`src/public/lib/hcoona-release-smoke-ruby`. Its require path is
`hcoona_release_smoke_ruby`; `HcoonaReleaseSmokeRuby.project_id` returns the
package name. `HcoonaReleaseSmokeRuby::VERSION` equals installed gem metadata.
It has no runtime dependencies, executables, native extensions, signing keys or
production consumers. One Build Definition produces one unsigned pure-Ruby
`.gem` with platform `ruby`. Initial build and consumer acceptance uses the
repository-pinned Ruby on Ubuntu; no multi-platform matrix is implied.

The package-local NBGV lineage starts with `0.1.0-beta.{height}`, no inherited
root path filters and public-release recognition for protected `main`. Its
version-affecting closure includes the smoke, V3 Ruby mechanisms and descriptors,
NBGV configuration, native dependency/toolchain inputs and relevant workflow
control. The exact descriptor and filters must cover the implemented closure;
an unrelated root filter cannot silently determine release height.

The full-history Provider obtains raw NBGV `SemVer2` once at the exact target
using the repository-owned NBGV tool. A native Ruby helper validates that string
with `Gem::Version.correct?` and freezes `Gem::Version.new(raw).to_s`. For
example, raw `0.1.0-beta.7` becomes `0.1.0.pre.beta.7`; a non-public
`0.1.0-beta.7.gabc1234` retains the commit suffix in the normalized prerelease.
This is the explicit Ruby projection, not an NBGV-provided Ruby field.
Whitespace, empty versions, unsupported native syntax and `+` metadata fail;
there is no stripping, fallback field or run-derived suffix. Keep the raw value,
normalized value, projection identity and exact NBGV/RubyGems versions in the
Provider facts. Source history and target identity remain separately bound.

Materialize `lib/hcoona_release_smoke_ruby/version.rb` with that static constant
in the Provider's isolated evaluation tree and independently in Build staging.
The source gemspec reads this generated file; direct source packaging requires
the documented version-materialization step rather than a placeholder version.
The installed package contains only the static form and never invokes Git,
NBGV, .NET or a source-checkout helper. No general `nbgv-ruby` library or new
published versioning product is needed for this bounded integration.

## Provider and Build Boundary

A gemspec is executable Ruby, not a safe metadata document. Evaluate it only in
an unprivileged target-evaluation job with no publication capability, outside
the decision and publisher zones. Use `Gem::Specification` to obtain native
name/version/platform, files, dependencies and required-runtime facts. The
smoke admits a fixed pure-Ruby shape, explicit files and no custom extension or
target-selected build command. Unknown files, dependencies, dynamic inputs or
contradictory metadata fail admission rather than being guessed by a parser.

The normalized Model closes one Project Node, Release Unit and gem variant.
Its source closure includes the gemspec, library, README/license, generated-file
recipe, NBGV lineage and reviewed native tools. A manifest names generated inputs
without pretending they existed in the Git source tree. The Provider's staged
evaluation is technical discovery; Build consumes its admitted facts and never
re-evaluates NBGV. Publisher and Finalizer import strict data records, never the
gemspec or target library.

Build staging performs the following closed transformation:

1. Copy the exact admitted source closure into a new directory outside Git.
   Reject unsafe paths, symlinks, undeclared outputs and ambient source lookup.
2. Write the frozen static version and canonical
   `lib/hcoona_release_smoke_ruby/_workflow_delivery_provenance.json` witness.
   The witness binds target, Release Unit, canonical/raw/native version facts,
   Build Definition, catalog/control identity and purpose; it contains no run,
   Attempt, destination, channel, current time or temporary path.
3. Derive `SOURCE_DATE_EPOCH` from the immutable target commit's committer time,
   not author time, workflow time or an ambient override. Freeze it in the Build
   Request. Normalize ordinary staged file modes to `0644`, directories to
   `0755`, metadata/file ordering and locale/timezone inputs. Do not embed a
   destination-specific `allowed_push_host`; the approved destination is an
   authority binding outside the archive.
4. Execute the maintained RubyGems builder with strict specification validation
   and no signing, installed plugins, user gem configuration or undeclared
   dependency resolution. Preserve the builder's original output unchanged.
5. Inspect the original package with native RubyGems readers in an unprivileged
   inspection process and export only strict bounded facts. Validate expected
   metadata, complete file set, static installed version, witness and checksums.
   Reject duplicate/traversing members, unexpected platforms, runtime
   dependencies, extensions and extra outputs. Compute whole-file SHA-256 and
   size; transport naming does not change logical identity.

The initial design basis is Ruby 4.0.7, its bundled RubyGems 4.0.20, and NBGV
3.10.94 as inspected in the accepted repository/toolchain. Native manifests and
mise locks retain ownership; the implementation must bind actual Ruby,
RubyGems, compression/runtime and NBGV identities in its profile without adding
a second dependency lock. A toolchain change invalidates assumptions needing
revalidation, not the historical evidence.

## Reproducibility and Consumer Acceptance

Before this Build Definition supports recovery, accepted validation
must independently build the same complete frozen request in two different
clean roots, with separate writable homes/caches and changed wall-clock/file
creation times. Retain both original `.gem` files, SHA-256/size, native
toolchain identities, exact source/version/witness, target-derived epoch and
member-level diagnostics. Byte equality must cover the entire package, not only
Ruby source or normalized metadata. A changed-source control and changed-epoch
control must yield different original outputs. This is a required validation protocol, not a claim of observed equality.

Use a fresh isolated GEM_HOME/GEM_PATH and home outside the checkout to install
the exact local archive with native `gem install --local --no-document`, without
network dependencies, source-directory load paths or an installed smoke copy.
Verify the native installed specification, `VERSION`, `project_id`, installed
file location and exact witness. Installation must work with Git/NBGV/.NET
unavailable. Qualification separately owns content and consumer obligations;
CI and Release produce their own artifacts and Evidence.

A later destination audit first resolves the exact native coordinate from the
selected registry, downloads the remote original, compares it with the
qualified artifact, and repeats the clean install from that downloaded file.
Installing the pre-upload local original is not remote-consumer evidence.
Native version equivalence must not allow a differently spelled existing
coordinate or different bytes to bypass conflict detection.

## Destination and Authentication Boundaries

Each Attempt selects exactly one destination. GitHub Packages Buddy completion
precedes a fresh independently built and qualified RubyGems.org Official
Attempt. Sharing a product/version or equal bytes never promotes an earlier
artifact, Approval or Evidence. These proposed names are configuration targets,
not claims that the resources exist:

| Binding              | GitHub Packages Buddy                        | RubyGems.org Official                               |
| -------------------- | -------------------------------------------- | --------------------------------------------------- |
| Package              | `hcoona-release-smoke-ruby` in `hcoona`      | `hcoona-release-smoke-ruby`                         |
| Registry             | `https://rubygems.pkg.github.com/hcoona`     | `https://rubygems.org`                              |
| Normal workflow      | `workflow-delivery-v3-ruby-smoke.yml`        | Same stable entry                                   |
| Approval Environment | `workflow-delivery-v3-ruby-github-packages`  | `workflow-delivery-v3-ruby-rubygems`                |
| Publisher capability | Repository `GITHUB_TOKEN`, `packages: write` | Gem-bound short-lived trusted-publishing credential |

GitHub association uses `github_repo` metadata pointing to `hcoona/three`.
The effective package access and visibility are reviewed facts. Do not assume
anonymous installation, infer authorization from a public source repository or
use a package-admin token. Read-only native consumers may use explicitly
admitted package-read capability; only the publisher receives write capability.
Credentials must not appear in URLs, process arguments, retained logs or
artifacts, or be forwarded to an unapproved download/redirect origin.

RubyGems.org uses OIDC with an exact gem/repository/workflow/Environment tuple,
no static-token fallback, and no target-defined Ruby execution in the publisher.
The protected Environment gates the action-bearing job after the immutable
Approval Bundle and summary exist. The trusted job validates native current-run
approval, persists Authorization, and only then obtains the short-lived token.
An exact-satisfied Attempt schedules no approval or token-acquisition job.

First-project establishment is a distinct prerequisite at each destination.
GitHub package creation/linking and RubyGems.org pending-publisher conversion
need later bounded requests, exact disposable scope, independently qualified
original artifacts, current-run approval, terminal evidence and reviewed
postcreation ownership/configuration. A pending publisher does not prove name
reservation or ownership. Failure consumes its operation lifetime; source or
configuration facts may establish only the prerequisites they actually prove.
No existing Python publisher/account confirmation supplies RubyGems ownership.

## Publication and Recovery

The owner accepted `WD-RUBY-005` platform reliance. No duplicate/race suite or
native-generation record is required. The shared Plan, Snapshot, Bundle,
Approval, Authorization, mutation marker, Result and Outcome lifecycle stays
unchanged. Ruby adds a strict one-gem action/result profile; it does not copy
Python's two-file set or introduce a transaction abstraction.

Observation classifies the native coordinate as missing, exact, conflicting or
unknown. Native equivalent versions with different spelling/platform or any
unexpected candidate block; do not choose one candidate arbitrarily. Exact
requires downloaded whole bytes, metadata and witness matching this Attempt's
qualified artifact. Zero-action Finalization needs a fresh proof and schedules
no approval, token acquisition or mutation. Missing state materializes one
profile-bound action. Immediately before its marker, revalidate current
Governance, Approval/Authorization, profile, artifact and missing state. Any
change blocks before mutation, including a newly appeared exact package.

The trusted HTTP transport sends the original immutable bytes once to
`POST /api/v1/gems` at the selected registry base, with no retry, redirect,
MFA challenge handling, API-key scope upgrade or CLI skip mode. Native
`gem push` is not the publication transport. The profile freezes method, URL,
TLS/runtime/source identity, headers, response bound and accepted success
status (HTTP 200); other statuses or interrupted responses fail. Upload timeout
or lost response is possibly mutated. A durable marker without Result remains
unknown and possibly mutated. A 409 remains rejection; it is not success.

Only after definitive success may visibility observation wait: at most six
inventory/exact reads, at least ten seconds between pending missing responses,
and a 60-second admission window from upload-response completion, within
unchanged outer authority deadlines. Downloads and metadata reads have separate
finite request/byte limits in the protected profile. Conflicting state,
transport/parser/download failure or exhausted bounds is terminal. Preserve
all safe original responses, explicitly screened redirect/error evidence and
ordering. A fresh independently qualified Attempt
may later find exact state; the failed uploading Attempt remains failed.

## Native Registry and Credential Profiles

GitHub Packages uses its authenticated RubyGems protocol. Native
`specs.4.8.gz` and `prerelease_specs.4.8.gz` indexes are decoded using the pinned
`Gem::SafeMarshal` reader in an unprivileged Ruby process with no write
capability; serialized native index data never executes in the trusted
publisher. The exact `.gem` URL is constructed from the admitted registry base
and `gems/<native-filename>` path, not an arbitrary metadata URL. The reviewed
profile admits direct HTTP 200 or exactly one HTTP 302 from that constructed gem
URL to the exact credential-free HTTPS storage origin and coordinate path in the
[operation protocol](./ruby-operation-protocol.md#github-gem-download-continuation).
Both direct sends spend the same phase budget before I/O. Uploads and indexes
never follow redirects. A second or unsupported redirect remains unknown; no
npm/NuGet or GitHub Actions artifact-download allowlist is imported.

Signed Location and HTML remain in memory only. Before evidence retention,
screen every redirect, including rejected redirects, and capability-bearing
storage failures. Distinguish screened status/route-policy/digest evidence from
original response bytes; a suppressed body is not an empty original. Exactness
still requires the actual complete downloaded gem bytes and witness. Offline
replay verifies the screened continuation joins without reconstructing a signed
URL or claiming to recover its discarded signature. The [accepted normal Live evidence](./validation/ruby-normal-live-evidence.md)
records completion and durable stops for both destinations. All eleven
protected request slots are null and campaign-specific external callers
are retired from invocation use. Historical ready normal Governance, blocked bootstrap
configuration, original expiries and the existing successor ledger remain
preserved. Earlier failed, cancelled and incomplete outcomes remain as
recorded; later completion does not reuse their artifacts or Approval or
renew their authority.

RubyGems.org uses the documented version inventory and exact-version/platform
API, then an admitted HTTPS gem download origin. Check native name, version,
platform, yanked status, SHA and exact bytes. No additional platforms, alternate
spellings, yanked or unverified records are accepted. Registry metadata alone
cannot satisfy exactness. HTTPS verification stays enabled; credentials never
cross a redirect origin. RubyGems.org retains its separately admitted direct
download route and does not inherit the GitHub storage allowlist. A changed
RubyGems download contract requires its own observed supported route and explicit
finite credential-free admission. No
unbounded pagination or automatic retry is admitted.

For RubyGems.org, request one GitHub OIDC assertion with audience
`rubygems.org`, then exchange once at
`https://rubygems.org/api/v1/oidc/trusted_publisher/exchange_token` with JSON
`jwt`. Validate the short-lived response before one upload; retain only
sanitized timing/status and binding evidence, never assertion/token bodies.
The optional response `gem` may be absent for either lifecycle; any present value
must be an object naming the selected gem. Registration attestation and current
authority supply the binding, not an omitted response field. Retain the first
exchange's fixed credential-free receipt through the existing hosted audit on
success or failure, before upload authority/action. It records trusted start time,
bounded elapsed milliseconds or null, whether a complete validated response
returned, its integer HTTP status when available, and a fixed validation code.
No complete response does not establish no send or no remote key creation.
No bodies, body hashes, arbitrary headers/fields or exception text are retained.
Diagnostic timing does not alter token expiry validation or permit another exchange.
Expiry cannot renew Approval and a token failure cannot trigger static fallback.
GitHub Packages authentication uses the job's repository token and expected
actor with no token in command arguments or persisted native configuration.

## Governance and First-Project Protocol

Maintain separate strict protected Governance sources for GitHub Packages and
RubyGems.org, initially `blocked` with live disabled and request slots null.
Each binds the accepted Ruby platform basis, exact source/profile identities,
repository/main/workflow/Environment/package, ownership/access attestation,
required reviewer and branch rules, actual bypass Boolean, inspection/expiry
and protected-path freshness. No fake native-generation field is admitted.
Independent configuration review precedes ready admission. Expiry is finite and
never renewed implicitly by a read, retry or Wave change. Current-run approval
and same-revision control remain mandatory even where administrator bypass is
available. Non-action jobs have no OIDC or package-write permissions.

The bootstrap uses a distinct stable workflow
`workflow-delivery-v3-ruby-bootstrap.yml` and the selected destination's
Environment. RubyGems.org requires a distinct bootstrap publisher tuple and a
normal tuple for `workflow-delivery-v3-ruby-smoke.yml`. The owner reports both
pending tuples already configured under controlled account `Raffle2282`;
preserve them without recreation or repeated account confirmation. Pending
registration does not prove actual gem ownership or an existing-gem normal
publisher association. Observe those facts after bootstrap before
normal admission. GitHub's
bootstrap creates only the selected associated gem. Bootstrap never uses
normal ready Governance to pretend the resource already exists.

Before configuration, bind the exact gem/repository/workflow/Environment and
account facts in the Issue and independently review their consistency. Normal
and bootstrap requests use the two-layer binding below: protected-deliver each
operation envelope, then independently admit the exact execution binding after
its target commit exists and before dispatch. Bootstrap retains the distinct
first-project admission obligations described below. Each request allows one workflow run, rejects
`run_attempt != 1`, and consumes its slot before dispatch. Wave ceilings are
cumulative across requests. Requests cannot refill themselves; a failed operation
does not authorize a resend.
Native registry read budget is at most 32 requests per operation, including
preflight, before-action, post-success and final consumer reads; each response
is bounded by its reviewed profile. One OIDC assertion and one exchange are
allowed only for an action-bearing RubyGems.org operation. Admission/configuration
inspection has finite per-invocation resource bounds, without a cumulative
authorization ceiling for read-only checks. Stop on conflicting or
unknown state, failed approval, expiry, profile drift or ambiguous effect.

Persist sanitized original HTTP envelopes, immutable GitHub artifacts, native
inspection/consumer output and terminal DAG evidence. An independent audit
binds actual downloaded package bytes to the operation and installs them in a
fresh consumer. After bootstrap, independently inspect actual ownership,
association/access and publisher conversion before normal admission. A failed
bootstrap can establish only separately verified resource facts; it cannot
claim normal completion. Keep failed versions untouched. Stop each destination
campaign at first independently audited normal completion and retire temporary
operators without deleting the stable publisher/workflow binding.

### Exact Normal Request Binding

A Git commit cannot contain its own final SHA. Normal Release still requires
one exact protected-main commit T as both source and workflow/control revision;
selecting an older package target with newer normal tooling is not an escape.
The protected envelope and its concrete execution binding have distinct roles:

1. Protected-deliver the closed envelope schema and validator, exact repository,
   main ref, workflow, destination, Environment, package, actor and reviewer;
   source/profile constraints, original-gem obligations, finite lifetime and
   cumulative request/effect ceilings; and uniquely identified finite slots.
   Slots start disabled/null. Enabling a slot requires protected delivery within
   the remaining Wave ceiling. The envelope selects the actual protected
   workflow revision; it does not try to contain its own eventual commit SHA.
2. After protected T and its required checks exist, seal canonical request bytes
   outside that commit. Bind T as source and control, its source tree, raw/native
   NBGV coordinate, envelope/profile/source hashes, destination, slot/generation,
   obligations, actor/reviewer, absolute expiry and finite budgets. Independently
   review the exact bytes and caller, retain their hashes and review evidence,
   and admit only a request inside the accepted envelope. The Issue retains
   screened evidence; it supplies neither new authority nor a substitute for
   protected delivery. The accepted Wave and envelope remain the authority.

The concrete operator protocol must identify the reviewed caller and durable
ledger, the independent admission carrier and reviewer, exact budget allocation
across jobs/operator/consumer, and how their bindings reach runtime. These are
execution prerequisites, not details that an arbitrary workflow input may
supply after dispatch. The accepted sole writer/operator is trusted to enforce
this reviewed admission boundary; a supplied digest proves byte identity only,
not independent approval or prior slot reservation.

The admitted operator durably reserves the unique slot before its one dispatch,
retaining the request and review identity. Failure or an uncertain response
leaves the slot spent. Bind the one actual run when known; never reconstruct or
refill a missing ledger, rerun the workflow, replace the dispatch, extend expiry
or rebind the request to a later target. Before dispatch, check current main
still equals T. A race selecting a different actual run target fails before
registry effects and still spends the slot.

The workflow receives the exact canonical request identity and retains the
request as an immutable current-run artifact. Before registry access, validate
it against the protected envelope and admitted operator evidence, including
slot, destination, actor, main ref, attempt one and target equal to both
`GITHUB_SHA` and `GITHUB_WORKFLOW_SHA`. Admission review and the actual run must
be joined before publication authority is available. Compare the freshly
derived NBGV coordinate to the request. Build and Qualification produce new
current-run originals; the external request cannot nominate prior artifacts or
replace Approval. Action-bearing current-run Approval follows review of the
actual original gem, qualified DAG, binding and reserved slot.

After dispatch, retain pinned T and establish protected-main ancestry and
relevant protected-path freshness. Unrelated later main commits are permitted;
a relevant path touch, including touch/revert, invalidates admission. Do not
replace that rule with whole-main equality during approval or finalization.
Fresh requests may use only remaining slots after independent admission.
The one-upload, one-assertion/exchange, 32-registry-request ceiling, separate
finite GitHub/configuration budgets, original-byte audit, terminal failure and
first-success stop rules remain unchanged.

### Bootstrap Request Binding

Bootstrap uses the [same two-layer request binding](#exact-normal-request-binding):
protected-deliver its closed envelope, then independently admit canonical exact
execution bytes outside the one protected-main commit T selected as both package
source and workflow/control revision. The durable reservation, current-run
request artifact, actor/main/attempt-one checks, fresh NBGV comparison, finite
lifetime/budgets and independent admission apply unchanged. Compare main to T
immediately before dispatch; after dispatch require ancestry and relevant-path
freshness, allowing unrelated main advancement. No separate ancestor-source or
later-tooling selection is admitted for this bootstrap.

The bootstrap retains its distinct stable workflow, selected Environment and
first-project configuration source. GitHub Packages permits two distinct lifetime
slots, including the spent original; RubyGems.org permits one. The additional
GitHub slot is `github-packages-bootstrap02`. It requires the
[protocol's exact predecessor binding](./ruby-operation-protocol.md#single-github-bootstrap-successor);
it cannot replace, refund or re-enable `github-packages-bootstrap`. A successor
ledger retains cumulative spending across both ledgers and leaves the original
ledger immutable. All normal slots remain at most four per destination.
Protected v2 envelope/ledger implementation and independent admission must
precede enabling the new slot; a design amendment alone does not make it executable.
Prove whole-project absence through the supported destination-specific read
contract; a missing version or unavailable inventory is insufficient. Build and
Qualification produce fresh current-run original bytes before action-bearing
current-run Approval/Authorization. Bootstrap does not use normal ready
Governance to assert ownership of an absent package. Independently inspect
postcreation ownership, association/access and publisher conversion before
normal admission. Sharing request binding does not turn bootstrap into normal
completion or allow prior artifacts, Approval or a spent slot to be reused.
Its concrete protocol and implementation require independent review and
protected delivery before use; this binding alone is not an executable request.

## Validation and Delivery Gates

Contract/scenario tests cover native version projection and conflicts,
source/witness closure, unsafe gem metadata/paths, clean installation, fresh
exact/missing/conflicting/unknown observations, current-run approval ordering,
zero/one effect, rejected and ambiguous uploads, visibility exhaustion and
fresh recovery. Controlled HTTP substitutes verify application behavior only;
real publication/readback/consumer audits establish destination integration.
No exhaustive platform immutability test or arbitrary coverage quota is needed.

Local reproducibility validation permits one baseline pair and the two negative
controls per pinned request; changed implementation/input after a diagnosed
failure starts a separately recorded validation case, not unbounded retries of
an unchanged case. Keep isolated temporary roots and evidence outside source;
cleanup may remove only task-owned disposable state after evidence retention.
No registry effects belong to local validation. Actual acceptance requires
protected CI, independent technical/record/evidence review, reproducibility
and each destination's independently audited normal outcome. Source findings,
local observations and hosted evidence remain explicitly distinct.

[issue]: https://github.com/hcoona/three/issues/954
