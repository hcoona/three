# TestPyPI First-Project Bootstrap Protocol

## Purpose and Authority

The V3 maintainer maintains this protocol for implementers, the smoke operator
and independent auditors. It realizes `WD-PY-009`; the existing native protocol
continues to require an already controlled project and a complete HTTP-200
initial index. Bootstrap is a separate prerequisite operation, not native
admission or a normal-Live Attempt. Its evidence cannot satisfy either gate.
Current work authorization is determined by the
[Wave](../../../../../../docs/delivery-wave.md). Configuration and execution
need separate concrete owner authorization.

The only destination is TestPyPI project `hcoona-release-smoke-python`, using
`workflow-delivery-v3-bootstrap-python.yml` in `hcoona/three` (1102295886),
Environment `workflow-delivery-v3-python-testpypi`, and audience `testpypi`.
GitHub operator/reviewer `hcoona` (712433) and TestPyPI username `Backspace7980`
are distinct service identities reported by the same owner. Username disclosure
establishes neither account control nor project ownership. The prospective
publisher is project-name-bound and pending; it is not ownership evidence.
No wildcard, static token, other project or PyPI destination is supported.

## Configuration and Request Prerequisites

A later configuration request must specify the exact pending publisher tuple
above and Environment settings: sole reviewer `hcoona`, self-review permitted,
no administrator bypass, zero wait, protected main only, and sentinel
`WDV3_APPROVAL_ENVIRONMENT_MARKER=workflow-delivery-v3-python-testpypi/v1`.
It must bound each proposed configuration write and readback. It must obtain
the owner's explicit sole account-control and smoke-only/no-production-use
confirmation. The user performs private website steps; no password, token,
recovery code or authenticated browser session is collected by the agent.

Authorized configuration evidence must retain sanitized account-side pending
publisher fields and complete GitHub writer/Environment settings with the actual
Environment ID. An independent reviewer compares that evidence to the request.
Where no supported public/runtime account inventory exists, reviewed operator
attestation is the evidence basis and its limitations remain explicit. Do not
claim independent direct service observation from an owner-provided statement.
Incomplete configuration prevents formation of an executable request.

One prospective request is protected at
`.github/workflow-delivery/bootstrap/python-request.json`, initially `null`.
The closed request binds schema `workflow-delivery/v3/python-bootstrap-request`,
fresh 32-hex generation, exact account/project/registry, profile digest, one
protected-main ancestor commit and its public prerelease version, original wheel
and sdist SHA-256 values, Environment ID/name/sentinel, and HTTPS URL/digest
references for owner authorization and independently reviewed configuration.
It does not assert pre-existing project ownership. No placeholder or pending
configuration is an executable request. A later protected change may populate
it only under the separate operational grant; preparation leaves it null.

Manual dispatch binds the exact protected tooling SHA and request digest.
Only the accepted actor/repository, protected `refs/heads/main`, unchanged
GitHub workflow/checkout SHA, ancestor target and run attempt one are admitted.
A generation is consumed by its one dispatch, including cancellation or failure;
no workflow rerun, replacement dispatch or refill is permitted. The operator
records the actual run in the work carrier and retires the slot before another
separately authorized request. Workflow concurrency shares the physical
TestPyPI project key with native and normal publication and does not cancel an
in-progress writer. This does not constrain a malicious accepted writer.

## Build and Immutable Authority

Credential-free preparation obtains full-history Provider facts and the unchanged
`nbgv-python` public prerelease projection. It reuses static staging, the pinned
Build Adapter, native archive inspection and separate clean wheel/sdist consumers.
The witness purpose is `destination-bootstrap`; no normal Release or native
Evidence is adopted. Retain original Provider facts, source manifest, Build
commands/resolved tools, staged manifest digest, witness and both consumer
results. The hosted build must reproduce both protected original file hashes.

Preparation persists one immutable bundle and exposes exact returned artifact
ID, raw service digest, logical payload digest and current-run URL. The reviewer
summary identifies source/version, both filenames/hashes, project/account,
publisher tuple, request/tooling identities, finite effects and partial-failure
risk before the Environment wait. Only its publisher job has `id-token: write`;
Build, consumers and audit have no registry capability and no target build code
runs in the publisher.

After Environment approval, authorization validates the immutable prepare bundle,
current-run owner approval/deployment and sentinel using at most eight GitHub
proof reads. Failed/truncated inventories block; no administrative credential
fallback is available. It reads the exact public JSON Simple Index once and
requires an actual HTTP 404. This is a conservative bootstrap entry condition,
not proof of name reservation, historical availability or ownership. HTTP 200,
any other status, transport failure, redirect or missing evidence blocks.

