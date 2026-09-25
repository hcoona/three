# Python Destination-Native Acceptance Protocol

## Authority and Readiness

This protocol realizes `WD-PY-006` and `WD-PY-008` in the
[requirements](../requirements.md#python-smoke-slice) and the
[Python LLD](../hcoona-release-smoke-python-lld.md#evidence-and-delivery-gates).
The V3 maintainer produces and maintains it. Implementers, the operator and
independent native auditors consume the fixed schedule and evidence contract;
the LLD routes here rather than duplicating an executable operation protocol.
Protocol and [tooling preparation](./python-native-readiness.md) are
protected-delivered through PRs #854 and #855. Future configuration, native
execution/admission and publication require separate
[Wave](../../../../../../docs/delivery-wave.md) authorization and domain gates.
Neither this protocol nor its tools grants execution.
Both normal-Live Governance files remain disabled. No native observation is
claimed by this record.

TestPyPI precedes PyPI, which requires its own request, fixtures, run, Environment
approval and independent audit. The initial protocol deliberately uses only
`hcoona-release-smoke-python` at the chosen registry. It adds two explicitly
approved smoke-only prereleases to that project. The existing adapter, package
inspection and consumers all bind that exact name; no disposable-project
parameter or credential alias is introduced. Operator control, no production
consumers and acceptance of these retained files must be confirmed before an
operational request. If that tuple cannot be authorized, this protocol is
unavailable; changing to a disposable project needs a reviewed protocol change
and cannot admit the smoke tuple implicitly.

If the exact project does not yet exist under the operator's control, the
separate [bootstrap protocol](python-bootstrap.md) must first establish its
independently audited ownership/configuration. Pending registration and HTTP 404
remain insufficient for this native protocol. Its two versions must differ from
the retained bootstrap version; no bootstrap evidence is native admission.
Under `WD-PY-009`, independently reviewed ownership/configuration after a failed
partial bootstrap may satisfy only this resource prerequisite. Bootstrap
completion is not claimed. Keep its partial version untouched and retain it in
C0; every subsequent inventory must preserve those existing entries. Native
publisher registration, current trust/configuration, complete fresh pairs and
clean consumers remain independently required.

## Prospective Request and Fixture Closure

Each destination has a protected native-request slot, initially null. A later
separately authorized protected change fills one slot with a fresh generation,
exact destination/profile, two distinct protected-main ancestor targets A and B,
expected distinct public NBGV versions, Environment ID/name/sentinel, and the
owner's bounded request and configuration/ownership evidence references and
hashes. Include the eight deterministic fixture digests in that protected
request and require hosted preparation to match them. No wildcard, pending
publisher or placeholder establishes ownership.
The dispatch supplies that slot's canonical digest and exact protected tooling
SHA. The hosted entry rejects a null/mismatched slot, non-owner actor, foreign
repository, non-main/unprotected ref, moving main or attempt other than one.
A consumed generation is not reusable; the operator records its actual run and
retires the slot through protected delivery before considering another request.
A flag or populated slot records authorization; it cannot create owner consent.

A credential-free preparation job evaluates each exact target through the
existing full-history Python Provider and freezes the unmodified public NBGV
projection. Native preparation uses `destination-acceptance` provenance, never
CI or normal-Live Evidence. The Provider selects the public-main ref in its
isolated clone for this purpose, as it already does for Live; no version is
invented, rewritten or derived from the generation/run. Build reuses the
existing static staged Hatch adapter and pinned backend constraints.

For each target, retain original wheel and sdist, raw Provider facts, witness,
source manifest, producer tool versions and build commands. Construct a second
valid candidate by changing only ZIP compression representation for the wheel
and gzip header timestamp for the sdist. Preserve every extracted member and
its bytes, metadata, version, filename, RECORD and witness. Require the original
and comparison bytes to differ. Inspect all eight files through the actual
adapter and run both clean consumers on both representations at both targets
before any native capability. Comparison archives are acceptance fixtures only;
normal publication continues to upload its original qualified artifacts.

Exact native filenames are derived from each frozen version V:
`hcoona_release_smoke_python-V-py3-none-any.whl` and
`hcoona_release_smoke_python-V.tar.gz`. Both versions and all four names must be
fresh in the complete initial inventory. Historical absence or availability is
not inferred from that read. The prepared immutable artifact binds the protected
request, tooling SHA, current run, all eight file digests and qualification.
The Environment summary exposes these inputs and finite effects before approval.

## Fixed Schedule and Budgets

One separately authorized manual dispatch runs one destination generation.
Prepare and audit are credential-free. Only the protected Environment-gated
probe job has `id-token: write`; it executes no target build or product code.
It verifies current run, exact native owner approval, Environment deployment and
sentinel before one OIDC assertion and one registry token exchange. Both are
memory-only and use the actual pinned transport. No static token is accepted.

A capture obtains a complete JSON Simple Index response and downloads every
present scenario file. C1/C2/C7/C8 alone may use the
[bounded post-upload observation](../hcoona-release-smoke-python-lld.md#bounded-post-upload-observation)
phase, with at most six index reads each; only the unchanged verified previous
inventory missing the one expected addition is pending. No files are downloaded
until the exact expected index. C0 and C3-C6 remain single reads. The same retained response supplies the two existing
version readers; this does not claim an atomic service snapshot. The full file
inventory, including unrelated versions, is retained and compared. Only the
expected named additions are permitted; all other file entries must remain
identical. Index-level serial/timestamp metadata is retained as raw evidence but
is not a file mutation. No missing page, malformed/oversized index, duplicate
filename, foreign filename, yanked scenario file, failed download or hash
mismatch is filtered into an empty or exact state.

| Step | Native operation                                          | Required observation before advancing                                      |
| ---- | --------------------------------------------------------- | -------------------------------------------------------------------------- |
| C0   | Capture initial state                                     | A and B absent; complete inventory retained                                |
| 1    | Upload A original wheel once                              | HTTP 200; C1 contains only that new file with exact bytes                  |
| 2    | Upload A original sdist once                              | HTTP 200; C2 contains the exact A pair                                     |
| 3    | Upload identical A wheel once                             | Definitive filename-duplicate rejection; C3 equals C2                      |
| 4    | Upload identical A sdist once                             | Definitive filename-duplicate rejection; C4 equals C3                      |
| 5    | Upload differing A wheel once                             | Definitive filename-duplicate rejection; C5 equals C4                      |
| 6    | Upload differing A sdist once                             | Definitive filename-duplicate rejection; C6 equals C5                      |
| 7    | Start B original/comparison wheel concurrently, once each | One HTTP 200 and one duplicate rejection; C7 adds exactly the winner bytes |
| 8    | Start B original/comparison sdist concurrently, once each | One HTTP 200 and one duplicate rejection; C8 adds exactly the winner bytes |

The two racing calls use a start barrier and separate actual HTTP connections.
Either candidate may win, independently for each format. Preserve their actual
start/finish intervals; overlapping client intervals establish a bounded
competing request observation, not proof of internal server scheduling.
No serialized pair is accepted as the requested competition. Both calls are
already admitted as one finite step; a failure cannot cancel an already sent
peer. Join both results and stop every later step on failure or ambiguity. The winning
response completion anchors its 60-second observation admission window; joining
the peer cannot renew it. The unchanged outer probe deadline can stop earlier.

A duplicate verdict needs HTTP 400 plus the retained service response explicitly
identifying an existing/reused filename (including the PyPI filename-reuse help
reference or the returned "File already exists" message). Other 4xx/5xx,
redirect, permission/metadata rejection or transport error is not duplicate
proof. The exact raw body remains subject to independent interpretation; this
recognizer is a conservative application gate, not a universal service promise.
Unexpected response wording stops the generation for review, without retry.

The cumulative maximum is 10 upload POSTs, one OIDC GET, one token-exchange POST,
29 index GETs and eighteen exact-file GETs (47 public registry reads).
There are four possible newly stored files across two versions; no deletion,
tag, access or configuration operation is part of the suite. Native HTTP
concurrency is one except for exactly two calls at each competing step. There
are no transport retries, redirects, token refresh, polling outside the four
eligible post-success captures, or partial completion. Per-request bytes/timeouts reuse the accepted profile (30-second
socket timeout, 2 MiB index, 4096 entries, 8 MiB file, 64 KiB upload/token reply).
A monotonic ten-minute probe deadline forbids new requests after expiry; an
in-flight request retains its socket bound. The hosted probe job has a
15-minute ceiling. A missing response after that ceiling remains ambiguous.

Current-run GitHub proof is bounded separately: one run read, one approval-history
read, one deployment listing (at most five entries), and one status listing per
returned deployment (at most five): eight API GETs maximum, 100 entries per
listing, no pagination or retries; a full page or exceeded cap fails closed.
Artifact upload/download actions and locked dependency preparation are ordinary
hosted transport/setup, not extra registry probes. One current-run prepared
artifact and one probe artifact use the existing `ArtifactReference` fields:
immutable artifact-service ID, service digest and URL, plus exact selected
payload path and logical SHA-256 of the original payload bytes. Consumers check
both digest bindings independently, even when raw `archive: false` transport
makes their values equal, and bind the producer, current run and tooling SHA.
Each bundle manifest binds every retained raw file's path, byte length and
SHA-256; raw service bytes are never JSON-canonicalized to manufacture equality.
No history or name lookup reconstructs evidence. No dispatch/polling operator is added:
the later authorized operator performs one explicit GitHub dispatch and retains
the exact returned run and complete output artifact references, including both
service and payload digest bindings. No rerun is admissible.

## Stops and Retained Evidence

Before credentials, persist the matched request, actual platform/profile and
native approval proof. Before each admitted upload step, write its exact file
identities and ordinal marker to an exclusive local file, then flush and fsync.
This is local crash consistency, not remote persistence across runner loss.
Record both started contenders before
releasing their barrier. A marker without complete results is possible mutation,
not absence or success. Token/assertion bodies, authorization headers and their
encoded forms never enter retained evidence. Sanitize upload responses against
all in-memory secrets before persisting them; unsafe content stops the suite
and records only a fixed failure category.

Every reached capture retains all original index responses, status/content type,
complete normalized file inventory, exact downloaded scenario bytes and digests.
The LLD phase trace binds each intermediate response to request order, upload
completion, spacing, admission deadline and pending/terminal disposition. Audit
reconstructs these facts from all raw responses, including failure/exhaustion;
final-only capture evidence cannot pass. Previous inventories and successful
race candidates must agree with the fixed schedule and actual upload journal.
Every reached upload retains profile/request/file binding, ordinal, actual
interval, sanitized response body/status/digest or a bounded error category.
Retain unsuccessful/partial data, including a later exact state that does not
convert a failed step into success. The audit directory is new and exclusive;
existing state cannot resume or refill a consumed generation. Final probe
archive/upload is best effort and may be missing after cancellation, runner loss
or artifact transport failure. Any dispatched generation missing required probe
evidence, markers or complete results remains spent and conservatively possibly
mutated, even when no marker survives. Absence of evidence establishes neither
absence of mutation nor success, and grants no retry, rerun, partial completion
or replacement dispatch. Native admission stays blocked; later inspection or
recovery requires its own bounded authorization. No automatic
cleanup of native files occurs. Any later recovery is a separate owner request.

The credential-free audit replays the entire fixed schedule from supplied raw
records and original bytes, checks all captures/deltas, duplicate responses,
winner bytes, request/profile/run bindings and budgets, and runs fresh clean
wheel and sdist consumers for both downloaded final pairs. It retains command
and installed metadata/witness evidence. Local replay may report only a
supplied-fact verdict. Replay performs no destination or OIDC requests; fresh
sdist consumers still use the existing explicit public build-prerequisite index.
The entire consumer command is therefore not a zero-network claim.
An independent auditor must additionally authenticate the
protected revision, real run/attempt, immutable artifacts, Environment approval,
actual configuration and original service observations. A local fake, passing
workflow or self-approved Environment cannot establish independent native
admission by itself. Missing native provenance or consumer proof blocks admission.

Retain every successfully produced prepared/probe/audit artifact and surviving
failed evidence for 45 days in Actions; this does not promise an artifact from
a canceled or lost runner. Copy their exact bodies, immutable references and hashes to durable
operator storage before expiration. Registry retention is not evidence storage.
The native audit is destination/profile/configuration-specific; installation into
normal-Live Governance requires a later protected admission decision matching
the normal workflow's separately configured publisher tuple. A native workflow
registration is not the normal workflow registration.

The [tooling and operator readiness](python-native-readiness.md) records the
preparation interfaces, local evidence limits and concrete request inputs.

## Operator Request and Remaining Resources

Preparation can validate the local protocol/tooling, but account ownership and
actual resource IDs are deliberately unverified. The later TestPyPI request must
supply this concrete checklist, with no executable placeholder defaults:

- Exact owner-controlled `hcoona-release-smoke-python` TestPyPI project and owner
  account; smoke-only/no-production-use confirmation and permission to retain
  the four scenario files. A pending registration alone is insufficient.
- Exact repository `hcoona/three` (1102295886), owner/operator `hcoona` (712433),
  protected-main tooling revision, request digest and fresh generation.
- Two ancestor targets, frozen public versions, all exact filenames and eight
  prepared digests; proof that the native request remains within these bounds.
- Existing or separately provisioned Environment
  `workflow-delivery-v3-python-testpypi`, numeric ID, sentinel, sole required
  reviewer, no bypass, and the reviewed complete configuration evidence.
- TestPyPI project-bound trusted publisher for
  `workflow-delivery-v3-native-python-acceptance.yml`, that exact Environment
  and repository; audience `testpypi`. Normal publication separately needs
  `workflow-delivery-v3-python-smoke.yml` registration.
- Explicit authorization for one native dispatch, one Environment approval,
  one OIDC assertion/exchange, the ten uploads, 47 registry reads and eight
  GitHub proof reads above, with retained partial-state and no-retry acceptance.
- Durable audit storage location and independent auditor; later native
  configuration/admission and each normal publication remain separate decisions.

PyPI requires its own resources, audience `pypi`, Environment
`workflow-delivery-v3-python-pypi`, request, fixtures and evidence after TestPyPI.
Neither slot is populated during preparation. No account inspection, publisher
registration, Environment configuration, credential request, native dispatch,
registry read/upload, activation or remote cleanup is performed by this stage.
