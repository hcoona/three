# Python Smoke Brief LLD

## Status and Ownership

This design realizes confirmed [`WD-PY-*`](./requirements.md#python-smoke-slice),
the [HLD](./high-level-design.md#python-smoke-extension) and its five MLDs.
The V3 maintainer authors and maintains this carrier; implementers and reviewers
use it to close the Python slice's concrete contracts without turning the MLDs
into command inventories. The revised contract trusts platform non-replacement
and identical replay under `WD-PY-006`. Runtime and both protected files
implement strict Python Governance v2. TestPyPI has reviewed ready admission;
PyPI remains blocked. Admission evidence, completed TestPyPI audit and the separate PyPI gate
are routed by the [handoff](./agent-handoff.md#starting-a-new-session). The
[retired native interface](./validation/python-native-readiness.md) preserves
failed-evidence consumers only; no duplicate/race suite is a prerequisite.
Implementation, configuration/admission and publication require their separate
[Wave](../../../../../docs/delivery-wave.md) authorization and domain gates.

The [source record](./research/python-smoke-evidence.md) owns service findings
and limits. The [migration policy](./migration-strategy.md#python-smoke-delivery)
owns staged delivery. No existing npm/NuGet permission is reused.

## Package and Build Contract

The package root is `src/public/lib/hcoona-release-smoke-python`, with
`src/hcoona_release_smoke_python/__init__.py` exporting `project_id()` and no
runtime dependencies. One Release Unit uses an Ubuntu/CPython 3.14 Build
Definition with wheel and sdist outputs. `Requires-Python` is `>=3.14`; the
universal `py3-none-any` wheel expresses absence of native ABI/platform payload,
not evidence of support on every Python implementation or operating system.

A project-local NBGV `version.json` owns the smoke lineage and public-main
projection. The Provider reads complete history/tags at the exact target,
selects `SemVer2`, calls the existing `nbgv-python` normalization and freezes
the canonical facts, field and exact PEP 440 value. Normalized parseable local
versions are valid for non-publishing CI/qualification. Release Live admission
requires an already normalized public version without local metadata and
rejects an ineligible projection without stripping or replacing it. The initial `0.1.0-beta.{height}` lineage must yield a supported prerelease projection on protected main; its concrete
version baseline is closed with the implementation descriptor, not derived
from a run number or package-index availability. Colliding coordinates fail.

Use the existing Hatch NBGV dynamic-version configuration in the source
manifest and a bounded staging transformation in the V3 Build Adapter:

1. Copy the admitted source closure into an isolated directory, without `.git`,
   workspace links or unrelated project configuration.
2. Replace the dynamic `version` entry with static `project.version` equal to
   the frozen projection. Remove only the NBGV version-source table and its
   build-system dependency; reject any other undeclared dynamic metadata or
   custom build hook. Keep `hatchling.build` and its declared build prerequisites.
3. Write canonical `_workflow_delivery_provenance.json` into the import package.
   Both wheel and sdist must contain it, and ordinary sdist rebuilding must
   preserve it in the installed package. The witness binds target, Release Unit,
   canonical/native version facts, Build Definition, catalog/control digests
   and purpose; no run, Attempt, channel or destination value is embedded.
4. Build both formats using UV with workspace sources disabled and the pinned
   Hatchling backend. Record exact resolved producer toolchain/build inputs.
   Release determinism excludes wall-clock/run/environment version data and
   binds archive ordering/timestamps through the Build Definition. Neither
   Build nor consumer invokes NBGV or the source dynamic-version hook.
5. Inspect the original wheel `METADATA`/`WHEEL`/`RECORD`, sdist `PKG-INFO` and
   staged `pyproject.toml` using native formats. Require matching name/version,
   no runtime dependencies, expected pure-Python payload, exact witness and the
   closed two-file output set. The sdist's static manifest and declared backend
   requirements must build outside Git with normal PEP 517 isolation.

The staging transformation is reviewed product-build behavior, not a general
manifest rewriting service. A missing or unsupported shape fails. `nbgv-python`
retains its existing public API and requirements. The producer lock/pinned
backend must bind its full resolved build closure; the sdist declares portable
build prerequisites, with the initial consumer's actual resolved versions
retained as evidence. The smoke package's `pyproject.toml` owns the exact
Hatchling build requirement. `mise run update-uv-lock` resolves that pin with UV
and generates both the hash-pinned
`eng/workflow-delivery/v3/python-build-constraints.txt` and the V3 package's
`_python_build_backend.py` from the manifest and `uv.lock`. Provider identity,
source admission, staging and sdist inspection consume that package projection;
they do not read ambient repository configuration at runtime. Root HK rejects
stale projections. Renovate includes both outputs with Python dependency updates.
The shared native-lock reader requires an unambiguous public-PyPI dependency
closure and exact wheel hashes. This keeps native manifests and locks as the
version authorities without a second dependency inventory.

CPython and UV versions are owned by `mise.toml` and `mise.lock`. The existing
`sync:python-version` task projects them into the installed V3 runtime and
projects UV into `.config/uv/uv.toml`, consumed by setup-uv. Provider and build
checks consume those package constants, preserving exact toolchain binding
without ambient configuration reads. The remaining producer baseline uses
NBGV 3.10.94, `nbgv-python` 2.1.0.dev1 and packaging 26.3. Updating a producer dependency changes
the frozen toolchain facts and source inputs; it does not grant publication or
reuse qualification from another revision.

## Qualification and Artifact Admission

One Build result contains two tagged Artifact References, `wheel` and `sdist`.
Each binds logical SHA-256, filename, target witness, producer and immutable
transport ID/digest. The pair's order is fixed and duplicate/missing variants
fail closed. Archive readers reject unsafe paths, duplicate members and
ambiguous metadata without extracting executable code in the decision zone.

Separate fresh credential-free environments outside the repository install the
exact wheel and rebuild/install the exact sdist. Disable package/workspace
substitution and caches; no `PYTHONPATH` to the checkout, installed smoke copy,
Git directory, .NET/NBGV command or ambient version variable can satisfy the
consumer. Build prerequisites use the explicit public PyPI index independently
of the smoke's destination. Use downloaded exact smoke files, never an
`--extra-index-url` fallback. Validate `importlib.metadata` version, the installed
witness and `project_id()`. The sdist consumer's wheel is retained only as
consumer evidence. CI and Release each bind their own Build/Evidence instances.

The later destination audit independently resolves each format from the chosen
registry, downloads it, verifies its digest/witness against the approved set,
and repeats these clean consumers. It must not install a local original and
call that destination consumption.

## Registry and Credential Profiles

| Binding              | TestPyPI Buddy                         | PyPI Official                      |
| -------------------- | -------------------------------------- | ---------------------------------- |
| Project              | `hcoona-release-smoke-python`          | `hcoona-release-smoke-python`      |
| Upload               | `https://test.pypi.org/legacy/`        | `https://upload.pypi.org/legacy/`  |
| Simple Index         | `https://test.pypi.org/simple/`        | `https://pypi.org/simple/`         |
| OIDC audience        | `testpypi`                             | `pypi`                             |
| Proposed Environment | `workflow-delivery-v3-python-testpypi` | `workflow-delivery-v3-python-pypi` |

These are the required normal-publication tuples, not evidence that their
registrations exist. The owner has confirmed TestPyPI Sole Owner status and
bootstrap/native registrations for the same Environment; those workflow tuples
do not grant the normal workflow. Carry them forward without routine
reconfirmation unless changed or contradicted. Each protected
admission file under `.github/workflow-delivery/` is destination-specific,
initially blocked with `live_enabled: false`. The workflow is `.github/workflows/workflow-delivery-v3-python-smoke.yml`.
The protected files are
`.github/workflow-delivery/governance/hcoona-release-smoke-python-testpypi.json`
and `hcoona-release-smoke-python-pypi.json` in the same directory. Future OIDC
project registrations must bind the exact workflow filename and Environment. Do not register a wildcard,
use a pending publisher as evidence of admitted project ownership, or silently
change a registration to get a token.

Use the documented PyPI OIDC exchange and upload APIs with a pinned HTTP client
profile. It closes runtime/client/TLS versions, trusted HTTPS origins, timeouts,
finite read budgets, multipart metadata mapping and exactly one POST per
file. Upload uses the original qualified bytes; no rebuild, signing, metadata
rewrite, skip-existing option or automatic resend is allowed. No mutating
redirect fallback is admitted. Timeout after send is ambiguous. HTTP 200 acknowledges success even for an
identical existing file; it does not identify a new insertion. A duplicate
rejection, other non-success or ambiguous response remains failed regardless
of later exact readback. Retry transport only for immutable record
persistence, not token-accompanied upload. Token acquisition fails closed; no
credential appears in persistent logs or artifacts.

Read the supported JSON Simple Index and use `packaging` to identify all files
for the native-equivalent version. Retain sanitized response hashes and actual
file bytes. Index file URLs must satisfy the admitted HTTPS file-host policy;
no credentials accompany public downloads. Missing/inconsistent listing,
unknown filename/version, extra or yanked file, or failed byte/witness readback
blocks exactness. Whole-set absence is a creation candidate, not proof that
PyPI has never used the filenames. Deleted filename rejection remains failure.
V3 tests must exercise its actual request mapping, response classification and
one-shot bounds. The actual publication journey tests destination integration;
no SDK/CLI name or mocked response proves platform behavior.

## Bounded Post-Upload Observation

The disabled implementation realizes the owner-confirmed `WD-PY-005` reliability choice,
not a service visibility guarantee. It applies only after a definitive HTTP-200
upload, before its readback terminates. Bootstrap P2/P3 and normal publication's
up to two post-upload readbacks share the mechanism. Initial checks, zero-action
proofs and independent final audits remain single reads. The retired native
reader retains its historical policy only for the original failed evidence. HTTP transport retries, cache-bypass headers, token
refresh and file-download retries remain unavailable.

Each phase closes the successful upload response, comparison scope, previously
verified inventory and exactly one expected addition before reading. Bootstrap
compares complete project file inventories; normal publication keeps
its target-version scope. Compare complete normalized file entries, including
URL, declared hashes, Python constraints and yanked state; ignore only index-level
serial/timestamp metadata. An expected addition must have the approved filename
and SHA-256, accepted URL and metadata. Raw index bytes are always retained
unchanged. An acknowledged identical replay uses the same expected original
bytes; it does not relax previous-entry preservation or final set exactness.

| Index observation                                                                                                                                                 | Phase transition                                                                          |
| ----------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| Exact expected inventory with unchanged previous entries                                                                                                          | Download and verify expected original bytes and witnesses once; success only if all pass. |
| Existing-project HTTP 200 exactly equal to its previously verified HTTP-200 inventory, missing only the expected addition (never initial bootstrap P2)            | Pending; no downloads.                                                                    |
| HTTP 404 at first-project bootstrap P2 only                                                                                                                       | Pending; no ownership or successful-publication claim.                                    |
| Existing-project 404, other status, malformed/oversized index, changed/missing prior entries, extra/foreign/conflicting/yanked files, transport or download error | Terminal failure; no later upload.                                                        |

The first read has no deliberate delay. At most six index requests may start.
Let `u` be the successful upload response's completion instant on the trusted
process monotonic clock. A request starts only at `t` with `u <= t < u + 60`
seconds and before every existing outer deadline. After a pending response
finishes at `f`, another request requires `t >= f + 10`. Read the clock again
after waiting; scheduler delay cannot extend a window. Exhaustion terminates
without another request. An admitted in-flight call retains the 30-second
socket timeout; the 60-second admission window is not a completion guarantee.
File requests retain their own existing outer authority and socket bounds.
Non-finite or reversed timing fails closed. Waiting never extends bootstrap's
UTC authority or normal publication's current authority.
Monotonic instants are compared only within one process; runner UTC retains
its existing bootstrap authority role, with both guards enforced there.

Retain a strict ordered phase trace binding its policy/profile, operation or
capture ordinal, successful upload response, previous/expected inventory,
clock domain, upload completion and applicable deadline. Each reached request
retains admission/completion instants, URL, original sanitized status/content
type/body and digest, or a bounded transport error, plus its classification.
Retain the exact final downloads and their bindings; a pending response triggers
none. Record exhaustion/cancellation/validation failure as a terminal cause.
Persist reached evidence before admitting a later request or upload. Interrupted
or unsafe response retention cannot yield success; preserve surviving evidence
under the existing missing-Result/possibly-mutated rules. Never retain tokens
or authorization headers. Clock values are trusted runner observations, not
external time attestations.

Bootstrap raw journals and immutable bundles retain the full trace;
normal Publication Result carries or explicitly references its immutable trace
through the existing artifact transport. Strict parsers and deterministic
registry-offline audit reconstruct classifications from raw responses, verify
all timing/count/deadline and previous-state bindings, enforce upload/download
ordering and reject missing, reordered, surplus or post-terminal requests.
They must not trust a serialized `pending`, `exact` or success flag. Replay
performs no sleep, destination read or token request. Finalizer admits the bound
replayed evidence without making network observations.

The destination operation profile binds this policy and its finite parameters;
bootstrap protocol identities bind their budgets. Old native/bootstrap
evidence remains evidence of its original revision and is not coerced into the
new contract. No failed run supplies normal publication evidence. Strict
transport/record changes and their consumers must ship together in the disabled
implementation. The existing build, Model and CI qualification contracts need
no change.

## Hosted Integration

`python -m three_workflow_delivery_v3.python_cli` exposes the bounded stages.
The workflow separates request, Provider, compiler, CI/Release Plan, Build,
Qualification, publication preparation, publisher and Finalizer jobs. Python CI
conservatively selects the entire slice at the trusted tested-merge SHA; it
makes no affected-path optimization claim. CI and Release retain separate
Plan/Snapshot, Evidence and Decision records. Only mechanical artifact and
consumer inspection are shared.

The exact-target Model schema is `workflow-delivery/v3/python-repository-model-snapshot`.
The successor Python Governance uses `workflow-delivery/v3/python-governance-v2`
as defined below; the other
Python records use their explicit `python-*` discriminators. The shared
Publication Result uses `variant: python-distribution-set`, and the shared
Finalizer returns the existing AttemptOutcome. Parsers reject foreign variants
and retain all current-run transitive references.

Each payload uses an immutable `archive: false` artifact. The persistence
composite downloads the returned artifact ID, requires digest validation and
binds readback bytes before exporting the next explicit DAG edge. No artifact
listing or name-based discovery reconstructs authority. Wheel and sdist remain
separate original transports. The publisher exports one scalar terminal
reference; later artifact presence cannot substitute for that output.

Protected configuration attestation covers the complete writer, Environment,
service registration and source-evidence basis. The job token cannot
read administrative Environment, invitation, team or variable APIs. Runtime
freshness therefore reads protected Governance plus accessible repository,
protected-branch and collaborator metadata; current-run approval, deployment
and the Environment sentinel establish the publisher boundary. Failed reads
block; no administrative-token fallback or empty-inventory substitution is
allowed. Changes outside this bounded runtime visibility still require the
owner to disable Governance and renew its configuration attestation.

The one-shot HTTPS transport uses CPython 3.14.3 with OpenSSL 3.5.5
(27 January 2026), verified TLS 1.2 or later and a 30-second socket timeout.
The exact version/profile check applies to the actual registry transport;
HTTP redirects, proxies and request retries are unavailable. Each actual
publication binds and checks this profile, with independent destination
readback/consumer audit; no separate duplicate/race execution qualifies it.

## Python Governance v2 Migration

The disabled implementation replaces v1 atomically across the strict
parser, constructors, serialized consumers, tests and both protected Governance
files. The v2 schema retains the existing exact top-level fields except
`native-acceptance`, which is removed, not set to a synthetic passing value.
The fields are `schema`, `publisher`, `accepted-operator`, `live_enabled`,
`state`, `operation-profile-digest`, `inspected-at`, `expires-at`,
`configuration` and `source-evidence-revision`. No native generation, suite,
passed flag or substitute platform-proof record is accepted. Reject v1, hybrid,
missing or extra fields at the new runtime boundary; historical bytes remain
bound to their original reader and revision.

Blocked v2 retains the exact destination publisher/operator/profile constants,
`state: blocked`, `live_enabled: false` and null configuration, source revision
and timestamps. Ready v2 requires `state: ready`, `live_enabled: true`, the
unchanged strict configuration tuple/attestation, protected source provenance
and a 40-hex `source-evidence-revision` pinning the accepted `WD-PY-006`, its
owner reliance decision and source findings. Inspection/expiry remains a
positive UTC interval of at most 90 days; existing observed-at, current-main
path continuity, freshness and mutation-boundary checks remain mandatory.
The configuration object keeps its existing closed fields and checks for the
actual Environment identity, sole reviewer/writer, no bypass, protected-main
selection, sentinel, no secrets and reviewed project-bound registration.
Nothing in v2 infers configuration from a successful upload or source research.

Migration must first ship both destinations blocked with no authentication,
registry read, publisher registration or publication. Both protected files
use the blocked v2 form. After implementation delivery, a
separately authorized configuration/admission change may make one destination
ready from reviewed configuration and the accepted dependency basis. It does
not require a completed publication before the first authorized publication.
That later Attempt and its independent audit establish integration completion,
not admission by circular evidence. TestPyPI admission does not enable PyPI.
The retained bootstrap/native registrations are not the normal workflow tuple;
any needed normal registration is a concrete later configuration change.

## Approval and Terminal Contract

The credential-free preparation job persists one Snapshot and Approval Bundle
for the complete freshly built and qualified pair. Its Observation classifies
whole absence, an exact existing subset, whole exact state or blocked state.
Whole exact state has no action. Whole absence or either exact single-file
subset forms one set action with ordinal 0 (wheel) and 1 (sdist), each explicitly
`upload` or `already-present`. Existing-file admission requires actual downloaded
bytes, matching filename/version/metadata/witness and no extra, yanked or unknown
file. The summary presents both original digests, each disposition, destination,
profile, ordered missing-file uploads and possible partial effects.
The destination-bound Environment gates the trusted publisher. After native
current-run approval verification, that job emits the immutable Authorization
before requesting an OIDC assertion or short-lived registry token. The job
permission exists after Environment approval; reviewed control enforces this
later token-acquisition order, not a cryptographic artifact-bound permission.
It verifies all current bindings,
final Governance/configuration and fresh pre-state before the marker. The
approved subset, missing-file plan and preserved target-version index entries
must be unchanged; even newly complete state stops this action rather than
rewriting its approved plan.

The action discriminator is `python-distribution-set`; its closed operations
are ordinal 0 (wheel) and 1 (sdist). One marker binds Authorization, actual
profile and the fresh `pre-state`, replacing the former absence-only field.
Persist and read-validate it before any upload. Retain exact existing files
without POSTs; upload each missing file at most once in ordinal order. A wheel
upload requires definitive success and exact readback before sdist upload.
A pre-existing wheel satisfies that prerequisite through the marker's exact
proof. Every post-upload observation preserves the already verified inventory
and adds only that file. The last actual upload must read back the complete
pair, including the retained file; it may be wheel when sdist already exists.

The existing Publication Result record gains a strict Python destination
variant with two operation entries and final whole-set readback. It refers to
the marker, which resolves both requested artifacts through Authorization and
Snapshot. `already-present` records exact pre-state proof with its digest,
`readback-exact: true` and no upload response or post-upload observation. It
cannot be assigned to a planned upload, and a retained file cannot be reported
as uploaded. `not-attempted` applies to missing files whose upload was not
reached. Both entries remain present even after failure. Final exactness binds
the last actual successful upload's readback digest, not necessarily ordinal 1;
all files must be satisfied and at least one upload must have succeeded for
`published`. Already-present entries alone imply no current-Attempt mutation.
Unknown/missing/extra operation entries fail admission; it cannot be
parsed as an npm or NuGet Result. Existing schema consumers must either
explicitly admit the Python variant or reject it before effects. No generic
list of independent actions or backward-compatible coercion is added.

| Terminal evidence                                                                                       | Set outcome                                                                          |
| ------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| Pre-marker rejection/cancellation                                                                       | No upload authorized or Result; current DAG explains failure.                        |
| Every planned upload definitively succeeds, retained files are exact and final complete readback passes | `published`, verified published state; no unique-insertion claim.                    |
| Wheel fails definitively                                                                                | Failed; sdist is not attempted; mutation facts require supported evidence.           |
| Wheel succeeds; sdist fails or is not attempted                                                         | Failed with retained partial mutation; no compensation.                              |
| Any ambiguous upload or unsuccessful final readback                                                     | Failed, retaining known and possible effects; never exact-satisfied in this Attempt. |
| Durable marker but no durable Result                                                                    | Unknown and possibly mutated, irrespective of subsequent registry reads.             |
| No action and fresh complete exact proof                                                                | Exact-satisfied without Environment, token or publisher.                             |

A successful acknowledgement can be a service no-op. Result mutation facts
record the reached uploads and observed state; they do not attribute unique
creation or count new insertions. Preserve failure/ambiguity classification and
the marker-before-upload rule even when replay is possible.

Result/marker/null remains one scalar terminal reference. Rerun/other-Attempt
records, malformed references and missing publisher outputs fail admission;
Finalizer never searches artifacts or synthesizes missing records. After a
failure, a new separately permitted dispatch starts a new build and observation;
an exact existing subset may now be completed under `WD-PY-005`. Rebuild and
qualify both formats from the same admitted target/version and frozen inputs;
never adopt old artifacts, Approval, Evidence or a prior run's success claim.
A recovered success belongs only to the new Attempt. Changed inputs or bytes
still block at comparison; recovery adds no target/control override, old-target
eligibility or same-Attempt retry.

The current workflow entry, protected Environments, publisher registrations,
Governance and per-file transport profile stay unchanged. Producers, strict
readers, summary, marker, Result audit, CLI and Finalizer consumers change
atomically at the same revision. Historical evidence retains its original
source revision; do not coerce old absence-only markers into recovery proofs.
No workflow dispatch, OIDC or upload is authorized by implementation delivery.

## Temporary Hosted Recovery Proof

The separately authorized [hosted recovery protocol](./validation/python-hosted-recovery.md)
proves wheel-present/sdist-missing recovery using the existing workflow and
publisher. It adds only a temporary manual `recovery-proof` input with choices
`none` (default) and `stop-after-wheel`. The protected workflow projects the
actual GitHub input into `WDV3_PYTHON_PROOF`. The CLI rejects unknown values
and rejects the stop mode outside TestPyPI Live on the existing current-run
protected-main entry. PR CI cannot select it. This changes neither frozen
build inputs nor distribution witness bytes.

The publication Snapshot adds a required strict `proof-mode` field with those
two values. Normal mode retains existing behavior. Stop mode requires whole
absence and TestPyPI; exact subset/whole state cannot seed a new experiment.
The deterministic Approval summary discloses intentional stopping after the
wheel and a failed partial publication. Bundle, Authorization and marker bind
this selection through their existing Snapshot references. Preparation takes
the actual input; before authorization, token acquisition, marker creation and
execution, the CLI requires the bound mode to equal that current input.
All strict producers, readers and offline auditors move together. Historical
records retain their original reader/revision; no old proof is coerced.

After actual wheel HTTP success and exact native readback, the executor in
stop mode returns the ordinary failed Result before calling sdist upload:
wheel `succeeded` with original response/observation, sdist `not-attempted`,
no final exact pair and retained mutation. Persist Result and expose its scalar
terminal before Finalizer emits the failed Outcome. Do not raise an artificial
HTTP failure, cancel the job, discard evidence or mark interruption successful.
Earlier transport/readback/persistence failure retains ordinary conservative
semantics. The normal mode of the next independent Attempt uses the existing
recovery executor without a fault or old-artifact adoption.

Validate input/domain rejection before credentials, immutable mode/summary
binding and tamper rejection, actual first-file success with no second POST,
truthful failed terminal replay, and a fresh normal recovery with one missing
POST. Preserve normal absent, exact-subset and whole-exact paths. Remove the
temporary input, mode field and stop branch after accepted proof; retained
evidence remains replayable at its pinned source. This is validation tooling,
not a new release destination, generic fault framework or retry contract.

The external proof operator separates idempotent evidence reads from one-shot
publication effects. Its persisted read scheduler and resumable immutable
captures follow the [protocol](./validation/python-hosted-recovery.md#finite-campaign-and-autonomous-continuation).
A narrow GET-only continuation may complete a terminal audit across an operator
correction; it binds the new caller separately from the pinned historical
publication reader and never inherits mutation methods. Each Attempt retains
its own immutable protocol and callers for historical closure. The
[continuation contract](./validation/python-hosted-recovery.md#read-only-continuation-across-a-protected-operator-correction)
owns admission, evidence preservation and successor conditions. These external
tooling changes do not alter the runtime transport profile or distribution
bytes.

## First-Project Bootstrap

`WD-PY-009` has a distinct manual workflow and protected request, initially null.
The [bootstrap protocol](./validation/python-bootstrap.md) owns exact resources,
source/artifact provenance, pending-publisher and initial-404 boundaries,
immutable authority/marker transport, finite requests, failure semantics and
independent project-control audit. Independently audited resource facts after
partial bootstrap may satisfy only the existing-project prerequisite for
normal admission; complete pair and clean consumers remain necessary to claim
bootstrap completion. The failed version remains untouched. Normal Release
builds and qualifies its own pair and obtains its own Approval and authority.
The [Delivery Wave](../../../../../docs/delivery-wave.md) determines current
work authorization; no external operation is authorized here.

## Evidence and Delivery Gates

The [retired native protocol](./validation/python-native-acceptance.md) retains
the failed run's interpretation and historical reader boundary. It is not an
operational prerequisite. Trusting the platform does not waive V3 validation
or the actual two-destination completion journey.

Implementation validation covers these distinct claims before normal admission:

- Observation scenarios: immediate exactness, pending then exact, all six
  pending, spacing/deadline boundaries and scheduler overshoot; first-project
  pending 404 versus terminal empty HTTP 200, and existing-project terminal 404; unchanged prior entries versus drift; failed
  download prevents later mutation. No pending read downloads files.
- Evidence contracts: full response/timing retention, failure interruption,
  altered/missing/reordered/excess traces, changed policy/profile and offline
  replay without requests or sleeps. Bootstrap's independent UTC deadline
  cannot renew.

- Scenario tests: complete two-file success; wheel failure prevents sdist;
  exact wheel-only and sdist-only pre-state uploads only the missing file;
  extra/yanked/conflicting/unverified partial state blocks; second-file failure and
  ambiguous response stop; HTTP-200 identical replay still requires exact
  readback; rejected duplicate cannot become skip-existing success; zero-action
  exactness; fresh state drift; marker or
  Result persistence loss; cancellation; independent Buddy/Official lineage.
  Include successful CI qualification with a preserved valid local-version
  projection and rejection of that same projection at Release Live admission.
- Strict contract tests: target/version/source closure, two-artifact ordering,
  witness and native identity, authority/profile/audience/Environment mismatch,
  cross-run/purpose/schema rejection, v1/hybrid Governance rejection, blocked
  v2 and independently configured ready v2, and one-shot mutation bounds. Mocked
  responses prove only application behavior.
- Real local integration: full-history NBGV projection, static Hatch metadata,
  original wheel/sdist contents, isolated clean consumers and reproducible
  bytes for the same target and frozen build inputs. Join an initial controlled
  partial failure to a new independently built/qualified Attempt; prove only the
  missing file is uploaded, fresh complete readback passes, strict Result and
  Finalizer replay agree, and the old failure is unchanged. Also cover sdist-only
  state, zero-action complete state, pre-marker drift, rejected/ambiguous upload,
  retained-file corruption and forged existing-file evidence. These local tests
  do not claim a real hosted recovery experiment. Any reproducibility gap
  blocks this slice; sealed-artifact resume is outside scope.
- Reviewed destination-specific configuration/admission: project ownership,
  exact normal-workflow OIDC tuple, Environment and writer/reviewer controls,
  current profile and accepted source-evidence revision. Preserve the stated
  attestation/runtime visibility limits. No duplicate/race probes or synthetic
  native evidence are required.
- Separately authorized TestPyPI publication, then PyPI publication: audit each
  current Attempt's target/version, original artifacts, Qualification, Snapshot,
  Approval/Authorization, terminal evidence/Outcome, destination downloads and
  consumers. Retain evidence outside the registry. Failed consumer audit blocks
  completion even when upload mutation succeeded.

No registry operation is authorized by this LLD. Later bounded requests supply
concrete coordinates/counts and permission. Local passing tests cannot replace
either destination's actual publication/consumer evidence. Platform reliance is
the `WD-PY-006` basis; no cross-file transaction or unlimited retention is
claimed. Review mutable source findings again when choosing the implementation
profile and at the implementation-Wave gate.