Persist the complete bootstrap authorization, including retained original proof
responses and the initial 404, as an immutable artifact and read it back by
returned ID before forming a mutation-may-start marker. The marker closes the
request, current run, exact prepare/authorization references and actual profile.
Persist and read-validate that marker before requesting OIDC or uploading.
The same pinned immutable-artifact mechanism supplies these explicit DAG edges;
listing artifacts or finding a similar name never reconstructs authority.

## Fixed Execution and Budgets

The publisher revalidates all bindings and actual transport profile, repeats
the bounded current-run approval/deployment/sentinel proof, and requires a
second actual HTTP 404 immediately before token acquisition. Authorization
records its trusted control runner's UTC creation instant. Its publisher
request-admission deadline is exactly that instant plus 600 seconds. Marker
formation and every publisher external request require current UTC in the
half-open interval from creation through, but excluding, that deadline.
Future-dated or expired authority blocks; artifact waits consume this same
window. A local monotonic guard may shorten it but cannot reset or extend it,
and monotonic values from different processes/runners are never compared.
Runner UTC is part of the accepted control trust base, not an external clock
attestation or new synchronization service.

One actual Actions assertion and one TestPyPI mint exchange are memory-only.
No credential is persisted or logged. The pinned 30-second socket timeout bounds
an already admitted in-flight request; expiry forbids new publisher requests,
not completion of that request or credential-free evidence archival. P4 is a
separate mandatory credential-free audit with its fixed read counts; it does
not renew the publisher window. Only P2/P3 permit the
[bounded post-upload observation](../hcoona-release-smoke-python-lld.md#bounded-post-upload-observation)
phase. No transport/upload retry, pagination expansion, other polling or
mutating redirect is admitted.

| Step | Required observation and effect                                                                                                                       |
| ---- | ----------------------------------------------------------------------------------------------------------------------------------------------------- |
| P0   | Authorization: exact project index returns HTTP 404; persist raw response.                                                                            |
| P1   | After durable marker, repeat proof and exact index HTTP 404; one OIDC assertion and one mint exchange.                                                |
| U1   | Upload the original wheel once; require definitive HTTP 200.                                                                                          |
| P2   | At most six index reads; only HTTP 404 may remain pending; HTTP 200 empty inventory fails; exact wheel inventory then original byte/witness download. |
| U2   | Upload the original sdist once, only after P2 passes; require definitive HTTP 200.                                                                    |
| P3   | At most six index reads; unchanged verified wheel inventory may remain pending; exact pair then both original byte/witness downloads.                 |
| P4   | Credential-free audit: one fresh HTTP-200 index and both fresh downloads; require the same exact pair and run two new clean consumers.                |

The entire generation permits at most two upload POSTs, fifteen index GETs, five
file GETs, sixteen GitHub approval-proof GETs (eight per proof), one OIDC
assertion request and one token-exchange POST. No unused allowance is reusable.
Each approval inventory is one page below 100 entries; at most five matching
Environment deployment candidates permit at most five status reads. All raw
responses are retained with status, content type, actual URL and digest, subject
to credential screening. Use the existing profile's HTTPS origins, response-size
limits, TLS/client pins and multipart mapping; public downloads carry no token.
All file entries across the whole project must equal the expected inventory;
extra, malformed, yanked, foreign or conflicting files stop progress.

Immutable evidence transport uses at most five original artifact creations:
prepare, authorization, marker, execution result and audit. Each is created once
without overwrite and explicitly downloaded/read-validated before dependent use;
the final audit is read-validated by the independent operator. Artifact service
internal transfer requests follow the pinned artifact action contract and are
not registry/proof reads. The workflow uses only explicit returned IDs/digests,
45-day retention and no name-based discovery. Execution failure preserves surviving
sanitized evidence through the single result upload; failed persistence grants no
registry retry. The operational request separately bounds operator configuration,
run/check inspection and artifact retrieval; these are not hidden extra allowance
inside the hosted generation.

## Failure, Audit and Later Admission

An eligible pending P2/P3 observation is not a terminal failure. Every other
non-success, ambiguous upload, exhausted/failed readback, name collision, cancellation
or missing evidence stops subsequent uploads. P2 failure prevents U2. A failure
cannot become success through later exact state. Preserve the definitive or
unknown per-file effects; no automatic partial completion, rollback, deletion,
replacement version, static-token fallback or cleanup is allowed. A durable
marker without a durable execution result means unknown/possibly-mutated.

The result retains the exact original responses, bindings and fixed step
sequence, including every pending P2/P3 response and the LLD timing trace;
the auditor replays them rather than trusting a claimed verdict. Count, spacing,
upload-completion anchor and both monotonic/UTC deadlines are enforced in live
admission and replay. Missing intermediate responses cannot be replaced by the
final exact index. The fifteen-index budget is shared across P0 through P4.
Success requires both definitive uploads, P2/P3 exactness and a passing P4
consumer audit. A successful upload result with failed P4 is not bootstrap
completion. Keep original and downloaded bytes, raw response metadata and
current-run artifact references outside the registry before Actions expiration.
No credentials or private account pages are retained in public evidence.

Independent provenance review binds the artifacts to the actual protected run,
request and current Environment approval, checks the fresh destination evidence,
and separately reviews the project's actual owner and converted publisher state
under authorized account-side evidence collection. Public file presence alone
cannot establish owner `Backspace7980`. Unexpected owner/publisher state or
unavailable evidence blocks all dependent work even if both files are visible.
The bootstrap publisher remains a configured capability until explicitly changed;
no removal is inferred. Native and normal workflow registrations require their
own later configuration grants.

Bootstrap completion still requires the full audit above. A failed bootstrap
may instead supply independently reviewed actual ownership/configuration only
as the existing-project resource prerequisite under `WD-PY-009`. Preserve its
failed verdict and partial version indefinitely without refill, deletion or
another absent-project bootstrap. That narrower resource audit must close
actual owner and converted publisher facts from retained effects and authorized
account-side evidence; missing or unexpected facts block continuation.

A separately authorized native suite on two other fresh unchanged NBGV versions
must independently close its native publisher registration, current trust and
configuration, exact fixtures and complete initial HTTP-200 inventory. It
preserves every pre-existing entry, including the partial bootstrap version,
and proves its own complete pairs, duplicate/race behavior and clean consumers. Both Governance destinations remain `live_enabled: false`, and
both native slots remain null throughout preparation/bootstrap. Bootstrap grants
no native admission, activation or normal publication. Subsequent TestPyPI Buddy
and independent PyPI Official Attempts retain their own complete qualification,
Approval, native evidence and publication grants. TestPyPI pruning and other
accepted availability limits remain; later absence does not authorize restoration.

## Tooling Interface

`three_workflow_delivery_v3.acceptance.python_bootstrap` implements the closed
bootstrap phases. The manual workflow at
`.github/workflows/workflow-delivery-v3-bootstrap-python.yml` separates prepare,
publisher and audit jobs. The protected bootstrap request admits only a
separately authorized operation; installing this tooling alone does not configure
or execute a publisher.

After local locked dependency and NBGV preparation, the operator can build a
prospective original pair from a protected-main ancestor without registry access:

```bash
uv run --no-sync --package three-workflow-delivery-v3 \
  python -m three_workflow_delivery_v3.acceptance.python_bootstrap build-fixtures \
  --root '<full-history-checkout>' --target '<protected-main-ancestor-sha>' \
  --output '<new-external-fixture-bundle>'
```

Use a configured POSIX environment such as WSL on Windows. The emitted
source/version/original-digest summary supplies the later operational proposal;
it is not an executable request or proof of account readiness. Existing output
paths reject rather than overwrite retained evidence.

Hosted `prepare`, `authorize`, `marker`, `execute` and `audit` require exact
protected request/tooling identities. Bundle input flags `--prepared`,
`--authorization`, `--marker` and `--result` bind corresponding original artifact
references supplied by the workflow. `archive` and `digest` seal surviving
sanitized bytes for the five original artifact creations. Authorize and execute
retain separate subdirectories of the single result archive, so authorization
failure evidence can survive even when mutation never starts. Audit retains its
surviving evidence if fresh readback or either consumer fails; that artifact is
not a successful bootstrap verdict.

The registry-offline `replay` command reads all five original downloaded bundles
and their explicit reference document through `--references`. It replays raw
proof and registry observations, timing, lineage and retained consumer evidence.
It grants no operational capability and does not freshly observe ownership or
registry state. `--help` gives the exact CLI inputs; actual dispatch and operator
artifact retrieval remain separately bounded by the later operational request.

## Validation Before Operations

Local tests exercise the complete successful state sequence and ensure wrong
account/workflow/Environment, null or mismatched request, stale main, rerun,
nonpublic version, changed artifacts/provenance, missing approval/persistence,
future-dated or expired authority and non-404 initial state prevent capability.
Cover delayed artifact handoffs and reject a reset or extension of the shared
publisher deadline. Prove wheel
failure/P2 mismatch prevents sdist, partial/ambiguous effects stay failed, budgets
stop before excess requests, and audit rejects forged lineage or incomplete raw
responses. Real local Provider/Build/consumer integration establishes deterministic
original bytes; it does not establish service behavior. Retain independent
review of protected tooling and actual later operational evidence separately.
